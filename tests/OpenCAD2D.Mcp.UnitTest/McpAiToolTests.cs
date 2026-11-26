using System;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.Json.Nodes;
using System.Threading.Tasks;
using Xunit;
using Xunit.Abstractions;

namespace OpenCAD2D.Mcp.UnitTest
{
    /// <summary>
    /// The tools of the place, render, score, refine loop over a drawing file: measuring before adding, the empty places of a frame,
    /// whether a place fits, the standard check and the exports.
    /// </summary>
    public class McpAiToolTests
    {
        private readonly ITestOutputHelper _output;

        public McpAiToolTests(ITestOutputHelper output)
        {
            _output = output;
        }

        private const string Heights35 =
            "{\"schema\":\"opencad2d.standard\",\"version\":1,\"name\":\"Test\",\"textHeights\":{\"paper\":[2.5,3.5]}}";

        private static async Task<McpHarness> Plate(string name, bool readOnly = false) =>
            await McpHarness.StartAsync(
                new McpOptions { File = Drawings.Plate(TestFolder.Make(name)), ReadOnly = readOnly }
            );

        private static double[] Numbers(JsonNode array) => array.AsArray().Select(n => (double)n).ToArray();

        private static void AssertBox(double[] expected, double[] actual, int digits = 3)
        {
            Assert.Equal(4, actual.Length);

            for (int i = 0; i < 4; i++)
            {
                Assert.Equal(expected[i], actual[i], digits);
            }
        }

        private static async Task<double[]> BoundsOf(McpHarness harness, JsonNode added) =>
            Numbers(
                (await harness.CallAsync("bounds", ("handles", new[] { (string)added["handle"] })))["rows"][0]["box"]
            );

        [Fact]
        public async Task Measure_text_gives_the_box_the_text_takes_once_added_and_adds_nothing()
        {
            await using McpHarness harness = await Plate("measuretext");
            var at = new[] { 10.0, 20.0 };

            JsonNode measured = await harness.CallAsync(
                "measure_text",
                ("text", "Plate A-12"),
                ("at", at),
                ("height", 5.0)
            );

            Assert.Equal(4, (int)(await harness.CallAsync("info"))["entityCount"]);
            Assert.False((bool)(await harness.CallAsync("info"))["modified"]);
            JsonNode added = await harness.CallAsync("add_text", ("text", "Plate A-12"), ("at", at), ("height", 5.0));
            AssertBox(await BoundsOf(harness, added), Numbers(measured["box"]));
            JsonNode frame = Assert.Single(measured["frames"].AsArray());
            Assert.True((double)frame["width"] > 20.0);
            Assert.True((double)frame["height"] >= 4.0);
            Assert.Equal(0.0, (double)frame["rotation"], 6);
            Assert.InRange((double)frame["x"], 9.9, 10.1);
        }

        [Fact]
        public async Task Measure_text_turns_with_its_rotation_and_stands_about_its_justification()
        {
            await using McpHarness harness = await Plate("measuretextturn");
            var at = new[] { 100.0, 50.0 };

            JsonNode turned = await harness.CallAsync(
                "measure_text",
                ("text", "TURNED"),
                ("at", at),
                ("height", 5.0),
                ("rotation", 90.0)
            );
            JsonNode level = await harness.CallAsync("measure_text", ("text", "TURNED"), ("at", at), ("height", 5.0));
            JsonNode centred = await harness.CallAsync(
                "measure_text",
                ("text", "TURNED"),
                ("at", at),
                ("height", 5.0),
                ("justify", "mc")
            );

            Assert.Equal(90.0, (double)turned["frames"][0]["rotation"], 4);
            double[] box = Numbers(turned["box"]);
            Assert.True(box[3] - box[1] > box[2] - box[0], "A text turned a quarter turn is taller than it is wide.");
            double[] middle = Numbers(centred["box"]);
            Assert.True(middle[0] < 100.0 && 100.0 < middle[2] && middle[1] < 50.0 && 50.0 < middle[3]);
            Assert.Equal(100.0, Numbers(level["box"])[0], 1);
        }

