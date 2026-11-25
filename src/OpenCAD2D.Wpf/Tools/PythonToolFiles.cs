using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading;
using GeometryHelper;
using OpenCAD2D.Plugins;

namespace OpenCAD2D.Wpf
{
    /// <summary>
    /// The Python tools of the window: PYLOAD and the start load a tool's folder, which shows its commands and ribbon buttons from
    /// its <c>plugin.json</c> and starts no Python; the tool's own process starts at the first command typed, after its venv is
    /// ready (<see cref="PythonEnvironments"/>, made off the window's thread, with the question of pip in a dialog of the window,
    /// or on the command line where the window shows none); PYRUN runs a lone <c>.py</c> file once in a process of the default
    /// venv. Everything here runs on the window's thread but the environment, which is made on a thread of its own. What the
    /// environment of each tool is doing, or left undone, is its <see cref="PythonToolState"/>, which the window's rows show.
    /// </summary>
    internal sealed class PythonToolFiles : IPythonFiles, IDisposable
    {
        private const string Run = "PYRUN";

        private readonly Func<CadToolHost> _makeHost;
        private readonly PythonEnvironments _environments;
        private readonly Func<string> _pythonPath;
        private readonly Func<string, string> _loadCheck;
        private readonly Action<Action> _post;
        private readonly CadPluginSettingStore _settings;
        private readonly Func<IEnumerable<string>> _snapKeywordsTaken;
        private readonly List<CadCommandRegistry> _registries = new List<CadCommandRegistry>();
        private readonly List<LoadedTool> _loaded = new List<LoadedTool>();
        private readonly object _prepareGate = new object();
        private CadToolHost _host;
        private CadRemoteToolRun _asking;
        private string _askingFolder;
        private bool _declined;
        private volatile bool _disposed;

        /// <summary>Makes the tools of a window; nothing is started, made or written until a tool is loaded and used.</summary>
        /// <param name="makeHost">Makes the host of the tools' processes, once, when the first tool loads or the first script runs.</param>
        /// <param name="environments">The venvs of the tools. Its <see cref="PythonEnvironments.Confirm"/> is set to the question of pip (the dialog of <see cref="AskQuestion"/>, else the command line) unless it has one already; either is wrapped to know whether it was answered No.</param>
        /// <param name="pythonPath">The Python of Global settings, read each time an environment is made; empty for the Python found on the machine.</param>
        /// <param name="loadCheck">What a file read at the user's request is checked with: the path in, null out to allow it, else the text that refuses it.</param>
        /// <param name="post">Runs an action on the window's thread after the work in hand, which PYRUN's run begins by once its own command has ended, and which a tool's snap questions and answers are handed on by.</param>
        /// <param name="settings">Keeps the values of the settings the tools declare, in the settings file.</param>
        /// <param name="snapKeywordsTaken">Gives the keywords of the object snaps the plugins loaded have, which a tool's snap may not use.</param>
        /// <exception cref="ArgumentNullException">Thrown when an argument is null.</exception>
        public PythonToolFiles(
            Func<CadToolHost> makeHost,
            PythonEnvironments environments,
            Func<string> pythonPath,
            Func<string, string> loadCheck,
            Action<Action> post,
            CadPluginSettingStore settings,
            Func<IEnumerable<string>> snapKeywordsTaken
        )
        {
            _makeHost = makeHost ?? throw new ArgumentNullException(nameof(makeHost));
            _environments = environments ?? throw new ArgumentNullException(nameof(environments));
            _pythonPath = pythonPath ?? throw new ArgumentNullException(nameof(pythonPath));
            _loadCheck = loadCheck ?? throw new ArgumentNullException(nameof(loadCheck));
            _post = post ?? throw new ArgumentNullException(nameof(post));
            _settings = settings ?? throw new ArgumentNullException(nameof(settings));
            _snapKeywordsTaken = snapKeywordsTaken ?? throw new ArgumentNullException(nameof(snapKeywordsTaken));

            Func<string, IReadOnlyList<PythonPackage>, bool> ask = environments.Confirm ?? Ask;
            environments.Confirm = (tool, packages) =>
            {
                bool approved = ask(tool, packages);
                _declined = !approved;
                return approved;
            };
        }

