using System;
using System.Collections.Generic;
using GeometryHelper;
using GeometryHelper.Dxf;
using GeometryHelper.Geometry;

namespace OpenCAD2D
{
    /// <summary>
    /// The edits of <see cref="CadEditor"/>: <see cref="Move{T}"/>, <see cref="Copy{T}"/>, <see cref="Rotate{T}"/>,
    /// <see cref="Scale{T}"/>, <see cref="Mirror{T}"/>, <see cref="Offset"/> and <see cref="Erase"/>, each doing what the
    /// command of its name does to the same objects, through the code the command runs.
    /// </summary>
    /// <remarks>
    /// <para>
    /// An edit is given the drawing's own objects (what <see cref="CadDocument.FindEntity"/> gives) and is refused whole,
    /// nothing changed, with an <see cref="InvalidOperationException"/> that names the first object it will not take: one that
    /// is of another drawing or was read back by an undo or a redo (<c>The object 2F is not in the drawing Plan.dxf.</c>),
    /// erased, inside a block, on a locked layer (<c>The object 2F is on the locked layer Steel.</c>), or one whose owner does
    /// not allow the change (<c>This line belongs to the model and can only be moved.</c>). Where a command runs in the
    /// session, the edit joins its undo step; at rest each call is one step named as the command is (MOVE, COPY, ROTATE, SCALE,
    /// MIRROR, OFFSET, ERASE). Nothing is written on the command line, and the pickfirst selection stays as it was.
    /// </para>
    /// <para>
    /// Points and displacements are of the world, not of the UCS; angles are in radians, counter-clockwise, whatever ANGDIR
    /// and ANGBASE say. Whether a mirror line, an offset distance or a displacement is nought is the session's
    /// <see cref="CadCommandSession.Tolerance"/>, read at the call. An empty list is allowed: it gives an empty list and
    /// records nothing; an object given twice is taken once.
    /// </para>
    /// </remarks>
    public sealed partial class CadEditor
    {
        /// <summary>How many <see cref="Check"/> calls are running: while any is, an edit does everything but write.</summary>
        private int _checking;

        /// <summary>
        /// Checks an edit without making it: runs the call the delegate makes, with every check the call makes (its arguments,
        /// its objects, the plan of the edit and the owner's gate) and none of its writing, and says what refused it. A Move of
        /// a line on the locked layer Steel checks to <c>The object 2F is on the locked layer Steel.</c>; an Offset of a circle
        /// of radius 10 inward by 15 to <c>Cannot offset that object.</c>; a Copy of a line to null, and no copy is made.
        /// </summary>
        /// <remarks>
        /// <para>
        /// <c>editor.Check(e =&gt; e.Offset(curve, 5.0, side))</c> is checked as <c>editor.Offset(curve, 5.0, side)</c> is refused,
        /// so that a script that has other changes to make first can find out whether the edit will be refused before it makes
        /// them. Inside the delegate each edit of the editor (<see cref="Move{T}"/>, <see cref="Copy{T}"/>, <see cref="Rotate{T}"/>,
        /// <see cref="Scale{T}"/>, <see cref="Mirror{T}"/>, <see cref="Offset"/>, <see cref="Erase"/>) checks and gives back what the
        /// call would not (a Copy none, for it makes none); it opens no undo step and no group, writes nothing on the command
        /// line and leaves the pickfirst selection alone. The first edit that is refused ends the check; the edits after it in the delegate are not looked at.
        /// What the delegate does besides calling the edits (a zoom, a variable set) is done.
        /// </para>
        /// <para>
        /// The check is of the drawing as it stands: an edit that follows others the script made first is not looked at as it will
        /// be after them. One thing is left to the writing: an owner that takes an edit itself can still refuse it then (<c>The
        /// model owner did not take this edit.</c>), and nothing is changed by that either.
        /// </para>
        /// <para>A plugin that uses it states <c>[assembly: CadPluginApi("1.5")]</c>.</para>
        /// </remarks>
        /// <param name="edit">Makes the calls to check, on the editor it is given (this one).</param>
        /// <returns>Null where the edit would be made; else why it would be refused, as the message of the exception the call throws for a number that is no edit (<see cref="ArgumentException"/>) or for what the drawing refuses (<see cref="InvalidOperationException"/>); an <see cref="ArgumentException"/>'s message may carry a second line naming the parameter.</returns>
        /// <exception cref="ArgumentNullException">Thrown when the delegate is null.</exception>
        public string Check(Action<CadEditor> edit)
        {
            if (edit == null)
            {
                throw new ArgumentNullException(nameof(edit));
            }

            _checking++;

            try
            {
                edit(this);
                return null;
            }
            catch (Exception exception) when (exception is ArgumentException || exception is InvalidOperationException)
            {
                return exception.Message;
            }
            finally
            {
                _checking--;
            }
        }

