using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Linq;
using GeometryHelper;
using GeometryHelper.Dxf;
using GeometryHelper.Geometry;
using ModelContextProtocol;
using ModelContextProtocol.Server;

namespace OpenCAD2D.Mcp
{
    /// <summary>
    /// The tools an agent calls to open a drawing, read it, check what is drawn and save it. Every tool works on the one
    /// drawing of the session, takes millimetres and degrees, and gives a small structured answer: a long list is paged.
    /// </summary>
    internal sealed partial class DrawingTools
    {
        /// <summary>The rows of a page when the caller gives none.</summary>
        public const int DefaultPageSize = 100;

        /// <summary>The most rows of a page: a page is read by a model, so it stays small.</summary>
        public const int MostPageSize = 500;

        /// <summary>The tolerance of the box tests, in millimetres: the library's own.</summary>
        private static readonly Tolerance Tol = new Tolerance(0.001, 1e-5);

        private readonly McpSession _session;

        /// <summary>Makes the tools of a session.</summary>
        public DrawingTools(McpSession session)
        {
            _session = session ?? throw new ArgumentNullException(nameof(session));
        }

        [McpServerTool(
            Name = "open_drawing",
            ReadOnly = false,
            Destructive = false,
            Idempotent = true,
            OpenWorld = false
        )]
        [Description(
            "Opens a DXF or DWG file and makes it the drawing every other tool works on (in the app, for a server attached to a running OpenCAD2D). Refused while the open drawing has unsaved changes unless discard is true."
        )]
        public DrawingResult OpenDrawing(
            [Description("The path of a .dxf or .dwg file.")] string path,
            [Description("True to throw away unsaved changes of the drawing open now.")] bool discard = false
        ) =>
            _session.RunWithout(() =>
            {
                _session.Open(path, discard);
                return Current();
            });

        [McpServerTool(
            Name = "new_drawing",
            ReadOnly = false,
            Destructive = false,
            Idempotent = false,
            OpenWorld = false
        )]
        [Description(
            "Makes a blank drawing (AutoCAD 2013 format, layer 0 only, no file yet) and makes it the drawing every other tool works on. Give save a path to write it."
        )]
        public DrawingResult NewDrawing(
            [Description("True to throw away unsaved changes of the drawing open now.")] bool discard = false
        ) =>
            _session.RunWithout(() =>
            {
                _session.New(discard);
                return Current();
            });

        private DrawingResult Current() => Describe(_session.Backend);

        private static DrawingResult Describe(IDrawingBackend drawing) =>
            new DrawingResult
            {
                Mode = drawing.Mode,
                Name = drawing.Name,
                Path = drawing.Path,
                Modified = drawing.IsModified,
            };

        [McpServerTool(Name = "save", ReadOnly = false, Destructive = true, Idempotent = true, OpenWorld = false)]
        [Description(
            "Saves the drawing to its own file, or to the path given (a name ending in .dwg is a DWG, any other a DXF), which it is known by from then on. This is the only tool that writes a file."
        )]
        public DrawingResult Save(
            [Description("Where to save it; leave out to save to the drawing's own file.")] string path = null
        ) => _session.Run("save", true, drawing =>
            {
                drawing.Save(path);
                return Describe(drawing);
            });

