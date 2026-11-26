using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using GeometryHelper;
using GeometryHelper.Dxf;
using GeometryHelper.Geometry;

namespace OpenCAD2D
{
    /// <summary>The kinds of edge of a <see cref="CadDrawingGraph"/>, numbered as <see cref="CadDrawingGraph.EdgeArray"/> gives them.</summary>
    public enum CadGraphEdgeKind
    {
        /// <summary>Two curves that come within the tolerance of each other: their ends meet, an end lies on the other, they cross, or they run along each other.</summary>
        Touches = 0,

        /// <summary>A closed boundary (a closed polyline, a circle, a whole ellipse, a closed spline) that holds the other entity inside it.</summary>
        Contains = 1,

        /// <summary>A dimension and what it measures: the objects its DIMASSOC ties it to, else the curves its definition points lie on.</summary>
        Dimensions = 2,

        /// <summary>A leader (or multileader) and what its arrow points at.</summary>
        Annotates = 3,
    }

    /// <summary>One edge of a <see cref="CadDrawingGraph"/>.</summary>
    public sealed class CadGraphEdge
    {
        internal CadGraphEdge(int source, int target, CadGraphEdgeKind kind, double x, double y)
        {
            Source = source;
            Target = target;
            Kind = kind;
            X = x;
            Y = y;
        }

        /// <summary>Gets the index of the node the edge leaves: the dimension, the leader, the boundary, the lower index of two that touch.</summary>
        public int Source { get; }

        /// <summary>Gets the index of the node the edge arrives at.</summary>
        public int Target { get; }

        /// <summary>Gets what the edge says.</summary>
        public CadGraphEdgeKind Kind { get; }

        /// <summary>Gets x of the place the two meet, in the drawing's own units; NaN for an edge of no one place (a boundary and what it holds).</summary>
        public double X { get; }

        /// <summary>Gets y of the place the two meet, in the drawing's own units; NaN for an edge of no one place.</summary>
        public double Y { get; }
    }

    /// <summary>
    /// A drawing as a graph for a network to read: a node for each entity of <see cref="CadFlatten"/> (numbered as
    /// <see cref="CadDrawingJson"/> lists them) and an edge for each way two entities are related: they touch, one holds the other,
    /// a dimension measures one, a leader points at one.
    /// </summary>
    /// <remarks>
    /// <para>
    /// A drawing of 10 000 entities is worked with a grid over the boxes of the entities, so that only entities near each other
    /// are tested; the edges come out sorted by kind, source and target, whatever order the grid was walked in, so the same
    /// drawing gives the same graph. Relations are found within one layout, never between model space and a sheet.
    /// </para>
    /// <para>
    /// <b>touches</b>: two curves that come within the tolerance of each other: their ends meet, an end lies on the other, they
    /// cross, or they run along each other. Lines, arcs, circles and polylines (a bulge is an arc) are measured exactly, the
    /// nearest places of their sides and arcs; an ellipse or a spline by the library, an end of one within the tolerance of the
    /// other or a crossing of the two (to the library's 0.001 mm). <b>contains</b>: a closed curve whose outline (arcs and splines taken as 48 chords, a
    /// polyline's bulges as 12 per arc) holds the other entity: the four corners of its box inside or on the outline and the
    /// middle of the box inside it, with a box smaller than its own, so that a line along one side touches the boundary and is
    /// not held by it. <b>dimensions</b>: from a dimension to the entities of its DIMASSOC where it has one, else to the curves
    /// its definition points lie on. <b>annotates</b>: from a leader to the curves (and hatches) its arrow tip lies on.
    /// </para>
    /// </remarks>
    public sealed class CadDrawingGraph
    {
        private const int Chords = 48;
        private const int ArcChords = 12;

        private CadDrawingGraph(CadFlatDrawing flat, IReadOnlyList<CadGraphEdge> edges, double tolerance)
        {
            Flat = flat;
            Edges = edges;
            Tolerance = tolerance;
        }

        /// <summary>Gets the walked drawing: its entities are the nodes, in this order.</summary>
        public CadFlatDrawing Flat { get; }

        /// <summary>Gets the number of nodes.</summary>
        public int NodeCount => Flat.Entities.Count;

        /// <summary>Gets the edges, sorted by kind, then source, then target.</summary>
        public IReadOnlyList<CadGraphEdge> Edges { get; }

        /// <summary>Gets the tolerance in millimetres the touches were found to.</summary>
        public double Tolerance { get; }

