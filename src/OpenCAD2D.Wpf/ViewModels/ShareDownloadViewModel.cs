using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Input;
using OpenCAD2D.Share;

namespace OpenCAD2D.Wpf
{
    /// <summary>
    /// Download from Share, shown by SHAREDOWNLOAD: the user pastes the link of a tool from opencad2d.com/share and presses
    /// Find, which asks Share for the tool (one request, and its cover picture) and shows the picture, name, summary, type,
    /// live version and author, SHA-256, what is on this PC (the same file, an older or a newer one, found by SHA-256 from
    /// what the last install left in the tool's folder, and "Changed on this PC" where a file of it was edited since), the review, the commands it adds and the file. Download saves it in
    /// the folder its kind loads from (<c>Lisps</c>, <c>Pythons</c> or <c>Plugins</c> beside the program, under the tool's slug)
    /// where <see cref="LoadNow"/> is ticked, and under <c>Share</c>, which the app never loads from, where it is not, so that
    /// nothing runs unless the user chose it (the … button picks another folder); checks every file's SHA-256 against the one
    /// Share gave; keeps a copy of any file the user changed that it replaces; and loads it as Load/Unload Applications does
    /// when <see cref="LoadNow"/> is ticked. The app asks Share only when Find or Download is pressed: nothing here runs by
    /// itself, and <see cref="Cancel"/> stops what is going on.
    /// </summary>
    public sealed class ShareDownloadViewModel : ObservableObject
    {
        private readonly MainViewModel _model;
        private string _link = string.Empty;
        private bool _isBusy;
        private ShareTool _tool;
        private byte[] _picture;
        private string _saveTo = string.Empty;
        private bool _loadNow = true;
        private ShareCopy _copy;
        private string _result = string.Empty;
        private bool _resultIsError;
        private bool _isDone;
        private string _customParent;
        private readonly CancellationTokenSource _cancel = new CancellationTokenSource();

        internal ShareDownloadViewModel(MainViewModel model)
        {
            _model = model;
            FindCommand = new DelegateCommand(_ => _ = FindAsync(), _ => !_isBusy);
            DownloadCommand = new DelegateCommand(_ => Download(), _ => _tool != null && !_isBusy);
            ChooseFolderCommand = new DelegateCommand(_ => ChooseFolder(), _ => _tool != null && !_isBusy);
            BrowseCommand = new DelegateCommand(_ => Browse());
        }

        /// <summary>Gets the address of Share's page of tools, which Browse Share in the browser opens.</summary>
        public static Uri ShareAddress { get; } = new Uri(ShareLink.Site, "share");

        /// <summary>Gets or sets how Browse Share in the browser opens an address in the default browser. Not set, it does nothing.</summary>
        public Action<Uri> OpenBrowser { get; set; }

        /// <summary>
        /// Gets or sets how … asks for a folder: it shows a folder dialog opened on the folder given and gives the folder chosen;
        /// null where none was. Not set, … does nothing.
        /// </summary>
        public Func<string, string> AskFolder { get; set; }

        /// <summary>
        /// Gets or sets how the model asks a yes-or-no question (the window shows it as a message box): given the question, it gives
        /// true for Yes. Download asks before it replaces files the user changed; not set, it counts as No, so that nothing
        /// the user changed is ever replaced without being asked.
        /// </summary>
        public Func<string, bool> AskConfirm { get; set; }

        /// <summary>Raised when Done is pressed: the window closes.</summary>
        public event EventHandler CloseRequested;

        /// <summary>Gets the command of Find.</summary>
        public ICommand FindCommand { get; }

        /// <summary>Gets the command of the main button: Download, Update or Done, as <see cref="DownloadLabel"/> says.</summary>
        public ICommand DownloadCommand { get; }

        /// <summary>Gets the command of the … button, which picks the folder to save the tool in.</summary>
        public ICommand ChooseFolderCommand { get; }

        /// <summary>Gets the command of Browse Share in the browser.</summary>
        public ICommand BrowseCommand { get; }

