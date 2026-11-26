using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using GeometryHelper.Dxf;
using GeometryHelper.Geometry;
using OpenCAD2D.SheetSets;
using Xunit;

namespace OpenCAD2D.Wpf.UnitTest
{
    /// <summary>
    /// The Model Views tab of the Sheet Set Manager palette (A12 4d): the resource locations, the drawings in them and their named
    /// model views read from the closed files, Add New Location, Remove Location, Refresh, Open, the menus and which of their
    /// items are on, and Place on Sheet (by the menu's command and by a drop on the drawing area), which leaves an attached xref, a
    /// viewport at the scale and a sheet view in the set. Sets and drawings are built in code in a temporary folder.
    /// </summary>
    public class SheetSetModelViewsTabTests : IDisposable
    {
        private readonly TestFolder _folder = new TestFolder();

        public void Dispose() => _folder.Dispose();

        private string SetFolder => Path.Combine(_folder.FullPath, "tower");

        private string Dst => Path.Combine(SetFolder, "Example Tower.dst");

        private string ModelFolder => Path.Combine(_folder.FullPath, "Model");

        private static string Drawing(string folder, string file, params string[] layouts)
        {
            Directory.CreateDirectory(folder);
            var database = new DxfDatabase("AC1032");
            database.Layouts.Single(l => l.LayoutName == "Layout1").LayoutName = layouts[0];

            foreach (string name in layouts.Skip(1))
            {
                var layout = new DxfLayout { LayoutName = name };
                layout.AddToLayoutDictionary(database, null);
                layout.Initialize();
            }

            foreach (string name in layouts)
            {
                database
                    .Layouts.Single(l => l.LayoutName == name)
                    .BlockTableRecord.AppendEntity(new DxfLine(new GeoPoint3(10, 0, 0), new GeoPoint3(10, 50, 0)));
            }

            database.ModelSpace.AppendEntity(new DxfCircle(new GeoPoint3(0, 0, 0), GeoVector3.ZAxis, 5));
            string path = Path.Combine(folder, file);
            CadDocument.From(database, file).SaveAs(path);
            return path;
        }

        /// <summary>A drawing of model space with the named views given, as name, centre and size, and one view saved in a layout.</summary>
        private string Model(string file, params (string Name, double X, double Y, double W, double H)[] views)
        {
            var database = new DxfDatabase("AC1032");
            database.Insunits = DxfUnits.Millimeters;
            database.ModelSpace.AppendEntity(new DxfLine(new GeoPoint3(0, 0, 0), new GeoPoint3(32000, 18500, 0)));

            foreach (var view in views)
            {
                database.ViewTable.Add(
                    new DxfViewTableRecord
                    {
                        Name = view.Name,
                        CenterPoint = new GeoPoint2(view.X, view.Y),
                        Width = view.W,
                        Height = view.H,
                    }
                );
            }

            database.ViewTable.Add(
                new DxfViewTableRecord
                {
                    Name = "On a sheet",
                    IsPaperspaceView = true,
                    CenterPoint = new GeoPoint2(1, 1),
                    Width = 2,
                    Height = 2,
                }
            );
            Directory.CreateDirectory(ModelFolder);
            string path = Path.Combine(ModelFolder, file);
            CadDocument.From(database, file).SaveAs(path);
            return path;
        }

        /// <summary>Example Tower with the sheet A-101 (a drawing of its own), a resource location of model drawings and the views of two.</summary>
        private SheetSetManagerViewModel Opened(out MainViewModel model, bool withLocation = true)
        {
            Directory.CreateDirectory(SetFolder);
            string dwt = Drawing(Path.Combine(_folder.FullPath, "templates"), "sheets.dwt", "Cover", "A1 sheet");
            Model("Tower model.dwg", ("Ground floor", 8000, 4000, 16000, 8000), ("Roof", 16000, 9250, 32000, 18500));
            Model("Site plan.dwg");
            CadSheetSetFile
                .Create(Dst, "Example Tower", null)
                .Change(set =>
                {
                    set.Template = new CadDrawingReference(
                        CadFileReference.FromPath(dwt, SetFolder),
                        "A1 sheet",
                        null,
                        null
                    );
                    set.NewSheet("A-101", "Ground Floor Plan");
                    set.NewSheet("A-102", "First Floor Plan");

                    if (withLocation)
                    {
                        set.AddResourceLocation(CadFileReference.FromPath(ModelFolder, SetFolder));
                    }
                });
            model = new MainViewModel();
            SheetSetManagerViewModel palette = model.SheetSetManager;
            Assert.True(palette.OpenSet(Dst));
            palette.Tab = "Model Views";
            return palette;
        }