        [Fact]
        public async Task Measure_text_refuses_a_style_a_justification_and_a_height_it_cannot_take()
        {
            await using McpHarness harness = await Plate("measuretextbad");
            var at = new[] { 0.0, 0.0 };

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
                "justification",
                await harness.FailAsync(
                    "measure_text",
                    ("text", "x"),
                    ("at", at),
                    ("height", 5.0),
                    ("justify", "sideways")
                )
            );
            Assert.Contains(
                "above nought",
                await harness.FailAsync("measure_text", ("text", "x"), ("at", at), ("height", 0.0))
            );
            Assert.Contains(
                "needs words",
                await harness.FailAsync("measure_text", ("text", ""), ("at", at), ("height", 1.0))
            );
        }

        [Fact]
        public async Task Measure_mtext_wraps_at_the_width_and_equals_the_box_of_the_mtext_once_added()
        {
            await using McpHarness harness = await Plate("measuremtext");
            var at = new[] { 10.0, 90.0 };

            JsonNode lines = await harness.CallAsync(
                "measure_mtext",
                ("text", "Plate PL10\\PGrade S355"),
                ("at", at),
                ("height", 4.0),
                ("width", 60.0)
            );
            JsonNode wrapped = await harness.CallAsync(
                "measure_mtext",
                ("text", "alpha beta gamma delta epsilon zeta"),
                ("at", at),
                ("height", 4.0),
                ("width", 30.0)
            );
            JsonNode single = await harness.CallAsync(
                "measure_mtext",
                ("text", "alpha beta gamma delta epsilon zeta"),
                ("at", at),
                ("height", 4.0)
            );

            Assert.Equal(2, lines["frames"].AsArray().Count);
            Assert.True(wrapped["frames"].AsArray().Count > single["frames"].AsArray().Count);
            JsonNode added = await harness.CallAsync(
                "add_mtext",
                ("text", "Plate PL10\\PGrade S355"),
                ("at", at),
                ("height", 4.0),
                ("width", 60.0)
            );
            AssertBox(await BoundsOf(harness, added), Numbers(lines["box"]));
            Assert.Equal(5, (int)(await harness.CallAsync("info"))["entityCount"]);
        }

        [Fact]
        public async Task Measure_mtext_turns_and_attaches_where_told_and_refuses_a_word_justification()
        {
            await using McpHarness harness = await Plate("measuremtextturn");
            var at = new[] { 100.0, 50.0 };

            JsonNode topLeft = await harness.CallAsync("measure_mtext", ("text", "Note"), ("at", at), ("height", 4.0));
            JsonNode bottomRight = await harness.CallAsync(
                "measure_mtext",
                ("text", "Note"),
                ("at", at),
                ("height", 4.0),
                ("justify", "BR")
            );
            JsonNode turned = await harness.CallAsync(
                "measure_mtext",
                ("text", "Note"),
                ("at", at),
                ("height", 4.0),
                ("rotation", 90.0)
            );

            Assert.InRange(Numbers(topLeft["box"])[0], 99.0, 100.5);
            Assert.True(Numbers(bottomRight["box"])[2] <= 100.5, "A text attached bottom right ends at its point.");
            Assert.Equal(90.0, (double)turned["frames"][0]["rotation"], 4);
            Assert.Contains(
                "justification",
                await harness.FailAsync(
                    "measure_mtext",
                    ("text", "x"),
                    ("at", at),
                    ("height", 4.0),
                    ("justify", "left")
                )
            );
        }

