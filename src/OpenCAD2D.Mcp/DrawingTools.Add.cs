using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Linq;
using GeometryHelper.Dxf;
using GeometryHelper.Geometry;
using ModelContextProtocol.Server;

namespace OpenCAD2D.Mcp
{
    /// <summary>The tools that add an entity: each call is one undo step and gives the handle the entity has.</summary>
    internal sealed partial class DrawingTools
    {
        private const string LayerText =
            "The layer; the drawing's current layer where left out. A layer the drawing has not is made.";
        private const string ColorText = "AutoCAD colour index 1 to 255; BYLAYER where left out.";
        private const string LinetypeText = "A linetype of the drawing; BYLAYER where left out.";
        private const string LayoutText = "The layout to add to; model space where left out.";

        [McpServerTool(Name = "add_line", ReadOnly = false, Destructive = false, Idempotent = false, OpenWorld = false)]
        [Description("Adds a line from one point to another. Points are [x, y] in millimetres. One undo step.")]
        public AddedResult AddLine(
            [Description("Start point [x, y].")] double[] start,
            [Description("End point [x, y].")] double[] end,
            [Description(LayerText)] string layer = null,
            [Description(ColorText)] int? color = null,
            [Description(LinetypeText)] string linetype = null,
            [Description(LayoutText)] string layout = null
        ) =>
            Add("add_line", layout, _ =>
                EntityFactory.Looks(
                    EntityFactory.Line(Point("start", start), Point("end", end)),
                    layer,
                    color,
                    linetype
                )
            );

        [McpServerTool(
            Name = "add_polyline",
            ReadOnly = false,
            Destructive = false,
            Idempotent = false,
            OpenWorld = false
        )]
        [Description(
            "Adds a polyline of straight sides through the points, closed where asked (the last point joins the first). One undo step."
        )]
        public AddedResult AddPolyline(
            [Description("Corners as [[x, y], [x, y], ...], at least two.")] double[][] points,
            [Description("True to close it with a side from the last point to the first.")] bool closed = false,
            [Description(LayerText)] string layer = null,
            [Description(ColorText)] int? color = null,
            [Description(LinetypeText)] string linetype = null,
            [Description(LayoutText)] string layout = null
        ) =>
            Add("add_polyline", layout, _ =>
                EntityFactory.Looks(EntityFactory.Polyline(Points("points", points), closed), layer, color, linetype)
            );

        [McpServerTool(
            Name = "add_circle",
            ReadOnly = false,
            Destructive = false,
            Idempotent = false,
            OpenWorld = false
        )]
        [Description("Adds a circle about a centre. One undo step.")]
        public AddedResult AddCircle(
            [Description("Centre [x, y].")] double[] center,
            [Description("Radius in millimetres, above 0.")] double radius,
            [Description(LayerText)] string layer = null,
            [Description(ColorText)] int? color = null,
            [Description(LinetypeText)] string linetype = null,
            [Description(LayoutText)] string layout = null
        ) =>
            Add("add_circle", layout, _ =>
                EntityFactory.Looks(EntityFactory.Circle(Point("center", center), radius), layer, color, linetype)
            );

        [McpServerTool(Name = "add_arc", ReadOnly = false, Destructive = false, Idempotent = false, OpenWorld = false)]
        [Description(
            "Adds an arc, counter-clockwise from the start angle to the end angle, in degrees from the +x direction: 0 to 90 is the quarter of a circle in the first quadrant. One undo step."
        )]
        public AddedResult AddArc(
            [Description("Centre [x, y].")] double[] center,
            [Description("Radius in millimetres, above 0.")] double radius,
            [Description("Start angle in degrees.")] double start_angle,
            [Description("End angle in degrees.")] double end_angle,
            [Description(LayerText)] string layer = null,
            [Description(ColorText)] int? color = null,
            [Description(LinetypeText)] string linetype = null,
            [Description(LayoutText)] string layout = null
        ) =>
            Add("add_arc", layout, _ =>
                EntityFactory.Looks(
                    EntityFactory.Arc(Point("center", center), radius, Radians(start_angle), Radians(end_angle)),
                    layer,
                    color,
                    linetype
                )
            );

