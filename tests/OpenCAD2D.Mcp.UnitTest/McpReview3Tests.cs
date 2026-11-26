using System;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using System.Text.Json.Nodes;
using System.Threading.Tasks;
using GeometryHelper.Dxf;
using GeometryHelper.Geometry;
using ModelContextProtocol.Protocol;
using Xunit;

namespace OpenCAD2D.Mcp.UnitTest
{
    /// <summary>
    /// The faults the third review of the MCP server found, each with the call that showed it: plans and the files they write, the
    /// read-only gate of a plan, the tools a plan refuses, null arguments, and the small refusals.
    /// </summary>
    public class McpReview3Tests
    {
        private static Task<McpHarness> Plate(string name, bool readOnly = false) =>
            McpHarness.StartAsync(new McpOptions { File = Drawings.Plate(TestFolder.Make(name)), ReadOnly = readOnly });

        private static Task<JsonNode> Plan(McpHarness harness, string steps, bool dryRun = false, bool stop = true) =>
            harness.CallAsync(
                "run_plan",
                ("steps", JsonNode.Parse(steps)),
                ("dry_run", dryRun),
                ("stop_on_error", stop)
            );

        private static string Q(string path) => JsonValue.Create(path).ToJsonString();

        [Theory]
        [InlineData("export_svg", ".svg")]
        [InlineData("plot_pdf", ".pdf")]
        [InlineData("export_to_file", ".json")]
        public async Task A_tool_that_writes_a_file_is_refused_in_the_middle_of_a_plan_and_writes_nothing(
            string tool,
            string extension
        )
        {
            await using McpHarness harness = await Plate("planmidfile");
            string path = Path.Combine(TestFolder.Make("planmidfile-out"), "mid" + extension);
            string arguments =
                tool == "export_to_file"
                    ? "{\"what\":\"json\",\"path\":" + Q(path) + "}"
                    : "{\"path\":" + Q(path) + "}";
            string steps =
                "[{\"tool\":\"add_line\",\"arguments\":{\"start\":[0,0],\"end\":[5,5]}},{\"tool\":\""
                + tool
                + "\",\"arguments\":"
                + arguments
                + "},{\"tool\":\"info\"}]";

            foreach (bool dry in new[] { true, false })
            {
                JsonNode result = await Plan(harness, steps, dryRun: dry);

                Assert.False((bool)result["ok"]);
                Assert.Contains("only as its last step", (string)result["steps"][1]["error"]);
                Assert.False(File.Exists(path), tool + " wrote " + path + " (dry run " + dry + ")");
            }

            Assert.Equal(4, (int)(await harness.CallAsync("info"))["entityCount"]);
        }

        [Fact]
        public async Task The_files_at_the_end_of_a_plan_are_written_after_the_edits_are_kept_and_not_in_a_dry_run()
        {
            await using McpHarness harness = await Plate("plantrail");
            string folder = TestFolder.Make("plantrail-out");
            string json = Path.Combine(folder, "trail.json");
            string dump = "plan-trail-" + Guid.NewGuid().ToString("N") + ".dxf";
            string dumped = Path.Combine(Path.GetTempPath(), "OpenCAD2D", "mcp-debug", dump);
            string steps =
                "[{\"tool\":\"add_line\",\"arguments\":{\"start\":[0,0],\"end\":[5,5]}},"
                + "{\"tool\":\"export_to_file\",\"id\":\"json\",\"arguments\":{\"what\":\"json\",\"path\":"
                + Q(json)
                + "}},{\"tool\":\"dump_drawing\",\"id\":\"dump\",\"arguments\":{\"path\":"
                + Q(dump)
                + "}}]";

            try
            {
                JsonNode dry = await Plan(harness, steps, dryRun: true);

                Assert.True((bool)dry["ok"], dry.ToJsonString());
                Assert.False((bool)dry["steps"][1]["result"]["written"]);
                Assert.False((bool)dry["steps"][2]["result"]["written"]);
                Assert.False(File.Exists(json));
                Assert.False(File.Exists(dumped));

                JsonNode real = await Plan(harness, steps);

                Assert.True((bool)real["ok"], real.ToJsonString());
                Assert.True(File.Exists(json));
                Assert.True(File.Exists(dumped));
                Assert.Equal(5, (int)(await harness.CallAsync("info"))["entityCount"]);
            }
            finally
            {
                if (File.Exists(dumped))
                {
                    File.Delete(dumped);
                }
            }
        }

