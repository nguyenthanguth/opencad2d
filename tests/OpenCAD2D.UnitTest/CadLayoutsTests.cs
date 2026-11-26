using System;
using System.Linq;
using GeometryHelper.Dxf;
using GeometryHelper.Geometry;
using Xunit;

namespace OpenCAD2D.UnitTest
{
    /// <summary>
    /// Layouts made and set up from code (CadLayouts): a new layout on a sheet, its page setup, and viewports at a scale, each one
    /// undo step.
    /// </summary>
    public class CadLayoutsTests
    {
        /// <summary>A drawing as the app's NEW makes it: Layout1 an A3 sheet in millimetres, a plate 100 by 60 in model space.</summary>
        private static CadDocument Plate()
        {
            var db = new DxfDatabase("AC1027");
            CadTemplate.UseMillimetreSheet(db);
            db.ModelSpace.AppendEntity(new DxfLine(new GeoPoint3(0, 0, 0), new GeoPoint3(100, 0, 0)));
            db.ModelSpace.AppendEntity(new DxfLine(new GeoPoint3(100, 0, 0), new GeoPoint3(100, 60, 0)));
            db.LayerTable.Add(new DxfLayerTableRecord { Name = "Notes" });
            return CadDocument.From(db, "plate.dxf");
        }

        private static DxfViewport[] Viewports(CadDocument document, string layout) =>
            document
                .Spaces.First(s => s.Name == layout)
                .Block.OfType<DxfViewport>()
                .Where(v => !v.IsErased && v.Number > 1)
                .ToArray();

        private static string[] Names(CadDocument document) => document.Spaces.Select(s => s.Name).ToArray();

        [Fact]
        public void AddingALayout_GivesItsSheetAndPlotsItAtOneToOne_AsOneUndoStep()
        {
            CadDocument document = Plate();

            CadLayoutInfo made = CadLayouts.Add(document, "Detail", "ISO_A4", true);

            Assert.Equal("Detail", made.Name);
            Assert.Equal("ISO A4", made.Paper);
            Assert.Equal(297.0, made.Width, 3);
            Assert.Equal(210.0, made.Height, 3);
            Assert.Equal("mm", made.Units);
            Assert.Equal("layout", made.Area);
            Assert.Equal("1:1", made.Scale);
            Assert.Equal(new[] { "Model", "Layout1", "Detail" }, Names(document));
            Assert.True(document.Undo());
            Assert.Equal(new[] { "Model", "Layout1" }, Names(document));
        }

        [Fact]
        public void AddingALayoutTurned_GivesTheSheetOnItsEnd()
        {
            CadDocument document = Plate();

            CadLayoutInfo made = CadLayouts.Add(document, "Tall", "A3", false);

            Assert.False(made.Landscape);
            Assert.Equal(297.0, made.Width, 3);
            Assert.Equal(420.0, made.Height, 3);
        }

        [Fact]
        public void AddingALayout_RefusesABadNameADuplicateAndASheetThePlotterLacks()
        {
            CadDocument document = Plate();

            Assert.Throws<ArgumentException>(() => CadLayouts.Add(document, "", "A3", true));
            Assert.Throws<ArgumentException>(() => CadLayouts.Add(document, "a/b", "A3", true));
            Assert.Throws<ArgumentException>(() => CadLayouts.Add(document, "model", "A3", true));
            Assert.Throws<ArgumentException>(() => CadLayouts.Add(document, "layout1", "A3", true));
            ArgumentException sheet = Assert.Throws<ArgumentException>(() =>
                CadLayouts.Add(document, "Odd", "A7", true)
            );
            Assert.Contains("ISO A3", sheet.Message);
            Assert.Equal(2, document.Spaces.Count);
        }

        [Fact]
        public void ThePageSetup_ChangesOnlyWhatIsGiven()
        {
            CadDocument document = Plate();

            CadLayoutInfo changed = CadLayouts.SetPageSetup(
                document,
                "Layout1",
                "A2",
                null,
                "extents",
                "1:50",
                "acad.ctb"
            );

            Assert.Equal("ISO A2", changed.Paper);
            Assert.Equal(594.0, changed.Width, 3);
            Assert.True(changed.Landscape);
            Assert.Equal("extents", changed.Area);
            Assert.Equal("1:50", changed.Scale);
            Assert.Equal("acad.ctb", changed.PlotStyle);

            CadLayoutInfo fitted = CadLayouts.SetPageSetup(document, "Layout1", null, "portrait", null, "fit", null);

            Assert.Null(fitted.Scale);
            Assert.False(fitted.Landscape);
            Assert.Equal("ISO A2", fitted.Paper);
            Assert.Equal("acad.ctb", fitted.PlotStyle);
            Assert.Equal("1:2", CadLayouts.SetPageSetup(document, "Layout1", null, null, null, "0.5", null).Scale);
            Assert.Equal("2:1", CadLayouts.SetPageSetup(document, "Layout1", null, null, null, "2:1", null).Scale);
        }

        [Fact]
        public void ThePageSetup_RefusesWhatItDoesNotKnow()
        {
            CadDocument document = Plate();

            Assert.Throws<ArgumentException>(() =>
                CadLayouts.SetPageSetup(document, "Nope", null, null, null, null, null)
            );
            Assert.Throws<ArgumentException>(() =>
                CadLayouts.SetPageSetup(document, "Layout1", null, "sideways", null, null, null)
            );
            Assert.Throws<ArgumentException>(() =>
                CadLayouts.SetPageSetup(document, "Layout1", null, null, "window", null, null)
            );
            Assert.Throws<ArgumentException>(() =>
                CadLayouts.SetPageSetup(document, "Layout1", null, null, null, "1:0", null)
            );
            Assert.Throws<ArgumentException>(() =>
                CadLayouts.SetPageSetup(document, "Layout1", null, null, null, "big", null)
            );
        }

