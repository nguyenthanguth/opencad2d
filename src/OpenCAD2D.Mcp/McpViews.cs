using System;
using System.Collections.Generic;
using System.Runtime.CompilerServices;

namespace OpenCAD2D.Mcp
{
    /// <summary>
    /// The view a server keeps for a drawing it works on in its own process, a layout at a time, which <c>render_png</c> draws when it
    /// is given no box: what <c>zoom_window</c> and <c>zoom_object</c> set and <c>zoom_extents</c> clears. Nothing is written to the
    /// drawing; the view is the server's, as the app's canvas keeps its own.
    /// </summary>
    internal static class McpViews
    {
        private static readonly ConditionalWeakTable<CadDocument, Dictionary<string, double[]>> Views =
            new ConditionalWeakTable<CadDocument, Dictionary<string, double[]>>();

        private static string Key(string layout) => string.IsNullOrEmpty(layout) ? "MODEL" : layout.ToUpperInvariant();

        /// <summary>Gets the view kept for a layout as <c>[minX, minY, maxX, maxY]</c>; null where none is kept.</summary>
        public static double[] Get(CadDocument document, string layout) =>
            Views.TryGetValue(document, out Dictionary<string, double[]> kept)
            && kept.TryGetValue(Key(layout), out double[] box)
                ? box
                : null;

        /// <summary>Keeps a view for a layout; null clears it.</summary>
        public static void Set(CadDocument document, string layout, double[] box)
        {
            if (document == null)
            {
                throw new ArgumentNullException(nameof(document));
            }

            Dictionary<string, double[]> kept = Views.GetOrCreateValue(document);

            if (box == null)
            {
                kept.Remove(Key(layout));
            }
            else
            {
                kept[Key(layout)] = box;
            }
        }
    }
}
