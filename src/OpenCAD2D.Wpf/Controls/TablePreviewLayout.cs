using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Windows;
using System.Windows.Media;
using GeometryHelper.Dxf;

namespace OpenCAD2D.Wpf
{
    /// <summary>One row of a <see cref="TablePreviewLayout"/>: its cell style, its box in millimetres and where it stands in its group.</summary>
    internal sealed class TablePreviewRow
    {
        internal TablePreviewRow(DxfRowType kind, Rect bounds, bool first, bool last)
        {
            Kind = kind;
            Bounds = bounds;
            First = first;
            Last = last;
        }

        /// <summary>Gets the cell style of the row.</summary>
        internal DxfRowType Kind { get; }

        /// <summary>Gets the row's box in millimetres, y down.</summary>
        internal Rect Bounds { get; }

        /// <summary>Gets whether no row of the same cell style lies above it, so that its top line is the group's.</summary>
        internal bool First { get; }

        /// <summary>Gets whether no row of the same cell style lies below it, so that its bottom line is the group's.</summary>
        internal bool Last { get; }
    }

    /// <summary>
    /// The table the preview of a table style draws, in millimetres: five columns of 25, a title, a header and three rows of data,
    /// each row as tall as its text with the margins above and below (<c>TextHeight * 4/3 + 2 * VerticalMargin</c>), or one row of a
    /// cell style in three columns. A table of the settings 4.5, 6 and 1.5 is 125 by 47: the title 11 high, the header and each row
    /// of data 9. The rows run down from the title, or up from it where the style says so.
    /// </summary>
    internal sealed class TablePreviewLayout
    {
        /// <summary>The width of a column, in millimetres.</summary>
        internal const double ColumnWidth = 25.0;

        private static readonly Typeface Face = new Typeface("Segoe UI");

        private readonly CadTableStyleSettings _settings;
        private readonly Dictionary<DxfRowType, double> _widths = new Dictionary<DxfRowType, double>();

        private TablePreviewLayout(CadTableStyleSettings settings, IReadOnlyList<TablePreviewRow> rows, int columns)
        {
            _settings = settings;
            Rows = rows;
            Columns = columns;
            Width = columns * ColumnWidth;
            Height = rows.Count == 0 ? 0.0 : rows.Max(r => r.Bounds.Bottom);
        }

        /// <summary>Gets the rows, from the top of the table down.</summary>
        internal IReadOnlyList<TablePreviewRow> Rows { get; }

        /// <summary>Gets the number of columns.</summary>
        internal int Columns { get; }

        /// <summary>Gets the width of the table in millimetres.</summary>
        internal double Width { get; }

        /// <summary>Gets the height of the table in millimetres.</summary>
        internal double Height { get; }

        /// <summary>Lays out the table of a style, or one row of a cell style of it.</summary>
        /// <param name="settings">The settings.</param>
        /// <param name="cellStyle">Data, Header or Title for that row alone; null for the whole table.</param>
        /// <returns>The layout.</returns>
        internal static TablePreviewLayout Of(CadTableStyleSettings settings, string cellStyle)
        {
            if (settings == null)
            {
                throw new ArgumentNullException(nameof(settings));
            }

            DxfRowType[] kinds =
                cellStyle != null
                    ? new[] { TableStylePreviewViewModel.KindOf(cellStyle) }
                    : new[]
                    {
                        DxfRowType.TitleRow,
                        DxfRowType.HeaderRow,
                        DxfRowType.DataRow,
                        DxfRowType.DataRow,
                        DxfRowType.DataRow,
                    };
            double[] tall = kinds
                .Select(k => settings.Cell(k).TextHeight * 4.0 / 3.0 + 2.0 * settings.VerticalMargin)
                .ToArray();
            double total = tall.Sum();
            bool up = settings.FlowDirection == DxfFlowDirection.BottomToTop;

            // The rows from the top of the picture down: the title first, or last where the rows run up from it.
            int[] order = Enumerable.Range(0, kinds.Length).ToArray();

            if (up)
            {
                Array.Reverse(order);
            }

            int columns = cellStyle != null ? 3 : 5;
            var rows = new List<TablePreviewRow>(kinds.Length);
            double y = 0.0;

            for (int i = 0; i < order.Length; i++)
            {
                DxfRowType kind = kinds[order[i]];
                bool first = i == 0 || kinds[order[i - 1]] != kind;
                bool last = i == order.Length - 1 || kinds[order[i + 1]] != kind;
                rows.Add(
                    new TablePreviewRow(kind, new Rect(0.0, y, columns * ColumnWidth, tall[order[i]]), first, last)
                );
                y += tall[order[i]];
            }

            return new TablePreviewLayout(settings, rows, columns);
        }