        /// <summary>
        /// Gets the edges as numbers, for an array: <c>source, target, kind</c> of each edge in turn (kind as
        /// <see cref="CadGraphEdgeKind"/> numbers it), three numbers an edge. Taken as an array of shape (n, 3) of float64 by
        /// the Python package.
        /// </summary>
        public double[] EdgeArray()
        {
            var numbers = new double[Edges.Count * 3];

            for (int i = 0; i < Edges.Count; i++)
            {
                numbers[i * 3] = Edges[i].Source;
                numbers[i * 3 + 1] = Edges[i].Target;
                numbers[i * 3 + 2] = (int)Edges[i].Kind;
            }

            return numbers;
        }

        /// <summary>
        /// Writes the graph as JSON: <c>schema</c> <c>opencad2d.graph</c>, <c>version</c> 1, <c>tolerance</c>, <c>nodes</c> (each
        /// with its <c>id</c>, handle, kind, layer, colour, linetype, layout, box in millimetres, block path and words) and
        /// <c>edges</c> (<c>source</c>, <c>target</c>, <c>kind</c> as <c>touches</c>, <c>contains</c>, <c>dimensions</c> or
        /// <c>annotates</c>, and <c>at</c>, the place in millimetres or null).
        /// </summary>
        /// <returns>The JSON text.</returns>
        public string ToJson()
        {
            var json = new CadJsonWriter(256 + NodeCount * 160 + Edges.Count * 60);
            json.Begin();
            json.Name("schema").Value("opencad2d.graph");
            json.Name("version").Value(1);
            json.Name("tolerance").Value(Tolerance);
            json.Name("nodes").BeginArray();

            for (int i = 0; i < NodeCount; i++)
            {
                CadFlatEntity flat = Flat.Entities[i];
                json.Begin();
                json.Name("id").Value(i);
                json.Name("handle").Value(flat.Handle);
                json.Name("kind").Value(flat.Entity.DxfName);
                json.Name("layer").Value(flat.Layer);
                json.Name("color").Value(CadDrawingJson.ColorText(flat.Color));
                json.Name("linetype").Value(flat.Linetype);
                json.Name("layout").Value(flat.Layout);
                double[] box = CadDrawingJson.BoxOf(flat.Entity, flat.Scale);

                if (box == null)
                {
                    json.Name("box").Null();
                }
                else
                {
                    json.Name("box").BeginArray().Value(box[0]).Value(box[1]).Value(box[2]).Value(box[3]).EndArray();
                }

                json.Name("path").BeginArray();

                foreach (string name in flat.Path)
                {
                    json.Value(name);
                }

                json.EndArray();
                json.Name("text").Value(WordsOf(flat.Entity));
                json.End();
            }

            json.EndArray();
            json.Name("edges").BeginArray();

            foreach (CadGraphEdge edge in Edges)
            {
                json.Begin();
                json.Name("source").Value(edge.Source);
                json.Name("target").Value(edge.Target);
                json.Name("kind").Value(KindName(edge.Kind));
                json.Name("at");

                if (double.IsNaN(edge.X))
                {
                    json.Null();
                }
                else
                {
                    json.Pair(edge.X * Flat.Entities[edge.Source].Scale, edge.Y * Flat.Entities[edge.Source].Scale);
                }

                json.End();
            }

            json.EndArray();
            json.End();
            return json.ToString();
        }

        /// <summary>The words an entity shows, or null for one that shows none.</summary>
        private static string WordsOf(DxfEntity entity)
        {
            try
            {
                switch (entity)
                {
                    case DxfAttributeReference attribute:
                        return attribute.IsMTextAttribute ? attribute.MTextAttribute.Text : attribute.TextString;
                    case DxfDBText text:
                        return text.TextString;
                    case DxfMText mtext:
                        return mtext.Text;
                    case DxfDimension dimension:
                        return dimension.DimensionText;
                    case DxfMLeader mleader:
                        return mleader.ContentType == DxfContentType.MTextContent && mleader.MText != null
                            ? mleader.MText.Text
                            : null;
                    default:
                        return null;
                }
            }
            catch (Exception exception)
                when (exception is InvalidOperationException
                    || exception is ArgumentException
                    || exception is NotSupportedException
                )
            {
                return null;
            }
        }

        /// <summary>The name an edge kind goes under in JSON.</summary>
        internal static string KindName(CadGraphEdgeKind kind) =>
            kind == CadGraphEdgeKind.Touches ? "touches"
            : kind == CadGraphEdgeKind.Contains ? "contains"
            : kind == CadGraphEdgeKind.Dimensions ? "dimensions"
            : "annotates";

