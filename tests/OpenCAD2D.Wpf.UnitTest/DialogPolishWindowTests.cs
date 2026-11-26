using System;
using System.Collections.Generic;
using System.IO;
using System.IO.Packaging;
using System.Linq;
using System.Threading;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using GeometryHelper.Dxf;
using GeometryHelper.Geometry;
using OpenCAD2D.SheetSets;
using Xunit;

namespace OpenCAD2D.Wpf.UnitTest
{
    /// <summary>
    /// The dialogs of the polish pass laid out in the test host: the tightest footers inside their windows, the Select Template
    /// list drawn by the theme's list style, the Plot Styles lists on the box ground, the Table View header in line with its
    /// cells, the Form View on the dialog's boxes with six between rows, and the save question read Save, Don't save, Cancel.
    /// </summary>
    public sealed class DialogPolishWindowTests
    {
        private static void OnSta(Action test)
        {
            Exception failure = null;
            var thread = new Thread(() =>
            {
                try
                {
                    _ = PackUriHelper.UriSchemePack;
                    test();
                }
                catch (Exception e)
                {
                    failure = e;
                }
            });
            thread.SetApartmentState(ApartmentState.STA);
            thread.Start();
            thread.Join();
            Assert.Null(failure);
        }

        private static IEnumerable<DependencyObject> Descendants(DependencyObject parent)
        {
            for (int i = 0; i < VisualTreeHelper.GetChildrenCount(parent); i++)
            {
                DependencyObject child = VisualTreeHelper.GetChild(parent, i);
                yield return child;

                foreach (DependencyObject deeper in Descendants(child))
                {
                    yield return deeper;
                }
            }
        }

        /// <summary>The window's content laid out at the size of the window, as a screenshot draws it.</summary>
        private static FrameworkElement Laid(Window window, FrameworkElement body, double height)
        {
            object context = window.DataContext;
            window.Content = null;
            body.DataContext = context;
            body.Measure(new Size(window.Width, height));
            body.Arrange(new Rect(0, 0, window.Width, height));
            body.UpdateLayout();
            return body;
        }

        /// <summary>The buttons of a body's footer band (the last 56 pixels) that are shown.</summary>
        private static List<Button> FooterButtons(FrameworkElement body, bool wholeBody) =>
            Descendants(body)
                .OfType<Button>()
                .Where(b => b.IsShown() && b.ActualWidth > 0 && b.Content is string)
                .Where(b =>
                    wholeBody
                    || body.ActualHeight - b.TransformToAncestor(body).Transform(new Point(0, b.ActualHeight)).Y <= 40
                )
                .ToList();

        private static void AssertTheFooterFits(
            string name,
            FrameworkElement body,
            double width,
            bool wholeBody = false
        )
        {
            List<Button> buttons = FooterButtons(body, wholeBody);
            Assert.True(buttons.Count >= 2, name + ": " + buttons.Count + " footer buttons found");

            foreach (Button button in buttons)
            {
                Rect box = button.TransformToAncestor(body).TransformBounds(new Rect(0, 0, button.ActualWidth, 1));
                Assert.True(
                    box.Right <= width - 1,
                    name + ": " + button.Content + " ends at " + box.Right + " of " + width
                );
                Assert.True(box.Left >= 0, name + ": " + button.Content + " starts at " + box.Left);
            }
        }

