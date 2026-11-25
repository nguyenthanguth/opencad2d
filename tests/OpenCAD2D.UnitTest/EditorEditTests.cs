using System;
using System.Collections.Generic;
using System.Linq;
using GeometryHelper;
using GeometryHelper.Dxf;
using GeometryHelper.Geometry;
using GeometryHelper.UnitTest;
using Xunit;
using static OpenCAD2D.UnitTest.EditingDrawings;

namespace OpenCAD2D.UnitTest
{
    /// <summary>
    /// The edit calls of <see cref="CadEditor"/> (Move, Copy, Rotate, Scale, Mirror, Offset, Erase), each against the command it
    /// stands for: the same drawing is made twice from <see cref="Plan"/> (or the drawing a test names), once by typing the
    /// command through a session and once by the call, and the ENTITIES of the two are compared. Every number is in millimetres
    /// and every angle in radians, and the session's tolerance is <see cref="EditingDrawings.Tight"/> (an EqualPoint of 1E-9) in both.
    /// The plan: A1 a line on layer Steel from (0, 0) to (100, 0), A2 a line from (0, 0) to (0, 50), A3 a circle of radius 10 about
    /// (200, 0), A4 an arc of radius 20 about (300, 0) from 0 to 90 degrees, A5 an open polyline (0, 100), (100, 100), (100, 150),
    /// A6 a text "NOTE" 5 high at (0, 200), A7 an INSERT of BOX at (500, 0), and A8 a line on the locked layer Locked.
    /// </summary>
    public class EditorEditTests
    {
        /// <summary>A line, a circle, an arc, a polyline, a text and an INSERT of the plan: one of each kind the edits meet.</summary>
        private static readonly string[] Kinds = { "A1", "A3", "A4", "A5", "A6", "A7" };

        private static readonly GeoVector2 Shift = new GeoVector2(10, 5);

        private static CadCommandSession Typed(CadDocument document)
        {
            CadCommandSession session = Session(document);
            session.Tolerance = Tight;
            return session;
        }

        private static DxfEntity[] Given(CadDocument document, params string[] handles) =>
            handles.Select(h => Entity<DxfEntity>(document, h)).ToArray();

        private static GeoPoint3 P3(double x, double y) => new GeoPoint3(x, y, 0);

        private static string Text(CadDocument document) => document.Database.ToText();

        #region The calls against the commands

        [Fact]
        public void ObjectsOfEveryKindMovedByTenAndFive_GiveTheDrawingMoveGives()
        {
            CadDocument typed = Plan();
            CadDocument called = Plan();

            Run(Typed(typed), "MOVE", Kinds, Enter, P(0, 0), P(10, 5));
            Typed(called).Editor.Move(Given(called, Kinds), Shift);

            // MOVE from (0, 0) to (10, 5): the line from (10, 5) to (110, 5), the circle about (210, 5).
            AssertSameEntities(typed, called, Tight);
            Near(P3(10, 5), Entity<DxfLine>(called, "A1").StartPoint);
            Near(P3(110, 5), Entity<DxfLine>(called, "A1").EndPoint);
            Near(P3(210, 5), Entity<DxfCircle>(called, "A3").Center);
        }

        [Fact]
        public void AMoveByTheCall_GivesBackTheObjectsItWasGiven_MovedWhereTheyStand_AndWritesNothingOnTheCommandLine()
        {
            CadDocument document = Plan();
            CadCommandSession session = Typed(document);
            DxfLine first = Entity<DxfLine>(document, "A1");
            DxfLine second = Entity<DxfLine>(document, "A2");
            int written = session.History.Count;

            IReadOnlyList<DxfLine> moved = session.Editor.Move(new[] { second, first }, Shift);

            // The same two objects in the order given, no new object, and not a line on the command line.
            Assert.Equal(2, moved.Count);
            Assert.Same(second, moved[0]);
            Assert.Same(first, moved[1]);
            Assert.Equal(8, document.Database.ModelSpace.Count());
            Near(P3(10, 5), first.StartPoint);
            Near(P3(10, 5), second.StartPoint);
            Near(P3(10, 55), second.EndPoint);
            Assert.Equal(written, session.History.Count);
        }

        [Fact]
        public void TheSameMoveCalledTwice_MovesTwice()
        {
            CadDocument document = Plan();
            CadCommandSession session = Typed(document);
            DxfLine line = Entity<DxfLine>(document, "A1");

            session.Editor.Move(new[] { line }, Shift);
            session.Editor.Move(new[] { line }, Shift);

            // Two moves of (10, 5): from (0, 0) to (20, 10), and two steps to undo (an undo reads the drawing back, so the line is
            // looked up again after each).
            Near(P3(20, 10), line.StartPoint);
            Assert.True(document.Undo());
            Near(P3(10, 5), Entity<DxfLine>(document, "A1").StartPoint);
            Assert.True(document.Undo());
            Near(P3(0, 0), Entity<DxfLine>(document, "A1").StartPoint);
            Assert.False(document.CanUndo);
        }

        [Fact]
        public void TwoLinesCopiedByFifty_GiveTheDrawingCopyGives_AndComeBackAsTheTwoNewLines()
        {
            CadDocument typed = Plan();
            CadDocument called = Plan();
            DxfLine a1 = Entity<DxfLine>(called, "A1");
            DxfLine a2 = Entity<DxfLine>(called, "A2");

            Run(Typed(typed), "COPY", new[] { "A1", "A2" }, Enter, P(0, 0), P(0, 50), Enter);
            IReadOnlyList<DxfLine> copies = Typed(called).Editor.Copy(new[] { a1, a2 }, new GeoVector2(0, 50));

            // COPY by (0, 50): the copy of A1 from (0, 50) to (100, 50) on Steel, that of A2 from (0, 50) to (0, 100) on 0; the sources stay.
            AssertSameEntities(typed, called, Tight);
            Assert.Equal(2, copies.Count);
            Assert.Equal("Steel", copies[0].Layer);
            Near(P3(0, 50), copies[0].StartPoint);
            Near(P3(100, 50), copies[0].EndPoint);
            Assert.Equal("0", copies[1].Layer);
            Near(P3(0, 50), copies[1].StartPoint);
            Near(P3(0, 100), copies[1].EndPoint);
            Assert.Same(called.FindEntity(copies[0].Handle), copies[0]);
            Assert.Same(called.FindEntity(copies[1].Handle), copies[1]);
            Assert.DoesNotContain(copies, c => c == a1 || c == a2);
            Near(P3(0, 0), a1.StartPoint);
            Near(P3(0, 0), a2.StartPoint);
        }

