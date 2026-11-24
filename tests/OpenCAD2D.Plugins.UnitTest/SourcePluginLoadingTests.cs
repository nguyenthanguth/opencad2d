using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Runtime.CompilerServices;
using GeometryHelper.Geometry;
using OpenCAD2D.Plugins;
using Xunit;
#if NET8_0_OR_GREATER
using System.Runtime.Loader;
#endif

namespace OpenCAD2D.Plugins.UnitTest
{
    /// <summary>
    /// Plugins given as a folder with a .csproj loaded by <see cref="CadPluginManager"/> as a .dll is: from the Plugins
    /// folder at start, by PLUGINLOAD, again by PLUGINRELOAD; refused with a line per compile error; a lone .cs or a folder of
    /// .cs files with no .csproj refused with the way to make one a plugin; and on .NET Framework a folder with a .csproj
    /// refused outright.
    /// </summary>
    public sealed class SourcePluginLoadingTests : IDisposable
    {
        private readonly PluginFiles _files = new PluginFiles();

        public void Dispose() => _files.Dispose();

        /// <summary>The plugin Stairs as a folder below the test's own, or below the plugin folder; the full path of the folder.</summary>
        private string Stairs(string where, string message = "Steps: ", string csproj = null)
        {
            string folder = Path.Combine(where, "Stairs");
            SourceProject.Folder(
                folder,
                csproj ?? SourceProject.Csproj(),
                "StairsCommands.cs",
                SourceProject.Stairs(message)
            );
            return folder;
        }

        private string Dev => Path.Combine(_files.Root, "dev");

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

        private static string LoneLine(string file, string stem) =>
            file
            + " is a lone source file: a C# plugin is a folder with a .csproj. Put it in Plugins\\"
            + stem
            + "\\ with "
            + stem
            + ".csproj (see the plugin guide).";

        // ---- S7: a lone .cs and a folder of .cs files are no plugin, on either runtime

        [Fact]
        public void PluginLoad_OfALoneCs_IsRefusedWithTheWayToMakeItAFolder()
        {
            string path = SourceProject.Write(Dev, "Stairs.cs", SourceProject.Stairs());

            using (CadPluginManager manager = _files.Manager(confirm: true))
            {
                CadPluginResult result = manager.Load(path);
                string[] written = Run(manager, "NETLOAD", path);

                Assert.False(result.Succeeded);
                Assert.Equal(LoneLine("Stairs.cs", "Stairs"), result.Message);
                Assert.Null(manager.Find("Stairs"));
                Assert.Equal(LoneLine("Stairs.cs", "Stairs"), written[written.Length - 1]);
                Assert.False(Directory.Exists(_files.CacheFolder));
            }
        }

        [Fact]
        public void ALoneCsInThePluginsFolder_IsRefusedAtStart_AndTheOthersLoad()
        {
            SourceProject.Write(_files.PluginFolder, "Stairs.cs", SourceProject.Stairs());
            File.Copy(PluginFiles.BuiltSample, Path.Combine(_files.PluginFolder, PluginFiles.SampleName + ".dll"));

            using (CadPluginManager manager = _files.Manager())
            {
                IReadOnlyList<CadPluginResult> started = manager.LoadAtStart();

                Assert.Equal(2, started.Count);
                Assert.True(started[0].Succeeded, started[0].ToString());
                Assert.Equal(PluginFiles.SampleName, started[0].Plugin.Name);
                Assert.False(started[1].Succeeded);
                Assert.Equal(LoneLine("Stairs.cs", "Stairs"), started[1].Message);
                Assert.Contains(manager.Log, e =>
                    !e.Succeeded && e.Plugin == "Stairs" && e.Result == LoneLine("Stairs.cs", "Stairs")
                );
            }
        }

