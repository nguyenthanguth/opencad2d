using System;
using System.Collections.Generic;
using System.Linq;
using GeometryHelper.Dxf;
using GeometryHelper.Geometry;
using OpenCAD2D.Remote;

namespace OpenCAD2D
{
    /// <summary>Writing objects: the queued inserts, modifies and deletes of a client, applied as one undo step.</summary>
    public sealed partial class CadRemoteServer
    {
        /// <summary>The name of the undo step a commit makes, as the Undo list and U show it.</summary>
        private const string StepName = "REMOTE";

        private enum StepKind
        {
            Insert,
            Modify,
            Delete,
            AddLayer,
            ModifyLayer,
        }

        private sealed class Step
        {
            public StepKind Kind;
            public string Handle;
            public DxfEntity Entity;
            public CadSpace Space;
            public JsonObject Layer;
        }

        private JsonObject Commit(CadDocument document, JsonObject request, CadRemoteToolRun run = null)
        {
            string text = request.GetString("fragment");
            DxfDatabase fragment = string.IsNullOrEmpty(text) ? null : DxfDatabase.Parse(text);
            var byReference = new Dictionary<string, DxfEntity>(StringComparer.Ordinal);

            if (fragment != null)
            {
                foreach (DxfEntity entity in fragment.ModelSpace)
                {
                    if (!string.IsNullOrEmpty(entity.Handle))
                    {
                        byReference[entity.Handle] = entity;
                    }
                }
            }

            // A command running holds the drawing's undo group open: an edit now would be taken back with the command's Esc.
            CadCommandSession session = _host.SessionOf(document);

            // A run's commits are made in its own command's undo step, which is the one group open, so they are taken back with
            // its Esc: only in the drawing it works in.
            if (run != null && !ReferenceEquals(document, run.Document))
            {
                throw new Refusal(
                    "The run "
                        + run.Id
                        + " of "
                        + run.Name
                        + " works in the drawing "
                        + run.Document.Title
                        + ": a commit into "
                        + document.Title
                        + " would not be part of its undo step."
                );
            }

            if (run == null && (document.IsInGroup || (session != null && session.IsRunning)))
            {
                throw new Refusal(
                    "The drawing "
                        + document.Title
                        + " is busy with the command "
                        + (session?.Current?.Name ?? "running in it")
                        + ": end it, then commit the changes again."
                );
            }

            // Every step is checked before any is applied, so that one that cannot be made changes nothing.
            var steps = new List<Step>();
            var planned = new List<CadEditRecord>();
            var edits = new List<EditStep>();
            var layersAdded = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

            foreach (object item in request.GetArray("operations"))
            {
                var operation = (JsonObject)item;
                string kind = operation.GetString("op");
                string handle = operation.GetString("handle");

                switch (kind)
                {
                    case "addLayer":
                        steps.Add(PlanNewLayer(document, operation.GetObject("layer"), layersAdded));
                        break;
                    case "modifyLayer":
                        steps.Add(PlanLayerLook(document, operation.GetObject("layer"), planned));
                        break;
                    case "insert":
                        DxfEntity added = Sent(byReference, operation);
                        CheckBlock(document, fragment, added);
                        steps.Add(
                            new Step
                            {
                                Kind = StepKind.Insert,
                                Entity = added,
                                Space = SpaceNamed(document, operation.GetString("layout")),
                            }
                        );
                        break;
                    case "modify":
                        Existing(document, handle);
                        DxfEntity changed = Sent(byReference, operation);
                        CheckBlock(document, fragment, changed);
                        steps.Add(
                            new Step
                            {
                                Kind = StepKind.Modify,
                                Handle = handle,
                                Entity = changed,
                            }
                        );
                        planned.Add(new CadEditRecord(CadEditRecordKind.Reshaped, handle, "Remote"));
                        break;
                    case "delete":
                        Existing(document, handle);
                        steps.Add(new Step { Kind = StepKind.Delete, Handle = handle });
                        planned.Add(new CadEditRecord(CadEditRecordKind.Erased, handle, "Remote"));
                        break;
                    case "transform":
                    case "offset":
                        EditStep edit = PlanEdit(document, session, operation);
                        edits.Add(edit);
                        planned.AddRange(edit.Plan.Planned);
                        break;
                    default:
                        throw new Refusal("OpenCAD2D does not know the change " + kind + ".");
                }
            }

            // An object an edit works on cannot be one the same commit erases: the edit would find nothing to write to.
            foreach (EditStep edit in edits)
            {
                foreach (string handle in edit.Handles)
                {
                    if (
                        steps.Exists(s =>
                            s.Kind == StepKind.Delete
                            && string.Equals(s.Handle, handle, StringComparison.OrdinalIgnoreCase)
                        )
                    )
                    {
                        throw new Refusal("The object " + handle + " is erased by the same commit.");
                    }
                }
            }

            var layers = steps.Where(s => s.Kind == StepKind.AddLayer || s.Kind == StepKind.ModifyLayer).ToList();
            steps.RemoveAll(s => s.Kind == StepKind.AddLayer || s.Kind == StepKind.ModifyLayer);
            var inserted = new JsonArray();
            CadEditResult result = document.ApplyEdit(StepName, planned, scope =>
                Apply(document, scope, layers, steps, fragment, inserted, edits)
            );

            if (!result.Succeeded)
            {
                throw new Refusal(result.Message);
            }

            JsonObject answer = Shown(document).Set("handles", inserted);
            return edits.Count == 0 ? answer : Edited(document, answer, edits);
        }

