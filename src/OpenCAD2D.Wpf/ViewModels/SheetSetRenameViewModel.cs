using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using OpenCAD2D.SheetSets;

namespace OpenCAD2D.Wpf
{
    /// <summary>
    /// Rename &amp; Renumber Sheet, as the board A12RenameRenumber draws it: Number, Sheet title, Layout name, File name and
    /// Folder path (read-only), the boxes Rename layout to match sheet title and Rename drawing file to match sheet title with
    /// their Prefix with sheet number, and Previous and Next. While a box is on, the name it is for follows the number and the
    /// title as they are typed (the number first where Prefix is on); a name typed directly is kept until the number or the
    /// title changes again. A layout name or file name the drawing cannot have is refused in red with OK greyed. Previous and
    /// Next apply the changes first, then load the neighbouring sheet in set order, so that a run of sheets can be renumbered
    /// in a row. A drawing open in the session is said so in yellow; saving, closing and reopening it is the caller's
    /// <see cref="SheetSetChanger"/>.
    /// </summary>
    public sealed class SheetSetRenameViewModel : SheetSetDialogViewModel
    {
        private string _sheetId;
        private string _number = string.Empty;
        private string _sheetTitle = string.Empty;
        private string _layoutName = string.Empty;
        private string _fileName = string.Empty;
        private string _folderPath = string.Empty;
        private bool _renameLayout = true;
        private bool _prefixLayout = true;
        private bool _renameFile;
        private bool _prefixFile = true;
        private string _currentLayout;
        private string _currentDrawing;
        private string _currentFile;
        private bool _loading;

        /// <summary>Makes the dialog over a sheet of the set.</summary>
        /// <param name="file">The sheet set's file.</param>
        /// <param name="sheet">The sheet.</param>
        /// <param name="options">What the caller gives the dialog; null for none.</param>
        /// <exception cref="ArgumentNullException">Thrown when the file or the sheet is null.</exception>
        public SheetSetRenameViewModel(
            CadSheetSetFile file,
            CadSheetSetSheet sheet,
            SheetSetDialogOptions options = null
        )
            : base(file, options)
        {
            if (sheet == null)
            {
                throw new ArgumentNullException(nameof(sheet));
            }

            Load(sheet);
        }

        /// <summary>Gets the dialog's title: "Rename &amp; Renumber Sheet".</summary>
        public override string Title => Titled("Rename & Renumber Sheet");

        /// <summary>Gets the ID of the sheet the dialog shows now.</summary>
        public string SheetId => _sheetId;

        /// <summary>Gets whether OK, Previous or Next has changed the set since the dialog opened.</summary>
        public bool Changed { get; private set; }

        /// <summary>Gets or sets the sheet number, free text; empty for none.</summary>
        public string Number
        {
            get => _number;
            set
            {
                if (Set(ref _number, value ?? string.Empty))
                {
                    Follow();
                }
            }
        }

        /// <summary>Gets or sets the sheet title.</summary>
        public string SheetTitle
        {
            get => _sheetTitle;
            set
            {
                if (Set(ref _sheetTitle, value ?? string.Empty))
                {
                    Follow();
                }
            }
        }

        /// <summary>Gets or sets the layout's name; it follows the title while its box is on.</summary>
        public string LayoutName
        {
            get => _layoutName;
            set
            {
                if (Set(ref _layoutName, value ?? string.Empty))
                {
                    Touch();
                }
            }
        }

        /// <summary>Gets or sets the file's name, without .dwg; it follows the title while its box is on.</summary>
        public string FileName
        {
            get => _fileName;
            set
            {
                if (Set(ref _fileName, value ?? string.Empty))
                {
                    Touch();
                }
            }
        }

        /// <summary>Gets the folder of the drawing (read-only; the file is renamed in its folder).</summary>
        public string FolderPath => _folderPath;

