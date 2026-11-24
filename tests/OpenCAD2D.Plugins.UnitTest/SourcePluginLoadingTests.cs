using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Runtime.CompilerServices;
using System.Text;
using GeometryHelper.Geometry;
using OpenCAD2D.Plugins;
using Xunit;
#if NET8_0_OR_GREATER
using System.Runtime.Loader;
#endif

namespace OpenCAD2D.Plugins.UnitTest
{
    /// <summary>
    /// Plugins given as C# source loaded by <see cref="CadPluginManager"/> as a .dll is: from the Plugins folder at start,
    /// by PLUGINLOAD, again by PLUGINRELOAD; refused with a line per compile error, and on .NET Framework refused outright.
    /// </summary>
    public sealed class SourcePluginLoadingTests : IDisposable
    {
        private readonly PluginFiles _files = new PluginFiles();

        public void Dispose() => _files.Dispose();

        /// <summary>A plugin with one command STAIRS that writes how many steps of at most 175 mm a rise of 3000 mm takes.</summary>
        private static string Stairs(string message = "Steps: ") =>
            "using System.Collections.Generic;\n"
            + "using OpenCAD2D;\n"
            + "using OpenCAD2D.Plugins;\n"
            + "\n"
            + "namespace Stairs\n"
            + "{\n"
            + "    public sealed class StairsCommands\n"
            + "    {\n"
            + "        [CadCommand(\"STAIRS\")]\n"
            + "        public IEnumerable<CadPrompt> Draw(CadCommandContext c)\n"
            + "        {\n"
            + "            c.Write(\""
            + message
            + "\" + (int)System.Math.Ceiling(3000.0 / 175.0));\n"
            + "            yield break;\n"
            + "        }\n"
            + "    }\n"
            + "}\n";

        private static void Write(string path, string text)
        {
            Directory.CreateDirectory(Path.GetDirectoryName(path));
            File.WriteAllText(path, text, new UTF8Encoding(false));
        }

        private static string[] Run(CadPluginManager manager, params string[] inputs)
        {
            var registry = CadCommandRegistry.CreateDefault();
            manager.Attach(registry);

            try
            {
                CadCommandSession session = PluginFiles.Session(PluginFiles.Circles(), registry);
                foreach (string input in inputs)
                {
                    session.Feed(input);
                }

                return session.History.Skip(1).Select(l => l.TrimEnd()).ToArray();
            }
            finally
            {
                manager.Detach(registry);
            }
        }

#if NET8_0_OR_GREATER
        /// <summary>The sample Drawing info as the build of the app installs it (its .dll) or as its source, in Plugins\DrawingInfo.</summary>
        private void InstallDrawingInfo(bool asSource)
        {
            string folder = Path.Combine(_files.PluginFolder, "DrawingInfo");
            Directory.CreateDirectory(folder);
            string beside = AppDomain.CurrentDomain.BaseDirectory;

            if (asSource)
            {
                File.Copy(
                    Path.Combine(beside, "DrawingInfoSource", "DrawingInfoCommands.cs.txt"),
                    Path.Combine(folder, "DrawingInfoCommands.cs")
                );
            }
            else
            {
                File.Copy(
                    Path.Combine(beside, "DrawingInfo", "OpenCAD2D.Plugins.DrawingInfo.dll"),
                    Path.Combine(folder, "OpenCAD2D.Plugins.DrawingInfo.dll")
                );
            }
        }

        private static (string[] Commands, string[] Output) DrawingInfo(CadPluginManager manager)
        {
            CadPluginResult started = Assert.Single(manager.LoadAtStart());
            Assert.True(started.Succeeded, started.ToString());
            Assert.Equal("DrawingInfo", started.Plugin.Name);
            CadDocument document = PluginFiles.Circles();
            document.Database.Extmin = new GeoPoint3(-10, -40, 0);
            document.Database.Extmax = new GeoPoint3(110, 30, 0);
            var registry = CadCommandRegistry.CreateDefault();
            manager.Attach(registry);
            CadCommandSession session = PluginFiles.Session(document, registry);
            session.Feed("GHDWGINFO");
            Assert.Equal(CadCommandOutcome.Completed, session.LastOutcome);
            return (
                started.Plugin.Plugin.Commands.Select(c => c.Name).ToArray(),
                session.History.Skip(1).Select(l => l.TrimEnd()).ToArray()
            );
        }

