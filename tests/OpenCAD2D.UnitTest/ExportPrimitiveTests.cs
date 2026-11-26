using System;
using System.Collections.Generic;
using System.Linq;
using GeometryHelper.Dxf;
using GeometryHelper.Geometry;
using Xunit;

namespace OpenCAD2D.UnitTest
{
    /// <summary>
    /// The distance between the sides and arcs a curve is made of (CadPrimitive), which the graph tells touching curves by:
    /// against the distance of two clouds of points laid along the same shapes, and on the cases a drawing is full of (a line
    /// along a side, a line through an arc, two circles that cross, an arc inside another).
    /// </summary>
    public class ExportPrimitiveTests
    {
        private const int Samples = 4000;

        private static GeoPoint2 At(CadPrimitive p, double t)
        {
            if (!p.IsArc)
            {
                return new GeoPoint2(p.X0 + t * (p.X1 - p.X0), p.Y0 + t * (p.Y1 - p.Y0));
            }

            double angle = p.Start + p.Sweep * t;
            return new GeoPoint2(p.Cx + p.R * Math.Cos(angle), p.Cy + p.R * Math.Sin(angle));
        }

        private static double Length(CadPrimitive p) =>
            p.IsArc ? p.R * p.Sweep : Math.Sqrt((p.X1 - p.X0) * (p.X1 - p.X0) + (p.Y1 - p.Y0) * (p.Y1 - p.Y0));

        private static double Cloud(CadPrimitive a, CadPrimitive b)
        {
            const int Stride = 8;
            GeoPoint2[] first = Enumerable.Range(0, Samples + 1).Select(i => At(a, (double)i / Samples)).ToArray();
            GeoPoint2[] second = Enumerable.Range(0, Samples + 1).Select(i => At(b, (double)i / Samples)).ToArray();
            double least = double.MaxValue;

            // Every 8th point of each, to find where the two come near; then every point near those places.
            for (int i = 0; i < first.Length; i += Stride)
            {
                for (int j = 0; j < second.Length; j += Stride)
                {
                    least = Math.Min(least, (first[i] - second[j]).Length);
                }
            }

            double reach = least + 4.0 * Stride * Math.Max(Length(a), Length(b)) / Samples;
            double best = double.MaxValue;

            for (int i = 0; i < first.Length; i += Stride)
            {
                for (int j = 0; j < second.Length; j += Stride)
                {
                    if ((first[i] - second[j]).Length > reach)
                    {
                        continue;
                    }

                    for (int k = Math.Max(0, i - Stride); k <= Math.Min(Samples, i + Stride); k++)
                    {
                        for (int m = Math.Max(0, j - Stride); m <= Math.Min(Samples, j + Stride); m++)
                        {
                            best = Math.Min(best, (first[k] - second[m]).Length);
                        }
                    }
                }
            }

            return best;
        }

        private static CadPrimitive Shape(Random random)
        {
            double x = random.Next(-20, 20) + random.NextDouble();
            double y = random.Next(-20, 20) + random.NextDouble();

            if (random.Next(3) == 0)
            {
                return CadPrimitive.Segment(
                    x,
                    y,
                    x + random.Next(-15, 15) + random.NextDouble(),
                    y + random.Next(-15, 15) + random.NextDouble()
                );
            }

            return CadPrimitive.Arc(
                x,
                y,
                1 + random.Next(10) + random.NextDouble(),
                random.NextDouble() * 6.0,
                0.3 + random.NextDouble() * 5.5
            );
        }

        [Fact]
        public void TheDistanceOfTwoSidesAndArcs_IsTheDistanceOfTheirPoints_WithinTheSpacingOfThePoints()
        {
            var random = new Random(20261126);
            int touching = 0;

            for (int n = 0; n < 600; n++)
            {
                CadPrimitive a = Shape(random);
                CadPrimitive b = Shape(random);

                double exact = CadPrimitive.Distance(a, b, out double x, out double y);
                double cloud = Cloud(a, b);
                double spacing = Math.Max(Length(a), Length(b)) / Samples;

                // No point of one is nearer the other than the nearest pair, and the points leave it by no more than their spacing.
                Assert.True(exact <= cloud + 1E-9, "exact " + exact + " cloud " + cloud);
                Assert.True(
                    cloud <= exact + spacing + 1E-9,
                    "exact " + exact + " cloud " + cloud + " spacing " + spacing
                );
                touching += exact <= 0.001 ? 1 : 0;
            }

            Assert.True(touching > 10, "the cases include meeting ones: " + touching);
        }

