using System;
using System.IO;
using GeometryHelper;
using OpenCAD2D.SheetSets;

namespace OpenCAD2D.Wpf
{
    /// <summary>OPENSHEETSET, NEWSHEETSET, SSMSTATE and SSLOCATE: what the app answers the sheet set requests of the commands and of a drawing that opens.</summary>
    public sealed partial class MainViewModel
    {
        private CadSheetSetOptions _sheetSetOptions;
        private bool _applyingSheetSetOptions;

        /// <summary>
        /// Gets the Sheet Set Manager's variables (SSLOCATE, SSMAUTOOPEN, SSMPOLLTIME, SSMSHEETSTATUS) every drawing's command line
        /// shares, kept in the settings: set in one drawing, changed in all, and saved at once.
        /// </summary>
        public CadSheetSetOptions SheetSetOptions
        {
            get
            {
                if (_sheetSetOptions == null)
                {
                    _sheetSetOptions = new CadSheetSetOptions();
                    _settings.CopySheetSetOptionsTo(_sheetSetOptions);
                    _sheetSetOptions.Changed += (sender, e) =>
                    {
                        if (!_applyingSheetSetOptions)
                        {
                            ChangeSettings(
                                s => s.TakeSheetSetOptionsFrom(_sheetSetOptions),
                                "a Sheet Set Manager variable"
                            );
                        }
                    };
                }

                return _sheetSetOptions;
            }
        }

        /// <summary>Gives the options the values of the settings now in use, as Global settings' Reset and a settings file read late change them.</summary>
        private void ApplySheetSetOptions()
        {
            if (_sheetSetOptions == null)
            {
                return;
            }

            _applyingSheetSetOptions = true;

            try
            {
                _settings.CopySheetSetOptionsTo(_sheetSetOptions);
            }
            finally
            {
                _applyingSheetSetOptions = false;
            }
        }

        /// <summary>
        /// A request of OPENSHEETSET, SSMSTATE or SSLOCATE. SSMSTATE is answered "1" while the palette is shown. A sheet set file
        /// handed over is opened in the Sheet Set Manager, which is shown where it is not (a drawing's set found with SSMAUTOOPEN
        /// on, a set the command line asked for); no file is the Open Sheet Set dialog of the palette. NEWSHEETSET is left to the
        /// command line until the wizard is built.
        /// </summary>
        private void OnSheetSetFileRequested(CadUiRequestEventArgs e)
        {
            if (e.Name == "SSMSTATE")
            {
                e.Answer = IsSheetSetShown ? "1" : "0";
                e.Handled = true;
                return;
            }

            if (e.Name == "NEWSHEETSET")
            {
                OnNewSheetSetRequested(e);
                return;
            }

            var request = e.Payload as CadSheetSetRequest;

            if (request == null)
            {
                return;
            }

            if (request.Path == null)
            {
                if (ShowSheetSetManager == null)
                {
                    return;
                }

                ShowSheetSetManager(SheetSetManager);

                if (SheetSetManager.AskOpenFile == null)
                {
                    return;
                }

                string chosen = SheetSetManager.AskOpenFile();

                if (chosen != null)
                {
                    SheetSetManager.OpenSet(chosen);
                }

                e.Handled = true;
                return;
            }

            e.Handled = SheetSetManager.OpenSet(request.Path);

            if (e.Handled && (request.ShowPalette || e.Name == "OPENSHEETSET") && !IsSheetSetShown)
            {
                ShowSheetSetManager?.Invoke(SheetSetManager);
            }
        }

        /// <summary>Finds the sheet set of a drawing just opened and has the Sheet Set Manager open it (SSLOCATE).</summary>
        private void LocateSheetSetOf(KnownFile file)
        {
            try
            {
                file.Session?.LocateSheetSet();
            }
            catch (Exception ex)
                when (ex is IOException
                    || ex is UnauthorizedAccessException
                    || ex is InvalidOperationException
                    || ex is FormatException
                )
            {
                GeometryHelperLog.Warn("MainViewModel: the sheet set of " + file.Path + " was not looked for.", ex);
            }
        }
    }
}
