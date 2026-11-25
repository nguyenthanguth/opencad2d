using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using GeometryHelper.Dxf;
using GeometryHelper.Geometry;
using OpenCAD2D.Plugins;
using Xunit;
using static OpenCAD2D.UnitTest.EditingDrawings;

namespace OpenCAD2D.UnitTest
{
    /// <summary>
    /// How far the object snaps plugins add reach (<see cref="CadCustomSnap"/>): through a viewport of a layout to the model
    /// space it shows, inside blocks, where the finder is given a copy of the entity placed where the references draw it, and
    /// for a point typed after the snap's keyword. The finder is the circle top of <see cref="CustomSnapGatheringTests"/>: a
    /// circle of radius 10 about the origin gives (0, 10); a circle of radius 10 in a block put at (100, 50) at scale 2 is a
    /// circle of radius 20 about (100, 50), and gives (100, 70).
    /// </summary>
    public class CustomSnapReachTests
    {
        private static CadCustomSnap CircleTop(List<CadSnapRequest> seen = null)
        {
            var snap = new CadCustomSnap("Circle top", "CTOP", request =>
            {
                seen?.Add(request);
                return request.Entity is DxfCircle circle
                    ? new[] { P(circle.Center.X, circle.Center.Y + circle.Radius) }
                    : Array.Empty<GeoPoint2>();
            });
            snap.EntityTypes.Add(typeof(DxfCircle));
            return snap;
        }

        private static ObjectSnap Snaps(CadDocument document, params CadCustomSnap[] custom) =>
            new ObjectSnap(Tight)
            {
                Modes = CadSnapModes.None,
                CustomSnaps = custom,
                EntityOf = document.FindEntity,
                Document = document,
            };

        private static DxfCircle Circle(double x, double y, double radius) =>
            new DxfCircle(new GeoPoint3(x, y, 0), new GeoVector3(0, 0, 1), radius);

        /// <summary>A circle of radius 10 about the origin, and the block B of such a circle put at (100, 50) at scale 2.</summary>
        private static CadDocument OneInsert(out string insert, out string inner)
        {
            DxfDatabase database = CadTemplate.CreateMetric();
            database.ModelSpace.AppendEntity(Circle(0, 0, 10));
            var block = new DxfBlockTableRecord { Name = "B", Origin = GeoPoint3.Origin };
            database.BlockTable.Add(block);
            DxfCircle circle = Circle(0, 0, 10);
            block.AppendEntity(circle);
            inner = circle.Handle;
            var reference = new DxfBlockReference(new GeoPoint3(100, 50, 0), block)
            {
                ScaleFactors = new GeoVector3(2, 2, 2),
            };
            database.ModelSpace.AppendEntity(reference);
            insert = reference.Handle;
            return CadDocument.From(database, "reach.dxf");
        }

        /// <summary>
        /// A circle of radius 10 about the origin on the layer WALLS, and the layout L with a viewport 200 by 100 about (150, 100)
        /// looking at (0, 0) over a height of 200, which is a scale of 0.5: the circle's top (0, 10) is at (150, 105) on the sheet.
        /// </summary>
        private static CadDocument OnALayout(bool frozen, out CadSpace layout)
        {
            DxfDatabase database = CadTemplate.CreateMetric();
            var walls = new DxfLayerTableRecord { Name = "WALLS", ColorIndex = 7 };
            database.LayerTable.Add(walls);
            DxfCircle circle = Circle(0, 0, 10);
            database.ModelSpace.AppendEntity(circle);
            circle.Layer = "WALLS";

            var sheet = new DxfLayout { LayoutName = "L" };
            sheet.AddToLayoutDictionary(database, null);
            sheet.Initialize();
            var frame = new DxfViewport
            {
                CenterPoint = new GeoPoint3(150, 100, 0),
                Width = 200,
                Height = 100,
            };
            sheet.BlockTableRecord.AppendEntity(frame);
            frame.ViewCenter = new GeoPoint2(0, 0);
            frame.ViewHeight = 200;
            frame.On = true;

            if (frozen)
            {
                frame.FreezeLayersInViewport(new[] { walls });
            }

            CadDocument document = CadDocument.From(database, "layout.dxf");
            layout = document.Spaces.Single(s => s.Name == "L");
            document.CurrentSpace = layout;
            return document;
        }

