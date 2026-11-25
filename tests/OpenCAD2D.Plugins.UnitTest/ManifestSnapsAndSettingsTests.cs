using System;
using System.Linq;
using OpenCAD2D.Plugins;
using Xunit;

namespace OpenCAD2D.Plugins.UnitTest
{
    /// <summary>
    /// The object snaps and the settings a Python tool declares in its <c>plugin.json</c>, read without running the tool
    /// (PW3-D12, D13, D15): the keyword in capitals, the DXF names of the entity types, a glyph by marker name or by strokes, and
    /// a setting of each kind with its default, range, choices and description.
    /// </summary>
    public class ManifestSnapsAndSettingsTests
    {
        private static CadPluginManifest Parse(string snaps = null, string settings = null) =>
            CadPluginManifest.Parse(
                "{ \"name\": \"Circles\""
                    + (snaps == null ? string.Empty : ", \"snaps\": " + snaps)
                    + (settings == null ? string.Empty : ", \"settings\": " + settings)
                    + " }"
            );

        private static string Refused(string settings) =>
            Assert.Throws<FormatException>(() => Parse(settings: settings)).Message;

        [Fact]
        public void AManifestReadsASnapWithItsKeywordInCapitalsNameDxfTypesGlyphAndOnByDefault()
        {
            CadPluginManifest manifest = Parse(
                "[ { \"keyword\": \"ctop\", \"name\": \"Circle top\", \"entityTypes\": [\"circle\", \"ARC\"], \"glyph\": \"quadrant\", \"onByDefault\": true },"
                    + " { \"keyword\": \"BOLT\", \"name\": \"Bolt centre\" } ]"
            );

            Assert.Equal(new[] { "CTOP", "BOLT" }, manifest.Snaps.Select(s => s.Keyword));
            CadPluginSnapEntry top = manifest.Snaps[0];
            Assert.Equal("Circle top", top.Name);
            Assert.Equal(new[] { "CIRCLE", "ARC" }, top.EntityTypes);
            Assert.NotNull(top.Glyph);
            Assert.True(top.IsOnByDefault);

            CadPluginSnapEntry bolt = manifest.Snaps[1];
            Assert.Empty(bolt.EntityTypes);
            Assert.Null(bolt.Glyph);
            Assert.False(bolt.IsOnByDefault);
        }

        [Fact]
        public void AManifestWithNoSnapsOrSettingsHasNone()
        {
            CadPluginManifest manifest = CadPluginManifest.Parse("{ \"name\": \"Tools\" }");

            Assert.Empty(manifest.Snaps);
            Assert.Empty(manifest.Settings);
        }

        [Fact]
        public void AGlyphNamedQuadrant_IsTheQuadrantMarkersStrokes_InAnyCase()
        {
            CadPluginManifest manifest = Parse(
                "[ { \"keyword\": \"CTOP\", \"name\": \"Circle top\", \"glyph\": \"Quadrant\" } ]"
            );

            var expected = CadSnapMarker.Strokes(CadSnapKind.Quadrant);
            var glyph = manifest.Snaps[0].Glyph;

            Assert.Equal(expected.Count, glyph.Count);

            for (int i = 0; i < expected.Count; i++)
            {
                Assert.Equal(expected[i].Points, glyph[i].Points);
                Assert.Equal(expected[i].IsClosed, glyph[i].IsClosed);
            }
        }

        [Fact]
        public void StrokesGiven_AreReadInPixels_WithTheirFlags()
        {
            CadPluginManifest manifest = Parse(
                "[ { \"keyword\": \"CTOP\", \"name\": \"Circle top\", \"glyph\": ["
                    + " { \"points\": [[-4.5, 4.5], [4.5, 4.5], [0, -4.5]], \"closed\": true, \"filled\": true },"
                    + " { \"points\": [[0, 0], [0, 6]], \"dotted\": true } ] } ]"
            );

            var glyph = manifest.Snaps[0].Glyph;

            Assert.Equal(2, glyph.Count);
            Assert.Equal(3, glyph[0].Points.Count);
            Assert.Equal(-4.5, glyph[0].Points[0].X);
            Assert.Equal(4.5, glyph[0].Points[0].Y);
            Assert.Equal(-4.5, glyph[0].Points[2].Y);
            Assert.True(glyph[0].IsClosed);
            Assert.True(glyph[0].IsFilled);
            Assert.False(glyph[0].IsDotted);
            Assert.Equal(6.0, glyph[1].Points[1].Y);
            Assert.False(glyph[1].IsClosed);
            Assert.True(glyph[1].IsDotted);
        }

        [Theory]
        [InlineData("\"nosuchmarker\"")]
        [InlineData("\"grid\"")]
        [InlineData("[ { \"points\": [[0, 0]] } ]")]
        [InlineData("[ { \"points\": [[0, 0], [1]] } ]")]
        [InlineData("[ { \"closed\": true } ]")]
        [InlineData("[ ]")]
        [InlineData("3")]
        public void AnUnknownGlyphNameOrAMalformedStroke_IsRefused_AndNamesTheSnap(string glyph)
        {
            FormatException refused = Assert.Throws<FormatException>(() =>
                Parse("[ { \"keyword\": \"CTOP\", \"name\": \"Circle top\", \"glyph\": " + glyph + " } ]")
            );

            Assert.Contains("CTOP", refused.Message, StringComparison.Ordinal);
            Assert.Contains("glyph", refused.Message, StringComparison.Ordinal);
        }

        [Fact]
        public void ASnapWithoutAKeywordOrAName_IsPassedOver()
        {
            CadPluginManifest manifest = Parse(
                "[ { \"name\": \"No keyword\" }, { \"keyword\": \"NONAME\" }, { \"keyword\": \" \", \"name\": \"Blank\" }, 5,"
                    + " { \"keyword\": \"OK\", \"name\": \"Kept\" } ]"
            );

            Assert.Equal("OK", Assert.Single(manifest.Snaps).Keyword);
        }

