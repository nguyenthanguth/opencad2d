using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text.RegularExpressions;
using GeometryHelper;
using GeometryHelper.Dxf;
using GeometryHelper.Geometry;
using OpenCAD2D.Remote;

namespace OpenCAD2D
{
    /// <summary>
    /// How a layout is set up, as <see cref="CadLayouts"/> reads it: the sheet, what is plotted, the scale and the plot style.
    /// A layout of an A3 sheet plotted at 1:50 reads as paper <c>ISO A3</c>, 420 by 297, scale <c>1:50</c>.
    /// </summary>
    public sealed class CadLayoutInfo
    {
        internal CadLayoutInfo(
            string name,
            bool isModel,
            string paper,
            double width,
            double height,
            bool landscape,
            string units,
            string area,
            string scale,
            string plotStyle,
            int viewports
        )
        {
            Name = name;
            IsModel = isModel;
            Paper = paper;
            Width = width;
            Height = height;
            Landscape = landscape;
            Units = units;
            Area = area;
            Scale = scale;
            PlotStyle = plotStyle;
            Viewports = viewports;
        }

        /// <summary>Gets the name of the layout: <c>Model</c> or the name on its tab.</summary>
        public string Name { get; }

        /// <summary>Gets whether it is model space.</summary>
        public bool IsModel { get; }

        /// <summary>Gets the name of the sheet, as <c>ISO A3</c>.</summary>
        public string Paper { get; }

        /// <summary>Gets the width of the sheet as plotted, in millimetres: 420 for A3 on its side.</summary>
        public double Width { get; }

        /// <summary>Gets the height of the sheet as plotted, in millimetres: 297 for A3 on its side.</summary>
        public double Height { get; }

        /// <summary>Gets whether the long side of the sheet runs across.</summary>
        public bool Landscape { get; }

        /// <summary>Gets the unit of the sheet: <c>mm</c>, or <c>in</c> or <c>px</c> for a drawing from another program.</summary>
        public string Units { get; }

        /// <summary>Gets what is plotted: <c>layout</c>, <c>extents</c>, <c>display</c>, <c>limits</c>, <c>window</c> or <c>view</c>.</summary>
        public string Area { get; }

        /// <summary>Gets the scale as <c>1:50</c> or <c>2:1</c> (paper to drawing); null where the plot is fitted to the sheet.</summary>
        public string Scale { get; }

        /// <summary>Gets the plot style table by the name of its file, as <c>monochrome.ctb</c>; empty for none.</summary>
        public string PlotStyle { get; }

        /// <summary>Gets how many viewports the layout shows model space through; the one of the sheet itself is not counted. 0 for model space.</summary>
        public int Viewports { get; }

        /// <summary>Writes the setup as one JSON object, its members in a fixed order: the text the remote channel and the Python package read.</summary>
        /// <returns>The JSON text.</returns>
        public string ToJson() => RemoteJson.Write(ToObject());

        internal JsonObject ToObject() =>
            new JsonObject()
                .Set("name", Name)
                .Set("model", IsModel)
                .Set("paper", Paper)
                .Set("width", Width)
                .Set("height", Height)
                .Set("landscape", Landscape)
                .Set("units", Units)
                .Set("area", Area)
                .Set("scale", Scale)
                .Set("plotStyle", PlotStyle)
                .Set("viewports", (long)Viewports);
    }

    /// <summary>
    /// Makes layouts and sets them up from code: a new layout on a sheet, its page setup (sheet, turn, what is plotted, scale and
    /// plot style) and viewports at a scale, the work of LAYOUT New, the page setup and MVIEW with a scale given. Each call is
    /// one undo step. A script that lays a sheet out, the Python package and the remote channel use it.
    /// </summary>
    public static class CadLayouts
    {
        private static readonly Regex Ratio = new Regex(
            @"^(\d+(?:\.\d+)?)\s*:\s*(\d+(?:\.\d+)?)$",
            RegexOptions.CultureInvariant
        );

        private static readonly Regex Number = new Regex(@"^\d+(?:\.\d+)?$", RegexOptions.CultureInvariant);

        /// <summary>Reads how every layout of a drawing is set up, model space first, then the tabs in their order.</summary>
        /// <param name="document">The drawing.</param>
        /// <returns>One entry for each space.</returns>
        /// <exception cref="ArgumentNullException">Thrown when the drawing is null.</exception>
        public static IReadOnlyList<CadLayoutInfo> List(CadDocument document)
        {
            if (document == null)
            {
                throw new ArgumentNullException(nameof(document));
            }

            return document.Spaces.Select(space => Describe(document, space)).ToList();
        }

