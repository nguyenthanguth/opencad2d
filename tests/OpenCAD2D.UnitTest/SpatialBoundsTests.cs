using System;
using System.Collections.Generic;
using System.Linq;
using GeometryHelper.Dxf;
using GeometryHelper.Geometry;
using Xunit;
using static OpenCAD2D.UnitTest.EditingDrawings;
using static OpenCAD2D.UnitTest.SpatialDrawings;

namespace OpenCAD2D.UnitTest
{
    /// <summary>
    /// <see cref="CadSpatial.Bounds(CadDocument, CadSpace)"/>: the box each entity takes as the display list draws it, in the
    /// order it draws them. The box of an entity is the union of the boxes of its items, which these tests rebuild from
    /// <see cref="DisplayList2.Items"/> alone and compare within 1E-6; a TrueType TEXT, whose display box is a guess of 0.6 of
    /// the height a letter, is measured by its font instead.
    /// </summary>
    public sealed class SpatialBoundsTests : IDisposable
    {
        private const double Within = 1E-6;

        private readonly SpatialDrawings _drawings = new SpatialDrawings();

        public void Dispose() => _drawings.Dispose();

        /// <summary>The box of every handle the list draws, as the union of the boxes of its items: what the table must equal.</summary>
        private static Dictionary<string, DisplayBox> Drawn(DisplayList2 list)
        {
            var boxes = new Dictionary<string, DisplayBox>(StringComparer.OrdinalIgnoreCase);

            foreach (DisplayItem2 item in list.Items)
            {
                if (string.IsNullOrEmpty(item.Handle))
                {
                    continue;
                }

                DisplayBox box = item is DisplayRay2 ray ? DisplayBox.Empty.Add(ray.X, ray.Y) : item.VisibleBounds;
                boxes[item.Handle] = boxes.TryGetValue(item.Handle, out DisplayBox before) ? before.Union(box) : box;
            }

            return boxes;
        }

        private static void AssertBox(DisplayBox wanted, CadBoundsTable table, int row)
        {
            DisplayBox got = table.BoxOf(row);
            Assert.Equal(wanted.MinX, got.MinX, 6);
            Assert.Equal(wanted.MinY, got.MinY, 6);
            Assert.Equal(wanted.MaxX, got.MaxX, 6);
            Assert.Equal(wanted.MaxY, got.MaxY, 6);
        }

        /// <summary>Every row of the table equals the list's box of that handle within 1E-6, and every handle the list draws has a row.</summary>
        private static void AssertEqualToTheList(CadDocument document, CadSpace space)
        {
            CadBoundsTable table = CadSpatial.Bounds(document, space);
            Dictionary<string, DisplayBox> drawn = Drawn(document.GetDisplay(space));

            Assert.Equal(drawn.Count(d => !d.Value.IsEmpty), table.Count);

            for (int row = 0; row < table.Count; row++)
            {
                AssertBox(drawn[table.Handles[row]], table, row);
            }
        }

        [Fact]
        public void ALine_IsItsOwnBox_AndAnInvisibleEntityHasNoRow()
        {
            CadDocument document = _drawings.Blank();
            DxfLine line = Put(document, Line(0, 0, 100, 40));
            DxfLine hidden = Put(document, Line(500, 500, 600, 600));
            hidden.Visible = false;

            CadBoundsTable table = CadSpatial.Bounds(document, document.ModelSpace);

            Assert.Equal(new[] { line.Handle }, table.Handles);
            AssertBox(new DisplayBox(0, 0, 100, 40), table, 0);
        }

        [Fact]
        public void Rows_FollowTheOrderTheListDrawsThem_AndABoundsOfHandlesFollowsTheOrderAsked()
        {
            CadDocument document = _drawings.Blank();
            DxfLine first = Put(document, Line(0, 0, 1, 0));
            DxfCircle second = Put(document, Circle(50, 50, 10));
            DxfLine third = Put(document, Line(0, 9, 1, 9));

            CadBoundsTable all = CadSpatial.Bounds(document, document.ModelSpace);
            CadBoundsTable some = CadSpatial.Bounds(document, new[] { third.Handle, "FFFF", first.Handle });

            Assert.Equal(new[] { first.Handle, second.Handle, third.Handle }, all.Handles);
            AssertBox(new DisplayBox(40, 40, 60, 60), all, 1);
            Assert.Equal(new[] { third.Handle, first.Handle }, some.Handles);
            AssertBox(new DisplayBox(0, 9, 1, 9), some, 0);
        }

