using System;
using System.Collections.Generic;
using System.Linq;
using GeometryHelper.Dxf;
using GeometryHelper.Geometry;

namespace OpenCAD2D.Mcp
{
    /// <summary>
    /// The values of an entity in full, read from the entity itself, in millimetres and degrees: what <c>query_entities</c> gives a
    /// handful of, and the rows of <c>get_texts</c> and <c>get_dimensions</c> are made of.
    /// </summary>
    internal static class EntityDetails
    {
        /// <summary>The most vertices of a polyline, cells of a table or loops given in full.</summary>
        public const int MostItems = 500;

        public static Dictionary<string, object> Of(DxfEntity entity)
        {
            var d = new Dictionary<string, object>();

            switch (entity)
            {
                case DxfLine line:
                    d["start"] = P(line.StartPoint);
                    d["end"] = P(line.EndPoint);
                    d["length"] = R(line.Length);
                    d["angle"] = Degrees(
                        Math.Atan2(line.EndPoint.Y - line.StartPoint.Y, line.EndPoint.X - line.StartPoint.X)
                    );
                    break;
                case DxfCircle circle:
                    d["center"] = P(circle.Center);
                    d["radius"] = R(circle.Radius);
                    d["diameter"] = R(circle.Diameter);
                    d["circumference"] = R(circle.Circumference);
                    d["area"] = R(circle.Area);
                    break;
                case DxfArc arc:
                    d["center"] = P(arc.Center);
                    d["radius"] = R(arc.Radius);
                    d["startAngle"] = Degrees(arc.StartAngle);
                    d["endAngle"] = Degrees(arc.EndAngle);
                    d["length"] = R(arc.Length);
                    break;
                case DxfEllipse ellipse:
                    d["center"] = P(ellipse.Center);
                    d["majorRadius"] = R(ellipse.MajorRadius);
                    d["minorRadius"] = R(ellipse.MinorRadius);
                    d["majorAxisAngle"] = Degrees(Math.Atan2(ellipse.MajorAxis.Y, ellipse.MajorAxis.X));
                    d["startAngle"] = Degrees(ellipse.StartAngle);
                    d["endAngle"] = Degrees(ellipse.EndAngle);
                    break;
                case DxfPolyline polyline:
                    Polyline(polyline, d);
                    break;
                case DxfDBPoint point:
                    d["at"] = P(point.Position);
                    break;
                case DxfAttributeReference attribute:
                    d["tag"] = attribute.Tag;
                    d["text"] = attribute.TextString;
                    d["at"] = P(attribute.Position);
                    d["height"] = R(attribute.Height);
                    d["style"] = attribute.TextStyleName;
                    d["invisible"] = attribute.Invisible;
                    d["constant"] = attribute.IsConstant;
                    break;
                case DxfAttributeDefinition definition:
                    d["tag"] = definition.Tag;
                    d["prompt"] = definition.Prompt;
                    d["text"] = definition.TextString;
                    d["at"] = P(definition.Position);
                    d["height"] = R(definition.Height);
                    d["style"] = definition.TextStyleName;
                    d["invisible"] = definition.Invisible;
                    d["constant"] = definition.Constant;
                    break;
                case DxfDBText text:
                    d["text"] = text.TextString;
                    d["at"] = P(text.Position);
                    d["height"] = R(text.Height);
                    d["rotation"] = Degrees(text.Rotation);
                    d["style"] = text.TextStyleName;
                    d["widthFactor"] = R(text.WidthFactor);
                    d["oblique"] = Degrees(text.Oblique);
                    d["justify"] = text.Justify.ToString();
                    break;
                case DxfMText mtext:
                    d["text"] = mtext.Text;
                    d["contents"] = mtext.Contents;
                    d["at"] = P(mtext.Location);
                    d["height"] = R(mtext.TextHeight);
                    d["width"] = R(mtext.Width);
                    d["rotation"] = Degrees(mtext.Rotation);
                    d["style"] = mtext.TextStyleName;
                    d["attachment"] = mtext.Attachment.ToString();
                    d["columns"] = new Dictionary<string, object>
                    {
                        ["type"] = mtext.ColumnType.ToString(),
                        ["count"] = mtext.ColumnCount,
                        ["width"] = R(mtext.ColumnWidth),
                        ["gutter"] = R(mtext.ColumnGutterWidth),
                    };
                    break;
                case DxfDimension dimension:
                    Dimension(dimension, d);
                    break;
                case DxfBlockReference insert:
                    d["block"] = insert.Name;
                    d["at"] = P(insert.Position);
                    d["scale"] = new[] { R(insert.ScaleFactors.X), R(insert.ScaleFactors.Y), R(insert.ScaleFactors.Z) };
                    d["rotation"] = Degrees(insert.Rotation);
                    d["attributes"] = Attributes(insert)
                        .Select(a => new Dictionary<string, object> { ["tag"] = a.Tag, ["value"] = a.TextString })
                        .ToList();
                    break;
                case DxfHatch hatch:
                    d["pattern"] = hatch.PatternName;
                    d["solid"] = hatch.IsSolidFill;
                    d["gradient"] = hatch.IsGradient;
                    d["angle"] = Degrees(hatch.PatternAngle);
                    d["scale"] = R(hatch.PatternScale);
                    d["associative"] = hatch.Associative;
                    d["area"] = R(hatch.Area);
                    break;
                case DxfMLeader mleader:
                    d["style"] = mleader.MLeaderStyle;
                    d["content"] =
                        mleader.ContentType == DxfContentType.MTextContent ? "text"
                        : mleader.ContentType == DxfContentType.BlockContent ? "block"
                        : "none";
                    d["text"] = mleader.ContentType == DxfContentType.MTextContent ? mleader.MText.Text : null;
                    d["textHeight"] = R(mleader.TextHeight);
                    d["scale"] = R(mleader.Scale);
                    d["leaders"] = mleader.LeaderLineCount;
                    break;
                case DxfTable table:
                    Table(table, d);
                    break;
                case DxfSpline spline:
                    d["closed"] = spline.Closed;
                    d["length"] = EntityMeasure.Length(spline) is double length ? R(length) : (double?)null;
                    break;
            }

            return d;
        }

