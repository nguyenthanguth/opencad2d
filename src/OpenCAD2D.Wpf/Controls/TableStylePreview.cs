using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Globalization;
using System.Linq;
using System.Windows;
using System.Windows.Input;
using System.Windows.Media;
using GeometryHelper.Dxf;

namespace OpenCAD2D.Wpf
{
    /// <summary>
    /// The preview of a table style, as the Table Style dialogs draw it on white paper and to scale: five columns of 25 mm, a
    /// title, a header and three rows of data, each row as tall as its text with the margins and the words at their text height (a
    /// text height of 4.5 in a frame that shows 2.5 pixels to a millimetre is 11 pixels), or, given a cell style, one row of it in
    /// three columns. Given a <see cref="TableStylePreviewViewModel"/> it also lights what the field being edited or hovered sets
    /// (the rest faded), draws the callout of a margin or a height with a magnifier for a small one, and answers the pointer: a row
    /// of the table picks its cell style, a part of the cell style preview goes to the field that sets it.
    /// </summary>
    public sealed class TableStylePreview : FrameworkElement
    {
        /// <summary>The settings drawn.</summary>
        public static readonly DependencyProperty SettingsProperty = DependencyProperty.Register(
            nameof(Settings),
            typeof(CadTableStyleSettings),
            typeof(TableStylePreview),
            new FrameworkPropertyMetadata(null, FrameworkPropertyMetadataOptions.AffectsRender)
        );

        /// <summary>A number changed whenever the settings drawn change in place, so that the preview is drawn anew.</summary>
        public static readonly DependencyProperty VersionProperty = DependencyProperty.Register(
            nameof(Version),
            typeof(int),
            typeof(TableStylePreview),
            new FrameworkPropertyMetadata(0, FrameworkPropertyMetadataOptions.AffectsRender)
        );

        /// <summary>The cell style drawn alone: Data, Header or Title; null for the whole table.</summary>
        public static readonly DependencyProperty CellStyleProperty = DependencyProperty.Register(
            nameof(CellStyle),
            typeof(string),
            typeof(TableStylePreview),
            new FrameworkPropertyMetadata(null, FrameworkPropertyMetadataOptions.AffectsRender)
        );

        /// <summary>What the preview lights and answers to; null for a plain picture of the style.</summary>
        public static readonly DependencyProperty ModelProperty = DependencyProperty.Register(
            nameof(Model),
            typeof(TableStylePreviewViewModel),
            typeof(TableStylePreview),
            new FrameworkPropertyMetadata(null, (d, e) => ((TableStylePreview)d).OnModelChanged(e))
        );

        /// <summary>The space round the table, in device-independent pixels.</summary>
        private const double Pad = 10.0;

        /// <summary>How far a line may lie from the pointer to be the part hovered, in device-independent pixels.</summary>
        private const double HoverReach = 4.0;

        /// <summary>The length on screen, in device-independent pixels, below which a callout gets a magnifier.</summary>
        private const double SmallCallout = 16.0;

        /// <summary>The shortest frame that gets a magnifier: a short one has no room for its inset.</summary>
        private const double MagnifierMinimumHeight = 160.0;

        private static readonly Typeface Face = new Typeface("Segoe UI");
        private static readonly Color Ink = Color.FromRgb(0x1A, 0x1A, 0x1A);

        private Point _pointer;
        private bool _pointerIn;
        private TableStylePreviewViewModel _subscribed;

        /// <summary>Gets or sets the settings drawn.</summary>
        public CadTableStyleSettings Settings
        {
            get => (CadTableStyleSettings)GetValue(SettingsProperty);
            set => SetValue(SettingsProperty, value);
        }

        /// <summary>Gets or sets the number changed whenever the settings change in place.</summary>
        public int Version
        {
            get => (int)GetValue(VersionProperty);
            set => SetValue(VersionProperty, value);
        }

        /// <summary>Gets or sets the cell style drawn alone; null for the whole table.</summary>
        public string CellStyle
        {
            get => (string)GetValue(CellStyleProperty);
            set => SetValue(CellStyleProperty, value);
        }

        /// <summary>Gets or sets what the preview lights and answers to; null for a plain picture of the style.</summary>
        public TableStylePreviewViewModel Model
        {
            get => (TableStylePreviewViewModel)GetValue(ModelProperty);
            set => SetValue(ModelProperty, value);
        }

