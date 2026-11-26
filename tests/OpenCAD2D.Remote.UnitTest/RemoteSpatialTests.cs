using System;
using System.Linq;
using GeometryHelper;
using GeometryHelper.Dxf;
using GeometryHelper.Geometry;
using Xunit;

namespace OpenCAD2D.Remote.UnitTest
{
    /// <summary>
    /// The boxes the app draws and the overlaps of words, asked from outside (<see cref="CadRemoteDocument.GetBounds"/>,
    /// <see cref="CadRemoteDocument.GetBoundsOf"/>, <see cref="CadRemoteDocument.GetOverlaps"/>): the answer is the one
    /// <see cref="CadSpatial"/> gives in the app, handle for handle and number for number.
    /// </summary>
    public class RemoteSpatialTests
    {
        /// <summary>A TEXT 5 high at (10, 20), a LINE down through its middle, a LINE far away, and a LINE on the first layout.</summary>
        private static CadDocument Plan(out string text, out string through, out string far, out string sheet)
        {
            var db = new DxfDatabase("AC1027");
            var words = new DxfDBText
            {
                TextString = "Plate A-12",
                Position = new GeoPoint3(10, 20, 0),
                Height = 5.0,
            };
            db.ModelSpace.AppendEntity(words);
            var across = new DxfLine(new GeoPoint3(20, 0, 0), new GeoPoint3(20, 60, 0));
            db.ModelSpace.AppendEntity(across);
            var away = new DxfLine(new GeoPoint3(500, 500, 0), new GeoPoint3(600, 500, 0));
            db.ModelSpace.AppendEntity(away);
            CadDocument document = CadDocument.From(db, "spatial.dxf");
            var onSheet = new DxfLine(new GeoPoint3(0, 0, 0), new GeoPoint3(100, 0, 0));
            document.Spaces.First(s => !s.IsModel).Block.AppendEntity(onSheet);
            text = words.Handle;
            through = across.Handle;
            far = away.Handle;
            sheet = onSheet.Handle;
            return document;
        }

        private static (Harness harness, CadRemoteDocument remote) Start(CadDocument document)
        {
            var harness = new Harness();
            var host = new TestHost();
            host.Add(document);
            harness.Serve(host);
            return (harness, harness.Connect().Documents.Single());
        }

        [Fact]
        public void TheBoundsOfASpace_AreTheAppsOwn_RowForRowAndNumberForNumber()
        {
            CadDocument document = Plan(out string text, out string through, out string far, out _);
            CadBoundsTable inApp = CadSpatial.Bounds(document, document.ModelSpace);
            var (harness, remote) = Start(document);

            using (harness)
            {
                CadRemoteBounds got = remote.GetBounds(null);

                Assert.Equal(new[] { text, through, far }, got.Handles);
                Assert.Equal(inApp.Boxes, got.Boxes);
                Assert.Equal(new[] { 500.0, 500.0, 600.0, 500.0 }, got.BoxOf(2));
            }
        }

        [Fact]
        public void TheBoundsOfAHandle_AreFoundInTheSpaceThatHoldsIt_AndOneTheAppDoesNotHaveHasNoRow()
        {
            CadDocument document = Plan(out string text, out _, out _, out string sheet);
            var (harness, remote) = Start(document);

            using (harness)
            {
                CadRemoteBounds got = remote.GetBoundsOf(new[] { sheet, "FFFF", text });
                CadRemoteBounds layout = remote.GetBounds(document.Spaces.First(s => !s.IsModel).Name);

                Assert.Equal(new[] { sheet, text }, got.Handles);
                Assert.Equal(new[] { 0.0, 0.0, 100.0, 0.0 }, got.BoxOf(0));
                Assert.Equal(new[] { sheet }, layout.Handles);
                Assert.Throws<ArgumentNullException>(() => remote.GetBoundsOf(null));
            }
        }

        [Fact]
        public void TheOverlapsOfASpace_AreTheAppsOwn_WithTheKindsAndHandlesAsked()
        {
            CadDocument document = Plan(out string text, out string through, out _, out _);
            var tolerance = new Tolerance(1E-3, 1E-5);
            CadOverlapTable inApp = CadSpatial.Overlaps(document, document.ModelSpace, null, tolerance);
            var (harness, remote) = Start(document);

            using (harness)
            {
                CadRemoteOverlaps all = remote.GetOverlaps(null, null, null, null, 1E-3);
                CadRemoteOverlaps byLine = remote.GetOverlaps(null, null, null, new[] { through }, 1E-3);
                CadRemoteOverlaps circles = remote.GetOverlaps(null, null, new[] { "CIRCLE" }, null, 1E-3);
                CadRemoteOverlaps lines = remote.GetOverlaps(null, new[] { "LINE" }, null, null, 1E-3);

                Assert.Equal(new[] { text }, all.Words);
                Assert.Equal(new[] { through }, all.Others);
                Assert.Equal(inApp.Boxes, all.Boxes);
                Assert.Equal(all.Words, byLine.Words);
                Assert.Equal(0, circles.Count);
                Assert.Equal(0, lines.Count);
            }
        }

        [Fact]
        public void ALayoutThatIsNotThere_IsRefusedInWords()
        {
            CadDocument document = Plan(out _, out _, out _, out _);
            var (harness, remote) = Start(document);

            using (harness)
            {
                CadRemoteException error = Assert.Throws<CadRemoteException>(() => remote.GetBounds("Nowhere"));

                Assert.Contains("Nowhere", error.Message, StringComparison.Ordinal);
            }
        }
    }
}
