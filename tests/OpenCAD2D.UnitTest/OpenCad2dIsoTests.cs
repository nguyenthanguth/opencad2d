using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using GeometryHelper;
using Xunit;

namespace OpenCAD2D.UnitTest
{
    /// <summary>
    /// OpenCAD2D ISO, the built-in font made in code from its data file: 242 code points, 100 above and 30 below, every
    /// glyph read back through <see cref="ShxFont"/> to the skeleton the file draws. Numbers are font units, a tenth of the
    /// line width d, so a capital is 100 high and a letter's side bearing is 10.
    /// </summary>
    public sealed class OpenCad2dIsoTests
    {
        // Points that must be exact: line vertices and the ends of arcs, which the pen lands on.
        private static readonly Tolerance Exactly = new Tolerance(1E-9, 1E-9);

        // Points along an arc drawn by a bulge, which AutoCAD takes in 1/127: the generator keeps them within 0.5 of a unit.
        private static readonly Tolerance Bulged = new Tolerance(0.6, 0.6);

        // The skeleton's own arcs against the read ones, that are cut into chords of 11.25 degrees (0.4 of a unit at the
        // widest arc of the font) on top of the bulge.
        private static readonly Tolerance Chorded = new Tolerance(1.0, 1.0);

        private static readonly ShxFont Font = ShxFont.Read(OpenCad2dIso.Unifont());

        /// <summary>The 12 bases of the table of 3.8 of the design, capitals: none, grave, acute, hook, tilde, dot below.</summary>
        private static readonly int[][] Table =
        {
            new[] { 0x41, 0xC0, 0xC1, 0x1EA2, 0xC3, 0x1EA0 },
            new[] { 0x102, 0x1EB0, 0x1EAE, 0x1EB2, 0x1EB4, 0x1EB6 },
            new[] { 0xC2, 0x1EA6, 0x1EA4, 0x1EA8, 0x1EAA, 0x1EAC },
            new[] { 0x45, 0xC8, 0xC9, 0x1EBA, 0x1EBC, 0x1EB8 },
            new[] { 0xCA, 0x1EC0, 0x1EBE, 0x1EC2, 0x1EC4, 0x1EC6 },
            new[] { 0x49, 0xCC, 0xCD, 0x1EC8, 0x128, 0x1ECA },
            new[] { 0x4F, 0xD2, 0xD3, 0x1ECE, 0xD5, 0x1ECC },
            new[] { 0xD4, 0x1ED2, 0x1ED0, 0x1ED4, 0x1ED6, 0x1ED8 },
            new[] { 0x1A0, 0x1EDC, 0x1EDA, 0x1EDE, 0x1EE0, 0x1EE2 },
            new[] { 0x55, 0xD9, 0xDA, 0x1EE6, 0x168, 0x1EE4 },
            new[] { 0x1AF, 0x1EEA, 0x1EE8, 0x1EEC, 0x1EEE, 0x1EF0 },
            new[] { 0x59, 0x1EF2, 0xDD, 0x1EF6, 0x1EF8, 0x1EF4 },
        };

        private static string Data()
        {
            using (Stream stream = typeof(ShxFont).Assembly.GetManifestResourceStream("OpenCAD2D.opencad2d.glyphs.txt"))
            using (var reader = new StreamReader(stream, new UTF8Encoding(false)))
            {
                return reader.ReadToEnd();
            }
        }

        private static List<int> ExpectedCodePoints()
        {
            var codes = new List<int>();
            codes.AddRange(Enumerable.Range(0x20, 95));
            codes.AddRange(new[] { 0xB0, 0xB1, 0xB2, 0xD8, 0x2013, 0x2014, 0x2205, 0x2300 });
            codes.AddRange(new[] { 0x300, 0x301, 0x303, 0x309, 0x323 });
            codes.AddRange(new[] { 0x110, 0x111 });

            foreach (int capital in Table.SelectMany(row => row).Where(c => c >= 0x80))
            {
                codes.Add(capital);
                codes.Add(char.ToLowerInvariant((char)capital));
            }

            codes.Sort();
            return codes;
        }

        private static void Near(double expected, double actual, Tolerance tolerance, string what)
        {
            Assert.True(
                Math.Abs(expected - actual) <= tolerance.EqualPoint,
                what + ": expected " + expected + ", got " + actual
            );
        }

