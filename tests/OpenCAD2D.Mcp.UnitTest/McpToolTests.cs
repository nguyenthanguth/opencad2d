using System;
using System.IO;
using System.Linq;
using System.Text.Json.Nodes;
using System.Threading.Tasks;
using GeometryHelper.Dxf;
using GeometryHelper.Geometry;
using ModelContextProtocol.Protocol;
using Xunit;

namespace OpenCAD2D.Mcp.UnitTest
{
    /// <summary>The tools one at a time over a drawing file: what each gives, what it changes and what it refuses.</summary>
    public class McpToolTests
    {
        private static async Task<McpHarness> Plate(string name, bool readOnly = false, bool scripts = false)
        {
            string folder = TestFolder.Make(name);
            return await McpHarness.StartAsync(
                new McpOptions
                {
                    File = Drawings.Plate(folder),
                    ReadOnly = readOnly,
                    AllowScripts = scripts,
                }
            );
        }

        private static async Task<int> Count(McpHarness harness) =>
            (int)(await harness.CallAsync("info"))["entityCount"];

        private static (int Width, int Height) PngSize(byte[] png)
        {
            Assert.True(png.Length > 24);
            Assert.Equal(new byte[] { 0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A }, png.Take(8).ToArray());
            int w = (png[16] << 24) | (png[17] << 16) | (png[18] << 8) | png[19];
            int h = (png[20] << 24) | (png[21] << 16) | (png[22] << 8) | png[23];
            return (w, h);
        }

        [Fact]
        public async Task Render_png_gives_an_image_of_the_size_asked_for()
        {
            await using McpHarness harness = await Plate("render");

            CallToolResult result = await harness.CallRawAsync("render_png", ("width", 400), ("height", 300));

            Assert.True(result.IsError != true);
            ImageContentBlock image = Assert.Single(result.Content.OfType<ImageContentBlock>());
            Assert.Equal("image/png", image.MimeType);
            Assert.Equal((400, 300), PngSize(image.DecodedData.ToArray()));
        }

        [Fact]
        public async Task Render_png_follows_the_shape_of_the_part_when_only_the_width_is_given()
        {
            await using McpHarness harness = await Plate("renderbox");

            CallToolResult result = await harness.CallRawAsync(
                "render_png",
                ("width", 600),
                ("box", new[] { 0.0, 0.0, 200.0, 100.0 })
            );

            (int width, int height) = PngSize(
                Assert.Single(result.Content.OfType<ImageContentBlock>()).DecodedData.ToArray()
            );
            Assert.Equal(600, width);
            Assert.Equal(300, height);
        }

        [Fact]
        public async Task Render_png_is_1024_wide_where_no_size_is_given()
        {
            await using McpHarness harness = await Plate("render1024");

            CallToolResult result = await harness.CallRawAsync("render_png");

            (int width, _) = PngSize(Assert.Single(result.Content.OfType<ImageContentBlock>()).DecodedData.ToArray());
            Assert.Equal(1024, width);
        }

        [Fact]
        public async Task Overlaps_finds_a_text_written_across_a_line()
        {
            await using McpHarness harness = await Plate("overlaps");
            JsonNode text = await harness.CallAsync(
                "add_text",
                ("text", "OVER"),
                ("at", new[] { 80.0, 48.0 }),
                ("height", 5.0)
            );

            JsonNode found = await harness.CallAsync("overlaps");

            JsonNode[] mine = found["rows"].AsArray().Where(r => (string)r["word"] == (string)text["handle"]).ToArray();
            Assert.NotEmpty(mine);
            Assert.All(mine, r => Assert.Equal(4, r["box"].AsArray().Count));
            Assert.Equal(found["rows"].AsArray().Count, (int)found["total"]);
        }

        [Fact]
        public async Task Overlaps_finds_nothing_in_the_plate_as_it_was_made()
        {
            await using McpHarness harness = await Plate("overlapsnone");

            JsonNode found = await harness.CallAsync("overlaps");

            Assert.Equal(0, (int)found["total"]);
        }

