using System;
using OpenCAD2D.SheetSets;

namespace OpenCAD2D.Wpf
{
    /// <summary>
    /// The Import Options dialog the Choose Layouts page of the Create Sheet Set wizard opens: "Prefix sheet titles with file
    /// name", "Create subsets based on folder structure" and, under it, "Ignore top level folder", which counts only where
    /// subsets are made. OK writes the three into the <see cref="CadSheetImportOptions"/> the dialog was opened on.
    /// </summary>
    public sealed class SheetImportOptionsViewModel : ObservableObject
    {
        private const string Help =
            "https://nguyenthanguth.github.io/opencad2d/guides/presenter/sheet-sets.html#the-create-sheet-set-wizard";

        private readonly CadSheetImportOptions _options;
        private bool _prefixTitles;
        private bool _subsets;
        private bool _ignoreTop;

        /// <summary>Makes the dialog over the options it starts from and changes on OK.</summary>
        /// <param name="options">The options.</param>
        /// <exception cref="ArgumentNullException">Thrown when the options are null.</exception>
        public SheetImportOptionsViewModel(CadSheetImportOptions options)
        {
            _options = options ?? throw new ArgumentNullException(nameof(options));
            _prefixTitles = options.PrefixTitleWithFileName;
            _subsets = options.CreateSubsetsFromFolders;
            _ignoreTop = options.IgnoreTopLevelFolder;
            OkCommand = new DelegateCommand(_ => Ok());
            CancelCommand = new DelegateCommand(_ => CloseRequested?.Invoke(this, false));
            HelpCommand = new DelegateCommand(_ => PlotHelp.Open(Help));
        }

        /// <summary>Raised when the dialog is to close: true for OK, false for Cancel.</summary>
        public event EventHandler<bool> CloseRequested;

        /// <summary>Gets the command of OK.</summary>
        public DelegateCommand OkCommand { get; }

        /// <summary>Gets the command of Cancel.</summary>
        public DelegateCommand CancelCommand { get; }

        /// <summary>Gets the command of Help, which opens the guide at the wizard's heading.</summary>
        public DelegateCommand HelpCommand { get; }

        /// <summary>Gets or sets whether a sheet's title is the drawing's file name, a hyphen and the layout's name.</summary>
        public bool PrefixTitleWithFileName
        {
            get => _prefixTitles;
            set => Set(ref _prefixTitles, value);
        }

        /// <summary>Gets or sets whether a subset is made for each folder the drawings lie in.</summary>
        public bool CreateSubsetsFromFolders
        {
            get => _subsets;
            set
            {
                if (Set(ref _subsets, value))
                {
                    Raise(nameof(CanIgnoreTopLevelFolder));
                }
            }
        }

        /// <summary>Gets or sets whether the folder that was picked makes no subset of its own.</summary>
        public bool IgnoreTopLevelFolder
        {
            get => _ignoreTop;
            set => Set(ref _ignoreTop, value);
        }

        /// <summary>Gets whether "Ignore top level folder" can be changed: it counts only while subsets are made from folders.</summary>
        public bool CanIgnoreTopLevelFolder => _subsets;

        /// <summary>The words under the boxes, as the design has them.</summary>
        public string Note =>
            "Sheet titles become \"file name - layout name\", as in AutoCAD; the number is left empty, to fill with Rename & Renumber.";

        /// <summary>OK: the three choices are written into the options the dialog was made over, and the dialog closes.</summary>
        public void Ok()
        {
            _options.PrefixTitleWithFileName = _prefixTitles;
            _options.CreateSubsetsFromFolders = _subsets;
            _options.IgnoreTopLevelFolder = _ignoreTop;
            CloseRequested?.Invoke(this, true);
        }
    }
}
