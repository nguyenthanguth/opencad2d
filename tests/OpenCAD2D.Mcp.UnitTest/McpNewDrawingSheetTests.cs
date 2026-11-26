using System.Linq;
using GeometryHelper.Dxf;
using Xunit;

namespace OpenCAD2D.Mcp.UnitTest
{
    /// <summary>The drawing <c>new_drawing</c> makes: its Layout1 is a sheet of millimetres, as the app's NEW makes it.</summary>
    public class McpNewDrawingSheetTests
    {
        [Fact]
        public void ANewDrawing_HasItsLayout1AsAnA3SheetInMillimetres()
        {
            LocalBackend backend = LocalBackend.New();

            DxfLayout layout = backend.Document.Database.Layouts.Single(l => !l.ModelType);

            Assert.Equal(DxfPlotPaperUnit.Millimeters, layout.PlotPaperUnits);
            Assert.Equal(420.0, layout.PlotPaperSize.X, 6);
            Assert.Equal(297.0, layout.PlotPaperSize.Y, 6);
            Assert.Equal(1.0, CadUnitScale.OfPaper(layout), 9);
        }
    }
}