        /// <summary>
        /// Gets or sets how the question of pip is put in a dialog: given the question (<see cref="PythonPackageQuestion"/>), it shows
        /// it over the window and gives the answer, true for Yes. It is called on the window's thread, which the environment's thread
        /// waits for. Null, or where the run that waits has FILEDIA 0, the question is asked on the command line.
        /// </summary>
        internal Func<PackageQuestionViewModel, bool> AskQuestion { get; set; }

        /// <summary>Occurs, on the window's thread, when a tool's state changed: its packages are being installed, the question was answered No, an install failed, or the tool is ready.</summary>
        internal event EventHandler<PythonToolStateEventArgs> StateChanged;

        /// <summary>Occurs, on the window's thread, when a tool has answered the question of its snaps for some drawings: the canvas follows the cursor again, so that the point shows with no other move.</summary>
        internal event Action<IReadOnlyList<CadDocument>> SnapsAnswered;

        /// <summary>A tool that is loaded: what it declared, the tool of the host that runs it, and what it added to the window.</summary>
        private sealed class LoadedTool
        {
            internal string Folder;
            internal string Name;
            internal CadFolderTool Tool;
            internal PythonToolState State = PythonToolState.Ready;
            internal IReadOnlyList<CadPluginSetting> Settings = Array.Empty<CadPluginSetting>();
            internal readonly List<KeyValuePair<string, string>> Aliases = new List<KeyValuePair<string, string>>();
            internal CadPluginHost Host;
        }

        /// <inheritdoc />
        public IReadOnlyList<string> Loaded => _loaded.Select(t => t.Folder).ToArray();

        /// <inheritdoc />
        public IReadOnlyList<CadPluginHost> PluginHosts => _loaded.Select(t => t.Host).ToArray();

        /// <inheritdoc />
        public event EventHandler PluginHostsChanged;

        /// <summary>Gets whether a tool's process is alive.</summary>
        public bool HasRunning => _host != null && _host.Tools.Any(t => t.IsRunning);

        /// <summary>Gets the tools loaded, in the order they first loaded.</summary>
        internal IReadOnlyList<CadFolderTool> Tools => _loaded.Select(t => t.Tool).ToArray();

        private CadToolHost Host
        {
            get
            {
                if (_host == null)
                {
                    _host = _makeHost();
                    _host.Post = _post;
                    _host.SnapsAnswered = documents => SnapsAnswered?.Invoke(documents);
                }

                return _host;
            }
        }

        // ---- Commands -------------------------------------------------------------------------------------------------

        /// <summary>Adds PYRUN and the commands of the tools loaded to a registry, and to each command a tool loaded from now on declares.</summary>
        /// <param name="registry">The registry of a drawing.</param>
        /// <exception cref="ArgumentNullException">Thrown when the registry is null.</exception>
        public void AddCommandsTo(CadCommandRegistry registry)
        {
            if (registry == null)
            {
                throw new ArgumentNullException(nameof(registry));
            }

            if (!_registries.Contains(registry))
            {
                _registries.Add(registry);
            }

            registry.Add(new CadCommand(Run, RunFileCommand));

            foreach (LoadedTool tool in _loaded)
            {
                AddTo(registry, tool);
            }
        }

        private static void AddTo(CadCommandRegistry registry, LoadedTool tool)
        {
            foreach (CadCommand command in tool.Tool.Commands)
            {
                registry.Add(command);
            }

            foreach (KeyValuePair<string, string> alias in tool.Aliases)
            {
                if (registry.Shortcuts.Aliases.ContainsKey(alias.Key) || registry.Find(alias.Key) != null)
                {
                    continue;
                }

                var aliases = new List<KeyValuePair<string, string>>(registry.Shortcuts.Aliases) { alias };
                registry.Shortcuts = new CadShortcuts(aliases, registry.Shortcuts.Keys);
            }
        }

        private static void RemoveFrom(CadCommandRegistry registry, LoadedTool tool)
        {
            foreach (CadCommand command in tool.Tool.Commands)
            {
                if (ReferenceEquals(registry.Find(command.Name), command))
                {
                    registry.Remove(command.Name);
                }
            }

            var aliases = registry
                .Shortcuts.Aliases.Where(a =>
                    !tool.Aliases.Any(t =>
                        string.Equals(t.Key, a.Key, StringComparison.OrdinalIgnoreCase)
                        && string.Equals(t.Value, a.Value, StringComparison.OrdinalIgnoreCase)
                    )
                )
                .ToList();

            if (aliases.Count != registry.Shortcuts.Aliases.Count)
            {
                registry.Shortcuts = new CadShortcuts(aliases, registry.Shortcuts.Keys);
            }
        }