        [Fact]
        public async Task Bounds_gives_the_box_each_entity_takes()
        {
            await using McpHarness harness = await Plate("bounds");
            JsonNode all = await harness.CallAsync("query_entities", ("kinds", new[] { "CIRCLE" }));
            string circle = (string)all["entities"][0]["handle"];

            JsonNode box = await harness.CallAsync("bounds", ("handles", new[] { circle }));

            double[] corners = box["rows"][0]["box"].AsArray().Select(n => (double)n).ToArray();
            Assert.Equal(new[] { 80.0, 30.0, 120.0, 70.0 }, corners.Select(c => Math.Round(c, 2)).ToArray());
        }

        [Fact]
        public async Task Undo_takes_back_each_change_one_step_at_a_time()
        {
            await using McpHarness harness = await Plate("undo");
            Assert.Equal(4, await Count(harness));

            await harness.CallAsync("add_line", ("start", new[] { 0.0, 0.0 }), ("end", new[] { 5.0, 5.0 }));
            await harness.CallAsync("add_circle", ("center", new[] { 5.0, 5.0 }), ("radius", 2.0));
            Assert.Equal(6, await Count(harness));

            JsonNode first = await harness.CallAsync("undo");
            Assert.True((bool)first["undone"]);
            Assert.Equal(5, await Count(harness));
            await harness.CallAsync("undo");
            Assert.Equal(4, await Count(harness));
            JsonNode none = await harness.CallAsync("undo");
            Assert.False((bool)none["undone"]);
            Assert.Equal(4, await Count(harness));
        }

        [Fact]
        public async Task Move_rotate_scale_and_erase_change_the_entities_and_each_is_one_undo_step()
        {
            await using McpHarness harness = await Plate("edits");
            JsonNode circles = await harness.CallAsync("query_entities", ("kinds", new[] { "CIRCLE" }));
            string circle = (string)circles["entities"][0]["handle"];

            await harness.CallAsync("move", ("handles", new[] { circle }), ("delta", new[] { 10.0, -5.0 }));
            JsonNode moved = (await harness.CallAsync("query_entities", ("kinds", new[] { "CIRCLE" })))["entities"][0];
            Assert.Equal(110.0, (double)moved["geometry"]["center"][0], 3);
            Assert.Equal(45.0, (double)moved["geometry"]["center"][1], 3);

            await harness.CallAsync(
                "rotate",
                ("handles", new[] { circle }),
                ("base_point", new[] { 0.0, 0.0 }),
                ("angle", 90.0)
            );
            JsonNode turned = (await harness.CallAsync("query_entities", ("kinds", new[] { "CIRCLE" })))["entities"][0];
            Assert.Equal(-45.0, (double)turned["geometry"]["center"][0], 3);
            Assert.Equal(110.0, (double)turned["geometry"]["center"][1], 3);

            await harness.CallAsync(
                "scale",
                ("handles", new[] { circle }),
                ("base_point", new[] { 0.0, 0.0 }),
                ("factor", 2.0)
            );
            JsonNode scaled = (await harness.CallAsync("query_entities", ("kinds", new[] { "CIRCLE" })))["entities"][0];
            Assert.Equal(40.0, (double)scaled["geometry"]["radius"], 3);
            Assert.Equal(-90.0, (double)scaled["geometry"]["center"][0], 3);

            await harness.CallAsync("undo");
            JsonNode back = (await harness.CallAsync("query_entities", ("kinds", new[] { "CIRCLE" })))["entities"][0];
            Assert.Equal(20.0, (double)back["geometry"]["radius"], 3);

            JsonNode erased = await harness.CallAsync("erase", ("handles", new[] { circle }));
            Assert.Equal(1, (int)erased["changed"]);
            Assert.Equal(3, await Count(harness));
            await harness.CallAsync("undo");
            Assert.Equal(4, await Count(harness));
        }

        [Fact]
        public async Task Set_properties_changes_the_layer_and_colour_in_one_undo_step()
        {
            await using McpHarness harness = await Plate("props");
            JsonNode circles = await harness.CallAsync("query_entities", ("kinds", new[] { "CIRCLE" }));
            string circle = (string)circles["entities"][0]["handle"];

            await harness.CallAsync("set_properties", ("handles", new[] { circle }), ("layer", "Notes"), ("color", 1));

            JsonNode after = (await harness.CallAsync("query_entities", ("kinds", new[] { "CIRCLE" })))["entities"][0];
            Assert.Equal("Notes", (string)after["layer"]);

            await harness.CallAsync("undo");
            JsonNode back = (await harness.CallAsync("query_entities", ("kinds", new[] { "CIRCLE" })))["entities"][0];
            Assert.Equal("Holes", (string)back["layer"]);
        }

