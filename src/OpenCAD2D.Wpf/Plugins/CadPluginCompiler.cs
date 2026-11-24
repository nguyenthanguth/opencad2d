#if NET8_0_OR_GREATER
using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Reflection;
using System.Security.Cryptography;
using System.Text;
using OpenCAD2D.Plugins;

namespace OpenCAD2D.Wpf.Plugins
{
    /// <summary>
    /// Compiles a plugin given as C# source, one .cs file or the .cs files of one folder compiled together, into an assembly
    /// and its PDB, and keeps the build in a cache keyed by the SHA-256 of the sources, so that a start with unchanged
    /// sources compiles nothing. A plugin of two files, 220 lines in all, compiles in about 0.9 s the first time in a
    /// session (the compiler's own start) and in about 20 ms after that; taken from the cache it does not load the compiler.
    /// </summary>
    /// <remarks>
    /// The sources are compiled against the .NET 8 runtime the app runs on (its own assemblies and WPF's), GeometryHelper,
    /// OpenCAD2D.Core and OpenCAD2D.Wpf, and the .dll files a <c>//#r "Name.dll"</c> line at the top of a file names beside
    /// it. C# is the latest the compiler knows, nullable off, no implicit usings; warnings are dropped. The cache key takes
    /// the app's version, the runtime's version, each source's name and bytes, and each <c>//#r</c> file's name and bytes.
    /// </remarks>
    public sealed class CadPluginCompiler
    {
        /// <summary>The most error lines a failed build gives; <see cref="CadPluginBuild.ErrorCount"/> counts them all.</summary>
        public const int MaxErrors = 20;

        private const string Directive = "//#r";

        private readonly string _appVersion;

        /// <summary>Initializes a compiler that caches its builds in a folder.</summary>
        /// <param name="cacheFolder">The cache folder; the app gives <see cref="DefaultCacheFolder"/>.</param>
        public CadPluginCompiler(string cacheFolder)
            : this(cacheFolder, AppVersion()) { }

        /// <summary>Initializes a compiler with the app's version given, which the tests change.</summary>
        internal CadPluginCompiler(string cacheFolder, string appVersion)
        {
            if (string.IsNullOrEmpty(cacheFolder))
            {
                throw new ArgumentException("A cache folder is needed.", nameof(cacheFolder));
            }

            CacheFolder = Path.GetFullPath(cacheFolder);
            _appVersion = appVersion ?? string.Empty;
        }

        /// <summary>Gets the cache folder of the app: <c>%LocalAppData%\OpenCAD2D\PluginCache</c>.</summary>
        public static string DefaultCacheFolder =>
            Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                "OpenCAD2D",
                "PluginCache"
            );

        /// <summary>Gets the folder the builds are cached in, one folder below it per plugin.</summary>
        public string CacheFolder { get; }

        /// <summary>
        /// Compiles a plugin's sources, or takes the build from the cache where they are unchanged since the last compile.
        /// A plugin Stairs of one file Stairs.cs gives <c>&lt;cache&gt;\Stairs\&lt;key&gt;.dll</c> and its .pdb; a reference to
        /// an unknown name on line 12, column 9 gives a failed build with the error
        /// <c>Stairs.cs(12,9): error CS0103: The name 'x' does not exist in the current context</c>.
        /// </summary>
        /// <param name="name">The plugin's name, which names the assembly and its folder in the cache.</param>
        /// <param name="sources">The full paths of the .cs files, all in one folder.</param>
        /// <returns>The assembly built or found in the cache, or the errors that kept it from being built.</returns>
        /// <exception cref="ArgumentException">There is no name or no source.</exception>
        public CadPluginBuild Compile(string name, IReadOnlyList<string> sources)
        {
            if (string.IsNullOrEmpty(name))
            {
                throw new ArgumentException("A plugin's name is needed.", nameof(name));
            }

            if (sources == null || sources.Count == 0)
            {
                throw new ArgumentException("A source plugin has a .cs file at least.", nameof(sources));
            }

            var files = new List<SourceFile>();

            foreach (string path in sources)
            {
                files.Add(new SourceFile(Path.GetFullPath(path), File.ReadAllBytes(path)));
            }

            files.Sort((a, b) => string.CompareOrdinal(a.Name, b.Name));
            var errors = new List<CadPluginError>();
            List<string> references = ReadReferences(files, errors);

            if (errors.Count > 0)
            {
                return Failed(errors);
            }

            string key = Key(files, references);
            string folder = Path.Combine(CacheFolder, Safe(name));
            string dll = Path.Combine(folder, key + ".dll");
            string pdb = Path.Combine(folder, key + ".pdb");

            // The .pdb is written first and the .dll last, so a .dll in the cache always has its .pdb.
            if (File.Exists(dll) && File.Exists(pdb))
            {
                return new CadPluginBuild(dll, pdb, true);
            }

            CadPluginEmit emit = CadPluginRoslyn.Compile(name, files, references);

            if (emit.Errors.Count > 0)
            {
                return Failed(emit.Errors);
            }

            Directory.CreateDirectory(folder);
            Write(pdb, emit.Pdb);
            Write(dll, emit.Assembly);
            Prune(folder, key);
            return new CadPluginBuild(dll, pdb, false);
        }

