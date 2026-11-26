using System;
using System.Collections.Generic;
using System.Linq;
using GeometryHelper;
using GeometryHelper.Dxf;
using GeometryHelper.Geometry;
using Xunit;
using static OpenCAD2D.UnitTest.EditingDrawings;
using static OpenCAD2D.UnitTest.ExportDrawings;
using static OpenCAD2D.UnitTest.ExportJsonTools;

namespace OpenCAD2D.UnitTest
{
    /// <summary>
    /// The drawing as JSON (CadDrawingJson): the document against its JSON Schema, the order and the numbers it is written
    /// in, the lengths in millimetres of whatever unit the drawing has, and what each kind of entity carries.
    /// </summary>
    public class ExportJsonTests
    {
        private static readonly Tolerance Mm = new Tolerance(1E-6, 1E-6);

        /// <summary>
        /// A drawing of one entity of every kind the export describes: a line, a circle, an arc whose normal points down, an
        /// ellipse, a closed polyline with a bulge, a spline, a point, a text, an MTEXT, a leader, a hatch, a solid, a 3D face
        /// and the dimension DIMLINEAR makes of a line, beside the layer, text style and linetype tables.
        /// </summary>
        private static CadDocument EveryKind()
        {
            CadDocument document = Blank();
            DxfDatabase db = document.Database;
            db.Insunits = DxfUnits.Millimeters;
            db.LayerTable.Add(new DxfLayerTableRecord { Name = "WALLS" });
            DxfBlockTableRecord space = db.ModelSpace;
            space.AppendEntity(new DxfLine(new GeoPoint3(0, 0, 0), new GeoPoint3(100, 0, 0)) { Layer = "WALLS" });
            space.AppendEntity(new DxfCircle(new GeoPoint3(50, 25, 0), new GeoVector3(0, 0, 1), 10.0));
            space.AppendEntity(new DxfArc(new GeoPoint3(200, 0, 0), new GeoVector3(0, 0, -1), 5.0, 0.0, Math.PI / 2));
            space.AppendEntity(
                new DxfEllipse(
                    new GeoPoint3(300, 0, 0),
                    new GeoVector3(0, 0, 1),
                    new GeoVector3(20, 0, 0),
                    0.5,
                    0.0,
                    Math.PI
                )
            );
            var closed = new DxfPolyline();
            closed.AddVertexAt(0, new GeoPoint2(0, 100), 0.0, 0.0, 0.0);
            closed.AddVertexAt(1, new GeoPoint2(40, 100), 0.5, 0.0, 0.0);
            closed.AddVertexAt(2, new GeoPoint2(40, 140), 0.0, 0.0, 0.0);
            closed.Closed = true;
            space.AppendEntity(closed);
            space.AppendEntity(
                new DxfSpline(
                    3,
                    false,
                    false,
                    false,
                    new[]
                    {
                        new GeoPoint3(0, 200, 0),
                        new GeoPoint3(10, 230, 0),
                        new GeoPoint3(30, 190, 0),
                        new GeoPoint3(50, 220, 0),
                    },
                    new double[] { 0, 0, 0, 0, 1, 1, 1, 1 },
                    new double[0],
                    1E-10,
                    1E-10
                )
            );
            space.AppendEntity(new DxfDBPoint(new GeoPoint3(5, 5, 0)));
            space.AppendEntity(
                new DxfDBText
                {
                    Position = new GeoPoint3(20, 20, 0),
                    TextString = "NOTE 1: ø16 \"quoted\"",
                    Height = 2.5,
                    Rotation = Math.PI / 6,
                }
            );
            space.AppendEntity(
                new DxfMText
                {
                    Location = new GeoPoint3(60, 60, 0),
                    Contents = "First line\\PSecond line",
                    TextHeight = 3.0,
                    Width = 40.0,
                }
            );
            var leader = new DxfLeader { HasArrowHead = true };
            leader.AppendVertex(new GeoPoint3(60, 25, 0));
            leader.AppendVertex(new GeoPoint3(80, 40, 0));
            leader.AppendVertex(new GeoPoint3(90, 40, 0));
            space.AppendEntity(leader);
            var hatch = new DxfHatch();
            hatch.AppendLoop(
                DxfHatchLoopTypes.External,
                new[] { new GeoPoint2(400, 0), new GeoPoint2(420, 0), new GeoPoint2(420, 10), new GeoPoint2(400, 10) },
                null
            );
            space.AppendEntity(hatch);
            space.AppendEntity(
                new DxfSolid(
                    new GeoPoint3(500, 0, 0),
                    new GeoPoint3(510, 0, 0),
                    new GeoPoint3(500, 10, 0),
                    new GeoPoint3(510, 10, 0)
                )
            );
            space.AppendEntity(
                new DxfFace(
                    new GeoPoint3(600, 0, 0),
                    new GeoPoint3(610, 0, 0),
                    new GeoPoint3(610, 10, 0),
                    new GeoPoint3(600, 10, 0),
                    true,
                    true,
                    true,
                    true
                )
            );
            Run(Session(document), "DIMLINEAR", P(0, -20), P(100, -20), P(50, -30));
            return document;
        }

