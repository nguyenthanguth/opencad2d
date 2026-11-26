using System;
using System.Collections.Generic;
using System.Linq;
using GeometryHelper;
using GeometryHelper.Dxf;
using GeometryHelper.Geometry;
using Xunit;
using static OpenCAD2D.UnitTest.EditingDrawings;
using static OpenCAD2D.UnitTest.SpatialDrawings;

namespace OpenCAD2D.UnitTest
{
    /// <summary>
    /// <see cref="CadSpatial.Overlaps"/>: a word is the frame of a text, turned as the text is; it overlaps a line, an arc or a
    /// fill that runs through it, and the frame of another word it shares an area with; it does not overlap what only touches it
    /// within the tolerance, nor a part of its own entity. Each test takes the frame of a text from a drawing of that text alone
    /// and puts the other thing at a place measured from it, in an SHX font of the test's own so that the frame is the display's.
    /// </summary>
    public sealed class SpatialOverlapTests : IDisposable
    {
        private static readonly Tolerance Mm = new Tolerance(1E-3, 1E-5);

        private readonly SpatialDrawings _drawings = new SpatialDrawings();

        public void Dispose() => _drawings.Dispose();

        private static CadOverlapTable Overlaps(
            CadDocument document,
            CadOverlapOptions options = null,
            CadSpace space = null
        ) => CadSpatial.Overlaps(document, space ?? document.ModelSpace, options, Mm);

        /// <summary>The middle of a frame, in the drawing.</summary>
        private static void Middle(CadTextFrame f, out double x, out double y)
        {
            double mx = (f.X0 + f.X1) / 2.0;
            double my = (f.Y0 + f.Y1) / 2.0;
            x = f.X + mx * f.Cos - my * f.Sin;
            y = f.Y + mx * f.Sin + my * f.Cos;
        }

        [Fact]
        public void ALineThroughATextsFrame_IsOneRow_WhoseBoxIsThePartOfTheLineInTheFrame()
        {
            CadTextFrame frame = _drawings.FrameOf("SIDE FOOD", 10, 20);
            double across = frame.X + (frame.X0 + frame.X1) / 2.0;
            CadDocument document = _drawings.Blank();
            DxfDBText text = Put(document, Text("SIDE FOOD", 10, 20));
            DxfLine line = Put(document, Line(across, 0, across, 60));

            CadOverlapTable table = Overlaps(document);

            Assert.Equal(1, table.Count);
            Assert.Equal(text.Handle, table.Words[0]);
            Assert.Equal(line.Handle, table.Others[0]);
            DisplayBox box = table.BoxOf(0);
            Assert.Equal(across, box.MinX, 9);
            Assert.Equal(across, box.MaxX, 9);
            Assert.Equal(frame.Y + frame.Y0 + 1E-3, box.MinY, 9);
            Assert.Equal(frame.Y + frame.Y1 - 1E-3, box.MaxY, 9);
        }

        [Fact]
        public void ALineThatOnlyTouchesTheFrameWithinTheTolerance_IsNoOverlap_ButOneThatEntersIsOne()
        {
            CadTextFrame frame = _drawings.FrameOf("SIDE FOOD", 10, 20);
            double top = frame.Y + frame.Y1;
            CadDocument document = _drawings.Blank();
            Put(document, Text("SIDE FOOD", 10, 20));
            Put(document, Line(0, top, 100, top));
            Put(document, Line(0, top - 0.0005, 100, top - 0.0005));
            Put(document, Line(0, frame.Y + frame.Y0 + 0.0005, 100, frame.Y + frame.Y0 + 0.0005));

            Assert.Equal(0, Overlaps(document).Count);

            CadDocument entering = _drawings.Blank();
            Put(entering, Text("SIDE FOOD", 10, 20));
            DxfLine inside = Put(entering, Line(0, top - 0.01, 100, top - 0.01));

            Assert.Equal(new[] { inside.Handle }, Overlaps(entering).Others);
        }

