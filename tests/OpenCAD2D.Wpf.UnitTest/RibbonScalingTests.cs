using System;
using System.Collections.Generic;
using System.IO.Packaging;
using System.Linq;
using System.Reflection;
using System.Threading;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Data;
using System.Windows.Input;
using System.Windows.Media;
using Xunit;
using static OpenCAD2D.Wpf.UnitTest.RibbonTabHarness;

namespace OpenCAD2D.Wpf.UnitTest
{
    /// <summary>
    /// The ribbon laid out at 120 and 144 dots per inch (125 and 150 per cent), as the window of a scaled desktop draws it: the
    /// device independent pixels of its layout are the same, so Home takes the same steps at the same strip widths, no label is
    /// cut, and a drop-down's pin and Esc work as at 96. The scale is set on the workspace once its templates have been laid out
    /// at 96 (VisualTreeHelper.SetRootDpi reaches the visuals that exist), then it is laid out again.
    /// </summary>
    public sealed class RibbonScalingTests
    {
        private static readonly double[] Dpis = { 96.0, 120.0, 144.0 };

        private static readonly (string Name, Func<MainViewModel, CadWorkspaceExtension> Tab)[] Tabs =
        {
            ("Home", null),
            ("Insert", m => m.InsertTab),
            ("Annotate", m => m.AnnotateTab),
            ("Parametric", m => m.ParametricTab),
            ("View", m => m.ViewTab),
            ("Manage", m => m.ManageTab),
            ("Output", m => m.OutputTab),
            ("Settings", m => m.SettingsTab),
        };

