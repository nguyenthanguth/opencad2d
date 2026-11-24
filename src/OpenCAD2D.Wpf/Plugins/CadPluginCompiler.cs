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
    /// Compiles a plugin given as a folder with a .csproj into an assembly and its PDB, and keeps the build in a cache keyed
    /// by the SHA-256 of the project and its files, so that a start with unchanged files compiles nothing. A plugin of two
    /// files, 220 lines in all, compiles in about 0.9 s the first time in a session (the compiler's own start) and in about
    /// 20 ms after that; taken from the cache it does not load the compiler.
    /// </summary>
    /// <remarks>
    /// What the .csproj says is read by <see cref="CadPluginProject"/>, and the sources are compiled against the .NET 8
    /// runtime the app runs on (its own assemblies and WPF's), GeometryHelper, OpenCAD2D.Core and OpenCAD2D.Wpf, and the .dll
    /// files its references name inside the plugin's folder. Where the .csproj is silent, C# is the latest the compiler knows,
    /// nullable off and no implicit usings; warnings of the compiler are dropped. The cache key takes the app's version, the
    /// runtime's version, the .csproj, each source's path from the folder and bytes, and each referenced .dll's name and
    /// bytes.
    /// </remarks>
    public sealed class CadPluginCompiler
    {
        /// <summary>The most error lines a failed build gives; <see cref="CadPluginBuild.ErrorCount"/> counts them all.</summary>
        public const int MaxErrors = 20;

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
        /// Compiles a plugin, or takes the build from the cache where its .csproj, sources and libraries are unchanged since the
        /// last compile. A plugin Stairs whose folder holds Stairs.csproj gives <c>&lt;cache&gt;\Stairs\&lt;key&gt;.dll</c> and
        /// its .pdb; a reference to an unknown name on line 12, column 9 of Stairs.cs gives a failed build with the error
        /// <c>Stairs.cs(12,9): error CS0103: The name 'x' does not exist in the current context</c>, and a .csproj with a
        /// <c>ProjectReference</c> on line 6 one with <c>Stairs.csproj(6,10): error OC0002: ...</c>.
        /// </summary>
        /// <param name="name">The plugin's name, which names its folder in the cache.</param>
        /// <param name="projectPath">The path of the plugin's .csproj; its folder holds the sources.</param>
        /// <returns>The assembly built or found in the cache, with the warnings about the project, or the errors that kept it from being built.</returns>
        /// <exception cref="ArgumentException">There is no name or no project.</exception>
        public CadPluginBuild Compile(string name, string projectPath)
        {
            if (string.IsNullOrEmpty(name))
            {
                throw new ArgumentException("A plugin's name is needed.", nameof(name));
            }

            if (string.IsNullOrEmpty(projectPath))
            {
                throw new ArgumentException("A source plugin has a .csproj.", nameof(projectPath));
            }

            var errors = new List<CadPluginError>();
            var warnings = new List<string>();
            CadPluginProject project = CadPluginProject.Read(Path.GetFullPath(projectPath), errors, warnings);

            if (errors.Count > 0)
            {
                return Failed(errors);
            }

            string key = Key(project);
            string folder = Path.Combine(CacheFolder, Safe(name));
            string dll = Path.Combine(folder, key + ".dll");
            string pdb = Path.Combine(folder, key + ".pdb");

            // The .pdb is written first and the .dll last, so a .dll in the cache always has its .pdb.
            if (File.Exists(dll) && File.Exists(pdb))
            {
                return new CadPluginBuild(dll, pdb, true, warnings);
            }

            CadPluginEmit emit = CadPluginRoslyn.Compile(project);

            if (emit.Errors.Count > 0)
            {
                return Failed(emit.Errors);
            }

            Directory.CreateDirectory(folder);
            Write(pdb, emit.Pdb);
            Write(dll, emit.Assembly);
            Prune(folder, key);
            return new CadPluginBuild(dll, pdb, false, warnings);
        }

        /// <summary>
        /// The cache key, 64 hex digits: the SHA-256 of a format tag, the app's version, the runtime's version, the .csproj's
        /// bytes, then each source (its path from the folder, its length, its bytes) in ordinal order of the paths, then each
        /// referenced .dll (its name, its length, its bytes) in order of the paths.
        /// </summary>
        internal string Key(CadPluginProject project)
        {
            using (var hash = IncrementalHash.CreateHash(HashAlgorithmName.SHA256))
            {
                AppendText(hash, "tf1");
                AppendText(hash, _appVersion);
                AppendText(hash, Environment.Version.ToString());
                AppendBytes(hash, project.Bytes);

                foreach (SourceFile file in project.Sources)
                {
                    AppendText(hash, file.Name);
                    AppendBytes(hash, file.Bytes);
                }

                foreach (string library in project.Libraries)
                {
                    AppendText(hash, Path.GetFileName(library));
                    AppendBytes(hash, File.ReadAllBytes(library));
                }

                return Convert.ToHexString(hash.GetHashAndReset());
            }
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

        /// <summary>Takes the length of a file and then the file, so that where one ends and the next begins is part of the key.</summary>
        private static void AppendBytes(IncrementalHash hash, byte[] bytes)
        {
            AppendText(hash, bytes.Length.ToString(CultureInfo.InvariantCulture));
            hash.AppendData(bytes);
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

    /// <summary>One source of a plugin: its full path, its name (the path from the plugin's folder) and its bytes as read once.</summary>
    internal sealed class SourceFile
    {
        internal SourceFile(string path, string name, byte[] bytes)
        {
            Path = path;
            Name = name;
            Bytes = bytes;
        }

        internal string Path { get; }

        /// <summary>Gets the path from the plugin's folder, with / for separators: Geometry/Steps.cs.</summary>
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
