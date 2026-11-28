using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using GeometryHelper.Dxf;
using GeometryHelper.Geometry;
using Xunit;

namespace OpenCAD2D.Remote.UnitTest
{
    /// <summary>
    /// The layouts, linetypes and block summaries of a drawing read from outside without copying its database or block objects:
    /// sorted and shaped as the MCP list tools show them, one request each.
    /// </summary>
    public class RemoteTableListTests
    {
        private static CadDocument RichDrawing(string folder, out string viewportHandle)
        {
            var grid = new DxfDatabase("AC1027");
            var tag = new DxfBlockTableRecord { Name = "TAG" };
            grid.BlockTable.Add(tag);
            tag.AppendEntity(new DxfLine(new GeoPoint3(0, 0, 0), new GeoPoint3(0, 10, 0)));
            grid.Save(Path.Combine(folder, "grid.dxf"));

            var db = new DxfDatabase("AC1027");
            CadTemplate.UseMillimetreSheet(db);
            db.LayerTable.Add(new DxfLayerTableRecord { Name = "zeta" });
            db.LayerTable.Add(new DxfLayerTableRecord { Name = "Alpha" });

            var zeta = new DxfLinetypeTableRecord
            {
                Name = "Zeta",
                AsciiDescription = "Dash gap",
                NumDashes = 4,
                PatternLength = 18.0,
            };
            zeta.SetDashLengthAt(0, 12.0);
            zeta.SetDashLengthAt(1, -3.0);
            zeta.SetDashLengthAt(2, 0.0);
            zeta.SetDashLengthAt(3, -3.0);
            db.LinetypeTable.Add(zeta);

            var alpha = new DxfLinetypeTableRecord
            {
                Name = "alpha",
                AsciiDescription = "Short dash",
                NumDashes = 2,
                PatternLength = 6.0,
            };
            alpha.SetDashLengthAt(0, 4.0);
            alpha.SetDashLengthAt(1, -2.0);
            db.LinetypeTable.Add(alpha);

            var nut = new DxfBlockTableRecord { Name = "Nut" };
            db.BlockTable.Add(nut);
            nut.AppendEntity(new DxfLine(new GeoPoint3(0, 0, 0), new GeoPoint3(5, 0, 0)));
            var erasedInNut = new DxfLine(new GeoPoint3(0, 5, 0), new GeoPoint3(5, 5, 0));
            nut.AppendEntity(erasedInNut);
            erasedInNut.Erase();

            var bolt = new DxfBlockTableRecord { Name = "bolt" };
            db.BlockTable.Add(bolt);
            bolt.AppendEntity(new DxfCircle(new GeoPoint3(0, 0, 0), GeoVector3.ZAxis, 5.0));
            bolt.AppendEntity(new DxfLine(new GeoPoint3(-5, 0, 0), new GeoPoint3(5, 0, 0)));

            var anonymous = new DxfBlockTableRecord { Name = "*D" };
            db.BlockTable.Add(anonymous);

            DxfBlockTableRecord xref = db.AttachXref("grid.dxf", "grid");
            db.ModelSpace.AppendEntity(new DxfBlockReference(new GeoPoint3(0, 0, 0), xref));
            db.ModelSpace.AppendEntity(new DxfLine(new GeoPoint3(0, 0, 0), new GeoPoint3(100, 0, 0)));

            string hostPath = Path.Combine(folder, "host.dxf");
            db.Save(hostPath);
            CadDocument document = CadDocument.Open(hostPath);

            CadLayouts.Add(document, "Detail", "ISO_A4", false);
            viewportHandle = CadLayouts.AddViewport(
                document,
                "Detail",
                new[] { 10.0, 20.0, 190.0, 140.0 },
                "1:10",
                new[] { 50.0, 25.0 },
                new[] { "zeta", "Alpha" }
            );
            CadSpace detail = document.Spaces.Single(s => s.Name == "Detail");
            detail.Block.AppendEntity(new DxfLine(new GeoPoint3(0, 0, 0), new GeoPoint3(20, 0, 0)));
            ((DxfViewport)document.FindEntity(viewportHandle)).Locked = true;
            document.ShowSpace(detail);
            return document;
        }

