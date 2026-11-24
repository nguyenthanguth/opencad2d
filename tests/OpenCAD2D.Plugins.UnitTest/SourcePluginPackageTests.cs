#if NET8_0_OR_GREATER
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using OpenCAD2D.Wpf.Plugins;
using Xunit;

namespace OpenCAD2D.Plugins.UnitTest
{
    /// <summary>
    /// The NuGet packages a plugin's .csproj names (TF3): restored from a folder of .nupkg made in code, never the network,
    /// into a packages folder of the test's own; the choice of versions as restore makes it, the question before a
    /// download, the assets chosen for .NET 8, native files, two plugins with two versions of one package, and the list kept
    /// so that a second load asks nothing of the source.
    /// </summary>
    public sealed class SourcePluginPackageTests : IDisposable
    {
        private readonly PluginFiles _files = new PluginFiles();
        private readonly SourcePluginFeed _feed;
        private readonly List<IReadOnlyList<CadPackage>> _asked = new List<IReadOnlyList<CadPackage>>();

        public SourcePluginPackageTests()
        {
            _feed = new SourcePluginFeed(Path.Combine(_files.Root, "feed"));
        }

        public void Dispose() => _files.Dispose();

        private string Packages => Path.Combine(_files.Root, "packages");

        private string Cache => _files.CacheFolder;

        private static string Ref(string id, string version) =>
            "<PackageReference Include=\"" + id + "\" Version=\"" + version + "\" />";

        /// <summary>A plugin source with one command that writes what the expression gives.</summary>
        private static string Source(string command, string expression) =>
            "using System.Collections.Generic;\nusing OpenCAD2D;\nusing OpenCAD2D.Plugins;\n"
            + "namespace P { public sealed class C { [CadCommand(\""
            + command
            + "\")] public IEnumerable<CadPrompt> Run(CadCommandContext c) { c.Write("
            + expression
            + "); yield break; } } }\n";

        /// <summary>A plugin folder below the test's own with the project's items and one source; the full path of the .csproj.</summary>
        private string Project(string name, string[] items, string command = "STAIRS", string expression = "\"ok\"")
        {
            string folder = Path.Combine(_files.Root, "dev", name);
            SourceProject.Write(folder, "Commands.cs", Source(command, expression));
            return SourceProject.Write(folder, name + ".csproj", SourceProject.Csproj(items: items));
        }

        private CadPackageRestorer Restorer(string source = null) =>
            new CadPackageRestorer(source ?? _feed.Folder, Packages);

        /// <summary>A compiler on the test's feed, asking is answered as given (null: no hook set).</summary>
        private CadPluginCompiler Compiler(bool? answer = true, string source = null)
        {
            var compiler = new CadPluginCompiler(Cache, Restorer(source));

            if (answer != null)
            {
                compiler.ConfirmDownload = (name, packages) =>
                {
                    _asked.Add(packages);
                    return answer.Value;
                };
            }

            return compiler;
        }

        private CadPluginManager Manager(bool? answer = true)
        {
            var loader = new CadPluginAssemblyLoader(Cache, Restorer());

            if (answer != null)
            {
                loader.ConfirmDownload = (name, packages) =>
                {
                    _asked.Add(packages);
                    return answer.Value;
                };
            }

            var manager = new CadPluginManager(loader, _files.PluginFolder, _files.SettingsPath, _files.ShadowFolder);
            manager.ConfirmUntrusted = path => true;
            return manager;
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

                return session.History.Skip(1).Select(l => l.TrimEnd()).Where(l => !l.StartsWith("Command:")).ToArray();
            }
            finally
            {
                manager.Detach(registry);
            }
        }

        private static string[] Listed(IReadOnlyList<CadPackage> packages) =>
            packages
                .Select(p =>
                    p.ToString() + (p.IsDirect ? " direct" : string.Empty) + (p.IsCached ? " cached" : string.Empty)
                )
                .ToArray();

