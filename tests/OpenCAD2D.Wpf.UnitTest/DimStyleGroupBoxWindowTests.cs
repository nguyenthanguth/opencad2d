using System;
using System.Collections.Generic;
using System.IO;
using System.IO.Packaging;
using System.Linq;
using System.Threading;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using Xunit;

namespace OpenCAD2D.Wpf.UnitTest
{
    /// <summary>
    /// The Modify Dimension Style window as AutoCAD lays its tabs out, with no window shown: each tab's group boxes under their
    /// titles in the left column, the right column under the preview, or across the width; the two Suppress pairs on one row each;
    /// the preview about 300 wide at the top right; nothing over another and nothing cut off at the 860 by 660 of the design.
    /// </summary>
    public sealed class DimStyleGroupBoxWindowTests
    {
        private const double Width = 860;
        private const double Height = 660;

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

        private static IEnumerable<T> Within<T>(DependencyObject parent)
            where T : DependencyObject
        {
            for (int i = 0; i < VisualTreeHelper.GetChildrenCount(parent); i++)
            {
                DependencyObject child = VisualTreeHelper.GetChild(parent, i);

                if (child is T found)
                {
                    yield return found;
                }

                foreach (T deeper in Within<T>(child))
                {
                    yield return deeper;
                }
            }
        }

        /// <summary>The window of a metric drawing's current dimension style, on a tab, laid out at the size of the design and not shown.</summary>
        private static FrameworkElement Shown(string tab, out DimStyleEditViewModel editor)
        {
            _ = PackUriHelper.UriSchemePack;
            CadDocument document = CadDocument.From(CadTemplate.CreateMetric(), "groups.dxf");
            editor = new DimStyleEditViewModel(
                document,
                DimStyleEditMode.Modify,
                CadDimStyleEditor.Current(document.Database)
            )
            {
                Tab = tab,
            };
            var window = new DimStyleEditWindow(editor);
            FrameworkElement body = window.Body;
            object context = window.DataContext;
            window.Content = null;
            body.DataContext = context;
            body.Measure(new Size(Width, Height));
            body.Arrange(new Rect(0, 0, Width, Height));
            body.UpdateLayout();
            return body;
        }

        private static ItemsControl Host(FrameworkElement body, string name) =>
            Within<ItemsControl>(body).Single(i => i.Name == name);

        private static string[] Titles(FrameworkElement host) =>
            Within<GroupBox>(host).Select(g => (string)g.Header).ToArray();

        private static Rect Bounds(FrameworkElement element, FrameworkElement body) =>
            element
                .TransformToAncestor(body)
                .TransformBounds(new Rect(0, 0, element.ActualWidth, element.ActualHeight));

        public static IEnumerable<object[]> Tabs() =>
            new[]
            {
                new object[] { "Lines", new[] { "Dimension lines" }, new string[0], new[] { "Extension lines" } },
                new object[]
                {
                    "Symbols and arrows",
                    new[] { "Arrowheads", "Centre marks" },
                    new[] { "Arc length symbol", "Radius jog dimension" },
                    new string[0],
                },
                new object[]
                {
                    "Text",
                    new[] { "Text appearance", "Text placement" },
                    new[] { "Text alignment" },
                    new string[0],
                },
                new object[]
                {
                    "Fit",
                    new[] { "Fit options", "Text placement" },
                    new[] { "Scale for dimension features", "Fine tuning" },
                    new string[0],
                },
                new object[]
                {
                    "Primary units",
                    new[] { "Linear dimensions", "Measurement scale", "Zero suppression" },
                    new[] { "Angular dimensions" },
                    new string[0],
                },
                new object[]
                {
                    "Alternate units",
                    new[] { "Display", "Alternate units", "Zero suppression" },
                    new[] { "Placement" },
                    new string[0],
                },
                new object[]
                {
                    "Tolerances",
                    new[] { "Tolerance format", "Tolerance zero suppression" },
                    new string[0],
                    new string[0],
                },
            };

