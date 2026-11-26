using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Globalization;
using System.IO;
using System.Linq;
using GeometryHelper.Dxf;
using OpenCAD2D.SheetSets;

namespace OpenCAD2D.Wpf
{
    /// <summary>
    /// The Sheet Views tab of the Sheet Set Manager (A12 4d): the views placed on the sheets of the set, listed by category or by
    /// sheet, with New View Category, Display, Rename &amp; Renumber View, Set Category and the View Category dialog. The views are
    /// the sheet views of the set (<see cref="CadSheetSetSheet.Views"/>); a plain viewport of a layout is not one.
    /// </summary>
    public sealed partial class SheetSetManagerViewModel
    {
        internal const string GuideSheetViews =
            "https://nguyenthanguth.github.io/opencad2d/guides/presenter/sheet-sets.html#the-sheet-views-tab";

        private const string SheetViewsTab = "Sheet Views";

        private readonly SheetSetTabTree _viewsTree = new SheetSetTabTree();
        private readonly Dictionary<string, string> _scaleCache = new Dictionary<string, string>(
            StringComparer.Ordinal
        );
        private bool _byCategory = true;
        private (string Path, string View)? _pendingDisplay;

        /// <summary>Gets the rows of the Sheet Views tab that are shown, in order: the set, then its categories or its sheets, then the views.</summary>
        public ObservableCollection<SheetSetTabRow> ViewRows => _viewsTree.Rows;

        /// <summary>Gets the rows selected in the Sheet Views tab, in the order of the tree.</summary>
        public IReadOnlyList<SheetSetTabRow> SelectedViewRows => _viewsTree.Selected;

        /// <summary>Gets or sets whether the Sheet Views tab lists the views under their categories (View by Category, the start) and not under their sheets.</summary>
        public bool ViewsByCategory
        {
            get => _byCategory;
            set
            {
                if (_byCategory != value)
                {
                    _byCategory = value;
                    Raise(nameof(ViewsByCategory));
                    Raise(nameof(ViewsBySheet));
                    RebuildTabs();
                }
            }
        }

        /// <summary>Gets or sets whether the Sheet Views tab lists the views under the sheets they are on (View by Sheet).</summary>
        public bool ViewsBySheet
        {
            get => !_byCategory;
            set => ViewsByCategory = !value;
        }

        /// <summary>Gets or sets how the palette asks the View Category dialog; the handler shows it and says whether OK was pressed. Null: the dialog is said not to be there.</summary>
        public Func<ViewCategoryDialogViewModel, bool> ShowViewCategoryDialog { get; set; }

        /// <summary>Gets or sets how the palette asks the Rename &amp; Renumber View dialog; the handler shows it and says whether OK was pressed. Null: the dialog is said not to be there.</summary>
        public Func<RenameRenumberViewDialogViewModel, bool> ShowRenameRenumberViewDialog { get; set; }

        // ---- The tabs

        /// <summary>Gets whether the Sheet List tab is the one shown.</summary>
        public bool IsSheetListTab => _tab == SheetListTab;

        /// <summary>Gets whether the Sheet Views tab is the one shown.</summary>
        public bool IsSheetViewsTab => _tab == SheetViewsTab;

        /// <summary>Gets whether the Model Views tab is the one shown.</summary>
        public bool IsModelViewsTab => _tab == ModelViewsTab;

        /// <summary>Gets whether the body of the Sheet Views or the Model Views tab is shown: a set is open and the Sheet List tab is not the one shown.</summary>
        public bool ShowTabBody => _current != null && _tab != SheetListTab;

        /// <summary>Gets what the Sheet Views tab says where the set has no sheet view; empty where it has some.</summary>
        public string ViewsHint =>
            _viewsTree.Root == null || ViewCount(_viewsTree.Root) > 0
                ? string.Empty
                : "No sheet views yet."
                    + Environment.NewLine
                    + "Place on Sheet in the Model Views tab puts a view on a sheet.";