        private string[] Extracted() =>
            !Directory.Exists(Packages)
                ? new string[0]
                : Directory
                    .GetDirectories(Packages)
                    .SelectMany(id => Directory.GetDirectories(id))
                    .Where(v =>
                        File.Exists(
                            Path.Combine(
                                v,
                                Path.GetFileName(Path.GetDirectoryName(v)) + "." + Path.GetFileName(v) + ".nupkg.sha512"
                            )
                        )
                    )
                    .Select(v => Path.GetFileName(Path.GetDirectoryName(v)) + " " + Path.GetFileName(v))
                    .OrderBy(s => s, StringComparer.Ordinal)
                    .ToArray();

        // ---- S13: the version restore chooses

        [Fact]
        public void ANumberAsVersion_InstallsTheLowestVersionThatMeetsIt_NeverTheHighest()
        {
            _feed.AddLibrary("Foo", "1.0.0", "1.0");
            _feed.AddLibrary("Foo", "1.5.0", "1.5");
            _feed.AddLibrary("Foo", "2.0.0", "2.0");

            CadPluginBuild exact = Compiler().Compile("Exact", Project("Exact", new[] { Ref("Foo", "1.0.0") }));
            CadPluginBuild between = Compiler().Compile("Between", Project("Between", new[] { Ref("Foo", "1.2.0") }));

            Assert.True(exact.Succeeded, string.Join("\n", exact.Errors));
            Assert.True(between.Succeeded, string.Join("\n", between.Errors));
            Assert.Equal(new[] { "Foo 1.0.0 direct" }, Listed(_asked[0]));
            Assert.Equal(new[] { "Foo 1.5.0 direct" }, Listed(_asked[1]));
            Assert.Equal(new[] { "foo 1.0.0", "foo 1.5.0" }, Extracted());
        }

        [Fact]
        public void TheQuestion_ListsTheDirectPackagesFirst_ThenTheRest_EachInOrdinalOrder_AndMarksTheCachedOnes()
        {
            _feed.AddLibrary("Zed", "1.0.0", "z", needs: SourcePluginFeed.Dep("Aaa", "1.0.0"));
            _feed.AddLibrary("Bar", "1.0.0", "b", needs: SourcePluginFeed.Dep("Foo", "1.0.0"));
            _feed.AddLibrary("Aaa", "1.0.0", "a");
            _feed.AddLibrary("Foo", "1.0.0", "f");

            Compiler().Compile("Stairs", Project("Stairs", new[] { Ref("Zed", "1.0.0"), Ref("Bar", "1.0.0") }));
            Compiler().Compile("Other", Project("Other", new[] { Ref("Foo", "1.0.0") }));

            Assert.Equal(new[] { "Bar 1.0.0 direct", "Zed 1.0.0 direct", "Aaa 1.0.0", "Foo 1.0.0" }, Listed(_asked[0]));
            Assert.Single(_asked);
        }

        [Fact]
        public void Cousins_TakeTheHigherOfTheirLowestVersions()
        {
            _feed.AddLibrary("Foo", "1.0.0", "1.0");
            _feed.AddLibrary("Foo", "1.2.0", "1.2");
            _feed.AddLibrary("Foo", "1.4.0", "1.4");
            _feed.AddLibrary("A", "1.0.0", "a", needs: SourcePluginFeed.Dep("Foo", "1.0.0"));
            _feed.AddLibrary("B", "1.0.0", "b", needs: SourcePluginFeed.Dep("Foo", "1.2.0"));

            CadPluginBuild build = Compiler()
                .Compile("Stairs", Project("Stairs", new[] { Ref("A", "1.0.0"), Ref("B", "1.0.0") }));

            Assert.True(build.Succeeded, string.Join("\n", build.Errors));
            Assert.Equal(new[] { "A 1.0.0 direct", "B 1.0.0 direct", "Foo 1.2.0" }, Listed(_asked[0]));
            Assert.Equal(new[] { "a 1.0.0", "b 1.0.0", "foo 1.2.0" }, Extracted());
        }

