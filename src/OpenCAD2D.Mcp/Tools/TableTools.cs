using System.Collections.Generic;
using System.ComponentModel;
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
            "The layouts of the drawing in the order of their tabs, Model first: the sheet's paper size and units, how the plot is turned, how many entities each has, and its viewports with where they stand on the sheet, the part of model space they show (view centre and height) and their scale (1:10)."
        )]
        public IReadOnlyList<LayoutInfo> ListLayouts() =>
            _session.Run("list_layouts", false, drawing => Q(drawing).LayoutInfos());

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
        public IReadOnlyList<LinetypeInfo> ListLinetypes() =>
            _session.Run("list_linetypes", false, drawing => Q(drawing).LinetypeInfos());

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