        private static CadSnapResult Through(CadDocument document, CadSpace layout, ObjectSnap snaps, GeoPoint2 cursor)
        {
            CadSnapResult own = snaps.Snap(document.GetPickIndex(layout), cursor, 1.0);
            return CadViewportSnap.Through(document, layout, snaps, own, cursor, 1.0, null, null);
        }

        [Fact]
        public void ThroughAViewport_ACustomSnapGivesThePaperPoint_AndNamesTheSnap()
        {
            CadDocument document = OnALayout(false, out CadSpace layout);
            CadCustomSnap snap = CircleTop();

            CadSnapResult result = Through(document, layout, Snaps(document, snap), P(150.2, 104.8));

            Assert.Equal(CadSnapKind.Custom, result.Kind);
            Assert.Same(snap, result.CustomSnap);
            Near(P(150, 105), result.Point);
        }

        [Fact]
        public void ThroughAViewport_TheCustomOverrideAloneAnswers_WithObjectSnapOff()
        {
            CadDocument document = OnALayout(false, out CadSpace layout);
            CadCustomSnap snap = CircleTop();
            ObjectSnap snaps = Snaps(document, snap);
            snaps.IsOn = false;

            Assert.Equal(CadSnapKind.None, Through(document, layout, snaps, P(150.2, 104.8)).Kind);

            snaps.CustomOverride = snap;
            CadSnapResult result = Through(document, layout, snaps, P(150.2, 104.8));

            Assert.Equal(CadSnapKind.Custom, result.Kind);
            Near(P(150, 105), result.Point);
        }

        [Fact]
        public void ThroughAViewport_ALayerFrozenInIt_IsNotSnapped()
        {
            CadDocument document = OnALayout(true, out CadSpace layout);

            CadSnapResult result = Through(document, layout, Snaps(document, CircleTop()), P(150.2, 104.8));

            Assert.Equal(CadSnapKind.None, result.Kind);
        }

        [Fact]
        public void InsideABlock_TheFinderIsGivenThePlacedCircle_AndItsTopIsTaken()
        {
            CadDocument document = OneInsert(out string insert, out string inner);
            var seen = new List<CadSnapRequest>();
            CadCustomSnap snap = CircleTop(seen);

            CadSnapResult result = Snaps(document, snap)
                .Snap(document.GetPickIndex(document.ModelSpace), P(100.5, 69.6), 2.0);

            Assert.Equal(CadSnapKind.Custom, result.Kind);
            Assert.Same(snap, result.CustomSnap);
            Assert.Equal(insert, result.Handle);
            Near(P(100, 70), result.Point);
            CadSnapRequest request = Assert.Single(seen);
            Assert.Equal(new[] { insert }, request.BlockReferences);
            Assert.Equal(inner, request.Handle);
            var placed = Assert.IsType<DxfCircle>(request.Entity);
            Assert.Equal(20.0, placed.Radius, 9);
            Near(new GeoPoint3(100, 50, 0), placed.Center);
        }

        [Fact]
        public void InsideANestedBlock_TheReferencesAreListedOutermostFirst()
        {
            DxfDatabase database = CadTemplate.CreateMetric();
            var small = new DxfBlockTableRecord { Name = "B", Origin = GeoPoint3.Origin };
            database.BlockTable.Add(small);
            small.AppendEntity(Circle(0, 0, 10));
            var large = new DxfBlockTableRecord { Name = "C", Origin = GeoPoint3.Origin };
            database.BlockTable.Add(large);
            var inside = new DxfBlockReference(new GeoPoint3(50, 0, 0), small);
            large.AppendEntity(inside);
            var outside = new DxfBlockReference(new GeoPoint3(100, 50, 0), large)
            {
                ScaleFactors = new GeoVector3(2, 2, 2),
            };
            database.ModelSpace.AppendEntity(outside);
            CadDocument document = CadDocument.From(database, "nested.dxf");
            var seen = new List<CadSnapRequest>();

            // The circle of B is at (50, 0) in C, so at (200, 50) in the drawing with a radius of 20: its top is (200, 70).
            CadSnapResult result = Snaps(document, CircleTop(seen))
                .Snap(document.GetPickIndex(document.ModelSpace), P(200.3, 69.8), 2.0);

            Assert.Equal(CadSnapKind.Custom, result.Kind);
            Assert.Equal(outside.Handle, result.Handle);
            Near(P(200, 70), result.Point);
            Assert.Equal(new[] { outside.Handle, inside.Handle }, Assert.Single(seen).BlockReferences);
        }

