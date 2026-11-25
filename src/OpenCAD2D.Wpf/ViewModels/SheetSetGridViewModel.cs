using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using OpenCAD2D.SheetSets;

namespace OpenCAD2D.Wpf
{
    /// <summary>What kind of row a <see cref="SheetSetGridRow"/> is in a property grid.</summary>
    public enum SheetSetRowKind
    {
        /// <summary>A heading that groups the rows under it ("Project Control").</summary>
        Section = 0,

        /// <summary>A value typed in a box.</summary>
        Text = 1,

        /// <summary>A value chosen from a drop-down ("Yes" or "No").</summary>
        Choice = 2,

        /// <summary>A value typed in a box, with a "..." button that picks it (a folder, a file, a template layout).</summary>
        Path = 3,
    }

    /// <summary>
    /// A row of the property grid of Sheet Set, Subset and Sheet Properties: a heading, or a label and a value that is typed,
    /// chosen from a short list or picked by a "..." button, or shown grey where it cannot be changed. The row that is clicked
    /// shows its description under the grid.
    /// </summary>
    public sealed class SheetSetGridRow : ObservableObject
    {
        private readonly SheetSetGridViewModel _owner;
        private readonly bool _fixed;
        private readonly Func<bool> _browse;
        private string _text;
        private bool _isSelected;
        private bool _hasError;
        private bool _isRed;

        internal SheetSetGridRow(
            SheetSetGridViewModel owner,
            SheetSetRowKind kind,
            string label,
            string text,
            string description,
            bool isFixed,
            Func<bool> browse,
            IReadOnlyList<string> choices
        )
        {
            _owner = owner;
            Kind = kind;
            Label = label;
            _text = text ?? string.Empty;
            Original = _text;
            Description = description ?? string.Empty;
            _fixed = isFixed;
            _browse = browse;
            Choices = choices ?? new string[0];
        }

        /// <summary>Gets the kind of the row.</summary>
        public SheetSetRowKind Kind { get; }

        /// <summary>Gets the label: "Project number".</summary>
        public string Label { get; }

        /// <summary>Gets what the row says about itself in the pane under the grid when it is clicked.</summary>
        public string Description { get; }

        /// <summary>Gets the names a drop-down row offers; empty for any other.</summary>
        public IReadOnlyList<string> Choices { get; }

        /// <summary>Gets whether the row is a heading.</summary>
        public bool IsSection => Kind == SheetSetRowKind.Section;

        /// <summary>Gets whether the row is a drop-down.</summary>
        public bool IsChoice => Kind == SheetSetRowKind.Choice;

        /// <summary>Gets whether the row draws a box for the value.</summary>
        public bool IsBox => Kind == SheetSetRowKind.Text || Kind == SheetSetRowKind.Path;

        /// <summary>Gets whether the row has its "..." button: a row of a picked value, in a dialog that is not locked and a row the dialog does not hold.</summary>
        public bool HasButton =>
            Kind == SheetSetRowKind.Path && _browse != null && !_owner.IsReadOnly && !_owner.IsRowLocked(this);

        /// <summary>Gets whether the value cannot be typed or chosen: a row shown only, or any row of a locked set.</summary>
        public bool IsReadOnly => !IsSection && (_fixed || _owner.IsReadOnly || _owner.IsRowLocked(this));

        /// <summary>Gets whether the row is drawn grey: a row that is shown only, or any row of a locked set. A value that can only be picked, with its "..." button, is not.</summary>
        public bool IsMuted => IsReadOnly && !HasButton;

        /// <summary>Gets or sets whether the value is drawn in red (a layout that is not found).</summary>
        public bool IsRed
        {
            get => _isRed;
            internal set => Set(ref _isRed, value);
        }

        /// <summary>Gets whether the value is the one the dialog refuses: its box is framed in red.</summary>
        public bool HasError
        {
            get => _hasError;
            internal set => Set(ref _hasError, value);
        }

