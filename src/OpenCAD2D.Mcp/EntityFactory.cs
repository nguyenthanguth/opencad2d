using System;
using System.Collections.Generic;
using GeometryHelper.Dxf;
using GeometryHelper.Geometry;

namespace OpenCAD2D.Mcp
{
    /// <summary>
    /// Makes the entities the drawing tools add, as the <c>add_*</c> methods of the Python <c>Drawing</c> make them: the
    /// same objects, the same refusals. Angles are in radians here; the tools turn the degrees an agent gives into radians
    /// before they call.
    /// </summary>
    internal static class EntityFactory
    {
        /// <summary>The distance below which two points of a dimension are taken as the same, in millimetres.</summary>
        private const double Tiny = 1e-9;

        /// <summary>A line from one point to another.</summary>
        public static DxfLine Line(GeoPoint3 start, GeoPoint3 end) => new DxfLine(start, end);

        /// <summary>A circle of a radius above nought.</summary>
        public static DxfCircle Circle(GeoPoint3 center, double radius)
        {
            Positive("The radius of a circle", radius);
            return new DxfCircle { Center = center, Radius = radius };
        }

        /// <summary>An arc, counter-clockwise from the start angle to the end angle (radians).</summary>
        public static DxfArc Arc(GeoPoint3 center, double radius, double startAngle, double endAngle)
        {
            Positive("The radius of an arc", radius);
            Finite("The angles of an arc", startAngle, endAngle);
            return new DxfArc(center, radius, startAngle, endAngle);
        }

        /// <summary>
        /// A polyline of straight sides through the points, closed where asked. The z of the first point is its elevation.
        /// </summary>
        public static DxfPolyline Polyline(IReadOnlyList<GeoPoint3> points, bool closed)
        {
            if (points.Count < 2)
            {
                throw new ArgumentException("A polyline needs at least two points.");
            }

            var polyline = new DxfPolyline();

            for (int i = 0; i < points.Count; i++)
            {
                polyline.AddVertexAt(i, new GeoPoint2(points[i].X, points[i].Y), 0.0, 0.0, 0.0);
            }

            polyline.Elevation = points[0].Z;
            polyline.Closed = closed;
            return polyline;
        }

        /// <summary>A single-line text; the style null or empty is the drawing's Standard.</summary>
        public static DxfDBText Text(string text, GeoPoint3 insert, double height, double rotation, string style)
        {
            Positive("The height of a text", height);
            Finite("The rotation of a text", rotation);
            var made = new DxfDBText
            {
                TextString = text,
                Position = insert,
                Height = height,
                Rotation = rotation,
            };

            if (!string.IsNullOrEmpty(style))
            {
                made.TextStyleName = style;
            }

            return made;
        }

        /// <summary>A multi-line text wrapped at a width (0 for no wrapping); the style null or empty is the drawing's Standard.</summary>
        public static DxfMText MText(string text, GeoPoint3 insert, double height, double width, string style)
        {
            Positive("The height of an MTEXT", height);
            Finite("The width of an MTEXT", width);
            var made = new DxfMText
            {
                Contents = text,
                Location = insert,
                TextHeight = height,
            };

            if (width > 0.0)
            {
                made.Width = width;
            }

            if (!string.IsNullOrEmpty(style))
            {
                made.TextStyleName = style;
            }

            return made;
        }

        /// <summary>
        /// A linear dimension of the distance between two points, as DIMLINEAR makes one: its line runs through the location
        /// along the angle (radians); with no angle it is level where the location lies above or below the two points and
        /// upright where it lies to their left or right.
        /// </summary>
        public static DxfRotatedDimension LinearDimension(
            GeoPoint3 first,
            GeoPoint3 second,
            GeoPoint3 location,
            double? angle,
            string text,
            string style
        )
        {
            Finite("A point of a dimension", first.X, first.Y, second.X, second.Y, location.X, location.Y);
            double direction;

            if (angle.HasValue)
            {
                direction = angle.Value;
            }
            else
            {
                double outX = Math.Max(
                    Math.Min(first.X, second.X) - location.X,
                    location.X - Math.Max(first.X, second.X)
                );
                double outY = Math.Max(
                    Math.Min(first.Y, second.Y) - location.Y,
                    location.Y - Math.Max(first.Y, second.Y)
                );
                direction = outX > outY ? Math.PI / 2.0 : 0.0;
            }

            Finite("The angle of a dimension", direction);
            double cos = Math.Cos(direction);
            double sin = Math.Sin(direction);

            if (Math.Abs((second.X - first.X) * cos + (second.Y - first.Y) * sin) <= Tiny)
            {
                throw new ArgumentException(
                    "The two points of a linear dimension are no distance apart along its direction."
                );
            }

            double foot = (second.X - location.X) * cos + (second.Y - location.Y) * sin;
            var line = new GeoPoint3(location.X + cos * foot, location.Y + sin * foot, 0.0);
            return Styled(new DxfRotatedDimension(direction, first, second, line, text ?? string.Empty, null), style);
        }

