using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Globalization;
using System.Linq;
using GeometryHelper.Dxf;
using GeometryHelper.Geometry;
using ModelContextProtocol.Server;

namespace OpenCAD2D.Mcp
{
    /// <summary>The tools that draw more shapes: rectangle, polygon, ellipse, spline, point, donut, construction lines, leaders, multileaders and tables.</summary>
    internal sealed partial class DrawingTools
    {
        [McpServerTool(
            Name = "add_rectangle",
            ReadOnly = false,
            Destructive = false,
            Idempotent = false,
            OpenWorld = false
        )]
        [Description(
            "Adds a rectangle as one closed polyline, as RECTANG, from two opposite corners. fillet rounds the four corners with that radius, chamfer cuts them that far along each side (not both). One undo step."
        )]
        public AddedResult AddRectangle(
            [Description("One corner [x, y].")] double[] corner1,
            [Description("The opposite corner [x, y].")] double[] corner2,
            [Description("Radius of the rounded corners in millimetres; 0 for sharp corners.")] double fillet = 0.0,
            [Description("Distance cut from each corner along the sides in millimetres; 0 for none.")]
                double chamfer = 0.0,
            [Description(LayerText)] string layer = null,
            [Description(ColorText)] int? color = null,
            [Description(LinetypeText)] string linetype = null,
            [Description(LayoutText)] string layout = null
        ) =>
            Add("add_rectangle", layout, _ =>
                EntityFactory.Looks(
                    CreateFactory.Rectangle(Point("corner1", corner1), Point("corner2", corner2), fillet, chamfer),
                    layer,
                    color,
                    linetype
                )
            );

        [McpServerTool(
            Name = "add_polygon",
            ReadOnly = false,
            Destructive = false,
            Idempotent = false,
            OpenWorld = false
        )]
        [Description(
            "Adds a regular polygon as one closed polyline, as POLYGON: inscribed, the radius is to the corners and the first corner is at the rotation; circumscribed (inscribed false), the radius is to the middle of the sides and a side's middle is at the rotation. One undo step."
        )]
        public AddedResult AddPolygon(
            [Description("The centre [x, y].")] double[] center,
            [Description("The radius in millimetres, above 0.")] double radius,
            [Description("The number of sides, 3 to 1024.")] int sides,
            [Description("True for a polygon inside a circle of the radius, false for one round it.")]
                bool inscribed = true,
            [Description("Rotation in degrees, counter-clockwise from the +x direction.")] double rotation = 0.0,
            [Description(LayerText)] string layer = null,
            [Description(ColorText)] int? color = null,
            [Description(LinetypeText)] string linetype = null,
            [Description(LayoutText)] string layout = null
        ) =>
            Add("add_polygon", layout, _ =>
                EntityFactory.Looks(
                    CreateFactory.Polygon(Point("center", center), radius, sides, inscribed, Radians(rotation)),
                    layer,
                    color,
                    linetype
                )
            );

        [McpServerTool(
            Name = "add_ellipse",
            ReadOnly = false,
            Destructive = false,
            Idempotent = false,
            OpenWorld = false
        )]
        [Description(
            "Adds an ellipse, or an elliptical arc, as ELLIPSE: about a centre, its major axis running to a point, the minor radius the ratio of the major. start_angle and end_angle (degrees from the major axis, counter-clockwise; 0 and 360 for the whole ellipse). One undo step."
        )]
        public AddedResult AddEllipse(
            [Description("The centre [x, y].")] double[] center,
            [Description("The end of the major axis [x, y]: its distance from the centre is the major radius.")]
                double[] major_axis_end,
            [Description("The minor radius over the major, above 0 and up to 1.")] double ratio,
            [Description("Start angle in degrees, 0 for a whole ellipse.")] double start_angle = 0.0,
            [Description("End angle in degrees, 360 for a whole ellipse.")] double end_angle = 360.0,
            [Description(LayerText)] string layer = null,
            [Description(ColorText)] int? color = null,
            [Description(LinetypeText)] string linetype = null,
            [Description(LayoutText)] string layout = null
        ) =>
            Add("add_ellipse", layout, _ =>
                EntityFactory.Looks(
                    CreateFactory.Ellipse(
                        Point("center", center),
                        Point("major_axis_end", major_axis_end),
                        ratio,
                        Radians(start_angle),
                        Radians(end_angle)
                    ),
                    layer,
                    color,
                    linetype
                )
            );

