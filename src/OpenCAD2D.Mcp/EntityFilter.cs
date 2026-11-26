using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using GeometryHelper;
using GeometryHelper.Dxf;

namespace OpenCAD2D.Mcp
{
    /// <summary>
    /// The filters the query tools share: kinds, layers, colours, linetypes and a box. A filter left out lets everything through.
    /// The box is tested against the box each entity takes as it is drawn, as <c>query_entities</c> does.
    /// </summary>
    internal sealed class EntityFilter
    {
        private static readonly Tolerance Tol = new Tolerance(0.001, 1e-5);

        private HashSet<string> _kinds;
        private HashSet<string> _layers;
        private HashSet<int> _colors;
        private HashSet<string> _linetypes;
        private DisplayBox? _box;

        /// <summary>Makes a filter from the arguments of a tool.</summary>
        /// <exception cref="ArgumentException">Thrown when the box is not four numbers.</exception>
        public static EntityFilter Of(string[] kinds, string[] layers, int[] colors, string[] linetypes, double[] box)
        {
            var filter = new EntityFilter
            {
                _kinds = Names(kinds, true),
                _layers = Names(layers, false),
                _colors = colors == null || colors.Length == 0 ? null : new HashSet<int>(colors),
                _linetypes = Names(linetypes, false),
            };

            if (box != null)
            {
                if (box.Length != 4 || box.Any(v => double.IsNaN(v) || double.IsInfinity(v)))
                {
                    throw new ArgumentException("box is [minX, minY, maxX, maxY], four numbers.");
                }

                filter._box = new DisplayBox(
                    Math.Min(box[0], box[2]),
                    Math.Min(box[1], box[3]),
                    Math.Max(box[0], box[2]),
                    Math.Max(box[1], box[3])
                );
            }

            return filter;
        }

        private static HashSet<string> Names(string[] names, bool upper) =>
            names == null || names.Length == 0
                ? null
                : new HashSet<string>(
                    names.Select(n => upper ? n.Trim().ToUpperInvariant() : n.Trim()),
                    StringComparer.OrdinalIgnoreCase
                );

        /// <summary>Gets whether the filter looks at the colour of the entities, so the layers' colours are needed.</summary>
        public bool NeedsLayerColors => _colors != null;

        /// <summary>Gets whether the filter has a box, so the drawn boxes are needed.</summary>
        public bool HasBox => _box.HasValue;

        /// <summary>Whether an entity passes the kind, layer, colour and linetype filters.</summary>
        /// <param name="item">The entity.</param>
        /// <param name="layerColors">The colour index of each layer, by name.</param>
        public bool Matches(EntityRef item, IReadOnlyDictionary<string, int> layerColors)
        {
            DxfEntity entity = item.Entity;

            if (_kinds != null && !_kinds.Contains(entity.DxfName))
            {
                return false;
            }

            if (_layers != null && !_layers.Contains(entity.Layer))
            {
                return false;
            }

            if (_linetypes != null && !_linetypes.Contains(entity.Linetype ?? "ByLayer"))
            {
                return false;
            }

            return _colors == null || (EffectiveColor(entity, layerColors) is int index && _colors.Contains(index));
        }

        /// <summary>The handles whose drawn box meets the filter's box; null where there is no box.</summary>
        public HashSet<string> HandlesInBox(BoxRows rows)
        {
            if (!_box.HasValue)
            {
                return null;
            }

            DisplayBox area = _box.Value.Inflate(Tol.EqualPoint);
            var inside = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

            for (int i = 0; i < rows.Handles.Length; i++)
            {
                var drawn = new DisplayBox(
                    rows.Boxes[4 * i],
                    rows.Boxes[4 * i + 1],
                    rows.Boxes[4 * i + 2],
                    rows.Boxes[4 * i + 3]
                );

                if (area.Intersects(drawn))
                {
                    inside.Add(rows.Handles[i]);
                }
            }

            return inside;
        }

        /// <summary>The colour index an entity is drawn in: its own, or its layer's where it is by layer; null for by block or a true colour.</summary>
        public static int? EffectiveColor(DxfEntity entity, IReadOnlyDictionary<string, int> layerColors)
        {
            if (entity.TrueColor.HasValue)
            {
                return null;
            }

            int index = entity.ColorIndex;

            if (index == 256)
            {
                return layerColors != null && layerColors.TryGetValue(entity.Layer, out int own) ? own : (int?)null;
            }

            return index == 0 ? (int?)null : index;
        }

        /// <summary>The colour as a group key: <c>7</c>, <c>BYBLOCK</c>, or <c>255,128,0</c> for a true colour.</summary>
        public static string ColorKey(DxfEntity entity, IReadOnlyDictionary<string, int> layerColors)
        {
            if (entity.TrueColor.HasValue)
            {
                int rgb = entity.TrueColor.Value;
                return string.Join(
                    ",",
                    ((rgb >> 16) & 255).ToString(CultureInfo.InvariantCulture),
                    ((rgb >> 8) & 255).ToString(CultureInfo.InvariantCulture),
                    (rgb & 255).ToString(CultureInfo.InvariantCulture)
                );
            }

            if (entity.ColorIndex == 0)
            {
                return "BYBLOCK";
            }

            int? index = EffectiveColor(entity, layerColors);
            return index.HasValue ? index.Value.ToString(CultureInfo.InvariantCulture) : "BYLAYER";
        }

        /// <summary>The colour as <c>get_entity</c> writes it: <c>BYLAYER</c>, <c>BYBLOCK</c>, an index or red, green and blue.</summary>
        public static string ColorAsSet(DxfEntity entity)
        {
            if (entity.TrueColor.HasValue)
            {
                int rgb = entity.TrueColor.Value;
                return string.Join(
                    ",",
                    ((rgb >> 16) & 255).ToString(CultureInfo.InvariantCulture),
                    ((rgb >> 8) & 255).ToString(CultureInfo.InvariantCulture),
                    (rgb & 255).ToString(CultureInfo.InvariantCulture)
                );
            }

            int index = entity.ColorIndex;
            return index == 256 ? "BYLAYER"
                : index == 0 ? "BYBLOCK"
                : index.ToString(CultureInfo.InvariantCulture);
        }
    }
}
