using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using GeometryHelper;
using OpenCAD2D.Remote;

namespace OpenCAD2D
{
    /// <summary>One object of a comparison of two drawings: where it is, what it is, and for a changed one what differs.</summary>
    public sealed class CadDrawingChange
    {
        internal CadDrawingChange(
            string handle,
            string kind,
            string layout,
            string layer,
            IReadOnlyList<string> properties,
            double[] boxBefore,
            double[] boxAfter,
            double[] movedBy
        )
        {
            Handle = handle;
            Kind = kind;
            Layout = layout;
            Layer = layer;
            Properties = properties;
            BoxBefore = boxBefore;
            BoxAfter = boxAfter;
            MovedBy = movedBy;
        }

        /// <summary>
        /// Gets the handle of the object: its own for one that stands in a space, the handle of the block reference it came out
        /// of for one drawn by a block, which then shares it with its pieces (see <see cref="CadFlatEntity.Handle"/>).
        /// </summary>
        public string Handle { get; }

        /// <summary>Gets the DXF name of the kind, as <c>LINE</c> or <c>MTEXT</c>.</summary>
        public string Kind { get; }

        /// <summary>Gets the layout it is drawn in, or <c>Model</c>; of the first drawing for a removed one, else of the second.</summary>
        public string Layout { get; }

        /// <summary>Gets the layer; of the first drawing for a removed one, else of the second.</summary>
        public string Layer { get; }

        /// <summary>
        /// Gets the names of what differs, in alphabetical order, as the JSON of <see cref="CadDrawingJson"/> names its fields
        /// (<c>layer</c>, <c>color</c>, <c>box</c>, <c>start</c>, <c>text</c>); empty for an object added or removed.
        /// </summary>
        public IReadOnlyList<string> Properties { get; }

        /// <summary>Gets the box it took in the first drawing, <c>[minX, minY, maxX, maxY]</c> in millimetres; null for an added object or one that draws nothing.</summary>
        public double[] BoxBefore { get; }

        /// <summary>Gets the box it takes in the second drawing; null for a removed object or one that draws nothing.</summary>
        public double[] BoxAfter { get; }

        /// <summary>
        /// Gets how far its box moved, <c>[dx, dy]</c> in millimetres, where it kept its width and height and its place changed;
        /// null where it did not move, or changed its size, or has no box in one of the drawings.
        /// </summary>
        public double[] MovedBy { get; }
    }

    /// <summary>What <see cref="CadDrawingDiff.Compare"/> found: the objects only the second drawing has, only the first has, and those both have that differ.</summary>
    public sealed class CadDrawingDiffResult
    {
        internal CadDrawingDiffResult(
            IReadOnlyList<CadDrawingChange> added,
            IReadOnlyList<CadDrawingChange> removed,
            IReadOnlyList<CadDrawingChange> changed,
            int same
        )
        {
            Added = added;
            Removed = removed;
            Changed = changed;
            Unchanged = same;
        }

        /// <summary>Gets the objects the second drawing has and the first has not, in the order of their handles.</summary>
        public IReadOnlyList<CadDrawingChange> Added { get; }

        /// <summary>Gets the objects the first drawing has and the second has not, in the order of their handles.</summary>
        public IReadOnlyList<CadDrawingChange> Removed { get; }

        /// <summary>Gets the objects both drawings have whose properties differ beyond the tolerance, in the order of their handles.</summary>
        public IReadOnlyList<CadDrawingChange> Changed { get; }

        /// <summary>Gets how many objects both drawings have that do not differ.</summary>
        public int Unchanged { get; }

        /// <summary>Gets whether the two drawings hold the same objects with the same properties.</summary>
        public bool IsSame => Added.Count == 0 && Removed.Count == 0 && Changed.Count == 0;
    }