        [Fact]
        public void TheCasesADrawingIsFullOf_AreMeasuredAsTheyAre()
        {
            double x;
            double y;
            CadPrimitive line = CadPrimitive.Segment(0, 0, 10, 0);

            // A line along the side of another, and one that stops 0.3 short of it.
            Assert.Equal(0.0, CadPrimitive.Distance(line, CadPrimitive.Segment(5, 0, 15, 0), out x, out y), 12);
            Assert.Equal(0.3, CadPrimitive.Distance(line, CadPrimitive.Segment(10.3, 0, 15, 0), out x, out y), 12);
            Assert.Equal(5.0, CadPrimitive.Distance(line, CadPrimitive.Segment(5, 5, 5, 9), out x, out y), 12);

            // A line through a circle, one that misses it by 2, and a point at its centre, 3 from it.
            CadPrimitive circle = CadPrimitive.Arc(5, 5, 3, 0, 2 * Math.PI);
            Assert.Equal(0.0, CadPrimitive.Distance(CadPrimitive.Segment(0, 5, 10, 5), circle, out x, out y), 12);
            Assert.Equal(2.0, CadPrimitive.Distance(CadPrimitive.Segment(0, 10, 10, 10), circle, out x, out y), 12);
            Assert.Equal(3.0, CadPrimitive.Distance(CadPrimitive.Segment(5, 5, 5, 5), circle, out x, out y), 12);

            // Two circles that cross, two that stand apart by 1, one inside the other by 1, and two about one centre.
            Assert.Equal(
                0.0,
                CadPrimitive.Distance(circle, CadPrimitive.Arc(9, 5, 3, 0, 2 * Math.PI), out x, out y),
                12
            );
            Assert.Equal(
                1.0,
                CadPrimitive.Distance(circle, CadPrimitive.Arc(12, 5, 3, 0, 2 * Math.PI), out x, out y),
                12
            );
            Assert.Equal(
                1.0,
                CadPrimitive.Distance(circle, CadPrimitive.Arc(5, 5, 2, 0, 2 * Math.PI), out x, out y),
                12
            );
            Assert.Equal(
                1.0,
                CadPrimitive.Distance(circle, CadPrimitive.Arc(5, 5, 4, 0, 2 * Math.PI), out x, out y),
                12
            );

            // An arc of a quarter turn from the right, and a line through where the arc is not.
            CadPrimitive quarter = CadPrimitive.Arc(0, 0, 10, 0, Math.PI / 2);
            Assert.Equal(0.0, CadPrimitive.Distance(quarter, CadPrimitive.Segment(5, 5, 15, 5), out x, out y), 12);
            Assert.True(CadPrimitive.Distance(quarter, CadPrimitive.Segment(-15, 5, -5, 5), out x, out y) > 5.0);

            // A bulge of 1 along a chord to the right is the half circle below it, counter-clockwise; -1 the one above.
            CadPrimitive half = CadPrimitive.Bulged(0, 0, 10, 0, 1.0);
            Assert.True(half.IsArc);
            Assert.Equal(5.0, half.R, 12);
            Assert.Equal(5.0, half.Cx, 12);
            Assert.Equal(0.0, CadPrimitive.Distance(half, CadPrimitive.Segment(5, -9, 5, -3), out x, out y), 9);
            Assert.Equal(-5.0, half.MinY, 9);
            Assert.Equal(0.0, half.MaxY, 9);
            Assert.Equal(5.0, CadPrimitive.Bulged(0, 0, 10, 0, -1.0).MaxY, 9);
        }

        [Fact]
        public void TheSidesAndArcsOfACurve_AreThoseItIsDrawnAs()
        {
            var polyline = new DxfPolyline();
            polyline.AddVertexAt(0, new GeoPoint2(0, 0), 0.0, 0.0, 0.0);
            polyline.AddVertexAt(1, new GeoPoint2(10, 0), 1.0, 0.0, 0.0);
            polyline.AddVertexAt(2, new GeoPoint2(10, 10), 0.0, 0.0, 0.0);
            polyline.Closed = true;

            List<CadPrimitive> sides = CadPrimitive.Of(polyline);

            Assert.Equal(3, sides.Count);
            Assert.False(sides[0].IsArc);
            Assert.True(sides[1].IsArc);
            Assert.False(sides[2].IsArc);
            Assert.Equal(10.0, sides[2].X0, 9);
            Assert.Equal(0.0, sides[2].X1, 9);
            Assert.Single(CadPrimitive.Of(new DxfCircle(new GeoPoint3(0, 0, 0), new GeoVector3(0, 0, 1), 4.0)));
            Assert.Null(
                CadPrimitive.Of(
                    new DxfEllipse(
                        new GeoPoint3(0, 0, 0),
                        new GeoVector3(0, 0, 1),
                        new GeoVector3(5, 0, 0),
                        0.5,
                        0.0,
                        6.2831853071795862
                    )
                )
            );

            // An arc whose normal points down runs the other way: its start and end have changed places seen from above.
            var down = new DxfArc(new GeoPoint3(0, 0, 0), new GeoVector3(0, 0, -1), 5.0, 0.0, Math.PI / 2);
            CadPrimitive arc = Assert.Single(CadPrimitive.Of(down));
            Assert.Equal(Math.PI / 2, arc.Start, 9);
            Assert.Equal(Math.PI / 2, arc.Sweep, 9);
        }
    }
}
