using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.IO;
using System.Linq;
using GeometryHelper;
using OpenCAD2D.SheetSets;

namespace OpenCAD2D.Wpf
{
    /// <summary>
    /// A row of the Sheets tab of Create Transmittal: the sheet set, a subset or a sheet, with a box to tick. A sheet whose
    /// drawing is not found is red and cannot be ticked; a set or subset shows a dash where some of its sheets are ticked and
    /// others are not. The row is the dialog's own copy of the part of the set.
    /// </summary>
    public sealed class TransmittalSheetRow : ObservableObject
    {
        private const int IndentWidth = 16;

        private readonly TransmittalViewModel _owner;
        private readonly List<TransmittalSheetRow> _children = new List<TransmittalSheetRow>();
        private bool _isChecked;
        private bool _isExpanded = true;

        internal TransmittalSheetRow(
            TransmittalViewModel owner,
            SheetSetNodeKind kind,
            string id,
            string text,
            int depth,
            bool isMissing
        )
        {
            _owner = owner;
            Kind = kind;
            Id = id;
            Text = text;
            Depth = depth;
            IsMissing = isMissing;
        }

        /// <summary>Gets what the row is: the set, a subset or a sheet.</summary>
        public SheetSetNodeKind Kind { get; }

        /// <summary>Gets the <c>ID</c> of the part in the sheet set file.</summary>
        public string Id { get; }

        /// <summary>Gets the words of the row: the name of the set or subset, <c>A-101 - Ground Floor Plan</c> for a sheet.</summary>
        public string Text { get; }

        /// <summary>Gets how deep the row is: 0 for the set.</summary>
        public int Depth { get; }

        /// <summary>Gets the width of the space left of the row for its depth.</summary>
        public double Indent => Depth * IndentWidth;

        /// <summary>Gets whether the sheet's drawing is not found, so that it is red and cannot be ticked; false for a set or subset.</summary>
        public bool IsMissing { get; }

        /// <summary>Gets whether the box can be ticked: not for a sheet that is missing, nor for a subset with nothing in it that can be.</summary>
        public bool IsEnabled => Kind == SheetSetNodeKind.Sheet ? !IsMissing : Leaves().Any(l => !l.IsMissing);

        /// <summary>Gets the name of the picture of the row: set, subset, sheet or sheet-missing.</summary>
        public string Icon =>
            Kind == SheetSetNodeKind.SheetSet ? "set"
            : Kind == SheetSetNodeKind.Subset ? "subset"
            : IsMissing ? "sheet-missing"
            : "sheet";

        /// <summary>Gets the colour of the words: red for a sheet that is missing.</summary>
        public SheetSetTone Tone => IsMissing ? SheetSetTone.Error : SheetSetTone.Normal;

        /// <summary>Gets whether the row has rows under it.</summary>
        public bool IsExpandable => _children.Count > 0;

        /// <summary>Gets or sets whether the rows under it are shown.</summary>
        public bool IsExpanded
        {
            get => _isExpanded;
            set
            {
                if (Set(ref _isExpanded, value))
                {
                    Raise(nameof(Chevron));
                    _owner.Rebuild();
                }
            }
        }

        /// <summary>Gets the name of the arrow of the row: chevron-down where it is open, chevron-right where it is closed.</summary>
        public string Chevron => _isExpanded ? "chevron-down" : "chevron-right";

        /// <summary>
        /// Gets or sets whether the box is ticked: true where the sheet is (for a set or subset, where every sheet under it that can
        /// be is), false where it is not, null for a set or subset with some of its sheets ticked. Setting a row ticks, or clears, the
        /// sheets under it.
        /// </summary>
        public bool? IsChecked
        {
            get
            {
                if (Kind == SheetSetNodeKind.Sheet)
                {
                    return _isChecked;
                }

                List<TransmittalSheetRow> leaves = Leaves().Where(l => !l.IsMissing).ToList();
                int ticked = leaves.Count(l => l._isChecked);
                return ticked == 0 ? false
                    : ticked == leaves.Count ? true
                    : (bool?)null;
            }
            set => _owner.SetChecked(this, value == true);
        }

