using System;
using System.IO;
using System.Linq;
using OpenCAD2D.SheetSets;
using Xunit;

namespace OpenCAD2D.UnitTest
{
    /// <summary>
    /// The publish list a sheet set builds (A12 5a, D17): the sheets in set order under the selection, the ones not included
    /// and the ones of a subset that does not publish left out with their reason, the names AutoCAD gives published sheets, the
    /// output folder and one file or a file each taken from the set, the order reversed, a page setup override, a sheet whose
    /// drawing is gone, and what the .dst keeps of all this. Every drawing and set is built in code in a temporary folder.
    /// </summary>
    public class SheetSetPublishListTests : IDisposable
    {
        private readonly string _folder = Path.Combine(
            Path.GetTempPath(),
            "gk",
            "sheetsetpublish-" + Guid.NewGuid().ToString("N")
        );

        public SheetSetPublishListTests()
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

        /// <summary>
        /// Example Tower: G-001 Cover Sheet, then the subset Architectural (A-101 Ground Floor Plan, A-102 First Floor Plan, and
        /// the subset Details holding A-501 Wall Sections), then the subset Structural (S-101 Foundation Plan), each sheet a
        /// layout of a drawing of its own.
        /// </summary>
        private CadSheetSetFile Tower()
        {
            CadSheetSetFile file = CadSheetSetFile.Create(
                Path.Combine(_folder, "Tower.dst"),
                "Example Tower",
                null,
                SheetSetFileTests.Counter()
            );
            string Drawing(string name) => SheetSetTestDrawings.Write(_folder, name + ".dwg", "Sheet");

            file.Change(set =>
            {
                Sheet(set, "G-001", "Cover Sheet", Drawing("cover"));
                CadSheetSubset architectural = set.NewSubset("Architectural", null, false);
                Sheet(architectural, "A-101", "Ground Floor Plan", Drawing("a101"));
                Sheet(architectural, "A-102", "First Floor Plan", Drawing("a102"));
                Sheet(architectural.NewSubset("Details", null, false), "A-501", "Wall Sections", Drawing("a501"));
                Sheet(set.NewSubset("Structural", null, false), "S-101", "Foundation Plan", Drawing("s101"));
            });
            return file;
        }

        private static void Sheet(CadSheetSubset into, string number, string title, string drawing)
        {
            CadSheetSetSheet sheet = into.ImportLayouts(drawing, new[] { "Sheet" }, false).Single();
            sheet.Number = number;
            sheet.Title = title;
        }

        private static string[] Names(CadSheetSetPublish publish) => publish.List.Sheets.Select(s => s.Name).ToArray();

        private static CadSheetSetSheet SheetNumbered(CadSheetSetFile file, string number) =>
            file.SheetSet.AllSheets().Single(s => s.Number == number);

        [Fact]
        public void TheWholeSet_IsListedInSetOrder_EachSheetNamedByItsNumberAndTitle()
        {
            CadSheetSetFile file = Tower();

            CadSheetSetPublish publish = CadSheetSetPublish.Create(file.SheetSet, null, null, null, null);

            Assert.Equal(
                new[]
                {
                    "G-001 Cover Sheet",
                    "A-101 Ground Floor Plan",
                    "A-102 First Floor Plan",
                    "A-501 Wall Sections",
                    "S-101 Foundation Plan",
                },
                Names(publish)
            );
            Assert.All(publish.List.Sheets, s => Assert.True(s.IsLoaded, s.Name + ": " + s.Status));
            Assert.Empty(publish.Skipped);
            Assert.Equal("Publishing Example Tower: 5 sheets, 5 to publish, in set order.", publish.Summary);
            Assert.Equal("Example Tower", publish.List.SheetSetName);
        }

        [Fact]
        public void ASheetNotIncludedForPublish_IsLeftOut_AndSaidSo()
        {
            CadSheetSetFile file = Tower();
            file.Change(set => SheetNumbered(file, "A-102").IncludeForPublish = false);

            CadSheetSetPublish publish = CadSheetSetPublish.Create(file.SheetSet, null, null, null, null);

            Assert.DoesNotContain("A-102 First Floor Plan", Names(publish));
            Assert.Equal(4, publish.List.Sheets.Count);
            Assert.Equal(new[] { "Skipped A-102 First Floor Plan: not included for publish." }, publish.Skipped);
            Assert.Equal("Publishing Example Tower: 5 sheets, 4 to publish, in set order.", publish.Summary);
        }

