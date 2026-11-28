using System;
using System.Collections.Generic;
using GeometryHelper.Dxf;
using OpenCAD2D.Wpf;
using SkiaSharp;
using Xunit;

namespace OpenCAD2D.UnitTest
{
    /// <summary>
    /// A text too small to read, zoomed out: by default (<see cref="CanvasOptions.SmallTextAsBars"/>) a bar of its colour the
    /// size of its box, as AutoCAD 2021 draws the lines of a sheet at its extents; with the option off the old rule, a text
    /// under 3 pixels high left out and one of 3 to 5 pixels in its letters. Drawn with no window, pixels read back.
    /// </summary>
    public class SmallTextBarTests
    {
        /// <summary>A canvas 200 by 150 at one pixel to the unit, the drawing's (0, 0) at its lower left corner.</summary>
        private static CadView PixelView()
        {
            var view = new CadView(200, 150);
            view.ZoomTo(new DisplayBox(0, 0, 200, 150), 0.0);
            return view;
        }

        private static CadDocument TextAt(double height, string value = "WORDS", string handle = "T1") =>
            CadDocument.From(
                TestDrawings.Drawing(null, null, TestDrawings.Text(20, 40, height, value, handle: handle))
            );

        private static SKBitmap Draw(CadDocument document, bool bars, IReadOnlyCollection<string> selected = null)
        {
            var bitmap = new SKBitmap(200, 150, SKColorType.Rgba8888, SKAlphaType.Premul);

            using (var canvas = new SKCanvas(bitmap))
            using (var renderer = new SkiaCanvasRenderer())
            {
                renderer.Render(
                    canvas,
                    document,
                    document.ModelSpace,
                    PixelView(),
                    new CanvasOptions { SmallTextAsBars = bars, Selected = selected }
                );
            }

            return bitmap;
        }

        private static bool Lit(SKColor c) => c.Red > 20 || c.Green > 20 || c.Blue > 20;

        /// <summary>The pixels lit and the box of them, in pixels of the canvas.</summary>
        private static int Lit(SKBitmap bitmap, out int left, out int top, out int right, out int bottom)
        {
            int count = 0;
            left = top = int.MaxValue;
            right = bottom = int.MinValue;

            for (int y = 0; y < bitmap.Height; y++)
            {
                for (int x = 0; x < bitmap.Width; x++)
                {
                    if (Lit(bitmap.GetPixel(x, y)))
                    {
                        count++;
                        left = Math.Min(left, x);
                        right = Math.Max(right, x);
                        top = Math.Min(top, y);
                        bottom = Math.Max(bottom, y);
                    }
                }
            }

            return count;
        }

        [Fact]
        public void ATextUnderThreePixelsHigh_IsASolidBarOfItsColourByDefault_WhereItUsedToBeLeftOut()
        {
            using (SKBitmap bitmap = Draw(TextAt(2.0), bars: true))
            {
                int count = Lit(bitmap, out int left, out int top, out int right, out int bottom);

                Assert.True(count >= 8, "a bar, not a speck: " + count);

                // Solid: every pixel of the box of the lit ones is lit, at the white of layer 0 on black, none of it dimmed.
                Assert.Equal((right - left + 1) * (bottom - top + 1), count);
                Assert.Equal(new SKColor(255, 255, 255), bitmap.GetPixel((left + right) / 2, (top + bottom) / 2));

                // Near the text's place, (20, 40) of the drawing being (20, 110) on the canvas.
                Assert.InRange(left, 18, 22);
                Assert.InRange(bottom, 108, 114);
            }

            using (SKBitmap off = Draw(TextAt(2.0), bars: false))
            {
                Assert.Equal(0, Lit(off, out _, out _, out _, out _));
            }
        }

