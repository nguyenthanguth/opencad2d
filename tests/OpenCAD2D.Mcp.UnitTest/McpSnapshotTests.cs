using System.Linq;
using System.Text.Json.Nodes;
using System.Threading.Tasks;
using Xunit;

namespace OpenCAD2D.Mcp.UnitTest
{
    /// <summary>An agent tries a layout and rolls it back: snapshot, reset and release_snapshot over a drawing file.</summary>
    public class McpSnapshotTests
    {
        private static async Task<McpHarness> Sheet(string name, bool readOnly = false) =>
            await McpHarness.StartAsync(
                new McpOptions { File = QueryDrawings.Sheet(TestFolder.Make(name)), ReadOnly = readOnly }
            );

        private static async Task<string[]> Texts(McpHarness harness) =>
            (await harness.CallAsync("query_entities", ("kinds", new[] { "TEXT" })))["entities"]
                .AsArray()
                .Select(e => (string)e["geometry"]["text"])
                .ToArray();

        [Fact]
        public async Task A_session_takes_a_snapshot_adds_two_overlapping_texts_checks_them_and_resets_them_away()
        {
            await using McpHarness harness = await Sheet("snapshot-script");
            int before = (int)(await harness.CallAsync("info"))["entityCount"];

            JsonNode taken = await harness.CallAsync("snapshot");
            JsonNode first = await harness.CallAsync(
                "add_text",
                ("text", "OVER ONE"),
                ("at", new[] { 80.0, 48.0 }),
                ("height", 5.0)
            );
            await harness.CallAsync("add_text", ("text", "OVER TWO"), ("at", new[] { 82.0, 49.0 }), ("height", 5.0));
            JsonNode crowded = await harness.CallAsync("overlaps");
            JsonNode reset = await harness.CallAsync("reset", ("snapshot", (string)taken["snapshot"]));
            JsonNode after = await harness.CallAsync("query_entities", ("kinds", new[] { "TEXT" }));
            JsonNode clear = await harness.CallAsync("overlaps");

            Assert.Equal("s1", (string)taken["snapshot"]);
            Assert.Equal("sheet.dxf", (string)taken["drawing"]);
            Assert.Equal(before, (int)taken["entities"]);
            Assert.True((int)crowded["total"] >= 1);
            Assert.Equal("reset", (string)reset["done"]);
            Assert.Equal(before, (int)reset["entities"]);
            Assert.Equal(
                new[] { "PLATE" },
                after["entities"].AsArray().Select(e => (string)e["geometry"]["text"]).ToArray()
            );
            Assert.Equal(before, (int)(await harness.CallAsync("info"))["entityCount"]);
            Assert.Equal(0, (int)clear["total"]);

            // The handle seed comes back too: the same text gets the same handle the second time.
            JsonNode again = await harness.CallAsync(
                "add_text",
                ("text", "OVER ONE"),
                ("at", new[] { 80.0, 48.0 }),
                ("height", 5.0)
            );
            Assert.Equal((string)first["handle"], (string)again["handle"]);
        }

        [Fact]
        public async Task A_snapshot_can_be_reset_to_again_and_again_and_a_layer_added_since_goes_too()
        {
            await using McpHarness harness = await Sheet("snapshot-again");
            string id = (string)(await harness.CallAsync("snapshot"))["snapshot"];

            for (int episode = 0; episode < 3; episode++)
            {
                await harness.CallAsync("add_layer", ("name", "Try" + episode));
                await harness.CallAsync(
                    "add_text",
                    ("text", "TRY" + episode),
                    ("at", new[] { 0.0, 0.0 }),
                    ("height", 2.0)
                );
                await harness.CallAsync("reset", ("snapshot", id));

                Assert.Equal(new[] { "PLATE" }, await Texts(harness));
                Assert.DoesNotContain((await harness.CallAsync("list_layers")).AsArray(), l =>
                    ((string)l["name"]).StartsWith("Try", System.StringComparison.Ordinal)
                );
            }
        }

        [Fact]
        public async Task Reset_is_refused_when_the_server_is_read_only_but_a_snapshot_and_its_release_are_not()
        {
            await using McpHarness harness = await Sheet("snapshot-ro", readOnly: true);

            JsonNode taken = await harness.CallAsync("snapshot");
            string refused = await harness.FailAsync("reset", ("snapshot", (string)taken["snapshot"]));
            JsonNode released = await harness.CallAsync("release_snapshot", ("snapshot", (string)taken["snapshot"]));

            Assert.Contains("reset is refused", refused);
            Assert.Contains("--read-only", refused);
            Assert.Equal("released", (string)released["done"]);
        }

        [Fact]
        public async Task A_released_snapshot_cannot_be_reset_to_and_releasing_twice_is_no_error()
        {
            await using McpHarness harness = await Sheet("snapshot-release");
            string id = (string)(await harness.CallAsync("snapshot"))["snapshot"];

            JsonNode first = await harness.CallAsync("release_snapshot", ("snapshot", id));
            JsonNode second = await harness.CallAsync("release_snapshot", ("snapshot", id));
            string message = await harness.FailAsync("reset", ("snapshot", id));

            Assert.Null(first["note"]);
            Assert.Contains("nothing was released", (string)second["note"]);
            Assert.Contains("There is no snapshot " + id, message);
            Assert.Contains("none is held", message);
        }

        [Fact]
        public async Task A_snapshot_ends_with_its_drawing_and_one_of_another_drawing_is_refused_by_id()
        {
            await using McpHarness harness = await Sheet("snapshot-gone");
            string id = (string)(await harness.CallAsync("snapshot"))["snapshot"];
            Assert.Equal(
                new[] { id },
                (await harness.CallAsync("server_status"))["snapshots"].AsArray().Select(s => (string)s).ToArray()
            );

            await harness.CallAsync("new_drawing", ("discard", true));

            Assert.Empty((await harness.CallAsync("server_status"))["snapshots"].AsArray());
            Assert.Contains("There is no snapshot " + id, await harness.FailAsync("reset", ("snapshot", id)));
            string second = (string)(await harness.CallAsync("snapshot"))["snapshot"];
            Assert.Equal("s2", second);
        }

        [Fact]
        public async Task A_snapshot_is_refused_where_the_id_is_empty_and_gives_the_ids_held_where_it_is_wrong()
        {
            await using McpHarness harness = await Sheet("snapshot-ids");
            await harness.CallAsync("snapshot");
            await harness.CallAsync("snapshot");

            string wrong = await harness.FailAsync("reset", ("snapshot", "s9"));

            Assert.Contains("the ones held are s1, s2", wrong);
        }
    }
}
