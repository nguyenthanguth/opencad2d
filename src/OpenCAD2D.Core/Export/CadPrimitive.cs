using System;
using System.Collections.Generic;
using GeometryHelper.Dxf;
using GeometryHelper.Geometry;

namespace OpenCAD2D
{
    /// <summary>
    /// A straight side or a circular arc of a curve, in the plane, with its box: what the graph measures the distance between
    /// two curves from, with no allocation. A line, a polyline (its bulges arcs), a circle and an arc are made of them; a spline
    /// or an ellipse is not, and is measured by the library.
    /// </summary>
    internal struct CadPrimitive
    {
        public bool IsArc;

        /// <summary>The ends of a segment, or of an arc.</summary>
        public double X0,
            Y0,
            X1,
            Y1;

        /// <summary>The centre and radius of an arc, the angle it starts at (counter-clockwise, radians, from nought to a turn) and how far it sweeps (up to a turn).</summary>
        public double Cx,
            Cy,
            R,
            Start,
            Sweep;

        public double MinX,
            MinY,
            MaxX,
            MaxY;

        private const double Turn = 2.0 * Math.PI;

        public static CadPrimitive Segment(double x0, double y0, double x1, double y1) =>
            new CadPrimitive
            {
                X0 = x0,
                Y0 = y0,
                X1 = x1,
                Y1 = y1,
                MinX = Math.Min(x0, x1),
                MinY = Math.Min(y0, y1),
                MaxX = Math.Max(x0, x1),
                MaxY = Math.Max(y0, y1),
            };

        /// <summary>An arc about a centre from an angle, counter-clockwise, through a sweep of up to a whole turn.</summary>
        public static CadPrimitive Arc(double cx, double cy, double r, double start, double sweep)
        {
            start = Wrap(start);
            double end = start + sweep;
            var arc = new CadPrimitive
            {
                IsArc = true,
                Cx = cx,
                Cy = cy,
                R = r,
                Start = start,
                Sweep = Math.Min(sweep, Turn),
                X0 = cx + r * Math.Cos(start),
                Y0 = cy + r * Math.Sin(start),
                X1 = cx + r * Math.Cos(end),
                Y1 = cy + r * Math.Sin(end),
            };
            arc.MinX = Math.Min(arc.X0, arc.X1);
            arc.MaxX = Math.Max(arc.X0, arc.X1);
            arc.MinY = Math.Min(arc.Y0, arc.Y1);
            arc.MaxY = Math.Max(arc.Y0, arc.Y1);

            // The box takes in each quarter turn the arc passes.
            if (arc.Includes(0.0))
            {
                arc.MaxX = cx + r;
            }

            if (arc.Includes(Math.PI / 2.0))
            {
                arc.MaxY = cy + r;
            }

            if (arc.Includes(Math.PI))
            {
                arc.MinX = cx - r;
            }

            if (arc.Includes(3.0 * Math.PI / 2.0))
            {
                arc.MinY = cy - r;
            }

            return arc;
        }

        private static double Wrap(double angle)
        {
            double wrapped = angle % Turn;
            return wrapped < 0.0 ? wrapped + Turn : wrapped;
        }

        /// <summary>Whether a direction (radians) lies within the sweep of the arc.</summary>
        public bool Includes(double angle) => Wrap(angle - Start) <= Sweep + 1E-12;

        /// <summary>The arc a polyline side of a bulge draws, from one corner to the next.</summary>
        public static CadPrimitive Bulged(double x0, double y0, double x1, double y1, double bulge)
        {
            double dx = x1 - x0;
            double dy = y1 - y0;
            double chord = Math.Sqrt(dx * dx + dy * dy);
            double angle = 4.0 * Math.Atan(bulge);
            double away = chord / 2.0 / Math.Tan(angle / 2.0);
            double cx = (x0 + x1) / 2.0 - dy / chord * away;
            double cy = (y0 + y1) / 2.0 + dx / chord * away;
            double r = Math.Sqrt((x0 - cx) * (x0 - cx) + (y0 - cy) * (y0 - cy));

            // A side that runs clockwise is the counter-clockwise arc from its far end.
            return angle >= 0.0
                ? Arc(cx, cy, r, Math.Atan2(y0 - cy, x0 - cx), angle)
                : Arc(cx, cy, r, Math.Atan2(y1 - cy, x1 - cx), -angle);
        }

