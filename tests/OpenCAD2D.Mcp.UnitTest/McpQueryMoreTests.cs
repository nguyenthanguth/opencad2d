using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json.Nodes;
using System.Threading.Tasks;
using GeometryHelper.Dxf;
using Xunit;

namespace OpenCAD2D.Mcp.UnitTest
{
    /// <summary>Layouts and styles, variables, measuring, selecting, status and the kinds, one tool at a time over a drawing file.</summary>
    public class McpQueryMoreTests
    {
        private static async Task<McpHarness> Sheet(string name, bool readOnly = false, bool scripts = false) =>
            await McpHarness.StartAsync(
                new McpOptions
                {
                    File = QueryDrawings.Sheet(TestFolder.Make(name)),
                    ReadOnly = readOnly,
                    AllowScripts = scripts,
                }
            );

        private static string[] Strings(JsonNode array) => array.AsArray().Select(n => (string)n).ToArray();

        private static double[] Numbers(JsonNode array) => array.AsArray().Select(n => (double)n).ToArray();

        [Fact]
        public async Task List_layouts_gives_the_sheet_size_units_and_the_viewport_scale_and_view_centre()
        {
            await using McpHarness harness = await McpHarness.StartAsync(new McpOptions { AllowScripts = true });
            await harness.CallAsync("new_drawing");
            CadDocument document = ((LocalBackend)harness.Session.Backend).Document;
            document.CurrentSpace = document.Spaces[1];
            await harness.CallAsync("run_command", ("text", "MV\n10,10\n210,110\n"));
            string viewport = document.CurrentSpace.Block.OfType<DxfViewport>().Last().Handle;
            Assert.True(
                document
                    .SetViewportView(viewport, new GeometryHelper.Geometry.GeoPoint2(1000, 500), 1000.0, "ZOOM")
                    .Succeeded
            );

            JsonNode layouts = await harness.CallAsync("list_layouts");

            JsonNode[] rows = layouts.AsArray().ToArray();
            Assert.Equal(new[] { "Model", "Layout1" }, rows.Select(r => (string)r["name"]).ToArray());
            Assert.Equal(new[] { 0, 1 }, rows.Select(r => (int)r["tabOrder"]).ToArray());
            Assert.True((bool)rows[1]["current"]);
            Assert.Null(rows[0]["paperSize"]);
            Assert.Equal(new[] { 420.0, 297.0 }, Numbers(rows[1]["paperSize"]));
            Assert.Equal("millimetres", (string)rows[1]["paperUnits"]);
            JsonNode frame = Assert.Single(rows[1]["viewports"].AsArray());
            Assert.Equal(viewport, (string)frame["handle"]);
            Assert.Equal("1:10", (string)frame["scale"]);
            Assert.Equal(0.1, (double)frame["customScale"], 6);
            Assert.Equal(new[] { 1000.0, 500.0 }, Numbers(frame["viewCenter"]));
            Assert.Equal(1000.0, (double)frame["viewHeight"], 3);
            Assert.Equal(new[] { 110.0, 60.0 }, Numbers(frame["center"]));
            Assert.Equal(200.0, (double)frame["width"], 3);
        }

