using System;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.Json.Nodes;
using System.Threading.Tasks;
using GeometryHelper.Dxf;
using GeometryHelper.Geometry;
using ModelContextProtocol.Protocol;
using Xunit;

namespace OpenCAD2D.Mcp.UnitTest
{
    /// <summary>The output and housekeeping tools over a drawing file: plot, SVG, save as, purge, audit and zoom.</summary>
    public class McpOutputTests
    {
        private sealed class Plated : IAsyncDisposable
        {
            public McpHarness Harness { get; set; }

            public string Folder { get; set; }

            public string File { get; set; }

            public ValueTask DisposeAsync() => Harness.DisposeAsync();
        }

        private static async Task<Plated> Plate(string name, bool readOnly = false, bool scripts = false)
        {
            string folder = TestFolder.Make(name);
            string file = Drawings.Plate(folder);
            McpHarness harness = await McpHarness.StartAsync(
                new McpOptions
                {
                    File = file,
                    ReadOnly = readOnly,
                    AllowScripts = scripts,
                }
            );
            return new Plated
            {
                Harness = harness,
                Folder = folder,
                File = file,
            };
        }

        private static (int Width, int Height) PngSize(CallToolResult result)
        {
            byte[] png = Assert.Single(result.Content.OfType<ImageContentBlock>()).DecodedData.ToArray();
            int w = (png[16] << 24) | (png[17] << 16) | (png[18] << 8) | png[19];
            int h = (png[20] << 24) | (png[21] << 16) | (png[22] << 8) | png[23];
            return (w, h);
        }

        [Fact]
        public async Task Plot_pdf_of_model_space_writes_one_a3_page_with_the_drawings_words()
        {
            await using Plated plate = await Plate("plot");
            McpHarness harness = plate.Harness;
            string folder = plate.Folder;
            string pdf = Path.Combine(folder, "plate.pdf");

            JsonNode result = await harness.CallAsync("plot_pdf", ("path", pdf), ("layout", "Model"));

            Assert.Equal(pdf, (string)result["path"]);
            Assert.Equal("Model", (string)result["layout"]);
            Assert.Equal(420.0, (double)result["sheet"][0], 3);
            Assert.Equal(297.0, (double)result["sheet"][1], 3);
            byte[] bytes = File.ReadAllBytes(pdf);
            Assert.Equal((long)bytes.Length, (long)result["bytes"]);
            Assert.StartsWith("%PDF", Encoding.ASCII.GetString(bytes, 0, 4));
            Assert.Contains("PLATE", PdfWords.Of(bytes));
        }

        [Fact]
        public async Task Plot_pdf_takes_the_current_layout_adds_the_extension_and_refuses_what_is_there()
        {
            await using Plated plate = await Plate("plotrules");
            McpHarness harness = plate.Harness;
            string folder = plate.Folder;
            string stem = Path.Combine(folder, "sheet");

            JsonNode first = await harness.CallAsync("plot_pdf", ("path", stem));

            Assert.Equal(stem + ".pdf", (string)first["path"]);
            Assert.True(File.Exists(stem + ".pdf"));
            Assert.Contains("is there already", await harness.FailAsync("plot_pdf", ("path", stem + ".pdf")));
            await harness.CallAsync("plot_pdf", ("path", stem + ".pdf"), ("overwrite", true));
            Assert.Contains("not .svg", await harness.FailAsync("plot_pdf", ("path", stem + ".svg")));
            Assert.Contains(
                "has no layout named Nowhere",
                await harness.FailAsync("plot_pdf", ("path", stem + "2"), ("layout", "Nowhere"))
            );
        }