        /// <summary>
        /// Adds a layout and sets its sheet, as LAYOUT New and the page setup do: the sheet of the paper given on its side or turned,
        /// what is plotted the layout itself at 1:1. It is not made the layout shown. One undo step.
        /// </summary>
        /// <param name="document">The drawing.</param>
        /// <param name="name">The name of the tab: not <c>Model</c>, not one the drawing has (letters of either case the same), without any of <c>&lt; &gt; / \ " : ; ? * | , = `</c>.</param>
        /// <param name="paper">The sheet, as <c>ISO A3</c>, <c>ISO_A3</c> or <c>A3</c> (see <see cref="CadPaper.Standard"/>); ISO A3 where null.</param>
        /// <param name="landscape">True for the long side across, false for the sheet turned.</param>
        /// <returns>How the new layout is set up.</returns>
        /// <exception cref="ArgumentNullException">Thrown when the drawing or the name is null.</exception>
        /// <exception cref="ArgumentException">Thrown when the name does not fit or the sheet is not one the plotter lists.</exception>
        public static CadLayoutInfo Add(CadDocument document, string name, string paper, bool landscape)
        {
            if (document == null)
            {
                throw new ArgumentNullException(nameof(document));
            }

            if (name == null)
            {
                throw new ArgumentNullException(nameof(name));
            }

            string label = name.Trim();

            if (
                label.Length == 0
                || label.Length > 255
                || label.IndexOfAny(CadCommands.LayoutBadCharacters.ToCharArray()) >= 0
            )
            {
                throw new ArgumentException(
                    "A layout needs a name of 1 to 255 characters, without any of "
                        + CadCommands.LayoutBadCharacters
                        + " in it: "
                        + name
                        + " is not one."
                );
            }

            if (document.Spaces.Any(s => string.Equals(s.Name, label, StringComparison.OrdinalIgnoreCase)))
            {
                throw new ArgumentException("The drawing has a layout named " + label + " already.");
            }

            CadPaper sheet = Paper(paper ?? CadPaper.Default.Name);
            bool own = !document.IsInGroup;
            document.BeginGroup("LAYOUT");

            try
            {
                CadEditResult added = document.ApplyEdit(
                    "LAYOUT",
                    new[] { new CadEditRecord(CadEditRecordKind.TableChanged, null, "New layout", null, label) },
                    s => CadCommands.AddLayout(s.Database, label)
                );

                if (!added.Succeeded)
                {
                    throw new InvalidOperationException(added.Message);
                }

                CadSpace space = Find(document, label);
                CadPlotSettings settings = CadPlotSettings.Read(document, space);
                settings.Paper = sheet;
                settings.Landscape = landscape;
                settings.Area = CadPlotArea.Layout;
                settings.FitToPaper = false;
                settings.PaperUnits = 1.0;
                settings.DrawingUnits = 1.0;
                Written(settings.ApplyTo(document, space));
            }
            catch
            {
                if (own)
                {
                    document.CancelGroup();
                }
                else
                {
                    document.EndGroup();
                }

                throw;
            }

            document.EndGroup();
            return Describe(document, Find(document, label));
        }

