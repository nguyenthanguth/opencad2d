using System;
using System.Collections.Generic;
using System.Linq;
using GeometryHelper.Dxf;
using GeometryHelper.Geometry;
using Xunit;

namespace OpenCAD2D.Remote.UnitTest
{
    /// <summary>
    /// The Properties palette, the entities in an area and the words of LIST, asked of the app
    /// (<see cref="CadRemoteDocument.GetProperties"/>, <see cref="CadRemoteDocument.GetHandlesIn"/>,
    /// <see cref="CadRemoteDocument.ListObjects"/>): the app answers from its own engine and its own selection is left alone.
    /// </summary>
    public class RemoteQueryTests
    {
        private static DxfLine Line(double x1, double y1, double x2, double y2) =>
            new DxfLine(new GeoPoint3(x1, y1, 0), new GeoPoint3(x2, y2, 0));

        /// <summary>A line (10, 10) to (20, 10), a line (0, 0) to (300, 400), a circle of radius 30 about (50, 25) and a line far away.</summary>
        private static CadDocument Plan()
        {
            var db = new DxfDatabase("AC1027");
            db.ModelSpace.AppendEntity(Line(10, 10, 20, 10));
            db.ModelSpace.AppendEntity(Line(0, 0, 300, 400));
            db.ModelSpace.AppendEntity(new DxfCircle { Center = new GeoPoint3(50, 25, 0), Radius = 30 });
            db.ModelSpace.AppendEntity(Line(1000, 1000, 1010, 1000));
            return CadDocument.From(db, "query.dxf");
        }

        private static (Harness harness, CadDocument document, CadRemoteDocument remote) Start()
        {
            CadDocument document = Plan();
            var harness = new Harness();
            var host = new TestHost();
            host.Add(document);
            harness.Serve(host);
            return (harness, document, harness.Connect().Documents.Single());
        }

        private static string[] Handles(CadDocument document) =>
            document.ModelSpace.Block.Where(e => !e.IsErased).Select(e => e.Handle).ToArray();

        [Fact]
        public void TheProperties_AreWhatThePaletteShows_ForOneEntityAndForSeveralOfAKind()
        {
            var (harness, document, remote) = Start();

            using (harness)
            {
                string[] h = Handles(document);

                CadRemoteProperties diagonal = remote.GetProperties(new[] { h[1] });
                CadRemoteProperties lines = remote.GetProperties(new[] { h[0], h[1] });
                CadRemoteProperties mixed = remote.GetProperties(new[] { h[0], h[2] });
                CadRemoteProperties none = remote.GetProperties(Array.Empty<string>());

                Assert.Equal("Line (1)", diagonal.Label);
                CadRemotePropertyRow length = diagonal
                    .Groups.Single(g => g.Title == "Geometry")
                    .Rows.Single(r => r.Label == "Length");
                Assert.Equal("500.0000", length.Value);
                Assert.True(length.IsReadOnly);
                Assert.Equal("Line (2)", lines.Label);
                Assert.Equal(
                    CadPropertyRow.VariesText,
                    lines.Groups.Single(g => g.Title == "Geometry").Rows.Single(r => r.Label == "Length").Value
                );
                Assert.Equal("All (2)", mixed.Label);
                Assert.Equal("No selection", none.Label);
                Assert.Empty(document.Selection.Handles);
            }
        }

        [Fact]
        public void TheHandlesInAnArea_AreTheOnesAWindowAndACrossingTake_WholeAndInTheOrderOfTheSpace()
        {
            var (harness, document, remote) = Start();

            using (harness)
            {
                string[] h = Handles(document);
                document.Selection.Set(new[] { h[3] });
                GeoPoint2[] corners = { new GeoPoint2(0, 0), new GeoPoint2(100, 50) };

                IReadOnlyList<string> window = remote.GetHandlesIn(null, "window", corners, 0.0);
                IReadOnlyList<string> crossing = remote.GetHandlesIn("Model", "crossing", corners, 0.0);
                IReadOnlyList<string> reversed = remote.GetHandlesIn(null, "window", corners.Reverse(), 0.0);

                // The short line lies inside; the circle (radius 30 about (50, 25)) leaves the box, as does the diagonal, which crosses.
                Assert.Equal(new[] { h[0] }, window);
                Assert.Equal(new[] { h[0], h[1], h[2] }, crossing);
                Assert.Equal(window, reversed);
                Assert.Equal(new[] { h[3] }, document.Selection.Handles.ToArray());
            }
        }

