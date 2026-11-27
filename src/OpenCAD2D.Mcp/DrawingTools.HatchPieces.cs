using System;
using System.Collections.Generic;
using System.Linq;
using GeometryHelper;
using GeometryHelper.Dxf;
using GeometryHelper.Geometry;

namespace OpenCAD2D.Mcp
{
    internal sealed partial class DrawingTools
    {
        /// <summary>
        /// One open object of the drawing that may be part of the outline of an <c>add_hatch</c>: a line, an arc, an open polyline
        /// (its vertices and the bulges of its sides) or an elliptic arc (the exact spline of it), in the world on the floor.
        /// </summary>
        private sealed class HatchPiece
        {
            private HatchPiece(string handle, int order, GeoPoint2[] points, double[] bulges, GeoSpline3 spline)
            {
                Handle = handle;
                Order = order;
                Points = points;
                Bulges = bulges;
                Spline = spline;
            }

            /// <summary>The handle the piece comes from.</summary>
            public string Handle { get; }

            /// <summary>Where the handle stood in the list given, which is the piece's place.</summary>
            public int Order { get; }

            /// <summary>The vertices, at least two; the first is where the piece starts.</summary>
            public GeoPoint2[] Points { get; }

            /// <summary>The bulge of each side, one fewer than the vertices.</summary>
            public double[] Bulges { get; }

            /// <summary>The spline of an elliptic arc; null for the other pieces.</summary>
            public GeoSpline3 Spline { get; }

            public GeoPoint2 Start => Points[0];

            public GeoPoint2 End => Points[Points.Length - 1];

            public static HatchPiece OfLine(string handle, int order, DxfLine line) =>
                new HatchPiece(
                    handle,
                    order,
                    new[] { Flat(line.StartPoint), Flat(line.EndPoint) },
                    new[] { 0.0 },
                    null
                );

            public static HatchPiece OfArc(string handle, int order, DxfArc arc, int facing)
            {
                if (!(arc.TotalAngle < 2.0 * Math.PI - 1E-9))
                {
                    throw new ArgumentException(
                        "The entity "
                            + handle
                            + " is an arc of a whole turn: a hatch takes it as a circle, which has no gap to close."
                    );
                }

                // An arc facing -Z turns the other way in the world than in its own system: it bulges the other way.
                return new HatchPiece(
                    handle,
                    order,
                    new[] { Flat(arc.StartPoint), Flat(arc.EndPoint) },
                    new[] { Math.Tan(arc.TotalAngle / 4.0) * facing },
                    null
                );
            }

            public static HatchPiece OfEllipse(string handle, int order, DxfEllipse part)
            {
                GeoSpline3 spline = part.ToGeoSpline3();
                return new HatchPiece(
                    handle,
                    order,
                    new[] { Flat(spline.StartPoint), Flat(spline.EndPoint) },
                    new[] { 0.0 },
                    spline
                );
            }

            public static HatchPiece OfPolyline(string handle, int order, DxfPolyline polyline, int facing)
            {
                int count = polyline.NumberOfVertices;
                return new HatchPiece(
                    handle,
                    order,
                    Enumerable.Range(0, count).Select(i => Flat(polyline.GetPoint3dAt(i))).ToArray(),
                    Enumerable.Range(0, count - 1).Select(i => polyline.GetBulgeAt(i) * facing).ToArray(),
                    null
                );
            }

