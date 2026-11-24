using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Windows.Input;
using OpenCAD2D.Plugins;

namespace OpenCAD2D.Wpf
{
    /// <summary>How the outcome of loading one application is told: loaded, refused for its type, or failed.</summary>
    public enum AppLoadStatusKind
    {
        /// <summary>Nothing to say yet.</summary>
        None,

        /// <summary>A file was loaded.</summary>
        Loaded,

        /// <summary>A file was refused, for its type or its name.</summary>
        Refused,

        /// <summary>A load failed.</summary>
        Failed,

        /// <summary>Something done that is neither.</summary>
        Info,
    }

    /// <summary>What came of loading one application: how to show it, and the line to show.</summary>
    internal sealed class AppLoadResult
    {
        internal AppLoadResult(AppLoadStatusKind kind, string message, string name = null, string detail = null)
        {
            Kind = kind;
            Message = message;
            Name = name;
            Detail = detail;
        }

        internal AppLoadStatusKind Kind { get; }

        internal string Message { get; }

        /// <summary>Gets the file's name, which the load-result line of the window shows in bold; null where the load was of no file.</summary>
        internal string Name { get; }

        /// <summary>
        /// Gets what the load-result line says after the name: the error the history wrote for a failed load, or the sentence
        /// after the name of a refusal.
        /// </summary>
        internal string Detail { get; }
    }

    /// <summary>
    /// A row of Load/Unload Applications: an AutoLISP file, a Python file or a plugin, with whether it is on, the commands it defines, the file,
    /// and whether it loads at every start. Ticking On loads it and unticking unloads it; ticking At start keeps it for every
    /// start, except where the file lies in the program's Lisps, Pythons (directly in it) or Plugins folder, which always load, and its box is ticked and
    /// disabled.
    /// </summary>
    public sealed class AppLoadRow : ObservableObject
    {
        private readonly AppLoadViewModel _owner;

        internal AppLoadRow(
            AppLoadViewModel owner,
            string kind,
            string name,
            string commands,
            string file,
            bool isOn,
            bool atStart,
            bool isAtStartFixed,
            string pluginName
        )
        {
            _owner = owner;
            Kind = kind;
            Name = name;
            Commands = commands;
            File = file;
            _isOn = isOn;
            _atStart = atStart;
            IsAtStartFixed = isAtStartFixed;
            PluginName = pluginName;
        }

        /// <summary>Gets the kind, <c>Lisp</c> (shown green), <c>Python</c> (shown amber), <c>Plugin</c> (shown blue) or <c>C# source</c> (shown light purple).</summary>
        public string Kind { get; }

        /// <summary>Gets whether it is a plugin.</summary>
        public bool IsPlugin => PluginName != null;

        /// <summary>Gets whether it is a Python file.</summary>
        public bool IsPython => Kind == "Python";

        /// <summary>Gets the file's name for an AutoLISP file, the plugin's name for a plugin.</summary>
        public string Name { get; }

        /// <summary>
        /// Gets the commands it defines, separated by commas, in the order they were defined: the <c>c:</c> functions the file
        /// defined while it loaded, or the commands the plugin registers; empty where there are none.
        /// </summary>
        public string Commands { get; }

        /// <summary>Gets the full path of the file.</summary>
        public string File { get; }

        /// <summary>Gets the plugin's name; null for an AutoLISP file.</summary>
        internal string PluginName { get; }

        /// <summary>Gets or sets whether it is loaded: unticking is Unload, ticking loads the file again.</summary>
        public bool IsOn
        {
            get => _isOn;
            set => _owner.SetOn(this, value);
        }

        private bool _isOn;

        /// <summary>Gets or sets whether it loads at every start: the Startup Suite for an AutoLISP file, the startup list for a plugin.</summary>
        public bool AtStart
        {
            get => _atStart;
            set => _owner.SetAtStart(this, value);
        }

        private bool _atStart;

        /// <summary>Gets whether the file always loads, and At start is ticked and cannot be changed.</summary>
        public bool IsAtStartFixed { get; }

        /// <summary>Gets whether the box of At start can be changed.</summary>
        public bool CanChangeAtStart => !IsAtStartFixed;

        /// <summary>
        /// Gets the badge shown after the name: <see cref="AppLoadBadge.NeedsPackages"/> for a plugin whose packages were not
        /// downloaded because the question was answered No (ticking the row asks again), <see cref="AppLoadBadge.InstallFailed"/>
        /// where they could not be restored; for a Python tool the same two, with <see cref="AppLoadBadge.Installing"/> while pip
        /// installs its packages; <see cref="AppLoadBadge.None"/> otherwise and for a plugin loaded.
        /// </summary>
        public AppLoadBadge Badge { get; internal set; }

        /// <summary>Gets whether a badge is shown after the name.</summary>
        public bool HasBadge => Badge != AppLoadBadge.None;

        /// <summary>Gets the badge's words: <c>needs packages</c>, <c>installing…</c> or <c>install failed</c>; empty with none.</summary>
        public string BadgeText =>
            Badge == AppLoadBadge.NeedsPackages ? "needs packages"
            : Badge == AppLoadBadge.Installing ? "installing…"
            : Badge == AppLoadBadge.InstallFailed ? "install failed"
            : string.Empty;

        /// <summary>
        /// Gets the badge's tooltip, which says what to do about it; empty with none. A Python tool is loaded already, so its
        /// <c>needs packages</c> says that its next command asks again.
        /// </summary>
        public string BadgeToolTip =>
            Badge == AppLoadBadge.NeedsPackages
                ? (
                    IsPython
                        ? "Its packages are not installed yet: its next command asks again"
                        : "Its packages are not downloaded yet: tick it to be asked"
                )
            : Badge == AppLoadBadge.Installing ? "pip is installing its packages; the window stays free"
            : Badge == AppLoadBadge.InstallFailed ? "The install failed; the line below says why"
            : string.Empty;