    /// <summary>
    /// Compares two drawings object by object, by handle: what was added, what was removed and what changed, with the names of
    /// the properties that differ and how far an object moved. It reads each drawing as <see cref="CadDrawingJson"/> writes it
    /// (every entity walked down to its shapes, in millimetres), so a block's pieces are told apart by their index path and a
    /// drawing in inches is compared with one in millimetres as the same thing. It is meant for two states of one drawing: a file
    /// and the file after an edit, a dump of the app and the app now. Two drawings that were drawn separately share no handles
    /// and every object of one is added or removed.
    /// </summary>
    public static class CadDrawingDiff
    {
        /// <summary>
        /// Compares two drawings: all their layouts, model space included. Two lines of one handle whose end moved 3 mm come out as one
        /// changed object with <c>end</c> and <c>box</c> among its properties.
        /// </summary>
        /// <param name="before">The first drawing, the one things changed from.</param>
        /// <param name="after">The second drawing.</param>
        /// <param name="tolerance">How far two numbers may differ and still be the same, in millimetres: its <see cref="Tolerance.EqualPoint"/>.</param>
        /// <returns>The objects added, removed and changed.</returns>
        /// <exception cref="ArgumentNullException">Thrown when a drawing is null.</exception>
        public static CadDrawingDiffResult Compare(CadDocument before, CadDocument after, Tolerance tolerance)
        {
            if (before == null)
            {
                throw new ArgumentNullException(nameof(before));
            }

            if (after == null)
            {
                throw new ArgumentNullException(nameof(after));
            }

            Dictionary<string, JsonObject> first = Entities(before);
            Dictionary<string, JsonObject> second = Entities(after);
            double epsilon = tolerance.EqualPoint;
            var added = new List<CadDrawingChange>();
            var removed = new List<CadDrawingChange>();
            var changed = new List<CadDrawingChange>();
            int same = 0;

            foreach (KeyValuePair<string, JsonObject> pair in first)
            {
                if (!second.TryGetValue(pair.Key, out JsonObject other))
                {
                    removed.Add(Removed(pair.Value));
                    continue;
                }

                List<string> differ = Differences(pair.Value, other, epsilon);

                if (differ.Count == 0)
                {
                    same++;
                    continue;
                }

                changed.Add(Changed(pair.Value, other, differ, epsilon));
            }

            foreach (KeyValuePair<string, JsonObject> pair in second)
            {
                if (!first.ContainsKey(pair.Key))
                {
                    added.Add(Added(pair.Value));
                }
            }

            return new CadDrawingDiffResult(Ordered(added), Ordered(removed), Ordered(changed), same);
        }

        private static List<CadDrawingChange> Ordered(List<CadDrawingChange> rows) =>
            rows.OrderBy(r => HandleNumber(r.Handle))
                .ThenBy(r => r.Handle, StringComparer.Ordinal)
                .ThenBy(r => r.Kind, StringComparer.Ordinal)
                .ThenBy(r => r.Layout, StringComparer.Ordinal)
                .ThenBy(r => r.BoxAfter == null ? double.NegativeInfinity : r.BoxAfter[0])
                .ThenBy(r => r.BoxBefore == null ? double.NegativeInfinity : r.BoxBefore[0])
                .ToList();

        private static long HandleNumber(string handle) =>
            long.TryParse(handle, NumberStyles.AllowHexSpecifier, CultureInfo.InvariantCulture, out long number)
                ? number
                : long.MaxValue;

