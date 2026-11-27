using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Globalization;
using System.Linq;
using GeometryHelper.Dxf;
using GeometryHelper.Geometry;
using ModelContextProtocol.Server;

namespace OpenCAD2D.Mcp
{
    internal sealed partial class DrawingTools
    {
        /// <summary>The group keys <c>count_entities</c> and <c>aggregate</c> take.</summary>
        private static readonly string[] GroupKeys = { "kind", "layer", "block", "color" };

        private static IQueryBackend Q(IDrawingBackend drawing) => (IQueryBackend)drawing;

        /// <summary>The colour index of each layer by name, for the entities that are by layer.</summary>
        private static Dictionary<string, int> LayerColors(IDrawingBackend drawing)
        {
            var colors = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);

            foreach (LayerRow layer in drawing.Layers())
            {
                colors[layer.Name] = layer.Color;
            }

            return colors;
        }

        /// <summary>The entities of a layout that pass a filter, in the order the layout draws them.</summary>
        private static List<EntityRef> Filtered(IDrawingBackend drawing, string space, EntityFilter filter)
        {
            Dictionary<string, int> colors = filter.NeedsLayerColors ? LayerColors(drawing) : null;
            HashSet<string> inside = filter.HasBox ? filter.HandlesInBox(drawing.Bounds(space)) : null;
            return drawing
                .Entities(space)
                .Where(e => filter.Matches(e, colors) && (inside == null || inside.Contains(e.Handle)))
                .ToList();
        }

        private static string Handle(string handle)
        {
            string trimmed = handle?.Trim();
            return string.IsNullOrEmpty(trimmed) ? throw new ArgumentException("Give a handle.") : trimmed;
        }

        private static EntityRef Found(IDrawingBackend drawing, string handle)
        {
            string wanted = Handle(handle);
            return Q(drawing).Find(wanted)
                ?? throw new ArgumentException(
                    "The drawing " + drawing.Name + " has no object " + wanted + ": query_entities lists the handles."
                );
        }

        /// <summary>The boxes of some handles by handle, each <c>[minX, minY, maxX, maxY]</c>.</summary>
        private static Dictionary<string, double[]> DrawnBoxes(IDrawingBackend drawing, IEnumerable<string> handles)
        {
            var byHandle = new Dictionary<string, double[]>(StringComparer.OrdinalIgnoreCase);
            List<string> list = handles.ToList();

            if (list.Count > 0)
            {
                BoxRows boxes = drawing.BoundsOf(list);

                for (int i = 0; i < boxes.Handles.Length; i++)
                {
                    byHandle[boxes.Handles[i]] = Corners(boxes.Boxes, i);
                }
            }

            return byHandle;
        }

        private static string GroupKey(string groupBy, EntityRef item, IReadOnlyDictionary<string, int> layerColors)
        {
            switch (groupBy)
            {
                case "kind":
                    return item.Entity.DxfName;
                case "layer":
                    return item.Entity.Layer;
                case "block":
                    return item.Entity is DxfBlockReference insert ? insert.Name : null;
                case "color":
                    return EntityFilter.ColorKey(item.Entity, layerColors);
                default:
                    return "all";
            }
        }

        /// <summary>Orders group keys: whole numbers (colour indexes) by value, the rest by text without regard to case, then by text.</summary>
        private static int KeyOrder(string a, string b)
        {
            bool aNumber = int.TryParse(a, NumberStyles.Integer, CultureInfo.InvariantCulture, out int x);
            bool bNumber = int.TryParse(b, NumberStyles.Integer, CultureInfo.InvariantCulture, out int y);

            if (aNumber && bNumber)
            {
                return x.CompareTo(y);
            }

            if (aNumber != bNumber)
            {
                return aNumber ? -1 : 1;
            }

            int byCase = string.Compare(a, b, StringComparison.OrdinalIgnoreCase);
            return byCase != 0 ? byCase : string.CompareOrdinal(a, b);
        }

        private static string CheckGroup(string groupBy, bool allowNone)
        {
            string key = string.IsNullOrWhiteSpace(groupBy)
                ? (allowNone ? "none" : "kind")
                : groupBy.Trim().ToLowerInvariant();

            if (!GroupKeys.Contains(key) && !(allowNone && key == "none"))
            {
                throw new ArgumentException(
                    "group_by is " + string.Join(", ", GroupKeys) + (allowNone ? " or none" : string.Empty) + "."
                );
            }

            return key;
        }

