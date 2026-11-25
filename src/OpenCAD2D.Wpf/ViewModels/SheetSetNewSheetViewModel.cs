using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.IO;
using System.Linq;
using GeometryHelper.Dxf;
using OpenCAD2D.SheetSets;

namespace OpenCAD2D.Wpf
{
    /// <summary>What New Sheet made: the sheet, its drawing and layout, and whether the drawing is to be opened in the editor.</summary>
    public sealed class SheetSetNewSheetResult
    {
        internal SheetSetNewSheetResult(string sheetId, string drawingFile, string layoutName, bool openInEditor)
        {
            SheetId = sheetId;
            DrawingFile = drawingFile;
            LayoutName = layoutName;
            OpenInEditor = openInEditor;
        }

        /// <summary>Gets the <c>ID</c> of the new sheet in the set.</summary>
        public string SheetId { get; }

        /// <summary>Gets the full path of the new drawing.</summary>
        public string DrawingFile { get; }

        /// <summary>Gets the name of the sheet's layout in the drawing.</summary>
        public string LayoutName { get; }

        /// <summary>Gets whether Open in drawing editor was on: the caller opens the drawing with that layout current.</summary>
        public bool OpenInEditor { get; }
    }

    /// <summary>
    /// New Sheet, as the board A12NewSheet draws it: Number, Sheet title, File name (which follows "Number Title" until it is
    /// typed in), Folder path and Sheet template (read-only, from the subset, else the set), and Open in drawing editor. An
    /// empty title, a file name a file cannot have and a file that is there already are refused in the dialog's words with OK
    /// greyed; an existing drawing is never overwritten. Where Prompt for template is Yes the caller asks Select Layout as Sheet
    /// Template first and gives the layout chosen as <see cref="Template"/>. OK makes the drawing from the template layout,
    /// adds the sheet after the sheet the dialog was asked from (else at the end of the subset) and says what it made in
    /// <see cref="Created"/>.
    /// </summary>
    public sealed class SheetSetNewSheetViewModel : SheetSetDialogViewModel
    {
        private readonly string _parentId;
        private readonly string _afterId;
        private string _number = string.Empty;
        private string _sheetTitle = string.Empty;
        private string _fileName = string.Empty;
        private bool _fileTyped;
        private bool _openInEditor = true;
        private CadDrawingReference _template;

        /// <summary>Makes the dialog for a new sheet in a group.</summary>
        /// <param name="file">The sheet set's file.</param>
        /// <param name="parent">The group the sheet goes into (the sheet set itself or a subset of it).</param>
        /// <param name="after">The sheet the sheet goes after, in the same group; null for the end of the group.</param>
        /// <param name="options">What the caller gives the dialog; null for none.</param>
        /// <exception cref="ArgumentNullException">Thrown when the file or the group is null.</exception>
        public SheetSetNewSheetViewModel(
            CadSheetSetFile file,
            CadSheetSubset parent,
            CadSheetSetSheet after,
            SheetSetDialogOptions options = null
        )
            : base(file, options)
        {
            if (parent == null)
            {
                throw new ArgumentNullException(nameof(parent));
            }

            _parentId = parent.Id;
            _afterId = after?.Id;
            PromptsForTemplate = parent.PromptForTemplate;
            IEnumerable<CadSheetSubset> chain = Chain(parent);
            if (!PromptsForTemplate)
            {
                _template = chain.Select(s => s.Template).FirstOrDefault(t => t != null);
            }

            FolderPath = chain
                .Select(s => s.NewSheetLocation)
                .Where(l => l != null)
                .Select(l => SheetSetGridViewModel.PathText(l, File.Folder))
                .FirstOrDefault(p => p.Length > 0)
                ?? File.Folder;
            Revalidate();
        }

        /// <summary>Gets the dialog's title: "New Sheet".</summary>
        public override string Title => Titled("New Sheet");

        /// <summary>Gets whether the group's Prompt for template is Yes: the template is chosen first, and given as <see cref="Template"/>.</summary>
        public bool PromptsForTemplate { get; }

