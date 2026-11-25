using System;
using System.IO;
using System.Threading;
using OpenCAD2D.SheetSets;

namespace OpenCAD2D.Wpf
{
    /// <summary>
    /// AutoCAD 2021's Sheet Set PDF Publish Options over a sheet set (Sheet List, set menu, Publish): Location, Prompt for name,
    /// File name and Multi-sheet file, the Data options Include hyperlinks and Create bookmarks, and the Quality and Font
    /// handling rows that the presenter's PDF writer does always or never, greyed as Export to PDF Options greys them. OK saves
    /// Location, Prompt for name and Multi-sheet file in the sheet set's data file (<see cref="CadSheetSetPublishOptions"/>)
    /// and the others in the options the publish is made with.
    /// </summary>
    public sealed class SheetSetPublishOptionsViewModel : ObservableObject
    {
        private readonly CadSheetSetFile _file;
        private readonly CadPublishOptions _options;
        private readonly string _startLocation;
        private string _location;
        private bool _promptForName;
        private bool _multiSheet;
        private string _fileName;
        private bool _includeHyperlinks;
        private bool _createBookmarks;
        private string _message = string.Empty;

        /// <summary>Makes the dialog over a sheet set's file and the PDF options of its publishes.</summary>
        /// <param name="file">The sheet set's file; its publish options are changed on OK.</param>
        /// <param name="options">The PDF options the publish is made with (hyperlinks, bookmarks, file name), changed on OK; null for the start: both on and the file named by the set.</param>
        /// <exception cref="ArgumentNullException">Thrown when the file is null.</exception>
        public SheetSetPublishOptionsViewModel(CadSheetSetFile file, CadPublishOptions options)
        {
            _file = file ?? throw new ArgumentNullException(nameof(file));
            _options = options ?? new CadPublishOptions { CreateBookmarks = true, IncludeHyperlinks = true };
            CadSheetSet set = file.SheetSet;
            CadSheetSetPublishOptions saved = set.PublishOptions;
            SetName = set.Name;
            _startLocation = saved.OutputFolder?.Locate(file.Folder) ?? file.Folder;
            _location = _startLocation;
            _promptForName = saved.PromptForName;
            _multiSheet = saved.MultiSheet;
            _fileName = string.IsNullOrWhiteSpace(_options.FileName) ? set.Name : _options.FileName;
            _includeHyperlinks = _options.IncludeHyperlinks;
            _createBookmarks = _options.CreateBookmarks;
        }

        /// <summary>Gets the name of the sheet set.</summary>
        public string SetName { get; }

        /// <summary>Gets the PDF options the dialog edits, which OK has changed: the options to publish with from now on.</summary>
        public CadPublishOptions Options => _options;

        /// <summary>Gets the page of the guide the Help button opens.</summary>
        public string HelpUrl => PlotHelp.Publish;

        /// <summary>Gets or sets the folder the PDF files are saved in, AutoCAD's Location.</summary>
        public string Location
        {
            get => _location;
            set
            {
                if (Set(ref _location, value ?? string.Empty))
                {
                    Message = string.Empty;
                }
            }
        }

        /// <summary>Gets or sets whether the multi-sheet file's name is asked for when publishing, AutoCAD's Prompt for name.</summary>
        public bool PromptForName
        {
            get => _promptForName;
            set
            {
                if (Set(ref _promptForName, value))
                {
                    Raise(nameof(IsFileNameEnabled));
                }
            }
        }

        /// <summary>Gets or sets whether the sheets are one PDF of a page each, AutoCAD's Multi-sheet file; off for a PDF for each sheet.</summary>
        public bool MultiSheet
        {
            get => _multiSheet;
            set
            {
                if (Set(ref _multiSheet, value))
                {
                    Raise(nameof(IsFileNameEnabled));
                }
            }
        }

        /// <summary>Gets or sets the name of the multi-sheet PDF, AutoCAD's File name; the sheet set's name until changed.</summary>
        public string FileName
        {
            get => _fileName;
            set
            {
                if (Set(ref _fileName, value ?? string.Empty))
                {
                    Message = string.Empty;
                }
            }
        }

        /// <summary>Gets whether File name can be typed in: not with Prompt for name, and not with a PDF for each sheet, which the sheets name.</summary>
        public bool IsFileNameEnabled => _multiSheet && !_promptForName;

        /// <summary>Gets or sets whether the hyperlinks of the objects plotted become links of the PDF, AutoCAD's Include hyperlinks.</summary>
        public bool IncludeHyperlinks
        {
            get => _includeHyperlinks;
            set => Set(ref _includeHyperlinks, value);
        }

        /// <summary>Gets or sets whether each PDF carries its bookmarks, AutoCAD's Create bookmarks: <c>Sheets and Views</c> and a bookmark for each page.</summary>
        public bool CreateBookmarks
        {
            get => _createBookmarks;
            set => Set(ref _createBookmarks, value);
        }

        /// <summary>Gets what is wrong with the entries, in the dialog's red line; empty where nothing is.</summary>
        public string Message
        {
            get => _message;
            private set => Set(ref _message, value);
        }

        /// <summary>Gets or sets how Browse… asks for a folder: given the folder now in Location, gives the one chosen, or null for none. The window sets it; a test gives its own.</summary>
        public Func<string, string> AskFolder { get; set; }

        /// <summary>Browse…: asks for a folder and sets Location to it.</summary>
        public void BrowseLocation()
        {
            string folder = AskFolder?.Invoke(_location);

            if (!string.IsNullOrEmpty(folder))
            {
                Location = folder;
            }
        }

