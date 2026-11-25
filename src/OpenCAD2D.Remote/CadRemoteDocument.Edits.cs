using System;
using System.Collections.Generic;
using GeometryHelper.Dxf;
using GeometryHelper.Geometry;

namespace OpenCAD2D.Remote
{
    /// <summary>
    /// The edits of a drawing as calls: move, copy, rotate, scale, mirror and offset objects as the app's own commands do.
    /// What is queued (<see cref="Insert"/>, <see cref="Modify"/>, <see cref="Delete(string)"/>, the layers) and the edit
    /// are one commit, so that the app applies them as one undo step, <c>REMOTE</c>, or as part of the step of the run
    /// where the drawing is worked in from a tool's run; a refusal anywhere applies nothing, queued changes included, and
    /// empties the queue.
    /// </summary>
    public sealed partial class CadRemoteDocument
    {
        /// <summary>
        /// Moves objects by a displacement, as MOVE does: a line from (0, 0) to (100, 0) moved by (10, 5) runs from (10, 5) to
        /// (110, 5) in the app. Sends the queued changes with it.
        /// </summary>
        /// <typeparam name="T">The type of the objects given.</typeparam>
        /// <param name="objects">Objects read from this drawing (<see cref="GetObject"/>, <see cref="GetObjects"/>, or what an edit gave back).</param>
        /// <param name="displacement">The displacement, in the world.</param>
        /// <returns>New copies of the objects as they are now, under the same handles, in the order given.</returns>
        /// <exception cref="ArgumentNullException">Thrown when the list is null.</exception>
        /// <exception cref="ArgumentException">Thrown when an object was not read from this drawing.</exception>
        /// <exception cref="ArgumentOutOfRangeException">Thrown when a number is not finite.</exception>
        /// <exception cref="CadRemoteException">Thrown when the app refuses: an object is gone, erased, inside a block, on a locked layer, or its owner does not allow the change. Nothing is applied.</exception>
        public IReadOnlyList<T> Move<T>(IEnumerable<T> objects, GeoVector2 displacement)
            where T : DxfEntity
        {
            Finite(nameof(displacement), displacement.X, displacement.Y);
            return Edit(objects, handles =>
                Transform("move", handles).Set("dx", displacement.X).Set("dy", displacement.Y)
            );
        }

        /// <summary>
        /// Copies objects by a displacement, as COPY does: two lines copied by (0, 50) add two lines 50 above them in the app.
        /// Sends the queued changes with it.
        /// </summary>
        /// <typeparam name="T">The type of the objects given.</typeparam>
        /// <param name="objects">Objects read from this drawing.</param>
        /// <param name="displacement">The displacement of the copies, in the world.</param>
        /// <returns>Copies of the new objects, each of the type of its source, each with a handle of its own (<see cref="HandleOf"/>), in the order given.</returns>
        /// <exception cref="ArgumentNullException">Thrown when the list is null.</exception>
        /// <exception cref="ArgumentException">Thrown when an object was not read from this drawing.</exception>
        /// <exception cref="ArgumentOutOfRangeException">Thrown when a number is not finite.</exception>
        /// <exception cref="CadRemoteException">Thrown when the app refuses, as <see cref="Move{T}"/> says. Nothing is applied.</exception>
        public IReadOnlyList<T> Copy<T>(IEnumerable<T> objects, GeoVector2 displacement)
            where T : DxfEntity
        {
            Finite(nameof(displacement), displacement.X, displacement.Y);
            return Edit(objects, handles =>
                Transform("copy", handles).Set("dx", displacement.X).Set("dy", displacement.Y)
            );
        }

