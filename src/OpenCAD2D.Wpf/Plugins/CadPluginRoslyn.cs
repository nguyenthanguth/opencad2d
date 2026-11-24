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
    /// The one place that touches the compiler (Roslyn), reached only when a plugin's sources are not in the cache, so
    /// that a start whose plugins are all cached never loads it.
    /// </summary>
    internal static class CadPluginRoslyn
    {
        /// <summary>The unification warnings of compiling against the runtime's own assemblies, as dotnet-script drops them.</summary>
        private static readonly string[] Suppressed = { "CS1701", "CS1702", "CS1705" };

        private static readonly Lazy<List<MetadataReference>> Framework = new Lazy<List<MetadataReference>>(
            FrameworkReferences
        );

        /// <summary>Compiles the sources, each a syntax tree of its own path, into an assembly named after the plugin and a portable PDB that embeds them.</summary>
        internal static CadPluginEmit Compile(string name, List<SourceFile> files, List<string> references)
        {
            var parse = new CSharpParseOptions(LanguageVersion.Latest);
            var trees = new List<SyntaxTree>();

            foreach (SourceFile file in files)
            {
                SourceText text = SourceText.From(
                    file.Bytes,
                    file.Bytes.Length,
                    Encoding.UTF8,
                    SourceHashAlgorithm.Sha256,
                    false,
                    true
                );
                trees.Add(CSharpSyntaxTree.ParseText(text, parse, file.Path));
            }

            var all = new List<MetadataReference>(Framework.Value);

            foreach (string reference in references)
            {
                all.Add(MetadataReference.CreateFromFile(reference));
            }

            var options = new CSharpCompilationOptions(
                OutputKind.DynamicallyLinkedLibrary,
                optimizationLevel: OptimizationLevel.Release,
                nullableContextOptions: NullableContextOptions.Disable,
                deterministic: true,
                allowUnsafe: false,
                specificDiagnosticOptions: Suppressed.Select(id => new KeyValuePair<string, ReportDiagnostic>(
                    id,
                    ReportDiagnostic.Suppress
                ))
            );
            CSharpCompilation compilation = CSharpCompilation.Create(name, trees, all, options);
            var emit = new CadPluginEmit();

            using (var assembly = new MemoryStream())
            using (var pdb = new MemoryStream())
            {
                EmitResult result = compilation.Emit(
                    assembly,
                    pdb,
                    options: new EmitOptions(
                        debugInformationFormat: DebugInformationFormat.PortablePdb,
                        pdbFilePath: name + ".pdb"
                    ),
                    embeddedTexts: trees.Select(t => EmbeddedText.FromSource(t.FilePath, t.GetText()))
                );

                foreach (Diagnostic diagnostic in result.Diagnostics)
                {
                    if (diagnostic.Severity == DiagnosticSeverity.Error)
                    {
                        emit.Errors.Add(Error(diagnostic));
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

        private static CadPluginError Error(Diagnostic diagnostic)
        {
            string message = diagnostic.GetMessage(CultureInfo.InvariantCulture);

            if (!diagnostic.Location.IsInSource)
            {
                return new CadPluginError(null, 0, 0, diagnostic.Id, message);
            }

            FileLinePositionSpan span = diagnostic.Location.GetLineSpan();
            return new CadPluginError(
                Path.GetFileName(span.Path),
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