        private static double[] Box(IEnumerable<double[]> strokes)
        {
            double[] box = { double.MaxValue, double.MaxValue, double.MinValue, double.MinValue };

            foreach (double[] stroke in strokes)
            {
                for (int i = 0; i + 1 < stroke.Length; i += 2)
                {
                    box[0] = Math.Min(box[0], stroke[i]);
                    box[1] = Math.Min(box[1], stroke[i + 1]);
                    box[2] = Math.Max(box[2], stroke[i]);
                    box[3] = Math.Max(box[3], stroke[i + 1]);
                }
            }

            return box;
        }

        private static double Turn(double angle)
        {
            double turn = angle % (2.0 * Math.PI);
            return turn < 0.0 ? turn + (2.0 * Math.PI) : turn;
        }

        private static double ToSegment(double px, double py, double ax, double ay, double bx, double by)
        {
            double dx = bx - ax;
            double dy = by - ay;
            double length2 = (dx * dx) + (dy * dy);
            double t =
                length2 == 0.0 ? 0.0 : Math.Max(0.0, Math.Min(1.0, (((px - ax) * dx) + ((py - ay) * dy)) / length2));
            return Math.Sqrt(Math.Pow(px - (ax + (t * dx)), 2) + Math.Pow(py - (ay + (t * dy)), 2));
        }

        /// <summary>How far a point is from the skeleton's stroke: from its lines, and from its arcs where the point lies in their sweep, else from their ends.</summary>
        private static double ToStroke(double px, double py, OpenCad2dIso.Stroke stroke)
        {
            double best = double.MaxValue;
            double x = stroke.X;
            double y = stroke.Y;

            foreach (OpenCad2dIso.Step step in stroke.Steps)
            {
                if (!step.IsArc)
                {
                    best = Math.Min(best, ToSegment(px, py, x, y, step.X, step.Y));
                }
                else
                {
                    double r = Math.Sqrt(Math.Pow(x - step.CentreX, 2) + Math.Pow(y - step.CentreY, 2));
                    double away = Math.Sqrt(Math.Pow(px - step.CentreX, 2) + Math.Pow(py - step.CentreY, 2));
                    double a0 = Math.Atan2(y - step.CentreY, x - step.CentreX);
                    double a1 = Math.Atan2(step.Y - step.CentreY, step.X - step.CentreX);
                    double at = Math.Atan2(py - step.CentreY, px - step.CentreX);
                    double sweep =
                        step.Whole ? 2.0 * Math.PI
                        : step.CounterClockwise ? Turn(a1 - a0)
                        : Turn(a0 - a1);
                    double along = step.CounterClockwise ? Turn(at - a0) : Turn(a0 - at);

                    if (along <= sweep)
                    {
                        best = Math.Min(best, Math.Abs(away - r));
                    }
                    else
                    {
                        best = Math.Min(best, Math.Sqrt(Math.Pow(px - x, 2) + Math.Pow(py - y, 2)));
                        best = Math.Min(best, Math.Sqrt(Math.Pow(px - step.X, 2) + Math.Pow(py - step.Y, 2)));
                    }
                }

                x = step.X;
                y = step.Y;
            }

            return best;
        }

        /// <summary>Points along the skeleton's stroke: its vertices and eight along each arc.</summary>
        private static IEnumerable<double[]> Along(OpenCad2dIso.Stroke stroke)
        {
            double x = stroke.X;
            double y = stroke.Y;
            yield return new double[] { x, y };

            foreach (OpenCad2dIso.Step step in stroke.Steps)
            {
                if (step.IsArc)
                {
                    double r = Math.Sqrt(Math.Pow(x - step.CentreX, 2) + Math.Pow(y - step.CentreY, 2));
                    double a0 = Math.Atan2(y - step.CentreY, x - step.CentreX);
                    double a1 = Math.Atan2(step.Y - step.CentreY, step.X - step.CentreX);
                    double sweep =
                        step.Whole ? 2.0 * Math.PI
                        : step.CounterClockwise ? Turn(a1 - a0)
                        : Turn(a0 - a1);

                    for (int k = 1; k < 8; k++)
                    {
                        double a = a0 + ((step.CounterClockwise ? 1.0 : -1.0) * sweep * k / 8.0);
                        yield return new[] { step.CentreX + (r * Math.Cos(a)), step.CentreY + (r * Math.Sin(a)) };
                    }
                }

                x = step.X;
                y = step.Y;
                yield return new double[] { x, y };
            }
        }

