using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;
using GeometryHelper.Dxf;
using GeometryHelper.Geometry;
using SkiaSharp;
using Xunit;

namespace OpenCAD2D.Wpf.UnitTest
{
    /// <summary>
    /// The PDF carries only the glyphs its text uses (P4): a plot of 100 lines and one text is a few kilobytes where it was
    /// 543 KB for Arial whole, the font keeps the tag of a subset in its name, and the text is still found and copied, in
    /// Latin, Vietnamese and Korean letters.
    /// </summary>
    public class PdfGlyphSubsetTests
    {
        private static string FontFile(string name) =>
            Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.Fonts), name);

        /// <summary>
        /// A plan of 100 lines across a sheet 4 000 by 2 500 and one text, ROOM 1 in Arial 250 high, as the plan of the size
        /// test of the plot guide.
        /// </summary>
        private static CadDocument HundredLinesAndOneText(string words = "ROOM 1", Action<DxfDatabase> more = null)
        {
            var database = new DxfDatabase("AC1027");
            database.TextStyleTable["Standard"].Font = new DxfFontDescriptor("Arial", false, false, 0, 34);

            for (int i = 0; i < 100; i++)
            {
                database.ModelSpace.AppendEntity(
                    new DxfLine(new GeoPoint3(40 * i, 0, 0), new GeoPoint3(4000 - 40 * i, 2500, 0))
                );
            }

            database.ModelSpace.AppendEntity(
                new DxfDBText
                {
                    TextString = words,
                    Position = new GeoPoint3(words.Length > 6 ? 100 : 1500, 1200, 0),
                    Height = words.Length > 6 ? 100 : 250,
                }
            );
            more?.Invoke(database);
            return CadDocument.From(database, "hundred.dxf");
        }

        private static CadPlotSettings OnA3() => new CadPlotSettings { Area = CadPlotArea.Extents, FitToPaper = true };

        private static byte[] Written(CadDocument document)
        {
            using (var memory = new MemoryStream())
            {
                CadPdfWriter.Write(memory, document, document.ModelSpace, OnA3(), DisplayBox.Empty);
                return memory.ToArray();
            }
        }

        private static string Latin(byte[] bytes) => Encoding.GetEncoding(28591).GetString(bytes);

        [Fact]
        public void HundredLinesAndOneText_IsUnder60Kilobytes_AndItsTextIsStillFound()
        {
            byte[] pdf = Written(HundredLinesAndOneText());

            Assert.True(pdf.Length < 60 * 1024, "The PDF is " + pdf.Length + " bytes.");
            Assert.Contains("ROOM 1", Pdf.Text(pdf));
            Pdf.AssertCrossReferencesPointAtTheirObjects(pdf);
        }

        [Fact]
        public void TheFontIsNamedWithASubsetTag_AlikeInTheThreeObjectsThatNameIt()
        {
            string text = Latin(Written(HundredLinesAndOneText()));

            var name = new Regex(@"/(?:BaseFont|FontName) /([A-Z]{6})\+ArialMT");
            List<string> tags = name.Matches(text).Cast<Match>().Select(m => m.Groups[1].Value).ToList();

            Assert.Equal(3, tags.Count);
            Assert.Single(tags.Distinct());
            Assert.Matches(@"/FontFile2 \d+ 0 R", text);
        }

        [Fact]
        public void VietnameseLetters_AreFoundInThePdf_AsTheyWereDrawn()
        {
            const string words = "Phòng họp số 1, Đường Nguyễn Huệ, ồ ỗ ợ Ỷ";
            byte[] pdf = Written(HundredLinesAndOneText(words));

            Assert.True(pdf.Length < 100 * 1024, "The PDF is " + pdf.Length + " bytes.");
            Assert.Contains(words, Pdf.Text(pdf));
            Pdf.AssertCrossReferencesPointAtTheirObjects(pdf);
        }

        [Fact]
        public void KoreanAndHanLetters_OfAFontThatHasThem_AreFoundInThePdf_AndTheFontIsFarSmallerThanItWas()
        {
            if (!File.Exists(FontFile("malgun.ttf")))
            {
                return;
            }

            const string words = "한국어 漢字 ROOM";
            CadDocument document = HundredLinesAndOneText("ROOM 1", database =>
            {
                database.TextStyleTable.Add(new DxfTextStyleTableRecord { Name = "KO", FileName = "malgun.ttf" });
                database.ModelSpace.AppendEntity(
                    new DxfDBText
                    {
                        TextString = words,
                        Position = new GeoPoint3(100, 600, 0),
                        Height = 100,
                        TextStyleName = "KO",
                    }
                );
            });

            byte[] pdf = Written(document);

            Assert.Contains(words, Pdf.Text(pdf));
            Assert.True(pdf.Length < 150 * 1024, "The PDF is " + pdf.Length + " bytes.");
            Assert.Matches(@"/BaseFont /[A-Z]{6}\+Malgun", Latin(pdf));
        }

        [Fact]
        public void AFontOfACollection_IsCutFromItsOwnFace_AndItsChineseTextIsFound()
        {
            if (!File.Exists(FontFile("msyh.ttc")))
            {
                return;
            }

            const string words = "房间 漢字 ROOM";
            CadDocument document = HundredLinesAndOneText("ROOM 1", database =>
            {
                database.TextStyleTable.Add(new DxfTextStyleTableRecord { Name = "ZH", FileName = "msyh.ttc" });
                database.ModelSpace.AppendEntity(
                    new DxfDBText
                    {
                        TextString = words,
                        Position = new GeoPoint3(100, 600, 0),
                        Height = 100,
                        TextStyleName = "ZH",
                    }
                );
            });

            byte[] pdf = Written(document);

            Assert.Contains(words, Pdf.Text(pdf));
            Assert.True(pdf.Length < 150 * 1024, "The PDF is " + pdf.Length + " bytes.");
            Assert.Matches(@"/BaseFont /[A-Z]{6}\+MicrosoftYaHei", Latin(pdf));
        }

        [Fact]
        public void ACollection_IsLeftWhole_WhereNoFaceHasTheNameAsked()
        {
            if (!File.Exists(FontFile("msyh.ttc")))
            {
                return;
            }

            byte[] collection = File.ReadAllBytes(FontFile("msyh.ttc"));

            Assert.Null(CadPdfFontSubset.TrueType(collection, new ushort[] { 0, 3 }, "NoSuchFace"));
            Assert.Null(CadPdfFontSubset.TrueType(collection, new ushort[] { 0, 3 }));
            Assert.NotNull(CadPdfFontSubset.TrueType(collection, new ushort[] { 0, 3 }, "MicrosoftYaHei"));
        }

        [Fact]
        public void TwoFonts_AreEachSubsetUnderATagOfItsOwn_AndEachKeepsItsText()
        {
            CadDocument document = HundredLinesAndOneText("ROOM 1", database =>
            {
                database.TextStyleTable.Add(new DxfTextStyleTableRecord { Name = "SERIF", FileName = "times.ttf" });
                database.ModelSpace.AppendEntity(
                    new DxfDBText
                    {
                        TextString = "SERIF ĐỒ",
                        Position = new GeoPoint3(1500, 600, 0),
                        Height = 250,
                        TextStyleName = "SERIF",
                    }
                );
            });

            byte[] pdf = Written(document);
            string shown = Pdf.Text(pdf);

            Assert.Contains("ROOM 1", shown);
            Assert.Contains("SERIF ĐỒ", shown);
            Assert.True(pdf.Length < 100 * 1024, "The PDF is " + pdf.Length + " bytes.");
            List<string> tags = Regex
                .Matches(Latin(pdf), @"/FontName /([A-Z]{6})\+")
                .Cast<Match>()
                .Select(m => m.Groups[1].Value)
                .ToList();
            Assert.Equal(2, tags.Count);
            Assert.Equal(2, tags.Distinct().Count());
        }

        [Fact]
        public void EighteenPages_ShareOneSubset_AndKeepTheirText()
        {
            CadDocument document = HundredLinesAndOneText();
            var pages = new List<CadPlotJob>();

            for (int i = 0; i < 18; i++)
            {
                pages.Add(new CadPlotJob(document.ModelSpace, OnA3(), DisplayBox.Empty, null));
            }

            string file = Path.Combine(Path.GetTempPath(), "p4-" + Guid.NewGuid().ToString("N") + ".pdf");

            try
            {
                CadPdfWriter.WritePages(file, document, pages);
                byte[] pdf = File.ReadAllBytes(file);

                Assert.True(pdf.Length < 300 * 1024, "The PDF is " + pdf.Length + " bytes.");
                Assert.Equal(18, Regex.Matches(Pdf.Text(pdf), "ROOM 1").Count);
                Assert.Single(Regex.Matches(Latin(pdf), @"/FontFile2 \d+ 0 R").Cast<Match>());
                Pdf.AssertCrossReferencesPointAtTheirObjects(pdf);
            }
            finally
            {
                File.Delete(file);
            }
        }

        [Fact]
        public void ASubsetFont_DrawsTheGlyphsItKeeps_ExactlyAsTheWholeFontDoes_AndNoOthers()
        {
            byte[] whole = File.ReadAllBytes(FontFile("arial.ttf"));

            using (var original = SKTypeface.FromData(SKData.CreateCopy(whole)))
            using (var font = new SKFont(original, 100))
            {
                ushort[] kept = font.GetGlyphs("RỒM ộ1Đ ").Distinct().ToArray();
                ushort other = font.GetGlyphs("Q")[0];
                Assert.DoesNotContain(other, kept);

                byte[] part = CadPdfFontSubset.TrueType(whole, kept);

                Assert.NotNull(part);
                Assert.True(part.Length < 40 * 1024, "The subset is " + part.Length + " bytes.");

                using (var subset = SKTypeface.FromData(SKData.CreateCopy(part)))
                using (var small = new SKFont(subset, 100))
                {
                    foreach (ushort glyph in kept)
                    {
                        using (SKPath was = font.GetGlyphPath(glyph))
                        using (SKPath now = small.GetGlyphPath(glyph))
                        {
                            Assert.Equal(was.Bounds, now.Bounds);
                            Assert.Equal(was.PointCount, now.PointCount);
                        }
                    }

                    using (SKPath none = small.GetGlyphPath(other))
                    {
                        Assert.True(none.IsEmpty);
                    }
                }
            }
        }

        [Fact]
        public void AFontThatForbidsSubsetting_OrIsNotTrueType_IsLeftWhole()
        {
            byte[] whole = File.ReadAllBytes(FontFile("arial.ttf"));
            ushort[] glyphs = { 0, 3, 53 };

            // The OS/2 table's fsType, bit 8: no subsetting.
            byte[] forbidden = (byte[])whole.Clone();
            int os2 = TableOffset(forbidden, "OS/2");
            forbidden[os2 + 8] |= 0x01;
            Assert.Null(CadPdfFontSubset.TrueType(forbidden, glyphs));

            // A font of CFF outlines, an OpenType 'OTTO'.
            byte[] cff = (byte[])whole.Clone();
            Encoding.ASCII.GetBytes("OTTO").CopyTo(cff, 0);
            Assert.Null(CadPdfFontSubset.TrueType(cff, glyphs));

            Assert.Null(CadPdfFontSubset.TrueType(new byte[] { 1, 2, 3 }, glyphs));
            Assert.NotNull(CadPdfFontSubset.TrueType(whole, glyphs));
        }

        [Fact]
        public void APdfWithNoFontOfItsOwn_IsGivenBackAsItWas()
        {
            byte[] pdf = Encoding.GetEncoding(28591).GetBytes("%PDF-1.4\nnot a plot\n");

            Assert.Same(pdf, CadPdfFontSubset.Apply(pdf));
        }

        private static int TableOffset(byte[] font, string tag)
        {
            int count = (font[4] << 8) | font[5];

            for (int i = 0; i < count; i++)
            {
                int record = 12 + 16 * i;

                if (Encoding.ASCII.GetString(font, record, 4) == tag)
                {
                    return (font[record + 8] << 24)
                        | (font[record + 9] << 16)
                        | (font[record + 10] << 8)
                        | font[record + 11];
                }
            }

            throw new InvalidOperationException("No table " + tag + ".");
        }
    }
}
