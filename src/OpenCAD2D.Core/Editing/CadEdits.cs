using System;
using System.Collections.Generic;
using GeometryHelper;
using GeometryHelper.Dxf;
using GeometryHelper.Geometry;

namespace OpenCAD2D
{
    /// <summary>
    /// An edit worked out and not yet made: what it changes of the entities there are (<see cref="Planned"/>, which the edit
    /// gate is asked about), and the writing of it (<see cref="Write"/>). The typed commands MOVE, COPY, ROTATE, SCALE, MIRROR,
    /// ERASE and OFFSET, <see cref="CadEditor"/> and a Remote commit each make the plan with <see cref="CadEdits"/> and hand it
    /// to <see cref="CadDocument.ApplyEdit"/>, so that one code writes the drawing whoever asks.
    /// </summary>
    internal sealed class CadEditPlan
    {
        private readonly IReadOnlyList<string> _changed;

        internal CadEditPlan(
            IReadOnlyList<CadEditRecord> planned,
            Action<CadEditScope> write,
            IReadOnlyList<string> changed = null
        )
        {
            Planned = planned;
            Write = write;
            _changed = changed;
        }

        /// <summary>The changes planned to entities that exist, for the gate; empty for an edit that only adds.</summary>
        public IReadOnlyList<CadEditRecord> Planned { get; }

        /// <summary>Writes the change. It may run more than once (an owner measures a move first); <see cref="Made"/> and <see cref="Copies"/> are those of the last run.</summary>
        public Action<CadEditScope> Write { get; }

        /// <summary>The handles of everything <see cref="Write"/> added, in the order it added them.</summary>
        public List<string> Made { get; } = new List<string>();

        /// <summary>The handle of the copy of each entity given, in the order given; filled by a plan that copies.</summary>
        public List<string> Copies { get; } = new List<string>();

        /// <summary>
        /// The handles of what the edit gives back, once written: the entities it changed where it changes them in place
        /// (a move, a turn, a scale, a mirror that erases the source), else the copies it made, else the curves an offset
        /// added.
        /// </summary>
        public IReadOnlyList<string> Result => _changed ?? (Copies.Count > 0 ? (IReadOnlyList<string>)Copies : Made);
    }