        [Fact]
        public void ARangeNoVersionMeets_IsRefused_NamingWhatEachNeeds_AndNothingIsAskedOrDownloaded()
        {
            _feed.AddLibrary("Foo", "1.0.0", "1.0");
            _feed.AddLibrary("Foo", "2.0.0", "2.0");
            _feed.AddLibrary("Bar", "1.0.0", "b", needs: SourcePluginFeed.Dep("Foo", "[1.0.0, 2.0.0)"));

            string project = Project("Stairs", new[] { Ref("Foo", "[2.0.0, 3.0.0)"), Ref("Bar", "1.0.0") });
            CadPluginBuild build = Compiler().Compile("Stairs", project);

            Assert.False(build.Succeeded);
            Assert.Equal(
                new[]
                {
                    "Stairs.csproj(6,10): error NU1107: Stairs needs Foo [2.0.0, 3.0.0); Bar 1.0.0 needs Foo [1.0.0, 2.0.0); no version meets both.",
                },
                build.Errors.Select(e => e.Replace("Stairs.csproj", "Stairs.csproj")).ToArray()
            );
            Assert.Empty(_asked);
            Assert.False(Directory.Exists(Packages));
        }

        [Fact]
        public void AVersionTheSourceLacks_AndAPackageItLacks_AreRefused()
        {
            _feed.AddLibrary("Foo", "1.0.0", "1.0");

            CadPluginBuild version = Compiler().Compile("Stairs", Project("Stairs", new[] { Ref("Foo", "5.0.0") }));
            CadPluginBuild missing = Compiler().Compile("Other", Project("Other", new[] { Ref("Nothing", "1.0.0") }));

            Assert.False(version.Succeeded);
            Assert.StartsWith(
                "Stairs.csproj(6,10): error NU1102: Stairs needs Foo 5.0.0 or higher;",
                version.Errors[0]
            );
            Assert.Contains("the latest is 1.0.0", version.Errors[0]);
            Assert.False(missing.Succeeded);
            Assert.StartsWith(
                "Other.csproj(6,10): error NU1101: Other needs Nothing 1.0.0 or higher;",
                missing.Errors[0]
            );
            Assert.Empty(_asked);
        }

        [Fact]
        public void ADirectReferenceLowerThanWhatAnotherPackageNeeds_IsRefused_AsTheSdkRefusesIt()
        {
            _feed.AddLibrary("Foo", "1.0.0", "1.0");
            _feed.AddLibrary("Foo", "1.2.0", "1.2");
            _feed.AddLibrary("Bar", "2.0.0", "b", needs: SourcePluginFeed.Dep("Foo", "1.2.0"));

            CadPluginBuild build = Compiler()
                .Compile("Stairs", Project("Stairs", new[] { Ref("Foo", "1.0.0"), Ref("Bar", "2.0.0") }));

            Assert.False(build.Succeeded);
            Assert.Equal(
                new[]
                {
                    "Stairs.csproj(6,10): error NU1605: Stairs asks Foo 1.0.0, and Bar 2.0.0 needs Foo 1.2.0 or higher.",
                },
                build.Errors
            );
            Assert.Empty(_asked);
        }

        // ---- S14: the assets

        [Fact]
        public void TheNearestFramework_IsTheAssetUsed_Net6OverNetStandard2()
        {
            _feed.Add(
                "Foo",
                "1.0.0",
                new[]
                {
                    (
                        "lib/netstandard2.0/Foo.dll",
                        SourcePluginFeed.Library("Foo", "1.0.0", SourcePluginFeed.Class("Foo", "standard"))
                    ),
                    (
                        "lib/net6.0/Foo.dll",
                        SourcePluginFeed.Library("Foo", "1.0.0", SourcePluginFeed.Class("Foo", "net6"))
                    ),
                    (
                        "lib/net48/Foo.dll",
                        SourcePluginFeed.Library("Foo", "1.0.0", SourcePluginFeed.Class("Foo", "net48"))
                    ),
                }
            );
            string project = Project("Stairs", new[] { Ref("Foo", "1.0.0") }, expression: "\"Foo \" + Foo.Foo.Version");

            using (CadPluginManager manager = Manager())
            {
                CadPluginResult result = manager.Load(Path.GetDirectoryName(project));

                Assert.True(result.Succeeded, result.ToString());
                Assert.Equal(new[] { "Foo net6" }, Run(manager, "STAIRS"));
            }
        }