        [Fact]
        public async Task List_linetypes_and_the_two_style_lists_give_what_the_drawing_has()
        {
            await using McpHarness harness = await Sheet("styles");
            CadDocument document = ((LocalBackend)harness.Session.Backend).Document;
            Assert.True(CadMLeaderStyleEditor.AddStandard(document).Succeeded);
            Assert.True(CadTableStyleEditor.AddStandard(document).Succeeded);

            JsonNode linetypes = await harness.CallAsync("list_linetypes");
            JsonNode leaders = await harness.CallAsync("list_multileader_styles");
            JsonNode tables = await harness.CallAsync("list_table_styles");

            string[] names = linetypes.AsArray().Select(l => (string)l["name"]).ToArray();
            Assert.Contains("Continuous", names, StringComparer.OrdinalIgnoreCase);
            Assert.Equal(names.OrderBy(n => n, StringComparer.OrdinalIgnoreCase).ToArray(), names);
            JsonNode continuous = linetypes
                .AsArray()
                .Single(l => string.Equals((string)l["name"], "Continuous", StringComparison.OrdinalIgnoreCase));
            Assert.Empty(continuous["dashes"].AsArray());
            JsonNode leader = leaders.AsArray().Single(l => (string)l["name"] == "Standard");
            Assert.Equal("straight", (string)leader["leaderType"]);
            Assert.Equal("text", (string)leader["content"]);
            Assert.True((double)leader["textHeight"] > 0.0);
            Assert.True((double)leader["arrowSize"] > 0.0);
            JsonNode table = tables.AsArray().Single(t => (string)t["name"] == "Standard");
            Assert.True((double)table["horizontalMargin"] > 0.0);
            Assert.True((bool)table["titleShown"]);
            Assert.NotNull(table["data"]["textStyle"]);
            Assert.True((double)table["data"]["textHeight"] > 0.0);
        }

        [Fact]
        public async Task Get_variables_reads_the_documented_ones_by_default_and_any_known_one_by_name()
        {
            await using McpHarness harness = await Sheet("variables");

            JsonNode documented = await harness.CallAsync("get_variables");
            JsonNode named = await harness.CallAsync(
                "get_variables",
                ("names", new[] { "ltscale", "CLAYER", "ANGBASE", "NOSUCH", "EXTMAX" })
            );

            JsonNode[] all = documented["variables"].AsArray().ToArray();
            Assert.Equal(SystemVariables.Documented.Count, all.Length);
            Assert.All(all, v => Assert.True(v["error"] == null, (string)v["name"] + ": " + (string)v["error"]));
            JsonNode[] rows = named["variables"].AsArray().ToArray();
            Assert.Equal(
                new[] { "LTSCALE", "CLAYER", "ANGBASE", "NOSUCH", "EXTMAX" },
                rows.Select(r => (string)r["name"]).ToArray()
            );
            Assert.Equal(1.0, (double)rows[0]["value"], 6);
            Assert.Equal("real", (string)rows[0]["kind"]);
            Assert.True((bool)rows[0]["settable"]);
            Assert.Equal("0", (string)rows[1]["value"]);
            Assert.Equal("angle", (string)rows[2]["kind"]);
            Assert.Equal(0.0, (double)rows[2]["value"], 6);
            Assert.Contains("NOSUCH", (string)rows[3]["error"]);
            Assert.Null(rows[3]["value"]);
            Assert.Equal("point", (string)rows[4]["kind"]);
            Assert.False((bool)rows[4]["settable"]);
            Assert.Equal(300.0 + 60.0, Numbers(rows[4]["value"])[0], 0);
        }

        [Fact]
        public async Task Set_variable_changes_a_documented_one_as_one_undo_step_and_refuses_the_rest()
        {
            await using McpHarness harness = await Sheet("setvar");

            JsonNode set = await harness.CallAsync("set_variable", ("name", "ltscale"), ("value", 2.5));
            JsonNode angle = await harness.CallAsync("set_variable", ("name", "ANGBASE"), ("value", 90));
            JsonNode layer = await harness.CallAsync("set_variable", ("name", "CLAYER"), ("value", "Plate"));

            Assert.Equal("LTSCALE", (string)set["name"]);
            Assert.Equal(2.5, (double)set["value"], 6);
            Assert.Equal(90.0, (double)angle["value"], 6);
            Assert.Equal("Plate", (string)layer["value"]);
            Assert.Equal("Plate", (string)(await harness.CallAsync("info"))["currentLayer"]);

            await harness.CallAsync("undo");
            Assert.Equal("0", (string)(await harness.CallAsync("info"))["currentLayer"]);
            Assert.Equal(
                90.0,
                (double)
                    (await harness.CallAsync("get_variables", ("names", new[] { "ANGBASE" })))["variables"][0]["value"],
                6
            );
            await harness.CallAsync("undo");
            await harness.CallAsync("undo");
            Assert.Equal(
                1.0,
                (double)
                    (await harness.CallAsync("get_variables", ("names", new[] { "LTSCALE" })))["variables"][0]["value"],
                6
            );

            string unlisted = await harness.FailAsync("set_variable", ("name", "OSMODE"), ("value", 0));
            string readOnly = await harness.FailAsync("set_variable", ("name", "EXTMAX"), ("value", 1));
            string wrong = await harness.FailAsync("set_variable", ("name", "LUPREC"), ("value", 2.5));
            string refused = await harness.FailAsync("set_variable", ("name", "CLAYER"), ("value", "NoSuchLayer"));
            Assert.Contains("OSMODE is not one set_variable sets", unlisted);
            Assert.Contains("LTSCALE", unlisted);
            Assert.Contains("EXTMAX is not one set_variable sets", readOnly);
            Assert.Contains("whole number", wrong);
            Assert.Contains("CLAYER", refused);
        }

