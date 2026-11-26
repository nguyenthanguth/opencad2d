using System;
using System.Collections.Generic;
using System.Runtime.InteropServices;
using OpenCAD2D.Wpf;
using SkiaSharp;

namespace OpenCAD2D
{
    /// <summary>
    /// Draws a space in id mode and reads what the pixels hold: the work behind <see cref="CadRender.DrawLabels"/>. Each item is
    /// drawn in the flat colour <c>(entity number &lt;&lt; 3) | class</c>, with no antialiasing, on black, so that a pixel's colour is
    /// the class and the entity of the item on top of it.
    /// </summary>
    internal static class CadLabeller
    {
        /// <summary>The most entities a picture can number: the id has 21 bits above the 3 of the class.</summary>
        private const int MostEntities = (1 << 21) - 1;

        // What an entity is, as far as the classes care.
        private const byte OtherKind = 0;
        private const byte WordKind = 1;
        private const byte DimensionKind = 2;
        private const byte HatchKind = 3;
        private const byte InsertKind = 4;

        public static CadRenderLabels Draw(
            CadDocument document,
            CadSpace space,
            CadView view,
            CanvasOptions canvas,
            int[] classes
        )
        {
            bool[] wanted = Wanted(classes);
            DisplayList2 list = document.GetDisplay(space);
            CadSpatialIndex index = CadSpatialIndex.Of(list);

            // The entities of the space in the order the list draws them, then those a viewport shows.
            var handles = new List<string>(index.Handles);
            var slots = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);

            for (int i = 0; i < handles.Count; i++)
            {
                slots[handles[i]] = i;
            }

            IReadOnlyList<DisplayItem2> items = list.Items;

            for (int i = 0; i < list.Count; i++)
            {
                if (items[i] is DisplayViewport2 viewport)
                {
                    foreach (string handle in CadSpatialIndex.Of(viewport.Model).Handles)
                    {
                        if (!slots.ContainsKey(handle))
                        {
                            slots[handle] = handles.Count;
                            handles.Add(handle);
                        }
                    }
                }
            }

            if (handles.Count > MostEntities)
            {
                throw new ArgumentException(
                    "A picture numbers " + MostEntities + " entities at most: this space draws " + handles.Count + ".",
                    nameof(document)
                );
            }

            int count = handles.Count;
            var kinds = new byte[count];
            var categories = new byte[count];

            for (int i = 0; i < count; i++)
            {
                kinds[i] = KindOf(document.FindEntity(handles[i])?.DxfName);
                categories[i] = (byte)CategoryOf(kinds[i]);
            }

            bool blocks = wanted[CadLabelClass.Blocks];
            canvas.Background = 0;
            canvas.Paper = 0;
            canvas.IdOf = item =>
            {
                // An item with no entity of the space draws nothing: a transparent colour.
                if (item.Handle == null || !slots.TryGetValue(item.Handle, out int slot))
                {
                    return -1;
                }

                return ((slot + 1) << 3) | ClassOf(item, kinds[slot], blocks);
            };

            int width;
            int height;
            byte[] bgra;
            int rowBytes;

            using (SKBitmap picture = CadRasterWriter.Draw(document, space, view, canvas, null))
            {
                width = picture.Width;
                height = picture.Height;
                rowBytes = picture.RowBytes;
                bgra = new byte[rowBytes * height];
                Marshal.Copy(picture.GetPixels(), bgra, 0, bgra.Length);
            }

            var classOf = new byte[width * height];
            var instanceOf = new int[width * height];
            var areas = new int[count];
            var least = new int[2 * count];
            var greatest = new int[2 * count];

            for (int i = 0; i < count; i++)
            {
                least[2 * i] = int.MaxValue;
                least[2 * i + 1] = int.MaxValue;
                greatest[2 * i] = -1;
                greatest[2 * i + 1] = -1;
            }

            for (int y = 0; y < height; y++)
            {
                int from = y * rowBytes;
                int to = y * width;

                for (int x = 0; x < width; x++)
                {
                    int id = (bgra[from + 4 * x + 2] << 16) | (bgra[from + 4 * x + 1] << 8) | bgra[from + 4 * x];

                    if (id == 0)
                    {
                        continue;
                    }

                    int cls = id & 7;
                    int number = id >> 3;

                    if (cls < CadLabelClass.Words || cls > CadLabelClass.Blocks || number < 1 || number > count)
                    {
                        continue;
                    }

                    if (!wanted[cls])
                    {
                        continue;
                    }

                    int k = number - 1;
                    classOf[to + x] = (byte)cls;
                    instanceOf[to + x] = number;
                    areas[k]++;
                    least[2 * k] = Math.Min(least[2 * k], x);
                    least[2 * k + 1] = Math.Min(least[2 * k + 1], y);
                    greatest[2 * k] = Math.Max(greatest[2 * k], x);
                    greatest[2 * k + 1] = Math.Max(greatest[2 * k + 1], y);
                }
            }

            Affine2 m = view.DrawingToScreen;

            // The box the drawing gives each entity, through the matrix: NaN where it gives none (an entity a viewport shows).
            var placed = new double[4 * count];

            for (int i = 0; i < placed.Length; i++)
            {
                placed[i] = double.NaN;
            }

            CadBoundsTable table = CadSpatial.Bounds(document, space);
            double[] drawn = table.Boxes;

