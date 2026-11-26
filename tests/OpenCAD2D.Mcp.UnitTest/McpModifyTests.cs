using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json.Nodes;
using System.Threading.Tasks;
using GeometryHelper.Dxf;
using Xunit;

namespace OpenCAD2D.Mcp.UnitTest
{
    /// <summary>
    /// A drawing worked on by the tools of the second phase, in both modes: a blank drawing made in this process, or one made in a
    /// drawing app with no window (the Remote test server), which gives the same answers.
    /// </summary>
    internal sealed class Bench : IAsyncDisposable
    {
        private readonly TestApp _app;

        private Bench(McpHarness harness, TestApp app)
        {
            Harness = harness;
            _app = app;
        }

        public McpHarness Harness { get; }

        /// <summary>Starts a server on a new blank drawing, in the mode: <c>file</c> or <c>app</c>.</summary>
        public static async Task<Bench> Start(string mode, bool readOnly = false, bool scripts = false)
        {
            if (mode == "file")
            {
                McpHarness file = await McpHarness.StartAsync(new McpOptions { ReadOnly = readOnly });
                await file.CallAsync("new_drawing");
                return new Bench(file, null);
            }

            TestApp app = TestApp.Start();

            // A read-only server is refused new_drawing in an app, so the blank drawing is made by a server that may write.
            if (readOnly)
            {
                await app.NewAsync();
            }

            McpHarness harness = await McpHarness.StartAsync(
                new McpOptions
                {
                    Attach = true,
                    RemoteFolder = app.Folder,
                    ReadOnly = readOnly,
                    AllowScripts = scripts,
                }
            );

            if (!readOnly)
            {
                await harness.CallAsync("new_drawing");
            }

            return new Bench(harness, app);
        }

        public async ValueTask DisposeAsync()
        {
            await Harness.DisposeAsync();
            _app?.Dispose();
        }

        /// <summary>Gets the number of tool calls made through this bench.</summary>
        public int Calls { get; private set; }

        /// <summary>Counts a call made around the bench, such as one that wants the raw result.</summary>
        public void CountCall() => Calls++;

        public Task<JsonNode> Call(string tool, params (string Name, object Value)[] arguments)
        {
            Calls++;
            return Harness.CallAsync(tool, arguments);
        }

        public Task<string> Fail(string tool, params (string Name, object Value)[] arguments) =>
            Harness.FailAsync(tool, arguments);

        /// <summary>The handle an add tool gave.</summary>
        public async Task<string> Add(string tool, params (string Name, object Value)[] arguments) =>
            (string)(await Call(tool, arguments))["handle"];

        public async Task<JsonNode[]> Entities(string kind = null, string layout = null)
        {
            var arguments = new List<(string, object)> { ("page_size", 500) };

            if (kind != null)
            {
                arguments.Add(("kinds", new[] { kind }));
            }

            if (layout != null)
            {
                arguments.Add(("layout", layout));
            }

            return (await Call("query_entities", arguments.ToArray()))["entities"].AsArray().ToArray();
        }

        public async Task<int> Count(string kind = null, string layout = null) => (await Entities(kind, layout)).Length;

        /// <summary>A line's four numbers as [x1, y1, x2, y2].</summary>
        public static double[] LineEnds(JsonNode entity) =>
            new[]
            {
                (double)entity["geometry"]["start"][0],
                (double)entity["geometry"]["start"][1],
                (double)entity["geometry"]["end"][0],
                (double)entity["geometry"]["end"][1],
            };
    }

    /// <summary>The modify tools, one at a time, in a drawing file and in a drawing app.</summary>
    public class McpModifyTests
    {
        public static IEnumerable<object[]> Modes => new[] { new object[] { "file" }, new object[] { "app" } };

        private static void Near(double expected, double actual) => Assert.Equal(expected, actual, 3);