        [Fact]
        public void AViewportAtOneToTen_ShowsTenTimesItsFrame_CentredWhereAsked_AsOneUndoStep()
        {
            CadDocument document = Plate();

            string handle = CadLayouts.AddViewport(
                document,
                "Layout1",
                new[] { 20.0, 30.0, 220.0, 130.0 },
                "1:10",
                new[] { 50.0, 30.0 },
                null
            );

            DxfViewport viewport = Assert.Single(Viewports(document, "Layout1"));
            Assert.Equal(handle, viewport.Handle);
            Assert.Equal(200.0, viewport.Width, 6);
            Assert.Equal(100.0, viewport.Height, 6);
            Assert.Equal(120.0, viewport.CenterPoint.X, 6);
            Assert.Equal(80.0, viewport.CenterPoint.Y, 6);
            Assert.Equal(1000.0, viewport.ViewHeight, 6);
            Assert.Equal(50.0, viewport.ViewCenter.X, 6);
            Assert.Equal(30.0, viewport.ViewCenter.Y, 6);
            Assert.Equal(1, CadLayouts.List(document).First(l => l.Name == "Layout1").Viewports);
            Assert.True(document.Undo());
            Assert.Empty(Viewports(document, "Layout1"));
        }

        [Fact]
        public void AViewportWithNoCentre_IsCentredOnWhatModelSpaceDraws()
        {
            CadDocument document = Plate();

            CadLayouts.AddViewport(document, "Layout1", new[] { 10.0, 10.0, 110.0, 70.0 }, "0.5", null, null);

            DxfViewport viewport = Assert.Single(Viewports(document, "Layout1"));
            Assert.Equal(50.0, viewport.ViewCenter.X, 6);
            Assert.Equal(30.0, viewport.ViewCenter.Y, 6);
            Assert.Equal(120.0, viewport.ViewHeight, 6);
        }

        [Fact]
        public void AViewportFreezesTheLayersNamedInItAlone()
        {
            CadDocument document = Plate();
            var frame = new[] { 0.0, 0.0, 100.0, 50.0 };

            string first = CadLayouts.AddViewport(document, "Layout1", frame, "1:5", null, new[] { "notes" });
            string second = CadLayouts.AddViewport(
                document,
                "Layout1",
                new[] { 120.0, 0.0, 220.0, 50.0 },
                "1:5",
                null,
                null
            );

            DxfViewport[] viewports = Viewports(document, "Layout1");
            Assert.Equal(2, viewports.Length);
            Assert.Contains(
                "Notes",
                viewports.First(v => v.Handle == first).GetFrozenLayers().Select(l => l.Name),
                StringComparer.OrdinalIgnoreCase
            );
            Assert.DoesNotContain(
                "Notes",
                viewports.First(v => v.Handle == second).GetFrozenLayers().Select(l => l.Name),
                StringComparer.OrdinalIgnoreCase
            );
        }

        [Fact]
        public void AViewport_RefusesWhatIsNotOne_AndLeavesTheDrawingAsItWas()
        {
            CadDocument document = Plate();
            var frame = new[] { 0.0, 0.0, 100.0, 50.0 };

            Assert.Throws<ArgumentException>(() =>
                CadLayouts.AddViewport(document, "Model", frame, "1:10", null, null)
            );
            Assert.Throws<ArgumentException>(() => CadLayouts.AddViewport(document, "Nope", frame, "1:10", null, null));
            Assert.Throws<ArgumentException>(() =>
                CadLayouts.AddViewport(document, "Layout1", new[] { 0.0, 0.0, 100.0, 0.0 }, "1:10", null, null)
            );
            Assert.Throws<ArgumentException>(() =>
                CadLayouts.AddViewport(document, "Layout1", new[] { 0.0, 0.0, 100.0 }, "1:10", null, null)
            );
            Assert.Throws<ArgumentException>(() =>
                CadLayouts.AddViewport(document, "Layout1", frame, "fit", null, null)
            );
            Assert.Throws<ArgumentException>(() =>
                CadLayouts.AddViewport(document, "Layout1", frame, "1:10", new[] { 1.0 }, null)
            );
            Assert.Throws<ArgumentException>(() =>
                CadLayouts.AddViewport(document, "Layout1", frame, "1:10", null, new[] { "Missing" })
            );
            Assert.Empty(Viewports(document, "Layout1"));
        }

        [Theory]
        [InlineData("1:50", 1.0, 50.0)]
        [InlineData("2:1", 2.0, 1.0)]
        [InlineData(" 1 : 2.5 ", 1.0, 2.5)]
        [InlineData("0.02", 0.02, 1.0)]
        public void AScale_IsReadAsPaperToDrawing(string text, double paper, double drawing)
        {
            CadLayouts.ParseScale(text, out double sheet, out double units);

            Assert.Equal(paper, sheet, 9);
            Assert.Equal(drawing, units, 9);
        }

        [Theory]
        [InlineData("")]
        [InlineData("1:0")]
        [InlineData("0:1")]
        [InlineData("-1")]
        [InlineData("1:2:3")]
        [InlineData("1e3")]
        public void AScale_IsRefusedWhereItIsNotNumbersAboveNought(string text) =>
            Assert.Throws<ArgumentException>(() => CadLayouts.ParseScale(text, out _, out _));

        [Theory]
        [InlineData(0.02, "1:50")]
        [InlineData(1.0, "1:1")]
        [InlineData(2.0, "2:1")]
        [InlineData(0.5, "1:2")]
        public void AScale_IsWrittenAsARatio(double scale, string text) =>
            Assert.Equal(text, CadLayouts.ScaleText(scale));
    }
}
