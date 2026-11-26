using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Windows.Media;
using GeometryHelper;
using OpenCAD2D.SheetSets;

namespace OpenCAD2D.Wpf
{
    /// <summary>
    /// AutoCAD 2021's Sheet Set Manager palette, which SHEETSET shows beside the window and SHEETSETHIDE closes: the sheet set
    /// control at the top (the sets open, Recent, New Sheet Set…, Open…), the tabs Sheet List, Sheet Views and Model Views, the
    /// tree of the current set (the set, its subsets and its sheets in set order, a sheet shown as "Number - Title") and the
    /// Details or Preview pane under it. Sheets and subsets move by drag and drop; the right-click menus of the set, a
    /// subset and a sheet run the operations of <see cref="CadSheetSetFile"/>. The window of the app (<c>SheetSetManagerWindow</c>) only
    /// draws what this model says.
    /// </summary>
    /// <remarks>
    /// <para>
    /// This model keeps the sets it has open and the one current for as long as its window of the app lives, whether the palette
    /// is shown or not. The tree is rebuilt from the file after every change and every <see cref="Refresh"/>: the rows are
    /// copies, and a part is found again in the file by its ID. A set that is read-only, or locked by another user, is shown
    /// with its lock and refuses every change.
    /// </para>
    /// <para>
    /// A drawing a sheet operation rewrites (an import, a rename of its layout or file, Resave All Sheets) is saved and closed
    /// first where it is open in the app, and opened again after, as the core's operations ask of their caller. Only the
    /// Sheet List tab is built; the Sheet Views and Model Views tabs come with the next packages, and so do the dialogs a menu
    /// item asks for (<see cref="DialogRequested"/>).
    /// </para>
    /// </remarks>
    public sealed partial class SheetSetManagerViewModel : ObservableObject
    {
        private const int PreviewWidth = 300;
        private const int PreviewHeight = 190;
        private const string SheetListTab = "Sheet List";

        private readonly MainViewModel _main;
        private readonly List<CadSheetSetFile> _sets = new List<CadSheetSetFile>();
        private readonly HashSet<string> _collapsed = new HashSet<string>(StringComparer.Ordinal);
        private readonly Dictionary<string, FoundCache> _found = new Dictionary<string, FoundCache>(
            StringComparer.Ordinal
        );
        private CadSheetSetFile _current;
        private SheetSetNodeViewModel _root;
        private string _lockReason;
        private string _tab = SheetListTab;
        private bool _showPreview;
        private ImageSource _preview;
        private PreviewKey _previewKey;
        private bool _suspended;

        /// <summary>Makes the palette over the window's view model, which opens the drawings of its sheets.</summary>
        /// <param name="main">The window's view model.</param>
        /// <exception cref="ArgumentNullException">Thrown when the model is null.</exception>
        public SheetSetManagerViewModel(MainViewModel main)
        {
            _main = main ?? throw new ArgumentNullException(nameof(main));
            _main.PropertyChanged += OnMainChanged;
            _viewsTree.Changed += OnTabTreeChanged;
            _modelTree.Changed += OnTabTreeChanged;
        }

        /// <summary>Gets the rows of the tree that are shown, in order: the set, then its subsets and sheets, a collapsed subset's rows left out.</summary>
        public ObservableCollection<SheetSetNodeViewModel> Rows { get; } =
            new ObservableCollection<SheetSetNodeViewModel>();

        /// <summary>Gets the lines of the Details pane for the first row selected; none where none is.</summary>
        public ObservableCollection<SheetSetDetailRow> Details { get; } = new ObservableCollection<SheetSetDetailRow>();

        /// <summary>Gets the row of the sheet set; null where no set is open.</summary>
        public SheetSetNodeViewModel Root => _root;

        /// <summary>Gets whether a sheet set is open and current.</summary>
        public bool HasSet => _current != null;

        /// <summary>Gets the sheet set current, as the file model; null where none is open.</summary>
        public CadSheetSetFile Current => _current;

