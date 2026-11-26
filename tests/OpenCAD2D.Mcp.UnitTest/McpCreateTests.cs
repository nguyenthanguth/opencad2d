using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json.Nodes;
using System.Threading.Tasks;
using GeometryHelper.Dxf;
using GeometryHelper.Geometry;
using Xunit;

namespace OpenCAD2D.Mcp.UnitTest
{
    /// <summary>The tools that create shapes, styles, blocks, notes and tables, and the edits of text, look and layers, in both modes.</summary>
    public class McpCreateTests
    {
        public static IEnumerable<object[]> Modes => new[] { new object[] { "file" }, new object[] { "app" } };

        private static void Near(double expected, double actual) => Assert.Equal(expected, actual, 3);

        /// <summary>The entity of a handle as the drawing holds it, in either mode.</summary>
        private static T Entity<T>(Bench bench, string handle, string layout = "Model")
            where T : DxfEntity =>
            (T)
                bench
                    .Harness.Session.Backend.Entities(layout)
                    .Single(e => string.Equals(e.Handle, handle, StringComparison.OrdinalIgnoreCase))
                    .Entity;

        [Theory]
        [MemberData(nameof(Modes))]
        public async Task Add_rectangle_with_a_fillet_or_a_chamfer_makes_one_closed_polyline(string mode)
        {
            await using Bench bench = await Bench.Start(mode);

            string plain = await bench.Add(
                "add_rectangle",
                ("corner1", new[] { 0.0, 0.0 }),
                ("corner2", new[] { 40.0, 20.0 })
            );
            string rounded = await bench.Add(
                "add_rectangle",
                ("corner1", new[] { 100.0, 0.0 }),
                ("corner2", new[] { 140.0, 20.0 }),
                ("fillet", 5.0)
            );
            string cut = await bench.Add(
                "add_rectangle",
                ("corner1", new[] { 200.0, 0.0 }),
                ("corner2", new[] { 240.0, 20.0 }),
                ("chamfer", 4.0)
            );

            Assert.Equal(4, Entity<DxfPolyline>(bench, plain).NumberOfVertices);
            DxfPolyline round = Entity<DxfPolyline>(bench, rounded);
            Assert.Equal(8, round.NumberOfVertices);
            Assert.True(round.Closed);
            Near(Math.Tan(Math.PI / 8.0), round.GetBulgeAt(1));
            Assert.Equal(0.0, Entity<DxfPolyline>(bench, cut).GetBulgeAt(1));
            Assert.Contains(
                "not both",
                await bench.Fail(
                    "add_rectangle",
                    ("corner1", new[] { 0.0, 0.0 }),
                    ("corner2", new[] { 40.0, 20.0 }),
                    ("fillet", 2.0),
                    ("chamfer", 2.0)
                )
            );
            Assert.Contains(
                "too big",
                await bench.Fail(
                    "add_rectangle",
                    ("corner1", new[] { 0.0, 0.0 }),
                    ("corner2", new[] { 40.0, 20.0 }),
                    ("fillet", 15.0)
                )
            );
        }

