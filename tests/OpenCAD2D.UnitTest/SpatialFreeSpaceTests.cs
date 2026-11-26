using System;
using System.Collections.Generic;
using System.Linq;
using GeometryHelper;
using GeometryHelper.Dxf;
using GeometryHelper.Geometry;
using Xunit;
using static OpenCAD2D.UnitTest.SpatialDrawings;

namespace OpenCAD2D.UnitTest
{
    /// <summary>
    /// <see cref="CadSpatial.FreeSpace"/> and <see cref="CadSpatial.Fits"/>, on drawings made in code: the empty rectangles of
    /// a frame holding a few lines and words, a brute-force check of them over random lines, and which candidates fit.
    /// </summary>
    public sealed class SpatialFreeSpaceTests : IDisposable
    {
        private static readonly Tolerance Touching = new Tolerance(1E-3, 1E-5);
        private readonly SpatialDrawings _drawings = new SpatialDrawings();

        public void Dispose() => _drawings.Dispose();

        private static List<double[]> Rows(CadFreeSpace table)
        {
            var rows = new List<double[]>();

            for (int i = 0; i < table.Count; i++)
            {
                DisplayBox box = table.BoxOf(i);
                rows.Add(new[] { box.MinX, box.MinY, box.MaxX, box.MaxY });
            }

            return rows;
        }

        private static void AssertRows(double[][] wanted, CadFreeSpace got)
        {
            List<double[]> rows = Rows(got);
            Assert.Equal(wanted.Length, rows.Count);

            for (int i = 0; i < wanted.Length; i++)
            {
                for (int k = 0; k < 4; k++)
                {
                    Assert.Equal(wanted[i][k], rows[i][k], 6);
                }
            }
        }

        private CadFreeSpace Free(
            CadDocument document,
            DisplayBox frame,
            string[] kinds = null,
            double margin = 0.0,
            double minWidth = 0.0,
            double minHeight = 0.0
        ) =>
            CadSpatial.FreeSpace(
                document,
                document.ModelSpace,
                frame,
                new CadFreeSpaceOptions
                {
                    Kinds = kinds,
                    Margin = margin,
                    MinWidth = minWidth,
                    MinHeight = minHeight,
                },
                Touching
            );

        private static readonly DisplayBox Frame = new DisplayBox(0, 0, 100, 50);

        [Fact]
        public void AnEmptyFrame_IsOneRectangle_ItsOwn()
        {
            CadDocument document = _drawings.Blank();

            AssertRows(new[] { new[] { 0.0, 0.0, 100.0, 50.0 } }, Free(document, Frame));
        }

        [Fact]
        public void AVerticalLineAcrossTheFrame_LeavesTwoHalves_TheLargerFirst()
        {
            CadDocument document = _drawings.Blank();
            Put(document, Line(40, -10, 40, 70));

            AssertRows(
                new[] { new[] { 40.0, 0.0, 100.0, 50.0 }, new[] { 0.0, 0.0, 40.0, 50.0 } },
                Free(document, Frame)
            );
        }

        [Fact]
        public void AShortLineInTheMiddle_LeavesFourMaximalRectangles_WhichOverlap()
        {
            CadDocument document = _drawings.Blank();
            Put(document, Line(40, 20, 40, 30));

            AssertRows(
                new[]
                {
                    new[] { 40.0, 0.0, 100.0, 50.0 },
                    new[] { 0.0, 0.0, 100.0, 20.0 },
                    new[] { 0.0, 0.0, 40.0, 50.0 },
                    new[] { 0.0, 30.0, 100.0, 50.0 },
                },
                Free(document, Frame)
            );
        }

        [Fact]
        public void TheMargin_GrowsWhatIsInTheWay_OnEverySide()
        {
            CadDocument document = _drawings.Blank();
            Put(document, Line(40, -10, 40, 70));

            AssertRows(
                new[] { new[] { 45.0, 0.0, 100.0, 50.0 }, new[] { 0.0, 0.0, 35.0, 50.0 } },
                Free(document, Frame, margin: 5.0)
            );
        }

        [Fact]
        public void AWordIsTakenByItsFrame_AndOnlyTheKindsAskedAboutTakeRoom()
        {
            CadDocument document = _drawings.Blank();
            DxfDBText text = Put(document, Text("SIDE FOOD", 10, 10, 0.0, 5.0));
            Put(document, Line(70, 0, 70, 50));
            DisplayBox word = CadMeasure.Of(document, text).Frames.Single().Box;

            CadFreeSpace both = Free(document, Frame);
            CadFreeSpace onlyText = Free(document, Frame, new[] { "TEXT" });
            CadFreeSpace onlyLine = Free(document, Frame, new[] { "line" });

            Assert.All(Rows(both), r => Assert.False(Enters(r, word)));
            Assert.Contains(Rows(onlyText), r => r[2] > 99.0 && r[0] <= word.MaxX + 1E-6);
            Assert.Contains(Rows(onlyLine), r => r[0] == 0.0 && r[2] == 70.0 && r[3] == 50.0 && r[1] == 0.0);
        }