        [Fact]
        public void AFolderOfCsFilesWithNoCsproj_IsRefused_AsAFolderToGiveOne()
        {
            string folder = Path.Combine(_files.PluginFolder, "Stairs");
            SourceProject.Write(folder, "StairsCommands.cs", SourceProject.Stairs());

            using (CadPluginManager manager = _files.Manager())
            {
                CadPluginResult started = Assert.Single(manager.LoadAtStart());

                Assert.False(started.Succeeded);
                Assert.Equal(
                    "The folder "
                        + folder
                        + " holds .cs files and no .csproj: a C# plugin is a folder with a .csproj. Add Stairs.csproj to it (see the plugin guide).",
                    started.Message
                );
            }
        }

        // ---- S1: one .csproj

        [Fact]
        public void AFolderWithTwoCsproj_IsRefused_AtStartAndByPluginLoad()
        {
            string folder = Stairs(_files.PluginFolder);
            SourceProject.Write(folder, "Other.csproj", SourceProject.Csproj());
            string line = folder + " holds 2 .csproj files: a plugin folder holds one.";

            using (CadPluginManager manager = _files.Manager(confirm: true))
            {
                CadPluginResult started = Assert.Single(manager.LoadAtStart());
                CadPluginResult loaded = manager.Load(folder);

                Assert.False(started.Succeeded);
                Assert.Equal(line, started.Message);
                Assert.Equal(line, loaded.Message);
                Assert.Empty(manager.Plugins);
            }
        }

        [Fact]
        public void AFolderWithACsproj_IsASourcePlugin_WhateverDllLiesBesideIt()
        {
            string folder = Stairs(Dev);
            File.Copy(PluginFiles.BuiltSample, Path.Combine(folder, PluginFiles.SampleName + ".dll"));

#if NET8_0_OR_GREATER
            using (CadPluginManager manager = _files.Manager(confirm: true))
            {
                CadPluginResult result = manager.Load(folder);

                Assert.True(result.Succeeded, result.ToString());
                Assert.Equal("Stairs", result.Plugin.Name);
                Assert.True(result.Plugin.IsSource);
                Assert.Equal(new[] { "Steps: 18" }, Run(manager, "STAIRS"));
                Assert.Equal(new[] { "Stairs" }, manager.Plugins.Select(p => p.Name));
            }
#else
            using (CadPluginManager manager = _files.Manager(confirm: true))
            {
                CadPluginResult result = manager.Load(folder);

                Assert.False(result.Succeeded);
                Assert.Equal("C# source plugins need the .NET 8 app; " + folder + " was not loaded.", result.Message);
            }
#endif
        }

#if NET8_0_OR_GREATER
        /// <summary>The sample Drawing info as the build of the app installs it (its .dll) or as its source and a .csproj, in Plugins\DrawingInfo.</summary>
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
                SourceProject.Write(folder, "DrawingInfo.csproj", SourceProject.Csproj());
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
        public void AFolderWithACsprojInThePluginsFolder_LoadsAtStartAfterTheDlls_AsAPluginOfItsOwnName()
        {
            Stairs(_files.PluginFolder);
            File.Copy(PluginFiles.BuiltSample, Path.Combine(_files.PluginFolder, PluginFiles.SampleName + ".dll"));

            using (CadPluginManager manager = _files.Manager())
            {
                manager.ConfirmUntrusted = path => throw new InvalidOperationException("Not to be asked: " + path);
                IReadOnlyList<CadPluginResult> started = manager.LoadAtStart();

                Assert.Equal(new[] { PluginFiles.SampleName, "Stairs" }, started.Select(r => r.Plugin.Name));
                Assert.All(started, r => Assert.True(r.Succeeded, r.ToString()));
                Assert.Equal("Loaded Stairs 1.0.0: STAIRS.", started[1].Message);
                Assert.True(started[1].Plugin.IsSource);
                Assert.True(started[1].Plugin.IsInstalled);
                Assert.Equal(new[] { "Steps: 18" }, Run(manager, "STAIRS"));
            }
        }

