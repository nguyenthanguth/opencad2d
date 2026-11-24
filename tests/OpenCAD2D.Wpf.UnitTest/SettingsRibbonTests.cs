using System;
using System.Collections.Generic;
using System.IO.Packaging;
using System.Linq;
using System.Threading;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using Xunit;
using static OpenCAD2D.Wpf.UnitTest.RibbonTabHarness;

namespace OpenCAD2D.Wpf.UnitTest
{
    /// <summary>
    /// The Settings tab of the ribbon (design RibbonSettings): one panel, Global settings, with Options, then one big button for
    /// each page of the dialog in the order of its list, each opening the dialog on that page. The labels are short where a page
    /// name is long (Canvas, Units, Folders); the tooltip gives the page's full name.
    /// </summary>
    public sealed class SettingsRibbonTests
    {
        // Label, tooltip and the page it opens (null for Options, which opens the dialog where it was last left).
        private static readonly string[][] Buttons =
        {
            new[] { "Options", "Global settings (OPTIONS)", null },
            new[] { "Appearance", "Global settings › Appearance", "Appearance" },
            new[] { "Canvas", "Global settings › Canvas and cursor", "Canvas and cursor" },
            new[] { "Units", "Global settings › Units and rounding", "Units and rounding" },
            new[] { "Text and fonts", "Global settings › Text and fonts", "Text and fonts" },
            new[] { "Folders", "Global settings › Folder preference", "Folder preference" },
            new[] { "Security", "Global settings › Security", "Security" },
            new[] { "Shortcuts", "Global settings › Shortcuts", "Shortcuts" },
            new[] { "Save and export", "Global settings › Save and export", "Save and export" },
        };

        [Fact]
        public void TheSettingsTab_HasOptionsAndOneButtonForEveryPageOfGlobalSettings_InTheOrderOfItsList()
        {
            OnSettingsTab(
                (model, workspace, buttons) =>
                {
                    Assert.Equal(Buttons.Select(b => b[0]), buttons.Select(b => (string)b.Content));
                    Assert.Equal(Buttons.Select(b => b[1]), buttons.Select(b => (string)b.ToolTip));
                    Assert.Equal(Buttons.Select(b => b[2]), buttons.Select(b => (string)b.CommandParameter));
                    Assert.All(buttons, b => Assert.True(b.IsEnabled, (string)b.Content));
                    Assert.All(buttons, b => Assert.Equal(72, b.ActualHeight));

                    // After Options, the pages are those of the dialog, one each, in the order of its list.
                    string[] pages = model.CreateGlobalSettings().Categories.Select(c => c.Name).ToArray();
                    Assert.Equal(pages, buttons.Skip(1).Select(b => (string)b.CommandParameter));
                }
            );
        }

        [Fact]
        public void EachButton_RunsTheOpenCommandOfTheTab_AndItsParameterOpensTheDialogOnThePageItNames()
        {
            OnSettingsTab(
                (model, workspace, buttons) =>
                {
                    // A button is never pressed here: the window the workspace gives the model would show the dialog.
                    var tab = (SettingsRibbonViewModel)model.SettingsTab.TabContent;
                    Assert.All(buttons, b => Assert.Same(tab.OpenCommand, b.Command));
                    Assert.All(buttons.Where(b => b.CommandParameter != null), b =>
                        Assert.Equal(
                            (string)b.CommandParameter,
                            model.CreateGlobalSettings((string)b.CommandParameter).SelectedCategory.Name
                        )
                    );
                }
            );
        }

        [Fact]
        public void TheOpenCommandOfTheTab_AsksForTheDialogOnTheParameter_AndForNoPageWhereItHasNone()
        {
            // A model with no window: the request is caught here, and nothing is shown.
            var model = new MainViewModel();
            var asked = new List<string>();
            model.SettingsRequested += (sender, category) => asked.Add(category);
            var tab = (SettingsRibbonViewModel)model.SettingsTab.TabContent;

            foreach (string[] button in Buttons)
            {
                tab.OpenCommand.Execute(button[2]);
            }

            Assert.Equal(Buttons.Select(b => b[2]), asked);
        }

        [Fact]
        public void TheLongLabels_Text_and_fonts_AndSave_and_export_BreakOverTwoLines_TheShortOnesStayOnOne()
        {
            OnSettingsTab(
                (model, workspace, buttons) =>
                {
                    double Height(string label) =>
                        Descendants(buttons.Single(b => (string)b.Content == label))
                            .OfType<TextBlock>()
                            .Single()
                            .ActualHeight;

                    double one = Height("Units");
                    Assert.All(new[] { "Options", "Appearance", "Canvas", "Folders", "Security", "Shortcuts" }, label =>
                        Assert.Equal(one, Height(label), 1)
                    );
                    Assert.Equal(2 * one, Height("Text and fonts"), 1);
                    Assert.Equal(2 * one, Height("Save and export"), 1);
                }
            );
        }

        [Fact]
        public void TheSettingsTab_HasOneGlobalSettingsPanel_AndEveryButtonDrawsAnIcon()
        {
            OnSettingsTab(
                (model, workspace, buttons) =>
                {
                    TextBlock title = Descendants(workspace)
                        .OfType<TextBlock>()
                        .Single(t => t.Text == "Global settings" && t.DataContext is SettingsRibbonViewModel);
                    Assert.NotNull(title);
                    Assert.All(buttons, b => Assert.Single(Descendants(b).OfType<IconView>()));
                    Assert.All(buttons, b =>
                        Assert.False(Descendants(b).OfType<IconView>().Single().Data.IsEmpty(), (string)b.Content)
                    );
                    Assert.Equal(
                        buttons.Count,
                        buttons
                            .Select(b => Descendants(b).OfType<IconView>().Single().Data.ToString())
                            .Distinct()
                            .Count()
                    );
                }
            );
        }

        // The Settings tab shown in the window drawn offscreen, its buttons from left to right.
        private static void OnSettingsTab(Action<MainViewModel, CadWorkspace, List<ButtonBase>> check)
        {
            using (var folder = new TestFolder())
            {
                Exception failure = null;
                var thread = new Thread(() =>
                {
                    try
                    {
                        // Registers the pack scheme, which the XAML of the controls is loaded through, with no Application running.
                        _ = PackUriHelper.UriSchemePack;
                        var model = new MainViewModel();
                        var workspace = new CadWorkspace { DataContext = model };
                        LetGoOfTheWindow(model);
                        model.SelectedTab = model.SettingsTab;

                        try
                        {
                            Layout(workspace, 1600);
                            Pump(workspace);
                            var buttons = Descendants(workspace)
                                .OfType<ButtonBase>()
                                .Where(b => b.DataContext is SettingsRibbonViewModel && !(b is ToggleButton))
                                .OrderBy(b => Left(b, workspace))
                                .ToList();
                            check(model, workspace, buttons);
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