        [Theory]
        [MemberData(nameof(Tabs))]
        public void EachTab_HasItsGroupBoxesUnderTheirTitles_InTheColumnAutoCadPutsThemIn(
            string tab,
            string[] left,
            string[] right,
            string[] wide
        )
        {
            OnSta(() =>
            {
                FrameworkElement body = Shown(tab, out _);

                Assert.Equal(left, Titles(Host(body, "LeftGroups")));
                Assert.Equal(right, Titles(Host(body, "RightGroups")));
                Assert.Equal(wide, Titles(Host(body, "WideGroups")));
            });
        }

        [Fact]
        public void TheSuppressPairs_SitOnOneRowEach_TheSecondBoxRightOfTheFirst()
        {
            OnSta(() =>
            {
                FrameworkElement body = Shown("Lines", out _);

                foreach (
                    string[] pair in new[]
                    {
                        new[] { "Dim line 1", "Dim line 2" },
                        new[] { "Ext line 1", "Ext line 2" },
                    }
                )
                {
                    CheckBox first = Within<CheckBox>(body).Single(c => Equals(c.Content, pair[0]));
                    CheckBox second = Within<CheckBox>(body).Single(c => Equals(c.Content, pair[1]));
                    Rect one = Bounds(first, body);
                    Rect two = Bounds(second, body);

                    Assert.True(Math.Abs(one.Top - two.Top) < 1, pair[0] + " is on the row of " + pair[1]);
                    Assert.True(two.Left > one.Right - 1, pair[1] + " is right of " + pair[0]);
                }

                // The caption of each row says what the two boxes suppress, the variables of both beside it.
                string[] hints = Within<TextBlock>(body).Select(t => t.Text).ToArray();
                Assert.Contains("DIMSD1 · DIMSD2", hints);
                Assert.Contains("DIMSE1 · DIMSE2", hints);
                Assert.DoesNotContain(Within<TextBlock>(body), t => t.Text == "Suppress dim line 1");
            });
        }

        [Fact]
        public void ABoxOfAPairRow_WritesItsVariable_AsAnyOtherBoxDoes()
        {
            OnSta(() =>
            {
                FrameworkElement body = Shown("Lines", out DimStyleEditViewModel editor);
                Assert.Equal(false, editor.Values["Dimsd2"]);

                Within<CheckBox>(body).Single(c => Equals(c.Content, "Dim line 2")).IsChecked = true;

                Assert.Equal(true, editor.Values["Dimsd2"]);
                Assert.Equal(false, editor.Values["Dimsd1"]);
            });
        }

        [Fact]
        public void TheExtensionLinesBox_HoldsItsFieldsInTwoColumns_AcrossTheWidth()
        {
            OnSta(() =>
            {
                FrameworkElement body = Shown("Lines", out _);
                GroupBox box = Within<GroupBox>(Host(body, "WideGroups")).Single();
                ComboBox colour = Within<ComboBox>(box).First();
                TextBox extend = Within<TextBox>(box).First(t => t.Visibility == Visibility.Visible);
                Rect frame = Bounds(box, body);

                // Colour sits in the left column, "Extend beyond dim lines" in the right one, level with the first row.
                Assert.True(Bounds(extend, body).Left > frame.Left + frame.Width / 2);
                Assert.True(Bounds(colour, body).Right < frame.Left + frame.Width / 2 + 40);
                Assert.True(Math.Abs(Bounds(extend, body).Top - Bounds(colour, body).Top) < 6);
                Assert.True(frame.Width > Width - 60);
            });
        }