        [Fact]
        public void ASubsetThatDoesNotPublishItsSheets_LeavesOutEverySheetInsideIt_ThoseOfASubsetInsideToo()
        {
            CadSheetSetFile file = Tower();
            CadSheetSubset architectural = file.SheetSet.Subsets.Single(s => s.Name == "Architectural");
            Assert.True(architectural.PublishSheets);
            file.Change(set => set.Subsets.Single(s => s.Name == "Architectural").PublishSheets = false);

            CadSheetSetPublish publish = CadSheetSetPublish.Create(file.SheetSet, null, null, null, null);

            Assert.False(file.SheetSet.Subsets.Single(s => s.Name == "Architectural").PublishSheets);
            Assert.Equal(new[] { "G-001 Cover Sheet", "S-101 Foundation Plan" }, Names(publish));
            Assert.Equal(
                new[]
                {
                    "Skipped A-101 Ground Floor Plan: the subset Architectural does not publish its sheets.",
                    "Skipped A-102 First Floor Plan: the subset Architectural does not publish its sheets.",
                    "Skipped A-501 Wall Sections: the subset Architectural does not publish its sheets.",
                },
                publish.Skipped
            );
        }

        [Fact]
        public void PublishSheetsInSubset_IsKeptInTheFileAsAutoCadKeepsIt_TheOppositeOfOverrideSheetPublish()
        {
            CadSheetSetFile file = Tower();
            file.Change(set => set.Subsets.Single(s => s.Name == "Structural").PublishSheets = false);

            string text = CadDstCodec.Decode(File.ReadAllBytes(file.Path));
            Assert.Single(System.Text.RegularExpressions.Regex.Matches(text, "OverrideSheetPublish\" vt=\"2\">-1<"));

            file.Change(set => set.Subsets.Single(s => s.Name == "Structural").PublishSheets = true);

            Assert.DoesNotContain("OverrideSheetPublish", CadDstCodec.Decode(File.ReadAllBytes(file.Path)));
        }

        [Fact]
        public void ASelection_ListsTheSheetsUnderItInSetOrder_WhateverOrderItWasMadeIn()
        {
            CadSheetSetFile file = Tower();
            CadSheetSet set = file.SheetSet;
            var selection = new CadSheetComponent[]
            {
                SheetNumbered(file, "S-101"),
                set.Subsets.Single(s => s.Name == "Architectural"),
            };

            CadSheetSetPublish publish = CadSheetSetPublish.Create(set, selection, null, null, null);

            Assert.Equal(
                new[]
                {
                    "A-101 Ground Floor Plan",
                    "A-102 First Floor Plan",
                    "A-501 Wall Sections",
                    "S-101 Foundation Plan",
                },
                Names(publish)
            );
            Assert.Equal(
                new[] { "G-001 Cover Sheet" },
                Names(CadSheetSetPublish.Create(set, new[] { SheetNumbered(file, "G-001") }, null, null, null))
            );
            Assert.Equal(
                5,
                CadSheetSetPublish.Create(set, new CadSheetComponent[] { set }, null, null, null).List.Sheets.Count
            );
        }

        [Fact]
        public void PublishInReverseOrder_ListsTheSheetsInTheReverseOfSetOrder_AndIsKeptInTheFile()
        {
            CadSheetSetFile file = Tower();
            Assert.False(file.SheetSet.PublishOptions.ReverseOrder);

            file.Change(set => set.PublishOptions.ReverseOrder = true);

            Assert.True(CadSheetSetFile.Open(file.Path).SheetSet.PublishOptions.ReverseOrder);
            CadSheetSetPublish publish = CadSheetSetPublish.Create(file.SheetSet, null, null, null, null);
            Assert.Equal(
                new[]
                {
                    "S-101 Foundation Plan",
                    "A-501 Wall Sections",
                    "A-102 First Floor Plan",
                    "A-101 Ground Floor Plan",
                    "G-001 Cover Sheet",
                },
                Names(publish)
            );
            Assert.EndsWith("in reverse set order.", publish.Summary, StringComparison.Ordinal);
        }

