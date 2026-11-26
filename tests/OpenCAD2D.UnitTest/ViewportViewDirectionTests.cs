using System;
using System.Collections.Generic;
using System.Linq;
using GeometryHelper.Dxf;
using GeometryHelper.Geometry;
using Xunit;
using static OpenCAD2D.UnitTest.EditingDrawings;

namespace OpenCAD2D.UnitTest
{
    /// <summary>
    /// A viewport whose view direction (group 16) is not the world's Z, as AutoCAD writes a viewport set to an isometric or
    /// a front view: model space is drawn through it projected along the direction, its points the viewport's display
    /// coordinates (TRANS 0 to 2), and a viewport looking straight down is drawn as it always was. Entering such a viewport
    /// stays refused: the presenter works in two dimensions.
    /// </summary>
    public class ViewportViewDirectionTests
    {
        private const double Root2 = 1.4142135623730951;
        private const double Root6 = 2.449489742783178;

        /// <summary>
        /// A drawing whose model space holds the twelve edges of a box 100 by 60 by 40 from the origin, and whose Layout1 holds
        /// one viewport 200 by 150 about (150, 100) showing it 300 high about the view's origin, at 1:2, looking along the
        /// direction given (from the target to the eye) at the target given.
        /// </summary>
        private static CadDocument BoxThrough(
            GeoVector3 direction,
            GeoPoint3 target,
            double twist,
            out DxfViewport viewport,
            out CadSpace layout
        )
        {
            DxfDatabase db = CadTemplate.CreateMetric();
            DxfLayout sheet = db.Layouts.First(l => l.LayoutName == "Layout1");
            sheet.Initialize();
            viewport = new DxfViewport
            {
                CenterPoint = new GeoPoint3(150, 100, 0),
                Width = 200,
                Height = 150,
                ViewHeight = 300,
                ViewCenter = new GeoPoint2(0, 0),
            };
            sheet.BlockTableRecord.AppendEntity(viewport);
            viewport.On = true;
            viewport.ViewDirection = direction;
            viewport.ViewTarget = target;
            viewport.TwistAngle = twist;

            foreach (GeoPoint3[] edge in Edges(100, 60, 40))
            {
                db.ModelSpace.AppendEntity(new DxfLine(edge[0], edge[1]));
            }

            CadDocument document = CadDocument.From(db, "box.dxf");
            layout = document.Spaces.First(s => !s.IsModel);
            return document;
        }

        /// <summary>The twelve edges of a box from the origin: the four along X, the four along Y, the four along Z.</summary>
        private static IEnumerable<GeoPoint3[]> Edges(double x, double y, double z)
        {
            foreach (double b in new[] { 0.0, y })
            {
                foreach (double c in new[] { 0.0, z })
                {
                    yield return new[] { new GeoPoint3(0, b, c), new GeoPoint3(x, b, c) };
                }
            }

            foreach (double a in new[] { 0.0, x })
            {
                foreach (double c in new[] { 0.0, z })
                {
                    yield return new[] { new GeoPoint3(a, 0, c), new GeoPoint3(a, y, c) };
                }
            }

            foreach (double a in new[] { 0.0, x })
            {
                foreach (double b in new[] { 0.0, y })
                {
                    yield return new[] { new GeoPoint3(a, b, 0), new GeoPoint3(a, b, z) };
                }
            }
        }

        private static DisplayViewport2 Drawn(CadDocument document, CadSpace layout) =>
            document.GetDisplay(layout).Items.OfType<DisplayViewport2>().Single(v => v.Handle != null);

        /// <summary>The end points of the line items of a display list, X and Y in turn, in the order of the list.</summary>
        private static List<GeoPoint2> Ends(DisplayList2 list)
        {
            var points = new List<GeoPoint2>();

            foreach (DisplayPolyline2 line in list.Items.OfType<DisplayPolyline2>())
            {
                Assert.Equal(4, line.Xy.Count);
                points.Add(new GeoPoint2(line.Xy[0], line.Xy[1]));
                points.Add(new GeoPoint2(line.Xy[2], line.Xy[3]));
            }

            return points;
        }