        /// <summary>Gets the tooltip of the On box, which says what a click does to this row: unload it where it is on, load it where it is off.</summary>
        public string OnToolTip => _isOn ? "Loaded: untick to unload it" : "Not loaded: tick to load it again";

        internal void ShowAtStart(bool value)
        {
            _atStart = value;
            Raise(nameof(AtStart));
        }

        /// <inheritdoc/>
        public override string ToString() => Name;
    }

    /// <summary>
    /// <para>
    /// Load/Unload Applications, shown by APPLOAD and by the Plugin manager button: the AutoLISP files loaded into the
    /// document shown, those of the Startup Suite, and the plugins known in this session, in one table (On, Name, Kind,
    /// Commands, File, At start) sorted by name and then by file, with chips that filter it by kind. Load takes .lsp, .py, .dll and
    /// .csproj files (an AutoLISP file as <see cref="CadCommandSession.LoadLisp"/> does, a .dll or a plugin's .csproj through the
    /// plugin manager as NETLOAD does) and Load folder takes a plugin's folder: a folder with a .csproj is a C# plugin, whose NuGet
    /// packages the app asks to download before they are (a plugin answered No stays in the table, unticked, with the badge
    /// <c>needs packages</c>, and ticking it asks again; one whose restore failed has <c>install failed</c> and a red line).
    /// Reload loads the rows chosen again and Unload unloads them: a plugin as the plugin manager does, an AutoLISP file
    /// by losing the <c>c:</c> commands it defined, which become unknown commands, its row staying in the table unticked;
    /// AutoLISP can unload no more than that. A Python tool (a row of Kind <c>Python</c>; its file is its folder) is loaded as PYLOAD does and belongs to the process, not to
    /// the drawing shown; unloading it takes the commands it declared out of every drawing, its Startup Suite is <see cref="GlobalSettings.PythonStartupSuite"/>.
    /// What each action did is a line of the command history. The folder of the last Load is kept in Global
    /// settings (<see cref="GlobalSettings.AppLoadFolder"/>), the Startup Suite in <see cref="GlobalSettings.LispStartupSuite"/>.
    /// </para>
    /// <para>
    /// Every file loaded is also kept in <see cref="GlobalSettings.KnownApplications"/>, with the commands it last defined, so
    /// that an unloaded file stays in the table, unticked, in a new window and after a restart of the app, as AutoCAD keeps its
    /// History list; ticking it loads it. Remove unloads the rows chosen and takes them out of the table, the known list and the
    /// Startup Suite (a plugin is forgotten by the plugin manager, its At start entry too); a file of the Lisps or Plugins
    /// folder cannot be removed, since it loads at every start.
    /// </para>
    /// <para>
    /// A load that fails (a .lsp or a .dll, from Load…, a drop, Reload or the On box), a file type refused, or Load… with no
    /// drawing open is also told by a red line above the hint, until the next load that has no failure
    /// (<see cref="HasLoadError"/>). It follows the plugin manager while open and lets go of it when disposed.
    /// </para>
    /// </summary>
    public sealed class AppLoadViewModel : ObservableObject, IDisposable
    {
        /// <summary>The filter of the file dialog Load… shows: applications (.lsp, the requirements.txt of a Python tool, .dll and .csproj), AutoLISP, Python tools, plugins, C# plugins (the .csproj of a plugin's folder), and all files.</summary>
        public const string OpenFilter =
            "Applications (*.lsp;requirements.txt;*.dll;*.csproj)|*.lsp;requirements.txt;*.dll;*.csproj|Lisp files (*.lsp)|*.lsp|Python tools (requirements.txt)|requirements.txt|Plugins (*.dll)|*.dll|C# plugin (*.csproj)|*.csproj|All files (*.*)|*.*";

        /// <summary>The filter of the file dialog LISPLOAD shows: AutoLISP files.</summary>
        public const string LispFilter = "Lisp files (*.lsp)|*.lsp";

        /// <summary>The filter of the file dialog PYLOAD shows: the <c>requirements.txt</c> of a Python tool.</summary>
        public const string PythonFilter = "Python tools (requirements.txt)|requirements.txt";

        private const string LispKind = "Lisp";
        private const string PythonKind = "Python";
        private const string PluginKind = "Plugin";
        private const string SourceKind = "C# source";
        private const string DidNotCompile = " did not compile: ";
        private const string NotInstalled = " was not installed: ";

        private readonly MainViewModel _model;
        private IReadOnlyList<AppLoadRow> _all = Array.Empty<AppLoadRow>();
        private IReadOnlyList<AppLoadRow> _rows = Array.Empty<AppLoadRow>();
        private IReadOnlyList<AppLoadRow> _chosen = Array.Empty<AppLoadRow>();
        private string _filter = "All";
        private string _loadErrorName;
        private string _loadErrorText;

        // The failures of the load going on, in order: the file's name and what to say after it. The line is made from them when it ends.
        private List<KeyValuePair<string, string>> _failures;

        // The whole message of each source plugin of the load going on that did not compile, by the file's name: the line's tooltip.
        private Dictionary<string, string> _compileErrors;
        private string _loadErrorTip;

