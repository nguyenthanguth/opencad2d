using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json.Nodes;
using System.Threading.Tasks;
using GeometryHelper.Dxf;
using ModelContextProtocol.Protocol;
using SkiaSharp;
using Xunit;
using Xunit.Abstractions;

namespace OpenCAD2D.Mcp.UnitTest
{
    /// <summary>
    /// One scripted session of an agent drawing a steel plate detail from nothing with the tools of the second phase and no command
    /// typed: layers, styles, outline, offset, notch with a fillet and its mirror, a polar array of holes, a hatch, a block with
    /// attributes inserted twice, dimensions, a multileader note, a table, an A3 layout with a title block and two viewports at 1:10
    /// and 1:5, a picture of the sheet and a DWG. In a drawing file and in a drawing app.
    /// </summary>
    public class McpPlateSessionTests
    {
        private readonly ITestOutputHelper _output;

        public McpPlateSessionTests(ITestOutputHelper output)
        {
            _output = output;
        }

        public static IEnumerable<object[]> Modes => new[] { new object[] { "file" }, new object[] { "app" } };

        private static string Handle(JsonNode result, int index = 0) => (string)result["created"][index];

        [Theory]
        [MemberData(nameof(Modes))]
        public async Task An_agent_draws_a_plate_detail_with_a_title_block_and_two_viewports_from_nothing(string mode)
        {
            await using Bench bench = await Bench.Start(mode);
            string folder = TestFolder.Make("plate-" + mode);

            // Layers, linetype and styles.
            await bench.Call("load_linetype", ("name", "DASHED"));
            await bench.Call("add_layer", ("name", "Plate"), ("color", 7), ("lineweight", 50));
            await bench.Call("add_layer", ("name", "Holes"), ("color", 3), ("lineweight", 35));
            await bench.Call("add_layer", ("name", "Hidden"), ("color", 8), ("linetype", "DASHED"), ("lineweight", 18));
            await bench.Call("add_layer", ("name", "Dim"), ("color", 2));
            await bench.Call("add_layer", ("name", "Notes"), ("color", 6));
            await bench.Call("add_layer", ("name", "Title"), ("color", 5));
            await bench.Call("add_text_style", ("name", "NOTE"), ("font", "romans.shx"));
            await bench.Call(
                "add_dimension_style",
                ("name", "PLAN"),
                ("text_style", "NOTE"),
                ("scale", 10.0),
                ("text_height", 2.5),
                ("arrow_size", 2.5)
            );
            await bench.Call("set_current_layer", ("name", "Plate"));

            // The plate: an outline, the same outline offset inside for the weld line, and a hatched pad.
            string outline = await bench.Add(
                "add_polyline",
                (
                    "points",
                    new[] { new[] { 0.0, 0.0 }, new[] { 1000.0, 0.0 }, new[] { 1000.0, 600.0 }, new[] { 0.0, 600.0 } }
                ),
                ("closed", true),
                ("layer", "Plate")
            );
            JsonNode weld = await bench.Call(
                "offset",
                ("handle", outline),
                ("side", new[] { 500.0, 300.0 }),
                ("distance", 40.0)
            );
            await bench.Call("set_properties", ("handles", new[] { Handle(weld) }), ("layer", "Hidden"));
            await bench.Call(
                "add_hatch",
                (
                    "boundary",
                    new[] { new[] { 50.0, 50.0 }, new[] { 250.0, 50.0 }, new[] { 250.0, 150.0 }, new[] { 50.0, 150.0 } }
                ),
                ("pattern", "ANSI31"),
                ("scale", 20.0),
                ("layer", "Plate")
            );

            // A notch with a rounded corner, and its mirror across the middle of the plate.
            string legA = await bench.Add(
                "add_line",
                ("start", new[] { 300.0, 0.0 }),
                ("end", new[] { 300.0, 100.0 }),
                ("layer", "Plate")
            );
            string legB = await bench.Add(
                "add_line",
                ("start", new[] { 300.0, 100.0 }),
                ("end", new[] { 380.0, 100.0 }),
                ("layer", "Plate")
            );
            JsonNode rounded = await bench.Call("fillet", ("first", legA), ("second", legB), ("radius", 30.0));
            JsonNode mirrored = await bench.Call(
                "mirror",
                ("handles", new[] { legA, legB, Handle(rounded) }),
                ("line_start", new[] { 500.0, 0.0 }),
                ("line_end", new[] { 500.0, 600.0 })
            );
            Assert.Equal(3, mirrored["created"].AsArray().Count);

            // Six holes round the middle.
            string hole = await bench.Add(
                "add_circle",
                ("center", new[] { 500.0, 450.0 }),
                ("radius", 30.0),
                ("layer", "Holes")
            );
            JsonNode ring = await bench.Call(
                "array_polar",
                ("handles", new[] { hole }),
                ("center", new[] { 500.0, 300.0 }),
                ("count", 6)
            );
            Assert.Equal(5, ring["created"].AsArray().Count);

            // A tag block with an attribute, inserted twice with its own words.
            string frame = await bench.Add(
                "add_rectangle",
                ("corner1", new[] { 2000.0, 0.0 }),
                ("corner2", new[] { 2200.0, 80.0 }),
                ("layer", "Notes")
            );
            string number = await bench.Add(
                "add_attribute_definition",
                ("tag", "NUMBER"),
                ("at", new[] { 2020.0, 25.0 }),
                ("height", 30.0),
                ("text_style", "NOTE"),
                ("default_value", "0"),
                ("layer", "Notes")
            );
            await bench.Call(
                "define_block",
                ("name", "TAG"),
                ("base_point", new[] { 2000.0, 0.0 }),
                ("handles", new[] { frame, number }),
                ("source", "delete")
            );
            await bench.Call(
                "insert_block_with_attributes",
                ("name", "TAG"),
                ("at", new[] { 60.0, 420.0 }),
                ("values", new Dictionary<string, string> { ["NUMBER"] = "A-101" }),
                ("layer", "Notes")
            );
            await bench.Call(
                "insert_block_with_attributes",
                ("name", "TAG"),
                ("at", new[] { 60.0, 500.0 }),
                ("values", new Dictionary<string, string> { ["NUMBER"] = "A-102" }),
                ("layer", "Notes")
            );

            // Dimensions and a note, placed in the clear.
            await bench.Call(
                "add_linear_dim",
                ("first", new[] { 0.0, 0.0 }),
                ("second", new[] { 1000.0, 0.0 }),
                ("location", new[] { 500.0, -100.0 }),
                ("dim_style", "PLAN"),
                ("layer", "Dim")
            );
            await bench.Call(
                "add_linear_dim",
                ("first", new[] { 1000.0, 0.0 }),
                ("second", new[] { 1000.0, 600.0 }),
                ("location", new[] { 1100.0, 300.0 }),
                ("dim_style", "PLAN"),
                ("layer", "Dim")
            );
            await bench.Call(
                "add_diameter_dim",
                ("center", new[] { 500.0, 450.0 }),
                ("radius", 30.0),
                ("location", new[] { 545.0, 468.0 }),
                ("dim_style", "PLAN"),
                ("layer", "Dim")
            );
            await bench.Call(
                "add_mleader",
                ("arrow", new[] { 700.0, 50.0 }),
                ("landing", new[] { 740.0, -45.0 }),
                ("text", "NOTCH"),
                ("scale", 10.0),
                ("layer", "Notes")
            );
            await bench.Call(
                "add_table",
                ("at", new[] { 1300.0, 600.0 }),
                ("columns", 3),
                ("rows", 2),
                (
                    "cells",
                    new[]
                    {
                        new[] { "PARTS" },
                        new[] { "NO", "NAME", "QTY" },
                        new[] { "1", "PLATE", "1" },
                        new[] { "2", "TAG", "2" },
                    }
                ),
                ("column_width", 150.0),
                ("layer", "Notes")
            );

            JsonNode overlaps = await bench.Call("overlaps");
            Assert.True((int)overlaps["total"] == 0, overlaps.ToJsonString());
            Assert.Contains((JsonArray)await bench.Call("list_blocks"), b => (string)b["name"] == "TAG");

            // The sheet: A3 in millimetres, a title block with its words, two viewports at 1:10 and 1:5.
            await bench.Call("create_layout", ("name", "Sheet1"), ("paper", "ISO A3"));
            string sheetFrame = await bench.Add(
                "add_rectangle",
                ("corner1", new[] { 5.0, 5.0 }),
                ("corner2", new[] { 415.0, 292.0 }),
                ("layer", "Title"),
                ("layout", "Sheet1")
            );
            string block = await bench.Add(
                "add_rectangle",
                ("corner1", new[] { 260.0, 5.0 }),
                ("corner2", new[] { 415.0, 45.0 }),
                ("layer", "Title"),
                ("layout", "Sheet1")
            );
            string title = await bench.Add(
                "add_attribute_definition",
                ("tag", "TITLE"),
                ("at", new[] { 265.0, 30.0 }),
                ("height", 5.0),
                ("layer", "Title"),
                ("layout", "Sheet1")
            );
            string drawingNumber = await bench.Add(
                "add_attribute_definition",
                ("tag", "NUMBER"),
                ("at", new[] { 265.0, 18.0 }),
                ("height", 5.0),
                ("layer", "Title"),
                ("layout", "Sheet1")
            );
            string scale = await bench.Add(
                "add_attribute_definition",
                ("tag", "SCALE"),
                ("at", new[] { 265.0, 8.0 }),
                ("height", 5.0),
                ("layer", "Title"),
                ("layout", "Sheet1")
            );
            await bench.Call("set_current_layout", ("name", "Sheet1"));
            await bench.Call(
                "define_block",
                ("name", "TITLEBLOCK"),
                ("base_point", new[] { 0.0, 0.0 }),
                ("handles", new[] { block, title, drawingNumber, scale }),
                ("source", "delete")
            );
            string inserted = await bench.Add(
                "insert_block_with_attributes",
                ("name", "TITLEBLOCK"),
                ("at", new[] { 0.0, 0.0 }),
                ("layout", "Sheet1"),
                (
                    "values",
                    new Dictionary<string, string>
                    {
                        ["TITLE"] = "PLATE",
                        ["NUMBER"] = "A-100",
                        ["SCALE"] = "1:10",
                    }
                )
            );
            await bench.Call("set_current_layout", ("name", "Model"));
            JsonNode main = await bench.Call(
                "add_viewport",
                ("layout", "Sheet1"),
                ("frame", new[] { 15.0, 55.0, 255.0, 285.0 }),
                ("scale", 10.0),
                ("center", new[] { 700.0, 300.0 })
            );
            JsonNode detail = await bench.Call(
                "add_viewport",
                ("layout", "Sheet1"),
                ("frame", new[] { 265.0, 55.0, 410.0, 200.0 }),
                ("scale", 5.0),
                ("center", new[] { 500.0, 300.0 }),
                ("freeze_layers", new[] { "Hidden" })
            );
            await bench.Call("lock_viewport", ("handle", (string)main["handles"][0]));

            // What was drawn.
            CadDocument snapshot = McpLayoutTests.Snapshot(bench);
            CadSpace sheet = snapshot.Spaces.Single(s => s.Name == "Sheet1");
            DxfViewport[] viewports = sheet
                .Block.OfType<DxfViewport>()
                .Where(v => !v.IsErased && v.Number != 1)
                .ToArray();
            Assert.Equal(
                new[] { 0.1, 0.2 },
                viewports.Select(v => Math.Round(v.CustomScale, 6)).OrderBy(x => x).ToArray()
            );
            Assert.True(viewports.Single(v => v.Handle == (string)main["handles"][0]).Locked);
            DxfBlockReference titleBlock = sheet
                .Block.OfType<DxfBlockReference>()
                .Single(b => !b.IsErased && b.Handle == inserted);
            Assert.Equal(
                new[] { "NUMBER=A-100", "SCALE=1:10", "TITLE=PLATE" },
                titleBlock
                    .AttributeCollection.Select(a => a.Tag + "=" + a.TextString)
                    .OrderBy(x => x, StringComparer.Ordinal)
                    .ToArray()
            );
            Assert.Equal(
                6,
                snapshot
                    .ModelSpace.Block.OfType<DxfCircle>()
                    .Count(c => !c.IsErased && Math.Abs(c.Radius - 30.0) < 1e-9)
            );
            Assert.Equal(
                2,
                snapshot.ModelSpace.Block.OfType<DxfBlockReference>().Count(b => !b.IsErased && b.Name == "TAG")
            );
            Assert.Equal(2, snapshot.ModelSpace.Block.OfType<DxfArc>().Count(a => !a.IsErased));
            Assert.Single(snapshot.ModelSpace.Block.OfType<DxfTable>(), t => !t.IsErased);
            Assert.Single(snapshot.ModelSpace.Block.OfType<DxfMLeader>(), t => !t.IsErased);
            Assert.Equal(3, snapshot.ModelSpace.Block.OfType<DxfDimension>().Count(d => !d.IsErased));

            // The sheet as a picture, and the drawing saved as DWG.
            CallToolResult picture = await bench.Harness.CallRawAsync(
                "render_png",
                ("layout", "Sheet1"),
                ("width", 1400)
            );
            bench.CountCall();
            byte[] png = Assert.Single(picture.Content.OfType<ImageContentBlock>()).DecodedData.ToArray();
            string pngPath = Path.Combine(folder, "plate-sheet-" + mode + ".png");
            File.WriteAllBytes(pngPath, png);

            // With OPENCAD2D_MCP_PICTURES set to a folder, the picture is kept there: the test's own folder goes with the run.
            string keep = Environment.GetEnvironmentVariable("OPENCAD2D_MCP_PICTURES");

            if (!string.IsNullOrEmpty(keep))
            {
                Directory.CreateDirectory(keep);
                File.Copy(pngPath, Path.Combine(keep, Path.GetFileName(pngPath)), true);
            }

            using (SKBitmap bitmap = SKBitmap.Decode(png))
            {
                int dark = 0;

                for (int y = 0; y < bitmap.Height; y += 2)
                {
                    for (int x = 0; x < bitmap.Width; x += 2)
                    {
                        SKColor c = bitmap.GetPixel(x, y);
                        dark += c.Red + c.Green + c.Blue < 300 ? 1 : 0;
                    }
                }

                Assert.True(dark > 800, "The picture of the sheet is nearly blank: " + dark + " dark pixels.");
            }

            string dwg = Path.Combine(folder, "plate.dwg");
            await bench.Call("save", ("path", dwg));
            Assert.True(new FileInfo(dwg).Length > 1000);
            _output.WriteLine("The sheet of the " + mode + " session as a picture: " + pngPath);
            _output.WriteLine("The plate as a DWG: " + dwg);
            _output.WriteLine("Tool calls of the " + mode + " session: " + bench.Calls);
        }

        [Fact]
        public async Task A_server_attached_read_only_refuses_the_new_writers_and_the_app_is_left_as_it_was()
        {
            await using Bench bench = await Bench.Start("app", readOnly: true);

            Assert.Contains(
                "--read-only",
                await bench.Fail("add_rectangle", ("corner1", new[] { 0.0, 0.0 }), ("corner2", new[] { 5.0, 5.0 }))
            );
            Assert.Contains("--read-only", await bench.Fail("create_layout", ("name", "Sheet1")));
            Assert.Contains("--read-only", await bench.Fail("set_layer", ("name", "0"), ("on", false)));
            Assert.Contains("--read-only", await bench.Fail("fillet", ("first", "1"), ("radius", 1.0)));
            Assert.Empty(await bench.Entities());
            Assert.Single(((JsonArray)(await bench.Call("info"))["layouts"]).AsEnumerable(), l => (string)l == "Model");
        }
    }
}
