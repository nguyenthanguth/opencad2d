using GeometryHelper.Dxf;
using GeometryHelper.Geometry;
using Xunit;

namespace OpenCAD2D.UnitTest
{
    /// <summary>
    /// How an MTEXT lays out a height, width, oblique or tracking code (\H \W \Q \T) that has no value, no semicolon or a
    /// value that is not a number, in AutoCAD 2021's numbers: the contents 100 high in opencad2d.shx, measured by
    /// accoreconsole as MTEXT ActualWidth and ActualHeight.
    /// </summary>
    public sealed class MTextFormatCodeTests
    {
        private static (double Width, double Height) Laid(string contents)
        {
            var db = new DxfDatabase("AC1032");
            db.TextStyleTable["Standard"].FileName = "opencad2d.shx";
            var mtext = new DxfMText
            {
                Contents = contents,
                TextHeight = 100.0,
                Location = new GeoPoint3(0, 0, 0),
            };
            db.ModelSpace.AppendEntity(mtext);
            CadDocument document = CadDocument.From(db);
            document.Fonts = new CadFonts(null, CadFonts.BuiltInName, null);
            CadMTextLayout layout = document.LayoutOf((DxfMText)document.FindEntity(mtext.Handle));
            return (layout.ActualWidth, layout.ActualHeight);
        }

        private static void AssertLaid(string contents, double width, double height)
        {
            (double w, double h) = Laid(contents);
            Assert.Equal(width, w, 0.001);
            Assert.Equal(height, h, 0.001);
        }

        [Fact]
        public void ABareHCode_AtTheEnd_IsTaken_AndLaysOutNothingAtTheTextsHeight()
        {
            // AutoCAD 2021: ActualWidth 0, ActualHeight 100. The app read it as the letter H and laid out 115 by 120.
            AssertLaid(@"\H", 0.0, 100.0);
        }

        [Theory]
        [InlineData(@"\H;")]
        [InlineData(@"\Hx;")]
        [InlineData(@"\Hx")]
        [InlineData(@"\H2")]
        [InlineData(@"\H2x")]
        [InlineData(@"\H2;")]
        [InlineData(@"\W")]
        [InlineData(@"\Wx")]
        [InlineData(@"\W2")]
        [InlineData(@"\W;")]
        [InlineData(@"\Q")]
        [InlineData(@"\Q15")]
        [InlineData(@"\T")]
        [InlineData(@"\T2")]
        [InlineData(@"{\H")]
        public void ACodeWithNothingAfterIt_LaysOutNothingAtTheTextsHeight(string contents)
        {
            AssertLaid(contents, 0.0, 100.0);
        }

        [Theory]
        [InlineData(@"\HA", 60.0, 100.0)]
        [InlineData(@"\H;A", 60.0, 100.0)]
        [InlineData(@"\Hx;A", 60.0, 100.0)]
        [InlineData(@"\H0A", 60.0, 100.0)]
        [InlineData(@"\H-2;A", 60.0, 100.0)]
        [InlineData(@"\H2;A", 1.2, 2.0)]
        [InlineData(@"\H2xA", 120.0, 200.0)]
        [InlineData(@"\H2x;A", 120.0, 200.0)]
        [InlineData(@"\H2B", 1.0, 2.0)]
        [InlineData(@"\H 2B", 1.0, 2.0)]
        [InlineData(@"\H2.5xB", 125.0, 250.0)]
        [InlineData(@"\H1e1;A", 6.0, 10.0)]
        [InlineData(@"\H2;A\H", 1.2, 2.0)]
        [InlineData(@"\Hab;A", 226.0, 115.0)]
        [InlineData(@"\Hxx;A", 156.0, 115.0)]
        [InlineData(@"\H;;A", 86.0, 115.0)]
        [InlineData(@"\W2A", 120.0, 100.0)]
        [InlineData(@"\W2xA", 120.0, 100.0)]
        [InlineData(@"\W;A", 6.0, 100.0)]
        [InlineData(@"\W0.05A", 6.0, 100.0)]
        [InlineData(@"\W0.2A", 12.0, 100.0)]
        [InlineData(@"\Q15A", 60.0, 100.0)]
        [InlineData(@"\Q15xA", 60.0, 100.0)]
        [InlineData(@"\T2;AB", 210.0, 100.0)]
        [InlineData(@"\T2AB", 210.0, 100.0)]
        [InlineData(@"\T;AB", 110.0, 100.0)]
        [InlineData(@"\T0;AB", 110.0, 100.0)]
        [InlineData(@"\T.5xAB", 110.0, 100.0)]
        [InlineData(@"\W;AB", 13.0, 100.0)]
        [InlineData(@"\H2B;C", 2.82, 2.426)]
        [InlineData(@"\H1.5.5;A", 2.73, 1.725)]
        public void ACodeThatNeedsNoSemicolon_OrHasNoNumber_ActsAsAutoCADActs(
            string contents,
            double width,
            double height
        )
        {
            AssertLaid(contents, width, height);
        }

