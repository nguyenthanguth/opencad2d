using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Xml.Linq;
using GeometryHelper.Dxf;
using OpenCAD2D.SheetSets;
using Xunit;

namespace OpenCAD2D.UnitTest
{
    /// <summary>
    /// Making a sheet set (A12 3b): from an example set, from existing drawings with the import options, and the custom
    /// properties, resource locations and page setup overrides file that go with it (D10). Every drawing and set is built in
    /// code in a temporary folder.
    /// </summary>
    public class SheetSetCreateTests : IDisposable
    {
        private static readonly DateTime Noon = new DateTime(2026, 11, 26, 14, 30, 5, 123);

        private readonly string _folder = Path.Combine(
            Path.GetTempPath(),
            "gk",
            "sheetsetcreate-" + Guid.NewGuid().ToString("N")
        );

        public SheetSetCreateTests()
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

        private static bool Same(string a, string b) => string.Equals(a, b, StringComparison.OrdinalIgnoreCase);

        /// <summary>A GUID source that counts from another base than <see cref="SheetSetFileTests.Counter"/>, so that two files never share an ID.</summary>
        private static Func<Guid> CounterFrom(uint start)
        {
            uint n = start;
            return () => new Guid(++n, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0);
        }

        private string Example(out string template)
        {
            string folder = Path.Combine(_folder, "example");
            Directory.CreateDirectory(Path.Combine(folder, "Models"));
            string dwt = SheetSetTestDrawings.Write(folder, "tpl.dwt", "Cover", "A1 sheet");
            string handle = SheetSetTestDrawings.HandleOf(dwt, "A1 sheet");
            string dst = Path.Combine(folder, "Example.dst");
            CadSheetSetFile example = CadSheetSetFile.Create(
                dst,
                "Example Set",
                "Example description",
                SheetSetFileTests.Counter()
            );
            example.Change(set =>
            {
                CadDrawingReference layout = new CadDrawingReference(
                    CadFileReference.FromPath(dwt, folder),
                    "A1 sheet",
                    handle,
                    null
                );
                set.ProjectName = "Project X";
                set.Template = layout;
                set.PromptForTemplate = true;
                set.PageSetupOverrides = CadFileReference.FromPath(dwt, folder);
                set.AddResourceLocation(CadFileReference.FromPath(Path.Combine(folder, "Models"), folder));
                set.AddCustomProperty("Client", "Acme", CadSheetSetPropertyFlags.Set);
                set.AddCustomProperty("Drawn By", "NT", CadSheetSetPropertyFlags.Sheet);
                CadSheetSubset plans = set.AddSubset("Plans", "Floor plans");
                plans.Template = layout;
                plans.NewSheetLocation = CadFileReference.FromPath(folder, folder);
                plans.AddSubset("Details");
                plans.AddSheet("A-101", "Ground floor", null);
                set.AddSubset("Structural");
                set.AddSheet("G-001", "Cover", null);
            });
            template = dwt;
            return dst;
        }

        // ----- From an example

        [Fact]
        public void ASetFromAnExample_KeepsTheSubsetsAndSettings_AndHasNoSheets()
        {
            string example = Example(out string dwt);
            byte[] exampleBytes = File.ReadAllBytes(example);
            string folder = Path.Combine(_folder, "new");
            Directory.CreateDirectory(folder);
            string dst = Path.Combine(folder, "Tower.dst");

            CadSheetSetFile file = CadSheetSetFile.CreateFromExample(
                dst,
                "Tower",
                "A tower",
                example,
                false,
                CounterFrom(0xB0000000)
            );

            CadSheetSet set = CadSheetSetFile.Open(dst).SheetSet;
            Assert.Equal(("Tower", "A tower"), (set.Name, set.Description));
            Assert.Equal("Project X", set.ProjectName);
            Assert.Empty(set.AllSheets());
            Assert.Equal(new[] { "Plans", "Structural" }, set.Subsets.Select(s => s.Name));
            Assert.Equal("Floor plans", set.Subsets[0].Description);
            Assert.Equal(new[] { "Details" }, set.Subsets[0].Subsets.Select(s => s.Name));
            Assert.Empty(set.Sheets);
            Assert.True(set.PromptForTemplate);
            Assert.Equal("1.1", file.DbVersion);
            Assert.Equal(1, CadSheetSetFile.Open(dst).FileRevision);
            Assert.Equal(exampleBytes, File.ReadAllBytes(example));
            Assert.NotNull(file.Document.Descendants("AcSmPublishOptions").SingleOrDefault());
        }

