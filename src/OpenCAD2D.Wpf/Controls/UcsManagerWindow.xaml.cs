using System;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Threading;

namespace OpenCAD2D.Wpf
{
    /// <summary>
    /// AutoCAD 2021's UCS dialog, which UCSMAN (UC) shows: Named UCSs, Orthographic UCSs and Settings, 424 by 361. Its logic is
    /// <see cref="UcsManagerViewModel"/>; this holds what only a window does: the rename in place, the menus, and the dialogs
    /// it opens (Details, Orthographic UCS depth, and the message of a name refused).
    /// </summary>
    public partial class UcsManagerWindow : Window
    {
        private const string HelpPage = "https://nguyenthanguth.github.io/opencad2d/guides/presenter/editing.html";

        // A click on a row already selected renames it after the time of a double click, which a second click cancels.
        private readonly DispatcherTimer _renameTimer = new DispatcherTimer
        {
            Interval = TimeSpan.FromMilliseconds(GetDoubleClickTime()),
        };
        private UcsRowViewModel _clicked;
        private bool _showingError;

        /// <summary>Makes the dialog over its view model.</summary>
        /// <param name="model">The view model.</param>
        /// <exception cref="ArgumentNullException">Thrown when the model is null.</exception>
        public UcsManagerWindow(UcsManagerViewModel model)
        {
            Model = model ?? throw new ArgumentNullException(nameof(model));
            InitializeComponent();
            DataContext = model;
            model.DetailsRequested += (sender, details) => UcsDetailsWindow.Show(this, details);
            model.DepthRequested += OnDepthRequested;
            _renameTimer.Tick += OnRenameTimer;
        }

        /// <summary>Gets the view model.</summary>
        public UcsManagerViewModel Model { get; }

        /// <summary>Gets the dialog's content, so that a screenshot can draw it with no window shown.</summary>
        public FrameworkElement Body => Root;

        /// <summary>Shows the dialog over a window; the action it closed with is in the request.</summary>
        /// <param name="owner">The window it is shown over; null for none.</param>
        /// <param name="model">The view model.</param>
        /// <returns>True: the request is answered.</returns>
        public static bool Show(Window owner, UcsManagerViewModel model)
        {
            new UcsManagerWindow(model) { Owner = owner }.ShowDialog();
            return true;
        }

        [DllImport("user32.dll")]
        private static extern uint GetDoubleClickTime();

        private void OnHeaderDown(object sender, MouseButtonEventArgs e)
        {
            if (e.ButtonState == MouseButtonState.Pressed && IsLoaded)
            {
                DragMove();
            }
        }

        private void OnOk(object sender, RoutedEventArgs e)
        {
            if (Model.Accept())
            {
                DialogResult = true;
            }
        }

        private void OnCancel(object sender, RoutedEventArgs e)
        {
            Model.Cancel();
            DialogResult = false;
        }

        private void OnHelp(object sender, RoutedEventArgs e) => PlotHelp.Open(HelpPage);

        /// <summary>The depth dialog; its Select new origin button closes this dialog too, and the command shows it again after the pick.</summary>
        private void OnDepthRequested(object sender, UcsDepthViewModel depth)
        {
            UcsDepthWindow.Ask(this, depth);

            if (Model.Request.Action == CadDialogAction.PickPoint)
            {
                DialogResult = false;
            }
        }

        private void OnOrthoDoubleClick(object sender, MouseButtonEventArgs e)
        {
            if (RowAt(e.OriginalSource) != null)
            {
                Model.OrthoDepthCommand.Execute(null);
            }
        }

        /// <summary>A right click selects the row under it, so that the menu acts on it.</summary>
        private void OnRowRightClick(object sender, MouseButtonEventArgs e)
        {
            ListBoxItem item = RowAt(e.OriginalSource);

            if (item != null)
            {
                item.IsSelected = true;
            }
        }

        private void OnNamedKeyDown(object sender, KeyEventArgs e)
        {
            if (e.OriginalSource is TextBox)
            {
                return;
            }

            if (e.Key == Key.F2)
            {
                Model.RenameCommand.Execute(null);
                e.Handled = true;
            }
            else if (e.Key == Key.Delete && Model.DeleteCommand.CanExecute(null))
            {
                Model.DeleteCommand.Execute(null);
                e.Handled = true;
            }
        }

        private void OnNamedMouseDown(object sender, MouseButtonEventArgs e)
        {
            if (e.OriginalSource is DependencyObject source && FindParent<TextBox>(source) != null)
            {
                return;
            }

            ListBoxItem item = RowAt(e.OriginalSource);
            _renameTimer.Stop();

            if (item == null)
            {
                return;
            }

            if (e.ClickCount >= 2)
            {
                // A double click is Set Current.
                _clicked = null;
                item.IsSelected = true;
                Model.SetCurrentCommand.Execute(null);
                return;
            }

            _clicked = item.IsSelected ? item.DataContext as UcsRowViewModel : null;
        }

        private void OnNamedMouseUp(object sender, MouseButtonEventArgs e)
        {
            ListBoxItem item = RowAt(e.OriginalSource);

            if (_clicked != null && item != null && ReferenceEquals(item.DataContext, _clicked) && _clicked.CanRename)
            {
                _renameTimer.Start();
            }
            else
            {
                _clicked = null;
            }
        }

        private void OnRenameTimer(object sender, EventArgs e)
        {
            _renameTimer.Stop();

            if (_clicked != null && ReferenceEquals(Model.SelectedRow, _clicked))
            {
                Model.BeginRename();
            }

            _clicked = null;
        }

        private void OnEditorShown(object sender, DependencyPropertyChangedEventArgs e)
        {
            if (sender is TextBox box && box.IsVisible)
            {
                box.Dispatcher.BeginInvoke(
                    DispatcherPriority.Input,
                    new Action(() =>
                    {
                        box.Focus();
                        box.SelectAll();
                    })
                );
            }
        }

        private void OnEditorKeyDown(object sender, KeyEventArgs e)
        {
            if (e.Key == Key.Enter || e.Key == Key.Return)
            {
                e.Handled = true;
                Commit((TextBox)sender);
            }
            else if (e.Key == Key.Escape)
            {
                e.Handled = true;
                Model.CancelRename();
                NamedList.Focus();
            }
        }

        private void OnEditorLostFocus(object sender, KeyboardFocusChangedEventArgs e)
        {
            if (!_showingError && Model.EditingRow != null && e.NewFocus != null)
            {
                Commit((TextBox)sender);
            }
        }

        /// <summary>Gives the row being edited the name typed; a name refused is said in a message, and the row goes back to editing after Close.</summary>
        private void Commit(TextBox box)
        {
            if (_showingError)
            {
                return;
            }

            CadUcsDialogError error = Model.CommitRename();

            if (error == null)
            {
                NamedList.Focus();
                return;
            }

            _showingError = true;

            try
            {
                UcsNoticeWindow.Show(this, error);
            }
            finally
            {
                _showingError = false;
            }

            box.Focus();
            box.SelectAll();
        }

        private static ListBoxItem RowAt(object source) =>
            source is DependencyObject element ? FindParent<ListBoxItem>(element) : null;

        private static T FindParent<T>(DependencyObject element)
            where T : DependencyObject
        {
            for (
                DependencyObject d = element;
                d != null;
                d =
                        d is Visual || d is System.Windows.Media.Media3D.Visual3D
                            ? VisualTreeHelper.GetParent(d)
                            : LogicalTreeHelper.GetParent(d)
            )
            {
                if (d is T found)
                {
                    return found;
                }
            }

            return null;
        }
    }
}