        /// <summary>
        /// The sides and arcs a curve is made of, or null for one that is not made of them (a spline, an ellipse, a polyline fitted
        /// with curves) or has no shape to measure.
        /// </summary>
        public static List<CadPrimitive> Of(DxfCurve curve)
        {
            var found = new List<CadPrimitive>();

            switch (curve)
            {
                case DxfLine line:
                    found.Add(Segment(line.StartPoint.X, line.StartPoint.Y, line.EndPoint.X, line.EndPoint.Y));
                    return found;
                case DxfCircle circle:
                    found.Add(Arc(circle.Center.X, circle.Center.Y, circle.Radius, 0.0, Turn));
                    return found;
                case DxfArc arc:
                {
                    // Seen from above, an arc of a normal turned down runs the other way, from pi minus its end angle.
                    double start = arc.Normal.Z < 0.0 ? Math.PI - arc.EndAngle : arc.StartAngle;
                    found.Add(Arc(arc.Center.X, arc.Center.Y, arc.Radius, start, arc.TotalAngle));
                    return found;
                }

                case DxfPolyline polyline:
                {
                    bool flip = polyline.Normal.Z < 0.0;
                    int count = polyline.NumberOfVertices;
                    int sides = polyline.Closed ? count : count - 1;

                    for (int i = 0; i < sides; i++)
                    {
                        GeoPoint3 from = polyline.GetPoint3dAt(i);
                        GeoPoint3 to = polyline.GetPoint3dAt((i + 1) % count);
                        double bulge = flip ? -polyline.GetBulgeAt(i) : polyline.GetBulgeAt(i);
                        found.Add(Side(from.X, from.Y, to.X, to.Y, bulge));
                    }

                    return found.Count > 0 ? found : null;
                }

                case DxfPolyline2d poly2d:
                {
                    if (poly2d.PolyType != DxfPoly2dType.SimplePoly)
                    {
                        return null;
                    }

                    var vertices = new List<DxfVertex2d>();

                    foreach (DxfVertex2d vertex in poly2d)
                    {
                        vertices.Add(vertex);
                    }

                    int sides = poly2d.Closed ? vertices.Count : vertices.Count - 1;

                    for (int i = 0; i < sides; i++)
                    {
                        GeoPoint3 from = vertices[i].Position;
                        GeoPoint3 to = vertices[(i + 1) % vertices.Count].Position;
                        found.Add(Side(from.X, from.Y, to.X, to.Y, vertices[i].Bulge));
                    }

                    return found.Count > 0 ? found : null;
                }

                case DxfPolyline3d poly3d:
                {
                    if (poly3d.PolyType != DxfPoly3dType.SimplePoly)
                    {
                        return null;
                    }

                    var points = new List<GeoPoint3>();

                    foreach (DxfPolylineVertex3d vertex in poly3d)
                    {
                        points.Add(vertex.Position);
                    }

                    int sides = poly3d.Closed ? points.Count : points.Count - 1;

                    for (int i = 0; i < sides; i++)
                    {
                        GeoPoint3 to = points[(i + 1) % points.Count];
                        found.Add(Segment(points[i].X, points[i].Y, to.X, to.Y));
                    }

                    return found.Count > 0 ? found : null;
                }

                default:
                    return null;
            }
        }

        /// <summary>A polyline side: a straight one where its bulge is nought or its ends are one point, else the arc.</summary>
        private static CadPrimitive Side(double x0, double y0, double x1, double y1, double bulge) =>
            Math.Abs(bulge) <= 1E-12 || (Math.Abs(x1 - x0) <= 0.0 && Math.Abs(y1 - y0) <= 0.0)
                ? Segment(x0, y0, x1, y1)
                : Bulged(x0, y0, x1, y1, bulge);

        // ---- the distance between two of them ------------------------------------------------------------------

        /// <summary>The distance from a point to a segment, and the nearest place on it.</summary>
        private static double ToSegment(double x, double y, in CadPrimitive s, out double nx, out double ny)
        {
            double dx = s.X1 - s.X0;
            double dy = s.Y1 - s.Y0;
            double length2 = dx * dx + dy * dy;
            double t =
                length2 <= 0.0 ? 0.0 : Math.Max(0.0, Math.Min(1.0, ((x - s.X0) * dx + (y - s.Y0) * dy) / length2));
            nx = s.X0 + t * dx;
            ny = s.Y0 + t * dy;
            return Math.Sqrt((nx - x) * (nx - x) + (ny - y) * (ny - y));
        }