        [Fact]
        public void ASetFromAnExample_HasIdsOfItsOwn_NoneSharedWithTheExampleAndNoneTwice()
        {
            string example = Example(out _);
            string dst = Path.Combine(_folder, "Tower.dst");

            CadSheetSetFile file = CadSheetSetFile.CreateFromExample(
                dst,
                "Tower",
                null,
                example,
                false,
                CounterFrom(0xB0000000)
            );

            var ids = file.Document.Descendants().Select(e => (string)e.Attribute("ID")).Where(i => i != null).ToList();
            var exampleIds = CadSheetSetFile
                .Open(example)
                .Document.Descendants()
                .Select(e => (string)e.Attribute("ID"))
                .Where(i => i != null)
                .ToList();
            Assert.Equal(ids.Count, ids.Distinct().Count());
            Assert.Empty(ids.Intersect(exampleIds));
            Assert.NotEqual(CadSheetSetFile.Open(example).DbFingerPrint, file.DbFingerPrint);
            Assert.All(ids, id => Assert.StartsWith("gB", id));
        }

        [Fact]
        public void ASetFromAnExample_KeepsTheCustomPropertyDefinitions_ButNoSheetHasValues()
        {
            string example = Example(out _);
            string dst = Path.Combine(_folder, "Tower.dst");

            CadSheetSet set = CadSheetSetFile
                .CreateFromExample(dst, "Tower", null, example, false, CounterFrom(0xB0000000))
                .SheetSet;

            Assert.Equal(
                new[] { "Client=Acme/Set", "Drawn By=NT/Sheet" },
                set.CustomProperties.Select(p => p.Name + "=" + p.Value + "/" + p.Flags)
            );
        }

        [Fact]
        public void TheFilesOfTheExample_AreNamedAgainRelativeToTheNewSet_AndItsOwnFolderBecomesTheNewOne()
        {
            string example = Example(out string dwt);
            string folder = Path.Combine(_folder, "elsewhere", "new");
            Directory.CreateDirectory(folder);
            string dst = Path.Combine(folder, "Tower.dst");

            CadSheetSet set = CadSheetSetFile
                .CreateFromExample(dst, "Tower", null, example, false, CounterFrom(0xB0000000))
                .SheetSet;

            Assert.True(Same(folder, set.NewSheetLocation.Resolve(folder)));
            Assert.Equal(".", set.NewSheetLocation.RelativeFileName);
            Assert.True(Same(folder, set.Subsets[0].NewSheetLocation.Resolve(folder)));
            Assert.True(Same(dwt, set.Template.File.Resolve(folder)));
            Assert.Equal(@"..\..\example\tpl.dwt", set.Template.File.RelativeFileName);
            Assert.Equal("A1 sheet", set.Template.Name);
            Assert.Equal(SheetSetTestDrawings.HandleOf(dwt, "A1 sheet"), set.Template.Handle);
            Assert.True(Same(dwt, set.PageSetupOverrides.Resolve(folder)));
            Assert.True(
                Same(Path.Combine(_folder, "example", "Models"), set.ResourceLocations.Single().Resolve(folder))
            );
            Assert.True(Same(dwt, set.Subsets[0].Template.File.Resolve(folder)));
        }

