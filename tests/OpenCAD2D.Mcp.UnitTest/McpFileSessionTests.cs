using System;
using System.IO;
using System.Linq;
using System.Text.Json.Nodes;
using System.Threading.Tasks;
using ModelContextProtocol.Client;
using ModelContextProtocol.Protocol;
using Xunit;

namespace OpenCAD2D.Mcp.UnitTest
{
    /// <summary>A scripted session of an agent over a drawing file worked on in the server's own process.</summary>
    public class McpFileSessionTests
    {
        private static McpOptions Options(string file, bool readOnly = false, bool scripts = false) =>
            new McpOptions
            {
                File = file,
                ReadOnly = readOnly,
                AllowScripts = scripts,
            };

        [Fact]
        public async Task The_server_lists_every_tool_with_a_description_and_a_schema()
        {
            string folder = TestFolder.Make("tools");
            await using McpHarness harness = await McpHarness.StartAsync(Options(Drawings.Plate(folder)));

            var tools = await harness.Client.ListToolsAsync();
            string[] names = tools.Select(t => t.Name).ToArray();

            string[] expected =
            {
                "add_aligned_dim",
                "add_angular_dim",
                "add_arc",
                "add_circle",
                "add_diameter_dim",
                "add_hatch",
                "add_insert",
                "add_line",
                "add_linear_dim",
                "add_mtext",
                "add_polyline",
                "add_radial_dim",
                "add_text",
                "bounds",
                "erase",
                "info",
                "list_blocks",
                "list_dimension_styles",
                "list_layers",
                "list_text_styles",
                "move",
                "new_drawing",
                "open_drawing",
                "overlaps",
                "query_entities",
                "render_png",
                "rotate",
                "run_command",
                "save",
                "scale",
                "set_properties",
                "undo",
            };
            Assert.Equal(expected, names.OrderBy(n => n, StringComparer.Ordinal).ToArray());

            foreach (McpClientTool tool in tools)
            {
                Assert.False(string.IsNullOrWhiteSpace(tool.Description), tool.Name + " has no description.");
                Assert.Equal("object", tool.JsonSchema.GetProperty("type").GetString());
            }
        }

        [Fact]
        public async Task Only_the_tools_that_read_say_so_to_the_client()
        {
            string folder = TestFolder.Make("hints");
            await using McpHarness harness = await McpHarness.StartAsync(Options(Drawings.Plate(folder)));
            string[] reading =
            {
                "bounds",
                "info",
                "list_blocks",
                "list_dimension_styles",
                "list_layers",
                "list_text_styles",
                "overlaps",
                "query_entities",
                "render_png",
            };

            var tools = await harness.Client.ListToolsAsync();

            foreach (McpClientTool tool in tools)
            {
                Assert.Equal(reading.Contains(tool.Name), tool.ProtocolTool.Annotations.ReadOnlyHint == true);
            }
        }

        [Fact]
        public async Task An_agent_reads_a_drawing_and_adds_a_line_a_text_and_a_dimension()
        {
            string folder = TestFolder.Make("script");
            string path = Drawings.Plate(folder);
            await using McpHarness harness = await McpHarness.StartAsync(Options(path));

            JsonNode info = await harness.CallAsync("info");
            Assert.Equal("file", (string)info["mode"]);
            Assert.Equal("plate.dxf", (string)info["name"]);
            Assert.Equal(4, (int)info["entityCount"]);
            Assert.Equal(1, (int)info["counts"]["CIRCLE"]);
            Assert.Equal(1, (int)info["counts"]["LWPOLYLINE"]);
            double[] extents = info["extents"].AsArray().Select(n => (double)n).ToArray();
            Assert.Equal(0.0, extents[0], 3);
            Assert.Equal(0.0, extents[1], 3);
            Assert.Equal(200.0, extents[2], 3);
            Assert.Equal(100.0, extents[3], 3);

            JsonNode layers = await harness.CallAsync("list_layers");
            Assert.Equal(
                new[] { "0", "Holes", "Notes", "Plate" },
                layers.AsArray().Select(l => (string)l["name"]).ToArray()
            );

            JsonNode added = await harness.CallAsync(
                "add_line",
                ("start", new[] { 0.0, 0.0 }),
                ("end", new[] { 100.0, 100.0 }),
                ("layer", "Plate")
            );
            Assert.Equal("LINE", (string)added["kind"]);
            string lineHandle = (string)added["handle"];
            Assert.False(string.IsNullOrEmpty(lineHandle));

            JsonNode text = await harness.CallAsync(
                "add_text",
                ("text", "NOTE 1"),
                ("at", new[] { 20.0, 80.0 }),
                ("height", 4.0),
                ("layer", "Notes")
            );
            Assert.Equal("TEXT", (string)text["kind"]);

            JsonNode dim = await harness.CallAsync(
                "add_linear_dim",
                ("first", new[] { 0.0, 0.0 }),
                ("second", new[] { 200.0, 0.0 }),
                ("location", new[] { 100.0, -20.0 })
            );
            Assert.Equal("DIMENSION", (string)dim["kind"]);

            JsonNode lines = await harness.CallAsync("query_entities", ("kinds", new[] { "LINE" }));
            Assert.Equal(2, (int)lines["total"]);
            JsonNode last = lines["entities"].AsArray().Last();
            Assert.Equal(lineHandle, (string)last["handle"]);
            Assert.Equal("Plate", (string)last["layer"]);
            Assert.Equal(100.0, (double)last["geometry"]["end"][0], 3);
            Assert.Equal(100.0, (double)last["box"][2], 3);

            JsonNode inBox = await harness.CallAsync(
                "query_entities",
                ("box", new[] { 90.0, 40.0, 110.0, 60.0 }),
                ("kinds", new[] { "CIRCLE" })
            );
            Assert.Equal(1, (int)inBox["total"]);

            JsonNode dims = await harness.CallAsync("query_entities", ("kinds", new[] { "DIMENSION" }));
            Assert.Equal(1, (int)dims["total"]);
        }
    }
}
