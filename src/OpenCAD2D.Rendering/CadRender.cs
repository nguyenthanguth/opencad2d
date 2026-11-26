using System;
using System.Globalization;
using System.IO;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using System.Text;
using System.Text.RegularExpressions;
using GeometryHelper.Dxf;
using OpenCAD2D.Wpf;
using SkiaSharp;

namespace OpenCAD2D
{
    /// <summary>
    /// Draws a drawing of a file to pixels, an SVG or a PDF with no window, as the canvas of the app draws it: the same
    /// renderer, the same fonts and the same colours, so that a picture of a drawing is the pixels the app's canvas shows of
    /// it (with its grid, UCS icon and grips off, which are the canvas's and not the drawing's). It draws with the app's
    /// default settings, never the user's: a picture is the same on every machine that has the same fonts.
    /// </summary>
    /// <remarks>
    /// The size of a picture, where <see cref="CadRenderOptions.Width"/> and <see cref="CadRenderOptions.Height"/> do not
    /// give it: one of the two given, the other follows the aspect of the part drawn, rounded (800 wide of a part 400 by
    /// 300 is 600 high); neither, a layout is its part in millimetres times <see cref="CadRenderOptions.Dpi"/> over 25.4 (an
    /// A3 sheet at 96 dpi is 1 587 by 1 123 pixels; a layout whose paper units are inches is its part in inches times the dpi,
    /// so its A3 sheet of 16.54 by 11.69 inches is the same 1 587 by 1 123), and model space is 1 600 pixels along the longer
    /// side (a drawing 400 by 300 mm is 1 600 by 1 200 pixels). A picture of more than 16 384 pixels a side or 100 000 000
    /// pixels in all is refused: it would hold 400 MB. One picture is drawn at a time in a process; the renderer's caches are
    /// not locked against two threads drawing at once.
    /// </remarks>
    public static class CadRender
    {
        /// <summary>The most pixels along a side of a picture.</summary>
        private const int MostSide = 16384;

        /// <summary>The most pixels in a picture: 400 MB of blue, green, red and alpha.</summary>
        private const long MostPixels = 100000000L;

        /// <summary>The pixels along the longer side of a picture of model space of no size given: the width of <c>--screenshot</c>.</summary>
        private const int ModelSide = 1600;

        /// <summary>The share of the picture left empty on each side where ZOOM Extents fits what is drawn.</summary>
        private const double ExtentsMargin = 0.05;

        private static readonly object Preparing = new object();

        private static readonly ConditionalWeakTable<CadDocument, object> Prepared =
            new ConditionalWeakTable<CadDocument, object>();

        /// <summary>
        /// Sets a drawing up as the app sets one up on opening it, so that a picture of it is the canvas's: the canvas's measure
        /// of TrueType letters (once in the process: MTEXT of several looks is laid out with it), the fonts and support folders
        /// of the default settings, and their default lineweight. A drawing of <see cref="CadDocument.Open(string)"/> draws its
        /// text in the canvas's font until this is done and lays an MTEXT out with every letter 0.6 of its height wide. A
        /// display built before is laid out again, once: preparing a drawing again does nothing. The other members call it
        /// first, so it is needed only to read the bounds or the display of a drawing as the picture sees them.
        /// </summary>
        /// <param name="document">The drawing.</param>
        /// <exception cref="ArgumentNullException">Thrown when the document is null.</exception>
        public static void Prepare(CadDocument document)
        {
            if (document == null)
            {
                throw new ArgumentNullException(nameof(document));
            }

            lock (Preparing)
            {
                bool measured = Equals(
                    CadTrueTypeFonts.MeasureWidth,
                    (Func<string, bool, bool, string, double>)SkiaCanvasRenderer.MeasureWidth
                );
                CadTrueTypeFonts.MeasureWidth = SkiaCanvasRenderer.MeasureWidth;

                if (Prepared.TryGetValue(document, out _))
                {
                    // Only a measure put back by someone else is set again here, and a display built with it is old.
                    if (!measured)
                    {
                        document.Regen();
                    }

                    return;
                }

                Prepared.Add(document, null);
                var settings = new GlobalSettings();
                CadFonts fonts = settings.ToFonts(SkiaCanvasRenderer.HasFamily);
                bool laidOut = !fonts.IsSetAs(document.Fonts);
                document.SupportFolders = settings.SupportSearchFolders;
                document.DefaultLineWeight = settings.DefaultLineweight / 100.0;
                document.Fonts = fonts;

                // Other fonts lay every space out again as they are set; the same fonts do not, and the display built is old.
                if (!laidOut)
                {
                    document.Regen();
                }
            }
        }

