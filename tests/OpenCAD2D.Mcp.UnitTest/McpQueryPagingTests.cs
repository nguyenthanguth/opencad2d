using System.Collections.Generic;
using System.Linq;
using System.Text.Json.Nodes;
using System.Threading.Tasks;
using Xunit;

namespace OpenCAD2D.Mcp.UnitTest
{
    /// <summary>The query tools page a long answer: a page is 100 rows unless page_size says up to 500, and every row is on one page.</summary>
    public class McpQueryPagingTests
    {
        private static async Task<McpHarness> Many(string name) =>
            await McpHarness.StartAsync(new McpOptions { File = QueryDrawings.Many(TestFolder.Make(name)) });

        [Fact]
        public async Task Select_all_comes_in_pages_of_500_that_hold_every_handle_once_in_the_drawing_order()
        {
            await using McpHarness harness = await Many("paging-select");

            JsonNode first = await harness.CallAsync("select", ("mode", "all"), ("page_size", 500));
            JsonNode second = await harness.CallAsync("select", ("mode", "all"), ("page_size", 500), ("page", 2));
            JsonNode third = await harness.CallAsync("select", ("mode", "all"), ("page_size", 500), ("page", 3));
            JsonNode small = await harness.CallAsync("select", ("mode", "all"));

            Assert.Equal(901, (int)first["total"]);
            Assert.Equal(2, (int)first["pages"]);
            Assert.Equal(500, first["handles"].AsArray().Count);
            Assert.Equal(401, second["handles"].AsArray().Count);
            Assert.Empty(third["handles"].AsArray());
            List<string> joined = first["handles"]
                .AsArray()
                .Concat(second["handles"].AsArray())
                .Select(h => (string)h)
                .ToList();
            Assert.Equal(901, joined.Distinct().Count());
            Assert.Equal(100, small["handles"].AsArray().Count);
            Assert.Equal(10, (int)small["pages"]);
            Assert.Equal(joined.Take(100), small["handles"].AsArray().Select(h => (string)h));
            Assert.Contains(
                "page_size is from 1 to 500",
                await harness.FailAsync("select", ("mode", "all"), ("page_size", 501))
            );
            Assert.Contains("numbered from 1", await harness.FailAsync("select", ("mode", "all"), ("page", 0)));
        }

        [Fact]
        public async Task Get_texts_and_count_entities_page_with_the_totals_of_the_whole()
        {
            await using McpHarness harness = await Many("paging-texts");

            JsonNode one = await harness.CallAsync("get_texts", ("page_size", 120));
            JsonNode three = await harness.CallAsync("get_texts", ("page_size", 120), ("page", 3));
            JsonNode groups = await harness.CallAsync("count_entities", ("page_size", 1), ("page", 2));
            JsonNode byLayer = await harness.CallAsync("count_entities", ("group_by", "layer"));

            Assert.Equal(300, (int)one["total"]);
            Assert.Equal(3, (int)one["pages"]);
            Assert.Equal(120, one["texts"].AsArray().Count);
            Assert.Equal("W0", (string)one["texts"][0]["text"]);
            Assert.Equal(60, three["texts"].AsArray().Count);
            Assert.Equal("W299", (string)three["texts"][59]["text"]);
            Assert.Equal(3, (int)groups["total"]);
            Assert.Equal(3, (int)groups["pages"]);
            Assert.Equal(901, (int)groups["entities"]);
            Assert.Equal("LINE", (string)groups["groups"][0]["key"]);
            Assert.Equal(
                new[] { "Rows:601", "Words:300" },
                byLayer["groups"].AsArray().Select(g => (string)g["key"] + ":" + (int)g["count"]).ToArray()
            );
        }

        [Fact]
        public async Task Aggregate_pages_its_groups_and_adds_up_six_hundred_lines()
        {
            await using McpHarness harness = await Many("paging-aggregate");

            JsonNode total = await harness.CallAsync("aggregate", ("measure", "length"));
            JsonNode byKind = await harness.CallAsync(
                "aggregate",
                ("measure", "length"),
                ("group_by", "kind"),
                ("page_size", 1),
                ("page", 3)
            );

            Assert.Equal(600 * 5.0 + 2.0 * System.Math.PI * 5.0, (double)total["groups"][0]["sum"], 2);
            Assert.Equal(601, (int)total["groups"][0]["count"]);
            Assert.Equal(300, (int)total["groups"][0]["skipped"]);
            Assert.Equal(3, (int)byKind["pages"]);
            Assert.Equal("TEXT", (string)byKind["groups"][0]["key"]);
            Assert.Equal(300, (int)byKind["groups"][0]["skipped"]);
        }
    }
}
