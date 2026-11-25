using System;
using System.Collections.Generic;
using System.IO;
using System.IO.Packaging;
using System.Linq;
using System.Threading;
using System.Windows;
using System.Windows.Automation;
using System.Windows.Automation.Peers;
using System.Windows.Automation.Provider;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using OpenCAD2D.Plugins;
using Xunit;

namespace OpenCAD2D.Wpf.UnitTest
{
    /// <summary>
    /// What the tests of the panels of a plugin's ribbon tab share: the workspace over a model drawn offscreen with the tab
    /// chosen, laid out so that its strip is a width wide, on a thread of its own, and the plugin of three panels they use.
    /// </summary>
    internal static class PluginRibbonHarness
    {
        /// <summary>The names of the three panels of <see cref="AddThreePanels"/>, left to right.</summary>
        public static readonly string[] Titles = { "One", "Two", "Three" };

        /// <summary>
        /// Adds a tab "Hook" of three panels, One, Two and Three: each a large button "Number circles" and three standard ones
        /// "Count the circles", "Clear every mark" and "Renumber everything". The first item of Two has a picture for its icon.
        /// </summary>
        public static void AddThreePanels(CadPluginHost host, Action<string> ran = null, byte[] picture = null)
        {
            var tab = new CadRibbonTab("Hook", "Hook");

            foreach (string title in Titles)
            {
                var panel = new CadRibbonPanel(title);
                var large = new CadRibbonButton("Number circles", c => ran?.Invoke(title + ":Number circles"))
                {
                    Size = CadRibbonItemSize.Large,
                };

                if (title == "Two" && picture != null)
                {
                    large.Icon = CadPluginIcon.FromImage(picture);
                }

                panel.Items.Add(large);

                foreach (string text in new[] { "Count the circles", "Clear every mark", "Renumber everything" })
                {
                    panel.Items.Add(new CadRibbonButton(text, c => ran?.Invoke(title + ":" + text)));
                }

                tab.Panels.Add(panel);
            }

            host.AddRibbonTab(tab);
        }

        /// <summary>Makes a small PNG, a picture for an icon.</summary>
        public static byte[] Png(int side)
        {
            var pixels = new byte[side * side * 4];
            BitmapSource source = BitmapSource.Create(side, side, 96, 96, PixelFormats.Bgra32, null, pixels, side * 4);
            var encoder = new PngBitmapEncoder();
            encoder.Frames.Add(BitmapFrame.Create(source));

            using (var stream = new MemoryStream())
            {
                encoder.Save(stream);
                return stream.ToArray();
            }
        }

