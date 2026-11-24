#if NET8_0_OR_GREATER
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Runtime.Loader;
using System.Text;
using OpenCAD2D.Wpf.Plugins;
using Xunit;
using Xunit.Abstractions;

namespace OpenCAD2D.Plugins.UnitTest
{
    /// <summary>
    /// The compiler of plugins given as a folder with a .csproj (<see cref="CadPluginCompiler"/>, .NET 8 only): what the
    /// .csproj says is read, the sources below the folder, the references, the errors as the command line writes them, and
    /// the cache keyed by the project and the sources.
    /// </summary>
    public sealed class SourcePluginCompilerTests : IDisposable
    {
        private readonly PluginFiles _files = new PluginFiles();
        private readonly ITestOutputHelper _output;

        public SourcePluginCompilerTests(ITestOutputHelper output)
        {
            _output = output;
        }

        public void Dispose() => _files.Dispose();

        private string Cache => Path.Combine(_files.Root, "cache");

        /// <summary>A plugin folder below the test's own: the .csproj and the sources (name, text), the full path of the .csproj.</summary>
        private string Project(string folder, string csproj, params string[] sources) =>
            SourceProject.Folder(Path.Combine(_files.Root, folder), csproj, sources);

        private CadPluginBuild Compile(string project, string name = "Stairs") =>
            new CadPluginCompiler(Cache).Compile(name, project);

        private static string[] TypesOf(string assemblyPath)
        {
            var context = new AssemblyLoadContext("test", true);

            try
            {
                using (var stream = new MemoryStream(File.ReadAllBytes(assemblyPath)))
                {
                    return context
                        .LoadFromStream(stream)
                        .GetTypes()
                        .Where(t => t.IsPublic)
                        .Select(t => t.FullName)
                        .OrderBy(n => n, StringComparer.Ordinal)
                        .ToArray();
                }
            }
            finally
            {
                context.Unload();
            }
        }

        /// <summary>The assembly's name, version and informational version, read without loading it into the app.</summary>
        private static (string Name, string Version, string Informational) IdentityOf(string assemblyPath)
        {
            var context = new AssemblyLoadContext("identity", true);

            try
            {
                using (var stream = new MemoryStream(File.ReadAllBytes(assemblyPath)))
                {
                    Assembly assembly = context.LoadFromStream(stream);
                    return (
                        assembly.GetName().Name,
                        assembly.GetName().Version.ToString(),
                        assembly.GetCustomAttribute<AssemblyInformationalVersionAttribute>()?.InformationalVersion
                    );
                }
            }
            finally
            {
                context.Unload();
            }
        }

        /// <summary>A source that does not compile unless the symbols named are all defined.</summary>
        private static string NeedsSymbols(params string[] symbols) =>
            "#if !("
            + string.Join(" && ", symbols)
            + ")\n#error a symbol is missing\n#endif\nnamespace Stairs { class A { } }\n";

        private static string Failure(CadPluginBuild build) => string.Join("\n", build.Errors);

        // ---- S2: what the .csproj says

        [Fact]
        public void OneFile_CompilesIntoAnAssemblyNamedAfterTheProject_WithItsPdb()
        {
            string project = Project("one", SourceProject.Csproj(), "Stairs.cs", SourceProject.Stairs());

            CadPluginBuild build = Compile(project);

            Assert.True(build.Succeeded, Failure(build));
            Assert.False(build.FromCache);
            Assert.Equal(0, build.ErrorCount);
            Assert.Empty(build.Warnings);
            Assert.True(File.Exists(build.AssemblyPath));
            Assert.True(File.Exists(build.PdbPath));
            Assert.Equal(Path.Combine(Cache, "Stairs"), Path.GetDirectoryName(build.AssemblyPath));
            Assert.Equal("Stairs", AssemblyName.GetAssemblyName(build.AssemblyPath).Name);
            Assert.Equal(new[] { "Stairs.StairsCommands" }, TypesOf(build.AssemblyPath));
        }

        [Fact]
        public void TheAssemblyName_IsTheOneTheProjectGives_ElseTheNameOfTheProjectFile()
        {
            string named = Project(
                "named",
                SourceProject.Csproj(new[] { "<AssemblyName>StairsPlugin</AssemblyName>" }),
                "Stairs.cs",
                SourceProject.Stairs()
            );
            string bare = Path.Combine(_files.Root, "bare", "FlightTool.csproj");
            SourceProject.Write(Path.GetDirectoryName(bare), "Stairs.cs", SourceProject.Stairs());
            SourceProject.Write(Path.GetDirectoryName(bare), "FlightTool.csproj", SourceProject.Csproj());

            CadPluginBuild withName = Compile(named);
            CadPluginBuild withoutName = Compile(bare, "Flight");

            Assert.Equal("StairsPlugin", AssemblyName.GetAssemblyName(withName.AssemblyPath).Name);
            Assert.Equal("FlightTool", AssemblyName.GetAssemblyName(withoutName.AssemblyPath).Name);
        }