        /// <summary>
        /// Changes the page setup of a layout, as the Page Setup dialog does: only what is given is changed. One undo step.
        /// </summary>
        /// <param name="document">The drawing.</param>
        /// <param name="layout">The layout, or <c>Model</c>.</param>
        /// <param name="paper">The sheet, as <see cref="Add"/> takes it; null to keep it.</param>
        /// <param name="orientation"><c>landscape</c> or <c>portrait</c>; null to keep it.</param>
        /// <param name="area">What is plotted: <c>layout</c>, <c>extents</c>, <c>display</c> or <c>limits</c>; null to keep it.</param>
        /// <param name="scale">The scale as <c>1:50</c>, <c>2:1</c> or a number of paper millimetres to the drawing unit (<c>0.02</c>), or <c>fit</c>; null to keep it.</param>
        /// <param name="plotStyle">The plot style table by the name of its file, empty for none; null to keep it.</param>
        /// <returns>How the layout is set up now.</returns>
        /// <exception cref="ArgumentNullException">Thrown when the drawing or the layout is null.</exception>
        /// <exception cref="ArgumentException">Thrown when the layout is not in the drawing, or a value is not one of those listed.</exception>
        public static CadLayoutInfo SetPageSetup(
            CadDocument document,
            string layout,
            string paper,
            string orientation,
            string area,
            string scale,
            string plotStyle
        )
        {
            if (document == null)
            {
                throw new ArgumentNullException(nameof(document));
            }

            CadSpace space = Find(document, layout);
            CadPlotSettings settings = CadPlotSettings.Read(document, space);

            if (paper != null)
            {
                settings.Paper = Paper(paper);
            }

            if (orientation != null)
            {
                settings.Landscape = Choice(orientation, "orientation", "landscape", "portrait") == "landscape";
            }

            if (area != null)
            {
                string chosen = Choice(area, "area", "layout", "extents", "display", "limits");
                settings.Area = (CadPlotArea)Enum.Parse(typeof(CadPlotArea), chosen, true);
            }

            if (scale != null)
            {
                if (string.Equals(scale.Trim(), "fit", StringComparison.OrdinalIgnoreCase))
                {
                    settings.FitToPaper = true;
                }
                else
                {
                    ParseScale(scale, out double sheet, out double units);
                    settings.FitToPaper = false;
                    settings.PaperUnits = sheet;
                    settings.DrawingUnits = units;
                }
            }

            if (plotStyle != null)
            {
                settings.StyleSheet = plotStyle;
            }

            Written(settings.ApplyTo(document, space));
            return Describe(document, space);
        }

        /// <summary>
        /// Adds a viewport to a layout that shows model space at a scale, as MVIEW with two corners and the scale of ZOOM nXP:
        /// the frame is the viewport's rectangle on the sheet, the view is centred on a point of model space and shows
        /// <paramref name="scale"/> paper millimetres to the unit, and the layers named are frozen in this viewport only. A frame
        /// 200 wide and 100 high at 1:50 shows 10 000 by 5 000 units. One undo step.
        /// </summary>
        /// <param name="document">The drawing.</param>
        /// <param name="layout">The layout, not Model.</param>
        /// <param name="frame">The rectangle on the sheet, <c>[x1, y1, x2, y2]</c> in millimetres, any two opposite corners.</param>
        /// <param name="scale">The scale as <c>1:50</c>, <c>2:1</c> or paper millimetres to the unit as <c>0.02</c>; not fit.</param>
        /// <param name="center">The point of model space at the middle of the frame, <c>[x, y]</c>; null for the middle of what model space draws, or the origin where it draws nothing.</param>
        /// <param name="frozenLayers">The layers to freeze in this viewport, or null; each must be in the drawing.</param>
        /// <returns>The handle of the viewport.</returns>
        /// <exception cref="ArgumentNullException">Thrown when the drawing, the layout, the frame or the scale is null.</exception>
        /// <exception cref="ArgumentException">Thrown when the layout is not a sheet of the drawing, the frame has no width or height, the scale is not above nought, the centre is not two finite numbers or a layer is not in the drawing.</exception>
        public static string AddViewport(
            CadDocument document,
            string layout,
            double[] frame,
            string scale,
            double[] center,
            IEnumerable<string> frozenLayers
        )
        {
            if (document == null)
            {
                throw new ArgumentNullException(nameof(document));
            }

            if (frame == null)
            {
                throw new ArgumentNullException(nameof(frame));
            }

            if (scale == null)
            {
                throw new ArgumentNullException(nameof(scale));
            }

            CadSpace space = Find(document, layout);

            if (space.IsModel || space.Layout == null)
            {
                throw new ArgumentException("Model is not a sheet: a viewport goes on a layout.");
            }

            if (frame.Length != 4 || frame.Any(n => double.IsNaN(n) || double.IsInfinity(n)))
            {
                throw new ArgumentException("A frame is four finite numbers, [x1, y1, x2, y2] in millimetres.");
            }

            var box = new DisplayBox(
                Math.Min(frame[0], frame[2]),
                Math.Min(frame[1], frame[3]),
                Math.Max(frame[0], frame[2]),
                Math.Max(frame[1], frame[3])
            );

            if (!(box.Width > Tolerance.DefaultEqualPoint) || !(box.Height > Tolerance.DefaultEqualPoint))
            {
                throw new ArgumentException("The frame of a viewport has a width and a height.");
            }

            if (string.Equals(scale.Trim(), "fit", StringComparison.OrdinalIgnoreCase))
            {
                throw new ArgumentException("A viewport has a scale, as 1:50: it is not fitted.");
            }

            ParseScale(scale, out double sheet, out double units);
            double viewHeight = box.Height / (sheet / units);

            if (center != null && (center.Length != 2 || center.Any(n => double.IsNaN(n) || double.IsInfinity(n))))
            {
                throw new ArgumentException("The centre is two finite numbers, [x, y].");
            }

            GeoPoint2 viewCenter = center != null ? new GeoPoint2(center[0], center[1]) : ModelMiddle(document);
            List<string> names = FrozenNames(document, frozenLayers);
            string handle = null;
            bool own = !document.IsInGroup;
            document.BeginGroup("MVIEW");

            try
            {
                CadEditResult made = document.ApplyEdit("MVIEW", null, s =>
                {
                    // A layout never shown has no viewport 1 for its sheet yet; AutoCAD gives it one before the first of its own.
                    int before = space.Layout.GetViewports().Count;
                    DxfViewport sheetViewport = space.Layout.Initialize();

                    if (space.Layout.GetViewports().Count > before)
                    {
                        s.Added(space, sheetViewport);
                    }

                    var viewport = new DxfViewport();
                    viewport.SetDatabaseDefaults(s.Database);
                    s.Append(space, viewport);
                    viewport.CenterPoint = new GeoPoint3((box.MinX + box.MaxX) / 2.0, (box.MinY + box.MaxY) / 2.0, 0.0);
                    viewport.Width = box.Width;
                    viewport.Height = box.Height;
                    viewport.ViewCenter = viewCenter;
                    viewport.ViewHeight = viewHeight;
                    viewport.On = true;
                    handle = viewport.Handle;
                });

                if (!made.Succeeded)
                {
                    throw new InvalidOperationException(made.Message);
                }

                if (names.Count > 0)
                {
                    Written(CadViewportLayerEditor.SetFrozen(document, new[] { handle }, names, true, "VPLAYER"));
                }
            }
            catch
            {
                if (own)
                {
                    document.CancelGroup();
                }
                else
                {
                    document.EndGroup();
                }

                throw;
            }

            document.EndGroup();
            return handle;
        }

