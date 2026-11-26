using System.Linq;
using System.Text.Json.Nodes;
using System.Threading.Tasks;
using OpenCAD2D.Remote;
using Xunit;

namespace OpenCAD2D.Mcp.UnitTest
{
    /// <summary>
    /// The read and query tools and the snapshots of an agent attached to an app (the Remote test server, which has no window): the
    /// answers are the ones the same drawing gives as a file, and the app's own selection and command line are left alone.
    /// </summary>
    public class McpQueryAttachTests
    {
        private static async Task<McpHarness> Attach(TestApp app, bool readOnly = false) =>
            await McpHarness.StartAsync(
                new McpOptions
                {
                    Attach = true,
                    RemoteFolder = app.Folder,
                    ReadOnly = readOnly,
                }
            );

        private static async Task<McpHarness> Opened(TestApp app, string name, bool readOnly = false)
        {
            McpHarness harness = await Attach(app, readOnly);
            await harness.CallAsync("open_drawing", ("path", QueryDrawings.Sheet(TestFolder.Make(name))));
            return harness;
        }

        private static string[] Strings(JsonNode array) => array.AsArray().Select(n => (string)n).ToArray();

        [Fact]
        public async Task Attached_get_entity_gives_what_it_gives_for_the_same_file()
        {
            using TestApp app = TestApp.Start();
            string folder = TestFolder.Make("attach-parity");
            string path = QueryDrawings.Sheet(folder);
            await using McpHarness attached = await Attach(app);
            await attached.CallAsync("open_drawing", ("path", path));
            await using McpHarness file = await McpHarness.StartAsync(new McpOptions { File = path });

            foreach (string kind in new[] { "LWPOLYLINE", "CIRCLE", "LINE", "TEXT", "MTEXT", "INSERT" })
            {
                string handle = await McpQueryTests.First(file, kind);
                JsonNode fromFile = await file.CallAsync("get_entity", ("handle", handle));
                JsonNode fromApp = await attached.CallAsync("get_entity", ("handle", handle));

                Assert.Equal(fromFile.ToJsonString(), fromApp.ToJsonString());
            }

            foreach (
                (string tool, (string, object)[] arguments) in new (string, (string, object)[])[]
                {
                    ("count_entities", new[] { ("group_by", (object)"layer") }),
                    ("count_entities", new[] { ("group_by", (object)"color") }),
                    ("aggregate", new[] { ("measure", (object)"length"), ("group_by", "layer") }),
                    ("aggregate", new[] { ("measure", (object)"area"), ("group_by", "kind") }),
                    ("get_texts", new (string, object)[0]),
                    ("get_block", new[] { ("name", (object)"TITLE") }),
                    ("select", new[] { ("mode", (object)"all") }),
                    ("list_layouts", new (string, object)[0]),
                    ("list_linetypes", new (string, object)[0]),
                    ("get_variables", new (string, object)[0]),
                }
            )
            {
                JsonNode fromFile = await file.CallAsync(tool, arguments);
                JsonNode fromApp = await attached.CallAsync(tool, arguments);

                Assert.True(
                    fromFile.ToJsonString() == fromApp.ToJsonString(),
                    tool + " differs between a file and the app: " + fromFile + " against " + fromApp
                );
            }
        }

