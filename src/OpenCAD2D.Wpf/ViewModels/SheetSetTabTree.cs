using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;

namespace OpenCAD2D.Wpf
{
    /// <summary>What a row of the Sheet Views tab or the Model Views tab is.</summary>
    public enum SheetSetTabRowKind
    {
        /// <summary>The sheet set, the root of the Sheet Views tab.</summary>
        SheetSet,

        /// <summary>A view category, a folder of views.</summary>
        Category,

        /// <summary>A sheet that holds views (view by sheet).</summary>
        Sheet,

        /// <summary>A sheet view, a view of a model drawing placed on a sheet.</summary>
        View,

        /// <summary>A resource location, a folder the set lists under Model views.</summary>
        Location,

        /// <summary>A drawing in a resource location.</summary>
        Drawing,

        /// <summary>A named view of model space of a drawing.</summary>
        ModelView,
    }

    /// <summary>
    /// A row of the Sheet Views tab's or the Model Views tab's tree: the set, a view category or a sheet, a view; or a resource
    /// location, a drawing in it, a named view of that. Like the rows of the Sheet List, it is the tree's own copy, built again
    /// from the set after every change and found again by its <see cref="Id"/>.
    /// </summary>
    public sealed class SheetSetTabRow : ObservableObject
    {
        private const int IndentWidth = 16;

        private readonly SheetSetTabTree _owner;
        private readonly List<SheetSetTabRow> _children = new List<SheetSetTabRow>();
        private Func<IEnumerable<SheetSetTabRow>> _loader;
        private bool _isExpanded = true;
        private bool _isSelected;
        private string _text = string.Empty;
        private string _detail = string.Empty;

        internal SheetSetTabRow(
            SheetSetTabTree owner,
            SheetSetTabRowKind kind,
            string id,
            string icon,
            SheetSetTabRow parent
        )
        {
            _owner = owner;
            Kind = kind;
            Id = id;
            Icon = icon;
            Parent = parent;
        }

        /// <summary>Gets what the row is.</summary>
        public SheetSetTabRowKind Kind { get; }

        /// <summary>Gets the key of the row, the same for the same part of the set or of the folder after the tree is built again: "view:" and the ID of the sheet view, "loc:" and the path of a folder.</summary>
        public string Id { get; }

        /// <summary>Gets the row above, the one this is in; null for the root.</summary>
        public SheetSetTabRow Parent { get; }

        /// <summary>Gets how many rows are above this one in a line: 0 for a root.</summary>
        public int Depth => Parent == null ? 0 : Parent.Depth + 1;

        /// <summary>Gets how far in from the left its row is written, in device independent pixels; a tree whose root is not shown has its first rows at the left.</summary>
        public double Indent => Math.Max(0, Depth - (_owner.HidesRoot ? 1 : 0)) * IndentWidth;

        /// <summary>Gets the rows in this one, in order.</summary>
        public IReadOnlyList<SheetSetTabRow> Children => _children;

        /// <summary>Gets whether the row can be opened: it holds rows, or can still be read for them (a drawing's named views are read when it is opened).</summary>
        public bool IsExpandable => _children.Count > 0 || _loader != null;

        /// <summary>Gets the words of the row: the name of a set or a category, "Number - Title" of a view or a sheet, a path for a location, a file name for a drawing, a view's name.</summary>
        public string Text
        {
            get => _text;
            internal set => Set(ref _text, value);
        }

        /// <summary>Gets the grey words at the right of the row: the number of the sheet a view is on, or the scale it is placed at.</summary>
        public string Detail
        {
            get => _detail;
            internal set => Set(ref _detail, value);
        }

        /// <summary>Gets the picture drawn before the words (a <c>SheetSetIcon</c> glyph).</summary>
        public string Icon { get; }

        /// <summary>Gets the colour of the row's words.</summary>
        public SheetSetTone Tone { get; internal set; }

        /// <summary>Gets whether the row's words are bold: the root of the tree and a resource location.</summary>
        public bool IsBold => Kind == SheetSetTabRowKind.SheetSet || Kind == SheetSetTabRowKind.Location;