        [Fact]
        public void ANewSheetInASetFromAnExample_UsesTheExamplesTemplateAndTheNewFolder()
        {
            string example = Example(out _);
            string folder = Path.Combine(_folder, "new");
            Directory.CreateDirectory(folder);
            string dst = Path.Combine(folder, "Tower.dst");
            CadSheetSetFile file = CadSheetSetFile.CreateFromExample(
                dst,
                "Tower",
                null,
                example,
                false,
                CounterFrom(0xB0000000)
            );
            file.Clock = () => Noon;

            file.Change(set => set.Subsets[0].NewSheet("A-101", "Ground floor"));

            string dwg = Path.Combine(folder, "A-101 Ground floor.dwg");
            Assert.Equal(new[] { "A-101 Ground floor" }, SheetSetTestDrawings.LayoutNames(dwg));
            Assert.Equal(1, SheetSetTestDrawings.Count<DxfLine>(dwg, "A-101 Ground floor"));
            Assert.True(Same(dst, CadSheetSetHint.Read(dwg).SheetSetFileName));
            Assert.Equal("NT", file.SheetSet.Subsets[0].Sheets.Single().FindCustomProperty("Drawn By").Value);
        }

        [Fact]
        public void WithAFolderHierarchy_EachSubsetGetsAFolderInsideItsParents()
        {
            string example = Example(out _);
            string folder = Path.Combine(_folder, "new");
            Directory.CreateDirectory(folder);
            string dst = Path.Combine(folder, "Tower.dst");

            CadSheetSet set = CadSheetSetFile
                .CreateFromExample(dst, "Tower", null, example, true, CounterFrom(0xB0000000))
                .SheetSet;

            Assert.True(Directory.Exists(Path.Combine(folder, "Plans")));
            Assert.True(Directory.Exists(Path.Combine(folder, "Plans", "Details")));
            Assert.True(Directory.Exists(Path.Combine(folder, "Structural")));
            Assert.True(Same(Path.Combine(folder, "Plans"), set.Subsets[0].NewSheetLocation.Resolve(folder)));
            Assert.True(
                Same(
                    Path.Combine(folder, "Plans", "Details"),
                    set.Subsets[0].Subsets[0].NewSheetLocation.Resolve(folder)
                )
            );
            Assert.True(Same(Path.Combine(folder, "Structural"), set.Subsets[1].NewSheetLocation.Resolve(folder)));
            Assert.True(Same(folder, set.NewSheetLocation.Resolve(folder)));
        }

        [Fact]
        public void AnExampleThatIsNotThere_OrIsNotASet_OrAPathThatIsTaken_IsRefused()
        {
            string example = Example(out string dwt);
            string dst = Path.Combine(_folder, "Tower.dst");

            Assert.Throws<FileNotFoundException>(() =>
                CadSheetSetFile.CreateFromExample(dst, "Tower", null, Path.Combine(_folder, "none.dst"), false)
            );
            Assert.Throws<FormatException>(() => CadSheetSetFile.CreateFromExample(dst, "Tower", null, dwt, false));
            Assert.Throws<ArgumentNullException>(() =>
                CadSheetSetFile.CreateFromExample(dst, "Tower", null, null, false)
            );
            CadSheetSetFile.CreateFromExample(dst, "Tower", null, example, false);
            Assert.Throws<IOException>(() => CadSheetSetFile.CreateFromExample(dst, "Again", null, example, false));
            Assert.Equal("Tower", CadSheetSetFile.Open(dst).SheetSet.Name);
        }

        [Fact]
        public void AnEmptySetAsTheExample_GivesAnEmptySet()
        {
            string blank = Path.Combine(_folder, "Blank.dst");
            CadSheetSetFile.Create(blank, "New Sheet Set", null, SheetSetFileTests.Counter());
            string dst = Path.Combine(_folder, "Tower.dst");

            CadSheetSet set = CadSheetSetFile.CreateFromExample(dst, "Tower", "x", blank, false).SheetSet;

            Assert.Equal("Tower", set.Name);
            Assert.Empty(set.Children);
            Assert.True(set.PromptForTemplate);
            Assert.True(Same(_folder, set.NewSheetLocation.Resolve(_folder)));
        }

        // ----- From existing drawings