        private static string[] Texts(SheetSetManagerViewModel palette) =>
            palette.ModelRows.Select(r => r.Text).ToArray();

        private static SheetSetTabRow Row(SheetSetManagerViewModel palette, string text) =>
            palette.ModelRows.First(r => r.Text == text);

        private static CadMenuItemViewModel Item(IEnumerable<CadMenuItemViewModel> items, string header) =>
            items.Single(i => i.Header == header);

        private static string[] Headers(IEnumerable<CadMenuItemViewModel> items) =>
            items.Where(i => !i.IsSeparator).Select(i => i.Header).ToArray();

        private static string[] Off(IEnumerable<CadMenuItemViewModel> items) =>
            items.Where(i => !i.IsSeparator && !i.IsEnabled).Select(i => i.Header).ToArray();

        private static void Click(CadMenuItemViewModel item) => item.Command.Execute(null);

        /// <summary>The sheet A-101 opened in the app with its layout current.</summary>
        private CadDocument ShowSheet(MainViewModel model)
        {
            string path = Path.Combine(SetFolder, "A-101 Ground Floor Plan.dwg");
            Assert.True(model.Open(path));
            model.SelectSpace(model.Document.Spaces.Single(s => s.Name == "A-101 Ground Floor Plan"), false);
            return model.Document;
        }

        private static DxfViewport[] ViewportsOf(CadSpace layout) =>
            layout.Block.OfType<DxfViewport>().Where(v => v.Number > 1).ToArray();

        // ---- The tree

        [Fact]
        public void TheTree_IsTheResourceLocationsThenTheirDrawingsClosed_AndAViewsDrawingReadsItsViewsWhenOpened()
        {
            SheetSetManagerViewModel palette = Opened(out _);

            Assert.Equal(new[] { ModelFolder, "Site plan.dwg", "Tower model.dwg" }, Texts(palette));
            Assert.True(Row(palette, ModelFolder).IsBold);
            Assert.False(Row(palette, "Tower model.dwg").IsExpanded);
            Assert.True(Row(palette, "Tower model.dwg").IsExpandable);
            Assert.Equal(new[] { "folder", "drawing", "drawing" }, palette.ModelRows.Select(r => r.Icon).ToArray());
            Assert.Equal(new[] { 0.0, 16.0, 16.0 }, palette.ModelRows.Select(r => r.Indent).ToArray());

            Row(palette, "Tower model.dwg").IsExpanded = true;

            // The views of model space only, in order of name; the one saved in a layout is not listed.
            Assert.Equal(
                new[] { ModelFolder, "Site plan.dwg", "Tower model.dwg", "Ground floor", "Roof" },
                Texts(palette)
            );
            Assert.Equal("view", Row(palette, "Roof").Icon);
            Assert.Equal(32.0, Row(palette, "Roof").Indent);
        }

        [Fact]
        public void ADrawingWithNoViews_LosesItsArrowOnceOpened()
        {
            SheetSetManagerViewModel palette = Opened(out _);
            SheetSetTabRow site = Row(palette, "Site plan.dwg");

            site.IsExpanded = true;

            Assert.False(site.IsExpandable);
            Assert.Equal(new[] { ModelFolder, "Site plan.dwg", "Tower model.dwg" }, Texts(palette));
        }

