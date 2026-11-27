using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json.Nodes;
using System.Threading.Tasks;
using GeometryHelper.Dxf;
using Xunit;

namespace OpenCAD2D.Mcp.UnitTest
{
    /// <summary>
    /// The tools that add and change multileader styles and table styles (N2), in a drawing file and in an app: what they set is what
    /// the list tools give back, add_mleader and add_table draw in the style, and a refusal leaves no style behind.
    /// </summary>
    public class McpAnnotationStyleTests
    {
        public static IEnumerable<object[]> Modes => new[] { new object[] { "file" }, new object[] { "app" } };

        private static JsonNode Row(JsonNode list, string name) =>
            list.AsArray().SingleOrDefault(r => (string)r["name"] == name);

        /// <summary>The height of the words of a note as drawn: the style's text height times the scale of the note.</summary>
        private static double WordsHeight(Bench bench, string handle) =>
            (
                (DxfMLeader)
                    bench
                        .Harness.Session.Backend.Entities("Model")
                        .Single(e => string.Equals(e.Handle, handle, StringComparison.OrdinalIgnoreCase))
                        .Entity
            )
                .MText
                .TextHeight;

        private static async Task<double> Height(Bench bench, string handle)
        {
            JsonNode box = (await bench.Call("bounds", ("handles", new[] { handle })))["rows"][0]["box"];
            return (double)box[3] - (double)box[1];
        }

        private static Task<string> Note(Bench bench, double y, string style = null) =>
            style == null
                ? bench.Add(
                    "add_mleader",
                    ("arrow", new[] { 0.0, y }),
                    ("landing", new[] { 100.0, y + 60.0 }),
                    ("text", "NOTCH")
                )
                : bench.Add(
                    "add_mleader",
                    ("arrow", new[] { 0.0, y }),
                    ("landing", new[] { 100.0, y + 60.0 }),
                    ("text", "NOTCH"),
                    ("style", style)
                );

        [Theory]
        [MemberData(nameof(Modes))]
        public async Task A_multileader_style_is_added_with_its_settings_and_a_note_is_drawn_in_it(string mode)
        {
            await using Bench bench = await Bench.Start(mode);
            await bench.Call("add_text_style", ("name", "NOTE"), ("font", "romans.shx"));
            await Note(bench, 0.0);

            JsonNode made = await bench.Call(
                "add_multileader_style",
                ("name", "PLAN-NOTE"),
                ("text_style", "NOTE"),
                ("text_height", 2.5),
                ("arrow", "Dot"),
                ("arrow_size", 3.0),
                ("landing_gap", 1.5),
                ("landing_length", 6.0),
                ("scale", 10.0)
            );

            Assert.Equal("PLAN-NOTE", (string)made["name"]);
            JsonNode row = Row(await bench.Call("list_multileader_styles"), "PLAN-NOTE");
            Assert.NotNull(row);
            Assert.Equal("NOTE", (string)row["textStyle"]);
            Assert.Equal(2.5, (double)row["textHeight"], 6);
            Assert.Equal("Dot", (string)row["arrowhead"]);
            Assert.Equal(3.0, (double)row["arrowSize"], 6);
            Assert.Equal(1.5, (double)row["landingGap"], 6);
            Assert.Equal(6.0, (double)row["landingDistance"], 6);
            Assert.Equal(10.0, (double)row["scale"], 6);
            Assert.False((bool)row["current"]);

            string styled = await Note(bench, 200.0, "PLAN-NOTE");
            string plain = await Note(bench, 400.0);

            Assert.Equal(25.0, WordsHeight(bench, styled), 6);
            Assert.Equal(2.5, WordsHeight(bench, plain), 6);
            JsonNode after = await bench.Call("list_multileader_styles");
            Assert.False((bool)Row(after, "PLAN-NOTE")["current"]);
            Assert.Equal("Standard", (string)after.AsArray().Single(r => (bool)r["current"])["name"]);
        }

