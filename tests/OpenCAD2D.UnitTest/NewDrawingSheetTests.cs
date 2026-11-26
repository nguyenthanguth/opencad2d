using System;
using System.Linq;
using GeometryHelper;
using GeometryHelper.Dxf;
using GeometryHelper.Geometry;
using Xunit;
using static OpenCAD2D.UnitTest.SpatialDrawings;

namespace OpenCAD2D.UnitTest
{
    /// <summary>
    /// The layouts of a new drawing: the metric template NEW starts from gives Layout1 as AutoCAD's acadiso.dwt does, an A3 sheet
    /// of 420 by 297 millimetres in millimetres, so that a length on it is a length in millimetres with nothing set up; a drawing
    /// read from a file keeps the units it was written in.
    /// </summary>
    public sealed class NewDrawingSheetTests : IDisposable
    {
        private const string Heights = "\"textHeights\":{\"paper\":[2.5,3.5],\"tolerance\":0.01}";

        private static readonly Tolerance Mm = new Tolerance(1E-3, 1E-5);

        private readonly SpatialDrawings _drawings = new SpatialDrawings();

        public void Dispose() => _drawings.Dispose();

        private static DxfLayout SheetOf(DxfDatabase database) => database.Layouts.Single(l => !l.ModelType);

        private static void AssertASheetOfMillimetres(DxfLayout layout)
        {
            Assert.Equal(DxfPlotPaperUnit.Millimeters, layout.PlotPaperUnits);
            Assert.Equal(420.0, layout.PlotPaperSize.X, 6);
            Assert.Equal(297.0, layout.PlotPaperSize.Y, 6);
            Assert.Equal("ISO_A3_(420.00_x_297.00_MM)", layout.CanonicalMediaName);
            Assert.Equal(DxfPlotType.Layout, layout.PlotType);
        }

        [Fact]
        public void TheMetricTemplatesLayout1_IsAnA3SheetInMillimetres_AndKeepsItsName()
        {
            DxfDatabase database = CadTemplate.CreateMetric();

            DxfLayout layout = SheetOf(database);

            Assert.Equal("Layout1", layout.LayoutName);
            AssertASheetOfMillimetres(layout);
        }

        [Fact]
        public void TheMillimetreSheet_IsGivenToEveryLayoutOfAnyDrawing_AndGivesTheSameAgain()
        {
            var database = new DxfDatabase("AC1027");
            Assert.Equal(DxfPlotPaperUnit.Inches, SheetOf(database).PlotPaperUnits);

            CadTemplate.UseMillimetreSheet(database);
            CadTemplate.UseMillimetreSheet(database);

            AssertASheetOfMillimetres(SheetOf(database));
            Assert.Equal(2, database.Layouts.Count());
        }

        [Fact]
        public void TheMillimetreSheet_RefusesNoDrawing()
        {
            Assert.Throws<ArgumentNullException>(() => CadTemplate.UseMillimetreSheet(null));
        }

        [Fact]
        public void TheImperialTemplatesLayout_IsStillInInches()
        {
            DxfDatabase database = CadTemplate.CreateImperial();

            Assert.Equal(DxfPlotPaperUnit.Inches, SheetOf(database).PlotPaperUnits);
        }

        [Fact]
        public void ALayoutOfADrawingMadeInCode_IsStillInches_AndNeverSetUp()
        {
            var database = new DxfDatabase("AC1027");

            Assert.Equal(25.4, CadUnitScale.OfPaper(SheetOf(database)), 9);
            Assert.Equal(0.0, SheetOf(database).PlotPaperSize.X, 9);
        }

        [Fact]
        public void TheLengthsOfANewDrawingsLayout_AreOneToOneInMillimetres_WithNothingSetUp()
        {
            DxfDatabase database = CadTemplate.CreateMetric();

            Assert.Equal(1.0, CadUnitScale.OfPaper(SheetOf(database)), 9);
        }

        [Fact]
        public void TheTextHeightRule_OnANewDrawingsLayout_MeasuresInMillimetres_WithNothingSetUp()
        {
            CadDocument document = _drawings.Blank();
            CadTemplate.UseMillimetreSheet(document.Database);
            CadSpace layout = document.Spaces.First(s => !s.IsModel);
            var words = new DxfDBText
            {
                TextString = "SIDE",
                Position = new GeoPoint3(0, 0, 0),
                Height = 4.0,
            };
            layout.Block.AppendEntity(words);
            words.TextStyleName = "OC_SHX";

            CadStandardResult result = CadStandards.Check(
                document,
                CadStandard.Parse("{\"schema\":\"opencad2d.standard\",\"version\":1," + Heights + "}"),
                0.0,
                Mm
            );

            CadViolation found = Assert.Single(result.Violations);
            Assert.Equal(words.Handle, found.Handle);
            Assert.Equal(4.0, found.Value, 9);
            Assert.Equal(3.5, found.Limit, 9);
        }

        [Fact]
        public void TheExportOfANewDrawingsLayout_HasAFactorOfOne_ToMillimetres()
        {
            DxfDatabase database = CadTemplate.CreateMetric();
            CadDocument document = CadDocument.From(database, "new.dxf");
            document
                .Spaces.First(s => !s.IsModel)
                .Block.AppendEntity(new DxfLine(new GeoPoint3(0, 0, 0), new GeoPoint3(100, 0, 0)));

            CadFlatDrawing flat = CadFlatten.Walk(document, "Layout1");

            Assert.Equal(1.0, flat.Layouts.Single().Scale, 9);
            Assert.Equal(1.0, flat.Entities.Single().Scale, 9);
        }

        [Fact]
        public void ADrawingReadFromAFile_KeepsTheUnitsOfItsLayouts()
        {
            var database = new DxfDatabase("AC1027");
            CadDocument written = CadDocument.From(database, "inches.dxf");
            DxfDatabase read = DxfDatabase.Parse(written.Database.ToText());

            Assert.Equal(DxfPlotPaperUnit.Inches, SheetOf(read).PlotPaperUnits);
        }
    }
}