        [Fact]
        public async Task Measure_dimension_equals_the_box_of_the_dimension_once_added_for_the_kinds_that_have_a_twin()
        {
            await using McpHarness harness = await Plate("measuredim");

            JsonNode linear = await harness.CallAsync(
                "measure_dimension",
                ("kind", "linear"),
                ("points", new[] { new[] { 0.0, 0.0 }, new[] { 100.0, 0.0 }, new[] { 50.0, -20.0 } })
            );
            JsonNode added = await harness.CallAsync(
                "add_linear_dim",
                ("first", new[] { 0.0, 0.0 }),
                ("second", new[] { 100.0, 0.0 }),
                ("location", new[] { 50.0, -20.0 })
            );
            AssertBox(await BoundsOf(harness, added), Numbers(linear["box"]));
            Assert.NotEmpty(linear["frames"].AsArray());

            JsonNode radial = await harness.CallAsync(
                "measure_dimension",
                ("kind", "radial"),
                ("points", new[] { new[] { 100.0, 50.0 }, new[] { 120.0, 50.0 }, new[] { 130.0, 70.0 } })
            );
            JsonNode radialAdded = await harness.CallAsync(
                "add_radial_dim",
                ("center", new[] { 100.0, 50.0 }),
                ("radius", 20.0),
                ("location", new[] { 130.0, 70.0 })
            );
            AssertBox(await BoundsOf(harness, radialAdded), Numbers(radial["box"]));
        }

        [Fact]
        public async Task Measure_dimension_takes_every_kind_and_a_text_and_refuses_what_it_cannot_make()
        {
            await using McpHarness harness = await Plate("measuredimkinds");
            double[][] three = { new[] { 0.0, 0.0 }, new[] { 100.0, 0.0 }, new[] { 50.0, 20.0 } };

            JsonNode plain = await harness.CallAsync("measure_dimension", ("kind", "aligned"), ("points", three));
            JsonNode words = await harness.CallAsync(
                "measure_dimension",
                ("kind", "Aligned"),
                ("points", three),
                ("text", "100 TYP. OVERALL")
            );
            JsonNode diameter = await harness.CallAsync(
                "measure_dimension",
                ("kind", "diameter"),
                ("points", new[] { new[] { 100.0, 50.0 }, new[] { 120.0, 50.0 }, new[] { 130.0, 70.0 } })
            );
            JsonNode angular = await harness.CallAsync(
                "measure_dimension",
                ("kind", "angular"),
                ("points", new[] { new[] { 0.0, 0.0 }, new[] { 50.0, 0.0 }, new[] { 0.0, 50.0 }, new[] { 35.0, 35.0 } })
            );

            Assert.NotNull(plain["box"]);
            Assert.True(
                Numbers(words["box"])[2] - Numbers(words["box"])[0]
                    > Numbers(plain["box"])[2] - Numbers(plain["box"])[0] - 0.001,
                "Longer words do not make the picture narrower."
            );
            Assert.NotNull(diameter["box"]);
            Assert.NotNull(angular["box"]);
            Assert.Contains(
                "linear, aligned",
                await harness.FailAsync("measure_dimension", ("kind", "oblique"), ("points", three))
            );
            Assert.Contains(
                "4 points",
                await harness.FailAsync("measure_dimension", ("kind", "angular"), ("points", three))
            );
            Assert.Contains(
                "NOPE",
                await harness.FailAsync(
                    "measure_dimension",
                    ("kind", "linear"),
                    ("points", three),
                    ("dim_style", "NOPE")
                )
            );
        }

        [Fact]
        public async Task Measure_leader_gives_the_box_of_the_leader_and_the_frame_of_its_words()
        {
            await using McpHarness harness = await Plate("measureleader");

            JsonNode measured = await harness.CallAsync(
                "measure_leader",
                ("arrow", new[] { 0.0, 0.0 }),
                ("landing", new[] { 30.0, 20.0 }),
                ("text", "B-12")
            );

            double[] box = Numbers(measured["box"]);
            Assert.Equal(0.0, box[0], 3);
            Assert.True(box[2] > 30.0, "The words stand past the landing.");
            Assert.True(box[3] >= 20.0);
            Assert.NotEmpty(measured["frames"].AsArray());
            Assert.Equal(4, (int)(await harness.CallAsync("info"))["entityCount"]);
            Assert.Contains(
                "scale",
                await harness.FailAsync(
                    "measure_leader",
                    ("arrow", new[] { 0.0, 0.0 }),
                    ("landing", new[] { 30.0, 20.0 }),
                    ("text", "x"),
                    ("scale", -1.0)
                )
            );
            Assert.Contains(
                "NOPE",
                await harness.FailAsync(
                    "measure_leader",
                    ("arrow", new[] { 0.0, 0.0 }),
                    ("landing", new[] { 30.0, 20.0 }),
                    ("text", "x"),
                    ("style", "NOPE")
                )
            );
        }