        [McpServerTool(Name = "get_entity", ReadOnly = true, Destructive = false, Idempotent = true, OpenWorld = false)]
        [Description(
            "One entity in full: its layer, colour, linetype, lineweight, the box it takes as drawn, the values of its kind (the text and height of a TEXT, the vertices and bulges of a polyline, the measurement and text of a dimension, the block and attributes of an INSERT, the pattern of a hatch, the width and columns of an MTEXT) and what the Properties palette shows for it, row by row. Take a handle from query_entities, select or get_texts."
        )]
        public EntityDetail GetEntity([Description("The handle, such as 2F.")] string handle) =>
            _session.Run("get_entity", false, drawing =>
            {
                EntityRef item = Found(drawing, handle);
                DxfEntity entity = item.Entity;
                var detail = new EntityDetail
                {
                    Handle = item.Handle,
                    Kind = entity.DxfName,
                    Layer = entity.Layer,
                    Color = EntityFilter.ColorAsSet(entity),
                    Linetype = entity.Linetype ?? "ByLayer",
                    LineWeight = (int)entity.LineWeight,
                    LinetypeScale = EntityDescriber.R(entity.LinetypeScale),
                    Details = EntityDetails.Of(entity),
                };
                BoxRows box = drawing.BoundsOf(new List<string> { item.Handle });
                detail.Box = box.Handles.Length > 0 ? Corners(box.Boxes, 0) : null;
                detail.Properties = Q(drawing).Properties(new List<string> { item.Handle });
                return detail;
            });

        [McpServerTool(
            Name = "count_entities",
            ReadOnly = true,
            Destructive = false,
            Idempotent = true,
            OpenWorld = false
        )]
        [Description(
            "Counts the entities of a layout, in groups: by kind (LINE, TEXT ...), by layer, by block (the block references, by block name) or by colour (the colour each is drawn in: an index, BYBLOCK, or red,green,blue). The filters keep only what they name; every one left out lets everything through. Groups come in order of key; paged."
        )]
        public CountResult CountEntities(
            [Description("What to group by: kind, layer, block or color. Kind where left out.")] string group_by = null,
            [Description("DXF kinds to keep, as [\"LINE\", \"TEXT\"], either case.")] string[] kinds = null,
            [Description("Layer names to keep, either case.")] string[] layers = null,
            [Description("Colour indexes to keep (the colour each entity is drawn in; 256 is not a drawn colour).")]
                int[] colors = null,
            [Description("Linetype names to keep, such as DASHED or ByLayer.")] string[] linetypes = null,
            [Description("Keep what is drawn inside or across this box, [minX, minY, maxX, maxY] in millimetres.")]
                double[] box = null,
            [Description("The layout; model space where left out.")] string layout = null,
            [Description("The page, numbered from 1.")] int page = 1,
            [Description("The rows of a page, 1 to 500.")] int page_size = DefaultPageSize
        ) => _session.Run("count_entities", false, drawing =>
            {
                string by = CheckGroup(group_by, false);
                EntityFilter filter = EntityFilter.Of(kinds, layers, colors, linetypes, box);
                List<EntityRef> kept = Filtered(drawing, Layout(drawing, layout), filter);
                Dictionary<string, int> layerColors = by == "color" ? LayerColors(drawing) : null;
                var counts = new Dictionary<string, int>(StringComparer.Ordinal);
                int total = 0;

                foreach (EntityRef item in kept)
                {
                    string key = GroupKey(by, item, layerColors);

                    if (key == null)
                    {
                        continue;
                    }

                    counts[key] = counts.TryGetValue(key, out int n) ? n + 1 : 1;
                    total++;
                }

                List<string> keys = counts.Keys.ToList();
                keys.Sort(KeyOrder);
                var result = new CountResult { GroupBy = by, Entities = total };
                result.Groups = Page(keys, page, page_size, result)
                    .Select(k => new CountRow { Key = k, Count = counts[k] })
                    .ToList();
                return result;
            });