        private static void Polyline(DxfPolyline polyline, Dictionary<string, object> d)
        {
            int count = polyline.NumberOfVertices;
            var vertices = new List<Dictionary<string, object>>();

            for (int i = 0; i < count && i < MostItems; i++)
            {
                GeoPoint2 p = polyline.GetPoint2dAt(i);
                var vertex = new Dictionary<string, object> { ["at"] = new[] { R(p.X), R(p.Y) } };
                double bulge = polyline.GetBulgeAt(i);

                if (Math.Abs(bulge) > 1e-12)
                {
                    vertex["bulge"] = R(bulge, 6);
                }

                double start = polyline.GetStartWidthAt(i);
                double end = polyline.GetEndWidthAt(i);

                if (Math.Abs(start) > 1e-12 || Math.Abs(end) > 1e-12)
                {
                    vertex["startWidth"] = R(start);
                    vertex["endWidth"] = R(end);
                }

                vertices.Add(vertex);
            }

            d["closed"] = polyline.Closed;
            d["vertexCount"] = count;
            d["vertices"] = vertices;
            d["constantWidth"] = R(polyline.ConstantWidth);
            d["elevation"] = R(polyline.Elevation);
            d["length"] = R(polyline.Length);

            if (polyline.Closed)
            {
                d["area"] = R(Math.Abs(polyline.Area));
            }
        }

        private static void Dimension(DxfDimension dimension, Dictionary<string, object> d)
        {
            bool angular = IsAngular(dimension);
            d["type"] = TypeOf(dimension);
            d["style"] = dimension.DimensionStyleName;
            d["measurement"] = angular ? Degrees(dimension.Measurement) : R(dimension.Measurement);
            d["unit"] = angular ? "degrees" : "mm";
            d["textOverride"] = dimension.DimensionText ?? string.Empty;
            d["text"] = DrawnText(dimension);
            d["textPosition"] = P(dimension.TextPosition);
            d["textRotation"] = Degrees(dimension.TextRotation);

            foreach (KeyValuePair<string, double[]> point in DimensionPoints(dimension))
            {
                d[point.Key] = point.Value;
            }

            if (dimension is DxfRotatedDimension rotated)
            {
                d["rotation"] = Degrees(rotated.Rotation);
            }
        }

        /// <summary>The name of the kind of dimension, as <c>query_entities</c> gives it: <c>RotatedDimension</c>.</summary>
        public static string TypeOf(DxfDimension dimension) => dimension.GetType().Name.Replace("Dxf", string.Empty);

