using System;
using System.Linq;
using GeometryHelper.Dxf;
using GeometryHelper.Geometry;
using GeometryHelper.UnitTest.Dxf;
using Xunit;

namespace OpenCAD2D.UnitTest
{
    /// <summary>
    /// A table's rows grow to hold their cells' words as AutoCAD 2021 lays a table out: each cell's text wrapped at the cell's
    /// width less its margins, in the presenter's opencad2d, and its row as high as the tallest cell needs, never lower than it
    /// was. The numbers are those AutoCAD 2021 gave the same tables with opencad2d.shx (GenerateLayout and
    /// RecomputeTableBlock, the probe of 2026-11-25).
    /// </summary>
    public class TableRowFitTests
    {
        // AutoCAD's numbers as its probe printed them, to five places.
        private const int Places = 5;

        /// <summary>A drawing of millimetres whose Standard text style is opencad2d, with AutoCAD's Standard table style.</summary>
        private static DxfDatabase Drawing()
        {
            var db = new DxfDatabase("AC1032");
            db.SetHeader("$MEASUREMENT", new DxfTypedValue(70, "1"));
            db.TextStyleTable["Standard"].FileName = "opencad2d.shx";

            if (db.TableStyleDictionary == null)
            {
                db.NamedObjectsDictionary.SetAt("ACAD_TABLESTYLE", new DxfDBDictionary());
            }

            db.TableStyleDictionary.SetAt("Standard", new DxfTableStyle());
            return db;
        }

        private static DxfTable Table(DxfDatabase db, int rows, double[] heights)
        {
            var table = new DxfTable(new GeoPoint3(0, 0, 0), rows, 3, heights[0], 30.0);
            db.ModelSpace.AppendEntity(table);

            for (int r = 0; r < rows; r++)
            {
                table.SetRowHeight(r, heights[r]);
            }

            return table;
        }

        private static DxfMText Text(DxfTable table, string words) =>
            table.BlockTableRecord.OfType<DxfMText>().Single(m => m.Contents == words);

        [Fact]
        public void WrappedWords_GrowTheirRows_ToTheTallestCell_AsAutoCadLaysThemOut()
        {
            DxfDatabase db = Drawing();
            DxfTable table = Table(db, 6, new[] { 11.0, 9, 9, 9, 9, 9 });
            table.SetTextString(0, 0, "Bill of steel members for the north frame of the hall");
            table.SetTextString(1, 0, "Mark");
            table.SetTextString(1, 1, "Section and grade");
            table.SetTextString(1, 2, "Qty");
            table.SetTextString(2, 0, "B1");
            table.SetTextString(2, 2, "4");
            table.SetTextString(3, 0, "B2 long mark name here");
            table.SetTextString(3, 1, "IPE200");
            table.SetTextString(4, 1, "Line one\\PLine two\\PLine three");
            table.SetTextString(5, 0, "x");
            table.SetAlignment(5, 0, DxfCellAlignment.MiddleLeft);
            table.SetTextString(5, 1, "bottom right words that wrap twice at least here");
            table.SetAlignment(5, 1, DxfCellAlignment.BottomRight);
            table.SetTextString(5, 2, "middle centre wraps");
            table.SetAlignment(5, 2, DxfCellAlignment.MiddleCenter);
            CadDocument document = CadDocument.From(db);
            document.Fonts = new CadFonts(null, CadFonts.BuiltInName, null);

            // The last cell typed, as the editor writes it: the table is laid out anew.
            Assert.True(
                CadTableEditor.SetText(document, table.Handle, 2, 1, "HEA300 S355 with end plates both sides").Succeeded
            );

            // Each row the actual height of its tallest text and 1.5 above and below: the title's lines of 6 and the
            // header's of 4.5 under Section and grade; the third row the four lines of "HEA300 S355 with end plates both sides"
            // (27.41031 and the margins, 30.41031).
            table = (DxfTable)document.FindEntity(table.Handle);
            double[] autocad = { 29.29823, 16.41343, 30.41031, 23.1013, 22.78346, 45.28346 };

            for (int r = 0; r < autocad.Length; r++)
            {
                Assert.Equal(autocad[r], table.RowHeight(r), Places);
            }

            // The words stand where AutoCAD 2021 put them in the rows grown: centred in the title, 1.5 under the top of a
            // row of data, centred in the last row and 1.5 above its bottom.
            DxfMText title = Text(table, "Bill of steel members for the north frame of the hall");
            Assert.Equal(45.0, title.Location.X, Places);
            Assert.Equal(-14.64912, title.Location.Y, Places);
            Assert.Equal(-47.21166, Text(table, "B1").Location.Y, Places);
            Assert.Equal(-77.62197, Text(table, "IPE200").Location.Y, Places);
            Assert.Equal(-144.64847, Text(table, "x").Location.Y, Places);
            Assert.Equal(1.5, Text(table, "x").Location.X, Places);
            DxfMText bottom = Text(table, "bottom right words that wrap twice at least here");
            Assert.Equal(58.5, bottom.Location.X, Places);
            Assert.Equal(-165.7902, bottom.Location.Y, Places);
            Assert.Equal(-144.64847, Text(table, "middle centre wraps").Location.Y, Places);
        }