        [Fact]
        public async Task A_file_step_at_the_end_that_fails_says_the_edits_were_kept()
        {
            await using McpHarness harness = await Plate("planfailtrail");
            string folder = TestFolder.Make("planfailtrail-out");
            JsonNode result = await Plan(
                harness,
                "[{\"tool\":\"add_line\",\"arguments\":{\"start\":[0,0],\"end\":[5,5]}},"
                    + "{\"tool\":\"plot_pdf\",\"arguments\":{\"path\":"
                    + Q(Path.Combine(folder, "a.pdf"))
                    + ",\"layout\":\"Nowhere\"}},"
                    + "{\"tool\":\"export_svg\",\"arguments\":{\"path\":"
                    + Q(Path.Combine(folder, "b.svg"))
                    + "}}]"
            );

            Assert.False((bool)result["ok"]);
            Assert.False((bool)result["rolledBack"]);
            Assert.True((bool)result["changed"]);
            Assert.True((bool)result["steps"][2]["skipped"]);
            string message = (string)result["message"];
            Assert.Contains("kept as one undo step", message);
            Assert.DoesNotContain("changed nothing", message);
            Assert.Equal(5, (int)(await harness.CallAsync("info"))["entityCount"]);
            Assert.True((bool)(await harness.CallAsync("undo"))["undone"]);
            Assert.Equal(4, (int)(await harness.CallAsync("info"))["entityCount"]);
        }

        [Fact]
        public async Task A_read_only_plan_may_hold_the_tools_a_read_only_server_runs_alone()
        {
            await using McpHarness harness = await Plate("planreadonlyallowed", readOnly: true);
            string dump = "plan-ro-" + Guid.NewGuid().ToString("N") + ".dxf";
            string dumped = Path.Combine(Path.GetTempPath(), "OpenCAD2D", "mcp-debug", dump);

            try
            {
                JsonNode result = await Plan(
                    harness,
                    "[{\"tool\":\"audit\"},{\"tool\":\"zoom_extents\"},{\"tool\":\"release_snapshot\",\"arguments\":{\"snapshot\":\"s1\"}},"
                        + "{\"tool\":\"dump_drawing\",\"arguments\":{\"path\":"
                        + Q(dump)
                        + "}}]"
                );

                Assert.True((bool)result["ok"], result.ToJsonString());
                Assert.False((bool)result["changed"]);
                Assert.True(File.Exists(dumped));
            }
            finally
            {
                if (File.Exists(dumped))
                {
                    File.Delete(dumped);
                }
            }

            // What a read-only server refuses alone is refused in a plan, before anything runs.
            string fix = await harness.FailAsync(
                "run_plan",
                ("steps", JsonNode.Parse("[{\"tool\":\"audit\",\"arguments\":{\"fix\":true}}]"))
            );
            Assert.Contains("--read-only", fix);
            Assert.Contains("Nothing was run", fix);
        }

        [Theory]
        [InlineData("snapshot", "{}")]
        [InlineData("reset", "{\"snapshot\":\"s1\"}")]
        public async Task A_plan_refuses_snapshot_and_reset_by_name_before_anything_runs(string tool, string arguments)
        {
            await using McpHarness harness = await Plate("plansnapshot");

            JsonNode result = await Plan(
                harness,
                "[{\"tool\":\"add_line\",\"arguments\":{\"start\":[0,0],\"end\":[5,5]}},{\"tool\":\""
                    + tool
                    + "\",\"arguments\":"
                    + arguments
                    + "}]"
            );

            Assert.False((bool)result["ok"]);
            Assert.Contains(tool + " is not allowed in a plan", (string)result["steps"][1]["error"]);
            Assert.True((bool)result["steps"][0]["skipped"]);
            Assert.Equal(4, (int)(await harness.CallAsync("info"))["entityCount"]);
        }

        /// <summary>A drawing with a polyline whose normal is -Z, a polyline whose normal is +Y and a circle whose normal is -Z.</summary>
        private static string Facing(string name)
        {
            var database = new DxfDatabase("AC1027");
            var flipped = new DxfPolyline { Normal = new GeoVector3(0, 0, -1) };
            flipped.AddVertexAt(0, new GeoPoint2(10, 0), 1, 0, 0);
            flipped.AddVertexAt(1, new GeoPoint2(20, 0), 0, 0, 0);
            flipped.AddVertexAt(2, new GeoPoint2(20, 10), 0, 0, 0);
            flipped.AddVertexAt(3, new GeoPoint2(10, 10), 0, 0, 0);
            flipped.Closed = true;
            database.ModelSpace.AppendEntity(flipped);
            var tilted = new DxfPolyline { Normal = new GeoVector3(0, 1, 0) };
            tilted.AddVertexAt(0, new GeoPoint2(0, 0), 0, 0, 0);
            tilted.AddVertexAt(1, new GeoPoint2(5, 0), 0, 0, 0);
            tilted.AddVertexAt(2, new GeoPoint2(5, 5), 0, 0, 0);
            tilted.Closed = true;
            database.ModelSpace.AppendEntity(tilted);
            database.ModelSpace.AppendEntity(
                new DxfCircle
                {
                    Center = new GeoPoint3(-50, 20, 0),
                    Radius = 4,
                    Normal = new GeoVector3(0, 0, -1),
                }
            );
            string path = Path.Combine(TestFolder.Make(name), "facing.dxf");
            CadDocument.From(database).SaveAs(path);
            return path;
        }