        [Fact]
        public async Task Set_variable_is_refused_when_the_server_is_read_only_and_get_variables_still_reads()
        {
            await using McpHarness harness = await Sheet("setvar-ro", readOnly: true);

            string message = await harness.FailAsync("set_variable", ("name", "LTSCALE"), ("value", 2.0));
            JsonNode read = await harness.CallAsync("get_variables", ("names", new[] { "LTSCALE" }));

            Assert.Contains("set_variable is refused", message);
            Assert.Contains("--read-only", message);
            Assert.Equal(1.0, (double)read["variables"][0]["value"], 6);
        }

        [Fact]
        public async Task Measure_distance_gives_the_distance_angle_and_changes_as_DIST_does()
        {
            await using McpHarness harness = await Sheet("dist");

            JsonNode away = await harness.CallAsync(
                "measure_distance",
                ("a", new[] { 0.0, 0.0 }),
                ("b", new[] { 30.0, 40.0 })
            );
            JsonNode back = await harness.CallAsync(
                "measure_distance",
                ("a", new[] { 10.0, 10.0 }),
                ("b", new[] { 9.0, 9.0 })
            );

            Assert.Equal(50.0, (double)away["distance"], 4);
            Assert.Equal(53.1301, (double)away["angle"], 4);
            Assert.Equal(30.0, (double)away["deltaX"], 4);
            Assert.Equal(40.0, (double)away["deltaY"], 4);
            Assert.Equal(Math.Sqrt(2.0), (double)back["distance"], 4);
            Assert.Equal(225.0, (double)back["angle"], 4);
            Assert.Contains(
                "a is a point",
                await harness.FailAsync("measure_distance", ("a", new[] { 0.0 }), ("b", new[] { 1.0, 1.0 }))
            );
        }