        /// <summary>Gets or sets whether the layout name follows the title.</summary>
        public bool RenameLayout
        {
            get => _renameLayout;
            set
            {
                if (Set(ref _renameLayout, value))
                {
                    Raise(nameof(CanEditPrefixLayout));
                    Follow();
                }
            }
        }

        /// <summary>Gets or sets whether the layout name starts with the sheet number.</summary>
        public bool PrefixLayoutWithNumber
        {
            get => _prefixLayout;
            set
            {
                if (Set(ref _prefixLayout, value))
                {
                    Follow();
                }
            }
        }

        /// <summary>Gets or sets whether the file name follows the title.</summary>
        public bool RenameFile
        {
            get => _renameFile;
            set
            {
                if (Set(ref _renameFile, value))
                {
                    Raise(nameof(CanEditPrefixFile));
                    Follow();
                }
            }
        }

        /// <summary>Gets or sets whether the file name starts with the sheet number.</summary>
        public bool PrefixFileWithNumber
        {
            get => _prefixFile;
            set
            {
                if (Set(ref _prefixFile, value))
                {
                    Follow();
                }
            }
        }

        /// <summary>Gets whether the layout's Prefix with sheet number box is live: its parent box is on.</summary>
        public bool CanEditPrefixLayout => _renameLayout;

        /// <summary>Gets whether the file's Prefix with sheet number box is live: its parent box is on.</summary>
        public bool CanEditPrefixFile => _renameFile;

        /// <summary>Gets whether a sheet comes before this one in the set.</summary>
        public bool HasPrevious => Neighbour(-1) != null;

        /// <summary>Gets whether a sheet comes after this one in the set.</summary>
        public bool HasNext => Neighbour(1) != null;

        /// <summary>Applies the changes, then shows the sheet before this one in the set.</summary>
        /// <returns>True when the previous sheet is shown; false where there is none or the changes were refused (the red line says why).</returns>
        public bool Previous() => Move(-1);

        /// <summary>Applies the changes, then shows the sheet after this one in the set.</summary>
        /// <returns>True when the next sheet is shown; false where there is none or the changes were refused (the red line says why).</returns>
        public bool Next() => Move(1);

        /// <inheritdoc />
        protected override string Validate()
        {
            if (LayoutChanges)
            {
                string reason = CadSheetSetDrawings.WhyNotLayoutName(_layoutName);
                if (reason != null)
                {
                    return reason;
                }
            }

            if (FileChanges)
            {
                string name = _fileName.Trim();
                if (name.Length == 0)
                {
                    return "A sheet needs a file name.";
                }

                if (name.IndexOfAny(Path.GetInvalidFileNameChars()) >= 0)
                {
                    return "The file name \"" + name + "\" holds a character a file cannot.";
                }

                string target = DwgName(name);
                if (
                    _currentDrawing != null
                    && !CadSheetSetDrawings.Same(Path.Combine(_folderPath, target), _currentDrawing)
                    && System.IO.File.Exists(Path.Combine(_folderPath, target))
                )
                {
                    return target + " already exists in this folder.";
                }
            }

            return null;
        }

        /// <inheritdoc />
        protected override void Save()
        {
            bool layout = LayoutChanges;
            bool renamed = FileChanges;
            string id = _sheetId;
            string number = _number;
            string title = _sheetTitle;
            string layoutName = layout ? _layoutName : null;
            string fileName = renamed ? _fileName.Trim() : null;
            CadSheetSetSheet sheet = File.SheetSet.Find(id) as CadSheetSetSheet;
            if (sheet != null && !layout && !renamed && sheet.Number == number && sheet.Title == title)
            {
                return;
            }

            var drawings = new List<string>();
            if ((layout || renamed) && _currentDrawing != null)
            {
                drawings.Add(_currentDrawing);
            }

            Commit(
                set => ((CadSheetSetSheet)set.Find(id)).RenameAndRenumber(number, title, layoutName, fileName),
                drawings,
                renamed ? id : null
            );
            Changed = true;
            Load((CadSheetSetSheet)File.SheetSet.Find(id));
        }

