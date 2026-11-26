using System;
using System.Linq;
using GeometryHelper;
using GeometryHelper.Dxf;
using GeometryHelper.Geometry;
using Xunit;
using static OpenCAD2D.UnitTest.ExportDrawings;

namespace OpenCAD2D.UnitTest
{
    /// <summary>
    /// Comparing two states of a drawing object by object (CadDrawingDiff): what was added, removed and changed, found by handle
    /// through the same walk and JSON as the AI export, with what differs and how far a box moved.
    /// </summary>
    public class ExportDiffTests
    {
        private static readonly Tolerance Mm = new Tolerance(0.001, 1E-5);

        /// <summary>A drawing of two lines, a circle and a closed polyline, in millimetres.</summary>
        private static CadDocument Plate()
        {
            var db = new DxfDatabase("AC1027");
            db.ModelSpace.AppendEntity(new DxfLine(new GeoPoint3(0, 0, 0), new GeoPoint3(100, 0, 0)));
            db.ModelSpace.AppendEntity(
                new DxfLine(new GeoPoint3(0, 10, 0), new GeoPoint3(100, 10, 0)) { Layer = "Edge" }
            );
            db.ModelSpace.AppendEntity(new DxfCircle { Center = new GeoPoint3(50, 50, 0), Radius = 10 });
            var outline = new DxfPolyline();
            outline.AddVertexAt(0, new GeoPoint2(0, 0), 0, 0, 0);
            outline.AddVertexAt(1, new GeoPoint2(20, 0), 0, 0, 0);
            outline.AddVertexAt(2, new GeoPoint2(20, 20), 0, 0, 0);
            outline.Closed = true;
            db.ModelSpace.AppendEntity(outline);
            return CadDocument.From(db, "plate.dxf");
        }

        /// <summary>The same drawing again, as a saved copy of it would be read: the same handles.</summary>
        private static CadDocument Copy(CadDocument document) =>
            CadDocument.From(DxfDatabase.Parse(document.Database.ToText()), "copy.dxf");

        private static T First<T>(CadDocument document)
            where T : DxfEntity => document.ModelSpace.Block.OfType<T>().First(e => !e.IsErased);

        [Fact]
        public void TheSameDrawingTwice_IsTheSame_AndEveryObjectIsUnchanged()
        {
            CadDocument a = Plate();

            CadDrawingDiffResult diff = CadDrawingDiff.Compare(a, Copy(a), Mm);

            Assert.True(diff.IsSame);
            Assert.Equal(4, diff.Unchanged);
            Assert.Empty(diff.Added);
            Assert.Empty(diff.Removed);
            Assert.Empty(diff.Changed);
        }

        [Fact]
        public void AMovedCircle_IsChanged_WithItsCentreAndBoxNamed_AndHowFarItsBoxMoved()
        {
            CadDocument a = Plate();
            CadDocument b = Copy(a);
            DxfCircle circle = First<DxfCircle>(b);
            circle.Center = new GeoPoint3(60, 45, 0);

            CadDrawingDiffResult diff = CadDrawingDiff.Compare(a, b, Mm);

            CadDrawingChange change = Assert.Single(diff.Changed);
            Assert.Empty(diff.Added);
            Assert.Empty(diff.Removed);
            Assert.Equal(3, diff.Unchanged);
            Assert.Equal(circle.Handle, change.Handle);
            Assert.Equal("CIRCLE", change.Kind);
            Assert.Contains("box", change.Properties);
            Assert.Contains("center", change.Properties);
            Assert.Equal(new[] { 40.0, 40.0, 60.0, 60.0 }, change.BoxBefore);
            Assert.Equal(new[] { 50.0, 35.0, 70.0, 55.0 }, change.BoxAfter);
            Assert.Equal(10.0, change.MovedBy[0], 6);
            Assert.Equal(-5.0, change.MovedBy[1], 6);
            Assert.Equal(change.Properties.OrderBy(p => p, StringComparer.Ordinal), change.Properties);
        }

        [Fact]
        public void ALineMadeLonger_IsChanged_ButItsBoxDidNotMove_AndALayerChangeIsNamed()
        {
            CadDocument a = Plate();
            CadDocument b = Copy(a);
            DxfLine line = First<DxfLine>(b);
            line.EndPoint = new GeoPoint3(130, 0, 0);
            line.Layer = "Steel";

            CadDrawingChange change = Assert.Single(CadDrawingDiff.Compare(a, b, Mm).Changed);

            Assert.Contains("end", change.Properties);
            Assert.Contains("layer", change.Properties);
            Assert.Equal("Steel", change.Layer);
            Assert.Null(change.MovedBy);
            Assert.Equal(130.0, change.BoxAfter[2], 6);
            Assert.Equal(100.0, change.BoxBefore[2], 6);
        }

