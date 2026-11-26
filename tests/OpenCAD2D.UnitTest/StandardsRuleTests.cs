using System;
using System.Collections.Generic;
using System.Linq;
using GeometryHelper;
using GeometryHelper.Dxf;
using GeometryHelper.Geometry;
using Xunit;
using static OpenCAD2D.UnitTest.EditingDrawings;
using static OpenCAD2D.UnitTest.SpatialDrawings;

namespace OpenCAD2D.UnitTest
{
    /// <summary>
    /// The drawing standards checker (<see cref="CadStandards.Check"/>): each rule on a small drawing made in code, once with the
    /// fault, where the violation is found with the numbers it should have, and once mended, where the rule scores 1. Words are
    /// in an SHX font of the test's own (<see cref="SpatialDrawings"/>) so that their frames are the display's and the gaps
    /// can be set to the hundredth of a millimetre.
    /// </summary>
    public sealed class StandardsRuleTests : IDisposable
    {
        private static readonly Tolerance Mm = new Tolerance(1E-3, 1E-5);

        private readonly SpatialDrawings _drawings = new SpatialDrawings();

        public void Dispose() => _drawings.Dispose();

        private static CadStandard Standard(string members) =>
            CadStandard.Parse("{\"schema\":\"opencad2d.standard\",\"version\":1," + members + "}");

        private static CadStandardResult Check(CadDocument document, string members, double modelScale = 0.0) =>
            CadStandards.Check(document, Standard(members), modelScale, Mm);

        private static CadViolation Only(CadStandardResult result) => Assert.Single(result.Violations);

        private static void AddLayers(CadDocument document, params string[] names)
        {
            foreach (string name in names)
            {
                document.Database.LayerTable.Add(new DxfLayerTableRecord { Name = name });
            }
        }

        // ---- the layers ---------------------------------------------------------------------------------------------

        [Fact]
        public void LayerName_FindsTheObjectOnALayerTheStandardDoesNotListWithItsBox_AndMendedScoresOne()
        {
            const string rules = "\"layers\":{\"allowed\":[{\"name\":\"WALL*\"}],\"allowLayer0\":true}";
            CadDocument document = _drawings.Blank();
            AddLayers(document, "WALL-1", "JUNK");
            DxfLine good = Put(document, Line(0, 0, 10, 0));
            good.Layer = "WALL-1";
            DxfLine bad = Put(document, Line(0, 5, 10, 5));
            bad.Layer = "JUNK";

            CadStandardResult result = Check(document, rules);

            CadViolation found = Only(result);
            Assert.Equal("layer-name", found.Rule);
            Assert.Equal(bad.Handle, found.Handle);
            Assert.Null(found.Other);
            Assert.Equal("Model", found.Space);
            Assert.Equal("JUNK", found.Measured);
            Assert.Equal("WALL*", found.Allowed);
            Assert.Equal(new[] { 0.0, 5.0, 10.0, 5.0 }, found.Box);
            Assert.Equal(0.5, result.ScoreOf("layer-name"), 9);
            Assert.Equal(0.5, result.Total, 9);

            bad.Layer = "WALL-1";
            CadStandardResult mended = Check(document, rules);

            Assert.Empty(mended.Violations);
            Assert.Equal(1.0, mended.ScoreOf("layer-name"), 9);
            Assert.Equal(1.0, mended.Total, 9);
        }

        [Fact]
        public void LayerProperties_FindsAnAllowedLayerWhoseColourAndWeightDiffer_OneViolationEach_AndMendedScoresOne()
        {
            const string rules =
                "\"layers\":{\"allowed\":[{\"name\":\"A-WALL\",\"color\":3,\"linetype\":\"Continuous\",\"lineweight\":0.35},"
                + "{\"name\":\"A-DOOR\",\"color\":\"#00FF00\"}],\"allowOthers\":true}";
            CadDocument document = _drawings.Blank();
            var wall = new DxfLayerTableRecord
            {
                Name = "A-WALL",
                Color = DxfColor.FromColorIndex(DxfColorMethod.ByAci, 1),
                LineWeight = DxfLineWeight.LineWeight025,
            };
            document.Database.LayerTable.Add(wall);
            var door = new DxfLayerTableRecord { Name = "A-DOOR", Color = DxfColor.FromRgb(0, 255, 0) };
            document.Database.LayerTable.Add(door);

            CadStandardResult result = Check(document, rules);

            Assert.Equal(2, result.Violations.Count);
            Assert.All(result.Violations, v => Assert.Equal(wall.Handle, v.Handle));
            Assert.Equal(new[] { "color 1", "lineweight 0.25 mm" }, result.Violations.Select(v => v.Measured));
            Assert.Equal(new[] { "color 3", "lineweight 0.35 mm" }, result.Violations.Select(v => v.Allowed));
            Assert.Null(result.Violations[0].Box);
            Assert.True(double.IsNaN(result.Boxes[0]));
            CadRuleScore score = Assert.Single(result.Rules, r => r.Rule == "layer-properties");
            Assert.Equal(2, score.Subjects);
            Assert.Equal(1, score.Offenders);
            Assert.Equal(0.5, score.Score, 9);

            wall.Color = DxfColor.FromColorIndex(DxfColorMethod.ByAci, 3);
            wall.LineWeight = DxfLineWeight.LineWeight035;

            Assert.Empty(Check(document, rules).Violations);
            Assert.Equal(1.0, Check(document, rules).ScoreOf("layer-properties"), 9);
        }

