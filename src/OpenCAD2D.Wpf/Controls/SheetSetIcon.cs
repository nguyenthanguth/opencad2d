using System;
using System.Collections.Generic;
using System.Windows;
using System.Windows.Media;

namespace OpenCAD2D.Wpf
{
    /// <summary>
    /// A small coloured picture of the Sheet Set Manager, drawn as the design draws them from a name: the icons of the tree (the
    /// set, a subset, a sheet and its three states), of the toolbar (Publish to PDF, Publish to Plotter, Publish Using Page Setup
    /// Override, Publish), the lock of a set that is read-only and the arrows. Each is a few strokes in the palette's colours on a
    /// grid of 24 (the arrows 12), scaled to <see cref="Size"/>.
    /// </summary>
    public sealed class SheetSetIcon : FrameworkElement
    {
        /// <summary>Identifies the <see cref="Glyph"/> property.</summary>
        public static readonly DependencyProperty GlyphProperty = DependencyProperty.Register(
            nameof(Glyph),
            typeof(string),
            typeof(SheetSetIcon),
            new FrameworkPropertyMetadata(null, FrameworkPropertyMetadataOptions.AffectsRender)
        );

        /// <summary>Identifies the <see cref="Size"/> property.</summary>
        public static readonly DependencyProperty SizeProperty = DependencyProperty.Register(
            nameof(Size),
            typeof(double),
            typeof(SheetSetIcon),
            new FrameworkPropertyMetadata(
                16.0,
                FrameworkPropertyMetadataOptions.AffectsMeasure | FrameworkPropertyMetadataOptions.AffectsRender
            )
        );

        private static readonly Brush Ink = Frozen("#C9D1DB");
        private static readonly Brush Blue = Frozen("#3D9BFF");
        private static readonly Brush Yellow = Frozen("#E3B341");
        private static readonly Brush Grey = Frozen("#6B7480");
        private static readonly Brush Muted = Frozen("#9AA4B1");
        private static readonly Brush Red = Frozen("#F85149");
        private static readonly Brush Salmon = Frozen("#FF8A7A");
        private static readonly Brush Green = Frozen("#3FB950");
        private static readonly Brush Ground = Frozen("#16191E");
        private static readonly Brush YellowTint = Frozen("#33E3B341");

        private const string Page = "M5 3h10l4 4v14H5z";
        private const string BadgeCircle = "M16 12a5 5 0 1 0 0 10a5 5 0 1 0 0-10z";

        // Each glyph: the grid it is drawn on, the stroke thickness on that grid, and its layers.
        private static readonly Dictionary<string, Glyphs> Table = new Dictionary<string, Glyphs>(
            StringComparer.Ordinal
        )
        {
            ["set"] = new Glyphs(24, 1.7, L("M5 3h9l3 3v11H5z", Ink), L("M3 5v14h11", Ink), L("M8 9h6M8 12h6", Blue)),
            ["subset"] = new Glyphs(
                24,
                1.7,
                L("M2 6h6l2 2h8v3H6l-4 8z", Yellow, YellowTint),
                L("M6 11h16l-4 8H2z", Yellow)
            ),
            ["sheet"] = new Glyphs(24, 1.7, L(Page, Ink), L("M8 17h8M13 14h3", Blue)),
            ["sheet-open"] = new Glyphs(
                24,
                1.7,
                L(Page, Ink),
                L("M8 17h8M13 14h3", Blue),
                L("M18 2a3 3 0 1 0 0 6a3 3 0 1 0 0-6z", null, Green)
            ),
            ["sheet-excluded"] = new Glyphs(
                24,
                1.7,
                L(Page, Grey),
                L(BadgeCircle, Muted, Ground),
                L("M12.5 20.5l7-7", Muted)
            ),
            ["sheet-missing"] = new Glyphs(
                24,
                1.7,
                L(Page, Grey, null, true),
                L(BadgeCircle, Salmon, Ground),
                L("M16 14.5v3M16 19.5v.2", Salmon)
            ),
            ["pdf"] = new Glyphs(
                24,
                1.7,
                L(Page, Ink),
                L("M7.5 12h9a1 1 0 0 1 1 1v4a1 1 0 0 1-1 1h-9a1 1 0 0 1-1-1v-4a1 1 0 0 1 1-1z", null, Red)
            ),
            ["plotter"] = new Glyphs(24, 1.7, L("M6 9V3h12v6M6 17H3v-8h18v8h-3M6 14h12v7H6z", Ink)),
            ["override"] = new Glyphs(
                24,
                1.7,
                L(Page, Ink),
                L("M8 14h8M8 17h8", Yellow),
                L("M12 6v5M9.5 8.5L12 11l2.5-2.5", Yellow)
            ),
            ["publish"] = new Glyphs(24, 1.7, L("M12 15V4M7.5 8.5L12 4l4.5 4.5", Blue), L("M4 14v6h16v-6", Ink)),
            ["lock"] = new Glyphs(
                24,
                1.7,
                L(
                    "M6.5 10h11a1.5 1.5 0 0 1 1.5 1.5v7a1.5 1.5 0 0 1-1.5 1.5h-11A1.5 1.5 0 0 1 5 18.5v-7A1.5 1.5 0 0 1 6.5 10z",
                    Yellow,
                    YellowTint
                ),
                L("M8 10V7a4 4 0 0 1 8 0v3", Yellow)
            ),
            ["chevron-down"] = new Glyphs(12, 1.5, L("M3 4.5l3 3 3-3", Muted)),
            ["chevron-right"] = new Glyphs(12, 1.5, L("M4.5 3l3 3-3 3", Muted)),
        };