        [Fact]
        public async Task Every_add_tool_makes_its_entity_and_each_call_is_one_undo_step()
        {
            await using McpHarness harness = await Plate("adds");
            var p = new[] { 10.0, 10.0 };

            (string Tool, string Kind, (string, object)[] Args)[] adds =
            {
                (
                    "add_polyline",
                    "LWPOLYLINE",
                    new (string, object)[]
                    {
                        ("points", new[] { new[] { 0.0, 0.0 }, new[] { 10.0, 0.0 }, new[] { 10.0, 10.0 } }),
                        ("closed", true),
                    }
                ),
                ("add_circle", "CIRCLE", new (string, object)[] { ("center", p), ("radius", 3.0) }),
                (
                    "add_arc",
                    "ARC",
                    new (string, object)[] { ("center", p), ("radius", 3.0), ("start_angle", 0.0), ("end_angle", 90.0) }
                ),
                (
                    "add_mtext",
                    "MTEXT",
                    new (string, object)[] { ("text", "A\\PB"), ("at", p), ("height", 3.0), ("width", 40.0) }
                ),
                (
                    "add_aligned_dim",
                    "DIMENSION",
                    new (string, object)[]
                    {
                        ("first", new[] { 0.0, 0.0 }),
                        ("second", new[] { 30.0, 40.0 }),
                        ("location", new[] { 35.0, 5.0 }),
                    }
                ),
                (
                    "add_radial_dim",
                    "DIMENSION",
                    new (string, object)[]
                    {
                        ("center", new[] { 100.0, 50.0 }),
                        ("radius", 20.0),
                        ("location", new[] { 130.0, 70.0 }),
                    }
                ),
                (
                    "add_diameter_dim",
                    "DIMENSION",
                    new (string, object)[]
                    {
                        ("center", new[] { 100.0, 50.0 }),
                        ("radius", 20.0),
                        ("location", new[] { 130.0, 70.0 }),
                    }
                ),
                (
                    "add_angular_dim",
                    "DIMENSION",
                    new (string, object)[]
                    {
                        ("vertex", new[] { 0.0, 0.0 }),
                        ("first", new[] { 100.0, 0.0 }),
                        ("second", new[] { 0.0, 100.0 }),
                        ("location", new[] { 50.0, 50.0 }),
                    }
                ),
                (
                    "add_hatch",
                    "HATCH",
                    new (string, object)[]
                    {
                        (
                            "boundary",
                            new[]
                            {
                                new[] { 0.0, 0.0 },
                                new[] { 100.0, 0.0 },
                                new[] { 100.0, 50.0 },
                                new[] { 0.0, 50.0 },
                            }
                        ),
                    }
                ),
            };

            int expected = 4;

            foreach ((string tool, string kind, (string, object)[] args) in adds)
            {
                JsonNode result = await harness.CallAsync(tool, args);
                expected++;
                Assert.Equal(kind, (string)result["kind"]);
                Assert.Equal(expected, await Count(harness));
                JsonNode found = await harness.CallAsync("query_entities", ("kinds", new[] { kind }));
                Assert.Contains(found["entities"].AsArray(), e => (string)e["handle"] == (string)result["handle"]);
            }

            for (int i = adds.Length; i > 0; i--)
            {
                await harness.CallAsync("undo");
                Assert.Equal(3 + i, await Count(harness));
            }
        }