    /// <summary>
    /// The plans of the edits MOVE, COPY, ROTATE, SCALE, MIRROR, ERASE and OFFSET write, which the commands and
    /// <see cref="CadEditor"/> both call (the command asks its prompts, the call checks its arguments; the writing is here
    /// once).
    /// </summary>
    internal static class CadEdits
    {
        /// <summary>
        /// Plans moving, turning, scaling or mirroring entities, or copies of them. A mirror that keeps texts readable gives
        /// each its own move (<paramref name="readable"/>), worked out before it moves.
        /// </summary>
        /// <param name="handles">The handles of the entities, each once.</param>
        /// <param name="transform">The transformation.</param>
        /// <param name="copy">Whether to make copies and leave the entities.</param>
        /// <param name="how">The kind of change the owner of an entity is told: Move, Rotate, Scale or Mirror.</param>
        /// <param name="value">The numbers of the change as the owner contract writes them, or null.</param>
        /// <param name="readable">How a mirror moves one entity so that it reads, or null.</param>
        /// <returns>The plan.</returns>
        public static CadEditPlan Transform(
            IReadOnlyList<string> handles,
            GeoTransform3 transform,
            bool copy,
            string how = "Move",
            string value = null,
            Func<DxfEntity, CadCommands.ReadableMirror> readable = null
        )
        {
            var planned = new List<CadEditRecord>();

            if (!copy)
            {
                foreach (string handle in handles)
                {
                    planned.Add(new CadEditRecord(CadEditRecordKind.Transformed, handle, how, null, value));
                }
            }

            CadEditPlan plan = null;
            plan = new CadEditPlan(
                planned,
                s =>
                {
                    plan.Made.Clear();
                    plan.Copies.Clear();
                    int before = s.Records.Count;
                    var copies = copy ? new Dictionary<string, DxfEntity>(StringComparer.OrdinalIgnoreCase) : null;
                    var together = new HashSet<string>(handles, StringComparer.OrdinalIgnoreCase);
                    var clippedViewports = new List<DxfViewport>();
                    Dictionary<string, CadCommands.ReadableMirror> readAhead = ReadAhead(s, handles, copy, readable);

                    foreach (string handle in handles)
                    {
                        if (!copy && s.IsHandled(handle))
                        {
                            continue;
                        }

                        DxfEntity entity = s.Document.FindEntity(handle);
                        CadCommands.ReadableMirror kept =
                            readAhead != null && readAhead.TryGetValue(handle, out CadCommands.ReadableMirror ahead)
                                ? ahead
                                : readable?.Invoke(entity);

                        if (
                            entity is DxfViewport plain
                            && plain.NonRectClipEntity == null
                            && (how == "Rotate" || how == "Mirror" || how == "Scale")
                        )
                        {
                            // A viewport that is not clipped cannot be turned or mirrored itself: a mirror moves its centre, a quarter
                            // turn turns it, any other turn makes it clipped; a scale scales its frame and keeps its view's scale, the
                            // model standing still on the sheet (CadViewportClips.TurnPlain).
                            DxfViewport turned = plain;

                            if (copy)
                            {
                                turned = (DxfViewport)CadCommands.AppendCopy(s, handle, GeoTransform3.Identity);
                                copies[handle] = turned;
                            }

                            CadViewportClips.TurnPlain(s, turned, transform, how);
                            continue;
                        }

                        if (copy)
                        {
                            // A clipped viewport copied with its boundary is not moved itself: it is tied to the copy of the boundary and
                            // fitted to it afterwards, as a turn or a mirror cannot move a viewport.
                            bool paired = entity is DxfViewport shown && CadViewportClips.IsPaired(shown, together);
                            DxfEntity copied;

                            using (Mirroring(kept))
                            {
                                copied = CadCommands.AppendCopy(
                                    s,
                                    handle,
                                    paired ? GeoTransform3.Identity : kept?.Transform ?? transform
                                );
                            }

                            kept?.Finish(copied);
                            copies[handle] = copied;
                        }
                        else if (entity is DxfViewport clipped && clipped.NonRectClipEntity != null)
                        {
                            // A clipped viewport takes its frame from its boundary, which moves with it: the edit refits it.
                            if (CadViewportClips.IsPaired(clipped, together))
                            {
                                clippedViewports.Add(clipped);
                            }

                            continue;
                        }
                        else
                        {
                            if (entity is DxfOle2Frame ole)
                            {
                                CadOleFrames.Transform(ole, kept?.Transform ?? transform);
                            }
                            else
                            {
                                using (Mirroring(kept))
                                {
                                    entity.TransformBy(kept?.Transform ?? transform);
                                }

                                CadCommands.CarryManualBreaks(s.Document, entity, kept?.Transform ?? transform);
                            }

                            kept?.Finish(entity);
                        }
                    }

                    foreach (DxfViewport clipped in clippedViewports)
                    {
                        CadViewportClips.Refit(clipped, transform, how);
                    }

                    if (copy)
                    {
                        CadDimAssociation.TieCopies(s, copies);
                        CadViewportClips.TieCopies(s, copies, transform, how);

                        foreach (string handle in handles)
                        {
                            if (copies.TryGetValue(handle, out DxfEntity made))
                            {
                                plan.Copies.Add(made.Handle);
                            }
                        }
                    }

                    for (int i = before; i < s.Records.Count; i++)
                    {
                        if (s.Records[i].Kind == CadEditRecordKind.Added)
                        {
                            plan.Made.Add(s.Records[i].Handle);
                        }
                    }
                },
                copy ? null : handles
            );
            return plan;
        }