        /// <summary>
        /// Builds the graph of a drawing: walks it (<see cref="CadFlatten.Walk"/>) and finds the edges.
        /// </summary>
        /// <param name="document">The drawing.</param>
        /// <param name="layout">The layout to take, or null for model space and every layout.</param>
        /// <param name="tolerance">How near two ends must be to touch, in millimetres: 0.001 is the library's own.</param>
        /// <returns>The graph.</returns>
        /// <exception cref="ArgumentNullException">Thrown when the drawing is null.</exception>
        /// <exception cref="ArgumentException">Thrown when the drawing has no layout of that name, or the tolerance is not a
        /// finite number above nought.</exception>
        public static CadDrawingGraph Build(CadDocument document, string layout, double tolerance) =>
            Build(CadFlatten.Walk(document, layout), tolerance);

        /// <summary>Builds the graph of a drawing already walked.</summary>
        /// <param name="flat">The walk.</param>
        /// <param name="tolerance">How near two ends must be to touch, in millimetres.</param>
        /// <returns>The graph.</returns>
        /// <exception cref="ArgumentNullException">Thrown when the walk is null.</exception>
        /// <exception cref="ArgumentException">Thrown when the tolerance is not a finite number above nought.</exception>
        public static CadDrawingGraph Build(CadFlatDrawing flat, double tolerance)
        {
            if (flat == null)
            {
                throw new ArgumentNullException(nameof(flat));
            }

            Guard.Argument(
                Guard.IsFinite(tolerance) && tolerance > 0.0,
                nameof(tolerance),
                "The tolerance is a number of millimetres above nought."
            );
            var edges = new Dictionary<long, CadGraphEdge>[4];

            for (int k = 0; k < edges.Length; k++)
            {
                edges[k] = new Dictionary<long, CadGraphEdge>(new KeyComparer());
            }

            var items = new Item[flat.Entities.Count];

            for (int i = 0; i < items.Length; i++)
            {
                items[i] = new Item(flat.Entities[i]);
            }

            // One layout at a time, in the order the walk gives them: a node of a sheet is never related to one of model space.
            foreach (
                IGrouping<string, int> group in Enumerable.Range(0, items.Length).GroupBy(i => flat.Entities[i].Layout)
            )
            {
                var layoutItems = group.ToList();
                double tolerance2 = tolerance / flat.Entities[layoutItems[0]].Scale;
                new Builder(items, layoutItems, tolerance2, edges).Run();
            }

            var sorted = new List<CadGraphEdge>();

            foreach (Dictionary<long, CadGraphEdge> ofKind in edges)
            {
                sorted.AddRange(ofKind.Values.OrderBy(e => e.Source).ThenBy(e => e.Target));
            }

            return new CadDrawingGraph(flat, sorted.AsReadOnly(), tolerance);
        }

        /// <summary>The key of an edge within its kind: the source as the high half of a long, the target as the low.</summary>
        private static long Key(int source, int target) => ((long)source << 32) | (uint)target;

        /// <summary>Compares the keys of edges as the numbers they are, hashing both halves so that near numbers spread.</summary>
        private sealed class KeyComparer : IEqualityComparer<long>
        {
            public bool Equals(long x, long y) => x == y;

            public int GetHashCode(long key) => unchecked((int)key ^ (int)(key >> 32));
        }

        /// <summary>What the graph keeps of a node: its box, and the curve and region it may be.</summary>
        private sealed class Item
        {
            public readonly CadFlatEntity Flat;
            public bool HasBox;
            public double MinX,
                MinY,
                MaxX,
                MaxY;
            public DxfCurve Curve;
            public CadPrimitive[] Prims;
            public GeoPoint2? Start,
                End;
            public List<GeoPoint2> Outline;

            public Item(CadFlatEntity flat)
            {
                Flat = flat;

                try
                {
                    GeoAabb3 box = flat.Entity.GeometricExtents;

                    if (
                        !double.IsNaN(box.Min.X)
                        && !double.IsInfinity(box.Min.X)
                        && !double.IsInfinity(box.Max.X)
                        && !double.IsInfinity(box.Min.Y)
                        && !double.IsInfinity(box.Max.Y)
                    )
                    {
                        HasBox = true;
                        MinX = box.Min.X;
                        MinY = box.Min.Y;
                        MaxX = box.Max.X;
                        MaxY = box.Max.Y;
                    }
                }
                catch (Exception exception)
                    when (exception is InvalidOperationException
                        || exception is ArgumentException
                        || exception is NotSupportedException
                    )
                {
                    // An entity with no extents is a node, and has no place.
                }

                if (
                    flat.Entity is DxfCurve curve
                    && !(curve is DxfLeader)
                    && !(curve is DxfRay)
                    && !(curve is DxfXline)
                    && HasBox
                )
                {
                    Curve = curve;

                    try
                    {
                        if (!curve.Closed)
                        {
                            GeoPoint3 start = curve.StartPoint;
                            GeoPoint3 end = curve.EndPoint;
                            Start = new GeoPoint2(start.X, start.Y);
                            End = new GeoPoint2(end.X, end.Y);
                        }

                        Outline = OutlineOf(curve);
                        Prims = CadPrimitive.Of(curve)?.ToArray();
                    }
                    catch (Exception exception)
                        when (exception is InvalidOperationException
                            || exception is ArgumentException
                            || exception is NotSupportedException
                        )
                    {
                        Curve = null;
                    }
                }
            }

