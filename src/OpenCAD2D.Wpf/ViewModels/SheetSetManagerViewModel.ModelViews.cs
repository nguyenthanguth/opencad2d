using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Globalization;
using System.IO;
using System.Linq;
using GeometryHelper.Dxf;
using GeometryHelper.Geometry;
using OpenCAD2D.SheetSets;

namespace OpenCAD2D.Wpf
{
    /// <summary>What a drag out of the Model Views tab carries, written as text: the drawing and, where it is a view, the name of the view.</summary>
    public static class SheetSetModelViewDrag
    {
        /// <summary>The name of the data format a drag out of the Model Views tab is dropped as.</summary>
        public const string Format = "OpenCAD2D.SheetSetModelView";

        /// <summary>Writes what a drag carries.</summary>
        /// <param name="drawingFile">The full path of the drawing.</param>
        /// <param name="viewName">The name of the view; null for the whole of the drawing's model space.</param>
        /// <returns>The text.</returns>
        public static string Write(string drawingFile, string viewName) =>
            drawingFile + "\n" + (viewName ?? string.Empty);

        /// <summary>Reads what <see cref="Write"/> wrote.</summary>
        /// <param name="text">The text.</param>
        /// <param name="drawingFile">The path of the drawing.</param>
        /// <param name="viewName">The name of the view; null for none.</param>
        /// <returns>Whether the text was one a drag wrote.</returns>
        public static bool TryRead(string text, out string drawingFile, out string viewName)
        {
            drawingFile = null;
            viewName = null;
            int at = text == null ? -1 : text.IndexOf('\n');

            if (at <= 0)
            {
                return false;
            }

            drawingFile = text.Substring(0, at);
            viewName = at + 1 < text.Length ? text.Substring(at + 1) : null;
            return true;
        }
    }

    /// <summary>
    /// The Model Views tab of the Sheet Set Manager (A12 4d): the resource locations of the set (folders), the drawings in each
    /// and the named model views of each drawing, read from the closed files; Add New Location, Remove Location, Refresh, Open,
    /// and Place on Sheet, which attaches the drawing as an attached external reference to the sheet's drawing, makes a viewport
    /// on the current layout at a scale of the drawing's list and records a sheet view in the set
    /// (<see cref="CadSheetSetPlacement"/>), by the menu or by dragging the view onto the drawing area.
    /// </summary>
    public sealed partial class SheetSetManagerViewModel
    {
        internal const string GuideModelViews =
            "https://nguyenthanguth.github.io/opencad2d/guides/presenter/sheet-sets.html#the-model-views-tab";

        private const string ModelViewsTab = "Model Views";

        private readonly SheetSetTabTree _modelTree = new SheetSetTabTree(true);
        private readonly Dictionary<string, ViewsCache> _modelViewsCache = new Dictionary<string, ViewsCache>(
            StringComparer.OrdinalIgnoreCase
        );
        private readonly Dictionary<string, string> _lastScale = new Dictionary<string, string>(StringComparer.Ordinal);
        private bool _paneShown = true;

        /// <summary>Gets the rows of the Model Views tab that are shown, in order: the resource locations, the drawings in those that are open, and the views in those drawings.</summary>
        public ObservableCollection<SheetSetTabRow> ModelRows => _modelTree.Rows;

        /// <summary>Gets the rows selected in the Model Views tab, in the order of the tree.</summary>
        public IReadOnlyList<SheetSetTabRow> SelectedModelRows => _modelTree.Selected;

        /// <summary>Gets or sets how the palette asks for a folder, Add New Location's dialog; it gives the folder's path, or null for Cancel. Null: the folder is not asked.</summary>
        public Func<string> AskFolder { get; set; }

        /// <summary>Gets or sets whether the pane under the tree is shown (the Preview/Details Pane item of the Model Views menu); shown at first.</summary>
        public bool PaneShown
        {
            get => _paneShown;
            set => Set(ref _paneShown, value);
        }

