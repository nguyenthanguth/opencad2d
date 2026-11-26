using System;
using System.Collections.Generic;
using System.Linq;
using GeometryHelper;
using GeometryHelper.Dxf;
using GeometryHelper.Geometry;
using Xunit;
using static OpenCAD2D.UnitTest.EditingDrawings;

namespace OpenCAD2D.UnitTest
{
    /// <summary>
    /// HATCH follow-ups, round five: -HATCH's draW boundary keeps the order the shapes were drawn in and flags each loop by how
    /// deep it lies, follows the retained polylines where the hatch is associative, and keeps an open shape beside a closed one as
    /// a loop that is not closed. The flags, orders and handles are those AutoCAD 2021's console gave in probes.
    /// </summary>
    public class HatchFollowUpsFiveTests
    {
        private const DxfHatchLoopTypes Outermost = DxfHatchLoopTypes.Outermost;

        private static CadCommandSession Start(CadDocument document, params object[] inputs)
        {
            CadCommandSession session = Session(document);
            session.Tolerance = Tolerance.Default;
            return Run(session, inputs);
        }

        private static DxfHatch OnlyHatch(CadDocument document) => Assert.Single(All<DxfHatch>(document.ModelSpace));

        /// <summary>The corners of a square drawn counter-clockwise and closed.</summary>
        private static object[] Square(double x0, double y0, double x1, double y1) =>
            new object[] { P(x0, y0), P(x1, y0), P(x1, y1), P(x0, y1), "C" };

        /// <summary>The inputs of -HATCH's draW boundary with a pattern of solid, the shapes, and the two Enters that end it.</summary>
        private static object[] Draw(string retain, params object[][] shapes) =>
            new object[] { "-HATCH", "P", "SOLID", "W", retain }
                .Concat(shapes.SelectMany(s => s))
                .Concat(new object[] { Enter, Enter })
                .ToArray();

        private static List<DxfHatchLoopTypes> Flags(DxfHatch hatch) =>
            Enumerable.Range(0, hatch.NumberOfLoops).Select(hatch.LoopTypeAt).ToList();

        /// <summary>The flags of a loop without the bit that says it is a polyline, which the library writes its closed loops with.</summary>
        private static DxfHatchLoopTypes Kind(DxfHatch hatch, int index) =>
            hatch.LoopTypeAt(index) & ~DxfHatchLoopTypes.Polyline;

        private static GeoPoint3 FirstPoint(DxfHatch hatch, int index)
        {
            DxfHatchLoop loop = hatch.GetLoopAt(index);

            switch (loop.Polyline)
            {
                case GeoPolygonArc3 polygon:
                    return polygon.Vertices[0];
                case GeoPolylineArc3 chain:
                    return chain.Vertices[0];
                default:
                    return ((GeoLine3)loop.Curves[0]).StartPoint;
            }
        }

        #region Drawing order and flags

        [Fact]
        public void DrawBoundary_AnIslandDrawnFirst_IsLoopZero_AsAutoCadKeepsTheDrawingOrder()
        {
            // AutoCAD 2021: the square (10,10)-(20,20) drawn first and the square (0,0)-(50,50) second make loop 0 the small one,
            // flagged Outermost (16), and loop 1 the big one, flagged External (1).
            CadDocument document = Blank();
            Start(document, Draw("N", Square(10, 10, 20, 20), Square(0, 0, 50, 50)));

            DxfHatch hatch = OnlyHatch(document);
            Assert.Equal(2, hatch.NumberOfLoops);
            Assert.Equal(new GeoPoint3(10, 10, 0), FirstPoint(hatch, 0));
            Assert.Equal(new GeoPoint3(0, 0, 0), FirstPoint(hatch, 1));
            Assert.Equal(Outermost, Kind(hatch, 0));
            Assert.Equal(DxfHatchLoopTypes.External, Kind(hatch, 1));
        }