        [Fact]
        public async Task Plot_pdf_paper_scale_and_fit_change_this_plot_and_not_the_drawing()
        {
            await using Plated plate = await Plate("plotsetup");
            McpHarness harness = plate.Harness;
            string folder = plate.Folder;
            string file = plate.File;

            JsonNode a4 = await harness.CallAsync("plot_pdf", ("path", Path.Combine(folder, "a4")), ("paper", "A4"));
            Assert.Equal(297.0, (double)a4["sheet"][0], 3);
            Assert.Equal(210.0, (double)a4["sheet"][1], 3);

            JsonNode scaled = await harness.CallAsync(
                "plot_pdf",
                ("path", Path.Combine(folder, "scaled")),
                ("scale", "1:2")
            );
            Assert.Equal("1:2", (string)scaled["scale"]);

            JsonNode fitted = await harness.CallAsync("plot_pdf", ("path", Path.Combine(folder, "fit")), ("fit", true));
            // The plate is 200 by 100 and the printable part of an A3 sheet 410 by 287: 2.05 times, a hair over.
            Assert.Equal("2.05:1", (string)fitted["scale"]);

            Assert.Contains(
                "two numbers",
                await harness.FailAsync("plot_pdf", ("path", Path.Combine(folder, "x")), ("scale", "big"))
            );
            Assert.Contains(
                "not both",
                await harness.FailAsync(
                    "plot_pdf",
                    ("path", Path.Combine(folder, "y")),
                    ("scale", "1:2"),
                    ("fit", true)
                )
            );
            string unknown = await harness.FailAsync("plot_pdf", ("path", Path.Combine(folder, "z")), ("paper", "A9"));
            Assert.Contains("ISO A3", unknown);
            Assert.False((bool)(await harness.CallAsync("info"))["modified"]);
            Assert.Equal(file, (string)(await harness.CallAsync("info"))["path"]);
        }

        [Fact]
        public async Task Plot_pdf_of_an_empty_layout_is_refused_in_words()
        {
            string folder = TestFolder.Make("plotempty");
            await using McpHarness harness = await McpHarness.StartAsync(new McpOptions());
            await harness.CallAsync("new_drawing");

            string message = await harness.FailAsync("plot_pdf", ("path", Path.Combine(folder, "empty.pdf")));

            Assert.Contains("nothing to plot", message, StringComparison.OrdinalIgnoreCase);
        }

        [Fact]
        public async Task Export_svg_writes_vectors_with_the_text_and_follows_the_view_zoom_window_set()
        {
            await using Plated plate = await Plate("svg");
            McpHarness harness = plate.Harness;
            string folder = plate.Folder;
            string svg = Path.Combine(folder, "plate.svg");

            JsonNode result = await harness.CallAsync("export_svg", ("path", svg), ("width", 800));
            string whole = File.ReadAllText(svg);

            Assert.Equal(svg, (string)result["path"]);
            Assert.StartsWith("<?xml", whole);
            Assert.Contains("<svg", whole);
            Assert.Contains("viewBox=\"0 0 800 ", whole);
            Assert.Contains("overwrite", await harness.FailAsync("export_svg", ("path", svg)));

            // A window 100 wide and 100 high is square: with no box the picture takes its shape.
            await harness.CallAsync("zoom_window", ("box", new[] { 0.0, 0.0, 100.0, 100.0 }));
            await harness.CallAsync("export_svg", ("path", svg), ("width", 800), ("overwrite", true));
            Assert.Contains("viewBox=\"0 0 800 800\"", File.ReadAllText(svg));
        }

        [Fact]
        public async Task Save_as_dxf_writes_a_copy_of_a_version_and_the_open_drawing_stays_as_it_was()
        {
            await using Plated plate = await Plate("saveasdxf");
            McpHarness harness = plate.Harness;
            string folder = plate.Folder;
            string file = plate.File;
            await harness.CallAsync("add_line", ("start", new[] { 0.0, 0.0 }), ("end", new[] { 5.0, 5.0 }));
            string copy = Path.Combine(folder, "copy.dxf");

            JsonNode result = await harness.CallAsync("save_as", ("path", copy), ("version", "2010"));

            Assert.Equal("dxf", (string)result["format"]);
            Assert.Equal("AC1024", (string)result["version"]);
            Assert.Contains("AC1024", File.ReadAllText(copy));
            JsonNode info = await harness.CallAsync("info");
            Assert.Equal(file, (string)info["path"]);
            Assert.True((bool)info["modified"]);

            // The copy has the line that was not saved to the drawing's own file.
            await using McpHarness reopened = await McpHarness.StartAsync(new McpOptions { File = copy });
            Assert.Equal(5, (int)(await reopened.CallAsync("info"))["entityCount"]);
            await using McpHarness original = await McpHarness.StartAsync(new McpOptions { File = file });
            Assert.Equal(4, (int)(await original.CallAsync("info"))["entityCount"]);
        }

