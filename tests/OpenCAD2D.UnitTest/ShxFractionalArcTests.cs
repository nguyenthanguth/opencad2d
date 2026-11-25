using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using Xunit;

namespace OpenCAD2D.UnitTest
{
    /// <summary>
    /// A fractional arc of an SHX font (shape code 11: start offset, end offset, high radius byte, low radius byte, octant
    /// byte) as AutoCAD 2021 draws it. The font is made here 100 above its baseline, so that a unit of the font is a unit of
    /// the glyph, and every number is what AutoCAD's TEXTBOX gave at a height of 100 for the same bytes: the end of the
    /// glyph's last stroke past a tail of 2540 right and 2540 up (where the arc left the pen), and the box of its ink. An arc
    /// is of radius 100 (high byte 0, low byte 100) unless a test says otherwise.
    /// </summary>
    public sealed class ShxFractionalArcTests
    {
        /// <summary>A unifont whose letter A is the fractional arc of the bytes given, then the tail given.</summary>
        private static ShxFont Font(byte start, byte end, byte high, byte low, byte octant, params byte[] tail)
        {
            byte[] shape = new byte[] { 1, 11, start, end, high, low, octant }
                .Concat(tail)
                .Concat(new byte[] { 0 })
                .ToArray();
            var bytes = new List<byte>(Encoding.ASCII.GetBytes("AutoCAD-86 unifont 1.0\r\n\x1A"));
            byte[] info = Encoding.ASCII.GetBytes("ARC\0").Concat(new byte[] { 100, 30, 0, 0, 0, 0 }).ToArray();
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

            // A glyph that draws nothing has no stroke: the pen's own place, as TEXTBOX gives 0 0 0 0 for it.
            if (glyph.Strokes.Count == 0)
            {
                return new[] { 0.0, 0.0, xs.Min(), ys.Min(), xs.Max(), ys.Max() };
            }

            double[] last = glyph.Strokes[glyph.Strokes.Count - 1];
            return new[] { last[last.Length - 2], last[last.Length - 1], xs.Min(), ys.Min(), xs.Max(), ys.Max() };
        }

        /// <summary>A tail of 20 moves of 127 to the right then 20 of 127 up: it starts where the arc left the pen, so its end is 2540 right and up of that.</summary>
        private static readonly byte[] Tail = Enumerable
            .Repeat(new byte[] { 8, 127, 0 }, 20)
            .Concat(Enumerable.Repeat(new byte[] { 8, 0, 127 }, 20))
            .SelectMany(move => move)
            .ToArray();

        private const double TailLength = 2540.0;

        private static void AssertLands(byte start, byte end, byte high, byte low, byte octant, double x, double y)
        {
            double[] m = Measure(Font(start, end, high, low, octant, Tail).GlyphOf('A'));
            Assert.Equal(x, m[0] - TailLength, 3);
            Assert.Equal(y, m[1] - TailLength, 3);
        }

        private static void AssertInk(
            byte start,
            byte end,
            byte high,
            byte low,
            byte octant,
            double left,
            double bottom,
            double right,
            double top
        )
        {
            double[] m = Measure(Font(start, end, high, low, octant).GlyphOf('A'));
            Assert.Equal(left, m[2], 3);
            Assert.Equal(bottom, m[3], 3);
            Assert.Equal(right, m[4], 3);
            Assert.Equal(top, m[5], 3);
        }

