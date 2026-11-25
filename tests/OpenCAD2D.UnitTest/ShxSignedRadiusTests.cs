using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using Xunit;

namespace OpenCAD2D.UnitTest
{
    /// <summary>
    /// The radius byte of an SHX octant arc (code 10) is a signed byte, as AutoCAD 2021 reads it: from 128 on it is negative
    /// and the arc is reflected through the pen. The font is made here 100 above its baseline, so that a unit of the font is
    /// a unit of the glyph, and the numbers are those AutoCAD's TEXTBOX gives at a height of 100.
    /// </summary>
    public sealed class ShxSignedRadiusTests
    {
        /// <summary>A unifont whose letter A is a quarter arc (start octant 0, two octants, counter-clockwise) of the radius byte given, then the tail given.</summary>
        private static ShxFont Font(byte radius, byte octant, params byte[] tail)
        {
            byte[] shape = new byte[] { 1, 10, radius, octant }
                .Concat(tail)
                .Concat(new byte[] { 0 })
                .ToArray();
            var bytes = new List<byte>(Encoding.ASCII.GetBytes("AutoCAD-86 unifont 1.0\r\n\x1A"));
            byte[] info = Encoding.ASCII.GetBytes("RAD\0").Concat(new byte[] { 100, 2, 0, 0, 0, 0 }).ToArray();
            U16(bytes, 2);
            U16(bytes, 0);
            U16(bytes, info.Length);
            bytes.AddRange(info);
            byte[] record = new byte[] { (byte)'n', 0 }
                .Concat(shape)
                .ToArray();
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

        /// <summary>The end of the last stroke and the box of the ink as TEXTBOX measures it: the strokes and the reach of the arcs.</summary>
        private static double[] Measure(ShxGlyph glyph)
        {
            double[] last = glyph.Strokes[glyph.Strokes.Count - 1];
            var xs = new List<double>();
            var ys = new List<double>();

            foreach (double[] stroke in glyph.Strokes)
            {
                for (int p = 0; p + 1 < stroke.Length; p += 2)
                {
                    xs.Add(stroke[p]);
                    ys.Add(stroke[p + 1]);
                }
            }

            for (int p = 0; p + 1 < glyph.Reach.Count; p += 2)
            {
                xs.Add(glyph.Reach[p]);
                ys.Add(glyph.Reach[p + 1]);
            }

            return new[] { last[last.Length - 2], last[last.Length - 1], xs.Min(), ys.Min(), xs.Max(), ys.Max() };
        }

        private static readonly byte[] Landing = { 8, 100, 0, 8, 0, 100 };

        [Fact]
        public void ARadiusByteOf200_IsMinus56_AndTheArcIsReflectedThroughThePen()
        {
            // AutoCAD 2021: the glyph [1, 10, 200, 0x02] ends at (+56, -56) with the box -11.598 -67.598 | 56 0.
            double[] m = Measure(Font(200, 0x02).GlyphOf('A'));
            Assert.Equal(56.0, m[0], 3);
            Assert.Equal(-56.0, m[1], 3);
            Assert.Equal(-11.598, m[2], 3);
            Assert.Equal(-67.598, m[3], 3);
            Assert.Equal(56.0, m[4], 3);
            Assert.Equal(0.0, m[5], 3);
        }

        [Theory]
        [InlineData(127, -127.0, 127.0)]
        [InlineData(128, 128.0, -128.0)]
        [InlineData(200, 56.0, -56.0)]
        [InlineData(255, 1.0, -1.0)]
        public void ARadiusByte_EndsWhereAutoCadsLandingTailLeavesIt(byte radius, double x, double y)
        {
            double[] m = Measure(Font(radius, 0x02, Landing).GlyphOf('A'));
            Assert.Equal(x + 100.0, m[0], 3);
            Assert.Equal(y + 100.0, m[1], 3);
        }

        [Theory]
        [InlineData(128, -26.5097, -154.5097, 228.0, 0.0)]
        [InlineData(200, -11.598, -67.598, 156.0, 44.0)]
        [InlineData(255, -0.2071, -1.2071, 101.0, 99.0)]
        public void ARadiusByteOf128OrMore_HasTheInkBoxAutoCadMeasures(
            byte radius,
            double left,
            double bottom,
            double right,
            double top
        )
        {
            double[] m = Measure(Font(radius, 0x02, Landing).GlyphOf('A'));
            Assert.Equal(left, m[2], 3);
            Assert.Equal(bottom, m[3], 3);
            Assert.Equal(right, m[4], 3);
            Assert.Equal(top, m[5], 3);
        }

        [Fact]
        public void AWholeCircleOfARadiusByteOf200_IsReflectedThroughThePen_ToTheRightOfIt()
        {
            // Radius -56 about a centre 56 to the right of the pen: the circle spans 0 to 112 across and -56 to 56 up.
            double[] m = Measure(Font(200, 0x00).GlyphOf('A'));
            Assert.Equal(0.0, m[2], 3);
            Assert.Equal(-56.0, m[3], 3);
            Assert.Equal(112.0, m[4], 3);
            Assert.Equal(56.0, m[5], 3);
        }
    }
}