            public double Area => (MaxX - MinX) * (MaxY - MinY);
        }

        /// <summary>The outline of a closed curve as chords, or null for one that is open or has none to be inside of.</summary>
        private static List<GeoPoint2> OutlineOf(DxfCurve curve)
        {
            if (!curve.Closed)
            {
                return null;
            }

            var outline = new List<GeoPoint2>();

            if (curve is DxfPolyline polyline)
            {
                // The corners of a closed polyline, each bulge sampled as the arc it draws.
                int count = polyline.NumberOfVertices;
                bool flip = polyline.Normal.Z < 0.0;

                for (int i = 0; i < count; i++)
                {
                    GeoPoint3 from = polyline.GetPoint3dAt(i);
                    outline.Add(new GeoPoint2(from.X, from.Y));
                    double bulge = flip ? -polyline.GetBulgeAt(i) : polyline.GetBulgeAt(i);

                    if (Math.Abs(bulge) > 1E-12)
                    {
                        // The arc of the bulge: its centre lies on the chord's left, (c / 2) / tan(angle / 2) from the middle.
                        GeoPoint3 to = polyline.GetPoint3dAt((i + 1) % count);
                        double angle = 4.0 * Math.Atan(bulge);
                        double dx = to.X - from.X;
                        double dy = to.Y - from.Y;
                        double chord = Math.Sqrt(dx * dx + dy * dy);

                        if (chord > 0.0)
                        {
                            double away = chord / 2.0 / Math.Tan(angle / 2.0);
                            double centerX = (from.X + to.X) / 2.0 - dy / chord * away;
                            double centerY = (from.Y + to.Y) / 2.0 + dx / chord * away;
                            double radius = Math.Sqrt(
                                (from.X - centerX) * (from.X - centerX) + (from.Y - centerY) * (from.Y - centerY)
                            );
                            double a0 = Math.Atan2(from.Y - centerY, from.X - centerX);

                            for (int k = 1; k < ArcChords; k++)
                            {
                                double a = a0 + angle * k / ArcChords;
                                outline.Add(
                                    new GeoPoint2(centerX + radius * Math.Cos(a), centerY + radius * Math.Sin(a))
                                );
                            }
                        }
                    }
                }

                return outline.Count >= 3 ? outline : null;
            }

            if (curve is DxfPolyline2d poly2d)
            {
                foreach (DxfVertex2d vertex in poly2d.Cast<DxfVertex2d>())
                {
                    outline.Add(new GeoPoint2(vertex.Position.X, vertex.Position.Y));
                }

                return outline.Count >= 3 ? outline : null;
            }

            double start = curve.StartParam;
            double end = curve.EndParam;

            for (int i = 0; i < Chords; i++)
            {
                GeoPoint3 point = curve.GetPointAtParameter(start + (end - start) * i / Chords);
                outline.Add(new GeoPoint2(point.X, point.Y));
            }

            return outline;
        }

        private sealed class Builder
        {
            private readonly Item[] _items;
            private readonly List<int> _members;
            private readonly double _tolerance;
            private readonly Dictionary<long, CadGraphEdge>[] _edges;
            private readonly Dictionary<long, List<int>> _cells = new Dictionary<long, List<int>>(new KeyComparer());
            private readonly List<int> _large = new List<int>();
            private double _cell;

            public Builder(Item[] items, List<int> members, double tolerance, Dictionary<long, CadGraphEdge>[] edges)
            {
                _items = items;
                _members = members;
                _tolerance = tolerance;
                _edges = edges;
            }

            public void Run()
            {
                Fill();
                Touches();
                Contains();
                Dimensions();
                Annotations();
            }

            // ---- the grid ------------------------------------------------------------------------------------------

            private static long CellKey(int x, int y) => ((long)x << 32) | (uint)y;

