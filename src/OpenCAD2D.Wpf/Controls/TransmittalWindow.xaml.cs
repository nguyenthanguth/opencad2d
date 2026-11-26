using System;
using System.Windows;
using System.Windows.Input;

namespace OpenCAD2D.Wpf
{
    /// <summary>AutoCAD 2021's Create Transmittal as the design draws it, reduced, from the Sheet List's eTransmit item; its logic is <see cref="TransmittalViewModel"/>.</summary>
    public partial class TransmittalWindow : Window
    {
        /// <summary>Makes the dialog over its view model.</summary>
        /// <param name="model">The view model.</param>
        /// <exception cref="ArgumentNullException">Thrown when the model is null.</exception>
        public TransmittalWindow(TransmittalViewModel model)
        {
            Model = model ?? throw new ArgumentNullException(nameof(model));
            InitializeComponent();
            DataContext = model;
            SheetsTab.IsChecked = model.IsSheetsTab;
            FilesTab.IsChecked = model.IsFilesTab;

            if (model.AskZip == null)
            {
                model.AskZip = AskZip;
            }

            if (model.AskReplace == null)
            {
                model.AskReplace = AskReplace;
            }

            if (model.ShowReport == null)
            {
                model.ShowReport = text => TransmittalReportWindow.Show(this, model.SetName, text);
            }
        }

        /// <summary>Gets the view model.</summary>
        public TransmittalViewModel Model { get; }

        /// <summary>Gets the dialog's content, so that a screenshot can draw it with no window shown.</summary>
        public FrameworkElement Body => Root;

        /// <summary>Shows the dialog over a window and says whether OK was pressed and the zip written.</summary>
        /// <param name="owner">The window it is shown over; null for none.</param>
        /// <param name="model">The view model; its <see cref="TransmittalViewModel.Result"/> is set on OK.</param>
        /// <returns>True when OK was pressed and the zip written.</returns>
        public static bool Show(Window owner, TransmittalViewModel model) =>
            new TransmittalWindow(model) { Owner = owner }.ShowDialog() == true;

        private string AskZip(string path)
        {
            var dialog = new Microsoft.Win32.SaveFileDialog
            {
                Title = "Transmittal Zip File",
                Filter = "Zip files (*.zip)|*.zip",
                DefaultExt = ".zip",
                FileName = System.IO.Path.GetFileName(path),
                OverwritePrompt = false,
            };
            string folder = System.IO.Path.GetDirectoryName(path);

            if (!string.IsNullOrEmpty(folder) && System.IO.Directory.Exists(folder))
            {
                dialog.InitialDirectory = folder;
            }

            return dialog.ShowDialog(this) == true ? dialog.FileName : null;
        }

        private bool AskReplace(string path) =>
            new CadQuestionWindow(
                "Replace the zip file?",
                "A file of that name is there already. It is replaced once the new zip is written.",
                path,
                "Replace",
                "Cancel"
            )
            {
                Owner = this,
            }.ShowDialog() == true;

        private void OnHeaderDown(object sender, MouseButtonEventArgs e)
        {
            if (e.ButtonState == MouseButtonState.Pressed && IsLoaded)
            {
                DragMove();
            }
        }

        private void OnChevronDown(object sender, MouseButtonEventArgs e)
        {
            if (((FrameworkElement)sender).DataContext is TransmittalSheetRow row && row.IsExpandable)
            {
                row.IsExpanded = !row.IsExpanded;
                e.Handled = true;
            }
        }

        private void OnTab(object sender, RoutedEventArgs e)
        {
            if (Model != null && IsLoaded)
            {
                Model.IsSheetsTab = ReferenceEquals(sender, SheetsTab);
            }
        }

        private void OnBrowse(object sender, RoutedEventArgs e) => Model.BrowseZip();

        private void OnViewReport(object sender, RoutedEventArgs e) => Model.ViewReport();

        private void OnHelp(object sender, RoutedEventArgs e) => PlotHelp.Open(Model.HelpUrl);

        private void OnOk(object sender, RoutedEventArgs e)
        {
            Cursor previous = Cursor;
            Cursor = Cursors.Wait;

            try
            {
                if (Model.Accept())
                {
                    DialogResult = true;
                }
            }
            finally
            {
                Cursor = previous;
            }
        }

        private void OnCancel(object sender, RoutedEventArgs e) => DialogResult = false;
    }
}
