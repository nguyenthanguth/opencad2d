using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json.Nodes;
using System.Threading.Tasks;
using GeometryHelper.Dxf;
using GeometryHelper.Geometry;
using OpenCAD2D.Remote;
using Xunit;

namespace OpenCAD2D.Mcp.UnitTest
{
    /// <summary>The faults an independent review of the MCP server found: names typed on a command line, and what the server does to a running command.</summary>
    public class McpReviewTests
    {
        /// <summary>A name that, typed on a command line, would set FILLETRAD to the value of ATTREQ (1) as AutoLISP.</summary>
        private const string LispName = "(setvar'FILLETRAD(getvar'ATTREQ))";

        public static IEnumerable<object[]> Modes => new[] { new object[] { "file" }, new object[] { "app" } };

        public static IEnumerable<object[]> ModesAndNames =>
            from mode in new[] { "file", "app" }
            from name in new[] { LispName, "!FILLETRAD", "'ZOOM", "A(B)" }
            select new object[] { mode, name };

        private static double Radius(Bench bench) =>
            Convert.ToDouble(bench.Harness.Session.Backend.Variable("FILLETRAD"));

        private static T Entity<T>(Bench bench, string handle)
            where T : DxfEntity =>
            (T)
                bench
                    .Harness.Session.Backend.Entities("Model")
                    .Single(e => string.Equals(e.Handle, handle, StringComparison.OrdinalIgnoreCase))
                    .Entity;

        [Theory]
        [InlineData("(x)")]
        [InlineData("!x")]
        [InlineData("'x")]
        [InlineData("(")]
        public void A_word_of_a_command_never_starts_like_lisp_or_a_transparent_command(string word)
        {
            Assert.Throws<ArgumentException>(() => new CommandScript().Word(word));
            Assert.Throws<ArgumentException>(() => new CommandScript().Word("-INSERT").Word(word));
        }

        [Theory]
        [InlineData("-INSERT")]
        [InlineData("@")]
        [InlineData("A(B)")]
        [InlineData("-5")]
        [InlineData("0,0")]
        public void A_word_with_a_bracket_or_a_mark_inside_is_still_a_word(string word)
        {
            Assert.Single(new CommandScript().Word(word).Steps);
        }

        [Theory]
        [InlineData("a\0b")]
        [InlineData("a\bb")]
        [InlineData("a\tb")]
        public void The_words_of_a_note_hold_no_control_character(string text)
        {
            Assert.Throws<ArgumentException>(() => new CommandScript().Text(text));
        }

        [Theory]
        [MemberData(nameof(ModesAndNames))]
        public async Task A_block_named_like_lisp_is_defined_and_inserted_by_name_and_no_lisp_runs(
            string mode,
            string name
        )
        {
            await using Bench bench = await Bench.Start(mode);
            double before = Radius(bench);
            string line = await bench.Add("add_line", ("start", new[] { 0.0, 0.0 }), ("end", new[] { 10.0, 0.0 }));

            JsonNode defined = await bench.Call(
                "define_block",
                ("name", name),
                ("base_point", new[] { 0.0, 0.0 }),
                ("handles", new[] { line })
            );
            string again = await bench.Add("insert_block_with_attributes", ("name", name), ("at", new[] { 50.0, 0.0 }));

            Assert.Equal(name, (string)defined["name"]);
            Assert.NotNull((string)defined["insert"]);
            Assert.Equal(name, Entity<DxfBlockReference>(bench, again).Name);
            Assert.Equal(before, Radius(bench), 6);
        }

        [Fact]
        public async Task A_layout_named_like_lisp_and_a_note_of_the_same_words_never_run_lisp()
        {
            await using Bench bench = await Bench.Start("file");
            double before = Radius(bench);

            await bench.Harness.CallRawAsync("create_layout", ("name", LispName));
            await bench.Harness.CallRawAsync(
                "add_mleader",
                ("arrow", new[] { 0.0, 0.0 }),
                ("landing", new[] { 30.0, 30.0 }),
                ("text", LispName)
            );

            Assert.Equal(before, Radius(bench), 6);
        }

