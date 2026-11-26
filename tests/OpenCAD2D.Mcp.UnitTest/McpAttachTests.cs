using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Text.Json.Nodes;
using System.Threading.Tasks;
using ModelContextProtocol.Protocol;
using Xunit;

namespace OpenCAD2D.Mcp.UnitTest
{
    /// <summary>
    /// A drawing app with no window (<c>tests/OpenCAD2D.Remote.TestServer</c>) in a process of its own, which announces itself in a
    /// folder of the test's own: the maintainer's running app is never looked at. Ended with <c>quit</c>, and by itself after a few
    /// minutes if the test run died.
    /// </summary>
    internal sealed class TestApp : IDisposable
    {
        private readonly Process _process;

        private TestApp(Process process, string folder, int processId)
        {
            _process = process;
            Folder = folder;
            ProcessId = processId;
        }

        /// <summary>Gets the folder the app announced itself in.</summary>
        public string Folder { get; }

        public int ProcessId { get; }

        /// <summary>Starts the test server and waits until it listens.</summary>
        public static TestApp Start()
        {
            string baseDirectory = AppContext.BaseDirectory.TrimEnd(Path.DirectorySeparatorChar);
            string config = new DirectoryInfo(baseDirectory).Parent.Name;
            string tests = new DirectoryInfo(baseDirectory).Parent.Parent.Parent.Parent.FullName;
            string exe = Path.Combine(
                tests,
                "OpenCAD2D.Remote.TestServer",
                "bin",
                config,
                "net8.0",
                "OpenCAD2D.Remote.TestServer.exe"
            );
            Assert.True(File.Exists(exe), "The remote test server is not built: " + exe);
            string folder = TestFolder.Make("app");
            var process = Process.Start(
                new ProcessStartInfo(exe, "--folder \"" + folder + "\" --minutes 5")
                {
                    RedirectStandardInput = true,
                    RedirectStandardOutput = true,
                    UseShellExecute = false,
                    CreateNoWindow = true,
                }
            );
            string ready = process.StandardOutput.ReadLine();
            Assert.StartsWith("READY ", ready);
            return new TestApp(process, folder, int.Parse(ready.Split(' ')[2]));
        }

        public void Dispose()
        {
            try
            {
                _process.StandardInput.WriteLine("quit");
                _process.StandardInput.Flush();

                if (!_process.WaitForExit(10000))
                {
                    _process.Kill();
                }
            }
            catch (InvalidOperationException) { }
            catch (IOException) { }
            finally
            {
                _process.Dispose();
            }
        }
    }

    /// <summary>A scripted session of an agent attached to a running app, through its Remote API.</summary>
    public class McpAttachTests
    {
        private static async Task<McpHarness> Attach(TestApp app, bool readOnly = false, bool scripts = false)
        {
            McpHarness harness = await McpHarness.StartAsync(
                new McpOptions
                {
                    Attach = true,
                    RemoteFolder = app.Folder,
                    ReadOnly = readOnly,
                    AllowScripts = scripts,
                }
            );
            return harness;
        }

        [Fact]
        public async Task Without_an_app_every_tool_says_so_and_the_server_stays_up()
        {
            string folder = TestFolder.Make("noapp");
            await using McpHarness harness = await McpHarness.StartAsync(
                new McpOptions { Attach = true, RemoteFolder = folder }
            );

            string message = await harness.FailAsync("info");

            Assert.Contains("No OpenCAD2D is running", message);
        }

