using System;
using System.Linq;
using System.Text.Json.Nodes;
using System.Threading.Tasks;
using Xunit;

namespace OpenCAD2D.Mcp.UnitTest
{
    /// <summary>
    /// <c>run_plan</c> over a drawing file: many calls in one, the references between them, one undo step, the way back for a
    /// step that fails, a dry run, and the refusals of a read-only or script-less server.
    /// </summary>
    public class PlanToolTests
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

        private static JsonNode Steps(string json) => JsonNode.Parse(json);

        private static async Task<JsonNode> Plan(
            McpHarness harness,
            string steps,
            bool dryRun = false,
            bool stop = true
        ) => await harness.CallAsync("run_plan", ("steps", Steps(steps)), ("dry_run", dryRun), ("stop_on_error", stop));

        private static async Task<int> Count(McpHarness harness) =>
            (int)(await harness.CallAsync("info"))["entityCount"];

        private const string AddsAndMove =
            "[{\"tool\":\"add_line\",\"id\":\"l\",\"arguments\":{\"start\":[0,0],\"end\":[50,0]}},"
            + "{\"tool\":\"add_circle\",\"id\":\"c\",\"arguments\":{\"center\":[25,25],\"radius\":5}},"
            + "{\"tool\":\"move\",\"arguments\":{\"handles\":[\"$l.handle\",\"$c.handle\"],\"delta\":[10,10]}}]";

        [Fact]
        public async Task A_plan_of_adds_and_a_move_runs_in_one_call_and_one_undo_takes_it_all_back()
        {
            await using McpHarness harness = await Plate("planone");
            Assert.Equal(4, await Count(harness));

            JsonNode result = await Plan(harness, AddsAndMove);

            Assert.True((bool)result["ok"], (string)result["message"]);
            Assert.False((bool)result["dryRun"]);
            Assert.False((bool)result["rolledBack"]);
            Assert.True((bool)result["changed"]);
            Assert.Equal(3, (int)result["totals"]["steps"]);
            Assert.Equal(3, (int)result["totals"]["ok"]);
            Assert.Equal(3, (int)result["totals"]["writers"]);
            Assert.Equal(new[] { "l", "c", "s3" }, result["steps"].AsArray().Select(s => (string)s["id"]).ToArray());
            Assert.Equal(6, await Count(harness));

            string line = (string)result["steps"][0]["result"]["handle"];
            JsonNode box = await harness.CallAsync("bounds", ("handles", new[] { line }));
            Assert.Equal(10.0, (double)box["rows"][0]["box"][0], 3);
            Assert.Equal(60.0, (double)box["rows"][0]["box"][2], 3);
            Assert.Equal(10.0, (double)box["rows"][0]["box"][1], 3);

            JsonNode undone = await harness.CallAsync("undo");
            Assert.True((bool)undone["undone"]);
            Assert.Equal(4, await Count(harness));
            Assert.False((bool)(await harness.CallAsync("undo"))["undone"]);
        }

        [Fact]
        public async Task A_tool_that_groups_itself_inside_a_plan_leaves_the_plan_one_undo_step()
        {
            await using McpHarness harness = await Plate("plangrouped");

            JsonNode result = await Plan(
                harness,
                "[{\"tool\":\"add_line\",\"arguments\":{\"start\":[0,0],\"end\":[50,0]}},"
                    + "{\"tool\":\"add_table\",\"arguments\":{\"at\":[100,100],\"columns\":2,\"rows\":1}},"
                    + "{\"tool\":\"add_line\",\"arguments\":{\"start\":[0,5],\"end\":[50,5]}}]"
            );

            Assert.True((bool)result["ok"], (string)result["message"]);
            Assert.Equal(7, await Count(harness));
            Assert.True((bool)(await harness.CallAsync("undo"))["undone"]);
            Assert.Equal(4, await Count(harness));
        }