        [Theory]
        [InlineData(@"\H2 B", 166.0472, 107.063)]
        [InlineData(@"\H2-B", 186.0472, 107.063)]
        [InlineData(@"\H2/B", 191.0472, 120.0)]
        [InlineData(@"\H2 1B", 236.0472, 107.063)]
        [InlineData(@"\H2 3", 173.5354, 108.4724)]
        [InlineData(@"\H2 x", 166.0472, 107.063)]
        [InlineData(@"\H-2 B", 220.0, 107.063)]
        [InlineData(@"\H+2 B", 240.0, 107.063)]
        [InlineData(@"\H2 B C", 271.0472, 113.3622)]
        [InlineData(@"\H2 1/0B", 371.0472, 120.0)]
        [InlineData(@"\H2/0B", 261.0472, 120.0)]
        [InlineData(@"\H2--1/2B", 451.0472, 120.0)]
        [InlineData(@"\H2  1/2B", 411.0472, 120.0)]
        [InlineData(@"\H2 1 /2B", 411.0472, 120.0)]
        [InlineData(@"\H2-1-2B", 386.0472, 107.063)]
        [InlineData(@"\H2 1/2B", 1.25, 2.5)]
        [InlineData(@"\H2-1/2B", 1.25, 2.5)]
        [InlineData(@"\H2/3B", 0.3333, 0.6667)]
        [InlineData(@"\H2 1/2 B", 2.5, 2.5)]
        [InlineData(@"\H2 3/4;A", 1.65, 2.75)]
        [InlineData(@"\H12/3B", 2.0, 4.0)]
        [InlineData(@"\H3/2B", 0.75, 1.5)]
        [InlineData(@"\H2/3x;B", 33.3333, 66.6667)]
        [InlineData(@"\H2 1/2xB", 125.0, 250.0)]
        [InlineData(@"\H1/2xB", 25.0, 50.0)]
        [InlineData(@"\H2 1/2/3B", 4.625, 3.0)]
        [InlineData(@"\H2 1/2.5B", 3.65, 2.6575)]
        [InlineData(@"\H2.5 B", 2.5, 2.5)]
        [InlineData(@"\H2.5 1/2B", 7.625, 3.0)]
        [InlineData(@"\H 2/3B", 3.7, 2.4)]
        [InlineData(@"\H 2 1/2B", 6.1, 2.4)]
        [InlineData(@"\W2 B", 16.6047, 107.063)]
        [InlineData(@"\W1 B", 15.0, 100.0)]
        [InlineData(@"\W1 1/2B", 75.0, 100.0)]
        [InlineData(@"\W3/2B", 75.0, 100.0)]
        [InlineData(@"\W2/3B", 33.3333, 100.0)]
        [InlineData(@"\Q30 B", 225.0, 102.8189)]
        [InlineData(@"\Q30 1/2B", 106.9045, 100.0)]
        [InlineData(@"\T2 B", 138.5472, 107.063)]
        [InlineData(@"\T1 1/2B", 50.0, 100.0)]
        public void ANumberOfAnInteger_FollowedByASpaceAHyphenOrASlash_IsAFractionOrNoNumberAtAll(
            string contents,
            double width,
            double height
        )
        {
            // AutoCAD 2021 reads "2 1/2", "2-1/2" and "2/3" as the numbers they are, and an integer left at a space, hyphen or
            // slash that no fraction follows as no number: the code is dropped and the number is text, "\H2 B" showing 2 B.
            AssertLaid(contents, width, height);
        }