            for (int row = 0; row < table.Count; row++)
            {
                if (!slots.TryGetValue(table.Handles[row], out int slot))
                {
                    continue;
                }

                // The four corners of the box through the matrix: a view that is turned makes it a box of its own.
                for (int c = 0; c < 4; c++)
                {
                    double cx = drawn[4 * row + (c & 1) * 2];
                    double cy = drawn[4 * row + 1 + (c >> 1) * 2];
                    Grow(placed, 4 * slot, m.X(cx, cy), m.Y(cx, cy));
                }
            }

            // Whole pixels that hold that box and every pixel of the entity, inside the picture.
            var boxes = new double[4 * count];

            for (int k = 0; k < count; k++)
            {
                if (areas[k] == 0)
                {
                    continue;
                }

                double x0 = Math.Min(double.IsNaN(placed[4 * k]) ? double.MaxValue : placed[4 * k], least[2 * k]);
                double y0 = Math.Min(
                    double.IsNaN(placed[4 * k]) ? double.MaxValue : placed[4 * k + 1],
                    least[2 * k + 1]
                );
                double x1 = Math.Max(
                    double.IsNaN(placed[4 * k]) ? double.MinValue : placed[4 * k + 2],
                    greatest[2 * k] + 1
                );
                double y1 = Math.Max(
                    double.IsNaN(placed[4 * k]) ? double.MinValue : placed[4 * k + 3],
                    greatest[2 * k + 1] + 1
                );
                boxes[4 * k] = Math.Max(0.0, Math.Floor(x0));
                boxes[4 * k + 1] = Math.Max(0.0, Math.Floor(y0));
                boxes[4 * k + 2] = Math.Min(width, Math.Ceiling(x1));
                boxes[4 * k + 3] = Math.Min(height, Math.Ceiling(y1));
            }

            var frameInstances = new List<int>();
            var frameCorners = new List<double>();

            for (int leaf = 0; leaf < index.LeafCount; leaf++)
            {
                if (index.LeafKind[leaf] != CadSpatialIndex.TextLeaf)
                {
                    continue;
                }

                int slot = index.LeafHandle[leaf];
                CadTextFrame frame = index.Frames[index.LeafFrame[leaf]];
                frameInstances.Add(slot + 1);

                for (int c = 0; c < 4; c++)
                {
                    frame.Corner(c, 0.0, out double x, out double y);
                    frameCorners.Add(m.X(x, y));
                    frameCorners.Add(m.Y(x, y));
                }
            }

            return new CadRenderLabels(
                width,
                height,
                classOf,
                instanceOf,
                handles.ToArray(),
                categories,
                new[] { m.A, m.C, m.E, m.B, m.D, m.F, 0.0, 0.0, 1.0 },
                boxes,
                areas,
                frameInstances.ToArray(),
                frameCorners.ToArray()
            );
        }

        /// <summary>Grows the box held at <paramref name="at"/> by a point; a box that is NaN is started by it.</summary>
        private static void Grow(double[] boxes, int at, double x, double y)
        {
            if (double.IsNaN(boxes[at]))
            {
                boxes[at] = x;
                boxes[at + 1] = y;
                boxes[at + 2] = x;
                boxes[at + 3] = y;
                return;
            }

            boxes[at] = Math.Min(boxes[at], x);
            boxes[at + 1] = Math.Min(boxes[at + 1], y);
            boxes[at + 2] = Math.Max(boxes[at + 2], x);
            boxes[at + 3] = Math.Max(boxes[at + 3], y);
        }

        private static bool[] Wanted(int[] classes)
        {
            var wanted = new bool[CadLabelClass.Count];

            if (classes == null)
            {
                for (int i = 1; i < wanted.Length; i++)
                {
                    wanted[i] = true;
                }

                return wanted;
            }

            foreach (int id in classes)
            {
                if (id < CadLabelClass.Words || id > CadLabelClass.Blocks)
                {
                    throw new ArgumentException("A class to label is 1 to 5, not " + id + ".", nameof(classes));
                }

                wanted[id] = true;
            }

            return wanted;
        }

        private static byte KindOf(string dxfName)
        {
            switch (dxfName)
            {
                case "TEXT":
                case "MTEXT":
                case "ATTRIB":
                case "ATTDEF":
                    return WordKind;
                case "DIMENSION":
                    return DimensionKind;
                case "HATCH":
                case "SOLID":
                case "TRACE":
                case "WIPEOUT":
                    return HatchKind;
                case "INSERT":
                    return InsertKind;
                default:
                    return OtherKind;
            }
        }

        private static int CategoryOf(byte kind)
        {
            switch (kind)
            {
                case WordKind:
                    return CadLabelClass.Words;
                case DimensionKind:
                    return CadLabelClass.Dimensions;
                case HatchKind:
                    return CadLabelClass.Hatches;
                case InsertKind:
                    return CadLabelClass.Blocks;
                default:
                    return CadLabelClass.Lines;
            }
        }

        private static int ClassOf(DisplayItem2 item, byte kind, bool blocks)
        {
            if (kind == InsertKind && blocks)
            {
                return CadLabelClass.Blocks;
            }

            if (item is DisplayText2 || kind == WordKind)
            {
                return CadLabelClass.Words;
            }

            if (kind == DimensionKind)
            {
                return CadLabelClass.Dimensions;
            }

            return kind == HatchKind ? CadLabelClass.Hatches : CadLabelClass.Lines;
        }
    }
}