        /// <summary>The window drawn offscreen at a scale with a tab shown and its strip a width wide, on a thread of its own.</summary>
        private static void OnScaled(
            double dpi,
            Func<MainViewModel, CadWorkspaceExtension> tab,
            double stripWidth,
            Action<MainViewModel, CadWorkspace, RibbonStrip, double> check
        )
        {
            using (var folder = new TestFolder())
            {
                string room = folder.Write("room.dxf", TestFiles.Room(out _, out _));
                Exception failure = null;
                var thread = new Thread(() =>
                {
                    try
                    {
                        _ = PackUriHelper.UriSchemePack;
                        var model = new MainViewModel();
                        Assert.True(model.Open(room));
                        var workspace = new CadWorkspace { DataContext = model };
                        LetGoOfTheWindow(model);
                        CadWorkspaceExtension chosen = tab?.Invoke(model);
                        model.SelectedTab = chosen;

                        try
                        {
                            Layout(workspace, 3200);
                            // Settings is a row of flat buttons, not panels: it has no strip.
                            RibbonStrip strip =
                                chosen == null
                                    ? Descendants(workspace)
                                        .OfType<RibbonStrip>()
                                        .Single(s => ReferenceEquals(s.DataContext, model))
                                    : Descendants(workspace)
                                        .OfType<RibbonStrip>()
                                        .SingleOrDefault(s => ReferenceEquals(s.DataContext, chosen.TabContent));
                            double around = strip == null ? 0 : 3200 - strip.ActualWidth;
                            double scale = dpi / 96.0;
                            VisualTreeHelper.SetRootDpi(workspace, new DpiScale(scale, scale));
                            workspace.InvalidateMeasure();
                            Layout(workspace, stripWidth + around);
                            Pump(workspace);
                            check(model, workspace, strip, scale);
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

        [Fact]
        public void AtOneHundredTwentyAndOneHundredFortyFour_HomeTakesTheSameStepsAsAtNinetySix()
        {
            foreach (double width in new[] { 1584.0, 1424.0, 1264.0, 1084.0 })
            {
                var steps = new List<string>();

                foreach (double dpi in Dpis)
                {
                    OnScaled(dpi, null, width, (model, workspace, strip, scale) =>
                    {
                        string sizes = string.Concat(
                            strip
                                .Children.OfType<RibbonPanel>()
                                .Select(p =>
                                    p.Size == RibbonPanelSize.Full ? 'F'
                                    : p.Size == RibbonPanelSize.Medium ? 'M'
                                    : 'C'
                                )
                        );
                        steps.Add(dpi + ": " + strip.StepsTaken + " steps " + sizes);
                    });
                }

                Assert.True(
                    steps.Select(s => s.Substring(s.IndexOf(':'))).Distinct().Count() == 1,
                    "Home at " + width + ": " + string.Join(" | ", steps)
                );
            }
        }

        [Fact]
        public void NoRibbonLabelIsCutAtEitherScale()
        {
            var faults = new List<string>();

            // Wide, where every panel is full and the slide-outs drop; narrow, where panels are medium or collapsed and the
            // drop-down of a collapsed one shows its full buttons.
            foreach (double width in new[] { 3000.0, 1084.0 })
            {
                foreach (double dpi in Dpis)
                {
                    foreach ((string name, Func<MainViewModel, CadWorkspaceExtension> tab) in Tabs)
                    {
                        try
                        {
                            OnScaled(dpi, tab, width, (model, workspace, strip, scale) =>
                                ScanTab(model, workspace, strip, scale, dpi + " " + name + " at " + width, faults)
                            );
                        }
                        catch (Exception e)
                        {
                            faults.Add(dpi + " " + name + ": " + e.Message + " " + e.InnerException?.Message);
                        }
                    }
                }
            }

            Assert.True(
                faults.Count == 0,
                faults.Count + " labels cut:" + Environment.NewLine + string.Join(Environment.NewLine, faults.Take(60))
            );
        }

        private static void ScanTab(
            MainViewModel model,
            CadWorkspace workspace,
            RibbonStrip strip,
            double scale,
            string where,
            List<string> faults
        )
        {
            IEnumerable<DependencyObject> parts =
                strip != null
                    ? new DependencyObject[] { strip }
                    : Descendants(workspace).OfType<ButtonBase>().Where(b => b.DataContext is SettingsRibbonViewModel);

            foreach (DependencyObject part in parts)
            {
                foreach (string cut in CutLabels(part, scale))
                {
                    faults.Add(where + ": " + cut);
                }
            }

            if (strip == null)
            {
                return;
            }

            // The slide-outs and the drop-downs of collapsed panels, each dropped under its panel and laid out at the scale.
            foreach (
                RibbonPanel panel in strip
                    .Children.OfType<RibbonPanel>()
                    .Where(p => p.SlideOut != null || p.Size == RibbonPanelSize.Collapsed)
            )
            {
                var popup = (Popup)panel.Template.FindName("PART_Popup", panel);
                panel.IsDropDownOpen = true;
                Pump(popup);
                ScanPopup((FrameworkElement)popup.Child, scale, where + " " + panel.Title + " drop-down", faults);
                panel.IsDropDownOpen = false;
            }

            // The Insert gallery with a block in it, dropped under Insert.
            if (model.SelectedTab != null && model.SelectedTab.TabContent is InsertRibbonViewModel insert)
            {
                foreach (string block in new[] { "DOOR_900", "A_VERY_LONG_BLOCK_NAME_FOR_A_TILE" })
                {
                    model.Execute("-B");
                    model.Execute(block);
                    model.Execute("0,0");
                    model.Execute(string.Empty);
                }

                insert.IsGalleryOpen = true;
                Pump(workspace);
                Popup gallery = Descendants(strip)
                    .OfType<Popup>()
                    .First(p => BindingOperations.GetBinding(p, Popup.IsOpenProperty)?.Path.Path == "IsGalleryOpen");
                ScanPopup((FrameworkElement)gallery.Child, scale, where + " gallery", faults);
                insert.IsGalleryOpen = false;
            }
        }

        private static void ScanPopup(FrameworkElement child, double scale, string where, List<string> faults)
        {
            VisualTreeHelper.SetRootDpi(child, new DpiScale(scale, scale));
            child.Measure(new Size(2000, 960));
            child.Arrange(new Rect(0, 0, child.DesiredSize.Width, child.DesiredSize.Height));
            child.UpdateLayout();

            foreach (string cut in CutLabels(child, scale))
            {
                faults.Add(where + ": " + cut);
            }
        }

        /// <summary>
        /// The labels of a laid-out part that do not fit their room: a one-line label wider than the room it was given (its
        /// margin left out), a wrapped one in more than two lines.
        /// </summary>
        private static List<string> CutLabels(DependencyObject root, double scale)
        {
            var cut = new List<string>();

            foreach (TextBlock text in Descendants(root).OfType<TextBlock>().ToList())
            {
                if (string.IsNullOrEmpty(text.Text) || text.ActualWidth <= 0 || !IsShown(text))
                {
                    continue;
                }

                if (text.TextWrapping == TextWrapping.NoWrap)
                {
                    double room = text.ActualWidth;
                    text.Measure(new Size(double.PositiveInfinity, double.PositiveInfinity));
                    double need = text.DesiredSize.Width - text.Margin.Left - text.Margin.Right;

                    if (need > room + 1.0 / scale)
                    {
                        cut.Add(
                            "\"" + text.Text + "\" needs " + need.ToString("0.0") + ", has " + room.ToString("0.0")
                        );
                    }
                }
                else
                {
                    double line = text.FontFamily.LineSpacing * text.FontSize;

                    // Display metrics round a line up to a device pixel: two lines at 150 per cent are 30.7, not 29.3.
                    if (text.ActualHeight > 2 * line + 2.0)
                    {
                        cut.Add(
                            "\""
                                + text.Text
                                + "\" in more than two lines ("
                                + text.ActualHeight.ToString("0.0")
                                + " high)"
                        );
                    }
                }
            }

            return cut;
        }

        private static bool IsShown(DependencyObject element)
        {
            for (DependencyObject d = element; d != null; d = VisualTreeHelper.GetParent(d))
            {
                if (d is UIElement ui && ui.Visibility != Visibility.Visible)
                {
                    return false;
                }
            }

            return true;
        }

        [Fact]
        public void ThePinKeepsASlideOutOpen_AndEscShutsADropDown_AtEitherScale()
        {
            foreach (double dpi in Dpis)
            {
                OnScaled(dpi, null, 3000, (model, workspace, strip, scale) =>
                {
                    RibbonPanel draw = strip.Children.OfType<RibbonPanel>().First(p => p.SlideOut != null);
                    var popup = (Popup)draw.Template.FindName("PART_Popup", draw);

                    // Esc with the slide-out open and pinned leaves it open; unpinned, it shuts it.
                    draw.IsDropDownOpen = true;
                    Pump(popup);
                    Assert.True(draw.IsDropDownOpen);
                    draw.IsPinned = true;
                    Escape(draw);
                    Assert.True(draw.IsDropDownOpen, dpi + ": Esc shut a pinned slide-out");
                    draw.IsPinned = false;
                    Escape(draw);
                    Assert.False(draw.IsDropDownOpen, dpi + ": Esc left an unpinned slide-out open");
                });
            }
        }

        // Window.PreviewKeyDown is what carries Esc to the panel in the app; in the host there is no window, so the handler it
        // would call is called with an Escape press made over a stand-in for the presentation source.
        private static void Escape(RibbonPanel panel)
        {
            MethodInfo handler = typeof(RibbonPanel).GetMethod(
                "OnWindowKeyDown",
                BindingFlags.Instance | BindingFlags.NonPublic
            );
            var press = new KeyEventArgs(Keyboard.PrimaryDevice, new StandInSource(), 0, Key.Escape)
            {
                RoutedEvent = Keyboard.PreviewKeyDownEvent,
            };
            handler.Invoke(panel, new object[] { panel, press });
        }

        private sealed class StandInSource : PresentationSource
        {
            public override Visual RootVisual { get; set; }

            public override bool IsDisposed => false;

            protected override CompositionTarget GetCompositionTargetCore() => null;
        }
    }
}