        /// <summary>An aligned dimension of the distance between two points, as DIMALIGNED makes one.</summary>
        public static DxfAlignedDimension AlignedDimension(
            GeoPoint3 first,
            GeoPoint3 second,
            GeoPoint3 location,
            string text,
            string style
        )
        {
            Finite("A point of a dimension", first.X, first.Y, second.X, second.Y, location.X, location.Y);
            double dx = second.X - first.X;
            double dy = second.Y - first.Y;
            double length = Math.Sqrt(dx * dx + dy * dy);

            if (length <= Tiny)
            {
                throw new ArgumentException("The two points of an aligned dimension are the same point.");
            }

            double cos = dx / length;
            double sin = dy / length;
            double foot = (second.X - location.X) * cos + (second.Y - location.Y) * sin;
            var line = new GeoPoint3(location.X + cos * foot, location.Y + sin * foot, 0.0);
            return Styled(new DxfAlignedDimension(first, second, line, text ?? string.Empty, null), style);
        }

        /// <summary>A radius dimension of the circle or arc about a centre, as DIMRADIUS makes one.</summary>
        public static DxfRadialDimension RadialDimension(
            GeoPoint3 center,
            double radius,
            GeoPoint3 location,
            string text,
            string style
        )
        {
            Rim(center, radius, location, "radius", out GeoPoint3 middle, out GeoPoint3 near, out _);
            return Styled(new DxfRadialDimension(middle, near, 0.0, text ?? string.Empty, null), style);
        }

        /// <summary>A diameter dimension of the circle or arc about a centre, as DIMDIAMETER makes one.</summary>
        public static DxfDiametricDimension DiameterDimension(
            GeoPoint3 center,
            double radius,
            GeoPoint3 location,
            string text,
            string style
        )
        {
            Rim(center, radius, location, "diameter", out _, out GeoPoint3 near, out GeoPoint3 far);
            return Styled(new DxfDiametricDimension(near, far, 0.0, text ?? string.Empty, null), style);
        }

        /// <summary>An angular dimension of the angle at a vertex between the rays through two points, as DIMANGULAR makes one from three points.</summary>
        public static DxfPoint3AngularDimension AngularDimension(
            GeoPoint3 vertex,
            GeoPoint3 first,
            GeoPoint3 second,
            GeoPoint3 location,
            string text,
            string style
        )
        {
            Finite(
                "A point of an angular dimension",
                vertex.X,
                vertex.Y,
                first.X,
                first.Y,
                second.X,
                second.Y,
                location.X,
                location.Y
            );

            foreach (GeoPoint3 ray in new[] { first, second })
            {
                double dx = ray.X - vertex.X;
                double dy = ray.Y - vertex.Y;

                if (Math.Sqrt(dx * dx + dy * dy) <= Tiny)
                {
                    throw new ArgumentException("A ray of an angular dimension starts and ends at its vertex.");
                }
            }

            return Styled(
                new DxfPoint3AngularDimension(vertex, first, second, location, text ?? string.Empty, null),
                style
            );
        }

        /// <summary>
        /// A hatch of the area a closed outline of straight sides encloses, with a pattern of the built-in set (or "SOLID" for a
        /// fill) at a scale and an angle (radians).
        /// </summary>
        public static DxfHatch Hatch(IReadOnlyList<GeoPoint3> corners, string pattern, double scale, double angle)
        {
            if (corners.Count < 3)
            {
                throw new ArgumentException("A hatch boundary needs at least three points.");
            }

            var points = new GeoPoint2[corners.Count];

            for (int i = 0; i < points.Length; i++)
            {
                points[i] = new GeoPoint2(corners[i].X, corners[i].Y);
            }

            return Hatch(new[] { (points, new double[points.Length]) }, pattern, scale, angle);
        }

