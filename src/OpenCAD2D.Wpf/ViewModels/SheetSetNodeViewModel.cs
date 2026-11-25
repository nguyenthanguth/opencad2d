using System;
using System.Collections.Generic;
using OpenCAD2D.SheetSets;

namespace OpenCAD2D.Wpf
{
    /// <summary>What a row of the Sheet Set Manager's tree is.</summary>
    public enum SheetSetNodeKind
    {
        /// <summary>The sheet set, the root of the tree.</summary>
        SheetSet,

        /// <summary>A subset, a group of sheets and other subsets.</summary>
        Subset,

        /// <summary>A sheet, a layout of a drawing.</summary>
        Sheet,
    }

    /// <summary>The colour the words of a row or of a detail are written in, as the design draws them.</summary>
    public enum SheetSetTone
    {
        /// <summary>The text colour of the palette.</summary>
        Normal,

        /// <summary>Grey: a sheet left out of publishing.</summary>
        Muted,

        /// <summary>Green: available.</summary>
        Ok,

        /// <summary>Yellow: a set that is read-only.</summary>
        Warn,

        /// <summary>Red: a sheet whose drawing or layout is not found.</summary>
        Error,
    }

    /// <summary>Where a row dragged is dropped on the row under the pointer.</summary>
    public enum SheetSetDropPosition
    {
        /// <summary>Above the row, in the group the row is in.</summary>
        Before,

        /// <summary>On the row, which must be a subset or the set: at the end of its group.</summary>
        Into,

        /// <summary>Below the row, in the group the row is in.</summary>
        After,
    }

    /// <summary>A dialog the palette asks the window for, the dialogs of the next package (A12 4b).</summary>
    public enum SheetSetDialog
    {
        /// <summary>New Sheet.</summary>
        NewSheet,

        /// <summary>New Subset (the Subset Properties on a new subset).</summary>
        NewSubset,

        /// <summary>Import Layouts as Sheets.</summary>
        ImportLayouts,

        /// <summary>Rename &amp; Renumber Sheet.</summary>
        RenameAndRenumber,

        /// <summary>Rename Subset (the Subset Properties on the name).</summary>
        RenameSubset,

        /// <summary>The Properties of the sheet set, a subset or a sheet.</summary>
        Properties,
    }

    /// <summary>Tells a window which dialog a menu item of the palette asks for, and on which row.</summary>
    public sealed class SheetSetDialogEventArgs : EventArgs
    {
        internal SheetSetDialogEventArgs(SheetSetDialog dialog, SheetSetNodeViewModel node)
        {
            Dialog = dialog;
            Node = node;
        }

        /// <summary>Gets the dialog.</summary>
        public SheetSetDialog Dialog { get; }

        /// <summary>Gets the row the menu was opened on.</summary>
        public SheetSetNodeViewModel Node { get; }

        /// <summary>Gets or sets whether a handler showed the dialog; the palette says on the command line that there is none otherwise.</summary>
        public bool Handled { get; set; }
    }

    /// <summary>Where a drop puts what was dragged: the group it goes to and its place among that group's other subsets and sheets.</summary>
    public sealed class SheetSetDrop
    {
        internal SheetSetDrop(SheetSetNodeViewModel parent, int index)
        {
            Parent = parent;
            Index = index;
        }

        /// <summary>Gets the sheet set or subset it goes into.</summary>
        public SheetSetNodeViewModel Parent { get; }

        /// <summary>Gets its place among the children of <see cref="Parent"/> once everything dragged has left them.</summary>
        public int Index { get; }
    }

    /// <summary>A line of the Details pane: a label and what it says, in a colour.</summary>
    public sealed class SheetSetDetailRow
    {
        internal SheetSetDetailRow(string label, string value, SheetSetTone tone = SheetSetTone.Normal)
        {
            Label = label;
            Value = value;
            Tone = tone;
        }

        /// <summary>Gets the label, as "Status".</summary>
        public string Label { get; }

        /// <summary>Gets what it says.</summary>
        public string Value { get; }

        /// <summary>Gets the colour of <see cref="Value"/>.</summary>
        public SheetSetTone Tone { get; }
    }

    /// <summary>
    /// A row of the Sheet Set Manager's tree: the sheet set, a subset or a sheet, in set order, with what the palette knows of
    /// its drawing (open in the session, left out of publishing, not found). The row is the tree's own copy of the part of the
    /// set; it is built again after every change, and the part is found again in the file by <see cref="Id"/>.
    /// </summary>
    public sealed class SheetSetNodeViewModel : ObservableObject
    {
        private const int IndentWidth = 16;

        private readonly SheetSetManagerViewModel _owner;
        private readonly List<SheetSetNodeViewModel> _children = new List<SheetSetNodeViewModel>();
        private bool _isExpanded = true;
        private bool _isSelected;
        private bool _isOpen;
        private bool _isMissing;
        private bool _isExcluded;
        private SheetSetTone _tone;
        private string _text = string.Empty;