        [Fact]
        public async Task A_step_takes_a_value_from_an_earlier_answer_by_its_id_and_path()
        {
            await using McpHarness harness = await Plate("planrefs");

            JsonNode result = await Plan(
                harness,
                "[{\"tool\":\"add_text\",\"id\":\"tag\",\"arguments\":{\"text\":\"TAG-1\",\"at\":[80,48],\"height\":3.5}},"
                    + "{\"tool\":\"measure_text\",\"id\":\"m\",\"arguments\":{\"text\":\"TAG-1\",\"at\":[80,48],\"height\":3.5}},"
                    + "{\"tool\":\"free_space\",\"id\":\"free\",\"arguments\":{\"frame\":[0,0,200,100],\"kinds\":[\"LINE\",\"CIRCLE\",\"LWPOLYLINE\",\"TEXT\"],\"margin\":2,"
                    + "\"min_width\":\"$m.frames[0].width\",\"min_height\":\"$m.frames.0.height\",\"page_size\":3}},"
                    + "{\"tool\":\"bounds\",\"id\":\"b\",\"arguments\":{\"handles\":[\"$tag.handle\"]}},"
                    + "{\"tool\":\"fits\",\"arguments\":{\"candidate\":\"$free.boxes[0]\",\"kinds\":[\"LINE\"]}},"
                    + "{\"tool\":\"add_text\",\"arguments\":{\"text\":\"$$5 each\",\"at\":[5,5],\"height\":3.5}}]"
            );

            Assert.True((bool)result["ok"], (string)result["message"]);
            JsonNode steps = result["steps"];
            double width = (double)steps[1]["result"]["frames"][0]["width"];
            Assert.True(width > 5.0);
            Assert.True((int)steps[2]["result"]["total"] > 0);
            Assert.Equal((string)steps[0]["result"]["handle"], (string)steps[3]["result"]["rows"][0]["handle"]);
            Assert.True((bool)steps[4]["result"]["fits"]);

            JsonNode texts = await harness.CallAsync("query_entities", ("kinds", new[] { "TEXT" }));
            Assert.Contains(texts["entities"].AsArray(), e => (string)e["geometry"]["text"] == "$5 each");
        }

        [Fact]
        public async Task A_bad_reference_is_found_before_anything_runs_or_when_it_is_read()
        {
            await using McpHarness harness = await Plate("planbadref");

            JsonNode forward = await Plan(
                harness,
                "[{\"tool\":\"move\",\"arguments\":{\"handles\":[\"$later.handle\"],\"delta\":[1,1]}},"
                    + "{\"tool\":\"add_line\",\"id\":\"later\",\"arguments\":{\"start\":[0,0],\"end\":[1,1]}}]"
            );
            Assert.False((bool)forward["ok"]);
            Assert.Contains("which is the step 2, after it", (string)forward["steps"][0]["error"]);
            Assert.True((bool)forward["steps"][1]["skipped"]);
            Assert.StartsWith("Nothing was run", (string)forward["message"]);

            JsonNode unknown = await Plan(
                harness,
                "[{\"tool\":\"move\",\"arguments\":{\"handles\":[\"$nobody.handle\"],\"delta\":[1,1]}}]"
            );
            Assert.Contains("no step has the id nobody", (string)unknown["steps"][0]["error"]);

            JsonNode shape = await Plan(
                harness,
                "[{\"tool\":\"add_line\",\"id\":\"a\",\"arguments\":{\"start\":[0,0],\"end\":[1,1]}},"
                    + "{\"tool\":\"move\",\"arguments\":{\"handles\":[\"$a..handle\"],\"delta\":[1,1]}}]"
            );
            Assert.Contains("starts like a reference but is not one", (string)shape["steps"][1]["error"]);

            JsonNode missing = await Plan(
                harness,
                "[{\"tool\":\"add_line\",\"id\":\"a\",\"arguments\":{\"start\":[0,0],\"end\":[1,1]}},"
                    + "{\"tool\":\"move\",\"arguments\":{\"handles\":[\"$a.handel\"],\"delta\":[1,1]}}]"
            );
            Assert.False((bool)missing["ok"]);
            Assert.True((bool)missing["rolledBack"]);
            string error = (string)missing["steps"][1]["error"];
            Assert.Contains("$a.handel", error);
            Assert.Contains("has no member handel", error);
            Assert.Contains("it has handle, kind", error);
            Assert.Equal(4, await Count(harness));
        }