        [Fact]
        public void ACsprojWithNoSourceBesideIt_AndItsSourcesBelow_LoadsFromAnyDepthOfThePluginsFolder()
        {
            string folder = Path.Combine(_files.PluginFolder, "Company", "Flights", "Stairs");
            SourceProject.Folder(folder, SourceProject.Csproj(), "Src/StairsCommands.cs", SourceProject.Stairs());

            using (CadPluginManager manager = _files.Manager())
            {
                CadPluginResult started = Assert.Single(manager.LoadAtStart());

                Assert.True(started.Succeeded, started.ToString());
                Assert.Equal("Stairs", started.Plugin.Name);
                Assert.Equal(folder, started.Plugin.Path);
                Assert.Equal(new[] { "Steps: 18" }, Run(manager, "STAIRS"));
            }
        }

        [Fact]
        public void PluginLoad_OfTheCsprojItself_LoadsTheFolder_WithTheSourcesOfSeveralFolders()
        {
            string folder = Path.Combine(Dev, "Stairs");
            string project = SourceProject.Folder(
                folder,
                SourceProject.Csproj(),
                "StairsCommands.cs",
                SourceProject.Command(),
                "Geometry/Steps.cs",
                SourceProject.StepsSource
            );

            using (CadPluginManager manager = _files.Manager(confirm: true))
            {
                CadPluginResult result = manager.Load(project);

                Assert.True(result.Succeeded, result.ToString());
                Assert.Equal("Stairs", result.Plugin.Name);
                Assert.Equal(folder, result.Plugin.Path);
                Assert.True(result.Plugin.IsSource);
                Assert.Equal(new[] { "Steps: 18" }, Run(manager, "STAIRS"));
            }
        }

        [Fact]
        public void ALibraryOfAHintPathInASubfolder_IsFoundWhenThePluginRuns()
        {
            // The library: Stairs.Steps built from its source by the same compiler, then put in libs\ of the plugin.
            string helper = SourceProject.Folder(
                Path.Combine(_files.Root, "helper"),
                SourceProject.Csproj(new[] { "<AssemblyName>StairsMath</AssemblyName>" }),
                "Steps.cs",
                SourceProject.StepsSource
            );
            var built = new OpenCAD2D.Wpf.Plugins.CadPluginCompiler(_files.CacheFolder).Compile("StairsMath", helper);
            Assert.True(built.Succeeded, string.Join("\n", built.Errors));
            string folder = Path.Combine(Dev, "Stairs");
            SourceProject.Folder(
                folder,
                SourceProject.Csproj(
                    items: new[] { "<Reference Include=\"StairsMath\" HintPath=\"libs\\StairsMath.dll\" />" }
                ),
                "StairsCommands.cs",
                SourceProject.Command()
            );
            Directory.CreateDirectory(Path.Combine(folder, "libs"));
            File.Copy(built.AssemblyPath, Path.Combine(folder, "libs", "StairsMath.dll"));

            using (CadPluginManager manager = _files.Manager(confirm: true))
            {
                CadPluginResult result = manager.Load(folder);

                Assert.True(result.Succeeded, result.ToString());
                Assert.Equal(new[] { "Steps: 18" }, Run(manager, "STAIRS"));
            }
        }

        [Fact]
        public void WhatTheProjectDoesNotSay_IsAWarningInTheLineOfTheLoad_AndTheLog()
        {
            string folder = Stairs(
                Dev,
                csproj: SourceProject.Csproj(
                    new[] { "<TargetFramework Condition=\"'$(X)' == ''\">net8.0</TargetFramework>" }
                )
            );
            SourceProject.Write(folder, "Directory.Build.props", "<Project />\n");

            using (CadPluginManager manager = _files.Manager(confirm: true))
            {
                CadPluginResult result = manager.Load(folder);

                Assert.True(result.Succeeded, result.ToString());
                Assert.Equal(
                    new[]
                    {
                        "Stairs.csproj: <TargetFramework Condition=\"'$(X)' == ''\"> was not read (conditions are not evaluated).",
                        "Stairs.csproj: Directory.Build.props is not read.",
                    },
                    result.Warnings
                );
                Assert.StartsWith(
                    "Loaded Stairs 1.0.0: STAIRS. Stairs.csproj: <TargetFramework Condition=",
                    manager.Log.Last().Result
                );
            }
        }