        [Theory]
        [InlineData("file")]
        [InlineData("app")]
        public async Task A_hatch_of_a_polyline_that_faces_down_is_made_where_the_polyline_is(string mode)
        {
            string path = Facing("hatchdown");
            using TestApp app = mode == "app" ? TestApp.Start() : null;

            if (app != null)
            {
                await app.OpenAsync(path);
            }

            await using McpHarness harness = await McpHarness.StartAsync(
                mode == "app"
                    ? new McpOptions { Attach = true, RemoteFolder = app.Folder }
                    : new McpOptions { File = path }
            );
            JsonNode found = await harness.CallAsync("query_entities", ("kinds", new[] { "LWPOLYLINE", "CIRCLE" }));
            string[] handles = found["entities"].AsArray().Select(e => (string)e["handle"]).ToArray();
            string flipped = (string)
                found["entities"].AsArray().First(e => e["kind"].ToString() == "LWPOLYLINE")["handle"];

            // The polyline is at x -20 to -10 in the world, its arc dipping under y 0 to -5.
            JsonNode own = await harness.CallAsync("bounds", ("handles", new[] { flipped }));
            double[] expected = own["rows"][0]["box"].AsArray().Select(n => (double)n).ToArray();
            Assert.Equal(-20.0, expected[0], 2);
            Assert.Equal(-10.0, expected[2], 2);

            JsonNode hatch = await harness.CallAsync("add_hatch", ("handles", new[] { flipped }), ("pattern", "SOLID"));
            JsonNode box = await harness.CallAsync("bounds", ("handles", new[] { (string)hatch["handle"] }));
            double[] made = box["rows"][0]["box"].AsArray().Select(n => (double)n).ToArray();

            for (int i = 0; i < 4; i++)
            {
                Assert.Equal(expected[i], made[i], 2);
            }

            // A polyline in a plane that is not the plane of the drawing has no outline in it: refused in words.
            string tilted = handles.First(h =>
                h != flipped
                && (string)found["entities"].AsArray().First(e => (string)e["handle"] == h)["kind"] == "LWPOLYLINE"
            );
            Assert.Contains(
                "not the plane of the drawing",
                await harness.FailAsync("add_hatch", ("handles", new[] { tilted }))
            );

            // A circle that faces down is the same circle: its centre is in the world already.
            string circle = (string)found["entities"].AsArray().First(e => e["kind"].ToString() == "CIRCLE")["handle"];
            JsonNode disc = await harness.CallAsync("add_hatch", ("handles", new[] { circle }), ("pattern", "SOLID"));
            JsonNode discBox = await harness.CallAsync("bounds", ("handles", new[] { (string)disc["handle"] }));
            Assert.Equal(-54.0, (double)discBox["rows"][0]["box"][0], 2);
            Assert.Equal(-46.0, (double)discBox["rows"][0]["box"][2], 2);
        }

        [Fact]
        public async Task A_null_for_an_argument_that_has_a_default_leaves_it_out()
        {
            await using McpHarness harness = await Plate("nullargs");

            JsonNode hatch = await harness.CallAsync(
                "add_hatch",
                ("circle_center", new[] { 50.0, 50.0 }),
                ("circle_radius", 10.0),
                ("scale", null),
                ("pattern", null),
                ("layer", null)
            );
            Assert.Equal("HATCH", (string)hatch["kind"]);

            CallToolResult picture = await harness.CallRawAsync("render_png", ("width", null), ("layout", null));
            Assert.True(
                picture.IsError != true,
                string.Join(" ", picture.Content.OfType<TextContentBlock>().Select(t => t.Text))
            );

            // The same in a plan.
            JsonNode plan = await Plan(
                harness,
                "[{\"tool\":\"add_hatch\",\"arguments\":{\"circle_center\":[50,50],\"circle_radius\":10,\"scale\":null}}]"
            );
            Assert.True((bool)plan["ok"], plan.ToJsonString());

            // A null for an argument that has none is still said in words.
            string missing = await harness.FailAsync("add_line", ("start", null), ("end", new[] { 1.0, 1.0 }));
            Assert.Contains("start", missing);
        }

