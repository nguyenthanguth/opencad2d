using System;
using System.Collections.Generic;

namespace OpenCAD2D
{
    /// <summary>
    /// A packed R-tree over boxes, built once and searched many times: the boxes are sorted by the Sort-Tile-Recursive rule of
    /// Leutenegger, Lopez and Edgington (1997), by the X of their centres into slices of about sqrt(n / 16) leaves and each
    /// slice by the Y, then every 16 in a row make a node, and the nodes are packed the same way up to one. Searching a box
    /// that meets 40 of 100 000 boxes looks at some 400 of them. A drawing is a title block and a few dense areas, where a
    /// grid of equal cells is too coarse for the one and too fine for the other. Every sort ends in the number of the box,
    /// so the tree and the order of its answers are the same on every run and every runtime.
    /// </summary>
    internal sealed class CadRTree
    {
        private const int Fanout = 16;

        // Level 0 holds the boxes themselves, sorted; level k + 1 holds the boxes of the nodes of level k, sorted. The
        // entries 16 j to 16 j + 15 of a level are the node j of it; an entry of level 0 names a box, of a higher level a node
        // of the level below.
        private readonly double[][] _box;
        private readonly int[][] _id;
        private readonly int[] _count;

        /// <summary>Makes the tree of boxes given as four numbers each: least X, least Y, greatest X, greatest Y.</summary>
        /// <param name="boxes">The boxes; none may be empty or hold a number that is not a number.</param>
        /// <param name="count">How many boxes.</param>
        public CadRTree(double[] boxes, int count)
        {
            var box = new List<double[]>();
            var id = new List<int[]>();
            var counts = new List<int>();
            var ids = new int[count];

            for (int i = 0; i < count; i++)
            {
                ids[i] = i;
            }

            double[] levelBox = boxes;
            int[] levelId = ids;
            int levelCount = count;

            while (true)
            {
                Sort(levelBox, levelId, levelCount, out double[] sortedBox, out int[] sortedId);
                box.Add(sortedBox);
                id.Add(sortedId);
                counts.Add(levelCount);

                if (levelCount <= Fanout)
                {
                    break;
                }

                // The nodes of this level become the entries of the next.
                int nodes = (levelCount + Fanout - 1) / Fanout;
                var nodeBox = new double[nodes * 4];
                var nodeId = new int[nodes];

                for (int n = 0; n < nodes; n++)
                {
                    int from = n * Fanout;
                    int to = Math.Min(levelCount, from + Fanout);
                    double minX = double.PositiveInfinity;
                    double minY = double.PositiveInfinity;
                    double maxX = double.NegativeInfinity;
                    double maxY = double.NegativeInfinity;

                    for (int e = from; e < to; e++)
                    {
                        minX = Math.Min(minX, sortedBox[4 * e]);
                        minY = Math.Min(minY, sortedBox[4 * e + 1]);
                        maxX = Math.Max(maxX, sortedBox[4 * e + 2]);
                        maxY = Math.Max(maxY, sortedBox[4 * e + 3]);
                    }

                    nodeBox[4 * n] = minX;
                    nodeBox[4 * n + 1] = minY;
                    nodeBox[4 * n + 2] = maxX;
                    nodeBox[4 * n + 3] = maxY;
                    nodeId[n] = n;
                }

                levelBox = nodeBox;
                levelId = nodeId;
                levelCount = nodes;
            }

            _box = box.ToArray();
            _id = id.ToArray();
            _count = counts.ToArray();
        }

        /// <summary>
        /// Puts the numbers of the boxes that touch a box into a list, in the order the tree meets them. A box that only touches
        /// at an edge or a corner is given; the caller tests the real shapes.
        /// </summary>
        /// <param name="minX">The least X of the box searched.</param>
        /// <param name="minY">The least Y.</param>
        /// <param name="maxX">The greatest X.</param>
        /// <param name="maxY">The greatest Y.</param>
        /// <param name="hits">The list the numbers are added to.</param>
        public void Search(double minX, double minY, double maxX, double maxY, List<int> hits)
        {
            if (_count.Length == 0 || _count[0] == 0)
            {
                return;
            }

            // The nodes still to look at: the level and the number of each, the root's level on top.
            int top = _count.Length - 1;
            var level = new int[Fanout * _count.Length + 1];
            var node = new int[level.Length];
            level[0] = top;
            node[0] = 0;
            int depth = 1;

            while (depth > 0)
            {
                depth--;
                int l = level[depth];
                int from = node[depth] * Fanout;
                int to = Math.Min(_count[l], from + Fanout);
                double[] box = _box[l];
                int[] ids = _id[l];

                for (int e = from; e < to; e++)
                {
                    if (
                        box[4 * e] <= maxX
                        && box[4 * e + 2] >= minX
                        && box[4 * e + 1] <= maxY
                        && box[4 * e + 3] >= minY
                    )
                    {
                        if (l == 0)
                        {
                            hits.Add(ids[e]);
                        }
                        else
                        {
                            level[depth] = l - 1;
                            node[depth] = ids[e];
                            depth++;
                        }
                    }
                }
            }
        }

        /// <summary>
        /// The entries in the order of the Sort-Tile-Recursive rule: by the X of their centres, cut into slices of whole nodes,
        /// each slice by the Y of their centres; a tie ends at the entry's own number.
        /// </summary>
        private static void Sort(double[] box, int[] id, int count, out double[] sortedBox, out int[] sortedId)
        {
            var centreX = new double[count];
            var centreY = new double[count];
            var order = new int[count];

            for (int i = 0; i < count; i++)
            {
                centreX[i] = box[4 * i] + box[4 * i + 2];
                centreY[i] = box[4 * i + 1] + box[4 * i + 3];
                order[i] = i;
            }

            Array.Sort(order, (a, b) =>
            {
                int c = centreX[a].CompareTo(centreX[b]);
                return c != 0 ? c : a.CompareTo(b);
            });

            int nodes = (count + Fanout - 1) / Fanout;
            int slices = (int)Math.Ceiling(Math.Sqrt(nodes));
            int perSlice = slices == 0 ? 0 : ((nodes + slices - 1) / slices) * Fanout;

            for (int from = 0; from < count; from += perSlice)
            {
                int length = Math.Min(perSlice, count - from);
                Array.Sort(
                    order,
                    from,
                    length,
                    Comparer<int>.Create(
                        (a, b) =>
                        {
                            int c = centreY[a].CompareTo(centreY[b]);
                            return c != 0 ? c : a.CompareTo(b);
                        }
                    )
                );
            }

            sortedBox = new double[count * 4];
            sortedId = new int[count];

            for (int i = 0; i < count; i++)
            {
                int from = order[i];
                sortedBox[4 * i] = box[4 * from];
                sortedBox[4 * i + 1] = box[4 * from + 1];
                sortedBox[4 * i + 2] = box[4 * from + 2];
                sortedBox[4 * i + 3] = box[4 * from + 3];
                sortedId[i] = id[from];
            }
        }
    }
}