        [McpServerTool(Name = "aggregate", ReadOnly = true, Destructive = false, Idempotent = true, OpenWorld = false)]
        [Description(
            "Adds up the length or the area of the entities of a layout: the sum, smallest, largest and average of each group, and how many entities had no such measure (a text has no length, an open polyline no area). Length is in millimetres (a line, an arc's length, a circle's circumference, a polyline with its bulges, an ellipse, a spline); area is in square millimetres (a circle, a closed polyline, a closed ellipse, a hatch). Group by kind, layer, block or colour, or leave group_by out for one total. The same filters as count_entities."
        )]
        public AggregateResult Aggregate(
            [Description("length or area.")] string measure,
            [Description("kind, layer, block, color or none (one total). None where left out.")] string group_by = null,
            [Description("DXF kinds to keep, as [\"LINE\", \"LWPOLYLINE\"], either case.")] string[] kinds = null,
            [Description("Layer names to keep, either case.")] string[] layers = null,
            [Description("Colour indexes to keep (the colour each entity is drawn in).")] int[] colors = null,
            [Description("Linetype names to keep.")] string[] linetypes = null,
            [Description("Keep what is drawn inside or across this box, [minX, minY, maxX, maxY] in millimetres.")]
                double[] box = null,
            [Description("The layout; model space where left out.")] string layout = null,
            [Description("The page, numbered from 1.")] int page = 1,
            [Description("The rows of a page, 1 to 500.")] int page_size = DefaultPageSize
        ) => _session.Run("aggregate", false, drawing =>
            {
                string what = measure?.Trim().ToLowerInvariant();

                if (what != "length" && what != "area")
                {
                    throw new ArgumentException("measure is length or area.");
                }

                string by = CheckGroup(group_by, true);
                EntityFilter filter = EntityFilter.Of(kinds, layers, colors, linetypes, box);
                List<EntityRef> kept = Filtered(drawing, Layout(drawing, layout), filter);
                Dictionary<string, int> layerColors = by == "color" ? LayerColors(drawing) : null;
                var groups = new Dictionary<string, List<double?>>(StringComparer.Ordinal);
                int total = 0;

                foreach (EntityRef item in kept)
                {
                    string key = by == "none" ? "all" : GroupKey(by, item, layerColors);

                    if (key == null)
                    {
                        continue;
                    }

                    if (!groups.TryGetValue(key, out List<double?> values))
                    {
                        groups[key] = values = new List<double?>();
                    }

                    values.Add(what == "length" ? EntityMeasure.Length(item.Entity) : EntityMeasure.Area(item.Entity));
                    total++;
                }

                List<string> keys = groups.Keys.ToList();
                keys.Sort(KeyOrder);
                var result = new AggregateResult
                {
                    Measure = what,
                    GroupBy = by,
                    Entities = total,
                };
                result.Groups = Page(keys, page, page_size, result)
                    .Select(k =>
                    {
                        List<double> have = groups[k].Where(v => v.HasValue).Select(v => v.Value).ToList();
                        return new AggregateRow
                        {
                            Key = k,
                            Count = have.Count,
                            Skipped = groups[k].Count - have.Count,
                            Sum = EntityDescriber.R(have.Sum()),
                            Min = have.Count == 0 ? 0.0 : EntityDescriber.R(have.Min()),
                            Max = have.Count == 0 ? 0.0 : EntityDescriber.R(have.Max()),
                            Average = have.Count == 0 ? 0.0 : EntityDescriber.R(have.Sum() / have.Count),
                        };
                    })
                    .ToList();
                return result;
            });