        [Theory]
        [MemberData(nameof(Modes))]
        public async Task The_shape_tools_each_add_the_kind_they_name(string mode)
        {
            await using Bench bench = await Bench.Start(mode);

            string polygon = await bench.Add(
                "add_polygon",
                ("center", new[] { 0.0, 0.0 }),
                ("radius", 10.0),
                ("sides", 6)
            );
            string ellipse = await bench.Add(
                "add_ellipse",
                ("center", new[] { 50.0, 0.0 }),
                ("major_axis_end", new[] { 70.0, 0.0 }),
                ("ratio", 0.5)
            );
            string fit = await bench.Add(
                "add_spline",
                ("fit_points", new[] { new[] { 0.0, 50.0 }, new[] { 20.0, 60.0 }, new[] { 40.0, 50.0 } })
            );
            string control = await bench.Add(
                "add_spline",
                (
                    "control_points",
                    new[] { new[] { 0.0, 80.0 }, new[] { 20.0, 100.0 }, new[] { 40.0, 80.0 }, new[] { 60.0, 100.0 } }
                )
            );
            string point = await bench.Add("add_point", ("at", new[] { 5.0, 5.0 }));
            string donut = await bench.Add(
                "add_donut",
                ("center", new[] { 100.0, 100.0 }),
                ("inner_diameter", 4.0),
                ("outer_diameter", 10.0)
            );
            string xline = await bench.Add("add_xline", ("through", new[] { 0.0, 0.0 }), ("angle", 45.0));
            string ray = await bench.Add("add_ray", ("start", new[] { 0.0, 0.0 }), ("angle", 90.0));
            string leader = await bench.Add(
                "add_leader",
                ("points", new[] { new[] { 0.0, 0.0 }, new[] { 10.0, 10.0 }, new[] { 30.0, 10.0 } })
            );

            DxfPolyline hexagon = Entity<DxfPolyline>(bench, polygon);
            Assert.Equal(6, hexagon.NumberOfVertices);
            Near(10.0, hexagon.GetPoint2dAt(0).X);
            Near(0.5, Entity<DxfEllipse>(bench, ellipse).RadiusRatio);
            Assert.IsType<DxfSpline>(Entity<DxfEntity>(bench, fit));
            Assert.IsType<DxfSpline>(Entity<DxfEntity>(bench, control));
            Assert.IsType<DxfDBPoint>(Entity<DxfEntity>(bench, point));
            DxfPolyline ring = Entity<DxfPolyline>(bench, donut);
            Assert.Equal(2, ring.NumberOfVertices);
            Near(3.0, ring.GetStartWidthAt(0));
            Assert.IsType<DxfXline>(Entity<DxfEntity>(bench, xline));
            Near(1.0, Entity<DxfRay>(bench, ray).UnitDir.Y);
            Assert.Equal(3, Entity<DxfLeader>(bench, leader).NumVertices);
            Assert.Contains("two", await bench.Fail("add_spline", ("fit_points", new[] { new[] { 0.0, 0.0 } })));
        }

        [Theory]
        [MemberData(nameof(Modes))]
        public async Task Edit_text_changes_the_words_height_rotation_style_and_justification(string mode)
        {
            await using Bench bench = await Bench.Start(mode);
            await bench.Call("add_text_style", ("name", "NOTE"), ("font", "romans.shx"), ("width_factor", 0.8));
            string text = await bench.Add("add_text", ("text", "OLD"), ("at", new[] { 0.0, 0.0 }), ("height", 5.0));
            string mtext = await bench.Add(
                "add_mtext",
                ("text", "OLDER"),
                ("at", new[] { 0.0, 30.0 }),
                ("height", 4.0),
                ("width", 60.0)
            );

            await bench.Call(
                "edit_text",
                ("handle", text),
                ("contents", "NEW"),
                ("height", 7.0),
                ("rotation", 90.0),
                ("style", "NOTE"),
                ("justify", "MiddleCenter")
            );
            await bench.Call("edit_text", ("handle", mtext), ("contents", "NEWER"), ("height", 6.0));

            DxfDBText changed = Entity<DxfDBText>(bench, text);
            Assert.Equal("NEW", changed.TextString);
            Near(7.0, changed.Height);
            Near(Math.PI / 2.0, changed.Rotation);
            Assert.Equal("NOTE", changed.TextStyleName);
            Assert.Equal(DxfAttachmentPoint.MiddleCenter, changed.Justify);
            DxfMText multi = Entity<DxfMText>(bench, mtext);
            Assert.Contains("NEWER", multi.Contents);
            Near(6.0, multi.TextHeight);
            Assert.Contains("TopLeft", await bench.Fail("edit_text", ("handle", text), ("justify", "sideways")));
            Assert.Contains("no text style", await bench.Fail("edit_text", ("handle", text), ("style", "NOPE")));
            await bench.Call("undo");
            Assert.Equal("NEW", Entity<DxfDBText>(bench, text).TextString);
        }