        [Fact]
        public async Task Free_space_gives_the_largest_empty_rectangles_first_and_keeps_the_margin()
        {
            await using McpHarness harness = await Plate("freespace");
            var frame = new[] { 0.0, 0.0, 200.0, 100.0 };

            JsonNode plain = await harness.CallAsync("free_space", ("frame", frame), ("kinds", new[] { "line" }));
            JsonNode margin = await harness.CallAsync(
                "free_space",
                ("frame", frame),
                ("kinds", new[] { "LINE" }),
                ("margin", 2.0)
            );

            Assert.Equal(2, (int)plain["total"]);
            AssertBox(new[] { 0.0, 0.0, 200.0, 50.0 }, Numbers(plain["boxes"][0]));
            AssertBox(new[] { 0.0, 50.0, 200.0, 100.0 }, Numbers(plain["boxes"][1]));
            AssertBox(new[] { 0.0, 0.0, 200.0, 48.0 }, Numbers(margin["boxes"][0]));
            AssertBox(new[] { 0.0, 52.0, 200.0, 100.0 }, Numbers(margin["boxes"][1]));
        }

        [Fact]
        public async Task Free_space_keeps_only_what_the_thing_could_stand_in_and_pages_a_long_answer()
        {
            await using McpHarness harness = await Plate("freespacepage");
            var frame = new[] { 0.0, 0.0, 200.0, 100.0 };

            JsonNode everything = await harness.CallAsync("free_space", ("frame", frame));
            JsonNode first = await harness.CallAsync("free_space", ("frame", frame), ("page_size", 1));
            JsonNode second = await harness.CallAsync("free_space", ("frame", frame), ("page_size", 1), ("page", 2));
            JsonNode tall = await harness.CallAsync(
                "free_space",
                ("frame", frame),
                ("kinds", new[] { "LINE" }),
                ("min_height", 60.0)
            );
            JsonNode wide = await harness.CallAsync(
                "free_space",
                ("frame", frame),
                ("kinds", new[] { "LINE" }),
                ("min_width", 201.0)
            );

            int total = (int)everything["total"];
            Assert.True(total > 2);
            Assert.Equal(total, (int)first["total"]);
            Assert.Equal(total, (int)first["pages"]);
            Assert.Single(first["boxes"].AsArray());
            AssertBox(Numbers(everything["boxes"][0]), Numbers(first["boxes"][0]));
            AssertBox(Numbers(everything["boxes"][1]), Numbers(second["boxes"][0]));
            Assert.Equal(0, (int)tall["total"]);
            Assert.Equal(0, (int)wide["total"]);
            Assert.Equal(0, (int)wide["pages"]);
        }

        [Fact]
        public async Task Free_space_refuses_a_frame_a_margin_and_a_layout_it_cannot_use()
        {
            await using McpHarness harness = await Plate("freespacebad");

            Assert.Contains(
                "width and a height",
                await harness.FailAsync("free_space", ("frame", new[] { 0.0, 0.0, 0.0, 10.0 }))
            );
            Assert.Contains("four numbers", await harness.FailAsync("free_space", ("frame", new[] { 0.0, 0.0, 10.0 })));
            Assert.Contains(
                "nought or more",
                await harness.FailAsync("free_space", ("frame", new[] { 0.0, 0.0, 10.0, 10.0 }), ("margin", -1.0))
            );
            Assert.Contains(
                "no layout named",
                await harness.FailAsync("free_space", ("frame", new[] { 0.0, 0.0, 10.0, 10.0 }), ("layout", "Nowhere"))
            );
        }