        [McpServerTool(
            Name = "add_spline",
            ReadOnly = false,
            Destructive = false,
            Idempotent = false,
            OpenWorld = false
        )]
        [Description(
            "Adds a spline, as SPLINE: a curve through the fit points given, or the curve a control polygon shapes (control_points, with its degree). Give one of the two. One undo step."
        )]
        public AddedResult AddSpline(
            [Description("Points the curve passes through, as [[x, y], ...], at least two.")]
                double[][] fit_points = null,
            [Description("The control polygon as [[x, y], ...], at least two, which the curve follows.")]
                double[][] control_points = null,
            [Description("The degree of a spline of control points, 1 to 10; 3 where left out.")] int degree = 3,
            [Description("True to close the curve.")] bool closed = false,
            [Description(LayerText)] string layer = null,
            [Description(ColorText)] int? color = null,
            [Description(LinetypeText)] string linetype = null,
            [Description(LayoutText)] string layout = null
        ) => Add("add_spline", layout, _ =>
            {
                if ((fit_points == null) == (control_points == null))
                {
                    throw new ArgumentException("Give fit_points or control_points, one of the two.");
                }

                return EntityFactory.Looks(
                    fit_points != null
                        ? CreateFactory.FitSpline(Points("fit_points", fit_points), closed)
                        : CreateFactory.ControlSpline(Points("control_points", control_points), degree, closed),
                    layer,
                    color,
                    linetype
                );
            });

        [McpServerTool(
            Name = "add_point",
            ReadOnly = false,
            Destructive = false,
            Idempotent = false,
            OpenWorld = false
        )]
        [Description("Adds a point object, drawn as the drawing's PDMODE says. One undo step.")]
        public AddedResult AddPoint(
            [Description("The point [x, y].")] double[] at,
            [Description(LayerText)] string layer = null,
            [Description(ColorText)] int? color = null,
            [Description(LinetypeText)] string linetype = null,
            [Description(LayoutText)] string layout = null
        ) =>
            Add("add_point", layout, _ =>
                EntityFactory.Looks(CreateFactory.Point(Point("at", at)), layer, color, linetype)
            );

        [McpServerTool(
            Name = "add_donut",
            ReadOnly = false,
            Destructive = false,
            Idempotent = false,
            OpenWorld = false
        )]
        [Description(
            "Adds a donut, as DONUT: a filled ring between an inner and an outer diameter, one closed polyline of two arcs with width. An inner diameter of 0 is a filled disc. One undo step."
        )]
        public AddedResult AddDonut(
            [Description("The centre [x, y].")] double[] center,
            [Description("The inner diameter in millimetres, 0 or more.")] double inner_diameter,
            [Description("The outer diameter in millimetres, above the inner.")] double outer_diameter,
            [Description(LayerText)] string layer = null,
            [Description(ColorText)] int? color = null,
            [Description(LinetypeText)] string linetype = null,
            [Description(LayoutText)] string layout = null
        ) =>
            Add("add_donut", layout, _ =>
                EntityFactory.Looks(
                    CreateFactory.Donut(Point("center", center), inner_diameter, outer_diameter),
                    layer,
                    color,
                    linetype
                )
            );