        [Fact]
        public void LayerZero_FindsTheObjectOnLayerZero_AndMovedOffItScoresOne()
        {
            const string rules = "\"layers\":{\"allowLayer0\":false}";
            CadDocument document = _drawings.Blank();
            AddLayers(document, "WALL-1");
            DxfLine onZero = Put(document, Line(0, 0, 10, 0));
            DxfLine fine = Put(document, Line(0, 5, 10, 5));
            fine.Layer = "WALL-1";

            CadStandardResult result = Check(document, rules);

            CadViolation found = Only(result);
            Assert.Equal("layer-zero", found.Rule);
            Assert.Equal(onZero.Handle, found.Handle);
            Assert.Equal("0", found.Measured);
            Assert.Equal(0.5, result.ScoreOf("layer-zero"), 9);

            onZero.Layer = "WALL-1";

            Assert.Equal(1.0, Check(document, rules).ScoreOf("layer-zero"), 9);
        }

        [Fact]
        public void ByLayer_FindsAColourALinetypeAndALineweightOfAnObjectsOwn_EachByItsRule()
        {
            const string rules = "\"byLayer\":{\"color\":true,\"linetype\":true,\"lineweight\":true}";
            CadDocument document = _drawings.Blank();
            DxfLine coloured = Put(document, Line(0, 0, 10, 0));
            coloured.ColorIndex = 1;
            DxfLine blocky = Put(document, Line(0, 5, 10, 5));
            blocky.Linetype = "ByBlock";
            DxfLine heavy = Put(document, Line(0, 10, 10, 10));
            heavy.LineWeight = DxfLineWeight.LineWeight025;
            Put(document, Line(0, 15, 10, 15));

            CadStandardResult result = Check(document, rules);

            Assert.Equal(3, result.Violations.Count);
            CadViolation color = Assert.Single(result.Violations, v => v.Rule == "by-layer-color");
            CadViolation linetype = Assert.Single(result.Violations, v => v.Rule == "by-layer-linetype");
            CadViolation weight = Assert.Single(result.Violations, v => v.Rule == "by-layer-lineweight");
            Assert.Equal((coloured.Handle, "1"), (color.Handle, color.Measured));
            Assert.Equal((blocky.Handle, "ByBlock"), (linetype.Handle, linetype.Measured));
            Assert.Equal((heavy.Handle, "0.25 mm"), (weight.Handle, weight.Measured));
            Assert.All(result.Rules, r => Assert.Equal(0.75, r.Score, 9));

            coloured.ColorIndex = 256;
            blocky.Linetype = "ByLayer";
            heavy.LineWeight = DxfLineWeight.ByLayer;

            CadStandardResult mended = Check(document, rules);

            Assert.Empty(mended.Violations);
            Assert.All(mended.Rules, r => Assert.Equal(1.0, r.Score, 9));
        }

        // ---- the styles ---------------------------------------------------------------------------------------------

