#if NET8_0_OR_GREATER
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using NuGet.Client;
using NuGet.Common;
using NuGet.ContentModel;
using NuGet.Frameworks;
using NuGet.Packaging;
using NuGet.Packaging.Core;
using NuGet.Packaging.Signing;
using NuGet.Protocol;
using NuGet.Protocol.Core.Types;
using NuGet.RuntimeModel;
using NuGet.Configuration;
using NuGet.Versioning;

namespace OpenCAD2D.Wpf.Plugins
{
    /// <summary>
    /// Restores the NuGet packages a plugin's .csproj names, as <c>dotnet restore</c> does and with no .NET SDK: for each
    /// range the lowest version the source has that meets it (a <c>Version="13.0.3"</c> means 13.0.3 or higher, so 13.0.3
    /// where the source has it), the dependency group nearest to <c>net8.0-windows7.0</c>, the same package met twice at the
    /// higher of its lowest versions, then every range checked against what was chosen. Packages are downloaded into one
    /// folder of the app in the layout of NuGet's global-packages folder, <c>&lt;folder&gt;\&lt;id&gt;\&lt;version&gt;\</c>, whose
    /// <c>.sha512</c> file is the mark that a package is complete. The source and the folder are given to the constructor, so
    /// that a test gives a folder of .nupkg and a temporary folder and reaches no network.
    /// </summary>
    internal sealed class CadPackageRestorer
    {
        /// <summary>The source of the app: nuget.org's index.</summary>
        internal const string NuGetOrg = "https://api.nuget.org/v3/index.json";

        private const int MaxPasses = 64;

        private static readonly NuGetFramework Target = NuGetFramework.Parse("net8.0-windows7.0");

        private static readonly RuntimeGraph Runtimes = new RuntimeGraph(
            new[]
            {
                new RuntimeDescription("win-x64", new[] { "win" }),
                new RuntimeDescription("win", new[] { "any" }),
                new RuntimeDescription("any"),
            }
        );

        private readonly string _source;
        private readonly object _gate = new object();
        private SourceRepository _repository;

        /// <summary>
        /// NuGet tries a refused connection 6 times a second apart, and the service index three times over: 17.7 s measured
        /// against a port nothing listens on, with the window waiting. Two tries half a second apart (about 2 s) say the same
        /// thing sooner; a value the user has set in the environment is kept.
        /// </summary>
        static CadPackageRestorer()
        {
            Default("NUGET_ENHANCED_MAX_NETWORK_TRY_COUNT", "2");
            Default("NUGET_ENHANCED_NETWORK_RETRY_DELAY_MILLISECONDS", "500");
        }

        private static void Default(string name, string value)
        {
            if (Environment.GetEnvironmentVariable(name) == null)
            {
                Environment.SetEnvironmentVariable(name, value);
            }
        }

        /// <summary>Initializes a restorer.</summary>
        /// <param name="source">The source: the address of a NuGet v3 index, or a folder of .nupkg files.</param>
        /// <param name="packagesFolder">The folder the packages are extracted into.</param>
        internal CadPackageRestorer(string source, string packagesFolder)
        {
            if (string.IsNullOrEmpty(source))
            {
                throw new ArgumentException("A source is needed.", nameof(source));
            }

            if (string.IsNullOrEmpty(packagesFolder))
            {
                throw new ArgumentException("A folder for the packages is needed.", nameof(packagesFolder));
            }

            _source = source;
            PackagesFolder = Path.GetFullPath(packagesFolder);
        }

        /// <summary>Gets the folder of the app's packages, <c>%LocalAppData%\OpenCAD2D\NuGet\packages</c>.</summary>
        internal static string DefaultPackagesFolder =>
            Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                "OpenCAD2D",
                "NuGet",
                "packages"
            );

        /// <summary>Gets the restorer of the app: nuget.org, and the folder of the app's packages.</summary>
        internal static CadPackageRestorer Default() => new CadPackageRestorer(NuGetOrg, DefaultPackagesFolder);

