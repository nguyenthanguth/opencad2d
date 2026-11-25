using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using GeometryHelper.Dxf;
using GeometryHelper.Geometry;

namespace OpenCAD2D
{
    /// <summary>
    /// Named views, as AutoCAD 2021 keeps and asks them: VIEW, the View Manager, where the window has one, and -VIEW, its ?,
    /// Delete, Orthographic, Restore, Save, sEttings and Window in AutoCAD's words. A view is a record of the VIEW table
    /// (<see cref="DxfViewTableRecord"/>), its layer snapshot the hidden layer state ACAD_VIEWS_ and its name; each run that
    /// changes the drawing is one undo step, restoring a view on screen is not, as ZOOM is not.
    /// </summary>
    internal static partial class CadCommands
    {
        private static readonly string[] ViewOptions =
        {
            "?",
            "Delete",
            "Orthographic",
            "Restore",
            "Save",
            "sEttings",
            "Window",
        };

        /// <summary>VIEW: the View Manager, shown again after each pick, until OK or Cancel; with no window, -VIEW's prompts.</summary>
        private static IEnumerable<CadPrompt> ViewManager(CadCommandContext c)
        {
            var request = new CadViewManagerRequest(c.Document);
            CadGroupMark mark = null;

            while (true)
            {
                ReadViews(c, request);
                request.Action = CadDialogAction.Cancel;
                request.Pick = null;
                request.Changing = (what, texts) =>
                {
                    if (mark == null && what != "Apply")
                    {
                        c.OpenUndoStep();
                        mark = c.Document.MarkGroup();
                    }

                    string said = ChangeViewInDialog(c, request, what, texts);
                    ReadViews(c, request);
                    return said;
                };

                bool asked = c.Session.Request("VIEW", c.Document, request);
                request.Changing = null;

                if (!asked)
                {
                    foreach (CadPrompt prompt in ViewTyped(c))
                    {
                        yield return prompt;
                    }

                    yield break;
                }

                if (request.Action == CadDialogAction.Ok)
                {
                    if (request.ToRestore != null)
                    {
                        CadNamedViews.Restore(c, request.ToRestore);
                    }

                    yield break;
                }

                if (request.Action != CadDialogAction.SelectObjects)
                {
                    if (mark != null)
                    {
                        c.Document.BackTo(mark);
                    }

                    yield break;
                }

                // Edit Boundaries… and Define window: two corners on the command line, the dialog hidden.
                yield return CadPrompt.ForPoint("Specify first corner");

                if (!c.Input.IsPoint)
                {
                    continue;
                }

                GeoPoint2 first = c.Input.Point;
                yield return CadPrompt.ForPoint("Specify opposite corner", first);

                if (!c.Input.IsPoint)
                {
                    continue;
                }

                DisplayBox box = CadNamedViews.Box(first, c.Input.Point);

                if (box.Width <= 0.0 || box.Height <= 0.0)
                {
                    c.Write("The window has no size.");
                    continue;
                }

                if (request.Pick == "NewWindow")
                {
                    request.NewWindow = box;
                }
                else if (CadNamedViews.Find(c, request.Selected) != null)
                {
                    if (mark == null)
                    {
                        c.OpenUndoStep();
                        mark = c.Document.MarkGroup();
                    }

                    string name = request.Selected;
                    c.Apply(
                        new[]
                        {
                            new CadEditRecord(
                                CadEditRecordKind.TableChanged,
                                null,
                                "View boundaries",
                                name,
                                Values(box.MinX, box.MinY, box.MaxX, box.MaxY)
                            ),
                        },
                        s => CadNamedViews.Bound(CadNamedViews.Find(c, name), box)
                    );
                }
            }
        }

        /// <summary>The views read into the request, model views then layout views; the view on screen as Current.</summary>
        private static void ReadViews(CadCommandContext c, CadViewManagerRequest request)
        {
            var rows = new List<CadViewRow>();

            foreach (DxfViewTableRecord view in c.Database.ViewTable)
            {
                rows.Add(
                    new CadViewRow(
                        view.Name,
                        view.IsPaperspaceView,
                        view.LayerState.Length > 0,
                        view.CenterPoint.X,
                        view.CenterPoint.Y,
                        view.Height,
                        view.Width,
                        view.ViewTwist * 180.0 / Math.PI
                    )
                );
            }

            rows.Sort(
                (a, b) =>
                {
                    int order = a.IsPaperspace.CompareTo(b.IsPaperspace);
                    order = order != 0 ? order : StringComparer.OrdinalIgnoreCase.Compare(a.Name, b.Name);
                    return order != 0 ? order : string.CompareOrdinal(a.Name, b.Name);
                }
            );

            request.Views = rows;
            CadView shown = c.Session.View;
            request.CurrentView =
                shown == null
                    ? null
                    : new CadViewRow(
                        "Current",
                        !c.Document.CurrentSpace.IsModel,
                        false,
                        shown.CenterX,
                        shown.CenterY,
                        shown.VisibleBox.Height,
                        shown.VisibleBox.Width,
                        0.0
                    );

            if (request.Selected != null && CadNamedViews.Find(c, request.Selected) == null)
            {
                request.Selected = null;
            }

            if (request.ToRestore != null && CadNamedViews.Find(c, request.ToRestore) == null)
            {
                request.ToRestore = null;
            }
        }