        [Fact]
        public void DrawingInfo_FromItsSource_GivesTheCommandsAndOutputOfItsDll()
        {
            InstallDrawingInfo(asSource: false);
            (string[] Commands, string[] Output) built;

            using (CadPluginManager manager = _files.Manager())
            {
                built = DrawingInfo(manager);
                Assert.False(manager.Find("DrawingInfo").IsSource);
            }

            Directory.Delete(Path.Combine(_files.PluginFolder, "DrawingInfo"), true);
            InstallDrawingInfo(asSource: true);

            using (CadPluginManager manager = _files.Manager())
            {
                (string[] Commands, string[] Output) source = DrawingInfo(manager);

                Assert.True(manager.Find("DrawingInfo").IsSource);
                Assert.Equal(new[] { "GHDWGINFO" }, source.Commands);
                Assert.Equal(built.Commands, source.Commands);
                Assert.Equal(built.Output, source.Output);
                Assert.Equal("Model space: 4 objects", source.Output[1]);
            }
        }

        [Fact]
        public void ACsInThePluginsFolder_LoadsAtStartAfterTheDlls_AsAPluginOfItsOwnName()
        {
            Write(Path.Combine(_files.PluginFolder, "Stairs.cs"), Stairs());
            File.Copy(PluginFiles.BuiltSample, Path.Combine(_files.PluginFolder, PluginFiles.SampleName + ".dll"));

            using (CadPluginManager manager = _files.Manager())
            {
                manager.ConfirmUntrusted = path => throw new InvalidOperationException("Not to be asked: " + path);
                IReadOnlyList<CadPluginResult> started = manager.LoadAtStart();

                Assert.Equal(new[] { PluginFiles.SampleName, "Stairs" }, started.Select(r => r.Plugin.Name));
                Assert.All(started, r => Assert.True(r.Succeeded, r.ToString()));
                Assert.Equal("Loaded Stairs: STAIRS.", started[1].Message);
                Assert.True(started[1].Plugin.IsSource);
                Assert.True(started[1].Plugin.IsInstalled);
                Assert.Equal(new[] { "Steps: 18" }, Run(manager, "STAIRS"));
            }
        }

        [Fact]
        public void PluginLoad_OfAFolderOfTwoSources_LoadsThemAsOnePlugin()
        {
            string folder = Path.Combine(_files.Root, "dev", "Stairs");
            Write(
                Path.Combine(folder, "StairsCommands.cs"),
                Stairs().Replace("(int)System.Math.Ceiling(3000.0 / 175.0)", "Rise.Steps(3000.0)")
            );
            Write(
                Path.Combine(folder, "Rise.cs"),
                "namespace Stairs\n{\n    internal static class Rise\n    {\n        internal static int Steps(double rise) => (int)System.Math.Ceiling(rise / 175.0);\n    }\n}\n"
            );

            using (CadPluginManager manager = _files.Manager(confirm: true))
            {
                CadPluginResult result = manager.Load(folder);

                Assert.True(result.Succeeded, result.ToString());
                Assert.Equal("Stairs", result.Plugin.Name);
                Assert.Equal(folder, result.Plugin.Path);
                Assert.True(result.Plugin.IsSource);
                Assert.Equal(new[] { "Steps: 18" }, Run(manager, "STAIRS"));
            }
        }