        /// <summary>
        /// Moves objects by a displacement, as MOVE does: a line from (0, 0) to (100, 0) moved by (10, 5) runs from (10, 5) to
        /// (110, 5). One undo step named MOVE.
        /// </summary>
        /// <typeparam name="T">The type of the objects given.</typeparam>
        /// <param name="entities">The drawing's objects to move.</param>
        /// <param name="displacement">The displacement, in the world.</param>
        /// <returns>The objects given, moved where they stand, in the order given.</returns>
        /// <exception cref="ArgumentNullException">Thrown when the list is null.</exception>
        /// <exception cref="ArgumentException">Thrown when an object of the list is null, or the displacement is not finite.</exception>
        /// <exception cref="InvalidOperationException">Thrown when an object is refused, as the remarks say.</exception>
        public IReadOnlyList<T> Move<T>(IEnumerable<T> entities, GeoVector2 displacement)
            where T : DxfEntity
        {
            List<T> given = Given(entities, nameof(entities));
            List<string> handles = Checked(given);
            CadEditPlan plan = CadEdits.Move(handles, displacement);

            if (handles.Count > 0)
            {
                Make("MOVE", plan);
            }

            return given;
        }

        /// <summary>
        /// Copies objects by a displacement, as COPY does with a displacement typed: two lines copied by (0, 50) leave the
        /// lines and add two lines 50 above them, on the layers of the lines they copy. A dimension copied with the line it is
        /// tied to is tied to the copy of the line. One undo step named COPY.
        /// </summary>
        /// <typeparam name="T">The type of the objects given.</typeparam>
        /// <param name="entities">The drawing's objects to copy.</param>
        /// <param name="displacement">The displacement of the copies, in the world; (0, 0) makes copies on the sources.</param>
        /// <returns>The copies, in the order the objects were given, each of the type of its source.</returns>
        /// <exception cref="ArgumentNullException">Thrown when the list is null.</exception>
        /// <exception cref="ArgumentException">Thrown when an object of the list is null, or the displacement is not finite.</exception>
        /// <exception cref="InvalidOperationException">Thrown when an object is refused, as the remarks say.</exception>
        public IReadOnlyList<T> Copy<T>(IEnumerable<T> entities, GeoVector2 displacement)
            where T : DxfEntity
        {
            List<T> given = Given(entities, nameof(entities));
            List<string> handles = Checked(given);
            CadEditPlan plan = CadEdits.Copy(handles, displacement);

            if (handles.Count == 0)
            {
                return given;
            }

            Make("COPY", plan);
            return Found<T>(plan.Copies);
        }

        /// <summary>
        /// Turns objects about a base point, as ROTATE does: counter-clockwise, the angle taken from nought up to a whole turn
        /// first as ROTATE takes it (a turn of 2 pi is none, -pi / 2 is three quarters, which a hatch's pattern lines show). A
        /// line from (0, 0) to (100, 0) turned about (0, 0) by pi / 2 runs from (0, 0) to (0, 100). One undo step named
        /// ROTATE.
        /// </summary>
        /// <typeparam name="T">The type of the objects given.</typeparam>
        /// <param name="entities">The drawing's objects to turn.</param>
        /// <param name="basePoint">The point they turn about, in the world.</param>
        /// <param name="angle">The angle, in radians.</param>
        /// <returns>The objects given, turned where they stand, in the order given.</returns>
        /// <exception cref="ArgumentNullException">Thrown when the list is null.</exception>
        /// <exception cref="ArgumentException">Thrown when an object of the list is null, or the base point is not finite.</exception>
        /// <exception cref="ArgumentOutOfRangeException">Thrown when the angle is not a finite number.</exception>
        /// <exception cref="InvalidOperationException">Thrown when an object is refused, as the remarks say.</exception>
        public IReadOnlyList<T> Rotate<T>(IEnumerable<T> entities, GeoPoint2 basePoint, double angle)
            where T : DxfEntity
        {
            List<T> given = Given(entities, nameof(entities));
            List<string> handles = Checked(given);
            CadEditPlan plan = CadEdits.Rotate(handles, basePoint, angle);

            if (handles.Count > 0)
            {
                Make("ROTATE", plan);
            }

            return given;
        }

