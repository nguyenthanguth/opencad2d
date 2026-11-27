using System;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Threading;
using System.Threading.Tasks;
using ModelContextProtocol.Protocol;
using ModelContextProtocol.Server;

namespace OpenCAD2D.Mcp
{
    /// <summary>Builds the server of a session: its tools, its name and its instructions, over stdio or over a pair of streams.</summary>
    internal static class McpHost
    {
        /// <summary>The name the server gives itself in the handshake.</summary>
        public const string ServerName = "OpenCAD2D";

        /// <summary>What the model reads once, at the start, about how to use the tools.</summary>
        public const string Instructions =
            "OpenCAD2D is a 2D CAD (like AutoCAD) and these tools read and draw a drawing. Units: millimetres for every length and "
            + "coordinate, degrees counter-clockwise from the +x direction for every angle, a point is [x, y], a lineweight is in "
            + "millimetres as AutoCAD shows it (0.5; the hundredths, 50, are read too), a viewport or plot scale is the n of 1:n. "
            + "Start with info, then list_layers, list_text_styles and list_dimension_styles to learn the names the drawing has, and "
            + "describe_kinds for what each kind of entity holds and which tool writes it. "
            + "Spaces: the add_* tools, the reading and measuring tools (query_entities, bounds, overlaps, get_texts, free_space, fits, "
            + "measure_* ...), render_png, export_svg and plot_pdf take an optional layout and work in model space where it is left "
            + "out; give layout (a sheet's name) to work on a sheet. The tools that run a command on objects (copy, mirror, offset, "
            + "fillet, chamfer, trim, extend, stretch, array_*, explode, join, break_at, lengthen, set_draw_order), run_command and "
            + "select work in the layout that is shown, which set_current_layout changes (in an app, it is what the user sees); "
            + "move, rotate, scale, erase, set_properties and the edit_* tools act on handles wherever they are. "
            + "Tool groups. Read: info, list_*, query_entities, get_entity, get_texts, get_attributes, count_entities, bounds, "
            + "overlaps, id_point, select. Place annotation: measure_text, measure_mtext, measure_dimension and measure_leader give "
            + "the box and frame of a word before it is added, free_space lists the empty rectangles of a frame, fits tests one place, "
            + "check_standard scores the drawing against a company standard (higher is better). Draw: the add_* tools. Change: move, "
            + "rotate, scale, copy, mirror, offset, fillet, chamfer, trim, extend, stretch, arrays, erase, set_properties, "
            + "match_properties, edit_text, edit_dimension. Layers and styles: add_layer, set_layer, set_current_layer, "
            + "add_text_style, add_dimension_style, load_linetype, add_multileader_style, set_multileader_style, "
            + "set_current_multileader_style, add_table_style, set_table_style, set_current_table_style. Layouts: create_layout, rename_layout, delete_layout, "
            + "set_current_layout, add_viewport, set_viewport_scale, lock_viewport, list_layouts. Blocks: define_block, "
            + "add_attribute_definition, insert_block_with_attributes, set_attribute, get_block, list_blocks. Output: render_png "
            + "(a picture), plot_pdf, export_svg, export_json and export_graph (a page at a time), export_to_file, save_as, save. "
            + "Plans: run_plan runs many calls in one, which are one undo step. Try and go back: snapshot, reset, release_snapshot, "
            + "undo, redo. Debug: audit, dump_drawing, timings, last_errors, command_history, app_screenshot, compare_drawings, "
            + "server_status. "
            + "Undo: each tool that draws or changes the drawing is one undo step that undo takes back (run_plan's are one step in "
            + "all; run_command is the steps of its commands). Files: only save writes the drawing's own file; save_as (a copy), "
            + "plot_pdf, export_svg, export_to_file and dump_drawing write other files, and no other tool writes a file. "
            + "A call with a missing or wrongly typed argument is refused with the name of the argument and what it takes.";

        /// <summary>Makes the options of a server over a session: every tool of <see cref="DrawingTools"/>.</summary>
        public static McpServerOptions Options(McpSession session)
        {
            var tools = new DrawingTools(session);
            var options = new McpServerOptions
            {
                ServerInfo = new Implementation
                {
                    Name = ServerName,
                    Version = typeof(McpHost).Assembly.GetName().Version.ToString(3),
                },
                ServerInstructions = Instructions,
            };
            options.ToolCollection = new McpServerPrimitiveCollection<McpServerTool>();

            foreach (
                MethodInfo method in typeof(DrawingTools)
                    .GetMethods(BindingFlags.Public | BindingFlags.Instance)
                    .Where(m => m.GetCustomAttribute<McpServerToolAttribute>() != null)
                    .OrderBy(m => m.GetCustomAttribute<McpServerToolAttribute>().Name, StringComparer.Ordinal)
            )
            {
                options.ToolCollection.Add(new CheckedTool(McpServerTool.Create(method, tools)));
            }

            // run_plan is added last and is given the table itself, so that every tool above is one a plan can call.
            options.ToolCollection.Add(
                new CheckedTool(
                    McpServerTool.Create(
                        typeof(PlanTools).GetMethod(nameof(PlanTools.RunPlan)),
                        new PlanTools(session, options.ToolCollection)
                    )
                )
            );

            return options;
        }

        /// <summary>Serves a session over the standard input and output until the client closes them.</summary>
        public static async Task RunStdioAsync(McpSession session, CancellationToken cancel)
        {
            McpServerOptions options = Options(session);
            await using McpServer server = McpServer.Create(new StdioServerTransport(ServerName), options);
            await server.RunAsync(cancel).ConfigureAwait(false);
        }

        /// <summary>Serves a session over two streams, which is how a test puts a client on the other end.</summary>
        public static async Task RunAsync(McpSession session, Stream input, Stream output, CancellationToken cancel)
        {
            McpServerOptions options = Options(session);
            await using McpServer server = McpServer.Create(
                new StreamServerTransport(input, output, ServerName),
                options
            );
            await server.RunAsync(cancel).ConfigureAwait(false);
        }
    }
}