        [Fact]
        public void ABoxSeenFromTheSouthEast_IsDrawnAsItsDisplayCoordinates()
        {
            CadDocument document = BoxThrough(
                new GeoVector3(1, -1, 1),
                new GeoPoint3(0, 0, 0),
                0.0,
                out DxfViewport viewport,
                out CadSpace layout
            );
            DisplayViewport2 shown = Drawn(document, layout);

            // The view's X runs to the north-east, (1, 1, 0) / sqrt 2, and its Y up the screen, (-1, 1, 2) / sqrt 6, so that
            // the corner (100, 60, 40) of the box is at (160 / sqrt 2, (-100 + 60 + 80) / sqrt 6) = (113.137, 16.330).
            var expected = new List<GeoPoint2>();

            foreach (GeoPoint3[] edge in Edges(100, 60, 40))
            {
                foreach (GeoPoint3 p in edge)
                {
                    expected.Add(P((p.X + p.Y) / Root2, (-p.X + p.Y + 2.0 * p.Z) / Root6));
                }
            }

            List<GeoPoint2> drawn = Ends(shown.Model);
            Assert.Equal(expected.Count, drawn.Count);

            for (int i = 0; i < expected.Count; i++)
            {
                Near(expected[i], drawn[i], 1E-9);
            }

            Near(P(113.137, 16.330), drawn[7], 1E-3);

            // The corner is at the frame's middle, (150, 100), plus half its display coordinates (1:2).
            Assert.Equal(150 + 0.5 * 113.137, shown.ModelToPaper.X(113.137, 16.330), 3);
            Assert.Equal(100 + 0.5 * 16.330, shown.ModelToPaper.Y(113.137, 16.330), 3);

            // The viewport's own arithmetic says the same, for a point at any height.
            Near(P(113.137, 16.330), CadViewports.ToView(viewport, new GeoPoint3(100, 60, 40)), 1E-3);
            Near(P(70.711, -40.825), CadViewports.ToView(viewport, new GeoPoint3(100, 0, 0)), 1E-3);
            Near(P(0, 81.650), CadViewports.ToView(viewport, new GeoPoint3(0, 0, 100)), 1E-3);
        }

        [Fact]
        public void AFrontView_ShowsTheWidthAndTheHeight_AndDropsTheDepth()
        {
            CadDocument document = BoxThrough(
                new GeoVector3(0, -1, 0),
                new GeoPoint3(0, 0, 0),
                0.0,
                out _,
                out CadSpace layout
            );

            // Looking along +Y from the front, X runs to the right and Z up: (100, 60, 40) is seen at (100, 40).
            var seen = new HashSet<(long, long)>(
                Ends(Drawn(document, layout).Model)
                    .Select(p => ((long)Math.Round(p.X * 1000.0), (long)Math.Round(p.Y * 1000.0)))
            );
            Assert.Equal(new HashSet<(long, long)> { (0, 0), (100000, 0), (0, 40000), (100000, 40000) }, seen);
        }

        [Fact]
        public void ATargetAndATwist_MoveAndTurnTheDisplayCoordinatesAsAutoCADsViewDoes()
        {
            CadDocument document = BoxThrough(
                new GeoVector3(1, -1, 1),
                new GeoPoint3(10, 20, 5),
                Math.PI / 6.0,
                out DxfViewport viewport,
                out CadSpace layout
            );
            DisplayViewport2 shown = Drawn(document, layout);

            // The model point (110, 20, 5) is 100 east of the target: (70.711, -40.825) about it, and turned 30 degrees,
            // (70.711 cos 30 + 40.825 sin 30, 70.711 sin 30 - 40.825 cos 30) = (81.650, 0).
            Near(P(81.650, 0.0), CadViewports.ToView(viewport, new GeoPoint3(110, 20, 5)), 1E-3);

            // The list holds the points before the twist, the viewport's transformation turns them: through it the point is
            // at the frame's middle (150, 100) plus half (81.650, 0).
            GeoPoint2 before = P(70.711, -40.825);
            Assert.Equal(150 + 0.5 * 81.650, shown.ModelToPaper.X(before.X, before.Y), 3);
            Assert.Equal(100.0, shown.ModelToPaper.Y(before.X, before.Y), 3);

            // The drawn edge from the origin of the world to (100, 0, 0) is, before the twist, the edge from (-10, -20, -5).
            Near(
                CadViewports.ToView(viewport, new GeoPoint3(0, 0, 0)),
                P(
                    Math.Cos(Math.PI / 6.0) * Ends(shown.Model)[0].X - Math.Sin(Math.PI / 6.0) * Ends(shown.Model)[0].Y,
                    Math.Sin(Math.PI / 6.0) * Ends(shown.Model)[0].X + Math.Cos(Math.PI / 6.0) * Ends(shown.Model)[0].Y
                ),
                1E-9
            );
        }

        [Fact]
        public void AViewportLookingStraightDown_IsDrawnAsItAlwaysWas()
        {
            CadDocument document = BoxThrough(
                new GeoVector3(0, 0, 1),
                new GeoPoint3(0, 0, 0),
                0.0,
                out DxfViewport viewport,
                out CadSpace layout
            );
            DisplayViewport2 shown = Drawn(document, layout);

            Assert.True(CadViewports.HasPlanView(viewport));

            // The very list of model space, as the plan of the world: the heights dropped.
            Assert.Same(document.GetDisplay(document.ModelSpace), shown.Model);
            Assert.Contains(Ends(shown.Model), p => (p - P(100, 60)).Length <= 1E-9);
            Near(P(0, 0), CadViewports.ToView(viewport, new GeoPoint3(0, 0, 40)), 1E-12);
            Assert.DoesNotContain(Ends(shown.Model), p => (p - P(0, 40)).Length <= 1E-9);
        }

