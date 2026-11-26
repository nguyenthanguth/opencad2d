using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using GeometryHelper.Dxf;
using OpenCAD2D.SheetSets;

namespace OpenCAD2D.Wpf
{
    /// <summary>A choice of the Data type list of a column: what the column shows of each sheet.</summary>
    public sealed class SheetListDataChoice
    {
        internal SheetListDataChoice(string name, CadSheetListData data, string customName)
        {
            Name = name;
            Data = data;
            CustomName = customName;
        }

        /// <summary>Gets the words the list shows: "Sheet Number", "Revision number", or the name of a custom property of the sheets ("Drawn By").</summary>
        public string Name { get; }

        /// <summary>Gets what the column shows.</summary>
        public CadSheetListData Data { get; }

        /// <summary>Gets the name of the custom property of a <see cref="CadSheetListData.CustomProperty"/> choice; empty for any other.</summary>
        public string CustomName { get; }

        /// <summary>Gets the heading a column of this choice has until one is typed: the choice's own words, as the table's engine gives them.</summary>
        internal string DefaultHeading =>
            Data == CadSheetListData.CustomProperty ? CustomName : CadSheetListColumn.Of(Data).Heading;

        /// <inheritdoc />
        public override string ToString() => Name;
    }

    /// <summary>
    /// A line of the Table Data tab's list: the Data type of a column and the heading text above it. The heading follows the
    /// data type (Sheet Number, Revision Number) until it is typed in, and then stays as typed.
    /// </summary>
    public sealed class SheetListColumnRow : ObservableObject
    {
        private readonly SheetListTableViewModel _owner;
        private SheetListDataChoice _dataType;
        private string _heading;
        private bool _typed;
        private bool _isSelected;

        internal SheetListColumnRow(SheetListTableViewModel owner, SheetListDataChoice dataType, string heading)
        {
            _owner = owner;
            _dataType = dataType;
            _heading = heading ?? dataType.DefaultHeading;
            _typed = !string.Equals(_heading, dataType.DefaultHeading, StringComparison.Ordinal);
        }

        /// <summary>Gets or sets what the column shows; its heading follows it unless one was typed.</summary>
        public SheetListDataChoice DataType
        {
            get => _dataType;
            set
            {
                if (value != null && Set(ref _dataType, value))
                {
                    if (!_typed)
                    {
                        _heading = value.DefaultHeading;
                        Raise(nameof(Heading));
                    }

                    _owner.ColumnsChanged();
                }
            }
        }

        /// <summary>Gets or sets the heading text above the column.</summary>
        public string Heading
        {
            get => _heading;
            set
            {
                string text = value ?? string.Empty;

                if (Set(ref _heading, text))
                {
                    _typed = !string.Equals(text, _dataType.DefaultHeading, StringComparison.Ordinal);
                    _owner.ColumnsChanged();
                }
            }
        }

        /// <summary>Gets or sets whether the line is the one selected: its Data type and heading are then edited in place.</summary>
        public bool IsSelected
        {
            get => _isSelected;
            set
            {
                if (Set(ref _isSelected, value) && value)
                {
                    _owner.SelectedColumn = this;
                }
            }
        }
    }

    /// <summary>
    /// A line of the Subsets and Sheets tab's tree: the sheet set, a subset or a sheet, with a check box. Checking a subset checks
    /// everything in it; a subset with some of its parts checked shows as mixed (<see cref="IsChecked"/> null).
    /// </summary>
    public sealed class SheetListCheckNode : ObservableObject
    {
        private readonly SheetListTableViewModel _owner;
        private readonly List<SheetListCheckNode> _children = new List<SheetListCheckNode>();
        private bool? _checked = true;
        private bool _isExpanded = true;

        internal SheetListCheckNode(
            SheetListTableViewModel owner,
            SheetListCheckNode parent,
            string id,
            string text,
            SheetSetNodeKind kind
        )
        {
            _owner = owner;
            Parent = parent;
            Id = id;
            Text = text;
            Kind = kind;
            Depth = parent == null ? 0 : parent.Depth + 1;
        }