        [Fact]
        public async Task Attached_a_fixed_text_is_refused_by_the_app_where_a_command_started_after_the_look_and_nothing_is_typed()
        {
            using TestApp app = TestApp.Start();
            await using McpHarness harness = await McpHarness.StartAsync(
                new McpOptions
                {
                    Attach = true,
                    RemoteFolder = app.Folder,
                    AllowScripts = true,
                }
            );
            await harness.CallAsync("new_drawing");
            await harness.CallAsync("run_command", ("text", "LINE 0,0"));
            var backend = (RemoteBackend)harness.Session.Backend;
            int lines = (int)(await harness.CallAsync("command_history"))["total"];

            // The tools look first and refuse in words; this is the second look, in the app, where the text would be typed.
            OpenCAD2D.Remote.CadRemoteException refused = Assert.Throws<OpenCAD2D.Remote.CadRemoteException>(() =>
                backend.RunFixed("AUDIT")
            );

            Assert.Contains("command is running", refused.Message);
            Assert.Equal(lines, (int)(await harness.CallAsync("command_history"))["total"]);
            await harness.CallAsync("run_command", ("text", "10,10" + (char)10 + (char)10));
            Assert.Equal(1, (int)(await harness.CallAsync("info"))["counts"]["LINE"]);
        }

        [Fact]
        public async Task A_lineweight_below_nought_that_is_not_a_whole_negative_code_is_refused_and_minus_3_is_the_layers_default()
        {
            await using McpHarness harness = await Plate("lineweights");
            string line = (string)
                (await harness.CallAsync("add_line", ("start", new[] { 0.0, 0.0 }), ("end", new[] { 9.0, 9.0 })))[
                    "handle"
                ];

            Assert.Contains(
                "none of AutoCAD",
                await harness.FailAsync("set_properties", ("handles", new[] { line }), ("lineweight", -0.5))
            );
            Assert.Contains(
                "none of AutoCAD",
                await harness.FailAsync("add_layer", ("name", "Neg"), ("lineweight", -0.5))
            );

            // The refusal of a whole number speaks the unit the description does: millimetres.
            string whole = await harness.FailAsync("add_layer", ("name", "Whole"), ("lineweight", 77));
            Assert.Contains("0.77 mm", whole);
            Assert.Contains("0.05", whole);
            Assert.DoesNotContain("0, 5, 9", whole);

            // -3, the default weight the description names, is taken by add_layer as it is by set_layer.
            JsonNode layer = await harness.CallAsync("add_layer", ("name", "Default"), ("lineweight", -3));
            Assert.Equal(-3, (int)layer["lineWeight"]);
        }

        [Fact]
        public async Task A_viewport_scale_too_small_to_write_is_refused_in_words_and_nothing_is_made()
        {
            await using McpHarness harness = await Plate("tinyscale");
            await harness.CallAsync("create_layout", ("name", "Sheet1"));

            string message = await harness.FailAsync(
                "add_viewport",
                ("layout", "Sheet1"),
                ("frame", new[] { 10.0, 10.0, 200.0, 150.0 }),
                ("scale", 1e-16)
            );

            Assert.Contains("0.000000000000001 or more", message);
            Assert.Empty((await harness.CallAsync("list_layouts"))[1]["viewports"].AsArray());
        }

        [Fact]
        public async Task A_hatch_refuses_an_outline_with_no_area_a_handle_given_twice_and_an_outline_of_another_layout()
        {
            await using McpHarness harness = await Plate("hatchrefusals");
            await harness.CallAsync("create_layout", ("name", "Sheet1"));
            string plate = (string)
                (await harness.CallAsync("query_entities", ("kinds", new[] { "LWPOLYLINE" })))["entities"][0]["handle"];

            // Two corners with a bulge and its opposite go there and back, and with none are a line.
            Assert.Contains(
                "no area",
                await harness.FailAsync(
                    "add_hatch",
                    ("boundary", new[] { new[] { 0.0, 0.0 }, new[] { 10.0, 0.0 } }),
                    ("bulges", new[] { 1.0, -1.0 })
                )
            );
            Assert.Contains(
                "no area",
                await harness.FailAsync(
                    "add_hatch",
                    ("boundary", new[] { new[] { 0.0, 0.0 }, new[] { 10.0, 0.0 } }),
                    ("bulges", new[] { 0.0, 0.0 })
                )
            );

            // The same outline twice would fill and cancel.
            Assert.Contains(
                "given twice",
                await harness.FailAsync("add_hatch", ("handles", new[] { plate, plate.ToLowerInvariant() }))
            );

            // The plate is in model space, and a hatch in a sheet is made there.
            string other = await harness.FailAsync("add_hatch", ("handles", new[] { plate }), ("layout", "Sheet1"));
            Assert.Contains("not in the layout Sheet1", other);
            Assert.Equal(
                0,
                (int)
                    (await harness.CallAsync("query_entities", ("kinds", new[] { "HATCH" }), ("layout", "Sheet1")))[
                        "total"
                    ]
            );

            // A lens of two different arcs is an area.
            JsonNode lens = await harness.CallAsync(
                "add_hatch",
                ("boundary", new[] { new[] { 0.0, 0.0 }, new[] { 10.0, 0.0 } }),
                ("bulges", new[] { 1.0, 0.5 })
            );
            Assert.Equal("HATCH", (string)lens["kind"]);
        }

