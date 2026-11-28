using System;
using System.Collections.Generic;
using System.Linq;

namespace OpenCAD2D.Wpf
{
    /// <summary>Where a group box of the dimension style dialogs sits on its tab, as AutoCAD lays the tab out.</summary>
    public enum DimGroupColumn
    {
        /// <summary>The left column, beside the preview.</summary>
        Left,

        /// <summary>The right column, under the preview.</summary>
        Right,

        /// <summary>Across the width of the tab, under both columns, its fields in two columns of its own.</summary>
        Wide,
    }

    /// <summary>
    /// One row of a group box: a field, or two boxes to tick that share a row as "Suppress: [ ] Dim line 1 [ ] Dim line 2" does,
    /// the dimension variables of both beside the caption.
    /// </summary>
    public sealed class DimRowViewModel
    {
        private DimRowViewModel(DimFieldViewModel first, DimFieldViewModel second, string caption)
        {
            First = first ?? throw new ArgumentNullException(nameof(first));
            Second = second;
            Caption = caption ?? string.Empty;
            Variables = second == null ? first.VariableText : first.VariableText + " · " + second.VariableText;

            if (second != null)
            {
                // "Suppress dim line 1" under the caption "Suppress" reads "Dim line 1" after its box.
                first.PairWords = Words(first.Label, Caption);
                second.PairWords = Words(second.Label, Caption);
            }
        }

        /// <summary>Gets the field of the row, or the left one of a pair.</summary>
        public DimFieldViewModel First { get; }

        /// <summary>Gets the right field of a pair; null for a row of one field.</summary>
        public DimFieldViewModel Second { get; }

        /// <summary>Gets whether the row holds two boxes to tick.</summary>
        public bool IsPair => Second != null;

        /// <summary>Gets the caption of a pair, as "Suppress"; empty for a row of one field.</summary>
        public string Caption { get; }

        /// <summary>Gets the dimension variables of the row, as "DIMSD1 · DIMSD2" for a pair, beside its caption.</summary>
        public string Variables { get; }

        /// <summary>Gets the fields of the row, in order.</summary>
        public IEnumerable<DimFieldViewModel> Fields => Second == null ? new[] { First } : new[] { First, Second };

        /// <summary>Makes a row of one field.</summary>
        /// <param name="field">The field.</param>
        /// <returns>The row.</returns>
        public static DimRowViewModel FromField(DimFieldViewModel field) => new DimRowViewModel(field, null, null);

        /// <summary>Makes a row of two boxes to tick, as "Suppress: [ ] Dim line 1 [ ] Dim line 2".</summary>
        /// <param name="caption">The caption, the word the two labels start with, as "Suppress".</param>
        /// <param name="first">The left field.</param>
        /// <param name="second">The right field.</param>
        /// <returns>The row.</returns>
        public static DimRowViewModel Pair(string caption, DimFieldViewModel first, DimFieldViewModel second) =>
            new DimRowViewModel(first, second ?? throw new ArgumentNullException(nameof(second)), caption);

        /// <summary>Makes a row of one field, so that a group can be listed from its fields.</summary>
        /// <param name="field">The field.</param>
        /// <returns>The row.</returns>
        public static implicit operator DimRowViewModel(DimFieldViewModel field) => FromField(field);

        private static string Words(string label, string caption)
        {
            string rest =
                caption.Length > 0 && label.StartsWith(caption, StringComparison.OrdinalIgnoreCase)
                    ? label.Substring(caption.Length).Trim()
                    : label;
            return rest.Length == 0 ? label : char.ToUpperInvariant(rest[0]) + rest.Substring(1);
        }
    }

    /// <summary>
    /// A group box of a tab of the dimension style dialogs: its title and its rows, in one column, or two where the group is
    /// <see cref="DimGroupColumn.Wide"/> (the Extension lines of the Lines tab).
    /// </summary>
    public sealed class DimGroupViewModel
    {
        internal DimGroupViewModel(
            string title,
            DimGroupColumn column,
            IReadOnlyList<IReadOnlyList<DimRowViewModel>> columns
        )
        {
            Title = title ?? throw new ArgumentNullException(nameof(title));
            Column = column;
            Columns = columns ?? throw new ArgumentNullException(nameof(columns));
            Fields = columns.SelectMany(c => c).SelectMany(r => r.Fields).ToList();

            foreach (DimFieldViewModel field in Fields)
            {
                field.FieldWidth = column == DimGroupColumn.Right ? 150 : 190;
                field.IsStacked = column == DimGroupColumn.Right && field.Kind == DimFieldKind.Choice;
            }
        }

        /// <summary>Gets the title, set in the top line of the box, as "Dimension lines".</summary>
        public string Title { get; }

        /// <summary>Gets where the box sits on its tab.</summary>
        public DimGroupColumn Column { get; }

        /// <summary>Gets the rows, by column: one column, two for a wide group.</summary>
        public IReadOnlyList<IReadOnlyList<DimRowViewModel>> Columns { get; }

        /// <summary>Gets the fields of the box, in order, the two of a pair row one after the other.</summary>
        public IReadOnlyList<DimFieldViewModel> Fields { get; }
    }
}