        /// <summary>
        /// Fills the box with the text of the clipboard where the box is empty and that text is the link of a tool on Share,
        /// <c>https://opencad2d.com/share/stairs</c> with spaces and line breaks around it let go; any other text, or a clipboard
        /// that cannot be read, leaves the box as it is. The window calls it once, as it opens; it asks Share nothing, and the
        /// clipboard is read nowhere else.
        /// </summary>
        /// <returns>True where the box was filled.</returns>
        internal bool PrefillFromClipboard()
        {
            if (Link.Length != 0)
            {
                return false;
            }

            string text = _model.ClipboardText().Trim();

            if (!ShareLink.TryGetSlug(text, ShareLink.Site, out _))
            {
                return false;
            }

            Link = text;
            return true;
        }

        /// <summary>Gets or sets the pasted link, as <c>https://opencad2d.com/share/stairs</c>.</summary>
        public string Link
        {
            get => _link;
            set => Set(ref _link, value ?? string.Empty);
        }

        /// <summary>Gets whether a request to Share is going on; Find and Download wait for it.</summary>
        public bool IsBusy => _isBusy;

        /// <summary>Gets whether a tool was found and its details are shown.</summary>
        public bool Found => _tool != null && !_isDone;

        /// <summary>Gets whether the install is done and the details give way to the result line.</summary>
        public bool IsDone => _isDone;

        /// <summary>Gets the cover picture's bytes (PNG, JPEG or WebP) as Share served them and the SHA-256 checked; null where the tool has none.</summary>
        public byte[] Picture => _picture;

        /// <summary>Gets the tool's name; empty before one is found.</summary>
        public string Title => _tool?.Name ?? string.Empty;

        /// <summary>Gets the tool's summary; empty before one is found.</summary>
        public string Summary => _tool?.Summary ?? string.Empty;

        /// <summary>Gets what kind of tool it is, in words: <c>C# source, compiled when it loads</c>.</summary>
        public string TypeText =>
            _tool == null ? string.Empty
            : _tool.Kind == ShareKind.Lisp ? "AutoLISP, loaded into every drawing"
            : _tool.Kind == ShareKind.Python ? "Python tool, started when its first command is typed"
            : "C# source, compiled when it loads";

        /// <summary>Gets the live version and the author: <c>1.2, by archi_tools</c>.</summary>
        public string VersionText =>
            _tool == null ? string.Empty
            : _tool.Author.Length == 0 ? _tool.Version
            : _tool.Version + ", by " + _tool.Author;

        /// <summary>Gets the SHA-256 of the file, abbreviated: <c>9c4e1b7a03f2…d41a7b3e90</c>; with several files, the first one's and how many more.</summary>
        public string ShaText
        {
            get
            {
                if (_tool == null || _tool.Files.Count == 0)
                {
                    return string.Empty;
                }

                string first = _tool.Files[0].Sha256;
                string shown = first.Substring(0, 12) + "…" + first.Substring(first.Length - 10);
                return _tool.Files.Count == 1
                    ? shown
                    : shown
                        + " ("
                        + _tool.Files[0].Name
                        + ", and "
                        + (_tool.Files.Count - 1).ToString(CultureInfo.InvariantCulture)
                        + " more)";
            }
        }

        /// <summary>
        /// Gets what is on this PC: <c>Not on this PC</c>, or the version of the copy and the start of its SHA-256 with how the
        /// file on Share compares, as <c>1.1 (SHA-256 3f8a2c6e91d4…), so this is a newer file</c>.
        /// </summary>
        public string OnThisPcText
        {
            get
            {
                if (_copy == null || _copy.State == ShareCopyState.NotOnThisPc)
                {
                    return "Not on this PC";
                }

                string have = _copy.Version + " (SHA-256 " + _copy.Sha256.Substring(0, 12) + "…)";

                switch (_copy.State)
                {
                    case ShareCopyState.Changed:
                        return "Changed on this PC: "
                            + string.Join(", ", _copy.ChangedFiles)
                            + " (installed as "
                            + have
                            + ")";
                    case ShareCopyState.Same:
                        return have + ", the same file";
                    case ShareCopyState.Newer:
                        return have + ", so this is a newer file";
                    case ShareCopyState.Older:
                        return have + ", so this is an older file";
                    default:
                        return have + ", a different file of the same version";
                }
            }
        }

