using System;
using System.IO;
using System.Linq;
using Xunit;

namespace OpenCAD2D.Mcp.UnitTest
{
    /// <summary>The small rules the output and debug tools share: the names of versions, the files a tool may write, the failures kept.</summary>
    public class McpKitTests
    {
        [Theory]
        [InlineData("2000", "AC1015")]
        [InlineData("R2004", "AC1018")]
        [InlineData("r2007", "AC1021")]
        [InlineData("2010", "AC1024")]
        [InlineData(" 2013 ", "AC1027")]
        [InlineData("2018", "AC1032")]
        [InlineData("AC1032", "AC1032")]
        [InlineData("ac1021", "AC1021")]
        [InlineData("12", "AC1009")]
        [InlineData("R12", "AC1009")]
        public void AVersionIsNamedByItsReleaseOrItsCode(string name, string code)
        {
            Assert.Equal(code, McpKit.VersionCode(name));
        }

        [Fact]
        public void NoVersionIsNoCode_AndAnUnknownOneIsRefusedWithTheOnesThereAre()
        {
            Assert.Null(McpKit.VersionCode(null));
            Assert.Null(McpKit.VersionCode("  "));
            ArgumentException refused = Assert.Throws<ArgumentException>(() => McpKit.VersionCode("2015"));
            Assert.Contains("2018", refused.Message);
            Assert.Contains("2015", refused.Message);
            Assert.Throws<ArgumentException>(() => McpKit.VersionCode("AC1999"));
        }

        [Fact]
        public void ATargetTakesTheExtensionAsked_MakesItsFolder_AndRefusesWhatIsThereUnlessAsked()
        {
            string folder = TestFolder.Make("target");
            string deeper = Path.Combine(folder, "a", "b", "plot");

            string full = McpKit.Target(deeper, ".pdf", false, null);

            Assert.Equal(Path.GetFullPath(deeper + ".pdf"), full);
            Assert.True(Directory.Exists(Path.Combine(folder, "a", "b")));
            File.WriteAllText(full, "x");
            Assert.Throws<ArgumentException>(() => McpKit.Target(full, ".pdf", false, null));
            Assert.Equal(full, McpKit.Target(full, ".PDF", true, null));
            Assert.Contains(
                "The file is .svg, not .pdf",
                Assert.Throws<ArgumentException>(() => McpKit.Target(full, ".svg", true, null)).Message
            );
            Assert.Throws<ArgumentException>(() => McpKit.Target("  ", ".pdf", false, null));
            Assert.Contains(
                "own file",
                Assert.Throws<ArgumentException>(() => McpKit.Target(full, ".pdf", false, full)).Message
            );
            Assert.Contains(
                "own file",
                Assert.Throws<ArgumentException>(() => McpKit.Target(full, ".pdf", true, full)).Message
            );
        }

        [Fact]
        public void TheFailuresKept_AreTheNewestFifty_NewestFirst_AndARefusalHasNoStack()
        {
            var failures = new McpFailures();

            for (int i = 0; i < 60; i++)
            {
                failures.Add("tool" + i, new ArgumentException("bad " + i), true);
            }

            try
            {
                throw new InvalidOperationException("broken");
            }
            catch (InvalidOperationException ex)
            {
                failures.Add("last", ex, false);
            }

            Assert.Equal(McpFailures.Kept, failures.Newest(1000).Count);
            Assert.Equal("last", failures.Newest(1)[0].Tool);
            Assert.NotEmpty(failures.Newest(1)[0].Stack);
            Assert.Contains("broken", failures.Newest(1)[0].Message);
            Assert.Equal("tool59", failures.Newest(2)[1].Tool);
            Assert.Empty(failures.Newest(2)[1].Stack);
            Assert.Equal("bad 59", failures.Newest(2)[1].Message);
            Assert.Equal("tool11", failures.Newest(1000).Last().Tool);
        }

        [Fact]
        public void AViewIsKeptForALayoutOfADocument_AndClearedWithNull()
        {
            CadDocument one = CadDocument.From(new GeometryHelper.Dxf.DxfDatabase("AC1027"));
            CadDocument two = CadDocument.From(new GeometryHelper.Dxf.DxfDatabase("AC1027"));

            McpViews.Set(one, "Model", new[] { 0.0, 0.0, 5.0, 5.0 });
            McpViews.Set(one, "layout1", new[] { 1.0, 1.0, 2.0, 2.0 });

            Assert.Equal(new[] { 0.0, 0.0, 5.0, 5.0 }, McpViews.Get(one, null));
            Assert.Equal(new[] { 1.0, 1.0, 2.0, 2.0 }, McpViews.Get(one, "LAYOUT1"));
            Assert.Null(McpViews.Get(two, "Model"));
            McpViews.Set(one, "Model", null);
            Assert.Null(McpViews.Get(one, "Model"));
        }
    }
}