        /// <summary>Gets or sets a scale to draw at instead of the display's, for a preview drawn with no window to give it one (a test, a screenshot); null for the display's.</summary>
        internal double? DpiOverride { get; set; }

        /// <summary>Gets the display's scale this preview is drawn at: 1 at 96 dots an inch, 1.5 at 150 %.</summary>
        internal double Dpi => DpiOverride ?? VisualTreeHelper.GetDpi(this).PixelsPerDip;

        /// <summary>Gets how many device-independent pixels a millimetre of the table is drawn as; 0 where nothing is drawn.</summary>
        internal double Scale => Frame(out _, out double scale, out _) ? scale : 0.0;

        /// <summary>Gets the size in device-independent pixels of the words of a kind of row: the text height of its cell style times the scale, not clamped.</summary>
        /// <param name="kind">The kind of row.</param>
        /// <returns>The size of the font, which is the height of the line of text less a third.</returns>
        internal double WordSize(DxfRowType kind) => Settings == null ? 0.0 : Settings.Cell(kind).TextHeight * Scale;

        /// <summary>Gets the rectangle of the magnifier's inset, in device-independent pixels; empty where none is drawn.</summary>
        internal Rect InsetFrame { get; private set; } = Rect.Empty;

        private void OnModelChanged(DependencyPropertyChangedEventArgs e)
        {
            if (_subscribed != null)
            {
                _subscribed.PropertyChanged -= OnModelPropertyChanged;
            }

            _subscribed = e.NewValue as TableStylePreviewViewModel;

            if (_subscribed != null)
            {
                _subscribed.PropertyChanged += OnModelPropertyChanged;
            }

            InvalidateVisual();
        }

        private void OnModelPropertyChanged(object sender, PropertyChangedEventArgs e) => InvalidateVisual();

        /// <summary>Works out where the table goes in the frame: how many pixels a millimetre is and where its top left is, so that it fits and is centred.</summary>
        private bool Frame(out TablePreviewLayout layout, out double scale, out Point origin)
        {
            double width = ActualWidth;
            double height = ActualHeight;
            scale = 0.0;
            origin = default;
            layout = null;

            if (Settings == null || width < 40 || height < 30)
            {
                return false;
            }

            layout = TablePreviewLayout.Of(Settings, CellStyle);
            scale = Math.Min(
                (width - 2.0 * Pad) / Math.Max(layout.Width, 1E-9),
                (height - 2.0 * Pad) / Math.Max(layout.Height, 1E-9)
            );
            origin = new Point((width - layout.Width * scale) / 2.0, (height - layout.Height * scale) / 2.0);
            return true;
        }

        private static Rect Dip(Rect mm, double scale, Point origin) =>
            new Rect(origin.X + mm.X * scale, origin.Y + mm.Y * scale, mm.Width * scale, mm.Height * scale);

        private static Point Dip(Point mm, double scale, Point origin) =>
            new Point(origin.X + mm.X * scale, origin.Y + mm.Y * scale);

        /// <summary>
        /// The part under a point of the preview: a row of the table, the whole row; in the cell style preview a line of the row, its
        /// words, or its fill, in that order of reach.
        /// </summary>
        /// <param name="dip">The point, in device-independent pixels.</param>
        /// <returns>The part; <c>default</c> where there is none.</returns>
        internal TableTarget HitTest(Point dip)
        {
            if (!Frame(out TablePreviewLayout layout, out double scale, out Point origin))
            {
                return default;
            }

            foreach (TablePreviewRow row in layout.Rows)
            {
                Rect box = Dip(row.Bounds, scale, origin);

                if (CellStyle == null)
                {
                    if (box.Contains(dip))
                    {
                        return new TableTarget(row.Kind, TablePart.Cells);
                    }

                    continue;
                }

                CadTableCellStyleSettings style = Settings.Cell(row.Kind);
                Rect around = box;
                around.Inflate(HoverReach, HoverReach);

                if (!around.Contains(dip))
                {
                    continue;
                }

                TablePart edge = EdgeAt(dip, row, layout, box, style);

                if (edge != TablePart.None)
                {
                    return new TableTarget(row.Kind, edge);
                }

                for (int c = 0; c < layout.Columns; c++)
                {
                    if (Dip(layout.WordBox(row, c), scale, origin).Contains(dip))
                    {
                        return new TableTarget(row.Kind, TablePart.Text);
                    }
                }

                if (style.FillColor != null && box.Contains(dip))
                {
                    return new TableTarget(row.Kind, TablePart.Fill);
                }
            }

            return default;
        }