        private List<CadSheetImportSource> Drawings(out string root)
        {
            root = Path.Combine(_folder, "drawings");
            string cover = SheetSetTestDrawings.Write(root, "cover.dwg", "Cover");
            string ground = SheetSetTestDrawings.Write(Path.Combine(root, "Arch"), "ground.dwg", "Ground", "First");
            string joint = SheetSetTestDrawings.Write(Path.Combine(root, "Arch", "Details"), "joint.dwg", "Joint");
            return new List<CadSheetImportSource>
            {
                new CadSheetImportSource(root, cover, new[] { "Cover" }),
                new CadSheetImportSource(root, ground, new[] { "First", "Ground" }),
                new CadSheetImportSource(root, joint, new[] { "Joint" }),
            };
        }

        private static string Tree(CadSheetSubset parent, string indent = "")
        {
            var lines = new List<string>();
            foreach (CadSheetComponent child in parent.Children)
            {
                if (child is CadSheetSubset subset)
                {
                    lines.Add(indent + "[" + subset.Name + "]");
                    lines.Add(Tree(subset, indent + "  "));
                }
                else
                {
                    lines.Add(indent + ((CadSheetSetSheet)child).Title);
                }
            }

            return string.Join("|", lines.Where(l => l.Length > 0));
        }

        [Fact]
        public void WithNoOptions_EverySheetIsAtTheTopInTheOrderGiven_TitledByItsLayout()
        {
            List<CadSheetImportSource> sources = Drawings(out _);
            string dst = Path.Combine(_folder, "Tower.dst");

            CadSheetSetFile file = CadSheetSetFile.CreateFromDrawings(
                dst,
                "Tower",
                "From drawings",
                sources,
                new CadSheetImportOptions(),
                SheetSetFileTests.Counter(),
                () => Noon
            );

            CadSheetSet set = CadSheetSetFile.Open(dst).SheetSet;
            Assert.Equal("Cover|First|Ground|Joint", Tree(set));
            Assert.Equal("From drawings", set.Description);
            Assert.All(set.Sheets, s => Assert.Equal("", s.Number));
            Assert.All(set.Sheets, s => Assert.Equal(CadSheetLayoutStatus.FoundByHandle, s.FindLayout().Status));
            Assert.Equal(file.FileRevision, CadSheetSetFile.Open(dst).FileRevision);
        }

        [Fact]
        public void WithSubsetsFromFoldersAndTheTopFolderIgnored_TheFoldersBelowItAreTheSubsets()
        {
            List<CadSheetImportSource> sources = Drawings(out _);
            string dst = Path.Combine(_folder, "Tower.dst");

            CadSheetSetFile.CreateFromDrawings(
                dst,
                "Tower",
                null,
                sources,
                new CadSheetImportOptions
                {
                    CreateSubsetsFromFolders = true,
                    IgnoreTopLevelFolder = true,
                    PrefixTitleWithFileName = true,
                },
                SheetSetFileTests.Counter(),
                () => Noon
            );

            Assert.Equal(
                "cover - Cover|[Arch]|  ground - First|  ground - Ground|  [Details]|    joint - Joint",
                Tree(CadSheetSetFile.Open(dst).SheetSet)
            );
        }

        [Fact]
        public void WithSubsetsFromFoldersAndTheTopFolderKept_TheTopFolderIsTheFirstSubset()
        {
            List<CadSheetImportSource> sources = Drawings(out _);
            string dst = Path.Combine(_folder, "Tower.dst");

            CadSheetSetFile.CreateFromDrawings(
                dst,
                "Tower",
                null,
                sources,
                new CadSheetImportOptions { CreateSubsetsFromFolders = true },
                SheetSetFileTests.Counter(),
                () => Noon
            );

            Assert.Equal(
                "[drawings]|  Cover|  [Arch]|    First|    Ground|    [Details]|      Joint",
                Tree(CadSheetSetFile.Open(dst).SheetSet)
            );
        }

        [Fact]
        public void ASourceWithNoRootFolder_GoesToTheTopWhateverTheOptions()
        {
            string loose = SheetSetTestDrawings.Write(Path.Combine(_folder, "loose"), "loose.dwg", "Loose");
            string dst = Path.Combine(_folder, "Tower.dst");

            CadSheetSetFile.CreateFromDrawings(
                dst,
                "Tower",
                null,
                new[] { new CadSheetImportSource(null, loose, new[] { "Loose" }) },
                new CadSheetImportOptions { CreateSubsetsFromFolders = true },
                SheetSetFileTests.Counter(),
                () => Noon
            );

            Assert.Equal("Loose", Tree(CadSheetSetFile.Open(dst).SheetSet));
        }