        private static double ToPolyline(double px, double py, double[] stroke)
        {
            double best = double.MaxValue;

            for (int i = 0; i + 3 < stroke.Length; i += 2)
            {
                best = Math.Min(best, ToSegment(px, py, stroke[i], stroke[i + 1], stroke[i + 2], stroke[i + 3]));
            }

            return best;
        }

        /// <summary>Whether a code is a letter made of a base and marks, or a combining mark: a glyph whose ink goes past its box.</summary>
        private static bool IsComposed(int code) =>
            (code >= 0x300 && code <= 0x323)
            || OpenCad2dIso.Vietnamese.Any(l => l.Code == code && (l.Modifier != null || l.Tone != 0));

        // ---- the code points ----

        [Fact]
        public void TheFontHoldsTheExact242CodePointsOfTheDesign()
        {
            List<int> expected = ExpectedCodePoints();
            Assert.Equal(242, expected.Count);
            Assert.Equal(expected, OpenCad2dIso.CodePoints);
            Assert.Equal(242, Font.Count);

            foreach (int code in expected)
            {
                Assert.True(Font.Has(code), "U+" + code.ToString("X4"));
            }
        }

        [Fact]
        public void TheFontIsAUnifont100AboveAndThirtyBelow()
        {
            Assert.Equal(ShxFontKind.Unifont, Font.Kind);
            Assert.Equal(100.0, Font.Above, 6);
            Assert.Equal(30.0, Font.Below, 6);
        }

        // ---- advance and ink ----

        [Fact]
        public void TheAdvanceIsTheInkPlusTwoD_SevenForADigit_FourForASpace_AndNothingForACombiningMark()
        {
            foreach (int code in OpenCad2dIso.CodePoints)
            {
                OpenCad2dIso.Glyph glyph = OpenCad2dIso.GlyphOf(code);
                double advance = Font.GlyphOf(code).Advance;
                int expected =
                    code == 0x20 ? 40
                    : code >= 0x30 && code <= 0x39 ? 70
                    : IsCombining(code) ? 0
                    : glyph.Width + 20;
                Near(expected, advance, Exactly, "advance of U+" + code.ToString("X4"));
            }

            Near(80, Font.GlyphOf('A').Advance, Exactly, "A");
            Near(110, Font.GlyphOf('W').Advance, Exactly, "W");
            Near(20, Font.GlyphOf('i').Advance, Exactly, "i");
            Near(95, Font.GlyphOf(0x1A0).Advance, Exactly, "Ơ, whose horn is part of its ink");
            Near(80, Font.GlyphOf(0x1EA4).Advance, Exactly, "Ấ, whose marks change no advance");
        }

        private static bool IsCombining(int code) => code >= 0x300 && code <= 0x323;

        [Fact]
        public void TheInkOfEveryGlyphStartsAtTheLeftBearingAndRunsItsWidth_ADigitInsideItsCell()
        {
            foreach (int code in OpenCad2dIso.CodePoints.Where(c => !IsComposed(c)))
            {
                OpenCad2dIso.Glyph glyph = OpenCad2dIso.GlyphOf(code);
                ShxGlyph read = Font.GlyphOf(code);
                var ink = new List<double[]>();

                for (int i = 0; i < glyph.Strokes.Count; i++)
                {
                    if (!glyph.Strokes[i].Dot)
                    {
                        ink.Add(read.Strokes[i]);
                    }
                }

                if (ink.Count == 0)
                {
                    continue;
                }

                double[] box = Box(ink);
                string what = "U+" + code.ToString("X4");

                if (code >= 0x30 && code <= 0x39)
                {
                    Assert.True(
                        box[0] >= 10.0 - Bulged.EqualPoint && box[2] <= 10.0 + glyph.Width + Bulged.EqualPoint,
                        what
                    );
                }
                else
                {
                    Near(10.0, box[0], Bulged, "left of " + what);
                    Near(10.0 + glyph.Width, box[2], Bulged, "right of " + what);
                }
            }
        }