        // The AutoLISP files unticked in this window, by full path: they stay in the table, off, until it closes; Unload keeps them too.
        private readonly HashSet<string> _off = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        internal AppLoadViewModel(MainViewModel model)
        {
            _model = model;
            LoadCommand = new DelegateCommand(_ => Load());
            LoadFolderCommand = new DelegateCommand(_ => LoadFolder());
            ReloadCommand = new DelegateCommand(_ => Reload(), _ => _chosen.Count > 0);
            UnloadCommand = new DelegateCommand(_ => Unload(), _ => _chosen.Count > 0);
            RemoveCommand = new DelegateCommand(_ => Remove(), _ => _chosen.Any(r => !r.IsAtStartFixed));
            FilterCommand = new DelegateCommand(p => Filter = p as string ?? "All");

            if (model.Plugins != null)
            {
                model.Plugins.PluginsChanged += OnPluginsChanged;
            }

            model.PythonToolStateChanged += OnPythonToolStateChanged;
            Refresh();
        }

        /// <summary>
        /// Gets or sets how Load… asks for files: it shows an Open dialog with <see cref="OpenFilter"/>, several files allowed,
        /// opened on the folder of the last Load, and gives the paths chosen; null where none was. Not set, Load… does nothing.
        /// </summary>
        public Func<string, IReadOnlyList<string>> AskFiles { get; set; }

        /// <summary>
        /// Gets or sets how Load folder… asks for a folder: it shows a folder dialog opened on the folder of the last Load, and
        /// gives the folder chosen; null where none was. Not set, Load folder… does nothing.
        /// </summary>
        public Func<string, string> AskFolder { get; set; }

        /// <summary>Gets the rows shown, as the chip chosen filters them, sorted by name and then by file.</summary>
        public IReadOnlyList<AppLoadRow> Rows => _rows;

        /// <summary>Gets or sets the chip chosen: <c>All</c>, <c>Lisp</c>, <c>Python</c>, <c>Plugin</c> or <c>C# source</c>.</summary>
        public string Filter
        {
            get => _filter;
            set
            {
                if (Set(ref _filter, value ?? "All"))
                {
                    ApplyFilter();
                }
            }
        }

        /// <summary>Gets the chip of every row, with their number: <c>All 4</c>.</summary>
        public string AllChipText => "All " + _all.Count.ToString(CultureInfo.InvariantCulture);

        /// <summary>Gets the chip of the AutoLISP files, with their number: <c>Lisp 3</c>.</summary>
        public string LispChipText =>
            "Lisp " + _all.Count(r => !r.IsPlugin && !r.IsPython).ToString(CultureInfo.InvariantCulture);

        /// <summary>Gets the chip of the Python files, with their number: <c>Python 2</c>.</summary>
        public string PythonChipText => "Python " + _all.Count(r => r.IsPython).ToString(CultureInfo.InvariantCulture);

        /// <summary>Gets the chip of the plugins built as a .dll, with their number: <c>Plugins 1</c>.</summary>
        public string PluginChipText =>
            "Plugins " + _all.Count(r => r.Kind == PluginKind).ToString(CultureInfo.InvariantCulture);

        /// <summary>Gets the chip of the plugins given as C# source, with their number: <c>C# source 1</c>.</summary>
        public string SourceChipText =>
            "C# source " + _all.Count(r => r.Kind == SourceKind).ToString(CultureInfo.InvariantCulture);

        /// <summary>Gets the rows chosen, in the order of the table.</summary>
        public IReadOnlyList<AppLoadRow> Chosen => _chosen;

        /// <summary>
        /// Gets whether the load-result line is shown: the last load, a batch of files or one, had a failure. It starts hidden,
        /// is not shown for a load that succeeded, and goes when the next load has no failure.
        /// </summary>
        public bool HasLoadError => _loadErrorName != null;

        /// <summary>Gets the file the load-result line names, shown in bold: the last file that failed; empty where the line has no file to name.</summary>
        public string LoadErrorName => _loadErrorName ?? string.Empty;

        /// <summary>
        /// Gets what the line says after the name: <c> failed to load: ; error: bad argument type: consp 5</c>, the error text
        /// as the command history wrote it, then for an AutoLISP file that defined commands first <c> (ERRONE was defined
        /// before the error)</c>; the sentence of a refusal, or <c>Open a drawing to load NAME.</c>; and <c> (and 2 more)</c>
        /// where three files failed in the load. Empty while the line is hidden.
        /// </summary>
        public string LoadErrorText => _loadErrorText ?? string.Empty;

        /// <summary>
        /// Gets the line's tooltip: the whole line, the name and then its text; for a C# source that did not compile, every
        /// error, as the command line writes them (<c>Plugin Stairs was not loaded: 1 error</c>, then
        /// <c>Stairs.cs(12,9): error CS0103: ...</c>). Empty while the line is hidden.
        /// </summary>
        public string LoadError => _loadErrorTip ?? LoadErrorName + LoadErrorText;

        /// <summary>
        /// Gets what the line says at its right: <c>Every error is on the command line</c> for a C# source that did not compile,
        /// <c>Every line is on the command line</c> for a plugin whose packages could not be installed, else
        /// <c>Shown until the next load</c>.
        /// </summary>
        public string LoadErrorHint =>
            _loadErrorTip == null ? "Shown until the next load"
            : (_loadErrorText ?? string.Empty).StartsWith(NotInstalled, StringComparison.Ordinal)
                ? "Every line is on the command line"
            : "Every error is on the command line";

        /// <summary>Gets the command of Load….</summary>
        public ICommand LoadCommand { get; }

        /// <summary>Gets the command of Load folder….</summary>
        public ICommand LoadFolderCommand { get; }

        /// <summary>Gets the command of Reload, which needs a row chosen.</summary>
        public ICommand ReloadCommand { get; }

        /// <summary>Gets the command of Unload, which needs a row chosen.</summary>
        public ICommand UnloadCommand { get; }