        [Theory]
        [MemberData(nameof(Modes))]
        public async Task A_note_can_be_drawn_in_a_style_of_a_drawing_that_has_no_other_multileader_style(string mode)
        {
            await using Bench bench = await Bench.Start(mode);
            await bench.Call("add_multileader_style", ("name", "PLAN-NOTE"), ("scale", 10.0));

            string note = await Note(bench, 0.0, "PLAN-NOTE");

            Assert.Equal(25.0, WordsHeight(bench, note), 6);
        }

        [Theory]
        [MemberData(nameof(Modes))]
        public async Task A_styled_note_in_a_new_drawing_leaves_the_current_style_and_the_scale_as_they_were(
            string mode
        )
        {
            await using Bench bench = await Bench.Start(mode);
            await bench.Call("add_multileader_style", ("name", "PLAN-NOTE"), ("scale", 10.0));

            string styled = await Note(bench, 0.0, "PLAN-NOTE");
            string plain = await Note(bench, 200.0);

            Assert.Equal(25.0, WordsHeight(bench, styled), 6);
            Assert.Equal(2.5, WordsHeight(bench, plain), 6);
            JsonNode after = await bench.Call("list_multileader_styles");
            Assert.Equal("Standard", (string)after.AsArray().Single(r => (bool)r["current"])["name"]);
            Assert.Equal(1.0, Convert.ToDouble(bench.Harness.Session.Backend.Variable("MLEADERSCALE")), 6);
        }

        [Theory]
        [MemberData(nameof(Modes))]
        public async Task A_styled_note_with_a_scale_of_its_own_leaves_the_scale_as_it_was(string mode)
        {
            await using Bench bench = await Bench.Start(mode);
            await bench.Call("add_multileader_style", ("name", "PLAN-NOTE"), ("scale", 10.0));
            await Note(bench, 0.0);
            double before = Convert.ToDouble(bench.Harness.Session.Backend.Variable("MLEADERSCALE"));

            string styled = await bench.Add(
                "add_mleader",
                ("arrow", new[] { 0.0, 200.0 }),
                ("landing", new[] { 100.0, 260.0 }),
                ("text", "NOTCH"),
                ("style", "PLAN-NOTE"),
                ("scale", 4.0)
            );

            Assert.Equal(10.0, WordsHeight(bench, styled), 6);
            Assert.Equal(before, Convert.ToDouble(bench.Harness.Session.Backend.Variable("MLEADERSCALE")), 6);
            Assert.Equal(
                "Standard",
                (string)(await bench.Call("list_multileader_styles")).AsArray().Single(r => (bool)r["current"])["name"]
            );
        }

        [Theory]
        [MemberData(nameof(Modes))]
        public async Task A_styled_table_in_a_new_drawing_leaves_the_current_style_as_it_was(string mode)
        {
            await using Bench bench = await Bench.Start(mode);
            await bench.Call("add_table_style", ("name", "PLAN-TABLE"), ("data_height", 40.0));
            string styled = await bench.Add(
                "add_table",
                ("at", new[] { 0.0, 0.0 }),
                ("columns", 2),
                ("rows", 2),
                ("style", "PLAN-TABLE")
            );

            string plain = await bench.Add("add_table", ("at", new[] { 0.0, -600.0 }), ("columns", 2), ("rows", 2));

            Assert.True(await Height(bench, plain) < await Height(bench, styled) - 50.0);
            Assert.False((bool)Row(await bench.Call("list_table_styles"), "PLAN-TABLE")["current"]);
        }

