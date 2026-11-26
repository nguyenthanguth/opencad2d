using System;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.Json.Nodes;
using System.Threading.Tasks;
using Xunit;

namespace OpenCAD2D.Mcp.UnitTest
{
    /// <summary>
    /// The output and debug tools of a server attached to an app, against the app with no window of
    /// <c>tests/OpenCAD2D.Remote.TestServer</c> (the maintainer's running app is never looked at).
    /// </summary>
    public class McpDebugAttachTests
    {
        private static async Task<McpHarness> Attach(TestApp app, bool readOnly = false, bool scripts = false) =>
            await McpHarness.StartAsync(
                new McpOptions
                {
                    Attach = true,
                    RemoteFolder = app.Folder,
                    ReadOnly = readOnly,
                    AllowScripts = scripts,
                }
            );

        private static string Unique(string stem) => stem + "-" + Guid.NewGuid().ToString("N").Substring(0, 8);

        private static void Remove(string path)
        {
            if (File.Exists(path))
            {
                File.Delete(path);
            }
        }

        [Fact]
        public async Task App_screenshot_of_an_app_with_no_window_is_refused_in_words()
        {
            using TestApp app = TestApp.Start();
            await app.OpenAsync(Drawings.Plate(TestFolder.Make("shotapp")));
            await using McpHarness harness = await Attach(app, readOnly: true);

            string message = await harness.FailAsync("app_screenshot");

            Assert.Contains("no window", message);
            Assert.Contains("render_png", message);
            Assert.Contains("from 0 to 16384", await harness.FailAsync("app_screenshot", ("max_side", 20000)));
        }

        [Fact]
        public async Task Last_errors_and_the_screenshot_ask_the_app_about_itself_and_need_no_drawing_open()
        {
            using TestApp app = TestApp.Start();
            await using McpHarness harness = await Attach(app, readOnly: true);

            JsonNode errors = await harness.CallAsync("last_errors");
            string shot = await harness.FailAsync("app_screenshot");

            Assert.Contains("writes no crash log", (string)errors["note"]);
            Assert.Contains("no window", shot);
            Assert.Contains("no drawing open", await harness.FailAsync("info"));
        }

        [Fact]
        public async Task Command_history_of_the_app_shows_what_was_typed_in_it_and_works_under_read_only()
        {
            using TestApp app = TestApp.Start();
            string path = Drawings.Plate(TestFolder.Make("historyapp"));
            await using McpHarness typist = await Attach(app, scripts: true);
            await typist.CallAsync("open_drawing", ("path", path));
            await typist.CallAsync("run_command", ("text", "LINE 0,0 30,40" + Drawings.TwoEnters));

            await using McpHarness reader = await Attach(app, readOnly: true);
            JsonNode history = await reader.CallAsync("command_history", ("count", 50));

            string all = string.Join("\n", history["lines"].AsArray().Select(l => (string)l));
            Assert.Contains("LINE", all);
            Assert.Contains("30,40", all);
            Assert.False((bool)history["running"]);
            Assert.Equal(history["lines"].AsArray().Count, Math.Min(50, (int)history["total"]));
            Assert.Single((await reader.CallAsync("command_history", ("count", 1)))["lines"].AsArray());
        }

        [Fact]
        public async Task Last_errors_of_an_app_that_writes_no_crash_log_says_so_and_lists_the_servers_own_failures()
        {
            using TestApp app = TestApp.Start();
            await app.OpenAsync(Drawings.Plate(TestFolder.Make("errorsapp")));
            await using McpHarness harness = await Attach(app, readOnly: true);
            await harness.FailAsync("erase", ("handles", new[] { "FFFF" }));
            await harness.FailAsync("bounds", ("handles", new[] { "FFFF" }), ("page", 0));

            JsonNode errors = await harness.CallAsync("last_errors");

            Assert.Null(errors["crashLog"]);
            Assert.Empty(errors["app"].AsArray());
            Assert.Contains("writes no crash log", (string)errors["note"]);
            Assert.Equal("bounds", (string)errors["server"][0]["where"]);
        }