        [Fact]
        public void TheSources_AreEveryCsBelowTheFolder_ButBinAndObjAndWhatCompileRemoveTakesOut()
        {
            string broken = "namespace Stairs { class Broken { int M() => nothing; } }\n";
            string project = Project(
                "below",
                SourceProject.Csproj(
                    items: new[]
                    {
                        "<Compile Remove=\"Old\\**\" />",
                        "<Compile Remove=\"*.Draft.cs;Geometry\\Tmp?.cs\" />",
                    }
                ),
                "StairsCommands.cs",
                SourceProject.Command(),
                "Geometry/Steps.cs",
                SourceProject.StepsSource,
                "bin/Release/Stale.cs",
                broken,
                "obj/Stale.cs",
                broken,
                "Old/Deep/Gone.cs",
                broken,
                "Notes.Draft.cs",
                broken,
                "Geometry/Tmp1.cs",
                broken,
                "Other/bin/Kept.cs",
                "namespace Stairs { public class Kept { } }\n"
            );

            CadPluginBuild build = Compile(project);

            Assert.True(build.Succeeded, Failure(build));
            Assert.Equal(new[] { "Stairs.Kept", "Stairs.StairsCommands", "Stairs.Steps" }, TypesOf(build.AssemblyPath));
        }

        [Fact]
        public void AnErrorInASourceBelowTheFolder_NamesItByItsPathFromTheFolder()
        {
            string project = Project(
                "path",
                SourceProject.Csproj(),
                "StairsCommands.cs",
                SourceProject.Command(),
                "Geometry/Steps.cs",
                SourceProject.StepsSource.Replace("rise / 175.0", "rise / run")
            );

            CadPluginBuild build = Compile(project);

            Assert.False(build.Succeeded);
            Assert.Equal(
                new[]
                {
                    "Geometry"
                        + Path.DirectorySeparatorChar
                        + "Steps.cs(5,81): error CS0103: The name 'run' does not exist in the current context",
                },
                build.Errors
            );
        }

        // ---- S2, S3: usings, language, version, symbols

        [Fact]
        public void ImplicitUsings_AreTheSdksOnes_WithWpfTakingOutSystemIoAndWindowsFormsAddingItsOwn()
        {
            string uses =
                "namespace Stairs { class A { object M() => new List<int>().Select(x => x).Count() + Path.GetFileName(\"a\").Length; Task T() => Task.CompletedTask; Thread Th() => null; } }\n";
            string withIo = "namespace Stairs { class B { bool M() => File.Exists(\"a\"); } }\n";
            string forms = "namespace Stairs { class C { object M() => Keys.A; Color K() => default; } }\n";

            CadPluginBuild sdk = Compile(
                Project(
                    "usings-sdk",
                    SourceProject.Csproj(new[] { "<ImplicitUsings>enable</ImplicitUsings>" }),
                    "A.cs",
                    uses,
                    "B.cs",
                    withIo
                )
            );
            CadPluginBuild without = Compile(Project("usings-none", SourceProject.Csproj(), "A.cs", uses), "None");
            CadPluginBuild wpf = Compile(
                Project(
                    "usings-wpf",
                    SourceProject.Csproj(new[] { "<ImplicitUsings>true</ImplicitUsings>", "<UseWPF>true</UseWPF>" }),
                    "A.cs",
                    uses.Replace(" + Path.GetFileName(\"a\").Length", string.Empty),
                    "B.cs",
                    withIo
                ),
                "Wpf"
            );
            CadPluginBuild winForms = Compile(
                Project(
                    "usings-forms",
                    SourceProject.Csproj(
                        new[] { "<ImplicitUsings>enable</ImplicitUsings>", "<UseWindowsForms>true</UseWindowsForms>" }
                    ),
                    "C.cs",
                    forms
                ),
                "Forms"
            );
            CadPluginBuild noForms = Compile(
                Project(
                    "usings-noforms",
                    SourceProject.Csproj(new[] { "<ImplicitUsings>enable</ImplicitUsings>" }),
                    "C.cs",
                    forms
                ),
                "NoForms"
            );

            Assert.True(sdk.Succeeded, Failure(sdk));
            Assert.False(without.Succeeded);
            Assert.Contains(
                "A.cs(1,48): error CS0246: The type or namespace name 'List<>' could not be found (are you missing a using directive or an assembly reference?)",
                without.Errors
            );
            Assert.False(wpf.Succeeded);
            Assert.Equal(
                new[] { "B.cs(1,42): error CS0103: The name 'File' does not exist in the current context" },
                wpf.Errors
            );
            Assert.True(winForms.Succeeded, Failure(winForms));
            Assert.False(noForms.Succeeded);
        }

        [Fact]
        public void UsingItems_AreGlobalUsings_PlainStaticAndWithAnAlias()
        {
            string source = "namespace Stairs { class A { int M() => Max(1, 2) + new Ints().Count; Flight F; } }\n";

            CadPluginBuild build = Compile(
                Project(
                    "using-items",
                    SourceProject.Csproj(
                        items: new[]
                        {
                            "<Using Include=\"System.Math\" Static=\"true\" />",
                            "<Using Include=\"System.Collections.Generic\" />",
                            "<Using Include=\"System.Collections.Generic.List&lt;int&gt;\" Alias=\"Ints\" />",
                            "<Using Include=\"System.Text.StringBuilder\" Alias=\"Flight\" />",
                        }
                    ),
                    "A.cs",
                    source
                )
            );

            Assert.True(build.Succeeded, Failure(build));
        }