        /// <summary>
        /// Turns objects about a base point, as ROTATE does: counter-clockwise, in radians, the angle taken from nought up to a
        /// whole turn as ROTATE takes it. Sends the queued changes with it.
        /// </summary>
        /// <typeparam name="T">The type of the objects given.</typeparam>
        /// <param name="objects">Objects read from this drawing.</param>
        /// <param name="basePoint">The point they turn about, in the world.</param>
        /// <param name="angle">The angle, in radians.</param>
        /// <returns>New copies of the objects as they are now, under the same handles, in the order given.</returns>
        /// <exception cref="ArgumentNullException">Thrown when the list is null.</exception>
        /// <exception cref="ArgumentException">Thrown when an object was not read from this drawing.</exception>
        /// <exception cref="ArgumentOutOfRangeException">Thrown when a number is not finite.</exception>
        /// <exception cref="CadRemoteException">Thrown when the app refuses, as <see cref="Move{T}"/> says. Nothing is applied.</exception>
        public IReadOnlyList<T> Rotate<T>(IEnumerable<T> objects, GeoPoint2 basePoint, double angle)
            where T : DxfEntity
        {
            Finite(nameof(basePoint), basePoint.X, basePoint.Y);
            Finite(nameof(angle), angle);
            return Edit(objects, handles =>
                Transform("rotate", handles).Set("x", basePoint.X).Set("y", basePoint.Y).Set("angle", angle)
            );
        }

        /// <summary>
        /// Scales objects about a base point, as SCALE does: a circle of radius 10 scaled by 2 is one of radius 20. Sends the
        /// queued changes with it.
        /// </summary>
        /// <typeparam name="T">The type of the objects given.</typeparam>
        /// <param name="objects">Objects read from this drawing.</param>
        /// <param name="basePoint">The point that stays where it is, in the world.</param>
        /// <param name="factor">The factor, above nought.</param>
        /// <returns>New copies of the objects as they are now, under the same handles, in the order given.</returns>
        /// <exception cref="ArgumentNullException">Thrown when the list is null.</exception>
        /// <exception cref="ArgumentException">Thrown when an object was not read from this drawing.</exception>
        /// <exception cref="ArgumentOutOfRangeException">Thrown when a number is not finite or the factor is not above nought.</exception>
        /// <exception cref="CadRemoteException">Thrown when the app refuses, as <see cref="Move{T}"/> says. Nothing is applied.</exception>
        public IReadOnlyList<T> Scale<T>(IEnumerable<T> objects, GeoPoint2 basePoint, double factor)
            where T : DxfEntity
        {
            Finite(nameof(basePoint), basePoint.X, basePoint.Y);
            Positive(nameof(factor), factor, "A scale factor is a finite number above nought.");
            return Edit(objects, handles =>
                Transform("scale", handles).Set("x", basePoint.X).Set("y", basePoint.Y).Set("factor", factor)
            );
        }

        /// <summary>
        /// Mirrors objects across the line through two points, as MIRROR does: a text stays readable under MIRRTEXT 0, a
        /// dimension too. Sends the queued changes with it.
        /// </summary>
        /// <typeparam name="T">The type of the objects given.</typeparam>
        /// <param name="objects">Objects read from this drawing.</param>
        /// <param name="first">The first point of the mirror line, in the world.</param>
        /// <param name="second">The second point; the app refuses a line no longer than its tolerance.</param>
        /// <param name="eraseSource">Whether the objects are mirrored where they stand rather than copied; false, as MIRROR's default.</param>
        /// <returns>With <paramref name="eraseSource"/> false copies of the new objects, in the order given; else new copies of the objects as they are now, under the same handles.</returns>
        /// <exception cref="ArgumentNullException">Thrown when the list is null.</exception>
        /// <exception cref="ArgumentException">Thrown when an object was not read from this drawing.</exception>
        /// <exception cref="ArgumentOutOfRangeException">Thrown when a number is not finite.</exception>
        /// <exception cref="CadRemoteException">Thrown when the app refuses, as <see cref="Move{T}"/> says, or the two points are not a line. Nothing is applied.</exception>
        public IReadOnlyList<T> Mirror<T>(
            IEnumerable<T> objects,
            GeoPoint2 first,
            GeoPoint2 second,
            bool eraseSource = false
        )
            where T : DxfEntity
        {
            Finite(nameof(first), first.X, first.Y);
            Finite(nameof(second), second.X, second.Y);
            return Edit(objects, handles =>
                Transform("mirror", handles)
                    .Set("x", first.X)
                    .Set("y", first.Y)
                    .Set("x2", second.X)
                    .Set("y2", second.Y)
                    .Set("eraseSource", eraseSource)
            );
        }

