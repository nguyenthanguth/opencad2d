using System;
using System.IO;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using Xunit;
using static OpenCAD2D.Wpf.UnitTest.PythonWindowKit;

namespace OpenCAD2D.Wpf.UnitTest
{
    /// <summary>
    /// The sheet set dialogs of A12 4b drawn with no window shown (the way <c>--dialog sheetset</c> and the rest draw them): each
    /// is made over a sample sheet set, laid out at its own width, drawn to a bitmap and looked at for what must show (the title,
    /// the footer buttons, the red or yellow line where the state has one). Where the environment variable
    /// <c>OPENCAD2D_SHEETSET_SHOTS</c> names a folder each picture is also written there as a PNG, which is how the boards are
    /// compared with the dialogs.
    /// </summary>
    public sealed class SheetSetDialogPictureTests
    {
        private static void Layout(FrameworkElement root, double width)
        {
            root.Measure(new Size(width, double.PositiveInfinity));
            root.Arrange(new Rect(0, 0, width, root.DesiredSize.Height));
            root.UpdateLayout();
            root.Measure(new Size(width, double.PositiveInfinity));
            root.Arrange(new Rect(0, 0, width, root.DesiredSize.Height));
            root.UpdateLayout();
        }

        private static BitmapSource Draw(FrameworkElement root)
        {
            int width = (int)Math.Ceiling(root.ActualWidth);
            int height = (int)Math.Ceiling(root.ActualHeight);
            var bitmap = new RenderTargetBitmap(width, height, 96, 96, PixelFormats.Pbgra32);
            bitmap.Render(root);
            return bitmap;
        }

        private static string Texts(DependencyObject root) =>
            string.Join("|", Descendants(root).OfType<TextBlock>().Where(t => IsShown(t)).Select(t => t.Text));

        private static System.Collections.Generic.IEnumerable<DependencyObject> Descendants(DependencyObject root)
        {
            int count = System.Windows.Media.VisualTreeHelper.GetChildrenCount(root);
            for (int i = 0; i < count; i++)
            {
                DependencyObject child = System.Windows.Media.VisualTreeHelper.GetChild(root, i);
                yield return child;
                foreach (DependencyObject below in Descendants(child))
                {
                    yield return below;
                }
            }
        }

        private static bool IsShown(UIElement element)
        {
            for (DependencyObject at = element; at != null; at = System.Windows.Media.VisualTreeHelper.GetParent(at))
            {
                if (at is UIElement ui && ui.Visibility != Visibility.Visible)
                {
                    return false;
                }
            }

            return true;
        }

        [Fact]
        public void EveryDialog_IsLaidOutAtItsWidth_DrawnWithItsTitleAndFooter_AndWrittenAsAPictureWhereAskedFor()
        {
            string shots = Environment.GetEnvironmentVariable("OPENCAD2D_SHEETSET_SHOTS");
            string temp = Path.Combine(Path.GetTempPath(), "gk", "sheetsetshots-" + Guid.NewGuid().ToString("N"));
            try
            {
                OnWindowThread(() =>
                {
                    if (!string.IsNullOrEmpty(shots))
                    {
                        Directory.CreateDirectory(shots);
                    }

                    foreach (string name in CadAppSheetSetDialogs.Names)
                    {
                        Assert.True(
                            CadAppSheetSetDialogs.TryCreate(name, temp, out Window window, out FrameworkElement root),
                            name
                        );
                        Layout(root, window.Width);
                        string text = Texts(root);
                        Assert.True(root.ActualWidth > 300, name + ": width " + root.ActualWidth);
                        Assert.True(
                            root.ActualHeight > 150 && root.ActualHeight < 900,
                            name + ": height " + root.ActualHeight
                        );
                        Assert.Contains(((SheetSetDialogWindow)window).Model.Title, text);
                        Assert.Contains(
                            "Help",
                            text.Split('|')
                                .Concat(
                                    Descendants(root).OfType<Button>().Select(b => b.Content as string ?? string.Empty)
                                )
                        );

                        BitmapSource picture = Draw(root);
                        Assert.Equal((int)Math.Ceiling(root.ActualWidth), picture.PixelWidth);

                        if (!string.IsNullOrEmpty(shots))
                        {
                            var encoder = new PngBitmapEncoder();
                            encoder.Frames.Add(BitmapFrame.Create(picture));
                            using (FileStream file = File.Create(Path.Combine(shots, name + ".png")))
                            {
                                encoder.Save(file);
                            }
                        }
                    }
                });
            }
            finally
            {
                try
                {
                    Directory.Delete(temp, true);
                }
                catch (IOException)
                {
                    // A file a watcher still holds is left for the system to clear.
                }
            }
        }

        [Fact]
        public void ALockedSet_DrawsTheLockLineAndOneCloseInPlaceOfOkAndCancel()
        {
            string temp = Path.Combine(Path.GetTempPath(), "gk", "sheetsetlock-" + Guid.NewGuid().ToString("N"));
            try
            {
                OnWindowThread(() =>
                {
                    Assert.True(
                        CadAppSheetSetDialogs.TryCreate(
                            "sheetset-locked",
                            temp,
                            out Window window,
                            out FrameworkElement root
                        )
                    );
                    Layout(root, window.Width);

                    string[] buttons = Descendants(root)
                        .OfType<Button>()
                        .Where(b => IsShown(b))
                        .Select(b => b.Content as string)
                        .Where(c => c != null)
                        .ToArray();

                    Assert.Contains("Close", buttons);
                    Assert.DoesNotContain("OK", buttons);
                    Assert.DoesNotContain("Cancel", buttons);
                    Assert.Contains("Nothing here can be changed.", Texts(root));
                });
            }
            finally
            {
                try
                {
                    Directory.Delete(temp, true);
                }
                catch (IOException)
                {
                    // A file a watcher still holds is left for the system to clear.
                }
            }
        }

        [Fact]
        public void ARefusedValue_DrawsTheRedLine_AndAnUntouchedDialogDoesNot()
        {
            string temp = Path.Combine(Path.GetTempPath(), "gk", "sheetsetred-" + Guid.NewGuid().ToString("N"));
            try
            {
                OnWindowThread(() =>
                {
                    Assert.True(
                        CadAppSheetSetDialogs.TryCreate(
                            "subset-refused",
                            temp,
                            out Window refused,
                            out FrameworkElement redRoot
                        )
                    );
                    Layout(redRoot, refused.Width);
                    Assert.Contains("Architectural already holds a subset named Details.", Texts(redRoot));

                    Assert.True(
                        CadAppSheetSetDialogs.TryCreate("subset", temp, out Window fine, out FrameworkElement plainRoot)
                    );
                    Layout(plainRoot, fine.Width);
                    Assert.DoesNotContain("already holds", Texts(plainRoot));
                });
            }
            finally
            {
                try
                {
                    Directory.Delete(temp, true);
                }
                catch (IOException)
                {
                    // A file a watcher still holds is left for the system to clear.
                }
            }
        }
    }
}