        // ---- Loading --------------------------------------------------------------------------------------------------

        /// <summary>
        /// Loads a tool: the folder, or its <c>requirements.txt</c>. Its commands are added to every drawing and what its
        /// <c>plugin.json</c> declares for the ribbon to the window; <c>Name loaded: A, B.</c> is written, or why it was not
        /// loaded. No Python starts. A tool loaded again replaces its commands.
        /// </summary>
        /// <param name="session">The session of the drawing the lines are written to.</param>
        /// <param name="path">The folder or its <c>requirements.txt</c>.</param>
        /// <returns>True when the tool loaded.</returns>
        /// <exception cref="ArgumentNullException">Thrown when the session or the path is null.</exception>
        public bool Load(CadCommandSession session, string path)
        {
            if (session == null)
            {
                throw new ArgumentNullException(nameof(session));
            }

            if (path == null)
            {
                throw new ArgumentNullException(nameof(path));
            }

            if (PythonToolFolder.IsLoneFile(path))
            {
                session.Write(PythonToolFolder.LoneFileLine(path));
                return false;
            }

            string folder = PythonToolFolder.Resolve(path);

            if (folder == null)
            {
                session.Write(
                    "Cannot find a Python tool at \"" + path + "\": a Python tool is a folder with requirements.txt."
                );
                return false;
            }

            string name = Path.GetFileName(folder);
            string init = Path.Combine(folder, "__init__.py");

            if (!File.Exists(init))
            {
                session.Write(
                    name + " has no __init__.py: a Python tool is a folder with requirements.txt and __init__.py."
                );
                return false;
            }

            string refusal = _loadCheck(init);

            if (refusal != null)
            {
                session.Write(refusal);
                return false;
            }

            CadPluginManifest manifest = null;
            string manifestPath = Path.Combine(folder, CadPluginManifest.FileName);

            if (File.Exists(manifestPath))
            {
                try
                {
                    manifest = CadPluginManifest.Read(manifestPath);
                }
                catch (Exception exception) when (exception is FormatException || exception is IOException)
                {
                    session.Write(name + " was not loaded: plugin.json: " + exception.Message);
                    return false;
                }
            }

            // A tool loaded again replaces its commands and what it added to the window.
            int index = _loaded.FindIndex(t => Same(t.Folder, folder));
            bool hadAdditions = index >= 0 && !_loaded[index].Host.IsEmpty;

            if (index >= 0)
            {
                Drop(_loaded[index]);
                _loaded.RemoveAt(index);
            }

            var declared = new List<CadPluginCommandEntry>();

            foreach (CadPluginCommandEntry entry in manifest?.CommandEntries ?? new CadPluginCommandEntry[0])
            {
                if (_registries.Any(r => r.Find(entry.Name) != null))
                {
                    session.Write("PYLOAD: " + entry.Name + " is a command already; " + name + " did not define it.");
                }
                else
                {
                    declared.Add(entry);
                }
            }

            CadFolderTool tool;
            var loaded = new LoadedTool { Folder = folder, Name = name };

            try
            {
                tool = Host.Add(
                    folder,
                    (run, beforeSwap) => Prepare(loaded, run, beforeSwap),
                    declared,
                    PythonsFolders(session)
                );
            }
            catch (Exception exception)
                when (exception is IOException
                    || exception is InvalidOperationException
                    || exception is ArgumentException
                )
            {
                session.Write(name + " was not loaded: " + exception.Message);
                return false;
            }

            loaded.Tool = tool;
            loaded.Host = HostOf(name, folder, manifest, tool, session);
            loaded.Settings = _settings.Declare(name, manifest?.Settings ?? Array.Empty<CadPluginSettingEntry>());
            _settings.Apply(name, loaded.Settings);
            tool.DeclareSettings(loaded.Settings);

            foreach (CadPluginCommandEntry entry in declared.Where(e => e.Alias != null))
            {
                loaded.Aliases.Add(new KeyValuePair<string, string>(entry.Alias, entry.Name));
            }

            if (index >= 0)
            {
                _loaded.Insert(index, loaded);
            }
            else
            {
                _loaded.Add(loaded);
            }

            foreach (CadCommandRegistry registry in _registries)
            {
                AddTo(registry, loaded);
            }

            if (manifest == null || manifest.CommandEntries.Count == 0)
            {
                session.Write(name + " declares no command in plugin.json.");
            }
            else
            {
                string names = string.Join(", ", declared.Select(e => e.Name).OrderBy(n => n, StringComparer.Ordinal));
                session.Write(name + " loaded" + (names.Length == 0 ? "." : ": " + names + "."));
            }

            if (hadAdditions || !loaded.Host.IsEmpty)
            {
                PluginHostsChanged?.Invoke(this, EventArgs.Empty);
            }

            return true;
        }