        [Fact]
        public void TheCapitalsReachTheHeight_TheSmallLettersTheirXHeight_TheDescendersThirtyBelow()
        {
            foreach (char c in "ABCDEFGHIJKLMNOPQRSTUVWXYZ0123456789")
            {
                Near(100.0, Box(Font.GlyphOf(c).Strokes)[3], Bulged, "top of " + c);
            }

            foreach (char c in "acemnorsuvwxz")
            {
                Near(70.0, Box(Font.GlyphOf(c).Strokes)[3], Bulged, "top of " + c);
            }

            foreach (char c in "bdfhkl")
            {
                Near(100.0, Box(Font.GlyphOf(c).Strokes)[3], Bulged, "top of " + c);
            }

            foreach (char c in "gjpq")
            {
                Near(-30.0, Box(Font.GlyphOf(c).Strokes)[1], Bulged, "bottom of " + c);
            }

            Near(-28.0, Box(Font.GlyphOf('y').Strokes)[1], Bulged, "bottom of y");
        }

        [Fact]
        public void TheAIsTwoLinesDrawnByTheNumbers()
        {
            ShxGlyph a = Font.GlyphOf('A');
            Assert.Equal(2, a.Strokes.Count);
            double[] first = { 10, 0, 40, 100, 70, 0 };
            double[] second = { 19, 30, 61, 30 };

            for (int i = 0; i < first.Length; i++)
            {
                Near(first[i], a.Strokes[0][i], Exactly, "stroke 1 value " + i);
            }

            for (int i = 0; i < second.Length; i++)
            {
                Near(second[i], a.Strokes[1][i], Exactly, "stroke 2 value " + i);
            }
        }

        [Fact]
        public void ASpaceDrawsNothingAndMovesFortyAndADotIsACircleOfRadius3()
        {
            Assert.Empty(Font.GlyphOf(' ').Strokes);
            Near(40, Font.GlyphOf(' ').Advance, Exactly, "space");

            double[] dot = Box(new[] { Font.GlyphOf('.').Strokes[0] });
            Near(10.0, dot[0], Exactly, "left of .");
            Near(16.0, dot[2], Exactly, "right of .");
            Near(0.0, dot[1], Bulged, "bottom of .");
            Near(6.0, dot[3], Bulged, "top of .");
        }

        // ---- the round trip ----

        [Fact]
        public void EveryGlyphReadBackKeepsItsVerticesExactAndItsArcsWithinHalfAUnit()
        {
            foreach (int code in OpenCad2dIso.CodePoints)
            {
                OpenCad2dIso.Glyph glyph = OpenCad2dIso.GlyphOf(code);
                ShxGlyph read = Font.GlyphOf(code);
                string what = "U+" + code.ToString("X4");
                Assert.Equal(glyph.Strokes.Count, read.Strokes.Count);

                for (int i = 0; i < glyph.Strokes.Count; i++)
                {
                    OpenCad2dIso.Stroke stroke = glyph.Strokes[i];
                    double[] points = read.Strokes[i];
                    Near(stroke.X, points[0], Exactly, what + " stroke " + i + " starts at x");
                    Near(stroke.Y, points[1], Exactly, what + " stroke " + i + " starts at y");
                    Near(
                        stroke.Steps[stroke.Steps.Count - 1].X,
                        points[points.Length - 2],
                        Exactly,
                        what + " ends at x"
                    );
                    Near(
                        stroke.Steps[stroke.Steps.Count - 1].Y,
                        points[points.Length - 1],
                        Exactly,
                        what + " ends at y"
                    );

                    foreach (OpenCad2dIso.Step step in stroke.Steps)
                    {
                        bool landed = false;

                        for (int p = 0; p + 1 < points.Length; p += 2)
                        {
                            landed |=
                                Math.Abs(points[p] - step.X) <= Exactly.EqualPoint
                                && Math.Abs(points[p + 1] - step.Y) <= Exactly.EqualPoint;
                        }

                        Assert.True(landed, what + " stroke " + i + " passes " + step.X + "," + step.Y);
                    }

                    for (int p = 0; p + 1 < points.Length; p += 2)
                    {
                        Near(
                            0.0,
                            ToStroke(points[p], points[p + 1], stroke),
                            Bulged,
                            what + " stroke " + i + " drawn point"
                        );
                    }

                    foreach (double[] on in Along(stroke))
                    {
                        Near(0.0, ToPolyline(on[0], on[1], points), Chorded, what + " stroke " + i + " skeleton point");
                    }
                }

                Near(glyph.Advance, read.Advance, Exactly, what + " final pen");
            }
        }

