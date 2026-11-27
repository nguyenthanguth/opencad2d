using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Linq;
using GeometryHelper.Dxf;
using ModelContextProtocol.Server;

namespace OpenCAD2D.Mcp
{
    internal sealed partial class DrawingTools
    {
        [McpServerTool(
            Name = "list_layouts",
            ReadOnly = true,
            Destructive = false,
            Idempotent = true,
            OpenWorld = false
        )]
        [Description(
            "The layouts of the drawing in the order of their tabs, Model first: the sheet's paper size and units, how the plot is turned, how many entities each has, and its viewports with where they stand on the sheet, the part of model space they show (view centre and height) and their scale (1:10). In a server attached to an app this reads a copy of the app's drawing."
        )]
        public IReadOnlyList<LayoutInfo> ListLayouts() => _session.Run("list_layouts", false, drawing =>
            {
                CadDocument document = Q(drawing).ReadDocument();
                string current = drawing.CurrentLayout;
                return (IReadOnlyList<LayoutInfo>)
                    document
                        .Spaces.Where(s => !s.IsBlockEditor)
                        .Select(s => LayoutOf(s, current))
                        .OrderBy(l => l.TabOrder)
                        .ThenBy(l => l.Name, StringComparer.OrdinalIgnoreCase)
                        .ThenBy(l => l.Name, StringComparer.Ordinal)
                        .ToList();
            });

        private static LayoutInfo LayoutOf(CadSpace space, string current)
        {
            var info = new LayoutInfo
            {
                Name = space.Name,
                Current = string.Equals(space.Name, current, StringComparison.OrdinalIgnoreCase),
                Entities = space.Block.Count(e => !e.IsErased && !(e is DxfViewport v && v.Number == 1)),
                Viewports = new List<ViewportInfo>(),
            };

            if (space.IsModel || space.Layout == null)
            {
                return info;
            }

            DxfLayout layout = space.Layout;
            info.TabOrder = layout.TabOrder;
            info.PaperSize = new[]
            {
                EntityDescriber.R(layout.PlotPaperSize.X),
                EntityDescriber.R(layout.PlotPaperSize.Y),
            };
            info.PaperUnits =
                layout.PlotPaperUnits == DxfPlotPaperUnit.Millimeters ? "millimetres"
                : layout.PlotPaperUnits == DxfPlotPaperUnit.Inches ? "inches"
                : "pixels";
            info.Paper = layout.CanonicalMediaName;
            info.Rotation = (int)layout.PlotRotation * 90;
            info.Viewports = space
                .Block.OfType<DxfViewport>()
                .Where(v => !v.IsErased && v.Number != 1)
                .OrderBy(v => v.Number)
                .ThenBy(v => v.Handle, StringComparer.Ordinal)
                .Select(ViewportOf)
                .ToList();
            return info;
        }

        private static ViewportInfo ViewportOf(DxfViewport v)
        {
            double scale = v.CustomScale;
            return new ViewportInfo
            {
                Handle = v.Handle,
                Number = v.Number,
                On = v.On,
                Locked = v.Locked,
                Center = new[] { EntityDescriber.R(v.CenterPoint.X), EntityDescriber.R(v.CenterPoint.Y) },
                Width = EntityDescriber.R(v.Width),
                Height = EntityDescriber.R(v.Height),
                ViewCenter = new[] { EntityDescriber.R(v.ViewCenter.X), EntityDescriber.R(v.ViewCenter.Y) },
                ViewHeight = EntityDescriber.R(v.ViewHeight),
                CustomScale = Math.Round(scale, 8),
                Scale = CadViewports.ScaleName(scale) is string name && name.Length > 0 ? name : null,
                FrozenLayers = v.GetFrozenLayers()
                    .Select(l => l.Name)
                    .OrderBy(n => n, StringComparer.OrdinalIgnoreCase)
                    .ThenBy(n => n, StringComparer.Ordinal)
                    .ToList(),
            };
        }

        [McpServerTool(
            Name = "list_linetypes",
            ReadOnly = true,
            Destructive = false,
            Idempotent = true,
            OpenWorld = false
        )]
        [Description(
            "The linetypes of the drawing, by name, with their description, the length of one repeat of the pattern and its dashes (a positive number is a dash, a negative a gap, 0 a dot), in millimetres. add_layer, add_* and set_properties take these names."
        )]
        public IReadOnlyList<LinetypeInfo> ListLinetypes() => _session.Run("list_linetypes", false, drawing =>
            {
                DxfDatabase database = Q(drawing).ReadDocument().Database;
                return (IReadOnlyList<LinetypeInfo>)
                    database
                        .LinetypeTable.OrderBy(l => l.Name, StringComparer.OrdinalIgnoreCase)
                        .ThenBy(l => l.Name, StringComparer.Ordinal)
                        .Select(l => new LinetypeInfo
                        {
                            Name = l.Name,
                            Description = l.AsciiDescription,
                            PatternLength = EntityDescriber.R(l.PatternLength),
                            Dashes = Enumerable
                                .Range(0, l.NumDashes)
                                .Select(i => EntityDescriber.R(l.DashLengthAt(i)))
                                .ToList(),
                        })
                        .ToList();
            });

        [McpServerTool(
            Name = "list_multileader_styles",
            ReadOnly = true,
            Destructive = false,
            Idempotent = true,
            OpenWorld = false
        )]
        [Description(
            "The multileader styles of the drawing, by name: the leader type, arrowhead and size, the landing, the text style and height, the overall scale, whether it is annotative, and what the leader holds (text or a block)."
        )]
        public IReadOnlyList<MLeaderStyleInfo> ListMultileaderStyles() =>
            _session.Run("list_multileader_styles", false, drawing => Q(drawing).MLeaderStyles());

        [McpServerTool(
            Name = "list_table_styles",
            ReadOnly = true,
            Destructive = false,
            Idempotent = true,
            OpenWorld = false
        )]
        [Description(
            "The table styles of the drawing, by name: the cell margins, whether the title and header rows are shown, and for the title, header and data rows the text style, text height and alignment."
        )]
        public IReadOnlyList<TableStyleInfo> ListTableStyles() =>
            _session.Run("list_table_styles", false, drawing => Q(drawing).TableStyles());
    }
}
