using System;
using System.Collections.Generic;
using System.IO;
using System.IO.Pipelines;
using System.Linq;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Threading;
using System.Threading.Tasks;
using GeometryHelper.Dxf;
using GeometryHelper.Geometry;
using ModelContextProtocol.Client;
using ModelContextProtocol.Protocol;
using Xunit;

namespace OpenCAD2D.Mcp.UnitTest
{
    /// <summary>The folder of this run of the tests, deleted when the run ends.</summary>
    internal static class TestFolder
    {
        private static readonly string Root = Path.Combine(
            Path.GetTempPath(),
            "opencad2d-mcp-tests-" + Guid.NewGuid().ToString("N")
        );

        static TestFolder()
        {
            AppDomain.CurrentDomain.ProcessExit += (s, e) =>
            {
                try
                {
                    if (Directory.Exists(Root))
                    {
                        Directory.Delete(Root, true);
                    }
                }
                catch (IOException) { }
                catch (UnauthorizedAccessException) { }
            };
        }

        /// <summary>A new folder of the run's own.</summary>
        public static string Make(string name)
        {
            string folder = Path.Combine(Root, name + "-" + Guid.NewGuid().ToString("N").Substring(0, 8));
            Directory.CreateDirectory(folder);
            return folder;
        }
    }

    /// <summary>Builds the small drawings the tests open, in code.</summary>
    internal static class Drawings
    {
        /// <summary>The end of a command text that leaves the command on its prompt: an empty line is Enter.</summary>
        public const string TwoEnters = "\n\n";

        /// <summary>
        /// A drawing of a plate 200 by 100 mm: a closed polyline outline on layer Plate, a circle of radius 20 at (100, 50) on
        /// layer Holes, a line across the plate on layer Plate and a text 5 high at (10, 10) on layer Notes, written as a DXF.
        /// </summary>
        public static string Plate(string folder)
        {
            var database = new DxfDatabase("AC1027");
            var outline = new DxfPolyline();
            outline.AddVertexAt(0, new GeoPoint2(0, 0), 0, 0, 0);
            outline.AddVertexAt(1, new GeoPoint2(200, 0), 0, 0, 0);
            outline.AddVertexAt(2, new GeoPoint2(200, 100), 0, 0, 0);
            outline.AddVertexAt(3, new GeoPoint2(0, 100), 0, 0, 0);
            outline.Closed = true;
            outline.Layer = "Plate";
            database.ModelSpace.AppendEntity(outline);
            database.ModelSpace.AppendEntity(
                new DxfCircle
                {
                    Center = new GeoPoint3(100, 50, 0),
                    Radius = 20,
                    Layer = "Holes",
                }
            );
            database.ModelSpace.AppendEntity(
                new DxfLine(new GeoPoint3(0, 50, 0), new GeoPoint3(200, 50, 0)) { Layer = "Plate" }
            );
            database.ModelSpace.AppendEntity(
                new DxfDBText
                {
                    TextString = "PLATE",
                    Position = new GeoPoint3(10, 10, 0),
                    Height = 5,
                    Layer = "Notes",
                }
            );
            string path = Path.Combine(folder, "plate.dxf");
            CadDocument.From(database).SaveAs(path);
            return path;
        }
    }

    /// <summary>A server over a session and a client on the other end of two in-memory pipes, as an agent's program is.</summary>
    internal sealed class McpHarness : IAsyncDisposable
    {
        private readonly CancellationTokenSource _stop;
        private readonly Task _server;

        private McpHarness(McpSession session, McpClient client, Task server, CancellationTokenSource stop)
        {
            _stop = stop;
            Session = session;
            Client = client;
            _server = server;
        }

        public McpSession Session { get; }

        public McpClient Client { get; }

        /// <summary>Starts a server with the options and a client connected to it.</summary>
        public static async Task<McpHarness> StartAsync(McpOptions options)
        {
            var session = new McpSession(options);
            session.Start();
            var toServer = new Pipe();
            var toClient = new Pipe();
            var stop = new CancellationTokenSource();
            Task server = McpHost.RunAsync(session, toServer.Reader.AsStream(), toClient.Writer.AsStream(), stop.Token);
            McpClient client = await McpClient.CreateAsync(
                new StreamClientTransport(toServer.Writer.AsStream(), toClient.Reader.AsStream())
            );
            return new McpHarness(session, client, server, stop);
        }

        /// <summary>Calls a tool and gives its result, errors included.</summary>
        public Task<CallToolResult> CallRawAsync(string tool, params (string Name, object Value)[] arguments) =>
            Client.CallToolAsync(tool, arguments.ToDictionary(a => a.Name, a => (object)a.Value)).AsTask();

        /// <summary>Calls a tool that must succeed and gives its answer as JSON.</summary>
        public async Task<JsonNode> CallAsync(string tool, params (string Name, object Value)[] arguments)
        {
            CallToolResult result = await CallRawAsync(tool, arguments);
            string text = string.Join("\n", result.Content.OfType<TextContentBlock>().Select(t => t.Text));
            Assert.False(result.IsError == true, tool + " failed: " + text);
            return result.StructuredContent.HasValue
                ? JsonNode.Parse(result.StructuredContent.Value.GetRawText())
                : JsonNode.Parse(text);
        }

        /// <summary>Calls a tool that must fail and gives the message it failed with.</summary>
        public async Task<string> FailAsync(string tool, params (string Name, object Value)[] arguments)
        {
            CallToolResult result = await CallRawAsync(tool, arguments);
            Assert.True(
                result.IsError == true,
                tool + " was expected to fail but gave " + result.Content.Count + " blocks."
            );
            return string.Join("\n", result.Content.OfType<TextContentBlock>().Select(t => t.Text));
        }

        public async ValueTask DisposeAsync()
        {
            await Client.DisposeAsync();
            _stop.Cancel();

            try
            {
                await _server.WaitAsync(TimeSpan.FromSeconds(5));
            }
            catch (OperationCanceledException) { }
            catch (TimeoutException) { }

            Session.Dispose();
        }
    }
}
