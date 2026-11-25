using System;
using System.Linq;
using OpenCAD2D.Plugins;
using Xunit;

namespace OpenCAD2D.Plugins.UnitTest
{
    /// <summary>
    /// A C# plugin declares its settings with <c>[CadSetting]</c> and its snaps in code, so what its <c>plugin.json</c> says of
    /// <c>settings</c> and <c>snaps</c> is ignored (PW3-D15): a malformed one must not keep the plugin from loading, as it keeps a
    /// Python tool, whose declarations they are, from loading.
    /// </summary>
    public sealed class ManifestIgnoredForPluginsTests : IDisposable
    {
        private readonly PluginFiles _files = new PluginFiles();

        public void Dispose() => _files.Dispose();

        private const string Broken =
            "\"settings\": [ { \"name\": \"1 bad\", \"kind\": \"number\", \"default\": \"two\" } ],"
            + " \"snaps\": [ { \"keyword\": \"CTOP\", \"name\": \"Circle top\", \"glyph\": \"no such marker\" } ]";

        private static string Manifest() =>
            "{ \"id\": \"number-circles\", \"name\": \"Number circles\", \"version\": \"1.2.0\", \"commands\": [ \"NUMCIRCLES\" ], "
            + Broken
            + " }";

        [Fact]
        public void ACSharpPluginWhoseManifestHasAMalformedSettingsArrayAndAGlyphThatIsNone_StillLoads()
        {
            string folder = _files.SampleFolder(Manifest());

            using (CadPluginManager manager = _files.Manager(true))
            {
                CadPluginResult loaded = manager.Load(folder);

                Assert.True(loaded.Succeeded, loaded.ToString());
                Assert.Equal("Number circles", loaded.Plugin.Name);
            }
        }

        [Fact]
        public void ACSharpPluginsManifest_HasNoSnapsAndNoSettings_WhateverItsFileSays()
        {
            string folder = _files.SampleFolder(
                "{ \"name\": \"Tools\", \"settings\": [ { \"name\": \"height\", \"kind\": \"number\", \"default\": 2.5 } ],"
                    + " \"snaps\": [ { \"keyword\": \"CTOP\", \"name\": \"Circle top\" } ] }"
            );

            using (CadPluginManager manager = _files.Manager(true))
            {
                CadPluginResult loaded = manager.Load(folder);

                Assert.True(loaded.Succeeded, loaded.ToString());
                Assert.Empty(loaded.Plugin.Manifest.Settings);
                Assert.Empty(loaded.Plugin.Manifest.Snaps);
            }
        }

        [Fact]
        public void ThePythonToolsRead_OfTheSameFile_StillRefusesIt()
        {
            Assert.Throws<FormatException>(() => CadPluginManifest.Parse(Manifest()));
        }
    }
}
