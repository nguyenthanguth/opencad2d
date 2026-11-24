#if NET8_0_OR_GREATER
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Security.Cryptography;
using System.Text.Json;
using GeometryHelper;
using OpenCAD2D.Plugins;

namespace OpenCAD2D.Wpf.Plugins
{
    /// <summary>One <c>PackageReference</c> of a plugin's .csproj that is restored: the package's id, the version text as written, and where it stands in the .csproj.</summary>
    internal sealed class CadPackageRequest
    {
        internal CadPackageRequest(string id, string version, int line, int column)
        {
            Id = id;
            Version = version;
            Line = line;
            Column = column;
        }

        internal string Id { get; }

        internal string Version { get; }

        internal int Line { get; }

        internal int Column { get; }
    }

    /// <summary>
    /// One package of a plugin's resolved list, as kept in <c>packages.&lt;key&gt;.json</c>: its id, the version chosen, the
    /// SHA-512 of its .nupkg and the files that are its assets for .NET 8 on Windows x64, as paths from its folder in the
    /// packages folder (<c>lib/net6.0/Foo.dll</c>).
    /// </summary>
    internal sealed class CadPackageEntry
    {
        public string Id { get; set; } = string.Empty;

        public string Version { get; set; } = string.Empty;

        public bool IsDirect { get; set; }

        public string Sha512 { get; set; }

        public List<string> Compile { get; set; } = new List<string>();

        public List<string> Runtime { get; set; } = new List<string>();

        public List<string> Native { get; set; } = new List<string>();
    }

    /// <summary>
    /// The packages of one plugin, resolved: found in the list kept for its .csproj without asking the source, or restored
    /// (the choice made, the question put, the download) and then kept. What the compiler builds against, what the load
    /// context loads and the cache key's package part all come from it.
    /// </summary>
    internal sealed class CadPackageSet
    {
        private static readonly JsonSerializerOptions Json = new JsonSerializerOptions
        {
            WriteIndented = true,
            PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        };

        private readonly string _folder;

        private CadPackageSet(string folder, IReadOnlyList<CadPackageEntry> entries)
        {
            _folder = folder;
            Entries = entries;
        }

        /// <summary>Gets the set of a plugin with no packages of its own.</summary>
        internal static CadPackageSet None { get; } = new CadPackageSet(string.Empty, Array.Empty<CadPackageEntry>());

        /// <summary>Gets the packages, the direct ones first, then the others, each in ordinal order of the id.</summary>
        internal IReadOnlyList<CadPackageEntry> Entries { get; }

        /// <summary>Gets the full paths of the assemblies to compile against, in the order of the packages.</summary>
        internal IEnumerable<string> CompilePaths => Entries.SelectMany(e => e.Compile.Select(p => PathOf(e, p)));

        /// <summary>Gets the assemblies the plugin runs with, by simple name, each with its package: a name a package gives twice is the first package's.</summary>
        internal IEnumerable<KeyValuePair<CadPackageEntry, string>> RuntimePaths =>
            Entries.SelectMany(e => e.Runtime.Select(p => new KeyValuePair<CadPackageEntry, string>(e, PathOf(e, p))));

        /// <summary>Gets the native files of the packages by full path, in the order of the packages.</summary>
        internal IEnumerable<string> NativePaths => Entries.SelectMany(e => e.Native.Select(p => PathOf(e, p)));

        /// <summary>
        /// Gets what the cache key takes of the packages, in ordinal order of the id: each package's id, version and SHA-512,
        /// as separate texts.
        /// </summary>
        internal IEnumerable<string> KeyParts =>
            Entries
                .OrderBy(e => e.Id, StringComparer.OrdinalIgnoreCase)
                .ThenBy(e => e.Id, StringComparer.Ordinal)
                .SelectMany(e => new[] { e.Id, e.Version, e.Sha512 ?? string.Empty });