        [Fact]
        public async Task Fits_tests_a_box_and_a_frame_against_what_is_drawn_and_the_margin()
        {
            await using McpHarness harness = await Plate("fits");
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
                (bool)(await harness.CallAsync("fits", ("candidate", new[] { 90.0, 40.0, 110.0, 60.0 })))["fits"]
            );

            var near = new[] { 10.0, 51.0, 50.0, 60.0 };
            Assert.True((bool)(await harness.CallAsync("fits", ("candidate", near), ("kinds", line)))["fits"]);
            Assert.False(
                (bool)(await harness.CallAsync("fits", ("candidate", near), ("kinds", line), ("margin", 2.0)))["fits"]
            );

            Assert.True(
                (bool)
                    (
                        await harness.CallAsync(
                            "fits",
                            ("candidate", new[] { 10.0, 60.0, 40.0, 30.0, 0.0 }),
                            ("kinds", line)
                        )
                    )["fits"]
            );
            Assert.False(
                (bool)
                    (
                        await harness.CallAsync(
                            "fits",
                            ("candidate", new[] { 10.0, 45.0, 40.0, 10.0, 0.0 }),
                            ("kinds", line)
                        )
                    )["fits"]
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
        }

        [Fact]
        public async Task Fits_takes_the_frame_measure_text_gave_and_refuses_a_candidate_it_cannot_use()
        {
            await using McpHarness harness = await Plate("fitsframe");
            JsonNode measured = await harness.CallAsync(
                "measure_text",
                ("text", "FREE"),
                ("at", new[] { 20.0, 80.0 }),
                ("height", 5.0)
            );
            JsonNode onLine = await harness.CallAsync(
                "measure_text",
                ("text", "ONLINE"),
                ("at", new[] { 20.0, 48.0 }),
                ("height", 5.0)
            );

            double[] free = Frame(measured["frames"][0]);
            double[] crossing = Frame(onLine["frames"][0]);

            Assert.True((bool)(await harness.CallAsync("fits", ("candidate", free)))["fits"]);
            Assert.False((bool)(await harness.CallAsync("fits", ("candidate", crossing)))["fits"]);
            Assert.Contains("box", await harness.FailAsync("fits", ("candidate", new[] { 1.0, 2.0, 3.0 })));
            Assert.Contains(
                "above nought",
                await harness.FailAsync("fits", ("candidate", new[] { 1.0, 2.0, 1.0, 5.0 }))
            );
            Assert.Contains("nought or more", await harness.FailAsync("fits", ("candidate", free), ("margin", -1.0)));
        }

        private static double[] Frame(JsonNode frame) =>
            new[]
            {
                (double)frame["x"],
                (double)frame["y"],
                (double)frame["width"],
                (double)frame["height"],
                (double)frame["rotation"],
            };

        [Fact]
        public async Task Check_standard_scores_the_drawing_and_lists_what_breaks_a_rule()
        {
            await using McpHarness harness = await Plate("check");
            JsonNode small = await harness.CallAsync(
                "add_text",
                ("text", "OK"),
                ("at", new[] { 150.0, 80.0 }),
                ("height", 3.5)
            );

            JsonNode result = await harness.CallAsync("check_standard", ("standard", JsonNode.Parse(Heights35)));

            Assert.Equal("Test", (string)result["name"]);
            Assert.Equal(0.5, (double)result["score"], 6);
            JsonNode rule = Assert.Single(result["rules"].AsArray());
            Assert.Equal("text-height", (string)rule["rule"]);
            Assert.Equal(2, (int)rule["subjects"]);
            Assert.Equal(1, (int)rule["offenders"]);
            Assert.Equal(1, (int)result["total"]);
            JsonNode violation = Assert.Single(result["violations"].AsArray());
            Assert.NotEqual((string)small["handle"], (string)violation["handle"]);
            Assert.Equal("text-height", (string)violation["rule"]);
            Assert.Contains("5", (string)violation["measured"]);
            Assert.Contains("2.5", (string)violation["allowed"]);
            Assert.Equal(5.0, (double)violation["value"], 3);
            Assert.Equal(4, Numbers(violation["box"]).Length);
            Assert.Equal(5, (int)(await harness.CallAsync("info"))["entityCount"]);
        }