        internal List<TransmittalSheetRow> Children => _children;

        internal TransmittalSheetRow Parent { get; set; }

        internal bool IsLeafChecked
        {
            get => _isChecked;
            set => _isChecked = value;
        }

        /// <summary>Tells the window the row's box has changed, itself and the rows above it.</summary>
        internal void RaiseChecked()
        {
            Raise(nameof(IsChecked));
        }

        internal IEnumerable<TransmittalSheetRow> Leaves()
        {
            if (Kind == SheetSetNodeKind.Sheet)
            {
                yield return this;
                yield break;
            }

            foreach (TransmittalSheetRow child in _children)
            {
                foreach (TransmittalSheetRow leaf in child.Leaves())
                {
                    yield return leaf;
                }
            }
        }
    }

    /// <summary>A row of the Files Table of Create Transmittal: a file, its box, where it goes in the zip and how big it is.</summary>
    public sealed class TransmittalFileRow : ObservableObject
    {
        private readonly TransmittalViewModel _owner;

        internal TransmittalFileRow(TransmittalViewModel owner, CadTransmittalFile file)
        {
            _owner = owner;
            File = file;
        }

        /// <summary>Gets the file.</summary>
        public CadTransmittalFile File { get; }

        /// <summary>Gets the name of the file.</summary>
        public string Name => File.Name;

        /// <summary>Gets or sets whether the file goes in the zip; a file that was not found stays off.</summary>
        public bool IsIncluded
        {
            get => File.IsIncluded;
            set
            {
                if (File.IsIncluded != (value && File.IsFound))
                {
                    File.IsIncluded = value;
                    Raise(nameof(IsIncluded));
                    _owner.NoteIncludedChanged(this);
                }
            }
        }

        /// <summary>Gets whether the box can be ticked: not for a file that was not found.</summary>
        public bool IsEnabled => File.IsFound;

        /// <summary>Gets the column "Path in zip": the folder of the file in the zip; for a file not found, <c>missing: referenced by a101.dwg</c>.</summary>
        public string Where =>
            File.IsFound ? File.FolderInZip : "missing: referenced by " + string.Join(", ", File.UsedBy);

        /// <summary>Gets the size as the table writes it.</summary>
        public string SizeText => File.SizeText;

        /// <summary>Gets the colour of the row: red for a file that was not found.</summary>
        public SheetSetTone Tone => File.IsFound ? SheetSetTone.Normal : SheetSetTone.Error;

        /// <summary>Gets what the file is, for its tooltip: <c>external reference</c>, <c>image</c>, <c>plot style table</c>.</summary>
        public string KindText
        {
            get
            {
                switch (File.Kind)
                {
                    case CadTransmittalFileKind.SheetSet:
                        return "Sheet set data file";
                    case CadTransmittalFileKind.Sheet:
                        return "Drawing of a sheet";
                    case CadTransmittalFileKind.ExternalReference:
                        return "External reference";
                    case CadTransmittalFileKind.Template:
                        return "Sheet creation template";
                    case CadTransmittalFileKind.PageSetupOverrides:
                        return "Page setup overrides";
                    case CadTransmittalFileKind.Image:
                        return "Image";
                    case CadTransmittalFileKind.PdfUnderlay:
                        return "PDF underlay";
                    case CadTransmittalFileKind.DataLink:
                        return "Data link file";
                    case CadTransmittalFileKind.PlotStyleTable:
                        return "Plot style table";
                    default:
                        return "Font";
                }
            }
        }

        /// <summary>Gets the name of the picture of the row (the Sheet Set Manager's): set for the data file, sheet-missing for a file not found, override for a template, sheet otherwise.</summary>
        public string Icon =>
            !File.IsFound ? "sheet-missing"
            : File.Kind == CadTransmittalFileKind.SheetSet ? "set"
            : File.Kind == CadTransmittalFileKind.Template || File.Kind == CadTransmittalFileKind.PageSetupOverrides
                ? "override"
            : File.Kind == CadTransmittalFileKind.PdfUnderlay ? "pdf"
            : "sheet";

