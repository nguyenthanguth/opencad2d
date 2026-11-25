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
    /// The blocks of the drawing of a loaded external reference (<c>grid|TAG</c>), read from outside as AutoCAD lists them in
    /// its block table (accoreconsole: <c>tblnext</c> says <c>70 48</c>, -BLOCK ? says <c>Xdep: "grid"</c>): carried in the
    /// answer with their objects, flagged dependent and resolved, none once the reference is unloaded or its file gone, and
    /// never in the app's own drawing.
    /// </summary>
    public class RemoteDependentBlockTests
    {
        private static DxfLine Line(double x1, double y1, double x2, double y2) =>
            new DxfLine(new GeoPoint3(x1, y1, 0), new GeoPoint3(x2, y2, 0));

        /// <summary>The drawing grid with the blocks TAG (a line from (0, 0) to (0, 10)) and PLAN (two lines), and a host with the block Bolt, grid attached and placed.</summary>
        private static (Harness harness, CadDocument document, CadRemoteDocument remote) Start(
            string gridFile = "grid.dxf"
        )
        {
            var harness = new Harness();
            var grid = new DxfDatabase("AC1027");
            var tag = new DxfBlockTableRecord { Name = "TAG" };
            grid.BlockTable.Add(tag);
            tag.AppendEntity(Line(0, 0, 0, 10));
            var plan = new DxfBlockTableRecord { Name = "PLAN" };
            grid.BlockTable.Add(plan);
            plan.AppendEntity(Line(0, 0, 5, 0));
            plan.AppendEntity(Line(5, 0, 5, 5));
            grid.Save(Path.Combine(harness.Folder, "grid.dxf"));

            var host = new DxfDatabase("AC1027");
            var bolt = new DxfBlockTableRecord { Name = "Bolt" };
            host.BlockTable.Add(bolt);
            bolt.AppendEntity(Line(0, 0, 10, 0));
            DxfBlockTableRecord reference = host.AttachXref("grid.dxf", "grid");
            host.ModelSpace.AppendEntity(new DxfBlockReference(new GeoPoint3(0, 0, 0), reference));
            string path = Path.Combine(harness.Folder, "A-101.dxf");
            host.Save(path);

            CadDocument document = CadDocument.Open(path);
            var testHost = new TestHost();
            testHost.Add(document);
            harness.Serve(testHost);
            return (harness, document, harness.Connect().Documents.Single());
        }

        [Fact]
        public void TheBlocks_CarryTheBlocksOfALoadedReference_AsGridBarTag_WithTheirObjects_SortedWithTheRest()
        {
            var (harness, document, remote) = Start();
            using (harness)
            {
                IReadOnlyList<DxfBlockTableRecord> blocks = remote.Blocks;

                Assert.Equal(new[] { "Bolt", "grid", "grid|PLAN", "grid|TAG" }, blocks.Select(b => b.Name));
                DxfBlockTableRecord tag = blocks.Single(b => b.Name == "grid|TAG");
                Assert.True(tag.IsDependent);
                Assert.True(tag.IsResolved);
                Assert.False(tag.IsFromExternalReference);
                DxfLine line = Assert.IsType<DxfLine>(Assert.Single(tag.Cast<DxfEntity>()));
                Assert.Equal(10.0, line.EndPoint.Y, 9);
                Assert.Equal(2, blocks.Single(b => b.Name == "grid|PLAN").Count());
                Assert.True(blocks.Single(b => b.Name == "grid").IsFromExternalReference);
                Assert.False(blocks.Single(b => b.Name == "Bolt").IsDependent);

                // The app's own drawing holds none of them, and the read changed nothing.
                Assert.False(document.Database.BlockTable.Has("grid|TAG"));
                Assert.False(document.IsModified);
            }
        }

        [Fact]
        public void AReferenceUnloaded_OrWhoseFileIsGone_BringsNoBlock()
        {
            var (harness, document, remote) = Start();
            using (harness)
            {
                document.Xrefs.SetUnloaded("grid", true);

                Assert.Equal(new[] { "Bolt", "grid" }, remote.Blocks.Select(b => b.Name));

                document.Xrefs.SetUnloaded("grid", false);
                Assert.Equal(4, remote.Blocks.Count);

                File.Delete(Path.Combine(harness.Folder, "grid.dxf"));

                Assert.Equal(new[] { "Bolt", "grid" }, remote.Blocks.Select(b => b.Name));
            }
        }

        [Fact]
        public void TheDependentBlocks_ComeToo_WhenTheDocumentIsOnlyAskedForItsReferencesBlocks()
        {
            var (harness, document, _) = Start();
            using (harness)
            {
                IReadOnlyList<DxfBlockTableRecord> blocks = document.Xrefs.GetDependentBlocks();

                Assert.Equal(new[] { "grid|PLAN", "grid|TAG" }, blocks.Select(b => b.Name));
                Assert.All(blocks, b => Assert.True(b.IsResolved));
            }
        }
    }
}