        [Fact]
        public void DrawBoundary_TheBigSquareDrawnFirst_IsLoopZero_AndTheIslandInsideItIsOutermost()
        {
            CadDocument document = Blank();
            Start(document, Draw("N", Square(0, 0, 50, 50), Square(10, 10, 20, 20)));

            DxfHatch hatch = OnlyHatch(document);
            Assert.Equal(new GeoPoint3(0, 0, 0), FirstPoint(hatch, 0));
            Assert.Equal(DxfHatchLoopTypes.External, Kind(hatch, 0));
            Assert.Equal(Outermost, Kind(hatch, 1));
        }

        [Fact]
        public void DrawBoundary_ThreeNestedSquaresDrawnSmallestBigThenMiddle_AreFlaggedByTheirDepthInThatOrder()
        {
            // AutoCAD 2021: depth 0 is External, depth 1 Outermost, deeper ones none.
            CadDocument document = Blank();
            Start(document, Draw("N", Square(20, 20, 25, 25), Square(0, 0, 50, 50), Square(10, 10, 30, 30)));

            DxfHatch hatch = OnlyHatch(document);
            Assert.Equal(new GeoPoint3(20, 20, 0), FirstPoint(hatch, 0));
            Assert.Equal(
                new[] { DxfHatchLoopTypes.Default, DxfHatchLoopTypes.External, Outermost },
                new[] { Kind(hatch, 0), Kind(hatch, 1), Kind(hatch, 2) }
            );
        }

        [Fact]
        public void DrawBoundary_FiveNestedSquaresDrawnOutwardsIn_AreExternalOutermostAndThenNone()
        {
            CadDocument document = Blank();
            Start(
                document,
                Draw(
                    "N",
                    Square(0, 0, 80, 80),
                    Square(5, 5, 75, 75),
                    Square(10, 10, 70, 70),
                    Square(15, 15, 65, 65),
                    Square(20, 20, 60, 60)
                )
            );

            DxfHatch hatch = OnlyHatch(document);
            Assert.Equal(
                new[]
                {
                    DxfHatchLoopTypes.External,
                    Outermost,
                    DxfHatchLoopTypes.Default,
                    DxfHatchLoopTypes.Default,
                    DxfHatchLoopTypes.Default,
                },
                Enumerable.Range(0, 5).Select(i => Kind(hatch, i)).ToArray()
            );
        }

        [Fact]
        public void DrawBoundary_TwoSeparateSquares_AreBothExternal_InTheOrderDrawn()
        {
            CadDocument document = Blank();
            Start(document, Draw("N", Square(30, 0, 50, 20), Square(0, 0, 20, 20)));

            DxfHatch hatch = OnlyHatch(document);
            Assert.Equal(new GeoPoint3(30, 0, 0), FirstPoint(hatch, 0));
            Assert.Equal(DxfHatchLoopTypes.External, Kind(hatch, 0));
            Assert.Equal(DxfHatchLoopTypes.External, Kind(hatch, 1));
        }

        [Theory]
        [InlineData(0.0, 0.0, 60.0, 60.0, 40.0, 40.0, 90.0, 90.0)]
        [InlineData(40.0, 40.0, 90.0, 90.0, 0.0, 0.0, 60.0, 60.0)]
        public void DrawBoundary_TwoSquaresThatOverlap_AreEachExternal(
            double ax0,
            double ay0,
            double ax1,
            double ay1,
            double bx0,
            double by0,
            double bx1,
            double by1
        )
        {
            // AutoCAD 2021: neither square lies inside the other, whichever was drawn first and whichever has the corner inside
            // the other.
            CadDocument document = Blank();
            Start(document, Draw("N", Square(ax0, ay0, ax1, ay1), Square(bx0, by0, bx1, by1)));

            DxfHatch hatch = OnlyHatch(document);
            Assert.Equal(DxfHatchLoopTypes.External, Kind(hatch, 0));
            Assert.Equal(DxfHatchLoopTypes.External, Kind(hatch, 1));
        }

