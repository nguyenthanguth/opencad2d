using System;
using System.Collections.Generic;
using System.Globalization;
using System.Windows;
using System.Windows.Media;

namespace OpenCAD2D.Wpf
{
    /// <summary>
    /// What the style previews draw over the sample in WPF's own vector drawing, sharp at any scale: the callout of a measured
    /// distance with its label, the ghost of a part left out, the chip, the frame of the magnifier and the tooltip of the part under
    /// the pointer. Every length is in device-independent pixels.
    /// </summary>
    internal static class StylePreviewOverlay
    {
        /// <summary>How far a callout stands off the part it measures, on its free side.</summary>
        internal const double CalloutOffset = 18.0;

        /// <summary>The length of the ticks at the ends of a callout.</summary>
        internal const double TickLength = 5.0;

        /// <summary>The size of the magnifier's inset.</summary>
        internal static readonly Size InsetSize = new Size(132.0, 100.0);

        private static readonly Typeface Mono = new Typeface(
            new FontFamily("JetBrains Mono, Cascadia Mono, Consolas"),
            FontStyles.Normal,
            FontWeights.Normal,
            FontStretches.Normal
        );

        private static readonly Typeface Words = new Typeface("Segoe UI");

        /// <summary>The colour a lit part and its callout are drawn in on a ground.</summary>
        internal static Color Lit(StylePreviewGround ground) =>
            ground == StylePreviewGround.White ? Color.FromRgb(0x0A, 0x6B, 0xD6) : Color.FromRgb(0x3D, 0x9B, 0xFF);

        private static Brush Solid(Color color)
        {
            var brush = new SolidColorBrush(color);
            brush.Freeze();
            return brush;
        }

        private static Color InkOnLit(StylePreviewGround ground) =>
            ground == StylePreviewGround.White ? Colors.White : Color.FromRgb(0x0B, 0x12, 0x20);

        /// <summary>Draws a chip: words on a rounded ground of the lit colour; returns the rectangle it took.</summary>
        /// <param name="dc">The drawing context.</param>
        /// <param name="text">The words.</param>
        /// <param name="centre">The middle of the chip.</param>
        /// <param name="ground">The ground of the preview.</param>
        /// <param name="pixelsPerDip">The display's scale.</param>
        /// <param name="within">The rectangle the chip is kept inside.</param>
        internal static Rect Chip(
            DrawingContext dc,
            string text,
            Point centre,
            StylePreviewGround ground,
            double pixelsPerDip,
            Rect within
        )
        {
            Rect box = ChipBox(text, centre, pixelsPerDip, within, out FormattedText formatted);
            dc.DrawRoundedRectangle(Solid(Lit(ground)), null, box, 3.0, 3.0);
            formatted.SetForegroundBrush(Solid(InkOnLit(ground)));
            dc.DrawText(formatted, new Point(box.X + 6.0, box.Y + 3.0));
            return box;
        }

        /// <summary>The rectangle a chip of words takes with its middle at a point, moved inside a rectangle where it would stand out.</summary>
        internal static Rect ChipBox(
            string text,
            Point centre,
            double pixelsPerDip,
            Rect within,
            out FormattedText formatted
        )
        {
            formatted = new FormattedText(
                text,
                CultureInfo.InvariantCulture,
                FlowDirection.LeftToRight,
                Mono,
                11.0,
                Brushes.Black,
                pixelsPerDip
            );
            double w = formatted.Width + 12.0;
            double h = formatted.Height + 6.0;
            double x = Math.Max(within.Left + 2.0, Math.Min(within.Right - w - 2.0, centre.X - w / 2.0));
            double y = Math.Max(within.Top + 2.0, Math.Min(within.Bottom - h - 2.0, centre.Y - h / 2.0));
            return new Rect(x, y, w, h);
        }

