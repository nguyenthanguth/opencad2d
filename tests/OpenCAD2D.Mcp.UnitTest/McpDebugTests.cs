using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json.Nodes;
using System.Threading.Tasks;
using Xunit;

namespace OpenCAD2D.Mcp.UnitTest
{
    /// <summary>The debug tools over a drawing file: the command line, the faults, a dump, a comparison and the timings.</summary>
    public class McpDebugTests
    {
        private static Task<McpHarness> Plate(string name, bool readOnly = false, bool scripts = false) =>
            McpHarness.StartAsync(
                new McpOptions
                {
                    File = Drawings.Plate(TestFolder.Make(name)),
                    ReadOnly = readOnly,
                    AllowScripts = scripts,
                }
            );

        /// <summary>A name no other test of the run uses, so that dumps of parallel tests never meet.</summary>
        private static string Unique(string stem) => stem + "-" + Guid.NewGuid().ToString("N").Substring(0, 8);

        private static void Remove(string path)
        {
            if (File.Exists(path))
            {
                File.Delete(path);
            }
        }

        [Fact]
        public async Task App_screenshot_on_a_file_is_refused_in_words_that_name_render_png()
        {
            await using McpHarness harness = await Plate("shotfile");

            string message = await harness.FailAsync("app_screenshot");

            Assert.Contains("running app", message);
            Assert.Contains("render_png", message);
        }

        [Fact]
        public async Task Last_errors_works_before_any_drawing_is_open()
        {
            await using McpHarness harness = await McpHarness.StartAsync(new McpOptions());

            await harness.FailAsync("open_drawing", ("path", Path.Combine(TestFolder.Make("nothing"), "missing.dxf")));
            JsonNode errors = await harness.CallAsync("last_errors");

            Assert.Equal("open or new drawing", (string)errors["server"][0]["where"]);
            Assert.Contains("missing.dxf", (string)errors["server"][0]["text"][0]);
        }

        [Fact]
        public async Task Command_history_shows_the_commands_a_file_session_ran_newest_last()
        {
            await using McpHarness harness = await Plate("history", scripts: true);
            Assert.Equal(0, (int)(await harness.CallAsync("command_history"))["total"]);

            await harness.CallAsync("run_command", ("text", "LINE 0,0 30,40" + Drawings.TwoEnters));
            JsonNode history = await harness.CallAsync("command_history");

            string all = string.Join("\n", history["lines"].AsArray().Select(l => (string)l));
            Assert.Contains("LINE", all);
            Assert.Contains("30,40", all);
            Assert.False((bool)history["running"]);
            Assert.True((int)history["total"] >= 3);

            JsonNode last = await harness.CallAsync("command_history", ("count", 1));
            Assert.Single(last["lines"].AsArray());
            Assert.Equal((string)history["lines"].AsArray().Last(), (string)last["lines"][0]);
            Assert.Contains("count is from 1 to 1000", await harness.FailAsync("command_history", ("count", 0)));
        }

        [Fact]
        public async Task Last_errors_lists_the_tool_calls_that_failed_newest_first_and_says_there_is_no_app()
        {
            await using McpHarness harness = await Plate("errors");
            Assert.Empty((await harness.CallAsync("last_errors"))["server"].AsArray());

            await harness.FailAsync("move", ("handles", new[] { "FFFF" }), ("delta", new[] { 1.0, 1.0 }));
            await harness.FailAsync("erase", ("handles", new[] { "FFFE" }));
            JsonNode errors = await harness.CallAsync("last_errors");

            JsonArray server = errors["server"].AsArray();
            Assert.Equal(2, server.Count);
            Assert.Equal("erase", (string)server[0]["where"]);
            Assert.Equal("move", (string)server[1]["where"]);
            Assert.Equal("tool refused", (string)server[0]["source"]);
            Assert.Contains("FFFE", (string)server[0]["text"][0]);
            Assert.Empty(errors["app"].AsArray());
            Assert.Contains("no app", (string)errors["note"]);
            Assert.Single((await harness.CallAsync("last_errors", ("count", 1)))["server"].AsArray());
        }

