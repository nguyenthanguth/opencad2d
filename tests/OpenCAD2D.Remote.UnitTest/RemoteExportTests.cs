using System;
using System.Linq;
using GeometryHelper.Dxf;
using GeometryHelper.Geometry;
using Xunit;

namespace OpenCAD2D.Remote.UnitTest
{
    /// <summary>
    /// The drawing as JSON and as a graph seen from outside the app (PW6.3): <see cref="CadRemoteDocument.ExportJson"/> and
    /// <see cref="CadRemoteDocument.ExportGraph"/> give the text the core writes, and a layout the drawing lacks is refused in words.
    /// </summary>
    public class RemoteExportTests
    {
        /// <summary>A drawing of millimetres with a block of a line and a circle inserted twice, and two lines that meet.</summary>
        private static CadDocument Drawing()
        {
            var db = new DxfDatabase("AC1027");
            db.Insunits = DxfUnits.Inches;
            var block = new DxfBlockTableRecord { Name = "BOLT", Origin = new GeoPoint3(0, 0, 0) };
            db.BlockTable.Add(block);
            block.AppendEntity(new DxfLine(new GeoPoint3(0, 0, 0), new GeoPoint3(5, 0, 0)));
            block.AppendEntity(new DxfCircle(new GeoPoint3(5, 0, 0), new GeoVector3(0, 0, 1), 1.0));
            db.ModelSpace.AppendEntity(new DxfBlockReference(new GeoPoint3(100, 100, 0), block));
            db.ModelSpace.AppendEntity(
                new DxfBlockReference(new GeoPoint3(200, 100, 0), block) { Rotation = Math.PI / 2 }
            );
            db.ModelSpace.AppendEntity(new DxfLine(new GeoPoint3(0, 0, 0), new GeoPoint3(10, 0, 0)));
            db.ModelSpace.AppendEntity(new DxfLine(new GeoPoint3(10, 0, 0), new GeoPoint3(10, 10, 0)));
            return CadDocument.From(db, "A-101.dxf");
        }

        private static (Harness harness, CadDocument document, CadRemoteDocument remote) Start()
        {
            var harness = new Harness();
            var host = new TestHost();
            CadDocument document = Drawing();
            host.Add(document);
            harness.Serve(host);
            return (harness, document, harness.Connect().Documents.Single());
        }

        [Fact]
        public void TheJsonThroughTheApp_IsTheTextTheCoreWrites()
        {
            var (harness, document, remote) = Start();
            using (harness)
            {
                string text = remote.ExportJson(null);

                Assert.Equal(CadDrawingJson.Write(document, null), text);
                Assert.Contains("\"toMillimetres\":25.4", text);
                Assert.Contains("\"path\":[\"BOLT\"]", text);
                Assert.Equal(CadDrawingJson.Write(document, "Model"), remote.ExportJson("Model"));
            }
        }

        [Fact]
        public void TheGraphThroughTheApp_IsTheTextTheCoreWrites_ToTheToleranceAsked()
        {
            var (harness, document, remote) = Start();
            using (harness)
            {
                string text = remote.ExportGraph(null, 0.001);

                Assert.Equal(CadDrawingGraph.Build(document, null, 0.001).ToJson(), text);
                Assert.Contains("\"kind\":\"touches\"", text);
                Assert.NotEqual(text, remote.ExportGraph(null, 1000.0));
            }
        }

        [Fact]
        public void TheGraphThroughTheApp_IsCutAtTheCapAsked_AsTheCoreCutsIt()
        {
            var (harness, document, remote) = Start();
            using (harness)
            {
                int count = CadDrawingGraph.Build(document, null, 0.001).Edges.Count;
                Assert.True(count >= 2);

                for (int cap = 1; cap <= count + 1; cap++)
                {
                    Assert.Equal(
                        CadDrawingGraph.Build(document, null, 0.001, cap).ToJson(),
                        remote.ExportGraph(null, 0.001, cap)
                    );
                }

                Assert.Contains("\"truncated\":true", remote.ExportGraph(null, 0.001, 1));
                Assert.Contains("\"truncated\":false", remote.ExportGraph(null, 0.001, count));
                Assert.Contains("\"truncated\":false", remote.ExportGraph(null, 0.001));
            }
        }

        [Fact]
        public void ACapBelowOne_IsRefusedInWords()
        {
            var (harness, _, remote) = Start();
            using (harness)
            {
                CadRemoteException refused = Assert.Throws<CadRemoteException>(() =>
                    remote.ExportGraph(null, 0.001, 0)
                );

                Assert.Contains("number of edges above nought", refused.Message);
            }
        }

        [Fact]
        public void ALayoutTheDrawingLacks_AndAToleranceBelowNought_AreRefusedInWords()
        {
            var (harness, _, remote) = Start();
            using (harness)
            {
                CadRemoteException layout = Assert.Throws<CadRemoteException>(() => remote.ExportJson("Nowhere"));
                CadRemoteException tolerance = Assert.Throws<CadRemoteException>(() => remote.ExportGraph(null, 0.0));

                Assert.Contains("no layout named Nowhere", layout.Message);
                Assert.Contains("millimetres above nought", tolerance.Message);
            }
        }
    }
}