        [McpServerTool(Name = "info", ReadOnly = true, Destructive = false, Idempotent = true, OpenWorld = false)]
        [Description(
            "What the drawing is: its name and file, units, layouts, the box everything of a layout is drawn in, and how many entities of each kind it has. Call it first."
        )]
        public InfoResult Info(
            [Description("The layout the extents and counts are of; model space where left out.")] string layout = null
        ) => _session.Run("info", false, drawing =>
            {
                IReadOnlyList<string> layouts = drawing.Layouts;
                string space = Layout(drawing, layout);
                IReadOnlyList<EntityRef> entities = drawing.Entities(space);
                var counts = new SortedDictionary<string, int>(StringComparer.Ordinal);

                foreach (EntityRef item in entities)
                {
                    string kind = item.Entity.DxfName;
                    counts[kind] = counts.TryGetValue(kind, out int n) ? n + 1 : 1;
                }

                return new InfoResult
                {
                    Mode = drawing.Mode,
                    Name = drawing.Name,
                    Path = drawing.Path,
                    Modified = drawing.IsModified,
                    ReadOnly = _session.Options.ReadOnly,
                    ScriptsAllowed = _session.Options.AllowScripts,
                    Units = UnitsName(drawing.Variable("INSUNITS")),
                    CurrentLayout = drawing.CurrentLayout,
                    CurrentLayer = drawing.CurrentLayer,
                    CurrentDimensionStyle = drawing.CurrentDimensionStyle,
                    Layouts = layouts,
                    Layout = space,
                    Extents = Extents(drawing.Bounds(space)),
                    Counts = counts,
                    EntityCount = entities.Count,
                };
            });

        [McpServerTool(
            Name = "list_layers",
            ReadOnly = true,
            Destructive = false,
            Idempotent = true,
            OpenWorld = false
        )]
        [Description(
            "The layers of the drawing, by name: colour index, linetype, lineweight and the on, frozen, locked and plot switches."
        )]
        public IReadOnlyList<LayerInfo> ListLayers() =>
            _session.Run("list_layers", false, drawing => (IReadOnlyList<LayerInfo>)
                    drawing
                        .Layers()
                        .OrderBy(l => l.Name, StringComparer.OrdinalIgnoreCase)
                        .ThenBy(l => l.Name, StringComparer.Ordinal)
                        .Select(l => new LayerInfo
                        {
                            Name = l.Name,
                            Color = l.Color,
                            Linetype = l.Linetype,
                            LineWeight = l.LineWeight,
                            On = l.IsOn,
                            Frozen = l.IsFrozen,
                            Locked = l.IsLocked,
                            Plottable = l.IsPlottable,
                            Description = l.Description,
                        })
                        .ToList());

        [McpServerTool(
            Name = "list_text_styles",
            ReadOnly = true,
            Destructive = false,
            Idempotent = true,
            OpenWorld = false
        )]
        [Description(
            "The text styles of the drawing, by name: the font, the fixed height (0 where each text sets its own) and the width factor. add_text and add_mtext take one of these names."
        )]
        public IReadOnlyList<TextStyleInfo> ListTextStyles() => _session.Run("list_text_styles", false, drawing =>
            {
                string current = Convert.ToString(drawing.Variable("TEXTSTYLE"));
                return (IReadOnlyList<TextStyleInfo>)
                    drawing
                        .TextStyles()
                        .OrderBy(s => s.Name, StringComparer.OrdinalIgnoreCase)
                        .ThenBy(s => s.Name, StringComparer.Ordinal)
                        .Select(s => new TextStyleInfo
                        {
                            Name = s.Name,
                            Font = string.IsNullOrEmpty(s.Font.TypeFace) ? s.FileName : s.Font.TypeFace,
                            Height = EntityDescriber.R(s.TextSize),
                            WidthFactor = EntityDescriber.R(s.XScale),
                            Current = string.Equals(s.Name, current, StringComparison.OrdinalIgnoreCase),
                        })
                        .ToList();
            });

        [McpServerTool(
            Name = "list_dimension_styles",
            ReadOnly = true,
            Destructive = false,
            Idempotent = true,
            OpenWorld = false
        )]
        [Description(
            "The dimension styles of the drawing, by name: overall scale, text height, arrow size, decimals. The add_*_dim tools take one of these names, or the current one."
        )]
        public IReadOnlyList<DimensionStyleInfo> ListDimensionStyles() =>
            _session.Run("list_dimension_styles", false, drawing =>
            {
                string current = drawing.CurrentDimensionStyle;
                return (IReadOnlyList<DimensionStyleInfo>)
                    drawing
                        .DimensionStyles()
                        .OrderBy(s => s.Name, StringComparer.OrdinalIgnoreCase)
                        .ThenBy(s => s.Name, StringComparer.Ordinal)
                        .Select(s => new DimensionStyleInfo
                        {
                            Name = s.Name,
                            Scale = EntityDescriber.R(s.Dimscale),
                            TextHeight = EntityDescriber.R(s.Dimtxt),
                            ArrowSize = EntityDescriber.R(s.Dimasz),
                            Decimals = s.Dimdec,
                            TextStyle = s.Dimtxsty?.Name,
                            Current = string.Equals(s.Name, current, StringComparison.OrdinalIgnoreCase),
                        })
                        .ToList();
            });

