using System;
using System.IO;
using System.Linq;
using GeometryHelper.Dxf;
using GeometryHelper.Geometry;
using Xunit;

namespace OpenCAD2D.Wpf.UnitTest
{
    /// <summary>
    /// How the facade that draws with no window treats paper: a space never set up is fitted to its sheet in a PDF, a page
    /// setup that was made is kept as it is, and a layout of inches is as large a picture as the same sheet of millimetres.
    /// </summary>
    public class CadRenderPaperTests : IDisposable
    {
        private readonly string _folder = Path.Combine(
            Path.GetTempPath(),
            "gk",
            "cad-render-paper-" + Guid.NewGuid().ToString("N")
        );

        public CadRenderPaperTests() => Directory.CreateDirectory(_folder);

        public void Dispose()
        {
            try
            {
                Directory.Delete(_folder, true);
            }
            catch (IOException) { }
            catch (UnauthorizedAccessException) { }
        }

        /// <summary>A drawing made in code and never set up: a line from (0, 0) to (400, 300) in model space, one from (0, 0) to (10, 10) on Layout1.</summary>
        private static CadDocument NeverSetUp()
        {
            var database = new DxfDatabase("AC1027");
            database.ModelSpace.AppendEntity(new DxfLine(new GeoPoint3(0, 0, 0), new GeoPoint3(400, 300, 0)));
            DxfLayout layout = database.Layouts.Single(l => l.LayoutName == RenderDrawings.LayoutName);
            layout.Initialize();
            layout.BlockTableRecord.AppendEntity(new DxfLine(new GeoPoint3(0, 0, 0), new GeoPoint3(10, 10, 0)));
            return CadDocument.From(database, "never.dxf");
        }

        [Theory]
        [InlineData("Model", 287.0 / 300.0)]
        [InlineData("Layout1", 287.0 / 10.0)]
        public void APdfOfASpaceNeverSetUp_IsFittedToTheDefaultSheet_NotPlottedAtAnInchToTheUnit(
            string layout,
            double scale
        )
        {
            CadPlotPage page = CadRender.WritePdf(Path.Combine(_folder, "never.pdf"), NeverSetUp(), layout);

            Assert.Equal(420.0, page.SheetWidth, 6);
            Assert.Equal(297.0, page.SheetHeight, 6);
            Assert.Equal(scale, page.Scale, 6);
            Assert.True(page.Area.Width * page.Scale <= page.Printable.Width + 1e-6);
            Assert.True(page.Area.Height * page.Scale <= page.Printable.Height + 1e-6);
        }

        [Fact]
        public void APdfOfASpaceWithAPageSetup_KeepsItsScale_FittedOrNot()
        {
            CadDocument document = NeverSetUp();
            CadSpace model = document.Spaces.Single(s => s.IsModel);
            CadPlotSettings settings = CadPlotSettings.Read(document, model);
            settings.Paper = CadPaper.Default;
            settings.Landscape = true;
            settings.FitToPaper = false;
            settings.PaperUnits = 1.0;
            settings.DrawingUnits = 2.0;
            Assert.True(settings.ApplyTo(document, model).Succeeded);
            CadDocument again = CadDocument.From(DxfDatabase.Parse(document.Database.ToText()), "set.dxf");

            CadPlotPage page = CadRender.WritePdf(Path.Combine(_folder, "set.pdf"), again, "Model");

            Assert.Equal(0.5, page.Scale, 6);
        }

        [Theory]
        [InlineData(false, 96.0, 1587, 1123)]
        [InlineData(true, 96.0, 1587, 1123)]
        [InlineData(true, 300.0, 4961, 3508)]
        [InlineData(true, 25.4, 420, 297)]
        public void ThePicturesSize_OfALayoutOfInches_IsTheSameSheetAsOneOfMillimetres(
            bool inches,
            double dpi,
            int expectedWidth,
            int expectedHeight
        )
        {
            CadDocument document = NeverSetUp();
            CadSpace space = document.Spaces.Single(s => s.Name == RenderDrawings.LayoutName);
            CadPlotSettings settings = CadPlotSettings.Read(document, space);
            settings.Paper = CadPaper.Default;
            settings.Landscape = true;
            Assert.True(settings.ApplyTo(document, space).Succeeded);
            DxfDatabase database = DxfDatabase.Parse(document.Database.ToText());
            database.Layouts.Single(l => l.LayoutName == RenderDrawings.LayoutName).PlotPaperUnits = inches
                ? DxfPlotPaperUnit.Inches
                : DxfPlotPaperUnit.Millimeters;
            CadDocument opened = CadDocument.From(database, "units.dxf");

            CadRenderImage picture = CadRender.Draw(
                opened,
                new CadRenderOptions { Layout = RenderDrawings.LayoutName, Dpi = dpi }
            );

            Assert.Equal(expectedWidth, picture.Width);
            Assert.Equal(expectedHeight, picture.Height);
        }
    }
}
