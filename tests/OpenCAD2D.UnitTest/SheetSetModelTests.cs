using System;
using System.IO;
using System.Linq;
using OpenCAD2D.SheetSets;
using Xunit;

namespace OpenCAD2D.UnitTest
{
    /// <summary>
    /// The sheet set model over the file: subsets and sheets in set order, a sheet's drawing and layout, custom properties
    /// by owner, resource locations, the sheet creation template and the page setup overrides file, view categories and
    /// sheet views. Every set is built in code in a temporary folder.
    /// </summary>
    public class SheetSetModelTests : IDisposable
    {
        private readonly string _folder = Path.Combine(
            Path.GetTempPath(),
            "gk",
            "sheetsetmodel-" + Guid.NewGuid().ToString("N")
        );

        public SheetSetModelTests()
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

        private CadSheetSetFile Empty() =>
            CadSheetSetFile.Create(Path.Combine(_folder, "set.dst"), "Tower", null, SheetSetFileTests.Counter());

        private static string[] Labels(CadSheetSubset parent) =>
            parent
                .Children.Select(c => c is CadSheetSubset s ? "[" + s.Name + "]" : ((CadSheetSetSheet)c).Number)
                .ToArray();

        [Fact]
        public void SubsetsAndSheets_KeepTheOrderTheyWereAdded_AndAnIndexPlacesOneBeforeAnother()
        {
            CadSheetSetFile file = Empty();

            file.Change(set =>
            {
                set.AddSheet("G-001", "Cover", null);
                set.AddSubset("Plans");
                set.AddSheet("S-101", "Frame", null);
                set.AddSheet("A-001", "Index", null, 1);
                set.AddSubset("Details", null, 0);
            });

            Assert.Equal(new[] { "[Details]", "G-001", "A-001", "[Plans]", "S-101" }, Labels(file.SheetSet));
            Assert.Equal(new[] { "[Details]", "[Plans]" }, file.SheetSet.Subsets.Select(s => "[" + s.Name + "]"));
            Assert.Equal(new[] { "G-001", "A-001", "S-101" }, file.SheetSet.Sheets.Select(s => s.Number));
        }

        [Fact]
        public void TheOrderOnDisk_IsTheOrderTheModelReads()
        {
            CadSheetSetFile file = Empty();
            file.Change(set =>
            {
                CadSheetSubset plans = set.AddSubset("Plans");
                plans.AddSheet("A-101", "Ground", null);
                plans.AddSheet("A-102", "First", null);
                set.AddSheet("G-001", "Cover", null, 0);
            });

            CadSheetSetFile reopened = CadSheetSetFile.Open(file.Path);

            Assert.Equal(new[] { "G-001", "[Plans]" }, Labels(reopened.SheetSet));
            Assert.Equal(new[] { "G-001", "A-101", "A-102" }, reopened.SheetSet.AllSheets().Select(s => s.Number));
        }

        [Fact]
        public void AllSheets_WalksTheSetDepthFirstInSetOrder()
        {
            CadSheetSetFile file = Empty();
            file.Change(set =>
            {
                set.AddSheet("1", "one", null);
                CadSheetSubset a = set.AddSubset("A");
                a.AddSheet("2", "two", null);
                CadSheetSubset b = a.AddSubset("B");
                b.AddSheet("3", "three", null);
                a.AddSheet("4", "four", null);
                set.AddSheet("5", "five", null);
            });

            Assert.Equal(new[] { "1", "2", "3", "4", "5" }, file.SheetSet.AllSheets().Select(s => s.Number));
        }

        [Fact]
        public void ASheetOrSubset_IsFoundByItsId_AndTellsItsParent()
        {
            CadSheetSetFile file = Empty();
            string sheetId = null;
            string subsetId = null;
            file.Change(set =>
            {
                CadSheetSubset plans = set.AddSubset("Plans");
                subsetId = plans.Id;
                sheetId = plans.AddSheet("A-101", "Ground", null).Id;
            });

            CadSheetComponent sheet = file.SheetSet.Find(sheetId);

            Assert.Equal("A-101", ((CadSheetSetSheet)sheet).Number);
            Assert.Equal(subsetId, sheet.Parent.Id);
            Assert.Null(file.SheetSet.Parent);
            Assert.Null(file.SheetSet.Find("gNOSUCH"));
        }