        /// <summary>
        /// How a mirror moves each text and INSERT of the selection, worked out before anything moves, in one scope of the
        /// document's fonts rather than one a text: each scope reads the font file again, so that MIRROR of 2000 texts in
        /// opencad2d.shx took 340 ms and not 95. The fonts are not left set while the copies are written, which would put a
        /// copy's words where they stand as it is appended. Null where nothing is read ahead: no way to move them, or no text
        /// or INSERT.
        /// </summary>
        private static Dictionary<string, CadCommands.ReadableMirror> ReadAhead(
            CadEditScope s,
            IReadOnlyList<string> handles,
            bool copy,
            Func<DxfEntity, CadCommands.ReadableMirror> readable
        )
        {
            if (readable == null)
            {
                return null;
            }

            Dictionary<string, CadCommands.ReadableMirror> ahead = null;
            IDisposable measuring = null;

            try
            {
                foreach (string handle in handles)
                {
                    if (copy || !s.IsHandled(handle))
                    {
                        DxfEntity entity = s.Document.FindEntity(handle);

                        if (entity is DxfDBText || entity is DxfBlockReference)
                        {
                            measuring = measuring ?? new Lisp.LispActiveX.MeasuringScope(s.Document);
                            ahead = ahead
                                ?? new Dictionary<string, CadCommands.ReadableMirror>(StringComparer.OrdinalIgnoreCase);
                            ahead[handle] = readable(entity);
                        }
                    }
                }
            }
            finally
            {
                measuring?.Dispose();
            }

            return ahead;
        }

        /// <summary>The scope in which a hatch mirrored reflects the lines of its pattern, for an object MIRROR moves under MIRRHATCH 1; none otherwise.</summary>
        private static IDisposable Mirroring(CadCommands.ReadableMirror kept) =>
            kept != null && kept.MirrorsPattern ? new DxfHatch.PatternMirroring() : null;

        /// <summary>Plans erasing entities.</summary>
        /// <param name="handles">The handles of the entities.</param>
        /// <returns>The plan.</returns>
        public static CadEditPlan Erase(IReadOnlyList<string> handles)
        {
            var planned = new List<CadEditRecord>();

            foreach (string handle in handles)
            {
                planned.Add(new CadEditRecord(CadEditRecordKind.Erased, handle));
            }

            return new CadEditPlan(planned, s =>
            {
                foreach (string handle in handles)
                {
                    if (!s.IsHandled(handle))
                    {
                        s.Document.FindEntity(handle).Erase();
                    }
                }
            });
        }

        /// <summary>
        /// Plans one pick of OFFSET: the curve offset to the side of a point (or through it where the distance is below
        /// nought), the copies added to the space of the curve and the curve erased where asked.
        /// </summary>
        /// <param name="session">The session whose tolerance and drawing it is.</param>
        /// <param name="handle">The handle of the curve.</param>
        /// <param name="side">The point on the side to offset to.</param>
        /// <param name="distance">The distance, or below nought for through the point.</param>
        /// <param name="erase">Whether the curve is erased.</param>
        /// <param name="layer">The layer of the copies, or null for the layer of the curve.</param>
        /// <returns>The plan; null where the offset makes no curve.</returns>
        public static CadEditPlan Offset(
            CadCommandSession session,
            string handle,
            GeoPoint2 side,
            double distance,
            bool erase,
            string layer
        )
        {
            IReadOnlyList<DxfCurve> made = CadCommands.OffsetCurves(session, handle, side, distance);

            if (made == null || made.Count == 0)
            {
                return null;
            }

            IReadOnlyList<CadEditRecord> planned = erase
                ? new[] { new CadEditRecord(CadEditRecordKind.Erased, handle) }
                : Array.Empty<CadEditRecord>();
            CadEditPlan plan = null;
            plan = new CadEditPlan(planned, s =>
            {
                plan.Made.Clear();
                CadSpace space = s.Document.FindSpace(handle);

                foreach (DxfCurve copy in CadCommands.OffsetCopies(made, layer))
                {
                    s.Append(space, copy);
                    plan.Made.Add(copy.Handle);
                }

                if (erase && !s.IsHandled(handle))
                {
                    s.Document.FindEntity(handle).Erase();
                }
            });
            return plan;
        }