        /// <summary>
        /// The cache key, 64 hex digits: the SHA-256 of a format tag, the app's version, the runtime's version, then each
        /// source (its name, its length, its bytes) in ordinal order of the names, then each <c>//#r</c> file (its name, its
        /// bytes) in ordinal order of the paths.
        /// </summary>
        internal string Key(IReadOnlyList<SourceFile> files, IReadOnlyList<string> references)
        {
            using (var hash = IncrementalHash.CreateHash(HashAlgorithmName.SHA256))
            {
                AppendText(hash, "cs1");
                AppendText(hash, _appVersion);
                AppendText(hash, Environment.Version.ToString());

                foreach (SourceFile file in files)
                {
                    AppendText(hash, file.Name);
                    AppendText(hash, file.Bytes.Length.ToString(CultureInfo.InvariantCulture));
                    hash.AppendData(file.Bytes);
                }

                foreach (string reference in references)
                {
                    byte[] bytes = File.ReadAllBytes(reference);
                    AppendText(hash, Path.GetFileName(reference));
                    AppendText(hash, bytes.Length.ToString(CultureInfo.InvariantCulture));
                    hash.AppendData(bytes);
                }

                return Convert.ToHexString(hash.GetHashAndReset());
            }
        }

        /// <summary>
        /// The .dll files the <c>//#r</c> lines at the top of the sources name, full paths sorted ordinal and each once. A line
        /// counts while no line but an empty one, a <c>//</c> comment or a <c>using</c> has come before it; a path that leaves
        /// the source's folder, is not a .dll or is missing is an error of the line.
        /// </summary>
        private static List<string> ReadReferences(List<SourceFile> files, List<CadPluginError> errors)
        {
            var references = new List<string>();

            foreach (SourceFile file in files)
            {
                string folder = Path.GetDirectoryName(file.Path);
                string[] lines = Encoding.UTF8.GetString(file.Bytes).Split('\n');

                for (int i = 0; i < lines.Length; i++)
                {
                    string line = lines[i].Trim().TrimStart('﻿');

                    if (line.StartsWith(Directive, StringComparison.Ordinal))
                    {
                        string target = line.Substring(Directive.Length).Trim().Trim('"');
                        string path = Reference(folder, target);

                        if (path == null)
                        {
                            errors.Add(
                                new CadPluginError(
                                    file.Name,
                                    i + 1,
                                    1,
                                    "OC0001",
                                    "//#r takes a .dll beside the source: '" + target + "'"
                                )
                            );
                        }
                        else if (!File.Exists(path))
                        {
                            errors.Add(
                                new CadPluginError(
                                    file.Name,
                                    i + 1,
                                    1,
                                    "CS0006",
                                    "Metadata file '" + target + "' could not be found"
                                )
                            );
                        }
                        else if (!references.Exists(r => string.Equals(r, path, StringComparison.OrdinalIgnoreCase)))
                        {
                            references.Add(path);
                        }
                    }
                    else if (
                        line.Length > 0
                        && !line.StartsWith("//", StringComparison.Ordinal)
                        && !line.StartsWith("using ", StringComparison.Ordinal)
                    )
                    {
                        break;
                    }
                }
            }

            references.Sort(StringComparer.OrdinalIgnoreCase);
            return references;
        }

        /// <summary>The full path a <c>//#r</c> names in a source's folder; null for one that is rooted, leaves the folder or is not a .dll.</summary>
        private static string Reference(string folder, string target)
        {
            if (
                target.Length == 0
                || Path.IsPathRooted(target)
                || target.IndexOfAny(Path.GetInvalidPathChars()) >= 0
                || !string.Equals(Path.GetExtension(target), ".dll", StringComparison.OrdinalIgnoreCase)
            )
            {
                return null;
            }

            string path = Path.GetFullPath(Path.Combine(folder, target));
            return path.StartsWith(folder + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase)
                ? path
                : null;
        }

