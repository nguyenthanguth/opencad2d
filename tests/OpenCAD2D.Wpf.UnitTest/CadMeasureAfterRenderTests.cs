using System;
using GeometryHelper.Dxf;
using GeometryHelper.Geometry;
using Xunit;

namespace OpenCAD2D.Wpf.UnitTest
{
    /// <summary>
    /// What the core measures of an MTEXT in an SHX style, as a drawing set up by <c>CadRender.Prepare</c> (the way the app sets
    /// one up on opening it) gives it, whether or not a picture was drawn before. They run here because <c>Prepare</c> sets the
    /// canvas's measure of TrueType letters for the whole process.
    /// </summary>
    public class CadMeasureAfterRenderTests
    {
        private static CadDocument NoteDrawing()
        {
            var database = new DxfDatabase("AC1027");
            database.TextStyleTable.Add(new DxfTextStyleTableRecord { Name = "NOTE", FileName = "romans.shx" });
            return CadDocument.From(database, "note.dxf");
        }

        private static DxfMText Words() =>
            new DxfMText
            {
                Contents = "Plate PL10 of grade S355 beside it",
                Location = new GeoPoint3(10, 80, 0),
                TextHeight = 4.0,
                Width = 50.0,
                TextStyleName = "NOTE",
            };

        private static double WidthPrepared()
        {
            CadDocument document = NoteDrawing();
            CadRender.Prepare(document);
            return CadMeasure.Of(document, Words()).Box.Width;
        }

        [Fact]
        public void AnMTextInAnShxStyle_OfAPreparedDrawing_IsTheSameWidthWhetherOrNotAPictureWasDrawnBefore()
        {
            CadTrueTypeFonts.MeasureWidth = null;
            double first = WidthPrepared();

            CadDocument other = NoteDrawing();
            other.Database.ModelSpace.AppendEntity(Words());
            CadRender.Prepare(other);

            double later = WidthPrepared();

            Assert.Equal(45.8, first, 6);
            Assert.Equal(first, later, 9);
        }

        [Fact]
        public void AnMTextInAnShxStyle_OfADrawingNotPrepared_IsNotTheAppsWidth_SoThatAReaderOfBoxesPreparesItFirst()
        {
            CadTrueTypeFonts.MeasureWidth = null;
            double guessed = CadMeasure.Of(NoteDrawing(), Words()).Box.Width;

            Assert.Equal(45.6, guessed, 6);
            Assert.NotEqual(WidthPrepared(), guessed, 3);
        }
    }
}
