using System;
using System.Collections.Generic;
using System.IO;
using System.IO.Packaging;
using System.Linq;
using System.Threading;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Documents;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using GeometryHelper.Dxf;
using GeometryHelper.Geometry;
using OpenCAD2D.SheetSets;
using Xunit;

namespace OpenCAD2D.Wpf.UnitTest
{
    /// <summary>
    /// The Sheet Views and Model Views tabs of the Sheet Set Manager made on a thread of their own and laid out with no window shown
    /// (A12 4d): what the designs draw is there (the toolbars, the trees, the two dialogs), and with <c>OPENCAD2D_SSM_PNG_DIR</c> set
    /// the states of the boards are drawn to PNG files in that folder.
    /// </summary>
    public class SheetSetViewsTabWindowTests : IDisposable
    {
        private readonly TestFolder _folder = new TestFolder();

        public void Dispose() => _folder.Dispose();

        private string SetFolder => Path.Combine(_folder.FullPath, "Example Tower");

        private string Dst => Path.Combine(SetFolder, "Example Tower.dst");

        private string ModelFolder => Path.Combine(_folder.FullPath, "Model");

        private static string Drawing(string folder, string file, params string[] layouts)
        {
            Directory.CreateDirectory(folder);
            var database = new DxfDatabase("AC1032");
            database.Layouts.Single(l => l.LayoutName == "Layout1").LayoutName = layouts[0];

            foreach (string name in layouts.Skip(1))
            {
                var layout = new DxfLayout { LayoutName = name };
                layout.AddToLayoutDictionary(database, null);
                layout.Initialize();
            }

            foreach (string name in layouts)
            {
                database
                    .Layouts.Single(l => l.LayoutName == name)
                    .BlockTableRecord.AppendEntity(new DxfLine(new GeoPoint3(10, 0, 0), new GeoPoint3(10, 50, 0)));
            }

            database.ModelSpace.AppendEntity(new DxfCircle(new GeoPoint3(0, 0, 0), GeoVector3.ZAxis, 5));
            string path = Path.Combine(folder, file);
            CadDocument.From(database, file).SaveAs(path);
            return path;
        }

        private string Model(string file, params string[] views)
        {
            var database = new DxfDatabase("AC1032");
            database.Insunits = DxfUnits.Millimeters;
            database.ModelSpace.AppendEntity(new DxfLine(new GeoPoint3(0, 0, 0), new GeoPoint3(32000, 18500, 0)));

            for (int i = 0; i < views.Length; i++)
            {
                database.ViewTable.Add(
                    new DxfViewTableRecord
                    {
                        Name = views[i],
                        CenterPoint = new GeoPoint2(16000, 9250),
                        Width = i == 0 ? 32000 : 16000,
                        Height = i == 0 ? 18500 : 8000,
                    }
                );
            }

            Directory.CreateDirectory(ModelFolder);
            string path = Path.Combine(ModelFolder, file);
            CadDocument.From(database, file).SaveAs(path);
            return path;
        }

