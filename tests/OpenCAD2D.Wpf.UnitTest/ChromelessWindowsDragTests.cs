using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Xunit;

namespace OpenCAD2D.Wpf.UnitTest
{
    /// <summary>
    /// A window that draws its own header can be moved by it: every chromeless window wires MouseLeftButtonDown on its header to
    /// OnHeaderDown, which calls DragMove, but the plot preview, which covers the window it belongs to.
    /// </summary>
    public sealed class ChromelessWindowsDragTests
    {
        [Fact]
        public void EveryChromelessWindowButThePreviewWiresItsHeaderToDragMove()
        {
            var faults = new List<string>();

            foreach (
                string file in DialogXaml
                    .ChromelessFiles()
                    .Where(f => f.Contains(Path.DirectorySeparatorChar + "Controls" + Path.DirectorySeparatorChar))
            )
            {
                string name = Path.GetFileName(file);

                // The preview covers its owner window (AutoCAD's replaces the drawing area), so it is not moved.
                if (name == "PlotPreviewWindow.xaml")
                {
                    continue;
                }

                string code = file + ".cs";
                bool wired = File.ReadAllText(file).Contains("MouseLeftButtonDown=\"OnHeaderDown\"");
                bool handler =
                    File.Exists(code)
                    && File.ReadAllText(code).Contains("void OnHeaderDown")
                    && File.ReadAllText(code).Contains("DragMove()");

                if (!wired || !handler)
                {
                    faults.Add(
                        name
                            + (wired ? string.Empty : " (header not wired)")
                            + (handler ? string.Empty : " (no DragMove)")
                    );
                }
            }

            Assert.True(faults.Count == 0, faults.Count + " windows cannot be dragged:\n" + string.Join("\n", faults));
        }
    }
}
