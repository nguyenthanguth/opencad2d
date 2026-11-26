#if NET8_0_OR_GREATER
using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Text;
using GeometryHelper;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.Emit;
using Microsoft.CodeAnalysis.Text;
using OpenCAD2D.Plugins;

namespace OpenCAD2D.Wpf.Plugins
{
    /// <summary>
    /// The one place that touches the compiler (Roslyn), reached only when a plugin's build is not in the cache, so
    /// that a start whose plugins are all cached never loads it.
    /// </summary>
    internal static class CadPluginRoslyn
    {
        /// <summary>The unification warnings of compiling against the runtime's own assemblies, as dotnet-script drops them.</summary>
        private static readonly string[] Suppressed = { "CS1701", "CS1702", "CS1705" };

        private static readonly Lazy<List<MetadataReference>> Framework = new Lazy<List<MetadataReference>>(
            FrameworkReferences
        );

        /// <summary>
        /// Compiles a project's sources, each a syntax tree of its own path, into an assembly named as the project says and a
        /// portable PDB that embeds them. The project's global usings and the assembly attributes the SDK would generate are
        /// two more trees of their own, put first. The assemblies of the plugin's packages are referenced after those its
        /// <c>Reference</c> items name.
        /// </summary>
        internal static CadPluginEmit Compile(CadPluginProject project, IReadOnlyList<string> packageReferences)
        {
            var emit = new CadPluginEmit();
            LanguageVersion language = LanguageVersion.Latest;

            if (project.LangVersion != null && !LanguageVersionFacts.TryParse(project.LangVersion, out language))
            {
                emit.Errors.Add(
                    new CadPluginError(
                        project.FileName,
                        project.LangVersionLine,
                        project.LangVersionColumn,
                        "CS1617",
                        "Invalid option '"
                            + project.LangVersion
                            + "' for /langversion. Use '/langversion:?' to list supported values."
                    )
                );
                return emit;
            }

            var parse = new CSharpParseOptions(language, preprocessorSymbols: project.Symbols);
            var trees = new List<SyntaxTree>();

            if (project.Usings.Count > 0)
            {
                trees.Add(Generated(project, ".GlobalUsings.g.cs", string.Join("\n", project.Usings) + "\n", parse));
            }

            if (project.AssemblyInfo != null)
            {
                trees.Add(Generated(project, ".AssemblyInfo.g.cs", project.AssemblyInfo, parse));
            }

            foreach (SourceFile file in project.Sources)
            {
                trees.Add(CSharpSyntaxTree.ParseText(Text(file.Bytes), parse, file.Path));
            }

            var all = new List<MetadataReference>(Framework.Value);

            foreach (string library in project.Libraries)
            {
                all.Add(MetadataReference.CreateFromFile(library));
            }

            // An assembly the framework references have under the same name is not added twice (CS1703).
            var known = new HashSet<string>(
                Framework.Value.Select(r =>
                    Path.GetFileNameWithoutExtension(((PortableExecutableReference)r).FilePath)
                ),
                StringComparer.OrdinalIgnoreCase
            );

            foreach (string reference in packageReferences)
            {
                if (known.Add(Path.GetFileNameWithoutExtension(reference)))
                {
                    all.Add(MetadataReference.CreateFromFile(reference));
                }
            }

            var options = new CSharpCompilationOptions(
                OutputKind.DynamicallyLinkedLibrary,
                optimizationLevel: OptimizationLevel.Release,
                nullableContextOptions: Nullable(project.Nullable),
                deterministic: true,
                allowUnsafe: project.AllowUnsafe,
                specificDiagnosticOptions: Suppressed.Select(id => new KeyValuePair<string, ReportDiagnostic>(
                    id,
                    ReportDiagnostic.Suppress
                ))
            );
            CSharpCompilation compilation = CSharpCompilation.Create(project.AssemblyName, trees, all, options);

            using (var assembly = new MemoryStream())
            using (var pdb = new MemoryStream())
            {
                EmitResult result = compilation.Emit(
                    assembly,
                    pdb,
                    options: new EmitOptions(
                        debugInformationFormat: DebugInformationFormat.PortablePdb,
                        pdbFilePath: project.AssemblyName + ".pdb"
                    ),
                    embeddedTexts: trees.Select(t => EmbeddedText.FromSource(t.FilePath, t.GetText()))
                );

                foreach (Diagnostic diagnostic in result.Diagnostics)
                {
                    if (diagnostic.Severity == DiagnosticSeverity.Error)
                    {
                        emit.Errors.Add(Error(diagnostic, project.Folder));
                    }
                }

                if (emit.Errors.Count == 0)
                {
                    emit.Assembly = assembly.ToArray();
                    emit.Pdb = pdb.ToArray();
                }
            }

            return emit;
        }