        // ---- Vietnamese ----

        [Fact]
        public void The132ComposedAndHornedLettersDecomposeToTheBaseAndMarksOfTheTable()
        {
            Assert.Equal(132, OpenCad2dIso.Vietnamese.Count);

            foreach (OpenCad2dIso.Letter letter in OpenCad2dIso.Vietnamese)
            {
                string precomposed = char.ConvertFromUtf32(letter.Code);
                Assert.Equal(
                    letter.Decomposition.Normalize(NormalizationForm.FormD),
                    precomposed.Normalize(NormalizationForm.FormD)
                );
                Assert.Equal(precomposed, letter.Decomposition.Normalize(NormalizationForm.FormC));
            }
        }

        [Fact]
        public void TheVietnameseLettersAreTheTableInBothCasesAndEachIsInTheFont()
        {
            var table = Table.SelectMany(row => row).Where(c => c >= 0x80).ToList();
            Assert.Equal(66, table.Count);

            foreach (int capital in table)
            {
                Assert.True(Font.Has(capital), "U+" + capital.ToString("X4"));
                Assert.True(Font.Has(char.ToLowerInvariant((char)capital)), "lowercase of U+" + capital.ToString("X4"));
            }

            Assert.Equal(table.Count * 2, OpenCad2dIso.Vietnamese.Count);
            Assert.True(Font.Has(0x110) && Font.Has(0x111));
            Assert.Equal("Đ", "Đ".Normalize(NormalizationForm.FormD));
        }

        [Fact]
        public void TheMarksSitWhereTheDesignPutsThem()
        {
            // Ấ: the circumflex apex 15 above the 110 zone line over the A's anchor 40, the acute beside its right arm.
            double[] a = Box(Font.GlyphOf(0x1EA4).Strokes);
            Near(131.0, a[3], Bulged, "top of Ấ");
            Near(10.0, a[0], Bulged, "left of Ấ");

            // Ẳ: the breve on 110 and the hook stacked 20 above it, the highest stack of the font.
            Near(150.0, Box(Font.GlyphOf(0x1EB2).Strokes)[3], Bulged, "top of Ẳ");

            // Ặ: the dot below in the descender, centre -16 radius 3.
            Near(-19.0, Box(Font.GlyphOf(0x1EB6).Strokes)[1], Bulged, "bottom of Ặ");

            // Lowercase marks start at 80.
            Near(80.0, Box(Font.GlyphOf(0xE0).Strokes.Skip(2))[1], Bulged, "bottom of à's grave");
            Near(96.0, Box(Font.GlyphOf(0xE1).Strokes)[3], Bulged, "top of á");

            // The E's anchor 32.5 goes to the next grid point: the circumflex apex of Ê stands at x 33.
            ShxGlyph e = Font.GlyphOf(0xCA);
            Near(33.0, e.Strokes[2][2], Exactly, "apex of Ê");
            Near(125.0, e.Strokes[2][3], Exactly, "apex height of Ê");

            // ì loses its dot, ị keeps it, and the dot of ị is the one below.
            Assert.Equal(2, Font.GlyphOf(0xEC).Strokes.Count);
            Assert.Equal(3, Font.GlyphOf(0x1ECB).Strokes.Count);
            Assert.Equal(2, Font.GlyphOf('i').Strokes.Count);
        }

        [Fact]
        public void ACombiningToneMarkMovesNothingAndDrawsBackOverTheLetterBefore()
        {
            ShxGlyph acute = Font.GlyphOf(0x301);
            Near(0.0, acute.Advance, Exactly, "advance of the combining acute");
            Assert.Single(acute.Strokes);

            double[] want = { -41, 80, -29, 96 };

            for (int i = 0; i < want.Length; i++)
            {
                Near(want[i], acute.Strokes[0][i], Exactly, "acute value " + i);
            }

            Near(-38.0, Box(Font.GlyphOf(0x323).Strokes)[0], Exactly, "the dot below, centred 35 back, is 3 wider");
        }

        // ---- the file ----