        /// <summary>
        /// Example Tower as the boards draw it: the sheets A-101 to A-103, A-201 and A-501, the view categories Plans, Elevations,
        /// Sections and Details with their views, the views of the sheets by sheet with the scales the boards give (placed in the
        /// sheets' drawings), and a resource location of model drawings.
        /// </summary>
        private void Tower()
        {
            Directory.CreateDirectory(SetFolder);
            string dwt = Drawing(Path.Combine(_folder.FullPath, "templates"), "sheets.dwt", "Cover", "A1 sheet");
            string model = Model("Tower model.dwg", "Ground floor", "First floor", "Roof", "Section A-A");
            Model("Site plan.dwg");
            Model("Typical details.dwg", "Detail 1");
            CadSheetSetFile file = CadSheetSetFile.Create(Dst, "Example Tower", null);
            file.Change(set =>
            {
                set.Template = new CadDrawingReference(
                    CadFileReference.FromPath(dwt, SetFolder),
                    "A1 sheet",
                    null,
                    null
                );
                set.AddResourceLocation(CadFileReference.FromPath(ModelFolder, SetFolder));
                CadSheetSubset architectural = set.NewSubset("Architectural", null, false);
                architectural.NewSheet("A-101", "Ground Floor Plan");
                architectural.NewSheet("A-102", "First Floor Plan");
                architectural.NewSheet("A-103", "Roof Plan");
                architectural.NewSheet("A-201", "North and South Elevations");
                architectural.NewSheet("A-501", "Wall Sections");
            });

            // The views are placed on the sheets' drawings with the scales of the board, then recorded in the set.
            var plan = new Dictionary<
                string,
                (string View, string Number, string Title, string Scale, string Category)[]
            >
            {
                ["A-101"] = new[]
                {
                    ("Ground floor", "1", "Ground Floor Plan", "1:100", "Plans"),
                    ("Roof", "2", "Entrance Detail", "1:20", null),
                },
                ["A-102"] = new[] { ("First floor", "1", "First Floor Plan", "1:100", "Plans") },
                ["A-103"] = new[] { ("Roof", "1", "Roof Plan", "1:100", "Plans") },
                ["A-201"] = new[]
                {
                    ("Ground floor", "1", "North Elevation", "1:100", "Elevations"),
                    ("First floor", "2", "South Elevation", "1:100", "Elevations"),
                },
                ["A-501"] = new[]
                {
                    ("Section A-A", "1", "Section A-A", "1:50", "Sections"),
                    ("Ground floor", "2", "Section B-B", "1:50", "Sections"),
                    ("Detail 1", "3", "Entrance Canopy", "1:20", null),
                },
            };

            foreach (
                KeyValuePair<
                    string,
                    (string View, string Number, string Title, string Scale, string Category)[]
                > sheet in plan.OrderBy(p => p.Key, StringComparer.Ordinal)
            )
            {
                CadSheetSetSheet row = CadSheetSetFile
                    .Open(Dst)
                    .SheetSet.AllSheets()
                    .Single(s => s.Number == sheet.Key);
                string drawing = row.Layout.File.Resolve(SetFolder);
                CadDocument document = CadDocument.Open(drawing);
                document.CurrentSpace = document.Spaces.Single(s => !s.IsModel);
                var placed = new List<CadPlaceOnSheetResult>();
                int at = 100;

                foreach (var view in sheet.Value)
                {
                    string source = view.View == "Detail 1" ? Path.Combine(ModelFolder, "Typical details.dwg") : model;
                    placed.Add(
                        CadSheetSetPlacement.Place(
                            document,
                            document.CurrentSpace,
                            CadSheetSetPlacement.Plan(source, view.View),
                            new GeoPoint2(at, 200),
                            view.Scale
                        )
                    );
                    at += 250;
                }

                document.Save();
                CadSheetSetFile
                    .Open(Dst)
                    .Change(set =>
                    {
                        CadSheetSetSheet target = set.AllSheets().Single(s => s.Number == sheet.Key);

                        for (int i = 0; i < sheet.Value.Length; i++)
                        {
                            CadViewCategory category =
                                sheet.Value[i].Category == null
                                    ? null
                                    : set.ViewCategories.FirstOrDefault(c => c.Name == sheet.Value[i].Category)
                                        ?? set.AddViewCategory(sheet.Value[i].Category, null);
                            target.AddView(
                                sheet.Value[i].Number,
                                sheet.Value[i].Title,
                                new CadDrawingReference(
                                    CadFileReference.FromPath(drawing, SetFolder),
                                    placed[i].NamedViewName,
                                    placed[i].NamedViewHandle,
                                    placed[i].NamedViewOwnerHandle
                                ),
                                category
                            );
                        }
                    });
            }

            CadSheetSetFile.Open(Dst).Change(set => set.AddViewCategory("Details", null));
        }