        [Fact]
        public async Task Dump_drawing_saves_the_drawing_as_it_is_now_in_the_debug_folder_and_it_reads_back()
        {
            await using McpHarness harness = await Plate("dump", readOnly: true);
            string name = Unique("dump") + ".dxf";

            JsonNode dump = await harness.CallAsync("dump_drawing", ("path", name));

            string path = (string)dump["path"];

            try
            {
                string folder = Path.Combine(Path.GetTempPath(), "OpenCAD2D", "mcp-debug");
                Assert.Equal(Path.GetFullPath(Path.Combine(folder, name)), path);
                Assert.Equal("file", (string)dump["mode"]);
                Assert.Equal(new FileInfo(path).Length, (long)dump["bytes"]);
                Assert.Equal(4, CadDocument.Open(path).ModelSpace.Block.Count(e => !e.IsErased));
            }
            finally
            {
                Remove(path);
            }
        }

        [Fact]
        public async Task Dump_drawing_has_a_default_name_and_never_writes_outside_the_debug_folder_or_the_drawings_own_file()
        {
            await using McpHarness harness = await Plate("dumprules");
            JsonNode info = await harness.CallAsync("info");

            JsonNode named = await harness.CallAsync("dump_drawing");
            string path = (string)named["path"];

            try
            {
                Assert.StartsWith("plate-", Path.GetFileName(path));
                Assert.EndsWith(".dxf", path);
                string elsewhere = Path.Combine(TestFolder.Make("dumpelsewhere"), "x.dxf");
                Assert.Contains(
                    "inside the debug folder",
                    await harness.FailAsync("dump_drawing", ("path", elsewhere))
                );
                Assert.False(File.Exists(elsewhere));
                Assert.Contains(
                    "inside the debug folder",
                    await harness.FailAsync("dump_drawing", ("path", "..\\up.dxf"))
                );
                Assert.Contains("DXF file", await harness.FailAsync("dump_drawing", ("path", "x.dwg")));

                // A dump opened as the drawing is a file the drawing owns: it is never overwritten by its own dump.
                await using McpHarness reopened = await McpHarness.StartAsync(new McpOptions { File = path });
                Assert.Contains("own file", await reopened.FailAsync("dump_drawing", ("path", path)));
                Assert.Equal((string)info["path"], (string)(await harness.CallAsync("info"))["path"]);
            }
            finally
            {
                Remove(path);
            }
        }

        [Fact]
        public async Task Compare_drawings_reports_what_was_added_removed_and_changed_by_handle_with_how_far_a_box_moved()
        {
            await using McpHarness harness = await Plate("compare");
            string name = Unique("before") + ".dxf";
            string before = (string)(await harness.CallAsync("dump_drawing", ("path", name)))["path"];

            try
            {
                JsonNode same = await harness.CallAsync("compare_drawings", ("a", before));
                Assert.True((bool)same["same"]);
                Assert.Equal(4, (int)same["unchanged"]);

                string circle = (string)
                    (await harness.CallAsync("query_entities", ("kinds", new[] { "CIRCLE" })))["entities"][0]["handle"];
                string text = (string)
                    (await harness.CallAsync("query_entities", ("kinds", new[] { "TEXT" })))["entities"][0]["handle"];
                await harness.CallAsync("move", ("handles", new[] { circle }), ("delta", new[] { 10.0, 5.0 }));
                await harness.CallAsync("erase", ("handles", new[] { text }));
                JsonNode added = await harness.CallAsync(
                    "add_line",
                    ("start", new[] { 0.0, 0.0 }),
                    ("end", new[] { 9.0, 0.0 })
                );

                JsonNode diff = await harness.CallAsync("compare_drawings", ("a", before));

                Assert.False((bool)diff["same"]);
                Assert.Equal(1, (int)diff["addedCount"]);
                Assert.Equal(1, (int)diff["removedCount"]);
                Assert.Equal(1, (int)diff["changedCount"]);
                Assert.Equal(2, (int)diff["unchanged"]);
                Assert.Equal((string)added["handle"], (string)diff["added"][0]["handle"]);
                Assert.Equal("LINE", (string)diff["added"][0]["kind"]);
                Assert.Equal(text, (string)diff["removed"][0]["handle"]);
                Assert.Equal("TEXT", (string)diff["removed"][0]["kind"]);
                JsonNode moved = diff["changed"][0];
                Assert.Equal(circle, (string)moved["handle"]);
                Assert.Contains(moved["properties"].AsArray(), p => (string)p == "box");
                Assert.Equal(10.0, (double)moved["movedBy"][0], 3);
                Assert.Equal(5.0, (double)moved["movedBy"][1], 3);
                Assert.Equal(80.0, (double)moved["boxBefore"][0], 3);
                Assert.Equal(90.0, (double)moved["boxAfter"][0], 3);

                // The other way round, what was added is removed.
                JsonNode back = await harness.CallAsync("compare_drawings", ("a", "current"), ("b", before));
                Assert.Equal(1, (int)back["removedCount"]);
                Assert.Equal(1, (int)back["addedCount"]);
                Assert.Equal((string)added["handle"], (string)back["removed"][0]["handle"]);
            }
            finally
            {
                Remove(before);
            }
        }

