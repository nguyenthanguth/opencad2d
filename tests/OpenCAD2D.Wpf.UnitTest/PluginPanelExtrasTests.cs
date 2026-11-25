using System;
using System.Collections.Generic;
using System.Linq;
using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using GeometryHelper.Dxf;
using OpenCAD2D.Plugins;
using Xunit;

namespace OpenCAD2D.Wpf.UnitTest
{
    /// <summary>
    /// What a plugin's panel has beyond its items (A2P P3 to P5): a slide-out under its title, a dialog launcher at the right
    /// of the title, and buttons that run a macro. The plugin is written by the test, as a hook plugin, so that the sample's
    /// panels stay as they were.
    /// </summary>
    public sealed class PluginPanelExtrasTests : IDisposable
    {
        private readonly PluginWindowKit _kit = new PluginWindowKit();

        public void Dispose() => _kit.Dispose();

        private static PluginPanelViewModel PanelOf(MainViewModel model, string title) =>
            ((PluginTabViewModel)model.PluginTabs.Single(t => t.TabHeader == "Hook").TabContent).Panels.Single(p =>
                p.Title == title
            );

        private static PluginButtonViewModel ButtonOf(IEnumerable<PluginColumnViewModel> columns, string text) =>
            columns.SelectMany(c => c.Items).OfType<PluginButtonViewModel>().Single(b => b.Text == text);

        private MainViewModel Load(Action<CadRibbonPanel> fill, string title = "Number")
        {
            MainViewModel model = _kit.Opened(out _, out _, out _);
            _kit.LoadHook(model, host =>
            {
                var panel = new CadRibbonPanel(title);
                fill(panel);
                var tab = new CadRibbonTab("Hook", "Hook");
                tab.Panels.Add(panel);
                host.AddRibbonTab(tab);
            });
            return model;
        }

        // ---- slide-outs (P3) ----

        [Fact]
        public void APanelWithASlideOut_HasItsColumnsLaidOutAsItsItems()
        {
            MainViewModel model = Load(panel =>
            {
                panel.Items.Add(new CadRibbonButton("Body", c => { }));
                panel.SlideOutItems.Add(new CadRibbonButton("Big", c => { }) { Size = CadRibbonItemSize.Large });

                for (int i = 1; i <= 4; i++)
                {
                    panel.SlideOutItems.Add(new CadRibbonButton("S" + i, c => { }));
                }
            });

            PluginPanelViewModel number = PanelOf(model, "Number");

            Assert.True(number.HasSlideOut);
            Assert.Equal(
                new[] { "Big", "S1+S2+S3", "S4" },
                number.SlideOutColumns.Select(c => string.Join("+", c.Items.Select(i => i.Text)))
            );
            Assert.Equal(new[] { "Body" }, number.Columns.SelectMany(c => c.Items).Select(i => i.Text));
        }

        [Fact]
        public void APanelWithNoSlideOut_HasNone()
        {
            MainViewModel model = Load(panel => panel.Items.Add(new CadRibbonButton("Body", c => { })));

            Assert.False(PanelOf(model, "Number").HasSlideOut);
            Assert.Empty(PanelOf(model, "Number").SlideOutColumns);
        }

        [Fact]
        public void ASlideOutButton_RunsItsCallback_AndIsAskedCanRunWithThePanel()
        {
            int ran = 0;
            int asked = 0;
            MainViewModel model = Load(panel =>
            {
                panel.Items.Add(new CadRibbonButton("Body", c => { }));
                panel.SlideOutItems.Add(
                    new CadRibbonButton("Slid", c => ran++)
                    {
                        CanRun = c =>
                        {
                            asked++;
                            return c.Selected.Count == 1;
                        },
                    }
                );
            });
            PluginButtonViewModel slid = ButtonOf(PanelOf(model, "Number").SlideOutColumns, "Slid");
            Assert.True(asked > 0);
            Assert.False(slid.IsEnabled);
            Assert.False(slid.RunCommand.CanExecute(null));

            model.Document.Selection.Set(new[] { model.Document.ModelSpace.Block.First().Handle });

            Assert.True(slid.IsEnabled);
            Assert.True(slid.RunCommand.CanExecute(null));
            slid.RunCommand.Execute(null);
            Assert.Equal(1, ran);
        }