        [Fact]
        public async Task Dump_drawing_of_the_app_holds_the_changes_not_saved_and_leaves_the_apps_file_alone()
        {
            using TestApp app = TestApp.Start();
            string path = Drawings.Plate(TestFolder.Make("dumpapp"));
            byte[] own = File.ReadAllBytes(path);
            await using McpHarness editor = await Attach(app);
            await editor.CallAsync("open_drawing", ("path", path));
            await editor.CallAsync("add_line", ("start", new[] { 0.0, 0.0 }), ("end", new[] { 9.0, 9.0 }));

            await using McpHarness reader = await Attach(app, readOnly: true);
            JsonNode dump = await reader.CallAsync("dump_drawing", ("path", Unique("app") + ".dxf"));
            string file = (string)dump["path"];

            try
            {
                Assert.Equal("app", (string)dump["mode"]);
                Assert.Equal(5, CadDocument.Open(file).ModelSpace.Block.Count(e => !e.IsErased));
                Assert.Equal(own, File.ReadAllBytes(path));
                Assert.True((bool)(await reader.CallAsync("info"))["modified"]);
            }
            finally
            {
                Remove(file);
            }
        }

        [Fact]
        public async Task Compare_drawings_of_the_app_against_an_earlier_dump_finds_the_line_added_since()
        {
            using TestApp app = TestApp.Start();
            string path = Drawings.Plate(TestFolder.Make("compareapp"));
            await using McpHarness harness = await Attach(app);
            await harness.CallAsync("open_drawing", ("path", path));
            string before = (string)
                (await harness.CallAsync("dump_drawing", ("path", Unique("before") + ".dxf")))["path"];

            try
            {
                Assert.True((bool)(await harness.CallAsync("compare_drawings", ("a", before)))["same"]);
                JsonNode line = await harness.CallAsync(
                    "add_line",
                    ("start", new[] { 0.0, 0.0 }),
                    ("end", new[] { 9.0, 9.0 })
                );

                JsonNode diff = await harness.CallAsync("compare_drawings", ("a", before));

                Assert.Equal(1, (int)diff["addedCount"]);
                Assert.Equal(0, (int)diff["removedCount"]);
                Assert.Equal((string)line["handle"], (string)diff["added"][0]["handle"]);
            }
            finally
            {
                Remove(before);
            }
        }

        [Fact]
        public async Task Audit_purge_plot_and_save_as_of_the_app_work_on_the_app_as_it_stands_and_never_touch_its_file()
        {
            using TestApp app = TestApp.Start();
            string folder = TestFolder.Make("outputapp");
            string path = Drawings.Plate(folder);
            byte[] own = File.ReadAllBytes(path);
            await using McpHarness harness = await Attach(app);
            await harness.CallAsync("open_drawing", ("path", path));
            await harness.CallAsync("add_layer", ("name", "Spare"));
            await harness.CallAsync("add_text", ("text", "UNSAVED"), ("at", new[] { 20.0, 70.0 }), ("height", 6.0));

            Assert.Equal(0, (int)(await harness.CallAsync("audit"))["found"]);

            JsonNode plot = await harness.CallAsync(
                "plot_pdf",
                ("path", Path.Combine(folder, "app.pdf")),
                ("layout", "Model")
            );
            Assert.Contains("UNSAVED", PdfWords.Of(File.ReadAllBytes((string)plot["path"])));

            JsonNode copy = await harness.CallAsync(
                "save_as",
                ("path", Path.Combine(folder, "app-copy.dwg")),
                ("version", "2013")
            );
            Assert.Equal("AC1027", Encoding.ASCII.GetString(File.ReadAllBytes((string)copy["path"]), 0, 6));
            JsonNode info = await harness.CallAsync("info");
            Assert.Equal(path, (string)info["path"]);
            Assert.True((bool)info["modified"]);
            Assert.Contains("open drawing's own file", await harness.FailAsync("save_as", ("path", path)));

            JsonNode purged = await harness.CallAsync("purge", ("kinds", new[] { "layers" }));
            Assert.Equal(1, (int)purged["purged"]["layers"]);
            Assert.DoesNotContain((await harness.CallAsync("list_layers")).AsArray(), l =>
                (string)l["name"] == "Spare"
            );
            await harness.CallAsync("undo");
            Assert.Contains((await harness.CallAsync("list_layers")).AsArray(), l => (string)l["name"] == "Spare");
            Assert.Equal(own, File.ReadAllBytes(path));
        }