        [Fact]
        public void ALocationThatIsNotThere_IsRedHasNoDrawings_AndSaysSoInTheDetails()
        {
            SheetSetManagerViewModel palette = Opened(out _);
            string gone = Path.Combine(_folder.FullPath, "Old");
            CadSheetSetFile
                .Open(Dst)
                .Change(set => set.AddResourceLocation(CadFileReference.FromPath(gone, SetFolder)));
            palette.Refresh();

            SheetSetTabRow row = Row(palette, gone);

            Assert.Equal(SheetSetTone.Error, row.Tone);
            Assert.False(row.IsExpandable);

            row.IsSelected = true;

            Assert.Equal(
                "Not found: the folder does not exist.",
                palette.Details.Single(d => d.Label == "Status").Value
            );
        }

        [Fact]
        public void WithNoLocationInTheSet_TheTreeIsEmpty_AndTheTabSaysHowToAddOne()
        {
            SheetSetManagerViewModel palette = Opened(out _, false);

            Assert.Empty(palette.ModelRows);
            Assert.Contains("Add New Location", palette.ModelsHint, StringComparison.Ordinal);
        }

        [Fact]
        public void WhatWasOpenAndWhatWasSelected_StaysSoWhenTheTreeIsBuiltAgain()
        {
            SheetSetManagerViewModel palette = Opened(out _);
            Row(palette, "Tower model.dwg").IsExpanded = true;
            Row(palette, "Roof").IsSelected = true;

            palette.Refresh();

            Assert.True(Row(palette, "Tower model.dwg").IsExpanded);
            Assert.True(Row(palette, "Roof").IsSelected);
            Assert.Equal(
                new[] { ModelFolder, "Site plan.dwg", "Tower model.dwg", "Ground floor", "Roof" },
                Texts(palette)
            );
        }

        [Fact]
        public void Refresh_ReadsTheFolderAndTheViewsAgain()
        {
            SheetSetManagerViewModel palette = Opened(out _);
            Row(palette, "Tower model.dwg").IsExpanded = true;
            Model("Another.dwg");
            Model("Tower model.dwg", ("Section A-A", 1, 1, 2, 2));

            palette.RefreshModelViews();

            Assert.Equal(
                new[] { ModelFolder, "Another.dwg", "Site plan.dwg", "Tower model.dwg", "Section A-A" },
                Texts(palette)
            );
        }

        // ---- The details

        [Fact]
        public void TheDetails_OfAView_NameItsDrawingAndFolder_SayHowBigItIsInTheDrawingsUnit_AndWhenTheFileWasSaved()
        {
            SheetSetManagerViewModel palette = Opened(out _);
            Row(palette, "Tower model.dwg").IsExpanded = true;

            Row(palette, "Roof").IsSelected = true;

            Assert.Equal(
                new[] { "View", "Drawing", "Folder", "Size", "Last saved" },
                palette.Details.Select(d => d.Label).ToArray()
            );
            Assert.Equal("Roof", palette.Details[0].Value);
            Assert.Equal("Tower model.dwg", palette.Details[1].Value);
            Assert.Equal(ModelFolder, palette.Details[2].Value);
            Assert.Equal("32 000 × 18 500 mm", palette.Details[3].Value);
            Assert.Matches(@"^\d{4}-\d{2}-\d{2} \d{2}:\d{2}$", palette.Details[4].Value);
        }

        [Fact]
        public void TheDetails_OfADrawingAndALocation_AreTheirFileAndFolder()
        {
            SheetSetManagerViewModel palette = Opened(out _);

            Row(palette, "Site plan.dwg").IsSelected = true;
            Assert.Equal(
                new[] { "Drawing", "Folder", "Size", "Last saved" },
                palette.Details.Select(d => d.Label).ToArray()
            );
            Assert.EndsWith(" KB", palette.Details.Single(d => d.Label == "Size").Value, StringComparison.Ordinal);

            Row(palette, "Site plan.dwg").IsSelected = false;
            Row(palette, ModelFolder).IsSelected = true;
            Assert.Equal(new[] { "Folder", "Status", "Drawings" }, palette.Details.Select(d => d.Label).ToArray());
            Assert.Equal("2", palette.Details.Single(d => d.Label == "Drawings").Value);
        }

        // ---- The locations

