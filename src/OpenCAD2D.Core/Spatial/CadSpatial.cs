using System;
using System.Collections.Generic;
using GeometryHelper;

namespace OpenCAD2D
{
    /// <summary>
    /// Questions about where things are drawn, for a program that lays a drawing out and wants to score it: the box each entity
    /// really takes (<see cref="Bounds(CadDocument, CadSpace)"/>) and which of them overlap (<see cref="Overlaps"/>). Both read
    /// the display list of the space, the items the canvas draws, so the answer is what is on the screen: the letters of an SHX
    /// or TrueType word and not its insertion point, the picture of a dimension, the contents of a block however deep and
    /// scaled, the columns of an MTEXT, the width of a wide polyline, the area of a hatch. A drawing of 100 000 entities has its
    /// answers in a fraction of a second after the list is made, and again after an edit at the cost of the entities the edit
    /// changed.
    /// </summary>
    public static class CadSpatial
    {
        private static readonly string[] DefaultWords = { "TEXT", "MTEXT", "DIMENSION", "MULTILEADER" };

        /// <summary>
        /// Gets the box of every entity a space draws, in the order it draws them. The box of an entity is the box of its display
        /// items, the same that <see cref="DisplayList2.Bounds"/> holds for the whole space; an entity that draws nothing (on a
        /// frozen layer, with no words) has no row. A line from (0, 0) to (100, 0) is the box from (0, 0) to (100, 0); a TEXT
        /// 2.5 high and 10 long set at (0, 0) in an SHX font is the box of its letters and of the frame they stand in.
        /// A text in a TrueType font is measured by its font's letters, as an MTEXT's words are.
        /// </summary>
        /// <param name="document">The drawing.</param>
        /// <param name="space">The space: model space or a layout of the drawing.</param>
        /// <returns>The boxes.</returns>
        /// <exception cref="ArgumentNullException">Thrown when the document or the space is null.</exception>
        public static CadBoundsTable Bounds(CadDocument document, CadSpace space)
        {
            Check(document, space);
            CadSpatialIndex index = CadSpatialIndex.Of(document.GetDisplay(space));
            var handles = new List<string>(index.HandleCount);
            var boxes = new List<double>(index.HandleCount * 4);

            for (int i = 0; i < index.HandleCount; i++)
            {
                AddRow(index, i, handles, boxes);
            }

            return new CadBoundsTable(handles.ToArray(), boxes.ToArray());
        }

        /// <summary>
        /// Gets the boxes of the entities of some handles, in the order given, each in the space that holds it (model space or
        /// a layout). A handle the drawing has no entity of, or one that draws nothing, has no row.
        /// </summary>
        /// <param name="document">The drawing.</param>
        /// <param name="handles">The handles.</param>
        /// <returns>The boxes.</returns>
        /// <exception cref="ArgumentNullException">Thrown when the document or the handles are null.</exception>
        public static CadBoundsTable Bounds(CadDocument document, IEnumerable<string> handles)
        {
            if (document == null)
            {
                throw new ArgumentNullException(nameof(document));
            }

            if (handles == null)
            {
                throw new ArgumentNullException(nameof(handles));
            }

            var found = new List<string>();
            var boxes = new List<double>();

            foreach (string handle in handles)
            {
                CadSpace space = document.FindSpace(handle);

                if (space == null)
                {
                    continue;
                }

                CadSpatialIndex index = CadSpatialIndex.Of(document.GetDisplay(space));
                int slot = index.Find(handle);

                if (slot >= 0)
                {
                    AddRow(index, slot, found, boxes);
                }
            }

            return new CadBoundsTable(found.ToArray(), boxes.ToArray());
        }

        private static void AddRow(CadSpatialIndex index, int slot, List<string> handles, List<double> boxes)
        {
            double[] box = index.HandleBox;

            if (box[4 * slot] > box[4 * slot + 2] || box[4 * slot + 1] > box[4 * slot + 3])
            {
                return;
            }

            handles.Add(index.Handles[slot]);
            boxes.Add(box[4 * slot]);
            boxes.Add(box[4 * slot + 1]);
            boxes.Add(box[4 * slot + 2]);
            boxes.Add(box[4 * slot + 3]);
        }