        [Fact]
        public async Task Purge_and_audit_fix_wait_for_an_app_with_a_command_running_and_read_only_refuses_every_writer()
        {
            using TestApp app = TestApp.Start();
            string folder = TestFolder.Make("busyapp");
            string path = Drawings.Plate(folder);
            await using McpHarness harness = await Attach(app, scripts: true);
            await harness.CallAsync("open_drawing", ("path", path));
            await harness.CallAsync("run_command", ("text", "LINE 0,0"));

            Assert.Contains("A command is running", await harness.FailAsync("purge", ("kinds", new[] { "layers" })));
            Assert.True((bool)(await harness.CallAsync("command_history"))["running"]);
            await harness.CallAsync("run_command", ("text", "5,5" + Drawings.TwoEnters));

            await using McpHarness readOnly = await Attach(app, readOnly: true);

            foreach (
                (string tool, (string, object)[] arguments) in new (string, (string, object)[])[]
                {
                    ("plot_pdf", new (string, object)[] { ("path", Path.Combine(folder, "a.pdf")) }),
                    ("export_svg", new (string, object)[] { ("path", Path.Combine(folder, "a.svg")) }),
                    ("save_as", new (string, object)[] { ("path", Path.Combine(folder, "a.dxf")) }),
                    ("purge", new (string, object)[] { ("kinds", new[] { "layers" }) }),
                    ("audit", new (string, object)[] { ("fix", true) }),
                    ("zoom_extents", Array.Empty<(string, object)>()),
                    ("zoom_window", new (string, object)[] { ("box", new[] { 0.0, 0.0, 9.0, 9.0 }) }),
                }
            )
            {
                Assert.Contains("--read-only", await readOnly.FailAsync(tool, arguments));
            }

            Assert.Empty(Directory.GetFiles(folder, "a.*"));
            Assert.Equal(0, (int)(await readOnly.CallAsync("audit"))["found"]);
        }

        [Fact]
        public async Task Zoom_of_the_app_moves_the_apps_view_and_gives_the_part_now_shown()
        {
            using TestApp app = TestApp.Start();
            await using McpHarness harness = await Attach(app);
            await harness.CallAsync("open_drawing", ("path", Drawings.Plate(TestFolder.Make("zoomapp"))));

            JsonNode window = await harness.CallAsync("zoom_window", ("box", new[] { 0.0, 0.0, 10.0, 10.0 }));

            Assert.Equal("app", (string)window["view"]);
            Assert.True((double)window["box"][0] <= 0.0 && (double)window["box"][2] >= 10.0);
            Assert.True((double)window["box"][3] - (double)window["box"][1] >= 10.0 - 1e-6);

            string circle = (string)
                (await harness.CallAsync("query_entities", ("kinds", new[] { "CIRCLE" })))["entities"][0]["handle"];
            JsonNode onObject = await harness.CallAsync("zoom_object", ("handles", new[] { circle }));
            Assert.True((double)onObject["box"][0] < 100.0 && (double)onObject["box"][2] > 100.0);

            JsonNode everything = await harness.CallAsync("zoom_extents");
            Assert.True((double)everything["box"][0] <= 0.0 && (double)everything["box"][2] >= 200.0);
        }

