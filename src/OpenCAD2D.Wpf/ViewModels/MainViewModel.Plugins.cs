using System;
using System.Collections.Generic;
using System.Linq;
using OpenCAD2D.Plugins;

namespace OpenCAD2D.Wpf
{
    /// <summary>The command plugins of the presenter's window: the manager and the commands that reach them.</summary>
    public sealed partial class MainViewModel
    {
        private readonly List<CadWorkspaceExtension> _builtInTabs = new List<CadWorkspaceExtension>();
        private CadPluginManager _plugins;
        private CadBuiltInTab _pluginsTab;
        private PluginRibbonViewModel _pluginRibbon;

        // The plugins whose last load stopped on their packages, by the full path of the folder: what the badge of a row says.
        private readonly Dictionary<string, ToolPackageState> _packageStates = new Dictionary<string, ToolPackageState>(
            StringComparer.OrdinalIgnoreCase
        );

        /// <summary>Raised by PLUGINLOAD (NETLOAD, and the Load Plugin button): the window shows its Open dialog for a .dll, then calls <see cref="LoadPlugin"/>.</summary>
        public event EventHandler PluginLoadRequested;

        /// <summary>Gets the manager of the command plugins; null until <see cref="UsePlugins"/>.</summary>
        public CadPluginManager Plugins => _plugins;

        /// <summary>Gets the view model of the Plugins ribbon tab: the panels the plugins loaded add with <see cref="CadPluginHost.AddRibbonPanel"/>.</summary>
        public PluginRibbonViewModel PluginRibbon => _pluginRibbon ?? (_pluginRibbon = new PluginRibbonViewModel());

        /// <summary>
        /// Gets the Plugins ribbon tab, after the extensions' tabs and Annotate and View. The presenter has no panel of its own
        /// on it, so it is shown only while a plugin has added a panel with <see cref="CadPluginHost.AddRibbonPanel"/>.
        /// </summary>
        public CadWorkspaceExtension PluginsTab
        {
            get
            {
                if (_pluginsTab == null)
                {
                    _pluginsTab = new CadBuiltInTab("Plugins", PluginRibbon, false);
                    _builtInTabs.Add(_pluginsTab);
                }

                return _pluginsTab;
            }
        }

        /// <summary>
        /// Puts the command plugins into the window: their commands on the command line of every document, open or opened later
        /// (after LAYMCUR and LAYMCH, and after the window's own PLUGINLOAD where the window shows a file dialog); with <paramref name="loadAtStart"/> the plugins of the plugin folder and the startup list are loaded,
        /// each result a line of the command line.
        /// </summary>
        /// <param name="plugins">The manager.</param>
        /// <param name="loadAtStart">Whether to load the plugins of the plugin folder and the startup list now.</param>
        /// <returns>What came of each plugin loaded at start; empty without <paramref name="loadAtStart"/>.</returns>
        /// <exception cref="ArgumentNullException">Thrown when the manager is null.</exception>
        /// <exception cref="InvalidOperationException">Thrown when a manager is in use already.</exception>
        public IReadOnlyList<CadPluginResult> UsePlugins(CadPluginManager plugins, bool loadAtStart)
        {
            if (plugins == null)
            {
                throw new ArgumentNullException(nameof(plugins));
            }

            if (_plugins != null)
            {
                throw new InvalidOperationException("The window has a plugin manager already.");
            }

            _plugins = plugins;

            // A load a command makes, PLUGINLOAD or PLUGINRELOAD, leaves its packages' state as the window's own loads do.
            plugins.AfterCommandLoad = NotePackages;
            plugins.PluginsChanged += (s, e) =>
            {
                RememberApplications();
                RebuildPluginUi();
            };

            foreach (KnownFile file in _files.Values.Where(f => f.Session != null))
            {
                AttachPlugins(file.Session.Registry);
            }

            IReadOnlyList<CadPluginResult> results = loadAtStart
                ? plugins.LoadAtStart()
                : Array.Empty<CadPluginResult>();

            foreach (CadPluginResult result in results)
            {
                SayResult(result);
            }

            RebuildPluginUi();
            Raise(nameof(Plugins));
            return results;
        }

        /// <summary>Loads a command plugin from a .dll or a plugin's folder, as PLUGINLOAD does, and says what came of it.</summary>
        /// <param name="path">The path.</param>
        /// <returns>What came of it; null without a manager.</returns>
        public CadPluginResult LoadPlugin(string path)
        {
            CadPluginResult result = Report(
                _plugins == null || string.IsNullOrWhiteSpace(path) ? null : _plugins.Load(path)
            );
            NotePackages(path, result);
            return result;
        }

        /// <summary>Loads a plugin again from its file, as PLUGINRELOAD does, and says what came of it.</summary>
        /// <param name="name">The plugin's name.</param>
        /// <returns>What came of it; null without a manager.</returns>
        public CadPluginResult ReloadPlugin(string name)
        {
            CadPluginResult result = Report(_plugins?.Reload(name));
            NotePackages(_plugins?.Find(name)?.Path, result);
            return result;
        }

