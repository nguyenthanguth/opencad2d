using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Globalization;
using System.Linq;
using System.Windows.Input;
using GeometryHelper.Geometry;

namespace OpenCAD2D.Wpf
{
    /// <summary>One row of the Named UCSs list of the UCS dialog: its icon, its name (edited in place by Rename) and the black triangle of the current system.</summary>
    public sealed class UcsRowViewModel : ObservableObject
    {
        private string _editText = string.Empty;
        private bool _isEditing;

        internal UcsRowViewModel(CadUcsDialogRow row)
        {
            Row = row;
        }

        /// <summary>Gets the row of the request.</summary>
        public CadUcsDialogRow Row { get; }

        /// <summary>Gets the name the list shows.</summary>
        public string Name => Row.Name;

        /// <summary>Gets the black triangle of the current system, empty for the other rows.</summary>
        public string Mark => Row.IsCurrent ? "▶" : string.Empty;

        /// <summary>Gets whether the row can be renamed (the menu's Rename is grey otherwise).</summary>
        public bool CanRename => Row.CanRename;

        /// <summary>Gets whether the row can be deleted (the menu's Delete is grey otherwise).</summary>
        public bool CanDelete => Row.CanDelete;

        /// <summary>Gets the drawing of the icon: the UCS icon for Unnamed, the globe for World, the return arrow for Previous; empty for a named system.</summary>
        public string Icon
        {
            get
            {
                switch (Row.Kind)
                {
                    case CadUcsRowKind.Unnamed:
                        return "M5 19V5M5 19h14M5 5l-2 3M5 5l2 3M19 19l-3-2M19 19l-3 2";
                    case CadUcsRowKind.World:
                        return "M12 3a9 9 0 1 0 0 18 9 9 0 0 0 0-18zM3 12h18M12 3c3 3 3 15 0 18M12 3c-3 3-3 15 0 18";
                    case CadUcsRowKind.Previous:
                        return "M9 14l-5-5 5-5M4 9h10a6 6 0 0 1 0 12h-3";
                    default:
                        return string.Empty;
                }
            }
        }

        /// <summary>Gets or sets whether the name is being edited in place.</summary>
        public bool IsEditing
        {
            get => _isEditing;
            set => Set(ref _isEditing, value);
        }

        /// <summary>Gets or sets the text of the name being edited.</summary>
        public string EditText
        {
            get => _editText;
            set => Set(ref _editText, value ?? string.Empty);
        }

        internal void Refresh()
        {
            Raise(nameof(Name));
            Raise(nameof(Mark));
            Raise(nameof(CanRename));
            Raise(nameof(CanDelete));
            Raise(nameof(Icon));
        }
    }

    /// <summary>One row of the Orthographic UCSs tab: the cube with the face drawn on it, the name and the depth to four places.</summary>
    public sealed class UcsOrthoRowViewModel : ObservableObject
    {
        internal UcsOrthoRowViewModel(CadUcsOrthoRow row)
        {
            Row = row;
        }

        /// <summary>Gets the row of the request.</summary>
        public CadUcsOrthoRow Row { get; }

        /// <summary>Gets the name: Top, Bottom, Front, Back, Left or Right.</summary>
        public string Name => Row.Name;

        /// <summary>Gets the depth with four decimals, as <c>0.0000</c>.</summary>
        public string DepthText => UcsNumbers.Format(Row.Depth);

        /// <summary>Gets the drawing of the face filled on the cube.</summary>
        public string Face
        {
            get
            {
                switch (Row.Name)
                {
                    case "Top":
                        return "M12 3l8 4.5-8 4.5-8-4.5z";
                    case "Bottom":
                    case "Back":
                    case "Right":
                        return "M12 12l8-4.5v9L12 21z";
                    default:
                        return "M4 7.5l8 4.5v9l-8-4.5z";
                }
            }
        }

        internal void Refresh() => Raise(nameof(DepthText));
    }

    /// <summary>The numbers of the UCS dialogs, as AutoCAD writes them: four decimals, and no minus sign on a nought.</summary>
    internal static class UcsNumbers
    {
        internal static string Format(double value) =>
            (Math.Round(value, 4, MidpointRounding.AwayFromZero) + 0.0).ToString(
                "0.0000",
                CultureInfo.InvariantCulture
            );
    }