        [Fact]
        public void DrawBoundary_ASquareThatSharesAnEdgeWithTheOuterOne_IsAnIslandInIt()
        {
            CadDocument document = Blank();
            Start(document, Draw("N", Square(0, 0, 60, 60), Square(10, 10, 60, 40)));

            DxfHatch hatch = OnlyHatch(document);
            Assert.Equal(DxfHatchLoopTypes.External, Kind(hatch, 0));
            Assert.Equal(Outermost, Kind(hatch, 1));
        }

        [Fact]
        public void DrawBoundary_AShapeDrawnClockwise_KeepsItsDirectionInTheHatchAndThePolylineRetained()
        {
            // AutoCAD 2021 keeps the corners as drawn: (0,0), (0,50), (50,50), (50,0) stays so in the loop and the polyline.
            CadDocument document = Blank();
            Start(document, Draw("Y", new object[] { P(0, 0), P(0, 50), P(50, 50), P(50, 0), "C" }));

            DxfPolyline kept = Assert.Single(All<DxfPolyline>(document.ModelSpace));
            Assert.Equal(new GeoPoint2(0, 50), kept.GetPoint2dAt(1));
            DxfHatch hatch = OnlyHatch(document);
            Assert.Equal(new GeoPoint3(0, 0, 0), FirstPoint(hatch, 0));
            var polygon = Assert.IsType<GeoPolygonArc3>(hatch.GetLoopAt(0).Polyline);
            Assert.Equal(new GeoPoint3(0, 50, 0), polygon.Vertices[1]);
        }

        #endregion

        #region Retain and association

        [Fact]
        public void DrawBoundary_RetainedUnderAssociativity_MakesTheHatchFollowThePolyline()
        {
            // AutoCAD 2021, HPASSOC 1, Retain Y: the hatch is associative (71 1) and its loop follows the polyline kept (97 1 and
            // its handle); HPASSOC stays 1.
            CadDocument document = Blank();
            Assert.True(document.HatchSettings.Associative);
            Start(document, Draw("Y", Square(0, 0, 50, 50)));

            DxfHatch hatch = OnlyHatch(document);
            DxfPolyline kept = Assert.Single(All<DxfPolyline>(document.ModelSpace));
            Assert.True(hatch.Associative);
            Assert.Equal(new[] { kept.Handle }, hatch.GetAssociatedObjectsAt(0).Select(e => e.Handle));
            Assert.True(document.HatchSettings.Associative);
        }

        [Fact]
        public void DrawBoundary_RetainedUnderAssociativity_FollowsEachLoopsOwnPolyline_InTheDrawingOrder()
        {
            CadDocument document = Blank();
            Start(document, Draw("Y", Square(10, 10, 20, 20), Square(0, 0, 50, 50)));

            DxfHatch hatch = OnlyHatch(document);
            List<DxfPolyline> kept = All<DxfPolyline>(document.ModelSpace);
            Assert.Equal(2, kept.Count);
            Assert.True(hatch.Associative);
            Assert.Equal(new[] { kept[0].Handle }, hatch.GetAssociatedObjectsAt(0).Select(e => e.Handle));
            Assert.Equal(new[] { kept[1].Handle }, hatch.GetAssociatedObjectsAt(1).Select(e => e.Handle));
            Assert.Equal(new GeoPoint2(10, 10), kept[0].GetPoint2dAt(0));
        }

        [Fact]
        public void DrawBoundary_Retained_PutsThePolylinesBeforeTheHatchInTheSpace_AsAutoCadDoes()
        {
            CadDocument document = Blank();
            Start(document, Draw("Y", Square(0, 0, 50, 50), Square(10, 10, 20, 20)));

            List<string> kinds = document.ModelSpace.Block.Select(e => e.GetType().Name).ToList();
            Assert.Equal(new[] { "DxfPolyline", "DxfPolyline", "DxfHatch" }, kinds);
        }

        [Fact]
        public void DrawBoundary_NotRetained_UnderAssociativity_MakesNoAssociativeHatch_AndTurnsAssociativityOff()
        {
            // AutoCAD 2021, HPASSOC 1, Retain N: the hatch is not associative and HPASSOC is 0 afterwards.
            CadDocument document = Blank();
            Start(document, Draw("N", Square(0, 0, 50, 50)));

            Assert.False(OnlyHatch(document).Associative);
            Assert.False(document.HatchSettings.Associative);
        }