        private static TablePart EdgeAt(
            Point dip,
            TablePreviewRow row,
            TablePreviewLayout layout,
            Rect box,
            CadTableCellStyleSettings style
        )
        {
            double each = box.Width / layout.Columns;
            double nearest = HoverReach + 1E-9;
            TablePart found = TablePart.None;

            void Try(int line, double distance)
            {
                if (style.LinesShown[line] && distance < nearest)
                {
                    nearest = distance;
                    found = (TablePart)(4 + line);
                }
            }

            bool within = dip.X >= box.Left - HoverReach && dip.X <= box.Right + HoverReach;
            bool beside = dip.Y >= box.Top - HoverReach && dip.Y <= box.Bottom + HoverReach;

            if (within)
            {
                Try(row.First ? 0 : 1, Math.Abs(dip.Y - box.Top));
                Try(row.Last ? 2 : 1, Math.Abs(dip.Y - box.Bottom));
            }

            if (beside)
            {
                Try(3, Math.Abs(dip.X - box.Left));
                Try(5, Math.Abs(dip.X - box.Right));

                for (int c = 1; c < layout.Columns; c++)
                {
                    Try(4, Math.Abs(dip.X - (box.Left + c * each)));
                }
            }

            return found;
        }

        /// <inheritdoc/>
        protected override void OnRender(DrawingContext dc)
        {
            dc.DrawRectangle(Brushes.White, null, new Rect(0, 0, ActualWidth, ActualHeight));

            if (!Frame(out TablePreviewLayout layout, out double scale, out Point origin))
            {
                return;
            }

            double dip = Dpi;
            DrawTable(dc, layout, scale, origin, dip);
            TableStylePreviewViewModel model = Model;
            InsetFrame = Rect.Empty;

            if (model == null)
            {
                return;
            }

            var within = new Rect(0.0, 0.0, ActualWidth, ActualHeight);
            DrawMeasures(dc, model, layout, scale, origin, within, dip);

            if (CellStyle == null && model.Chip.Length > 0)
            {
                Rect box = StylePreviewOverlay.ChipBox(model.Chip, default, dip, within, out _);
                StylePreviewOverlay.Chip(
                    dc,
                    model.Chip,
                    new Point(8.0 + box.Width / 2.0, ActualHeight - 8.0 - box.Height / 2.0),
                    StylePreviewGround.White,
                    dip,
                    within
                );
            }

            if (model.Hover.Part != TablePart.None && _pointerIn)
            {
                StylePreviewOverlay.Tooltip(
                    dc,
                    model.HoverText
                        + (model.Hover.Part == TablePart.Cells ? " · click to pick it" : " · click to edit"),
                    _pointer,
                    within,
                    dip,
                    model.Hover.Part == TablePart.Cells ? "pick it" : "edit"
                );
            }
        }