        [Fact]
        public void Remove_TakesASheetOrASubsetWithItsContents_OutOfTheSet()
        {
            CadSheetSetFile file = Empty();
            file.Change(set =>
            {
                CadSheetSubset plans = set.AddSubset("Plans");
                plans.AddSheet("A-101", "Ground", null);
                set.AddSheet("G-001", "Cover", null);
            });

            file.Change(set => set.Remove(set.Subsets.Single()));

            Assert.Equal(new[] { "G-001" }, Labels(file.SheetSet));
            Assert.Single(file.SheetSet.AllSheets());
        }

        [Fact]
        public void Remove_OfAnythingNotInTheSet_IsRefused()
        {
            CadSheetSetFile file = Empty();
            CadSheetSetFile other = CadSheetSetFile.Create(
                Path.Combine(_folder, "other.dst"),
                "Other",
                null,
                SheetSetFileTests.Counter()
            );
            CadSheetSetSheet foreign = null;
            other.Change(set => foreign = set.AddSheet("X", "x", null));

            Assert.Throws<ArgumentException>(() => file.Change(set => set.Remove(foreign)));
        }

        [Fact]
        public void Move_ChangesTheOrderAndTheParent_AndRefusesAMoveIntoItself()
        {
            CadSheetSetFile file = Empty();
            file.Change(set =>
            {
                CadSheetSubset plans = set.AddSubset("Plans");
                CadSheetSubset details = set.AddSubset("Details");
                plans.AddSheet("A-101", "Ground", null);
                plans.AddSheet("A-102", "First", null);
                set.AddSheet("G-001", "Cover", null);
            });

            file.Change(set =>
            {
                CadSheetSubset plans = set.Subsets.First(s => s.Name == "Plans");
                CadSheetSubset details = set.Subsets.First(s => s.Name == "Details");
                set.Move(plans.Sheets[1], details, 0);
                set.Move(set.Sheets.Single(), plans, 0);
            });

            Assert.Equal(new[] { "[Plans]", "[Details]" }, Labels(file.SheetSet));
            CadSheetSubset plansAfter = file.SheetSet.Subsets[0];
            Assert.Equal(new[] { "G-001", "A-101" }, Labels(plansAfter));
            Assert.Equal(new[] { "A-102" }, Labels(file.SheetSet.Subsets[1]));
            Assert.Equal("Details", file.SheetSet.Find(file.SheetSet.Subsets[1].Sheets[0].Id).Parent.Name);

            Assert.Throws<ArgumentException>(() => file.Change(set => set.Move(set.Subsets[0], set.Subsets[0], 0)));
            Assert.Throws<ArgumentException>(() =>
                file.Change(set =>
                {
                    CadSheetSubset outer = set.AddSubset("Outer");
                    CadSheetSubset inner = outer.AddSubset("Inner");
                    set.Move(outer, inner, 0);
                })
            );
        }

        [Fact]
        public void AnIndexOutOfRange_IsRefused()
        {
            CadSheetSetFile file = Empty();

            Assert.Throws<ArgumentOutOfRangeException>(() => file.Change(set => set.AddSheet("A", "a", null, 5)));
            Assert.Throws<ArgumentOutOfRangeException>(() => file.Change(set => set.AddSubset("A", null, -2)));
        }

