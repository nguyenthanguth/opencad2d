using System;
using System.Diagnostics;
using GeometryHelper;
using GeometryHelper.Dxf;
using GeometryHelper.Geometry;
using Xunit;
using Xunit.Abstractions;
using static OpenCAD2D.UnitTest.SpatialDrawings;

namespace OpenCAD2D.UnitTest
{
    /// <summary>
    /// The overlap test of a drawing of 100 000 objects, which a reward function asks again after each change it makes: 25 000
    /// TEXTs, 50 000 LINEs, 15 000 CIRCLEs and 10 000 INSERTs of a block of a line, an arc and a text, laid at random over
    /// 3 000 by 3 000, so that some thousands of pairs overlap. The limits are ceilings a quadratic test or an index that is not
    /// reused crosses by a long way; the times are written to the test output.
    /// </summary>
    public sealed class SpatialSpeedTests : IDisposable
    {
        private readonly SpatialDrawings _drawings = new SpatialDrawings();
        private readonly ITestOutputHelper _output;

        public SpatialSpeedTests(ITestOutputHelper output) => _output = output;

        public void Dispose() => _drawings.Dispose();

        private CadDocument Hundred()
        {
            CadDocument document = _drawings.Blank();
            var block = new DxfBlockTableRecord { Name = "MARK" };
            document.Database.BlockTable.Add(block);
            block.AppendEntity(Line(0, 0, 12, 4));
            block.AppendEntity(new DxfArc(new GeoPoint3(6, 0, 0), GeoVector3.ZAxis, 4.0, 0.0, Math.PI));
            DxfDBText label = Text("SIDE", 1, 6, 0.0, 2.5);
            block.AppendEntity(label);
            label.TextStyleName = "OC_SHX";
            var random = new Random(100000);

            for (int i = 0; i < 100000; i++)
            {
                double x = random.NextDouble() * 3000.0;
                double y = random.NextDouble() * 3000.0;
                int kind = i % 20;

                if (kind < 5)
                {
                    Put(document, Text("SIDE FOOD", x, y, random.Next(4) * 90.0, 2.5 + random.NextDouble() * 2.5));
                }
                else if (kind < 15)
                {
                    double length = 5.0 + random.NextDouble() * 25.0;
                    double angle = random.NextDouble() * Math.PI;
                    Put(document, Line(x, y, x + length * Math.Cos(angle), y + length * Math.Sin(angle)));
                }
                else if (kind < 18)
                {
                    Put(document, Circle(x, y, 1.0 + random.NextDouble() * 9.0));
                }
                else
                {
                    Put(
                        document,
                        new DxfBlockReference(new GeoPoint3(x, y, 0), block)
                        {
                            Rotation = random.NextDouble() * Math.PI,
                        }
                    );
                }
            }

            return document;
        }

        [Fact]
        public void AHundredThousandObjects_HaveTheirOverlapsInSecondsAndAnEditedEntitysInAFractionOfOne()
        {
            CadDocument document = Hundred();
            var tolerance = new Tolerance(1E-3, 1E-5);
            var clock = Stopwatch.StartNew();
            document.GetDisplay(document.ModelSpace);
            long display = clock.ElapsedMilliseconds;

            clock.Restart();
            CadBoundsTable bounds = CadSpatial.Bounds(document, document.ModelSpace);
            long first = clock.ElapsedMilliseconds;

            clock.Restart();
            CadOverlapTable all = CadSpatial.Overlaps(document, document.ModelSpace, null, tolerance);
            long overlaps = clock.ElapsedMilliseconds;

            clock.Restart();
            CadOverlapTable again = CadSpatial.Overlaps(document, document.ModelSpace, null, tolerance);
            long second = clock.ElapsedMilliseconds;

            clock.Restart();
            CadOverlapTable one = CadSpatial.Overlaps(
                document,
                document.ModelSpace,
                new CadOverlapOptions { Handles = new[] { all.Words[0] } },
                tolerance
            );
            long single = clock.ElapsedMilliseconds;

            _output.WriteLine(
                $"100 000 objects: display {display} ms, bounds {first} ms ({bounds.Count} rows), "
                    + $"overlaps {overlaps} ms ({all.Count} rows), again {second} ms, one entity {single} ms ({one.Count} rows)"
            );
            Assert.Equal(all.Words.Length, again.Words.Length);
            Assert.True(bounds.Count > 99000, "rows: " + bounds.Count);
            Assert.True(all.Count > 1000, "pairs: " + all.Count);
            Assert.True(one.Count >= 1);
            Assert.True(first < 20000, "bounds " + first + " ms");
            Assert.True(overlaps < 30000, "overlaps " + overlaps + " ms");
            Assert.True(second < 20000, "overlaps again " + second + " ms");
            Assert.True(single < 1000, "overlaps of one entity " + single + " ms");
        }
    }
}
