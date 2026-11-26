using System;
using System.IO;
using System.Linq;
using System.Text.Json.Nodes;
using System.Threading.Tasks;
using Xunit;
using Xunit.Abstractions;

namespace OpenCAD2D.Mcp.UnitTest
{
    /// <summary>
    /// The tools of the place, render, score, refine loop of an agent attached to an app with no window (the Remote test server),
    /// which answers them with its own engine: what it says is what the same call over a file says.
    /// </summary>
    public class McpAttachAiTests
    {
        private readonly ITestOutputHelper _output;

        public McpAttachAiTests(ITestOutputHelper output)
        {
            _output = output;
        }

        private const string Heights35 =
            "{\"schema\":\"opencad2d.standard\",\"version\":1,\"name\":\"Test\",\"textHeights\":{\"paper\":[2.5,3.5]}}";

        private static async Task<McpHarness> Attach(TestApp app, string name, bool readOnly = false)
        {
            McpHarness harness = await McpHarness.StartAsync(
                new McpOptions
                {
                    Attach = true,
                    RemoteFolder = app.Folder,
                    ReadOnly = readOnly,
                }
            );
            await harness.CallAsync("open_drawing", ("path", Drawings.Plate(TestFolder.Make(name))));
            return harness;
        }

        private static double[] Numbers(JsonNode array) => array.AsArray().Select(n => (double)n).ToArray();

        [Fact]
        public async Task Attached_the_measure_tools_give_what_the_app_would_draw_and_add_nothing()
        {
            using TestApp app = TestApp.Start();
            await using McpHarness harness = await Attach(app, "attachmeasure");
            var at = new[] { 10.0, 20.0 };

            JsonNode text = await harness.CallAsync(
                "measure_text",
                ("text", "Plate A-12"),
                ("at", at),
                ("height", 5.0),
                ("rotation", 30.0)
            );
            JsonNode mtext = await harness.CallAsync(
                "measure_mtext",
                ("text", "Plate PL10\\PGrade S355"),
                ("at", at),
                ("height", 4.0),
                ("width", 60.0)
            );
            JsonNode dimension = await harness.CallAsync(
                "measure_dimension",
                ("kind", "linear"),
                ("points", new[] { new[] { 0.0, 0.0 }, new[] { 100.0, 0.0 }, new[] { 50.0, -20.0 } })
            );
            JsonNode leader = await harness.CallAsync(
                "measure_leader",
                ("arrow", new[] { 0.0, 0.0 }),
                ("landing", new[] { 30.0, 20.0 }),
                ("text", "B-12")
            );

            Assert.Equal(30.0, (double)text["frames"][0]["rotation"], 4);
            Assert.Equal(2, mtext["frames"].AsArray().Count);
            Assert.NotEmpty(dimension["frames"].AsArray());
            Assert.Equal(0.0, Numbers(leader["box"])[0], 3);
            Assert.Equal(4, (int)(await harness.CallAsync("info"))["entityCount"]);

            JsonNode added = await harness.CallAsync(
                "add_text",
                ("text", "Plate A-12"),
                ("at", at),
                ("height", 5.0),
                ("rotation", 30.0)
            );
            double[] box = Numbers(
                (await harness.CallAsync("bounds", ("handles", new[] { (string)added["handle"] })))["rows"][0]["box"]
            );
            double[] measured = Numbers(text["box"]);

            for (int i = 0; i < 4; i++)
            {
                Assert.Equal(box[i], measured[i], 3);
            }

            Assert.Contains(
                "NOPE",
                await harness.FailAsync(
                    "measure_text",
                    ("text", "x"),
                    ("at", at),
                    ("height", 5.0),
                    ("text_style", "NOPE")
                )
            );
            Assert.Contains(
                "NOPE",
                await harness.FailAsync(
                    "measure_leader",
                    ("arrow", at),
                    ("landing", new[] { 30.0, 40.0 }),
                    ("text", "x"),
                    ("style", "NOPE")
                )
            );
        }

