using System.Collections.Generic;
using System.Linq;
using GeometryHelper.Dxf;
using GeometryHelper.Geometry;
using Xunit;

namespace OpenCAD2D.UnitTest
{
    /// <summary>
    /// The colour, true colour, align, font, height and width codes of an MTEXT read as AutoCAD 2021 reads them (accoreconsole,
    /// the MTEXT exploded: the colour of each piece and where it stands), by every reader of the codes: the content parser,
    /// the leading codes the display reads, FIND and SPELL.
    /// </summary>
    public sealed class MTextCodeReadersTests
    {
        private static string Runs(string contents)
        {
            CadMTextContent content = CadMTextContent.Parse(contents, 100.0);
            IEnumerable<string> runs = content
                .Paragraphs.SelectMany(p => p.Runs)
                .Select(r =>
                    r.Text
                    + "|c"
                    + (r.Look.ColorIndex?.ToString() ?? "-")
                    + "|t"
                    + (r.Look.TrueColor.HasValue ? r.Look.TrueColor.Value.ToString("X6") : "-")
                    + "|a"
                    + (r.Look.Align?.ToString() ?? "-")
                );
            return string.Join(" ", runs);
        }

        [Theory]
        [InlineData(@"\C1A", "A|c1|t-|a-")]
        [InlineData(@"\C1;A", "A|c1|t-|a-")]
        [InlineData(@"\C12A", "A|c12|t-|a-")]
        [InlineData(@"\C01A", "A|c1|t-|a-")]
        [InlineData(@"\C1 A", " A|c1|t-|a-")]
        [InlineData(@"\C1.5A", ".5A|c1|t-|a-")]
        [InlineData(@"\C1xA", "xA|c1|t-|a-")]
        [InlineData(@"\C0A", "A|c0|t-|a-")]
        [InlineData(@"\CA", "A|c0|t-|a-")]
        [InlineData(@"\C;A", "A|c0|t-|a-")]
        [InlineData(@"\C 1A", " 1A|c0|t-|a-")]
        [InlineData(@"\C+1A", "+1A|c0|t-|a-")]
        [InlineData(@"\C-0A", "A|c0|t-|a-")]
        [InlineData(@"\C-A", "A|c0|t-|a-")]
        [InlineData(@"\C256A", "A|c256|t-|a-")]
        [InlineData(@"\C257A", "A|c256|t-|a-")]
        [InlineData(@"\C300A", "A|c256|t-|a-")]
        [InlineData(@"\C-1A", "A|c256|t-|a-")]
        [InlineData(@"\C99999999999A", "A|c256|t-|a-")]
        [InlineData(@"\C65536A", "A|c0|t-|a-")]
        [InlineData(@"\C65537A", "A|c1|t-|a-")]
        [InlineData(@"\C65792A", "A|c256|t-|a-")]
        [InlineData(@"\C32768A", "A|c256|t-|a-")]
        [InlineData(@"\C2147483648A", "A|c0|t-|a-")]
        [InlineData(@"\C2147483649A", "A|c1|t-|a-")]
        [InlineData(@"\C-4294967295A", "A|c1|t-|a-")]
        [InlineData(@"\C1;\C257A", "A|c256|t-|a-")]
        [InlineData(@"\C1;\C 1A", " 1A|c0|t-|a-")]
        public void AColourIndexCode_TakesDigitsOnly_AndOneOutOfRangeIsByLayer(string contents, string runs)
        {
            // AutoCAD 2021: \C1A shows A in colour 1; no digits is colour 0 (ByBlock); above 255 and below 0 is ByLayer.
            Assert.Equal(runs, Runs(contents));
        }

        [Theory]
        [InlineData(@"\c255A", "A|c-|tFF0000|a-")]
        [InlineData(@"\c255;A", "A|c-|tFF0000|a-")]
        [InlineData(@"\c255 A", " A|c-|tFF0000|a-")]
        [InlineData(@"\c1.5A", ".5A|c-|t010000|a-")]
        [InlineData(@"\c255xA", "xA|c-|tFF0000|a-")]
        [InlineData(@"\c16777215A", "A|c-|tFFFFFF|a-")]
        [InlineData(@"\c0A", "A|c-|t000000|a-")]
        [InlineData(@"\cA", "A|c-|t000000|a-")]
        [InlineData(@"\c;A", "A|c-|t000000|a-")]
        [InlineData(@"\c 255A", " 255A|c-|t000000|a-")]
        [InlineData(@"\c+255A", "+255A|c-|t000000|a-")]
        [InlineData(@"\c16777216A", "A|c256|t-|a-")]
        [InlineData(@"\c-1A", "A|c256|t-|a-")]
        [InlineData(@"\c-A", "A|c-|t000000|a-")]
        [InlineData(@"\c2147483648A", "A|c256|t-|a-")]
        [InlineData(@"\c4294967297A", "A|c-|t010000|a-")]
        [InlineData(@"\c65537A", "A|c-|t010001|a-")]
        [InlineData(@"\c-4294967295A", "A|c-|t010000|a-")]
        [InlineData(@"\c255;\c-1A", "A|c256|t-|a-")]
        [InlineData(@"\c255;\C2A", "A|c2|t-|a-")]
        [InlineData(@"\C1;\c255A", "A|c-|tFF0000|a-")]
        public void ATrueColourCode_TakesDigitsOnly_AndOneOutOfRangeIsByLayer(string contents, string runs)
        {
            // AutoCAD 2021: \c255A shows A in true colour 255,0,0 (the number is blue, green, red); none is black.
            Assert.Equal(runs, Runs(contents));
        }

