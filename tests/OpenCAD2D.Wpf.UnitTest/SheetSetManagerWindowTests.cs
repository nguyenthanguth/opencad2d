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
    /// The Sheet Set Manager palette made on a thread of its own and laid out with no window shown (A12 4a): what the design
    /// draws is there (the control, the three tabs, the toolbar, the tree with its icons, the pane under it), and with
    /// <c>OPENCAD2D_SSM_PNG_DIR</c> set the states of the design's boards are drawn to PNG files in that folder.
    /// </summary>
    public class SheetSetManagerWindowTests : IDisposable
    {
        private readonly TestFolder _folder = new TestFolder();

        public void Dispose() => _folder.Dispose();

        private string SetFolder => Path.Combine(_folder.FullPath, "Projects", "Example Tower");

        private string Dst => Path.Combine(SetFolder, "Example Tower.dst");

        private string Drawing(string folder, string file, params string[] layouts)
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

            // A title block and a plan on each sheet, so that the preview has something to show.
            foreach (string name in layouts)
            {
                DxfBlockTableRecord block = database.Layouts.Single(l => l.LayoutName == name).BlockTableRecord;
                block.AppendEntity(new DxfLine(new GeoPoint3(10, 10, 0), new GeoPoint3(400, 10, 0)));
                block.AppendEntity(new DxfLine(new GeoPoint3(400, 10, 0), new GeoPoint3(400, 280, 0)));
                block.AppendEntity(new DxfLine(new GeoPoint3(400, 280, 0), new GeoPoint3(10, 280, 0)));
                block.AppendEntity(new DxfLine(new GeoPoint3(10, 280, 0), new GeoPoint3(10, 10, 0)));
                block.AppendEntity(new DxfCircle(new GeoPoint3(200, 150, 0), GeoVector3.ZAxis, 60));
            }

            string path = Path.Combine(folder, file);
            CadDocument.From(database, file).SaveAs(path);
            return path;
        }

        /// <summary>Example Tower as the design draws it, plus a sheet with no number.</summary>
        private void Tower()
        {
            Directory.CreateDirectory(SetFolder);
            string dwt = Drawing(Path.Combine(_folder.FullPath, "templates"), "sheets.dwt", "Cover", "A1 sheet");
            CadSheetSetFile file = CadSheetSetFile.Create(
                Dst,
                "Example Tower",
                "Six-storey residential tower, 48 flats"
            );
            file.Change(set =>
            {
                set.Template = new CadDrawingReference(
                    CadFileReference.FromPath(dwt, SetFolder),
                    "A1 sheet",
                    null,
                    null
                );
                set.NewSheet("G-001", "Cover Sheet");
                CadSheetSubset architectural = set.NewSubset("Architectural", null, false);
                architectural.NewSheet("A-101", "Ground Floor Plan").Description = "Plan at ground level, 1:100";
                architectural.NewSheet("A-102", "First Floor Plan");
                architectural.NewSheet("A-103", "Roof Plan");
                architectural.NewSheet("A-201", "North and South Elevations");
                architectural.NewSheet("A-202", "East and West Elevations");
                CadSheetSubset details = architectural.NewSubset("Details", null, false);
                details.NewSheet("A-501", "Wall Sections");
                details.NewSheet("A-502", "Stair Details");
                CadSheetSubset structural = set.NewSubset("Structural", null, false);
                structural.NewSheet("S-101", "Foundation Plan");
                structural.NewSheet("S-102", "First Floor Framing Plan");
                structural.NewSheet("S-501", "Typical Details");
            });
        }

        private static SheetSetNodeViewModel Node(SheetSetManagerViewModel palette, string text) =>
            palette.Rows.Single(r => r.Text == text);

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
            Layout(body, 320, 760);
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
                Settle(element);
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

        /// <summary>The items of a menu laid out as an open menu is, in a frame of a width, for a picture: the context menu's own items taken out of it.</summary>
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

        private static FrameworkElement Page(params FrameworkElement[] parts)
        {
            var row = new StackPanel
            {
                Orientation = Orientation.Horizontal,
                Background = new SolidColorBrush(Color.FromRgb(0x16, 0x19, 0x1E)),
            };

            foreach (FrameworkElement part in parts)
            {
                part.VerticalAlignment = VerticalAlignment.Top;
                part.Margin = new Thickness(0, 0, 24, 0);
                row.Children.Add(part);
            }

            row.Margin = new Thickness(0);
            return row;
        }

        private static void Settle(FrameworkElement element)
        {
            element.Measure(new Size(2000, 2000));
            element.Arrange(new Rect(0, 0, element.DesiredSize.Width, element.DesiredSize.Height));
            element.UpdateLayout();
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

        [Fact]
        public void TheWindow_HasTheControl_TheThreeTabs_TheToolbar_TheTree_AndThePaneOfTheDesign()
        {
            OnSta(() =>
            {
                _ = PackUriHelper.UriSchemePack;
                Tower();
                var model = new MainViewModel();
                var palette = new SheetSetManagerViewModel(model);
                palette.OpenSet(Dst);
                SheetSetManagerWindow window = Made(palette, out FrameworkElement body);

                Assert.Equal("Sheet Set Manager", window.Title);
                List<string> words = Descendants(body).OfType<TextBlock>().Select(t => t.Text).ToList();
                Assert.Contains("Sheet Set Manager", words);
                Assert.Contains("Example Tower", words);
                Assert.Equal(
                    new[] { "Sheet List", "Sheet Views", "Model Views", "Details", "Preview" },
                    Descendants(body).OfType<RadioButton>().Select(r => (string)r.Content).ToArray()
                );
                Assert.Equal(
                    new[] { "Publish to PDF", "Publish to Plotter", "Publish Using Page Setup Override", "Publish" },
                    Descendants(body)
                        .OfType<Button>()
                        .Select(b => (string)b.ToolTip)
                        .Where(t => t != null && t != "Close (SHEETSETHIDE)")
                        .ToArray()
                );
                Assert.All(Descendants(body).OfType<Button>().Where(b => b.ToolTip as string == "Publish to PDF"), b =>
                    Assert.True(b.IsEnabled)
                );
                ListBox tree = Descendants(body).OfType<ListBox>().Single();
                Assert.Equal(15, tree.Items.Count);
                Assert.Equal(SelectionMode.Extended, tree.SelectionMode);
                Assert.True(tree.AllowDrop);
                Assert.Contains("Architectural", words);
                Assert.Contains("A-101 - Ground Floor Plan", words);

                Button cross = Descendants(body)
                    .OfType<Button>()
                    .Single(b => b.Style == (Style)window.FindResource("GhTitleCloseButton"));
                Assert.Equal("Close (SHEETSETHIDE)", cross.ToolTip);
                window.Close();
            });
        }

        [Fact]
        public void TheIconsOfTheTree_ShowTheSetASubsetASheetAndWhetherItsOpenLeftOutOrMissing()
        {
            OnSta(() =>
            {
                _ = PackUriHelper.UriSchemePack;
                Tower();
                File.Delete(Path.Combine(SetFolder, "A-202 East and West Elevations.dwg"));
                CadSheetSetFile
                    .Open(Dst)
                    .Change(set => set.AllSheets().Single(s => s.Number == "S-501").IncludeForPublish = false);
                var model = new MainViewModel();
                Assert.True(model.Open(Path.Combine(SetFolder, "A-101 Ground Floor Plan.dwg")));
                var palette = new SheetSetManagerViewModel(model);
                palette.OpenSet(Dst);
                Made(palette, out FrameworkElement body);

                string[] glyphs = Descendants(body)
                    .OfType<SheetSetIcon>()
                    .Select(i => i.Glyph)
                    .Where(g => !g.StartsWith("chevron", StringComparison.Ordinal))
                    .ToArray();

                Assert.Contains("set", glyphs);
                Assert.Equal(3, glyphs.Count(g => g == "subset"));
                Assert.Single(glyphs, g => g == "sheet-open");
                Assert.Single(glyphs, g => g == "sheet-missing");
                Assert.Single(glyphs, g => g == "sheet-excluded");
                Assert.Equal(8, glyphs.Count(g => g == "sheet"));
            });
        }

        [Fact]
        public void WithNoSetOpen_TheBodySaysSo_AndTheToolbarIsOff()
        {
            OnSta(() =>
            {
                _ = PackUriHelper.UriSchemePack;
                var palette = new SheetSetManagerViewModel(new MainViewModel());
                Made(palette, out FrameworkElement body);

                TextBlock hint = Descendants(body)
                    .OfType<TextBlock>()
                    .Single(t =>
                        t.Text != null && t.Text.StartsWith("No sheet set is open.", StringComparison.Ordinal)
                    );
                Assert.Equal(Visibility.Visible, hint.Visibility);
                Assert.Equal(Visibility.Collapsed, Descendants(body).OfType<ListBox>().Single().Visibility);
                Assert.Contains("Open…", Descendants(body).OfType<TextBlock>().Select(t => t.Text));
                Border toolbar = Descendants(body)
                    .OfType<Border>()
                    .Single(b => b.Child is StackPanel s && s.Children.OfType<Button>().Count() == 4);
                Assert.False(toolbar.IsEnabled);
            });
        }

        [Fact]
        public void ALockedSet_ShowsItsLockByTheControl_WithTheReasonAsTheTooltip()
        {
            OnSta(() =>
            {
                _ = PackUriHelper.UriSchemePack;
                Tower();
                File.SetAttributes(Dst, FileAttributes.ReadOnly);

                try
                {
                    var palette = new SheetSetManagerViewModel(new MainViewModel());
                    palette.OpenSet(Dst);
                    Made(palette, out FrameworkElement body);

                    SheetSetIcon lockIcon = Descendants(body).OfType<SheetSetIcon>().Single(i => i.Glyph == "lock");
                    Assert.Equal(Visibility.Visible, lockIcon.Visibility);
                    Assert.Contains("read-only", (string)lockIcon.ToolTip, StringComparison.Ordinal);
                }
                finally
                {
                    File.SetAttributes(Dst, FileAttributes.Normal);
                }
            });
        }

        [Fact]
        public void ThePreviewSwitch_ShowsThePictureOfTheSheet_InPlaceOfTheDetails()
        {
            OnSta(() =>
            {
                _ = PackUriHelper.UriSchemePack;
                Tower();
                var palette = new SheetSetManagerViewModel(new MainViewModel());
                palette.OpenSet(Dst);
                Made(palette, out FrameworkElement body);
                Node(palette, "A-101 - Ground Floor Plan").IsSelected = true;
                body.UpdateLayout();
                ItemsControl details = Descendants(body).OfType<ItemsControl>().Single(i => !(i is ListBox));

                Assert.Equal(Visibility.Visible, details.Visibility);
                Assert.Equal(9, details.Items.Count);

                palette.ShowPreview = true;
                body.UpdateLayout();

                Assert.Equal(Visibility.Collapsed, details.Visibility);
                Image picture = Descendants(body).OfType<Image>().Single();
                Assert.NotNull(picture.Source);
                Assert.Equal(Visibility.Visible, ((UIElement)VisualTreeHelper.GetParent(picture)).Visibility);
            });
        }

        [Fact]
        public void WhereOnARowADropIs_DependsOnWhetherTheRowCanHoldRows()
        {
            Assert.Equal(SheetSetDropPosition.Before, SheetSetManagerWindow.PositionAt(true, 0.2));
            Assert.Equal(SheetSetDropPosition.After, SheetSetManagerWindow.PositionAt(true, 0.6));
            Assert.Equal(SheetSetDropPosition.Before, SheetSetManagerWindow.PositionAt(false, 0.1));
            Assert.Equal(SheetSetDropPosition.Into, SheetSetManagerWindow.PositionAt(false, 0.5));
            Assert.Equal(SheetSetDropPosition.After, SheetSetManagerWindow.PositionAt(false, 0.9));
        }

        [Fact]
        public void OpeningASheetInTheBackground_MakesItsLayoutCurrentOnceTheDrawingIsShown()
        {
            OnSta(() =>
            {
                _ = PackUriHelper.UriSchemePack;
                Tower();
                var model = new MainViewModel { OpensInBackground = true };
                var palette = new SheetSetManagerViewModel(model);
                palette.OpenSet(Dst);

                palette.OpenSheets(new[] { Node(palette, "A-102 - First Floor Plan") }, false);

                Assert.True(model.IsOpening);
                DateTime end = DateTime.UtcNow.AddMinutes(1);

                while (
                    (model.Document == null || model.Document.CurrentSpace.Name != "A-102 First Floor Plan")
                    && DateTime.UtcNow < end
                )
                {
                    var frame = new System.Windows.Threading.DispatcherFrame();
                    System.Windows.Threading.Dispatcher.CurrentDispatcher.BeginInvoke(
                        System.Windows.Threading.DispatcherPriority.Background,
                        new Action(() => frame.Continue = false)
                    );
                    System.Windows.Threading.Dispatcher.PushFrame(frame);
                    Thread.Sleep(5);
                }

                Assert.Equal("A-102 First Floor Plan", model.Document.CurrentSpace.Name);
                Assert.True(Node(palette, "A-102 - First Floor Plan").IsOpen);
            });
        }

        [Fact]
        public void TheBoardsStates_AreDrawnToPngFiles_WhereTheFolderIsSet()
        {
            if (string.IsNullOrEmpty(Environment.GetEnvironmentVariable("OPENCAD2D_SSM_PNG_DIR")))
            {
                return;
            }

            OnSta(() =>
            {
                _ = PackUriHelper.UriSchemePack;
                Tower();
                string drawings = SetFolder;
                File.Delete(Path.Combine(drawings, "A-202 East and West Elevations.dwg"));
                CadSheetSetFile
                    .Open(Dst)
                    .Change(set => set.AllSheets().Single(s => s.Number == "S-501").IncludeForPublish = false);
                var model = new MainViewModel();
                Assert.True(model.Open(Path.Combine(drawings, "A-102 First Floor Plan.dwg")));
                string other = Path.Combine(_folder.FullPath, "Riverside Clinic.dst");
                CadSheetSetFile.Create(other, "Riverside Clinic");
                model.NoteRecentSheetSet(@"\\server\share\offices\Harbour Offices.dst");

                // 1. No set open.
                var empty = new SheetSetManagerViewModel(model);
                Made(empty, out FrameworkElement noSet);
                Png(noSet, "1-no-set");

                // 2. The control opened: the palette with the list beside it.
                var palette = new SheetSetManagerViewModel(model);
                palette.OpenSet(other);
                palette.OpenSet(Dst);
                Made(palette, out FrameworkElement body);
                FrameworkElement list = MenuPicture(palette.ControlMenu(), 190);
                FrameworkElement recent = MenuPicture(
                    palette.ControlMenu().Single(i => i.Header == "Recent").Items,
                    200
                );
                Settle(list);
                Settle(recent);
                Png(Page(CopyOf(body), list, recent), "2-control");

                // 3. A sheet selected, the Details pane.
                Node(palette, "A-101 - Ground Floor Plan").IsSelected = true;
                Layout(body, 320, 760);
                Png(body, "3-details");

                // 4. A missing sheet selected.
                Node(palette, "A-101 - Ground Floor Plan").IsSelected = false;
                Node(palette, "A-202 - East and West Elevations").IsSelected = true;
                Layout(body, 320, 760);
                Png(body, "4-missing");

                // 5. The preview of a sheet.
                Node(palette, "A-202 - East and West Elevations").IsSelected = false;
                Node(palette, "A-101 - Ground Floor Plan").IsSelected = true;
                palette.ShowPreview = true;
                Layout(body, 320, 760);
                Png(body, "5-preview");

                // 6. A locked set.
                File.SetAttributes(Dst, FileAttributes.ReadOnly);

                try
                {
                    var locked = new SheetSetManagerViewModel(model);
                    locked.OpenSet(Dst);
                    Made(locked, out FrameworkElement lockedBody);
                    locked.Root.IsSelected = true;
                    Layout(lockedBody, 320, 760);
                    Png(lockedBody, "6-locked");
                    // The menus, each with its Publish submenu open, and the locked set's.

                    Png(Menu(locked, locked.Root, 220), "7-menu-set-locked");
                }
                finally
                {
                    File.SetAttributes(Dst, FileAttributes.Normal);
                }

                Png(Menu(palette, palette.Root, 220), "7-menu-set");
                Png(Menu(palette, Node(palette, "Architectural"), 200), "7-menu-subset");
                Png(Menu(palette, Node(palette, "A-101 - Ground Floor Plan"), 220), "7-menu-sheet");
            });
        }

        /// <summary>A menu and, beside it, the Publish submenu it opens, as the design draws them.</summary>
        private static FrameworkElement Menu(SheetSetManagerViewModel p, SheetSetNodeViewModel n, double width)
        {
            IReadOnlyList<CadMenuItemViewModel> items = p.MenuFor(n);
            FrameworkElement menu = MenuPicture(items, width);
            FrameworkElement publish = MenuPicture(items.Single(i => i.Header == "Publish").Items, 240);
            Settle(menu);
            Settle(publish);
            return Page(menu, publish);
        }

        /// <summary>A second palette of the same model, laid out again, so that a page can hold the palette and a list beside it.</summary>
        private static FrameworkElement CopyOf(FrameworkElement body)
        {
            var picture = new Border { Width = body.ActualWidth, Height = body.ActualHeight };
            picture.Background = new VisualBrush(body)
            {
                Stretch = Stretch.None,
                AlignmentX = AlignmentX.Left,
                AlignmentY = AlignmentY.Top,
            };
            picture.Measure(new Size(picture.Width, picture.Height));
            picture.Arrange(new Rect(0, 0, picture.Width, picture.Height));
            return picture;
        }
    }
}