        [DllImport("kernel32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
        private static extern bool CreateHardLinkW(string link, string existing, IntPtr security);

        [Fact]
        public async Task A_path_that_names_the_open_drawings_file_another_way_is_refused_as_its_own()
        {
            string path = Drawings.Plate(TestFolder.Make("ownfile"));
            string link = Path.Combine(Path.GetDirectoryName(path), "same-file-by-another-name.dxf");
            Assert.True(CreateHardLinkW(link, path, IntPtr.Zero), "No hard link: " + Marshal.GetLastWin32Error());
            await using McpHarness harness = await McpHarness.StartAsync(new McpOptions { File = path });
            long before = new FileInfo(path).Length;

            string message = await harness.FailAsync("save_as", ("path", link), ("overwrite", true));

            Assert.Contains("own file", message);
            Assert.Equal(before, new FileInfo(path).Length);
        }

        [Fact]
        public async Task A_plot_or_a_picture_that_names_no_layout_or_paper_the_drawing_has_makes_no_folder()
        {
            await using McpHarness harness = await Plate("nofolder");
            string folder = Path.Combine(TestFolder.Make("nofolder-out"), "never", "made");

            Assert.Contains(
                "no layout named Nowhere",
                await harness.FailAsync("plot_pdf", ("path", Path.Combine(folder, "a.pdf")), ("layout", "Nowhere"))
            );
            await harness.FailAsync("plot_pdf", ("path", Path.Combine(folder, "b.pdf")), ("paper", "A9"));
            await harness.FailAsync("plot_pdf", ("path", Path.Combine(folder, "c.pdf")), ("scale", "big"));
            await harness.FailAsync(
                "plot_pdf",
                ("path", Path.Combine(folder, "d.pdf")),
                ("scale", "1:10"),
                ("fit", true)
            );
            await harness.FailAsync("export_svg", ("path", Path.Combine(folder, "e.svg")), ("layout", "Nowhere"));

            Assert.False(
                Directory.Exists(Path.GetDirectoryName(folder)),
                "The folder was made for a plot that was refused."
            );
        }

        [Theory]
        [InlineData("U")]
        [InlineData("undo 1")]
        [InlineData("_REDO")]
        [InlineData("QSAVE")]
        [InlineData(".saveas")]
        [InlineData("OPEN x.dxf")]
        [InlineData("NEW")]
        [InlineData("CLOSE")]
        [InlineData("QUIT")]
        [InlineData("SAVE")]
        public async Task A_plan_does_not_let_run_command_start_a_command_that_defeats_its_undo_step(string text)
        {
            await using McpHarness harness = await McpHarness.StartAsync(
                new McpOptions { File = Drawings.Plate(TestFolder.Make("plancmds")), AllowScripts = true }
            );

            JsonNode result = await Plan(
                harness,
                "[{\"tool\":\"add_line\",\"arguments\":{\"start\":[0,0],\"end\":[5,5]}},"
                    + "{\"tool\":\"run_command\",\"arguments\":{\"text\":"
                    + Q(text)
                    + "}}]"
            );

            Assert.False((bool)result["ok"]);
            Assert.Contains("is not allowed to start", (string)result["steps"][1]["error"]);
            Assert.Equal(4, (int)(await harness.CallAsync("info"))["entityCount"]);

            // A command that is no such, and a word of one that is, are typed as before.
            JsonNode fine = await Plan(
                harness,
                "[{\"tool\":\"run_command\",\"arguments\":{\"text\":\"CIRCLE 50,50 5\"}}]"
            );
            Assert.True((bool)fine["ok"], fine.ToJsonString());
        }
    }
}