        [Fact]
        public void AnObjectErasedAndAnObjectAdded_AreRemovedAndAdded_InTheOrderOfTheirHandles()
        {
            CadDocument a = Plate();
            CadDocument b = Copy(a);
            DxfLine gone = First<DxfLine>(b);
            string goneHandle = gone.Handle;
            gone.Erase();
            var added = new DxfLine(new GeoPoint3(5, 5, 0), new GeoPoint3(9, 9, 0));
            var more = new DxfCircle { Center = new GeoPoint3(1, 1, 0), Radius = 2 };
            b.ModelSpace.Block.AppendEntity(added);
            b.ModelSpace.Block.AppendEntity(more);

            CadDrawingDiffResult diff = CadDrawingDiff.Compare(a, b, Mm);

            Assert.Equal(new[] { goneHandle }, diff.Removed.Select(r => r.Handle).ToArray());
            Assert.Equal(new[] { added.Handle, more.Handle }, diff.Added.Select(r => r.Handle).ToArray());
            Assert.Null(diff.Removed[0].BoxAfter);
            Assert.NotNull(diff.Removed[0].BoxBefore);
            Assert.Null(diff.Added[0].BoxBefore);
            Assert.Equal(new[] { 5.0, 5.0, 9.0, 9.0 }, diff.Added[0].BoxAfter);
            Assert.Equal("LINE", diff.Added[0].Kind);
            Assert.Equal("CIRCLE", diff.Added[1].Kind);
            Assert.Equal(3, diff.Unchanged);
        }

        [Fact]
        public void ADifferenceUnderTheToleranceIsNoDifference_AndOneOverItIs()
        {
            CadDocument a = Plate();
            CadDocument b = Copy(a);
            First<DxfCircle>(b).Radius = 10.0004;

            Assert.True(CadDrawingDiff.Compare(a, b, Mm).IsSame);
            Assert.False(CadDrawingDiff.Compare(a, b, new Tolerance(0.0001, 1E-5)).IsSame);
            First<DxfCircle>(b).Radius = 10.01;
            Assert.False(CadDrawingDiff.Compare(a, b, Mm).IsSame);
        }

        [Fact]
        public void ABlockReferenceMoved_ChangesEveryPieceItDraws_EachFoundByItsPlaceInTheBlock()
        {
            CadDocument a = BlockTree(out DxfBlockReference[] _);
            CadDocument b = Copy(a);
            int pieces = CadFlatten.Walk(a, null).Entities.Count;
            DxfBlockReference top = b.ModelSpace.Block.OfType<DxfBlockReference>().First(e => !e.IsErased);
            top.Position = new GeoPoint3(top.Position.X + 25, top.Position.Y, top.Position.Z);

            CadDrawingDiffResult diff = CadDrawingDiff.Compare(a, b, Mm);

            Assert.NotEmpty(diff.Changed);
            Assert.All(diff.Changed, c => Assert.Contains("box", c.Properties));
            Assert.True(diff.Changed.Count + diff.Unchanged == pieces, "Every piece is found in the other drawing.");
            Assert.Empty(diff.Added);
            Assert.Empty(diff.Removed);
        }

        [Fact]
        public void TwoDrawingsDrawnApart_ShareNoHandles_AndAreTheSameTwiceRoundWithTheSameOrder()
        {
            CadDocument a = Plate();
            CadDocument b = Copy(a);
            First<DxfLine>(b).EndPoint = new GeoPoint3(5, 5, 0);
            First<DxfCircle>(b).Radius = 3;

            CadDrawingDiffResult one = CadDrawingDiff.Compare(a, b, Mm);
            CadDrawingDiffResult two = CadDrawingDiff.Compare(a, b, Mm);

            Assert.Equal(one.Changed.Select(c => c.Handle), two.Changed.Select(c => c.Handle));
            Assert.Equal(
                one.Changed.Select(c => c.Handle).OrderBy(h => Convert.ToInt64(h, 16)),
                one.Changed.Select(c => c.Handle)
            );
        }

        [Fact]
        public void PiecesOfABlockThatTieOnEverythingButTheirPlaceInTheBlock_AreOrderedByThatPlaceNumerically()
        {
            var db = new DxfDatabase("AC1027");
            var block = new DxfBlockTableRecord { Name = "TWELVE" };
            db.BlockTable.Add(block);

            for (int i = 0; i < 12; i++)
            {
                block.AppendEntity(new DxfLine(new GeoPoint3(0, 0, 0), new GeoPoint3(10, 0, 0)));
            }

            db.ModelSpace.AppendEntity(new DxfBlockReference(new GeoPoint3(0, 0, 0), block));
            CadDocument a = CadDocument.From(db, "twelve.dxf");
            CadDocument b = Copy(a);
            DxfBlockReference top = b.ModelSpace.Block.OfType<DxfBlockReference>().First(e => !e.IsErased);
            top.Position = new GeoPoint3(25, 0, 0);

            CadDrawingDiffResult diff = CadDrawingDiff.Compare(a, b, Mm);

            Assert.Equal(12, diff.Changed.Count);
            string[] orders = diff.Changed.Select(c => c.Order).ToArray();
            Assert.Equal(orders.OrderBy(o => o, StringComparer.Ordinal), orders);
            Assert.Equal(12, orders.Distinct().Count());
            Assert.StartsWith("0000000002", orders[2], StringComparison.Ordinal);
            Assert.StartsWith("0000000010", orders[10], StringComparison.Ordinal);
        }

        [Fact]
        public void ANullDrawingIsRefused()
        {
            CadDocument a = Plate();

            Assert.Throws<ArgumentNullException>(() => CadDrawingDiff.Compare(null, a, Mm));
            Assert.Throws<ArgumentNullException>(() => CadDrawingDiff.Compare(a, null, Mm));
        }
    }
}