        [Fact]
        public async Task A_step_that_fails_takes_the_whole_plan_back_and_the_later_steps_are_not_run()
        {
            await using McpHarness harness = await Plate("planfail");

            JsonNode result = await Plan(
                harness,
                "[{\"tool\":\"add_line\",\"id\":\"a\",\"arguments\":{\"start\":[0,0],\"end\":[50,0]}},"
                    + "{\"tool\":\"add_text\",\"id\":\"t\",\"arguments\":{\"text\":\"X\",\"at\":[1,1],\"height\":3,\"text_style\":\"NoSuchStyle\"}},"
                    + "{\"tool\":\"add_circle\",\"id\":\"c\",\"arguments\":{\"center\":[5,5],\"radius\":2}}]"
            );

            Assert.False((bool)result["ok"]);
            Assert.True((bool)result["rolledBack"]);
            Assert.False((bool)result["changed"]);
            Assert.Contains("taken back", (string)result["message"]);
            Assert.True((bool)result["steps"][0]["ok"]);
            Assert.False((bool)result["steps"][1]["ok"]);
            Assert.Contains("NoSuchStyle", (string)result["steps"][1]["error"]);
            Assert.True((bool)result["steps"][2]["skipped"]);
            Assert.Equal(1, (int)result["totals"]["failed"]);
            Assert.Equal(1, (int)result["totals"]["skipped"]);

            Assert.Equal(4, await Count(harness));
            Assert.False((bool)harness.Session.Backend.IsModified);
            Assert.False((bool)(await harness.CallAsync("undo"))["undone"]);
        }

        [Fact]
        public async Task A_tool_that_cancels_its_own_group_takes_the_whole_plan_back_with_it()
        {
            await using McpHarness harness = await Plate("plancancel");
            await harness.CallAsync("add_layer", ("name", "Lk"), ("locked", true));
            string locked = (string)
                (
                    await harness.CallAsync(
                        "add_line",
                        ("start", new[] { 0.0, 0.0 }),
                        ("end", new[] { 9.0, 9.0 }),
                        ("layer", "Lk")
                    )
                )["handle"];
            Assert.Equal(5, await Count(harness));

            JsonNode result = await Plan(
                harness,
                "[{\"tool\":\"add_circle\",\"arguments\":{\"center\":[5,5],\"radius\":2}},"
                    + "{\"tool\":\"set_properties\",\"arguments\":{\"handles\":[\""
                    + locked
                    + "\"],\"color\":3}},"
                    + "{\"tool\":\"add_line\",\"arguments\":{\"start\":[0,0],\"end\":[1,1]}}]",
                stop: false
            );

            Assert.False((bool)result["ok"]);
            Assert.True((bool)result["rolledBack"]);
            Assert.False((bool)result["changed"]);
            Assert.Contains("took the whole plan back", (string)result["message"]);
            Assert.True((bool)result["steps"][2]["skipped"]);
            Assert.Equal(5, await Count(harness));
        }

        [Fact]
        public async Task With_stop_on_error_false_the_steps_that_worked_stay_as_one_undo_step()
        {
            await using McpHarness harness = await Plate("plango");

            JsonNode result = await Plan(
                harness,
                "[{\"tool\":\"add_line\",\"arguments\":{\"start\":[0,0],\"end\":[50,0]}},"
                    + "{\"tool\":\"add_text\",\"arguments\":{\"text\":\"X\",\"at\":[1,1],\"height\":3,\"text_style\":\"NoSuchStyle\"}},"
                    + "{\"tool\":\"add_circle\",\"arguments\":{\"center\":[5,5],\"radius\":2}}]",
                stop: false
            );

            Assert.False((bool)result["ok"]);
            Assert.False((bool)result["rolledBack"]);
            Assert.True((bool)result["changed"]);
            Assert.Equal(new[] { true, false, true }, result["steps"].AsArray().Select(s => (bool)s["ok"]).ToArray());
            Assert.Equal(0, (int)result["totals"]["skipped"]);
            Assert.Equal(6, await Count(harness));

            await harness.CallAsync("undo");
            Assert.Equal(4, await Count(harness));
        }

        [Fact]
        public async Task A_dry_run_changes_nothing_and_reports_what_a_real_run_gives_handles_included()
        {
            await using McpHarness harness = await Plate("plandry");
            bool modified = harness.Session.Backend.IsModified;

            JsonNode dry = await Plan(harness, AddsAndMove, dryRun: true);

            Assert.True((bool)dry["ok"], (string)dry["message"]);
            Assert.True((bool)dry["dryRun"]);
            Assert.True((bool)dry["rolledBack"]);
            Assert.False((bool)dry["changed"]);
            Assert.Equal(4, await Count(harness));
            Assert.Equal(modified, harness.Session.Backend.IsModified);
            Assert.False((bool)(await harness.CallAsync("undo"))["undone"]);

            JsonNode real = await Plan(harness, AddsAndMove);

            Assert.True((bool)real["ok"]);
            Assert.Equal((string)dry["steps"][0]["result"]["handle"], (string)real["steps"][0]["result"]["handle"]);
            Assert.Equal((string)dry["steps"][1]["result"]["handle"], (string)real["steps"][1]["result"]["handle"]);
            Assert.Equal(6, await Count(harness));
        }

