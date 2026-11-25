using System;
using System.Collections.Generic;
using GeometryHelper;
using OpenCAD2D.SheetSets;

namespace OpenCAD2D.Wpf
{
    /// <summary>
    /// Publishing a sheet set from the Sheet List (A12 5a): the Publish items of its menus and toolbar call
    /// <see cref="PublishSheetSet"/>, <see cref="PublishSheetSetDialog"/> and <see cref="EditSheetSetPublishOptions"/>. What is
    /// told goes on the command line, as the report of the board: the sheets to publish and those left out, then the files
    /// written. The windows are shown through functions the workspace sets, so that a test drives them with none.
    /// </summary>
    public sealed partial class MainViewModel
    {
        /// <summary>The PDF options of each sheet set's publishes (hyperlinks, bookmarks, file name), as the last Sheet Set PDF Publish Options left them, by the path of its .dst.</summary>
        private readonly Dictionary<string, CadPublishOptions> _sheetSetPdfOptions = new Dictionary<
            string,
            CadPublishOptions
        >(StringComparer.OrdinalIgnoreCase);

        /// <summary>Gets or sets how the workspace shows Sheet Set PDF Publish Options: true when OK was pressed and the options were saved.</summary>
        public Func<SheetSetPublishOptionsViewModel, bool> ShowSheetSetPublishOptions { get; set; }

        /// <summary>
        /// Gets or sets how the workspace asks for the name of the multi-sheet PDF where the set has Prompt for name on: given the
        /// path it would be written to, gives the path chosen, or null for none, which cancels the publish.
        /// </summary>
        public Func<string, string> AskSheetSetPublishFile { get; set; }

        /// <summary>
        /// Gets or sets how the workspace carries a PDF publish out with its progress window: the sheet list and the printers in,
        /// what was done out. Where it is not set the publish runs at once, with no window.
        /// </summary>
        public Func<CadPublishList, IEnumerable<IPlotPrinter>, CadPublishResult> RunSheetSetPublish { get; set; }

        /// <summary>
        /// Publish to PDF and Publish to Plotter of the Sheet List: publishes the sheets under the selected nodes (all of the set
        /// where none is), in set order, as the set's publish options say: one PDF or a file each, the output folder, the file
        /// name asked for, the order reversed. Says the sheets to publish and those left out, then what was written.
        /// </summary>
        /// <param name="file">The sheet set's file; it is reloaded first where it has changed on disk.</param>
        /// <param name="selection">The selected sheets, subsets and the set; null or empty for the whole set.</param>
        /// <param name="to">PDF, or the plotter each sheet's page setup names.</param>
        /// <param name="pageSetupOverride">The name of a page setup of the set's overrides file to publish every sheet with (Publish Using Page Setup Override); null for each sheet's own.</param>
        /// <exception cref="ArgumentNullException">Thrown when the file is null.</exception>
        public void PublishSheetSet(
            CadSheetSetFile file,
            IReadOnlyList<CadSheetComponent> selection,
            CadPublishTo to,
            string pageSetupOverride
        )
        {
            CadSheetSetPublish publish = Prepared(file, selection, to, pageSetupOverride);

            if (publish == null)
            {
                return;
            }

            CadPublishList list = publish.List;

            try
            {
                CadSheetSetPublishOptions saved = file.SheetSet.PublishOptions;

                if (to == CadPublishTo.Pdf && saved.MultiSheet && saved.PromptForName && AskSheetSetPublishFile != null)
                {
                    string asked = AskSheetSetPublishFile(list.MultiSheetFile());

                    if (asked == null)
                    {
                        Say("The publishing operation was canceled.");
                        return;
                    }

                    list.Options.FileName = asked;
                }

                CadPublishResult result =
                    to == CadPublishTo.Pdf && RunSheetSetPublish != null
                        ? RunSheetSetPublish(list, Printers())
                        : CadPublisher.Publish(list, Printers());
                SayPublished(result, list);
            }
            catch (Exception exception) when (IsSheetSetPublishFailure(exception))
            {
                GeometryHelperLog.Warn("MainViewModel: the sheet set publish was not carried out.", exception);
                Say("The publish failed: " + exception.Message);
            }
        }

