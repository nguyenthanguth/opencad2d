using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using GeometryHelper.Dxf;
using GeometryHelper.Geometry;
using OpenCAD2D.SheetSets;
using Xunit;

namespace OpenCAD2D.UnitTest
{
    /// <summary>
    /// The sheet list table (A12-D16): a table of the current style with a title row, a row of headings and a row for each sheet in
    /// set order, with a row for each subset where the subsets are headers; the cells are fields of the set bound to the sheet,
    /// the number and title ones with the hyperlink switch; the settings are kept in the table, and Update lists the set again,
    /// after a sheet was added, in the same undo step. Every set and drawing is built in code in a temporary folder.
    /// </summary>
    public sealed class SheetListTableTests : IDisposable
    {
        private readonly string _folder = Path.Combine(
            Path.GetTempPath(),
            "gk",
            "sheetlist-" + Guid.NewGuid().ToString("N")
        );

        public SheetListTableTests()
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

        private string SetFolder => Path.Combine(_folder, "set");

        /// <summary>The set: G-001 Cover in the set itself, the subset Plans with A-101 and A-102, the subset Details with D-001; each a layout of a drawing of its own.</summary>
        private CadSheetSetFile Tower(out CadSheetSubset plans, out CadSheetSubset details)
        {
            Directory.CreateDirectory(SetFolder);
            CadSheetSetFile file = CadSheetSetFile.Create(Path.Combine(SetFolder, "Tower.dst"), "Tower", null);
            CadSheetSubset p = null;
            CadSheetSubset d = null;
            file.Change(set =>
            {
                set.AddCustomProperty("Drawn By", "TN", CadSheetSetPropertyFlags.Sheet);
                Sheet(set, "G-001", "Cover");
                p = set.AddSubset("Plans");
                Sheet(p, "A-101", "Ground floor");
                Sheet(p, "A-102", "First floor");
                d = set.AddSubset("Details");
                Sheet(d, "D-001", "Door");
            });
            plans = p;
            details = d;
            return file;
        }

        private void Sheet(CadSheetSubset into, string number, string title)
        {
            string drawing = SheetSetTestDrawings.Write(SetFolder, number + ".dwg", number);
            CadSheetSetSheet sheet = into.ImportLayouts(drawing, new[] { number }, false).Single();
            sheet.Number = number;
            sheet.Title = title;
        }

        private CadDocument Drawing()
        {
            CadDocument document = CadDocument.From(new DxfDatabase("AC1032"), Path.Combine(SetFolder, "list.dwg"));
            Assert.True(document.ShowSpace(document.Spaces.Single(s => s.Name == "Layout1")).Succeeded);
            return document;
        }

        private static DxfTable TableOf(CadDocument document, string handle) => (DxfTable)document.FindEntity(handle);

        private static List<string> Column(DxfTable table, int column, int from = 2) =>
            Enumerable.Range(from, table.NumRows - from).Select(r => table.TextString(r, column)).ToList();

        [Fact]
        public void TheTable_HasATitleRow_AHeadingRow_AndARowForEachSheetInSetOrder()
        {
            CadSheetSetFile file = Tower(out _, out _);
            CadDocument document = Drawing();

            string handle = CadSheetListTable.Insert(
                document,
                file,
                new CadSheetListTableSettings(),
                new GeoPoint2(0, 100)
            );

            DxfTable table = TableOf(document, handle);
            Assert.Equal(6, table.NumRows);
            Assert.Equal(2, table.NumColumns);
            Assert.Equal("Sheet List Table", table.TextString(0, 0));
            Assert.Equal("Sheet Number", table.TextString(1, 0));
            Assert.Equal("Sheet Title", table.TextString(1, 1));
            Assert.Equal(new[] { "G-001", "A-101", "A-102", "D-001" }, Column(table, 0));
            Assert.Equal(new[] { "Cover", "Ground floor", "First floor", "Door" }, Column(table, 1));
            Assert.Equal(100.0, table.Position.Y, 6);
        }