        private sealed class ViewsCache
        {
            internal ViewsCache(DateTime stamp, long length, IReadOnlyList<CadModelView> views)
            {
                Stamp = stamp;
                Length = length;
                Views = views;
            }

            internal DateTime Stamp { get; }

            internal long Length { get; }

            internal IReadOnlyList<CadModelView> Views { get; }
        }

        // ---- The tree

        private void BuildModels()
        {
            if (_current == null || _tab != ModelViewsTab)
            {
                _modelTree.Load(null);
                return;
            }

            SheetSetTabRow root = _modelTree.Row(SheetSetTabRowKind.SheetSet, "models", "set", null);

            foreach (CadFileReference location in _current.SheetSet.ResourceLocations)
            {
                string folder = location.Locate(_current.Folder) ?? location.FileName ?? string.Empty;
                bool there = Directory.Exists(folder);
                SheetSetTabRow row = _modelTree.Row(SheetSetTabRowKind.Location, "loc:" + folder, "folder", root);
                row.Text = folder;
                row.Path = folder;
                row.Tone = there ? SheetSetTone.Normal : SheetSetTone.Error;
                root.Add(row);

                if (!there)
                {
                    continue;
                }

                foreach (string file in CadSheetSetResources.DrawingsIn(folder))
                {
                    string drawing = file;
                    SheetSetTabRow drawingRow = _modelTree.Row(
                        SheetSetTabRowKind.Drawing,
                        "dwg:" + drawing,
                        "drawing",
                        row
                    );
                    drawingRow.Text = Path.GetFileName(drawing);
                    drawingRow.Path = drawing;
                    drawingRow.DefaultExpanded = false;
                    drawingRow.LoadWith(() => ModelViewRows(drawingRow, drawing));
                    row.Add(drawingRow);
                }
            }

            _modelTree.Load(root);
        }

        private IEnumerable<SheetSetTabRow> ModelViewRows(SheetSetTabRow parent, string drawing)
        {
            var rows = new List<SheetSetTabRow>();

            foreach (CadModelView view in ModelViewsOfDrawing(drawing))
            {
                SheetSetTabRow row = _modelTree.Row(
                    SheetSetTabRowKind.ModelView,
                    "mv:" + drawing + "|" + view.Name,
                    "view",
                    parent
                );
                row.Text = view.Name;
                row.Path = drawing;
                row.ViewName = view.Name;
                rows.Add(row);
            }

            return rows;
        }

        /// <summary>The named model views of a drawing, read again only when its file's time stamp or length moved; none for a drawing that cannot be read, which the command line says.</summary>
        private IReadOnlyList<CadModelView> ModelViewsOfDrawing(string drawing)
        {
            try
            {
                var info = new FileInfo(drawing);

                if (
                    _modelViewsCache.TryGetValue(drawing, out ViewsCache cache)
                    && cache.Stamp == info.LastWriteTimeUtc
                    && cache.Length == info.Length
                )
                {
                    return cache.Views;
                }

                IReadOnlyList<CadModelView> views = CadSheetSetResources.ModelViewsOf(drawing);
                _modelViewsCache[drawing] = new ViewsCache(info.LastWriteTimeUtc, info.Length, views);
                return views;
            }
            catch (Exception ex) when (IsFileProblem(ex))
            {
                _main.Say("Cannot read the views of " + drawing + ": " + ex.Message);
                return Array.Empty<CadModelView>();
            }
        }

        // ---- The details

