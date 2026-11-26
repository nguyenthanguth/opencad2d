using System.Linq;
using System.Windows;
using GeometryHelper.Dxf;
using Xunit;
using static OpenCAD2D.Wpf.UnitTest.CanvasMouseTests;
using static OpenCAD2D.Wpf.UnitTest.CanvasViewportDoubleClickTests;

namespace OpenCAD2D.Wpf.UnitTest
{
    /// <summary>
    /// What the cursor does on a layout over the model space a viewport shows, beyond the object snap itself
    /// (<see cref="CanvasViewportSnapTests"/>), offscreen on an STA thread: the room's viewport is 200 by 150 about (150, 100)
    /// on the sheet, showing the room at 1:20 about (2 000, 1 500); a model line from (1 000, 500) to (3 000, 2 500) is shown
    /// from (100, 50) to (200, 150). A snap override chosen for the next click, a point acquired by resting the cursor for
    /// object snap tracking, and the dimension a click through the viewport ties to the model line.
    /// </summary>
    public class CanvasViewportThroughTests
    {
        private static void WithModelLine(DxfDatabase db, string viewport) =>
            db.ModelSpace.AppendEntity(
                new DxfLine(
                    new GeometryHelper.Geometry.GeoPoint3(1000, 500, 0),
                    new GeometryHelper.Geometry.GeoPoint3(3000, 2500, 0)
                )
                {
                    Layer = "WALLS",
                }
            );

        /// <summary>Where the cursor goes to ask for a point at a point of the sheet, a few pixels off it.</summary>
        private static Point Near(CadCanvas canvas, double x, double y, double pixels = 3.0)
        {
            Point at = At(canvas, x, y);
            return new Point(at.X + pixels, at.Y - pixels);
        }

        [Fact]
        public void EndChosenForTheNextClick_TakesTheEndOfAModelLineThroughTheViewport_ObjectSnapOff()
        {
            OnLayout(WithModelLine, (model, canvas, viewport) =>
            {
                model.StartCommand("LINE");
                model.Execute("END");
                Assert.Equal(CadSnapModes.Endpoint, model.Snap.Override);

                Click(canvas, Near(canvas, 100, 50));
                Click(canvas, Near(canvas, 200, 150, 6.0));
                model.Execute(string.Empty);

                // The first point is the end as the viewport shows it; the second, with no snap set, is the cursor.
                var line = model.Document.CurrentSpace.Block.Cast<DxfEntity>().OfType<DxfLine>().Single();
                Assert.Equal(100.0, line.StartPoint.X, 6);
                Assert.Equal(50.0, line.StartPoint.Y, 6);
                Assert.NotEqual(200.0, line.EndPoint.X, 3);
            });
        }

        [Fact]
        public void ARestOnTheEndOfAModelLineShownInTheViewport_AcquiresIt_AndTheCursorIsTrackedUprightFromIt()
        {
            OnLayout(WithModelLine, (model, canvas, viewport) =>
            {
                model.Snap.Modes = CadSnapModes.Endpoint;
                model.Toggles.Single(t => t.Name == "OSNAP").IsOn = true;
                model.Toggles.Single(t => t.Name == "OTRACK").IsOn = true;
                model.StartCommand("LINE");

                Mouse(canvas, System.Windows.UIElement.MouseMoveEvent, Near(canvas, 200, 150));
                Assert.True(canvas.AcquireAtRest());

                // The cross is where the viewport shows the end, on the sheet.
                CadAcquiredPoint acquired = Assert.Single(model.Snap.Acquired);
                Assert.Equal(CadSnapKind.Endpoint, acquired.Kind);
                Assert.Equal(200.0, acquired.Point.X, 6);
                Assert.Equal(150.0, acquired.Point.Y, 6);

                // The cursor held above the cross is held on the line up from it, on the sheet.
                Mouse(canvas, System.Windows.UIElement.MouseMoveEvent, At(canvas, 200.3, 60));
                Assert.Equal(CadSnapKind.Tracking, canvas.CurrentSnap.Kind);
                Assert.Equal(200.0, canvas.CurrentSnap.Point.X, 6);
                Assert.Single(canvas.CurrentSnap.Tracks);

                // Rested on again, it is let go.
                Mouse(canvas, System.Windows.UIElement.MouseMoveEvent, Near(canvas, 200, 150));
                Assert.True(canvas.AcquireAtRest());
                Assert.Empty(model.Snap.Acquired);
            });
        }

        [Fact]
        public void ARestOnEmptyPaperInsideTheViewport_AcquiresNothing()
        {
            OnLayout(WithModelLine, (model, canvas, viewport) =>
            {
                model.Snap.Modes = CadSnapModes.Endpoint;
                model.Toggles.Single(t => t.Name == "OSNAP").IsOn = true;
                model.Toggles.Single(t => t.Name == "OTRACK").IsOn = true;
                model.StartCommand("LINE");

                Mouse(canvas, System.Windows.UIElement.MouseMoveEvent, At(canvas, 150, 120));

                Assert.False(canvas.AcquireAtRest());
                Assert.Empty(model.Snap.Acquired);
            });
        }

        [Fact]
        public void ADimLinearClickedThroughTheViewport_IsTiedToTheModelLine_ByATransSpatialDimAssoc()
        {
            OnLayout(WithModelLine, (model, canvas, viewport) =>
            {
                model.Snap.Modes = CadSnapModes.Endpoint;
                model.Toggles.Single(t => t.Name == "OSNAP").IsOn = true;
                model.StartCommand("DIMLINEAR");

                Click(canvas, Near(canvas, 100, 50));
                Click(canvas, Near(canvas, 200, 150));
                Click(canvas, At(canvas, 150, 30));

                var dimension = model
                    .Document.CurrentSpace.Block.Cast<DxfEntity>()
                    .OfType<DxfRotatedDimension>()
                    .Single();
                Assert.NotNull(dimension.DimAssoc);
                Assert.True(dimension.DimAssoc.IsTransSpace);
                Assert.Equal(2, dimension.DimAssoc.PointReferences.Count);
                Assert.All(dimension.DimAssoc.PointReferences, r => Assert.Equal(viewport, r.ViewportHandle));

                // 100 across on the sheet at 1:20 is 2 000 of the room.
                Assert.Equal(-20.0, dimension.Dimlfac, 9);
            });
        }
    }
}