        [Fact]
        public void WithSubsetsAsHeaders_ASubsetsNameHeadsItsSheets_InAMergedRow()
        {
            CadSheetSetFile file = Tower(out _, out _);
            CadDocument document = Drawing();
            var settings = new CadSheetListTableSettings { IncludeSubsetHeaders = true, Title = "Drawing list" };

            DxfTable table = TableOf(
                document,
                CadSheetListTable.Insert(document, file, settings, new GeoPoint2(0, 100))
            );

            Assert.Equal(8, table.NumRows);
            Assert.Equal("Drawing list", table.TextString(0, 0));
            Assert.Equal(new[] { "G-001", "Plans", "A-101", "A-102", "Details", "D-001" }, Column(table, 0));
            Assert.True(table.IsMergedCell(3, 1, out DxfCellRange merged));
            Assert.Equal(3, merged.TopRow);
            Assert.Equal(0, merged.LeftColumn);
            Assert.Equal(1, merged.RightColumn);
            Assert.True(table.IsMergedCell(0, 1, out _));
        }

        [Fact]
        public void TheNumberAndTitleCells_AreBoundFieldsOfTheSheet_WithTheHyperlinkOfItsDrawing()
        {
            CadSheetSetFile file = Tower(out CadSheetSubset plans, out _);
            CadDocument document = Drawing();
            DxfTable table = TableOf(
                document,
                CadSheetListTable.Insert(document, file, new CadSheetListTableSettings(), new GeoPoint2(0, 100))
            );
            CadSheetSetSheet ground = plans.Sheets[0];
            string head =
                "\\AcSm Database(\""
                + file.Path
                + "\").SheetSet(\""
                + file.SheetSet.Id
                + "\").Component(\""
                + ground.Id
                + "\").";

            Assert.Equal(
                head + "Number \\href \".\\A-101.dwg#,A-101#A-101#1\"",
                table.GetFieldCode(3, 0, 0).Replace("%<", string.Empty).Replace(">%", string.Empty)
            );
            Assert.Equal(
                head + "Title \\href \".\\A-101.dwg#,A-101#Ground floor#1\"",
                table.GetFieldCode(3, 1, 0).Replace("%<", string.Empty).Replace(">%", string.Empty)
            );
        }

        [Fact]
        public void TheCells_FollowARenumberingOfTheSet_WhenTheFieldsAreUpdated()
        {
            CadSheetSetFile file = Tower(out CadSheetSubset plans, out _);
            CadDocument document = Drawing();
            DxfTable table = TableOf(
                document,
                CadSheetListTable.Insert(document, file, new CadSheetListTableSettings(), new GeoPoint2(0, 100))
            );

            file.Change(set => ((CadSheetSubset)set.Find(plans.Id)).Sheets[0].Number = "A-110");
            Assert.True(CadTableEditor.UpdateFields(document, TableOf(document, table.Handle)) > 0);

            Assert.Equal("A-110", TableOf(document, table.Handle).TextString(3, 0));
        }

