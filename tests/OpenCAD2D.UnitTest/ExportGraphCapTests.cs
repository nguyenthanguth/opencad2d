using System;
using System.Collections.Generic;
using System.Linq;
using GeometryHelper;
using GeometryHelper.Dxf;
using GeometryHelper.Geometry;
using Xunit;
using static OpenCAD2D.UnitTest.EditingDrawings;
using static OpenCAD2D.UnitTest.ExportJsonTools;

namespace OpenCAD2D.UnitTest
{
    /// <summary>
    /// The cap on the edges of the graph (<c>CadDrawingGraph.Build</c> with <c>maxEdges</c>): the edges kept are the first of the
    /// full graph in its order (kind, source, target), the graph says it was cut and how many edges had been found, and a
    /// cap the graph does not reach changes nothing.
    /// </summary>
    public class ExportGraphCapTests
    {
        /// <summary>
        /// Model space and Layout1 each hold a ladder of lines that cross a long rail, their handles interleaved (one of model
        /// space, one of the layout, and so on), with a plate holding a circle, so that touches, contains and the layouts all
        /// have edges.
        /// </summary>
        private static CadDocument Ladders()
        {
            CadDocument document = Blank();
            document.Database.Insunits = DxfUnits.Millimeters;
            DxfBlockTableRecord model = document.Database.ModelSpace;
            DxfBlockTableRecord sheet = document.Spaces.First(s => !s.IsModel).Block;
            model.AppendEntity(new DxfLine(new GeoPoint3(0, 0, 0), new GeoPoint3(100, 0, 0)));
            sheet.AppendEntity(new DxfLine(new GeoPoint3(0, 0, 0), new GeoPoint3(100, 0, 0)));

            for (int i = 0; i < 6; i++)
            {
                model.AppendEntity(new DxfLine(new GeoPoint3(10 * i + 5, -5, 0), new GeoPoint3(10 * i + 5, 5, 0)));
                sheet.AppendEntity(new DxfLine(new GeoPoint3(10 * i + 5, -5, 0), new GeoPoint3(10 * i + 5, 5, 0)));
            }

            var plate = new DxfPolyline();
            plate.AddVertexAt(0, new GeoPoint2(200, 0), 0.0, 0.0, 0.0);
            plate.AddVertexAt(1, new GeoPoint2(300, 0), 0.0, 0.0, 0.0);
            plate.AddVertexAt(2, new GeoPoint2(300, 50), 0.0, 0.0, 0.0);
            plate.AddVertexAt(3, new GeoPoint2(200, 50), 0.0, 0.0, 0.0);
            plate.Closed = true;
            model.AppendEntity(plate);
            model.AppendEntity(new DxfCircle(new GeoPoint3(250, 25, 0), new GeoVector3(0, 0, 1), 10.0));
            return document;
        }

        private static List<(int, int, CadGraphEdgeKind)> Keys(CadDrawingGraph graph) =>
            graph.Edges.Select(e => (e.Source, e.Target, e.Kind)).ToList();

        [Fact]
        public void ACappedGraph_HoldsTheFirstEdgesOfTheFullGraph_InItsOrder_ForEveryCap()
        {
            CadDocument document = Ladders();
            CadDrawingGraph full = CadDrawingGraph.Build(document, null, 0.001);
            Assert.False(full.Truncated);
            Assert.Equal(full.Edges.Count, full.EdgesFound);
            Assert.True(full.Edges.Count > 10);
            Assert.Contains(full.Edges, e => e.Kind == CadGraphEdgeKind.Contains);
            Assert.Contains(full.Edges, e => e.Kind == CadGraphEdgeKind.Touches);
            List<(int, int, CadGraphEdgeKind)> all = Keys(full);

            for (int cap = 1; cap <= all.Count + 1; cap++)
            {
                CadDrawingGraph capped = CadDrawingGraph.Build(document, null, 0.001, cap);

                Assert.Equal(all.Take(cap).ToList(), Keys(capped));
                Assert.Equal(cap < all.Count, capped.Truncated);
                Assert.Equal(capped.Truncated ? cap + 1 : all.Count, Math.Min(capped.EdgesFound, cap + 1));
                Assert.True(capped.EdgesFound >= capped.Edges.Count);
                Assert.Equal(full.NodeCount, capped.NodeCount);
            }
        }

