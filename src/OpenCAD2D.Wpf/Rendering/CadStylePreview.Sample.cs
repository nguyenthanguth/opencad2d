using System;
using System.Collections.Generic;
using System.Runtime.CompilerServices;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using SkiaSharp;

namespace OpenCAD2D.Wpf
{
    public static partial class CadStylePreview
    {
        /// <summary>The share of its colour a part that is not lit keeps, as 1 less this: 0.65 faded is 35 % kept.</summary>
        private const double FadeOfTheRest = 0.65;

        /// <summary>The least width of a line of a preview, in device-independent pixels.</summary>
        private const double LeastLineDips = 1.5;

        /// <summary>The width a lit line is drawn at, in device-independent pixels.</summary>
        private const double LitLineDips = 2.5;

        /// <summary>The width of the halo round a hovered part, in device-independent pixels each side of the line.</summary>
        private const double HaloDips = 4.0;

        private static readonly ConditionalWeakTable<CadStyleSample, CadDocument> Documents =
            new ConditionalWeakTable<CadStyleSample, CadDocument>();

        /// <summary>The colour a lit part is drawn in on a ground: #3D9BFF on black, the darker #0A6BD6 on white.</summary>
        internal static int LitColour(StylePreviewGround ground) =>
            ground == StylePreviewGround.White ? 0x0A6BD6 : 0x3D9BFF;

        /// <summary>The colour of a ground as 0xRRGGBB: black as the canvas's, or white.</summary>
        internal static int GroundColour(StylePreviewGround ground) =>
            ground == StylePreviewGround.White ? 0xFFFFFF : 0x000000;

        /// <summary>
        /// The drawing of a sample, made once for the sample (its display list is kept with it, so that a redraw for a lit part or
        /// a zoom costs the drawing, not the display), its text in the fonts given the first time.
        /// </summary>
        /// <param name="sample">The sample.</param>
        /// <param name="fonts">The fonts of the drawing edited; null for the preview's own.</param>
        /// <returns>The document.</returns>
        internal static CadDocument DocumentOf(CadStyleSample sample, CadFonts fonts)
        {
            if (sample == null)
            {
                throw new ArgumentNullException(nameof(sample));
            }

            return Documents.GetValue(sample, s =>
            {
                CadDocument document = CadDocument.From(s.Database, "preview.dxf");

                if (fonts != null)
                {
                    document.Fonts = fonts;
                }

                return document;
            });
        }

        /// <summary>The box of what a sample draws, in drawing coordinates; empty for a sample of nothing.</summary>
        internal static DisplayBox BoundsOf(CadStyleSample sample, CadFonts fonts)
        {
            CadDocument document = DocumentOf(sample, fonts);
            return document.GetDisplay(document.ModelSpace).Bounds;
        }

