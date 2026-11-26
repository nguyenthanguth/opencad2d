using System;
using System.IO;
using System.Linq;
using GeometryHelper.Dxf;
using GeometryHelper.Geometry;
using OpenCAD2D.SheetSets;
using Xunit;

namespace OpenCAD2D.UnitTest
{
    /// <summary>
    /// What Edit Sheet List Table Settings asks of the sheet list table engine: the path of the set a table was made from, and an
    /// update that lists the set by new settings, in the same place and style, the columns that stay keeping their widths, as one
    /// undo step. The set and the drawing are built in code in a temporary folder.
    /// </summary>
    public sealed class SheetListTableEditTests : IDisposable
    {
        private readonly string _folder = Path.Combine(
            Path.GetTempPath(),
            "gk",
            "sheetlistedit-" + Guid.NewGuid().ToString("N")
        );

        public SheetListTableEditTests()
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

        private CadSheetSetFile Tower()
        {
            CadSheetSetFile file = CadSheetSetFile.Create(Path.Combine(_folder, "Tower.dst"), "Tower", null);
            file.Change(set =>
            {
                set.AddCustomProperty("Drawn By", "TN", CadSheetSetPropertyFlags.Sheet);
                CadSheetSubset plans = set.AddSubset("Plans");

                foreach (string number in new[] { "A-101", "A-102" })
                {
                    string drawing = SheetSetTestDrawings.Write(_folder, number + ".dwg", number);
                    CadSheetSetSheet sheet = plans.ImportLayouts(drawing, new[] { number }, false).Single();
                    sheet.Number = number;
                    sheet.Title = "Plan " + number;
                }
            });
            return file;
        }

        private CadDocument Drawing()
        {
            CadDocument document = CadDocument.From(new DxfDatabase("AC1032"), Path.Combine(_folder, "list.dwg"));
            Assert.True(document.ShowSpace(document.Spaces.Single(s => s.Name == "Layout1")).Succeeded);
            return document;
        }

        [Fact]
        public void ReadSetFile_GivesThePathOfTheSet_AndNullForAnObjectThatIsNotASheetListTable()
        {
            CadSheetSetFile file = Tower();
            CadDocument document = Drawing();
            string handle = CadSheetListTable.Insert(
                document,
                file,
                new CadSheetListTableSettings(),
                new GeoPoint2(0, 0)
            );

            Assert.Equal(file.Path, CadSheetListTable.ReadSetFile(document, handle));
            Assert.Null(CadSheetListTable.ReadSetFile(document, "FFFF"));
            Assert.Throws<ArgumentNullException>(() => CadSheetListTable.ReadSetFile(document, null));
        }

        [Fact]
        public void UpdateWithNewSettings_ListsTheSetByThem_InTheSamePlace_KeepsTheWidthOfAColumnThatStays_AsOneUndoStep()
        {
            CadSheetSetFile file = Tower();
            CadDocument document = Drawing();
            string handle = CadSheetListTable.Insert(
                document,
                file,
                new CadSheetListTableSettings(),
                new GeoPoint2(12, 345)
            );
            var table = (DxfTable)document.FindEntity(handle);
            table.SetColumnWidth(0, 51.0);

            var settings = new CadSheetListTableSettings { Title = "Drawn", IncludeSubsetHeaders = true };
            settings.Columns.Clear();
            settings.Columns.Add(CadSheetListColumn.Of(CadSheetListData.SheetNumber, "No."));
            settings.Columns.Add(CadSheetListColumn.Custom("Drawn By", "Who"));
            settings.Columns.Add(CadSheetListColumn.Of(CadSheetListData.SheetTitle));
            string again = CadSheetListTable.Update(document, handle, file, settings);

            var made = (DxfTable)document.FindEntity(again);
            Assert.Equal(3, made.NumColumns);
            Assert.Equal(5, made.NumRows);
            Assert.Equal("Drawn", made.TextString(0, 0));
            Assert.Equal("No.", made.TextString(1, 0));
            Assert.Equal("Plans", made.TextString(2, 0));
            Assert.Equal(51.0, made.ColumnWidth(0), 6);
            Assert.Equal(45.0, made.ColumnWidth(1), 6);
            Assert.Equal(90.0, made.ColumnWidth(2), 6);
            Assert.Equal(12.0, made.Position.X, 6);
            Assert.Equal(345.0, made.Position.Y, 6);
            CadSheetListTableSettings kept = CadSheetListTable.ReadSettings(document, again);
            Assert.Equal("Drawn", kept.Title);
            Assert.True(kept.IncludeSubsetHeaders);
            Assert.Equal("Who", kept.Columns[1].Heading);
            Assert.Null(document.FindEntity(handle));
            Assert.True(document.Undo());
            Assert.NotNull(document.FindEntity(handle));
            Assert.Null(document.FindEntity(again));
        }

        [Fact]
        public void UpdateWithSettingsThatHaveNoColumn_IsRefused_AndTheTableStays()
        {
            CadSheetSetFile file = Tower();
            CadDocument document = Drawing();
            string handle = CadSheetListTable.Insert(
                document,
                file,
                new CadSheetListTableSettings(),
                new GeoPoint2(0, 0)
            );
            var settings = new CadSheetListTableSettings();
            settings.Columns.Clear();

            Assert.Throws<ArgumentException>(() => CadSheetListTable.Update(document, handle, file, settings));
            Assert.NotNull(document.FindEntity(handle));
        }
    }
}