        [Fact]
        public void TheWordsOfATextAreItsLettersAndItsFrame_NotItsInsertionPoint_AndRotateWithIt()
        {
            CadDocument document = _drawings.Blank();
            Put(document, Text("SIDE FOOD", 10, 20));
            Put(document, Text("SIDE FOOD", 10, 20, 30.0));

            CadBoundsTable table = CadSpatial.Bounds(document, document.ModelSpace);

            DisplayBox level = table.BoxOf(0);
            DisplayBox slanted = table.BoxOf(1);
            Assert.True(level.Width > 20.0, "nine letters are wider than four heights: " + level.Width);
            Assert.True(level.Height >= 5.0 - Within);
            Assert.True(
                slanted.Height > level.Height * 2.0,
                "turned 30 degrees the box is taller than the level text's"
            );
            AssertEqualToTheList(document, document.ModelSpace);
        }

        [Fact]
        public void ATrueTypeText_IsMeasuredByItsFontsLetters_NotByTheDisplaysGuessOfSixTenthsOfTheHeight()
        {
            var database = new DxfDatabase("AC1027");
            var style = new DxfTextStyleTableRecord { Name = "ARIAL" };
            style.Font = new DxfFontDescriptor("Arial", false, false, 0, 34);
            database.TextStyleTable.Add(style);
            DxfDBText narrow = Text("iiiiiiiiii", 0, 0, 0.0, 10.0);
            DxfDBText wide = Text("WWWWWWWWWW", 0, 40, 0.0, 10.0);
            database.ModelSpace.AppendEntity(narrow);
            database.ModelSpace.AppendEntity(wide);
            narrow.TextStyleName = "ARIAL";
            wide.TextStyleName = "ARIAL";
            CadDocument document = CadDocument.From(database);
            document.Fonts = new CadFonts(null, CadFonts.BuiltInName, null, f => f == "Arial");

            CadBoundsTable table = CadSpatial.Bounds(document, document.ModelSpace);

            Assert.Equal(2, table.Count);
            double guess = 10 * 10.0 * 0.6;
            DisplayBox narrowBox = table.BoxOf(0);
            DisplayBox wideBox = table.BoxOf(1);

            // Arial's i is 0.22 of its em, its W 0.94, the em being 1.4 of the capital's height; the display's one width for both is 60.
            Assert.True(narrowBox.Width < guess * 0.6, "narrow " + narrowBox.Width);
            Assert.True(wideBox.Width > guess * 1.5, "wide " + wideBox.Width);
            Assert.InRange(wideBox.Width / narrowBox.Width, 3.5, 5.5);
            Assert.Equal(0.0, narrowBox.MinX, 6);
            Assert.True(narrowBox.MinY < 0.0, "the box reaches down to the font's descenders");
        }

        [Fact]
        public void ADimension_HoldsItsLinesArrowsAndWords_AsItsPictureDrawsThem()
        {
            CadDocument document = _drawings.Blank();
            Run(Session(document), "DLI", P(0, 0), P(300, 0), P(150, -40));
            DxfRotatedDimension dimension = All<DxfRotatedDimension>(document.ModelSpace).Single();

            CadBoundsTable table = CadSpatial.Bounds(document, document.ModelSpace);

            Assert.Equal(new[] { dimension.Handle }, table.Handles);
            DisplayBox box = table.BoxOf(0);
            Assert.True(
                box.MinX <= 0.0 + Within && box.MaxX >= 300.0 - Within,
                "the extension lines and arrows reach both ends"
            );
            Assert.True(box.MinY <= -40.0 + Within, "the dimension line is at y -40");
            AssertEqualToTheList(document, document.ModelSpace);
        }

        [Fact]
        public void ABlockInsertedInABlockScaledAndTurned_HasTheBoxOfWhatItDrawsInTheDrawing()
        {
            CadDocument document = _drawings.Blank();
            DxfDatabase database = document.Database;
            var inner = new DxfBlockTableRecord { Name = "INNER" };
            var outer = new DxfBlockTableRecord { Name = "OUTER" };
            database.BlockTable.Add(inner);
            database.BlockTable.Add(outer);
            inner.AppendEntity(Line(0, 0, 10, 0));
            var nested = new DxfBlockReference(new GeoPoint3(5, 5, 0), inner)
            {
                ScaleFactors = new GeoVector3(2, 2, 1),
                Rotation = Math.PI / 2.0,
            };
            outer.AppendEntity(nested);
            var insert = Put(
                document,
                new DxfBlockReference(new GeoPoint3(100, 100, 0), outer) { ScaleFactors = new GeoVector3(3, 3, 1) }
            );

            CadBoundsTable table = CadSpatial.Bounds(document, document.ModelSpace);

            // INNER's line (0,0)-(10,0), scaled 2 and turned 90 degrees at (5, 5), is (5, 5) to (5, 25); OUTER scales it 3 from (100, 100).
            Assert.Equal(new[] { insert.Handle }, table.Handles);
            AssertBox(new DisplayBox(115, 115, 115, 175), table, 0);
            AssertEqualToTheList(document, document.ModelSpace);
        }