        /// <summary>
        /// Scales objects about a base point, as SCALE does: a circle of radius 10 about (200, 0) scaled about (0, 0) by 2 is
        /// one of radius 20 about (400, 0), and a text 5 high is 10 high. One undo step named SCALE.
        /// </summary>
        /// <typeparam name="T">The type of the objects given.</typeparam>
        /// <param name="entities">The drawing's objects to scale.</param>
        /// <param name="basePoint">The point that stays where it is, in the world.</param>
        /// <param name="factor">The factor, above nought.</param>
        /// <returns>The objects given, scaled where they stand, in the order given.</returns>
        /// <exception cref="ArgumentNullException">Thrown when the list is null.</exception>
        /// <exception cref="ArgumentException">Thrown when an object of the list is null, or the base point is not finite.</exception>
        /// <exception cref="ArgumentOutOfRangeException">Thrown when the factor is not a finite number above nought.</exception>
        /// <exception cref="InvalidOperationException">Thrown when an object is refused, as the remarks say.</exception>
        public IReadOnlyList<T> Scale<T>(IEnumerable<T> entities, GeoPoint2 basePoint, double factor)
            where T : DxfEntity
        {
            List<T> given = Given(entities, nameof(entities));
            List<string> handles = Checked(given);
            CadEditPlan plan = CadEdits.Scale(handles, basePoint, factor);

            if (handles.Count > 0)
            {
                Make("SCALE", plan);
            }

            return given;
        }

        /// <summary>
        /// Mirrors objects across the line through two points, as MIRROR does: a TEXT stays readable under MIRRTEXT 0 and is
        /// written backwards under 1, a hatch's pattern is kept under MIRRHATCH 0, a dimension stays readable. A line from
        /// (0, 0) to (0, 50) mirrored across the line (0, 0) to (10, 0) gives a line from (0, 0) to (0, -50). One undo step
        /// named MIRROR.
        /// </summary>
        /// <typeparam name="T">The type of the objects given.</typeparam>
        /// <param name="entities">The drawing's objects to mirror.</param>
        /// <param name="first">The first point of the mirror line, in the world.</param>
        /// <param name="second">The second point; the line has to be more than the session's <c>EqualPoint</c> long, and long enough to have a direction (about 2E-5).</param>
        /// <param name="eraseSource">Whether the objects are mirrored where they stand (MIRROR's "Erase source objects? Yes") rather than copied; false by default, as MIRROR's default.</param>
        /// <returns>With <paramref name="eraseSource"/> false the mirrored copies, in the order the objects were given, each of the type of its source; else the objects given.</returns>
        /// <exception cref="ArgumentNullException">Thrown when the list is null.</exception>
        /// <exception cref="ArgumentException">Thrown when an object of the list is null, or the two points are not a line.</exception>
        /// <exception cref="InvalidOperationException">Thrown when an object is refused, as the remarks say.</exception>
        public IReadOnlyList<T> Mirror<T>(
            IEnumerable<T> entities,
            GeoPoint2 first,
            GeoPoint2 second,
            bool eraseSource = false
        )
            where T : DxfEntity
        {
            List<T> given = Given(entities, nameof(entities));
            List<string> handles = Checked(given);
            CadEditPlan plan = CadEdits.Mirror(_session, handles, first, second, eraseSource);

            if (handles.Count == 0)
            {
                return given;
            }

            Make("MIRROR", plan);
            return eraseSource ? given : Found<T>(plan.Copies);
        }