        /// <summary>Gets the words of the sheet set control: the name of the current set, or "Open…" where none is.</summary>
        public string ControlText => _current == null ? "Open…" : _current.SheetSet.Name;

        /// <summary>Gets what the tree says where no set is open.</summary>
        public string EmptyHint =>
            "No sheet set is open." + Environment.NewLine + "Pick New Sheet Set… or Open… in the list above.";

        /// <summary>Gets whether the current set is read-only: its file is, or another user holds its lock. Every change is refused and the lock shows by the control.</summary>
        public bool IsLocked => _lockReason != null;

        /// <summary>Gets why the current set is locked, as "Read-only: the file is read-only."; null where it is not.</summary>
        public string LockText => _lockReason;

        /// <summary>Gets the tooltip of the lock icon by the control; null where the set is not locked.</summary>
        public string LockToolTip =>
            _lockReason == null ? null : _lockReason + Environment.NewLine + "The sheet set opens read-only.";

        /// <summary>Gets or sets the tab shown: "Sheet List", "Sheet Views" or "Model Views".</summary>
        public string Tab
        {
            get => _tab;
            set
            {
                if (Set(ref _tab, value))
                {
                    Raise(nameof(ShowTree));
                    Raise(nameof(HintText));
                    OnTabChanged();
                }
            }
        }

        /// <summary>Gets whether the tree is shown: a set is open and the Sheet List tab is.</summary>
        public bool ShowTree => _current != null && _tab == SheetListTab;

        /// <summary>Gets what the body says where there is no tree: the hint for no set open; nothing where a set is open.</summary>
        public string HintText => _current == null ? EmptyHint : string.Empty;

        /// <summary>Gets or sets whether the pane under the tree shows the picture of the sheet (Preview) and not its details (Details).</summary>
        public bool ShowPreview
        {
            get => _showPreview;
            set
            {
                if (Set(ref _showPreview, value))
                {
                    Raise(nameof(ShowDetails));
                    Raise(nameof(HasPreview));
                    Raise(nameof(NoPreview));
                    UpdatePreview();
                }
            }
        }

        /// <summary>Gets whether the pane under the tree shows the details.</summary>
        public bool ShowDetails => !_showPreview;

        /// <summary>Gets whether the pane shows a picture: it shows the preview and the sheet selected has one.</summary>
        public bool HasPreview => _showPreview && _preview != null;

        /// <summary>Gets whether the pane shows the preview and has no picture to show.</summary>
        public bool NoPreview => _showPreview && _preview == null;

        /// <summary>Gets the picture of the first sheet selected, its layout drawn; null for none, or where the pane shows the details.</summary>
        public ImageSource Preview
        {
            get => _preview;
            private set
            {
                if (Set(ref _preview, value))
                {
                    Raise(nameof(HasPreview));
                    Raise(nameof(NoPreview));
                }
            }
        }

        /// <summary>Gets the rows selected, in the order of the tree.</summary>
        public IReadOnlyList<SheetSetNodeViewModel> SelectedNodes => Rows.Where(r => r.IsSelected).ToList();

        /// <summary>Gets or sets how the palette asks the file dialog of Open…, which gives the path of a .dst; null where there is none (Open… then starts OPENSHEETSET).</summary>
        public Func<string> AskOpenFile { get; set; }

        /// <summary>Gets or sets how Remove Sheet asks before it removes, given the question; null asks nothing.</summary>
        public Func<string, bool> ConfirmRemove { get; set; }

        /// <summary>Raised when a menu item asks for a dialog of the next package; a handler that shows it sets <see cref="SheetSetDialogEventArgs.Handled"/>.</summary>
        public event EventHandler<SheetSetDialogEventArgs> DialogRequested;