        /// <summary>Gets what the Model Views tab says where the set has no resource location; empty where it has some.</summary>
        public string ModelsHint =>
            _modelTree.Root == null || _modelTree.Root.Children.Count > 0
                ? string.Empty
                : "The sheet set has no resource locations."
                    + Environment.NewLine
                    + "Add New Location picks a folder of drawings.";

        /// <summary>The rows selected or shown in a tab's tree changed: the Details pane says what the first row selected is, where that tab is the one shown.</summary>
        private void OnTabTreeChanged(object sender, EventArgs e)
        {
            if ((sender == _viewsTree && _tab == SheetViewsTab) || (sender == _modelTree && _tab == ModelViewsTab))
            {
                UpdateDetails();
            }
        }

        private void OnTabChanged()
        {
            Raise(nameof(IsSheetListTab));
            Raise(nameof(IsSheetViewsTab));
            Raise(nameof(IsModelViewsTab));
            Raise(nameof(ShowTabBody));
            RebuildTabs();
            UpdateDetails();
        }

        /// <summary>Builds the tree of the tab that is shown from the set again (the other is let go until it is shown).</summary>
        private void RebuildTabs()
        {
            BuildViews();
            BuildModels();
            Raise(nameof(ViewsHint));
            Raise(nameof(ModelsHint));
        }

        // ---- The tree

        private void BuildViews()
        {
            if (_current == null || _tab != SheetViewsTab)
            {
                _viewsTree.Load(null);
                return;
            }

            CadSheetSet set = _current.SheetSet;
            SheetSetTabRow root = _viewsTree.Row(SheetSetTabRowKind.SheetSet, "set", "set", null);
            root.Text = set.Name;
            root.ComponentId = set.Id;
            var reads = new Dictionary<string, DxfDatabase>(StringComparer.OrdinalIgnoreCase);
            List<(CadSheetSetSheet Sheet, CadSheetView View)> views = set.AllSheets()
                .SelectMany(sheet => sheet.Views.Select(view => (sheet, view)))
                .ToList();

            if (_byCategory)
            {
                // A category with no name is the blank one a new set holds: its views sit under the set, as those with none do.
                List<CadViewCategory> categories = set.ViewCategories.Where(c => c.Name.Length > 0).ToList();

                foreach (CadViewCategory category in categories)
                {
                    SheetSetTabRow row = _viewsTree.Row(
                        SheetSetTabRowKind.Category,
                        "cat:" + category.Id,
                        "category",
                        root
                    );
                    row.Text = category.Name;
                    row.ComponentId = category.Id;
                    root.Add(row);

                    foreach (var pair in views.Where(v => v.View.Category?.Id == category.Id))
                    {
                        row.Add(ViewRow(pair.Sheet, pair.View, row, pair.Sheet.Number, reads));
                    }
                }

                foreach (
                    var pair in views.Where(v =>
                    {
                        string id = v.View.Category?.Id;
                        return id == null || !categories.Any(c => c.Id == id);
                    })
                )
                {
                    root.Add(ViewRow(pair.Sheet, pair.View, root, pair.Sheet.Number, reads));
                }
            }
            else
            {
                foreach (CadSheetSetSheet sheet in views.Select(v => v.Sheet).Distinct())
                {
                    SheetSetTabRow row = _viewsTree.Row(SheetSetTabRowKind.Sheet, "sheet:" + sheet.Id, "sheet", root);
                    row.Text = Joined(sheet.Number, sheet.Title);
                    row.ComponentId = sheet.Id;
                    root.Add(row);

                    foreach (CadSheetView view in sheet.Views)
                    {
                        row.Add(ViewRow(sheet, view, row, ScaleOfView(view, reads), reads));
                    }
                }
            }

            _viewsTree.Load(root);
        }

        private SheetSetTabRow ViewRow(
            CadSheetSetSheet sheet,
            CadSheetView view,
            SheetSetTabRow parent,
            string detail,
            Dictionary<string, DxfDatabase> reads
        )
        {
            SheetSetTabRow row = _viewsTree.Row(SheetSetTabRowKind.View, "view:" + view.Id, "view", parent);
            row.Text = Joined(view.Number, view.Title);
            row.Detail = detail ?? string.Empty;
            row.ComponentId = view.Id;
            row.ViewName = view.NamedView?.Name;
            return row;
        }

