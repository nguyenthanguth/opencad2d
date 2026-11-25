using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Threading;
using System.Windows.Media;
using GeometryHelper;

namespace OpenCAD2D.Wpf
{
    /// <summary>What a publish did: the PDF files written, the sheets printed, the sheets passed over, and one line for each thing that happened, as the command line says them.</summary>
    public sealed class CadPublishResult
    {
        private readonly List<string> _files = new List<string>();
        private readonly List<string> _messages = new List<string>();

        /// <summary>Gets the PDF files written, in the order they were written.</summary>
        public IReadOnlyList<string> Files => _files;

        /// <summary>Gets the lines to tell on the command line, in order: a file written, a sheet printed, a sheet passed over, a file that could not be written.</summary>
        public IReadOnlyList<string> Messages => _messages;

        /// <summary>Gets the number of sheets written to PDF files.</summary>
        public int Written { get; internal set; }

        /// <summary>Gets the number of sheets sent to a printer.</summary>
        public int Printed { get; internal set; }

        /// <summary>Gets the number of sheets passed over or lost to a failure.</summary>
        public int Failed { get; internal set; }

        internal void Say(string message) => _messages.Add(message);

        internal void Wrote(string file) => _files.Add(file);
    }

    /// <summary>Where a publish is: the sheet it is drawing, which of how many, for a progress window.</summary>
    public sealed class CadPublishProgress
    {
        internal CadPublishProgress(int sheet, int count, string name)
        {
            Sheet = sheet;
            Count = count;
            Name = name;
        }

        /// <summary>Gets the place of the sheet being drawn among those to publish, counted from 1.</summary>
        public int Sheet { get; }

        /// <summary>Gets the number of sheets to publish: those the plan does not pass over.</summary>
        public int Count { get; }

        /// <summary>Gets the sheet's name in the list.</summary>
        public string Name { get; }
    }

    /// <summary>
    /// Carries out a publish: the steps of a sheet list (<see cref="CadPublishList.Plan"/>) through the presenter's own
    /// writers. The sheets of a PDF go to <see cref="CadPdfWriter.WritePages(string, IReadOnlyList{CadDocument}, IReadOnlyList{CadPlotJob})"/>,
    /// one file for all of them or a file each, a page the size of its own sheet; a sheet for a printer is drawn as a visual
    /// (<see cref="CadPrintWriter"/>) and handed to the printer, on the sheet of its list nearest the size the page setup asks.
    /// A file or a sheet that fails is told in <see cref="CadPublishResult.Messages"/> and does not stop the sheets after it.
    /// </summary>
    public static class CadPublisher
    {
        /// <summary>
        /// Publishes a sheet list. A list of a drawing open and two layouts, ISO A3 and ISO A1, and a file added with one more
        /// layout is a PDF of three pages of 420 by 297, 841 by 594 and the third's size, when the options say multi-sheet.
        /// </summary>
        /// <param name="list">The sheet list and its options.</param>
        /// <param name="printers">The printers of Windows this computer has, for the sheets whose page setup names one; null for none.</param>
        /// <returns>What was done.</returns>
        /// <exception cref="ArgumentNullException">Thrown when the list is null.</exception>
        public static CadPublishResult Publish(CadPublishList list, IEnumerable<IPlotPrinter> printers) =>
            Publish(list, printers, null, CancellationToken.None);

        /// <summary>
        /// Publishes a sheet list as <see cref="Publish(CadPublishList, IEnumerable{IPlotPrinter})"/> does, telling the progress
        /// before each sheet is drawn and stopping where cancelled. A cancel stops at the next sheet: the sheets already written
        /// stay, a PDF of several sheets that is not finished is not written, and the report says the publish was canceled. Where
        /// <see cref="CadPublishOptions.CreateBookmarks"/> is on, each PDF carries the outline of AutoCAD's published sheets: a
        /// bookmark <c>Sheets and Views</c> and under it one for each page, named as the sheet is in the list.
        /// </summary>
        /// <param name="list">The sheet list and its options.</param>
        /// <param name="printers">The printers of Windows this computer has, for the sheets whose page setup names one; null for none.</param>
        /// <param name="progress">Told of each sheet before it is drawn; null for none. It is called on the thread that publishes.</param>
        /// <param name="cancel">Asked before each sheet.</param>
        /// <returns>What was done.</returns>
        /// <exception cref="ArgumentNullException">Thrown when the list is null.</exception>
        public static CadPublishResult Publish(
            CadPublishList list,
            IEnumerable<IPlotPrinter> printers,
            IProgress<CadPublishProgress> progress,
            CancellationToken cancel
        )
        {
            if (list == null)
            {
                throw new ArgumentNullException(nameof(list));
            }

            List<IPlotPrinter> known = (printers ?? Array.Empty<IPlotPrinter>()).Where(p => p != null).ToList();
            IReadOnlyList<CadPublishStep> steps = list.Plan(known.Select(p => p.Name));
            var result = new CadPublishResult();
            var written = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            int total = steps.Count(s => s.Skipped == null);
            int done = 0;

            try
            {
                foreach (CadPublishStep step in steps)
                {
                    cancel.ThrowIfCancellationRequested();

                    if (step.Skipped != null)
                    {
                        result.Failed++;
                        result.Say(step.Sheet.Name + ": " + step.Skipped.TrimEnd('.') + ". Not published.");
                    }
                    else if (step.Printer != null)
                    {
                        progress?.Report(new CadPublishProgress(done + 1, total, step.Sheet.Name));
                        Print(list, step, known, result);
                        done++;
                    }
                    else if (written.Add(step.File))
                    {
                        List<CadPublishStep> pages = steps
                            .Where(s => s.File != null && SameFile(s.File, step.File))
                            .ToList();
                        int before = done;
                        WritePdf(list, step.File, pages, result, i =>
                        {
                            cancel.ThrowIfCancellationRequested();
                            progress?.Report(new CadPublishProgress(before + i + 1, total, pages[i].Sheet.Name));
                        });
                        done += pages.Count;
                    }
                }
            }
            catch (OperationCanceledException)
            {
                result.Say("The publishing operation was canceled.");
                return result;
            }

            result.Say(
                "Publish complete: "
                    + Count(result.Written + result.Printed, "sheet")
                    + " published"
                    + (result.Failed == 0 ? string.Empty : ", " + Count(result.Failed, "sheet") + " not published")
                    + "."
            );
            return result;
        }