        /// <summary>Gets the part's ID in the set.</summary>
        public string Id { get; }

        /// <summary>Gets the words of the line: the set's name, a subset's name, or "Number - Title" of a sheet.</summary>
        public string Text { get; }

        /// <summary>Gets whether the line is the set, a subset or a sheet.</summary>
        public SheetSetNodeKind Kind { get; }

        /// <summary>Gets how deep the line is: 0 for the set.</summary>
        public int Depth { get; }

        /// <summary>Gets the line above this one; null for the set.</summary>
        public SheetListCheckNode Parent { get; }

        /// <summary>Gets the lines in this one, in set order; none for a sheet.</summary>
        public IReadOnlyList<SheetListCheckNode> Children => _children;

        /// <summary>Gets whether the line has lines under it.</summary>
        public bool HasChildren => _children.Count > 0;

        /// <summary>Gets the space before the arrow of the line, 16 for each level below the set.</summary>
        public double Indent => Depth * 16.0;

        /// <summary>Gets the picture drawn before the words (a <c>SheetSetIcon</c> glyph).</summary>
        public string Icon =>
            Kind == SheetSetNodeKind.SheetSet ? "set"
            : Kind == SheetSetNodeKind.Subset ? "subset"
            : "sheet";

        /// <summary>Gets the arrow of the line, a <c>SheetSetIcon</c> glyph: down where it is open, right where it is closed.</summary>
        public string Chevron => _isExpanded ? "chevron-down" : "chevron-right";

        /// <summary>Gets or sets whether the lines under this one are shown.</summary>
        public bool IsExpanded
        {
            get => _isExpanded;
            set
            {
                if (Set(ref _isExpanded, value))
                {
                    Raise(nameof(Chevron));
                    _owner.ShowRows();
                }
            }
        }

        /// <summary>
        /// Gets or sets whether the part is listed: true or false; null where only some of what is under a subset is (it can only
        /// be read). Setting a value checks or unchecks everything under the line, and the lines above show as checked, unchecked
        /// or mixed to match.
        /// </summary>
        public bool? IsChecked
        {
            get => _checked;
            set
            {
                if (value.HasValue)
                {
                    Cascade(value.Value);
                    Parent?.Recompute();
                    _owner.CheckedChanged();
                }
            }
        }

        internal void Add(SheetListCheckNode child) => _children.Add(child);

        /// <summary>Sets the line and everything under it, with no word to the owner: the owner is told once, by the caller.</summary>
        internal void Cascade(bool value)
        {
            Show(value);

            foreach (SheetListCheckNode child in _children)
            {
                child.Cascade(value);
            }
        }

        /// <summary>Works the line's own check out again from the lines under it, and the lines above it after.</summary>
        internal void Recompute()
        {
            if (_children.Count > 0)
            {
                Show(
                    _children.All(c => c._checked == true) ? true
                    : _children.All(c => c._checked == false) ? false
                    : (bool?)null
                );
            }

            Parent?.Recompute();
        }

        private void Show(bool? value)
        {
            if (_checked != value)
            {
                _checked = value;
                Raise(nameof(IsChecked));
            }
        }
    }