        [Fact]
        public async Task Check_standard_reads_the_standard_from_an_object_a_json_text_or_a_file()
        {
            string folder = TestFolder.Make("checksource");
            string path = Path.Combine(folder, "company.json");
            File.WriteAllText(path, Heights35, new UTF8Encoding(false));
            await using McpHarness harness = await Plate("checksources");

            JsonNode asObject = await harness.CallAsync("check_standard", ("standard", JsonNode.Parse(Heights35)));
            JsonNode asText = await harness.CallAsync("check_standard", ("standard", Heights35));
            JsonNode asFile = await harness.CallAsync("check_standard", ("standard", path));

            Assert.Equal((double)asObject["score"], (double)asText["score"], 9);
            Assert.Equal((double)asObject["score"], (double)asFile["score"], 9);
            Assert.Equal(0.0, (double)asFile["score"], 6);
            Assert.Contains(
                "no standard file",
                await harness.FailAsync("check_standard", ("standard", path + ".missing"))
            );
            Assert.Contains("JSON object", await harness.FailAsync("check_standard", ("standard", 5)));
            Assert.False(
                string.IsNullOrWhiteSpace(
                    await harness.FailAsync("check_standard", ("standard", JsonNode.Parse("{\"schema\":\"nope\"}")))
                )
            );
            Assert.Contains(
                "model_scale",
                await harness.FailAsync("check_standard", ("standard", Heights35), ("model_scale", -1.0))
            );
        }

        [Fact]
        public async Task Check_standard_pages_the_violations_and_the_model_scale_changes_the_height_on_paper()
        {
            await using McpHarness harness = await Plate("checkpage");

            for (int i = 0; i < 4; i++)
            {
                await harness.CallAsync(
                    "add_text",
                    ("text", "BIG" + i),
                    ("at", new[] { 20.0 + 30 * i, 80.0 }),
                    ("height", 6.0)
                );
            }

            JsonNode first = await harness.CallAsync("check_standard", ("standard", Heights35), ("page_size", 2));
            JsonNode last = await harness.CallAsync(
                "check_standard",
                ("standard", Heights35),
                ("page_size", 2),
                ("page", 3)
            );
            // Model space plotted 1:100: a text 5 high is 0.05 mm on paper, which no height of the standard lists either.
            JsonNode scaled = await harness.CallAsync(
                "check_standard",
                (
                    "standard",
                    "{\"schema\":\"opencad2d.standard\",\"version\":1,\"textHeights\":{\"paper\":[0.05,0.06]}}"
                ),
                ("model_scale", 0.01)
            );

            Assert.Equal(5, (int)first["total"]);
            Assert.Equal(3, (int)first["pages"]);
            Assert.Equal(2, first["violations"].AsArray().Count);
            Assert.Single(last["violations"].AsArray());
            Assert.NotEqual((string)first["violations"][0]["handle"], (string)last["violations"][0]["handle"]);
            Assert.Equal(
                1.0 - 4.0 / 5.0,
                (double)
                    (
                        await harness.CallAsync(
                            "check_standard",
                            (
                                "standard",
                                "{\"schema\":\"opencad2d.standard\",\"version\":1,\"textHeights\":{\"paper\":[5]}}"
                            )
                        )
                    )["score"],
                6
            );
            Assert.Equal(1.0, (double)scaled["score"], 6);
        }

