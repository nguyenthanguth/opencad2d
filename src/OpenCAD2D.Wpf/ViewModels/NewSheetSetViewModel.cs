using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.IO;
using System.Linq;
using System.Text;
using OpenCAD2D.SheetSets;

namespace OpenCAD2D.Wpf
{
    /// <summary>The pages of the Create Sheet Set wizard (NEWSHEETSET); each route shows four of the five.</summary>
    public enum NewSheetSetPage
    {
        /// <summary>Begin: "Create a sheet set using" an example sheet set or existing drawings.</summary>
        Begin = 0,

        /// <summary>Sheet Set Example (the example route): the example sheet set whose organisation the new set copies.</summary>
        Example = 1,

        /// <summary>Sheet Set Details: the name, the description and where the .dst is stored.</summary>
        Details = 2,

        /// <summary>Choose Layouts (the existing drawings route): the folders, drawings and layouts that become sheets.</summary>
        ChooseLayouts = 3,

        /// <summary>Confirm: the Sheet Set Preview.</summary>
        Confirm = 4,
    }

    /// <summary>One page of the list down the left of the wizard: its name, and whether the wizard is at it.</summary>
    public sealed class NewSheetSetPageRow
    {
        internal NewSheetSetPageRow(NewSheetSetPage page, string title, bool isCurrent)
        {
            Page = page;
            Title = title;
            IsCurrent = isCurrent;
        }

        /// <summary>Gets the page.</summary>
        public NewSheetSetPage Page { get; }

        /// <summary>Gets the page's name, as the title of the window gives it after "Create Sheet Set - ".</summary>
        public string Title { get; }

        /// <summary>Gets whether the wizard is at this page.</summary>
        public bool IsCurrent { get; }
    }

    /// <summary>An example sheet set listed on the Sheet Set Example page: a .dst in the template folder.</summary>
    public sealed class NewSheetSetExampleRow
    {
        internal NewSheetSetExampleRow(string path)
        {
            Path = path;
            Name = System.IO.Path.GetFileNameWithoutExtension(path);
        }

        /// <summary>Gets the name of the file without its extension, as the list shows it.</summary>
        public string Name { get; }

        /// <summary>Gets the full path of the .dst.</summary>
        public string Path { get; }
    }

    /// <summary>
    /// The Create Sheet Set wizard of NEWSHEETSET, in the pattern of the Data Extraction wizard and as AutoCAD 2021's: Begin
    /// ("Create a sheet set using" an example sheet set or existing drawings), then by the example route Sheet Set Example,
    /// Sheet Set Details and Confirm, and by the existing drawings route Sheet Set Details, Choose Layouts (with Import
    /// Options) and Confirm. Nothing is written before Finish: a Back, a Cancel and a closed window leave no file and no
    /// folder behind. Finish makes the set through <see cref="CadSheetSetFile.CreateFromExample(string, string, string, string, bool)"/>
    /// or <see cref="CadSheetSetFile.CreateFromDrawings(string, string, string, IEnumerable{CadSheetImportSource}, CadSheetImportOptions)"/>,
    /// opens it in the Sheet Set Manager (which puts it first in Recent) and keeps the route, the folder and the import options
    /// in the settings for the next time.
    /// </summary>
    /// <remarks>
    /// <para>
    /// Sheet Set Properties… of the Details page edits a draft of the set, a .dst in a folder of its own under the user's
    /// temporary folder that is deleted again on Finish, Cancel and <see cref="Dispose"/>: <see cref="AskProperties"/> is given
    /// the draft and edits it as it edits a set (a dialog on a <see cref="CadSheetSetFile"/>). On Finish the properties of the
    /// draft are written into the set made: the project strings, the template, the label block, the page setup overrides,
    /// the sheet location where it is not the draft's own folder, and the custom properties. Changing the route or the
    /// example drops the draft.
    /// </para>
    /// </remarks>
    public sealed partial class NewSheetSetViewModel : ObservableObject, IDisposable
    {
        private const string Help =
            "https://nguyenthanguth.github.io/opencad2d/guides/presenter/sheet-sets.html#the-create-sheet-set-wizard";

