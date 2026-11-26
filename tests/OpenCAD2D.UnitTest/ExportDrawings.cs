using System;
using System.Collections.Generic;
using System.Linq;
using GeometryHelper;
using GeometryHelper.Dxf;
using GeometryHelper.Geometry;
using static OpenCAD2D.UnitTest.EditingDrawings;

namespace OpenCAD2D.UnitTest
{
    /// <summary>The drawings of the export tests (flatten, JSON, graph), built in code.</summary>
    internal static class ExportDrawings
    {
        public const double Degree = Math.PI / 180.0;

        /// <summary>
        /// A drawing of millimetres with a tree of blocks: HEAD (a circle of radius 2 at the origin and a line from (0, 0) to
        /// (10, 0), ByBlock in colour), BOLT (a line from (0, 0) to (30, 0), an arc, a polyline with a bulge and HEAD inserted at
        /// (20, 5) turned 30 degrees and scaled 1 by 2), PLATE (BOLT inserted at (50, 0) turned 90 degrees, and a text). Model
        /// space holds PLATE as it is at (100, 100), turned 20 degrees and scaled 2; PLATE mirrored in x at (300, 100); PLATE
        /// scaled 3 by 1.5 at (500, 100); and one line.
        /// </summary>
        public static CadDocument BlockTree(out DxfBlockReference[] inserts)
        {
            var db = new DxfDatabase("AC1027");
            db.Insunits = DxfUnits.Millimeters;
            db.LayerTable.Add(new DxfLayerTableRecord { Name = "WALLS" });
            db.LayerTable.Add(new DxfLayerTableRecord { Name = "BOLTS" });

            var head = new DxfBlockTableRecord { Name = "HEAD", Origin = new GeoPoint3(0, 0, 0) };
            db.BlockTable.Add(head);
            head.AppendEntity(
                new DxfCircle(new GeoPoint3(0, 0, 0), new GeoVector3(0, 0, 1), 2.0) { Color = DxfColor.ByBlock }
            );
            head.AppendEntity(new DxfLine(new GeoPoint3(0, 0, 0), new GeoPoint3(10, 0, 0)));

            var bolt = new DxfBlockTableRecord { Name = "BOLT", Origin = new GeoPoint3(0, 0, 0) };
            db.BlockTable.Add(bolt);
            bolt.AppendEntity(new DxfLine(new GeoPoint3(0, 0, 0), new GeoPoint3(30, 0, 0)));
            bolt.AppendEntity(new DxfArc(new GeoPoint3(10, 10, 0), new GeoVector3(0, 0, 1), 4.0, 0.5, 2.0));
            var polyline = new DxfPolyline();
            polyline.AddVertexAt(0, new GeoPoint2(0, 0), 0.0, 0.0, 0.0);
            polyline.AddVertexAt(1, new GeoPoint2(8, 0), 0.5, 0.0, 0.0);
            polyline.AddVertexAt(2, new GeoPoint2(8, 6), 0.0, 0.0, 0.0);
            bolt.AppendEntity(polyline);
            bolt.AppendEntity(
                new DxfBlockReference(new GeoPoint3(20, 5, 0), head)
                {
                    Rotation = 30 * Degree,
                    ScaleFactors = new GeoVector3(1, 2, 1),
                    Layer = "BOLTS",
                    Color = DxfColor.ByBlock,
                }
            );

            var plate = new DxfBlockTableRecord { Name = "PLATE", Origin = new GeoPoint3(0, 0, 0) };
            db.BlockTable.Add(plate);
            plate.AppendEntity(
                new DxfBlockReference(new GeoPoint3(50, 0, 0), bolt)
                {
                    Rotation = 90 * Degree,
                    Color = DxfColor.ByBlock,
                }
            );
            plate.AppendEntity(
                new DxfDBText
                {
                    Position = new GeoPoint3(5, 5, 0),
                    TextString = "PL-1",
                    Height = 2.5,
                }
            );

            DxfBlockReference first = new DxfBlockReference(new GeoPoint3(100, 100, 0), plate)
            {
                Rotation = 20 * Degree,
                ScaleFactors = new GeoVector3(2, 2, 2),
                Layer = "WALLS",
                Color = DxfColor.FromColorIndex(DxfColorMethod.ByAci, 1),
            };
            var mirrored = new DxfBlockReference(new GeoPoint3(300, 100, 0), plate)
            {
                ScaleFactors = new GeoVector3(-1, 1, 1),
                Layer = "WALLS",
            };
            var stretched = new DxfBlockReference(new GeoPoint3(500, 100, 0), plate)
            {
                ScaleFactors = new GeoVector3(3, 1.5, 1),
                Layer = "WALLS",
            };

            foreach (DxfBlockReference insert in new[] { first, mirrored, stretched })
            {
                db.ModelSpace.AppendEntity(insert);
            }

            db.ModelSpace.AppendEntity(new DxfLine(new GeoPoint3(0, 0, 0), new GeoPoint3(1000, 0, 0)));
            inserts = new[] { first, mirrored, stretched };
            return CadDocument.From(db, "tree.dxf");
        }

        /// <summary>Explodes every block reference that is not an array, again and again, with the EXPLODE command, until none is left.</summary>
        public static void ExplodeAll(CadDocument document)
        {
            for (int round = 0; round < 12; round++)
            {
                string[] inserts = All<DxfBlockReference>(document.ModelSpace)
                    .Where(i => !(i is DxfMInsertBlock))
                    .Select(i => i.Handle)
                    .ToArray();

                if (inserts.Length == 0)
                {
                    return;
                }

                Run(Session(document), "EXPLODE", inserts, Enter);
            }
        }

        /// <summary>One entity as the round trip compares it: its kind, and the box of its extents.</summary>
        public static string Signature(DxfEntity entity)
        {
            GeoAabb3 box = entity.GeometricExtents;
            return string.Join(
                " ",
                entity.DxfName,
                Math.Round(box.Min.X, 3).ToString("0.###", System.Globalization.CultureInfo.InvariantCulture),
                Math.Round(box.Min.Y, 3).ToString("0.###", System.Globalization.CultureInfo.InvariantCulture),
                Math.Round(box.Max.X, 3).ToString("0.###", System.Globalization.CultureInfo.InvariantCulture),
                Math.Round(box.Max.Y, 3).ToString("0.###", System.Globalization.CultureInfo.InvariantCulture)
            );
        }

        /// <summary>The boxes of entities in a canonical order: by kind, then by corner, rounded to a thousandth of a millimetre.</summary>
        public static List<KeyValuePair<string, GeoAabb3>> Boxes(IEnumerable<DxfEntity> entities) =>
            entities
                .Where(e => !(e is DxfAttributeDefinition))
                .Select(e => new KeyValuePair<string, GeoAabb3>(Signature(e), e.GeometricExtents))
                .OrderBy(p => p.Key, StringComparer.Ordinal)
                .ToList();
    }
}
