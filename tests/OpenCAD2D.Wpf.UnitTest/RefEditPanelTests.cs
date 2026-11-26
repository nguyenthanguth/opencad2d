using System;
using System.Collections.Generic;
using System.IO.Packaging;
using System.Linq;
using System.Threading;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using GeometryHelper.Dxf;
using GeometryHelper.Geometry;
using Xunit;
using static OpenCAD2D.Wpf.UnitTest.RibbonTabHarness;

namespace OpenCAD2D.Wpf.UnitTest
{
    /// <summary>
    /// The Edit Reference panel of REFEDIT, AutoCAD 2021's: merged at the right end of the tab shown while an edit is open,
    /// four large buttons (Save Changes, Discard Changes, a line, Add to Working Set, Remove from Working Set), each running
    /// REFCLOSE or REFSET with its option, and gone again at REFCLOSE.
    /// </summary>
    public sealed class RefEditPanelTests
    {
        private static readonly string[] Words =
        {
            "Save Changes",
            "Discard Changes",
            "Add to Working Set",
            "Remove from Working Set",
        };

        private static CadDocument Boxes(out string reference)
        {
            DxfDatabase database = CadTemplate.CreateMetric();
            var box = new DxfBlockTableRecord { Name = "BOX", Origin = new GeoPoint3(0, 0, 0) };
            database.BlockTable.Add(box);
            box.AppendEntity(new DxfLine(new GeoPoint3(0, 0, 0), new GeoPoint3(100, 0, 0)));
            box.AppendEntity(new DxfCircle(new GeoPoint3(50, 30, 0), new GeoVector3(0, 0, 1), 10.0));
            var instance = new DxfBlockReference(new GeoPoint3(1000, 0, 0), box);
            database.ModelSpace.AppendEntity(instance);
            reference = instance.Handle;
            return CadDocument.From(database, "boxes.dxf");
        }

