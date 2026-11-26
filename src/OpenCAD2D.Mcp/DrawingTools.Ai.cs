using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.Json;
using GeometryHelper.Dxf;
using GeometryHelper.Geometry;
using ModelContextProtocol.Server;

namespace OpenCAD2D.Mcp
{
    /// <summary>
    /// The tools of the place, render, score, refine loop of an agent that lays annotation out: measure a word before it is added,
    /// find the empty places of a frame, ask whether a place is free, score the drawing against a company standard, and take the
    /// drawing out as JSON or as a graph. The calls are those the Python <c>Drawing</c> makes (<c>measure_text</c>,
    /// <c>free_space</c>, <c>fits</c>, <c>check</c>, <c>to_json</c>, <c>to_graph</c>), on a file and on an app alike.
    /// </summary>
    internal sealed partial class DrawingTools
    {
        /// <summary>The rows of a page of an export when the caller gives none: an entity of the document is some hundreds of bytes.</summary>
        public const int DefaultExportPageSize = 50;

        private const string FramesText =
            "Each frame is {x, y, width, height, rotation}: the first corner, the length of the word and its height, and the angle the length runs at in degrees. Pass a frame to fits as [x, y, width, height, rotation].";

        [McpServerTool(
            Name = "measure_text",
            ReadOnly = true,
            Destructive = false,
            Idempotent = true,
            OpenWorld = false
        )]
        [Description(
            "Measures the single-line text add_text would add, without adding it: the box it would take and the frame of its word, found by the code that draws it, in the drawing's text style and fonts, so the box equals bounds of the text once added. Nothing is added and no undo step is made. "
                + FramesText
        )]
        public MeasureResult MeasureText(
            [Description("The words.")] string text,
            [Description("Insertion point [x, y], or the point the text stands about where justify is given.")]
                double[] at,
            [Description("Letter height in millimetres, above 0.")] double height,
            [Description("Rotation in degrees, counter-clockwise.")] double rotation = 0.0,
            [Description("A text style of the drawing (list_text_styles); Standard where left out.")]
                string text_style = null,
            [Description(
                "left, center, right, middle or a code TL, TC, TR, ML, MC, MR, BL, BC, BR (either case); the insertion point is the lower left corner of the first letter where left out."
            )]
                string justify = null
        ) => _session.Run("measure_text", false, drawing =>
            {
                DxfDBText made = EntityFactory.Text(
                    Words(text),
                    Point("at", at),
                    height,
                    Radians(rotation),
                    text_style
                );

                if (justify != null)
                {
                    made.Justify = Justification(justify, true);
                    made.AlignmentPoint = Point("at", at);
                }

                return Measured(drawing.Measure(made));
            });

        [McpServerTool(
            Name = "measure_mtext",
            ReadOnly = true,
            Destructive = false,
            Idempotent = true,
            OpenWorld = false
        )]
        [Description(
            "Measures the multi-line text add_mtext would add, without adding it: the box of its lines and a frame for each run of a line, as the drawing lays it out in its fonts, wrapped at the width given. Nothing is added. "
                + FramesText
        )]
        public MeasureResult MeasureMText(
            [Description("The words; \\P starts a new line.")] string text,
            [Description("Insertion point [x, y]: the attachment point of the text (the top left corner by default).")]
                double[] at,
            [Description("Letter height in millimetres, above 0.")] double height,
            [Description("Width the lines wrap at in millimetres; 0 or left out for no wrapping.")] double width = 0.0,
            [Description("Rotation in degrees, counter-clockwise.")] double rotation = 0.0,
            [Description("A text style of the drawing (list_text_styles); Standard where left out.")]
                string text_style = null,
            [Description("The attachment: TL (the default), TC, TR, ML, MC, MR, BL, BC or BR, either case.")]
                string justify = null
        ) => _session.Run("measure_mtext", false, drawing =>
            {
                DxfMText made = EntityFactory.MText(Words(text), Point("at", at), height, width, text_style);
                Finite("The rotation of an MTEXT", rotation);

                if (rotation != 0.0)
                {
                    made.Rotation = Radians(rotation);
                }

                if (justify != null)
                {
                    made.Attachment = Justification(justify, false);
                }

                return Measured(drawing.Measure(made));
            });

