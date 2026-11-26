using System;

namespace OpenCAD2D
{
    /// <summary>The least distance between two shapes and the two points, one on each, that are that far apart.</summary>
    internal struct CadNearest
    {
        public double Distance;
        public double Ax;
        public double Ay;
        public double Bx;
        public double By;

        public static CadNearest None => new CadNearest { Distance = double.PositiveInfinity };

        /// <summary>Keeps the nearer of this and a candidate.</summary>
        public void Take(CadNearest other)
        {
            if (other.Distance < Distance)
            {
                this = other;
            }
        }
    }

    /// <summary>
    /// The exact distances the gap rules measure: between segments, between a word's frame (a turned rectangle) and a segment, and
    /// between two frames. Every one is 0 where the shapes meet or one lies in the other, and carries the two nearest points, so
    /// that a violation can name the place the gap is.
    /// </summary>
    internal static class CadStandardDistance
    {
        /// <summary>The corners of a frame, counter-clockwise, X and Y in turn.</summary>
        public static double[] Corners(in CadTextFrame frame)
        {
            var corners = new double[8];

            for (int i = 0; i < 4; i++)
            {
                frame.Corner(i, 0.0, out corners[2 * i], out corners[2 * i + 1]);
            }

            return corners;
        }

        /// <summary>Whether a point lies in a convex polygon of four counter-clockwise corners, its edges included.</summary>
        public static bool Inside(double[] corners, double x, double y)
        {
            for (int i = 0; i < 4; i++)
            {
                int j = (i + 1) % 4;
                double cross =
                    (corners[2 * j] - corners[2 * i]) * (y - corners[2 * i + 1])
                    - (corners[2 * j + 1] - corners[2 * i + 1]) * (x - corners[2 * i]);

                if (cross < 0.0)
                {
                    return false;
                }
            }

            return true;
        }

        /// <summary>The distance from a point to a segment, with the point of the segment that is nearest.</summary>
        public static CadNearest PointSegment(double px, double py, double ax, double ay, double bx, double by)
        {
            double dx = bx - ax;
            double dy = by - ay;
            double length = dx * dx + dy * dy;
            double t = length > 0.0 ? Math.Max(0.0, Math.Min(1.0, ((px - ax) * dx + (py - ay) * dy) / length)) : 0.0;
            double qx = ax + t * dx;
            double qy = ay + t * dy;
            return new CadNearest
            {
                Distance = Math.Sqrt((px - qx) * (px - qx) + (py - qy) * (py - qy)),
                Ax = px,
                Ay = py,
                Bx = qx,
                By = qy,
            };
        }

        /// <summary>The distance between two segments: 0, at the point they cross, where they do.</summary>
        public static CadNearest SegmentSegment(
            double ax,
            double ay,
            double bx,
            double by,
            double cx,
            double cy,
            double dx,
            double dy
        )
        {
            double rx = bx - ax;
            double ry = by - ay;
            double sx = dx - cx;
            double sy = dy - cy;
            double denominator = rx * sy - ry * sx;

            if (denominator != 0.0)
            {
                double t = ((cx - ax) * sy - (cy - ay) * sx) / denominator;
                double u = ((cx - ax) * ry - (cy - ay) * rx) / denominator;

                if (t >= 0.0 && t <= 1.0 && u >= 0.0 && u <= 1.0)
                {
                    double x = ax + t * rx;
                    double y = ay + t * ry;
                    return new CadNearest
                    {
                        Distance = 0.0,
                        Ax = x,
                        Ay = y,
                        Bx = x,
                        By = y,
                    };
                }
            }

            CadNearest best = PointSegment(ax, ay, cx, cy, dx, dy);
            best.Take(PointSegment(bx, by, cx, cy, dx, dy));
            CadNearest back = PointSegment(cx, cy, ax, ay, bx, by);
            best.Take(Swap(back));
            back = PointSegment(dx, dy, ax, ay, bx, by);
            best.Take(Swap(back));
            return best;
        }

        private static CadNearest Swap(CadNearest n) =>
            new CadNearest
            {
                Distance = n.Distance,
                Ax = n.Bx,
                Ay = n.By,
                Bx = n.Ax,
                By = n.Ay,
            };

        /// <summary>The distance from a frame, a turned rectangle, to a segment: the first point of the answer is on the frame.</summary>
        public static CadNearest FrameSegment(double[] corners, double ax, double ay, double bx, double by)
        {
            if (Inside(corners, ax, ay))
            {
                return new CadNearest
                {
                    Distance = 0.0,
                    Ax = ax,
                    Ay = ay,
                    Bx = ax,
                    By = ay,
                };
            }

            if (Inside(corners, bx, by))
            {
                return new CadNearest
                {
                    Distance = 0.0,
                    Ax = bx,
                    Ay = by,
                    Bx = bx,
                    By = by,
                };
            }

            CadNearest best = CadNearest.None;

            for (int i = 0; i < 4; i++)
            {
                int j = (i + 1) % 4;
                best.Take(
                    SegmentSegment(
                        corners[2 * i],
                        corners[2 * i + 1],
                        corners[2 * j],
                        corners[2 * j + 1],
                        ax,
                        ay,
                        bx,
                        by
                    )
                );

                if (best.Distance == 0.0)
                {
                    break;
                }
            }

            return best;
        }

        /// <summary>The distance between two frames: 0 where they share an area or touch.</summary>
        public static CadNearest FrameFrame(double[] a, double[] b)
        {
            for (int i = 0; i < 4; i++)
            {
                if (Inside(b, a[2 * i], a[2 * i + 1]))
                {
                    return new CadNearest
                    {
                        Distance = 0.0,
                        Ax = a[2 * i],
                        Ay = a[2 * i + 1],
                        Bx = a[2 * i],
                        By = a[2 * i + 1],
                    };
                }

                if (Inside(a, b[2 * i], b[2 * i + 1]))
                {
                    return new CadNearest
                    {
                        Distance = 0.0,
                        Ax = b[2 * i],
                        Ay = b[2 * i + 1],
                        Bx = b[2 * i],
                        By = b[2 * i + 1],
                    };
                }
            }

            CadNearest best = CadNearest.None;

            for (int i = 0; i < 4; i++)
            {
                int j = (i + 1) % 4;

                for (int k = 0; k < 4; k++)
                {
                    int l = (k + 1) % 4;
                    best.Take(
                        SegmentSegment(
                            a[2 * i],
                            a[2 * i + 1],
                            a[2 * j],
                            a[2 * j + 1],
                            b[2 * k],
                            b[2 * k + 1],
                            b[2 * l],
                            b[2 * l + 1]
                        )
                    );
                }
            }

            return best;
        }
    }
}
