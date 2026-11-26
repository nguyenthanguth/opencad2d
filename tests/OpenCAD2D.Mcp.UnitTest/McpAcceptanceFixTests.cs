using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json.Nodes;
using System.Threading.Tasks;
using Xunit;

namespace OpenCAD2D.Mcp.UnitTest
{
    /// <summary>What the end-to-end acceptance of the server found, one test for each finding.</summary>
    public class McpAcceptanceFixTests
    {
        public static IEnumerable<object[]> Modes => new[] { new object[] { "file" }, new object[] { "app" } };

        [Fact]
        public async Task A_read_only_server_attached_to_an_app_refuses_new_drawing_and_open_drawing()
        {
            using TestApp app = TestApp.Start();
            string path = Drawings.Plate(TestFolder.Make("readonly-open"));
            await using McpHarness harness = await McpHarness.StartAsync(
                new McpOptions
                {
                    Attach = true,
                    RemoteFolder = app.Folder,
                    ReadOnly = true,
                }
            );

            Assert.Contains("--read-only", await harness.FailAsync("new_drawing"));
            Assert.Contains("--read-only", await harness.FailAsync("open_drawing", ("path", path)));
        }

        [Fact]
        public async Task A_read_only_server_on_a_file_still_starts_with_new_drawing_and_opens_files()
        {
            string path = Drawings.Plate(TestFolder.Make("readonly-file"));
            await using McpHarness harness = await McpHarness.StartAsync(new McpOptions { ReadOnly = true });

            Assert.Equal("file", (string)(await harness.CallAsync("new_drawing"))["mode"]);
            Assert.Equal("plate.dxf", (string)(await harness.CallAsync("open_drawing", ("path", path)))["name"]);
        }

        [Theory]
        [MemberData(nameof(Modes))]
        public async Task A_missing_or_wrongly_typed_argument_says_which_argument_and_what_the_tool_takes(string mode)
        {
            await using Bench bench = await Bench.Start(mode);

            string missing = await bench.Fail("add_line", ("start", new[] { 0.0, 0.0 }));
            Assert.Contains("add_line", missing);
            Assert.Contains("end", missing);
            Assert.Contains("required", missing);

            string wrong = await bench.Fail("add_line", ("start", "0,0"), ("end", new[] { 5.0, 5.0 }));
            Assert.Contains("start", wrong);
            Assert.Contains("string", wrong);
            Assert.Contains("array", wrong);

            string unknown = await bench.Fail(
                "add_line",
                ("start", new[] { 0.0, 0.0 }),
                ("end", new[] { 5.0, 5.0 }),
                ("colour", 3)
            );
            Assert.Contains("colour", unknown);
            Assert.Equal(0, (int)(await bench.Call("info"))["entityCount"]);
        }

        [Theory]
        [MemberData(nameof(Modes))]
        public async Task Fits_and_free_space_take_the_frame_a_measure_tool_gives_as_an_object_or_an_array(string mode)
        {
            await using Bench bench = await Bench.Start(mode);
            await bench.Call("add_rectangle", ("corner1", new[] { 0.0, 0.0 }), ("corner2", new[] { 100.0, 60.0 }));
            JsonNode measured = await bench.Call(
                "measure_text",
                ("text", "NOTE"),
                ("at", new[] { 300.0, 30.0 }),
                ("height", 5.0),
                ("rotation", 30.0)
            );
            JsonNode frame = measured["frames"][0];

            Assert.True((bool)(await bench.Call("fits", ("candidate", frame.DeepClone())))["fits"]);
            Assert.False(
                (bool)
                    (
                        await bench.Call(
                            "fits",
                            (
                                "candidate",
                                new JsonObject
                                {
                                    ["x"] = 90.0,
                                    ["y"] = 20.0,
                                    ["width"] = 20.0,
                                    ["height"] = 10.0,
                                    ["rotation"] = 0.0,
                                }
                            )
                        )
                    )["fits"]
            );
            JsonNode free = await bench.Call(
                "free_space",
                ("frame", frame.DeepClone()),
                ("margin", 1.0),
                ("page_size", 5)
            );
            Assert.True((int)free["total"] > 0);

            // In a plan the whole frame of an earlier step is the argument.
            JsonNode plan = await bench.Call(
                "run_plan",
                (
                    "steps",
                    JsonNode.Parse(
                        "[{\"id\":\"m\",\"tool\":\"measure_text\",\"arguments\":{\"text\":\"NOTE\",\"at\":[300,30],\"height\":5}},"
                            + "{\"id\":\"f\",\"tool\":\"fits\",\"arguments\":{\"candidate\":\"$m.frames[0]\"}},"
                            + "{\"id\":\"s\",\"tool\":\"free_space\",\"arguments\":{\"frame\":\"$m.frames[0]\"}}]"
                    )
                )
            );
            Assert.True((bool)plan["ok"], plan.ToJsonString());
            Assert.True((bool)plan["steps"][1]["result"]["fits"]);

            string wrong = await bench.Fail("fits", ("candidate", new JsonObject { ["x"] = 1.0, ["y"] = 2.0 }));
            Assert.Contains("width", wrong);
        }

