using System.IO;
using System.Text;
using Xunit;

namespace OpenCAD2D.UnitTest
{
    /// <summary>The built-in font is OpenCAD2D ISO, in its title and in the shipped .shx file.</summary>
    public sealed class BuiltInFontTitleTests
    {
        [Fact]
        public void TheBuiltInFont_IsOpenCad2dIso_InItsTitle_AndInTheShippedShxFile()
        {
            Assert.Equal("OpenCAD2D ISO", CadFonts.BuiltInTitle);
            string shipped = Encoding.ASCII.GetString(
                File.ReadAllBytes(
                    Path.Combine(
                        CadSupportFolders.Default.FolderOf(CadSupportKind.Fonts),
                        CadFonts.BuiltInName + ".shx"
                    )
                )
            );

            Assert.Contains("OpenCAD2D ISO\0", shipped);
            Assert.DoesNotContain("GeometryHelper", shipped);
        }
    }
}
