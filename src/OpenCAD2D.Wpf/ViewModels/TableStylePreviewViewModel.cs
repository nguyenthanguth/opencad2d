using System;
using System.Collections.Generic;
using System.Linq;
using System.Windows;
using GeometryHelper.Dxf;

namespace OpenCAD2D.Wpf
{
    /// <summary>
    /// The parts of a row of a table that a field of the Modify Table Style dialog sets: the whole row, its fill, its words, and
    /// its six lines. The lines are in the order of <see cref="CadTableCellStyleSettings.LinesShown"/>.
    /// </summary>
    internal enum TablePart
    {
        None,
        Cells,
        Fill,
        Text,
        TopEdge,
        InsideHorizontal,
        BottomEdge,
        LeftEdge,
        InsideVertical,
        RightEdge,
    }

    /// <summary>A part of the rows of one cell style: what the pointer is over, or what a field lights.</summary>
    internal struct TableTarget : IEquatable<TableTarget>
    {
        internal TableTarget(DxfRowType kind, TablePart part)
        {
            Kind = kind;
            Part = part;
        }

        /// <summary>Gets the cell style of the rows: the title, the header or the data.</summary>
        internal DxfRowType Kind { get; }

        /// <summary>Gets the part; <see cref="TablePart.None"/> for nothing.</summary>
        internal TablePart Part { get; }

        public bool Equals(TableTarget other) => Kind == other.Kind && Part == other.Part;

        public override bool Equals(object obj) => obj is TableTarget other && Equals(other);

        public override int GetHashCode() => ((int)Kind * 397) ^ (int)Part;
    }

    /// <summary>A distance the preview of a table style measures, in millimetres of the table, y down.</summary>
    internal sealed class TableMeasure
    {
        internal TableMeasure(string key, Point from, Point to, string label)
        {
            Key = key;
            From = from;
            To = to;
            Label = label;
        }

        /// <summary>Gets the key of the setting: <c>HorizontalMargin</c>.</summary>
        internal string Key { get; }

        /// <summary>Gets where the distance starts.</summary>
        internal Point From { get; }

        /// <summary>Gets where it ends.</summary>
        internal Point To { get; }

        /// <summary>Gets the words, as <c>Horizontal margin = 1.5</c>.</summary>
        internal string Label { get; }

        /// <summary>Gets the length of the distance.</summary>
        internal double Length => (To - From).Length;
    }

    /// <summary>
    /// What the previews of the Modify Table Style dialog show of the field being edited or hovered, and what they are asked: the
    /// parts of the rows it sets (lit, the rest faded), the callout of the margin or the height it sets, the chip for a field with
    /// no part to light, and the part under the pointer with the fields that set it. It holds no picture and knows no window; both
    /// previews of the dialog draw what it holds.
    /// </summary>
    public sealed class TableStylePreviewViewModel : ObservableObject
    {
        private static readonly DxfRowType[] AllRows =
        {
            DxfRowType.TitleRow,
            DxfRowType.HeaderRow,
            DxfRowType.DataRow,
        };

        private readonly TableStyleModifyViewModel _owner;
        private TableTarget _hover;
        private string _activeKey;
        private HashSet<TableTarget> _lit;
        private bool _ghost;

        internal TableStylePreviewViewModel(TableStyleModifyViewModel owner)
        {
            _owner = owner ?? throw new ArgumentNullException(nameof(owner));
        }

        /// <summary>Raised when a click on a part asks for the first field that sets it; the argument is the field's key, as <c>TextHeight</c>.</summary>
        internal event EventHandler<string> FieldRequested;

        /// <summary>
        /// Gets the chip for a field that has no part to light, or lights a part that is not drawn: <c>Suppressed</c> for a fill of
        /// none, the direction of the rows, the format; empty for none.
        /// </summary>
        public string Chip
        {
            get
            {
                if (_hover.Part != TablePart.None || _activeKey == null)
                {
                    return string.Empty;
                }

                switch (_activeKey)
                {
                    case "Direction":
                        return "Table direction = " + (_owner.Direction == "Up" ? "Up ↑" : "Down ↓");
                    case "Format":
                        return "Format = " + _owner.FormatText;
                    case "Fill":
                        return Ghost ? "Suppressed" : string.Empty;
                    default:
                        return string.Empty;
                }
            }
        }

        /// <summary>Gets what the tooltip under the pointer says: <c>Header · cell style</c> for a row, or the part and the fields that set it as <c>Words · Alignment, Text height</c>; empty where nothing is hovered.</summary>
        public string HoverText
        {
            get
            {
                if (_hover.Part == TablePart.None)
                {
                    return string.Empty;
                }

                string kind = NameOf(_hover.Kind);

                if (_hover.Part == TablePart.Cells)
                {
                    return kind + " · cell style";
                }

                return PartName(_hover.Part) + " · " + string.Join(", ", HoverKeysInOrder().Select(LabelOf));
            }
        }

        /// <summary>Gets the part under the pointer; <see cref="TablePart.None"/> where there is none.</summary>
        internal TableTarget Hover => _hover;

        /// <summary>Gets the key of the field that is active; null for none.</summary>
        internal string ActiveKey => _activeKey;