        [Fact]
        public void TheBytesHaveTheHeaderTheCountAndTheInfoAutoCadWantsAndEveryRecordIsUnder2000()
        {
            byte[] bytes = OpenCad2dIso.Unifont();
            string header = "AutoCAD-86 unifont 1.0\r\n\x1A";
            Assert.Equal(header, Encoding.ASCII.GetString(bytes, 0, header.Length));
            int at = header.Length;
            int count = BitConverter.ToInt32(bytes, at);
            Assert.Equal(243, count);
            int info = BitConverter.ToUInt16(bytes, at + 4);
            Assert.Equal(20, info);
            at += 6;
            Assert.Equal("OpenCAD2D ISO\0", Encoding.ASCII.GetString(bytes, at, 14));
            Assert.Equal(new byte[] { 100, 30, 0, 0, 0, 0 }, bytes.Skip(at + 14).Take(6).ToArray());
            at += info;

            int previous = -1;
            int records = 0;
            int largest = 0;

            while (at < bytes.Length)
            {
                int code = BitConverter.ToUInt16(bytes, at);
                int length = BitConverter.ToUInt16(bytes, at + 2);
                Assert.True(code > previous, "records ascend by code");
                Assert.True(length < 2000, "U+" + code.ToString("X4") + " is " + length);
                Assert.Equal(0, bytes[at + 4]);
                Assert.Equal(0, bytes[at + 3 + length]);
                previous = code;
                records++;
                largest = Math.Max(largest, length);
                at += 4 + length;
            }

            Assert.Equal(bytes.Length, at);
            Assert.Equal(242, records);
            Assert.True(largest > 50);
        }

        [Fact]
        public void EveryRecordUsesOnlyTheCodesTheDesignAllows_ArcsOfARadiusUnder128_AndMovesWithinASignedByte()
        {
            byte[] bytes = OpenCad2dIso.Unifont();
            int at = 25 + 6 + 20;
            var seen = new SortedSet<int>();
            bool clockwise = false;

            while (at < bytes.Length)
            {
                int code = BitConverter.ToUInt16(bytes, at);
                int length = BitConverter.ToUInt16(bytes, at + 2);
                int i = at + 5;
                int end = at + 4 + length - 1;

                while (i < end)
                {
                    int op = bytes[i++];
                    seen.Add(op);

                    switch (op)
                    {
                        case 1:
                        case 2:
                            break;
                        case 8:
                            Assert.NotEqual(0x80, bytes[i]);
                            Assert.NotEqual(0x80, bytes[i + 1]);
                            i += 2;
                            break;
                        case 10:
                            Assert.InRange((int)bytes[i], 1, 127);
                            clockwise |= (bytes[i + 1] & 0x80) != 0;
                            i += 2;
                            break;
                        case 12:
                            Assert.NotEqual(0x80, bytes[i]);
                            Assert.NotEqual(0x80, bytes[i + 1]);
                            Assert.NotEqual(0x80, bytes[i + 2]);
                            i += 3;
                            break;
                        default:
                            Assert.Fail("U+" + code.ToString("X4") + " uses code " + op);
                            break;
                    }
                }

                at += 4 + length;
            }

            Assert.Equal(new[] { 1, 2, 8, 10, 12 }, seen.ToArray());
            Assert.True(clockwise, "a clockwise quarter-turn arc is written");
        }

        [Fact]
        public void TwoRunsGiveTheSameBytes()
        {
            Assert.Equal(OpenCad2dIso.Unifont(), OpenCad2dIso.Unifont());
            string data = Data();
            Assert.Equal(
                OpenCad2dIso.Write(OpenCad2dIso.Build(OpenCad2dIso.Parse(data))),
                OpenCad2dIso.Write(OpenCad2dIso.Build(OpenCad2dIso.Parse(data)))
            );
            Assert.Equal(OpenCad2dIso.Unifont(), OpenCad2dIso.Write(OpenCad2dIso.Build(OpenCad2dIso.Parse(data))));
        }

        // ---- the data file ----

        [Fact]
        public void TheShippedDataParsesWithNoError_109GlyphsAnd7Marks()
        {
            OpenCad2dIso.Skeleton skeleton = OpenCad2dIso.Parse(Data());
            Assert.Equal(109, skeleton.Glyphs.Count);
            Assert.Equal(7, skeleton.Marks.Count);
            Assert.Equal(
                new[] { "acute", "breve", "circumflex", "dotbelow", "grave", "hook", "tilde" },
                skeleton.Marks.Keys.ToArray()
            );
        }