        [Fact]
        public void ATurnedText_IsNotItsBox_ALineThroughTheEmptyCornerOfTheBoxMissesIt()
        {
            CadTextFrame frame = _drawings.FrameOf("SIDE FOOD", 0, 0, 45.0, 5.0);
            Middle(frame, out double cx, out double cy);
            DisplayBox box = frame.Box();
            CadDocument document = _drawings.Blank();
            Put(document, Text("SIDE FOOD", 0, 0, 45.0, 5.0));

            // A level stroke at the top left corner of the box, which the text, running up at 45 degrees, leaves empty.
            DxfLine miss = Put(document, Line(box.MinX, box.MaxY, box.MinX + 3, box.MaxY));
            // A stroke across the middle of the frame, square to the text.
            DxfLine hit = Put(document, Line(cx - 3, cy + 3, cx + 3, cy - 3));

            CadOverlapTable table = Overlaps(document);

            Assert.Equal(new[] { hit.Handle }, table.Others);
            Assert.DoesNotContain(miss.Handle, table.Others);
        }

        [Fact]
        public void TwoTextsThatShareAnArea_AreOneRowWithTheFirstDrawnAsTheWord_AndTwoThatOnlyAbutAreNone()
        {
            CadTextFrame frame = _drawings.FrameOf("SIDE FOOD", 0, 0);
            double right = frame.X + frame.X1;
            CadDocument abut = _drawings.Blank();
            Put(abut, Text("SIDE FOOD", 0, 0));
            Put(abut, Text("SIDE", right, 0));

            Assert.Equal(0, Overlaps(abut).Count);

            CadDocument document = _drawings.Blank();
            DxfDBText first = Put(document, Text("SIDE FOOD", 0, 0));
            DxfDBText over = Put(document, Text("SIDE", right - 4.0, 1.0));
            CadOverlapTable table = Overlaps(document);

            Assert.Equal(1, table.Count);
            Assert.Equal(new[] { first.Handle }, table.Words);
            Assert.Equal(new[] { over.Handle }, table.Others);
            DisplayBox box = table.BoxOf(0);
            Assert.True(box.Width > 3.0 && box.Width < 4.01, "the two share the end of the first: " + box.Width);
        }

        [Fact]
        public void ADimensionsOwnLinesAreNotOverIts_ButAnotherEntitysLineThroughItsWordsIs()
        {
            CadDocument document = _drawings.Blank();
            CadCommandSession session = Session(document);
            Run(session, "DLI", P(0, 0), P(300, 0), P(150, -40));
            DxfRotatedDimension dimension = All<DxfRotatedDimension>(document.ModelSpace).Single();

            Assert.Equal(0, Overlaps(document).Count);

            DisplayText2 words = document
                .GetDisplay(document.ModelSpace)
                .Items.OfType<DisplayText2>()
                .Single(t => t.Handle == dimension.Handle && t.Parts != null);
            double middle = (words.Bounds.MinX + words.Bounds.MaxX) / 2.0;
            Run(session, "LINE", P(middle, words.Bounds.MinY - 10), P(middle, words.Bounds.MaxY + 10), Enter);
            DxfLine through = All<DxfLine>(document.ModelSpace).Single();
            CadOverlapTable table = Overlaps(document);

            Assert.Equal(new[] { dimension.Handle }, table.Words);
            Assert.Equal(new[] { through.Handle }, table.Others);
        }

        [Fact]
        public void AMultileadersWords_AreWordsToo_AndALineThroughThemIsOverThem()
        {
            CadDocument document = BlankWithMLeaderStyle();
            CadCommandSession session = Session(document);
            Run(session, "MLD", P(0, 0), P(30, 20), "B-12");
            DxfMLeader leader = All<DxfMLeader>(document.ModelSpace).Single();
            Assert.Equal(0, Overlaps(document).Count);

            DisplayText2 words = document
                .GetDisplay(document.ModelSpace)
                .Items.OfType<DisplayText2>()
                .First(t => t.Handle == leader.Handle);
            double middle = (words.Bounds.MinX + words.Bounds.MaxX) / 2.0;
            Run(session, "LINE", P(middle, words.Bounds.MinY - 5), P(middle, words.Bounds.MaxY + 5), Enter);
            DxfLine through = All<DxfLine>(document.ModelSpace).Single();

            CadOverlapTable table = Overlaps(document);

            Assert.Equal(new[] { leader.Handle }, table.Words);
            Assert.Equal(new[] { through.Handle }, table.Others);
            Assert.Equal(table.Words, Overlaps(document, new CadOverlapOptions { Words = new[] { "mleader" } }).Words);
        }