        /// <summary>
        /// Draws the callout of the distance the active field sets in place, as a line with a tick at each end, and its label in a chip
        /// below the table (above it where there is no room); for a small distance in a frame with room for it, also the magnifier.
        /// </summary>
        private void DrawMeasures(
            DrawingContext dc,
            TableStylePreviewViewModel model,
            TablePreviewLayout layout,
            double scale,
            Point origin,
            Rect within,
            double dip
        )
        {
            Rect table = Dip(new Rect(0.0, 0.0, layout.Width, layout.Height), scale, origin);

            foreach (TableMeasure measure in model.Measures(layout))
            {
                Point a = Dip(measure.From, scale, origin);
                Point b = Dip(measure.To, scale, origin);
                DrawMeasure(dc, a, b);
                Rect labelSize = StylePreviewOverlay.ChipBox(measure.Label, default, dip, within, out _);
                double y = table.Bottom + 6.0 + labelSize.Height / 2.0;

                if (y + labelSize.Height / 2.0 > ActualHeight - 2.0)
                {
                    y = table.Top - 6.0 - labelSize.Height / 2.0;
                }

                var at = new Point((a.X + b.X) / 2.0, y);
                Rect chip = StylePreviewOverlay.ChipBox(measure.Label, at, dip, within, out _);
                StylePreviewOverlay.Chip(dc, measure.Label, at, StylePreviewGround.White, dip, within);

                if (
                    (b - a).Length >= SmallCallout
                    || ActualHeight < MagnifierMinimumHeight
                    || measure.Key == "TextHeight"
                )
                {
                    continue;
                }

                double zoom = (b - a).Length < 6.0 ? 8.0 : 4.0;
                var middle = new Point((a.X + b.X) / 2.0, (a.Y + b.Y) / 2.0);
                Size inset = StylePreviewOverlay.InsetSize;
                Rect frame = FreeCorner(table, chip, inset, middle);
                InsetFrame = frame;
                var centre = new Point(frame.X + inset.Width / 2.0, frame.Y + inset.Height / 2.0);

                // The table drawn again about the region at the magnified scale, its lines as thick as ever.
                var shifted = new Point(
                    centre.X - (middle.X - origin.X) * zoom,
                    centre.Y - (middle.Y - origin.Y) * zoom
                );
                StylePreviewOverlay.Region(dc, middle, inset.Height / 2.0 / zoom, StylePreviewGround.White);
                StylePreviewOverlay.Inset(
                    dc,
                    frame,
                    c =>
                    {
                        DrawTable(c, layout, scale * zoom, shifted, dip);
                        DrawMeasure(
                            c,
                            Dip(measure.From, scale * zoom, shifted),
                            Dip(measure.To, scale * zoom, shifted)
                        );
                    },
                    "x" + zoom.ToString("0", CultureInfo.InvariantCulture),
                    StylePreviewGround.White,
                    dip
                );
                break;
            }
        }

        /// <summary>A distance: a line in the lit colour with a tick across each end.</summary>
        private static void DrawMeasure(DrawingContext dc, Point a, Point b)
        {
            var pen = new Pen(Solid(StylePreviewOverlay.Lit(StylePreviewGround.White)), 1.0);
            pen.Freeze();
            dc.DrawLine(pen, a, b);
            Vector line = b - a;

            if (line.Length < 1E-9)
            {
                return;
            }

            line.Normalize();
            var tick = new Vector(-line.Y, line.X) * (StylePreviewOverlay.TickLength / 2.0);
            dc.DrawLine(pen, a - tick, a + tick);
            dc.DrawLine(pen, b - tick, b + tick);
        }

        /// <summary>The corner of the frame the inset goes in: the one where the table and the label draw least under it, the one farthest from the callout where two draw as little.</summary>
        private Rect FreeCorner(Rect table, Rect label, Size size, Point callout)
        {
            const double Gap = 8.0;
            double width = Math.Min(size.Width, ActualWidth - 2.0 * Gap);
            double height = Math.Min(size.Height, ActualHeight - 2.0 * Gap);
            Rect best = Rect.Empty;
            double bestCost = double.PositiveInfinity;
            double bestFar = -1.0;

            foreach (bool right in new[] { false, true })
            {
                foreach (bool bottom in new[] { false, true })
                {
                    var frame = new Rect(
                        right ? ActualWidth - width - Gap : Gap,
                        bottom ? ActualHeight - height - Gap : Gap,
                        width,
                        height
                    );
                    double cost = Overlap(table, frame) + 10.0 * Overlap(label, frame);
                    double far = (new Point(frame.X + width / 2.0, frame.Y + height / 2.0) - callout).Length;

                    if (cost < bestCost - 1E-6 || (cost <= bestCost + 1E-6 && far > bestFar))
                    {
                        best = frame;
                        bestCost = Math.Min(bestCost, cost);
                        bestFar = far;
                    }
                }
            }

            return best;
        }

        private static double Overlap(Rect a, Rect b)
        {
            Rect common = Rect.Intersect(a, b);
            return common.IsEmpty ? 0.0 : common.Width * common.Height;
        }

