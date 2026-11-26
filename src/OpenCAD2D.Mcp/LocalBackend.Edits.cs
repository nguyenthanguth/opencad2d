using System;
using System.Collections.Generic;
using System.Linq;
using GeometryHelper.Dxf;

namespace OpenCAD2D.Mcp
{
    /// <summary>The second phase's calls on a drawing file: scripts on the command session, edits of the core, records, layers.</summary>
    internal sealed partial class LocalBackend
    {
        public string Run(CommandScript script)
        {
            CadCommandSession session = Session;

            if (session.IsRunning)
            {
                throw new InvalidOperationException("A command is running in the drawing: " + session.PromptText);
            }

            int mark = session.History.Count;

            try
            {
                // The next input starts a command: the first of the script, and the first after an edit of the core. Any other input
                // with no command running would start one (an Enter repeats the last), so the script is refused instead.
                bool fresh = true;

                foreach (ScriptStep step in script.Steps)
                {
                    if (step.Kind != "make" && !fresh && !session.IsRunning)
                    {
                        throw new InvalidOperationException(
                            "The command ended before it had all its inputs: it takes fewer than the tool sent. "
                                + Said(session, mark)
                        );
                    }

                    switch (step.Kind)
                    {
                        case "word":
                        case "text":
                            session.Feed(step.Text);
                            fresh = false;
                            break;
                        case "enter":
                            session.Enter();
                            fresh = false;
                            break;
                        case "pick":
                            if (!session.FeedSelection(step.Handles))
                            {
                                throw new InvalidOperationException(
                                    "The command did not take the objects. " + Said(session, mark)
                                );
                            }

                            fresh = false;
                            break;
                        default:
                            CadMake.Run(Document, step.What, step.Args);
                            fresh = true;
                            break;
                    }
                }

                if (session.IsRunning)
                {
                    string waiting = session.PromptText;
                    session.Escape();
                    throw new InvalidOperationException(
                        "The command was still asking \"" + waiting + "\" and was cancelled. " + Said(session, mark)
                    );
                }
            }
            catch
            {
                if (session.IsRunning)
                {
                    session.Escape();
                }

                throw;
            }

            return string.Join("\n", session.History.Skip(mark));
        }

        /// <summary>The last few lines the command line wrote since a mark, for a refusal.</summary>
        private static string Said(CadCommandSession session, int mark) =>
            "The command line said: " + string.Join(" | ", session.History.Skip(mark).Reverse().Take(6).Reverse());

        public void Group(Action work)
        {
            Document.BeginGroup("MCP");

            try
            {
                work();
            }
            catch
            {
                Document.CancelGroup();
                throw;
            }

            Document.EndGroup();
        }

        public IReadOnlyList<EntityRef> Objects(IReadOnlyList<string> handles) =>
            handles
                .Select(h => Document.FindEntity(h))
                .Where(e => e != null && !e.IsErased)
                .Select(e => new EntityRef(e.Handle, e))
                .ToList();

        public IReadOnlyList<string> Make(string what, CadMakeArgs args) => CadMake.Run(Document, what, args);

        public IReadOnlyList<string> Apply(
            DxfDatabase records,
            IReadOnlyList<DxfEntity> add,
            string layout,
            IReadOnlyList<string> erase
        )
        {
            CadSpace space = SpaceNamed(layout);
            Found(erase);
            var batch = new DxfDatabase("AC1027");
            var group = new List<DxfEntity>();

            foreach (DxfEntity entity in add)
            {
                DxfEntity copy = entity.Clone();
                ProvideFor(batch, copy, records);

                if (copy is DxfDimension dimension && dimension.DimBlock == null)
                {
                    var picture = new DxfBlockTableRecord { Name = "*D" };
                    batch.BlockTable.Add(picture);
                    dimension.DimBlock = picture;
                }

                batch.ModelSpace.AppendEntity(copy);
                group.Add(copy);
            }

            var handles = new List<string>();
            List<CadEditRecord> planned = erase
                .Select(h => new CadEditRecord(CadEditRecordKind.Erased, h, "MCP"))
                .ToList();
            CadEditResult result = Document.ApplyEdit("MCP", planned, scope =>
            {
                if (records != null)
                {
                    BringRecords(scope, records);
                }

                if (group.Count > 0)
                {
                    InsertGroup(scope, batch, space, group, handles);
                }

                foreach (string handle in erase)
                {
                    DxfEntity old = scope.Document.FindEntity(handle);

                    if (old != null && !scope.IsHandled(handle))
                    {
                        old.Erase();
                    }
                }
            });

            if (!result.Succeeded)
            {
                throw new InvalidOperationException(result.Message);
            }

            return handles;
        }

        /// <summary>The names the drawing has not and a new small drawing is not made with (its defaults are not meant to be brought).</summary>
        private static List<string> Missing(IEnumerable<string> names, DxfSymbolTable have, DxfSymbolTable defaults) =>
            names
                .Where(n => !have.Has(n) && !defaults.Has(n))
                .OrderBy(n => n, StringComparer.OrdinalIgnoreCase)
                .ThenBy(n => n, StringComparer.Ordinal)
                .ToList();