        /// <summary>The name of the file the list of a .csproj is kept in: <c>packages.</c> and the SHA-256 of the .csproj's bytes.</summary>
        internal static string ListFileName(byte[] csproj)
        {
            using (SHA256 hash = SHA256.Create())
            {
                return "packages." + Convert.ToHexString(hash.ComputeHash(csproj)) + ".json";
            }
        }

        /// <summary>
        /// Gets the packages a plugin needs. The list kept for its .csproj is read where every package of it is in the folder
        /// complete, and the source is not asked. Otherwise the list is made (or the kept one taken as it is), the question
        /// put where a package is to be downloaded, the download made, the assets read and the list kept.
        /// </summary>
        /// <param name="restorer">The source and the folder.</param>
        /// <param name="listFolder">The plugin's folder in the plugin cache, which holds the list.</param>
        /// <param name="tool">The plugin's name.</param>
        /// <param name="project">The .csproj as read.</param>
        /// <param name="confirm">The question before a download, given the plugin's name and the packages; null leaves a download unmade.</param>
        /// <param name="errors">Receives what cannot be restored.</param>
        /// <param name="warnings">Receives the lines about the host's packages and the ranges.</param>
        /// <returns>The packages, or null where there are errors.</returns>
        /// <exception cref="CadPluginException">The packages were not to be downloaded, or the source could not be reached.</exception>
        internal static CadPackageSet Obtain(
            CadPackageRestorer restorer,
            string listFolder,
            string tool,
            CadPluginProject project,
            Func<string, IReadOnlyList<CadPackage>, bool> confirm,
            List<CadPluginError> errors,
            List<string> warnings
        )
        {
            foreach (CadPackageRequest host in project.HostPackages)
            {
                string lowest = CadPackageRestorer.LowestOf(host.Version);
                string name = CadPluginProject.HostName(host.Id);
                string app = AppVersion(name);

                if (lowest != null && app != null && CadPackageRestorer.IsAbove(lowest, app))
                {
                    warnings.Add(tool + " was written for " + name + " " + lowest + "; this app has " + app + ".");
                }
            }

            if (project.Packages.Count == 0)
            {
                return None;
            }

            string list = Path.Combine(listFolder, ListFileName(project.Bytes));
            List<CadPackageEntry> entries = Read(list);

            if (entries == null || !entries.TrueForAll(e => restorer.IsComplete(e.Id, e.Version, e.Sha512)))
            {
                try
                {
                    entries = entries ?? restorer.Resolve(tool, project.FileName, project.Packages, errors);

                    if (entries == null)
                    {
                        return null;
                    }

                    Fetch(restorer, tool, entries, confirm);
                }
                catch (CadPackageSourceException exception)
                {
                    throw new CadPluginException(
                        tool,
                        tool
                            + " was not loaded: "
                            + restorer.SourceName
                            + " could not be reached ("
                            + exception.Message
                            + "). Connect and run PLUGINRELOAD "
                            + tool
                            + ".",
                        exception
                    );
                }

                foreach (CadPackageEntry entry in entries)
                {
                    string problem = restorer.Describe(entry);

                    if (problem != null)
                    {
                        CadPackageRequest asked = project.Packages.FirstOrDefault(r =>
                            string.Equals(r.Id, entry.Id, StringComparison.OrdinalIgnoreCase)
                        );
                        errors.Add(
                            new CadPluginError(
                                project.FileName,
                                asked?.Line ?? 0,
                                asked?.Column ?? 0,
                                "NU1202",
                                problem
                            )
                        );
                    }
                }

                if (errors.Count > 0)
                {
                    return null;
                }

                Write(list, entries);
            }

            foreach (CadPackageRequest request in project.Packages)
            {
                CadPackageEntry chosen = entries.Find(e =>
                    string.Equals(e.Id, request.Id, StringComparison.OrdinalIgnoreCase)
                );

                if (chosen != null && CadPackageRestorer.IsFloating(request.Version))
                {
                    warnings.Add(
                        project.FileName
                            + ": "
                            + request.Id
                            + " "
                            + request.Version
                            + " is a range: "
                            + chosen.Id
                            + " "
                            + chosen.Version
                            + " was chosen and stays until the .csproj changes."
                    );
                }
            }

            return new CadPackageSet(restorer.PackagesFolder, entries);
        }

