using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using GeometryHelper.Dxf;
using GeometryHelper.Geometry;
using ModelContextProtocol;
using ModelContextProtocol.Server;
using OpenCAD2D.Remote;
using OpenCAD2D.Wpf;

namespace OpenCAD2D.Mcp
{
    /// <summary>The tools that put the drawing on paper or in another file, clean it, check it and move what is shown: plot, SVG, save as, purge, audit and zoom.</summary>
    internal sealed partial class DrawingTools
    {
        private const string OverwriteText =
            "True to replace a file that is there already; a file that is there is refused otherwise.";

        [McpServerTool(Name = "plot_pdf", ReadOnly = false, Destructive = true, Idempotent = true, OpenWorld = false)]
        [Description(
            "Plots a layout (or model space) to a PDF file of one page, as PLOT to the PDF plotter does: the layout's own page setup (its sheet and scale), lines at their lineweights, text as text. A space never set up is fitted to an A3 sheet. paper, scale or fit change the setup for this plot only and are not written into the drawing: paper is a sheet of the plotter's list (A0 to A4, ANSI A to E) and the plot is fitted to it unless a scale is given too; scale is 1:10 or 2:1, on the sheet the layout has or the paper given; fit fills the sheet. The text of an SHX font is also written as invisible text over its strokes, so that a PDF reader's search and a program that reads the plot back find the words. An attached server plots the app's drawing as it stands, changes not saved included, in its own process; the app is not touched. Refused with --read-only."
        )]
        public PlotResult PlotPdf(
            [Description("The PDF file to write; .pdf is added where there is no extension.")] string path,
            [Description("The layout, or Model; the layout the drawing is in now where left out.")]
                string layout = null,
            [Description("The sheet, as A3 or ISO A3.")] string paper = null,
            [Description("The scale, as 1:10 (paper millimetres to drawing units).")] string scale = null,
            [Description("True to fit the plot to the sheet.")] bool fit = false,
            [Description(OverwriteText)] bool overwrite = false
        ) => _session.Run("plot_pdf", true, drawing =>
            {
                string full = McpKit.Target(path, ".pdf", overwrite, drawing.Path);
                string space = layout == null ? drawing.CurrentLayout : Layout(drawing, layout);
                CadDocument document = McpKit.Snapshot(drawing);
                CadPlotPage page = Plot(document, full, space, paper, scale, fit);
                return new PlotResult
                {
                    Path = full,
                    Layout = space,
                    Bytes = new FileInfo(full).Length,
                    Sheet = new[] { EntityDescriber.R(page.SheetWidth), EntityDescriber.R(page.SheetHeight) },
                    Scale = ScaleText(page.Scale),
                    Area = page.Area.IsEmpty
                        ? null
                        : new[]
                        {
                            EntityDescriber.R(page.Area.MinX),
                            EntityDescriber.R(page.Area.MinY),
                            EntityDescriber.R(page.Area.MaxX),
                            EntityDescriber.R(page.Area.MaxY),
                        },
                };
            });

        /// <summary>Plots a space as CadRender.WritePdf does, with the page setup of the layout as it is, or with the changes asked for.</summary>
        private static CadPlotPage Plot(
            CadDocument document,
            string path,
            string layout,
            string paper,
            string scale,
            bool fit
        )
        {
            if (scale != null && fit)
            {
                throw new ArgumentException(
                    "Give a scale or fit, not both: a scale is how big the drawing is plotted."
                );
            }

            CadSpace space = document.Spaces.FirstOrDefault(s =>
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
                settings.Paper = Sheet(paper);
            }

            if (scale != null)
            {
                Match ratio = Regex.Match(
                    scale.Trim(),
                    @"^(\d+(?:\.\d+)?)\s*:\s*(\d+(?:\.\d+)?)$",
                    RegexOptions.CultureInvariant
                );

                if (
                    !ratio.Success
                    || !double.TryParse(
                        ratio.Groups[1].Value,
                        NumberStyles.Float,
                        CultureInfo.InvariantCulture,
                        out double sheet
                    )
                    || !double.TryParse(
                        ratio.Groups[2].Value,
                        NumberStyles.Float,
                        CultureInfo.InvariantCulture,
                        out double units
                    )
                    || !(sheet > 0.0)
                    || !(units > 0.0)
                )
                {
                    throw new ArgumentException(
                        "The scale is two numbers above nought with a colon, as 1:10: " + scale + " is not."
                    );
                }

                settings.FitToPaper = false;
                settings.PaperUnits = sheet;
                settings.DrawingUnits = units;
            }
            else if (paper != null || fit || !HasSheet(document, space))
            {
                // A sheet changed has no scale that suits it, and a space never set up has no sheet at all: both are fitted, as AutoCAD's page setup starts out.
                settings.FitToPaper = true;
            }

            return CadPdfWriter.Write(path, document, space, settings, DisplayBox.Empty);
        }