        [Fact]
        public void EveryDrawingImported_CarriesTheMarkOfTheNewSet()
        {
            List<CadSheetImportSource> sources = Drawings(out _);
            string dst = Path.Combine(_folder, "Tower.dst");

            CadSheetSetFile.CreateFromDrawings(
                dst,
                "Tower",
                null,
                sources,
                new CadSheetImportOptions(),
                SheetSetFileTests.Counter(),
                () => Noon
            );

            foreach (CadSheetImportSource source in sources)
            {
                CadSheetSetHint hint = CadSheetSetHint.Read(source.DrawingFile);
                Assert.True(Same(dst, hint.SheetSetFileName));
                Assert.Equal(source.LayoutNames.Last(), hint.LayoutName);
                Assert.Equal((2, 1, "2026/11/26 14:30:05.123"), (hint.Version, hint.UpdateCount, hint.UpdateTime));
            }
        }

        [Fact]
        public void ARefusedLayout_LeavesNoSetBehind()
        {
            string root = Path.Combine(_folder, "drawings");
            string good = SheetSetTestDrawings.Write(root, "good.dwg", "Good");
            string bad = SheetSetTestDrawings.Write(root, "bad.dwg", "Level #2");
            string dst = Path.Combine(_folder, "Tower.dst");

            Assert.Throws<InvalidOperationException>(() =>
                CadSheetSetFile.CreateFromDrawings(
                    dst,
                    "Tower",
                    null,
                    new[]
                    {
                        new CadSheetImportSource(root, good, new[] { "Good" }),
                        new CadSheetImportSource(root, bad, new[] { "Level #2" }),
                    },
                    new CadSheetImportOptions(),
                    SheetSetFileTests.Counter(),
                    () => Noon
                )
            );

            Assert.False(File.Exists(dst));
            Assert.Empty(Directory.GetFiles(_folder, "*.tmp"));
            Assert.Throws<IOException>(() =>
            {
                File.WriteAllText(dst, "taken");
                CadSheetSetFile.CreateFromDrawings(dst, "Tower", null, new CadSheetImportSource[0], null);
            });
            Assert.Equal("taken", File.ReadAllText(dst));
        }

        [Fact]
        public void TheImportSource_RefusesWhatItCannotHold()
        {
            Assert.Throws<ArgumentNullException>(() => new CadSheetImportSource("r", null, new[] { "a" }));
            Assert.Throws<ArgumentNullException>(() => new CadSheetImportSource("r", "a.dwg", null));
            Assert.Throws<ArgumentException>(() => new CadSheetImportSource("r", " ", new[] { "a" }));
            Assert.Throws<ArgumentException>(() => new CadSheetImportSource("r", "a.dwg", new[] { "a", "" }));
            Assert.Null(new CadSheetImportSource(" ", "a.dwg", new string[0]).RootFolder);
        }

        // ----- Custom properties (D10)

        private CadSheetSetFile TowerWithSheets()
        {
            CadSheetSetFile file = CadSheetSetFile.Create(
                Path.Combine(_folder, "Tower.dst"),
                "Tower",
                null,
                SheetSetFileTests.Counter()
            );
            file.Change(set =>
            {
                set.AddSheet("G-001", "Cover", null);
                CadSheetSubset plans = set.AddSubset("Plans");
                plans.AddSheet("A-101", "Ground", null);
                plans.AddSubset("Details").AddSheet("D-1", "Joint", null);
            });
            return file;
        }

