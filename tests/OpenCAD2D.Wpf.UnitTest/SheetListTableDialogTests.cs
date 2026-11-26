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
    /// Insert Sheet List Table (A12 4e), as the board A12SheetListTable draws it: the dialog's values read from a sheet set made in
    /// code (table style, title, the columns with their data type and heading, add, remove and reorder, subsets as headers, the
    /// subsets and sheets checked), the table made in the current sheet layout where the insertion point is given, rows in set
    /// order with the subsets as headers, Update Sheet List Table after a sheet was added, Edit Sheet List Table Settings on the
    /// settings the table keeps, and the palette's item on only on a layout that is a sheet of the set. No dialog is shown: the
    /// test takes the place of the call that shows it.
    /// </summary>
    public sealed class SheetListTableDialogTests : IDisposable
    {
        private readonly TestFolder _folder = new TestFolder();

        public void Dispose() => _folder.Dispose();

        private string SetFolder => Path.Combine(_folder.FullPath, "tower");

        private string Dst => Path.Combine(SetFolder, "Example Tower.dst");

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

            database.ModelSpace.AppendEntity(new DxfCircle(new GeoPoint3(0, 0, 0), GeoVector3.ZAxis, 5));
            string path = Path.Combine(folder, file);
            CadDocument.From(database, file).SaveAs(path);
            return path;
        }

        /// <summary>Example Tower: G-001 in the set, Architectural with A-101, A-102 and Details with A-501, then Structural with S-101; a sheet custom property Drawn By and a set one, Client.</summary>
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
                set.PromptForTemplate = false;
                set.AddCustomProperty("Client", "Northbank", CadSheetSetPropertyFlags.Set);
                set.AddCustomProperty("Drawn By", "T. Tran", CadSheetSetPropertyFlags.Sheet);
                set.NewSheet("G-001", "Cover Sheet");
                CadSheetSubset architectural = set.NewSubset("Architectural", null, false);
                architectural.NewSheet("A-101", "Ground Floor Plan");
                architectural.NewSheet("A-102", "First Floor Plan");
                architectural.NewSubset("Details", null, false).NewSheet("A-501", "Wall Sections");
                set.NewSubset("Structural", null, false).NewSheet("S-101", "Foundation Plan");
            });
            return file;
        }

        /// <summary>The palette over the set, and a drawing of one of its sheets open with that sheet's layout current.</summary>
        private SheetSetManagerViewModel OnASheet(out MainViewModel model)
        {
            Tower();
            model = new MainViewModel();
            var palette = new SheetSetManagerViewModel(model);
            Assert.True(palette.OpenSet(Dst));
            Assert.True(model.Open(Path.Combine(SetFolder, "A-101 Ground Floor Plan.dwg")));
            model.SelectSpace(model.Document.Spaces.Single(s => s.Name == "A-101 Ground Floor Plan"), false);
            return palette;
        }

        private static CadMenuItemViewModel Item(IEnumerable<CadMenuItemViewModel> items, string header) =>
            items.Single(i => i.Header == header);

        private CadDocument Dummy()
        {
            CadDocument document = CadDocument.From(new DxfDatabase("AC1032"), Path.Combine(_folder.FullPath, "d.dwg"));
            Assert.True(document.ShowSpace(document.Spaces.Single(s => s.Name == "Layout1")).Succeeded);
            return document;
        }

        private static string[] TreeTexts(SheetListTableViewModel dialog) => dialog.Nodes.Select(n => n.Text).ToArray();

        private static SheetListCheckNode Node(SheetListTableViewModel dialog, string text) =>
            dialog.Nodes.Single(n => n.Text == text);

        private static DxfTable TableIn(CadDocument document) =>
            document.CurrentSpace.Block.OfType<DxfTable>().Single();

        private static string[] Rows(DxfTable table, int column) =>
            Enumerable.Range(2, table.NumRows - 2).Select(r => table.TextString(r, column)).ToArray();

        // ---- The dialog

        [Fact]
        public void TheDialog_StartsWithTheEnginesDefaults_TheStylesOfTheDrawing_TheSetsCustomPropertiesAndEveryPartChecked()
        {
            CadSheetSetFile file = Tower();
            var dialog = new SheetListTableViewModel(file, Dummy());

            Assert.Equal("Insert Sheet List Table", dialog.Title);
            Assert.False(dialog.IsEditing);
            Assert.Contains("Standard", dialog.TableStyles);
            Assert.Equal("Standard", dialog.TableStyle);
            Assert.True(dialog.CanChooseStyle);
            Assert.Equal("Sheet List Table", dialog.TitleText);
            Assert.False(dialog.IncludeSubsetHeaders);
            Assert.Equal(new[] { "Sheet Number", "Sheet Title" }, dialog.Columns.Select(c => c.Heading).ToArray());
            Assert.Equal(
                new[]
                {
                    "Sheet Number",
                    "Sheet Title",
                    "Sheet Description",
                    "Subset name",
                    "Revision number",
                    "Revision date",
                    "Issue purpose",
                    "Category",
                    "Drawing file name",
                    "Layout name",
                    "Drawn By",
                },
                dialog.DataTypes.Select(d => d.Name).ToArray()
            );
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
                TreeTexts(dialog)
            );
            Assert.All(dialog.Nodes, n => Assert.True(n.IsChecked));
            Assert.Null(dialog.Settings.CheckedIds);
            Assert.True(dialog.CanApply);
            Assert.False(dialog.CanPopulate);
            Assert.Equal("(none)", dialog.PopulateFromSheetSelection);
        }

        [Fact]
        public void TheColumns_CanBeAdded_Removed_AndMovedUpAndDown_AndKeepTheirOrderInTheSettings()
        {
            var dialog = new SheetListTableViewModel(Tower(), Dummy());
            Assert.True(dialog.CanRemoveColumn);

            dialog.AddColumn();

            Assert.Equal(3, dialog.Columns.Count);
            Assert.Same(dialog.Columns[2], dialog.SelectedColumn);
            Assert.Equal(CadSheetListData.SheetNumber, dialog.Columns[2].DataType.Data);
            dialog.Columns[2].DataType = dialog.DataTypes.Single(d => d.Name == "Drawn By");
            Assert.Equal("Drawn By", dialog.Columns[2].Heading);
            Assert.True(dialog.CanMoveUp);
            Assert.False(dialog.CanMoveDown);

            dialog.MoveColumnUp();
            dialog.MoveColumnUp();

            Assert.Equal(new[] { "Drawn By", "Sheet Number", "Sheet Title" }, dialog.Columns.Select(c => c.Heading));
            Assert.False(dialog.CanMoveUp);
            Assert.Same(dialog.Columns[0], dialog.SelectedColumn);
            CadSheetListTableSettings settings = dialog.Settings;
            Assert.Equal(
                new[] { CadSheetListData.CustomProperty, CadSheetListData.SheetNumber, CadSheetListData.SheetTitle },
                settings.Columns.Select(c => c.Data)
            );
            Assert.Equal("Drawn By", settings.Columns[0].CustomName);

            dialog.SelectedColumn = dialog.Columns[1];
            dialog.MoveColumnDown();
            dialog.RemoveColumn();

            Assert.Equal(new[] { "Drawn By", "Sheet Title" }, dialog.Columns.Select(c => c.Heading));
            Assert.Same(dialog.Columns[1], dialog.SelectedColumn);
        }

        [Fact]
        public void AHeading_FollowsTheDataTypeUntilItIsTyped_ThenStaysAsTyped()
        {
            var dialog = new SheetListTableViewModel(Tower(), Dummy());
            SheetListColumnRow row = dialog.Columns[1];

            row.DataType = dialog.DataTypes.Single(d => d.Name == "Revision number");
            Assert.Equal("Revision Number", row.Heading);

            row.Heading = "Rev.";
            row.DataType = dialog.DataTypes.Single(d => d.Name == "Category");

            Assert.Equal("Rev.", row.Heading);
            Assert.Equal("Rev.", dialog.Settings.Columns[1].Heading);
            Assert.Equal(CadSheetListData.Category, dialog.Settings.Columns[1].Data);
        }

        [Fact]
        public void TheSubsetsAndSheetsTab_ChecksAndUnchecksAsATree_AndSaysWhichPartsAreListed()
        {
            var dialog = new SheetListTableViewModel(Tower(), Dummy());

            Node(dialog, "A-102 - First Floor Plan").IsChecked = false;

            Assert.Null(Node(dialog, "Architectural").IsChecked);
            Assert.Null(Node(dialog, "Example Tower").IsChecked);
            Assert.True(Node(dialog, "Details").IsChecked);
            Assert.True(Node(dialog, "Structural").IsChecked);
            CadSheetSetFile file = dialog.File;
            string Id(string number) => file.SheetSet.AllSheets().Single(s => s.Number == number).Id;
            string[] ids = dialog.Settings.CheckedIds.ToArray();
            Assert.Equal(
                new[]
                {
                    Id("G-001"),
                    Id("A-101"),
                    file.SheetSet.Subsets.Single(s => s.Name == "Architectural").Subsets.Single().Id,
                    file.SheetSet.Subsets.Single(s => s.Name == "Structural").Id,
                },
                ids
            );

            Node(dialog, "Architectural").IsChecked = false;
            Assert.False(Node(dialog, "A-501 - Wall Sections").IsChecked);
            Assert.False(Node(dialog, "Details").IsChecked);
            Assert.Equal(
                new[] { Id("G-001"), file.SheetSet.Subsets.Single(s => s.Name == "Structural").Id },
                dialog.Settings.CheckedIds
            );

            Node(dialog, "Architectural").IsChecked = true;
            Assert.True(Node(dialog, "A-102 - First Floor Plan").IsChecked);
            Node(dialog, "Example Tower").IsChecked = true;
            Assert.Null(dialog.Settings.CheckedIds);
        }

        [Fact]
        public void AnEditDialog_ShowsTheSettingsTheTableKeeps_TheStyleFixed_AndGivesThemBack()
        {
            CadSheetSetFile file = Tower();
            CadSheetSubset structural = file.SheetSet.Subsets.Single(s => s.Name == "Structural");
            var kept = new CadSheetListTableSettings
            {
                Title = "Sheet List",
                IncludeSubsetHeaders = true,
                CheckedIds = new[] { structural.Id, file.SheetSet.AllSheets().Single(s => s.Number == "G-001").Id },
            };
            kept.Columns.Clear();
            kept.Columns.Add(CadSheetListColumn.Of(CadSheetListData.SheetNumber, "Sheet No."));
            kept.Columns.Add(CadSheetListColumn.Custom("Drawn By", "Drawn"));

            var dialog = new SheetListTableViewModel(file, Dummy(), kept, "Standard");

            Assert.Equal("Edit Sheet List Table Settings", dialog.Title);
            Assert.True(dialog.IsEditing);
            Assert.False(dialog.CanChooseStyle);
            Assert.Equal("Sheet List", dialog.TitleText);
            Assert.True(dialog.IncludeSubsetHeaders);
            Assert.Equal(new[] { "Sheet No.", "Drawn" }, dialog.Columns.Select(c => c.Heading));
            Assert.Equal("Drawn By", dialog.Columns[1].DataType.Name);
            Assert.True(Node(dialog, "G-001 - Cover Sheet").IsChecked);
            Assert.True(Node(dialog, "S-101 - Foundation Plan").IsChecked);
            Assert.False(Node(dialog, "A-101 - Ground Floor Plan").IsChecked);
            Assert.Null(Node(dialog, "Example Tower").IsChecked);

            CadSheetListTableSettings back = dialog.Settings;
            Assert.Equal("Sheet List", back.Title);
            Assert.True(back.IncludeSubsetHeaders);
            Assert.Equal(kept.CheckedIds.OrderBy(i => i), back.CheckedIds.OrderBy(i => i));
            Assert.Equal(new[] { "Sheet No.", "Drawn" }, back.Columns.Select(c => c.Heading));
            Assert.Equal("Drawn By", back.Columns[1].CustomName);
        }

        [Fact]
        public void ATableWithNoColumn_OrNoSheetChecked_IsRefusedInTheDialogsWords_WithOkGreyed()
        {
            var dialog = new SheetListTableViewModel(Tower(), Dummy());

            dialog.SelectedColumn = dialog.Columns[0];
            dialog.RemoveColumn();
            dialog.RemoveColumn();

            Assert.Equal("The table needs a column.", dialog.Error);
            Assert.False(dialog.CanApply);
            dialog.AddColumn();
            Assert.Null(dialog.Error);

            Node(dialog, "Example Tower").IsChecked = false;

            Assert.Equal("No sheet is checked, so the table would list nothing.", dialog.Error);
            Assert.False(dialog.CanApply);
        }

        // ---- The table in the sheet

        [Fact]
        public void TheMenuItem_AsksForTheDialog_ThenForTheInsertionPoint_AndMakesTheTableInSetOrderWithSubsetsAsHeaders()
        {
            SheetSetManagerViewModel palette = OnASheet(out MainViewModel model);
            var shown = new List<SheetListTableViewModel>();
            model.ShowSheetListTable = dialog =>
            {
                shown.Add(dialog);
                dialog.TitleText = "Sheet List";
                dialog.IncludeSubsetHeaders = true;
                dialog.Columns[0].Heading = "Sheet No.";
                dialog.AddColumn();
                dialog.Columns[2].DataType = dialog.DataTypes.Single(d => d.Name == "Drawn By");
                dialog.Columns[2].Heading = "Drawn";
                return dialog.Apply();
            };

            Item(palette.MenuFor(palette.Root), "Insert Sheet List Table…").Command.Execute(null);

            Assert.Single(shown);
            Assert.DoesNotContain(model.History, line => line.Contains("not built yet"));
            Assert.Contains("insertion point", model.Session.PromptText);
            Assert.True(model.Session.Feed(new GeoPoint2(30, 250)));

            DxfTable table = TableIn(model.Document);
            Assert.Equal(3, table.NumColumns);
            Assert.Equal("Sheet List", table.TextString(0, 0));
            Assert.Equal(
                new[] { "Sheet No.", "Sheet Title", "Drawn" },
                Enumerable.Range(0, 3).Select(c => table.TextString(1, c))
            );
            Assert.Equal(
                new[] { "G-001", "Architectural", "A-101", "A-102", "Details", "A-501", "Structural", "S-101" },
                Rows(table, 0)
            );
            Assert.Equal(30.0, table.Position.X, 6);
            Assert.Equal(250.0, table.Position.Y, 6);
            Assert.NotNull(CadSheetListTable.ReadSettings(model.Document, table.Handle));
            Assert.True(model.Document.Undo());
            Assert.Empty(model.Document.CurrentSpace.Block.OfType<DxfTable>());
        }

        [Fact]
        public void ADialogThatIsCancelled_MakesNoTable_AndAsksForNoPoint()
        {
            SheetSetManagerViewModel palette = OnASheet(out MainViewModel model);
            model.ShowSheetListTable = dialog => false;

            Item(palette.MenuFor(palette.Root), "Insert Sheet List Table…").Command.Execute(null);

            Assert.False(model.Session.IsRunning);
            Assert.Empty(model.Document.CurrentSpace.Block.OfType<DxfTable>());
        }

        [Fact]
        public void WithNoWindowToShowTheDialog_TheCommandLineSaysSo()
        {
            SheetSetManagerViewModel palette = OnASheet(out MainViewModel model);

            Item(palette.MenuFor(palette.Root), "Insert Sheet List Table…").Command.Execute(null);

            Assert.Contains(model.History, line => line.Contains("Insert Sheet List Table"));
            Assert.False(model.Session.IsRunning);
        }

        [Fact]
        public void TheItem_IsOnOnlyOnALayoutThatIsASheetOfTheSet_AndOffInALockedSet()
        {
            SheetSetManagerViewModel palette = OnASheet(out MainViewModel model);
            SheetSetNodeViewModel sheet = palette.Rows.Single(r => r.Text == "A-102 - First Floor Plan");
            Assert.True(Item(palette.MenuFor(palette.Root), "Insert Sheet List Table…").IsEnabled);
            Assert.True(Item(palette.MenuFor(sheet), "Insert Sheet List Table…").IsEnabled);

            model.SelectSpace(model.Document.ModelSpace, false);
            Assert.False(Item(palette.MenuFor(palette.Root), "Insert Sheet List Table…").IsEnabled);
            Assert.False(Item(palette.MenuFor(sheet), "Insert Sheet List Table…").IsEnabled);
        }

        // ---- The right-click items of the table

        private DxfTable MadeTable(MainViewModel model, SheetSetManagerViewModel palette, bool headers)
        {
            model.ShowSheetListTable = dialog =>
            {
                dialog.IncludeSubsetHeaders = headers;
                return dialog.Apply();
            };
            Item(palette.MenuFor(palette.Root), "Insert Sheet List Table…").Command.Execute(null);
            Assert.True(model.Session.Feed(new GeoPoint2(0, 100)));
            return TableIn(model.Document);
        }

        private static IReadOnlyList<CadMenuItemViewModel> CellMenu(MainViewModel model, DxfTable table) =>
            new TableCellViewModel(model, table.Handle, 3, 0).Menu();

        [Fact]
        public void TheRightClickMenuOfASheetListTable_HasTheTwoItems_AheadOfTheOthers_AnOrdinaryTableHasNeither()
        {
            SheetSetManagerViewModel palette = OnASheet(out MainViewModel model);
            DxfTable table = MadeTable(model, palette, false);

            IReadOnlyList<CadMenuItemViewModel> menu = CellMenu(model, table);

            Assert.Equal("Update Sheet List Table", menu[0].Header);
            Assert.Equal("Edit Sheet List Table Settings…", menu[1].Header);
            Assert.True(menu[2].IsSeparator);
            Assert.True(menu[0].IsEnabled);
            Assert.True(menu[1].IsEnabled);

            var plain = new DxfTable(new GeoPoint3(0, 0, 0), 2, 2, 8, 20);
            plain.SetDatabaseDefaults(model.Document.Database);
            model.Document.CurrentSpace.Block.AppendEntity(plain);
            IReadOnlyList<CadMenuItemViewModel> ordinary = CellMenu(model, plain);
            Assert.DoesNotContain(ordinary, i => i.Header != null && i.Header.Contains("Sheet List"));
        }

        [Fact]
        public void UpdateSheetListTable_ListsTheSetAgainAfterASheetWasAdded_SaysSoOnTheCommandLine_AndIsOneUndoStep()
        {
            SheetSetManagerViewModel palette = OnASheet(out MainViewModel model);
            DxfTable table = MadeTable(model, palette, true);
            string old = table.Handle;
            table.SetTextString(2, 1, "typed by hand");
            palette.Current.Change(set =>
                set.Subsets.Single(s => s.Name == "Structural").NewSheet("S-102", "Slab Plan")
            );

            Item(CellMenu(model, table), "Update Sheet List Table").Command.Execute(null);

            DxfTable again = TableIn(model.Document);
            Assert.NotEqual(old, again.Handle);
            Assert.Equal(
                new[]
                {
                    "G-001",
                    "Architectural",
                    "A-101",
                    "A-102",
                    "Details",
                    "A-501",
                    "Structural",
                    "S-101",
                    "S-102",
                },
                Rows(again, 0)
            );
            Assert.DoesNotContain("typed by hand", Rows(again, 1));
            Assert.Contains(model.History, line =>
                line.Contains("Sheet list table updated") && line.Contains("by hand")
            );
            Assert.True(model.Document.Undo());
            Assert.Equal(old, TableIn(model.Document).Handle);
        }

        [Fact]
        public void EditSheetListTableSettings_OpensTheDialogOnTheTablesRecord_AndOkListsTheSetByTheNewSettings()
        {
            SheetSetManagerViewModel palette = OnASheet(out MainViewModel model);
            DxfTable table = MadeTable(model, palette, false);
            var shown = new List<SheetListTableViewModel>();
            model.ShowSheetListTable = dialog =>
            {
                shown.Add(dialog);
                dialog.TitleText = "Drawings";
                dialog.IncludeSubsetHeaders = true;
                Node(dialog, "Structural").IsChecked = false;
                return dialog.Apply();
            };

            Item(CellMenu(model, table), "Edit Sheet List Table Settings…").Command.Execute(null);

            SheetListTableViewModel edit = Assert.Single(shown);
            Assert.True(edit.IsEditing);
            Assert.Equal("Edit Sheet List Table Settings", edit.Title);
            DxfTable again = TableIn(model.Document);
            Assert.Equal("Drawings", again.TextString(0, 0));
            Assert.Equal(new[] { "G-001", "Architectural", "A-101", "A-102", "Details", "A-501" }, Rows(again, 0));
            Assert.Equal("Drawings", CadSheetListTable.ReadSettings(model.Document, again.Handle).Title);
        }

        [Fact]
        public void EditSheetListTableSettings_CancelledChangesNothing()
        {
            SheetSetManagerViewModel palette = OnASheet(out MainViewModel model);
            DxfTable table = MadeTable(model, palette, false);
            model.ShowSheetListTable = dialog =>
            {
                dialog.TitleText = "Never";
                return false;
            };

            Item(CellMenu(model, table), "Edit Sheet List Table Settings…").Command.Execute(null);

            Assert.Equal(table.Handle, TableIn(model.Document).Handle);
            Assert.Equal("Sheet List Table", TableIn(model.Document).TextString(0, 0));
        }

        [Fact]
        public void ATableWhoseSetIsGone_SaysItOnTheCommandLine_AndStaysAsItIs()
        {
            SheetSetManagerViewModel palette = OnASheet(out MainViewModel model);
            DxfTable table = MadeTable(model, palette, false);
            palette.CloseSet();
            model.SheetSetManager.CloseSet();
            File.Delete(Dst);

            Item(CellMenu(model, table), "Update Sheet List Table").Command.Execute(null);

            Assert.Equal(table.Handle, TableIn(model.Document).Handle);
            Assert.Contains(model.History, line => line.Contains("Example Tower.dst") && line.Contains("not there"));
        }
    }
}
