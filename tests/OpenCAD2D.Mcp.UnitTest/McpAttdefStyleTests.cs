using System.Collections.Generic;
using System.Linq;
using System.Text.Json.Nodes;
using System.Threading.Tasks;
using Xunit;

namespace OpenCAD2D.Mcp.UnitTest
{
    /// <summary>
    /// A block whose attribute definitions use a text style of the drawing's own, not Standard, inserted with its attributes (N1): the
    /// INSERT is made in the drawing itself, in a file and in an app, so its attributes take the style of their definitions.
    /// </summary>
    public class McpAttdefStyleTests
    {
        public static IEnumerable<object[]> Modes => new[] { new object[] { "file" }, new object[] { "app" } };

        [Theory]
        [MemberData(nameof(Modes))]
        public async Task A_block_whose_attributes_use_a_text_style_of_the_drawing_is_inserted_with_its_words(
            string mode
        )
        {
            await using Bench bench = await Bench.Start(mode);
            await bench.Call("add_text_style", ("name", "SYN"), ("font", "txt"));
            string frame = await bench.Add(
                "add_rectangle",
                ("corner1", new[] { 2000.0, 0.0 }),
                ("corner2", new[] { 2160.0, 35.0 })
            );
            string drawing = await bench.Add(
                "add_attribute_definition",
                ("tag", "DRAWING"),
                ("at", new[] { 2004.0, 21.0 }),
                ("height", 5.0),
                ("text_style", "SYN")
            );
            string number = await bench.Add(
                "add_attribute_definition",
                ("tag", "NUMBER"),
                ("at", new[] { 2004.0, 5.0 }),
                ("height", 5.0),
                ("text_style", "SYN")
            );
            await bench.Call(
                "define_block",
                ("name", "TITLE"),
                ("base_point", new[] { 2000.0, 0.0 }),
                ("handles", new[] { frame, drawing, number }),
                ("source", "delete")
            );

            string insert = await bench.Add(
                "insert_block_with_attributes",
                ("name", "TITLE"),
                ("at", new[] { 250.0, 10.0 }),
                ("values", new Dictionary<string, string> { ["DRAWING"] = "PLATE PL12", ["NUMBER"] = "A-101" })
            );

            JsonNode attributes = await bench.Call("get_attributes", ("handle", insert));
            Assert.Equal(
                new[] { "DRAWING=PLATE PL12", "NUMBER=A-101" },
                attributes["attributes"].AsArray().Select(a => (string)a["tag"] + "=" + (string)a["value"]).ToArray()
            );
            JsonNode block = await bench.Call("get_block", ("name", "TITLE"));
            Assert.Equal(2, block["attributeDefinitions"].AsArray().Count);
        }
    }
}