    /// <summary>
    /// Insert Sheet List Table, as the board A12SheetListTable draws it, and Edit Sheet List Table Settings, the same dialog on the
    /// settings a table keeps. Two tabs beside the table style (its name, "..." to the Table Style dialog, a preview) and Include
    /// subsets as headers in output. Table Data: the Title text, and one line a column (top to bottom is left to right) with its
    /// Data type, from the list of what a sheet shows (Sheet Number, Sheet Title, Sheet Description, Subset name, Revision number
    /// and date, Issue purpose, Category, Drawing file name, Layout name, and the custom properties the sheets hold) and its
    /// heading, with Add (a Sheet Number column at the end), Remove, Move Up and Move Down. Subsets and Sheets: the tree of the
    /// set, populated from it, each part checked or not; Populate From Sheet Selection is shown greyed, as sheet selections are
    /// left out. A table with no column, or no sheet checked, is refused in the dialog's words with OK greyed. The dialog changes
    /// nothing itself: OK is answered with <see cref="Settings"/>, which the caller draws the table by. Where the dialog edits a
    /// table the style is fixed, as it is the table's own.
    /// </summary>
    public sealed class SheetListTableViewModel : SheetSetDialogViewModel
    {
        private static readonly (CadSheetListData Data, string Name)[] Standard =
        {
            (CadSheetListData.SheetNumber, "Sheet Number"),
            (CadSheetListData.SheetTitle, "Sheet Title"),
            (CadSheetListData.SheetDescription, "Sheet Description"),
            (CadSheetListData.SubsetName, "Subset name"),
            (CadSheetListData.RevisionNumber, "Revision number"),
            (CadSheetListData.RevisionDate, "Revision date"),
            (CadSheetListData.IssuePurpose, "Issue purpose"),
            (CadSheetListData.Category, "Category"),
            (CadSheetListData.DrawingFileName, "Drawing file name"),
            (CadSheetListData.LayoutName, "Layout name"),
        };

        private readonly CadDocument _document;
        private readonly List<SheetListCheckNode> _nodes = new List<SheetListCheckNode>();
        private readonly List<SheetListDataChoice> _dataTypes = new List<SheetListDataChoice>();
        private string _tableStyle;
        private string _titleText;
        private bool _includeSubsetHeaders;
        private SheetListColumnRow _selected;
        private bool _subsetsTab;

        /// <summary>Makes the dialog over a set and the drawing the table goes into.</summary>
        /// <param name="file">The sheet set's file.</param>
        /// <param name="document">The drawing; its table styles are listed.</param>
        /// <param name="settings">The settings of a table to edit; null for a new table, which starts with the engine's own.</param>
        /// <param name="tableStyle">The name of the table's style where one is edited; null for the drawing's current style.</param>
        /// <exception cref="ArgumentNullException">Thrown when the file or the document is null.</exception>
        public SheetListTableViewModel(
            CadSheetSetFile file,
            CadDocument document,
            CadSheetListTableSettings settings = null,
            string tableStyle = null
        )
            : base(file, null)
        {
            _document = document ?? throw new ArgumentNullException(nameof(document));
            IsEditing = settings != null;
            CadSheetListTableSettings given = settings ?? new CadSheetListTableSettings();
            _titleText = given.Title ?? string.Empty;
            _includeSubsetHeaders = given.IncludeSubsetHeaders;
            _tableStyle = tableStyle ?? CadTableStyleEditor.Current(document.Database);
            ReloadStyles();

            foreach ((CadSheetListData data, string name) in Standard)
            {
                _dataTypes.Add(new SheetListDataChoice(name, data, string.Empty));
            }

            foreach (CadSheetSetProperty property in file.SheetSet.CustomProperties.Where(p => p.IsSheetProperty))
            {
                AddCustomChoice(property.Name);
            }

            foreach (CadSheetListColumn column in given.Columns)
            {
                SheetListDataChoice choice =
                    column.Data == CadSheetListData.CustomProperty
                        ? AddCustomChoice(column.CustomName)
                        : _dataTypes.First(d => d.Data == column.Data);
                Columns.Add(new SheetListColumnRow(this, choice, column.Heading));
            }

            SelectedColumn = Columns.FirstOrDefault();
            BuildTree(file.SheetSet, null);
            Apply(Root, false, given.CheckedIds);
            ShowRows();
            Revalidate();
        }

        /// <summary>Gets the dialog's title: "Insert Sheet List Table", or "Edit Sheet List Table Settings" where a table is edited.</summary>
        public override string Title => IsEditing ? "Edit Sheet List Table Settings" : "Insert Sheet List Table";