        [Fact]
        public async Task Add_insert_places_a_block_of_the_drawing_and_refuses_one_it_has_not()
        {
            string folder = TestFolder.Make("insert");
            var database = new DxfDatabase("AC1027");
            var bolt = new DxfBlockTableRecord { Name = "BOLT" };
            database.BlockTable.Add(bolt);
            bolt.AppendEntity(new DxfCircle { Center = new GeoPoint3(0, 0, 0), Radius = 3 });
            string path = Path.Combine(folder, "blocks.dxf");
            CadDocument.From(database).SaveAs(path);
            await using McpHarness harness = await McpHarness.StartAsync(new McpOptions { File = path });

            JsonNode blocks = await harness.CallAsync("list_blocks");
            Assert.Equal("BOLT", (string)blocks[0]["name"]);
            Assert.Equal(1, (int)blocks[0]["entities"]);

            JsonNode placed = await harness.CallAsync(
                "add_insert",
                ("name", "BOLT"),
                ("at", new[] { 50.0, 20.0 }),
                ("scale", 2.0),
                ("rotation", 90.0)
            );
            Assert.Equal("INSERT", (string)placed["kind"]);
            JsonNode insert = (await harness.CallAsync("query_entities", ("kinds", new[] { "INSERT" })))["entities"][0];
            Assert.Equal("BOLT", (string)insert["geometry"]["block"]);
            Assert.Equal(2.0, (double)insert["geometry"]["scale"], 3);
            Assert.Equal(90.0, (double)insert["geometry"]["rotation"], 3);

            string refused = await harness.FailAsync("add_insert", ("name", "NUT"), ("at", new[] { 0.0, 0.0 }));
            Assert.Contains("no block NUT", refused);
        }

        [Fact]
        public async Task The_style_lists_give_the_names_the_add_tools_take()
        {
            await using McpHarness harness = await Plate("styles");

            JsonNode text = await harness.CallAsync("list_text_styles");
            JsonNode dims = await harness.CallAsync("list_dimension_styles");

            Assert.Contains(text.AsArray(), s => (string)s["name"] == "Standard");
            Assert.Contains(dims.AsArray(), s => (bool)s["current"]);
            string refused = await harness.FailAsync(
                "add_text",
                ("text", "x"),
                ("at", new[] { 0.0, 0.0 }),
                ("height", 2.5),
                ("text_style", "NoSuchStyle")
            );
            Assert.Contains("no text style NoSuchStyle", refused);
        }

        [Fact]
        public async Task A_refusal_names_what_is_wrong_in_words_an_agent_can_act_on()
        {
            await using McpHarness harness = await Plate("refusals");

            Assert.Contains(
                "is a point",
                await harness.FailAsync("add_line", ("start", new[] { 1.0 }), ("end", new[] { 0.0, 0.0 }))
            );
            Assert.Contains(
                "above nought",
                await harness.FailAsync("add_circle", ("center", new[] { 0.0, 0.0 }), ("radius", -1.0))
            );
            Assert.Contains("has no object ZZZ", await harness.FailAsync("erase", ("handles", new[] { "ZZZ" })));
            Assert.Contains(
                "no layer Nope",
                await harness.FailAsync("set_properties", ("handles", new[] { "2" }), ("layer", "Nope"))
            );
            Assert.Contains("no layout named Sheet9", await harness.FailAsync("query_entities", ("layout", "Sheet9")));
            Assert.Contains("page is numbered from 1", await harness.FailAsync("query_entities", ("page", 0)));
            Assert.Equal(4, await Count(harness));
        }

        [Fact]
        public async Task A_long_answer_is_paged()
        {
            await using McpHarness harness = await Plate("paging");

            JsonNode first = await harness.CallAsync("query_entities", ("page_size", 3));
            JsonNode second = await harness.CallAsync("query_entities", ("page_size", 3), ("page", 2));

            Assert.Equal(4, (int)first["total"]);
            Assert.Equal(2, (int)first["pages"]);
            Assert.Equal(3, first["entities"].AsArray().Count);
            Assert.Single(second["entities"].AsArray());
            Assert.DoesNotContain(first["entities"].AsArray().Select(e => (string)e["handle"]), h =>
                h == (string)second["entities"][0]["handle"]
            );
        }

        [Fact]
        public async Task Save_writes_the_drawing_to_its_file_and_to_a_path_given()
        {
            string folder = TestFolder.Make("save");
            string path = Drawings.Plate(folder);
            await using McpHarness harness = await McpHarness.StartAsync(new McpOptions { File = path });
            await harness.CallAsync("add_line", ("start", new[] { 0.0, 0.0 }), ("end", new[] { 7.0, 7.0 }));
            Assert.True((bool)(await harness.CallAsync("info"))["modified"]);

            JsonNode saved = await harness.CallAsync("save");

            Assert.False((bool)saved["modified"]);
            Assert.Equal(5, CadDocument.Open(path).ModelSpace.Block.Count(e => !e.IsErased));

            string copy = Path.Combine(folder, "copy.dxf");
            JsonNode elsewhere = await harness.CallAsync("save", ("path", copy));
            Assert.Equal(copy, (string)elsewhere["path"]);
            Assert.Equal(5, CadDocument.Open(copy).ModelSpace.Block.Count(e => !e.IsErased));
        }