        [McpServerTool(
            Name = "add_xline",
            ReadOnly = false,
            Destructive = false,
            Idempotent = false,
            OpenWorld = false
        )]
        [Description(
            "Adds a construction line through a point at an angle, running on without end both ways, as XLINE. One undo step."
        )]
        public AddedResult AddXline(
            [Description("A point on the line [x, y].")] double[] through,
            [Description("The angle in degrees from the +x direction.")] double angle,
            [Description(LayerText)] string layer = null,
            [Description(ColorText)] int? color = null,
            [Description(LinetypeText)] string linetype = null,
            [Description(LayoutText)] string layout = null
        ) =>
            Add("add_xline", layout, _ =>
                EntityFactory.Looks(
                    CreateFactory.Xline(Point("through", through), Radians(angle)),
                    layer,
                    color,
                    linetype
                )
            );

        [McpServerTool(Name = "add_ray", ReadOnly = false, Destructive = false, Idempotent = false, OpenWorld = false)]
        [Description("Adds a ray from a point at an angle, running on without end one way, as RAY. One undo step.")]
        public AddedResult AddRay(
            [Description("The start of the ray [x, y].")] double[] start,
            [Description("The angle in degrees from the +x direction.")] double angle,
            [Description(LayerText)] string layer = null,
            [Description(ColorText)] int? color = null,
            [Description(LinetypeText)] string linetype = null,
            [Description(LayoutText)] string layout = null
        ) =>
            Add("add_ray", layout, _ =>
                EntityFactory.Looks(CreateFactory.Ray(Point("start", start), Radians(angle)), layer, color, linetype)
            );

        [McpServerTool(
            Name = "add_leader",
            ReadOnly = false,
            Destructive = false,
            Idempotent = false,
            OpenWorld = false
        )]
        [Description(
            "Adds a leader line, as LEADER with no annotation: an arrowhead at the first point and straight segments through the rest, in a dimension style (its arrow size and scale). Use add_mleader for a note with words. One undo step."
        )]
        public AddedResult AddLeader(
            [Description("The points as [[x, y], ...], at least two; the arrowhead is at the first.")]
                double[][] points,
            [Description("A dimension style of the drawing; the current one where left out.")] string dim_style = null,
            [Description(LayerText)] string layer = null,
            [Description(ColorText)] int? color = null,
            [Description(LinetypeText)] string linetype = null,
            [Description(LayoutText)] string layout = null
        ) =>
            Add("add_leader", layout, drawing =>
                EntityFactory.Looks(
                    CreateFactory.Leader(Points("points", points), DimStyle(drawing, dim_style)),
                    layer,
                    color,
                    linetype
                )
            );

        /// <summary>Runs work with a layout shown, then shows the one that was; the caller's group makes the steps one.</summary>
        private static void InLayout(IDrawingBackend drawing, string layout, Action work)
        {
            IDrawingEditing editing = Editing(drawing);
            string wanted = Layout(drawing, layout);
            string was = drawing.CurrentLayout;

            if (string.Equals(wanted, was, StringComparison.OrdinalIgnoreCase))
            {
                work();
                return;
            }

            editing.SetCurrentLayout(wanted);

            try
            {
                work();
            }
            finally
            {
                editing.SetCurrentLayout(was);
            }
        }

        private static AddedResult Added(ModifiedResult made, string kind)
        {
            if (made.Created.Count == 0)
            {
                throw new InvalidOperationException("Nothing was made. The command line said: " + made.Output);
            }

            return new AddedResult { Handle = made.Created[0], Kind = kind };
        }

        /// <summary>Gives made objects the layer and colour asked for, inside the undo step they were made in.</summary>
        private static void Look(IDrawingEditing editing, IReadOnlyList<string> handles, string layer, int? color)
        {
            var rows = new List<string[]>();

            if (!string.IsNullOrWhiteSpace(layer))
            {
                rows.Add(new[] { "Layer", layer.Trim() });
            }

            if (color.HasValue)
            {
                rows.Add(new[] { "Color", ColorRow(color.Value) });
            }

            if (rows.Count > 0)
            {
                editing.Make(
                    "set_rows",
                    new CadMakeArgs().Set("handles", handles.ToArray()).Set("rows", rows.ToArray())
                );
            }
        }

