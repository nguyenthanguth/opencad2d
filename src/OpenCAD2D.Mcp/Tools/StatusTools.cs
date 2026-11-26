using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Linq;
using ModelContextProtocol;
using ModelContextProtocol.Server;
using OpenCAD2D.Remote;

namespace OpenCAD2D.Mcp
{
    internal sealed partial class DrawingTools
    {
        private static readonly string[] Common =
        {
            "layer",
            "color",
            "linetype",
            "linetypeScale",
            "lineWeight",
            "box",
        };

        private static readonly IReadOnlyList<KindDescription> KindTable = new[]
        {
            Kind("LINE", "a straight segment", "add_line", new[] { "start", "end", "length", "angle" }, "start", "end"),
            Kind(
                "CIRCLE",
                "a circle",
                "add_circle",
                new[] { "center", "radius", "diameter", "circumference", "area" },
                "center",
                "radius"
            ),
            Kind(
                "ARC",
                "a circular arc, counter-clockwise from start_angle to end_angle in degrees",
                "add_arc",
                new[] { "center", "radius", "startAngle", "endAngle", "length" },
                "center",
                "radius",
                "start_angle",
                "end_angle"
            ),
            Kind(
                "LWPOLYLINE",
                "a polyline of straight sides, closed or open (add_rectangle, add_polygon and add_donut write one too)",
                "add_polyline, add_rectangle, add_polygon, add_donut",
                new[]
                {
                    "closed",
                    "vertexCount",
                    "vertices (at, bulge, startWidth, endWidth)",
                    "constantWidth",
                    "elevation",
                    "length",
                    "area",
                },
                "points",
                "closed"
            ),
            Kind(
                "TEXT",
                "one line of words",
                "add_text",
                new[] { "text", "at", "height", "rotation", "style", "widthFactor", "oblique", "justify" },
                "text",
                "at",
                "height",
                "rotation",
                "text_style",
                "justify"
            ),
            Kind(
                "MTEXT",
                "words that wrap in a width",
                "add_mtext",
                new[]
                {
                    "text",
                    "contents",
                    "at",
                    "height",
                    "width",
                    "rotation",
                    "style",
                    "attachment",
                    "columns (type, count, width, gutter)",
                },
                "text",
                "at",
                "height",
                "width",
                "text_style"
            ),
            Kind(
                "DIMENSION",
                "a dimension: RotatedDimension, AlignedDimension, RadialDimension, DiametricDimension, LineAngularDimension2, Point3AngularDimension, OrdinateDimension or ArcDimension",
                "add_linear_dim, add_aligned_dim, add_radial_dim, add_diameter_dim, add_angular_dim",
                new[]
                {
                    "type",
                    "style",
                    "measurement",
                    "unit",
                    "textOverride",
                    "text",
                    "textPosition",
                    "textRotation",
                    "the defining points by name",
                },
                "first",
                "second",
                "location",
                "text",
                "dim_style"
            ),
            Kind(
                "INSERT",
                "a block reference",
                "add_insert",
                new[] { "block", "at", "scale", "rotation", "attributes (tag, value)" },
                "name",
                "at"
            ),
            Kind(
                "HATCH",
                "a hatch of closed outlines",
                "add_hatch",
                new[] { "pattern", "solid", "gradient", "angle", "scale", "associative", "area" },
                "boundary",
                "bulges",
                "circle_center",
                "circle_radius",
                "handles",
                "pattern",
                "scale",
                "angle"
            ),
            Kind(
                "ELLIPSE",
                "an ellipse or an elliptical arc",
                "add_ellipse",
                new[] { "center", "majorRadius", "minorRadius", "majorAxisAngle", "startAngle", "endAngle" },
                "center",
                "major_axis_end",
                "ratio",
                "start_angle",
                "end_angle"
            ),
            Kind(
                "SPLINE",
                "a spline",
                "add_spline",
                new[] { "closed", "length" },
                "fit_points",
                "control_points",
                "degree",
                "closed"
            ),
            Kind("POINT", "a point", "add_point", new[] { "at" }, "at"),
            Kind(
                "XLINE",
                "a construction line without end both ways",
                "add_xline",
                new[] { "through", "angle" },
                "through",
                "angle"
            ),
            Kind(
                "RAY",
                "a construction line without end one way",
                "add_ray",
                new[] { "start", "angle" },
                "start",
                "angle"
            ),
            Kind(
                "LEADER",
                "a leader line with an arrowhead and no words",
                "add_leader",
                new[] { "points", "style" },
                "points",
                "dim_style"
            ),
            Kind(
                "MULTILEADER",
                "a leader with words or a block",
                "add_mleader",
                new[] { "style", "content", "text", "textHeight", "scale", "leaders" },
                "arrow",
                "landing",
                "text",
                "scale"
            ),
            Kind(
                "ACAD_TABLE",
                "a table of cells",
                "add_table",
                new[] { "style", "at", "rows", "columns", "cells (rows of text)" },
                "at",
                "columns",
                "rows",
                "cells"
            ),
            Kind(
                "ATTRIB",
                "the value of one attribute of a block reference (listed by get_attributes and get_texts)",
                null,
                new[] { "tag", "text", "at", "height", "style", "invisible", "constant" }
            ),
            Kind(
                "ATTDEF",
                "an attribute definition inside a block (listed by get_block)",
                "add_attribute_definition",
                new[] { "tag", "prompt", "text", "at", "height", "style", "invisible", "constant" },
                "tag",
                "at",
                "height",
                "prompt",
                "default_value",
                "text_style",
                "invisible",
                "constant"
            ),
            Kind(
                "VIEWPORT",
                "a window of a layout onto model space (listed by list_layouts)",
                "add_viewport",
                new[] { "see list_layouts" },
                "frame",
                "scale",
                "center",
                "freeze_layers"
            ),
            Record(
                "STYLE",
                "a text style, a record of the drawing and no entity (listed by list_text_styles)",
                "add_text_style",
                "list_text_styles"
            ),
            Record(
                "DIMSTYLE",
                "a dimension style, a record of the drawing and no entity (listed by list_dimension_styles)",
                "add_dimension_style",
                "list_dimension_styles"
            ),
            Record(
                "LAYER",
                "a layer, a record of the drawing and no entity (listed by list_layers)",
                "add_layer",
                "list_layers"
            ),
        };

