using System;
using System.Linq;
using GeometryHelper.Dxf;
using GeometryHelper.Geometry;
using Xunit;

namespace OpenCAD2D.Remote.UnitTest
{
    /// <summary>
    /// The sheets of a drawing of the app made and set up from outside (<see cref="CadRemoteDocument.AddLayout"/>,
    /// <see cref="CadRemoteDocument.SetPageSetup"/>, <see cref="CadRemoteDocument.AddViewport"/>): the app does what
    /// <see cref="CadLayouts"/> does, a refusal comes back with its words, and a command that runs holds the writes off.
    /// </summary>
    public class RemoteSheetsTests
    {
        private static CadDocument Plate()
        {
            var db = new DxfDatabase("AC1027");
            CadTemplate.UseMillimetreSheet(db);
            db.ModelSpace.AppendEntity(new DxfLine(new GeoPoint3(0, 0, 0), new GeoPoint3(100, 0, 0)));
            db.LayerTable.Add(new DxfLayerTableRecord { Name = "Notes" });
            return CadDocument.From(db, "plate.dxf");
        }

        private static (Harness harness, CadRemoteDocument remote) Start(CadDocument document)
        {
            var harness = new Harness();
            var host = new TestHost();
            host.Add(document);
            harness.Serve(host);
            return (harness, harness.Connect().Documents.First());
        }

        [Fact]
        public void ALayoutAddedFromOutside_IsInTheApp_WithItsSheet_AsOneUndoStep()
        {
            CadDocument document = Plate();
            var (harness, remote) = Start(document);

            using (harness)
            {
                string made = remote.AddLayout("Detail", "ISO_A4", true);

                Assert.Contains("\"name\":\"Detail\"", made);
                Assert.Contains("\"paper\":\"ISO A4\"", made);
                Assert.Contains("\"scale\":\"1:1\"", made);
                Assert.Equal(new[] { "Model", "Layout1", "Detail" }, remote.Layouts);
                Assert.Equal(new[] { "Model", "Layout1", "Detail" }, document.Spaces.Select(s => s.Name));
                Assert.True(document.Undo());
                Assert.Equal(new[] { "Model", "Layout1" }, document.Spaces.Select(s => s.Name));
            }
        }

        [Fact]
        public void APageSetupFromOutside_ChangesOnlyWhatIsGiven_AndTheListSaysSo()
        {
            CadDocument document = Plate();
            var (harness, remote) = Start(document);

            using (harness)
            {
                string changed = remote.SetPageSetup("Layout1", "A2", null, "extents", "1:50", "acad.ctb");

                Assert.Contains("\"paper\":\"ISO A2\"", changed);
                Assert.Contains("\"scale\":\"1:50\"", changed);
                Assert.Contains("\"area\":\"extents\"", changed);
                Assert.Contains("\"plotStyle\":\"acad.ctb\"", changed);
                Assert.Equal("1:50", CadLayouts.List(document).First(l => l.Name == "Layout1").Scale);
                string list = remote.GetLayoutSetups();
                Assert.StartsWith("[{\"name\":\"Model\"", list);
                Assert.Contains("\"name\":\"Layout1\"", list);
            }
        }

        [Fact]
        public void AViewportFromOutside_IsOnTheSheetAtItsScale_FrozenWhereAsked()
        {
            CadDocument document = Plate();
            var (harness, remote) = Start(document);

            using (harness)
            {
                string handle = remote.AddViewport(
                    "Layout1",
                    new[] { 20.0, 30.0, 220.0, 130.0 },
                    "1:10",
                    new[] { 50.0, 30.0 },
                    new[] { "Notes" }
                );

                var viewport = (DxfViewport)document.FindEntity(handle);
                Assert.Equal(1000.0, viewport.ViewHeight, 6);
                Assert.Equal(50.0, viewport.ViewCenter.X, 6);
                Assert.Equal(new[] { "Notes" }, viewport.GetFrozenLayers().Select(l => l.Name));
                Assert.Equal(1, CadLayouts.List(document).First(l => l.Name == "Layout1").Viewports);
            }
        }

        [Fact]
        public void ARefusalFromTheSheetsComesBackInWords_AndACommandThatRunsHoldsTheWritesOff()
        {
            CadDocument document = Plate();
            var (harness, remote) = Start(document);

            using (harness)
            {
                CadRemoteException sheet = Assert.Throws<CadRemoteException>(() => remote.AddLayout("Odd", "A7", true));
                Assert.Contains("ISO A3", sheet.Message);
                CadRemoteException scale = Assert.Throws<CadRemoteException>(() =>
                    remote.SetPageSetup("Layout1", null, null, null, "1:0", null)
                );
                Assert.Contains("scale", scale.Message);
                CadRemoteException layer = Assert.Throws<CadRemoteException>(() =>
                    remote.AddViewport("Layout1", new[] { 0.0, 0.0, 10.0, 10.0 }, "1:1", null, new[] { "Missing" })
                );
                Assert.Contains("Missing", layer.Message);
                Assert.Throws<ArgumentNullException>(() => remote.AddLayout(null, null, true));

                document.BeginGroup("LINE");
                CadRemoteException busy = Assert.Throws<CadRemoteException>(() =>
                    remote.AddLayout("Detail", null, true)
                );
                Assert.Contains("busy", busy.Message);
                document.CancelGroup();
                Assert.Equal(new[] { "Model", "Layout1" }, remote.Layouts);
            }
        }
    }
}