        [Fact]
        public async Task Measure_area_and_length_of_points_and_of_entities()
        {
            await using McpHarness harness = await Sheet("area");
            string circle = await McpQueryTests.First(harness, "CIRCLE");
            string outline = await McpQueryTests.First(harness, "LWPOLYLINE", "Plate");
            JsonNode all = await harness.CallAsync("query_entities", ("kinds", new[] { "LWPOLYLINE" }));
            string open = (string)
                all["entities"].AsArray().Single(e => (int)e["geometry"]["vertexCount"] == 2)["handle"];

            JsonNode points = await harness.CallAsync(
                "measure_area",
                ("points", new[] { new[] { 0.0, 0.0 }, new[] { 10.0, 0.0 }, new[] { 10.0, 5.0 }, new[] { 0.0, 5.0 } })
            );
            JsonNode disc = await harness.CallAsync("measure_area", ("handle", circle));
            JsonNode plate = await harness.CallAsync("measure_area", ("handle", outline));
            JsonNode circumference = await harness.CallAsync("measure_length", ("handle", circle));
            JsonNode half = await harness.CallAsync("measure_length", ("handle", open));

            Assert.Equal(50.0, (double)points["area"], 4);
            Assert.Equal(30.0, (double)points["perimeter"], 4);
            Assert.Equal("points", (string)points["of"]);
            Assert.Equal(Math.PI * 400.0, (double)disc["area"], 3);
            Assert.Equal(Math.PI * 40.0, (double)disc["perimeter"], 3);
            Assert.Equal("CIRCLE", (string)disc["of"]);
            Assert.Equal(20000.0, (double)plate["area"], 3);
            Assert.Equal(600.0, (double)plate["perimeter"], 3);
            Assert.Equal(Math.PI * 40.0, (double)circumference["length"], 3);
            Assert.Equal(Math.PI * 50.0, (double)half["length"], 3);
            Assert.Contains("which is open", await harness.FailAsync("measure_area", ("handle", open)));
            Assert.Contains(
                "it has no length",
                await harness.FailAsync("measure_length", ("handle", await McpQueryTests.First(harness, "TEXT")))
            );
            Assert.Contains("points or a handle", await harness.FailAsync("measure_area"));
            Assert.Contains(
                "three points",
                await harness.FailAsync("measure_area", ("points", new[] { new[] { 0.0, 0.0 }, new[] { 1.0, 1.0 } }))
            );
        }

        [Fact]
        public async Task List_properties_gives_the_lines_LIST_writes_for_each_handle()
        {
            await using McpHarness harness = await Sheet("list");
            string line = await McpQueryTests.First(harness, "LINE");
            string circle = await McpQueryTests.First(harness, "CIRCLE");

            JsonNode listed = await harness.CallAsync("list_properties", ("handles", new[] { line, circle }));

            string text = string.Join("\n", Strings(listed["lines"]));
            Assert.Contains("LINE", text);
            Assert.Contains("Layer: \"Plate\"", text);
            Assert.Contains("Length = 200.0000", text);
            Assert.Contains("CIRCLE", text);
            Assert.Contains("radius", text, StringComparison.OrdinalIgnoreCase);
            Assert.Contains("Handle = " + line.ToLowerInvariant(), text);
            Assert.Contains(
                "no object FFFF",
                await harness.FailAsync("list_properties", ("handles", new[] { "FFFF" }))
            );
        }

        [Fact]
        public async Task Id_point_gives_the_point_and_the_entities_that_pass_within_the_aperture()
        {
            await using McpHarness harness = await Sheet("id");
            string circle = await McpQueryTests.First(harness, "CIRCLE");
            string line = await McpQueryTests.First(harness, "LINE");

            JsonNode onCircle = await harness.CallAsync("id_point", ("point", new[] { 100.0, 70.0 }));
            JsonNode crossing = await harness.CallAsync("id_point", ("point", new[] { 80.0, 50.0 }), ("aperture", 0.5));
            JsonNode nothing = await harness.CallAsync("id_point", ("point", new[] { 500.0, 500.0 }));

            Assert.Equal(100.0, (double)onCircle["x"], 4);
            Assert.Equal(70.0, (double)onCircle["y"], 4);
            Assert.Equal(new[] { circle }, onCircle["entities"].AsArray().Select(e => (string)e["handle"]).ToArray());
            Assert.NotNull(onCircle["entities"][0]["box"]);
            Assert.Equal(
                new[] { circle, line }.OrderBy(h => h, StringComparer.Ordinal).ToArray(),
                crossing["entities"]
                    .AsArray()
                    .Select(e => (string)e["handle"])
                    .OrderBy(h => h, StringComparer.Ordinal)
                    .ToArray()
            );
            Assert.Empty(nothing["entities"].AsArray());
            Assert.Contains(
                "aperture",
                await harness.FailAsync("id_point", ("point", new[] { 0.0, 0.0 }), ("aperture", 0.0))
            );
        }

