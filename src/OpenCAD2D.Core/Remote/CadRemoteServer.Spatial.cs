using System.Collections.Generic;
using System.Linq;
using GeometryHelper;
using OpenCAD2D.Remote;

namespace OpenCAD2D
{
    /// <summary>The boxes of the entities as they are drawn and the overlaps of their words, asked from outside.</summary>
    public sealed partial class CadRemoteServer
    {
        // The tolerance of an overlap sent with none: a thousandth of a millimetre, the library's own.
        private static readonly Tolerance Touching = new Tolerance(
            Tolerance.DefaultEqualPoint,
            Tolerance.DefaultEqualVector
        );

        private static JsonObject Bounds(CadDocument document, JsonObject request)
        {
            List<string> handles = NamesMember(request, "handles");
            string layout = TextMember(request, "layout");
            CadBoundsTable table =
                handles != null
                    ? CadSpatial.Bounds(document, handles)
                    : CadSpatial.Bounds(document, SpaceNamed(document, layout));
            return new JsonObject().Set("handles", Strings(table.Handles)).Set("boxes", Numbers(table.Boxes));
        }

        private static JsonObject Overlaps(CadDocument document, JsonObject request)
        {
            var options = new CadOverlapOptions
            {
                Words = NamesMember(request, "words"),
                Over = NamesMember(request, "over"),
                Handles = NamesMember(request, "handles"),
            };
            Tolerance tolerance = ToleranceOf(request);
            string layout = TextMember(request, "layout");
            CadOverlapTable table = CadSpatial.Overlaps(document, SpaceNamed(document, layout), options, tolerance);
            return new JsonObject()
                .Set("words", Strings(table.Words))
                .Set("others", Strings(table.Others))
                .Set("boxes", Numbers(table.Boxes));
        }

        private static JsonArray Strings(string[] items) => new JsonArray(items.Cast<object>());

        private static JsonArray Numbers(double[] items)
        {
            var numbers = new JsonArray();

            foreach (double item in items)
            {
                numbers.Add(item);
            }

            return numbers;
        }
    }
}