        /// <summary>A block TAG of a frame and an attribute NUMBER, made far from the origin and inserted at (60, 420).</summary>
        private static async Task<string> InsertedTag(Bench bench, string value)
        {
            string frame = await bench.Add(
                "add_rectangle",
                ("corner1", new[] { 2000.0, 0.0 }),
                ("corner2", new[] { 2220.0, 60.0 })
            );
            string number = await bench.Add(
                "add_attribute_definition",
                ("tag", "NUMBER"),
                ("at", new[] { 2010.0, 15.0 }),
                ("height", 25.0),
                ("default_value", "0")
            );
            await bench.Call(
                "define_block",
                ("name", "TAG"),
                ("base_point", new[] { 2000.0, 0.0 }),
                ("handles", new[] { frame, number }),
                ("source", "delete")
            );
            return await bench.Add(
                "insert_block_with_attributes",
                ("name", "TAG"),
                ("at", new[] { 60.0, 420.0 }),
                ("values", new Dictionary<string, string> { ["NUMBER"] = value })
            );
        }

        [Theory]
        [MemberData(nameof(Modes))]
        public async Task Overlaps_counts_the_attribute_words_of_a_block_reference(string mode)
        {
            await using Bench bench = await Bench.Start(mode);
            string tag = await InsertedTag(bench, "A-101");
            Assert.Equal(0, (int)(await bench.Call("overlaps"))["total"]);

            // A line across the words A-101 (25 high at 70, 435).
            string line = await bench.Add(
                "add_line",
                ("start", new[] { 65.0, 447.0 }),
                ("end", new[] { 300.0, 447.0 })
            );

            JsonNode across = await bench.Call("overlaps");
            Assert.True((int)across["total"] > 0, across.ToJsonString());
            Assert.Contains(across["rows"].AsArray(), r => (string)r["word"] == tag && (string)r["other"] == line);
            Assert.True((int)(await bench.Call("overlaps", ("of", new[] { tag })))["total"] > 0);

            // What was asked for stays as it was: only TEXT is looked at, and the words of the block are not.
            Assert.Equal(0, (int)(await bench.Call("overlaps", ("kinds", new[] { "TEXT" })))["total"]);
        }

        [Theory]
        [MemberData(nameof(Modes))]
        public async Task An_attribute_value_with_spaces_is_kept_whole(string mode)
        {
            await using Bench bench = await Bench.Start(mode);
            string tag = await InsertedTag(bench, "1:10 AND 1:5");

            JsonNode attributes = await bench.Call("get_attributes", ("handle", tag));

            Assert.Equal(
                new[] { "NUMBER=1:10 AND 1:5" },
                attributes["attributes"].AsArray().Select(a => (string)a["tag"] + "=" + (string)a["value"]).ToArray()
            );
        }