        /// <summary>Gets the folder the drawing is saved in (read-only): the group's New Sheet Location, else the nearest above it.</summary>
        public string FolderPath { get; }

        /// <summary>Gets or sets the template layout the drawing is made from; null for none.</summary>
        public CadDrawingReference Template
        {
            get => _template;
            set
            {
                if (Set(ref _template, value))
                {
                    Raise(nameof(SheetTemplate));
                    Revalidate();
                }
            }
        }

        /// <summary>Gets the template as the read-only box shows it: "A1 sheet (D:\Templates\OpenCAD2D-mm.dwt)"; "None" where there is none.</summary>
        public string SheetTemplate => SheetSetGridViewModel.DrawingText(_template, File.Folder);

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

        /// <summary>Gets or sets the sheet title, which must not be empty.</summary>
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

        /// <summary>Gets or sets the file name of the drawing, without .dwg: "Number Title" until it is typed in.</summary>
        public string FileName
        {
            get => _fileName;
            set
            {
                if (Set(ref _fileName, value ?? string.Empty))
                {
                    _fileTyped = !string.Equals(_fileName, DefaultName(), StringComparison.Ordinal);
                    Revalidate();
                }
            }
        }

        /// <summary>Gets or sets whether the drawing is opened in the editor once it is made.</summary>
        public bool OpenInEditor
        {
            get => _openInEditor;
            set => Set(ref _openInEditor, value);
        }

        /// <summary>Gets what OK made; null before it has.</summary>
        public SheetSetNewSheetResult Created { get; private set; }

        /// <inheritdoc />
        protected override bool IsComplete =>
            _sheetTitle.Trim().Length > 0 && _template != null && _fileName.Trim().Length > 0;

        /// <inheritdoc />
        protected override string Validate()
        {
            if (_template == null && !PromptsForTemplate)
            {
                return "There is no sheet creation template: set one in the Sheet Set Properties of the sheet set or the subset.";
            }

            string name = _fileName.Trim();
            if (name.Length == 0)
            {
                return null;
            }

            if (name.IndexOfAny(Path.GetInvalidFileNameChars()) >= 0)
            {
                return "The file name \"" + name + "\" holds a character a file cannot.";
            }

            string withExtension = name.EndsWith(".dwg", StringComparison.OrdinalIgnoreCase) ? name : name + ".dwg";
            return System.IO.File.Exists(Path.Combine(FolderPath, withExtension))
                ? withExtension
                    + " already exists in this folder. Choose another file name, or Import Layout as Sheet to use that drawing."
                : null;
        }

        /// <inheritdoc />
        protected override void Save()
        {
            string number = _number;
            string title = _sheetTitle.Trim();
            string name = _fileName.Trim();
            CadDrawingReference template = _template;
            string id = null;
            string drawing = null;
            string layout = null;
            Commit(set =>
            {
                var parent = (CadSheetSubset)set.Find(_parentId);
                int index = -1;
                if (_afterId != null)
                {
                    int at = parent.Children.ToList().FindIndex(c => c.Id == _afterId);
                    index = at < 0 ? -1 : at + 1;
                }

                CadSheetSetSheet sheet = parent.NewSheet(number, title, template, name, index);
                id = sheet.Id;
                drawing = sheet.Layout.File.Resolve(File.Folder);
                layout = sheet.Layout.Name;
            });
            Created = new SheetSetNewSheetResult(id, drawing, layout, _openInEditor);
        }

        private static IEnumerable<CadSheetSubset> Chain(CadSheetSubset group)
        {
            for (CadSheetSubset at = group; at != null; at = at.Parent)
            {
                yield return at;
            }
        }

        private string DefaultName() => CadSheetSetSheet.DefaultFileName(_number, _sheetTitle);

        /// <summary>The file name follows the number and the title where it was not typed in.</summary>
        private void Follow()
        {
            if (!_fileTyped)
            {
                string name = DefaultName();
                if (!string.Equals(_fileName, name, StringComparison.Ordinal))
                {
                    _fileName = name;
                    Raise(nameof(FileName));
                }
            }

            Revalidate();
        }
    }

