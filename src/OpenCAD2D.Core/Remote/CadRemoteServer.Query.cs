using System;
using System.Collections.Generic;
using System.Linq;
using GeometryHelper.Geometry;
using OpenCAD2D.Remote;

namespace OpenCAD2D
{
    /// <summary>The Properties palette, the selection by where things are drawn, and LIST, asked from outside.</summary>
    public sealed partial class CadRemoteServer
    {
        /// <summary>Answers a <c>properties</c>: what the Properties palette shows for the entities of the handles, as <see cref="CadProperties.Describe(CadDocument, CadSpace, IReadOnlyCollection{string})"/> gives it.</summary>
        private static JsonObject PropertiesOf(CadDocument document, JsonObject request)
        {
            List<string> handles = Strings(request.GetArray("handles")).ToList();
            CadSelectionInfo info = CadProperties.Describe(document, null, handles);
            var groups = new JsonArray();

            foreach (CadPropertyGroup group in info.Groups)
            {
                var rows = new JsonArray();

                foreach (CadPropertyRow row in group.Rows)
                {
                    rows.Add(
                        new JsonObject()
                            .Set("label", row.Label)
                            .Set("value", row.Value ?? string.Empty)
                            .Set("readOnly", row.IsReadOnly)
                    );
                }

                groups.Add(new JsonObject().Set("title", group.Title).Set("rows", rows));
            }

            return new JsonObject().Set("label", info.Label).Set("groups", groups);
        }

        /// <summary>Answers a <c>selectGeometry</c>: the handles a window, a crossing window, a fence, a polygon or a point takes in a space, whole and not held to the view.</summary>
        private static JsonObject SelectGeometry(CadDocument document, JsonObject request)
        {
            string how = request.GetString("how") ?? string.Empty;
            CadAreaSelection selection;

            switch (how)
            {
                case "window":
                    selection = CadAreaSelection.Window;
                    break;
                case "crossing":
                    selection = CadAreaSelection.Crossing;
                    break;
                case "fence":
                    selection = CadAreaSelection.Fence;
                    break;
                case "wpolygon":
                    selection = CadAreaSelection.WindowPolygon;
                    break;
                case "cpolygon":
                    selection = CadAreaSelection.CrossingPolygon;
                    break;
                case "point":
                    selection = CadAreaSelection.Point;
                    break;
                default:
                    throw new Refusal(
                        "OpenCAD2D does not know the selection "
                            + how
                            + ": it is window, crossing, fence, wpolygon, cpolygon or point."
                    );
            }

            var points = new List<GeoPoint2>();

            foreach (object item in request.GetArray("points"))
            {
                var at = (JsonObject)item;
                points.Add(new GeoPoint2(at.GetDouble("x"), at.GetDouble("y")));
            }

            IReadOnlyList<string> handles = Edit(() =>
                CadSpatial.Select(
                    document,
                    SpaceNamed(document, request.GetString("layout")),
                    selection,
                    points,
                    request.Get("tolerance") == null ? 0.0 : request.GetDouble("tolerance")
                )
            );
            return new JsonObject().Set("handles", Strings(handles.ToArray()));
        }

        /// <summary>Answers a <c>list</c>: the lines AutoCAD's LIST writes for the entities of the handles, run in a command session of its own.</summary>
        private static JsonObject ListOf(CadDocument document, JsonObject request)
        {
            List<string> handles = Strings(request.GetArray("handles")).ToList();
            IReadOnlyList<string> lines = Edit(() => CadInquiry.List(document, handles));
            return new JsonObject().Set("lines", Strings(lines.ToArray()));
        }
    }
}
