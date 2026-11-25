using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using GeometryHelper.Geometry;
using OpenCAD2D.Plugins;
using Xunit;

#if NET8_0_OR_GREATER
namespace OpenCAD2D.Plugins.UnitTest
{
    /// <summary>
    /// The sample plugin that ships in the Plugins folder, Drawing info, as C# source: it is the folder <c>Plugins\DrawingInfo</c> with
    /// <c>DrawingInfo.csproj</c> and <c>DrawingInfoCommands.cs</c>, which the app compiles at start as its own folder does, and its
    /// command CSINFO writes what the drawing holds and changes nothing. On .NET 8 only: .NET Framework does not compile source.
    /// </summary>
    public sealed class SourcePluginDrawingInfoTests : IDisposable
    {
        private readonly PluginFiles _files = new PluginFiles();

        public void Dispose() => _files.Dispose();

        /// <summary>Copies the two files of the sample, as the build of the app does, into Plugins\DrawingInfo of the test folder.</summary>
        private void Install()
        {
            string sample = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "DrawingInfo");
            string folder = Path.Combine(_files.PluginFolder, "DrawingInfo");
            Directory.CreateDirectory(folder);
            File.Copy(Path.Combine(sample, "DrawingInfo.csproj.txt"), Path.Combine(folder, "DrawingInfo.csproj"));
            File.Copy(
                Path.Combine(sample, "DrawingInfoCommands.cs.txt"),
                Path.Combine(folder, "DrawingInfoCommands.cs")
            );
        }

        [Fact]
        public void ThePluginInThePluginsFolder_LoadsAtStartWithNoQuestion_AsTheDrawingInfoPlugin()
        {
            Install();
            CadPluginManager manager = _files.Manager();
            manager.ConfirmUntrusted = path => throw new InvalidOperationException("Not to be asked: " + path);
            var registry = CadCommandRegistry.CreateDefault();
            manager.Attach(registry);

            using (manager)
            {
                CadPluginResult started = Assert.Single(manager.LoadAtStart());

                Assert.True(started.Succeeded, started.ToString());
                Assert.Equal("DrawingInfo", started.Plugin.Name);
                Assert.Equal("1.0.0", started.Plugin.Plugin.Version);
                Assert.True(started.Plugin.IsTrusted);
                Assert.True(started.Plugin.IsSource);
                Assert.Equal(new[] { "CSINFO" }, started.Plugin.Plugin.Commands.Select(c => c.Name));
                Assert.NotNull(registry.Find("CSINFO"));
            }
        }

        [Fact]
        public void TheFolderHoldsTheCsprojAndTheSourceOnly_NoManifest_NoDll()
        {
            Install();

            Assert.Equal(
                new[] { "DrawingInfo.csproj", "DrawingInfoCommands.cs" },
                Directory
                    .GetFiles(Path.Combine(_files.PluginFolder, "DrawingInfo"))
                    .Select(Path.GetFileName)
                    .OrderBy(n => n, StringComparer.Ordinal)
            );
        }

        [Fact]
        public void Csinfo_OnThreeCirclesAndALine_WritesTheNameTheObjectsByTypeTheLayersTheBlocksAndTheExtents()
        {
            Install();
            CadDocument document = PluginFiles.Circles();
            document.Database.Extmin = new GeoPoint3(-10, -40, 0);
            document.Database.Extmax = new GeoPoint3(110, 30, 0);
            var registry = CadCommandRegistry.CreateDefault();

            using (CadPluginManager manager = _files.Manager())
            {
                manager.Attach(registry);
                manager.LoadAtStart();
                CadCommandSession session = PluginFiles.Session(document, registry);

                session.Feed("CSINFO");

                Assert.Equal(CadCommandOutcome.Completed, session.LastOutcome);
                Assert.Equal(
                    new[]
                    {
                        "Drawing: circles.dxf",
                        "Model space: 4 objects",
                        "  Circle   3",
                        "  Line     1",
                        "Layers: 1, blocks: 0",
                        "Extents: (-10.00,-40.00) to (110.00,30.00)",
                    },
                    session.History.Skip(1).Select(l => l.TrimEnd()).ToArray()
                );
                Assert.False(document.CanUndo);
            }
        }
    }
}
#endif