        /// <summary>Gets whether the dialog edits the settings of a table that is there, and not those of one to be made.</summary>
        public bool IsEditing { get; }

        /// <summary>Gets the table styles of the drawing; Standard where it has none yet.</summary>
        public ObservableCollection<string> TableStyles { get; } = new ObservableCollection<string>();

        /// <summary>Gets or sets the table style chosen.</summary>
        public string TableStyle
        {
            get => _tableStyle;
            set
            {
                if (value != null && Set(ref _tableStyle, value))
                {
                    Raise(nameof(Preview));
                }
            }
        }

        /// <summary>Gets whether the table style can be chosen: it can for a table to be made, not for one that is edited.</summary>
        public bool CanChooseStyle => !IsEditing;

        /// <summary>Gets the settings of the style chosen, which the preview draws: AutoCAD's Standard where the drawing has no such style yet.</summary>
        public CadTableStyleSettings Preview
        {
            get
            {
                DxfTableStyle style = CadTableStyleEditor.Find(_document.Database, _tableStyle);
                return style == null
                    ? CadTableStyleEditor.Standard(_document.Database)
                    : CadTableStyleSettings.Of(style);
            }
        }

        /// <summary>Gets or sets the Title text, the first row of the table.</summary>
        public string TitleText
        {
            get => _titleText;
            set => Set(ref _titleText, value ?? string.Empty);
        }

        /// <summary>Gets or sets Include subsets as headers in output: a row with the subset's name ahead of its sheets.</summary>
        public bool IncludeSubsetHeaders
        {
            get => _includeSubsetHeaders;
            set => Set(ref _includeSubsetHeaders, value);
        }

        /// <summary>Gets what the Data type list offers a column: the ten things a sheet shows, then the custom properties the sheets hold.</summary>
        public IReadOnlyList<SheetListDataChoice> DataTypes => _dataTypes;

        /// <summary>Gets the columns, left to right.</summary>
        public ObservableCollection<SheetListColumnRow> Columns { get; } =
            new ObservableCollection<SheetListColumnRow>();

        /// <summary>Gets or sets the column selected, the one Remove, Move Up and Move Down act on.</summary>
        public SheetListColumnRow SelectedColumn
        {
            get => _selected;
            set
            {
                if (Set(ref _selected, value))
                {
                    foreach (SheetListColumnRow row in Columns)
                    {
                        row.IsSelected = ReferenceEquals(row, value);
                    }

                    RaiseColumnButtons();
                }
            }
        }

        /// <summary>Gets whether Remove is on: a column is selected.</summary>
        public bool CanRemoveColumn => _selected != null;

        /// <summary>Gets whether Move Up is on: the column selected is not the first.</summary>
        public bool CanMoveUp => _selected != null && Columns.IndexOf(_selected) > 0;

        /// <summary>Gets whether Move Down is on: the column selected is not the last.</summary>
        public bool CanMoveDown => _selected != null && Columns.IndexOf(_selected) < Columns.Count - 1;

        /// <summary>Gets or sets whether the Table Data tab shows (the other tab is Subsets and Sheets).</summary>
        public bool IsTableDataTab
        {
            get => !_subsetsTab;
            set
            {
                if (value && _subsetsTab)
                {
                    _subsetsTab = false;
                    Raise(nameof(IsTableDataTab));
                    Raise(nameof(IsSubsetsTab));
                }
            }
        }

        /// <summary>Gets or sets whether the Subsets and Sheets tab shows.</summary>
        public bool IsSubsetsTab
        {
            get => _subsetsTab;
            set
            {
                if (value && !_subsetsTab)
                {
                    _subsetsTab = true;
                    Raise(nameof(IsTableDataTab));
                    Raise(nameof(IsSubsetsTab));
                }
            }
        }