        /// <summary>Gets the tooltip: what the file is, and where it is.</summary>
        public string ToolTip =>
            KindText + (File.IsFound ? ": " + File.Source : ": not found, named " + File.Reference);
    }

    /// <summary>
    /// AutoCAD 2021's Create Transmittal, reduced as the design draws it (A12 5b, D18): a Sheets tab with the set, its subsets
    /// and sheets to tick, a Files Table with what those sheets need (a file can be unticked, one that is not found is red and
    /// left out), the notes that go in the report, the zip to write (<c>Transmittals\&lt;set name&gt;.zip</c> beside the .dst
    /// until changed) and Update fields in the copies first. View Report… shows <c>transmittal.txt</c> as it would be written; OK
    /// writes the zip (<see cref="CadTransmittal"/>).
    /// </summary>
    public sealed class TransmittalViewModel : ObservableObject
    {
        private readonly CadTransmittal _transmittal;
        private readonly List<TransmittalSheetRow> _all = new List<TransmittalSheetRow>();

        // The files unticked by their full path, kept across a new list as sheets are ticked: looked up only, never listed.
        private readonly HashSet<string> _unticked = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        private IReadOnlyList<CadTransmittalFile> _files = new List<CadTransmittalFile>();
        private bool _isSheetsTab = true;
        private bool _updateFields;
        private string _notes = string.Empty;
        private string _zipPath;
        private string _message = string.Empty;
        private string _status = string.Empty;
        private bool _statusIsError;
        private bool _loading;

        /// <summary>Makes the dialog over the sheets a right-click chose.</summary>
        /// <param name="transmittal">The transmittal of the sheet set, which the dialog changes the notes and the field box of.</param>
        /// <param name="selection">The sheets, subsets and the set the menu was opened on; null or empty for the whole set. The sheets under them are ticked, but those whose drawing is not found.</param>
        /// <exception cref="ArgumentNullException">Thrown when the transmittal is null.</exception>
        public TransmittalViewModel(CadTransmittal transmittal, IReadOnlyList<CadSheetComponent> selection)
        {
            _transmittal = transmittal ?? throw new ArgumentNullException(nameof(transmittal));
            _updateFields = transmittal.UpdateFields;
            _notes = transmittal.Notes ?? string.Empty;
            _zipPath = transmittal.DefaultZipPath;
            CadSheetSetFile file = transmittal.SheetSetFile;
            file.ReloadIfChanged();
            CadSheetSet set = file.SheetSet;
            SetName = set.Name;
            var chosen = new HashSet<string>(
                (selection ?? new List<CadSheetComponent>()).Where(c => c != null).Select(c => c.Id),
                StringComparer.Ordinal
            );
            bool whole = chosen.Count == 0 || chosen.Contains(set.Id);

            _loading = true;
            Build(set, null, 0, chosen, whole);
            _loading = false;
            Rows = new ObservableCollection<TransmittalSheetRow>();
            Rebuild();
            Recompute();
        }

        /// <summary>Gets the name of the sheet set.</summary>
        public string SetName { get; }

        /// <summary>Gets the title of the window, <c>Create Transmittal - Example Tower</c>.</summary>
        public string Title => "Create Transmittal - " + SetName;

        /// <summary>Gets the rows of the Sheets tab that are shown: the set, then its subsets and sheets in set order, those under a closed row left out.</summary>
        public ObservableCollection<TransmittalSheetRow> Rows { get; }

        /// <summary>Gets the rows of the Files Table: the files found first, in order of kind and then of path, then the files not found.</summary>
        public ObservableCollection<TransmittalFileRow> FileRows { get; } =
            new ObservableCollection<TransmittalFileRow>();

        /// <summary>Gets the page of the guide the Help button opens.</summary>
        public string HelpUrl =>
            "https://nguyenthanguth.github.io/opencad2d/guides/presenter/sheet-sets.html#etransmit";

        /// <summary>Gets or sets whether the Sheets tab is shown; the Files Table is where it is not.</summary>
        public bool IsSheetsTab
        {
            get => _isSheetsTab;
            set
            {
                if (Set(ref _isSheetsTab, value))
                {
                    Raise(nameof(IsFilesTab));
                }
            }
        }

