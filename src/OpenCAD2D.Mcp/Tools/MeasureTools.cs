using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Linq;
using GeometryHelper.Dxf;
using GeometryHelper.Geometry;
using ModelContextProtocol.Server;

namespace OpenCAD2D.Mcp
{
    internal sealed partial class DrawingTools
    {
        /// <summary>The most handles one <c>list_properties</c> call lists: LIST writes a dozen lines for each.</summary>
        private const int MostListed = 50;

        /// <summary>The most entities <c>id_point</c> gives for a point.</summary>
        private const int MostAtPoint = 20;

        [McpServerTool(
            Name = "measure_distance",
            ReadOnly = true,
            Destructive = false,
            Idempotent = true,
            OpenWorld = false
        )]
        [Description(
            "The distance between two points, as DIST gives it: the distance, the angle in the XY plane in degrees from 0 to 360 (counter-clockwise from +x), and the changes in x and y. (0, 0) to (30, 40) is 50 at 53.1301 degrees."
        )]
        public DistanceResult MeasureDistance(
            [Description("The first point, [x, y] in millimetres.")] double[] a,
            [Description("The second point, [x, y].")] double[] b
        ) =>
            _session.RunWithout(() =>
            {
                GeoPoint3 from = Point("a", a);
                GeoPoint3 to = Point("b", b);
                double dx = to.X - from.X;
                double dy = to.Y - from.Y;
                double angle = Math.Atan2(dy, dx) * 180.0 / Math.PI;
                return new DistanceResult
                {
                    Distance = EntityDescriber.R(Math.Sqrt(dx * dx + dy * dy)),
                    Angle = EntityDescriber.R(angle < 0.0 ? angle + 360.0 : angle),
                    DeltaX = EntityDescriber.R(dx),
                    DeltaY = EntityDescriber.R(dy),
                };
            });

        [McpServerTool(
            Name = "measure_area",
            ReadOnly = true,
            Destructive = false,
            Idempotent = true,
            OpenWorld = false
        )]
        [Description(
            "The area and perimeter of a shape, as AREA gives them: of points [[x, y], ...] closed back to the first (three or more, straight sides), or of the entity of a handle (a circle, a closed polyline, a closed ellipse or spline, a hatch), in square millimetres and millimetres. Give points or a handle, not both. A hatch has an area and no perimeter."
        )]
        public AreaResult MeasureArea(
            [Description("The corners, [[x, y], ...], three or more.")] double[][] points = null,
            [Description("The handle of a closed shape.")] string handle = null
        ) => _session.Run("measure_area", false, drawing =>
            {
                bool hasPoints = points != null && points.Length > 0;
                bool hasHandle = !string.IsNullOrWhiteSpace(handle);

                if (hasPoints == hasHandle)
                {
                    throw new ArgumentException("Give points or a handle, one of them.");
                }

                if (hasPoints)
                {
                    List<GeoPoint3> corners = Points("points", points);

                    if (corners.Count < 3)
                    {
                        throw new ArgumentException("An area needs three points or more.");
                    }

                    double twice = 0.0;
                    double perimeter = 0.0;

                    for (int i = 0; i < corners.Count; i++)
                    {
                        GeoPoint3 p = corners[i];
                        GeoPoint3 q = corners[(i + 1) % corners.Count];
                        twice += p.X * q.Y - q.X * p.Y;
                        perimeter += Math.Sqrt((q.X - p.X) * (q.X - p.X) + (q.Y - p.Y) * (q.Y - p.Y));
                    }

                    return new AreaResult
                    {
                        Area = EntityDescriber.R(Math.Abs(twice) / 2.0),
                        Perimeter = EntityDescriber.R(perimeter),
                        Of = "points",
                    };
                }

                EntityRef item = Found(drawing, handle);
                double? area = EntityMeasure.Area(item.Entity);

                if (!area.HasValue)
                {
                    throw new ArgumentException(
                        "The object "
                            + item.Handle
                            + " is a "
                            + item.Entity.DxfName
                            + (
                                item.Entity is DxfCurve
                                    ? ", which is open: it has no area. A closed polyline, a circle, an ellipse or a hatch has."
                                    : ": it has no area."
                            )
                    );
                }

                return new AreaResult
                {
                    Area = EntityDescriber.R(area.Value),
                    Perimeter = EntityMeasure.Length(item.Entity) is double length ? EntityDescriber.R(length) : 0.0,
                    Of = item.Entity.DxfName,
                };
            });