        /// <summary>Gets or sets the name of the picture: set, subset, sheet, sheet-open, sheet-excluded, sheet-missing, pdf, plotter, override, publish, lock, chevron-down or chevron-right; null draws nothing.</summary>
        public string Glyph
        {
            get => (string)GetValue(GlyphProperty);
            set => SetValue(GlyphProperty, value);
        }

        /// <summary>Gets or sets the width and height of the picture in pixels; 16 by default.</summary>
        public double Size
        {
            get => (double)GetValue(SizeProperty);
            set => SetValue(SizeProperty, value);
        }

        /// <inheritdoc/>
        protected override Size MeasureOverride(Size availableSize) => new Size(Size, Size);

        /// <inheritdoc/>
        protected override void OnRender(DrawingContext drawingContext)
        {
            if (Glyph == null || !Table.TryGetValue(Glyph, out Glyphs glyphs))
            {
                return;
            }

            // A clear square under the strokes, so that the whole box answers the mouse (the arrow of a row is clicked).
            drawingContext.DrawRectangle(Brushes.Transparent, null, new Rect(0, 0, Size, Size));
            double fit = Size / glyphs.Grid;
            drawingContext.PushTransform(new ScaleTransform(fit, fit));

            foreach (Layer layer in glyphs.Layers)
            {
                Pen pen = null;

                if (layer.Stroke != null)
                {
                    pen = new Pen(layer.Stroke, glyphs.Thickness)
                    {
                        StartLineCap = PenLineCap.Round,
                        EndLineCap = PenLineCap.Round,
                        LineJoin = PenLineJoin.Round,
                    };

                    if (layer.Dashed)
                    {
                        pen.DashStyle = new DashStyle(new[] { 1.2, 1.2 }, 0);
                    }
                }

                drawingContext.DrawGeometry(layer.Fill, pen, layer.Geometry);
            }

            drawingContext.Pop();
        }

        private static Brush Frozen(string color)
        {
            var brush = new SolidColorBrush((Color)ColorConverter.ConvertFromString(color));
            brush.Freeze();
            return brush;
        }

        private static Layer L(string data, Brush stroke, Brush fill = null, bool dashed = false)
        {
            Geometry geometry = Geometry.Parse(data);
            geometry.Freeze();
            return new Layer(geometry, stroke, fill, dashed);
        }

        private sealed class Layer
        {
            internal Layer(Geometry geometry, Brush stroke, Brush fill, bool dashed)
            {
                Geometry = geometry;
                Stroke = stroke;
                Fill = fill;
                Dashed = dashed;
            }

            internal Geometry Geometry { get; }

            internal Brush Stroke { get; }

            internal Brush Fill { get; }

            internal bool Dashed { get; }
        }

        private sealed class Glyphs
        {
            internal Glyphs(double grid, double thickness, params Layer[] layers)
            {
                Grid = grid;
                Thickness = thickness;
                Layers = layers;
            }

            internal double Grid { get; }

            internal double Thickness { get; }

            internal Layer[] Layers { get; }
        }
    }
}