        [Fact]
        public void ASourceThatDoesNotCompile_IsRefusedWithALinePerError_OnTheCommandLine()
        {
            var lines = new List<string>(Stairs().Split('\n'));
            lines[11] = "        x = 1;";
            string path = Path.Combine(_files.Root, "dev", "Stairs.cs");
            Write(path, string.Join("\n", lines));

            using (CadPluginManager manager = _files.Manager(confirm: true))
            {
                CadPluginResult result = manager.Load(path);
                string[] written = Run(manager, "PLUGINLOAD", path);

                Assert.False(result.Succeeded);
                Assert.Equal(
                    "Plugin Stairs was not loaded: 1 error\n"
                        + "Stairs.cs(12,9): error CS0103: The name 'x' does not exist in the current context",
                    result.Message
                );
                Assert.Null(manager.Find("Stairs"));
                Assert.Equal(
                    new[]
                    {
                        "Plugin Stairs was not loaded: 1 error",
                        "Stairs.cs(12,9): error CS0103: The name 'x' does not exist in the current context",
                    },
                    written.Skip(written.Length - 2)
                );
            }
        }

        [Fact]
        public void PluginReload_CompilesTheEditedSource()
        {
            string path = Path.Combine(_files.Root, "dev", "Stairs.cs");
            Write(path, Stairs());

            using (CadPluginManager manager = _files.Manager(confirm: true))
            {
                Assert.True(manager.Load(path).Succeeded);
                Assert.Equal(new[] { "Steps: 18" }, Run(manager, "STAIRS"));
                Write(path, Stairs("Risers: "));

                CadPluginResult reloaded = manager.Reload("Stairs");

                Assert.True(reloaded.Succeeded, reloaded.ToString());
                Assert.Equal(new[] { "Risers: 18" }, Run(manager, "STAIRS"));
            }
        }

        [Fact]
        public void ASourceFromAFolderNotTrusted_IsAskedBeforeItIsCompiled_AndRefusedOnNo()
        {
            string path = Path.Combine(_files.Root, "downloads", "Stairs.cs");
            Write(path, Stairs());
            var asked = new List<string>();

            using (CadPluginManager manager = _files.Manager())
            {
                manager.ConfirmUntrusted = p =>
                {
                    asked.Add(p);
                    return false;
                };

                CadPluginResult result = manager.Load(path);

                Assert.False(result.Succeeded);
                Assert.Equal(new[] { path }, asked);
                Assert.False(Directory.Exists(_files.CacheFolder));
            }
        }

        [Fact]
        public void UnloadingASourcePlugin_FreesItsLoadContext()
        {
            string path = Path.Combine(_files.Root, "dev", "Stairs.cs");
            Write(path, Stairs());

            using (CadPluginManager manager = _files.Manager(confirm: true))
            {
                WeakReference context = LoadAndUnload(manager, path);

                for (int i = 0; i < 10 && context.IsAlive; i++)
                {
                    GC.Collect();
                    GC.WaitForPendingFinalizers();
                }

                Assert.False(context.IsAlive);
            }
        }

        [MethodImpl(MethodImplOptions.NoInlining)]
        private static WeakReference LoadAndUnload(CadPluginManager manager, string path)
        {
            CadPluginResult result = manager.Load(path);
            Assert.True(result.Succeeded, result.ToString());
            var context = new WeakReference(AssemblyLoadContext.GetLoadContext(result.Plugin.Assemblies[0]));
            Assert.True(manager.Unload("Stairs").Succeeded);
            return context;
        }
#else
        [Fact]
        public void OnNetFramework_ASourcePlugin_IsRefused_AsItNeedsTheNet8App()
        {
            string path = Path.Combine(_files.Root, "dev", "Stairs.cs");
            Write(path, Stairs());

            using (CadPluginManager manager = _files.Manager(confirm: true))
            {
                CadPluginResult result = manager.Load(path);

                Assert.False(result.Succeeded);
                Assert.Equal("C# source plugins need the .NET 8 app; " + path + " was not loaded.", result.Message);
            }
        }
#endif
    }
}