            private int CellOf(double v) =>
                (int)Math.Max(Math.Min(Math.Floor(v / _cell), int.MaxValue / 2), int.MinValue / 2);

            /// <summary>Puts every node with a box in the cells it covers; one covering more than 32 by 32 cells goes in a list of its own.</summary>
            private void Fill()
            {
                var sizes = new List<double>();
                double minX = double.MaxValue,
                    minY = double.MaxValue,
                    maxX = double.MinValue,
                    maxY = double.MinValue;

                foreach (int i in _members)
                {
                    Item item = _items[i];

                    if (!item.HasBox)
                    {
                        continue;
                    }

                    double size = Math.Max(item.MaxX - item.MinX, item.MaxY - item.MinY);

                    if (size > 0.0)
                    {
                        sizes.Add(size);
                    }

                    minX = Math.Min(minX, item.MinX);
                    minY = Math.Min(minY, item.MinY);
                    maxX = Math.Max(maxX, item.MaxX);
                    maxY = Math.Max(maxY, item.MaxY);
                }

                if (minX > maxX)
                {
                    _cell = 1.0;
                    return;
                }

                sizes.Sort();
                double median = sizes.Count == 0 ? 1.0 : sizes[sizes.Count / 2];
                double extent = Math.Max(maxX - minX, maxY - minY);
                _cell = Math.Max(Math.Max(median * 4.0, extent / 1024.0), _tolerance * 8.0);

                foreach (int i in _members)
                {
                    Item item = _items[i];

                    if (!item.HasBox)
                    {
                        continue;
                    }

                    int x0 = CellOf(item.MinX - _tolerance),
                        x1 = CellOf(item.MaxX + _tolerance);
                    int y0 = CellOf(item.MinY - _tolerance),
                        y1 = CellOf(item.MaxY + _tolerance);

                    if ((long)(x1 - x0 + 1) * (y1 - y0 + 1) > 1024L)
                    {
                        _large.Add(i);
                        continue;
                    }

                    for (int x = x0; x <= x1; x++)
                    {
                        for (int y = y0; y <= y1; y++)
                        {
                            long key = CellKey(x, y);

                            if (!_cells.TryGetValue(key, out List<int> list))
                            {
                                list = new List<int>(4);
                                _cells.Add(key, list);
                            }

                            list.Add(i);
                        }
                    }
                }
            }

            /// <summary>The nodes whose boxes may meet a box grown by the tolerance, each once, by index.</summary>
            private IEnumerable<int> Near(double minX, double minY, double maxX, double maxY, HashSet<int> seen)
            {
                seen.Clear();
                int x0 = CellOf(minX - _tolerance),
                    x1 = CellOf(maxX + _tolerance);
                int y0 = CellOf(minY - _tolerance),
                    y1 = CellOf(maxY + _tolerance);

                if ((long)(x1 - x0 + 1) * (y1 - y0 + 1) <= 1024L)
                {
                    for (int x = x0; x <= x1; x++)
                    {
                        for (int y = y0; y <= y1; y++)
                        {
                            if (_cells.TryGetValue(CellKey(x, y), out List<int> list))
                            {
                                foreach (int j in list)
                                {
                                    if (seen.Add(j))
                                    {
                                        yield return j;
                                    }
                                }
                            }
                        }
                    }
                }
                else
                {
                    // A box of more than 1024 cells meets the nodes of the cells that exist, not each cell it covers.
                    foreach (KeyValuePair<long, List<int>> pair in _cells)
                    {
                        foreach (int j in pair.Value)
                        {
                            if (seen.Add(j))
                            {
                                yield return j;
                            }
                        }
                    }
                }

                foreach (int j in _large)
                {
                    if (seen.Add(j))
                    {
                        yield return j;
                    }
                }
            }

            private bool BoxesMeet(Item a, Item b, double grow) =>
                a.HasBox
                && b.HasBox
                && a.MinX <= b.MaxX + grow
                && b.MinX <= a.MaxX + grow
                && a.MinY <= b.MaxY + grow
                && b.MinY <= a.MaxY + grow;

            private void Add(CadGraphEdgeKind kind, int source, int target, double x, double y)
            {
                Dictionary<long, CadGraphEdge> ofKind = _edges[(int)kind];
                long key = Key(source, target);

                if (!ofKind.ContainsKey(key))
                {
                    ofKind.Add(key, new CadGraphEdge(source, target, kind, x, y));
                }
            }

            // ---- touches -------------------------------------------------------------------------------------------