        /// <summary>Gets the review: <c>Approved by the Share reviewers, 2026-11-20</c>. Share shows only what its reviewers approved.</summary>
        public string ReviewText
        {
            get
            {
                if (_tool == null)
                {
                    return string.Empty;
                }

                string day = _tool.Approved.Length >= 10 ? _tool.Approved.Substring(0, 10) : string.Empty;
                bool isDay = DateTime.TryParseExact(
                    day,
                    "yyyy-MM-dd",
                    CultureInfo.InvariantCulture,
                    DateTimeStyles.None,
                    out _
                );
                return "Approved by the Share reviewers" + (isDay ? ", " + day : string.Empty);
            }
        }

        /// <summary>Gets the commands the tool adds, separated by commas, as <c>STAIRS</c>.</summary>
        public string CommandsText =>
            _tool == null ? string.Empty
            : _tool.Commands.Count == 0 ? "None read from the file"
            : string.Join(", ", _tool.Commands);

        /// <summary>Gets the file, its size and the licence: <c>Stairs.cs, 6 KB, MIT licence</c>; with several files, their number, names and total size.</summary>
        public string FileText
        {
            get
            {
                if (_tool == null)
                {
                    return string.Empty;
                }

                long size = _tool.Files.Sum(f => f.Size);
                string names =
                    _tool.Files.Count == 1
                        ? _tool.Files[0].Name
                        : _tool.Files.Count.ToString(CultureInfo.InvariantCulture)
                            + " files ("
                            + string.Join(", ", _tool.Files.Select(f => f.Name))
                            + ")";
                return names + ", " + SizeText(size) + ", MIT licence";
            }
        }

        /// <summary>
        /// Gets the folder the tool is saved in, as <c>C:\App\Plugins\stairs</c>. With <see cref="LoadNow"/> ticked it is the
        /// tool's slug under the folder the app loads its kind from (<c>Lisps</c>, <c>Pythons</c>, <c>Plugins</c>); unticked, under
        /// <c>Share</c> beside them, which the app never loads from, so that nothing runs until the user loads it. The … button
        /// puts the tool's folder under a folder of the user's choice instead, whatever the box says.
        /// </summary>
        public string SaveTo
        {
            get => _saveTo;
            private set
            {
                if (Set(ref _saveTo, value ?? string.Empty))
                {
                    CheckCopy();
                }
            }
        }

        /// <summary>
        /// Gets or sets whether the tool is saved where the app loads it from and loaded at once (and kept for every start, where
        /// the user picked a folder with … outside the program's own). Unticked, the tool is only saved, under <c>Share</c>, and
        /// nothing of it runs until it is loaded from the Applications window. Ticking or unticking it changes
        /// <see cref="SaveTo"/> unless … picked a folder.
        /// </summary>
        public bool LoadNow
        {
            get => _loadNow;
            set
            {
                if (Set(ref _loadNow, value))
                {
                    if (_tool != null && _customParent == null)
                    {
                        SaveTo = DefaultFolder(_tool, _loadNow);
                    }

                    Raise(nameof(DownloadLabel));
                }
            }
        }

        /// <summary>
        /// Gets the main button's words: <c>Download</c> for a tool not on this PC, <c>Update to 1.2</c> for a newer file,
        /// <c>Download again</c> for the same one and <c>Replace with 1.1</c> for an older one or one the user changed, each with
        /// <c> and load</c> where <see cref="LoadNow"/> is ticked; <c>Done</c> after the install.
        /// </summary>
        public string DownloadLabel
        {
            get
            {
                if (_isDone)
                {
                    return "Done";
                }

                string version = _tool?.Version ?? string.Empty;
                ShareCopyState state = _copy?.State ?? ShareCopyState.NotOnThisPc;
                string what =
                    _tool == null || state == ShareCopyState.NotOnThisPc ? "Download"
                    : state == ShareCopyState.Same ? "Download again"
                    : state == ShareCopyState.Older || state == ShareCopyState.Changed ? "Replace with " + version
                    : "Update to " + version;
                return _loadNow ? what + " and load" : what;
            }
        }