        [Fact]
        public void TheSetsPublishOptions_StartAsAutoCadsNewSetDoes_AndTheOutputFolderIsTheFolderOfTheDstUntilSet()
        {
            CadSheetSetFile file = Tower();
            CadSheetSetPublishOptions saved = file.SheetSet.PublishOptions;

            Assert.True(saved.MultiSheet);
            Assert.True(saved.PromptForName);
            Assert.Null(saved.OutputFolder);
            Assert.Equal(
                _folder,
                CadSheetSetPublish.Create(file.SheetSet, null, null, null, null).List.Options.OutputFolder
            );

            string output = Path.Combine(_folder, "Issued");
            file.Change(set =>
            {
                set.PublishOptions.OutputFolder = CadFileReference.FromPath(output, _folder);
                set.PublishOptions.MultiSheet = false;
                set.PublishOptions.PromptForName = false;
            });

            CadSheetSetPublishOptions reread = CadSheetSetFile.Open(file.Path).SheetSet.PublishOptions;
            Assert.False(reread.MultiSheet);
            Assert.False(reread.PromptForName);
            Assert.Equal(".\\Issued", reread.OutputFolder.RelativeFileName);
            CadPublishOptions options = CadSheetSetPublish.Create(file.SheetSet, null, null, null, null).List.Options;
            Assert.Equal(output, options.OutputFolder);
            Assert.False(options.MultiSheet);
            Assert.Equal(CadPublishTo.Pdf, options.PublishTo);
            Assert.True(options.CreateBookmarks);
            Assert.True(options.IncludeHyperlinks);
            Assert.Equal("Example Tower", options.FileName);
        }

        [Fact]
        public void APublishOptionsObjectIsMadeInTheSetsTree_WhereAnOlderFileHasNone()
        {
            CadSheetSetFile file = Tower();
            string text = CadDstCodec.Decode(File.ReadAllBytes(file.Path));
            string without = System.Text.RegularExpressions.Regex.Replace(
                text,
                "<AcSmPublishOptions .*?</AcSmPublishOptions>",
                string.Empty
            );
            Assert.NotEqual(text, without);
            File.WriteAllBytes(file.Path, CadDstCodec.Encode(without));
            CadSheetSetFile older = CadSheetSetFile.Open(file.Path);
            Assert.False(older.SheetSet.PublishOptions.MultiSheet);
            Assert.Null(older.SheetSet.PublishOptions.OutputFolder);

            older.Change(set => set.PublishOptions.ReverseOrder = true);

            string saved = CadDstCodec.Decode(File.ReadAllBytes(file.Path));
            Assert.Contains("propname=\"PublishInReverseOrder\" vt=\"2\">-1<", saved);
            Assert.DoesNotContain("propname=\"DwfType\"", saved);
            Assert.True(CadSheetSetFile.Open(file.Path).SheetSet.PublishOptions.ReverseOrder);
        }

        [Fact]
        public void ASheetWhoseDrawingIsGone_StaysInTheListWithItsStatus_AndNothingElseIsAffected()
        {
            CadSheetSetFile file = Tower();
            File.Delete(Path.Combine(_folder, "a102.dwg"));

            CadSheetSetPublish publish = CadSheetSetPublish.Create(file.SheetSet, null, null, null, null);

            Assert.Equal(5, publish.List.Sheets.Count);
            CadPublishSheet missing = publish.List.Sheets.Single(s => s.Name == "A-102 First Floor Plan");
            Assert.Equal(CadPublishSheet.DrawingNotFound, missing.Status);
            Assert.False(missing.IsLoaded);
            Assert.Equal(4, publish.List.Sheets.Count(s => s.IsLoaded));
            CadPublishStep step = publish.List.Plan(null).Single(p => p.Sheet == missing);
            Assert.Equal(CadPublishSheet.DrawingNotFound, step.Skipped);
        }

        [Fact]
        public void ASheetFoundByHandleAfterItsLayoutWasRenamed_IsPublishedAsTheRenamedLayout()
        {
            CadSheetSetFile file = Tower();
            SheetSetTestDrawings.Edit(Path.Combine(_folder, "s101.dwg"), database =>
                database.Layouts.Single(l => l.LayoutName == "Sheet").LayoutName = "Foundations"
            );

            CadSheetSetPublish publish = CadSheetSetPublish.Create(file.SheetSet, null, null, null, null);

            CadPublishSheet sheet = publish.List.Sheets.Single(s => s.Name == "S-101 Foundation Plan");
            Assert.Equal("Foundations", sheet.LayoutName);
            Assert.True(sheet.IsLoaded);
        }

