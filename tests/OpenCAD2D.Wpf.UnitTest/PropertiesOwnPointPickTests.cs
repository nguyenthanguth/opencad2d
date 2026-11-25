using System;
using System.IO;
using System.Linq;
using GeometryHelper.Dxf;
using GeometryHelper.Geometry;
using Xunit;

namespace OpenCAD2D.Wpf.UnitTest
{
    /// <summary>
    /// The pick-point button on the coordinate rows of the presenter's own Properties (Center X of a circle, Start Y of a line,
    /// Position Z of a text, Vertex X of a polyline), as AutoCAD 2021 has it: the same button and prompt as a plugin's point
    /// row ("Pick a point in the drawing:", no line of history, a rubber band from the point), the pick setting X and Y of
    /// what is selected as one undo step, as typing a value in the row is one. The drawing: a circle of radius 25 about (50, 50),
    /// a second about (200, 80), a line (0, 0, 7) to (100, 0, 9), a text "NOTE" at (10, 10), a closed polyline (0, 300) to
    /// (100, 350) and a line on a locked layer. The window's settings go to a temporary file.
    /// </summary>
    public sealed class PropertiesOwnPointPickTests : IDisposable
    {
        private readonly TestFolder _folder = new TestFolder();
        private string _circle;
        private string _other;
        private string _line;
        private string _text;
        private string _plate;
        private string _locked;

        public void Dispose() => _folder.Dispose();

        private MainViewModel Model()
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
            var circle = new DxfCircle(new GeoPoint3(50, 50, 0), new GeoVector3(0, 0, 1), 25);
            var other = new DxfCircle(new GeoPoint3(200, 80, 0), new GeoVector3(0, 0, 1), 25);
            var line = new DxfLine(new GeoPoint3(0, 0, 7), new GeoPoint3(100, 0, 9));
            var text = new DxfDBText();
            var plate = new DxfPolyline();
            plate.AddVertexAt(0, new GeoPoint2(0, 300), 0.0, 0.0, 0.0);
            plate.AddVertexAt(1, new GeoPoint2(100, 300), 0.0, 0.0, 0.0);
            plate.AddVertexAt(2, new GeoPoint2(100, 350), 0.0, 0.0, 0.0);
            plate.AddVertexAt(3, new GeoPoint2(0, 350), 0.0, 0.0, 0.0);
            plate.Closed = true;
            var locked = new DxfLine(new GeoPoint3(0, -50, 0), new GeoPoint3(100, -50, 0)) { Layer = "Locked" };

            foreach (DxfEntity entity in new DxfEntity[] { circle, other, line, text, plate, locked })
            {
                db.ModelSpace.AppendEntity(entity);
            }

            text.TextString = "NOTE";
            text.Height = 5;
            text.Position = new GeoPoint3(10, 10, 0);
            _circle = circle.Handle;
            _other = other.Handle;
            _line = line.Handle;
            _text = text.Handle;
            _plate = plate.Handle;
            _locked = locked.Handle;

            var model = new MainViewModel { SettingsPath = Path.Combine(_folder.FullPath, "settings.json") };
            Assert.True(model.Open(_folder.Write("own.dxf", db)));
            return model;
        }

        private static PropertyRowViewModel RowOf(MainViewModel model, string label) =>
            model.PropertyGroups.SelectMany(g => g.Rows).Single(r => r.Label == label);

        private static GeoPoint3 CenterOf(MainViewModel model, string handle) =>
            ((DxfCircle)model.Document.FindEntity(handle)).Center;

        [Fact]
        public void TheCoordinateRowsOfACircle_HaveThePickButton_TheOthersHaveNone()
        {
            MainViewModel model = Model();
            model.Document.Selection.Set(new[] { _circle });

            foreach (string label in new[] { "Center X", "Center Y", "Center Z" })
            {
                PropertyRowViewModel row = RowOf(model, label);
                Assert.True(row.CanPickPoint, label);
                Assert.NotNull(row.PickPointCommand);
            }

            foreach (string label in new[] { "Radius", "Diameter", "Circumference", "Layer", "Color" })
            {
                Assert.False(RowOf(model, label).CanPickPoint, label);
                Assert.Null(RowOf(model, label).PickPointCommand);
            }
        }

