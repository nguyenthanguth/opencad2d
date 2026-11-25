using System;
using System.Collections.Generic;
using System.Windows.Input;

namespace OpenCAD2D.Wpf
{
    /// <summary>
    /// One row of the Properties panel: a label and a value that, unless read-only, is edited in place. Setting
    /// <see cref="Value"/> commits it through <see cref="CadPropertyEditor"/> as one undo step; a value refused is said on the
    /// command line and the row shows the value it had.
    /// </summary>
    public sealed class PropertyRowViewModel : ObservableObject
    {
        private readonly Func<PropertyRowViewModel, string, bool> _commit;
        private readonly int _byLayerRgb;
        private readonly DelegateCommand _pick;
        private string _value;

        internal PropertyRowViewModel(
            CadPropertyRow row,
            IReadOnlyList<string> options,
            Func<PropertyRowViewModel, string, bool> commit,
            bool isColor = false,
            int byLayerRgb = 0xFFFFFF,
            Action<CadPropertyRow> pick = null,
            Func<bool> canPick = null
        )
        {
            Label = row.Label;
            _value = row.Value;
            IsReadOnly = row.IsReadOnly;
            IsCheck = row.IsCheck && !row.IsReadOnly;
            IsColor = isColor;
            _byLayerRgb = byLayerRgb;

            // A coordinate of a point is picked on the drawing, X, Y and Z alike: of a point an extension or a plugin gave a setter,
            // and of the presenter's own (Center X of a circle, Start Y of a line), where the caller gives the pick.
            if (pick != null && !row.IsReadOnly)
            {
                _pick = new DelegateCommand(_ => pick(row), _ => canPick == null || canPick());
            }

            // The drop-down shows the value the row has, one of its list or not; a colour not in the list (a true colour,
            // *Varies*) stands before Select Color..., which is last.
            if (options != null && !System.Linq.Enumerable.Contains(options, _value))
            {
                var all = new List<string>(options);
                int last = isColor ? all.IndexOf(MainViewModel.SelectColorOption) : -1;
                all.Insert(last < 0 ? 0 : last, _value);
                options = all;
            }

            Options = options;
            _commit = commit;
        }

        /// <summary>Gets the label, as Layer or Start X.</summary>
        public string Label { get; }

        /// <summary>Gets or sets the value as text; setting a new one commits it, and a refused one leaves the old value.</summary>
        public string Value
        {
            get => _value;
            set
            {
                string typed = value ?? string.Empty;

                if (IsReadOnly || string.Equals(typed, _value, StringComparison.Ordinal))
                {
                    return;
                }

                string old = _value;
                _value = typed;

                if (!_commit(this, typed))
                {
                    _value = old;
                }

                Raise(nameof(Value));
                Raise(nameof(Varies));
                Raise(nameof(IsOn));
                Raise(nameof(SwatchRgb));
            }
        }

        /// <summary>Gets whether the row holds a colour: the presenter's Color row, or a row of kind <see cref="CadPropertyKind.Color"/> a plugin or an extension gave.</summary>
        public bool IsColor { get; }

        /// <summary>
        /// Gets the swatch drawn before the value of a colour row as 0xRRGGBB: the colour of the value, that of the layer of the
        /// objects for ByLayer, white for ByBlock; null for none, where the row holds no colour, or *Varies*, or text that is no
        /// colour. White (7) is drawn split black and white all the same, its value here 0xFFFFFF.
        /// </summary>
        public int? SwatchRgb => IsColor ? ColorTextSwatch.Rgb(_value, _byLayerRgb) : null;

        /// <summary>Gets the colour of the layer of the objects, which a colour of ByLayer is drawn in: that of the one layer they are on, white where they differ.</summary>
        internal int ByLayerRgb => _byLayerRgb;

        /// <summary>Gets whether the row is a coordinate of a point that can be changed (the presenter's own, or one a plugin or an extension gave a setter), which shows a button to pick the point on the drawing while it is edited.</summary>
        public bool CanPickPoint => _pick != null;

        /// <summary>
        /// Gets what the pick-point button does: asks "Pick a point in the drawing:" on the command line, with a rubber band from the
        /// point, and sets the point to the one picked, its X and Y the picked ones and its Z as it was. It cannot run while a command
        /// runs. Null for a row that is not a coordinate of a point.
        /// </summary>
        public ICommand PickPointCommand => _pick;

        /// <summary>Asks again whether the pick-point button can run, as a command starts or ends.</summary>
        internal void RefreshPick() => _pick?.RaiseCanExecuteChanged();

        /// <summary>Gets whether the row is a check box an extension gave, which the window draws as a box to tick.</summary>
        public bool IsCheck { get; }

        /// <summary>
        /// Gets or sets whether a check box is ticked: setting it commits <see cref="CadPropertyRow.YesText"/> or
        /// <see cref="CadPropertyRow.NoText"/> as <see cref="Value"/> does.
        /// </summary>
        public bool IsOn
        {
            get => string.Equals(_value, CadPropertyRow.YesText, StringComparison.Ordinal);
            set => Value = value ? CadPropertyRow.YesText : CadPropertyRow.NoText;
        }

        /// <summary>Gets whether the value cannot be edited, drawn grey.</summary>
        public bool IsReadOnly { get; }

        /// <summary>Gets whether the selected entities differ in this property (*Varies*), drawn grey and slanted.</summary>
        public bool Varies => _value == CadPropertyRow.VariesText;

        /// <summary>Gets the values the drop-down offers, as the layers for Layer; null for a row typed into.</summary>
        public IReadOnlyList<string> Options { get; }

        /// <summary>Gets whether the row is an editable drop-down.</summary>
        public bool HasOptions => !IsReadOnly && Options != null;

        /// <summary>Gets whether the row is a box typed into.</summary>
        public bool IsTyped => !IsReadOnly && Options == null && !IsCheck;

        /// <inheritdoc/>
        public override string ToString() => Label + " = " + _value;
    }

    /// <summary>A titled group of rows of the Properties panel, such as General or Geometry.</summary>
    public sealed class PropertyGroupViewModel
    {
        internal PropertyGroupViewModel(string title, IReadOnlyList<PropertyRowViewModel> rows)
        {
            Title = title;
            Rows = rows;
        }

        /// <summary>Gets the title.</summary>
        public string Title { get; }

        /// <summary>Gets the rows.</summary>
        public IReadOnlyList<PropertyRowViewModel> Rows { get; }

        /// <inheritdoc/>
        public override string ToString() => Title;
    }
}
