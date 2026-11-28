using System;
using System.Collections.Generic;
using System.IO;
using System.IO.Packaging;
using System.Linq;
using System.Threading;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Shell;
using GeometryHelper.Dxf;
using Xunit;

namespace OpenCAD2D.Wpf.UnitTest
{
    /// <summary>
    /// A double click on a style in the Styles list of a style manager (dimension, text, multileader, table, multiline) makes
    /// that style current, as the manager's Set Current does, and a resizable window with no frame of the system draws no light
    /// line along its top edge. The windows are made on a thread of their own and never shown.
    /// </summary>
    public sealed class StyleListDoubleClickAndWindowChromeTests : IDisposable
    {
        private readonly TestFolder _folder = new TestFolder();

        public void Dispose() => _folder.Dispose();

        private MainViewModel Opened(out CadDocument document)
        {
            var model = new MainViewModel { SettingsPath = Path.Combine(_folder.FullPath, "settings.json") };
            document = CadDocument.From(CadTemplate.CreateMetric(), "styles.dxf");
            model.OpenDocument(document);
            return model;
        }

        [Fact]
        public void ADoubleClickOnADimensionStyle_MakesItCurrent_AsOneUndoStep_AndDoesNotOpenModify()
        {
            OnSta(() =>
            {
                MainViewModel model = Opened(out CadDocument document);
                string start = CadDimStyleEditor.Current(document.Database);
                Assert.True(CadDimStyleEditor.Add(document, "OC_25", start, null).Succeeded);
                DimStyleManagerViewModel manager = model.CreateDimStyleManager();
                var window = new DimStyleManagerWindow(manager);
                bool modified = false;
                manager.ShowEditor = editor =>
                {
                    modified = true;
                    return false;
                };
                manager.SelectedStyle = manager.Styles.Single(s => s.Name == "OC_25");

                ListBox list = StyleList(window);
                DoubleClickOutsideTheRows(list);
                Assert.Equal(start, CadDimStyleEditor.Current(document.Database));
                DoubleClick(list);

                Assert.False(modified);
                Assert.Equal("OC_25", CadDimStyleEditor.Current(document.Database));
                Assert.Contains(manager.Styles, s => s.IsCurrent && s.Name == "OC_25");
                Assert.True(document.Undo());
                Assert.Equal(start, CadDimStyleEditor.Current(document.Database));
            });
        }

        [Fact]
        public void ADoubleClickOnATextStyle_MakesItCurrent_AsOneUndoStep()
        {
            OnSta(() =>
            {
                MainViewModel model = Opened(out CadDocument document);
                string start = CadTextStyleEditor.Current(document.Database);
                Assert.True(CadTextStyleEditor.Add(document, "PLAN", start).Succeeded);
                TextStyleViewModel dialog = model.CreateTextStyle();
                var window = new TextStyleWindow(dialog);
                dialog.SelectedStyle = dialog.Styles.Single(s => s.Name == "PLAN");

                ListBox list = StyleList(window);
                DoubleClickOutsideTheRows(list);
                Assert.Equal(start, CadTextStyleEditor.Current(document.Database));
                DoubleClick(list);

                Assert.Equal("PLAN", CadTextStyleEditor.Current(document.Database));
                Assert.True(document.Undo());
                Assert.Equal(start, CadTextStyleEditor.Current(document.Database));
            });
        }

        [Fact]
        public void ADoubleClickOnAMultileaderStyle_MakesItCurrent_AsOneUndoStep_AndDoesNotOpenModify()
        {
            OnSta(() =>
            {
                MainViewModel model = Opened(out CadDocument document);
                string start = CadMLeaderStyleEditor.Current(document.Database);
                Assert.True(CadMLeaderStyleEditor.Add(document, "NOTES", start).Succeeded);
                MLeaderStyleManagerViewModel manager = model.CreateMLeaderStyleManager();
                var window = new MLeaderStyleManagerWindow(manager);
                bool modified = false;
                manager.ShowEditor = editor =>
                {
                    modified = true;
                    return false;
                };
                manager.SelectedStyle = manager.Styles.Single(s => s.Name == "NOTES");

                ListBox list = StyleList(window);
                DoubleClickOutsideTheRows(list);
                Assert.Equal(start, CadMLeaderStyleEditor.Current(document.Database));
                DoubleClick(list);

                Assert.False(modified);
                Assert.Equal("NOTES", CadMLeaderStyleEditor.Current(document.Database));
                Assert.True(document.Undo());
                Assert.Equal(start, CadMLeaderStyleEditor.Current(document.Database));
            });
        }