        [Fact]
        public async Task Export_json_summarises_the_document_and_pages_its_entities()
        {
            await using McpHarness harness = await Plate("exportjson");

            JsonNode all = await harness.CallAsync("export_json");
            JsonNode paged = await harness.CallAsync("export_json", ("page_size", 3));
            JsonNode second = await harness.CallAsync("export_json", ("page_size", 3), ("page", 2));

            Assert.Equal("opencad2d.drawing", (string)all["header"]["schema"]);
            Assert.Equal("entities", (string)all["part"]);
            Assert.Equal(4, (int)all["tables"]["entities"]);
            Assert.Equal(4, (int)all["total"]);
            Assert.Equal(4, all["rows"].AsArray().Count);
            Assert.Equal(1, (int)all["kinds"]["LINE"]);
            Assert.Equal(1, (int)all["kinds"]["CIRCLE"]);
            Assert.Equal(1, (int)all["kinds"]["TEXT"]);
            Assert.Equal(4, all["kinds"].AsObject().Sum(k => (int)k.Value));
            Assert.Equal(2, (int)paged["pages"]);
            Assert.Equal(3, paged["rows"].AsArray().Count);
            Assert.Single(second["rows"].AsArray());
            Assert.Equal((string)all["rows"][3]["handle"], (string)second["rows"][0]["handle"]);
            Assert.Equal(4, (int)(await harness.CallAsync("export_json", ("layout", "Model")))["tables"]["entities"]);
            Assert.Contains("no layout named", await harness.FailAsync("export_json", ("layout", "Nowhere")));
        }

        [Fact]
        public async Task Export_graph_counts_nodes_and_edges_and_pages_either_list()
        {
            await using McpHarness harness = await Plate("exportgraph");

            JsonNode edges = await harness.CallAsync("export_graph");
            JsonNode nodes = await harness.CallAsync("export_graph", ("part", "nodes"), ("page_size", 3));

            Assert.Equal("opencad2d.graph", (string)edges["header"]["schema"]);
            Assert.Equal(4, (int)edges["tables"]["nodes"]);
            Assert.True((int)edges["tables"]["edges"] >= 1, "The line ends on the outline: they touch.");
            Assert.Equal((int)edges["tables"]["edges"], (int)edges["total"]);
            Assert.True((int)edges["kinds"]["touches"] >= 1);
            Assert.Equal("edges", (string)edges["part"]);
            Assert.NotNull(edges["rows"][0]["source"]);
            Assert.NotNull(edges["rows"][0]["target"]);
            Assert.Equal("nodes", (string)nodes["part"]);
            Assert.Equal(4, (int)nodes["total"]);
            Assert.Equal(3, nodes["rows"].AsArray().Count);
            Assert.Equal(2, (int)nodes["pages"]);
            Assert.Contains("edges or nodes", await harness.FailAsync("export_graph", ("part", "faces")));
            Assert.Contains("above nought", await harness.FailAsync("export_graph", ("tolerance", 0.0)));
        }