    /// <summary>The three numbers of one group of the Details dialog (Origin, X Axis, Y Axis, ZAxis), each to four places.</summary>
    public sealed class UcsVectorText
    {
        internal UcsVectorText(string title, double x, double y, double z)
        {
            Title = title;
            X = UcsNumbers.Format(x);
            Y = UcsNumbers.Format(y);
            Z = UcsNumbers.Format(z);
        }

        /// <summary>Gets the title of the group: <c>Origin</c>, <c>X Axis</c>, <c>Y Axis</c> or <c>ZAxis</c>, as AutoCAD spells them.</summary>
        public string Title { get; }

        /// <summary>Gets the X number, as <c>35.3553</c>.</summary>
        public string X { get; }

        /// <summary>Gets the Y number.</summary>
        public string Y { get; }

        /// <summary>Gets the Z number.</summary>
        public string Z { get; }
    }

    /// <summary>
    /// AutoCAD's UCS Details dialog: a system's name, origin and three axes to four places, relative to World, Unnamed or a
    /// named system chosen in Relative to. Read only; OK closes it.
    /// </summary>
    public sealed class UcsDetailsViewModel : ObservableObject
    {
        private readonly Func<CadUcsDialogRow, CadUcsDetails> _describe;
        private CadUcsDialogRow _relativeTo;
        private IReadOnlyList<UcsVectorText> _groups;
        private string _name;

        internal UcsDetailsViewModel(CadUcsDialogRequest request, Func<CadUcsDialogRow, CadUcsDetails> describe)
        {
            _describe = describe;
            RelativeToChoices = request.RelativeToRows;
            _relativeTo = request.World;
            Recompute();
        }

        /// <summary>Gets the systems Relative to offers: Unnamed where there is one, World, then the named systems.</summary>
        public IReadOnlyList<CadUcsDialogRow> RelativeToChoices { get; }

        /// <summary>Gets or sets the system the numbers are relative to: World until chosen.</summary>
        public CadUcsDialogRow RelativeTo
        {
            get => _relativeTo;
            set
            {
                if (value != null && Set(ref _relativeTo, value))
                {
                    Recompute();
                }
            }
        }

        /// <summary>Gets the name of the system, as <c>Org100</c>.</summary>
        public string Name => _name;

        /// <summary>Gets the line <c>Name:  Org100</c>.</summary>
        public string NameLine => "Name:  " + _name;

        /// <summary>Gets the four groups: Origin, X Axis, Y Axis and ZAxis.</summary>
        public IReadOnlyList<UcsVectorText> Groups => _groups;

        private void Recompute()
        {
            CadUcsDetails details = _describe(_relativeTo);
            _name = details.Name;
            _groups = new[]
            {
                new UcsVectorText("Origin", details.Origin.X, details.Origin.Y, details.Origin.Z),
                new UcsVectorText("X Axis", details.XAxis.X, details.XAxis.Y, details.XAxis.Z),
                new UcsVectorText("Y Axis", details.YAxis.X, details.YAxis.Y, details.YAxis.Z),
                new UcsVectorText("ZAxis", details.ZAxis.X, details.ZAxis.Y, details.ZAxis.Z),
            };
            Raise(nameof(Name));
            Raise(nameof(NameLine));
            Raise(nameof(Groups));
        }
    }

    /// <summary>
    /// AutoCAD's Orthographic UCS depth dialog: the depth of one face as a number, and the button that picks a new origin in the
    /// drawing instead. OK gives the row the number; a text that is not a number is refused in the dialog.
    /// </summary>
    public sealed class UcsDepthViewModel : ObservableObject
    {
        private readonly UcsManagerViewModel _manager;
        private readonly UcsOrthoRowViewModel _row;
        private string _text;
        private string _notice = string.Empty;

        internal UcsDepthViewModel(UcsManagerViewModel manager, UcsOrthoRowViewModel row)
        {
            _manager = manager;
            _row = row;
            _text = row.DepthText;
        }

        /// <summary>Gets the label of the box: <c>Front Depth:</c>.</summary>
        public string Label => _row.Name + " Depth:";

        /// <summary>Gets or sets the depth as typed.</summary>
        public string Text
        {
            get => _text;
            set => Set(ref _text, value ?? string.Empty);
        }

        /// <summary>Gets why OK was refused; empty otherwise.</summary>
        public string Notice
        {
            get => _notice;
            private set => Set(ref _notice, value);
        }