        [Fact]
        public void LangVersion_IsReadFromTheProject_AndAnUnknownOneIsAnError()
        {
            string switchExpression = "namespace Stairs { class A { int M(int x) => x switch { 1 => 2, _ => 3 }; } }\n";

            CadPluginBuild old = Compile(
                Project(
                    "lang-old",
                    SourceProject.Csproj(new[] { "<LangVersion>7.3</LangVersion>" }),
                    "A.cs",
                    switchExpression
                )
            );
            CadPluginBuild current = Compile(
                Project(
                    "lang-13",
                    SourceProject.Csproj(new[] { "<LangVersion>13.0</LangVersion>" }),
                    "A.cs",
                    switchExpression
                ),
                "Current"
            );
            CadPluginBuild latest = Compile(
                Project(
                    "lang-latest",
                    SourceProject.Csproj(new[] { "<LangVersion>latest</LangVersion>" }),
                    "A.cs",
                    switchExpression
                ),
                "Latest"
            );
            CadPluginBuild unknown = Compile(
                Project(
                    "lang-unknown",
                    SourceProject.Csproj(new[] { "<LangVersion>99</LangVersion>" }),
                    "A.cs",
                    switchExpression
                ),
                "Unknown"
            );

            Assert.False(old.Succeeded);
            Assert.Equal(
                new[]
                {
                    "A.cs(1,48): error CS8370: Feature 'recursive patterns' is not available in C# 7.3. Please use language version 8.0 or greater.",
                },
                old.Errors.Take(1)
            );
            Assert.True(current.Succeeded, Failure(current));
            Assert.True(latest.Succeeded, Failure(latest));
            Assert.False(unknown.Succeeded);
            Assert.Equal(
                new[]
                {
                    "Stairs.csproj(4,10): error CS1617: Invalid option '99' for /langversion. Use '/langversion:?' to list supported values.",
                },
                unknown.Errors
            );
        }

        [Fact]
        public void Nullable_IsReadFromTheProject_AndAValueThatIsNoneOfTheFourIsAnError()
        {
            string source = "namespace Stairs { class A { string? M() => null; } }\n";

            // Nullable reference types need C# 8: with 7.3 the project asks for what the language cannot give.
            CadPluginBuild enabled = Compile(
                Project(
                    "nullable-on",
                    SourceProject.Csproj(new[] { "<LangVersion>7.3</LangVersion>", "<Nullable>enable</Nullable>" }),
                    "A.cs",
                    "namespace Stairs { class A { } }\n"
                )
            );
            CadPluginBuild disabled = Compile(
                Project(
                    "nullable-off",
                    SourceProject.Csproj(new[] { "<LangVersion>7.3</LangVersion>", "<Nullable>disable</Nullable>" }),
                    "A.cs",
                    "namespace Stairs { class A { } }\n"
                ),
                "Off"
            );
            CadPluginBuild warnings = Compile(
                Project(
                    "nullable-warn",
                    SourceProject.Csproj(new[] { "<Nullable>warnings</Nullable>" }),
                    "A.cs",
                    source
                ),
                "Warn"
            );
            CadPluginBuild bad = Compile(
                Project("nullable-bad", SourceProject.Csproj(new[] { "<Nullable>yes</Nullable>" }), "A.cs", source),
                "Bad"
            );

            Assert.False(enabled.Succeeded);
            Assert.Contains(
                "error CS8630: Invalid 'NullableContextOptions' value: 'Enable' for C# 7.3.",
                Failure(enabled)
            );
            Assert.True(disabled.Succeeded, Failure(disabled));
            Assert.True(warnings.Succeeded, Failure(warnings));
            Assert.Equal(
                new[]
                {
                    "Stairs.csproj(4,10): error OC0002: Nullable 'yes' is none of enable, disable, warnings and annotations.",
                },
                bad.Errors
            );
        }

        [Fact]
        public void TheVersion_BecomesTheAssemblyVersionFileVersionAndInformationalVersion_AsTheSdkWritesThem()
        {
            string source = "namespace Stairs { public class A { } }\n";

            CadPluginBuild versioned = Compile(
                Project("version", SourceProject.Csproj(new[] { "<Version>1.2.3-beta.1</Version>" }), "A.cs", source)
            );
            CadPluginBuild plain = Compile(Project("version-none", SourceProject.Csproj(), "A.cs", source), "Plain");
            CadPluginBuild off = Compile(
                Project(
                    "version-off",
                    SourceProject.Csproj(
                        new[] { "<Version>1.2.3</Version>", "<GenerateAssemblyInfo>false</GenerateAssemblyInfo>" }
                    ),
                    "A.cs",
                    source
                ),
                "Off"
            );
            CadPluginBuild twice = Compile(
                Project(
                    "version-twice",
                    SourceProject.Csproj(new[] { "<Version>1.2.3</Version>" }),
                    "A.cs",
                    "[assembly: System.Reflection.AssemblyVersion(\"4.0.0.0\")]\n" + source
                ),
                "Twice"
            );

            Assert.Equal(("Stairs", "1.2.3.0", "1.2.3-beta.1"), IdentityOf(versioned.AssemblyPath));
            Assert.Equal(("Stairs", "1.0.0.0", "1.0.0"), IdentityOf(plain.AssemblyPath));
            Assert.Equal(("Stairs", "0.0.0.0", null), IdentityOf(off.AssemblyPath));
            Assert.False(twice.Succeeded);
            Assert.Equal(
                new[] { "A.cs(1,12): error CS0579: Duplicate 'System.Reflection.AssemblyVersion' attribute" },
                twice.Errors
            );
        }