        [Fact]
        public void ATextFourPixelsHigh_IsABarByDefault_AndItsLettersWithTheOptionOff()
        {
            using (SKBitmap bars = Draw(TextAt(4.0), bars: true))
            using (SKBitmap letters = Draw(TextAt(4.0), bars: false))
            {
                int barCount = Lit(bars, out int left, out int top, out int right, out int bottom);
                int letterCount = Lit(
                    letters,
                    out int letterLeft,
                    out int letterTop,
                    out int letterRight,
                    out int letterBottom
                );

                Assert.Equal((right - left + 1) * (bottom - top + 1), barCount);
                Assert.True(letterCount > 0);

                // The letters leave gaps in their box; the bar does not.
                Assert.True(letterCount < (letterRight - letterLeft + 1) * (letterBottom - letterTop + 1));
            }
        }

        [Fact]
        public void ATextFivePixelsHighOrMore_IsDrawnInItsLetters_AsBefore()
        {
            using (SKBitmap on = Draw(TextAt(8.0), bars: true))
            using (SKBitmap off = Draw(TextAt(8.0), bars: false))
            {
                Assert.Equal(off.Bytes, on.Bytes);
                int count = Lit(on, out int left, out int top, out int right, out int bottom);
                Assert.True(count < (right - left + 1) * (bottom - top + 1), "letters leave gaps");
            }
        }

        [Fact]
        public void ATextASixthOfAPixelHigh_IsStillAPixelHighBarOfItsColour()
        {
            using (SKBitmap bitmap = Draw(TextAt(0.15), bars: true))
            {
                int count = Lit(bitmap, out int left, out int top, out int right, out int bottom);

                Assert.True(count > 0);
                Assert.InRange(bottom - top, 0, 1);

                // At least one pixel of it at full strength where it lies a pixel wide.
                int brightest = 0;

                for (int y = top; y <= bottom; y++)
                {
                    for (int x = left; x <= right; x++)
                    {
                        brightest = Math.Max(brightest, bitmap.GetPixel(x, y).Red);
                    }
                }

                Assert.True(brightest >= 120, "brightest " + brightest);
            }
        }

        [Fact]
        public void ASelectedSmallText_IsABarInTheSelectionColour()
        {
            CadDocument document = TextAt(2.0);

            using (SKBitmap bitmap = Draw(document, bars: true, selected: new[] { "T1" }))
            {
                int count = Lit(bitmap, out int left, out int top, out int right, out int bottom);
                SKColor middle = bitmap.GetPixel((left + right) / 2, (top + bottom) / 2);

                Assert.True(count >= 8);
                Assert.Equal(0x3D, middle.Red);
                Assert.Equal(0x9B, middle.Green);
                Assert.Equal(0xFF, middle.Blue);
            }
        }

        [Fact]
        public void ATextOfSpacesAlone_HasNoBar()
        {
            using (SKBitmap bitmap = Draw(TextAt(2.0, "   "), bars: true))
            {
                Assert.Equal(0, Lit(bitmap, out _, out _, out _, out _));
            }
        }

        [Fact]
        public void ASelectedSmallText_IsABarInTheSelectionColourOverTheSceneToo_AsTheCanvasHighlightsIt()
        {
            // The canvas keeps the scene and draws the highlight over it, on threads where many items are selected.
            CadDocument document = TextAt(2.0);
            var options = new CanvasOptions();

            using (var scene = new SKBitmap(200, 150, SKColorType.Rgba8888, SKAlphaType.Premul))
            using (var canvas = new SKCanvas(scene))
            using (var renderer = new SkiaCanvasRenderer())
            {
                renderer.RenderScene(canvas, document, document.ModelSpace, PixelView(), options);
                int count = Lit(scene, out int left, out int top, out int right, out int bottom);
                renderer.RenderHighlight(
                    canvas,
                    document,
                    document.ModelSpace,
                    PixelView(),
                    options,
                    new[] { "T1" },
                    false
                );
                SKColor middle = scene.GetPixel((left + right) / 2, (top + bottom) / 2);

                Assert.True(count >= 8);
                Assert.Equal(new SKColor(0x3D, 0x9B, 0xFF), middle);
            }
        }
    }
}