        [Fact]
        public void TheCoordinateRowsOfALineATextAndAPolyline_HaveThePickButton()
        {
            MainViewModel model = Model();
            model.Document.Selection.Set(new[] { _line });
            Assert.All(new[] { "Start X", "Start Y", "Start Z", "End X", "End Y", "End Z" }, label =>
                Assert.True(RowOf(model, label).CanPickPoint, label)
            );
            Assert.False(RowOf(model, "Length").CanPickPoint);
            Assert.False(RowOf(model, "Angle").CanPickPoint);

            model.Document.Selection.Set(new[] { _text });
            Assert.All(new[] { "Position X", "Position Y", "Position Z" }, label =>
                Assert.True(RowOf(model, label).CanPickPoint, label)
            );
            Assert.False(RowOf(model, "Height").CanPickPoint);

            model.Document.Selection.Set(new[] { _plate });
            Assert.True(RowOf(model, "Vertex X").CanPickPoint);
            Assert.True(RowOf(model, "Vertex Y").CanPickPoint);
            Assert.False(RowOf(model, "Elevation").CanPickPoint);
            Assert.False(RowOf(model, "Global width").CanPickPoint);
        }

        [Fact]
        public void TheRowsOfTheDrawingWithNothingSelected_HaveNoPickButton()
        {
            MainViewModel model = Model();
            model.Document.Selection.Clear();

            Assert.All(model.PropertyGroups.SelectMany(g => g.Rows), r => Assert.False(r.CanPickPoint, r.Label));
        }

        [Fact]
        public void APickOnCenterX_AsksThePointFromTheCentre_AndSetsTheWholeCentreAsOneUndoStep()
        {
            MainViewModel model = Model();
            model.Document.Selection.Set(new[] { _circle });
            int lines = model.History.Count;

            RowOf(model, "Center X").PickPointCommand.Execute(null);

            Assert.True(model.Session.IsRunning);
            Assert.Equal("Pick a point in the drawing:", model.Session.PromptText);
            Assert.Equal(new GeoPoint2(50, 50), model.Session.Prompt.BasePoint);
            Assert.Equal(50.0, CenterOf(model, _circle).X, 9);

            Assert.True(model.Session.Feed(new GeoPoint2(120.5, 80.25)));

            GeoPoint3 center = CenterOf(model, _circle);
            Assert.Equal(120.5, center.X, 9);
            Assert.Equal(80.25, center.Y, 9);
            Assert.Equal(0.0, center.Z, 9);
            Assert.Equal(25.0, ((DxfCircle)model.Document.FindEntity(_circle)).Radius, 9);
            Assert.False(model.Session.IsRunning);
            Assert.Equal("120.5000", RowOf(model, "Center X").Value);
            Assert.Equal("80.2500", RowOf(model, "Center Y").Value);
            Assert.DoesNotContain(model.History.Skip(lines), l =>
                l.Contains("Pick a point") || l.Contains("PICKPOINT")
            );

            // One undo takes both coordinates back.
            Assert.True(model.Undo());
            Assert.Equal(50.0, CenterOf(model, _circle).X, 9);
            Assert.Equal(50.0, CenterOf(model, _circle).Y, 9);
        }

        [Fact]
        public void APickOnAnyOfTheThreeRows_SetsXAndYAndKeepsZ()
        {
            MainViewModel model = Model();
            model.Document.Selection.Set(new[] { _line });

            RowOf(model, "End Z").PickPointCommand.Execute(null);
            Assert.Equal(new GeoPoint2(100, 0), model.Session.Prompt.BasePoint);
            Assert.True(model.Session.Feed(new GeoPoint2(30, 40)));

            var line = (DxfLine)model.Document.FindEntity(_line);
            Assert.Equal(30.0, line.EndPoint.X, 9);
            Assert.Equal(40.0, line.EndPoint.Y, 9);
            Assert.Equal(9.0, line.EndPoint.Z, 9);
            Assert.Equal(0.0, line.StartPoint.X, 9);
            Assert.Equal(7.0, line.StartPoint.Z, 9);
        }

        [Fact]
        public void APickOnATextAndOnAPolylineVertex_SetsThePoint()
        {
            MainViewModel model = Model();
            model.Document.Selection.Set(new[] { _text });
            RowOf(model, "Position Y").PickPointCommand.Execute(null);
            Assert.Equal(new GeoPoint2(10, 10), model.Session.Prompt.BasePoint);
            Assert.True(model.Session.Feed(new GeoPoint2(15, 25)));
            var text = (DxfDBText)model.Document.FindEntity(_text);
            Assert.Equal(15.0, text.Position.X, 9);
            Assert.Equal(25.0, text.Position.Y, 9);

            model.Document.Selection.Set(new[] { _plate });
            RowOf(model, "Vertex X").PickPointCommand.Execute(null);
            Assert.Equal(new GeoPoint2(0, 300), model.Session.Prompt.BasePoint);
            Assert.True(model.Session.Feed(new GeoPoint2(-10, 290)));
            var plate = (DxfPolyline)model.Document.FindEntity(_plate);
            Assert.Equal(-10.0, plate.GetPoint2dAt(0).X, 9);
            Assert.Equal(290.0, plate.GetPoint2dAt(0).Y, 9);
            Assert.Equal(100.0, plate.GetPoint2dAt(1).X, 9);
        }

