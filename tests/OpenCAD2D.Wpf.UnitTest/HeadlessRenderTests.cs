using System;
using System.Diagnostics;
using System.IO;
using System.Text.RegularExpressions;
using SkiaSharp;
using Xunit;

namespace OpenCAD2D.Wpf.UnitTest
{
    /// <summary>
    /// The picture of <c>CadRender</c> is the picture of the app: each of ten drawings built in code is written to a file,
    /// drawn by the app's <c>--screenshot ... --canvas</c> (the canvas's own bitmap, under the overlays the window adds) and
    /// by <c>CadRender.Draw</c> at the canvas's size, and every red, green and blue byte is the same. The app runs on .NET 8
    /// and these tests on .NET Framework 4.8, so the same bytes also show that the two runtimes and the two builds of
    /// SkiaSharp draw one picture.
    /// </summary>
    public class HeadlessRenderTests : IDisposable
    {
        private readonly string _folder = Path.Combine(
            Path.GetTempPath(),
            "gk",
            "headless-render-" + Guid.NewGuid().ToString("N")
        );

        public HeadlessRenderTests() => Directory.CreateDirectory(_folder);

        public void Dispose()
        {
            try
            {
                Directory.Delete(_folder, true);
            }
            catch (IOException) { }
            catch (UnauthorizedAccessException) { }
        }

        [Theory]
        [InlineData(1)]
        [InlineData(2)]
        [InlineData(3)]
        [InlineData(4)]
        [InlineData(5)]
        [InlineData(6)]
        [InlineData(7)]
        [InlineData(8)]
        [InlineData(9)]
        [InlineData(10)]
        public void TheFacadesPicture_IsTheAppsCanvas_ByteForByte(int number)
        {
            string file = RenderDrawings.Build(number, _folder);
            Shot shot = Screenshot(file, RenderDrawings.Space(number));

            CadDocument document = CadDocument.Open(file);
            CadRenderImage drawn = CadRender.Draw(
                document,
                new CadRenderOptions
                {
                    Layout = RenderDrawings.Space(number),
                    Width = shot.Canvas.Width,
                    Height = shot.Canvas.Height,
                }
            );

            Assert.Equal(shot.Canvas.Width, drawn.Width);
            Assert.Equal(shot.Canvas.Height, drawn.Height);
            Assert.True(Different(shot.Canvas.Rgb, drawn.Rgb, drawn.Width, out string where) == 0, where);

            if (number == 9)
            {
                // The picture of the image is in both, not its frame alone: the squares are red 220, 30, 30 and green 30, 200, 60.
                bool red = false;
                bool green = false;

                for (int i = 0; i < shot.Canvas.Rgb.Length; i += 3)
                {
                    byte r = shot.Canvas.Rgb[i];
                    byte g = shot.Canvas.Rgb[i + 1];
                    red |= r > 150 && g < 80;
                    green |= g > 150 && r < 80;
                }

                Assert.True(red && green, "The image is not in the canvas of the app.");
            }
        }

        [Fact]
        public void TheFacadesPicture_OfDrawing1OnWhite_IsTheRasterWritersWithAWhiteCanvas_AndColour7IsBlack()
        {
            string file = RenderDrawings.Build(1, _folder);
            CadDocument document = CadDocument.Open(file);
            CadRender.Prepare(document);
            CadRenderImage white = CadRender.Draw(
                document,
                new CadRenderOptions
                {
                    Width = 1000,
                    Height = 679,
                    Background = 0xFFFFFF,
                }
            );
            var view = new CadView(1000, 679);
            view.ZoomTo(document.GetDisplay(document.ModelSpace).Bounds, 0.05);
            var rgb = new byte[1000 * 679 * 3];

            using (
                SKBitmap expected = CadRasterWriter.Draw(
                    document,
                    document.ModelSpace,
                    view,
                    new CanvasOptions { Background = 0xFFFFFF, ShowLineWeights = true },
                    null
                )
            )
            {
                for (int i = 0; i < 1000 * 679; i++)
                {
                    SKColor c = expected.GetPixel(i % 1000, i / 1000);
                    rgb[3 * i] = c.Red;
                    rgb[3 * i + 1] = c.Green;
                    rgb[3 * i + 2] = c.Blue;
                }
            }

            Assert.True(Different(rgb, white.Rgb, 1000, out string where) == 0, where);
            Assert.Equal(new byte[] { 255, 255, 255 }, new[] { white.Rgb[0], white.Rgb[1], white.Rgb[2] });
            bool black = false;

            for (int i = 0; i < white.Rgb.Length; i += 3)
            {
                black |= white.Rgb[i] < 64 && white.Rgb[i + 1] < 64 && white.Rgb[i + 2] < 64;
            }

            Assert.True(black, "No pixel of colour 7 was drawn black on the white ground.");
        }