        /// <summary>The table: in two passes, what is not lit and then what is, so that nothing faded is drawn over a lit line.</summary>
        private void DrawTable(DrawingContext dc, TablePreviewLayout layout, double scale, Point origin, double dip)
        {
            // What is not lit is faded where something of these rows is lit; a preview of other rows is left as it is.
            bool fade = Model != null && layout.Rows.Any(r => Model.IsLitKind(r.Kind));

            foreach (bool litPass in new[] { false, true })
            {
                foreach (TablePreviewRow row in layout.Rows)
                {
                    DrawRow(dc, layout, row, scale, origin, litPass, fade, dip);
                }
            }
        }

        /// <summary>A row of cells of a cell style: its fill, its lines, and its word in each cell where its alignment says.</summary>
        private void DrawRow(
            DrawingContext dc,
            TablePreviewLayout layout,
            TablePreviewRow row,
            double scale,
            Point origin,
            bool litPass,
            bool fade,
            double dip
        )
        {
            TableStylePreviewViewModel model = Model;
            CadTableStyleSettings settings = Settings;
            CadTableCellStyleSettings cell = settings.Cell(row.Kind);
            Rect box = Dip(row.Bounds, scale, origin);
            Color lit = StylePreviewOverlay.Lit(StylePreviewGround.White);
            bool litFill = model != null && model.IsLitPart(row.Kind, TablePart.Fill);
            bool whole = model != null && model.IsLitPart(row.Kind, TablePart.Cells);

            if (cell.FillColor != null && !litPass)
            {
                Color fill = Shown(cell.FillColor, Color.FromRgb(0xD9, 0xDD, 0xE3));
                dc.DrawRectangle(Solid(fade && !(litFill || whole) ? Faded(fill) : fill), null, box);
            }

            if (litPass && litFill)
            {
                if (model.Ghost)
                {
                    StylePreviewOverlay.GhostBox(dc, box, StylePreviewGround.White);
                }
                else
                {
                    var inner = box;
                    inner.Inflate(-1.25, -1.25);
                    dc.DrawRectangle(null, new Pen(Solid(lit), 2.5), inner);
                }
            }

            double each = box.Width / layout.Columns;

            void Edge(int line, Point from, Point to) =>
                DrawLine(dc, cell, row.Kind, line, from, to, litPass, fade, model);

            Edge(row.First ? 0 : 1, box.TopLeft, box.TopRight);
            Edge(row.Last ? 2 : 1, box.BottomLeft, box.BottomRight);
            Edge(3, box.TopLeft, box.BottomLeft);
            Edge(5, box.TopRight, box.BottomRight);

            for (int c = 1; c < layout.Columns; c++)
            {
                Edge(4, new Point(box.Left + c * each, box.Top), new Point(box.Left + c * each, box.Bottom));
            }

            bool litText = model != null && (whole || model.IsLitPart(row.Kind, TablePart.Text));

            if (litText == litPass)
            {
                DrawWords(dc, layout, row, cell, scale, origin, litText, fade, dip);
            }

            if (litPass && litText && model.ActiveKey == "Alignment" && model.Hover.Part == TablePart.None)
            {
                DrawAnchors(dc, layout, row, cell, scale, origin);
            }
        }

        private static void DrawLine(
            DrawingContext dc,
            CadTableCellStyleSettings cell,
            DxfRowType kind,
            int line,
            Point from,
            Point to,
            bool litPass,
            bool fade,
            TableStylePreviewViewModel model
        )
        {
            bool isLit = model != null && model.IsLit(kind, (TablePart)(4 + line));
            Color colour = Shown(cell.LineColors[line], Ink);
            DxfLineWeight weight = cell.LineWeights[line];
            double thickness = (int)weight > 0 ? Math.Max(1.0, (int)weight / 25.0) : 1.0;

            if (isLit != litPass)
            {
                return;
            }

            if (!isLit)
            {
                if (cell.LinesShown[line])
                {
                    dc.DrawLine(new Pen(Solid(fade ? Faded(colour) : colour), thickness), from, to);
                }

                return;
            }

            Color lit = StylePreviewOverlay.Lit(StylePreviewGround.White);

            if (model.Ghost)
            {
                StylePreviewOverlay.Ghost(dc, from, to, StylePreviewGround.White);
            }
            else
            {
                dc.DrawLine(new Pen(Solid(lit), Math.Max(thickness, 2.5)), from, to);
            }
        }