        /// <summary>
        /// AutoCAD reads the two radius bytes as one unsigned number of 16 bits, <c>(high &lt;&lt; 8) + low</c>: a high byte of
        /// 0x80 or more is not a sign, as the radius byte of code 10 is. A code 10 radius byte of 200 is -56 and reflects the
        /// arc through the pen; here <c>00 C8</c> is 200 and <c>FF C8</c> is 65480, both turning left of the pen. The quarter
        /// arc (octant byte 0x02) of radius r from the pen at the origin has ink from -r to a little right of the pen across.
        /// </summary>
        [Theory]
        [InlineData(0x00, 0xC8, -200.0, 0.0, 41.421, 241.421)]
        [InlineData(0x01, 0x00, -256.0, 0.0, 53.019, 309.019)]
        [InlineData(0x01, 0x2C, -300.0, 0.0, 62.132, 362.132)]
        [InlineData(0x01, 0xC8, -456.0, 0.0, 94.441, 550.441)]
        [InlineData(0x80, 0x01, -32769.0, 0.0, 6786.682, 39555.682)]
        [InlineData(0xFE, 0x00, -65024.0, 0.0, 13466.911, 78490.911)]
        [InlineData(0xFF, 0x00, -65280.0, 0.0, 13519.931, 78799.931)]
        [InlineData(0xFF, 0xC8, -65480.0, 0.0, 13561.352, 79041.352)]
        public void ARadiusOfTwoBytes_IsReadUnsigned_AndTheQuarterArcTurnsLeftOfThePen(
            byte high,
            byte low,
            double left,
            double bottom,
            double right,
            double top
        )
        {
            AssertInk(0, 0, high, low, 0x02, left, bottom, right, top);
        }

        /// <summary>The quarter arc of a radius of 200 to 456 lands that far to the left of the pen and as far up: the high byte weighs 256.</summary>
        [Theory]
        [InlineData(0x00, 0xC8, -200.0, 200.0)]
        [InlineData(0x01, 0x00, -256.0, 256.0)]
        [InlineData(0x01, 0x2C, -300.0, 300.0)]
        [InlineData(0x01, 0xC8, -456.0, 456.0)]
        public void ARadiusOfTwoBytes_LandsItsQuarterArcThatFarToTheLeftAndUp(byte high, byte low, double x, double y)
        {
            AssertLands(0, 0, high, low, 0x02, x, y);
        }

        /// <summary>
        /// The start offset moves the start by offset / 256 of an octant (45 degrees), the end offset the end the same way, and
        /// an end offset of 0 is the whole octant. The octant byte 0x01 is one octant from octant 0, 0x02 two, 0x23 three from
        /// octant 2, 0x72 two from octant 7 (round past 360); counter-clockwise, and 0x81 to 0xF2 the same turning clockwise.
        /// An offset of 64 is 11.25 degrees and 11, one of 255 is 44.8 and 45: neither near a half degree.
        /// </summary>
        [Theory]
        [InlineData(0, 0, 0x01, -29.289, 70.711)]
        [InlineData(64, 0, 0x01, -27.452, 51.63)]
        [InlineData(0, 64, 0x01, -1.837, 19.081)]
        [InlineData(0, 255, 0x01, -29.289, 70.711)]
        [InlineData(0, 0, 0x02, -100.0, 100.0)]
        [InlineData(64, 0, 0x02, -98.163, 80.919)]
        [InlineData(0, 64, 0x02, -44.081, 82.904)]
        [InlineData(0, 255, 0x02, -100.0, 100.0)]
        [InlineData(0, 0, 0x23, -70.711, -170.711)]
        [InlineData(64, 0, 0x23, -51.63, -168.873)]
        [InlineData(0, 64, 0x23, -98.163, -119.081)]
        [InlineData(0, 255, 0x23, -70.711, -170.711)]
        [InlineData(0, 0, 0x72, 0.0, 141.421)]
        [InlineData(64, 0, 0x72, -12.193, 126.63)]
        [InlineData(0, 64, 0x72, 27.452, 89.792)]
        [InlineData(0, 255, 0x72, 0.0, 141.421)]
        [InlineData(0, 0, 0x81, -29.289, -70.711)]
        [InlineData(64, 0, 0x81, -27.452, -51.63)]
        [InlineData(0, 64, 0x81, -1.837, -19.081)]
        [InlineData(0, 255, 0x81, -29.289, -70.711)]
        [InlineData(0, 0, 0x82, -100.0, -100.0)]
        [InlineData(64, 0, 0x82, -98.163, -80.919)]
        [InlineData(0, 64, 0x82, -44.081, -82.904)]
        [InlineData(0, 255, 0x82, -100.0, -100.0)]
        [InlineData(0, 0, 0xA3, 70.711, -170.711)]
        [InlineData(64, 0, 0xA3, 51.63, -168.873)]
        [InlineData(0, 64, 0xA3, 98.163, -119.081)]
        [InlineData(0, 255, 0xA3, 70.711, -170.711)]
        [InlineData(0, 0, 0xF2, -141.421, 0.0)]
        [InlineData(64, 0, 0xF2, -126.63, 12.193)]
        [InlineData(0, 64, 0xF2, -89.792, -27.452)]
        [InlineData(0, 255, 0xF2, -141.421, 0.0)]
        public void AnOffsetPair_LandsWhereAutoCadLeavesThePen(byte start, byte end, byte octant, double x, double y)
        {
            AssertLands(start, end, 0, 100, octant, x, y);
        }