        [Fact]
        public void DrawBoundary_Retained_WithAssociativityOff_MakesNoAssociativeHatch_AndLeavesTheSettingOff()
        {
            CadDocument document = Blank();
            CadHatchSettings settings = document.HatchSettings.Clone();
            settings.Associative = false;
            document.HatchSettings = settings;
            Start(document, Draw("Y", Square(0, 0, 50, 50)));

            Assert.False(OnlyHatch(document).Associative);
            Assert.Single(All<DxfPolyline>(document.ModelSpace));
            Assert.False(document.HatchSettings.Associative);
        }

        #endregion

        #region Open shapes

        [Fact]
        public void DrawBoundary_AnOpenTwoPointShapeBesideASquare_IsAOneEdgeLoopThatIsNotClosed()
        {
            // AutoCAD 2021: the square, then (60,0)-(80,0) ended with Enter: two loops, the second flagged External | NotClosed
            // (33) with one line edge from (60,0) to (80,0).
            CadDocument document = Blank();
            Start(document, Draw("N", Square(0, 0, 50, 50), new object[] { P(60, 0), P(80, 0), Enter }));

            DxfHatch hatch = OnlyHatch(document);
            Assert.Equal(2, hatch.NumberOfLoops);
            Assert.Equal(DxfHatchLoopTypes.External, Kind(hatch, 0));
            Assert.Equal(DxfHatchLoopTypes.External | DxfHatchLoopTypes.NotClosed, Kind(hatch, 1));
            DxfHatchLoop open = hatch.GetLoopAt(1);
            Assert.False(open.IsPolyline);
            GeoLine3 line = Assert.IsType<GeoLine3>(Assert.Single(open.Curves));
            Assert.Equal(new GeoPoint3(60, 0, 0), line.StartPoint);
            Assert.Equal(new GeoPoint3(80, 0, 0), line.EndPoint);
        }

        [Fact]
        public void DrawBoundary_AnOpenThreePointShapeBesideASquare_IsNotClosedAcross()
        {
            // AutoCAD 2021: two edges (60,0)-(80,0) and (80,0)-(70,20), no third edge back to the start.
            CadDocument document = Blank();
            Start(document, Draw("N", Square(0, 0, 50, 50), new object[] { P(60, 0), P(80, 0), P(70, 20), Enter }));

            DxfHatch hatch = OnlyHatch(document);
            Assert.Equal(DxfHatchLoopTypes.External | DxfHatchLoopTypes.NotClosed, Kind(hatch, 1));
            Assert.Equal(2, hatch.GetLoopAt(1).Curves.Count);
        }

        [Fact]
        public void DrawBoundary_AnOpenShapeDrawnFirst_KeepsItsPlaceBeforeTheSquare()
        {
            CadDocument document = Blank();
            Start(document, Draw("N", new object[] { P(60, 0), P(80, 0), Enter }, Square(0, 0, 50, 50)));

            DxfHatch hatch = OnlyHatch(document);
            Assert.Equal(DxfHatchLoopTypes.External | DxfHatchLoopTypes.NotClosed, Kind(hatch, 0));
            Assert.Equal(DxfHatchLoopTypes.External, Kind(hatch, 1));
        }

        [Fact]
        public void DrawBoundary_AnOpenShapeInsideASquare_IsOutermostAndNotClosed()
        {
            // AutoCAD 2021: flag 48, whether it was drawn before the square or after it.
            CadDocument after = Blank();
            Start(after, Draw("N", Square(0, 0, 50, 50), new object[] { P(10, 10), P(30, 10), Enter }));
            Assert.Equal(Outermost | DxfHatchLoopTypes.NotClosed, Kind(OnlyHatch(after), 1));

            CadDocument before = Blank();
            Start(before, Draw("N", new object[] { P(10, 10), P(30, 10), Enter }, Square(0, 0, 50, 50)));
            Assert.Equal(Outermost | DxfHatchLoopTypes.NotClosed, Kind(OnlyHatch(before), 0));
        }