        /// <summary>One edit operation of a commit: the plan, and the handles it works on.</summary>
        private sealed class EditStep
        {
            public CadEditPlan Plan;
            public IReadOnlyList<string> Handles;
        }

        /// <summary>
        /// Plans the edit operation <c>transform</c> or <c>offset</c> of a commit, checking it as <see cref="CadEditor"/> checks
        /// its call, so that a refusal changes nothing: the objects are the drawing's, not erased, in a space and not on a locked
        /// layer, and the numbers are those the call takes. Its writing runs with the commit's, after the queued changes.
        /// </summary>
        private static EditStep PlanEdit(CadDocument document, CadCommandSession session, JsonObject operation)
        {
            string kind = operation.GetString("op");
            var handles = new List<string>();

            List<object> named =
                kind == "offset" ? new List<object> { operation.GetString("handle") } : operation.GetArray("handles");

            foreach (object item in named)
            {
                DxfEntity entity = item is string handle ? document.FindEntity(handle) : null;

                if (entity == null)
                {
                    throw new Refusal("The drawing " + document.Title + " has no object " + (item as string) + ".");
                }

                string refused = CadEdits.Unfit(document, entity);

                if (refused != null)
                {
                    throw new Refusal(refused);
                }

                if (!handles.Contains(entity.Handle, StringComparer.OrdinalIgnoreCase))
                {
                    handles.Add(entity.Handle);
                }
            }

            GeoPoint2 at = new GeoPoint2(operation.GetDouble("x"), operation.GetDouble("y"));
            CadEditPlan plan = Edit(() =>
            {
                switch (kind == "offset" ? "offset" : operation.GetString("how"))
                {
                    case "move":
                        return CadEdits.Move(
                            handles,
                            new GeoVector2(operation.GetDouble("dx"), operation.GetDouble("dy"))
                        );
                    case "copy":
                        return CadEdits.Copy(
                            handles,
                            new GeoVector2(operation.GetDouble("dx"), operation.GetDouble("dy"))
                        );
                    case "rotate":
                        return CadEdits.Rotate(handles, at, operation.GetDouble("angle"));
                    case "scale":
                        return CadEdits.Scale(handles, at, operation.GetDouble("factor"));
                    case "mirror":
                        return CadEdits.Mirror(
                            session,
                            handles,
                            at,
                            new GeoPoint2(operation.GetDouble("x2"), operation.GetDouble("y2")),
                            operation.GetBool("eraseSource")
                        );
                    case "offset":
                        return CadEdits.OffsetBy(
                            session,
                            document.FindEntity(handles[0]) as DxfCurve ?? throw new Refusal(CadEdits.CannotOffset),
                            operation.GetDouble("distance"),
                            at
                        );
                    default:
                        throw new Refusal(
                            "OpenCAD2D does not know the transform "
                                + operation.GetString("how")
                                + ": it is of an older version."
                        );
                }
            });
            return new EditStep { Plan = plan, Handles = handles };
        }