        /// <summary>A kind that is a record of the drawing and no entity: a table record the add_* tools write, which another tool lists.</summary>
        private static KindDescription Record(string kind, string what, string written, string listedBy) =>
            new KindDescription
            {
                Kind = kind,
                What = what,
                Written = written,
                Fields = new List<string> { "see " + listedBy },
            };

        private static KindDescription Kind(
            string kind,
            string what,
            string written,
            string[] fields,
            params string[] arguments
        ) =>
            new KindDescription
            {
                Kind = kind,
                What = what,
                Written = written,
                Fields = Common.Concat(fields).ToList(),
                Arguments =
                    arguments.Length == 0
                        ? null
                        : arguments.Concat(new[] { "layer", "color", "linetype", "layout" }).ToList(),
            };

        [McpServerTool(
            Name = "describe_kinds",
            ReadOnly = true,
            Destructive = false,
            Idempotent = true,
            OpenWorld = false
        )]
        [Description(
            "The kinds of entity this server reads and writes, and the records (STYLE, DIMSTYLE, LAYER) the add_* tools make, with the fields get_entity gives for each entity kind (in details, beside the layer, colour and box every entity has) and the tool and arguments that make it where there is one: every add_* tool is named in the table. Other kinds in a drawing are listed by query_entities with their layer and box, and get_entity gives the Properties palette for them. Ask this before guessing a field name."
        )]
        public IReadOnlyList<KindDescription> DescribeKinds() => KindTable;

        [McpServerTool(
            Name = "server_status",
            ReadOnly = true,
            Destructive = false,
            Idempotent = true,
            OpenWorld = false
        )]
        [Description(
            "How the server stands: file mode or attached to the app, the flags in force (read-only, scripts allowed), the version, the drawing the tools work on (name, file, whether it has unsaved changes) and the snapshots held. Works with no drawing open, and says why where the app cannot be reached."
        )]
        public StatusResult ServerStatus() =>
            _session.RunWithout(() =>
            {
                McpOptions options = _session.Options;
                var status = new StatusResult
                {
                    Server = McpHost.ServerName,
                    Version = typeof(McpHost).Assembly.GetName().Version.ToString(3),
                    Mode = options.Attach ? "app" : "file",
                    ReadOnly = options.ReadOnly,
                    ScriptsAllowed = options.AllowScripts,
                    ProcessId = options.ProcessId,
                    Snapshots = _session.Snapshots.Ids,
                };

                try
                {
                    IDrawingBackend drawing = _session.Backend;
                    status.DrawingOpen = true;
                    status.Drawing = Describe(drawing);
                    status.CurrentLayout = drawing.CurrentLayout;
                    status.CurrentLayer = drawing.CurrentLayer;
                }
                catch (Exception exception) when (exception is McpException || exception is CadRemoteException)
                {
                    status.Problem = exception.Message;
                }

                return status;
            });
    }
}