        [Fact]
        public async Task A_dry_run_of_a_plan_that_only_reads_runs_it_and_a_dry_run_reports_a_failure_too()
        {
            await using McpHarness harness = await Plate("plandryread");

            JsonNode reads = await Plan(
                harness,
                "[{\"tool\":\"info\"},{\"tool\":\"list_layers\",\"id\":\"layers\"}]",
                dryRun: true
            );
            Assert.True((bool)reads["ok"]);
            Assert.False((bool)reads["rolledBack"]);
            Assert.Equal(4, (int)reads["steps"][0]["result"]["entityCount"]);
            Assert.Contains("none of them writes", (string)reads["message"]);

            JsonNode failing = await Plan(
                harness,
                "[{\"tool\":\"add_line\",\"arguments\":{\"start\":[0,0],\"end\":[1,1]}},"
                    + "{\"tool\":\"erase\",\"arguments\":{\"handles\":[\"FFFFF\"]}}]",
                dryRun: true
            );
            Assert.False((bool)failing["ok"]);
            Assert.Contains("Dry run: step s2 (erase) failed", (string)failing["message"]);
            Assert.Equal(4, await Count(harness));
        }

        [Fact]
        public async Task Every_step_is_checked_first_and_every_problem_is_reported_with_nothing_run()
        {
            await using McpHarness harness = await Plate("planvalid");

            JsonNode result = await Plan(
                harness,
                "[{\"tool\":\"add_line\",\"arguments\":{\"start\":[0,0],\"end\":[1,1]}},"
                    + "{\"tool\":\"add_circle\",\"arguments\":{\"center\":[5,5],\"radius\":\"big\"}},"
                    + "{\"tool\":\"move\",\"arguments\":{\"handles\":[\"2F\"]}},"
                    + "{\"tool\":\"add_ellipse_of_dreams\",\"arguments\":{}},"
                    + "{\"tool\":\"add_line\",\"arguments\":{\"start\":[0,0],\"end\":[1,1],\"colour\":3}},"
                    + "{\"tool\":\"add_line\",\"arguments\":{\"start\":[0,0],\"end\":[1,[2]]}}]"
            );

            Assert.False((bool)result["ok"]);
            Assert.False((bool)result["changed"]);
            Assert.StartsWith("Nothing was run: 5 steps are wrong", (string)result["message"]);
            Assert.True((bool)result["steps"][0]["skipped"]);
            Assert.Contains("radius is a string but the tool takes number", (string)result["steps"][1]["error"]);
            Assert.Contains("delta is required and is missing", (string)result["steps"][2]["error"]);
            Assert.Contains("There is no tool add_ellipse_of_dreams", (string)result["steps"][3]["error"]);
            Assert.Contains("colour is not an argument of the tool", (string)result["steps"][4]["error"]);
            Assert.Contains("end[1] is a list but the tool takes number", (string)result["steps"][5]["error"]);
            Assert.Equal(4, await Count(harness));
            Assert.False(harness.Session.Backend.IsModified);
        }

        [Fact]
        public async Task The_tools_a_plan_may_not_hold_are_refused_by_name()
        {
            await using McpHarness harness = await Plate("planrefused", scripts: true);
            string[] banned = { "run_plan", "open_drawing", "new_drawing", "undo", "save" };

            foreach (string tool in banned)
            {
                string arguments = tool == "run_plan" ? "{\"steps\":[{\"tool\":\"info\"}]}" : "{}";
                JsonNode result = await Plan(harness, "[{\"tool\":\"" + tool + "\",\"arguments\":" + arguments + "}]");
                Assert.False((bool)result["ok"], tool);
                Assert.Contains(tool + " is not allowed in a plan", (string)result["steps"][0]["error"]);
            }

            string message = await harness.FailAsync("run_plan", ("steps", Steps("[]")));
            Assert.Contains("needs at least one step", message);
            string many = await harness.FailAsync(
                "run_plan",
                ("steps", Steps("[" + string.Join(",", Enumerable.Repeat("{\"tool\":\"info\"}", 201)) + "]"))
            );
            Assert.Contains("at most 200 steps", many);
        }