        // The windows laid out here are the ones whose footers are the tightest or the busiest (RenameRenumberView: five buttons in
        // 470; PdfLayers: four in 420; MTextFindReplace: four in 470; LoadSecurity: three of AutoCAD's long words; the others'
        // OK and Cancel in 290 to 420). Each is made over a model as the tests of its own window make it.
        [Fact]
        public void TheTightestFootersFitTheirWindows_WithTheFooterButtonSizeOfEveryDialog()
        {
            using (var folder = new TestFolder())
            {
                OnSta(() =>
                {
                    var views = new List<SheetViewEntry>
                    {
                        new SheetViewEntry("a", "1", "Section A-A", "A-501 - Wall Sections"),
                        new SheetViewEntry("b", "2", "Section B-B", "A-501 - Wall Sections"),
                    };
                    var rename = new RenameRenumberViewDialogViewModel(views, 0, (id, number, title) => true);
                    var renameWindow = new RenameRenumberViewWindow(rename);
                    CadAppHost.TakeOut(renameWindow, renameWindow.Body);
                    renameWindow.Body.DataContext = rename;
                    FitAt(renameWindow.Body, 470);
                    AssertTheFooterFits("RenameRenumberView", renameWindow.Body, 470);

                    var setupName = new PageSetupNameViewModel(
                        "New Page Setup",
                        new[] { "A2 plan" },
                        new[] { "<None>", "<Default output device>", "<Previous plot>", "*Model*", "A2 plan" }
                    )
                    {
                        ImportPageSetups = () => new[] { "A2 plan" },
                    };
                    var setupWindow = new PageSetupNameWindow(setupName);
                    FitAt(setupWindow.Body, 420);
                    AssertTheFooterFits("PageSetupName", setupWindow.Body, 420);

                    var sizeWindow = new MTextSizeWindow(new MTextSizeViewModel(false, 55.0));
                    var sizeBody = (FrameworkElement)sizeWindow.Content;
                    FitAt(sizeBody, 290);
                    AssertTheFooterFits("MTextSize", sizeBody, 290);

                    var importWindow = new SheetImportOptionsWindow(
                        new SheetImportOptionsViewModel(new CadSheetImportOptions())
                    );
                    CadAppHost.TakeOut(importWindow, importWindow.Body);
                    importWindow.Body.DataContext = importWindow.DataContext;
                    FitAt(importWindow.Body, 380);
                    AssertTheFooterFits("SheetImportOptions", importWindow.Body, 380);

                    var layers = new List<PdfLayer>
                    {
                        new PdfLayer
                        {
                            Id = 1,
                            Name = "Walls",
                            IsOn = true,
                        },
                    };
                    var request = new CadPdfLayersRequest(
                        "plan - Page_1",
                        new[] { "Walls" },
                        new Dictionary<string, bool>(),
                        layers
                    );
                    var pdfWindow = new PdfLayersWindow(new PdfLayersViewModel(request));
                    FitAt(pdfWindow.Body, 420);
                    AssertTheFooterFits("PdfLayers", pdfWindow.Body, 420);

                    var security = new LoadSecurityViewModel(SecurityKit.Code(folder, "tool.lsp"));
                    var securityWindow = new LoadSecurityWindow(security);
                    FitAt(securityWindow.Body, 616);
                    AssertTheFooterFits("LoadSecurity", securityWindow.Body, 616);

                    // MTextFindReplace needs a text editor open in a drawing: it is built as TextEditorToolsTests builds it.
                    var model = new MainViewModel();
                    var database = new DxfDatabase("AC1027");
                    var mtext = new DxfMText
                    {
                        Location = new GeoPoint3(0, 0, 0),
                        TextHeight = 2.5,
                        Width = 55.0,
                        Contents = "AB",
                    };
                    database.ModelSpace.AppendEntity(mtext);
                    CadDocument document = CadDocument.From(database, "notes.dxf");
                    model.OpenDocument(document, "notes.dxf");
                    Assert.True(model.EditTextAt(mtext.Handle));
                    var findWindow = new MTextFindReplaceWindow(model.TextEditor);
                    FitAt(findWindow.Body, 470);
                    AssertTheFooterFits("MTextFindReplace", findWindow.Body, 470, true);
                });
            }
        }

        private static void FitAt(FrameworkElement body, double width)
        {
            body.Measure(new Size(width, double.PositiveInfinity));
            body.Arrange(new Rect(0, 0, width, Math.Max(body.DesiredSize.Height, 100)));
            body.UpdateLayout();
        }

