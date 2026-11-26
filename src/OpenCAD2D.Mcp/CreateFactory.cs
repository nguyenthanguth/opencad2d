using System;
using System.Collections.Generic;
using System.Linq;
using GeometryHelper.Dxf;
using GeometryHelper.Geometry;

namespace OpenCAD2D.Mcp
{
    /// <summary>
    /// Makes the entities of the second phase's drawing tools in code: the shapes the commands RECTANG, POLYGON, ELLIPSE, SPLINE,
    /// POINT, DONUT, XLINE, RAY and LEADER draw, with the same geometry. Angles are in radians here, as in <see cref="EntityFactory"/>.
    /// </summary>
    internal static class CreateFactory
    {
        /// <summary>The bulge of a quarter of a circle, <c>tan(pi / 8)</c>.</summary>
        private static readonly double QuarterBulge = Math.Tan(Math.PI / 8.0);

        private static void Finite(string what, params double[] numbers)
        {
            if (numbers.Any(n => double.IsNaN(n) || double.IsInfinity(n)))
            {
                throw new ArgumentException(what + " are finite numbers.");
            }
        }

        private static void Vertex(DxfPolyline polyline, double x, double y, double bulge, double width = 0.0) =>
            polyline.AddVertexAt(polyline.NumberOfVertices, new GeoPoint2(x, y), bulge, width, width);

        /// <summary>
        /// A closed polyline round a rectangle of two opposite corners, as RECTANG draws one, its first corner the lower left: with a
        /// fillet radius each corner is a quarter arc, with a chamfer distance each corner is cut, the same on both sides.
        /// </summary>
        public static DxfPolyline Rectangle(GeoPoint3 first, GeoPoint3 second, double fillet, double chamfer)
        {
            Finite("The corners", first.X, first.Y, second.X, second.Y);
            double x0 = Math.Min(first.X, second.X);
            double x1 = Math.Max(first.X, second.X);
            double y0 = Math.Min(first.Y, second.Y);
            double y1 = Math.Max(first.Y, second.Y);
            double width = x1 - x0;
            double height = y1 - y0;

            if (!(width > 0.0) || !(height > 0.0))
            {
                throw new ArgumentException("The two corners of a rectangle differ in x and in y.");
            }

            if (!(fillet >= 0.0) || !(chamfer >= 0.0) || (fillet > 0.0 && chamfer > 0.0))
            {
                throw new ArgumentException("Give a fillet radius or a chamfer distance, 0 or more, not both.");
            }

            double cut = Math.Max(fillet, chamfer);

            if (cut * 2.0 >= Math.Min(width, height) && cut > 0.0)
            {
                throw new ArgumentException(
                    "A fillet or chamfer of " + cut + " is too big for a rectangle " + width + " by " + height + "."
                );
            }

            var polyline = new DxfPolyline();

            if (cut == 0.0)
            {
                Vertex(polyline, x0, y0, 0.0);
                Vertex(polyline, x1, y0, 0.0);
                Vertex(polyline, x1, y1, 0.0);
                Vertex(polyline, x0, y1, 0.0);
            }
            else
            {
                double b = fillet > 0.0 ? QuarterBulge : 0.0;
                Vertex(polyline, x0 + cut, y0, 0.0);
                Vertex(polyline, x1 - cut, y0, b);
                Vertex(polyline, x1, y0 + cut, 0.0);
                Vertex(polyline, x1, y1 - cut, b);
                Vertex(polyline, x1 - cut, y1, 0.0);
                Vertex(polyline, x0 + cut, y1, b);
                Vertex(polyline, x0, y1 - cut, 0.0);
                Vertex(polyline, x0, y0 + cut, b);
            }

            polyline.Closed = true;
            polyline.Elevation = first.Z;
            return polyline;
        }

        /// <summary>
        /// A regular polygon as a closed polyline, as POLYGON draws one: inscribed, the radius runs to the vertices and the first
        /// vertex lies at the rotation; circumscribed, the radius runs to the middle of the sides and a side's middle lies at the rotation.
        /// </summary>
        public static DxfPolyline Polygon(GeoPoint3 center, double radius, int sides, bool inscribed, double rotation)
        {
            Finite("The centre and rotation", center.X, center.Y, rotation);

            if (sides < 3 || sides > 1024)
            {
                throw new ArgumentException("A polygon has 3 to 1024 sides.");
            }

            if (!(radius > 0.0) || double.IsInfinity(radius))
            {
                throw new ArgumentException("The radius of a polygon is above nought.");
            }

            double turn = 2.0 * Math.PI / sides;
            double reach = inscribed ? radius : radius / Math.Cos(Math.PI / sides);
            double first = inscribed ? rotation : rotation - Math.PI / sides;
            var polyline = new DxfPolyline();

            for (int i = 0; i < sides; i++)
            {
                double a = first + i * turn;
                Vertex(polyline, center.X + reach * Math.Cos(a), center.Y + reach * Math.Sin(a), 0.0);
            }

            polyline.Closed = true;
            polyline.Elevation = center.Z;
            return polyline;
        }

