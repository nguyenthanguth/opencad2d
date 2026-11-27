using System;
using System.Collections.Generic;
using GeometryHelper.Dxf;
using GeometryHelper.Geometry;
using Xunit;

namespace OpenCAD2D.UnitTest
{
    /// <summary>
    /// The edits of <see cref="CadMake"/> that add and change multileader styles and table styles: each is one undo step, a refusal part
    /// way leaves no style behind, and only the settings given are changed.
    /// </summary>
    public class MakeStylesTests
    {
        private static CadDocument Blank() => CadDocument.From(new DxfDatabase("AC1027"), "styles.dxf");

        [Fact]
        public void AnAnnotativeMultileaderStyle_IsAddedWithItsSettings_InOneUndoStep()
        {
            CadDocument document = Blank();
            int before = document.UndoPosition;

            IReadOnlyList<string> made = CadMake.Run(
                document,
                "add_mleader_style",
                new CadMakeArgs()
                    .Set("name", "ANNO")
                    .Set("annotative", true)
                    .Set("text_height", 3.5)
                    .Set("arrow", "dot")
            );

            Assert.Equal("ANNO", Assert.Single(made));
            CadMLeaderStyleSettings settings = CadMLeaderStyleSettings.Of(
                CadMLeaderStyleEditor.Find(document.Database, "anno")
            );
            Assert.True(settings.Annotative);
            Assert.Equal(3.5, settings.TextHeight, 6);
            Assert.Equal("Dot", settings.Arrowhead);
            Assert.Equal(before + 1, document.UndoPosition);
            Assert.True(document.Undo());
            Assert.Null(CadMLeaderStyleEditor.Find(document.Database, "ANNO"));
        }

        [Fact]
        public void ASetOfAMultileaderStyle_ChangesWhatIsGivenAndNothingElse()
        {
            CadDocument document = Blank();
            CadMake.Run(
                document,
                "add_mleader_style",
                new CadMakeArgs().Set("name", "PLAN").Set("text_height", 3.5).Set("arrow_size", 4.0).Set("scale", 10.0)
            );

            CadMake.Run(
                document,
                "set_mleader_style",
                new CadMakeArgs().Set("name", "plan").Set("landing_gap", 1.25).Set("landing_length", 9.0)
            );

            CadMLeaderStyleSettings settings = CadMLeaderStyleSettings.Of(
                CadMLeaderStyleEditor.Find(document.Database, "PLAN")
            );
            Assert.Equal(1.25, settings.LandingGap, 6);
            Assert.Equal(9.0, settings.LandingDistance, 6);
            Assert.True(settings.SetLandingDistance);
            Assert.Equal(3.5, settings.TextHeight, 6);
            Assert.Equal(4.0, settings.ArrowSize, 6);
            Assert.Equal(10.0, settings.Scale, 6);
            Assert.False(settings.Annotative);
        }

        [Fact]
        public void AMultileaderStyleRefusedPartWay_LeavesNoStyleAndNoUndoStep()
        {
            CadDocument document = Blank();
            int before = document.UndoPosition;

            InvalidOperationException refused = Assert.Throws<InvalidOperationException>(() =>
                CadMake.Run(
                    document,
                    "add_mleader_style",
                    new CadMakeArgs().Set("name", "NEW").Set("text_style", "Nowhere")
                )
            );

            Assert.Contains("no text style", refused.Message);
            Assert.Null(CadMLeaderStyleEditor.Find(document.Database, "NEW"));
            Assert.Equal(before, document.UndoPosition);
            Assert.Throws<ArgumentException>(() =>
                CadMake.Run(
                    document,
                    "add_mleader_style",
                    new CadMakeArgs().Set("name", "NEW").Set("scale", 2.0).Set("annotative", true)
                )
            );
            Assert.Null(CadMLeaderStyleEditor.Find(document.Database, "NEW"));
        }

        [Fact]
        public void ATableStyle_IsAddedWithItsHeightsAndMargins_InOneUndoStep_AndSetChangesOnlyWhatIsGiven()
        {
            CadDocument document = Blank();
            int before = document.UndoPosition;

            CadMake.Run(
                document,
                "add_table_style",
                new CadMakeArgs()
                    .Set("name", "PLAN-TABLE")
                    .Set("title_height", 35.0)
                    .Set("header_height", 25.0)
                    .Set("data_height", 25.0)
                    .Set("horizontal_margin", 15.0)
                    .Set("vertical_margin", 12.0)
            );

            Assert.Equal(before + 1, document.UndoPosition);
            CadTableStyleSettings settings = CadTableStyleSettings.Of(
                CadTableStyleEditor.Find(document.Database, "PLAN-TABLE")
            );
            Assert.Equal(
                (35.0, 25.0, 25.0),
                (settings.Title.TextHeight, settings.Header.TextHeight, settings.Data.TextHeight)
            );
            Assert.Equal((15.0, 12.0), (settings.HorizontalMargin, settings.VerticalMargin));

            CadMake.Run(
                document,
                "set_table_style",
                new CadMakeArgs().Set("name", "plan-table").Set("data_height", 30.0)
            );

            settings = CadTableStyleSettings.Of(CadTableStyleEditor.Find(document.Database, "PLAN-TABLE"));
            Assert.Equal(
                (35.0, 25.0, 30.0),
                (settings.Title.TextHeight, settings.Header.TextHeight, settings.Data.TextHeight)
            );
            Assert.True(document.Undo());
            Assert.Equal(
                25.0,
                CadTableStyleSettings.Of(CadTableStyleEditor.Find(document.Database, "PLAN-TABLE")).Data.TextHeight,
                6
            );
            Assert.True(document.Undo());
            Assert.Null(CadTableStyleEditor.Find(document.Database, "PLAN-TABLE"));
        }

        [Fact]
        public void ANoteIsMeasuredInAStyleOfADrawingWithNoStandardMultileaderStyle()
        {
            CadDocument document = Blank();
            CadMake.Run(document, "add_mleader_style", new CadMakeArgs().Set("name", "PLAN").Set("scale", 10.0));

            CadMeasurement measured = CadMeasure.Leader(
                document,
                "PLAN",
                new GeoPoint2(0, 0),
                new GeoPoint2(100, 60),
                "NOTCH",
                10.0
            );

            Assert.InRange(measured.Frames[0].Height, 25.0, 40.0);
        }

        [Fact]
        public void ATableStyleRefusedPartWay_LeavesNoStyle()
        {
            CadDocument document = Blank();

            Assert.Throws<ArgumentException>(() =>
                CadMake.Run(document, "add_table_style", new CadMakeArgs().Set("name", "NEW").Set("data_height", 0.0))
            );
            Assert.Throws<InvalidOperationException>(() =>
                CadMake.Run(
                    document,
                    "add_table_style",
                    new CadMakeArgs().Set("name", "NEW").Set("text_style", "Nowhere")
                )
            );

            Assert.Null(CadTableStyleEditor.Find(document.Database, "NEW"));
        }
    }
}