        [Fact]
        public void ASheet_NamesItsDrawingAndLayout_InFourFormsOfThePath()
        {
            CadSheetSetFile file = Empty();
            string dwg = Path.Combine(_folder, "Sheets", "A-101.dwg");
            file.Change(set =>
                set.AddSheet(
                    "A-101",
                    "Ground floor",
                    new CadDrawingReference(CadFileReference.FromPath(dwg, file.Folder), "GROUND", "1A2B", "1B")
                )
            );

            CadSheetSetSheet sheet = CadSheetSetFile.Open(file.Path).SheetSet.Sheets.Single();

            Assert.Equal("GROUND", sheet.Layout.Name);
            Assert.Equal("1A2B", sheet.Layout.Handle);
            Assert.Equal("1B", sheet.Layout.OwnerHandle);
            Assert.Equal(dwg, sheet.Layout.File.FileName);
            Assert.Equal(@".\Sheets\A-101.dwg", sheet.Layout.File.RelativeFileName);
            Assert.Null(sheet.Layout.File.EnvironFileName);
            Assert.Null(sheet.Layout.File.SpecialFolderFileName);
        }

        [Fact]
        public void ASheetWithNoLayout_ReadsNull_AndALayoutCanBeSetAndCleared()
        {
            CadSheetSetFile file = Empty();
            file.Change(set => set.AddSheet("A-101", "Ground", null));
            Assert.Null(file.SheetSet.Sheets.Single().Layout);
            string dwg = Path.Combine(_folder, "A-101.dwg");

            file.Change(set =>
                set.Sheets.Single().Layout = new CadDrawingReference(
                    CadFileReference.FromPath(dwg, file.Folder),
                    "L1",
                    "30",
                    "2"
                )
            );
            Assert.Equal("L1", file.SheetSet.Sheets.Single().Layout.Name);

            file.Change(set => set.Sheets.Single().Layout = null);
            Assert.Null(file.SheetSet.Sheets.Single().Layout);
        }

        [Fact]
        public void TheFourFormsOfAFileReferenceAreKeptAsTheyWereWritten_WhenAnotherPartOfTheSheetChanges()
        {
            CadSheetSetFile file = Empty();
            var reference = new CadFileReference(
                @"C:\Old\A-101.dwg",
                @".\A-101.dwg",
                @"%USERPROFILE%\A-101.dwg",
                @"$(CSIDL_PERSONAL)\A-101.dwg"
            );
            file.Change(set => set.AddSheet("A-101", "Ground", new CadDrawingReference(reference, "L1", "30", "2")));

            file.Change(set => set.Sheets.Single().Title = "Ground floor");

            CadFileReference read = file.SheetSet.Sheets.Single().Layout.File;
            Assert.Equal(reference.FileName, read.FileName);
            Assert.Equal(reference.RelativeFileName, read.RelativeFileName);
            Assert.Equal(reference.EnvironFileName, read.EnvironFileName);
            Assert.Equal(reference.SpecialFolderFileName, read.SpecialFolderFileName);
        }

        [Fact]
        public void CustomProperties_AreKeptByOwner_AndFoundByNameLetterCaseIgnored()
        {
            CadSheetSetFile file = Empty();

            file.Change(set =>
            {
                set.SetCustomProperty("Client", "Acme", CadSheetSetPropertyFlags.Set);
                set.SetCustomProperty("Drawn By", "NT", CadSheetSetPropertyFlags.Sheet);
                CadSheetSetSheet sheet = set.AddSheet("A-101", "Ground", null);
                sheet.SetCustomProperty("Drawn By", "TN", CadSheetSetPropertyFlags.Sheet);
            });

            CadSheetSet set2 = file.SheetSet;
            CadSheetSetProperty client = set2.FindCustomProperty("CLIENT");
            Assert.Equal("Client", client.Name);
            Assert.Equal("Acme", client.Value);
            Assert.True(client.IsSetProperty);
            Assert.False(client.IsSheetProperty);
            CadSheetSetProperty drawn = set2.FindCustomProperty("drawn by");
            Assert.True(drawn.IsSheetProperty);
            Assert.Equal("NT", drawn.Value);
            Assert.Equal("TN", set2.Sheets.Single().FindCustomProperty("Drawn By").Value);
            Assert.Null(set2.FindCustomProperty("none"));
            Assert.Equal(new[] { "Client", "Drawn By" }, set2.CustomProperties.Select(p => p.Name));
        }

