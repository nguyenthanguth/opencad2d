using GeometryHelper.Dxf;
using GeometryHelper.Geometry;
using Xunit;

namespace OpenCAD2D.UnitTest
{
    /// <summary>
    /// How far a stacked fraction moves the pen past its ink, as AutoCAD 2021 sets it: a tenth of the text's height in the
    /// built-in font, opencad2d.shx, and a seventh in the other SHX fonts, which the presenter has not measured against
    /// AutoCAD.
    /// </summary>
    public sealed class MTextStackGapTests
    {
        private static double WidthOf(string contents)
        {
            var db = new DxfDatabase("AC1032");
            db.TextStyleTable["Standard"].FileName = "opencad2d.shx";
            var mtext = new DxfMText
            {
                Contents = contents,
                TextHeight = 10.0,
                Location = new GeoPoint3(0, 0, 0),
            };
            db.ModelSpace.AppendEntity(mtext);
            CadDocument document = CadDocument.From(db);
            document.Fonts = new CadFonts(null, CadFonts.BuiltInName, null);
            return document.LayoutOf((DxfMText)document.FindEntity(mtext.Handle)).ActualWidth;
        }

        [Fact]
        public void AStackInTheBuiltInFont_MovesThePenATenthOfTheHeightPastItsInk()
        {
            // AutoCAD 2021, \SH/H;H 10 high in opencad2d.shx: ActualWidth 12.0. The stack is 5 wide of ink, the pen moves 1 past
            // it (a tenth of 10), and the H after it is inked from its left bearing, 1 on, for 5: 5 + 1 + 1 + 5. A seventh of
            // the height gave 12.4286.
            Assert.Equal(12.0, WidthOf(@"\SH/H;H"), 4);
        }
    }
}