        [Theory]
        [MemberData(nameof(Tabs))]
        public void EveryTab_FitsTheWindowOfTheDesign_PreviewAtTheTopRight_NoBoxOverAnother(
            string tab,
            string[] left,
            string[] right,
            string[] wide
        )
        {
            OnSta(() =>
            {
                FrameworkElement body = Shown(tab, out _);
                SaveShot(body, tab);
                StylePreviewPanel preview = Within<StylePreviewPanel>(body).Single();
                Rect view = Bounds(preview, body);

                // The preview is about 300 wide, flush right of the content, above the right column's boxes.
                Assert.InRange(view.Width, 296, 304);
                Assert.InRange(view.Right, Width - 40, Width);
                Assert.True(view.Top < 100, "preview top " + view.Top);

                var boxes = Within<GroupBox>(body).Select(g => (Box: g, Rect: Bounds(g, body))).ToList();
                Assert.Equal(left.Length + right.Length + wide.Length, boxes.Count);

                foreach (var one in boxes)
                {
                    Assert.True(
                        one.Rect.Left >= 0 && one.Rect.Right <= Width,
                        "box " + one.Box.Header + " is inside the width"
                    );
                    Assert.False(one.Rect.IntersectsWith(Inflate(view, -1)), one.Box.Header + " is over the preview");

                    foreach (var other in boxes.Where(b => !ReferenceEquals(b.Box, one.Box)))
                    {
                        Assert.False(
                            Inflate(one.Rect, -1).IntersectsWith(Inflate(other.Rect, -1)),
                            one.Box.Header + " is over " + other.Box.Header
                        );
                    }
                }

                // The right column's boxes are under the preview and as wide as it; the wide ones are under everything.
                foreach (var one in boxes.Where(b => right.Contains((string)b.Box.Header)))
                {
                    Assert.True(one.Rect.Top >= view.Bottom - 1);
                    Assert.InRange(one.Rect.Width, view.Width - 2, view.Width + 2);
                }

                double above = boxes
                    .Where(b => !wide.Contains((string)b.Box.Header))
                    .Select(b => b.Rect.Bottom)
                    .Concat(new[] { view.Bottom })
                    .Max();

                foreach (var one in boxes.Where(b => wide.Contains((string)b.Box.Header)))
                {
                    Assert.True(one.Rect.Top >= above - 1);
                }

                // The description spans the width of both columns and nothing is cut off: the tab needs no scroll bar.
                Border description = Within<Border>(body)
                    .First(b => Within<TextBlock>(b).Any(t => t.Text == "Description"));
                Assert.True(Bounds(description, body).Width > Width - 40);
                ScrollViewer scroll = Within<ScrollViewer>(body).First(s => s.Name == "FieldsScroll");
                Assert.True(scroll.ScrollableHeight < 1, tab + " needs " + scroll.ScrollableHeight + " more height");
                Assert.True(Bounds(description, body).Bottom <= Height, tab + " description bottom");
            });
        }

        private static Rect Inflate(Rect rect, double by)
        {
            rect.Inflate(by, by);
            return rect;
        }

        /// <summary>Draws the window to a PNG in the folder named by GH_DIMSTYLE_PNG_DIR, when it is set, so that a person can look at each tab.</summary>
        private static void SaveShot(FrameworkElement body, string tab)
        {
            string folder = Environment.GetEnvironmentVariable("GH_DIMSTYLE_PNG_DIR");

            if (string.IsNullOrEmpty(folder))
            {
                return;
            }

            Directory.CreateDirectory(folder);
            var target = new RenderTargetBitmap((int)Width, (int)Height, 96, 96, PixelFormats.Pbgra32);
            target.Render(body);
            var encoder = new PngBitmapEncoder();
            encoder.Frames.Add(BitmapFrame.Create(target));

            using (
                FileStream file = File.Create(Path.Combine(folder, tab.Replace(' ', '-').ToLowerInvariant() + ".png"))
            )
            {
                encoder.Save(file);
            }
        }

        [Fact]
        public void TheFitTabsLongLists_SitUnderTheirLabels_TheWidthOfTheirGroupBox()
        {
            OnSta(() =>
            {
                FrameworkElement body = Shown("Fit", out _);

                foreach (GroupBox box in Within<GroupBox>(Host(body, "LeftGroups")))
                {
                    ComboBox list = Within<ComboBox>(box).Single();
                    TextBlock label = Within<TextBlock>(box).First(t => t.Text.StartsWith("When "));

                    Assert.True(Bounds(list, body).Top >= Bounds(label, body).Bottom - 1, "list under its label");
                    Assert.True(Bounds(list, body).Width > 440, "list width " + Bounds(list, body).Width);
                }
            });
        }

