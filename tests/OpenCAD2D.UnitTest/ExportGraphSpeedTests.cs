using System.Diagnostics;
using System.Linq;
using GeometryHelper;
using GeometryHelper.Dxf;
using GeometryHelper.Geometry;
using Xunit;
using Xunit.Abstractions;
using static OpenCAD2D.UnitTest.EditingDrawings;

namespace OpenCAD2D.UnitTest
{
    /// <summary>
    /// The graph of a long chain of lines, each touching the next: the keys of its edges (the node numbers next to each other)
    /// and of its grid cells (the cells next to each other) must spread over the buckets of the tables, or the build slows
    /// with the square of the number of lines (10 000 lines 0.2 s, 160 000 lines 23 s with the keys hashed by their two
    /// halves xor-ed).
    /// </summary>
    [Collection(ViewerSpeedCollection.Name)]
    public class ExportGraphSpeedTests
    {
        private readonly ITestOutputHelper _output;

        public ExportGraphSpeedTests(ITestOutputHelper output)
        {
            _output = output;
        }

        /// <summary>A zig-zag of lines, each starting where the one before ended.</summary>
        private static CadDocument Chain(int lines)
        {
            CadDocument document = Blank();
            document.Database.Insunits = DxfUnits.Millimeters;
            DxfBlockTableRecord model = document.Database.ModelSpace;

            for (int i = 0; i < lines; i++)
            {
                model.AppendEntity(
                    new DxfLine(
                        new GeoPoint3(3.0 * i, 4.0 * (i % 2), 0),
                        new GeoPoint3(3.0 * (i + 1), 4.0 * ((i + 1) % 2), 0)
                    )
                );
            }

            return document;
        }

        [Fact]
        public void AChainOfLines_HasOneTouchBetweenEachTwoNeighbours_AndNoOther()
        {
            CadDrawingGraph graph = CadDrawingGraph.Build(Chain(3000), null, 0.001);

            Assert.Equal(2999, graph.Edges.Count);
            Assert.All(graph.Edges, e => Assert.Equal(CadGraphEdgeKind.Touches, e.Kind));
            Assert.Equal(
                Enumerable.Range(0, 2999).Select(i => (i, i + 1)).ToList(),
                graph.Edges.Select(e => (e.Source, e.Target)).ToList()
            );
        }

        [Fact]
        [Trait("Category", "Timing")]
        public void AChainOfAHundredThousandLines_IsBuiltInSeconds_NotInMinutes()
        {
            CadDocument document = Chain(100000);
            var watch = Stopwatch.StartNew();
            CadDrawingGraph graph = CadDrawingGraph.Build(document, null, 0.001);
            watch.Stop();
            _output.WriteLine("graph of 100 000 chained lines: " + watch.ElapsedMilliseconds + " ms");

            Assert.Equal(99999, graph.Edges.Count);
            Assert.True(watch.ElapsedMilliseconds < 5000, "build " + watch.ElapsedMilliseconds + " ms");
        }
    }
}
