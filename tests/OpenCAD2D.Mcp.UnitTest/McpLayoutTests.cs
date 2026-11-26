using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json.Nodes;
using System.Threading.Tasks;
using GeometryHelper.Dxf;
using Xunit;

namespace OpenCAD2D.Mcp.UnitTest
{
    /// <summary>The layout tools: a sheet with its paper, the tab shown, viewports at a scale.</summary>
    public class McpLayoutTests
    {
        public static IEnumerable<object[]> Modes => new[] { new object[] { "file" }, new object[] { "app" } };

        private static void Near(double expected, double actual) => Assert.Equal(expected, actual, 3);

        /// <summary>The drawing as the backend has it now, a copy of the database in either mode.</summary>
        internal static CadDocument Snapshot(Bench bench)
        {
            IDrawingBackend backend = bench.Harness.Session.Backend;
            DxfDatabase database = backend is LocalBackend local
                ? local.Document.Database
                : ((RemoteBackend)backend).Document.GetDatabase();
            return CadDocument.From(DxfDatabase.Parse(database.ToText()), "snapshot.dxf");
        }

        private static DxfViewport[] Viewports(Bench bench, string layout) =>
            Snapshot(bench)
                .Spaces.Single(s => string.Equals(s.Name, layout, StringComparison.OrdinalIgnoreCase))
                .Block.OfType<DxfViewport>()
                .Where(v => !v.IsErased)
                .ToArray();

        private static DxfViewport Viewport(Bench bench, string layout, string handle) =>
            Viewports(bench, layout).Single(v => string.Equals(v.Handle, handle, StringComparison.OrdinalIgnoreCase));

        [Theory]
        [MemberData(nameof(Modes))]
        public async Task Create_layout_makes_an_a3_sheet_in_millimetres_and_set_current_layout_shows_it(string mode)
        {
            await using Bench bench = await Bench.Start(mode);

            JsonNode made = await bench.Call("create_layout", ("name", "Sheet1"), ("paper", "ISO A3"));
            JsonNode small = await bench.Call(
                "create_layout",
                ("name", "Sheet2"),
                ("paper", "ISO A4"),
                ("landscape", false)
            );

            Assert.Equal("Sheet1", (string)made["name"]);
            Assert.Contains("Sheet1", ((JsonArray)(await bench.Call("info"))["layouts"]).Select(l => (string)l));
            CadDocument snapshot = Snapshot(bench);
            CadPlotSettings a3 = CadPlotSettings.Read(snapshot, snapshot.Spaces.Single(s => s.Name == "Sheet1"));
            Near(420.0, a3.Paper.Width);
            Near(297.0, a3.Paper.Height);
            Assert.True(a3.Landscape);
            Assert.Equal(CadPlotArea.Layout, a3.Area);
            Near(1.0, a3.Scale);
            CadPlotSettings a4 = CadPlotSettings.Read(snapshot, snapshot.Spaces.Single(s => s.Name == "Sheet2"));
            Near(297.0, a4.Paper.Width);
            Assert.False(a4.Landscape);

            Assert.Equal("Sheet1", (string)(await bench.Call("set_current_layout", ("name", "sheet1")))["name"]);
            Assert.Equal("Sheet1", (string)(await bench.Call("info"))["currentLayout"]);
            Assert.Equal("Model", (string)(await bench.Call("set_current_layout", ("name", "Model")))["name"]);
            Assert.Contains("already exists", await bench.Fail("create_layout", ("name", "sheet1")));
            Assert.Contains(
                "paper is one of",
                await bench.Fail("create_layout", ("name", "Sheet3"), ("paper", "Foolscap"))
            );
            Assert.DoesNotContain("Sheet3", ((JsonArray)(await bench.Call("info"))["layouts"]).Select(l => (string)l));
        }

        [Theory]
        [MemberData(nameof(Modes))]
        public async Task Two_viewports_at_1_to_10_and_1_to_5_show_model_space_at_their_scales(string mode)
        {
            await using Bench bench = await Bench.Start(mode);
            await bench.Call("add_layer", ("name", "Notes"));
            await bench.Add("add_rectangle", ("corner1", new[] { 0.0, 0.0 }), ("corner2", new[] { 1000.0, 600.0 }));
            await bench.Call("create_layout", ("name", "Sheet1"), ("paper", "ISO A3"));

            JsonNode first = await bench.Call(
                "add_viewport",
                ("layout", "Sheet1"),
                ("frame", new[] { 10.0, 10.0, 210.0, 150.0 }),
                ("scale", 10.0),
                ("center", new[] { 500.0, 300.0 })
            );
            JsonNode second = await bench.Call(
                "add_viewport",
                ("layout", "Sheet1"),
                ("frame", new[] { 220.0, 10.0, 410.0, 150.0 }),
                ("scale", 5.0),
                ("center", new[] { 100.0, 50.0 }),
                ("freeze_layers", new[] { "Notes" })
            );

            string one = (string)first["handles"][0];
            string two = (string)second["handles"][0];
            DxfViewport a = Viewport(bench, "Sheet1", one);
            DxfViewport b = Viewport(bench, "Sheet1", two);
            Near(0.1, a.CustomScale);
            Near(0.2, b.CustomScale);
            Assert.Equal(new[] { "Notes" }, b.GetFrozenLayers().Select(l => l.Name).ToArray());
            Assert.Empty(a.GetFrozenLayers());
            Near(200.0, a.Width);
            Near(500.0, a.ViewCenter.X);
            Near(300.0, a.ViewCenter.Y);
            Near(100.0, b.ViewCenter.X);
            Assert.Equal("Model", (string)(await bench.Call("info"))["currentLayout"]);

            await bench.Call("set_viewport_scale", ("handle", one), ("scale", 20.0));
            Near(0.05, Viewport(bench, "Sheet1", one).CustomScale);
            Near(500.0, Viewport(bench, "Sheet1", one).ViewCenter.X);
            await bench.Call("lock_viewport", ("handle", one));
            Assert.True(Viewport(bench, "Sheet1", one).Locked);
            Assert.Contains("locked", await bench.Fail("set_viewport_scale", ("handle", one), ("scale", 10.0)));
            await bench.Call("lock_viewport", ("handle", one), ("locked", false));
            Assert.False(Viewport(bench, "Sheet1", one).Locked);

            Assert.Contains(
                "Model is not a sheet",
                await bench.Fail(
                    "add_viewport",
                    ("layout", "Model"),
                    ("frame", new[] { 0.0, 0.0, 10.0, 10.0 }),
                    ("scale", 1.0)
                )
            );
            int before = Viewports(bench, "Sheet1").Length;
            JsonNode third = await bench.Call(
                "add_viewport",
                ("layout", "Sheet1"),
                ("frame", new[] { 10.0, 160.0, 100.0, 200.0 }),
                ("scale", 2.0)
            );
            Assert.Equal(before + 1, Viewports(bench, "Sheet1").Length);
            Assert.NotNull((string)third["handles"][0]);
            await bench.Call("undo");
            Assert.Equal(before, Viewports(bench, "Sheet1").Length);
        }
    }
}