        /// <summary>Loads the tools of the start: the folders directly in each Pythons folder that hold a <c>requirements.txt</c>, then the Startup Suite; a failure writes its line and the next one loads.</summary>
        /// <param name="session">The session of the first drawing.</param>
        /// <param name="pythonsFolders">The Pythons folders.</param>
        /// <param name="suite">The Python Startup Suite of the settings.</param>
        public void Startup(CadCommandSession session, IEnumerable<string> pythonsFolders, IEnumerable<string> suite)
        {
            foreach (string path in ToolsToLoad(pythonsFolders, suite))
            {
                Load(session, path);
            }
        }

        /// <summary>
        /// Lists what loads at start: the tool folders directly in each Pythons folder, the folders in the order given and the
        /// tools of each by name ignoring case, then by the name as written, then the Startup Suite in its order; a path reached
        /// twice is listed once. A <c>.py</c> file of the suite is listed as it is, for <see cref="Load"/> to refuse; a <c>.py</c>
        /// directly in a Pythons folder is not a tool and is not listed.
        /// </summary>
        /// <param name="pythonsFolders">The Pythons folders.</param>
        /// <param name="suite">The Startup Suite.</param>
        /// <returns>The paths, in the order they load.</returns>
        internal static IReadOnlyList<string> ToolsToLoad(IEnumerable<string> pythonsFolders, IEnumerable<string> suite)
        {
            var found = new List<string>();
            var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

            foreach (string folder in pythonsFolders ?? new string[0])
            {
                if (string.IsNullOrWhiteSpace(folder) || !Directory.Exists(folder))
                {
                    continue;
                }

                var here = new List<string>();

                foreach (string child in Directory.GetDirectories(folder))
                {
                    if (PythonToolFolder.Resolve(child) != null)
                    {
                        here.Add(Path.GetFullPath(child));
                    }
                }

                here.Sort(
                    (a, b) =>
                    {
                        int order = string.Compare(
                            Path.GetFileName(a),
                            Path.GetFileName(b),
                            StringComparison.OrdinalIgnoreCase
                        );
                        return order != 0 ? order : string.CompareOrdinal(a, b);
                    }
                );

                foreach (string child in here)
                {
                    if (seen.Add(child))
                    {
                        found.Add(child);
                    }
                }
            }

            foreach (string entry in suite ?? new string[0])
            {
                if (string.IsNullOrWhiteSpace(entry))
                {
                    continue;
                }

                string resolved = PythonToolFolder.Resolve(entry) ?? entry.Trim();

                if (seen.Add(resolved))
                {
                    found.Add(resolved);
                }
            }

            return found;
        }

        private static string PythonsFolders(CadCommandSession session)
        {
            IReadOnlyList<string> folders = session.Document.SupportRoots.ExistingOf(CadSupportKind.Pythons);
            return folders.Count == 0 ? null : string.Join(Path.PathSeparator.ToString(), folders);
        }

        private static bool Same(string a, string b) => string.Equals(a, b, StringComparison.OrdinalIgnoreCase);