        /// <summary>Gets the folder the packages are extracted into.</summary>
        internal string PackagesFolder { get; }

        /// <summary>Gets the source as the lines about it name it: <c>nuget.org</c>, or the address or folder given.</summary>
        internal string SourceName =>
            string.Equals(_source, NuGetOrg, StringComparison.Ordinal) ? "nuget.org" : _source;

        /// <summary>Gets whether a version text is a floating range such as <c>1.*</c>, which takes the highest version of the range.</summary>
        internal static bool IsFloating(string text) =>
            VersionRange.TryParse(text, out VersionRange range) && range.IsFloating;

        /// <summary>
        /// Gets the lowest version a requested version or range asks for, as NuGet writes it: <c>1.2.0</c> for a plain
        /// <c>1.2.0</c> or <c>[1.2.0, 2.0.0)</c>; null where there is no lower end or the text is none.
        /// </summary>
        internal static string LowestOf(string text) =>
            VersionRange.TryParse(text, out VersionRange range) ? range.MinVersion?.ToNormalizedString() : null;

        /// <summary>Gets a version as NuGet writes it (<c>1.0</c> is <c>1.0.0</c>), or null where the text is no version.</summary>
        internal static string Normalize(string text) =>
            NuGetVersion.TryParse(text, out NuGetVersion version) ? version.ToNormalizedString() : null;

        /// <summary>Gets whether one version, as <see cref="Normalize"/> gives it, is above another.</summary>
        internal static bool IsAbove(string version, string other) =>
            NuGetVersion.TryParse(version, out NuGetVersion a)
            && NuGetVersion.TryParse(other, out NuGetVersion b)
            && a > b;

        /// <summary>Gets whether a package of this id and version, as the .sha512 mark says, is in the folder complete; with <paramref name="sha512"/> given, the mark must be that one.</summary>
        internal bool IsComplete(string id, string version, string sha512)
        {
            string mark = new VersionFolderPathResolver(PackagesFolder).GetHashPath(id, NuGetVersion.Parse(version));

            if (!File.Exists(mark))
            {
                return false;
            }

            return sha512 == null || string.Equals(File.ReadAllText(mark).Trim(), sha512, StringComparison.Ordinal);
        }

        /// <summary>
        /// Works out which packages a plugin needs, asking the source for version lists and the dependencies of the version
        /// chosen, and nothing else: no package is downloaded. The direct ones come first in the list, then the others, each
        /// in ordinal order of the id.
        /// </summary>
        /// <param name="tool">The plugin's name, which the lines about it begin with.</param>
        /// <param name="project">The .csproj's file name, which an error is placed in.</param>
        /// <param name="requests">What the .csproj names: the packages that are not the host's and not for the build alone.</param>
        /// <param name="errors">Receives what cannot be restored, as <c>Stairs.csproj(6,10): error NU1107: ...</c>.</param>
        /// <returns>The packages, or null where there are errors.</returns>
        /// <exception cref="CadPackageSourceException">The source could not be reached.</exception>
        internal List<CadPackageEntry> Resolve(
            string tool,
            string project,
            IReadOnlyList<CadPackageRequest> requests,
            List<CadPluginError> errors
        )
        {
            try
            {
                return Run(() => new Walk(this, tool, project, requests, errors).Resolve());
            }
            catch (Exception exception) when (Unreached(exception))
            {
                throw new CadPackageSourceException(Reason(exception), exception);
            }
        }

        /// <summary>Downloads packages into the folder and extracts them; the .sha512 mark of each is written last.</summary>
        /// <exception cref="CadPackageSourceException">The source could not be reached, or has no such package.</exception>
        internal void Download(IReadOnlyList<CadPackageEntry> packages)
        {
            try
            {
                Run(() => DownloadAsync(packages));
            }
            catch (Exception exception) when (Unreached(exception))
            {
                throw new CadPackageSourceException(Reason(exception), exception);
            }
        }