        [Theory]
        [MemberData(nameof(Modes))]
        public async Task A_hatch_takes_arcs_a_circle_or_the_objects_already_in_the_drawing_as_its_outline(string mode)
        {
            await using Bench bench = await Bench.Start(mode);

            // Two half circles of diameter 100: the corners (0, 0) and (100, 0) with a bulge of 1 each.
            string rounded = await bench.Add(
                "add_hatch",
                ("boundary", new[] { new[] { 0.0, 0.0 }, new[] { 100.0, 0.0 } }),
                ("bulges", new[] { 1.0, 1.0 })
            );
            string disc = await bench.Add(
                "add_hatch",
                ("circle_center", new[] { 300.0, 0.0 }),
                ("circle_radius", 50.0),
                ("pattern", "SOLID")
            );
            string ring = await bench.Add("add_circle", ("center", new[] { 600.0, 0.0 }), ("radius", 50.0));
            string slot = await bench.Add(
                "add_polyline",
                (
                    "points",
                    new[] { new[] { 800.0, 0.0 }, new[] { 900.0, 0.0 }, new[] { 900.0, 50.0 }, new[] { 800.0, 50.0 } }
                ),
                ("closed", true)
            );
            string fromObjects = await bench.Add("add_hatch", ("handles", new[] { ring, slot }), ("pattern", "SOLID"));

            foreach (string handle in new[] { rounded, disc })
            {
                JsonNode hatch = await bench.Call("get_entity", ("handle", handle));
                Assert.Equal(Math.PI * 50.0 * 50.0, (double)hatch["details"]["area"], 0);
            }

            JsonNode both = await bench.Call("get_entity", ("handle", fromObjects));
            Assert.Equal(Math.PI * 50.0 * 50.0 + 100.0 * 50.0, (double)both["details"]["area"], 0);

            Assert.Contains("no entity", await bench.Fail("add_hatch", ("handles", new[] { "FFFF" })));
            string notClosed = await bench.Add("add_line", ("start", new[] { 0.0, 0.0 }), ("end", new[] { 1.0, 1.0 }));
            Assert.Contains(
                "closed polyline or a circle",
                await bench.Fail("add_hatch", ("handles", new[] { notClosed }))
            );
            Assert.Contains("an outline", await bench.Fail("add_hatch"));
            Assert.Contains(
                "bulge for each corner",
                await bench.Fail(
                    "add_hatch",
                    ("boundary", new[] { new[] { 0.0, 0.0 }, new[] { 1.0, 0.0 }, new[] { 1.0, 1.0 } }),
                    ("bulges", new[] { 0.0 })
                )
            );
        }

        [Theory]
        [MemberData(nameof(Modes))]
        public async Task A_text_added_with_justify_stands_about_its_point_as_measure_text_measured_it(string mode)
        {
            await using Bench bench = await Bench.Start(mode);
            var at = new[] { 100.0, 50.0 };
            JsonNode measured = await bench.Call(
                "measure_text",
                ("text", "CENTRED"),
                ("at", at),
                ("height", 5.0),
                ("justify", "mc")
            );
            string handle = await bench.Add(
                "add_text",
                ("text", "CENTRED"),
                ("at", at),
                ("height", 5.0),
                ("justify", "mc")
            );

            JsonNode box = (await bench.Call("bounds", ("handles", new[] { handle })))["rows"][0]["box"];
            Assert.Equal((double)measured["box"][0], (double)box[0], 2);
            Assert.Equal((double)measured["box"][2], (double)box[2], 2);
            Assert.Equal(100.0, ((double)box[0] + (double)box[2]) / 2.0, 0);
            Assert.Contains(
                "justification",
                await bench.Fail("add_text", ("text", "x"), ("at", at), ("height", 5.0), ("justify", "sideways"))
            );
        }