        /// <summary>
        /// Draws a space of a drawing and gives its pixels, as the canvas of the app draws it: nothing selected, the drawing's
        /// lineweights shown where its <c>$LWDISPLAY</c> says so, and images read before the picture is drawn. A drawing whose
        /// extents are 400 by 300 mm is a picture of 1 600 by 1 200 pixels, 5 760 000 bytes.
        /// </summary>
        /// <param name="document">The drawing.</param>
        /// <param name="options">The space, the part, the size and the ground; null for the defaults.</param>
        /// <returns>The picture.</returns>
        /// <exception cref="ArgumentNullException">Thrown when the document is null.</exception>
        /// <exception cref="ArgumentException">
        /// Thrown when the layout is not in the drawing, a size is negative, the <see cref="CadRenderOptions.Dpi"/> is not above
        /// nought, the ground is not a colour of 0 to 0xFFFFFF, or the picture is over 16 384 pixels a side or 100 000 000
        /// pixels in all.
        /// </exception>
        public static CadRenderImage Draw(CadDocument document, CadRenderOptions options)
        {
            CadSpace space = Set(document, options, out CadView view, out CanvasOptions canvas);

            using (SKBitmap picture = CadRasterWriter.Draw(document, space, view, canvas, null))
            {
                int width = picture.Width;
                int height = picture.Height;
                var bgra = new byte[picture.RowBytes * height];
                Marshal.Copy(picture.GetPixels(), bgra, 0, bgra.Length);
                var rgb = new byte[width * height * 3];

                for (int y = 0; y < height; y++)
                {
                    int from = y * picture.RowBytes;
                    int to = y * width * 3;

                    for (int x = 0; x < width; x++)
                    {
                        rgb[to + 3 * x] = bgra[from + 4 * x + 2];
                        rgb[to + 3 * x + 1] = bgra[from + 4 * x + 1];
                        rgb[to + 3 * x + 2] = bgra[from + 4 * x];
                    }
                }

                return new CadRenderImage(width, height, rgb);
            }
        }

        /// <summary>
        /// Gives the matrix from the drawing to the pixels of the picture <see cref="Draw"/> makes for the same options: the very
        /// view the renderer draws by, nine numbers of a 3 by 3 row by row, <c>[a, c, e, b, d, f, 0, 0, 1]</c>, so that a point
        /// (x, y) of the space is at the pixel position <c>(a x + c y + e, b x + d y + f)</c>. Pixel (column, row) is the square from
        /// (column, row) to (column + 1, row + 1), row 0 at the top and y of the drawing upward, so the pixel a point falls in is
        /// the floor of both numbers and the centre of a pixel is at column + 0.5. For a drawing 400 by 300 whose extents are drawn
        /// at 1 600 by 1 200 pixels with the margin ZOOM Extents leaves (a scale of 3.6), the point (0, 0) is at (80, 1 140) and
        /// (400, 300) at (1 520, 60). On a layout it maps the paper in millimetres; the model entities a viewport shows have the
        /// viewport's own matrix on top, which is not given here. The inverse is the matrix inverted.
        /// </summary>
        /// <param name="document">The drawing.</param>
        /// <param name="options">The space, the part, the size and the ground; null for the defaults.</param>
        /// <returns>The nine numbers.</returns>
        /// <exception cref="ArgumentNullException">Thrown when the document is null.</exception>
        /// <exception cref="ArgumentException">Thrown for the options <see cref="Draw"/> refuses.</exception>
        public static double[] Transform(CadDocument document, CadRenderOptions options)
        {
            Set(document, options, out CadView view, out _);
            Affine2 m = view.DrawingToScreen;
            return new[] { m.A, m.C, m.E, m.B, m.D, m.F, 0.0, 0.0, 1.0 };
        }

