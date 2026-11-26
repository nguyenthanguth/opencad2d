using System;
using System.Collections.Generic;
using GeometryHelper;

namespace OpenCAD2D
{
    public static partial class CadSpatial
    {
        // The most free rectangles held at once, and the most rectangles looked at in all (some seconds' work): past either the frame
        // is too crowded to give them all, and a least size is asked for.
        private const int MostFreeRectangles = 400000;
        private const long MostWork = 400000000L;

        // A slanted line is cut into pieces whose boxes are at most this share of the frame's shorter side across.
        private const double PiecesAcross = 32.0;
        private const int MostPieces = 256;

        private struct Rect
        {
            public double X0;
            public double Y0;
            public double X1;
            public double Y1;

            public Rect(double x0, double y0, double x1, double y1)
            {
                X0 = x0;
                Y0 = y0;
                X1 = x1;
                Y1 = y1;
            }
        }

        /// <summary>
        /// Finds the empty places of a frame: the largest rectangles, not turned, that no line or word of the kinds asked about
        /// runs through (or comes within the margin of) and that cannot be made larger without doing so. A thing whose box fits
        /// in the frame at all lies inside one of them, which is what a program that lays words out looks for. A frame 100 by 50
        /// holding one vertical line at x 40 from y 0 to y 50 has two: from (0, 0) to (40, 50) and from (40, 0) to (100, 50); one
        /// short line at x 40 from y 20 to y 30 has four, the two halves of the frame and the strips above and below it.
        /// A word is taken by the box of its turned frame, cut into pieces when it is turned off the axes, a line by the boxes
        /// of its segments, a slanted one cut into pieces of at most a thirty-second of the frame's shorter side across so that a
        /// diagonal does not take the whole square it spans, an arc as the chain it is drawn as, a hatch or any filled area by its
        /// box. The margin grows each of them by that much on every side.
        /// The rectangles are found by the free-rectangle list of the MaxRects bin packer (Jukka Jylanki, "A Thousand Ways to Pack
        /// the Bin", 2010): the list starts as the frame and each obstacle, taken in the order of its box, cuts every rectangle it
        /// enters into the largest pieces on either side of it, a piece lying in another rectangle dropped. The list is
        /// exact whatever the order. A frame crowded with thousands of small things has thousands of rectangles: give the width
        /// and height of the thing to place as the least size and only the rectangles it could stand in are found, which is
        /// exact and much faster; a frame so crowded that listing every rectangle takes some seconds is refused.
        /// </summary>
        /// <param name="document">The drawing.</param>
        /// <param name="space">The space: model space or a layout of the drawing.</param>
        /// <param name="frame">The frame, a box above nought each way.</param>
        /// <param name="options">The kinds that take room, the margin and the least size; null for every kind, no margin and no least size.</param>
        /// <param name="tolerance">How small a piece, or how little a thing enters a rectangle, still counts: <see cref="Tolerance.EqualPoint"/>. Pass the tolerance, never a global.</param>
        /// <returns>The rectangles, the largest first.</returns>
        /// <exception cref="ArgumentNullException">Thrown when the document or the space is null.</exception>
        /// <exception cref="ArgumentException">Thrown when the frame is empty or has no width or height, or the margin or a least size is not a finite number of nought or more.</exception>
        /// <exception cref="InvalidOperationException">Thrown when the frame is so crowded that listing every empty rectangle takes some seconds: ask for a least size.</exception>
        public static CadFreeSpace FreeSpace(
            CadDocument document,
            CadSpace space,
            DisplayBox frame,
            CadFreeSpaceOptions options,
            Tolerance tolerance
        )
        {
            Check(document, space);
            Guard.Argument(
                !frame.IsEmpty
                    && Guard.IsFinite(frame.MinX)
                    && Guard.IsFinite(frame.MinY)
                    && Guard.IsFinite(frame.MaxX)
                    && Guard.IsFinite(frame.MaxY)
                    && frame.MaxX > frame.MinX
                    && frame.MaxY > frame.MinY,
                nameof(frame),
                "The frame is a finite box with a width and a height."
            );
            double margin = options?.Margin ?? 0.0;
            double minWidth = options?.MinWidth ?? 0.0;
            double minHeight = options?.MinHeight ?? 0.0;
            Guard.Argument(
                Guard.IsFinite(margin) && margin >= 0.0,
                nameof(options),
                "The margin is a finite number of nought or more."
            );
            Guard.Argument(
                Guard.IsFinite(minWidth) && minWidth >= 0.0 && Guard.IsFinite(minHeight) && minHeight >= 0.0,
                nameof(options),
                "The least width and height are finite numbers of nought or more."
            );
            double eps = Math.Max(tolerance.EqualPoint, 0.0);
            CadSpatialIndex index = CadSpatialIndex.Of(document.GetDisplay(space));
            bool[] taken = Taken(document, index, options?.Kinds);
            double step = Math.Max(
                Math.Min(frame.MaxX - frame.MinX, frame.MaxY - frame.MinY) / PiecesAcross,
                4.0 * eps
            );
            var obstacles = new List<Rect>();
            var hits = new List<int>();
            index.Tree.Search(frame.MinX - margin, frame.MinY - margin, frame.MaxX + margin, frame.MaxY + margin, hits);
            var reach = new Rect(frame.MinX - margin, frame.MinY - margin, frame.MaxX + margin, frame.MaxY + margin);

            foreach (int leaf in hits)
            {
                if (taken[index.LeafHandle[leaf]])
                {
                    ObstaclesOf(index, leaf, reach, margin, step, eps, obstacles);
                }
            }

            obstacles.Sort(CompareRects);
            var free = new List<Rect> { new Rect(frame.MinX, frame.MinY, frame.MaxX, frame.MaxY) };
            var kept = new List<Rect>();
            var pieces = new List<Rect>();
            Rect last = default;
            long work = 0;

            for (int i = 0; i < obstacles.Count; i++)
            {
                Rect o = obstacles[i];

                if (i > 0 && CompareRects(o, last) == 0)
                {
                    continue;
                }

                last = o;
                kept.Clear();
                pieces.Clear();
                work += free.Count;

                foreach (Rect r in free)
                {
                    if (!(o.X0 < r.X1 - eps && o.X1 > r.X0 + eps && o.Y0 < r.Y1 - eps && o.Y1 > r.Y0 + eps))
                    {
                        kept.Add(r);
                        continue;
                    }

                    AddPiece(
                        pieces,
                        r.X0,
                        r.Y0,
                        Math.Min(o.X0, r.X1),
                        r.Y1,
                        o.X0 > r.X0 + eps,
                        minWidth,
                        minHeight,
                        eps
                    );
                    AddPiece(
                        pieces,
                        Math.Max(o.X1, r.X0),
                        r.Y0,
                        r.X1,
                        r.Y1,
                        o.X1 < r.X1 - eps,
                        minWidth,
                        minHeight,
                        eps
                    );
                    AddPiece(
                        pieces,
                        r.X0,
                        r.Y0,
                        r.X1,
                        Math.Min(o.Y0, r.Y1),
                        o.Y0 > r.Y0 + eps,
                        minWidth,
                        minHeight,
                        eps
                    );
                    AddPiece(
                        pieces,
                        r.X0,
                        Math.Max(o.Y1, r.Y0),
                        r.X1,
                        r.Y1,
                        o.Y1 < r.Y1 - eps,
                        minWidth,
                        minHeight,
                        eps
                    );
                }

                if (kept.Count == free.Count)
                {
                    continue;
                }

                work += (long)pieces.Count * (kept.Count + pieces.Count);

                // A piece in a rectangle of the list, or in another piece (the first of two that are one kept), is not largest.
                for (int p = 0; p < pieces.Count; p++)
                {
                    if (!InAnother(pieces, p, kept, eps))
                    {
                        kept.Add(pieces[p]);
                    }
                }

                if (kept.Count > MostFreeRectangles || work > MostWork)
                {
                    throw new InvalidOperationException(
                        "The frame is too crowded to list every empty rectangle: ask for a least width and height, or a smaller frame."
                    );
                }

                List<Rect> swap = free;
                free = kept;
                kept = swap;
            }

            free.Sort(CompareFree);
            var boxes = new double[free.Count * 4];

            for (int i = 0; i < free.Count; i++)
            {
                boxes[4 * i] = free[i].X0;
                boxes[4 * i + 1] = free[i].Y0;
                boxes[4 * i + 2] = free[i].X1;
                boxes[4 * i + 3] = free[i].Y1;
            }

            return new CadFreeSpace(boxes);
        }