        private static string ColorRow(int index)
        {
            if (index < 0 || index > 256)
            {
                throw new ArgumentException("A colour index is 0 to 256.");
            }

            return index == 256 ? "ByLayer"
                : index == 0 ? "ByBlock"
                : index.ToString(CultureInfo.InvariantCulture);
        }

        [McpServerTool(
            Name = "add_mleader",
            ReadOnly = false,
            Destructive = false,
            Idempotent = false,
            OpenWorld = false
        )]
        [Description(
            "Adds a multileader note, as MLEADER: an arrowhead pointing at one place, a leader line to a landing and the words beside it, in a multileader style of the drawing (its text height, arrow and landing). One undo step."
        )]
        public AddedResult AddMLeader(
            [Description("Where the arrowhead points [x, y].")] double[] arrow,
            [Description("Where the leader lands, the words beyond it [x, y].")] double[] landing,
            [Description("The words; \\P starts a new line.")] string text,
            [Description("A multileader style of the drawing; the current one where left out.")] string style = null,
            [Description(
                "The scale of the note, above 0: the style's text height and arrow size are multiplied by it (10 for a note on a sheet plotted at 1:10); the drawing's MLEADERSCALE where left out."
            )]
                double? scale = null,
            [Description(LayerText)] string layer = null,
            [Description(ColorText)] int? color = null,
            [Description(LayoutText)] string layout = null
        ) => _session.Run("add_mleader", true, drawing =>
            {
                GeoPoint3 a = Point("arrow", arrow);
                GeoPoint3 b = Point("landing", landing);
                Words(text);

                if (Math.Abs(a.X - b.X) <= Tol.EqualPoint && Math.Abs(a.Y - b.Y) <= Tol.EqualPoint)
                {
                    throw new ArgumentException("The landing is not at the arrow.");
                }

                IDrawingEditing editing = Editing(drawing);
                ModifiedResult made = null;
                editing.Group(() => InLayout(drawing, layout, () =>
                    {
                        bool styled = !string.IsNullOrWhiteSpace(style);
                        string was = styled
                            ? Convert.ToString(drawing.Variable("CMLEADERSTYLE"), CultureInfo.InvariantCulture)
                            : null;

                        // A drawing with no style of that name (a new one has none until a note is made) has none to go back to.
                        bool restore =
                            !string.IsNullOrEmpty(was)
                            && CadMLeaderStyleEditor.Find(Q(drawing).ReadDocument().Database, was) != null;
                        object wasScale = scale.HasValue ? drawing.Variable("MLEADERSCALE") : null;

                        // The style and the scale the note is made in are put back whatever comes of it.
                        try
                        {
                            if (styled)
                            {
                                editing.Make(
                                    "current_style",
                                    new CadMakeArgs().Set("kind", "mleader").Set("name", style.Trim())
                                );
                            }

                            if (scale.HasValue)
                            {
                                if (!(scale.Value > 0.0) || double.IsInfinity(scale.Value))
                                {
                                    throw new ArgumentException("The scale is a number above nought.");
                                }

                                editing.SetVariable("MLEADERSCALE", scale.Value);
                            }

                            made = Script(drawing, new List<string>(), true, () =>
                                new CommandScript().Word("MLEADER").Point(a.X, a.Y).Point(b.X, b.Y).Text(text)
                            );
                            Look(editing, made.Created, layer, color);
                        }
                        finally
                        {
                            if (wasScale != null)
                            {
                                editing.SetVariable("MLEADERSCALE", wasScale);
                            }

                            if (restore)
                            {
                                editing.Make(
                                    "current_style",
                                    new CadMakeArgs().Set("kind", "mleader").Set("name", was)
                                );
                            }
                        }
                    }));
                return Added(made, "MULTILEADER");
            });