        private readonly MainViewModel _main;
        private readonly string _exampleFolder;
        private readonly CadSheetImportOptions _importOptions;
        private NewSheetSetPage _page = NewSheetSetPage.Begin;
        private bool _fromDrawings;
        private string _name;
        private string _description = string.Empty;
        private string _folder;
        private bool _createFolderHierarchy;
        private string _problem = string.Empty;
        private string _notice;
        private string _previewTitle = string.Empty;
        private string _previewBody = string.Empty;
        private CadSheetSetFile _draft;
        private CadSheetSetFile _example;
        private string _exampleProblem;
        private bool _browsedExample;
        private NewSheetSetExampleRow _selectedExample;
        private string _browsedPath = string.Empty;
        private string _exampleDetails = string.Empty;
        private string _exampleTitle = string.Empty;
        private bool _finished;

        /// <summary>Makes the wizard over the window's view model, at Begin, with the route, the folder and the import options the settings keep.</summary>
        /// <param name="main">The window's view model, whose Sheet Set Manager opens the set made and whose settings the wizard reads and keeps.</param>
        /// <param name="exampleFolder">The folder whose .dst files the Sheet Set Example page lists; null for the examples shipped with the program.</param>
        /// <exception cref="ArgumentNullException">Thrown when the model is null.</exception>
        public NewSheetSetViewModel(MainViewModel main, string exampleFolder = null)
        {
            _main = main ?? throw new ArgumentNullException(nameof(main));
            _exampleFolder = exampleFolder;
            GlobalSettings settings = main.Settings;
            _fromDrawings = settings.NewSheetSetFromDrawings;
            _importOptions = new CadSheetImportOptions
            {
                PrefixTitleWithFileName = settings.SheetImportPrefixTitles,
                CreateSubsetsFromFolders = settings.SheetImportSubsetsFromFolders,
                IgnoreTopLevelFolder = settings.SheetImportIgnoreTopFolder,
            };
            _folder = main.SheetSetStoreFolder;
            _name = FreeName();
            BackCommand = new DelegateCommand(_ => Back(), _ => CanBack);
            NextCommand = new DelegateCommand(_ => Next(), _ => CanNext);
            FinishCommand = new DelegateCommand(_ => Finish(), _ => CanFinish);
            CancelCommand = new DelegateCommand(_ => Cancel());
            BrowseExampleCommand = new DelegateCommand(_ => BrowseExample());
            BrowseFolderCommand = new DelegateCommand(_ => BrowseStorage());
            PropertiesCommand = new DelegateCommand(_ => OpenProperties());
            AddFolderCommand = new DelegateCommand(_ => AddFolder());
            ImportOptionsCommand = new DelegateCommand(_ => OpenImportOptions());
            LoadExamples();
            Update();
        }

        /// <summary>Raised when the wizard is to close: true for Finish, false for Cancel.</summary>
        public event EventHandler<bool> CloseRequested;

        /// <summary>Gets or sets what asks for a sheet set to use as an example, given the one named now; null where none is chosen. Set by the window.</summary>
        public Func<string, string> AskExample { get; set; }

        /// <summary>Gets or sets what asks for the folder the .dst is stored in, given the one named now; null where none is chosen. Set by the window.</summary>
        public Func<string, string> AskStorageFolder { get; set; }

        /// <summary>Gets or sets what asks for a folder of drawings to add to Choose Layouts; null where none is chosen. Set by the window.</summary>
        public Func<string> AskDrawingFolder { get; set; }

        /// <summary>Gets or sets how the Import Options dialog is shown over its model: true where OK was pressed. Set by the window.</summary>
        public Func<SheetImportOptionsViewModel, bool> AskImportOptions { get; set; }

        /// <summary>
        /// Gets or sets how Sheet Set Properties… is shown over the draft of the set being made: true where the dialog was
        /// closed with OK. The dialog edits the draft as it edits any set (name and data file read-only). Null where there
        /// is no such dialog, and then the button says so on the wizard's line. Set by the window.
        /// </summary>
        public Func<CadSheetSetFile, bool> AskProperties { get; set; }

        /// <summary>Gets the command of Back.</summary>
        public DelegateCommand BackCommand { get; }

        /// <summary>Gets the command of Next.</summary>
        public DelegateCommand NextCommand { get; }