        [Fact]
        public void TextStyle_FindsATextInAStyleTheStandardDoesNotAllow_AndTextFontAnAllowedStyleInAnotherFont()
        {
            const string rules = "\"textStyles\":[{\"name\":\"NOTE\",\"font\":\"simplex.shx\"}]";
            CadDocument document = _drawings.Blank();
            var note = new DxfTextStyleTableRecord { Name = "NOTE", FileName = "txt.shx" };
            document.Database.TextStyleTable.Add(note);
            document.Database.TextStyleTable.Add(
                new DxfTextStyleTableRecord { Name = "SCRATCH", FileName = "txt.shx" }
            );
            DxfDBText inNote = Put(document, Text("SIDE", 0, 0));
            inNote.TextStyleName = "NOTE";
            DxfDBText inScratch = Put(document, Text("FOOD", 0, 20));
            inScratch.TextStyleName = "SCRATCH";

            CadStandardResult result = Check(document, rules);

            Assert.Equal(2, result.Violations.Count);
            CadViolation style = Assert.Single(result.Violations, v => v.Rule == "text-style");
            Assert.Equal(inScratch.Handle, style.Handle);
            Assert.Equal("SCRATCH", style.Measured);
            Assert.Equal("NOTE", style.Allowed);
            CadViolation font = Assert.Single(result.Violations, v => v.Rule == "text-font");
            Assert.Equal(note.Handle, font.Handle);
            Assert.Equal("txt.shx", font.Measured);
            Assert.Equal("simplex.shx", font.Allowed);
            Assert.Equal(0.5, result.ScoreOf("text-style"), 9);
            Assert.Equal(0.0, result.ScoreOf("text-font"), 9);

            inScratch.TextStyleName = "NOTE";
            note.FileName = "SIMPLEX";

            CadStandardResult mended = Check(document, rules);

            Assert.Empty(mended.Violations);
            Assert.Equal(1.0, mended.Total, 9);
        }

        [Fact]
        public void DimStyle_FindsADimensionInAStyleNotAllowed_AndDimFontAnAllowedStyleWhoseTextStyleHasAnotherFont()
        {
            const string rules = "\"dimStyles\":[{\"name\":\"COMPANY\",\"font\":\"romans.shx\"}]";
            CadDocument document = _drawings.Blank();
            DxfDatabase db = document.Database;
            var plain = new DxfTextStyleTableRecord { Name = "PLAIN", FileName = "txt.shx" };
            var roman = new DxfTextStyleTableRecord { Name = "ROMAN", FileName = "romans.shx" };
            db.TextStyleTable.Add(plain);
            db.TextStyleTable.Add(roman);
            var company = new DxfDimStyleTableRecord { Name = "COMPANY" };
            db.DimStyleTable.Add(company);
            company.Dimtxsty = plain;
            CadCommandSession session = Session(document);
            Run(session, "DIMLINEAR", P(0, 0), P(100, 0), P(50, -20));
            Run(session, "DIMLINEAR", P(100, 0), P(200, 0), P(150, -20));
            DxfDimension[] dimensions = db.ModelSpace.OfType<DxfDimension>().ToArray();
            Assert.Equal(2, dimensions.Length);
            dimensions[1].DimensionStyleName = "COMPANY";

            CadStandardResult result = Check(document, rules);

            Assert.Equal(2, result.Violations.Count);
            CadViolation style = Assert.Single(result.Violations, v => v.Rule == "dim-style");
            Assert.Equal(dimensions[0].Handle, style.Handle);
            Assert.Equal("ISO-25", style.Measured);
            CadViolation font = Assert.Single(result.Violations, v => v.Rule == "dim-font");
            Assert.Equal(company.Handle, font.Handle);
            Assert.Equal("txt.shx", font.Measured);
            Assert.Equal("romans.shx", font.Allowed);

            dimensions[0].DimensionStyleName = "COMPANY";
            company.Dimtxsty = roman;

            Assert.Equal(1.0, Check(document, rules).Total, 9);
        }

        // ---- the heights of words on paper ----------------------------------------------------------------------------

        private const string Heights = "\"textHeights\":{\"paper\":[2.5,3.5,5,7],\"tolerance\":0.01}";

        [Fact]
        public void TextHeight_OfModelSpaceWithNoViewport_IsMeasuredAtOneToOne_TheNearestListedHeightIsTheLimit()
        {
            CadDocument document = _drawings.Blank();
            Put(document, Text("SIDE", 0, 0, 0.0, 2.5));
            DxfDBText off = Put(document, Text("FOOD", 0, 20, 0.0, 3.1));
            Put(document, Text("SIDE FOOD", 0, 40, 0.0, 5.005));

            CadStandardResult result = Check(document, Heights);

            CadViolation found = Only(result);
            Assert.Equal("text-height", found.Rule);
            Assert.Equal(off.Handle, found.Handle);
            Assert.Equal(3.1, found.Value, 9);
            Assert.Equal(3.5, found.Limit, 9);
            Assert.Equal("3.1 mm", found.Measured);
            Assert.Equal("2.5, 3.5, 5, 7 mm", found.Allowed);
            Assert.Null(found.Through);
            Assert.NotNull(found.Box);
            CadRuleScore score = Assert.Single(result.Rules);
            Assert.Equal((3, 1), (score.Subjects, score.Offenders));
            Assert.Equal(2.0 / 3.0, score.Score, 9);

            off.Height = 3.5;
            CadDocument mended = _drawings.Blank();
            Put(mended, Text("SIDE", 0, 0, 0.0, 2.5));
            Put(mended, Text("FOOD", 0, 20, 0.0, 3.5));

            Assert.Equal(1.0, Check(mended, Heights).Total, 9);
        }

