using System;
using System.IO;
using System.Linq;
using GeometryHelper;
using GeometryHelper.Dxf;
using GeometryHelper.Geometry;
using OpenCAD2D.SheetSets;
using Xunit;

namespace OpenCAD2D.UnitTest
{
    /// <summary>
    /// What the Model Views tab reads of a resource location and what Place on Sheet makes (A12 4d): the drawings of a folder and
    /// the named model views of one, then the view placed on a layout of a sheet's drawing as an attached xref in model space, a
    /// viewport at a scale of the list, and a named view saved in the layout, in one undo step. Drawings are built in code in
    /// a temporary folder.
    /// </summary>
    public class SheetSetPlaceOnSheetTests : IDisposable
    {
        private readonly string _folder = Path.Combine(
            Path.GetTempPath(),
            "gk",
            "placeonsheet-" + Guid.NewGuid().ToString("N")
        );

        public SheetSetPlaceOnSheetTests()
        {
            Directory.CreateDirectory(_folder);
        }

        public void Dispose()
        {
            try
            {
                Directory.Delete(_folder, true);
            }
            catch (IOException)
            {
                // A file a watcher still holds is left for the system to clear.
            }
        }

        /// <summary>A model drawing in millimetres: a rectangle 32 000 by 18 500, and the views Ground floor, then Roof, and a view saved in a layout.</summary>
        private string Model(string file = "Tower model.dwg")
        {
            var database = new DxfDatabase("AC1032");
            database.Insunits = DxfUnits.Millimeters;
            database.ModelSpace.AppendEntity(new DxfLine(new GeoPoint3(0, 0, 0), new GeoPoint3(32000, 0, 0)));
            database.ModelSpace.AppendEntity(new DxfLine(new GeoPoint3(32000, 0, 0), new GeoPoint3(32000, 18500, 0)));
            database.ModelSpace.AppendEntity(new DxfLine(new GeoPoint3(32000, 18500, 0), new GeoPoint3(0, 18500, 0)));
            database.ModelSpace.AppendEntity(new DxfLine(new GeoPoint3(0, 18500, 0), new GeoPoint3(0, 0, 0)));
            database.ViewTable.Add(
                new DxfViewTableRecord
                {
                    Name = "Roof",
                    CenterPoint = new GeoPoint2(16000, 9250),
                    Width = 32000,
                    Height = 18500,
                }
            );
            database.ViewTable.Add(
                new DxfViewTableRecord
                {
                    Name = "Ground floor",
                    CenterPoint = new GeoPoint2(8000, 4000),
                    Width = 16000,
                    Height = 8000,
                }
            );
            database.ViewTable.Add(
                new DxfViewTableRecord
                {
                    Name = "On a sheet",
                    IsPaperspaceView = true,
                    CenterPoint = new GeoPoint2(100, 100),
                    Width = 50,
                    Height = 50,
                }
            );
            Directory.CreateDirectory(Path.Combine(_folder, "resources"));
            string path = Path.Combine(_folder, "resources", file);
            CadDocument.From(database, file).SaveAs(path);
            return path;
        }

        /// <summary>A sheet's drawing, saved in its own folder, open with its layout current.</summary>
        private CadDocument Sheet()
        {
            Directory.CreateDirectory(Path.Combine(_folder, "sheets"));
            CadDocument document = CadDocument.From(SheetSetTestDrawings.Build("Plan"), "plan.dwg");
            string path = Path.Combine(_folder, "sheets", "plan.dwg");
            document.SaveAs(path);
            document.CurrentSpace = document.Spaces.Single(s => !s.IsModel);
            return document;
        }

        private static DxfViewport[] ViewportsOf(CadSpace layout) =>
            layout.Block.OfType<DxfViewport>().Where(v => v.Number > 1).ToArray();