        [Fact]
        public void DrawBoundary_AClosedSquareInsideAnOpenTriangle_IsOutermost_AndTheOpenOneExternalNotClosed()
        {
            // AutoCAD 2021: the open shape counts, closed across, for what lies inside it.
            CadDocument document = Blank();
            Start(document, Draw("N", new object[] { P(0, 0), P(100, 0), P(50, 80), Enter }, Square(40, 10, 60, 30)));

            DxfHatch hatch = OnlyHatch(document);
            Assert.Equal(DxfHatchLoopTypes.External | DxfHatchLoopTypes.NotClosed, Kind(hatch, 0));
            Assert.Equal(Outermost, Kind(hatch, 1));
        }

        [Theory]
        [InlineData("SOLID", "Unable to fill the boundary with solid.")]
        [InlineData("ANSI31", "Unable to hatch the boundary.")]
        public void DrawBoundary_OnlyOpenShapes_MakeNoHatch_AndSayTheSame(string pattern, string said)
        {
            // AutoCAD 2021, an open triangle alone (and an open two-point shape): nothing is made.
            CadDocument document = Blank();
            CadCommandSession session = Start(document, "-HATCH", "P", pattern);

            if (pattern != "SOLID")
            {
                Run(session, "1", "0");
            }

            Run(session, "W", "N", P(0, 0), P(50, 0), P(25, 40), Enter, Enter, Enter);

            Assert.Empty(All<DxfHatch>(document.ModelSpace));
            Assert.Contains(said, session.History);
        }

        [Fact]
        public void DrawBoundary_AnOpenShapeAlone_RetainedIsStillKeptAsAnOpenPolyline()
        {
            // AutoCAD 2021: Retain Y, an open triangle alone: no hatch ("Unable to fill the boundary with solid."), the polyline kept open.
            CadDocument document = Blank();
            CadCommandSession session = Start(
                document,
                "-HATCH",
                "P",
                "SOLID",
                "W",
                "Y",
                P(0, 0),
                P(50, 0),
                P(25, 40),
                Enter,
                Enter,
                Enter
            );

            Assert.Empty(All<DxfHatch>(document.ModelSpace));
            Assert.Contains("Unable to fill the boundary with solid.", session.History);
            DxfPolyline kept = Assert.Single(All<DxfPolyline>(document.ModelSpace));
            Assert.False(kept.Closed);
            Assert.Equal(3, kept.NumberOfVertices);
        }

        [Fact]
        public void DrawBoundary_AnOpenShapeRetainedBesideASquare_IsFollowedByTheHatchToo()
        {
            // AutoCAD 2021, HPASSOC 1, Retain Y: the open loop follows its own open polyline.
            CadDocument document = Blank();
            Start(document, Draw("Y", Square(0, 0, 50, 50), new object[] { P(60, 0), P(80, 0), P(70, 20), Enter }));

            DxfHatch hatch = OnlyHatch(document);
            List<DxfPolyline> kept = All<DxfPolyline>(document.ModelSpace);
            Assert.Equal(2, kept.Count);
            Assert.True(kept[0].Closed);
            Assert.False(kept[1].Closed);
            Assert.True(hatch.Associative);
            Assert.Equal(new[] { kept[1].Handle }, hatch.GetAssociatedObjectsAt(1).Select(e => e.Handle));
        }

        [Fact]
        public void DrawBoundary_AShapeLeftOpenOnItsFirstPoint_IsAClosedLoop_AndItsPolylineStaysOpenWithTheFirstPointAgain()
        {
            // AutoCAD 2021: (0,0), (50,0), (50,50), (0,50), (0,0) ended with Enter: the hatch has the four edges of a closed loop,
            // and the polyline kept is open with its five vertices.
            CadDocument document = Blank();
            Start(document, Draw("Y", new object[] { P(0, 0), P(50, 0), P(50, 50), P(0, 50), P(0, 0), Enter }));

            DxfHatch hatch = OnlyHatch(document);
            Assert.Equal(1, hatch.NumberOfLoops);
            Assert.Equal(DxfHatchLoopTypes.External, Kind(hatch, 0));
            Assert.Equal(2500.0, hatch.Area, 6);
            DxfPolyline kept = Assert.Single(All<DxfPolyline>(document.ModelSpace));
            Assert.False(kept.Closed);
            Assert.Equal(5, kept.NumberOfVertices);
        }