        /// <summary>Gets or sets whether the row is the one clicked.</summary>
        public bool IsSelected
        {
            get => _isSelected;
            set => Set(ref _isSelected, value);
        }

        /// <summary>Gets or sets the value as the box shows it ("Yes" and "No" for a drop-down).</summary>
        public string Text
        {
            get => _text;
            set
            {
                if (Set(ref _text, value ?? string.Empty))
                {
                    _owner.RowEdited(this);
                }
            }
        }

        /// <summary>Gets the value the row was made with, or read again with: what has not been edited.</summary>
        internal string Original { get; set; }

        /// <summary>Gets whether the value is not what the row was made with.</summary>
        internal bool IsEdited => !string.Equals(_text, Original, StringComparison.Ordinal);

        /// <summary>Runs what the "..." button does, which may set the value.</summary>
        /// <returns>True when a value was picked; false where the button is not there or the pick was cancelled.</returns>
        public bool Browse() => HasButton && _browse();

        /// <summary>Tells the view that whether the row is read-only has changed.</summary>
        internal void RaiseReadOnly()
        {
            Raise(nameof(IsReadOnly));
            Raise(nameof(IsMuted));
            Raise(nameof(HasButton));
        }
    }

    /// <summary>
    /// The base of the three dialogs that are a property grid (Sheet Set, Subset and Sheet Properties): the rows in AutoCAD's
    /// sections, the row that is clicked and its description under the grid, and the pickers the "..." buttons ask for.
    /// </summary>
    public abstract class SheetSetGridViewModel : SheetSetDialogViewModel
    {
        private readonly ObservableCollection<SheetSetGridRow> _rows = new ObservableCollection<SheetSetGridRow>();
        private SheetSetGridRow _selected;

        /// <summary>Makes the view model over a set.</summary>
        /// <param name="file">The sheet set's file.</param>
        /// <param name="options">What the caller gives the dialog; null for none.</param>
        protected SheetSetGridViewModel(CadSheetSetFile file, SheetSetDialogOptions options)
            : base(file, options) { }

        /// <summary>Gets the rows, headings and values, in the order the grid draws them.</summary>
        public IReadOnlyList<SheetSetGridRow> Rows => _rows;

        /// <summary>Gets whether the dialog shows a description pane under the grid.</summary>
        public virtual bool HasDescriptionPane => true;

        /// <summary>Gets or sets the row that was clicked, whose description the pane shows.</summary>
        public SheetSetGridRow SelectedRow
        {
            get => _selected;
            set
            {
                if (_selected == value)
                {
                    return;
                }

                if (_selected != null)
                {
                    _selected.IsSelected = false;
                }

                _selected = value;

                if (_selected != null)
                {
                    _selected.IsSelected = true;
                }

                Raise(nameof(SelectedRow));
                Raise(nameof(DescriptionTitle));
                Raise(nameof(DescriptionText));
            }
        }

        /// <summary>Gets the title of the description pane: the label of the row clicked.</summary>
        public string DescriptionTitle => _selected?.Label ?? string.Empty;

        /// <summary>Gets the text of the description pane: what the row clicked says about itself.</summary>
        public string DescriptionText => _selected?.Description ?? string.Empty;

        /// <summary>Gets or sets what the "..." button of a template row asks: given the layout named now (or null), it returns the layout chosen or null. The window sets it to Select Layout as Sheet Template.</summary>
        public Func<CadDrawingReference, CadDrawingReference> PickTemplate { get; set; }

        /// <summary>Gets a row by its label.</summary>
        /// <param name="label">The label, "Project number".</param>
        /// <returns>The first row of that label.</returns>
        /// <exception cref="InvalidOperationException">Thrown when there is no row of that label.</exception>
        public SheetSetGridRow Row(string label) =>
            _rows.FirstOrDefault(r => string.Equals(r.Label, label, StringComparison.Ordinal))
            ?? throw new InvalidOperationException("The grid has no row \"" + label + "\".");

