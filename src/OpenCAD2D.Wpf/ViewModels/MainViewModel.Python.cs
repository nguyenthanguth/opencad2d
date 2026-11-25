using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Windows.Threading;
using OpenCAD2D.Plugins;

namespace OpenCAD2D.Wpf
{
    /// <summary>
    /// How the window reaches the Python tools loaded in the process: load one into a drawing, list those loaded and the
    /// commands each declared, and unload one. Over <see cref="PythonToolFiles"/> in the app; a test gives a fake, since a tool
    /// starts real processes.
    /// </summary>
    internal interface IPythonFiles
    {
        /// <summary>Loads a tool as <see cref="PythonToolFiles.Load"/> does; true when it loaded.</summary>
        bool Load(CadCommandSession session, string path);

        /// <summary>Gets the tools loaded and not unloaded, as full paths of their folders, in the order they first loaded.</summary>
        IReadOnlyList<string> Loaded { get; }

        /// <summary>Gets the names of the commands a loaded tool declared, in its order; empty for a tool not loaded.</summary>
        IReadOnlyList<string> CommandsOf(string path);

        /// <summary>Takes a loaded tool's commands out of every drawing; false where it was not loaded.</summary>
        bool Unload(string path);

        /// <summary>Gets the plugin host of each tool loaded, in the order they first loaded: what each added to the window.</summary>
        IReadOnlyList<CadPluginHost> PluginHosts { get; }

        /// <summary>Occurs when what the tools added to the window changed: one loaded, loaded again or was unloaded.</summary>
        event EventHandler PluginHostsChanged;
    }

    /// <summary>What the window needs to make its Python tools: the venvs and the host of the tools' processes. The app uses its own; a test gives folders of its own.</summary>
    internal sealed class PythonToolSetup
    {
        /// <summary>Gets or sets the venvs of the tools; null for the app's, under <c>%LocalAppData%\OpenCAD2D\python</c>.</summary>
        internal PythonEnvironments Environments { get; set; }

        /// <summary>Gets or sets how the host of the processes is made, given the drawings and the dispatcher that runs the host's work on the window's thread; null for the host of the app.</summary>
        internal Func<ICadRemoteHost, ICadRemoteDispatcher, CadToolHost> MakeHost { get; set; }
    }

    /// <summary>
    /// Python in the window: the commands PYRUN and PYLOAD in every drawing, and the Python tools of the Pythons folder and of the
    /// Python Startup Suite loaded once, when the first drawing opens. A tool is a folder with a <c>requirements.txt</c> whose
    /// commands and ribbon buttons come from its <c>plugin.json</c>; loading starts no Python and the process of a tool starts at
    /// its first command (<see cref="PythonToolFiles"/>). Nothing of Python is in the process of the window.
    /// </summary>
    public sealed partial class MainViewModel
    {
        // The window's PYLOAD, which asks for a tool in a file dialog.
        private const string PythonLoadName = "PYLOAD";

        private PythonToolFiles _pythonTools;
        private bool _pythonStartupDone;
        private IPythonFiles _pythonFiles;
        private Dispatcher _pythonDispatcher;
        private Func<PackageQuestionViewModel, bool> _askPythonPackages;

        /// <summary>Gets or sets the Python tools of the process, as Load/Unload Applications reaches them; set over the tools by <see cref="UsePython(Dispatcher)"/>, null where the app does not use Python.</summary>
        internal IPythonFiles PythonFiles
        {
            get => _pythonFiles;
            set
            {
                IPythonFiles old = _pythonFiles;

                if (ReferenceEquals(old, value))
                {
                    return;
                }

                if (old != null)
                {
                    old.PluginHostsChanged -= OnPythonHostsChanged;
                }

                _pythonFiles = value;

                if (value != null)
                {
                    value.PluginHostsChanged += OnPythonHostsChanged;
                }

                // What the tools added to the window follows the tools: the old ones' tabs and panels go, the new ones' come.
                if ((old != null && old.PluginHosts.Count > 0) || (value != null && value.PluginHosts.Count > 0))
                {
                    RebuildPluginUi();
                }
            }
        }

        private void OnPythonHostsChanged(object sender, EventArgs e) => RebuildPluginUi();

        /// <summary>
        /// Gets or sets how the window asks for the tool of PYLOAD: given the filter (<see cref="AppLoadViewModel.PythonFilter"/>)
        /// and the folder of the last load, it shows an Open dialog with several files allowed and gives the paths chosen (the
        /// <c>requirements.txt</c> of each tool); null where none was. Null where no window shows one, when PYLOAD says so.
        /// </summary>
        public Func<string, string, IReadOnlyList<string>> AskPythonFiles { get; set; }