        [Fact]
        public void TextHeight_AtAGivenModelScale_IsTheHeightOnPaperAtThatScale()
        {
            CadDocument document = _drawings.Blank();
            Put(document, Text("SIDE", 0, 0, 0.0, 250.0));
            DxfDBText off = Put(document, Text("FOOD", 0, 400, 0.0, 310.0));

            CadStandardResult result = Check(document, Heights, 0.01);

            CadViolation found = Only(result);
            Assert.Equal(off.Handle, found.Handle);
            Assert.Equal(3.1, found.Value, 9);
            Assert.Equal(3.5, found.Limit, 9);
            Assert.Equal(2, Assert.Single(result.Rules).Subjects);
        }

        /// <summary>A drawing whose layout holds a viewport 100 by 100 showing 10 000 of model space about (10, 10): a scale of 0.01.</summary>
        private CadDocument Sheet(out DxfViewport viewport, double viewHeight = 10000.0)
        {
            CadDocument document = _drawings.Blank();
            CadSpace layout = document.Spaces.First(s => !s.IsModel);
            layout.Layout.PlotPaperUnits = DxfPlotPaperUnit.Millimeters;
            viewport = new DxfViewport
            {
                CenterPoint = new GeoPoint3(100, 100, 0),
                Width = 100,
                Height = 100,
            };
            layout.Block.AppendEntity(viewport);
            viewport.ViewCenter = new GeoPoint2(10, 10);
            viewport.ViewHeight = viewHeight;
            viewport.On = true;
            return document;
        }

        [Fact]
        public void TextHeight_ThroughAViewport_IsTheHeightTimesTheViewportsScale_AndWhatItDoesNotShowIsNotChecked()
        {
            CadDocument document = Sheet(out DxfViewport viewport);
            Put(document, Text("SIDE", 0, 0, 0.0, 250.0));
            DxfDBText off = Put(document, Text("FOOD", 0, 400, 0.0, 310.0));
            Put(document, Text("DISE", 100000, 100000, 0.0, 999.0));

            CadStandardResult result = Check(document, Heights);

            CadViolation found = Only(result);
            Assert.Equal(off.Handle, found.Handle);
            Assert.Equal("Model", found.Space);
            Assert.Equal(viewport.Handle, found.Through);
            Assert.Equal(3.1, found.Value, 9);
            CadRuleScore score = Assert.Single(result.Rules);
            Assert.Equal(2, score.Subjects);
        }

        [Fact]
        public void TextHeight_OnALayoutsOwnPaper_IsTheHeightAsDrawn()
        {
            CadDocument document = _drawings.Blank();
            CadSpace layout = document.Spaces.First(s => !s.IsModel);
            layout.Layout.PlotPaperUnits = DxfPlotPaperUnit.Millimeters;
            var words = new DxfDBText
            {
                TextString = "SIDE",
                Position = new GeoPoint3(0, 0, 0),
                Height = 4.0,
            };
            layout.Block.AppendEntity(words);
            words.TextStyleName = "OC_SHX";

            CadViolation found = Only(Check(document, Heights));

            Assert.Equal(words.Handle, found.Handle);
            Assert.Equal(layout.Name, found.Space);
            Assert.Equal(4.0, found.Value, 9);
            Assert.Equal(3.5, found.Limit, 9);
        }

        [Fact]
        public void TextHeight_OfAnAnnotativeText_IsTheSizeItWasMadeAt_WhateverTheModelScaleAsked()
        {
            CadDocument document = _drawings.Blank();
            DxfObjectContextCollection scales = document.Database.ObjectContextManager.GetContextCollection(
                "ACDB_ANNOTATIONSCALES"
            );
            foreach (double each in new[] { 1.0, 50.0 })
            {
                scales.AddContext(
                    new DxfAnnotationScale
                    {
                        Name = "1:" + each.ToString("0"),
                        PaperUnits = 1,
                        DrawingUnits = each,
                    }
                );
            }

            DxfDBText note = Put(document, Text("SIDE", 0, 0, 0.0, 2.5));
            note.Annotative = DxfAnnotativeStates.True;
            note.AddContext((DxfAnnotationScale)scales.GetContext("1:50"));
            document.Database.Cannoscale = "1:50";
            DxfDBText plain = Put(document, Text("FOOD", 0, 400, 0.0, 125.0));

            Assert.Equal(
                125.0,
                document.GetDisplay(document.ModelSpace).Items.OfType<DisplayText2>().First().Height,
                9
            );
            Assert.Empty(Check(document, Heights, 0.02).Violations);

            // At 1:100 the annotative text is still 2.5 on paper, and the plain one, 125 in the model, is 1.25.
            CadViolation found = Only(Check(document, Heights, 0.01));
            Assert.Equal(plain.Handle, found.Handle);
            Assert.Equal(1.25, found.Value, 9);
            Assert.Equal(2.5, found.Limit, 9);
        }

