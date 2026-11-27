using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json.Nodes;
using System.Threading.Tasks;
using GeometryHelper.Dxf;
using GeometryHelper.Geometry;
using Xunit;

namespace OpenCAD2D.Mcp.UnitTest
{
    /// <summary>
    /// add_hatch from objects, the points a review made (N3): outlines at a height are flattened as circles and polylines are, a line
    /// that rises is refused, pieces shorter than the tolerance are left out, an arc or a polyline that faces down and an ellipse
    /// that faces down are taken as drawn, and an outline of no area is refused.
    /// </summary>
    public class McpHatchReviewTests
    {
        public static IEnumerable<object[]> Modes => new[] { new object[] { "file" }, new object[] { "app" } };

        private static readonly double HalfEllipse = Math.PI * 40.0 * 20.0 / 2.0;
        private static readonly double HalfCircle = Math.PI * 40.0 * 40.0 / 2.0;

        private static async Task<double> Area(Bench bench, string hatch) =>
            (double)(await bench.Call("get_entity", ("handle", hatch)))["details"]["area"];

        private static Task<string> Line(Bench bench, double x1, double y1, double x2, double y2) =>
            bench.Add("add_line", ("start", new[] { x1, y1 }), ("end", new[] { x2, y2 }));

        private static Task<string> UpperHalf(Bench bench, double z = 0.0) =>
            bench.Add(
                "add_ellipse",
                ("center", new[] { 0.0, 0.0, z }),
                ("major_axis_end", new[] { 40.0, 0.0, z }),
                ("ratio", 0.5),
                ("start_angle", 0.0),
                ("end_angle", 180.0)
            );

        [Theory]
        [MemberData(nameof(Modes))]
        public async Task An_ellipse_and_a_line_at_a_height_are_flattened_and_a_line_that_rises_is_refused(string mode)
        {
            await using Bench bench = await Bench.Start(mode);
            string whole = await bench.Add(
                "add_ellipse",
                ("center", new[] { 300.0, 0.0, 5.0 }),
                ("major_axis_end", new[] { 340.0, 0.0, 5.0 }),
                ("ratio", 0.5)
            );
            string disc = await bench.Add("add_hatch", ("handles", new[] { whole }), ("pattern", "SOLID"));
            Assert.Equal(Math.PI * 40.0 * 20.0, await Area(bench, disc), 1);

            string arc = await UpperHalf(bench, 5.0);
            string foot = await bench.Add(
                "add_line",
                ("start", new[] { 40.0, 0.0, 5.0 }),
                ("end", new[] { -40.0, 0.0, 5.0 })
            );
            string half = await bench.Add("add_hatch", ("handles", new[] { arc, foot }), ("pattern", "SOLID"));
            Assert.Equal(HalfEllipse, await Area(bench, half), 1);

            string rising = await bench.Add(
                "add_line",
                ("start", new[] { 40.0, 0.0, 0.0 }),
                ("end", new[] { -40.0, 0.0, 9.0 })
            );
            Assert.Contains(
                "differ in height",
                await bench.Fail("add_hatch", ("handles", new[] { arc, rising }), ("pattern", "SOLID"))
            );
        }

        [Theory]
        [MemberData(nameof(Modes))]
        public async Task A_side_that_is_an_arc_closes_an_elliptic_arc_and_a_hair_of_a_piece_is_left_out(string mode)
        {
            await using Bench bench = await Bench.Start(mode);
            string top = await UpperHalf(bench);
            string below = await bench.Add(
                "add_arc",
                ("center", new[] { 0.0, 0.0 }),
                ("radius", 40.0),
                ("start_angle", 180.0),
                ("end_angle", 360.0)
            );
            string both = await bench.Add("add_hatch", ("handles", new[] { top, below }), ("pattern", "SOLID"));
            Assert.Equal(HalfEllipse + HalfCircle, await Area(bench, both), 1);

            // A line of no length first, and an arc that comes back to its start within the tolerance: left out, the outline is made.
            string nothing = await Line(bench, 500.0, 500.0, 500.0, 500.0);
            string almost = await bench.Add(
                "add_arc",
                ("center", new[] { 600.0, 600.0 }),
                ("radius", 10.0),
                ("start_angle", 0.0),
                ("end_angle", 359.9997)
            );
            string foot = await Line(bench, 40.0, 0.0, -40.0, 0.0);
            string made = await bench.Add(
                "add_hatch",
                ("handles", new[] { nothing, almost, top, foot }),
                ("pattern", "SOLID")
            );
            Assert.Equal(HalfEllipse, await Area(bench, made), 1);

            // Nothing but such pieces is refused in words.
            Assert.Contains(
                "shorter than",
                await bench.Fail("add_hatch", ("handles", new[] { nothing, almost }), ("pattern", "SOLID"))
            );
        }

