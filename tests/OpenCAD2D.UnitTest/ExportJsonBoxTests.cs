using System;
using System.Collections.Generic;
using System.Linq;
using GeometryHelper.Dxf;
using GeometryHelper.Geometry;
using Xunit;
using static OpenCAD2D.UnitTest.ExportDrawings;
using static OpenCAD2D.UnitTest.ExportJsonTools;
using static OpenCAD2D.UnitTest.SpatialDrawings;

namespace OpenCAD2D.UnitTest
{
    /// <summary>
    /// The box of an entity in the JSON export (and the node of the graph): the box as the entity is drawn
    /// (<see cref="CadSpatial.Bounds(CadDocument, CadSpace)"/>), the letters of a word and the width of a polyline in it; the
    /// extents of the library only for what the display list holds no box of.
    /// </summary>
    public sealed class ExportJsonBoxTests : IDisposable
    {
        private const double Within = 1E-6;

        private readonly SpatialDrawings _drawings = new SpatialDrawings();

        public void Dispose() => _drawings.Dispose();

        private static double[] BoxIn(Dictionary<string, object> json, string handle) =>
            Arr(Arr(json["entities"]).Select(Obj).Single(e => (string)e["handle"] == handle)["box"])
                .Select(n => (double)n)
                .ToArray();

        private static double[] Drawn(CadDocument document, string handle)
        {
            CadBoundsTable table = CadSpatial.Bounds(document, document.ModelSpace);
            int row = Array.IndexOf(table.Handles, handle);
            Assert.True(row >= 0, "The space draws no entity " + handle);
            DisplayBox box = table.BoxOf(row);
            return new[] { box.MinX, box.MinY, box.MaxX, box.MaxY };
        }

        private static void Near(double[] expected, double[] actual)
        {
            Assert.Equal(expected.Length, actual.Length);

            for (int i = 0; i < expected.Length; i++)
            {
                Assert.InRange(actual[i], expected[i] - Within, expected[i] + Within);
            }
        }

        [Fact]
        public void ATextsBox_IsTheBoxOfItsLetters_AsTheSpatialBoundsGiveIt()
        {
            CadDocument document = _drawings.Blank();
            document.Database.Insunits = DxfUnits.Millimeters;
            DxfDBText text = Put(document, Text("SIDE FOOD", 20, 30, 0.0, 5.0));
            DxfDBText turned = Put(document, Text("SIDE", 100, 30, 30.0, 5.0));

            Dictionary<string, object> json = Obj(Parse(CadDrawingJson.Write(document, null)));

            Near(Drawn(document, text.Handle), BoxIn(json, text.Handle));
            Near(Drawn(document, turned.Handle), BoxIn(json, turned.Handle));
        }

        [Fact]
        public void AWidePolylinesBox_IncludesItsWidth_AsTheSpatialBoundsGiveIt()
        {
            CadDocument document = _drawings.Blank();
            document.Database.Insunits = DxfUnits.Millimeters;
            var wide = new DxfPolyline();
            wide.AddVertexAt(0, new GeoPoint2(0, 0), 0.0, 4.0, 4.0);
            wide.AddVertexAt(1, new GeoPoint2(100, 0), 0.0, 4.0, 4.0);
            document.Database.ModelSpace.AppendEntity(wide);

            Dictionary<string, object> json = Obj(Parse(CadDrawingJson.Write(document, null)));

            double[] box = BoxIn(json, wide.Handle);
            Near(Drawn(document, wide.Handle), box);
            Assert.InRange(box[3] - box[1], 4.0 - Within, 4.0 + Within);
        }

        [Fact]
        public void ADimensionsBox_IsTheBoxOfItsPicture_AsTheSpatialBoundsGiveIt()
        {
            CadDocument document = _drawings.Blank();
            document.Database.Insunits = DxfUnits.Millimeters;
            CadCommandSession session = EditingDrawings.Session(document);
            EditingDrawings.Run(
                session,
                "DIMLINEAR",
                EditingDrawings.P(0, 0),
                EditingDrawings.P(100, 0),
                EditingDrawings.P(50, -20)
            );
            DxfDimension dimension = document.Database.ModelSpace.OfType<DxfDimension>().Single();

            Dictionary<string, object> json = Obj(Parse(CadDrawingJson.Write(document, null)));

            Near(Drawn(document, dimension.Handle), BoxIn(json, dimension.Handle));
        }

        [Fact]
        public void APieceOfABlock_KeepsTheExtentsOfTheLibrary_ForTheDisplayListHoldsNoBoxOfItsOwn()
        {
            CadDocument document = BlockTree(out DxfBlockReference[] _);
            CadFlatDrawing flat = CadFlatten.Walk(document, null);
            CadFlatEntity piece = flat.Entities.First(e => e.Path.Count > 0 && e.Entity is DxfLine);

            Dictionary<string, object> json = Obj(Parse(CadDrawingJson.Write(flat, document)));

            GeoAabb3 extents = piece.Entity.GeometricExtents;
            var index = flat.Entities.ToList().IndexOf(piece);
            double[] box = Arr(Obj(Arr(json["entities"])[index])["box"]).Select(n => (double)n).ToArray();
            Near(
                new[]
                {
                    extents.Min.X * piece.Scale,
                    extents.Min.Y * piece.Scale,
                    extents.Max.X * piece.Scale,
                    extents.Max.Y * piece.Scale,
                },
                box
            );
        }

        [Fact]
        public void AnEntityOnAFrozenLayer_KeepsItsExtents_ItsBoxIsNotLost()
        {
            CadDocument document = _drawings.Blank();
            document.Database.Insunits = DxfUnits.Millimeters;
            document.Database.LayerTable.Add(new DxfLayerTableRecord { Name = "HIDDEN", IsFrozen = true });
            var line = new DxfLine(new GeoPoint3(10, 20, 0), new GeoPoint3(60, 20, 0)) { Layer = "HIDDEN" };
            document.Database.ModelSpace.AppendEntity(line);

            Dictionary<string, object> json = Obj(Parse(CadDrawingJson.Write(document, null)));

            Near(new[] { 10.0, 20.0, 60.0, 20.0 }, BoxIn(json, line.Handle));
        }

        [Fact]
        public void TheGraphsNodeBox_IsTheSameBoxAsTheJsonsEntity()
        {
            CadDocument document = _drawings.Blank();
            document.Database.Insunits = DxfUnits.Millimeters;
            DxfDBText text = Put(document, Text("SIDE FOOD", 20, 30, 0.0, 5.0));
            var wide = new DxfPolyline();
            wide.AddVertexAt(0, new GeoPoint2(0, 0), 0.0, 4.0, 4.0);
            wide.AddVertexAt(1, new GeoPoint2(100, 0), 0.0, 4.0, 4.0);
            document.Database.ModelSpace.AppendEntity(wide);

            Dictionary<string, object> drawing = Obj(Parse(CadDrawingJson.Write(document, null)));
            Dictionary<string, object> graph = Obj(Parse(CadDrawingGraph.Build(document, null, 0.001).ToJson()));

            foreach (DxfEntity entity in new DxfEntity[] { text, wide })
            {
                double[] node = Arr(
                        Arr(graph["nodes"]).Select(Obj).Single(n => (string)n["handle"] == entity.Handle)["box"]
                    )
                    .Select(n => (double)n)
                    .ToArray();
                Near(BoxIn(drawing, entity.Handle), node);
            }
        }
    }
}
