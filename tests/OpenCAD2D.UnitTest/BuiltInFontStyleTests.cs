using System.Linq;
using GeometryHelper.Dxf;
using Xunit;
using static OpenCAD2D.UnitTest.EditingDrawings;

namespace OpenCAD2D.UnitTest
{
    /// <summary>The presenter's own font, opencad2d, known by its name wherever a font is named: typed to -STYLE it is an SHX font, not a TrueType family.</summary>
    public class BuiltInFontStyleTests
    {
        [Fact]
        public void TheBuiltInNameIsKnownWithOrWithoutShx_InEitherCase_AndNoOtherIs()
        {
            Assert.True(CadFonts.IsBuiltIn("opencad2d"));
            Assert.True(CadFonts.IsBuiltIn(" OPENCAD2D.SHX "));
            Assert.False(CadFonts.IsBuiltIn("simplex.shx"));
            Assert.False(CadFonts.IsBuiltIn("opencad2d.ttf"));
            Assert.False(CadFonts.IsBuiltIn(null));
        }

        [Fact]
        public void TheBuiltInIsNamedOpenCad2d_AndTitledOpenCad2dIso()
        {
            Assert.Equal("opencad2d", CadFonts.BuiltInName);
            Assert.Equal("OpenCAD2D ISO", CadFonts.BuiltInTitle);
        }

        [Fact]
        public void TheOldBuiltInName_Ghsimplex_IsNotTheBuiltInFontAnyMore_WithOrWithoutShx()
        {
            Assert.False(CadFonts.IsBuiltIn("ghsimplex"));
            Assert.False(CadFonts.IsBuiltIn("ghsimplex.shx"));
            Assert.False(CadFonts.IsBuiltIn(" GHSIMPLEX.SHX "));
        }

        [Fact]
        public void Opencad2dTypedToStyle_IsTheBuiltInShxFont_NotATrueTypeFamily()
        {
            CadDocument document = Blank();
            CadCommandSession session = Session(document);

            Run(session, "-STYLE", "OC_OWN", "opencad2d", "0", "1", "0", "N", "N", "N");

            DxfTextStyleTableRecord style = document.Database.TextStyleTable["OC_OWN"];
            Assert.Equal("opencad2d.shx", style.FileName);
            Assert.Equal(string.Empty, style.Font.TypeFace);
            Assert.False(session.IsRunning);
            Assert.Equal("\"OC_OWN\" is now the current text style.", session.History.Last());
        }
    }
}