        [Fact]
        public void CopiesComeBackInTheOrderTheyWereGiven_AnObjectGivenTwiceIsCopiedOnce_AndTheListGivenIsLeftAlone()
        {
            CadDocument document = Plan();
            DxfLine a1 = Entity<DxfLine>(document, "A1");
            DxfLine a2 = Entity<DxfLine>(document, "A2");
            var given = new List<DxfLine> { a2, a1, a2 };

            IReadOnlyList<DxfLine> copies = Typed(document).Editor.Copy(given, new GeoVector2(0, 50));

            // The copy of A2 (on layer 0, ending at (0, 100)) first, that of A1 (on Steel, ending at (100, 50)) second; three lines
            // given, two copies made, so ten objects in all.
            Assert.Equal(2, copies.Count);
            Assert.Equal("0", copies[0].Layer);
            Near(P3(0, 100), copies[0].EndPoint);
            Assert.Equal("Steel", copies[1].Layer);
            Near(P3(100, 50), copies[1].EndPoint);
            Assert.Equal(10, document.Database.ModelSpace.Count());
            Assert.Equal(new[] { a2, a1, a2 }, given);
        }

        [Fact]
        public void ALineAndItsAssociativeDimensionCopiedTogether_GiveTheDrawingCopyGives_TheDimensionCopyTiedToTheLineCopy()
        {
            CadDocument typed = TiedDimension(out string[] handles);
            CadDocument called = TiedDimension(out _);

            Run(Typed(typed), "COPY", handles, Enter, P(0, 0), P(0, 100), Enter);
            IReadOnlyList<DxfEntity> copies = Typed(called).Editor.Copy(Given(called, handles), new GeoVector2(0, 100));

            // COPY by (0, 100): the dimension copied with its line is tied to the copy of the line at both ends, not to the line.
            AssertSameEntities(typed, called, Tight);
            Assert.Equal(2, copies.Count);
            var dimension = Assert.IsType<DxfRotatedDimension>(copies[1]);
            Assert.NotEmpty(dimension.DimAssoc.PointReferences);
            Assert.All(dimension.DimAssoc.PointReferences, r => Assert.Equal(copies[0].Handle, r.ObjectHandle));
            Near(P3(0, 100), dimension.XLine1Point);
        }

        [Fact]
        public void ALineMovedWithoutItsAssociativeDimension_MakesTheDimensionFollow_AsMoveDoes()
        {
            CadDocument typed = TiedDimension(out string[] handles);
            CadDocument called = TiedDimension(out _);

            Run(Typed(typed), "MOVE", new[] { handles[0] }, Enter, P(0, 0), P(0, 20));
            Typed(called).Editor.Move(Given(called, handles[0]), new GeoVector2(0, 20));

            // The line moved up 20, the dimension's origins follow it to (0, 20) and (100, 20), its measurement still 100.
            AssertSameEntities(typed, called, Tight);
            var dimension = Entity<DxfRotatedDimension>(called, handles[1]);
            Near(P3(0, 20), dimension.XLine1Point);
            Near(P3(100, 20), dimension.XLine2Point);
            Assert.Equal(100.0, dimension.Measurement, 9);
        }

        [Fact]
        public void ACopyByNothing_MakesACopyOnTheSource_AsCopyDoes()
        {
            CadDocument typed = Plan();
            CadDocument called = Plan();

            Run(Typed(typed), "COPY", new[] { "A1" }, Enter, P(0, 0), P(0, 0), Enter);
            IReadOnlyList<DxfLine> copies = Typed(called)
                .Editor.Copy(new[] { Entity<DxfLine>(called, "A1") }, new GeoVector2(0, 0));

            // COPY with the second point on the first makes one more line, from (0, 0) to (100, 0), not a refusal.
            AssertSameEntities(typed, called, Tight);
            Assert.Equal(9, called.Database.ModelSpace.Count());
            Near(P3(100, 0), Assert.Single(copies).EndPoint);
        }

        [Fact]
        public void ObjectsOfEveryKindRotatedAQuarterTurnAboutTheOrigin_GiveTheDrawingRotateGives()
        {
            CadDocument typed = Plan();
            CadDocument called = Plan();

            Run(Typed(typed), "ROTATE", Kinds, Enter, P(0, 0), "90");
            Typed(called).Editor.Rotate(Given(called, Kinds), new GeoPoint2(0, 0), Math.PI / 2.0);

            // ROTATE 90 about (0, 0): the line from (0, 0) to (100, 0) now ends at (0, 100), the circle's centre (200, 0) at (0, 200).
            AssertSameEntities(typed, called, Tight);
            Near(P3(0, 100), Entity<DxfLine>(called, "A1").EndPoint, 1E-9);
            Near(P3(0, 200), Entity<DxfCircle>(called, "A3").Center, 1E-9);
        }

        [Theory]
        [InlineData(-30.0)]
        [InlineData(-180.0)]
        [InlineData(450.0)]
        public void AHatchRotatedByTheCall_IsTurnedAsRotateTurnsIt_TheAngleTakenFromNoughtUpToAWholeTurn(double degrees)
        {
            // ROTATE takes its angle from nought up to a whole turn (-30 is 330, -180 is 180, 450 is 90), which a hatch's pattern
            // lines show: the call turns the same way as the command or the pattern is written at another angle.
            CadDocument typed = MirrorSources(false, out string[] handles);
            CadDocument called = MirrorSources(false, out _);

            Run(
                Typed(typed),
                "ROTATE",
                new[] { handles[1] },
                Enter,
                P(5, 5),
                degrees.ToString(System.Globalization.CultureInfo.InvariantCulture)
            );
            Typed(called).Editor.Rotate(Given(called, handles[1]), new GeoPoint2(5, 5), degrees * Math.PI / 180.0);

            AssertSameEntities(typed, called, Tight);
        }

        [Fact]
        public void AHatchRotatedAWholeTurnByTheCall_IsNotTouched_AsRotate360LeavesIt()
        {
            // A whole turn is none: 2 * Math.PI is exactly the 360 degrees ROTATE takes (360 * Math.PI / 180 is the same double,
            // which the first line says, so that the case is a whole turn for both and not a hair under it).
            Assert.Equal(360.0 * Math.PI / 180.0, 2.0 * Math.PI);
            CadDocument typed = MirrorSources(false, out string[] handles);
            CadDocument called = MirrorSources(false, out _);
            CadDocument untouched = MirrorSources(false, out _);

            Run(Typed(typed), "ROTATE", new[] { handles[1] }, Enter, P(5, 5), "360");
            Typed(called).Editor.Rotate(Given(called, handles[1]), new GeoPoint2(5, 5), 2.0 * Math.PI);

            AssertSameEntities(typed, called, Tight);
            AssertSameEntities(untouched, called, Tight);
        }