        /// <summary>Called by a row when its value was edited: checks the values again.</summary>
        /// <param name="row">The row.</param>
        internal void RowEdited(SheetSetGridRow row)
        {
            OnRowEdited(row);
            Revalidate();
        }

        /// <summary>Gets whether the dialog holds a row's value for now, as a folder that is to be made takes the place of a typed one; the row is then read-only and has no button.</summary>
        /// <param name="row">The row.</param>
        /// <returns>True when the row cannot be changed for now.</returns>
        internal bool IsRowLocked(SheetSetGridRow row) => IsLocked(row);

        /// <summary>Gets whether the dialog holds a row's value for now.</summary>
        /// <param name="row">The row.</param>
        /// <returns>True when it does; the default is that no row is.</returns>
        protected virtual bool IsLocked(SheetSetGridRow row) => false;

        /// <summary>Called when a row's value was edited, before the values are checked again; the dialog follows the change (a folder made visible, a row greyed).</summary>
        /// <param name="row">The row.</param>
        protected virtual void OnRowEdited(SheetSetGridRow row) { }

        /// <summary>Adds a heading.</summary>
        /// <param name="label">The heading.</param>
        protected void AddSection(string label) =>
            _rows.Add(new SheetSetGridRow(this, SheetSetRowKind.Section, label, null, null, true, null, null));

        /// <summary>Adds a row of a value typed in a box.</summary>
        /// <param name="label">The label.</param>
        /// <param name="value">The value now.</param>
        /// <param name="description">What the row says about itself.</param>
        /// <param name="isFixed">Whether the value is shown only.</param>
        /// <returns>The row.</returns>
        protected SheetSetGridRow AddText(string label, string value, string description, bool isFixed = false) =>
            Add(new SheetSetGridRow(this, SheetSetRowKind.Text, label, value, description, isFixed, null, null));

        /// <summary>Adds a Yes or No drop-down.</summary>
        /// <param name="label">The label.</param>
        /// <param name="value">The value now.</param>
        /// <param name="description">What the row says about itself.</param>
        /// <returns>The row.</returns>
        protected SheetSetGridRow AddYesNo(string label, bool value, string description) =>
            Add(
                new SheetSetGridRow(
                    this,
                    SheetSetRowKind.Choice,
                    label,
                    value ? "Yes" : "No",
                    description,
                    false,
                    null,
                    new[] { "Yes", "No" }
                )
            );

        /// <summary>Adds a row with a "..." button.</summary>
        /// <param name="label">The label.</param>
        /// <param name="value">The value now.</param>
        /// <param name="description">What the row says about itself.</param>
        /// <param name="browse">What the button does, true where it picked a value.</param>
        /// <param name="isFixed">Whether the value cannot be typed (it is only picked).</param>
        /// <returns>The row.</returns>
        protected SheetSetGridRow AddPath(
            string label,
            string value,
            string description,
            Func<bool> browse,
            bool isFixed = false
        ) => Add(new SheetSetGridRow(this, SheetSetRowKind.Path, label, value, description, isFixed, browse, null));

        /// <summary>Takes away every row from the one given on, as a dialog that rebuilds a section does.</summary>
        /// <param name="first">The first row to take away.</param>
        protected void RemoveRowsFrom(SheetSetGridRow first)
        {
            int at = _rows.IndexOf(first);
            while (_rows.Count > at)
            {
                _rows.RemoveAt(_rows.Count - 1);
            }

            if (_selected != null && !_rows.Contains(_selected))
            {
                SelectedRow = null;
            }
        }

        /// <summary>Gets the index of a row.</summary>
        /// <param name="row">The row.</param>
        /// <returns>Its place in <see cref="Rows"/>.</returns>
        protected int IndexOf(SheetSetGridRow row) => _rows.IndexOf(row);