        private static bool Enters(double[] r, DisplayBox box) =>
            r[0] < box.MaxX - 1E-6 && r[2] > box.MinX + 1E-6 && r[1] < box.MaxY - 1E-6 && r[3] > box.MinY + 1E-6;

        [Fact]
        public void ALeastSize_DropsTheRectanglesTooSmallToMatter_AndKeepsTheOthersExactly()
        {
            CadDocument document = _drawings.Blank();
            Put(document, Line(40, 20, 40, 30));
            CadFreeSpace all = Free(document, Frame);

            CadFreeSpace wide = Free(document, Frame, minWidth: 50.0);
            CadFreeSpace tall = Free(document, Frame, minHeight: 25.0);

            Assert.Equal(
                Rows(all).Where(r => r[2] - r[0] >= 50.0 - 1E-6).Select(r => string.Join(",", r)),
                Rows(wide).Select(r => string.Join(",", r))
            );
            Assert.Equal(
                Rows(all).Where(r => r[3] - r[1] >= 25.0 - 1E-6).Select(r => string.Join(",", r)),
                Rows(tall).Select(r => string.Join(",", r))
            );
            Assert.Empty(Rows(Free(document, Frame, minWidth: 101.0)));
        }

        [Fact]
        public void ASlantedLine_TakesTheStaircaseAlongItself_NotTheSquareItSpans()
        {
            CadDocument document = _drawings.Blank();
            Put(document, Line(0, 0, 100, 50));

            CadFreeSpace table = Free(document, Frame);

            Assert.All(Rows(table), r =>
                Assert.False(Contains(r, 50.0, 25.0), "a rectangle holds the middle of the line")
            );
            Assert.All(Rows(table), r =>
                Assert.False(Contains(r, 80.0, 40.0), "a rectangle holds a point of the line")
            );
            Assert.Contains(Rows(table), r => Contains(r, 5.0, 45.0) && Contains(r, 20.0, 48.0));
            Assert.Contains(Rows(table), r => Contains(r, 95.0, 5.0) && Contains(r, 80.0, 2.0));
        }

        private static bool Contains(double[] r, double x, double y) => r[0] < x && x < r[2] && r[1] < y && y < r[3];

        [Fact]
        public void TheAnswer_DoesNotFollowTheOrderTheEntitiesWereAdded()
        {
            var random = new Random(77);
            var lines = new List<DxfLine>();

            for (int i = 0; i < 25; i++)
            {
                double x = random.Next(0, 90);
                double y = random.Next(0, 40);
                lines.Add(
                    random.Next(2) == 0 ? Line(x, y, x + random.Next(3, 10), y) : Line(x, y, x, y + random.Next(3, 10))
                );
            }

            CadDocument forward = _drawings.Blank();
            CadDocument backward = _drawings.Blank();

            foreach (DxfLine line in lines)
            {
                Put(forward, (DxfLine)line.Clone());
            }

            foreach (DxfLine line in Enumerable.Reverse(lines))
            {
                Put(backward, (DxfLine)line.Clone());
            }

            Assert.Equal(Free(forward, Frame).Boxes, Free(backward, Frame).Boxes);
        }

        [Fact]
        public void RandomLines_GiveExactlyTheMaximalEmptyRectanglesABruteForceFinds()
        {
            for (int seed = 1; seed <= 40; seed++)
            {
                var random = new Random(seed);
                CadDocument document = _drawings.Blank();
                var boxes = new List<double[]>();
                int count = random.Next(1, 9);

                for (int i = 0; i < count; i++)
                {
                    double x = random.Next(0, 20) * 5.0;
                    double y = random.Next(0, 10) * 5.0;
                    double length = random.Next(1, 8) * 5.0;
                    bool level = random.Next(2) == 0;
                    double x2 = level ? x + length : x;
                    double y2 = level ? y : y + length;
                    Put(document, Line(x, y, x2, y2));
                    boxes.Add(new[] { x, y, Math.Max(x, x2), Math.Max(y, y2) });
                }

                List<string> wanted = BruteForce(boxes)
                    .Select(r => string.Join(",", r.Select(v => v.ToString("0.###"))))
                    .OrderBy(s => s, StringComparer.Ordinal)
                    .ToList();
                List<string> got = Rows(Free(document, Frame))
                    .Select(r => string.Join(",", r.Select(v => v.ToString("0.###"))))
                    .OrderBy(s => s, StringComparer.Ordinal)
                    .ToList();

                Assert.True(
                    wanted.SequenceEqual(got),
                    "seed " + seed + ": wanted " + string.Join(" ", wanted) + " got " + string.Join(" ", got)
                );
            }
        }