        [Fact]
        public async Task Export_to_file_writes_the_whole_document_where_asked_and_nowhere_else()
        {
            string folder = TestFolder.Make("exportfile");
            string plate = Drawings.Plate(folder);
            await using McpHarness harness = await McpHarness.StartAsync(new McpOptions { File = plate });
            await harness.CallAsync("export_json");
            await harness.CallAsync("export_graph");
            Assert.Equal(new[] { "plate.dxf" }, Directory.GetFiles(folder).Select(Path.GetFileName).ToArray());
            string json = Path.Combine(folder, "plate.json");
            string graph = Path.Combine(folder, "plate-graph.json");

            JsonNode wrote = await harness.CallAsync("export_to_file", ("what", "json"), ("path", json));
            JsonNode wroteGraph = await harness.CallAsync("export_to_file", ("what", "graph"), ("path", graph));

            Assert.Equal(json, (string)wrote["path"]);
            Assert.Equal(new FileInfo(json).Length, (long)wrote["bytes"]);
            Assert.Equal(new FileInfo(graph).Length, (long)wroteGraph["bytes"]);
            Assert.Equal("opencad2d.drawing", (string)JsonNode.Parse(File.ReadAllText(json))["schema"]);
            Assert.Equal(4, JsonNode.Parse(File.ReadAllText(json))["entities"].AsArray().Count);
            Assert.Equal("opencad2d.graph", (string)JsonNode.Parse(File.ReadAllText(graph))["schema"]);
            Assert.Equal(new byte[] { (byte)'{' }, File.ReadAllBytes(json).Take(1).ToArray());
            Assert.Contains(
                "json or graph",
                await harness.FailAsync("export_to_file", ("what", "csv"), ("path", json))
            );
            Assert.Contains(
                "Give the path",
                await harness.FailAsync("export_to_file", ("what", "json"), ("path", " "))
            );

            // A folder that is not there is made; a file that is there, the drawing's own file and another extension are refused.
            string deeper = Path.Combine(folder, "made", "x");
            Assert.Equal(
                deeper + ".json",
                (string)(await harness.CallAsync("export_to_file", ("what", "json"), ("path", deeper)))["path"]
            );
            Assert.Contains(
                "is there already",
                await harness.FailAsync("export_to_file", ("what", "json"), ("path", json))
            );
            await harness.CallAsync("export_to_file", ("what", "json"), ("path", json), ("overwrite", true));
            byte[] own = File.ReadAllBytes(plate);
            Assert.Contains(
                "The file is .json, not .dxf",
                await harness.FailAsync("export_to_file", ("what", "json"), ("path", plate), ("overwrite", true))
            );
            Assert.Contains(
                "not .txt",
                await harness.FailAsync(
                    "export_to_file",
                    ("what", "json"),
                    ("path", Path.Combine(folder, "x.txt")),
                    ("overwrite", true)
                )
            );
            Assert.Equal(own, File.ReadAllBytes(plate));
        }

        [Fact]
        public async Task A_read_only_server_reads_with_every_new_tool_and_refuses_export_to_file()
        {
            string folder = TestFolder.Make("aireadonly");
            await using McpHarness harness = await McpHarness.StartAsync(
                new McpOptions { File = Drawings.Plate(folder), ReadOnly = true }
            );
            var at = new[] { 10.0, 20.0 };

            await harness.CallAsync("measure_text", ("text", "x"), ("at", at), ("height", 2.0));
            await harness.CallAsync("measure_mtext", ("text", "x"), ("at", at), ("height", 2.0));
            await harness.CallAsync(
                "measure_dimension",
                ("kind", "linear"),
                ("points", new[] { new[] { 0.0, 0.0 }, new[] { 10.0, 0.0 }, new[] { 5.0, 5.0 } })
            );
            await harness.CallAsync("measure_leader", ("arrow", at), ("landing", new[] { 30.0, 40.0 }), ("text", "x"));
            await harness.CallAsync("free_space", ("frame", new[] { 0.0, 0.0, 50.0, 50.0 }));
            await harness.CallAsync("fits", ("candidate", new[] { 0.0, 0.0, 5.0, 5.0 }));
            await harness.CallAsync("check_standard", ("standard", JsonNode.Parse(Heights35)));
            await harness.CallAsync("export_json");
            await harness.CallAsync("export_graph");

            string refused = await harness.FailAsync(
                "export_to_file",
                ("what", "json"),
                ("path", Path.Combine(folder, "out.json"))
            );

            Assert.Contains("--read-only", refused);
            Assert.False(File.Exists(Path.Combine(folder, "out.json")));
            Assert.False((bool)(await harness.CallAsync("info"))["modified"]);
        }

        [Fact]
        public async Task A_placement_loop_clears_the_overlaps_and_raises_the_score_over_a_file()
        {
            await using McpHarness harness = await Plate("loop");

            var log = await PlacementLoop.RunAsync(harness);
            _output.WriteLine(string.Join(Environment.NewLine, log));

            Assert.Contains(log, l => l.StartsWith("render_png"));
            Assert.Equal(1, log.Count(l => l.StartsWith("overlaps of the tags -> 0")));
            Assert.Equal(6, (int)(await harness.CallAsync("info"))["entityCount"]);
            Assert.True((bool)(await harness.CallAsync("info"))["modified"]);
            Assert.True((bool)(await harness.CallAsync("undo"))["undone"]);
        }
    }
}