        private static string Joined(string number, string title) =>
            string.IsNullOrEmpty(number) ? title : number + " - " + title;

        /// <summary>The scale a sheet view was placed at, read from the sheet's drawing (the one open in the app, else the file, kept until it changes); empty where there is none to read.</summary>
        private string ScaleOfView(CadSheetView view, Dictionary<string, DxfDatabase> reads)
        {
            CadDrawingReference named = view.NamedView;
            string path = named?.File.Resolve(_current.Folder);

            if (named?.Name == null || path == null)
            {
                return string.Empty;
            }

            try
            {
                CadDocument open = _main.OpenDocumentOf(path);

                if (open != null)
                {
                    return CadSheetSetPlacement.ScaleOf(open.Database, named.Name);
                }

                var info = new FileInfo(path);
                string key = path + "|" + named.Name + "|" + info.LastWriteTimeUtc.Ticks + "|" + info.Length;

                if (_scaleCache.TryGetValue(key, out string kept))
                {
                    return kept;
                }

                if (!reads.TryGetValue(path, out DxfDatabase database))
                {
                    database = CadDrawingFile.Open(path);
                    reads[path] = database;
                }

                string scale = CadSheetSetPlacement.ScaleOf(database, named.Name);
                _scaleCache[key] = scale;
                return scale;
            }
            catch (Exception ex) when (IsFileProblem(ex))
            {
                return string.Empty;
            }
        }

        // ---- The details

        private void DescribeTab()
        {
            if (_current == null)
            {
                return;
            }

            if (_tab == SheetViewsTab)
            {
                DescribeSheetView(_viewsTree.Selected.FirstOrDefault());
            }
            else
            {
                DescribeModelView(_modelTree.Selected.FirstOrDefault());
            }
        }

        private void DescribeSheetView(SheetSetTabRow row)
        {
            if (row == null)
            {
                return;
            }

            CadSheetSet set = _current.SheetSet;

            switch (row.Kind)
            {
                case SheetSetTabRowKind.SheetSet:
                    Details.Add(new SheetSetDetailRow("Sheet set", set.Name));
                    Details.Add(
                        new SheetSetDetailRow(
                            "Categories",
                            set.ViewCategories.Count(c => c.Name.Length > 0).ToString(CultureInfo.InvariantCulture)
                        )
                    );
                    Details.Add(new SheetSetDetailRow("Views", ViewCount(row).ToString(CultureInfo.InvariantCulture)));
                    break;
                case SheetSetTabRowKind.Category:
                    Details.Add(new SheetSetDetailRow("Category", row.Text));
                    Details.Add(new SheetSetDetailRow("Views", ViewCount(row).ToString(CultureInfo.InvariantCulture)));
                    string description = set.ViewCategories.FirstOrDefault(c => c.Id == row.ComponentId)?.Description;

                    if (!string.IsNullOrEmpty(description))
                    {
                        Details.Add(new SheetSetDetailRow("Description", description));
                    }

                    break;
                case SheetSetTabRowKind.Sheet:
                    Details.Add(new SheetSetDetailRow("Sheet", row.Text));
                    Details.Add(new SheetSetDetailRow("Views", ViewCount(row).ToString(CultureInfo.InvariantCulture)));
                    break;
                default:
                    (CadSheetSetSheet sheet, CadSheetView view)? found = FindView(set, row.ComponentId);

                    if (found == null)
                    {
                        break;
                    }

                    CadViewCategory category = found.Value.view.Category;
                    var reads = new Dictionary<string, DxfDatabase>(StringComparer.OrdinalIgnoreCase);
                    Details.Add(new SheetSetDetailRow("View", row.Text));
                    Details.Add(
                        new SheetSetDetailRow("Sheet", Joined(found.Value.sheet.Number, found.Value.sheet.Title))
                    );
                    Details.Add(
                        new SheetSetDetailRow(
                            "Category",
                            category == null || category.Name.Length == 0 ? "None" : category.Name
                        )
                    );
                    Details.Add(new SheetSetDetailRow("Scale", ScaleOfView(found.Value.view, reads)));
                    break;
            }
        }