        [Fact]
        public void SettingACustomPropertyAgain_ReplacesItsValueInPlace_AndKeepsItsIdAndFlags()
        {
            CadSheetSetFile file = Empty();
            file.Change(set =>
            {
                set.SetCustomProperty("Client", "Acme", CadSheetSetPropertyFlags.Set);
                set.SetCustomProperty("Client", "Beta", CadSheetSetPropertyFlags.Set);
            });

            Assert.Single(file.SheetSet.CustomProperties);
            Assert.Equal("Beta", file.SheetSet.FindCustomProperty("client").Value);
            Assert.Single(file.Document.Descendants("AcSmCustomPropertyValue"));
        }

        [Fact]
        public void TheFlagBitsAProgramDoesNotKnow_AreKept_AsTheTypeLibraryNamesThem()
        {
            // The type library of AcSmComponents names 1 set, 2 sheet, 4 subset and 8 child (the 3a probe of R5).
            Assert.Equal(1, (int)CadSheetSetPropertyFlags.Set);
            Assert.Equal(2, (int)CadSheetSetPropertyFlags.Sheet);
            Assert.Equal(4, (int)CadSheetSetPropertyFlags.Subset);
            Assert.Equal(8, (int)CadSheetSetPropertyFlags.Child);
            CadSheetSetFile file = Empty();
            file.Change(set =>
                set.SetCustomProperty("Odd", "x", CadSheetSetPropertyFlags.Sheet | CadSheetSetPropertyFlags.Child)
            );

            CadSheetSetProperty read = CadSheetSetFile.Open(file.Path).SheetSet.FindCustomProperty("Odd");

            Assert.Equal(10, (int)read.Flags);
            Assert.True(read.IsSheetProperty);
        }

        [Fact]
        public void RemoveCustomProperty_TakesItOut_AndSaysWhetherItWasThere()
        {
            CadSheetSetFile file = Empty();
            file.Change(set => set.SetCustomProperty("Client", "Acme", CadSheetSetPropertyFlags.Set));
            bool first = false;
            bool second = true;

            file.Change(set =>
            {
                first = set.RemoveCustomProperty("client");
                second = set.RemoveCustomProperty("client");
            });

            Assert.True(first);
            Assert.False(second);
            Assert.Empty(file.SheetSet.CustomProperties);
        }

        [Fact]
        public void AnEntryOfTheBagThatIsNotAFlagAndValuePair_IsKeptAndNotListed()
        {
            CadSheetSetFile file = Empty();
            file.Change(set => set.SetCustomProperty("Client", "Acme", CadSheetSetPropertyFlags.Set));
            // AutoCAD keeps the transmittal setups of the set in its custom property bag, as an object with no Flags or Value.
            var odd = new System.Xml.Linq.XElement(
                "SSTransmittalSetups.16",
                new System.Xml.Linq.XAttribute("propname", "TransmittalSetups"),
                new System.Xml.Linq.XAttribute("vt", "13")
            );
            file.Document.Root.Element("AcSmSheetSet").Element("AcSmCustomPropertyBag").Add(odd);

            file.Change(set => set.SetCustomProperty("Another", "x", CadSheetSetPropertyFlags.Set));

            Assert.Equal(new[] { "Another", "Client" }, file.SheetSet.CustomProperties.Select(p => p.Name));
            Assert.Contains("SSTransmittalSetups.16", CadDstCodec.Decode(File.ReadAllBytes(file.Path)));
        }

        [Fact]
        public void ASheetsFlags_AreItsIncludeForPublishInverted_AndItsOtherFieldsRoundTrip()
        {
            CadSheetSetFile file = Empty();
            file.Change(set =>
            {
                CadSheetSetSheet sheet = set.AddSheet("A-101", "Ground", null);
                sheet.IncludeForPublish = false;
                sheet.Description = "Plan";
                sheet.RevisionNumber = "C";
                sheet.RevisionDate = "2026-11-26";
                sheet.IssuePurpose = "Tender";
                sheet.Category = "Plans";
            });

            CadSheetSetSheet read = CadSheetSetFile.Open(file.Path).SheetSet.Sheets.Single();

            Assert.False(read.IncludeForPublish);
            Assert.Equal("Plan", read.Description);
            Assert.Equal("C", read.RevisionNumber);
            Assert.Equal("2026-11-26", read.RevisionDate);
            Assert.Equal("Tender", read.IssuePurpose);
            Assert.Equal("Plans", read.Category);
            Assert.Contains("DoNotPlot", CadDstCodec.Decode(File.ReadAllBytes(file.Path)));
        }