        private static void Layout(FrameworkElement body, double width, double height)
        {
            body.Width = width;
            body.Height = height;
            body.Measure(new Size(width, height));
            body.Arrange(new Rect(0, 0, width, height));
            body.UpdateLayout();
        }

        private static IEnumerable<DependencyObject> Descendants(DependencyObject parent)
        {
            for (int i = 0; i < VisualTreeHelper.GetChildrenCount(parent); i++)
            {
                DependencyObject child = VisualTreeHelper.GetChild(parent, i);
                yield return child;

                foreach (DependencyObject below in Descendants(child))
                {
                    yield return below;
                }
            }
        }

        private static SheetSetManagerWindow Made(SheetSetManagerViewModel palette, out FrameworkElement body)
        {
            var window = new SheetSetManagerWindow(palette);
            body = window.Body;
            CadAppHost.TakeOut(window, body);
            body.DataContext = palette;
            Layout(body, 320, 610);
            return window;
        }

        private static string Png(FrameworkElement element, string name)
        {
            string folder = Environment.GetEnvironmentVariable("OPENCAD2D_SSM_PNG_DIR");

            if (string.IsNullOrEmpty(folder))
            {
                return null;
            }

            if (element.ActualWidth < 1)
            {
                element.Measure(new Size(2000, 2000));
                element.Arrange(new Rect(0, 0, element.DesiredSize.Width, element.DesiredSize.Height));
                element.UpdateLayout();
            }

            Directory.CreateDirectory(folder);
            var bitmap = new RenderTargetBitmap(
                (int)Math.Ceiling(element.ActualWidth),
                (int)Math.Ceiling(element.ActualHeight),
                96,
                96,
                PixelFormats.Pbgra32
            );
            bitmap.Render(element);
            var encoder = new PngBitmapEncoder();
            encoder.Frames.Add(BitmapFrame.Create(bitmap));
            string path = Path.Combine(folder, name + ".png");

            using (FileStream stream = File.Create(path))
            {
                encoder.Save(stream);
            }

            return path;
        }

        private static FrameworkElement MenuPicture(IReadOnlyList<CadMenuItemViewModel> items, double width)
        {
            ContextMenu menu = CadMenus.Make(items);
            List<object> parts = menu.Items.Cast<object>().ToList();
            menu.Items.Clear();
            var stack = new StackPanel();

            foreach (object part in parts)
            {
                stack.Children.Add((UIElement)part);
            }

            var frame = new Border
            {
                Width = width,
                Background = (Brush)menu.Background,
                BorderBrush = menu.BorderBrush,
                BorderThickness = new Thickness(1),
                Padding = new Thickness(0, 3, 0, 3),
                Child = stack,
            };
            TextElement.SetForeground(frame, (Brush)menu.Foreground);
            TextElement.SetFontFamily(frame, menu.FontFamily);
            TextElement.SetFontSize(frame, menu.FontSize);
            return frame;
        }

        private static void OnSta(Action test)
        {
            Exception failure = null;
            var thread = new Thread(() =>
            {
                try
                {
                    test();
                }
                catch (Exception ex)
                {
                    failure = ex;
                }
            });
            thread.SetApartmentState(ApartmentState.STA);
            thread.Start();
            thread.Join();
            Assert.Null(failure);
        }

        private static SheetSetTabRow Row(IEnumerable<SheetSetTabRow> rows, string text) =>
            rows.First(r => r.Text == text);