        [Fact]
        public async Task Select_takes_by_window_crossing_fence_polygon_and_all_and_narrows_by_the_filters()
        {
            await using McpHarness harness = await Sheet("select");
            string outline = await McpQueryTests.First(harness, "LWPOLYLINE", "Plate");
            string circle = await McpQueryTests.First(harness, "CIRCLE");
            string line = await McpQueryTests.First(harness, "LINE");
            string text = await McpQueryTests.First(harness, "TEXT");

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
                ("mode", "polygon"),
                (
                    "points",
                    new[] { new[] { -1.0, -1.0 }, new[] { 201.0, -1.0 }, new[] { 201.0, 101.0 }, new[] { -1.0, 101.0 } }
                )
            );
            JsonNode touching = await harness.CallAsync(
                "select",
                ("mode", "crossing_polygon"),
                ("points", new[] { new[] { 150.0, 40.0 }, new[] { 250.0, 40.0 }, new[] { 250.0, 60.0 } })
            );
            JsonNode all = await harness.CallAsync("select", ("mode", "all"));
            JsonNode previous = await harness.CallAsync("select", ("mode", "previous"));
            JsonNode narrowed = await harness.CallAsync(
                "select",
                ("mode", "window"),
                ("points", new[] { new[] { -1.0, -1.0 }, new[] { 201.0, 101.0 } }),
                ("kinds", new[] { "CIRCLE", "LINE" })
            );

            // In the order the drawing draws them: the outline, the circle, the line, the text.
            Assert.Equal(new[] { outline, circle, line, text }, Strings(window["handles"]));
            Assert.Equal(4, (int)window["total"]);
            Assert.Equal("window", (string)window["mode"]);
            Assert.Equal(new[] { outline, line }, Strings(crossing["handles"]));
            Assert.Equal(new[] { outline, circle, line }, Strings(fence["handles"]));
            Assert.Equal(Strings(window["handles"]), Strings(polygon["handles"]));
            Assert.Contains(outline, Strings(touching["handles"]));
            Assert.Contains(line, Strings(touching["handles"]));
            Assert.DoesNotContain(circle, Strings(touching["handles"]));
            Assert.Equal(7, (int)all["total"]);
            Assert.Equal(0, (int)previous["total"]);
            Assert.Equal(new[] { circle, line }, Strings(narrowed["handles"]));
            Assert.Contains("mode is window", await harness.FailAsync("select", ("mode", "lasso")));
            Assert.Contains(
                "two corners",
                await harness.FailAsync("select", ("mode", "window"), ("points", new[] { new[] { 0.0, 0.0 } }))
            );
            Assert.Contains(
                "three points",
                await harness.FailAsync(
                    "select",
                    ("mode", "polygon"),
                    ("points", new[] { new[] { 0.0, 0.0 }, new[] { 1.0, 1.0 } })
                )
            );
        }

        [Fact]
        public async Task Select_similar_takes_the_entities_like_one_by_kind_layer_colour_or_style()
        {
            await using McpHarness harness = await Sheet("similar");
            string line = await McpQueryTests.First(harness, "LINE");
            string outline = await McpQueryTests.First(harness, "LWPOLYLINE", "Plate");
            string text = await McpQueryTests.First(harness, "TEXT");
            string mtext = await McpQueryTests.First(harness, "MTEXT");

            JsonNode byDefault = await harness.CallAsync("select_similar", ("handle", line));
            JsonNode byLayer = await harness.CallAsync(
                "select_similar",
                ("handle", line),
                ("match", new[] { "layer" })
            );
            JsonNode byKind = await harness.CallAsync(
                "select_similar",
                ("handle", outline),
                ("match", new[] { "kind" })
            );
            JsonNode byColour = await harness.CallAsync(
                "select_similar",
                ("handle", line),
                ("match", new[] { "color" })
            );
            JsonNode byStyle = await harness.CallAsync(
                "select_similar",
                ("handle", text),
                ("match", new[] { "text_style" })
            );

            Assert.Equal(new[] { line }, Strings(byDefault["handles"]));
            Assert.Equal(3, (int)byLayer["total"]);
            Assert.Equal(2, (int)byKind["total"]);
            Assert.Equal(new[] { line }, Strings(byColour["handles"]));
            Assert.Equal(new[] { text, mtext }, Strings(byStyle["handles"]));
            Assert.Contains(
                "match is any of",
                await harness.FailAsync("select_similar", ("handle", line), ("match", new[] { "size" }))
            );
        }