        [Theory]
        [InlineData(@"X\A1\H0.5x;Y", "X|c-|t-|a- Y|c-|t-|a1")]
        [InlineData(@"X\A2\H0.5x;Y", "X|c-|t-|a- Y|c-|t-|a2")]
        [InlineData(@"X\A1;\H0.5x;Y", "X|c-|t-|a- Y|c-|t-|a1")]
        [InlineData(@"X\A0\H0.5x;Y", "X|c-|t-|a- Y|c-|t-|a-")]
        [InlineData(@"X\A3\H0.5x;Y", "X|c-|t-|a- Y|c-|t-|a-")]
        [InlineData(@"X\A-1\H0.5x;Y", "X|c-|t-|a- Y|c-|t-|a-")]
        [InlineData(@"X\A-\H0.5x;Y", "X|c-|t-|a- Y|c-|t-|a-")]
        [InlineData(@"X\A\H0.5x;Y", "X|c-|t-|a- Y|c-|t-|a-")]
        [InlineData(@"X\A01\H0.5x;Y", "X|c-|t-|a- Y|c-|t-|a1")]
        [InlineData(@"X\A12\H0.5x;Y", "X|c-|t-|a- Y|c-|t-|a-")]
        [InlineData(@"X\A65537\H0.5x;Y", "X|c-|t-|a- Y|c-|t-|a1")]
        [InlineData(@"X\A4294967298\H0.5x;Y", "X|c-|t-|a- Y|c-|t-|a2")]
        [InlineData(@"X\A1.5\H0.5x;Y", "X|c-|t-|a- .5|c-|t-|a1 Y|c-|t-|a1")]
        [InlineData(@"X\A1B", "X|c-|t-|a- B|c-|t-|a1")]
        [InlineData(@"X\A+1\H0.5x;Y", "X+1|c-|t-|a- Y|c-|t-|a-")]
        [InlineData(@"X\A 1;Y", "X 1;Y|c-|t-|a-")]
        [InlineData(@"\A1;X\A3;Y", "X|c-|t-|a1 Y|c-|t-|a-")]
        [InlineData(@"\A1;X\A;Y", "X|c-|t-|a1 Y|c-|t-|a-")]
        [InlineData(@"\A2;X\A0;Y", "X|c-|t-|a2 Y|c-|t-|a-")]
        public void AnAlignCode_TakesDigitsOnly_AndOneNotOneOrTwoIsTheBaseline(string contents, string runs)
        {
            // AutoCAD 2021, the Y of X\A1\H0.5x;Y 25 above the baseline (middle), \A2 50 above (top), any other value on it.
            Assert.Equal(runs, Runs(contents));
        }

        [Theory]
        [InlineData(@"\H2xWord", "Word|c-|t-|a-")]
        [InlineData(@"\W2Word", "Word|c-|t-|a-")]
        [InlineData(@"\Q15Word", "Word|c-|t-|a-")]
        [InlineData(@"\T2Word", "Word|c-|t-|a-")]
        [InlineData(@"\C1Word", "Word|c1|t-|a-")]
        [InlineData(@"\c255Word", "Word|c-|tFF0000|a-")]
        [InlineData(@"\A1Word", "Word|c-|t-|a1")]
        [InlineData(@"\H2 1/2Word", "Word|c-|t-|a-")]
        public void TheRunsAfterANumberCodeWithNoSemicolon_AreItsWords(string contents, string runs)
        {
            Assert.Equal(runs, Runs(contents));
        }

        private static string Put(DxfDatabase database, string contents)
        {
            var mtext = new DxfMText
            {
                Contents = contents,
                Location = new GeoPoint3(0, 0, 0),
                TextHeight = 2.5,
            };
            database.ModelSpace.AppendEntity(mtext);
            return mtext.Handle;
        }