        /// <summary>Gives the row the depth typed.</summary>
        /// <returns>True where it was a number; false, with the reason in <see cref="Notice"/>, where it was not.</returns>
        public bool Accept()
        {
            if (
                !double.TryParse(
                    (_text ?? string.Empty).Trim(),
                    NumberStyles.Float,
                    CultureInfo.InvariantCulture,
                    out double depth
                )
                || double.IsNaN(depth)
                || double.IsInfinity(depth)
            )
            {
                Notice = "Enter a number for the depth.";
                return false;
            }

            _manager.SetDepth(_row, depth);
            Notice = string.Empty;
            return true;
        }

        /// <summary>Asks the command to pick a new origin in the drawing for this depth, as the Select new origin button does; the UCS dialog is shown again after.</summary>
        public void Pick() => _manager.RequestPick(_row);
    }

    /// <summary>
    /// AutoCAD 2021's UCS dialog (UCSMAN, UC) over the request its command hands the window: Named UCSs, Orthographic UCSs and
    /// Settings. Nothing reaches the drawing until <see cref="Accept"/>; <see cref="Cancel"/>, Esc and the close button leave
    /// it as it was. The list, the names and the numbers are the request's; this adds what the window needs: the rows to bind
    /// to, the row in the editor, the tabs and the commands of the buttons and the menu.
    /// </summary>
    public sealed class UcsManagerViewModel : ObservableObject
    {
        private readonly Dictionary<CadUcsDialogRow, UcsRowViewModel> _made =
            new Dictionary<CadUcsDialogRow, UcsRowViewModel>();
        private readonly Dictionary<CadUcsOrthoRow, UcsOrthoRowViewModel> _madeOrtho =
            new Dictionary<CadUcsOrthoRow, UcsOrthoRowViewModel>();
        private UcsRowViewModel _selected;
        private UcsOrthoRowViewModel _selectedOrtho;
        private string _notice = string.Empty;

        /// <summary>Makes the dialog over a request.</summary>
        /// <param name="request">The request of UCSMAN.</param>
        /// <exception cref="ArgumentNullException">Thrown when the request is null.</exception>
        public UcsManagerViewModel(CadUcsDialogRequest request)
        {
            Request = request ?? throw new ArgumentNullException(nameof(request));
            Rows = new ObservableCollection<UcsRowViewModel>();
            OrthoRows = new ObservableCollection<UcsOrthoRowViewModel>(request.OrthoRows.Select(r => OrthoOf(r)));
            _selectedOrtho = OrthoRows[0];
            SetCurrentCommand = new DelegateCommand(_ => SetCurrent(), _ => CanSetCurrent);
            DetailsCommand = new DelegateCommand(_ => ShowDetails(), _ => _selected != null);
            RenameCommand = new DelegateCommand(_ => BeginRename(), _ => _selected != null && _selected.CanRename);
            DeleteCommand = new DelegateCommand(_ => Delete(), _ => _selected != null && _selected.CanDelete);
            OrthoSetCurrentCommand = new DelegateCommand(_ => SetCurrentOrtho(), _ => _selectedOrtho != null);
            OrthoDetailsCommand = new DelegateCommand(_ => ShowOrthoDetails(), _ => _selectedOrtho != null);
            OrthoResetCommand = new DelegateCommand(_ => ResetDepth(), _ => _selectedOrtho != null);
            OrthoDepthCommand = new DelegateCommand(_ => ShowDepth(), _ => _selectedOrtho != null);
            Refresh();
            _selected = Rows.FirstOrDefault(r => r.Row.IsCurrent) ?? Rows[0];
        }

        /// <summary>Raised when the Details dialog is to be shown; its view model is the argument.</summary>
        public event EventHandler<UcsDetailsViewModel> DetailsRequested;

        /// <summary>Raised when the Orthographic UCS depth dialog is to be shown; its view model is the argument.</summary>
        public event EventHandler<UcsDepthViewModel> DepthRequested;

        /// <summary>Gets the request.</summary>
        public CadUcsDialogRequest Request { get; }

        /// <summary>Gets the rows of the Named UCSs list.</summary>
        public ObservableCollection<UcsRowViewModel> Rows { get; }

        /// <summary>Gets the rows of the Orthographic UCSs tab.</summary>
        public ObservableCollection<UcsOrthoRowViewModel> OrthoRows { get; }

        /// <summary>Gets the line <c>Current UCS:  Unnamed</c>, which follows Set Current at once.</summary>
        public string CurrentLine => "Current UCS:  " + Request.CurrentName;