        [McpServerTool(
            Name = "measure_dimension",
            ReadOnly = true,
            Destructive = false,
            Idempotent = true,
            OpenWorld = false
        )]
        [Description(
            "Measures the dimension an add_*_dim call would add, without adding it: the box of its lines, arrows and words and the frame of its words, as the drawing draws its picture in the dimension style. kind and points are linear and [first, second, location] (with angle of the dimension line as add_linear_dim takes it), aligned and [first, second, location], radial or diameter and [center, rim, location] (a point of the circle, in place of the radius), angular and [vertex, first, second, location]. Nothing is added. "
                + FramesText
        )]
        public MeasureResult MeasureDimension(
            [Description("linear, aligned, radial, diameter or angular.")] string kind,
            [Description("The points as [[x, y], ...]: three, or four for angular.")] double[][] points,
            [Description("A dimension style of the drawing; the current one where left out.")] string dim_style = null,
            [Description("The words in place of the measurement; <> stands for the measurement.")] string text = null,
            [Description("Direction of a linear dimension line in degrees; chosen from location where left out.")]
                double? angle = null
        ) => _session.Run("measure_dimension", false, drawing =>
            {
                string name = (kind ?? string.Empty).Trim().ToLowerInvariant();
                int count = name == "angular" ? 4 : 3;

                if (!new[] { "linear", "aligned", "radial", "diameter", "angular" }.Contains(name))
                {
                    throw new ArgumentException(
                        "A dimension to measure is of the kind linear, aligned, radial, diameter or angular, not "
                            + kind
                            + "."
                    );
                }

                List<GeoPoint3> given = Points("points", points);

                if (given.Count != count)
                {
                    throw new ArgumentException(
                        "A " + name + " dimension is measured from " + count + " points, not " + given.Count + "."
                    );
                }

                string style = DimStyle(drawing, dim_style);
                DxfDimension made;

                switch (name)
                {
                    case "linear":
                        made = EntityFactory.LinearDimension(
                            given[0],
                            given[1],
                            given[2],
                            angle.HasValue ? Radians(angle.Value) : (double?)null,
                            text,
                            style
                        );
                        break;
                    case "aligned":
                        made = EntityFactory.AlignedDimension(given[0], given[1], given[2], text, style);
                        break;
                    case "angular":
                        made = EntityFactory.AngularDimension(given[0], given[1], given[2], given[3], text, style);
                        break;
                    default:
                        double dx = given[1].X - given[0].X;
                        double dy = given[1].Y - given[0].Y;
                        double radius = Math.Sqrt(dx * dx + dy * dy);
                        made =
                            name == "radial"
                                ? (DxfDimension)EntityFactory.RadialDimension(given[0], radius, given[2], text, style)
                                : EntityFactory.DiameterDimension(given[0], radius, given[2], text, style);
                        break;
                }

                return Measured(drawing.Measure(made));
            });