        /// <summary>
        /// Reads a package in the folder: the SHA-512 of its .nupkg and the files that are its assets for .NET 8 on Windows
        /// x64 (what compiles against, what runs, what is native), as paths from the package's folder. A package with
        /// libraries and none for this framework is an error.
        /// </summary>
        /// <returns>The error line of a package that has nothing for .NET 8, else null.</returns>
        internal string Describe(CadPackageEntry package)
        {
            var resolver = new VersionFolderPathResolver(PackagesFolder);
            NuGetVersion version = NuGetVersion.Parse(package.Version);
            package.Sha512 = File.ReadAllText(resolver.GetHashPath(package.Id, version)).Trim();

            using (var reader = new PackageFolderReader(resolver.GetInstallPath(package.Id, version)))
            {
                var items = new ContentItemCollection();
                items.Load(reader.GetFiles());
                var conventions = new ManagedCodeConventions(Runtimes);
                SelectionCriteria criteria = conventions.Criteria.ForFrameworkAndRuntime(Target, "win-x64");
                ContentItemGroup compile = items.FindBestItemGroup(
                    criteria,
                    conventions.Patterns.CompileRefAssemblies,
                    conventions.Patterns.CompileLibAssemblies
                );
                ContentItemGroup runtime = items.FindBestItemGroup(criteria, conventions.Patterns.RuntimeAssemblies);
                ContentItemGroup native = items.FindBestItemGroup(criteria, conventions.Patterns.NativeLibraries);
                package.Compile = Paths(compile);
                package.Runtime = Paths(runtime);
                package.Native = Paths(native);

                if (compile == null && runtime == null)
                {
                    var has = reader
                        .GetLibItems()
                        .Where(g => g.Items.Any(i => !IsPlaceholder(i)))
                        .Select(g => g.TargetFramework.GetShortFolderName())
                        .Distinct(StringComparer.Ordinal)
                        .OrderBy(n => n, StringComparer.Ordinal)
                        .ToList();

                    if (has.Count > 0)
                    {
                        return package.Id
                            + " "
                            + package.Version
                            + " has nothing for "
                            + Target.GetShortFolderName()
                            + " (it has "
                            + string.Join(", ", has)
                            + ").";
                    }
                }
            }

            return null;
        }

        private static List<string> Paths(ContentItemGroup group) =>
            group == null
                ? new List<string>()
                : group
                    .Items.Select(i => i.Path)
                    .Where(p => !IsPlaceholder(p))
                    .OrderBy(p => p, StringComparer.Ordinal)
                    .ToList();

        private static bool IsPlaceholder(string path) =>
            string.Equals(Path.GetFileName(path), "_._", StringComparison.Ordinal);

        /// <summary>Runs asynchronous work to its end from a thread of the pool, so that it never waits for the thread the app calls from.</summary>
        private static T Run<T>(Func<Task<T>> work) => Task.Run(work).GetAwaiter().GetResult();

        private static void Run(Func<Task> work) => Task.Run(work).GetAwaiter().GetResult();

        /// <summary>Whether an exception is the source failing (no network, a bad address, a package that is no package), not a fault of the app.</summary>
        private static bool Unreached(Exception exception) =>
            exception is FatalProtocolException
            || exception is System.Net.Http.HttpRequestException
            || exception is IOException
            || exception is InvalidDataException
            || exception is PackagingException
            || exception is UnauthorizedAccessException
            || exception is TaskCanceledException
            || exception is System.Net.Sockets.SocketException
            || (exception is AggregateException aggregate && aggregate.InnerExceptions.All(Unreached));

        /// <summary>The message of the exception at the bottom of a chain, which names what failed (<c>No connection could be made because the target machine actively refused it.</c>).</summary>
        private static string Reason(Exception exception)
        {
            while (exception.InnerException != null)
            {
                exception = exception.InnerException;
            }

            return exception.Message.Trim().Split('\n')[0].Trim();
        }

