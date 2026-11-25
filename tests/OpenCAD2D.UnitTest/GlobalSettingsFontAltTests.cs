using System;
using System.IO;
using System.Text;
using Xunit;

namespace OpenCAD2D.UnitTest
{
    /// <summary>
    /// The FONTALT replacement a settings file saved before the built-in font became opencad2d: a value of exactly
    /// ghsimplex (or ghsimplex.shx) is read as opencad2d, once, as the settings load; the setter and every other value are
    /// left as they are.
    /// </summary>
    public sealed class GlobalSettingsFontAltTests : IDisposable
    {
        private readonly string _folder = Path.Combine(
            Path.GetTempPath(),
            "gh-fontalt-" + Guid.NewGuid().ToString("N")
        );

        public GlobalSettingsFontAltTests()
        {
            Directory.CreateDirectory(_folder);
        }

        public void Dispose()
        {
            try
            {
                Directory.Delete(_folder, true);
            }
            catch (IOException) { }
        }

        // A settings file whose FONTALT replacement is the text given, as the file of an older version held it.
        private GlobalSettings Read(string replacement)
        {
            string path = Path.Combine(_folder, "settings.json");
            File.WriteAllText(
                path,
                "{\"fonts\":{\"replacement\":\"" + replacement.Replace("\\", "\\\\") + "\"}}",
                new UTF8Encoding(false)
            );
            return GlobalSettings.Load(path);
        }

        [Theory]
        [InlineData("ghsimplex")]
        [InlineData("ghsimplex.shx")]
        [InlineData("GhSimplex.SHX")]
        [InlineData("  ghsimplex ")]
        public void ASavedReplacementOfExactlyGhsimplex_IsReadAsOpencad2d(string saved)
        {
            Assert.Equal(CadFonts.BuiltInName, Read(saved).ReplacementFont);
            Assert.Equal("opencad2d", Read(saved).ReplacementFont);
        }

        [Theory]
        [InlineData("simplex.shx")]
        [InlineData("ghsimplex.shx,gbcbig.shx")]
        [InlineData("ghsimplex2.shx")]
        [InlineData("D:\\fonts\\ghsimplex.shx")]
        [InlineData("Arial")]
        [InlineData("")]
        public void AnyOtherSavedReplacement_IsReadAsItWas(string saved)
        {
            Assert.Equal(saved.Trim(), Read(saved).ReplacementFont);
        }

        [Fact]
        public void TheOldNameSetInCode_IsNotRewritten_ItIsOnlyAFontNotFound()
        {
            var settings = new GlobalSettings { ReplacementFont = "ghsimplex" };

            Assert.Equal("ghsimplex", settings.ReplacementFont);
            Assert.False(CadFonts.IsBuiltIn(settings.ReplacementFont));
        }

        [Fact]
        public void AMigratedReplacement_IsSavedAsOpencad2d()
        {
            string path = Path.Combine(_folder, "saved.json");

            Read("ghsimplex").Save(path);

            Assert.Equal("opencad2d", GlobalSettings.Load(path).ReplacementFont);
            Assert.DoesNotContain("ghsimplex", File.ReadAllText(path));
        }
    }
}