        [Fact]
        public void AWidePolyline_TakesItsWidthIn_AndAHatchTheLinesItDraws()
        {
            CadDocument document = _drawings.Blank();
            var wide = new DxfPolyline { ConstantWidth = 4.0 };
            wide.AddVertexAt(0, new GeoPoint2(0, 0), 0.0, 0.0, 0.0);
            wide.AddVertexAt(1, new GeoPoint2(100, 0), 0.0, 0.0, 0.0);
            Put(document, wide);
            var hatch = new DxfHatch();
            hatch.SetHatchPattern(DxfHatchPatternType.UserDefined, "_USER");
            hatch.PatternSpace = 2.0;
            hatch.AppendLoop(
                DxfHatchLoopTypes.External,
                new[]
                {
                    new GeoPoint2(200, 10),
                    new GeoPoint2(230, 10),
                    new GeoPoint2(230, 40),
                    new GeoPoint2(200, 40),
                },
                null
            );
            Put(document, hatch);

            CadBoundsTable table = CadSpatial.Bounds(document, document.ModelSpace);

            Assert.Equal(new[] { wide.Handle, hatch.Handle }, table.Handles);
            Assert.True(table.BoxOf(0).MinY <= -2.0 + Within && table.BoxOf(0).MaxY >= 2.0 - Within);
            DisplayBox lines = table.BoxOf(1);
            Assert.True(lines.MinX >= 200.0 - Within && lines.MaxX <= 230.0 + Within);
            Assert.True(lines.MinY >= 10.0 - Within && lines.MaxY <= 40.0 + Within);
            Assert.True(lines.Width > 28.0 && lines.Height >= 26.0 - Within);
            AssertEqualToTheList(document, document.ModelSpace);
        }

        [Fact]
        public void AnMTextInTwoColumns_HoldsBothColumns_AsTheLayoutSetsThem()
        {
            CadDocument document = _drawings.Blank();
            var mtext = new DxfMText
            {
                Contents = @"SIDE FOOD\PFOOD SIDE\PSIDE SIDE\PFOOD FOOD",
                Location = new GeoPoint3(0, 0, 0),
                TextHeight = 5.0,
                Width = 60.0,
            };
            Put(document, mtext);
            mtext.Height = 20.0;
            mtext.SetStaticColumns(60.0, 10.0, 2);

            CadBoundsTable table = CadSpatial.Bounds(document, document.ModelSpace);

            Assert.Equal(new[] { mtext.Handle }, table.Handles);
            Assert.True(
                table.BoxOf(0).Width > 60.0,
                "the second column stands to the right of the first: " + table.BoxOf(0).Width
            );
            AssertEqualToTheList(document, document.ModelSpace);
        }

        [Fact]
        public void ALayoutHasItsOwnBoxes_AndModelSpaceHasNoneOfThem()
        {
            CadDocument document = _drawings.Blank();
            DxfLine onSheet = AddLineOnSheet(document, out CadSpace sheet);

            CadBoundsTable layout = CadSpatial.Bounds(document, sheet);
            CadBoundsTable model = CadSpatial.Bounds(document, document.ModelSpace);
            CadBoundsTable byHandle = CadSpatial.Bounds(document, new[] { onSheet.Handle });

            Assert.Equal(new[] { onSheet.Handle }, layout.Handles);
            AssertBox(new DisplayBox(0, 0, 100, 0), layout, 0);
            Assert.Equal(0, model.Count);
            Assert.Equal(new[] { onSheet.Handle }, byHandle.Handles);
        }

        [Fact]
        public void ATextMovedAfterTheBoxWasAsked_HasTheNewBox_AsEachEditMakesANewList()
        {
            CadDocument document = _drawings.Blank();
            DxfDBText text = Put(document, Text("SIDE FOOD", 0, 0));
            CadBoundsTable before = CadSpatial.Bounds(document, document.ModelSpace);

            Run(Session(document), "MOVE", new[] { text.Handle }, Enter, P(0, 0), P(100, 50));
            CadBoundsTable after = CadSpatial.Bounds(document, document.ModelSpace);

            Assert.Equal(before.BoxOf(0).MinX + 100.0, after.BoxOf(0).MinX, 6);
            Assert.Equal(before.BoxOf(0).MinY + 50.0, after.BoxOf(0).MinY, 6);
        }

        [Fact]
        public void ANullDocumentOrSpace_IsRefused()
        {
            CadDocument document = _drawings.Blank();

            Assert.Throws<ArgumentNullException>(() => CadSpatial.Bounds(null, document.ModelSpace));
            Assert.Throws<ArgumentNullException>(() => CadSpatial.Bounds(document, (CadSpace)null));
            Assert.Throws<ArgumentNullException>(() => CadSpatial.Bounds(document, (IEnumerable<string>)null));
        }
    }
}