        [Fact]
        public void TextHeight_AtTheToleranceFromAListedHeight_Passes_AndAHairMoreFails()
        {
            CadDocument document = _drawings.Blank();
            Put(document, Text("SIDE", 0, 0, 0.0, 2.51));
            Put(document, Text("FOOD", 0, 20, 0.0, 3.49));
            DxfDBText over = Put(document, Text("SIDE FOOD", 0, 40, 0.0, 5.0101));

            CadViolation found = Only(Check(document, Heights));

            Assert.Equal(over.Handle, found.Handle);
            Assert.Equal(5.0101, found.Value, 9);
            Assert.Equal(5.0, found.Limit, 9);
        }

        // ---- the gaps around words ------------------------------------------------------------------------------------

        [Fact]
        public void GapWordLine_FindsALineNearerThanTheGapAllows_WithTheGapMeasured_AndAFarOneIsNone()
        {
            const string rules = "\"gaps\":{\"wordLine\":1.0}";
            CadTextFrame frame = _drawings.FrameOf("SIDE FOOD", 10, 20);
            double top = frame.Y + frame.Y1;
            double right = frame.X + frame.X1;
            CadDocument document = _drawings.Blank();
            DxfDBText words = Put(document, Text("SIDE FOOD", 10, 20));
            DxfLine near = Put(document, Line(0, top + 0.4, 100, top + 0.4));
            Put(document, Line(0, top + 1.5, 100, top + 1.5));
            Put(document, Line(right + 30, 0, right + 30, 100));

            CadStandardResult result = Check(document, rules);

            CadViolation found = Only(result);
            Assert.Equal("gap-word-line", found.Rule);
            Assert.Equal(words.Handle, found.Handle);
            Assert.Equal(near.Handle, found.Other);
            Assert.Equal(0.4, found.Value, 6);
            Assert.Equal(1.0, found.Limit, 9);
            Assert.Equal("at least 1 mm", found.Allowed);
            Assert.Equal(frame.X + frame.X0, found.Box[0], 6);
            Assert.Equal(top + 0.4, found.Box[3], 6);
            Assert.Equal(0.0, result.ScoreOf("gap-word-line"), 9);

            CadDocument mended = _drawings.Blank();
            Put(mended, Text("SIDE FOOD", 10, 20));
            Put(mended, Line(0, top + 1.2, 100, top + 1.2));

            Assert.Equal(1.0, Check(mended, rules).Total, 9);
        }

        [Fact]
        public void GapWordLine_ALineThroughTheWordIsAGapOfNought_AndTheDimensionsOwnLinesAreNot()
        {
            const string rules = "\"gaps\":{\"wordLine\":0.5}";
            CadTextFrame frame = _drawings.FrameOf("SIDE FOOD", 10, 20);
            double across = frame.X + (frame.X0 + frame.X1) / 2.0;
            CadDocument document = _drawings.Blank();
            Put(document, Text("SIDE FOOD", 10, 20));
            DxfLine through = Put(document, Line(across, 0, across, 60));
            Run(Session(document), "DIMLINEAR", P(0, 200), P(100, 200), P(50, 180));

            CadViolation found = Only(Check(document, rules));

            Assert.Equal(through.Handle, found.Other);
            Assert.Equal(0.0, found.Value, 9);
        }

        [Fact]
        public void GapWordLine_ThroughAViewport_IsMeasuredOnPaper_ThePictureAtOneToTwoIsTighterThanAtOneToOne()
        {
            const string rules = "\"gaps\":{\"wordLine\":0.8}";
            CadTextFrame frame = _drawings.FrameOf("SIDE FOOD", 10, 20);
            double top = frame.Y + frame.Y1;

            // One millimetre of model between the word and the line: 1 mm on paper at 1:1, 0.5 mm at a scale of 0.5.
            CadDocument half = Sheet(out DxfViewport viewport, 200.0);
            DxfDBText words = Put(half, Text("SIDE FOOD", 10, 20));
            DxfLine line = Put(half, Line(0, top + 1.0, 100, top + 1.0));

            CadViolation found = Only(Check(half, rules));
            Assert.Equal(words.Handle, found.Handle);
            Assert.Equal(line.Handle, found.Other);
            Assert.Equal(viewport.Handle, found.Through);
            Assert.Equal(0.5, found.Value, 6);

            CadDocument whole = Sheet(out _, 100.0);
            Put(whole, Text("SIDE FOOD", 10, 20));
            Put(whole, Line(0, top + 1.0, 100, top + 1.0));

            Assert.Empty(Check(whole, rules).Violations);
        }