        [Fact]
        public void ARowHigherThanItsWordsNeed_KeepsItsHeight_AndShorterWordsShrinkNoRow()
        {
            DxfDatabase db = Drawing();
            DxfTable table = Table(db, 4, new[] { 20.0, 20, 20, 20 });
            table.SetTextString(0, 0, "Short title");
            table.SetTextString(1, 1, "Section and grade");
            table.SetTextString(2, 1, "HEA300 S355 with end plates both sides");
            CadDocument document = CadDocument.From(db);
            document.Fonts = new CadFonts(null, CadFonts.BuiltInName, null);
            Assert.True(
                CadTableEditor
                    .SetText(
                        document,
                        table.Handle,
                        3,
                        1,
                        "one two three four five six seven eight nine ten eleven twelve"
                    )
                    .Succeeded
            );

            table = (DxfTable)document.FindEntity(table.Handle);
            double[] autocad = { 20.0, 20.0, 30.41031, 53.00714 };

            for (int r = 0; r < autocad.Length; r++)
            {
                Assert.Equal(autocad[r], table.RowHeight(r), Places);
            }

            // The long words made short again: the row keeps its 30.41031, as AutoCAD 2021 kept it.
            Assert.True(CadTableEditor.SetText(document, table.Handle, 2, 1, "a").Succeeded);
            table = (DxfTable)document.FindEntity(table.Handle);
            Assert.Equal(30.41031, table.RowHeight(2), Places);
        }

        private static string Lines(int count) =>
            string.Join("\\P", Enumerable.Range(0, count).Select(i => ((char)('a' + i)).ToString()));

        /// <summary>A table of six rows of the heights given, each merge in the order given and each text set, laid out as the editor lays a table out.</summary>
        private static double[] Laid(double[] heights, int[][] merges, params (int Row, int Column, int Lines)[] texts)
        {
            DxfDatabase db = Drawing();
            DxfTable table = Table(db, heights.Length, heights);

            foreach (int[] m in merges)
            {
                table.MergeCells(new DxfCellRange(m[0], m[1], m[2], m[3]));
            }

            foreach ((int row, int column, int lines) in texts)
            {
                table.SetTextString(row, column, Lines(lines));
            }

            CadDocument document = CadDocument.From(db);
            document.Fonts = new CadFonts(null, CadFonts.BuiltInName, null);
            CadTableEditor.Redraw(table, document);
            return Enumerable.Range(0, heights.Length).Select(r => table.RowHeight(r)).ToArray();
        }

        private static void AssertHeights(double[] expected, double[] actual)
        {
            Assert.Equal(expected.Length, actual.Length);

            for (int r = 0; r < expected.Length; r++)
            {
                Assert.Equal(expected[r], actual[r], Places);
            }
        }

        [Fact]
        public void TwelveLinesMergedOverTwoRows_ShareTheirHeightEqually_NotInTheLastRow()
        {
            // AutoCAD 2021 GenerateLayout: 12 lines of 4.5 in a merged block of rows 2 and 3 need 90.11184 with the margins (the
            // same lines in one row made it 90.11184), and the rows, each 9, came to 45.05592 each, not 9 and 81.11184.
            double[] nine = { 9, 9, 9, 9, 9, 9 };
            AssertHeights(
                new[] { 11.0, 9, 45.05592, 45.05592, 9, 9 },
                Laid(nine, new[] { new[] { 2, 0, 3, 0 } }, (2, 0, 12))
            );
            AssertHeights(
                new[] { 11.0, 9, 22.55592, 22.55592, 9, 9 },
                Laid(nine, new[] { new[] { 2, 0, 3, 0 } }, (2, 0, 6))
            );
            AssertHeights(
                new[] { 11.0, 9, 30.03728, 30.03728, 30.03728, 9 },
                Laid(nine, new[] { new[] { 2, 0, 4, 0 } }, (2, 0, 12))
            );

            // A block of two rows and two columns, or all three columns, shares the same way.
            AssertHeights(
                new[] { 11.0, 9, 45.05592, 45.05592, 9, 9 },
                Laid(nine, new[] { new[] { 2, 0, 3, 1 } }, (2, 0, 12))
            );
        }