        [Fact]
        public void APackageWithNoAssetForNet8_IsRefused_NamingWhatItHas()
        {
            _feed.Add(
                "Foo",
                "1.0.0",
                new[]
                {
                    (
                        "lib/net48/Foo.dll",
                        SourcePluginFeed.Library("Foo", "1.0.0", SourcePluginFeed.Class("Foo", "48"))
                    ),
                }
            );

            CadPluginBuild build = Compiler().Compile("Stairs", Project("Stairs", new[] { Ref("Foo", "1.0.0") }));

            Assert.False(build.Succeeded);
            Assert.Equal(
                new[]
                {
                    "Stairs.csproj(6,10): error NU1202: Foo 1.0.0 has nothing for net8.0-windows7.0 (it has net48).",
                },
                build.Errors
            );
        }

        // ---- S19: native files

        /// <summary>A library whose Size(path) is the size of a file's version information, asked of the native file "foo".</summary>
        private static string SizeLibrary(string id) =>
            "using System.Runtime.InteropServices;\nnamespace "
            + id
            + " { public static class "
            + id
            + " { [DllImport(\"foo\", EntryPoint = \"GetFileVersionInfoSizeW\", CharSet = CharSet.Unicode)] static extern uint Call(string path, out uint handle); "
            + "public static uint Size(string path) { uint handle; return Call(path, out handle); } } }\n";

        /// <summary>A library whose Clock() is the milliseconds the system has run, asked of the native file "foo".</summary>
        private static string ClockLibrary(string id) =>
            "using System.Runtime.InteropServices;\nnamespace "
            + id
            + " { public static class "
            + id
            + " { [DllImport(\"foo\", EntryPoint = \"timeGetTime\")] static extern uint Call(); public static uint Clock() => Call(); } }\n";