        private void DescribeModelView(SheetSetTabRow row)
        {
            if (row == null)
            {
                return;
            }

            if (row.Kind == SheetSetTabRowKind.Location)
            {
                bool there = Directory.Exists(row.Path);
                Details.Add(new SheetSetDetailRow("Folder", row.Path));
                Details.Add(
                    there
                        ? new SheetSetDetailRow("Status", "Available", SheetSetTone.Ok)
                        : new SheetSetDetailRow("Status", "Not found: the folder does not exist.", SheetSetTone.Error)
                );

                if (there)
                {
                    Details.Add(
                        new SheetSetDetailRow("Drawings", row.Children.Count.ToString(CultureInfo.InvariantCulture))
                    );
                }

                return;
            }

            var file = new FileInfo(row.Path);
            string folder = Path.GetDirectoryName(row.Path);

            if (row.Kind == SheetSetTabRowKind.ModelView)
            {
                Details.Add(new SheetSetDetailRow("View", row.Text));
            }

            Details.Add(new SheetSetDetailRow("Drawing", Path.GetFileName(row.Path)));
            Details.Add(new SheetSetDetailRow("Folder", folder));

            if (!file.Exists)
            {
                Details.Add(
                    new SheetSetDetailRow("Status", "Not found: the drawing does not exist.", SheetSetTone.Error)
                );
                return;
            }

            if (row.Kind == SheetSetTabRowKind.ModelView)
            {
                CadModelView view = ModelViewsOfDrawing(row.Path).FirstOrDefault(v => v.Name == row.ViewName);

                if (view != null)
                {
                    Details.Add(new SheetSetDetailRow("Size", SizeOfView(view)));
                }
            }
            else
            {
                Details.Add(new SheetSetDetailRow("Size", SizeOf(file.Length)));
            }

            Details.Add(
                new SheetSetDetailRow(
                    "Last saved",
                    file.LastWriteTime.ToString("yyyy-MM-dd HH:mm", CultureInfo.InvariantCulture)
                )
            );
        }

        /// <summary>The size of the area a view shows as the pane writes it, in the unit of its drawing: "32 000 × 18 500 mm".</summary>
        private static string SizeOfView(CadModelView view)
        {
            string Number(double value) =>
                Math.Round(value).ToString("#,##0", CultureInfo.InvariantCulture).Replace(',', ' ');

            string unit;

            switch (view.Units)
            {
                case DxfUnits.Millimeters:
                    unit = " mm";
                    break;
                case DxfUnits.Centimeters:
                    unit = " cm";
                    break;
                case DxfUnits.Meters:
                    unit = " m";
                    break;
                case DxfUnits.Inches:
                    unit = " in";
                    break;
                case DxfUnits.Feet:
                    unit = " ft";
                    break;
                default:
                    unit = string.Empty;
                    break;
            }

            return Number(view.Width) + " × " + Number(view.Height) + unit;
        }

        // ---- Locations

        /// <summary>Adds a folder to the set's resource locations, as Add New Location does, after the ones it has.</summary>
        /// <param name="folder">The folder.</param>
        /// <returns>Whether it was added; why not is said on the command line.</returns>
        public bool AddLocation(string folder)
        {
            if (_current == null || string.IsNullOrWhiteSpace(folder))
            {
                return false;
            }

            string full;

            try
            {
                full = Path.GetFullPath(folder).TrimEnd('\\', '/');
            }
            catch (Exception ex) when (IsFileProblem(ex))
            {
                _main.Say("Cannot add the location " + folder + ": " + ex.Message);
                return false;
            }

            if (!Directory.Exists(full))
            {
                _main.Say("The folder " + full + " is not there.");
                return false;
            }

            if (_current.SheetSet.ResourceLocations.Any(l => SamePath(l.Locate(_current.Folder), full)))
            {
                _main.Say("The folder " + full + " is a resource location of the sheet set already.");
                return false;
            }

            string setFolder = _current.Folder;
            return Change(set => set.AddResourceLocation(CadFileReference.FromPath(full, setFolder)));
        }

        /// <summary>Asks for a folder and adds it, as Add New Location… does.</summary>
        /// <returns>Whether a location was added.</returns>
        public bool AddLocationDialog()
        {
            if (_current == null)
            {
                return false;
            }

            if (AskFolder == null)
            {
                _main.Say("The folder dialog is shown by the window.");
                return false;
            }

            string folder = AskFolder();
            return folder != null && AddLocation(folder);
        }