        [McpServerTool(
            Name = "measure_leader",
            ReadOnly = true,
            Destructive = false,
            Idempotent = true,
            OpenWorld = false
        )]
        [Description(
            "Measures the multileader of words the MLEADER command would make with its arrow at one point and its landing at another, without adding it: the box of the leader line, its arrowhead, the landing and the words, and the frame of the words, laid out as the command lays them. A style the drawing lacks, one of a block or of no content, and an annotative one are refused. "
                + FramesText
        )]
        public MeasureResult MeasureLeader(
            [Description("Where the arrowhead points, [x, y].")] double[] arrow,
            [Description("Where the leader lands, [x, y].")] double[] landing,
            [Description("The words.")] string text,
            [Description("A multileader style of the drawing; the current one where left out.")] string style = null,
            [Description("The scale of the multileader, above 0; the one MLEADER gives where left out.")]
                double? scale = null
        ) => _session.Run("measure_leader", false, drawing =>
            {
                GeoPoint3 start = Point("arrow", arrow);
                GeoPoint3 end = Point("landing", landing);

                if (scale.HasValue && !(scale.Value > 0.0 && !double.IsInfinity(scale.Value)))
                {
                    throw new ArgumentException("The scale of a leader is a finite number above nought.");
                }

                return Measured(
                    drawing.MeasureLeader(
                        string.IsNullOrWhiteSpace(style) ? null : style.Trim(),
                        start.X,
                        start.Y,
                        end.X,
                        end.Y,
                        Words(text),
                        scale
                    )
                );
            });

        [McpServerTool(Name = "free_space", ReadOnly = true, Destructive = false, Idempotent = true, OpenWorld = false)]
        [Description(
            "The empty places of a frame: every rectangle, not turned, inside the frame that no line or word of the kinds runs through or comes within margin of, and that cannot be made larger, the largest first (equal areas by min y, min x). They overlap one another, as the empty places of a page do: a box a thing could stand in lies inside one of them, which is what fits tests exactly. In a crowded frame the rectangles number in thousands: give the width and height of the thing to place as min_width and min_height and only the rectangles it could stand in come back. A frame too crowded to list every rectangle is refused. Paged."
        )]
        public FreeSpaceResult FreeSpace(
            [Description("The frame to look in, [minX, minY, maxX, maxY] in millimetres, with a width and a height.")]
                double[] frame,
            [Description("DXF kinds that take room, as [\"LINE\", \"TEXT\"], either case; every kind where left out.")]
                string[] kinds = null,
            [Description("Clear room kept round each thing, in millimetres, 0 or more.")] double margin = 0.0,
            [Description("Keep only rectangles at least this wide, in millimetres.")] double min_width = 0.0,
            [Description("Keep only rectangles at least this high, in millimetres.")] double min_height = 0.0,
            [Description("The layout; model space where left out.")] string layout = null,
            [Description("How little still counts as touching, in millimetres.")] double tolerance = 0.001,
            [Description("The page, numbered from 1.")] int page = 1,
            [Description("The rows of a page, 1 to 500.")] int page_size = DefaultPageSize
        ) => _session.Run("free_space", false, drawing =>
            {
                DisplayBox? area = Box("frame", frame);

                if (!area.HasValue)
                {
                    throw new ArgumentException("A frame is [minX, minY, maxX, maxY], four numbers.");
                }

                DisplayBox within = area.Value;

                if (!(within.MaxX > within.MinX && within.MaxY > within.MinY))
                {
                    throw new ArgumentException("A frame has a width and a height above nought.");
                }

                NotNegative("The margin", margin);
                NotNegative("The least width", min_width);
                NotNegative("The least height", min_height);
                NotNegative("The tolerance", tolerance);
                double[] boxes = drawing.FreeSpace(
                    Layout(drawing, layout),
                    new[] { within.MinX, within.MinY, within.MaxX, within.MaxY },
                    kinds == null ? null : Upper(kinds),
                    margin,
                    min_width,
                    min_height,
                    tolerance
                );
                var all = Enumerable.Range(0, boxes.Length / 4).ToList();
                var result = new FreeSpaceResult();
                result.Boxes = Page(all, page, page_size, result).Select(i => Corners(boxes, i)).ToList();
                return result;
            });

