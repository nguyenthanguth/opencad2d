using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using GeometryHelper.Dxf;

namespace OpenCAD2D.SheetSets
{
    /// <summary>
    /// The sheets of a sheet set that a publish is to write, as the Sheet List's Publish items choose them (D17): a
    /// <see cref="CadPublishList"/> of the sheets in set order, each named as AutoCAD names a published sheet (its number, a
    /// space and its title, the title alone for a sheet with no number), with the set's output folder, one file or a file each,
    /// and the page setup override if one was picked; and a line for each sheet left out and why. Nothing is written here: the
    /// window's publisher carries the list out (<c>CadPublisher.Publish</c>), as it does for the Publish dialog's list.
    /// </summary>
    /// <remarks>
    /// <para>
    /// A sheet is left out where it is not included for publish (<see cref="CadSheetSetSheet.IncludeForPublish"/>), or sits in a
    /// subset, at any depth, that does not publish its sheets (<see cref="CadSheetSubset.PublishSheets"/>). Both were checked on
    /// AutoCAD 2021: a sheet marked <c>DoNotPlot</c> and the sheets of a subset marked <c>OverrideSheetPublish</c>, a subset
    /// inside it included, were not in the PDF its PUBLISHSHEETSET wrote. A sheet whose drawing is gone stays in the list and
    /// <c>Plan</c> passes it over with its status.
    /// </para>
    /// <para>
    /// The PDF carries the bookmarks of AutoCAD's: one <c>Sheets and Views</c>, and under it a bookmark for each page named as
    /// the sheet is, in the order of the pages; AutoCAD 2021 writes no bookmark for a subset. With a multi-sheet file the pages
    /// are the sheets in set order, or the reverse of it where the set publishes in reverse order
    /// (<see cref="CadSheetSetPublishOptions.ReverseOrder"/>).
    /// </para>
    /// </remarks>
    public sealed class CadSheetSetPublish
    {
        private CadSheetSetPublish(CadPublishList list, string summary, IReadOnlyList<string> skipped)
        {
            List = list;
            Summary = summary;
            Skipped = skipped;
        }

        /// <summary>Gets the list to publish: the sheets in the order they are written, and the options. Set <see cref="CadPublishList.Options"/>' <see cref="CadPublishOptions.PublishTo"/> to <see cref="CadPublishTo.PlotterNamedInPageSetup"/> to publish to the plotter.</summary>
        public CadPublishList List { get; }

        /// <summary>Gets the first line of the report: <c>Publishing Example Tower: 11 sheets, 9 to publish, in set order.</c></summary>
        public string Summary { get; }

        /// <summary>Gets a line for each sheet left out, in set order: <c>Skipped S-501 - Typical Details: not included for publish.</c> Empty where all are published.</summary>
        public IReadOnlyList<string> Skipped { get; }

        /// <summary>
        /// Builds the list for a selection of the Sheet List: the sheets under the selected nodes, in set order, or all of the
        /// set where nothing is selected or the set itself is.
        /// </summary>
        /// <param name="set">The sheet set; its output folder, one file or a file each and publishing order are used. The set belongs to a file as loaded: take it from <see cref="CadSheetSetFile.SheetSet"/> just before.</param>
        /// <param name="selection">The selected sheets, subsets and the set; null or empty for the whole set.</param>
        /// <param name="pageSetupOverride">The name of a page setup of the set's page setup overrides file to publish every sheet with, in place of its own; null or empty for each sheet's own.</param>
        /// <param name="pdfOptions">The PDF options the sheet set PDF dialog set (<see cref="CadPublishOptions.CreateBookmarks"/>, <see cref="CadPublishOptions.IncludeHyperlinks"/> and the file name); a copy is used. Null for the dialog's start: bookmarks and hyperlinks on, the file named by the set.</param>
        /// <param name="current">The drawing open, used as it stands for a sheet that is a layout of it; null for none.</param>
        /// <returns>The publish.</returns>
        /// <exception cref="ArgumentNullException">Thrown when the set is null.</exception>
        /// <exception cref="InvalidOperationException">Thrown when a page setup override is asked for and the set names no overrides file.</exception>
        public static CadSheetSetPublish Create(
            CadSheetSet set,
            IEnumerable<CadSheetComponent> selection,
            string pageSetupOverride,
            CadPublishOptions pdfOptions,
            CadDocument current
        )
        {
            if (set == null)
            {
                throw new ArgumentNullException(nameof(set));
            }

            string folder = set.File.Folder;
            string overrides = null;
            if (!string.IsNullOrEmpty(pageSetupOverride))
            {
                overrides = set.PageSetupOverrides?.Locate(folder);
                if (overrides == null)
                {
                    throw new InvalidOperationException(
                        "The sheet set names no page setup overrides file to take the page setup \""
                            + pageSetupOverride
                            + "\" from."
                    );
                }
            }

            var chosen = new HashSet<string>(
                (selection ?? Enumerable.Empty<CadSheetComponent>()).Where(c => c != null).Select(c => c.Id),
                StringComparer.Ordinal
            );
            bool whole = chosen.Count == 0 || chosen.Contains(set.Id);
            var included = new List<CadSheetSetSheet>();
            var skipped = new List<string>();
            int count = 0;
            Walk(set, whole, null, chosen, included, skipped, ref count);

            CadPublishOptions options = pdfOptions?.Clone()
                ?? new CadPublishOptions { CreateBookmarks = true, IncludeHyperlinks = true };
            options.PublishTo = CadPublishTo.Pdf;
            CadSheetSetPublishOptions saved = set.PublishOptions;
            options.MultiSheet = saved.MultiSheet;
            options.OutputFolder = saved.OutputFolder?.Locate(folder) ?? folder;
            if (string.IsNullOrWhiteSpace(options.FileName))
            {
                options.FileName = CadSheetSetDrawings.AsFileName(set.Name);
            }

            if (saved.ReverseOrder)
            {
                included.Reverse();
            }

            var list = new CadPublishList(current) { Options = options, SheetSetName = set.Name };
            foreach (CadSheetSetSheet sheet in included)
            {
                AddSheet(list, sheet, folder, overrides, pageSetupOverride);
            }

            string summary =
                "Publishing "
                + set.Name
                + ": "
                + Sheets(count)
                + ", "
                + included.Count.ToString(CultureInfo.InvariantCulture)
                + " to publish, in "
                + (saved.ReverseOrder ? "reverse set order" : "set order")
                + ".";
            return new CadSheetSetPublish(list, summary, skipped);
        }