        /// <summary>
        /// What the manifest declares for the ribbon and the object snaps, as the host of a plugin holds it. A snap whose keyword
        /// the host refuses, or another snap has (a loaded plugin's, an earlier tool's or an earlier snap of this one), is left
        /// out with a line, and the rest of the tool loads.
        /// </summary>
        private CadPluginHost HostOf(
            string name,
            string folder,
            CadPluginManifest manifest,
            CadFolderTool tool,
            CadCommandSession session
        )
        {
            var host = new CadPluginHost(name, folder);

            foreach (CadPluginRibbonTabEntry entry in manifest?.Ribbon ?? new CadPluginRibbonTabEntry[0])
            {
                var tab = new CadRibbonTab(entry.Id, entry.Title);

                foreach (CadPluginRibbonPanelEntry panelEntry in entry.Panels)
                {
                    var panel = new CadRibbonPanel(panelEntry.Title);

                    foreach (CadPluginRibbonButtonEntry buttonEntry in panelEntry.Buttons)
                    {
                        panel.Items.Add(
                            new CadRibbonButton(buttonEntry.Text, buttonEntry.Command)
                            {
                                Size = buttonEntry.IsLarge ? CadRibbonItemSize.Large : CadRibbonItemSize.Standard,
                                ToolTip = buttonEntry.ToolTip,
                                Icon = IconOf(folder, buttonEntry.Icon),
                            }
                        );
                    }

                    tab.Panels.Add(panel);
                }

                host.AddRibbonTab(tab);
            }

            foreach (CadPluginSnapEntry entry in manifest?.Snaps ?? new CadPluginSnapEntry[0])
            {
                string reason = SnapRefusal(host, entry.Keyword);

                if (reason == null)
                {
                    try
                    {
                        host.AddObjectSnap(
                            new CadCustomSnap(entry.Name, entry.Keyword, tool.Snaps.FinderOf(entry))
                            {
                                Glyph = entry.Glyph,
                                IsOnByDefault = entry.IsOnByDefault,
                            }
                        );
                    }
                    catch (ArgumentException exception)
                    {
                        // The host says it as "The snap keyword CT is 2 to 16 letters and digits long."
                        string words = CadPluginSetting.WordsOf(exception);
                        string lead = "The snap keyword " + entry.Keyword + " ";
                        reason =
                            "the keyword "
                            + (words.StartsWith(lead, StringComparison.Ordinal) ? words.Substring(lead.Length) : words);
                    }
                }

                if (reason != null)
                {
                    session.Write(name + ": the snap " + entry.Keyword + " was left out: " + reason);
                }
            }

            host.Close();
            return host;
        }

        /// <summary>Why a snap's keyword is not left to a tool: a plugin loaded, an earlier tool or an earlier snap of this tool has it; null where it is free.</summary>
        private string SnapRefusal(CadPluginHost host, string keyword) =>
            host.ObjectSnaps.Any(s => s.Keyword == keyword)
            || _loaded.Any(t => t.Host.ObjectSnaps.Any(s => s.Keyword == keyword))
            || _snapKeywordsTaken().Contains(keyword)
                ? "another plugin or tool has that keyword."
                : null;

        private static CadPluginIcon IconOf(string folder, string icon)
        {
            if (icon == null)
            {
                return null;
            }

            try
            {
                string path = Path.Combine(folder, icon);
                return File.Exists(path) ? CadPluginIcon.FromFile(path) : null;
            }
            catch (Exception exception)
                when (exception is IOException
                    || exception is ArgumentException
                    || exception is UnauthorizedAccessException
                )
            {
                GeometryHelperLog.Debug("PythonToolFiles: the icon " + icon + " could not be read.", exception);
                return null;
            }
        }

        // ---- Unloading and what a tool has declared -------------------------------------------------------------------

        /// <summary>Gets the names of the commands a tool declared, in the order its <c>plugin.json</c> gives them; empty for a tool not loaded.</summary>
        /// <param name="path">The folder, or its <c>requirements.txt</c>.</param>
        /// <returns>The names.</returns>
        /// <exception cref="ArgumentNullException">Thrown when the path is null.</exception>
        public IReadOnlyList<string> CommandsOf(string path)
        {
            if (path == null)
            {
                throw new ArgumentNullException(nameof(path));
            }

            string folder = PythonToolFolder.Resolve(path) ?? path;
            LoadedTool tool = _loaded.FirstOrDefault(t => Same(t.Folder, folder));
            return tool == null ? new string[0] : tool.Tool.Commands.Select(c => c.Name).ToArray();
        }