        [Fact]
        public void TheTemplateListIsDrawnByTheThemesListStyle()
        {
            using (var folder = new TestFolder())
            {
                OnSta(() =>
                {
                    var dialog = new SelectTemplateViewModel(folder.FullPath, new string[0]);
                    var window = new SelectTemplateWindow(dialog);
                    window.Body.Measure(new Size(520, 440));
                    window.Body.Arrange(new Rect(0, 0, 520, 440));
                    window.Body.UpdateLayout();

                    ListBox list = Descendants(window.Body)
                        .OfType<ListBox>()
                        .Single(l => System.Windows.Automation.AutomationProperties.GetName(l) == "Templates");
                    Assert.Same(window.FindResource("GhListBox"), list.Style);
                    Assert.Equal(new Thickness(1), list.BorderThickness);
                });
            }
        }

        [Fact]
        public void SaveDontSaveCancelReadLeftToRight()
        {
            OnSta(() =>
            {
                var database = new DxfDatabase("AC1027");
                CadDocument document = CadDocument.From(database, "notes.dxf");
                CadQuestionWindow question = CadQuestionWindow.ForSave(new[] { document });
                var buttons = (StackPanel)question.FindName("Buttons");

                Assert.Equal(
                    new[] { "Save", "Don't save", "Cancel" },
                    buttons
                        .Children.OfType<Button>()
                        .Where(b => b.Visibility == Visibility.Visible)
                        .Select(b => (string)b.Content)
                        .ToArray()
                );

                // And a two-button question has no gap left of its last button where the third would be.
                var two = new CadQuestionWindow("Title", "Text", "Detail", "Load", "Do not load");
                var twoButtons = (StackPanel)two.FindName("Buttons");
                two.Content = null;
                twoButtons.Measure(new Size(400, 100));
                twoButtons.Arrange(new Rect(0, 0, 400, 100));
                Button last = twoButtons.Children.OfType<Button>().First(b => "Do not load".Equals(b.Content));
                Assert.Equal(0.0, last.Margin.Right);
            });
        }

        [Fact]
        public void TheThreeListsStandOnTheBoxGround_NotBlack()
        {
            using (var folder = new TestFolder())
            {
                OnSta(() =>
                {
                    CadPlotStyleFolder.EnsureBuiltIns(folder.FullPath);
                    var window = new PlotStylesWindow(new PlotStylesManagerViewModel(folder.FullPath, false));
                    Color ground = ((SolidColorBrush)window.FindResource("GhInput")).Color;
                    FrameworkElement body = Laid(window, window.Body, 480);

                    ListBox files = Descendants(body).OfType<ListBox>().Single();
                    Assert.Equal(ground, ((SolidColorBrush)files.Background).Color);

                    var editor = new PlotStyleEditorViewModel(
                        CadPlotStyleFolder.BuiltIn("monochrome.ctb"),
                        @"D:\Plot Styles"
                    );
                    var editorWindow = new PlotStyleEditorWindow(editor);
                    FrameworkElement editorBody = Laid(editorWindow, editorWindow.Body, 680);
                    List<ListBox> lists = Descendants(editorBody).OfType<ListBox>().ToList();

                    Assert.Equal(2, lists.Count);
                    Assert.All(lists, l => Assert.Equal(ground, ((SolidColorBrush)l.Background).Color));
                });
            }
        }

