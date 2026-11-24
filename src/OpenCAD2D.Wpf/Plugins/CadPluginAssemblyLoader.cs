using System;
using System.Collections.Generic;
using System.IO;
using System.Reflection;
using GeometryHelper;
using OpenCAD2D.Plugins;
#if NET8_0_OR_GREATER
using System.Runtime.Loader;
#endif

namespace OpenCAD2D.Wpf.Plugins
{
    /// <summary>
    /// The loader of plugins for the runtime the presenter runs on, given to <see cref="CadPluginManager"/>. On .NET 8 each
    /// plugin is loaded into a collectible <c>AssemblyLoadContext</c> of its own, which unloading frees, so that a new build
    /// is taken without a restart and two plugins' dependencies do not clash (D19 of the plan). On .NET Framework, hosted in
    /// Tekla, every plugin is loaded into the one AppDomain with <c>Assembly.LoadFrom</c>, and nothing is unloaded: unloading
    /// only switches a plugin off.
    /// </summary>
    /// <remarks>
    /// Either way the presenter's own assemblies (GeometryHelper, OpenCAD2D, ...) are never loaded from a
    /// plugin's folder: a plugin is given the presenter's, so that the <see cref="CadCommandContext"/> it is handed is of the
    /// type it was built against. A dependency a plugin asks for is taken from its folder of copies, copied there first from
    /// the folder of a bare .dll where it is missing, and then from the NuGet packages its .csproj names, loaded in place
    /// from the packages folder. So two plugins may run two versions of one package, Foo 1.0 and Foo 2.0 each in its own
    /// load context, but not of a library the app carries itself (the framework, SkiaSharp, Roslyn's and NuGet's own): the
    /// app's copy is compiled against and runs, and a package asking a higher version gets a line of the load log. A native
    /// file of a package is loaded by its full path; it stays loaded until the app exits.
    /// </remarks>
    public sealed class CadPluginAssemblyLoader : CadPluginLoader
    {
        /// <summary>The presenter's own assemblies, by simple name, never loaded from a plugin's folder.</summary>
        private static readonly string[] Own =
        {
            typeof(GeometryHelperLog).Assembly.GetName().Name,
            typeof(CadPlugin).Assembly.GetName().Name,
            typeof(CadPluginAssemblyLoader).Assembly.GetName().Name,
        };

#if NET8_0_OR_GREATER
        private readonly CadPluginCompiler _compiler;

        /// <summary>The packages of the plugins compiled and not yet loaded, by plugin name: what <see cref="Compile(string, string)"/> found for <see cref="Load"/>.</summary>
        private readonly Dictionary<string, CadPackageSet> _packages = new Dictionary<string, CadPackageSet>(
            StringComparer.OrdinalIgnoreCase
        );

        /// <summary>
        /// The load contexts of the plugins loaded and not unloaded. A collectible context is held by the runtime only weakly,
        /// and its finalizer starts the unloading: where nothing holds it, a collection puts it in the state of unloading while
        /// its plugin is in use, and a library the plugin first asks for after that is refused (<c>AssemblyLoadContext is
        /// unloading</c>).
        /// </summary>
        private readonly List<AssemblyLoadContext> _contexts = new List<AssemblyLoadContext>();
#endif

        /// <summary>Initializes the loader of the app: on .NET 8 plugins given as C# source are cached in <c>%LocalAppData%\OpenCAD2D\PluginCache</c>.</summary>
        public CadPluginAssemblyLoader()
            : this(null) { }

