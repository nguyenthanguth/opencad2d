using System;
using System.Collections.Generic;
using GeometryHelper.Geometry;

namespace OpenCAD2D.Remote
{
    /// <summary>The Properties palette, the entities in an area of the drawing, and the words of LIST, as the app has them.</summary>
    public sealed partial class CadRemoteDocument
    {
        /// <summary>
        /// Asks the app what its Properties palette shows for some entities, as it would with them selected: for one line from (0, 0)
        /// to (300, 400) the label <c>Line (1)</c> and, in the group Geometry, <c>Length</c> <c>500.0000</c>. Several entities of one
        /// kind show the rows they share, a value that differs as <c>*Varies*</c>. The selection of the drawing is not changed.
        /// </summary>
        /// <param name="handles">The handles of the entities; one the drawing has not is passed over.</param>
        /// <returns>The label and the groups.</returns>
        /// <exception cref="ArgumentNullException">Thrown when the handles are null.</exception>
        /// <exception cref="CadRemoteException">Thrown when the drawing is closed.</exception>
        public CadRemoteProperties GetProperties(IEnumerable<string> handles)
        {
            if (handles == null)
            {
                throw new ArgumentNullException(nameof(handles));
            }

            JsonObject answer = Ask(RemoteOps.Properties, request => request.Set("handles", Strings(handles)));
            var groups = new List<CadRemotePropertyGroup>();

            foreach (object item in answer.GetArray("groups"))
            {
                var group = (JsonObject)item;
                var rows = new List<CadRemotePropertyRow>();

                foreach (object row in group.GetArray("rows"))
                {
                    var entry = (JsonObject)row;
                    rows.Add(
                        new CadRemotePropertyRow(
                            entry.GetString("label") ?? string.Empty,
                            entry.GetString("value") ?? string.Empty,
                            entry.GetBool("readOnly")
                        )
                    );
                }

                groups.Add(new CadRemotePropertyGroup(group.GetString("title") ?? string.Empty, rows));
            }

            return new CadRemoteProperties(answer.GetString("label") ?? string.Empty, groups);
        }

        /// <summary>
        /// Asks the app for the handles of the entities of a space that lie in an area, whole: unlike <see cref="SelectWindow"/> it is
        /// not held to the view of the window, so a drawing gives the same handles with a window or without. A box from (0, 0) to
        /// (100, 50) in <c>window</c> takes a line from (10, 10) to (20, 10) and leaves a circle of radius 30 about (50, 25), which
        /// <c>crossing</c> takes. The selection of the drawing is not changed.
        /// </summary>
        /// <param name="layout">The name of a layout, or null or empty for model space.</param>
        /// <param name="how"><c>window</c> or <c>crossing</c> (two corners), <c>fence</c> (two points or more), <c>wpolygon</c> or <c>cpolygon</c> (three or more), or <c>point</c> (one point and a tolerance).</param>
        /// <param name="points">The points.</param>
        /// <param name="tolerance">For <c>point</c>, how far from the point an entity may pass, above nought; 0 for the others.</param>
        /// <returns>The handles, in the order the space draws its entities; for a point, the one picked first leading.</returns>
        /// <exception cref="ArgumentNullException">Thrown when the selection or the points are null.</exception>
        /// <exception cref="CadRemoteException">Thrown when the drawing is closed, has no layout of that name, or the points do not fit the selection.</exception>
        public IReadOnlyList<string> GetHandlesIn(
            string layout,
            string how,
            IEnumerable<GeoPoint2> points,
            double tolerance
        )
        {
            if (how == null)
            {
                throw new ArgumentNullException(nameof(how));
            }

            if (points == null)
            {
                throw new ArgumentNullException(nameof(points));
            }

            var corners = new JsonArray();

            foreach (GeoPoint2 point in points)
            {
                corners.Add(Json(point));
            }

            JsonObject answer = Ask(RemoteOps.SelectGeometry, request =>
                request.Set("layout", layout).Set("how", how).Set("points", corners).Set("tolerance", tolerance)
            );
            return StringsOf(answer.GetArray("handles"));
        }

        /// <summary>
        /// Asks the app for the lines AutoCAD's LIST writes for some entities: the kind and layer, the space, the handle and the shape
        /// of each (the length and angle of a line, the centre and radius of a circle). The app runs LIST in a command session of its
        /// own, so its command line, its selection and its undo list are as they were.
        /// </summary>
        /// <param name="handles">The handles of the entities.</param>
        /// <returns>The lines.</returns>
        /// <exception cref="ArgumentNullException">Thrown when the handles are null.</exception>
        /// <exception cref="CadRemoteException">Thrown when the drawing is closed or has no entity of a handle.</exception>
        public IReadOnlyList<string> ListObjects(IEnumerable<string> handles)
        {
            if (handles == null)
            {
                throw new ArgumentNullException(nameof(handles));
            }

            return StringsOf(
                Ask(RemoteOps.List, request => request.Set("handles", Strings(handles))).GetArray("lines")
            );
        }
    }
}