        /// <summary>
        /// Draws a space of a drawing in flat ids and gives, for every pixel, the class of what is drawn on it and the entity that
        /// draws it: the picture <see cref="Draw"/> makes for the same options, as labels for training a vision model. It is the
        /// renderer of the canvas, drawing each item in the colour that is its id with no antialiasing, in the order the drawing draws
        /// them, so what is on top in the picture is on top in the labels; a pixel is of one entity and one class, exactly. A text
        /// under 3 pixels high, as in the picture, is left out; the paper of a layout, the frame of a viewport and raster images are not
        /// labelled. The classes are those of <see cref="CadLabelClass"/>. An INSERT's items are classed by what they are unless
        /// blocks are asked for, when all of them are class 5. The ground option has no effect: the ground is class 0.
        /// </summary>
        /// <param name="document">The drawing.</param>
        /// <param name="options">The space, the part, the size; null for the defaults.</param>
        /// <param name="classes">
        /// The classes to label, from 1 to 5 (<see cref="CadLabelClass"/>); null for the first four, which leave blocks off. A pixel
        /// of a class not given is 0 in both arrays.
        /// </param>
        /// <returns>The labels.</returns>
        /// <exception cref="ArgumentNullException">Thrown when the document is null.</exception>
        /// <exception cref="ArgumentException">
        /// Thrown for the options <see cref="Draw"/> refuses, a class that is not 1 to 5, or a space of more than 2 097 151 entities.
        /// </exception>
        public static CadRenderLabels DrawLabels(CadDocument document, CadRenderOptions options, int[] classes)
        {
            CadSpace space = Set(document, options, out CadView view, out CanvasOptions canvas);
            return CadLabeller.Draw(
                document,
                space,
                view,
                canvas,
                classes
                    ?? new[]
                    {
                        CadLabelClass.Words,
                        CadLabelClass.Dimensions,
                        CadLabelClass.Lines,
                        CadLabelClass.Hatches,
                    }
            );
        }

        /// <summary>
        /// Draws a space of a drawing to a PNG file, replacing a file of the name: the picture of <see cref="Draw"/> as 8-bit
        /// red, green and blue with no alpha, as PNGOUT writes it.
        /// </summary>
        /// <param name="path">The file.</param>
        /// <param name="document">The drawing.</param>
        /// <param name="options">The space, the part, the size and the ground; null for the defaults.</param>
        /// <exception cref="ArgumentNullException">Thrown when the path or the document is null.</exception>
        /// <exception cref="ArgumentException">Thrown for the options <see cref="Draw"/> refuses.</exception>
        /// <exception cref="IOException">Thrown when the file cannot be written.</exception>
        public static void WritePng(string path, CadDocument document, CadRenderOptions options)
        {
            if (path == null)
            {
                throw new ArgumentNullException(nameof(path));
            }

            CadSpace space = Set(document, options, out CadView view, out CanvasOptions canvas);

            using (SKBitmap picture = CadRasterWriter.Draw(document, space, view, canvas, null))
            {
                File.WriteAllBytes(path, CadRasterWriter.Encode(picture, CadImageFormat.Png));
            }
        }

        /// <summary>
        /// Draws a space of a drawing to an SVG file, replacing a file of the name: the picture of <see cref="WritePng"/> in
        /// vectors, its <c>width</c> and <c>height</c> the pixels of the picture, the images in it as PNG data. It is drawn by
        /// the renderer of the canvas, so it keeps the canvas's level of detail at the picture's size: an item under half a
        /// pixel is one dot, a text under 3 pixels high is left out and a hatch whose lines come closer than 2 pixels is a
        /// tint. The PDF of <see cref="WritePdf"/> is the full-detail form.
        /// </summary>
        /// <param name="path">The file.</param>
        /// <param name="document">The drawing.</param>
        /// <param name="options">The space, the part, the size and the ground; null for the defaults.</param>
        /// <exception cref="ArgumentNullException">Thrown when the path or the document is null.</exception>
        /// <exception cref="ArgumentException">Thrown for the options <see cref="Draw"/> refuses.</exception>
        /// <exception cref="IOException">Thrown when the file cannot be written.</exception>
        public static void WriteSvg(string path, CadDocument document, CadRenderOptions options)
        {
            if (path == null)
            {
                throw new ArgumentNullException(nameof(path));
            }

            CadSpace space = Set(document, options, out CadView view, out CanvasOptions canvas);
            int width = (int)view.Width;
            int height = (int)view.Height;
            var drawn = new MemoryStream();

            using (SKCanvas svg = SKSvgCanvas.Create(SKRect.Create((float)view.Width, (float)view.Height), drawn))
            using (var renderer = new SkiaCanvasRenderer())
            {
                renderer.Render(svg, document, space, view, canvas);
                svg.Flush();
            }

            // Skia writes the size of the picture and no viewBox, so that the picture would not scale where it is shown at another size.
            string text = Encoding.UTF8.GetString(drawn.ToArray());
            string box = " viewBox=\"0 0 " + width + " " + height + "\"";
            text = new Regex(@"(<svg[^>]*?\sheight=""[^""]*"")").Replace(text, "$1" + box, 1);
            File.WriteAllBytes(path, new UTF8Encoding(false).GetBytes(text));
        }

