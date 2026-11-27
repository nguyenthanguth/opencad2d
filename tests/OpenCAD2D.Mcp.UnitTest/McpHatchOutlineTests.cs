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
    /// add_hatch given the handles of objects that make an outline together (N3), in a file and in an app: lines, arcs, open
    /// polylines and elliptic arcs that join end to end into a closed loop, and ellipses; a loop that does not close is refused.
    /// </summary>
    public class McpHatchOutlineTests
    {
        public static IEnumerable<object[]> Modes => new[] { new object[] { "file" }, new object[] { "app" } };

        private static DxfHatch HatchOf(Bench bench, string handle) =>
            (DxfHatch)
                bench
                    .Harness.Session.Backend.Entities("Model")
                    .Single(e => string.Equals(e.Handle, handle, StringComparison.OrdinalIgnoreCase))
                    .Entity;

        private static Task<string> Line(Bench bench, double x1, double y1, double x2, double y2) =>
            bench.Add("add_line", ("start", new[] { x1, y1 }), ("end", new[] { x2, y2 }));

        private static async Task<double[]> Box(Bench bench, string handle) =>
            (await bench.Call("bounds", ("handles", new[] { handle })))["rows"]
                [0]["box"]
                .AsArray()
                .Select(n => (double)n)
                .ToArray();

        [Theory]
        [MemberData(nameof(Modes))]
        public async Task Four_lines_given_in_any_order_and_direction_make_one_outline(string mode)
        {
            await using Bench bench = await Bench.Start(mode);
            string bottom = await Line(bench, 0.0, 0.0, 100.0, 0.0);
            string top = await Line(bench, 0.0, 60.0, 100.0, 60.0);
            string right = await Line(bench, 100.0, 60.0, 100.0, 0.0);
            string left = await Line(bench, 0.0, 0.0, 0.0, 60.0);

            JsonNode made = await bench.Call(
                "add_hatch",
                ("handles", new[] { top, left, bottom, right }),
                ("pattern", "SOLID")
            );

            DxfHatch hatch = HatchOf(bench, (string)made["handle"]);
            Assert.Equal(1, hatch.NumberOfLoops);
            Assert.Equal(6000.0, hatch.Area, 3);
            double[] box = await Box(bench, (string)made["handle"]);
            Assert.Equal(new[] { 0.0, 0.0, 100.0, 60.0 }, box.Select(v => Math.Round(v, 3)).ToArray());
        }

        [Theory]
        [MemberData(nameof(Modes))]
        public async Task Lines_and_arcs_make_a_slot_and_an_open_polyline_takes_part(string mode)
        {
            await using Bench bench = await Bench.Start(mode);
            string bottom = await Line(bench, 0.0, 0.0, 100.0, 0.0);
            string top = await Line(bench, 100.0, 20.0, 0.0, 20.0);
            string right = await bench.Add(
                "add_arc",
                ("center", new[] { 100.0, 10.0 }),
                ("radius", 10.0),
                ("start_angle", -90.0),
                ("end_angle", 90.0)
            );
            string left = await bench.Add(
                "add_arc",
                ("center", new[] { 0.0, 10.0 }),
                ("radius", 10.0),
                ("start_angle", 90.0),
                ("end_angle", 270.0)
            );

            JsonNode slot = await bench.Call(
                "add_hatch",
                ("handles", new[] { bottom, right, top, left }),
                ("pattern", "SOLID")
            );

            // 100 by 20 and a half circle of radius 10 at each end: 2000 + 100 pi.
            DxfHatch hatch = HatchOf(bench, (string)slot["handle"]);
            Assert.Equal(2000.0 + 100.0 * Math.PI, hatch.Area, 2);
            double[] box = await Box(bench, (string)slot["handle"]);
            Assert.Equal(new[] { -10.0, 0.0, 110.0, 20.0 }, box.Select(v => Math.Round(v, 3)).ToArray());

            // A triangle of an open polyline of two sides and a line back: 50 by 40 right-angled, 1000 in area.
            string path = await bench.Add(
                "add_polyline",
                ("points", new[] { new[] { 200.0, 0.0 }, new[] { 250.0, 0.0 }, new[] { 250.0, 40.0 } })
            );
            string back = await Line(bench, 200.0, 0.0, 250.0, 40.0);
            JsonNode triangle = await bench.Call("add_hatch", ("handles", new[] { back, path }), ("pattern", "SOLID"));
            Assert.Equal(1000.0, HatchOf(bench, (string)triangle["handle"]).Area, 3);
        }

        [Theory]
        [MemberData(nameof(Modes))]
        public async Task An_ellipse_and_an_elliptic_arc_closed_by_a_line_are_outlines(string mode)
        {
            await using Bench bench = await Bench.Start(mode);
            string whole = await bench.Add(
                "add_ellipse",
                ("center", new[] { 0.0, 0.0 }),
                ("major_axis_end", new[] { 40.0, 0.0 }),
                ("ratio", 0.5)
            );
            JsonNode disc = await bench.Call("add_hatch", ("handles", new[] { whole }), ("pattern", "SOLID"));
            Assert.Equal(Math.PI * 40.0 * 20.0, HatchOf(bench, (string)disc["handle"]).Area, 1);

            // The upper half of an ellipse of radii 40 and 20 about (200, 0) and the line across its foot: half the area.
            string arc = await bench.Add(
                "add_ellipse",
                ("center", new[] { 200.0, 0.0 }),
                ("major_axis_end", new[] { 240.0, 0.0 }),
                ("ratio", 0.5),
                ("start_angle", 0.0),
                ("end_angle", 180.0)
            );
            string foot = await Line(bench, 240.0, 0.0, 160.0, 0.0);
            JsonNode half = await bench.Call("add_hatch", ("handles", new[] { foot, arc }), ("pattern", "SOLID"));
            Assert.Equal(Math.PI * 40.0 * 20.0 / 2.0, HatchOf(bench, (string)half["handle"]).Area, 1);
            double[] box = await Box(bench, (string)half["handle"]);
            Assert.Equal(160.0, box[0], 1);
            Assert.Equal(240.0, box[2], 1);
            Assert.Equal(20.0, box[3], 1);
        }

        [Theory]
        [MemberData(nameof(Modes))]
        public async Task A_chain_that_does_not_close_is_refused_and_a_hair_of_gap_is_not_a_gap(string mode)
        {
            await using Bench bench = await Bench.Start(mode);
            string a = await Line(bench, 0.0, 0.0, 100.0, 0.0);
            string b = await Line(bench, 100.0, 0.0, 100.0, 50.0);
            string c = await Line(bench, 100.0, 50.0, 0.0, 0.5);

            string refused = await bench.Fail("add_hatch", ("handles", new[] { a, b, c }));
            Assert.Contains("do not close", refused);
            Assert.Contains(a, refused);
            Assert.Equal(0, await bench.Count("HATCH"));

            // Two lines of three that meet: the third has nothing to join to.
            string refusedTwo = await bench.Fail("add_hatch", ("handles", new[] { a, b }));
            Assert.Contains("do not close", refusedTwo);

            // A last side that stops 0.0004 off the first start closes within the point tolerance of 0.001.
            string close = await Line(bench, 300.0, 0.0, 400.0, 0.0);
            string closeB = await Line(bench, 400.0, 0.0, 400.0, 50.0);
            string closeC = await Line(bench, 400.0, 50.0, 300.0004, 0.0);
            JsonNode made = await bench.Call(
                "add_hatch",
                ("handles", new[] { close, closeB, closeC }),
                ("pattern", "SOLID")
            );
            Assert.Equal(2500.0, HatchOf(bench, (string)made["handle"]).Area, 1);
        }

        [Theory]
        [MemberData(nameof(Modes))]
        public async Task A_chain_and_a_circle_island_keep_the_order_of_the_handles(string mode)
        {
            await using Bench bench = await Bench.Start(mode);
            string a = await Line(bench, 0.0, 0.0, 100.0, 0.0);
            string b = await Line(bench, 100.0, 0.0, 100.0, 100.0);
            string c = await Line(bench, 100.0, 100.0, 0.0, 100.0);
            string d = await Line(bench, 0.0, 100.0, 0.0, 0.0);
            string island = await bench.Add("add_circle", ("center", new[] { 50.0, 50.0 }), ("radius", 20.0));

            JsonNode made = await bench.Call(
                "add_hatch",
                ("handles", new[] { a, island, b, c, d }),
                ("pattern", "SOLID")
            );

            DxfHatch hatch = HatchOf(bench, (string)made["handle"]);
            Assert.Equal(2, hatch.NumberOfLoops);
            Assert.Equal(10000.0 - Math.PI * 400.0, hatch.Area, 1);
        }
    }
}