        /// <summary>Gets the command of Finish.</summary>
        public DelegateCommand FinishCommand { get; }

        /// <summary>Gets the command of Cancel, which the cross of the title also runs.</summary>
        public DelegateCommand CancelCommand { get; }

        /// <summary>Gets the command of the button by "Browse to another sheet set to use as an example".</summary>
        public DelegateCommand BrowseExampleCommand { get; }

        /// <summary>Gets the command of the button by the folder the .dst is stored in.</summary>
        public DelegateCommand BrowseFolderCommand { get; }

        /// <summary>Gets the command of Sheet Set Properties….</summary>
        public DelegateCommand PropertiesCommand { get; }

        /// <summary>Gets the command of Browse… on the Choose Layouts page.</summary>
        public DelegateCommand AddFolderCommand { get; }

        /// <summary>Gets the command of Import Options… on the Choose Layouts page.</summary>
        public DelegateCommand ImportOptionsCommand { get; }

        /// <summary>Gets the full path of the .dst Finish made; null until it has.</summary>
        public string CreatedFile { get; private set; }

        /// <summary>Gets the address of the guide's page on the wizard, for a Help button.</summary>
        public string HelpUrl => Help;

        // ---- The pages

        /// <summary>Gets the page the wizard is at.</summary>
        public NewSheetSetPage Page => _page;

        /// <summary>Gets whether the wizard is at Begin.</summary>
        public bool IsBeginPage => _page == NewSheetSetPage.Begin;

        /// <summary>Gets whether the wizard is at Sheet Set Example.</summary>
        public bool IsExamplePage => _page == NewSheetSetPage.Example;

        /// <summary>Gets whether the wizard is at Sheet Set Details.</summary>
        public bool IsDetailsPage => _page == NewSheetSetPage.Details;

        /// <summary>Gets whether the wizard is at Choose Layouts.</summary>
        public bool IsLayoutsPage => _page == NewSheetSetPage.ChooseLayouts;

        /// <summary>Gets whether the wizard is at Confirm.</summary>
        public bool IsConfirmPage => _page == NewSheetSetPage.Confirm;

        /// <summary>Gets the pages of the route chosen, in order, for the list down the left.</summary>
        public IReadOnlyList<NewSheetSetPageRow> Pages =>
            Route().Select(p => new NewSheetSetPageRow(p, PageName(p), p == _page)).ToList();

        /// <summary>Gets the title of the window: "Create Sheet Set - " and the page's name.</summary>
        public string PageTitle => "Create Sheet Set - " + PageName(_page);

        /// <summary>Gets whether Back can be pressed: not on the first page.</summary>
        public bool CanBack => _page != NewSheetSetPage.Begin;

        /// <summary>Gets whether Next can be pressed: not on Confirm, and not while the line of the page is red.</summary>
        public bool CanNext => _page != NewSheetSetPage.Confirm && _problem.Length == 0;

        /// <summary>Gets whether Finish can be pressed: on Confirm only.</summary>
        public bool CanFinish => _page == NewSheetSetPage.Confirm && !_finished;

        /// <summary>
        /// Gets the red line at the foot of the page: what is wrong with it, in the wizard's words, which keeps Next grey; or
        /// what Finish was refused for; or what a button said (Sheet Set Properties… not built). Empty where there is nothing.
        /// </summary>
        public string Message => _notice ?? _problem;

        /// <summary>Gets whether <see cref="Message"/> says anything.</summary>
        public bool HasMessage => Message.Length > 0;

        /// <summary>Gets whether the sheet set is made from existing drawings (the second route) and not from an example.</summary>
        public bool IsFromDrawings => _fromDrawings;

        /// <summary>Gets or sets whether the sheet set is made from an example sheet set (the first route, the default).</summary>
        public bool IsFromExample
        {
            get => !_fromDrawings;
            set
            {
                if (value)
                {
                    ChooseRoute(false);
                }
            }
        }

        /// <summary>Gets or sets whether the sheet set is made from existing drawings (the second route).</summary>
        public bool IsExistingDrawings
        {
            get => _fromDrawings;
            set
            {
                if (value)
                {
                    ChooseRoute(true);
                }
            }
        }

