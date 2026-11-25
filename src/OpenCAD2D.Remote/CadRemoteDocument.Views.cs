using System;
using System.Collections.Generic;
using GeometryHelper.Dxf;
using GeometryHelper.Geometry;

namespace OpenCAD2D.Remote
{
    /// <summary>The view, the named views, the system variables and the selection by area of a drawing.</summary>
    public sealed partial class CadRemoteDocument
    {
        /// <summary>
        /// Zooms the drawing to its extents, as ZOOM Extents does: what the space shown draws, fitted with a margin of 5 %. One
        /// undo step of the app, ZOOM. The view calls work on the drawing the app shows, which is not this one until
        /// <see cref="Activate"/> is called.
        /// </summary>
        /// <exception cref="CadRemoteException">Thrown when the drawing is closed or is not the drawing shown.</exception>
        public void ZoomExtents() => Ask(RemoteOps.Zoom, request => request.Set("how", "extents"));

        /// <summary>
        /// Zooms the drawing to a window, as ZOOM Window does: the window fitted with no margin. With the console view of the app
        /// with no window (942 by 534 pixels), (0, 0) to (10, 10) shows (-3.820, 0) to (13.820, 10).
        /// </summary>
        /// <param name="first">One corner.</param>
        /// <param name="second">The opposite corner.</param>
        /// <exception cref="CadRemoteException">Thrown when the drawing is closed or is not the drawing shown, or the window has no width or no height.</exception>
        public void ZoomWindow(GeoPoint2 first, GeoPoint2 second) =>
            Ask(RemoteOps.Zoom, request =>
                request.Set("how", "window").Set("first", Json(first)).Set("second", Json(second))
            );

        /// <summary>
        /// Zooms to objects, as ZOOM Object does: the box of the ones drawn in the space shown, fitted with a margin of 5 %;
        /// none drawn there leaves the view as it was.
        /// </summary>
        /// <param name="handles">The handles, or tickets of objects committed.</param>
        /// <exception cref="ArgumentNullException">Thrown when the handles are null.</exception>
        /// <exception cref="CadRemoteException">Thrown when the drawing is closed or is not the drawing shown, or a handle is the ticket of an object not committed yet.</exception>
        public void ZoomObjects(IEnumerable<string> handles)
        {
            if (handles == null)
            {
                throw new ArgumentNullException(nameof(handles));
            }

            var list = new JsonArray();

            foreach (string handle in handles)
            {
                list.Add(Resolve(handle));
            }

            Ask(RemoteOps.Zoom, request => request.Set("how", "objects").Set("handles", list));
        }

        /// <summary>Zooms to a height about a centre, as ZOOM Center does.</summary>
        /// <param name="center">The centre.</param>
        /// <param name="height">The height the view shows, above nought.</param>
        /// <exception cref="CadRemoteException">Thrown when the drawing is closed or is not the drawing shown, the height is not above nought, or a viewport is worked through.</exception>
        public void ZoomCenter(GeoPoint2 center, double height) =>
            Ask(RemoteOps.Zoom, request =>
                request.Set("how", "center").Set("center", Json(center)).Set("height", height)
            );

        /// <summary>Scales the view as it is about its centre, as ZOOM nX does: 2 shows half the width.</summary>
        /// <param name="factor">The factor, above nought.</param>
        /// <exception cref="CadRemoteException">Thrown when the drawing is closed or is not the drawing shown, or the factor is not above nought.</exception>
        public void ZoomScaled(double factor) =>
            Ask(RemoteOps.Zoom, request => request.Set("how", "scaled").Set("factor", factor));

        /// <summary>Goes back to the view before the last zoom or pan, as ZOOM Previous does.</summary>
        /// <returns>False where the app has none saved.</returns>
        /// <exception cref="CadRemoteException">Thrown when the drawing is closed or is not the drawing shown.</exception>
        public bool ZoomPrevious() => Ask(RemoteOps.Zoom, request => request.Set("how", "previous")).GetBool("moved");

        /// <summary>Pans the view so that the drawing moves by the displacement, as -PAN does.</summary>
        /// <param name="displacement">The displacement, in drawing units.</param>
        /// <exception cref="CadRemoteException">Thrown when the drawing is closed or is not the drawing shown.</exception>
        public void Pan(GeoVector2 displacement) =>
            Ask(RemoteOps.Pan, request => request.Set("dx", displacement.X).Set("dy", displacement.Y));

        /// <summary>Regenerates the drawing, as REGEN does.</summary>
        /// <exception cref="CadRemoteException">Thrown when the drawing is closed.</exception>
        public void Regen() => Ask(RemoteOps.Regen);