        [McpServerTool(Name = "fits", ReadOnly = true, Destructive = false, Idempotent = true, OpenWorld = false)]
        [Description(
            "Whether a place is free: nothing of the kinds runs through the candidate or comes within margin of it, tested on the real lines, arcs, fills and word frames and not on their boxes, so a diagonal passing a corner at a distance fits. What only touches it within the tolerance does not count. The candidate is a box [minX, minY, maxX, maxY] or a frame [x, y, width, height, rotation in degrees], as measure_* gives one."
        )]
        public FitsResult Fits(
            [Description("A box of four numbers or a frame of five, in millimetres and degrees.")] double[] candidate,
            [Description("DXF kinds that take room; every kind where left out.")] string[] kinds = null,
            [Description("Clear room to leave round the candidate, in millimetres, 0 or more.")] double margin = 0.0,
            [Description("The layout; model space where left out.")] string layout = null,
            [Description("How little still counts as touching, in millimetres.")] double tolerance = 0.001
        ) => _session.Run("fits", false, drawing =>
            {
                if (
                    candidate == null
                    || (candidate.Length != 4 && candidate.Length != 5)
                    || candidate.Any(v => double.IsNaN(v) || double.IsInfinity(v))
                )
                {
                    throw new ArgumentException(
                        "A candidate is a box [minX, minY, maxX, maxY] or a frame [x, y, width, height, rotation], finite numbers."
                    );
                }

                NotNegative("The margin", margin);
                NotNegative("The tolerance", tolerance);
                double[] frame;

                if (candidate.Length == 4)
                {
                    DisplayBox box = Box("candidate", candidate).Value;
                    frame = new[] { box.MinX, box.MinY, box.MaxX - box.MinX, box.MaxY - box.MinY, 0.0 };
                }
                else
                {
                    frame = new[] { candidate[0], candidate[1], candidate[2], candidate[3], Radians(candidate[4]) };
                }

                if (!(frame[2] > 0.0 && frame[3] > 0.0))
                {
                    throw new ArgumentException("A candidate has a width and a height above nought.");
                }

                return new FitsResult
                {
                    Fits = drawing.Fits(
                        Layout(drawing, layout),
                        frame,
                        kinds == null ? null : Upper(kinds),
                        margin,
                        tolerance
                    ),
                };
            });

        [McpServerTool(
            Name = "check_standard",
            ReadOnly = true,
            Destructive = false,
            Idempotent = true,
            OpenWorld = false
        )]
        [Description(
            "Checks the drawing against a company drawing standard and scores it: a score from 0 to 1 for each rule the standard runs, a total score (the weighted mean) and the violations, each with its rule, the object (and the second where the rule is about two), the place as a box, what was found and what is allowed, sorted by rule and then handle. A rule scores 1 minus the share of the things it looked at that offend. The standard is a JSON object (schema opencad2d.standard, docs/schemas/opencad2d-standard-v1.schema.json: the layers allowed, text and dimension styles with fonts, heights words may have on paper, the least gap between words and lines, dimension chains, layer 0 and BYLAYER) or the path of such a file. Words are measured on paper: in a layout as drawn, in model space through each viewport at its scale, or at model_scale where there is none. Nothing is changed. Paged."
        )]
        public StandardResult CheckStandard(
            [Description("The standard: a JSON object, or the path of a .json file of one.")] JsonElement standard,
            [Description(
                "Paper millimetres of one unit of model space where model space is checked on its own: 0.01 for a drawing of millimetres plotted 1:100; 0 for none (the viewports, or 1:1)."
            )]
                double model_scale = 0.0,
            [Description("Millimetres on paper within which a gap counts as the one allowed.")]
                double tolerance = 0.001,
            [Description("The page of violations, numbered from 1.")] int page = 1,
            [Description("The violations of a page, 1 to 500.")] int page_size = DefaultPageSize
        ) => _session.Run("check_standard", false, drawing =>
            {
                string text = StandardText(standard);

                if (!(model_scale >= 0.0) || double.IsInfinity(model_scale))
                {
                    throw new ArgumentException("model_scale is a finite number of nought or more.");
                }

                NotNegative("The tolerance", tolerance);
                StandardRows rows = drawing.Check(text, model_scale, tolerance);
                var result = new StandardResult
                {
                    Name = rows.Name,
                    Score = Math.Round(rows.Total, 6),
                    Rules = rows
                        .Rules.Select(r => new RuleScoreInfo
                        {
                            Rule = r.Rule,
                            Score = Math.Round(r.Score, 6),
                            Subjects = r.Subjects,
                            Offenders = r.Offenders,
                            Weight = r.Weight,
                        })
                        .ToList(),
                };
                result.Violations = Page(rows.Violations, page, page_size, result)
                    .Select(v => new ViolationInfo
                    {
                        Rule = v.Rule,
                        Handle = v.Handle,
                        Other = v.Other,
                        Space = v.Space,
                        Through = v.Through,
                        Box = v.Box?.Select(EntityDescriber.R).ToArray(),
                        Measured = v.Measured,
                        Allowed = v.Allowed,
                        Value = v.Value.HasValue ? EntityDescriber.R(v.Value.Value) : (double?)null,
                        Limit = v.Limit.HasValue ? EntityDescriber.R(v.Limit.Value) : (double?)null,
                    })
                    .ToList();
                return result;
            });