        [Theory]
        [InlineData(2.0, "2")]
        [InlineData(0.5, "0.5")]
        public void ObjectsOfEveryKindScaled_GiveTheDrawingScaleGives(double factor, string typedFactor)
        {
            CadDocument typed = Plan();
            CadDocument called = Plan();

            Run(Typed(typed), "SCALE", Kinds, Enter, P(0, 0), typedFactor);
            Typed(called).Editor.Scale(Given(called, Kinds), new GeoPoint2(0, 0), factor);

            // SCALE about (0, 0): the circle about (200, 0) of radius 10 becomes one about (200 * f, 0) of radius 10 * f, the text 5 * f high.
            AssertSameEntities(typed, called, Tight);
            DxfCircle circle = Entity<DxfCircle>(called, "A3");
            Assert.Equal(10.0 * factor, circle.Radius, 9);
            Near(P3(200.0 * factor, 0), circle.Center);
            Assert.Equal(5.0 * factor, Entity<DxfDBText>(called, "A6").Height, 9);
        }

        public static IEnumerable<object[]> MirrorCases()
        {
            foreach (bool mirrtext in new[] { false, true })
            {
                foreach (bool erase in new[] { false, true })
                {
                    foreach (bool mirrhatch in new[] { false, true })
                    {
                        foreach (bool slanted in new[] { false, true })
                        {
                            yield return new object[] { mirrtext, erase, mirrhatch, slanted };
                        }
                    }
                }
            }
        }

        [Theory]
        [MemberData(nameof(MirrorCases))]
        public void ATextAHatchAndADimensionMirrored_GiveTheDrawingMirrorGives(
            bool mirrtext,
            bool eraseSource,
            bool mirrhatch,
            bool slanted
        )
        {
            // The TEXT, the hatch and the rotated dimension of MirrorSources across x = 0 (the line (0, 0)-(0, 10)) or across the
            // line (20, 5)-(30, 20), under MIRRTEXT 0 and 1, MIRRHATCH 0 and 1, the sources kept and erased.
            CadDocument typed = MirrorSources(mirrtext, out string[] handles);
            CadDocument called = MirrorSources(mirrtext, out _);
            GeoPoint2 first = slanted ? P(20, 5) : P(0, 0);
            GeoPoint2 second = slanted ? P(30, 20) : P(0, 10);
            CadCommandSession typing = Typed(typed);
            CadCommandSession calling = Typed(called);

            if (mirrhatch)
            {
                Run(typing, "MIRRHATCH", "1");
                Run(calling, "MIRRHATCH", "1");
            }

            Run(typing, "MIRROR", handles, Enter, first, second, eraseSource ? (object)"Y" : Enter);
            DxfEntity[] sources = Given(called, handles);
            IReadOnlyList<DxfEntity> result = calling.Editor.Mirror(sources, first, second, eraseSource);

            AssertSameEntities(typed, called, Tight);
            Assert.Equal(sources.Select(s => s.GetType()), result.Select(r => r.GetType()));

            for (int i = 0; i < sources.Length; i++)
            {
                if (eraseSource)
                {
                    Assert.Same(sources[i], result[i]);
                }
                else
                {
                    Assert.NotSame(sources[i], result[i]);
                    Assert.False(result[i].IsErased);
                }
            }
        }

        [Theory]
        [InlineData(false)]
        [InlineData(true)]
        public void ATextMirroredAcrossAnUpright_ReadsForwardsUnderMirrtext0_AndIsWrittenBackwardsUnderMirrtext1(
            bool mirrtext
        )
        {
            CadDocument document = Blank();
            document.Database.Mirrtext = mirrtext;
            var text = new DxfDBText();
            document.Database.ModelSpace.AppendEntity(text);
            text.TextString = "ABC";
            text.Height = 2.5;
            text.Position = P3(0, 0);

            DxfDBText copy = Assert.Single(
                Typed(document).Editor.Mirror(new[] { text }, new GeoPoint2(100, 0), new GeoPoint2(100, 10))
            );

            // As MIRROR does (AutoCAD 2021, probed): the normal stays up either way; MIRRTEXT 1 writes the text backwards at
            // (200, 0), MIRRTEXT 0 keeps it readable.
            Assert.True(copy.Normal.Z > 1.0 - 1E-9);
            Assert.Equal(mirrtext, copy.IsMirroredInX);
            Assert.False(copy.IsMirroredInY);

            if (mirrtext)
            {
                Near(P3(200, 0), copy.Position);
            }
        }

        [Fact]
        public void AnObjectGivenToMirror_IsKeptByDefault()
        {
            CadDocument document = Plan();
            DxfLine line = Entity<DxfLine>(document, "A1");

            DxfLine copy = Assert.Single(
                Typed(document).Editor.Mirror(new[] { line }, new GeoPoint2(0, 0), new GeoPoint2(10, 0))
            );

            // Across the x axis the line from (0, 0) to (100, 0) lies on itself: two lines, the source not erased.
            Assert.False(line.IsErased);
            Assert.NotSame(line, copy);
            Assert.Equal(9, document.Database.ModelSpace.Count());
        }

        [Theory]
        [InlineData(10.0, 5.0)]
        [InlineData(-10.0, -5.0)]
        public void ALineOffsetByFiveTowardAPoint_GivesTheLineOffsetGives_OnItsOwnLayer_AndKeepsTheSource(
            double towards,
            double y
        )
        {
            CadDocument typed = Plan();
            CadDocument called = Plan();
            DxfLine source = Entity<DxfLine>(called, "A1");

            Run(Typed(typed), "OFFSET", "5", new[] { "A1" }, P(50, towards), Enter);
            IReadOnlyList<DxfCurve> made = Typed(called).Editor.Offset(source, 5.0, P(50, towards));

            // OFFSET 5 toward (50, 10): the line from (0, 5) to (100, 5), toward (50, -10): from (0, -5) to (100, -5); on Steel, the
            // source's layer and not the current one (0); the source stays where it was.
            AssertSameEntities(typed, called, Tight);
            DxfLine line = Assert.IsType<DxfLine>(Assert.Single(made));
            Assert.Equal("Steel", line.Layer);
            Near(P3(0, y), line.StartPoint);
            Near(P3(100, y), line.EndPoint);
            Assert.Same(called.FindEntity(line.Handle), line);
            Near(P3(0, 0), source.StartPoint);
            Near(P3(100, 0), source.EndPoint);
        }