        [McpServerTool(Name = "get_texts", ReadOnly = true, Destructive = false, Idempotent = true, OpenWorld = false)]
        [Description(
            "The words of a layout: each TEXT, MTEXT and multileader text with its contents, place, height, rotation and style, the box it takes as drawn, and the values of the attributes of block references (kind ATTRIB, with the handle of the block reference and the tag). In the order the drawing draws them; paged. contains keeps the words that hold a piece of text, either case."
        )]
        public TextsResult GetTexts(
            [Description("Keep the words that contain this text, either case.")] string contains = null,
            [Description("Text style names to keep, either case.")] string[] styles = null,
            [Description("Layer names to keep, either case.")] string[] layers = null,
            [Description("Keep what is drawn inside or across this box, [minX, minY, maxX, maxY] in millimetres.")]
                double[] box = null,
            [Description("The layout; model space where left out.")] string layout = null,
            [Description("The page, numbered from 1.")] int page = 1,
            [Description("The rows of a page, 1 to 500.")] int page_size = DefaultPageSize
        ) => _session.Run("get_texts", false, drawing =>
            {
                EntityFilter filter = EntityFilter.Of(null, layers, null, null, box);
                HashSet<string> wantedStyles = Set(styles);
                var rows = new List<TextRow>();

                foreach (EntityRef item in Filtered(drawing, Layout(drawing, layout), filter))
                {
                    foreach (TextRow row in TextRows(item))
                    {
                        if (
                            (wantedStyles == null || (row.Style != null && wantedStyles.Contains(row.Style)))
                            && (
                                string.IsNullOrEmpty(contains)
                                || (row.Text ?? string.Empty).IndexOf(contains, StringComparison.OrdinalIgnoreCase) >= 0
                            )
                        )
                        {
                            rows.Add(row);
                        }
                    }
                }

                var result = new TextsResult();
                List<TextRow> shown = Page(rows, page, page_size, result);
                Dictionary<string, double[]> boxes = DrawnBoxes(
                    drawing,
                    shown.Where(r => r.Kind != "ATTRIB").Select(r => r.Handle)
                );

                foreach (TextRow row in shown)
                {
                    row.Box =
                        row.Kind != "ATTRIB" && boxes.TryGetValue(row.Handle, out double[] corners) ? corners : null;
                }

                result.Texts = shown;
                return result;
            });

        private static IEnumerable<TextRow> TextRows(EntityRef item)
        {
            DxfEntity entity = item.Entity;

            switch (entity)
            {
                case DxfAttributeDefinition _:
                    yield break;
                case DxfDBText text:
                    yield return new TextRow
                    {
                        Handle = item.Handle,
                        Kind = entity.DxfName,
                        Layer = entity.Layer,
                        Text = text.TextString,
                        At = new[] { EntityDescriber.R(text.Position.X), EntityDescriber.R(text.Position.Y) },
                        Height = EntityDescriber.R(text.Height),
                        Rotation = EntityDescriber.R(text.Rotation * 180.0 / Math.PI),
                        Style = text.TextStyleName,
                    };
                    break;
                case DxfMText mtext:
                    yield return new TextRow
                    {
                        Handle = item.Handle,
                        Kind = entity.DxfName,
                        Layer = entity.Layer,
                        Text = mtext.Text,
                        At = new[] { EntityDescriber.R(mtext.Location.X), EntityDescriber.R(mtext.Location.Y) },
                        Height = EntityDescriber.R(mtext.TextHeight),
                        Rotation = EntityDescriber.R(mtext.Rotation * 180.0 / Math.PI),
                        Style = mtext.TextStyleName,
                    };
                    break;
                case DxfMLeader mleader when mleader.ContentType == DxfContentType.MTextContent:
                    yield return new TextRow
                    {
                        Handle = item.Handle,
                        Kind = entity.DxfName,
                        Layer = entity.Layer,
                        Text = mleader.MText.Text,
                        At = new[]
                        {
                            EntityDescriber.R(mleader.TextLocation.X),
                            EntityDescriber.R(mleader.TextLocation.Y),
                        },
                        Height = EntityDescriber.R(mleader.TextHeight),
                        Rotation = EntityDescriber.R(mleader.MText.Rotation * 180.0 / Math.PI),
                        Style = mleader.MText.TextStyleName,
                    };
                    break;
                case DxfBlockReference insert:
                    foreach (DxfAttributeReference attribute in EntityDetails.Attributes(insert))
                    {
                        yield return new TextRow
                        {
                            Handle = item.Handle,
                            Kind = "ATTRIB",
                            Layer = attribute.Layer,
                            Text = attribute.TextString,
                            At = new[]
                            {
                                EntityDescriber.R(attribute.Position.X),
                                EntityDescriber.R(attribute.Position.Y),
                            },
                            Height = EntityDescriber.R(attribute.Height),
                            Rotation = EntityDescriber.R(attribute.Rotation * 180.0 / Math.PI),
                            Style = attribute.TextStyleName,
                            Tag = attribute.Tag,
                        };
                    }

                    break;
            }
        }