        /// <summary>Gets the command of Remove, which needs a row chosen that is not of the Lisps or Plugins folder.</summary>
        public ICommand RemoveCommand { get; }

        /// <summary>
        /// Gets the tooltip of Remove: what it does, or, where every row chosen lies in the Lisps or Plugins folder, why it
        /// cannot be pressed.
        /// </summary>
        public string RemoveToolTip =>
            _chosen.Count > 0 && _chosen.All(r => r.IsAtStartFixed)
                ? "Files in the Lisps and Plugins folders load at every start, so they cannot be taken out of the list."
                : "Unload the chosen files and take them out of the list";

        /// <summary>Gets the command of a filter chip, given All, Lisp, Python or Plugin.</summary>
        public ICommand FilterCommand { get; }

        /// <summary>Takes the rows chosen in the table, which Reload and Unload act on.</summary>
        /// <param name="chosen">The rows chosen.</param>
        public void ChooseRows(IEnumerable<AppLoadRow> chosen)
        {
            var wanted = new HashSet<AppLoadRow>(chosen ?? Enumerable.Empty<AppLoadRow>());
            _chosen = _rows.Where(wanted.Contains).ToList();
            Raise(nameof(Chosen));
            ((DelegateCommand)ReloadCommand).RaiseCanExecuteChanged();
            ((DelegateCommand)UnloadCommand).RaiseCanExecuteChanged();
            ((DelegateCommand)RemoveCommand).RaiseCanExecuteChanged();
            Raise(nameof(RemoveToolTip));
        }

        /// <summary>Load…: asks for files with <see cref="AskFiles"/> and loads them as <see cref="Load(IEnumerable{string})"/> does.</summary>
        public void Load()
        {
            IReadOnlyList<string> paths = AskFiles?.Invoke(_model.Settings.AppLoadFolder);

            if (paths != null && paths.Count > 0)
            {
                Load(paths);
            }
        }

        /// <summary>Load folder…: asks for a folder with <see cref="AskFolder"/> and loads it as <see cref="LoadFolder(string)"/> does.</summary>
        public void LoadFolder()
        {
            string folder = AskFolder?.Invoke(_model.Settings.AppLoadFolder);

            if (!string.IsNullOrEmpty(folder))
            {
                LoadFolder(folder);
            }
        }

        /// <summary>
        /// Loads a plugin's folder as PLUGINLOAD does: a folder with a .csproj is a C# plugin, compiled and, where it names NuGet
        /// packages that are not on the computer, loaded after the question whether to download them; a folder of .dll files is a
        /// plugin as it is. A folder with a requirements.txt is a Python tool, loaded as PYLOAD loads it (its commands
        /// from its plugin.json; no Python starts). The folder's parent is kept for the next Load.
        /// </summary>
        /// <param name="folder">The folder.</param>
        public void LoadFolder(string folder) => Load(new[] { folder });

        /// <summary>
        /// Loads files in the order given, each as the command line would: an AutoLISP file into the document shown (what it
        /// wrote, then <c>name.lsp successfully loaded.</c>, or the <c>; error:</c> line) and a .dll through the plugin manager,
        /// which asks about a file outside its trusted folders; <c>.arx</c>, <c>.dbx</c>, <c>.dvb</c>, <c>.fas</c> and <c>.vlx</c>
        /// files are refused and any other type is said to be neither. The folder of the last file is kept for the next Load.
        /// </summary>
        /// <param name="paths">The files.</param>
        public void Load(IEnumerable<string> paths)
        {
            string last = LoadMany(paths ?? Enumerable.Empty<string>());

            if (last != null)
            {
                string folder = Path.GetDirectoryName(last);
                _model.ChangeSettings(s => s.AppLoadFolder = folder, "APPLOAD loaded files");
            }
        }

        /// <summary>
        /// Loads files dropped on the table, from Explorer, in the order of the drop as <see cref="Load(IEnumerable{string})"/> does,
        /// though the folder kept is not changed. A tool's folder loads as <see cref="LoadFolder(string)"/> loads it (a folder with a
        /// .csproj is a C# plugin, a folder with a requirements.txt a Python tool); any other folder is passed over.
        /// </summary>
        /// <param name="paths">The full paths dropped.</param>
        public void LoadDropped(IEnumerable<string> paths) =>
            LoadMany(
                (paths ?? Enumerable.Empty<string>()).Where(p =>
                    !string.IsNullOrWhiteSpace(p) && (File.Exists(p) || IsToolFolder(p))
                )
            );

        /// <summary>Whether a path is the folder of a tool: a folder with a .csproj directly in it, or with a requirements.txt.</summary>
        private static bool IsToolFolder(string path) =>
            Directory.Exists(path) && (IsSource(path) || PythonToolFolder.Resolve(path) != null);

        /// <summary>Reload: loads the rows chosen again, an AutoLISP file as Load does and a plugin as PLUGINRELOAD does.</summary>
        public void Reload()
        {
            BeginLoad();

            foreach (AppLoadRow row in _chosen)
            {
                if (row.IsPlugin)
                {
                    // A plugin the manager does not know yet (known from an earlier run) is loaded from its file.
                    CadLoadedPlugin managed = ManagedPlugin(row);
                    NotePlugin(
                        row.File,
                        managed != null ? _model.ReloadPlugin(managed.Name) : _model.LoadPlugin(row.File)
                    );
                }
                else
                {
                    LoadScript(row.File);
                }
            }

            EndLoad();
            Refresh();
        }