        /// <summary>
        /// A drawing with a BOX reference opened in the model, the window drawn offscreen at a width with the tab chosen shown
        /// (null for Home), the edit open or not.
        /// </summary>
        private static void OnPanel(
            double width,
            bool open,
            Func<MainViewModel, CadWorkspaceExtension> tab,
            Action<MainViewModel, CadWorkspace> check
        )
        {
            Exception failure = null;
            var thread = new Thread(() =>
            {
                try
                {
                    _ = PackUriHelper.UriSchemePack;
                    var model = new MainViewModel();
                    CadDocument document = Boxes(out string reference);
                    model.OpenDocument(document, "boxes.dxf");
                    var workspace = new CadWorkspace { DataContext = model };
                    LetGoOfTheWindow(model);

                    try
                    {
                        model.SelectedTab = tab?.Invoke(model);

                        if (open)
                        {
                            Assert.True(document.OpenRefEdit(new[] { reference }, null).Succeeded);
                        }

                        Layout(workspace, width);
                        Pump(workspace);
                        check(model, workspace);
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

        private static RibbonPanel EditReference(CadWorkspace workspace) =>
            Descendants(workspace).OfType<RibbonPanel>().Single(p => p.Title == "Edit Reference");

        private static bool IsShown(UIElement element)
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
        public void WithNoEditOpenTheEditReferencePanelIsNotShownOnAnyTab()
        {
            foreach (
                Func<MainViewModel, CadWorkspaceExtension> tab in new Func<MainViewModel, CadWorkspaceExtension>[]
                {
                    null,
                    m => m.InsertTab,
                }
            )
            {
                OnPanel(1920, false, tab, (model, workspace) =>
                {
                    Assert.False(model.IsRefEditOpen);
                    Assert.False(IsShown(EditReference(workspace)));
                });
            }
        }

        [Fact]
        public void AnOpenEditShowsThePanelAtTheRightEndOfTheShownTab_HomeAndInsert()
        {
            foreach (
                Func<MainViewModel, CadWorkspaceExtension> tab in new Func<MainViewModel, CadWorkspaceExtension>[]
                {
                    null,
                    m => m.InsertTab,
                }
            )
            {
                OnPanel(1920, true, tab, (model, workspace) =>
                {
                    Assert.True(model.IsRefEditOpen);
                    RibbonPanel panel = EditReference(workspace);
                    Assert.True(IsShown(panel));

                    // At the right end: no other panel of the shown tab is right of it, and it ends where the ribbon does.
                    RibbonStrip strip = Descendants(workspace)
                        .OfType<RibbonStrip>()
                        .First(s => IsShown(s) && s.Children.OfType<RibbonPanel>().Any());
                    double panelLeft = panel.TransformToAncestor(workspace).Transform(new Point(0, 0)).X;
                    double stripRight = strip
                        .TransformToAncestor(workspace)
                        .Transform(new Point(strip.ActualWidth, 0))
                        .X;
                    Assert.True(
                        panelLeft >= stripRight - 0.5,
                        "panel at " + panelLeft + ", strip ends at " + stripRight
                    );
                    Assert.True(
                        panelLeft + panel.ActualWidth <= workspace.ActualWidth + 0.5,
                        "panel ends at " + (panelLeft + panel.ActualWidth)
                    );

                    // Its four buttons in the order of the CUIX, the line between the second and the third.
                    List<FrameworkElement> parts = ((Panel)panel.Body)
                        .Children.OfType<FrameworkElement>()
                        .OrderBy(c => c.TransformToAncestor(panel).Transform(new Point(0, 0)).X)
                        .ToList();
                    Assert.Equal(5, parts.Count);
                    Assert.IsType<Border>(parts[2]);
                    var buttons = parts.OfType<Button>().ToList();
                    Assert.Equal(Words, buttons.Select(b => ((string)b.Content).Replace("\n", " ")));
                    Assert.Equal(
                        new[]
                        {
                            "Save Changes (REFCLOSE)",
                            "Discard Changes (REFCLOSE)",
                            "Add to Working Set (REFSET)",
                            "Remove from Working Set (REFSET)",
                        },
                        buttons.Select(b => (string)b.ToolTip)
                    );
                    Assert.All(buttons, b => Assert.Equal(72.0, b.ActualHeight, 1));

                    // The icons are the four of the kit, the label in at most two lines.
                    string[] icons = { "GhIconRefSave", "GhIconRefDiscard", "GhIconRefAdd", "GhIconRefRemove" };

                    for (int i = 0; i < 4; i++)
                    {
                        Assert.Same(panel.FindResource(icons[i] + ".Color"), buttons[i].Tag);
                        TextBlock label = Descendants(buttons[i]).OfType<TextBlock>().Single();
                        Assert.True(
                            label.ActualHeight <= 2 * label.FontSize * 1.4 + 2,
                            label.Text
                                + ": "
                                + label.ActualHeight
                                + " high, "
                                + label.ActualWidth
                                + " wide, font "
                                + label.FontFamily
                                + " "
                                + label.FontSize
                        );
                    }
                });
            }
        }

        [Fact]
        public void EachButtonRunsItsCommandWithItsOption()
        {
            string[][] expected =
            {
                new[] { "REFCLOSE", "Save" },
                new[] { "REFCLOSE", "Discard" },
                new[] { "REFSET", "Add" },
                new[] { "REFSET", "Remove" },
            };

            for (int i = 0; i < 4; i++)
            {
                int which = i;
                OnPanel(1920, true, null, (model, workspace) =>
                {
                    RibbonPanel panel = EditReference(workspace);
                    Button button = ((Panel)panel.Body).Children.OfType<Button>().ElementAt(which);
                    Assert.Equal(expected[which][0] + " " + expected[which][1], button.CommandParameter);
                    Assert.Same(model.StartCommandWithOptionCommand, button.Command);

                    Press(button);

                    if (expected[which][0] == "REFCLOSE")
                    {
                        // Save or Discard ends the edit: the panel goes.
                        Assert.False(model.Session.IsRunning, model.Session.PromptText);
                        Assert.Null(model.Document.RefEdit);
                        Assert.False(model.IsRefEditOpen);
                        Layout(workspace, 1920);
                        Assert.False(IsShown(panel));
                    }
                    else
                    {
                        // Add or Remove asks for the objects, the edit still open.
                        Assert.True(model.Session.IsRunning);
                        Assert.Contains(expected[which][0], model.Session.History.Last(h => h.Contains("REFSET")));
                        Assert.True(model.IsRefEditOpen);
                        Assert.True(IsShown(panel));
                    }
                });
            }
        }

        [Fact]
        public void ThePanelTakesItsWidthFromTheStrip()
        {
            int without = 0;
            int with = 0;
            OnPanel(1920, false, null, (model, workspace) => without = HomeStrip(model, workspace).StepsTaken);
            OnPanel(1920, true, null, (model, workspace) =>
            {
                with = HomeStrip(model, workspace).StepsTaken;
                RibbonPanel panel = EditReference(workspace);
                Assert.Equal(RibbonPanelSize.Full, panel.Size);
            });

            // 1 920 holds Home's 1 695 and no more; the panel (about 280) leaves less than 1 584, so Home takes steps.
            Assert.Equal(0, without);
            Assert.True(with > without, "steps with the panel: " + with);
        }

        private static RibbonStrip HomeStrip(MainViewModel model, CadWorkspace workspace) =>
            Descendants(workspace).OfType<RibbonStrip>().Single(s => ReferenceEquals(s.DataContext, model));
    }
}