        [McpServerTool(
            Name = "get_dimensions",
            ReadOnly = true,
            Destructive = false,
            Idempotent = true,
            OpenWorld = false
        )]
        [Description(
            "The dimensions of a layout: each with its type, style, the measured value (millimetres, or degrees for an angular one), the words it is drawn with, the text typed over the measurement (<> is the measurement), its defining points, the place of its text and the box it takes as drawn. In the order the drawing draws them; paged."
        )]
        public DimensionsResult GetDimensions(
            [Description(
                "Types to keep, as [\"Rotated\", \"Radial\"] (RotatedDimension, AlignedDimension, RadialDimension, DiametricDimension, LineAngularDimension2, Point3AngularDimension, OrdinateDimension, ArcDimension), either case."
            )]
                string[] types = null,
            [Description("Dimension style names to keep, either case.")] string[] styles = null,
            [Description("Layer names to keep, either case.")] string[] layers = null,
            [Description("Keep what is drawn inside or across this box, [minX, minY, maxX, maxY] in millimetres.")]
                double[] box = null,
            [Description("The layout; model space where left out.")] string layout = null,
            [Description("The page, numbered from 1.")] int page = 1,
            [Description("The rows of a page, 1 to 500.")] int page_size = DefaultPageSize
        ) => _session.Run("get_dimensions", false, drawing =>
            {
                EntityFilter filter = EntityFilter.Of(new[] { "DIMENSION" }, layers, null, null, box);
                HashSet<string> wantedTypes =
                    types == null || types.Length == 0
                        ? null
                        : new HashSet<string>(
                            types.Select(t =>
                                t.Trim().EndsWith("Dimension", StringComparison.OrdinalIgnoreCase)
                                || t.Trim().EndsWith("Dimension2", StringComparison.OrdinalIgnoreCase)
                                    ? t.Trim()
                                    : t.Trim() + "Dimension"
                            ),
                            StringComparer.OrdinalIgnoreCase
                        );
                HashSet<string> wantedStyles = Set(styles);
                List<EntityRef> kept = Filtered(drawing, Layout(drawing, layout), filter)
                    .Where(e => e.Entity is DxfDimension)
                    .Where(e =>
                        wantedTypes == null
                        || wantedTypes.Any(t =>
                            EntityDetails
                                .TypeOf((DxfDimension)e.Entity)
                                .StartsWith(t, StringComparison.OrdinalIgnoreCase)
                        )
                    )
                    .Where(e =>
                        wantedStyles == null
                        || wantedStyles.Contains(((DxfDimension)e.Entity).DimensionStyleName ?? string.Empty)
                    )
                    .ToList();
                var result = new DimensionsResult();
                List<EntityRef> shown = Page(kept, page, page_size, result);
                Dictionary<string, double[]> boxes = DrawnBoxes(drawing, shown.Select(e => e.Handle));
                result.Dimensions = shown
                    .Select(e =>
                    {
                        var dimension = (DxfDimension)e.Entity;
                        bool angular = EntityDetails.IsAngular(dimension);
                        return new DimensionRow
                        {
                            Handle = e.Handle,
                            Layer = dimension.Layer,
                            Type = EntityDetails.TypeOf(dimension),
                            Style = dimension.DimensionStyleName,
                            Measurement = EntityDescriber.R(
                                angular ? dimension.Measurement * 180.0 / Math.PI : dimension.Measurement
                            ),
                            Unit = angular ? "degrees" : "mm",
                            Text = EntityDetails.DrawnText(dimension),
                            TextOverride = dimension.DimensionText ?? string.Empty,
                            Points = new Dictionary<string, double[]>(EntityDetails.DimensionPoints(dimension)),
                            TextPosition = new[]
                            {
                                EntityDescriber.R(dimension.TextPosition.X),
                                EntityDescriber.R(dimension.TextPosition.Y),
                            },
                            Box = boxes.TryGetValue(e.Handle, out double[] corners) ? corners : null,
                        };
                    })
                    .ToList();
                return result;
            });

        private static readonly string[] Modes =
        {
            "window",
            "crossing",
            "fence",
            "polygon",
            "crossing_polygon",
            "all",
            "previous",
        };

