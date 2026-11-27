using System.Collections.Generic;
using System.Text.Json.Nodes;
using System.Threading.Tasks;
using Xunit;

namespace OpenCAD2D.Mcp.UnitTest
{
    /// <summary>
    /// get_entity of a multileader or a table (N3) says the name of its style, in a file and in an app: the copy of the object the
    /// app gives is read with its style resolved.
    /// </summary>
    public class McpAnnotationStyleNameTests
    {
        public static IEnumerable<object[]> Modes => new[] { new object[] { "file" }, new object[] { "app" } };

        [Theory]
        [MemberData(nameof(Modes))]
        public async Task A_multileader_and_a_table_give_the_name_of_their_style(string mode)
        {
            await using Bench bench = await Bench.Start(mode);
            await bench.Call("add_multileader_style", ("name", "NOTCH-NOTE"), ("text_height", 3.5));
            await bench.Call("add_table_style", ("name", "PLAN-TABLE"), ("data_height", 40.0));
            string note = await bench.Add(
                "add_mleader",
                ("arrow", new[] { 0.0, 0.0 }),
                ("landing", new[] { 100.0, 60.0 }),
                ("text", "NOTCH"),
                ("style", "NOTCH-NOTE")
            );
            string plain = await bench.Add(
                "add_mleader",
                ("arrow", new[] { 0.0, 200.0 }),
                ("landing", new[] { 100.0, 260.0 }),
                ("text", "PLAIN")
            );
            string table = await bench.Add(
                "add_table",
                ("at", new[] { 500.0, 0.0 }),
                ("columns", 2),
                ("rows", 2),
                ("style", "PLAN-TABLE")
            );

            Assert.Equal("NOTCH-NOTE", (string)(await bench.Call("get_entity", ("handle", note)))["details"]["style"]);
            Assert.Equal("Standard", (string)(await bench.Call("get_entity", ("handle", plain)))["details"]["style"]);
            Assert.Equal("PLAN-TABLE", (string)(await bench.Call("get_entity", ("handle", table)))["details"]["style"]);
        }
    }
}
