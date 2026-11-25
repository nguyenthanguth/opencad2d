using System;
using System.Collections.Generic;
using System.Linq;
using GeometryHelper.Dxf;
using GeometryHelper.Geometry;

namespace OpenCAD2D
{
    /// <summary>
    /// The named views of a drawing, as -VIEW and the View Manager keep them and as <see cref="CadEditor"/> keeps them: a view
    /// is a record of the VIEW table (<see cref="DxfViewTableRecord"/>), its layer snapshot the hidden layer state ACAD_VIEWS_
    /// and its name. Each helper is one the commands call, with the context of the command whose undo step the edit joins, so
    /// that a view saved, restored or deleted through the editor is what the command makes of it.
    /// </summary>
    internal static class CadNamedViews
    {
        /// <summary>The names of the views that match a list of patterns, each with its wildcards, in ordinal order, letters of either case alike.</summary>
        internal static List<string> Matching(CadCommandContext c, string patterns)
        {
            string[] wanted = (string.IsNullOrWhiteSpace(patterns) ? "*" : patterns)
                .Split(',')
                .Select(p => p.Trim())
                .Where(p => p.Length > 0)
                .ToArray();
            var names = new List<string>();

            foreach (DxfViewTableRecord view in c.Database.ViewTable)
            {
                if (wanted.Any(p => CadWildcard.IsMatch(view.Name, p, true)))
                {
                    names.Add(view.Name);
                }
            }

            names.Sort(
                (a, b) =>
                {
                    int order = StringComparer.OrdinalIgnoreCase.Compare(a, b);
                    return order != 0 ? order : string.CompareOrdinal(a, b);
                }
            );

            return names;
        }

        /// <summary>A view's name AutoCAD takes: not empty, 255 letters at most, no space at either end and none of &lt;&gt;/\":;?*|=`.</summary>
        internal static bool IsName(string name) =>
            !string.IsNullOrEmpty(name)
            && name.Length <= 255
            && name.Trim().Length == name.Length
            && name.IndexOfAny("<>/\\\":;?*|=`".ToCharArray()) < 0
            && !name.Any(ch => ch < ' ');

        internal static DxfViewTableRecord Find(CadCommandContext c, string name) =>
            string.IsNullOrEmpty(name) || !c.Database.ViewTable.Has(name) ? null : c.Database.ViewTable[name];

        internal static DisplayBox Box(GeoPoint2 a, GeoPoint2 b) =>
            new DisplayBox(Math.Min(a.X, b.X), Math.Min(a.Y, b.Y), Math.Max(a.X, b.X), Math.Max(a.Y, b.Y));

        /// <summary>A view of a box saved in the space on screen, an edit of the command's step: made, or redefined where the name has one; its layer snapshot saved where asked.</summary>
        internal static void Save(CadCommandContext c, string name, DisplayBox box, bool snapshot)
        {
            bool paper = !c.Document.CurrentSpace.IsModel;
            c.Apply(new[] { new CadEditRecord(CadEditRecordKind.TableChanged, null, "View", null, name) }, s =>
            {
                DxfViewTableRecord view = Find(c, name);

                if (view == null)
                {
                    view = new DxfViewTableRecord
                    {
                        Name = name,
                        IsPaperspaceView = paper,
                        CenterPoint = new GeoPoint2((box.MinX + box.MaxX) / 2.0, (box.MinY + box.MaxY) / 2.0),
                        Height = box.Height,
                        Width = box.Width,
                    };
                    s.Database.ViewTable.Add(view);
                }
                else
                {
                    view.IsPaperspaceView = paper;
                    Bound(view, box);
                }

                // A view saved in a layout remembers the layout, as AutoCAD's -VIEW Save does; one of model space has none.
                view.Layout = paper ? c.Document.CurrentSpace.Layout : null;
            });

            if (snapshot)
            {
                Snapshot(c, Find(c, name).Name, true);
            }
        }

        /// <summary>A view's centre, height and width set from a box.</summary>
        internal static void Bound(DxfViewTableRecord view, DisplayBox box)
        {
            view.CenterPoint = new GeoPoint2((box.MinX + box.MaxX) / 2.0, (box.MinY + box.MaxY) / 2.0);
            view.Height = box.Height;
            view.Width = box.Width;
        }

        /// <summary>
        /// A view's layer snapshot saved from the layers as they stand, as the hidden state ACAD_VIEWS_ and its name with
        /// AutoCAD 2021's mask 0x8200, or deleted with the state; an edit of the command's step.
        /// </summary>
        internal static void Snapshot(CadCommandContext c, string name, bool save)
        {
            string state = CadLayerStates.ViewPrefix + name;
            c.Apply(
                new[]
                {
                    new CadEditRecord(
                        CadEditRecordKind.TableChanged,
                        null,
                        "View layer snapshot",
                        name,
                        save ? state : null
                    ),
                },
                s =>
                {
                    DxfViewTableRecord view = Find(c, name);
                    string had = view.LayerState;

                    if (had.Length > 0 && !string.Equals(had, state, StringComparison.OrdinalIgnoreCase))
                    {
                        CadLayerStates.Delete(s.Database, had);
                    }

                    if (save)
                    {
                        CadLayerStates.Save(
                            s.Database,
                            state,
                            string.Empty,
                            CadLayerStates.Hidden | CadLayerStates.CurrentViewport
                        );
                        view.LayerState = state;
                    }
                    else
                    {
                        CadLayerStates.Delete(s.Database, state);
                        view.LayerState = string.Empty;
                    }
                }
            );
        }