        private static Dictionary<string, object> Written(CadDocument document, string layout = null) =>
            Obj(Parse(CadDrawingJson.Write(document, layout)));

        private static Dictionary<string, object> OfKind(Dictionary<string, object> json, string kind) =>
            Arr(json["entities"]).Select(Obj).First(e => (string)e["kind"] == kind);

        private static double[] Numbers(object array) => Arr(array).Select(n => (double)n).ToArray();

        [Fact]
        public void TheSchemaFile_IsJson_OfVersionOne_AndBuiltIntoTheAssembly()
        {
            Dictionary<string, object> schema = Obj(Parse(CadDrawingJson.Schema));

            Assert.Equal("https://json-schema.org/draft/2020-12/schema", schema["$schema"]);
            Assert.Equal("https://opencad2d.com/schemas/opencad2d-drawing-v1.schema.json", schema["$id"]);
            Assert.Equal(1.0, (double)Obj(Obj(schema["properties"])["version"])["const"]);
            Assert.Equal(CadDrawingJson.Version, (int)(double)Obj(Obj(schema["properties"])["version"])["const"]);
        }

        [Fact]
        public void ADrawingOfEveryKind_FitsItsSchema()
        {
            CadDocument document = EveryKind();
            object json = Parse(CadDrawingJson.Write(document, null));
            object schema = Parse(CadDrawingJson.Schema);

            List<string> problems = Validate(json, schema);

            Assert.Empty(problems);
            string[] kinds = Arr(Obj(json)["entities"]).Select(e => (string)Obj(e)["kind"]).ToArray();

            foreach (
                string expected in new[]
                {
                    "LINE",
                    "CIRCLE",
                    "ARC",
                    "ELLIPSE",
                    "LWPOLYLINE",
                    "SPLINE",
                    "POINT",
                    "TEXT",
                    "MTEXT",
                    "LEADER",
                    "HATCH",
                    "SOLID",
                    "3DFACE",
                    "DIMENSION",
                }
            )
            {
                Assert.Contains(expected, kinds);
            }
        }

        [Fact]
        public void ATreeOfBlocks_FitsItsSchema_WithEveryPieceKeepingItsPathAndMatrix()
        {
            CadDocument document = BlockTree(out DxfBlockReference[] _);
            object json = Parse(CadDrawingJson.Write(document, null));

            Assert.Empty(Validate(json, Parse(CadDrawingJson.Schema)));
            Dictionary<string, object> text = Arr(Obj(json)["entities"])
                .Select(Obj)
                .First(e => (string)e["kind"] == "TEXT");
            Assert.Equal(new object[] { "PLATE" }, Arr(text["path"]).ToArray());
            Assert.Equal(4, Arr(text["matrix"]).Count);
            Assert.Equal("PL-1", text["text"]);
        }

        [Fact]
        public void TheSchemaChecker_RefusesADocumentThatLacksOrBreaksWhatTheSchemaSays()
        {
            CadDocument document = EveryKind();
            Dictionary<string, object> json = Written(document);
            object schema = Parse(CadDrawingJson.Schema);

            Obj(Arr(json["entities"])[0]).Remove("layer");
            Obj(Arr(json["entities"])[1])["radius"] = "ten";
            Obj(Arr(json["entities"])[1]).Remove("center");
            json["version"] = 2.0;
            json["extra"] = true;
            List<string> problems = Validate(json, schema);

            Assert.Contains(problems, p => p.Contains("'layer' is missing"));
            Assert.Contains(problems, p => p.Contains("'center' is missing"));
            Assert.Contains(problems, p => p.Contains("$.version"));
            Assert.Contains(problems, p => p.Contains("'extra' is not in the schema"));
            Assert.Contains(problems, p => p.Contains("radius: expected number"));
        }