        /// <summary>Whether OFFSET takes an entity: a line, arc, circle, ellipse, polyline, spline, xline or ray.</summary>
        /// <param name="entity">The entity.</param>
        /// <returns>True when it does.</returns>
        public static bool CanOffset(DxfEntity entity) =>
            entity is DxfLine
            || entity is DxfArc
            || entity is DxfCircle
            || entity is DxfEllipse
            || entity is DxfPolyline
            || entity is DxfSpline
            || entity is DxfXline
            || entity is DxfRay;

        /// <summary>
        /// The turn of ROTATE about a base point: the angle taken from nought up to a whole turn first, as AutoCAD 2021 takes it
        /// (a turn of 360 degrees is none, -90 is 270), which it writes into a hatch's pattern lines as it does.
        /// </summary>
        /// <param name="center">The base point.</param>
        /// <param name="angle">The angle in radians, counter-clockwise.</param>
        /// <returns>The transformation.</returns>
        public static GeoTransform3 Rotation(GeoPoint2 center, double angle)
        {
            double turn = angle % (2.0 * Math.PI);
            return GeoTransform3.RotationAxis(
                CadCommands.P3(center),
                new GeoVector3(0.0, 0.0, 1.0),
                turn < 0.0 ? turn + 2.0 * Math.PI : turn
            );
        }

        /// <summary>
        /// The reflection across the line through two points, as MIRROR makes it; null where the points are not a line: not
        /// more than <c>EqualPoint</c> apart, or so near that the library cannot give the line a direction (under about 2E-5).
        /// </summary>
        /// <param name="first">The first point of the line.</param>
        /// <param name="second">The second point.</param>
        /// <param name="tolerance">The session's tolerance.</param>
        /// <returns>The transformation, or null.</returns>
        public static GeoTransform3 MirrorAcross(GeoPoint2 first, GeoPoint2 second, Tolerance tolerance)
        {
            GeoVector2 way = second - first;

            if (!(way.Length > tolerance.EqualPoint))
            {
                return null;
            }

            return new GeoVector3(-way.Y, way.X, 0.0).TryGetNormal(out GeoVector3 normal)
                ? GeoTransform3.Mirror(new GeoPlane3(CadCommands.P3(first), normal))
                : null;
        }

        // ---- What a call makes of its arguments: the checks, then the plan; the editor and a Remote commit both ask these ----

        /// <summary>Plans MOVE by a displacement; entities are moved where they stand.</summary>
        /// <param name="handles">The handles of the entities, each once.</param>
        /// <param name="displacement">The displacement, in the world.</param>
        /// <returns>The plan.</returns>
        /// <exception cref="ArgumentException">Thrown when the displacement is not finite.</exception>
        public static CadEditPlan Move(IReadOnlyList<string> handles, GeoVector2 displacement)
        {
            Guard.Argument(IsFinite(displacement), nameof(displacement), "The displacement is not finite.");
            return Transform(
                handles,
                GeoTransform3.Translation(new GeoVector3(displacement.X, displacement.Y, 0.0)),
                false,
                "Move",
                CadCommands.Values(displacement.X, displacement.Y)
            );
        }

        /// <summary>Plans COPY by a displacement.</summary>
        /// <param name="handles">The handles of the entities, each once.</param>
        /// <param name="displacement">The displacement of the copies, in the world.</param>
        /// <returns>The plan.</returns>
        /// <exception cref="ArgumentException">Thrown when the displacement is not finite.</exception>
        public static CadEditPlan Copy(IReadOnlyList<string> handles, GeoVector2 displacement)
        {
            Guard.Argument(IsFinite(displacement), nameof(displacement), "The displacement is not finite.");
            return Transform(
                handles,
                GeoTransform3.Translation(new GeoVector3(displacement.X, displacement.Y, 0.0)),
                true
            );
        }