        [Fact]
        public void GapWordLine_AtTheGapWithinTheTolerance_Passes_AndJustUnderItFails()
        {
            const string rules = "\"gaps\":{\"wordLine\":1.0}";
            CadTextFrame frame = _drawings.FrameOf("SIDE FOOD", 10, 20);
            double top = frame.Y + frame.Y1;
            CadDocument CheckedAt(double gap)
            {
                CadDocument document = _drawings.Blank();
                Put(document, Text("SIDE FOOD", 10, 20));
                Put(document, Line(0, top + gap, 100, top + gap));
                return document;
            }

            Assert.Empty(Check(CheckedAt(1.0), rules).Violations);
            Assert.Empty(Check(CheckedAt(0.9995), rules).Violations);
            CadViolation under = Only(Check(CheckedAt(0.998), rules));
            Assert.Equal(0.998, under.Value, 6);
        }

        [Fact]
        public void GapWordLine_OfATurnedWord_IsToItsFrameAndNotToTheBoxOfIt_AndAnArcIsMeasuredToo()
        {
            const string rules = "\"gaps\":{\"wordLine\":1.0}";
            CadTextFrame frame = _drawings.FrameOf("SIDE FOOD", 0, 0, 45.0, 5.0);
            DisplayBox box = frame.Box();
            CadDocument document = _drawings.Blank();
            Put(document, Text("SIDE FOOD", 0, 0, 45.0, 5.0));

            // A level stroke at the empty top left corner of the box, a long way from the frame the word is turned in.
            Put(document, Line(box.MinX, box.MaxY, box.MinX + 3, box.MaxY));
            Assert.Empty(Check(document, rules).Violations);

            // A circle under the lowest corner of the frame, its edge 0.4 mm from it.
            frame.Corner(0, 0.0, out double cx, out double cy);
            CadDocument arced = _drawings.Blank();
            DxfDBText turned = Put(arced, Text("SIDE FOOD", 0, 0, 45.0, 5.0));
            DxfCircle circle = Put(arced, Circle(cx, cy - 3.4, 3.0));

            CadViolation found = Only(Check(arced, rules));
            Assert.Equal(turned.Handle, found.Handle);
            Assert.Equal(circle.Handle, found.Other);
            Assert.Equal(0.4, found.Value, 2);
        }

        [Fact]
        public void TextHeight_OfAWordTwoViewportsShow_IsReportedAtTheWorseAndThroughThatOne()
        {
            CadDocument document = Sheet(out DxfViewport first);
            CadSpace layout = document.Spaces.First(s => !s.IsModel);
            var second = new DxfViewport
            {
                CenterPoint = new GeoPoint3(300, 100, 0),
                Width = 100,
                Height = 100,
            };
            layout.Block.AppendEntity(second);
            second.ViewCenter = new GeoPoint2(10, 10);
            second.ViewHeight = 5000.0;
            second.On = true;
            DxfDBText words = Put(document, Text("SIDE", 0, 0, 0.0, 150.0));

            // At 0.01 the word is 1.5 mm (1.0 from 2.5), at 0.02 it is 3.0 mm (0.5 from 2.5 or 3.5): the first is the worse.
            CadViolation found = Only(Check(document, Heights));

            Assert.Equal(words.Handle, found.Handle);
            Assert.Equal(first.Handle, found.Through);
            Assert.Equal(1.5, found.Value, 9);
            Assert.Equal(2.5, found.Limit, 9);
            Assert.Equal(1, Assert.Single(Check(document, Heights).Rules).Offenders);
        }

        [Fact]
        public void GapWordWord_FindsTwoWordsNearerThanTheGapAllows_TheFirstDrawnNamedFirst()
        {
            const string rules = "\"gaps\":{\"wordWord\":1.0}";
            CadTextFrame frame = _drawings.FrameOf("SIDE FOOD", 0, 0);
            double right = frame.X + frame.X1;
            CadDocument document = _drawings.Blank();
            DxfDBText first = Put(document, Text("SIDE FOOD", 0, 0));
            DxfDBText second = Put(document, Text("SIDE", right + 0.3, 0));

            CadStandardResult result = Check(document, rules);

            CadViolation found = Only(result);
            Assert.Equal("gap-word-word", found.Rule);
            Assert.Equal(first.Handle, found.Handle);
            Assert.Equal(second.Handle, found.Other);
            Assert.Equal(0.3, found.Value, 6);
            Assert.Equal(0.0, result.ScoreOf("gap-word-word"), 9);

            second.Position = new GeoPoint3(right + 2.0, 0, 0);
            CadDocument mended = _drawings.Blank();
            Put(mended, Text("SIDE FOOD", 0, 0));
            Put(mended, Text("SIDE", right + 2.0, 0));

            Assert.Equal(1.0, Check(mended, rules).Total, 9);
        }