        /// <summary>
        /// OK: checks the entries and, where they are good, saves Location (as a reference to the folder against the .dst),
        /// Prompt for name and Multi-sheet file in the sheet set's data file, and the file name, Include hyperlinks and Create
        /// bookmarks in the options. A Location left as it was starting is not written to a set that names none.
        /// </summary>
        /// <returns>True where the options were saved; false where an entry is wrong, which <see cref="Message"/> says.</returns>
        public bool Accept()
        {
            string location = _location.Trim();
            string name = _fileName.Trim();
            string full;

            try
            {
                full = location.Length == 0 ? string.Empty : Path.GetFullPath(location);
            }
            catch (Exception exception)
                when (exception is ArgumentException
                    || exception is NotSupportedException
                    || exception is PathTooLongException
                )
            {
                full = string.Empty;
            }

            if (full.Length == 0)
            {
                Message = "Enter the folder the PDF files are saved in.";
                return false;
            }

            if (IsFileNameEnabled && (name.Length == 0 || name.IndexOfAny(Path.GetInvalidFileNameChars()) >= 0))
            {
                Message = "Enter a file name that holds none of " + "< > / \\ \" : ? * |.";
                return false;
            }

            bool moved = !string.Equals(
                full.TrimEnd('\\', '/'),
                Path.GetFullPath(_startLocation).TrimEnd('\\', '/'),
                StringComparison.OrdinalIgnoreCase
            );
            _file.Change(set =>
            {
                CadSheetSetPublishOptions saved = set.PublishOptions;

                if (moved)
                {
                    saved.OutputFolder = CadFileReference.FromPath(full, _file.Folder);
                }

                saved.MultiSheet = _multiSheet;
                saved.PromptForName = _promptForName;
            });
            _options.FileName = IsFileNameEnabled ? name : _options.FileName;
            _options.IncludeHyperlinks = _includeHyperlinks;
            _options.CreateBookmarks = _createBookmarks;
            return true;
        }
    }

    /// <summary>
    /// The window a sheet set publish shows while it runs (the board's Publishing state): a heading, the file written, a
    /// progress bar, the sheet being drawn as <c>Sheet 4 of 9: A-103 Roof Plan</c> and its percent, and Cancel, which stops the
    /// publish at the next sheet. Its logic is here; <see cref="SheetSetPublishProgressWindow"/> shows it.
    /// </summary>
    public sealed class SheetSetPublishProgressViewModel : ObservableObject
    {
        private readonly CancellationTokenSource _cancel = new CancellationTokenSource();
        private string _status = "Preparing the sheets";
        private double _percent;
        private bool _canceling;

        /// <summary>Makes the window's state at the start.</summary>
        /// <param name="setName">The name of the sheet set, which the title and the heading carry.</param>
        /// <param name="target">What is written: the PDF's path for one file, the folder for a file for each sheet.</param>
        /// <exception cref="ArgumentNullException">Thrown when an argument is null.</exception>
        public SheetSetPublishProgressViewModel(string setName, string target)
        {
            SetName = setName ?? throw new ArgumentNullException(nameof(setName));
            Target = target ?? throw new ArgumentNullException(nameof(target));
        }

        /// <summary>Gets the name of the sheet set.</summary>
        public string SetName { get; }

        /// <summary>Gets the window's title, <c>Publish - Example Tower</c>.</summary>
        public string Title => "Publish - " + SetName;

        /// <summary>Gets the heading, <c>Publishing Example Tower to PDF</c>.</summary>
        public string Heading => "Publishing " + SetName + " to PDF";

        /// <summary>Gets what is written: the PDF's path, or the folder of the files.</summary>
        public string Target { get; }

        /// <summary>Gets the line under the bar: which sheet is being drawn, or that the publish is being canceled.</summary>
        public string Status
        {
            get => _status;
            private set => Set(ref _status, value);
        }

        /// <summary>Gets how far the publish is, 0 to 100: the sheets finished of those to publish.</summary>
        public double Percent
        {
            get => _percent;
            private set
            {
                if (Set(ref _percent, value))
                {
                    Raise(nameof(PercentText));
                }
            }
        }

        /// <summary>Gets the percent as the window writes it, <c>33%</c>.</summary>
        public string PercentText =>
            Math.Floor(_percent).ToString("0", System.Globalization.CultureInfo.InvariantCulture) + "%";

        /// <summary>Gets whether Cancel can still be pressed: until it has been.</summary>
        public bool IsCancelEnabled => !_canceling;

        /// <summary>Gets whether Cancel was pressed.</summary>
        public bool IsCanceling => _canceling;

        /// <summary>Gets the token the publish asks before each sheet.</summary>
        public CancellationToken Token => _cancel.Token;

        /// <summary>Tells the window the sheet the publish is about to draw.</summary>
        /// <param name="progress">The publisher's report.</param>
        /// <exception cref="ArgumentNullException">Thrown when the report is null.</exception>
        public void Report(CadPublishProgress progress)
        {
            if (progress == null)
            {
                throw new ArgumentNullException(nameof(progress));
            }

            Percent = (progress.Sheet - 1) * 100.0 / Math.Max(1, progress.Count);

            if (!_canceling)
            {
                Status =
                    "Sheet "
                    + progress.Sheet.ToString(System.Globalization.CultureInfo.InvariantCulture)
                    + " of "
                    + progress.Count.ToString(System.Globalization.CultureInfo.InvariantCulture)
                    + ": "
                    + progress.Name;
            }
        }

        /// <summary>Cancel: asks the publish to stop at the next sheet.</summary>
        public void Cancel()
        {
            _canceling = true;
            Raise(nameof(IsCanceling));
            Raise(nameof(IsCancelEnabled));
            Status = "Canceling after the sheet being drawn";
            _cancel.Cancel();
        }
    }
}
