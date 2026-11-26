using System;
using System.Collections.Generic;
using System.Diagnostics;
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
    /// The drawing as a graph (CadDrawingGraph): a small drawing built in code with the edges it is expected to have, the
    /// tolerance of a touch, the order of the edges, and the time of a drawing of ten thousand entities.
    /// </summary>
    public class ExportGraphTests
    {
        private static readonly Tolerance Mm = new Tolerance(1E-6, 1E-6);

        /// <summary>
        /// A drawing of millimetres: the line L0 from (0, 0) to (100, 0) with the dimension DIMLINEAR makes of it, tied to it; the
        /// plate, a closed polyline from (0, 0) to (100, 50); the circle C of radius 10 about (50, 25), inside the plate; the text
        /// NOTE at (20, 20), inside it; the line L1 from (100, 50) to (150, 50), which starts at a corner of the plate; the line L2
        /// from (125, 0) to (125, 100), crossing L1; the leader from (60, 25), on the circle, to (90, 40); and the line FAR from
        /// (500, 500) to (600, 500), alone.
        /// </summary>
        private static CadDocument Sample(out Dictionary<string, string> handles)
        {
            CadDocument document = Blank();
            document.Database.Insunits = DxfUnits.Millimeters;
            CadCommandSession session = Session(document);
            Run(session, "LINE", P(0, 0), P(100, 0), Enter);
            string l0 = All<DxfLine>(document.ModelSpace).Single().Handle;
            Run(session, "DIMLINEAR", Enter, new[] { l0 }, P(50, -30));
            DxfBlockTableRecord space = document.Database.ModelSpace;
            var plate = new DxfPolyline();
            plate.AddVertexAt(0, new GeoPoint2(0, 0), 0.0, 0.0, 0.0);
            plate.AddVertexAt(1, new GeoPoint2(100, 0), 0.0, 0.0, 0.0);
            plate.AddVertexAt(2, new GeoPoint2(100, 50), 0.0, 0.0, 0.0);
            plate.AddVertexAt(3, new GeoPoint2(0, 50), 0.0, 0.0, 0.0);
            plate.Closed = true;
            space.AppendEntity(plate);
            var circle = new DxfCircle(new GeoPoint3(50, 25, 0), new GeoVector3(0, 0, 1), 10.0);
            space.AppendEntity(circle);
            var note = new DxfDBText
            {
                Position = new GeoPoint3(20, 20, 0),
                TextString = "NOTE",
                Height = 2.5,
            };
            space.AppendEntity(note);
            var l1 = new DxfLine(new GeoPoint3(100, 50, 0), new GeoPoint3(150, 50, 0));
            space.AppendEntity(l1);
            var l2 = new DxfLine(new GeoPoint3(125, 0, 0), new GeoPoint3(125, 100, 0));
            space.AppendEntity(l2);
            var leader = new DxfLeader { HasArrowHead = true };
            leader.AppendVertex(new GeoPoint3(60, 25, 0));
            leader.AppendVertex(new GeoPoint3(80, 40, 0));
            leader.AppendVertex(new GeoPoint3(90, 40, 0));
            space.AppendEntity(leader);
            var far = new DxfLine(new GeoPoint3(500, 500, 0), new GeoPoint3(600, 500, 0));
            space.AppendEntity(far);
            handles = new Dictionary<string, string>
            {
                { "L0", l0 },
                { "DIM", All<DxfRotatedDimension>(document.ModelSpace).Single().Handle },
                { "PLATE", plate.Handle },
                { "C", circle.Handle },
                { "NOTE", note.Handle },
                { "L1", l1.Handle },
                { "L2", l2.Handle },
                { "LEADER", leader.Handle },
                { "FAR", far.Handle },
            };
            return document;
        }

        private static int Node(CadDrawingGraph graph, string handle) =>
            Enumerable.Range(0, graph.NodeCount).Single(i => graph.Flat.Entities[i].Handle == handle);

        private static bool Has(CadDrawingGraph graph, CadGraphEdgeKind kind, int a, int b) =>
            graph.Edges.Any(e => e.Kind == kind && e.Source == a && e.Target == b);

        private static bool HasEither(CadDrawingGraph graph, CadGraphEdgeKind kind, int a, int b) =>
            Has(graph, kind, a, b) || Has(graph, kind, b, a);

        [Fact]
        public void TheGraphOfASampleDrawing_HasTheEdgesItIsExpectedToHave_AndNoOthers()
        {
            CadDocument document = Sample(out Dictionary<string, string> h);

            CadDrawingGraph graph = CadDrawingGraph.Build(document, null, 0.001);

            int l0 = Node(graph, h["L0"]);
            int dim = Node(graph, h["DIM"]);
            int plate = Node(graph, h["PLATE"]);
            int c = Node(graph, h["C"]);
            int note = Node(graph, h["NOTE"]);
            int l1 = Node(graph, h["L1"]);
            int l2 = Node(graph, h["L2"]);
            int leader = Node(graph, h["LEADER"]);
            int far = Node(graph, h["FAR"]);
            Assert.Equal(9, graph.NodeCount);

            var touches = graph
                .Edges.Where(e => e.Kind == CadGraphEdgeKind.Touches)
                .Select(e => Math.Min(e.Source, e.Target) * 100 + Math.Max(e.Source, e.Target))
                .OrderBy(k => k)
                .ToList();
            var expected = new[] { (l0, plate), (l1, plate), (l1, l2) }
                .Select(p => Math.Min(p.Item1, p.Item2) * 100 + Math.Max(p.Item1, p.Item2))
                .OrderBy(k => k)
                .ToList();
            Assert.Equal(expected, touches);

            var contains = graph
                .Edges.Where(e => e.Kind == CadGraphEdgeKind.Contains)
                .Select(e => (e.Source, e.Target))
                .OrderBy(p => p.Source)
                .ThenBy(p => p.Target)
                .ToList();
            Assert.Equal(
                new[] { (plate, c), (plate, note), (plate, leader) }
                    .OrderBy(p => p.Item1)
                    .ThenBy(p => p.Item2)
                    .ToList(),
                contains.Select(p => (p.Source, p.Target)).Where(p => p.Source == plate).ToList()
            );
            // L0 lies along the plate's lower side, which touches it and is not inside it.
            Assert.DoesNotContain(contains, p =>
                p.Target == far || p.Source == far || p.Target == l2 || p.Target == l0
            );

            Assert.Equal(
                new[] { (dim, l0) },
                graph
                    .Edges.Where(e => e.Kind == CadGraphEdgeKind.Dimensions)
                    .Select(e => (e.Source, e.Target))
                    .ToArray()
            );
            Assert.Equal(
                new[] { (leader, c) },
                graph.Edges.Where(e => e.Kind == CadGraphEdgeKind.Annotates).Select(e => (e.Source, e.Target)).ToArray()
            );
            Assert.DoesNotContain(graph.Edges, e => e.Source == far || e.Target == far);

            // Where two lines cross, the edge says where.
            CadGraphEdge crossing = graph.Edges.Single(e =>
                e.Kind == CadGraphEdgeKind.Touches && (e.Source == l2 || e.Target == l2)
            );
            Assert.Equal(125.0, crossing.X, 6);
            Assert.Equal(50.0, crossing.Y, 6);
        }

        [Fact]
        public void TheEdges_AreSortedByKindSourceAndTarget_AndEachIsOnceOnly()
        {
            CadDocument document = Sample(out Dictionary<string, string> _);

            CadDrawingGraph graph = CadDrawingGraph.Build(document, null, 0.001);

            var keys = graph.Edges.Select(e => ((int)e.Kind, e.Source, e.Target)).ToList();
            Assert.Equal(keys.OrderBy(k => k.Item1).ThenBy(k => k.Source).ThenBy(k => k.Target), keys);
            Assert.Equal(keys.Count, keys.Distinct().Count());
            Assert.Equal(CadDrawingGraph.Build(document, null, 0.001).ToJson(), graph.ToJson());
        }

        [Fact]
        public void TwoLinesNearEachOther_TouchWhenTheGapIsWithinTheTolerance_AndOnlyThen()
        {
            CadDocument document = Blank();
            document.Database.Insunits = DxfUnits.Millimeters;
            document.Database.ModelSpace.AppendEntity(new DxfLine(new GeoPoint3(0, 0, 0), new GeoPoint3(10, 0, 0)));
            document.Database.ModelSpace.AppendEntity(new DxfLine(new GeoPoint3(10.4, 0, 0), new GeoPoint3(20, 0, 0)));
            document.Database.ModelSpace.AppendEntity(new DxfLine(new GeoPoint3(10, 0.4, 0), new GeoPoint3(10, 9, 0)));

            CadDrawingGraph tight = CadDrawingGraph.Build(document, null, 0.1);
            CadDrawingGraph loose = CadDrawingGraph.Build(document, null, 0.5);

            CadDrawingGraph looser = CadDrawingGraph.Build(document, null, 0.6);

            // The first line is 0.4 from each of the others, which are 0.566 from each other.
            Assert.Empty(tight.Edges);
            Assert.Equal(2, loose.Edges.Count);
            Assert.Equal(3, looser.Edges.Count);
            Assert.All(looser.Edges, e => Assert.Equal(CadGraphEdgeKind.Touches, e.Kind));
        }

        [Fact]
        public void TheToleranceAndTheDrawing_AreChecked()
        {
            Assert.Throws<ArgumentException>(() => CadDrawingGraph.Build(Blank(), null, 0.0));
            Assert.Throws<ArgumentException>(() => CadDrawingGraph.Build(Blank(), null, double.NaN));
            Assert.Throws<ArgumentNullException>(() => CadDrawingGraph.Build((CadFlatDrawing)null, 0.001));
            Assert.Throws<ArgumentNullException>(() => CadDrawingGraph.Build((CadDocument)null, null, 0.001));
        }

        [Fact]
        public void TheGraphAsJson_FitsItsShape_AndTheEdgeArrayIsThreeNumbersAnEdge()
        {
            CadDocument document = Sample(out Dictionary<string, string> _);
            CadDrawingGraph graph = CadDrawingGraph.Build(document, null, 0.001);

            Dictionary<string, object> json = Obj(Parse(graph.ToJson()));

            Assert.Equal("opencad2d.graph", json["schema"]);
            Assert.Equal(1.0, (double)json["version"]);
            Assert.Equal(graph.NodeCount, Arr(json["nodes"]).Count);
            Assert.Equal(graph.Edges.Count, Arr(json["edges"]).Count);
            Dictionary<string, object> first = Obj(Arr(json["nodes"])[0]);
            foreach (
                string name in new[]
                {
                    "id",
                    "handle",
                    "kind",
                    "layer",
                    "color",
                    "linetype",
                    "layout",
                    "box",
                    "path",
                    "text",
                }
            )
            {
                Assert.True(first.ContainsKey(name), name);
            }

            Assert.Contains(Arr(json["edges"]).Select(Obj), e => (string)e["kind"] == "contains");
            Assert.Contains(Arr(json["edges"]).Select(Obj), e => (string)e["kind"] == "annotates");
            double[] array = graph.EdgeArray();
            Assert.Equal(graph.Edges.Count * 3, array.Length);
            Assert.Equal(graph.Edges[0].Source, array[0]);
            Assert.Equal(graph.Edges[0].Target, array[1]);
            Assert.Equal((int)graph.Edges[0].Kind, array[2]);
            Assert.Equal("NOTE", Arr(json["nodes"]).Select(Obj).Single(n => (string)n["kind"] == "TEXT")["text"]);
        }

        [Fact]
        public void ABlockReferenceIsWalked_BeforeItsPartsAreRelated()
        {
            CadDocument document = Blank();
            document.Database.Insunits = DxfUnits.Millimeters;
            var block = new DxfBlockTableRecord { Name = "TEE", Origin = new GeoPoint3(0, 0, 0) };
            document.Database.BlockTable.Add(block);
            block.AppendEntity(new DxfLine(new GeoPoint3(0, 0, 0), new GeoPoint3(10, 0, 0)));
            block.AppendEntity(new DxfLine(new GeoPoint3(10, 0, 0), new GeoPoint3(10, 10, 0)));
            document.Database.ModelSpace.AppendEntity(
                new DxfBlockReference(new GeoPoint3(100, 100, 0), block) { Rotation = Math.PI / 2 }
            );

            CadDrawingGraph graph = CadDrawingGraph.Build(document, null, 0.001);

            Assert.Equal(2, graph.NodeCount);
            CadGraphEdge edge = Assert.Single(graph.Edges);
            Assert.Equal(CadGraphEdgeKind.Touches, edge.Kind);
            Assert.Equal(100.0, edge.X, 6);
            Assert.Equal(110.0, edge.Y, 6);
        }

        [Fact]
        public void ADrawingOfTenThousandEntities_GivesItsGraphAndItsJson_InSeconds()
        {
            CadDocument document = CadDocument.From(DxfDatabase.Parse(BigDrawing.Text(10000)), "big.dxf");
            CadFlatDrawing flat = CadFlatten.Walk(document, null);

            var clock = Stopwatch.StartNew();
            CadDrawingGraph graph = CadDrawingGraph.Build(flat, 0.001);
            long graphMilliseconds = clock.ElapsedMilliseconds;
            clock.Restart();
            string json = CadDrawingJson.Write(flat, document);
            long jsonMilliseconds = clock.ElapsedMilliseconds;
            clock.Restart();
            string graphJson = graph.ToJson();
            long graphJsonMilliseconds = clock.ElapsedMilliseconds;
            clock.Restart();
            CadFlatten.Walk(document, null);
            long walkMilliseconds = clock.ElapsedMilliseconds;

            Console.WriteLine(
                "10 000 entities ("
                    + flat.Entities.Count
                    + " flat, "
                    + graph.Edges.Count
                    + " edges): walk "
                    + walkMilliseconds
                    + " ms, graph "
                    + graphMilliseconds
                    + " ms, json "
                    + jsonMilliseconds
                    + " ms ("
                    + json.Length / 1024
                    + " KB), graph json "
                    + graphJsonMilliseconds
                    + " ms"
            );
            Assert.True(flat.Entities.Count >= 10000);
            Assert.True(graph.Edges.Count > 0);
            Assert.True(graphMilliseconds < 60000, "graph " + graphMilliseconds + " ms");
            Assert.True(jsonMilliseconds < 30000, "json " + jsonMilliseconds + " ms");
            Assert.True(graphJson.Length > 0);
        }
    }
}