        [McpServerTool(Name = "select", ReadOnly = true, Destructive = false, Idempotent = true, OpenWorld = false)]
        [Description(
            "Finds entities by where they are drawn and gives their handles, in the order the drawing draws them. mode: window (wholly inside the box of two corners), crossing (the box of two corners lies across or contains them), fence (a line of two or more points crosses them), polygon (wholly inside a polygon of three or more points), crossing_polygon (a polygon of three or more points touches them), all (the whole layout), previous (what is selected in the drawing now: in a server attached to an app, what the user has selected). Not held to what the window shows. The filters keep only what they name. This never changes what the user has selected. Paged."
        )]
        public SelectResult Select(
            [Description("window, crossing, fence, polygon, crossing_polygon, all or previous.")] string mode,
            [Description(
                "The points in millimetres, [[x, y], ...]: two corners for window and crossing, two or more for a fence, three or more for a polygon; none for all and previous."
            )]
                double[][] points = null,
            [Description("DXF kinds to keep, as [\"LINE\", \"TEXT\"], either case.")] string[] kinds = null,
            [Description("Layer names to keep, either case.")] string[] layers = null,
            [Description("Colour indexes to keep (the colour each entity is drawn in).")] int[] colors = null,
            [Description("Linetype names to keep.")] string[] linetypes = null,
            [Description("The layout; model space where left out.")] string layout = null,
            [Description("The page, numbered from 1.")] int page = 1,
            [Description("The rows of a page, 1 to 500.")] int page_size = DefaultPageSize
        ) => _session.Run("select", false, drawing =>
            {
                string how = mode?.Trim().ToLowerInvariant();

                if (!Modes.Contains(how))
                {
                    throw new ArgumentException("mode is " + string.Join(", ", Modes) + ".");
                }

                string space = Layout(drawing, layout);
                EntityFilter filter = EntityFilter.Of(kinds, layers, colors, linetypes, null);
                IReadOnlyList<string> handles = HandlesOf(drawing, how, points, space);
                List<EntityRef> items = ItemsOf(drawing, handles, space, how);
                Dictionary<string, int> layerColors = filter.NeedsLayerColors ? LayerColors(drawing) : null;
                List<string> kept = items.Where(i => filter.Matches(i, layerColors)).Select(i => i.Handle).ToList();
                var result = new SelectResult { Mode = how };
                result.Handles = Page(kept, page, page_size, result);
                return result;
            });

        /// <summary>The handles a selection mode takes, before the filters.</summary>
        private static IReadOnlyList<string> HandlesOf(
            IDrawingBackend drawing,
            string how,
            double[][] points,
            string space
        )
        {
            IQueryBackend query = Q(drawing);

            if (how == "all")
            {
                return drawing
                    .Entities(space)
                    .Where(e => !(e.Entity is DxfViewport viewport && viewport.Number == 1))
                    .Select(e => e.Handle)
                    .ToList();
            }

            if (how == "previous")
            {
                return query.SelectedHandles();
            }

            List<GeoPoint2> corners = (points ?? Array.Empty<double[]>())
                .Select(p => Point("points", p))
                .Select(p => new GeoPoint2(p.X, p.Y))
                .ToList();

            switch (how)
            {
                case "window":
                case "crossing":
                    if (corners.Count != 2)
                    {
                        throw new ArgumentException(how + " takes two corners: points is [[x1, y1], [x2, y2]].");
                    }

                    return query.HandlesIn(
                        space,
                        how == "window" ? CadAreaSelection.Window : CadAreaSelection.Crossing,
                        corners,
                        0.0
                    );
                case "fence":
                    if (corners.Count < 2)
                    {
                        throw new ArgumentException("A fence takes two points or more.");
                    }

                    return query.HandlesIn(space, CadAreaSelection.Fence, corners, 0.0);
                default:
                    if (corners.Count < 3)
                    {
                        throw new ArgumentException("A polygon takes three points or more.");
                    }

                    return query.HandlesIn(
                        space,
                        how == "polygon" ? CadAreaSelection.WindowPolygon : CadAreaSelection.CrossingPolygon,
                        corners,
                        0.0
                    );
            }
        }

        /// <summary>The entities of some handles, in the order of the handles; one the drawing does not have is left out.</summary>
        private static List<EntityRef> ItemsOf(
            IDrawingBackend drawing,
            IReadOnlyList<string> handles,
            string space,
            string how
        )
        {
            if (how == "all")
            {
                return drawing.Entities(space).Where(e => handles.Contains(e.Handle)).ToList();
            }

            if (handles.Count == 0)
            {
                return new List<EntityRef>();
            }

            if (how == "previous")
            {
                return Q(drawing).FindAll(handles).ToList();
            }

            var wanted = new HashSet<string>(handles, StringComparer.OrdinalIgnoreCase);
            return drawing.Entities(space).Where(e => wanted.Contains(e.Handle)).ToList();
        }