        /// <summary>
        /// Draws the workspace over the model with the tab chosen, on a thread of its own: laid out wide first, so that the
        /// strip's full width is read, then so that the strip is <paramref name="stripWidth"/> wide, as the window gives it its
        /// width less what is around it; the check is given the strip and the width the panels need drawn full. Without a width
        /// the strip stays as wide as it was.
        /// </summary>
        public static void OnTab(
            MainViewModel model,
            CadWorkspaceExtension tab,
            Func<double, double?> stripWidth,
            Action<CadWorkspace, RibbonStrip, Func<double, RibbonStrip>> check
        )
        {
            Exception failure = null;
            var thread = new Thread(() =>
            {
                try
                {
                    _ = PackUriHelper.UriSchemePack;
                    var workspace = new CadWorkspace { DataContext = model };

                    try
                    {
                        model.SelectedTab = tab;
                        RibbonTabHarness.Layout(workspace, 3200);
                        RibbonStrip strip = RibbonTabHarness.StripOf(workspace, tab.TabContent);
                        double around = 3200 - strip.ActualWidth;
                        double full = strip.DesiredSize.Width;
                        double? width = stripWidth?.Invoke(full);

                        // The same strip laid out again at another width: each call lays the workspace out anew.
                        RibbonStrip At(double w)
                        {
                            RibbonTabHarness.Layout(workspace, w + around);
                            RibbonTabHarness.Pump(workspace);
                            return strip;
                        }

                        if (width != null)
                        {
                            At(width.Value);
                        }

                        check(workspace, strip, At);
                    }
                    finally
                    {
                        workspace.DrawingCanvas.Dispose();
                    }
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

        /// <summary>The visible texts of the controls under an element.</summary>
        public static List<string> VisibleTexts(DependencyObject root) =>
            RibbonTabHarness
                .Descendants(root)
                .OfType<TextBlock>()
                .Where(t => t.ActualWidth > 0)
                .Select(t => t.Text)
                .ToList();

        /// <summary>Presses a button as a click does, the command it has run.</summary>
        public static void Click(Button button) =>
            ((IInvokeProvider)new ButtonAutomationPeer(button).GetPattern(PatternInterface.Invoke)).Invoke();
    }

    /// <summary>
    /// The panels of a plugin's tab shrink as AutoCAD's add-in panels do (A2P P2): the right-most goes medium first, then each
    /// of the others, then each is collapsed from the right, and they widen back in the reverse order; a medium panel shows its
    /// standard items by their icons, a collapsed one the title and the icon of its first item over a drop-down that runs its
    /// buttons.
    /// </summary>
    public sealed class PluginPanelShrinkTests : IDisposable
    {
        private readonly PluginWindowKit _kit = new PluginWindowKit();

        public void Dispose() => _kit.Dispose();

        private MainViewModel Model(Action<string> ran = null, byte[] picture = null)
        {
            MainViewModel model = _kit.Opened(out _, out _, out _);
            _kit.LoadHook(model, host => PluginRibbonHarness.AddThreePanels(host, ran, picture));
            return model;
        }

        [Fact]
        public void ARibbonStripThatShrinksRightToLeft_TakesTheRightmostPanelMediumFirst()
        {
            MainViewModel model = Model();

            // Just under the width the three need drawn full (20 pixels inside the step, never on it): the third goes medium.
            PluginRibbonHarness.OnTab(
                model,
                model.PluginTabs[0],
                full => full - 20,
                (workspace, strip, at) =>
                {
                    Assert.True(strip.ShrinksRightToLeft);
                    Assert.Equal("FFM", RibbonTabHarness.Sizes(strip));
                    Assert.Equal(1, strip.StepsTaken);
                }
            );
        }

        [Fact]
        public void AtTheLeastWidth_EveryPanelIsCollapsed_AndWidensBackInReverse()
        {
            MainViewModel model = Model();
            string[] order = { "FFF", "FFM", "FMM", "MMM", "MMC", "MCC", "CCC" };

            PluginRibbonHarness.OnTab(
                model,
                model.PluginTabs[0],
                full => full,
                (workspace, strip, at) =>
                {
                    var narrowing = new List<string>();
                    double full = strip.DesiredSize.Width;

                    for (double width = full + 10; width >= 30; width -= 5)
                    {
                        string sizes = RibbonTabHarness.Sizes(at(width));

                        if (narrowing.Count == 0 || narrowing[narrowing.Count - 1] != sizes)
                        {
                            narrowing.Add(sizes);
                        }
                    }

                    // Every state seen is one of the six steps, in the order of the steps, and the ends are all full and all collapsed.
                    Assert.Equal("FFF", narrowing[0]);
                    Assert.Equal("CCC", narrowing[narrowing.Count - 1]);
                    Assert.Equal(narrowing.OrderBy(s => Array.IndexOf(order, s)), narrowing);
                    Assert.DoesNotContain(narrowing, s => Array.IndexOf(order, s) < 0);

                    var widening = new List<string>();

                    for (double width = 30; width <= full + 10; width += 5)
                    {
                        string sizes = RibbonTabHarness.Sizes(at(width));

                        if (widening.Count == 0 || widening[widening.Count - 1] != sizes)
                        {
                            widening.Add(sizes);
                        }
                    }

                    narrowing.Reverse();
                    Assert.Equal(narrowing, widening);
                }
            );
        }

        [Fact]
        public void AMediumPluginPanel_ShowsItsStandardItemsByTheirIconsAlone_AndKeepsTheLargeOnesText()
        {
            MainViewModel model = Model();

            PluginRibbonHarness.OnTab(
                model,
                model.PluginTabs[0],
                full => full - 20,
                (workspace, strip, at) =>
                {
                    RibbonPanel medium = RibbonTabHarness.Panel(strip, "Three");
                    RibbonPanel full = RibbonTabHarness.Panel(strip, "One");
                    Assert.Equal(RibbonPanelSize.Medium, medium.Size);
                    Assert.Equal(RibbonPanelSize.Full, full.Size);

                    List<string> mediumTexts = PluginRibbonHarness.VisibleTexts(medium);
                    Assert.Contains("Number circles", mediumTexts);
                    Assert.DoesNotContain("Count the circles", mediumTexts);
                    Assert.DoesNotContain("Clear every mark", mediumTexts);

                    // The standard buttons still carry their names for the tooltip and for screen readers.
                    Button count = RibbonTabHarness
                        .Descendants(medium)
                        .OfType<Button>()
                        .Single(b => AutomationProperties.GetName(b) == "Count the circles");
                    Assert.Equal(24, count.ActualWidth, 0);

                    List<string> fullTexts = PluginRibbonHarness.VisibleTexts(full);
                    Assert.Contains("Count the circles", fullTexts);
                    Assert.Contains("Number circles", fullTexts);
                }
            );
        }

        [Fact]
        public void ACollapsedPluginPanel_ShowsTheIconOfItsFirstItem_AGeometryOrAPicture()
        {
            byte[] picture = PluginRibbonHarness.Png(32);
            MainViewModel model = Model(picture: picture);
            var panels = ((PluginTabViewModel)model.PluginTabs[0].TabContent).Panels;

            // Panel One's first item has no icon of its own: the plugin's path; Two's is a picture.
            Assert.NotNull(panels[0].IconGeometry);
            Assert.Null(panels[0].IconImage);
            Assert.Null(panels[1].IconGeometry);
            Assert.NotNull(panels[1].IconImage);

            PluginRibbonHarness.OnTab(
                model,
                model.PluginTabs[0],
                full => 30,
                (workspace, strip, at) =>
                {
                    Assert.Equal("CCC", RibbonTabHarness.Sizes(strip));
                    RibbonPanel one = RibbonTabHarness.Panel(strip, "One");
                    RibbonPanel two = RibbonTabHarness.Panel(strip, "Two");

                    ToggleButton button = RibbonTabHarness
                        .Descendants(one)
                        .OfType<ToggleButton>()
                        .First(b => b.ActualWidth > 0);
                    Assert.Same(panels[0].IconGeometry, button.Tag);
                    Assert.Contains("One", PluginRibbonHarness.VisibleTexts(one));
                    Assert.Same(panels[0].IconGeometry, one.Icon);
                    Assert.Null(one.IconImage);

                    Assert.Same(panels[1].IconImage, two.IconImage);
                    Image drawn = RibbonTabHarness
                        .Descendants(two)
                        .OfType<Image>()
                        .First(i => ReferenceEquals(i.Source, panels[1].IconImage));
                    Assert.True(drawn.ActualWidth > 0);
                }
            );
        }

        [Fact]
        public void ACollapsedPluginPanelsDropDown_RunsItsButtons_AndClosesAfterAClick()
        {
            var ran = new List<string>();
            MainViewModel model = Model(ran.Add);

            PluginRibbonHarness.OnTab(
                model,
                model.PluginTabs[0],
                full => 30,
                (workspace, strip, at) =>
                {
                    RibbonPanel two = RibbonTabHarness.Panel(strip, "Two");
                    Assert.Equal(RibbonPanelSize.Collapsed, two.Size);
                    var popup = (Popup)two.Template.FindName("PART_Popup", two);

                    two.IsDropDownOpen = true;
                    RibbonTabHarness.Pump(popup);
                    popup.Child.Measure(new Size(2000, 960));
                    popup.Child.Arrange(new Rect(0, 0, popup.Child.DesiredSize.Width, popup.Child.DesiredSize.Height));

                    Button count = RibbonTabHarness
                        .Descendants(popup.Child)
                        .OfType<Button>()
                        .Single(b => AutomationProperties.GetName(b) == "Count the circles");
                    Assert.True(count.IsEnabled);

                    PluginRibbonHarness.Click(count);
                    RibbonTabHarness.Pump(two);

                    Assert.Equal(new[] { "Two:Count the circles" }, ran);
                    Assert.False(two.IsDropDownOpen);
                }
            );
        }

        [Fact]
        public void TheBuiltInTabs_ShrinkAsBefore()
        {
            // Home sets its steps and not ShrinksRightToLeft: the plugin's way of shrinking is not its own.
            using (var folder = new TestFolder())
            {
                string file = folder.Write("room.dxf", TestFiles.Room(out _, out _));
                Exception failure = null;
                var thread = new Thread(() =>
                {
                    try
                    {
                        _ = PackUriHelper.UriSchemePack;
                        var model = new MainViewModel();
                        Assert.True(model.Open(file));
                        var workspace = new CadWorkspace { DataContext = model };

                        try
                        {
                            RibbonTabHarness.Layout(workspace, 1600);
                            RibbonStrip strip = RibbonTabHarness.StripOf(workspace, model);

                            Assert.False(strip.ShrinksRightToLeft);
                            Assert.Equal(3, strip.StepsTaken);
                            Assert.Equal(RibbonPanelSize.Medium, RibbonTabHarness.Panel(strip, "Modify").Size);
                        }
                        finally
                        {
                            workspace.DrawingCanvas.Dispose();
                        }
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
        }
    }
}