        /// <summary>
        /// Finds the entities whose words overlap what is drawn near them. A word is the frame of a text (a TEXT, an MTEXT's
        /// words one run of a line at a time so that a gap between columns is no word, a dimension's text), the rectangle its
        /// height and the width of its letters make, turned as the text is. It overlaps a line, an arc, a polyline or a hatch's
        /// edge that runs through it, a filled area it lies in, and the frame of another word it shares an area with; one that
        /// only touches it, or comes within the tolerance of its edge, does not. Two parts of one entity never overlap: a
        /// dimension's text is not over its own lines.
        /// A row is a pair of entities with the box of every place they meet, however many their parts. With a word frame
        /// from (0, 0) to (20, 5) and a line from (10, -10) to (10, 10), the pair is one row whose box runs from (10, 0.001) to
        /// (10, 4.999) at a tolerance of 0.001.
        /// </summary>
        /// <param name="document">The drawing.</param>
        /// <param name="space">The space: model space or a layout of the drawing.</param>
        /// <param name="options">Which kinds are words, which they are looked at against and which entities the answer is about; null for the defaults.</param>
        /// <param name="tolerance">How near an edge still counts as touching it: <see cref="Tolerance.EqualPoint"/>, in the units of the drawing. Pass the tolerance, never a global.</param>
        /// <returns>The pairs, in the order the space draws the entities of the words and then of the others.</returns>
        /// <exception cref="ArgumentNullException">Thrown when the document or the space is null.</exception>
        public static CadOverlapTable Overlaps(
            CadDocument document,
            CadSpace space,
            CadOverlapOptions options,
            Tolerance tolerance
        )
        {
            Check(document, space);
            CadSpatialIndex index = CadSpatialIndex.Of(document.GetDisplay(space));
            string[] kinds = index.KindsOf(document);
            HashSet<string> words = KindSet(options?.Words) ?? KindSet(DefaultWords);
            HashSet<string> over = KindSet(options?.Over);
            var flags = new byte[index.HandleCount];

            for (int i = 0; i < flags.Length; i++)
            {
                string kind = kinds[i];

                if (kind != null && words.Contains(kind))
                {
                    flags[i] |= WordFlag;
                }

                if (over == null || (kind != null && over.Contains(kind)))
                {
                    flags[i] |= OverFlag;
                }
            }

            bool[] asked = null;

            if (options?.Handles != null)
            {
                asked = new bool[index.HandleCount];

                foreach (string handle in options.Handles)
                {
                    int slot = index.Find(handle);

                    if (slot >= 0)
                    {
                        asked[slot] = true;
                    }
                }
            }

            var found = new List<Meeting>();
            var hits = new List<int>();
            double margin = Math.Max(tolerance.EqualPoint, 0.0);

            // The words asked about (all, where none are asked) against everything near them.
            for (int leaf = 0; leaf < index.LeafCount; leaf++)
            {
                int word = index.LeafHandle[leaf];

                if (
                    index.LeafKind[leaf] == CadSpatialIndex.TextLeaf
                    && (flags[word] & WordFlag) != 0
                    && (asked == null || asked[word])
                )
                {
                    Examine(index, leaf, margin, flags, hits, found, false, null);
                }
            }

            // Words of other entities that meet what is asked about: the leaves of the asked entities are searched, and the
            // words found are of entities not asked about (those were done above).
            if (asked != null)
            {
                for (int leaf = 0; leaf < index.LeafCount; leaf++)
                {
                    if (asked[index.LeafHandle[leaf]] && (flags[index.LeafHandle[leaf]] & OverFlag) != 0)
                    {
                        Examine(index, leaf, margin, flags, hits, found, true, asked);
                    }
                }
            }

            return Collect(index, found);
        }

        private const byte WordFlag = 1;
        private const byte OverFlag = 2;

        /// <summary>
        /// Looks for what a leaf meets. With <paramref name="fromOther"/> false the leaf is a word and the others are what it
        /// is over; with it true the leaf is what is over and the words found near it are those of entities not in
        /// <paramref name="skip"/>.
        /// </summary>
        private static void Examine(
            CadSpatialIndex index,
            int leaf,
            double margin,
            byte[] flags,
            List<int> hits,
            List<Meeting> found,
            bool fromOther,
            bool[] skip
        )
        {
            double[] box = index.LeafBox;
            hits.Clear();
            index.Tree.Search(box[4 * leaf], box[4 * leaf + 1], box[4 * leaf + 2], box[4 * leaf + 3], hits);
            int owner = index.LeafHandle[leaf];

            foreach (int near in hits)
            {
                int other = index.LeafHandle[near];

                if (other == owner)
                {
                    continue;
                }

                int word = fromOther ? near : leaf;
                int under = fromOther ? leaf : near;
                int wordHandle = index.LeafHandle[word];
                int underHandle = index.LeafHandle[under];

                if (
                    fromOther
                    && (
                        index.LeafKind[word] != CadSpatialIndex.TextLeaf
                        || (flags[wordHandle] & WordFlag) == 0
                        || skip[wordHandle]
                    )
                )
                {
                    continue;
                }

                if ((flags[underHandle] & OverFlag) == 0)
                {
                    continue;
                }

                CadMeeting meeting = CadMeeting.None;

                if (index.LeafKind[under] == CadSpatialIndex.TextLeaf)
                {
                    // Two words: the one met first by the list is the row's word, whichever found the other.
                    int first = Math.Min(word, under);
                    int second = Math.Max(word, under);

                    if (
                        !CadSpatialShapes.MeetFrames(
                            index.Frames[index.LeafFrame[first]],
                            index.Frames[index.LeafFrame[second]],
                            margin,
                            out meeting
                        )
                    )
                    {
                        continue;
                    }

                    found.Add(Row(Math.Min(wordHandle, underHandle), Math.Max(wordHandle, underHandle), meeting));
                    continue;
                }

                CadTextFrame frame = index.Frames[index.LeafFrame[word]];

                if (frame.X1 - frame.X0 <= 2.0 * margin || frame.Y1 - frame.Y0 <= 2.0 * margin)
                {
                    continue;
                }

                MeetCurve(frame, margin, index.LeafItem[under], ref meeting);

                if (meeting.Any)
                {
                    found.Add(Row(wordHandle, underHandle, meeting));
                }
            }
        }