        /// <summary>Gets the name of the current system, as the label shows it.</summary>
        public string CurrentName => Request.CurrentName;

        /// <summary>Gets the systems the Orthographic UCSs tab can be relative to: World, then the named ones.</summary>
        public IReadOnlyList<CadUcsDialogRow> OrthoBaseChoices => Request.OrthoBaseRows;

        /// <summary>Gets or sets the system the Orthographic UCSs tab is relative to.</summary>
        public CadUcsDialogRow OrthoBase
        {
            get => Request.OrthoBase;
            set
            {
                if (value != null && !ReferenceEquals(value, Request.OrthoBase))
                {
                    Request.OrthoBase = value;
                    Raise(nameof(OrthoBase));
                }
            }
        }

        /// <summary>Gets or sets the row selected in the Named UCSs list.</summary>
        public UcsRowViewModel SelectedRow
        {
            get => _selected;
            set
            {
                if (Set(ref _selected, value))
                {
                    RefreshCommands();
                }
            }
        }

        /// <summary>Gets or sets the row selected in the Orthographic UCSs list.</summary>
        public UcsOrthoRowViewModel SelectedOrtho
        {
            get => _selectedOrtho;
            set
            {
                if (Set(ref _selectedOrtho, value))
                {
                    RefreshCommands();
                }
            }
        }

        /// <summary>Gets why Set Current was refused (a system not parallel to the view); empty otherwise.</summary>
        public string Notice
        {
            get => _notice;
            private set => Set(ref _notice, value);
        }

        /// <summary>Gets or sets the tab shown: 0 for Named UCSs, 1 for Orthographic UCSs, 2 for Settings; the command keeps it for the next time.</summary>
        public int Tab
        {
            get => Request.Tab;
            set
            {
                if (value != Request.Tab)
                {
                    Request.Tab = value;
                    Raise(nameof(Tab));
                    Raise(nameof(IsNamedTab));
                    Raise(nameof(IsOrthoTab));
                    Raise(nameof(IsSettingsTab));
                    Notice = string.Empty;
                }
            }
        }

        /// <summary>Gets or sets whether the Named UCSs tab is shown.</summary>
        public bool IsNamedTab
        {
            get => Request.Tab == 0;
            set => ShowTab(value, 0);
        }

        /// <summary>Gets or sets whether the Orthographic UCSs tab is shown.</summary>
        public bool IsOrthoTab
        {
            get => Request.Tab == 1;
            set => ShowTab(value, 1);
        }

        /// <summary>Gets or sets whether the Settings tab is shown.</summary>
        public bool IsSettingsTab
        {
            get => Request.Tab == 2;
            set => ShowTab(value, 2);
        }

        /// <summary>Gets or sets whether the UCS icon is shown (UCSICON bit 1: On).</summary>
        public bool IconOn
        {
            get => Request.IconOn;
            set => Edit(() => Request.IconOn = value, nameof(IconOn));
        }

        /// <summary>Gets or sets whether the icon is drawn at the origin of the system (UCSICON bit 2: Display at UCS origin point).</summary>
        public bool IconAtOrigin
        {
            get => Request.IconAtOrigin;
            set => Edit(() => Request.IconAtOrigin = value, nameof(IconAtOrigin));
        }

        /// <summary>Gets or sets whether OK gives every viewport the icon settings (Apply to all active viewports); off every time the dialog opens.</summary>
        public bool ApplyToAllViewports
        {
            get => Request.ApplyToAllViewports;
            set => Edit(() => Request.ApplyToAllViewports = value, nameof(ApplyToAllViewports));
        }

        /// <summary>Gets or sets whether the UCS icon can be selected (UCSSELECTMODE: Allow Selecting UCS icon).</summary>
        public bool IconSelectable
        {
            get => Request.IconSelectable;
            set => Edit(() => Request.IconSelectable = value, nameof(IconSelectable));
        }

        /// <summary>Gets or sets whether each viewport keeps its own system (UCSVP: Save UCS with viewport).</summary>
        public bool SaveWithViewport
        {
            get => Request.SaveWithViewport;
            set => Edit(() => Request.SaveWithViewport = value, nameof(SaveWithViewport));
        }

        /// <summary>Gets or sets whether the view goes to the plan when the system changes (UCSFOLLOW: Update view to Plan when UCS is changed).</summary>
        public bool FollowToPlan
        {
            get => Request.FollowToPlan;
            set => Edit(() => Request.FollowToPlan = value, nameof(FollowToPlan));
        }

