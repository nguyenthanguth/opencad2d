using System.Linq;
using System.Text.Json.Nodes;
using System.Threading.Tasks;
using Xunit;

namespace OpenCAD2D.Mcp.UnitTest
{
    /// <summary>The read tools of phase 2 one at a time over a drawing file: what each gives and what it refuses.</summary>
    public class McpQueryTests
    {
        private static async Task<McpHarness> Sheet(string name, bool readOnly = false) =>
            await McpHarness.StartAsync(
                new McpOptions { File = QueryDrawings.Sheet(TestFolder.Make(name)), ReadOnly = readOnly }
            );

        /// <summary>The handle of the first entity of a kind, in the order the drawing draws them.</summary>
        internal static async Task<string> First(McpHarness harness, string kind, string layer = null)
        {
            JsonNode found = await harness.CallAsync(
                "query_entities",
                ("kinds", new[] { kind }),
                ("layers", layer == null ? null : new[] { layer })
            );
            return (string)found["entities"][0]["handle"];
        }

        private static double[] Numbers(JsonNode array) => array.AsArray().Select(n => (double)n).ToArray();

        [Fact]
        public async Task Get_entity_gives_a_closed_polyline_with_its_vertices_length_area_box_and_palette()
        {
            await using McpHarness harness = await Sheet("entity-polyline");
            string handle = await First(harness, "LWPOLYLINE", "Plate");

            JsonNode entity = await harness.CallAsync("get_entity", ("handle", handle));

            Assert.Equal(handle, (string)entity["handle"]);
            Assert.Equal("LWPOLYLINE", (string)entity["kind"]);
            Assert.Equal("Plate", (string)entity["layer"]);
            Assert.Equal("BYLAYER", (string)entity["color"]);
            JsonNode details = entity["details"];
            Assert.True((bool)details["closed"]);
            Assert.Equal(4, (int)details["vertexCount"]);
            Assert.Equal(new[] { 200.0, 100.0 }, Numbers(details["vertices"][2]["at"]));
            Assert.Equal(600.0, (double)details["length"], 3);
            Assert.Equal(20000.0, (double)details["area"], 3);
            Assert.Equal(new[] { 0.0, 0.0, 200.0, 100.0 }, Numbers(entity["box"]));
            Assert.Equal("Polyline (1)", (string)entity["properties"]["label"]);
            JsonNode geometry = entity["properties"]["groups"].AsArray().Single(g => (string)g["title"] == "Geometry");
            JsonNode length = geometry["rows"].AsArray().Single(r => (string)r["label"] == "Length");
            Assert.Equal("600.0000", (string)length["value"]);
            Assert.True((bool)length["readOnly"]);
        }

        [Fact]
        public async Task Get_entity_gives_the_bulges_of_a_polyline_and_the_colour_it_is_set_to()
        {
            await using McpHarness harness = await Sheet("entity-bulge");
            JsonNode all = await harness.CallAsync("query_entities", ("kinds", new[] { "LWPOLYLINE" }));
            string open = (string)
                all["entities"].AsArray().Single(e => (int)e["geometry"]["vertexCount"] == 2)["handle"];
            string line = await First(harness, "LINE");

            JsonNode arc = await harness.CallAsync("get_entity", ("handle", open));
            JsonNode coloured = await harness.CallAsync("get_entity", ("handle", line));

            Assert.Equal(1.0, (double)arc["details"]["vertices"][0]["bulge"], 6);
            Assert.False((bool)arc["details"]["closed"]);
            Assert.Equal(System.Math.PI * 50.0, (double)arc["details"]["length"], 3);
            Assert.Null(arc["details"]["area"]);
            Assert.Equal("1", (string)coloured["color"]);
        }