        private bool LayoutChanges =>
            !string.Equals(_layoutName, _currentLayout ?? string.Empty, StringComparison.Ordinal);

        private bool FileChanges =>
            _currentFile != null && !string.Equals(DwgName(_fileName.Trim()), _currentFile, StringComparison.Ordinal);

        private static string DwgName(string name) =>
            name.EndsWith(".dwg", StringComparison.OrdinalIgnoreCase) ? name : name + ".dwg";

        private CadSheetSetSheet Neighbour(int step)
        {
            List<CadSheetSetSheet> all = File.SheetSet.AllSheets().ToList();
            int at = all.FindIndex(s => s.Id == _sheetId);
            int to = at + step;
            return at < 0 || to < 0 || to >= all.Count ? null : all[to];
        }

        private bool Move(int step)
        {
            if (!Apply())
            {
                return false;
            }

            CadSheetSetSheet next = Neighbour(step);
            if (next == null)
            {
                return false;
            }

            Load(next);
            return true;
        }

        /// <summary>Shows a sheet: its number, title and the names its layout and file have now.</summary>
        private void Load(CadSheetSetSheet sheet)
        {
            _loading = true;
            try
            {
                _sheetId = sheet.Id;
                CadFoundLayout found = sheet.FindLayout();
                CadDrawingReference reference = sheet.Layout;
                _currentLayout = found.IsMissing ? reference?.Name : found.LayoutName;
                _currentDrawing = found.DrawingFile ?? reference?.File.Locate(File.Folder);
                _currentFile = _currentDrawing == null ? null : Path.GetFileName(_currentDrawing);
                _number = sheet.Number;
                _sheetTitle = sheet.Title;
                _layoutName = _currentLayout ?? string.Empty;
                _fileName = _currentFile == null ? string.Empty : Path.GetFileNameWithoutExtension(_currentFile);
                _folderPath = _currentDrawing == null ? File.Folder : Path.GetDirectoryName(_currentDrawing);
            }
            finally
            {
                _loading = false;
            }

            foreach (
                string name in new[]
                {
                    nameof(Number),
                    nameof(SheetTitle),
                    nameof(LayoutName),
                    nameof(FileName),
                    nameof(FolderPath),
                    nameof(HasPrevious),
                    nameof(HasNext),
                    nameof(Title),
                    nameof(SheetId),
                }
            )
            {
                Raise(name);
            }

            Touch();
        }

        /// <summary>The names that follow the number and the title do so.</summary>
        private void Follow()
        {
            if (_loading)
            {
                return;
            }

            if (_renameLayout)
            {
                string name = CadSheetSetSheet.DefaultLayoutName(_prefixLayout ? _number : string.Empty, _sheetTitle);
                if (!string.Equals(name, _layoutName, StringComparison.Ordinal))
                {
                    _layoutName = name;
                    Raise(nameof(LayoutName));
                }
            }

            if (_renameFile)
            {
                string name = CadSheetSetSheet.DefaultFileName(_prefixFile ? _number : string.Empty, _sheetTitle);
                if (!string.Equals(name, _fileName, StringComparison.Ordinal))
                {
                    _fileName = name;
                    Raise(nameof(FileName));
                }
            }

            Touch();
        }

        /// <summary>Says whether the drawing is open, and checks the values again.</summary>
        private void Touch()
        {
            if (_loading)
            {
                return;
            }

            Notice = null;
            if (IsOpenInSession(_currentDrawing) && (_renameFile || FileChanges || LayoutChanges))
            {
                Notice =
                    _renameFile || FileChanges
                        ? _currentFile
                            + " is open in this session. It is saved, renamed and reopened under the new name at OK."
                        : _currentFile + " is open in this session. It is saved and reopened at OK.";
            }

            Revalidate();
        }
    }
}