        /// <summary>
        /// Gets the part of the space shown that the canvas of the app shows, asked each time: the whole canvas, so wider or
        /// taller than a box given to <see cref="SetCurrentView"/> where the shape differs.
        /// </summary>
        /// <exception cref="CadRemoteException">Thrown when the drawing is closed or is not the drawing shown.</exception>
        public CadRemoteExtents CurrentView => ViewOf(Ask(RemoteOps.View));

        /// <summary>Sets the view to show a box, fitted with no margin as ZOOM Window fits one.</summary>
        /// <param name="first">One corner.</param>
        /// <param name="second">The opposite corner.</param>
        /// <exception cref="CadRemoteException">Thrown when the drawing is closed or is not the drawing shown, or the box has no width or no height.</exception>
        public void SetCurrentView(GeoPoint2 first, GeoPoint2 second) =>
            Ask(RemoteOps.View, request =>
                request
                    .Set("min", Json(new GeoPoint2(Math.Min(first.X, second.X), Math.Min(first.Y, second.Y))))
                    .Set("max", Json(new GeoPoint2(Math.Max(first.X, second.X), Math.Max(first.Y, second.Y))))
            );

        /// <summary>
        /// Gets whether the app shows the drawing in a window: false where the drawing's session has no view of the canvas, as
        /// in a test of the app with no window, where the view calls work on a view of 942 by 534 pixels showing 10 units of
        /// height about the origin. Asked each time.
        /// </summary>
        /// <exception cref="CadRemoteException">Thrown when the drawing is closed or is not the drawing shown.</exception>
        public bool HasWindow => Ask(RemoteOps.View).GetBool("window");

        /// <summary>Gets the names of the named views of the drawing, as -VIEW ? lists them: in ordinal order, letters of either case alike. Asked each time.</summary>
        /// <exception cref="CadRemoteException">Thrown when the drawing is closed.</exception>
        public IReadOnlyList<string> NamedViews
        {
            get
            {
                var names = new List<string>();

                foreach (object item in Ask(RemoteOps.NamedViews).GetArray("names"))
                {
                    names.Add(item as string ?? string.Empty);
                }

                return names;
            }
        }

        /// <summary>Saves the view on screen under a name, as -VIEW Save does; a name that is a view's already redefines it. One undo step of the app, VIEW.</summary>
        /// <param name="name">The name: not empty, 255 letters at most, no space at either end and none of &lt;&gt;/\":;?*|=`.</param>
        /// <exception cref="ArgumentNullException">Thrown when the name is null.</exception>
        /// <exception cref="CadRemoteException">Thrown when the drawing is closed or is not the drawing shown, or the name is not one a view can have.</exception>
        public void SaveView(string name) => NamedView(RemoteOps.SaveView, name);

        /// <summary>Shows a named view, as -VIEW Restore does.</summary>
        /// <param name="name">The name, letters of either case alike.</param>
        /// <exception cref="ArgumentNullException">Thrown when the name is null.</exception>
        /// <exception cref="CadRemoteException">Thrown when the drawing is closed or is not the drawing shown, or has no view of that name.</exception>
        public void RestoreView(string name) => NamedView(RemoteOps.RestoreView, name);

        /// <summary>Erases a named view, as -VIEW Delete does.</summary>
        /// <param name="name">The name, letters of either case alike.</param>
        /// <exception cref="ArgumentNullException">Thrown when the name is null.</exception>
        /// <exception cref="CadRemoteException">Thrown when the drawing is closed or has no view of that name.</exception>
        public void DeleteView(string name) => NamedView(RemoteOps.DeleteView, name);

        private void NamedView(string op, string name)
        {
            if (name == null)
            {
                throw new ArgumentNullException(nameof(name));
            }

            Ask(op, request => request.Set("name", name));
        }

        /// <summary>
        /// Gets the value of a system variable of the drawing as AutoLISP's <c>getvar</c> gives it: an <see cref="int"/> for a
        /// whole number or a switch, a <see cref="double"/> for a real (an angle in radians), a <see cref="string"/> for a text
        /// and a <c>double[]</c> of three coordinates for a point, two for a variable AutoCAD keeps as a 2D point.
        /// </summary>
        /// <param name="name">The name, letters of either case alike.</param>
        /// <returns>The value.</returns>
        /// <exception cref="ArgumentNullException">Thrown when the name is null.</exception>
        /// <exception cref="CadRemoteException">Thrown when the drawing is closed or the app has no variable of that name.</exception>
        public object GetVariable(string name) => Variable(name, null);

