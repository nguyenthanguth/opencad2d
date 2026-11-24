#if NET8_0_OR_GREATER
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using OpenCAD2D.Plugins;
using Xunit;

namespace OpenCAD2D.Plugins.UnitTest
{
    /// <summary>
    /// The example of the plugin guide, "A plugin as C# source", read out of the guide itself and loaded with the real
    /// compiler, and the sample the app ships as Plugins\Stairs, so that neither drifts from what the app does.
    /// </summary>
    public sealed class SourcePluginGuideTests : IDisposable
    {
        private readonly PluginFiles _files = new PluginFiles();

        public void Dispose() => _files.Dispose();

        /// <summary>The first block of a language after the heading of the section, as the guide writes it (LF line ends).</summary>
        private static string GuidesBlock(string language)
        {
            string guide = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "Guide", "plugins.md");
            Assert.True(File.Exists(guide), guide);
            string[] lines = File.ReadAllText(guide, Encoding.UTF8).Replace("\r\n", "\n").Split('\n');

            int heading = Array.FindIndex(lines, l => l == "## A plugin as C# source");
            Assert.True(heading >= 0, "The guide has no section '## A plugin as C# source'.");

            int open = Array.FindIndex(lines, heading + 1, l => l == "```" + language);
            Assert.True(open >= 0, "The section 'A plugin as C# source' has no " + language + " block.");

            int close = Array.FindIndex(lines, open + 1, l => l == "```");
            Assert.True(close >= 0, "The " + language + " block of the section 'A plugin as C# source' is not closed.");

            return string.Join("\n", lines.Skip(open + 1).Take(close - open - 1)) + "\n";
        }

        /// <summary>A file of the sample as the build of the tests left it beside them.</summary>
        private static string Sample(string file) =>
            Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "Stairs", file);

        [Fact]
        public void TheGuidesExample_IsTheSampleTheAppShipsAsPluginsStairs_ByteForByte()
        {
            // examples/Stairs, which the build of OpenCAD2D.App copies to Plugins\Stairs.
            Assert.True(File.Exists(Sample("StairsCommands.cs.txt")), Sample("StairsCommands.cs.txt"));
            Assert.True(File.Exists(Sample("Stairs.csproj.txt")), Sample("Stairs.csproj.txt"));

            Assert.Equal(
                new UTF8Encoding(false).GetBytes(GuidesBlock("csharp")),
                File.ReadAllBytes(Sample("StairsCommands.cs.txt"))
            );
            Assert.Equal(
                new UTF8Encoding(false).GetBytes(GuidesBlock("xml")),
                File.ReadAllBytes(Sample("Stairs.csproj.txt"))
            );
        }

        [Fact]
        public void TheGuidesExample_SavedAsPluginsStairs_LoadsAtStartAndWritesSteps18()
        {
            string folder = Path.Combine(_files.PluginFolder, "Stairs");
            SourceProject.Write(folder, "StairsCommands.cs", GuidesBlock("csharp"));
            SourceProject.Write(folder, "Stairs.csproj", GuidesBlock("xml"));

            Assert.Equal(new[] { "Steps: 18" }, LoadAtStartAndRun());
        }

        [Fact]
        public void TheSampleOfTheApp_FromItsFolder_LoadsAtStartAndWritesSteps18_WithTheCommandsItHadAsOneFile()
        {
            string folder = Path.Combine(_files.PluginFolder, "Stairs");
            Directory.CreateDirectory(folder);
            File.Copy(Sample("StairsCommands.cs.txt"), Path.Combine(folder, "StairsCommands.cs"));
            File.Copy(Sample("Stairs.csproj.txt"), Path.Combine(folder, "Stairs.csproj"));

            Assert.Equal(new[] { "Steps: 18" }, LoadAtStartAndRun());
        }

        private string[] LoadAtStartAndRun()
        {
            using (CadPluginManager manager = _files.Manager())
            {
                CadPluginResult loaded = Assert.Single(manager.LoadAtStart());
                Assert.True(loaded.Succeeded, loaded.ToString());
                Assert.Equal("Loaded Stairs 1.0.0: STAIRS.", loaded.Message);
                Assert.Empty(loaded.Warnings);

                var registry = CadCommandRegistry.CreateDefault();
                manager.Attach(registry);

                try
                {
                    CadCommandSession session = PluginFiles.Session(PluginFiles.Circles(), registry);
                    session.Feed("STAIRS");

                    Assert.Equal(CadCommandOutcome.Completed, session.LastOutcome);
                    return session.History.Skip(1).Select(l => l.TrimEnd()).ToArray();
                }
                finally
                {
                    manager.Detach(registry);
                }
            }
        }
    }
}
#endif
