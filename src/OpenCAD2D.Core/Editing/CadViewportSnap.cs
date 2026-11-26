using System;
using System.Collections.Generic;
using GeometryHelper;
using GeometryHelper.Dxf;
using GeometryHelper.Geometry;

namespace OpenCAD2D
{
    /// <summary>
    /// Object snaps to the model space a viewport shows, as AutoCAD gives them: from the paper, a point of a LINE or a
    /// dimension snaps to an endpoint, a centre or a nearest point of the model geometry seen through a viewport, the point
    /// taken where the viewport shows it; through a viewport worked in, the layers frozen in it are left out of the snaps.
    /// </summary>
    internal static class CadViewportSnap
    {
        /// <summary>The layers frozen in a viewport, whose objects it does not show.</summary>
        internal static ISet<string> FrozenIn(DxfViewport viewport)
        {
            if (viewport == null)
            {
                return null;
            }

            HashSet<string> names = null;

            foreach (DxfLayerTableRecord layer in viewport.GetFrozenLayers())
            {
                names = names ?? new HashSet<string>(StringComparer.OrdinalIgnoreCase);
                names.Add(layer.Name);
            }

            return names;
        }

        /// <summary>
        /// The object snap of model space seen through the viewports of the layout shown, where it lies nearer the cursor than
        /// the snap of the paper's own objects; the paper's where it does not or none is seen. Model space is snapped in model
        /// units, the aperture in the viewport's scale, and the point taken to the paper by the viewport's view, twist and
        /// scale. The result names the object of model space and the viewport (<see cref="CadSnapResult.ViewportHandle"/>),
        /// its point being the paper's: a dimension made from it is tied to the object through the viewport, as AutoCAD ties a
        /// trans-spatial dimension (<see cref="CadDimAssociation.FromSnap"/>). A snap of a plugin is worked out in model units
        /// as the built-in ones are, and the result names it, for its glyph and its tip.
        /// </summary>
        /// <param name="document">The document.</param>
        /// <param name="space">The layout shown.</param>
        /// <param name="snap">The snaps.</param>
        /// <param name="own">The snap of the paper's own objects, as the snap gave it.</param>
        /// <param name="cursor">The cursor, on the paper.</param>
        /// <param name="aperture">The aperture, in paper units.</param>
        /// <param name="basePoint">The point the rubber band runs from, on the paper; null for none.</param>
        /// <param name="drawnFrom">The point perpendicular and tangent are drawn from, on the paper; null for none.</param>
        /// <returns>The snap to take.</returns>
        internal static CadSnapResult Through(
            CadDocument document,
            CadSpace space,
            ObjectSnap snap,
            CadSnapResult own,
            GeoPoint2 cursor,
            double aperture,
            GeoPoint2? basePoint,
            GeoPoint2? drawnFrom
        )
        {
            if (space == null || space.IsModel || !(aperture > 0.0))
            {
                return own;
            }

            double best = own != null && IsObject(own.Kind) ? (own.Point - cursor).Length : double.MaxValue;
            CadSnapResult found = null;
            PickIndex model = null;

            foreach (DxfViewport viewport in document.EnterableViewports)
            {
                if (!viewport.On || !Reaches(viewport, cursor, aperture))
                {
                    continue;
                }

                model = model ?? document.GetPickIndex(document.ModelSpace);
                double scale = viewport.Height / viewport.ViewHeight;
                snap.HiddenLayers = FrozenIn(viewport);
                CadSnapResult seen;

                try
                {
                    seen = snap.SnapToObjects(
                        model,
                        CadViewports.ToModel(viewport, cursor),
                        aperture / scale,
                        basePoint.HasValue ? CadViewports.ToModel(viewport, basePoint.Value) : (GeoPoint2?)null,
                        drawnFrom.HasValue ? CadViewports.ToModel(viewport, drawnFrom.Value) : (GeoPoint2?)null
                    );
                }
                finally
                {
                    snap.HiddenLayers = null;
                }

                // An object offered as the first of an extended apparent intersection is one of model space: not taken here.
                if (seen == null || seen.Source != null || !IsObject(seen.Kind))
                {
                    continue;
                }

                GeoPoint2 at = CadViewports.ToPaper(viewport, seen.Point);
                double reach = (at - cursor).Length;

                if (reach < best && Reaches(viewport, at, Tolerance.Default.EqualPoint))
                {
                    best = reach;
                    found = new CadSnapResult(seen.Kind, at, seen.Handle, null, null, seen.CustomSnap)
                    {
                        ViewportHandle = viewport.Handle,
                        ModelPoint = seen.Point,
                    };
                }
            }

            return found ?? own;
        }

