using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO.Packaging;
using System.Linq;
using System.Threading;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using GeometryHelper;
using GeometryHelper.Dxf;
using Xunit;

namespace OpenCAD2D.Wpf.UnitTest
{
    /// <summary>
    /// A14 package P3: the previews of the Modify Table Style dialog. The view model that links the setting being edited or hovered
    /// to the parts of the rows (lit parts, the callout of a margin or a height, chips, ghosts, the part under the pointer and the
    /// fields that set it), the layout of the table to scale, the hit test of the parts, the words drawn at their text height, the
    /// size of the dialog and the time of a redraw per value.
    /// </summary>
    public sealed class TableStylePreviewPartsTests
    {
        private static readonly Tolerance Tol = new Tolerance(1E-9, 1E-9);

        private static void OnSta(Action action)
        {
            Exception failure = null;
            var thread = new Thread(() =>
            {
                try
                {
                    action();
                }
                catch (Exception exception)
                {
                    failure = exception;
                }
            });
            thread.SetApartmentState(ApartmentState.STA);
            thread.Start();
            thread.Join();

            if (failure != null)
            {
                throw new Xunit.Sdk.XunitException(failure.ToString());
            }
        }

        private static TableStyleModifyViewModel Dialog()
        {
            var document = CadDocument.From(CadTemplate.CreateMetric(), "tables.dxf");

            // A drawing from the metric template has no table style until one is made: Standard, as a new drawing starts with.
            CadTableStyleEditor.AddStandard(document);
            return new TableStyleModifyViewModel(document, "Standard", false);
        }

        private static TableStylePreview Preview(
            TableStyleModifyViewModel model,
            double width,
            double height,
            string cellStyle,
            double dpi = 1.0
        )
        {
            var view = new TableStylePreview
            {
                Settings = model.Settings,
                Model = model.Preview,
                CellStyle = cellStyle,
                DpiOverride = dpi,
            };
            view.Measure(new Size(width, height));
            view.Arrange(new Rect(0, 0, width, height));
            return view;
        }

        private static IEnumerable<T> Descendants<T>(DependencyObject parent)
            where T : DependencyObject
        {
            for (int i = 0; i < VisualTreeHelper.GetChildrenCount(parent); i++)
            {
                DependencyObject child = VisualTreeHelper.GetChild(parent, i);

                if (child is T found)
                {
                    yield return found;
                }

                foreach (T deeper in Descendants<T>(child))
                {
                    yield return deeper;
                }
            }
        }

        private static readonly DxfRowType[] Kinds = { DxfRowType.TitleRow, DxfRowType.HeaderRow, DxfRowType.DataRow };

        private static readonly TablePart[] Edges =
        {
            TablePart.TopEdge,
            TablePart.InsideHorizontal,
            TablePart.BottomEdge,
            TablePart.LeftEdge,
            TablePart.InsideVertical,
            TablePart.RightEdge,
        };

        [Fact]
        public void TheHorizontalMarginFocusedWithDataPicked_LightsTheWordsOfEveryDataRow_AndMeasuresItFromTheCellsEdge()
        {
            OnSta(() =>
            {
                TableStyleModifyViewModel model = Dialog();
                model.CellStyle = "Data";
                model.HorizontalMargin = "1.5";

                model.SetFocused("HorizontalMargin", true);

                Assert.Equal("HorizontalMargin", model.ActiveSetting);
                TableStylePreviewViewModel preview = model.Preview;
                Assert.True(preview.IsLit(DxfRowType.DataRow, TablePart.Text));
                Assert.False(preview.IsLit(DxfRowType.HeaderRow, TablePart.Text));
                Assert.False(preview.IsLit(DxfRowType.TitleRow, TablePart.Text));

                foreach (TablePart edge in Edges)
                {
                    Assert.False(preview.IsLit(DxfRowType.DataRow, edge));
                }

                Assert.False(preview.IsLit(DxfRowType.DataRow, TablePart.Fill));
                Assert.Equal(string.Empty, preview.Chip);

                TablePreviewLayout layout = TablePreviewLayout.Of(model.Settings, null);
                Assert.Equal(3, layout.Rows.Count(r => r.Kind == DxfRowType.DataRow));
                TableMeasure measure = Assert.Single(preview.Measures(layout));
                TablePreviewRow first = layout.Rows.First(r => r.Kind == DxfRowType.DataRow);
                Assert.Equal("HorizontalMargin", measure.Key);
                Assert.Equal(1.5, measure.Length, 9);
                Assert.Equal(layout.Cell(first, 2).Left, measure.From.X, 9);
                Assert.Equal(measure.From.Y, measure.To.Y, 9);
                Assert.Contains("Horizontal margin = 1.5", measure.Label);

                model.SetFocused("HorizontalMargin", false);
                Assert.Null(model.ActiveSetting);
                Assert.False(preview.HasLit);
                Assert.Empty(preview.Measures(layout));
            });
        }

