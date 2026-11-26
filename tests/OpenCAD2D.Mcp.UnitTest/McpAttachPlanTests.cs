using System.Text.Json.Nodes;
using System.Threading.Tasks;
using Xunit;

namespace OpenCAD2D.Mcp.UnitTest
{
    /// <summary>
    /// <c>run_plan</c> on a drawing of an app with no window (the Remote test server): the writers of a plan are one UNDO group of
    /// the app, and a plan that stops is put back by the snapshot taken before it.
    /// </summary>
    public class McpAttachPlanTests
    {
        private static async Task<McpHarness> Attach(
            TestApp app,
            string name,
            bool readOnly = false,
            bool scripts = false
        )
        {
            string path = Drawings.Plate(TestFolder.Make(name));

            if (readOnly)
            {
                await app.OpenAsync(path);
            }

            McpHarness harness = await McpHarness.StartAsync(
                new McpOptions
                {
                    Attach = true,
                    RemoteFolder = app.Folder,
                    ReadOnly = readOnly,
                    AllowScripts = scripts,
                }
            );

            if (!readOnly)
            {
                await harness.CallAsync("open_drawing", ("path", path));
            }

            return harness;
        }

        private static async Task<JsonNode> Plan(
            McpHarness harness,
            string steps,
            bool dryRun = false,
            bool stop = true
        ) =>
            await harness.CallAsync(
                "run_plan",
                ("steps", JsonNode.Parse(steps)),
                ("dry_run", dryRun),
                ("stop_on_error", stop)
            );

        private static async Task<int> Count(McpHarness harness) =>
            (int)(await harness.CallAsync("info"))["entityCount"];

        private const string AddsAndMove =
            "[{\"tool\":\"add_line\",\"id\":\"l\",\"arguments\":{\"start\":[0,0],\"end\":[50,0]}},"
            + "{\"tool\":\"add_circle\",\"id\":\"c\",\"arguments\":{\"center\":[25,25],\"radius\":5}},"
            + "{\"tool\":\"move\",\"arguments\":{\"handles\":[\"$l.handle\",\"$c.handle\"],\"delta\":[10,10]}}]";

        [Fact]
        public async Task Attached_a_plan_of_adds_and_a_move_is_one_undo_step_of_the_app()
        {
            using TestApp app = TestApp.Start();
            await using McpHarness harness = await Attach(app, "attplan");
            Assert.Equal(4, await Count(harness));

            JsonNode result = await Plan(harness, AddsAndMove);

            Assert.True((bool)result["ok"], (string)result["message"]);
            Assert.True((bool)result["changed"]);
            Assert.Equal(6, await Count(harness));
            string line = (string)result["steps"][0]["result"]["handle"];
            JsonNode box = await harness.CallAsync("bounds", ("handles", new[] { line }));
            Assert.Equal(10.0, (double)box["rows"][0]["box"][0], 3);
            Assert.Equal(60.0, (double)box["rows"][0]["box"][2], 3);

            Assert.True((bool)(await harness.CallAsync("undo"))["undone"]);
            Assert.Equal(4, await Count(harness));
            Assert.Equal(1, (int)(await harness.CallAsync("info"))["counts"]["CIRCLE"]);

            // The undo group is closed: the next two calls are two steps again.
            await harness.CallAsync("add_line", ("start", new[] { 0.0, 0.0 }), ("end", new[] { 1.0, 1.0 }));
            await harness.CallAsync("add_line", ("start", new[] { 0.0, 0.0 }), ("end", new[] { 2.0, 2.0 }));
            await harness.CallAsync("undo");
            Assert.Equal(5, await Count(harness));
        }

        [Fact]
        public async Task Attached_a_step_that_fails_puts_the_app_back_as_it_was()
        {
            using TestApp app = TestApp.Start();
            await using McpHarness harness = await Attach(app, "attfail");
            await harness.CallAsync("add_line", ("start", new[] { 0.0, 0.0 }), ("end", new[] { 5.0, 5.0 }));
            Assert.Equal(5, await Count(harness));

            JsonNode result = await Plan(
                harness,
                "[{\"tool\":\"add_line\",\"arguments\":{\"start\":[0,0],\"end\":[50,0]}},"
                    + "{\"tool\":\"add_circle\",\"arguments\":{\"center\":[5,5],\"radius\":2}},"
                    + "{\"tool\":\"erase\",\"arguments\":{\"handles\":[\"FFFFF\"]}},"
                    + "{\"tool\":\"add_circle\",\"arguments\":{\"center\":[9,9],\"radius\":2}}]"
            );

            Assert.False((bool)result["ok"]);
            Assert.True((bool)result["rolledBack"]);
            Assert.False((bool)result["changed"]);
            Assert.True((bool)result["steps"][3]["skipped"]);
            Assert.Equal(5, await Count(harness));

            // What the user did before the plan can still be undone, one step: the plan left nothing in the history.
            Assert.True((bool)(await harness.CallAsync("undo"))["undone"]);
            Assert.Equal(4, await Count(harness));
        }