        /// <summary>
        /// An offset is rounded to whole degrees, half up: 128 / 256 of 45 is 22.5 and is 23, 127 is 22.3 and 126 is 22.1 and
        /// are 22, 129 is 22.7 and 130 is 22.8 and are 23; and the same turning clockwise (-23, where rounding to even gives -22).
        /// Octant byte 0x01 is the one octant counter-clockwise, 0x81 clockwise, 0x23 and 0xA3 three octants from octant 2.
        /// </summary>
        [Theory]
        [InlineData(126, 0, 0x01, -22.008, 33.25)]
        [InlineData(127, 0, 0x01, -22.008, 33.25)]
        [InlineData(128, 0, 0x01, -21.34, 31.638)]
        [InlineData(129, 0, 0x01, -21.34, 31.638)]
        [InlineData(130, 0, 0x01, -21.34, 31.638)]
        [InlineData(0, 126, 0x01, -7.282, 37.461)]
        [InlineData(0, 127, 0x01, -7.282, 37.461)]
        [InlineData(0, 128, 0x01, -7.95, 39.073)]
        [InlineData(0, 129, 0x01, -7.95, 39.073)]
        [InlineData(0, 130, 0x01, -7.95, 39.073)]
        [InlineData(64, 128, 0x01, -6.112, 19.992)]
        [InlineData(128, 64, 0x01, 6.112, -19.992)]
        [InlineData(126, 0, 0x81, -22.008, -33.25)]
        [InlineData(127, 0, 0x81, -22.008, -33.25)]
        [InlineData(128, 0, 0x81, -21.34, -31.638)]
        [InlineData(129, 0, 0x81, -21.34, -31.638)]
        [InlineData(130, 0, 0x81, -21.34, -31.638)]
        [InlineData(0, 126, 0x81, -7.282, -37.461)]
        [InlineData(0, 127, 0x81, -7.282, -37.461)]
        [InlineData(0, 128, 0x81, -7.95, -39.073)]
        [InlineData(0, 129, 0x81, -7.95, -39.073)]
        [InlineData(0, 130, 0x81, -7.95, -39.073)]
        [InlineData(64, 128, 0x81, -6.112, -19.992)]
        [InlineData(128, 64, 0x81, 6.112, 19.992)]
        [InlineData(126, 0, 0x23, -33.25, -163.429)]
        [InlineData(127, 0, 0x23, -33.25, -163.429)]
        [InlineData(128, 0, 0x23, -31.638, -162.761)]
        [InlineData(129, 0, 0x23, -31.638, -162.761)]
        [InlineData(130, 0, 0x23, -31.638, -162.761)]
        [InlineData(0, 126, 0x23, -92.718, -137.461)]
        [InlineData(0, 127, 0x23, -92.718, -137.461)]
        [InlineData(0, 128, 0x23, -92.05, -139.073)]
        [InlineData(0, 129, 0x23, -92.05, -139.073)]
        [InlineData(0, 130, 0x23, -92.05, -139.073)]
        [InlineData(64, 128, 0x23, -72.97, -137.236)]
        [InlineData(128, 64, 0x23, -59.09, -111.131)]
        [InlineData(126, 0, 0xA3, 33.25, -163.429)]
        [InlineData(127, 0, 0xA3, 33.25, -163.429)]
        [InlineData(128, 0, 0xA3, 31.638, -162.761)]
        [InlineData(129, 0, 0xA3, 31.638, -162.761)]
        [InlineData(130, 0, 0xA3, 31.638, -162.761)]
        [InlineData(0, 126, 0xA3, 92.718, -137.461)]
        [InlineData(0, 127, 0xA3, 92.718, -137.461)]
        [InlineData(0, 128, 0xA3, 92.05, -139.073)]
        [InlineData(0, 129, 0xA3, 92.05, -139.073)]
        [InlineData(0, 130, 0xA3, 92.05, -139.073)]
        [InlineData(64, 128, 0xA3, 72.97, -137.236)]
        [InlineData(128, 64, 0xA3, 59.09, -111.131)]
        public void AnOffsetOf128_IsRoundedToWholeDegreesHalfUp_BetweenThe22OfItsNeighboursAndThe23(
            byte start,
            byte end,
            byte octant,
            double x,
            double y
        )
        {
            AssertLands(start, end, 0, 100, octant, x, y);
        }