        [McpServerTool(
            Name = "measure_length",
            ReadOnly = true,
            Destructive = false,
            Idempotent = true,
            OpenWorld = false
        )]
        [Description(
            "The length of a curve by handle, in millimetres: a line, the arc length of an arc, the circumference of a circle, a polyline along its sides and bulges (with the closing side where it is closed), an ellipse or a spline along its curve."
        )]
        public LengthResult MeasureLength([Description("The handle of the curve.")] string handle) =>
            _session.Run("measure_length", false, drawing =>
            {
                EntityRef item = Found(drawing, handle);
                double? length = EntityMeasure.Length(item.Entity);

                if (!length.HasValue)
                {
                    throw new ArgumentException(
                        "The object " + item.Handle + " is a " + item.Entity.DxfName + ": it has no length."
                    );
                }

                return new LengthResult
                {
                    Handle = item.Handle,
                    Kind = item.Entity.DxfName,
                    Length = EntityDescriber.R(length.Value),
                };
            });

        [McpServerTool(
            Name = "list_properties",
            ReadOnly = true,
            Destructive = false,
            Idempotent = true,
            OpenWorld = false
        )]
        [Description(
            "What AutoCAD's LIST command writes for entities, line by line: the kind and layer, the space, the handle and the shape of each (a line's endpoints, length and angle; a circle's centre, radius, circumference and area; a polyline's corners). At most 50 handles at a time. Use get_entity for one entity as structured values."
        )]
        public ListResult ListProperties([Description("The handles of the entities.")] string[] handles) =>
            _session.Run("list_properties", false, drawing =>
            {
                List<string> list = Handles(handles);

                if (list.Count > MostListed)
                {
                    throw new ArgumentException("List at most " + MostListed + " entities at a time.");
                }

                foreach (string handle in list)
                {
                    Found(drawing, handle);
                }

                return new ListResult { Lines = Q(drawing).ListLines(list) };
            });

        [McpServerTool(Name = "id_point", ReadOnly = true, Destructive = false, Idempotent = true, OpenWorld = false)]
        [Description(
            "A point as ID gives it, and what is drawn there: the entities that pass within the aperture of the point, the one on top first, each as query_entities describes it. Use it to find what is at a place before changing it."
        )]
        public IdPointResult IdPoint(
            [Description("The point, [x, y] in millimetres.")] double[] point,
            [Description("How far from the point an entity may pass, in millimetres; 1 where left out.")]
                double aperture = 1.0,
            [Description("The layout; model space where left out.")] string layout = null
        ) => _session.Run("id_point", false, drawing =>
            {
                GeoPoint3 at = Point("point", point);

                if (!(aperture > 0.0) || double.IsInfinity(aperture))
                {
                    throw new ArgumentException("The aperture is a length above nought.");
                }

                IReadOnlyList<string> handles = Q(drawing)
                    .HandlesIn(
                        Layout(drawing, layout),
                        CadAreaSelection.Point,
                        new List<GeoPoint2> { new GeoPoint2(at.X, at.Y) },
                        aperture
                    );
                List<EntityInfo> found = Q(drawing)
                    .FindAll(handles.Take(MostAtPoint).ToList())
                    .Select(EntityDescriber.Describe)
                    .ToList();
                Dictionary<string, double[]> boxes = DrawnBoxes(drawing, found.Select(i => i.Handle));

                foreach (EntityInfo info in found)
                {
                    info.Box = boxes.TryGetValue(info.Handle, out double[] corners) ? corners : null;
                }

                return new IdPointResult
                {
                    X = EntityDescriber.R(at.X),
                    Y = EntityDescriber.R(at.Y),
                    Aperture = aperture,
                    Entities = found,
                };
            });
    }
}