        [Fact]
        public void TheLayouts_CarryPaperViewportsAndCounts_InTabOrder()
        {
            using (var harness = new Harness())
            {
                var host = new TestHost();
                CadDocument document = host.Add(RichDrawing(harness.Folder, out string vpHandle));
                harness.Serve(host);
                CadRemoteDocument remote = harness.Connect().Documents.Single();

                IReadOnlyList<CadRemoteLayout> layouts = remote.GetLayouts();

                Assert.Equal(new[] { "Model", "Layout1", "Detail" }, layouts.Select(l => l.Name).ToArray());
                CadRemoteLayout model = layouts[0];
                Assert.True(model.IsModel);
                Assert.False(model.IsCurrent);
                Assert.Equal(0, model.TabOrder);
                Assert.Null(model.PaperUnits);
                Assert.Equal(2, model.Entities);
                Assert.Empty(model.Viewports);

                CadRemoteLayout detail = layouts[2];
                Assert.False(detail.IsModel);
                Assert.True(detail.IsCurrent);
                Assert.Equal("millimetres", detail.PaperUnits);
                Assert.Equal(2, detail.Entities);
                CadRemoteViewport vp = Assert.Single(detail.Viewports);
                Assert.Equal(vpHandle, vp.Handle);
                Assert.True(vp.IsOn);
                Assert.True(vp.IsLocked);
                Assert.Equal(100.0, vp.CenterX, 6);
                Assert.Equal(80.0, vp.CenterY, 6);
                Assert.Equal(180.0, vp.Width, 6);
                Assert.Equal(120.0, vp.Height, 6);
                Assert.Equal(50.0, vp.ViewCenterX, 6);
                Assert.Equal(25.0, vp.ViewCenterY, 6);
                Assert.Equal(0.1, vp.CustomScale, 6);
                Assert.Equal("1:10", vp.Scale);
                Assert.Equal(new[] { "Alpha", "zeta" }, vp.FrozenLayers.ToArray());
                Assert.NotNull(document);
            }
        }

        [Fact]
        public void TheLinetypes_AreSortedIgnoringCase_WithDescriptionsPatternLengthsAndDashes()
        {
            using (var harness = new Harness())
            {
                var host = new TestHost();
                host.Add(RichDrawing(harness.Folder, out _));
                harness.Serve(host);
                CadRemoteDocument remote = harness.Connect().Documents.Single();

                IReadOnlyList<CadRemoteLinetype> linetypes = remote.GetLinetypes();

                Assert.Equal(
                    new[] { "alpha", "ByBlock", "ByLayer", "Continuous", "Zeta" },
                    linetypes.Select(l => l.Name).ToArray()
                );
                CadRemoteLinetype zeta = linetypes.Single(l => l.Name == "Zeta");
                Assert.Equal("Dash gap", zeta.Description);
                Assert.Equal(18.0, zeta.PatternLength, 6);
                Assert.Equal(new[] { 12.0, -3.0, 0.0, -3.0 }, zeta.Dashes.ToArray());
                Assert.Empty(linetypes.Single(l => l.Name == "Continuous").Dashes);
            }
        }

        [Fact]
        public void TheBlocks_AreSortedIgnoringCase_CountingLiveEntitiesOnly_LeavingLayoutsAnonymousAndXrefsOut()
        {
            using (var harness = new Harness())
            {
                var host = new TestHost();
                host.Add(RichDrawing(harness.Folder, out _));
                harness.Serve(host);
                CadRemoteDocument remote = harness.Connect().Documents.Single();

                IReadOnlyList<CadRemoteBlock> blocks = remote.GetBlocks();

                Assert.Equal(new[] { "bolt", "Nut" }, blocks.Select(b => b.Name).ToArray());
                Assert.Equal(2, blocks.Single(b => b.Name == "bolt").Entities);
                Assert.Equal(1, blocks.Single(b => b.Name == "Nut").Entities);
            }
        }
    }
}