        [Fact]
        public async Task An_agent_attached_to_an_app_opens_a_drawing_reads_it_and_draws_in_it()
        {
            using TestApp app = TestApp.Start();
            string path = Drawings.Plate(TestFolder.Make("attached"));
            await using McpHarness harness = await Attach(app);

            JsonNode opened = await harness.CallAsync("open_drawing", ("path", path));
            Assert.Equal("app", (string)opened["mode"]);
            Assert.Equal("plate.dxf", (string)opened["name"]);

            JsonNode info = await harness.CallAsync("info");
            Assert.Equal("app", (string)info["mode"]);
            Assert.Equal(4, (int)info["entityCount"]);
            Assert.Equal(200.0, (double)info["extents"][2], 3);

            JsonNode line = await harness.CallAsync(
                "add_line",
                ("start", new[] { 0.0, 0.0 }),
                ("end", new[] { 100.0, 100.0 })
            );
            await harness.CallAsync("add_text", ("text", "OVER"), ("at", new[] { 80.0, 48.0 }), ("height", 5.0));
            await harness.CallAsync(
                "add_linear_dim",
                ("first", new[] { 0.0, 0.0 }),
                ("second", new[] { 200.0, 0.0 }),
                ("location", new[] { 100.0, -20.0 })
            );
            Assert.Equal(7, (int)(await harness.CallAsync("info"))["entityCount"]);

            JsonNode lines = await harness.CallAsync("query_entities", ("kinds", new[] { "LINE" }));
            Assert.Contains(lines["entities"].AsArray(), e => (string)e["handle"] == (string)line["handle"]);

            JsonNode found = await harness.CallAsync("overlaps");
            Assert.True((int)found["total"] >= 1);

            JsonNode box = await harness.CallAsync("bounds", ("handles", new[] { (string)line["handle"] }));
            Assert.Equal(100.0, (double)box["rows"][0]["box"][2], 3);

            JsonNode circles = await harness.CallAsync("query_entities", ("kinds", new[] { "CIRCLE" }));
            string circle = (string)circles["entities"][0]["handle"];
            await harness.CallAsync("move", ("handles", new[] { circle }), ("delta", new[] { 10.0, 0.0 }));
            JsonNode moved = (await harness.CallAsync("query_entities", ("kinds", new[] { "CIRCLE" })))["entities"][0];
            Assert.Equal(110.0, (double)moved["geometry"]["center"][0], 3);
            await harness.CallAsync("set_properties", ("handles", new[] { circle }), ("layer", "Notes"));
            JsonNode relayered = (await harness.CallAsync("query_entities", ("kinds", new[] { "CIRCLE" })))["entities"][
                0
            ];
            Assert.Equal("Notes", (string)relayered["layer"]);
            await harness.CallAsync("erase", ("handles", new[] { circle }));
            Assert.Equal(6, (int)(await harness.CallAsync("info"))["entityCount"]);

            await harness.CallAsync("undo");
            Assert.Equal(7, (int)(await harness.CallAsync("info"))["entityCount"]);
        }

        [Fact]
        public async Task Attached_a_picture_is_of_the_saved_file_and_the_server_says_why_when_there_are_unsaved_changes()
        {
            using TestApp app = TestApp.Start();
            string path = Drawings.Plate(TestFolder.Make("attachrender"));
            await using McpHarness harness = await Attach(app);
            await harness.CallAsync("open_drawing", ("path", path));

            CallToolResult first = await harness.CallRawAsync("render_png", ("width", 300), ("height", 200));
            Assert.True(first.IsError != true);
            Assert.Single(first.Content.OfType<ImageContentBlock>());

            await harness.CallAsync("add_circle", ("center", new[] { 5.0, 5.0 }), ("radius", 3.0));
            string refused = await harness.FailAsync("render_png");
            Assert.Contains("has changes the app has not saved", refused);
            Assert.Contains("call save", refused);

            await harness.CallAsync("save");
            CallToolResult again = await harness.CallRawAsync("render_png", ("width", 300), ("height", 200));
            Assert.True(again.IsError != true);
        }

        [Fact]
        public async Task Attached_read_only_and_scripts_are_refused_as_they_are_on_a_file()
        {
            using TestApp app = TestApp.Start();
            string path = Drawings.Plate(TestFolder.Make("attachsafe"));

            await using (McpHarness readOnly = await Attach(app, readOnly: true, scripts: true))
            {
                await readOnly.CallAsync("open_drawing", ("path", path));
                Assert.Contains(
                    "--read-only",
                    await readOnly.FailAsync("add_line", ("start", new[] { 0.0, 0.0 }), ("end", new[] { 1.0, 1.0 }))
                );
                Assert.Contains("--read-only", await readOnly.FailAsync("undo"));
                Assert.Contains("--read-only", await readOnly.FailAsync("run_command", ("text", "REGEN")));
                Assert.Equal(4, (int)(await readOnly.CallAsync("info"))["entityCount"]);
            }

            await using McpHarness noScripts = await Attach(app);
            Assert.Contains("--allow-scripts", await noScripts.FailAsync("run_command", ("text", "REGEN")));
        }

        [Fact]
        public async Task Attached_run_command_types_on_the_apps_command_line_with_allow_scripts()
        {
            using TestApp app = TestApp.Start();
            string path = Drawings.Plate(TestFolder.Make("attachcmd"));
            await using McpHarness harness = await Attach(app, scripts: true);
            await harness.CallAsync("open_drawing", ("path", path));

            JsonNode result = await harness.CallAsync("run_command", ("text", "LINE 0,0 30,40" + Drawings.TwoEnters));

            Assert.NotNull((string)result["output"]);
            Assert.Equal(5, (int)(await harness.CallAsync("info"))["entityCount"]);
        }
    }
}
