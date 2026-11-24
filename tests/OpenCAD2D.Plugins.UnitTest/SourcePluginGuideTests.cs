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
    /// compiler, so that the guide cannot drift from what the app does.
    /// </summary>
    public sealed class SourcePluginGuideTests : IDisposable
    {
        private readonly PluginFiles _files = new PluginFiles();

        public void Dispose() => _files.Dispose();

        /// <summary>The first csharp block after the heading of the section, as the guide writes it (LF line ends).</summary>
        private static string GuidesExample()
        {
            string guide = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "Guide", "plugins.md");
            Assert.True(File.Exists(guide), guide);
            string[] lines = File.ReadAllText(guide, Encoding.UTF8).Replace("\r\n", "\n").Split('\n');

            int heading = Array.FindIndex(lines, l => l == "## A plugin as C# source");
            Assert.True(heading >= 0, "The guide has no section '## A plugin as C# source'.");

            int open = Array.FindIndex(lines, heading + 1, l => l == "```csharp");
            Assert.True(open >= 0, "The section 'A plugin as C# source' has no csharp block.");

            int close = Array.FindIndex(lines, open + 1, l => l == "```");
            Assert.True(close >= 0, "The csharp block of the section 'A plugin as C# source' is not closed.");

            return string.Join("\n", lines.Skip(open + 1).Take(close - open - 1)) + "\n";
        }

        [Fact]
        public void TheGuidesExample_SavedAsPluginsStairsCs_LoadsAtStartAndWritesSteps18()
        {
            string path = Path.Combine(_files.PluginFolder, "Stairs.cs");
            Directory.CreateDirectory(_files.PluginFolder);
            File.WriteAllText(path, GuidesExample(), new UTF8Encoding(false));

            using (CadPluginManager manager = _files.Manager())
            {
                CadPluginResult loaded = Assert.Single(manager.LoadAtStart());
                Assert.True(loaded.Succeeded, loaded.ToString());
                Assert.Equal("Loaded Stairs: STAIRS.", loaded.Message);

                var registry = CadCommandRegistry.CreateDefault();
                manager.Attach(registry);

                try
                {
                    CadCommandSession session = PluginFiles.Session(PluginFiles.Circles(), registry);
                    session.Feed("STAIRS");

                    Assert.Equal(CadCommandOutcome.Completed, session.LastOutcome);
                    Assert.Equal(new[] { "Steps: 18" }, session.History.Skip(1).Select(l => l.TrimEnd()));
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
