using System;
using System.Collections.Generic;
using System.Linq;

namespace OpenCAD2D.Wpf
{
    /// <summary>The Sheet Set Manager palette of SHEETSET and SHEETSETHIDE.</summary>
    public sealed partial class MainViewModel
    {
        private SheetSetManagerViewModel _sheetSetManager;
        private bool _isSheetSetShown;

        /// <summary>Gets or sets how the workspace shows the Sheet Set Manager palette (SHEETSET), beside the window and left open; the one open is brought forward.</summary>
        public Action<SheetSetManagerViewModel> ShowSheetSetManager { get; set; }

        /// <summary>Gets or sets how the workspace closes the Sheet Set Manager palette (SHEETSETHIDE).</summary>
        public Action CloseSheetSetManager { get; set; }

        /// <summary>Gets whether the Sheet Set Manager palette is open, as its window says (AutoCAD's SSMSTATE).</summary>
        public bool IsSheetSetShown
        {
            get => _isSheetSetShown;
            private set => Set(ref _isSheetSetShown, value);
        }

        /// <summary>
        /// Gets the Sheet Set Manager of this window: the sheet sets it has open and the one current, which stay as they are while
        /// the palette is closed and shown again.
        /// </summary>
        public SheetSetManagerViewModel SheetSetManager =>
            _sheetSetManager ?? (_sheetSetManager = new SheetSetManagerViewModel(this));

        /// <summary>Tells the model the Sheet Set Manager palette is open or closed; the window calls it as it is shown and closed.</summary>
        /// <param name="shown">Whether it is open.</param>
        public void NoteSheetSetShown(bool shown) => IsSheetSetShown = shown;

        /// <summary>A request of SHEETSET or SHEETSETHIDE: the palette shown or closed where the workspace shows it.</summary>
        private void OnSheetSetRequested(CadUiRequestEventArgs e)
        {
            if (e.Name == "SHEETSET" && ShowSheetSetManager != null)
            {
                ShowSheetSetManager(SheetSetManager);
                e.Handled = true;
            }
            else if (e.Name == "SHEETSETHIDE" && CloseSheetSetManager != null)
            {
                CloseSheetSetManager();
                e.Handled = true;
            }
        }

        /// <summary>Puts a sheet set data file first in the sheet set control's Recent list; the settings file keeps it.</summary>
        /// <param name="path">The full path of the .dst.</param>
        internal void NoteRecentSheetSet(string path)
        {
            _settings.NoteRecentSheetSet(path);
            SettingsChanged?.Invoke(this, EventArgs.Empty);
        }

        /// <summary>The document that shows a drawing file, or null where it is not open.</summary>
        internal CadDocument OpenDocumentOf(string path) =>
            OpenDocuments.FirstOrDefault(d => d.FileName != null && SamePath(d.FileName, path));

        /// <summary>
        /// Closes a drawing a sheet set operation is about to rewrite on disk: changes not saved are saved first, with no
        /// question, then the document is let go as CLOSE lets it go.
        /// </summary>
        /// <param name="document">The document.</param>
        /// <returns>Whether it is closed; false where it could not be saved, and then it stays open.</returns>
        internal bool CloseForSheetSet(CadDocument document)
        {
            KnownFile file = _files.Values.FirstOrDefault(f => f.Document == document);

            if (file?.Document == null)
            {
                return true;
            }

            KnownFile shown = _current;
            EndCommands(file.Session);

            if (file.Document.IsModified && !SaveFile(file))
            {
                ShowAgain(shown, file);
                return false;
            }

            ShowAgain(shown, file);
            Release(file);
            return true;
        }
    }
}
