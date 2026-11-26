using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json.Nodes;
using System.Threading.Tasks;
using ModelContextProtocol.Protocol;
using Xunit;

namespace OpenCAD2D.Mcp.UnitTest
{
    /// <summary>
    /// The place, render, score, refine loop of an agent that lays annotation out, as a scripted session of tool calls over the
    /// plate of <see cref="Drawings.Plate"/>: the same script runs over a file and over an app.
    /// </summary>
    internal static class PlacementLoop
    {
        /// <summary>A standard that lets words be 3.5 or 5 high on paper and keeps them 1 mm from a line and from each other.</summary>
        public const string Standard =
            "{\"schema\":\"opencad2d.standard\",\"version\":1,\"name\":\"Loop\",\"textHeights\":{\"paper\":[3.5,5]},\"gaps\":{\"wordLine\":1.0,\"wordWord\":1.0}}";

        private static readonly string[] Obstacles = { "LINE", "CIRCLE", "LWPOLYLINE", "TEXT" };

        private static double[] Numbers(JsonNode array) => array.AsArray().Select(n => (double)n).ToArray();

        /// <summary>
        /// Adds two tags across the line of the plate, finds that they overlap, measures each, asks for the free places, moves each
        /// to the middle of the largest place it fits in, and shows that the overlaps are gone, the standard's score has risen and a
        /// picture can be drawn. Returns what each call said, one line a call.
        /// </summary>
        public static async Task<List<string>> RunAsync(McpHarness harness)
        {
            var log = new List<string>();
            JsonNode standard = JsonNode.Parse(Standard);
            double[] sheet = { 0.0, 0.0, 200.0, 100.0 };

            string[] tags = new string[2];
            double[][] at = { new[] { 80.0, 48.0 }, new[] { 20.0, 48.5 } };

            for (int i = 0; i < 2; i++)
            {
                JsonNode added = await harness.CallAsync(
                    "add_text",
                    ("text", "TAG-" + (i + 1)),
                    ("at", at[i]),
                    ("height", 3.5)
                );
                tags[i] = (string)added["handle"];
                log.Add("add_text TAG-" + (i + 1) + " -> " + tags[i]);
            }

            JsonNode before = await harness.CallAsync("overlaps", ("of", tags));
            int overlapsBefore = (int)before["total"];
            log.Add("overlaps of the tags -> " + overlapsBefore);
            Assert.True(overlapsBefore > 0, "The tags across the line must overlap it.");

            JsonNode scoreBefore = await harness.CallAsync("check_standard", ("standard", standard));
            double first = (double)scoreBefore["score"];
            log.Add(
                "check_standard -> score " + first.ToString("0.000", System.Globalization.CultureInfo.InvariantCulture)
            );
            Assert.True(first < 1.0, "The tags across the line must break the gap rule.");
            Assert.Contains(scoreBefore["violations"].AsArray(), v =>
                (string)v["rule"] == "gap-word-line" && tags.Contains((string)v["handle"])
            );

            for (int i = 0; i < 2; i++)
            {
                JsonNode measured = await harness.CallAsync(
                    "measure_text",
                    ("text", "TAG-" + (i + 1)),
                    ("at", at[i]),
                    ("height", 3.5)
                );
                JsonNode frame = measured["frames"][0];
                double width = (double)frame["width"];
                double height = (double)frame["height"];
                log.Add(
                    "measure_text TAG-"
                        + (i + 1)
                        + " -> frame "
                        + width.ToString("0.0")
                        + " by "
                        + height.ToString("0.0")
                );

                JsonNode places = await harness.CallAsync(
                    "free_space",
                    ("frame", sheet),
                    ("kinds", Obstacles),
                    ("margin", 2.0),
                    ("min_width", width),
                    ("min_height", height),
                    ("page_size", 5)
                );
                Assert.True((int)places["total"] > 0, "There is no place for the tag.");
                double[] room = Numbers(places["boxes"][0]);
                log.Add("free_space -> " + (int)places["total"] + " places, the largest " + string.Join(",", room));

                double[] box = Numbers(
                    (await harness.CallAsync("bounds", ("handles", new[] { tags[i] })))["rows"][0]["box"]
                );
                double dx = (room[0] + room[2]) / 2.0 - (box[0] + box[2]) / 2.0;
                double dy = (room[1] + room[3]) / 2.0 - (box[1] + box[3]) / 2.0;
                await harness.CallAsync("move", ("handles", new[] { tags[i] }), ("delta", new[] { dx, dy }));
                log.Add("move " + tags[i] + " by " + dx.ToString("0.0") + ", " + dy.ToString("0.0"));

                double[] moved = Numbers(
                    (await harness.CallAsync("bounds", ("handles", new[] { tags[i] })))["rows"][0]["box"]
                );
                JsonNode fits = await harness.CallAsync(
                    "fits",
                    ("candidate", moved),
                    ("kinds", new[] { "LINE", "CIRCLE", "LWPOLYLINE" }),
                    ("margin", 1.0)
                );
                Assert.True((bool)fits["fits"], "The tag does not fit where it was put.");
                log.Add("fits -> true");
            }

            JsonNode after = await harness.CallAsync("overlaps", ("of", tags));
            log.Add("overlaps of the tags -> " + (int)after["total"]);
            Assert.Equal(0, (int)after["total"]);

            JsonNode scoreAfter = await harness.CallAsync("check_standard", ("standard", standard));
            double second = (double)scoreAfter["score"];
            log.Add(
                "check_standard -> score " + second.ToString("0.000", System.Globalization.CultureInfo.InvariantCulture)
            );
            Assert.True(second > first, "The score did not rise: " + first + " then " + second);
            Assert.DoesNotContain(scoreAfter["violations"].AsArray(), v =>
                (string)v["rule"] == "gap-word-line" || (string)v["rule"] == "gap-word-word"
            );

            CallToolResult picture = await harness.CallRawAsync("render_png", ("width", 400));
            Assert.True(picture.IsError != true);
            ImageContentBlock image = Assert.Single(picture.Content.OfType<ImageContentBlock>());
            Assert.Equal("image/png", image.MimeType);
            log.Add("render_png -> " + image.DecodedData.Length + " bytes");
            Assert.True(image.DecodedData.Length > 1000);
            return log;
        }
    }
}