        /// <summary>Takes a folder out of the set's resource locations, as Remove Location does. No file or folder is touched.</summary>
        /// <param name="location">The location's row.</param>
        /// <returns>Whether it was removed; why not is said on the command line.</returns>
        /// <exception cref="ArgumentNullException">Thrown when the row is null.</exception>
        public bool RemoveLocation(SheetSetTabRow location)
        {
            if (location == null)
            {
                throw new ArgumentNullException(nameof(location));
            }

            if (_current == null || location.Kind != SheetSetTabRowKind.Location)
            {
                return false;
            }

            string setFolder = _current.Folder;
            return Change(set =>
            {
                List<CadFileReference> locations = set.ResourceLocations.ToList();
                int at = locations.FindIndex(l => SamePath(l.Locate(setFolder) ?? l.FileName, location.Path));

                if (at < 0)
                {
                    throw new InvalidOperationException(
                        "The location " + location.Path + " is no longer in the sheet set: refresh the palette."
                    );
                }

                set.RemoveResourceLocationAt(at);
            });
        }

        /// <summary>Reads the folders and their drawings again, as Refresh does: the views of every drawing are read again when it is opened.</summary>
        public void RefreshModelViews()
        {
            _modelViewsCache.Clear();
            Refresh();
        }

        /// <summary>Opens every row that holds rows, as Expand does.</summary>
        public void ExpandModelRows() => _modelTree.ExpandAll();

        /// <summary>Closes every row that holds rows, as Collapse does.</summary>
        public void CollapseModelRows() => _modelTree.CollapseAll();

        // ---- Open

        /// <summary>
        /// Opens the drawing of a row of the Model Views tab in the app, or shows it where it is open, as Open does. The presenter
        /// has no read-only drawings, so Open Read-Only opens for editing and says so.
        /// </summary>
        /// <param name="row">The row of a drawing or one of its views.</param>
        /// <param name="readOnly">Whether Open Read-Only was chosen.</param>
        /// <returns>Whether the drawing was asked to open.</returns>
        /// <exception cref="ArgumentNullException">Thrown when the row is null.</exception>
        public bool OpenModelDrawing(SheetSetTabRow row, bool readOnly)
        {
            if (row == null)
            {
                throw new ArgumentNullException(nameof(row));
            }

            if (row.Path == null || row.Kind == SheetSetTabRowKind.Location)
            {
                return false;
            }

            if (!File.Exists(row.Path))
            {
                _main.Say("The drawing " + row.Path + " is not found.");
                return false;
            }

            OpenDrawing(row.Path, null);

            if (readOnly)
            {
                _main.Say("The presenter has no read-only drawings: the drawing is opened for editing.");
            }

            RefreshOpenState();
            return true;
        }

        // ---- Place on Sheet

        /// <summary>The sheet the layout current in the app is: its row of the Sheet List; null where the layout is Model or is not a sheet of the set.</summary>
        private SheetSetNodeViewModel SheetOfCurrentLayout()
        {
            CadDocument document = _main.Document;
            CadSpace space = document?.CurrentSpace;

            if (_current == null || space == null || space.IsModel || document.FileName == null)
            {
                return null;
            }

            return AllSheets()
                .FirstOrDefault(s =>
                    !s.IsMissing
                    && SamePath(s.Found.DrawingFile, document.FileName)
                    && string.Equals(s.Found.LayoutName, space.Name, StringComparison.Ordinal)
                );
        }

        /// <summary>Gets whether Place on Sheet is on: the set can be changed and the layout current in the app is a sheet of it.</summary>
        public bool CanPlaceOnSheet => !IsLocked && SheetOfCurrentLayout() != null;

        private string ScaleFor(CadPlaceOnSheetPlan plan) =>
            _lastScale.TryGetValue(plan.DrawingFile + "|" + plan.View?.Name, out string scale)
                ? scale
                : CadSheetSetPlacement.DefaultScale;