        [Fact]
        public void TheHoveredSettingWins_TheFocusedOneComesBack_WhenThePointerLeaves()
        {
            OnSta(() =>
            {
                TableStyleModifyViewModel model = Dialog();
                model.SetFocused("TextHeight", true);
                model.SetHovered("Fill", true);

                Assert.Equal("Fill", model.ActiveSetting);
                Assert.True(model.Preview.IsLitPart(DxfRowType.DataRow, TablePart.Fill));

                model.SetHovered("Fill", false);

                Assert.Equal("TextHeight", model.ActiveSetting);
                Assert.True(model.Preview.IsLit(DxfRowType.DataRow, TablePart.Text));
                Assert.Contains("TextHeight", model.LitSettings);
            });
        }

        [Fact]
        public void TheMarginsAndTheTextHeightAreMeasuredOnTheMiddleCellOfTheFirstRowOfTheCellStyle()
        {
            OnSta(() =>
            {
                TableStyleModifyViewModel model = Dialog();
                model.CellStyle = "Header";
                model.Settings.Header.Alignment = DxfCellAlignment.BottomRight;
                TablePreviewLayout layout = TablePreviewLayout.Of(model.Settings, null);
                TablePreviewRow header = layout.Rows.Single(r => r.Kind == DxfRowType.HeaderRow);
                Rect cell = layout.Cell(header, 2);

                // A margin at the right, and below.
                TableMeasure across = Assert.Single(layout.Measures("HorizontalMargin", DxfRowType.HeaderRow));
                Assert.Equal(cell.Right - 1.5, across.From.X, 9);
                Assert.Equal(cell.Right, across.To.X, 9);
                TableMeasure down = Assert.Single(layout.Measures("VerticalMargin", DxfRowType.HeaderRow));
                Assert.Equal(down.From.X, down.To.X, 9);
                Assert.Equal(cell.Bottom - 1.5, down.From.Y, 9);
                Assert.Equal(cell.Bottom, down.To.Y, 9);

                // A word at the top left: the margin from the left edge and the top edge.
                model.Settings.Header.Alignment = DxfCellAlignment.TopLeft;
                layout = TablePreviewLayout.Of(model.Settings, null);
                Assert.Equal(
                    cell.Left,
                    Assert.Single(layout.Measures("HorizontalMargin", DxfRowType.HeaderRow)).From.X,
                    9
                );
                Assert.Equal(
                    cell.Top,
                    Assert.Single(layout.Measures("VerticalMargin", DxfRowType.HeaderRow)).From.Y,
                    9
                );

                // A word in the middle of its cell: the margin is still measured at the edge, and the label says the words are centred.
                model.Settings.Header.Alignment = DxfCellAlignment.MiddleCenter;
                layout = TablePreviewLayout.Of(model.Settings, null);
                Assert.Contains(
                    "words centred",
                    Assert.Single(layout.Measures("HorizontalMargin", DxfRowType.HeaderRow)).Label
                );

                // The text height on the word: 4.5 long, at the word's left.
                TableMeasure height = Assert.Single(layout.Measures("TextHeight", DxfRowType.HeaderRow));
                Rect word = layout.WordBox(header, 2);
                Assert.Equal(4.5, height.Length, 9);
                Assert.Equal(word.Left, height.From.X, 9);
                Assert.Equal(height.From.X, height.To.X, 9);
                Assert.Equal("Text height = 4.5", height.Label);
                Assert.Empty(layout.Measures("Fill", DxfRowType.HeaderRow));
            });
        }