        [McpServerTool(
            Name = "list_blocks",
            ReadOnly = true,
            Destructive = false,
            Idempotent = true,
            OpenWorld = false
        )]
        [Description(
            "The blocks of the drawing that add_insert can place, by name, with the number of entities in each (anonymous blocks, layouts and dimension pictures are left out)."
        )]
        public IReadOnlyList<BlockInfo> ListBlocks() =>
            _session.Run("list_blocks", false, drawing =>
                (IReadOnlyList<BlockInfo>)
                    drawing
                        .Blocks()
                        .OrderBy(b => b.Name, StringComparer.OrdinalIgnoreCase)
                        .ThenBy(b => b.Name, StringComparer.Ordinal)
                        .Select(b => new BlockInfo { Name = b.Name, Entities = b.Count(e => !e.IsErased) })
                        .ToList()
            );

        [McpServerTool(
            Name = "query_entities",
            ReadOnly = true,
            Destructive = false,
            Idempotent = true,
            OpenWorld = false
        )]
        [Description(
            "Lists the entities of a layout in the order the drawing draws them: the handle, the kind (LINE, CIRCLE, ARC, LWPOLYLINE, TEXT, MTEXT, DIMENSION, INSERT, HATCH ...), the layer, the key geometry and the box it takes as drawn. Every filter left out lets everything through. Paged: a page is 100 entities unless page_size says up to 500."
        )]
        public EntitiesResult QueryEntities(
            [Description("DXF kinds to keep, as [\"LINE\", \"TEXT\"], either case.")] string[] kinds = null,
            [Description("Layer names to keep, either case.")] string[] layers = null,
            [Description("Keep what is drawn inside or across this box, [minX, minY, maxX, maxY] in millimetres.")]
                double[] box = null,
            [Description("The layout; model space where left out.")] string layout = null,
            [Description("The page, numbered from 1.")] int page = 1,
            [Description("The rows of a page, 1 to 500.")] int page_size = DefaultPageSize
        ) => _session.Run("query_entities", false, drawing =>
            {
                string space = Layout(drawing, layout);
                var wantedKinds = Set(kinds);
                var wantedLayers = Set(layers);
                DisplayBox? within = Box("box", box);
                HashSet<string> inside = null;

                if (within.HasValue)
                {
                    DisplayBox area = within.Value.Inflate(Tol.EqualPoint);
                    inside = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
                    BoxRows rows = drawing.Bounds(space);

                    for (int i = 0; i < rows.Handles.Length; i++)
                    {
                        if (area.Intersects(Row(rows.Boxes, i)))
                        {
                            inside.Add(rows.Handles[i]);
                        }
                    }
                }

                List<EntityRef> kept = drawing
                    .Entities(space)
                    .Where(e => wantedKinds == null || wantedKinds.Contains(e.Entity.DxfName))
                    .Where(e => wantedLayers == null || wantedLayers.Contains(e.Entity.Layer))
                    .Where(e => inside == null || inside.Contains(e.Handle))
                    .ToList();
                var result = new EntitiesResult();
                List<EntityRef> shown = Page(kept, page, page_size, result);
                var infos = shown.Select(EntityDescriber.Describe).ToList();

                if (infos.Count > 0)
                {
                    BoxRows boxes = drawing.BoundsOf(infos.Select(i => i.Handle).ToList());
                    var byHandle = new Dictionary<string, double[]>(StringComparer.OrdinalIgnoreCase);

                    for (int i = 0; i < boxes.Handles.Length; i++)
                    {
                        byHandle[boxes.Handles[i]] = Corners(boxes.Boxes, i);
                    }

                    foreach (EntityInfo info in infos)
                    {
                        info.Box = byHandle.TryGetValue(info.Handle, out double[] corners) ? corners : null;
                    }
                }

                result.Entities = infos;
                return result;
            });