        [Fact]
        public async Task Timings_of_the_app_time_a_regen_a_render_and_the_reading_of_its_file()
        {
            using TestApp app = TestApp.Start();
            await app.OpenAsync(Drawings.Plate(TestFolder.Make("timingsapp")));
            await using McpHarness harness = await Attach(app, readOnly: true);

            foreach (string what in new[] { "regen", "render", "open" })
            {
                JsonNode timing = await harness.CallAsync("timings", ("what", what), ("runs", 2));

                Assert.Equal("app", (string)timing["mode"]);
                Assert.Equal(2, timing["ms"].AsArray().Count);
                Assert.True((double)timing["medianMs"] >= 0.0);
            }
        }

        [Fact]
        public async Task A_debug_session_on_the_app_read_only_finds_the_text_over_a_line_dumps_the_drawing_and_reads_the_commands_that_made_it()
        {
            using TestApp app = TestApp.Start();
            string path = Drawings.Plate(TestFolder.Make("session"));
            string dump = null;

            // The user's work: a drawing, and a text typed across the line of the plate.
            await using (McpHarness user = await Attach(app, scripts: true))
            {
                await user.CallAsync("open_drawing", ("path", path));
                await user.CallAsync("run_command", ("text", "TEXT 80,48 5 0" + "\n" + "OVER" + "\n\n"));
                await user.CallAsync("run_command", ("text", "LINE 0,0 30,40" + Drawings.TwoEnters));
            }

            // The debugger: attached read-only.
            await using McpHarness debug = await Attach(app, readOnly: true);

            try
            {
                JsonNode info = await debug.CallAsync("info");
                Assert.True((bool)info["readOnly"]);
                Assert.Equal("app", (string)info["mode"]);

                JsonNode over = await debug.CallAsync("overlaps");
                Assert.True((int)over["total"] >= 1, "The text across the line is not found.");
                string word = (string)over["rows"][0]["word"];
                string other = (string)over["rows"][0]["other"];
                JsonNode texts = await debug.CallAsync("query_entities", ("kinds", new[] { "TEXT" }));
                Assert.Contains(texts["entities"].AsArray(), e => (string)e["handle"] == word);
                JsonNode box = await debug.CallAsync("bounds", ("handles", new[] { word, other }));
                Assert.Equal(2, box["rows"].AsArray().Count);

                // The picture of the canvas is the app's window; this app has none and says so, and render_png draws the same part.
                Assert.Contains("no window", await debug.FailAsync("app_screenshot"));
                var render = await debug.CallRawAsync(
                    "render_png",
                    ("box", new[] { 60.0, 30.0, 120.0, 70.0 }),
                    ("width", 600)
                );
                Assert.True(render.IsError != true);

                JsonNode dumped = await debug.CallAsync("dump_drawing", ("path", Unique("session") + ".dxf"));
                dump = (string)dumped["path"];
                Assert.Equal(6, CadDocument.Open(dump).ModelSpace.Block.Count(e => !e.IsErased));

                JsonNode history = await debug.CallAsync("command_history", ("count", 100));
                string commands = string.Join("\n", history["lines"].AsArray().Select(l => (string)l));
                Assert.Contains("TEXT", commands);
                Assert.Contains("OVER", commands);
                Assert.Contains("LINE", commands);

                Assert.Equal(0, (int)(await debug.CallAsync("audit"))["found"]);
                Assert.Contains(
                    "--read-only",
                    await debug.FailAsync("move", ("handles", new[] { word }), ("delta", new[] { 0.0, 20.0 }))
                );
                Assert.True((bool)(await debug.CallAsync("compare_drawings", ("a", dump)))["same"]);
                Assert.Equal(6, (int)(await debug.CallAsync("info"))["entityCount"]);
            }
            finally
            {
                if (dump != null)
                {
                    Remove(dump);
                }
            }
        }
    }
}