        [Fact]
        public void ADoubleClickOnATableStyle_MakesItCurrent_AsOneUndoStep()
        {
            OnSta(() =>
            {
                MainViewModel model = Opened(out CadDocument document);
                string start = CadTableStyleEditor.Current(document.Database);
                Assert.True(CadTableStyleEditor.Add(document, "SCHEDULE", start).Succeeded);
                TableStyleManagerViewModel manager = model.CreateTableStyleManager();
                var window = new TableStyleWindow(manager);
                bool modified = false;
                manager.ShowModify = modify =>
                {
                    modified = true;
                    return false;
                };
                manager.SelectedStyle = manager.Styles.Single(s => s.Name == "SCHEDULE");

                ListBox list = StyleList(window);
                DoubleClickOutsideTheRows(list);
                Assert.Equal(start, CadTableStyleEditor.Current(document.Database));
                DoubleClick(list);

                Assert.False(modified);
                Assert.Equal("SCHEDULE", CadTableStyleEditor.Current(document.Database));
                Assert.True(document.Undo());
                Assert.Equal(start, CadTableStyleEditor.Current(document.Database));
            });
        }

        [Fact]
        public void ADoubleClickOnAMultilineStyle_PicksItAsTheCurrentOne_AndDoesNotOpenModify()
        {
            OnSta(() =>
            {
                MainViewModel model = Opened(out _);
                bool shown = false;
                bool modified = false;
                string before = null;
                string after = null;
                string outside = null;
                model.ShowMlineStyle = dialog =>
                {
                    shown = true;
                    var window = new MlineStyleWindow(dialog);
                    dialog.AskNewStyle = naming =>
                    {
                        naming.Name = "Wall";
                        return naming.TryContinue();
                    };
                    dialog.EditStyle = editing => editing.TryAccept();
                    dialog.New();
                    before = dialog.Selected + " " + dialog.CurrentName;
                    dialog.EditStyle = editing =>
                    {
                        modified = true;
                        return false;
                    };

                    ListBox list = StyleList(window);
                    DoubleClickOutsideTheRows(list);
                    outside = dialog.CurrentName;
                    DoubleClick(list);

                    after = dialog.CurrentName;
                    return false;
                };

                model.Execute("MLSTYLE");

                Assert.True(shown);
                Assert.Equal("WALL STANDARD", before);
                Assert.Equal("STANDARD", outside);
                Assert.False(modified);
                Assert.Equal("WALL", after);
            });
        }

        [Fact]
        public void AResizableWindowWithNoFrameOfTheSystem_HasAWindowChromeWithNoGlassFrame()
        {
            OnSta(() =>
            {
                MainViewModel model = Opened(out CadDocument document);
                MLeaderStyleManagerViewModel leaders = model.CreateMLeaderStyleManager();
                Assert.True(CadTableStyleEditor.Add(document, "SCHEDULE", "Standard").Succeeded);
                var windows = new Window[]
                {
                    new DimStyleEditWindow(model.CreateDimStyleManager().CreateModify()),
                    new MLeaderStyleEditWindow(leaders.CreateModify()),
                    new TableStyleModifyWindow(new TableStyleModifyViewModel(document, "SCHEDULE", false)),
                    new SheetSetManagerWindow(new SheetSetManagerViewModel(model)),
                    new TransmittalReportWindow("Example Tower", "report"),
                };

                foreach (Window window in windows)
                {
                    string name = window.GetType().Name;
                    Assert.Equal(WindowStyle.None, window.WindowStyle);
                    Assert.NotEqual(ResizeMode.NoResize, window.ResizeMode);
                    WindowChrome chrome = WindowChrome.GetWindowChrome(window);
                    Assert.True(
                        chrome != null,
                        name + " has no WindowChrome, so Windows draws a light line on its top edge"
                    );
                    Assert.Equal(new Thickness(0), chrome.GlassFrameThickness);
                    Assert.Equal(0.0, chrome.CaptionHeight);
                    Assert.True(chrome.ResizeBorderThickness.Top > 0.0, name);
                }
            });
        }

        private static ListBox StyleList(Window window)
        {
            FrameworkElement body = (FrameworkElement)window.Content;
            body.Measure(new Size(960, 660));
            body.Arrange(new Rect(0, 0, 960, 660));
            body.UpdateLayout();
            return Descendants(body).OfType<ListBox>().First();
        }

        /// <summary>A double click on the row of the style selected.</summary>
        private static void DoubleClick(ListBox list) =>
            Raise(list, (ListBoxItem)list.ItemContainerGenerator.ContainerFromItem(list.SelectedItem));

        /// <summary>A double click on the empty area of the list and on its scroll bar, which are no style.</summary>
        private static void DoubleClickOutsideTheRows(ListBox list)
        {
            Raise(list, list);
            Raise(list, Descendants(list).OfType<ScrollBar>().First());
        }

        private static void Raise(ListBox list, object source)
        {
            Assert.NotNull(source);
            list.RaiseEvent(
                new MouseButtonEventArgs(Mouse.PrimaryDevice, 0, MouseButton.Left)
                {
                    RoutedEvent = Control.MouseDoubleClickEvent,
                    Source = source,
                }
            );
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
    }
}
