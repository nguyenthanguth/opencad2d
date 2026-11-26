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
            CadBoundsTable table = request.Get("handles") is JsonArray handles
                ? CadSpatial.Bounds(document, Strings(handles))
                : CadSpatial.Bounds(document, SpaceNamed(document, request.GetString("layout")));
            return new JsonObject().Set("handles", Strings(table.Handles)).Set("boxes", Numbers(table.Boxes));
        }

        private static JsonObject Overlaps(CadDocument document, JsonObject request)
        {
            var options = new CadOverlapOptions
            {
                Words = request.Get("words") is JsonArray words ? Strings(words).ToList() : null,
                Over = request.Get("over") is JsonArray over ? Strings(over).ToList() : null,
                Handles = request.Get("handles") is JsonArray handles ? Strings(handles).ToList() : null,
            };
            Tolerance tolerance =
                request.Get("tolerance") == null
                    ? Touching
                    : new Tolerance(request.GetDouble("tolerance"), Tolerance.DefaultEqualVector);
            CadOverlapTable table = CadSpatial.Overlaps(
                document,
                SpaceNamed(document, request.GetString("layout")),
                options,
                tolerance
            );
            return new JsonObject()
                .Set("words", Strings(table.Words))
                .Set("others", Strings(table.Others))
                .Set("boxes", Numbers(table.Boxes));
        }

        private static IEnumerable<string> Strings(JsonArray items) =>
            items.Select(item => item as string ?? string.Empty);

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