        /// <summary>
        /// Makes the middle of a path short, as Recent lists the paths: the drive, "...", then the file name, "D:\...\Example
        /// Tower.dst"; a network path as "\\...\Harbour Offices.dst". A path whose file is in the drive's own folder is left whole.
        /// </summary>
        /// <param name="path">The full path.</param>
        /// <returns>The short form.</returns>
        public static string ShortPath(string path)
        {
            string folder = Path.GetDirectoryName(path);
            string root = Path.GetPathRoot(path);

            if (
                string.IsNullOrEmpty(folder)
                || string.IsNullOrEmpty(root)
                || folder.TrimEnd('\\') == root.TrimEnd('\\')
            )
            {
                return path;
            }

            return (root.StartsWith(@"\\", StringComparison.Ordinal) ? @"\\" : root) + @"...\" + Path.GetFileName(path);
        }

        /// <summary>
        /// Opens a sheet set data file and makes it the current set; a set open already is made current. The set is put first in
        /// Recent. A file that is not there or is no sheet set data file is not opened and the command line says why.
        /// </summary>
        /// <param name="path">The path of the .dst.</param>
        /// <returns>Whether the set is open and current.</returns>
        public bool OpenSet(string path)
        {
            if (string.IsNullOrWhiteSpace(path))
            {
                return false;
            }

            string full;

            try
            {
                full = Path.GetFullPath(path);
            }
            catch (Exception ex) when (IsFileProblem(ex))
            {
                _main.Say("Cannot open the sheet set " + path + ": " + ex.Message);
                return false;
            }

            CadSheetSetFile known = _sets.FirstOrDefault(s => SamePath(s.Path, full));

            if (known != null)
            {
                MakeCurrent(known);
                return true;
            }

            CadSheetSetFile file;

            try
            {
                file = CadSheetSetFile.OpenForEditing(full);
            }
            catch (Exception ex) when (IsFileProblem(ex))
            {
                _main.Say("Cannot open the sheet set " + full + ": " + ex.Message);
                return false;
            }

            _sets.Add(file);
            _main.NoteRecentSheetSet(file.Path);
            MakeCurrent(file);
            return true;
        }

        /// <summary>Closes the current sheet set, as Close Sheet Set does; the set opened before it, if there is one, becomes current. The lock file the set was opened with is deleted; the .dst is not touched.</summary>
        public void CloseSet()
        {
            if (_current == null)
            {
                return;
            }

            _current.ReleaseLock();
            _sets.Remove(_current);
            MakeCurrent(_sets.LastOrDefault());
        }

        /// <summary>
        /// Reads the set again where its file changed on disk, looks at every sheet's drawing again and shows the tree as it is
        /// now: a drawing opened or closed in the session, a file deleted or renamed, the lock taken or let go.
        /// </summary>
        public void Refresh()
        {
            if (_current != null)
            {
                try
                {
                    _current.ReloadIfChanged();
                }
                catch (Exception ex) when (IsFileProblem(ex))
                {
                    _main.Say("Cannot read the sheet set " + _current.Path + " again: " + ex.Message);
                }

                _lockReason = LockReasonOf(_current);
                Raise(nameof(IsLocked));
                Raise(nameof(LockText));
                Raise(nameof(LockToolTip));
            }

            Rebuild();
        }

        private void MakeCurrent(CadSheetSetFile file)
        {
            _current = file;
            _lockReason = file == null ? null : LockReasonOf(file);
            Raise(nameof(Current));
            Raise(nameof(HasSet));
            Raise(nameof(ShowTree));
            Raise(nameof(HintText));
            Raise(nameof(ShowTabBody));
            Raise(nameof(IsLocked));
            Raise(nameof(LockText));
            Raise(nameof(LockToolTip));

            foreach (SheetSetNodeViewModel node in Rows)
            {
                node.StartSelected(false);
            }

            Rebuild();
        }

