using System.ComponentModel;
using System.Linq;
using ModelContextProtocol.Server;

namespace OpenCAD2D.Mcp
{
    /// <summary>The tools that add and change the styles of multileaders and tables, which add_mleader and add_table draw in.</summary>
    internal sealed partial class DrawingTools
    {
        private const string MLeaderStyleText =
            "A multileader style takes its text height and arrow size in the drawing's units, millimetres, as they are drawn: a note "
            + "on a sheet plotted at 1:10 wants a scale of 10 (or an annotative style), with 2.5 the height of its words on paper.";

        /// <summary>Makes one of the edits of the core that gives the name of the style it made or changed.</summary>
        private static RecordResult StyleEdit(IDrawingBackend drawing, string what, CadMakeArgs args)
        {
            string name = Editing(drawing).Make(what, args).FirstOrDefault();
            return new RecordResult { Name = name };
        }

        [McpServerTool(
            Name = "add_multileader_style",
            ReadOnly = false,
            Destructive = false,
            Idempotent = false,
            OpenWorld = false
        )]
        [Description(
            "Adds a multileader style, as the Multileader Style Manager's New and Modify do: a copy of another style (the metric Standard where from is left out) with the settings given, which add_mleader then draws in (its style argument) or which set_current_multileader_style makes current. "
                + MLeaderStyleText
                + " Refused for a name the drawing has or a text style it has not. One undo step."
        )]
        public RecordResult AddMultileaderStyle(
            [Description("The style's name.")] string name,
            [Description(
                "A multileader style of the drawing whose settings it starts from; the metric Standard where left out."
            )]
                string from = null,
            [Description(
                "A text style of the drawing for the words (list_text_styles); the one of the style it starts from where left out."
            )]
                string text_style = null,
            [Description("The height of the words in millimetres, above 0; 2.5 where left out.")]
                double? text_height = null,
            [Description(
                "The arrowhead, by the name the Multileader Style Manager shows (Closed filled, Dot, Open 30, None...)."
            )]
                string arrow = null,
            [Description("The size of the arrowhead in millimetres, 0 or more.")] double? arrow_size = null,
            [Description("The gap between the landing and the words in millimetres, 0 or more.")]
                double? landing_gap = null,
            [Description("The length of the landing (the dogleg) in millimetres, 0 or more; it is then set.")]
                double? landing_length = null,
            [Description("False for a leader with no landing.")] bool? landing = null,
            [Description(
                "The scale of the style, above 0: a note on a sheet plotted at 1:10 wants 10. Not with annotative."
            )]
                double? scale = null,
            [Description(
                "True for an annotative style, which takes its scale from the annotation scale. Not with scale."
            )]
                bool? annotative = null
        ) =>
            _session.Run("add_multileader_style", true, drawing =>
                StyleEdit(
                    drawing,
                    "add_mleader_style",
                    MLeaderArguments(
                        name,
                        from,
                        text_style,
                        text_height,
                        arrow,
                        arrow_size,
                        landing_gap,
                        landing_length,
                        landing,
                        scale,
                        annotative
                    )
                )
            );

        [McpServerTool(
            Name = "set_multileader_style",
            ReadOnly = false,
            Destructive = false,
            Idempotent = true,
            OpenWorld = false
        )]
        [Description(
            "Changes a multileader style of the drawing, as the Multileader Style Manager's Modify does: a setting left out stays as it is, and the multileaders drawn in the style follow. "
                + MLeaderStyleText
                + " One undo step."
        )]
        public RecordResult SetMultileaderStyle(
            [Description("The style's name.")] string name,
            [Description("A text style of the drawing for the words (list_text_styles).")] string text_style = null,
            [Description("The height of the words in millimetres, above 0.")] double? text_height = null,
            [Description(
                "The arrowhead, by the name the Multileader Style Manager shows (Closed filled, Dot, Open 30, None...)."
            )]
                string arrow = null,
            [Description("The size of the arrowhead in millimetres, 0 or more.")] double? arrow_size = null,
            [Description("The gap between the landing and the words in millimetres, 0 or more.")]
                double? landing_gap = null,
            [Description("The length of the landing (the dogleg) in millimetres, 0 or more; it is then set.")]
                double? landing_length = null,
            [Description("False for a leader with no landing.")] bool? landing = null,
            [Description(
                "The scale of the style, above 0: a note on a sheet plotted at 1:10 wants 10. Not with annotative."
            )]
                double? scale = null,
            [Description(
                "True for an annotative style, which takes its scale from the annotation scale. Not with scale."
            )]
                bool? annotative = null
        ) =>
            _session.Run("set_multileader_style", true, drawing =>
                StyleEdit(
                    drawing,
                    "set_mleader_style",
                    MLeaderArguments(
                        name,
                        null,
                        text_style,
                        text_height,
                        arrow,
                        arrow_size,
                        landing_gap,
                        landing_length,
                        landing,
                        scale,
                        annotative
                    )
                )
            );

        private static CadMakeArgs MLeaderArguments(
            string name,
            string from,
            string textStyle,
            double? textHeight,
            string arrow,
            double? arrowSize,
            double? landingGap,
            double? landingLength,
            bool? landing,
            double? scale,
            bool? annotative
        ) =>
            new CadMakeArgs()
                .Set("name", name?.Trim())
                .Set("from", from?.Trim())
                .Set("text_style", textStyle?.Trim())
                .Set("text_height", textHeight)
                .Set("arrow", arrow?.Trim())
                .Set("arrow_size", arrowSize)
                .Set("landing_gap", landingGap)
                .Set("landing_length", landingLength)
                .Set("landing", landing)
                .Set("scale", scale)
                .Set("annotative", annotative);