        /// <summary>
        /// Publish Dialog Box… of the Sheet List: the Publish dialog over the sheets of the selection, in set order, each with its
        /// page setup, to be published, saved as a sheet list or changed there; its sheet list file is marked as a sheet set's
        /// (<c>IsSheetSet=TRUE</c>, <c>SheetSet Name=</c>).
        /// </summary>
        /// <param name="file">The sheet set's file.</param>
        /// <param name="selection">The selected sheets, subsets and the set; null or empty for the whole set.</param>
        /// <exception cref="ArgumentNullException">Thrown when the file is null.</exception>
        public void PublishSheetSetDialog(CadSheetSetFile file, IReadOnlyList<CadSheetComponent> selection)
        {
            CadSheetSetPublish publish = Prepared(file, selection, CadPublishTo.Pdf, null);

            if (publish == null || ShowPublish == null)
            {
                return;
            }

            try
            {
                if (!ShowPublish(new PublishViewModel(publish.List)))
                {
                    Say("The publishing operation was canceled.");
                    return;
                }

                SayPublished(CadPublisher.Publish(publish.List, Printers()), publish.List);
            }
            catch (Exception exception) when (IsSheetSetPublishFailure(exception))
            {
                GeometryHelperLog.Warn("MainViewModel: the sheet set publish was not carried out.", exception);
                Say("The publish failed: " + exception.Message);
            }
        }

        /// <summary>
        /// Sheet Set PDF Publish Options… of the Sheet List: the dialog over the set's PDF options. OK saves Location, Prompt for
        /// name and Multi-sheet file in the set's data file, and Include hyperlinks, Create bookmarks and the file name for the
        /// publishes of this session.
        /// </summary>
        /// <param name="file">The sheet set's file.</param>
        /// <exception cref="ArgumentNullException">Thrown when the file is null.</exception>
        public void EditSheetSetPublishOptions(CadSheetSetFile file)
        {
            if (file == null)
            {
                throw new ArgumentNullException(nameof(file));
            }

            if (ShowSheetSetPublishOptions == null)
            {
                return;
            }

            file.ReloadIfChanged();
            _sheetSetPdfOptions.TryGetValue(file.Path, out CadPublishOptions kept);
            CadPublishOptions options = kept?.Clone();
            var dialog = new SheetSetPublishOptionsViewModel(file, options);

            if (ShowSheetSetPublishOptions(dialog))
            {
                _sheetSetPdfOptions[file.Path] = dialog.Options;
            }
        }

        /// <summary>Builds the publish of a selection and says what is to be published and what is left out; null, with it said, where no sheet is to be published or the page setup override cannot be had.</summary>
        private CadSheetSetPublish Prepared(
            CadSheetSetFile file,
            IReadOnlyList<CadSheetComponent> selection,
            CadPublishTo to,
            string pageSetupOverride
        )
        {
            if (file == null)
            {
                throw new ArgumentNullException(nameof(file));
            }

            try
            {
                file.ReloadIfChanged();
                _sheetSetPdfOptions.TryGetValue(file.Path, out CadPublishOptions kept);
                CadSheetSetPublish publish = CadSheetSetPublish.Create(
                    file.SheetSet,
                    selection,
                    pageSetupOverride,
                    kept,
                    Document
                );
                publish.List.Options.PublishTo = to;
                Say(publish.Summary);

                foreach (string line in publish.Skipped)
                {
                    Say(line);
                }

                if (publish.List.Sheets.Count == 0)
                {
                    Say("There are no sheets to publish.");
                    return null;
                }

                return publish;
            }
            catch (Exception exception) when (IsSheetSetPublishFailure(exception))
            {
                GeometryHelperLog.Warn("MainViewModel: the sheet set publish could not be prepared.", exception);
                Say("The publish failed: " + exception.Message);
                return null;
            }
        }

        private void SayPublished(CadPublishResult result, CadPublishList list)
        {
            foreach (string message in result.Messages)
            {
                Say(message);
            }

            if (list.Options.OpenInViewer)
            {
                foreach (string written in result.Files)
                {
                    OpenInViewer?.Invoke(written);
                }
            }
        }

        private static bool IsSheetSetPublishFailure(Exception exception) =>
            exception is InvalidOperationException
            || exception is System.IO.IOException
            || exception is UnauthorizedAccessException
            || exception is ArgumentException
            || exception is FormatException;
    }
}