        internal SheetSetNodeViewModel(
            SheetSetManagerViewModel owner,
            SheetSetNodeKind kind,
            string id,
            SheetSetNodeViewModel parent
        )
        {
            _owner = owner;
            Kind = kind;
            Id = id;
            Parent = parent;
        }

        /// <summary>Gets what the row is.</summary>
        public SheetSetNodeKind Kind { get; }

        /// <summary>Gets the <c>ID</c> of the part in the file.</summary>
        public string Id { get; }

        /// <summary>Gets the row above, the subset or the set this one is in; null for the set.</summary>
        public SheetSetNodeViewModel Parent { get; }

        /// <summary>Gets how many rows are above this one in a line: 0 for the set.</summary>
        public int Depth => Parent == null ? 0 : Parent.Depth + 1;

        /// <summary>Gets how far in from the left its row is written, in device independent pixels.</summary>
        public double Indent => Depth * IndentWidth;

        /// <summary>Gets the rows in this one, in set order.</summary>
        public IReadOnlyList<SheetSetNodeViewModel> Children => _children;

        /// <summary>Gets whether the row can be opened: it holds rows.</summary>
        public bool IsExpandable => _children.Count > 0;

        /// <summary>Gets the words of the row: the name of the set or a subset, "Number - Title" of a sheet, the title alone where it has no number.</summary>
        public string Text
        {
            get => _text;
            internal set => Set(ref _text, value);
        }

        /// <summary>Gets or sets whether the rows in it are shown.</summary>
        public bool IsExpanded
        {
            get => _isExpanded;
            set
            {
                if (Set(ref _isExpanded, value))
                {
                    Raise(nameof(Chevron));
                    _owner.NodeExpansionChanged(this);
                }
            }
        }

        /// <summary>Gets or sets whether the row is selected, as the list's own selection says.</summary>
        public bool IsSelected
        {
            get => _isSelected;
            set
            {
                if (Set(ref _isSelected, value))
                {
                    _owner.NodeSelectionChanged();
                }
            }
        }

        /// <summary>Gets whether the sheet's drawing is open in this session (a green dot on its icon).</summary>
        public bool IsOpen
        {
            get => _isOpen;
            internal set
            {
                if (Set(ref _isOpen, value))
                {
                    Raise(nameof(Icon));
                }
            }
        }

        /// <summary>Gets whether the sheet's drawing or layout is not found (a red label and a dashed icon).</summary>
        public bool IsMissing
        {
            get => _isMissing;
            internal set
            {
                if (Set(ref _isMissing, value))
                {
                    Raise(nameof(Icon));
                }
            }
        }

        /// <summary>Gets whether the sheet is left out of publishing (a grey label and a struck icon).</summary>
        public bool IsExcluded
        {
            get => _isExcluded;
            internal set
            {
                if (Set(ref _isExcluded, value))
                {
                    Raise(nameof(Icon));
                }
            }
        }

        /// <summary>Gets the colour of the row's words.</summary>
        public SheetSetTone Tone
        {
            get => _tone;
            internal set => Set(ref _tone, value);
        }

        /// <summary>Gets the picture drawn before the words (a <c>SheetSetIcon</c> glyph): the set, a folder for a subset, and for a sheet a page that has a green dot where its drawing is open, is struck where it is left out of publishing, is dashed and red where it is not found.</summary>
        public string Icon =>
            IsSheetSet ? "set"
            : IsSubset ? "subset"
            : IsMissing ? "sheet-missing"
            : IsExcluded ? "sheet-excluded"
            : IsOpen ? "sheet-open"
            : "sheet";

        /// <summary>Gets the arrow of the row, a <c>SheetSetIcon</c> glyph: down where it is open, right where it is closed.</summary>
        public string Chevron => _isExpanded ? "chevron-down" : "chevron-right";

        /// <summary>Gets whether the row is the sheet set.</summary>
        public bool IsSheetSet => Kind == SheetSetNodeKind.SheetSet;

        /// <summary>Gets whether the row is a subset.</summary>
        public bool IsSubset => Kind == SheetSetNodeKind.Subset;

        /// <summary>Gets whether the row is a sheet.</summary>
        public bool IsSheet => Kind == SheetSetNodeKind.Sheet;

        /// <summary>Gets what was found of the sheet's layout when the row was made; null for the set and a subset.</summary>
        internal CadFoundLayout Found { get; set; }

        /// <summary>Gets the layout the sheet expects, as the file says it; null for the set and a subset.</summary>
        internal CadDrawingReference Expected { get; set; }

        internal void Add(SheetSetNodeViewModel child) => _children.Add(child);

        /// <summary>Sets the expansion without telling the palette, as the tree being built does.</summary>
        internal void StartExpanded(bool expanded) => _isExpanded = expanded;

        /// <summary>Sets the selection without telling the palette, as the tree being built does.</summary>
        internal void StartSelected(bool selected) => _isSelected = selected;

        /// <inheritdoc/>
        public override string ToString() => Text;
    }
}