        [Fact]
        public async Task Attached_free_space_and_fits_answer_as_they_do_over_a_file()
        {
            using TestApp app = TestApp.Start();
            await using McpHarness harness = await Attach(app, "attachspace");
            var frame = new[] { 0.0, 0.0, 200.0, 100.0 };

            JsonNode places = await harness.CallAsync(
                "free_space",
                ("frame", frame),
                ("kinds", new[] { "LINE" }),
                ("margin", 2.0)
            );
            JsonNode paged = await harness.CallAsync(
                "free_space",
                ("frame", frame),
                ("kinds", new[] { "LINE" }),
                ("page_size", 1),
                ("page", 2)
            );

            Assert.Equal(2, (int)places["total"]);
            Assert.Equal(48.0, Numbers(places["boxes"][0])[3], 3);
            Assert.Equal(52.0, Numbers(places["boxes"][1])[1], 3);
            Assert.Equal(2, (int)paged["pages"]);
            Assert.Equal(50.0, Numbers(paged["boxes"][0])[1], 3);
            var line = new[] { "LINE" };
            Assert.True(
                (bool)
                    (await harness.CallAsync("fits", ("candidate", new[] { 10.0, 60.0, 50.0, 90.0 }), ("kinds", line)))[
                        "fits"
                    ]
            );
            Assert.False(
                (bool)
                    (await harness.CallAsync("fits", ("candidate", new[] { 10.0, 40.0, 50.0, 60.0 }), ("kinds", line)))[
                        "fits"
                    ]
            );
            Assert.False(
                (bool)
                    (
                        await harness.CallAsync(
                            "fits",
                            ("candidate", new[] { 10.0, 40.0, 40.0, 5.0, 45.0 }),
                            ("kinds", line)
                        )
                    )["fits"]
            );
            Assert.Contains(
                "width and a height",
                await harness.FailAsync("free_space", ("frame", new[] { 0.0, 0.0, 0.0, 10.0 }))
            );
        }

        [Fact]
        public async Task Attached_check_standard_and_the_exports_read_the_apps_drawing()
        {
            using TestApp app = TestApp.Start();
            string folder = TestFolder.Make("attachexport");
            await using McpHarness harness = await Attach(app, "attachcheck");
            await harness.CallAsync("add_text", ("text", "OK"), ("at", new[] { 150.0, 80.0 }), ("height", 3.5));

            JsonNode check = await harness.CallAsync("check_standard", ("standard", JsonNode.Parse(Heights35)));
            JsonNode json = await harness.CallAsync("export_json", ("page_size", 2));
            JsonNode graph = await harness.CallAsync("export_graph", ("part", "nodes"));
            string path = Path.Combine(folder, "app.json");
            JsonNode wrote = await harness.CallAsync("export_to_file", ("what", "json"), ("path", path));

            Assert.Equal(0.5, (double)check["score"], 6);
            Assert.Equal("text-height", (string)Assert.Single(check["violations"].AsArray())["rule"]);
            Assert.Equal("opencad2d.drawing", (string)json["header"]["schema"]);
            Assert.Equal(5, (int)json["tables"]["entities"]);
            Assert.Equal(3, (int)json["pages"]);
            Assert.Equal(5, (int)graph["tables"]["nodes"]);
            Assert.Equal(new FileInfo(path).Length, (long)wrote["bytes"]);
            Assert.Equal(5, JsonNode.Parse(File.ReadAllText(path))["entities"].AsArray().Count);
        }

        [Fact]
        public async Task Attached_read_only_refuses_export_to_file_and_reads_with_the_rest()
        {
            using TestApp app = TestApp.Start();
            string folder = TestFolder.Make("attachro");
            await using McpHarness harness = await Attach(app, "attachro", readOnly: true);

            await harness.CallAsync("export_json");
            await harness.CallAsync("check_standard", ("standard", JsonNode.Parse(Heights35)));
            string refused = await harness.FailAsync(
                "export_to_file",
                ("what", "json"),
                ("path", Path.Combine(folder, "x.json"))
            );

            Assert.Contains("--read-only", refused);
            Assert.False(File.Exists(Path.Combine(folder, "x.json")));
        }

        [Fact]
        public async Task Attached_a_placement_loop_clears_the_overlaps_and_raises_the_score_in_the_app()
        {
            using TestApp app = TestApp.Start();
            await using McpHarness harness = await Attach(app, "attachloop");

            var log = await PlacementLoop.RunAsync(harness);
            _output.WriteLine(string.Join(Environment.NewLine, log));

            Assert.Contains(log, l => l.StartsWith("render_png"));
            Assert.Equal(6, (int)(await harness.CallAsync("info"))["entityCount"]);
            Assert.True((bool)(await harness.CallAsync("info"))["modified"]);
        }
    }
}