        /// <summary>
        /// Draws a style sample at the device's pixels, as the preview shows it: the sample's drawing by the canvas's renderer on a
        /// ground, every line at least 1.5 device-independent pixels wide (the lineweights shown), the parts lit drawn in the lit
        /// colour at least 2.5 wide and the rest faded towards the ground; a part hovered with a halo of 4 round it at 30 %. The
        /// picture is as many pixels as asked and carries the display's own resolution, so that it is shown one pixel to a pixel.
        /// </summary>
        /// <param name="sample">The sample.</param>
        /// <param name="fonts">The fonts of the drawing edited; null for the preview's own.</param>
        /// <param name="view">What part of the sample is shown and at what zoom, in device pixels.</param>
        /// <param name="ground">The ground.</param>
        /// <param name="lit">The handles of the entities lit; none or null to draw every part as it is.</param>
        /// <param name="deviceWidth">The width of the picture, in device pixels.</param>
        /// <param name="deviceHeight">The height of the picture, in device pixels.</param>
        /// <param name="dpi">The display's scale: 1 at 96 dots an inch, 1.5 at 150 %.</param>
        /// <param name="halo">True to draw the halo round the lit parts, as for the part under the pointer.</param>
        /// <returns>The picture, frozen.</returns>
        internal static BitmapSource Render(
            CadStyleSample sample,
            CadFonts fonts,
            CadView view,
            StylePreviewGround ground,
            IReadOnlyCollection<string> lit,
            int deviceWidth,
            int deviceHeight,
            double dpi,
            bool halo = false
        )
        {
            if (view == null)
            {
                throw new ArgumentNullException(nameof(view));
            }

            CadDocument document = DocumentOf(sample, fonts);
            bool anyLit = lit != null && lit.Count > 0;
            int colour = LitColour(ground);
            var options = new CanvasOptions
            {
                Background = GroundColour(ground),
                ShowLineWeights = true,
                LineweightScale = 1.0,
                MinimumLineWidth = (float)(LeastLineDips * dpi),
                SelectedLineWidth = (float)(LitLineDips * dpi),
                SelectedFillAlpha = 255,
                SelectionColor = colour,
                Selected = anyLit ? lit : null,
                Fade = anyLit ? FadeOfTheRest : 0.0,
            };
            var info = new SKImageInfo(deviceWidth, deviceHeight, SKColorType.Bgra8888, SKAlphaType.Premul);
            double pixels = 96.0 * dpi;
            var bitmap = new WriteableBitmap(deviceWidth, deviceHeight, pixels, pixels, PixelFormats.Pbgra32, null);
            bitmap.Lock();

            try
            {
                using (SKSurface surface = SKSurface.Create(info, bitmap.BackBuffer, bitmap.BackBufferStride))
                using (var renderer = new SkiaCanvasRenderer())
                {
                    renderer.Render(surface.Canvas, document, document.ModelSpace, view, options);

                    if (halo && anyLit)
                    {
                        // The halo: the lit lines again, wide, at 30 %, then the lit parts once more over it at their own width.
                        var wide = new CanvasOptions
                        {
                            Background = options.Background,
                            ShowLineWeights = true,
                            MinimumLineWidth = options.MinimumLineWidth,
                            SelectedLineWidth = (float)((LitLineDips + 2.0 * HaloDips) * dpi),
                            SelectedFillAlpha = 255,
                            SelectionColor = colour,
                        };

                        using (var layer = new SKPaint { Color = new SKColor(0, 0, 0, 77) })
                        {
                            surface.Canvas.SaveLayer(layer);
                            renderer.RenderHighlight(
                                surface.Canvas,
                                document,
                                document.ModelSpace,
                                view,
                                wide,
                                lit,
                                false
                            );
                            surface.Canvas.Restore();
                        }

                        renderer.RenderHighlight(
                            surface.Canvas,
                            document,
                            document.ModelSpace,
                            view,
                            options,
                            lit,
                            false
                        );
                    }

                    surface.Canvas.Flush();
                }

                bitmap.AddDirtyRect(new System.Windows.Int32Rect(0, 0, deviceWidth, deviceHeight));
            }
            finally
            {
                bitmap.Unlock();
            }

            bitmap.Freeze();
            return bitmap;
        }

        /// <summary>
        /// Finds the part of a sample under a point, as the preview's hover does: the nearest entity that is a part within a reach,
        /// a line by its distance, a filled arrowhead or words by their box (nought inside it).
        /// </summary>
        /// <param name="sample">The sample.</param>
        /// <param name="document">The sample's drawing, a document of its database.</param>
        /// <param name="view">What part of the sample is shown and at what zoom.</param>
        /// <param name="x">The X of the point, in the pixels of the view.</param>
        /// <param name="y">The Y of the point, in the pixels of the view.</param>
        /// <param name="reach">The farthest a part may lie from the point, in the pixels of the view.</param>
        /// <returns>The part; <see cref="CadStylePart.None"/> where none is within the reach.</returns>
        public static CadStylePart PartAt(
            CadStyleSample sample,
            CadDocument document,
            CadView view,
            double x,
            double y,
            double reach
        )
        {
            if (sample == null)
            {
                throw new ArgumentNullException(nameof(sample));
            }

            if (document == null)
            {
                throw new ArgumentNullException(nameof(document));
            }

            if (view == null)
            {
                throw new ArgumentNullException(nameof(view));
            }

            view.ScreenToDrawing(x, y, out double dx, out double dy);
            double tolerance = reach / view.PixelsPerUnit;
            CadStylePart found = CadStylePart.None;
            double nearest = double.PositiveInfinity;

            foreach (DisplayItem2 item in document.GetDisplay(document.ModelSpace).Items)
            {
                CadStylePart part = sample.PartOf(item.Handle);

                if (part == CadStylePart.None || part == CadStylePart.DefinitionPoint)
                {
                    continue;
                }

                if (!item.Bounds.Inflate(tolerance).Contains(dx, dy))
                {
                    continue;
                }

                double distance = item.DistanceTo(dx, dy);

                if (distance > tolerance)
                {
                    continue;
                }

                // The nearest; of two as near (a filled arrowhead over the line it ends), the one drawn later, on top, as the list orders them.
                if (distance <= nearest + 1E-9)
                {
                    nearest = Math.Min(nearest, distance);
                    found = part;
                }
            }

            return found;
        }
    }
}