        /// <summary>The distance from a point to an arc, and the nearest place on it.</summary>
        private static double ToArc(double x, double y, in CadPrimitive a, out double nx, out double ny)
        {
            double dx = x - a.Cx;
            double dy = y - a.Cy;
            double distance = Math.Sqrt(dx * dx + dy * dy);

            if (distance > 0.0 && a.Includes(Math.Atan2(dy, dx)))
            {
                nx = a.Cx + dx / distance * a.R;
                ny = a.Cy + dy / distance * a.R;
                return Math.Abs(distance - a.R);
            }

            double d0 = Math.Sqrt((x - a.X0) * (x - a.X0) + (y - a.Y0) * (y - a.Y0));
            double d1 = Math.Sqrt((x - a.X1) * (x - a.X1) + (y - a.Y1) * (y - a.Y1));

            if (d0 <= d1)
            {
                nx = a.X0;
                ny = a.Y0;
                return d0;
            }

            nx = a.X1;
            ny = a.Y1;
            return d1;
        }

        private static double ToPrimitive(double x, double y, in CadPrimitive p, out double nx, out double ny) =>
            p.IsArc ? ToArc(x, y, p, out nx, out ny) : ToSegment(x, y, p, out nx, out ny);

        private static double Cross(double ax, double ay, double bx, double by) => ax * by - ay * bx;

        /// <summary>
        /// The distance between two primitives, nought where they meet, and a place where the nearest parts lie (the middle of the
        /// two nearest points).
        /// </summary>
        public static double Distance(in CadPrimitive a, in CadPrimitive b, out double x, out double y)
        {
            if (!a.IsArc && !b.IsArc)
            {
                return SegmentSegment(a, b, out x, out y);
            }

            if (a.IsArc != b.IsArc)
            {
                return a.IsArc ? SegmentArc(b, a, out x, out y) : SegmentArc(a, b, out x, out y);
            }

            return ArcArc(a, b, out x, out y);
        }

        private static double SegmentSegment(in CadPrimitive a, in CadPrimitive b, out double x, out double y)
        {
            double rx = a.X1 - a.X0;
            double ry = a.Y1 - a.Y0;
            double sx = b.X1 - b.X0;
            double sy = b.Y1 - b.Y0;
            double denominator = Cross(rx, ry, sx, sy);

            if (Math.Abs(denominator) > 0.0)
            {
                double t = Cross(b.X0 - a.X0, b.Y0 - a.Y0, sx, sy) / denominator;
                double u = Cross(b.X0 - a.X0, b.Y0 - a.Y0, rx, ry) / denominator;

                if (t >= 0.0 && t <= 1.0 && u >= 0.0 && u <= 1.0)
                {
                    x = a.X0 + t * rx;
                    y = a.Y0 + t * ry;
                    return 0.0;
                }
            }

            // No crossing: the nearest pair has an end of one in it.
            double best = double.MaxValue;
            x = y = 0.0;
            double mx,
                my,
                d;

            d = ToSegment(a.X0, a.Y0, b, out mx, out my);
            Keep(d, (a.X0 + mx) / 2.0, (a.Y0 + my) / 2.0, ref best, ref x, ref y);
            d = ToSegment(a.X1, a.Y1, b, out mx, out my);
            Keep(d, (a.X1 + mx) / 2.0, (a.Y1 + my) / 2.0, ref best, ref x, ref y);
            d = ToSegment(b.X0, b.Y0, a, out mx, out my);
            Keep(d, (b.X0 + mx) / 2.0, (b.Y0 + my) / 2.0, ref best, ref x, ref y);
            d = ToSegment(b.X1, b.Y1, a, out mx, out my);
            Keep(d, (b.X1 + mx) / 2.0, (b.Y1 + my) / 2.0, ref best, ref x, ref y);
            return best;
        }

        private static void Keep(double distance, double x, double y, ref double best, ref double bx, ref double by)
        {
            if (distance < best)
            {
                best = distance;
                bx = x;
                by = y;
            }
        }

