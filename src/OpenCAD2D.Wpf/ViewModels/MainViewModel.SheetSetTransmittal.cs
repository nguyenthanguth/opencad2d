using System;
using System.Collections.Generic;
using System.Globalization;
using GeometryHelper;
using OpenCAD2D.SheetSets;

namespace OpenCAD2D.Wpf
{
    /// <summary>
    /// eTransmit of a sheet set from the Sheet List (A12 5b): the eTransmit item of its menus calls
    /// <see cref="TransmitSheetSet"/>, which shows Create Transmittal over the sheets chosen and, once OK has written the zip, says
    /// on the command line what was written and what was changed in the copies. The window is shown through a function the
    /// workspace sets, so that a test drives it with none.
    /// </summary>
    public sealed partial class MainViewModel
    {
        /// <summary>Gets or sets how the workspace shows Create Transmittal: true when OK was pressed and the zip was written (<see cref="TransmittalViewModel.Result"/>).</summary>
        public Func<TransmittalViewModel, bool> ShowTransmittal { get; set; }

        /// <summary>
        /// eTransmit… of the Sheet List: Create Transmittal over the sheets under the rows chosen (all of the set where none is), the
        /// zip written beside the .dst or where the dialog says. The plot style tables are looked for in the plot style folder of the
        /// document open, or of Global settings where none is; the font files, in its support folders. Says the zip written and the
        /// files, then each change made in the copies.
        /// </summary>
        /// <param name="file">The sheet set's file; it is reloaded first where it has changed on disk.</param>
        /// <param name="selection">The selected sheets, subsets and the set; null or empty for the whole set.</param>
        /// <exception cref="ArgumentNullException">Thrown when the file is null.</exception>
        public void TransmitSheetSet(CadSheetSetFile file, IReadOnlyList<CadSheetComponent> selection)
        {
            if (file == null)
            {
                throw new ArgumentNullException(nameof(file));
            }

            if (ShowTransmittal == null)
            {
                Say("Create Transmittal has no window to be shown in.");
                return;
            }

            try
            {
                string styles = Document?.PlotStyleFolder;
                if (string.IsNullOrWhiteSpace(styles))
                {
                    styles = CadPlotStyleFolder.Resolve(_settings.PlotStyleFolder, SettingsPath);
                }

                var transmittal = new CadTransmittal(file, styles, Document?.SupportFolders);
                var dialog = new TransmittalViewModel(transmittal, selection);

                if (!ShowTransmittal(dialog) || dialog.Result == null)
                {
                    Say("The transmittal was canceled.");
                    return;
                }

                CadTransmittalResult result = dialog.Result;
                Say(
                    "Transmittal written: "
                        + result.ZipPath
                        + " ("
                        + result.FileCount.ToString(CultureInfo.InvariantCulture)
                        + (result.FileCount == 1 ? " file, " : " files, ")
                        + CadTransmittalFile.SizeOf(result.ZipSize)
                        + ")."
                );

                foreach (string change in result.Changes)
                {
                    Say(change);
                }
            }
            catch (Exception exception) when (IsSheetSetPublishFailure(exception))
            {
                GeometryHelperLog.Warn("MainViewModel: the sheet set transmittal was not made.", exception);
                Say("The transmittal failed: " + exception.Message);
            }
        }
    }
}
