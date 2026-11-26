using System;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using ModelContextProtocol.Client;
using ModelContextProtocol.Protocol;
using Xunit;

namespace OpenCAD2D.Mcp.UnitTest
{
    /// <summary>The server as an agent starts it: the exe, in a process of its own, spoken to over its standard input and output.</summary>
    public class McpProcessTests
    {
        private static string Exe()
        {
            string exe = Path.Combine(AppContext.BaseDirectory, "OpenCAD2D.Mcp.exe");
            Assert.True(File.Exists(exe), "OpenCAD2D.Mcp.exe is not beside the tests: " + exe);
            return exe;
        }

        private static Task<McpClient> Start(params string[] arguments) =>
            McpClient.CreateAsync(
                new StdioClientTransport(
                    new StdioClientTransportOptions
                    {
                        Name = "opencad2d",
                        Command = Exe(),
                        Arguments = arguments,
                    }
                )
            );

        [Fact]
        public async Task The_exe_serves_a_file_over_stdio_and_gives_a_picture()
        {
            string path = Drawings.Plate(TestFolder.Make("process"));
            await using McpClient client = await Start("--file", path, "--read-only");

            Assert.Equal("OpenCAD2D", client.ServerInfo.Name);
            Assert.Contains("millimetres", client.ServerInstructions);
            var tools = await client.ListToolsAsync();
            Assert.Equal(131, tools.Count);

            CallToolResult info = await client.CallToolAsync("info");
            Assert.Contains(
                "\"name\":\"plate.dxf\"",
                string.Join(string.Empty, info.Content.OfType<TextContentBlock>().Select(t => t.Text))
            );

            CallToolResult picture = await client.CallToolAsync(
                "render_png",
                new System.Collections.Generic.Dictionary<string, object> { ["width"] = 320, ["height"] = 160 }
            );
            ImageContentBlock image = Assert.Single(picture.Content.OfType<ImageContentBlock>());
            byte[] png = image.DecodedData.ToArray();
            Assert.Equal(320, (png[16] << 24) | (png[17] << 16) | (png[18] << 8) | png[19]);
            Assert.Equal(160, (png[20] << 24) | (png[21] << 16) | (png[22] << 8) | png[23]);

            CallToolResult refused = await client.CallToolAsync(
                "erase",
                new System.Collections.Generic.Dictionary<string, object> { ["handles"] = new[] { "2" } }
            );
            Assert.True(refused.IsError);
        }

        [Fact]
        public async Task The_exe_starts_with_no_drawing_and_an_agent_opens_one()
        {
            string path = Drawings.Plate(TestFolder.Make("processopen"));
            await using McpClient client = await Start();

            CallToolResult none = await client.CallToolAsync("info");
            Assert.True(none.IsError);

            CallToolResult opened = await client.CallToolAsync(
                "open_drawing",
                new System.Collections.Generic.Dictionary<string, object> { ["path"] = path }
            );
            Assert.True(opened.IsError != true);
        }
    }
}