    /// <summary>
    /// Select Layout as Sheet Template (and, in the same form, the block picker of Label block for views): a drawing template
    /// file name with a ... button, and the list of that drawing's layouts in tab order (or its blocks) to choose one from. A
    /// file that is not there or cannot be read is said so in red and the list is empty. OK gives the layout (or block) as a
    /// <see cref="Result"/> reference, with its handle, for the set to keep.
    /// </summary>
    public sealed class SheetSetTemplatePickViewModel : SheetSetDialogViewModel
    {
        private const string Filter =
            "Drawing templates (*.dwt)|*.dwt|Drawings (*.dwg;*.dxf)|*.dwg;*.dxf|All files (*.*)|*.*";

        private readonly bool _blocks;
        private readonly string _title;
        private readonly string _fileCaption;
        private readonly string _listCaption;
        private readonly Dictionary<string, string[]> _handles = new Dictionary<string, string[]>(
            StringComparer.Ordinal
        );
        private readonly ObservableCollection<string> _items = new ObservableCollection<string>();
        private string _fileName = string.Empty;
        private string _loadError;
        private string _selected;

        private SheetSetTemplatePickViewModel(
            CadSheetSetFile file,
            bool blocks,
            string title,
            string fileCaption,
            string listCaption,
            CadDrawingReference current,
            SheetSetDialogOptions options
        )
            : base(file, options)
        {
            _blocks = blocks;
            _title = title;
            _fileCaption = fileCaption;
            _listCaption = listCaption;
            if (current != null)
            {
                _fileName = SheetSetGridViewModel.PathText(current.File, File.Folder);
                Load();
                if (current.Name != null && _items.Contains(current.Name))
                {
                    SelectedItem = current.Name;
                }
            }
        }

        /// <summary>Makes the dialog that picks a layout of a drawing as a sheet template.</summary>
        /// <param name="file">The sheet set's file.</param>
        /// <param name="current">The layout now named, whose drawing is listed and which is chosen; null for none.</param>
        /// <param name="options">What the caller gives the dialog; null for none.</param>
        /// <returns>The dialog.</returns>
        /// <exception cref="ArgumentNullException">Thrown when the file is null.</exception>
        public static SheetSetTemplatePickViewModel ForLayouts(
            CadSheetSetFile file,
            CadDrawingReference current,
            SheetSetDialogOptions options = null
        ) =>
            new SheetSetTemplatePickViewModel(
                file,
                false,
                "Select Layout as Sheet Template",
                "Drawing template file name:",
                "Select a layout to create new sheets:",
                current,
                options
            );

        /// <summary>Makes the dialog that picks the layout a sheet is to be (the ... button of Expected layout in the Sheet Properties): a drawing and a layout in it.</summary>
        /// <param name="file">The sheet set's file.</param>
        /// <param name="current">The layout now named, whose drawing is listed and which is chosen; null for none.</param>
        /// <param name="options">What the caller gives the dialog; null for none.</param>
        /// <returns>The dialog.</returns>
        /// <exception cref="ArgumentNullException">Thrown when the file is null.</exception>
        public static SheetSetTemplatePickViewModel ForExpectedLayout(
            CadSheetSetFile file,
            CadDrawingReference current,
            SheetSetDialogOptions options = null
        ) =>
            new SheetSetTemplatePickViewModel(
                file,
                false,
                "Select Expected Layout",
                "Drawing file name:",
                "Select the layout the sheet is:",
                current,
                options
            );

        /// <summary>Makes the dialog that picks a block of a drawing as the label block for views.</summary>
        /// <param name="file">The sheet set's file.</param>
        /// <param name="current">The block now named, whose drawing is listed and which is chosen; null for none.</param>
        /// <param name="options">What the caller gives the dialog; null for none.</param>
        /// <returns>The dialog.</returns>
        /// <exception cref="ArgumentNullException">Thrown when the file is null.</exception>
        public static SheetSetTemplatePickViewModel ForBlocks(
            CadSheetSetFile file,
            CadDrawingReference current,
            SheetSetDialogOptions options = null
        ) =>
            new SheetSetTemplatePickViewModel(
                file,
                true,
                "Select Block as Label Block",
                "Drawing file name:",
                "Select a block:",
                current,
                options
            );