        /// <summary>
        /// Plots a space of a drawing to a PDF file of one page, replacing a file of the name, as EXPORTPDF does: the page setup
        /// of the layout (its sheet, scale and plot style), the lines at the lineweights of the plot, text as text, and for
        /// model space the extents. An A3 sheet on its side is 1 190.5512 by 841.8898 points. A space with no sheet recorded in
        /// its plot settings (a drawing made in code, never set up) is fitted to the default sheet, ISO A3, as AutoCAD fits a
        /// page setup it has not been given; one with a sheet is plotted at the scale it records.
        /// </summary>
        /// <param name="path">The file.</param>
        /// <param name="document">The drawing.</param>
        /// <param name="layout">The layout, or "Model", as <see cref="CadRenderOptions.Layout"/> says; null for the space the drawing was left in.</param>
        /// <returns>The plot as it was laid on its sheet.</returns>
        /// <exception cref="ArgumentNullException">Thrown when the path or the document is null.</exception>
        /// <exception cref="ArgumentException">Thrown when the layout is not in the drawing.</exception>
        /// <exception cref="InvalidOperationException">Thrown when the plot area is empty: there is nothing to plot.</exception>
        /// <exception cref="IOException">Thrown when the file cannot be written.</exception>
        public static CadPlotPage WritePdf(string path, CadDocument document, string layout)
        {
            if (path == null)
            {
                throw new ArgumentNullException(nameof(path));
            }

            CadSpace space = Find(document, layout);
            Prepare(document);
            CadPlotSettings settings = CadPlotSettings.Read(document, space);
            settings.UseDevice(CadPlotSettings.PdfDevice);

            // A space never set up (a drawing made in code, or by a program that writes no plot settings) has no sheet recorded and
            // would plot at 1 inch to the unit, off the page: it is fitted to the sheet, as AutoCAD's page setup starts out.
            if (!HasSheet(document, space))
            {
                settings.FitToPaper = true;
            }

            return CadPdfWriter.Write(path, document, space, settings, DisplayBox.Empty);
        }

        /// <summary>Whether the sheet of a layout is drawn in inches: its plot settings say inches and give a paper size.</summary>
        private static bool HasInches(CadSpace space) =>
            space.Layout != null
            && space.Layout.PlotPaperUnits == DxfPlotPaperUnit.Inches
            && space.Layout.PlotPaperSize.X > 0.0
            && space.Layout.PlotPaperSize.Y > 0.0;

        /// <summary>Whether a space has a sheet of paper in its plot settings: a layout's, or model space's in the layout of its name.</summary>
        internal static bool HasSheet(CadDocument document, CadSpace space)
        {
            DxfLayout layout = CadPlotSettings.LayoutOf(document, space);
            return layout != null && layout.PlotPaperSize.X > 0.0 && layout.PlotPaperSize.Y > 0.0;
        }

        /// <summary>Prepares the drawing and finds the space, the view fitted to the picture and the options of the canvas.</summary>
        private static CadSpace Set(
            CadDocument document,
            CadRenderOptions options,
            out CadView view,
            out CanvasOptions canvas
        )
        {
            options = options ?? new CadRenderOptions();

            if (options.Width < 0 || options.Height < 0)
            {
                throw new ArgumentException(
                    "A picture has no negative size: " + options.Width + " by " + options.Height + ".",
                    nameof(options)
                );
            }

            if (!(options.Dpi > 0.0) || double.IsInfinity(options.Dpi))
            {
                throw new ArgumentException(
                    "The pixels to the inch must be above nought: "
                        + options.Dpi.ToString(CultureInfo.InvariantCulture)
                        + ".",
                    nameof(options)
                );
            }

            if (options.Background < 0 || options.Background > 0xFFFFFF)
            {
                throw new ArgumentException(
                    "The ground is a colour of 0 to 0xFFFFFF: " + options.Background + ".",
                    nameof(options)
                );
            }

            CadSpace space = Find(document, options.Layout);
            Prepare(document);
            bool given = !options.Box.IsEmpty;
            DisplayBox box = given ? options.Box : document.GetDisplay(space).Bounds.Union(space.PaperBounds);
            GetSize(options, space, box, out int width, out int height);
            view = new CadView(width, height);
            view.ZoomTo(box, given ? 0.0 : ExtentsMargin);
            canvas = CanvasOf(document, options.Background);
            return space;
        }