        [Fact]
        public void ThePreprocessorSymbols_AreTheSdksForNet8WindowsRelease_ThenTheProjectsOwn()
        {
            string[] sdk =
            {
                "TRACE",
                "RELEASE",
                "NET",
                "NET8_0",
                "NETCOREAPP",
                "WINDOWS",
                "WINDOWS7_0",
                "NET5_0_OR_GREATER",
                "NET6_0_OR_GREATER",
                "NET7_0_OR_GREATER",
                "NET8_0_OR_GREATER",
                "NETCOREAPP1_0_OR_GREATER",
                "NETCOREAPP2_1_OR_GREATER",
                "NETCOREAPP3_1_OR_GREATER",
                "WINDOWS7_0_OR_GREATER",
            };
            string noDebug = "#if DEBUG\n#error DEBUG is defined\n#endif\nnamespace Stairs { class B { } }\n";

            CadPluginBuild own = Compile(
                Project(
                    "defines",
                    SourceProject.Csproj(
                        new[] { "<DefineConstants>$(DefineConstants);STAIRS_X;STAIRS_Y</DefineConstants>" }
                    ),
                    "A.cs",
                    NeedsSymbols(sdk.Concat(new[] { "STAIRS_X", "STAIRS_Y" }).ToArray()),
                    "B.cs",
                    noDebug
                )
            );
            CadPluginBuild conditional = Compile(
                Project(
                    "defines-condition",
                    SourceProject.Csproj(
                        new[]
                        {
                            "<DefineConstants Condition=\"'$(Configuration)' == 'Release'\">STAIRS_X</DefineConstants>",
                        }
                    ),
                    "A.cs",
                    NeedsSymbols("STAIRS_X")
                ),
                "Conditional"
            );

            Assert.True(own.Succeeded, Failure(own));
            Assert.Empty(own.Warnings);
            Assert.False(conditional.Succeeded);
        }

        [Fact]
        public void AllowUnsafeBlocks_IsReadFromTheProject()
        {
            string source = "namespace Stairs { class A { unsafe int M(int* p) => *p; } }\n";

            CadPluginBuild allowed = Compile(
                Project(
                    "unsafe-on",
                    SourceProject.Csproj(new[] { "<AllowUnsafeBlocks>true</AllowUnsafeBlocks>" }),
                    "A.cs",
                    source
                )
            );
            CadPluginBuild refused = Compile(Project("unsafe-off", SourceProject.Csproj(), "A.cs", source), "Off");

            Assert.True(allowed.Succeeded, Failure(allowed));
            Assert.False(refused.Succeeded);
            Assert.Contains("error CS0227: Unsafe code may only appear if compiling with /unsafe", Failure(refused));
        }

        // ---- S4: what is not read

        [Fact]
        public void AConditionedGroupThatHoldsSomethingRead_IsSkipped_WithOneLineSayingSo()
        {
            string project = Project(
                "condition",
                SourceProject
                    .Csproj(
                        properties: new[]
                        {
                            "<Description>Not honoured, and nothing to say about it.</Description>",
                            "<Version Condition=\"'$(Configuration)' == 'Debug'\">9.9.9</Version>",
                        },
                        items: new[] { "<Compile Remove=\"A.cs\" Condition=\"Exists('A.cs')\" />" }
                    )
                    .Replace(
                        "    </PropertyGroup>\n",
                        "    </PropertyGroup>\n    <PropertyGroup Condition=\"'$(Configuration)' == 'Debug'\">\n        <AssemblyName>Debugging</AssemblyName>\n    </PropertyGroup>\n    <PropertyGroup Condition=\"'$(Configuration)' == 'Debug'\">\n        <Description>Holds nothing that is read.</Description>\n    </PropertyGroup>\n"
                    ),
                "A.cs",
                "namespace Stairs { public class A { } }\n"
            );

            CadPluginBuild build = Compile(project);

            Assert.True(build.Succeeded, Failure(build));
            Assert.Equal(
                new[]
                {
                    "Stairs.csproj: <Version Condition=\"'$(Configuration)' == 'Debug'\"> was not read (conditions are not evaluated).",
                    "Stairs.csproj: <PropertyGroup Condition=\"'$(Configuration)' == 'Debug'\"> was not read (conditions are not evaluated).",
                    "Stairs.csproj: <Compile Condition=\"Exists('A.cs')\"> was not read (conditions are not evaluated).",
                },
                build.Warnings
            );
            Assert.Equal("Stairs", AssemblyName.GetAssemblyName(build.AssemblyPath).Name);
            Assert.Equal("1.0.0.0", IdentityOf(build.AssemblyPath).Version);
        }

        [Fact]
        public void ADirectoryBuildPropsInThePluginFolder_IsNotRead_AndTheProjectSaysSo()
        {
            string project = Project(
                "props",
                SourceProject.Csproj(),
                "A.cs",
                "namespace Stairs { public class A { } }\n",
                "Directory.Build.props",
                "<Project />\n",
                "Directory.Build.targets",
                "<Project />\n",
                "Directory.Packages.props",
                "<Project />\n"
            );

            CadPluginBuild build = Compile(project);

            Assert.True(build.Succeeded, Failure(build));
            Assert.Equal(
                new[]
                {
                    "Stairs.csproj: Directory.Build.props is not read.",
                    "Stairs.csproj: Directory.Build.targets is not read.",
                    "Stairs.csproj: Directory.Packages.props is not read.",
                },
                build.Warnings
            );
        }

        [Fact]
        public void AValueWithAnMsbuildProperty_IsTakenAsWritten_WithALine()
        {
            CadPluginBuild build = Compile(
                Project(
                    "dollar",
                    SourceProject.Csproj(new[] { "<AssemblyName>$(MSBuildProjectName)Tool</AssemblyName>" }),
                    "A.cs",
                    "namespace Stairs { public class A { } }\n"
                )
            );

            Assert.True(build.Succeeded, Failure(build));
            Assert.Equal(
                new[]
                {
                    "Stairs.csproj: <AssemblyName> holds $(...), which is not expanded; '$(MSBuildProjectName)Tool' is taken as written.",
                },
                build.Warnings
            );
            Assert.Equal("$(MSBuildProjectName)Tool", AssemblyName.GetAssemblyName(build.AssemblyPath).Name);
        }