        /// <summary>Gets the dialog's title.</summary>
        public override string Title => _title;

        /// <summary>Gets the caption of the file box.</summary>
        public string FileCaption => _fileCaption;

        /// <summary>Gets the caption of the list.</summary>
        public string ListCaption => _listCaption;

        /// <summary>Gets or sets the file name; the list is made again from the file each time it is set.</summary>
        public string FileName
        {
            get => _fileName;
            set
            {
                if (Set(ref _fileName, value ?? string.Empty))
                {
                    Load();
                }
            }
        }

        /// <summary>Gets the names the drawing offers, in tab order for layouts and in name order for blocks.</summary>
        public IReadOnlyList<string> Items => _items;

        /// <summary>Gets or sets the name chosen in the list.</summary>
        public string SelectedItem
        {
            get => _selected;
            set
            {
                if (Set(ref _selected, value))
                {
                    Raise(nameof(CanApply));
                }
            }
        }

        /// <summary>Gets the layout or block OK chose; null before it did.</summary>
        public CadDrawingReference Result { get; private set; }

        /// <inheritdoc />
        protected override bool IsComplete => _selected != null;

        /// <summary>Runs the ... button: asks for a file and, where one is chosen, makes it the file name.</summary>
        /// <returns>True when a file was chosen.</returns>
        public bool Browse()
        {
            string file = BrowseFile?.Invoke(Title, Filter, _fileName);
            if (file == null)
            {
                return false;
            }

            FileName = file;
            return true;
        }

        /// <inheritdoc />
        protected override string Validate() => _loadError;

        /// <inheritdoc />
        protected override void Save()
        {
            string path = System.IO.Path.GetFullPath(_fileName.Trim());
            string[] handles = _handles[_selected];
            Result = new CadDrawingReference(
                CadFileReference.FromPath(path, File.Folder),
                _selected,
                handles[0],
                handles[1]
            );
        }

        private void Load()
        {
            _items.Clear();
            _handles.Clear();
            _loadError = null;
            SelectedItem = null;
            string text = _fileName.Trim();
            if (text.Length > 0)
            {
                try
                {
                    string path = System.IO.Path.GetFullPath(text);
                    if (!System.IO.File.Exists(path))
                    {
                        _loadError = "The file " + path + " is not there.";
                    }
                    else
                    {
                        Read(CadDrawingFile.Open(path));
                    }
                }
                catch (Exception e)
                    when (e is ArgumentException
                        || e is IOException
                        || e is NotSupportedException
                        || e is FormatException
                        || e is UnauthorizedAccessException
                        || e is InvalidOperationException
                    )
                {
                    _loadError = "The file " + text + " cannot be read: " + e.Message;
                }
            }

            SelectedItem = _items.FirstOrDefault();
            Revalidate();
        }

        private void Read(DxfDatabase database)
        {
            if (_blocks)
            {
                foreach (
                    DxfBlockTableRecord block in database
                        .BlockTable.Where(b =>
                            !b.IsLayout && !b.IsAnonymous && !b.IsFromExternalReference && !string.IsNullOrEmpty(b.Name)
                        )
                        .OrderBy(b => b.Name, StringComparer.Ordinal)
                )
                {
                    if (!_handles.ContainsKey(block.Name))
                    {
                        _items.Add(block.Name);
                        _handles[block.Name] = new[] { block.Handle, null };
                    }
                }

                return;
            }

            foreach (DxfLayout layout in CadSheetSetDrawings.SheetLayouts(database))
            {
                if (!_handles.ContainsKey(layout.LayoutName))
                {
                    _items.Add(layout.LayoutName);
                    _handles[layout.LayoutName] = new[] { layout.Handle, layout.OwnerHandle };
                }
            }
        }
    }
}