        [Fact]
        public void TheOtherColumns_ShowTheirProperties_TheFileAndLayoutNamesAsText_AndTheSubsetByItsField()
        {
            CadSheetSetFile file = Tower(out _, out _);
            CadDocument document = Drawing();
            var settings = new CadSheetListTableSettings();
            settings.Columns.Clear();
            settings.Columns.Add(CadSheetListColumn.Of(CadSheetListData.SheetNumber));
            settings.Columns.Add(CadSheetListColumn.Of(CadSheetListData.SubsetName));
            settings.Columns.Add(CadSheetListColumn.Of(CadSheetListData.DrawingFileName));
            settings.Columns.Add(CadSheetListColumn.Of(CadSheetListData.LayoutName, "Tab"));
            settings.Columns.Add(CadSheetListColumn.Custom("Drawn By"));
            settings.Columns.Add(CadSheetListColumn.Of(CadSheetListData.RevisionNumber));

            DxfTable table = TableOf(
                document,
                CadSheetListTable.Insert(document, file, settings, new GeoPoint2(0, 100))
            );

            Assert.Equal(
                new[] { "Sheet Number", "Subset", "Drawing File", "Tab", "Drawn By", "Revision Number" },
                Enumerable.Range(0, 6).Select(c => table.TextString(1, c))
            );
            Assert.Equal(new[] { string.Empty, "Plans", "Plans", "Details" }, Column(table, 1));
            Assert.Equal(new[] { "G-001.dwg", "A-101.dwg", "A-102.dwg", "D-001.dwg" }, Column(table, 2));
            Assert.Equal(new[] { "G-001", "A-101", "A-102", "D-001" }, Column(table, 3));
            Assert.Equal(new[] { "TN", "TN", "TN", "TN" }, Column(table, 4));
            Assert.Equal(new[] { "----", "----", "----", "----" }, Column(table, 5));
        }

        [Fact]
        public void ACheckedSubset_ListsItsSheets_AndPicksUpTheOnesAddedLater_ASheetCheckedAloneIsListedAlone()
        {
            CadSheetSetFile file = Tower(out CadSheetSubset plans, out CadSheetSubset details);
            CadSheetSetSheet cover = file.SheetSet.Sheets.Single();
            CadDocument document = Drawing();
            var settings = new CadSheetListTableSettings { CheckedIds = new[] { plans.Id, cover.Id } };
            string handle = CadSheetListTable.Insert(document, file, settings, new GeoPoint2(0, 100));
            Assert.Equal(new[] { "G-001", "A-101", "A-102" }, Column(TableOf(document, handle), 0));

            file.Change(set => Sheet((CadSheetSubset)set.Find(plans.Id), "A-103", "Second floor"));
            file.Change(set => Sheet((CadSheetSubset)set.Find(details.Id), "D-002", "Window"));
            string again = CadSheetListTable.Update(document, handle, file);

            Assert.Equal(new[] { "G-001", "A-101", "A-102", "A-103" }, Column(TableOf(document, again), 0));
        }

        [Fact]
        public void Update_ListsTheSetAgainAfterASheetWasAdded_InTheSamePlaceAndStyle_AsOneUndoStep()
        {
            CadSheetSetFile file = Tower(out CadSheetSubset plans, out _);
            CadDocument document = Drawing();
            var settings = new CadSheetListTableSettings { IncludeSubsetHeaders = true };
            string handle = CadSheetListTable.Insert(document, file, settings, new GeoPoint2(12, 345));
            DxfTable before = TableOf(document, handle);
            double width = before.ColumnWidth(1);
            string style = before.TableStyleName;

            file.Change(set => Sheet((CadSheetSubset)set.Find(plans.Id), "A-103", "Second floor"));
            string again = CadSheetListTable.Update(document, handle, file);

            DxfTable table = TableOf(document, again);
            Assert.Equal(new[] { "G-001", "Plans", "A-101", "A-102", "A-103", "Details", "D-001" }, Column(table, 0));
            Assert.Null(document.FindEntity(handle));
            Assert.Equal(12.0, table.Position.X, 6);
            Assert.Equal(345.0, table.Position.Y, 6);
            Assert.Equal(style, table.TableStyleName);
            Assert.Equal(width, table.ColumnWidth(1), 6);
            Assert.True(CadSheetListTable.ReadSettings(document, again).IncludeSubsetHeaders);
            Assert.True(document.Undo());
            Assert.NotNull(document.FindEntity(handle));
            Assert.Null(document.FindEntity(again));
        }