        /// <summary>The word a cell of a kind of row holds in the preview.</summary>
        internal static string WordOf(DxfRowType kind) =>
            kind == DxfRowType.TitleRow ? "Title"
            : kind == DxfRowType.HeaderRow ? "Header"
            : "Data";

        /// <summary>The box of a cell of a row in millimetres.</summary>
        internal Rect Cell(TablePreviewRow row, int column) =>
            new Rect(row.Bounds.Left + column * ColumnWidth, row.Bounds.Top, ColumnWidth, row.Bounds.Height);

        /// <summary>The width, in millimetres, the word of a kind of row takes in the preview's font at the text height of its cell style.</summary>
        internal double WordWidth(DxfRowType kind)
        {
            if (!_widths.TryGetValue(kind, out double width))
            {
                width = new FormattedText(
                    WordOf(kind),
                    CultureInfo.InvariantCulture,
                    FlowDirection.LeftToRight,
                    Face,
                    _settings.Cell(kind).TextHeight,
                    Brushes.Black,
                    1.0
                ).WidthIncludingTrailingWhitespace;
                _widths[kind] = width;
            }

            return width;
        }

        /// <summary>The box of the word of a cell in millimetres: where the alignment of the cell style puts it, inside the margins.</summary>
        internal Rect WordBox(TablePreviewRow row, int column)
        {
            CadTableCellStyleSettings style = _settings.Cell(row.Kind);
            Rect cell = Cell(row, column);
            double width = WordWidth(row.Kind);
            double height = style.TextHeight * 4.0 / 3.0;
            int across = ((int)style.Alignment - 1) % 3;
            int upDown = ((int)style.Alignment - 1) / 3;
            double x =
                across == 0 ? cell.Left + _settings.HorizontalMargin
                : across == 1 ? cell.Left + (cell.Width - width) / 2.0
                : cell.Right - _settings.HorizontalMargin - width;
            double y =
                upDown == 0 ? cell.Top + _settings.VerticalMargin
                : upDown == 1 ? cell.Top + (cell.Height - height) / 2.0
                : cell.Bottom - _settings.VerticalMargin - height;
            return new Rect(x, y, width, height);
        }

        /// <summary>The distance a field sets, measured on the middle cell of the first row of a cell style (the third of five, the second of three).</summary>
        /// <param name="key">The field's key: <c>HorizontalMargin</c>, <c>VerticalMargin</c> or <c>TextHeight</c>; any other sets no distance.</param>
        /// <param name="picked">The cell style whose rows are measured.</param>
        /// <returns>The measure, in millimetres of the table; none where the field sets no distance or no row is of the cell style.</returns>
        internal IReadOnlyList<TableMeasure> Measures(string key, DxfRowType picked)
        {
            TablePreviewRow row = Rows.FirstOrDefault(r => r.Kind == picked);

            if (row == null)
            {
                return Array.Empty<TableMeasure>();
            }

            int column = Columns / 2;
            Rect cell = Cell(row, column);
            Rect word = WordBox(row, column);
            CadTableCellStyleSettings style = _settings.Cell(picked);
            int across = ((int)style.Alignment - 1) % 3;
            int upDown = ((int)style.Alignment - 1) / 3;
            double middle = cell.Top + cell.Height / 2.0;
            double h = _settings.HorizontalMargin;
            double v = _settings.VerticalMargin;

            switch (key)
            {
                case "HorizontalMargin":
                    return new[]
                    {
                        new TableMeasure(
                            key,
                            new Point(across == 2 ? cell.Right - h : cell.Left, middle),
                            new Point(across == 2 ? cell.Right : cell.Left + h, middle),
                            "Horizontal margin = " + Text(h) + (across == 1 ? " · words centred" : string.Empty)
                        ),
                    };
                case "VerticalMargin":
                    double x = cell.Left + h;
                    return new[]
                    {
                        new TableMeasure(
                            key,
                            new Point(x, upDown == 2 ? cell.Bottom - v : cell.Top),
                            new Point(x, upDown == 2 ? cell.Bottom : cell.Top + v),
                            "Vertical margin = " + Text(v)
                        ),
                    };
                case "TextHeight":
                    double top = word.Top + (word.Height - style.TextHeight) / 2.0;
                    return new[]
                    {
                        new TableMeasure(
                            key,
                            new Point(word.Left, top),
                            new Point(word.Left, top + style.TextHeight),
                            "Text height = " + Text(style.TextHeight)
                        ),
                    };
                default:
                    return Array.Empty<TableMeasure>();
            }
        }

        private static string Text(double value) => value.ToString("0.####", CultureInfo.InvariantCulture);
    }
}