        [Theory]
        [MemberData(nameof(Modes))]
        public async Task A_layout_is_renamed_and_deleted_and_Model_and_the_layout_shown_are_refused(string mode)
        {
            await using Bench bench = await Bench.Start(mode);
            await bench.Call("create_layout", ("name", "Draft"));
            await bench.Call("create_layout", ("name", "Other"));
            string note = await bench.Add(
                "add_text",
                ("text", "ON DRAFT"),
                ("at", new[] { 10.0, 10.0 }),
                ("height", 5.0),
                ("layout", "Draft")
            );

            Assert.Equal(
                "Sheet A",
                (string)(await bench.Call("rename_layout", ("name", "draft"), ("new_name", "Sheet A")))["name"]
            );
            Assert.Contains("Sheet A", ((JsonArray)(await bench.Call("info"))["layouts"]).Select(l => (string)l));
            Assert.DoesNotContain("Draft", ((JsonArray)(await bench.Call("info"))["layouts"]).Select(l => (string)l));
            Assert.Equal(1, (int)(await bench.Call("info", ("layout", "Sheet A")))["counts"]["TEXT"]);

            Assert.Contains("Model", await bench.Fail("rename_layout", ("name", "Model"), ("new_name", "X")));
            Assert.Contains("already", await bench.Fail("rename_layout", ("name", "Sheet A"), ("new_name", "other")));
            Assert.Contains("name", await bench.Fail("rename_layout", ("name", "Sheet A"), ("new_name", "A/B")));
            Assert.Contains("Model", await bench.Fail("delete_layout", ("name", "Model")));

            await bench.Call("set_current_layout", ("name", "Other"));
            Assert.Contains("shown", await bench.Fail("delete_layout", ("name", "Other")));
            await bench.Call("delete_layout", ("name", "Sheet A"));
            Assert.DoesNotContain("Sheet A", ((JsonArray)(await bench.Call("info"))["layouts"]).Select(l => (string)l));

            await bench.Call("undo");
            Assert.Contains("Sheet A", ((JsonArray)(await bench.Call("info"))["layouts"]).Select(l => (string)l));
            Assert.Equal(1, (int)(await bench.Call("info", ("layout", "Sheet A")))["counts"]["TEXT"]);
        }

        [Theory]
        [MemberData(nameof(Modes))]
        public async Task A_viewport_can_be_put_on_a_layer_of_its_own(string mode)
        {
            await using Bench bench = await Bench.Start(mode);
            await bench.Call("add_layer", ("name", "Frames"), ("plot", false));
            await bench.Call("create_layout", ("name", "Sheet1"));
            JsonNode made = await bench.Call(
                "add_viewport",
                ("layout", "Sheet1"),
                ("frame", new[] { 10.0, 10.0, 200.0, 150.0 }),
                ("scale", 10.0),
                ("layer", "Frames")
            );
            string handle = (string)made["handles"][0];

            Assert.Equal("Frames", (string)(await bench.Call("get_entity", ("handle", handle)))["layer"]);

            // A viewport is a row of query_entities in an app too, as it is in a file.
            JsonNode rows = await bench.Call("query_entities", ("layout", "Sheet1"), ("kinds", new[] { "VIEWPORT" }));
            Assert.Contains(rows["entities"].AsArray(), e =>
                (string)e["handle"] == handle && (string)e["layer"] == "Frames"
            );
            Assert.Contains(
                "no layer",
                await bench.Fail(
                    "add_viewport",
                    ("layout", "Sheet1"),
                    ("frame", new[] { 10.0, 10.0, 20.0, 20.0 }),
                    ("scale", 10.0),
                    ("layer", "Nowhere")
                )
            );
        }

