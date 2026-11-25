using System.Collections.Generic;
using System.Linq;
using GeometryHelper.Dxf;
using GeometryHelper.Geometry;
using Xunit;
using static OpenCAD2D.UnitTest.EditingDrawings;

namespace OpenCAD2D.UnitTest
{
    /// <summary>SHEETSET and SHEETSETHIDE (A12 4a): each asks the window for the Sheet Set Manager palette, and says so where there is no window.</summary>
    public sealed class SheetSetCommandTests
    {
        private static CadDocument Site()
        {
            var database = new DxfDatabase("AC1027");
            database.ModelSpace.AppendEntity(new DxfLine(new GeoPoint3(0, 0, 0), new GeoPoint3(100, 0, 0)));
            return CadDocument.From(database, "site.dxf");
        }

        [Fact]
        public void SheetsetAndSheetsethide_AskTheWindowForThePalette()
        {
            var asked = new List<string>();
            CadDocument document = Site();
            CadCommandSession session = Session(document);
            session.UiRequested += (sender, e) =>
            {
                asked.Add(e.Name);
                e.Handled = true;
            };

            Run(session, "SHEETSET", "SHEETSETHIDE");

            Assert.Equal(new[] { "SHEETSET", "SHEETSETHIDE" }, asked);
        }

        [Fact]
        public void WithNoWindow_TheCommandsSayThereIsNoPalette()
        {
            CadCommandSession session = Run(Session(Site()), "SHEETSET");
            Assert.Equal(
                "The Sheet Set Manager is a palette of the window; there is none to show.",
                session.History.Last()
            );

            Run(session, "SHEETSETHIDE");

            Assert.Equal(
                "The Sheet Set Manager is a palette of the window; there is none to close.",
                session.History.Last()
            );
        }
    }
}