        [Fact]
        public void TheDrawingsOfAFolder_AreTheDwgFilesInIt_InOrderOfName_AndNotThoseInTheFoldersUnderIt()
        {
            Model("b model.dwg");
            Model("A site.dwg");
            File.WriteAllText(Path.Combine(_folder, "resources", "notes.txt"), "x");
            Directory.CreateDirectory(Path.Combine(_folder, "resources", "old"));
            Model("old\\c old.dwg");

            string[] names = CadSheetSetResources
                .DrawingsIn(Path.Combine(_folder, "resources"))
                .Select(Path.GetFileName)
                .ToArray();

            Assert.Equal(new[] { "A site.dwg", "b model.dwg" }, names);
            Assert.Empty(CadSheetSetResources.DrawingsIn(Path.Combine(_folder, "nowhere")));
        }

        [Fact]
        public void TheViewsOfADrawing_AreThoseOfModelSpaceInOrderOfName_WithTheirSizeAndUnit()
        {
            string path = Model();

            CadModelView[] views = CadSheetSetResources.ModelViewsOf(path).ToArray();

            Assert.Equal(new[] { "Ground floor", "Roof" }, views.Select(v => v.Name).ToArray());
            Assert.Equal(16000, views[0].Width, 6);
            Assert.Equal(8000, views[0].Height, 6);
            Assert.Equal(8000, views[0].Center.X, 6);
            Assert.Equal(DxfUnits.Millimeters, views[0].Units);
            Assert.False(string.IsNullOrEmpty(views[0].Handle));
        }

        [Fact]
        public void PlaceOnSheet_AttachesTheDrawingAsAnAttachXrefOnce_MakesAViewportAtTheScale_AndSavesANamedView()
        {
            string model = Model();
            CadDocument sheet = Sheet();
            CadSpace layout = sheet.CurrentSpace;
            CadPlaceOnSheetPlan plan = CadSheetSetPlacement.Plan(model, "Ground floor");

            CadPlaceOnSheetResult result = CadSheetSetPlacement.Place(
                sheet,
                layout,
                plan,
                new GeoPoint2(200, 150),
                "1:100"
            );

            // The drawing is an attached xref of the sheet's drawing, with one INSERT of it in model space at the origin.
            DxfBlockTableRecord xref = sheet.Database.BlockTable["Tower model"];
            Assert.Equal("Tower model", result.XrefName);
            Assert.True(xref.IsFromExternalReference);
            Assert.False(xref.IsFromOverlayReference);
            Assert.Equal(Path.Combine("..", "resources", "Tower model.dwg"), xref.PathName);
            DxfBlockReference insert = sheet.ModelSpace.Block.OfType<DxfBlockReference>().Single();
            Assert.Same(xref, insert.BlockTableRecord);
            Assert.Equal(0, insert.Position.X, 6);

            // The viewport is the view's size at 1:100, on the paper where it was placed, looking at the view's middle.
            DxfViewport viewport = ViewportsOf(layout).Single();
            Assert.Equal(result.ViewportHandle, viewport.Handle);
            Assert.Equal(160, viewport.Width, 6);
            Assert.Equal(80, viewport.Height, 6);
            Assert.Equal(200, viewport.CenterPoint.X, 6);
            Assert.Equal(150, viewport.CenterPoint.Y, 6);
            Assert.Equal(0.01, viewport.CustomScale, 9);
            Assert.Equal(8000, viewport.ViewCenter.X, 6);
            Assert.Equal(4000, viewport.ViewCenter.Y, 6);
            Assert.Equal("1:100", result.Scale);

            // A named view of the layout round the viewport, which the sheet view points at; the model view's name is its title.
            DxfViewTableRecord named = sheet.Database.ViewTable[result.NamedViewName];
            Assert.Equal("Ground floor", result.Title);
            Assert.Equal("Ground floor", named.Name);
            Assert.True(named.IsPaperspaceView);
            Assert.Equal(160, named.Width, 6);
            Assert.Equal(result.NamedViewHandle, named.Handle);
            Assert.Equal("1:100", CadSheetSetPlacement.ScaleOf(sheet.Database, result.NamedViewName));
        }

