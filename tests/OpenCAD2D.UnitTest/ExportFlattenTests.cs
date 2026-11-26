using System;
using System.Collections.Generic;
using System.Linq;
using GeometryHelper;
using GeometryHelper.Dxf;
using GeometryHelper.Geometry;
using Xunit;
using static OpenCAD2D.UnitTest.EditingDrawings;
using static OpenCAD2D.UnitTest.ExportDrawings;

namespace OpenCAD2D.UnitTest
{
    /// <summary>
    /// Walking the blocks of a drawing down to the shapes they draw (CadFlatten): nested, turned, scaled, mirrored and arrayed
    /// block references with their paths and transformations, the look they hand down, attributes, dynamic blocks and clips, the
    /// drawing left as it was, and the same shapes as the EXPLODE command gives.
    /// </summary>
    public class ExportFlattenTests
    {
        private static readonly Tolerance Mm = new Tolerance(1E-6, 1E-6);

        private static DxfBlockTableRecord NewBlock(DxfDatabase db, string name)
        {
            var block = new DxfBlockTableRecord { Name = name, Origin = new GeoPoint3(0, 0, 0) };
            db.BlockTable.Add(block);
            return block;
        }

        [Fact]
        public void ALineInABlockInABlock_IsWhereTheNestPutsIt_WithItsPathAndTheComposedMatrix()
        {
            var db = new DxfDatabase("AC1027");
            DxfBlockTableRecord bolt = NewBlock(db, "BOLT");
            bolt.AppendEntity(new DxfLine(new GeoPoint3(0, 0, 0), new GeoPoint3(30, 0, 0)));
            DxfBlockTableRecord plate = NewBlock(db, "PLATE");
            plate.AppendEntity(new DxfBlockReference(new GeoPoint3(50, 0, 0), bolt) { Rotation = 90 * Degree });
            var top = new DxfBlockReference(new GeoPoint3(100, 100, 0), plate);
            db.ModelSpace.AppendEntity(top);
            CadDocument document = CadDocument.From(db, "nest.dxf");

            CadFlatDrawing flat = CadFlatten.Walk(document, null);

            CadFlatEntity line = Assert.Single(flat.Entities);
            DxfLine world = Assert.IsType<DxfLine>(line.Entity);
            Near(new GeoPoint3(150, 100, 0), world.StartPoint, 1E-9);
            Near(new GeoPoint3(150, 130, 0), world.EndPoint, 1E-9);
            Assert.Equal(new[] { "PLATE", "BOLT" }, line.Path);
            Assert.Equal(top.Handle, line.Handle);
            Assert.Equal(new[] { 0, 0 }, line.IndexPath);

            // The matrix takes the coordinates of BOLT to the world: its (30, 0) is the line's end.
            Near(new GeoPoint3(150, 130, 0), line.Matrix.Transform(new GeoPoint3(30, 0, 0)), 1E-9);
            Near(new GeoPoint3(150, 100, 0), line.Matrix.Transform(new GeoPoint3(0, 0, 0)), 1E-9);
            Assert.Equal(1, flat.Report.Inserts - 1);
            Assert.Empty(flat.Report.Xrefs);
        }