        /// <summary>Gets the settings a loaded tool declares, which Load/Unload Applications shows in Plugin Settings, in the order its <c>plugin.json</c> gives them; empty for a tool not loaded, or one that declares none.</summary>
        /// <param name="path">The folder, or its <c>requirements.txt</c>.</param>
        /// <returns>The settings, made over the settings store.</returns>
        /// <exception cref="ArgumentNullException">Thrown when the path is null.</exception>
        internal IReadOnlyList<CadPluginSetting> SettingsOf(string path)
        {
            if (path == null)
            {
                throw new ArgumentNullException(nameof(path));
            }

            string folder = PythonToolFolder.Resolve(path) ?? path;
            return _loaded.FirstOrDefault(t => Same(t.Folder, folder))?.Settings ?? Array.Empty<CadPluginSetting>();
        }

        /// <summary>Gets what a loaded tool's environment is doing or left undone, which the tool's row shows; <see cref="PythonToolState.Ready"/> for a tool not loaded.</summary>
        /// <param name="path">The folder, or its <c>requirements.txt</c>.</param>
        /// <returns>The state.</returns>
        /// <exception cref="ArgumentNullException">Thrown when the path is null.</exception>
        internal PythonToolState StateOf(string path)
        {
            if (path == null)
            {
                throw new ArgumentNullException(nameof(path));
            }

            string folder = PythonToolFolder.Resolve(path) ?? path;
            return _loaded.FirstOrDefault(t => Same(t.Folder, folder))?.State ?? PythonToolState.Ready;
        }

        /// <summary>Unloads a tool: its commands and aliases leave every drawing, its process is told to leave (ended after 5 seconds where it does not), and what it added to the window goes.</summary>
        /// <param name="path">The folder, or its <c>requirements.txt</c>.</param>
        /// <returns>False where the tool was not loaded.</returns>
        /// <exception cref="ArgumentNullException">Thrown when the path is null.</exception>
        public bool Unload(string path)
        {
            if (path == null)
            {
                throw new ArgumentNullException(nameof(path));
            }

            string folder = PythonToolFolder.Resolve(path) ?? path;
            int index = _loaded.FindIndex(t => Same(t.Folder, folder));

            if (index < 0 || _disposed)
            {
                return false;
            }

            LoadedTool tool = _loaded[index];
            _loaded.RemoveAt(index);
            Drop(tool);

            if (!tool.Host.IsEmpty)
            {
                PluginHostsChanged?.Invoke(this, EventArgs.Empty);
            }

            return true;
        }

        private void Drop(LoadedTool tool)
        {
            foreach (CadCommandRegistry registry in _registries)
            {
                RemoveFrom(registry, tool);
            }

            tool.Tool.Unload();
        }

        // ---- The environment of a tool and the question of pip --------------------------------------------------------

        /// <summary>
        /// Makes a tool's venv ready, on the thread it is called on (not the window's). One at a time: the question of pip is that of
        /// the run that waits for it. The tool's state follows: <see cref="AppLoadBadge.Installing"/> from the yes until pip is done,
        /// then <see cref="AppLoadBadge.NeedsPackages"/> where the question was answered No, <see cref="AppLoadBadge.InstallFailed"/>
        /// with the lines where the venv could not be made, the lines alone where a change failed and the tool runs on the venv it had.
        /// </summary>
        private PythonEnvironment Prepare(LoadedTool tool, CadRemoteToolRun run, Action beforeSwap)
        {
            // A snap's start asks nobody: no run waits to be asked, and nothing is changed that a mouse move has no business
            // changing. A tool whose packages are not approved, or are being installed, is not started; nor is one whose gate is in use.
            if (run == null)
            {
                if (tool.State.Badge != AppLoadBadge.None || !Monitor.TryEnter(_prepareGate))
                {
                    return PythonEnvironment.Failed(
                        tool.Folder,
                        tool.Name + " was not started: its packages need a command to make them ready."
                    );
                }

                try
                {
                    return _environments.Prepare(
                        tool.Folder,
                        PythonInstallation.Find(_pythonPath()),
                        beforeSwap,
                        null,
                        false
                    );
                }
                finally
                {
                    Monitor.Exit(_prepareGate);
                }
            }

            lock (_prepareGate)
            {
                _asking = run;
                _askingFolder = tool.Folder;
                _declined = false;

                try
                {
                    PythonEnvironment environment = _environments.Prepare(
                        tool.Folder,
                        PythonInstallation.Find(_pythonPath()),
                        beforeSwap,
                        () => SetState(tool, PythonToolState.Installing())
                    );
                    SetState(
                        tool,
                        !environment.IsReady
                                ? (
                                    _declined
                                        ? PythonToolState.NeedsPackages()
                                        : PythonToolState.InstallFailed(environment.Error)
                                )
                            : environment.Failure != null ? PythonToolState.Kept(environment.Failure)
                            : PythonToolState.Ready
                    );
                    return environment;
                }
                catch (Exception ex) when (!(ex is OutOfMemoryException))
                {
                    SetState(
                        tool,
                        PythonToolState.InstallFailed(
                            tool.Name + " was not started: its environment could not be made (" + ex.Message + ")."
                        )
                    );
                    throw;
                }
                finally
                {
                    _asking = null;
                    _askingFolder = null;
                }
            }
        }

