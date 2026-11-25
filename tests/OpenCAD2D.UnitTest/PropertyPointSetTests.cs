using System.Collections.Generic;
using System.Linq;
using GeometryHelper.Dxf;
using GeometryHelper.Geometry;
using Xunit;

namespace OpenCAD2D.UnitTest
{
    /// <summary>
    /// Setting a whole point of the Properties panel at once (<see cref="CadPropertyEditor.SetPoint"/>), as AutoCAD 2021's pick
    /// button on a coordinate row does: X and Y of the point written to every selected object as one undo step, Z left as each
    /// had it, a refusal writing nothing. The drawing has a line (0, 0, 7) to (100, 0, 9), a circle of radius 25 about (50, 50, 4),
    /// a second circle about (200, 80, 0), a text at (10, 10, 0) and a line on a locked layer.
    /// </summary>
    public class PropertyPointSetTests
    {
        private sealed class Kit
        {
            public CadDocument Document;
            public DxfLine Line;
            public DxfCircle Circle;
            public DxfCircle Other;
            public DxfDBText Text;
            public DxfLine Locked;
        }

        private static Kit Make()
        {
            var db = new DxfDatabase("AC1027");
            db.LayerTable.Add(
                new DxfLayerTableRecord
                {
                    Name = "Locked",
                    ColorIndex = 3,
                    IsLocked = true,
                }
            );
            var kit = new Kit
            {
                Line = new DxfLine(new GeoPoint3(0, 0, 7), new GeoPoint3(100, 0, 9)),
                Circle = new DxfCircle(new GeoPoint3(50, 50, 4), new GeoVector3(0, 0, 1), 25),
                Other = new DxfCircle(new GeoPoint3(200, 80, 0), new GeoVector3(0, 0, 1), 25),
                Text = new DxfDBText(),
                Locked = new DxfLine(new GeoPoint3(0, -50, 0), new GeoPoint3(100, -50, 0)) { Layer = "Locked" },
            };
            db.ModelSpace.AppendEntity(kit.Line);
            db.ModelSpace.AppendEntity(kit.Circle);
            db.ModelSpace.AppendEntity(kit.Other);
            db.ModelSpace.AppendEntity(kit.Text);
            kit.Text.TextString = "NOTE";
            kit.Text.Height = 5;
            kit.Text.Position = new GeoPoint3(10, 10, 0);
            db.ModelSpace.AppendEntity(kit.Locked);
            kit.Document = CadDocument.From(db, "point.dxf");
            return kit;
        }

        [Theory]
        [InlineData("Start X", "Start")]
        [InlineData("Start Y", "Start")]
        [InlineData("Start Z", "Start")]
        [InlineData("End Z", "End")]
        [InlineData("Center X", "Center")]
        [InlineData("Vertex Y", "Vertex")]
        [InlineData("Position Z", "Position")]
        [InlineData("Radius", null)]
        [InlineData("Scale X", null)]
        [InlineData("Origin X", null)]
        [InlineData("Start", null)]
        [InlineData("Layer", null)]
        [InlineData("Length", null)]
        public void ThePointARowBelongsTo_IsItsLabelWithoutTheAxis(string label, string name) =>
            Assert.Equal(name, CadPropertyEditor.PointName(label));

        [Fact]
        public void APointOfALine_IsSetAsOneUndoStep_WithZKept()
        {
            Kit kit = Make();

            CadEditResult result = CadPropertyEditor.SetPoint(
                kit.Document,
                new[] { kit.Line.Handle },
                "End Z",
                new GeoPoint2(120.5, 30.25)
            );

            Assert.True(result.Succeeded, result.Message);
            Assert.Equal(120.5, kit.Line.EndPoint.X, 9);
            Assert.Equal(30.25, kit.Line.EndPoint.Y, 9);
            Assert.Equal(9.0, kit.Line.EndPoint.Z, 9);
            Assert.Equal(7.0, kit.Line.StartPoint.Z, 9);
            Assert.Equal("Properties", kit.Document.NextUndo.Name);

            Assert.True(kit.Document.Undo());

            // The drawing is read again by an undo: the entities are found by their handles.
            var back = (DxfLine)kit.Document.FindEntity(kit.Line.Handle);
            Assert.Equal(100.0, back.EndPoint.X, 9);
            Assert.Equal(0.0, back.EndPoint.Y, 9);
            Assert.Equal(9.0, back.EndPoint.Z, 9);
            Assert.False(kit.Document.CanUndo);
        }