        /// <summary>
        /// Builds the tree again from the file, keeping what is collapsed and what is selected, by ID. Where the set has the same
        /// subsets and sheets in the same places as the rows shown, the rows are kept and only what they say is updated, so that the
        /// list keeps its place and its selection (a look at the sheets again must not scroll it to the top).
        /// </summary>
        private void Rebuild()
        {
            RebuildTabs();
            SheetSetNodeViewModel fresh = _current == null ? null : Build(_current.SheetSet, null);

            if (fresh != null && _root != null && SameShape(_root, fresh))
            {
                Update(_root, fresh);
                Raise(nameof(ControlText));
                UpdateDetails();
                return;
            }

            List<string> selected = Rows.Where(r => r.IsSelected).Select(r => r.Id).ToList();
            _suspended = true;
            Rows.Clear();
            _root = null;

            if (_current != null)
            {
                _root = fresh;

                foreach (SheetSetNodeViewModel node in Show(_root))
                {
                    node.StartSelected(selected.Contains(node.Id));
                    Rows.Add(node);
                }
            }

            _suspended = false;
            Raise(nameof(ControlText));
            Raise(nameof(Root));
            Raise(nameof(SelectedNodes));
            UpdateDetails();
        }

        /// <summary>Whether two trees hold the same parts, of the same kinds, in the same places.</summary>
        private static bool SameShape(SheetSetNodeViewModel a, SheetSetNodeViewModel b) =>
            a.Id == b.Id
            && a.Kind == b.Kind
            && a.Children.Count == b.Children.Count
            && a.Children.Zip(b.Children, SameShape).All(same => same);

        /// <summary>Gives the rows of a tree what the same rows of a newer tree say.</summary>
        private static void Update(SheetSetNodeViewModel row, SheetSetNodeViewModel fresh)
        {
            row.Text = fresh.Text;
            row.Found = fresh.Found;
            row.Expected = fresh.Expected;
            row.IsMissing = fresh.IsMissing;
            row.IsExcluded = fresh.IsExcluded;
            row.IsOpen = fresh.IsOpen;
            row.Tone = fresh.Tone;

            for (int i = 0; i < row.Children.Count; i++)
            {
                Update(row.Children[i], fresh.Children[i]);
            }
        }

        private SheetSetNodeViewModel Build(CadSheetComponent component, SheetSetNodeViewModel parent)
        {
            SheetSetNodeKind kind =
                component is CadSheetSet ? SheetSetNodeKind.SheetSet
                : component is CadSheetSubset ? SheetSetNodeKind.Subset
                : SheetSetNodeKind.Sheet;
            var node = new SheetSetNodeViewModel(this, kind, component.Id, parent);
            node.StartExpanded(!_collapsed.Contains(component.Id));

            if (component is CadSheetSubset subset)
            {
                node.Text = subset.Name;

                foreach (CadSheetComponent child in subset.Children)
                {
                    node.Add(Build(child, node));
                }
            }
            else
            {
                var sheet = (CadSheetSetSheet)component;
                node.Text = string.IsNullOrEmpty(sheet.Number) ? sheet.Title : sheet.Number + " - " + sheet.Title;
                node.Expected = sheet.Layout;
                node.Found = FoundOf(sheet);
                node.IsMissing = node.Found.IsMissing;
                node.IsExcluded = !sheet.IncludeForPublish;
                node.IsOpen = !node.IsMissing && _main.OpenDocumentOf(node.Found.DrawingFile) != null;
                node.Tone =
                    node.IsMissing ? SheetSetTone.Error
                    : node.IsExcluded ? SheetSetTone.Muted
                    : SheetSetTone.Normal;
            }

            return node;
        }

        /// <summary>The rows a node and the nodes it shows under it take in the list, in order.</summary>
        private static IEnumerable<SheetSetNodeViewModel> Show(SheetSetNodeViewModel node)
        {
            yield return node;

            if (node.IsExpanded)
            {
                foreach (SheetSetNodeViewModel child in node.Children)
                {
                    foreach (SheetSetNodeViewModel row in Show(child))
                    {
                        yield return row;
                    }
                }
            }
        }

        private static IEnumerable<SheetSetNodeViewModel> Walk(SheetSetNodeViewModel node)
        {
            yield return node;

            foreach (SheetSetNodeViewModel child in node.Children)
            {
                foreach (SheetSetNodeViewModel below in Walk(child))
                {
                    yield return below;
                }
            }
        }

        /// <summary>Every sheet row of the tree, shown or not, in set order.</summary>
        private IEnumerable<SheetSetNodeViewModel> AllSheets() =>
            _root == null ? Enumerable.Empty<SheetSetNodeViewModel>() : Walk(_root).Where(n => n.IsSheet);

