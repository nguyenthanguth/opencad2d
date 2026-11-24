using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using Xunit;

namespace OpenCAD2D.Wpf.UnitTest
{
    /// <summary>
    /// The question before a tool's packages are downloaded or installed (TF6), as the PackageQuestion board draws it: its
    /// texts come from the caller, so that the NuGet question and, later, the pip question are one window. Laid out offscreen,
    /// never shown.
    /// </summary>
    public sealed class PackageQuestionWindowTests
    {
        private static PackageQuestionViewModel NuGet() =>
            new PackageQuestionViewModel(
                "Packages for Stairs",
                "The plugin Stairs (D:\\Tools\\Stairs\\Stairs.csproj) uses 2 packages that are not on this computer yet.",
                "State",
                new[]
                {
                    new PackageQuestionRow("Newtonsoft.Json", "13.0.3", true, "download", true),
                    new PackageQuestionRow("NetTopologySuite", "2.5.0", true, "download", true),
                    new PackageQuestionRow("System.Memory", "4.5.5", false, "in the cache", false),
                },
                "Loading a plugin runs its code, and its packages\u2019 code, with your rights.",
                "From nuget.org",
                "Don\u2019t load",
                "Download and load"
            );

        private static PackageQuestionViewModel Pip() =>
            new PackageQuestionViewModel(
                "Packages for Python tool Survey",
                "The tool Survey (D:\\Tools\\Survey\\requirements.txt) needs these packages in its own environment.",
                "Asked as",
                new[] { new PackageQuestionRow("shapely", "2.0.0", true, "shapely>=2.0", false) },
                "Installing runs the packages\u2019 code with your rights.",
                "With pip, from the Python package index",
                "Don\u2019t install",
                "Install"
            );

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

        /// <summary>Lays the window out at the board's size and gives what it draws.</summary>
        private static List<DependencyObject> Layout(PackageQuestionWindow window)
        {
            FrameworkElement root = window.Body;
            window.Content = null;
            root.DataContext = window.Model;
            root.Measure(new Size(560, 440));
            root.Arrange(new Rect(0, 0, 560, 440));
            root.UpdateLayout();
            return Descendants(root).ToList();
        }

        [Fact]
        public void TheNuGetQuestion_HasTheTextsOfTheBoard_InThePlacesOfTheBoard()
        {
            string[] texts = null;
            Size size = default;

            OnSta(() =>
            {
                var window = new PackageQuestionWindow(NuGet());
                List<DependencyObject> all = Layout(window);
                size = new Size(window.Width, window.Height);
                texts = all.OfType<TextBlock>()
                    .Select(t => t.Text)
                    .Concat(all.OfType<Button>().Select(b => b.Content as string))
                    .ToArray();
                window.Close();
            });

            Assert.Equal(new Size(560, 440), size);

            foreach (
                string text in new[]
                {
                    "Packages for Stairs",
                    "The plugin Stairs (D:\\Tools\\Stairs\\Stairs.csproj) uses 2 packages that are not on this computer yet.",
                    "Package",
                    "Version",
                    "State",
                    "Newtonsoft.Json",
                    "13.0.3",
                    "download",
                    "System.Memory",
                    "in the cache",
                    "Loading a plugin runs its code, and its packages\u2019 code, with your rights.",
                    "From nuget.org",
                    "Don\u2019t load",
                    "Download and load",
                }
            )
            {
                Assert.Contains(text, texts);
            }
        }