        private static CadPluginBuild Failed(List<CadPluginError> errors)
        {
            errors.Sort();
            var lines = new List<string>();

            for (int i = 0; i < errors.Count && i < MaxErrors; i++)
            {
                lines.Add(errors[i].ToString());
            }

            return new CadPluginBuild(errors.Count, lines);
        }

        /// <summary>Writes a file through a temporary name, so that a file cut short is never taken from the cache.</summary>
        private static void Write(string path, byte[] bytes)
        {
            string temporary = path + "." + Guid.NewGuid().ToString("N") + ".tmp";
            File.WriteAllBytes(temporary, bytes);
            File.Move(temporary, path, true);
        }

        /// <summary>Deletes the other builds of a plugin in the cache; one in use stays.</summary>
        private static void Prune(string folder, string key)
        {
            foreach (string file in Directory.GetFiles(folder))
            {
                if (Path.GetFileName(file).StartsWith(key + ".", StringComparison.OrdinalIgnoreCase))
                {
                    continue;
                }

                try
                {
                    File.Delete(file);
                }
                catch (Exception exception) when (exception is IOException || exception is UnauthorizedAccessException)
                {
                    // In use or read-only: the next build tries again.
                }
            }
        }

        private static void AppendText(IncrementalHash hash, string text)
        {
            hash.AppendData(Encoding.UTF8.GetBytes(text));
            hash.AppendData(new byte[] { 0 });
        }

        private static string Safe(string name)
        {
            var safe = new StringBuilder(name.Length);

            foreach (char c in name)
            {
                safe.Append(Array.IndexOf(Path.GetInvalidFileNameChars(), c) >= 0 ? '_' : c);
            }

            return safe.ToString();
        }

        private static string AppVersion()
        {
            Assembly assembly = typeof(CadPluginCompiler).Assembly;
            return assembly.GetCustomAttribute<AssemblyInformationalVersionAttribute>()?.InformationalVersion
                ?? assembly.GetName().Version?.ToString()
                ?? string.Empty;
        }
    }

    /// <summary>One source of a plugin: its full path, its name and its bytes as read once.</summary>
    internal sealed class SourceFile
    {
        internal SourceFile(string path, byte[] bytes)
        {
            Path = path;
            Name = System.IO.Path.GetFileName(path);
            Bytes = bytes;
        }

        internal string Path { get; }

        internal string Name { get; }

        internal byte[] Bytes { get; }
    }

    /// <summary>One error of a build, written <c>Stairs.cs(12,9): error CS0103: message</c>; sorted by file, line, column and id.</summary>
    internal sealed class CadPluginError : IComparable<CadPluginError>
    {
        internal CadPluginError(string file, int line, int column, string id, string message)
        {
            File = file;
            Line = line;
            Column = column;
            Id = id;
            Message = message;
        }

        internal string File { get; }

        internal int Line { get; }

        internal int Column { get; }

        internal string Id { get; }

        internal string Message { get; }

        public int CompareTo(CadPluginError other)
        {
            int order = string.CompareOrdinal(File ?? string.Empty, other.File ?? string.Empty);
            order = order != 0 ? order : Line.CompareTo(other.Line);
            order = order != 0 ? order : Column.CompareTo(other.Column);
            order = order != 0 ? order : string.CompareOrdinal(Id, other.Id);
            return order != 0 ? order : string.CompareOrdinal(Message, other.Message);
        }

        public override string ToString()
        {
            string where =
                File == null ? string.Empty
                : Line > 0
                    ? File
                        + "("
                        + Line.ToString(CultureInfo.InvariantCulture)
                        + ","
                        + Column.ToString(CultureInfo.InvariantCulture)
                        + "): "
                : File + ": ";
            return where + "error " + Id + ": " + Message;
        }
    }

    /// <summary>What the compiler emitted: the assembly and PDB bytes, or the errors.</summary>
    internal sealed class CadPluginEmit
    {
        internal byte[] Assembly { get; set; }

        internal byte[] Pdb { get; set; }

        internal List<CadPluginError> Errors { get; } = new List<CadPluginError>();
    }
}
#endif