        /// <summary>Gets the button and menu command Set Current of the Named UCSs tab.</summary>
        public ICommand SetCurrentCommand { get; }

        /// <summary>Gets the button and menu command Details of the Named UCSs tab.</summary>
        public ICommand DetailsCommand { get; }

        /// <summary>Gets the menu command Rename, which edits the selected row in place.</summary>
        public ICommand RenameCommand { get; }

        /// <summary>Gets the menu command Delete, which takes the selected row out with no question.</summary>
        public ICommand DeleteCommand { get; }

        /// <summary>Gets the button and menu command Set Current of the Orthographic UCSs tab.</summary>
        public ICommand OrthoSetCurrentCommand { get; }

        /// <summary>Gets the button and menu command Details of the Orthographic UCSs tab.</summary>
        public ICommand OrthoDetailsCommand { get; }

        /// <summary>Gets the menu command Reset, which gives the depth of the selected face nought.</summary>
        public ICommand OrthoResetCommand { get; }

        /// <summary>Gets the menu command Depth, which shows the Orthographic UCS depth dialog.</summary>
        public ICommand OrthoDepthCommand { get; }

        /// <summary>Gets the row of the Named UCSs list that is being edited in place; null for none.</summary>
        public UcsRowViewModel EditingRow => Rows.FirstOrDefault(r => r.IsEditing);

        /// <summary>
        /// Makes the selected system current in the dialog: the black triangle and the label move at once, the drawing is
        /// given it at OK. A system not parallel to the view is refused, with the reason in <see cref="Notice"/>.
        /// </summary>
        public void SetCurrent()
        {
            if (_selected == null)
            {
                return;
            }

            Notice = Request.SetCurrent(_selected.Row) ?? string.Empty;
            Refresh();
        }

        /// <summary>Starts editing the name of the selected row in place, as Rename, F2 and a second click on a selected row do.</summary>
        public void BeginRename()
        {
            if (_selected == null || !_selected.CanRename)
            {
                return;
            }

            _selected.EditText = _selected.Name;
            _selected.IsEditing = true;
        }

        /// <summary>
        /// Gives the row being edited the name typed: Enter, or the box losing focus. A name AutoCAD refuses stays in the box,
        /// the row still being edited, and the message is returned for the window to show.
        /// </summary>
        /// <returns>Null where the name was taken or nothing was being edited; else the message to show.</returns>
        public CadUcsDialogError CommitRename()
        {
            UcsRowViewModel row = EditingRow;

            if (row == null)
            {
                return null;
            }

            CadUcsDialogError error = Request.Rename(row.Row, row.EditText);

            if (error == null)
            {
                row.IsEditing = false;
                Refresh();
            }

            return error;
        }

        /// <summary>Drops what was typed into the row being edited, as Esc does; the name is as it was.</summary>
        public void CancelRename()
        {
            UcsRowViewModel row = EditingRow;

            if (row != null)
            {
                row.IsEditing = false;
            }
        }

        /// <summary>Takes the selected named system out of the list, with no question.</summary>
        public void Delete()
        {
            if (_selected == null || !_selected.CanDelete)
            {
                return;
            }

            int index = Rows.IndexOf(_selected);
            Request.Delete(_selected.Row);
            Refresh();
            _selected = Rows[Math.Min(index, Rows.Count - 1)];
            Raise(nameof(SelectedRow));
            RefreshCommands();
        }

        /// <summary>Makes the Details dialog of the selected row of the Named UCSs list.</summary>
        /// <returns>The dialog's view model, relative to World; null where no row is selected.</returns>
        public UcsDetailsViewModel CreateDetails()
        {
            UcsRowViewModel row = _selected;
            return row == null ? null : new UcsDetailsViewModel(Request, to => Request.Details(row.Row, to));
        }

        /// <summary>Makes the Details dialog of the selected row of the Orthographic UCSs tab.</summary>
        /// <returns>The dialog's view model, relative to World; null where no row is selected.</returns>
        public UcsDetailsViewModel CreateOrthoDetails()
        {
            UcsOrthoRowViewModel row = _selectedOrtho;
            return row == null ? null : new UcsDetailsViewModel(Request, to => Request.Details(row.Row, to));
        }

        /// <summary>Makes the Orthographic UCS depth dialog of the selected face.</summary>
        /// <returns>The dialog's view model; null where no row is selected.</returns>
        public UcsDepthViewModel CreateDepth() =>
            _selectedOrtho == null ? null : new UcsDepthViewModel(this, _selectedOrtho);