        /// <summary>A change of the dialog's made at once, an edit of the command's step; what it says where it is refused, else null.</summary>
        private static string ChangeViewInDialog(
            CadCommandContext c,
            CadViewManagerRequest request,
            string what,
            string[] texts
        )
        {
            if (what == "New")
            {
                string name = (request.NewName ?? string.Empty).Trim();

                if (!CadNamedViews.IsName(name))
                {
                    return "Invalid view name.";
                }

                if (CadNamedViews.Find(c, name) != null && texts[0] != "1")
                {
                    return "A view named " + name + " already exists.";
                }

                DisplayBox? box = request.NewWindow ?? Shown(c);

                if (box == null)
                {
                    return "There is no view to save.";
                }

                CadNamedViews.Save(c, name, box.Value, request.NewLayerSnapshot);
                request.Selected = CadNamedViews.Find(c, name).Name;
                request.NewWindow = null;
                request.IsNewViewOpen = false;
                return null;
            }

            if (what == "Apply")
            {
                if (request.ToRestore == null)
                {
                    return "Choose a view and Set Current first.";
                }

                CadNamedViews.Restore(c, request.ToRestore);
                return null;
            }

            DxfViewTableRecord view = CadNamedViews.Find(c, request.Selected);

            if (view == null)
            {
                return "Choose a named view in the list first.";
            }

            string chosen = view.Name;

            switch (what)
            {
                case "Delete":
                    CadNamedViews.Delete(c, new[] { chosen });
                    request.Selected = null;
                    return null;
                case "Layers":
                    CadNamedViews.Snapshot(c, chosen, true);
                    return null;
                case "Rename":
                    string typed = (texts[0] ?? string.Empty).Trim();

                    if (!CadNamedViews.IsName(typed))
                    {
                        return "Invalid view name.";
                    }

                    DxfViewTableRecord other = CadNamedViews.Find(c, typed);

                    if (other != null && !string.Equals(other.Name, chosen, StringComparison.Ordinal))
                    {
                        return "A view named " + typed + " already exists.";
                    }

                    c.Apply(
                        new[] { new CadEditRecord(CadEditRecordKind.TableChanged, null, "Rename view", chosen, typed) },
                        s => CadNamedViews.Rename(c, chosen, typed)
                    );
                    request.Selected = typed;
                    return null;
                default:
                    if (
                        !double.TryParse(texts[0], NumberStyles.Float, CultureInfo.InvariantCulture, out double height)
                        || !(height > 0.0)
                        || double.IsInfinity(height)
                        || !double.TryParse(
                            texts[1],
                            NumberStyles.Float,
                            CultureInfo.InvariantCulture,
                            out double width
                        )
                        || !(width > 0.0)
                        || double.IsInfinity(width)
                    )
                    {
                        return "A view's height and width are numbers above nought.";
                    }

                    c.Apply(
                        new[]
                        {
                            new CadEditRecord(
                                CadEditRecordKind.TableChanged,
                                null,
                                "View size",
                                chosen,
                                Values(height, width)
                            ),
                        },
                        s =>
                        {
                            DxfViewTableRecord record = CadNamedViews.Find(c, chosen);
                            record.Height = height;
                            record.Width = width;
                        }
                    );
                    return null;
            }
        }