        [Theory]
        [InlineData(30.0, 25.0)]
        [InlineData(10.0, 15.0)]
        public void AnArcOffsetByFiveOutwardOrInward_GivesTheArcOffsetGives_ItsCentreAndAnglesKept(
            double reach,
            double radius
        )
        {
            CadDocument typed = Plan();
            CadDocument called = Plan();
            GeoPoint2 side = P(300 + reach * Math.Cos(Math.PI / 4.0), reach * Math.Sin(Math.PI / 4.0));

            Run(Typed(typed), "OFFSET", "5", new[] { "A4" }, side, Enter);
            IReadOnlyList<DxfCurve> made = Typed(called).Editor.Offset(Entity<DxfArc>(called, "A4"), 5.0, side);

            // The arc of radius 20 about (300, 0), from 0 to 90 degrees, with the point 30 from its centre: radius 25; 10 from it: 15.
            AssertSameEntities(typed, called, Tight);
            DxfArc arc = Assert.IsType<DxfArc>(Assert.Single(made));
            Assert.Equal(radius, arc.Radius, 9);
            Near(P3(300, 0), arc.Center);
            Assert.Equal(0.0, arc.StartAngle, 9);
            Assert.Equal(Math.PI / 2.0, arc.EndAngle, 9);
        }

        [Theory]
        [InlineData(15.0, 14.0)]
        [InlineData(5.0, 6.0)]
        public void ACircleOffsetByFour_GivesTheCircleOffsetGives_BiggerWithThePointOutsideSmallerWithItInside(
            double reach,
            double radius
        )
        {
            CadDocument typed = Plan();
            CadDocument called = Plan();
            GeoPoint2 side = P(200 + reach, 0);

            Run(Typed(typed), "OFFSET", "4", new[] { "A3" }, side, Enter);
            IReadOnlyList<DxfCurve> made = Typed(called).Editor.Offset(Entity<DxfCircle>(called, "A3"), 4.0, side);

            // The circle of radius 10 about (200, 0): 15 from its centre gives radius 14, 5 from it radius 6.
            AssertSameEntities(typed, called, Tight);
            Assert.Equal(radius, Assert.IsType<DxfCircle>(Assert.Single(made)).Radius, 9);
        }

        [Theory]
        [InlineData(110.0, 0.0, 110.0, 90.0, 110.0, 90.0, 150.0)]
        [InlineData(90.0, 0.0, 90.0, 110.0, 90.0, 110.0, 150.0)]
        public void AnOpenPolylineOffsetByTen_GivesTheOffsetOfItsSide_TheCornerRunOnUntilTheSegmentsMeet(
            double towards,
            double x0,
            double y0,
            double x1,
            double y1,
            double x2,
            double y2
        )
        {
            // A5 runs (0, 100), (100, 100), (100, 150), turning left. Toward (50, 110), its left, the offset is (0, 110), (90, 110),
            // (90, 150): the corner comes in to meet; toward (50, 90), its right, (0, 90), (110, 90), (110, 150): the corner goes out.
            CadDocument typed = Plan();
            CadDocument called = Plan();
            GeoPoint2 side = P(50, towards);

            Run(Typed(typed), "OFFSET", "10", new[] { "A5" }, side, Enter);
            IReadOnlyList<DxfCurve> made = Typed(called).Editor.Offset(Entity<DxfPolyline>(called, "A5"), 10.0, side);

            AssertSameEntities(typed, called, Tight);
            DxfPolyline offset = Assert.IsType<DxfPolyline>(Assert.Single(made));
            Assert.False(offset.Closed);
            Assert.Equal(3, offset.NumberOfVertices);
            Near(P(x0, y0), offset.GetPoint2dAt(0));
            Near(P(x1, y1), offset.GetPoint2dAt(1));
            Near(P(x2, y2), offset.GetPoint2dAt(2));
        }

        [Theory]
        [InlineData(290.0, -10.0, 290.0, 110.0, 290.0, 110.0, 360.0, -10.0, 360.0)]
        [InlineData(325.0, 10.0, 310.0, 90.0, 310.0, 90.0, 340.0, 10.0, 340.0)]
        public void AClosedPolylineOffsetByTenOutwardOrInward_GivesTheGrownOrShrunkRectangle(
            double towards,
            double ax,
            double ay,
            double bx,
            double by,
            double cx,
            double cy,
            double dx,
            double dy
        )
        {
            // The plate (0, 300)-(100, 350): toward (50, 290), below it, the rectangle (-10, 290)-(110, 360); toward (50, 325), inside it,
            // (10, 310)-(90, 340); the first corner first, each kept closed.
            CadDocument typed = Plan();
            CadDocument called = Plan();
            DxfPolyline plate = AddPlate(called);
            AddPlate(typed);
            GeoPoint2 side = P(50, towards);

            Run(Typed(typed), "OFFSET", "10", new[] { plate.Handle }, side, Enter);
            IReadOnlyList<DxfCurve> made = Typed(called).Editor.Offset(plate, 10.0, side);

            AssertSameEntities(typed, called, Tight);
            DxfPolyline offset = Assert.IsType<DxfPolyline>(Assert.Single(made));
            Assert.True(offset.Closed);
            Assert.Equal(4, offset.NumberOfVertices);
            Near(P(ax, ay), offset.GetPoint2dAt(0));
            Near(P(bx, by), offset.GetPoint2dAt(1));
            Near(P(cx, cy), offset.GetPoint2dAt(2));
            Near(P(dx, dy), offset.GetPoint2dAt(3));
        }

        [Fact]
        public void AnOwnedLineOffset_GivesACopyWithNoOwnerTag_AsOffsetDoes()
        {
            CadDocument typed = OwnedLines(CadEditRule.MoveOnly);
            CadDocument called = OwnedLines(CadEditRule.MoveOnly);

            Run(Typed(typed), "OFFSET", "5", new[] { "A1" }, P(50, 10), Enter);
            IReadOnlyList<DxfCurve> made = Typed(called).Editor.Offset(Entity<DxfLine>(called, "A1"), 5.0, P(50, 10));

            AssertSameEntities(typed, called, Tight);
            Assert.False(CadOwnerTag.TryRead(Assert.Single(made), out _));
        }