        /// <summary>Makes the selected face's system current in the dialog; one not parallel to the view is refused, with the reason in <see cref="Notice"/>.</summary>
        public void SetCurrentOrtho()
        {
            if (_selectedOrtho == null)
            {
                return;
            }

            Notice = Request.SetCurrent(_selectedOrtho.Row) ?? string.Empty;
            Refresh();
        }

        /// <summary>Gives the selected face the depth nought.</summary>
        public void ResetDepth()
        {
            if (_selectedOrtho != null)
            {
                SetDepth(_selectedOrtho, 0.0);
            }
        }

        /// <summary>Gives a face a depth.</summary>
        /// <param name="row">The face.</param>
        /// <param name="depth">The depth in millimetres.</param>
        internal void SetDepth(UcsOrthoRowViewModel row, double depth)
        {
            Request.SetDepth(row.Row, depth);
            row.Refresh();
        }

        /// <summary>Asks for a new origin to be picked in the drawing for the depth of a face; the dialog closes and the command shows it again afterwards.</summary>
        internal void RequestPick(UcsOrthoRowViewModel row)
        {
            Request.PickRow = row.Row;
            Request.Action = CadDialogAction.PickPoint;
        }

        /// <summary>OK: the drawing is given what the dialog changed, as one undo step.</summary>
        /// <returns>True: the dialog may close.</returns>
        public bool Accept()
        {
            CancelRename();
            Request.Action = CadDialogAction.Ok;
            return true;
        }

        /// <summary>Cancel, Esc and the close button: nothing reaches the drawing.</summary>
        public void Cancel() => Request.Action = CadDialogAction.Cancel;

        private void ShowDetails()
        {
            UcsDetailsViewModel details = CreateDetails();

            if (details != null)
            {
                DetailsRequested?.Invoke(this, details);
            }
        }

        private void ShowOrthoDetails()
        {
            UcsDetailsViewModel details = CreateOrthoDetails();

            if (details != null)
            {
                DetailsRequested?.Invoke(this, details);
            }
        }

        private void ShowDepth()
        {
            UcsDepthViewModel depth = CreateDepth();

            if (depth != null)
            {
                DepthRequested?.Invoke(this, depth);
            }
        }

        private bool CanSetCurrent => _selected != null;

        private UcsOrthoRowViewModel OrthoOf(CadUcsOrthoRow row)
        {
            if (!_madeOrtho.TryGetValue(row, out UcsOrthoRowViewModel made))
            {
                made = new UcsOrthoRowViewModel(row);
                _madeOrtho.Add(row, made);
            }

            return made;
        }

        /// <summary>Lists the rows again as the request has them, keeping each row's own view model, and the row selected.</summary>
        private void Refresh()
        {
            UcsRowViewModel selected = _selected;
            Rows.Clear();

            foreach (CadUcsDialogRow row in Request.Rows)
            {
                if (!_made.TryGetValue(row, out UcsRowViewModel made))
                {
                    made = new UcsRowViewModel(row);
                    _made.Add(row, made);
                }

                made.Refresh();
                Rows.Add(made);
            }

            _selected = selected != null && Rows.Contains(selected) ? selected : Rows.FirstOrDefault();
            Raise(nameof(SelectedRow));
            Raise(nameof(CurrentLine));
            Raise(nameof(CurrentName));
            Raise(nameof(OrthoBaseChoices));
            Raise(nameof(OrthoBase));
            RefreshCommands();
        }

        private void RefreshCommands()
        {
            foreach (
                DelegateCommand command in new[]
                {
                    (DelegateCommand)SetCurrentCommand,
                    (DelegateCommand)DetailsCommand,
                    (DelegateCommand)RenameCommand,
                    (DelegateCommand)DeleteCommand,
                    (DelegateCommand)OrthoSetCurrentCommand,
                    (DelegateCommand)OrthoDetailsCommand,
                    (DelegateCommand)OrthoResetCommand,
                    (DelegateCommand)OrthoDepthCommand,
                }
            )
            {
                command.RaiseCanExecuteChanged();
            }
        }

        private void ShowTab(bool shown, int tab)
        {
            if (shown)
            {
                Tab = tab;
            }
        }

        private void Edit(Action write, string name)
        {
            write();
            Raise(name);
        }
    }
}