            private void Touches()
            {
                var seen = new HashSet<int>();

                foreach (int i in _members)
                {
                    Item a = _items[i];

                    if (a.Curve == null)
                    {
                        continue;
                    }

                    foreach (int j in Near(a.MinX, a.MinY, a.MaxX, a.MaxY, seen))
                    {
                        Item b = _items[j];

                        if (j <= i || b.Curve == null || !BoxesMeet(a, b, _tolerance))
                        {
                            continue;
                        }

                        if (Meet(a, b, out double x, out double y))
                        {
                            Add(CadGraphEdgeKind.Touches, i, j, x, y);
                        }
                    }
                }
            }

            private static double Distance(GeoPoint2 p, GeoPoint2 q) =>
                Math.Sqrt((p.X - q.X) * (p.X - q.X) + (p.Y - q.Y) * (p.Y - q.Y));

            /// <summary>Whether two curves meet, and where: ends together, an end on the other, or a crossing.</summary>
            private bool Meet(Item a, Item b, out double x, out double y)
            {
                x = y = 0.0;

                // Curves made of sides and arcs are measured here, exactly; the rest (splines, ellipses) by the library.
                if (a.Prims != null && b.Prims != null)
                {
                    return MeetPrimitives(a.Prims, b.Prims, out x, out y);
                }
                GeoPoint2?[] ends = { a.Start, a.End };
                GeoPoint2?[] others = { b.Start, b.End };

                foreach (GeoPoint2? p in ends)
                {
                    foreach (GeoPoint2? q in others)
                    {
                        if (p.HasValue && q.HasValue && Distance(p.Value, q.Value) <= _tolerance)
                        {
                            x = (p.Value.X + q.Value.X) / 2.0;
                            y = (p.Value.Y + q.Value.Y) / 2.0;
                            return true;
                        }
                    }
                }

                foreach (GeoPoint2? p in ends)
                {
                    if (p.HasValue && OnCurve(b.Curve, p.Value, out x, out y))
                    {
                        return true;
                    }
                }

                foreach (GeoPoint2? q in others)
                {
                    if (q.HasValue && OnCurve(a.Curve, q.Value, out x, out y))
                    {
                        return true;
                    }
                }

                try
                {
                    IReadOnlyList<GeoPoint3> points = a.Curve.IntersectWith(b.Curve, DxfIntersect.OnBothOperands);

                    if (points.Count > 0)
                    {
                        x = points[0].X;
                        y = points[0].Y;
                        return true;
                    }
                }
                catch (Exception exception)
                    when (exception is InvalidOperationException
                        || exception is ArgumentException
                        || exception is NotSupportedException
                    )
                {
                    // A pair the library has no intersection of does not cross.
                }

                return false;
            }

            /// <summary>
            /// Whether two curves made of sides and arcs come within the tolerance of each other (they cross, meet or lie along each
            /// other), and where: the first pair of parts, in the order of the curves, that do.
            /// </summary>
            private bool MeetPrimitives(CadPrimitive[] first, CadPrimitive[] second, out double x, out double y)
            {
                for (int i = 0; i < first.Length; i++)
                {
                    for (int j = 0; j < second.Length; j++)
                    {
                        if (
                            first[i].MinX > second[j].MaxX + _tolerance
                            || second[j].MinX > first[i].MaxX + _tolerance
                            || first[i].MinY > second[j].MaxY + _tolerance
                            || second[j].MinY > first[i].MaxY + _tolerance
                        )
                        {
                            continue;
                        }

                        if (CadPrimitive.Distance(first[i], second[j], out x, out y) <= _tolerance)
                        {
                            return true;
                        }
                    }
                }

                x = y = 0.0;
                return false;
            }

            /// <summary>Whether a point is within the tolerance of a curve, and the nearest place on it.</summary>
            private bool OnCurve(DxfCurve curve, GeoPoint2 point, out double x, out double y)
            {
                x = y = 0.0;

                try
                {
                    GeoPoint3 near = curve.GetClosestPointTo(new GeoPoint3(point.X, point.Y, 0.0), false);

                    if (Distance(new GeoPoint2(near.X, near.Y), point) <= _tolerance)
                    {
                        x = near.X;
                        y = near.Y;
                        return true;
                    }
                }
                catch (Exception exception)
                    when (exception is InvalidOperationException
                        || exception is ArgumentException
                        || exception is NotSupportedException
                    )
                {
                    // A curve with nothing near has nothing to be on.
                }

                return false;
            }

            // ---- contains ------------------------------------------------------------------------------------------

