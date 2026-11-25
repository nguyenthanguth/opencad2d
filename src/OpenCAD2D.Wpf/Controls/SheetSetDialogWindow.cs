using System;
using System.IO;
using System.Windows;
using System.Windows.Input;
using Microsoft.Win32;

namespace OpenCAD2D.Wpf
{
    /// <summary>
    /// The window the sheet set dialogs share: its content is a <see cref="SheetSetDialogFrame"/>, whose OK applies the view
    /// model (and closes the dialog where it was applied), whose Cancel, Close and cross close it, and whose Help opens the
    /// guide. It gives the view model the file and folder dialogs its ... buttons ask for. Esc is Cancel. The window is made
    /// for a view model and shows nothing until <see cref="Window.ShowDialog"/>; <see cref="Body"/> is what a screenshot draws.
    /// </summary>
    public abstract class SheetSetDialogWindow : Window
    {
        /// <summary>Makes the window over its view model; the derived window then loads its XAML and calls <see cref="Wire"/>.</summary>
        /// <param name="model">The view model.</param>
        /// <exception cref="ArgumentNullException">Thrown when the model is null.</exception>
        protected SheetSetDialogWindow(SheetSetDialogViewModel model)
        {
            Model = model ?? throw new ArgumentNullException(nameof(model));
            DataContext = model;
        }

        /// <summary>Gets the view model.</summary>
        public SheetSetDialogViewModel Model { get; }

        /// <summary>Gets or sets what shows a dialog in place of <see cref="Window.ShowDialog"/>, which a test sets to see the view model of each dialog that is asked for without a window; null shows it as a dialog.</summary>
        internal static Func<SheetSetDialogWindow, bool?> Presenter { get; set; }

        /// <summary>Gets the dialog's content, so that a screenshot can draw it with no window shown.</summary>
        public FrameworkElement Body => (FrameworkElement)Content;

        /// <summary>Joins the frame's buttons to the view model and gives the view model its file and folder dialogs. Called once, by the derived window, after its XAML is loaded.</summary>
        /// <param name="frame">The frame that is the window's content.</param>
        protected void Wire(SheetSetDialogFrame frame)
        {
            frame.OkClick += (s, e) => OnOk();
            frame.CancelClick += (s, e) => OnCancel();
            frame.HelpClick += (s, e) => PlotHelp.Open(Model.HelpUrl);
            frame.HeaderPressed += OnHeaderDown;
            Model.BrowseFile = (title, filter, current) => AskFile(title, filter, current);
            Model.BrowseFolder = current => AskFolder(current);
        }

        /// <summary>Shows the dialog over a window, which waits until it is closed.</summary>
        /// <param name="owner">The window it is shown over; null for none.</param>
        /// <returns>True when the dialog was closed by OK.</returns>
        internal bool Present(Window owner)
        {
            if (owner != null)
            {
                Owner = owner;
            }

            return (Presenter != null ? Presenter(this) : ShowDialog()) == true;
        }

        /// <summary>Applies the view model and closes the dialog where it was applied.</summary>
        protected virtual void OnOk()
        {
            if (Model.Apply())
            {
                DialogResult = true;
            }
        }

        /// <summary>Closes the dialog and applies nothing.</summary>
        protected void OnCancel() => DialogResult = false;

        private void OnHeaderDown(object sender, MouseButtonEventArgs e)
        {
            if (e.ButtonState == MouseButtonState.Pressed && IsLoaded)
            {
                DragMove();
            }
        }

        private string AskFile(string title, string filter, string current)
        {
            var dialog = new OpenFileDialog
            {
                Title = title,
                Filter = filter,
                CheckFileExists = true,
            };
            if (!string.IsNullOrWhiteSpace(current))
            {
                try
                {
                    dialog.FileName = Path.GetFileName(current);
                    string folder = Path.GetDirectoryName(current);
                    if (!string.IsNullOrEmpty(folder) && Directory.Exists(folder))
                    {
                        dialog.InitialDirectory = folder;
                    }
                }
                catch (ArgumentException)
                {
                    // A text that is no path starts the dialog where it likes.
                }
            }

            return dialog.ShowDialog(this) == true ? dialog.FileName : null;
        }

        private string AskFolder(string current)
        {
            bool start = !string.IsNullOrEmpty(current) && Directory.Exists(current);
#if NET8_0_OR_GREATER
            var dialog = new OpenFolderDialog { Title = "Choose a folder" };
            if (start)
            {
                dialog.InitialDirectory = current;
            }

            return dialog.ShowDialog(this) == true ? dialog.FolderName : null;
#else
            var dialog = new SaveFileDialog
            {
                Title = "Choose a folder",
                FileName = "Use this folder",
                Filter = "Folders|*.folder",
                OverwritePrompt = false,
            };
            if (start)
            {
                dialog.InitialDirectory = current;
            }

            return dialog.ShowDialog(this) == true ? Path.GetDirectoryName(dialog.FileName) : null;
#endif
        }
    }
}