        [Theory]
        [InlineData("net8.0", false)]
        [InlineData("net8.0-windows", false)]
        [InlineData("net8.0-windows10.0.19041", false)]
        [InlineData("netstandard2.0", false)]
        [InlineData("netstandard2.1", false)]
        [InlineData("net6.0", true)]
        [InlineData("net48", true)]
        public void TheTargetFramework_IsReadOnlyToWarnWhenItIsNotNet8OrNetStandard2(string framework, bool warns)
        {
            string project = Project(
                "framework-" + framework,
                SourceProject.Csproj().Replace("net8.0-windows", framework),
                "A.cs",
                "namespace Stairs { public class A { } }\n"
            );

            CadPluginBuild build = Compile(project, "Fw" + framework.Replace('.', '_').Replace('-', '_'));

            Assert.True(build.Succeeded, Failure(build));
            Assert.Equal(
                warns
                    ? new[]
                    {
                        "Stairs.csproj: TargetFramework "
                            + framework
                            + " is not net8.0 or netstandard2.x; the app compiles for its own .NET 8.",
                    }
                    : new string[0],
                build.Warnings
            );
        }

        // ---- S5: what is refused

        [Fact]
        public void WhatTheAppCannotReadIsRefused_OneErrorLineEach_InTheFormOfACompilerError()
        {
            string project = Project(
                "refused",
                SourceProject.Csproj(
                    new[] { "<EnableDefaultCompileItems>false</EnableDefaultCompileItems>" },
                    new[]
                    {
                        "<Compile Include=\"..\\Shared\\Shared.cs\" />",
                        "<ProjectReference Include=\"..\\Lib\\Lib.csproj\" />",
                        "<PackageReference Include=\"OpenCAD2D.Core\" />",
                        "<PackageReference Include=\"OpenCAD2D.Core\" Version=\"\" />",
                    }
                ),
                "A.cs",
                "namespace Stairs { public class A { } }\n"
            );

            CadPluginBuild build = Compile(project);

            Assert.False(build.Succeeded);
            Assert.Equal(
                new[]
                {
                    "Stairs.csproj(4,10): error OC0002: EnableDefaultCompileItems false is not read: the sources are every .cs below the folder.",
                    "Stairs.csproj(7,10): error OC0002: <Compile Include> takes a file inside the plugin's folder: '..\\Shared\\Shared.cs'.",
                    "Stairs.csproj(8,10): error OC0002: <ProjectReference> is not read: a plugin is built from its own folder alone.",
                    "Stairs.csproj(9,10): error OC0002: <PackageReference Include=\"OpenCAD2D.Core\"> has no Version: central package management is not read.",
                    "Stairs.csproj(10,10): error OC0002: <PackageReference Include=\"OpenCAD2D.Core\"> has no Version: central package management is not read.",
                },
                build.Errors
            );
            Assert.Equal(5, build.ErrorCount);
            Assert.False(Directory.Exists(Cache));
        }

        [Fact]
        public void APackageOfTheHost_IsTheAppsOwn_AnyOtherPackageIsRefusedUntilNuGetComes()
        {
            string[] host =
            {
                "<PackageReference Include=\"OpenCAD2D.Core\" Version=\"1.0.0\" />",
                "<PackageReference Include=\"opencad2d.remote\" Version=\"1.0.0\" />",
                "<PackageReference Include=\"OpenCAD2D.Wpf\" Version=\"1.0.0\" />",
                "<PackageReference Include=\"GeometryHelper\">\n            <Version>13.12.1</Version>\n        </PackageReference>",
            };
            string source = "namespace Stairs { public class A { } }\n";

            CadPluginBuild own = Compile(Project("package-host", SourceProject.Csproj(items: host), "A.cs", source));
            CadPluginBuild other = Compile(
                Project(
                    "package-other",
                    SourceProject.Csproj(
                        items: new[]
                        {
                            "<PackageReference Include=\"OpenCAD2D.Core\" Version=\"1.0.0\" />",
                            "<PackageReference Include=\"Newtonsoft.Json\" Version=\"13.0.3\" />",
                        }
                    ),
                    "A.cs",
                    source
                ),
                "Other"
            );

            Assert.True(own.Succeeded, Failure(own));
            Assert.Empty(own.Warnings);
            Assert.False(other.Succeeded);
            Assert.Equal(
                new[]
                {
                    "Stairs.csproj(7,10): error OC0002: <PackageReference Include=\"Newtonsoft.Json\"> is refused: NuGet comes with TF3.",
                },
                other.Errors
            );
        }

        [Fact]
        public void AProjectSavedWithAByteOrderMark_AsVisualStudioSavesIt_IsRead()
        {
            string project = Project(
                "bom",
                SourceProject.Csproj(new[] { "<AssemblyName>WithMark</AssemblyName>" }),
                "A.cs",
                "namespace Stairs { public class A { } }\n"
            );
            File.WriteAllText(project, File.ReadAllText(project), new UTF8Encoding(true));

            CadPluginBuild build = Compile(project);

            Assert.Equal(new byte[] { 0xEF, 0xBB, 0xBF }, File.ReadAllBytes(project).Take(3));
            Assert.True(build.Succeeded, Failure(build));
            Assert.Equal("WithMark", AssemblyName.GetAssemblyName(build.AssemblyPath).Name);
        }