        [Fact]
        public void ACollapsedPanelsDropDown_HoldsItsBodyAndItsSlideOut()
        {
            MainViewModel model = Load(panel =>
            {
                panel.Items.Add(new CadRibbonButton("Count the circles", c => { }));
                panel.SlideOutItems.Add(new CadRibbonButton("Start value", c => { }));
            });

            PluginRibbonHarness.OnTab(
                model,
                model.PluginTabs[0],
                full => 20,
                (workspace, strip, at) =>
                {
                    RibbonPanel number = RibbonTabHarness.Panel(strip, "Number");
                    Assert.Equal(RibbonPanelSize.Collapsed, number.Size);
                    var popup = (Popup)number.Template.FindName("PART_Popup", number);

                    number.IsDropDownOpen = true;
                    RibbonTabHarness.Pump(popup);
                    popup.Child.Measure(new Size(2000, 960));
                    popup.Child.Arrange(new Rect(0, 0, popup.Child.DesiredSize.Width, popup.Child.DesiredSize.Height));

                    string[] names = RibbonTabHarness
                        .Descendants(popup.Child)
                        .OfType<Button>()
                        .Select(AutomationProperties.GetName)
                        .Where(n => !string.IsNullOrEmpty(n))
                        .ToArray();
                    Assert.Contains("Count the circles", names);
                    Assert.Contains("Start value", names);

                    // The slide-out is under the body, after a line.
                    Button body = RibbonTabHarness
                        .Descendants(popup.Child)
                        .OfType<Button>()
                        .Single(b => AutomationProperties.GetName(b) == "Count the circles");
                    Button slid = RibbonTabHarness
                        .Descendants(popup.Child)
                        .OfType<Button>()
                        .Single(b => AutomationProperties.GetName(b) == "Start value");
                    Assert.True(
                        slid.TranslatePoint(new Point(0, 0), popup.Child).Y
                            > body.TranslatePoint(new Point(0, body.ActualHeight), popup.Child).Y
                    );

                    // Shut again, the body is back in the panel and the slide-out waits for the panel to widen.
                    number.IsDropDownOpen = false;
                    Assert.Null(((Decorator)number.Template.FindName("PART_Dropped", number)).Child);
                }
            );
        }

        [Fact]
        public void APanelWithASlideOut_DrawsItsTitleAsTheTitleThatOpensIt()
        {
            MainViewModel model = Load(panel =>
            {
                panel.Items.Add(new CadRibbonButton("Body", c => { }));
                panel.SlideOutItems.Add(new CadRibbonButton("Start value", c => { }));
            });

            PluginRibbonHarness.OnTab(model, model.PluginTabs[0], null, (workspace, strip, at) =>
            {
                RibbonPanel number = RibbonTabHarness.Panel(strip, "Number");
                ToggleButton title = Assert.IsType<ToggleButton>(number.Header);
                Assert.Equal("Number ▾", title.Content);
                Assert.False(number.IsDropDownOpen);

                RibbonTabHarness.OpenSlideOut(number);

                Assert.True(number.IsDropDownOpen);
                Assert.True(title.IsChecked);
                Assert.Contains(RibbonTabHarness.Descendants(number.SlideOut).OfType<Button>(), b =>
                    AutomationProperties.GetName(b) == "Start value"
                );
            });
        }

        [Fact]
        public void UnloadingThePlugin_DropsTheSlideOut()
        {
            MainViewModel model = Load(panel =>
            {
                panel.Items.Add(new CadRibbonButton("Body", c => { }));
                panel.SlideOutItems.Add(new CadRibbonButton("Start value", c => { }));
                panel.DialogLauncher = new CadRibbonButton("Settings", "CIRCLE");
            });
            Assert.True(PanelOf(model, "Number").HasSlideOut);

            Assert.True(model.UnloadPlugin(PluginWindowKit.HookName).Succeeded);

            Assert.Empty(model.PluginTabs);
        }

        // ---- dialog launchers (P4) ----

        [Fact]
        public void ADialogLauncher_StartsItsCommand_AsTheRibbonsButtonsDo()
        {
            MainViewModel model = Load(panel =>
            {
                panel.Items.Add(new CadRibbonButton("Body", c => { }));
                panel.DialogLauncher = new CadRibbonButton("Numbering settings", "NUMCIRCLES2");
            });
            PluginButtonViewModel launcher = PanelOf(model, "Number").Launcher;
            Assert.NotNull(launcher);
            model.Execute("LINE");
            Assert.Equal("LINE", model.Session.Current.Name);

            launcher.RunCommand.Execute(null);

            // A running command is cancelled first, then the command named is started: NUMCIRCLES2 is not a command of this
            // hook plugin, so the command line says so.
            Assert.False(model.Session.IsRunning);
            Assert.Contains(model.History, line => line.Contains("*Cancel*"));
            Assert.Contains(model.History, line => line.StartsWith("Command: NUMCIRCLES2", StringComparison.Ordinal));
        }

        [Fact]
        public void ADialogLauncherWhoseCanRunIsFalse_IsGrey()
        {
            MainViewModel model = Load(panel =>
            {
                panel.Items.Add(new CadRibbonButton("Body", c => { }));
                panel.DialogLauncher = new CadRibbonButton("Numbering settings", c => { })
                {
                    CanRun = c => c.Selected.Count > 0,
                };
            });
            PluginButtonViewModel launcher = PanelOf(model, "Number").Launcher;

            Assert.False(launcher.RunCommand.CanExecute(null));

            model.Document.Selection.Set(new[] { model.Document.ModelSpace.Block.First().Handle });

            Assert.True(launcher.RunCommand.CanExecute(null));
        }

