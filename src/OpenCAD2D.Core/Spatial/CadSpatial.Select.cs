using System;
using System.Collections.Generic;
using GeometryHelper.Dxf;
using GeometryHelper.Geometry;

namespace OpenCAD2D
{
    /// <summary>How <see cref="CadSpatial.Select"/> takes entities by where they are drawn.</summary>
    public enum CadAreaSelection
    {
        /// <summary>The entities wholly inside the box of two corners, as the Window option of "Select objects:".</summary>
        Window,

        /// <summary>The entities the box of two corners lies across or contains, as the Crossing option.</summary>
        Crossing,

        /// <summary>The entities a fence of two or more points crosses, as the Fence option.</summary>
        Fence,

        /// <summary>The entities wholly inside a polygon of three or more points, as WPolygon.</summary>
        WindowPolygon,

        /// <summary>The entities a polygon of three or more points touches, as CPolygon.</summary>
        CrossingPolygon,

        /// <summary>The entities that pass within a distance of one point, the one picked first leading.</summary>
        Point,
    }

    public static partial class CadSpatial
    {
        /// <summary>
        /// Takes the entities of a space by where they are drawn, as "Select objects:" takes them, but whole: the answer is not held to
        /// the view of a window, so the same drawing gives the same handles with a window or without. A box from (0, 0) to (100, 50)
        /// in Window takes a line from (10, 10) to (20, 10) and leaves a circle of radius 30 about (50, 25), which Crossing takes.
        /// The pickfirst selection of the drawing is not changed, and objects on locked, off or frozen layers are taken as the space
        /// draws them (what draws nothing is not found).
        /// </summary>
        /// <param name="document">The drawing.</param>
        /// <param name="space">The space: model space or a layout of the drawing.</param>
        /// <param name="how">What the points mean.</param>
        /// <param name="points">Two corners for <see cref="CadAreaSelection.Window"/> and <see cref="CadAreaSelection.Crossing"/>, two or more for a fence, three or more for a polygon, one for a point.</param>
        /// <param name="tolerance">For <see cref="CadAreaSelection.Point"/>, how far from the point an entity may pass, above nought; not used by the others.</param>
        /// <returns>The handles, in the order the space draws its entities (the first viewport of a layout left out); for a point, the one picked first leading.</returns>
        /// <exception cref="ArgumentNullException">Thrown when the document, the space or the points are null.</exception>
        /// <exception cref="ArgumentException">Thrown when a point is not finite, or there are too few or too many points for the selection, or the tolerance of a point is not above nought.</exception>
        public static IReadOnlyList<string> Select(
            CadDocument document,
            CadSpace space,
            CadAreaSelection how,
            IReadOnlyList<GeoPoint2> points,
            double tolerance
        )
        {
            Check(document, space);

            if (points == null)
            {
                throw new ArgumentNullException(nameof(points));
            }

            foreach (GeoPoint2 point in points)
            {
                if (
                    double.IsNaN(point.X)
                    || double.IsNaN(point.Y)
                    || double.IsInfinity(point.X)
                    || double.IsInfinity(point.Y)
                )
                {
                    throw new ArgumentException("A point of the selection is not a finite point.", nameof(points));
                }
            }

            PickIndex index = document.GetPickIndex(space);
            IReadOnlyList<string> found;

            switch (how)
            {
                case CadAreaSelection.Window:
                case CadAreaSelection.Crossing:
                    if (points.Count != 2)
                    {
                        throw new ArgumentException("A window is two corners.", nameof(points));
                    }

                    var box = new DisplayBox(
                        Math.Min(points[0].X, points[1].X),
                        Math.Min(points[0].Y, points[1].Y),
                        Math.Max(points[0].X, points[1].X),
                        Math.Max(points[0].Y, points[1].Y)
                    );
                    found = how == CadAreaSelection.Window ? index.Window(box) : index.Crossing(box);
                    break;
                case CadAreaSelection.Fence:
                    if (points.Count < 2)
                    {
                        throw new ArgumentException("A fence is two points or more.", nameof(points));
                    }

                    found = index.Fence(points);
                    break;
                case CadAreaSelection.WindowPolygon:
                case CadAreaSelection.CrossingPolygon:
                    if (points.Count < 3)
                    {
                        throw new ArgumentException("A polygon is three points or more.", nameof(points));
                    }

                    found =
                        how == CadAreaSelection.WindowPolygon
                            ? index.WindowPolygon(points)
                            : index.CrossingPolygon(points);
                    break;
                case CadAreaSelection.Point:
                    if (points.Count != 1)
                    {
                        throw new ArgumentException("A point selection is one point.", nameof(points));
                    }

                    if (!(tolerance > 0.0) || double.IsInfinity(tolerance))
                    {
                        throw new ArgumentException(
                            "The distance of a point selection is above nought.",
                            nameof(tolerance)
                        );
                    }

                    return index.Pick(points[0].X, points[0].Y, tolerance);
                default:
                    throw new ArgumentException("Unknown selection " + how + ".", nameof(how));
            }

            var wanted = new HashSet<string>(found, StringComparer.OrdinalIgnoreCase);
            var inOrder = new List<string>(wanted.Count);

            foreach (DxfEntity entity in space.Block)
            {
                if (
                    !entity.IsErased
                    && !string.IsNullOrEmpty(entity.Handle)
                    && wanted.Contains(entity.Handle)
                    && !(entity is DxfViewport viewport && viewport.Number == 1)
                )
                {
                    inOrder.Add(entity.Handle);
                }
            }

            return inOrder;
        }
    }
}