        /// <summary>Sets a tool's state on the window's thread, where the rows are drawn, and says so; waits for it, so that the state is shown before the environment is handed on. Nothing is set where the window is gone.</summary>
        private void SetState(LoadedTool tool, PythonToolState state)
        {
            if (_disposed || _host == null)
            {
                return;
            }

            try
            {
                _host.Dispatcher.Invoke(() =>
                {
                    tool.State = state;
                    StateChanged?.Invoke(this, new PythonToolStateEventArgs(tool.Name, tool.Folder, state));
                });
            }
            catch (Exception ex) when (!(ex is OutOfMemoryException))
            {
                GeometryHelperLog.Warn("PythonToolFiles: the state of " + tool.Name + " could not be shown.", ex);
            }
        }

        private PythonEnvironment PrepareDefault(CadRemoteToolRun run, Action beforeSwap)
        {
            lock (_prepareGate)
            {
                _asking = run;
                _askingFolder = null;

                try
                {
                    return _environments.Prepare(
                        _environments.DefaultTool(),
                        PythonInstallation.Find(_pythonPath()),
                        beforeSwap
                    );
                }
                finally
                {
                    _asking = null;
                }
            }
        }

        /// <summary>
        /// Asks whether pip may install the packages: with the question of the window, over it and on its thread, where
        /// <see cref="AskQuestion"/> is set and the run that waits has file dialogs on (FILEDIA 1); else on the command line.
        /// Called on the thread that makes the environment, which waits for the answer.
        /// </summary>
        /// <param name="tool">The name of the tool.</param>
        /// <param name="packages">What pip would install.</param>
        /// <returns>True for Yes.</returns>
        private bool Ask(string tool, IReadOnlyList<PythonPackage> packages)
        {
            Func<PackageQuestionViewModel, bool> dialog = AskQuestion;
            CadRemoteToolRun run = _asking;
            string folder = _askingFolder;

            if (dialog == null || run == null || folder == null)
            {
                return AskOnCommandLine(tool, packages);
            }

            bool dialogs = false;
            _host.Dispatcher.Invoke(() => dialogs = run.Session.FileDialogs);

            if (!dialogs)
            {
                return AskOnCommandLine(tool, packages);
            }

            PackageQuestionViewModel question = PythonPackageQuestion.Question(
                tool,
                Path.Combine(folder, "requirements.txt"),
                packages
            );
            bool answer = false;
            _host.Dispatcher.Invoke(() => answer = dialog(question));
            return answer;
        }