        [Fact]
        public async Task Server_status_tells_the_mode_flags_version_drawing_and_snapshots_even_with_no_drawing()
        {
            await using McpHarness empty = await McpHarness.StartAsync(new McpOptions { ReadOnly = true });
            await using McpHarness open = await Sheet("status", scripts: true);

            JsonNode none = await empty.CallAsync("server_status");
            JsonNode status = await open.CallAsync("server_status");
            await open.CallAsync("snapshot");
            JsonNode held = await open.CallAsync("server_status");

            Assert.Equal("OpenCAD2D", (string)none["server"]);
            Assert.False((bool)none["drawingOpen"]);
            Assert.True((bool)none["readOnly"]);
            Assert.Contains("No drawing is open", (string)none["problem"]);
            Assert.Equal("file", (string)status["mode"]);
            Assert.True((bool)status["drawingOpen"]);
            Assert.Equal("sheet.dxf", (string)status["drawing"]["name"]);
            Assert.False((bool)status["drawing"]["modified"]);
            Assert.False((bool)status["readOnly"]);
            Assert.True((bool)status["scriptsAllowed"]);
            Assert.Equal("Model", (string)status["currentLayout"]);
            Assert.False(string.IsNullOrEmpty((string)status["version"]));
            Assert.Empty(status["snapshots"].AsArray());
            Assert.Equal(new[] { "s1" }, Strings(held["snapshots"]));
        }

        [Fact]
        public async Task Describe_kinds_lists_every_kind_the_add_tools_write_with_its_fields()
        {
            await using McpHarness harness = await Sheet("kinds");

            JsonNode kinds = await harness.CallAsync("describe_kinds");

            JsonNode[] rows = kinds.AsArray().ToArray();
            string written = string.Join(", ", rows.Select(r => (string)r["written"]).Where(w => w != null));
            var tools = await harness.Client.ListToolsAsync();

            foreach (
                string tool in tools
                    .Select(t => t.Name)
                    .Where(n => n.StartsWith("add_", StringComparison.Ordinal) && n != "add_layer")
            )
            {
                Assert.Contains(tool, written);
            }

            JsonNode line = rows.Single(r => (string)r["kind"] == "LINE");
            Assert.Equal("add_line", (string)line["written"]);
            Assert.Contains("length", Strings(line["fields"]));
            Assert.Contains("start", Strings(line["arguments"]));
            Assert.Contains("layer", Strings(line["fields"]));
            Assert.Equal("add_ellipse", (string)rows.Single(r => (string)r["kind"] == "ELLIPSE")["written"]);
            Assert.Equal(rows.Select(r => (string)r["kind"]).Distinct().Count(), rows.Length);
        }

        [Fact]
        public async Task The_documented_kinds_and_fields_are_what_get_entity_gives()
        {
            await using McpHarness harness = await Sheet("fields");
            JsonNode kinds = await harness.CallAsync("describe_kinds");

            foreach (string kind in new[] { "LINE", "CIRCLE", "LWPOLYLINE", "TEXT", "MTEXT", "INSERT" })
            {
                string handle = await McpQueryTests.First(harness, kind);
                JsonNode entity = await harness.CallAsync("get_entity", ("handle", handle));
                string[] fields = Strings(kinds.AsArray().Single(k => (string)k["kind"] == kind)["fields"]);
                var details = (JsonObject)entity["details"];

                foreach (string key in details.Select(p => p.Key))
                {
                    Assert.True(
                        fields.Any(f => f == key || f.StartsWith(key + " ", StringComparison.Ordinal)),
                        kind + " gives " + key + " which describe_kinds does not name."
                    );
                }
            }
        }
    }
}