        [McpServerTool(
            Name = "add_table",
            ReadOnly = false,
            Destructive = false,
            Idempotent = false,
            OpenWorld = false
        )]
        [Description(
            "Adds a table, as TABLE: a title row, a header row and the data rows under them, each column column_width wide, its top left corner at a point, in a table style of the drawing. cells fills the words from the top left cell (the first row is the title, the second the header), a row of texts at a time. One undo step."
        )]
        public AddedResult AddTable(
            [Description("The top left corner of the table [x, y].")] double[] at,
            [Description("The number of columns, 1 to 100.")] int columns,
            [Description("The number of data rows under the title and the header, 1 to 500.")] int rows,
            [Description(
                "The words as rows of texts, [[title], [header, header], [data, data], ...]; an empty text leaves a cell empty."
            )]
                string[][] cells = null,
            [Description("The width of a column in millimetres; the table style's where left out.")]
                double? column_width = null,
            [Description("The height of a data row in lines of text, 1 or more; 1 where left out.")] int row_lines = 1,
            [Description("A table style of the drawing; the current one where left out.")] string style = null,
            [Description(LayerText)] string layer = null,
            [Description(ColorText)] int? color = null,
            [Description(LayoutText)] string layout = null
        ) => _session.Run("add_table", true, drawing =>
            {
                GeoPoint3 p = Point("at", at);

                if (columns < 1 || columns > 100 || rows < 1 || rows > 500 || row_lines < 1 || row_lines > 50)
                {
                    throw new ArgumentException(
                        "A table has 1 to 100 columns, 1 to 500 data rows and 1 to 50 lines to a row."
                    );
                }

                if (column_width.HasValue && (!(column_width.Value > 0.0) || double.IsInfinity(column_width.Value)))
                {
                    throw new ArgumentException("The column width is a length above nought.");
                }

                if (cells != null && (cells.Length > rows + 2 || cells.Any(r => r == null || r.Length > columns)))
                {
                    throw new ArgumentException(
                        "The table has "
                            + (rows + 2)
                            + " rows with the title and the header, and "
                            + columns
                            + " columns: the cells do not fit."
                    );
                }

                IDrawingEditing editing = Editing(drawing);
                ModifiedResult made = null;
                editing.Group(() => InLayout(drawing, layout, () =>
                    {
                        string was = string.IsNullOrWhiteSpace(style)
                            ? null
                            : Convert.ToString(drawing.Variable("CTABLESTYLE"), CultureInfo.InvariantCulture);

                        // The style the table is made in is put back whatever comes of it.
                        try
                        {
                            if (was != null)
                            {
                                editing.Make(
                                    "current_style",
                                    new CadMakeArgs().Set("kind", "table").Set("name", style.Trim())
                                );
                            }

                            made = Script(drawing, new List<string>(), true, () =>
                            {
                                CommandScript script = new CommandScript()
                                    .Word("-TABLE")
                                    .Word(columns.ToString(CultureInfo.InvariantCulture))
                                    .Word(rows.ToString(CultureInfo.InvariantCulture));

                                if (column_width.HasValue)
                                {
                                    script.Word("W").Number(column_width.Value);
                                }

                                if (row_lines != 1)
                                {
                                    script.Word("H").Word(row_lines.ToString(CultureInfo.InvariantCulture));
                                }

                                return script.Point(p.X, p.Y);
                            });
                            string handle = made.Created.FirstOrDefault()
                                ?? throw new InvalidOperationException(
                                    "The table was not made. The command line said: " + made.Output
                                );

                            if (cells != null && cells.Length > 0)
                            {
                                editing.Make(
                                    "table_cells",
                                    new CadMakeArgs()
                                        .Set("handle", handle)
                                        .Set(
                                            "cells",
                                            cells.Select(r => r.Select(c => c ?? string.Empty).ToArray()).ToArray()
                                        )
                                );
                            }

                            Look(editing, new[] { handle }, layer, color);
                        }
                        finally
                        {
                            if (!string.IsNullOrEmpty(was))
                            {
                                editing.Make("current_style", new CadMakeArgs().Set("kind", "table").Set("name", was));
                            }
                        }
                    }));
                return Added(made, "ACAD_TABLE");
            });
    }
}