        [Fact]
        public async Task Attached_select_takes_by_every_mode_and_the_users_selection_is_left_as_it_was()
        {
            using TestApp app = TestApp.Start();
            await using McpHarness harness = await Opened(app, "attach-select");
            string outline = await McpQueryTests.First(harness, "LWPOLYLINE", "Plate");
            string circle = await McpQueryTests.First(harness, "CIRCLE");
            string line = await McpQueryTests.First(harness, "LINE");
            string text = await McpQueryTests.First(harness, "TEXT");
            CadRemoteDocument document = ((RemoteBackend)harness.Session.Backend).Document;
            document.Select(new[] { circle });

            JsonNode window = await harness.CallAsync(
                "select",
                ("mode", "window"),
                ("points", new[] { new[] { -1.0, -1.0 }, new[] { 201.0, 101.0 } })
            );
            JsonNode crossing = await harness.CallAsync(
                "select",
                ("mode", "crossing"),
                ("points", new[] { new[] { 150.0, 40.0 }, new[] { 250.0, 60.0 } })
            );
            JsonNode fence = await harness.CallAsync(
                "select",
                ("mode", "fence"),
                ("points", new[] { new[] { 100.0, -10.0 }, new[] { 100.0, 110.0 } })
            );
            JsonNode polygon = await harness.CallAsync(
                "select",
                ("mode", "crossing_polygon"),
                ("points", new[] { new[] { 150.0, 40.0 }, new[] { 250.0, 40.0 }, new[] { 250.0, 60.0 } })
            );
            JsonNode previous = await harness.CallAsync("select", ("mode", "previous"));
            JsonNode point = await harness.CallAsync("id_point", ("point", new[] { 100.0, 70.0 }));

            Assert.Equal(new[] { outline, circle, line, text }, Strings(window["handles"]));
            Assert.Equal(new[] { outline, line }, Strings(crossing["handles"]));
            Assert.Equal(new[] { outline, circle, line }, Strings(fence["handles"]));
            Assert.Contains(outline, Strings(polygon["handles"]));
            Assert.Equal(new[] { circle }, Strings(previous["handles"]));
            Assert.Equal(new[] { circle }, point["entities"].AsArray().Select(e => (string)e["handle"]).ToArray());
            Assert.Equal(new[] { circle }, document.GetSelectedObjects().Select(document.HandleOf).ToArray());
        }

        [Fact]
        public async Task Attached_list_properties_and_measuring_work_and_the_apps_command_line_is_not_written_to()
        {
            using TestApp app = TestApp.Start();
            await using McpHarness harness = await Opened(app, "attach-list");
            string line = await McpQueryTests.First(harness, "LINE");
            string circle = await McpQueryTests.First(harness, "CIRCLE");

            JsonNode listed = await harness.CallAsync("list_properties", ("handles", new[] { line }));
            JsonNode area = await harness.CallAsync("measure_area", ("handle", circle));
            JsonNode length = await harness.CallAsync("measure_length", ("handle", line));
            JsonNode status = await harness.CallAsync("info");

            string text = string.Join("\n", Strings(listed["lines"]));
            Assert.Contains("Length = 200.0000", text);
            Assert.Contains("Layer: \"Plate\"", text);
            Assert.Equal(System.Math.PI * 400.0, (double)area["area"], 3);
            Assert.Equal(200.0, (double)length["length"], 4);
            Assert.False((bool)status["modified"]);
            Assert.Contains(
                "no object FFFF",
                await harness.FailAsync("list_properties", ("handles", new[] { "FFFF" }))
            );
        }

        [Fact]
        public async Task Attached_set_variable_changes_the_drawing_of_the_app_as_one_undo_step_and_is_refused_read_only()
        {
            using TestApp app = TestApp.Start();
            await using McpHarness harness = await Opened(app, "attach-setvar");
            await using McpHarness readOnly = await Attach(app, readOnly: true);

            JsonNode set = await harness.CallAsync("set_variable", ("name", "LTSCALE"), ("value", 3.0));
            string refused = await readOnly.FailAsync("set_variable", ("name", "LTSCALE"), ("value", 4.0));
            string unlisted = await harness.FailAsync("set_variable", ("name", "OSMODE"), ("value", 0));
            CadRemoteDocument document = ((RemoteBackend)harness.Session.Backend).Document;

            Assert.Equal(3.0, (double)set["value"], 6);
            Assert.Equal(3.0, (double)document.GetVariable("LTSCALE"), 6);
            Assert.Contains("set_variable is refused", refused);
            Assert.Contains("OSMODE is not one set_variable sets", unlisted);

            await harness.CallAsync("undo");
            Assert.Equal(1.0, (double)document.GetVariable("LTSCALE"), 6);
        }