        [Theory]
        [InlineData(@"\H2xWord", 1)]
        [InlineData(@"\W2Word", 1)]
        [InlineData(@"\Q15Word", 1)]
        [InlineData(@"\T2Word", 1)]
        [InlineData(@"\C1Word", 1)]
        [InlineData(@"\c255Word", 1)]
        [InlineData(@"\A1Word", 1)]
        [InlineData(@"\H2 1/2Word", 1)]
        [InlineData(@"\H2 Word", 1)]
        [InlineData(@"\H 2 Word", 1)]
        [InlineData(@"\C1 Word", 1)]
        [InlineData(@"\fArial;Word", 1)]
        [InlineData(@"\fArialWord", 0)]
        [InlineData(@"{\fArial}A;Word", 1)]
        [InlineData(@"\fArial\PA;Word", 1)]
        [InlineData(@"Say Word \fArial", 1)]
        public void Find_ReadsTheCodesOfAnMTextAsAutoCADDoes(string contents, int count)
        {
            var db = new DxfDatabase("AC1027");
            Put(db, contents);
            CadDocument document = CadDocument.From(db, "find.dxf");

            IReadOnlyList<CadFindResult> found = CadFind.Find(
                document,
                "Word",
                new CadFindOptions { MatchCase = true, WholeWords = true },
                CadFindScope.EntireDrawing
            );

            Assert.Equal(count, found.Sum(r => r.Count));
        }

        [Theory]
        [InlineData(@"\H2xWord", @"\H2xNew")]
        [InlineData(@"\W2Word", @"\W2New")]
        [InlineData(@"\C1Word", @"\C1New")]
        [InlineData(@"\c255Word", @"\c255New")]
        [InlineData(@"\A1Word", @"\A1New")]
        [InlineData(@"\H2 1/2Word", @"\H2 1/2New")]
        [InlineData(@"\H2 Word", @"\H2 New")]
        [InlineData(@"{\fArial;Word}\H2x Word", @"{\fArial;New}\H2x New")]
        public void Replace_KeepsTheCodesOfAnMTextWhoseNumbersHaveNoSemicolon(string contents, string replaced)
        {
            var db = new DxfDatabase("AC1027");
            string handle = Put(db, contents);
            CadDocument document = CadDocument.From(db, "find.dxf");
            var options = new CadFindOptions { MatchCase = true, WholeWords = true };
            IReadOnlyList<CadFindResult> found = CadFind.Find(document, "Word", options, CadFindScope.EntireDrawing);

            Assert.True(CadFind.ReplaceAll(document, found, "Word", "New", options).Succeeded);

            Assert.Equal(replaced, ((DxfMText)document.FindEntity(handle)).Contents);
        }

        [Theory]
        [InlineData(@"\H2xWord", "Word@4")]
        [InlineData(@"\W2Word", "Word@3")]
        [InlineData(@"\Q15Word", "Word@4")]
        [InlineData(@"\T2Word", "Word@3")]
        [InlineData(@"\C1Word", "Word@3")]
        [InlineData(@"\c255Word", "Word@5")]
        [InlineData(@"\A1Word", "Word@3")]
        [InlineData(@"\H2 1/2Word", "Word@7")]
        [InlineData(@"\H2 Word", "2@2 Word@4")]
        [InlineData(@"\H 2 Word", "Word@5")]
        [InlineData(@"\fArialWord", "")]
        [InlineData(@"Say \fArial", "Say@0")]
        [InlineData(@"\fArial;Word", "Word@8")]
        [InlineData(@"\fArial\PA;Word", "Word@11")]
        [InlineData(@"{\fArial}A;Word", "Word@11")]
        public void Spelling_TakesTheWordsOfAnMTextAfterCodesWithNoSemicolon(string contents, string words)
        {
            Assert.Equal(
                words,
                string.Join(" ", CadSpelling.WordsOf(contents, true).Select(w => w.Text + "@" + w.Start))
            );
        }

        private static DisplayText2 Shown(string contents)
        {
            var db = new DxfDatabase("AC1018");
            Put(db, contents);
            CadDocument document = CadDocument.From(db);
            document.Fonts = new CadFonts(null, CadFonts.BuiltInName, null);
            return Assert.IsType<DisplayText2>(Assert.Single(document.GetDisplay(document.ModelSpace).Items));
        }

        [Theory]
        [InlineData(@"\C1\fArial;PLATE")]
        [InlineData(@"\A1\fArial;PLATE")]
        [InlineData(@"\H2x\fArial;PLATE")]
        [InlineData(@"\W2\fArial;PLATE")]
        [InlineData(@"\c255\fArial;PLATE")]
        public void TheFontOfAnMTextIsReadPastCodesWithNoSemicolon(string contents)
        {
            string arial = Shown(@"\fArial;PLATE").FontName;

            Assert.NotEqual(Shown("PLATE").FontName, arial);
            Assert.Equal(arial, Shown(contents).FontName);
        }

        [Theory]
        [InlineData(@"\W0.8PLATE", 0.8)]
        [InlineData(@"\W2\W0.4xPLATE", 0.8)]
        [InlineData(@"\C1\W0.5;PLATE", 0.5)]
        [InlineData(@"\H2x\W0.8PLATE", 0.8)]
        [InlineData(@"\W0.05PLATE", 0.1)]
        [InlineData(@"\W;PLATE", 0.1)]
        public void TheWidthOfAnMTextIsReadPastCodesWithNoSemicolon(string contents, double factor)
        {
            Assert.Equal(factor, Shown(contents).WidthFactor, 9);
        }
    }
}