        /// <summary>
        /// Draws the callout of a distance from <paramref name="a"/> to <paramref name="b"/>: the measure stood off the part on the side
        /// away from <paramref name="away"/>, its witness lines, a tick at each end and the label in a chip beside it.
        /// </summary>
        /// <param name="dc">The drawing context.</param>
        /// <param name="a">Where the distance starts.</param>
        /// <param name="b">Where it ends.</param>
        /// <param name="away">A point the callout stands away from: the middle of the sample.</param>
        /// <param name="label">The words, as <c>DIMEXE = 1.25</c>.</param>
        /// <param name="ground">The ground of the preview.</param>
        /// <param name="pixelsPerDip">The display's scale.</param>
        /// <param name="within">The rectangle the label is kept inside.</param>
        internal static void Callout(
            DrawingContext dc,
            Point a,
            Point b,
            Point away,
            string label,
            StylePreviewGround ground,
            double pixelsPerDip,
            Rect within
        )
        {
            Vector line = b - a;
            double length = line.Length;
            Vector along = length > 1E-9 ? line / length : new Vector(1.0, 0.0);
            var normal = new Vector(-along.Y, along.X);
            Point middle = a + line / 2.0;
            double side = normal * (middle - away) >= 0.0 ? 1.0 : -1.0;
            Vector off = normal * (side * CalloutOffset);
            Point a2 = a + off;
            Point b2 = b + off;
            var pen = new Pen(Solid(Lit(ground)), 1.0);
            pen.Freeze();
            var witness = new Pen(Solid(Color.FromArgb(150, Lit(ground).R, Lit(ground).G, Lit(ground).B)), 1.0);
            witness.Freeze();
            dc.DrawLine(witness, a, a2);
            dc.DrawLine(witness, b, b2);
            dc.DrawLine(pen, a2, b2);
            Vector tick = normal * (TickLength / 2.0);
            dc.DrawLine(pen, a2 - tick, a2 + tick);
            dc.DrawLine(pen, b2 - tick, b2 + tick);
            ChipBox(label, new Point(0, 0), pixelsPerDip, within, out FormattedText measured);
            double reach =
                (measured.Width + 12.0) / 2.0 * Math.Abs(normal.X)
                + (measured.Height + 6.0) / 2.0 * Math.Abs(normal.Y)
                + 4.0;
            Point at = a2 + (b2 - a2) / 2.0 + normal * (side * reach);
            Chip(dc, label, at, ground, pixelsPerDip, within);
        }

        /// <summary>Draws the callout of an angle: an arc through the points given, a tick at each end and the label in a chip at its middle.</summary>
        internal static void ArcCallout(
            DrawingContext dc,
            IReadOnlyList<Point> arc,
            string label,
            StylePreviewGround ground,
            double pixelsPerDip,
            Rect within
        )
        {
            if (arc.Count < 2)
            {
                return;
            }

            var pen = new Pen(Solid(Lit(ground)), 1.0);
            pen.Freeze();

            for (int i = 1; i < arc.Count; i++)
            {
                dc.DrawLine(pen, arc[i - 1], arc[i]);
            }

            Point middle = arc[arc.Count / 2];
            Point end = arc[arc.Count - 1];
            dc.DrawEllipse(Solid(Lit(ground)), null, end, 2.0, 2.0);
            Chip(dc, label, new Point(middle.X + 40.0, middle.Y - 14.0), ground, pixelsPerDip, within);
        }

        /// <summary>Draws the ghost of a part left out: a dashed line in the lit colour from <paramref name="a"/> to <paramref name="b"/>.</summary>
        internal static void Ghost(DrawingContext dc, Point a, Point b, StylePreviewGround ground)
        {
            var pen = new Pen(Solid(Lit(ground)), 1.5) { DashStyle = new DashStyle(new double[] { 4.0, 3.0 }, 0.0) };
            pen.Freeze();
            dc.DrawLine(pen, a, b);
        }

        /// <summary>Draws the ghost of a box left out, as the frame or fill round words: a dashed rectangle in the lit colour.</summary>
        internal static void GhostBox(DrawingContext dc, Rect box, StylePreviewGround ground)
        {
            var pen = new Pen(Solid(Lit(ground)), 1.5) { DashStyle = new DashStyle(new double[] { 4.0, 3.0 }, 0.0) };
            pen.Freeze();
            dc.DrawRectangle(null, pen, box);
        }