        [Fact]
        public void ObjectsOfEveryKindErased_GiveTheDrawingEraseGives_AndUndoBringsThemBack()
        {
            CadDocument typed = Plan();
            CadDocument called = Plan();
            string start = Text(called);
            DxfEntity[] given = Given(called, Kinds);

            Run(Typed(typed), "ERASE", Kinds, Enter);
            Typed(called).Editor.Erase(given);

            AssertSameEntities(typed, called, Tight);
            Assert.All(given, e => Assert.True(e.IsErased));
            Assert.Equal("ERASE", called.NextUndo.Name);
            Assert.True(called.Undo());
            Assert.Equal(start, Text(called));
        }

        #endregion

        #region Refusals: the whole call refused, nothing changed

        /// <summary>One call of the editor on a list of entities, named for the test's display.</summary>
        public sealed class Call
        {
            public Call(string name, Func<CadEditor, DxfEntity[], object> run, bool needsCurve = false)
            {
                Name = name;
                Run = run;
                NeedsCurve = needsCurve;
            }

            public string Name { get; }

            public Func<CadEditor, DxfEntity[], object> Run { get; }

            /// <summary>Whether it takes one curve and not a list, and so is left out of the tests of lists.</summary>
            public bool NeedsCurve { get; }

            public override string ToString() => Name;
        }

        private static readonly Call[] Calls =
        {
            new Call("Move", (e, l) => e.Move(l, new GeoVector2(10, 5))),
            new Call("Copy", (e, l) => e.Copy(l, new GeoVector2(10, 5))),
            new Call("Rotate", (e, l) => e.Rotate(l, new GeoPoint2(0, 0), Math.PI / 2.0)),
            new Call("Scale", (e, l) => e.Scale(l, new GeoPoint2(0, 0), 2.0)),
            new Call("Mirror", (e, l) => e.Mirror(l, new GeoPoint2(0, 0), new GeoPoint2(0, 10), false)),
            new Call("MirrorAndErase", (e, l) => e.Mirror(l, new GeoPoint2(0, 0), new GeoPoint2(0, 10), true)),
            new Call("Erase", (e, l) =>
            {
                e.Erase(l);
                return null;
            }),
            new Call("Offset", (e, l) => e.Offset((DxfCurve)l[0], 5.0, new GeoPoint2(50, 10)), true),
        };

        public static IEnumerable<object[]> EveryCall() => Calls.Select(c => new object[] { c });

        public static IEnumerable<object[]> EveryCallOnAList() =>
            Calls.Where(c => !c.NeedsCurve).Select(c => new object[] { c });

        public static IEnumerable<object[]> EveryCallThatChangesAnObject() =>
            Calls
                .Where(c => c.Name == "Rotate" || c.Name == "Scale" || c.Name == "MirrorAndErase" || c.Name == "Erase")
                .Select(c => new object[] { c });

        public static IEnumerable<object[]> EveryCallAndEveryReason() =>
            Calls.SelectMany(c =>
                new[] { "Locked", "OtherDrawing", "Erased", "InsideABlock" }.Select(r => new object[] { c, r })
            );

        [Theory]
        [MemberData(nameof(EveryCallAndEveryReason))]
        public void AnObjectTheDrawingWillNotLetBeEdited_RefusesTheWholeCall_ChangingNothingAndWritingNothing(
            Call call,
            string reason
        )
        {
            CadDocument document = Plan();
            CadDocument other = Plan();
            DxfLine target;
            string message;

            switch (reason)
            {
                case "Locked":
                    // A8 is on the layer Locked.
                    target = Entity<DxfLine>(document, "A8");
                    message = "The object A8 is on the locked layer Locked.";
                    break;
                case "OtherDrawing":
                    // A1 of another drawing: the same handle, not the drawing's own object.
                    target = Entity<DxfLine>(other, "A1");
                    message = "The object A1 is not in the drawing " + document.Title + ".";
                    break;
                case "Erased":
                    target = Entity<DxfLine>(document, "A2");
                    target.Erase();
                    message = null;
                    break;
                default:
                    target = AddLineInBox(document);
                    message = null;
                    break;
            }

            Assert.NotEmpty(target.Handle);
            CadCommandSession session = Typed(document);
            string text = Text(document);
            string otherText = Text(other);
            bool modified = document.IsModified;
            int written = session.History.Count;

            InvalidOperationException refused = Assert.Throws<InvalidOperationException>(() =>
                call.Run(session.Editor, new DxfEntity[] { target })
            );

            if (message != null)
            {
                Assert.Equal(message, refused.Message);
            }
            else
            {
                Assert.Contains(target.Handle, refused.Message, StringComparison.Ordinal);
            }

            Assert.Equal(text, Text(document));
            Assert.Equal(otherText, Text(other));
            Assert.False(document.CanUndo);
            Assert.False(other.CanUndo);
            Assert.Equal(modified, document.IsModified);
            Assert.Equal(written, session.History.Count);
        }

        [Fact]
        public void ALockedObjectAmongFreeOnes_RefusesTheWholeCall_TheFreeOnesStayWhereTheyWere_AndTheFirstRefusedIsNamed()
        {
            CadDocument document = Plan();
            CadCommandSession session = Typed(document);
            string text = Text(document);
            DxfEntity[] given =
            {
                Entity<DxfLine>(document, "A1"),
                Entity<DxfLine>(document, "A8"),
                Entity<DxfLine>(Plan(), "A2"),
            };

            InvalidOperationException refused = Assert.Throws<InvalidOperationException>(() =>
                session.Editor.Move(given, Shift)
            );

            // A1 is fine, A8 is the first that is not (on Locked), A2 is of another drawing: the message names A8.
            Assert.Equal("The object A8 is on the locked layer Locked.", refused.Message);
            Assert.Equal(text, Text(document));
            Assert.False(document.CanUndo);
        }

        [Theory]
        [MemberData(nameof(EveryCallThatChangesAnObject))]
        public void AnObjectItsOwnerAllowsOnlyToMove_IsRefusedWithTheOwnersWords_ThoseBeforeItStayWhereTheyWere(
            Call call
        )
        {
            CadDocument document = OwnedLines(CadEditRule.MoveOnly);
            CadCommandSession session = Typed(document);
            string text = Text(document);
            DxfEntity[] given = { Entity<DxfLine>(document, "A2"), Entity<DxfLine>(document, "A1") };

            InvalidOperationException refused = Assert.Throws<InvalidOperationException>(() =>
                call.Run(session.Editor, given)
            );

            // The gate answers as it does for the typed command (probed: ROTATE of A1 writes the same line); A2, which nothing owns,
            // is not moved either.
            Assert.Equal("This line belongs to the model and can only be moved.", refused.Message);
            Assert.Equal(text, Text(document));
            Assert.False(document.CanUndo);
        }