        /// <summary>
        /// Whether a candidate place is free: nothing of the kinds asked about runs through it or comes within the margin of it.
        /// The candidate is taken as a word is by <see cref="Overlaps"/>, on the real shapes and not their boxes: a line passing
        /// a turned frame's corner at 1 mm is outside it, a word whose frame overlaps it by a hair inside the tolerance is
        /// outside it. A candidate from (40, 10) to (60, 20) fits beside a line at x 70, and does not fit with a margin of 15 or
        /// with the line at x 50.
        /// </summary>
        /// <param name="document">The drawing.</param>
        /// <param name="space">The space: model space or a layout of the drawing.</param>
        /// <param name="candidate">The place, as <see cref="CadMeasurement.Frames"/> gives one or <see cref="CadFrame.FromBox"/> makes one.</param>
        /// <param name="options">The kinds that take room and the margin; null for every kind and no margin. The least size is not used.</param>
        /// <param name="tolerance">How near an edge still counts as touching it: <see cref="Tolerance.EqualPoint"/>. Pass the tolerance, never a global.</param>
        /// <returns>True where nothing is in the way.</returns>
        /// <exception cref="ArgumentNullException">Thrown when the document, the space or the candidate is null.</exception>
        /// <exception cref="ArgumentException">Thrown when the margin is not a finite number of nought or more.</exception>
        public static bool Fits(
            CadDocument document,
            CadSpace space,
            CadFrame candidate,
            CadFreeSpaceOptions options,
            Tolerance tolerance
        )
        {
            Check(document, space);

            if (candidate == null)
            {
                throw new ArgumentNullException(nameof(candidate));
            }

            double margin = options?.Margin ?? 0.0;
            Guard.Argument(
                Guard.IsFinite(margin) && margin >= 0.0,
                nameof(options),
                "The margin is a finite number of nought or more."
            );
            double eps = Math.Max(tolerance.EqualPoint, 0.0);

            // The frame drawn in by the tolerance and out by the margin: the shapes draw a frame in by what they are given.
            double shrink = eps - margin;
            CadSpatialIndex index = CadSpatialIndex.Of(document.GetDisplay(space));
            bool[] taken = Taken(document, index, options?.Kinds);
            CadTextFrame frame = candidate.ToTextFrame();
            DisplayBox around = frame.Box();
            var hits = new List<int>();
            index.Tree.Search(
                around.MinX - margin,
                around.MinY - margin,
                around.MaxX + margin,
                around.MaxY + margin,
                hits
            );

            foreach (int leaf in hits)
            {
                if (!taken[index.LeafHandle[leaf]])
                {
                    continue;
                }

                CadMeeting meeting = CadMeeting.None;

                if (index.LeafKind[leaf] == CadSpatialIndex.TextLeaf)
                {
                    if (CadSpatialShapes.MeetFrames(frame, index.Frames[index.LeafFrame[leaf]], shrink, out meeting))
                    {
                        return false;
                    }
                }
                else
                {
                    MeetCurve(frame, shrink, Math.Max(eps, 1E-6), index.LeafItem[leaf], ref meeting);

                    if (meeting.Any)
                    {
                        return false;
                    }
                }
            }

            return true;
        }

