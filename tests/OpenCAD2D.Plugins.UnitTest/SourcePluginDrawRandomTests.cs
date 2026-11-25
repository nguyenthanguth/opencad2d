using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using GeometryHelper.Dxf;
using GeometryHelper.Geometry;
using OpenCAD2D.Plugins;
using Xunit;

#if NET8_0_OR_GREATER
namespace OpenCAD2D.Plugins.UnitTest
{
    /// <summary>
    /// The sample plugin that ships in the Plugins folder, DrawRandom, as C# source: the folder <c>Plugins\DrawRandom</c> with
    /// <c>DrawRandom.csproj</c> and <c>DrawRandomCommands.cs</c>, which the app compiles at start as its own folder does. Its
    /// command CSRANDOM asks for a number and an area and draws that many objects of random kinds on the layers RANDOM-1 to
    /// RANDOM-5, in one undo step. On .NET 8 only: .NET Framework does not compile source.
    /// </summary>
    public sealed class SourcePluginDrawRandomTests : IDisposable
    {
        private const string SeedVariable = "OPENCAD2D_RANDOM_SEED";
        private const double Slack = 1e-6;

        private readonly PluginFiles _files = new PluginFiles();
        private readonly string _seedBefore = Environment.GetEnvironmentVariable(SeedVariable);

        public void Dispose()
        {
            Environment.SetEnvironmentVariable(SeedVariable, _seedBefore);
            _files.Dispose();
        }

        /// <summary>Copies the two files of the sample, as the build of the app does, into Plugins\DrawRandom of the test folder.</summary>
        private void Install()
        {
            string sample = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "DrawRandom");
            string folder = Path.Combine(_files.PluginFolder, "DrawRandom");
            Directory.CreateDirectory(folder);
            File.Copy(Path.Combine(sample, "DrawRandom.csproj.txt"), Path.Combine(folder, "DrawRandom.csproj"), true);
            File.Copy(
                Path.Combine(sample, "DrawRandomCommands.cs.txt"),
                Path.Combine(folder, "DrawRandomCommands.cs"),
                true
            );
        }

        /// <summary>The drawing of three circles and a line, and a session of it with the sample loaded; the manager is the caller's to dispose.</summary>
        private CadCommandSession Start(out CadDocument document, out CadPluginManager manager)
        {
            Install();
            document = PluginFiles.Circles();
            document.Database.Extmin = new GeoPoint3(-10, -40, 0);
            document.Database.Extmax = new GeoPoint3(110, 30, 0);
            var registry = CadCommandRegistry.CreateDefault();
            manager = _files.Manager();
            manager.Attach(registry);
            manager.LoadAtStart();
            return PluginFiles.Session(document, registry);
        }

        /// <summary>The objects CSRANDOM added: those of model space after the four the drawing had.</summary>
        private static List<DxfEntity> Drawn(CadDocument document) => document.ModelSpace.Block.Skip(4).ToList();