        [McpServerTool(Name = "bounds", ReadOnly = true, Destructive = false, Idempotent = true, OpenWorld = false)]
        [Description(
            "The box each entity takes as the app draws it (the letters of a text and not its insertion point, a dimension's picture, a block however deep), as [minX, minY, maxX, maxY]. For the handles given, or for every entity of a layout. An entity that draws nothing has no row. Paged."
        )]
        public BoundsResult Bounds(
            [Description("Handles to give the boxes of; leave out for every entity of the layout.")]
                string[] handles = null,
            [Description("The layout when no handles are given; model space where left out.")] string layout = null,
            [Description("The page, numbered from 1.")] int page = 1,
            [Description("The rows of a page, 1 to 500.")] int page_size = DefaultPageSize
        ) => _session.Run("bounds", false, drawing =>
            {
                BoxRows rows =
                    handles != null && handles.Length > 0
                        ? drawing.BoundsOf(Handles(handles))
                        : drawing.Bounds(Layout(drawing, layout));
                var all = Enumerable.Range(0, rows.Handles.Length).ToList();
                var result = new BoundsResult();
                result.Rows = Page(all, page, page_size, result)
                    .Select(i => new BoundsRow { Handle = rows.Handles[i], Box = Corners(rows.Boxes, i) })
                    .ToList();
                return result;
            });

        [McpServerTool(Name = "overlaps", ReadOnly = true, Destructive = false, Idempotent = true, OpenWorld = false)]
        [Description(
            "Finds the words (TEXT, MTEXT, dimension text, multileader text) that overlap what is drawn near them: a line, arc, polyline or hatch edge running through the word's frame, a filled area it lies in, or another word. Each row is the entity of the word, the entity it overlaps and the box of the places they meet. Touching within the tolerance is no overlap. Use it to check a layout before saving. Paged."
        )]
        public OverlapsResult Overlaps(
            [Description(
                "The kinds whose words are looked at, as [\"TEXT\", \"MTEXT\"]; TEXT, MTEXT, DIMENSION and MULTILEADER where left out."
            )]
                string[] kinds = null,
            [Description("The kinds the words are looked at against; every kind where left out.")] string[] over = null,
            [Description("Only pairs in which one of these handles stands; every pair where left out.")]
                string[] of = null,
            [Description("The layout; model space where left out.")] string layout = null,
            [Description("How near an edge still counts as touching it, in millimetres.")] double tolerance = 0.001,
            [Description("The page, numbered from 1.")] int page = 1,
            [Description("The rows of a page, 1 to 500.")] int page_size = DefaultPageSize
        ) => _session.Run("overlaps", false, drawing =>
            {
                if (!(tolerance > 0.0) || double.IsInfinity(tolerance))
                {
                    throw new ArgumentException("The tolerance is a length above nought.");
                }

                OverlapRows rows = drawing.Overlaps(
                    Layout(drawing, layout),
                    kinds == null ? null : Upper(kinds),
                    over == null ? null : Upper(over),
                    of == null ? null : Handles(of),
                    tolerance
                );
                var all = Enumerable.Range(0, rows.Words.Length).ToList();
                var result = new OverlapsResult();
                result.Rows = Page(all, page, page_size, result)
                    .Select(i => new OverlapRow
                    {
                        Word = rows.Words[i],
                        Other = rows.Others[i],
                        Box = Corners(rows.Boxes, i),
                    })
                    .ToList();
                return result;
            });

        /// <summary>The layout name the tools use: the one given, else model space.</summary>
        private static string Layout(IDrawingBackend drawing, string layout)
        {
            if (string.IsNullOrWhiteSpace(layout) || string.Equals(layout, "Model", StringComparison.OrdinalIgnoreCase))
            {
                return "Model";
            }

            string found = drawing.Layouts.FirstOrDefault(l =>
                string.Equals(l, layout, StringComparison.OrdinalIgnoreCase)
            );
            return found
                ?? throw new ArgumentException(
                    "The drawing has no layout named "
                        + layout
                        + ": its layouts are "
                        + string.Join(", ", drawing.Layouts)
                        + "."
                );
        }