        /// <summary>The entities of a drawing by identity: the handle, and for a piece of a block the index path that tells it from its sisters.</summary>
        private static Dictionary<string, JsonObject> Entities(CadDocument document)
        {
            var found = new Dictionary<string, JsonObject>(StringComparer.Ordinal);
            var root = (JsonObject)RemoteJson.Parse(CadDrawingJson.Write(document, null));

            foreach (object item in root.GetArray("entities"))
            {
                if (item is JsonObject entity)
                {
                    string key = entity.GetString("handle") ?? string.Empty;

                    if (entity.Get("indexPath") is JsonArray path)
                    {
                        key +=
                            "/" + string.Join(".", path.Select(i => Convert.ToString(i, CultureInfo.InvariantCulture)));
                    }

                    // A handle two entities share (a drawing the library repaired) keeps the first, so that a pair is never lost silently:
                    // the second is told apart by its place in the walk.
                    int again = 1;
                    string unique = key;

                    while (found.ContainsKey(unique))
                    {
                        unique = key + "#" + again++.ToString(CultureInfo.InvariantCulture);
                    }

                    found[unique] = entity;
                }
            }

            return found;
        }

        private static CadDrawingChange Added(JsonObject entity) =>
            Row(entity, new List<string>(), null, Box(entity), null);

        private static CadDrawingChange Removed(JsonObject entity) =>
            Row(entity, new List<string>(), Box(entity), null, null);

        private static CadDrawingChange Changed(
            JsonObject before,
            JsonObject after,
            List<string> differ,
            double epsilon
        )
        {
            double[] from = Box(before);
            double[] to = Box(after);
            double[] moved = null;

            if (from != null && to != null)
            {
                double dx = to[0] - from[0];
                double dy = to[1] - from[1];
                bool sameSize =
                    Math.Abs(to[2] - to[0] - (from[2] - from[0])) <= epsilon
                    && Math.Abs(to[3] - to[1] - (from[3] - from[1])) <= epsilon;

                if (sameSize && (Math.Abs(dx) > epsilon || Math.Abs(dy) > epsilon))
                {
                    moved = new[] { dx, dy };
                }
            }

            return Row(after, differ, from, to, moved);
        }

        private static CadDrawingChange Row(
            JsonObject entity,
            List<string> differ,
            double[] before,
            double[] after,
            double[] moved
        ) =>
            new CadDrawingChange(
                entity.GetString("handle") ?? string.Empty,
                entity.GetString("kind") ?? string.Empty,
                entity.GetString("layout") ?? string.Empty,
                entity.GetString("layer") ?? string.Empty,
                differ,
                before,
                after,
                moved
            );

        private static double[] Box(JsonObject entity)
        {
            if (!(entity.Get("box") is JsonArray box) || box.Count != 4)
            {
                return null;
            }

            return box.Select(v => Number(v)).ToArray();
        }

        private static double Number(object value) =>
            value is double real ? real
            : value is long whole ? whole
            : double.NaN;

        /// <summary>The names of the members that differ, in alphabetical order: a member one has and the other has not differs.</summary>
        private static List<string> Differences(JsonObject a, JsonObject b, double epsilon)
        {
            var names = new SortedSet<string>(StringComparer.Ordinal);

            foreach (KeyValuePair<string, object> member in a.Members)
            {
                names.Add(member.Key);
            }

            foreach (KeyValuePair<string, object> member in b.Members)
            {
                names.Add(member.Key);
            }

            names.Remove("handle");
            var differ = new List<string>();

            foreach (string name in names)
            {
                if (!Same(a.Get(name), b.Get(name), epsilon))
                {
                    differ.Add(name);
                }
            }

            return differ;
        }

        private static bool Same(object a, object b, double epsilon)
        {
            if (a == null || b == null)
            {
                return a == null && b == null;
            }

            if ((a is long || a is double) && (b is long || b is double))
            {
                return Math.Abs(Number(a) - Number(b)) <= epsilon;
            }

            if (a is JsonArray listA && b is JsonArray listB)
            {
                if (listA.Count != listB.Count)
                {
                    return false;
                }

                for (int i = 0; i < listA.Count; i++)
                {
                    if (!Same(listA[i], listB[i], epsilon))
                    {
                        return false;
                    }
                }

                return true;
            }

            if (a is JsonObject objectA && b is JsonObject objectB)
            {
                return Differences(objectA, objectB, epsilon).Count == 0;
            }

            return a.Equals(b);
        }
    }
}