        /// <summary>Unloads a plugin, as PLUGINUNLOAD does, and says what came of it.</summary>
        /// <param name="name">The plugin's name.</param>
        /// <returns>What came of it; null without a manager.</returns>
        public CadPluginResult UnloadPlugin(string name) => Report(_plugins?.Unload(name));

        /// <summary>
        /// What a load of the plugin at a path left on its packages: <see cref="ToolPackageState"/> where the question before
        /// the download was answered No, or the restore failed, and the load has not succeeded since; null otherwise.
        /// </summary>
        /// <param name="path">The plugin's folder or its .csproj.</param>
        internal ToolPackageState PackageStateOf(string path)
        {
            _packageStates.TryGetValue(PluginFolderOf(path), out ToolPackageState state);
            return state;
        }

        /// <summary>Forgets what a plugin's packages left behind, as Remove takes the plugin out of the list.</summary>
        internal void ForgetPackageState(string path) => _packageStates.Remove(PluginFolderOf(path));

        /// <summary>
        /// Keeps what a load said of the plugin's packages, or forgets it where the load went well or stopped for another
        /// reason. A plugin held back by its packages is kept in the known applications, so that its row stays in the list,
        /// unticked, with its badge.
        /// </summary>
        private void NotePackages(string path, CadPluginResult result)
        {
            if (string.IsNullOrWhiteSpace(path) || result == null)
            {
                return;
            }

            string folder = PluginFolderOf(path);
            ToolPackageState state = ToolPackageState.From(result);

            if (state == null)
            {
                _packageStates.Remove(folder);
                return;
            }

            _packageStates[folder] = state;

            if (!_settings.KnownApplications.ContainsKey(folder))
            {
                ChangeSettings(
                    s =>
                        s.SetKnownApplications(
                            s.KnownApplications.Concat(new[] { new KeyValuePair<string, string>(folder, string.Empty) })
                        ),
                    "a plugin waiting for its packages was remembered"
                );
            }
        }

        /// <summary>The full path of a plugin's folder: a .csproj stands for the folder it lies in.</summary>
        private static string PluginFolderOf(string path)
        {
            string full = AppLoadViewModel.Normal(path).TrimEnd('\\', '/');
            return string.Equals(System.IO.Path.GetExtension(full), ".csproj", StringComparison.OrdinalIgnoreCase)
                ? System.IO.Path.GetDirectoryName(full)
                : full;
        }

        /// <summary>Asks the window for the file dialog of PLUGINLOAD.</summary>
        internal void RequestPluginLoad() => PluginLoadRequested?.Invoke(this, EventArgs.Empty);

        /// <summary>
        /// Adds the plugins' commands to a document's command line: first the window's own PLUGINLOAD, which shows a file dialog,
        /// where the window listens for it, then the manager's, which leaves a command already there.
        /// </summary>
        private void AttachPlugins(CadCommandRegistry registry)
        {
            if (_plugins == null)
            {
                return;
            }

            if (PluginLoadRequested != null && registry.Find(CadPluginManager.LoadCommand) == null)
            {
                registry.Add(new CadCommand(CadPluginManager.LoadCommand, PluginLoadByDialog));
            }

            foreach (string refused in _plugins.Attach(registry))
            {
                Say(refused);
            }
        }

        private void DetachPlugins(CadCommandRegistry registry) => _plugins?.Detach(registry);

        /// <summary>PLUGINLOAD with a window: its Open dialog for a .dll, the load done when a file is chosen; with FILEDIA 0 the path is asked on the command line.</summary>
        private IEnumerable<CadPrompt> PluginLoadByDialog(CadCommandContext c)
        {
            if (!c.Session.FileDialogs)
            {
                return _plugins.LoadByPath(c);
            }

            RequestPluginLoad();
            return Array.Empty<CadPrompt>();
        }

        private CadPluginResult Report(CadPluginResult result)
        {
            if (result == null)
            {
                Say("No plugin manager is in use.");
                return null;
            }

            SayResult(result);
            return result;
        }

        /// <summary>Writes what came of a load, a line per line of it: a source plugin that did not compile has a line per error.</summary>
        private void SayResult(CadPluginResult result)
        {
            foreach (string line in result.ToString().Split('\n'))
            {
                Say(line);
            }
        }
    }

    /// <summary>A ribbon tab of the presenter's own, as Manage and Settings: shown unless made otherwise, its content given.</summary>
    internal sealed class CadBuiltInTab : CadWorkspaceExtension
    {
        private readonly string _header;

        public CadBuiltInTab(string header, object content, bool isShown = true)
        {
            _header = header;
            TabContent = content;
            IsTabVisible = isShown;
        }

        public override string TabHeader => _header;

        public void SetVisible(bool visible) => IsTabVisible = visible;
    }
}