        /// <summary>A view renamed, its layer snapshot's state given the name that goes with the new one.</summary>
        internal static void Rename(CadCommandContext c, string name, string newName)
        {
            DxfViewTableRecord view = Find(c, name);
            string state = view.LayerState;
            view.Name = newName;

            if (state.Length > 0 && CadLayerStates.Find(c.Database, state) != null)
            {
                string renamed = CadLayerStates.ViewPrefix + newName;
                CadLayerStates.Rename(c.Database, state, renamed);
                view.LayerState = renamed;
            }
        }

        /// <summary>Views erased with their layer snapshots, an edit of the command's step.</summary>
        internal static void Delete(CadCommandContext c, IReadOnlyList<string> names)
        {
            c.Apply(
                names
                    .Select(n => new CadEditRecord(CadEditRecordKind.TableChanged, null, "Delete view", n, null))
                    .ToArray(),
                s =>
                {
                    foreach (string name in names)
                    {
                        DxfViewTableRecord view = Find(c, name);
                        string state = view.LayerState;
                        view.Erase();

                        if (state.Length > 0)
                        {
                            CadLayerStates.Delete(s.Database, state);
                        }
                    }
                }
            );
        }

        /// <summary>
        /// A view shown: its space made current where the canvas shows another (a layout view in the layout it was saved in, else
        /// the layout on screen or the first layout), the canvas zoomed to it, and its layer snapshot restored, the one edit of
        /// the step. A view of model space given a viewport of the layout shown goes into that viewport, as AutoCAD 2021 puts it:
        /// the viewport shows the whole view, its centre the view's and its height the view's, or the view's width over the
        /// viewport's shape where that is more (a view 44.85 by 25.4 into a viewport 3 by 2 is shown 29.9 high), and the viewport
        /// is the one worked through afterwards.
        /// </summary>
        internal static void Restore(CadCommandContext c, string name, string viewport = null)
        {
            DxfViewTableRecord view = Find(c, name);

            if (view == null)
            {
                return;
            }

            if (viewport != null)
            {
                RestoreInViewport(c, view, viewport);
                return;
            }

            GeoPoint2 center = view.CenterPoint;
            var box = new DisplayBox(
                center.X - view.Width / 2.0,
                center.Y - view.Height / 2.0,
                center.X + view.Width / 2.0,
                center.Y + view.Height / 2.0
            );
            CadSpace space = c.Document.CurrentSpace;
            string layout = view.IsPaperspaceView ? view.Layout?.Handle : null;
            CadSpace wanted = view.IsPaperspaceView
                ? c.Document.Spaces.FirstOrDefault(s =>
                    !s.IsModel
                    && layout != null
                    && string.Equals(s.Layout?.Handle, layout, StringComparison.OrdinalIgnoreCase)
                ) ?? (space.IsModel ? c.Document.Spaces.FirstOrDefault(s => !s.IsModel) : space)
                : c.Document.ModelSpace;

            if (wanted == null)
            {
                c.Write("The drawing has no layout to show the view \"" + view.Name + "\" in.");
                return;
            }

            if (wanted == space)
            {
                c.Session.CommandView.ZoomTo(box, 0.0);
            }
            else
            {
                // The switch joins the command's step, as AutoCAD 2021's console undoes -VIEW Restore from the Model tab in one
                // U that goes back to the tab, the layout shown by it.
                c.OpenUndoStep();
                CadEditResult shown = c.Document.ShowSpace(wanted, c.Command.Name);

                if (!shown.Succeeded)
                {
                    c.Write(shown.Message);
                    return;
                }

                wanted.PendingView = box;
            }

            RestoreLayers(c, view);
        }

        /// <summary>A view of model space shown through a viewport of the layout on screen, which is then the one worked through.</summary>
        internal static void RestoreInViewport(CadCommandContext c, DxfViewTableRecord view, string handle)
        {
            if (!(c.Document.FindEntity(handle) is DxfViewport viewport) || !(viewport.Height > 0.0))
            {
                return;
            }

            // The view shown whole: its height, or its width fitted to the viewport's shape where that takes more.
            double height = Math.Max(view.Height, view.Width * viewport.Height / viewport.Width);
            c.OpenUndoStep();
            CadEditResult set = c.Document.SetViewportView(handle, view.CenterPoint, height, c.Command.Name);

            if (!set.Succeeded)
            {
                c.Write(set.Message);
                return;
            }

            c.Document.WorkThrough(handle, c.Command.Name);
            RestoreLayers(c, view);
        }

        /// <summary>A view's layer snapshot restored, as an edit of the command's step; nothing where the view keeps none.</summary>
        internal static void RestoreLayers(CadCommandContext c, DxfViewTableRecord view)
        {
            string state = view.LayerState;

            if (state.Length > 0 && CadLayerStates.Find(c.Database, state) != null)
            {
                CadCommands.RestoreLayerState(c, state, false);
            }
        }
    }
}