        private SourceRepository Repository()
        {
            lock (_gate)
            {
                return _repository ?? (_repository = NuGet.Protocol.Core.Types.Repository.Factory.GetCoreV3(_source));
            }
        }

        private async Task DownloadAsync(IReadOnlyList<CadPackageEntry> packages)
        {
            ILogger log = NullLogger.Instance;
            CancellationToken token = CancellationToken.None;
            FindPackageByIdResource byId = await Repository()
                .GetResourceAsync<FindPackageByIdResource>(token)
                .ConfigureAwait(false);
            ClientPolicyContext policy = ClientPolicyContext.GetClientPolicy(NullSettings.Instance, log);

            using (var cache = new SourceCacheContext { NoCache = true })
            {
                foreach (CadPackageEntry package in packages)
                {
                    var identity = new PackageIdentity(package.Id, NuGetVersion.Parse(package.Version));

                    using (var copy = new MemoryStream())
                    {
                        bool found = await byId.CopyNupkgToStreamAsync(
                                identity.Id,
                                identity.Version,
                                copy,
                                cache,
                                log,
                                token
                            )
                            .ConfigureAwait(false);

                        if (!found)
                        {
                            throw new IOException(identity + " is not on the source.");
                        }

                        copy.Position = 0;
                        using (
                            await GlobalPackagesFolderUtility
                                .AddPackageAsync(
                                    _source,
                                    identity,
                                    copy,
                                    PackagesFolder,
                                    Guid.Empty,
                                    policy,
                                    log,
                                    token
                                )
                                .ConfigureAwait(false)
                        ) { }
                    }
                }
            }
        }

        /// <summary>One resolution: the graph walked from the .csproj's own references until what is chosen stops changing.</summary>
        private sealed class Walk
        {
            private readonly CadPackageRestorer _restorer;
            private readonly string _tool;
            private readonly string _project;
            private readonly IReadOnlyList<CadPackageRequest> _requests;
            private readonly List<CadPluginError> _errors;
            private readonly Dictionary<string, List<NuGetVersion>> _versions = new Dictionary<
                string,
                List<NuGetVersion>
            >(StringComparer.OrdinalIgnoreCase);
            private readonly Dictionary<string, FindPackageByIdDependencyInfo> _infos = new Dictionary<
                string,
                FindPackageByIdDependencyInfo
            >(StringComparer.OrdinalIgnoreCase);
            private readonly Dictionary<string, string> _names = new Dictionary<string, string>(
                StringComparer.OrdinalIgnoreCase
            );
            private readonly HashSet<string> _reported = new HashSet<string>(StringComparer.Ordinal);
            private FindPackageByIdResource _byId;
            private SourceCacheContext _cache;

            internal Walk(
                CadPackageRestorer restorer,
                string tool,
                string project,
                IReadOnlyList<CadPackageRequest> requests,
                List<CadPluginError> errors
            )
            {
                _restorer = restorer;
                _tool = tool;
                _project = project;
                _requests = requests;
                _errors = errors;
            }

