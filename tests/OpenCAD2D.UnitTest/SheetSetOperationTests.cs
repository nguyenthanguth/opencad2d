using System;
using System.IO;
using System.Linq;
using GeometryHelper.Dxf;
using OpenCAD2D.SheetSets;
using Xunit;

namespace OpenCAD2D.UnitTest
{
    /// <summary>
    /// The operations on a sheet set and the drawings of its sheets (A12 3b): New Subset and New Sheet from the template
    /// layout, Import Layouts as Sheets and the checks that refuse a layout, finding a sheet's layout by handle and then name,
    /// re-association, Rename &amp; Renumber with the layout and file renames, Resave All Sheets, and removing and moving.
    /// Every drawing and every set is built in code in a temporary folder.
    /// </summary>
    public class SheetSetOperationTests : IDisposable
    {
        private static readonly DateTime Noon = new DateTime(2026, 11, 26, 14, 30, 5, 123);

        private readonly string _folder = Path.Combine(
            Path.GetTempPath(),
            "gk",
            "sheetsetops-" + Guid.NewGuid().ToString("N")
        );

        public SheetSetOperationTests()
        {
            Directory.CreateDirectory(SetFolder);
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

        private string Dst => Path.Combine(SetFolder, "Tower.dst");

        private string Templates => Path.Combine(_folder, "templates");

        private CadSheetSetFile NewSet(string dst = null)
        {
            CadSheetSetFile file = CadSheetSetFile.Create(dst ?? Dst, "Tower", null, SheetSetFileTests.Counter());
            file.Clock = () => Noon;
            return file;
        }

        /// <summary>A set whose sheet creation template is the "A1 sheet" layout of a template of two layouts.</summary>
        private CadSheetSetFile NewSetWithTemplate(out string template)
        {
            CadSheetSetFile file = NewSet();
            string dwt = SheetSetTestDrawings.Write(Templates, "sheets.dwt", "Cover", "A1 sheet");
            file.Change(set => set.Template = TemplateOf(dwt, "A1 sheet"));
            template = dwt;
            return file;
        }

        private CadDrawingReference TemplateOf(string dwt, string layout) =>
            new CadDrawingReference(CadFileReference.FromPath(dwt, SetFolder), layout, null, null);

        private static bool Same(string a, string b) => string.Equals(a, b, StringComparison.OrdinalIgnoreCase);

        private string Sheets(string file) => Path.Combine(SetFolder, file);

        /// <summary>A drawing beside the set with the layouts named, and its sheets imported into the set, in a new set.</summary>
        private CadSheetSetFile SetWithImport(string drawing, out string path, params string[] layouts)
        {
            path = SheetSetTestDrawings.Write(_folder, drawing, layouts);
            CadSheetSetFile file = NewSet();
            string imported = path;
            file.Change(set => set.ImportLayouts(imported, layouts, false));
            return file;
        }

        // ----- New Subset

        [Fact]
        public void ASubsetName_MustNotBeEmptyNorASiblingsLetterCaseIgnored()
        {
            CadSheetSetFile file = NewSet();
            file.Change(set => set.NewSubset("Plans", null, false));

            Assert.Throws<ArgumentException>(() => file.Change(set => set.NewSubset("PLANS", null, false)));
            Assert.Throws<ArgumentException>(() => file.Change(set => set.NewSubset("  ", null, false)));
            Assert.Throws<ArgumentNullException>(() => file.Change(set => set.NewSubset(null, null, false)));
            file.Change(set => set.Subsets.Single().NewSubset("plans", null, false));
            Assert.Equal(new[] { "Plans" }, file.SheetSet.Subsets.Select(s => s.Name));
            Assert.Equal(new[] { "plans" }, file.SheetSet.Subsets.Single().Subsets.Select(s => s.Name));
        }

        [Fact]
        public void ASubsetWithAFolder_GetsAFolderOnDiskInsideItsParentsAndItIsItsSheetLocation()
        {
            CadSheetSetFile file = NewSet();

            file.Change(set =>
            {
                CadSheetSubset plans = set.NewSubset("Plans", "Floor plans", true);
                plans.NewSubset("Details", null, true);
                set.NewSubset("Notes", null, false);
            });

            string plansFolder = Path.Combine(SetFolder, "Plans");
            Assert.True(Directory.Exists(plansFolder));
            Assert.True(Directory.Exists(Path.Combine(plansFolder, "Details")));
            Assert.False(Directory.Exists(Path.Combine(SetFolder, "Notes")));
            CadSheetSubset reopened = CadSheetSetFile.Open(Dst).SheetSet.Subsets[0];
            Assert.Equal("Floor plans", reopened.Description);
            Assert.True(Same(plansFolder, reopened.NewSheetLocation.Resolve(SetFolder)));
            Assert.Equal(@".\Plans", reopened.NewSheetLocation.RelativeFileName);
            Assert.True(
                Same(
                    Path.Combine(plansFolder, "Details"),
                    reopened.Subsets.Single().NewSheetLocation.Resolve(SetFolder)
                )
            );
            Assert.Null(CadSheetSetFile.Open(Dst).SheetSet.Subsets[1].NewSheetLocation);
        }

        [Fact]
        public void ASubsetFolderName_MustBeANameAFolderCanHave()
        {
            CadSheetSetFile file = NewSet();

            Assert.Throws<ArgumentException>(() => file.Change(set => set.NewSubset("Plans/First", null, true)));

            Assert.Empty(file.SheetSet.Subsets);
            Assert.Equal(new[] { Dst }, Directory.GetFiles(SetFolder));
            file.Change(set => set.NewSubset("Plans/First", null, false));
            Assert.Equal("Plans/First", file.SheetSet.Subsets.Single().Name);
        }

        [Fact]
        public void ASubsetAtAnIndex_GoesThereAmongTheChildren()
        {
            CadSheetSetFile file = NewSet();

            file.Change(set =>
            {
                set.AddSheet("G-001", "Cover", null);
                set.NewSubset("Plans", null, false, 0);
            });

            Assert.Equal("Plans", ((CadSheetSubset)file.SheetSet.Children[0]).Name);
        }

        // ----- New Sheet

        [Fact]
        public void ANewSheet_IsADrawingMadeFromTheTemplatesLayout_WithTheOtherLayoutsTakenOut_AndModelSpaceAsTheTemplateHasIt()
        {
            CadSheetSetFile file = NewSetWithTemplate(out _);

            file.Change(set => set.NewSheet("A-101", "Ground floor"));

            string dwg = Sheets("A-101 Ground floor.dwg");
            Assert.True(File.Exists(dwg));
            Assert.Equal(new[] { "A-101 Ground floor" }, SheetSetTestDrawings.LayoutNames(dwg));
            Assert.Equal(1, SheetSetTestDrawings.ModelCount(dwg));
            Assert.Equal(1, SheetSetTestDrawings.Count<DxfLine>(dwg, "A-101 Ground floor"));
            DxfLine line = SheetSetTestDrawings
                .Read(dwg)
                .Layouts.Single(l => l.LayoutName == "A-101 Ground floor")
                .BlockTableRecord.OfType<DxfLine>()
                .Single();
            Assert.Equal(20, line.StartPoint.X, 6);
            Assert.True(SheetSetTestDrawings.Count<DxfViewport>(dwg, "A-101 Ground floor") >= 1);
        }

        [Fact]
        public void ANewSheet_IsInTheSetWithItsLayoutHandleAndFile()
        {
            CadSheetSetFile file = NewSetWithTemplate(out _);

            file.Change(set => set.NewSheet("A-101", "Ground floor"));

            CadSheetSetSheet sheet = CadSheetSetFile.Open(Dst).SheetSet.Sheets.Single();
            string dwg = Sheets("A-101 Ground floor.dwg");
            Assert.Equal(("A-101", "Ground floor"), (sheet.Number, sheet.Title));
            Assert.Equal("A-101 Ground floor", sheet.Layout.Name);
            Assert.Equal(SheetSetTestDrawings.HandleOf(dwg, "A-101 Ground floor"), sheet.Layout.Handle);
            Assert.Equal(sheet.Layout.Handle.ToUpperInvariant(), sheet.Layout.Handle);
            Assert.NotNull(sheet.Layout.OwnerHandle);
            Assert.True(Same(dwg, sheet.Layout.File.Resolve(SetFolder)));
            Assert.Equal(@".\A-101 Ground floor.dwg", sheet.Layout.File.RelativeFileName);
            CadFoundLayout found = sheet.FindLayout();
            Assert.Equal(CadSheetLayoutStatus.FoundByHandle, found.Status);
        }

        [Fact]
        public void ANewSheet_LeavesTheMarkInItsDrawing_AsAutoCadWritesIt()
        {
            CadSheetSetFile file = NewSetWithTemplate(out _);

            file.Change(set => set.NewSheet("A-101", "Ground floor"));

            string dwg = Sheets("A-101 Ground floor.dwg");
            CadSheetSetHint hint = CadSheetSetHint.Read(dwg);
            string handle = SheetSetTestDrawings.HandleOf(dwg, "A-101 Ground floor");
            Assert.True(Same(Dst, hint.SheetSetFileName));
            Assert.True(Same(dwg, hint.SheetDrawingName));
            Assert.Equal("A-101 Ground floor", hint.LayoutName);
            Assert.Equal(handle.ToLowerInvariant(), hint.LayoutHandle);
            Assert.Equal((2, 1, "2026/11/26 14:30:05.123"), (hint.Version, hint.UpdateCount, hint.UpdateTime));
        }

        [Fact]
        public void ANewSheet_FindsItsLayoutInTheTemplateByHandleThenByName()
        {
            CadSheetSetFile file = NewSet();
            string dwt = SheetSetTestDrawings.Write(Templates, "sheets.dwt", "Cover", "A1 sheet");
            string cover = SheetSetTestDrawings.HandleOf(dwt, "Cover");

            file.Change(set =>
            {
                // The handle is the Cover layout's but the name is A1 sheet: the handle wins.
                set.NewSheet(
                    "1",
                    "By handle",
                    new CadDrawingReference(CadFileReference.FromPath(dwt, SetFolder), "A1 sheet", cover, null)
                );
                // A handle no layout has: the name finds it.
                set.NewSheet(
                    "2",
                    "By name",
                    new CadDrawingReference(CadFileReference.FromPath(dwt, SetFolder), "A1 sheet", "FFFF", null)
                );
            });

            Assert.Equal(
                10,
                SheetSetTestDrawings
                    .Read(Sheets("1 By handle.dwg"))
                    .Layouts.Single(l => l.LayoutName == "1 By handle")
                    .BlockTableRecord.OfType<DxfLine>()
                    .Single()
                    .StartPoint.X,
                6
            );
            Assert.Equal(
                20,
                SheetSetTestDrawings
                    .Read(Sheets("2 By name.dwg"))
                    .Layouts.Single(l => l.LayoutName == "2 By name")
                    .BlockTableRecord.OfType<DxfLine>()
                    .Single()
                    .StartPoint.X,
                6
            );
        }

        [Fact]
        public void ASubsetsTemplateAndLocation_AreUsedBeforeTheSets_AndAFolderThatIsNotThereIsMade()
        {
            CadSheetSetFile file = NewSetWithTemplate(out _);
            string other = SheetSetTestDrawings.Write(Templates, "details.dwt", "Detail sheet");

            file.Change(set =>
            {
                CadSheetSubset details = set.AddSubset("Details");
                details.Template = TemplateOf(other, "Detail sheet");
                details.NewSheetLocation = CadFileReference.FromPath(Sheets("Details"), SetFolder);
                details.NewSheet("D-1", "Joint");
                set.NewSheet("A-1", "Plan");
            });

            Assert.True(File.Exists(Sheets(@"Details\D-1 Joint.dwg")));
            Assert.Equal(new[] { "D-1 Joint" }, SheetSetTestDrawings.LayoutNames(Sheets(@"Details\D-1 Joint.dwg")));
            Assert.True(File.Exists(Sheets("A-1 Plan.dwg")));
        }

        [Fact]
        public void ANestedSubsetWithNoTemplateOrLocationOfItsOwn_UsesTheNearestOneAbove()
        {
            CadSheetSetFile file = NewSetWithTemplate(out _);

            file.Change(set =>
            {
                CadSheetSubset plans = set.AddSubset("Plans");
                plans.NewSheetLocation = CadFileReference.FromPath(Sheets("Plans"), SetFolder);
                plans.AddSubset("Upper").NewSheet("A-2", "First");
            });

            Assert.True(File.Exists(Sheets(@"Plans\A-2 First.dwg")));
        }

        [Fact]
        public void ASheetsLocationWithNoneAnywhere_IsTheFolderOfTheSet()
        {
            CadSheetSetFile file = NewSetWithTemplate(out _);
            file.Change(set => set.NewSheetLocation = null);

            file.Change(set => set.NewSheet("A-3", "Third"));

            Assert.True(File.Exists(Sheets("A-3 Third.dwg")));
        }

        [Fact]
        public void ANewSheet_TakesEverySheetPropertyAtItsDefault_AndNotTheSetsOwn()
        {
            CadSheetSetFile file = NewSetWithTemplate(out _);
            file.Change(set =>
            {
                set.AddCustomProperty("Drawn By", "NT", CadSheetSetPropertyFlags.Sheet);
                set.AddCustomProperty("Client", "Acme", CadSheetSetPropertyFlags.Set);
            });

            file.Change(set => set.NewSheet("A-1", "Plan"));

            CadSheetSetSheet sheet = file.SheetSet.Sheets.Single();
            CadSheetSetProperty drawn = sheet.FindCustomProperty("Drawn By");
            Assert.Equal(("NT", CadSheetSetPropertyFlags.Sheet), (drawn.Value, drawn.Flags));
            Assert.Null(sheet.FindCustomProperty("Client"));
        }

        [Fact]
        public void ANewSheetWithAFileName_IsSavedUnderItWithDwgAddedWhereItIsLeftOff_AtTheIndex()
        {
            CadSheetSetFile file = NewSetWithTemplate(out _);

            file.Change(set =>
            {
                set.AddSheet("G-001", "Cover", null);
                set.NewSheet("A-1", "Plan", null, "ground", 0);
                set.NewSheet("A-2", "Plan two", null, "second.DWG");
            });

            Assert.True(File.Exists(Sheets("ground.dwg")));
            Assert.True(File.Exists(Sheets("second.DWG")));
            Assert.Equal(new[] { "A-1", "G-001", "A-2" }, file.SheetSet.Sheets.Select(s => s.Number));
            Assert.Equal(new[] { "A-1 Plan" }, SheetSetTestDrawings.LayoutNames(Sheets("ground.dwg")));
        }

        [Fact]
        public void TheDefaultNames_AreNumberSpaceTitle_WithCharactersAFileOrLayoutCannotHoldMadeUnderscores()
        {
            Assert.Equal("A-101 Ground floor", CadSheetSetSheet.DefaultFileName("A-101", "Ground floor"));
            Assert.Equal("Ground floor", CadSheetSetSheet.DefaultFileName("", "Ground floor"));
            Assert.Equal("Ground floor", CadSheetSetSheet.DefaultFileName(null, "Ground floor"));
            Assert.Equal("A_1 Plan_ x", CadSheetSetSheet.DefaultFileName("A/1", "Plan: x"));
            Assert.Equal("A_1 Plan_ x", CadSheetSetSheet.DefaultLayoutName("A/1", "Plan: x"));
            Assert.Equal("A_101 Plan_1", CadSheetSetSheet.DefaultLayoutName("A#101", "Plan,1"));
            Assert.Equal("A.1 Plan.x", CadSheetSetSheet.DefaultFileName("A.1", "Plan.x"));
        }

        [Fact]
        public void ANewSheetWhoseFileIsThereAlready_IsRefused_AndNothingIsChanged()
        {
            CadSheetSetFile file = NewSetWithTemplate(out _);
            File.WriteAllText(Sheets("A-1 Plan.dwg"), "not a drawing");
            byte[] set = File.ReadAllBytes(Dst);

            Assert.Throws<IOException>(() => file.Change(s => s.NewSheet("A-1", "Plan")));

            Assert.Equal("not a drawing", File.ReadAllText(Sheets("A-1 Plan.dwg")));
            Assert.Equal(set, File.ReadAllBytes(Dst));
            Assert.Empty(file.SheetSet.Sheets);
        }

        [Fact]
        public void ANewSheet_NeedsATitleATemplateAndAFileNameAFileCanHave()
        {
            CadSheetSetFile file = NewSetWithTemplate(out _);

            Assert.Throws<ArgumentException>(() => file.Change(set => set.NewSheet("A-1", "  ")));
            Assert.Throws<ArgumentNullException>(() => file.Change(set => set.NewSheet(null, "x")));
            Assert.Throws<ArgumentNullException>(() => file.Change(set => set.NewSheet("A-1", null)));
            Assert.Throws<ArgumentException>(() => file.Change(set => set.NewSheet("A-1", "Plan", null, "a?b")));
            CadSheetSetFile bare = NewSet(Path.Combine(_folder, "set", "Bare.dst"));
            Assert.Throws<InvalidOperationException>(() => bare.Change(set => set.NewSheet("A-1", "Plan")));
            CadSheetSetFile gone = NewSet(Path.Combine(_folder, "set", "Gone.dst"));
            gone.Change(set => set.Template = TemplateOf(Path.Combine(Templates, "nothing.dwt"), "A1 sheet"));
            Assert.Throws<FileNotFoundException>(() => gone.Change(set => set.NewSheet("A-1", "Plan")));
            Assert.Throws<FileNotFoundException>(() =>
                file.Change(set =>
                    set.NewSheet("A-1", "Plan", TemplateOf(Path.Combine(Templates, "sheets.dwt"), "No such layout"))
                )
            );
            Assert.Empty(Directory.GetFiles(SetFolder, "*.dwg"));
        }

        // ----- Import Layouts as Sheets

        [Fact]
        public void ImportedLayouts_BecomeSheetsAtTheEndOfTheGroup_WithTheLayoutNameAsTitleAndNoNumber()
        {
            string path = SheetSetTestDrawings.Write(_folder, "plan.dwg", "Cover", "Ground", "First");
            CadSheetSetFile file = NewSet();
            file.Change(set =>
            {
                set.AddSheet("G-001", "Cover", null);
                CadSheetSubset plans = set.AddSubset("Plans");
                plans.AddSheet("A-0", "Existing", null);
                plans.ImportLayouts(path, new[] { "First", "Ground" }, false);
            });

            CadSheetSubset reopened = CadSheetSetFile.Open(Dst).SheetSet.Subsets.Single();
            Assert.Equal(new[] { "A-0", "", "" }, reopened.Sheets.Select(s => s.Number));
            Assert.Equal(new[] { "Existing", "First", "Ground" }, reopened.Sheets.Select(s => s.Title));
            Assert.Equal(new[] { "First", "Ground" }, reopened.Sheets.Skip(1).Select(s => s.Layout.Name));
            Assert.Equal(SheetSetTestDrawings.HandleOf(path, "Ground"), reopened.Sheets[2].Layout.Handle);
            Assert.True(Same(path, reopened.Sheets[1].Layout.File.Resolve(SetFolder)));
        }

        [Fact]
        public void TheFileNameInTheTitle_IsTheDrawingsNameAHyphenAndTheLayout()
        {
            string path = SheetSetTestDrawings.Write(_folder, "plan.dwg", "Ground");
            CadSheetSetFile file = NewSet();

            file.Change(set => set.ImportLayouts(path, new[] { "Ground" }, true));

            Assert.Equal("plan - Ground", file.SheetSet.Sheets.Single().Title);
        }

        [Fact]
        public void AnImport_LeavesTheLayoutsAsTheyWere_AndTheMarkNamesTheLastOne()
        {
            string path = SheetSetTestDrawings.Write(_folder, "plan.dwg", "Cover", "Ground", "First");
            string[] before = SheetSetTestDrawings
                .Read(path)
                .Layouts.OrderBy(l => l.TabOrder)
                .Select(l => l.LayoutName + l.Handle)
                .ToArray();
            int lines = SheetSetTestDrawings.Count<DxfLine>(path, "Ground");
            CadSheetSetFile file = NewSet();

            file.Change(set => set.ImportLayouts(path, new[] { "Ground", "First" }, false));

            string[] after = SheetSetTestDrawings
                .Read(path)
                .Layouts.OrderBy(l => l.TabOrder)
                .Select(l => l.LayoutName + l.Handle)
                .ToArray();
            Assert.Equal(before, after);
            Assert.Equal(lines, SheetSetTestDrawings.Count<DxfLine>(path, "Ground"));
            Assert.Equal(1, SheetSetTestDrawings.ModelCount(path));
            CadSheetSetHint hint = CadSheetSetHint.Read(path);
            Assert.True(Same(Dst, hint.SheetSetFileName));
            Assert.Equal("First", hint.LayoutName);
            Assert.Equal(1, hint.UpdateCount);
        }

        [Fact]
        public void ImportedSheets_TakeEverySheetPropertyAtItsDefault()
        {
            string path = SheetSetTestDrawings.Write(_folder, "plan.dwg", "Ground");
            CadSheetSetFile file = NewSet();
            file.Change(set => set.AddCustomProperty("Checked By", "JD", CadSheetSetPropertyFlags.Sheet));

            file.Change(set => set.ImportLayouts(path, new[] { "Ground" }, false));

            Assert.Equal("JD", file.SheetSet.Sheets.Single().FindCustomProperty("Checked By").Value);
        }

        [Fact]
        public void ALayoutWithAHashInItsName_IsRefused_AndSoIsOneThatIsNotInTheDrawing_AndNothingChanges()
        {
            string path = SheetSetTestDrawings.Write(_folder, "plan.dwg", "Ground", "Level #2");
            CadSheetSetFile file = NewSet();
            byte[] drawing = File.ReadAllBytes(path);

            InvalidOperationException hash = Assert.Throws<InvalidOperationException>(() =>
                file.Change(set => set.ImportLayouts(path, new[] { "Ground", "Level #2" }, false))
            );
            Assert.Throws<InvalidOperationException>(() =>
                file.Change(set => set.ImportLayouts(path, new[] { "Ground", "Nothing" }, false))
            );

            Assert.Contains("#", hash.Message);
            Assert.Empty(file.SheetSet.Sheets);
            Assert.Equal(drawing, File.ReadAllBytes(path));
        }

        [Fact]
        public void AnImportNeedsALayoutAndNoLayoutTwice_AndADrawingThatIsThere()
        {
            string path = SheetSetTestDrawings.Write(_folder, "plan.dwg", "Ground");
            CadSheetSetFile file = NewSet();

            Assert.Throws<ArgumentException>(() => file.Change(set => set.ImportLayouts(path, new string[0], false)));
            Assert.Throws<ArgumentException>(() =>
                file.Change(set => set.ImportLayouts(path, new[] { "Ground", "GROUND" }, false))
            );
            Assert.Throws<ArgumentNullException>(() => file.Change(set => set.ImportLayouts(path, null, false)));
            Assert.Throws<FileNotFoundException>(() =>
                file.Change(set => set.ImportLayouts(Path.Combine(_folder, "none.dwg"), new[] { "Ground" }, false))
            );
        }

        [Fact]
        public void ALayoutThatIsASheetOfThisSetAlready_IsRefused()
        {
            CadSheetSetFile file = SetWithImport("plan.dwg", out string path, "Ground", "First");

            InvalidOperationException refused = Assert.Throws<InvalidOperationException>(() =>
                file.Change(set => set.ImportLayouts(path, new[] { "First" }, false))
            );

            Assert.Contains("already a sheet", refused.Message);
            Assert.Contains("Tower", refused.Message);
            Assert.Equal(2, file.SheetSet.Sheets.Count);
        }

        [Fact]
        public void ALayoutThatIsASheetOfAnotherSetThatIsThere_IsRefused_WithThatSetsName()
        {
            CadSheetSetFile other = SetWithImport("plan.dwg", out string path, "Ground", "First");
            string otherPath = Path.Combine(_folder, "other", "Other.dst");
            Directory.CreateDirectory(Path.GetDirectoryName(otherPath));
            CadSheetSetFile second = CadSheetSetFile.Create(otherPath, "Other", null, SheetSetFileTests.Counter());

            InvalidOperationException refused = Assert.Throws<InvalidOperationException>(() =>
                second.Change(set => set.ImportLayouts(path, new[] { "First" }, false))
            );

            Assert.Contains("Tower", refused.Message);
            Assert.Contains("Tower.dst", refused.Message);
            Assert.Empty(second.SheetSet.Sheets);
            Assert.Equal(2, other.SheetSet.Sheets.Count);
        }

        [Fact]
        public void AMarkThatNamesASetThatIsGone_OrNoLongerListsTheLayout_RefusesNothing()
        {
            CadSheetSetFile first = SetWithImport("plan.dwg", out string path, "Ground", "First");
            CadSheetSetFile second = CadSheetSetFile.Create(
                Path.Combine(_folder, "Second.dst"),
                "Second",
                null,
                SheetSetFileTests.Counter()
            );

            // The first set no longer lists "First": its sheet is removed, the drawing still names the set.
            first.Change(set => set.Remove(set.Sheets.Single(s => s.Layout.Name == "First")));
            second.Change(set => set.ImportLayouts(path, new[] { "First" }, false));
            Assert.Equal("First", second.SheetSet.Sheets.Single().Title);

            // And the set the mark names is gone altogether.
            File.Delete(second.Path);
            File.Delete(first.Path);
            CadSheetSetFile third = CadSheetSetFile.Create(
                Path.Combine(_folder, "Third.dst"),
                "Third",
                null,
                SheetSetFileTests.Counter()
            );
            third.Change(set => set.ImportLayouts(path, new[] { "Ground" }, false));
            Assert.Equal("Ground", third.SheetSet.Sheets.Single().Title);
        }

        [Fact]
        public void AMarkThatNamesNoPathAtAll_RefusesNothing_AndDoesNotThrow()
        {
            string path = SheetSetTestDrawings.Write(_folder, "plan.dwg", "Ground");
            SheetSetTestDrawings.Edit(path, database =>
                CadSheetSetHint.Write(database, "<>|?*\"", path, "Ground", "2B", Noon)
            );
            CadSheetSetFile file = NewSet();

            Assert.False(file.SheetSet.CheckLayouts(path).Single().IsRefused);
            file.Change(set => set.ImportLayouts(path, new[] { "Ground" }, false));

            Assert.Single(file.SheetSet.Sheets);
            Assert.True(Same(Dst, CadSheetSetHint.Read(path).SheetSetFileName));
        }

        [Fact]
        public void CheckingLayouts_ListsEveryLayoutButModelInTabOrder_WithTheReasonItIsRefused()
        {
            CadSheetSetFile file = SetWithImport("plan.dwg", out string path, "Ground");
            string plan2 = SheetSetTestDrawings.Write(_folder, "plan2.dwg", "Cover", "Level #2", "Roof");
            file.Change(set => set.ImportLayouts(plan2, new[] { "Roof" }, false));

            var checks = file.SheetSet.CheckLayouts(plan2);

            Assert.Equal(new[] { "Cover", "Level #2", "Roof" }, checks.Select(c => c.Name));
            Assert.Equal(new[] { false, true, true }, checks.Select(c => c.IsRefused));
            Assert.Contains("#", checks[1].Refusal);
            Assert.Contains("already a sheet", checks[2].Refusal);
            Assert.Null(checks[0].Refusal);
            Assert.Equal(SheetSetTestDrawings.HandleOf(plan2, "Cover"), checks[0].Handle);
            Assert.Single(file.SheetSet.CheckLayouts(path));
        }

        [Fact]
        public void CheckingLayoutsBeforeAnySetIsThere_ReadsTheMarkAloneAndAGoneSetRefusesNothing()
        {
            CadSheetSetFile file = SetWithImport("plan.dwg", out string path, "Ground");
            var checks = CadSheetSetFile.CheckLayouts(path);

            Assert.True(checks.Single().IsRefused);
            Assert.Contains("Tower", checks.Single().Refusal);

            File.Delete(file.Path);
            Assert.False(CadSheetSetFile.CheckLayouts(path).Single().IsRefused);
        }

        [Fact]
        public void ADxfDrawing_HasNoLayoutThatCanBeASheet()
        {
            DxfDatabase database = SheetSetTestDrawings.Build("Ground");
            string dxf = Path.Combine(_folder, "plan.dxf");
            database.Save(dxf);
            CadSheetSetFile file = NewSet();

            Assert.Empty(file.SheetSet.CheckLayouts(dxf));
            Assert.Throws<InvalidOperationException>(() =>
                file.Change(set => set.ImportLayouts(dxf, new[] { "Ground" }, false))
            );
        }

        // ----- Finding the layout of a sheet

        [Fact]
        public void ALayoutIsFoundByHandle_AfterItWasRenamedInTheDrawing()
        {
            CadSheetSetFile file = SetWithImport("plan.dwg", out string path, "Ground", "First");
            SheetSetTestDrawings.Edit(path, database =>
                database.Layouts.Single(l => l.LayoutName == "First").LayoutName = "Level 1"
            );

            CadFoundLayout found = file.SheetSet.Sheets.Single(s => s.Title == "First").FindLayout();

            Assert.Equal(CadSheetLayoutStatus.FoundByHandle, found.Status);
            Assert.Equal("Level 1", found.LayoutName);
            Assert.Equal(SheetSetTestDrawings.HandleOf(path, "Level 1"), found.LayoutHandle);
            Assert.True(Same(path, found.DrawingFile));
            Assert.False(found.IsMissing);
        }

        [Fact]
        public void ALayoutIsFoundByName_WhenTheHandleIsGone()
        {
            CadSheetSetFile file = SetWithImport("plan.dwg", out string path, "Ground");
            file.Change(set =>
            {
                CadSheetSetSheet sheet = set.Sheets.Single();
                CadDrawingReference was = sheet.Layout;
                sheet.Layout = new CadDrawingReference(was.File, was.Name, "FFFF0", was.OwnerHandle);
            });

            CadFoundLayout found = file.SheetSet.Sheets.Single().FindLayout();

            Assert.Equal(CadSheetLayoutStatus.FoundByName, found.Status);
            Assert.Equal("Ground", found.LayoutName);
            Assert.Equal(SheetSetTestDrawings.HandleOf(path, "Ground"), found.LayoutHandle);
        }

        [Fact]
        public void ALayoutIsFoundByName_LetterCaseIgnored_WhenTheSheetHoldsNoHandle()
        {
            CadSheetSetFile file = SetWithImport("plan.dwg", out _, "Ground");
            file.Change(set =>
            {
                CadDrawingReference was = set.Sheets.Single().Layout;
                set.Sheets.Single().Layout = new CadDrawingReference(was.File, "GROUND", null, null);
            });

            Assert.Equal(CadSheetLayoutStatus.FoundByName, file.SheetSet.Sheets.Single().FindLayout().Status);
        }

        [Fact]
        public void ASheetIsMissing_WhenItsFileIsDeleted_OrTheDrawingHasNeitherHandleNorName_OrItHasNoLayout()
        {
            CadSheetSetFile file = SetWithImport("plan.dwg", out string path, "Ground", "First");
            file.Change(set => set.AddSheet("X-1", "No layout", null));
            SheetSetTestDrawings.Edit(path, database =>
            {
                database.Layouts.Single(l => l.LayoutName == "First").LayoutName = "Other";
            });
            file.Change(set =>
            {
                CadSheetSetSheet first = set.Sheets.Single(s => s.Title == "First");
                first.Layout = new CadDrawingReference(first.Layout.File, "First", "FFFF0", null);
            });

            Assert.True(file.SheetSet.Sheets.Single(s => s.Title == "First").FindLayout().IsMissing);
            Assert.True(file.SheetSet.Sheets.Single(s => s.Title == "No layout").FindLayout().IsMissing);
            Assert.Null(file.SheetSet.Sheets.Single(s => s.Title == "No layout").FindLayout().DrawingFile);
            Assert.False(file.SheetSet.Sheets.Single(s => s.Title == "Ground").FindLayout().IsMissing);

            File.Delete(path);
            CadFoundLayout gone = file.SheetSet.Sheets.Single(s => s.Title == "Ground").FindLayout();
            Assert.True(gone.IsMissing);
            Assert.Null(gone.DrawingFile);
            Assert.Null(gone.LayoutName);
        }

        [Fact]
        public void ASheetIsStillFound_WhenTheWholeFolderIsMoved()
        {
            string drawings = Path.Combine(SetFolder, "Sheets");
            string path = SheetSetTestDrawings.Write(drawings, "plan.dwg", "Ground");
            CadSheetSetFile file = NewSet();
            file.Change(set => set.ImportLayouts(path, new[] { "Ground" }, false));
            string moved = Path.Combine(_folder, "moved");

            Directory.Move(SetFolder, moved);

            CadSheetSetFile reopened = CadSheetSetFile.Open(Path.Combine(moved, "Tower.dst"));
            Assert.Equal(CadSheetLayoutStatus.FoundByHandle, reopened.SheetSet.Sheets.Single().FindLayout().Status);
            Assert.True(
                Same(
                    Path.Combine(moved, "Sheets", "plan.dwg"),
                    reopened.SheetSet.Sheets.Single().FindLayout().DrawingFile
                )
            );
        }

        // ----- Associate (Expected Layout)

        [Fact]
        public void ASheetThatIsMissing_IsAssociatedWithAnotherLayout_AndTheDrawingGetsTheMark()
        {
            CadSheetSetFile file = SetWithImport("plan.dwg", out string path, "Ground");
            File.Delete(path);
            string other = SheetSetTestDrawings.Write(_folder, "other.dwg", "Cover", "Site");

            file.Change(set => set.Sheets.Single().Associate(other, "site"));

            CadSheetSetSheet sheet = CadSheetSetFile.Open(Dst).SheetSet.Sheets.Single();
            Assert.Equal("Site", sheet.Layout.Name);
            Assert.Equal(SheetSetTestDrawings.HandleOf(other, "Site"), sheet.Layout.Handle);
            Assert.True(Same(other, sheet.Layout.File.Resolve(SetFolder)));
            Assert.Equal(CadSheetLayoutStatus.FoundByHandle, sheet.FindLayout().Status);
            CadSheetSetHint hint = CadSheetSetHint.Read(other);
            Assert.Equal(("Site", 1), (hint.LayoutName, hint.UpdateCount));
            Assert.Equal("Ground", sheet.Title);
        }

        [Fact]
        public void AssociatingWithALayoutAnotherSheetIsOrThatIsNotThere_IsRefused_ButItsOwnIsNot()
        {
            CadSheetSetFile file = SetWithImport("plan.dwg", out string path, "Ground", "First");

            Assert.Throws<InvalidOperationException>(() =>
                file.Change(set => set.Sheets.First(s => s.Title == "Ground").Associate(path, "First"))
            );
            Assert.Throws<InvalidOperationException>(() =>
                file.Change(set => set.Sheets.First(s => s.Title == "Ground").Associate(path, "Nothing"))
            );
            Assert.Throws<FileNotFoundException>(() =>
                file.Change(set => set.Sheets.First().Associate(Path.Combine(_folder, "none.dwg"), "Ground"))
            );

            file.Change(set => set.Sheets.First(s => s.Title == "Ground").Associate(path, "Ground"));
            Assert.Equal("Ground", file.SheetSet.Sheets.First(s => s.Title == "Ground").Layout.Name);
        }

        // ----- Rename & Renumber

        [Fact]
        public void ANewNumberAndTitleAlone_AreWrittenIntoTheSet_AndTheDrawingIsNotTouched()
        {
            CadSheetSetFile file = SetWithImport("plan.dwg", out string path, "Ground");
            byte[] drawing = File.ReadAllBytes(path);

            file.Change(set => set.Sheets.Single().RenameAndRenumber("A-102", "Ground floor"));

            CadSheetSetSheet sheet = CadSheetSetFile.Open(Dst).SheetSet.Sheets.Single();
            Assert.Equal(("A-102", "Ground floor"), (sheet.Number, sheet.Title));
            Assert.Equal("Ground", sheet.Layout.Name);
            Assert.Equal(drawing, File.ReadAllBytes(path));
        }

        [Fact]
        public void ANewNumberAndTitle_AreAllowedOnASheetThatIsMissing()
        {
            CadSheetSetFile file = NewSet();
            file.Change(set => set.AddSheet("A-1", "Gone", null));

            file.Change(set => set.Sheets.Single().RenameAndRenumber("A-2", "Still gone", null, null));

            Assert.Equal("A-2", file.SheetSet.Sheets.Single().Number);
        }

        [Fact]
        public void ANewLayoutName_RenamesTheLayoutInTheDrawingKeepingItsHandle_AndRewritesTheMark()
        {
            CadSheetSetFile file = SetWithImport("plan.dwg", out string path, "Ground", "First");
            string handle = SheetSetTestDrawings.HandleOf(path, "Ground");
            int lines = SheetSetTestDrawings.Count<DxfLine>(path, "Ground");
            file.Clock = () => Noon.AddHours(1);

            file.Change(set =>
                set.Sheets.Single(s => s.Title == "Ground")
                    .RenameAndRenumber("A-101", "Ground floor", "A-101 Ground floor")
            );

            Assert.Equal(new[] { "A-101 Ground floor", "First" }, SheetSetTestDrawings.LayoutNames(path));
            Assert.Equal(handle, SheetSetTestDrawings.HandleOf(path, "A-101 Ground floor"));
            Assert.Equal(lines, SheetSetTestDrawings.Count<DxfLine>(path, "A-101 Ground floor"));
            CadSheetSetSheet sheet = CadSheetSetFile.Open(Dst).SheetSet.Sheets.Single(s => s.Number == "A-101");
            Assert.Equal("A-101 Ground floor", sheet.Layout.Name);
            Assert.Equal(handle, sheet.Layout.Handle);
            CadSheetSetHint hint = CadSheetSetHint.Read(path);
            Assert.Equal(("A-101 Ground floor", handle.ToLowerInvariant()), (hint.LayoutName, hint.LayoutHandle));
            Assert.Equal(2, hint.UpdateCount);
            Assert.Equal("2026/11/26 15:30:05.123", hint.UpdateTime);
            Assert.Equal(CadSheetLayoutStatus.FoundByHandle, sheet.FindLayout().Status);
        }

        [Fact]
        public void ANewFileName_MovesTheDrawing_AndEverySheetOfThatDrawingFollowsIt_AndTheMarkNamesTheNewFile()
        {
            CadSheetSetFile file = SetWithImport("plan.dwg", out string path, "Ground", "First");
            string handle = SheetSetTestDrawings.HandleOf(path, "Ground");

            file.Change(set =>
                set.Sheets.Single(s => s.Title == "Ground").RenameAndRenumber("A-101", "Ground", null, "A-101 Plans")
            );

            string renamed = Path.Combine(_folder, "A-101 Plans.dwg");
            Assert.False(File.Exists(path));
            Assert.True(File.Exists(renamed));
            Assert.Equal(new[] { "Ground", "First" }, SheetSetTestDrawings.LayoutNames(renamed));
            Assert.Equal(handle, SheetSetTestDrawings.HandleOf(renamed, "Ground"));
            CadSheetSetFile reopened = CadSheetSetFile.Open(Dst);
            Assert.All(reopened.SheetSet.Sheets, s => Assert.True(Same(renamed, s.Layout.File.Resolve(SetFolder))));
            Assert.All(reopened.SheetSet.Sheets, s =>
                Assert.Equal(CadSheetLayoutStatus.FoundByHandle, s.FindLayout().Status)
            );
            Assert.True(Same(renamed, CadSheetSetHint.Read(renamed).SheetDrawingName));
            Assert.Equal(new[] { Dst }, Directory.GetFiles(SetFolder));
            Assert.Equal(new[] { "A-101 Plans.dwg" }, Directory.GetFiles(_folder, "*.dwg").Select(Path.GetFileName));
        }

        [Fact]
        public void ALayoutNameAndAFileNameTogether_AreBothDone()
        {
            CadSheetSetFile file = SetWithImport("plan.dwg", out string path, "Ground");

            file.Change(set =>
                set.Sheets.Single().RenameAndRenumber("A-1", "Ground floor", "A-1 Ground floor", "A-1 Ground floor.dwg")
            );

            string renamed = Path.Combine(_folder, "A-1 Ground floor.dwg");
            Assert.Equal(new[] { "A-1 Ground floor" }, SheetSetTestDrawings.LayoutNames(renamed));
            Assert.Equal("A-1 Ground floor", CadSheetSetHint.Read(renamed).LayoutName);
            Assert.Equal(CadSheetLayoutStatus.FoundByHandle, file.SheetSet.Sheets.Single().FindLayout().Status);
            Assert.False(File.Exists(path));
        }

        [Fact]
        public void ACaseOnlyChangeOfTheFileName_RenamesTheFile()
        {
            CadSheetSetFile file = SetWithImport("plan.dwg", out string path, "Ground");

            file.Change(set => set.Sheets.Single().RenameAndRenumber("", "Ground", null, "PLAN.dwg"));

            Assert.Equal(new[] { "PLAN.dwg" }, Directory.GetFiles(_folder, "*.dwg").Select(Path.GetFileName));
            Assert.Equal(CadSheetLayoutStatus.FoundByHandle, file.SheetSet.Sheets.Single().FindLayout().Status);
            Assert.Equal("Ground", SheetSetTestDrawings.LayoutNames(Path.Combine(_folder, "PLAN.dwg")).Single());
            Assert.NotNull(path);
        }

        [Fact]
        public void AFileNameThatIsThereAlready_IsRefused_AndNothingChanges()
        {
            CadSheetSetFile file = SetWithImport("plan.dwg", out string path, "Ground");
            string other = SheetSetTestDrawings.Write(_folder, "taken.dwg", "Site");
            byte[] drawing = File.ReadAllBytes(path);
            byte[] taken = File.ReadAllBytes(other);

            Assert.Throws<IOException>(() =>
                file.Change(set => set.Sheets.Single().RenameAndRenumber("A-9", "Ground", "Renamed", "taken"))
            );

            Assert.Equal(drawing, File.ReadAllBytes(path));
            Assert.Equal(taken, File.ReadAllBytes(other));
            Assert.Equal("", file.SheetSet.Sheets.Single().Number);
            Assert.Equal("Ground", file.SheetSet.Sheets.Single().Layout.Name);
        }

        [Fact]
        public void ALayoutNameNoLayoutCanHave_OrTheNameOfAnother_OrAFileNameNoFileCanHave_IsRefused()
        {
            CadSheetSetFile file = SetWithImport("plan.dwg", out string path, "Ground", "First");
            byte[] drawing = File.ReadAllBytes(path);
            CadSheetSetSheet Ground(CadSheetSet set) => set.Sheets.Single(s => s.Title == "Ground");

            Assert.Throws<ArgumentException>(() =>
                file.Change(set => Ground(set).RenameAndRenumber("", "Ground", "A#1"))
            );
            Assert.Throws<ArgumentException>(() =>
                file.Change(set => Ground(set).RenameAndRenumber("", "Ground", "A/1"))
            );
            Assert.Throws<ArgumentException>(() =>
                file.Change(set => Ground(set).RenameAndRenumber("", "Ground", "Model"))
            );
            Assert.Throws<ArgumentException>(() =>
                file.Change(set => Ground(set).RenameAndRenumber("", "Ground", "  "))
            );
            Assert.Throws<ArgumentException>(() =>
                file.Change(set => Ground(set).RenameAndRenumber("", "Ground", null, "a:b"))
            );
            Assert.Throws<InvalidOperationException>(() =>
                file.Change(set => Ground(set).RenameAndRenumber("", "Ground", "first"))
            );
            Assert.Throws<ArgumentNullException>(() =>
                file.Change(set => Ground(set).RenameAndRenumber(null, "Ground"))
            );
            Assert.Equal(drawing, File.ReadAllBytes(path));
        }

        [Fact]
        public void ALayoutOrFileRename_OfASheetThatIsMissing_IsRefused()
        {
            CadSheetSetFile file = SetWithImport("plan.dwg", out string path, "Ground");
            File.Delete(path);

            Assert.Throws<InvalidOperationException>(() =>
                file.Change(set => set.Sheets.Single().RenameAndRenumber("", "Ground", "Renamed"))
            );
            Assert.Throws<InvalidOperationException>(() =>
                file.Change(set => set.Sheets.Single().RenameAndRenumber("", "Ground", null, "renamed"))
            );
        }

        [Fact]
        public void RenamingTheLayoutToWhatItIsAlready_OrTheFileToItsOwnName_TouchesNoDrawing()
        {
            CadSheetSetFile file = SetWithImport("plan.dwg", out string path, "Ground");
            byte[] drawing = File.ReadAllBytes(path);

            file.Change(set => set.Sheets.Single().RenameAndRenumber("A-1", "Ground", "Ground", "plan.dwg"));

            Assert.Equal(drawing, File.ReadAllBytes(path));
            Assert.Equal("A-1", file.SheetSet.Sheets.Single().Number);
        }

        // ----- Resave All Sheets

        [Fact]
        public void ResavingAllSheets_BringsTheSetAndTheDrawingsToAgree_AndReportsTheSheetsThatAreMissing()
        {
            string plan = SheetSetTestDrawings.Write(_folder, "plan.dwg", "Ground", "First");
            string site = SheetSetTestDrawings.Write(_folder, "site.dwg", "Site");
            string gone = SheetSetTestDrawings.Write(_folder, "gone.dwg", "Lost");
            CadSheetSetFile file = NewSet();
            file.Change(set =>
            {
                set.ImportLayouts(plan, new[] { "Ground", "First" }, false);
                set.ImportLayouts(site, new[] { "Site" }, false);
                set.ImportLayouts(gone, new[] { "Lost" }, false);
            });
            SheetSetTestDrawings.Edit(plan, database =>
                database.Layouts.Single(l => l.LayoutName == "First").LayoutName = "Level 1"
            );
            File.Delete(gone);
            file.Clock = () => Noon.AddDays(1);

            string[] missing = null;
            file.Change(set => missing = set.ResaveAllSheets().Select(s => s.Title).ToArray());

            Assert.Equal(new[] { "Lost" }, missing);
            CadSheetSet reopened = CadSheetSetFile.Open(Dst).SheetSet;
            Assert.Equal(new[] { "Ground", "Level 1", "Site", "Lost" }, reopened.Sheets.Select(s => s.Layout.Name));
            Assert.Equal("Level 1", CadSheetSetHint.Read(plan).LayoutName);
            Assert.Equal(("Site", 2), (CadSheetSetHint.Read(site).LayoutName, CadSheetSetHint.Read(site).UpdateCount));
            Assert.Equal("2026/11/27 14:30:05.123", CadSheetSetHint.Read(site).UpdateTime);
        }

        // ----- Removing and moving

        [Fact]
        public void RemovingASheet_TakesItOutOfTheSetOnly_AndRemovingASubsetTakesItsSheetsToo()
        {
            string plan = SheetSetTestDrawings.Write(_folder, "plan.dwg", "Ground", "First");
            CadSheetSetFile file = NewSet();
            file.Change(set =>
            {
                set.NewSubset("Plans", null, false).ImportLayouts(plan, new[] { "Ground", "First" }, false);
                set.NewSubset("Details", null, false).AddSheet("D-1", "Joint", null);
            });
            byte[] drawing = File.ReadAllBytes(plan);

            file.Change(set => set.Subsets[0].Remove(set.Subsets[0].Sheets[0]));
            Assert.Equal(new[] { "First" }, file.SheetSet.Subsets[0].Sheets.Select(s => s.Title));
            file.Change(set => set.Remove(set.Subsets[0]));

            Assert.Equal(new[] { "Details" }, file.SheetSet.Subsets.Select(s => s.Name));
            Assert.Single(file.SheetSet.AllSheets());
            Assert.Equal(drawing, File.ReadAllBytes(plan));
        }

        [Fact]
        public void MovingASheetOrASubset_ChangesItsPlaceAndItsParent_AndNoFile()
        {
            string plan = SheetSetTestDrawings.Write(_folder, "plan.dwg", "Ground", "First");
            CadSheetSetFile file = NewSet();
            file.Change(set =>
            {
                CadSheetSubset plans = set.NewSubset("Plans", null, false);
                plans.ImportLayouts(plan, new[] { "Ground", "First" }, false);
                set.NewSubset("Details", null, false);
                set.AddSheet("G-001", "Cover", null);
            });
            byte[] drawing = File.ReadAllBytes(plan);

            file.Change(set => set.Move(set.Subsets[0].Sheets[1], set.Subsets[1], 0));
            file.Change(set => set.Move(set.Subsets[1], set.Subsets[0], -1));
            file.Change(set => set.Move(set.Sheets[0], set, 0));

            CadSheetSet reopened = CadSheetSetFile.Open(Dst).SheetSet;
            Assert.Equal(2, reopened.Children.Count);
            Assert.Equal("G-001", ((CadSheetSetSheet)reopened.Children[0]).Number);
            CadSheetSubset plansNow = (CadSheetSubset)reopened.Children[1];
            Assert.Equal(new[] { "Ground" }, plansNow.Sheets.Select(s => s.Title));
            Assert.Equal(new[] { "Details" }, plansNow.Subsets.Select(s => s.Name));
            Assert.Equal(new[] { "First" }, plansNow.Subsets.Single().Sheets.Select(s => s.Title));
            Assert.Equal(drawing, File.ReadAllBytes(plan));
        }
    }
}