        // The header and the cells of the Table View: the six cells with a name a header and a column share are checked, and a
        // style with no colour (its swatch hidden, not collapsed) keeps the same place as one with a colour.
        [Fact]
        public void TheTableViewHeaderStartsWhereTheCellsStart_AndScrollsWithThem()
        {
            OnSta(() =>
            {
                var editor = new PlotStyleEditorViewModel(
                    CadPlotStyleFolder.BuiltIn("monochrome.ctb"),
                    @"D:\Plot Styles"
                );
                editor.Rows[0].Color = "Use object color";
                editor.Rows[1].Color = "255,0,0";
                Assert.False(editor.Rows[0].HasColor);
                Assert.True(editor.Rows[1].HasColor);

                var window = new PlotStyleEditorWindow(editor);
                FrameworkElement body = Laid(window, window.Body, 680);
                editor.Tab = PlotStyleEditorViewModel.TableViewTab;
                body.UpdateLayout();

                ScrollViewer scroll = Descendants(body)
                    .OfType<ScrollViewer>()
                    .First(s => s.IsShown() && s.Content is DockPanel);
                string[][] columns =
                {
                    new[] { "Name", "Name" },
                    new[] { "Color", "Color" },
                    new[] { "Dither", "Enable dithering" },
                    new[] { "Pen", "Use assigned pen number" },
                    new[] { "Linetype", "Linetype" },
                    new[] { "Lineweight", "Lineweight" },
                    new[] { "Fill", "Fill style" },
                };

                foreach (double offset in new[] { 0.0, 100.0 })
                {
                    scroll.ScrollToHorizontalOffset(offset);
                    body.UpdateLayout();
                    var rows = Descendants(scroll).OfType<ListBoxItem>().Where(i => i.IsShown()).Take(2).ToList();
                    Assert.Equal(2, rows.Count);

                    foreach (string[] column in columns)
                    {
                        TextBlock head = Descendants(scroll)
                            .OfType<TextBlock>()
                            .First(t => t.Text == column[0] && t.IsShown() && t.Width > 0);
                        double headX = head.TransformToAncestor(body).Transform(new Point(0, 0)).X;

                        foreach (ListBoxItem row in rows)
                        {
                            FrameworkElement cell = Descendants(row)
                                .OfType<FrameworkElement>()
                                .First(e => System.Windows.Automation.AutomationProperties.GetName(e) == column[1]);
                            double cellX = cell.TransformToAncestor(body).Transform(new Point(0, 0)).X;
                            Assert.True(
                                Math.Abs(headX - cellX) <= 0.5,
                                column[0] + " at scroll " + offset + ": header " + headX + ", cell " + cellX
                            );
                        }
                    }
                }
            });
        }

        [Fact]
        public void TheFormViewUsesTheDialogsBoxes_WithSixBetweenRows()
        {
            OnSta(() =>
            {
                var editor = new PlotStyleEditorViewModel(
                    CadPlotStyleFolder.BuiltIn("monochrome.ctb"),
                    @"D:\Plot Styles"
                );
                editor.Tab = PlotStyleEditorViewModel.FormViewTab;
                var window = new PlotStyleEditorWindow(editor);
                FrameworkElement body = Laid(window, window.Body, 680);
                Grid grid = Descendants(body).OfType<Grid>().Single(g => g.RowDefinitions.Count == 14 && g.IsShown());

                object box = window.FindResource("StyleBox");
                List<TextBox> boxes = Descendants(grid)
                    .OfType<TextBox>()
                    .Where(b => b.TemplatedParent == null)
                    .ToList();
                Assert.Equal(6, boxes.Count);
                Assert.All(boxes, b => Assert.Same(box, b.Style));

                for (int row = 0; row < 14; row++)
                {
                    // The row is as high as its tallest cell and six more: three above and three below.
                    double tallest = grid
                        .Children.OfType<FrameworkElement>()
                        .Where(c => Grid.GetRow(c) == row)
                        .Max(c => c.ActualHeight);
                    // Layout rounding may add a pixel to a row; a row with no gap (today's) is six away.
                    double gap = grid.RowDefinitions[row].ActualHeight - tallest;
                    Assert.True(gap >= 6.0 - 0.5 && gap <= 6.0 + 1.0, "row " + row + ": " + gap);
                }
            });
        }
    }
}