        private static int ViewCount(SheetSetTabRow row) =>
            SheetSetTabTree.Walk(row).Count(r => r.Kind == SheetSetTabRowKind.View);

        // ---- Finding parts again

        /// <summary>A sheet view of the set and the sheet it is on, found by the view's ID; null where the set has none.</summary>
        private static (CadSheetSetSheet sheet, CadSheetView view)? FindView(CadSheetSet set, string id)
        {
            foreach (CadSheetSetSheet sheet in set.AllSheets())
            {
                foreach (CadSheetView view in sheet.Views)
                {
                    if (view.Id == id)
                    {
                        return (sheet, view);
                    }
                }
            }

            return null;
        }

        private static CadViewCategory FindCategory(CadSheetSet set, string id) =>
            set.ViewCategories.FirstOrDefault(c => c.Id == id)
            ?? throw new InvalidOperationException(
                "The view category is no longer in the sheet set: refresh the palette."
            );

        // ---- The operations

        /// <summary>Why a name cannot be a view category's: it is empty, or another category of the set has it (letter case ignored); null where it is good.</summary>
        /// <param name="name">The name, without the spaces at its ends.</param>
        /// <param name="exceptId">The ID of the category being renamed, which may keep its name; null for a new one.</param>
        /// <returns>The words; null where the name is good.</returns>
        public string WhyNotCategoryName(string name, string exceptId = null)
        {
            if (string.IsNullOrWhiteSpace(name))
            {
                return "A view category is named.";
            }

            if (
                _current != null
                && _current.SheetSet.ViewCategories.Any(c =>
                    c.Id != exceptId && string.Equals(c.Name, name.Trim(), StringComparison.OrdinalIgnoreCase)
                )
            )
            {
                return "The sheet set has a view category named " + name.Trim() + ".";
            }

            return null;
        }

        /// <summary>Adds a view category, as New View Category does, after the ones the set has.</summary>
        /// <param name="name">The name; not empty, and no other category has it.</param>
        /// <param name="description">The description, or null for none.</param>
        /// <returns>Whether it was added; why not is said on the command line.</returns>
        public bool NewViewCategory(string name, string description = null)
        {
            string why = WhyNotCategoryName(name);

            if (why != null)
            {
                _main.Say(why);
                return false;
            }

            return Change(set => set.AddViewCategory(name.Trim(), description));
        }

        /// <summary>Renames a view category, as Rename or Properties… of a category does.</summary>
        /// <param name="category">The category's row.</param>
        /// <param name="name">The new name; not empty, and no other category has it.</param>
        /// <returns>Whether it was renamed; why not is said on the command line.</returns>
        /// <exception cref="ArgumentNullException">Thrown when the row is null.</exception>
        public bool RenameViewCategory(SheetSetTabRow category, string name)
        {
            if (category == null)
            {
                throw new ArgumentNullException(nameof(category));
            }

            string why = WhyNotCategoryName(name, category.ComponentId);

            if (why != null)
            {
                _main.Say(why);
                return false;
            }

            return Change(set => FindCategory(set, category.ComponentId).Name = name.Trim());
        }

        /// <summary>Takes a view category out, as Remove Category does. A category that still holds views is not removed.</summary>
        /// <param name="category">The category's row.</param>
        /// <returns>Whether it was removed; why not is said on the command line.</returns>
        /// <exception cref="ArgumentNullException">Thrown when the row is null.</exception>
        public bool RemoveViewCategory(SheetSetTabRow category)
        {
            if (category == null)
            {
                throw new ArgumentNullException(nameof(category));
            }

            if (category.Children.Count > 0)
            {
                _main.Say("The view category " + category.Text + " holds views: move them to another category first.");
                return false;
            }

            return Change(set => set.RemoveViewCategory(FindCategory(set, category.ComponentId)));
        }

