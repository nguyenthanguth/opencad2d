using System;
using System.IO;
using System.Linq;
using GeometryHelper.Dxf;
using OpenCAD2D.SheetSets;
using Xunit;

namespace OpenCAD2D.UnitTest
{
    /// <summary>
    /// The mark a sheet set leaves in a sheet's drawing (<c>AcSheetSetData</c>, D8): the seven xrecords in AutoCAD's group
    /// codes, written, rewritten with the count up, and read back after a DWG save; and the layout handles the sheet set
    /// finds layouts by surviving that save (R7).
    /// </summary>
    public class SheetSetHintTests : IDisposable
    {
        private static readonly DateTime Noon = new DateTime(2026, 11, 26, 14, 30, 5, 123);

        private readonly string _folder = Path.Combine(
            Path.GetTempPath(),
            "gk",
            "sheetsethint-" + Guid.NewGuid().ToString("N")
        );

        public SheetSetHintTests()
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

        [Fact]
        public void TheMark_IsWrittenAsSevenXrecordsInAutoCadsGroupCodes()
        {
            DxfDatabase database = SheetSetTestDrawings.Build("Ground floor");
            string handle = database.Layouts.Single(l => l.LayoutName == "Ground floor").Handle;

            CadSheetSetHint.Write(database, @"C:\Sets\Tower.dst", @"C:\Sets\A-101.dwg", "Ground floor", handle, Noon);

            var mark = (DxfDBDictionary)database.NamedObjectsDictionary.GetAt("AcSheetSetData");
            var groups = mark.ToDictionary(e => e.Key, e => ((DxfXrecord)e.Value).Data.Single());
            Assert.Equal(
                new[]
                {
                    "LayoutHandle",
                    "LayoutName",
                    "SheetDwgName",
                    "ShSetFileName",
                    "ShSetVersion",
                    "UpdateCount",
                    "UpdateTime",
                },
                mark.Select(e => e.Key).OrderBy(k => k, StringComparer.OrdinalIgnoreCase)
            );
            Assert.Equal(
                (1, handle.ToLowerInvariant()),
                (groups["LayoutHandle"].TypeCode, groups["LayoutHandle"].Value)
            );
            Assert.Equal((1, "Ground floor"), (groups["LayoutName"].TypeCode, groups["LayoutName"].Value));
            Assert.Equal((1, @"C:\Sets\A-101.dwg"), (groups["SheetDwgName"].TypeCode, groups["SheetDwgName"].Value));
            Assert.Equal((1, @"C:\Sets\Tower.dst"), (groups["ShSetFileName"].TypeCode, groups["ShSetFileName"].Value));
            Assert.Equal((90, "2"), (groups["ShSetVersion"].TypeCode, groups["ShSetVersion"].Value));
            Assert.Equal((90, "1"), (groups["UpdateCount"].TypeCode, groups["UpdateCount"].Value));
            Assert.Equal((1, "2026/11/26 14:30:05.123"), (groups["UpdateTime"].TypeCode, groups["UpdateTime"].Value));
        }

        [Fact]
        public void TheLayoutHandle_IsWrittenInLowerCaseWhateverCaseItIsGiven()
        {
            DxfDatabase database = SheetSetTestDrawings.Build("Plan");

            CadSheetSetHint.Write(database, "a.dst", "a.dwg", "Plan", "BA6", Noon);

            Assert.Equal("ba6", CadSheetSetHint.Read(database).LayoutHandle);
        }

        [Fact]
        public void ReadingTheMark_GivesBackWhatWasWritten()
        {
            DxfDatabase database = SheetSetTestDrawings.Build("Plan");
            Assert.Null(CadSheetSetHint.Read(database));

            CadSheetSetHint.Write(database, @"C:\Sets\Tower.dst", @"C:\Sets\A-101.dwg", "Plan", "2B", Noon);

            CadSheetSetHint hint = CadSheetSetHint.Read(database);
            Assert.Equal(@"C:\Sets\Tower.dst", hint.SheetSetFileName);
            Assert.Equal(@"C:\Sets\A-101.dwg", hint.SheetDrawingName);
            Assert.Equal("Plan", hint.LayoutName);
            Assert.Equal("2b", hint.LayoutHandle);
            Assert.Equal(2, hint.Version);
            Assert.Equal(1, hint.UpdateCount);
            Assert.Equal("2026/11/26 14:30:05.123", hint.UpdateTime);
        }

        [Fact]
        public void WritingAgain_ReplacesTheValuesAndRaisesTheCountByOne_WithStillSevenXrecords()
        {
            DxfDatabase database = SheetSetTestDrawings.Build("Plan");
            CadSheetSetHint.Write(database, "a.dst", "a.dwg", "Plan", "2B", Noon);

            CadSheetSetHint.Write(database, "b.dst", "b.dwg", "Plan 2", "2C", Noon.AddMinutes(5));

            CadSheetSetHint hint = CadSheetSetHint.Read(database);
            Assert.Equal(
                ("b.dst", "b.dwg", "Plan 2", "2c"),
                (hint.SheetSetFileName, hint.SheetDrawingName, hint.LayoutName, hint.LayoutHandle)
            );
            Assert.Equal(2, hint.UpdateCount);
            Assert.Equal("2026/11/26 14:35:05.123", hint.UpdateTime);
            Assert.Equal(7, ((DxfDBDictionary)database.NamedObjectsDictionary.GetAt("AcSheetSetData")).Count);
        }