        /// <summary>Gets or sets whether the Files Table is shown.</summary>
        public bool IsFilesTab
        {
            get => !_isSheetsTab;
            set => IsSheetsTab = !value;
        }

        /// <summary>Gets or sets the notes that go in the report, the box on the right.</summary>
        public string Notes
        {
            get => _notes;
            set => Set(ref _notes, value ?? string.Empty);
        }

        /// <summary>Gets or sets the zip to write, the Zip file box; <c>Transmittals\&lt;set name&gt;.zip</c> beside the .dst until changed.</summary>
        public string ZipPath
        {
            get => _zipPath;
            set
            {
                if (Set(ref _zipPath, value ?? string.Empty))
                {
                    Message = string.Empty;
                }
            }
        }

        /// <summary>Gets or sets whether the fields of the drawings are updated in the copies first (<see cref="CadTransmittal.UpdateFields"/>).</summary>
        public bool UpdateFields
        {
            get => _updateFields;
            set => Set(ref _updateFields, value);
        }

        /// <summary>Gets the line at the left of the buttons: what goes in the zip, <c>Included 14 files, 6.2 MB.</c>, or what is wrong with the files, in red.</summary>
        public string Status
        {
            get => _status;
            private set
            {
                if (Set(ref _status, value))
                {
                    RaiseFooter();
                }
            }
        }

        /// <summary>Gets whether <see cref="Status"/> is a fault (a file not found, no sheet ticked), to be written in red.</summary>
        public bool StatusIsError
        {
            get => _statusIsError;
            private set
            {
                if (Set(ref _statusIsError, value))
                {
                    RaiseFooter();
                }
            }
        }

        /// <summary>Gets what is wrong with the entries as OK found them, in the dialog's red line; empty where nothing is.</summary>
        public string Message
        {
            get => _message;
            private set
            {
                if (Set(ref _message, value))
                {
                    RaiseFooter();
                }
            }
        }

        /// <summary>Gets the line at the left of the buttons as the window shows it: <see cref="Message"/> where there is one, else <see cref="Status"/>.</summary>
        public string FooterText => _message.Length > 0 ? _message : _status;

        /// <summary>Gets whether <see cref="FooterText"/> is written in red: a message always is, a status where it is a fault.</summary>
        public bool FooterIsError => _message.Length > 0 || _statusIsError;

        /// <summary>Gets what OK wrote; null until it has.</summary>
        public CadTransmittalResult Result { get; private set; }

        /// <summary>Gets or sets how the browse button asks for the zip: given the path now in the box, gives the path chosen, or null for none. The window sets it; a test gives its own.</summary>
        public Func<string, string> AskZip { get; set; }

        /// <summary>Gets or sets how OK asks to replace a zip that is there: given its path, true to replace it. Where it is not set the zip is replaced.</summary>
        public Func<string, bool> AskReplace { get; set; }

        /// <summary>Gets or sets how View Report… shows the report: given its text. The window sets it; a test gives its own.</summary>
        public Action<string> ShowReport { get; set; }

        /// <summary>Gets how many files are ticked.</summary>
        public int IncludedCount => _files.Count(f => f.IsIncluded);

        /// <summary>Gets how many files were not found.</summary>
        public int MissingCount => _files.Count(f => !f.IsFound);

        /// <summary>Gets the sheets that are ticked, in set order.</summary>
        public IReadOnlyList<string> TickedSheets =>
            _all.Where(r => r.Kind == SheetSetNodeKind.Sheet && r.IsLeafChecked).Select(r => r.Id).ToList();

        /// <summary>The … button: asks for the zip and sets the Zip file box to it.</summary>
        public void BrowseZip()
        {
            string path = AskZip?.Invoke(_zipPath);

            if (!string.IsNullOrEmpty(path))
            {
                ZipPath = path;
            }
        }