        /// <summary>Puts the question where a package is not in the folder, and downloads those; no question where all are there.</summary>
        private static void Fetch(
            CadPackageRestorer restorer,
            string tool,
            List<CadPackageEntry> entries,
            Func<string, IReadOnlyList<CadPackage>, bool> confirm
        )
        {
            List<CadPackageEntry> missing = entries.FindAll(e => !restorer.IsComplete(e.Id, e.Version, e.Sha512));

            if (missing.Count == 0)
            {
                return;
            }

            var question = new List<CadPackage>();

            foreach (CadPackageEntry entry in entries)
            {
                question.Add(new CadPackage(entry.Id, entry.Version, entry.IsDirect, !missing.Contains(entry)));
            }

            if (!Confirmed(confirm, tool, question))
            {
                throw new CadPluginException(tool, tool + " was not loaded: its packages were not downloaded.");
            }

            restorer.Download(missing);
        }

        /// <summary>Whether the question was answered yes; a question that throws is a no, as the manager takes one about an untrusted plugin.</summary>
        private static bool Confirmed(
            Func<string, IReadOnlyList<CadPackage>, bool> confirm,
            string tool,
            IReadOnlyList<CadPackage> question
        )
        {
            if (confirm == null)
            {
                return false;
            }

            try
            {
                return confirm(tool, question);
            }
            catch (Exception exception)
            {
                GeometryHelperLog.Warn(
                    "CadPackageSet: asking whether to download the packages of "
                        + tool
                        + " failed; they are not downloaded.",
                    exception
                );
                return false;
            }
        }

        /// <summary>The list kept for a .csproj; null where there is none, or it cannot be read.</summary>
        private static List<CadPackageEntry> Read(string path)
        {
            try
            {
                if (!File.Exists(path))
                {
                    return null;
                }

                List<CadPackageEntry> entries = JsonSerializer.Deserialize<List<CadPackageEntry>>(
                    File.ReadAllText(path),
                    Json
                );
                return entries != null && entries.TrueForAll(e => e != null && e.Id.Length > 0 && e.Version.Length > 0)
                    ? entries
                    : null;
            }
            catch (Exception exception) when (exception is JsonException || exception is IOException)
            {
                return null;
            }
        }

        /// <summary>Keeps the list through a temporary name, so that a list cut short is never read.</summary>
        private static void Write(string path, List<CadPackageEntry> entries)
        {
            Directory.CreateDirectory(Path.GetDirectoryName(path));
            string temporary = path + "." + Guid.NewGuid().ToString("N") + ".tmp";
            File.WriteAllText(temporary, JsonSerializer.Serialize(entries, Json), new System.Text.UTF8Encoding(false));
            File.Move(temporary, path, true);
        }

        private string PathOf(CadPackageEntry entry, string relative) =>
            Path.Combine(
                _folder,
                entry.Id.ToLowerInvariant(),
                entry.Version.ToLowerInvariant(),
                relative.Replace('/', Path.DirectorySeparatorChar)
            );

        /// <summary>The version of a host assembly the app has, as NuGet writes it: the assembly's informational version before any <c>+</c>, else its version.</summary>
        private static string AppVersion(string host)
        {
            Assembly assembly = string.Equals(host, "GeometryHelper", StringComparison.Ordinal)
                ? typeof(GeometryHelperLog).Assembly
                : typeof(CadPlugin).Assembly;
            string text = assembly.GetCustomAttribute<AssemblyInformationalVersionAttribute>()?.InformationalVersion
                ?? assembly.GetName().Version?.ToString(3);
            return CadPackageRestorer.Normalize(text?.Split('+')[0]);
        }
    }
}
#endif