        [Fact]
        public void AnXmlFileThatIsNotAnSdkProject_IsRefusedWithItsLine()
        {
            string source = "namespace Stairs { public class A { } }\n";

            CadPluginBuild broken = Compile(
                Project(
                    "xml-broken",
                    "<Project Sdk=\"Microsoft.NET.Sdk\">\n  <PropertyGroup>\n</Project>\n",
                    "A.cs",
                    source
                )
            );
            CadPluginBuild old = Compile(
                Project(
                    "xml-old",
                    "<Project ToolsVersion=\"15.0\">\n  <ItemGroup><Compile Include=\"A.cs\" /></ItemGroup>\n</Project>\n",
                    "A.cs",
                    source
                ),
                "Old"
            );

            Assert.False(broken.Succeeded);
            Assert.StartsWith("Stairs.csproj(3,3): error OC0002: ", broken.Errors[0]);
            Assert.Equal(
                new[]
                {
                    "Stairs.csproj(1,2): error OC0002: Only an SDK-style project is read: <Project Sdk=\"Microsoft.NET.Sdk\">.",
                },
                old.Errors
            );
        }

        // ---- S6: references

        /// <summary>A library built from <see cref="SourceProject.StepsSource"/>, as a .dll in a folder of the plugin.</summary>
        private string Library(string folder, string file, string name = "StairsMath")
        {
            string source = SourceProject.Write(
                Path.Combine(_files.Root, "lib-" + name),
                "Steps.cs",
                SourceProject.StepsSource
            );
            string csproj = SourceProject.Write(
                Path.GetDirectoryName(source),
                name + ".csproj",
                SourceProject.Csproj()
            );
            CadPluginBuild helper = Compile(csproj, name);
            Assert.True(helper.Succeeded, Failure(helper));
            string target = Path.Combine(_files.Root, folder, file);
            Directory.CreateDirectory(Path.GetDirectoryName(target));
            File.Copy(helper.AssemblyPath, target);
            return target;
        }

        [Fact]
        public void AReferenceWithAHintPathInsideTheFolder_AddsTheDll()
        {
            Library("hint-inside", "libs/StairsMath.dll");
            string project = Project(
                "hint-inside",
                SourceProject.Csproj(
                    items: new[]
                    {
                        "<Reference Include=\"StairsMath\">",
                        "    <HintPath>libs\\StairsMath.dll</HintPath>",
                        "</Reference>",
                    }
                ),
                "StairsCommands.cs",
                SourceProject.Command()
            );

            CadPluginBuild build = Compile(project);

            Assert.True(build.Succeeded, Failure(build));
            Assert.Equal(new[] { "Stairs.StairsCommands" }, TypesOf(build.AssemblyPath));
        }

        [Fact]
        public void AHintPathAsAnAttribute_AndOneRootedInsideTheFolder_AreTakenToo()
        {
            string library = Library("hint-forms", "StairsMath.dll");
            string project = Project(
                "hint-forms",
                SourceProject.Csproj(
                    items: new[]
                    {
                        "<Reference Include=\"StairsMath, Version=1.0.0.0, Culture=neutral\" HintPath=\""
                            + library
                            + "\" />",
                    }
                ),
                "StairsCommands.cs",
                SourceProject.Command()
            );

            CadPluginBuild build = Compile(project);

            Assert.True(build.Succeeded, Failure(build));
        }

        [Fact]
        public void WithoutTheReference_TheSameSourceDoesNotCompile()
        {
            CadPluginBuild build = Compile(
                Project("no-ref", SourceProject.Csproj(), "Stairs.cs", SourceProject.Command())
            );

            Assert.False(build.Succeeded);
            Assert.Equal(
                new[] { "Stairs.cs(12,33): error CS0103: The name 'Steps' does not exist in the current context" },
                build.Errors
            );
        }

        [Fact]
        public void AHintPathThatIsMissingOutsideTheFolderOrNotADll_IsAnErrorOfItsLine()
        {
            string project = Project(
                "hint-bad",
                SourceProject.Csproj(
                    items: new[]
                    {
                        "<Reference Include=\"Missing\" HintPath=\"libs\\Missing.dll\" />",
                        "<Reference Include=\"Outside\" HintPath=\"..\\Outside.dll\" />",
                        "<Reference Include=\"Rooted\" HintPath=\"C:\\Other\\Rooted.dll\" />",
                        "<Reference Include=\"Text\" HintPath=\"libs\\Notes.txt\" />",
                        "<Reference Include=\"NoHint\" />",
                    }
                ),
                "StairsCommands.cs",
                SourceProject.Stairs()
            );

            CadPluginBuild build = Compile(project);

            Assert.False(build.Succeeded);
            Assert.Equal(
                new[]
                {
                    "Stairs.csproj(6,10): error CS0006: Metadata file 'libs\\Missing.dll' could not be found",
                    "Stairs.csproj(7,10): error OC0001: <Reference> takes a .dll inside the tool's folder: '..\\Outside.dll'",
                    "Stairs.csproj(8,10): error OC0001: <Reference> takes a .dll inside the tool's folder: 'C:\\Other\\Rooted.dll'",
                    "Stairs.csproj(9,10): error OC0001: <Reference> takes a .dll inside the tool's folder: 'libs\\Notes.txt'",
                    "Stairs.csproj(10,10): error OC0002: <Reference Include=\"NoHint\"> has no HintPath.",
                },
                build.Errors
            );
            Assert.False(Directory.Exists(Cache));
        }