        [Fact]
        public void ATreeOfTurnedScaledAndMirroredBlocks_GivesTheShapesEXPLODEGivesRepeatedly()
        {
            CadDocument document = BlockTree(out DxfBlockReference[] _);
            CadDocument exploded = CadDocument.From(DxfDatabase.Parse(document.Database.ToText()), "again.dxf");
            ExplodeAll(exploded);

            CadFlatDrawing flat = CadFlatten.Walk(document, null);

            List<KeyValuePair<string, GeoAabb3>> walked = Boxes(flat.Entities.Select(f => f.Entity));
            List<KeyValuePair<string, GeoAabb3>> explodedBoxes = Boxes(
                exploded.ModelSpace.Block.Where(e => !e.IsErased)
            );
            Assert.Equal(explodedBoxes.Count, walked.Count);

            for (int i = 0; i < walked.Count; i++)
            {
                Assert.Equal(explodedBoxes[i].Key.Split(' ')[0], walked[i].Key.Split(' ')[0]);
                Assert.True(
                    walked[i].Value.Min.IsEqualTo(explodedBoxes[i].Value.Min, Mm)
                        && walked[i].Value.Max.IsEqualTo(explodedBoxes[i].Value.Max, Mm),
                    walked[i].Key + " against " + explodedBoxes[i].Key
                );
            }

            // The circle of HEAD under the scale of 3 by 1.5 and the 1 by 2 of the HEAD insert is no circle any more.
            Assert.Contains(flat.Entities, f =>
                f.Entity is DxfEllipse && f.Path.SequenceEqual(new[] { "PLATE", "BOLT", "HEAD" })
            );
            // 18 pieces of the three plates and the line of model space, and one more: the polyline of BOLT, which the scale of 3 by 1.5
            // cannot hold, comes out as the two shapes it is drawn as (an arc and a line), as EXPLODE gives them.
            Assert.Equal(20, flat.Entities.Count);
        }

        [Fact]
        public void EveryLineOfBolt_StandsWhereItsMatrixPutsItsBlockCoordinates()
        {
            CadDocument document = BlockTree(out DxfBlockReference[] inserts);
            int checkedLines = 0;

            foreach (
                CadFlatEntity flat in CadFlatten
                    .Walk(document, null)
                    .Entities.Where(f =>
                        f.Entity is DxfLine
                        && f.Path.SequenceEqual(new[] { "PLATE", "BOLT" })
                        && f.Handle != inserts[2].Handle
                    )
            )
            {
                // BOLT's line runs from (0, 0) to (30, 0) in its own coordinates; it lies where the matrix of BOLT takes it. (Under
                // the scale of 3 by 1.5 HEAD is exploded in its turn and its pieces come out with BOLT's path, so the third plate
                // is left out here.)
                var line = (DxfLine)flat.Entity;
                GeoPoint3 start = flat.Matrix.Transform(new GeoPoint3(0, 0, 0));
                GeoPoint3[] ends = { flat.Matrix.Transform(new GeoPoint3(30, 0, 0)) };
                Assert.True(
                    ends.Any(end =>
                        (start.IsEqualTo(line.StartPoint, Mm) && end.IsEqualTo(line.EndPoint, Mm))
                        || (start.IsEqualTo(line.EndPoint, Mm) && end.IsEqualTo(line.StartPoint, Mm))
                    ),
                    flat.Handle + ": " + line.StartPoint + " " + line.EndPoint
                );
                checkedLines++;
            }

            Assert.Equal(2, checkedLines);
        }

        [Fact]
        public void WhatIsOnLayerZeroOrByBlock_TakesTheLookOfTheReferenceItIsIn_AllTheWayDown()
        {
            CadDocument document = BlockTree(out DxfBlockReference[] _);

            CadFlatDrawing flat = CadFlatten.Walk(document, null);

            // The first PLATE is on WALLS in red; HEAD is placed on BOLTS, so its circle (ByBlock) is red too, on BOLTS, and its line (layer 0) is on BOLTS.
            CadFlatEntity circle = flat.Entities.First(f =>
                f.Path.Count == 3 && (f.Entity is DxfCircle || f.Entity is DxfEllipse)
            );
            Assert.Equal("BOLTS", circle.Layer);
            Assert.True(circle.Color.IsByAci);
            Assert.Equal(1, circle.Color.ColorIndex);

            // BOLT's own line is on layer 0 inside PLATE: it goes to WALLS.
            CadFlatEntity bolt = flat.Entities.First(f =>
                f.Entity is DxfLine && f.Path.SequenceEqual(new[] { "PLATE", "BOLT" })
            );
            Assert.Equal("WALLS", bolt.Layer);
            Assert.True(bolt.Color.IsByLayer);

            // The line of model space is left as it is.
            CadFlatEntity own = flat.Entities.Single(f => f.Path.Count == 0);
            Assert.Equal("0", own.Layer);
            Assert.True(own.Color.IsByLayer);
        }