        [Theory]
        [InlineData("glyph 0041 A w=5 : M0,0 L3,10 L6,0", "0041")]
        [InlineData("glyph 0043 C w=4 : M4,0 A+ 0,0 0,3", "0043")]
        [InlineData("glyph 0044 D w=5 : M0,0 L0,10.25", "0044")]
        [InlineData("glyph 0045 E w=5 : M0,0 L0,10 X1,2", "0045")]
        [InlineData("glyph 0046 F w=5 : L0,10", "0046")]
        [InlineData("glyph 0047 G w=5 : M0,0", "0047")]
        [InlineData("glyph 0048 H w=5 q=1 : M0,0 L5,0", "0048")]
        [InlineData("glyph 0049 I : M0,0 L0,10", "0049")]
        [InlineData("glyph 004A J w=4 : M0,0 L4,0 A+ 4,0 4,0", "004A")]
        [InlineData("glyph 004B K w=5 : M0,0 L5,0 O1,1", "004B")]
        [InlineData("glyph 00GG X w=5 : M0,0 L5,0", "00GG")]
        [InlineData("glyph 004C L w=5 : M0,0 L5,0\nglyph 004C L w=5 : M0,0 L5,0", "004C")]
        [InlineData("mark acute : M0,0 L0,1\nsurprise 1", "line 2")]
        public void ABrokenLineThrowsAnInvalidDataExceptionThatNamesTheGlyph(string data, string named)
        {
            var thrown = Assert.Throws<InvalidDataException>(() => OpenCad2dIso.Parse(data));
            Assert.Contains(named, thrown.Message);
        }

        [Fact]
        public void ABrokenLineInTheShippedDataThrowsAndNamesItsCode()
        {
            string broken = Data().Replace("glyph 0042 B w=5 ", "glyph 0042 B w=6 ");
            var thrown = Assert.Throws<InvalidDataException>(() => OpenCad2dIso.Parse(broken));
            Assert.Contains("0042", thrown.Message);

            string off = Data().Replace("A- 2.6,7.8 2.6,5.6", "A- 2.6,7.8 2.6,5.7");
            thrown = Assert.Throws<InvalidDataException>(() => OpenCad2dIso.Parse(off));
            Assert.Contains("0042", thrown.Message);
        }

        [Fact]
        public void AComposedLetterWhoseBaseIsMissingThrowsAndNamesItsCode()
        {
            string data = string.Join(
                "\n",
                Data().Split('\n').Where(l => !l.StartsWith("glyph 0041 ", StringComparison.Ordinal))
            );
            var thrown = Assert.Throws<InvalidDataException>(() => OpenCad2dIso.Build(OpenCad2dIso.Parse(data)));
            Assert.Contains("0041", thrown.Message);
        }

        // ---- writing arcs and lines ----

        private static OpenCad2dIso.Glyph One(params OpenCad2dIso.Stroke[] strokes) =>
            new OpenCad2dIso.Glyph(0x41, 0, 20, 0.0, 0.0, strokes);

        private static OpenCad2dIso.Stroke Line(int x, int y, int toX, int toY) =>
            new OpenCad2dIso.Stroke(x, y, new[] { new OpenCad2dIso.Step(toX, toY) }, false);

        private static OpenCad2dIso.Stroke Arc(int x, int y, int cx, int cy, bool ccw, int toX, int toY) =>
            new OpenCad2dIso.Stroke(x, y, new[] { new OpenCad2dIso.Step(cx, cy, ccw, false, toX, toY) }, false);

        [Fact]
        public void ALineOver127IsCutIntoEqualPartsAndStaysOneStraightStroke()
        {
            ShxFont font = ShxFont.Read(OpenCad2dIso.Write(new[] { One(Line(10, -20, 10, 235)) }));
            ShxGlyph glyph = font.GlyphOf(0x41);
            Assert.Single(glyph.Strokes);
            double[] points = glyph.Strokes[0];
            Assert.Equal(8, points.Length);
            Near(10.0, points[0], Exactly, "x");
            Near(-20.0, points[1], Exactly, "y");
            Near(65.0, points[3], Exactly, "after a third");
            Near(150.0, points[5], Exactly, "after two thirds");
            Near(235.0, points[7], Exactly, "end");
        }