        [Fact]
        public async Task Save_of_a_new_drawing_with_no_path_says_to_give_one_and_writes_nothing()
        {
            await using McpHarness harness = await McpHarness.StartAsync(new McpOptions());
            await harness.CallAsync("new_drawing");

            string refused = await harness.FailAsync("save");

            Assert.Contains("give save a path", refused);
        }

        [Fact]
        public async Task Query_entities_keeps_what_the_filters_let_through_in_the_drawings_order()
        {
            await using McpHarness harness = await Plate("filters");

            JsonNode onPlate = await harness.CallAsync("query_entities", ("layers", new[] { "plate" }));
            JsonNode both = await harness.CallAsync("query_entities", ("layers", new[] { "Notes", "Holes" }));
            JsonNode none = await harness.CallAsync("query_entities", ("kinds", new[] { "ARC" }));

            Assert.Equal(
                new[] { "LWPOLYLINE", "LINE" },
                onPlate["entities"].AsArray().Select(e => (string)e["kind"]).ToArray()
            );
            Assert.Equal(
                new[] { "CIRCLE", "TEXT" },
                both["entities"].AsArray().Select(e => (string)e["kind"]).ToArray()
            );
            Assert.Equal(0, (int)none["total"]);
            Assert.Equal(0, (int)none["pages"]);
        }

        [Fact]
        public async Task Add_layer_makes_a_layer_with_the_look_given_in_one_undo_step()
        {
            await using McpHarness harness = await Plate("addlayer");

            JsonNode made = await harness.CallAsync(
                "add_layer",
                ("name", "Steel"),
                ("color", 1),
                ("lineweight", 35),
                ("frozen", true),
                ("locked", true)
            );

            Assert.Equal("Steel", (string)made["name"]);
            JsonNode steel = (await harness.CallAsync("list_layers"))
                .AsArray()
                .Single(l => (string)l["name"] == "Steel");
            Assert.Equal(1, (int)steel["color"]);
            Assert.Equal("Continuous", (string)steel["linetype"]);
            Assert.Equal(35, (int)steel["lineWeight"]);
            Assert.True((bool)steel["frozen"]);
            Assert.True((bool)steel["locked"]);
            Assert.True((bool)steel["on"]);

            await harness.CallAsync("undo");
            Assert.DoesNotContain((await harness.CallAsync("list_layers")).AsArray(), l =>
                (string)l["name"] == "Steel"
            );
        }

        [Fact]
        public async Task Add_layer_refuses_a_name_the_drawing_has_a_bad_name_and_a_look_it_does_not_take()
        {
            await using McpHarness harness = await Plate("addlayerrefused");

            Assert.Contains("already exists", await harness.FailAsync("add_layer", ("name", "plate")));
            Assert.Contains("invalid layer name", await harness.FailAsync("add_layer", ("name", "a/b")));
            Assert.Contains("needs a name", await harness.FailAsync("add_layer", ("name", " ")));
            Assert.Contains(
                "no linetype DASHED",
                await harness.FailAsync("add_layer", ("name", "X"), ("linetype", "DASHED"))
            );
            Assert.Contains("none of AutoCAD", await harness.FailAsync("add_layer", ("name", "X"), ("lineweight", 33)));
            Assert.Contains("1 to 255", await harness.FailAsync("add_layer", ("name", "X"), ("color", 0)));
            Assert.Equal(4, (await harness.CallAsync("list_layers")).AsArray().Count);
        }

        [Fact]
        public async Task Nothing_reaches_a_file_before_save()
        {
            string folder = TestFolder.Make("nosave");
            string path = Drawings.Plate(folder);
            DateTime before = File.GetLastWriteTimeUtc(path);
            await using McpHarness harness = await McpHarness.StartAsync(new McpOptions { File = path });

            await harness.CallAsync("add_line", ("start", new[] { 0.0, 0.0 }), ("end", new[] { 7.0, 7.0 }));
            await harness.CallRawAsync("render_png", ("width", 200));

            Assert.Equal(before, File.GetLastWriteTimeUtc(path));
            Assert.Equal(new[] { "plate.dxf" }, Directory.GetFiles(folder).Select(Path.GetFileName).ToArray());
        }