        /// <summary>
        /// Asks on the command line of the run that waits for the environment whether pip may install the packages: they are
        /// written, one to a line, and <c>Install with pip [Yes/No] &lt;No&gt;:</c> is asked as the run's own question; Esc, or
        /// anything but Yes, is a no. Called on the thread that makes the environment, which waits for the answer.
        /// </summary>
        /// <param name="tool">The name of the tool.</param>
        /// <param name="packages">What pip would install.</param>
        /// <returns>True for Yes.</returns>
        internal bool AskOnCommandLine(string tool, IReadOnlyList<PythonPackage> packages)
        {
            CadRemoteToolRun run = _asking;

            if (run == null)
            {
                return false;
            }

            var question = new CadRemoteToolRun.RunQuestion
            {
                Kind = "keyword",
                Message = "Install with pip",
                Keywords = new[] { "Yes", "No" },
                Default = "No",
            };
            var lines = new StringBuilder(tool + " needs these packages from the Python Package Index:");

            foreach (PythonPackage package in packages)
            {
                lines
                    .Append('\n')
                    .Append("  ")
                    .Append(package)
                    .Append(package.IsRequested ? string.Empty : " (needed by another)");
            }

            _host.Dispatcher.Invoke(() =>
            {
                foreach (string line in lines.ToString().Split('\n'))
                {
                    run.Session.Write(line);
                }

                run.Pending = question;

                if (!run.Session.Resume(run.Command))
                {
                    run.Pending = null;
                    question.Cancelled = true;
                    question.Done.Set();
                }
            });

            while (!question.Done.Wait(50))
            {
                if (_disposed)
                {
                    return false;
                }
            }

            return !question.Cancelled
                && question.Input.Kind == CadInputKind.Keyword
                && string.Equals(question.Input.Text, "Yes", StringComparison.OrdinalIgnoreCase);
        }

        // ---- PYRUN ----------------------------------------------------------------------------------------------------

        /// <summary>PYRUN: the file by the file dialog where the window shows one, else asked on the command line as SCRIPT asks; the run itself begins after this command has ended.</summary>
        private IEnumerable<CadPrompt> RunFileCommand(CadCommandContext c)
        {
            string name = null;
            bool asked = false;

            if (c.Session.FileDialogs)
            {
                CadUiRequestEventArgs reply = c.Session.Ask(
                    "GETFILED",
                    c.Document,
                    new CadGetFileRequest("Run Python file", string.Empty, "py", 4)
                );

                if (reply.Handled)
                {
                    asked = true;
                    name = reply.Answer;
                }
            }

            if (!asked)
            {
                yield return CadPrompt.ForString("Enter Python file name");
                name = c.Input.Kind == CadInputKind.Text ? c.Input.Text.Trim().Trim('"') : null;
            }

            if (string.IsNullOrEmpty(name))
            {
                yield break;
            }

            if (!Path.HasExtension(name))
            {
                name += ".py";
            }

            if (!Path.IsPathRooted(name) && c.Document.HasFile)
            {
                name = Path.Combine(Path.GetDirectoryName(Path.GetFullPath(c.Document.FileName)), name);
            }

            // The command is running now: the run is posted, to begin when it has ended.
            CadCommandSession session = c.Session;
            string file = name;
            _post(() => RunNow(session, file));
        }

        /// <summary>Runs a file once as <c>python file.py</c> does, in a process of the default venv, as a run named PYRUN: one undo step, Esc stops it. The line of why not is written where it cannot begin.</summary>
        /// <param name="session">The session of the drawing the run works in.</param>
        /// <param name="path">The file.</param>
        internal void RunNow(CadCommandSession session, string path)
        {
            if (_disposed)
            {
                return;
            }

            if (session.IsRunning)
            {
                session.Write("A command is running: finish it or press Esc first.");
                return;
            }

            string full;

            try
            {
                full = Path.GetFullPath(path);
            }
            catch (Exception exception)
                when (exception is ArgumentException
                    || exception is NotSupportedException
                    || exception is PathTooLongException
                )
            {
                session.Write("Cannot find Python file \"" + path + "\".");
                return;
            }

            string refusal = _loadCheck(full);

            if (refusal == null && !File.Exists(full))
            {
                refusal = "Cannot find Python file \"" + path + "\".";
            }

            if (refusal != null)
            {
                session.Write(refusal);
                return;
            }

            CadFolderTool tool;

            try
            {
                tool = Host.AddScript(full, PrepareDefault);
            }
            catch (Exception exception) when (exception is IOException || exception is InvalidOperationException)
            {
                session.Write(Run + ": " + exception.Message);
                return;
            }

            if (!session.Start(tool.Commands[0]))
            {
                tool.Unload();
            }
        }

        /// <summary>Ends every tool's process and closes the channel of the tools.</summary>
        public void Dispose()
        {
            _disposed = true;
            _host?.Dispose();
        }
    }
}