        [Fact]
        public void DrawBoundary_TheGapToleranceDoesNotCloseADrawnShape()
        {
            // AutoCAD 2021, HPGAPTOL 5: a U whose ends are 3 apart is a loop that is not closed; alone it makes no hatch, beside a
            // square it is kept as one not closed with its five edges' worth of corners as drawn.
            CadDocument document = Blank();
            CadHatchSettings settings = document.HatchSettings.Clone();
            settings.GapTolerance = 5.0;
            document.HatchSettings = settings;
            Start(
                document,
                Draw(
                    "N",
                    Square(60, 0, 80, 20),
                    new object[] { P(0, 0), P(50, 0), P(50, 50), P(0, 50), P(0, 3), Enter }
                )
            );

            DxfHatch hatch = OnlyHatch(document);
            Assert.Equal(2, hatch.NumberOfLoops);
            Assert.Equal(DxfHatchLoopTypes.External | DxfHatchLoopTypes.NotClosed, Kind(hatch, 1));
            Assert.Equal(4, hatch.GetLoopAt(1).Curves.Count);
        }

        #endregion

        #region Duplicate loops

        /// <summary>The flags of the loops of the only hatch, without the polyline bit, as numbers (External 1, Outermost 16).</summary>
        private static int[] Numbers(CadDocument document) =>
            Enumerable
                .Range(0, OnlyHatch(document).NumberOfLoops)
                .Select(i => (int)Kind(OnlyHatch(document), i))
                .ToArray();

        /// <summary>A hatch of -HATCH's draW boundary of squares, the sizes the lengths of their sides, all about the point (50, 50).</summary>
        private static CadDocument DrawnCopies(params double[] sizes)
        {
            CadDocument document = Blank();
            Start(
                document,
                Draw("N", sizes.Select(z => Square(50 - z / 2.0, 50 - z / 2.0, 50 + z / 2.0, 50 + z / 2.0)).ToArray())
            );
            return document;
        }

        [Fact]
        public void DrawBoundary_TwoCopiesOfAShape_AreBothExternalAndOutermost()
        {
            // AutoCAD 2021: a square drawn twice makes 2 loops, flagged 17 and 17, not 1 and 1.
            Assert.Equal(new[] { 17, 17 }, Numbers(DrawnCopies(20, 20)));
        }

        [Fact]
        public void DrawBoundary_ThreeOrMoreCopies_FlagTheSecondAndThirdOnly_AndTheRestNothing()
        {
            // AutoCAD 2021, a square drawn 3, 4, 5 and 6 times: 0 17 17, 0 17 17 0, 0 17 17 0 0, 0 17 17 0 0 0.
            Assert.Equal(new[] { 0, 17, 17 }, Numbers(DrawnCopies(20, 20, 20)));
            Assert.Equal(new[] { 0, 17, 17, 0 }, Numbers(DrawnCopies(20, 20, 20, 20)));
            Assert.Equal(new[] { 0, 17, 17, 0, 0, 0 }, Numbers(DrawnCopies(20, 20, 20, 20, 20, 20)));
        }

        [Fact]
        public void DrawBoundary_CopiesInsideASquare_AreOutermost_ByTheSameCount()
        {
            // AutoCAD 2021, a square of 100 and then 2, 3, 4 and 5 squares of 20 over each other: 1 16 16, 1 0 16 16, 1 0 16 16 0,
            // 1 0 16 16 0 0.
            Assert.Equal(new[] { 1, 16, 16 }, Numbers(DrawnCopies(100, 20, 20)));
            Assert.Equal(new[] { 1, 0, 16, 16 }, Numbers(DrawnCopies(100, 20, 20, 20)));
            Assert.Equal(new[] { 1, 0, 16, 16, 0 }, Numbers(DrawnCopies(100, 20, 20, 20, 20)));
            Assert.Equal(new[] { 1, 0, 16, 16, 0, 0 }, Numbers(DrawnCopies(100, 20, 20, 20, 20, 20)));
        }