        /// <summary>Whether a space has a sheet of paper in its plot settings: a layout's, or model space's in the layout of its name.</summary>
        private static bool HasSheet(CadDocument document, CadSpace space)
        {
            DxfLayout layout = document.Database.Layouts.FirstOrDefault(l =>
                string.Equals(l.LayoutName, space.Name, StringComparison.OrdinalIgnoreCase)
            );
            return layout != null && layout.PlotPaperSize.X > 0.0 && layout.PlotPaperSize.Y > 0.0;
        }

        private static CadPaper Sheet(string name)
        {
            string wanted = name.Trim();
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

        private static string ScaleText(double scale)
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

        [McpServerTool(Name = "export_svg", ReadOnly = false, Destructive = true, Idempotent = true, OpenWorld = false)]
        [Description(
            "Draws a layout (or a part of it) to an SVG file, as the app's canvas draws it on a white ground: vectors, text as text, at the canvas's level of detail (a text under 3 pixels is left out). Size and box as render_png; where no box is given, the view zoom_window or zoom_object set is drawn. An attached server draws the app's drawing as it stands, in its own process. Refused with --read-only."
        )]
        public FileResult ExportSvg(
            [Description("The SVG file to write; .svg is added where there is no extension.")] string path,
            [Description(
                "The part to draw, [minX, minY, maxX, maxY] in millimetres; all of the layout where left out."
            )]
                double[] box = null,
            [Description("The layout; model space where left out.")] string layout = null,
            [Description("Width of the picture in pixels.")] int width = 0,
            [Description("Height of the picture in pixels.")] int height = 0,
            [Description(
                "Pixels to an inch of paper for a layout, where width and height are left out; 96 is the default."
            )]
                double dpi = 0.0,
            [Description(OverwriteText)] bool overwrite = false
        ) => _session.Run("export_svg", true, drawing =>
            {
                Box("box", box);

                if (width < 0 || height < 0 || dpi < 0.0 || double.IsNaN(dpi))
                {
                    throw new ArgumentException("A size is not negative.");
                }

                string full = McpKit.Target(path, ".svg", overwrite, drawing.Path);
                string space = Layout(drawing, layout);
                CadDocument document = McpKit.Snapshot(drawing);
                var request = new RenderRequest
                {
                    Layout = space,
                    Box = box ?? (McpKit.IsApp(drawing) ? null : McpViews.Get(document, space)),
                    Width = width,
                    Height = height,
                    Dpi = dpi,
                };
                CadRender.WriteSvg(full, document, RenderOptions.Of(request));
                return new FileResult
                {
                    Path = full,
                    Layout = space,
                    Bytes = new FileInfo(full).Length,
                };
            });

        [McpServerTool(Name = "save_as", ReadOnly = false, Destructive = true, Idempotent = true, OpenWorld = false)]
        [Description(
            "Writes a COPY of the drawing as a DXF or a DWG of a version of AutoCAD, as the core's writers do, and leaves the open drawing as it is: its own file, its name and its unsaved changes (save with a path is the tool that moves the drawing to a new file). An attached server writes a copy of the app's drawing as it stands, changes not saved included, and never touches the app. A file that is there, the drawing's own among them, is refused unless overwrite is true. Refused with --read-only."
        )]
        public SaveAsResult SaveAs(
            [Description("The file to write.")] string path,
            [Description("dxf or dwg; taken from the path's extension where left out.")] string format = null,
            [Description(
                "The version: 2000, 2004, 2007, 2010, 2013 or 2018 (or its code, as AC1032), and 12 for a DXF; the drawing's own for a DXF, 2018 for a DWG, where left out."
            )]
                string version = null,
            [Description(OverwriteText)] bool overwrite = false
        ) => _session.Run("save_as", true, drawing =>
            {
                string kind = Format(path, format);
                string code = McpKit.VersionCode(version);
                string full = McpKit.Target(path, "." + kind, overwrite, drawing.Path);
                IReadOnlyList<string> left = Copy(drawing, full, kind, code);
                return new SaveAsResult
                {
                    Path = full,
                    Format = kind,
                    Version = code,
                    Bytes = new FileInfo(full).Length,
                    LeftOut = left,
                };
            });

        private static string Format(string path, string format)
        {
            string wanted = format?.Trim().TrimStart('.').ToLowerInvariant();

            if (wanted == null || wanted.Length == 0)
            {
                wanted = Path.GetExtension(path ?? string.Empty).TrimStart('.').ToLowerInvariant();
            }

            if (wanted != "dxf" && wanted != "dwg")
            {
                throw new ArgumentException(
                    "The format is dxf or dwg: give format, or a path that ends in .dxf or .dwg."
                );
            }

            return wanted;
        }

        /// <summary>Writes the copy: a DXF straight from the document, a DWG from a document of its own made from the DXF of it.</summary>
        private static IReadOnlyList<string> Copy(IDrawingBackend drawing, string full, string kind, string code)
        {
            CadDocument document = McpKit.Snapshot(drawing);

            if (kind == "dxf")
            {
                return document.SaveCopy(full, code);
            }

            // The writer of a DWG saves a document and makes it the file the document is known by: a document made from a DXF copy takes that.
            string temporary = Path.Combine(
                Path.GetTempPath(),
                "opencad2d-mcp-" + Guid.NewGuid().ToString("N") + ".dxf"
            );

            try
            {
                document.SaveCopy(temporary, null);
                CadDocument copy = CadDocument.Open(temporary);
                copy.SaveAs(full, code, null);
                return (copy.LeftOutOfDwg ?? string.Empty)
                    .Replace("\r", string.Empty)
                    .Split(new[] { '\n' }, StringSplitOptions.RemoveEmptyEntries)
                    .Select(l => l.Trim())
                    .ToList();
            }
            finally
            {
                if (File.Exists(temporary))
                {
                    File.Delete(temporary);
                }
            }
        }

        private static readonly Dictionary<string, string> PurgeKeywords = new Dictionary<string, string>(
            StringComparer.Ordinal
        )
        {
            ["all"] = "All",
            ["blocks"] = "Blocks",
            ["dimension_styles"] = "Dimstyles",
            ["empty_text"] = "Empty text objects",
            ["groups"] = "Groups",
            ["layers"] = "LAyers",
            ["linetypes"] = "LTypes",
            ["multileader_styles"] = "MUltileaderstyles",
            ["plot_styles"] = "Plotstyles",
            ["shapes"] = "SHapes",
            ["table_styles"] = "Tablestyles",
            ["text_styles"] = "textSTyles",
            ["zero_length"] = "Zero-length geometry",
        };

        /// <summary>The words the command says for what it deleted, one to many, to count by one name.</summary>
        private static readonly string[][] PurgedNames =
        {
            new[] { "block", "blocks" },
            new[] { "layer", "layers" },
            new[] { "linetype", "linetypes" },
            new[] { "text style", "text styles" },
            new[] { "shape file", "shape files" },
            new[] { "dimension style", "dimension styles" },
            new[] { "plotstyle", "plotstyles" },
            new[] { "table style", "table styles" },
            new[] { "multileader style", "multileader styles" },
            new[] { "group", "groups" },
            new[] { "registered application", "registered applications" },
        };

        [McpServerTool(Name = "purge", ReadOnly = false, Destructive = true, Idempotent = true, OpenWorld = false)]
        [Description(
            "Purges what nothing uses, as -PURGE does: unused blocks, layers, linetypes, text styles, dimension styles, groups, multileader styles, table styles, plot styles and shapes, repeatedly so that a block holding the only reference to another takes that one with it, and with zero_length the lines, arcs and polylines of no length and with empty_text the texts of no content. What AutoCAD keeps is kept (layer 0, the current layer and styles, Standard). It types fixed -PURGE input into the command session (the kind, *, no questions), never free text. Each call is the undo steps of the command. Refused with --read-only, and in an app that has a command running."
        )]
        public PurgeResult Purge(
            [Description(
                "What to purge, as [\"blocks\", \"layers\"]: blocks, layers, linetypes, text_styles, dimension_styles, groups, multileader_styles, table_styles, plot_styles, shapes, zero_length, empty_text, or all (every named kind)."
            )]
                string[] kinds
        ) => _session.Run("purge", true, drawing =>
            {
                if (kinds == null || kinds.Length == 0)
                {
                    throw new ArgumentException(
                        "Give at least one kind to purge: " + string.Join(", ", PurgeKeywords.Keys) + "."
                    );
                }

                var keywords = new List<string>();

                foreach (string kind in kinds)
                {
                    string name = (kind ?? string.Empty).Trim().ToLowerInvariant();

                    if (!PurgeKeywords.TryGetValue(name, out string keyword))
                    {
                        throw new ArgumentException(
                            "The kind " + kind + " is not one to purge: " + string.Join(", ", PurgeKeywords.Keys) + "."
                        );
                    }

                    if (!keywords.Contains(keyword))
                    {
                        keywords.Add(keyword);
                    }
                }

                var counts = new SortedDictionary<string, int>(StringComparer.Ordinal);
                var messages = new List<string>();

                foreach (string keyword in keywords)
                {
                    bool entities = keyword == "Zero-length geometry" || keyword == "Empty text objects";
                    string answer = TypeFixed(
                        drawing,
                        entities ? "-PURGE\n" + keyword.Substring(0, 1) : "-PURGE\n" + keyword + "\n*\nNo"
                    );

                    foreach (string line in answer.Replace("\r", string.Empty).Split('\n'))
                    {
                        string said = line.Trim();

                        if (said.Length == 0 || said.StartsWith("Nested items", StringComparison.Ordinal))
                        {
                            continue;
                        }

                        messages.Add(said);
                        Match deleted = Regex.Match(said, @"^(\d+) (.+) deleted\.$", RegexOptions.CultureInvariant);

                        if (deleted.Success)
                        {
                            string label = deleted.Groups[2].Value;
                            string[] names = PurgedNames.FirstOrDefault(n => n[0] == label || n[1] == label);
                            string key = names == null ? label : names[1];
                            int number = int.Parse(deleted.Groups[1].Value, CultureInfo.InvariantCulture);
                            counts[key] = (counts.TryGetValue(key, out int before) ? before : 0) + number;
                        }
                    }
                }

                return new PurgeResult
                {
                    Purged = counts,
                    Total = counts.Values.Sum(),
                    Messages = messages,
                };
            });

        /// <summary>Types a fixed command text on the drawing's command line, refused in an app where a command already waits for the user's answer.</summary>
        private static string TypeFixed(IDrawingBackend drawing, string text)
        {
            if (drawing is RemoteBackend app && app.Document.GetHistory(1).IsRunning)
            {
                throw new InvalidOperationException(
                    "A command is running in the app: end it (answer it or press Esc) before this tool types its own."
                );
            }

            return drawing.RunCommand(text);
        }

        [McpServerTool(Name = "audit", ReadOnly = false, Destructive = false, Idempotent = true, OpenWorld = false)]
        [Description(
            "Checks the drawing for errors, as AUDIT does, and counts them by kind with the first sentences it says (a block reference of a block the drawing has not, a handle two records share, a layer a record names that is not there). With fix true it also fixes what it can, as AUDIT with Yes, as one undo step, then audits again; the fix is refused with --read-only, and in an app that has a command running. The check alone writes nothing and is allowed everywhere."
        )]
        public AuditResult Audit([Description("True to fix what can be fixed.")] bool fix = false) =>
            _session.Run("audit", fix, drawing =>
            {
                DxfRepairReport report = McpKit.Snapshot(drawing).Database.Audit(false);
                int found = report.Count;
                SortedDictionary<string, int> kinds = Kinds(report);
                List<string> messages = report.Repairs.Take(20).Select(r => r.Message).ToList();
                int remaining = found;

                if (fix && found > 0)
                {
                    TypeFixed(drawing, "AUDIT\nYes");
                    remaining = McpKit.Snapshot(drawing).Database.Audit(false).Count;
                }

                return new AuditResult
                {
                    Found = found,
                    Fixed = fix,
                    Remaining = remaining,
                    Kinds = kinds,
                    Messages = messages,
                };
            });

        private static SortedDictionary<string, int> Kinds(DxfRepairReport report)
        {
            var kinds = new SortedDictionary<string, int>(StringComparer.Ordinal);

            foreach (DxfRepair repair in report.Repairs)
            {
                string kind = repair.Kind.ToString();
                kinds[kind] = (kinds.TryGetValue(kind, out int n) ? n : 0) + 1;
            }

            return kinds;
        }

        [McpServerTool(
            Name = "zoom_extents",
            ReadOnly = false,
            Destructive = false,
            Idempotent = true,
            OpenWorld = false
        )]
        [Description(
            "Shows everything of the layout. Attached to an app it is ZOOM Extents of the app's window (the drawing and layout the user has on screen), and the app's view moves, so it is refused with --read-only; on a drawing file it clears the view render_png and export_svg use when no box is given. Gives the part of the drawing now shown."
        )]
        public ZoomResult ZoomExtents(
            [Description(
                "The layout, on a drawing file; model space where left out. An app zooms the layout it shows."
            )]
                string layout = null
        ) => _session.Run("zoom_extents", false, drawing =>
            {
                if (McpKit.IsApp(drawing))
                {
                    CadRemoteDocument app = ZoomApp(drawing, "zoom_extents");
                    app.ZoomExtents();
                    return AppView(app);
                }

                string space = Layout(drawing, layout);
                McpViews.Set(McpKit.Snapshot(drawing), space, null);
                return new ZoomResult { View = "render", Box = Rounded(McpKit.Union(drawing.Bounds(space))) };
            });

        [McpServerTool(
            Name = "zoom_window",
            ReadOnly = false,
            Destructive = false,
            Idempotent = true,
            OpenWorld = false
        )]
        [Description(
            "Shows a window of the drawing, as ZOOM Window. Attached to an app it moves the app's view (refused with --read-only, and when the drawing is not the one shown); on a drawing file it sets the view render_png and export_svg use when no box is given. Gives the part of the drawing now shown."
        )]
        public ZoomResult ZoomWindow(
            [Description("The window, [minX, minY, maxX, maxY] in millimetres.")] double[] box,
            [Description("The layout, on a drawing file; model space where left out.")] string layout = null
        ) => _session.Run("zoom_window", false, drawing =>
            {
                DisplayBox window = Box("box", box)
                    ?? throw new ArgumentException("Give the window: box is [minX, minY, maxX, maxY].");

                if (!(window.Width > 0.0) || !(window.Height > 0.0))
                {
                    throw new ArgumentException("The window has a width and a height above nought.");
                }

                if (McpKit.IsApp(drawing))
                {
                    CadRemoteDocument app = ZoomApp(drawing, "zoom_window");
                    app.ZoomWindow(new GeoPoint2(window.MinX, window.MinY), new GeoPoint2(window.MaxX, window.MaxY));
                    return AppView(app);
                }

                string space = Layout(drawing, layout);
                var view = new[] { window.MinX, window.MinY, window.MaxX, window.MaxY };
                McpViews.Set(McpKit.Snapshot(drawing), space, view);
                return new ZoomResult { View = "render", Box = Rounded(view) };
            });

        [McpServerTool(
            Name = "zoom_object",
            ReadOnly = false,
            Destructive = false,
            Idempotent = true,
            OpenWorld = false
        )]
        [Description(
            "Shows entities with a margin of 5 %, as ZOOM Object. Attached to an app it moves the app's view (refused with --read-only, and when the drawing is not the one shown); on a drawing file it sets the view render_png and export_svg use when no box is given. Gives the part of the drawing now shown."
        )]
        public ZoomResult ZoomObject(
            [Description(HandlesText)] string[] handles,
            [Description("The layout, on a drawing file; model space where left out.")] string layout = null
        ) => _session.Run("zoom_object", false, drawing =>
            {
                List<string> list = Handles(handles);

                if (McpKit.IsApp(drawing))
                {
                    CadRemoteDocument app = ZoomApp(drawing, "zoom_object");
                    app.ZoomObjects(list);
                    return AppView(app);
                }

                double[] box = McpKit.Union(drawing.BoundsOf(list));

                if (box == null)
                {
                    throw new ArgumentException("None of those entities draws anything: there is nothing to show.");
                }

                double margin = Math.Max(Math.Max(box[2] - box[0], box[3] - box[1]) * 0.05, 1.0);
                var view = new[] { box[0] - margin, box[1] - margin, box[2] + margin, box[3] + margin };
                McpViews.Set(McpKit.Snapshot(drawing), Layout(drawing, layout), view);
                return new ZoomResult { View = "render", Box = Rounded(view) };
            });

        /// <summary>The app's drawing of an attached server, for a tool that moves the app's view: refused where the server is read-only.</summary>
        private CadRemoteDocument ZoomApp(IDrawingBackend drawing, string tool)
        {
            if (_session.Options.ReadOnly)
            {
                throw new McpException(
                    tool
                        + " is refused: this server was started with --read-only, and it would move the view the user has on screen. Use render_png with a box to look at a part."
                );
            }

            return McpKit.App(drawing, tool, "it is for an app.");
        }

        private static ZoomResult AppView(CadRemoteDocument app)
        {
            CadRemoteExtents view = app.CurrentView;
            return new ZoomResult
            {
                View = "app",
                Box = Rounded(new[] { view.Min.X, view.Min.Y, view.Max.X, view.Max.Y }),
            };
        }

        private static double[] Rounded(double[] box) => box?.Select(EntityDescriber.R).ToArray();
    }
}