        [Fact]
        public void AddLocation_AddsTheFolderAfterTheOthers_WithItsRelativePath_AndRefusesOneThatIsNotThereOrIsListedAlready()
        {
            SheetSetManagerViewModel palette = Opened(out MainViewModel model);
            string library = Path.Combine(_folder.FullPath, "library");
            Directory.CreateDirectory(library);
            Model("Tower model.dwg");

            Assert.False(palette.AddLocation(Path.Combine(_folder.FullPath, "nowhere")));
            Assert.Contains("is not there", model.History.Last(), StringComparison.Ordinal);
            Assert.False(palette.AddLocation(ModelFolder + "\\"));
            Assert.Contains("already", model.History.Last(), StringComparison.Ordinal);
            Assert.True(palette.AddLocation(library));

            Assert.Equal(new[] { ModelFolder, "Site plan.dwg", "Tower model.dwg", library }, Texts(palette));
            IReadOnlyList<CadFileReference> locations = CadSheetSetFile.Open(Dst).SheetSet.ResourceLocations;
            Assert.Equal(2, locations.Count);
            Assert.Equal(@"..\library", locations[1].RelativeFileName);
        }

        [Fact]
        public void TheAddLocationDialog_AsksForAFolderAndAddsIt_AndCancelAddsNothing()
        {
            SheetSetManagerViewModel palette = Opened(out _);
            string library = Path.Combine(_folder.FullPath, "library");
            Directory.CreateDirectory(library);
            palette.AskFolder = () => null;
            Assert.False(palette.AddLocationDialog());
            Assert.Single(CadSheetSetFile.Open(Dst).SheetSet.ResourceLocations);

            palette.AskFolder = () => library;

            Assert.True(palette.AddLocationDialog());
            Assert.Equal(2, CadSheetSetFile.Open(Dst).SheetSet.ResourceLocations.Count);
        }

        [Fact]
        public void RemoveLocation_TakesTheFolderOutOfTheSet_AndTouchesNoFile()
        {
            SheetSetManagerViewModel palette = Opened(out _);

            Assert.True(palette.RemoveLocation(Row(palette, ModelFolder)));

            Assert.Empty(palette.ModelRows);
            Assert.Empty(CadSheetSetFile.Open(Dst).SheetSet.ResourceLocations);
            Assert.True(File.Exists(Path.Combine(ModelFolder, "Tower model.dwg")));
        }

        // ---- Open

        [Fact]
        public void Open_OpensTheDrawingOfAViewOrADrawing_OpenReadOnlyOpensItForEditingAndSaysSo()
        {
            SheetSetManagerViewModel palette = Opened(out MainViewModel model);
            Row(palette, "Tower model.dwg").IsExpanded = true;

            Assert.True(palette.OpenModelDrawing(Row(palette, "Roof"), false));
            Assert.Equal(
                Path.Combine(ModelFolder, "Tower model.dwg"),
                model.Document.FileName,
                StringComparer.OrdinalIgnoreCase
            );

            Assert.True(palette.OpenModelDrawing(Row(palette, "Site plan.dwg"), true));
            Assert.Equal(
                Path.Combine(ModelFolder, "Site plan.dwg"),
                model.Document.FileName,
                StringComparer.OrdinalIgnoreCase
            );
            Assert.Contains("no read-only", model.History.Last(), StringComparison.Ordinal);
            Assert.False(palette.OpenModelDrawing(Row(palette, ModelFolder), false));
        }

        // ---- The menus

        [Fact]
        public void TheMenuOfAView_HasPlaceOnSheetFirstAndInBold_GreyedWhereTheLayoutCurrentIsNotASheetOfTheSet()
        {
            SheetSetManagerViewModel palette = Opened(out MainViewModel model);
            Row(palette, "Tower model.dwg").IsExpanded = true;

            IReadOnlyList<CadMenuItemViewModel> menu = palette.ModelViewsMenuFor(Row(palette, "Roof"));

            Assert.Equal(
                new[]
                {
                    "Place on Sheet",
                    "Open",
                    "Open Read-Only",
                    "eTransmit…",
                    "Preview/Details Pane",
                    "Expand",
                    "Collapse",
                },
                Headers(menu)
            );
            Assert.Equal(new[] { "Place on Sheet" }, Off(menu));
            Assert.True(Item(menu, "Place on Sheet").IsDefault);
            Assert.True(Item(menu, "Preview/Details Pane").IsChecked);

            CadDocument sheet = ShowSheet(model);
            Assert.True(Item(palette.ModelViewsMenuFor(Row(palette, "Roof")), "Place on Sheet").IsEnabled);

            model.SelectSpace(sheet.ModelSpace, false);
            Assert.False(Item(palette.ModelViewsMenuFor(Row(palette, "Roof")), "Place on Sheet").IsEnabled);
        }

