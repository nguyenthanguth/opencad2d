using System;

namespace OpenCAD2D
{
    /// <summary>
    /// The frame of a piece of text: its own rectangle, x along the baseline and y up it from the text's point, turned by its
    /// angle. The words of a text 2.5 high and 20 long at 30 degrees are a frame 20 by 2.5 turned 30 degrees, whose box in
    /// the drawing is 19.4 by 12.6 and mostly empty.
    /// </summary>
    internal struct CadTextFrame
    {
        public double X;
        public double Y;
        public double Cos;
        public double Sin;
        public double X0;
        public double Y0;
        public double X1;
        public double Y1;

        /// <summary>The frame of a text as its display item has it.</summary>
        public static CadTextFrame Of(DisplayText2 text)
        {
            text.GetFrame(out double x0, out double y0, out double x1, out double y1);
            return new CadTextFrame
            {
                X = text.X,
                Y = text.Y,
                Cos = Math.Cos(text.Rotation),
                Sin = Math.Sin(text.Rotation),
                X0 = x0,
                Y0 = y0,
                X1 = x1,
                Y1 = y1,
            };
        }

        /// <summary>Whether the frame has width and height and every number of it is a number.</summary>
        public bool IsUsable =>
            X1 > X0 && Y1 > Y0 && IsFinite(X) && IsFinite(Y) && IsFinite(Cos) && IsFinite(Sin) && IsFinite(X1 - X0);

        private static bool IsFinite(double value) => !double.IsNaN(value) && !double.IsInfinity(value);

        /// <summary>The corner of a number, 0 to 3 counter-clockwise from the foot of the start, of the frame grown by a margin (shrunk where it is below nought).</summary>
        public void Corner(int corner, double margin, out double x, out double y)
        {
            double lx = corner == 0 || corner == 3 ? X0 - margin : X1 + margin;
            double ly = corner < 2 ? Y0 - margin : Y1 + margin;
            x = X + lx * Cos - ly * Sin;
            y = Y + lx * Sin + ly * Cos;
        }

        /// <summary>The box of the frame in the drawing.</summary>
        public DisplayBox Box()
        {
            DisplayBox box = DisplayBox.Empty;

            for (int i = 0; i < 4; i++)
            {
                Corner(i, 0.0, out double x, out double y);
                box = box.Add(x, y);
            }

            return box;
        }
    }

    /// <summary>
    /// What the box of a place where two things meet is built from: the points of the common part, taken one at a time.
    /// </summary>
    internal struct CadMeeting
    {
        public double MinX;
        public double MinY;
        public double MaxX;
        public double MaxY;

        public static CadMeeting None =>
            new CadMeeting
            {
                MinX = double.PositiveInfinity,
                MinY = double.PositiveInfinity,
                MaxX = double.NegativeInfinity,
                MaxY = double.NegativeInfinity,
            };

        public bool Any => MinX <= MaxX;

        public void Add(double x, double y)
        {
            MinX = Math.Min(MinX, x);
            MinY = Math.Min(MinY, y);
            MaxX = Math.Max(MaxX, x);
            MaxY = Math.Max(MaxY, y);
        }
    }

    /// <summary>
    /// The tests of what overlaps what, each on the real shapes: a segment, an arc and a fill against the frame of a word, and
    /// the frames of two words against each other. The frame of a word is drawn in by the margin on every side, so that a line
    /// along its edge, or one that only touches it within the tolerance, does not meet it.
    /// </summary>
    internal static class CadSpatialShapes
    {
        private static bool IsFinite(double value) => !double.IsNaN(value) && !double.IsInfinity(value);

        /// <summary>
        /// Adds to a meeting the part of a segment that lies inside the frame drawn in by a margin (Liang and Barsky: the
        /// segment as a line in the frame's own axes, cut by the four sides).
        /// </summary>
        public static void ClipSegment(
            in CadTextFrame frame,
            double margin,
            double ax,
            double ay,
            double bx,
            double by,
            ref CadMeeting meeting
        )
        {
            if (!IsFinite(ax) || !IsFinite(ay) || !IsFinite(bx) || !IsFinite(by))
            {
                return;
            }

            double minX = frame.X0 + margin;
            double maxX = frame.X1 - margin;
            double minY = frame.Y0 + margin;
            double maxY = frame.Y1 - margin;
            double ux = ax - frame.X;
            double uy = ay - frame.Y;
            double vx = bx - frame.X;
            double vy = by - frame.Y;
            double lax = ux * frame.Cos + uy * frame.Sin;
            double lay = -ux * frame.Sin + uy * frame.Cos;
            double lbx = vx * frame.Cos + vy * frame.Sin;
            double lby = -vx * frame.Sin + vy * frame.Cos;
            double dx = lbx - lax;
            double dy = lby - lay;
            double t0 = 0.0;
            double t1 = 1.0;

            if (
                !Clip(-dx, lax - minX, ref t0, ref t1)
                || !Clip(dx, maxX - lax, ref t0, ref t1)
                || !Clip(-dy, lay - minY, ref t0, ref t1)
                || !Clip(dy, maxY - lay, ref t0, ref t1)
                || !(t1 > t0)
            )
            {
                return;
            }

            meeting.Add(ax + t0 * (bx - ax), ay + t0 * (by - ay));
            meeting.Add(ax + t1 * (bx - ax), ay + t1 * (by - ay));
        }