        [Theory]
        [MemberData(nameof(Modes))]
        public async Task A_lineweight_is_given_in_millimetres_as_AutoCAD_shows_it_or_in_hundredths_as_before(
            string mode
        )
        {
            await using Bench bench = await Bench.Start(mode);

            async Task<int> Weight(string layer) =>
                (int)(await bench.Call("list_layers")).AsArray().Single(l => (string)l["name"] == layer)["lineWeight"];

            await bench.Call("add_layer", ("name", "Mm"), ("lineweight", 0.5));
            await bench.Call("add_layer", ("name", "Hundredths"), ("lineweight", 50));
            await bench.Call("add_layer", ("name", "Light"), ("lineweight", 0.18));
            await bench.Call("add_layer", ("name", "One"), ("lineweight", 1));
            await bench.Call("add_layer", ("name", "Hidden"), ("plot", false));

            Assert.Equal(50, await Weight("Mm"));
            Assert.Equal(50, await Weight("Hundredths"));
            Assert.Equal(18, await Weight("Light"));
            Assert.Equal(100, await Weight("One"));
            Assert.False(
                (bool)
                    (await bench.Call("list_layers")).AsArray().Single(l => (string)l["name"] == "Hidden")["plottable"]
            );

            await bench.Call("set_layer", ("name", "Mm"), ("lineweight", 0.35));
            Assert.Equal(35, await Weight("Mm"));
            await bench.Call("set_layer", ("name", "Mm"), ("lineweight", 25));
            Assert.Equal(25, await Weight("Mm"));
            await bench.Call("set_layer", ("name", "Mm"), ("lineweight", -3));
            Assert.Equal(-3, await Weight("Mm"));

            string line = await bench.Add("add_line", ("start", new[] { 0.0, 0.0 }), ("end", new[] { 10.0, 0.0 }));
            await bench.Call("set_properties", ("handles", new[] { line }), ("lineweight", 0.7));
            Assert.Equal(70, (int)(await bench.Call("get_entity", ("handle", line)))["lineWeight"]);

            Assert.Contains("none of AutoCAD", await bench.Fail("add_layer", ("name", "Bad"), ("lineweight", 0.45)));
            Assert.Contains("none of AutoCAD", await bench.Fail("set_layer", ("name", "Mm"), ("lineweight", 33)));
        }

        [Theory]
        [MemberData(nameof(Modes))]
        public async Task An_offset_that_does_not_erase_its_source_is_not_made_to_by_an_earlier_one_that_did(
            string mode
        )
        {
            await using Bench bench = await Bench.Start(mode);
            string first = await bench.Add("add_line", ("start", new[] { 0.0, 0.0 }), ("end", new[] { 100.0, 0.0 }));
            JsonNode erasing = await bench.Call(
                "offset",
                ("handle", first),
                ("side", new[] { 50.0, 10.0 }),
                ("distance", 10.0),
                ("erase_source", true)
            );
            Assert.Single(erasing["erased"].AsArray());

            string second = await bench.Add(
                "add_line",
                ("start", new[] { 0.0, 100.0 }),
                ("end", new[] { 100.0, 100.0 })
            );
            JsonNode keeping = await bench.Call(
                "offset",
                ("handle", second),
                ("side", new[] { 50.0, 110.0 }),
                ("distance", 10.0)
            );

            Assert.Empty(keeping["erased"].AsArray());
            Assert.Contains(
                (await bench.Call("query_entities", ("kinds", new[] { "LINE" })))["entities"].AsArray(),
                e => (string)e["handle"] == second
            );
        }

        private static JsonNode Plan(string steps) => JsonNode.Parse(steps);