        [Fact]
        public void TheSheetViewsTab_HasTheToolbarOfTheDesign_AndTheTreeOfTheSetInPlaceOfTheSheetListsOwn()
        {
            OnSta(() =>
            {
                _ = PackUriHelper.UriSchemePack;
                Tower();
                var model = new MainViewModel();
                SheetSetManagerViewModel palette = model.SheetSetManager;
                palette.OpenSet(Dst);
                SheetSetManagerWindow window = Made(palette, out FrameworkElement body);

                // On the Sheet List tab the host of the other tabs is empty.
                Assert.Empty(Descendants(body).OfType<SheetViewsTab>());

                palette.Tab = "Sheet Views";
                Layout(body, 320, 610);

                SheetViewsTab tab = Descendants(body).OfType<SheetViewsTab>().Single();
                Assert.Equal(
                    new[] { "New View Category", "View by Category", "View by Sheet" },
                    Descendants(tab)
                        .OfType<FrameworkElement>()
                        .Select(e => e.GetValue(System.Windows.Automation.AutomationProperties.NameProperty) as string)
                        .Where(n => !string.IsNullOrEmpty(n))
                        .Take(3)
                        .ToArray()
                );
                Assert.True(tab.TreeList.Items.Count > 5);
                Assert.Equal(SelectionMode.Extended, tab.TreeList.SelectionMode);
                List<string> words = Descendants(tab).OfType<TextBlock>().Select(t => t.Text).ToList();
                Assert.Contains("Example Tower", words);
                Assert.Contains("Plans", words);
                Assert.Contains("1 - Ground Floor Plan", words);
                Assert.Contains("A-101", words);
                Assert.True(tab.ActualHeight > 100);
                Assert.Equal(
                    1,
                    Descendants(body)
                        .OfType<ListBox>()
                        .Count(l => l.Visibility == Visibility.Visible && l.ActualHeight > 0)
                );

                // The toolbar of the Sheet List tab is hidden, and the tab radio buttons follow the model's.
                Assert.All(Descendants(body).OfType<Button>().Where(b => b.ToolTip as string == "Publish to PDF"), b =>
                    Assert.False(b.IsVisible)
                );
                palette.Tab = "Sheet List";
                Assert.Empty(Descendants(body).OfType<SheetViewsTab>());
                window.Close();
            });
        }

        [Fact]
        public void TheTwoViewByButtons_AreASwitch_TheTreeFollowsThem()
        {
            OnSta(() =>
            {
                _ = PackUriHelper.UriSchemePack;
                Tower();
                SheetSetManagerViewModel palette = new MainViewModel().SheetSetManager;
                palette.OpenSet(Dst);
                palette.Tab = "Sheet Views";
                Made(palette, out FrameworkElement body);
                SheetViewsTab tab = Descendants(body).OfType<SheetViewsTab>().Single();
                List<RadioButton> buttons = Descendants(tab).OfType<RadioButton>().ToList();

                Assert.Equal(2, buttons.Count);
                Assert.True(buttons[0].IsChecked);
                Assert.False(buttons[1].IsChecked);

                buttons[1].IsChecked = true;

                Assert.True(palette.ViewsBySheet);
                Assert.False(buttons[0].IsChecked);
                Assert.Equal("A-101 - Ground Floor Plan", ((SheetSetTabRow)tab.TreeList.Items[1]).Text);
                Assert.Equal("1:100", ((SheetSetTabRow)tab.TreeList.Items[2]).Detail);
            });
        }

        [Fact]
        public void TheModelViewsTab_HasTheToolbarOfTheDesign_TheLocationsDrawingsAndViews_AndThePaneCanBeHidden()
        {
            OnSta(() =>
            {
                _ = PackUriHelper.UriSchemePack;
                Tower();
                SheetSetManagerViewModel palette = new MainViewModel().SheetSetManager;
                palette.OpenSet(Dst);
                palette.Tab = "Model Views";
                Made(palette, out FrameworkElement body);
                ModelViewsTab tab = Descendants(body).OfType<ModelViewsTab>().Single();

                Assert.Equal(
                    new[] { "Refresh", "Add New Location" },
                    Descendants(tab)
                        .OfType<Button>()
                        .Select(b => b.GetValue(System.Windows.Automation.AutomationProperties.NameProperty) as string)
                        .ToArray()
                );
                Assert.Equal(4, tab.TreeList.Items.Count);

                palette.PaneShown = false;
                Layout(body, 320, 610);
                Assert.Equal(
                    Visibility.Collapsed,
                    Descendants(body).OfType<DockPanel>().First(d => d.Name == "Pane").Visibility
                );
                palette.PaneShown = true;
                Layout(body, 320, 610);
                Assert.Equal(
                    Visibility.Visible,
                    Descendants(body).OfType<DockPanel>().First(d => d.Name == "Pane").Visibility
                );
            });
        }