        [Fact]
        public void ACircleOnThePlan_SeenFromTheFront_IsASegmentAsWideAsItIs_AndATextIsPlacedByItsPoint()
        {
            CadDocument document = BoxThrough(
                new GeoVector3(0, -1, 0),
                new GeoPoint3(0, 0, 0),
                0.0,
                out _,
                out CadSpace layout
            );
            document.ModelSpace.Block.AppendEntity(
                new DxfCircle(new GeoPoint3(200, 50, 0), new GeoVector3(0, 0, 1), 10)
            );
            document.ModelSpace.Block.AppendEntity(
                new DxfDBText
                {
                    TextString = "FRONT",
                    Position = new GeoPoint3(300, 50, 0),
                    Height = 5,
                }
            );
            document.Regen();
            DisplayViewport2 shown = Drawn(document, layout);

            // The circle, edge on, is a chain of points from x 190 to 210 at no height.
            DisplayPolyline2 circle = shown.Model.Items.OfType<DisplayPolyline2>().Last();
            double[] xy = circle.Xy.ToArray();
            Assert.True(xy.Length > 8);

            for (int i = 0; i < xy.Length; i += 2)
            {
                Assert.InRange(xy[i], 190.0 - 1E-9, 210.0 + 1E-9);
                Assert.Equal(0.0, xy[i + 1], 9);
            }

            Assert.Equal(190.0, xy.Where((v, i) => i % 2 == 0).Min(), 9);
            Assert.Equal(210.0, xy.Where((v, i) => i % 2 == 0).Max(), 9);

            // The text of the plan of the world is placed by where its point is seen: (300, 50, 0) at (300, 0).
            DisplayText2 text = shown.Model.Items.OfType<DisplayText2>().Single();
            Assert.Equal(300.0, text.X, 9);
            Assert.Equal(0.0, text.Y, 9);
        }

        [Fact]
        public void AViewportLookingFromBelow_DoesNotCrash_AndShowsTheBoxProjected()
        {
            CadDocument document = BoxThrough(
                new GeoVector3(0, 0, -1),
                new GeoPoint3(0, 0, 0),
                0.0,
                out _,
                out CadSpace layout
            );

            // Seen end on, the twelve edges are the four of a rectangle 100 by 60 and the four posts, which are points.
            DisplayViewport2 shown = Drawn(document, layout);
            Assert.Equal(12, shown.Model.Items.Count);
            Assert.NotEmpty(Ends(shown.Model));
        }

        [Fact]
        public void AViewportWithAThreeDView_IsNotWorkedThrough_AndMSPACESaysWhy()
        {
            CadDocument document = BoxThrough(
                new GeoVector3(1, -1, 1),
                new GeoPoint3(0, 0, 0),
                0.0,
                out DxfViewport viewport,
                out CadSpace layout
            );
            document.CurrentSpace = layout;
            CadCommandSession session = Session(document);

            Assert.False(CadViewports.HasPlanView(viewport));
            Assert.False(CadViewports.CanWorkThrough(viewport));
            Assert.False(document.EnterViewport(viewport.Handle));
            Assert.Empty(document.EnterableViewports);
            Assert.Null(document.ActiveViewport);

            Run(session, "MSPACE");
            Assert.Null(document.ActiveViewport);
            Assert.Equal(
                "Cannot work in model space through a viewport whose view is not a plan from above (the presenter is 2D).",
                session.History.Last()
            );
        }

        [Fact]
        public void AMovedViewport_IsDrawnWithItsModelProjectedStill()
        {
            CadDocument document = BoxThrough(
                new GeoVector3(1, -1, 1),
                new GeoPoint3(0, 0, 0),
                0.0,
                out DxfViewport viewport,
                out CadSpace layout
            );
            List<GeoPoint2> before = Ends(Drawn(document, layout).Model);
            document.CurrentSpace = layout;
            CadCommandSession session = Session(document);

            Run(session, "MOVE", new[] { viewport.Handle }, Enter, P(0, 0), P(30, 10));

            DisplayViewport2 moved = Drawn(document, layout);
            Assert.Equal(180, moved.CenterX, 9);
            Assert.Equal(110, moved.CenterY, 9);
            List<GeoPoint2> after = Ends(moved.Model);
            Assert.Equal(before.Count, after.Count);

            for (int i = 0; i < before.Count; i++)
            {
                Near(before[i], after[i], 1E-9);
            }
        }
    }
}
