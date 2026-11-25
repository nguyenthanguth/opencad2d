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
            Assert.Equal(width, w, 3);
            Assert.Equal(height, h, 3);
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
    }
}