        /// <summary>Initializes a loader that caches the builds of plugins given as C# source in a folder of its own (on .NET 8; .NET Framework compiles none).</summary>
        /// <param name="cacheFolder">The cache folder; null for the app's, <c>%LocalAppData%\OpenCAD2D\PluginCache</c>.</param>
        public CadPluginAssemblyLoader(string cacheFolder)
        {
#if NET8_0_OR_GREATER
            _compiler = new CadPluginCompiler(cacheFolder ?? CadPluginCompiler.DefaultCacheFolder);
#endif
        }

#if NET8_0_OR_GREATER
        /// <summary>Initializes a loader whose NuGet packages come from a restorer of its own, which the tests point at a folder of .nupkg and a temporary folder.</summary>
        internal CadPluginAssemblyLoader(string cacheFolder, CadPackageRestorer restorer)
        {
            _compiler = new CadPluginCompiler(cacheFolder, restorer);
        }

        /// <summary>
        /// Gets or sets what is asked before the NuGet packages of a plugin are downloaded: given the plugin's name and its
        /// packages (the direct ones first, then the rest, each in ordinal order, those already in the cache marked), it says
        /// whether to download them from nuget.org. The window asks with a dialog, the command line with
        /// <see cref="CadPackageQuestion.Prompt"/>. Null, as it is until set, downloads none and refuses a plugin whose
        /// packages are not all in the cache with <c>Stairs was not loaded: its packages were not downloaded.</c>; it is not
        /// asked where every package is in the cache already.
        /// </summary>
        public Func<string, IReadOnlyList<CadPackage>, bool> ConfirmDownload
        {
            get => _compiler.ConfirmDownload;
            set => _compiler.ConfirmDownload = value;
        }

        /// <summary>Gets true: a plugin's load context is freed when it is unloaded.</summary>
        public override bool CanUnload => true;

        /// <summary>Gets true: on .NET 8 plugins given as C# source are compiled.</summary>
        public override bool CanCompile => true;

        /// <inheritdoc/>
        public override CadPluginBuild Compile(string name, string projectPath)
        {
            CadPluginBuild build = _compiler.Compile(name, projectPath, out CadPackageSet packages);

            lock (_packages)
            {
                _packages[name] = packages;
            }

            return build;
        }

        /// <inheritdoc/>
        public override IReadOnlyList<Assembly> Load(string name, IReadOnlyList<string> files, string probeFolder)
        {
            if (files == null)
            {
                throw new ArgumentNullException(nameof(files));
            }

            var loaded = new List<Assembly>();

            if (files.Count == 0)
            {
                return loaded;
            }

            CadPackageSet packages;

            lock (_packages)
            {
                if (_packages.TryGetValue(name, out packages))
                {
                    _packages.Remove(name);
                }
            }

            var context = new PluginContext(
                name,
                Path.GetDirectoryName(files[0]),
                probeFolder,
                packages ?? CadPackageSet.None
            );

            lock (_contexts)
            {
                _contexts.Add(context);
            }

            try
            {
                foreach (string file in files)
                {
                    if (!PluginContext.IsShared(AssemblyName.GetAssemblyName(file)))
                    {
                        loaded.Add(context.LoadFromAssemblyPath(file));
                    }
                }
            }
            catch
            {
                Release(context);
                context.Unload();
                throw;
            }

            if (loaded.Count == 0)
            {
                Release(context);
                context.Unload();
            }

            return loaded;
        }

        /// <inheritdoc/>
        public override void Unload(IReadOnlyList<Assembly> assemblies)
        {
            if (assemblies == null || assemblies.Count == 0)
            {
                return;
            }

            if (AssemblyLoadContext.GetLoadContext(assemblies[0]) is PluginContext context)
            {
                Release(context);
                context.Unload();
            }
        }

        private void Release(AssemblyLoadContext context)
        {
            lock (_contexts)
            {
                _contexts.Remove(context);
            }
        }

        /// <summary>
        /// The version of the app's own copy of an assembly the plugin is given instead of its own (the framework, the app's
        /// libraries); null where the name is not shared.
        /// </summary>
        internal static Version SharedVersion(AssemblyName name)
        {
            if (!PluginContext.IsShared(name))
            {
                return null;
            }

            foreach (Assembly assembly in AssemblyLoadContext.Default.Assemblies)
            {
                if (string.Equals(assembly.GetName().Name, name.Name, StringComparison.OrdinalIgnoreCase))
                {
                    return assembly.GetName().Version;
                }
            }

            return AssemblyLoadContext.Default.LoadFromAssemblyName(new AssemblyName(name.Name)).GetName().Version;
        }