            /// <summary>Whether a point is inside an outline; one on it, to within the tolerance, is inside unless strict is asked for.</summary>
            private bool Inside(List<GeoPoint2> outline, double x, double y, bool strict)
            {
                bool inside = false;

                for (int i = 0, j = outline.Count - 1; i < outline.Count; j = i++)
                {
                    GeoPoint2 p = outline[i];
                    GeoPoint2 q = outline[j];

                    if (DistanceToSegment(x, y, p, q) <= _tolerance)
                    {
                        return !strict;
                    }

                    if ((p.Y > y) != (q.Y > y) && x < (q.X - p.X) * (y - p.Y) / (q.Y - p.Y) + p.X)
                    {
                        inside = !inside;
                    }
                }

                return inside;
            }

            private static double DistanceToSegment(double x, double y, GeoPoint2 p, GeoPoint2 q)
            {
                double dx = q.X - p.X;
                double dy = q.Y - p.Y;
                double length2 = dx * dx + dy * dy;
                double t =
                    length2 <= 0.0 ? 0.0 : Math.Max(0.0, Math.Min(1.0, ((x - p.X) * dx + (y - p.Y) * dy) / length2));
                double ex = p.X + t * dx - x;
                double ey = p.Y + t * dy - y;
                return Math.Sqrt(ex * ex + ey * ey);
            }

            private void Contains()
            {
                var seen = new HashSet<int>();

                foreach (int i in _members)
                {
                    Item region = _items[i];

                    if (region.Outline == null)
                    {
                        continue;
                    }

                    foreach (int j in Near(region.MinX, region.MinY, region.MaxX, region.MaxY, seen))
                    {
                        Item inner = _items[j];

                        if (
                            j == i
                            || !inner.HasBox
                            || inner.MinX < region.MinX - _tolerance
                            || inner.MinY < region.MinY - _tolerance
                            || inner.MaxX > region.MaxX + _tolerance
                            || inner.MaxY > region.MaxY + _tolerance
                            || inner.Area >= region.Area - _tolerance * _tolerance
                        )
                        {
                            continue;
                        }

                        if (
                            Inside(region.Outline, inner.MinX, inner.MinY, false)
                            && Inside(region.Outline, inner.MaxX, inner.MinY, false)
                            && Inside(region.Outline, inner.MaxX, inner.MaxY, false)
                            && Inside(region.Outline, inner.MinX, inner.MaxY, false)
                            && Inside(
                                region.Outline,
                                (inner.MinX + inner.MaxX) / 2.0,
                                (inner.MinY + inner.MaxY) / 2.0,
                                true
                            )
                        )
                        {
                            Add(CadGraphEdgeKind.Contains, i, j, double.NaN, double.NaN);
                        }
                    }
                }
            }

            // ---- dimensions ----------------------------------------------------------------------------------------

            /// <summary>The points of a dimension that lie on what it measures.</summary>
            private static IEnumerable<GeoPoint3> MeasuredPoints(DxfDimension dimension)
            {
                switch (dimension)
                {
                    case DxfRotatedDimension rotated:
                        return new[] { rotated.XLine1Point, rotated.XLine2Point };
                    case DxfAlignedDimension aligned:
                        return new[] { aligned.XLine1Point, aligned.XLine2Point };
                    case DxfRadialDimension radial:
                        return new[] { radial.ChordPoint };
                    case DxfDiametricDimension diameter:
                        return new[] { diameter.ChordPoint, diameter.FarChordPoint };
                    case DxfOrdinateDimension ordinate:
                        return new[] { ordinate.DefiningPoint };
                    case DxfLineAngularDimension2 angular2:
                        return new[]
                        {
                            angular2.XLine1Start,
                            angular2.XLine1End,
                            angular2.XLine2Start,
                            angular2.XLine2End,
                        };
                    case DxfPoint3AngularDimension angular3:
                        return new[] { angular3.XLine1Point, angular3.XLine2Point };
                    case DxfArcDimension arcLength:
                        return new[] { arcLength.XLine1Point, arcLength.XLine2Point };
                    case DxfRadialDimensionLarge jogged:
                        return new[] { jogged.ChordPoint };
                    default:
                        return new GeoPoint3[0];
                }
            }