        [Fact]
        public void TheMenuOfADrawingAlsoHasSeeModelSpaceViews_WhichOpensIt_AndOfALocationItsOwnItems()
        {
            SheetSetManagerViewModel palette = Opened(out _);

            IReadOnlyList<CadMenuItemViewModel> menu = palette.ModelViewsMenuFor(Row(palette, "Tower model.dwg"));

            Assert.Contains("See Model Space Views", Headers(menu));
            Click(Item(menu, "See Model Space Views"));
            Assert.True(Row(palette, "Tower model.dwg").IsExpanded);
            Assert.Contains("Roof", Texts(palette));
            Assert.Equal(
                new[] { "Add New Location…", "Remove Location", "Preview/Details Pane", "Expand", "Collapse" },
                Headers(palette.ModelViewsMenuFor(Row(palette, ModelFolder)))
            );
            Assert.Equal(
                new[] { "Add New Location…", "Preview/Details Pane" },
                Headers(palette.ModelViewsMenuFor(null))
            );
        }

        [Fact]
        public void ExpandAndCollapseOfTheMenu_OpenAndCloseEveryRowThatHoldsRows_AndThePaneItemTicksTheOtherWay()
        {
            SheetSetManagerViewModel palette = Opened(out _);
            IReadOnlyList<CadMenuItemViewModel> menu = palette.ModelViewsMenuFor(Row(palette, ModelFolder));

            Click(Item(menu, "Expand"));
            Assert.Contains("Roof", Texts(palette));

            Click(Item(palette.ModelViewsMenuFor(Row(palette, ModelFolder)), "Collapse"));
            Assert.Equal(new[] { ModelFolder }, Texts(palette));

            Click(Item(palette.ModelViewsMenuFor(Row(palette, ModelFolder)), "Preview/Details Pane"));
            Assert.False(palette.PaneShown);
            Assert.False(Item(palette.ModelViewsMenuFor(Row(palette, ModelFolder)), "Preview/Details Pane").IsChecked);
        }

        [Fact]
        public void InALockedSet_AddAndRemoveLocationAndPlaceOnSheetAreGreyedAndRefused()
        {
            Opened(out _);
            File.SetAttributes(Dst, FileAttributes.ReadOnly);

            try
            {
                var model = new MainViewModel();
                var palette = new SheetSetManagerViewModel(model);
                palette.OpenSet(Dst);
                palette.Tab = "Model Views";
                ShowSheet(model);

                Assert.Equal(
                    new[] { "Add New Location…", "Remove Location" },
                    Off(palette.ModelViewsMenuFor(Row(palette, ModelFolder)))
                );
                Row(palette, "Tower model.dwg").IsExpanded = true;
                Assert.Equal(new[] { "Place on Sheet" }, Off(palette.ModelViewsMenuFor(Row(palette, "Roof"))));
                Assert.False(palette.AddLocation(_folder.FullPath));
                Assert.False(palette.PlaceOnSheet(Row(palette, "Roof")));
                Assert.Contains("read-only", model.History.Last(), StringComparison.Ordinal);
            }
            finally
            {
                File.SetAttributes(Dst, FileAttributes.Normal);
            }
        }

        // ---- Place on Sheet