        /// <summary>Gets whether something is lit, so that the rest is faded.</summary>
        internal bool HasLit => Lit.Count > 0;

        /// <summary>Gets whether what is lit is shown as a ghost, dashed: the fill a cell style has none of, the lines the None button would hide.</summary>
        internal bool Ghost
        {
            get
            {
                _ = Lit;
                return _ghost;
            }
        }

        /// <summary>Gets the keys of the fields that set the part under the pointer, those of the tab shown first, each group in the dialog's order.</summary>
        internal IReadOnlyList<string> HoverKeys => HoverKeysInOrder().ToList();

        private HashSet<TableTarget> Lit => _lit ?? (_lit = Compute());

        /// <summary>Whether a kind of row has a part lit; a row lit whole has every part lit.</summary>
        /// <param name="kind">The cell style of the row.</param>
        /// <param name="part">The part.</param>
        /// <returns>True where it is lit.</returns>
        internal bool IsLit(DxfRowType kind, TablePart part) =>
            Lit.Contains(new TableTarget(kind, part)) || Lit.Contains(new TableTarget(kind, TablePart.Cells));

        /// <summary>Whether anything of a kind of row is lit, so that a preview that shows rows of that kind fades what is not.</summary>
        /// <param name="kind">The cell style of the rows.</param>
        /// <returns>True where some part of those rows is lit.</returns>
        internal bool IsLitKind(DxfRowType kind) => Lit.Any(t => t.Kind == kind);

        /// <summary>Whether a part of a kind of row is lit on its own, not just because its whole row is.</summary>
        /// <param name="kind">The cell style of the row.</param>
        /// <param name="part">The part.</param>
        /// <returns>True where that part is among those lit.</returns>
        internal bool IsLitPart(DxfRowType kind, TablePart part) => Lit.Contains(new TableTarget(kind, part));

        /// <summary>The keys of the fields that set a part, in the dialog's order: the General tab, the Text tab, the Borders tab.</summary>
        internal static IReadOnlyList<string> KeysOf(TablePart part)
        {
            switch (part)
            {
                case TablePart.Cells:
                    return new[] { "CellStyle" };
                case TablePart.Fill:
                    return new[] { "Fill" };
                case TablePart.Text:
                    return new[]
                    {
                        "Alignment",
                        "Format",
                        "HorizontalMargin",
                        "VerticalMargin",
                        "TextStyle",
                        "TextHeight",
                        "TextColor",
                    };
                case TablePart.None:
                    return Array.Empty<string>();
                default:
                    return new[] { "BorderWeight", "BorderColor" };
            }
        }

        /// <summary>The tab a field is in; null for a field that is in none (the cell style box, the direction).</summary>
        internal static string TabOf(string key)
        {
            switch (key)
            {
                case "Fill":
                case "Alignment":
                case "Format":
                case "HorizontalMargin":
                case "VerticalMargin":
                    return "General";
                case "TextStyle":
                case "TextHeight":
                case "TextColor":
                    return "Text";
                case "BorderWeight":
                case "BorderColor":
                    return "Borders";
                default:
                    return key != null && key.StartsWith("Border:", StringComparison.Ordinal) ? "Borders" : null;
            }
        }

        /// <summary>The lines a border button gives, as parts: All, Outside, Inside, Bottom, Left, Top, Right; None names every line.</summary>
        internal static IReadOnlyList<TablePart> EdgesOf(string button)
        {
            switch (button)
            {
                case "Outside":
                    return new[] { TablePart.TopEdge, TablePart.BottomEdge, TablePart.LeftEdge, TablePart.RightEdge };
                case "Inside":
                    return new[] { TablePart.InsideHorizontal, TablePart.InsideVertical };
                case "Top":
                    return new[] { TablePart.TopEdge };
                case "Bottom":
                    return new[] { TablePart.BottomEdge };
                case "Left":
                    return new[] { TablePart.LeftEdge };
                case "Right":
                    return new[] { TablePart.RightEdge };
                default:
                    return new[]
                    {
                        TablePart.TopEdge,
                        TablePart.InsideHorizontal,
                        TablePart.BottomEdge,
                        TablePart.LeftEdge,
                        TablePart.InsideVertical,
                        TablePart.RightEdge,
                    };
            }
        }

        /// <summary>Tells the preview which field is active, by its key (<c>TextHeight</c>, <c>Border:Outside</c>); null for none.</summary>
        internal void SetActiveKey(string key)
        {
            string next = string.IsNullOrEmpty(key) ? null : key;

            if (!string.Equals(next, _activeKey, StringComparison.Ordinal))
            {
                _activeKey = next;
                Reset();
            }
        }

        /// <summary>Tells the preview a value or the cell style picked changed, so that what is lit is worked out again.</summary>
        internal void Invalidate() => Reset();

        /// <summary>Tells the preview which part the pointer is over, as the view finds it; <c>default</c> for none.</summary>
        /// <param name="target">The part.</param>
        internal void PointerAt(TableTarget target)
        {
            if (target.Part == TablePart.None)
            {
                target = default;
            }

            if (!target.Equals(_hover))
            {
                _hover = target;
                Reset();
            }
        }