        [Fact]
        public void ASourceThatDoesNotCompile_IsRefusedWithALinePerError_OnTheCommandLine()
        {
            var lines = new List<string>(SourceProject.Stairs().Split('\n'));
            lines[11] = "        x = 1;";
            string folder = Path.Combine(Dev, "Stairs");
            SourceProject.Folder(folder, SourceProject.Csproj(), "Stairs.cs", string.Join("\n", lines));

            using (CadPluginManager manager = _files.Manager(confirm: true))
            {
                CadPluginResult result = manager.Load(folder);
                string[] written = Run(manager, "PLUGINLOAD", folder);

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
        public void AProjectThatIsRefused_GivesItsErrorLine_AndNothingIsCompiled()
        {
            string folder = Stairs(
                Dev,
                csproj: SourceProject.Csproj(items: new[] { "<ProjectReference Include=\"..\\Lib\\Lib.csproj\" />" })
            );

            using (CadPluginManager manager = _files.Manager(confirm: true))
            {
                CadPluginResult result = manager.Load(folder);

                Assert.False(result.Succeeded);
                Assert.Equal(
                    "Plugin Stairs was not loaded: 1 error\n"
                        + "Stairs.csproj(6,10): error OC0002: <ProjectReference> is not read: a plugin is built from its own folder alone.",
                    result.Message
                );
                Assert.False(Directory.Exists(_files.CacheFolder));
            }
        }

        [Fact]
        public void PluginReload_CompilesTheEditedSource_AndTheEditedCsproj()
        {
            string folder = Stairs(Dev);

            using (CadPluginManager manager = _files.Manager(confirm: true))
            {
                Assert.True(manager.Load(folder).Succeeded);
                Assert.Equal(new[] { "Steps: 18" }, Run(manager, "STAIRS"));
                SourceProject.Write(folder, "StairsCommands.cs", SourceProject.Stairs("Risers: "));

                CadPluginResult reloaded = manager.Reload("Stairs");

                Assert.True(reloaded.Succeeded, reloaded.ToString());
                Assert.Equal(new[] { "Risers: 18" }, Run(manager, "STAIRS"));

                // The same source, one other symbol in the project: a build of its own.
                SourceProject.Write(
                    folder,
                    "Stairs.csproj",
                    SourceProject.Csproj(new[] { "<AssemblyName>StairsTwo</AssemblyName>" })
                );
                CadPluginResult again = manager.Reload("Stairs");

                Assert.True(again.Succeeded, again.ToString());
                Assert.Equal("StairsTwo", again.Plugin.Assemblies[0].GetName().Name);
                Assert.Equal(new[] { "Risers: 18" }, Run(manager, "STAIRS"));
            }
        }

        [Fact]
        public void ASourceFromAFolderNotTrusted_IsAskedBeforeItIsCompiled_AndRefusedOnNo()
        {
            string folder = Stairs(Path.Combine(_files.Root, "downloads"));
            var asked = new List<string>();

            using (CadPluginManager manager = _files.Manager())
            {
                manager.ConfirmUntrusted = p =>
                {
                    asked.Add(p);
                    return false;
                };

                CadPluginResult result = manager.Load(Path.Combine(folder, "Stairs.csproj"));

                Assert.False(result.Succeeded);
                Assert.Equal(new[] { folder }, asked);
                Assert.False(Directory.Exists(_files.CacheFolder));
            }
        }

        [Fact]
        public void UnloadingASourcePlugin_FreesItsLoadContext()
        {
            string folder = Stairs(Dev);

            using (CadPluginManager manager = _files.Manager(confirm: true))
            {
                WeakReference context = LoadAndUnload(manager, folder);

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
#endif
    }
}
