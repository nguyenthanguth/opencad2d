using System;
using System.IO;
using System.Linq;
using OpenCAD2D.Plugins;
using Xunit;

namespace OpenCAD2D.Plugins.UnitTest
{
    public sealed class ManifestTests : IDisposable
    {
        private readonly PluginFiles _files = new PluginFiles();

        public void Dispose() => _files.Dispose();

        [Fact]
        public void AManifestGivesWhatThePluginSaysOfItself()
        {
            CadPluginManifest manifest = CadPluginManifest.Parse(PluginFiles.Manifest());

            Assert.Equal("number-circles", manifest.Id);
            Assert.Equal("Number circles", manifest.Name);
            Assert.Equal("1.2.0", manifest.Version);
            Assert.Equal("Nguyen Thang", manifest.Author);
            Assert.Equal("command", manifest.Kind);
            Assert.Equal(new Version(1, 0), manifest.MinimumApiVersion);
            Assert.Equal(new[] { "NUMCIRCLES", "FAILNOW" }, manifest.Commands);
            Assert.Equal("icon.png", manifest.Icon);
            Assert.Null(manifest.Refusal());
        }

        [Fact]
        public void AManifestWithANameOnlyIsACommandPluginOfApiOneWhoseIdIsItsName()
        {
            CadPluginManifest manifest = CadPluginManifest.Parse("{ \"name\": \"Tools\", \"extra\": 3 }");

            Assert.Equal("Tools", manifest.Id);
            Assert.Equal("command", manifest.Kind);
            Assert.Equal(new Version(1, 0), manifest.MinimumApiVersion);
            Assert.Empty(manifest.Commands);
            Assert.Null(manifest.Icon);
            Assert.Equal(string.Empty, manifest.Version);
        }

        [Fact]
        public void TheCommandsOfAManifest_AreNamesOrObjects_InTheOrderWritten()
        {
            CadPluginManifest manifest = CadPluginManifest.Parse(
                "{ \"name\": \"Tool\", \"commands\": [ \"first\", { \"name\": \"second\", \"alias\": \"sc\", \"description\": \"The second\", \"usesSelection\": true }, { \"alias\": \"none\" }, \" \", 5 ] }"
            );

            Assert.Equal(new[] { "FIRST", "SECOND" }, manifest.Commands);
            Assert.Equal(new[] { "FIRST", "SECOND" }, manifest.CommandEntries.Select(c => c.Name));
            Assert.Null(manifest.CommandEntries[0].Alias);
            Assert.Null(manifest.CommandEntries[0].Description);
            Assert.False(manifest.CommandEntries[0].UsesSelection);
            Assert.Equal("SC", manifest.CommandEntries[1].Alias);
            Assert.Equal("The second", manifest.CommandEntries[1].Description);
            Assert.True(manifest.CommandEntries[1].UsesSelection);
        }

        [Fact]
        public void TheRibbonOfAManifest_IsTabsOfPanelsOfButtons_AndALeftOutPartIsPassedOver()
        {
            CadPluginManifest manifest = CadPluginManifest.Parse(
                "{ \"name\": \"Tool\", \"ribbon\": [ "
                    + "{ \"id\": \"tools\", \"title\": \"Tools\", \"panels\": [ "
                    + "{ \"title\": \"Draw\", \"buttons\": [ "
                    + "{ \"text\": \"Ring\", \"command\": \"RING\", \"icon\": \"ring.png\", \"size\": \"large\", \"tooltip\": \"Draws a ring\" }, "
                    + "{ \"text\": \"Dot\", \"command\": \"DOT\" }, "
                    + "{ \"text\": \"No command\" } ] } ] }, "
                    + "{ \"title\": \"Second\" }, "
                    + "{ \"id\": \"untitled\" } ] }"
            );

            Assert.Equal(new[] { "tools", "Second" }, manifest.Ribbon.Select(t => t.Id));
            CadPluginRibbonTabEntry tab = manifest.Ribbon[0];
            Assert.Equal("Tools", tab.Title);
            CadPluginRibbonPanelEntry panel = Assert.Single(tab.Panels);
            Assert.Equal("Draw", panel.Title);
            Assert.Equal(new[] { "Ring", "Dot" }, panel.Buttons.Select(b => b.Text));
            Assert.Equal("RING", panel.Buttons[0].Command);
            Assert.Equal("ring.png", panel.Buttons[0].Icon);
            Assert.True(panel.Buttons[0].IsLarge);
            Assert.Equal("Draws a ring", panel.Buttons[0].ToolTip);
            Assert.False(panel.Buttons[1].IsLarge);
            Assert.Null(panel.Buttons[1].Icon);
            Assert.Empty(manifest.Ribbon[1].Panels);
        }

        [Fact]
        public void AManifestWithNoRibbon_HasNoTabs_AndAnOldManifestOfNamesReadsAsBefore()
        {
            CadPluginManifest manifest = CadPluginManifest.Parse(PluginFiles.Manifest());

            Assert.Empty(manifest.Ribbon);
            Assert.Equal(new[] { "NUMCIRCLES", "FAILNOW" }, manifest.CommandEntries.Select(c => c.Name));
        }

        [Theory]
        [InlineData("{ \"id\": \"x\" }", "no name")]
        [InlineData("not json", "not JSON")]
        [InlineData("{ \"name\": \"X\", \"minimumApiVersion\": \"one\" }", "not a version")]
        public void AManifestThatCannotBeReadIsRefusedSayingWhy(string json, string why)
        {
            FormatException refused = Assert.Throws<FormatException>(() => CadPluginManifest.Parse(json));

            Assert.Contains(why, refused.Message, StringComparison.Ordinal);
        }

        [Fact]
        public void APluginThatNeedsANewerApiOrIsOfAnotherKindIsRefusedSayingWhy()
        {
            Assert.Equal(
                "The plugin Number circles needs version 2.0 of the plugin API, and this presenter has 1.1: it needs a newer presenter.",
                CadPluginManifest.Parse(PluginFiles.Manifest(api: "2.0")).Refusal()
            );
            Assert.Equal(
                "The plugin Number circles is of the kind \"drawing\", and this presenter loads command plugins only.",
                CadPluginManifest.Parse(PluginFiles.Manifest(kind: "drawing")).Refusal()
            );
        }

        [Fact]
        public void TheManifestOfAPluginJsonFileIsRead_AndAFileOfAnyOtherNameIsReadAsJsonToo()
        {
            string path = Path.Combine(_files.Root, CadPluginManifest.FileName);
            File.WriteAllText(path, PluginFiles.Manifest());

            CadPluginManifest manifest = CadPluginManifest.Read(path);

            Assert.Equal("Number circles", manifest.Name);
            Assert.Equal(path, Assert.Single(Directory.GetFileSystemEntries(_files.Root)));
            Assert.Throws<FileNotFoundException>(() => CadPluginManifest.Read(Path.Combine(_files.Root, "none.json")));

            // A .ghplugin is not a package any more: it is read as the JSON it is not.
            string zip = Path.Combine(_files.Root, "sample.ghplugin");
            File.WriteAllBytes(zip, new byte[] { 0x50, 0x4b, 3, 4 });
            Assert.Throws<FormatException>(() => CadPluginManifest.Read(zip));
        }
    }
}