        [Fact]
        public void APlacedViewWhoseDrawingIsAttachedAlready_ReusesTheXref_AndItsNamedViewTakesANumber()
        {
            string model = Model();
            CadDocument sheet = Sheet();
            CadSpace layout = sheet.CurrentSpace;

            CadPlaceOnSheetResult first = CadSheetSetPlacement.Place(
                sheet,
                layout,
                CadSheetSetPlacement.Plan(model, "Ground floor"),
                new GeoPoint2(100, 100),
                "1:200"
            );
            CadPlaceOnSheetResult second = CadSheetSetPlacement.Place(
                sheet,
                layout,
                CadSheetSetPlacement.Plan(model, "Ground floor"),
                new GeoPoint2(300, 100),
                "1:50"
            );

            Assert.Equal(first.XrefName, second.XrefName);
            Assert.Single(sheet.ModelSpace.Block.OfType<DxfBlockReference>());
            Assert.Equal(1, sheet.Database.BlockTable.Count(b => b.IsFromExternalReference));
            Assert.Equal(2, ViewportsOf(layout).Length);
            Assert.Equal("Ground floor", first.NamedViewName);
            Assert.Equal("Ground floor2", second.NamedViewName);
            Assert.Equal("1:200", CadSheetSetPlacement.ScaleOf(sheet.Database, first.NamedViewName));
            Assert.Equal("1:50", CadSheetSetPlacement.ScaleOf(sheet.Database, second.NamedViewName));
        }

        [Fact]
        public void TheScaleOfAView_IsReadThroughANamedViewLargerThanItsViewport_AsAutoCADsOwnSheetViewsAre()
        {
            string model = Model();
            CadDocument sheet = Sheet();
            CadPlaceOnSheetResult result = CadSheetSetPlacement.Place(
                sheet,
                sheet.CurrentSpace,
                CadSheetSetPlacement.Plan(model, "Ground floor"),
                new GeoPoint2(200, 150),
                "1:100"
            );

            // AutoCAD's views of the sheets surround their viewport with a margin (the middle the same, 12 percent wider).
            DxfViewTableRecord named = sheet.Database.ViewTable[result.NamedViewName];
            named.Width *= 1.12;
            named.Height *= 1.29;

            Assert.Equal("1:100", CadSheetSetPlacement.ScaleOf(sheet.Database, result.NamedViewName));
            Assert.Equal(string.Empty, CadSheetSetPlacement.ScaleOf(sheet.Database, "No such view"));
            named.CenterPoint = new GeoPoint2(0, 0);
            Assert.Equal(string.Empty, CadSheetSetPlacement.ScaleOf(sheet.Database, result.NamedViewName));
        }

        [Fact]
        public void ADrawingPlacedWithNoView_ShowsAllOfModelSpace_AndIsTitledByItsFileName()
        {
            string model = Model();
            CadDocument sheet = Sheet();
            CadPlaceOnSheetPlan plan = CadSheetSetPlacement.Plan(model, null);

            CadPlaceOnSheetResult result = CadSheetSetPlacement.Place(
                sheet,
                sheet.CurrentSpace,
                plan,
                new GeoPoint2(250, 150),
                "1:200"
            );

            Assert.Equal("Tower model", result.Title);
            DxfViewport viewport = ViewportsOf(sheet.CurrentSpace).Single();
            Assert.Equal(160, viewport.Width, 3);
            Assert.Equal(92.5, viewport.Height, 3);
            Assert.Equal(0.005, viewport.CustomScale, 9);
        }