        [Fact]
        public void TheSheetCreationTemplate_TheNewSheetFolderAndThePromptFlag_BelongToASubsetAsToTheSet()
        {
            CadSheetSetFile file = Empty();
            string dwt = Path.Combine(_folder, "T.dwt");
            string sheets = Path.Combine(_folder, "Plans");
            file.Change(set =>
            {
                CadSheetSubset plans = set.AddSubset("Plans");
                plans.Template = new CadDrawingReference(CadFileReference.FromPath(dwt, file.Folder), "A1", "2B", null);
                plans.NewSheetLocation = CadFileReference.FromPath(sheets, file.Folder);
                plans.PromptForTemplate = true;
            });

            CadSheetSubset read = CadSheetSetFile.Open(file.Path).SheetSet.Subsets.Single();

            Assert.Equal("A1", read.Template.Name);
            Assert.Equal(dwt, read.Template.File.FileName);
            Assert.Equal(sheets, read.NewSheetLocation.FileName);
            Assert.True(read.PromptForTemplate);
            Assert.False(file.SheetSet.PromptForTemplate && file.SheetSet.Template != null);
        }

        [Fact]
        public void AnEmptyTemplateOfANewSet_ReadsAsNull_AndPromptsForOne()
        {
            CadSheetSetFile file = Empty();

            Assert.Null(file.SheetSet.Template);
            Assert.Null(file.SheetSet.LabelBlock);
            Assert.Null(file.SheetSet.PageSetupOverrides);
            Assert.True(file.SheetSet.PromptForTemplate);
            Assert.Equal(file.Folder, file.SheetSet.NewSheetLocation.FileName);
            Assert.Equal(".", file.SheetSet.NewSheetLocation.RelativeFileName);
        }

        [Fact]
        public void ResourceLocations_AreFolders_KeptInTheOrderAdded_AndRemovedByPlace()
        {
            CadSheetSetFile file = Empty();
            file.Change(set =>
            {
                set.AddResourceLocation(CadFileReference.FromPath(Path.Combine(_folder, "Models"), file.Folder));
                set.AddResourceLocation(CadFileReference.FromPath(Path.Combine(_folder, "Details"), file.Folder));
                set.AddResourceLocation(CadFileReference.FromPath(Path.Combine(_folder, "Site"), file.Folder));
            });

            file.Change(set => set.RemoveResourceLocationAt(1));

            Assert.Equal(
                new[] { @".\Models", @".\Site" },
                CadSheetSetFile.Open(file.Path).SheetSet.ResourceLocations.Select(r => r.RelativeFileName)
            );
            Assert.Throws<ArgumentOutOfRangeException>(() => file.Change(set => set.RemoveResourceLocationAt(2)));
        }

        [Fact]
        public void TheProjectFieldsAndThePageSetupOverrides_RoundTrip_AndClearByNull()
        {
            CadSheetSetFile file = Empty();
            string setups = Path.Combine(_folder, "Setups.dwt");
            file.Change(set =>
            {
                set.ProjectNumber = "T-2026";
                set.ProjectName = "Tower B";
                set.ProjectPhase = "Design";
                set.ProjectMilestone = "50%";
                set.PageSetupOverrides = CadFileReference.FromPath(setups, file.Folder);
            });

            CadSheetSet read = CadSheetSetFile.Open(file.Path).SheetSet;
            Assert.Equal("T-2026", read.ProjectNumber);
            Assert.Equal("Tower B", read.ProjectName);
            Assert.Equal("Design", read.ProjectPhase);
            Assert.Equal("50%", read.ProjectMilestone);
            Assert.Equal(setups, read.PageSetupOverrides.FileName);

            file.Change(set => set.PageSetupOverrides = null);
            Assert.Null(file.SheetSet.PageSetupOverrides);
        }

