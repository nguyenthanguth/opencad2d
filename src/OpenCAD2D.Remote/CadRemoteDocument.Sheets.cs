using System;
using System.Collections.Generic;
using System.Linq;

namespace OpenCAD2D.Remote
{
    /// <summary>The sheets of a drawing of the app: its layouts made and set up, and viewports put on them.</summary>
    public sealed partial class CadRemoteDocument
    {
        /// <summary>
        /// Asks the app how every layout is set up, model space first: its sheet, what is plotted, its scale and plot style. The
        /// answer is JSON text, an array with an object for each layout: <c>name</c>, <c>model</c>, <c>paper</c>, <c>width</c> and
        /// <c>height</c> (millimetres), <c>landscape</c>, <c>units</c>, <c>area</c>, <c>scale</c> (as <c>1:50</c>, null where fitted),
        /// <c>plotStyle</c> and <c>viewports</c>.
        /// </summary>
        /// <returns>The JSON text.</returns>
        /// <exception cref="CadRemoteException">Thrown when the drawing is closed.</exception>
        public string GetLayoutSetups() =>
            RemoteJson.Write(Ask(RemoteOps.Sheets, request => request.Set("action", "list")).GetArray("layouts"));

        /// <summary>
        /// Asks the app to add a layout on a sheet, as LAYOUT New and the page setup do, one undo step; it does not become the
        /// tab shown.
        /// </summary>
        /// <param name="name">The name of the tab.</param>
        /// <param name="paper">The sheet, as <c>ISO A3</c>, <c>ISO_A3</c> or <c>A3</c>; null for ISO A3.</param>
        /// <param name="landscape">True for the long side across.</param>
        /// <returns>How the new layout is set up, as one object of <see cref="GetLayoutSetups"/>.</returns>
        /// <exception cref="ArgumentNullException">Thrown when the name is null.</exception>
        /// <exception cref="CadRemoteException">Thrown when the drawing is closed, a command is running in it, the name does not fit, or the sheet is not one the plotter lists.</exception>
        public string AddLayout(string name, string paper, bool landscape)
        {
            if (name == null)
            {
                throw new ArgumentNullException(nameof(name));
            }

            return RemoteJson.Write(
                Ask(RemoteOps.Sheets, request =>
                        request.Set("action", "add").Set("name", name).Set("paper", paper).Set("landscape", landscape)
                    )
                    .Get("layout")
            );
        }

        /// <summary>Asks the app to change the page setup of a layout, one undo step; only what is given is changed.</summary>
        /// <param name="layout">The layout, or <c>Model</c>.</param>
        /// <param name="paper">The sheet; null to keep it.</param>
        /// <param name="orientation"><c>landscape</c> or <c>portrait</c>; null to keep it.</param>
        /// <param name="area"><c>layout</c>, <c>extents</c>, <c>display</c> or <c>limits</c>; null to keep it.</param>
        /// <param name="scale">As <c>1:50</c> or <c>0.02</c>, or <c>fit</c>; null to keep it.</param>
        /// <param name="plotStyle">The plot style table by the name of its file, empty for none; null to keep it.</param>
        /// <returns>How the layout is set up now, as one object of <see cref="GetLayoutSetups"/>.</returns>
        /// <exception cref="ArgumentNullException">Thrown when the layout is null.</exception>
        /// <exception cref="CadRemoteException">Thrown when the drawing is closed, a command is running in it, or a value is not one of those listed.</exception>
        public string SetPageSetup(
            string layout,
            string paper,
            string orientation,
            string area,
            string scale,
            string plotStyle
        )
        {
            if (layout == null)
            {
                throw new ArgumentNullException(nameof(layout));
            }

            return RemoteJson.Write(
                Ask(RemoteOps.Sheets, request =>
                        request
                            .Set("action", "setup")
                            .Set("layout", layout)
                            .Set("paper", paper)
                            .Set("orientation", orientation)
                            .Set("area", area)
                            .Set("scale", scale)
                            .Set("plotStyle", plotStyle)
                    )
                    .Get("layout")
            );
        }

        /// <summary>
        /// Asks the app to add a viewport to a layout that shows model space at a scale, as MVIEW with two corners does, one undo
        /// step.
        /// </summary>
        /// <param name="layout">The layout, not Model.</param>
        /// <param name="frame">The rectangle on the sheet, <c>[x1, y1, x2, y2]</c> in millimetres.</param>
        /// <param name="scale">As <c>1:50</c> or <c>0.02</c> (paper millimetres to the unit).</param>
        /// <param name="center">The point of model space at the middle of the frame, <c>[x, y]</c>; null for the middle of what model space draws.</param>
        /// <param name="frozenLayers">The layers to freeze in this viewport only, or null.</param>
        /// <returns>The handle of the viewport.</returns>
        /// <exception cref="ArgumentNullException">Thrown when the layout, the frame or the scale is null.</exception>
        /// <exception cref="CadRemoteException">Thrown when the drawing is closed, a command is running in it, or an argument is refused (see CadLayouts.AddViewport).</exception>
        public string AddViewport(
            string layout,
            IReadOnlyList<double> frame,
            string scale,
            IReadOnlyList<double> center,
            IEnumerable<string> frozenLayers
        )
        {
            if (layout == null)
            {
                throw new ArgumentNullException(nameof(layout));
            }

            if (frame == null)
            {
                throw new ArgumentNullException(nameof(frame));
            }

            if (scale == null)
            {
                throw new ArgumentNullException(nameof(scale));
            }

            return Ask(RemoteOps.Sheets, request =>
                    request
                        .Set("action", "viewport")
                        .Set("layout", layout)
                        .Set("frame", new JsonArray(frame.Select(n => (object)n)))
                        .Set("scale", scale)
                        .Set("center", new JsonArray((center ?? new double[0]).Select(n => (object)n)))
                        .Set("frozen", new JsonArray((frozenLayers ?? new string[0]).Select(n => (object)n)))
                )
                .GetString("handle");
        }
    }
}