        /// <summary>Whether each entity of the list is of a kind that takes room; all of them where no kinds are given.</summary>
        private static bool[] Taken(CadDocument document, CadSpatialIndex index, IReadOnlyCollection<string> kinds)
        {
            var taken = new bool[index.HandleCount];
            HashSet<string> wanted = KindSet(kinds);

            if (wanted == null)
            {
                for (int i = 0; i < taken.Length; i++)
                {
                    taken[i] = true;
                }

                return taken;
            }

            string[] names = index.KindsOf(document);

            for (int i = 0; i < taken.Length; i++)
            {
                taken[i] = names[i] != null && wanted.Contains(names[i]);
            }

            return taken;
        }

        private static void AddPiece(
            List<Rect> pieces,
            double x0,
            double y0,
            double x1,
            double y1,
            bool cut,
            double minWidth,
            double minHeight,
            double eps
        )
        {
            if (cut && x1 - x0 > eps && y1 - y0 > eps && x1 - x0 >= minWidth - eps && y1 - y0 >= minHeight - eps)
            {
                pieces.Add(new Rect(x0, y0, x1, y1));
            }
        }

        /// <summary>Whether a new piece lies in a rectangle of the list or in another new piece; of two that are one, the first stays.</summary>
        private static bool InAnother(List<Rect> pieces, int piece, List<Rect> kept, double eps)
        {
            Rect p = pieces[piece];

            foreach (Rect k in kept)
            {
                if (Holds(k, p, eps))
                {
                    return true;
                }
            }

            for (int q = 0; q < pieces.Count; q++)
            {
                if (q != piece && Holds(pieces[q], p, eps) && (!Holds(p, pieces[q], eps) || q < piece))
                {
                    return true;
                }
            }

            return false;
        }