        [Fact]
        public async Task Save_as_dwg_writes_a_dwg_that_opens_again_with_every_entity()
        {
            await using Plated plate = await Plate("saveasdwg");
            McpHarness harness = plate.Harness;
            string folder = plate.Folder;
            string copy = Path.Combine(folder, "copy.dwg");

            JsonNode result = await harness.CallAsync("save_as", ("path", copy), ("version", "2013"));

            Assert.Equal("dwg", (string)result["format"]);
            Assert.Equal("AC1027", (string)result["version"]);
            Assert.Equal("AC1027", Encoding.ASCII.GetString(File.ReadAllBytes(copy), 0, 6));
            await using McpHarness reopened = await McpHarness.StartAsync(new McpOptions { File = copy });
            Assert.Equal(4, (int)(await reopened.CallAsync("info"))["entityCount"]);

            JsonNode defaulted = await harness.CallAsync(
                "save_as",
                ("path", Path.Combine(folder, "plain")),
                ("format", "dwg")
            );
            Assert.EndsWith("plain.dwg", (string)defaulted["path"]);
            Assert.Equal("AC1032", Encoding.ASCII.GetString(File.ReadAllBytes((string)defaulted["path"]), 0, 6));
        }

        [Fact]
        public async Task Save_as_refuses_what_it_must_a_file_that_is_there_the_drawings_own_a_bad_format_and_a_bad_version()
        {
            await using Plated plate = await Plate("saveasrefuse");
            McpHarness harness = plate.Harness;
            string folder = plate.Folder;
            string file = plate.File;
            byte[] own = File.ReadAllBytes(file);

            string theirs = await harness.FailAsync("save_as", ("path", file));
            Assert.Contains("open drawing's own file", theirs);
            Assert.Equal(own, File.ReadAllBytes(file));
            Assert.Contains(
                "format is dxf or dwg",
                await harness.FailAsync("save_as", ("path", Path.Combine(folder, "a.txt")))
            );
            Assert.Contains(
                "is not",
                await harness.FailAsync("save_as", ("path", Path.Combine(folder, "b.dxf")), ("version", "1999"))
            );
            Assert.Contains(
                "not .dxf",
                await harness.FailAsync("save_as", ("path", Path.Combine(folder, "c.dxf")), ("format", "dwg"))
            );

            // Not even with overwrite: save_as writes a copy, and the drawing's own file is written by save alone.
            Assert.Contains(
                "open drawing's own file",
                await harness.FailAsync("save_as", ("path", file), ("overwrite", true))
            );
            Assert.Equal(own, File.ReadAllBytes(file));
        }

        [Fact]
        public async Task Purge_removes_a_layer_nothing_uses_and_undo_brings_it_back()
        {
            await using Plated plate = await Plate("purge");
            McpHarness harness = plate.Harness;
            await harness.CallAsync("add_layer", ("name", "Spare"));
            await harness.CallAsync("add_layer", ("name", "Used"));
            await harness.CallAsync(
                "add_line",
                ("start", new[] { 0.0, 0.0 }),
                ("end", new[] { 5.0, 5.0 }),
                ("layer", "Used")
            );

            JsonNode result = await harness.CallAsync("purge", ("kinds", new[] { "layers" }));

            Assert.Equal(1, (int)result["total"]);
            Assert.Equal(1, (int)result["purged"]["layers"]);
            Assert.Contains(result["messages"].AsArray(), m => ((string)m).Contains("Spare"));
            JsonNode layers = await harness.CallAsync("list_layers");
            Assert.DoesNotContain(layers.AsArray(), l => (string)l["name"] == "Spare");
            Assert.Contains(layers.AsArray(), l => (string)l["name"] == "Used");

            await harness.CallAsync("undo");
            Assert.Contains((await harness.CallAsync("list_layers")).AsArray(), l => (string)l["name"] == "Spare");
        }