        /// <summary>View Report…: shows <c>transmittal.txt</c> as it would be written now, the notes and the unticked files in it.</summary>
        public void ViewReport()
        {
            if (ShowReport == null)
            {
                return;
            }

            try
            {
                Hand();
                ShowReport(_transmittal.Report(_files, FullZipPath() ?? _transmittal.DefaultZipPath));
            }
            catch (Exception exception) when (IsFailure(exception))
            {
                GeometryHelperLog.Warn("TransmittalViewModel: the report was not made.", exception);
                Message = "The report could not be made: " + exception.Message;
            }
        }

        /// <summary>
        /// OK: checks the entries and, where they are good, writes the zip (asking first to replace one that is there). A zip name
        /// with no <c>.zip</c> at its end gets it.
        /// </summary>
        /// <returns>True where the zip was written (<see cref="Result"/> says what); false where an entry is wrong or the write failed, which <see cref="Message"/> says.</returns>
        public bool Accept()
        {
            if (!_all.Any(r => r.Kind == SheetSetNodeKind.Sheet && r.IsLeafChecked))
            {
                Message = "Tick at least one sheet.";
                return false;
            }

            string full = FullZipPath();
            if (full == null)
            {
                Message = "Enter the zip file to write.";
                return false;
            }

            if (File.Exists(full) && AskReplace != null && !AskReplace(full))
            {
                Message = "The zip was not replaced.";
                return false;
            }

            try
            {
                Hand();
                Result = _transmittal.Write(_files, full);
                return true;
            }
            catch (Exception exception) when (IsFailure(exception))
            {
                GeometryHelperLog.Warn("TransmittalViewModel: the transmittal was not written.", exception);
                Message = "The transmittal was not written: " + exception.Message;
                return false;
            }
        }

        private void RaiseFooter()
        {
            Raise(nameof(FooterText));
            Raise(nameof(FooterIsError));
        }

        private static bool IsFailure(Exception exception) =>
            exception is IOException
            || exception is UnauthorizedAccessException
            || exception is ArgumentException
            || exception is InvalidOperationException
            || exception is FormatException
            || exception is NotSupportedException;

        /// <summary>The Zip file box as a full path with .zip at its end; null where it holds no path.</summary>
        private string FullZipPath()
        {
            string text = _zipPath.Trim();

            if (text.Length == 0)
            {
                return null;
            }

            try
            {
                string full = Path.GetFullPath(text);
                return full.EndsWith(".zip", StringComparison.OrdinalIgnoreCase) ? full : full + ".zip";
            }
            catch (Exception exception)
                when (exception is ArgumentException
                    || exception is NotSupportedException
                    || exception is PathTooLongException
                )
            {
                return null;
            }
        }

        /// <summary>Hands the notes and the field box to the transmittal, which writes them.</summary>
        private void Hand()
        {
            _transmittal.Notes = _notes;
            _transmittal.UpdateFields = _updateFields;
        }

        private TransmittalSheetRow Build(
            CadSheetSubset group,
            TransmittalSheetRow parent,
            int depth,
            HashSet<string> chosen,
            bool selected
        )
        {
            bool isSet = parent == null;
            var row = new TransmittalSheetRow(
                this,
                isSet ? SheetSetNodeKind.SheetSet : SheetSetNodeKind.Subset,
                group.Id,
                isSet ? SetName : ((CadSheetSubset)group).Name,
                depth,
                false
            )
            {
                Parent = parent,
            };
            _all.Add(row);
            parent?.Children.Add(row);
            bool inside = selected || chosen.Contains(group.Id);

            foreach (CadSheetComponent child in group.Children)
            {
                if (child is CadSheetSubset subset)
                {
                    Build(subset, row, depth + 1, chosen, inside);
                    continue;
                }

                var sheet = (CadSheetSetSheet)child;
                bool missing = sheet.FindLayout().IsMissing;
                var leaf = new TransmittalSheetRow(
                    this,
                    SheetSetNodeKind.Sheet,
                    sheet.Id,
                    Label(sheet),
                    depth + 1,
                    missing
                )
                {
                    Parent = row,
                };
                leaf.IsLeafChecked = !missing && (inside || chosen.Contains(sheet.Id));
                row.Children.Add(leaf);
                _all.Add(leaf);
            }

            return row;
        }