        [Fact]
        public void AnOwnedLineCopied_GivesTheDrawingCopyGives_TheCopyOwnedByNobody()
        {
            CadDocument typed = OwnedLines(CadEditRule.MoveOnly);
            CadDocument called = OwnedLines(CadEditRule.MoveOnly);

            Run(Typed(typed), "COPY", new[] { "A1" }, Enter, P(0, 0), P(0, 10), Enter);
            IReadOnlyList<DxfLine> copies = Typed(called)
                .Editor.Copy(new[] { Entity<DxfLine>(called, "A1") }, new GeoVector2(0, 10));

            // COPY is not a change to the owner's object: the owner that allows only moving lets it be copied, and the copy carries no tag.
            AssertSameEntities(typed, called, Tight);
            Assert.False(CadOwnerTag.TryRead(Assert.Single(copies), out _));
        }

        [Theory]
        [InlineData(0.0)]
        [InlineData(-1.0)]
        [InlineData(double.NaN)]
        [InlineData(double.PositiveInfinity)]
        public void AScaleFactorOfNoughtBelowItOrNotFinite_IsRefusedBeforeAnythingChanges(double factor)
        {
            CadDocument document = Plan();
            CadCommandSession session = Typed(document);
            string text = Text(document);

            Assert.Throws<ArgumentOutOfRangeException>(() =>
                session.Editor.Scale(Given(document, "A1"), new GeoPoint2(0, 0), factor)
            );

            Assert.Equal(text, Text(document));
            Assert.False(document.CanUndo);
        }

        [Theory]
        [InlineData(0.0, 0.0)]
        [InlineData(1E-12, 0.0)]
        [InlineData(5E-10, 5E-10)]
        public void AMirrorLineOfTwoPointsLessThanTheToleranceApart_IsRefusedBeforeAnythingChanges(double dx, double dy)
        {
            // The session's EqualPoint is 1E-9; the points are 0, 1E-12 and 7.1E-10 apart, all under it.
            CadDocument document = Plan();
            CadCommandSession session = Typed(document);
            string text = Text(document);

            Assert.Throws<ArgumentException>(() =>
                session.Editor.Mirror(Given(document, "A1"), new GeoPoint2(0, 0), new GeoPoint2(dx, dy))
            );

            Assert.Equal(text, Text(document));
            Assert.False(document.CanUndo);
        }

        [Fact]
        public void AMirrorLineOfTwoPointsAMillionthApart_IsRefusedAsNoLine_AsTheCommandCannotNormaliseItEither()
        {
            // Probed on the commit this was drafted against: MIRROR across (0, 0)-(1E-6, 0), a thousand times the tolerance's
            // EqualPoint of 1E-9, ends with "*Error* Cannot normalize a zero-length vector." (the library will not normalise a
            // vector shorter than about 2E-5: 1E-5 fails, 5E-5 does not), and writes nothing. The call is to refuse it whole as
            // a degenerate line, in the words of the other degenerate lines, and not to let the library's exception out.
            CadDocument document = Plan();
            CadCommandSession session = Typed(document);
            string text = Text(document);

            Assert.Throws<ArgumentException>(() =>
                session.Editor.Mirror(Given(document, "A2"), new GeoPoint2(0, 0), new GeoPoint2(1E-6, 0))
            );

            Assert.Equal(text, Text(document));
            Assert.False(document.CanUndo);
        }

        [Fact]
        public void AMirrorLineOfTwoPointsAThousandthApart_IsAMirrorLine()
        {
            // 1E-3 is far from the cliff above (5E-5 is the nearest that works): the line from (0, 0) to (1E-3, 0) is the x axis,
            // and A2 (0, 0)-(0, 50) gives (0, 0)-(0, -50).
            CadDocument document = Plan();

            DxfLine copy = Assert.Single(
                Typed(document)
                    .Editor.Mirror(Given(document, "A2").OfType<DxfLine>(), new GeoPoint2(0, 0), new GeoPoint2(1E-3, 0))
            );

            Near(P3(0, 0), copy.StartPoint);
            Near(P3(0, -50), copy.EndPoint);
        }

        [Theory]
        [InlineData(0.0)]
        [InlineData(-5.0)]
        [InlineData(5E-10)]
        [InlineData(double.NaN)]
        [InlineData(double.PositiveInfinity)]
        public void AnOffsetDistanceOfNoughtBelowItOrUnderTheTolerance_IsRefusedBeforeAnythingChanges(double distance)
        {
            // The command refuses these too: "Value must be positive and nonzero." for 0, -5 and 5E-10 (under the EqualPoint of 1E-9).
            CadDocument document = Plan();
            CadCommandSession session = Typed(document);
            string text = Text(document);

            Assert.Throws<ArgumentOutOfRangeException>(() =>
                session.Editor.Offset(Entity<DxfLine>(document, "A1"), distance, new GeoPoint2(50, 10))
            );

            Assert.Equal(text, Text(document));
            Assert.False(document.CanUndo);
        }

        [Fact]
        public void AnOffsetDistanceAMillionth_IsAnOffset()
        {
            // 1E-6 is a thousand times the tolerance: the line from (0, 1E-6) to (100, 1E-6), as the command makes it.
            CadDocument document = Plan();

            DxfCurve made = Assert.Single(
                Typed(document).Editor.Offset(Entity<DxfLine>(document, "A1"), 1E-6, new GeoPoint2(50, 10))
            );

            Near(P3(0, 1E-6), Assert.IsType<DxfLine>(made).StartPoint);
        }

        [Fact]
        public void AnOffsetThatMakesNoCurve_IsRefusedWithTheCommandsWords_ChangingNothing()
        {
            // A circle of radius 10 offset inward by 15 makes nothing, and OFFSET says "Cannot offset that object."
            CadDocument document = Plan();
            CadCommandSession session = Typed(document);
            string text = Text(document);

            InvalidOperationException refused = Assert.Throws<InvalidOperationException>(() =>
                session.Editor.Offset(Entity<DxfCircle>(document, "A3"), 15.0, new GeoPoint2(205, 0))
            );

            Assert.Equal("Cannot offset that object.", refused.Message);
            Assert.Equal(text, Text(document));
            Assert.False(document.CanUndo);
        }

