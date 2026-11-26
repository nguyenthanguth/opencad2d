using System;
using System.IO;
using System.Linq;
using GeometryHelper.Dxf;
using GeometryHelper.Geometry;
using Xunit;

namespace OpenCAD2D.Wpf.UnitTest
{
    /// <summary>
    /// The PDF a script plots (<see cref="CadPlot.WritePdf"/>): a layout at the page setup it has, or at a sheet, a scale or fit given for that
    /// plot alone, which are not written into the drawing; what it refuses it refuses in words.
    /// </summary>
    public class CadPlotTests : IDisposable
    {
        private readonly string _folder = Path.Combine(
            Path.GetTempPath(),
            "gk",
            "cad-plot-" + Guid.NewGuid().ToString("N")
        );

        public CadPlotTests() => Directory.CreateDirectory(_folder);

        public void Dispose()
        {
            try
            {
                Directory.Delete(_folder, true);
            }
            catch (IOException) { }
            catch (UnauthorizedAccessException) { }
        }

        /// <summary>A drawing as NEW makes it (Layout1 an A3 sheet in millimetres) with a line 100 long on the sheet and one 400 long in model space.</summary>
        private static CadDocument Sheet()
        {
            var database = new DxfDatabase("AC1027");
            CadTemplate.UseMillimetreSheet(database);
            database.ModelSpace.AppendEntity(new DxfLine(new GeoPoint3(0, 0, 0), new GeoPoint3(400, 300, 0)));
            DxfLayout layout = database.Layouts.Single(l => l.LayoutName == "Layout1");
            layout.Initialize();
            layout.BlockTableRecord.AppendEntity(new DxfLine(new GeoPoint3(10, 10, 0), new GeoPoint3(110, 10, 0)));
            return CadDocument.From(database, "sheet.dxf");
        }

        private string At(string name) => Path.Combine(_folder, name);

        [Fact]
        public void ALayoutPlotsAtTheScaleItHas_AndASheetOrScaleGivenIsForThatPlotAlone()
        {
            CadDocument document = Sheet();
            CadLayouts.SetPageSetup(document, "Layout1", null, null, null, "1:2", null);

            CadPlotPage own = CadPlot.WritePdf(At("own.pdf"), document, "Layout1", null, null, false);
            CadPlotPage tenth = CadPlot.WritePdf(At("tenth.pdf"), document, "layout1", null, "1:10", false);
            CadPlotPage small = CadPlot.WritePdf(At("small.pdf"), document, "Layout1", "A4", null, false);
            CadPlotPage fitted = CadPlot.WritePdf(At("fit.pdf"), document, "Layout1", null, null, true);

            Assert.Equal(0.5, own.Scale, 9);
            Assert.Equal(0.1, tenth.Scale, 9);
            Assert.Equal(297.0, small.SheetWidth, 6);
            Assert.Equal(210.0, small.SheetHeight, 6);
            Assert.True(fitted.Scale > 0.5);
            Assert.Equal(0.5, CadPlot.WritePdf(At("again.pdf"), document, "Layout1", null, null, false).Scale, 9);
            Assert.Equal("1:2", CadLayouts.List(document)[1].Scale);
            Assert.All(new[] { "own", "tenth", "small", "fit" }, name => Assert.True(File.Exists(At(name + ".pdf"))));
        }

        [Fact]
        public void TheCurrentSpaceIsPlotted_WhereNoLayoutIsNamed()
        {
            CadDocument document = Sheet();
            document.ShowSpace(document.Spaces[1]);

            CadPlotPage page = CadPlot.WritePdf(At("current.pdf"), document, null, null, null, false);

            Assert.Equal(1.0, page.Scale, 9);
        }

        [Fact]
        public void APlotIsRefusedInWords()
        {
            CadDocument document = Sheet();

            Assert.Throws<ArgumentException>(() =>
                CadPlot.WritePdf(At("a.pdf"), document, "Layout1", null, "1:1", true)
            );
            Assert.Throws<ArgumentException>(() => CadPlot.WritePdf(At("a.pdf"), document, "Nope", null, null, false));
            Assert.Throws<ArgumentException>(() =>
                CadPlot.WritePdf(At("a.pdf"), document, "Layout1", "A9", null, false)
            );
            Assert.Throws<ArgumentException>(() =>
                CadPlot.WritePdf(At("a.pdf"), document, "Layout1", null, "1:0", false)
            );
            Assert.Throws<ArgumentNullException>(() => CadPlot.WritePdf(null, document, "Layout1", null, null, false));
            Assert.Throws<ArgumentNullException>(() =>
                CadPlot.WritePdf(At("a.pdf"), null, "Layout1", null, null, false)
            );
            Assert.False(File.Exists(At("a.pdf")));
        }
    }
}
