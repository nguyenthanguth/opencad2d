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
    /// The logic of the Sheet Set Manager palette (A12 4a): the tree built from a sheet set, what each row says of its
    /// drawing, the three context menus and which of their items are on, drag and drop, and the operations the menus run
    /// (Open, New Subset, New Sheet, Import, Rename and Renumber, Remove, Resave All Sheets), with the drawings and sets
    /// built in code in a temporary folder.
    /// </summary>
    public class SheetSetManagerViewModelTests : IDisposable
    {
        private readonly TestFolder _folder = new TestFolder();

        public void Dispose() => _folder.Dispose();

        private string SetFolder => Path.Combine(_folder.FullPath, "tower");

        private string Dst => Path.Combine(SetFolder, "Example Tower.dst");

        private static string[] LayoutsOf(string path) =>
            CadDrawingFile
                .Open(path)
                .Layouts.Where(l => l.LayoutName != "Model")
                .OrderBy(l => l.TabOrder)
                .Select(l => l.LayoutName)
                .ToArray();

        /// <summary>A drawing with the layouts named, a line on each and a circle in model space, written as a DWG (or .dwt) and its path given.</summary>
        private string Drawing(string folder, string file, params string[] layouts)
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

        /// <summary>
        /// Example Tower as the design draws it, in a folder of its own: G-001 at the top, Architectural holding A-101, A-102 and
        /// the nested Details with A-501, then Structural with S-101. New sheets are made from the "A1 sheet" layout of a template.
        /// </summary>
        private CadSheetSetFile Tower()
        {
            Directory.CreateDirectory(SetFolder);
            string dwt = Drawing(Path.Combine(_folder.FullPath, "templates"), "sheets.dwt", "Cover", "A1 sheet");
            CadSheetSetFile file = CadSheetSetFile.Create(Dst, "Example Tower", "Six-storey residential tower");
            file.Change(set =>
            {
                set.Template = new CadDrawingReference(
                    CadFileReference.FromPath(dwt, SetFolder),
                    "A1 sheet",
                    null,
                    null
                );
                set.NewSheet("G-001", "Cover Sheet");
                CadSheetSubset architectural = set.NewSubset("Architectural", null, false);
                architectural.NewSheet("A-101", "Ground Floor Plan");
                architectural.NewSheet("A-102", "First Floor Plan");
                architectural.NewSubset("Details", null, false).NewSheet("A-501", "Wall Sections");
                set.NewSubset("Structural", null, false).NewSheet("S-101", "Foundation Plan");
            });
            return file;
        }

        private static SheetSetManagerViewModel Palette(out MainViewModel model)
        {
            model = new MainViewModel();
            return new SheetSetManagerViewModel(model);
        }

        private SheetSetManagerViewModel Opened(out MainViewModel model)
        {
            Tower();
            SheetSetManagerViewModel palette = Palette(out model);
            Assert.True(palette.OpenSet(Dst));
            return palette;
        }

        private static SheetSetNodeViewModel Node(SheetSetManagerViewModel palette, string text) =>
            palette.Rows.Single(r => r.Text == text);

        private static string[] Texts(SheetSetManagerViewModel palette) => palette.Rows.Select(r => r.Text).ToArray();

        private static string[] Headers(IEnumerable<CadMenuItemViewModel> items) =>
            items.Where(i => !i.IsSeparator).Select(i => i.Header).ToArray();

        private static CadMenuItemViewModel Item(IEnumerable<CadMenuItemViewModel> items, string header) =>
            items.Single(i => i.Header == header);

        private static string[] Off(IEnumerable<CadMenuItemViewModel> items) =>
            items.Where(i => !i.IsSeparator && !i.IsEnabled).Select(i => i.Header).ToArray();

        private string SheetFile(string file) => Path.Combine(SetFolder, file);

        private static void Click(CadMenuItemViewModel item) => item.Command.Execute(null);

        // ---- The tree

        [Fact]
        public void WithNoSetOpen_TheControlReadsOpen_TheTreeIsEmptyAndTheHintSaysWhy()
        {
            SheetSetManagerViewModel palette = Palette(out _);

            Assert.False(palette.HasSet);
            Assert.Equal("Open…", palette.ControlText);
            Assert.Empty(palette.Rows);
            Assert.Equal(
                "No sheet set is open." + Environment.NewLine + "Pick New Sheet Set… or Open… in the list above.",
                palette.EmptyHint
            );
            Assert.Equal("Sheet List", palette.Tab);
        }

        [Fact]
        public void TheTree_IsTheSetThenItsSubsetsAndSheetsInSetOrder_EveryNodeOpenAtFirst()
        {
            SheetSetManagerViewModel palette = Opened(out _);

            Assert.True(palette.HasSet);
            Assert.Equal("Example Tower", palette.ControlText);
            Assert.Equal(
                new[]
                {
                    "Example Tower",
                    "G-001 - Cover Sheet",
                    "Architectural",
                    "A-101 - Ground Floor Plan",
                    "A-102 - First Floor Plan",
                    "Details",
                    "A-501 - Wall Sections",
                    "Structural",
                    "S-101 - Foundation Plan",
                },
                Texts(palette)
            );
            Assert.Equal(new[] { 0, 1, 1, 2, 2, 2, 3, 1, 2 }, palette.Rows.Select(r => r.Depth).ToArray());
            Assert.Equal(
                new[]
                {
                    SheetSetNodeKind.SheetSet,
                    SheetSetNodeKind.Sheet,
                    SheetSetNodeKind.Subset,
                    SheetSetNodeKind.Sheet,
                    SheetSetNodeKind.Sheet,
                    SheetSetNodeKind.Subset,
                    SheetSetNodeKind.Sheet,
                    SheetSetNodeKind.Subset,
                    SheetSetNodeKind.Sheet,
                },
                palette.Rows.Select(r => r.Kind).ToArray()
            );
            Assert.Same(palette.Root, palette.Rows[0]);
            Assert.Same(Node(palette, "Details"), Node(palette, "A-501 - Wall Sections").Parent);
        }

        [Fact]
        public void ASheetWithNoNumber_ShowsItsTitleAlone()
        {
            Tower();
            CadSheetSetFile file = CadSheetSetFile.Open(Dst);
            string drawing = Drawing(_folder.FullPath, "extra.dwg", "Plot");
            file.Change(set => set.ImportLayouts(drawing, new[] { "Plot" }, false));
            SheetSetManagerViewModel palette = Palette(out _);
            palette.OpenSet(Dst);

            Assert.Equal("Plot", palette.Rows.Last().Text);
        }

        [Fact]
        public void ACollapsedNode_HidesItsChildren_AndOpensThemAgain()
        {
            SheetSetManagerViewModel palette = Opened(out _);
            SheetSetNodeViewModel architectural = Node(palette, "Architectural");

            architectural.IsExpanded = false;

            Assert.Equal(
                new[]
                {
                    "Example Tower",
                    "G-001 - Cover Sheet",
                    "Architectural",
                    "Structural",
                    "S-101 - Foundation Plan",
                },
                Texts(palette)
            );

            architectural.IsExpanded = true;

            Assert.Equal(9, palette.Rows.Count);
            Assert.Equal("A-501 - Wall Sections", palette.Rows[6].Text);
        }

        [Fact]
        public void ACollapsedNode_LetsGoOfTheSelectionOfWhatItHid()
        {
            SheetSetManagerViewModel palette = Opened(out _);
            Node(palette, "A-101 - Ground Floor Plan").IsSelected = true;

            Node(palette, "Architectural").IsExpanded = false;

            Assert.Empty(palette.SelectedNodes);
        }

        [Fact]
        public void ASheetsRow_SaysWhetherItsDrawingIsThere_OpenInTheSession_OrLeftOutOfPublishing()
        {
            SheetSetManagerViewModel palette = Opened(out MainViewModel model);
            string drawing = SheetFile("A-102 First Floor Plan.dwg");
            File.Delete(SheetFile("A-101 Ground Floor Plan.dwg"));
            Assert.True(model.Open(drawing));
            CadSheetSetFile
                .Open(Dst)
                .Change(set => set.AllSheets().Single(s => s.Number == "S-101").IncludeForPublish = false);

            palette.Refresh();

            SheetSetNodeViewModel missing = Node(palette, "A-101 - Ground Floor Plan");
            Assert.True(missing.IsMissing);
            Assert.Equal(SheetSetTone.Error, missing.Tone);
            SheetSetNodeViewModel open = Node(palette, "A-102 - First Floor Plan");
            Assert.True(open.IsOpen);
            Assert.False(open.IsMissing);
            Assert.Equal(SheetSetTone.Normal, open.Tone);
            SheetSetNodeViewModel left = Node(palette, "S-101 - Foundation Plan");
            Assert.True(left.IsExcluded);
            Assert.Equal(SheetSetTone.Muted, left.Tone);
            Assert.False(Node(palette, "G-001 - Cover Sheet").IsOpen);
        }

        [Fact]
        public void ARefreshOfASetThatHasTheSameParts_KeepsTheRowsAndTheirSelection_AndUpdatesWhatTheySay()
        {
            SheetSetManagerViewModel palette = Opened(out MainViewModel model);
            SheetSetNodeViewModel row = Node(palette, "A-102 - First Floor Plan");
            row.IsSelected = true;
            SheetSetNodeViewModel[] before = palette.Rows.ToArray();
            Assert.True(model.Open(SheetFile("A-102 First Floor Plan.dwg")));

            palette.Refresh();

            Assert.Equal(before, palette.Rows.ToArray());
            Assert.Same(row, Assert.Single(palette.SelectedNodes));
            Assert.True(row.IsOpen);
        }

        [Fact]
        public void ARefreshAfterAnotherProgramChangedTheSet_ShowsTheNewTree()
        {
            SheetSetManagerViewModel palette = Opened(out _);
            CadSheetSetFile other = CadSheetSetFile.Open(Dst);
            other.Change(set => set.AllSheets().Single(s => s.Number == "G-001").Title = "Title Sheet");
            File.SetLastWriteTimeUtc(Dst, DateTime.UtcNow.AddMinutes(1));

            palette.Refresh();

            Assert.Equal("G-001 - Title Sheet", palette.Rows[1].Text);
        }

        // ---- The details pane

        [Fact]
        public void TheDetailsOfASheet_AreItsNumberAndTitle_StatusSubsetFileFolderAndLayout()
        {
            SheetSetManagerViewModel palette = Opened(out _);
            Node(palette, "A-101 - Ground Floor Plan").IsSelected = true;

            Assert.Equal(
                new[] { "Sheet", "Status", "Subset", "File", "Folder", "Layout", "Size", "Last saved" },
                palette.Details.Select(d => d.Label).ToArray()
            );
            Assert.Equal("A-101 - Ground Floor Plan", palette.Details[0].Value);
            Assert.Equal("Available", palette.Details[1].Value);
            Assert.Equal(SheetSetTone.Ok, palette.Details[1].Tone);
            Assert.Equal("Architectural", palette.Details[2].Value);
            Assert.Equal("A-101 Ground Floor Plan.dwg", palette.Details[3].Value);
            Assert.Equal(SetFolder, palette.Details[4].Value);
            Assert.Equal("A-101 Ground Floor Plan", palette.Details[5].Value);
            Assert.EndsWith(" KB", palette.Details[6].Value, StringComparison.Ordinal);
        }

        [Fact]
        public void TheDetailsOfAMissingSheet_SayWhatIsNotFoundAndHowToMendIt()
        {
            SheetSetManagerViewModel palette = Opened(out _);
            File.Delete(SheetFile("A-102 First Floor Plan.dwg"));
            palette.Refresh();
            Node(palette, "A-102 - First Floor Plan").IsSelected = true;

            Assert.Equal(
                new[] { "Sheet", "Status", "Expected", "Layout", "To fix" },
                palette.Details.Select(d => d.Label).ToArray()
            );
            Assert.Equal("Not found: the drawing file does not exist.", palette.Details[1].Value);
            Assert.Equal(SheetSetTone.Error, palette.Details[1].Tone);
            Assert.Equal(SheetFile("A-102 First Floor Plan.dwg"), palette.Details[2].Value);
            Assert.Equal("A-102 First Floor Plan", palette.Details[3].Value);
        }

        [Fact]
        public void TheDetailsOfTheSetAndOfASubset_CountTheSheets()
        {
            SheetSetManagerViewModel palette = Opened(out _);

            palette.Root.IsSelected = true;
            Assert.Equal(
                new[] { "Sheet set", "Status", "Data file", "Sheets", "Description" },
                palette.Details.Select(d => d.Label).ToArray()
            );
            Assert.Equal("Example Tower", palette.Details[0].Value);
            Assert.Equal(Dst, palette.Details[2].Value);
            Assert.Equal("5 in 3 subsets", palette.Details[3].Value);
            Assert.Equal("Six-storey residential tower", palette.Details[4].Value);

            palette.Root.IsSelected = false;
            Node(palette, "Architectural").IsSelected = true;
            Assert.Equal("Architectural", palette.Details[0].Value);
            Assert.Equal("3", palette.Details.Single(d => d.Label == "Sheets").Value);
        }

        [Fact]
        public void ThePreviewOfASheet_IsItsLayoutDrawn_AndNoneForASetOrAMissingSheet()
        {
            SheetSetManagerViewModel palette = Opened(out _);
            palette.ShowPreview = true;

            palette.Root.IsSelected = true;
            Assert.Null(palette.Preview);

            palette.Root.IsSelected = false;
            Node(palette, "A-101 - Ground Floor Plan").IsSelected = true;
            Assert.NotNull(palette.Preview);
            Assert.True(palette.Preview.Width > 0);
        }

        // ---- The locked set

        [Fact]
        public void ASetThatIsReadOnly_OpensLockedWithTheReasonInTheTooltipAndTheStatus()
        {
            Tower();
            File.SetAttributes(Dst, FileAttributes.ReadOnly);

            try
            {
                SheetSetManagerViewModel palette = Palette(out _);
                Assert.True(palette.OpenSet(Dst));

                Assert.True(palette.IsLocked);
                Assert.StartsWith("Read-only:", palette.LockText, StringComparison.Ordinal);
                palette.Root.IsSelected = true;
                Assert.StartsWith("Read-only:", palette.Details[1].Value, StringComparison.Ordinal);
                Assert.Equal(SheetSetTone.Warn, palette.Details[1].Tone);
            }
            finally
            {
                File.SetAttributes(Dst, FileAttributes.Normal);
            }
        }

        [Fact]
        public void ALockedSet_GreysEveryItemThatChangesIt_AndRefusesTheChangeWithTheFileUntouched()
        {
            Tower();
            File.SetAttributes(Dst, FileAttributes.ReadOnly);

            try
            {
                SheetSetManagerViewModel palette = Palette(out MainViewModel model);
                palette.OpenSet(Dst);
                IReadOnlyList<CadMenuItemViewModel> menu = palette.MenuFor(palette.Root);

                Assert.Equal(
                    new[]
                    {
                        "New Sheet…",
                        "New Subset…",
                        "Import Layout as Sheet…",
                        "Resave All Sheets",
                        "Insert Sheet List Table…",
                    },
                    Off(menu)
                );
                Assert.True(Item(menu, "Close Sheet Set").IsEnabled);
                Assert.True(Item(menu, "Properties…").IsEnabled);
                Assert.True(Item(menu, "eTransmit…").IsEnabled);
                int revision = CadSheetSetFile.Open(Dst).FileRevision;

                Assert.False(palette.NewSubset(palette.Root, "Services", null, false));

                Assert.Equal(revision, CadSheetSetFile.Open(Dst).FileRevision);
                Assert.Contains("read-only", model.History.Last(), StringComparison.Ordinal);
                Assert.Equal(
                    new[] { "New Sheet…", "Rename & Renumber…", "Remove Sheet", "Insert Sheet List Table…" },
                    Off(palette.MenuFor(Node(palette, "A-101 - Ground Floor Plan")))
                );
            }
            finally
            {
                File.SetAttributes(Dst, FileAttributes.Normal);
            }
        }

        // ---- The menus

        [Fact]
        public void TheSetMenu_HasTheItemsOfTheDesign_InOrder_AllOnWhereTheSetCanBeChanged()
        {
            SheetSetManagerViewModel palette = Opened(out _);

            IReadOnlyList<CadMenuItemViewModel> menu = palette.MenuFor(palette.Root);

            Assert.Equal(
                new[]
                {
                    "Close Sheet Set",
                    "New Sheet…",
                    "New Subset…",
                    "Import Layout as Sheet…",
                    "Resave All Sheets",
                    "Publish",
                    "eTransmit…",
                    "Insert Sheet List Table…",
                    "Manage Page Setups…",
                    "Plot Stamp Settings…",
                    "Properties…",
                },
                Headers(menu)
            );
            Assert.Equal(new[] { "Insert Sheet List Table…" }, Off(menu));
            Assert.Equal(
                new[]
                {
                    "Publish to PDF",
                    "Publish to Plotter",
                    "Publish Using Page Setup Override",
                    "Publish in Reverse Order",
                    "Sheet Set PDF Publish Options…",
                    "Publish Dialog Box…",
                },
                Headers(Item(menu, "Publish").Items)
            );
            Assert.Equal(5, menu.Count(m => m.IsSeparator));
        }

        [Fact]
        public void PublishUsingPageSetupOverride_IsGreyedUntilTheSetNamesAnOverridesFile()
        {
            SheetSetManagerViewModel palette = Opened(out _);

            CadMenuItemViewModel publish = Item(palette.MenuFor(palette.Root), "Publish");

            Assert.False(Item(publish.Items, "Publish Using Page Setup Override").IsEnabled);
        }

        [Fact]
        public void ASubsetMenu_HasItsItems_RemoveSubsetGreyedWhileItHoldsSheetsOrSubsets()
        {
            SheetSetManagerViewModel palette = Opened(out _);

            IReadOnlyList<CadMenuItemViewModel> menu = palette.MenuFor(Node(palette, "Architectural"));

            Assert.Equal(
                new[]
                {
                    "New Sheet…",
                    "New Subset…",
                    "Import Layout as Sheet…",
                    "Remove Subset",
                    "Rename Subset…",
                    "Publish",
                    "eTransmit…",
                    "Properties…",
                },
                Headers(menu)
            );
            Assert.Equal(new[] { "Remove Subset" }, Off(menu));
            Assert.True(Item(Item(menu, "Publish").Items, "Publish Sheets in Subset").IsChecked);
            Assert.False(Item(Item(menu, "Publish").Items, "Publish in Reverse Order").IsChecked);
            Assert.Equal(
                new[]
                {
                    "Publish to PDF",
                    "Publish to Plotter",
                    "Publish Using Page Setup Override",
                    "Publish Sheets in Subset",
                    "Publish in Reverse Order",
                    "Publish Dialog Box…",
                },
                Headers(Item(menu, "Publish").Items)
            );
            Assert.True(palette.NewSubset(Node(palette, "Structural"), "Empty", null, false));
            Assert.True(Item(palette.MenuFor(Node(palette, "Empty")), "Remove Subset").IsEnabled);
        }

        [Fact]
        public void ASheetMenu_OpensFirstInBold_AndHasTheItemsOfTheDesign()
        {
            SheetSetManagerViewModel palette = Opened(out _);

            IReadOnlyList<CadMenuItemViewModel> menu = palette.MenuFor(Node(palette, "A-101 - Ground Floor Plan"));

            Assert.Equal(
                new[]
                {
                    "Open",
                    "Open Read-Only",
                    "New Sheet…",
                    "Rename & Renumber…",
                    "Remove Sheet",
                    "Publish",
                    "eTransmit…",
                    "Insert Sheet List Table…",
                    "Properties…",
                },
                Headers(menu)
            );
            Assert.True(Item(menu, "Open").IsDefault);
            Assert.Equal(new[] { "Insert Sheet List Table…" }, Off(menu));
            CadMenuItemViewModel include = Item(Item(menu, "Publish").Items, "Include for Publish");
            Assert.True(include.IsCheckable);
            Assert.True(include.IsChecked);
        }

        [Fact]
        public void IncludeForPublish_IsTickedForASheetInTheSet_AndUnticksWhenChosen()
        {
            SheetSetManagerViewModel palette = Opened(out _);
            SheetSetNodeViewModel sheet = Node(palette, "S-101 - Foundation Plan");

            Click(Item(Item(palette.MenuFor(sheet), "Publish").Items, "Include for Publish"));

            Assert.False(
                CadSheetSetFile.Open(Dst).SheetSet.AllSheets().Single(s => s.Number == "S-101").IncludeForPublish
            );
            Assert.True(Node(palette, "S-101 - Foundation Plan").IsExcluded);
            Assert.False(
                Item(
                    Item(palette.MenuFor(Node(palette, "S-101 - Foundation Plan")), "Publish").Items,
                    "Include for Publish"
                ).IsChecked
            );
        }

        [Fact]
        public void ASheetWhoseDrawingIsMissing_CannotBeOpened()
        {
            SheetSetManagerViewModel palette = Opened(out _);
            File.Delete(SheetFile("A-101 Ground Floor Plan.dwg"));
            palette.Refresh();

            IReadOnlyList<CadMenuItemViewModel> menu = palette.MenuFor(Node(palette, "A-101 - Ground Floor Plan"));

            Assert.False(Item(menu, "Open").IsEnabled);
            Assert.False(Item(menu, "Open Read-Only").IsEnabled);
            Assert.True(Item(menu, "Remove Sheet").IsEnabled);
        }

        [Fact]
        public void InsertSheetListTable_IsOnOnlyOnALayoutThatIsASheetOfTheSet()
        {
            SheetSetManagerViewModel palette = Opened(out MainViewModel model);
            Assert.True(model.Open(SheetFile("A-101 Ground Floor Plan.dwg")));
            CadSpace layout = model.Document.Spaces.Single(s => s.Name == "A-101 Ground Floor Plan");

            model.SelectSpace(model.Document.ModelSpace, false);
            Assert.False(Item(palette.MenuFor(palette.Root), "Insert Sheet List Table…").IsEnabled);

            model.SelectSpace(layout, false);
            Assert.True(Item(palette.MenuFor(palette.Root), "Insert Sheet List Table…").IsEnabled);
        }

        [Fact]
        public void TheMenuOfARowOutsideTheSelection_ActsOnThatRow_OneInsideItActsOnAllSelected()
        {
            SheetSetManagerViewModel palette = Opened(out MainViewModel model);
            SheetSetNodeViewModel first = Node(palette, "A-101 - Ground Floor Plan");
            SheetSetNodeViewModel second = Node(palette, "A-102 - First Floor Plan");
            first.IsSelected = true;
            second.IsSelected = true;

            Click(Item(palette.MenuFor(first), "Open"));

            Assert.Equal(2, model.OpenDocuments.Count);
            Assert.True(Node(palette, "A-101 - Ground Floor Plan").IsOpen);
            Assert.True(Node(palette, "A-102 - First Floor Plan").IsOpen);
        }

        [Fact]
        public void TheItemsThatNeedADialog_SayTheWindowShowsItWhereNoneIsSet_AndAskItsDialogWhereOneIsSet()
        {
            SheetSetManagerViewModel palette = Opened(out MainViewModel model);
            SheetSetNodeViewModel sheet = Node(palette, "A-101 - Ground Floor Plan");

            Click(Item(palette.MenuFor(sheet), "Rename & Renumber…"));
            Assert.Contains("is shown by the window", model.History.Last(), StringComparison.Ordinal);

            var asked = new List<(SheetSetDialog, SheetSetNodeViewModel)>();
            palette.DialogRequested += (s, e) =>
            {
                asked.Add((e.Dialog, e.Node));
                e.Handled = true;
            };
            Click(Item(palette.MenuFor(sheet), "Rename & Renumber…"));
            Click(Item(palette.MenuFor(sheet), "Properties…"));
            Click(Item(palette.MenuFor(Node(palette, "Architectural")), "Rename Subset…"));
            Click(Item(palette.MenuFor(Node(palette, "Architectural")), "New Subset…"));
            Click(Item(palette.MenuFor(palette.Root), "Import Layout as Sheet…"));

            Assert.Equal(
                new[]
                {
                    (SheetSetDialog.RenameAndRenumber, sheet),
                    (SheetSetDialog.Properties, sheet),
                    (SheetSetDialog.RenameSubset, Node(palette, "Architectural")),
                    (SheetSetDialog.NewSubset, Node(palette, "Architectural")),
                    (SheetSetDialog.ImportLayouts, palette.Root),
                },
                asked.ToArray()
            );
        }

        // ---- Open

        [Fact]
        public void OpeningASheet_OpensItsDrawing_WithTheSheetsLayoutCurrent()
        {
            SheetSetManagerViewModel palette = Opened(out MainViewModel model);

            Click(Item(palette.MenuFor(Node(palette, "A-102 - First Floor Plan")), "Open"));

            Assert.Equal(
                SheetFile("A-102 First Floor Plan.dwg"),
                model.Document.FileName,
                StringComparer.OrdinalIgnoreCase
            );
            Assert.False(model.Document.CurrentSpace.IsModel);
            Assert.Equal("A-102 First Floor Plan", model.Document.CurrentSpace.Name);
            Assert.True(Node(palette, "A-102 - First Floor Plan").IsOpen);
        }

        [Fact]
        public void OpeningASheetWhoseDrawingIsOpenAlready_ShowsThatDrawingAndItsLayout()
        {
            SheetSetManagerViewModel palette = Opened(out MainViewModel model);
            Assert.True(model.Open(SheetFile("A-102 First Floor Plan.dwg")));
            CadDocument opened = model.Document;
            model.SelectSpace(opened.ModelSpace, false);
            Assert.True(model.Open(SheetFile("G-001 Cover Sheet.dwg")));

            palette.OpenSheets(new[] { Node(palette, "A-102 - First Floor Plan") }, false);

            Assert.Same(opened, model.Document);
            Assert.Equal("A-102 First Floor Plan", model.Document.CurrentSpace.Name);
            Assert.Equal(2, model.OpenDocuments.Count);
        }

        // ---- Drag and drop

        [Fact]
        public void DraggingASheetBeforeAnother_PutsItThereInTheSameSubset()
        {
            SheetSetManagerViewModel palette = Opened(out _);
            SheetSetNodeViewModel dragged = Node(palette, "A-102 - First Floor Plan");
            SheetSetNodeViewModel target = Node(palette, "A-101 - Ground Floor Plan");

            SheetSetDrop plan = palette.PlanDrop(new[] { dragged }, target, SheetSetDropPosition.Before);

            Assert.Same(Node(palette, "Architectural"), plan.Parent);
            Assert.Equal(0, plan.Index);
            Assert.True(palette.Move(new[] { dragged }, target, SheetSetDropPosition.Before));
            Assert.Equal(
                new[]
                {
                    "Example Tower",
                    "G-001 - Cover Sheet",
                    "Architectural",
                    "A-102 - First Floor Plan",
                    "A-101 - Ground Floor Plan",
                    "Details",
                    "A-501 - Wall Sections",
                    "Structural",
                    "S-101 - Foundation Plan",
                },
                Texts(palette)
            );
            Assert.Equal(
                new[] { "A-102", "A-101", null },
                CadSheetSetFile
                    .Open(Dst)
                    .SheetSet.Subsets.Single(s => s.Name == "Architectural")
                    .Children.Select(c => (c as CadSheetSetSheet)?.Number)
                    .ToArray()
            );
        }

        [Fact]
        public void DraggingASheetAfterAnother_CountsTheSheetsThatStayAndNotTheOneThatLeaves()
        {
            SheetSetManagerViewModel palette = Opened(out _);
            SheetSetNodeViewModel dragged = Node(palette, "A-101 - Ground Floor Plan");
            SheetSetNodeViewModel target = Node(palette, "A-102 - First Floor Plan");

            SheetSetDrop plan = palette.PlanDrop(new[] { dragged }, target, SheetSetDropPosition.After);

            Assert.Same(Node(palette, "Architectural"), plan.Parent);
            Assert.Equal(1, plan.Index);
            Assert.True(palette.Move(new[] { dragged }, target, SheetSetDropPosition.After));
            Assert.Equal("A-102 - First Floor Plan", palette.Rows[3].Text);
            Assert.Equal("A-101 - Ground Floor Plan", palette.Rows[4].Text);
        }

        [Fact]
        public void DroppingASheetOnASubset_PutsItAtTheEndOfThatSubset_AndOnTheSetAtTheEndOfTheSet()
        {
            SheetSetManagerViewModel palette = Opened(out _);
            SheetSetNodeViewModel cover = Node(palette, "G-001 - Cover Sheet");

            SheetSetDrop plan = palette.PlanDrop(
                new[] { cover },
                Node(palette, "Structural"),
                SheetSetDropPosition.Into
            );
            Assert.Same(Node(palette, "Structural"), plan.Parent);
            Assert.Equal(1, plan.Index);
            Assert.True(palette.Move(new[] { cover }, Node(palette, "Structural"), SheetSetDropPosition.Into));
            Assert.Equal("G-001 - Cover Sheet", palette.Rows.Last().Text);
            Assert.Equal(2, Node(palette, "G-001 - Cover Sheet").Depth);

            SheetSetNodeViewModel moved = Node(palette, "G-001 - Cover Sheet");
            Assert.True(palette.Move(new[] { moved }, palette.Root, SheetSetDropPosition.Into));
            Assert.Equal("G-001 - Cover Sheet", palette.Rows.Last().Text);
            Assert.Equal(1, Node(palette, "G-001 - Cover Sheet").Depth);
        }

        [Fact]
        public void DraggingSeveralSheets_KeepsThemInTheOrderTheyHadInTheTree()
        {
            SheetSetManagerViewModel palette = Opened(out _);
            SheetSetNodeViewModel[] dragged =
            {
                Node(palette, "A-102 - First Floor Plan"),
                Node(palette, "A-101 - Ground Floor Plan"),
            };

            Assert.True(palette.Move(dragged, Node(palette, "Structural"), SheetSetDropPosition.Into));

            Assert.Equal(
                new[] { "S-101 - Foundation Plan", "A-101 - Ground Floor Plan", "A-102 - First Floor Plan" },
                palette.Rows.Skip(palette.Rows.IndexOf(Node(palette, "Structural")) + 1).Select(r => r.Text).ToArray()
            );
        }

        [Fact]
        public void ASubsetCannotBeDroppedIntoItselfOrWhatItHolds_NorTheSetMoved_NorASheetOnASheet()
        {
            SheetSetManagerViewModel palette = Opened(out _);
            SheetSetNodeViewModel architectural = Node(palette, "Architectural");

            Assert.Null(palette.PlanDrop(new[] { architectural }, architectural, SheetSetDropPosition.Into));
            Assert.Null(palette.PlanDrop(new[] { architectural }, Node(palette, "Details"), SheetSetDropPosition.Into));
            Assert.Null(
                palette.PlanDrop(
                    new[] { architectural },
                    Node(palette, "A-501 - Wall Sections"),
                    SheetSetDropPosition.After
                )
            );
            Assert.Null(palette.PlanDrop(new[] { palette.Root }, architectural, SheetSetDropPosition.Into));
            Assert.Null(
                palette.PlanDrop(
                    new[] { Node(palette, "S-101 - Foundation Plan") },
                    Node(palette, "G-001 - Cover Sheet"),
                    SheetSetDropPosition.Into
                )
            );
            Assert.Null(
                palette.PlanDrop(
                    new[] { Node(palette, "S-101 - Foundation Plan") },
                    palette.Root,
                    SheetSetDropPosition.Before
                )
            );
            Assert.False(palette.Move(new[] { architectural }, Node(palette, "Details"), SheetSetDropPosition.Into));
            Assert.Equal(9, palette.Rows.Count);
        }

        [Fact]
        public void ASubsetDroppedOnAnother_BringsItsSheetsWithIt()
        {
            SheetSetManagerViewModel palette = Opened(out _);

            Assert.True(
                palette.Move(new[] { Node(palette, "Details") }, Node(palette, "Structural"), SheetSetDropPosition.Into)
            );

            Assert.Equal(
                new[] { "Structural", "S-101 - Foundation Plan", "Details", "A-501 - Wall Sections" },
                Texts(palette).Skip(Texts(palette).Length - 4).ToArray()
            );
        }

        [Fact]
        public void ALockedSet_DoesNotAllowADrop()
        {
            Tower();
            File.SetAttributes(Dst, FileAttributes.ReadOnly);

            try
            {
                SheetSetManagerViewModel palette = Palette(out _);
                palette.OpenSet(Dst);

                Assert.Null(
                    palette.PlanDrop(
                        new[] { Node(palette, "A-102 - First Floor Plan") },
                        Node(palette, "A-101 - Ground Floor Plan"),
                        SheetSetDropPosition.Before
                    )
                );
            }
            finally
            {
                File.SetAttributes(Dst, FileAttributes.Normal);
            }
        }

        // ---- The operations the menus run

        [Fact]
        public void NewSubset_GoesAtTheEndOfTheSubsetPickedOrAfterTheSheetPicked()
        {
            SheetSetManagerViewModel palette = Opened(out _);

            Assert.True(palette.NewSubset(Node(palette, "Structural"), "Connections", "Bolted", false));
            Assert.True(palette.NewSubset(Node(palette, "A-101 - Ground Floor Plan"), "Plans", null, false));

            Assert.Equal(
                new[]
                {
                    "Architectural",
                    "A-101 - Ground Floor Plan",
                    "Plans",
                    "A-102 - First Floor Plan",
                    "Details",
                    "A-501 - Wall Sections",
                    "Structural",
                    "S-101 - Foundation Plan",
                    "Connections",
                },
                Texts(palette).Skip(2).ToArray()
            );
        }

        [Fact]
        public void ANameThatIsEmptyOrASiblingsIsRefused_WithTheCoreMessageOnTheCommandLine()
        {
            SheetSetManagerViewModel palette = Opened(out MainViewModel model);
            Assert.True(model.Open(SheetFile("G-001 Cover Sheet.dwg")));

            Assert.False(palette.NewSubset(palette.Root, "architectural", null, false));

            Assert.Contains("architectural", model.History.Last(), StringComparison.OrdinalIgnoreCase);
            Assert.Equal(9, palette.Rows.Count);
        }

        [Fact]
        public void NewSheet_MakesTheDrawingAndPutsTheSheetAfterTheOnePicked_OrAtTheEndOfTheSubset()
        {
            SheetSetManagerViewModel palette = Opened(out _);

            Assert.True(palette.NewSheet(Node(palette, "A-101 - Ground Floor Plan"), "A-103", "Roof Plan"));
            Assert.True(palette.NewSheet(Node(palette, "Structural"), "S-102", "Framing"));

            Assert.True(File.Exists(SheetFile("A-103 Roof Plan.dwg")));
            Assert.Equal(new[] { "A-103 Roof Plan" }, LayoutsOf(SheetFile("A-103 Roof Plan.dwg")));
            Assert.Equal(
                new[] { "A-101 - Ground Floor Plan", "A-103 - Roof Plan", "A-102 - First Floor Plan" },
                Texts(palette).Skip(3).Take(3).ToArray()
            );
            Assert.Equal("S-102 - Framing", palette.Rows.Last().Text);
        }

        [Fact]
        public void NewSheet_OpenInTheDrawingEditor_OpensTheNewDrawingOnItsLayout()
        {
            SheetSetManagerViewModel palette = Opened(out MainViewModel model);

            Assert.True(palette.NewSheet(Node(palette, "Structural"), "S-102", "Framing", null, null, true));

            Assert.Equal(SheetFile("S-102 Framing.dwg"), model.Document.FileName, StringComparer.OrdinalIgnoreCase);
            Assert.Equal("S-102 Framing", model.Document.CurrentSpace.Name);
        }

        [Fact]
        public void NewSheet_WithAnExistingFile_IsRefused_NothingChanges()
        {
            SheetSetManagerViewModel palette = Opened(out MainViewModel model);
            Assert.True(model.Open(SheetFile("G-001 Cover Sheet.dwg")));

            Assert.False(palette.NewSheet(palette.Root, "A-101", "Ground Floor Plan"));

            Assert.Contains("is there already", model.History.Last(), StringComparison.Ordinal);
            Assert.Equal(9, palette.Rows.Count);
        }

        [Fact]
        public void ImportLayouts_AddsASheetForEachAtTheEndOfTheSubset_AndMarksTheDrawing()
        {
            SheetSetManagerViewModel palette = Opened(out _);
            string drawing = Drawing(_folder.FullPath, "services.dwg", "Water", "Gas");

            Assert.True(palette.ImportLayouts(Node(palette, "Structural"), drawing, new[] { "Gas", "Water" }, true));

            Assert.Equal(
                new[] { "S-101 - Foundation Plan", "services - Gas", "services - Water" },
                Texts(palette).Skip(Texts(palette).Length - 3).ToArray()
            );
            Assert.NotNull(CadSheetSetHint.Read(drawing));
        }

        [Fact]
        public void ImportLayouts_OfALayoutAlreadyASheet_IsRefusedWithTheCoreMessage()
        {
            SheetSetManagerViewModel palette = Opened(out MainViewModel model);
            Assert.True(model.Open(SheetFile("G-001 Cover Sheet.dwg")));

            Assert.False(
                palette.ImportLayouts(
                    palette.Root,
                    SheetFile("A-101 Ground Floor Plan.dwg"),
                    new[] { "A-101 Ground Floor Plan" },
                    false
                )
            );

            Assert.Contains("A-101 Ground Floor Plan", model.History.Last(), StringComparison.Ordinal);
            Assert.Equal(9, palette.Rows.Count);
        }

        [Fact]
        public void RenumberingASheet_OnlyWritesTheSet_TheDrawingOpenIsLeftAlone()
        {
            SheetSetManagerViewModel palette = Opened(out MainViewModel model);
            Assert.True(model.Open(SheetFile("A-101 Ground Floor Plan.dwg")));
            CadDocument document = model.Document;

            Assert.True(
                palette.RenameAndRenumber(
                    Node(palette, "A-101 - Ground Floor Plan"),
                    "A-100",
                    "Ground Floor Plan",
                    null,
                    null
                )
            );

            Assert.Equal("A-100 - Ground Floor Plan", palette.Rows[3].Text);
            Assert.Same(document, model.Document);
            Assert.True(File.Exists(SheetFile("A-101 Ground Floor Plan.dwg")));
        }

        [Fact]
        public void RenamingTheFileOfASheetOpenInTheSession_SavesItClosesItRenamesItAndOpensItAgain()
        {
            SheetSetManagerViewModel palette = Opened(out MainViewModel model);
            string old = SheetFile("A-101 Ground Floor Plan.dwg");
            Assert.True(model.Open(old));
            model.Execute("CIRCLE");
            model.Execute("100,100");
            model.Execute("25");
            Assert.True(model.Document.IsModified);
            int circles = model.Document.ModelSpace.Block.OfType<DxfCircle>().Count();

            Assert.True(
                palette.RenameAndRenumber(
                    Node(palette, "A-101 - Ground Floor Plan"),
                    "A-110",
                    "Ground Plan",
                    "A-110 Ground Plan",
                    "A-110 Ground Plan"
                )
            );

            string renamed = SheetFile("A-110 Ground Plan.dwg");
            Assert.False(File.Exists(old));
            Assert.True(File.Exists(renamed));
            Assert.Single(model.OpenDocuments);
            Assert.Equal(renamed, model.Document.FileName, StringComparer.OrdinalIgnoreCase);
            Assert.False(model.Document.IsModified);
            Assert.Equal(circles, model.Document.ModelSpace.Block.OfType<DxfCircle>().Count());
            Assert.Equal("A-110 Ground Plan", model.Document.CurrentSpace.Name);
            Assert.Equal("A-110 - Ground Plan", palette.Rows[3].Text);
            Assert.True(palette.Rows[3].IsOpen);
        }

        [Fact]
        public void ARenameThatTheCoreRefuses_LeavesTheDrawingOpenAsItWas_AndSaysWhy()
        {
            SheetSetManagerViewModel palette = Opened(out MainViewModel model);
            Assert.True(model.Open(SheetFile("A-101 Ground Floor Plan.dwg")));

            Assert.False(
                palette.RenameAndRenumber(
                    Node(palette, "A-101 - Ground Floor Plan"),
                    "A-101",
                    "Ground Floor Plan",
                    null,
                    "A-102 First Floor Plan"
                )
            );

            Assert.Contains(model.History, l => l.Contains("is there already"));
            Assert.Equal(
                SheetFile("A-101 Ground Floor Plan.dwg"),
                model.Document.FileName,
                StringComparer.OrdinalIgnoreCase
            );
            Assert.True(File.Exists(SheetFile("A-101 Ground Floor Plan.dwg")));
        }

        [Fact]
        public void RemovingASheet_AsksFirst_AndKeepsItsDrawing()
        {
            SheetSetManagerViewModel palette = Opened(out _);
            var asked = new List<string>();
            palette.ConfirmRemove = text =>
            {
                asked.Add(text);
                return asked.Count > 1;
            };
            SheetSetNodeViewModel sheet = Node(palette, "A-101 - Ground Floor Plan");

            Assert.False(palette.Remove(new[] { sheet }));
            Assert.Equal(9, palette.Rows.Count);
            Assert.True(palette.Remove(new[] { sheet }));

            Assert.Equal(8, palette.Rows.Count);
            Assert.DoesNotContain("A-101 - Ground Floor Plan", Texts(palette));
            Assert.True(File.Exists(SheetFile("A-101 Ground Floor Plan.dwg")));
            Assert.Contains("A-101 - Ground Floor Plan", asked[0], StringComparison.Ordinal);
            Assert.Contains("not deleted", asked[0], StringComparison.Ordinal);
        }

        [Fact]
        public void RemoveSheet_OfSeveralSelected_RemovesThemAllAfterOneQuestion()
        {
            SheetSetManagerViewModel palette = Opened(out _);
            int asked = 0;
            palette.ConfirmRemove = text =>
            {
                asked++;
                return true;
            };
            Node(palette, "A-101 - Ground Floor Plan").IsSelected = true;
            Node(palette, "A-102 - First Floor Plan").IsSelected = true;

            Click(Item(palette.MenuFor(Node(palette, "A-102 - First Floor Plan")), "Remove Sheet"));

            Assert.Equal(1, asked);
            Assert.Equal(7, palette.Rows.Count);
        }

        [Fact]
        public void ASubsetThatHoldsSomethingIsNotRemoved_AnEmptyOneIs()
        {
            SheetSetManagerViewModel palette = Opened(out _);
            palette.NewSubset(palette.Root, "Empty", null, false);

            Assert.False(palette.Remove(new[] { Node(palette, "Architectural") }));
            Assert.True(palette.Remove(new[] { Node(palette, "Empty") }));

            Assert.Equal(9, palette.Rows.Count);
        }

        [Fact]
        public void ResaveAllSheets_ClosesAndOpensAgainTheSheetsOpenInTheSession()
        {
            SheetSetManagerViewModel palette = Opened(out MainViewModel model);
            Assert.True(model.Open(SheetFile("A-102 First Floor Plan.dwg")));

            Click(Item(palette.MenuFor(palette.Root), "Resave All Sheets"));

            Assert.Single(model.OpenDocuments);
            Assert.Equal(
                SheetFile("A-102 First Floor Plan.dwg"),
                model.Document.FileName,
                StringComparer.OrdinalIgnoreCase
            );
            Assert.True(CadSheetSetHint.Read(SheetFile("A-102 First Floor Plan.dwg")).UpdateCount >= 2);
        }

        // ---- The sheet set control

        [Fact]
        public void TheSetControl_ListsTheOpenSetsWithTheCurrentTicked_ThenRecent_NewAndOpen()
        {
            Tower();
            string other = Path.Combine(_folder.FullPath, "clinic", "Riverside Clinic.dst");
            Directory.CreateDirectory(Path.GetDirectoryName(other));
            CadSheetSetFile.Create(other, "Riverside Clinic");
            SheetSetManagerViewModel palette = Palette(out MainViewModel model);
            palette.OpenSet(Dst);
            palette.OpenSet(other);

            IReadOnlyList<CadMenuItemViewModel> menu = palette.ControlMenu();

            Assert.Equal(
                new[] { "Example Tower", "Riverside Clinic", "Recent", "New Sheet Set…", "Open…" },
                Headers(menu)
            );
            Assert.False(Item(menu, "Example Tower").IsChecked);
            Assert.True(Item(menu, "Riverside Clinic").IsChecked);
            Assert.True(Item(menu, "Riverside Clinic").IsCheckable);
            Assert.Equal("Riverside Clinic", palette.ControlText);

            Click(Item(menu, "Example Tower"));

            Assert.Equal("Example Tower", palette.ControlText);
            Assert.Equal(9, palette.Rows.Count);
        }

        [Fact]
        public void Recent_HoldsTheLastTenNewestFirst_ShortenedInTheMiddle_AMissingFileGreyed()
        {
            Tower();
            SheetSetManagerViewModel palette = Palette(out MainViewModel model);
            palette.OpenSet(Dst);
            model.NoteRecentSheetSet(@"\\server\share\offices\Harbour Offices.dst");

            CadMenuItemViewModel recent = Item(palette.ControlMenu(), "Recent");

            Assert.Equal(
                new[] { @"\\...\Harbour Offices.dst", Path.GetPathRoot(Dst) + @"...\Example Tower.dst" },
                recent.Items.Select(i => i.Header)
            );
            Assert.Equal(@"\\server\share\offices\Harbour Offices.dst", recent.Items[0].ToolTip);
            Assert.False(recent.Items[0].IsEnabled);
            Assert.Equal(Dst, recent.Items[1].ToolTip);
            Assert.True(recent.Items[1].IsEnabled);
        }

        [Fact]
        public void ShortPath_KeepsTheRootAndTheFileName()
        {
            Assert.Equal(
                @"D:\...\Example Tower.dst",
                SheetSetManagerViewModel.ShortPath(@"D:\Projects\Example Tower\Example Tower.dst")
            );
            Assert.Equal(
                @"\\...\Harbour Offices.dst",
                SheetSetManagerViewModel.ShortPath(@"\\server\share\x\Harbour Offices.dst")
            );
            Assert.Equal(@"D:\Tower.dst", SheetSetManagerViewModel.ShortPath(@"D:\Tower.dst"));
        }

        [Fact]
        public void OpeningTheSameSetTwice_ShowsItOnce_AndMakesItCurrent()
        {
            Tower();
            string other = Path.Combine(_folder.FullPath, "Riverside Clinic.dst");
            CadSheetSetFile.Create(other, "Riverside Clinic");
            SheetSetManagerViewModel palette = Palette(out _);
            palette.OpenSet(Dst);
            palette.OpenSet(other);

            Assert.True(palette.OpenSet(Dst.ToUpperInvariant()));

            Assert.Equal("Example Tower", palette.ControlText);
            Assert.Equal(2, Headers(palette.ControlMenu()).Count(h => h == "Example Tower" || h == "Riverside Clinic"));
        }

        [Fact]
        public void CloseSheetSet_DropsTheCurrentOne_TheNextBecomesCurrent_NoneLeftReadsOpen()
        {
            Tower();
            string other = Path.Combine(_folder.FullPath, "Riverside Clinic.dst");
            CadSheetSetFile.Create(other, "Riverside Clinic");
            SheetSetManagerViewModel palette = Palette(out _);
            palette.OpenSet(Dst);
            palette.OpenSet(other);

            Click(Item(palette.MenuFor(palette.Root), "Close Sheet Set"));
            Assert.Equal("Example Tower", palette.ControlText);

            Click(Item(palette.MenuFor(palette.Root), "Close Sheet Set"));
            Assert.False(palette.HasSet);
            Assert.Equal("Open…", palette.ControlText);
            Assert.Empty(palette.Rows);
        }

        [Fact]
        public void AFileThatIsNoSheetSet_IsNotOpened_TheCommandLineSaysWhy()
        {
            SheetSetManagerViewModel palette = Palette(out MainViewModel model);
            string bad = _folder.WriteText("bad.dst", "this is not a sheet set");

            Assert.False(palette.OpenSet(bad));
            Assert.False(palette.OpenSet(Path.Combine(_folder.FullPath, "none.dst")));

            Assert.False(palette.HasSet);
            Assert.Contains("none.dst", model.History.Last(), StringComparison.Ordinal);
            Assert.Empty(model.Settings.RecentSheetSets);
        }

        [Fact]
        public void TheOpenItem_AsksTheFileDialog_AndOpensWhatItGives()
        {
            Tower();
            SheetSetManagerViewModel palette = Palette(out _);
            palette.AskOpenFile = () => Dst;

            Click(Item(palette.ControlMenu(), "Open…"));

            Assert.Equal("Example Tower", palette.ControlText);
        }

        [Fact]
        public void TheNewSheetSetItem_StartsNEWSHEETSET()
        {
            SheetSetManagerViewModel palette = Palette(out MainViewModel model);
            Assert.True(model.Open(_folder.Write("plan.dxf", CadTemplate.CreateMetric())));

            Click(Item(palette.ControlMenu(), "New Sheet Set…"));

            Assert.Contains(
                "NEWSHEETSET",
                model.History.Last(l => l.StartsWith("Command:", StringComparison.Ordinal)),
                StringComparison.Ordinal
            );
        }

        // ---- The commands and the settings

        [Fact]
        public void SheetsetAndSheetsethide_ShowAndCloseThePalette_ThroughTheWindow()
        {
            var model = new MainViewModel();
            Assert.True(model.Open(_folder.Write("plan.dxf", CadTemplate.CreateMetric())));

            model.Execute("SHEETSET");
            Assert.Equal(
                "The Sheet Set Manager is a palette of the window; there is none to show.",
                model.History.Last()
            );
            model.Execute("SHEETSETHIDE");
            Assert.Equal(
                "The Sheet Set Manager is a palette of the window; there is none to close.",
                model.History.Last()
            );

            SheetSetManagerViewModel shown = null;
            bool closed = false;
            model.ShowSheetSetManager = palette => shown = palette;
            model.CloseSheetSetManager = () => closed = true;
            model.Execute("SHEETSET");
            model.Execute("SHEETSETHIDE");

            Assert.Same(model.SheetSetManager, shown);
            Assert.True(closed);
            Assert.False(model.IsSheetSetShown);
            model.NoteSheetSetShown(true);
            Assert.True(model.IsSheetSetShown);
        }

        [Fact]
        public void TheRecentSheetSets_AreKeptTenAtMost_NewestFirst_EachOnce_AcrossTheSettingsFile()
        {
            var settings = new GlobalSettings();

            for (int i = 0; i < 12; i++)
            {
                settings.NoteRecentSheetSet(@"D:\Sets\Set" + i + ".dst");
            }

            settings.NoteRecentSheetSet(@"d:\sets\SET5.dst");
            string file = Path.Combine(_folder.FullPath, "settings.xml");
            settings.Save(file);

            GlobalSettings again = GlobalSettings.Load(file);

            Assert.Equal(10, again.RecentSheetSets.Count);
            Assert.Equal(@"d:\sets\SET5.dst", again.RecentSheetSets[0]);
            Assert.Equal(@"D:\Sets\Set11.dst", again.RecentSheetSets[1]);
            Assert.Equal(@"D:\Sets\Set2.dst", again.RecentSheetSets[9]);
            Assert.Single(again.RecentSheetSets, p => p.EndsWith("set5.dst", StringComparison.OrdinalIgnoreCase));
        }
    }
}
