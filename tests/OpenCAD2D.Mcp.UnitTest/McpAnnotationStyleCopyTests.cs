using System;
using System.Collections.Concurrent;
using System.IO;
using System.IO.Pipes;
using System.Linq;
using System.Text.Json.Nodes;
using System.Threading;
using System.Threading.Tasks;
using Xunit;

namespace OpenCAD2D.Mcp.UnitTest
{
    /// <summary>
    /// Attached to an app, list_multileader_styles, list_table_styles, measure_leader with a style and get_entity of a multileader
    /// or a table read the styles from the app's <c>annotationStyles</c> answer and the object's own copy: none of them asks the
    /// app for its whole drawing (the request <c>database</c>). The requests are counted on a pipe that relays the app's own.
    /// </summary>
    public class McpAnnotationStyleCopyTests
    {
        [Fact]
        public async Task Attached_the_style_lists_measure_leader_and_get_entity_never_copy_the_drawing()
        {
            using TestApp app = TestApp.Start();
            await app.NewAsync();
            using var relay = new CountingRelay(app);
            await using McpHarness harness = await McpHarness.StartAsync(
                new McpOptions { Attach = true, RemoteFolder = relay.Folder }
            );
            await harness.CallAsync("add_multileader_style", ("name", "Ten"), ("scale", 10.0));
            await harness.CallAsync("add_table_style", ("name", "PLAN-TABLE"), ("data_height", 40.0));
            string note = (string)
                (
                    await harness.CallAsync(
                        "add_mleader",
                        ("arrow", new[] { 0.0, 0.0 }),
                        ("landing", new[] { 100.0, 60.0 }),
                        ("text", "NOTCH"),
                        ("style", "Ten")
                    )
                )["handle"];
            string table = (string)
                (
                    await harness.CallAsync(
                        "add_table",
                        ("at", new[] { 500.0, 0.0 }),
                        ("columns", 2),
                        ("rows", 2),
                        ("style", "PLAN-TABLE")
                    )
                )["handle"];
            int copies = relay.Count("database");
            int styles = relay.Count("annotationStyles");

            JsonNode leaders = await harness.CallAsync("list_multileader_styles");
            JsonNode tables = await harness.CallAsync("list_table_styles");
            JsonNode byStyle = await harness.CallAsync(
                "measure_leader",
                ("arrow", new[] { 0.0, 0.0 }),
                ("landing", new[] { 100.0, 60.0 }),
                ("text", "NOTCH"),
                ("style", "ten")
            );
            JsonNode byScale = await harness.CallAsync(
                "measure_leader",
                ("arrow", new[] { 0.0, 0.0 }),
                ("landing", new[] { 100.0, 60.0 }),
                ("text", "NOTCH"),
                ("style", "Ten"),
                ("scale", 10.0)
            );
            JsonNode noteEntity = await harness.CallAsync("get_entity", ("handle", note));
            JsonNode tableEntity = await harness.CallAsync("get_entity", ("handle", table));

            Assert.Equal(copies, relay.Count("database"));
            // One answer for each list and one for the style measure_leader is given without a scale.
            Assert.Equal(styles + 3, relay.Count("annotationStyles"));
            Assert.Equal(10.0, (double)leaders.AsArray().Single(r => (string)r["name"] == "Ten")["scale"], 6);
            Assert.Equal(
                40.0,
                (double)tables.AsArray().Single(r => (string)r["name"] == "PLAN-TABLE")["data"]["textHeight"],
                6
            );
            Assert.Equal(byScale.ToJsonString(), byStyle.ToJsonString());
            Assert.Equal("Ten", (string)noteEntity["details"]["style"]);
            Assert.Equal("PLAN-TABLE", (string)tableEntity["details"]["style"]);

            // The relay sees a copy where a tool does make one: audit checks a copy of the whole drawing.
            await harness.CallAsync("audit");
            Assert.Equal(copies + 1, relay.Count("database"));
        }