        [Fact]
        public void HoveringTheOutsideBordersButton_LightsTheOuterLinesOfThePickedRows_NotTheInsideOnes()
        {
            OnSta(() =>
            {
                TableStyleModifyViewModel model = Dialog();
                model.CellStyle = "Data";

                model.SetHovered("Border:Outside", true);

                TableStylePreviewViewModel preview = model.Preview;

                foreach (
                    TablePart edge in new[]
                    {
                        TablePart.TopEdge,
                        TablePart.BottomEdge,
                        TablePart.LeftEdge,
                        TablePart.RightEdge,
                    }
                )
                {
                    Assert.True(preview.IsLit(DxfRowType.DataRow, edge), edge.ToString());
                    Assert.False(preview.IsLit(DxfRowType.HeaderRow, edge), edge.ToString());
                }

                Assert.False(preview.IsLit(DxfRowType.DataRow, TablePart.InsideHorizontal));
                Assert.False(preview.IsLit(DxfRowType.DataRow, TablePart.InsideVertical));
                Assert.False(preview.IsLit(DxfRowType.DataRow, TablePart.Text));
                Assert.False(preview.Ghost);

                // The button that hides the lines lights them dashed.
                model.SetHovered("Border:Outside", false);
                model.SetHovered("Border:None", true);
                Assert.True(preview.Ghost);
                Assert.True(preview.IsLit(DxfRowType.DataRow, TablePart.InsideVertical));

                // The lineweight and colour boxes light the lines of the button pressed last.
                model.SetHovered("Border:None", false);
                model.BorderCommand.Execute("Inside");
                model.SetFocused("BorderWeight", true);
                Assert.True(preview.IsLit(DxfRowType.DataRow, TablePart.InsideHorizontal));
                Assert.False(preview.IsLit(DxfRowType.DataRow, TablePart.TopEdge));
                Assert.False(preview.Ghost);
            });
        }

        [Fact]
        public void AFillOfNone_LightsAGhostWithTheChipSuppressed_AndAFillLightsTheFillItself()
        {
            OnSta(() =>
            {
                TableStyleModifyViewModel model = Dialog();
                model.CellStyle = "Data";
                model.SetFocused("Fill", true);

                Assert.True(model.Preview.IsLitPart(DxfRowType.DataRow, TablePart.Fill));
                Assert.True(model.Preview.Ghost);
                Assert.Equal("Suppressed", model.Preview.Chip);

                model.Fill = model.Fills.Single(f => f.Name == "Color 8");

                Assert.False(model.Preview.Ghost);
                Assert.Equal(string.Empty, model.Preview.Chip);
                Assert.True(model.Preview.IsLitPart(DxfRowType.DataRow, TablePart.Fill));
            });
        }

        [Fact]
        public void TheTableDirection_LightsEveryRowWithAChipThatNamesIt()
        {
            OnSta(() =>
            {
                TableStyleModifyViewModel model = Dialog();
                model.CellStyle = "Header";
                model.SetFocused("Direction", true);

                foreach (DxfRowType kind in Kinds)
                {
                    Assert.True(model.Preview.IsLit(kind, TablePart.Text), kind.ToString());
                    Assert.True(model.Preview.IsLit(kind, TablePart.TopEdge), kind.ToString());
                }

                Assert.Equal("Table direction = Down ↓", model.Preview.Chip);
                model.Direction = "Up";
                Assert.Equal("Table direction = Up ↑", model.Preview.Chip);
            });
        }

        [Fact]
        public void ThePointerOnTheHeaderRow_NamesItsCellStyle_AndAClickPicksIt()
        {
            OnSta(() =>
            {
                _ = PackUriHelper.UriSchemePack;
                TableStyleModifyViewModel model = Dialog();
                model.CellStyle = "Data";
                TableStylePreview view = Preview(model, 328, 240, null);
                double scale = view.Scale;
                var origin = new Point((328 - 125.0 * scale) / 2.0, (240 - 47.0 * scale) / 2.0);

                // The title is 11 high, the header 9: the header's middle is 15.5 down, in its third column.
                TableTarget target = view.HitTest(new Point(origin.X + 62.0 * scale, origin.Y + 15.5 * scale));

                Assert.Equal(DxfRowType.HeaderRow, target.Kind);
                Assert.Equal(TablePart.Cells, target.Part);
                Assert.Equal(TablePart.None, view.HitTest(new Point(3, 3)).Part);
                Assert.Equal(TablePart.None, view.HitTest(new Point(origin.X + 62.0 * scale, 239)).Part);

                model.Preview.PointerAt(target);

                Assert.Equal("Header · cell style", model.Preview.HoverText);
                Assert.True(model.Preview.IsLit(DxfRowType.HeaderRow, TablePart.Cells));
                Assert.False(model.Preview.IsLit(DxfRowType.DataRow, TablePart.Cells));
                Assert.Equal(new[] { "CellStyle" }, model.Preview.HoverKeys.ToArray());
                Assert.Contains("CellStyle", model.LitSettings);

                model.Preview.Click();

                Assert.Equal("Header", model.CellStyle);
                model.Preview.PointerAt(default);
                Assert.Equal(string.Empty, model.Preview.HoverText);
            });
        }