        /// <summary>Gets what came of the last Find or Download, in a sentence: the reason a link did not give a tool, what was saved and loaded, or what refused. Empty before the first.</summary>
        public string Result => _result;

        /// <summary>Gets whether <see cref="Result"/> says that something failed.</summary>
        public bool ResultIsError => _resultIsError;

        /// <summary>Gets whether <see cref="Result"/> has a sentence to show.</summary>
        public bool HasResult => _result.Length > 0;

        /// <summary>
        /// Cancels what the window is doing and everything it would do later: a request to Share is abandoned, an install that
        /// has not swapped its folder in stops with the old version as it was, and nothing is loaded after it. The window calls it
        /// when it closes; the model is not used again.
        /// </summary>
        public void Cancel() => _cancel.Cancel();

        /// <summary>
        /// Find: asks Share for the tool of <see cref="Link"/>, then for its cover picture, and shows what came back, or says why
        /// there is no tool. Nothing is asked of a link that is no tool's. Whatever goes wrong, a failure of Share, of the
        /// network or of anything else, ends as a sentence in <see cref="Result"/> with no tool shown; the tool is set only when
        /// everything came.
        /// </summary>
        /// <returns>A task that ends when the answer is shown; it never faults.</returns>
        public async Task FindAsync()
        {
            if (_isBusy || _cancel.IsCancellationRequested)
            {
                return;
            }

            SetBusy(true);
            ClearResult();
            _isDone = false;

            try
            {
                // What an install that was cut off left in the staging folder goes now, as the app next looks at Share.
                ShareInstaller.CleanLeftovers(ShareInstaller.StagingRoot(Roots));
                ShareTool tool = await _model.ShareClient.FindAsync(Link, _cancel.Token).ConfigureAwait(true);
                byte[] picture = await _model.ShareClient.GetPictureAsync(tool, _cancel.Token).ConfigureAwait(true);
                _customParent = null;
                _tool = tool;
                _picture = picture;
                _saveTo = DefaultFolder(tool, _loadNow);
                CheckCopy();
            }
            catch (Exception e)
            {
                _tool = null;
                _picture = null;
                _copy = null;
                _saveTo = string.Empty;

                if (!(e is OperationCanceledException))
                {
                    SetResult(
                        e is ShareException ? e.Message : "That tool could not be read from Share (" + e.Message + ").",
                        true
                    );
                }
            }
            finally
            {
                SetBusy(false);
                RaiseDetails();
            }
        }

