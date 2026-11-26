using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.RegularExpressions;

namespace OpenCAD2D.Wpf
{
    /// <summary>What a row of the Choose Layouts tree of the Create Sheet Set wizard stands for.</summary>
    public enum NewSheetSetNodeKind
    {
        /// <summary>A folder: the one that was picked, or one inside it.</summary>
        Folder = 0,

        /// <summary>A drawing, a DWG.</summary>
        Drawing = 1,

        /// <summary>A layout of a drawing, which becomes a sheet where it is ticked.</summary>
        Layout = 2,
    }

    /// <summary>
    /// A row of the Choose Layouts tree of the Create Sheet Set wizard: a folder, a drawing in it, or a layout of the drawing,
    /// each with its box. A tick on a folder or a drawing ticks every layout below it that can be a sheet, and a box above
    /// shows what is below it (ticked, clear, or mixed, which is null). A layout that cannot be a sheet (its name holds #, or
    /// it is a sheet of a set already) is grey, clear and cannot be ticked; <see cref="Note"/> says why on the right.
    /// </summary>
    public sealed class NewSheetSetLayoutNode : ObservableObject
    {
        private static readonly Regex SetFile = new Regex(@"\(([^()]*\.dst)\)\.?$", RegexOptions.IgnoreCase);

        private readonly Action _checkedChanged;
        private readonly Action _shownChanged;
        private bool? _isChecked = false;
        private bool _isExpanded = true;

        internal NewSheetSetLayoutNode(
            NewSheetSetNodeKind kind,
            string text,
            string path,
            string layoutName,
            NewSheetSetLayoutNode parent,
            string refusal,
            Action checkedChanged,
            Action shownChanged
        )
        {
            Kind = kind;
            Text = text;
            Path = path;
            LayoutName = layoutName;
            Parent = parent;
            Depth = parent == null ? 0 : parent.Depth + 1;
            Refusal = refusal;
            _checkedChanged = checkedChanged;
            _shownChanged = shownChanged;
            parent?.Children.Add(this);
        }

        /// <summary>Gets what the row stands for.</summary>
        public NewSheetSetNodeKind Kind { get; }

        /// <summary>Gets the words of the row: the folder's full path for the one picked, a folder's name, a drawing's file name, a layout's name.</summary>
        public string Text { get; }

        /// <summary>Gets the full path of the folder or the drawing; for a layout, the drawing's.</summary>
        public string Path { get; }

        /// <summary>Gets the layout's name; null for a folder or a drawing.</summary>
        public string LayoutName { get; }

        /// <summary>Gets the row above, or null for a folder that was picked.</summary>
        public NewSheetSetLayoutNode Parent { get; }

        /// <summary>Gets how many rows lie above this one, which sets its indent.</summary>
        public int Depth { get; }

        /// <summary>Gets the width, in pixels, the row is indented by.</summary>
        public double Indent => Depth * 18.0;

        /// <summary>Gets the rows below, in the order the sheets are made.</summary>
        public List<NewSheetSetLayoutNode> Children { get; } = new List<NewSheetSetLayoutNode>();

        /// <summary>Gets why a layout cannot be a sheet, in a sentence; null where it can, and for a folder and a drawing.</summary>
        public string Refusal { get; }

        /// <summary>Gets whether this is a layout that cannot be a sheet.</summary>
        public bool IsRefused => Refusal != null;

        /// <summary>Gets the short reason shown on the right of a refused layout ("# in the name", "in Riverside Clinic.dst"); empty for any other row.</summary>
        public string Note
        {
            get
            {
                if (Refusal == null)
                {
                    return string.Empty;
                }

                if (LayoutName != null && LayoutName.IndexOf('#') >= 0)
                {
                    return "# in the name";
                }

                Match set = SetFile.Match(Refusal);
                return set.Success ? "in " + System.IO.Path.GetFileName(set.Groups[1].Value) : "not a sheet";
            }
        }

        /// <summary>Gets whether the row has rows below it to show or hide.</summary>
        public bool IsExpandable => Children.Count > 0;

        /// <summary>Gets or sets whether the rows below are shown.</summary>
        public bool IsExpanded
        {
            get => _isExpanded;
            set
            {
                if (Set(ref _isExpanded, value))
                {
                    _shownChanged?.Invoke();
                }
            }
        }

        /// <summary>Gets whether the box can be ticked: a layout that can be a sheet, or a folder or a drawing with such a layout below it.</summary>
        public bool IsCheckable =>
            Kind == NewSheetSetNodeKind.Layout ? Refusal == null : Children.Any(c => c.IsCheckable);

        /// <summary>
        /// Gets or sets the box: true when this layout is ticked, or every layout below it that can be is; false when none is;
        /// null when some are. Setting it from the box ticks or clears every layout below (a mixed box, clicked, ticks).
        /// </summary>
        public bool? IsChecked
        {
            get => _isChecked;
            set
            {
                if (!IsCheckable || value == null)
                {
                    return;
                }

                bool on = _isChecked == null || value.Value;
                Spread(on);
                Parent?.Recount();
                _checkedChanged?.Invoke();
            }
        }

        /// <summary>The layouts below this row (or this row, a layout) that are ticked, in the order of the tree.</summary>
        internal IEnumerable<NewSheetSetLayoutNode> TickedLayouts()
        {
            if (Kind == NewSheetSetNodeKind.Layout)
            {
                if (_isChecked == true)
                {
                    yield return this;
                }

                yield break;
            }

            foreach (NewSheetSetLayoutNode layout in Children.SelectMany(c => c.TickedLayouts()))
            {
                yield return layout;
            }
        }

        private void Spread(bool on)
        {
            if (Kind == NewSheetSetNodeKind.Layout)
            {
                Take(Refusal == null && on);
                return;
            }

            foreach (NewSheetSetLayoutNode child in Children)
            {
                child.Spread(on);
            }

            Recount();
        }

        /// <summary>Works the box out from the boxes of the rows below, and tells the boxes above.</summary>
        private void Recount()
        {
            bool[] below = Children.Where(c => c.IsCheckable).Select(c => c._isChecked == true).ToArray();
            bool anyMixed = Children.Any(c => c.IsCheckable && c._isChecked == null);
            bool? now =
                anyMixed ? (bool?)null
                : below.Length == 0 ? false
                : below.All(b => b) ? true
                : below.Any(b => b) ? (bool?)null
                : false;
            Take(now);
            Parent?.Recount();
        }

        private void Take(bool? value)
        {
            if (_isChecked != value)
            {
                _isChecked = value;
                Raise(nameof(IsChecked));
            }
        }
    }
}
