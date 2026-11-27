using System.Collections.Generic;
using System.Linq;
using System.Text.Json.Nodes;
using System.Threading.Tasks;
using Xunit;

namespace OpenCAD2D.Mcp.UnitTest
{
    /// <summary>
    /// list_multileader_styles and list_table_styles (N5) give the same rows from a drawing file and from an app, which answers with
    /// the settings of its styles and not a copy of its drawing; measure_leader finds the scale of a style the same way.
    /// </summary>
    public class McpAnnotationStyleListTests
    {
        public static IEnumerable<object[]> Modes => new[] { new object[] { "file" }, new object[] { "app" } };

        private static JsonNode Row(JsonNode list, string name) =>
            list.AsArray().SingleOrDefault(r => (string)r["name"] == name);

        [Theory]
        [MemberData(nameof(Modes))]
        public async Task The_multileader_styles_are_listed_with_their_settings_sorted_by_name(string mode)
        {
            await using Bench bench = await Bench.Start(mode);
            await bench.Call("add_text_style", ("name", "NOTE"), ("font", "romans.shx"));
            await bench.Call(
                "add_multileader_style",
                ("name", "Zeta"),
                ("text_style", "NOTE"),
                ("text_height", 3.5),
                ("arrow", "Dot"),
                ("arrow_size", 4.0),
                ("landing_gap", 1.5),
                ("landing_length", 6.0),
                ("scale", 10.0)
            );
            await bench.Call("add_multileader_style", ("name", "alpha"), ("annotative", true));
            await bench.Call("set_current_multileader_style", ("name", "Zeta"));

            JsonNode list = await bench.Call("list_multileader_styles");

            Assert.Equal(new[] { "alpha", "Zeta" }, list.AsArray().Select(r => (string)r["name"]).ToArray());
            JsonNode zeta = Row(list, "Zeta");
            Assert.True((bool)zeta["current"]);
            Assert.Equal("NOTE", (string)zeta["textStyle"]);
            Assert.Equal(3.5, (double)zeta["textHeight"], 6);
            Assert.Equal(4.0, (double)zeta["arrowSize"], 6);
            Assert.Equal(1.5, (double)zeta["landingGap"], 6);
            Assert.Equal(6.0, (double)zeta["landingDistance"], 6);
            Assert.Equal(10.0, (double)zeta["scale"], 6);
            Assert.False((bool)zeta["annotative"]);
            Assert.Equal("straight", (string)zeta["leaderType"]);
            Assert.Equal("text", (string)zeta["content"]);
            Assert.True((bool)Row(list, "alpha")["annotative"]);
        }

        [Theory]
        [MemberData(nameof(Modes))]
        public async Task The_table_styles_are_listed_with_their_settings_sorted_by_name(string mode)
        {
            await using Bench bench = await Bench.Start(mode);
            await bench.Call(
                "add_table_style",
                ("name", "Zeta"),
                ("title_height", 8.0),
                ("header_height", 6.0),
                ("data_height", 5.0),
                ("horizontal_margin", 2.0),
                ("vertical_margin", 3.0)
            );
            await bench.Call("add_table_style", ("name", "alpha"));
            await bench.Call("set_current_table_style", ("name", "Zeta"));

            JsonNode list = await bench.Call("list_table_styles");

            Assert.Equal(new[] { "alpha", "Zeta" }, list.AsArray().Select(r => (string)r["name"]).ToArray());
            JsonNode zeta = Row(list, "Zeta");
            Assert.True((bool)zeta["current"]);
            Assert.Equal(2.0, (double)zeta["horizontalMargin"], 6);
            Assert.Equal(3.0, (double)zeta["verticalMargin"], 6);
            Assert.Equal(8.0, (double)zeta["title"]["textHeight"], 6);
            Assert.Equal(6.0, (double)zeta["header"]["textHeight"], 6);
            Assert.Equal(5.0, (double)zeta["data"]["textHeight"], 6);
            Assert.True((bool)zeta["titleShown"]);
            Assert.True((bool)zeta["headerShown"]);
        }

        [Theory]
        [MemberData(nameof(Modes))]
        public async Task A_leader_is_measured_at_the_scale_of_its_style_and_an_annotative_style_is_refused(string mode)
        {
            await using Bench bench = await Bench.Start(mode);
            await bench.Call("add_multileader_style", ("name", "Ten"), ("scale", 10.0));
            await bench.Call("add_multileader_style", ("name", "Anno"), ("annotative", true));

            JsonNode withStyle = await bench.Call(
                "measure_leader",
                ("arrow", new[] { 0.0, 0.0 }),
                ("landing", new[] { 100.0, 60.0 }),
                ("text", "NOTCH"),
                ("style", "ten")
            );
            JsonNode explicitScale = await bench.Call(
                "measure_leader",
                ("arrow", new[] { 0.0, 0.0 }),
                ("landing", new[] { 100.0, 60.0 }),
                ("text", "NOTCH"),
                ("style", "Ten"),
                ("scale", 10.0)
            );

            Assert.Equal(explicitScale.ToJsonString(), withStyle.ToJsonString());
            Assert.Contains(
                "annotative",
                await bench.Fail(
                    "measure_leader",
                    ("arrow", new[] { 0.0, 0.0 }),
                    ("landing", new[] { 100.0, 60.0 }),
                    ("text", "NOTCH"),
                    ("style", "Anno")
                ),
                System.StringComparison.OrdinalIgnoreCase
            );
        }
    }
}