        [Fact]
        public async Task Get_entity_gives_the_words_height_style_and_columns_of_texts()
        {
            await using McpHarness harness = await Sheet("entity-text");

            JsonNode text = await harness.CallAsync("get_entity", ("handle", await First(harness, "TEXT")));
            JsonNode mtext = await harness.CallAsync("get_entity", ("handle", await First(harness, "MTEXT")));

            Assert.Equal("PLATE", (string)text["details"]["text"]);
            Assert.Equal(5.0, (double)text["details"]["height"], 6);
            Assert.Equal("Standard", (string)text["details"]["style"]);
            Assert.Equal(new[] { 10.0, 10.0 }, Numbers(text["details"]["at"]));
            Assert.Equal(80.0, (double)mtext["details"]["width"], 6);
            Assert.Equal(3.5, (double)mtext["details"]["height"], 6);
            Assert.Contains("Second line", (string)mtext["details"]["text"]);
            Assert.Contains("\\P", (string)mtext["details"]["contents"]);
            Assert.NotNull(mtext["details"]["columns"]["type"]);
            Assert.NotNull(text["box"]);
        }

        [Fact]
        public async Task Get_entity_gives_the_block_and_attributes_of_an_insert_and_a_dimension_with_its_measurement()
        {
            await using McpHarness harness = await Sheet("entity-insert");
            JsonNode dim = await harness.CallAsync(
                "add_linear_dim",
                ("first", new[] { 0.0, 0.0 }),
                ("second", new[] { 200.0, 0.0 }),
                ("location", new[] { 100.0, -20.0 })
            );

            JsonNode insert = await harness.CallAsync("get_entity", ("handle", await First(harness, "INSERT")));
            JsonNode dimension = await harness.CallAsync("get_entity", ("handle", (string)dim["handle"]));

            Assert.Equal("TITLE", (string)insert["details"]["block"]);
            JsonNode attributes = insert["details"]["attributes"];
            Assert.Equal(2, attributes.AsArray().Count);
            Assert.Equal(
                "PLATE 10 THK",
                (string)attributes.AsArray().Single(a => (string)a["tag"] == "TITLE")["value"]
            );
            Assert.Equal(new[] { 300.0, 0.0 }, Numbers(insert["details"]["at"]));
            Assert.Equal("RotatedDimension", (string)dimension["details"]["type"]);
            Assert.Equal(200.0, (double)dimension["details"]["measurement"], 6);
            Assert.Equal("mm", (string)dimension["details"]["unit"]);
            Assert.Equal("200", (string)dimension["details"]["text"]);
            Assert.Equal(new[] { 200.0, 0.0 }, Numbers(dimension["details"]["xLine2Point"]));
        }

        [Fact]
        public async Task Get_entity_refuses_a_handle_the_drawing_has_not_and_an_empty_one()
        {
            await using McpHarness harness = await Sheet("entity-missing");

            string missing = await harness.FailAsync("get_entity", ("handle", "FFFF"));
            string empty = await harness.FailAsync("get_entity", ("handle", " "));

            Assert.Contains("no object FFFF", missing);
            Assert.Contains("query_entities", missing);
            Assert.Contains("Give a handle", empty);
        }

