using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.IO;
using System.Linq;
using OpenCAD2D.SheetSets;

namespace OpenCAD2D.Wpf
{
    /// <summary>One row of the list of Import Layouts as Sheets: a check box, the drawing's name, the layout's name and, where the set refuses the layout, why.</summary>
    public sealed class SheetSetImportRow : ObservableObject
    {
        private readonly SheetSetImportLayoutsViewModel _owner;
        private bool _isChecked;

        internal SheetSetImportRow(
            SheetSetImportLayoutsViewModel owner,
            string drawingName,
            CadSheetImportLayout layout
        )
        {
            _owner = owner;
            DrawingName = drawingName;
            LayoutName = layout.Name;
            Refusal = layout.Refusal;
            _isChecked = !layout.IsRefused;
        }

        /// <summary>Gets the drawing's file name ("S-101 Foundation Plan.dwg").</summary>
        public string DrawingName { get; }

        /// <summary>Gets the layout's name.</summary>
        public string LayoutName { get; }

        /// <summary>Gets why the set refuses the layout; null where it can become a sheet.</summary>
        public string Refusal { get; }

        /// <summary>Gets whether the set refuses the layout: its box is unchecked and cannot be checked.</summary>
        public bool IsRefused => Refusal != null;

        /// <summary>Gets the short note after a refused layout's name: "(in Riverside Clinic.dst)", "(already a sheet)" or "(# in its name)"; empty for a layout that is accepted.</summary>
        public string Note
        {
            get
            {
                if (!IsRefused)
                {
                    return string.Empty;
                }

                if (Refusal.IndexOf('#') >= 0 && Refusal.IndexOf("holds a #", StringComparison.Ordinal) >= 0)
                {
                    return "(# in its name)";
                }

                int open = Refusal.LastIndexOf('(');
                string path = open < 0 ? string.Empty : Refusal.Substring(open + 1).TrimEnd('.', ')');
                string folder = string.Empty;
                try
                {
                    folder = Path.GetFileName(path);
                }
                catch (ArgumentException)
                {
                    // A refusal whose parenthesis holds no path has no set file to name.
                }

                return folder.EndsWith(".dst", StringComparison.OrdinalIgnoreCase)
                    ? "(in " + folder + ")"
                    : "(already a sheet)";
            }
        }

        /// <summary>Gets or sets whether the layout is imported; a refused layout cannot be checked.</summary>
        public bool IsChecked
        {
            get => _isChecked;
            set
            {
                if (!IsRefused && Set(ref _isChecked, value))
                {
                    _owner.Changed();
                }
            }
        }
    }

    /// <summary>
    /// Import Layouts as Sheets, as the board A12ImportLayouts draws it: Browse for Drawings… chooses one drawing, the list
    /// shows its layouts in tab order with a check box each (checked unless the set refuses the layout: a # in its name, a
    /// sheet of this set already, a sheet of another sheet set), the reason of a refused layout is said in yellow, and Prefix
    /// sheet titles with file name. Import Checked adds a sheet for each layout checked at the end of the subset the dialog
    /// was opened from, with no number and the layout's name as its title, and writes the sheet set mark into the drawing.
    /// </summary>
    public sealed class SheetSetImportLayoutsViewModel : SheetSetDialogViewModel
    {
        private const string Filter = "Drawings (*.dwg)|*.dwg|All files (*.*)|*.*";

        private readonly string _intoId;
        private readonly ObservableCollection<SheetSetImportRow> _layouts =
            new ObservableCollection<SheetSetImportRow>();
        private string _drawingFile = string.Empty;
        private string _loadError;
        private bool _prefix;
        private List<string> _imported = new List<string>();

        /// <summary>Makes the dialog for a group.</summary>
        /// <param name="file">The sheet set's file.</param>
        /// <param name="into">The group the sheets go into (the sheet set itself or a subset of it).</param>
        /// <param name="options">What the caller gives the dialog; null for none.</param>
        /// <exception cref="ArgumentNullException">Thrown when the file or the group is null.</exception>
        public SheetSetImportLayoutsViewModel(
            CadSheetSetFile file,
            CadSheetSubset into,
            SheetSetDialogOptions options = null
        )
            : base(file, options)
        {
            _intoId = (into ?? throw new ArgumentNullException(nameof(into))).Id;
        }

        /// <summary>Gets the dialog's title: "Import Layouts as Sheets".</summary>
        public override string Title => Titled("Import Layouts as Sheets");