        [Fact]
        public void ALineOver127ThatCannotBeCutOnTheGridThrows()
        {
            var thrown = Assert.Throws<InvalidDataException>(() =>
                OpenCad2dIso.Write(new[] { One(Line(0, 0, 130, 1)) })
            );
            Assert.Contains("0041", thrown.Message);
        }

        [Fact]
        public void AQuarterTurnArcOfARadiusOver127ThrowsAndOneOf127IsWritten()
        {
            var thrown = Assert.Throws<InvalidDataException>(() =>
                OpenCad2dIso.Write(new[] { One(Arc(128, 0, 0, 0, true, 0, 128)) })
            );
            Assert.Contains("0041", thrown.Message);
            ShxFont font = ShxFont.Read(OpenCad2dIso.Write(new[] { One(Arc(127, 0, 0, 0, true, 0, 127)) }));
            double[] points = font.GlyphOf(0x41).Strokes[0];
            Near(0.0, points[points.Length - 2], Exactly, "ends at x 0");
            Near(127.0, points[points.Length - 1], Exactly, "ends at y 127");
        }

        [Theory]
        [InlineData(true, 20, 0, 0, 20)]
        [InlineData(false, 0, 20, 20, 0)]
        public void AQuarterTurnArcAboutTheOriginLandsOnItsEnd_ClockwiseOrNot(bool ccw, int x, int y, int toX, int toY)
        {
            ShxFont font = ShxFont.Read(OpenCad2dIso.Write(new[] { One(Arc(x, y, 0, 0, ccw, toX, toY)) }));
            double[] points = font.GlyphOf(0x41).Strokes[0];
            Near(toX, points[points.Length - 2], Exactly, "end x");
            Near(toY, points[points.Length - 1], Exactly, "end y");
            Near(x, points[0], Exactly, "start x");

            // The middle of the arc is on the circle of radius 20 about the origin, 45 degrees between the ends.
            int mid = (points.Length / 4) * 2;
            Near(20.0, Math.Sqrt((points[mid] * points[mid]) + (points[mid + 1] * points[mid + 1])), Exactly, "radius");
            Assert.True(points[mid] > 0.0 && points[mid + 1] > 0.0, "the arc runs through the first quadrant");
        }

        [Fact]
        public void AnArcThatIsNotAQuarterTurnIsWrittenByItsBulgeAndLandsExactlyOnItsEnd()
        {
            // 143 degrees of radius 25 about the origin, counter-clockwise from (0, 25) to (-15, -20).
            OpenCad2dIso.Stroke arc = Arc(0, 25, 0, 0, true, -15, -20);
            ShxFont font = ShxFont.Read(OpenCad2dIso.Write(new[] { One(arc) }));
            double[] points = font.GlyphOf(0x41).Strokes[0];
            Near(-15.0, points[points.Length - 2], Exactly, "end x");
            Near(-20.0, points[points.Length - 1], Exactly, "end y");

            for (int p = 0; p + 1 < points.Length; p += 2)
            {
                Near(
                    25.0,
                    Math.Sqrt((points[p] * points[p]) + (points[p + 1] * points[p + 1])),
                    Bulged,
                    "radius at " + p
                );
            }
        }

        [Fact]
        public void AnArcOver180IsCutAtGridPointsOnTheCircleAndTheWholeStaysWithinHalfAUnit()
        {
            // The S of the font: 233 degrees of radius 25 from (45, 90) round to (25, 50).
            OpenCad2dIso.Stroke arc = Arc(45, 90, 25, 75, true, 25, 50);
            ShxFont font = ShxFont.Read(OpenCad2dIso.Write(new[] { One(arc) }));
            double[] points = font.GlyphOf(0x41).Strokes[0];
            Near(25.0, points[points.Length - 2], Exactly, "end x");
            Near(50.0, points[points.Length - 1], Exactly, "end y");

            for (int p = 0; p + 1 < points.Length; p += 2)
            {
                double radius = Math.Sqrt(Math.Pow(points[p] - 25.0, 2) + Math.Pow(points[p + 1] - 75.0, 2));
                Near(25.0, radius, Bulged, "radius at " + p);
            }
        }
    }
}