        [Fact]
        public void AReferenceToTheHostOrTheFramework_IsTheAppsOwn_AndItsHintPathIsIgnored()
        {
            string project = Project(
                "host-ref",
                SourceProject.Csproj(
                    items: new[]
                    {
                        "<Reference Include=\"OpenCAD2D.Core\" HintPath=\"C:\\Program Files\\OpenCAD2D\\OpenCAD2D.Core.dll\" />",
                        "<Reference Include=\"GeometryHelper\" HintPath=\"..\\..\\Lib\\GeometryHelper.dll\" />",
                        "<Reference Include=\"OpenCAD2D.Wpf\"><HintPath>..\\OpenCAD2D.Wpf.dll</HintPath></Reference>",
                        "<Reference Include=\"OpenCAD2D.Remote\" />",
                        "<Reference Include=\"System.Xml\" />",
                        "<Reference Include=\"PresentationFramework\" />",
                        "<Reference Include=\"WindowsBase\" />",
                    }
                ),
                "A.cs",
                "namespace Stairs { public class A { } }\n"
            );

            CadPluginBuild build = Compile(project);

            Assert.True(build.Succeeded, Failure(build));
            Assert.Empty(build.Warnings);
        }

        [Fact]
        public void ALineOfTheOldDirective_IsAComment_AndNothingIsRead()
        {
            string source = "//#r \"Missing.dll\"\n//#r \"../Other.dll\"\n" + SourceProject.Stairs();

            CadPluginBuild build = Compile(Project("directive", SourceProject.Csproj(), "Stairs.cs", source));

            Assert.True(build.Succeeded, Failure(build));
        }

        // ---- errors of the sources

        [Fact]
        public void AnError_IsWrittenAsFileLineColumnAndId_AndNothingIsCached()
        {
            var lines = new List<string>(SourceProject.Command().Split('\n'));
            lines[11] = "        x = 1;";

            CadPluginBuild build = Compile(
                Project("error", SourceProject.Csproj(), "Stairs.cs", string.Join("\n", lines))
            );

            Assert.False(build.Succeeded);
            Assert.Null(build.AssemblyPath);
            Assert.Equal(1, build.ErrorCount);
            Assert.Equal(
                new[] { "Stairs.cs(12,9): error CS0103: The name 'x' does not exist in the current context" },
                build.Errors
            );
            Assert.False(Directory.Exists(Path.Combine(Cache, "Stairs")));
        }

        [Fact]
        public void TwentyFiveErrors_GiveTheFirstTwentyInOrderOfFileLineAndColumn_AndCountAll()
        {
            var body = new StringBuilder(
                "namespace Stairs\n{\n    public static class Bad\n    {\n        public static void M()\n        {\n"
            );

            for (int i = 0; i < 25; i++)
            {
                body.Append("            u").Append(i).Append(" = 1;\n");
            }

            body.Append("        }\n    }\n}\n");
            string project = Project(
                "many",
                SourceProject.Csproj(),
                "B.cs",
                body.ToString(),
                "A.cs",
                "namespace Stairs { class A { int M() => y; } }\n"
            );

            CadPluginBuild build = Compile(project);

            Assert.Equal(26, build.ErrorCount);
            Assert.Equal(CadPluginCompiler.MaxErrors, build.Errors.Count);
            Assert.Equal(
                "A.cs(1,41): error CS0103: The name 'y' does not exist in the current context",
                build.Errors[0]
            );
            Assert.Equal(
                "B.cs(7,13): error CS0103: The name 'u0' does not exist in the current context",
                build.Errors[1]
            );
            Assert.Equal(
                "B.cs(25,13): error CS0103: The name 'u18' does not exist in the current context",
                build.Errors[19]
            );
        }

        // ---- S10: the cache

        [Fact]
        public void ASecondCompileOfUnchangedFiles_IsTakenFromTheCache_WithTheWarningsStillGiven()
        {
            string project = Project(
                "cached",
                SourceProject.Csproj().Replace("net8.0-windows", "net6.0"),
                "StairsCommands.cs",
                SourceProject.Command(),
                "Steps.cs",
                SourceProject.StepsSource
            );
            var compiler = new CadPluginCompiler(Cache);
            var clock = Stopwatch.StartNew();
            CadPluginBuild first = compiler.Compile("Stairs", project);
            long compiled = clock.ElapsedMilliseconds;
            DateTime written = File.GetLastWriteTimeUtc(first.AssemblyPath);

            clock.Restart();
            CadPluginBuild second = new CadPluginCompiler(Cache).Compile("Stairs", project);
            long cached = clock.ElapsedMilliseconds;
            _output.WriteLine("compiled in " + compiled + " ms, taken from the cache in " + cached + " ms");

            Assert.True(first.Succeeded, Failure(first));
            Assert.False(first.FromCache);
            Assert.True(second.FromCache);
            Assert.Equal(first.AssemblyPath, second.AssemblyPath);
            Assert.Equal(written, File.GetLastWriteTimeUtc(second.AssemblyPath));
            Assert.Single(first.Warnings);
            Assert.Equal(first.Warnings, second.Warnings);
        }

        [Fact]
        public void OneSourceByteChanged_GivesANewKey_AndTheOldBuildIsDeleted()
        {
            string project = Project(
                "changed",
                SourceProject.Csproj(),
                "StairsCommands.cs",
                SourceProject.Command(),
                "Steps.cs",
                SourceProject.StepsSource
            );
            var compiler = new CadPluginCompiler(Cache);
            CadPluginBuild first = compiler.Compile("Stairs", project);
            File.WriteAllText(
                Path.Combine(Path.GetDirectoryName(project), "Steps.cs"),
                SourceProject.StepsSource.Replace("175.0", "176.0"),
                new UTF8Encoding(false)
            );

            CadPluginBuild second = compiler.Compile("Stairs", project);

            Assert.True(second.Succeeded, Failure(second));
            Assert.False(second.FromCache);
            Assert.NotEqual(first.AssemblyPath, second.AssemblyPath);
            Assert.Equal(
                new[] { Path.GetFileName(second.AssemblyPath), Path.GetFileName(second.PdbPath) }.OrderBy(
                    n => n,
                    StringComparer.Ordinal
                ),
                Directory
                    .GetFiles(Path.Combine(Cache, "Stairs"))
                    .Select(Path.GetFileName)
                    .OrderBy(n => n, StringComparer.Ordinal)
            );
        }