        [Fact]
        public void ThePluginInThePluginsFolder_LoadsAtStartWithNoQuestion_AsTheDrawRandomPlugin()
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
                Assert.Equal("DrawRandom", started.Plugin.Name);
                Assert.Equal("1.0.0", started.Plugin.Plugin.Version);
                Assert.True(started.Plugin.IsTrusted);
                Assert.True(started.Plugin.IsSource);
                Assert.Equal(new[] { "CSRANDOM" }, started.Plugin.Plugin.Commands.Select(c => c.Name));
                Assert.NotNull(registry.Find("CSRANDOM"));
            }
        }

        [Fact]
        public void TheFolderHoldsTheCsprojAndTheSourceOnly_NoManifest_NoDll()
        {
            Install();

            Assert.Equal(
                new[] { "DrawRandom.csproj", "DrawRandomCommands.cs" },
                Directory
                    .GetFiles(Path.Combine(_files.PluginFolder, "DrawRandom"))
                    .Select(Path.GetFileName)
                    .OrderBy(n => n, StringComparer.Ordinal)
            );
        }

        [Fact]
        public void Csrandom_With300ObjectsAndTwoCorners_DrawsThemInsideTheAreaOnTheFiveLayers_InOneUndoStep()
        {
            Environment.SetEnvironmentVariable(SeedVariable, "7");
            CadCommandSession session = Start(out CadDocument document, out CadPluginManager manager);

            using (manager)
            {
                session.Feed("CSRANDOM");
                Assert.Equal("Number of objects <50>:", session.Prompt.Text);
                session.Feed("300");
                Assert.Equal("Specify first corner <view>:", session.Prompt.Text);
                session.Feed("100,200");
                Assert.Equal("Specify opposite corner:", session.Prompt.Text);
                session.Feed("600,500");

                Assert.Equal(CadCommandOutcome.Completed, session.LastOutcome);
                Assert.Equal("Drew 300 objects on 5 layers.", session.History.Last().TrimEnd());
                List<DxfEntity> drawn = Drawn(document);
                Assert.Equal(300, drawn.Count);

                foreach (DxfEntity entity in drawn)
                {
                    GeoAabb3 box = entity.GeometricExtents;
                    Assert.True(
                        box.Min.X >= 100 - Slack
                            && box.Max.X <= 600 + Slack
                            && box.Min.Y >= 200 - Slack
                            && box.Max.Y <= 500 + Slack,
                        entity.GetType().Name + " " + entity.Handle + " lies outside the area: " + box
                    );
                    Assert.Matches("^RANDOM-[1-5]$", entity.Layer);
                    Assert.True(entity.ColorIndex >= 1 && entity.ColorIndex <= 256, "colour " + entity.ColorIndex);
                }

                // 300 draws: every kind, every layer, both colourings and both kinds of polyline are there.
                Assert.Equal(
                    new[] { "DxfArc", "DxfCircle", "DxfDBText", "DxfLine", "DxfPolyline" },
                    drawn.Select(e => e.GetType().Name).Distinct().OrderBy(n => n, StringComparer.Ordinal)
                );
                Assert.Equal(5, drawn.Select(e => e.Layer).Distinct().Count());
                Assert.Contains(drawn, e => e.ColorIndex == 256);
                Assert.Contains(drawn, e => e.ColorIndex < 256);
                Assert.Contains(drawn, e => e is DxfPolyline p && p.Closed && p.NumberOfVertices == 4);
                Assert.Contains(drawn, e => e is DxfPolyline p && !p.Closed && p.NumberOfVertices >= 3);
                Assert.All(drawn.OfType<DxfPolyline>(), p => Assert.InRange(p.NumberOfVertices, 3, 6));

                for (int i = 1; i <= 5; i++)
                {
                    DxfLayerTableRecord layer = document.Database.LayerTable["RANDOM-" + i];
                    Assert.InRange(layer.ColorIndex, 1, 255);
                }

                // One step takes the objects and the layers back.
                Assert.True(document.CanUndo);
                document.Undo();
                Assert.Empty(Drawn(document));
                Assert.False(document.Database.LayerTable.Has("RANDOM-1"));
                Assert.False(document.Database.LayerTable.Has("RANDOM-5"));
                Assert.False(document.CanUndo);
            }
        }

        [Fact]
        public void Csrandom_WithEnterTwice_Draws50ObjectsInTheExtentsOfTheDrawing()
        {
            CadCommandSession session = Start(out CadDocument document, out CadPluginManager manager);

            using (manager)
            {
                session.Feed("CSRANDOM");
                session.Enter();
                session.Enter();

                Assert.Equal(CadCommandOutcome.Completed, session.LastOutcome);
                Assert.Equal("Drew 50 objects on 5 layers.", session.History.Last().TrimEnd());
                List<DxfEntity> drawn = Drawn(document);
                Assert.Equal(50, drawn.Count);
                Assert.All(drawn, e =>
                {
                    GeoAabb3 box = e.GeometricExtents;
                    Assert.True(
                        box.Min.X >= -10 - Slack
                            && box.Max.X <= 110 + Slack
                            && box.Min.Y >= -40 - Slack
                            && box.Max.Y <= 30 + Slack,
                        e.GetType().Name + " lies outside the extents: " + box
                    );
                });
            }
        }

        [Fact]
        public void Csrandom_OnABlankDrawing_TakesAnAreaOf1000By1000_AndKeepsALayerThatIsThereAsItIs()
        {
            Install();
            var database = new DxfDatabase("AC1027");
            database.LayerTable.Add(new DxfLayerTableRecord { Name = "RANDOM-3", ColorIndex = 1 });
            CadDocument document = CadDocument.From(database, "blank.dxf");
            var registry = CadCommandRegistry.CreateDefault();

            using (CadPluginManager manager = _files.Manager())
            {
                manager.Attach(registry);
                manager.LoadAtStart();
                CadCommandSession session = PluginFiles.Session(document, registry);

                session.Feed("CSRANDOM");
                session.Feed("40");
                session.Enter();

                Assert.Equal("Drew 40 objects on 5 layers.", session.History.Last().TrimEnd());
                Assert.Equal(40, document.ModelSpace.Block.Count());
                Assert.Equal(1, document.Database.LayerTable["RANDOM-3"].ColorIndex);
                Assert.Equal(6, document.Database.LayerTable.Count());
                Assert.All(document.ModelSpace.Block, e =>
                {
                    GeoAabb3 box = e.GeometricExtents;
                    Assert.True(
                        box.Min.X >= -Slack
                            && box.Max.X <= 1000 + Slack
                            && box.Min.Y >= -Slack
                            && box.Max.Y <= 1000 + Slack
                    );
                });
            }
        }

        [Fact]
        public void Csrandom_WithANumberOutOfRange_AsksAgain_AndWithTwoCornersOnALine_AsksForTheOppositeCornerAgain()
        {
            CadCommandSession session = Start(out CadDocument document, out CadPluginManager manager);

            using (manager)
            {
                session.Feed("CSRANDOM");
                session.Feed("0");
                Assert.Equal("Number of objects <50>:", session.Prompt.Text);
                session.Feed("10001");
                Assert.Equal("Number of objects <50>:", session.Prompt.Text);
                session.Feed("5");
                session.Feed("0,0");
                session.Feed("100,0");
                Assert.Equal("Specify opposite corner:", session.Prompt.Text);
                session.Feed("100,80");

                Assert.Equal(
                    new[] { "Requires an integer between 1 and 10000.", "Requires an integer between 1 and 10000." },
                    session
                        .History.Where(l => l.StartsWith("Requires", StringComparison.Ordinal))
                        .Select(l => l.TrimEnd())
                );
                Assert.Contains("The two corners must differ in x and in y.", session.History.Select(l => l.TrimEnd()));
                Assert.Equal(5, Drawn(document).Count);
            }
        }

        [Fact]
        public void Csrandom_WithTheSeedVariableSet_DrawsTheSameObjectsEveryTime()
        {
            CadCommandSession session = Start(out CadDocument document, out CadPluginManager manager);

            using (manager)
            {
                Environment.SetEnvironmentVariable(SeedVariable, "42");
                string first = Run(session, document);
                string second = Run(session, Fresh(session));

                Assert.Equal(first, second);
                Assert.NotEmpty(first);

                Environment.SetEnvironmentVariable(SeedVariable, "43");
                Assert.NotEqual(first, Run(session, Fresh(session)));
            }
        }

        /// <summary>The drawing of the sample test, opened again in a session that has the same commands.</summary>
        private static CadDocument Fresh(CadCommandSession like)
        {
            CadDocument document = PluginFiles.Circles();
            document.Database.Extmin = new GeoPoint3(-10, -40, 0);
            document.Database.Extmax = new GeoPoint3(110, 30, 0);
            return document;
        }

        /// <summary>Runs CSRANDOM for 30 objects and the extents on a drawing and gives what it drew as text: type, layer, colour and extents of each.</summary>
        private static string Run(CadCommandSession like, CadDocument document)
        {
            CadCommandSession session = PluginFiles.Session(document, like.Registry);
            session.Feed("CSRANDOM");
            session.Feed("30");
            session.Enter();

            Assert.True(
                session.History.Last().StartsWith("Drew 30", StringComparison.Ordinal),
                string.Join(" / ", session.History)
            );
            return string.Join(
                "|",
                Drawn(document)
                    .Select(e => e.GetType().Name + " " + e.Layer + " " + e.ColorIndex + " " + e.GeometricExtents)
            );
        }
    }
}
#endif