        [Fact]
        public void PlaceOnSheet_LeavesAnAttachXrefAViewportAtTheScaleAndASheetViewInTheSet()
        {
            SheetSetManagerViewModel palette = Opened(out MainViewModel model);
            CadDocument sheet = ShowSheet(model);
            Row(palette, "Tower model.dwg").IsExpanded = true;

            Assert.True(palette.PlaceOnSheet(Row(palette, "Ground floor")));

            // The command line asks for the point, with the default scale; the Scale option lists the drawing's scales.
            Assert.True(model.Session.IsRunning);
            Assert.StartsWith("Specify insertion point", model.Session.Prompt.Message, StringComparison.Ordinal);
            Assert.True(model.Session.Feed("Scale"));
            Assert.Contains("1:100", model.Session.Prompt.Keywords);
            Assert.True(model.Session.Feed("1:50"));
            Assert.True(model.Session.Feed(new GeoPoint2(420, 297)));
            Assert.False(model.Session.IsRunning);

            // The drawing is an attached xref of the sheet's drawing, with one INSERT of it in model space.
            DxfBlockTableRecord xref = sheet.Database.BlockTable["Tower model"];
            Assert.True(xref.IsFromExternalReference);
            Assert.False(xref.IsFromOverlayReference);
            Assert.Single(sheet.ModelSpace.Block.OfType<DxfBlockReference>());

            // The viewport is the view's size at 1:50, centred where the click was.
            DxfViewport viewport = ViewportsOf(sheet.CurrentSpace).Single();
            Assert.Equal(320, viewport.Width, 6);
            Assert.Equal(160, viewport.Height, 6);
            Assert.Equal(420, viewport.CenterPoint.X, 6);
            Assert.Equal(0.02, viewport.CustomScale, 9);

            // The set holds a sheet view: the next number of the sheet, the view's name for a title, and a named view of the sheet's drawing.
            CadSheetSetSheet recorded = CadSheetSetFile.Open(Dst).SheetSet.AllSheets().Single(s => s.Number == "A-101");
            CadSheetView view = Assert.Single(recorded.Views);
            Assert.Equal("1", view.Number);
            Assert.Equal("Ground floor", view.Title);
            Assert.Equal("Ground floor", view.NamedView.Name);
            Assert.Equal(
                sheet.Database.ViewTable["Ground floor"].Handle,
                view.NamedView.Handle,
                StringComparer.OrdinalIgnoreCase
            );
            Assert.Null(view.Category);
            Assert.Equal(
                Path.GetFileName(sheet.FileName),
                Path.GetFileName(view.NamedView.File.Resolve(SetFolder)),
                StringComparer.OrdinalIgnoreCase
            );
            Assert.Empty(CadSheetSetFile.Open(Dst).SheetSet.AllSheets().Single(s => s.Number == "A-102").Views);

            // The Sheet Views tab lists it, with the scale it is placed at.
            palette.Tab = "Sheet Views";
            palette.ViewsBySheet = true;
            Assert.Equal("1:50", palette.ViewRows.Single(r => r.Text == "1 - Ground floor").Detail);
        }

        [Fact]
        public void PlaceOnSheet_IsOneUndoStepOfTheDrawing_AndTheNextViewOfTheSheetTakesTheNextNumber_AtTheLastScale()
        {
            SheetSetManagerViewModel palette = Opened(out MainViewModel model);
            CadDocument sheet = ShowSheet(model);
            Row(palette, "Tower model.dwg").IsExpanded = true;
            palette.PlaceOnSheet(Row(palette, "Roof"));
            model.Session.Feed("Scale");
            model.Session.Feed("1:200");
            model.Session.Feed(new GeoPoint2(200, 150));

            palette.PlaceOnSheet(Row(palette, "Roof"));
            model.Session.Feed(new GeoPoint2(500, 150));

            Assert.Equal(2, ViewportsOf(sheet.CurrentSpace).Length);
            Assert.All(ViewportsOf(sheet.CurrentSpace), v => Assert.Equal(0.005, v.CustomScale, 9));
            CadSheetSetSheet recorded = CadSheetSetFile.Open(Dst).SheetSet.AllSheets().Single(s => s.Number == "A-101");
            Assert.Equal(new[] { "1", "2" }, recorded.Views.Select(v => v.Number).ToArray());
            Assert.Equal(new[] { "Roof", "Roof" }, recorded.Views.Select(v => v.Title).ToArray());
            Assert.Equal(new[] { "Roof", "Roof2" }, recorded.Views.Select(v => v.NamedView.Name).ToArray());

            Assert.True(sheet.Undo());
            Assert.Single(ViewportsOf(sheet.CurrentSpace));
            Assert.True(sheet.Database.BlockTable.Has("Tower model"));
        }