        /// <summary>
        /// Starts Place on Sheet for a drawing or a named view of the Model Views tab: on the command line, "Specify insertion
        /// point or [Scale]:" with the view's outline at the scale (the view's last, else 1:100) following the cursor; the click
        /// attaches the drawing as an attached xref, makes the viewport and a named view in the layout, and records a sheet view on
        /// the sheet: the next number on that sheet, the title the view's name. One undo step of the drawing.
        /// </summary>
        /// <param name="row">The row of a drawing or one of its views.</param>
        /// <returns>Whether the command was started; why not is said on the command line.</returns>
        /// <exception cref="ArgumentNullException">Thrown when the row is null.</exception>
        public bool PlaceOnSheet(SheetSetTabRow row)
        {
            if (row == null)
            {
                throw new ArgumentNullException(nameof(row));
            }

            if (!CanPlaceBy(row, out SheetSetNodeViewModel sheet, out CadPlaceOnSheetPlan plan))
            {
                return false;
            }

            return _main.StartSheetSetCommand(
                CadSheetSetPlacement.Command(plan, ScaleFor(plan), result => Record(sheet.Id, plan, result))
            );
        }

        /// <summary>
        /// Places what a drag out of the Model Views tab carried where it was dropped on the drawing area, at the view's last scale
        /// (else 1:100): the same as Place on Sheet with the click made at the drop.
        /// </summary>
        /// <param name="text">What <see cref="SheetSetModelViewDrag.Write"/> wrote.</param>
        /// <param name="at">Where it is dropped, on the paper of the layout current.</param>
        /// <returns>Whether the view was placed; why not is said on the command line.</returns>
        public bool PlaceDropped(string text, GeoPoint2 at)
        {
            if (!SheetSetModelViewDrag.TryRead(text, out string file, out string viewName) || _main.Document == null)
            {
                return false;
            }

            SheetSetTabRow row = new SheetSetTabRow(
                _modelTree,
                viewName == null ? SheetSetTabRowKind.Drawing : SheetSetTabRowKind.ModelView,
                "drop",
                "view",
                null
            )
            {
                Path = file,
                ViewName = viewName,
            };

            if (!CanPlaceBy(row, out SheetSetNodeViewModel sheet, out CadPlaceOnSheetPlan plan))
            {
                return false;
            }

            try
            {
                CadDocument document = _main.Document;
                CadPlaceOnSheetResult result = CadSheetSetPlacement.Place(
                    document,
                    document.CurrentSpace,
                    plan,
                    at,
                    ScaleFor(plan)
                );
                Record(sheet.Id, plan, result);
                return true;
            }
            catch (Exception ex) when (IsFileProblem(ex))
            {
                _main.Say(ex.Message);
                return false;
            }
        }

        /// <summary>Checks Place on Sheet can be done for a row and works out what it places.</summary>
        private bool CanPlaceBy(SheetSetTabRow row, out SheetSetNodeViewModel sheet, out CadPlaceOnSheetPlan plan)
        {
            sheet = null;
            plan = null;

            if (
                _current == null
                || (row.Kind != SheetSetTabRowKind.Drawing && row.Kind != SheetSetTabRowKind.ModelView)
            )
            {
                return false;
            }

            if (IsLocked)
            {
                _main.Say("The sheet set is read-only and cannot be changed. " + _lockReason);
                return false;
            }

            sheet = SheetOfCurrentLayout();

            if (sheet == null)
            {
                _main.Say(
                    "Place on Sheet needs the layout current to be a sheet of the sheet set: show a sheet first."
                );
                return false;
            }

            if (!File.Exists(row.Path))
            {
                _main.Say("The drawing " + row.Path + " is not found.");
                return false;
            }

            try
            {
                plan = CadSheetSetPlacement.Plan(row.Path, row.ViewName);
                return true;
            }
            catch (Exception ex) when (IsFileProblem(ex))
            {
                _main.Say(ex.Message);
                return false;
            }
        }