        [Fact]
        public void TheViewCategoryDialog_HasTheNameBox_TheGreyedCalloutList_AndOkGoesGreyOnAName()
        {
            OnSta(() =>
            {
                _ = PackUriHelper.UriSchemePack;
                var dialog = new ViewCategoryDialogViewModel(string.Empty, n =>
                    n.Length == 0 ? "A view category is named." : null
                );
                var window = new ViewCategoryWindow(dialog);
                CadAppHost.TakeOut(window, window.Body);
                window.Body.DataContext = dialog;
                Layout(window.Body, 406, 302);

                List<string> words = Descendants(window.Body).OfType<TextBlock>().Select(t => t.Text).ToList();
                Assert.Contains("View Category", words);
                Assert.Contains("Category name:", words);
                Assert.Contains("Select the callout blocks to be used in this category:", words);
                Assert.Contains("The set names no callout blocks.", words);
                Assert.Contains("A view category is named.", words);
                Button ok = Descendants(window.Body).OfType<Button>().Single(b => (b.Content as string) == "OK");
                Assert.False(ok.IsEnabled);
                Assert.False(
                    Descendants(window.Body)
                        .OfType<Button>()
                        .Single(b => (b.Content as string) == "Add Blocks…")
                        .IsEnabled
                );

                dialog.Name = "Details";
                Assert.True(ok.IsEnabled);
                window.Close();
            });
        }

        [Fact]
        public void TheRenameDialog_HasTheNumberAndTitleBoxes_ThePreviousAndNextButtons_AndTheSheetLine()
        {
            OnSta(() =>
            {
                _ = PackUriHelper.UriSchemePack;
                var views = new List<SheetViewEntry>
                {
                    new SheetViewEntry("a", "1", "Section A-A", "A-501 - Wall Sections"),
                    new SheetViewEntry("b", "2", "Section B-B", "A-501 - Wall Sections"),
                };
                var dialog = new RenameRenumberViewDialogViewModel(views, 0, (id, number, title) => true);
                var window = new RenameRenumberViewWindow(dialog);
                CadAppHost.TakeOut(window, window.Body);
                window.Body.DataContext = dialog;
                Layout(window.Body, 470, 222);

                List<string> words = Descendants(window.Body).OfType<TextBlock>().Select(t => t.Text).ToList();
                Assert.Contains("Number:", words);
                Assert.Contains("View title:", words);
                Assert.Contains("Sheet: A-501 - Wall Sections.", words);
                List<Button> buttons = Descendants(window.Body).OfType<Button>().ToList();
                Assert.False(buttons.Single(b => (b.Content as string) == "< Previous").IsEnabled);
                Assert.True(buttons.Single(b => (b.Content as string) == "Next >").IsEnabled);

                dialog.Next();

                Assert.True(buttons.Single(b => (b.Content as string) == "< Previous").IsEnabled);
                Assert.False(buttons.Single(b => (b.Content as string) == "Next >").IsEnabled);
                window.Close();
            });
        }