            private void Dimensions()
            {
                var byHandle = new Dictionary<string, List<int>>(StringComparer.OrdinalIgnoreCase);

                foreach (int i in _members)
                {
                    string handle = _items[i].Flat.Handle;

                    if (!byHandle.TryGetValue(handle ?? string.Empty, out List<int> list))
                    {
                        list = new List<int>(1);
                        byHandle.Add(handle ?? string.Empty, list);
                    }

                    list.Add(i);
                }

                var seen = new HashSet<int>();

                foreach (int i in _members)
                {
                    if (!(_items[i].Flat.Entity is DxfDimension dimension))
                    {
                        continue;
                    }

                    bool tied = false;

                    try
                    {
                        DxfDimAssoc assoc = dimension.DimAssoc;

                        if (assoc != null)
                        {
                            foreach (DxfOsnapPointRef reference in assoc.PointReferences)
                            {
                                if (
                                    reference.ObjectHandle != null
                                    && byHandle.TryGetValue(reference.ObjectHandle, out List<int> targets)
                                )
                                {
                                    foreach (int target in targets.Where(t => t != i))
                                    {
                                        Add(
                                            CadGraphEdgeKind.Dimensions,
                                            i,
                                            target,
                                            reference.Point.X,
                                            reference.Point.Y
                                        );
                                        tied = true;
                                    }
                                }
                            }
                        }
                    }
                    catch (Exception exception)
                        when (exception is InvalidOperationException
                            || exception is ArgumentException
                            || exception is NotSupportedException
                        )
                    {
                        // A dimension whose association cannot be read is taken by its points.
                    }

                    if (tied)
                    {
                        continue;
                    }

                    foreach (GeoPoint3 point in MeasuredPoints(dimension))
                    {
                        foreach (int j in CurvesAt(point.X, point.Y, seen, i))
                        {
                            Add(CadGraphEdgeKind.Dimensions, i, j, point.X, point.Y);
                        }
                    }
                }
            }

            /// <summary>The curve nodes within the tolerance of a point, other than the one that asks, in index order.</summary>
            private List<int> CurvesAt(double x, double y, HashSet<int> seen, int except)
            {
                var found = new List<int>();

                foreach (int j in Near(x, y, x, y, seen))
                {
                    Item item = _items[j];

                    if (j != except && item.Curve != null && OnCurve(item.Curve, new GeoPoint2(x, y), out _, out _))
                    {
                        found.Add(j);
                    }
                }

                found.Sort();
                return found;
            }

            // ---- annotates -----------------------------------------------------------------------------------------

            private void Annotations()
            {
                var seen = new HashSet<int>();

                foreach (int i in _members)
                {
                    DxfEntity entity = _items[i].Flat.Entity;
                    var tips = new List<GeoPoint3>();

                    try
                    {
                        if (entity is DxfLeader leader && leader.HasArrowHead && leader.NumVertices > 0)
                        {
                            tips.Add(leader.FirstVertex);
                        }
                        else if (entity is DxfMLeader mleader)
                        {
                            GeoPoint3 text = mleader.TextLocation;

                            foreach (object shape in mleader.GetGeometry())
                            {
                                IReadOnlyList<GeoPoint3> vertices =
                                    shape is GeoPolyline3 polyline ? polyline.Vertices
                                    : shape is GeoLine3 line ? new[] { line.StartPoint, line.EndPoint }
                                    : null;

                                if (vertices == null || vertices.Count < 2)
                                {
                                    continue;
                                }

                                // The arrow is at the end of the line farthest from the words.
                                GeoPoint3 first = vertices[0];
                                GeoPoint3 last = vertices[vertices.Count - 1];
                                tips.Add(
                                    (first.X - text.X) * (first.X - text.X) + (first.Y - text.Y) * (first.Y - text.Y)
                                    >= (last.X - text.X) * (last.X - text.X) + (last.Y - text.Y) * (last.Y - text.Y)
                                        ? first
                                        : last
                                );
                            }
                        }
                    }
                    catch (Exception exception)
                        when (exception is InvalidOperationException
                            || exception is ArgumentException
                            || exception is NotSupportedException
                        )
                    {
                        continue;
                    }

                    foreach (GeoPoint3 tip in tips)
                    {
                        foreach (int j in Near(tip.X, tip.Y, tip.X, tip.Y, seen).ToList().OrderBy(k => k))
                        {
                            Item item = _items[j];
                            bool onIt =
                                j != i
                                && (
                                    item.Curve != null && OnCurve(item.Curve, new GeoPoint2(tip.X, tip.Y), out _, out _)
                                    || item.Flat.Entity is DxfHatch && item.HasBox && Within(item, tip.X, tip.Y)
                                );

                            if (onIt)
                            {
                                Add(CadGraphEdgeKind.Annotates, i, j, tip.X, tip.Y);
                            }
                        }
                    }
                }
            }

            private bool Within(Item item, double x, double y) =>
                x >= item.MinX - _tolerance
                && x <= item.MaxX + _tolerance
                && y >= item.MinY - _tolerance
                && y <= item.MaxY + _tolerance;
        }
    }
}