        private void DrawWords(
            DrawingContext dc,
            TablePreviewLayout layout,
            TablePreviewRow row,
            CadTableCellStyleSettings cell,
            double scale,
            Point origin,
            bool litText,
            bool fade,
            double dip
        )
        {
            Color colour = litText ? StylePreviewOverlay.Lit(StylePreviewGround.White) : Shown(cell.TextColor, Ink);
            var text = new FormattedText(
                TablePreviewLayout.WordOf(row.Kind),
                CultureInfo.InvariantCulture,
                FlowDirection.LeftToRight,
                Face,
                Math.Max(cell.TextHeight * scale, 0.5),
                Solid(fade && !litText ? Faded(colour) : colour),
                dip
            );

            for (int c = 0; c < layout.Columns; c++)
            {
                Rect word = Dip(layout.WordBox(row, c), scale, origin);
                dc.PushClip(new RectangleGeometry(Dip(layout.Cell(row, c), scale, origin)));
                dc.DrawText(text, word.TopLeft);
                dc.Pop();
            }
        }

        /// <summary>A dot at the point of each cell its alignment puts the words at, inside the margins.</summary>
        private void DrawAnchors(
            DrawingContext dc,
            TablePreviewLayout layout,
            TablePreviewRow row,
            CadTableCellStyleSettings cell,
            double scale,
            Point origin
        )
        {
            int across = ((int)cell.Alignment - 1) % 3;
            int upDown = ((int)cell.Alignment - 1) / 3;
            double h = Settings.HorizontalMargin;
            double v = Settings.VerticalMargin;
            Brush brush = Solid(StylePreviewOverlay.Lit(StylePreviewGround.White));

            for (int c = 0; c < layout.Columns; c++)
            {
                Rect box = layout.Cell(row, c);
                double x =
                    across == 0 ? box.Left + h
                    : across == 1 ? box.Left + box.Width / 2.0
                    : box.Right - h;
                double y =
                    upDown == 0 ? box.Top + v
                    : upDown == 1 ? box.Top + box.Height / 2.0
                    : box.Bottom - v;
                dc.DrawEllipse(brush, null, Dip(new Point(x, y), scale, origin), 3.0, 3.0);
            }
        }

        /// <summary>A colour as the preview shows it on white: by block, by layer and white drawn dark, as AutoCAD's preview does.</summary>
        private static Color Shown(DxfColor color, Color otherwise)
        {
            if (color == null || color.IsByBlock || color.IsByLayer || color.ColorIndex == 7)
            {
                return otherwise;
            }

            int rgb = color.ColorValue & 0xFFFFFF;
            return Color.FromRgb((byte)(rgb >> 16), (byte)(rgb >> 8), (byte)rgb);
        }

        /// <summary>A colour taken 65 % of the way to white: what is not lit while something is.</summary>
        private static Color Faded(Color colour) =>
            Color.FromRgb(
                (byte)Math.Round(colour.R + (255 - colour.R) * 0.65),
                (byte)Math.Round(colour.G + (255 - colour.G) * 0.65),
                (byte)Math.Round(colour.B + (255 - colour.B) * 0.65)
            );

        private static Brush Solid(Color colour)
        {
            var brush = new SolidColorBrush(colour);
            brush.Freeze();
            return brush;
        }

        /// <inheritdoc/>
        protected override void OnMouseMove(MouseEventArgs e)
        {
            base.OnMouseMove(e);

            if (Model == null)
            {
                return;
            }

            _pointer = e.GetPosition(this);
            _pointerIn = true;
            TableTarget target = HitTest(_pointer);
            Model.PointerAt(target);
            Cursor = target.Part != TablePart.None ? Cursors.Hand : Cursors.Arrow;

            if (target.Part != TablePart.None)
            {
                InvalidateVisual();
            }
        }

        /// <inheritdoc/>
        protected override void OnMouseLeave(MouseEventArgs e)
        {
            base.OnMouseLeave(e);
            _pointerIn = false;
            Model?.PointerAt(default);
        }

        /// <inheritdoc/>
        protected override void OnMouseLeftButtonUp(MouseButtonEventArgs e)
        {
            base.OnMouseLeftButtonUp(e);
            Model?.Click();
        }
    }
}