        [Fact]
        public void APointInTheCellStylePreview_FindsTheLineTheWordsOrTheFillUnderIt()
        {
            OnSta(() =>
            {
                _ = PackUriHelper.UriSchemePack;
                TableStyleModifyViewModel model = Dialog();
                model.CellStyle = "Data";
                TableStylePreview view = Preview(model, 560, 120, "Data");

                // Three cells of 25 mm at 7.2 a millimetre, 9 mm high, in the middle: the row is 10 to 550 across, 27.6 to 92.4 down.
                Assert.Equal(7.2, view.Scale, 9);
                Assert.Equal(TablePart.TopEdge, view.HitTest(new Point(100, 29)).Part);
                Assert.Equal(TablePart.BottomEdge, view.HitTest(new Point(100, 91)).Part);
                Assert.Equal(TablePart.LeftEdge, view.HitTest(new Point(11, 60)).Part);
                Assert.Equal(TablePart.RightEdge, view.HitTest(new Point(549, 60)).Part);
                Assert.Equal(TablePart.InsideVertical, view.HitTest(new Point(190, 60)).Part);
                Assert.Equal(TablePart.Text, view.HitTest(new Point(100, 55)).Part);
                Assert.Equal(DxfRowType.DataRow, view.HitTest(new Point(100, 55)).Kind);

                // No fill: the corner of a cell is nothing; a fill is something to point at.
                Assert.Equal(TablePart.None, view.HitTest(new Point(18, 84)).Part);
                model.Fill = model.Fills.Single(f => f.Name == "Color 8");
                Assert.Equal(TablePart.Fill, view.HitTest(new Point(18, 84)).Part);
                Assert.Equal(TablePart.None, view.HitTest(new Point(300, 4)).Part);

                // A line that is not drawn is not there to point at.
                model.Settings.Data.LinesShown[0] = false;
                Assert.NotEqual(TablePart.TopEdge, view.HitTest(new Point(100, 29)).Part);
            });
        }

        [Fact]
        public void APartOfTheCellStylePreviewClicked_GoesToTheFirstFieldOfTheTabShown_ElseTheFirstOfTheTabs()
        {
            OnSta(() =>
            {
                TableStyleModifyViewModel model = Dialog();
                model.CellStyle = "Data";
                var asked = new List<string>();
                model.FocusRequested += (s, key) => asked.Add(key);
                var words = new TableTarget(DxfRowType.DataRow, TablePart.Text);

                model.Tab = "General";
                model.Preview.PointerAt(words);
                Assert.StartsWith("Words · ", model.Preview.HoverText);
                Assert.Contains("Text height", model.Preview.HoverText);
                Assert.Contains("TextHeight", model.LitSettings);
                model.Preview.Click();
                Assert.Equal("General", model.Tab);
                Assert.Equal("Alignment", asked.Last());

                model.Tab = "Text";
                model.Preview.PointerAt(default);
                model.Preview.PointerAt(words);
                model.Preview.Click();
                Assert.Equal("Text", model.Tab);
                Assert.Equal("TextStyle", asked.Last());

                model.Tab = "Borders";
                model.Preview.PointerAt(default);
                model.Preview.PointerAt(words);
                model.Preview.Click();
                Assert.Equal("General", model.Tab);
                Assert.Equal("Alignment", asked.Last());
                Assert.Equal("Alignment", model.ActiveSetting);

                // A line goes to the Borders tab.
                model.Preview.PointerAt(default);
                model.Preview.PointerAt(new TableTarget(DxfRowType.DataRow, TablePart.TopEdge));
                Assert.Equal("Top border · Lineweight, Color", model.Preview.HoverText);
                model.Preview.Click();
                Assert.Equal("Borders", model.Tab);
                Assert.Equal("BorderWeight", asked.Last());
            });
        }

