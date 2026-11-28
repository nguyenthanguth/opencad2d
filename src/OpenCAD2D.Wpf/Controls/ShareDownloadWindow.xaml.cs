using System;
using System.ComponentModel;
using System.Diagnostics;
using System.IO;
using System.Windows;
using System.Windows.Input;
using System.Windows.Media.Imaging;
using Microsoft.Win32;

namespace OpenCAD2D.Wpf
{
    /// <summary>
    /// Download from Share, from SHAREDOWNLOAD, as the design draws it: 760 wide, the box for the link with Find, the card of the
    /// tool found (cover picture, name, summary, type, version, SHA-256, what is on this PC, review, commands and file), where to
    /// save it and whether to load it, the result line, and at the bottom Browse Share in the browser with the main button
    /// (Download, Update or Done) and Close. Its logic is <see cref="ShareDownloadViewModel"/>; the folder dialog of … and the
    /// default browser are the window's.
    /// </summary>
    public partial class ShareDownloadWindow : Window
    {
        /// <summary>Makes the dialog over its view model.</summary>
        /// <param name="model">The view model.</param>
        /// <exception cref="ArgumentNullException">Thrown when the model is null.</exception>
        public ShareDownloadWindow(ShareDownloadViewModel model)
        {
            Model = model ?? throw new ArgumentNullException(nameof(model));
            InitializeComponent();
            DataContext = model;
            Cover.Source = Decode(model.Picture);
            model.AskFolder = folder => AskFolder(this, folder);
            model.OpenBrowser = OpenInBrowser;
            model.AskConfirm = text =>
                MessageBox.Show(this, text, "Download from Share", MessageBoxButton.YesNo, MessageBoxImage.Warning)
                == MessageBoxResult.Yes;
            model.CloseRequested += OnCloseRequested;
            model.PropertyChanged += OnModelChanged;
            Loaded += OnLoaded;
            Closed += (s, e) =>
            {
                Model.PropertyChanged -= OnModelChanged;
                Model.CloseRequested -= OnCloseRequested;
                Model.Cancel();
            };
        }

        /// <summary>Gets the view model.</summary>
        public ShareDownloadViewModel Model { get; }

        /// <summary>Gets the dialog's content, so that a screenshot can draw it with no window shown.</summary>
        public FrameworkElement Body => Root;

        /// <summary>Shows the dialog over a window, which waits until it is closed.</summary>
        /// <param name="owner">The window it is shown over; null for none.</param>
        /// <param name="model">The view model.</param>
        /// <returns>True when the dialog was closed with Done or Close.</returns>
        public static bool Show(Window owner, ShareDownloadViewModel model) =>
            new ShareDownloadWindow(model) { Owner = owner }.ShowDialog() == true;

        // As the window opens: a Share link on the clipboard fills the box (the one time the clipboard is read), and the
        // keyboard goes to the box with its text selected, so that a paste or a typed link replaces it.
        private void OnLoaded(object sender, RoutedEventArgs e)
        {
            Model.PrefillFromClipboard();
            LinkBox.Focus();
            LinkBox.SelectAll();
        }

        private void OnModelChanged(object sender, PropertyChangedEventArgs e)
        {
            if (e.PropertyName == nameof(ShareDownloadViewModel.Picture))
            {
                Cover.Source = Decode(Model.Picture);
            }
        }

        // The cover picture as the window draws it; a picture that cannot be decoded is no picture.
        private static BitmapImage Decode(byte[] bytes)
        {
            if (bytes == null)
            {
                return null;
            }

            try
            {
                var image = new BitmapImage();
                image.BeginInit();
                image.CacheOption = BitmapCacheOption.OnLoad;
                image.StreamSource = new MemoryStream(bytes);
                image.EndInit();
                image.Freeze();
                return image;
            }
            catch (Exception e) when (e is NotSupportedException || e is FileFormatException || e is IOException)
            {
                return null;
            }
        }

        private static void OpenInBrowser(Uri address)
        {
            try
            {
                Process.Start(new ProcessStartInfo(address.AbsoluteUri) { UseShellExecute = true });
            }
            catch (Exception e) when (e is Win32Exception || e is InvalidOperationException)
            {
                // The page is on the web: there is nothing to say if no browser opens it.
            }
        }

        /// <summary>… : a folder dialog opened on the folder given; null when the user cancelled.</summary>
        private static string AskFolder(Window owner, string folder)
        {
            bool start = !string.IsNullOrEmpty(folder) && Directory.Exists(folder);
#if NET8_0_OR_GREATER
            var dialog = new OpenFolderDialog { Title = "Choose the folder the tool's own folder is made in" };

            if (start)
            {
                dialog.InitialDirectory = folder;
            }

            return dialog.ShowDialog(owner) == true ? dialog.FolderName : null;
#else
            var dialog = new SaveFileDialog
            {
                Title = "Choose the folder the tool's own folder is made in",
                FileName = "Save the tool in this folder",
                Filter = "Folders|*.folder",
                OverwritePrompt = false,
            };

            if (start)
            {
                dialog.InitialDirectory = folder;
            }

            return dialog.ShowDialog(owner) == true ? Path.GetDirectoryName(dialog.FileName) : null;
#endif
        }

        private void OnHeaderDown(object sender, MouseButtonEventArgs e)
        {
            if (e.ButtonState == MouseButtonState.Pressed && IsLoaded)
            {
                DragMove();
            }
        }

        private void OnClose(object sender, RoutedEventArgs e) => DialogResult = true;

        private void OnCloseRequested(object sender, EventArgs e) => DialogResult = true;
    }
}