        /// <summary>Puts sheet views in a category, as Set Category and a drag onto a category do.</summary>
        /// <param name="views">The rows of the views.</param>
        /// <param name="category">The category's row; null, or the set's row, takes them out of any category.</param>
        /// <returns>Whether the set was changed.</returns>
        /// <exception cref="ArgumentNullException">Thrown when the rows are null.</exception>
        public bool SetCategory(IEnumerable<SheetSetTabRow> views, SheetSetTabRow category)
        {
            if (views == null)
            {
                throw new ArgumentNullException(nameof(views));
            }

            List<string> ids = views.Where(v => v.Kind == SheetSetTabRowKind.View).Select(v => v.ComponentId).ToList();
            string categoryId =
                category != null && category.Kind == SheetSetTabRowKind.Category ? category.ComponentId : null;

            if (ids.Count == 0)
            {
                return false;
            }

            return Change(set =>
            {
                CadViewCategory target = categoryId == null ? null : FindCategory(set, categoryId);

                foreach (string id in ids)
                {
                    var found = FindView(set, id);

                    if (found != null)
                    {
                        found.Value.view.Category = target;
                    }
                }
            });
        }

        /// <summary>Why a view cannot take a title: it is empty; null where it is good.</summary>
        /// <param name="title">The title.</param>
        /// <returns>The words; null where it is good.</returns>
        public static string WhyNotViewTitle(string title) =>
            string.IsNullOrWhiteSpace(title) ? "A view has a title." : null;

        /// <summary>Renames and renumbers a sheet view, as Rename &amp; Renumber View does.</summary>
        /// <param name="view">The view's row.</param>
        /// <param name="number">The new number; empty for none.</param>
        /// <param name="title">The new title; not empty.</param>
        /// <returns>Whether it was changed; why not is said on the command line.</returns>
        /// <exception cref="ArgumentNullException">Thrown when the row is null.</exception>
        public bool RenameAndRenumberView(SheetSetTabRow view, string number, string title)
        {
            if (view == null)
            {
                throw new ArgumentNullException(nameof(view));
            }

            return RenameAndRenumberView(view.ComponentId, number, title);
        }

        private bool RenameAndRenumberView(string id, string number, string title)
        {
            string why = WhyNotViewTitle(title);

            if (why != null)
            {
                _main.Say(why);
                return false;
            }

            return Change(set =>
            {
                var found = FindView(set, id)
                    ?? throw new InvalidOperationException(
                        "The view is no longer in the sheet set: refresh the palette."
                    );
                found.view.Number = (number ?? string.Empty).Trim();
                found.view.Title = title.Trim();
            });
        }

        /// <summary>
        /// Shows a sheet view, as Display does: the sheet's drawing is opened (or shown where it is open) on the sheet's layout, and
        /// the named view the sheet view points at is shown. A sheet that is missing is not opened.
        /// </summary>
        /// <param name="view">The view's row.</param>
        /// <returns>Whether the drawing was asked to open.</returns>
        /// <exception cref="ArgumentNullException">Thrown when the row is null.</exception>
        public bool DisplayView(SheetSetTabRow view)
        {
            if (view == null)
            {
                throw new ArgumentNullException(nameof(view));
            }

            if (_current == null || view.Kind != SheetSetTabRowKind.View)
            {
                return false;
            }

            var found = FindView(_current.SheetSet, view.ComponentId);
            SheetSetNodeViewModel sheet =
                found == null ? null : AllSheets().FirstOrDefault(s => s.Id == found.Value.sheet.Id);

            if (sheet == null)
            {
                return false;
            }

            if (sheet.IsMissing)
            {
                _main.Say("The sheet " + sheet.Text + " is not found: there is no drawing to show the view in.");
                return false;
            }

            string name = found.Value.view.NamedView?.Name;
            _pendingDisplay = name == null ? ((string, string)?)null : (sheet.Found.DrawingFile, name);
            OpenDrawing(sheet.Found.DrawingFile, sheet.Found.LayoutName);
            ApplyPendingDisplay(sheet.Found.DrawingFile);
            return true;
        }

