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
    /// <summary>
    /// The tools that change what is drawn the way the modify commands do: copy, mirror, offset, arrays, explode, join, break, trim,
    /// extend, fillet, chamfer, stretch, lengthen, draw order, and the edits of a text, a dimension and the look of objects. Each
    /// types the command on the drawing's own command line with the inputs the tool builds from numbers, handles and names it has
    /// checked (never free text), so what comes of it is what the command makes; each call is one undo step.
    /// </summary>
    internal sealed partial class DrawingTools
    {
        private const string TargetsText =
            "Handles of the objects, as [\"2F\", \"30\"] (query_entities gives them), in the current layout.";

        private static IDrawingEditing Editing(IDrawingBackend drawing) =>
            drawing as IDrawingEditing ?? throw new InvalidOperationException("This drawing does not take this tool.");

        /// <summary>
        /// Runs the script a tool builds on the drawing and says what it did: the handles it made and took away, found by looking at
        /// the current layout before and after, and the given objects whose box moved. A command that did nothing is refused with
        /// what the command line said, unless the tool says it may change no box (draw order).
        /// </summary>
        private ModifiedResult Script(
            IDrawingBackend drawing,
            IReadOnlyList<string> given,
            bool mustChange,
            Func<CommandScript> build
        )
        {
            IDrawingEditing editing = Editing(drawing);
            string space = drawing.CurrentLayout;
            List<string> before = HandlesOf(editing, space);

            foreach (string handle in given)
            {
                if (!before.Contains(handle, StringComparer.OrdinalIgnoreCase))
                {
                    throw new ArgumentException(
                        "The layout "
                            + space
                            + " has no object "
                            + handle
                            + ": a command works in the current layout (set_current_layout changes it)."
                    );
                }
            }

            Dictionary<string, string> shapesBefore = Shapes(drawing, given);
            string output = editing.Run(build());
            List<string> after = HandlesOf(editing, space);
            var was = new HashSet<string>(before, StringComparer.OrdinalIgnoreCase);
            var is_ = new HashSet<string>(after, StringComparer.OrdinalIgnoreCase);
            Dictionary<string, string> shapesAfter = Shapes(drawing, given.Where(h => is_.Contains(h)).ToList());
            var changed = new List<string>();

            foreach (string handle in given)
            {
                if (
                    shapesBefore.TryGetValue(handle, out string a)
                    && shapesAfter.TryGetValue(handle, out string b)
                    && a != b
                )
                {
                    changed.Add(handle);
                }
            }

            var result = new ModifiedResult
            {
                Created = after.Where(h => !was.Contains(h)).ToList(),
                Erased = before.Where(h => !is_.Contains(h)).ToList(),
                Changed = changed,
                Output = Tail(output),
            };

            if (mustChange && result.Created.Count == 0 && result.Erased.Count == 0 && result.Changed.Count == 0)
            {
                throw new InvalidOperationException(
                    "The command changed nothing. The command line said: " + Tail(output)
                );
            }

            return result;
        }

        /// <summary>The handles of every object of a layout, in drawing order, of every kind (a viewport too).</summary>
        private static List<string> HandlesOf(IDrawingEditing editing, string layout) =>
            editing.Make("handles", new CadMakeArgs().Set("layout", layout)).ToList();

        /// <summary>
        /// What each given object is now, as one text of its key geometry and its drawn box, each number to 0.0001 of a millimetre: two
        /// texts differ where the object was changed in place (a polyline given its rounded corners keeps its box, not its vertices).
        /// </summary>
        private static Dictionary<string, string> Shapes(IDrawingBackend drawing, IReadOnlyList<string> handles)
        {
            var shapes = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);

            if (handles.Count == 0)
            {
                return shapes;
            }

            var wanted = new HashSet<string>(handles, StringComparer.OrdinalIgnoreCase);
            BoxRows rows = drawing.BoundsOf(handles);
            var boxes = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);

            for (int i = 0; i < rows.Handles.Length; i++)
            {
                boxes[rows.Handles[i]] = string.Join(",", Corners(rows.Boxes, i));
            }

            // A few objects are read by their handles; many (the whole layout, for TRIM) in one query of the layout.
            IEnumerable<EntityRef> items =
                handles.Count <= 40 ? Editing(drawing).Objects(handles) : drawing.Entities(drawing.CurrentLayout);

            foreach (EntityRef item in items)
            {
                if (wanted.Contains(item.Handle))
                {
                    EntityInfo info = EntityDescriber.Describe(item);
                    string geometry = string.Join(
                        ";",
                        info.Geometry.OrderBy(g => g.Key, StringComparer.Ordinal)
                            .Select(g => g.Key + "=" + Flat(g.Value))
                    );
                    shapes[item.Handle] =
                        info.Layer
                        + "|"
                        + geometry
                        + "|"
                        + (boxes.TryGetValue(item.Handle, out string box) ? box : string.Empty);
                }
            }

            return shapes;
        }

        private static string Flat(object value)
        {
            switch (value)
            {
                case double[] numbers:
                    return "["
                        + string.Join(",", numbers.Select(n => n.ToString("R", CultureInfo.InvariantCulture)))
                        + "]";
                case IEnumerable<double[]> lists:
                    return "[" + string.Join(";", lists.Select(Flat)) + "]";
                case double number:
                    return number.ToString("R", CultureInfo.InvariantCulture);
                default:
                    return Convert.ToString(value, CultureInfo.InvariantCulture);
            }
        }

        /// <summary>The last lines of what the command line wrote, as one line.</summary>
        private static string Tail(string output)
        {
            string[] lines = (output ?? string.Empty)
                .Split('\n')
                .Select(l => l.Trim())
                .Where(l => l.Length > 0)
                .ToArray();
            return string.Join(" | ", lines.Skip(Math.Max(0, lines.Length - 6)));
        }

        private ModifiedResult Modify(
            string tool,
            string[] handles,
            bool mustChange,
            Func<IDrawingBackend, List<string>, CommandScript> build
        ) => _session.Run(tool, true, drawing =>
            {
                List<string> list = Handles(handles);
                return Script(drawing, list, mustChange, () => build(drawing, list));
            });

        [McpServerTool(Name = "copy", ReadOnly = false, Destructive = false, Idempotent = false, OpenWorld = false)]
        [Description(
            "Copies objects, as COPY with displacements typed: one copy for each displacement [dx, dy] given, in millimetres from the objects. Gives the handles of the copies. One undo step."
        )]
        public ModifiedResult Copy(
            [Description(TargetsText)] string[] handles,
            [Description("The displacements as [[dx, dy], ...], at least one; each makes a copy.")]
                double[][] displacements
        ) => Modify("copy", handles, true, (drawing, list) =>
            {
                if (displacements == null || displacements.Length == 0)
                {
                    throw new ArgumentException("Give at least one displacement [dx, dy].");
                }

                CommandScript script = new CommandScript().Word("COPY").Pick(list).Enter().Point(0.0, 0.0);

                foreach (double[] d in displacements)
                {
                    GeoPoint3 p = Point("a displacement", d);
                    script.Point(p.X, p.Y);
                }

                return script.Enter();
            });

        [McpServerTool(Name = "mirror", ReadOnly = false, Destructive = false, Idempotent = false, OpenWorld = false)]
        [Description(
            "Mirrors objects across the line through two points, as MIRROR: the mirrored copies are added and the objects kept, or erased where keep_source is false. A text keeps reading the right way (MIRRTEXT 0). Gives the handles of the new objects. One undo step."
        )]
        public ModifiedResult Mirror(
            [Description(TargetsText)] string[] handles,
            [Description("First point of the mirror line [x, y].")] double[] line_start,
            [Description("Second point of the mirror line [x, y].")] double[] line_end,
            [Description("False to erase the objects after mirroring them.")] bool keep_source = true
        ) => Modify("mirror", handles, true, (drawing, list) =>
            {
                GeoPoint3 a = Point("line_start", line_start);
                GeoPoint3 b = Point("line_end", line_end);

                if ((a.X - b.X) * (a.X - b.X) + (a.Y - b.Y) * (a.Y - b.Y) <= Tol.EqualPoint * Tol.EqualPoint)
                {
                    throw new ArgumentException("The two points of the mirror line are the same point.");
                }

                return new CommandScript()
                    .Word("MIRROR")
                    .Pick(list)
                    .Enter()
                    .Point(a.X, a.Y)
                    .Point(b.X, b.Y)
                    .Word(keep_source ? "N" : "Y");
            });

        [McpServerTool(Name = "offset", ReadOnly = false, Destructive = false, Idempotent = false, OpenWorld = false)]
        [Description(
            "Offsets a line, arc, circle, ellipse, polyline or spline to one side, as OFFSET: by a distance, or through a point. The side is the point given. Gives the handles of the offset curves. One undo step."
        )]
        public ModifiedResult Offset(
            [Description("The handle of the curve.")] string handle,
            [Description(
                "A point on the side to offset to [x, y]; with no distance, the point the offset curve passes through."
            )]
                double[] side,
            [Description("The distance in millimetres, above 0; leave out to offset through the point given as side.")]
                double? distance = null,
            [Description("True to erase the curve after offsetting it.")] bool erase_source = false
        ) => Modify("offset", new[] { handle }, true, (drawing, list) =>
            {
                GeoPoint3 p = Point("side", side);
                CommandScript script = new CommandScript().Word("OFFSET");

                if (erase_source)
                {
                    script.Word("E").Word("Y");
                }

                if (distance.HasValue)
                {
                    if (!(distance.Value > 0.0) || double.IsInfinity(distance.Value))
                    {
                        throw new ArgumentException("The distance is a length above nought.");
                    }

                    script.Number(distance.Value);
                }
                else
                {
                    script.Word("T");
                }

                return script.Pick(list).Point(p.X, p.Y).Enter();
            });

        [McpServerTool(
            Name = "array_rectangular",
            ReadOnly = false,
            Destructive = false,
            Idempotent = false,
            OpenWorld = false
        )]
        [Description(
            "Copies objects into rows and columns, as -ARRAY Rectangular: rows counted up the y direction at the row distance, columns along x at the column distance (a negative distance goes the other way). The objects themselves are the first of the array. Gives the handles of the copies. One undo step."
        )]
        public ModifiedResult ArrayRectangular(
            [Description(TargetsText)] string[] handles,
            [Description("The number of rows, 1 or more.")] int rows,
            [Description("The number of columns, 1 or more.")] int columns,
            [Description("The distance between rows in millimetres.")] double row_distance,
            [Description("The distance between columns in millimetres.")] double column_distance
        ) => Modify("array_rectangular", handles, true, (drawing, list) =>
            {
                if (rows < 1 || columns < 1 || (long)rows * columns > 10000L)
                {
                    throw new ArgumentException("Rows and columns are 1 or more, and no more than 10 000 copies.");
                }

                if (rows * columns < 2)
                {
                    throw new ArgumentException("An array of one row and one column makes no copy.");
                }

                Finite("The distances", row_distance, column_distance);

                // -ARRAY asks a distance only along a direction that has more than one item.
                CommandScript script = new CommandScript()
                    .Word("-ARRAY")
                    .Pick(list)
                    .Enter()
                    .Word("R")
                    .Word(rows.ToString(CultureInfo.InvariantCulture))
                    .Word(columns.ToString(CultureInfo.InvariantCulture));

                if (rows > 1)
                {
                    script.Number(row_distance);
                }

                if (columns > 1)
                {
                    script.Number(column_distance);
                }

                return script;
            });

        [McpServerTool(
            Name = "array_polar",
            ReadOnly = false,
            Destructive = false,
            Idempotent = false,
            OpenWorld = false
        )]
        [Description(
            "Copies objects round a centre, as -ARRAY Polar: count items in all (the objects are the first) spread over the angle, counter-clockwise for a positive angle, each turned with the circle unless rotate_items is false. Gives the handles of the copies. One undo step."
        )]
        public ModifiedResult ArrayPolar(
            [Description(TargetsText)] string[] handles,
            [Description("The centre of the array [x, y].")] double[] center,
            [Description("The number of items, 2 or more, the objects included.")] int count,
            [Description("The angle to fill in degrees, 360 for a full circle; negative goes clockwise.")]
                double angle = 360.0,
            [Description("False to keep each copy turned as the objects are.")] bool rotate_items = true
        ) => Modify("array_polar", handles, true, (drawing, list) =>
            {
                GeoPoint3 c = Point("center", center);

                if (count < 2 || count > 10000)
                {
                    throw new ArgumentException("The number of items is 2 to 10 000.");
                }

                Finite("The angle", angle);

                if (angle == 0.0)
                {
                    throw new ArgumentException("The angle to fill is not 0.");
                }

                return new CommandScript()
                    .Word("-ARRAY")
                    .Pick(list)
                    .Enter()
                    .Word("P")
                    .Point(c.X, c.Y)
                    .Word(count.ToString(CultureInfo.InvariantCulture))
                    .Number(angle)
                    .Word(rotate_items ? "Y" : "N");
            });

        [McpServerTool(Name = "explode", ReadOnly = false, Destructive = true, Idempotent = false, OpenWorld = false)]
        [Description(
            "Breaks objects into the parts they are made of, as EXPLODE: a polyline into lines and arcs, a block reference into its objects, a dimension into lines and text. The objects are replaced; undo brings them back. Gives the handles of the parts. One undo step."
        )]
        public ModifiedResult Explode([Description(TargetsText)] string[] handles) =>
            Modify("explode", handles, true, (drawing, list) => new CommandScript().Word("EXPLODE").Pick(list).Enter());

        [McpServerTool(Name = "join", ReadOnly = false, Destructive = true, Idempotent = false, OpenWorld = false)]
        [Description(
            "Joins lines, arcs, polylines and splines that meet end to end into one object, as JOIN. The objects are replaced by the joined one. Gives the handles. One undo step."
        )]
        public ModifiedResult Join([Description(TargetsText)] string[] handles) =>
            Modify("join", handles, true, (drawing, list) =>
            {
                if (list.Count < 2)
                {
                    throw new ArgumentException("Join needs at least two objects.");
                }

                return new CommandScript().Word("JOIN").Pick(list).Enter();
            });

        [McpServerTool(Name = "break_at", ReadOnly = false, Destructive = true, Idempotent = false, OpenWorld = false)]
        [Description(
            "Breaks an object at a point into two (as BREAK at one point), or with a second point cuts out the part between the two points, as BREAK. The points are on the object. Gives the handle of the part made. One undo step."
        )]
        public ModifiedResult BreakAt(
            [Description("The handle of the line, arc, circle or polyline.")] string handle,
            [Description("The first break point [x, y], on the object.")] double[] point,
            [Description("The second break point [x, y], on the object; leave out to break at the first point only.")]
                double[] second_point = null
        ) => Modify("break_at", new[] { handle }, true, (drawing, list) =>
            {
                GeoPoint3 a = Point("point", point);
                CommandScript script = new CommandScript().Word("BREAK").Pick(list).Word("F").Point(a.X, a.Y);

                if (second_point == null)
                {
                    return script.Word("@");
                }

                GeoPoint3 b = Point("second_point", second_point);
                return script.Point(b.X, b.Y);
            });

        [McpServerTool(Name = "trim", ReadOnly = false, Destructive = true, Idempotent = false, OpenWorld = false)]
        [Description(
            "Trims objects at the cutting edges, as TRIM: each pick point is on the object, on the part to cut away. With no edges every object of the layout is an edge. Gives what changed. One undo step."
        )]
        public ModifiedResult Trim(
            [Description("The pick points as [[x, y], ...], each on the part of an object to trim away.")]
                double[][] pick_points,
            [Description("Handles of the cutting edges; every object where left out.")] string[] edges = null
        ) => _session.Run("trim", true, drawing =>
            {
                List<GeoPoint3> picks = Picks(pick_points);
                List<string> given = edges == null || edges.Length == 0 ? new List<string>() : Handles(edges);
                return Script(drawing, Everything(drawing), true, () =>
                {
                    CommandScript script = new CommandScript().Word("TRIM");

                    if (given.Count > 0)
                    {
                        script.Word("T").Pick(given).Enter();
                    }

                    foreach (GeoPoint3 p in picks)
                    {
                        script.Point(p.X, p.Y);
                    }

                    return script.Enter();
                });
            });

        [McpServerTool(Name = "extend", ReadOnly = false, Destructive = false, Idempotent = false, OpenWorld = false)]
        [Description(
            "Extends objects to the boundary edges, as EXTEND: each pick point is on the object, near the end to extend. With no boundaries every object of the layout is a boundary. Gives what changed. One undo step."
        )]
        public ModifiedResult Extend(
            [Description("The pick points as [[x, y], ...], each on an object near the end to extend.")]
                double[][] pick_points,
            [Description("Handles of the boundary edges; every object where left out.")] string[] boundaries = null
        ) => _session.Run("extend", true, drawing =>
            {
                List<GeoPoint3> picks = Picks(pick_points);
                List<string> given =
                    boundaries == null || boundaries.Length == 0 ? new List<string>() : Handles(boundaries);
                return Script(drawing, Everything(drawing), true, () =>
                {
                    CommandScript script = new CommandScript().Word("EXTEND");

                    if (given.Count > 0)
                    {
                        script.Word("B").Pick(given).Enter();
                    }

                    foreach (GeoPoint3 p in picks)
                    {
                        script.Point(p.X, p.Y);
                    }

                    return script.Enter();
                });
            });

        /// <summary>The handles of every object of the current layout: the objects whose box a command may change that the tool cannot name.</summary>
        private static List<string> Everything(IDrawingBackend drawing) =>
            HandlesOf(Editing(drawing), drawing.CurrentLayout);

        private static List<GeoPoint3> Picks(double[][] points)
        {
            if (points == null || points.Length == 0)
            {
                throw new ArgumentException("Give at least one pick point [x, y].");
            }

            return points.Select(p => Point("a pick point", p)).ToList();
        }

        [McpServerTool(Name = "fillet", ReadOnly = false, Destructive = false, Idempotent = false, OpenWorld = false)]
        [Description(
            "Rounds the corner of two objects with an arc of the radius, as FILLET (trimming both), or every corner of one polyline when only first is given. A radius of 0 makes a sharp corner. Gives the handle of the arc made. One undo step."
        )]
        public ModifiedResult Fillet(
            [Description("The first line, arc, circle or polyline.")] string first,
            [Description("The radius in millimetres, 0 or more.")] double radius,
            [Description("The second object; leave out to round every corner of the polyline first.")]
                string second = null,
            [Description("A point [x, y] on the first object, to choose the side where two objects cross.")]
                double[] first_point = null,
            [Description("A point [x, y] on the second object.")] double[] second_point = null
        ) => Modify("fillet", second == null ? new[] { first } : new[] { first, second }, true, (drawing, list) =>
            {
                if (!(radius >= 0.0) || double.IsInfinity(radius))
                {
                    throw new ArgumentException("The radius is 0 or more.");
                }

                CommandScript script = new CommandScript().Word("FILLET").Word("R").Number(radius);

                if (second == null)
                {
                    return script.Word("P").Pick(new[] { list[0] });
                }

                Corner(drawing, script, list[0], first_point);
                Corner(drawing, script, list[1], second_point);
                return script;
            });

        /// <summary>
        /// Answers the prompt for a corner's object with a point on it: the one given, else the middle of the object, so that the
        /// side of a corner the command keeps does not depend on where the object happens to start.
        /// </summary>
        private static CommandScript Corner(
            IDrawingBackend drawing,
            CommandScript script,
            string handle,
            double[] point
        )
        {
            if (point != null)
            {
                GeoPoint3 p = Point("a point of an object", point);
                return script.Point(p.X, p.Y);
            }

            DxfEntity entity = Editing(drawing).Objects(new[] { handle }).First().Entity;

            if (entity is DxfCurve curve && !(curve is DxfXline) && !(curve is DxfRay))
            {
                GeoPoint3 middle = curve.GetPointAtParameter((curve.StartParam + curve.EndParam) / 2.0);
                return script.Point(middle.X, middle.Y);
            }

            return script.Pick(new[] { handle });
        }

        [McpServerTool(Name = "chamfer", ReadOnly = false, Destructive = false, Idempotent = false, OpenWorld = false)]
        [Description(
            "Cuts the corner of two lines with a straight edge, as CHAMFER with two distances (trimming both): distance1 along the first line, distance2 along the second. Gives the handle of the chamfer made. One undo step."
        )]
        public ModifiedResult Chamfer(
            [Description("The first line.")] string first,
            [Description("The second line.")] string second,
            [Description("The distance cut from the first line, in millimetres, 0 or more.")] double distance1,
            [Description("The distance cut from the second line; the same as distance1 where left out.")]
                double? distance2 = null,
            [Description("A point [x, y] on the first line, to choose the side where the lines cross.")]
                double[] first_point = null,
            [Description("A point [x, y] on the second line.")] double[] second_point = null
        ) => Modify("chamfer", new[] { first, second }, true, (drawing, list) =>
            {
                double d2 = distance2 ?? distance1;

                if (!(distance1 >= 0.0) || !(d2 >= 0.0) || double.IsInfinity(distance1) || double.IsInfinity(d2))
                {
                    throw new ArgumentException("The distances are 0 or more.");
                }

                CommandScript script = new CommandScript().Word("CHAMFER").Word("D").Number(distance1).Number(d2);
                Corner(drawing, script, list[0], first_point);
                Corner(drawing, script, list[1], second_point);
                return script;
            });

        [McpServerTool(Name = "stretch", ReadOnly = false, Destructive = false, Idempotent = false, OpenWorld = false)]
        [Description(
            "Stretches what a crossing window takes, as STRETCH: objects wholly inside the window move, those the window crosses have their ends inside it moved, by the displacement. Gives what changed. One undo step."
        )]
        public ModifiedResult Stretch(
            [Description("The crossing window [x1, y1, x2, y2] in millimetres.")] double[] window,
            [Description("The displacement [dx, dy] in millimetres.")] double[] displacement
        ) => _session.Run("stretch", true, drawing =>
            {
                DisplayBox box = Box("window", window)
                    ?? throw new ArgumentException("window is [x1, y1, x2, y2], four numbers.");
                GeoPoint3 d = Point("displacement", displacement);

                // The objects the window takes are the ones to watch for a change of their box.
                BoxRows near = drawing.Bounds(drawing.CurrentLayout);
                var watch = new List<string>();

                for (int i = 0; i < near.Handles.Length; i++)
                {
                    if (box.Inflate(Tol.EqualPoint).Intersects(Row(near.Boxes, i)))
                    {
                        watch.Add(near.Handles[i]);
                    }
                }

                return Script(drawing, watch, true, () =>
                    new CommandScript()
                        .Word("STRETCH")
                        .Word("C")
                        .Point(box.MinX, box.MinY)
                        .Point(box.MaxX, box.MaxY)
                        .Enter()
                        .Point(0.0, 0.0)
                        .Point(d.X, d.Y)
                );
            });

        [McpServerTool(Name = "lengthen", ReadOnly = false, Destructive = false, Idempotent = false, OpenWorld = false)]
        [Description(
            "Lengthens or shortens a line, arc or open polyline, as LENGTHEN: by a delta (positive grows, negative shrinks), to a total length, or to a percentage of its length. The end changed is the one nearest the end_point given. One undo step."
        )]
        public ModifiedResult Lengthen(
            [Description("The handle of the line, arc or open polyline.")] string handle,
            [Description("How: delta, total or percent.")] string mode,
            [Description("The delta or total length in millimetres, or the percentage (100 leaves the length).")]
                double value,
            [Description("A point [x, y] on the object near the end to change.")] double[] end_point
        ) => Modify("lengthen", new[] { handle }, true, (drawing, list) =>
            {
                string word;

                switch ((mode ?? string.Empty).Trim().ToLowerInvariant())
                {
                    case "delta":
                        word = "DE";
                        break;
                    case "total":
                        word = "T";
                        break;
                    case "percent":
                        word = "P";
                        break;
                    default:
                        throw new ArgumentException("The mode is delta, total or percent.");
                }

                Finite("The value", value);
                GeoPoint3 p = Point("end_point", end_point);
                return new CommandScript().Word("LENGTHEN").Word(word).Number(value).Point(p.X, p.Y).Enter();
            });

        [McpServerTool(
            Name = "set_draw_order",
            ReadOnly = false,
            Destructive = false,
            Idempotent = false,
            OpenWorld = false
        )]
        [Description(
            "Changes which object is drawn over which, as DRAWORDER: to the front or the back of the layout, or just above or under a reference object. One undo step."
        )]
        public ModifiedResult SetDrawOrder(
            [Description(TargetsText)] string[] handles,
            [Description("front, back, above or under.")] string where,
            [Description("The handle of the reference object, for above and under.")] string reference = null
        ) => Modify("set_draw_order", handles, false, (drawing, list) =>
            {
                string word = (where ?? string.Empty).Trim().ToLowerInvariant();
                CommandScript script = new CommandScript().Word("DRAWORDER").Pick(list).Enter();

                switch (word)
                {
                    case "front":
                        return script.Word("F");
                    case "back":
                        return script.Word("B");
                    case "above":
                    case "under":
                        if (string.IsNullOrWhiteSpace(reference) || list.Contains(reference.Trim()))
                        {
                            throw new ArgumentException(
                                "above and under need the handle of a reference object not among handles."
                            );
                        }

                        return script.Word(word == "above" ? "A" : "U").Pick(new[] { reference.Trim() }).Enter();
                    default:
                        throw new ArgumentException("where is front, back, above or under.");
                }
            });

        [McpServerTool(Name = "redo", ReadOnly = false, Destructive = false, Idempotent = false, OpenWorld = false)]
        [Description(
            "Takes forward again the step the last undo took back. In a server attached to an app this types REDO into the app."
        )]
        public RedoResult Redo() => _session.Run("redo", true, drawing =>
            {
                bool redone = Editing(drawing).Redo();
                return new RedoResult
                {
                    Redone = redone,
                    Message = redone ? "The step was done again." : "There was nothing to do again.",
                };
            });

        private static void Finite(string what, params double[] numbers)
        {
            if (numbers.Any(n => double.IsNaN(n) || double.IsInfinity(n)))
            {
                throw new ArgumentException(what + " are finite numbers.");
            }
        }
    }
}
