using System.Globalization;
using System.Linq;
using System.Text.Json.Nodes;
using System.Threading.Tasks;
using Xunit;

namespace OpenCAD2D.Mcp.UnitTest
{
    /// <summary>
    /// The place, render, score, refine loop of <see cref="PlacementLoop"/>, redone as plans: what needs no arithmetic of the agent
    /// goes into one call, so that the loop that took 19 calls takes 4.
    /// </summary>
    internal static class PlanPlacementLoop
    {
        /// <summary>The calls <see cref="PlacementLoop"/> makes: 15 it logs, and the 4 bounds calls it does not.</summary>
        public const int CallsOfTheSingleLoop = 19;

        private static readonly string Kinds = "[\"LINE\",\"CIRCLE\",\"LWPOLYLINE\",\"TEXT\"]";

        private static double[] Numbers(JsonNode array) => array.AsArray().Select(n => (double)n).ToArray();

        private static string F(double value) => value.ToString("R", CultureInfo.InvariantCulture);

        /// <summary>
        /// Runs the loop over the plate of <see cref="Drawings.Plate"/>, asserting what <see cref="PlacementLoop.RunAsync"/> does,
        /// and returns the number of calls it made.
        /// </summary>
        public static async Task<int> RunAsync(McpHarness harness)
        {
            int calls = 0;

            async Task<JsonNode> Plan(string steps)
            {
                calls++;
                JsonNode result = await harness.CallAsync("run_plan", ("steps", JsonNode.Parse(steps)));
                Assert.True((bool)result["ok"], (string)result["message"]);
                return result;
            }

            // 1. Add the two tags, find that they overlap, and score the sheet.
            JsonNode first = await Plan(
                "[{\"tool\":\"add_text\",\"id\":\"t1\",\"arguments\":{\"text\":\"TAG-1\",\"at\":[80,48],\"height\":3.5}},"
                    + "{\"tool\":\"add_text\",\"id\":\"t2\",\"arguments\":{\"text\":\"TAG-2\",\"at\":[20,48.5],\"height\":3.5}},"
                    + "{\"tool\":\"overlaps\",\"id\":\"over\",\"arguments\":{\"of\":[\"$t1.handle\",\"$t2.handle\"]}},"
                    + "{\"tool\":\"check_standard\",\"id\":\"score\",\"arguments\":{\"standard\":"
                    + PlacementLoop.Standard
                    + "}}]"
            );
            string[] tags =
            {
                (string)first["steps"][0]["result"]["handle"],
                (string)first["steps"][1]["result"]["handle"],
            };
            Assert.True((int)first["steps"][2]["result"]["total"] > 0, "The tags across the line must overlap it.");
            double before = (double)first["steps"][3]["result"]["score"];
            Assert.True(before < 1.0);

            // 2. Measure tag 1, ask where there is room for it (its width and height taken from the measure), and where it stands.
            JsonNode second = await Plan(
                "[{\"tool\":\"measure_text\",\"id\":\"m1\",\"arguments\":{\"text\":\"TAG-1\",\"at\":[80,48],\"height\":3.5}},"
                    + "{\"tool\":\"free_space\",\"id\":\"room1\",\"arguments\":{\"frame\":[0,0,200,100],\"kinds\":"
                    + Kinds
                    + ",\"margin\":2,\"min_width\":\"$m1.frames[0].width\",\"min_height\":\"$m1.frames[0].height\",\"page_size\":5}},"
                    + "{\"tool\":\"bounds\",\"id\":\"at1\",\"arguments\":{\"handles\":[\""
                    + tags[0]
                    + "\"]}},"
                    + "{\"tool\":\"measure_text\",\"id\":\"m2\",\"arguments\":{\"text\":\"TAG-2\",\"at\":[20,48.5],\"height\":3.5}}]"
            );
            double[] room = Numbers(second["steps"][1]["result"]["boxes"][0]);
            double[] box = Numbers(second["steps"][2]["result"]["rows"][0]["box"]);
            double width2 = (double)second["steps"][3]["result"]["frames"][0]["width"];
            double height2 = (double)second["steps"][3]["result"]["frames"][0]["height"];
            double dx = (room[0] + room[2]) / 2.0 - (box[0] + box[2]) / 2.0;
            double dy = (room[1] + room[3]) / 2.0 - (box[1] + box[3]) / 2.0;

            // 3. Move tag 1, check that it fits, then ask where there is room for tag 2 and where tag 2 stands.
            JsonNode third = await Plan(
                "[{\"tool\":\"move\",\"arguments\":{\"handles\":[\""
                    + tags[0]
                    + "\"],\"delta\":["
                    + F(dx)
                    + ","
                    + F(dy)
                    + "]}},"
                    + "{\"tool\":\"bounds\",\"id\":\"now1\",\"arguments\":{\"handles\":[\""
                    + tags[0]
                    + "\"]}},"
                    + "{\"tool\":\"fits\",\"id\":\"fits1\",\"arguments\":{\"candidate\":\"$now1.rows[0].box\",\"kinds\":[\"LINE\",\"CIRCLE\",\"LWPOLYLINE\"],\"margin\":1}},"
                    + "{\"tool\":\"free_space\",\"id\":\"room2\",\"arguments\":{\"frame\":[0,0,200,100],\"kinds\":"
                    + Kinds
                    + ",\"margin\":2,\"min_width\":"
                    + F(width2)
                    + ",\"min_height\":"
                    + F(height2)
                    + ",\"page_size\":5}},"
                    + "{\"tool\":\"bounds\",\"id\":\"at2\",\"arguments\":{\"handles\":[\""
                    + tags[1]
                    + "\"]}}]"
            );
            Assert.True((bool)third["steps"][2]["result"]["fits"], "Tag 1 does not fit where it was put.");
            double[] room2 = Numbers(third["steps"][3]["result"]["boxes"][0]);
            double[] box2 = Numbers(third["steps"][4]["result"]["rows"][0]["box"]);
            double dx2 = (room2[0] + room2[2]) / 2.0 - (box2[0] + box2[2]) / 2.0;
            double dy2 = (room2[1] + room2[3]) / 2.0 - (box2[1] + box2[3]) / 2.0;

            // 4. Move tag 2, check it, count the overlaps, score again and draw: all in one call.
            JsonNode fourth = await Plan(
                "[{\"tool\":\"move\",\"arguments\":{\"handles\":[\""
                    + tags[1]
                    + "\"],\"delta\":["
                    + F(dx2)
                    + ","
                    + F(dy2)
                    + "]}},"
                    + "{\"tool\":\"bounds\",\"id\":\"now2\",\"arguments\":{\"handles\":[\""
                    + tags[1]
                    + "\"]}},"
                    + "{\"tool\":\"fits\",\"id\":\"fits2\",\"arguments\":{\"candidate\":\"$now2.rows[0].box\",\"kinds\":[\"LINE\",\"CIRCLE\",\"LWPOLYLINE\"],\"margin\":1}},"
                    + "{\"tool\":\"overlaps\",\"id\":\"over\",\"arguments\":{\"of\":[\""
                    + tags[0]
                    + "\",\""
                    + tags[1]
                    + "\"]}},"
                    + "{\"tool\":\"check_standard\",\"id\":\"score\",\"arguments\":{\"standard\":"
                    + PlacementLoop.Standard
                    + "}},"
                    + "{\"tool\":\"render_png\",\"id\":\"picture\",\"arguments\":{\"width\":400}}]"
            );
            Assert.True((bool)fourth["steps"][2]["result"]["fits"], "Tag 2 does not fit where it was put.");
            Assert.Equal(0, (int)fourth["steps"][3]["result"]["total"]);
            double after = (double)fourth["steps"][4]["result"]["score"];
            Assert.True(after > before, "The score did not rise: " + before + " then " + after);
            Assert.DoesNotContain(fourth["steps"][4]["result"]["violations"].AsArray(), v =>
                (string)v["rule"] == "gap-word-line" || (string)v["rule"] == "gap-word-word"
            );
            Assert.True((int)fourth["steps"][5]["result"]["images"][0]["bytes"] > 1000);

            // The tags are where the single-call loop puts them: nothing overlaps them, as seen by a call of its own.
            JsonNode check = await harness.CallAsync("overlaps", ("of", tags));
            Assert.Equal(0, (int)check["total"]);
            return calls;
        }
    }
}
