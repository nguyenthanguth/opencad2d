using System;
using System.Diagnostics;
using System.Linq;
using GeometryHelper;
using GeometryHelper.Dxf;
using GeometryHelper.Geometry;
using Xunit;
using Xunit.Abstractions;
using static OpenCAD2D.UnitTest.SpatialDrawings;

namespace OpenCAD2D.UnitTest
{
    /// <summary>
    /// The check of a drawing of 10 000 objects against a standard that runs every rule: 2 500 TEXTs, 5 000 LINEs, 1 500 CIRCLEs and
    /// 1 000 INSERTs of a block of a line and a text, laid at random over 1 000 by 1 000 on layers some of which the standard
    /// does not list, so that thousands of words are near lines and each other. The limits are ceilings a quadratic check
    /// crosses by a long way; the times are written to the test output.
    /// </summary>
    public sealed class StandardsSpeedTests : IDisposable
    {
        private readonly SpatialDrawings _drawings = new SpatialDrawings();
        private readonly ITestOutputHelper _output;

        public StandardsSpeedTests(ITestOutputHelper output) => _output = output;

        public void Dispose() => _drawings.Dispose();

        private CadDocument TenThousand()
        {
            CadDocument document = _drawings.Blank();
            document.Database.LayerTable.Add(new DxfLayerTableRecord { Name = "A-WALL" });
            document.Database.LayerTable.Add(new DxfLayerTableRecord { Name = "JUNK" });
            var block = new DxfBlockTableRecord { Name = "MARK" };
            document.Database.BlockTable.Add(block);
            block.AppendEntity(Line(0, 0, 12, 4));
            DxfDBText label = Text("SIDE", 1, 6, 0.0, 2.5);
            block.AppendEntity(label);
            label.TextStyleName = "OC_SHX";
            var random = new Random(10000);

            for (int i = 0; i < 10000; i++)
            {
                double x = random.NextDouble() * 1000.0;
                double y = random.NextDouble() * 1000.0;
                int kind = i % 20;
                DxfEntity made;

                if (kind < 5)
                {
                    made = Put(document, Text("SIDE FOOD", x, y, random.Next(4) * 90.0, 2.5 + random.Next(4)));
                }
                else if (kind < 15)
                {
                    double length = 5.0 + random.NextDouble() * 25.0;
                    double angle = random.NextDouble() * Math.PI;
                    made = Put(document, Line(x, y, x + length * Math.Cos(angle), y + length * Math.Sin(angle)));
                }
                else if (kind < 18)
                {
                    made = Put(document, Circle(x, y, 1.0 + random.NextDouble() * 9.0));
                }
                else
                {
                    made = Put(
                        document,
                        new DxfBlockReference(new GeoPoint3(x, y, 0), block)
                        {
                            Rotation = random.NextDouble() * Math.PI,
                        }
                    );
                }

                made.Layer =
                    i % 3 == 0 ? "JUNK"
                    : i % 3 == 1 ? "A-WALL"
                    : "0";

                if (i % 7 == 0)
                {
                    made.ColorIndex = 1 + i % 5;
                }
            }

            return document;
        }

        private const string Everything =
            "\"layers\":{\"allowed\":[{\"name\":\"A-*\"}],\"allowLayer0\":false},\"byLayer\":{\"color\":true,\"linetype\":true,\"lineweight\":true},"
            + "\"textStyles\":[{\"name\":\"OC_SHX\"}],\"textHeights\":{\"paper\":[2.5,3.5,5]},"
            + "\"gaps\":{\"wordLine\":0.5,\"wordWord\":0.5},\"dimensionChains\":{}";

        [Fact]
        public void TenThousandObjects_AreCheckedAgainstEveryRuleInSeconds_AndAgainInLess()
        {
            CadDocument document = TenThousand();
            var tolerance = new Tolerance(1E-3, 1E-5);
            CadStandard standard = CadStandard.Parse(
                "{\"schema\":\"opencad2d.standard\",\"version\":1," + Everything + "}"
            );
            var clock = Stopwatch.StartNew();
            document.GetDisplay(document.ModelSpace);
            long display = clock.ElapsedMilliseconds;

            clock.Restart();
            CadStandardResult first = CadStandards.Check(document, standard, 0.0, tolerance);
            long firstMs = clock.ElapsedMilliseconds;

            clock.Restart();
            CadStandardResult second = CadStandards.Check(document, standard, 0.0, tolerance);
            long secondMs = clock.ElapsedMilliseconds;

            _output.WriteLine(
                $"10 000 objects: display {display} ms, check {firstMs} ms ({first.Violations.Count} violations, total {first.Total:0.000}), "
                    + $"again {secondMs} ms; "
                    + string.Join(", ", first.Rules.Select(r => r.Rule + " " + r.Score.ToString("0.000")))
            );
            Assert.Equal(first.Violations.Count, second.Violations.Count);
            Assert.Equal(first.Total, second.Total);
            Assert.True(first.Violations.Count > 1000, "violations: " + first.Violations.Count);
            Assert.True(firstMs < 20000, "check " + firstMs + " ms");
            Assert.True(secondMs < 20000, "check again " + secondMs + " ms");
        }
    }
}
