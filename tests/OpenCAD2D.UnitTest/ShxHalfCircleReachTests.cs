using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using Xunit;

namespace OpenCAD2D.UnitTest
{
    /// <summary>
    /// How AutoCAD 2021 measures the ink of a half circle drawn by a code 12 bulge arc of exactly 180 degrees (bulge 127 or
    /// -127): by the rectangle of its chord reaching to the arc's side, wherever the arc starts, and never by the square of
    /// its circle, which is how it measures an octant arc (code 10) of a half circle that starts above the X axis. The numbers
    /// are those of AutoCAD 2021 (accoreconsole, the probe of 2026-11-25 with opencad2d.shx and a one-glyph unifont).
    /// </summary>
    public sealed class ShxHalfCircleReachTests
    {
        /// <summary>A unifont 100 above and 30 below of one letter, A, whose codes are those given and whose name is empty.</summary>
        private static ShxFont OneGlyph(params byte[] codes)
        {
            var bytes = new List<byte>(Encoding.ASCII.GetBytes("AutoCAD-86 unifont 1.0\r\n\x1A"));
            byte[] info = Encoding.ASCII.GetBytes("TEST\0").Concat(new byte[] { 100, 30, 0, 0, 0, 0 }).ToArray();
            byte[] record = new byte[] { 0 }
                .Concat(codes)
                .ToArray();
            U16(bytes, 2);
            U16(bytes, 0);
            U16(bytes, info.Length);
            bytes.AddRange(info);
            U16(bytes, 'A');
            U16(bytes, record.Length);
            bytes.AddRange(record);
            return ShxFont.Read(bytes.ToArray());
        }

        private static void U16(List<byte> bytes, int value)
        {
            bytes.Add((byte)(value & 0xFF));
            bytes.Add((byte)((value >> 8) & 0xFF));
        }

        /// <summary>The box AutoCAD measures a glyph's ink in: its strokes and the points its arcs reach, as left, bottom, right, top.</summary>
        private static double[] Ink(ShxGlyph glyph)
        {
            var xs = new List<double>();
            var ys = new List<double>();

            foreach (double[] stroke in glyph.Strokes)
            {
                for (int i = 0; i + 1 < stroke.Length; i += 2)
                {
                    xs.Add(stroke[i]);
                    ys.Add(stroke[i + 1]);
                }
            }

            for (int i = 0; i + 1 < glyph.Reach.Count; i += 2)
            {
                xs.Add(glyph.Reach[i]);
                ys.Add(glyph.Reach[i + 1]);
            }

            return new[] { xs.Min(), ys.Min(), xs.Max(), ys.Max() };
        }

        /// <summary>The box to the three decimals AutoCAD prints.</summary>
        private static void Box(double left, double bottom, double right, double top, double[] box)
        {
            Assert.Equal(left, box[0], 3);
            Assert.Equal(bottom, box[1], 3);
            Assert.Equal(right, box[2], 3);
            Assert.Equal(top, box[3], 3);
        }

        [Fact]
        public void ACode12HalfCircleStartingAboveTheXAxis_IsMeasuredByTheRectangleOfItsChord()
        {
            // 1 12 40 -30 127 0: from (0, 0) to (40, -30), a chord of 50, the circle about (20, -15) of radius 25; the pen
            // stands at 143 degrees on it, above the X axis, where the octant rule would measure the square (-5, -40) to
            // (45, 10). AutoCAD: the chord's rectangle bulging to the right of the chord, (-15, -50) to (40, 0).
            ShxFont font = OneGlyph(1, 12, 40, unchecked((byte)-30), 127, 0);

            Box(-15, -50, 40, 0, Ink(font.GlyphOf('A')));
        }

        [Fact]
        public void ACode12HalfCircleTheOtherWay_IsMeasuredByTheRectangleOnTheOtherSideOfItsChord()
        {
            // The same chord with a bulge of -127, clockwise: the arc bulges to the left of the chord, to the corners (0, 0),
            // (40, -30), (15, 20) and (55, -10), the left normal (0.6, 0.8) of the chord times the radius 25 away from it.
            ShxFont font = OneGlyph(1, 12, 40, unchecked((byte)-30), unchecked((byte)-127), 0);

            Box(0, -30, 55, 20, Ink(font.GlyphOf('A')));
        }

        [Fact]
        public void TheShippedAmpersandAt100High_HasTheBoxAutoCadMeasuresIt()
        {
            // AutoCAD 2021's (textbox) of & at 100 high in opencad2d.shx: (0, -10) to (70, 103.685). The reader gave
            // (10, 0) to (70, 103.685) while it measured the half circle of the bowl by its square.
            ShxGlyph glyph = CadFonts.BuiltIn.GlyphOf('&');

            Box(0, -10, 70, 103.685, Ink(glyph));
        }
    }
}