        /// <summary>
        /// An ellipse about a centre, its major axis to a point, the minor over the major as the ratio; the whole ellipse, or the
        /// arc from the start to the end angle (radians, parameters from the major axis, counter-clockwise).
        /// </summary>
        public static DxfEllipse Ellipse(
            GeoPoint3 center,
            GeoPoint3 majorEnd,
            double ratio,
            double startAngle,
            double endAngle
        )
        {
            Finite("The ellipse", center.X, center.Y, majorEnd.X, majorEnd.Y, startAngle, endAngle);
            var major = new GeoVector3(majorEnd.X - center.X, majorEnd.Y - center.Y, 0.0);

            if (major.Length <= 1e-9)
            {
                throw new ArgumentException("The end of the major axis is not at the centre.");
            }

            if (!(ratio > 0.0 && ratio <= 1.0))
            {
                throw new ArgumentException("The ratio of the minor radius to the major is above 0 and up to 1.");
            }

            return new DxfEllipse(center, new GeoVector3(0.0, 0.0, 1.0), major, ratio, startAngle, endAngle);
        }

        /// <summary>The cubic spline through the points, as SPLINE with fit points makes it.</summary>
        public static DxfSpline FitSpline(IReadOnlyList<GeoPoint3> points, bool closed)
        {
            if (points.Count < (closed ? 3 : 2))
            {
                throw new ArgumentException(
                    closed ? "A closed spline has at least three fit points." : "A spline has at least two fit points."
                );
            }

            var spline = new DxfSpline();
            spline.FitData = new DxfFitData(points, 3, 0.0) { Periodic = closed };
            return spline;
        }

        /// <summary>The spline of control vertices, of the degree (lowered to the points less one), clamped, as SPLINE with control vertices makes it.</summary>
        public static DxfSpline ControlSpline(IReadOnlyList<GeoPoint3> points, int degree, bool closed)
        {
            if (degree < 1 || degree > 10)
            {
                throw new ArgumentException("The degree of a spline is 1 to 10.");
            }

            if (points.Count < (closed ? 3 : 2))
            {
                throw new ArgumentException(
                    closed
                        ? "A closed spline has at least three control points."
                        : "A spline has at least two control points."
                );
            }

            if (closed)
            {
                throw new ArgumentException("A closed spline is made from fit points: give fit_points.");
            }

            int p = Math.Min(degree, points.Count - 1);
            var knots = new List<double>(points.Count + p + 1);

            for (int i = 0; i <= p; i++)
            {
                knots.Add(0.0);
            }

            for (int i = 1; i < points.Count - p; i++)
            {
                knots.Add(i);
            }

            for (int i = 0; i <= p; i++)
            {
                knots.Add(points.Count - p);
            }

            return new DxfSpline(p, false, false, false, points, knots, null, 1E-10, 1E-10);
        }

        /// <summary>A point object.</summary>
        public static DxfDBPoint Point(GeoPoint3 at)
        {
            Finite("The point", at.X, at.Y);
            return new DxfDBPoint(at);
        }

        /// <summary>
        /// A donut as DONUT draws it: a closed polyline of two half-circle arcs with the width between the inner and outer diameter,
        /// a solid disc where the inner diameter is 0.
        /// </summary>
        public static DxfPolyline Donut(GeoPoint3 center, double inner, double outer)
        {
            Finite("The donut", center.X, center.Y, inner, outer);

            if (!(outer > 0.0) || !(inner >= 0.0) || inner >= outer)
            {
                throw new ArgumentException("A donut has an outer diameter above its inner one, which is 0 or more.");
            }

            double mean = (inner + outer) / 4.0;
            double width = (outer - inner) / 2.0;
            var polyline = new DxfPolyline();
            Vertex(polyline, center.X - mean, center.Y, 1.0, width);
            Vertex(polyline, center.X + mean, center.Y, 1.0, width);
            polyline.Closed = true;
            polyline.Elevation = center.Z;
            return polyline;
        }

        /// <summary>A construction line through a point at an angle (radians), unbounded both ways.</summary>
        public static DxfXline Xline(GeoPoint3 through, double angle)
        {
            Finite("The line", through.X, through.Y, angle);
            return new DxfXline
            {
                BasePoint = through,
                UnitDir = new GeoVector3(Math.Cos(angle), Math.Sin(angle), 0.0),
            };
        }

        /// <summary>A ray from a point at an angle (radians), unbounded one way.</summary>
        public static DxfRay Ray(GeoPoint3 from, double angle)
        {
            Finite("The ray", from.X, from.Y, angle);
            return new DxfRay { BasePoint = from, UnitDir = new GeoVector3(Math.Cos(angle), Math.Sin(angle), 0.0) };
        }

        /// <summary>A leader (LEADER) through the points, with an arrowhead at the first and no annotation, in a dimension style.</summary>
        public static DxfLeader Leader(IReadOnlyList<GeoPoint3> points, string style)
        {
            if (points.Count < 2)
            {
                throw new ArgumentException("A leader runs through at least two points.");
            }

            var leader = new DxfLeader();

            foreach (GeoPoint3 point in points)
            {
                Finite("The points", point.X, point.Y);
                leader.AppendVertex(point);
            }

            leader.HasArrowHead = true;

            if (!string.IsNullOrEmpty(style))
            {
                leader.DimensionStyleName = style;
            }

            return leader;
        }
    }
}