        /// <summary>
        /// Reads a scale as paper millimetres to drawing units: <c>1:50</c> is 1 to 50, <c>2:1</c> is 2 to 1, and a plain number
        /// is paper millimetres to one unit (<c>0.02</c> is 1:50).
        /// </summary>
        /// <param name="text">The scale.</param>
        /// <param name="paperUnits">The paper millimetres, above nought.</param>
        /// <param name="drawingUnits">The drawing units they stand for, above nought.</param>
        /// <exception cref="ArgumentNullException">Thrown when the text is null.</exception>
        /// <exception cref="ArgumentException">Thrown when the text is not two numbers above nought with a colon, or one number above nought.</exception>
        public static void ParseScale(string text, out double paperUnits, out double drawingUnits)
        {
            if (text == null)
            {
                throw new ArgumentNullException(nameof(text));
            }

            Match ratio = Ratio.Match(text.Trim());
            bool ok;
            paperUnits = 0.0;
            drawingUnits = 1.0;

            if (ratio.Success)
            {
                ok =
                    double.TryParse(
                        ratio.Groups[1].Value,
                        NumberStyles.Float,
                        CultureInfo.InvariantCulture,
                        out paperUnits
                    )
                    && double.TryParse(
                        ratio.Groups[2].Value,
                        NumberStyles.Float,
                        CultureInfo.InvariantCulture,
                        out drawingUnits
                    );
            }
            else
            {
                ok =
                    Number.IsMatch(text.Trim())
                    && double.TryParse(text.Trim(), NumberStyles.Float, CultureInfo.InvariantCulture, out paperUnits);
            }

            if (!ok || !(paperUnits > 0.0) || !(drawingUnits > 0.0) || double.IsInfinity(paperUnits / drawingUnits))
            {
                throw new ArgumentException(
                    "A scale is two numbers above nought with a colon, as 1:50, or one number of paper millimetres to the unit, as 0.02: "
                        + text
                        + " is not."
                );
            }
        }

        /// <summary>The scale as <c>1:50</c> or <c>2:1</c>; null where it is not a number above nought.</summary>
        /// <param name="scale">Paper millimetres to the drawing unit: 0.02 for 1:50.</param>
        /// <returns>The text, rounded to four decimals.</returns>
        public static string ScaleText(double scale)
        {
            if (!(scale > 0.0) || double.IsInfinity(scale))
            {
                return null;
            }

            double units = 1.0 / scale;
            return units >= 1.0
                ? "1:" + Math.Round(units, 4).ToString("0.####", CultureInfo.InvariantCulture)
                : Math.Round(scale, 4).ToString("0.####", CultureInfo.InvariantCulture) + ":1";
        }