        [Fact]
        public async Task Purge_all_and_zero_length_work_and_a_kind_nobody_knows_is_refused()
        {
            await using Plated plate = await Plate("purgeall");
            McpHarness harness = plate.Harness;
            await harness.CallAsync("add_layer", ("name", "Spare"));
            await harness.CallAsync("add_line", ("start", new[] { 7.0, 7.0 }), ("end", new[] { 7.0, 7.0 }));
            Assert.Equal(5, (int)(await harness.CallAsync("info"))["entityCount"]);

            JsonNode all = await harness.CallAsync("purge", ("kinds", new[] { "all", "zero_length" }));

            Assert.True((int)all["purged"]["layers"] >= 1, all.ToJsonString());
            Assert.Equal(4, (int)(await harness.CallAsync("info"))["entityCount"]);
            Assert.Contains("is not one to purge", await harness.FailAsync("purge", ("kinds", new[] { "everything" })));
            Assert.Contains("at least one kind", await harness.FailAsync("purge", ("kinds", Array.Empty<string>())));
        }

        [Fact]
        public async Task Audit_of_a_sound_drawing_finds_nothing_and_with_fix_does_nothing()
        {
            await using Plated plate = await Plate("audit");
            McpHarness harness = plate.Harness;

            JsonNode checkedOnly = await harness.CallAsync("audit");
            JsonNode fixedToo = await harness.CallAsync("audit", ("fix", true));

            Assert.Equal(0, (int)checkedOnly["found"]);
            Assert.False((bool)checkedOnly["fixed"]);
            Assert.Equal(0, (int)fixedToo["found"]);
            Assert.True((bool)fixedToo["fixed"]);
            Assert.Equal(0, (int)fixedToo["remaining"]);
        }

        [Fact]
        public async Task Audit_counts_the_errors_of_a_damaged_drawing_by_kind_and_fix_repairs_them_in_one_undo_step()
        {
            string folder = TestFolder.Make("auditbad");
            string file = Drawings.Plate(folder);

            // The circle is put on a layer the layer table has not: its group 8 names Ghost.
            string text = File.ReadAllText(file);
            string ghost = System.Text.RegularExpressions.Regex.Replace(text, @"(\r?\n\s*8\r?\n)Holes", "${1}Ghost");
            Assert.NotEqual(text, ghost);
            string broken = Path.Combine(folder, "broken.dxf");
            File.WriteAllText(broken, ghost);
            await using McpHarness harness = await McpHarness.StartAsync(new McpOptions { File = broken });

            JsonNode found = await harness.CallAsync("audit");

            Assert.True((int)found["found"] >= 1, found.ToJsonString());
            Assert.NotEmpty(found["kinds"].AsObject());
            Assert.NotEmpty(found["messages"].AsArray());
            Assert.Equal((int)found["found"], (int)found["remaining"]);

            JsonNode repaired = await harness.CallAsync("audit", ("fix", true));

            Assert.True((int)repaired["found"] >= 1);
            Assert.Equal(0, (int)repaired["remaining"]);
            Assert.Equal(0, (int)(await harness.CallAsync("audit"))["found"]);
            await harness.CallAsync("undo");
            Assert.True((int)(await harness.CallAsync("audit"))["found"] >= 1, "One undo takes the fix back.");
        }

