using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using System.Xml.Linq;
using GeometryHelper.Dxf;
using GeometryHelper.Geometry;
using SkiaSharp;
using Xunit;

namespace OpenCAD2D.Wpf.UnitTest
{
    /// <summary>
    /// The facade that draws a drawing with no window: the size of a picture by its rules, the refusals, what <c>Prepare</c>
    /// does once, the PNG, and the SVG and the PDF read back. They run here, where nothing runs in parallel, because
    /// <c>Prepare</c> sets the canvas's measure of TrueType letters for the whole process.
    /// </summary>
    public class CadRenderTests : IDisposable
    {
        private readonly string _folder = Path.Combine(
            Path.GetTempPath(),
            "gk",
            "cad-render-" + Guid.NewGuid().ToString("N")
        );

        public CadRenderTests() => Directory.CreateDirectory(_folder);

        public void Dispose()
        {
            try
            {
                Directory.Delete(_folder, true);
            }
            catch (IOException) { }
            catch (UnauthorizedAccessException) { }
        }

        /// <summary>A drawing whose extents are a rectangle 400 by 300, from (0, 0): a line from corner to corner.</summary>
        private static CadDocument Rectangle()
        {
            var database = new DxfDatabase("AC1027");
            database.ModelSpace.AppendEntity(new DxfLine(new GeoPoint3(0, 0, 0), new GeoPoint3(400, 300, 0)));
            return CadDocument.From(database, "rectangle.dxf");
        }

        /// <summary>
        /// A drawing of A3 on its side with a layout of two words in Arial and a line, and in model space a text.
        /// </summary>
        private static CadDocument WithSheet()
        {
            var database = new DxfDatabase("AC1027");
            database.TextStyleTable["Standard"].FileName = "arial.ttf";
            database.ModelSpace.AppendEntity(new DxfLine(new GeoPoint3(0, 0, 0), new GeoPoint3(400, 300, 0)));
            database.ModelSpace.AppendEntity(
                new DxfDBText
                {
                    Position = new GeoPoint3(10, 10, 0),
                    Height = 20,
                    TextString = "Modelwords",
                }
            );
            DxfLayout layout = database.Layouts.Single(l => l.LayoutName == RenderDrawings.LayoutName);
            layout.Initialize();
            layout.BlockTableRecord.AppendEntity(
                new DxfDBText
                {
                    Position = new GeoPoint3(20, 20, 0),
                    Height = 10,
                    TextString = "Sheetwords",
                }
            );
            layout.BlockTableRecord.AppendEntity(new DxfLine(new GeoPoint3(0, 0, 0), new GeoPoint3(100, 100, 0)));
            var document = CadDocument.From(database, "sheet.dxf");

            foreach (CadSpace space in document.Spaces)
            {
                CadPlotSettings settings = CadPlotSettings.Read(document, space);
                settings.Paper = CadPaper.Default;
                settings.Landscape = true;
                settings.FitToPaper = true;
                Assert.True(settings.ApplyTo(document, space).Succeeded);
            }

            return CadDocument.From(DxfDatabase.Parse(document.Database.ToText()), "sheet.dxf");
        }

        [Theory]
        [InlineData(800, 0, 800, 600)]
        [InlineData(0, 600, 800, 600)]
        [InlineData(1000, 1000, 1000, 1000)]
        [InlineData(0, 0, 1600, 1200)]
        [InlineData(1, 0, 1, 1)]
        public void ThePicturesSize_OfModelSpace_FollowsTheRules(
            int width,
            int height,
            int expectedWidth,
            int expectedHeight
        )
        {
            CadRenderImage picture = CadRender.Draw(
                Rectangle(),
                new CadRenderOptions { Width = width, Height = height }
            );

            Assert.Equal(expectedWidth, picture.Width);
            Assert.Equal(expectedHeight, picture.Height);
            Assert.Equal(expectedWidth * expectedHeight * 3, picture.Rgb.Length);
        }

