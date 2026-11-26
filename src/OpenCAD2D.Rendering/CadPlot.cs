using System;
using System.Linq;
using GeometryHelper.Dxf;
using OpenCAD2D.Wpf;

namespace OpenCAD2D
{
    /// <summary>
    /// Plots a layout to a PDF as a script wants it: the layout's own page setup, or the sheet and scale given for this plot
    /// alone, with the text of an SHX font also written as invisible text so that a PDF reader's search, and a program that
    /// reads the plot back, find the words. <see cref="CadRender.WritePdf"/> plots as the page setup says and no more; this
    /// takes the changes a call may ask for.
    /// </summary>
    public static class CadPlot
    {
        /// <summary>
        /// Plots a space to a PDF file of one page, replacing a file of the name, with the page setup of the layout as it is, or
        /// with the changes asked for, none of which is written into the drawing. A layout plotted at 1:50 on A3 with nothing
        /// asked for is written at 1:50 on A3; a space never set up (no sheet in its plot settings) is fitted to ISO A3; a sheet
        /// given with no scale is fitted to it.
        /// </summary>
        /// <param name="path">The file.</param>
        /// <param name="document">The drawing.</param>
        /// <param name="layout">The layout, or <c>Model</c>; the space the drawing was left in where null.</param>
        /// <param name="paper">A sheet of the plotter's list (<see cref="CadLayouts.Paper"/>) for this plot; null for the layout's own.</param>
        /// <param name="scale">A scale for this plot, <c>1:10</c>, <c>2:1</c> or paper millimetres to the unit as <c>0.1</c>; null for the layout's own.</param>
        /// <param name="fit">True to fit the plot to the sheet; not with a scale.</param>
        /// <returns>The plot as it was laid on its sheet.</returns>
        /// <exception cref="ArgumentNullException">Thrown when the path or the document is null.</exception>
        /// <exception cref="ArgumentException">Thrown when the layout is not in the drawing, the sheet is not one the plotter lists, the scale is not above nought, or a scale and fit are both given.</exception>
        /// <exception cref="InvalidOperationException">Thrown when the plot area is empty: there is nothing to plot.</exception>
        public static CadPlotPage WritePdf(
            string path,
            CadDocument document,
            string layout,
            string paper,
            string scale,
            bool fit
        )
        {
            if (path == null)
            {
                throw new ArgumentNullException(nameof(path));
            }

            if (document == null)
            {
                throw new ArgumentNullException(nameof(document));
            }

            if (scale != null && fit)
            {
                throw new ArgumentException(
                    "Give a scale or fit, not both: a scale is how big the drawing is plotted."
                );
            }

            CadSpace space =
                layout == null
                    ? document.CurrentSpace
                    : document.Spaces.FirstOrDefault(s =>
                        string.Equals(s.Name, layout, StringComparison.OrdinalIgnoreCase)
                    );

            if (space == null)
            {
                throw new ArgumentException("The drawing has no layout named " + layout + ".");
            }

            CadRender.Prepare(document);
            CadPlotSettings settings = CadPlotSettings.Read(document, space);
            settings.UseDevice(CadPlotSettings.PdfDevice);

            // The text of an SHX font is written as text no eye can see over its strokes, so that a reader's search (and a program
            // that reads the plot back) finds the words: PDFSHX. The picture is the same.
            settings.PdfShx = true;

            if (paper != null)
            {
                settings.Paper = CadLayouts.Paper(paper);
            }

            if (scale != null)
            {
                CadLayouts.ParseScale(scale, out double sheet, out double units);
                settings.FitToPaper = false;
                settings.PaperUnits = sheet;
                settings.DrawingUnits = units;
            }
            else if (paper != null || fit || !CadRender.HasSheet(document, space))
            {
                // A sheet changed has no scale that suits it, and a space never set up has no sheet at all: both are fitted, as AutoCAD's page setup starts out.
                settings.FitToPaper = true;
            }

            return CadPdfWriter.Write(path, document, space, settings, DisplayBox.Empty);
        }
    }
}