        /// <summary>
        /// Unload: unloads the rows chosen that are loaded. A plugin is unloaded as PLUGINUNLOAD does. An AutoLISP file stays in
        /// the table, unticked, as a plugin does, so that ticking it loads it again; the <c>c:</c> commands it defined are
        /// removed from the interpreter, and the other functions it defined stay. The row stays in the table, in a new window and
        /// after a restart of the app too, until Remove takes it out.
        /// </summary>
        public void Unload()
        {
            foreach (AppLoadRow row in _chosen)
            {
                if (row.IsOn)
                {
                    UnloadRow(row);
                }

                if (!row.IsPlugin)
                {
                    _off.Add(Normal(row.File));
                }
            }

            Refresh();
        }

        /// <summary>
        /// Remove: unloads the rows chosen that are loaded, as Unload does, and takes them out of the table, out of
        /// <see cref="GlobalSettings.KnownApplications"/> and, for an AutoLISP file, out of the Startup Suite; a plugin is
        /// forgotten by the plugin manager, which takes it off its startup list too. A row of the Lisps or Plugins folder is
        /// left as it is, since it loads at every start.
        /// </summary>
        public void Remove()
        {
            List<AppLoadRow> gone = _chosen.Where(r => !r.IsAtStartFixed).ToList();

            foreach (AppLoadRow row in gone)
            {
                if (row.IsOn)
                {
                    UnloadRow(row);
                }

                if (!row.IsPlugin)
                {
                    _off.Remove(Normal(row.File));
                }
            }

            var files = new HashSet<string>(gone.Select(r => Normal(r.File)), StringComparer.OrdinalIgnoreCase);
            var lisps = new HashSet<string>(
                gone.Where(r => !r.IsPlugin && !r.IsPython).Select(r => Normal(r.File)),
                StringComparer.OrdinalIgnoreCase
            );
            var pythons = new HashSet<string>(
                gone.Where(r => r.IsPython).Select(r => Normal(r.File)),
                StringComparer.OrdinalIgnoreCase
            );
            _model.ChangeSettings(
                s =>
                {
                    s.SetKnownApplications(s.KnownApplications.Where(p => !files.Contains(Normal(p.Key))).ToList());
                    s.LispStartupSuite = s.LispStartupSuite.Where(f => !lisps.Contains(Normal(f))).ToList();
                    s.PythonStartupSuite = s.PythonStartupSuite.Where(f => !pythons.Contains(Normal(f))).ToList();
                },
                "applications were removed from the list"
            );

            foreach (AppLoadRow row in gone.Where(r => r.IsPlugin))
            {
                _model.ForgetPackageState(row.File);
                CadLoadedPlugin managed = ManagedPlugin(row);

                if (managed != null)
                {
                    _model.Plugins.Remove(managed.Name);
                }
            }

            Refresh();
        }

        /// <summary>Lets go of the plugin manager.</summary>
        public void Dispose()
        {
            _model.PythonToolStateChanged -= OnPythonToolStateChanged;

            if (_model.Plugins != null)
            {
                _model.Plugins.PluginsChanged -= OnPluginsChanged;
            }
        }

        /// <summary>Unticking On is Unload of the row, ticking it loads the file again.</summary>
        internal void SetOn(AppLoadRow row, bool on)
        {
            if (row.IsOn == on)
            {
                return;
            }

            if (on)
            {
                BeginLoad();

                if (row.IsPlugin)
                {
                    NotePlugin(row.File, _model.LoadPlugin(row.File));
                }
                else
                {
                    LoadScript(row.File);
                }

                EndLoad();
            }
            else
            {
                UnloadRow(row);

                // The row stays, off, so that a second click on the same box ticks it again and no other row moves.
                if (!row.IsPlugin)
                {
                    _off.Add(Normal(row.File));
                }
            }

            Refresh();
        }

        /// <summary>Ticking At start keeps the file for every start: the Startup Suite for AutoLISP, the startup list for a plugin.</summary>
        internal void SetAtStart(AppLoadRow row, bool value)
        {
            if (row.IsAtStartFixed || row.AtStart == value)
            {
                return;
            }

            if (row.IsPlugin)
            {
                // A plugin the manager does not know yet (known from an earlier run) has no startup entry to change until it
                // is loaded: the box is put back as it was.
                CadLoadedPlugin managed = ManagedPlugin(row);

                if (managed == null)
                {
                    row.ShowAtStart(row.AtStart);
                    return;
                }

                _model.Plugins.SetLoadAtStartup(managed.Name, value);
            }
            else if (row.IsPython)
            {
                string file = row.File;
                _model.ChangeSettings(
                    s =>
                        s.PythonStartupSuite = value
                            ? s.PythonStartupSuite.Concat(new[] { file }).ToList()
                            : s.PythonStartupSuite.Where(f => !SameFile(f, file)).ToList(),
                    "the Python Startup Suite was changed"
                );
            }
            else
            {
                string file = row.File;
                _model.ChangeSettings(
                    s =>
                        s.LispStartupSuite = value
                            ? s.LispStartupSuite.Concat(new[] { file }).ToList()
                            : s.LispStartupSuite.Where(f => !SameFile(f, file)).ToList(),
                    "the Startup Suite was changed"
                );
            }

            row.ShowAtStart(value);
        }

        private string LoadMany(IEnumerable<string> paths)
        {
            string last = null;
            BeginLoad();

            foreach (string path in paths)
            {
                string full;

                try
                {
                    full = Path.GetFullPath(path ?? string.Empty);
                }
                catch (Exception ex) when (ex is ArgumentException || ex is NotSupportedException || ex is IOException)
                {
                    _model.Say(path + " is not a valid file name.");
                    continue;
                }

                // A Python tool is its folder, however it was named: the folder or its requirements.txt.
                full = PythonToolFolder.Resolve(full) ?? full;
                last = full;

                if (IsLisp(full) || IsPython(full))
                {
                    LoadScript(full);
                }
                else
                {
                    Note(full, _model.LoadApplication(full));
                }
            }

            EndLoad();
            Refresh();
            return last;
        }