        [Theory]
        [MemberData(nameof(Modes))]
        public async Task A_style_to_start_from_that_the_drawing_lacks_is_refused(string mode)
        {
            await using Bench bench = await Bench.Start(mode);

            Assert.Contains(
                "no multileader style",
                await bench.Fail("add_multileader_style", ("name", "NEW"), ("from", "Nowhere"))
            );
            Assert.Contains(
                "no table style",
                await bench.Fail("add_table_style", ("name", "NEW"), ("from", "Nowhere"))
            );
            Assert.Null(Row(await bench.Call("list_multileader_styles"), "NEW"));
            Assert.Null(Row(await bench.Call("list_table_styles"), "NEW"));
            await bench.Call("add_multileader_style", ("name", "FROM-STANDARD"), ("from", "Standard"));
            await bench.Call("add_table_style", ("name", "FROM-STANDARD"), ("from", "standard"));
        }

        [Theory]
        [MemberData(nameof(Modes))]
        public async Task An_annotative_style_is_made_fixed_again_with_annotative_false(string mode)
        {
            await using Bench bench = await Bench.Start(mode);
            await bench.Call("add_multileader_style", ("name", "ANNO"), ("annotative", true));

            string measure = "measure_leader";
            (string, object)[] asked =
            {
                ("arrow", new[] { 0.0, 0.0 }),
                ("landing", new[] { 100.0, 60.0 }),
                ("text", "NOTCH"),
                ("style", "ANNO"),
            };
            Assert.Contains("annotative", await bench.Fail(measure, asked));

            await bench.Call("set_multileader_style", ("name", "ANNO"), ("annotative", false));

            await bench.Call(measure, asked);
            Assert.Equal(1.0, (double)Row(await bench.Call("list_multileader_styles"), "ANNO")["scale"], 6);
            await bench.Call("set_multileader_style", ("name", "ANNO"), ("annotative", false), ("scale", 3.0));
            Assert.Equal(3.0, (double)Row(await bench.Call("list_multileader_styles"), "ANNO")["scale"], 6);
        }

        [Theory]
        [MemberData(nameof(Modes))]
        public async Task A_multileader_style_is_changed_and_made_current_and_the_notes_in_it_follow(string mode)
        {
            await using Bench bench = await Bench.Start(mode);
            await bench.Call("add_multileader_style", ("name", "PLAN-NOTE"), ("scale", 10.0));
            string note = await Note(bench, 0.0, "PLAN-NOTE");
            Assert.Equal(25.0, WordsHeight(bench, note), 6);

            await bench.Call("set_multileader_style", ("name", "plan-note"), ("text_height", 3.5), ("scale", 5.0));

            JsonNode row = Row(await bench.Call("list_multileader_styles"), "PLAN-NOTE");
            Assert.Equal(3.5, (double)row["textHeight"], 6);
            Assert.Equal(5.0, (double)row["scale"], 6);
            Assert.Equal(17.5, WordsHeight(bench, note), 6);

            JsonNode current = await bench.Call("set_current_multileader_style", ("name", "PLAN-NOTE"));
            Assert.Equal("PLAN-NOTE", (string)current["name"]);
            Assert.True((bool)Row(await bench.Call("list_multileader_styles"), "PLAN-NOTE")["current"]);
            Assert.Equal(17.5, WordsHeight(bench, await Note(bench, 200.0)), 6);
        }

        [Theory]
        [MemberData(nameof(Modes))]
        public async Task A_note_is_measured_in_a_multileader_style_of_a_drawing_that_has_no_other(string mode)
        {
            await using Bench bench = await Bench.Start(mode);
            await bench.Call("add_multileader_style", ("name", "PLAN-NOTE"), ("scale", 10.0));

            JsonNode measured = await bench.Call(
                "measure_leader",
                ("arrow", new[] { 0.0, 0.0 }),
                ("landing", new[] { 100.0, 60.0 }),
                ("text", "NOTCH"),
                ("style", "PLAN-NOTE")
            );

            double height = (double)measured["frames"][0]["height"];
            Assert.InRange(height, 25.0, 40.0);
        }