        /// <summary>
        /// Gets or sets how the question before pip installs a Python tool's packages is shown: given the question, it shows
        /// <see cref="PackageQuestionWindow"/> over the window and gives the answer, true for Yes. It is called on the window's
        /// thread, while the thread that makes the tool's environment waits. Null, or with FILEDIA 0, the question is asked on the
        /// command line of the run that waits for the environment.
        /// </summary>
        public Func<PackageQuestionViewModel, bool> AskPythonPackages
        {
            get => _askPythonPackages;
            set
            {
                _askPythonPackages = value;

                if (_pythonTools != null)
                {
                    _pythonTools.AskQuestion = value;
                }
            }
        }

        /// <summary>Occurs, on the window's thread, when what a Python tool's environment is doing changed: its packages are being installed, the question was answered No, an install failed or ended.</summary>
        internal event EventHandler<PythonToolStateEventArgs> PythonToolStateChanged;

        /// <summary>Gets what a Python tool's environment is doing or left undone, for its row in Load/Unload Applications; ready for a tool not loaded.</summary>
        /// <param name="path">The tool's folder or its <c>requirements.txt</c>.</param>
        internal PythonToolState PythonStateOf(string path) => _pythonTools?.StateOf(path) ?? PythonToolState.Ready;

        /// <summary>Gets whether the app uses Python (<see cref="CadAppHost.EnablePython"/> is true and <see cref="UsePython(Dispatcher)"/> was called).</summary>
        internal bool UsesPython => _pythonTools != null;

        /// <summary>Gets whether a Python tool has a process running in this session: a Python set in Global settings from now on takes effect when a tool's process starts again.</summary>
        internal bool HasPythonStarted => _pythonTools != null && _pythonTools.HasRunning;

        /// <summary>
        /// Gives the commands of Python, PYRUN and PYLOAD and those of the tools loaded, to every drawing open and made later. The
        /// Python is the one the settings name (or found as <see cref="PythonInstallation.Find(string)"/> finds it), read when a
        /// tool's environment is made. Nothing starts: the tools' host and its channel are made at the first tool loaded, a
        /// process at its first command. Called once, on the window's thread, after the settings are applied.
        /// </summary>
        /// <param name="dispatcher">The dispatcher of the window's thread, which owns the drawings.</param>
        internal void UsePython(Dispatcher dispatcher) => UsePython(dispatcher, null);

        /// <summary>As <see cref="UsePython(Dispatcher)"/>, over the venvs and the host a setup gives.</summary>
        /// <param name="dispatcher">The dispatcher of the window's thread.</param>
        /// <param name="setup">The venvs and the host; null for the app's own.</param>
        internal void UsePython(Dispatcher dispatcher, PythonToolSetup setup)
        {
            if (_pythonTools != null)
            {
                return;
            }

            _pythonDispatcher = dispatcher;
            PythonEnvironments environments = setup?.Environments
                ?? PythonEnvironments.ForApp(AppDomain.CurrentDomain.BaseDirectory);
            Func<ICadRemoteHost, ICadRemoteDispatcher, CadToolHost> makeHost = setup?.MakeHost
                ?? (
                    (host, runner) =>
                        new CadToolHost(
                            host,
                            runner,
                            Path.GetDirectoryName(typeof(CadCommand).Assembly.Location)
                                ?? AppDomain.CurrentDomain.BaseDirectory
                        )
                );
            _pythonTools = new PythonToolFiles(
                () =>
                {
                    var remote = new CadAppRemoteHost(this, () => "OpenCAD2D", dispatcher);
                    return makeHost(remote, new SyncingDispatcher(remote, AfterPythonRun));
                },
                environments,
                () => _settings.PythonPath,
                // The same gate as AutoLISP's: a file outside the trusted folders is asked about, or refused (SECURELOAD).
                path => _loadSecurity.MayLoad(path) ? null : "File load canceled: " + path,
                work => dispatcher.BeginInvoke(DispatcherPriority.Background, work),
                PluginSettingStore,
                () => LoadedPlugins().SelectMany(p => p.Plugin.Host.ObjectSnaps).Select(s => s.Keyword).ToList()
            );
            _pythonTools.SnapsAnswered += OnPluginSnapsAnswered;
            _pythonTools.AskQuestion = _askPythonPackages;
            _pythonTools.StateChanged += (sender, e) => PythonToolStateChanged?.Invoke(this, e);
            PythonFiles = _pythonTools;
            Raise(nameof(UsesPython));

            foreach (KnownFile file in _files.Values.Where(f => f.Session != null))
            {
                AddPythonCommands(file.Session.Registry);
            }
        }