        [McpServerTool(
            Name = "export_json",
            ReadOnly = true,
            Destructive = false,
            Idempotent = true,
            OpenWorld = false
        )]
        [Description(
            "The drawing as the document Python's to_json writes (schema opencad2d.drawing, docs/schemas/opencad2d-drawing-v1.schema.json), in millimetres with every block reference walked down to what it draws: the members that are not lists (schema, version, units), how many rows each list has (layers, textStyles, blocks, entities ...), how many entities there are of each kind, and a page of the entities as the document writes them, in the order of their handles. A document is megabytes for a large drawing, so it is paged here; export_to_file writes all of it to a file. Paged."
        )]
        public ExportResult ExportJson(
            [Description("One layout (Model for model space); model space and every layout where left out.")]
                string layout = null,
            [Description("The page of entities, numbered from 1.")] int page = 1,
            [Description("The entities of a page, 1 to 500.")] int page_size = DefaultExportPageSize
        ) => _session.Run("export_json", false, drawing =>
            {
                string text = drawing.ExportJson(ExportLayout(drawing, layout));
                return Summarised(text, "entities", page, page_size);
            });

        [McpServerTool(
            Name = "export_graph",
            ReadOnly = true,
            Destructive = false,
            Idempotent = true,
            OpenWorld = false
        )]
        [Description(
            "The drawing as the graph Python's to_graph writes (schema opencad2d.graph): a node for each entity of export_json and an edge for each way two are related, touches (curves within the tolerance of each other), contains, dimensions and annotates. The answer says how many nodes and edges there are and of what kind, and gives a page of the edges (each is {source, target, kind, at}, the nodes numbered as export_json lists the entities) or, with part nodes, of the nodes. Paged; export_to_file writes all of it."
        )]
        public ExportResult ExportGraph(
            [Description("One layout (Model for model space); model space and every layout where left out.")]
                string layout = null,
            [Description("How near two ends must be to touch, in millimetres, above 0.")] double tolerance = 0.001,
            [Description("edges (the default) or nodes: which list the page is of.")] string part = "edges",
            [Description("The page, numbered from 1.")] int page = 1,
            [Description("The rows of a page, 1 to 500.")] int page_size = DefaultExportPageSize
        ) => _session.Run("export_graph", false, drawing =>
            {
                string name = (part ?? string.Empty).Trim().ToLowerInvariant();

                if (name != "edges" && name != "nodes")
                {
                    throw new ArgumentException("part is edges or nodes, not " + part + ".");
                }

                Positive("The tolerance", tolerance);
                string text = drawing.ExportGraph(ExportLayout(drawing, layout), tolerance);
                return Summarised(text, name, page, page_size);
            });