        /// <summary>Gets the path of the drawing chosen; empty before one is.</summary>
        public string DrawingFile => _drawingFile;

        /// <summary>Gets the line above the list: "Select a drawing file containing layouts:" before a drawing is chosen, then "Layouts in S-101.dwg (D:\Sheets):".</summary>
        public string ListCaption =>
            _drawingFile.Length == 0
                ? "Select a drawing file containing layouts:"
                : "Layouts in " + Path.GetFileName(_drawingFile) + " (" + Path.GetDirectoryName(_drawingFile) + "):";

        /// <summary>Gets the layouts of the drawing chosen, in tab order; empty before one is.</summary>
        public IReadOnlyList<SheetSetImportRow> Layouts => _layouts;

        /// <summary>Gets or sets whether each sheet's title is the drawing's file name, a hyphen and the layout's name.</summary>
        public bool PrefixTitleWithFileName
        {
            get => _prefix;
            set => Set(ref _prefix, value);
        }

        /// <summary>Gets the IDs of the sheets Import Checked made, in the order of the layouts; empty before it did.</summary>
        public IReadOnlyList<string> ImportedSheetIds => _imported;

        /// <summary>Gets whether Browse for Drawings… is live: not on a locked set.</summary>
        public bool CanBrowse => !IsReadOnly;

        /// <summary>Gets whether the list holds layouts.</summary>
        public bool HasLayouts => _layouts.Count > 0;

        /// <inheritdoc />
        protected override bool IsComplete => _layouts.Any(l => l.IsChecked);

        /// <summary>Runs Browse for Drawings…: asks for a drawing and lists its layouts.</summary>
        /// <returns>True when a drawing was chosen.</returns>
        public bool BrowseDrawing()
        {
            string path = BrowseFile?.Invoke("Select a drawing file containing layouts", Filter, _drawingFile);
            if (path == null)
            {
                return false;
            }

            Load(path);
            return true;
        }

        /// <summary>Lists the layouts of a drawing, checked where the set accepts them; a drawing that is not there or not a DWG is said so in red and leaves the list empty.</summary>
        /// <param name="path">The path of the drawing.</param>
        public void Load(string path)
        {
            _layouts.Clear();
            _loadError = null;
            _drawingFile = path ?? string.Empty;
            try
            {
                string full = Path.GetFullPath(_drawingFile);
                if (!System.IO.File.Exists(full))
                {
                    _loadError = "The drawing " + full + " is not there.";
                }
                else if (!CadDrawingFile.IsDwgName(full))
                {
                    _loadError = "Only a layout of a DWG can be a sheet, not one of " + full + ".";
                }
                else
                {
                    _drawingFile = full;
                    foreach (CadSheetImportLayout layout in File.SheetSet.CheckLayouts(full))
                    {
                        _layouts.Add(new SheetSetImportRow(this, Path.GetFileName(full), layout));
                    }

                    if (_layouts.Count == 0)
                    {
                        _loadError = "The drawing " + full + " has no layout.";
                    }
                }
            }
            catch (Exception e)
                when (e is ArgumentException
                    || e is IOException
                    || e is NotSupportedException
                    || e is FormatException
                    || e is UnauthorizedAccessException
                )
            {
                _loadError = "The drawing " + _drawingFile + " cannot be read: " + e.Message;
            }

            Raise(nameof(DrawingFile));
            Raise(nameof(HasLayouts));
            Raise(nameof(ListCaption));
            Changed();
        }

        /// <summary>Called by a row when it was checked or unchecked.</summary>
        internal void Changed()
        {
            Notice = _layouts.FirstOrDefault(l => l.IsRefused)?.Refusal;
            Revalidate();
        }

        /// <inheritdoc />
        protected override string Validate() => _loadError;

        /// <inheritdoc />
        protected override void Save()
        {
            string[] names = _layouts.Where(l => l.IsChecked).Select(l => l.LayoutName).ToArray();
            bool prefix = _prefix;
            string path = _drawingFile;
            var ids = new List<string>();
            Commit(
                set =>
                {
                    ids.Clear();
                    var into = (CadSheetSubset)set.Find(_intoId);
                    ids.AddRange(into.ImportLayouts(path, names, prefix).Select(s => s.Id));
                },
                new[] { path }
            );
            _imported = ids;
            Raise(nameof(ImportedSheetIds));
        }
    }
}