        [Theory]
        [MemberData(nameof(Modes))]
        public async Task Edit_dimension_sets_the_text_and_an_override_and_the_dimension_is_drawn_again(string mode)
        {
            await using Bench bench = await Bench.Start(mode);
            string dim = await bench.Add(
                "add_linear_dim",
                ("first", new[] { 0.0, 0.0 }),
                ("second", new[] { 100.0, 0.0 }),
                ("location", new[] { 50.0, -20.0 })
            );

            await bench.Call(
                "edit_dimension",
                ("handle", dim),
                ("text", "<> TYP"),
                ("overrides", new Dictionary<string, string> { ["Text height"] = "4" })
            );

            DxfDimension dimension = Entity<DxfDimension>(bench, dim);
            Assert.Equal("<> TYP", dimension.DimensionText);
            Near(4.0, dimension.Dimtxt);
            Assert.Contains("Give", await bench.Fail("edit_dimension", ("handle", dim)));
        }

        [Theory]
        [MemberData(nameof(Modes))]
        public async Task Match_properties_and_set_properties_set_the_look_of_objects(string mode)
        {
            await using Bench bench = await Bench.Start(mode);
            await bench.Call("add_layer", ("name", "Steel"), ("color", 1));
            string source = await bench.Add(
                "add_line",
                ("start", new[] { 0.0, 0.0 }),
                ("end", new[] { 10.0, 0.0 }),
                ("layer", "Steel"),
                ("color", 3)
            );
            string target = await bench.Add("add_line", ("start", new[] { 0.0, 10.0 }), ("end", new[] { 10.0, 10.0 }));

            await bench.Call(
                "set_properties",
                ("handles", new[] { source }),
                ("lineweight", 50),
                ("linetype_scale", 2.5),
                ("transparency", 40)
            );
            DxfLine styled = Entity<DxfLine>(bench, source);
            Assert.Equal(DxfLineWeight.LineWeight050, styled.LineWeight);
            Near(2.5, styled.LinetypeScale);
            Assert.Equal(153, styled.Transparency.Alpha);
            await bench.Call(
                "match_properties",
                ("source", source),
                ("targets", new[] { target }),
                ("which", new[] { "layer", "color", "lineweight" })
            );

            DxfLine matched = Entity<DxfLine>(bench, target);
            Assert.Equal("Steel", matched.Layer);
            Assert.Equal(3, matched.ColorIndex);
            Assert.Equal(DxfLineWeight.LineWeight050, matched.LineWeight);
            Near(1.0, matched.LinetypeScale);
            Assert.Contains(
                "which holds",
                await bench.Fail(
                    "match_properties",
                    ("source", source),
                    ("targets", new[] { target }),
                    ("which", new[] { "mood" })
                )
            );
            Assert.Contains(
                "none of AutoCAD's",
                await bench.Fail("set_properties", ("handles", new[] { source }), ("lineweight", 77))
            );
        }

        [Theory]
        [MemberData(nameof(Modes))]
        public async Task The_layer_tools_change_rename_and_delete_layers(string mode)
        {
            await using Bench bench = await Bench.Start(mode);
            await bench.Call("load_linetype", ("name", "hidden"));
            await bench.Call("add_layer", ("name", "Steel"), ("color", 1));
            await bench.Call("add_layer", ("name", "Spare"), ("color", 2));
            string line = await bench.Add(
                "add_line",
                ("start", new[] { 0.0, 0.0 }),
                ("end", new[] { 10.0, 0.0 }),
                ("layer", "Steel")
            );

            JsonNode look = await bench.Call(
                "set_layer",
                ("name", "steel"),
                ("color", 5),
                ("linetype", "HIDDEN"),
                ("lineweight", 35),
                ("plot", false),
                ("locked", true)
            );
            Assert.Equal(5, (int)look["color"]);
            Assert.Equal("HIDDEN", (string)look["linetype"]);
            Assert.Equal(35, (int)look["lineWeight"]);
            Assert.False((bool)look["plottable"]);
            Assert.True((bool)look["locked"]);
            await bench.Call("set_layer", ("name", "Steel"), ("locked", false));

            JsonNode renamed = await bench.Call("rename_layer", ("name", "Steel"), ("new_name", "Plate"));
            Assert.Equal("Plate", (string)renamed["name"]);
            Assert.Equal("Plate", (string)(await bench.Entities("LINE")).Single()["layer"]);
            Assert.Contains(
                "already exists",
                await bench.Fail("rename_layer", ("name", "Plate"), ("new_name", "Spare"))
            );

            Assert.Contains("objects are on it", await bench.Fail("delete_layer", ("name", "Plate")));
            await bench.Call("delete_layer", ("name", "Plate"), ("move_to", "Spare"));
            Assert.Equal("Spare", (string)(await bench.Entities("LINE")).Single()["layer"]);
            Assert.DoesNotContain((await bench.Call("list_layers")).AsArray(), l => (string)l["name"] == "Plate");
            JsonNode current = await bench.Call("set_current_layer", ("name", "Spare"));
            Assert.Equal("Spare", (string)(await bench.Call("info"))["currentLayer"]);
            Assert.Contains("no layer", await bench.Fail("set_current_layer", ("name", "Nope")));
            Assert.Equal("Spare", (string)current["name"]);
            Assert.Equal(line, (string)(await bench.Entities("LINE")).Single()["handle"]);
        }