        [McpServerTool(
            Name = "export_to_file",
            ReadOnly = false,
            Destructive = true,
            Idempotent = true,
            OpenWorld = false
        )]
        [Description(
            "Writes the whole of export_json (what json) or export_graph (what graph) to a .json file as UTF-8 text, and answers the path and the bytes written; for a pipeline that reads the file and not the agent. .json is added where the path has no extension, another extension is refused, the folder is made where it is not there, a file that is there is refused unless overwrite is true, and the drawing's own file is always refused. Refused by a server started with --read-only."
        )]
        public ExportedResult ExportToFile(
            [Description("json or graph.")] string what,
            [Description("The .json file to write.")] string path,
            [Description("One layout (Model for model space); model space and every layout where left out.")]
                string layout = null,
            [Description("For graph: how near two ends must be to touch, in millimetres, above 0.")]
                double tolerance = 0.001,
            [Description(OverwriteText)] bool overwrite = false
        ) => _session.Run("export_to_file", true, drawing =>
            {
                string kind = (what ?? string.Empty).Trim().ToLowerInvariant();

                if (kind != "json" && kind != "graph")
                {
                    throw new ArgumentException("what is json or graph, not " + what + ".");
                }

                string space = ExportLayout(drawing, layout);

                if (kind == "graph")
                {
                    Positive("The tolerance", tolerance);
                }

                string full = McpKit.Target(path, ".json", overwrite, drawing.Path);
                string text = kind == "graph" ? drawing.ExportGraph(space, tolerance) : drawing.ExportJson(space);

                var encoding = new UTF8Encoding(false);
                File.WriteAllText(full, text, encoding);
                return new ExportedResult
                {
                    Path = full,
                    What = kind,
                    Bytes = encoding.GetByteCount(text),
                };
            });

        /// <summary>The layout of an export: null for model space and every layout, else the name the drawing has.</summary>
        private static string ExportLayout(IDrawingBackend drawing, string layout) =>
            string.IsNullOrWhiteSpace(layout) ? null : Layout(drawing, layout);

        /// <summary>
        /// Reads an export document (the JSON text) into what the answer holds: its members that are not lists, the rows of each list,
        /// the kinds of the rows of the list asked for and a page of them.
        /// </summary>
        private static ExportResult Summarised(string text, string part, int page, int pageSize)
        {
            using (JsonDocument document = JsonDocument.Parse(text))
            {
                var result = new ExportResult
                {
                    Header = new SortedDictionary<string, JsonElement>(StringComparer.Ordinal),
                    Tables = new SortedDictionary<string, int>(StringComparer.Ordinal),
                    Kinds = new SortedDictionary<string, int>(StringComparer.Ordinal),
                    Part = part,
                };
                var rows = new List<JsonElement>();

                foreach (JsonProperty member in document.RootElement.EnumerateObject())
                {
                    if (member.Value.ValueKind != JsonValueKind.Array)
                    {
                        result.Header[member.Name] = member.Value.Clone();
                        continue;
                    }

                    result.Tables[member.Name] = member.Value.GetArrayLength();

                    if (member.Name == part)
                    {
                        rows.AddRange(member.Value.EnumerateArray());
                    }
                }

                foreach (JsonElement row in rows)
                {
                    if (row.ValueKind == JsonValueKind.Object && row.TryGetProperty("kind", out JsonElement kind))
                    {
                        string name = kind.ValueKind == JsonValueKind.String ? kind.GetString() : kind.ToString();
                        result.Kinds[name] = result.Kinds.TryGetValue(name, out int n) ? n + 1 : 1;
                    }
                }

                result.Rows = Page(rows, page, pageSize, result).Select(r => r.Clone()).ToList();
                return result;
            }
        }