        /// <summary>The sheet of a name in the plotter's list: <c>ISO A3</c>, <c>ISO_A3</c> and <c>A3</c> are one, letters of either case the same.</summary>
        /// <param name="name">The name.</param>
        /// <returns>The sheet.</returns>
        /// <exception cref="ArgumentNullException">Thrown when the name is null.</exception>
        /// <exception cref="ArgumentException">Thrown when the plotter has no such sheet; the message lists those it has.</exception>
        public static CadPaper Paper(string name)
        {
            if (name == null)
            {
                throw new ArgumentNullException(nameof(name));
            }

            string wanted = name.Trim().Replace('_', ' ');
            CadPaper found = CadPaper.Standard.FirstOrDefault(p =>
                string.Equals(p.Name, wanted, StringComparison.OrdinalIgnoreCase)
                || string.Equals(p.Name, "ISO " + wanted, StringComparison.OrdinalIgnoreCase)
            );
            return found
                ?? throw new ArgumentException(
                    "The plotter has no sheet "
                        + name
                        + ": its sheets are "
                        + string.Join(", ", CadPaper.Standard.Select(p => p.Name))
                        + "."
                );
        }

        private static CadLayoutInfo Describe(CadDocument document, CadSpace space)
        {
            CadPlotSettings settings = CadPlotSettings.Read(document, space);
            CadPaper paper = settings.Paper;
            double longSide = Math.Max(paper.Width, paper.Height);
            double shortSide = Math.Min(paper.Width, paper.Height);
            DxfLayout layout = CadPlotSettings.LayoutOf(document, space);
            string units =
                layout != null && layout.PlotPaperUnits == DxfPlotPaperUnit.Inches ? "in"
                : layout != null && layout.PlotPaperUnits == DxfPlotPaperUnit.Pixels ? "px"
                : "mm";
            int viewports =
                space.IsModel || space.Block == null
                    ? 0
                    : space.Block.OfType<DxfViewport>().Count(v => !v.IsErased && v.Number > 1);
            return new CadLayoutInfo(
                space.Name,
                space.IsModel,
                paper.Name,
                settings.Landscape ? longSide : shortSide,
                settings.Landscape ? shortSide : longSide,
                settings.Landscape,
                units,
                settings.Area.ToString().ToLowerInvariant(),
                settings.FitToPaper ? null : ScaleText(settings.Scale),
                settings.StyleSheet,
                viewports
            );
        }

        private static CadSpace Find(CadDocument document, string layout)
        {
            if (layout == null)
            {
                throw new ArgumentNullException(nameof(layout));
            }

            return document.Spaces.FirstOrDefault(s =>
                string.Equals(s.Name, layout.Trim(), StringComparison.OrdinalIgnoreCase)
            )
                ?? throw new ArgumentException(
                    "The drawing has no layout named "
                        + layout
                        + ": it has "
                        + string.Join(", ", document.Spaces.Select(s => s.Name))
                        + "."
                );
        }

        private static string Choice(string text, string what, params string[] allowed)
        {
            string found = allowed.FirstOrDefault(a =>
                string.Equals(a, text.Trim(), StringComparison.OrdinalIgnoreCase)
            );
            return found
                ?? throw new ArgumentException(
                    "The " + what + " is one of " + string.Join(", ", allowed) + ": " + text + " is not."
                );
        }

        private static void Written(CadEditResult result)
        {
            if (!result.Succeeded)
            {
                throw new InvalidOperationException(result.Message);
            }
        }

        private static GeoPoint2 ModelMiddle(CadDocument document)
        {
            DisplayBox model = document.GetDisplay(document.ModelSpace).Bounds;
            return model.IsEmpty
                ? new GeoPoint2(0.0, 0.0)
                : new GeoPoint2((model.MinX + model.MaxX) / 2.0, (model.MinY + model.MaxY) / 2.0);
        }

        private static List<string> FrozenNames(CadDocument document, IEnumerable<string> layers)
        {
            var names = new List<string>();

            if (layers == null)
            {
                return names;
            }

            foreach (string layer in layers)
            {
                DxfLayerTableRecord record = document.Database.LayerTable.Has(layer ?? string.Empty)
                    ? document.Database.LayerTable[layer]
                    : throw new ArgumentException("The drawing has no layer " + layer + ".");

                if (!names.Contains(record.Name))
                {
                    names.Add(record.Name);
                }
            }

            return names;
        }
    }
}
