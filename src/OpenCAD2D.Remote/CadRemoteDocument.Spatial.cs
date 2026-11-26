using System;
using System.Collections.Generic;
using System.Linq;

namespace OpenCAD2D.Remote
{
    /// <summary>Where things are drawn: the box each entity takes and the overlaps of words, as the app draws the drawing.</summary>
    public sealed partial class CadRemoteDocument
    {
        /// <summary>
        /// Gets the box every entity of a space takes as the app draws it: the letters of an SHX or TrueType word and not its
        /// insertion point, the picture of a dimension, a block however deep and scaled, the columns of an MTEXT. An entity that draws
        /// nothing has no row. A line from (0, 0) to (100, 40) has the box from (0, 0) to (100, 40).
        /// </summary>
        /// <param name="layout">The name of a layout, or null or empty for model space.</param>
        /// <returns>The boxes, in the order the space is drawn.</returns>
        /// <exception cref="CadRemoteException">Thrown when the drawing is closed or has no layout of that name.</exception>
        public CadRemoteBounds GetBounds(string layout) =>
            BoundsOf(Ask(RemoteOps.Bounds, request => request.Set("layout", layout)));

        /// <summary>
        /// Gets the boxes of the entities of some handles as the app draws them, in the order given, each in the space that holds it.
        /// A handle the drawing has no entity of, or one that draws nothing, has no row.
        /// </summary>
        /// <param name="handles">The handles.</param>
        /// <returns>The boxes.</returns>
        /// <exception cref="ArgumentNullException">Thrown when the handles are null.</exception>
        /// <exception cref="CadRemoteException">Thrown when the drawing is closed.</exception>
        public CadRemoteBounds GetBoundsOf(IEnumerable<string> handles)
        {
            if (handles == null)
            {
                throw new ArgumentNullException(nameof(handles));
            }

            return BoundsOf(Ask(RemoteOps.Bounds, request => request.Set("handles", Strings(handles))));
        }

        /// <summary>
        /// Finds the entities whose words overlap what is drawn near them, as the app's spatial query does: a word is the frame of
        /// a text (a TEXT, an MTEXT's runs, a dimension's words) turned as the text is, and it overlaps a line, an arc, a polyline or
        /// a hatch that runs through it, a filled area it lies in, and the frame of another word it shares an area with; what only
        /// touches it within the tolerance does not, nor a part of its own entity. A kind is the DXF name of an entity,
        /// <c>TEXT</c>, <c>MTEXT</c>, <c>DIMENSION</c>, <c>MULTILEADER</c>, <c>LINE</c>, <c>INSERT</c>.
        /// </summary>
        /// <param name="layout">The name of a layout, or null or empty for model space.</param>
        /// <param name="words">The kinds whose words are looked at; null for TEXT, MTEXT, DIMENSION and MULTILEADER.</param>
        /// <param name="over">The kinds the words are looked at against; null for every kind.</param>
        /// <param name="handles">The handles the answer is about: only pairs in which one of them stands are given; null for every pair.</param>
        /// <param name="tolerance">How near an edge still counts as touching it, in drawing units; 0.001 is the library's.</param>
        /// <returns>The pairs, in the order the space draws the entities.</returns>
        /// <exception cref="CadRemoteException">Thrown when the drawing is closed or has no layout of that name.</exception>
        public CadRemoteOverlaps GetOverlaps(
            string layout,
            IEnumerable<string> words,
            IEnumerable<string> over,
            IEnumerable<string> handles,
            double tolerance
        )
        {
            JsonObject answer = Ask(RemoteOps.Overlaps, request =>
            {
                request.Set("layout", layout).Set("tolerance", tolerance);

                if (words != null)
                {
                    request.Set("words", Strings(words));
                }

                if (over != null)
                {
                    request.Set("over", Strings(over));
                }

                if (handles != null)
                {
                    request.Set("handles", Strings(handles));
                }
            });
            return new CadRemoteOverlaps(
                StringsOf(answer.GetArray("words")),
                StringsOf(answer.GetArray("others")),
                NumbersOf(answer.GetArray("boxes"))
            );
        }

        private static CadRemoteBounds BoundsOf(JsonObject answer) =>
            new CadRemoteBounds(StringsOf(answer.GetArray("handles")), NumbersOf(answer.GetArray("boxes")));

        private static JsonArray Strings(IEnumerable<string> items) => new JsonArray(items.Cast<object>());

        private static string[] StringsOf(JsonArray items) =>
            items.Select(item => item as string ?? string.Empty).ToArray();

        private static double[] NumbersOf(JsonArray items) =>
            items
                .Select(item =>
                    item is double real ? real
                    : item is long whole ? whole
                    : 0.0
                )
                .ToArray();
    }
}
