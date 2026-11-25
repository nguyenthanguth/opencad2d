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
    /// compiler, so that it does not drift from what the app does.
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

        [Fact]
        public void TheGuidesExample_SavedAsPluginsStairs_LoadsAtStartAndWritesSteps18()
        {
            string folder = Path.Combine(_files.PluginFolder, "Stairs");
            SourceProject.Write(folder, "StairsCommands.cs", GuidesBlock("csharp"));
            SourceProject.Write(folder, "Stairs.csproj", GuidesBlock("xml"));

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