        [Theory]
        [MemberData(nameof(Modes))]
        public async Task Styles_and_linetypes_are_added_once_and_used_by_what_follows(string mode)
        {
            await using Bench bench = await Bench.Start(mode);

            await bench.Call(
                "add_text_style",
                ("name", "NOTE"),
                ("font", "romans.shx"),
                ("width_factor", 0.8),
                ("height", 0.0)
            );
            await bench.Call(
                "add_dimension_style",
                ("name", "PLAN"),
                ("text_style", "NOTE"),
                ("scale", 50.0),
                ("text_height", 3.5),
                ("arrow_size", 3.0),
                ("variables", new Dictionary<string, double> { ["Dimdec"] = 1 })
            );
            JsonNode loaded = await bench.Call("load_linetype", ("name", "dashed"));
            Assert.Equal("DASHED", (string)loaded["name"]);
            Assert.Equal("DASHED", (string)(await bench.Call("load_linetype", ("name", "DASHED")))["name"]);

            JsonNode styles = await bench.Call("list_text_styles");
            JsonNode note = styles.AsArray().Single(s => (string)s["name"] == "NOTE");
            Near(0.8, (double)note["widthFactor"]);
            JsonNode plan = (await bench.Call("list_dimension_styles"))
                .AsArray()
                .Single(s => (string)s["name"] == "PLAN");
            Near(50.0, (double)plan["scale"]);
            Near(3.5, (double)plan["textHeight"]);
            Assert.Equal("NOTE", (string)plan["textStyle"]);
            await bench.Call("add_layer", ("name", "Hidden"), ("linetype", "DASHED"));
            string text = await bench.Add(
                "add_text",
                ("text", "Plate"),
                ("at", new[] { 0.0, 0.0 }),
                ("height", 5.0),
                ("text_style", "NOTE")
            );
            Assert.Equal("NOTE", Entity<DxfDBText>(bench, text).TextStyleName);

            Assert.Contains("already exists", await bench.Fail("add_text_style", ("name", "note")));
            Assert.Contains("already exists", await bench.Fail("add_dimension_style", ("name", "Plan")));
            Assert.Contains("built-in", await bench.Fail("load_linetype", ("name", "WAVY")));
            Assert.Contains(
                "no dimension variable",
                await bench.Fail(
                    "add_dimension_style",
                    ("name", "BAD"),
                    ("variables", new Dictionary<string, double> { ["Nope"] = 1 })
                )
            );
        }