        /// <summary>Gets the set's line of the tree.</summary>
        public SheetListCheckNode Root => _nodes[0];

        /// <summary>Gets every line of the tree in set order, shown or not: the set, then each subset and sheet under it.</summary>
        public IReadOnlyList<SheetListCheckNode> Nodes => _nodes;

        /// <summary>Gets the lines of the tree that are shown, in order: the lines under a subset that is closed are left out.</summary>
        public ObservableCollection<SheetListCheckNode> Rows { get; } = new ObservableCollection<SheetListCheckNode>();

        /// <summary>Gets what the Populate From Sheet Selection box says: "(none)", as sheet selections are left out.</summary>
        public string PopulateFromSheetSelection => "(none)";

        /// <summary>Gets the items of the Populate From Sheet Selection box: the one, "(none)".</summary>
        public IReadOnlyList<string> PopulateItems => new[] { PopulateFromSheetSelection };

        /// <summary>Gets whether Populate From Sheet Selection is on: never, as sheet selections are left out.</summary>
        public bool CanPopulate => false;

        /// <summary>
        /// Gets the settings the dialog has now, a new set each time: the title, the subsets as headers, the columns, and the parts
        /// checked (null where the whole set is, so that a sheet added later is listed too).
        /// </summary>
        public CadSheetListTableSettings Settings
        {
            get
            {
                var settings = new CadSheetListTableSettings
                {
                    Title = _titleText,
                    IncludeSubsetHeaders = _includeSubsetHeaders,
                    CheckedIds = CheckedIds(),
                };
                settings.Columns.Clear();

                foreach (SheetListColumnRow row in Columns)
                {
                    SheetListDataChoice type = row.DataType;
                    settings.Columns.Add(
                        type.Data == CadSheetListData.CustomProperty
                            ? CadSheetListColumn.Custom(type.CustomName, row.Heading)
                            : CadSheetListColumn.Of(type.Data, row.Heading)
                    );
                }

                return settings;
            }
        }

        /// <summary>Reads the table styles again, as after the Table Style dialog launched from this one; the style chosen kept where it is still there.</summary>
        public void ReloadStyles()
        {
            string kept = _tableStyle;
            TableStyles.Clear();

            foreach (string name in CadTableStyleEditor.Styles(_document.Database))
            {
                TableStyles.Add(name);
            }

            if (!TableStyles.Contains("Standard", StringComparer.OrdinalIgnoreCase))
            {
                TableStyles.Add("Standard");
            }

            TableStyle = TableStyles.FirstOrDefault(s => string.Equals(s, kept, StringComparison.OrdinalIgnoreCase))
                ?? "Standard";
            Raise(nameof(Preview));
        }

        /// <summary>Add: a Sheet Number column at the end, selected.</summary>
        public void AddColumn()
        {
            var row = new SheetListColumnRow(this, _dataTypes[0], null);
            Columns.Add(row);
            SelectedColumn = row;
            ColumnsChanged();
        }

        /// <summary>Remove: the column selected taken away; the one now at its place (else the last) is selected.</summary>
        public void RemoveColumn()
        {
            if (_selected == null)
            {
                return;
            }

            int at = Columns.IndexOf(_selected);
            Columns.RemoveAt(at);
            SelectedColumn = Columns.Count == 0 ? null : Columns[Math.Min(at, Columns.Count - 1)];
            ColumnsChanged();
        }

        /// <summary>Move Up: the column selected one place to the left in the table.</summary>
        public void MoveColumnUp() => MoveColumn(-1);

        /// <summary>Move Down: the column selected one place to the right in the table.</summary>
        public void MoveColumnDown() => MoveColumn(1);

        /// <inheritdoc />
        protected override void Save()
        {
            // The dialog changes nothing itself: the caller reads Settings and TableStyle once OK is answered.
        }