        /// <summary>
        /// Offsets a curve to the side of a point, as one pick of OFFSET with a distance typed: a line from (0, 0) to (100, 0)
        /// offset by 5 toward (50, 10) adds a line from (0, 5) to (100, 5) on the layer of the curve, which is kept. Sends the
        /// queued changes with it.
        /// </summary>
        /// <param name="curve">A curve read from this drawing: a line, arc, circle, ellipse, polyline, spline, xline or ray.</param>
        /// <param name="distance">The distance, above nought (the app refuses one the tolerance calls nought).</param>
        /// <param name="side">The point on the side to offset to, in the world, not on the curve.</param>
        /// <returns>Copies of the curves the offset added, each with a handle of its own.</returns>
        /// <exception cref="ArgumentNullException">Thrown when the curve is null.</exception>
        /// <exception cref="ArgumentException">Thrown when the curve was not read from this drawing.</exception>
        /// <exception cref="ArgumentOutOfRangeException">Thrown when a number is not finite or the distance is not above nought.</exception>
        /// <exception cref="CadRemoteException">Thrown when the app refuses, as <see cref="Move{T}"/> says, or the curve is not one OFFSET takes, the side is on the curve, or the offset makes no curve. Nothing is applied.</exception>
        public IReadOnlyList<DxfCurve> Offset(DxfCurve curve, double distance, GeoPoint2 side)
        {
            if (curve == null)
            {
                throw new ArgumentNullException(nameof(curve));
            }

            Positive(nameof(distance), distance, "An offset distance is a finite number above nought.");
            Finite(nameof(side), side.X, side.Y);
            return Edit(new[] { curve }, handles =>
                new JsonObject()
                    .Set("op", "offset")
                    .Set("handle", handles[0])
                    .Set("distance", distance)
                    .Set("x", side.X)
                    .Set("y", side.Y)
            );
        }

        private static JsonObject Transform(string how, JsonArray handles) =>
            new JsonObject().Set("op", "transform").Set("how", how).Set("handles", handles);

        /// <summary>Refuses a number that is not finite, which the request cannot carry.</summary>
        private static void Finite(string name, params double[] numbers)
        {
            foreach (double number in numbers)
            {
                if (!(Math.Abs(number) <= double.MaxValue))
                {
                    throw new ArgumentOutOfRangeException(name, number, "A number of an edit is finite.");
                }
            }
        }

        /// <summary>Refuses a number that is not finite and above nought.</summary>
        private static void Positive(string name, double number, string message)
        {
            if (!(number > 0.0 && number <= double.MaxValue))
            {
                throw new ArgumentOutOfRangeException(name, number, message);
            }
        }

        /// <summary>
        /// Sends the queue and an edit operation as one commit and reads the objects it gave back. None given is no commit,
        /// and the queue stays.
        /// </summary>
        /// <param name="objects">The objects, which this drawing gave.</param>
        /// <param name="operation">Makes the operation of the edit from the handles of the objects.</param>
        private IReadOnlyList<T> Edit<T>(IEnumerable<T> objects, Func<JsonArray, JsonObject> operation)
            where T : DxfEntity
        {
            if (objects == null)
            {
                throw new ArgumentNullException(nameof(objects));
            }

            var handles = new JsonArray();

            foreach (T entity in objects)
            {
                string handle = HandleOf(entity ?? throw new ArgumentNullException(nameof(objects)));

                if (handle == null)
                {
                    throw new ArgumentException(
                        "An edit takes objects read from this drawing with GetObject or GetObjects, or given back by an edit.",
                        nameof(objects)
                    );
                }

                handles.Add(handle);
            }

            var results = new List<T>();

            if (handles.Count == 0)
            {
                return results;
            }

            JsonObject answer = Send(operation(handles), new List<string>());

            foreach (DxfEntity made in Objects(answer.GetObject("objects") ?? new JsonObject()))
            {
                results.Add((T)made);
            }

            return results;
        }
    }
}
