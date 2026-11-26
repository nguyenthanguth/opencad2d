using System;
using System.Collections.Generic;
using System.Runtime.CompilerServices;

namespace OpenCAD2D
{
    /// <summary>
    /// What <see cref="CadSpatial"/> asks of one display list, made once for it: the box of each entity as it is drawn, and its
    /// leaves, the pieces overlaps are tested on (a piece of text with its frame, a chain, some segments, an arc, a fill), with
    /// the R-tree over their boxes. A list is not changed after it is made (an edit makes another), so the index lasts as long
    /// as the list does and a program that asks again after each edit pays for the new list alone.
    /// </summary>
    internal sealed class CadSpatialIndex
    {
        internal const byte TextLeaf = 0;
        internal const byte ChainLeaf = 1;
        internal const byte SegmentsLeaf = 2;
        internal const byte ArcLeaf = 3;
        internal const byte FillLeaf = 4;

        private static readonly ConditionalWeakTable<DisplayList2, CadSpatialIndex> Kept =
            new ConditionalWeakTable<DisplayList2, CadSpatialIndex>();

        // The entities in the order the list first draws them, by handle: looked up only, never enumerated.
        private readonly Dictionary<string, int> _slots = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
        private readonly List<string> _handles = new List<string>();
        private double[] _handleBox = new double[64];

        private readonly List<DisplayItem2> _items = new List<DisplayItem2>();
        private readonly List<int> _leafHandle = new List<int>();
        private readonly List<byte> _leafKind = new List<byte>();
        private readonly List<int> _leafFrame = new List<int>();
        private readonly List<double> _leafBox = new List<double>();
        private readonly List<CadTextFrame> _frames = new List<CadTextFrame>();

        // The TrueType files the words are measured in, by font and cut, found once for the list.
        private readonly Dictionary<string, CadFontFace> _faces = new Dictionary<string, CadFontFace>(
            StringComparer.OrdinalIgnoreCase
        );

        private string[] _kinds;

        /// <summary>Gets the index of a list, made on the first call.</summary>
        public static CadSpatialIndex Of(DisplayList2 list) => Kept.GetValue(list, made => new CadSpatialIndex(made));

        private CadSpatialIndex(DisplayList2 list)
        {
            IReadOnlyList<DisplayItem2> items = list.Items;
            int count = list.Count;

            for (int i = 0; i < count; i++)
            {
                DisplayItem2 item = items[i];
                string handle = item.Handle;

                if (string.IsNullOrEmpty(handle))
                {
                    continue;
                }

                int slot = SlotOf(handle);
                DisplayBox drawn;

                if (item is DisplayText2 text)
                {
                    drawn = AddText(text, slot, true);
                }
                else if (item is DisplayRay2 ray)
                {
                    // A line without end counts by its base point, as the list's box takes it.
                    drawn = DisplayBox.Empty.Add(ray.X, ray.Y);
                }
                else
                {
                    drawn = item.VisibleBounds;
                    AddCurve(item, slot);
                }

                if (!drawn.IsEmpty)
                {
                    Grow(slot, drawn);
                }
            }

            LeafCount = _leafHandle.Count;
            LeafHandle = _leafHandle.ToArray();
            LeafKind = _leafKind.ToArray();
            LeafFrame = _leafFrame.ToArray();
            LeafBox = _leafBox.ToArray();
            Frames = _frames.ToArray();
            LeafItem = _items.ToArray();
            HandleCount = _handles.Count;
            Handles = _handles.ToArray();
            HandleBox = _handleBox;
            Tree = new CadRTree(LeafBox, LeafCount);
        }

        /// <summary>Gets the number of entities the list draws, by handle.</summary>
        public int HandleCount { get; }

        /// <summary>Gets the handles in the order the list first draws them.</summary>
        public string[] Handles { get; }

        /// <summary>Gets the box of each entity, four numbers each; an entity that draws nothing has an empty box (its least above its greatest).</summary>
        public double[] HandleBox { get; }

        /// <summary>Gets the number of leaves.</summary>
        public int LeafCount { get; }

        /// <summary>Gets the handle slot of each leaf.</summary>
        public int[] LeafHandle { get; }

        /// <summary>Gets what each leaf is: <see cref="TextLeaf"/>, <see cref="ChainLeaf"/> and the others.</summary>
        public byte[] LeafKind { get; }

        /// <summary>Gets the number of the frame of each leaf of text in <see cref="Frames"/>; -1 for the others.</summary>
        public int[] LeafFrame { get; }

        /// <summary>Gets the box of each leaf, four numbers each.</summary>
        public double[] LeafBox { get; }

        /// <summary>Gets the display item each leaf is, or is a part of.</summary>
        public DisplayItem2[] LeafItem { get; }

        /// <summary>Gets the frames of the words.</summary>
        public CadTextFrame[] Frames { get; }

        /// <summary>Gets the tree over the boxes of the leaves.</summary>
        public CadRTree Tree { get; }

        /// <summary>Gets the number of the entity of a handle, or -1 where the list draws none of that handle.</summary>
        public int Find(string handle) => handle != null && _slots.TryGetValue(handle, out int slot) ? slot : -1;

        /// <summary>
        /// Gets the DXF name of the entity of each handle, found once by the document: null for a handle the document no longer
        /// has (an entity an edit erased after the list was made cannot be, for an edit makes another list).
        /// </summary>
        public string[] KindsOf(CadDocument document)
        {
            if (_kinds == null)
            {
                var kinds = new string[HandleCount];

                for (int i = 0; i < kinds.Length; i++)
                {
                    kinds[i] = document.FindEntity(Handles[i])?.DxfName;
                }

                _kinds = kinds;
            }

            return _kinds;
        }

