using System.IO;
using GeometryHelper.Dxf;
using GeometryHelper.Geometry;

namespace OpenCAD2D.Mcp.UnitTest
{
    /// <summary>The drawings the query tests read, built in code.</summary>
    internal static class QueryDrawings
    {
        /// <summary>
        /// A sheet with a plate and a title block. Model space: a closed polyline 200 by 100 on layer Plate; a circle of radius 20 at
        /// (100, 50) on Holes; a line (0, 50) to (200, 50) on Plate in colour 1; a TEXT "PLATE" 5 high at (10, 10) on Notes; an MTEXT
        /// 80 wide of two lines at (10, 150) on Notes; an open polyline from (0, 200) to (100, 200) with a bulge of 1 (a half circle
        /// of diameter 100) on Plate; and an INSERT of the block TITLE at (300, 0) on Sheet with the attributes TITLE "PLATE 10 THK"
        /// and NUMBER "A-101". The block holds a line 60 long and the two attribute definitions. The layers have colours 3 (Plate),
        /// 5 (Holes), 2 (Notes) and 7 (Sheet).
        /// </summary>
        public static string Sheet(string folder)
        {
            var database = new DxfDatabase("AC1027");
            var outline = new DxfPolyline();
            outline.AddVertexAt(0, new GeoPoint2(0, 0), 0, 0, 0);
            outline.AddVertexAt(1, new GeoPoint2(200, 0), 0, 0, 0);
            outline.AddVertexAt(2, new GeoPoint2(200, 100), 0, 0, 0);
            outline.AddVertexAt(3, new GeoPoint2(0, 100), 0, 0, 0);
            outline.Closed = true;
            outline.Layer = "Plate";
            database.ModelSpace.AppendEntity(outline);
            database.ModelSpace.AppendEntity(
                new DxfCircle
                {
                    Center = new GeoPoint3(100, 50, 0),
                    Radius = 20,
                    Layer = "Holes",
                }
            );
            database.ModelSpace.AppendEntity(
                new DxfLine(new GeoPoint3(0, 50, 0), new GeoPoint3(200, 50, 0)) { Layer = "Plate", ColorIndex = 1 }
            );
            database.ModelSpace.AppendEntity(
                new DxfDBText
                {
                    TextString = "PLATE",
                    Position = new GeoPoint3(10, 10, 0),
                    Height = 5,
                    Layer = "Notes",
                }
            );
            database.ModelSpace.AppendEntity(
                new DxfMText
                {
                    Contents = "First line\\PSecond line",
                    Location = new GeoPoint3(10, 150, 0),
                    TextHeight = 3.5,
                    Width = 80,
                    Layer = "Notes",
                }
            );
            var arc = new DxfPolyline();
            arc.AddVertexAt(0, new GeoPoint2(0, 200), 1.0, 0, 0);
            arc.AddVertexAt(1, new GeoPoint2(100, 200), 0, 0, 0);
            arc.Layer = "Plate";
            database.ModelSpace.AppendEntity(arc);

            var block = new DxfBlockTableRecord { Name = "TITLE" };
            database.BlockTable.Add(block);
            block.AppendEntity(new DxfLine(new GeoPoint3(0, 0, 0), new GeoPoint3(60, 0, 0)));
            var title = new DxfAttributeDefinition(new GeoPoint3(1, 2, 0), "?", "TITLE", "Drawing title", null);
            var number = new DxfAttributeDefinition(new GeoPoint3(1, 6, 0), "?", "NUMBER", "Drawing number", null);
            block.AppendEntity(title);
            block.AppendEntity(number);
            var insert = new DxfBlockReference(new GeoPoint3(300, 0, 0), block) { Layer = "Sheet" };
            database.ModelSpace.AppendEntity(insert);

            foreach (
                (DxfAttributeDefinition definition, string value) in new[]
                {
                    (title, "PLATE 10 THK"),
                    (number, "A-101"),
                }
            )
            {
                var attribute = new DxfAttributeReference();
                attribute.SetAttributeFromBlock(definition, insert.BlockTransform);
                attribute.TextString = value;
                insert.AttributeCollection.AppendAttribute(attribute);
            }

            CadDocument document = CadDocument.From(database);

            foreach ((string name, int color) in new[] { ("Plate", 3), ("Holes", 5), ("Notes", 2), ("Sheet", 7) })
            {
                CadLayerEditor.Add(document, name);
                CadLayerEditor.SetColor(document, name, color);
            }

            string path = Path.Combine(folder, "sheet.dxf");
            document.SaveAs(path);
            return path;
        }

        /// <summary>A drawing of 600 short lines on layer Rows, 300 texts on layer Words and 1 circle: enough for two pages of 500.</summary>
        public static string Many(string folder)
        {
            var database = new DxfDatabase("AC1027");

            for (int i = 0; i < 600; i++)
            {
                database.ModelSpace.AppendEntity(
                    new DxfLine(new GeoPoint3(i * 10, 0, 0), new GeoPoint3(i * 10 + 5, 0, 0)) { Layer = "Rows" }
                );
            }

            for (int i = 0; i < 300; i++)
            {
                database.ModelSpace.AppendEntity(
                    new DxfDBText
                    {
                        TextString = "W" + i,
                        Position = new GeoPoint3(i * 10, 20, 0),
                        Height = 2,
                        Layer = "Words",
                    }
                );
            }

            database.ModelSpace.AppendEntity(
                new DxfCircle
                {
                    Center = new GeoPoint3(0, 100, 0),
                    Radius = 5,
                    Layer = "Rows",
                }
            );
            string path = Path.Combine(folder, "many.dxf");
            CadDocument.From(database).SaveAs(path);
            return path;
        }
    }
}