        [Fact]
        public void ACapAtTheNumberOfEdges_IsNotTruncated_AndOneBelowIs()
        {
            CadDocument document = Ladders();
            int count = CadDrawingGraph.Build(document, null, 0.001).Edges.Count;

            CadDrawingGraph exact = CadDrawingGraph.Build(document, null, 0.001, count);
            CadDrawingGraph below = CadDrawingGraph.Build(document, null, 0.001, count - 1);

            Assert.False(exact.Truncated);
            Assert.Equal(count, exact.Edges.Count);
            Assert.True(below.Truncated);
            Assert.Equal(count - 1, below.Edges.Count);
            Assert.Equal(count, below.EdgesFound);
        }

        [Fact]
        public void ACappedGraph_IsTheSameOnEveryRun_AndTheSameFromAWalkOrADocument()
        {
            CadDocument document = Ladders();

            string first = CadDrawingGraph.Build(document, null, 0.001, 7).ToJson();
            string second = CadDrawingGraph.Build(CadFlatten.Walk(document, null), 0.001, 7).ToJson();

            Assert.Equal(first, second);
        }

        [Fact]
        public void TheJson_SaysWhetherTheGraphWasCut_AndHowManyEdgesWereFound()
        {
            CadDocument document = Ladders();
            int count = CadDrawingGraph.Build(document, null, 0.001).Edges.Count;

            Dictionary<string, object> whole = Obj(Parse(CadDrawingGraph.Build(document, null, 0.001).ToJson()));
            Dictionary<string, object> cut = Obj(Parse(CadDrawingGraph.Build(document, null, 0.001, 5).ToJson()));

            Assert.Equal(false, whole["truncated"]);
            Assert.Equal((double)count, (double)whole["edgesFound"]);
            Assert.Equal(true, cut["truncated"]);
            Assert.Equal(5, Arr(cut["edges"]).Count);
            Assert.True((double)cut["edgesFound"] >= 6.0);
            Assert.True((double)cut["edgesFound"] <= count);
        }

        [Fact]
        public void ACapOfNoEdges_OrLess_IsRefused()
        {
            CadDocument document = Ladders();

            Assert.Throws<ArgumentException>(() => CadDrawingGraph.Build(document, null, 0.001, 0));
            Assert.Throws<ArgumentException>(() => CadDrawingGraph.Build(document, null, 0.001, -3));
            Assert.Throws<ArgumentException>(() => CadDrawingGraph.Build(CadFlatten.Walk(document, null), 0.001, 0));
        }

        [Fact]
        public void ADenseDrawing_StopsAtTheCapWithoutFindingEveryCrossing()
        {
            // 600 horizontal lines crossed by 600 vertical ones: 360 000 crossings, of which 50 are asked for.
            CadDocument document = Blank();
            DxfBlockTableRecord model = document.Database.ModelSpace;

            for (int i = 0; i < 600; i++)
            {
                model.AppendEntity(new DxfLine(new GeoPoint3(0, i, 0), new GeoPoint3(600, i, 0)));
            }

            for (int i = 0; i < 600; i++)
            {
                model.AppendEntity(new DxfLine(new GeoPoint3(i + 0.5, 0, 0), new GeoPoint3(i + 0.5, 600, 0)));
            }

            CadDrawingGraph capped = CadDrawingGraph.Build(document, null, 0.001, 50);

            Assert.True(capped.Truncated);
            Assert.Equal(50, capped.Edges.Count);
            Assert.True(capped.EdgesFound < 360000);
            Assert.True(capped.Edges.All(e => e.Kind == CadGraphEdgeKind.Touches));
            Assert.Equal(0, capped.Edges[0].Source);
        }
    }
}