        /// <summary>Sets a system variable of the drawing as AutoLISP's <c>setvar</c> does; one the drawing keeps is an undo step of the app, SETVAR.</summary>
        /// <param name="name">The name, letters of either case alike.</param>
        /// <param name="value">An <see cref="int"/>, a <see cref="double"/>, a <see cref="string"/>, or a <c>double[]</c> of two or three coordinates.</param>
        /// <exception cref="ArgumentNullException">Thrown when the name or the value is null.</exception>
        /// <exception cref="ArgumentException">Thrown when the value is of another type.</exception>
        /// <exception cref="CadRemoteException">Thrown when the drawing is closed, the app has no variable of that name, it is read only, or it refuses the value.</exception>
        public void SetVariable(string name, object value)
        {
            if (value == null)
            {
                throw new ArgumentNullException(nameof(value));
            }

            Variable(name, value);
        }

        private object Variable(string name, object value)
        {
            if (name == null)
            {
                throw new ArgumentNullException(nameof(name));
            }

            object sent = null;

            if (value != null)
            {
                if (value is int whole)
                {
                    sent = (long)whole;
                }
                else if (value is double || value is string)
                {
                    sent = value;
                }
                else if (value is double[] point)
                {
                    var numbers = new JsonArray();

                    foreach (double coordinate in point)
                    {
                        numbers.Add(coordinate);
                    }

                    sent = numbers;
                }
                else
                {
                    throw new ArgumentException(
                        "A variable is set to an int, a double, a string or a double[]; not a "
                            + value.GetType().Name
                            + ".",
                        nameof(value)
                    );
                }
            }

            JsonObject answer = Ask(RemoteOps.Variable, request =>
            {
                request.Set("name", name);

                if (sent != null)
                {
                    request.Set("value", sent);
                }
            });
            object got = answer.Get("value");

            switch (answer.GetString("kind"))
            {
                case "integer":
                    return (int)answer.GetLong("value");
                case "real":
                    return answer.GetDouble("value");
                case "point":
                    var coordinates = new List<double>();

                    foreach (object item in (JsonArray)got)
                    {
                        coordinates.Add(
                            item is double real ? real
                            : item is long number ? number
                            : 0.0
                        );
                    }

                    return coordinates.ToArray();
                default:
                    return got as string ?? string.Empty;
            }
        }

        /// <summary>
        /// Takes every object of the space the drawing works in, as the All option of "Select objects:" does, the first viewport
        /// of a layout left out. The objects the user has selected stay selected: <see cref="Select"/> changes them.
        /// </summary>
        /// <returns>Copies of the objects as <see cref="GetObjects"/> gives them, in the order the space lists them.</returns>
        /// <exception cref="CadRemoteException">Thrown when the drawing is closed.</exception>
        public IReadOnlyList<DxfEntity> SelectAll() =>
            Objects(Ask(RemoteOps.SelectArea, request => request.Set("how", "all")));

        /// <summary>
        /// Takes the objects that lie wholly inside a window, as the Window option of "Select objects:" does: where the app has
        /// a window, only objects its view shows. The objects the user has selected stay selected.
        /// </summary>
        /// <param name="first">One corner.</param>
        /// <param name="second">The opposite corner.</param>
        /// <returns>Copies of the objects as <see cref="GetObjects"/> gives them.</returns>
        /// <exception cref="CadRemoteException">Thrown when the drawing is closed.</exception>
        public IReadOnlyList<DxfEntity> SelectWindow(GeoPoint2 first, GeoPoint2 second) =>
            Area("window", first, second);

        /// <summary>
        /// Takes the objects a window lies across or contains, as the Crossing option of "Select objects:" does, held to the
        /// view as <see cref="SelectWindow"/> is. The objects the user has selected stay selected.
        /// </summary>
        /// <param name="first">One corner.</param>
        /// <param name="second">The opposite corner.</param>
        /// <returns>Copies of the objects as <see cref="GetObjects"/> gives them.</returns>
        /// <exception cref="CadRemoteException">Thrown when the drawing is closed.</exception>
        public IReadOnlyList<DxfEntity> SelectCrossingWindow(GeoPoint2 first, GeoPoint2 second) =>
            Area("crossing", first, second);

        private IReadOnlyList<DxfEntity> Area(string how, GeoPoint2 first, GeoPoint2 second) =>
            Objects(
                Ask(RemoteOps.SelectArea, request =>
                    request.Set("how", how).Set("first", Json(first)).Set("second", Json(second))
                )
            );

        private static JsonObject Json(GeoPoint2 point) => new JsonObject().Set("x", point.X).Set("y", point.Y);

        private static CadRemoteExtents ViewOf(JsonObject answer)
        {
            JsonObject least = answer.GetObject("min");
            JsonObject greatest = answer.GetObject("max");
            return new CadRemoteExtents(
                new GeoPoint3(least.GetDouble("x"), least.GetDouble("y"), least.GetDouble("z")),
                new GeoPoint3(greatest.GetDouble("x"), greatest.GetDouble("y"), greatest.GetDouble("z"))
            );
        }
    }
}