        /// <inheritdoc />
        protected override string Validate()
        {
            if (Columns.Count == 0)
            {
                return "The table needs a column.";
            }

            return _nodes.Any(n => n.Kind == SheetSetNodeKind.Sheet && n.IsChecked == true)
                ? null
                : "No sheet is checked, so the table would list nothing.";
        }

        internal void ColumnsChanged()
        {
            RaiseColumnButtons();
            Revalidate();
        }

        internal void CheckedChanged() => Revalidate();

        /// <summary>Makes the list of the lines shown again, after a subset was opened or closed.</summary>
        internal void ShowRows()
        {
            Rows.Clear();
            Show(Root);
        }

        private void Show(SheetListCheckNode node)
        {
            Rows.Add(node);

            if (node.IsExpanded)
            {
                foreach (SheetListCheckNode child in node.Children)
                {
                    Show(child);
                }
            }
        }

        private void MoveColumn(int step)
        {
            if (_selected == null)
            {
                return;
            }

            int from = Columns.IndexOf(_selected);
            int to = from + step;

            if (to < 0 || to >= Columns.Count)
            {
                return;
            }

            SheetListColumnRow row = _selected;
            Columns.Move(from, to);
            SelectedColumn = row;
            ColumnsChanged();
        }

        private void RaiseColumnButtons()
        {
            Raise(nameof(CanRemoveColumn));
            Raise(nameof(CanMoveUp));
            Raise(nameof(CanMoveDown));
        }

        private SheetListDataChoice AddCustomChoice(string name)
        {
            SheetListDataChoice found = _dataTypes.FirstOrDefault(d =>
                d.Data == CadSheetListData.CustomProperty
                && string.Equals(d.CustomName, name, StringComparison.OrdinalIgnoreCase)
            );

            if (found == null)
            {
                found = new SheetListDataChoice(name, CadSheetListData.CustomProperty, name);
                _dataTypes.Add(found);
            }

            return found;
        }

        // ---- The tree

        private SheetListCheckNode BuildTree(CadSheetSubset group, SheetListCheckNode parent)
        {
            var node = new SheetListCheckNode(
                this,
                parent,
                group.Id,
                group.Name,
                parent == null ? SheetSetNodeKind.SheetSet : SheetSetNodeKind.Subset
            );
            _nodes.Add(node);
            parent?.Add(node);

            foreach (CadSheetComponent child in group.Children)
            {
                if (child is CadSheetSubset subset)
                {
                    BuildTree(subset, node);
                }
                else if (child is CadSheetSetSheet sheet)
                {
                    var line = new SheetListCheckNode(
                        this,
                        node,
                        sheet.Id,
                        string.IsNullOrEmpty(sheet.Number) ? sheet.Title : sheet.Number + " - " + sheet.Title,
                        SheetSetNodeKind.Sheet
                    );
                    _nodes.Add(line);
                    node.Add(line);
                }
            }

            return node;
        }

        /// <summary>Checks the lines the IDs name, and every line under one of those (a checked subset takes in what is under it); null checks every line.</summary>
        private static void Apply(SheetListCheckNode node, bool above, IList<string> ids)
        {
            bool on = above || ids == null || (node.Parent != null && ids.Contains(node.Id));

            if (on)
            {
                node.Cascade(true);
                return;
            }

            if (node.HasChildren)
            {
                foreach (SheetListCheckNode child in node.Children)
                {
                    Apply(child, false, ids);
                }

                node.Recompute();
            }
            else
            {
                node.Cascade(false);
            }
        }

        private IList<string> CheckedIds()
        {
            if (Root.IsChecked == true)
            {
                return null;
            }

            var ids = new List<string>();
            Collect(Root, ids);
            return ids;
        }

        private static void Collect(SheetListCheckNode group, List<string> ids)
        {
            foreach (SheetListCheckNode child in group.Children)
            {
                if (child.IsChecked == true)
                {
                    ids.Add(child.Id);
                }
                else if (child.IsChecked == null)
                {
                    Collect(child, ids);
                }
            }
        }
    }
}