        /// <summary>The sheet as the palette writes it: <c>A-101 - Ground Floor Plan</c>, the title alone for a sheet with no number.</summary>
        private static string Label(CadSheetSetSheet sheet)
        {
            string number = sheet.Number;
            string title = sheet.Title;
            return string.IsNullOrEmpty(number) ? title
                : string.IsNullOrEmpty(title) ? number
                : number + " - " + title;
        }

        /// <summary>Makes the shown rows again from the rows' open state: the set, and under each open row its own.</summary>
        internal void Rebuild()
        {
            if (_loading || Rows == null)
            {
                return;
            }

            Rows.Clear();
            Add(_all[0]);

            void Add(TransmittalSheetRow row)
            {
                Rows.Add(row);

                if (row.IsExpanded)
                {
                    foreach (TransmittalSheetRow child in row.Children)
                    {
                        Add(child);
                    }
                }
            }
        }

        /// <summary>Ticks or clears the sheets under a row that can be, and makes the file list again.</summary>
        internal void SetChecked(TransmittalSheetRow row, bool on)
        {
            foreach (TransmittalSheetRow leaf in row.Leaves().Where(l => !l.IsMissing))
            {
                leaf.IsLeafChecked = on;
            }

            foreach (TransmittalSheetRow each in _all)
            {
                each.RaiseChecked();
            }

            Message = string.Empty;
            Recompute();
        }

        internal void NoteIncludedChanged(TransmittalFileRow row)
        {
            if (row.File.IsFound)
            {
                if (row.File.IsIncluded)
                {
                    _unticked.Remove(row.File.Source);
                }
                else
                {
                    _unticked.Add(row.File.Source);
                }
            }

            Message = string.Empty;
            SayStatus();
        }

        /// <summary>Lists the files the ticked sheets need, with the files the user unticked left unticked, and says what goes in.</summary>
        private void Recompute()
        {
            List<CadSheetComponent> sheets = new List<CadSheetComponent>();
            CadSheetSet set = _transmittal.SheetSetFile.SheetSet;

            foreach (TransmittalSheetRow row in _all.Where(r => r.Kind == SheetSetNodeKind.Sheet && r.IsLeafChecked))
            {
                CadSheetComponent sheet = set.Find(row.Id);
                if (sheet != null)
                {
                    sheets.Add(sheet);
                }
            }

            FileRows.Clear();

            if (sheets.Count == 0)
            {
                _files = new List<CadTransmittalFile>();
                SayStatus();
                return;
            }

            try
            {
                _files = _transmittal.Gather(sheets);
            }
            catch (Exception exception) when (IsFailure(exception))
            {
                GeometryHelperLog.Warn("TransmittalViewModel: the files were not listed.", exception);
                _files = new List<CadTransmittalFile>();
                Message = "The files could not be listed: " + exception.Message;
                SayStatus();
                return;
            }

            foreach (CadTransmittalFile file in _files)
            {
                if (file.IsFound && _unticked.Contains(file.Source))
                {
                    file.IsIncluded = false;
                }

                FileRows.Add(new TransmittalFileRow(this, file));
            }

            SayStatus();
        }

        private void SayStatus()
        {
            Raise(nameof(IncludedCount));
            Raise(nameof(MissingCount));

            if (_files.Count == 0)
            {
                Status = "Tick at least one sheet.";
                StatusIsError = true;
                return;
            }

            int missing = MissingCount;
            if (missing > 0)
            {
                Status =
                    missing.ToString(System.Globalization.CultureInfo.InvariantCulture)
                    + (missing == 1 ? " file" : " files")
                    + " not found; "
                    + (missing == 1 ? "it is" : "they are")
                    + " listed in the report and left out.";
                StatusIsError = true;
                return;
            }

            int count = IncludedCount;
            long size = _files.Where(f => f.IsIncluded).Sum(f => f.Size);
            Status =
                "Included "
                + count.ToString(System.Globalization.CultureInfo.InvariantCulture)
                + (count == 1 ? " file, " : " files, ")
                + CadTransmittalFile.SizeOf(size)
                + ".";
            StatusIsError = false;
        }
    }
}