        [Fact]
        public async Task Zoom_on_a_file_sets_the_view_render_png_uses_and_zoom_extents_clears_it()
        {
            await using Plated plate = await Plate("zoomfile");
            McpHarness harness = plate.Harness;
            (int wholeWidth, int wholeHeight) = PngSize(await harness.CallRawAsync("render_png", ("width", 400)));
            Assert.Equal(400, wholeWidth);
            Assert.True(wholeHeight < 300, "The plate is twice as wide as it is high.");

            JsonNode window = await harness.CallAsync("zoom_window", ("box", new[] { 0.0, 0.0, 100.0, 100.0 }));
            Assert.Equal("render", (string)window["view"]);
            Assert.Equal(100.0, (double)window["box"][2], 3);
            Assert.Equal((400, 400), PngSize(await harness.CallRawAsync("render_png", ("width", 400))));

            // A box given to render_png still wins.
            (_, int boxHeight) = PngSize(
                await harness.CallRawAsync("render_png", ("width", 400), ("box", new[] { 0.0, 0.0, 200.0, 100.0 }))
            );
            Assert.Equal(200, boxHeight);

            JsonNode circle = (await harness.CallAsync("query_entities", ("kinds", new[] { "CIRCLE" })))["entities"][0];
            JsonNode onObject = await harness.CallAsync("zoom_object", ("handles", new[] { (string)circle["handle"] }));
            Assert.True((double)onObject["box"][0] < 80.0 && (double)onObject["box"][2] > 120.0);
            Assert.Equal(400, PngSize(await harness.CallRawAsync("render_png", ("width", 400))).Width);

            JsonNode extents = await harness.CallAsync("zoom_extents");
            Assert.Equal(200.0, (double)extents["box"][2], 1);
            Assert.Equal((wholeWidth, wholeHeight), PngSize(await harness.CallRawAsync("render_png", ("width", 400))));
            Assert.Contains("There is no layout", "There is no layout named Nowhere");
            Assert.Contains("no layout named Nowhere", await harness.FailAsync("zoom_extents", ("layout", "Nowhere")));
            Assert.Contains("window", await harness.FailAsync("zoom_window", ("box", new[] { 5.0, 5.0, 5.0, 9.0 })));
        }

        [Fact]
        public async Task Read_only_refuses_the_tools_that_write_a_file_or_the_drawing_and_allows_the_rest()
        {
            await using Plated plate = await Plate("readonly", readOnly: true);
            McpHarness harness = plate.Harness;
            string folder = plate.Folder;
            string file = plate.File;
            byte[] own = File.ReadAllBytes(file);

            foreach (
                (string tool, (string, object)[] arguments) in new (string, (string, object)[])[]
                {
                    ("plot_pdf", new (string, object)[] { ("path", Path.Combine(folder, "a.pdf")) }),
                    ("export_svg", new (string, object)[] { ("path", Path.Combine(folder, "a.svg")) }),
                    ("save_as", new (string, object)[] { ("path", Path.Combine(folder, "a.dxf")) }),
                    ("purge", new (string, object)[] { ("kinds", new[] { "layers" }) }),
                    ("audit", new (string, object)[] { ("fix", true) }),
                }
            )
            {
                Assert.Contains("--read-only", await harness.FailAsync(tool, arguments));
            }

            Assert.Empty(Directory.GetFiles(folder, "a.*"));
            await harness.CallAsync("audit");
            await harness.CallAsync("zoom_extents");
            await harness.CallAsync("zoom_window", ("box", new[] { 0.0, 0.0, 10.0, 10.0 }));
            Assert.Equal(own, File.ReadAllBytes(file));
        }

        [Fact]
        public async Task Plot_pdf_attached_to_nothing_and_on_a_new_drawing_with_a_layout_sheet_uses_the_layouts_setup()
        {
            string folder = TestFolder.Make("plotlayout");
            await using McpHarness harness = await McpHarness.StartAsync(new McpOptions());
            await harness.CallAsync("new_drawing");
            await harness.CallAsync(
                "add_text",
                ("text", "ONSHEET"),
                ("at", new[] { 20.0, 20.0 }),
                ("height", 5.0),
                ("layout", "Layout1")
            );

            JsonNode result = await harness.CallAsync(
                "plot_pdf",
                ("path", Path.Combine(folder, "sheet.pdf")),
                ("layout", "Layout1")
            );

            Assert.Equal(420.0, (double)result["sheet"][0], 3);
            Assert.Equal(297.0, (double)result["sheet"][1], 3);
            Assert.Contains("ONSHEET", PdfWords.Of(File.ReadAllBytes((string)result["path"])));
        }
    }
}