        [Fact]
        public void ATallDrawing_IsFittedToTheLongerSide_OfModelSpace()
        {
            var database = new DxfDatabase("AC1027");
            database.ModelSpace.AppendEntity(new DxfLine(new GeoPoint3(0, 0, 0), new GeoPoint3(300, 400, 0)));

            CadRenderImage picture = CadRender.Draw(CadDocument.From(database, "tall.dxf"), null);

            Assert.Equal(1200, picture.Width);
            Assert.Equal(1600, picture.Height);
        }

        [Theory]
        [InlineData(96.0, 1587, 1123)]
        [InlineData(300.0, 4961, 3508)]
        [InlineData(25.4, 420, 297)]
        public void ThePicturesSize_OfALayout_IsItsSheetInMillimetresTimesDpiOver25AndAQuarter(
            double dpi,
            int expectedWidth,
            int expectedHeight
        )
        {
            CadRenderImage picture = CadRender.Draw(
                WithSheet(),
                new CadRenderOptions { Layout = RenderDrawings.LayoutName, Dpi = dpi }
            );

            Assert.Equal(expectedWidth, picture.Width);
            Assert.Equal(expectedHeight, picture.Height);
        }

        [Fact]
        public void TheDpi_HasNoEffectInModelSpace_NorWhereASizeIsGiven()
        {
            CadDocument document = Rectangle();

            CadRenderImage model = CadRender.Draw(document, new CadRenderOptions { Dpi = 300.0 });
            CadRenderImage sized = CadRender.Draw(
                WithSheet(),
                new CadRenderOptions
                {
                    Layout = RenderDrawings.LayoutName,
                    Dpi = 300.0,
                    Width = 840,
                }
            );

            Assert.Equal(1600, model.Width);
            Assert.Equal(840, sized.Width);
            Assert.Equal(594, sized.Height);
        }

        [Theory]
        [InlineData(16385, 10)]
        [InlineData(10, 16385)]
        [InlineData(12000, 9000)]
        public void APictureOverTheCaps_IsRefused_NamingItsSizeAndTheCap(int width, int height)
        {
            var ex = Assert.Throws<ArgumentException>(() =>
                CadRender.Draw(Rectangle(), new CadRenderOptions { Width = width, Height = height })
            );

            Assert.Contains(width + " by " + height, ex.Message);
            Assert.Contains("16384", ex.Message);
        }

        [Fact]
        public void ASizeOverTheCap_DerivedFromTheOther_IsRefusedToo()
        {
            // A part 1 by 2 at 16 384 wide is 32 768 high, over the side.
            var ex = Assert.Throws<ArgumentException>(() =>
                CadRender.Draw(
                    Rectangle(),
                    new CadRenderOptions
                    {
                        Width = 16384,
                        Dpi = 96,
                        Height = 0,
                        Box = new DisplayBox(0, 0, 1, 2),
                    }
                )
            );

            Assert.Contains("32768", ex.Message);
        }

        [Fact]
        public void TheOptionsThatCannotBeDrawn_AreRefused()
        {
            CadDocument document = Rectangle();

            Assert.Throws<ArgumentNullException>(() => CadRender.Draw(null, null));
            Assert.Throws<ArgumentNullException>(() => CadRender.WritePng(null, document, null));
            Assert.Throws<ArgumentNullException>(() => CadRender.WriteSvg(null, document, null));
            Assert.Throws<ArgumentNullException>(() => CadRender.WritePdf(null, document, null));
            Assert.Throws<ArgumentNullException>(() => CadRender.Prepare(null));
            var missing = Assert.Throws<ArgumentException>(() =>
                CadRender.Draw(document, new CadRenderOptions { Layout = "Elsewhere" })
            );
            Assert.Contains("Elsewhere", missing.Message);
            Assert.Throws<ArgumentException>(() => CadRender.Draw(document, new CadRenderOptions { Width = -1 }));
            Assert.Throws<ArgumentException>(() => CadRender.Draw(document, new CadRenderOptions { Height = -5 }));
            Assert.Throws<ArgumentException>(() => CadRender.Draw(document, new CadRenderOptions { Dpi = 0.0 }));
            Assert.Throws<ArgumentException>(() => CadRender.Draw(document, new CadRenderOptions { Dpi = -96.0 }));
            Assert.Throws<ArgumentException>(() => CadRender.Draw(document, new CadRenderOptions { Dpi = double.NaN }));
            Assert.Throws<ArgumentException>(() => CadRender.Draw(document, new CadRenderOptions { Background = -1 }));
            Assert.Throws<ArgumentException>(() =>
                CadRender.Draw(document, new CadRenderOptions { Background = 0x1000000 })
            );
            Assert.Throws<ArgumentException>(() =>
                CadRender.WritePdf(Path.Combine(_folder, "x.pdf"), document, "Elsewhere")
            );
        }