        // ---- the dimension chains -------------------------------------------------------------------------------------

        private CadDocument Chain(double third, out string[] handles)
        {
            CadDocument document = _drawings.Blank();
            CadCommandSession session = Session(document);
            Run(session, "DIMLINEAR", P(0, 0), P(100, 0), P(50, -20));
            Run(session, "DIMLINEAR", P(100, 0), P(200, 0), P(150, -20));
            Run(session, "DIMLINEAR", P(200, 0), P(300, 0), P(250, third));
            Run(session, "DIMLINEAR", P(500, 0), P(600, 0), P(550, -25));
            handles = document.Database.ModelSpace.OfType<DxfDimension>().Select(d => d.Handle).ToArray();
            return document;
        }

        [Fact]
        public void DimChain_FindsTheDimensionLineThatLiesOffTheLineOfItsChain_AndOneOfAnotherRowIsNoChain()
        {
            const string rules = "\"dimensionChains\":{\"tolerance\":0.5,\"reach\":10}";
            CadDocument document = Chain(-21.0, out string[] handles);
            Assert.Equal(4, handles.Length);

            CadStandardResult result = Check(document, rules);

            CadViolation found = Only(result);
            Assert.Equal("dim-chain", found.Rule);
            Assert.Equal(handles[2], found.Handle);
            Assert.Equal(handles[0], found.Other);
            Assert.Equal(1.0, found.Value, 6);
            Assert.Equal(0.5, found.Limit, 9);
            CadRuleScore score = Assert.Single(result.Rules);
            Assert.Equal((3, 1), (score.Subjects, score.Offenders));
            Assert.Equal(2.0 / 3.0, score.Score, 9);

            CadDocument mended = Chain(-20.3, out _);

            Assert.Empty(Check(mended, rules).Violations);
            Assert.Equal(1.0, Check(mended, rules).ScoreOf("dim-chain"), 9);
        }

        // ---- the scores -----------------------------------------------------------------------------------------------

        [Fact]
        public void TheTotal_IsTheWeightedMeanOfTheRulesRun_AWeightOfNoughtLeavesARuleOut_AndNoRuleScoresOne()
        {
            CadDocument document = _drawings.Blank();
            AddLayers(document, "WALL-1");
            DxfLine onZero = Put(document, Line(0, 0, 10, 0));
            DxfLine coloured = Put(document, Line(0, 5, 10, 5));
            coloured.Layer = "WALL-1";
            coloured.ColorIndex = 1;
            DxfLine fine = Put(document, Line(0, 10, 10, 10));
            fine.Layer = "WALL-1";
            Assert.NotNull(onZero);
            const string members =
                "\"layers\":{\"allowLayer0\":false},\"byLayer\":{\"color\":true},"
                + "\"weights\":{\"layer-zero\":3,\"by-layer-color\":1}";

            CadStandardResult result = Check(document, members);

            // layer-zero: 1 of 3 offends, 2/3; by-layer-color: 1 of 3, 2/3. The weights 3 and 1 make no difference to equal scores,
            Assert.Equal(2.0 / 3.0, result.Total, 9);
            Assert.Equal(new[] { 3.0, 1.0 }, result.Rules.Select(r => r.Weight));

            // so make them unequal: put the coloured line's colour right and the weight of layer-zero 3 against 1.
            coloured.ColorIndex = 256;
            CadStandardResult unequal = Check(document, members);
            Assert.Equal((3 * (2.0 / 3.0) + 1 * 1.0) / 4.0, unequal.Total, 9);

            CadStandardResult left = Check(
                document,
                "\"layers\":{\"allowLayer0\":false},\"byLayer\":{\"color\":true},\"weights\":{\"layer-zero\":0}"
            );
            Assert.Equal(1.0, left.Total, 9);
            Assert.Equal(2, left.Rules.Count);

            CadStandardResult none = Check(document, "\"name\":\"empty\"");
            Assert.Empty(none.Rules);
            Assert.Equal(1.0, none.Total, 9);
            Assert.Equal("empty", none.Name);
        }

