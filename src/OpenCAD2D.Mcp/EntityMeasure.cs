using System;
using GeometryHelper.Dxf;

namespace OpenCAD2D.Mcp
{
    /// <summary>The length and the area of an entity, in millimetres and square millimetres, as the Properties palette gives them.</summary>
    internal static class EntityMeasure
    {
        /// <summary>
        /// The length of a curve: a line, the arc length of an arc, the circumference of a circle, a polyline's with its bulges,
        /// an ellipse or a spline along its curve. Null for what has no length (a text, a hatch) or an infinite one.
        /// </summary>
        public static double? Length(DxfEntity entity)
        {
            try
            {
                switch (entity)
                {
                    case DxfLine line:
                        return Finite(line.Length);
                    case DxfArc arc:
                        return Finite(arc.Length);
                    case DxfCircle circle:
                        return Finite(circle.Circumference);
                    case DxfPolyline polyline:
                        return Finite(polyline.Length);
                    case DxfXline _:
                    case DxfRay _:
                        return null;
                    case DxfCurve curve:
                        return Finite(
                            curve.GetDistanceAtParameter(curve.EndParam)
                                - curve.GetDistanceAtParameter(curve.StartParam)
                        );
                    default:
                        return null;
                }
            }
            catch (Exception exception) when (exception is ArgumentException || exception is InvalidOperationException)
            {
                return null;
            }
        }

        /// <summary>
        /// The area a closed shape takes: a circle, a closed polyline, a closed ellipse or spline, a hatch. Null for an open curve
        /// and for what has no area.
        /// </summary>
        public static double? Area(DxfEntity entity)
        {
            try
            {
                switch (entity)
                {
                    case DxfHatch hatch:
                        return Finite(Math.Abs(hatch.Area));
                    case DxfCircle circle:
                        return Finite(Math.Abs(circle.Area));
                    case DxfCurve curve when curve.Closed && !(entity is DxfLine) && !(entity is DxfArc):
                        return Finite(Math.Abs(curve.Area));
                    default:
                        return null;
                }
            }
            catch (Exception exception) when (exception is ArgumentException || exception is InvalidOperationException)
            {
                return null;
            }
        }

        private static double? Finite(double value) =>
            double.IsNaN(value) || double.IsInfinity(value) ? (double?)null : value;
    }
}