        [Fact]
        public void ALayoutIsFoundByItsName_InLettersOfEitherCase_AndModelByItsName()
        {
            CadDocument document = WithSheet();

            CadRenderImage lower = CadRender.Draw(document, new CadRenderOptions { Layout = "layout1", Width = 200 });
            CadRenderImage model = CadRender.Draw(document, new CadRenderOptions { Layout = "Model", Width = 200 });

            Assert.Equal(200, lower.Width);
            Assert.NotEqual(lower.Height, model.Height);
        }

        [Fact]
        public void Prepare_IsOneRegen_AndAgainIsNone()
        {
            CadDocument document = Rectangle();
            int regens = 0;
            document.Changed += (s, e) => regens += e.Kind == CadChangeKind.Regen ? 1 : 0;

            CadRender.Prepare(document);
            Assert.Equal(1, regens);

            CadRender.Prepare(document);
            CadRender.Draw(document, null);
            Assert.Equal(1, regens);
        }

        [Fact]
        public void Prepare_SetsTheCanvasFontsAndMeasure_OfADrawingOpenedByTheLibrary()
        {
            Func<string, bool, bool, string, double> was = CadTrueTypeFonts.MeasureWidth;

            try
            {
                CadTrueTypeFonts.MeasureWidth = null;
                CadDocument document = Rectangle();
                CadFonts before = document.Fonts;

                CadRender.Prepare(document);

                Assert.False(document.Fonts.IsSetAs(before));
                Assert.NotNull(CadTrueTypeFonts.MeasureWidth);
                Assert.Equal(0.25, document.DefaultLineWeight, 6);
            }
            finally
            {
                CadTrueTypeFonts.MeasureWidth = was;
            }
        }

        [Fact]
        public void Prepare_AfterABoundsRead_LaysAnMTextOutAgain_WithTheCanvasMeasure()
        {
            Func<string, bool, bool, string, double> was = CadTrueTypeFonts.MeasureWidth;

            try
            {
                CadTrueTypeFonts.MeasureWidth = null;
                var database = new DxfDatabase("AC1027");
                database.ModelSpace.AppendEntity(
                    new DxfMText
                    {
                        Location = new GeoPoint3(0, 0, 0),
                        TextHeight = 10,
                        Contents =
                            "{\\fNoSuchFamilyOfLetters|b0|i0;iiiiiiiiii}{\\fNoSuchFamilyOfLetters|b1|i0;WWWWWWWWWW}",
                    }
                );
                CadDocument document = CadDocument.From(database, "words.dxf");
                double before = document.GetDisplay(document.ModelSpace).Bounds.Width;

                CadRender.Prepare(document);
                double after = document.GetDisplay(document.ModelSpace).Bounds.Width;

                Assert.True(Math.Abs(after - before) > 1.0, "The width stayed " + before + " after Prepare.");
            }
            finally
            {
                CadTrueTypeFonts.MeasureWidth = was;
            }
        }