        [Theory]
        [MemberData(nameof(Modes))]
        public async Task Copy_adds_a_copy_for_each_displacement_and_undo_takes_them_back(string mode)
        {
            await using Bench bench = await Bench.Start(mode);
            string line = await bench.Add("add_line", ("start", new[] { 0.0, 0.0 }), ("end", new[] { 100.0, 0.0 }));

            JsonNode copied = await bench.Call(
                "copy",
                ("handles", new[] { line }),
                ("displacements", new[] { new[] { 0.0, 50.0 }, new[] { 0.0, 80.0 } })
            );

            Assert.Equal(2, copied["created"].AsArray().Count);
            JsonNode[] lines = await bench.Entities("LINE");
            Assert.Equal(3, lines.Length);
            Assert.Contains(lines, l => Math.Abs((double)l["geometry"]["start"][1] - 80.0) < 1e-6);

            await bench.Call("undo");
            Assert.Equal(1, await bench.Count("LINE"));
            JsonNode redone = await bench.Call("redo");
            Assert.True((bool)redone["redone"]);
            Assert.Equal(3, await bench.Count("LINE"));
        }

        [Theory]
        [MemberData(nameof(Modes))]
        public async Task Mirror_keeps_the_source_or_erases_it(string mode)
        {
            await using Bench bench = await Bench.Start(mode);
            string line = await bench.Add("add_line", ("start", new[] { 10.0, 10.0 }), ("end", new[] { 60.0, 10.0 }));

            JsonNode kept = await bench.Call(
                "mirror",
                ("handles", new[] { line }),
                ("line_start", new[] { 0.0, 0.0 }),
                ("line_end", new[] { 0.0, 100.0 })
            );
            string made = (string)kept["created"][0];
            JsonNode[] lines = await bench.Entities("LINE");
            Assert.Equal(2, lines.Length);
            double[] mirrored = Bench.LineEnds(lines.Single(l => (string)l["handle"] == made));
            Near(-10.0, mirrored[0]);
            Near(-60.0, mirrored[2]);

            JsonNode moved = await bench.Call(
                "mirror",
                ("handles", new[] { line }),
                ("line_start", new[] { 0.0, 0.0 }),
                ("line_end", new[] { 0.0, 100.0 }),
                ("keep_source", false)
            );
            Assert.Contains(line, moved["changed"].AsArray().Select(h => (string)h));
            double[] flipped = Bench.LineEnds((await bench.Entities("LINE")).Single(l => (string)l["handle"] == line));
            Near(-10.0, flipped[0]);
            Assert.Equal(2, await bench.Count("LINE"));
        }

        [Theory]
        [MemberData(nameof(Modes))]
        public async Task Offset_makes_a_curve_a_distance_to_the_side_or_through_a_point(string mode)
        {
            await using Bench bench = await Bench.Start(mode);
            string line = await bench.Add("add_line", ("start", new[] { 0.0, 0.0 }), ("end", new[] { 100.0, 0.0 }));

            JsonNode byDistance = await bench.Call(
                "offset",
                ("handle", line),
                ("side", new[] { 50.0, 10.0 }),
                ("distance", 5.0)
            );
            JsonNode throughPoint = await bench.Call("offset", ("handle", line), ("side", new[] { 50.0, -12.0 }));

            JsonNode[] lines = await bench.Entities("LINE");
            double[] up = Bench.LineEnds(lines.Single(l => (string)l["handle"] == (string)byDistance["created"][0]));
            double[] down = Bench.LineEnds(
                lines.Single(l => (string)l["handle"] == (string)throughPoint["created"][0])
            );
            Near(5.0, up[1]);
            Near(-12.0, down[1]);
            string message = await bench.Fail(
                "offset",
                ("handle", line),
                ("side", new[] { 50.0, 10.0 }),
                ("distance", -1.0)
            );
            Assert.Contains("distance", message);
        }

