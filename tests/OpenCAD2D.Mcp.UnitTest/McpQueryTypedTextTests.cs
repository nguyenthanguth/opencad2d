using System.Linq;
using System.Text.Json.Nodes;
using System.Threading.Tasks;
using Xunit;

namespace OpenCAD2D.Mcp.UnitTest
{
    /// <summary>
    /// What an agent gives the query tools is never typed at a command prompt: a handle or a name that starts with ( ! or ' is
    /// refused as an unknown handle or value, and the AutoLISP it holds does not run.
    /// </summary>
    public class McpQueryTypedTextTests
    {
        private const string Lisp = "(command \"_.ERASE\" \"_ALL\" \"\")";

        private static async Task<McpHarness> Sheet(string name) =>
            await McpHarness.StartAsync(new McpOptions { File = QueryDrawings.Sheet(TestFolder.Make(name)) });

        private static async Task<int> Count(McpHarness harness) =>
            (int)(await harness.CallAsync("info"))["entityCount"];

        [Fact]
        public async Task A_handle_that_is_AutoLISP_is_refused_by_every_tool_that_takes_one_and_does_not_run()
        {
            await using McpHarness harness = await Sheet("typed-handle");
            int before = await Count(harness);

            foreach (string text in new[] { Lisp, "!x", "'ZOOM", "2F\u0001", "\u0007" })
            {
                Assert.Contains("no object", await harness.FailAsync("list_properties", ("handles", new[] { text })));
                Assert.Contains("no object", await harness.FailAsync("get_entity", ("handle", text)));
                Assert.Contains("no object", await harness.FailAsync("get_attributes", ("handle", text)));
                Assert.Contains("no object", await harness.FailAsync("measure_length", ("handle", text)));
                Assert.Contains("no object", await harness.FailAsync("select_similar", ("handle", text)));
            }

            Assert.Equal(before, await Count(harness));
            Assert.False((bool)(await harness.CallAsync("info"))["modified"]);
        }

        [Fact]
        public async Task A_variable_value_that_is_AutoLISP_is_refused_and_does_not_run()
        {
            await using McpHarness harness = await Sheet("typed-variable");
            int before = await Count(harness);

            string layer = await harness.FailAsync("set_variable", ("name", "CLAYER"), ("value", Lisp));
            string style = await harness.FailAsync("set_variable", ("name", "TEXTSTYLE"), ("value", "!x"));
            string name = await harness.FailAsync("set_variable", ("name", Lisp), ("value", 1));
            JsonNode read = await harness.CallAsync("get_variables", ("names", new[] { Lisp }));

            Assert.Contains("CLAYER", layer);
            Assert.Contains("TEXTSTYLE", style);
            Assert.Contains("is not one set_variable sets", name);
            Assert.NotNull(read["variables"][0]["error"]);
            Assert.Equal(before, await Count(harness));
            Assert.Equal("0", (string)(await harness.CallAsync("info"))["currentLayer"]);
        }

        [Fact]
        public async Task A_block_or_layout_name_that_is_AutoLISP_is_refused_and_does_not_run()
        {
            await using McpHarness harness = await Sheet("typed-name");
            int before = await Count(harness);

            Assert.Contains("no block", await harness.FailAsync("get_block", ("name", Lisp)));
            Assert.Contains("no layout", await harness.FailAsync("select", ("mode", "all"), ("layout", Lisp)));
            Assert.Contains("no snapshot", await harness.FailAsync("reset", ("snapshot", Lisp)));
            Assert.Equal(before, await Count(harness));
        }

        [Fact]
        public async Task Attached_a_handle_that_is_AutoLISP_is_refused_and_does_not_run_in_the_app()
        {
            using TestApp app = TestApp.Start();
            await using McpHarness harness = await McpHarness.StartAsync(
                new McpOptions { Attach = true, RemoteFolder = app.Folder }
            );
            await harness.CallAsync("open_drawing", ("path", QueryDrawings.Sheet(TestFolder.Make("typed-attached"))));
            int before = await Count(harness);

            Assert.Contains("no object", await harness.FailAsync("list_properties", ("handles", new[] { Lisp })));
            Assert.Contains("no object", await harness.FailAsync("get_entity", ("handle", Lisp)));
            string variable = await harness.FailAsync("set_variable", ("name", "CLAYER"), ("value", Lisp));

            Assert.Contains("CLAYER", variable);
            Assert.Equal(before, await Count(harness));
            Assert.Equal("0", (string)(await harness.CallAsync("info"))["currentLayer"]);
        }
    }
}