        [Fact]
        public void APng_IsOfColourType2_AndIsWhatDrawGives()
        {
            CadDocument document = WithSheet();
            string path = Path.Combine(_folder, "a.png");
            var options = new CadRenderOptions { Width = 640, Height = 480 };

            CadRender.WritePng(path, document, options);
            CadRenderImage drawn = CadRender.Draw(document, options);

            byte[] bytes = File.ReadAllBytes(path);
            Assert.Equal(new byte[] { 0x89, (byte)'P', (byte)'N', (byte)'G' }, bytes.Take(4).ToArray());
            Assert.Equal(640, (bytes[16] << 24) | (bytes[17] << 16) | (bytes[18] << 8) | bytes[19]);
            Assert.Equal(480, (bytes[20] << 24) | (bytes[21] << 16) | (bytes[22] << 8) | bytes[23]);
            Assert.Equal(8, bytes[24]);
            Assert.Equal(2, bytes[25]);

            using (SKBitmap decoded = SKBitmap.Decode(path))
            {
                Assert.Equal(640, decoded.Width);
                Assert.Equal(480, decoded.Height);
                int differing = 0;

                for (int y = 0; y < 480; y++)
                {
                    for (int x = 0; x < 640; x++)
                    {
                        SKColor c = decoded.GetPixel(x, y);
                        int at = 3 * (y * 640 + x);
                        differing +=
                            c.Red != drawn.Rgb[at] || c.Green != drawn.Rgb[at + 1] || c.Blue != drawn.Rgb[at + 2]
                                ? 1
                                : 0;
                    }
                }

                Assert.Equal(0, differing);
            }
        }

        [Fact]
        public void APictureOfAModelOnTheCanvasBlack_HoldsTheLineInItsColour()
        {
            var database = new DxfDatabase("AC1027");
            database.ModelSpace.AppendEntity(
                new DxfLine(new GeoPoint3(0, 0, 0), new GeoPoint3(400, 0, 0)) { ColorIndex = 1 }
            );
            database.ModelSpace.AppendEntity(
                new DxfLine(new GeoPoint3(0, 300, 0), new GeoPoint3(400, 300, 0)) { ColorIndex = 1 }
            );

            CadRenderImage picture = CadRender.Draw(
                CadDocument.From(database, "red.dxf"),
                new CadRenderOptions { Width = 400, Height = 300 }
            );

            Assert.Equal(new byte[] { 0, 0, 0 }, picture.Rgb.Take(3).ToArray());
            bool red = false;

            // A line on the half of a pixel is shared by two rows: red at about half, never another colour.
            for (int i = 0; i < picture.Rgb.Length; i += 3)
            {
                red |= picture.Rgb[i] >= 100 && picture.Rgb[i + 1] == 0 && picture.Rgb[i + 2] == 0;
            }

            Assert.True(red, "No pixel is the line's red.");
        }

        // ---- the SVG read back

        [Fact]
        public void AnSvg_IsAnSvgRoot_OfThePicturesSize_WithTheLineAtItsPixels()
        {
            var database = new DxfDatabase("AC1027");
            database.ModelSpace.AppendEntity(
                new DxfLine(new GeoPoint3(0, 0, 0), new GeoPoint3(400, 300, 0)) { ColorIndex = 1 }
            );
            CadDocument document = CadDocument.From(database, "line.dxf");
            string path = Path.Combine(_folder, "a.svg");

            CadRender.WriteSvg(path, document, new CadRenderOptions { Width = 800, Height = 600 });

            XElement root = XDocument.Load(path).Root;
            Assert.Equal("svg", root.Name.LocalName);
            Assert.Equal(800.0, Number(root.Attribute("width")), 6);
            Assert.Equal(600.0, Number(root.Attribute("height")), 6);
            XAttribute box = root.Attribute("viewBox");
            Assert.NotNull(box);
            Assert.Equal(
                new[] { 0.0, 0.0, 800.0, 600.0 },
                box.Value.Split(' ', ',')
                    .Where(v => v.Length > 0)
                    .Select(v => double.Parse(v, System.Globalization.CultureInfo.InvariantCulture))
                    .ToArray()
            );
            // The drawing's (0, 0) to (400, 300), fitted with a 5 per cent margin to 800 by 600, is (40, 570) to (760, 30).
            string d = string.Join(
                " ",
                root.Descendants().Where(e => e.Name.LocalName == "path").Select(e => (string)e.Attribute("d"))
            );
            double[] numbers = System
                .Text.RegularExpressions.Regex.Matches(d, @"-?\d+(\.\d+)?")
                .Cast<System.Text.RegularExpressions.Match>()
                .Select(m => double.Parse(m.Value, System.Globalization.CultureInfo.InvariantCulture))
                .ToArray();
            Assert.True(HasPoint(numbers, 40, 570), "No point (40, 570) in " + d);
            Assert.True(HasPoint(numbers, 760, 30), "No point (760, 30) in " + d);
        }