        [McpServerTool(Name = "add_text", ReadOnly = false, Destructive = false, Idempotent = false, OpenWorld = false)]
        [Description(
            "Adds a single-line text (TEXT) at an insertion point, with its baseline turned by rotation degrees; with justify, the point is the one the words stand about (measure_text takes the same justify, so the box it gives is the box of the text added). One undo step."
        )]
        public AddedResult AddText(
            [Description("The words.")] string text,
            [Description(
                "Insertion point [x, y]: the lower left corner of the first letter, or the point the text stands about where justify is given."
            )]
                double[] at,
            [Description("Letter height in millimetres, above 0.")] double height,
            [Description("Rotation in degrees, counter-clockwise.")] double rotation = 0.0,
            [Description("A text style of the drawing (list_text_styles); Standard where left out.")]
                string text_style = null,
            [Description(
                "left, center, right, middle or a code TL, TC, TR, ML, MC, MR, BL, BC, BR (either case); the insertion point is the lower left corner of the first letter where left out."
            )]
                string justify = null,
            [Description(LayerText)] string layer = null,
            [Description(ColorText)] int? color = null,
            [Description(LinetypeText)] string linetype = null,
            [Description(LayoutText)] string layout = null
        ) => Add("add_text", layout, _ =>
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

                return EntityFactory.Looks(made, layer, color, linetype);
            });

        [McpServerTool(
            Name = "add_mtext",
            ReadOnly = false,
            Destructive = false,
            Idempotent = false,
            OpenWorld = false
        )]
        [Description(
            "Adds a multi-line text (MTEXT) at an insertion point, wrapped at a width. \\P in the text is a new line. One undo step."
        )]
        public AddedResult AddMText(
            [Description("The words; \\P starts a new line.")] string text,
            [Description("Insertion point [x, y]: the top left corner of the text.")] double[] at,
            [Description("Letter height in millimetres, above 0.")] double height,
            [Description("Width the lines wrap at in millimetres; 0 or left out for no wrapping.")] double width = 0.0,
            [Description("A text style of the drawing (list_text_styles); Standard where left out.")]
                string text_style = null,
            [Description(LayerText)] string layer = null,
            [Description(ColorText)] int? color = null,
            [Description(LinetypeText)] string linetype = null,
            [Description(LayoutText)] string layout = null
        ) =>
            Add("add_mtext", layout, _ =>
                EntityFactory.Looks(
                    EntityFactory.MText(Words(text), Point("at", at), height, width, text_style),
                    layer,
                    color,
                    linetype
                )
            );

        [McpServerTool(
            Name = "add_linear_dim",
            ReadOnly = false,
            Destructive = false,
            Idempotent = false,
            OpenWorld = false
        )]
        [Description(
            "Adds a linear dimension of the distance between two points along a direction, as DIMLINEAR: the dimension line runs through location, level or upright. With no angle it is level when location is above or below the two points and upright when it is to their left or right. One undo step."
        )]
        public AddedResult AddLinearDim(
            [Description("First point [x, y].")] double[] first,
            [Description("Second point [x, y].")] double[] second,
            [Description("A point [x, y] the dimension line passes through.")] double[] location,
            [Description("Direction of the dimension line in degrees; chosen from location where left out.")]
                double? angle = null,
            [Description("The words in place of the measurement; <> stands for the measurement, as in \"<> TYP\".")]
                string text = null,
            [Description("A dimension style of the drawing (list_dimension_styles); the current one where left out.")]
                string dim_style = null,
            [Description(LayerText)] string layer = null,
            [Description(ColorText)] int? color = null,
            [Description(LinetypeText)] string linetype = null,
            [Description(LayoutText)] string layout = null
        ) =>
            Add("add_linear_dim", layout, drawing =>
                EntityFactory.Looks(
                    EntityFactory.LinearDimension(
                        Point("first", first),
                        Point("second", second),
                        Point("location", location),
                        angle.HasValue ? Radians(angle.Value) : (double?)null,
                        text,
                        DimStyle(drawing, dim_style)
                    ),
                    layer,
                    color,
                    linetype
                )
            );

        [McpServerTool(
            Name = "add_aligned_dim",
            ReadOnly = false,
            Destructive = false,
            Idempotent = false,
            OpenWorld = false
        )]
        [Description(
            "Adds an aligned dimension of the distance between two points, as DIMALIGNED: the dimension line is parallel to the two points and passes through location. One undo step."
        )]
        public AddedResult AddAlignedDim(
            [Description("First point [x, y].")] double[] first,
            [Description("Second point [x, y].")] double[] second,
            [Description("A point [x, y] the dimension line passes through.")] double[] location,
            [Description("The words in place of the measurement; <> stands for the measurement.")] string text = null,
            [Description("A dimension style of the drawing; the current one where left out.")] string dim_style = null,
            [Description(LayerText)] string layer = null,
            [Description(ColorText)] int? color = null,
            [Description(LinetypeText)] string linetype = null,
            [Description(LayoutText)] string layout = null
        ) =>
            Add("add_aligned_dim", layout, drawing =>
                EntityFactory.Looks(
                    EntityFactory.AlignedDimension(
                        Point("first", first),
                        Point("second", second),
                        Point("location", location),
                        text,
                        DimStyle(drawing, dim_style)
                    ),
                    layer,
                    color,
                    linetype
                )
            );