        [Fact]
        public void ANativeFile_IsLoadedByItsFullPathInThePackagesFolder_WhenTheLibraryAsksForItByName()
        {
            string size = SizeLibrary("Foo");
            _feed.Add(
                "Foo",
                "1.0.0",
                new[]
                {
                    ("lib/netstandard2.0/Foo.dll", SourcePluginFeed.Library("Foo", "1.0.0", size)),
                    ("runtimes/win-x64/native/foo.dll", SourcePluginFeed.Native("version.dll")),
                }
            );
            string kernel = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.System), "kernel32.dll")
                .Replace("\\", "\\\\");
            string project = Project(
                "Stairs",
                new[] { Ref("Foo", "1.0.0") },
                expression: "\"size \" + (Foo.Foo.Size(\"" + kernel + "\") > 0)"
            );

            using (CadPluginManager manager = Manager())
            {
                CadPluginResult result = manager.Load(Path.GetDirectoryName(project));

                Assert.True(result.Succeeded, result.ToString());
                Assert.Equal(new[] { "size True" }, Run(manager, "STAIRS"));

                string loaded = Process
                    .GetCurrentProcess()
                    .Modules.Cast<ProcessModule>()
                    .Select(m => m.FileName)
                    .Single(f =>
                        f.StartsWith(Packages, StringComparison.OrdinalIgnoreCase)
                        && f.EndsWith(Path.Combine("native", "foo.dll"), StringComparison.OrdinalIgnoreCase)
                    );
                Assert.Equal(
                    Path.Combine(Packages, "foo", "1.0.0", "runtimes", "win-x64", "native", "foo.dll"),
                    loaded,
                    ignoreCase: true
                );
            }
        }

        [Fact]
        public void TwoPluginsWithTwoNativeFilesOfOneName_LoadSideBySide_EachCallingItsOwn()
        {
            _feed.Add(
                "NatA",
                "1.0.0",
                new[]
                {
                    ("lib/netstandard2.0/NatA.dll", SourcePluginFeed.Library("NatA", "1.0.0", SizeLibrary("NatA"))),
                    ("runtimes/win-x64/native/foo.dll", SourcePluginFeed.Native("version.dll")),
                }
            );
            _feed.Add(
                "NatB",
                "1.0.0",
                new[]
                {
                    ("lib/netstandard2.0/NatB.dll", SourcePluginFeed.Library("NatB", "1.0.0", ClockLibrary("NatB"))),
                    ("runtimes/win-x64/native/foo.dll", SourcePluginFeed.Native("winmm.dll")),
                }
            );
            string a = Project(
                "PluginA",
                new[] { Ref("NatA", "1.0.0") },
                "SIZEA",
                "\"a \" + (NatA.NatA.Size(\"C:\\\\Windows\\\\System32\\\\kernel32.dll\") > 0)"
            );
            string b = Project("PluginB", new[] { Ref("NatB", "1.0.0") }, "CLOCKB", "\"b \" + (NatB.NatB.Clock() > 0)");

            using (CadPluginManager manager = Manager())
            {
                Assert.True(manager.Load(Path.GetDirectoryName(a)).Succeeded);
                Assert.True(manager.Load(Path.GetDirectoryName(b)).Succeeded);
                Assert.Equal(new[] { "a True", "b True" }, Run(manager, "SIZEA", "CLOCKB"));
            }
        }

        // ---- S18: two plugins, two versions

        [Fact]
        public void TwoPluginsWithTwoVersionsOfOnePackage_BothRun_EachWithItsOwn()
        {
            _feed.AddLibrary("Foo", "1.0.0", "one");
            _feed.AddLibrary("Foo", "2.0.0", "two");
            string one = Project("PluginOne", new[] { Ref("Foo", "1.0.0") }, "ONE", "\"Foo \" + Foo.Foo.Version");
            string two = Project("PluginTwo", new[] { Ref("Foo", "2.0.0") }, "TWO", "\"Foo \" + Foo.Foo.Version");

            using (CadPluginManager manager = Manager())
            {
                Assert.True(manager.Load(Path.GetDirectoryName(one)).Succeeded);
                Assert.True(manager.Load(Path.GetDirectoryName(two)).Succeeded);
                Assert.Equal(new[] { "Foo one", "Foo two" }, Run(manager, "ONE", "TWO"));
                Assert.Equal(new[] { "foo 1.0.0", "foo 2.0.0" }, Extracted());
            }
        }

        [Fact]
        public void APackageFirstUsedAfterACollection_IsStillLoadedIntoItsPlugin()
        {
            // A package's library is loaded when the command first runs, which may be long after the plugin was loaded; a
            // collection in between must not take the plugin's load context, which nothing but the loader holds.
            _feed.AddLibrary("Foo", "1.0.0", "one");
            string project = Project("PluginOne", new[] { Ref("Foo", "1.0.0") }, "ONE", "\"Foo \" + Foo.Foo.Version");

            using (CadPluginManager manager = Manager())
            {
                Assert.True(manager.Load(Path.GetDirectoryName(project)).Succeeded);

                for (int i = 0; i < 3; i++)
                {
                    GC.Collect();
                    GC.WaitForPendingFinalizers();
                }

                Assert.Equal(new[] { "Foo one" }, Run(manager, "ONE"));
            }
        }

        [Fact]
        public void APackageAskingAHigherVersionOfWhatTheAppCarries_RunsOnTheAppsCopy_WithALine()
        {
            string json =
                "namespace System.Text.Json { public static class Marker { public static string Version => \"package\"; } }\n";
            _feed.Add(
                "System.Text.Json",
                "99.0.0",
                new[]
                {
                    ("lib/net8.0/System.Text.Json.dll", SourcePluginFeed.Library("System.Text.Json", "99.0.0", json)),
                }
            );
            string project = Project(
                "Stairs",
                new[] { Ref("System.Text.Json", "99.0.0") },
                expression: "System.Text.Json.JsonSerializer.Serialize(5)"
            );

            using (CadPluginManager manager = Manager())
            {
                CadPluginResult result = manager.Load(Path.GetDirectoryName(project));
                string app = typeof(System.Text.Json.JsonSerializer).Assembly.GetName().Version.ToString(3);

                Assert.True(result.Succeeded, result.ToString());
                Assert.Contains("Stairs asks System.Text.Json 99.0.0; the app runs " + app + ".", result.Warnings);
                Assert.Equal(new[] { "5" }, Run(manager, "STAIRS"));
            }
        }

        // ---- S15: the host's own, and what is only for the build

        [Fact]
        public void TheHostsIds_AreNeverAskedOfTheSource_NorTheirDependenciesThroughThem()
        {
            // A source that throws on them: the .nupkg of each is not a package, so reading one fails.
            foreach (string id in new[] { "OpenCAD2D.Core", "OpenCAD2D.Remote", "OpenCAD2D.Wpf", "GeometryHelper" })
            {
                File.WriteAllText(Path.Combine(_feed.Folder, id + ".1.0.0.nupkg"), "not a package");
            }

            _feed.AddLibrary("Bar", "1.0.0", "bar", needs: SourcePluginFeed.Dep("GeometryHelper", "1.0.0"));
            string project = Project(
                "Stairs",
                new[]
                {
                    Ref("OpenCAD2D.Core", "1.0.0"),
                    Ref("opencad2d.remote", "1.0.0"),
                    Ref("OpenCAD2D.Wpf", "1.0.0"),
                    Ref("GeometryHelper", "1.0.0"),
                    Ref("Bar", "1.0.0"),
                }
            );

            CadPluginBuild build = Compiler().Compile("Stairs", project);

            Assert.True(build.Succeeded, string.Join("\n", build.Errors));
            Assert.Equal(new[] { "Bar 1.0.0 direct" }, Listed(_asked[0]));
            Assert.Equal(new[] { "bar 1.0.0" }, Extracted());
        }

        [Fact]
        public void AHostVersionAboveTheApps_GivesALine_AndTheCompileGoesOnAgainstTheApps()
        {
            string project = Project(
                "Stairs",
                new[] { Ref("OpenCAD2D.Core", "99.2.0"), Ref("OpenCAD2D.Wpf", "1.0.0") }
            );

            CadPluginBuild build = Compiler(null).Compile("Stairs", project);
            string app = typeof(CadPlugin).Assembly.GetName().Version.ToString(3);

            Assert.True(build.Succeeded, string.Join("\n", build.Errors));
            Assert.Equal(
                new[] { "Stairs was written for OpenCAD2D.Core 99.2.0; this app has " + app + "." },
                build.Warnings
            );
        }

        [Fact]
        public void PrivateAssetsAll_AndIncludeAssetsWithoutCompileOrRuntime_AreBuildOnly_AndSkipped()
        {
            // None is in the source: asking for one would be an error.
            string project = Project(
                "Stairs",
                new[]
                {
                    "<PackageReference Include=\"CSharpier.MsBuild\" Version=\"1.0.0\" PrivateAssets=\"all\" />",
                    "<PackageReference Include=\"Some.Analyzers\" Version=\"1.0.0\">\n            <IncludeAssets>analyzers; build</IncludeAssets>\n        </PackageReference>",
                    "<PackageReference Include=\"Other.Analyzers\" Version=\"1.0.0\" IncludeAssets=\"build\" />",
                }
            );

            CadPluginBuild build = Compiler(null).Compile("Stairs", project);

            Assert.True(build.Succeeded, string.Join("\n", build.Errors));
            Assert.Empty(build.Warnings);
            Assert.False(Directory.Exists(Packages));
        }

        // ---- S16: the list kept

        [Fact]
        public void ASecondLoad_ReadsTheResolvedList_AndTouchesNoSource()
        {
            _feed.AddLibrary("Foo", "1.0.0", "1.0");
            string project = Project("Stairs", new[] { Ref("Foo", "1.0.0") });

            CadPluginBuild first = Compiler().Compile("Stairs", project);
            string gone = Path.Combine(_files.Root, "no-such-feed");
            CadPluginCompiler offline = Compiler(null, gone);
            CadPluginBuild second = offline.Compile("Stairs", project);

            Assert.True(first.Succeeded, string.Join("\n", first.Errors));
            Assert.False(first.FromCache);
            Assert.True(second.Succeeded, string.Join("\n", second.Errors));
            Assert.True(second.FromCache);
            Assert.Equal(first.AssemblyPath, second.AssemblyPath);
            Assert.Single(Directory.GetFiles(Path.Combine(Cache, "Stairs"), "packages.*.json"));
            Assert.False(Directory.Exists(gone));

            // One byte of the .csproj more, and the list is made again, which needs the source.
            File.AppendAllText(project, "\n<!-- changed -->\n");
            Assert.Throws<CadPluginException>(() => offline.Compile("Stairs", project));
        }

        [Fact]
        public void ARangeWithAFloat_IsResolvedOnce_WithALine_AndKeptUntilTheCsprojChanges()
        {
            _feed.AddLibrary("Foo", "1.0.0", "1.0");
            _feed.AddLibrary("Foo", "1.4.2", "1.4");
            string project = Project("Stairs", new[] { Ref("Foo", "1.*") });

            CadPluginBuild first = Compiler().Compile("Stairs", project);
            _feed.AddLibrary("Foo", "1.9.0", "1.9");
            CadPluginBuild second = Compiler().Compile("Stairs", project);
            string line =
                "Stairs.csproj: Foo 1.* is a range: Foo 1.4.2 was chosen and stays until the .csproj changes.";

            Assert.Equal(new[] { line }, first.Warnings);
            Assert.Equal(new[] { line }, second.Warnings);
            Assert.True(second.FromCache);
            Assert.Single(_asked);
        }

        [Fact]
        public void TheKey_TakesThePackagesBytes_SoAPackageMadeAgainMakesANewBuild()
        {
            _feed.AddLibrary("Foo", "1.0.0", "first");
            string project = Project("Stairs", new[] { Ref("Foo", "1.0.0") });
            CadPluginBuild first = Compiler().Compile("Stairs", project);

            // The same id and version, other bytes, in a packages folder made again: the build is not the cached one.
            Directory.Delete(Packages, true);
            File.Delete(Path.Combine(_feed.Folder, "Foo.1.0.0.nupkg"));
            _feed.AddLibrary("Foo", "1.0.0", "second");
            CadPluginBuild second = Compiler().Compile("Stairs", project);

            Assert.True(first.Succeeded && second.Succeeded);
            Assert.NotEqual(first.AssemblyPath, second.AssemblyPath);
            Assert.False(second.FromCache);
        }

        // ---- S17: the folder; S20: the question

        [Fact]
        public void APackageHalfExtracted_HasNoShaMark_SoItIsNotCached_AndIsDownloadedAgain()
        {
            _feed.AddLibrary("Foo", "1.0.0", "1.0");
            string half = Path.Combine(Packages, "foo", "1.0.0", "lib", "netstandard2.0");
            Directory.CreateDirectory(half);
            File.WriteAllText(Path.Combine(half, "Foo.dll"), "cut short");

            CadPluginBuild build = Compiler().Compile("Stairs", Project("Stairs", new[] { Ref("Foo", "1.0.0") }));

            Assert.True(build.Succeeded, string.Join("\n", build.Errors));
            Assert.Equal(new[] { "Foo 1.0.0 direct" }, Listed(_asked[0]));
            Assert.Equal(new[] { "foo 1.0.0" }, Extracted());
        }

        [Fact]
        public void PackagesAllInTheCache_NeedNoQuestion_EvenForAnotherPlugin()
        {
            _feed.AddLibrary("Foo", "1.0.0", "1.0");
            Compiler().Compile("Stairs", Project("Stairs", new[] { Ref("Foo", "1.0.0") }));

            CadPluginBuild other = Compiler(null).Compile("Other", Project("Other", new[] { Ref("Foo", "1.0.0") }));

            Assert.True(other.Succeeded, string.Join("\n", other.Errors));
            Assert.Single(_asked);
        }

        [Fact]
        public void TheQuestionOnTheCommandLine_CountsThePackagesToDownload_AndReadsYesAndNo()
        {
            var packages = new[]
            {
                new CadPackage("Bar", "1.0.0", true, false),
                new CadPackage("Foo", "1.2.0", false, false),
                new CadPackage("Aaa", "1.0.0", false, true),
            };

            Assert.Equal(
                "Download 2 packages for Stairs from nuget.org? [Yes/No] <No>:",
                CadPackageQuestion.Prompt("Stairs", packages)
            );
            Assert.Equal(
                "Download 1 package for Stairs from nuget.org? [Yes/No] <No>:",
                CadPackageQuestion.Prompt("Stairs", packages.Take(1).ToArray())
            );
            Assert.Equal("Bar 1.0.0", packages[0].ToString());
            Assert.True(CadPackageQuestion.Answer("y"));
            Assert.True(CadPackageQuestion.Answer(" YES "));
            Assert.False(CadPackageQuestion.Answer("n"));
            Assert.False(CadPackageQuestion.Answer("No"));
            Assert.False(CadPackageQuestion.Answer(""));
            Assert.False(CadPackageQuestion.Answer(null));
            Assert.Null(CadPackageQuestion.Answer("maybe"));
        }

        [Theory]
        [InlineData(false)]
        [InlineData(null)]
        public void No_AndNoQuestionSet_LeaveThePluginNotLoaded_WithNothingDownloaded(bool? answer)
        {
            _feed.AddLibrary("Foo", "1.0.0", "1.0");
            string project = Project("Stairs", new[] { Ref("Foo", "1.0.0") });

            using (CadPluginManager manager = Manager(answer))
            {
                CadPluginResult result = manager.Load(Path.GetDirectoryName(project));

                Assert.False(result.Succeeded);
                Assert.Equal("Stairs was not loaded: its packages were not downloaded.", result.Message);
                Assert.Null(manager.Find("Stairs"));
                Assert.Equal(answer == null ? 0 : 1, _asked.Count);
                Assert.False(Directory.Exists(Packages));
            }
        }

        // ---- S21: offline

        [Fact]
        public void ASourceThatCannotBeReached_LeavesThePluginNotLoaded_WithTheLineToReconnect()
        {
            Unreachable();
        }

        private void Unreachable()
        {
            string project = Project("Stairs", new[] { Ref("Foo", "1.0.0") });
            var loader = new CadPluginAssemblyLoader(
                Cache,
                new CadPackageRestorer("http://127.0.0.1:9/index.json", Packages)
            );
            loader.ConfirmDownload = (name, packages) => true;

            using (
                var manager = new CadPluginManager(
                    loader,
                    _files.PluginFolder,
                    _files.SettingsPath,
                    _files.ShadowFolder
                )
            )
            {
                manager.ConfirmUntrusted = path => true;
                CadPluginResult result = manager.Load(Path.GetDirectoryName(project));

                Assert.False(result.Succeeded);
                Assert.StartsWith(
                    "Stairs was not loaded: http://127.0.0.1:9/index.json could not be reached (",
                    result.Message
                );
                Assert.EndsWith("). Connect and run PLUGINRELOAD Stairs.", result.Message);
                Assert.False(Directory.Exists(Packages));
            }
        }
    }
}
#endif