        [Fact]
        public async Task Open_drawing_and_new_drawing_refuse_to_lose_unsaved_changes_unless_told_to()
        {
            string folder = TestFolder.Make("open");
            string path = Drawings.Plate(folder);
            await using McpHarness harness = await McpHarness.StartAsync(new McpOptions());
            Assert.Contains("No drawing is open", await harness.FailAsync("info"));

            JsonNode blank = await harness.CallAsync("new_drawing");
            Assert.Null((string)blank["path"]);
            await harness.CallAsync("add_circle", ("center", new[] { 0.0, 0.0 }), ("radius", 5.0));
            Assert.Contains("not saved", await harness.FailAsync("open_drawing", ("path", path)));

            JsonNode opened = await harness.CallAsync("open_drawing", ("path", path), ("discard", true));
            Assert.Equal("plate.dxf", (string)opened["name"]);
            Assert.Equal(4, await Count(harness));
            Assert.Contains(
                "There is no file",
                await harness.FailAsync("open_drawing", ("path", Path.Combine(folder, "missing.dxf")))
            );
        }

        [Fact]
        public async Task Read_only_refuses_every_tool_that_writes_and_leaves_the_reading_tools()
        {
            await using McpHarness harness = await Plate("readonly", readOnly: true);
            string[] handles = { "2" };
            var point = new[] { 0.0, 0.0 };

            (string Tool, (string, object)[] Args)[] writes =
            {
                ("add_line", new (string, object)[] { ("start", point), ("end", point) }),
                ("add_circle", new (string, object)[] { ("center", point), ("radius", 1.0) }),
                ("add_layer", new (string, object)[] { ("name", "Steel") }),
                ("add_text", new (string, object)[] { ("text", "x"), ("at", point), ("height", 1.0) }),
                ("move", new (string, object)[] { ("handles", handles), ("delta", point) }),
                ("rotate", new (string, object)[] { ("handles", handles), ("base_point", point), ("angle", 1.0) }),
                ("scale", new (string, object)[] { ("handles", handles), ("base_point", point), ("factor", 2.0) }),
                ("erase", new (string, object)[] { ("handles", handles) }),
                ("set_properties", new (string, object)[] { ("handles", handles), ("color", 1) }),
                ("undo", new (string, object)[] { }),
                ("save", new (string, object)[] { }),
            };

            foreach ((string tool, (string, object)[] args) in writes)
            {
                Assert.Contains("--read-only", await harness.FailAsync(tool, args));
            }

            Assert.Equal(4, await Count(harness));
            Assert.True((bool)(await harness.CallAsync("info"))["readOnly"]);
            Assert.False((bool)(await harness.CallAsync("info"))["modified"]);
            await harness.CallAsync("query_entities");
            await harness.CallAsync("overlaps");
            Assert.True((await harness.CallRawAsync("render_png", ("width", 100))).IsError != true);
        }

        [Fact]
        public async Task Run_command_is_refused_without_allow_scripts_even_when_the_command_is_harmless()
        {
            await using McpHarness harness = await Plate("noscripts");

            string refused = await harness.FailAsync("run_command", ("text", "REGEN"));

            Assert.Contains("--allow-scripts", refused);
            Assert.Equal(4, await Count(harness));
        }

        [Fact]
        public async Task Run_command_types_a_command_with_allow_scripts_and_is_still_refused_when_read_only()
        {
            await using (McpHarness harness = await Plate("scripts", scripts: true))
            {
                JsonNode result = await harness.CallAsync(
                    "run_command",
                    ("text", "LINE 0,0 30,40" + Drawings.TwoEnters)
                );

                Assert.NotNull((string)result["output"]);
                Assert.Equal(5, await Count(harness));
                JsonNode line = (await harness.CallAsync("query_entities", ("kinds", new[] { "LINE" })))["entities"]
                    .AsArray()
                    .Last();
                Assert.Equal(30.0, (double)line["geometry"]["end"][0], 3);
                Assert.Equal(40.0, (double)line["geometry"]["end"][1], 3);
            }

            await using McpHarness readOnly = await Plate("scriptsro", readOnly: true, scripts: true);
            Assert.Contains("--read-only", await readOnly.FailAsync("run_command", ("text", "REGEN")));
        }
    }
}