        /// <summary>Gets the ID in the sheet set file of the part the row stands for (the sheet view, category or sheet); null for a row of the Model Views tab.</summary>
        public string ComponentId { get; internal set; }

        /// <summary>Gets the full path of the folder or drawing a row of the Model Views tab stands for; null for a row of the Sheet Views tab.</summary>
        public string Path { get; internal set; }

        /// <summary>Gets the name of the named view of a model view row, or of the sheet view's named view; null for a row that stands for none.</summary>
        public string ViewName { get; internal set; }

        /// <summary>Gets or sets whether the rows in it are shown.</summary>
        public bool IsExpanded
        {
            get => _isExpanded;
            set
            {
                if (Set(ref _isExpanded, value))
                {
                    Raise(nameof(Chevron));
                    _owner.ExpansionChanged(this);
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
                    _owner.SelectionChanged();
                }
            }
        }

        /// <summary>Gets the arrow of the row, a <c>SheetSetIcon</c> glyph: down where it is open, right where it is closed.</summary>
        public string Chevron => _isExpanded ? "chevron-down" : "chevron-right";

        /// <summary>Gets whether the row is the root of its tree.</summary>
        public bool IsRoot => Parent == null;

        internal bool DefaultExpanded { get; set; } = true;

        internal void Add(SheetSetTabRow child) => _children.Add(child);

        /// <summary>Lets the row read its rows when it is first opened, as a drawing reads its named views.</summary>
        internal void LoadWith(Func<IEnumerable<SheetSetTabRow>> loader) => _loader = loader;

        internal bool EnsureLoaded()
        {
            Func<IEnumerable<SheetSetTabRow>> loader = _loader;

            if (loader == null)
            {
                return false;
            }

            _loader = null;

            foreach (SheetSetTabRow child in loader())
            {
                _children.Add(child);
            }

            Raise(nameof(IsExpandable));
            return true;
        }

        internal void StartExpanded(bool expanded) => _isExpanded = expanded;

        internal void StartSelected(bool selected) => _isSelected = selected;

        /// <inheritdoc/>
        public override string ToString() => Text;
    }

    /// <summary>
    /// The rows of one tab's tree, the rows shown in order (a collapsed row's children left out) and what the user opened,
    /// closed and selected, kept by the row's <see cref="SheetSetTabRow.Id"/> across the tree being built again. Both the Sheet
    /// Views tab and the Model Views tab keep one.
    /// </summary>
    public sealed class SheetSetTabTree
    {
        private readonly Dictionary<string, bool> _opened = new Dictionary<string, bool>(StringComparer.Ordinal);
        private bool _suspended;

        /// <summary>Makes a tree.</summary>
        /// <param name="hidesRoot">Whether the root is left out of the rows shown, as the Model Views tab leaves out the row that holds the locations.</param>
        public SheetSetTabTree(bool hidesRoot = false)
        {
            HidesRoot = hidesRoot;
        }

        /// <summary>Gets whether the root is left out of <see cref="Rows"/>: its rows are the first.</summary>
        public bool HidesRoot { get; }

        /// <summary>Gets the rows that are shown, in order.</summary>
        public ObservableCollection<SheetSetTabRow> Rows { get; } = new ObservableCollection<SheetSetTabRow>();

        /// <summary>Gets the root of the tree; null where it has none.</summary>
        public SheetSetTabRow Root { get; private set; }

        /// <summary>Gets the rows selected, in the order of the tree.</summary>
        public IReadOnlyList<SheetSetTabRow> Selected => Rows.Where(r => r.IsSelected).ToList();

        /// <summary>Raised when the selection, or what is shown, changed.</summary>
        public event EventHandler Changed;

        /// <summary>Makes a root of the tree to be built, whose rows are made with <see cref="Row"/>.</summary>
        /// <param name="kind">What the row is.</param>
        /// <param name="id">Its key.</param>
        /// <param name="icon">Its picture.</param>
        /// <param name="parent">The row above; null for the root.</param>
        /// <returns>The row, not yet in a tree.</returns>
        internal SheetSetTabRow Row(SheetSetTabRowKind kind, string id, string icon, SheetSetTabRow parent) =>
            new SheetSetTabRow(this, kind, id, icon, parent);