        internal void NodeExpansionChanged(SheetSetNodeViewModel node)
        {
            if (_suspended)
            {
                return;
            }

            if (node.IsExpanded)
            {
                _collapsed.Remove(node.Id);
            }
            else
            {
                _collapsed.Add(node.Id);
            }

            int at = Rows.IndexOf(node);

            if (at < 0)
            {
                return;
            }

            _suspended = true;

            if (node.IsExpanded)
            {
                int place = at + 1;

                foreach (SheetSetNodeViewModel row in Show(node).Skip(1))
                {
                    Rows.Insert(place++, row);
                }
            }
            else
            {
                while (at + 1 < Rows.Count && Rows[at + 1].Depth > node.Depth)
                {
                    Rows[at + 1].StartSelected(false);
                    Rows.RemoveAt(at + 1);
                }
            }

            _suspended = false;
            Raise(nameof(SelectedNodes));
            UpdateDetails();
        }

        internal void NodeSelectionChanged()
        {
            if (_suspended)
            {
                return;
            }

            Raise(nameof(SelectedNodes));
            UpdateDetails();
        }

        private void OnMainChanged(object sender, PropertyChangedEventArgs e)
        {
            if (e.PropertyName == nameof(MainViewModel.Document) && _root != null)
            {
                RefreshOpenState();
            }
        }

        /// <summary>Marks the sheets whose drawing is open in the session, as the green dot of their icons says.</summary>
        private void RefreshOpenState()
        {
            foreach (SheetSetNodeViewModel sheet in AllSheets())
            {
                sheet.IsOpen = !sheet.IsMissing && _main.OpenDocumentOf(sheet.Found.DrawingFile) != null;
            }

            UpdateDetails();
        }

        // ---- What is found of each sheet's layout

        /// <summary>The layout of a sheet, found as <see cref="CadSheetSetSheet.FindLayout"/> finds it, but a drawing is read again only when its file's time stamp or length moved (reading one takes about 0.8 ms).</summary>
        private CadFoundLayout FoundOf(CadSheetSetSheet sheet)
        {
            CadDrawingReference reference = sheet.Layout;
            string path = reference?.File.Resolve(_current.Folder);

            if (path == null || !File.Exists(path))
            {
                return sheet.FindLayout();
            }

            var info = new FileInfo(path);
            string key = path + "|" + reference.Handle + "|" + reference.Name;

            if (
                _found.TryGetValue(sheet.Id, out FoundCache cache)
                && cache.Key == key
                && cache.Stamp == info.LastWriteTimeUtc
                && cache.Length == info.Length
            )
            {
                return cache.Found;
            }

            CadFoundLayout found = sheet.FindLayout();
            _found[sheet.Id] = new FoundCache(key, info.LastWriteTimeUtc, info.Length, found);
            return found;
        }

        private sealed class FoundCache
        {
            internal FoundCache(string key, DateTime stamp, long length, CadFoundLayout found)
            {
                Key = key;
                Stamp = stamp;
                Length = length;
                Found = found;
            }

            internal string Key { get; }

            internal DateTime Stamp { get; }

            internal long Length { get; }

            internal CadFoundLayout Found { get; }
        }

        // ---- The details and the preview

        private SheetSetNodeViewModel FirstSelected => Rows.FirstOrDefault(r => r.IsSelected);

        private void UpdateDetails()
        {
            Details.Clear();

            if (_tab != SheetListTab)
            {
                DescribeTab();
                UpdatePreview();
                return;
            }

            SheetSetNodeViewModel node = FirstSelected;

            if (node != null && _current != null)
            {
                switch (node.Kind)
                {
                    case SheetSetNodeKind.SheetSet:
                        DescribeSet(node);
                        break;
                    case SheetSetNodeKind.Subset:
                        DescribeSubset(node);
                        break;
                    default:
                        DescribeSheet(node);
                        break;
                }
            }

            UpdatePreview();
        }