        [Theory]
        [MemberData(nameof(Modes))]
        public async Task A_plan_may_end_with_file_writers_which_run_after_its_edits_are_kept(string mode)
        {
            await using Bench bench = await Bench.Start(mode);
            string folder = TestFolder.Make("plan-files");
            string pdf = System.IO.Path.Combine(folder, "plan.pdf");
            string dxf = System.IO.Path.Combine(folder, "plan.dxf");
            string steps =
                "[{\"tool\":\"add_rectangle\",\"arguments\":{\"corner1\":[0,0],\"corner2\":[100,60]}},"
                + "{\"tool\":\"plot_pdf\",\"id\":\"pdf\",\"arguments\":{\"path\":"
                + JsonValue.Create(pdf).ToJsonString()
                + "}},{\"tool\":\"save_as\",\"id\":\"copy\",\"arguments\":{\"path\":"
                + JsonValue.Create(dxf).ToJsonString()
                + "}}]";

            // A dry run writes no file and puts the drawing back.
            JsonNode dry = await bench.Call("run_plan", ("steps", Plan(steps)), ("dry_run", true));
            Assert.True((bool)dry["ok"], dry.ToJsonString());
            Assert.False(System.IO.File.Exists(pdf));
            Assert.False(System.IO.File.Exists(dxf));
            Assert.Equal(0, (int)(await bench.Call("info"))["entityCount"]);

            // A plan that fails before them writes none either.
            string failing = steps.Insert(1, "{\"tool\":\"erase\",\"arguments\":{\"handles\":[\"FFFF\"]}},");
            JsonNode failed = await bench.Call("run_plan", ("steps", Plan(failing)));
            Assert.False((bool)failed["ok"]);
            Assert.False(System.IO.File.Exists(dxf));

            JsonNode real = await bench.Call("run_plan", ("steps", Plan(steps)));
            Assert.True((bool)real["ok"], real.ToJsonString());
            Assert.True(new System.IO.FileInfo(pdf).Length > 500);
            Assert.True(new System.IO.FileInfo(dxf).Length > 500);
            Assert.Equal(1, (int)(await bench.Call("info"))["counts"]["LWPOLYLINE"]);

            // save_as anywhere else is refused, naming where it may go.
            JsonNode middle = await bench.Call(
                "run_plan",
                (
                    "steps",
                    Plan(
                        "[{\"tool\":\"save_as\",\"arguments\":{\"path\":"
                            + JsonValue.Create(System.IO.Path.Combine(folder, "x.dxf")).ToJsonString()
                            + "}},{\"tool\":\"info\"}]"
                    )
                )
            );
            Assert.False((bool)middle["ok"]);
            Assert.Contains("only as its last step", (string)middle["steps"][0]["error"]);
        }

        [Fact]
        public async Task The_server_instructions_and_descriptions_say_which_space_each_tool_acts_in_and_which_tools_write_files()
        {
            await using McpHarness harness = await McpHarness.StartAsync(new McpOptions());
            string instructions = harness.Client.ServerInstructions;
            var tools = (await harness.Client.ListToolsAsync()).ToDictionary(t => t.Name, t => t.Description);

            foreach (
                string word in new[]
                {
                    "millimetres",
                    "degrees",
                    "lineweight",
                    "layout",
                    "plot_pdf",
                    "run_plan",
                    "snapshot",
                    "Blocks",
                    "Layouts",
                    "Debug",
                    "save_as",
                    "export_svg",
                    "dump_drawing",
                }
            )
            {
                Assert.Contains(word, instructions);
            }

            // Two things the instructions used to say that were not true.
            Assert.DoesNotContain("Nothing reaches a file until save", instructions);
            Assert.DoesNotContain("only tool that writes a file", tools["save"]);
            Assert.Contains("save_as", tools["save"]);
            Assert.Contains("model space where it is left", instructions);
            Assert.Contains("add_* tools", tools["set_current_layout"]);
            Assert.Contains("still marked modified", tools["save_as"]);
            Assert.Contains("model space where layout is left out", tools["plot_pdf"]);
            Assert.Contains("run_plan", tools["render_png"]);
        }

        [Theory]
        [MemberData(nameof(Modes))]
        public async Task Plot_pdf_without_a_layout_plots_model_space_whatever_layout_is_shown(string mode)
        {
            await using Bench bench = await Bench.Start(mode);
            await bench.Call("create_layout", ("name", "Sheet1"));
            await bench.Call("add_text", ("text", "INMODEL"), ("at", new[] { 10.0, 10.0 }), ("height", 5.0));
            await bench.Call(
                "add_text",
                ("text", "ONSHEET"),
                ("at", new[] { 20.0, 20.0 }),
                ("height", 5.0),
                ("layout", "Sheet1")
            );
            await bench.Call("set_current_layout", ("name", "Sheet1"));
            string folder = TestFolder.Make("plot-default");

            JsonNode plotted = await bench.Call("plot_pdf", ("path", System.IO.Path.Combine(folder, "default.pdf")));

            Assert.Equal("Model", (string)plotted["layout"]);
            string words = PdfWords.Of(System.IO.File.ReadAllBytes((string)plotted["path"]));
            Assert.Contains("INMOD", words);
            Assert.DoesNotContain("ONSH", words);
        }
    }
}