        private static readonly string[] SimilarKeys = { "kind", "layer", "color", "linetype", "text_style", "block" };

        [McpServerTool(
            Name = "select_similar",
            ReadOnly = true,
            Destructive = false,
            Idempotent = true,
            OpenWorld = false
        )]
        [Description(
            "Finds the entities like one entity: the same kind and layer by default, or the properties named in match (kind, layer, color, linetype, text_style, block). Looks in the layout the entity is in, or the layout given. Gives the handles in the order the drawing draws them, the entity itself included; paged."
        )]
        public SelectResult SelectSimilar(
            [Description("The handle of the entity to be like.")] string handle,
            [Description(
                "What must be the same: any of kind, layer, color, linetype, text_style, block. Kind and layer where left out."
            )]
                string[] match = null,
            [Description("The layout to look in; the one the entity is in where left out.")] string layout = null,
            [Description("The page, numbered from 1.")] int page = 1,
            [Description("The rows of a page, 1 to 500.")] int page_size = DefaultPageSize
        ) => _session.Run("select_similar", false, drawing =>
            {
                EntityRef source = Found(drawing, handle);
                List<string> keys =
                    match == null || match.Length == 0
                        ? new List<string> { "kind", "layer" }
                        : match.Select(m => m?.Trim().ToLowerInvariant()).Distinct().ToList();

                foreach (string key in keys)
                {
                    if (!SimilarKeys.Contains(key))
                    {
                        throw new ArgumentException("match is any of " + string.Join(", ", SimilarKeys) + ".");
                    }
                }

                string space = string.IsNullOrWhiteSpace(layout)
                    ? SpaceOf(drawing, source.Handle)
                    : Layout(drawing, layout);
                Dictionary<string, int> colors = keys.Contains("color") ? LayerColors(drawing) : null;
                List<string> found = drawing
                    .Entities(space)
                    .Where(e => keys.All(k => Same(k, source, e, colors)))
                    .Select(e => e.Handle)
                    .ToList();
                var result = new SelectResult { Mode = "similar" };
                result.Handles = Page(found, page, page_size, result);
                return result;
            });

        /// <summary>The layout whose block holds an entity: model space unless a layout does.</summary>
        private static string SpaceOf(IDrawingBackend drawing, string handle)
        {
            foreach (
                string space in drawing
                    .Layouts.Where(l => !string.Equals(l, "Model", StringComparison.OrdinalIgnoreCase))
                    .Prepend("Model")
            )
            {
                if (
                    drawing
                        .Entities(space)
                        .Any(e => string.Equals(e.Handle, handle, StringComparison.OrdinalIgnoreCase))
                )
                {
                    return space;
                }
            }

            return "Model";
        }

        private static bool Same(string key, EntityRef a, EntityRef b, IReadOnlyDictionary<string, int> layerColors)
        {
            DxfEntity x = a.Entity;
            DxfEntity y = b.Entity;

            switch (key)
            {
                case "kind":
                    return x.DxfName == y.DxfName;
                case "layer":
                    return string.Equals(x.Layer, y.Layer, StringComparison.OrdinalIgnoreCase);
                case "color":
                    return EntityFilter.ColorKey(x, layerColors) == EntityFilter.ColorKey(y, layerColors);
                case "linetype":
                    return string.Equals(
                        x.Linetype ?? "ByLayer",
                        y.Linetype ?? "ByLayer",
                        StringComparison.OrdinalIgnoreCase
                    );
                case "text_style":
                    return StyleOf(x) != null
                        && string.Equals(StyleOf(x), StyleOf(y), StringComparison.OrdinalIgnoreCase);
                default:
                    return x is DxfBlockReference p
                        && y is DxfBlockReference q
                        && string.Equals(p.Name, q.Name, StringComparison.OrdinalIgnoreCase);
            }
        }

        private static string StyleOf(DxfEntity entity) =>
            entity is DxfDBText text ? text.TextStyleName
            : entity is DxfMText mtext ? mtext.TextStyleName
            : null;
    }
}