        [McpServerTool(
            Name = "add_radial_dim",
            ReadOnly = false,
            Destructive = false,
            Idempotent = false,
            OpenWorld = false
        )]
        [Description(
            "Adds a radius dimension of the circle or arc about a centre, as DIMRADIUS: the line runs from the centre toward location, which also says where the words stand. The words read R and the radius. One undo step."
        )]
        public AddedResult AddRadialDim(
            [Description("Centre [x, y] of the circle or arc.")] double[] center,
            [Description("Its radius in millimetres, above 0.")] double radius,
            [Description("A point [x, y] the line runs toward and the words stand near.")] double[] location,
            [Description("The words in place of the measurement.")] string text = null,
            [Description("A dimension style of the drawing; the current one where left out.")] string dim_style = null,
            [Description(LayerText)] string layer = null,
            [Description(ColorText)] int? color = null,
            [Description(LinetypeText)] string linetype = null,
            [Description(LayoutText)] string layout = null
        ) =>
            Add("add_radial_dim", layout, drawing =>
                EntityFactory.Looks(
                    EntityFactory.RadialDimension(
                        Point("center", center),
                        radius,
                        Point("location", location),
                        text,
                        DimStyle(drawing, dim_style)
                    ),
                    layer,
                    color,
                    linetype
                )
            );

        [McpServerTool(
            Name = "add_diameter_dim",
            ReadOnly = false,
            Destructive = false,
            Idempotent = false,
            OpenWorld = false
        )]
        [Description(
            "Adds a diameter dimension of the circle or arc about a centre, as DIMDIAMETER: the line runs across the circle along the line from the centre through location. The words read the diameter sign and the diameter. One undo step."
        )]
        public AddedResult AddDiameterDim(
            [Description("Centre [x, y] of the circle or arc.")] double[] center,
            [Description("Its radius in millimetres, above 0.")] double radius,
            [Description("A point [x, y] giving the direction of the line.")] double[] location,
            [Description("The words in place of the measurement.")] string text = null,
            [Description("A dimension style of the drawing; the current one where left out.")] string dim_style = null,
            [Description(LayerText)] string layer = null,
            [Description(ColorText)] int? color = null,
            [Description(LinetypeText)] string linetype = null,
            [Description(LayoutText)] string layout = null
        ) =>
            Add("add_diameter_dim", layout, drawing =>
                EntityFactory.Looks(
                    EntityFactory.DiameterDimension(
                        Point("center", center),
                        radius,
                        Point("location", location),
                        text,
                        DimStyle(drawing, dim_style)
                    ),
                    layer,
                    color,
                    linetype
                )
            );

        [McpServerTool(
            Name = "add_angular_dim",
            ReadOnly = false,
            Destructive = false,
            Idempotent = false,
            OpenWorld = false
        )]
        [Description(
            "Adds an angular dimension of the angle at a vertex between the rays through two points, as DIMANGULAR from three points: its arc passes through location, which says the radius of the arc and which of the four angles the rays make is measured. One undo step."
        )]
        public AddedResult AddAngularDim(
            [Description("The vertex [x, y].")] double[] vertex,
            [Description("A point [x, y] on the first ray.")] double[] first,
            [Description("A point [x, y] on the second ray.")] double[] second,
            [Description("A point [x, y] the arc passes through.")] double[] location,
            [Description("The words in place of the measurement.")] string text = null,
            [Description("A dimension style of the drawing; the current one where left out.")] string dim_style = null,
            [Description(LayerText)] string layer = null,
            [Description(ColorText)] int? color = null,
            [Description(LinetypeText)] string linetype = null,
            [Description(LayoutText)] string layout = null
        ) =>
            Add("add_angular_dim", layout, drawing =>
                EntityFactory.Looks(
                    EntityFactory.AngularDimension(
                        Point("vertex", vertex),
                        Point("first", first),
                        Point("second", second),
                        Point("location", location),
                        text,
                        DimStyle(drawing, dim_style)
                    ),
                    layer,
                    color,
                    linetype
                )
            );