        /// <summary>The collectible load context of one plugin.</summary>
        private sealed class PluginContext : AssemblyLoadContext
        {
            private readonly string _folder;
            private readonly string _probeFolder;
            private readonly Dictionary<string, string> _managed = new Dictionary<string, string>(
                StringComparer.OrdinalIgnoreCase
            );
            private readonly Dictionary<string, string> _native = new Dictionary<string, string>(
                StringComparer.OrdinalIgnoreCase
            );

            internal PluginContext(string name, string folder, string probeFolder, CadPackageSet packages)
                : base("OpenCAD2D plugin " + name, true)
            {
                _folder = folder;
                _probeFolder = probeFolder;

                // The first package to give a name is the one that does: the packages are in a fixed order.
                foreach (KeyValuePair<CadPackageEntry, string> asset in packages.RuntimePaths)
                {
                    string simple = Path.GetFileNameWithoutExtension(asset.Value);

                    if (!_managed.ContainsKey(simple))
                    {
                        _managed.Add(simple, asset.Value);
                    }
                }

                foreach (string path in packages.NativePaths)
                {
                    string file = Path.GetFileName(path);

                    if (!_native.ContainsKey(file))
                    {
                        _native.Add(file, path);
                    }
                }
            }

            /// <summary>Whether the presenter has an assembly of the name, or can load one: then the plugin is given the presenter's.</summary>
            internal static bool IsShared(AssemblyName name)
            {
                if (Array.Exists(Own, n => string.Equals(n, name.Name, StringComparison.OrdinalIgnoreCase)))
                {
                    return true;
                }

                foreach (Assembly assembly in Default.Assemblies)
                {
                    if (string.Equals(assembly.GetName().Name, name.Name, StringComparison.OrdinalIgnoreCase))
                    {
                        return true;
                    }
                }

                try
                {
                    Default.LoadFromAssemblyName(new AssemblyName(name.Name));
                    return true;
                }
                catch (Exception exception)
                    when (exception is FileNotFoundException
                        || exception is FileLoadException
                        || exception is BadImageFormatException
                    )
                {
                    return false;
                }
            }

            protected override Assembly Load(AssemblyName assemblyName)
            {
                // Null hands the name to the default context: the presenter's own assembly.
                if (IsShared(assemblyName))
                {
                    return null;
                }

                // The plugin's own folder first, then the package folders in the cache, from where they are loaded in place.
                string path = Locate(_folder, _probeFolder, assemblyName.Name);

                if (path == null)
                {
                    _managed.TryGetValue(assemblyName.Name, out path);
                }

                return path != null ? LoadFromAssemblyPath(path) : null;
            }

            /// <summary>A native file of a package, loaded by its full path in the packages folder; the default search finds any other.</summary>
            protected override IntPtr LoadUnmanagedDll(string unmanagedDllName)
            {
                string file = Path.GetFileName(unmanagedDllName);

                if (
                    _native.TryGetValue(file, out string path)
                    || (
                        !file.EndsWith(".dll", StringComparison.OrdinalIgnoreCase)
                        && _native.TryGetValue(file + ".dll", out path)
                    )
                )
                {
                    return LoadUnmanagedDllFromPath(path);
                }

                return IntPtr.Zero;
            }
        }
#else
        private static readonly object Gate = new object();

        /// <summary>The folders of copies of the plugins loaded, the latest first, each with the folder its dependencies are copied from.</summary>
        private static readonly List<KeyValuePair<string, string>> Folders = new List<KeyValuePair<string, string>>();

        private static bool _resolving;

        /// <summary>Gets false: .NET Framework cannot unload an assembly, so unloading only switches a plugin off.</summary>
        public override bool CanUnload => false;