        private static bool Holds(Rect outer, Rect inner, double eps) =>
            outer.X0 <= inner.X0 + eps
            && outer.Y0 <= inner.Y0 + eps
            && outer.X1 >= inner.X1 - eps
            && outer.Y1 >= inner.Y1 - eps;

        private static int CompareRects(Rect a, Rect b)
        {
            int c = a.X0.CompareTo(b.X0);

            if (c != 0)
            {
                return c;
            }

            c = a.Y0.CompareTo(b.Y0);

            if (c != 0)
            {
                return c;
            }

            c = a.X1.CompareTo(b.X1);
            return c != 0 ? c : a.Y1.CompareTo(b.Y1);
        }

        /// <summary>The largest first; of one area by the least Y, then the least X, then the greatest Y, then the greatest X.</summary>
        private static int CompareFree(Rect a, Rect b)
        {
            int c = ((b.X1 - b.X0) * (b.Y1 - b.Y0)).CompareTo((a.X1 - a.X0) * (a.Y1 - a.Y0));

            if (c != 0)
            {
                return c;
            }

            c = a.Y0.CompareTo(b.Y0);

            if (c != 0)
            {
                return c;
            }

            c = a.X0.CompareTo(b.X0);

            if (c != 0)
            {
                return c;
            }

            c = a.Y1.CompareTo(b.Y1);
            return c != 0 ? c : a.X1.CompareTo(b.X1);
        }

        /// <summary>The boxes a leaf takes room by, each grown by the margin and those outside the frame left out.</summary>
        private static void ObstaclesOf(
            CadSpatialIndex index,
            int leaf,
            Rect reach,
            double margin,
            double step,
            double eps,
            List<Rect> into
        )
        {
            switch (index.LeafKind[leaf])
            {
                case CadSpatialIndex.TextLeaf:
                    CadTextFrame word = index.Frames[index.LeafFrame[leaf]];
                    word.Corner(0, 0.0, out double ax, out double ay);
                    word.Corner(1, 0.0, out double bx, out double by);
                    word.Corner(3, 0.0, out double dx, out double dy);
                    AddTurned(ax, ay, bx - ax, by - ay, dx - ax, dy - ay, reach, margin, step, eps, into);
                    break;
                case CadSpatialIndex.FillLeaf:
                    double[] box = index.LeafBox;
                    Add(into, box[4 * leaf], box[4 * leaf + 1], box[4 * leaf + 2], box[4 * leaf + 3], reach, margin);
                    break;
                case CadSpatialIndex.ChainLeaf:
                    var chain = (DisplayPolyline2)index.LeafItem[leaf];

                    for (int i = 0; i + 1 < chain.Count; i++)
                    {
                        AddSegment(
                            chain.VertexX(i),
                            chain.VertexY(i),
                            chain.VertexX(i + 1),
                            chain.VertexY(i + 1),
                            reach,
                            margin,
                            step,
                            eps,
                            into
                        );
                    }

                    if (chain.Closed && chain.Count > 2)
                    {
                        AddSegment(
                            chain.VertexX(chain.Count - 1),
                            chain.VertexY(chain.Count - 1),
                            chain.VertexX(0),
                            chain.VertexY(0),
                            reach,
                            margin,
                            step,
                            eps,
                            into
                        );
                    }

                    break;
                case CadSpatialIndex.SegmentsLeaf:
                    IReadOnlyList<double> all = ((DisplaySegments2)index.LeafItem[leaf]).Xy;

                    for (int i = 0; i + 3 < all.Count; i += 4)
                    {
                        AddSegment(all[i], all[i + 1], all[i + 2], all[i + 3], reach, margin, step, eps, into);
                    }

                    break;
                case CadSpatialIndex.ArcLeaf:
                    double[] flat = ((DisplayArc2)index.LeafItem[leaf]).Flatten(Math.Max(eps, step / 16.0));

                    for (int i = 0; i + 3 < flat.Length; i += 2)
                    {
                        AddSegment(flat[i], flat[i + 1], flat[i + 2], flat[i + 3], reach, margin, step, eps, into);
                    }

                    break;
            }
        }

        private static void Add(List<Rect> into, double x0, double y0, double x1, double y1, Rect reach, double margin)
        {
            var box = new Rect(
                Math.Min(x0, x1) - margin,
                Math.Min(y0, y1) - margin,
                Math.Max(x0, x1) + margin,
                Math.Max(y0, y1) + margin
            );

            if (box.X0 <= reach.X1 && box.X1 >= reach.X0 && box.Y0 <= reach.Y1 && box.Y1 >= reach.Y0)
            {
                into.Add(box);
            }
        }