        [Fact]
        public void AManifestReadsASettingOfEachKind_WithItsDefaultRangeChoicesAndDescription()
        {
            CadPluginManifest manifest = Parse(
                settings: "[ { \"name\": \"textHeight\", \"label\": \"Text height\", \"kind\": \"number\", \"default\": 2.5, \"minimum\": 0.1, \"maximum\": 1000, \"description\": \"How high the marks are written.\" },"
                    + " { \"name\": \"start\", \"kind\": \"integer\", \"default\": 1, \"minimum\": 0 },"
                    + " { \"name\": \"boxed\", \"kind\": \"boolean\", \"default\": true },"
                    + " { \"name\": \"prefix\", \"kind\": \"text\", \"default\": \"N\" },"
                    + " { \"name\": \"side\", \"label\": \"Side\", \"kind\": \"choice\", \"choices\": [\"Left\", \"Right\"], \"default\": \"right\" } ]"
            );

            Assert.Equal(
                new[] { "textHeight", "start", "boxed", "prefix", "side" },
                manifest.Settings.Select(s => s.Name)
            );

            CadPluginSettingEntry height = manifest.Settings[0];
            Assert.Equal("Text height", height.Label);
            Assert.Equal(CadSettingKind.Number, height.Kind);
            Assert.Equal(2.5, Assert.IsType<double>(height.Default));
            Assert.Equal(0.1, height.Minimum);
            Assert.Equal(1000.0, height.Maximum);
            Assert.Equal("How high the marks are written.", height.Description);
            Assert.Empty(height.Choices);

            CadPluginSettingEntry start = manifest.Settings[1];
            Assert.Equal(CadSettingKind.Integer, start.Kind);
            Assert.Equal(1, Assert.IsType<int>(start.Default));
            Assert.Equal(0.0, start.Minimum);
            Assert.Equal(double.PositiveInfinity, start.Maximum);
            Assert.Null(start.Description);

            Assert.Equal(CadSettingKind.Boolean, manifest.Settings[2].Kind);
            Assert.True(Assert.IsType<bool>(manifest.Settings[2].Default));
            Assert.Equal(double.NegativeInfinity, manifest.Settings[2].Minimum);
            Assert.Equal("N", Assert.IsType<string>(manifest.Settings[3].Default));
            Assert.Equal(CadSettingKind.Text, manifest.Settings[3].Kind);

            CadPluginSettingEntry side = manifest.Settings[4];
            Assert.Equal(CadSettingKind.Choice, side.Kind);
            Assert.Equal(new[] { "Left", "Right" }, side.Choices);
            Assert.Equal("Right", side.Default);
        }

        [Fact]
        public void TheLabelOfASetting_IsItsNameWhereNotGiven()
        {
            CadPluginManifest manifest = Parse(
                settings: "[ { \"name\": \"start\", \"kind\": \"integer\", \"default\": 1 } ]"
            );

            Assert.Equal("start", manifest.Settings[0].Label);
        }

        [Theory]
        [InlineData("[ { \"name\": \"h\", \"kind\": \"number\", \"default\": \"tall\" } ]", "default")]
        [InlineData("[ { \"name\": \"h\", \"kind\": \"number\", \"default\": 0.05, \"minimum\": 0.1 } ]", "outside")]
        [InlineData(
            "[ { \"name\": \"h\", \"kind\": \"number\", \"default\": 5, \"minimum\": 10, \"maximum\": 1 } ]",
            "above the maximum"
        )]
        [InlineData("[ { \"name\": \"n\", \"kind\": \"integer\", \"default\": 1.5 } ]", "whole")]
        [InlineData("[ { \"name\": \"b\", \"kind\": \"boolean\", \"default\": 1 } ]", "true or false")]
        [InlineData("[ { \"name\": \"t\", \"kind\": \"text\", \"default\": 4 } ]", "text")]
        [InlineData(
            "[ { \"name\": \"s\", \"kind\": \"choice\", \"choices\": [\"Left\", \"Right\"], \"default\": \"Up\" } ]",
            "choices"
        )]
        [InlineData("[ { \"name\": \"s\", \"kind\": \"choice\", \"default\": \"Up\" } ]", "choices")]
        [InlineData("[ { \"name\": \"h\", \"kind\": \"number\" } ]", "default")]
        [InlineData("[ { \"name\": \"h\", \"kind\": \"colour\", \"default\": 1 } ]", "kind")]
        [InlineData("[ { \"name\": \"2nd\", \"kind\": \"number\", \"default\": 1 } ]", "identifier")]
        [InlineData("[ { \"name\": \"text height\", \"kind\": \"number\", \"default\": 1 } ]", "identifier")]
        [InlineData("[ { \"kind\": \"number\", \"default\": 1 } ]", "identifier")]
        [InlineData(
            "[ { \"name\": \"h\", \"kind\": \"number\", \"default\": 1 }, { \"name\": \"h\", \"kind\": \"text\", \"default\": \"\" } ]",
            "twice"
        )]
        [InlineData("[ 4 ]", "object")]
        public void ASettingThatBreaksARule_MakesTheManifestUnreadable_WithTheRuleInTheMessage(
            string settings,
            string rule
        )
        {
            Assert.Contains(rule, Refused(settings), StringComparison.Ordinal);
        }

        [Fact]
        public void TheSettingsOfAManifestThatIsNoArray_AreNone()
        {
            Assert.Empty(CadPluginManifest.Parse("{ \"name\": \"Tools\", \"settings\": { \"h\": 1 } }").Settings);
        }
    }
}
