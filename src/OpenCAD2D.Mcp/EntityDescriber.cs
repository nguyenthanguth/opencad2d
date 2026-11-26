using System;
using System.Collections.Generic;
using GeometryHelper.Dxf;

namespace OpenCAD2D.Mcp
{
    /// <summary>One entity as <c>query_entities</c> gives it: the handle, the kind, the layer and the key geometry.</summary>
    internal sealed class EntityInfo
    {
        /// <summary>Gets or sets the handle, as <c>2F</c>.</summary>
        public string Handle { get; set; }

        /// <summary>Gets or sets the DXF name of the kind: LINE, CIRCLE, ARC, LWPOLYLINE, TEXT, MTEXT, DIMENSION, INSERT, HATCH.</summary>
        public string Kind { get; set; }

        public string Layer { get; set; }

        /// <summary>Gets or sets the key numbers of the kind, in millimetres and degrees; empty for a kind this does not describe.</summary>
        public Dictionary<string, object> Geometry { get; set; }

        /// <summary>Gets or sets the box the entity takes as it is drawn, <c>[minX, minY, maxX, maxY]</c>; null where it draws nothing.</summary>
        public double[] Box { get; set; }
    }

    /// <summary>Gives the key geometry of an entity, in plain numbers an agent reads, to 0.0001 of a millimetre.</summary>
    internal static class EntityDescriber
    {
        /// <summary>The most corners of a polyline given in full.</summary>
        public const int MostVertices = 24;

        public static EntityInfo Describe(EntityRef item)
        {
            DxfEntity entity = item.Entity;
            return new EntityInfo
            {
                Handle = item.Handle,
                Kind = entity.DxfName,
                Layer = entity.Layer,
                Geometry = Geometry(entity),
            };
        }

        private static Dictionary<string, object> Geometry(DxfEntity entity)
        {
            var g = new Dictionary<string, object>();

            switch (entity)
            {
                case DxfLine line:
                    g["start"] = Point(line.StartPoint.X, line.StartPoint.Y);
                    g["end"] = Point(line.EndPoint.X, line.EndPoint.Y);
                    break;
                case DxfCircle circle:
                    g["center"] = Point(circle.Center.X, circle.Center.Y);
                    g["radius"] = R(circle.Radius);
                    break;
                case DxfArc arc:
                    g["center"] = Point(arc.Center.X, arc.Center.Y);
                    g["radius"] = R(arc.Radius);
                    g["startAngle"] = R(arc.StartAngle * 180.0 / Math.PI);
                    g["endAngle"] = R(arc.EndAngle * 180.0 / Math.PI);
                    break;
                case DxfPolyline polyline:
                    int count = polyline.NumberOfVertices;
                    var corners = new List<double[]>();

                    for (int i = 0; i < count && i < MostVertices; i++)
                    {
                        var p = polyline.GetPoint2dAt(i);
                        corners.Add(Point(p.X, p.Y));
                    }

                    g["closed"] = polyline.Closed;
                    g["vertexCount"] = count;
                    g["vertices"] = corners;
                    break;
                case DxfDBText text:
                    g["text"] = text.TextString;
                    g["at"] = Point(text.Position.X, text.Position.Y);
                    g["height"] = R(text.Height);
                    g["rotation"] = R(text.Rotation * 180.0 / Math.PI);
                    g["style"] = text.TextStyleName;
                    break;
                case DxfMText mtext:
                    g["text"] = mtext.Contents;
                    g["at"] = Point(mtext.Location.X, mtext.Location.Y);
                    g["height"] = R(mtext.TextHeight);
                    g["width"] = R(mtext.Width);
                    g["style"] = mtext.TextStyleName;
                    break;
                case DxfDimension dimension:
                    g["type"] = dimension.GetType().Name.Replace("Dxf", string.Empty);
                    g["style"] = dimension.DimensionStyleName;
                    g["textOverride"] = dimension.DimensionText;
                    break;
                case DxfBlockReference insert:
                    g["block"] = insert.Name;
                    g["at"] = Point(insert.Position.X, insert.Position.Y);
                    g["scale"] = R(insert.ScaleFactors.X);
                    g["rotation"] = R(insert.Rotation * 180.0 / Math.PI);
                    break;
                case DxfHatch hatch:
                    g["pattern"] = hatch.PatternName;
                    break;
            }

            return g;
        }

        private static double[] Point(double x, double y) => new[] { R(x), R(y) };

        /// <summary>A number to 0.0001: a tenth of a micrometre, below any tolerance of a drawing in millimetres.</summary>
        public static double R(double value) => Math.Round(value, 4);
    }
}