        /// <summary>
        /// Download or Update: saves the tool in <see cref="SaveTo"/> with every file's SHA-256 checked against Share's, then
        /// loads it where <see cref="LoadNow"/> is ticked, and says what came of it. Where files the install wrote have been
        /// changed since (<see cref="ShareCopyState.Changed"/>), it first asks through <see cref="AskConfirm"/>, and with no
        /// answer or No it does nothing; going ahead keeps a copy of each changed file beside it as <c>name.local-yyyy-MM-dd</c>.
        /// A file that does not match leaves the folder as it was. After a good install the details give way to the result line
        /// and the main button reads Done. Nothing is loaded after <see cref="Cancel"/>.
        /// </summary>
        /// <returns>A task that ends when the result is shown; it never faults.</returns>
        public async Task DownloadAsync()
        {
            if (_tool == null || _isBusy || _isDone || _cancel.IsCancellationRequested)
            {
                return;
            }

            if (_saveTo.Length == 0)
            {
                SetResult("Choose the folder to save " + _tool.Name + " in.", true);
                return;
            }

            if (_copy != null && _copy.State == ShareCopyState.Changed)
            {
                string ask =
                    string.Join(", ", _copy.ChangedFiles)
                    + (_copy.ChangedFiles.Count == 1 ? " is" : " are")
                    + " not as Share's install left "
                    + (_copy.ChangedFiles.Count == 1 ? "it" : "them")
                    + " in "
                    + _saveTo
                    + ". Replace "
                    + (_copy.ChangedFiles.Count == 1 ? "it" : "them")
                    + " with "
                    + _tool.Name
                    + " "
                    + _tool.Version
                    + "? A copy of what you changed is kept beside it, as <name>.local-<date>.";

                if (AskConfirm == null || !AskConfirm(ask))
                {
                    SetResult("Nothing was changed: the files you changed in " + _saveTo + " are as they were.", false);
                    return;
                }
            }

            SetBusy(true);
            ClearResult();
            ShareTool tool = _tool;

            try
            {
                ShareInstalled installed = await ShareInstaller
                    .InstallAsync(_model.ShareClient, tool, _saveTo, ShareInstaller.StagingRoot(Roots), _cancel.Token)
                    .ConfigureAwait(true);

                // The window was closed while the files came: they are saved, and nothing runs.
                if (_cancel.IsCancellationRequested)
                {
                    return;
                }

                string failure = _loadNow ? LoadInstalled(tool, installed) : null;
                string said =
                    tool.Name
                    + " "
                    + tool.Version
                    + (
                        installed.PreviousVersion == null
                            ? " was saved to " + installed.Folder
                            : " replaced " + installed.PreviousVersion
                    );
                string kept =
                    installed.BackedUp.Count == 0
                        ? string.Empty
                        : " Your changes were kept as "
                            + string.Join(", ", installed.BackedUp.Select(Path.GetFileName))
                            + ".";

                if (failure != null)
                {
                    SetResult(said + ", but it did not load: " + failure + kept, true);
                }
                else
                {
                    string tail =
                        !_loadNow ? " Saved; not loaded. Load it from the Applications window."
                        : tool.Commands.Count > 0 ? " Type " + tool.Commands[0] + " to use it."
                        : string.Empty;
                    SetResult(
                        said
                            + (_loadNow ? " and is loaded" : string.Empty)
                            + "; its SHA-256 matches the one on Share."
                            + tail
                            + kept,
                        false
                    );
                    _isDone = true;
                }

                _model.Say(Result);
            }
            catch (Exception e)
            {
                if (!(e is OperationCanceledException))
                {
                    SetResult(
                        e is ShareException ? e.Message : "The tool could not be installed (" + e.Message + ").",
                        true
                    );
                }
            }
            finally
            {
                SetBusy(false);
                CheckCopy();
                RaiseDetails();
            }
        }

        // The main button: Download or Update runs the install; Done closes the window.
        private void Download()
        {
            if (_isDone)
            {
                CloseRequested?.Invoke(this, EventArgs.Empty);
            }
            else
            {
                _ = DownloadAsync();
            }
        }

        // …: the folder chosen is the one the tool's own folder goes in, so a folder of the Plugins kind need not be made by hand.
        private void ChooseFolder()
        {
            string start = Path.GetDirectoryName(_saveTo);
            string parent = AskFolder?.Invoke(start);

            if (!string.IsNullOrWhiteSpace(parent))
            {
                _customParent = parent;
                SaveTo = Path.Combine(parent, _tool.Slug);
                RaiseDetails();
            }
        }

        private void Browse() => OpenBrowser?.Invoke(ShareAddress);