        /// <summary>Brings a block of a line and the attribute TITLE (default D) into the drawing, one that takes one scale where asked.</summary>
        private static void ProvideBlock(Bench bench, string name, bool uniform)
        {
            var records = new DxfDatabase("AC1027");
            var block = new DxfBlockTableRecord
            {
                Name = name,
                BlockScaling = uniform ? DxfBlockScaling.Uniform : DxfBlockScaling.Any,
            };
            records.BlockTable.Add(block);
            block.AppendEntity(new DxfLine(new GeoPoint3(0, 0, 0), new GeoPoint3(10, 0, 0)));
            block.AppendEntity(
                new DxfAttributeDefinition(new GeoPoint3(1, 1, 0), "D", "TITLE", "Title?", null) { Height = 2.0 }
            );
            ((IDrawingEditing)bench.Harness.Session.Backend).Apply(
                records,
                Array.Empty<DxfEntity>(),
                null,
                Array.Empty<string>()
            );
        }

        [Theory]
        [InlineData("file", true, null)]
        [InlineData("app", true, null)]
        [InlineData("file", false, null)]
        [InlineData("app", false, null)]
        [InlineData("file", true, 0)]
        [InlineData("app", true, 0)]
        [InlineData("file", false, 0)]
        [InlineData("app", false, 0)]
        public async Task A_block_is_inserted_at_its_place_scale_and_turn_with_its_words_whatever_it_asks_for(
            string mode,
            bool uniform,
            int? attreq
        )
        {
            await using Bench bench = await Bench.Start(mode);
            ProvideBlock(bench, "TB", uniform);

            if (attreq.HasValue)
            {
                ((IDrawingEditing)bench.Harness.Session.Backend).SetVariable("ATTREQ", attreq.Value);
            }

            string handle = await bench.Add(
                "insert_block_with_attributes",
                ("name", "TB"),
                ("at", new[] { 100.0, 20.0 }),
                ("values", new Dictionary<string, string> { ["TITLE"] = "MAIN FRAME A-1" }),
                ("scale", 2.0),
                ("rotation", 30.0)
            );

            DxfBlockReference insert = Entity<DxfBlockReference>(bench, handle);
            Assert.Single(await bench.Entities());
            Assert.Equal(100.0, insert.Position.X, 6);
            Assert.Equal(20.0, insert.Position.Y, 6);
            Assert.Equal(2.0, insert.ScaleFactors.X, 6);
            Assert.Equal(2.0, insert.ScaleFactors.Y, 6);
            Assert.Equal(30.0 * Math.PI / 180.0, insert.Rotation, 6);
            Assert.Equal("MAIN FRAME A-1", insert.AttributeCollection.Single().TextString);
        }

        [Fact]
        public async Task A_block_is_inserted_without_asking_the_window_for_the_edit_attributes_dialog()
        {
            await using Bench bench = await Bench.Start("file");
            ProvideBlock(bench, "TB", false);
            ((IDrawingEditing)bench.Harness.Session.Backend).SetVariable("ATTDIA", 1);
            var asked = new List<string>();
            ((LocalBackend)bench.Harness.Session.Backend).Session.UiRequested += (s, e) => asked.Add(e.Name);

            string handle = await bench.Add(
                "insert_block_with_attributes",
                ("name", "TB"),
                ("at", new[] { 0.0, 0.0 }),
                ("values", new Dictionary<string, string> { ["TITLE"] = "X" })
            );

            Assert.Empty(asked);
            Assert.Equal("X", Entity<DxfBlockReference>(bench, handle).AttributeCollection.Single().TextString);
        }