        private static string Sheets(int n) =>
            n.ToString(CultureInfo.InvariantCulture) + (n == 1 ? " sheet" : " sheets");

        /// <summary>Goes through a group in set order: the sheets selected, inside it or chosen themselves, that publish are collected, the others told.</summary>
        private static void Walk(
            CadSheetSubset group,
            bool selected,
            string notPublishing,
            HashSet<string> chosen,
            List<CadSheetSetSheet> included,
            List<string> skipped,
            ref int count
        )
        {
            foreach (CadSheetComponent child in group.Children)
            {
                bool inside = selected || chosen.Contains(child.Id);
                var subset = child as CadSheetSubset;
                if (subset != null)
                {
                    string blocked = notPublishing ?? (subset.PublishSheets ? null : subset.Name);
                    Walk(subset, inside, blocked, chosen, included, skipped, ref count);
                    continue;
                }

                if (!inside)
                {
                    continue;
                }

                var sheet = (CadSheetSetSheet)child;
                count++;
                if (notPublishing != null)
                {
                    skipped.Add(
                        "Skipped " + Name(sheet) + ": the subset " + notPublishing + " does not publish its sheets."
                    );
                }
                else if (!sheet.IncludeForPublish)
                {
                    skipped.Add("Skipped " + Name(sheet) + ": not included for publish.");
                }
                else
                {
                    included.Add(sheet);
                }
            }
        }

        /// <summary>The name of a published sheet: the number, a space and the title; the title alone for a sheet with no number; the layout's name for a sheet with neither.</summary>
        private static string Name(CadSheetSetSheet sheet)
        {
            string name = CadSheetSetDrawings.Joined(sheet.Number, sheet.Title);
            return name.Length > 0 ? name : sheet.Layout?.Name ?? string.Empty;
        }

        private static void AddSheet(
            CadPublishList list,
            CadSheetSetSheet sheet,
            string folder,
            string overrides,
            string pageSetupOverride
        )
        {
            CadDrawingReference reference = sheet.Layout;
            string file = reference?.File.Locate(folder) ?? string.Empty;
            string layout = reference?.Name ?? string.Empty;
            CadDocument drawing = file.Length == 0 ? null : list.DrawingOf(file);
            if (drawing != null)
            {
                // The layout the sheet means is the one with its handle, which may have been renamed since (D7).
                layout = CadSheetSetDrawings.Find(drawing.Database, reference.Handle, reference.Name, out _)?.LayoutName
                    ?? layout;
            }

            CadPublishSheet added = list.AddLayout(file, layout, Name(sheet));
            if (overrides != null && added.IsLoaded)
            {
                string refused = list.SetPageSetup(added, pageSetupOverride, overrides);
                if (refused != null)
                {
                    // As a list file's sheet whose page setup is not there: the plan passes it over with the reason.
                    added.PageSetup = pageSetupOverride;
                    added.PageSetupFile = overrides;
                    added.Status = refused;
                }
            }
        }
    }
}