        /// <summary>
        /// Loads what was saved, as Load/Unload Applications loads it: each <c>.lsp</c> in name order for AutoLISP, the folder for
        /// a Python tool and for C# source. Where the tool was saved outside the program's folders, which load at every start,
        /// it is also put on the startup list. Gives why it failed; null where it loaded.
        /// </summary>
        private string LoadInstalled(ShareTool tool, ShareInstalled installed)
        {
            using (var loading = new AppLoadViewModel(_model))
            {
                string[] paths =
                    tool.Kind == ShareKind.Lisp
                        ? installed
                            .Files.Where(f =>
                                string.Equals(Path.GetExtension(f), ".lsp", StringComparison.OrdinalIgnoreCase)
                            )
                            .OrderBy(f => f, StringComparer.Ordinal)
                            .ToArray()
                        : new[] { installed.Folder };

                if (paths.Length == 0)
                {
                    return tool.Name + " has no .lsp file to load.";
                }

                loading.Load(paths);

                if (loading.HasLoadError)
                {
                    return (loading.LoadErrorName + loading.LoadErrorText).Trim();
                }

                if (!IsInProgramFolder(tool, installed.Folder))
                {
                    foreach (string path in paths)
                    {
                        AppLoadRow row = loading.Rows.FirstOrDefault(r =>
                            string.Equals(
                                AppLoadViewModel.Normal(r.File),
                                AppLoadViewModel.Normal(path),
                                StringComparison.OrdinalIgnoreCase
                            )
                        );

                        if (row != null)
                        {
                            loading.SetAtStart(row, true);
                        }
                    }
                }

                return null;
            }
        }

        private CadSupportFolders Roots => _model.Session?.Document.SupportRoots ?? CadSupportFolders.Default;

        // The folder of a tool where the user has not picked one: under the folder its kind loads from, or under Share.
        private string DefaultFolder(ShareTool tool, bool autoLoad) =>
            ShareInstaller.ToolFolder(Roots, tool.Kind, tool.Slug, autoLoad) ?? string.Empty;

        // Whether a tool's folder lies directly in the program's folder of its kind, which the app loads from at every start.
        private bool IsInProgramFolder(ShareTool tool, string folder)
        {
            string own = DefaultFolder(tool, true);
            return own.Length > 0
                && string.Equals(
                    Path.GetFullPath(folder).TrimEnd(Path.DirectorySeparatorChar),
                    Path.GetFullPath(own).TrimEnd(Path.DirectorySeparatorChar),
                    StringComparison.OrdinalIgnoreCase
                );
        }

        private void CheckCopy()
        {
            _copy = _tool != null && _saveTo.Length > 0 ? ShareCopy.Check(_tool, _saveTo) : null;
            Raise(nameof(OnThisPcText));
            Raise(nameof(DownloadLabel));
        }

        private void SetBusy(bool busy)
        {
            _isBusy = busy;
            Raise(nameof(IsBusy));
            (FindCommand as DelegateCommand)?.RaiseCanExecuteChanged();
            (DownloadCommand as DelegateCommand)?.RaiseCanExecuteChanged();
            (ChooseFolderCommand as DelegateCommand)?.RaiseCanExecuteChanged();
        }

        private void SetResult(string text, bool isError)
        {
            _result = text;
            _resultIsError = isError;
            Raise(nameof(Result));
            Raise(nameof(ResultIsError));
            Raise(nameof(HasResult));
        }

        private void ClearResult() => SetResult(string.Empty, false);

        // A new tool, or a new state of it: every line of the details is read again.
        private void RaiseDetails()
        {
            foreach (
                string name in new[]
                {
                    nameof(Found),
                    nameof(IsDone),
                    nameof(Picture),
                    nameof(Title),
                    nameof(Summary),
                    nameof(TypeText),
                    nameof(VersionText),
                    nameof(ShaText),
                    nameof(OnThisPcText),
                    nameof(ReviewText),
                    nameof(CommandsText),
                    nameof(FileText),
                    nameof(SaveTo),
                    nameof(DownloadLabel),
                }
            )
            {
                Raise(name);
            }

            (DownloadCommand as DelegateCommand)?.RaiseCanExecuteChanged();
            (ChooseFolderCommand as DelegateCommand)?.RaiseCanExecuteChanged();
        }

        private static string SizeText(long bytes) =>
            bytes < 1024 ? bytes.ToString(CultureInfo.InvariantCulture) + " bytes"
            : bytes < 1024 * 1024 ? ((bytes + 1023) / 1024).ToString(CultureInfo.InvariantCulture) + " KB"
            : (bytes / (1024.0 * 1024.0)).ToString("0.#", CultureInfo.InvariantCulture) + " MB";
    }
}