        /// <summary>
        /// A pipe that stands for an app: it announces itself in a folder of its own with the app's process id, passes every
        /// message both ways unchanged and counts the requests by their <c>op</c>, each before the app is given it.
        /// </summary>
        private sealed class CountingRelay : IDisposable
        {
            private readonly string _app;
            private readonly string _pipe = "OpenCAD2D.relay." + Guid.NewGuid().ToString("N");
            private readonly ConcurrentDictionary<string, int> _counts = new ConcurrentDictionary<string, int>(
                StringComparer.Ordinal
            );
            private readonly CancellationTokenSource _stop = new CancellationTokenSource();

            public CountingRelay(TestApp app)
            {
                string file = Assert.Single(Directory.GetFiles(app.Folder, "*.json"));
                JsonObject instance = JsonNode.Parse(File.ReadAllText(file)).AsObject();
                _app = (string)instance["pipe"];
                instance["pipe"] = _pipe;
                Folder = TestFolder.Make("relay");
                File.WriteAllText(Path.Combine(Folder, Path.GetFileName(file)), instance.ToJsonString());
                _ = Task.Run(Accept);
            }

            /// <summary>Gets the folder the relay announces itself in.</summary>
            public string Folder { get; }

            /// <summary>The number of requests of an op passed to the app so far.</summary>
            public int Count(string op) => _counts.TryGetValue(op, out int n) ? n : 0;

            public void Dispose() => _stop.Cancel();

            private async Task Accept()
            {
                while (!_stop.IsCancellationRequested)
                {
                    var client = new NamedPipeServerStream(
                        _pipe,
                        PipeDirection.InOut,
                        NamedPipeServerStream.MaxAllowedServerInstances,
                        PipeTransmissionMode.Byte,
                        PipeOptions.Asynchronous
                    );

                    NamedPipeClientStream app = null;

                    try
                    {
                        await client.WaitForConnectionAsync(_stop.Token);
                        app = new NamedPipeClientStream(".", _app, PipeDirection.InOut, PipeOptions.Asynchronous);
                        await app.ConnectAsync(5000, _stop.Token);
                        NamedPipeClientStream connected = app;
                        _ = Task.Run(() => Pass(client, connected, true));
                        _ = Task.Run(() => Pass(connected, client, false));
                    }
                    catch (Exception)
                    {
                        // Cancelled, or the app did not take the connection (a timeout or a broken pipe): closing the
                        // MCP side makes its read end, so the test fails rather than waiting for ever.
                        app?.Dispose();
                        client.Dispose();
                        return;
                    }
                }
            }

            private async Task Pass(Stream from, Stream to, bool count)
            {
                try
                {
                    byte[] head = new byte[4];

                    while (await Fill(from, head))
                    {
                        byte[] body = new byte[BitConverter.ToInt32(head, 0)];

                        if (!await Fill(from, body))
                        {
                            break;
                        }

                        if (count)
                        {
                            string op = (string)JsonNode.Parse(body)["op"] ?? string.Empty;
                            _counts.AddOrUpdate(op, 1, (_, n) => n + 1);
                        }

                        await to.WriteAsync(head, _stop.Token);
                        await to.WriteAsync(body, _stop.Token);
                        await to.FlushAsync(_stop.Token);
                    }
                }
                catch (Exception ex)
                    when (ex is IOException || ex is ObjectDisposedException || ex is OperationCanceledException)
                {
                    // One side has gone: the other is closed below.
                }
                finally
                {
                    from.Dispose();
                    to.Dispose();
                }
            }

            private async Task<bool> Fill(Stream stream, byte[] buffer)
            {
                int read = 0;

                while (read < buffer.Length)
                {
                    int n = await stream.ReadAsync(buffer.AsMemory(read), _stop.Token);

                    if (n <= 0)
                    {
                        return false;
                    }

                    read += n;
                }

                return true;
            }
        }
    }
}