        private void DescribeSet(SheetSetNodeViewModel node)
        {
            CadSheetSet set = _current.SheetSet;
            Details.Add(new SheetSetDetailRow("Sheet set", set.Name));
            Details.Add(
                IsLocked
                    ? new SheetSetDetailRow("Status", _lockReason, SheetSetTone.Warn)
                    : new SheetSetDetailRow("Status", "Available", SheetSetTone.Ok)
            );
            Details.Add(new SheetSetDetailRow("Data file", _current.Path));
            Details.Add(new SheetSetDetailRow("Sheets", Counted(node)));
            AddDescription(set.Description);
        }

        private void DescribeSubset(SheetSetNodeViewModel node)
        {
            Details.Add(new SheetSetDetailRow("Subset", node.Text));
            Details.Add(
                new SheetSetDetailRow("Sheets", Walk(node).Count(n => n.IsSheet).ToString(CultureInfo.InvariantCulture))
            );
            AddDescription((_current.SheetSet.Find(node.Id) as CadSheetSubset)?.Description);
        }

        private void DescribeSheet(SheetSetNodeViewModel node)
        {
            var sheet = _current.SheetSet.Find(node.Id) as CadSheetSetSheet;
            Details.Add(new SheetSetDetailRow("Sheet", node.Text));

            if (node.IsMissing)
            {
                CadFoundLayout found = node.Found;
                Details.Add(
                    new SheetSetDetailRow(
                        "Status",
                        found.DrawingFile == null
                            ? "Not found: the drawing file does not exist."
                            : "Not found: the drawing has no layout \"" + node.Expected?.Name + "\".",
                        SheetSetTone.Error
                    )
                );
                Details.Add(new SheetSetDetailRow("Expected", ExpectedPath(node.Expected)));
                Details.Add(new SheetSetDetailRow("Layout", node.Expected?.Name ?? string.Empty));
                Details.Add(
                    new SheetSetDetailRow(
                        "To fix",
                        "Sheet Properties, Expected layout: pick the drawing and layout, or Remove Sheet."
                    )
                );
                return;
            }

            Details.Add(
                node.IsOpen
                    ? new SheetSetDetailRow("Status", "Open in this session", SheetSetTone.Ok)
                    : new SheetSetDetailRow("Status", "Available", SheetSetTone.Ok)
            );
            Details.Add(new SheetSetDetailRow("Subset", node.Parent?.Text ?? string.Empty));
            AddDescription(sheet?.Description);
            string drawing = node.Found.DrawingFile;
            Details.Add(new SheetSetDetailRow("File", Path.GetFileName(drawing)));
            Details.Add(new SheetSetDetailRow("Folder", Path.GetDirectoryName(drawing)));
            Details.Add(new SheetSetDetailRow("Layout", node.Found.LayoutName));
            var info = new FileInfo(drawing);
            Details.Add(new SheetSetDetailRow("Size", SizeOf(info.Length)));
            Details.Add(
                new SheetSetDetailRow(
                    "Last saved",
                    info.LastWriteTime.ToString("yyyy-MM-dd HH:mm", CultureInfo.InvariantCulture)
                )
            );
        }

        private void AddDescription(string description)
        {
            if (!string.IsNullOrEmpty(description))
            {
                Details.Add(new SheetSetDetailRow("Description", description));
            }
        }

        /// <summary>The sheets of the set and its subsets as the Details pane counts them: "11 in 3 subsets", or the number alone where there is no subset.</summary>
        private static string Counted(SheetSetNodeViewModel node)
        {
            int sheets = Walk(node).Count(n => n.IsSheet);
            int subsets = Walk(node).Count(n => n.IsSubset);
            string count = sheets.ToString(CultureInfo.InvariantCulture);
            return subsets == 0
                ? count
                : count
                    + " in "
                    + subsets.ToString(CultureInfo.InvariantCulture)
                    + (subsets == 1 ? " subset" : " subsets");
        }

