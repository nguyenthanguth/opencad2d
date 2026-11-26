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
    /// The Sheet Views tab of the Sheet Set Manager palette (A12 4d): the tree of the views placed on the sheets by category and
    /// by sheet, New View Category, Rename, Remove Category, Set Category, Rename &amp; Renumber View and its Previous and Next, Display,
    /// the right-click menus and which of their items are on, and the two dialogs' view models. Sets and drawings are built in code
    /// in a temporary folder.
    /// </summary>
    public class SheetSetViewsTabTests : IDisposable
    {
        private readonly TestFolder _folder = new TestFolder();

        public void Dispose() => _folder.Dispose();

        private string SetFolder => Path.Combine(_folder.FullPath, "tower");

        private string Dst => Path.Combine(SetFolder, "Example Tower.dst");

        /// <summary>A drawing with the layouts named, a line on each and a circle in model space, written as a DWG (or .dwt) and its path given.</summary>
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

        /// <summary>A model drawing with two views of model space, Ground floor (16 000 by 8 000) and Roof (32 000 by 18 500).</summary>
        private string Model()
        {
            var database = new DxfDatabase("AC1032");
            database.Insunits = DxfUnits.Millimeters;
            database.ModelSpace.AppendEntity(new DxfLine(new GeoPoint3(0, 0, 0), new GeoPoint3(32000, 18500, 0)));
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
                    Name = "Roof",
                    CenterPoint = new GeoPoint2(16000, 9250),
                    Width = 32000,
                    Height = 18500,
                }
            );
            string folder = Path.Combine(_folder.FullPath, "model");
            Directory.CreateDirectory(folder);
            string path = Path.Combine(folder, "Tower model.dwg");
            CadDocument.From(database, "Tower model.dwg").SaveAs(path);
            return path;
        }

        /// <summary>
        /// Example Tower with the sheets A-101 and A-102 in the subset Architectural and A-501 in Structural, the view categories
        /// Plans, Elevations and Sections, and the views 1 - Ground Floor Plan (Plans, A-101), 1 - First Floor Plan (Plans, A-102),
        /// 2 - Entrance Detail (no category, A-101) and 1 - Section A-A (Sections, A-501).
        /// </summary>
        private CadSheetSetFile Tower()
        {
            Directory.CreateDirectory(SetFolder);
            string dwt = Drawing(Path.Combine(_folder.FullPath, "templates"), "sheets.dwt", "Cover", "A1 sheet");
            CadSheetSetFile file = CadSheetSetFile.Create(Dst, "Example Tower", null);
            file.Change(set =>
            {
                set.Template = new CadDrawingReference(
                    CadFileReference.FromPath(dwt, SetFolder),
                    "A1 sheet",
                    null,
                    null
                );
                CadSheetSubset architectural = set.NewSubset("Architectural", null, false);
                CadSheetSetSheet a101 = architectural.NewSheet("A-101", "Ground Floor Plan");
                CadSheetSetSheet a102 = architectural.NewSheet("A-102", "First Floor Plan");
                CadSheetSetSheet a501 = set.NewSubset("Structural", null, false).NewSheet("A-501", "Wall Sections");
                CadViewCategory plans = set.AddViewCategory("Plans", null);
                set.AddViewCategory("Elevations", null);
                CadViewCategory sections = set.AddViewCategory("Sections", null);
                a101.AddView("1", "Ground Floor Plan", null, plans);
                a102.AddView("1", "First Floor Plan", null, plans);
                a101.AddView("2", "Entrance Detail", null, null);
                a501.AddView("1", "Section A-A", null, sections);
            });
            return file;
        }

        private SheetSetManagerViewModel Opened(out MainViewModel model, string tab = "Sheet Views")
        {
            Tower();
            model = new MainViewModel();
            var palette = new SheetSetManagerViewModel(model);
            Assert.True(palette.OpenSet(Dst));
            palette.Tab = tab;
            return palette;
        }

        private static string[] Texts(SheetSetManagerViewModel palette) =>
            palette.ViewRows.Select(r => r.Text).ToArray();

        private static SheetSetTabRow Row(SheetSetManagerViewModel palette, string text) =>
            palette.ViewRows.First(r => r.Text == text);

        private static CadMenuItemViewModel Item(IEnumerable<CadMenuItemViewModel> items, string header) =>
            items.Single(i => i.Header == header);

        private static string[] Headers(IEnumerable<CadMenuItemViewModel> items) =>
            items.Where(i => !i.IsSeparator).Select(i => i.Header).ToArray();

        private static string[] Off(IEnumerable<CadMenuItemViewModel> items) =>
            items.Where(i => !i.IsSeparator && !i.IsEnabled).Select(i => i.Header).ToArray();

        private static void Click(CadMenuItemViewModel item) => item.Command.Execute(null);

        // ---- The tree

        [Fact]
        public void ByCategory_TheTreeIsTheSetThenItsCategoriesInFileOrder_EachWithItsViewsInSheetOrder_AndViewsWithNoCategoryLast()
        {
            SheetSetManagerViewModel palette = Opened(out _);

            Assert.True(palette.ViewsByCategory);
            Assert.Equal(
                new[]
                {
                    "Example Tower",
                    "Plans",
                    "1 - Ground Floor Plan",
                    "1 - First Floor Plan",
                    "Elevations",
                    "Sections",
                    "1 - Section A-A",
                    "2 - Entrance Detail",
                },
                Texts(palette)
            );
            Assert.Equal(
                new[] { "A-101", "A-102", "A-501", "A-101" },
                palette.ViewRows.Where(r => r.Kind == SheetSetTabRowKind.View).Select(r => r.Detail).ToArray()
            );
            Assert.Equal(
                new[] { "set", "category", "view", "view", "category", "category", "view", "view" },
                palette.ViewRows.Select(r => r.Icon).ToArray()
            );
            Assert.True(Row(palette, "Example Tower").IsBold);
            Assert.False(Row(palette, "Elevations").IsExpandable);
            Assert.True(Row(palette, "Plans").IsExpandable);
            Assert.Equal(new[] { 0.0, 16.0, 32.0 }, palette.ViewRows.Take(3).Select(r => r.Indent).ToArray());
        }

        [Fact]
        public void ACategoryWithNoName_IsLeftOutOfTheTree_AndItsViewsSitUnderTheSet()
        {
            SheetSetManagerViewModel palette = Opened(out _);

            // A new set holds one category with no name, which views may name; it is shown under no name of its own.
            CadSheetSetFile file = CadSheetSetFile.Open(Dst);
            file.Change(set =>
            {
                CadViewCategory blank = set.ViewCategories.Single(c => c.Name.Length == 0);
                set.AllSheets().Single(s => s.Number == "A-501").Views[0].Category = blank;
            });
            palette.Refresh();

            Assert.DoesNotContain(palette.ViewRows, r => r.Kind == SheetSetTabRowKind.Category && r.Text.Length == 0);
            Assert.Equal(
                new[] { "2 - Entrance Detail", "1 - Section A-A" },
                palette
                    .ViewRows.Where(r => r.Kind == SheetSetTabRowKind.View && r.Parent == palette.ViewRows[0])
                    .Select(r => r.Text)
                    .ToArray()
            );
        }

        [Fact]
        public void BySheet_TheTreeIsTheSheetsThatHoldViews_InSetOrder_EachViewWithTheScaleItIsPlacedAt()
        {
            SheetSetManagerViewModel palette = Opened(out MainViewModel model);
            string a101 = Path.Combine(SetFolder, "A-101 Ground Floor Plan.dwg");
            string modelFile = Model();
            CadDocument sheet = CadDocument.Open(a101);
            sheet.CurrentSpace = sheet.Spaces.Single(s => s.Name == "A-101 Ground Floor Plan");
            CadPlaceOnSheetResult placed = CadSheetSetPlacement.Place(
                sheet,
                sheet.CurrentSpace,
                CadSheetSetPlacement.Plan(modelFile, "Ground floor"),
                new GeoPoint2(300, 200),
                "1:100"
            );
            sheet.Save();
            CadSheetSetFile
                .Open(Dst)
                .Change(set =>
                {
                    CadSheetView view = set.AllSheets().Single(s => s.Number == "A-101").Views[0];
                    set.AllSheets()
                        .Single(s => s.Number == "A-101")
                        .AddView(
                            "3",
                            "Placed",
                            new CadDrawingReference(
                                CadFileReference.FromPath(a101, SetFolder),
                                placed.NamedViewName,
                                placed.NamedViewHandle,
                                placed.NamedViewOwnerHandle
                            ),
                            null
                        );
                });

            palette.Refresh();
            palette.ViewsBySheet = true;

            Assert.Equal(
                new[]
                {
                    "Example Tower",
                    "A-101 - Ground Floor Plan",
                    "1 - Ground Floor Plan",
                    "2 - Entrance Detail",
                    "3 - Placed",
                    "A-102 - First Floor Plan",
                    "1 - First Floor Plan",
                    "A-501 - Wall Sections",
                    "1 - Section A-A",
                },
                Texts(palette)
            );
            Assert.Equal("1:100", Row(palette, "3 - Placed").Detail);
            Assert.Equal(string.Empty, Row(palette, "2 - Entrance Detail").Detail);
            Assert.False(palette.ViewsByCategory);
            Assert.Equal("sheet", Row(palette, "A-102 - First Floor Plan").Icon);
        }

        [Fact]
        public void WithNoViewsInTheSet_TheTreeIsTheSetAlone_AndTheTabSaysHowToPlaceOne()
        {
            Tower();
            CadSheetSetFile
                .Open(Dst)
                .Change(set =>
                {
                    foreach (CadSheetSetSheet sheet in set.AllSheets())
                    {
                        foreach (CadSheetView view in sheet.Views.ToList())
                        {
                            sheet.RemoveView(view);
                        }
                    }
                });
            var model = new MainViewModel();
            var palette = new SheetSetManagerViewModel(model);
            palette.OpenSet(Dst);
            palette.Tab = "Sheet Views";

            Assert.Equal(new[] { "Example Tower", "Plans", "Elevations", "Sections" }, Texts(palette));
            Assert.Contains("Place on Sheet", palette.ViewsHint, StringComparison.Ordinal);
        }

        [Fact]
        public void TheTabsTree_IsMadeOnlyWhereItsTabIsShown_AndTheSheetListsRowsAreUntouched()
        {
            SheetSetManagerViewModel palette = Opened(out _, "Sheet List");

            Assert.Empty(palette.ViewRows);
            Assert.True(palette.ShowTree);
            Assert.True(palette.IsSheetListTab);
            Assert.False(palette.ShowTabBody);

            palette.Tab = "Sheet Views";

            Assert.False(palette.ShowTree);
            Assert.True(palette.ShowTabBody);
            Assert.True(palette.IsSheetViewsTab);
            Assert.NotEmpty(palette.ViewRows);
            Assert.Equal(string.Empty, palette.HintText);
        }

        [Fact]
        public void ACollapsedCategoryStaysCollapsed_AndTheSelectionStays_WhenTheTreeIsBuiltAgain()
        {
            SheetSetManagerViewModel palette = Opened(out _);
            Row(palette, "Plans").IsExpanded = false;
            Row(palette, "Sections").IsSelected = true;
            Assert.DoesNotContain("1 - Ground Floor Plan", Texts(palette));

            palette.Refresh();

            Assert.DoesNotContain("1 - Ground Floor Plan", Texts(palette));
            Assert.False(Row(palette, "Plans").IsExpanded);
            Assert.True(Row(palette, "Sections").IsSelected);
        }

        // ---- The details

        [Fact]
        public void TheDetails_OfAView_AreItsNumberAndTitle_ItsSheet_ItsCategory_AndTheScale()
        {
            SheetSetManagerViewModel palette = Opened(out _);

            Row(palette, "1 - First Floor Plan").IsSelected = true;

            Assert.Equal(
                new[] { "View", "Sheet", "Category", "Scale" },
                palette.Details.Select(d => d.Label).ToArray()
            );
            Assert.Equal("1 - First Floor Plan", palette.Details[0].Value);
            Assert.Equal("A-102 - First Floor Plan", palette.Details[1].Value);
            Assert.Equal("Plans", palette.Details[2].Value);

            Row(palette, "2 - Entrance Detail").IsSelected = true;
            Row(palette, "1 - First Floor Plan").IsSelected = false;

            Assert.Equal("None", palette.Details.Single(d => d.Label == "Category").Value);
        }

        [Fact]
        public void TheDetails_OfACategoryAndTheSet_CountTheirViews()
        {
            SheetSetManagerViewModel palette = Opened(out _);

            Row(palette, "Plans").IsSelected = true;
            Assert.Equal("2", palette.Details.Single(d => d.Label == "Views").Value);

            Row(palette, "Plans").IsSelected = false;
            Row(palette, "Example Tower").IsSelected = true;
            Assert.Equal("4", palette.Details.Single(d => d.Label == "Views").Value);
            Assert.Equal("3", palette.Details.Single(d => d.Label == "Categories").Value);
        }

        // ---- View categories

        [Fact]
        public void NewViewCategory_AddsItAfterTheOthers_ButNotAnEmptyNameOrOneAnotherCategoryHasInAnyCase()
        {
            SheetSetManagerViewModel palette = Opened(out MainViewModel model);

            Assert.False(palette.NewViewCategory("  "));
            Assert.Equal("A view category is named.", model.History.Last());
            Assert.False(palette.NewViewCategory("plans"));
            Assert.Equal("The sheet set has a view category named plans.", model.History.Last());
            Assert.True(palette.NewViewCategory("Details"));

            Assert.Equal("Details", Row(palette, "Details").Text);
            Assert.Equal(
                new[] { "Plans", "Elevations", "Sections", "Details" },
                CadSheetSetFile
                    .Open(Dst)
                    .SheetSet.ViewCategories.Where(c => c.Name.Length > 0)
                    .Select(c => c.Name)
                    .ToArray()
            );
        }

        [Fact]
        public void RenameViewCategory_KeepsItsViews_AndRefusesAnotherCategorysName_ButNotItsOwnInAnotherCase()
        {
            SheetSetManagerViewModel palette = Opened(out _);
            SheetSetTabRow plans = Row(palette, "Plans");

            Assert.False(palette.RenameViewCategory(plans, "Sections"));
            Assert.True(palette.RenameViewCategory(plans, "PLANS"));
            Assert.True(palette.RenameViewCategory(Row(palette, "PLANS"), "Floor Plans"));

            Assert.Equal(
                new[] { "1 - Ground Floor Plan", "1 - First Floor Plan" },
                Row(palette, "Floor Plans").Children.Select(c => c.Text).ToArray()
            );
        }

        [Fact]
        public void RemoveViewCategory_IsRefusedWhileItHoldsViews_AndTakesAnEmptyOneOut()
        {
            SheetSetManagerViewModel palette = Opened(out MainViewModel model);

            Assert.False(palette.RemoveViewCategory(Row(palette, "Plans")));
            Assert.Contains("holds views", model.History.Last(), StringComparison.Ordinal);
            Assert.True(palette.RemoveViewCategory(Row(palette, "Elevations")));

            Assert.DoesNotContain("Elevations", Texts(palette));
            Assert.Contains("Plans", Texts(palette));
        }

        [Fact]
        public void SetCategory_PutsViewsInACategory_AndNullTakesThemOutOfAny()
        {
            SheetSetManagerViewModel palette = Opened(out _);

            Assert.True(
                palette.SetCategory(
                    new[] { Row(palette, "2 - Entrance Detail"), Row(palette, "1 - First Floor Plan") },
                    Row(palette, "Elevations")
                )
            );

            Assert.Equal(
                new[] { "2 - Entrance Detail", "1 - First Floor Plan" },
                Row(palette, "Elevations").Children.Select(c => c.Text).ToArray()
            );
            Assert.Equal(
                new[] { "1 - Ground Floor Plan" },
                Row(palette, "Plans").Children.Select(c => c.Text).ToArray()
            );

            Assert.True(palette.SetCategory(new[] { Row(palette, "1 - Section A-A") }, null));

            Assert.Empty(Row(palette, "Sections").Children);
            Assert.Equal("Example Tower", palette.ViewRows.Single(r => r.Text == "1 - Section A-A").Parent.Text);
        }

        // ---- Rename & Renumber View

        [Fact]
        public void RenameAndRenumberView_ChangesItsNumberAndTitleInTheSet_AndRefusesAnEmptyTitle()
        {
            SheetSetManagerViewModel palette = Opened(out MainViewModel model);

            Assert.False(palette.RenameAndRenumberView(Row(palette, "2 - Entrance Detail"), "2", " "));
            Assert.Equal("A view has a title.", model.History.Last());
            Assert.True(palette.RenameAndRenumberView(Row(palette, "2 - Entrance Detail"), " 5 ", " Entrance Canopy "));

            Assert.Contains("5 - Entrance Canopy", Texts(palette));
            CadSheetView view = CadSheetSetFile
                .Open(Dst)
                .SheetSet.AllSheets()
                .Single(s => s.Number == "A-101")
                .Views.Single(v => v.Number == "5");
            Assert.Equal("Entrance Canopy", view.Title);
        }

        [Fact]
        public void TheRenameDialog_WalksTheViewsInTheOrderOfTheTree_PreviousAndNextMakeTheChangesFirst_AndGoGreyAtTheEnds()
        {
            SheetSetManagerViewModel palette = Opened(out _);
            RenameRenumberViewDialogViewModel dialog = null;
            palette.ShowRenameRenumberViewDialog = d =>
            {
                dialog = d;
                return true;
            };

            Assert.True(palette.RenameAndRenumberViewDialog(Row(palette, "1 - First Floor Plan")));

            Assert.Equal("1", dialog.Number);
            Assert.Equal("First Floor Plan", dialog.Title);
            Assert.Equal("Sheet: A-102 - First Floor Plan.", dialog.Info);
            Assert.True(dialog.CanPrevious);
            Assert.True(dialog.CanNext);

            dialog.Number = "7";
            Assert.True(dialog.Next());

            // Next made the change of the view before it, and loaded the view after it in the tree: Elevations has none, so Sections'.
            Assert.Contains("7 - First Floor Plan", Texts(palette));
            Assert.Equal("Section A-A", dialog.Title);
            Assert.Equal("Sheet: A-501 - Wall Sections.", dialog.Info);

            dialog.Title = "Section B-B";
            Assert.True(dialog.Next());
            Assert.Equal("Entrance Detail", dialog.Title);
            Assert.False(dialog.CanNext);
            Assert.False(dialog.Next());
            Assert.True(dialog.Previous());
            Assert.Equal("Section B-B", dialog.Title);
            Assert.True(dialog.Previous());
            Assert.True(dialog.Previous());
            Assert.Equal("Ground Floor Plan", dialog.Title);
            Assert.False(dialog.CanPrevious);
            Assert.Contains("1 - Section B-B", Texts(palette));
        }

        [Fact]
        public void TheRenameDialog_RefusesAnEmptyTitle_GreysOkPreviousAndNext_AndChangesNothing()
        {
            SheetSetManagerViewModel palette = Opened(out _);
            RenameRenumberViewDialogViewModel dialog = null;
            palette.ShowRenameRenumberViewDialog = d =>
            {
                dialog = d;
                return true;
            };
            palette.RenameAndRenumberViewDialog(Row(palette, "1 - First Floor Plan"));

            dialog.Title = "  ";

            Assert.Equal("A view has a title.", dialog.Error);
            Assert.False(dialog.CanOk);
            Assert.False(dialog.CanNext);
            Assert.False(dialog.CanPrevious);
            Assert.False(dialog.Accept());
            Assert.Contains("1 - First Floor Plan", Texts(palette));
        }

        // ---- The View Category dialog

        [Fact]
        public void TheViewCategoryDialog_NewAsksForAName_RefusesAnEmptyOrTakenOne_AndOkAddsTheCategory()
        {
            SheetSetManagerViewModel palette = Opened(out _);
            ViewCategoryDialogViewModel seen = null;
            palette.ShowViewCategoryDialog = d =>
            {
                seen = d;
                Assert.Equal(string.Empty, d.Name);
                Assert.False(d.CanOk);
                Assert.Equal("A view category is named.", d.Error);
                d.Name = "sections";
                Assert.Equal("The sheet set has a view category named sections.", d.Error);
                d.Name = "  Details ";
                Assert.True(d.CanOk);
                Assert.Null(d.Error);
                Assert.False(d.CanAddBlocks);
                Assert.Equal("The set names no callout blocks.", d.CalloutHint);
                return d.Accept();
            };

            Assert.True(palette.NewViewCategoryDialog());

            Assert.Equal("Details", seen.Result);
            Assert.Contains("Details", Texts(palette));
        }

        [Fact]
        public void TheViewCategoryDialog_OfACategory_ShowsItsName_AllowsItsOwnNameAndRenamesOnOk()
        {
            SheetSetManagerViewModel palette = Opened(out _);
            palette.ShowViewCategoryDialog = d =>
            {
                Assert.Equal("Plans", d.Name);
                Assert.True(d.CanOk);
                d.Name = "Floor Plans";
                return d.Accept();
            };

            Assert.True(palette.EditViewCategoryDialog(Row(palette, "Plans")));

            Assert.Contains("Floor Plans", Texts(palette));
            Assert.DoesNotContain("Plans", Texts(palette));
        }

        [Fact]
        public void ACanceledViewCategoryDialog_ChangesNothing()
        {
            SheetSetManagerViewModel palette = Opened(out _);
            string[] before = Texts(palette);
            palette.ShowViewCategoryDialog = d =>
            {
                d.Name = "Details";
                return false;
            };

            Assert.False(palette.NewViewCategoryDialog());

            Assert.Equal(before, Texts(palette));
        }

        // ---- The menus

        [Fact]
        public void TheMenuOfAView_HasDisplayFirstAndInBold_PlaceViewLabelBlockAndPropertiesGreyed_AndSetCategoryTicksItsCategory()
        {
            SheetSetManagerViewModel palette = Opened(out _);

            IReadOnlyList<CadMenuItemViewModel> menu = palette.SheetViewsMenuFor(Row(palette, "1 - First Floor Plan"));

            Assert.Equal(
                new[]
                {
                    "Display",
                    "New View Category…",
                    "Place View Label Block",
                    "Rename & Renumber…",
                    "Set Category",
                    "Properties…",
                },
                Headers(menu)
            );
            Assert.Equal(new[] { "Place View Label Block", "Properties…" }, Off(menu));
            Assert.True(Item(menu, "Display").IsDefault);
            CadMenuItemViewModel categories = Item(menu, "Set Category");
            Assert.Equal(new[] { "Plans", "Elevations", "Sections" }, Headers(categories.Items));
            Assert.Equal(new[] { true, false, false }, categories.Items.Select(i => i.IsChecked).ToArray());
        }

        [Fact]
        public void SetCategoryOfTheMenu_ActsOnEveryViewSelected_WhereTheRowClickedIsOneOfThem()
        {
            SheetSetManagerViewModel palette = Opened(out _);
            Row(palette, "1 - Ground Floor Plan").IsSelected = true;
            Row(palette, "1 - First Floor Plan").IsSelected = true;

            Click(
                Item(
                    Item(palette.SheetViewsMenuFor(Row(palette, "1 - Ground Floor Plan")), "Set Category").Items,
                    "Elevations"
                )
            );

            Assert.Equal(
                new[] { "1 - Ground Floor Plan", "1 - First Floor Plan" },
                Row(palette, "Elevations").Children.Select(c => c.Text).ToArray()
            );
        }

        [Fact]
        public void TheMenuOfACategory_HasRemoveCategoryGreyedWhileItHoldsViews_AndOfTheSetOrNoRowJustNewViewCategory()
        {
            SheetSetManagerViewModel palette = Opened(out _);

            Assert.Equal(
                new[] { "New View Category…", "Rename…", "Remove Category", "Properties…" },
                Headers(palette.SheetViewsMenuFor(Row(palette, "Plans")))
            );
            Assert.Equal(new[] { "Remove Category" }, Off(palette.SheetViewsMenuFor(Row(palette, "Plans"))));
            Assert.Empty(Off(palette.SheetViewsMenuFor(Row(palette, "Elevations"))));
            Assert.Equal(
                new[] { "New View Category…" },
                Headers(palette.SheetViewsMenuFor(Row(palette, "Example Tower")))
            );
            Assert.Equal(new[] { "New View Category…" }, Headers(palette.SheetViewsMenuFor(null)));
        }

        [Fact]
        public void InALockedSet_EveryChangeOfTheTabIsGreyedAndRefused_ButDisplayStaysOn()
        {
            Tower();
            File.SetAttributes(Dst, FileAttributes.ReadOnly);

            try
            {
                var model = new MainViewModel();
                var palette = new SheetSetManagerViewModel(model);
                palette.OpenSet(Dst);
                palette.Tab = "Sheet Views";
                IReadOnlyList<CadMenuItemViewModel> menu = palette.SheetViewsMenuFor(
                    Row(palette, "1 - First Floor Plan")
                );

                Assert.Equal(
                    new[]
                    {
                        "New View Category…",
                        "Place View Label Block",
                        "Rename & Renumber…",
                        "Set Category",
                        "Properties…",
                    },
                    Off(menu)
                );
                Assert.True(Item(menu, "Display").IsEnabled);
                Assert.False(palette.NewViewCategory("Details"));
                Assert.Contains("read-only", model.History.Last(), StringComparison.Ordinal);
                Assert.False(palette.RenameAndRenumberView(Row(palette, "1 - First Floor Plan"), "9", "No"));
            }
            finally
            {
                File.SetAttributes(Dst, FileAttributes.Normal);
            }
        }

        [Fact]
        public void TheMenuOfASheetOfTheBySheetList_OpensItsDrawing_AndOfNoRowOffersNewViewCategory()
        {
            SheetSetManagerViewModel palette = Opened(out MainViewModel model);
            palette.ViewsBySheet = true;

            IReadOnlyList<CadMenuItemViewModel> menu = palette.SheetViewsMenuFor(
                Row(palette, "A-102 - First Floor Plan")
            );

            Assert.Equal(new[] { "Open", "New View Category…" }, Headers(menu));
            Click(Item(menu, "Open"));
            Assert.Equal("A-102 First Floor Plan", model.Document.CurrentSpace.Name);
        }

        // ---- Display

        [Fact]
        public void Display_OpensTheSheetsDrawingOnItsLayout_AndShowsTheNamedViewOfTheView()
        {
            SheetSetManagerViewModel palette = Opened(out MainViewModel model);
            string a101 = Path.Combine(SetFolder, "A-101 Ground Floor Plan.dwg");
            CadDocument sheet = CadDocument.Open(a101);
            sheet.CurrentSpace = sheet.Spaces.Single(s => s.Name == "A-101 Ground Floor Plan");
            CadPlaceOnSheetResult placed = CadSheetSetPlacement.Place(
                sheet,
                sheet.CurrentSpace,
                CadSheetSetPlacement.Plan(Model(), "Roof"),
                new GeoPoint2(300, 200),
                "1:200"
            );
            sheet.Save();
            CadSheetSetFile
                .Open(Dst)
                .Change(set =>
                    set.AllSheets()
                        .Single(s => s.Number == "A-101")
                        .AddView(
                            "3",
                            "Roof",
                            new CadDrawingReference(
                                CadFileReference.FromPath(a101, SetFolder),
                                placed.NamedViewName,
                                placed.NamedViewHandle,
                                placed.NamedViewOwnerHandle
                            ),
                            null
                        )
                );
            palette.Refresh();

            Assert.True(palette.DisplayView(Row(palette, "3 - Roof")));

            Assert.Equal(a101, model.Document.FileName, StringComparer.OrdinalIgnoreCase);
            Assert.Equal("A-101 Ground Floor Plan", model.Document.CurrentSpace.Name);
            Assert.False(model.Session.IsRunning);
            Assert.True(palette.ViewRows.Count > 0);
        }

        [Fact]
        public void Display_OfAViewOnASheetWhoseDrawingIsGone_SaysSoAndOpensNothing()
        {
            SheetSetManagerViewModel palette = Opened(out MainViewModel model);
            File.Delete(Path.Combine(SetFolder, "A-102 First Floor Plan.dwg"));
            palette.Refresh();

            Assert.False(palette.DisplayView(Row(palette, "1 - First Floor Plan")));

            Assert.Contains("not found", model.History.Last(), StringComparison.Ordinal);
            Assert.Empty(model.OpenDocuments);
        }
    }
}