        [Fact]
        public void TheRowsOfAMergedBlock_GrowInProportionToTheirHeights_AndNoneIsShrunk()
        {
            // Rows of 12 and 20 under 12 lines (90.11184): 90.11184 * 12 / 32 and * 20 / 32, as AutoCAD 2021 laid them out; rows
            // of 20 and 20 make 45.05592 each, though they hold 40 of the 90.11184 already.
            AssertHeights(
                new[] { 11.0, 9, 33.79194, 56.3199, 9, 9 },
                Laid(new double[] { 9, 9, 12, 20, 9, 9 }, new[] { new[] { 2, 0, 3, 0 } }, (2, 0, 12))
            );
            AssertHeights(
                new[] { 11.0, 9, 56.3199, 33.79194, 9, 9 },
                Laid(new double[] { 9, 9, 20, 12, 9, 9 }, new[] { new[] { 2, 0, 3, 0 } }, (2, 0, 12))
            );
            AssertHeights(
                new[] { 11.0, 9, 45.05592, 45.05592, 9, 9 },
                Laid(new double[] { 9, 9, 20, 20, 9, 9 }, new[] { new[] { 2, 0, 3, 0 } }, (2, 0, 12))
            );

            // Rows that hold the words already stay as they are: 6 lines need 45.11184 in rows of 5 and 50, which come to 9 (the least a
            // row of words is) and 50.
            AssertHeights(
                new[] { 11.0, 9, 9, 50, 9, 9 },
                Laid(new double[] { 9, 9, 5, 50, 9, 9 }, new[] { new[] { 2, 0, 3, 0 } }, (2, 0, 6))
            );
            AssertHeights(
                new[] { 11.0, 9, 22.55592, 22.55592, 9, 9 },
                Laid(new double[] { 9, 9, 5, 5, 9, 9 }, new[] { new[] { 2, 0, 3, 0 } }, (2, 0, 6))
            );
        }

        [Fact]
        public void ABlockAcrossRows_SharesAmongTheRowsTheirOwnCellsHaveMadeTallFirst()
        {
            // 12 lines over rows 2 and 3 with 6 lines (45.11184) in (3, 1): the rows stand at 9 and 45.11184 when the block is laid out, and
            // share its 90.11184 in proportion to 9 and 45.11184, 14.9876 and 75.12424; with the 6 lines in (2, 1) the other way round.
            double[] nine = { 9, 9, 9, 9, 9, 9 };
            AssertHeights(
                new[] { 11.0, 9, 14.9876, 75.12424, 9, 9 },
                Laid(nine, new[] { new[] { 2, 0, 3, 0 } }, (2, 0, 12), (3, 1, 6))
            );
            AssertHeights(
                new[] { 11.0, 9, 75.12424, 14.9876, 9, 9 },
                Laid(nine, new[] { new[] { 2, 0, 3, 0 } }, (2, 0, 12), (2, 1, 6))
            );

            // Words in a cell of a row of their own that are as tall as the block's: the rows come to what they need.
            AssertHeights(
                new[] { 11.0, 9, 90.11184, 90.11184, 9, 9 },
                Laid(nine, new[] { new[] { 2, 0, 3, 0 } }, (2, 0, 12), (2, 1, 12), (3, 1, 12))
            );
        }

