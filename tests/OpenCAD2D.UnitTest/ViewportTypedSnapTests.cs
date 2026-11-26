using System.Linq;
using GeometryHelper.Dxf;
using GeometryHelper.Geometry;
using Xunit;
using static OpenCAD2D.UnitTest.EditingDrawings;

namespace OpenCAD2D.UnitTest
{
    /// <summary>
    /// A point typed after an object snap override (END, MID, INT) at a point of a layout, which the viewport shows model
    /// space under: the objects of model space the viewport shows within the aperture of the point are snapped to as the
    /// objects of the sheet are, the aperture in the viewport's scale, the point found however far from the typed one and
    /// taken to the sheet by the viewport's view and scale; the nearer of a sheet object's snap and a model one wins, nothing
    /// is found outside the frame or on a layer frozen in the viewport. The layout holds a viewport 200 by 100 about (120, 70)
    /// looking at (50, 25) at 1:2: the model line A from (0, 0) to (100, 0) is shown from (95, 57.5) to (145, 57.5), the line
    /// C across it from (50, -20) to (50, 20) from (120, 47.5) to (120, 67.5).
    /// </summary>
    public class ViewportTypedSnapTests
    {
        private static CadDocument Sheet(out CadCommandSession session, out DxfViewport viewport)
        {
            DxfDatabase database = CadTemplate.CreateMetric();
            database.LayerTable.Add(new DxfLayerTableRecord { Name = "WALLS", ColorIndex = 7 });
            database.ModelSpace.AppendEntity(
                new DxfLine(new GeoPoint3(0, 0, 0), new GeoPoint3(100, 0, 0)) { Layer = "WALLS" }
            );
            database.ModelSpace.AppendEntity(new DxfLine(new GeoPoint3(50, -20, 0), new GeoPoint3(50, 20, 0)));
            var sheet = new DxfLayout { LayoutName = "L" };
            sheet.AddToLayoutDictionary(database, null);
            sheet.Initialize();
            viewport = new DxfViewport
            {
                CenterPoint = new GeoPoint3(120, 70, 0),
                Width = 200,
                Height = 100,
            };
            sheet.BlockTableRecord.AppendEntity(viewport);
            viewport.ViewCenter = new GeoPoint2(50, 25);
            viewport.ViewHeight = 200;
            viewport.On = true;
            CadDocument document = CadDocument.From(database, "sheet.dxf");
            document.CurrentSpace = document.Spaces.Single(s => s.Name == "L");
            session = Session(document);
            session.SnapAperture = 1.0;
            return document;
        }

        private static void At(CadCommandSession session, double x, double y)
        {
            Assert.Equal(x, session.LastPoint.X, 6);
            Assert.Equal(y, session.LastPoint.Y, 6);
        }

        [Fact]
        public void EndTypedBeforeAPointOnTheSheetNearAModelEnd_GivesTheEndWhereTheViewportShowsIt()
        {
            CadDocument document = Sheet(out CadCommandSession session, out _);
            Run(session, "LINE");
            session.SnapOverride = CadSnapModes.Endpoint;

            Run(session, "95.4,57.1");

            At(session, 95, 57.5);
            Assert.Equal("Specify next point or [Undo]:", session.PromptText);
            Assert.Null(session.SnapOverride);
        }

        [Fact]
        public void MidTypedNearTheMiddleOfAModelLine_GivesTheMiddleWhereTheViewportShowsIt()
        {
            CadDocument document = Sheet(out CadCommandSession session, out _);
            Run(session, "LINE");
            session.SnapOverride = CadSnapModes.Midpoint;

            Run(session, "120.4,57.9");

            At(session, 120, 57.5);
        }

        [Fact]
        public void IntTypedWhereTwoModelLinesCrossInTheViewport_GivesTheCrossingWhereTheViewportShowsIt()
        {
            CadDocument document = Sheet(out CadCommandSession session, out _);
            Run(session, "LINE");
            session.SnapOverride = CadSnapModes.Intersection;

            Run(session, "120.3,57.8");

            At(session, 120, 57.5);
        }

        [Fact]
        public void EndTypedFartherThanTheApertureFromAModelEnd_SaysNoEndpointFound()
        {
            CadDocument document = Sheet(out CadCommandSession session, out _);
            Run(session, "LINE");
            session.SnapOverride = CadSnapModes.Endpoint;
            int before = session.History.Count;

            // 1.2 from the end shown at (95, 57.5), the aperture 1 on the sheet.
            Run(session, "95,58.7");

            Assert.Contains("No Endpoint found for specified point.", session.History.Skip(before));
            Assert.Equal("Specify first point:", session.PromptText);
        }

        [Fact]
        public void EndTypedOutsideTheViewportsFrame_SaysNoEndpointFound()
        {
            CadDocument document = Sheet(out CadCommandSession session, out DxfViewport viewport);

            // The frame ends at x = 220: the end of a line at (400, 0) would be shown at (295, 57.5), outside it.
            document.ModelSpace.Block.AppendEntity(new DxfLine(new GeoPoint3(300, 0, 0), new GeoPoint3(400, 0, 0)));
            document.Regen();
            Run(session, "LINE");
            session.SnapOverride = CadSnapModes.Endpoint;
            int before = session.History.Count;

            Run(session, "295,57.5");

            Assert.Contains("No Endpoint found for specified point.", session.History.Skip(before));
        }

        [Fact]
        public void EndTypedAtALayerFrozenInTheViewport_SaysNoEndpointFound()
        {
            CadDocument document = Sheet(out CadCommandSession session, out DxfViewport viewport);
            viewport.FreezeLayersInViewport(new[] { document.Database.LayerTable["WALLS"] });
            document.Regen();
            Run(session, "LINE");
            session.SnapOverride = CadSnapModes.Endpoint;
            int before = session.History.Count;

            Run(session, "95.4,57.1");

            Assert.Contains("No Endpoint found for specified point.", session.History.Skip(before));
        }

        [Fact]
        public void ANearerEndOfTheSheetItself_IsTakenInPlaceOfTheModelOne()
        {
            CadDocument document = Sheet(out CadCommandSession session, out _);
            document.CurrentSpace.Block.AppendEntity(new DxfLine(new GeoPoint3(96, 58, 0), new GeoPoint3(110, 58, 0)));
            document.Regen();
            Run(session, "LINE");
            session.SnapOverride = CadSnapModes.Endpoint;

            Run(session, "95.9,57.9");

            At(session, 96, 58);
        }

        [Fact]
        public void ThePointTypedAtTheSheetOfModelSpace_StillSnapsInModelUnits()
        {
            // The viewport entered, a point typed is model space's own, in model units, as before.
            CadDocument document = Sheet(out CadCommandSession session, out DxfViewport viewport);
            Assert.True(document.EnterViewport(viewport.Handle));
            session.SnapAperture = 2.0;
            Run(session, "LINE");
            session.SnapOverride = CadSnapModes.Endpoint;

            Run(session, "99,1");

            At(session, 100, 0);
        }
    }
}
