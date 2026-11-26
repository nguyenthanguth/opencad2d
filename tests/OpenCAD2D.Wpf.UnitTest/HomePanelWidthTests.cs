using System;
using System.Collections.Generic;
using System.IO.Packaging;
using System.Linq;
using System.Threading;
using System.Windows;
using System.Windows.Controls;
using Xunit;
using static OpenCAD2D.Wpf.UnitTest.RibbonTabHarness;

namespace OpenCAD2D.Wpf.UnitTest
{
    /// <summary>
    /// The widths of Home's panels against the RibbonHome board (D1317, AutoCAD's own widths for the four floors): a panel is
    /// no wider than its design width and a pixel, and no more than three narrower (the board is drawn with Inter, this machine
    /// with Segoe UI where Inter is not installed); the four floored panels are exactly their floors.
    /// </summary>
    public sealed class HomePanelWidthTests
    {
        // Title, design width, whether a floor (MinFullWidth) sets it.
        private static readonly (string Title, double Design, bool Floored)[] Design =
        {
            ("Draw", 224, true),
            ("Modify", 250, true),
            ("Annotation", 184, false),
            ("Layers", 276, false),
            ("Block", 160, false),
            ("Properties", 268, false),
            ("Groups", 70, false),
            ("Utilities", 97, true),
            ("Clipboard", 80, true),
            ("View", 91, false),
        };

        [Fact]
        public void EachHomePanelIsNoWiderThanItsDesignWidth()
        {
            using (var folder = new TestFolder())
            {
                string room = folder.Write("room.dxf", TestFiles.Room(out _, out _));
                Exception failure = null;
                var widths = new List<string>();
                var thread = new Thread(() =>
                {
                    try
                    {
                        _ = PackUriHelper.UriSchemePack;
                        var model = new MainViewModel();
                        Assert.True(model.Open(room));
                        var workspace = new CadWorkspace { DataContext = model };

                        try
                        {
                            Layout(workspace, 3000);
                            RibbonStrip strip = Descendants(workspace)
                                .OfType<RibbonStrip>()
                                .Single(s => ReferenceEquals(s.DataContext, model));
                            List<RibbonPanel> panels = strip.Children.OfType<RibbonPanel>().ToList();
                            Assert.Equal(Design.Select(d => d.Title), panels.Select(p => p.Title));

                            for (int i = 0; i < panels.Count; i++)
                            {
                                double width = panels[i].ActualWidth;
                                widths.Add(Design[i].Title + " " + width);
                                Assert.Equal(RibbonPanelSize.Full, panels[i].Size);

                                if (Design[i].Floored)
                                {
                                    Assert.Equal(Design[i].Design, width, 0.5);
                                }
                                else
                                {
                                    Assert.InRange(width, Design[i].Design - 3, Design[i].Design + 1);
                                }
                            }
                        }
                        finally
                        {
                            workspace.DrawingCanvas.Dispose();
                        }
                    }
                    catch (Exception ex)
                    {
                        failure = new Exception(string.Join(", ", widths), ex);
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