        /// <summary>-VIEW: AutoCAD 2021's options, one a run, as its -VIEW asks them.</summary>
        private static IEnumerable<CadPrompt> ViewTyped(CadCommandContext c)
        {
            yield return CadPrompt.ForKeyword("Enter an option", ViewOptions);

            if (c.Input.Is("?"))
            {
                yield return CadPrompt.ForString("Enter view name(s) to list", "*");
                ListViews(c, c.Input.Kind == CadInputKind.Text ? c.Input.Text : "*");
                yield break;
            }

            if (c.Input.Kind != CadInputKind.Keyword)
            {
                yield break;
            }

            switch (c.Input.Text)
            {
                case "Delete":
                    yield return CadPrompt.ForString("Enter view name(s) to delete");

                    if (c.Input.Kind == CadInputKind.Text)
                    {
                        List<string> names = CadNamedViews.Matching(c, c.Input.Text);

                        if (names.Count == 0)
                        {
                            c.Write("No matching view names found.");
                        }
                        else
                        {
                            c.OpenUndoStep();
                            CadNamedViews.Delete(c, names);
                        }
                    }

                    break;
                case "Orthographic":
                    c.Write("Orthographic views look at a model in 3D: a drawing here is 2D and has none.");
                    break;
                case "Restore":
                    yield return CadPrompt.ForString("Enter view name to restore");

                    if (c.Input.Kind == CadInputKind.Text)
                    {
                        DxfViewTableRecord view = CadNamedViews.Find(c, c.Input.Text.Trim());

                        if (view == null)
                        {
                            c.Write("Cannot find view \"" + c.Input.Text.Trim() + "\".");
                            break;
                        }

                        string restoreName = view.Name;
                        string through = null;

                        // A view of model space restored in a layout goes into a viewport: the one worked through, else one picked
                        // ("Restoring Model space view." and "Select Viewport for view:", as AutoCAD 2021 asks).
                        if (!view.IsPaperspaceView && !c.Document.CurrentSpace.IsModel)
                        {
                            through = c.Document.ActiveViewport?.Handle;

                            if (through == null)
                            {
                                c.Write("Restoring Model space view.");

                                while (through == null)
                                {
                                    yield return CadPrompt.ForEntity("Select Viewport for view");

                                    if (c.Input.Kind != CadInputKind.Selection)
                                    {
                                        break;
                                    }

                                    if (
                                        c.Document.FindEntity(c.Input.Handles[0]) is DxfViewport picked
                                        && picked.Number != 1
                                    )
                                    {
                                        through = picked.Handle;
                                    }
                                }

                                if (through == null)
                                {
                                    break;
                                }
                            }
                        }

                        CadNamedViews.Restore(c, restoreName, through);
                    }

                    break;
                case "Save":
                case "Window":
                    bool window = c.Input.Is("Window");
                    yield return CadPrompt.ForString("Enter view name to save");
                    string name = c.Input.Kind == CadInputKind.Text ? c.Input.Text.Trim() : string.Empty;

                    if (!CadNamedViews.IsName(name))
                    {
                        if (name.Length > 0)
                        {
                            c.Write("Invalid view name.");
                        }

                        break;
                    }

                    DisplayBox? box = Shown(c);

                    if (window)
                    {
                        yield return CadPrompt.ForPoint("Specify first corner");

                        if (!c.Input.IsPoint)
                        {
                            break;
                        }

                        GeoPoint2 first = c.Input.Point;
                        yield return CadPrompt.ForPoint("Specify opposite corner", first);

                        if (!c.Input.IsPoint)
                        {
                            break;
                        }

                        box = CadNamedViews.Box(first, c.Input.Point);
                    }

                    if (box == null || box.Value.Width <= 0.0 || box.Value.Height <= 0.0)
                    {
                        c.Write("There is no view to save.");
                        break;
                    }

                    if (CadNamedViews.Find(c, name) != null)
                    {
                        yield return CadPrompt.ForKeyword(
                            "View \""
                                + CadNamedViews.Find(c, name).Name
                                + "\" already exists. Do you want to redefine it?",
                            new[] { "Yes", "No" },
                            "No"
                        );

                        if (!c.Input.Is("Yes"))
                        {
                            break;
                        }
                    }

                    c.OpenUndoStep();
                    CadNamedViews.Save(c, name, box.Value, false);
                    break;
                default:
                    yield return CadPrompt.ForKeyword(
                        "Enter an option",
                        new[] { "Background", "Categorize", "Layer snapshot", "live Section", "Ucs", "Visual style" }
                    );

                    if (!c.Input.Is("Layer snapshot"))
                    {
                        if (c.Input.Kind == CadInputKind.Keyword)
                        {
                            c.Write("A drawing here is 2D: of a view's settings only its layer snapshot is kept.");
                        }

                        break;
                    }

                    while (true)
                    {
                        yield return CadPrompt.ForKeywordOrText(
                            "Enter view name to edit Layer snapshot or",
                            new[] { "?" },
                            null
                        );

                        if (c.Input.Is("?"))
                        {
                            yield return CadPrompt.ForString("Enter view name(s) to list", "*");
                            ListViews(c, c.Input.Kind == CadInputKind.Text ? c.Input.Text : "*");
                            continue;
                        }

                        if (c.Input.Kind != CadInputKind.Text)
                        {
                            break;
                        }

                        DxfViewTableRecord view = CadNamedViews.Find(c, c.Input.Text.Trim());

                        if (view == null)
                        {
                            c.Write("No matching view names found.");
                            continue;
                        }

                        string chosen = view.Name;
                        yield return CadPrompt.ForKeyword("Enter an option", new[] { "Save", "Delete" }, "Cancel");

                        if (c.Input.Is("Save") || c.Input.Is("Delete"))
                        {
                            c.OpenUndoStep();
                            CadNamedViews.Snapshot(c, chosen, c.Input.Is("Save"));
                        }

                        break;
                    }

                    break;
            }
        }

        /// <summary>-VIEW ?: the views whose names match, each with M for model space or P for a layout, as AutoCAD 2021 lists them.</summary>
        private static void ListViews(CadCommandContext c, string pattern)
        {
            List<string> names = CadNamedViews.Matching(c, pattern);
            c.Write("Saved views:");

            if (names.Count == 0)
            {
                c.Write("  No matching views found.");
                return;
            }

            c.Write("View name                        Space");

            foreach (string name in names)
            {
                c.Write(("\"" + name + "\"").PadRight(35) + (CadNamedViews.Find(c, name).IsPaperspaceView ? "P" : "M"));
            }
        }

        /// <summary>What the canvas shows of the space on screen; null where it shows none.</summary>
        private static DisplayBox? Shown(CadCommandContext c) => c.Session.CommandView.VisibleBox;
    }
}