            internal async Task<List<CadPackageEntry>> Resolve()
            {
                var direct = new List<Need>();

                foreach (CadPackageRequest request in _requests)
                {
                    if (!VersionRange.TryParse(request.Version, out VersionRange range))
                    {
                        _errors.Add(
                            new CadPluginError(
                                _project,
                                request.Line,
                                request.Column,
                                "OC0002",
                                "Version '"
                                    + request.Version
                                    + "' of "
                                    + request.Id
                                    + " is not a NuGet version or range."
                            )
                        );
                        continue;
                    }

                    direct.Add(new Need(request.Id, range, _tool, true, request));
                    _names[request.Id] = request.Id;
                }

                if (_errors.Count > 0 || direct.Count == 0)
                {
                    return _errors.Count > 0 ? null : new List<CadPackageEntry>();
                }

                _byId = await _restorer
                    .Repository()
                    .GetResourceAsync<FindPackageByIdResource>(CancellationToken.None)
                    .ConfigureAwait(false);

                using (_cache = new SourceCacheContext { NoCache = true })
                {
                    var chosen = new Dictionary<string, NuGetVersion>(StringComparer.OrdinalIgnoreCase);
                    Dictionary<string, List<Need>> needs = null;

                    for (int pass = 0; pass < MaxPasses; pass++)
                    {
                        needs = await Collect(direct, chosen).ConfigureAwait(false);
                        var next = new Dictionary<string, NuGetVersion>(StringComparer.OrdinalIgnoreCase);

                        foreach (KeyValuePair<string, List<Need>> pair in needs)
                        {
                            NuGetVersion best = null;
                            bool missing = false;

                            foreach (Need need in pair.Value)
                            {
                                NuGetVersion lowest = await Lowest(pair.Key, need).ConfigureAwait(false);
                                need.Lowest = lowest;

                                if (lowest == null)
                                {
                                    missing = true;
                                }
                                else if (best == null || lowest > best)
                                {
                                    best = lowest;
                                }
                            }

                            if (!missing && best != null)
                            {
                                next[pair.Key] = best;
                            }
                        }

                        if (Same(chosen, next))
                        {
                            return Finish(direct, chosen, needs);
                        }

                        chosen = next;
                    }
                }

                _errors.Add(
                    new CadPluginError(
                        _project,
                        0,
                        0,
                        "OC0002",
                        _tool + "'s packages need each other in a way that never settles."
                    )
                );
                return null;
            }

            private static bool Same(Dictionary<string, NuGetVersion> a, Dictionary<string, NuGetVersion> b) =>
                a.Count == b.Count && a.All(p => b.TryGetValue(p.Key, out NuGetVersion v) && v == p.Value);

            /// <summary>Every need of the graph reached from the .csproj's references, through the versions chosen so far.</summary>
            private async Task<Dictionary<string, List<Need>>> Collect(
                List<Need> direct,
                Dictionary<string, NuGetVersion> chosen
            )
            {
                var needs = new Dictionary<string, List<Need>>(StringComparer.OrdinalIgnoreCase);
                var queue = new Queue<string>();

                foreach (Need need in direct)
                {
                    Add(needs, queue, need);
                }

                while (queue.Count > 0)
                {
                    string id = queue.Dequeue();

                    if (!chosen.TryGetValue(id, out NuGetVersion version))
                    {
                        continue;
                    }

                    FindPackageByIdDependencyInfo info = await Info(id, version).ConfigureAwait(false);

                    if (info == null)
                    {
                        continue;
                    }

                    PackageDependencyGroup group = NuGetFrameworkUtility.GetNearest(info.DependencyGroups, Target, g =>
                        g.TargetFramework
                    );

                    if (group == null)
                    {
                        if (info.DependencyGroups.Any())
                        {
                            Report(
                                "NU1202",
                                0,
                                0,
                                info.PackageIdentity.Id
                                    + " "
                                    + version.ToNormalizedString()
                                    + " has nothing for "
                                    + Target.GetShortFolderName()
                                    + " (it has "
                                    + string.Join(
                                        ", ",
                                        info.DependencyGroups.Select(g => g.TargetFramework.GetShortFolderName())
                                            .Distinct(StringComparer.Ordinal)
                                            .OrderBy(n => n, StringComparer.Ordinal)
                                    )
                                    + ")."
                            );
                        }

                        continue;
                    }

                    string requirer = info.PackageIdentity.Id + " " + version.ToNormalizedString();

                    foreach (
                        PackageDependency dependency in group.Packages.OrderBy(
                            d => d.Id,
                            StringComparer.OrdinalIgnoreCase
                        )
                    )
                    {
                        if (CadPluginProject.IsHost(dependency.Id))
                        {
                            continue;
                        }

                        Add(needs, queue, new Need(dependency.Id, dependency.VersionRange, requirer, false, null));
                    }
                }

                return needs;
            }