        /// <summary>The tab stops of a tab in the order Tab visits them: the controls drawn, shown and enabled, in tree order.</summary>
        private static IEnumerable<Control> TabStops(DependencyObject parent)
        {
            for (int i = 0; i < VisualTreeHelper.GetChildrenCount(parent); i++)
            {
                DependencyObject child = VisualTreeHelper.GetChild(parent, i);

                if (child is UIElement shown && shown.Visibility != Visibility.Visible)
                {
                    continue;
                }

                if (
                    child is Control control
                    && control.IsTabStop
                    && control.Focusable
                    && control.IsEnabled
                    && (control is TextBox || control is ComboBox || control is CheckBox || control is Button)
                )
                {
                    yield return control;
                }

                foreach (Control deeper in TabStops(child))
                {
                    yield return deeper;
                }
            }
        }

        [Theory]
        [InlineData("Lines", "Left,Wide")]
        [InlineData("Text", "Left,Right")]
        [InlineData("Fit", "Left,Right")]
        public void Tab_GoesLeftColumn_ThenRightColumn_ThenWide_AndNeverStopsOnThePreviewToolbar(
            string tab,
            string order
        )
        {
            OnSta(() =>
            {
                FrameworkElement body = Shown(tab, out _);
                FrameworkElement[] hosts =
                {
                    Host(body, "LeftGroups"),
                    Host(body, "RightGroups"),
                    Host(body, "WideGroups"),
                };
                string[] names = { "Left", "Right", "Wide" };
                StylePreviewPanel preview = Within<StylePreviewPanel>(body).Single();
                var visited = new List<string>();

                foreach (Control stop in TabStops(body))
                {
                    Assert.False(IsWithin(stop, preview), "a tab stop on the preview: " + stop.GetType().Name);
                    int at = Array.FindIndex(hosts, h => IsWithin(stop, h));

                    if (at >= 0 && (visited.Count == 0 || visited[visited.Count - 1] != names[at]))
                    {
                        visited.Add(names[at]);
                    }
                }

                Assert.Equal(order, string.Join(",", visited));
            });
        }

        private static bool IsWithin(DependencyObject element, DependencyObject ancestor)
        {
            for (DependencyObject d = element; d != null; d = VisualTreeHelper.GetParent(d))
            {
                if (ReferenceEquals(d, ancestor))
                {
                    return true;
                }
            }

            return false;
        }

        [Theory]
        [InlineData("DIMSD1", "Dim line 1")]
        [InlineData("DIMSD2", "Dim line 2")]
        [InlineData("DIMSE1", "Ext line 1")]
        [InlineData("DIMSE2", "Ext line 2")]
        public void ThePreviewAskingForASuppressField_FindsTheShownBoxOfItsPairRow_NotTheCollapsedSingleRow(
            string variable,
            string words
        )
        {
            OnSta(() =>
            {
                FrameworkElement body = Shown("Lines", out DimStyleEditViewModel editor);
                DimFieldViewModel field = editor.FieldsOf("Lines").Single(f => f.Variable == variable);

                FrameworkElement row = StyleFieldLinks.RowOf(body, field);

                Assert.NotNull(row);

                for (
                    DependencyObject d = row;
                    d != null && !ReferenceEquals(d, body);
                    d = VisualTreeHelper.GetParent(d)
                )
                {
                    Assert.True(
                        !(d is UIElement element) || element.Visibility == Visibility.Visible,
                        variable + ": the row is inside a collapsed " + d.GetType().Name
                    );
                }

                Assert.Contains(Within<CheckBox>(row), c => Equals(c.Content, words));
            });
        }

        [Fact]
        public void TheWindow_IsTheWidthOfTheDesign_AndKeepsItsGripAndTabs()
        {
            OnSta(() =>
            {
                _ = PackUriHelper.UriSchemePack;
                CadDocument document = CadDocument.From(CadTemplate.CreateMetric(), "size.dxf");
                var window = new DimStyleEditWindow(
                    new DimStyleEditViewModel(
                        document,
                        DimStyleEditMode.Modify,
                        CadDimStyleEditor.Current(document.Database)
                    )
                );

                Assert.Equal(Width, window.Width);
                Assert.Equal(Height, window.Height);
                Assert.Equal(Width, window.MinWidth);
                Assert.Equal(Height, window.MinHeight);
                Assert.Equal(ResizeMode.CanResizeWithGrip, window.ResizeMode);
                window.Close();
            });
        }
    }
}