        /// <summary>Draws a ring round the region a magnifier shows, in the lit colour.</summary>
        internal static void Region(DrawingContext dc, Point centre, double radius, StylePreviewGround ground)
        {
            var pen = new Pen(Solid(Lit(ground)), 1.0);
            pen.Freeze();
            dc.DrawEllipse(null, pen, centre, radius, radius);
        }

        /// <summary>
        /// Draws the magnifier's inset: the picture in a frame of the ground's colour with a 1 pixel border in the lit colour, rounded by 4,
        /// and the chip of its magnification in the corner.
        /// </summary>
        internal static void Inset(
            DrawingContext dc,
            Rect frame,
            ImageSource picture,
            string magnification,
            StylePreviewGround ground,
            double pixelsPerDip
        ) => Inset(dc, frame, c => c.DrawImage(picture, frame), magnification, ground, pixelsPerDip);

        /// <summary>
        /// Draws the magnifier's inset with what it shows drawn by a method, for a preview that is vectors: the frame is clipped to the
        /// inset and the method draws in the preview's own coordinates.
        /// </summary>
        internal static void Inset(
            DrawingContext dc,
            Rect frame,
            Action<DrawingContext> draw,
            string magnification,
            StylePreviewGround ground,
            double pixelsPerDip
        )
        {
            var pen = new Pen(Solid(Lit(ground)), 1.0);
            pen.Freeze();
            Brush floor = Solid(ground == StylePreviewGround.White ? Colors.White : Colors.Black);
            dc.DrawRoundedRectangle(floor, null, frame, 4.0, 4.0);
            dc.PushClip(new RectangleGeometry(frame, 4.0, 4.0));
            draw(dc);
            dc.Pop();
            dc.DrawRoundedRectangle(null, pen, frame, 4.0, 4.0);
            var chip = new FormattedText(
                magnification,
                CultureInfo.InvariantCulture,
                FlowDirection.LeftToRight,
                Mono,
                10.0,
                Solid(InkOnLit(ground)),
                pixelsPerDip
            );
            var box = new Rect(frame.X + 4.0, frame.Y + 4.0, chip.Width + 8.0, chip.Height + 2.0);
            dc.DrawRoundedRectangle(Solid(Lit(ground)), null, box, 2.0, 2.0);
            dc.DrawText(chip, new Point(box.X + 4.0, box.Y + 1.0));
        }

        /// <summary>Draws the tooltip of the part under the pointer: its words on a dark box, below and to the right of the pointer, kept inside the preview.</summary>
        internal static void Tooltip(
            DrawingContext dc,
            string text,
            Point pointer,
            Rect within,
            double pixelsPerDip,
            string click = "edit"
        )
        {
            var formatted = new FormattedText(
                text.Replace(" · click to " + click, string.Empty)
                    + (
                        text.EndsWith("click to " + click, StringComparison.Ordinal)
                            ? "\nClick to " + click
                            : string.Empty
                    ),
                CultureInfo.InvariantCulture,
                FlowDirection.LeftToRight,
                Mono,
                11.0,
                Solid(Color.FromRgb(0xE6, 0xEA, 0xF0)),
                pixelsPerDip
            )
            {
                MaxTextWidth = Math.Max(120.0, Math.Min(300.0, within.Width - 24.0)),
            };
            double w = formatted.Width + 20.0;
            double h = formatted.Height + 14.0;
            double x = Math.Max(within.Left + 2.0, Math.Min(within.Right - w - 2.0, pointer.X + 14.0));
            double y =
                pointer.Y + 18.0 + h > within.Bottom - 2.0
                    ? Math.Max(within.Top + 2.0, pointer.Y - 12.0 - h)
                    : pointer.Y + 18.0;
            var border = new Pen(Solid(Color.FromRgb(0x3A, 0x43, 0x50)), 1.0);
            border.Freeze();
            dc.DrawRoundedRectangle(Solid(Color.FromRgb(0x14, 0x1A, 0x24)), border, new Rect(x, y, w, h), 4.0, 4.0);
            dc.DrawText(formatted, new Point(x + 10.0, y + 7.0));
        }
    }
}