        [Fact]
        public void TheHandlesAlongAFenceInAPolygonAndAtAPoint_AreTheOnesTheEngineGives()
        {
            var (harness, document, remote) = Start();

            using (harness)
            {
                string[] h = Handles(document);

                IReadOnlyList<string> fence = remote.GetHandlesIn(
                    null,
                    "fence",
                    new[] { new GeoPoint2(15, 0), new GeoPoint2(15, 30) },
                    0.0
                );
                IReadOnlyList<string> inside = remote.GetHandlesIn(
                    null,
                    "wpolygon",
                    new[] { new GeoPoint2(5, 5), new GeoPoint2(25, 5), new GeoPoint2(25, 15), new GeoPoint2(5, 15) },
                    0.0
                );
                IReadOnlyList<string> touched = remote.GetHandlesIn(
                    null,
                    "cpolygon",
                    new[] { new GeoPoint2(90, 25), new GeoPoint2(120, 25), new GeoPoint2(120, 40) },
                    0.0
                );
                IReadOnlyList<string> point = remote.GetHandlesIn(null, "point", new[] { new GeoPoint2(80, 25) }, 0.5);

                Assert.Equal(new[] { h[0], h[1] }, fence);
                Assert.Equal(new[] { h[0] }, inside);
                Assert.Empty(touched);
                Assert.Equal(new[] { h[2] }, point);
            }
        }

        [Fact]
        public void AnAreaThatIsWrong_IsRefusedInTheEnginesWords()
        {
            var (harness, _, remote) = Start();

            using (harness)
            {
                CadRemoteException window = Assert.Throws<CadRemoteException>(() =>
                    remote.GetHandlesIn(null, "window", new[] { new GeoPoint2(0, 0) }, 0.0)
                );
                CadRemoteException polygon = Assert.Throws<CadRemoteException>(() =>
                    remote.GetHandlesIn(null, "cpolygon", new[] { new GeoPoint2(0, 0), new GeoPoint2(1, 1) }, 0.0)
                );
                CadRemoteException how = Assert.Throws<CadRemoteException>(() =>
                    remote.GetHandlesIn(null, "lasso", new[] { new GeoPoint2(0, 0) }, 0.0)
                );
                CadRemoteException point = Assert.Throws<CadRemoteException>(() =>
                    remote.GetHandlesIn(null, "point", new[] { new GeoPoint2(0, 0) }, 0.0)
                );
                CadRemoteException layout = Assert.Throws<CadRemoteException>(() =>
                    remote.GetHandlesIn("Nowhere", "window", new[] { new GeoPoint2(0, 0), new GeoPoint2(1, 1) }, 0.0)
                );

                Assert.Contains("two corners", window.Message);
                Assert.Contains("three points", polygon.Message);
                Assert.Contains("does not know the selection lasso", how.Message);
                Assert.Contains("above nought", point.Message);
                Assert.Contains("no layout named Nowhere", layout.Message);
            }
        }

        [Fact]
        public void TheLinesOfList_AreTheOnesLISTWrites_AndTheAppsCommandLineAndSelectionAreNotTouched()
        {
            var (harness, document, remote) = Start();

            using (harness)
            {
                string[] h = Handles(document);
                bool undoBefore = document.CanUndo;

                IReadOnlyList<string> lines = remote.ListObjects(new[] { h[1], h[2] });

                string text = string.Join("\n", lines);
                Assert.Contains("LINE", text);
                Assert.Contains("Length = 500.0000", text);
                Assert.Contains("CIRCLE", text);
                Assert.Contains("Handle = " + h[1].ToLowerInvariant(), text);
                Assert.Empty(document.Selection.Handles);
                Assert.Equal(undoBefore, document.CanUndo);
                Assert.Empty(remote.ListObjects(Array.Empty<string>()));
                Assert.Contains(
                    "no object FFFF",
                    Assert.Throws<CadRemoteException>(() => remote.ListObjects(new[] { "FFFF" })).Message
                );
            }
        }
    }
}