        [Fact]
        public void ADialogLaunchersTooltip_SaysItsTitleAndThePluginsName()
        {
            MainViewModel model = Load(panel =>
            {
                panel.Items.Add(new CadRibbonButton("Body", c => { }));
                panel.DialogLauncher = new CadRibbonButton("Settings", "CIRCLE")
                {
                    ToolTip = "Numbering settings",
                    Description = "Opens the numbering settings.",
                };
            });

            PluginRibbonHarness.OnTab(model, model.PluginTabs[0], null, (workspace, strip, at) =>
            {
                RibbonPanel number = RibbonTabHarness.Panel(strip, "Number");
                ToolTip tip = Assert.IsType<ToolTip>(number.LauncherToolTip);
                var info = Assert.IsType<PluginToolTipInfo>(tip.Content);

                Assert.Equal("Numbering settings (CIRCLE)", info.Title);
                Assert.Equal("Opens the numbering settings.", info.Body);
                Assert.Equal(PluginWindowKit.HookName, info.Plugin);
                Assert.Same(PanelOf(model, "Number").Launcher.RunCommand, number.Launcher);
            });
        }

        [Fact]
        public void APanelWithNoLauncher_HasNone()
        {
            MainViewModel model = Load(panel => panel.Items.Add(new CadRibbonButton("Body", c => { })));

            Assert.Null(PanelOf(model, "Number").Launcher);

            PluginRibbonHarness.OnTab(model, model.PluginTabs[0], null, (workspace, strip, at) =>
            {
                RibbonPanel number = RibbonTabHarness.Panel(strip, "Number");
                Assert.Null(number.Launcher);
                Assert.Null(number.LauncherToolTip);
            });
        }

        // ---- macros (P5) ----

        [Fact]
        public void AMacroButton_RunsItsMacro_AndItsTooltipHasNoCommandInBrackets()
        {
            MainViewModel model = Load(panel =>
            {
                CadRibbonButton macro = CadRibbonButton.FromMacro("Circle R50", "^C^C_CIRCLE;0,0;50;");
                macro.Description = "Draws a circle of radius 50 at 0,0.";
                panel.Items.Add(macro);
            });
            PluginButtonViewModel button = ButtonOf(PanelOf(model, "Number").Columns, "Circle R50");
            Assert.True(button.IsEnabled);
            Assert.Null(button.Command);
            Assert.Equal("Circle R50", button.ToolTipInfo.Title);
            Assert.Equal("Draws a circle of radius 50 at 0,0.", button.ToolTipInfo.Body);
            Assert.Equal(PluginWindowKit.HookName, button.ToolTipInfo.Plugin);
            int before = model.Document.ModelSpace.Block.OfType<DxfCircle>().Count();

            button.RunCommand.Execute(null);

            DxfCircle[] circles = model.Document.ModelSpace.Block.OfType<DxfCircle>().ToArray();
            Assert.Equal(before + 1, circles.Length);
            Assert.Equal(50.0, circles[circles.Length - 1].Radius, 9);
            Assert.False(model.Session.IsRunning);
            Assert.Contains(model.History, line => line.Contains("CIRCLE"));
        }

        [Fact]
        public void AMacroWithoutADrawing_IsGrey()
        {
            MainViewModel model = Load(panel => panel.Items.Add(CadRibbonButton.FromMacro("Line", "^C^C_LINE;")));
            PluginButtonViewModel button = ButtonOf(PanelOf(model, "Number").Columns, "Line");
            Assert.True(button.IsEnabled);

            Assert.True(model.CloseDocument());

            Assert.False(button.IsEnabled);
            Assert.False(button.RunCommand.CanExecute(null));
        }

        [Fact]
        public void AMacroWithASnapWord_TakesItAsTheOverride()
        {
            MainViewModel model = Load(panel =>
                panel.Items.Add(CadRibbonButton.FromMacro("Line from the end", "^C^C_LINE;0,0;_END;"))
            );
            PluginButtonViewModel button = ButtonOf(PanelOf(model, "Number").Columns, "Line from the end");

            button.RunCommand.Execute(null);

            Assert.Equal("LINE", model.Session.Current.Name);
            Assert.Equal(CadSnapModes.Endpoint, model.Snap.Override);
            Assert.Contains(model.History, line => line.Contains("_end of"));
        }

        [Fact]
        public void AMacroThePresenterDoesNotRun_IsRefusedWithThePluginsName()
        {
            MainViewModel model = Load(panel => panel.Items.Add(CadRibbonButton.FromMacro("Repeat", "*^C^C_LINE")));

            ButtonOf(PanelOf(model, "Number").Columns, "Repeat").RunCommand.Execute(null);

            Assert.False(model.Session.IsRunning);
            Assert.Equal(
                "HookPlugin: the macro *^C^C_LINE uses *, which the presenter does not run.",
                model.History.Last()
            );
        }
    }
}