        [Fact]
        public void PlaceOnSheet_IsOneUndoStep_TakingBackTheXrefTheViewportAndTheView()
        {
            string model = Model();
            CadDocument sheet = Sheet();
            string before = sheet.Database.ToText();

            CadSheetSetPlacement.Place(
                sheet,
                sheet.CurrentSpace,
                CadSheetSetPlacement.Plan(model, "Roof"),
                new GeoPoint2(200, 150),
                "1:200"
            );
            Assert.True(sheet.Undo());

            Assert.Empty(ViewportsOf(sheet.CurrentSpace));
            Assert.False(sheet.Database.BlockTable.Has("Tower model"));
            Assert.False(sheet.Database.ViewTable.Has("Roof"));
            Assert.Empty(sheet.ModelSpace.Block.OfType<DxfBlockReference>());
        }

        [Fact]
        public void PlaceOnSheet_IsRefusedOnModel_ForAScaleThatIsNone_AndForAViewTheDrawingLacks()
        {
            string model = Model();
            CadDocument sheet = Sheet();
            CadPlaceOnSheetPlan plan = CadSheetSetPlacement.Plan(model, "Roof");

            Assert.Throws<InvalidOperationException>(() =>
                CadSheetSetPlacement.Place(sheet, sheet.ModelSpace, plan, new GeoPoint2(0, 0), "1:100")
            );
            Assert.Throws<ArgumentException>(() =>
                CadSheetSetPlacement.Place(sheet, sheet.CurrentSpace, plan, new GeoPoint2(0, 0), "big")
            );
            Assert.Throws<InvalidOperationException>(() => CadSheetSetPlacement.Plan(model, "Attic"));
            Assert.Empty(ViewportsOf(sheet.CurrentSpace));
        }

        [Fact]
        public void ThePlaceOnSheetCommand_ShowsTheOutlineAtTheScale_TakesTheScaleOption_AndPlacesWhereTheClickIs()
        {
            string model = Model();
            CadDocument sheet = Sheet();
            CadPlaceOnSheetResult placed = null;
            var session = new CadCommandSession(sheet);
            CadPlaceOnSheetPlan plan = CadSheetSetPlacement.Plan(model, "Ground floor");

            Assert.True(session.Start(CadSheetSetPlacement.Command(plan, "1:100", r => placed = r)));
            Assert.Equal("Specify insertion point", session.Prompt.Message);
            Assert.Contains("Scale", session.Prompt.Keywords);
            Assert.NotNull(session.Prompt.Preview);

            Assert.True(session.Feed("Scale"));
            Assert.StartsWith("Enter scale", session.Prompt.Message);
            Assert.Contains("1:50", session.Prompt.Keywords);
            Assert.True(session.Feed("1:50"));
            Assert.StartsWith("Specify insertion point", session.Prompt.Message);
            Assert.True(session.Feed(new GeoPoint2(210, 148)));

            Assert.False(session.IsRunning);
            Assert.NotNull(placed);
            Assert.Equal("1:50", placed.Scale);
            DxfViewport viewport = ViewportsOf(sheet.CurrentSpace).Single();
            Assert.Equal(320, viewport.Width, 6);
            Assert.Equal(210, viewport.CenterPoint.X, 6);
            Assert.Equal(0.02, viewport.CustomScale, 9);
            Assert.True(sheet.Undo());
            Assert.Empty(ViewportsOf(sheet.CurrentSpace));
        }

        [Fact]
        public void ThePlaceOnSheetCommand_OnModel_SaysItIsNotAllowedThere_AndMakesNothing()
        {
            string model = Model();
            CadDocument sheet = Sheet();
            sheet.CurrentSpace = sheet.ModelSpace;
            CadPlaceOnSheetResult placed = null;
            var session = new CadCommandSession(sheet);

            session.Start(
                CadSheetSetPlacement.Command(CadSheetSetPlacement.Plan(model, "Roof"), "1:100", r => placed = r)
            );

            Assert.False(session.IsRunning);
            Assert.Null(placed);
            Assert.Equal("** Command not allowed in Model Tab **", session.History.Last());
        }
    }
}