        [McpServerTool(
            Name = "set_current_multileader_style",
            ReadOnly = false,
            Destructive = false,
            Idempotent = true,
            OpenWorld = false
        )]
        [Description(
            "Makes a multileader style the current one (CMLEADERSTYLE), the one add_mleader draws in when no style is given; MLEADERSCALE takes the style's scale. Refused for a style the drawing has not (list_multileader_styles). One undo step."
        )]
        public RecordResult SetCurrentMultileaderStyle([Description("The style's name.")] string name) =>
            _session.Run("set_current_multileader_style", true, drawing =>
                StyleEdit(drawing, "current_style", new CadMakeArgs().Set("kind", "mleader").Set("name", name?.Trim()))
            );

        [McpServerTool(
            Name = "add_table_style",
            ReadOnly = false,
            Destructive = false,
            Idempotent = false,
            OpenWorld = false
        )]
        [Description(
            "Adds a table style, as the Table Style dialog's New and Modify do: a copy of another style (AutoCAD's Standard where from is left out) with the settings given, which add_table then draws in (its style argument) or which set_current_table_style makes current. Heights and margins are in millimetres as drawn: a table on a sheet plotted at 1:10 wants 10 times the height of its words on paper (25 for 2.5). Refused for a name the drawing has or a text style it has not. One undo step."
        )]
        public RecordResult AddTableStyle(
            [Description("The style's name.")] string name,
            [Description(
                "A table style of the drawing whose settings it starts from; AutoCAD's Standard where left out."
            )]
                string from = null,
            [Description("A text style of the drawing for the title, the header and the data (list_text_styles).")]
                string text_style = null,
            [Description("The height of the words of the title row in millimetres, above 0; 6 where left out.")]
                double? title_height = null,
            [Description("The height of the words of the header row in millimetres, above 0; 4.5 where left out.")]
                double? header_height = null,
            [Description("The height of the words of the data rows in millimetres, above 0; 4.5 where left out.")]
                double? data_height = null,
            [Description(
                "The margin at the left and right of the words in a cell in millimetres, 0 or more; 1.5 where left out."
            )]
                double? horizontal_margin = null,
            [Description(
                "The margin above and below the words in a cell in millimetres, 0 or more; 1.5 where left out."
            )]
                double? vertical_margin = null,
            [Description("A description of the style.")] string description = null
        ) =>
            _session.Run("add_table_style", true, drawing =>
                StyleEdit(
                    drawing,
                    "add_table_style",
                    TableArguments(
                        name,
                        from,
                        text_style,
                        title_height,
                        header_height,
                        data_height,
                        horizontal_margin,
                        vertical_margin,
                        description
                    )
                )
            );

        [McpServerTool(
            Name = "set_table_style",
            ReadOnly = false,
            Destructive = false,
            Idempotent = true,
            OpenWorld = false
        )]
        [Description(
            "Changes a table style of the drawing, as the Table Style dialog's Modify does: a setting left out stays as it is, and the tables written in the style are drawn again. Heights and margins are in millimetres as drawn. One undo step."
        )]
        public RecordResult SetTableStyle(
            [Description("The style's name.")] string name,
            [Description("A text style of the drawing for the title, the header and the data (list_text_styles).")]
                string text_style = null,
            [Description("The height of the words of the title row in millimetres, above 0.")]
                double? title_height = null,
            [Description("The height of the words of the header row in millimetres, above 0.")]
                double? header_height = null,
            [Description("The height of the words of the data rows in millimetres, above 0.")]
                double? data_height = null,
            [Description("The margin at the left and right of the words in a cell in millimetres, 0 or more.")]
                double? horizontal_margin = null,
            [Description("The margin above and below the words in a cell in millimetres, 0 or more.")]
                double? vertical_margin = null,
            [Description("A description of the style.")] string description = null
        ) =>
            _session.Run("set_table_style", true, drawing =>
                StyleEdit(
                    drawing,
                    "set_table_style",
                    TableArguments(
                        name,
                        null,
                        text_style,
                        title_height,
                        header_height,
                        data_height,
                        horizontal_margin,
                        vertical_margin,
                        description
                    )
                )
            );

        private static CadMakeArgs TableArguments(
            string name,
            string from,
            string textStyle,
            double? titleHeight,
            double? headerHeight,
            double? dataHeight,
            double? horizontalMargin,
            double? verticalMargin,
            string description
        ) =>
            new CadMakeArgs()
                .Set("name", name?.Trim())
                .Set("from", from?.Trim())
                .Set("text_style", textStyle?.Trim())
                .Set("title_height", titleHeight)
                .Set("header_height", headerHeight)
                .Set("data_height", dataHeight)
                .Set("horizontal_margin", horizontalMargin)
                .Set("vertical_margin", verticalMargin)
                .Set("description", description);

        [McpServerTool(
            Name = "set_current_table_style",
            ReadOnly = false,
            Destructive = false,
            Idempotent = true,
            OpenWorld = false
        )]
        [Description(
            "Makes a table style the current one (CTABLESTYLE), the one add_table draws in when no style is given. Refused for a style the drawing has not (list_table_styles). One undo step."
        )]
        public RecordResult SetCurrentTableStyle([Description("The style's name.")] string name) =>
            _session.Run("set_current_table_style", true, drawing =>
                StyleEdit(drawing, "current_style", new CadMakeArgs().Set("kind", "table").Set("name", name?.Trim()))
            );
    }
}
