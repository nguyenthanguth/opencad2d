using System;
using System.IO;
using System.Linq;
using OpenCAD2D.SheetSets;
using Xunit;

namespace OpenCAD2D.UnitTest
{
    /// <summary>
    /// Publish Sheets in Subset (the Subset Properties row of A12 4b): true for a subset the file says nothing of, false where
    /// the sheets of the subset are left out of publishing, kept in the file as AutoCAD's <c>OverrideSheetPublish</c> (the
    /// opposite), which is left out while it is false so that a subset that is published writes nothing.
    /// </summary>
    public class SheetSetSubsetPublishTests : IDisposable
    {
        private readonly string _folder = Path.Combine(
            Path.GetTempPath(),
            "gk",
            "sheetsetpublish-" + Guid.NewGuid().ToString("N")
        );

        public SheetSetSubsetPublishTests()
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
        public void ASubsetIsPublishedUnlessTheFileSaysOtherwise_AndTheChoiceSurvivesASaveAndReopen()
        {
            string dst = Path.Combine(_folder, "Tower.dst");
            CadSheetSetFile file = CadSheetSetFile.Create(dst, "Tower", null, SheetSetFileTests.Counter());
            file.Change(set =>
            {
                set.AddSubset("Plans");
                set.AddSubset("Drafts");
            });
            Assert.All(file.SheetSet.Subsets, s => Assert.True(s.PublishSheets));

            file.Change(set => set.Subsets.Single(s => s.Name == "Drafts").PublishSheets = false);

            CadSheetSet reopened = CadSheetSetFile.Open(dst).SheetSet;
            Assert.True(reopened.Subsets.Single(s => s.Name == "Plans").PublishSheets);
            Assert.False(reopened.Subsets.Single(s => s.Name == "Drafts").PublishSheets);

            int revision = file.FileRevision;
            file.Change(set => set.Subsets.Single(s => s.Name == "Drafts").PublishSheets = true);

            Assert.Equal(revision + 1, file.FileRevision);
            Assert.True(CadSheetSetFile.Open(dst).SheetSet.Subsets.Single(s => s.Name == "Drafts").PublishSheets);
        }

        [Fact]
        public void PublishSheets_CanBeChangedOnlyInsideChange()
        {
            CadSheetSetFile file = CadSheetSetFile.Create(
                Path.Combine(_folder, "Tower.dst"),
                "Tower",
                null,
                SheetSetFileTests.Counter()
            );
            file.Change(set => set.AddSubset("Plans"));
            CadSheetSubset plans = file.SheetSet.Subsets.Single();

            Assert.Throws<InvalidOperationException>(() => plans.PublishSheets = false);
        }
    }
}