        [Fact]
        public void WithTwoCirclesSelected_TheRowVaries_AndThePickSetsBothCentres()
        {
            MainViewModel model = Model();
            model.Document.Selection.Set(new[] { _circle, _other });
            PropertyRowViewModel row = RowOf(model, "Center X");
            Assert.Equal(CadPropertyRow.VariesText, row.Value);
            Assert.True(row.CanPickPoint);

            row.PickPointCommand.Execute(null);

            // The centres differ, so there is no point to rubber-band from.
            Assert.True(model.Session.IsRunning);
            Assert.Null(model.Session.Prompt.BasePoint);
            Assert.True(model.Session.Feed(new GeoPoint2(300, 400)));
            Assert.Equal(300.0, CenterOf(model, _circle).X, 9);
            Assert.Equal(400.0, CenterOf(model, _circle).Y, 9);
            Assert.Equal(300.0, CenterOf(model, _other).X, 9);
            Assert.Equal(400.0, CenterOf(model, _other).Y, 9);
            Assert.Equal("300.0000", RowOf(model, "Center X").Value);

            Assert.True(model.Undo());
            Assert.Equal(50.0, CenterOf(model, _circle).X, 9);
            Assert.Equal(200.0, CenterOf(model, _other).X, 9);
        }

        [Fact]
        public void EscAtThePrompt_LeavesThePoint()
        {
            MainViewModel model = Model();
            model.Document.Selection.Set(new[] { _circle });
            RowOf(model, "Center Y").PickPointCommand.Execute(null);

            model.Escape();

            Assert.False(model.Session.IsRunning);
            Assert.Equal(50.0, CenterOf(model, _circle).X, 9);
            Assert.Equal(50.0, CenterOf(model, _circle).Y, 9);
            Assert.False(model.Document.CanUndo);
        }

        [Fact]
        public void ThePickButton_IsGreyWhileACommandRuns()
        {
            MainViewModel model = Model();
            model.Document.Selection.Set(new[] { _circle });
            PropertyRowViewModel row = RowOf(model, "Center X");
            Assert.True(row.PickPointCommand.CanExecute(null));

            model.Execute("LINE");

            Assert.False(row.PickPointCommand.CanExecute(null));
            row.PickPointCommand.Execute(null);
            Assert.Equal("LINE", model.Session.Current.Name);
            model.Escape();
            Assert.True(row.PickPointCommand.CanExecute(null));
        }

        [Fact]
        public void APointOnALockedLayer_IsRefusedOnTheCommandLine_AndNothingChanges()
        {
            MainViewModel model = Model();
            model.Document.Selection.Set(new[] { _locked });
            RowOf(model, "Start X").PickPointCommand.Execute(null);

            Assert.True(model.Session.Feed(new GeoPoint2(5, 5)));

            Assert.Contains("locked layer", model.History.Last());
            var line = (DxfLine)model.Document.FindEntity(_locked);
            Assert.Equal(0.0, line.StartPoint.X, 9);
            Assert.False(model.Document.CanUndo);
        }

        [Fact]
        public void EnterAfterThePick_RepeatsTheCommandBeforeIt_NotThePick()
        {
            MainViewModel model = Model();
            model.Execute("LINE");
            model.Escape();
            model.Document.Selection.Set(new[] { _circle });
            RowOf(model, "Center X").PickPointCommand.Execute(null);
            Assert.True(model.Session.Feed(new GeoPoint2(5, 5)));

            model.Execute(string.Empty);

            Assert.Equal("LINE", model.Session.Current.Name);
        }

        [Fact]
        public void TypingAValueInTheRow_StillSetsOnlyThatCoordinate()
        {
            MainViewModel model = Model();
            model.Document.Selection.Set(new[] { _circle });

            RowOf(model, "Center X").Value = "75";

            Assert.Equal(75.0, CenterOf(model, _circle).X, 9);
            Assert.Equal(50.0, CenterOf(model, _circle).Y, 9);
        }
    }
}