        [Fact]
        public void TheWindowsPictureOfTheCanvasRectangle_IsTheCanvasBitmap_ButForTheViewportLabelInItsCorner()
        {
            string file = RenderDrawings.Build(1, _folder);
            Shot shot = Screenshot(file, null);
            SKBitmap window = SKBitmap.Decode(Path.Combine(_folder, "window.png"));
            int differing = 0;
            int right = -1;
            int bottom = -1;

            for (int y = 0; y < shot.Canvas.Height; y++)
            {
                for (int x = 0; x < shot.Canvas.Width; x++)
                {
                    SKColor w = window.GetPixel(shot.X + x, shot.Y + y);
                    int at = 3 * (y * shot.Canvas.Width + x);

                    if (
                        w.Red != shot.Canvas.Rgb[at]
                        || w.Green != shot.Canvas.Rgb[at + 1]
                        || w.Blue != shot.Canvas.Rgb[at + 2]
                    )
                    {
                        differing++;
                        right = Math.Max(right, x);
                        bottom = Math.Max(bottom, y);
                    }
                }
            }

            window.Dispose();

            // With the UCS icon off, the one thing the window draws over the canvas is the label of the viewport at its top left
            // corner ([-][Model][2D Wireframe], 153 by 19 pixels): composing the window moves no other pixel.
            Assert.True(
                right < 160 && bottom < 20,
                differing + " pixels differ, within 0,0 to " + right + "," + bottom + "."
            );
        }

        /// <summary>What the app wrote: the canvas's pixels and where the canvas lies in the window.</summary>
        private sealed class Shot
        {
            public CadRenderImage Canvas;
            public int X;
            public int Y;
        }

        private Shot Screenshot(string file, string space)
        {
            string window = Path.Combine(_folder, "window.png");
            string canvas = Path.Combine(_folder, "canvas.png");
            var args = new System.Collections.Generic.List<string>
            {
                "--screenshot",
                window,
                file,
                "--size",
                "1600",
                "960",
                "--run",
                "UCSICON;OFF;GRIDMODE;0;ZOOM;E",
                "--canvas",
                canvas,
            };

            if (space != null)
            {
                args.AddRange(new[] { "--space", space });
            }

            (int code, string output) = AppHostTests.RunApp(args.ToArray());
            Assert.True(code == 0, output);
            Match corner = Regex.Match(output, @"at (-?[\d.]+),(-?[\d.]+) of the window");
            Assert.True(corner.Success, output);

            using (SKBitmap bitmap = SKBitmap.Decode(canvas))
            {
                var rgb = new byte[bitmap.Width * bitmap.Height * 3];
                int at = 0;

                for (int y = 0; y < bitmap.Height; y++)
                {
                    for (int x = 0; x < bitmap.Width; x++)
                    {
                        SKColor c = bitmap.GetPixel(x, y);
                        rgb[at++] = c.Red;
                        rgb[at++] = c.Green;
                        rgb[at++] = c.Blue;
                    }
                }

                return new Shot
                {
                    Canvas = new CadRenderImage(bitmap.Width, bitmap.Height, rgb),
                    X = (int)
                        Math.Round(
                            double.Parse(corner.Groups[1].Value, System.Globalization.CultureInfo.InvariantCulture)
                        ),
                    Y = (int)
                        Math.Round(
                            double.Parse(corner.Groups[2].Value, System.Globalization.CultureInfo.InvariantCulture)
                        ),
                };
            }
        }

        /// <summary>The number of bytes that differ, and where the first does.</summary>
        private static int Different(byte[] a, byte[] b, int width, out string where)
        {
            where = "the same";

            if (a.Length != b.Length)
            {
                where = "the lengths are " + a.Length + " and " + b.Length;
                return Math.Max(a.Length, b.Length);
            }

            int count = 0;

            for (int i = 0; i < a.Length; i++)
            {
                if (a[i] != b[i])
                {
                    if (count == 0)
                    {
                        int pixel = i / 3;
                        where =
                            "the first of the bytes is at pixel "
                            + pixel % width
                            + ","
                            + pixel / width
                            + ", "
                            + a[i]
                            + " and "
                            + b[i];
                    }

                    count++;
                }
            }

            where = count + " bytes differ; " + where;
            return count;
        }
    }
}