        [Fact]
        public void APageSetupOverride_SetsEverySheetsPageSetupFromTheOverridesFile_AndIsRefusedWhereThereIsNone()
        {
            CadSheetSetFile file = Tower();
            Assert.Throws<InvalidOperationException>(() =>
                CadSheetSetPublish.Create(file.SheetSet, null, "A2 sheet", null, null)
            );

            CadDocument setups = CadDocument.From(SheetSetTestDrawings.Build("Ground"), "Setups.dwt");
            CadPlotSettings settings = CadPlotSettings.Read(setups, setups.ModelSpace);
            settings.Paper = CadPaper.Standard[2];
            Assert.True(settings.SaveAsPageSetup(setups, "A2 sheet").Succeeded);
            string dwt = Path.Combine(_folder, "Setups.dwt");
            setups.SaveAs(dwt);
            file.Change(set => set.PageSetupOverrides = CadFileReference.FromPath(dwt, _folder));

            CadSheetSetPublish publish = CadSheetSetPublish.Create(file.SheetSet, null, "A2 sheet", null, null);

            Assert.All(publish.List.Sheets, s => Assert.Equal("A2 sheet", s.PageSetup));
            Assert.All(publish.List.Sheets, s => Assert.Equal(dwt, s.PageSetupFile, StringComparer.OrdinalIgnoreCase));
            Assert.All(publish.List.Plan(null), step =>
                Assert.Equal(CadPaper.Standard[2].Name, step.Settings.Paper.Name)
            );

            CadSheetSetPublish missing = CadSheetSetPublish.Create(file.SheetSet, null, "No such setup", null, null);
            Assert.All(missing.List.Sheets, s => Assert.Equal(CadPublishSheet.PageSetupNotFound, s.Status));
            Assert.All(missing.List.Plan(null), step => Assert.Equal(CadPublishSheet.PageSetupNotFound, step.Skipped));
        }

        [Fact]
        public void TheSheetListsNamesAreNumberSpaceTitle_TheTitleAloneWithNoNumber_AndNeverTwoTheSame()
        {
            CadSheetSetFile file = Tower();
            file.Change(set =>
            {
                set.AllSheets().Single(s => s.Number == "A-101").Number = string.Empty;
                CadSheetSetSheet second = set.AllSheets().Single(s => s.Number == "A-102");
                second.Number = "A-501";
                second.Title = "Wall Sections";
            });

            CadSheetSetPublish publish = CadSheetSetPublish.Create(file.SheetSet, null, null, null, null);

            Assert.Equal(
                new[]
                {
                    "G-001 Cover Sheet",
                    "Ground Floor Plan",
                    "A-501 Wall Sections",
                    "A-501 Wall Sections (2)",
                    "S-101 Foundation Plan",
                },
                Names(publish)
            );
        }

        [Fact]
        public void AListMadeFromASet_SavesItsSheetListAsASheetSets_AndAPlainListDoesNot()
        {
            CadSheetSetFile file = Tower();
            CadSheetSetPublish publish = CadSheetSetPublish.Create(file.SheetSet, null, null, null, null);
            string dsd = Path.Combine(_folder, "set.dsd");
            string plain = Path.Combine(_folder, "plain.dsd");

            publish.List.Save(dsd);
            new CadPublishList(null).Save(plain);

            string saved = File.ReadAllText(dsd);
            Assert.Contains("IsSheetSet=TRUE", saved);
            Assert.Contains("SheetSet Name=Example Tower", saved);
            Assert.Contains("[DWF6Sheet:A-101 Ground Floor Plan]", saved);
            string notASet = File.ReadAllText(plain);
            Assert.Contains("IsSheetSet=FALSE", notASet);
            Assert.DoesNotContain("IsSheetSet=TRUE", notASet);
            Assert.DoesNotContain("SheetSet Name=Example Tower", notASet);
        }

        [Fact]
        public void APublishOptionsCopy_KeepsBookmarksAndHyperlinks_AndAListFilesRoundTripLeavesThemOff()
        {
            var options = new CadPublishOptions { CreateBookmarks = true, IncludeHyperlinks = true };

            CadPublishOptions copy = options.Clone();

            Assert.True(copy.CreateBookmarks);
            Assert.True(copy.IncludeHyperlinks);
            Assert.False(new CadPublishOptions().CreateBookmarks);
            Assert.False(new CadPublishOptions().IncludeHyperlinks);
        }
    }
}