        [McpServerTool(
            Name = "add_hatch",
            ReadOnly = false,
            Destructive = false,
            Idempotent = false,
            OpenWorld = false
        )]
        [Description(
            "Adds a hatch of the area a closed outline encloses, with a pattern of the built-in set (ANSI31 to ANSI38, BRICK, EARTH, HONEY, STEEL, AR-CONC and the rest; SOLID for a fill). The outline is given as corners (boundary, with bulges where a side is an arc), as a circle (circle_center and circle_radius), as the closed polylines and circles that are already in the drawing (handles), or as several of these; the first is the outer outline and the others are islands or pieces, the fill alternating as the display fills it. The hatch follows no object: it is the outline as given. One undo step."
        )]
        public AddedResult AddHatch(
            [Description("Corners of the outline as [[x, y], ...], at least three; the last joins the first.")]
                double[][] boundary = null,
            [Description(
                "A bulge for each corner, as a polyline's: the tangent of a quarter of the angle the arc from that corner to the next turns through, counter-clockwise where above 0, clockwise where below 0 (1 is a half circle); 0 for a straight side. As many as boundary has corners; all straight where left out."
            )]
                double[] bulges = null,
            [Description("The centre [x, y] of a circular outline; with circle_radius.")] double[] circle_center = null,
            [Description("The radius of a circular outline in millimetres, above 0; with circle_center.")]
                double? circle_radius = null,
            [Description(
                "Handles of closed polylines (LWPOLYLINE, straight or with arcs) and circles of the drawing to hatch inside; each is one outline."
            )]
                string[] handles = null,
            [Description("A pattern name, or SOLID.")] string pattern = "ANSI31",
            [Description("Pattern scale, above 0.")] double scale = 1.0,
            [Description("Pattern angle in degrees.")] double angle = 0.0,
            [Description(LayerText)] string layer = null,
            [Description(ColorText)] int? color = null,
            [Description(LinetypeText)] string linetype = null,
            [Description(LayoutText)] string layout = null
        ) =>
            Add("add_hatch", layout, drawing =>
                EntityFactory.Looks(
                    EntityFactory.Hatch(
                        HatchLoops(drawing, boundary, bulges, circle_center, circle_radius, handles),
                        pattern,
                        scale,
                        Radians(angle)
                    ),
                    layer,
                    color,
                    linetype
                )
            );

        /// <summary>The outlines of an <c>add_hatch</c>: the corners given (with their bulges), the circle, and the closed polylines and circles of the drawing.</summary>
        private static List<(GeoPoint2[] Points, double[] Bulges)> HatchLoops(
            IDrawingBackend drawing,
            double[][] boundary,
            double[] bulges,
            double[] circleCenter,
            double? circleRadius,
            string[] handles
        )
        {
            var loops = new List<(GeoPoint2[] Points, double[] Bulges)>();

            if (boundary != null)
            {
                List<GeoPoint3> corners = Points("boundary", boundary);

                // Two corners with bulges are two arcs, a circle or a lens; with none they are no area.
                if (corners.Count < (bulges != null ? 2 : 3))
                {
                    throw new ArgumentException("A hatch boundary needs at least three points.");
                }

                if (bulges != null && bulges.Length != corners.Count)
                {
                    throw new ArgumentException(
                        "bulges has a bulge for each corner: " + corners.Count + " of them, not " + bulges.Length + "."
                    );
                }

                if (bulges != null && bulges.Any(b => double.IsNaN(b) || double.IsInfinity(b)))
                {
                    throw new ArgumentException("A bulge is a finite number.");
                }

                loops.Add(
                    (
                        corners.Select(c => new GeoPoint2(c.X, c.Y)).ToArray(),
                        bulges != null ? (double[])bulges.Clone() : new double[corners.Count]
                    )
                );
            }
            else if (bulges != null)
            {
                throw new ArgumentException("bulges go with boundary: give the corners they belong to.");
            }

            if (circleCenter != null || circleRadius.HasValue)
            {
                if (circleCenter == null || !circleRadius.HasValue)
                {
                    throw new ArgumentException("A circular outline needs circle_center and circle_radius both.");
                }

                loops.Add(Circular(Point("circle_center", circleCenter), circleRadius.Value));
            }

            if (handles != null && handles.Length > 0)
            {
                foreach (string handle in Handles(handles))
                {
                    EntityRef found = Editing(drawing).Objects(new[] { handle }).FirstOrDefault();

                    switch (found?.Entity)
                    {
                        case null:
                            throw new ArgumentException("The drawing has no entity with the handle " + handle + ".");
                        case DxfCircle circle:
                            loops.Add(Circular(circle.Center, circle.Radius));
                            break;
                        case DxfPolyline polyline when polyline.Closed && polyline.NumberOfVertices >= 2:
                            int count = polyline.NumberOfVertices;
                            loops.Add(
                                (
                                    Enumerable.Range(0, count).Select(i => polyline.GetPoint2dAt(i)).ToArray(),
                                    Enumerable.Range(0, count).Select(i => polyline.GetBulgeAt(i)).ToArray()
                                )
                            );
                            break;
                        default:
                            throw new ArgumentException(
                                "The entity "
                                    + handle
                                    + " is "
                                    + (
                                        found.Entity is DxfPolyline
                                            ? "a polyline that is not closed"
                                            : "a " + found.Entity.DxfName
                                    )
                                    + ": a hatch takes the handle of a closed polyline or a circle."
                            );
                    }
                }
            }

            if (loops.Count == 0)
            {
                throw new ArgumentException(
                    "A hatch needs an outline: give boundary (the corners), circle_center and circle_radius, or the handles of closed polylines and circles."
                );
            }

            return loops;
        }