        [Theory]
        [MemberData(nameof(Modes))]
        public async Task A_block_with_attributes_is_defined_and_inserted_twice_with_its_own_words(string mode)
        {
            await using Bench bench = await Bench.Start(mode);
            string frame = await bench.Add(
                "add_rectangle",
                ("corner1", new[] { 0.0, 0.0 }),
                ("corner2", new[] { 60.0, 20.0 })
            );
            string title = await bench.Add(
                "add_attribute_definition",
                ("tag", "TITLE"),
                ("at", new[] { 2.0, 12.0 }),
                ("height", 4.0),
                ("prompt", "Title?"),
                ("default_value", "UNTITLED")
            );
            string number = await bench.Add(
                "add_attribute_definition",
                ("tag", "NUMBER"),
                ("at", new[] { 2.0, 3.0 }),
                ("height", 4.0),
                ("default_value", "0")
            );

            JsonNode defined = await bench.Call(
                "define_block",
                ("name", "TB"),
                ("base_point", new[] { 0.0, 0.0 }),
                ("handles", new[] { frame, title, number }),
                ("source", "delete")
            );

            Assert.Equal(3, (int)defined["entities"]);
            Assert.Null(defined["insert"]);
            Assert.Equal(0, await bench.Count());
            Assert.Contains(((JsonArray)await bench.Call("list_blocks")), b => (string)b["name"] == "TB");
            string first = await bench.Add(
                "insert_block_with_attributes",
                ("name", "TB"),
                ("at", new[] { 100.0, 0.0 }),
                ("values", new Dictionary<string, string> { ["TITLE"] = "Plate", ["number"] = "A-101" })
            );
            string second = await bench.Add(
                "insert_block_with_attributes",
                ("name", "TB"),
                ("at", new[] { 200.0, 0.0 })
            );

            DxfBlockReference one = Entity<DxfBlockReference>(bench, first);
            Assert.Equal(
                new[] { "NUMBER=A-101", "TITLE=Plate" },
                one.AttributeCollection.Select(a => a.Tag + "=" + a.TextString).OrderBy(x => x).ToArray()
            );
            Near(102.0, one.AttributeCollection.Single(a => a.Tag == "TITLE").Position.X);
            DxfBlockReference two = Entity<DxfBlockReference>(bench, second);
            Assert.Equal("UNTITLED", two.AttributeCollection.Single(a => a.Tag == "TITLE").TextString);

            await bench.Call("set_attribute", ("handle", second), ("tag", "NUMBER"), ("value", "B-7"));
            Assert.Equal(
                "B-7",
                Entity<DxfBlockReference>(bench, second).AttributeCollection.Single(a => a.Tag == "NUMBER").TextString
            );
            Assert.Contains(
                "has no attribute",
                await bench.Fail("set_attribute", ("handle", second), ("tag", "NOPE"), ("value", "x"))
            );
            Assert.Contains(
                "has no attribute",
                await bench.Fail(
                    "insert_block_with_attributes",
                    ("name", "TB"),
                    ("at", new[] { 0.0, 0.0 }),
                    ("values", new Dictionary<string, string> { ["NOPE"] = "x" })
                )
            );
            Assert.Contains(
                "already exists",
                await bench.Fail(
                    "define_block",
                    ("name", "tb"),
                    ("base_point", new[] { 0.0, 0.0 }),
                    ("handles", new[] { first })
                )
            );
        }

        [Theory]
        [MemberData(nameof(Modes))]
        public async Task Define_block_converts_the_objects_into_one_insert_or_keeps_them(string mode)
        {
            await using Bench bench = await Bench.Start(mode);
            string a = await bench.Add("add_circle", ("center", new[] { 5.0, 5.0 }), ("radius", 3.0));
            string b = await bench.Add("add_line", ("start", new[] { 0.0, 0.0 }), ("end", new[] { 10.0, 0.0 }));

            JsonNode kept = await bench.Call(
                "define_block",
                ("name", "KEEP"),
                ("base_point", new[] { 0.0, 0.0 }),
                ("handles", new[] { a, b }),
                ("source", "keep")
            );
            Assert.Null(kept["insert"]);
            Assert.Equal(2, await bench.Count());

            JsonNode converted = await bench.Call(
                "define_block",
                ("name", "ONE"),
                ("base_point", new[] { 5.0, 5.0 }),
                ("handles", new[] { a, b })
            );

            string insert = (string)converted["insert"];
            Assert.NotNull(insert);
            Assert.Single(await bench.Entities());
            Assert.Equal("ONE", Entity<DxfBlockReference>(bench, insert).Name);
            await bench.Call("undo");
            Assert.Equal(2, await bench.Count());
        }