            private static void Add(Dictionary<string, List<Need>> needs, Queue<string> queue, Need need)
            {
                if (!needs.TryGetValue(need.Id, out List<Need> list))
                {
                    needs[need.Id] = list = new List<Need>();
                    queue.Enqueue(need.Id);
                }

                list.Add(need);
            }

            private async Task<List<NuGetVersion>> Versions(string id)
            {
                if (!_versions.TryGetValue(id, out List<NuGetVersion> all))
                {
                    IEnumerable<NuGetVersion> found = await _byId
                        .GetAllVersionsAsync(id, _cache, NullLogger.Instance, CancellationToken.None)
                        .ConfigureAwait(false);
                    _versions[id] = all = (found ?? Enumerable.Empty<NuGetVersion>()).OrderBy(v => v).ToList();
                }

                return all;
            }

            private async Task<FindPackageByIdDependencyInfo> Info(string id, NuGetVersion version)
            {
                string key = id + " " + version.ToNormalizedString();

                if (!_infos.TryGetValue(key, out FindPackageByIdDependencyInfo info))
                {
                    info = await _byId
                        .GetDependencyInfoAsync(id, version, _cache, NullLogger.Instance, CancellationToken.None)
                        .ConfigureAwait(false);
                    _infos[key] = info;

                    if (info != null)
                    {
                        _names[id] = info.PackageIdentity.Id;
                    }
                }

                return info;
            }

            /// <summary>The lowest version of the source that meets a need (the highest of a floating range, as NuGet takes it); null, with an error given once, where there is none.</summary>
            private async Task<NuGetVersion> Lowest(string id, Need need)
            {
                List<NuGetVersion> all = await Versions(id).ConfigureAwait(false);
                NuGetVersion lowest;

                if (need.Range.IsFloating)
                {
                    lowest = need.Range.FindBestMatch(all);
                }
                else
                {
                    bool prerelease = need.Range.MinVersion != null && need.Range.MinVersion.IsPrerelease;
                    lowest = all.FirstOrDefault(v => need.Range.Satisfies(v) && (prerelease || !v.IsPrerelease));
                }

                if (lowest == null)
                {
                    string asked = need.Describe();

                    if (all.Count == 0)
                    {
                        Report(
                            "NU1101",
                            need.Request?.Line ?? 0,
                            need.Request?.Column ?? 0,
                            need.Requirer
                                + " needs "
                                + id
                                + " "
                                + asked
                                + "; "
                                + _restorer.SourceName
                                + " has no package "
                                + id
                                + "."
                        );
                    }
                    else
                    {
                        Report(
                            "NU1102",
                            need.Request?.Line ?? 0,
                            need.Request?.Column ?? 0,
                            need.Requirer
                                + " needs "
                                + id
                                + " "
                                + asked
                                + "; "
                                + _restorer.SourceName
                                + " has no such version (the latest is "
                                + all[all.Count - 1].ToNormalizedString()
                                + ")."
                        );
                    }
                }

                return lowest;
            }

            private void Report(string id, int line, int column, string message)
            {
                if (_reported.Add(id + "|" + message))
                {
                    _errors.Add(new CadPluginError(_project, line, column, id, message));
                }
            }

