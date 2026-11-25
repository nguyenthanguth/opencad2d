using GeometryHelper.Geometry;
using OpenCAD2D.Plugins;
using Xunit;

namespace OpenCAD2D.Wpf.UnitTest
{
    /// <summary>
    /// The window and the editor of a drawing's session (A6): a view moved by a call of the editor is shown at once and stays
    /// where it was put when the canvas changes size, as one moved by a typed ZOOM does, and a plugin's callback is given the
    /// editor of the drawing shown.
    /// </summary>
    public class EditorWindowTests
    {
        [Fact]
        public void AnApiZoomOfTheDrawingShown_BumpsTheRevision_AndSurvivesANewCanvasSize()
        {
            using (var folder = new TestFolder())
            {
                var model = new MainViewModel();
                model.Open(folder.Write("room.dxf", TestFiles.Room(out _, out _)));
                model.SetCanvasSize(1000, 600);
                int revision = model.Revision;

                model.Session.Editor.ZoomWindow(new GeoPoint2(1000, 500), new GeoPoint2(2000, 1500));

                Assert.True(model.Revision > revision);
                Assert.Equal(0.6, model.View.PixelsPerUnit, 9);
                Assert.Equal(1500.0, model.View.CenterX, 6);

                // A canvas twice the size would have refitted a view still fitted to its extents.
                model.SetCanvasSize(2000, 1200);

                Assert.Equal(0.6, model.View.PixelsPerUnit, 9);
                Assert.Equal(1500.0, model.View.CenterX, 6);
            }
        }

        [Fact]
        public void EveryViewCallOfTheEditor_TellsTheWindow_ThePanAndTheZoomPreviousAndTheNamedViewToo()
        {
            using (var folder = new TestFolder())
            {
                var model = new MainViewModel();
                model.Open(folder.Write("room.dxf", TestFiles.Room(out _, out _)));
                model.SetCanvasSize(1000, 600);
                CadEditor editor = model.Session.Editor;
                int revision = model.Revision;

                editor.Pan(new GeoVector2(100, 0));
                Assert.True(model.Revision > revision);
                revision = model.Revision;

                editor.ZoomScaled(3.0);
                Assert.True(model.Revision > revision);
                revision = model.Revision;

                editor.SaveView("Zoomed");
                editor.ZoomExtents();
                editor.RestoreView("Zoomed");
                Assert.True(model.Revision > revision);
                double zoom = model.View.PixelsPerUnit;

                model.SetCanvasSize(2000, 1200);

                Assert.Equal(zoom, model.View.PixelsPerUnit, 9);
            }
        }

        [Fact]
        public void ATypedZoomWindow_SurvivesANewCanvasSize_AsTheUserMovedItNotRefitted()
        {
            using (var folder = new TestFolder())
            {
                var model = new MainViewModel();
                model.Open(folder.Write("room.dxf", TestFiles.Room(out _, out _)));
                model.SetCanvasSize(1000, 600);

                model.Execute("ZOOM W");
                model.ClickAt(new GeoPoint2(1000, 500), 1.0);
                model.ClickAt(new GeoPoint2(2000, 1500), 1.0);
                Assert.Equal(0.6, model.View.PixelsPerUnit, 9);

                model.SetCanvasSize(2000, 1200);

                Assert.Equal(0.6, model.View.PixelsPerUnit, 9);
            }
        }

        [Fact]
        public void ACallbacksContext_HasTheEditorOfTheDrawingShown_AndNullWithNoDrawing()
        {
            using (var folder = new TestFolder())
            {
                var model = new MainViewModel();

                CadPluginContext none = model.PluginContext();
                Assert.Null(none.Document);
                Assert.Null(none.Editor);

                model.Open(folder.Write("room.dxf", TestFiles.Room(out _, out _)));
                model.SetCanvasSize(1000, 600);
                CadPluginContext context = model.PluginContext();

                Assert.Same(model.Session.Editor, context.Editor);
                Assert.Same(model.Document, context.Editor.Document);
                Assert.True(context.Editor.HasWindow);

                context.Editor.ZoomExtents();

                Assert.Equal(0.18, model.View.PixelsPerUnit, 9);
            }
        }
    }
}
