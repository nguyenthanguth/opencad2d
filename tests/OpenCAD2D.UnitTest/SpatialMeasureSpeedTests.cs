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
    /// What a layout program asks again and again, timed on a drawing of 100 000 objects (the one of
    /// <see cref="SpatialSpeedTests"/>: 25 000 TEXTs, 50 000 LINEs, 15 000 CIRCLEs and 10 000 INSERTs laid at random over 3 000
    /// by 3 000): the empty rectangles of a frame, whether a candidate fits, and the measure of a text and a dimension. The
    /// limits are ceilings a quadratic test crosses by a long way; the times are written to the test output.
    /// </summary>
    public sealed class SpatialMeasureSpeedTests : IDisposable
    {
        private static readonly Tolerance Touching = new Tolerance(1E-3, 1E-5);
        private readonly SpatialDrawings _drawings = new SpatialDrawings();
        private readonly ITestOutputHelper _output;

        public SpatialMeasureSpeedTests(ITestOutputHelper output) => _output = output;

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
        public void FreeSpaceAndFitsAndMeasure_OnAHundredThousandObjects_AreFastEnoughToAskInALoop()
        {
            CadDocument document = Hundred();
            document.GetDisplay(document.ModelSpace);
            CadSpatial.Bounds(document, document.ModelSpace);
            var clock = Stopwatch.StartNew();

            // The index is made by the first question, as Bounds and Overlaps make it: its cost is not this question's.
            var sheet = new DisplayBox(1000, 1000, 1300, 1300);
            CadFreeSpace first = CadSpatial.FreeSpace(document, document.ModelSpace, sheet, null, Touching);
            long sheetMs = clock.ElapsedMilliseconds;

            clock.Restart();
            CadFreeSpace sized = CadSpatial.FreeSpace(
                document,
                document.ModelSpace,
                new DisplayBox(0, 0, 3000, 3000),
                new CadFreeSpaceOptions { MinWidth = 20.0, MinHeight = 20.0 },
                Touching
            );
            long wholeMs = clock.ElapsedMilliseconds;

            clock.Restart();
            CadFreeSpace again = CadSpatial.FreeSpace(
                document,
                document.ModelSpace,
                sheet,
                new CadFreeSpaceOptions
                {
                    Margin = 1.0,
                    MinWidth = 10.0,
                    MinHeight = 5.0,
                },
                Touching
            );
            long sizedMs = clock.ElapsedMilliseconds;

            var random = new Random(9);
            int fitting = 0;
            clock.Restart();

            for (int i = 0; i < 1000; i++)
            {
                double x = random.NextDouble() * 2900.0;
                double y = random.NextDouble() * 2900.0;
                var candidate = new CadFrame(x, y, 20.0, 5.0, random.NextDouble() * Math.PI);

                if (CadSpatial.Fits(document, document.ModelSpace, candidate, null, Touching))
                {
                    fitting++;
                }
            }

            double fitsEach = clock.Elapsed.TotalMilliseconds / 1000.0;

            clock.Restart();

            for (int i = 0; i < 500; i++)
            {
                CadMeasure.Of(document, Text("SIDE FOOD", 5.0 * i, 10, 0.0, 5.0));
            }

            double textEach = clock.Elapsed.TotalMilliseconds / 500.0;

            clock.Restart();

            for (int i = 0; i < 100; i++)
            {
                CadMeasure.Of(
                    document,
                    new DxfRotatedDimension(
                        0.0,
                        new GeoPoint3(0, 0, 0),
                        new GeoPoint3(100 + i, 0, 0),
                        new GeoPoint3(100, 20, 0),
                        string.Empty,
                        null
                    )
                    {
                        DimensionStyleName = "Standard",
                    }
                );
            }

            double dimensionEach = clock.Elapsed.TotalMilliseconds / 100.0;

            _output.WriteLine(
                $"100 000 objects: free_space of a 300 by 300 frame {sheetMs} ms ({first.Count} rectangles), "
                    + $"of the 3000 by 3000 drawing at least 20 by 20 {wholeMs} ms ({sized.Count}), "
                    + $"300 frame with margin 1 and at least 10 by 5 {sizedMs} ms ({again.Count}); "
                    + $"fits {fitsEach:0.000} ms each ({fitting} of 1000 fit); "
                    + $"measure text {textEach:0.000} ms, dimension {dimensionEach:0.000} ms"
            );
            Assert.True(first.Count > 0);
            Assert.True(sheetMs < 10000, "free space of a frame " + sheetMs + " ms");
            Assert.True(wholeMs < 60000, "free space of the drawing " + wholeMs + " ms");
            Assert.True(fitsEach < 50.0, "fits " + fitsEach + " ms");
            Assert.True(textEach < 50.0, "measure text " + textEach + " ms");
            Assert.True(dimensionEach < 200.0, "measure dimension " + dimensionEach + " ms");
        }
    }
}