        [Fact]
        public void TheKindsFilterNamesTheWordsAndWhatTheyAreOver()
        {
            CadTextFrame frame = _drawings.FrameOf("SIDE FOOD", 10, 20);
            double across = frame.X + (frame.X0 + frame.X1) / 2.0;
            CadDocument document = _drawings.Blank();
            Put(document, Text("SIDE FOOD", 10, 20));
            DxfLine line = Put(document, Line(across, 0, across, 60));
            DxfCircle circle = Put(document, Circle(across, frame.Y + frame.Y1 / 2.0, (frame.X1 - frame.X0) / 4.0));

            CadOverlapTable all = Overlaps(document);
            CadOverlapTable onlyCircles = Overlaps(document, new CadOverlapOptions { Over = new[] { "circle" } });
            CadOverlapTable onlyMText = Overlaps(document, new CadOverlapOptions { Words = new[] { "MTEXT" } });
            CadOverlapTable linesAreWords = Overlaps(document, new CadOverlapOptions { Words = new[] { "LINE" } });

            Assert.Equal(new[] { line.Handle, circle.Handle }, all.Others);
            Assert.Equal(new[] { circle.Handle }, onlyCircles.Others);
            Assert.Equal(0, onlyMText.Count);
            Assert.Equal(0, linesAreWords.Count);
        }

        [Fact]
        public void TheHandlesFilter_GivesTheRowsOfThoseEntitiesWhicheverSideTheyAreOn()
        {
            CadTextFrame frame = _drawings.FrameOf("SIDE FOOD", 10, 20);
            double across = frame.X + (frame.X0 + frame.X1) / 2.0;
            CadDocument document = _drawings.Blank();
            DxfDBText text = Put(document, Text("SIDE FOOD", 10, 20));
            DxfDBText far = Put(document, Text("FOOD", 300, 300));
            DxfLine line = Put(document, Line(across, 0, across, 60));
            DxfLine farLine = Put(document, Line(300 + 5, 290, 300 + 5, 330));

            CadOverlapTable everything = Overlaps(document);
            CadOverlapTable byLine = Overlaps(document, new CadOverlapOptions { Handles = new[] { line.Handle } });
            CadOverlapTable byText = Overlaps(document, new CadOverlapOptions { Handles = new[] { far.Handle } });
            CadOverlapTable byNothing = Overlaps(document, new CadOverlapOptions { Handles = new[] { "FFFF" } });

            Assert.Equal(2, everything.Count);
            Assert.Equal(new[] { text.Handle }, byLine.Words);
            Assert.Equal(new[] { line.Handle }, byLine.Others);
            Assert.Equal(new[] { far.Handle }, byText.Words);
            Assert.Equal(new[] { farLine.Handle }, byText.Others);
            Assert.Equal(0, byNothing.Count);
        }

        [Fact]
        public void ATextInsideAFilledArea_OverlapsIt_AndOneBesideItDoesNot()
        {
            CadDocument document = _drawings.Blank();
            var hatch = new DxfHatch();
            hatch.SetHatchPattern(DxfHatchPatternType.PreDefined, "SOLID");
            hatch.AppendLoop(
                DxfHatchLoopTypes.External,
                new[] { new GeoPoint2(0, 0), new GeoPoint2(100, 0), new GeoPoint2(100, 40), new GeoPoint2(0, 40) },
                null
            );
            Put(document, hatch);
            DxfDBText inside = Put(document, Text("FOOD", 40, 15));
            DxfDBText beside = Put(document, Text("FOOD", 150, 15));

            CadOverlapTable table = Overlaps(document);

            Assert.Equal(new[] { inside.Handle }, table.Words);
            Assert.Equal(new[] { hatch.Handle }, table.Others);
            Assert.DoesNotContain(beside.Handle, table.Words);
        }

        [Fact]
        public void AnArcThroughTheFrame_IsOver_AndOneAroundItIsNot()
        {
            CadTextFrame frame = _drawings.FrameOf("SIDE FOOD", 0, 0);
            Middle(frame, out double cx, out double cy);
            CadDocument document = _drawings.Blank();
            Put(document, Text("SIDE FOOD", 0, 0));
            DxfCircle through = Put(document, Circle(cx, cy - 30, 30.0));
            DxfCircle around = Put(document, Circle(cx, cy, 100.0));

            CadOverlapTable table = Overlaps(document);

            Assert.Equal(new[] { through.Handle }, table.Others);
            Assert.DoesNotContain(around.Handle, table.Others);
        }