        // One side of the frame: false where the segment is wholly outside it. A segment along the side, outside or on it, is outside.
        private static bool Clip(double p, double q, ref double t0, ref double t1)
        {
            if (p == 0.0)
            {
                return q > 0.0;
            }

            double r = q / p;

            if (p < 0.0)
            {
                if (r > t1)
                {
                    return false;
                }

                if (r > t0)
                {
                    t0 = r;
                }
            }
            else
            {
                if (r < t0)
                {
                    return false;
                }

                if (r < t1)
                {
                    t1 = r;
                }
            }

            return true;
        }

        /// <summary>Adds the parts of a chain of X and Y in turn that lie in the frame; <paramref name="closed"/> joins its last point to its first.</summary>
        public static void ClipChain(
            in CadTextFrame frame,
            double margin,
            double[] xy,
            int count,
            bool closed,
            ref CadMeeting meeting
        )
        {
            for (int i = 0; i + 1 < count; i++)
            {
                ClipSegment(frame, margin, xy[2 * i], xy[2 * i + 1], xy[2 * i + 2], xy[2 * i + 3], ref meeting);
            }

            if (closed && count > 2)
            {
                ClipSegment(frame, margin, xy[2 * count - 2], xy[2 * count - 1], xy[0], xy[1], ref meeting);
            }
        }

        /// <summary>
        /// Adds the common part of a fill and the frame: the edges of the fill that run through it, and the corners of the
        /// frame that lie inside the fill by the even-odd rule.
        /// </summary>
        public static void MeetFill(in CadTextFrame frame, double margin, DisplayFill2 fill, ref CadMeeting meeting)
        {
            foreach (double[] ring in fill.Rings)
            {
                ClipChain(frame, margin, ring, ring.Length / 2, true, ref meeting);
            }

            for (int i = 0; i < 4; i++)
            {
                frame.Corner(i, -margin, out double x, out double y);

                if (fill.Contains(x, y))
                {
                    meeting.Add(x, y);
                }
            }
        }

        /// <summary>
        /// Whether the frames of two words share more than their edges, each drawn in by half the margin, and the box of the
        /// common part: the first clipped by the sides of the second, whose corners are taken counter-clockwise.
        /// </summary>
        public static bool MeetFrames(in CadTextFrame a, in CadTextFrame b, double margin, out CadMeeting meeting)
        {
            meeting = CadMeeting.None;
            double half = margin / 2.0;

            if (a.X1 - a.X0 <= margin || a.Y1 - a.Y0 <= margin || b.X1 - b.X0 <= margin || b.Y1 - b.Y0 <= margin)
            {
                return false;
            }

            var pa = new double[8];
            var pb = new double[8];

            for (int i = 0; i < 4; i++)
            {
                a.Corner(i, -half, out pa[2 * i], out pa[2 * i + 1]);
                b.Corner(i, -half, out pb[2 * i], out pb[2 * i + 1]);
            }

            // Separating axes: the two sides of each frame. Frames whose projections only touch are apart.
            if (
                Apart(pa, pb, a.Cos, a.Sin)
                || Apart(pa, pb, -a.Sin, a.Cos)
                || Apart(pa, pb, b.Cos, b.Sin)
                || Apart(pa, pb, -b.Sin, b.Cos)
            )
            {
                return false;
            }

            double[] clipped = pa;
            int count = 4;
            var next = new double[2 * (count + 4)];

            for (int side = 0; side < 4 && count > 0; side++)
            {
                double px = pb[2 * side];
                double py = pb[2 * side + 1];
                double qx = pb[2 * ((side + 1) % 4)];
                double qy = pb[2 * ((side + 1) % 4) + 1];
                int made = 0;

                for (int i = 0; i < count; i++)
                {
                    int j = (i + 1) % count;
                    double sx = clipped[2 * i];
                    double sy = clipped[2 * i + 1];
                    double ex = clipped[2 * j];
                    double ey = clipped[2 * j + 1];
                    double inS = (qx - px) * (sy - py) - (qy - py) * (sx - px);
                    double inE = (qx - px) * (ey - py) - (qy - py) * (ex - px);

                    if (inS >= 0.0)
                    {
                        next[2 * made] = sx;
                        next[2 * made + 1] = sy;
                        made++;
                    }

                    if ((inS >= 0.0) != (inE >= 0.0))
                    {
                        double t = inS / (inS - inE);
                        next[2 * made] = sx + t * (ex - sx);
                        next[2 * made + 1] = sy + t * (ey - sy);
                        made++;
                    }
                }

                var swap = new double[next.Length];
                Array.Copy(next, swap, 2 * made);
                clipped = swap;
                count = made;
                next = new double[2 * (count + 4)];
            }

            for (int i = 0; i < count; i++)
            {
                meeting.Add(clipped[2 * i], clipped[2 * i + 1]);
            }

            return meeting.Any;
        }

        private static bool Apart(double[] pa, double[] pb, double ax, double ay)
        {
            double minA = double.PositiveInfinity;
            double maxA = double.NegativeInfinity;
            double minB = double.PositiveInfinity;
            double maxB = double.NegativeInfinity;

            for (int i = 0; i < 4; i++)
            {
                double da = pa[2 * i] * ax + pa[2 * i + 1] * ay;
                double db = pb[2 * i] * ax + pb[2 * i + 1] * ay;
                minA = Math.Min(minA, da);
                maxA = Math.Max(maxA, da);
                minB = Math.Min(minB, db);
                maxB = Math.Max(maxB, db);
            }

            return maxA <= minB || maxB <= minA;
        }
    }
}