        /// <summary>Shows the named view of a Display once its drawing is the one shown in the app: the layout of the sheet, the view restored.</summary>
        private void ApplyPendingDisplay(string path)
        {
            if (_pendingDisplay == null || !SamePath(_pendingDisplay.Value.Path, path))
            {
                return;
            }

            CadDocument document = _main.OpenDocumentOf(path);

            if (document == null || _main.Document != document)
            {
                return;
            }

            string name = _pendingDisplay.Value.View;
            _pendingDisplay = null;
            _main.StartSheetSetCommand(new CadCommand("DISPLAYVIEW", c => RestoreBody(c, name)));
        }

        private static IEnumerable<CadPrompt> RestoreBody(CadCommandContext c, string name)
        {
            CadNamedViews.Restore(c, name);
            yield break;
        }

        // ---- The dialogs

        /// <summary>Asks the View Category dialog for a new category and adds it, as New View Category… does.</summary>
        /// <returns>Whether a category was added.</returns>
        public bool NewViewCategoryDialog()
        {
            if (_current == null)
            {
                return false;
            }

            var dialog = new ViewCategoryDialogViewModel(string.Empty, name => WhyNotCategoryName(name));

            if (!AskViewCategory(dialog))
            {
                return false;
            }

            return NewViewCategory(dialog.Result);
        }

        /// <summary>Asks the View Category dialog on a category and renames it, as Rename and Properties… of a category do.</summary>
        /// <param name="category">The category's row.</param>
        /// <returns>Whether the category was renamed.</returns>
        /// <exception cref="ArgumentNullException">Thrown when the row is null.</exception>
        public bool EditViewCategoryDialog(SheetSetTabRow category)
        {
            if (category == null)
            {
                throw new ArgumentNullException(nameof(category));
            }

            var dialog = new ViewCategoryDialogViewModel(category.Text, name =>
                WhyNotCategoryName(name, category.ComponentId)
            );

            if (!AskViewCategory(dialog))
            {
                return false;
            }

            return dialog.Result == category.Text || RenameViewCategory(category, dialog.Result);
        }

        private bool AskViewCategory(ViewCategoryDialogViewModel dialog)
        {
            if (ShowViewCategoryDialog == null)
            {
                _main.Say("The View Category dialog is shown by the window.");
                return false;
            }

            return ShowViewCategoryDialog(dialog) && dialog.Accepted;
        }

        /// <summary>Asks the Rename &amp; Renumber View dialog on a view, which walks the views in the order of the tree with Previous and Next, as AutoCAD's does.</summary>
        /// <param name="view">The view's row it opens on.</param>
        /// <returns>Whether OK was pressed.</returns>
        /// <exception cref="ArgumentNullException">Thrown when the row is null.</exception>
        public bool RenameAndRenumberViewDialog(SheetSetTabRow view)
        {
            if (view == null)
            {
                throw new ArgumentNullException(nameof(view));
            }

            if (_current == null || _viewsTree.Root == null)
            {
                return false;
            }

            if (ShowRenameRenumberViewDialog == null)
            {
                _main.Say("The Rename & Renumber View dialog is shown by the window.");
                return false;
            }

            CadSheetSet set = _current.SheetSet;
            var entries = new List<SheetViewEntry>();

            foreach (
                SheetSetTabRow row in SheetSetTabTree
                    .Walk(_viewsTree.Root)
                    .Where(r => r.Kind == SheetSetTabRowKind.View)
            )
            {
                var found = FindView(set, row.ComponentId);

                if (found != null)
                {
                    entries.Add(
                        new SheetViewEntry(
                            found.Value.view.Id,
                            found.Value.view.Number,
                            found.Value.view.Title,
                            Joined(found.Value.sheet.Number, found.Value.sheet.Title)
                        )
                    );
                }
            }

            int index = entries.FindIndex(e => e.Id == view.ComponentId);

            if (index < 0)
            {
                return false;
            }

            var dialog = new RenameRenumberViewDialogViewModel(entries, index, (id, number, title) =>
                RenameAndRenumberView(id, number, title)
            );
            return ShowRenameRenumberViewDialog(dialog);
        }

        // ---- The menus