        [Fact]
        public async Task Compare_drawings_cuts_a_long_list_at_max_rows_and_counts_everything()
        {
            await using McpHarness harness = await Plate("comparecut");
            string name = Unique("cut") + ".dxf";
            string before = (string)(await harness.CallAsync("dump_drawing", ("path", name)))["path"];

            try
            {
                for (int i = 0; i < 5; i++)
                {
                    await harness.CallAsync("add_line", ("start", new[] { 0.0, i }), ("end", new[] { 9.0, i }));
                }

                JsonNode diff = await harness.CallAsync("compare_drawings", ("a", before), ("max_rows", 2));

                Assert.Equal(5, (int)diff["addedCount"]);
                Assert.Equal(2, diff["added"].AsArray().Count);
                Assert.True((bool)diff["truncated"]);
                Assert.Contains(
                    "1 to 500",
                    await harness.FailAsync("compare_drawings", ("a", before), ("max_rows", 0))
                );
                Assert.Contains(
                    "there is no file",
                    await harness.FailAsync("compare_drawings", ("a", before + ".missing"))
                );
                Assert.Contains("current, or the path", await harness.FailAsync("compare_drawings", ("a", "")));
            }
            finally
            {
                Remove(before);
            }
        }

        [Fact]
        public async Task Timings_give_the_milliseconds_of_each_run_and_the_median()
        {
            await using McpHarness harness = await Plate("timings");

            foreach (string what in new[] { "regen", "render", "open" })
            {
                JsonNode timing = await harness.CallAsync("timings", ("what", what), ("runs", 3));

                Assert.Equal(what, (string)timing["what"]);
                Assert.Equal(3, timing["ms"].AsArray().Count);
                Assert.All(timing["ms"].AsArray(), t => Assert.True((double)t >= 0.0));
                double[] sorted = timing["ms"].AsArray().Select(t => (double)t).OrderBy(t => t).ToArray();
                Assert.Equal(sorted[1], (double)timing["medianMs"], 2);
                Assert.NotEmpty((string)timing["note"]);
            }

            Assert.Equal(4, (int)(await harness.CallAsync("timings", ("what", "render")))["entities"]);
            Assert.Contains("regen, render or open", await harness.FailAsync("timings", ("what", "plot")));
            Assert.Contains("1 to 20", await harness.FailAsync("timings", ("what", "regen"), ("runs", 21)));
        }

        [Fact]
        public async Task Timings_of_open_needs_a_file_the_drawing_has()
        {
            await using McpHarness harness = await McpHarness.StartAsync(new McpOptions());
            await harness.CallAsync("new_drawing");

            Assert.Contains("no file yet", await harness.FailAsync("timings", ("what", "open")));
            Assert.True((double)(await harness.CallAsync("timings", ("what", "regen")))["medianMs"] >= 0.0);
        }

        [Fact]
        public async Task The_debug_tools_that_only_read_work_under_read_only_and_none_of_them_changes_the_drawing()
        {
            await using McpHarness harness = await Plate("debugreadonly", readOnly: true);
            string name = Unique("ro") + ".dxf";
            var tools = new List<string>();

            JsonNode dump = await harness.CallAsync("dump_drawing", ("path", name));
            tools.Add("dump_drawing");
            await harness.CallAsync("command_history");
            tools.Add("command_history");
            await harness.CallAsync("last_errors");
            tools.Add("last_errors");
            await harness.CallAsync("compare_drawings", ("a", (string)dump["path"]));
            tools.Add("compare_drawings");
            await harness.CallAsync("timings", ("what", "regen"));
            tools.Add("timings");

            Assert.Equal(5, tools.Count);
            Assert.False((bool)(await harness.CallAsync("info"))["modified"]);
            Remove((string)dump["path"]);
        }
    }
}