        /// <summary>Brings the records of a small drawing the drawing has not into it, telling the edit of each.</summary>
        private static void BringRecords(CadEditScope scope, DxfDatabase records)
        {
            DxfDatabase database = scope.Document.Database;
            var made = new DxfDatabase("AC1027");
            List<string> styles = Missing(
                records.TextStyleTable.Select(r => r.Name),
                database.TextStyleTable,
                made.TextStyleTable
            );
            List<string> linetypes = Missing(
                records.LinetypeTable.Select(r => r.Name),
                database.LinetypeTable,
                made.LinetypeTable
            );
            List<string> dimensions = Missing(
                records.DimStyleTable.Select(r => r.Name),
                database.DimStyleTable,
                made.DimStyleTable
            );
            List<string> blocks = Missing(
                records
                    .BlockTable.Where(b =>
                        !b.IsLayout && !b.IsAnonymous && !b.Name.StartsWith("*", StringComparison.Ordinal)
                    )
                    .Select(b => b.Name),
                database.BlockTable,
                made.BlockTable
            );
            CadTableRecords.Add(database, records, styles, linetypes, blocks, dimensions);

            foreach (string name in styles)
            {
                scope.Record(new CadEditRecord(CadEditRecordKind.TableChanged, null, "Text style", null, name));
            }

            foreach (string name in linetypes)
            {
                scope.Record(new CadEditRecord(CadEditRecordKind.TableChanged, null, "Linetype", null, name));
            }

            foreach (string name in blocks)
            {
                scope.Record(new CadEditRecord(CadEditRecordKind.TableChanged, null, "Block", null, name));
            }

            foreach (string name in dimensions)
            {
                scope.Record(new CadEditRecord(CadEditRecordKind.TableChanged, null, "Dimension style", null, name));
            }
        }

        /// <summary>As <c>Provide</c>, with the records the same step brings counted as the drawing's own.</summary>
        private void ProvideFor(DxfDatabase batch, DxfEntity entity, DxfDatabase records)
        {
            if (records == null)
            {
                Provide(batch, entity);
                return;
            }

            DxfDatabase database = Document.Database;
            string linetype = entity.Linetype;

            if (
                !string.IsNullOrEmpty(linetype)
                && !string.Equals(linetype, "BYLAYER", StringComparison.OrdinalIgnoreCase)
                && !string.Equals(linetype, "BYBLOCK", StringComparison.OrdinalIgnoreCase)
                && !string.Equals(linetype, "CONTINUOUS", StringComparison.OrdinalIgnoreCase)
                && !batch.LinetypeTable.Has(linetype)
            )
            {
                if (!database.LinetypeTable.Has(linetype) && !records.LinetypeTable.Has(linetype))
                {
                    throw new ArgumentException("The drawing has no linetype " + linetype + ".");
                }

                batch.LinetypeTable.Add(new DxfLinetypeTableRecord { Name = linetype });
            }

            string style =
                entity is DxfDBText text ? text.TextStyleName
                : entity is DxfMText mtext ? mtext.TextStyleName
                : null;

            if (
                !string.IsNullOrEmpty(style)
                && !string.Equals(style, "Standard", StringComparison.OrdinalIgnoreCase)
                && !batch.TextStyleTable.Has(style)
            )
            {
                if (!database.TextStyleTable.Has(style) && !records.TextStyleTable.Has(style))
                {
                    throw new ArgumentException("The drawing has no text style " + style + ".");
                }

                batch.TextStyleTable.Add(new DxfTextStyleTableRecord { Name = style });
            }

            if (entity is DxfBlockReference insert && !batch.BlockTable.Has(insert.Name))
            {
                if (!database.BlockTable.Has(insert.Name) && !records.BlockTable.Has(insert.Name))
                {
                    throw new ArgumentException("The drawing has no block " + insert.Name + ".");
                }

                batch.BlockTable.Add(new DxfBlockTableRecord { Name = insert.Name });
            }
        }

        public void ModifyLayer(LayerRow look)
        {
            LayerRow was = Layers()
                .FirstOrDefault(l => string.Equals(l.Name, look.Name, StringComparison.OrdinalIgnoreCase));

            if (was == null)
            {
                throw new ArgumentException("The drawing has no layer " + look.Name + ".");
            }

            string name = was.Name;
            Document.BeginGroup("MCP");

            try
            {
                if (was.Color != look.Color)
                {
                    Check(CadLayerEditor.SetColor(Document, name, look.Color));
                }

                if (!string.Equals(was.Linetype, look.Linetype, StringComparison.OrdinalIgnoreCase))
                {
                    Check(CadLayerEditor.SetLinetype(Document, name, look.Linetype));
                }

                if (was.LineWeight != look.LineWeight)
                {
                    Check(CadLayerEditor.SetLineWeight(Document, name, (DxfLineWeight)look.LineWeight));
                }

                if (was.IsOn != look.IsOn)
                {
                    Check(CadLayerEditor.SetOn(Document, name, look.IsOn));
                }

                if (was.IsFrozen != look.IsFrozen)
                {
                    Check(CadLayerEditor.SetFrozen(Document, name, look.IsFrozen));
                }

                if (was.IsLocked != look.IsLocked)
                {
                    Check(CadLayerEditor.SetLocked(Document, name, look.IsLocked));
                }

                if (was.IsPlottable != look.IsPlottable)
                {
                    Check(CadLayerEditor.SetPlottable(Document, name, look.IsPlottable));
                }
            }
            catch
            {
                Document.CancelGroup();
                throw;
            }

            Document.EndGroup();
        }

        public void SetVariable(string name, object value) => Session.Editor.SetVariable(name, value);

        public void SetCurrentLayer(string name) => Check(CadLayerEditor.SetCurrent(Document, name));

        public void SetCurrentLayout(string name) => Check(Document.ShowSpace(SpaceNamed(name)));

        public void AddLayout(string name, string paper, bool landscape) =>
            CadLayouts.Add(Document, name, paper, landscape);

        public string AddViewport(
            string layout,
            IReadOnlyList<double> frame,
            string scale,
            IReadOnlyList<double> center,
            IReadOnlyList<string> frozenLayers
        ) => CadLayouts.AddViewport(Document, layout, frame.ToArray(), scale, center?.ToArray(), frozenLayers);

        public bool Redo() => Document.Redo();
    }
}