        [Theory]
        [MemberData(nameof(Modes))]
        public async Task Arrays_make_the_copies_of_a_grid_and_of_a_circle(string mode)
        {
            await using Bench bench = await Bench.Start(mode);
            string hole = await bench.Add("add_circle", ("center", new[] { 100.0, 50.0 }), ("radius", 4.0));

            JsonNode grid = await bench.Call(
                "array_rectangular",
                ("handles", new[] { hole }),
                ("rows", 2),
                ("columns", 3),
                ("row_distance", 30.0),
                ("column_distance", 40.0)
            );
            Assert.Equal(5, grid["created"].AsArray().Count);
            Assert.Equal(6, await bench.Count("CIRCLE"));

            // A row or a column of one asks no distance along the other direction.
            JsonNode column = await bench.Call(
                "array_rectangular",
                ("handles", new[] { hole }),
                ("rows", 3),
                ("columns", 1),
                ("row_distance", 5.0),
                ("column_distance", 9.0)
            );
            Assert.Equal(2, column["created"].AsArray().Count);
            JsonNode row = await bench.Call(
                "array_rectangular",
                ("handles", new[] { hole }),
                ("rows", 1),
                ("columns", 3),
                ("row_distance", 9.0),
                ("column_distance", 7.0)
            );
            Assert.Equal(2, row["created"].AsArray().Count);
            Assert.Equal(10, await bench.Count("CIRCLE"));
            Assert.Contains(
                "one row and one column",
                await bench.Fail(
                    "array_rectangular",
                    ("handles", new[] { hole }),
                    ("rows", 1),
                    ("columns", 1),
                    ("row_distance", 1.0),
                    ("column_distance", 1.0)
                )
            );

            string centre = await bench.Add("add_circle", ("center", new[] { 300.0, 0.0 }), ("radius", 3.0));
            JsonNode ring = await bench.Call(
                "array_polar",
                ("handles", new[] { centre }),
                ("center", new[] { 300.0, 50.0 }),
                ("count", 6)
            );
            Assert.Equal(5, ring["created"].AsArray().Count);
            JsonNode[] circles = await bench.Entities("CIRCLE");
            Assert.Equal(16, circles.Length);
            Assert.Contains(circles, c =>
                Math.Abs((double)c["geometry"]["center"][0] - 300.0) < 1e-6
                && Math.Abs((double)c["geometry"]["center"][1] - 100.0) < 1e-6
            );
        }

        [Theory]
        [MemberData(nameof(Modes))]
        public async Task Explode_and_join_turn_a_polyline_into_lines_and_back(string mode)
        {
            await using Bench bench = await Bench.Start(mode);
            string polyline = await bench.Add(
                "add_polyline",
                ("points", new[] { new[] { 0.0, 0.0 }, new[] { 100.0, 0.0 }, new[] { 100.0, 50.0 } })
            );

            JsonNode exploded = await bench.Call("explode", ("handles", new[] { polyline }));

            Assert.Equal(2, exploded["created"].AsArray().Count);
            Assert.Equal(2, await bench.Count("LINE"));
            Assert.Equal(0, await bench.Count("LWPOLYLINE"));
            JsonNode joined = await bench.Call(
                "join",
                ("handles", exploded["created"].AsArray().Select(h => (string)h).ToArray())
            );
            Assert.Single(joined["created"].AsArray());
            Assert.Equal(1, await bench.Count("LWPOLYLINE"));
            Assert.Contains("two", await bench.Fail("join", ("handles", new[] { (string)joined["created"][0] })));
        }

        [Theory]
        [MemberData(nameof(Modes))]
        public async Task Break_trim_extend_and_lengthen_cut_and_stretch_lines(string mode)
        {
            await using Bench bench = await Bench.Start(mode);
            string bar = await bench.Add("add_line", ("start", new[] { 0.0, 0.0 }), ("end", new[] { 100.0, 0.0 }));
            string stick = await bench.Add("add_line", ("start", new[] { 50.0, -20.0 }), ("end", new[] { 50.0, 60.0 }));

            JsonNode broken = await bench.Call("break_at", ("handle", bar), ("point", new[] { 30.0, 0.0 }));
            Assert.Single(broken["created"].AsArray());
            Assert.Equal(3, await bench.Count("LINE"));

            JsonNode trimmed = await bench.Call(
                "trim",
                ("pick_points", new[] { new[] { 50.0, 50.0 } }),
                ("edges", new[] { (string)broken["created"][0] })
            );
            Assert.NotEmpty(trimmed["changed"].AsArray());

            JsonNode longer = await bench.Call(
                "lengthen",
                ("handle", stick),
                ("mode", "delta"),
                ("value", 10.0),
                ("end_point", new[] { 50.0, -15.0 })
            );
            Assert.Contains(stick, longer["changed"].AsArray().Select(h => (string)h));
        }