        /// <summary>Whether the measurement of a dimension is an angle (in radians as the library keeps it).</summary>
        public static bool IsAngular(DxfDimension dimension) =>
            dimension is DxfLineAngularDimension2 || dimension is DxfPoint3AngularDimension;

        /// <summary>The words a dimension is drawn with, read from its picture; null where it has none.</summary>
        public static string DrawnText(DxfDimension dimension)
        {
            DxfBlockTableRecord picture = dimension.DimBlock;

            if (picture == null)
            {
                return null;
            }

            foreach (DxfEntity part in picture)
            {
                if (part.IsErased)
                {
                    continue;
                }

                if (part is DxfMText mtext)
                {
                    return mtext.Text;
                }

                if (part is DxfDBText text)
                {
                    return text.TextString;
                }
            }

            return null;
        }

        /// <summary>The points that define a dimension, by name, <c>[x, y]</c> each in order of name.</summary>
        public static SortedDictionary<string, double[]> DimensionPoints(DxfDimension dimension)
        {
            var points = new SortedDictionary<string, double[]>(StringComparer.Ordinal);

            switch (dimension)
            {
                case DxfRotatedDimension rotated:
                    points["xLine1Point"] = P(rotated.XLine1Point);
                    points["xLine2Point"] = P(rotated.XLine2Point);
                    points["dimLinePoint"] = P(rotated.DimLinePoint);
                    break;
                case DxfAlignedDimension aligned:
                    points["xLine1Point"] = P(aligned.XLine1Point);
                    points["xLine2Point"] = P(aligned.XLine2Point);
                    points["dimLinePoint"] = P(aligned.DimLinePoint);
                    break;
                case DxfRadialDimension radial:
                    points["center"] = P(radial.Center);
                    points["chordPoint"] = P(radial.ChordPoint);
                    break;
                case DxfDiametricDimension diametric:
                    points["chordPoint"] = P(diametric.ChordPoint);
                    points["farChordPoint"] = P(diametric.FarChordPoint);
                    break;
                case DxfLineAngularDimension2 lines:
                    points["xLine1Start"] = P(lines.XLine1Start);
                    points["xLine1End"] = P(lines.XLine1End);
                    points["xLine2Start"] = P(lines.XLine2Start);
                    points["xLine2End"] = P(lines.XLine2End);
                    points["arcPoint"] = P(lines.ArcPoint);
                    break;
                case DxfPoint3AngularDimension three:
                    points["center"] = P(three.CenterPoint);
                    points["xLine1Point"] = P(three.XLine1Point);
                    points["xLine2Point"] = P(three.XLine2Point);
                    points["arcPoint"] = P(three.ArcPoint);
                    break;
                case DxfArcDimension arc:
                    points["center"] = P(arc.CenterPoint);
                    points["xLine1Point"] = P(arc.XLine1Point);
                    points["xLine2Point"] = P(arc.XLine2Point);
                    points["arcPoint"] = P(arc.ArcPoint);
                    break;
                case DxfOrdinateDimension ordinate:
                    points["origin"] = P(ordinate.Origin);
                    points["definingPoint"] = P(ordinate.DefiningPoint);
                    points["leaderEndPoint"] = P(ordinate.LeaderEndPoint);
                    break;
            }

            return points;
        }

        private static void Table(DxfTable table, Dictionary<string, object> d)
        {
            int rows = table.NumRows;
            int columns = table.NumColumns;
            d["style"] = table.TableStyleName;
            d["at"] = P(table.Position);
            d["rows"] = rows;
            d["columns"] = columns;
            var cells = new List<List<string>>();

            for (int r = 0; r < rows && r * Math.Max(columns, 1) < MostItems; r++)
            {
                var line = new List<string>();

                for (int c = 0; c < columns; c++)
                {
                    line.Add(table.TextString(r, c));
                }

                cells.Add(line);
            }

            d["cells"] = cells;
        }

        /// <summary>The attributes of a block reference as tag and value, in the order the reference holds them, none erased.</summary>
        public static List<DxfAttributeReference> Attributes(DxfBlockReference insert) =>
            insert.AttributeCollection.Where(a => !a.IsErased).ToList();

        private static double[] P(GeoPoint3 point) => new[] { R(point.X), R(point.Y) };

        private static double R(double value) => EntityDescriber.R(value);

        private static double R(double value, int places) => Math.Round(value, places);

        private static double Degrees(double radians) => EntityDescriber.R(radians * 180.0 / Math.PI);
    }
}