        [Fact]
        public void ASheetProperty_IsDefinedInTheSetAndGivenAtItsDefaultToEverySheetInOrOutOfSubsets()
        {
            CadSheetSetFile file = TowerWithSheets();

            file.Change(set => set.AddCustomProperty("Drawn By", "NT", CadSheetSetPropertyFlags.Sheet));

            CadSheetSet set2 = CadSheetSetFile.Open(file.Path).SheetSet;
            CadSheetSetProperty definition = set2.FindCustomProperty("Drawn By");
            Assert.Equal(("NT", CadSheetSetPropertyFlags.Sheet), (definition.Value, definition.Flags));
            Assert.Equal(3, set2.AllSheets().Count());
            Assert.All(set2.AllSheets(), s =>
            {
                CadSheetSetProperty p = s.FindCustomProperty("Drawn By");
                Assert.Equal(("NT", CadSheetSetPropertyFlags.Sheet), (p.Value, p.Flags));
            });
            Assert.Null(set2.Subsets[0].FindCustomProperty("Drawn By"));
        }

        [Fact]
        public void ASetProperty_IsHeldByTheSetAlone()
        {
            CadSheetSetFile file = TowerWithSheets();

            file.Change(set => set.AddCustomProperty("Client", "Acme", CadSheetSetPropertyFlags.Set));

            Assert.Equal(
                ("Acme", CadSheetSetPropertyFlags.Set),
                (file.SheetSet.FindCustomProperty("Client").Value, file.SheetSet.FindCustomProperty("Client").Flags)
            );
            Assert.All(file.SheetSet.AllSheets(), s => Assert.Empty(s.CustomProperties));
        }

        [Fact]
        public void APropertyAddedLater_LeavesTheValuesSheetsHaveAlreadyChanged()
        {
            CadSheetSetFile file = TowerWithSheets();
            file.Change(set => set.AddCustomProperty("Drawn By", "NT", CadSheetSetPropertyFlags.Sheet));
            file.Change(set => set.Sheets[0].SetCustomProperty("Drawn By", "AB", CadSheetSetPropertyFlags.Sheet));

            file.Change(set => set.AddCustomProperty("Checked By", "JD", CadSheetSetPropertyFlags.Sheet));

            Assert.Equal("AB", file.SheetSet.Sheets[0].FindCustomProperty("Drawn By").Value);
            Assert.Equal("NT", file.SheetSet.Subsets[0].Sheets[0].FindCustomProperty("Drawn By").Value);
            Assert.All(file.SheetSet.AllSheets(), s => Assert.Equal("JD", s.FindCustomProperty("Checked By").Value));
            Assert.Equal(new[] { "Checked By", "Drawn By" }, file.SheetSet.CustomProperties.Select(p => p.Name));
        }

        [Fact]
        public void ANameThatIsAPropertysAlready_OrIsEmptyOrHoldsADotOrAQuote_IsRefused_AndNothingChanges()
        {
            CadSheetSetFile file = TowerWithSheets();
            file.Change(set => set.AddCustomProperty("Drawn By", "NT", CadSheetSetPropertyFlags.Sheet));
            byte[] bytes = File.ReadAllBytes(file.Path);

            Assert.Throws<ArgumentException>(() =>
                file.Change(set => set.AddCustomProperty("DRAWN by", "x", CadSheetSetPropertyFlags.Set))
            );
            Assert.Throws<ArgumentException>(() =>
                file.Change(set => set.AddCustomProperty("", "x", CadSheetSetPropertyFlags.Set))
            );
            Assert.Throws<ArgumentException>(() =>
                file.Change(set => set.AddCustomProperty("A.B", "x", CadSheetSetPropertyFlags.Set))
            );
            Assert.Throws<ArgumentException>(() =>
                file.Change(set => set.AddCustomProperty("A\"B", "x", CadSheetSetPropertyFlags.Set))
            );
            Assert.Throws<ArgumentException>(() =>
                file.Change(set => set.AddCustomProperty("Other", "x", CadSheetSetPropertyFlags.None))
            );
            Assert.Throws<ArgumentException>(() =>
                file.Change(set =>
                    set.AddCustomProperty("Other", "x", CadSheetSetPropertyFlags.Set | CadSheetSetPropertyFlags.Sheet)
                )
            );
            Assert.Throws<ArgumentNullException>(() =>
                file.Change(set => set.AddCustomProperty(null, "x", CadSheetSetPropertyFlags.Set))
            );

            Assert.Equal(bytes, File.ReadAllBytes(file.Path));
        }