        /// <summary>
        /// Makes the right-click menu of a row of the Sheet Views tab: for a view Display, New View Category…, Place View Label
        /// Block (greyed: label blocks are left out), Rename &amp; Renumber…, Set Category ▸ and Properties… (greyed); for a category
        /// New View Category…, Rename…, Remove Category (greyed while it holds views) and Properties…; for a sheet Open; for the set
        /// New View Category…. Changes are greyed in a locked set.
        /// </summary>
        /// <param name="row">The row the menu is opened on; null for a click on no row.</param>
        /// <returns>The items; empty where no set is open.</returns>
        public IReadOnlyList<CadMenuItemViewModel> SheetViewsMenuFor(SheetSetTabRow row)
        {
            if (_current == null)
            {
                return Array.Empty<CadMenuItemViewModel>();
            }

            bool change = !IsLocked;
            CadMenuItemViewModel newCategory = Item("New View Category…", () => NewViewCategoryDialog(), change);

            if (row == null || row.Kind == SheetSetTabRowKind.SheetSet)
            {
                return new List<CadMenuItemViewModel> { newCategory };
            }

            if (row.Kind == SheetSetTabRowKind.Category)
            {
                return new List<CadMenuItemViewModel>
                {
                    newCategory,
                    Item("Rename…", () => EditViewCategoryDialog(row), change),
                    Item("Remove Category", () => RemoveViewCategory(row), change && row.Children.Count == 0),
                    CadMenuItemViewModel.Separator(),
                    Item("Properties…", () => EditViewCategoryDialog(row), change),
                };
            }

            if (row.Kind == SheetSetTabRowKind.Sheet)
            {
                SheetSetNodeViewModel sheet = AllSheets().FirstOrDefault(s => s.Id == row.ComponentId);
                return new List<CadMenuItemViewModel>
                {
                    Item(
                        "Open",
                        () => OpenSheets(new[] { sheet }, false),
                        sheet != null && !sheet.IsMissing,
                        isDefault: true
                    ),
                    CadMenuItemViewModel.Separator(),
                    newCategory,
                };
            }

            List<SheetSetTabRow> chosen = _viewsTree.Selected.Where(r => r.Kind == SheetSetTabRowKind.View).ToList();

            if (!chosen.Contains(row))
            {
                chosen = new List<SheetSetTabRow> { row };
            }

            CadViewCategory current = FindView(_current.SheetSet, row.ComponentId)?.view.Category;
            var categories = _current
                .SheetSet.ViewCategories.Where(c => c.Name.Length > 0)
                .Select(c =>
                {
                    string id = c.Id;
                    return Item(
                        c.Name,
                        () => SetCategoryById(chosen, id),
                        change,
                        isChecked: current != null && current.Id == c.Id ? (bool?)true : null
                    );
                })
                .ToList();
            return new List<CadMenuItemViewModel>
            {
                Item("Display", () => DisplayView(row), true, isDefault: true),
                CadMenuItemViewModel.Separator(),
                newCategory,
                new CadMenuItemViewModel(
                    "Place View Label Block",
                    () => { },
                    false,
                    "Label blocks are left out of this version."
                ),
                CadMenuItemViewModel.Separator(),
                Item("Rename & Renumber…", () => RenameAndRenumberViewDialog(row), change),
                new CadMenuItemViewModel(
                    "Set Category",
                    change && categories.Count > 0,
                    categories.Count > 0
                        ? categories
                        : new List<CadMenuItemViewModel> { Item("None", () => { }, false) }
                ),
                CadMenuItemViewModel.Separator(),
                Item("Properties…", () => { }, false),
            };
        }

        private bool SetCategoryById(IEnumerable<SheetSetTabRow> views, string categoryId)
        {
            List<string> ids = views.Where(v => v.Kind == SheetSetTabRowKind.View).Select(v => v.ComponentId).ToList();
            return Change(set =>
            {
                CadViewCategory target = FindCategory(set, categoryId);

                foreach (string id in ids)
                {
                    var found = FindView(set, id);

                    if (found != null)
                    {
                        found.Value.view.Category = target;
                    }
                }
            });
        }
    }
}