        /// <summary>Plans ROTATE about a base point; entities are turned where they stand.</summary>
        /// <param name="handles">The handles of the entities, each once.</param>
        /// <param name="basePoint">The point they turn about, in the world.</param>
        /// <param name="angle">The angle in radians, counter-clockwise.</param>
        /// <returns>The plan.</returns>
        /// <exception cref="ArgumentException">Thrown when the base point is not finite.</exception>
        /// <exception cref="ArgumentOutOfRangeException">Thrown when the angle is not a finite number.</exception>
        public static CadEditPlan Rotate(IReadOnlyList<string> handles, GeoPoint2 basePoint, double angle)
        {
            Guard.Argument(IsFinite(basePoint), nameof(basePoint), "The base point is not a finite point.");
            Guard.Finite(angle, nameof(angle), "An angle is a finite number.");
            return Transform(
                handles,
                Rotation(basePoint, angle),
                false,
                "Rotate",
                CadCommands.Values(basePoint.X, basePoint.Y, angle)
            );
        }

        /// <summary>Plans SCALE about a base point; entities are scaled where they stand.</summary>
        /// <param name="handles">The handles of the entities, each once.</param>
        /// <param name="basePoint">The point that stays where it is, in the world.</param>
        /// <param name="factor">The factor, above nought.</param>
        /// <returns>The plan.</returns>
        /// <exception cref="ArgumentException">Thrown when the base point is not finite.</exception>
        /// <exception cref="ArgumentOutOfRangeException">Thrown when the factor is not a finite number above nought.</exception>
        public static CadEditPlan Scale(IReadOnlyList<string> handles, GeoPoint2 basePoint, double factor)
        {
            Guard.Argument(IsFinite(basePoint), nameof(basePoint), "The base point is not a finite point.");
            Guard.Positive(factor, nameof(factor), "A scale factor is a finite number above nought.");
            return Transform(
                handles,
                GeoTransform3.Scaling(CadCommands.P3(basePoint), factor),
                false,
                "Scale",
                CadCommands.Values(basePoint.X, basePoint.Y, factor)
            );
        }

        /// <summary>Plans MIRROR across the line through two points, with the readability MIRRTEXT and MIRRHATCH give.</summary>
        /// <param name="session">The session whose tolerance, drawing and MIRRHATCH it is.</param>
        /// <param name="handles">The handles of the entities, each once.</param>
        /// <param name="first">The first point of the mirror line, in the world.</param>
        /// <param name="second">The second point.</param>
        /// <param name="eraseSource">Whether the entities are mirrored where they stand, rather than copied.</param>
        /// <returns>The plan.</returns>
        /// <exception cref="ArgumentException">Thrown when a point is not finite or the two are not a line (<see cref="MirrorAcross"/>).</exception>
        public static CadEditPlan Mirror(
            CadCommandSession session,
            IReadOnlyList<string> handles,
            GeoPoint2 first,
            GeoPoint2 second,
            bool eraseSource
        )
        {
            Guard.Argument(IsFinite(first), nameof(first), "A point of the mirror line is not finite.");
            Guard.Argument(IsFinite(second), nameof(second), "A point of the mirror line is not finite.");
            GeoTransform3 mirror = MirrorAcross(first, second, session.Tolerance);
            Guard.Argument(mirror != null, nameof(second), "The mirror line needs two points apart.");
            return Transform(
                handles,
                mirror,
                !eraseSource,
                "Mirror",
                CadCommands.Values(first.X, first.Y, second.X, second.Y),
                CadCommands.MirrorReadable(session, mirror)
            );
        }

        /// <summary>The words OFFSET uses for an object it cannot offset.</summary>
        public const string CannotOffset = "Cannot offset that object.";