        [Fact]
        public void DeletingAProperty_TakesItOutOfTheSetAndOfEverySheet()
        {
            CadSheetSetFile file = TowerWithSheets();
            file.Change(set =>
            {
                set.AddCustomProperty("Drawn By", "NT", CadSheetSetPropertyFlags.Sheet);
                set.AddCustomProperty("Client", "Acme", CadSheetSetPropertyFlags.Set);
            });
            bool first = false;
            bool again = true;

            file.Change(set =>
            {
                first = set.DeleteCustomProperty("drawn by");
                again = set.DeleteCustomProperty("Drawn By");
            });

            Assert.True(first);
            Assert.False(again);
            Assert.Equal(new[] { "Client" }, file.SheetSet.CustomProperties.Select(p => p.Name));
            Assert.All(file.SheetSet.AllSheets(), s => Assert.Empty(s.CustomProperties));
            Assert.Throws<ArgumentNullException>(() => file.Change(set => set.DeleteCustomProperty(null)));
        }

        // ----- Resource locations and the page setup overrides file

        [Fact]
        public void ResourceLocations_KeepTheOrderAreSavedAndFollowAMovedFolder()
        {
            string set = Path.Combine(_folder, "set");
            Directory.CreateDirectory(Path.Combine(set, "Models"));
            Directory.CreateDirectory(Path.Combine(set, "Details"));
            CadSheetSetFile file = CadSheetSetFile.Create(
                Path.Combine(set, "Tower.dst"),
                "Tower",
                null,
                SheetSetFileTests.Counter()
            );
            file.Change(s =>
            {
                s.AddResourceLocation(CadFileReference.FromPath(Path.Combine(set, "Models"), set));
                s.AddResourceLocation(CadFileReference.FromPath(Path.Combine(set, "Details"), set));
            });
            Assert.Equal(
                new[] { @".\Models", @".\Details" },
                CadSheetSetFile.Open(file.Path).SheetSet.ResourceLocations.Select(r => r.RelativeFileName)
            );

            file.Change(s => s.RemoveResourceLocationAt(0));
            string moved = Path.Combine(_folder, "moved");
            Directory.Move(set, moved);

            CadFileReference left = CadSheetSetFile
                .Open(Path.Combine(moved, "Tower.dst"))
                .SheetSet.ResourceLocations.Single();
            Assert.True(Same(Path.Combine(moved, "Details"), left.Resolve(moved)));
        }

        [Fact]
        public void ThePageSetupOverridesFile_ListsItsPageSetupsAndIsEmptyWhereThereIsNoneOrNoFile()
        {
            CadDocument setups = CadDocument.From(SheetSetTestDrawings.Build("Ground"), "Setups.dwt");
            CadPlotSettings settings = CadPlotSettings.Read(setups, setups.ModelSpace);
            Assert.True(settings.SaveAsPageSetup(setups, "A3 plan").Succeeded);
            Assert.True(settings.SaveAsPageSetup(setups, "A1 plan").Succeeded);
            string dwt = Path.Combine(_folder, "Setups.dwt");
            setups.SaveAs(dwt);
            CadSheetSetFile file = CadSheetSetFile.Create(
                Path.Combine(_folder, "Tower.dst"),
                "Tower",
                null,
                SheetSetFileTests.Counter()
            );
            Assert.Empty(file.SheetSet.PageSetupOverrideNames());

            file.Change(set => set.PageSetupOverrides = CadFileReference.FromPath(dwt, _folder));
            Assert.Equal(
                new[] { "A1 plan", "A3 plan" },
                file.SheetSet.PageSetupOverrideNames().OrderBy(n => n, StringComparer.Ordinal)
            );
            Assert.Equal(2, file.SheetSet.PageSetupOverrideNames().Count);

            File.Delete(dwt);
            Assert.Empty(file.SheetSet.PageSetupOverrideNames());
        }
    }
}