        /// <summary>Records a view placed on a sheet in the set: a sheet view with the next number of the sheet and the view's name for a title, pointing at the named view saved in the layout.</summary>
        private void Record(string sheetId, CadPlaceOnSheetPlan plan, CadPlaceOnSheetResult result)
        {
            CadDocument document = _main.Document;
            _lastScale[plan.DrawingFile + "|" + plan.View?.Name] = result.Scale;
            string number = null;
            string setFolder = _current.Folder;
            bool made = Change(set =>
            {
                var sheet =
                    set.Find(sheetId) as CadSheetSetSheet
                    ?? throw new InvalidOperationException(
                        "The sheet is no longer in the sheet set: refresh the palette."
                    );
                number = NextViewNumber(sheet);
                var named = new CadDrawingReference(
                    CadFileReference.FromPath(document.FileName, setFolder),
                    result.NamedViewName,
                    result.NamedViewHandle,
                    result.NamedViewOwnerHandle
                );
                sheet.AddView(number, result.Title, named, null);
            });

            if (made)
            {
                _main.Say("Sheet view " + number + " - " + result.Title + " recorded in the sheet set.");
            }
        }

        /// <summary>The number the next view of a sheet takes: one more than the largest number the views have, or than how many there are.</summary>
        private static string NextViewNumber(CadSheetSetSheet sheet)
        {
            int largest = 0;
            IReadOnlyList<CadSheetView> views = sheet.Views;

            foreach (CadSheetView view in views)
            {
                if (
                    int.TryParse(view.Number, NumberStyles.None, CultureInfo.InvariantCulture, out int n)
                    && n > largest
                )
                {
                    largest = n;
                }
            }

            return (Math.Max(largest, views.Count) + 1).ToString(CultureInfo.InvariantCulture);
        }

        // ---- The menus

        /// <summary>
        /// Makes the right-click menu of a row of the Model Views tab: on a view or a drawing Place on Sheet (greyed where the layout
        /// current is not a sheet of the set), Open, Open Read-Only, See Model Space Views (a drawing), eTransmit…, the Preview/Details
        /// Pane tick, Expand and Collapse; on a location Add New Location…, Remove Location, the pane tick, Expand and Collapse; on no row
        /// Add New Location… and the pane tick. Changes are greyed in a locked set.
        /// </summary>
        /// <param name="row">The row the menu is opened on; null for a click on no row.</param>
        /// <returns>The items; empty where no set is open.</returns>
        public IReadOnlyList<CadMenuItemViewModel> ModelViewsMenuFor(SheetSetTabRow row)
        {
            if (_current == null)
            {
                return Array.Empty<CadMenuItemViewModel>();
            }

            bool change = !IsLocked;
            CadMenuItemViewModel pane = Item(
                "Preview/Details Pane",
                () => PaneShown = !PaneShown,
                true,
                isChecked: PaneShown
            );
            CadMenuItemViewModel add = Item("Add New Location…", () => AddLocationDialog(), change);

            if (row == null)
            {
                return new List<CadMenuItemViewModel> { add, CadMenuItemViewModel.Separator(), pane };
            }

            var expand = new List<CadMenuItemViewModel>
            {
                pane,
                Item("Expand", ExpandModelRows),
                Item("Collapse", CollapseModelRows),
            };

            if (row.Kind == SheetSetTabRowKind.Location)
            {
                var items = new List<CadMenuItemViewModel>
                {
                    add,
                    Item("Remove Location", () => RemoveLocation(row), change),
                    CadMenuItemViewModel.Separator(),
                };
                items.AddRange(expand);
                return items;
            }

            var menu = new List<CadMenuItemViewModel>
            {
                Item("Place on Sheet", () => PlaceOnSheet(row), CanPlaceOnSheet, isDefault: true),
                CadMenuItemViewModel.Separator(),
                Item("Open", () => OpenModelDrawing(row, false)),
                Item("Open Read-Only", () => OpenModelDrawing(row, true)),
            };

            if (row.Kind == SheetSetTabRowKind.Drawing)
            {
                menu.Add(Item("See Model Space Views", () => row.IsExpanded = true));
            }

            menu.Add(CadMenuItemViewModel.Separator());
            menu.Add(Item("eTransmit…", () => NotBuilt("eTransmit", "5b")));
            menu.Add(CadMenuItemViewModel.Separator());
            menu.AddRange(expand);
            return menu;
        }
    }
}