        private void LoadScript(string path)
        {
            if (_model.HasDocument)
            {
                Note(path, _model.LoadApplication(path));
            }
            else
            {
                string text = "Open a drawing to load " + Path.GetFileName(path) + ".";
                _model.Say(text);
                _failures.Add(new KeyValuePair<string, string>(string.Empty, text));
            }
        }

        private static bool IsLisp(string path) =>
            string.Equals(Path.GetExtension(path), ".lsp", StringComparison.OrdinalIgnoreCase);

        private static bool IsPython(string path) =>
            string.Equals(Path.GetExtension(path), ".py", StringComparison.OrdinalIgnoreCase)
            || PythonToolFolder.Resolve(path) != null;

        /// <summary>Whether a plugin known from an earlier run is given as C# source: a .csproj, or a folder with a .csproj directly in it.</summary>
        private static bool IsSource(string path)
        {
            if (string.Equals(Path.GetExtension(path), ".csproj", StringComparison.OrdinalIgnoreCase))
            {
                return true;
            }

            try
            {
                return Directory.Exists(path)
                    && Directory
                        .GetFiles(path)
                        .Any(f => string.Equals(Path.GetExtension(f), ".csproj", StringComparison.OrdinalIgnoreCase));
            }
            catch (Exception ex) when (ex is IOException || ex is UnauthorizedAccessException)
            {
                return false;
            }
        }

        /// <summary>
        /// What the red line says after the file's name for a plugin that failed: for a source that did not compile, the count and
        /// the first error (<c> did not compile: 1 error. Stairs.cs(12,9): error CS0103: ...</c>), every error being on the command
        /// line; for any other, <c> failed to load: </c> and what the plugin manager said.
        /// </summary>
        private static string PluginFailure(string message)
        {
            string[] lines = message.Split('\n');
            const string NotLoaded = " was not loaded: ";
            int at = lines[0].IndexOf(NotLoaded, StringComparison.Ordinal);

            if (lines.Length > 1 && lines[0].StartsWith("Plugin ", StringComparison.Ordinal) && at > 0)
            {
                return DidNotCompile + lines[0].Substring(at + NotLoaded.Length) + ". " + lines[1];
            }

            return " failed to load: " + message;
        }

        /// <summary>A load begins: the failures of the one before are forgotten, and the line stays as it was until this one ends.</summary>
        private void BeginLoad()
        {
            _failures = new List<KeyValuePair<string, string>>();
            _compileErrors = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        }

        /// <summary>Keeps what a failed or refused load said, as the line will show it; a load that succeeded keeps nothing.</summary>
        private void Note(string path, AppLoadResult result)
        {
            if (result.Kind == AppLoadStatusKind.Refused)
            {
                _failures.Add(new KeyValuePair<string, string>(result.Name, result.Detail));
            }
            else if (result.Kind == AppLoadStatusKind.Failed)
            {
                ToolPackageState packages = IsLisp(path) || IsPython(path) ? null : _model.PackageStateOf(path);

                // The question answered No is the user's own word: the row says so with its badge, and no red line is shown.
                if (packages?.Kind == AppLoadBadge.NeedsPackages)
                {
                    return;
                }

                string text =
                    IsLisp(path) || IsPython(path) ? " failed to load: " + result.Detail
                    : packages != null ? NotInstalled + packages.Text
                    : PluginFailure(result.Detail);
                KeepCompileErrors(result.Name, text, result.Detail);

                if (IsLisp(path))
                {
                    IReadOnlyList<string> defined = _model.LispCommandsOf(path);

                    if (defined.Count > 0)
                    {
                        text +=
                            " ("
                            + string.Join(", ", defined)
                            + (defined.Count == 1 ? " was" : " were")
                            + " defined before the error)";
                    }
                }

                _failures.Add(new KeyValuePair<string, string>(result.Name, text));
            }
        }

        /// <summary>Keeps the failure of a plugin loaded or reloaded from a row; the result is null with no plugin manager, which the history says.</summary>
        private void NotePlugin(string path, CadPluginResult result)
        {
            if (result != null && !result.Succeeded)
            {
                ToolPackageState packages = _model.PackageStateOf(path);

                if (packages?.Kind == AppLoadBadge.NeedsPackages)
                {
                    return;
                }

                string text = packages != null ? NotInstalled + packages.Text : PluginFailure(result.ToString());
                KeepCompileErrors(Path.GetFileName(path), text, result.ToString());
                _failures.Add(new KeyValuePair<string, string>(Path.GetFileName(path), text));
            }
        }

        /// <summary>A load ends: the line shows the last failure of it, with the number of the others, or hides where there was none.</summary>
        private void EndLoad()
        {
            List<KeyValuePair<string, string>> failures = _failures;
            _failures = null;

            Dictionary<string, string> compiled = _compileErrors;
            _compileErrors = null;
            _loadErrorTip = null;

            if (failures.Count == 0)
            {
                _loadErrorName = null;
                _loadErrorText = null;
            }
            else
            {
                KeyValuePair<string, string> last = failures[failures.Count - 1];
                _loadErrorName = last.Key;
                compiled.TryGetValue(last.Key, out _loadErrorTip);
                _loadErrorText =
                    last.Value
                    + (
                        failures.Count > 1
                            ? " (and " + (failures.Count - 1).ToString(CultureInfo.InvariantCulture) + " more)"
                            : string.Empty
                    );
            }

            Raise(nameof(HasLoadError));
            Raise(nameof(LoadErrorName));
            Raise(nameof(LoadErrorText));
            Raise(nameof(LoadError));
            Raise(nameof(LoadErrorHint));
        }