        [Fact]
        public void TheTableIsFiveColumnsOf25Millimetres_TitleElevenHigh_AndItsRowsRunUpWhereTheStyleSaysSo()
        {
            OnSta(() =>
            {
                TableStyleModifyViewModel model = Dialog();
                TablePreviewLayout down = TablePreviewLayout.Of(model.Settings, null);

                Assert.Equal(125.0, down.Width, 9);
                Assert.Equal(47.0, down.Height, 9);
                Assert.Equal(
                    new[]
                    {
                        DxfRowType.TitleRow,
                        DxfRowType.HeaderRow,
                        DxfRowType.DataRow,
                        DxfRowType.DataRow,
                        DxfRowType.DataRow,
                    },
                    down.Rows.Select(r => r.Kind).ToArray()
                );
                Assert.Equal(new[] { 11.0, 9.0, 9.0, 9.0, 9.0 }, down.Rows.Select(r => r.Bounds.Height).ToArray());
                Assert.Equal(new[] { true, true, true, false, false }, down.Rows.Select(r => r.First).ToArray());
                Assert.Equal(new[] { true, true, false, false, true }, down.Rows.Select(r => r.Last).ToArray());

                model.Direction = "Up";
                TablePreviewLayout up = TablePreviewLayout.Of(model.Settings, null);

                Assert.Equal(DxfRowType.TitleRow, up.Rows[4].Kind);
                Assert.Equal(DxfRowType.HeaderRow, up.Rows[3].Kind);
                Assert.Equal(47.0, up.Rows[4].Bounds.Bottom, 9);
                Assert.True(up.Rows[0].First);
                Assert.False(up.Rows[0].Last);
                Assert.True(up.Rows[2].Last);

                // A cell style alone: one row, three columns.
                TablePreviewLayout alone = TablePreviewLayout.Of(model.Settings, "Title");
                Assert.Equal(75.0, alone.Width, 9);
                Assert.Equal(11.0, alone.Height, 9);
                Assert.True(alone.Rows[0].First && alone.Rows[0].Last);
            });
        }

        [Fact]
        public void TheWordsAreDrawnAtTheirTextHeightTimesTheScale_NotClamped()
        {
            OnSta(() =>
            {
                _ = PackUriHelper.UriSchemePack;
                TableStyleModifyViewModel model = Dialog();
                TableStylePreview table = Preview(model, 328, 240, null);
                TableStylePreview cell = Preview(model, 560, 120, "Data");

                // 308 DIP across 125 mm.
                Assert.Equal(308.0 / 125.0, table.Scale, 9);
                Assert.Equal(4.5 * table.Scale, table.WordSize(DxfRowType.DataRow), 9);
                Assert.Equal(6.0 * table.Scale, table.WordSize(DxfRowType.TitleRow), 9);
                Assert.Equal(7.2, cell.Scale, 9);
                Assert.Equal(32.4, cell.WordSize(DxfRowType.DataRow), 9);

                // 20 mm text in 9 mm rows would not fit the width: the table is drawn smaller, the words are still to scale.
                model.TextHeight = "9";
                Assert.Equal(9.0 * table.Scale, table.WordSize(DxfRowType.DataRow), 9);
                Assert.True(table.WordSize(DxfRowType.DataRow) > 16.0);
            });
        }

        [Fact]
        public void TheMagnifier_IsDrawnForASmallMarginInTheTablePreview_AndNotInTheCellStylePreview()
        {
            OnSta(() =>
            {
                _ = PackUriHelper.UriSchemePack;
                TableStyleModifyViewModel model = Dialog();
                model.CellStyle = "Data";
                TableStylePreview table = Preview(model, 328, 240, null);
                TableStylePreview cell = Preview(model, 560, 120, "Data");
                model.SetFocused("HorizontalMargin", true);

                table.UpdateLayout();
                cell.UpdateLayout();
                new RenderTargetBitmap(328, 240, 96, 96, PixelFormats.Pbgra32).Render(table);
                new RenderTargetBitmap(560, 120, 96, 96, PixelFormats.Pbgra32).Render(cell);

                // 1.5 mm is 3.7 DIP in the table preview, under 6: eight times.
                Assert.Equal(132.0, table.InsetFrame.Width);
                Assert.Equal(100.0, table.InsetFrame.Height);
                Assert.True(cell.InsetFrame.IsEmpty);

                // The text height is a callout that is never magnified, small as it is in the table preview.
                model.SetFocused("HorizontalMargin", false);
                model.SetFocused("TextHeight", true);
                table.UpdateLayout();
                new RenderTargetBitmap(328, 240, 96, 96, PixelFormats.Pbgra32).Render(table);
                Assert.True(table.InsetFrame.IsEmpty);
            });
        }