        /// <summary>
        /// A segment as boxes: its part in the reach, one box where it runs along an axis, else pieces of at most a step across
        /// (at most 256), so that the boxes lie along the line and not over the square it spans.
        /// </summary>
        private static void AddSegment(
            double ax,
            double ay,
            double bx,
            double by,
            Rect reach,
            double margin,
            double step,
            double eps,
            List<Rect> into
        )
        {
            if (!Guard.IsFinite(ax) || !Guard.IsFinite(ay) || !Guard.IsFinite(bx) || !Guard.IsFinite(by))
            {
                return;
            }

            double t0 = 0.0;
            double t1 = 1.0;
            double dx = bx - ax;
            double dy = by - ay;

            if (
                !Limit(-dx, ax - reach.X0, ref t0, ref t1)
                || !Limit(dx, reach.X1 - ax, ref t0, ref t1)
                || !Limit(-dy, ay - reach.Y0, ref t0, ref t1)
                || !Limit(dy, reach.Y1 - ay, ref t0, ref t1)
            )
            {
                return;
            }

            double length = Math.Max(Math.Abs((t1 - t0) * dx), Math.Abs((t1 - t0) * dy));
            bool slanted = Math.Abs(dx) > eps && Math.Abs(dy) > eps;
            int count = slanted ? Math.Min(MostPieces, Math.Max(1, (int)Math.Ceiling(length / step))) : 1;

            for (int i = 0; i < count; i++)
            {
                double u = t0 + (t1 - t0) * i / count;
                double v = t0 + (t1 - t0) * (i + 1) / count;
                Add(into, ax + u * dx, ay + u * dy, ax + v * dx, ay + v * dy, reach, margin);
            }
        }

        // One side of the reach, as Liang and Barsky clip a line: false where the segment is wholly outside it.
        private static bool Limit(double p, double q, ref double t0, ref double t1)
        {
            if (p == 0.0)
            {
                return q >= 0.0;
            }

            double r = q / p;

            if (p < 0.0)
            {
                if (r > t1)
                {
                    return false;
                }

                t0 = Math.Max(t0, r);
            }
            else
            {
                if (r < t0)
                {
                    return false;
                }

                t1 = Math.Min(t1, r);
            }

            return true;
        }

        /// <summary>
        /// A rectangle given by a corner and its two sides as boxes: one where it lies along the axes, else strips along its
        /// longer side, each as wide across as a step.
        /// </summary>
        private static void AddTurned(
            double x,
            double y,
            double ux,
            double uy,
            double vx,
            double vy,
            Rect reach,
            double margin,
            double step,
            double eps,
            List<Rect> into
        )
        {
            bool onAxes = (Math.Abs(ux) <= eps || Math.Abs(uy) <= eps) && (Math.Abs(vx) <= eps || Math.Abs(vy) <= eps);
            double alongLength = Math.Max(Math.Abs(ux), Math.Abs(uy));
            double acrossLength = Math.Max(Math.Abs(vx), Math.Abs(vy));

            if (onAxes)
            {
                Add(into, x, y, x + ux + vx, y + uy + vy, reach, margin);
                return;
            }

            // Cut along the longer of the two sides.
            bool cutU = ux * ux + uy * uy >= vx * vx + vy * vy;
            double lx = cutU ? ux : vx;
            double ly = cutU ? uy : vy;
            double ox = cutU ? vx : ux;
            double oy = cutU ? vy : uy;
            double longest = Math.Max(alongLength, acrossLength);
            int count = Math.Min(MostPieces, Math.Max(1, (int)Math.Ceiling(longest / step)));

            for (int i = 0; i < count; i++)
            {
                double a = (double)i / count;
                double b = (double)(i + 1) / count;
                double x0 = x + lx * a;
                double y0 = y + ly * a;
                double x1 = x + lx * b;
                double y1 = y + ly * b;
                double minX = Math.Min(Math.Min(x0, x1), Math.Min(x0 + ox, x1 + ox));
                double maxX = Math.Max(Math.Max(x0, x1), Math.Max(x0 + ox, x1 + ox));
                double minY = Math.Min(Math.Min(y0, y1), Math.Min(y0 + oy, y1 + oy));
                double maxY = Math.Max(Math.Max(y0, y1), Math.Max(y0 + oy, y1 + oy));
                Add(into, minX, minY, maxX, maxY, reach, margin);
            }
        }
    }
}
