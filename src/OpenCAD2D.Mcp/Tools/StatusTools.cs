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
                "a polyline of straight sides, closed or open",
                "add_polyline",
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
                "text_style"
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
                "pattern",
                "scale",
                "angle"
            ),
            Kind(
                "ELLIPSE",
                "an ellipse or an elliptical arc",
                null,
                new[] { "center", "majorRadius", "minorRadius", "majorAxisAngle", "startAngle", "endAngle" }
            ),
            Kind("SPLINE", "a spline", null, new[] { "closed", "length" }),
            Kind("POINT", "a point", null, new[] { "at" }),
            Kind(
                "MULTILEADER",
                "a leader with words or a block",
                null,
                new[] { "style", "content", "text", "textHeight", "scale", "leaders" }
            ),
            Kind(
                "ACAD_TABLE",
                "a table of cells",
                null,
                new[] { "style", "at", "rows", "columns", "cells (rows of text)" }
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
                null,
                new[] { "tag", "prompt", "text", "at", "height", "style", "invisible", "constant" }
            ),
            Kind(
                "VIEWPORT",
                "a window of a layout onto model space (listed by list_layouts)",
                null,
                new[] { "see list_layouts" }
            ),
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
            "The kinds of entity this server reads and writes, with the fields get_entity gives for each (in details, beside the layer, colour and box every entity has) and the tool and arguments that make it where there is one. Other kinds in a drawing are listed by query_entities with their layer and box, and get_entity gives the Properties palette for them. Ask this before guessing a field name."
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