        /// <summary>With <c>OPENCAD2D_SSM_PNG_DIR</c> set, the boards' states drawn: both tabs, their menus, the details, the dialogs.</summary>
        [Fact]
        public void WithTheFolderSet_TheStatesOfTheBoardsAreDrawn()
        {
            OnSta(() =>
            {
                _ = PackUriHelper.UriSchemePack;

                if (string.IsNullOrEmpty(Environment.GetEnvironmentVariable("OPENCAD2D_SSM_PNG_DIR")))
                {
                    return;
                }

                Tower();
                var model = new MainViewModel();
                SheetSetManagerViewModel palette = model.SheetSetManager;
                palette.OpenSet(Dst);
                palette.Tab = "Sheet Views";
                SheetSetManagerWindow window = Made(palette, out FrameworkElement body);

                // Board 1: by category, the first view selected, Details collapsed as the board has it.
                Row(palette.ViewRows, "Details").IsExpanded = false;
                Row(palette.ViewRows, "1 - Ground Floor Plan").IsSelected = true;
                Layout(body, 320, 610);
                Png(body, "sheetviews-by-category");

                // Board 2: by sheet.
                palette.ViewsBySheet = true;
                Row(palette.ViewRows, "A-103 - Roof Plan").IsExpanded = false;
                Row(palette.ViewRows, "1 - First Floor Plan").IsSelected = true;
                Layout(body, 320, 610);
                Png(body, "sheetviews-by-sheet");

                // Board 3: the menu of a view with Set Category open.
                palette.ViewsByCategory = true;
                SheetSetTabRow view = Row(palette.ViewRows, "1 - Section A-A");
                IReadOnlyList<CadMenuItemViewModel> menu = palette.SheetViewsMenuFor(view);
                FrameworkElement top = MenuPicture(menu, 200);
                FrameworkElement sub = MenuPicture(menu.Single(i => i.Header == "Set Category").Items, 140);
                var pair = new StackPanel { Orientation = Orientation.Horizontal, Background = Brushes.Transparent };
                pair.Children.Add(top);
                pair.Children.Add(sub);
                Png(pair, "sheetviews-menu");

                // Board 4: the View Category dialog on Sections.
                var category = new ViewCategoryDialogViewModel("Sections", n => null);
                var categoryWindow = new ViewCategoryWindow(category);
                CadAppHost.TakeOut(categoryWindow, categoryWindow.Body);
                categoryWindow.Body.DataContext = category;
                Layout(categoryWindow.Body, 406, 302);
                Png(categoryWindow.Body, "sheetviews-category");

                // Board 5: Rename & Renumber View on Section A-A.
                var rename = new RenameRenumberViewDialogViewModel(
                    new List<SheetViewEntry>
                    {
                        new SheetViewEntry("a", "1", "Section A-A", "A-501 - Wall Sections"),
                        new SheetViewEntry("b", "2", "Section B-B", "A-501 - Wall Sections"),
                    },
                    0,
                    (id, number, title) => true
                );
                var renameWindow = new RenameRenumberViewWindow(rename);
                CadAppHost.TakeOut(renameWindow, renameWindow.Body);
                renameWindow.Body.DataContext = rename;
                Layout(renameWindow.Body, 470, 222);
                Png(renameWindow.Body, "sheetviews-rename");

                // The Model Views tab: the first drawing open, a view selected.
                palette.Tab = "Model Views";
                Row(palette.ModelRows, "Tower model.dwg").IsExpanded = true;
                Row(palette.ModelRows, "Ground floor").IsSelected = true;
                Layout(body, 320, 610);
                Png(body, "modelviews");

                IReadOnlyList<CadMenuItemViewModel> viewMenu = palette.ModelViewsMenuFor(
                    Row(palette.ModelRows, "Ground floor")
                );
                IReadOnlyList<CadMenuItemViewModel> locationMenu = palette.ModelViewsMenuFor(palette.ModelRows[0]);
                var menus = new StackPanel { Orientation = Orientation.Horizontal, Background = Brushes.Transparent };
                FrameworkElement first = MenuPicture(viewMenu, 160);
                FrameworkElement second = MenuPicture(locationMenu, 160);
                first.Margin = new Thickness(0, 0, 16, 0);
                first.VerticalAlignment = VerticalAlignment.Top;
                second.VerticalAlignment = VerticalAlignment.Top;
                menus.Children.Add(first);
                menus.Children.Add(second);
                Png(menus, "modelviews-menus");
                window.Close();
            });
        }
    }
}