        private int SlotOf(string handle)
        {
            if (!_slots.TryGetValue(handle, out int slot))
            {
                slot = _handles.Count;
                _slots[handle] = slot;
                _handles.Add(handle);

                if (4 * (slot + 1) > _handleBox.Length)
                {
                    Array.Resize(ref _handleBox, _handleBox.Length * 2);
                }

                _handleBox[4 * slot] = double.PositiveInfinity;
                _handleBox[4 * slot + 1] = double.PositiveInfinity;
                _handleBox[4 * slot + 2] = double.NegativeInfinity;
                _handleBox[4 * slot + 3] = double.NegativeInfinity;
            }

            return slot;
        }

        private void Grow(int slot, DisplayBox box)
        {
            _handleBox[4 * slot] = Math.Min(_handleBox[4 * slot], box.MinX);
            _handleBox[4 * slot + 1] = Math.Min(_handleBox[4 * slot + 1], box.MinY);
            _handleBox[4 * slot + 2] = Math.Max(_handleBox[4 * slot + 2], box.MaxX);
            _handleBox[4 * slot + 3] = Math.Max(_handleBox[4 * slot + 3], box.MaxY);
        }

        /// <summary>
        /// Takes in a piece of text and returns the box it is drawn in. A text made of parts (an MTEXT) gives its parts, one
        /// leaf each, and its own box, that of the parts; a TrueType text outside an MTEXT is measured by its font's letters
        /// (<see cref="Measured"/>) where the display gave it a width of 0.6 of its height a letter.
        /// </summary>
        private DisplayBox AddText(DisplayText2 text, int slot, bool top)
        {
            IReadOnlyList<DisplayItem2> parts = text.Parts;

            if (parts != null)
            {
                foreach (DisplayItem2 part in parts)
                {
                    if (part is DisplayText2 word)
                    {
                        AddText(word, slot, false);
                    }
                    else
                    {
                        AddCurve(part, slot);
                    }
                }

                return text.VisibleBounds;
            }

            DisplayText2 shown = top ? Measured(text) : text;
            DisplayBox box = shown.VisibleBounds;

            if (!string.IsNullOrWhiteSpace(text.Text))
            {
                CadTextFrame frame = CadTextFrame.Of(shown);

                if (frame.IsUsable)
                {
                    _items.Add(text);
                    _leafHandle.Add(slot);
                    _leafKind.Add(TextLeaf);
                    _leafFrame.Add(_frames.Count);
                    _frames.Add(frame);
                    AddBox(frame.Box());
                }
            }

            return box;
        }

        private void AddCurve(DisplayItem2 item, int slot)
        {
            byte kind;

            if (item is DisplayPolyline2)
            {
                kind = ChainLeaf;
            }
            else if (item is DisplaySegments2)
            {
                kind = SegmentsLeaf;
            }
            else if (item is DisplayArc2)
            {
                kind = ArcLeaf;
            }
            else if (item is DisplayFill2)
            {
                kind = FillLeaf;
            }
            else
            {
                return;
            }

            DisplayBox box = item.VisibleBounds;

            if (box.IsEmpty)
            {
                return;
            }

            _items.Add(item);
            _leafHandle.Add(slot);
            _leafKind.Add(kind);
            _leafFrame.Add(-1);
            AddBox(box);
        }

        private void AddBox(DisplayBox box)
        {
            _leafBox.Add(box.MinX);
            _leafBox.Add(box.MinY);
            _leafBox.Add(box.MaxX);
            _leafBox.Add(box.MaxY);
        }

        /// <summary>
        /// The text as the canvas will draw it where its font is a TrueType file the core reads: the width the letters' advances
        /// add up to (the longest line, at the text's height and width factor, as an MTEXT run is measured, <see cref="CadTextInk"/>)
        /// and the depth the font's descenders reach, in place of the display's guess of 0.6 of the height a letter and none.
        /// A text of an SHX font, whose strokes the display measured, one with parts and one whose font has no file is as it is.
        /// </summary>
        private DisplayText2 Measured(DisplayText2 text)
        {
            if (text.Strokes != null || string.IsNullOrEmpty(text.FontName) || !(text.Height > 0.0))
            {
                return text;
            }

            string key = text.FontName;

            if (!_faces.TryGetValue(key, out CadFontFace face))
            {
                face = new CadFontFace(text.FontName, null, null, null);
                _faces[key] = face;
            }

            CadTextInk ink = CadTextInk.Of(face, text.IsBold, text.IsItalic);
            GeometryHelper.Dxf.DxfTrueTypeFont file = ink.TrueType;

            if (file == null || !(file.Above > 0.0))
            {
                return text;
            }

            string words = text.Text ?? string.Empty;
            double widest = 0.0;
            double line = 0.0;
            int at = 0;

            while (at < words.Length)
            {
                if (words[at] == '\n')
                {
                    widest = Math.Max(widest, line);
                    line = 0.0;
                    at++;
                }
                else
                {
                    line += ink.Next(words, ref at).Advance;
                }
            }

            widest = Math.Max(widest, line);
            double below = double.IsNaN(file.Below) ? 0.0 : file.Below;
            var measured = new DisplayText2(
                text.Text,
                text.X,
                text.Y,
                text.Height,
                text.Rotation,
                text.WidthFactor,
                text.Horizontal,
                text.Vertical,
                text.Style,
                text.Handle,
                text.Layer,
                text.FontName,
                text.Oblique,
                text.IsBackward
            )
            {
                IsBold = text.IsBold,
                IsItalic = text.IsItalic,
                IsQuick = text.IsQuick,
                IsField = text.IsField,
            };
            measured.BlockClip = text.BlockClip;
            measured.SetMeasure(widest * text.Height * text.WidthFactor, text.Height * below / file.Above);
            return measured;
        }
    }
}