            /// <summary>Checks every range against what was chosen: none may be left unmet, and a direct reference may not be lower than what another package needs.</summary>
            private List<CadPackageEntry> Finish(
                List<Need> direct,
                Dictionary<string, NuGetVersion> chosen,
                Dictionary<string, List<Need>> needs
            )
            {
                foreach (
                    KeyValuePair<string, NuGetVersion> pair in chosen.OrderBy(
                        p => p.Key,
                        StringComparer.OrdinalIgnoreCase
                    )
                )
                {
                    List<Need> all = needs[pair.Key];

                    if (all.Any(n => !n.Range.Satisfies(pair.Value)))
                    {
                        Need first = all.FirstOrDefault(n => n.Direct);
                        Report(
                            "NU1107",
                            first?.Request.Line ?? 0,
                            first?.Request.Column ?? 0,
                            string.Join(
                                "; ",
                                all.OrderBy(n => n.Direct ? 0 : 1)
                                    .ThenBy(n => n.Requirer, StringComparer.OrdinalIgnoreCase)
                                    .Select(n => n.Requirer + " needs " + Name(pair.Key) + " " + n.Describe())
                            ) + "; no version meets both."
                        );
                        continue;
                    }

                    foreach (Need asked in all.Where(n => n.Direct))
                    {
                        Need other = all.Where(n => !n.Direct && n.Lowest == pair.Value)
                            .OrderBy(n => n.Requirer, StringComparer.OrdinalIgnoreCase)
                            .FirstOrDefault();

                        if (other != null && asked.Lowest != null && pair.Value > asked.Lowest)
                        {
                            Report(
                                "NU1605",
                                asked.Request.Line,
                                asked.Request.Column,
                                _tool
                                    + " asks "
                                    + Name(pair.Key)
                                    + " "
                                    + asked.Request.Version
                                    + ", and "
                                    + other.Requirer
                                    + " needs "
                                    + Name(pair.Key)
                                    + " "
                                    + other.Describe()
                                    + "."
                            );
                        }
                    }
                }

                if (_errors.Count > 0)
                {
                    return null;
                }

                var directIds = new HashSet<string>(direct.Select(d => d.Id), StringComparer.OrdinalIgnoreCase);
                return chosen
                    .Select(p => new CadPackageEntry
                    {
                        Id = Name(p.Key),
                        Version = p.Value.ToNormalizedString(),
                        IsDirect = directIds.Contains(p.Key),
                    })
                    .OrderBy(e => e.IsDirect ? 0 : 1)
                    .ThenBy(e => e.Id, StringComparer.OrdinalIgnoreCase)
                    .ThenBy(e => e.Id, StringComparer.Ordinal)
                    .ToList();
            }

            private string Name(string id) => _names.TryGetValue(id, out string name) ? name : id;
        }

        /// <summary>One thing asked of a package: by the .csproj (<see cref="Direct"/>) or by another package.</summary>
        private sealed class Need
        {
            internal Need(string id, VersionRange range, string requirer, bool direct, CadPackageRequest request)
            {
                Id = id;
                Range = range;
                Requirer = requirer;
                Direct = direct;
                Request = request;
            }

            internal string Id { get; }

            internal VersionRange Range { get; }

            /// <summary>Gets who asks: the plugin's name, or <c>Bar 1.0.0</c>.</summary>
            internal string Requirer { get; }

            internal bool Direct { get; }

            /// <summary>Gets or sets the lowest version of the source that meets the need; null where there is none.</summary>
            internal NuGetVersion Lowest { get; set; }

            /// <summary>Gets the line of the .csproj that asks, for a direct need.</summary>
            internal CadPackageRequest Request { get; }

            /// <summary>Gets the range as the lines write it: <c>1.2.0 or higher</c> for a lower end alone, else <c>[2.0.0, 3.0.0)</c>.</summary>
            internal string Describe() =>
                Range.IsMinInclusive && !Range.HasUpperBound && Range.MinVersion != null && !Range.IsFloating
                    ? Range.MinVersion.ToNormalizedString() + " or higher"
                    : Range.ToNormalizedString();
        }
    }

    /// <summary>The source of packages could not be reached or gave a package that is none; <see cref="Exception.Message"/> says why, as <c>No connection could be made...</c>.</summary>
    internal sealed class CadPackageSourceException : Exception
    {
        internal CadPackageSourceException(string reason, Exception inner)
            : base(reason, inner) { }
    }
}
#endif