        /// <summary>The text of a standard: the JSON object given, a JSON text given as a string, or the file a path names.</summary>
        private static string StandardText(JsonElement standard)
        {
            if (standard.ValueKind == JsonValueKind.Object)
            {
                return standard.GetRawText();
            }

            if (standard.ValueKind != JsonValueKind.String)
            {
                throw new ArgumentException("The standard is a JSON object, or the path of a JSON file of one.");
            }

            string given = standard.GetString().Trim();

            if (given.StartsWith("{", StringComparison.Ordinal))
            {
                return given;
            }

            string full = Path.GetFullPath(given);

            if (!File.Exists(full))
            {
                throw new ArgumentException("There is no standard file " + full + ".");
            }

            return File.ReadAllText(full, Encoding.UTF8);
        }

        /// <summary>A measurement as the answer gives it: the box to 0.0001 and each frame's angle in degrees.</summary>
        private static MeasureResult Measured(MeasureRows rows)
        {
            var frames = new List<FrameInfo>();

            for (int i = 0; i + 4 < rows.Frames.Length; i += 5)
            {
                frames.Add(
                    new FrameInfo
                    {
                        X = EntityDescriber.R(rows.Frames[i]),
                        Y = EntityDescriber.R(rows.Frames[i + 1]),
                        Width = EntityDescriber.R(rows.Frames[i + 2]),
                        Height = EntityDescriber.R(rows.Frames[i + 3]),
                        Rotation = Math.Round(rows.Frames[i + 4] * 180.0 / Math.PI, 6),
                    }
                );
            }

            return new MeasureResult { Box = rows.Box?.Select(EntityDescriber.R).ToArray(), Frames = frames };
        }

        /// <summary>The attachment a justify name stands for: for a TEXT the words and the codes, for an MTEXT the codes alone.</summary>
        private static DxfAttachmentPoint Justification(string name, bool single)
        {
            var codes = new Dictionary<string, DxfAttachmentPoint>(StringComparer.Ordinal)
            {
                ["TL"] = DxfAttachmentPoint.TopLeft,
                ["TC"] = DxfAttachmentPoint.TopCenter,
                ["TR"] = DxfAttachmentPoint.TopRight,
                ["ML"] = DxfAttachmentPoint.MiddleLeft,
                ["MC"] = DxfAttachmentPoint.MiddleCenter,
                ["MR"] = DxfAttachmentPoint.MiddleRight,
                ["BL"] = DxfAttachmentPoint.BottomLeft,
                ["BC"] = DxfAttachmentPoint.BottomCenter,
                ["BR"] = DxfAttachmentPoint.BottomRight,
            };
            var words = new Dictionary<string, DxfAttachmentPoint>(StringComparer.Ordinal)
            {
                ["LEFT"] = DxfAttachmentPoint.BaseLeft,
                ["CENTER"] = DxfAttachmentPoint.BaseCenter,
                ["RIGHT"] = DxfAttachmentPoint.BaseRight,
                ["MIDDLE"] = DxfAttachmentPoint.MiddleCenter,
            };
            string key = (name ?? string.Empty).Trim().ToUpperInvariant();

            if (codes.TryGetValue(key, out DxfAttachmentPoint found) || (single && words.TryGetValue(key, out found)))
            {
                return found;
            }

            throw new ArgumentException(
                "A justification is one of "
                    + string.Join(
                        ", ",
                        single ? codes.Keys.Concat(words.Keys.Select(w => w.ToLowerInvariant())) : codes.Keys
                    )
                    + ", not "
                    + name
                    + "."
            );
        }

        private static void NotNegative(string what, double value)
        {
            if (double.IsNaN(value) || double.IsInfinity(value) || value < 0.0)
            {
                throw new ArgumentException(what + " is a finite number of nought or more, not " + value + ".");
            }
        }

        private static void Positive(string what, double value)
        {
            if (double.IsNaN(value) || double.IsInfinity(value) || value <= 0.0)
            {
                throw new ArgumentException(what + " is a finite number above nought, not " + value + ".");
            }
        }

        private static void Finite(string what, double value)
        {
            if (double.IsNaN(value) || double.IsInfinity(value))
            {
                throw new ArgumentException(what + " is a finite number, not " + value + ".");
            }
        }
    }
}