        [Fact]
        public void AnMTextIsFramesRunByRun_SoALineThroughTheGapBetweenColumnsIsClear()
        {
            CadDocument scratch = _drawings.Blank();
            DxfMText measured = Put(scratch, Columns());
            DisplayText2[] runs = scratch
                .GetDisplay(scratch.ModelSpace)
                .Items.OfType<DisplayText2>()
                .SelectMany(t => t.Parts == null ? new[] { t } : t.Parts.OfType<DisplayText2>().ToArray())
                .ToArray();
            double leftEnd = runs.Where(r => r.X < 65.0).Max(r => r.Bounds.MaxX);
            double rightStart = runs.Where(r => r.X >= 65.0).Min(r => r.Bounds.MinX);
            Assert.True(rightStart - leftEnd > 4.0, "the columns leave a gap: " + leftEnd + " to " + rightStart);
            double gap = (leftEnd + rightStart) / 2.0;
            Assert.NotNull(measured);

            CadDocument clear = _drawings.Blank();
            Put(clear, Columns());
            Put(clear, Line(gap, -100, gap, 100));

            Assert.Equal(0, Overlaps(clear).Count);

            CadDocument through = _drawings.Blank();
            DxfMText mtext = Put(through, Columns());
            DxfLine column = Put(through, Line(leftEnd - 2.0, -100, leftEnd - 2.0, 100));
            CadOverlapTable table = Overlaps(through);

            Assert.Equal(new[] { mtext.Handle }, table.Words);
            Assert.Equal(new[] { column.Handle }, table.Others);
        }

        /// <summary>An MTEXT 5 high of four lines, in two columns 60 wide a gutter of 40 apart, two lines to a column.</summary>
        private static DxfMText Columns()
        {
            var mtext = new DxfMText
            {
                Contents = @"SIDE FOOD\PFOOD SIDE\PSIDE SIDE\PFOOD FOOD",
                Location = new GeoPoint3(0, 0, 0),
                TextHeight = 5.0,
                Width = 60.0,
                Height = 20.0,
            };
            mtext.SetStaticColumns(60.0, 40.0, 2);
            return mtext;
        }

        [Fact]
        public void AnInsertsOwnLinesAreNotOverItsWords_ButAnotherEntitysAre_WhenInsertIsAKindOfWords()
        {
            CadDocument document = _drawings.Blank();
            var block = new DxfBlockTableRecord { Name = "TAG" };
            document.Database.BlockTable.Add(block);
            block.AppendEntity(Line(0, 0, 40, 0));
            var label = Text("SIDE", 2, -1);
            block.AppendEntity(label);
            label.TextStyleName = "OC_SHX";
            DxfBlockReference insert = Put(document, new DxfBlockReference(new GeoPoint3(0, 0, 0), block));
            DxfLine over = Put(document, Line(10, -10, 10, 10));

            Assert.Equal(0, Overlaps(document).Count);

            CadOverlapTable table = Overlaps(document, new CadOverlapOptions { Words = new[] { "INSERT" } });

            Assert.Equal(new[] { insert.Handle }, table.Words);
            Assert.Equal(new[] { over.Handle }, table.Others);
        }

        [Fact]
        public void ALayoutIsAskedOnItsOwn_ModelSpaceHasNoneOfItsRows()
        {
            CadDocument document = _drawings.Blank();
            DxfLine onSheet = AddLineOnSheet(document, out CadSpace sheet);
            DxfDBText note = Text("SIDE FOOD", 20, -2);
            sheet.Block.AppendEntity(note);
            note.TextStyleName = "OC_SHX";
            Put(document, Text("SIDE FOOD", 20, -2));

            CadOverlapTable layout = Overlaps(document, space: sheet);
            CadOverlapTable model = Overlaps(document);

            Assert.Equal(new[] { note.Handle }, layout.Words);
            Assert.Equal(new[] { onSheet.Handle }, layout.Others);
            Assert.Equal(0, model.Count);
        }