        /// <summary>The dispatcher of the tools' channel, which has the window draw again what a request of a tool changed: the lines it wrote, the prompt, the undo steps.</summary>
        private sealed class SyncingDispatcher : ICadRemoteDispatcher
        {
            private readonly ICadRemoteDispatcher _inner;
            private readonly Action _changed;

            internal SyncingDispatcher(ICadRemoteDispatcher inner, Action changed)
            {
                _inner = inner;
                _changed = changed;
            }

            public void Invoke(Action action) =>
                _inner.Invoke(() =>
                {
                    action();
                    _changed();
                });
        }

        /// <summary>
        /// Loads the Python tools of the start, once, when the first drawing opens and after its AutoLISP start: the folders
        /// with a <c>requirements.txt</c> directly in the Pythons folder, then the Python Startup Suite (a <c>.py</c> file there
        /// is refused with the line that says how to make it a tool). No Python starts; nothing happens where the app does not use Python.
        /// </summary>
        /// <param name="session">The session of the drawing that opened.</param>
        private void RunPythonStartup(CadCommandSession session)
        {
            if (_pythonTools == null || _pythonStartupDone)
            {
                return;
            }

            _pythonStartupDone = true;
            _pythonTools.Startup(
                session,
                session.Document.SupportRoots.ExistingOf(CadSupportKind.Pythons),
                _settings.PythonStartupSuite
            );
        }

        /// <summary>
        /// PYLOAD: asks for the tools with <see cref="AskPythonFiles"/> (the <c>requirements.txt</c> of each) and loads them in the
        /// order chosen, as the Load… button of Load/Unload Applications does, the folder chosen kept for the next load.
        /// </summary>
        private void LoadPythonByDialog()
        {
            if (AskPythonFiles == null)
            {
                Say("PYLOAD needs a file dialog, which the window cannot show here.");
                return;
            }

            IReadOnlyList<string> paths = AskPythonFiles(AppLoadViewModel.PythonFilter, Settings.AppLoadFolder);

            if (paths != null && paths.Count > 0)
            {
                using (var loading = new AppLoadViewModel(this))
                {
                    loading.Load(paths);
                }
            }
        }

        /// <summary>Gives a drawing's commands the window's PYLOAD, which asks for a tool in a file dialog.</summary>
        private void AddPythonLoad(CadCommandRegistry registry) =>
            registry.Add(new CadCommand(PythonLoadName, c => RunWindow(LoadPythonByDialog)));

        /// <summary>Gets the Python tools loaded, as full paths of their folders in the order they loaded; empty where the app does not use Python.</summary>
        internal IReadOnlyList<string> LoadedPythonFiles =>
            PythonFiles?.Loaded ?? (IReadOnlyList<string>)Array.Empty<string>();

        /// <summary>Gets the commands a loaded Python tool declared, in the order its plugin.json gives them; empty where it is not loaded.</summary>
        internal IReadOnlyList<string> PythonCommandsOf(string path) =>
            PythonFiles?.CommandsOf(path) ?? (IReadOnlyList<string>)Array.Empty<string>();

        /// <summary>Gets how many runs of the loaded Python tools' commands ended any way but well; 0 where the app does not use Python.</summary>
        internal int PythonRunsNotCompleted => _pythonTools?.Tools.Sum(t => t.RunsNotCompleted) ?? 0;

        /// <summary>Unloads a Python tool: its commands are unknown again in every drawing.</summary>
        internal bool UnloadPythonFile(string path) => PythonFiles != null && PythonFiles.Unload(path);

        /// <summary>
        /// Gives a drawing's commands PYRUN, PYLOAD and the commands of the tools loaded; nothing where the app does not use
        /// Python.
        /// </summary>
        /// <param name="registry">The drawing's registry.</param>
        private void AddPythonCommands(CadCommandRegistry registry)
        {
            if (_pythonTools == null)
            {
                return;
            }

            _pythonTools.AddCommandsTo(registry);
            AddPythonLoad(registry);
        }

        /// <summary>A request of a tool was served, or a run ended: what it wrote to the command line is shown, and the drawing, its undo steps and the prompt are drawn again.</summary>
        private void AfterPythonRun()
        {
            SyncHistory();
            RaiseModified();
            Raise(nameof(Prompt));
            Raise(nameof(IsCommandRunning));
        }

        /// <summary>Ends the tools at the close of the window: every process is ended and the channel of the tools closed.</summary>
        internal void DisposePython() => _pythonTools?.Dispose();
    }
}
