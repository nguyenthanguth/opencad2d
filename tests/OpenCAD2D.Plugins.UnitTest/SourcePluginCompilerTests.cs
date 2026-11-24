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
    /// The compiler of plugins given as C# source (<see cref="CadPluginCompiler"/>, .NET 8 only): one file and two, a
    /// <c>//#r</c> reference, the errors as the command line writes them, and the cache keyed by the sources.
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

        /// <summary>A plugin with one command STAIRS that writes how many steps a flight of 3 m has; <paramref name="steps"/> is the class it takes the count from.</summary>
        private static string Command(string steps = "Steps") =>
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
            + "            c.Write(\"Steps: \" + "
            + steps
            + ".Count(3000.0));\n"
            + "            yield break;\n"
            + "        }\n"
            + "    }\n"
            + "}\n";

        private const string StepsSource =
            "namespace Stairs\n{\n    public static class Steps\n    {\n        public static int Count(double rise) => (int)System.Math.Ceiling(rise / 175.0);\n    }\n}\n";

        private string Write(string folder, string file, string text)
        {
            string dir = Path.Combine(_files.Root, folder);
            Directory.CreateDirectory(dir);
            string path = Path.Combine(dir, file);
            File.WriteAllText(path, text, new UTF8Encoding(false));
            return path;
        }

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

        [Fact]
        public void OneFile_CompilesIntoAnAssemblyNamedAfterThePlugin_WithItsPdb()
        {
            string source = Write(
                "one",
                "Stairs.cs",
                Command("Stairs.Local")
                    .Replace(
                        "namespace Stairs\n{\n",
                        "namespace Stairs\n{\n    public static class Local\n    {\n        public static int Count(double rise) => (int)System.Math.Ceiling(rise / 175.0);\n    }\n\n"
                    )
            );
            var compiler = new CadPluginCompiler(Cache);

            CadPluginBuild build = compiler.Compile("Stairs", new[] { source });

            Assert.True(build.Succeeded, string.Join("\n", build.Errors));
            Assert.False(build.FromCache);
            Assert.Equal(0, build.ErrorCount);
            Assert.True(File.Exists(build.AssemblyPath));
            Assert.True(File.Exists(build.PdbPath));
            Assert.Equal(Path.Combine(Cache, "Stairs"), Path.GetDirectoryName(build.AssemblyPath));
            Assert.Equal("Stairs", AssemblyName.GetAssemblyName(build.AssemblyPath).Name);
            Assert.Equal(new[] { "Stairs.Local", "Stairs.StairsCommands" }, TypesOf(build.AssemblyPath));
        }

        [Fact]
        public void TwoFiles_AreCompiledTogether_OneUsingTheOther()
        {
            string commands = Write("two", "StairsCommands.cs", Command());
            string steps = Write("two", "Steps.cs", StepsSource);

            CadPluginBuild build = new CadPluginCompiler(Cache).Compile("Stairs", new[] { steps, commands });

            Assert.True(build.Succeeded, string.Join("\n", build.Errors));
            Assert.Equal(new[] { "Stairs.StairsCommands", "Stairs.Steps" }, TypesOf(build.AssemblyPath));
        }

        [Fact]
        public void AReferenceLine_AddsTheDllBesideTheSource()
        {
            var compiler = new CadPluginCompiler(Cache);
            CadPluginBuild helper = compiler.Compile("StairsMath", new[] { Write("math", "Steps.cs", StepsSource) });
            Assert.True(helper.Succeeded, string.Join("\n", helper.Errors));
            string folder = Path.Combine(_files.Root, "with-ref");
            Directory.CreateDirectory(folder);
            File.Copy(helper.AssemblyPath, Path.Combine(folder, "StairsMath.dll"));
            string source = Write("with-ref", "Stairs.cs", "//#r \"StairsMath.dll\"\n// The flight.\n" + Command());

            CadPluginBuild build = compiler.Compile("Stairs", new[] { source });

            Assert.True(build.Succeeded, string.Join("\n", build.Errors));
            Assert.Equal(new[] { "Stairs.StairsCommands" }, TypesOf(build.AssemblyPath));
        }

        [Fact]
        public void WithoutTheReference_TheSameSourceDoesNotCompile()
        {
            string source = Write("no-ref", "Stairs.cs", Command());

            CadPluginBuild build = new CadPluginCompiler(Cache).Compile("Stairs", new[] { source });

            Assert.False(build.Succeeded);
            Assert.Equal(
                new[] { "Stairs.cs(12,33): error CS0103: The name 'Steps' does not exist in the current context" },
                build.Errors
            );
        }

        [Fact]
        public void AReferenceThatIsMissingOrOutsideTheFolder_IsAnErrorOfItsLine()
        {
            string source = Write(
                "bad-ref",
                "Stairs.cs",
                "// Two references.\n//#r \"Missing.dll\"\n//#r \"../Other.dll\"\n" + Command()
            );

            CadPluginBuild build = new CadPluginCompiler(Cache).Compile("Stairs", new[] { source });

            Assert.False(build.Succeeded);
            Assert.Equal(
                new[]
                {
                    "Stairs.cs(2,1): error CS0006: Metadata file 'Missing.dll' could not be found",
                    "Stairs.cs(3,1): error OC0001: //#r takes a .dll beside the source: '../Other.dll'",
                },
                build.Errors
            );
            Assert.False(Directory.Exists(Cache));
        }

        [Fact]
        public void AnError_IsWrittenAsFileLineColumnAndId_AndNothingIsCached()
        {
            var lines = new List<string>(Command().Split('\n'));
            lines[11] = "        x = 1;";
            string source = Write("error", "Stairs.cs", string.Join("\n", lines));

            CadPluginBuild build = new CadPluginCompiler(Cache).Compile("Stairs", new[] { source });

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
            string b = Write("many", "B.cs", body.ToString());
            string a = Write("many", "A.cs", "namespace Stairs { class A { int M() => y; } }\n");

            CadPluginBuild build = new CadPluginCompiler(Cache).Compile("Stairs", new[] { b, a });

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

        [Fact]
        public void ASecondCompileOfUnchangedSources_IsTakenFromTheCache()
        {
            string commands = Write("cached", "StairsCommands.cs", Command());
            string steps = Write("cached", "Steps.cs", StepsSource);
            var compiler = new CadPluginCompiler(Cache);
            var clock = Stopwatch.StartNew();
            CadPluginBuild first = compiler.Compile("Stairs", new[] { commands, steps });
            long compiled = clock.ElapsedMilliseconds;
            DateTime written = File.GetLastWriteTimeUtc(first.AssemblyPath);

            clock.Restart();
            CadPluginBuild second = new CadPluginCompiler(Cache).Compile("Stairs", new[] { steps, commands });
            long cached = clock.ElapsedMilliseconds;
            _output.WriteLine("compiled in " + compiled + " ms, taken from the cache in " + cached + " ms");

            Assert.True(first.Succeeded, string.Join("\n", first.Errors));
            Assert.False(first.FromCache);
            Assert.True(second.FromCache);
            Assert.Equal(first.AssemblyPath, second.AssemblyPath);
            Assert.Equal(written, File.GetLastWriteTimeUtc(second.AssemblyPath));
        }

        [Fact]
        public void OneSourceByteChanged_GivesANewKey_AndTheOldBuildIsDeleted()
        {
            string commands = Write("changed", "StairsCommands.cs", Command());
            string steps = Write("changed", "Steps.cs", StepsSource);
            var compiler = new CadPluginCompiler(Cache);
            CadPluginBuild first = compiler.Compile("Stairs", new[] { commands, steps });
            File.WriteAllText(steps, StepsSource.Replace("175.0", "176.0"), new UTF8Encoding(false));

            CadPluginBuild second = compiler.Compile("Stairs", new[] { commands, steps });

            Assert.True(second.Succeeded, string.Join("\n", second.Errors));
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
        public void AnotherAppVersion_GivesANewKey()
        {
            string commands = Write("version", "StairsCommands.cs", Command());
            string steps = Write("version", "Steps.cs", StepsSource);
            CadPluginBuild first = new CadPluginCompiler(Cache, "1.0.0").Compile("Stairs", new[] { commands, steps });

            CadPluginBuild same = new CadPluginCompiler(Cache, "1.0.0").Compile("Stairs", new[] { commands, steps });
            CadPluginBuild next = new CadPluginCompiler(Cache, "1.0.1").Compile("Stairs", new[] { commands, steps });

            Assert.True(same.FromCache);
            Assert.False(next.FromCache);
            Assert.NotEqual(first.AssemblyPath, next.AssemblyPath);
        }

        [Fact]
        public void ABuildTakenFromTheCache_DoesNotLoadTheCompiler()
        {
            string commands = Write("no-roslyn", "StairsCommands.cs", Command());
            string steps = Write("no-roslyn", "Steps.cs", StepsSource);
            Assert.True(new CadPluginCompiler(Cache).Compile("Stairs", new[] { commands, steps }).Succeeded);

            // OpenCAD2D.Wpf loaded again into a context that writes down every assembly it is asked for: the compiler
            // (Microsoft.CodeAnalysis) loaded in the default context already would be asked for again here.
            var context = new Recording();

            try
            {
                Assembly wpf = context.LoadFromAssemblyPath(typeof(CadPluginCompiler).Assembly.Location);
                Type type = wpf.GetType(typeof(CadPluginCompiler).FullName, true);
                object compiler = Activator.CreateInstance(type, Cache);
                object build = type.GetMethod("Compile")
                    .Invoke(compiler, new object[] { "Stairs", new[] { commands, steps } });

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