        [Fact]
        public void AnSvg_OfALayoutWithAClippedViewport_HoldsAClipPath()
        {
            string file = RenderDrawings.Build(10, _folder);
            string path = Path.Combine(_folder, "layout.svg");

            CadRender.WriteSvg(
                path,
                CadDocument.Open(file),
                new CadRenderOptions { Layout = RenderDrawings.LayoutName, Width = 1200 }
            );

            XDocument svg = XDocument.Load(path);
            Assert.Contains(svg.Descendants(), e => e.Name.LocalName == "clipPath");
        }

        [Fact]
        public void AnSvg_HoldsTheWordsOfATrueTypeText_AsText()
        {
            string path = Path.Combine(_folder, "words.svg");

            CadRender.WriteSvg(path, WithSheet(), new CadRenderOptions { Width = 800 });

            string words = string.Concat(
                XDocument.Load(path).Descendants().Where(e => e.Name.LocalName == "text").Select(e => e.Value)
            );
            Assert.Contains("Modelwords", words);
        }

        [Fact]
        public void AnSvg_HoldsAnImageAsPngData()
        {
            string file = RenderDrawings.Build(9, _folder);
            string path = Path.Combine(_folder, "image.svg");

            CadRender.WriteSvg(path, CadDocument.Open(file), new CadRenderOptions { Width = 800 });

            Assert.Contains("data:image/png", File.ReadAllText(path));
        }

        // ---- the PDF read back

        [Fact]
        public void APdf_IsOnePage_OfTheSheetInPoints_WithTheTrueTypeWords()
        {
            string path = Path.Combine(_folder, "sheet.pdf");

            CadPlotPage page = CadRender.WritePdf(path, WithSheet(), RenderDrawings.LayoutName);

            Assert.Equal(420.0, page.SheetWidth, 6);
            Assert.Equal(297.0, page.SheetHeight, 6);
            PdfDocument pdf = PdfDocument.Open(File.ReadAllBytes(path));
            Assert.Single(pdf.Pages);
            Assert.Equal(1190.5512, pdf.Pages[0].Width, 3);
            Assert.Equal(841.8898, pdf.Pages[0].Height, 3);
            string words = WordsOf(File.ReadAllBytes(path));
            Assert.Contains("Sheetwords", words);
            Assert.DoesNotContain("Modelwords", words);
        }

        [Fact]
        public void APdf_OfModelSpace_HoldsItsWords_AndTheLayoutNameIsNotNeeded()
        {
            string path = Path.Combine(_folder, "model.pdf");

            CadDocument document = WithSheet();
            CadRender.WritePdf(path, document, "Model");

            PdfDocument pdf = PdfDocument.Open(File.ReadAllBytes(path));
            Assert.Single(pdf.Pages);
            Assert.Contains("Modelwords", WordsOf(File.ReadAllBytes(path)));
        }

        [Fact]
        public void APdf_OfALayoutWithNothingOnIt_IsRefused_AsExportPdfRefusesIt()
        {
            var database = new DxfDatabase("AC1027");
            CadDocument document = CadDocument.From(database, "empty.dxf");

            Assert.Throws<InvalidOperationException>(() =>
                CadRender.WritePdf(Path.Combine(_folder, "empty.pdf"), document, null)
            );
        }