        /// <summary>Every rectangle of the frame with its edges at the lines' coordinates that no line enters and that none of its four sides can move out from.</summary>
        private static List<double[]> BruteForce(List<double[]> obstacles)
        {
            var xs = new SortedSet<double> { 0.0, 100.0 };
            var ys = new SortedSet<double> { 0.0, 50.0 };

            foreach (double[] o in obstacles)
            {
                xs.Add(o[0]);
                xs.Add(o[2]);
                ys.Add(o[1]);
                ys.Add(o[3]);
            }

            double[] gx = xs.Where(v => v >= 0.0 && v <= 100.0).ToArray();
            double[] gy = ys.Where(v => v >= 0.0 && v <= 50.0).ToArray();
            var found = new List<double[]>();

            for (int a = 0; a < gx.Length; a++)
            {
                for (int b = a + 1; b < gx.Length; b++)
                {
                    for (int c = 0; c < gy.Length; c++)
                    {
                        for (int d = c + 1; d < gy.Length; d++)
                        {
                            double[] r = { gx[a], gy[c], gx[b], gy[d] };

                            if (IsFree(r, obstacles) && !CanGrow(r, gx, gy, obstacles))
                            {
                                found.Add(r);
                            }
                        }
                    }
                }
            }

            return found;
        }

        private static bool IsFree(double[] r, List<double[]> obstacles) =>
            !obstacles.Any(o => o[0] < r[2] - 1E-9 && o[2] > r[0] + 1E-9 && o[1] < r[3] - 1E-9 && o[3] > r[1] + 1E-9);

        private static bool CanGrow(double[] r, double[] gx, double[] gy, List<double[]> obstacles)
        {
            double left = gx.Where(v => v < r[0] - 1E-9).DefaultIfEmpty(double.NaN).Max();
            double right = gx.Where(v => v > r[2] + 1E-9).DefaultIfEmpty(double.NaN).Min();
            double down = gy.Where(v => v < r[1] - 1E-9).DefaultIfEmpty(double.NaN).Max();
            double up = gy.Where(v => v > r[3] + 1E-9).DefaultIfEmpty(double.NaN).Min();
            return (!double.IsNaN(left) && IsFree(new[] { left, r[1], r[2], r[3] }, obstacles))
                || (!double.IsNaN(right) && IsFree(new[] { r[0], r[1], right, r[3] }, obstacles))
                || (!double.IsNaN(down) && IsFree(new[] { r[0], down, r[2], r[3] }, obstacles))
                || (!double.IsNaN(up) && IsFree(new[] { r[0], r[1], r[2], up }, obstacles));
        }

        [Fact]
        public void ACrowdedFrame_IsRefusedInWordsUnlessALeastSizeIsGiven()
        {
            CadDocument document = _drawings.Blank();
            var random = new Random(3);

            for (int i = 0; i < 6000; i++)
            {
                double x = random.NextDouble() * 1000.0;
                double y = random.NextDouble() * 1000.0;
                Put(document, Line(x, y, x + 1.0 + random.NextDouble() * 3.0, y + 1.0 + random.NextDouble() * 3.0));
            }

            var whole = new DisplayBox(0, 0, 1000, 1000);

            InvalidOperationException crowded = Assert.Throws<InvalidOperationException>(() => Free(document, whole));
            CadFreeSpace sized = Free(document, whole, minWidth: 60.0, minHeight: 60.0);

            Assert.Contains("least width and height", crowded.Message, StringComparison.Ordinal);
            Assert.All(Rows(sized), r => Assert.True(r[2] - r[0] >= 60.0 - 1E-6 && r[3] - r[1] >= 60.0 - 1E-6));
        }

        [Fact]
        public void AFrameThatIsNoFrame_OrABadMargin_IsRefused()
        {
            CadDocument document = _drawings.Blank();

            Assert.Throws<ArgumentException>(() => Free(document, DisplayBox.Empty));
            Assert.Throws<ArgumentException>(() => Free(document, new DisplayBox(0, 0, 0, 10)));
            Assert.Throws<ArgumentException>(() => Free(document, Frame, margin: -1.0));
            Assert.Throws<ArgumentException>(() => Free(document, Frame, minWidth: double.NaN));
            Assert.Throws<ArgumentNullException>(() => CadSpatial.FreeSpace(null, null, Frame, null, Touching));
        }