        /// <summary>
        /// Offsets a curve to the side of a point, as one pick of OFFSET with a distance typed: a line from (0, 0) to (100, 0)
        /// offset by 5 toward (50, 10) gives a line from (0, 5) to (100, 5). The copies go on the layer of the curve, which is
        /// kept (OFFSET's defaults; its Erase, Layer and Through options are not taken). A line goes to the side of the point
        /// across it, an arc or a circle outward where the point is outside it, a polyline to the side of the point with its
        /// corners run on until they meet. One undo step named OFFSET.
        /// </summary>
        /// <param name="curve">The drawing's curve: a line, arc, circle, ellipse, polyline, spline, xline or ray.</param>
        /// <param name="distance">The distance, above the session's <c>EqualPoint</c>.</param>
        /// <param name="side">The point on the side to offset to, in the world, not on the curve.</param>
        /// <returns>The curves the offset added, which is one for a line, an arc, a circle or a polyline and may be more for a polyline whose offset falls apart.</returns>
        /// <exception cref="ArgumentNullException">Thrown when the curve is null.</exception>
        /// <exception cref="ArgumentException">Thrown when the curve is not one OFFSET takes (<c>Cannot offset that object.</c>), or the side point is not finite or lies on the curve.</exception>
        /// <exception cref="ArgumentOutOfRangeException">Thrown when the distance is not a finite number above <c>EqualPoint</c>.</exception>
        /// <exception cref="InvalidOperationException">Thrown when the curve is refused, as the remarks say, or the offset makes no curve (a circle of radius 10 offset inward by 15: <c>Cannot offset that object.</c>).</exception>
        public IReadOnlyList<DxfCurve> Offset(DxfCurve curve, double distance, GeoPoint2 side)
        {
            if (curve == null)
            {
                throw new ArgumentNullException(nameof(curve));
            }

            Checked(new List<DxfCurve> { curve });
            CadEditPlan plan = CadEdits.OffsetBy(_session, curve, distance, side);
            Make("OFFSET", plan);
            return Found<DxfCurve>(plan.Made);
        }

        /// <summary>
        /// Erases objects, as ERASE does: the drawing then has none of them, and an undo brings them back. One undo step named
        /// ERASE.
        /// </summary>
        /// <param name="entities">The drawing's objects to erase.</param>
        /// <exception cref="ArgumentNullException">Thrown when the list is null.</exception>
        /// <exception cref="ArgumentException">Thrown when an object of the list is null.</exception>
        /// <exception cref="InvalidOperationException">Thrown when an object is refused, as the remarks say.</exception>
        public void Erase(IEnumerable<DxfEntity> entities)
        {
            List<string> handles = Checked(Given(entities, nameof(entities)));

            if (handles.Count > 0)
            {
                Make("ERASE", CadEdits.Erase(handles));
            }
        }

        // ---- The checks and the writing ----

        /// <summary>The objects of a list as a list of its own, each looked at once.</summary>
        private static List<T> Given<T>(IEnumerable<T> entities, string name)
            where T : DxfEntity
        {
            if (entities == null)
            {
                throw new ArgumentNullException(name);
            }

            var given = new List<T>(entities);
            Guard.Argument(!given.Exists(e => ReferenceEquals(e, null)), name, "The list holds a null.");
            return given;
        }

        /// <summary>
        /// The handles of the objects, each once, in the order given, after every one is found to be the drawing's own, not
        /// erased, in a space and not on a locked layer; the first that is not is named in the exception.
        /// </summary>
        private List<string> Checked<T>(List<T> given)
            where T : DxfEntity
        {
            var handles = new List<string>(given.Count);
            var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

            foreach (T entity in given)
            {
                string refused = CadEdits.Unfit(Document, entity);

                if (refused != null)
                {
                    throw new InvalidOperationException(refused);
                }

                if (seen.Add(entity.Handle))
                {
                    handles.Add(entity.Handle);
                }
            }

            return handles;
        }

        /// <summary>Writes a plan as a step named <paramref name="name"/> or in the step of the command running, and says what refused it; in a <see cref="Check"/> only asks whether it would be.</summary>
        private CadEditPlan Make(string name, CadEditPlan plan)
        {
            if (_checking > 0)
            {
                // A check: the gate is asked of what the plan changes, and nothing is written, no step or group opened.
                string refused = Document.CheckEdit(plan.Planned);

                if (refused != null)
                {
                    throw new InvalidOperationException(refused);
                }

                return plan;
            }

            CadEditResult result = InStep(name, () => Document.ApplyEdit(name, plan.Planned, plan.Write));

            if (!result.Succeeded)
            {
                throw new InvalidOperationException(result.Message);
            }

            return plan;
        }

        /// <summary>The objects of the drawing the handles name, in their order, as the type the caller gave.</summary>
        private IReadOnlyList<T> Found<T>(IReadOnlyList<string> handles)
            where T : DxfEntity
        {
            var found = new List<T>(handles.Count);

            foreach (string handle in handles)
            {
                found.Add((T)Document.FindEntity(handle));
            }

            return found;
        }
    }
}