            /// <summary>
            /// Joins pieces end to end into closed outlines, the lowest piece not yet used first and the lowest that meets the end of
            /// the chain next, each piece turned about where it has to be. Each outline is at the order of its first piece. Throws
            /// where a chain finds nothing to carry on with before it is back at its start.
            /// </summary>
            public static List<(int Order, (GeoPoint2[] Points, double[] Bulges, object[] Edges) Loop)> Join(
                List<HatchPiece> pieces,
                Tolerance tolerance
            )
            {
                var outlines = new List<(int Order, (GeoPoint2[] Points, double[] Bulges, object[] Edges) Loop)>();
                var used = new bool[pieces.Count];

                for (int first = 0; first < pieces.Count; first++)
                {
                    if (used[first])
                    {
                        continue;
                    }

                    used[first] = true;
                    var chain = new List<HatchPiece> { pieces[first] };

                    while (!chain[chain.Count - 1].End.IsEqualTo(chain[0].Start, tolerance))
                    {
                        GeoPoint2 tail = chain[chain.Count - 1].End;
                        int next = -1;
                        bool turn = false;

                        for (int j = 0; j < pieces.Count && next < 0; j++)
                        {
                            if (used[j])
                            {
                                continue;
                            }

                            if (pieces[j].Start.IsEqualTo(tail, tolerance))
                            {
                                next = j;
                            }
                            else if (pieces[j].End.IsEqualTo(tail, tolerance))
                            {
                                next = j;
                                turn = true;
                            }
                        }

                        if (next < 0)
                        {
                            throw new ArgumentException(
                                "The objects "
                                    + string.Join(", ", chain.Select(p => p.Handle))
                                    + " do not close into an outline: joined end to end they run from ("
                                    + EntityDescriber.R(chain[0].Start.X)
                                    + ", "
                                    + EntityDescriber.R(chain[0].Start.Y)
                                    + ") to ("
                                    + EntityDescriber.R(tail.X)
                                    + ", "
                                    + EntityDescriber.R(tail.Y)
                                    + "), and no other of the handles starts or ends within "
                                    + EntityDescriber.R(tolerance.EqualPoint)
                                    + " mm of that end: a hatch takes the handle of a closed polyline or a circle, a whole ellipse, or lines, arcs, open polylines and elliptic arcs that join end to end into a closed outline."
                            );
                        }

                        used[next] = true;
                        chain.Add(turn ? pieces[next].Reversed() : pieces[next]);
                    }

                    outlines.Add((chain[0].Order, Outline(chain)));
                }

                return outlines;
            }

            /// <summary>The chain turned about: its vertices the other way round and its bulges the other way round and of the other sign.</summary>
            private HatchPiece Reversed() =>
                new HatchPiece(
                    Handle,
                    Order,
                    Enumerable.Reverse(Points).ToArray(),
                    Enumerable.Reverse(Bulges).Select(b => -b).ToArray(),
                    Spline?.Reverse()
                );

            /// <summary>A closed chain as one outline: corners and bulges, or, where it has the part of an ellipse, its edges in order.</summary>
            private static (GeoPoint2[] Points, double[] Bulges, object[] Edges) Outline(List<HatchPiece> chain)
            {
                var points = new List<GeoPoint2>();
                var bulges = new List<double>();
                var edges = new List<object>();
                var up = new GeoVector3(0.0, 0.0, 1.0);

                foreach (HatchPiece piece in chain)
                {
                    if (piece.Spline != null)
                    {
                        edges.Add(piece.Spline);
                    }

                    for (int i = 0; i < piece.Bulges.Length && piece.Spline == null; i++)
                    {
                        points.Add(piece.Points[i]);
                        bulges.Add(piece.Bulges[i]);
                        var from = new GeoPoint3(piece.Points[i].X, piece.Points[i].Y, 0.0);
                        var to = new GeoPoint3(piece.Points[i + 1].X, piece.Points[i + 1].Y, 0.0);
                        edges.Add(
                            piece.Bulges[i] == 0.0
                                ? new GeoEdge3(from, to)
                                : new GeoEdge3(from, to, piece.Bulges[i], up)
                        );
                    }
                }

                string handles = string.Join(", ", chain.Select(p => p.Handle));

                // Two corners enclose something only where the two sides differ, and one corner or none enclose nothing.
                if (
                    chain.All(p => p.Spline == null)
                    && (points.Count < 2 || (points.Count == 2 && Math.Abs(bulges[0] + bulges[1]) <= Tol.EqualVector))
                )
                {
                    throw new ArgumentException("The objects " + handles + " enclose no area.");
                }

                return chain.Any(p => p.Spline != null)
                    ? (null, null, edges.ToArray())
                    : (points.ToArray(), bulges.ToArray(), null);
            }

            private static GeoPoint2 Flat(GeoPoint3 world) => new GeoPoint2(world.X, world.Y);
        }
    }
}