        /// <summary>
        /// An end offset that rounds to 0 degrees (1 is 0.18, 2 is 0.35) is not an end at the octant's start but the whole octant,
        /// as an offset of 0 is; 3 (0.53) and 5 (0.88) are 1 degree, an arc of 1 degree from the start and no more.
        /// </summary>
        [Theory]
        [InlineData(0, 1, 0x01, -29.289, 70.711)]
        [InlineData(0, 2, 0x01, -29.289, 70.711)]
        [InlineData(1, 1, 0x01, -29.289, 70.711)]
        [InlineData(2, 2, 0x01, -29.289, 70.711)]
        [InlineData(0, 3, 0x01, -0.015, 1.745)]
        [InlineData(0, 5, 0x01, -0.015, 1.745)]
        [InlineData(0, 1, 0x81, -29.289, -70.711)]
        [InlineData(0, 2, 0x81, -29.289, -70.711)]
        [InlineData(1, 1, 0x81, -29.289, -70.711)]
        [InlineData(2, 2, 0x81, -29.289, -70.711)]
        [InlineData(0, 3, 0x81, -0.015, -1.745)]
        [InlineData(0, 5, 0x81, -0.015, -1.745)]
        [InlineData(1, 1, 0x02, -100.0, 100.0)]
        [InlineData(1, 1, 0x23, -70.711, -170.711)]
        [InlineData(1, 1, 0x72, 0.0, 141.421)]
        [InlineData(1, 1, 0xA3, 70.711, -170.711)]
        public void AnEndOffsetThatRoundsToZeroDegrees_IsTheWholeOctant(
            byte start,
            byte end,
            byte octant,
            double x,
            double y
        )
        {
            AssertLands(start, end, 0, 100, octant, x, y);
        }

        /// <summary>
        /// An end before the start (start 200 is 35 degrees, end 100 is 18; start 128 is 23, end 64 is 11) turns the way the
        /// octant byte says, counter-clockwise for 0x01, the long way: 343 degrees where the short way back is 17 clockwise. The
        /// two end at one place; the ink differs, a circle nearly whole against a sliver.
        /// </summary>
        [Theory]
        [InlineData(200, 100, 0x01, -181.915, -157.358, 18.085, 42.642)]
        [InlineData(128, 64, 0x01, -192.05, -139.073, 7.95, 60.927)]
        [InlineData(200, 100, 0x81, -181.915, -42.642, 18.085, 157.358)]
        [InlineData(128, 64, 0x81, -192.05, -60.927, 7.95, 139.073)]
        public void AnEndBeforeTheStart_TurnsTheLongWayAroundInTheOctantBytesSense(
            byte start,
            byte end,
            byte octant,
            double left,
            double bottom,
            double right,
            double top
        )
        {
            AssertInk(start, end, 0, 100, octant, left, bottom, right, top);
        }