        [Theory]
        [MemberData(nameof(Modes))]
        public async Task One_undo_takes_a_multileader_style_back(string mode)
        {
            await using Bench bench = await Bench.Start(mode);

            await bench.Call("add_multileader_style", ("name", "ANNO"), ("annotative", true), ("text_height", 3.5));

            Assert.Equal(3.5, (double)Row(await bench.Call("list_multileader_styles"), "ANNO")["textHeight"], 6);
            await bench.Call("undo");
            Assert.Null(Row(await bench.Call("list_multileader_styles"), "ANNO"));
        }

        [Theory]
        [MemberData(nameof(Modes))]
        public async Task A_multileader_style_that_is_refused_leaves_nothing_behind(string mode)
        {
            await using Bench bench = await Bench.Start(mode);
            await bench.Call("add_multileader_style", ("name", "PLAN-NOTE"));

            Assert.Contains("already exists", await bench.Fail("add_multileader_style", ("name", "plan-note")));
            Assert.Contains(
                "no text style",
                await bench.Fail("add_multileader_style", ("name", "NEW"), ("text_style", "Nowhere"))
            );
            Assert.Contains(
                "above nought",
                await bench.Fail("add_multileader_style", ("name", "NEW"), ("text_height", 0.0))
            );
            Assert.Contains(
                "arrowheads are",
                await bench.Fail("add_multileader_style", ("name", "NEW"), ("arrow", "Banana"))
            );
            Assert.Contains(
                "annotative",
                await bench.Fail("add_multileader_style", ("name", "NEW"), ("scale", 2.0), ("annotative", true))
            );
            Assert.Contains("no multileader style", await bench.Fail("set_multileader_style", ("name", "Nowhere")));
            Assert.Contains(
                "no multileader style",
                await bench.Fail("set_current_multileader_style", ("name", "Nowhere"))
            );
            Assert.Contains(
                "no text style",
                await bench.Fail("set_multileader_style", ("name", "PLAN-NOTE"), ("text_style", "Nowhere"))
            );

            Assert.Null(Row(await bench.Call("list_multileader_styles"), "NEW"));
        }

        [Theory]
        [MemberData(nameof(Modes))]
        public async Task A_table_style_is_added_with_its_settings_and_a_table_is_drawn_in_it(string mode)
        {
            await using Bench bench = await Bench.Start(mode);
            await bench.Call("add_text_style", ("name", "NOTE"), ("font", "romans.shx"));
            string small = await bench.Add(
                "add_table",
                ("at", new[] { 0.0, 0.0 }),
                ("columns", 2),
                ("rows", 2),
                ("column_width", 400.0)
            );

            JsonNode made = await bench.Call(
                "add_table_style",
                ("name", "PLAN-TABLE"),
                ("text_style", "NOTE"),
                ("title_height", 35.0),
                ("header_height", 25.0),
                ("data_height", 25.0),
                ("horizontal_margin", 15.0),
                ("vertical_margin", 15.0),
                ("description", "For a sheet at 1:10")
            );

            Assert.Equal("PLAN-TABLE", (string)made["name"]);
            JsonNode row = Row(await bench.Call("list_table_styles"), "PLAN-TABLE");
            Assert.NotNull(row);
            Assert.Equal(15.0, (double)row["horizontalMargin"], 6);
            Assert.Equal(15.0, (double)row["verticalMargin"], 6);
            Assert.Equal("For a sheet at 1:10", (string)row["description"]);
            Assert.Equal(("NOTE", 35.0), ((string)row["title"]["textStyle"], (double)row["title"]["textHeight"]));
            Assert.Equal(("NOTE", 25.0), ((string)row["header"]["textStyle"], (double)row["header"]["textHeight"]));
            Assert.Equal(("NOTE", 25.0), ((string)row["data"]["textStyle"], (double)row["data"]["textHeight"]));

            string big = await bench.Add(
                "add_table",
                ("at", new[] { 0.0, -500.0 }),
                ("columns", 2),
                ("rows", 2),
                ("column_width", 400.0),
                ("style", "PLAN-TABLE"),
                ("cells", new[] { new[] { "PARTS" }, new[] { "MARK", "QTY" }, new[] { "PL10", "2" } })
            );
            double smallHeight = await Height(bench, small);
            double bigHeight = await Height(bench, big);
            Assert.True(smallHeight < 60.0, "a table in Standard is " + smallHeight + " high");
            Assert.True(bigHeight > 200.0, "a table in PLAN-TABLE is " + bigHeight + " high");
            Assert.False((bool)Row(await bench.Call("list_table_styles"), "PLAN-TABLE")["current"]);
        }