        /// <summary>
        /// Adds to the answer of a commit what its edit operations gave: for each, in order, the handles of what it gives back
        /// (<c>edited</c>), and the objects of all of them as <c>get</c> answers (<c>objects</c>).
        /// </summary>
        private static JsonObject Edited(CadDocument document, JsonObject answer, List<EditStep> edits)
        {
            var lists = new JsonArray();
            var entities = new List<DxfEntity>();

            foreach (EditStep edit in edits)
            {
                var list = new JsonArray();

                foreach (string handle in edit.Plan.Result)
                {
                    list.Add(handle);
                    DxfEntity entity = document.FindEntity(handle);

                    if (entity != null)
                    {
                        entities.Add(entity);
                    }
                }

                lists.Add(list);
            }

            return answer.Set("edited", lists).Set("objects", Pack(document, entities));
        }

        private static DxfEntity Sent(Dictionary<string, DxfEntity> byReference, JsonObject operation)
        {
            string reference = operation.GetString("ref");

            if (reference == null || !byReference.TryGetValue(reference, out DxfEntity entity))
            {
                throw new Refusal("A change names an object that did not come with it.");
            }

            return entity;
        }

        private static DxfEntity Existing(CadDocument document, string handle)
        {
            DxfEntity entity = handle == null ? null : document.FindEntity(handle);

            if (entity == null || entity.IsErased)
            {
                throw new Refusal("The drawing " + document.Title + " has no object " + handle + ".");
            }

            if (document.FindSpace(handle) == null)
            {
                throw new Refusal(
                    "The object "
                        + handle
                        + " is inside a block of "
                        + document.Title
                        + ", not in a space, and is not changed from outside."
                );
            }

            return entity;
        }

        private static CadSpace SpaceNamed(CadDocument document, string layout)
        {
            if (string.IsNullOrEmpty(layout))
            {
                return document.ModelSpace;
            }

            CadSpace space = document.Spaces.FirstOrDefault(s =>
                !s.IsBlockEditor && string.Equals(s.Name, layout, StringComparison.OrdinalIgnoreCase)
            );
            return space ?? throw new Refusal("The drawing " + document.Title + " has no layout named " + layout + ".");
        }

        /// <summary>
        /// Refuses an INSERT of a block that is neither in the drawing nor defined in the commit: a block the drawing has is its
        /// own and wins, and one the client defined with objects in it comes with the commit, copied in with the INSERT.
        /// </summary>
        private static void CheckBlock(CadDocument document, DxfDatabase fragment, DxfEntity entity)
        {
            if (entity is DxfBlockReference insert)
            {
                string name = insert.Name;

                if (
                    !name.StartsWith("*", StringComparison.Ordinal)
                    && !document.Database.BlockTable.Has(name)
                    && !Defines(fragment, name)
                )
                {
                    throw new Refusal(
                        "The drawing "
                            + document.Title
                            + " has no block "
                            + name
                            + " for the INSERT to place, and the commit does not define it: make the INSERT from a block of your own that holds objects."
                    );
                }
            }
        }

        /// <summary>Whether the commit's small drawing holds a block of the name with an object in it.</summary>
        private static bool Defines(DxfDatabase fragment, string name)
        {
            if (fragment == null || !fragment.BlockTable.Has(name))
            {
                return false;
            }

            foreach (DxfEntity entity in fragment.BlockTable[name])
            {
                if (!entity.IsErased)
                {
                    return true;
                }
            }

            return false;
        }