        [Fact]
        public void ADrawingScoredBeforeAndAfterAFix_ClimbsFromItsFaultsToOne()
        {
            const string rules =
                "\"layers\":{\"allowed\":[{\"name\":\"A-*\"}],\"allowLayer0\":false},\"byLayer\":{\"color\":true},"
                + "\"textHeights\":{\"paper\":[2.5,3.5]},\"gaps\":{\"wordLine\":1,\"wordWord\":1}";
            CadTextFrame frame = _drawings.FrameOf("SIDE FOOD", 0, 0, 0.0, 2.5);
            double top = frame.Y + frame.Y1;

            CadDocument Drawing(bool mended)
            {
                CadDocument document = _drawings.Blank();
                AddLayers(document, "A-TEXT", "A-LINE", "JUNK");
                DxfDBText words = Put(document, Text("SIDE FOOD", 0, 0, 0.0, mended ? 2.5 : 4.0));
                words.Layer = mended ? "A-TEXT" : "JUNK";
                DxfLine line = Put(document, Line(-5, top + (mended ? 3.0 : 0.2), 40, top + (mended ? 3.0 : 0.2)));
                line.Layer = "A-LINE";
                line.ColorIndex = mended ? 256 : 5;
                return document;
            }

            CadStandardResult before = Check(Drawing(false), rules);
            CadStandardResult after = Check(Drawing(true), rules);

            // layer-name 1 of 2 offends (the word on JUNK), by-layer-color 1 of 2, text-height 1 of 1, gap-word-line 1 of 1 (the
            // text-height rule's word is measured at 4 mm, whose frame is bigger, so the line is nearer still).
            Assert.Equal(0.5, before.ScoreOf("layer-name"), 9);
            Assert.Equal(0.5, before.ScoreOf("by-layer-color"), 9);
            Assert.Equal(0.0, before.ScoreOf("text-height"), 9);
            Assert.Equal(0.0, before.ScoreOf("gap-word-line"), 9);
            Assert.Equal(1.0, before.ScoreOf("layer-zero"), 9);
            Assert.Equal(0.5, before.Total, 9);
            Assert.NotEmpty(before.Violations);
            Assert.Empty(after.Violations);
            Assert.Equal(1.0, after.Total, 9);
        }

        [Fact]
        public void TheResult_IsTheSameEachTimeAndInTheOrderOfRuleThenHandle()
        {
            const string rules =
                "\"layers\":{\"allowed\":[{\"name\":\"A-*\"}],\"allowLayer0\":false},\"byLayer\":{\"color\":true},"
                + "\"textHeights\":{\"paper\":[2.5]}";
            CadDocument document = _drawings.Blank();

            for (int i = 0; i < 40; i++)
            {
                DxfDBText text = Put(document, Text("SIDE", i * 30, 0, 0.0, 1.0 + i % 4));
                text.ColorIndex = 1 + i % 3;
            }

            CadStandardResult a = Check(document, rules);
            CadStandardResult b = Check(document, rules);

            Assert.True(a.Violations.Count > 100);
            Assert.Equal(
                a.Violations.Select(v => v.Rule + "|" + v.Handle + "|" + v.Measured),
                b.Violations.Select(v => v.Rule + "|" + v.Handle + "|" + v.Measured)
            );
            Assert.Equal(a.Boxes, b.Boxes);
            string[] order = CadStandards.RuleIds.ToArray();

            for (int i = 1; i < a.Violations.Count; i++)
            {
                CadViolation before = a.Violations[i - 1];
                CadViolation after = a.Violations[i];
                int byRule = Array.IndexOf(order, before.Rule).CompareTo(Array.IndexOf(order, after.Rule));
                Assert.True(
                    byRule < 0
                        || (byRule == 0 && Convert.ToInt64(before.Handle, 16) <= Convert.ToInt64(after.Handle, 16))
                );
            }
        }

        [Fact]
        public void ARuleIsRunOnlyWhereTheStandardAsksForIt_AndAnIdThatIsNoRuleIsRefused()
        {
            CadStandard standard = Standard("\"gaps\":{\"wordLine\":0.5},\"textHeights\":{\"paper\":[2.5]}");

            Assert.Equal(new[] { "text-height", "gap-word-line" }, CadStandards.RuleIds.Where(standard.Runs));
            Assert.Equal(1.0, standard.WeightOf("text-height"));
            Assert.Throws<ArgumentException>(() => standard.Runs("no-such-rule"));
            Assert.Throws<ArgumentException>(() => standard.WeightOf("no-such-rule"));
            Assert.Throws<ArgumentNullException>(() => CadStandards.Check(null, standard, 0.0, Mm));
            Assert.Throws<ArgumentNullException>(() => CadStandards.Check(_drawings.Blank(), null, 0.0, Mm));
            Assert.Throws<ArgumentOutOfRangeException>(() => CadStandards.Check(_drawings.Blank(), standard, -1.0, Mm));
        }
    }
}