        [Fact]
        public void AnArrayOfBlocks_IsWalkedCellByCell_AlongItsTurnedAxes()
        {
            var db = new DxfDatabase("AC1027");
            DxfBlockTableRecord block = NewBlock(db, "B1");
            block.AppendEntity(new DxfCircle(new GeoPoint3(0, 0, 0), new GeoVector3(0, 0, 1), 1.0));
            var array = new DxfMInsertBlock(new GeoPoint3(20, 40, 0), block, 3, 2, 15, 8) { Rotation = 90 * Degree };
            db.ModelSpace.AppendEntity(array);
            CadDocument document = CadDocument.From(db, "array.dxf");

            CadFlatDrawing flat = CadFlatten.Walk(document, null);

            Assert.Equal(6, flat.Entities.Count);
            Assert.All(flat.Entities, f => Assert.Equal(new[] { "B1" }, f.Path));
            Assert.Equal(new[] { 0, 1, 2, 3, 4, 5 }, flat.Entities.Select(f => f.IndexPath[0]));

            // Turned by 90 degrees, the columns run up the page (15 apart) and the rows to the left (8 apart).
            var centres = flat
                .Entities.Select(f => ((DxfCircle)f.Entity).Center)
                .OrderBy(c => c.Y)
                .ThenBy(c => c.X)
                .ToList();
            Near(new GeoPoint3(12, 40, 0), centres[0], 1E-9);
            Near(new GeoPoint3(20, 40, 0), centres[1], 1E-9);
            Near(new GeoPoint3(12, 55, 0), centres[2], 1E-9);
            Near(new GeoPoint3(20, 70, 0), centres[5], 1E-9);

            // Every cell's matrix takes the block's origin to its centre.
            foreach (CadFlatEntity cell in flat.Entities)
            {
                Near(((DxfCircle)cell.Entity).Center, cell.Matrix.Transform(new GeoPoint3(0, 0, 0)), 1E-9);
            }
        }

        [Fact]
        public void TheAttributesOfAReference_AreEntitiesOfTheirOwn_AndAConstantOneIsToo()
        {
            var db = new DxfDatabase("AC1027");
            DxfBlockTableRecord block = NewBlock(db, "GB");
            block.AppendEntity(new DxfLine(new GeoPoint3(0, 0, 0), new GeoPoint3(10, 0, 0)));
            block.AppendEntity(
                new DxfAttributeDefinition(new GeoPoint3(0, 10, 0), "CONST", "NOTE", "Note", null) { Constant = true }
            );
            var tagDefinition = new DxfAttributeDefinition(new GeoPoint3(0, 20, 0), "X", "TAG", "Tag", null);
            block.AppendEntity(tagDefinition);
            var reference = new DxfBlockReference(new GeoPoint3(100, 100, 0), block);
            db.ModelSpace.AppendEntity(reference);
            var tag = new DxfAttributeReference();
            tag.SetAttributeFromBlock(tagDefinition, reference.BlockTransform);
            tag.TextString = "B-12";
            reference.AttributeCollection.AppendAttribute(tag);
            CadDocument document = CadDocument.From(DxfDatabase.Parse(db.ToText()), "attributes.dxf");

            CadFlatDrawing flat = CadFlatten.Walk(document, null);

            Assert.Equal(3, flat.Entities.Count);
            Assert.Single(flat.Entities, f => f.Entity is DxfLine);
            CadFlatEntity constant = Assert.Single(flat.Entities, f => f.Constant);
            Assert.Equal("CONST", ((DxfAttributeDefinition)constant.Entity).TextString);
            Near(new GeoPoint3(100, 110, 0), ((DxfAttributeDefinition)constant.Entity).Position, 1E-9);
            CadFlatEntity variable = Assert.Single(flat.Entities, f => f.Entity is DxfAttributeReference);
            Assert.Equal("B-12", ((DxfAttributeReference)variable.Entity).TextString);
            Assert.True(variable.IsAttribute);
            Assert.DoesNotContain(flat.Entities, f => f.Entity is DxfAttributeDefinition d && !d.Constant);
        }