        [Fact]
        public void AnInsertOfAnExternalReference_IsNotGoneInto()
        {
            string folder = Path.Combine(
                Path.GetTempPath(),
                "gk",
                "drawing",
                "custom-snap-reach",
                Guid.NewGuid().ToString("N")
            );
            Directory.CreateDirectory(folder);

            try
            {
                var source = new DxfDatabase("AC1027");
                source.ModelSpace.AppendEntity(Circle(0, 0, 10));
                source.Save(Path.Combine(folder, "site.dxf"));
                var host = new DxfDatabase("AC1027");
                DxfBlockTableRecord xref = host.AttachXref(".\\site.dxf", "site");
                host.ModelSpace.AppendEntity(new DxfBlockReference(new GeoPoint3(100, 50, 0), xref));
                string path = Path.Combine(folder, "host.dxf");
                host.Save(path);
                CadDocument document = CadDocument.Open(path);
                Assert.True(document.Database.BlockTable["site"].IsFromExternalReference);
                var seen = new List<CadSnapRequest>();

                CadSnapResult result = Snaps(document, CircleTop(seen))
                    .Snap(document.GetPickIndex(document.ModelSpace), P(100.5, 59.6), 12.0);

                Assert.Empty(seen);
                Assert.Equal(CadSnapKind.None, result.Kind);
            }
            finally
            {
                Directory.Delete(folder, true);
            }
        }

        [Fact]
        public void ATypedPointAfterTheKeyword_IsSnapped()
        {
            CadDocument document = OneInsert(out _, out _);
            CadCommandSession session = Session(document);
            session.SnapAperture = 2.0;
            Run(session, "LINE");

            session.CustomSnapOverride = CircleTop();
            Run(session, "1,9");

            Near(P(0, 10), session.LastPoint);
            Assert.Null(session.CustomSnapOverride);
        }

        [Fact]
        public void ATypedPointWithNothingNear_SaysNoCircleTopFound_AndAsksAgain()
        {
            CadDocument document = OneInsert(out _, out _);
            CadCommandSession session = Session(document);
            session.SnapAperture = 2.0;
            Run(session, "LINE");
            int before = session.History.Count;

            session.CustomSnapOverride = CircleTop();
            Run(session, "30,30");

            Assert.Equal(
                new[] { "Specify first point: 30,30", "No Circle top found for specified point.", "Invalid point." },
                session.History.Skip(before)
            );
            Assert.Equal("Specify first point:", session.PromptText);
            Assert.Null(session.CustomSnapOverride);
        }

        [Fact]
        public void ABuiltInOverrideTyped_EndsTheCustomOne()
        {
            CadDocument document = OneInsert(out _, out _);
            CadCommandSession session = Session(document);
            session.SnapAperture = 2.0;
            Run(session, "LINE");

            session.CustomSnapOverride = CircleTop();
            session.SnapOverride = CadSnapModes.Endpoint;
            Assert.Null(session.CustomSnapOverride);

            // The circle has no endpoint: the built-in override is the one asked, so the custom snap did not give (0, 10).
            Run(session, "1,9");
            Assert.Equal(
                "No Endpoint found for specified point.",
                session.History.Last(l => l.StartsWith("No ", StringComparison.Ordinal))
            );
            Assert.Null(session.SnapOverride);

            session.CustomSnapOverride = CircleTop();
            session.SnapOverride = null;
            Run(session, "1,9");
            Near(P(1, 9), session.LastPoint);
        }

        [Fact]
        public void ATypedPointOnAPlacedCircleInABlock_IsSnapped()
        {
            CadDocument document = OneInsert(out _, out _);
            CadCommandSession session = Session(document);
            session.SnapAperture = 2.0;
            Run(session, "LINE");

            session.CustomSnapOverride = CircleTop();
            Run(session, "101,69");

            Near(P(100, 70), session.LastPoint);
        }

        [Fact]
        public void AFinderThatThrowsOnATypedPoint_GivesTheSameWordsAsNothingFound_AndIsNotSwitchedOff()
        {
            CadDocument document = OneInsert(out _, out _);
            CadCommandSession session = Session(document);
            session.SnapAperture = 2.0;
            Run(session, "LINE");
            var snap = new CadCustomSnap("Circle top", "CTOP", request => throw new InvalidOperationException("no"));

            session.CustomSnapOverride = snap;
            Run(session, "1,9");

            Assert.Contains("No Circle top found for specified point.", session.History);
            Assert.Equal("Specify first point:", session.PromptText);
        }
    }
}