        [Fact]
        public async Task Attached_a_dry_run_changes_nothing_and_gives_the_handles_a_real_run_gives()
        {
            using TestApp app = TestApp.Start();
            await using McpHarness harness = await Attach(app, "attdry");

            JsonNode dry = await Plan(harness, AddsAndMove, dryRun: true);

            Assert.True((bool)dry["ok"], (string)dry["message"]);
            Assert.True((bool)dry["rolledBack"]);
            Assert.Equal(4, await Count(harness));

            JsonNode real = await Plan(harness, AddsAndMove);
            Assert.Equal(6, await Count(harness));
            Assert.Equal((string)dry["steps"][0]["result"]["handle"], (string)real["steps"][0]["result"]["handle"]);
            Assert.Equal((string)dry["steps"][1]["result"]["handle"], (string)real["steps"][1]["result"]["handle"]);
        }

        [Fact]
        public async Task Attached_the_refusals_are_the_same_and_a_plan_that_reads_runs_on_a_read_only_server()
        {
            using TestApp app = TestApp.Start();
            await using McpHarness readOnly = await Attach(app, "attro", readOnly: true);

            string message = await readOnly.FailAsync("run_plan", ("steps", JsonNode.Parse(AddsAndMove)));
            Assert.Contains("--read-only", message);
            Assert.Equal(4, await Count(readOnly));

            JsonNode reads = await Plan(readOnly, "[{\"tool\":\"info\"},{\"tool\":\"list_layers\"}]");
            Assert.True((bool)reads["ok"]);
            Assert.False((bool)reads["changed"]);

            await using McpHarness scriptless = await Attach(app, "attscripts");
            JsonNode refused = await Plan(
                scriptless,
                "[{\"tool\":\"add_line\",\"arguments\":{\"start\":[0,0],\"end\":[1,1]}},"
                    + "{\"tool\":\"run_command\",\"arguments\":{\"text\":\"LINE 0,0 9,9\"}}]"
            );
            Assert.False((bool)refused["ok"]);
            Assert.Contains("--allow-scripts", (string)refused["steps"][1]["error"]);
            Assert.Equal(4, await Count(scriptless));
        }

        [Fact]
        public async Task Attached_the_placement_loop_as_plans_takes_4_calls()
        {
            using TestApp app = TestApp.Start();
            await using McpHarness harness = await Attach(app, "attloop");

            int calls = await PlanPlacementLoop.RunAsync(harness);

            Assert.Equal(4, calls);
            JsonNode tags = await harness.CallAsync("query_entities", ("kinds", new[] { "TEXT" }));
            Assert.Equal(3, tags["entities"].AsArray().Count);
        }

        [Fact]
        public async Task Attached_a_plan_never_types_into_a_command_that_is_running_and_run_command_is_not_allowed_in_it()
        {
            using TestApp app = TestApp.Start();
            await using McpHarness harness = await Attach(app, "attbusy", scripts: true);

            JsonNode refused = await Plan(
                harness,
                "[{\"tool\":\"run_command\",\"arguments\":{\"text\":\"CIRCLE 50,50 5\\n\\n\"}}]"
            );
            Assert.False((bool)refused["ok"]);
            Assert.Contains("not allowed in a plan on an app", (string)refused["steps"][0]["error"]);
            Assert.Equal(4, await Count(harness));

            // A command waiting for the user's answer in the app: the plan is refused before anything is typed.
            await harness.CallAsync("run_command", ("text", "LINE 0,0"));
            string message = await harness.FailAsync("run_plan", ("steps", JsonNode.Parse(AddsAndMove)));
            Assert.Contains("busy", message);
            Assert.Contains("Nothing was typed", message);

            // The command is still waiting for its next point, as it was: one answer ends it with a line.
            await harness.CallAsync("run_command", ("text", "10,10\n\n"));
            Assert.Equal(5, await Count(harness));
        }
    }
}