        [Fact]
        public void ACurveOffsetDoesNotTake_IsRefusedWithTheCommandsWords_ChangingNothing()
        {
            // A 3D polyline is a DxfCurve that OFFSET does not take (line, arc, circle, ellipse, polyline, spline, xline and ray).
            CadDocument document = Plan();
            var spine = new DxfPolyline3d(
                DxfPoly3dType.SimplePoly,
                new[] { P3(0, 400), P3(50, 400), P3(50, 450) },
                false
            );
            document.Database.ModelSpace.AppendEntity(spine);
            CadCommandSession session = Typed(document);
            string text = Text(document);

            ArgumentException refused = Assert.Throws<ArgumentException>(() =>
                session.Editor.Offset(spine, 5.0, new GeoPoint2(25, 410))
            );

            Assert.Contains("Cannot offset that object.", refused.Message, StringComparison.Ordinal);
            Assert.Equal(text, Text(document));
            Assert.False(document.CanUndo);
        }

        [Theory]
        [InlineData("Undo")]
        [InlineData("Redo")]
        public void AnObjectKeptAcrossAnUndoOrARedo_IsRefusedAsNotInTheDrawing_ForTheDrawingHasReadItBackInItsPlace(
            string how
        )
        {
            // An undo or a redo reads the drawing back, so the line a plugin kept is no longer the drawing's A1 although it has its
            // handle: a move of it would move nothing the drawing shows, and is refused whole, as an object of another drawing is.
            CadDocument document = Plan();
            CadCommandSession session = Typed(document);
            DxfLine kept = Entity<DxfLine>(document, "A1");
            session.Editor.Move(new[] { kept }, Shift);
            Assert.True(document.Undo());

            if (how == "Redo")
            {
                Assert.True(document.Redo());
            }

            string text = Text(document);
            bool couldRedo = document.CanRedo;

            InvalidOperationException refused = Assert.Throws<InvalidOperationException>(() =>
                session.Editor.Move(new[] { kept }, Shift)
            );

            Assert.Equal("The object A1 is not in the drawing " + document.Title + ".", refused.Message);
            Assert.Equal(text, Text(document));
            Assert.Equal(couldRedo, document.CanRedo);
        }

        [Fact]
        public void TheSessionsToleranceDecidesWhatIsNought_NotTheGlobalOne()
        {
            // With an EqualPoint of 0.01, a mirror line of two points 0.005 apart and an offset of 0.005 are nought, which with the
            // 1E-9 of the other tests they are not.
            CadDocument document = Plan();
            CadCommandSession session = Session(document);
            session.Tolerance = new Tolerance(0.01, 0.01);
            string text = Text(document);

            Assert.Throws<ArgumentException>(() =>
                session.Editor.Mirror(Given(document, "A2"), new GeoPoint2(0, 0), new GeoPoint2(0.005, 0))
            );
            Assert.Throws<ArgumentOutOfRangeException>(() =>
                session.Editor.Offset(Entity<DxfLine>(document, "A1"), 0.005, new GeoPoint2(50, 10))
            );

            Assert.Equal(text, Text(document));
            Assert.False(document.CanUndo);
        }

        [Theory]
        [MemberData(nameof(EveryCallOnAList))]
        public void AnEmptyList_GivesAnEmptyList_AndRecordsNothing(Call call)
        {
            CadDocument document = Plan();
            CadCommandSession session = Typed(document);
            string text = Text(document);
            int written = session.History.Count;

            object result = call.Run(session.Editor, new DxfEntity[0]);

            if (result != null)
            {
                Assert.Empty((System.Collections.IEnumerable)result);
            }

            Assert.Equal(text, Text(document));
            Assert.False(document.CanUndo);
            Assert.False(document.IsModified);
            Assert.Equal(written, session.History.Count);
        }

        #endregion

        #region Undo: a step of its own, or the step of the command running

        [Fact]
        public void OutsideACommand_EachCallIsOneUndoStepNamedAsItsCommand_AndUndoGivesTheDrawingBack()
        {
            CadDocument document = Plan();
            CadCommandSession session = Typed(document);
            string start = Text(document);
            int written = session.History.Count;
            var states = new List<string> { start };

            session.Editor.Move(Given(document, "A1"), Shift);
            states.Add(Text(document));
            session.Editor.Copy(Given(document, "A2"), Shift);
            states.Add(Text(document));
            session.Editor.Rotate(Given(document, "A3"), new GeoPoint2(0, 0), Math.PI / 2.0);
            states.Add(Text(document));
            session.Editor.Scale(Given(document, "A4"), new GeoPoint2(0, 0), 2.0);
            states.Add(Text(document));
            session.Editor.Mirror(Given(document, "A5"), new GeoPoint2(0, 0), new GeoPoint2(0, 10));
            states.Add(Text(document));
            session.Editor.Offset(Entity<DxfLine>(document, "A1"), 5.0, new GeoPoint2(50, 10));
            states.Add(Text(document));
            session.Editor.Erase(Given(document, "A6"));
            states.Add(Text(document));

            // Seven calls, seven steps, taken back one at a time to the text before each; none wrote a line on the command line.
            var names = new List<string>();

            for (int step = states.Count - 2; step >= 0; step--)
            {
                names.Add(document.NextUndo.Name);
                Assert.True(document.Undo());
                Assert.Equal(states[step], Text(document));
            }

            Assert.Equal(new[] { "ERASE", "OFFSET", "MIRROR", "SCALE", "ROTATE", "COPY", "MOVE" }, names);
            Assert.False(document.CanUndo);
            Assert.Equal(written, session.History.Count);
        }

        [Fact]
        public void TheUndoCommandTypedAfterACall_TakesTheCallBack()
        {
            CadDocument document = Plan();
            CadCommandSession session = Typed(document);
            string start = Text(document);

            session.Editor.Copy(Given(document, "A1", "A3"), Shift);
            Run(session, "U");

            Assert.Equal(start, Text(document));
        }

        [Fact]
        public void AnUndoneCall_IsDoneAgainByRedo_ToTheSameText()
        {
            CadDocument document = Plan();
            CadCommandSession session = Typed(document);

            session.Editor.Rotate(Given(document, "A1", "A5"), new GeoPoint2(0, 0), 1.0);
            string done = Text(document);

            Assert.True(document.Undo());
            Assert.True(document.Redo());
            Assert.Equal(done, Text(document));
        }

        private static CadCommandSession WithCommand(
            CadDocument document,
            string name,
            Func<CadCommandContext, IEnumerable<CadPrompt>> body
        )
        {
            CadCommandSession session = Typed(document);
            session.Registry.Add(new CadCommand(name, body));
            return session;
        }