        private static NullableContextOptions Nullable(string value)
        {
            switch (value)
            {
                case "enable":
                    return NullableContextOptions.Enable;
                case "warnings":
                    return NullableContextOptions.Warnings;
                case "annotations":
                    return NullableContextOptions.Annotations;
                default:
                    return NullableContextOptions.Disable;
            }
        }

        /// <summary>A source the project generates, as a tree of a path in the plugin's folder named after the project and not on disk.</summary>
        private static SyntaxTree Generated(
            CadPluginProject project,
            string suffix,
            string text,
            CSharpParseOptions parse
        ) =>
            CSharpSyntaxTree.ParseText(
                Text(Encoding.UTF8.GetBytes(text)),
                parse,
                Path.Combine(project.Folder, Path.GetFileNameWithoutExtension(project.FileName) + suffix)
            );

        private static SourceText Text(byte[] bytes) =>
            SourceText.From(bytes, bytes.Length, Encoding.UTF8, SourceHashAlgorithm.Sha256, false, true);

        private static CadPluginError Error(Diagnostic diagnostic, string folder)
        {
            string message = diagnostic.GetMessage(CultureInfo.InvariantCulture);

            if (!diagnostic.Location.IsInSource)
            {
                return new CadPluginError(null, 0, 0, diagnostic.Id, message);
            }

            FileLinePositionSpan span = diagnostic.Location.GetLineSpan();
            return new CadPluginError(
                span.Path.StartsWith(folder + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase)
                    ? Path.GetRelativePath(folder, span.Path)
                    : Path.GetFileName(span.Path),
                span.StartLinePosition.Line + 1,
                span.StartLinePosition.Character + 1,
                diagnostic.Id,
                message
            );
        }

        /// <summary>
        /// Every assembly of the two shared frameworks the app runs on (.NET and the Windows desktop, which holds WPF), then
        /// GeometryHelper, OpenCAD2D.Core and OpenCAD2D.Wpf, sorted by path. Each is mapped into memory rather than read: 51 MB
        /// of private bytes after a compile against 198 MB.
        /// </summary>
        private static List<MetadataReference> FrameworkReferences()
        {
            var paths = new List<string>();

            foreach (
                string folder in new[]
                {
                    Path.GetDirectoryName(typeof(object).Assembly.Location),
                    Path.GetDirectoryName(typeof(System.Windows.Window).Assembly.Location),
                }.Distinct(StringComparer.OrdinalIgnoreCase)
            )
            {
                foreach (string file in Directory.GetFiles(folder, "*.dll"))
                {
                    if (IsAssembly(file))
                    {
                        paths.Add(file);
                    }
                }
            }

            foreach (
                Assembly own in new[]
                {
                    typeof(GeometryHelperLog).Assembly,
                    typeof(CadPlugin).Assembly,
                    typeof(CadPluginRoslyn).Assembly,
                    typeof(SkiaCanvasRenderer).Assembly,
                }
            )
            {
                if (!paths.Exists(p => string.Equals(p, own.Location, StringComparison.OrdinalIgnoreCase)))
                {
                    paths.Add(own.Location);
                }
            }

            paths.Sort(StringComparer.OrdinalIgnoreCase);
            var references = new List<MetadataReference>(paths.Count);

            foreach (string path in paths)
            {
                references.Add(AssemblyMetadata.CreateFromFile(path).GetReference(filePath: path));
            }

            return references;
        }

        private static bool IsAssembly(string path)
        {
            try
            {
                AssemblyName.GetAssemblyName(path);
                return true;
            }
            catch (Exception exception)
                when (exception is BadImageFormatException || exception is FileLoadException || exception is IOException
                )
            {
                return false;
            }
        }
    }
}
#endif