        [Fact]
        public void ARandomDrawing_GivesTheRowsOfTheBruteForceOverEveryPairOfItems_TwiceTheSame()
        {
            CadDocument document = _drawings.Blank();
            var random = new Random(20260401);

            for (int i = 0; i < 400; i++)
            {
                double x = random.NextDouble() * 400.0;
                double y = random.NextDouble() * 400.0;

                switch (i % 4)
                {
                    case 0:
                        Put(
                            document,
                            Text(
                                "SIDE" + (i % 3 == 0 ? " FOOD" : string.Empty),
                                x,
                                y,
                                random.NextDouble() * 90.0,
                                2.0 + random.NextDouble() * 4.0
                            )
                        );
                        break;
                    case 1:
                        Put(
                            document,
                            Line(x, y, x + random.NextDouble() * 60.0 - 30.0, y + random.NextDouble() * 60.0 - 30.0)
                        );
                        break;
                    case 2:
                        Put(document, Circle(x, y, 1.0 + random.NextDouble() * 20.0));
                        break;
                    default:
                        Put(
                            document,
                            Line(x, y, x + random.NextDouble() * 20.0 - 10.0, y + random.NextDouble() * 20.0 - 10.0)
                        );
                        break;
                }
            }

            CadOverlapTable first = Overlaps(document);
            CadOverlapTable second = Overlaps(document);
            List<string> expected = BruteForce(document);

            Assert.True(first.Count > 20, "a drawing this dense has overlaps: " + first.Count);
            Assert.Equal(first.Words, second.Words);
            Assert.Equal(first.Others, second.Others);
            Assert.Equal(first.Boxes, second.Boxes);
            Assert.Equal(
                expected,
                first.Words.Zip(first.Others, (w, o) => w + ">" + o).OrderBy(p => p, StringComparer.Ordinal).ToList()
            );
        }

        /// <summary>Every (word, other) pair of entities by testing every text item against every other item directly, with no tree; sorted.</summary>
        private static List<string> BruteForce(CadDocument document)
        {
            DisplayList2 list = document.GetDisplay(document.ModelSpace);
            var pairs = new HashSet<string>(StringComparer.Ordinal);
            List<DisplayItem2> items = list.Items.ToList();
            List<string> order = items.Select(i => i.Handle).Distinct().ToList();
            double margin = Mm.EqualPoint;

            foreach (DisplayText2 word in items.OfType<DisplayText2>())
            {
                CadTextFrame frame = CadTextFrame.Of(word);

                foreach (DisplayItem2 other in items)
                {
                    if (other.Handle == word.Handle)
                    {
                        continue;
                    }

                    CadMeeting meeting = CadMeeting.None;

                    if (other is DisplayText2 text)
                    {
                        if (CadSpatialShapes.MeetFrames(frame, CadTextFrame.Of(text), margin, out meeting))
                        {
                            bool wordFirst = order.IndexOf(word.Handle) < order.IndexOf(text.Handle);
                            pairs.Add(wordFirst ? word.Handle + ">" + text.Handle : text.Handle + ">" + word.Handle);
                        }

                        continue;
                    }

                    if (other is DisplayPolyline2 chain)
                    {
                        var xy = new double[chain.Count * 2];

                        for (int i = 0; i < chain.Count; i++)
                        {
                            xy[2 * i] = chain.VertexX(i);
                            xy[2 * i + 1] = chain.VertexY(i);
                        }

                        CadSpatialShapes.ClipChain(frame, margin, xy, chain.Count, chain.Closed, ref meeting);
                    }
                    else if (other is DisplayArc2 arc)
                    {
                        double[] flat = arc.Flatten(margin);
                        CadSpatialShapes.ClipChain(frame, margin, flat, flat.Length / 2, false, ref meeting);
                    }

                    if (meeting.Any)
                    {
                        pairs.Add(word.Handle + ">" + other.Handle);
                    }
                }
            }

            return pairs.OrderBy(p => p, StringComparer.Ordinal).ToList();
        }

        [Fact]
        public void ANullDocumentOrSpace_IsRefused()
        {
            CadDocument document = _drawings.Blank();

            Assert.Throws<ArgumentNullException>(() => CadSpatial.Overlaps(null, document.ModelSpace, null, Mm));
            Assert.Throws<ArgumentNullException>(() => CadSpatial.Overlaps(document, null, null, Mm));
        }
    }
}