        /// <summary>Keeps the whole message of a source plugin that did not compile, for the line's tooltip.</summary>
        private void KeepCompileErrors(string name, string text, string message)
        {
            if (
                text.StartsWith(DidNotCompile, StringComparison.Ordinal)
                || text.StartsWith(NotInstalled, StringComparison.Ordinal)
            )
            {
                _compileErrors[name] = message;
            }
        }

        private void UnloadRow(AppLoadRow row)
        {
            if (row.IsPlugin)
            {
                _model.UnloadPlugin(row.PluginName);
            }
            else if (row.IsPython ? _model.UnloadPythonFile(row.File) : _model.UnloadLispFile(row.File))
            {
                _model.Say(row.Name + " unloaded.");
            }
        }

        /// <summary>Lists the files and plugins again, as after one was loaded or unloaded.</summary>
        private void Refresh()
        {
            _model.RememberApplications();
            var rows = new List<AppLoadRow>();
            string[] suite = _model.Settings.LispStartupSuite.ToArray();
            IReadOnlyDictionary<string, string> known = _model.Settings.KnownApplications;
            IReadOnlyList<string> loaded = _model.LoadedLispFiles;
            IReadOnlyList<string> lisps = _model.Session?.Document.SupportRoots.ExistingOf(CadSupportKind.Lisps)
                ?? Array.Empty<string>();

            // The files loaded, those that failed half way (their commands are bound), the files unticked here, those of the
            // Startup Suite and the AutoLISP files known from before, which stay to be ticked or unticked. A path is one file
            // however it was written.
            IReadOnlyList<string> failed = _model.FailedLispFiles;
            var on = new HashSet<string>(loaded.Concat(failed).Select(Normal), StringComparer.OrdinalIgnoreCase);
            var inSuite = new HashSet<string>(suite.Select(Normal), StringComparer.OrdinalIgnoreCase);
            var remembered = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);

            foreach (KeyValuePair<string, string> pair in known)
            {
                remembered[Normal(pair.Key)] = pair.Value;
            }

            foreach (
                string file in loaded
                    .Concat(failed)
                    .Concat(_off.Where(f => !IsPython(f)))
                    .Concat(suite)
                    .Concat(known.Keys.Where(IsLisp))
                    .Select(Normal)
                    .Distinct(StringComparer.OrdinalIgnoreCase)
            )
            {
                bool fixedOn = lisps.Any(folder => IsUnder(file, folder));

                // A file not loaded shows the commands it last defined.
                string commands =
                    on.Contains(file) ? string.Join(", ", _model.LispCommandsOf(file))
                    : remembered.TryGetValue(file, out string last) ? last
                    : string.Empty;
                rows.Add(
                    new AppLoadRow(
                        this,
                        LispKind,
                        Path.GetFileName(file),
                        commands,
                        file,
                        on.Contains(file),
                        fixedOn || inSuite.Contains(file),
                        fixedOn,
                        null
                    )
                );
            }

            // The Python files: those loaded in the process (not tied to the drawing shown), those unticked here, those of the
            // Python Startup Suite and those known from before. A file directly in a Pythons folder always loads at start.
            string[] pythonSuite = _model.Settings.PythonStartupSuite.ToArray();
            IReadOnlyList<string> pythonsFolders = _model.Session?.Document.SupportRoots.ExistingOf(
                CadSupportKind.Pythons
            ) ?? Array.Empty<string>();
            var pythonOn = new HashSet<string>(
                _model.LoadedPythonFiles.Select(Normal),
                StringComparer.OrdinalIgnoreCase
            );
            var inPythonSuite = new HashSet<string>(pythonSuite.Select(Normal), StringComparer.OrdinalIgnoreCase);

            foreach (
                string file in _model
                    .LoadedPythonFiles.Concat(_off.Where(IsPython))
                    .Concat(pythonSuite)
                    .Concat(known.Keys.Where(IsPython))
                    .Select(Normal)
                    .Distinct(StringComparer.OrdinalIgnoreCase)
            )
            {
                bool fixedOn = pythonsFolders.Any(folder => IsDirectlyIn(file, folder));
                string commands =
                    pythonOn.Contains(file) ? string.Join(", ", _model.PythonCommandsOf(file))
                    : remembered.TryGetValue(file, out string last) ? last
                    : string.Empty;
                rows.Add(
                    new AppLoadRow(
                        this,
                        PythonKind,
                        Path.GetFileName(file),
                        commands,
                        file,
                        pythonOn.Contains(file),
                        fixedOn || inPythonSuite.Contains(file),
                        fixedOn,
                        null
                    )
                    {
                        Badge = pythonOn.Contains(file) ? _model.PythonStateOf(file).Badge : AppLoadBadge.None,
                    }
                );
            }

            var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

            if (_model.Plugins != null)
            {
                foreach (CadLoadedPlugin plugin in _model.Plugins.Plugins)
                {
                    string names = string.Join(", ", CommandsOf(plugin));
                    seen.Add(Normal(plugin.Path));
                    rows.Add(
                        new AppLoadRow(
                            this,
                            plugin.IsSource ? SourceKind : PluginKind,
                            plugin.Name,
                            names.Length == 0 && remembered.TryGetValue(Normal(plugin.Path), out string last)
                                ? last
                                : names,
                            plugin.Path,
                            plugin.IsLoaded,
                            plugin.IsInstalled || plugin.LoadAtStartup,
                            plugin.IsInstalled,
                            plugin.Name
                        )
                        {
                            Badge = plugin.IsLoaded ? AppLoadBadge.None : BadgeOf(plugin.Path),
                        }
                    );
                }
            }