        [Fact]
        public void BlocksAcrossRowsThatShareARow_ShareOneAfterTheOther_InTheOrderOfTheirTopLeftCells()
        {
            // AutoCAD 2021 read back from a DXF: the block at (2, 1) over rows 2 and 3 first, 45.05592 each, then the block at
            // (3, 0) over rows 3 and 4, which finds 45.05592 and 9 and shares its 90.11184 in proportion to them: 75.10873 and 15.0031.
            double[] nine = { 9, 9, 9, 9, 9, 9 };
            AssertHeights(
                new[] { 11.0, 9, 45.05592, 75.10873, 15.0031, 9 },
                Laid(nine, new[] { new[] { 3, 0, 4, 0 }, new[] { 2, 1, 3, 1 } }, (3, 0, 12), (2, 1, 12))
            );

            // A block of rows 2 to 4 first (30.03728 each), then one over rows 2 and 3 that needs 90.11184 of their 60.07456.
            AssertHeights(
                new[] { 11.0, 9, 45.05592, 45.05592, 30.03728, 9 },
                Laid(nine, new[] { new[] { 2, 0, 4, 0 }, new[] { 2, 1, 3, 1 } }, (2, 0, 12), (2, 1, 12))
            );

            // A block of 6 lines over rows 2 and 3 (22.55592 each) and one of 12 over rows 2 to 4, which finds 22.55592, 22.55592 and 9.
            AssertHeights(
                new[] { 11.0, 9, 37.56212, 37.56212, 14.9876, 9 },
                Laid(nine, new[] { new[] { 2, 0, 3, 0 }, new[] { 2, 1, 4, 1 } }, (2, 0, 6), (2, 1, 12))
            );
        }

        [Fact]
        public void ATablesOwnTextHeightOfItsData_SetsTheHeightItsDataRowsAreLaidOutBy_AsInAutoCad()
        {
            // AutoCAD 2021's GenerateLayout of a table whose data has a text height of its own of 8 (SetTextHeight(8, data row)),
            // the text in opencad2d.shx: each data row, empty, 8 * 4 / 3 + 2 * 1.5 = 13.66667 high, where the style's 4.5 made
            // them 9; a header row 13.24 (13.66667 in ghsimplex.shx); 3 lines in one cell 38.1706; 'aaa bbb ccc ddd eee' in a
            // column 30 wide 64.83727, where the style's height made it 22.5. Here the data and the header of D33b's table are 9
            // high in OC_TXT: AutoCAD gives the title 11, the header 14.52 and a data row 9 * 4 / 3 + 2 * the margin (1.5) = 15,
            // as the editor lays it out.
            const string Head = " 93\n0\n 94\n0\n 95\n0\n 96\n0\n141\n10.0\n";
            Assert.Contains(Head, DxfDimensionDrawings.R2018, StringComparison.Ordinal);
            DxfDatabase db = DxfDatabase.Parse(
                DxfDimensionDrawings.R2018.Replace(
                    Head,
                    " 93\n6886016\n 94\n0\n 95\n0\n 96\n0\n  7\nGH_TXT\n140\n9.0\n170\n1\n 63\n3\n 64\n1\n140\n9.0\n283\n0\n141\n10.0\n"
                )
            );
            DxfTable table = db.ModelSpace.OfType<DxfTable>().First();
            DxfRowType[] kinds = Enumerable.Range(0, table.NumRows).Select(r => table.RowType(r)).ToArray();
            Assert.Contains(DxfRowType.DataRow, kinds);

            for (int r = 0; r < table.NumRows; r++)
            {
                table.SetRowHeight(r, 5.0);
            }

            CadDocument document = CadDocument.From(db);
            document.Fonts = new CadFonts(null, CadFonts.BuiltInName, null);
            table = (DxfTable)document.FindEntity(table.Handle);
            CadTableEditor.Redraw(table, document);

            for (int r = 0; r < table.NumRows; r++)
            {
                double autocad =
                    kinds[r] == DxfRowType.TitleRow ? 11.0
                    : kinds[r] == DxfRowType.HeaderRow ? 14.52
                    : 15.0;
                Assert.Equal(autocad, table.RowHeight(r), Places);
            }
        }

        [Theory]
        [InlineData(21.65, 1)]
        [InlineData(21.60, 2)]
        public void AWordWhoseInkEndsOnTheWidth_StaysOnItsLine_AsInAutoCad(double width, int lines)
        {
            // AutoCAD 2021 set "with end" in opencad2d.shx 4.5 high on one line from a width of 21.62 (21.615 gives two), its
            // ActualWidth 21.375.
            DxfDatabase db = Drawing();
            db.ModelSpace.AppendEntity(
                new DxfMText
                {
                    Contents = "with end",
                    TextHeight = 4.5,
                    Width = width,
                    Location = new GeoPoint3(0, 0, 0),
                }
            );
            CadDocument document = CadDocument.From(db);
            document.Fonts = new CadFonts(null, CadFonts.BuiltInName, null);

            DisplayText2 shown = document.GetDisplay(document.ModelSpace).Items.OfType<DisplayText2>().Single();
            Assert.Equal(lines, shown.Parts.OfType<DisplayText2>().Count());
        }
    }
}