        [Fact]
        public async Task A_read_only_server_refuses_a_plan_with_a_writer_before_running_anything_and_runs_one_that_reads()
        {
            await using McpHarness harness = await Plate("planreadonly", readOnly: true);

            string message = await harness.FailAsync("run_plan", ("steps", Steps(AddsAndMove)));

            Assert.Contains("--read-only", message);
            Assert.Contains("l add_line", message);
            Assert.Contains("Nothing was run", message);
            Assert.Equal(4, await Count(harness));

            JsonNode reads = await Plan(
                harness,
                "[{\"tool\":\"info\"},{\"tool\":\"query_entities\",\"arguments\":{\"kinds\":[\"CIRCLE\"]}}]"
            );
            Assert.True((bool)reads["ok"]);
            Assert.False((bool)reads["changed"]);
        }

        [Fact]
        public async Task Run_command_is_refused_in_a_plan_without_allow_scripts_and_runs_with_it()
        {
            await using McpHarness without = await Plate("planscripts");
            JsonNode refused = await Plan(
                without,
                "[{\"tool\":\"add_line\",\"arguments\":{\"start\":[0,0],\"end\":[1,1]}},"
                    + "{\"tool\":\"run_command\",\"arguments\":{\"text\":\"LINE 0,0 9,9\" }}]"
            );
            Assert.False((bool)refused["ok"]);
            Assert.Contains("--allow-scripts", (string)refused["steps"][1]["error"]);
            Assert.True((bool)refused["steps"][0]["skipped"]);
            Assert.Equal(4, await Count(without));

            await using McpHarness with = await Plate("planscripts2", scripts: true);
            JsonNode allowed = await Plan(
                with,
                "[{\"tool\":\"add_line\",\"arguments\":{\"start\":[0,0],\"end\":[1,1]}},"
                    + "{\"tool\":\"run_command\",\"arguments\":{\"text\":\"CIRCLE 50,50 5\\n\\n\"}}]"
            );
            Assert.True((bool)allowed["ok"], (string)allowed["message"]);
            Assert.Equal(6, await Count(with));
            await with.CallAsync("undo");
            Assert.Equal(4, await Count(with));
        }

        [Fact]
        public async Task A_picture_in_a_plan_is_reported_by_its_size_and_the_plan_is_in_the_tool_list_as_a_writer()
        {
            await using McpHarness harness = await Plate("planimage");

            JsonNode result = await Plan(harness, "[{\"tool\":\"render_png\",\"arguments\":{\"width\":200}}]");

            Assert.True((bool)result["ok"]);
            JsonNode answer = result["steps"][0]["result"];
            Assert.Equal("image/png", (string)answer["images"][0]["mimeType"]);
            Assert.True((int)answer["images"][0]["bytes"] > 500);

            var tools = await harness.Client.ListToolsAsync();
            var plan = Assert.Single(tools, t => t.Name == "run_plan");
            Assert.True(plan.ProtocolTool.Annotations.ReadOnlyHint != true);
            Assert.Equal(
                "array",
                plan.JsonSchema.GetProperty("properties").GetProperty("steps").GetProperty("type").GetString()
            );
        }

        [Fact]
        public async Task The_placement_loop_as_plans_takes_4_calls_where_the_single_calls_take_19()
        {
            await using McpHarness single = await Plate("planloop1");
            var log = await PlacementLoop.RunAsync(single);
            int singleCalls = log.Count + 4;

            await using McpHarness planned = await Plate("planloop2");
            int plannedCalls = await PlanPlacementLoop.RunAsync(planned);

            Assert.Equal(PlanPlacementLoop.CallsOfTheSingleLoop, singleCalls);
            Assert.Equal(4, plannedCalls);
        }