        [Fact]
        public async Task Attached_a_snapshot_is_reset_to_in_the_app_and_a_read_only_server_cannot_reset()
        {
            using TestApp app = TestApp.Start();
            await using McpHarness harness = await Opened(app, "attach-snapshot");
            await using McpHarness readOnly = await Attach(app, readOnly: true);
            int before = (int)(await harness.CallAsync("info"))["entityCount"];

            JsonNode taken = await harness.CallAsync("snapshot");
            await harness.CallAsync("add_text", ("text", "OVER ONE"), ("at", new[] { 80.0, 48.0 }), ("height", 5.0));
            await harness.CallAsync("add_text", ("text", "OVER TWO"), ("at", new[] { 82.0, 49.0 }), ("height", 5.0));
            Assert.True((int)(await harness.CallAsync("overlaps"))["total"] >= 1);
            Assert.Equal(before + 2, (int)(await harness.CallAsync("info"))["entityCount"]);

            string refused = await readOnly.FailAsync("reset", ("snapshot", (string)taken["snapshot"]));
            JsonNode reset = await harness.CallAsync("reset", ("snapshot", (string)taken["snapshot"]));

            Assert.Contains("reset is refused", refused);
            Assert.Equal("reset", (string)reset["done"]);
            Assert.Equal(before, (int)(await harness.CallAsync("info"))["entityCount"]);
            JsonNode texts = await harness.CallAsync("query_entities", ("kinds", new[] { "TEXT" }));
            Assert.Equal(
                new[] { "PLATE" },
                texts["entities"].AsArray().Select(e => (string)e["geometry"]["text"]).ToArray()
            );
            Assert.Equal(0, (int)(await harness.CallAsync("overlaps"))["total"]);

            await harness.CallAsync("release_snapshot", ("snapshot", (string)taken["snapshot"]));
            Assert.Contains(
                "There is no snapshot",
                await harness.FailAsync("reset", ("snapshot", (string)taken["snapshot"]))
            );
        }

        [Fact]
        public async Task Attached_a_snapshot_of_one_drawing_is_refused_for_another_and_the_status_says_how_it_stands()
        {
            using TestApp app = TestApp.Start();
            await using McpHarness harness = await Opened(app, "attach-closed");
            string id = (string)(await harness.CallAsync("snapshot"))["snapshot"];
            JsonNode status = await harness.CallAsync("server_status");

            await harness.CallAsync("new_drawing");
            string message = await harness.FailAsync("reset", ("snapshot", id));

            Assert.Equal("app", (string)status["mode"]);
            Assert.True((bool)status["drawingOpen"]);
            Assert.Equal("sheet.dxf", (string)status["drawing"]["name"]);
            Assert.Equal(new[] { id }, Strings(status["snapshots"]));
            Assert.Contains("another drawing", message);
        }

        [Fact]
        public async Task Attached_server_status_without_an_app_says_so_and_the_server_stays_up()
        {
            string folder = TestFolder.Make("attach-status-noapp");
            await using McpHarness harness = await McpHarness.StartAsync(
                new McpOptions { Attach = true, RemoteFolder = folder }
            );

            JsonNode status = await harness.CallAsync("server_status");

            Assert.Equal("app", (string)status["mode"]);
            Assert.False((bool)status["drawingOpen"]);
            Assert.Contains("No OpenCAD2D is running", (string)status["problem"]);
        }

        [Fact]
        public async Task Attached_list_styles_and_blocks_read_a_copy_of_the_apps_drawing_with_changes_not_saved()
        {
            using TestApp app = TestApp.Start();
            await using McpHarness harness = await Opened(app, "attach-copy");
            await harness.CallAsync("add_layer", ("name", "Steel"));
            await harness.CallAsync("add_text", ("text", "UNSAVED"), ("at", new[] { 5.0, 5.0 }), ("height", 2.0));

            JsonNode layouts = await harness.CallAsync("list_layouts");
            JsonNode texts = await harness.CallAsync("get_texts", ("contains", "unsaved"));
            JsonNode block = await harness.CallAsync("get_block", ("name", "TITLE"));

            Assert.Equal("Model", (string)layouts[0]["name"]);
            Assert.Equal(1, (int)texts["total"]);
            Assert.Equal(2, block["attributeDefinitions"].AsArray().Count);
        }
    }
}