        [Fact]
        public void TheSameDrawing_GivesTheSameText_WhateverTheOrderItsTablesAreIn_AndAfterAReopen()
        {
            CadDocument document = EveryKind();
            string first = CadDrawingJson.Write(document, null);
            string second = CadDrawingJson.Write(document, null);
            CadDocument reopened = CadDocument.From(DxfDatabase.Parse(document.Database.ToText()), "plan.dxf");

            Assert.Equal(first, second);
            Assert.Equal(
                first.Replace("plan.dxf", "blank.dxf"),
                CadDrawingJson.Write(reopened, null).Replace("plan.dxf", "blank.dxf")
            );
            Assert.DoesNotContain("\r", first);
            Assert.False(first.Contains("E+") || first.Contains("e+") || first.Contains("NaN"));
        }

        [Fact]
        public void TheEntities_AreInTheOrderOfTheirHandles_AsNumbers()
        {
            CadDocument document = EveryKind();
            Dictionary<string, object> json = Written(document);

            long[] keys = Arr(json["entities"]).Select(e => CadFlatten.HandleKey((string)Obj(e)["handle"])).ToArray();

            Assert.Equal(keys.OrderBy(k => k), keys);
            Assert.True(keys.Length >= 14);
        }

        [Fact]
        public void ADrawingOfInches_IsWrittenInMillimetres_AndOneOfNoUnitIsLeftAsItIsAndSaidSo()
        {
            foreach (
                KeyValuePair<DxfUnits, double> unit in new Dictionary<DxfUnits, double>
                {
                    { DxfUnits.Inches, 25.4 },
                    { DxfUnits.Meters, 1000.0 },
                    { DxfUnits.Feet, 304.8 },
                    { DxfUnits.Centimeters, 10.0 },
                    { DxfUnits.Millimeters, 1.0 },
                    { DxfUnits.Undefined, 1.0 },
                }
            )
            {
                CadDocument document = Blank();
                document.Database.Insunits = unit.Key;
                document.Database.ModelSpace.AppendEntity(new DxfLine(new GeoPoint3(1, 2, 0), new GeoPoint3(11, 2, 0)));
                document.Database.ModelSpace.AppendEntity(
                    new DxfDBText
                    {
                        Position = new GeoPoint3(3, 4, 0),
                        TextString = "T",
                        Height = 0.5,
                    }
                );
                document.Database.ModelSpace.AppendEntity(
                    new DxfCircle(new GeoPoint3(5, 6, 0), new GeoVector3(0, 0, 1), 2.0)
                );

                Dictionary<string, object> json = Written(document);

                Dictionary<string, object> units = Obj(json["units"]);
                Assert.Equal(unit.Value, (double)units["toMillimetres"], 9);
                Assert.Equal(unit.Key != DxfUnits.Undefined, (bool)units["normalised"]);
                Dictionary<string, object> line = OfKind(json, "LINE");
                Assert.Equal(new[] { 1 * unit.Value, 2 * unit.Value }, Numbers(line["start"]), new DoubleComparer());
                Assert.Equal(new[] { 11 * unit.Value, 2 * unit.Value }, Numbers(line["end"]), new DoubleComparer());
                Assert.Equal(0.5 * unit.Value, (double)OfKind(json, "TEXT")["height"], 5);
                Assert.Equal(2.0 * unit.Value, (double)OfKind(json, "CIRCLE")["radius"], 5);
                Assert.Equal(1 * unit.Value, Numbers(line["box"])[0], 5);
            }
        }

        [Fact]
        public void ALayoutOfInches_IsWrittenInMillimetres_WhateverTheDrawingsOwnUnitIs()
        {
            CadDocument document = Blank();
            document.Database.Insunits = DxfUnits.Meters;
            AddLineOnSheet(document, out CadSpace sheet);
            sheet.Layout.PlotPaperUnits = DxfPlotPaperUnit.Inches;

            Dictionary<string, object> json = Written(document);

            Dictionary<string, object> paper = Arr(json["layouts"])
                .Select(Obj)
                .Single(l => (string)l["name"] == sheet.Name);
            Assert.Equal(25.4, (double)paper["toMillimetres"], 9);
            Assert.Equal(1000.0, (double)Obj(Arr(json["layouts"])[0])["toMillimetres"], 9);
            Dictionary<string, object> line = OfKind(json, "LINE");
            Assert.Equal(2540.0, Numbers(line["end"])[0], 6);
        }