        /// <summary>Finds a space by the name of its tab, letters of either case the same; the current space for null.</summary>
        private static CadSpace Find(CadDocument document, string layout)
        {
            if (document == null)
            {
                throw new ArgumentNullException(nameof(document));
            }

            if (layout == null)
            {
                return document.CurrentSpace;
            }

            foreach (CadSpace space in document.Spaces)
            {
                if (string.Equals(space.Name, layout, StringComparison.OrdinalIgnoreCase))
                {
                    return space;
                }
            }

            throw new ArgumentException("The drawing has no layout named " + layout + ".", nameof(layout));
        }

        /// <summary>The size of the picture in pixels by the rules of <see cref="CadRender"/>, within the caps.</summary>
        private static void GetSize(
            CadRenderOptions options,
            CadSpace space,
            DisplayBox box,
            out int width,
            out int height
        )
        {
            // A part with no width or no height is taken as a hair wide, so that its aspect is a number; nothing drawn is 1 600 by 960.
            double w = Math.Max(box.IsEmpty ? ModelSide : box.Width, 1E-9);
            double h = Math.Max(box.IsEmpty ? 960.0 : box.Height, 1E-9);
            double pixelsWide = options.Width;
            double pixelsHigh = options.Height;

            if (options.Width > 0 && options.Height > 0) { }
            else if (options.Width > 0)
            {
                pixelsHigh = Pixels(options.Width * h / w);
            }
            else if (options.Height > 0)
            {
                pixelsWide = Pixels(options.Height * w / h);
            }
            else if (!space.IsModel)
            {
                double perInch = HasInches(space) ? 1.0 : 25.4;
                pixelsWide = Pixels(w * options.Dpi / perInch);
                pixelsHigh = Pixels(h * options.Dpi / perInch);
            }
            else if (w >= h)
            {
                pixelsWide = ModelSide;
                pixelsHigh = Pixels(ModelSide * h / w);
            }
            else
            {
                pixelsHigh = ModelSide;
                pixelsWide = Pixels(ModelSide * w / h);
            }

            if (pixelsWide > MostSide || pixelsHigh > MostSide || pixelsWide * pixelsHigh > MostPixels)
            {
                throw new ArgumentException(
                    "A picture of "
                        + pixelsWide.ToString("0", CultureInfo.InvariantCulture)
                        + " by "
                        + pixelsHigh.ToString("0", CultureInfo.InvariantCulture)
                        + " pixels is over the most, "
                        + MostSide
                        + " a side or "
                        + MostPixels
                        + " pixels in all.",
                    nameof(options)
                );
            }

            width = (int)pixelsWide;
            height = (int)pixelsHigh;
        }

        /// <summary>A length in pixels rounded to a whole number of at least 1.</summary>
        private static double Pixels(double length) => Math.Max(1.0, Math.Round(length, MidpointRounding.AwayFromZero));

        /// <summary>The options of the canvas of the app's default settings, the drawing's own say on lineweights, and a ground.</summary>
        private static CanvasOptions CanvasOf(CadDocument document, int background)
        {
            var settings = new GlobalSettings();
            return new CanvasOptions
            {
                Background = background,
                Paper = settings.Paper,
                BlockEditorBackground = settings.BlockEditorBackground,
                SelectionColor = settings.Selection,
                ShowLineWeights =
                    document.Database.GetHeader("$LWDISPLAY") != null
                        ? document.Database.LineWeightDisplay
                        : settings.ShowLineWeights,
                LineweightScale = settings.LineweightDisplayScale,
                FontName = settings.DrawingFont,
                VisibleLineColor = settings.VisibleLines,
            };
        }
    }
}