        [Fact]
        public void OneByteOfTheCsproj_GivesANewKey_WhateverItSays()
        {
            string project = Project("key-csproj", SourceProject.Csproj(), "Stairs.cs", SourceProject.Stairs());
            var compiler = new CadPluginCompiler(Cache);
            CadPluginBuild first = compiler.Compile("Stairs", project);
            Assert.True(compiler.Compile("Stairs", project).FromCache);

            // A comment changes nothing the compiler is told, and the key still changes.
            File.WriteAllText(
                project,
                File.ReadAllText(project).Replace("<PropertyGroup>", "<PropertyGroup><!-- a -->"),
                new UTF8Encoding(false)
            );
            CadPluginBuild second = compiler.Compile("Stairs", project);

            Assert.True(first.Succeeded, Failure(first));
            Assert.True(second.Succeeded, Failure(second));
            Assert.False(second.FromCache);
            Assert.NotEqual(first.AssemblyPath, second.AssemblyPath);
        }

        [Fact]
        public void ASourceMovedToAnotherFolder_GivesANewKey()
        {
            string project = Project("key-moved", SourceProject.Csproj(), "Stairs.cs", SourceProject.Stairs());
            var compiler = new CadPluginCompiler(Cache);
            CadPluginBuild first = compiler.Compile("Stairs", project);
            string folder = Path.GetDirectoryName(project);
            Directory.CreateDirectory(Path.Combine(folder, "Sub"));
            File.Move(Path.Combine(folder, "Stairs.cs"), Path.Combine(folder, "Sub", "Stairs.cs"));

            CadPluginBuild second = compiler.Compile("Stairs", project);

            Assert.True(second.Succeeded, Failure(second));
            Assert.False(second.FromCache);
            Assert.NotEqual(first.AssemblyPath, second.AssemblyPath);
        }

        [Fact]
        public void AHintPathLibraryChanged_GivesANewKey()
        {
            string library = Library("key-library", "StairsMath.dll");
            string project = Project(
                "key-library",
                SourceProject.Csproj(
                    items: new[] { "<Reference Include=\"StairsMath\" HintPath=\"StairsMath.dll\" />" }
                ),
                "Stairs.cs",
                SourceProject.Command()
            );
            var compiler = new CadPluginCompiler(Cache);
            CadPluginBuild first = compiler.Compile("Stairs", project);
            Assert.True(compiler.Compile("Stairs", project).FromCache);
            File.AppendAllText(library, "x");

            CadPluginBuild second = compiler.Compile("Stairs", project);

            Assert.True(first.Succeeded, Failure(first));
            Assert.False(second.FromCache);
        }

        [Fact]
        public void AnotherAppVersion_GivesANewKey()
        {
            string project = Project("version-key", SourceProject.Csproj(), "Stairs.cs", SourceProject.Stairs());
            CadPluginBuild first = new CadPluginCompiler(Cache, "1.0.0").Compile("Stairs", project);

            CadPluginBuild same = new CadPluginCompiler(Cache, "1.0.0").Compile("Stairs", project);
            CadPluginBuild next = new CadPluginCompiler(Cache, "1.0.1").Compile("Stairs", project);

            Assert.True(same.FromCache);
            Assert.False(next.FromCache);
            Assert.NotEqual(first.AssemblyPath, next.AssemblyPath);
        }

        [Fact]
        public void ABuildTakenFromTheCache_DoesNotLoadTheCompiler()
        {
            string project = Project(
                "no-roslyn",
                SourceProject.Csproj(new[] { "<LangVersion>latest</LangVersion>", "<Nullable>enable</Nullable>" }),
                "StairsCommands.cs",
                SourceProject.Command(),
                "Steps.cs",
                SourceProject.StepsSource
            );
            Assert.True(new CadPluginCompiler(Cache).Compile("Stairs", project).Succeeded);

            // OpenCAD2D.Wpf loaded again into a context that writes down every assembly it is asked for: the compiler
            // (Microsoft.CodeAnalysis) loaded in the default context already would be asked for again here.
            var context = new Recording();

            try
            {
                Assembly wpf = context.LoadFromAssemblyPath(typeof(CadPluginCompiler).Assembly.Location);
                Type type = wpf.GetType(typeof(CadPluginCompiler).FullName, true);
                object compiler = Activator.CreateInstance(type, Cache);
                object build = type.GetMethod("Compile").Invoke(compiler, new object[] { "Stairs", project });

                Assert.True((bool)build.GetType().GetProperty("FromCache").GetValue(build));
                Assert.DoesNotContain(context.Asked, n =>
                    n.StartsWith("Microsoft.CodeAnalysis", StringComparison.Ordinal)
                );
            }
            finally
            {
                context.Unload();
            }
        }

        private sealed class Recording : AssemblyLoadContext
        {
            public Recording()
                : base("recording", true) { }

            public List<string> Asked { get; } = new List<string>();

            protected override Assembly Load(AssemblyName assemblyName)
            {
                Asked.Add(assemblyName.Name);
                return null;
            }
        }
    }
}
#endif