        private static void MeetCurve(in CadTextFrame frame, double margin, DisplayItem2 item, ref CadMeeting meeting)
        {
            switch (item)
            {
                case DisplayPolyline2 chain:
                    for (int i = 0; i + 1 < chain.Count; i++)
                    {
                        CadSpatialShapes.ClipSegment(
                            frame,
                            margin,
                            chain.VertexX(i),
                            chain.VertexY(i),
                            chain.VertexX(i + 1),
                            chain.VertexY(i + 1),
                            ref meeting
                        );
                    }

                    if (chain.Closed && chain.Count > 2)
                    {
                        CadSpatialShapes.ClipSegment(
                            frame,
                            margin,
                            chain.VertexX(chain.Count - 1),
                            chain.VertexY(chain.Count - 1),
                            chain.VertexX(0),
                            chain.VertexY(0),
                            ref meeting
                        );
                    }

                    break;
                case DisplaySegments2 segments:
                    IReadOnlyList<double> all = segments.Xy;

                    for (int i = 0; i + 3 < all.Count; i += 4)
                    {
                        CadSpatialShapes.ClipSegment(
                            frame,
                            margin,
                            all[i],
                            all[i + 1],
                            all[i + 2],
                            all[i + 3],
                            ref meeting
                        );
                    }

                    break;
                case DisplayArc2 arc:
                    double[] flat = arc.Flatten(Math.Max(margin, 1E-6));
                    CadSpatialShapes.ClipChain(frame, margin, flat, flat.Length / 2, false, ref meeting);
                    break;
                case DisplayFill2 fill:
                    CadSpatialShapes.MeetFill(frame, margin, fill, ref meeting);
                    break;
            }
        }

        /// <summary>One place two entities meet: the numbers of the two in the order the list draws them and the box of the place.</summary>
        private struct Meeting
        {
            public int First;
            public int Second;
            public double MinX;
            public double MinY;
            public double MaxX;
            public double MaxY;
        }

        private static Meeting Row(int first, int second, CadMeeting meeting) =>
            new Meeting
            {
                First = first,
                Second = second,
                MinX = meeting.MinX,
                MinY = meeting.MinY,
                MaxX = meeting.MaxX,
                MaxY = meeting.MaxY,
            };

        /// <summary>
        /// Sorts the places by the pair, the word first, and makes one row of the places of a pair. The sort ends in the key of the
        /// pair, and the boxes of a pair are joined by taking least and greatest, which no order changes.
        /// </summary>
        private static CadOverlapTable Collect(CadSpatialIndex index, List<Meeting> found)
        {
            var keys = new long[found.Count];
            var order = new int[found.Count];

            for (int i = 0; i < keys.Length; i++)
            {
                keys[i] = ((long)found[i].First << 32) | (uint)found[i].Second;
                order[i] = i;
            }

            Array.Sort(keys, order);
            var words = new List<string>();
            var others = new List<string>();
            var boxes = new List<double>();

            for (int i = 0; i < keys.Length; )
            {
                int j = i;
                Meeting joined = found[order[i]];

                while (++j < keys.Length && keys[j] == keys[i])
                {
                    Meeting next = found[order[j]];
                    joined.MinX = Math.Min(joined.MinX, next.MinX);
                    joined.MinY = Math.Min(joined.MinY, next.MinY);
                    joined.MaxX = Math.Max(joined.MaxX, next.MaxX);
                    joined.MaxY = Math.Max(joined.MaxY, next.MaxY);
                }

                words.Add(index.Handles[joined.First]);
                others.Add(index.Handles[joined.Second]);
                boxes.Add(joined.MinX);
                boxes.Add(joined.MinY);
                boxes.Add(joined.MaxX);
                boxes.Add(joined.MaxY);
                i = j;
            }

            return new CadOverlapTable(words.ToArray(), others.ToArray(), boxes.ToArray());
        }

        /// <summary>The kinds of a list in capitals, MLEADER taken as MULTILEADER; null for a list that is null.</summary>
        private static HashSet<string> KindSet(IEnumerable<string> kinds)
        {
            if (kinds == null)
            {
                return null;
            }

            var set = new HashSet<string>(StringComparer.Ordinal);

            foreach (string kind in kinds)
            {
                string name = (kind ?? string.Empty).Trim().ToUpperInvariant();
                set.Add(name == "MLEADER" ? "MULTILEADER" : name);
            }

            return set;
        }

        private static void Check(CadDocument document, CadSpace space)
        {
            if (document == null)
            {
                throw new ArgumentNullException(nameof(document));
            }

            if (space == null)
            {
                throw new ArgumentNullException(nameof(space));
            }
        }
    }
}