        // ---- fits -------------------------------------------------------------------------------------------------------

        private bool Fits(CadDocument document, CadFrame candidate, double margin = 0.0, string[] kinds = null) =>
            CadSpatial.Fits(
                document,
                document.ModelSpace,
                candidate,
                new CadFreeSpaceOptions { Margin = margin, Kinds = kinds },
                Touching
            );

        [Fact]
        public void ACandidateBesideALine_Fits_AcrossItDoesNot_AndWithAMarginItNeedsRoom()
        {
            CadDocument document = _drawings.Blank();
            Put(document, Line(70, 0, 70, 50));
            CadFrame beside = CadFrame.FromBox(new DisplayBox(40, 10, 60, 20));
            CadFrame across = CadFrame.FromBox(new DisplayBox(60, 10, 80, 20));

            Assert.True(Fits(document, beside));
            Assert.False(Fits(document, across));
            Assert.True(Fits(document, beside, margin: 9.0));
            Assert.False(Fits(document, beside, margin: 11.0));
            Assert.True(
                Fits(document, CadFrame.FromBox(new DisplayBox(50, 10, 70, 20))),
                "touching a line is no overlap"
            );
        }

        [Fact]
        public void ACandidateIsTestedAgainstTheLineItself_NotTheBoxOfTheLine()
        {
            CadDocument document = _drawings.Blank();
            Put(document, Line(0, 30, 30, 0));
            CadFrame corner = CadFrame.FromBox(new DisplayBox(0, 0, 10, 10));
            CadFrame onIt = CadFrame.FromBox(new DisplayBox(10, 10, 25, 25));

            Assert.True(Fits(document, corner), "the line's box covers the corner, the line does not");
            Assert.False(Fits(document, onIt), "on the line");
            Assert.True(Fits(document, corner, margin: 4.9), "4.9 of room");
            Assert.False(Fits(document, corner, margin: 5.1), "5.1 of room");
        }

        [Fact]
        public void ACandidateMeetingAWordsFrame_DoesNotFit_AndOneAlongsideIt_Does()
        {
            CadDocument document = _drawings.Blank();
            DxfDBText text = Put(document, Text("SIDE FOOD", 20, 20, 0.0, 5.0));
            CadFrame frame = CadMeasure.Of(document, text).Frames.Single();
            CadFrame over = new CadFrame(frame.X + 3.0, frame.Y + 1.0, frame.Width, frame.Height, 0.0);
            CadFrame above = new CadFrame(frame.X, frame.Y + frame.Height + 2.0, frame.Width, frame.Height, 0.0);
            CadFrame turned = new CadFrame(frame.X + 1.0, frame.Y + 1.0, frame.Width, frame.Height, 0.7);

            Assert.False(Fits(document, frame), "the word's own frame is over the word");
            Assert.False(Fits(document, over));
            Assert.True(Fits(document, above));
            Assert.False(Fits(document, above, margin: 3.0));
            Assert.False(Fits(document, turned));
        }

        [Fact]
        public void OnlyTheKindsAskedAboutAreInTheWay()
        {
            CadDocument document = _drawings.Blank();
            Put(document, Line(70, 0, 70, 50));
            CadFrame across = CadFrame.FromBox(new DisplayBox(60, 10, 80, 20));

            Assert.False(Fits(document, across, kinds: new[] { "LINE" }));
            Assert.True(Fits(document, across, kinds: new[] { "TEXT" }));
        }

        [Fact]
        public void AFitsTrueCandidate_LiesInsideOneOfTheFreeRectangles()
        {
            var random = new Random(5);
            CadDocument document = _drawings.Blank();

            for (int i = 0; i < 30; i++)
            {
                double x = random.Next(0, 90);
                double y = random.Next(0, 45);
                Put(document, random.Next(2) == 0 ? Line(x, y, x + 8, y) : Line(x, y, x, y + 8));
            }

            List<double[]> free = Rows(Free(document, Frame));

            for (int i = 0; i < 300; i++)
            {
                double w = 1.0 + random.NextDouble() * 9.0;
                double h = 1.0 + random.NextDouble() * 5.0;
                double x = random.NextDouble() * (100.0 - w);
                double y = random.NextDouble() * (50.0 - h);
                bool fits = Fits(document, CadFrame.FromBox(new DisplayBox(x, y, x + w, y + h)));
                bool inside = free.Any(r =>
                    r[0] <= x + 1E-6 && r[1] <= y + 1E-6 && r[2] >= x + w - 1E-6 && r[3] >= y + h - 1E-6
                );

                Assert.True(
                    inside == fits,
                    $"candidate ({x}, {y}) {w} by {h}: fits {fits}, in a free rectangle {inside}"
                );
            }
        }
    }
}