        /// <inheritdoc/>
        public override IReadOnlyList<Assembly> Load(string name, IReadOnlyList<string> files, string probeFolder)
        {
            if (files == null)
            {
                throw new ArgumentNullException(nameof(files));
            }

            var loaded = new List<Assembly>();

            if (files.Count == 0)
            {
                return loaded;
            }

            lock (Gate)
            {
                if (!_resolving)
                {
                    AppDomain.CurrentDomain.AssemblyResolve += Resolve;
                    _resolving = true;
                }

                Folders.Insert(0, new KeyValuePair<string, string>(Path.GetDirectoryName(files[0]), probeFolder));
            }

            foreach (string file in files)
            {
                if (Shared(AssemblyName.GetAssemblyName(file).Name) == null)
                {
                    loaded.Add(Assembly.LoadFrom(file));
                }
            }

            return loaded;
        }

        /// <summary>Does nothing: an assembly loaded into an AppDomain stays until the process ends.</summary>
        /// <param name="assemblies">The assemblies.</param>
        public override void Unload(IReadOnlyList<Assembly> assemblies) { }

        /// <summary>The presenter's own assembly of a simple name, loaded from outside the plugins' copies; null where there is none.</summary>
        private static Assembly Shared(string name)
        {
            foreach (Assembly assembly in AppDomain.CurrentDomain.GetAssemblies())
            {
                if (
                    !string.Equals(assembly.GetName().Name, name, StringComparison.OrdinalIgnoreCase)
                    || assembly.IsDynamic
                )
                {
                    continue;
                }

                bool own = Array.Exists(Own, n => string.Equals(n, name, StringComparison.OrdinalIgnoreCase));

                if (own || !InCopies(assembly.Location))
                {
                    return assembly;
                }
            }

            return null;
        }

        private static bool InCopies(string location)
        {
            lock (Gate)
            {
                foreach (KeyValuePair<string, string> folder in Folders)
                {
                    if (
                        !string.IsNullOrEmpty(location)
                        && location.StartsWith(
                            folder.Key + Path.DirectorySeparatorChar,
                            StringComparison.OrdinalIgnoreCase
                        )
                    )
                    {
                        return true;
                    }
                }
            }

            return false;
        }

        private static Assembly Resolve(object sender, ResolveEventArgs args)
        {
            var name = new AssemblyName(args.Name);
            Assembly shared = Shared(name.Name);

            if (shared != null)
            {
                return shared;
            }

            // The folder of the plugin that asks first, then every plugin's, the latest loaded first.
            string asking =
                args.RequestingAssembly != null && !args.RequestingAssembly.IsDynamic
                    ? Path.GetDirectoryName(args.RequestingAssembly.Location)
                    : null;
            var folders = new List<KeyValuePair<string, string>>();

            lock (Gate)
            {
                folders.AddRange(
                    Folders.FindAll(f => string.Equals(f.Key, asking, StringComparison.OrdinalIgnoreCase))
                );
                folders.AddRange(
                    Folders.FindAll(f => !string.Equals(f.Key, asking, StringComparison.OrdinalIgnoreCase))
                );
            }

            foreach (KeyValuePair<string, string> folder in folders)
            {
                string path = Locate(folder.Key, folder.Value, name.Name);

                if (path != null)
                {
                    return Assembly.LoadFrom(path);
                }
            }

            return null;
        }
#endif

        /// <summary>The path of a dependency in a plugin's folder of copies, copied there from the probe folder where missing; null where neither has it.</summary>
        private static string Locate(string folder, string probeFolder, string name)
        {
            string path = Path.Combine(folder, name + ".dll");

            if (File.Exists(path))
            {
                return path;
            }

            string original = probeFolder == null ? null : Path.Combine(probeFolder, name + ".dll");

            if (original == null || !File.Exists(original))
            {
                return null;
            }

            File.Copy(original, path, true);
            return path;
        }
    }
}