        /// <summary>
        /// A start and an end at the same whole degree are a whole circle, drawn from the pen round to the pen: 100 and 100 are
        /// both 18 degrees, 3 and 4 both 1, 255 and 255 and 254 and 255 all 45. The circle's centre is 100 from the pen, so its
        /// ink is the square about the centre, and the pen lands where it began.
        /// </summary>
        [Theory]
        [InlineData(100, 100, 0x01, -195.106, -130.902, 4.894, 69.098)]
        [InlineData(3, 3, 0x01, -199.985, -101.745, 0.015, 98.255)]
        [InlineData(3, 4, 0x01, -199.985, -101.745, 0.015, 98.255)]
        [InlineData(255, 255, 0x01, -170.711, -170.711, 29.289, 29.289)]
        [InlineData(254, 255, 0x01, -170.711, -170.711, 29.289, 29.289)]
        [InlineData(255, 255, 0x81, -170.711, -29.289, 29.289, 170.711)]
        [InlineData(100, 100, 0x81, -195.106, -69.098, 4.894, 130.902)]
        public void AStartAndEndAtTheSameWholeDegree_AreAWholeCircle(
            byte start,
            byte end,
            byte octant,
            double left,
            double bottom,
            double right,
            double top
        )
        {
            AssertInk(start, end, 0, 100, octant, left, bottom, right, top);
            AssertLands(start, end, 0, 100, octant, 0.0, 0.0);
        }

        /// <summary>
        /// An odd corner AutoCAD has: a start of 255 (45 degrees) and an end of 0 (the octant's end, also 45) of one octant is no
        /// arc when it turns counter-clockwise, and the whole circle when it turns clockwise. The pen lands where it began either
        /// way.
        /// </summary>
        [Fact]
        public void AStartOf255AndAnEndOf0_DrawNothingCounterClockwise_ButTheWholeCircleClockwise()
        {
            AssertLands(255, 0, 0, 100, 0x01, 0.0, 0.0);
            AssertInk(255, 0, 0, 100, 0x01, 0.0, 0.0, 0.0, 0.0);
            AssertLands(255, 0, 0, 100, 0x81, 0.0, 0.0);
            AssertInk(255, 0, 0, 100, 0x81, -170.711, -29.289, 29.289, 170.711);
        }

        /// <summary>
        /// An octant byte of no octants (low nibble 0) is 8, a circle from its start octant: 0x00 is the whole circle, 0x80 the
        /// same turning clockwise, 0x20 and 0xA0 the whole circle from octant 2. A start offset of 64 (11 degrees) of 0x00 goes
        /// 349 degrees round to 360, where the short arc back 11 degrees would be 19 high; an end offset of 64 (11 degrees of the
        /// eighth octant) stops at 326 degrees, and a start of 128 (23) goes 337 round.
        /// </summary>
        [Theory]
        [InlineData(0, 0, 0x00, -200.0, -100.0, 0.0, 100.0)]
        [InlineData(64, 0, 0x00, -198.163, -119.081, 1.837, 80.919)]
        [InlineData(64, 0, 0x80, -198.163, -80.919, 1.837, 119.081)]
        [InlineData(0, 0, 0x20, -100.0, -200.0, 100.0, 0.0)]
        [InlineData(0, 0, 0xA0, -100.0, -200.0, 100.0, 0.0)]
        [InlineData(0, 64, 0x00, -200.0, -100.0, 0.0, 100.0)]
        [InlineData(128, 0, 0x00, -192.05, -139.073, 7.95, 60.927)]
        public void AnOctantByteOfNoOctants_IsAWholeCircleFromItsStartOctant(
            byte start,
            byte end,
            byte octant,
            double left,
            double bottom,
            double right,
            double top
        )
        {
            AssertInk(start, end, 0, 100, octant, left, bottom, right, top);
        }
    }
}