        [Fact]
        public void ViewCategories_AreListed_AddedAndRemoved_AndASheetViewNamesOne()
        {
            CadSheetSetFile file = Empty();
            string dwg = Path.Combine(_folder, "A-101.dwg");
            CadViewCategory floorPlans = null;
            CadViewCategory sections = null;
            file.Change(set =>
            {
                floorPlans = set.AddViewCategory("Floor Plans", "Plans of the floors");
                sections = set.AddViewCategory("Sections", null);
                CadSheetSetSheet sheet = set.AddSheet("A-101", "Ground", null);
                sheet.AddView(
                    "1",
                    "Ground floor",
                    new CadDrawingReference(CadFileReference.FromPath(dwg, file.Folder), "GROUND", "2C", "6"),
                    floorPlans
                );
                sheet.AddView("2", "Section", null, sections);
            });

            CadSheetSet read = CadSheetSetFile.Open(file.Path).SheetSet;

            Assert.Equal(new[] { "", "Floor Plans", "Sections" }, read.ViewCategories.Select(c => c.Name));
            CadSheetView first = read.Sheets.Single().Views[0];
            Assert.Equal("1", first.Number);
            Assert.Equal("Ground floor", first.Title);
            Assert.Equal("GROUND", first.NamedView.Name);
            Assert.Equal("Floor Plans", first.Category.Name);
            Assert.Equal("Sections", read.Sheets.Single().Views[1].Category.Name);
            Assert.Null(read.Sheets.Single().Views[1].NamedView);
        }

        [Fact]
        public void RemovingAViewCategory_LeavesItsSheetViewsWithNoCategory()
        {
            CadSheetSetFile file = Empty();
            file.Change(set =>
            {
                CadViewCategory category = set.AddViewCategory("Floor Plans", null);
                set.AddSheet("A-101", "Ground", null).AddView("1", "Ground", null, category);
            });

            file.Change(set => set.RemoveViewCategory(set.ViewCategories.Single(c => c.Name == "Floor Plans")));

            CadSheetView view = file.SheetSet.Sheets.Single().Views.Single();
            Assert.Null(view.Category);
            Assert.DoesNotContain("Floor Plans", file.SheetSet.ViewCategories.Select(c => c.Name));
        }

        [Fact]
        public void ASheetView_CanBeRemoved_ByItself()
        {
            CadSheetSetFile file = Empty();
            file.Change(set =>
            {
                CadSheetSetSheet sheet = set.AddSheet("A-101", "Ground", null);
                sheet.AddView("1", "One", null, null);
                sheet.AddView("2", "Two", null, null);
            });

            file.Change(set => set.Sheets.Single().RemoveView(set.Sheets.Single().Views[0]));

            Assert.Equal(new[] { "2" }, file.SheetSet.Sheets.Single().Views.Select(v => v.Number));
        }

        [Fact]
        public void AnArgumentThatIsNull_IsRefused_WhereTheSpecNeedsAValue()
        {
            CadSheetSetFile file = Empty();

            Assert.Throws<ArgumentNullException>(() => file.Change(null));
            Assert.Throws<ArgumentNullException>(() => file.Change(set => set.AddSubset(null)));
            Assert.Throws<ArgumentNullException>(() => file.Change(set => set.AddSheet("A", null, null)));
            Assert.Throws<ArgumentNullException>(() => file.Change(set => set.SetCustomProperty(null, "x", 0)));
            Assert.Throws<ArgumentNullException>(() => file.Change(set => set.AddResourceLocation(null)));
            Assert.Throws<ArgumentNullException>(() => CadSheetSetFile.Open(null));
            Assert.Throws<ArgumentNullException>(() => CadSheetSetFile.Create(null, "x"));
            Assert.Throws<ArgumentNullException>(() => CadSheetSetFile.Create(Path.Combine(_folder, "n.dst"), null));
        }
    }
}