        /// <summary>A circle as the outline of a hatch: two vertices on its ends across, each a half circle (bulge 1) to the other.</summary>
        private static (GeoPoint2[] Points, double[] Bulges) Circular(GeoPoint3 center, double radius)
        {
            if (!(radius > 0.0) || double.IsInfinity(radius))
            {
                throw new ArgumentException("The radius of a circular outline is a number above nought.");
            }

            return (
                new[] { new GeoPoint2(center.X + radius, center.Y), new GeoPoint2(center.X - radius, center.Y) },
                new[] { 1.0, 1.0 }
            );
        }

        [McpServerTool(
            Name = "add_insert",
            ReadOnly = false,
            Destructive = false,
            Idempotent = false,
            OpenWorld = false
        )]
        [Description(
            "Places a block of the drawing (list_blocks) at a point, scaled and turned, as INSERT. One undo step."
        )]
        public AddedResult AddInsert(
            [Description("The name of a block of the drawing.")] string name,
            [Description("Insertion point [x, y].")] double[] at,
            [Description("Scale factor, above 0.")] double scale = 1.0,
            [Description("Rotation in degrees, counter-clockwise.")] double rotation = 0.0,
            [Description(LayerText)] string layer = null,
            [Description(ColorText)] int? color = null,
            [Description(LinetypeText)] string linetype = null,
            [Description(LayoutText)] string layout = null
        ) =>
            Add("add_insert", layout, _ =>
                EntityFactory.Looks(
                    EntityFactory.Insert(name, Point("at", at), scale, Radians(rotation)),
                    layer,
                    color,
                    linetype
                )
            );

        /// <summary>Makes the entity and adds it as one undo step.</summary>
        private AddedResult Add(string tool, string layout, Func<IDrawingBackend, DxfEntity> make) =>
            _session.Run(tool, true, drawing =>
            {
                DxfEntity entity = make(drawing);
                string space = Layout(drawing, layout);
                IReadOnlyList<string> handles = drawing.Add(new[] { entity }, space == "Model" ? null : space);
                return new AddedResult { Handle = handles.FirstOrDefault(), Kind = entity.DxfName };
            });

        private static string DimStyle(IDrawingBackend drawing, string style) =>
            string.IsNullOrWhiteSpace(style) ? drawing.CurrentDimensionStyle : style.Trim();

        private static string Words(string text) =>
            string.IsNullOrEmpty(text) ? throw new ArgumentException("A text needs words.") : text;

        private static double Radians(double degrees) => degrees * Math.PI / 180.0;

        private static GeoPoint3 Point(string name, double[] value)
        {
            if (
                value == null
                || (value.Length != 2 && value.Length != 3)
                || value.Any(v => double.IsNaN(v) || double.IsInfinity(v))
            )
            {
                throw new ArgumentException(name + " is a point [x, y] of finite numbers in millimetres.");
            }

            return new GeoPoint3(value[0], value[1], value.Length == 3 ? value[2] : 0.0);
        }

        private static List<GeoPoint3> Points(string name, double[][] values)
        {
            if (values == null)
            {
                throw new ArgumentException(name + " is a list of points [[x, y], ...].");
            }

            return values.Select(v => Point(name, v)).ToList();
        }
    }
}