        [Fact]
        public void TheLitWords_AreDrawnInTheAccent_AndTheRestIsFaded()
        {
            OnSta(() =>
            {
                _ = PackUriHelper.UriSchemePack;
                TableStyleModifyViewModel model = Dialog();
                model.CellStyle = "Data";
                TableStylePreview cell = Preview(model, 560, 120, "Data");
                model.SetFocused("TextColor", true);
                cell.UpdateLayout();
                var target = new RenderTargetBitmap(560, 120, 96, 96, PixelFormats.Pbgra32);
                target.Render(cell);

                int accent = 0;
                int ink = 0;
                var pixels = new byte[560 * 120 * 4];
                target.CopyPixels(pixels, 560 * 4, 0);

                for (int i = 0; i < pixels.Length; i += 4)
                {
                    // Pbgra32: blue, green, red.
                    if (
                        Math.Abs(pixels[i + 2] - 0x0A) < 8
                        && Math.Abs(pixels[i + 1] - 0x6B) < 8
                        && Math.Abs(pixels[i] - 0xD6) < 8
                    )
                    {
                        accent++;
                    }

                    if (pixels[i + 2] < 0x30 && pixels[i + 1] < 0x30 && pixels[i] < 0x30)
                    {
                        ink++;
                    }
                }

                // The words of Data in the accent; no dark line or word left, all faded.
                Assert.True(accent > 400, "accent pixels " + accent);
                Assert.Equal(0, ink);

                // Nothing active: the words and lines are dark again.
                model.SetFocused("TextColor", false);
                cell.UpdateLayout();
                target.Clear();
                target.Render(cell);
                target.CopyPixels(pixels, 560 * 4, 0);
                int dark = 0;

                for (int i = 0; i < pixels.Length; i += 4)
                {
                    if (pixels[i + 2] < 0x30 && pixels[i + 1] < 0x30 && pixels[i] < 0x30)
                    {
                        dark++;
                    }
                }

                Assert.True(dark > 400, "dark pixels " + dark);
            });
        }

        [Fact]
        public void ThePointerOnARowOfAnotherCellStyle_LeavesTheCellStylePreviewAsItIs_AndFadesTheRestOfTheTable()
        {
            OnSta(() =>
            {
                _ = PackUriHelper.UriSchemePack;
                TableStyleModifyViewModel model = Dialog();
                model.CellStyle = "Data";
                TableStylePreview table = Preview(model, 328, 240, null);
                TableStylePreview cell = Preview(model, 560, 120, "Data");
                model.Preview.PointerAt(new TableTarget(DxfRowType.HeaderRow, TablePart.Cells));
                table.UpdateLayout();
                cell.UpdateLayout();

                (int accent, int dark) Count(TableStylePreview view, int width, int height)
                {
                    var target = new RenderTargetBitmap(width, height, 96, 96, PixelFormats.Pbgra32);
                    target.Render(view);
                    var pixels = new byte[width * height * 4];
                    target.CopyPixels(pixels, width * 4, 0);
                    int accent = 0;
                    int dark = 0;

                    for (int i = 0; i < pixels.Length; i += 4)
                    {
                        if (
                            Math.Abs(pixels[i + 2] - 0x0A) < 8
                            && Math.Abs(pixels[i + 1] - 0x6B) < 8
                            && Math.Abs(pixels[i] - 0xD6) < 8
                        )
                        {
                            accent++;
                        }

                        if (pixels[i + 2] < 0x30 && pixels[i + 1] < 0x30 && pixels[i] < 0x30)
                        {
                            dark++;
                        }
                    }

                    return (accent, dark);
                }

                (int tableAccent, int tableDark) = Count(table, 328, 240);
                (int cellAccent, int cellDark) = Count(cell, 560, 120);

                // The header row is lit and the rest of the table faded; the cell style preview shows Data, which nothing lit touches.
                Assert.True(tableAccent > 200, "accent " + tableAccent);
                Assert.Equal(0, tableDark);
                Assert.Equal(0, cellAccent);
                Assert.True(cellDark > 400, "dark " + cellDark);
            });
        }