        [Fact]
        public void AnArcOfANormalPointingDown_IsWrittenAsItIsSeenFromAbove()
        {
            Dictionary<string, object> json = Written(EveryKind());

            Dictionary<string, object> arc = OfKind(json, "ARC");

            // From 0 to 90 degrees about the normal (0, 0, -1) is, seen from above, from 90 to 180 degrees (x runs the other way).
            Assert.Equal(new[] { 200.0, 0.0 }, Numbers(arc["center"]));
            Assert.Equal(5.0, (double)arc["radius"], 9);
            Assert.Equal(90.0, (double)arc["startAngle"], 6);
            Assert.Equal(180.0, (double)arc["endAngle"], 6);
        }

        [Fact]
        public void AnArcAndABulgeInAMirroredBlock_AreWrittenWhereTheyAreDrawn()
        {
            CadDocument document = BlockTree(out DxfBlockReference[] inserts);
            string mirrored = inserts[1].Handle;
            CadFlatDrawing flat = CadFlatten.Walk(document, null);
            Dictionary<string, object> json = Obj(Parse(CadDrawingJson.Write(flat, document)));
            List<Dictionary<string, object>> written = Arr(json["entities"]).Select(Obj).ToList();

            // The arc: the points of its start and end angles are the ends the library gives it, in either order.
            int arcAt = flat.Entities.ToList().FindIndex(f => f.Handle == mirrored && f.Entity is DxfArc);
            var arc = (DxfArc)flat.Entities[arcAt].Entity;
            Dictionary<string, object> arcJson = written[arcAt];
            double[] centre = Numbers(arcJson["center"]);
            double radius = (double)arcJson["radius"];
            GeoPoint3 start = new GeoPoint3(
                centre[0] + radius * Math.Cos((double)arcJson["startAngle"] * Degree),
                centre[1] + radius * Math.Sin((double)arcJson["startAngle"] * Degree),
                0
            );
            GeoPoint3 end = new GeoPoint3(
                centre[0] + radius * Math.Cos((double)arcJson["endAngle"] * Degree),
                centre[1] + radius * Math.Sin((double)arcJson["endAngle"] * Degree),
                0
            );
            Assert.True(
                (start.IsEqualTo(arc.StartPoint, Mm) && end.IsEqualTo(arc.EndPoint, Mm))
                    || (start.IsEqualTo(arc.EndPoint, Mm) && end.IsEqualTo(arc.StartPoint, Mm)),
                start + " " + end + " against " + arc.StartPoint + " " + arc.EndPoint
            );
            Assert.Equal(arc.Center.X, centre[0], 6);
            Assert.Equal(arc.Radius, radius, 6);

            // The polyline: the middle of its bulged side, found from the vertices and the bulge, is the middle the library draws.
            int polylineAt = flat.Entities.ToList().FindIndex(f => f.Handle == mirrored && f.Entity is DxfPolyline);
            var polyline = (DxfPolyline)flat.Entities[polylineAt].Entity;
            double[] a = Numbers(Arr(written[polylineAt]["vertices"])[1]);
            double[] b = Numbers(Arr(written[polylineAt]["vertices"])[2]);
            double bulge = Numbers(written[polylineAt]["bulges"])[1];
            double chord = Math.Sqrt((b[0] - a[0]) * (b[0] - a[0]) + (b[1] - a[1]) * (b[1] - a[1]));
            double sagitta = bulge * chord / 2.0;
            double mx = (a[0] + b[0]) / 2.0 - (-(b[1] - a[1]) / chord) * sagitta;
            double my = (a[1] + b[1]) / 2.0 - ((b[0] - a[0]) / chord) * sagitta;
            GeoPoint3 middle = polyline.GetPointAtParameter(1.5);
            Assert.Equal(middle.X, mx, 6);
            Assert.Equal(middle.Y, my, 6);
        }