        [Fact]
        public async Task Ids_are_unique_and_plain_and_a_step_cannot_use_the_answer_of_a_step_that_failed()
        {
            await using McpHarness harness = await Plate("planids");

            JsonNode clash = await Plan(
                harness,
                "[{\"tool\":\"info\",\"id\":\"s2\"},{\"tool\":\"info\"},{\"tool\":\"info\",\"id\":\"bad id\"}]"
            );
            Assert.False((bool)clash["ok"]);
            Assert.Contains("The id s2 is also the id of step 1", (string)clash["steps"][1]["error"]);
            Assert.Contains("The id bad id is not allowed", (string)clash["steps"][2]["error"]);

            JsonNode failed = await Plan(
                harness,
                "[{\"tool\":\"add_text\",\"id\":\"a\",\"arguments\":{\"text\":\"X\",\"at\":[1,1],\"height\":3,\"text_style\":\"NoSuch\"}},"
                    + "{\"tool\":\"move\",\"arguments\":{\"handles\":[\"$a.handle\"],\"delta\":[1,1]}},"
                    + "{\"tool\":\"add_circle\",\"id\":\"C\",\"arguments\":{\"center\":[5,5],\"radius\":2}},"
                    + "{\"tool\":\"bounds\",\"arguments\":{\"handles\":[\"$c.Handle\"]}}]",
                stop: false
            );
            Assert.Contains("but the step a gave no answer", (string)failed["steps"][1]["error"]);
            Assert.True((bool)failed["steps"][3]["ok"], (string)failed["steps"][3]["error"]);
            Assert.Equal(5, await Count(harness));
        }

        [Fact]
        public async Task A_value_from_an_earlier_answer_that_looks_like_lisp_or_a_command_is_only_ever_text()
        {
            await using McpHarness harness = await Plate("planlisp", scripts: true);
            await harness.CallAsync("add_layer", ("name", "(setq planx 1)"));
            await harness.CallAsync("add_layer", ("name", "'ZOOM"));
            await harness.CallAsync("add_layer", ("name", "!planx"));

            JsonNode result = await Plan(
                harness,
                "[{\"tool\":\"list_layers\",\"id\":\"ll\"},"
                    + "{\"tool\":\"add_text\",\"id\":\"a\",\"arguments\":{\"text\":\"$ll[0].name\",\"at\":[1,1],\"height\":3}},"
                    + "{\"tool\":\"add_text\",\"id\":\"b\",\"arguments\":{\"text\":\"$ll[1].name\",\"at\":[1,6],\"height\":3}},"
                    + "{\"tool\":\"add_text\",\"id\":\"c\",\"arguments\":{\"text\":\"$ll[2].name\",\"at\":[1,11],\"height\":3}},"
                    + "{\"tool\":\"add_circle\",\"arguments\":{\"center\":[5,5],\"radius\":\"$ll[0].name\"}}]"
            );

            // The last step puts a text where a number is taken: refused with the others run and taken back.
            Assert.False((bool)result["ok"]);
            Assert.Contains("radius is a string but the tool takes number", (string)result["steps"][4]["error"]);
            Assert.True((bool)result["rolledBack"]);
            Assert.Equal(4, await Count(harness));

            JsonNode texts = await Plan(
                harness,
                "[{\"tool\":\"list_layers\",\"id\":\"ll\"},"
                    + "{\"tool\":\"add_text\",\"arguments\":{\"text\":\"$ll[0].name\",\"at\":[1,1],\"height\":3}},"
                    + "{\"tool\":\"add_text\",\"arguments\":{\"text\":\"$ll[1].name\",\"at\":[1,6],\"height\":3}},"
                    + "{\"tool\":\"add_text\",\"arguments\":{\"text\":\"$ll[2].name\",\"at\":[1,11],\"height\":3}}]"
            );
            Assert.True((bool)texts["ok"], (string)texts["message"]);
            JsonNode found = await harness.CallAsync("query_entities", ("kinds", new[] { "TEXT" }));
            string[] words = found["entities"].AsArray().Select(e => (string)e["geometry"]["text"]).ToArray();
            Assert.Contains("!planx", words);
            Assert.Contains("'ZOOM", words);
            Assert.Contains("(setq planx 1)", words);
            Assert.Equal(7, await Count(harness));
        }

        [Fact]
        public async Task The_text_of_run_command_in_a_plan_is_typed_as_written_and_no_reference_is_read_in_it()
        {
            await using McpHarness harness = await Plate("planrun", scripts: true);

            JsonNode result = await Plan(
                harness,
                "[{\"tool\":\"run_command\",\"arguments\":{\"text\":\"$nobody.handle\"}}]"
            );

            string text = result.ToJsonString();
            Assert.DoesNotContain("no step has the id", text);
            Assert.DoesNotContain("starts like a reference", text);
        }
    }
}
