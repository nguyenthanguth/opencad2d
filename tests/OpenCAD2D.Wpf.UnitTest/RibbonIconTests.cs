using System;
using System.Collections.Generic;
using System.IO.Packaging;
using System.Linq;
using System.Threading;
using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using Xunit;

namespace OpenCAD2D.Wpf.UnitTest
{
    /// <summary>
    /// The colour icons of the ribbon (A13): the dictionary of them, and the IconView that draws one: the small drawing under 24
    /// device pixels, the large one from 24, so that a 16 pixel icon keeps its whole-pixel lines at 100 % and at 150 %.
    /// </summary>
    public sealed class RibbonIconTests
    {
        private const double Grid = 32;
        private const double Slack = 0.001;

        [Fact]
        public void TheRibbonIconsDictionary_HoldsEveryIconWithALargeAndASmallDrawingInsideItsGrid()
        {
            OnSta(() =>
            {
                ResourceDictionary icons = Icons();
                var all = icons.Keys.Cast<object>().Where(k => icons[k] is RibbonIcon).Select(k => (string)k).ToList();
                var problems = new List<string>();

                Assert.Equal(382, all.Count);
                Assert.All(all, k => Assert.EndsWith(".Color", k, StringComparison.Ordinal));
                Assert.Contains("GhIconLine.Color", all);

                foreach (string key in all.OrderBy(k => k, StringComparer.Ordinal))
                {
                    var icon = (RibbonIcon)icons[key];

                    if (!icon.IsFrozen)
                    {
                        problems.Add(key + " is not frozen");
                    }

                    Check(key + " large", icon.Large, Grid, problems);
                    Check(key + " small", icon.Small, Grid / 2, problems);
                }

                Assert.True(problems.Count == 0, string.Join(Environment.NewLine, problems));
            });
        }

        [Fact]
        public void TheSmallDrawing_HoldsItsOwnScaleOfOneHalf_SoItsBoundsLieInSixteen()
        {
            OnSta(() =>
            {
                var line = (RibbonIcon)Icons()["GhIconLine.Color"];
                var small = Assert.IsType<DrawingGroup>(line.Small);
                var scale = Assert.IsType<ScaleTransform>(small.Transform);
                Assert.Equal(0.5, scale.ScaleX, 6);
                Assert.Equal(0.5, scale.ScaleY, 6);
                Assert.Null(((DrawingGroup)line.Large).Transform);
            });
        }

        [Fact]
        public void AnIconViewOfSixteen_DrawsTheSmallDrawingAt96Dpi_AndTheLargeOneAt144()
        {
            OnSta(() =>
            {
                RibbonIcon icon = Probe();

                // 16 pixels at 100 %: the small drawing (blue), whole.
                Assert.Equal(Blue, CentreOf(Render(16, 96, icon), 16));

                // 20 pixels at 125 %: still the small one.
                Assert.Equal(Blue, CentreOf(Render(16, 120, icon), 20));

                // 24 pixels at 150 %: the large drawing (green), scaled by 0.75.
                Assert.Equal(Green, CentreOf(Render(16, 144, icon), 24));

                // 32 pixels at 100 %: the large one, whole.
                Assert.Equal(Green, CentreOf(Render(32, 96, icon), 32));
            });
        }

        [Fact]
        public void AnIconViewOfSixteen_DrawsTheWholeSlotAtEveryDpi()
        {
            OnSta(() =>
            {
                RibbonIcon icon = Probe();
                foreach (var (dpi, pixels) in new[] { (96, 16), (120, 20), (144, 24), (192, 32) })
                {
                    BitmapSource picture = Render(16, dpi, icon);
                    Assert.Equal(pixels, picture.PixelWidth);
                    Assert.NotEqual(Transparent, Pixel(picture, 0, 0));
                    Assert.NotEqual(Transparent, Pixel(picture, pixels - 1, pixels - 1));
                }
            });
        }

        [Fact]
        public void AnIconViewWithAnIcon_DrawsItInsteadOfItsData_AndWithNoneDrawsItsData()
        {
            OnSta(() =>
            {
                var data = Geometry.Parse("M0 0h24v24h-24z");
                var view = new IconView
                {
                    Size = 24,
                    Data = data,
                    Fill = new SolidColorBrush(Color.FromRgb(255, 0, 0)),
                    Stroke = Brushes.Transparent,
                };
                Assert.Equal(Color.FromRgb(255, 0, 0), CentreOf(Render(view, 96), 24));

                view.Icon = Probe();
                Assert.Equal(Green, CentreOf(Render(view, 96), 24));

                view.Icon = null;
                Assert.Equal(Color.FromRgb(255, 0, 0), CentreOf(Render(view, 96), 24));
            });
        }

        private static readonly Color Blue = Color.FromRgb(0, 0, 255);
        private static readonly Color Green = Color.FromRgb(0, 255, 0);
        private static readonly Color Transparent = Color.FromArgb(0, 0, 0, 0);

        // A large drawing that is one green square over the whole 32 grid, a small one a blue square over the 16 grid.
        private static RibbonIcon Probe()
        {
            return new RibbonIcon
            {
                Large = Square(Colors.Lime, Grid, null),
                Small = Square(Colors.Blue, Grid, new ScaleTransform(0.5, 0.5)),
            };
        }

        private static DrawingGroup Square(Color color, double side, Transform transform)
        {
            var group = new DrawingGroup { Transform = transform };
            group.Children.Add(
                new GeometryDrawing(new SolidColorBrush(color), null, new RectangleGeometry(new Rect(0, 0, side, side)))
            );
            return group;
        }

        private static void Check(string what, Drawing drawing, double side, List<string> problems)
        {
            if (!(drawing is DrawingGroup group) || group.Children.Count == 0)
            {
                problems.Add(what + " has no parts");
                return;
            }

            Rect bounds = group.Bounds;

            if (
                bounds.IsEmpty
                || bounds.Left < -Slack
                || bounds.Top < -Slack
                || bounds.Right > side + Slack
                || bounds.Bottom > side + Slack
            )
            {
                problems.Add(what + " " + bounds);
            }
        }

        private static BitmapSource Render(double size, double dpi, RibbonIcon icon) =>
            Render(new IconView { Size = size, Icon = icon }, dpi);

        private static BitmapSource Render(IconView view, double dpi)
        {
            double scale = dpi / 96.0;
            VisualTreeHelper.SetRootDpi(view, new DpiScale(scale, scale));
            view.Measure(new Size(view.Size, view.Size));
            view.Arrange(new Rect(0, 0, view.Size, view.Size));
            int pixels = (int)Math.Round(view.Size * scale);
            var picture = new RenderTargetBitmap(pixels, pixels, dpi, dpi, PixelFormats.Pbgra32);
            picture.Render(view);
            return picture;
        }

        private static Color Pixel(BitmapSource picture, int x, int y)
        {
            var bytes = new byte[4];
            picture.CopyPixels(new Int32Rect(x, y, 1, 1), bytes, 4, 0);
            return Color.FromArgb(bytes[3], bytes[2], bytes[1], bytes[0]);
        }

        private static Color CentreOf(BitmapSource picture, int pixels) => Pixel(picture, pixels / 2, pixels / 2);

        private static ResourceDictionary Icons()
        {
            // Registers the pack scheme, which the dictionaries are loaded through, with no Application running.
            _ = PackUriHelper.UriSchemePack;
            return (ResourceDictionary)
                Application.LoadComponent(
                    new Uri("/OpenCAD2D.Wpf;component/Themes/RibbonIcons.xaml", UriKind.Relative)
                );
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