        [Fact]
        public void TheSettings_AreKeptInTheTable_AndSurviveTheDrawingBeingSavedAndOpened()
        {
            CadSheetSetFile file = Tower(out CadSheetSubset plans, out _);
            CadDocument document = Drawing();
            var settings = new CadSheetListTableSettings
            {
                Title = "Sheets",
                IncludeSubsetHeaders = true,
                CheckedIds = new[] { plans.Id },
            };
            settings.Columns.Add(CadSheetListColumn.Custom("Drawn By", "Who"));
            string handle = CadSheetListTable.Insert(document, file, settings, new GeoPoint2(0, 0));
            settings.Title = "changed after the table was made";
            string path = Path.Combine(SetFolder, "list.dwg");
            document.SaveAs(path);

            CadDocument reopened = CadDocument.Open(path);
            DxfTable table = reopened
                .Database.Layouts.Single(l => l.LayoutName == "Layout1")
                .BlockTableRecord.OfType<DxfTable>()
                .Single();
            CadSheetListTableSettings read = CadSheetListTable.ReadSettings(reopened, table.Handle);

            Assert.Equal("Sheets", read.Title);
            Assert.True(read.IncludeSubsetHeaders);
            Assert.Equal(new[] { plans.Id }, read.CheckedIds);
            Assert.Equal(
                new[] { CadSheetListData.SheetNumber, CadSheetListData.SheetTitle, CadSheetListData.CustomProperty },
                read.Columns.Select(c => c.Data)
            );
            Assert.Equal("Who", read.Columns[2].Heading);
            Assert.Equal("Drawn By", read.Columns[2].CustomName);
            Assert.Equal(handle, table.Handle);
            Assert.StartsWith("\\AcSm Database(", table.GetFieldCode(3, 0, 0));
            CadTableEditor.UpdateFields(reopened, table);
            Assert.Equal(new[] { "A-101", "A-102" }, Column(table, 0, 3).Take(2));
        }

        [Fact]
        public void UpdateWithNoSetGiven_OpensTheOneTheTableNames_AndSaysWhereItIsGone()
        {
            CadSheetSetFile file = Tower(out _, out _);
            CadDocument document = Drawing();
            string handle = CadSheetListTable.Insert(
                document,
                file,
                new CadSheetListTableSettings(),
                new GeoPoint2(0, 0)
            );

            string again = CadSheetListTable.Update(document, handle);
            File.Delete(file.Path);

            Assert.Equal(6, TableOf(document, again).NumRows);
            Assert.Throws<FileNotFoundException>(() => CadSheetListTable.Update(document, again));
        }

        [Fact]
        public void ARefusedCall_IsSaidWithAnException()
        {
            CadSheetSetFile file = Tower(out _, out _);
            CadDocument document = Drawing();
            var model = CadDocument.From(new DxfDatabase("AC1032"), Path.Combine(SetFolder, "model.dwg"));
            var none = new CadSheetListTableSettings();
            none.Columns.Clear();

            Assert.Throws<InvalidOperationException>(() =>
                CadSheetListTable.Insert(model, file, new CadSheetListTableSettings(), new GeoPoint2(0, 0))
            );
            Assert.Throws<ArgumentException>(() => CadSheetListTable.Insert(document, file, none, new GeoPoint2(0, 0)));
            Assert.Throws<ArgumentNullException>(() =>
                CadSheetListTable.Insert(null, file, new CadSheetListTableSettings(), new GeoPoint2(0, 0))
            );
            Assert.Throws<ArgumentNullException>(() =>
                CadSheetListTable.Insert(document, null, new CadSheetListTableSettings(), new GeoPoint2(0, 0))
            );
            Assert.Throws<ArgumentNullException>(() =>
                CadSheetListTable.Insert(document, file, null, new GeoPoint2(0, 0))
            );
            Assert.Throws<ArgumentException>(() => CadSheetListTable.Update(document, "FFFF"));
            Assert.Null(CadSheetListTable.ReadSettings(document, "FFFF"));
            Assert.Throws<ArgumentException>(() => CadSheetListColumn.Of(CadSheetListData.CustomProperty));
            Assert.Throws<ArgumentException>(() => CadSheetListColumn.Custom(" "));
        }
    }
}