        /// <summary>Plans one pick of OFFSET of a curve of the drawing by a typed distance, the copies on the layer of the curve and the curve kept.</summary>
        /// <param name="session">The session whose tolerance and drawing it is.</param>
        /// <param name="curve">The drawing's curve.</param>
        /// <param name="distance">The distance, above the session's <c>EqualPoint</c>.</param>
        /// <param name="side">The point on the side to offset to, in the world, not on the curve.</param>
        /// <returns>The plan.</returns>
        /// <exception cref="ArgumentException">Thrown when the curve is not one OFFSET takes, or the point is not finite or lies on the curve.</exception>
        /// <exception cref="ArgumentOutOfRangeException">Thrown when the distance is not a finite number above <c>EqualPoint</c>.</exception>
        /// <exception cref="InvalidOperationException">Thrown when the offset makes no curve.</exception>
        public static CadEditPlan OffsetBy(CadCommandSession session, DxfCurve curve, double distance, GeoPoint2 side)
        {
            Guard.Argument(IsFinite(side), nameof(side), "The side point is not a finite point.");

            if (!(Guard.IsFinite(distance) && distance > session.Tolerance.EqualPoint))
            {
                throw new ArgumentOutOfRangeException(
                    nameof(distance),
                    distance,
                    "An offset distance is a finite number above the tolerance."
                );
            }

            Guard.Argument(CanOffset(curve), nameof(curve), CannotOffset);
            Guard.Argument(
                !LiesOn(curve, side, session.Tolerance),
                nameof(side),
                "The side point lies on the curve, so it names no side."
            );
            return Offset(session, curve.Handle, side, distance, false, null)
                ?? throw new InvalidOperationException(CannotOffset);
        }

        /// <summary>Whether a point lies on a curve to a tolerance, where it names no side of it.</summary>
        private static bool LiesOn(DxfCurve curve, GeoPoint2 point, Tolerance tolerance)
        {
            try
            {
                bool straight = curve is DxfLine || curve is DxfXline || curve is DxfRay;
                GeoPoint3 near = curve.GetClosestPointTo(CadCommands.P3(point), straight);
                return new GeoVector2(point.X - near.X, point.Y - near.Y).Length <= tolerance.EqualPoint;
            }
            catch (Exception exception)
                when (exception is ArgumentException
                    || exception is InvalidOperationException
                    || exception is NotSupportedException
                )
            {
                // As OFFSET reads it: a curve with no nearest point has no side to name, and the offset refuses it in its words.
                return false;
            }
        }

        /// <summary>
        /// Why an entity cannot be edited by a call, in words that name it; null where it can. The entity is to be the
        /// drawing's own object (what <see cref="CadDocument.FindEntity"/> gives for its handle, so that an object kept across
        /// an undo is not), not erased, in a space and not on a locked layer.
        /// </summary>
        /// <param name="document">The drawing.</param>
        /// <param name="entity">The entity.</param>
        /// <returns>The words, or null.</returns>
        public static string Unfit(CadDocument document, DxfEntity entity)
        {
            string handle = entity.Handle;

            if (entity.IsErased)
            {
                return "The object " + handle + " is erased.";
            }

            if (string.IsNullOrEmpty(handle) || !ReferenceEquals(document.FindEntity(handle), entity))
            {
                return "The object " + handle + " is not in the drawing " + document.Title + ".";
            }

            if (document.FindSpace(handle) == null)
            {
                return "The object "
                    + handle
                    + " is inside a block, not in a space of the drawing "
                    + document.Title
                    + ".";
            }

            return CadLayerEditor.IsLocked(document.Database, entity.Layer)
                ? "The object " + handle + " is on the locked layer " + entity.Layer + "."
                : null;
        }

        private static bool IsFinite(GeoVector2 vector) => Guard.IsFinite(vector.X) && Guard.IsFinite(vector.Y);

        private static bool IsFinite(GeoPoint2 point) => Guard.IsFinite(point.X) && Guard.IsFinite(point.Y);
    }
}