        /// <summary>
        /// A hatch of the areas closed outlines enclose, each a list of vertices with a bulge for each (the tangent of a quarter of the
        /// angle the arc from that vertex to the next turns through, counter-clockwise where positive; 0 for a straight side): the
        /// first outline is the external one and the others, islands or other pieces, are found by the fill as the display fills them.
        /// </summary>
        public static DxfHatch Hatch(
            IReadOnlyList<(GeoPoint2[] Points, double[] Bulges)> loops,
            string pattern,
            double scale,
            double angle
        )
        {
            if (loops.Count == 0)
            {
                throw new ArgumentException("A hatch needs a boundary.");
            }

            Positive("The scale of a hatch", scale);
            Finite("The angle of a hatch", angle);
            string name = string.IsNullOrWhiteSpace(pattern) ? "ANSI31" : pattern.Trim().ToUpperInvariant();
            CadHatchPattern found = name == "SOLID" ? null : CadPatternFile.FindBuiltIn(name);

            if (name != "SOLID" && found == null)
            {
                throw new ArgumentException(
                    "The pattern " + pattern + " is not in the built-in set " + CadPatternFile.BuiltInName + "."
                );
            }

            var made = new DxfHatch();
            made.SetHatchPattern(DxfHatchPatternType.PreDefined, name);

            if (found != null)
            {
                made.PatternAngle = angle;
                made.PatternScale = scale;
                made.SetPatternDefinitions(found.Place(angle * 180.0 / Math.PI, scale, 0.0, 0.0));
            }

            for (int i = 0; i < loops.Count; i++)
            {
                made.AppendLoop(
                    i == 0 ? DxfHatchLoopTypes.External : DxfHatchLoopTypes.Default,
                    loops[i].Points,
                    loops[i].Bulges
                );
            }

            return made;
        }

        /// <summary>An INSERT of a block by name, scaled and turned (radians). The block is the drawing's; the backend refuses a name it has not.</summary>
        public static DxfBlockReference Insert(string name, GeoPoint3 insert, double scale, double rotation)
        {
            if (string.IsNullOrWhiteSpace(name))
            {
                throw new ArgumentException("A block needs a name.");
            }

            Positive("The scale of an insert", scale);
            Finite("The rotation of an insert", rotation);
            var carrier = new DxfDatabase("AC1027");
            var record = new DxfBlockTableRecord { Name = name };
            carrier.BlockTable.Add(record);
            return new DxfBlockReference(insert, record)
            {
                ScaleFactors = new GeoVector3(scale, scale, scale),
                Rotation = rotation,
            };
        }

        /// <summary>
        /// Sets the layer, the colour index and the linetype an object is made with, where given: what is left out stays the
        /// drawing's current layer and a colour and linetype BYLAYER.
        /// </summary>
        public static T Looks<T>(T entity, string layer, int? color, string linetype)
            where T : DxfEntity
        {
            if (!string.IsNullOrEmpty(layer))
            {
                entity.Layer = layer;
            }

            if (color.HasValue)
            {
                entity.ColorIndex = color.Value;
            }

            if (!string.IsNullOrEmpty(linetype))
            {
                entity.Linetype = linetype;
            }

            return entity;
        }

        private static T Styled<T>(T dimension, string style)
            where T : DxfDimension
        {
            dimension.DimensionStyleName = style;
            return dimension;
        }

        /// <summary>The centre, the point of the rim the line of a radius or diameter dimension meets and the point opposite it.</summary>
        private static void Rim(
            GeoPoint3 center,
            double radius,
            GeoPoint3 location,
            string what,
            out GeoPoint3 middle,
            out GeoPoint3 near,
            out GeoPoint3 far
        )
        {
            Finite("A point of a " + what + " dimension", center.X, center.Y, location.X, location.Y);
            Positive("The radius of a " + what + " dimension", radius);
            double dx = location.X - center.X;
            double dy = location.Y - center.Y;
            double length = Math.Sqrt(dx * dx + dy * dy);
            double ux = length <= Tiny ? 1.0 : dx / length;
            double uy = length <= Tiny ? 0.0 : dy / length;
            middle = new GeoPoint3(center.X, center.Y, 0.0);
            near = new GeoPoint3(center.X + ux * radius, center.Y + uy * radius, 0.0);
            far = new GeoPoint3(center.X - ux * radius, center.Y - uy * radius, 0.0);
        }

        private static void Finite(string what, params double[] numbers)
        {
            foreach (double number in numbers)
            {
                if (double.IsNaN(number) || double.IsInfinity(number))
                {
                    throw new ArgumentException(what + " is a finite number, not " + number + ".");
                }
            }
        }

        private static void Positive(string what, double number)
        {
            if (double.IsNaN(number) || double.IsInfinity(number) || number <= 0.0)
            {
                throw new ArgumentException(what + " is a finite number above nought, not " + number + ".");
            }
        }
    }
}
