using System;
using System.IO;
using System.Windows;
using System.Windows.Input;
using Microsoft.Win32;

namespace OpenCAD2D.Wpf
{
    /// <summary>
    /// The Create Sheet Set wizard of NEWSHEETSET, dark as the window's other dialogs: 660 by 470, the pages of the route down the
    /// left, the page on the right, and Back, Next (Finish on Confirm) and Cancel under it. Its logic is
    /// <see cref="NewSheetSetViewModel"/>; this window gives it the file and folder dialogs of its buttons.
    /// </summary>
    public partial class NewSheetSetWindow : Window
    {
        /// <summary>Makes the wizard over its view model, giving it the dialogs of its buttons.</summary>
        /// <param name="model">The view model.</param>
        /// <exception cref="ArgumentNullException">Thrown when the model is null.</exception>
        public NewSheetSetWindow(NewSheetSetViewModel model)
        {
            Model = model ?? throw new ArgumentNullException(nameof(model));
            InitializeComponent();
            DataContext = model;
            model.AskExample = present =>
            {
                var dialog = new OpenFileDialog
                {
                    Title = "Select Sheet Set Example",
                    Filter = "Sheet set data (*.dst)|*.dst|All files (*.*)|*.*",
                    FileName = Path.GetFileName(present ?? string.Empty),
                };
                return dialog.ShowDialog(this) == true ? dialog.FileName : null;
            };
            model.AskStorageFolder = present => AskFolder("Store Sheet Set Data File In", present);
            model.AskDrawingFolder = () => AskFolder("Add Folder of Drawings", null);
            model.AskImportOptions = options => SheetImportOptionsWindow.Show(this, options);
            model.AskProperties = draft =>
                SheetSetDialogs.ShowSheetSetProperties(this, draft, new SheetSetDialogOptions { NameReadOnly = true });
            model.CloseRequested += (s, ok) =>
            {
                if (IsLoaded)
                {
                    DialogResult = ok;
                }
            };
        }

        /// <summary>Gets the view model.</summary>
        public NewSheetSetViewModel Model { get; }

        /// <summary>Gets the dialog's content, so that a screenshot can draw it with no window shown.</summary>
        public FrameworkElement Body => Root;

        /// <summary>Shows the wizard over a window until Finish or Cancel.</summary>
        /// <param name="owner">The window it is shown over; null for none.</param>
        /// <param name="model">The view model.</param>
        public static void Show(Window owner, NewSheetSetViewModel model) =>
            new NewSheetSetWindow(model) { Owner = owner }.ShowDialog();

        private string AskFolder(string title, string present)
        {
            // A folder is chosen as the open file dialog chooses it: its name typed, no file needed.
            var dialog = new OpenFileDialog
            {
                Title = title,
                ValidateNames = false,
                CheckFileExists = false,
                CheckPathExists = true,
                FileName = "Select this folder",
                InitialDirectory = ExistingFolder(present),
            };
            return dialog.ShowDialog(this) == true ? Path.GetDirectoryName(dialog.FileName) : null;
        }

        private static string ExistingFolder(string folder)
        {
            try
            {
                return !string.IsNullOrWhiteSpace(folder) && Directory.Exists(folder) ? folder : null;
            }
            catch (ArgumentException)
            {
                return null;
            }
        }

        private void OnHeaderDown(object sender, MouseButtonEventArgs e)
        {
            if (e.ButtonState == MouseButtonState.Pressed && IsLoaded)
            {
                DragMove();
            }
        }
    }
}
