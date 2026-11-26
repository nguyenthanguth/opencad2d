using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using ModelContextProtocol.Client;
using Xunit;

namespace OpenCAD2D.Mcp.UnitTest
{
    /// <summary>The create, modify, layer and layout tools are all refused by a server started read-only, and say they write.</summary>
    public class McpDraftSafetyTests
    {
        private static readonly double[] P = { 0.0, 0.0 };
        private static readonly string[] H = { "1" };

        private static (string Tool, (string, object)[] Args)[] Writers() =>
            new (string, (string, object)[])[]
            {
                ("add_rectangle", new (string, object)[] { ("corner1", P), ("corner2", new[] { 5.0, 5.0 }) }),
                ("add_polygon", new (string, object)[] { ("center", P), ("radius", 5.0), ("sides", 5) }),
                (
                    "add_ellipse",
                    new (string, object)[] { ("center", P), ("major_axis_end", new[] { 5.0, 0.0 }), ("ratio", 0.5) }
                ),
                ("add_spline", new (string, object)[] { ("fit_points", new[] { P, new[] { 5.0, 5.0 } }) }),
                ("add_point", new (string, object)[] { ("at", P) }),
                (
                    "add_donut",
                    new (string, object)[] { ("center", P), ("inner_diameter", 1.0), ("outer_diameter", 2.0) }
                ),
                ("add_xline", new (string, object)[] { ("through", P), ("angle", 0.0) }),
                ("add_ray", new (string, object)[] { ("start", P), ("angle", 0.0) }),
                ("add_leader", new (string, object)[] { ("points", new[] { P, new[] { 5.0, 5.0 } }) }),
                (
                    "add_mleader",
                    new (string, object)[] { ("arrow", P), ("landing", new[] { 5.0, 5.0 }), ("text", "x") }
                ),
                ("add_table", new (string, object)[] { ("at", P), ("columns", 2), ("rows", 2) }),
                ("add_text_style", new (string, object)[] { ("name", "NOTE") }),
                ("add_dimension_style", new (string, object)[] { ("name", "PLAN") }),
                ("load_linetype", new (string, object)[] { ("name", "DASHED") }),
                ("add_attribute_definition", new (string, object)[] { ("tag", "T"), ("at", P), ("height", 3.0) }),
                ("insert_block_with_attributes", new (string, object)[] { ("name", "B"), ("at", P) }),
                ("set_attribute", new (string, object)[] { ("handle", "1"), ("tag", "T"), ("value", "x") }),
                ("define_block", new (string, object)[] { ("name", "B"), ("base_point", P), ("handles", H) }),
                ("copy", new (string, object)[] { ("handles", H), ("displacements", new[] { new[] { 1.0, 1.0 } }) }),
                (
                    "mirror",
                    new (string, object)[] { ("handles", H), ("line_start", P), ("line_end", new[] { 0.0, 5.0 }) }
                ),
                ("offset", new (string, object)[] { ("handle", "1"), ("side", P), ("distance", 1.0) }),
                (
                    "array_rectangular",
                    new (string, object)[]
                    {
                        ("handles", H),
                        ("rows", 2),
                        ("columns", 2),
                        ("row_distance", 1.0),
                        ("column_distance", 1.0),
                    }
                ),
                ("array_polar", new (string, object)[] { ("handles", H), ("center", P), ("count", 4) }),
                ("explode", new (string, object)[] { ("handles", H) }),
                ("join", new (string, object)[] { ("handles", H) }),
                ("break_at", new (string, object)[] { ("handle", "1"), ("point", P) }),
                ("trim", new (string, object)[] { ("pick_points", new[] { P }) }),
                ("extend", new (string, object)[] { ("pick_points", new[] { P }) }),
                ("fillet", new (string, object)[] { ("first", "1"), ("radius", 1.0) }),
                ("chamfer", new (string, object)[] { ("first", "1"), ("second", "2"), ("distance1", 1.0) }),
                ("stretch", new (string, object)[] { ("window", new[] { 0.0, 0.0, 5.0, 5.0 }), ("displacement", P) }),
                (
                    "lengthen",
                    new (string, object)[] { ("handle", "1"), ("mode", "delta"), ("value", 1.0), ("end_point", P) }
                ),
                ("set_draw_order", new (string, object)[] { ("handles", H), ("where", "back") }),
                ("edit_text", new (string, object)[] { ("handle", "1"), ("contents", "x") }),
                ("edit_dimension", new (string, object)[] { ("handle", "1"), ("text", "x") }),
                ("match_properties", new (string, object)[] { ("source", "1"), ("targets", new[] { "2" }) }),
                ("set_properties", new (string, object)[] { ("handles", H), ("lineweight", 25) }),
                ("redo", new (string, object)[0]),
                ("set_current_layer", new (string, object)[] { ("name", "0") }),
                ("set_layer", new (string, object)[] { ("name", "0"), ("on", true) }),
                ("rename_layer", new (string, object)[] { ("name", "A"), ("new_name", "B") }),
                ("delete_layer", new (string, object)[] { ("name", "A") }),
                ("create_layout", new (string, object)[] { ("name", "Sheet1") }),
                ("set_current_layout", new (string, object)[] { ("name", "Model") }),
                (
                    "add_viewport",
                    new (string, object)[]
                    {
                        ("layout", "Layout1"),
                        ("frame", new[] { 0.0, 0.0, 5.0, 5.0 }),
                        ("scale", 1.0),
                    }
                ),
                ("set_viewport_scale", new (string, object)[] { ("handle", "1"), ("scale", 2.0) }),
                ("lock_viewport", new (string, object)[] { ("handle", "1") }),
            };

        [Fact]
        public async Task Every_writer_of_the_second_phase_is_refused_when_the_server_is_read_only()
        {
            await using Bench bench = await Bench.Start("file", readOnly: true);

            foreach ((string tool, (string, object)[] args) in Writers())
            {
                string message = await bench.Fail(tool, args);
                Assert.Contains("--read-only", message);
            }

            Assert.Empty(await bench.Entities());
            Assert.False((bool)(await bench.Call("info"))["modified"]);
        }

        [Fact]
        public async Task Every_writer_of_the_second_phase_is_listed_as_one_that_writes()
        {
            await using Bench bench = await Bench.Start("file");

            IList<McpClientTool> tools = await bench.Harness.Client.ListToolsAsync();

            foreach ((string tool, _) in Writers())
            {
                McpClientTool listed = tools.SingleOrDefault(t => t.Name == tool);
                Assert.True(listed != null, tool + " is not served.");
                Assert.False(listed.ProtocolTool.Annotations?.ReadOnlyHint ?? false, tool + " says it only reads.");
            }
        }

        [Fact]
        public async Task The_commands_the_tools_type_never_carry_text_of_the_callers_other_than_the_words_of_a_note()
        {
            await using Bench bench = await Bench.Start("file");
            string line = await bench.Add("add_line", ("start", P), ("end", new[] { 10.0, 0.0 }));

            string message = await bench.Fail(
                "lengthen",
                ("handle", line),
                ("mode", "delta; ERASE ALL"),
                ("value", 1.0),
                ("end_point", P)
            );
            Assert.Contains("delta, total or percent", message);
            Assert.Contains(
                "name",
                await bench.Fail("define_block", ("name", "A B"), ("base_point", P), ("handles", new[] { line }))
                    + " name"
            );
            Assert.Contains("one word", await bench.Fail("insert_block_with_attributes", ("name", "A B"), ("at", P)));
            Assert.Single(await bench.Entities());
        }
    }
}