        /// <summary>Goes to the next page of the route.</summary>
        public void Next()
        {
            if (!CanNext)
            {
                return;
            }

            GoTo(Route().SkipWhile(p => p != _page).Skip(1).First());
        }

        /// <summary>Goes back to the page before.</summary>
        public void Back()
        {
            if (!CanBack)
            {
                return;
            }

            GoTo(Route().TakeWhile(p => p != _page).Last());
        }

        /// <summary>Closes the wizard with nothing written and the draft of the properties deleted.</summary>
        public void Cancel()
        {
            DiscardDraft();
            CloseRequested?.Invoke(this, false);
        }

        /// <summary>Deletes the draft of the properties where there is one.</summary>
        public void Dispose() => DiscardDraft();

        private IReadOnlyList<NewSheetSetPage> Route() =>
            _fromDrawings
                ? new[]
                {
                    NewSheetSetPage.Begin,
                    NewSheetSetPage.Details,
                    NewSheetSetPage.ChooseLayouts,
                    NewSheetSetPage.Confirm,
                }
                : new[]
                {
                    NewSheetSetPage.Begin,
                    NewSheetSetPage.Example,
                    NewSheetSetPage.Details,
                    NewSheetSetPage.Confirm,
                };

        private static string PageName(NewSheetSetPage page)
        {
            switch (page)
            {
                case NewSheetSetPage.Begin:
                    return "Begin";
                case NewSheetSetPage.Example:
                    return "Sheet Set Example";
                case NewSheetSetPage.Details:
                    return "Sheet Set Details";
                case NewSheetSetPage.ChooseLayouts:
                    return "Choose Layouts";
                default:
                    return "Confirm";
            }
        }

        private void ChooseRoute(bool fromDrawings)
        {
            if (_fromDrawings == fromDrawings)
            {
                return;
            }

            _fromDrawings = fromDrawings;
            DiscardDraft();
            Raise(nameof(IsFromExample));
            Raise(nameof(IsExistingDrawings));
            Raise(nameof(IsFromDrawings));
            Raise(nameof(ShowFolderHierarchy));
            Raise(nameof(Pages));
            Update();
        }

        private void GoTo(NewSheetSetPage page)
        {
            _page = page;
            _notice = null;

            if (page == NewSheetSetPage.Confirm)
            {
                BuildPreview();
            }

            Raise(nameof(Page));
            Raise(nameof(IsBeginPage));
            Raise(nameof(IsExamplePage));
            Raise(nameof(IsDetailsPage));
            Raise(nameof(IsLayoutsPage));
            Raise(nameof(IsConfirmPage));
            Raise(nameof(Pages));
            Raise(nameof(PageTitle));
            Update();
        }

        /// <summary>Works out the line of the page and tells the buttons and the window what changed.</summary>
        private void Update()
        {
            _problem = ProblemOfPage();
            RaiseMessage();
            Raise(nameof(CanBack));
            Raise(nameof(CanNext));
            Raise(nameof(CanFinish));
            BackCommand.RaiseCanExecuteChanged();
            NextCommand.RaiseCanExecuteChanged();
            FinishCommand.RaiseCanExecuteChanged();
        }

        /// <summary>What is wrong with the page the wizard is at, as the wizard says it; empty where nothing is.</summary>
        private string ProblemOfPage()
        {
            switch (_page)
            {
                case NewSheetSetPage.Example:
                    return _exampleProblem ?? string.Empty;
                case NewSheetSetPage.Details:
                    return DetailsProblem() ?? string.Empty;
                case NewSheetSetPage.ChooseLayouts:
                    return LayoutsProblem() ?? string.Empty;
                default:
                    return string.Empty;
            }
        }

        private void RaiseMessage()
        {
            Raise(nameof(Message));
            Raise(nameof(HasMessage));
        }

        /// <summary>A change on the page: the red line of a refusal or a button is dropped, the page looked at again.</summary>
        private void Changed()
        {
            _notice = null;
            Update();
        }

        private static bool IsProblem(Exception ex) =>
            ex is IOException
            || ex is UnauthorizedAccessException
            || ex is FormatException
            || ex is ArgumentException
            || ex is NotSupportedException
            || ex is InvalidOperationException
            || ex is System.Xml.XmlException;
    }
}