        /// <summary>A click on the part under the pointer: a row picks its cell style; a part asks for the first field that sets it.</summary>
        internal void Click()
        {
            if (_hover.Part == TablePart.None)
            {
                return;
            }

            if (_hover.Part == TablePart.Cells)
            {
                _owner.CellStyle = NameOf(_hover.Kind);
                return;
            }

            string first = HoverKeysInOrder().FirstOrDefault();

            if (first != null)
            {
                FieldRequested?.Invoke(this, first);
            }
        }

        /// <summary>The callout of the margin or the height the active field sets, on a layout of the table.</summary>
        /// <param name="layout">The layout the preview draws.</param>
        /// <returns>The measures; none while a part is hovered or where the field sets no distance.</returns>
        internal IReadOnlyList<TableMeasure> Measures(TablePreviewLayout layout) =>
            _hover.Part != TablePart.None || _activeKey == null
                ? Array.Empty<TableMeasure>()
                : layout.Measures(_activeKey, KindOf(_owner.CellStyle));

        internal static DxfRowType KindOf(string cellStyle) =>
            cellStyle == "Title" ? DxfRowType.TitleRow
            : cellStyle == "Header" ? DxfRowType.HeaderRow
            : DxfRowType.DataRow;

        internal static string NameOf(DxfRowType kind) =>
            kind == DxfRowType.TitleRow ? "Title"
            : kind == DxfRowType.HeaderRow ? "Header"
            : "Data";

        private void Reset()
        {
            _lit = null;
            _ghost = false;
            Raise(nameof(Chip));
            Raise(nameof(HoverText));
            Raise(nameof(HasLit));
        }

        private IEnumerable<string> HoverKeysInOrder()
        {
            IReadOnlyList<string> keys = KeysOf(_hover.Part);
            return keys.Where(k => TabOf(k) == _owner.Tab).Concat(keys.Where(k => TabOf(k) != _owner.Tab));
        }

        private HashSet<TableTarget> Compute()
        {
            var lit = new HashSet<TableTarget>();
            _ghost = false;

            if (_hover.Part != TablePart.None)
            {
                lit.Add(_hover);
                return lit;
            }

            if (_activeKey == null)
            {
                return lit;
            }

            DxfRowType picked = KindOf(_owner.CellStyle);
            CadTableCellStyleSettings cell = _owner.Settings.Cell(picked);

            switch (_activeKey)
            {
                case "Direction":
                    AddRows(lit, AllRows, TablePart.Cells);
                    break;
                case "CellStyle":
                    lit.Add(new TableTarget(picked, TablePart.Cells));
                    break;
                case "Fill":
                    lit.Add(new TableTarget(picked, TablePart.Fill));
                    _ghost = cell.FillColor == null;
                    break;
                case "Alignment":
                case "Format":
                case "HorizontalMargin":
                case "VerticalMargin":
                case "TextStyle":
                case "TextHeight":
                case "TextColor":
                    lit.Add(new TableTarget(picked, TablePart.Text));
                    break;
                case "BorderWeight":
                case "BorderColor":
                    AddEdges(lit, picked, _owner.LastBorder);
                    break;
                default:
                    if (_activeKey.StartsWith("Border:", StringComparison.Ordinal))
                    {
                        string button = _activeKey.Substring("Border:".Length);
                        AddEdges(lit, picked, button);
                        _ghost = button == "None";
                    }

                    break;
            }

            return lit;
        }

        private static void AddRows(HashSet<TableTarget> lit, IEnumerable<DxfRowType> kinds, TablePart part)
        {
            foreach (DxfRowType kind in kinds)
            {
                lit.Add(new TableTarget(kind, part));
            }
        }

        private static void AddEdges(HashSet<TableTarget> lit, DxfRowType kind, string button)
        {
            foreach (TablePart edge in EdgesOf(button))
            {
                lit.Add(new TableTarget(kind, edge));
            }
        }

        private static string PartName(TablePart part)
        {
            switch (part)
            {
                case TablePart.Fill:
                    return "Fill";
                case TablePart.Text:
                    return "Words";
                case TablePart.TopEdge:
                    return "Top border";
                case TablePart.BottomEdge:
                    return "Bottom border";
                case TablePart.LeftEdge:
                    return "Left border";
                case TablePart.RightEdge:
                    return "Right border";
                case TablePart.InsideHorizontal:
                    return "Inside horizontal border";
                case TablePart.InsideVertical:
                    return "Inside vertical border";
                default:
                    return "Cells";
            }
        }

        private static string LabelOf(string key)
        {
            switch (key)
            {
                case "Fill":
                    return "Fill color";
                case "HorizontalMargin":
                    return "Horizontal margin";
                case "VerticalMargin":
                    return "Vertical margin";
                case "TextStyle":
                    return "Text style";
                case "TextHeight":
                    return "Text height";
                case "TextColor":
                    return "Text color";
                case "BorderWeight":
                    return "Lineweight";
                case "BorderColor":
                    return "Color";
                case "CellStyle":
                    return "Cell style";
                default:
                    return key;
            }
        }
    }
}