        [Theory]
        [MemberData(nameof(Modes))]
        public async Task Fillet_and_chamfer_cut_the_corner_of_two_lines(string mode)
        {
            await using Bench bench = await Bench.Start(mode);
            string a = await bench.Add("add_line", ("start", new[] { 0.0, 0.0 }), ("end", new[] { 100.0, 0.0 }));
            string b = await bench.Add("add_line", ("start", new[] { 0.0, 0.0 }), ("end", new[] { 0.0, 100.0 }));

            JsonNode rounded = await bench.Call("fillet", ("first", a), ("second", b), ("radius", 12.0));
            Assert.Single(rounded["created"].AsArray());
            JsonNode arc = (await bench.Entities("ARC")).Single();
            Near(12.0, (double)arc["geometry"]["radius"]);

            string c = await bench.Add("add_line", ("start", new[] { 200.0, 0.0 }), ("end", new[] { 300.0, 0.0 }));
            string d = await bench.Add("add_line", ("start", new[] { 200.0, 0.0 }), ("end", new[] { 200.0, 100.0 }));
            JsonNode cut = await bench.Call(
                "chamfer",
                ("first", c),
                ("second", d),
                ("distance1", 10.0),
                ("distance2", 15.0)
            );
            Assert.Single(cut["created"].AsArray());
            Assert.Single(await bench.Entities("ARC"));
        }

        [Theory]
        [MemberData(nameof(Modes))]
        public async Task Stretch_moves_the_end_a_window_takes(string mode)
        {
            await using Bench bench = await Bench.Start(mode);
            string bar = await bench.Add("add_line", ("start", new[] { 0.0, 0.0 }), ("end", new[] { 100.0, 0.0 }));

            JsonNode stretched = await bench.Call(
                "stretch",
                ("window", new[] { 90.0, -10.0, 110.0, 10.0 }),
                ("displacement", new[] { 25.0, 0.0 })
            );

            Assert.Contains(bar, stretched["changed"].AsArray().Select(h => (string)h));
            double[] ends = Bench.LineEnds((await bench.Entities("LINE")).Single());
            Near(0.0, ends[0]);
            Near(125.0, ends[2]);
        }

        [Theory]
        [MemberData(nameof(Modes))]
        public async Task Set_draw_order_sends_a_filled_shape_behind_another(string mode)
        {
            await using Bench bench = await Bench.Start(mode);
            string first = await bench.Add("add_circle", ("center", new[] { 0.0, 0.0 }), ("radius", 5.0));
            string second = await bench.Add("add_circle", ("center", new[] { 30.0, 0.0 }), ("radius", 5.0));

            await bench.Call("set_draw_order", ("handles", new[] { second }), ("where", "back"));

            Assert.Equal(2, await bench.Count("CIRCLE"));
            Assert.Empty(
                (await bench.Call("set_draw_order", ("handles", new[] { first }), ("where", "front")))["created"]
                    .AsArray()
            );
            Assert.Contains(
                "order",
                await bench.Fail("set_draw_order", ("handles", new[] { first }), ("where", "sideways")) + " order"
            );
        }

        [Theory]
        [MemberData(nameof(Modes))]
        public async Task A_command_that_is_left_asking_is_cancelled_and_the_drawing_goes_on(string mode)
        {
            await using Bench bench = await Bench.Start(mode);
            string text = await bench.Add("add_text", ("text", "WORDS"), ("at", new[] { 0.0, 0.0 }), ("height", 5.0));
            string line = await bench.Add("add_line", ("start", new[] { 0.0, 10.0 }), ("end", new[] { 50.0, 10.0 }));

            string message = await bench.Fail("fillet", ("first", text), ("second", line), ("radius", 3.0));

            Assert.True(message.Contains("cancelled"), message);
            Assert.Equal(2, await bench.Count());
            string next = await bench.Add("add_line", ("start", new[] { 0.0, 20.0 }), ("end", new[] { 50.0, 20.0 }));
            JsonNode copied = await bench.Call(
                "copy",
                ("handles", new[] { next }),
                ("displacements", new[] { new[] { 0.0, 5.0 } })
            );
            Assert.Single(copied["created"].AsArray());
            Assert.Contains(
                "no object",
                await bench.Fail("copy", ("handles", new[] { "FFFF" }), ("displacements", new[] { new[] { 0.0, 5.0 } }))
            );
        }