        [Fact]
        public void TheCentresOfTwoCircles_AreBothSetToThePoint_WithTheirOwnZ()
        {
            Kit kit = Make();

            CadEditResult result = CadPropertyEditor.SetPoint(
                kit.Document,
                new[] { kit.Circle.Handle, kit.Other.Handle },
                "Center X",
                new GeoPoint2(300, 400)
            );

            Assert.True(result.Succeeded, result.Message);
            Assert.Equal(300.0, kit.Circle.Center.X, 9);
            Assert.Equal(400.0, kit.Circle.Center.Y, 9);
            Assert.Equal(4.0, kit.Circle.Center.Z, 9);
            Assert.Equal(300.0, kit.Other.Center.X, 9);
            Assert.Equal(400.0, kit.Other.Center.Y, 9);
            Assert.Equal(0.0, kit.Other.Center.Z, 9);
            Assert.Equal(25.0, kit.Circle.Radius, 9);
            Assert.True(kit.Document.Undo());
            Assert.Equal(50.0, ((DxfCircle)kit.Document.FindEntity(kit.Circle.Handle)).Center.X, 9);
            Assert.Equal(200.0, ((DxfCircle)kit.Document.FindEntity(kit.Other.Handle)).Center.X, 9);
            Assert.False(kit.Document.CanUndo);
        }

        [Fact]
        public void APointToAFullPrecision_IsNotRounded()
        {
            Kit kit = Make();

            Assert.True(
                CadPropertyEditor
                    .SetPoint(
                        kit.Document,
                        new[] { kit.Text.Handle },
                        "Position Y",
                        new GeoPoint2(1.0 / 3.0, 2.0 / 3.0)
                    )
                    .Succeeded
            );

            Assert.Equal(1.0 / 3.0, kit.Text.Position.X, 14);
            Assert.Equal(2.0 / 3.0, kit.Text.Position.Y, 14);
        }

        [Fact]
        public void APointOnALockedLayer_IsRefused_AndNothingIsWritten()
        {
            Kit kit = Make();

            CadEditResult result = CadPropertyEditor.SetPoint(
                kit.Document,
                new[] { kit.Locked.Handle },
                "Start X",
                new GeoPoint2(5, 5)
            );

            Assert.False(result.Succeeded);
            Assert.Contains("locked layer", result.Message);
            Assert.Equal(0.0, kit.Locked.StartPoint.X, 9);
            Assert.Equal(-50.0, kit.Locked.StartPoint.Y, 9);
            Assert.False(kit.Document.CanUndo);
        }

        [Fact]
        public void ARowThatIsNoCoordinateOfAPoint_IsRefused()
        {
            Kit kit = Make();

            CadEditResult result = CadPropertyEditor.SetPoint(
                kit.Document,
                new[] { kit.Circle.Handle },
                "Radius",
                new GeoPoint2(5, 5)
            );

            Assert.False(result.Succeeded);
            Assert.Equal(25.0, kit.Circle.Radius, 9);
            Assert.False(kit.Document.CanUndo);
        }

        [Fact]
        public void APointOfAnObjectWithoutTheRow_IsRefusedWhole()
        {
            Kit kit = Make();

            // The line has no Center: nothing of the circle is written either.
            CadEditResult result = CadPropertyEditor.SetPoint(
                kit.Document,
                new[] { kit.Circle.Handle, kit.Line.Handle },
                "Center X",
                new GeoPoint2(5, 5)
            );

            Assert.False(result.Succeeded);
            Assert.Equal(50.0, kit.Circle.Center.X, 9);
            Assert.Equal(50.0, kit.Circle.Center.Y, 9);
            Assert.False(kit.Document.CanUndo);
        }
    }
}