        private static double SegmentArc(in CadPrimitive s, in CadPrimitive a, out double x, out double y)
        {
            double best = double.MaxValue;
            x = y = 0.0;
            double nx,
                ny,
                d;

            // Where the line meets the circle, inside the segment and the sweep: the two meet.
            double dx = s.X1 - s.X0;
            double dy = s.Y1 - s.Y0;
            double fx = s.X0 - a.Cx;
            double fy = s.Y0 - a.Cy;
            double qa = dx * dx + dy * dy;
            double qb = 2.0 * (fx * dx + fy * dy);
            double qc = fx * fx + fy * fy - a.R * a.R;
            double discriminant = qb * qb - 4.0 * qa * qc;

            if (qa > 0.0 && discriminant >= 0.0)
            {
                double root = Math.Sqrt(discriminant);

                for (int k = 0; k < 2; k++)
                {
                    double t = (k == 0 ? -qb - root : -qb + root) / (2.0 * qa);

                    if (t >= 0.0 && t <= 1.0)
                    {
                        double px = s.X0 + t * dx;
                        double py = s.Y0 + t * dy;

                        if (a.Includes(Math.Atan2(py - a.Cy, px - a.Cx)))
                        {
                            x = px;
                            y = py;
                            return 0.0;
                        }
                    }
                }
            }

            d = ToArc(s.X0, s.Y0, a, out nx, out ny);
            Keep(d, (s.X0 + nx) / 2.0, (s.Y0 + ny) / 2.0, ref best, ref x, ref y);
            d = ToArc(s.X1, s.Y1, a, out nx, out ny);
            Keep(d, (s.X1 + nx) / 2.0, (s.Y1 + ny) / 2.0, ref best, ref x, ref y);
            d = ToSegment(a.X0, a.Y0, s, out nx, out ny);
            Keep(d, (a.X0 + nx) / 2.0, (a.Y0 + ny) / 2.0, ref best, ref x, ref y);
            d = ToSegment(a.X1, a.Y1, s, out nx, out ny);
            Keep(d, (a.X1 + nx) / 2.0, (a.Y1 + ny) / 2.0, ref best, ref x, ref y);

            // The nearest of the line to the centre, where it falls inside the segment and its direction inside the sweep.
            double length2 = dx * dx + dy * dy;

            if (length2 > 0.0)
            {
                double t = ((a.Cx - s.X0) * dx + (a.Cy - s.Y0) * dy) / length2;

                if (t > 0.0 && t < 1.0)
                {
                    double px = s.X0 + t * dx;
                    double py = s.Y0 + t * dy;
                    d = ToArc(px, py, a, out nx, out ny);
                    Keep(d, (px + nx) / 2.0, (py + ny) / 2.0, ref best, ref x, ref y);
                }
            }

            return best;
        }

        private static double ArcArc(in CadPrimitive a, in CadPrimitive b, out double x, out double y)
        {
            double best = double.MaxValue;
            x = y = 0.0;
            double nx,
                ny,
                d;
            double dx = b.Cx - a.Cx;
            double dy = b.Cy - a.Cy;
            double between = Math.Sqrt(dx * dx + dy * dy);

            if (between > 0.0)
            {
                // Where the circles meet, inside both sweeps.
                double along = (between * between + a.R * a.R - b.R * b.R) / (2.0 * between);
                double height2 = a.R * a.R - along * along;

                if (height2 >= 0.0)
                {
                    double height = Math.Sqrt(height2);
                    double ux = dx / between;
                    double uy = dy / between;

                    for (int k = 0; k < 2; k++)
                    {
                        double sign = k == 0 ? 1.0 : -1.0;
                        double px = a.Cx + ux * along - uy * height * sign;
                        double py = a.Cy + uy * along + ux * height * sign;

                        if (
                            a.Includes(Math.Atan2(py - a.Cy, px - a.Cx)) && b.Includes(Math.Atan2(py - b.Cy, px - b.Cx))
                        )
                        {
                            x = px;
                            y = py;
                            return 0.0;
                        }
                    }
                }

                // The nearest places of the two circles lie on the line of the centres.
                double ex = dx / between;
                double ey = dy / between;

                for (int k = 0; k < 2; k++)
                {
                    double sa = k == 0 ? 1.0 : -1.0;
                    double ax = a.Cx + sa * a.R * ex;
                    double ay = a.Cy + sa * a.R * ey;

                    if (!a.Includes(Math.Atan2(ay - a.Cy, ax - a.Cx)))
                    {
                        continue;
                    }

                    d = ToArc(ax, ay, b, out nx, out ny);
                    Keep(d, (ax + nx) / 2.0, (ay + ny) / 2.0, ref best, ref x, ref y);
                }
            }
            else if (a.Includes(b.Start) || b.Includes(a.Start))
            {
                // Circles about one centre are as far apart as their radii where their sweeps share a direction.
                x = a.Cx + a.R;
                y = a.Cy;
                best = Math.Abs(a.R - b.R);
            }

            d = ToArc(a.X0, a.Y0, b, out nx, out ny);
            Keep(d, (a.X0 + nx) / 2.0, (a.Y0 + ny) / 2.0, ref best, ref x, ref y);
            d = ToArc(a.X1, a.Y1, b, out nx, out ny);
            Keep(d, (a.X1 + nx) / 2.0, (a.Y1 + ny) / 2.0, ref best, ref x, ref y);
            d = ToArc(b.X0, b.Y0, a, out nx, out ny);
            Keep(d, (b.X0 + nx) / 2.0, (b.Y0 + ny) / 2.0, ref best, ref x, ref y);
            d = ToArc(b.X1, b.Y1, a, out nx, out ny);
            Keep(d, (b.X1 + nx) / 2.0, (b.Y1 + ny) / 2.0, ref best, ref x, ref y);
            return best;
        }
    }
}