        [Fact]
        public void DrawBoundary_ACopyOfTheOuterSquare_MakesWhatLiesInsideBothDeeper()
        {
            // AutoCAD 2021: the square of 100 twice and one of 20 inside both is 17 17 0, the 20 two deep; the square of 20
            // drawn between the two copies of the 100 changes nothing of their flags (17 0 17); the pair of 20 and the pair of 10
            // inside it are 1 16 16 0 0 under a square of 100.
            Assert.Equal(new[] { 17, 17, 0 }, Numbers(DrawnCopies(100, 100, 20)));
            Assert.Equal(new[] { 17, 0, 17 }, Numbers(DrawnCopies(100, 20, 100)));
            Assert.Equal(new[] { 1, 16, 16, 0, 0 }, Numbers(DrawnCopies(100, 20, 20, 10, 10)));
        }

        [Fact]
        public void DrawBoundary_ShapesThatAreNotCopies_KeepTheFlagsOfTheirDepth()
        {
            Assert.Equal(new[] { 1, 16, 0 }, Numbers(DrawnCopies(100, 20, 10)));
            Assert.Equal(new[] { 1 }, Numbers(DrawnCopies(20)));

            // AutoCAD 2021: the pair of 20 drawn either side of the square of 100 is 16 1 16.
            Assert.Equal(new[] { 16, 1, 16 }, Numbers(DrawnCopies(20, 100, 20)));
        }

        /// <summary>The loops of the hatch a pick or a selection makes of a square of 100 with circles of 10 about its middle, drawn over each other.</summary>
        private static CadDocument SquareWithCircles(int circles)
        {
            CadDocument document = Blank();
            var outer = new DxfPolyline();
            outer.AddVertexAt(0, new GeoPoint2(0, 0), 0.0, 0.0, 0.0);
            outer.AddVertexAt(1, new GeoPoint2(100, 0), 0.0, 0.0, 0.0);
            outer.AddVertexAt(2, new GeoPoint2(100, 100), 0.0, 0.0, 0.0);
            outer.AddVertexAt(3, new GeoPoint2(0, 100), 0.0, 0.0, 0.0);
            outer.Closed = true;
            document.Database.ModelSpace.AppendEntity(outer);

            for (int i = 0; i < circles; i++)
            {
                document.Database.ModelSpace.AppendEntity(
                    new DxfCircle(new GeoPoint3(50, 50, 0), new GeoVector3(0, 0, 1), 10)
                );
            }

            return document;
        }

        [Fact]
        public void SelectObjects_AShapeAndSixteenCopiesOfAnother_MakeSeventeenLoops()
        {
            // AutoCAD 2021, -HATCH Select objects of a square of 100 and 16 circles of 10 over each other: 17 loops.
            CadDocument document = SquareWithCircles(16);
            Start(
                document,
                "-HATCH",
                "P",
                "SOLID",
                "S",
                document.ModelSpace.Block.Select(e => e.Handle).ToArray(),
                Enter,
                Enter
            );

            Assert.Equal(17, OnlyHatch(document).NumberOfLoops);
        }

        [Fact]
        public void APickInsideASquareWithSixteenCopiesOfACircle_FindsTheSquareAndTheCircleOnce()
        {
            // AutoCAD 2021, -HATCH Select internal point of the same drawing: 2 loops, the copies of the circle one island.
            CadDocument document = SquareWithCircles(16);
            Start(document, "-HATCH", "P", "SOLID", P(20, 20), Enter);

            Assert.Equal(2, OnlyHatch(document).NumberOfLoops);
        }

        #endregion
    }
}