        [Theory]
        [MemberData(nameof(Modes))]
        public async Task A_table_style_is_changed_and_made_current_and_the_tables_in_it_are_drawn_again(string mode)
        {
            await using Bench bench = await Bench.Start(mode);
            await bench.Call("add_table_style", ("name", "PLAN-TABLE"), ("data_height", 10.0));
            string table = await bench.Add(
                "add_table",
                ("at", new[] { 0.0, 0.0 }),
                ("columns", 2),
                ("rows", 2),
                ("style", "PLAN-TABLE")
            );
            double before = await Height(bench, table);

            await bench.Call(
                "set_table_style",
                ("name", "plan-table"),
                ("data_height", 40.0),
                ("vertical_margin", 8.0)
            );

            JsonNode row = Row(await bench.Call("list_table_styles"), "PLAN-TABLE");
            Assert.Equal(40.0, (double)row["data"]["textHeight"], 6);
            Assert.Equal(8.0, (double)row["verticalMargin"], 6);
            Assert.True(await Height(bench, table) > before + 50.0);

            JsonNode current = await bench.Call("set_current_table_style", ("name", "PLAN-TABLE"));
            Assert.Equal("PLAN-TABLE", (string)current["name"]);
            Assert.True((bool)Row(await bench.Call("list_table_styles"), "PLAN-TABLE")["current"]);
            string plain = await bench.Add("add_table", ("at", new[] { 0.0, -600.0 }), ("columns", 2), ("rows", 2));
            Assert.True(await Height(bench, plain) > before + 50.0);
        }

        [Theory]
        [MemberData(nameof(Modes))]
        public async Task A_table_style_that_is_refused_leaves_nothing_behind(string mode)
        {
            await using Bench bench = await Bench.Start(mode);
            await bench.Call("add_table_style", ("name", "PLAN-TABLE"));

            Assert.Contains("already exists", await bench.Fail("add_table_style", ("name", "plan-table")));
            Assert.Contains(
                "no text style",
                await bench.Fail("add_table_style", ("name", "NEW"), ("text_style", "Nowhere"))
            );
            Assert.Contains(
                "above nought",
                await bench.Fail("add_table_style", ("name", "NEW"), ("data_height", -1.0))
            );
            Assert.Contains(
                "nought or more",
                await bench.Fail("add_table_style", ("name", "NEW"), ("vertical_margin", -1.0))
            );
            Assert.Contains("no table style", await bench.Fail("set_table_style", ("name", "Nowhere")));
            Assert.Contains("no table style", await bench.Fail("set_current_table_style", ("name", "Nowhere")));

            Assert.Null(Row(await bench.Call("list_table_styles"), "NEW"));
        }

        [Theory]
        [MemberData(nameof(Modes))]
        public async Task The_style_tools_are_refused_by_a_read_only_server(string mode)
        {
            await using Bench bench = await Bench.Start(mode, readOnly: true);

            Assert.Contains("--read-only", await bench.Fail("add_multileader_style", ("name", "A")));
            Assert.Contains("--read-only", await bench.Fail("set_multileader_style", ("name", "Standard")));
            Assert.Contains("--read-only", await bench.Fail("set_current_multileader_style", ("name", "Standard")));
            Assert.Contains("--read-only", await bench.Fail("add_table_style", ("name", "A")));
            Assert.Contains("--read-only", await bench.Fail("set_table_style", ("name", "Standard")));
            Assert.Contains("--read-only", await bench.Fail("set_current_table_style", ("name", "Standard")));
        }
    }
}