        [Fact]
        public async Task Count_entities_groups_by_kind_layer_block_and_colour_and_the_filters_narrow_it()
        {
            await using McpHarness harness = await Sheet("count");

            JsonNode kinds = await harness.CallAsync("count_entities");
            JsonNode layers = await harness.CallAsync("count_entities", ("group_by", "layer"));
            JsonNode blocks = await harness.CallAsync("count_entities", ("group_by", "block"));
            JsonNode colours = await harness.CallAsync("count_entities", ("group_by", "color"));
            JsonNode narrowed = await harness.CallAsync(
                "count_entities",
                ("group_by", "kind"),
                ("layers", new[] { "plate" }),
                ("box", new[] { -5.0, -5.0, 205.0, 105.0 })
            );

            Assert.Equal(7, (int)kinds["entities"]);
            Assert.Equal(
                new[] { "CIRCLE:1", "INSERT:1", "LINE:1", "LWPOLYLINE:2", "MTEXT:1", "TEXT:1" },
                kinds["groups"].AsArray().Select(g => (string)g["key"] + ":" + (int)g["count"]).ToArray()
            );
            Assert.Equal(
                new[] { "Holes:1", "Notes:2", "Plate:3", "Sheet:1" },
                layers["groups"].AsArray().Select(g => (string)g["key"] + ":" + (int)g["count"]).ToArray()
            );
            Assert.Equal("TITLE:1", (string)blocks["groups"][0]["key"] + ":" + (int)blocks["groups"][0]["count"]);
            Assert.Equal(1, (int)blocks["total"]);
            // Plate is 3, Holes 5, Notes 2, Sheet 7; the line is set to 1.
            Assert.Equal(
                new[] { "1:1", "2:2", "3:2", "5:1", "7:1" },
                colours["groups"].AsArray().Select(g => (string)g["key"] + ":" + (int)g["count"]).ToArray()
            );
            Assert.Equal(
                new[] { "LINE:1", "LWPOLYLINE:1" },
                narrowed["groups"].AsArray().Select(g => (string)g["key"] + ":" + (int)g["count"]).ToArray()
            );
            string bad = await harness.FailAsync("count_entities", ("group_by", "size"));
            Assert.Contains("group_by is kind, layer, block, color", bad);
        }

        [Fact]
        public async Task Aggregate_sums_lengths_and_areas_by_group_and_counts_what_has_no_measure()
        {
            await using McpHarness harness = await Sheet("aggregate");

            JsonNode length = await harness.CallAsync("aggregate", ("measure", "length"), ("group_by", "layer"));
            JsonNode area = await harness.CallAsync("aggregate", ("measure", "area"));
            JsonNode byKind = await harness.CallAsync("aggregate", ("measure", "length"), ("group_by", "kind"));

            JsonNode plate = length["groups"].AsArray().Single(g => (string)g["key"] == "Plate");
            // The outline 600, the line 200 and the half circle of diameter 100.
            Assert.Equal(600.0 + 200.0 + System.Math.PI * 50.0, (double)plate["sum"], 3);
            Assert.Equal(3, (int)plate["count"]);
            Assert.Equal(System.Math.PI * 50.0, (double)plate["min"], 3);
            Assert.Equal(600.0, (double)plate["max"], 3);
            Assert.Equal((double)plate["sum"] / 3.0, (double)plate["average"], 2);
            JsonNode notes = length["groups"].AsArray().Single(g => (string)g["key"] == "Notes");
            Assert.Equal(0, (int)notes["count"]);
            Assert.Equal(2, (int)notes["skipped"]);
            // The outline 20000 and the hole of radius 20; the open half circle and the line have none.
            Assert.Equal("all", (string)area["groups"][0]["key"]);
            Assert.Equal(20000.0 + System.Math.PI * 400.0, (double)area["groups"][0]["sum"], 3);
            Assert.Equal(2, (int)area["groups"][0]["count"]);
            JsonNode circles = byKind["groups"].AsArray().Single(g => (string)g["key"] == "CIRCLE");
            Assert.Equal(System.Math.PI * 40.0, (double)circles["sum"], 3);
            string bad = await harness.FailAsync("aggregate", ("measure", "volume"));
            Assert.Contains("length or area", bad);
        }

        [Fact]
        public async Task Get_texts_gives_words_with_place_height_and_style_and_the_values_of_attributes()
        {
            await using McpHarness harness = await Sheet("texts");

            JsonNode all = await harness.CallAsync("get_texts");
            JsonNode found = await harness.CallAsync("get_texts", ("contains", "plate"));
            JsonNode styled = await harness.CallAsync(
                "get_texts",
                ("layers", new[] { "Notes" }),
                ("styles", new[] { "standard" })
            );

            JsonNode[] rows = all["texts"].AsArray().ToArray();
            Assert.Equal(4, (int)all["total"]);
            Assert.Equal(new[] { "TEXT", "MTEXT", "ATTRIB", "ATTRIB" }, rows.Select(r => (string)r["kind"]).ToArray());
            Assert.Equal("PLATE", (string)rows[0]["text"]);
            Assert.Equal(5.0, (double)rows[0]["height"], 6);
            Assert.NotNull(rows[0]["box"]);
            Assert.Equal("TITLE", (string)rows[2]["tag"]);
            Assert.Equal("PLATE 10 THK", (string)rows[2]["text"]);
            Assert.Equal(await First(harness, "INSERT"), (string)rows[2]["handle"]);
            Assert.Null(rows[2]["box"]);
            Assert.Equal(2, (int)found["total"]);
            Assert.Equal(2, (int)styled["total"]);
        }