        /// <summary>Tells every row that whether it is read-only may have changed.</summary>
        protected void RaiseRowsReadOnly()
        {
            foreach (SheetSetGridRow row in _rows)
            {
                row.RaiseReadOnly();
            }
        }

        /// <summary>
        /// Asks the "..." button of a folder row for a folder, starting at the one the row names.
        /// </summary>
        /// <param name="current">The folder now named.</param>
        /// <returns>The folder chosen; null where the pick was cancelled or the dialog has no picker.</returns>
        protected string AskFolder(string current) => BrowseFolder?.Invoke(current);

        /// <summary>Asks the "..." button of a file row for a file.</summary>
        /// <param name="title">The title of the file dialog.</param>
        /// <param name="filter">The filter of the file dialog.</param>
        /// <param name="current">The file now named.</param>
        /// <returns>The file chosen; null where the pick was cancelled or the dialog has no picker.</returns>
        protected string AskFile(string title, string filter, string current) =>
            BrowseFile?.Invoke(title, filter, current);

        private SheetSetGridRow Add(SheetSetGridRow row)
        {
            _rows.Add(row);
            return row;
        }

        /// <summary>The text of a layout of a drawing in a grid row: "A1 sheet (D:\Templates\OpenCAD2D-mm.dwt)"; "None" for no layout.</summary>
        /// <param name="reference">The reference; null for none.</param>
        /// <param name="setFolder">The folder of the .dst.</param>
        /// <returns>The text.</returns>
        internal static string DrawingText(CadDrawingReference reference, string setFolder)
        {
            if (reference == null)
            {
                return "None";
            }

            string path = reference.File.Locate(setFolder) ?? reference.File.FileName;
            return string.IsNullOrEmpty(reference.Name) ? path : reference.Name + " (" + path + ")";
        }

        /// <summary>The path a file reference names where the file may not be there yet; empty for none.</summary>
        /// <param name="reference">The reference; null for none.</param>
        /// <param name="setFolder">The folder of the .dst.</param>
        /// <returns>The path.</returns>
        internal static string PathText(CadFileReference reference, string setFolder) =>
            reference == null ? string.Empty : reference.Locate(setFolder) ?? reference.FileName ?? string.Empty;

        /// <summary>A path typed in a box as a reference to a file or folder, relative to the folder of the .dst where it is not rooted; null for an empty box.</summary>
        /// <param name="text">What the box holds.</param>
        /// <param name="setFolder">The folder of the .dst.</param>
        /// <returns>The reference.</returns>
        internal static CadFileReference ReferenceOf(string text, string setFolder)
        {
            string trimmed = (text ?? string.Empty).Trim();
            if (trimmed.Length == 0)
            {
                return null;
            }

            string full = System.IO.Path.IsPathRooted(trimmed)
                ? System.IO.Path.GetFullPath(trimmed)
                : System.IO.Path.GetFullPath(System.IO.Path.Combine(setFolder, trimmed));
            return CadFileReference.FromPath(full, setFolder);
        }

        /// <summary>Whether two paths typed name the same place, letter case ignored (an empty text names none).</summary>
        /// <param name="a">The first.</param>
        /// <param name="b">The second.</param>
        /// <returns>True when they do.</returns>
        internal static bool SamePlace(string a, string b)
        {
            if (string.IsNullOrWhiteSpace(a) || string.IsNullOrWhiteSpace(b))
            {
                return string.IsNullOrWhiteSpace(a) && string.IsNullOrWhiteSpace(b);
            }

            try
            {
                return string.Equals(
                    System.IO.Path.GetFullPath(a.Trim()).TrimEnd('\\', '/'),
                    System.IO.Path.GetFullPath(b.Trim()).TrimEnd('\\', '/'),
                    StringComparison.OrdinalIgnoreCase
                );
            }
            catch (ArgumentException)
            {
                return string.Equals(a.Trim(), b.Trim(), StringComparison.OrdinalIgnoreCase);
            }
        }
    }
}