        [Fact]
        public void WalkingADrawing_ChangesNothingOfIt()
        {
            CadDocument document = BlockTree(out DxfBlockReference[] _);
            string before = document.Database.ToText();
            int undo = document.CanUndo ? 1 : 0;

            CadFlatten.Walk(document, null);
            CadFlatten.Walk(document, "Model");

            Assert.Equal(before, document.Database.ToText());
            Assert.Equal(undo, document.CanUndo ? 1 : 0);
            Assert.False(document.IsModified);
        }

        [Fact]
        public void TheEntities_ComeInTheOrderOfTheirHandlesAsNumbers_ThePiecesOfOneReferenceAfterEachOther()
        {
            CadDocument document = BlockTree(out DxfBlockReference[] _);

            IReadOnlyList<CadFlatEntity> entities = CadFlatten.Walk(document, null).Entities;

            long last = -1;

            foreach (CadFlatEntity entity in entities)
            {
                long key = CadFlatten.HandleKey(entity.Handle);
                Assert.True(key >= last, "handle " + entity.Handle + " after a greater one");
                last = key;
            }

            // Within one reference the index paths ascend.
            foreach (IGrouping<string, CadFlatEntity> group in entities.GroupBy(e => e.Handle))
            {
                List<int[]> paths = group.Select(e => e.IndexPath.ToArray()).ToList();

                for (int i = 1; i < paths.Count; i++)
                {
                    int common = Math.Min(paths[i - 1].Length, paths[i].Length);
                    int order = 0;

                    for (int k = 0; k < common && order == 0; k++)
                    {
                        order = paths[i - 1][k].CompareTo(paths[i][k]);
                    }

                    Assert.True(order <= 0);
                }
            }

            Assert.Equal(9, CadFlatten.HandleKey("9"));
            Assert.True(CadFlatten.HandleKey("9") < CadFlatten.HandleKey("1A"));
        }

        [Fact]
        public void AClippedReference_IsWalkedUnclipped_AndSaidSo()
        {
            var db = new DxfDatabase("AC1027");
            DxfBlockTableRecord block = NewBlock(db, "GHB");
            block.AppendEntity(new DxfLine(new GeoPoint3(0, 0, 0), new GeoPoint3(100, 0, 0)));
            var insert = new DxfBlockReference(new GeoPoint3(0, 0, 0), block);
            db.ModelSpace.AppendEntity(insert);
            insert.ClipTo(new[] { new GeoPoint2(10, -10), new GeoPoint2(40, 10) }, GeoTransform3.Identity);
            CadDocument document = CadDocument.From(db, "clip.dxf");

            CadFlatDrawing flat = CadFlatten.Walk(document, null);

            CadFlatEntity line = Assert.Single(flat.Entities);
            Assert.True(line.Clipped);
            Assert.Equal(new[] { "GHB" }, flat.Report.Clipped);
            Near(new GeoPoint3(100, 0, 0), ((DxfLine)line.Entity).EndPoint, 1E-9);
        }