        private void Apply(
            CadDocument document,
            CadEditScope scope,
            List<Step> layers,
            List<Step> steps,
            DxfDatabase fragment,
            JsonArray inserted,
            List<EditStep> edits
        )
        {
            // The layers are made first, so that the objects of the commit can be on them.
            foreach (Step layer in layers)
            {
                WriteLayer(document, scope, layer);
            }

            int i = 0;

            while (i < steps.Count)
            {
                Step step = steps[i];

                if (step.Kind == StepKind.Insert)
                {
                    // Objects inserted one after another into one space are copied together, so that what points to another
                    // among them (a hatch to its boundary) points to its copy.
                    var group = new List<DxfEntity>();

                    while (
                        i < steps.Count
                        && steps[i].Kind == StepKind.Insert
                        && ReferenceEquals(steps[i].Space, step.Space)
                    )
                    {
                        group.Add(steps[i].Entity);
                        i++;
                    }

                    InsertGroup(document, scope, fragment, step.Space, group, inserted);
                    continue;
                }

                // An object whose owner took the change itself is left alone.
                if (!scope.IsHandled(step.Handle))
                {
                    DxfEntity old = document.FindEntity(step.Handle);

                    if (step.Kind == StepKind.Delete)
                    {
                        old.Erase();
                    }
                    else
                    {
                        Replace(document, fragment, old, step);
                    }
                }

                i++;
            }

            // The edits of the commit are written last, on the objects as the queued changes left them.
            foreach (EditStep edit in edits)
            {
                edit.Plan.Write(scope);
            }
        }

        private static void InsertGroup(
            CadDocument document,
            CadEditScope scope,
            DxfDatabase fragment,
            CadSpace space,
            List<DxfEntity> group,
            JsonArray inserted
        )
        {
            // A layer the drawing lacks is made by the copy; the layer box and the Layer manager are told.
            List<string> missing = group
                .Select(e => e.Layer)
                .Where(l => !string.IsNullOrEmpty(l) && !document.Database.LayerTable.Has(l))
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .OrderBy(l => l, StringComparer.OrdinalIgnoreCase)
                .ThenBy(l => l, StringComparer.Ordinal)
                .ToList();
            IReadOnlyList<DxfEntity> copies = fragment.WblockCloneObjects(group, space.Block);

            foreach (DxfEntity copy in copies)
            {
                // An MTEXT copied into a drawing of R12 is written as TEXTs and is in no space itself.
                if (string.IsNullOrEmpty(copy.Handle) || copy.IsErased)
                {
                    inserted.Add(null);
                    continue;
                }

                // A dimension made in code comes with a picture block that is empty: the app draws it, as DIM would.
                if (copy is DxfDimension dimension && !dimension.DimBlock.Any(e => !e.IsErased))
                {
                    CadCommands.Redraw(dimension);
                }

                scope.Added(space, copy);
                inserted.Add(copy.Handle);
            }

            foreach (string layer in missing)
            {
                scope.Record(new CadEditRecord(CadEditRecordKind.TableChanged, null, "Layer", null, layer));
            }
        }

        /// <summary>
        /// Writes a changed object over the one of the drawing, which keeps its handle (AutoCAD's <c>HandOverTo</c>): the
        /// object is copied into the drawing first, so that the layers, styles and blocks it needs are brought, and that copy
        /// is what takes the place. A dimension is drawn again, in the picture block it had.
        /// </summary>
        private static void Replace(CadDocument document, DxfDatabase fragment, DxfEntity old, Step step)
        {
            CadSpace space = document.FindSpace(step.Handle);
            DxfEntity made = fragment.WblockCloneObjects(new[] { step.Entity }, space.Block)[0];
            DxfEntity fresh = made.Clone();
            made.Erase();
            DxfBlockTableRecord spare = null;

            // The copy was brought with a picture block of its own; the object keeps the one it had, drawn again below.
            if (fresh is DxfDimension dimension && old is DxfDimension was && was.DimBlock != null)
            {
                spare = dimension.DimBlock;
                dimension.DimBlock = was.DimBlock;
            }

            old.HandOverTo(fresh, true, true);

            if (spare != null && spare.IsAnonymous)
            {
                spare.Erase();
            }

            if (document.FindEntity(step.Handle) is DxfDimension drawn)
            {
                CadCommands.Redraw(drawn);
            }
        }
    }
}