        [Fact]
        public async Task A_tool_that_types_is_refused_while_the_user_has_a_command_running_and_takes_none_of_the_users_work()
        {
            await using Bench bench = await Bench.Start("app", scripts: true);
            string a = await bench.Add("add_line", ("start", new[] { 0.0, 0.0 }), ("end", new[] { 100.0, 0.0 }));
            string b = await bench.Add("add_line", ("start", new[] { 50.0, -50.0 }), ("end", new[] { 50.0, 50.0 }));
            ProvideBlock(bench, "TB", false);
            await bench.Call("create_layout", ("name", "Sheet1"));
            int before = await bench.Count("LINE");

            // The user's LINE has drawn two segments and waits for the next point.
            JsonNode typed = await bench.Call("run_command", ("text", "LINE\n0,200\n10,200\n20,200\n"));
            Assert.Contains("still waiting", (string)typed["output"]);
            Assert.Equal(before + 2, await bench.Count("LINE"));

            double[] p = { 0.0, 0.0 };
            var tools = new (string, (string, object)[])[]
            {
                (
                    "add_mleader",
                    new (string, object)[] { ("arrow", p), ("landing", new[] { 5.0, 5.0 }), ("text", "x") }
                ),
                ("add_table", new (string, object)[] { ("at", p), ("columns", 2), ("rows", 2) }),
                ("insert_block_with_attributes", new (string, object)[] { ("name", "TB"), ("at", p) }),
                (
                    "define_block",
                    new (string, object)[] { ("name", "NB"), ("base_point", p), ("handles", new[] { a }) }
                ),
                ("create_layout", new (string, object)[] { ("name", "Sheet2") }),
                (
                    "add_viewport",
                    new (string, object)[]
                    {
                        ("layout", "Sheet1"),
                        ("frame", new[] { 0.0, 0.0, 50.0, 50.0 }),
                        ("scale", 1.0),
                    }
                ),
                (
                    "copy",
                    new (string, object)[] { ("handles", new[] { a }), ("displacements", new[] { new[] { 1.0, 1.0 } }) }
                ),
                ("explode", new (string, object)[] { ("handles", new[] { a }) }),
                ("undo", new (string, object)[0]),
                ("redo", new (string, object)[0]),
            };

            foreach ((string tool, (string, object)[] arguments) in tools)
            {
                string message = await bench.Fail(tool, arguments);
                Assert.True(
                    message.Contains("command is running") || message.Contains("is busy with the command"),
                    tool + " said: " + message
                );
            }

            Assert.Equal(before + 2, await bench.Count("LINE"));
            Assert.DoesNotContain("Sheet2", bench.Harness.Session.Backend.Layouts);
            Assert.DoesNotContain(bench.Harness.Session.Backend.Blocks(), x => x.Name == "NB");
        }

        [Theory]
        [MemberData(nameof(Modes))]
        public async Task A_layer_of_a_true_colour_switched_off_keeps_its_colour(string mode)
        {
            await using Bench bench = await Bench.Start(mode);
            DxfColor rgb = DxfColor.FromRgb(10, 200, 30);

            if (bench.Harness.Session.Backend is LocalBackend local)
            {
                local.Document.Database.LayerTable.Add(new DxfLayerTableRecord { Name = "RGB", Color = rgb });
            }
            else
            {
                var app = (RemoteBackend)bench.Harness.Session.Backend;
                app.Document.AddLayer(new CadRemoteLayer("RGB") { Color = rgb });
                app.Document.CommitChanges();
            }

            await bench.Call("set_layer", ("name", "RGB"), ("on", false));

            DxfColor now = bench.Harness.Session.Backend is LocalBackend after
                ? after.Document.Database.LayerTable["RGB"].Color
                : ((RemoteBackend)bench.Harness.Session.Backend).Document.Layers.Single(l => l.Name == "RGB").Color;
            Assert.True(now.IsByColor);
            Assert.Equal(rgb, now);
        }

        [Theory]
        [InlineData("12")]
        [InlineData("AC1009")]
        [InlineData("R12")]
        public async Task A_dwg_of_version_12_is_refused_before_anything_is_made(string version)
        {
            await using Bench bench = await Bench.Start("file");
            await bench.Add("add_line", ("start", new[] { 0.0, 0.0 }), ("end", new[] { 10.0, 0.0 }));
            string folder = TestFolder.Make("dwg12");
            string path = System.IO.Path.Combine(folder, "deeper", "x.dwg");

            string message = await bench.Fail("save_as", ("path", path), ("version", version));

            Assert.Contains("DXF", message);
            Assert.False(System.IO.Directory.Exists(System.IO.Path.Combine(folder, "deeper")));
        }

        [Theory]
        [MemberData(nameof(Modes))]
        public async Task Fillet_chamfer_and_offset_leave_the_radius_and_the_distances_the_next_command_offers_as_they_were(
            string mode
        )
        {
            await using Bench bench = await Bench.Start(mode);
            IDrawingBackend drawing = bench.Harness.Session.Backend;
            string a = await bench.Add("add_line", ("start", new[] { 0.0, 0.0 }), ("end", new[] { 100.0, 0.0 }));
            string b = await bench.Add("add_line", ("start", new[] { 0.0, 0.0 }), ("end", new[] { 0.0, 100.0 }));
            string c = await bench.Add("add_line", ("start", new[] { 50.0, 50.0 }), ("end", new[] { 150.0, 50.0 }));
            string d = await bench.Add("add_line", ("start", new[] { 50.0, 50.0 }), ("end", new[] { 50.0, 150.0 }));
            string[] names = { "FILLETRAD", "CHAMFERA", "CHAMFERB", "OFFSETDIST" };
            double[] before = names.Select(n => Convert.ToDouble(drawing.Variable(n))).ToArray();

            await bench.Call("fillet", ("first", a), ("second", b), ("radius", 7.0));
            await bench.Call("chamfer", ("first", c), ("second", d), ("distance1", 3.0), ("distance2", 4.0));
            await bench.Call("offset", ("handle", a), ("side", new[] { 50.0, 20.0 }), ("distance", 11.0));

            Assert.Equal(before, names.Select(n => Convert.ToDouble(drawing.Variable(n))).ToArray());
            Assert.Equal(1, await bench.Count("ARC"));

            // Each is still one undo step.
            await bench.Call("undo");
            Assert.Equal(5, await bench.Count("LINE"));
            await bench.Call("undo");
            Assert.Equal(4, await bench.Count("LINE"));
            await bench.Call("undo");
            Assert.Equal(0, await bench.Count("ARC"));
        }