        private static HashSet<string> Set(string[] names) =>
            names == null || names.Length == 0
                ? null
                : new HashSet<string>(names.Select(n => n.Trim()), StringComparer.OrdinalIgnoreCase);

        private static List<string> Upper(string[] kinds) => kinds.Select(k => k.Trim().ToUpperInvariant()).ToList();

        private static List<string> Handles(string[] handles)
        {
            if (handles == null || handles.Length == 0)
            {
                throw new ArgumentException("Give at least one handle.");
            }

            var list = new List<string>();

            foreach (string handle in handles)
            {
                string trimmed = handle?.Trim();

                if (string.IsNullOrEmpty(trimmed))
                {
                    throw new ArgumentException("A handle is not empty.");
                }

                if (!list.Contains(trimmed, StringComparer.OrdinalIgnoreCase))
                {
                    list.Add(trimmed);
                }
            }

            return list;
        }

        private static DisplayBox? Box(string name, double[] box)
        {
            if (box == null)
            {
                return null;
            }

            if (box.Length != 4 || box.Any(v => double.IsNaN(v) || double.IsInfinity(v)))
            {
                throw new ArgumentException(name + " is [minX, minY, maxX, maxY], four numbers.");
            }

            return new DisplayBox(
                Math.Min(box[0], box[2]),
                Math.Min(box[1], box[3]),
                Math.Max(box[0], box[2]),
                Math.Max(box[1], box[3])
            );
        }

        private static DisplayBox Row(double[] boxes, int row) =>
            new DisplayBox(boxes[4 * row], boxes[4 * row + 1], boxes[4 * row + 2], boxes[4 * row + 3]);

        private static double[] Corners(double[] boxes, int row) =>
            new[]
            {
                EntityDescriber.R(boxes[4 * row]),
                EntityDescriber.R(boxes[4 * row + 1]),
                EntityDescriber.R(boxes[4 * row + 2]),
                EntityDescriber.R(boxes[4 * row + 3]),
            };

        private static double[] Extents(BoxRows rows)
        {
            if (rows.Handles.Length == 0)
            {
                return null;
            }

            DisplayBox all = Row(rows.Boxes, 0);

            for (int i = 1; i < rows.Handles.Length; i++)
            {
                all = all.Union(Row(rows.Boxes, i));
            }

            return new[]
            {
                EntityDescriber.R(all.MinX),
                EntityDescriber.R(all.MinY),
                EntityDescriber.R(all.MaxX),
                EntityDescriber.R(all.MaxY),
            };
        }

        /// <summary>Takes a page of a list and fills the numbers of the page into the answer.</summary>
        private static List<T> Page<T>(IReadOnlyList<T> all, int page, int pageSize, PagedResult result)
        {
            if (page < 1)
            {
                throw new ArgumentException("The page is numbered from 1.");
            }

            if (pageSize < 1 || pageSize > MostPageSize)
            {
                throw new ArgumentException("page_size is from 1 to " + MostPageSize + ".");
            }

            result.Total = all.Count;
            result.Page = page;
            result.PageSize = pageSize;
            result.Pages = (all.Count + pageSize - 1) / pageSize;
            return all.Skip((page - 1) * pageSize).Take(pageSize).ToList();
        }

        private static string UnitsName(object insunits)
        {
            switch (Convert.ToInt32(insunits))
            {
                case 0:
                    return "unitless";
                case 1:
                    return "inches";
                case 2:
                    return "feet";
                case 4:
                    return "millimetres";
                case 5:
                    return "centimetres";
                case 6:
                    return "metres";
                case 14:
                    return "decimetres";
                default:
                    return "INSUNITS " + Convert.ToInt32(insunits);
            }
        }
    }
}