        [Fact]
        public void ADrawingDroppedOnTheDrawingArea_IsPlacedWhereItIsDropped_AtTheDefaultScale_AsAViewOfAllOfModelSpace()
        {
            SheetSetManagerViewModel palette = Opened(out MainViewModel model);
            CadDocument sheet = ShowSheet(model);
            string text = SheetSetModelViewDrag.Write(Path.Combine(ModelFolder, "Tower model.dwg"), "Roof");

            Assert.True(model.DropFromSheetSetManager(text, new GeoPoint2(300, 200)));

            DxfViewport viewport = ViewportsOf(sheet.CurrentSpace).Single();
            Assert.Equal(320, viewport.Width, 6);
            Assert.Equal(185, viewport.Height, 6);
            Assert.Equal(300, viewport.CenterPoint.X, 6);
            Assert.Equal(0.01, viewport.CustomScale, 9);
            Assert.Equal(
                "Roof",
                CadSheetSetFile.Open(Dst).SheetSet.AllSheets().Single(s => s.Number == "A-101").Views.Single().Title
            );

            Assert.True(
                model.DropFromSheetSetManager(
                    SheetSetModelViewDrag.Write(Path.Combine(ModelFolder, "Site plan.dwg"), null),
                    new GeoPoint2(100, 100)
                )
            );
            Assert.Equal(
                new[] { "Roof", "Site plan" },
                CadSheetSetFile
                    .Open(Dst)
                    .SheetSet.AllSheets()
                    .Single(s => s.Number == "A-101")
                    .Views.Select(v => v.Title)
                    .ToArray()
            );
            Assert.False(model.DropFromSheetSetManager("not a drag", new GeoPoint2(0, 0)));
            Assert.False(model.DropFromSheetSetManager(text, null));
        }

        [Fact]
        public void PlaceOnSheet_IsRefusedWhereTheLayoutCurrentIsModelOrNoSheetOfTheSet_AndSaysSo()
        {
            SheetSetManagerViewModel palette = Opened(out MainViewModel model);
            Row(palette, "Tower model.dwg").IsExpanded = true;

            Assert.False(palette.PlaceOnSheet(Row(palette, "Roof")));
            Assert.Contains("needs the layout current to be a sheet", model.History.Last(), StringComparison.Ordinal);

            CadDocument sheet = ShowSheet(model);
            model.SelectSpace(sheet.ModelSpace, false);
            Assert.False(palette.PlaceOnSheet(Row(palette, "Roof")));

            model.SelectSpace(sheet.Spaces.Single(s => s.Name == "A-101 Ground Floor Plan"), false);
            Assert.False(palette.PlaceOnSheet(Row(palette, ModelFolder)));
            Assert.False(model.Session.IsRunning);
            Assert.Empty(CadSheetSetFile.Open(Dst).SheetSet.AllSheets().SelectMany(s => s.Views));
        }

        [Fact]
        public void ADragOfAViewOrADrawing_WritesItsFileAndNameAsTheDropReadsThem()
        {
            string text = SheetSetModelViewDrag.Write(@"D:\Model\Tower model.dwg", "Ground floor");

            Assert.True(SheetSetModelViewDrag.TryRead(text, out string file, out string view));
            Assert.Equal(@"D:\Model\Tower model.dwg", file);
            Assert.Equal("Ground floor", view);
            Assert.True(
                SheetSetModelViewDrag.TryRead(SheetSetModelViewDrag.Write(@"D:\a.dwg", null), out file, out view)
            );
            Assert.Equal(@"D:\a.dwg", file);
            Assert.Null(view);
            Assert.False(SheetSetModelViewDrag.TryRead("nothing", out _, out _));
            Assert.False(SheetSetModelViewDrag.TryRead(null, out _, out _));
        }
    }
}