        [Theory]
        [MemberData(nameof(Modes))]
        public async Task Add_mleader_and_add_table_make_a_note_and_a_filled_table(string mode)
        {
            await using Bench bench = await Bench.Start(mode);

            double scaleBefore = Convert.ToDouble(bench.Harness.Session.Backend.Variable("MLEADERSCALE"));
            string note = await bench.Add(
                "add_mleader",
                ("arrow", new[] { 10.0, 10.0 }),
                ("landing", new[] { 40.0, 40.0 }),
                ("text", "NOTE"),
                ("scale", 10.0)
            );
            string table = await bench.Add(
                "add_table",
                ("at", new[] { 100.0, 100.0 }),
                ("columns", 3),
                ("rows", 2),
                (
                    "cells",
                    new[]
                    {
                        new[] { "PARTS" },
                        new[] { "NO", "NAME", "QTY" },
                        new[] { "1", "BASE", "2" },
                        new[] { "2", "TOP", "4" },
                    }
                ),
                ("column_width", 30.0)
            );

            Assert.Equal("NOTE", Entity<DxfMLeader>(bench, note).MText.Contents);
            Near(10.0 * 2.5, Entity<DxfMLeader>(bench, note).MText.TextHeight);
            Near(scaleBefore, Convert.ToDouble(bench.Harness.Session.Backend.Variable("MLEADERSCALE")));
            DxfTable made = Entity<DxfTable>(bench, table);
            Assert.Equal(4, made.NumRows);
            Assert.Equal(3, made.NumColumns);
            Assert.Equal("BASE", CellText(made, 2, 1));
            Assert.Equal("QTY", CellText(made, 1, 2));
            await bench.Call("undo");
            Assert.Empty(await bench.Entities("ACAD_TABLE"));
            Assert.Contains(
                "do not fit",
                await bench.Fail(
                    "add_table",
                    ("at", new[] { 0.0, 0.0 }),
                    ("columns", 2),
                    ("rows", 1),
                    ("cells", new[] { new[] { "a", "b", "c" } })
                )
            );
        }

        [Fact]
        public async Task The_words_of_a_note_an_attribute_and_a_layout_name_may_hold_spaces_in_a_drawing_file()
        {
            // The command line of a drawing file takes a line at a prompt for words whole. (The test server of the app types a
            // line word by word, so this is not run against it; the app's own window takes the line whole.)
            await using Bench bench = await Bench.Start("file");
            string frame = await bench.Add(
                "add_rectangle",
                ("corner1", new[] { 0.0, 0.0 }),
                ("corner2", new[] { 60.0, 20.0 })
            );
            string title = await bench.Add(
                "add_attribute_definition",
                ("tag", "TITLE"),
                ("at", new[] { 2.0, 5.0 }),
                ("height", 4.0)
            );
            await bench.Call(
                "define_block",
                ("name", "TB"),
                ("base_point", new[] { 0.0, 0.0 }),
                ("handles", new[] { frame, title }),
                ("source", "delete")
            );

            string note = await bench.Add(
                "add_mleader",
                ("arrow", new[] { 0.0, 0.0 }),
                ("landing", new[] { 30.0, 30.0 }),
                ("text", "STEEL PLATE 10 THK")
            );
            string tag = await bench.Add(
                "insert_block_with_attributes",
                ("name", "TB"),
                ("at", new[] { 100.0, 0.0 }),
                ("values", new Dictionary<string, string> { ["TITLE"] = "MAIN FRAME A-1" })
            );
            JsonNode layout = await bench.Call("create_layout", ("name", "A3 Plan"), ("paper", "ISO A3"));

            Assert.Equal("STEEL PLATE 10 THK", Entity<DxfMLeader>(bench, note).MText.Contents);
            Assert.Equal(
                "MAIN FRAME A-1",
                Entity<DxfBlockReference>(bench, tag).AttributeCollection.Single().TextString
            );
            Assert.Equal("A3 Plan", (string)layout["name"]);
        }

        private static string CellText(DxfTable table, int row, int column) => table.GetTextString(row, column, 0);
    }
}