            // The plugins known from an earlier run, which the manager does not hold yet: off, to be ticked to load them.
            foreach (KeyValuePair<string, string> pair in known.Where(p => !IsLisp(p.Key) && !IsPython(p.Key)))
            {
                string name = Path.GetFileNameWithoutExtension(pair.Key.TrimEnd('\\', '/'));

                if (seen.Add(Normal(pair.Key)))
                {
                    string kind = IsSource(pair.Key) ? SourceKind : PluginKind;
                    rows.Add(
                        new AppLoadRow(this, kind, name, pair.Value, pair.Key, false, false, false, name)
                        {
                            Badge = BadgeOf(pair.Key),
                        }
                    );
                }
            }

            rows.Sort(Compare);
            _all = rows;
            Raise(nameof(AllChipText));
            Raise(nameof(LispChipText));
            Raise(nameof(PythonChipText));
            Raise(nameof(PluginChipText));
            Raise(nameof(SourceChipText));
            ApplyFilter();
        }

        /// <summary>The badge of a plugin that is not loaded: what its last load said of its packages, none where it said nothing.</summary>
        private AppLoadBadge BadgeOf(string path) => _model.PackageStateOf(path)?.Kind ?? AppLoadBadge.None;

        /// <summary>The plugin of the manager the row stands for, found by its file; null where the manager does not hold it, as for a plugin known only from an earlier run.</summary>
        private CadLoadedPlugin ManagedPlugin(AppLoadRow row) =>
            _model.Plugins?.Plugins.FirstOrDefault(p => SameFile(p.Path, row.File));

        /// <summary>The names of the commands a plugin registers, or its manifest lists while it is not loaded.</summary>
        internal static IEnumerable<string> CommandsOf(CadLoadedPlugin plugin) =>
            plugin.Plugin?.Commands.Select(c => c.Name) ?? plugin.Manifest?.Commands ?? Array.Empty<string>();

        /// <summary>The rows are made anew, so the rows chosen are found again by file or plugin name and stay chosen.</summary>
        private void ApplyFilter()
        {
            var keep = new HashSet<string>(_chosen.Select(KeyOf), StringComparer.OrdinalIgnoreCase);
            _rows = _all.Where(r => _filter == "All" || _filter == r.Kind).ToList();
            Raise(nameof(Rows));
            ChooseRows(_rows.Where(r => keep.Contains(KeyOf(r))));
        }

        private static string KeyOf(AppLoadRow row) => row.IsPlugin ? "P|" + row.PluginName : "L|" + Normal(row.File);

        private static int Compare(AppLoadRow a, AppLoadRow b)
        {
            int order = string.Compare(a.Name, b.Name, StringComparison.OrdinalIgnoreCase);

            if (order == 0)
            {
                order = string.Compare(a.File, b.File, StringComparison.OrdinalIgnoreCase);
            }

            if (order == 0)
            {
                order = string.CompareOrdinal(a.Name, b.Name);
            }

            if (order == 0)
            {
                order = string.CompareOrdinal(a.File, b.File);
            }

            return order != 0 ? order : string.CompareOrdinal(a.Kind, b.Kind);
        }

        private static bool SameFile(string a, string b) =>
            string.Equals(Normal(a), Normal(b), StringComparison.OrdinalIgnoreCase);

        /// <summary>The full path, so that <c>C:/a/b.lsp</c> and <c>C:\a\x\..\b.lsp</c> are one file; a name that is no path is kept as it is.</summary>
        internal static string Normal(string path)
        {
            try
            {
                return Path.GetFullPath(path);
            }
            catch (Exception ex) when (ex is ArgumentException || ex is NotSupportedException || ex is IOException)
            {
                return path;
            }
        }

        /// <summary>Whether the file lies in the folder or in any folder below it.</summary>
        internal static bool IsUnder(string file, string folder)
        {
            string root = Normal(folder)?.TrimEnd('\\', '/');
            return !string.IsNullOrEmpty(root)
                && Normal(file).StartsWith(root + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase);
        }

        /// <summary>Whether the file lies in the folder itself, not in a folder below it.</summary>
        internal static bool IsDirectlyIn(string file, string folder)
        {
            string root = Normal(folder)?.TrimEnd('\\', '/');
            string own = Path.GetDirectoryName(Normal(file))?.TrimEnd('\\', '/');
            return !string.IsNullOrEmpty(root) && string.Equals(own, root, StringComparison.OrdinalIgnoreCase);
        }

        private void OnPluginsChanged(object sender, EventArgs e) => Refresh();

        /// <summary>
        /// What a Python tool's environment is doing changed: the rows are made again, so that the badge appears and clears, and a
        /// failed install, or a change that failed and left the tool on its old environment, is told by the red line: the tool's
        /// name, <c> was not installed: </c> and pip's first line, every line in its tooltip.
        /// </summary>
        private void OnPythonToolStateChanged(object sender, PythonToolStateEventArgs e)
        {
            Refresh();

            if (e.State.Lines == null)
            {
                return;
            }

            string first = e.State.FirstLine;
            string own = e.Name + NotInstalled.TrimEnd();
            _loadErrorName = e.Name;
            _loadErrorText = first.StartsWith(own, StringComparison.Ordinal)
                ? first.Substring(e.Name.Length)
                : NotInstalled + first;
            _loadErrorTip = e.State.Lines.Replace("\r", string.Empty);
            Raise(nameof(HasLoadError));
            Raise(nameof(LoadErrorName));
            Raise(nameof(LoadErrorText));
            Raise(nameof(LoadError));
            Raise(nameof(LoadErrorHint));
        }
    }
}