        [Fact]
        public async Task A_box_given_by_its_far_corner_first_draws_the_same_part_as_the_box_the_right_way_round()
        {
            await using Bench bench = await Bench.Start("file");
            await bench.Add("add_line", ("start", new[] { 0.0, 0.0 }), ("end", new[] { 100.0, 100.0 }));
            await bench.Add("add_line", ("start", new[] { 0.0, 0.0 }), ("end", new[] { 1000.0, 500.0 }));

            byte[] Png(double[] box, byte[] unused = null) =>
                Convert.FromBase64String(
                    (string)
                        System.Text.Json.Nodes.JsonNode.Parse(
                            System.Text.Json.JsonSerializer.Serialize(
                                bench
                                    .Harness.CallRawAsync("render_png", ("box", box), ("width", 200))
                                    .Result.Content.OfType<ModelContextProtocol.Protocol.ImageContentBlock>()
                                    .Single()
                            )
                        )["data"]
                );

            byte[] right = Png(new[] { 0.0, 0.0, 100.0, 100.0 });
            byte[] reversed = Png(new[] { 100.0, 100.0, 0.0, 0.0 });
            byte[] everything = await Task.FromResult(Png(new[] { 0.0, 0.0, 1000.0, 500.0 }));

            Assert.Equal(right, reversed);
            Assert.NotEqual(right, everything);

            string folder = TestFolder.Make("svgbox");
            await bench.Call(
                "export_svg",
                ("path", System.IO.Path.Combine(folder, "a.svg")),
                ("box", new[] { 0.0, 0.0, 100.0, 100.0 })
            );
            await bench.Call(
                "export_svg",
                ("path", System.IO.Path.Combine(folder, "b.svg")),
                ("box", new[] { 100.0, 100.0, 0.0, 0.0 })
            );
            Assert.Equal(
                System.IO.File.ReadAllText(System.IO.Path.Combine(folder, "a.svg")),
                System.IO.File.ReadAllText(System.IO.Path.Combine(folder, "b.svg"))
            );
        }

        [Theory]
        [MemberData(nameof(Modes))]
        public async Task Undo_and_redo_say_whether_a_step_was_taken(string mode)
        {
            await using Bench bench = await Bench.Start(mode);

            Assert.False((bool)(await bench.Call("undo"))["undone"]);
            await bench.Add("add_line", ("start", new[] { 0.0, 0.0 }), ("end", new[] { 10.0, 0.0 }));
            Assert.True((bool)(await bench.Call("undo"))["undone"]);
            Assert.Equal(0, await bench.Count("LINE"));
            Assert.True((bool)(await bench.Call("redo"))["redone"]);
            Assert.Equal(1, await bench.Count("LINE"));
            Assert.False((bool)(await bench.Call("redo"))["redone"]);
        }

        [Fact]
        public async Task Undo_and_redo_are_refused_while_the_users_line_or_text_waits_for_an_answer()
        {
            await using Bench bench = await Bench.Start("app", scripts: true);
            await bench.Add("add_line", ("start", new[] { 0.0, 0.0 }), ("end", new[] { 100.0, 0.0 }));
            int before = await bench.Count("LINE");

            await bench.Call("run_command", ("text", "LINE\n0,200\n10,200\n20,200\n"));
            Assert.Contains("command is running", await bench.Fail("undo"));
            Assert.Contains("command is running", await bench.Fail("redo"));
            Assert.Equal(before + 2, await bench.Count("LINE"));
            await bench.Call("run_command", ("text", "\u001b"));

            await bench.Call("run_command", ("text", "TEXT\n0,300\n"));
            Assert.Contains("command is running", await bench.Fail("undo"));
            Assert.Equal(before + 2, await bench.Count("LINE"));
            Assert.Equal(0, await bench.Count("TEXT"));
        }
    }
}
