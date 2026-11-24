using System;
using System.Collections.Generic;
using System.IO.Packaging;
using System.Linq;
using System.Threading;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Data;
using System.Windows.Media;
using System.Windows.Shapes;
using Xunit;

namespace OpenCAD2D.Wpf.UnitTest
{
    /// <summary>
    /// The application menu button at the top left of the window (design MainWindow and AppMenu): the small OpenCAD2D mark,
    /// a white tile with an O and a C, in place of the letter G, and a ring of 2 px around it under the pointer, pressed and
    /// while the menu is open.
    /// </summary>
    public sealed class AppMenuButtonTests
    {
        [Fact]
        public void TheApplicationMenuButton_HoldsTheOpenCad2dMark_AndNoLetterG()
        {
            OnSta(() =>
            {
                CadWorkspace workspace = Workspace();
                var button = (Button)workspace.FindName("AppMenuButton");
                Assert.Equal(22, button.ActualWidth);
                Assert.Equal(22, button.ActualHeight);
                Assert.Equal("Application menu", button.ToolTip);
                Assert.Equal("Application menu", System.Windows.Automation.AutomationProperties.GetName(button));

                List<DependencyObject> below = Descendants(button).ToList();
                Assert.DoesNotContain(below, d => d is TextBlock);
                Assert.DoesNotContain(below, d => d is Image);

                // The mark drawn as vectors in a box of 256 units: the tile, the O and the C.
                List<Path> shapes = below.OfType<Path>().ToList();
                Assert.Equal(3, shapes.Count);
                Assert.Equal(Colors.White, ((SolidColorBrush)shapes[0].Fill).Color);
                Assert.Equal(Color.FromRgb(0xD5, 0xDB, 0xE3), ((SolidColorBrush)shapes[0].Stroke).Color);
                var tile = (RectangleGeometry)shapes[0].Data;
                Assert.Equal(new Rect(18, 18, 220, 220), tile.Rect);
                Assert.Equal(42, tile.RadiusX);
                var o = (EllipseGeometry)shapes[1].Data;
                Assert.Equal(new Point(128, 128), o.Center);
                Assert.Equal(84, o.RadiusX);
                Assert.Equal(24, shapes[1].StrokeThickness);
                Assert.Equal(Color.FromRgb(0x1F, 0x6F, 0xD6), ((SolidColorBrush)shapes[1].Stroke).Color);
                Assert.Equal(20, shapes[2].StrokeThickness);
                Assert.Equal(Color.FromRgb(0x1F, 0x6F, 0xD6), ((SolidColorBrush)shapes[2].Stroke).Color);
                Assert.NotNull(below.OfType<Viewbox>().Single());
            });
        }

        [Fact]
        public void TheRing_IsHiddenAtRest_AndShownHoveredPressedAndWhileTheMenuIsOpen()
        {
            OnSta(() =>
            {
                CadWorkspace workspace = Workspace();
                var button = (Button)workspace.FindName("AppMenuButton");
                var popup = (Popup)workspace.FindName("AppMenuPopup");
                Border ring = Descendants(button).OfType<Border>().Single(b => b.Name == "Ring");
                Assert.Equal(Visibility.Collapsed, ring.Visibility);
                Assert.Equal(new Thickness(2), ring.BorderThickness);
                Assert.Equal(Color.FromRgb(0x6C, 0xB6, 0xFF), ((SolidColorBrush)ring.BorderBrush).Color);

                // Hovered or pressed, the ring shows; the template sets it for each.
                foreach (string property in new[] { "IsMouseOver", "IsPressed" })
                {
                    Assert.Contains(button.Template.Triggers.OfType<Trigger>(), t =>
                        t.Property.Name == property
                        && t.Setters.OfType<Setter>()
                            .Any(s => s.TargetName == "Ring" && Equals(s.Value, Visibility.Visible))
                    );
                }

                // While the menu is open: the button's Tag follows the popup's IsOpen (a popup opens only in a shown window).
                Binding open = BindingOperations.GetBinding(button, FrameworkElement.TagProperty);
                Assert.Equal("IsOpen", open.Path.Path);
                Assert.Equal("AppMenuPopup", open.ElementName);
                Assert.NotNull(popup);

                // The Tag as the binding sets it when the popup is open: the ring shows, and hides again.
                button.Tag = true;
                workspace.UpdateLayout();
                Assert.Equal(Visibility.Visible, ring.Visibility);
                button.Tag = false;
                workspace.UpdateLayout();
                Assert.Equal(Visibility.Collapsed, ring.Visibility);
            });
        }

        private static CadWorkspace Workspace()
        {
            _ = PackUriHelper.UriSchemePack;
            var workspace = new CadWorkspace { DataContext = new MainViewModel() };
            workspace.Measure(new Size(1600, 960));
            workspace.Arrange(new Rect(0, 0, 1600, 960));
            workspace.UpdateLayout();
            return workspace;
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
            if (failure != null)
            {
                System.Runtime.ExceptionServices.ExceptionDispatchInfo.Capture(failure).Throw();
            }
        }
    }
}