        [Fact]
        public void TheKindsCarryTheirGeometry_InMillimetres()
        {
            Dictionary<string, object> json = Written(EveryKind());

            Dictionary<string, object> ellipse = OfKind(json, "ELLIPSE");
            Assert.Equal(new[] { 20.0, 0.0 }, Numbers(ellipse["majorAxis"]));
            Assert.Equal(new[] { 0.0, 10.0 }, Numbers(ellipse["minorAxis"]));
            Assert.Equal(0.0, (double)ellipse["startParam"], 6);
            Assert.Equal(Math.PI, (double)ellipse["endParam"], 6);
            Assert.False((bool)ellipse["closed"]);

            Dictionary<string, object> polyline = OfKind(json, "LWPOLYLINE");
            Assert.Equal(3, Arr(polyline["vertices"]).Count);
            Assert.Equal(new[] { 0.0, 0.5, 0.0 }, Numbers(polyline["bulges"]));
            Assert.True((bool)polyline["closed"]);

            Dictionary<string, object> spline = OfKind(json, "SPLINE");
            Assert.Equal(3, (int)(double)spline["degree"]);
            Assert.Equal(4, Arr(spline["controlPoints"]).Count);
            Assert.Equal(8, Arr(spline["knots"]).Count);

            Dictionary<string, object> text = OfKind(json, "TEXT");
            Assert.Equal("NOTE 1: ø16 \"quoted\"", text["text"]);
            Assert.Equal(30.0, (double)text["rotation"], 6);
            Assert.Equal(2.5, (double)text["height"], 9);

            Dictionary<string, object> mtext = OfKind(json, "MTEXT");
            Assert.Equal("First line\nSecond line", ((string)mtext["text"]).Replace("\r", string.Empty));
            Assert.Equal("First line\\PSecond line", mtext["contents"]);
            Assert.Equal(40.0, (double)mtext["width"], 9);

            Dictionary<string, object> leader = OfKind(json, "LEADER");
            Assert.Equal(3, Arr(leader["vertices"]).Count);
            Assert.True((bool)leader["arrow"]);

            Dictionary<string, object> hatch = OfKind(json, "HATCH");
            Assert.True((bool)hatch["solid"]);
            Assert.Equal(4, Arr(Arr(hatch["loops"])[0]).Count);

            Dictionary<string, object> dimension = OfKind(json, "DIMENSION");
            Assert.Equal("linear", dimension["dimType"]);
            Assert.Equal(100.0, (double)dimension["measurement"], 6);
            Assert.Equal(new[] { 0.0, -20.0 }, Numbers(Obj(dimension["points"])["xLine1"]));
            Assert.Equal(new[] { 100.0, -20.0 }, Numbers(Obj(dimension["points"])["xLine2"]));

            Assert.Equal(new[] { 5.0, 5.0 }, Numbers(OfKind(json, "POINT")["position"]));
            Assert.Equal(4, Arr(OfKind(json, "SOLID")["vertices"]).Count);
            Assert.Equal(4, Arr(OfKind(json, "3DFACE")["vertices"]).Count);
        }

        [Fact]
        public void TheTables_AreListedByName_WithTheirLookups()
        {
            Dictionary<string, object> json = Written(EveryKind());

            string[] layers = Arr(json["layers"]).Select(l => (string)Obj(l)["name"]).ToArray();

            Assert.Contains("0", layers);
            Assert.Contains("WALLS", layers);
            Assert.Equal(
                layers.OrderBy(n => n, StringComparer.OrdinalIgnoreCase).ThenBy(n => n, StringComparer.Ordinal),
                layers
            );
            Assert.NotEmpty(Arr(json["textStyles"]));
            Assert.NotEmpty(Arr(json["linetypes"]));
            Assert.NotEmpty(Arr(json["dimStyles"]));
            Assert.Equal("WALLS", OfKind(json, "LINE")["layer"]);
            Assert.Equal("Model", OfKind(json, "LINE")["layout"]);
        }

        [Fact]
        public void TheReport_ListsWhatTheWalkLeftAlone()
        {
            CadDocument document = BlockTree(out DxfBlockReference[] _);
            document.Database.BlockTable["BOLT"].Units = DxfUnits.Inches;

            Dictionary<string, object> report = Obj(Written(document)["report"]);

            // Nine references in all (PLATE, BOLT and HEAD of each plate), one fewer because under the scale of 3 by 1.5 of the
            // third plate one of them cannot hold the placing and is exploded in its turn.
            Assert.Equal(8, (int)(double)report["inserts"]);
            Assert.Single(Arr(report["blockUnits"]));
            Assert.Empty(Arr(report["xrefs"]));
        }

        [Fact]
        public void ADrawingThatCannotBeRead_IsRefusedInWords()
        {
            Assert.Throws<ArgumentNullException>(() => CadDrawingJson.Write((CadDocument)null, null));
            Assert.Throws<ArgumentNullException>(() => CadDrawingJson.Write((CadFlatDrawing)null, Blank()));
            Assert.Throws<ArgumentException>(() => CadDrawingJson.Write(Blank(), "Nowhere"));
        }

        private sealed class DoubleComparer : IEqualityComparer<double>
        {
            public bool Equals(double x, double y) => Math.Abs(x - y) <= 1E-6;

            public int GetHashCode(double value) => 0;
        }
    }
}