        private static bool SameFile(string a, string b) => string.Equals(a, b, StringComparison.OrdinalIgnoreCase);

        private static string Count(int n, string what) =>
            n.ToString(CultureInfo.InvariantCulture) + " " + what + (n == 1 ? string.Empty : "s");

        private static void WritePdf(
            CadPublishList list,
            string file,
            List<CadPublishStep> pages,
            CadPublishResult result,
            Action<int> beforePage
        )
        {
            try
            {
                string folder = Path.GetDirectoryName(file);

                if (!string.IsNullOrEmpty(folder))
                {
                    Directory.CreateDirectory(folder);
                }

                CadPdfWriter.WritePages(
                    file,
                    pages.Select(p => p.Sheet.Document).ToList(),
                    pages.Select(p => new CadPlotJob(p.Sheet.Space, p.Settings, DisplayBox.Empty, file)).ToList(),
                    list.Options.CreateBookmarks ? Bookmarks(pages) : null,
                    beforePage
                );
                result.Written += pages.Count;
                result.Wrote(file);
                result.Say("Published " + Count(pages.Count, "sheet") + " to " + file + ".");
            }
            catch (Exception exception) when (IsPublishFailure(exception))
            {
                GeometryHelperLog.Warn("CadPublisher: " + file + " was not written.", exception);
                result.Failed += pages.Count;
                result.Say(file + " was not written: " + exception.Message);
            }
        }

        /// <summary>
        /// The outline AutoCAD writes for published sheets: one bookmark, <c>Sheets and Views</c>, going nowhere, and under it a
        /// bookmark for each page, named as its sheet is in the list.
        /// </summary>
        private static List<CadPdfBookmark> Bookmarks(List<CadPublishStep> pages)
        {
            var root = new CadPdfBookmark("Sheets and Views", -1);

            for (int i = 0; i < pages.Count; i++)
            {
                root.Children.Add(new CadPdfBookmark(pages[i].Sheet.Name, i));
            }

            return new List<CadPdfBookmark> { root };
        }

        private static void Print(
            CadPublishList list,
            CadPublishStep step,
            List<IPlotPrinter> printers,
            CadPublishResult result
        )
        {
            CadPublishSheet sheet = step.Sheet;

            try
            {
                IPlotPrinter printer = printers.First(p =>
                    string.Equals(p.Name, step.Printer, StringComparison.OrdinalIgnoreCase)
                );
                IReadOnlyList<CadPaper> papers = printer.GetPapers();
                step.Settings.Paper = PlotDialogViewModel.ClosestPaper(
                    papers == null || papers.Count == 0 ? CadPaper.Standard : papers,
                    step.Settings.Paper.IsPixels ? CadPaper.Default : step.Settings.Paper
                );
                DrawingVisual visual = CadPrintWriter.CreatePage(
                    sheet.Document,
                    sheet.Space,
                    step.Settings,
                    DisplayBox.Empty,
                    out CadPlotPage page
                );
                int copies = list.Options.Copies;
                printer.Print(
                    new PlotPrintJob(sheet.Name, step.Settings.Paper, page.SheetWidth, page.SheetHeight, copies, visual)
                );
                result.Printed++;
                result.Say(
                    "Printed "
                        + sheet.Name
                        + " on "
                        + printer.Name
                        + (
                            copies == 1
                                ? string.Empty
                                : ", " + copies.ToString(CultureInfo.InvariantCulture) + " copies"
                        )
                        + "."
                );
            }
            catch (Exception exception) when (IsPublishFailure(exception))
            {
                GeometryHelperLog.Warn("CadPublisher: " + sheet.Name + " was not printed.", exception);
                result.Failed++;
                result.Say(sheet.Name + " was not printed: " + exception.Message);
            }
        }

        private static bool IsPublishFailure(Exception exception) =>
            exception is InvalidOperationException
            || exception is IOException
            || exception is UnauthorizedAccessException
            || exception is ArgumentException;
    }
}