        [Fact]
        public void TheDialog_Is940By660AndResizable_ItsPreviewsAreTheDesignsSizes_AndItsRowsLightWithTheSetting()
        {
            OnSta(() =>
            {
                _ = PackUriHelper.UriSchemePack;
                TableStyleModifyViewModel model = Dialog();
                var window = new TableStyleModifyWindow(model);
                Assert.Equal(940, window.Width);
                Assert.Equal(660, window.Height);
                Assert.Equal(940, window.MinWidth);
                Assert.Equal(660, window.MinHeight);
                Assert.Equal(ResizeMode.CanResizeWithGrip, window.ResizeMode);

                FrameworkElement body = window.Body;
                object context = window.DataContext;
                window.Content = null;
                body.DataContext = context;
                body.Measure(new Size(window.Width, window.Height));
                body.Arrange(new Rect(0, 0, window.Width, window.Height));
                body.UpdateLayout();
                List<TableStylePreview> previews = Descendants<TableStylePreview>(body).ToList();

                Assert.Equal(2, previews.Count);
                TableStylePreview table = previews.Single(p => p.CellStyle == null);
                TableStylePreview cell = previews.Single(p => p.CellStyle != null);
                Assert.InRange(table.ActualWidth, 326, 332);
                Assert.InRange(table.ActualHeight, 236, 240);
                Assert.InRange(cell.ActualWidth, 556, 562);
                Assert.InRange(cell.ActualHeight, 116, 120);

                // A setting hovered lights its row of the dialog, the Cell styles box when the pointer is on a row of the table.
                Func<int> lit = () =>
                    Descendants<Border>(body)
                        .Count(b => Panel.GetZIndex(b) == -1 && b.Visibility == Visibility.Visible);
                Assert.Equal(0, lit());
                model.SetHovered("Fill", true);
                Assert.Equal(1, lit());
                model.SetHovered("Fill", false);
                Assert.Equal(0, lit());
                model.Preview.PointerAt(new TableTarget(DxfRowType.HeaderRow, TablePart.Cells));
                Assert.Equal(1, lit());

                // The control of a row is named by its row: the box of the horizontal margin.
                Grid general = Descendants<Grid>(body)
                    .First(g => TableStyleFieldLinks.GetRowKeys(g)?.Contains("HorizontalMargin") == true);
                UIElement box = general.Children.OfType<TextBox>().First(t => Grid.GetRow(t) == 5);
                Assert.Equal("HorizontalMargin", TableStyleFieldLinks.KeyOf(box, body));
                Assert.Null(
                    TableStyleFieldLinks.KeyOf(
                        general.Children.OfType<TextBlock>().First(t => Grid.GetRow(t) == 4),
                        body
                    )
                );
                window.Close();
            });
        }

        [Fact]
        public void ARedrawPerValue_TakesUnder30Milliseconds_At100And150PercentWithTheMagnifier()
        {
            OnSta(() =>
            {
                _ = PackUriHelper.UriSchemePack;

                foreach (double dpi in new[] { 1.0, 1.5 })
                {
                    TableStyleModifyViewModel model = Dialog();
                    model.CellStyle = "Data";
                    TableStylePreview table = Preview(model, 328, 240, null, dpi);
                    TableStylePreview cell = Preview(model, 560, 120, "Data", dpi);
                    var first = new RenderTargetBitmap(
                        (int)(328 * dpi),
                        (int)(240 * dpi),
                        96 * dpi,
                        96 * dpi,
                        PixelFormats.Pbgra32
                    );
                    var second = new RenderTargetBitmap(
                        (int)(560 * dpi),
                        (int)(120 * dpi),
                        96 * dpi,
                        96 * dpi,
                        PixelFormats.Pbgra32
                    );
                    model.SetFocused("HorizontalMargin", true);
                    first.Render(table);
                    second.Render(cell);
                    var times = new List<double>();

                    for (int i = 0; i < 20; i++)
                    {
                        var watch = Stopwatch.StartNew();
                        model.HorizontalMargin = (1.0 + i * 0.05).ToString(
                            System.Globalization.CultureInfo.InvariantCulture
                        );
                        table.InvalidateVisual();
                        cell.InvalidateVisual();
                        table.UpdateLayout();
                        first.Render(table);
                        second.Render(cell);
                        watch.Stop();
                        times.Add(watch.Elapsed.TotalMilliseconds);
                    }

                    times.Sort();
                    double median = times[times.Count / 2];
                    Assert.True(median < 30.0, "median " + median.ToString("0.0") + " ms at " + dpi);
                }
            });
        }
    }
}