        [Theory]
        [InlineData(@"\C1A", 60.0, 100.0)]
        [InlineData(@"\C1 A", 110.0, 100.0)]
        [InlineData(@"\C 1A", 180.0, 100.0)]
        [InlineData(@"\C 1 A", 220.0, 100.0)]
        [InlineData(@"\C;A", 60.0, 100.0)]
        [InlineData(@"\CA", 60.0, 100.0)]
        [InlineData(@"\C12A", 60.0, 100.0)]
        [InlineData(@"\C1.5A", 156.0, 106.2992)]
        [InlineData(@"\C1xA", 130.0, 100.0)]
        [InlineData(@"\C1e1A", 205.4331, 106.2992)]
        [InlineData(@"\C+1A", 210.0, 100.0)]
        [InlineData(@"\C1/2A", 195.0, 120.0)]
        [InlineData(@"\C1-2A", 190.0, 107.063)]
        [InlineData(@"\C-1A", 60.0, 100.0)]
        [InlineData(@"\C", 0.0, 100.0)]
        [InlineData(@"\C1", 0.0, 100.0)]
        [InlineData(@"\c255A", 60.0, 100.0)]
        [InlineData(@"\c255 A", 110.0, 100.0)]
        [InlineData(@"\c 255A", 320.0, 113.3622)]
        [InlineData(@"\c;A", 60.0, 100.0)]
        [InlineData(@"\cA", 60.0, 100.0)]
        [InlineData(@"\c1.5A", 156.0, 106.2992)]
        [InlineData(@"\c255xA", 130.0, 100.0)]
        [InlineData(@"\c+255A", 350.0, 113.3622)]
        [InlineData(@"\c", 0.0, 100.0)]
        [InlineData(@"\c255", 0.0, 100.0)]
        [InlineData(@"X\A1B", 130.0, 100.0)]
        [InlineData(@"X\A1;B", 130.0, 100.0)]
        [InlineData(@"X\A 1B", 240.0, 100.0)]
        [InlineData(@"X\A1 B", 170.0, 100.0)]
        [InlineData(@"X\A", 60.0, 100.0)]
        [InlineData(@"X\A1", 60.0, 100.0)]
        [InlineData(@"X\A+1\H0.5x;Y", 255.0, 100.0)]
        [InlineData(@"X\A1.5\H0.5x;Y", 201.0, 106.2992)]
        [InlineData(@"X\A 1;\H0.5x;Y", 241.0, 115.0)]
        public void TheColourAndAlignCodes_NeedNoSemicolon_AndTakeDigitsOnly(
            string contents,
            double width,
            double height
        )
        {
            AssertLaid(contents, width, height);
        }

        [Theory]
        [InlineData(@"\fArialA", 0.0, 100.0)]
        [InlineData(@"\fArial|b0|i0|c0|p34A", 0.0, 100.0)]
        [InlineData(@"\f A", 0.0, 100.0)]
        [InlineData(@"\fArial", 0.0, 100.0)]
        [InlineData(@"\FromansA", 0.0, 100.0)]
        [InlineData(@"\Fromans|c0A", 0.0, 100.0)]
        [InlineData(@"\f1A", 0.0, 100.0)]
        [InlineData(@"\fArial A", 0.0, 100.0)]
        [InlineData(@"\f", 0.0, 100.0)]
        [InlineData(@"\F", 0.0, 100.0)]
        [InlineData(@"A\fArial", 60.0, 100.0)]
        [InlineData(@"\f;A", 60.0, 100.0)]
        [InlineData(@"\Fopencad2d.shx;A", 60.0, 100.0)]
        [InlineData(@"\SA/B", 60.0, 233.3333)]
        [InlineData(@"\SA/B;C", 125.0, 239.6325)]
        [InlineData(@"A\pxql", 60.0, 100.0)]
        [InlineData(@"A\pxql;B", 130.0, 100.0)]
        [InlineData(@"A\p;B", 130.0, 100.0)]
        public void AFontStackOrParagraphCode_WithNoSemicolon_TakesTheRestOfTheText(
            string contents,
            double width,
            double height
        )
        {
            // AutoCAD 2021 reads a \f, \F, \S or \p value up to the next semicolon wherever it is, and to the end of the text
            // where there is none; the app showed the letter and went on.
            AssertLaid(contents, width, height);
        }
    }
}