        /// <summary>
        /// The words a PDF of CadPdfWriter shows, read back as a reader's search reads them: the glyph codes of its <c>Tj</c>
        /// strings through the <c>ToUnicode</c> maps of its fonts, in the order the page draws them.
        /// </summary>
        private static string WordsOf(byte[] pdf)
        {
            var codes = new Dictionary<string, string>();
            var content = new List<string>();
            string text = System.Text.Encoding.GetEncoding(28591).GetString(pdf);

            foreach (
                Match stream in Regex.Matches(
                    text,
                    @"stream?
(.*?)endstream",
                    RegexOptions.Singleline
                )
            )
            {
                byte[] bytes = System.Text.Encoding.GetEncoding(28591).GetBytes(stream.Groups[1].Value);
                string inflated;

                try
                {
                    using (var from = new MemoryStream(bytes, 2, bytes.Length - 2))
                    using (
                        var deflate = new System.IO.Compression.DeflateStream(
                            from,
                            System.IO.Compression.CompressionMode.Decompress
                        )
                    )
                    using (var to = new MemoryStream())
                    {
                        deflate.CopyTo(to);
                        inflated = System.Text.Encoding.GetEncoding(28591).GetString(to.ToArray());
                    }
                }
                catch (InvalidDataException)
                {
                    continue;
                }

                if (inflated.Contains("begincmap"))
                {
                    foreach (
                        Match section in Regex.Matches(inflated, @"beginbfchar(.*?)endbfchar", RegexOptions.Singleline)
                    )
                    {
                        foreach (
                            Match pair in Regex.Matches(section.Groups[1].Value, @"<([0-9A-Fa-f]+)>\s*<([0-9A-Fa-f]+)>")
                        )
                        {
                            codes[pair.Groups[1].Value.ToUpperInvariant()] = (
                                (char)Convert.ToInt32(pair.Groups[2].Value, 16)
                            ).ToString();
                        }
                    }

                    foreach (
                        Match section in Regex.Matches(
                            inflated,
                            @"beginbfrange(.*?)endbfrange",
                            RegexOptions.Singleline
                        )
                    )
                    {
                        foreach (
                            Match range in Regex.Matches(
                                section.Groups[1].Value,
                                @"<([0-9A-Fa-f]+)>\s*<([0-9A-Fa-f]+)>\s*<([0-9A-Fa-f]+)>"
                            )
                        )
                        {
                            int low = Convert.ToInt32(range.Groups[1].Value, 16);
                            int high = Convert.ToInt32(range.Groups[2].Value, 16);
                            int first = Convert.ToInt32(range.Groups[3].Value, 16);

                            for (int code = low; code <= high; code++)
                            {
                                codes[code.ToString("X4")] = ((char)(first + code - low)).ToString();
                            }
                        }
                    }
                }
                else if (inflated.Contains(" Tj"))
                {
                    content.Add(inflated);
                }
            }

            var words = new System.Text.StringBuilder();

            foreach (string page in content)
            {
                foreach (Match shown in Regex.Matches(page, @"<([0-9A-Fa-f]+)>\s*Tj"))
                {
                    string hex = shown.Groups[1].Value.ToUpperInvariant();

                    for (int i = 0; i + 4 <= hex.Length; i += 4)
                    {
                        words.Append(codes.TryGetValue(hex.Substring(i, 4), out string letter) ? letter : "?");
                    }
                }
            }

            return words.ToString();
        }

        private static double Number(XAttribute attribute) =>
            double.Parse(
                attribute.Value.Replace("px", string.Empty),
                System.Globalization.CultureInfo.InvariantCulture
            );

        /// <summary>Whether a list of numbers holds an X and the Y after it, each within 0.01.</summary>
        private static bool HasPoint(IReadOnlyList<double> numbers, double x, double y)
        {
            for (int i = 0; i + 1 < numbers.Count; i++)
            {
                if (Math.Abs(numbers[i] - x) <= 0.01 && Math.Abs(numbers[i + 1] - y) <= 0.01)
                {
                    return true;
                }
            }

            return false;
        }
    }
}