        [Fact]
        public void ADynamicBlocksAnonymousCopy_IsWalked_UnderTheNameOfTheDynamicBlock()
        {
            var db = new DxfDatabase("AC1032");
            var block = new DxfBlockTableRecord { Name = "OC_PANEL", Origin = new GeoPoint3(0, 0, 0) };
            db.BlockTable.Add(block);
            DxfLine[] lines =
            {
                new DxfLine(new GeoPoint3(100, 0, 0), new GeoPoint3(100, 50, 0)),
                new DxfLine(new GeoPoint3(0, 0, 0), new GeoPoint3(100, 0, 0)),
                new DxfLine(new GeoPoint3(0, 50, 0), new GeoPoint3(100, 50, 0)),
                new DxfLine(new GeoPoint3(0, 50, 0), new GeoPoint3(0, 0, 0)),
            };

            foreach (DxfLine line in lines)
            {
                block.AppendEntity(line);
            }

            DxfBlockLinearParameter width = block.AddLinearParameter(
                "Width",
                "Overall width",
                new GeoPoint3(0, 0, 0),
                new GeoPoint3(100, 0, 0),
                -20,
                DxfBlockParameterValueSet.List(new[] { 100.0, 150, 200 })
            );
            block.AddStretchAction(width, new GeoPoint2(90, -10), new GeoPoint2(110, 60), lines.Take(3));
            var insert = new DxfBlockReference(new GeoPoint3(1000, 500, 0), block);
            db.ModelSpace.AppendEntity(insert);
            CadDocument document = CadDocument.From(db, "panel.dxf");
            Assert.True(document.GripDrag(insert.Handle, 0, P(1180, 530)).Succeeded);
            Assert.True(((DxfBlockReference)document.FindEntity(insert.Handle)).BlockTableRecord.IsAnonymous);

            CadFlatDrawing flat = CadFlatten.Walk(document, null);

            Assert.Equal(4, flat.Entities.Count);
            Assert.All(flat.Entities, f => Assert.Equal(new[] { "OC_PANEL" }, f.Path));
            Assert.All(flat.Entities, f => Assert.True(f.Dynamic));
            Assert.Contains(flat.Entities, f =>
                f.Entity is DxfLine l && l.StartPoint.IsEqualTo(new GeoPoint3(1200, 500, 0), Mm)
            );
        }

        [Fact]
        public void ALayoutNamed_IsWalkedAlone_AndOneThatIsNotThereIsRefused()
        {
            CadDocument document = BlockTree(out DxfBlockReference[] _);
            AddLineOnSheet(document, out CadSpace sheet);

            CadFlatDrawing model = CadFlatten.Walk(document, "model");
            CadFlatDrawing paper = CadFlatten.Walk(document, sheet.Name);
            CadFlatDrawing all = CadFlatten.Walk(document, null);

            Assert.All(model.Entities, f => Assert.Equal("Model", f.Layout));
            Assert.Single(paper.Entities);
            Assert.Equal(sheet.Name, paper.Entities[0].Layout);
            Assert.Equal(model.Entities.Count + 1, all.Entities.Count);
            Assert.Throws<ArgumentException>(() => CadFlatten.Walk(document, "No such layout"));
            Assert.Throws<ArgumentNullException>(() => CadFlatten.Walk(null, null));
        }

        [Fact]
        public void ABlockDrawnInAnotherUnit_IsListedInTheReport_AndPlacedAsItsReferenceSaysItIs()
        {
            var db = new DxfDatabase("AC1027");
            db.Insunits = DxfUnits.Millimeters;
            DxfBlockTableRecord block = NewBlock(db, "INCHES");
            block.Units = DxfUnits.Inches;
            block.AppendEntity(new DxfLine(new GeoPoint3(0, 0, 0), new GeoPoint3(1, 0, 0)));
            db.ModelSpace.AppendEntity(new DxfBlockReference(new GeoPoint3(0, 0, 0), block));
            CadDocument document = CadDocument.From(db, "units.dxf");

            CadFlatDrawing flat = CadFlatten.Walk(document, null);

            Assert.Single(flat.Report.BlockUnits);
            Assert.Contains("INCHES is drawn in Inches in a drawing of Millimeters", flat.Report.BlockUnits.Single());
            Assert.Contains("25.4", flat.Report.BlockUnits.Single());
            Near(new GeoPoint3(1, 0, 0), ((DxfLine)flat.Entities[0].Entity).EndPoint, 1E-9);
        }
    }
}