        /// <summary>Shows a tree built, keeping what was open and what was selected, by key. Null empties the tree.</summary>
        /// <param name="root">The root of the new tree.</param>
        internal void Load(SheetSetTabRow root)
        {
            var selected = new HashSet<string>(Rows.Where(r => r.IsSelected).Select(r => r.Id), StringComparer.Ordinal);
            _suspended = true;
            Rows.Clear();
            Root = root;

            if (root != null)
            {
                foreach (SheetSetTabRow row in Walk(root))
                {
                    row.StartExpanded(_opened.TryGetValue(row.Id, out bool open) ? open : row.DefaultExpanded);
                }

                // A row that was open before and reads its rows on opening is read now, so that it comes back open.
                foreach (SheetSetTabRow row in Walk(root).ToList())
                {
                    if (row.IsExpanded && row.EnsureLoaded())
                    {
                        foreach (SheetSetTabRow child in row.Children)
                        {
                            child.StartExpanded(
                                _opened.TryGetValue(child.Id, out bool childOpen) ? childOpen : child.DefaultExpanded
                            );
                        }
                    }
                }

                foreach (SheetSetTabRow row in HidesRoot ? Show(root).Skip(1) : Show(root))
                {
                    row.StartSelected(selected.Contains(row.Id));
                    Rows.Add(row);
                }
            }

            _suspended = false;
            Changed?.Invoke(this, EventArgs.Empty);
        }

        /// <summary>Opens every row that holds rows.</summary>
        internal void ExpandAll() => SetAll(true);

        /// <summary>Closes every row that holds rows, the root left open.</summary>
        internal void CollapseAll() => SetAll(false);

        private void SetAll(bool open)
        {
            if (Root == null)
            {
                return;
            }

            foreach (SheetSetTabRow row in Walk(Root).ToList())
            {
                if (row.IsExpandable && (open || !row.IsRoot) && row.IsExpanded != open)
                {
                    row.IsExpanded = open;
                }
            }
        }

        internal void ExpansionChanged(SheetSetTabRow row)
        {
            if (_suspended)
            {
                return;
            }

            _opened[row.Id] = row.IsExpanded;
            int at = Rows.IndexOf(row);

            if (at < 0)
            {
                return;
            }

            _suspended = true;

            if (row.IsExpanded)
            {
                if (row.EnsureLoaded())
                {
                    foreach (SheetSetTabRow child in row.Children)
                    {
                        child.StartExpanded(
                            _opened.TryGetValue(child.Id, out bool open) ? open : child.DefaultExpanded
                        );
                    }
                }

                int place = at + 1;

                foreach (SheetSetTabRow shown in Show(row).Skip(1))
                {
                    Rows.Insert(place++, shown);
                }
            }
            else
            {
                while (at + 1 < Rows.Count && Rows[at + 1].Depth > row.Depth)
                {
                    Rows[at + 1].StartSelected(false);
                    Rows.RemoveAt(at + 1);
                }
            }

            _suspended = false;
            Changed?.Invoke(this, EventArgs.Empty);
        }

        internal void SelectionChanged()
        {
            if (!_suspended)
            {
                Changed?.Invoke(this, EventArgs.Empty);
            }
        }

        /// <summary>Every row of a tree, shown or not, the row first and then those in it in order.</summary>
        internal static IEnumerable<SheetSetTabRow> Walk(SheetSetTabRow row)
        {
            yield return row;

            foreach (SheetSetTabRow child in row.Children)
            {
                foreach (SheetSetTabRow below in Walk(child))
                {
                    yield return below;
                }
            }
        }

        private static IEnumerable<SheetSetTabRow> Show(SheetSetTabRow row)
        {
            yield return row;

            if (row.IsExpanded)
            {
                foreach (SheetSetTabRow child in row.Children)
                {
                    foreach (SheetSetTabRow shown in Show(child))
                    {
                        yield return shown;
                    }
                }
            }
        }
    }
}
