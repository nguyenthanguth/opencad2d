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

        /// <summary>
        /// AutoCAD 2021's own display coordinates, <c>(trans p 0 2)</c> (X and Y; the depth is dropped) in a viewport of each
        /// view, read in its console (probe P0, trans-p1 and trans-e): the view direction, the target, the twist in degrees, then
        /// the points, each as its X, Y and Z in the world and its X and Y in the view.
        /// </summary>
        public static IEnumerable<object[]> AutoCADsViews()
        {
            yield return View(
                "south-east",
                "1 -1 1",
                "0 0 0",
                0,
                "100 60 40 113.137085 16.329932; 110 20 5 91.923882 -32.659863; 0 0 0 0 0; 1 0 0 0.707107 -0.408248;"
                    + " 0 1 0 0.707107 0.408248; 100 0 0 70.710678 -40.824829; 0 0 100 0 81.649658;"
                    + " 30 40 50 49.497475 44.907312; -20 70 10 35.355339 44.907312; 120 -35 -8 60.104076 -69.810458"
            );
            yield return View(
                "south-west",
                "-1 -1 1",
                "0 0 0",
                0,
                "100 0 0 70.710678 40.824829; 0 100 0 -70.710678 40.824829; 0 0 100 0 81.649658;"
                    + " 30 40 50 -7.071068 69.402209; -20 70 10 -63.63961 28.57738; 120 -35 -8 109.601551 28.169132"
            );
            yield return View(
                "front",
                "0 -1 0",
                "0 0 0",
                0,
                "100 0 0 100 0; 0 100 0 0 0; 0 0 100 0 100; 30 40 50 30 50; -20 70 10 -20 10; 120 -35 -8 120 -8"
            );
            yield return View(
                "right",
                "1 0 0",
                "0 0 0",
                0,
                "100 0 0 0 0; 0 100 0 100 0; 0 0 100 0 100; 30 40 50 40 50; -20 70 10 70 10; 120 -35 -8 -35 -8"
            );
            yield return View(
                "from below, the south-east",
                "1 1 -1",
                "0 0 0",
                0,
                "100 0 0 -70.710678 40.824829; 0 100 0 70.710678 40.824829; 0 0 100 0 81.649658;"
                    + " 30 40 50 7.071068 69.402209; -20 70 10 63.63961 28.57738; 120 -35 -8 -109.601551 28.169132"
            );

            // Straight below, where the arbitrary axis algorithm turns X round: (100, 60, 40) is at (-100, 60).
            yield return View(
                "bottom",
                "0 0 -1",
                "0 0 0",
                0,
                "100 60 40 -100 60; 110 20 5 -110 20; 1 0 0 -1 0; 0 1 0 0 1"
            );

            // Within 1/64 of the Z axis, where the world's Y is crossed with the direction in place of its Z.
            yield return View(
                "near vertical",
                "0.001 0 1",
                "0 0 0",
                0,
                "100 60 40 99.95995 60; 110 20 5 109.994945 20; 1 0 0 1 0; 0 1 0 0 1"
            );
            yield return View(
                "south-east about a target, twisted 30 degrees",
                "1 -1 1",
                "10 20 5",
                30,
                "100 60 40 75.525934 53.033009; 110 20 5 81.649658 0; 0 0 0 -14.28869 -17.67767;"
                    + " 1 0 0 -13.472194 -17.67767; 0 1 0 -13.880442 -16.970563"
            );
            yield return View(
                "south-east about another target, twisted 30 degrees",
                "1 -1 1",
                "8 2.5 2",
                30,
                "0 0 0 -6.736097 -3.181981; 100 0 0 74.913561 -3.181981; 0 100 0 34.088732 67.528698;"
                    + " 0 0 100 -47.560926 67.528698; 30 40 50 13.676318 60.45763; -20 70 10 1.428869 53.386562;"
                    + " 120 -35 -8 80.220789 -33.587572"
            );

            // A direction of any length: (100, -100, 90) about the target (50, 20, 10).
            yield return View(
                "a long direction about a target",
                "100 -100 90",
                "50 20 10",
                0,
                "0 0 0 -49.497475 2.952772; 100 0 0 21.213203 -35.011437; 0 100 0 21.213203 40.91698;"
                    + " 0 0 100 -49.497475 87.31768; 30 40 50 0 48.931647; -20 70 10 -14.142136 45.55705;"
                    + " 120 -35 -8 10.606602 -62.640944"
            );
        }

        private static object[] View(string name, string direction, string target, double twist, string points) =>
            new object[] { name, direction, target, twist, points };

        private static double[] Numbers(string text) =>
            text.Split(new[] { ' ', ';' }, StringSplitOptions.RemoveEmptyEntries)
                .Select(v => double.Parse(v, System.Globalization.CultureInfo.InvariantCulture))
                .ToArray();

        [Theory]
        [MemberData(nameof(AutoCADsViews))]
        public void ThePointsOfAViewport_AreAtAutoCADsDisplayCoordinates(
            string name,
            string direction,
            string target,
            double twist,
            string points
        )
        {
            double[] d = Numbers(direction);
            double[] t = Numbers(target);
            double[] p = Numbers(points);
            BoxThrough(
                new GeoVector3(d[0], d[1], d[2]),
                new GeoPoint3(t[0], t[1], t[2]),
                twist * Math.PI / 180.0,
                out DxfViewport viewport,
                out _
            );
            Assert.Equal(0, p.Length % 5);

            for (int i = 0; i < p.Length; i += 5)
            {
                Near(
                    P(p[i + 3], p[i + 4]),
                    CadViewports.ToView(viewport, new GeoPoint3(p[i], p[i + 1], p[i + 2])),
                    2E-5
                );
            }

            Assert.NotNull(name);
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