        [Fact]
        public void TheMark_SurvivesADwgSaveAndReopen()
        {
            DxfDatabase database = SheetSetTestDrawings.Build("Plan");
            string handle = database.Layouts.Single(l => l.LayoutName == "Plan").Handle;
            CadSheetSetHint.Write(database, @"C:\Sets\Tower.dst", @"C:\Sets\A-101.dwg", "Plan", handle, Noon);
            string path = Path.Combine(_folder, "a.dwg");

            CadDocument.From(database, "a.dwg").SaveAs(path);

            CadSheetSetHint hint = CadSheetSetHint.Read(path);
            Assert.Equal(@"C:\Sets\Tower.dst", hint.SheetSetFileName);
            Assert.Equal("Plan", hint.LayoutName);
            Assert.Equal(handle.ToLowerInvariant(), hint.LayoutHandle);
            Assert.Equal((2, 1, "2026/11/26 14:30:05.123"), (hint.Version, hint.UpdateCount, hint.UpdateTime));
            DxfDBDictionary mark = (DxfDBDictionary)
                SheetSetTestDrawings.Read(path).NamedObjectsDictionary.GetAt("AcSheetSetData");
            Assert.Equal(90, ((DxfXrecord)mark.GetAt("UpdateCount")).Data.Single().TypeCode);
            Assert.Equal(1, ((DxfXrecord)mark.GetAt("LayoutHandle")).Data.Single().TypeCode);
        }

        [Fact]
        public void ADrawingWithNoMark_ReadsNull_FromAFileToo()
        {
            string path = SheetSetTestDrawings.Write(_folder, "a.dwg", "Plan");

            Assert.Null(CadSheetSetHint.Read(path));
        }

        [Fact]
        public void LayoutHandlesAndNames_AreTheSameAfterADwgSaveAndReopen()
        {
            DxfDatabase database = SheetSetTestDrawings.Build("Cover", "Plan", "Section");
            var before = database
                .Layouts.OrderBy(l => l.TabOrder)
                .Select(l => l.LayoutName + "=" + l.Handle + "/" + l.OwnerHandle)
                .ToArray();
            string path = Path.Combine(_folder, "a.dwg");

            CadDocument.From(database, "a.dwg").SaveAs(path);

            var after = SheetSetTestDrawings
                .Read(path)
                .Layouts.OrderBy(l => l.TabOrder)
                .Select(l => l.LayoutName + "=" + l.Handle + "/" + l.OwnerHandle)
                .ToArray();
            Assert.Equal(before, after);
            Assert.Equal(4, after.Length);
        }

        [Fact]
        public void ADrawingTheDwgWriterWouldLoseSomethingOf_IsNotReplacedWhenTheCallerAsksForThat()
        {
            // A region, whose SAT data the DWG writer does not write yet, is put into the DXF's entities by hand.
            string dxf = Path.Combine(_folder, "host.dxf");
            DxfDatabase.Parse(SheetSetTestDrawings.Build("Plan").ToText()).Save(dxf);
            string text = File.ReadAllText(dxf);
            string line = text.Contains("\r\n") ? "\r\n" : "\n";
            int entities = text.IndexOf("ENTITIES", StringComparison.Ordinal);
            int end = text.IndexOf("ENDSEC", entities, StringComparison.Ordinal);
            int at = text.LastIndexOf('\n', text.LastIndexOf('\n', end - 1) - 1) + 1;
            string region = string.Join(
                line,
                new[]
                {
                    "  0",
                    "REGION",
                    "  5",
                    "FFFF0",
                    "100",
                    "AcDbEntity",
                    "  8",
                    "0",
                    "100",
                    "AcDbModelerGeometry",
                    " 70",
                    "1",
                    string.Empty,
                }
            );
            File.WriteAllText(dxf, text.Insert(at, region));
            DxfDatabase database = CadDrawingFile.Open(dxf);
            string dwg = Path.Combine(_folder, "kept.dwg");
            File.WriteAllText(dwg, "the user's drawing");

            NotSupportedException refused = Assert.Throws<NotSupportedException>(() =>
                CadSheetSetDrawings.Save(database, dwg, true)
            );

            Assert.Contains("REGION 1", refused.Message);
            Assert.Equal("the user's drawing", File.ReadAllText(dwg));
            Assert.Equal(
                new[] { "host.dxf", "kept.dwg" },
                Directory.GetFiles(_folder).Select(Path.GetFileName).OrderBy(n => n, StringComparer.Ordinal)
            );
            CadSheetSetDrawings.Save(database, dwg, false);
            Assert.Equal(new[] { "Plan" }, SheetSetTestDrawings.LayoutNames(dwg));
        }

        [Fact]
        public void LayoutHandles_AreTheSameAfterTheSheetSetSaveOfADrawingToo()
        {
            string path = SheetSetTestDrawings.Write(_folder, "a.dwg", "Cover", "Plan");
            string plan = SheetSetTestDrawings.HandleOf(path, "Plan");
            DxfDatabase database = SheetSetTestDrawings.Read(path);

            CadSheetSetDrawings.Save(database, path, true);
            CadSheetSetDrawings.Save(SheetSetTestDrawings.Read(path), path, true);

            Assert.Equal(plan, SheetSetTestDrawings.HandleOf(path, "Plan"));
            Assert.Equal(new[] { "a.dwg" }, Directory.GetFiles(_folder).Select(Path.GetFileName));
        }
    }
}
