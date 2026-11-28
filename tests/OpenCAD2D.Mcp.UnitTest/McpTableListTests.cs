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
    /// list_layouts, list_linetypes and list_blocks give the same rows from a drawing file and from an app, which answers
    /// with the summaries of its spaces, linetypes and blocks rather than copying its whole drawing.
    /// </summary>
    public class McpTableListTests
    {
        public static IEnumerable<object[]> Modes => new[] { new object[] { "file" }, new object[] { "app" } };

        [Theory]
        [MemberData(nameof(Modes))]
        public async Task Layouts_linetypes_and_blocks_reflect_edits_and_viewports_in_both_modes(string mode)
        {
            await using Bench bench = await Bench.Start(mode);
            await bench.Call("add_layer", ("name", "zeta"));
            await bench.Call("add_layer", ("name", "Alpha"));
            await bench.Call("load_linetype", ("name", "DASHED"));
            await bench.Call("load_linetype", ("name", "CENTER"));

            string c = await bench.Add("add_circle", ("center", new[] { 0.0, 0.0 }), ("radius", 5.0));
            string l = await bench.Add("add_line", ("start", new[] { -5.0, 0.0 }), ("end", new[] { 5.0, 0.0 }));
            await bench.Call(
                "define_block",
                ("name", "Nut"),
                ("base_point", new[] { 0.0, 0.0 }),
                ("handles", new[] { c }),
                ("source", "delete")
            );
            await bench.Call(
                "define_block",
                ("name", "bolt"),
                ("base_point", new[] { 0.0, 0.0 }),
                ("handles", new[] { l }),
                ("source", "keep")
            );

            await bench.Call("create_layout", ("name", "Detail"), ("paper", "ISO A4"), ("landscape", false));
            JsonNode vp = await bench.Call(
                "add_viewport",
                ("layout", "Detail"),
                ("frame", new[] { 10.0, 20.0, 190.0, 140.0 }),
                ("scale", 10.0),
                ("center", new[] { 50.0, 25.0 }),
                ("freeze_layers", new[] { "zeta", "Alpha" })
            );
            await bench.Call("lock_viewport", ("handle", (string)vp["handles"][0]));
            await bench.Call("set_current_layout", ("name", "Detail"));

            JsonNode layouts = await bench.Call("list_layouts");
            Assert.Equal(
                new[] { "Model", "Layout1", "Detail" },
                layouts.AsArray().Select(r => (string)r["name"]).ToArray()
            );
            JsonNode detail = layouts.AsArray().Single(r => (string)r["name"] == "Detail");
            Assert.True((bool)detail["current"]);
            Assert.Equal("millimetres", (string)detail["paperUnits"]);
            JsonNode frame = Assert.Single(detail["viewports"].AsArray());
            Assert.True((bool)frame["locked"]);
            Assert.Equal("1:10", (string)frame["scale"]);
            Assert.Equal(new[] { "Alpha", "zeta" }, frame["frozenLayers"].AsArray().Select(n => (string)n).ToArray());

            JsonNode linetypes = await bench.Call("list_linetypes");
            string[] ltNames = linetypes.AsArray().Select(r => (string)r["name"]).ToArray();
            Assert.Contains("CENTER", ltNames);
            Assert.Contains("DASHED", ltNames);
            Assert.True(linetypes.AsArray().Single(r => (string)r["name"] == "DASHED")["dashes"].AsArray().Count >= 2);

            JsonNode blocks = await bench.Call("list_blocks");
            Assert.Equal(new[] { "bolt", "Nut" }, blocks.AsArray().Select(r => (string)r["name"]).ToArray());
            Assert.Equal(1, (int)blocks.AsArray().Single(r => (string)r["name"] == "bolt")["entities"]);
            Assert.Equal(1, (int)blocks.AsArray().Single(r => (string)r["name"] == "Nut")["entities"]);
        }

        [Fact]
        public async Task File_and_app_modes_give_identical_rows_for_layouts_linetypes_and_blocks_with_an_xref_loaded()
        {
            string folder = TestFolder.Make("table-list-parity");
            var grid = new DxfDatabase("AC1027");
            var tag = new DxfBlockTableRecord { Name = "TAG" };
            grid.BlockTable.Add(tag);
            tag.AppendEntity(new DxfLine(new GeoPoint3(0, 0, 0), new GeoPoint3(0, 10, 0)));
            grid.Save(Path.Combine(folder, "grid.dxf"));

            var db = new DxfDatabase("AC1027");
            CadTemplate.UseMillimetreSheet(db);
            var dashed = new DxfLinetypeTableRecord
            {
                Name = "Zeta",
                AsciiDescription = "Dash gap",
                NumDashes = 2,
                PatternLength = 15.0,
            };
            dashed.SetDashLengthAt(0, 10.0);
            dashed.SetDashLengthAt(1, -5.0);
            db.LinetypeTable.Add(dashed);
            var nut = new DxfBlockTableRecord { Name = "Nut" };
            db.BlockTable.Add(nut);
            nut.AppendEntity(new DxfLine(new GeoPoint3(0, 0, 0), new GeoPoint3(8, 0, 0)));
            var bolt = new DxfBlockTableRecord { Name = "bolt" };
            db.BlockTable.Add(bolt);
            bolt.AppendEntity(new DxfCircle(new GeoPoint3(0, 0, 0), GeoVector3.ZAxis, 4.0));
            DxfBlockTableRecord xref = db.AttachXref("grid.dxf", "grid");
            db.ModelSpace.AppendEntity(new DxfBlockReference(new GeoPoint3(0, 0, 0), xref));
            string path = Path.Combine(folder, "host.dxf");
            CadDocument saved = CadDocument.From(db);
            CadLayouts.Add(saved, "SheetA", "ISO_A3", true);
            CadLayouts.AddViewport(
                saved,
                "SheetA",
                new[] { 20.0, 20.0, 220.0, 160.0 },
                "1:5",
                new[] { 10.0, 10.0 },
                null
            );
            saved.SaveAs(path);

            using TestApp app = TestApp.Start();
            await using McpHarness attached = await McpHarness.StartAsync(
                new McpOptions { Attach = true, RemoteFolder = app.Folder }
            );
            await attached.CallAsync("open_drawing", ("path", path));
            await using McpHarness file = await McpHarness.StartAsync(new McpOptions { File = path });

            foreach (string tool in new[] { "list_layouts", "list_linetypes", "list_blocks" })
            {
                JsonNode fromFile = await file.CallAsync(tool);
                JsonNode fromApp = await attached.CallAsync(tool);

                Assert.Equal(fromFile.ToJsonString(), fromApp.ToJsonString());
            }

            JsonNode blocks = await attached.CallAsync("list_blocks");
            Assert.Equal(new[] { "bolt", "Nut" }, blocks.AsArray().Select(r => (string)r["name"]).ToArray());
        }
    }
}
