#if NET8_0_OR_GREATER
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using NuGet.Frameworks;
using NuGet.Packaging;
using NuGet.Versioning;
using Xunit;

namespace OpenCAD2D.Plugins.UnitTest
{
    /// <summary>
    /// A folder of .nupkg files made in code, which stands for nuget.org in the tests of packages: no test reaches the
    /// network. A package is its id and version, the files it holds (a library compiled in the test, a native file copied
    /// from Windows) and what it needs.
    /// </summary>
    internal sealed class SourcePluginFeed
    {
        public SourcePluginFeed(string folder)
        {
            Folder = folder;
            Directory.CreateDirectory(folder);
        }

        public string Folder { get; }

        /// <summary>One package a package needs: <c>Dep("Foo", "1.0.0")</c> is "Foo 1.0.0 or higher".</summary>
        public static (string Id, string Range) Dep(string id, string range) => (id, range);

        /// <summary>Adds a package with the files given as (path in the package, bytes) and the dependencies given, all in the group for net8.0.</summary>
        public void Add(
            string id,
            string version,
            (string Path, byte[] Bytes)[] files = null,
            params (string Id, string Range)[] needs
        )
        {
            var builder = new PackageBuilder
            {
                Id = id,
                Version = NuGetVersion.Parse(version),
                Description = id,
            };
            builder.Authors.Add("tests");

            if (needs.Length > 0)
            {
                builder.DependencyGroups.Add(
                    new PackageDependencyGroup(
                        NuGetFramework.Parse("net8.0"),
                        needs.Select(n => new NuGet.Packaging.Core.PackageDependency(n.Id, VersionRange.Parse(n.Range)))
                    )
                );
            }

            foreach ((string path, byte[] bytes) in files ?? Array.Empty<(string, byte[])>())
            {
                builder.Files.Add(new PhysicalPackageFile(new MemoryStream(bytes)) { TargetPath = path });
            }

            using (FileStream stream = File.Create(Path.Combine(Folder, id + "." + version + ".nupkg")))
            {
                builder.Save(stream);
            }
        }

        /// <summary>Adds a package of one library: the class <c>Foo.Foo</c> of an assembly named <paramref name="id"/>, its Version property giving <paramref name="text"/>, in the folder of the framework given.</summary>
        public void AddLibrary(
            string id,
            string version,
            string text,
            string folder = "lib/netstandard2.0",
            params (string Id, string Range)[] needs
        ) => Add(id, version, new[] { (folder + "/" + id + ".dll", Library(id, version, Class(id, text))) }, needs);

        /// <summary>A public static class of the package's name in the namespace of the same, with a string property Version.</summary>
        public static string Class(string id, string text) =>
            "namespace "
            + id
            + " { public static class "
            + id
            + " { public static string Version => \""
            + text
            + "\"; } }\n";

        /// <summary>Compiles C# source into an assembly of a name and version; the bytes.</summary>
        public static byte[] Library(string name, string version, string code)
        {
            string runtime = Path.GetDirectoryName(typeof(object).Assembly.Location);
            var references = new[]
            {
                typeof(object).Assembly.Location,
                Path.Combine(runtime, "System.Runtime.dll"),
                Path.Combine(runtime, "System.Runtime.InteropServices.dll"),
            }.Select(p => (MetadataReference)MetadataReference.CreateFromFile(p));
            // The version in a tree of its own: an assembly attribute must come before the code's own using lines.
            SyntaxTree[] trees =
            {
                CSharpSyntaxTree.ParseText("[assembly: System.Reflection.AssemblyVersion(\"" + version + ".0\")]\n"),
                CSharpSyntaxTree.ParseText(code),
            };
            CSharpCompilation compilation = CSharpCompilation.Create(
                name,
                trees,
                references,
                new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary)
            );

            using (var stream = new MemoryStream())
            {
                var result = compilation.Emit(stream);
                Assert.True(result.Success, string.Join("\n", result.Diagnostics));
                return stream.ToArray();
            }
        }

        /// <summary>A native file for a package: a copy of a DLL of Windows, which a library of the package calls under the name the package gives it.</summary>
        public static byte[] Native(string windowsDll) =>
            File.ReadAllBytes(Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.System), windowsDll));
    }
}
#endif