        [Fact]
        public void ACommandThatCopiesMovesAndZooms_IsOneUndoStepNamedAfterTheCommand()
        {
            CadDocument document = Plan();
            string start = Text(document);
            CadEditor seen = null;
            IReadOnlyList<DxfLine> copies = null;
            CadCommandSession session = WithCommand(document, "EDITSTEPS", c =>
            {
                seen = c.Editor;
                copies = c.Editor.Copy(new[] { Entity<DxfLine>(document, "A1") }, new GeoVector2(0, 50));
                c.Editor.Move(Given(document, "A3"), Shift);
                c.Editor.ZoomExtents();
                return Array.Empty<CadPrompt>();
            });

            Run(session, "EDITSTEPS");

            // The context's editor is the session's, made once; the three calls are the one step of the command, which one U takes back.
            Assert.Same(session.Editor, seen);
            Assert.Same(session.Editor, session.Editor);
            Assert.Single(copies);
            Assert.False(session.IsRunning);
            Assert.False(document.IsInGroup);
            Assert.Equal("EDITSTEPS", document.NextUndo.Name);
            Assert.NotEqual(start, Text(document));
            Assert.True(document.Undo());
            Assert.Equal(start, Text(document));
            Assert.False(document.CanUndo);
        }

        [Fact]
        public void ARefusedCallInsideACommand_ChangesNothing_AndTheEditsBeforeItKeepToTheCommandsStep()
        {
            CadDocument document = Plan();
            string start = Text(document);
            string refusal = null;
            string afterMove = null;
            CadCommandSession session = WithCommand(document, "EDITSTEPS", c =>
            {
                c.Editor.Move(Given(document, "A1"), Shift);
                afterMove = Text(document);

                try
                {
                    c.Editor.Rotate(Given(document, "A2", "A8"), new GeoPoint2(0, 0), 1.0);
                }
                catch (InvalidOperationException ex)
                {
                    refusal = ex.Message;
                }

                return Array.Empty<CadPrompt>();
            });

            Run(session, "EDITSTEPS");

            // A8 is on Locked: the rotate of A2 and A8 is refused whole, A2 stays, and the move before it is still the command's step.
            Assert.Equal("The object A8 is on the locked layer Locked.", refusal);
            Assert.Equal(afterMove, Text(document));
            Assert.Equal("EDITSTEPS", document.NextUndo.Name);
            Assert.True(document.Undo());
            Assert.Equal(start, Text(document));
            Assert.False(document.CanUndo);
        }

        [Fact]
        public void ACommandThatAsksForAPointAndThenCallsMove_MakesTheCallInItsOwnStep()
        {
            CadDocument document = Plan();
            string start = Text(document);
            CadCommandSession session = WithCommand(document, "NUDGE", c => Nudge(c, document));

            Run(session, "NUDGE", P(30, 40));

            // The point (30, 40) read at the prompt is the move: A1 from (0, 0) to (30, 40), as the step of NUDGE.
            Near(P3(30, 40), Entity<DxfLine>(document, "A1").StartPoint);
            Assert.False(session.IsRunning);
            Assert.Equal("NUDGE", document.NextUndo.Name);
            Assert.True(document.Undo());
            Assert.Equal(start, Text(document));
            Assert.False(document.CanUndo);
        }

        private static IEnumerable<CadPrompt> Nudge(CadCommandContext c, CadDocument document)
        {
            yield return CadPrompt.ForPoint("Specify the move");
            c.Editor.Move(new[] { Entity<DxfLine>(document, "A1") }, new GeoVector2(c.Input.Point.X, c.Input.Point.Y));
        }

        #endregion

        #region Other spaces

        [Fact]
        public void ALineOnASheetCopied_IsCopiedOntoTheSheet()
        {
            CadDocument document = Blank();
            DxfLine line = AddLineOnSheet(document, out CadSpace sheet);

            DxfLine copy = Assert.Single(Typed(document).Editor.Copy(new[] { line }, new GeoVector2(0, 20)));

            Assert.Equal(sheet.Name, document.FindSpace(copy.Handle).Name);
            Near(P3(0, 20), copy.StartPoint);
        }

        [Fact]
        public void ALineOnASheetOffset_IsOffsetOntoTheSheet()
        {
            CadDocument document = Blank();
            DxfLine line = AddLineOnSheet(document, out CadSpace sheet);

            DxfCurve made = Assert.Single(Typed(document).Editor.Offset(line, 5.0, new GeoPoint2(50, 10)));

            Assert.Equal(sheet.Name, document.FindSpace(made.Handle).Name);
            Near(P3(0, 5), Assert.IsType<DxfLine>(made).StartPoint);
        }

        #endregion
    }

    /// <summary>The edit calls on a drawing of twenty thousand objects, timed.</summary>
    [Collection(ViewerSpeedCollection.Name)]
    public class EditorEditSpeedTests
    {
        private const int Count = 20000;

        /// <summary>A session on a drawing of 20 000 objects and the objects, as a call is given them.</summary>
        private static Tuple<CadCommandSession, DxfEntity[]> Big()
        {
            CadDocument document = CadDocument.From(DxfDatabase.Parse(BigDrawing.Text(Count)), "speed-20k.dxf");
            DxfEntity[] all = document.ModelSpace.Block.Where(e => !e.IsErased).ToArray();
            return Tuple.Create(Session(document), all);
        }

        // Each call below is given the objects of a drawing of 20 000 (lines, arcs, circles, polylines with a bulge, texts and
        // INSERTs of a block of twenty), all of them. The ceiling, 10 s a call (the typed commands take about half a second each on the same drawing), is a blow-up's and not a measure: a check of
        // each object against the whole drawing that grows with the square of the count would pass it at 20 000 and not at
        // 200 000.

        [Fact]
        public void EveryObjectOfAHugeDrawingMovedByOneCall_TakesSeconds_NotMinutes() =>
            SpeedGuard.AssertWithin(10.0, Big, big => big.Item1.Editor.Move(big.Item2, new GeoVector2(10, 5)));

        [Fact]
        public void EveryObjectOfAHugeDrawingCopiedByOneCall_TakesSeconds_NotMinutes() =>
            SpeedGuard.AssertWithin(10.0, Big, big => big.Item1.Editor.Copy(big.Item2, new GeoVector2(10, 5)));

        [Fact]
        public void EveryObjectOfAHugeDrawingErasedByOneCall_TakesSeconds_NotMinutes() =>
            SpeedGuard.AssertWithin(10.0, Big, big => big.Item1.Editor.Erase(big.Item2));
    }
}