        [Theory]
        [MemberData(nameof(Modes))]
        public async Task The_other_modes_of_extend_lengthen_fillet_break_offset_and_chamfer(string mode)
        {
            await using Bench bench = await Bench.Start(mode);

            string shortLine = await bench.Add("add_line", ("start", new[] { 0.0, 0.0 }), ("end", new[] { 40.0, 0.0 }));
            string wall = await bench.Add(
                "add_line",
                ("start", new[] { 100.0, -50.0 }),
                ("end", new[] { 100.0, 50.0 })
            );
            JsonNode extended = await bench.Call(
                "extend",
                ("pick_points", new[] { new[] { 35.0, 0.0 } }),
                ("boundaries", new[] { wall })
            );
            Assert.Contains(shortLine, extended["changed"].AsArray().Select(h => (string)h));
            Near(
                100.0,
                Bench.LineEnds((await bench.Entities("LINE")).Single(l => (string)l["handle"] == shortLine))[2]
            );

            JsonNode total = await bench.Call(
                "lengthen",
                ("handle", shortLine),
                ("mode", "total"),
                ("value", 150.0),
                ("end_point", new[] { 95.0, 0.0 })
            );
            Assert.Contains(shortLine, total["changed"].AsArray().Select(h => (string)h));
            Near(
                150.0,
                Bench.LineEnds((await bench.Entities("LINE")).Single(l => (string)l["handle"] == shortLine))[2]
            );
            await bench.Call(
                "lengthen",
                ("handle", shortLine),
                ("mode", "percent"),
                ("value", 50.0),
                ("end_point", new[] { 145.0, 0.0 })
            );
            Near(75.0, Bench.LineEnds((await bench.Entities("LINE")).Single(l => (string)l["handle"] == shortLine))[2]);

            JsonNode cut = await bench.Call(
                "break_at",
                ("handle", shortLine),
                ("point", new[] { 10.0, 0.0 }),
                ("second_point", new[] { 20.0, 0.0 })
            );
            Assert.Single(cut["created"].AsArray());

            string box = await bench.Add(
                "add_polyline",
                (
                    "points",
                    new[] { new[] { 200.0, 0.0 }, new[] { 300.0, 0.0 }, new[] { 300.0, 100.0 }, new[] { 200.0, 100.0 } }
                ),
                ("closed", true)
            );
            await bench.Call("fillet", ("first", box), ("radius", 10.0));
            Assert.Equal(8, Entity(bench, box).NumberOfVertices);

            string source = await bench.Add("add_circle", ("center", new[] { 500.0, 0.0 }), ("radius", 20.0));
            JsonNode outer = await bench.Call(
                "offset",
                ("handle", source),
                ("side", new[] { 500.0, 50.0 }),
                ("distance", 10.0),
                ("erase_source", true)
            );
            Assert.Contains(source, outer["erased"].AsArray().Select(h => (string)h));
            Assert.Equal(30.0, (double)(await bench.Entities("CIRCLE")).Single()["geometry"]["radius"], 3);

            string a = await bench.Add("add_line", ("start", new[] { 600.0, 0.0 }), ("end", new[] { 700.0, 0.0 }));
            string b = await bench.Add("add_line", ("start", new[] { 600.0, 0.0 }), ("end", new[] { 600.0, 100.0 }));
            JsonNode equal = await bench.Call("chamfer", ("first", a), ("second", b), ("distance1", 12.0));
            Assert.Single(equal["created"].AsArray());
            double[] edge = Bench.LineEnds(
                (await bench.Entities("LINE")).Single(l => (string)l["handle"] == (string)equal["created"][0])
            );
            Assert.Equal(
                new[] { 0.0, 12.0, 12.0, 600.0 },
                new[]
                {
                    Math.Min(edge[1], edge[3]),
                    Math.Max(edge[1], edge[3]),
                    Math.Max(edge[0], edge[2]) - 600.0,
                    Math.Min(edge[0], edge[2]),
                }
                    .Select(v => Math.Round(v, 3))
                    .ToArray()
            );
        }

        private static DxfPolyline Entity(Bench bench, string handle) =>
            (DxfPolyline)
                bench
                    .Harness.Session.Backend.Entities("Model")
                    .Single(e => string.Equals(e.Handle, handle, StringComparison.OrdinalIgnoreCase))
                    .Entity;
    }
}