        [Theory]
        [MemberData(nameof(Modes))]
        public async Task An_elliptic_arc_gone_over_again_encloses_no_area(string mode)
        {
            await using Bench bench = await Bench.Start(mode);
            string one = await UpperHalf(bench);
            string again = await UpperHalf(bench);

            Assert.Contains("enclose no area", await bench.Fail("add_hatch", ("handles", new[] { one, again })));
            Assert.Equal(0, await bench.Count("HATCH"));
        }

        /// <summary>A drawing of shapes that face down: an arc, an open polyline with an arc, a whole ellipse and an elliptic arc.</summary>
        private static string Down(string name)
        {
            var database = new DxfDatabase("AC1027");
            var down = new GeoVector3(0, 0, -1);
            database.ModelSpace.AppendEntity(
                new DxfEllipse(
                    new GeoPoint3(0, 0, 0),
                    new GeoVector3(0, 0, 1),
                    new GeoVector3(40, 0, 0),
                    0.5,
                    0.0,
                    Math.PI
                )
            );

            // Seen from above it is the lower half of the circle of radius 40, run from (40, 0) round to (-40, 0).
            database.ModelSpace.AppendEntity(
                new DxfArc
                {
                    Center = new GeoPoint3(0, 0, 0),
                    Radius = 40,
                    Normal = down,
                    StartAngle = Math.PI,
                    EndAngle = 2.0 * Math.PI,
                }
            );

            // Its own system runs (-40, 0) to (40, 0) by a half circle below; in the world it is (40, 0) to (-40, 0) below.
            var path = new DxfPolyline { Normal = down };
            path.AddVertexAt(0, new GeoPoint2(-40, 0), 1, 0, 0);
            path.AddVertexAt(1, new GeoPoint2(40, 0), 0, 0, 0);
            database.ModelSpace.AppendEntity(path);
            database.ModelSpace.AppendEntity(
                new DxfEllipse(new GeoPoint3(200, 0, 0), down, new GeoVector3(40, 0, 0), 0.5, 0.0, 2.0 * Math.PI)
            );
            database.ModelSpace.AppendEntity(
                new DxfEllipse(new GeoPoint3(400, 0, 0), down, new GeoVector3(40, 0, 0), 0.5, 0.0, Math.PI)
            );
            database.ModelSpace.AppendEntity(new DxfLine(new GeoPoint3(360, 0, 0), new GeoPoint3(440, 0, 0)));
            string file = Path.Combine(TestFolder.Make(name), "down.dxf");
            CadDocument.From(database).SaveAs(file);
            return file;
        }

        [Theory]
        [InlineData("file")]
        [InlineData("app")]
        public async Task Arcs_polylines_and_ellipses_that_face_down_are_taken_as_drawn(string mode)
        {
            string file = Down("hatchdown");
            using TestApp app = mode == "app" ? TestApp.Start() : null;

            if (app != null)
            {
                await app.OpenAsync(file);
            }

            await using McpHarness harness = await McpHarness.StartAsync(
                mode == "app"
                    ? new McpOptions { Attach = true, RemoteFolder = app.Folder }
                    : new McpOptions { File = file }
            );
            JsonNode listed = await harness.CallAsync("query_entities", ("page_size", 50));
            string[] all = listed["entities"].AsArray().Select(e => (string)e["handle"]).ToArray();
            Assert.Equal(6, all.Length);

            async Task<double> Hatched(params string[] handles)
            {
                JsonNode made = await harness.CallAsync("add_hatch", ("handles", handles), ("pattern", "SOLID"));
                JsonNode entity = await harness.CallAsync("get_entity", ("handle", (string)made["handle"]));
                return (double)entity["details"]["area"];
            }

            // The half ellipse above and the half circle below, as the arc facing down is drawn: not the one that bulges up.
            Assert.Equal(HalfEllipse + HalfCircle, await Hatched(all[0], all[1]), 1);

            // The same with a polyline of its own system turned about, its bulge with it.
            Assert.Equal(HalfEllipse + HalfCircle, await Hatched(all[0], all[2]), 1);
            Assert.Equal(Math.PI * 40.0 * 20.0, await Hatched(all[3]), 1);
            Assert.Equal(HalfEllipse, await Hatched(all[4], all[5]), 1);
        }
    }
}