        [Fact]
        public void TheTable_ShowsThePackagesTheToolNamesInBold_TheOthersPlain_AndDownloadInBlue()
        {
            var names = new Dictionary<string, FontWeight>();
            var notes = new Dictionary<string, Color>();
            Color yes = default;

            OnSta(() =>
            {
                var window = new PackageQuestionWindow(NuGet());
                List<DependencyObject> all = Layout(window);

                foreach (TextBlock name in all.OfType<TextBlock>().Where(t => t.Name == "Name"))
                {
                    names[name.Text] = name.FontWeight;
                }

                List<TextBlock> columns = all.OfType<TextBlock>().Where(t => t.Name == "Note").ToList();
                notes["download"] = ((SolidColorBrush)columns.First(t => t.Text == "download").Foreground).Color;
                notes["in the cache"] = (
                    (SolidColorBrush)columns.First(t => t.Text == "in the cache").Foreground
                ).Color;
                yes = ((SolidColorBrush)all.OfType<Button>().Single(b => b.Name == "YesButton").Background).Color;
                window.Close();
            });

            Assert.Equal(FontWeights.SemiBold, names["Newtonsoft.Json"]);
            Assert.Equal(FontWeights.SemiBold, names["NetTopologySuite"]);
            Assert.NotEqual(FontWeights.SemiBold, names["System.Memory"]);
            Assert.Equal(Color.FromRgb(0x6C, 0xB6, 0xFF), notes["download"]);
            Assert.Equal(Color.FromRgb(0x9A, 0xA4, 0xB1), notes["in the cache"]);
            Assert.Equal(Color.FromRgb(0x3D, 0x9B, 0xFF), yes);
        }

        [Fact]
        public void ThePipQuestion_IsTheSameWindowWithItsOwnTitleLeadThirdColumnFootAndButtons()
        {
            string[] texts = null;

            OnSta(() =>
            {
                var window = new PackageQuestionWindow(Pip());
                List<DependencyObject> all = Layout(window);
                texts = all.OfType<TextBlock>()
                    .Select(t => t.Text)
                    .Concat(all.OfType<Button>().Select(b => b.Content as string))
                    .ToArray();
                window.Close();
            });

            Assert.Contains("Packages for Python tool Survey", texts);
            Assert.Contains("Asked as", texts);
            Assert.Contains("shapely>=2.0", texts);
            Assert.Contains("With pip, from the Python package index", texts);
            Assert.Contains("Don\u2019t install", texts);
            Assert.Contains("Install", texts);
            Assert.DoesNotContain("State", texts);
        }

        [Fact]
        public void YesIsTheAnswerOfTheYesButton_AndTheCloseButtonAndNoAreNo()
        {
            bool yes = false;
            bool no = true;
            bool closed = true;

            OnSta(() =>
            {
                var window = new PackageQuestionWindow(NuGet());
                Assert.False(window.Answer);
                List<DependencyObject> all = Layout(window);
                Button yesButton = all.OfType<Button>().Single(b => b.Name == "YesButton");
                Button noButton = all.OfType<Button>().Single(b => b.Name == "NoButton");
                Button close = all.OfType<Button>().Single(b => "Close".Equals(b.ToolTip));

                // The window is not shown, so setting its DialogResult is refused after the answer is kept.
                Assert.Throws<InvalidOperationException>(() =>
                    yesButton.RaiseEvent(new RoutedEventArgs(Button.ClickEvent))
                );
                yes = window.Answer;
                Assert.Throws<InvalidOperationException>(() =>
                    noButton.RaiseEvent(new RoutedEventArgs(Button.ClickEvent))
                );
                no = window.Answer;
                Assert.Throws<InvalidOperationException>(() =>
                    yesButton.RaiseEvent(new RoutedEventArgs(Button.ClickEvent))
                );
                Assert.Throws<InvalidOperationException>(() =>
                    close.RaiseEvent(new RoutedEventArgs(Button.ClickEvent))
                );
                closed = window.Answer;
                window.Close();
            });

            Assert.True(yes);
            Assert.False(no);
            Assert.False(closed);
        }

        [Fact]
        public void ANoButton_IsTheCancelButton_SoEscIsNo()
        {
            bool cancel = false;

            OnSta(() =>
            {
                var window = new PackageQuestionWindow(NuGet());
                cancel = Layout(window).OfType<Button>().Single(b => b.Name == "NoButton").IsCancel;
                window.Close();
            });

            Assert.True(cancel);
        }
    }
}