        /// <summary>
        /// The cursor has rested at a point prompt of a layout, nothing of the sheet acquired: the snap point of model space a
        /// viewport shows under it is acquired for object snap tracking, as one of the sheet is, at the place the viewport shows
        /// it (the nearest where two viewports show one), the tracking lines running from it on the sheet. Only the points of
        /// OTRACK are acquired so: the run of Extension and the line of Parallel are not.
        /// </summary>
        /// <param name="document">The document.</param>
        /// <param name="space">The layout shown.</param>
        /// <param name="snap">The snaps.</param>
        /// <param name="cursor">The cursor, on the sheet.</param>
        /// <param name="aperture">The aperture, in sheet units.</param>
        /// <returns>Whether a point was acquired.</returns>
        internal static bool AcquireThrough(
            CadDocument document,
            CadSpace space,
            ObjectSnap snap,
            GeoPoint2 cursor,
            double aperture
        )
        {
            if (space == null || space.IsModel || !(aperture > 0.0))
            {
                return false;
            }

            CadAcquiredPoint best = null;
            double nearest = double.MaxValue;
            PickIndex model = null;

            foreach (DxfViewport viewport in document.EnterableViewports)
            {
                if (!viewport.On || !Reaches(viewport, cursor, aperture))
                {
                    continue;
                }

                model = model ?? document.GetPickIndex(document.ModelSpace);
                double scale = viewport.Height / viewport.ViewHeight;
                snap.HiddenLayers = FrozenIn(viewport);
                CadAcquiredPoint found;
                double reach;

                try
                {
                    found = snap.TrackingPointThrough(
                        model,
                        CadViewports.ToModel(viewport, cursor),
                        aperture / scale,
                        scale,
                        at => CadViewports.ToPaper(viewport, at),
                        out reach
                    );
                }
                finally
                {
                    snap.HiddenLayers = null;
                }

                if (found != null && reach < nearest && Reaches(viewport, found.Point, Tolerance.Default.EqualPoint))
                {
                    best = found;
                    nearest = reach;
                }
            }

            if (best == null)
            {
                return false;
            }

            snap.AcquireFound(best);
            return true;
        }

        /// <summary>
        /// The snap points of a kind a point typed after an override gets from the model space the viewports of the layout show,
        /// as the snap of the objects of the sheet gives them: the objects of model space the viewport shows within the aperture
        /// of the point (<see cref="CadCommandSession.SnapAperture"/>, in the viewport's scale), the layers frozen in it left
        /// out, each giving the point of the snap nearest the point however far, taken to the sheet; a point the viewport does not
        /// show is left out. Nothing for the model space or where no viewport reaches the point. INT and APP need two objects
        /// under the point: one alone is not asked for its second.
        /// </summary>
        /// <param name="session">The session.</param>
        /// <param name="kind">The snap.</param>
        /// <param name="point">The point typed, on the sheet.</param>
        /// <param name="from">The point perpendicular and tangent are from, on the sheet; null for the others.</param>
        /// <returns>The points, on the sheet, viewport by viewport.</returns>
        internal static IEnumerable<GeoPoint2> TypedPoints(
            CadCommandSession session,
            CadSnapKind kind,
            GeoPoint2 point,
            GeoPoint2? from
        )
        {
            CadDocument document = session.Document;
            CadSpace space = document.WorkingSpace;
            double aperture = Math.Max(session.SnapAperture, 1E-9);
            var found = new List<GeoPoint2>();

            if (space == null || space.IsModel)
            {
                return found;
            }

            PickIndex model = null;

            foreach (DxfViewport viewport in document.EnterableViewports)
            {
                if (!viewport.On || !Reaches(viewport, point, aperture))
                {
                    continue;
                }

                model = model ?? document.GetPickIndex(document.ModelSpace);
                double scale = viewport.Height / viewport.ViewHeight;
                ISet<string> hidden = FrozenIn(viewport);
                GeoPoint2 at = CadViewports.ToModel(viewport, point);
                GeoPoint2? fromModel = from.HasValue ? CadViewports.ToModel(viewport, from.Value) : (GeoPoint2?)null;
                var near = new List<string>();

                foreach (string handle in model.Pick(at.X, at.Y, aperture / scale))
                {
                    DxfEntity entity = document.FindEntity(handle);

                    if (entity != null && (hidden == null || !hidden.Contains(entity.Layer)))
                    {
                        near.Add(handle);
                    }
                }

                near.Sort(StringComparer.Ordinal);
                var shown = new List<GeoPoint2?>();

                if (kind == CadSnapKind.Intersection || kind == CadSnapKind.ApparentIntersection)
                {
                    for (int i = 0; i < near.Count && i < 8; i++)
                    {
                        for (int j = i + 1; j < near.Count && j < 8; j++)
                        {
                            shown.Add(CadCalcSnap.Between(session, kind, near[i], near[j], at, model));
                        }
                    }
                }
                else
                {
                    foreach (string handle in near)
                    {
                        shown.Add(CadCalcSnap.Snap(session, kind, handle, at, fromModel, model));
                    }
                }

                foreach (GeoPoint2? seen in shown)
                {
                    if (seen.HasValue)
                    {
                        GeoPoint2 on = CadViewports.ToPaper(viewport, seen.Value);

                        if (Reaches(viewport, on, Tolerance.Default.EqualPoint))
                        {
                            found.Add(on);
                        }
                    }
                }
            }

            return found;
        }

        private static bool IsObject(CadSnapKind kind) =>
            kind != CadSnapKind.None
            && kind != CadSnapKind.Grid
            && kind != CadSnapKind.Ortho
            && kind != CadSnapKind.Polar
            && kind != CadSnapKind.Tracking;

        /// <summary>Whether a point of the paper lies in the frame of a viewport, or in its clip outline, or within a reach of its edge.</summary>
        private static bool Reaches(DxfViewport viewport, GeoPoint2 point, double reach)
        {
            DisplayClip2 clip = CadViewportClips.OutlineOf(viewport);

            if (clip != null)
            {
                return clip.Contains(point.X, point.Y) || clip.DistanceToEdge(point.X, point.Y) <= reach;
            }

            GeoPoint3 middle = viewport.CenterPoint;
            return Math.Abs(point.X - middle.X) <= viewport.Width / 2.0 + reach
                && Math.Abs(point.Y - middle.Y) <= viewport.Height / 2.0 + reach;
        }
    }
}