        private string ExpectedPath(CadDrawingReference reference)
        {
            CadFileReference file = reference?.File;

            if (file == null)
            {
                return string.Empty;
            }

            return file.FileName
                ?? (
                    file.RelativeFileName == null
                        ? string.Empty
                        : Path.GetFullPath(Path.Combine(_current.Folder, file.RelativeFileName))
                );
        }

        private static string SizeOf(long bytes) =>
            bytes < 1024L * 1024L * 10L
                ? Math.Max(1L, (bytes + 512L) / 1024L).ToString(CultureInfo.InvariantCulture) + " KB"
                : (bytes / (1024.0 * 1024.0)).ToString("0.0", CultureInfo.InvariantCulture) + " MB";

        private sealed class PreviewKey : IEquatable<PreviewKey>
        {
            internal PreviewKey(string path, string layout, DateTime stamp, long length)
            {
                Path = path;
                Layout = layout;
                Stamp = stamp;
                Length = length;
            }

            internal string Path { get; }

            internal string Layout { get; }

            internal DateTime Stamp { get; }

            internal long Length { get; }

            public bool Equals(PreviewKey other) =>
                other != null
                && Path == other.Path
                && Layout == other.Layout
                && Stamp == other.Stamp
                && Length == other.Length;

            public override bool Equals(object obj) => Equals(obj as PreviewKey);

            public override int GetHashCode() => (Path ?? string.Empty).GetHashCode() ^ Length.GetHashCode();
        }

        /// <summary>Draws the layout of the first sheet selected where the pane shows the preview; the picture of a drawing is kept until its file changes.</summary>
        private void UpdatePreview()
        {
            SheetSetNodeViewModel node = FirstSelected;

            if (!_showPreview || _tab != SheetListTab || node == null || !node.IsSheet || node.IsMissing)
            {
                _previewKey = null;
                Preview = null;
                return;
            }

            string path = node.Found.DrawingFile;
            var info = new FileInfo(path);
            var key = new PreviewKey(path, node.Found.LayoutName, info.LastWriteTimeUtc, info.Length);

            if (key.Equals(_previewKey))
            {
                return;
            }

            _previewKey = key;

            try
            {
                Preview = CadStylePreview.Layout(
                    CadDrawingFile.Open(path),
                    node.Found.LayoutName,
                    _main.Document?.Fonts,
                    PreviewWidth,
                    PreviewHeight
                );
            }
            catch (Exception ex) when (IsFileProblem(ex))
            {
                GeometryHelperLog.Debug("SheetSetManagerViewModel: no preview of " + path + ": " + ex.Message);
                Preview = null;
            }
        }

        // ---- Locks and paths

        /// <summary>
        /// Why a sheet set cannot be changed, as "Read-only: The sheet set data file is read-only." or "Read-only: Locked for Edit by
        /// InuYasha on INUYASHA-MEV.", or null where it can: the file is read-only, or another user holds the .DBL lock file
        /// AutoCAD and this program keep beside a set that is open for editing (<see cref="CadSheetSetFile.ReadOnlyReason"/>).
        /// </summary>
        internal static string LockReasonOf(CadSheetSetFile file)
        {
            string reason = file.ReadOnlyReason;
            return reason == null ? null : "Read-only: " + reason;
        }

        private static bool SamePath(string a, string b)
        {
            if (a == null || b == null)
            {
                return false;
            }

            try
            {
                return string.Equals(
                    Path.GetFullPath(a).TrimEnd('\\', '/'),
                    Path.GetFullPath(b).TrimEnd('\\', '/'),
                    StringComparison.OrdinalIgnoreCase
                );
            }
            catch (Exception ex) when (IsFileProblem(ex))
            {
                return false;
            }
        }

        /// <summary>Whether an exception is a file that cannot be read or written, or a name that is no path, which the palette says on the command line and goes on.</summary>
        private static bool IsFileProblem(Exception ex) =>
            ex is IOException
            || ex is UnauthorizedAccessException
            || ex is FormatException
            || ex is ArgumentException
            || ex is NotSupportedException
            || ex is InvalidOperationException
            || ex is System.Xml.XmlException;
    }
}
