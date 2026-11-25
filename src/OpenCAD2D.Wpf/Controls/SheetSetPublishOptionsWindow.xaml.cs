using System;
using System.Windows;
using System.Windows.Input;

namespace OpenCAD2D.Wpf
{
    /// <summary>AutoCAD's Sheet Set PDF Publish Options, from the Sheet List's Publish menu; its logic is <see cref="SheetSetPublishOptionsViewModel"/>.</summary>
    public partial class SheetSetPublishOptionsWindow : Window
    {
        /// <summary>Makes the dialog over its view model.</summary>
        /// <param name="model">The view model.</param>
        /// <exception cref="ArgumentNullException">Thrown when the model is null.</exception>
        public SheetSetPublishOptionsWindow(SheetSetPublishOptionsViewModel model)
        {
            Model = model ?? throw new ArgumentNullException(nameof(model));
            InitializeComponent();
            DataContext = model;

            if (model.AskFolder == null)
            {
                model.AskFolder = folder => AskFolder(this, folder);
            }
        }

        /// <summary>Gets the view model.</summary>
        public SheetSetPublishOptionsViewModel Model { get; }

        /// <summary>Gets the dialog's content, so that a screenshot can draw it with no window shown.</summary>
        public FrameworkElement Body => Root;

        /// <summary>Shows the dialog over a window and says whether OK was pressed with entries that were good.</summary>
        /// <param name="owner">The window it is shown over; null for none.</param>
        /// <param name="model">The view model; its options are saved on OK.</param>
        /// <returns>True when OK was pressed and the options were saved.</returns>
        public static bool Show(Window owner, SheetSetPublishOptionsViewModel model) =>
            new SheetSetPublishOptionsWindow(model) { Owner = owner }.ShowDialog() == true;

        /// <summary>Asks for the name of the multi-sheet PDF, as Prompt for name does: a Save dialog offering the path.</summary>
        /// <param name="owner">The window it is shown over; null for none.</param>
        /// <param name="path">The path the PDF would be written to.</param>
        /// <returns>The path chosen; null where the dialog was canceled.</returns>
        public static string AskPdfFile(Window owner, string path)
        {
            var dialog = new Microsoft.Win32.SaveFileDialog
            {
                Title = "Publish to PDF",
                Filter = "PDF files (*.pdf)|*.pdf",
                DefaultExt = ".pdf",
                FileName = System.IO.Path.GetFileName(path),
                OverwritePrompt = true,
            };
            string folder = System.IO.Path.GetDirectoryName(path);

            if (!string.IsNullOrEmpty(folder) && System.IO.Directory.Exists(folder))
            {
                dialog.InitialDirectory = folder;
            }

            return dialog.ShowDialog(owner) == true ? dialog.FileName : null;
        }

        private static string AskFolder(Window owner, string folder)
        {
            // The folder is the one the file dialog is left in: neither WPF of .NET Framework nor this project has a folder dialog.
            var dialog = new Microsoft.Win32.OpenFileDialog
            {
                Title = "Select a Folder for the PDF Files",
                FileName = "Select this folder",
                ValidateNames = false,
                CheckFileExists = false,
                CheckPathExists = true,
            };

            if (!string.IsNullOrEmpty(folder) && System.IO.Directory.Exists(folder))
            {
                dialog.InitialDirectory = folder;
            }

            return dialog.ShowDialog(owner) == true ? System.IO.Path.GetDirectoryName(dialog.FileName) : null;
        }

        private void OnHeaderDown(object sender, MouseButtonEventArgs e)
        {
            if (e.ButtonState == MouseButtonState.Pressed && IsLoaded)
            {
                DragMove();
            }
        }

        private void OnBrowse(object sender, RoutedEventArgs e) => Model.BrowseLocation();

        private void OnHelp(object sender, RoutedEventArgs e) => PlotHelp.Open(Model.HelpUrl);

        private void OnOk(object sender, RoutedEventArgs e)
        {
            if (Model.Accept())
            {
                DialogResult = true;
            }
        }

        private void OnCancel(object sender, RoutedEventArgs e) => DialogResult = false;
    }
}