        [Fact]
        public async Task Get_dimensions_gives_the_measured_value_text_override_and_points_of_each()
        {
            await using McpHarness harness = await Sheet("dimensions");
            await harness.CallAsync(
                "add_linear_dim",
                ("first", new[] { 0.0, 0.0 }),
                ("second", new[] { 200.0, 0.0 }),
                ("location", new[] { 100.0, -20.0 })
            );
            await harness.CallAsync(
                "add_radial_dim",
                ("center", new[] { 100.0, 50.0 }),
                ("radius", 20.0),
                ("location", new[] { 130.0, 70.0 }),
                ("text", "R20 HOLE")
            );

            JsonNode all = await harness.CallAsync("get_dimensions");
            JsonNode radial = await harness.CallAsync("get_dimensions", ("types", new[] { "radial" }));

            Assert.Equal(2, (int)all["total"]);
            JsonNode linear = all["dimensions"][0];
            Assert.Equal("RotatedDimension", (string)linear["type"]);
            Assert.Equal(200.0, (double)linear["measurement"], 6);
            Assert.Equal("200", (string)linear["text"]);
            Assert.Equal(new[] { 0.0, 0.0 }, Numbers(linear["points"]["xLine1Point"]));
            Assert.NotNull(linear["box"]);
            Assert.Equal(1, (int)radial["total"]);
            Assert.Equal(20.0, (double)radial["dimensions"][0]["measurement"], 6);
            Assert.Equal("R20 HOLE", (string)radial["dimensions"][0]["textOverride"]);
        }

        [Fact]
        public async Task Get_block_gives_the_definition_and_get_attributes_the_values_of_a_reference()
        {
            await using McpHarness harness = await Sheet("block");

            JsonNode block = await harness.CallAsync("get_block", ("name", "title"));
            JsonNode attributes = await harness.CallAsync("get_attributes", ("handle", await First(harness, "INSERT")));

            Assert.Equal("TITLE", (string)block["name"]);
            Assert.Equal(new[] { 0.0, 0.0 }, Numbers(block["basePoint"]));
            Assert.Equal(1, (int)block["total"]);
            Assert.Equal("LINE", (string)block["entities"][0]["kind"]);
            JsonNode[] definitions = block["attributeDefinitions"].AsArray().ToArray();
            Assert.Equal(new[] { "TITLE", "NUMBER" }, definitions.Select(d => (string)d["tag"]).ToArray());
            Assert.Equal("Drawing title", (string)definitions[0]["prompt"]);
            Assert.Equal("?", (string)definitions[0]["value"]);
            Assert.Equal("TITLE", (string)attributes["block"]);
            JsonNode[] rows = attributes["attributes"].AsArray().ToArray();
            Assert.Equal(new[] { "PLATE 10 THK", "A-101" }, rows.Select(r => (string)r["value"]).ToArray());
            Assert.Equal("Drawing number", (string)rows[1]["prompt"]);
            Assert.Empty(attributes["missing"].AsArray());
            Assert.Contains("no block NOPE", await harness.FailAsync("get_block", ("name", "NOPE")));
            Assert.Contains(
                "not an INSERT",
                await harness.FailAsync("get_attributes", ("handle", await First(harness, "TEXT")))
            );
        }
    }
}
