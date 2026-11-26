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
            "OpenCAD2D is a 2D CAD (like AutoCAD) and these tools read and draw a drawing. Units are millimetres, angles are degrees "
            + "counter-clockwise from the +x direction, a point is [x, y]. Start with info, then list_layers, list_text_styles and "
            + "list_dimension_styles to learn the names the drawing has. Read with query_entities (paged), bounds and overlaps; "
            + "draw with the add_* tools; change with move, rotate, scale, erase and set_properties; every writing call is one "
            + "undo step that undo takes back. Look at the result with render_png. Check a layout with overlaps before saving. "
            + "Nothing reaches a file until save.";

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
                options.ToolCollection.Add(McpServerTool.Create(method, tools));
            }

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
