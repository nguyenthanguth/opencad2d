using System;
using System.IO;
using System.Linq;
using OpenCAD2D.SheetSets;
using Xunit;

namespace OpenCAD2D.Wpf.UnitTest
{
    /// <summary>
    /// Subset Properties (A12 4b, board A12SubsetProperties), which New Subset, Rename Subset and Properties of a subset open:
    /// a new subset starts from its parent's values, the name is refused where a sibling has it, and Create Folder Hierarchy
    /// makes the folder and points the subset's sheet location to it. Every set and folder is made in code.
    /// </summary>
    public sealed class SheetSetSubsetDialogTests : IDisposable
    {
        private readonly SheetSetDialogKit _kit = new SheetSetDialogKit();

        public void Dispose() => _kit.Dispose();

        [Fact]
        public void ANewSubset_StartsWithTheValuesOfItsParent()
        {
            CadSheetSetFile file = _kit.Make();

            var dialog = SheetSetSubsetPropertiesViewModel.New(file, _kit.Subset("Architectural"));

            Assert.Equal("Subset Properties", dialog.Title);
            Assert.Equal(string.Empty, dialog.Row("Subset name").Text);
            Assert.Equal("No", dialog.Row("Create Folder Hierarchy").Text);
            Assert.Equal("Yes", dialog.Row("Publish Sheets in Subset").Text);
            Assert.Equal(Path.Combine(_kit.SetFolder, "Architectural"), dialog.Row("New Sheet Location").Text);
            Assert.Equal("A1 sheet (" + _kit.Template + ")", dialog.Row("Sheet Creation Template").Text);
            Assert.Equal("No", dialog.Row("Prompt for Template").Text);
        }

        [Fact]
        public void OkMakesTheSubsetUnderItsParent_ThatInheritsWhatTheUserLeftAlone()
        {
            CadSheetSetFile file = _kit.Make();
            var dialog = SheetSetSubsetPropertiesViewModel.New(file, _kit.Subset("Architectural"));

            dialog.Row("Subset name").Text = "Interiors";

            Assert.True(dialog.CanApply);
            Assert.True(dialog.Apply());

            CadSheetSubset interiors = _kit.Reopen()
                .SheetSet.Subsets.Single(s => s.Name == "Architectural")
                .Subsets.Single();
            Assert.Equal("Interiors", interiors.Name);
            Assert.Equal(interiors.Id, dialog.SubsetId);
            Assert.Null(interiors.Template);
            Assert.Null(interiors.NewSheetLocation);
            Assert.True(interiors.PublishSheets);
        }

        [Fact]
        public void CreateFolderHierarchyYes_MakesTheFolderAtOk_AndPointsTheLocationToIt()
        {
            CadSheetSetFile file = _kit.Make();
            var dialog = SheetSetSubsetPropertiesViewModel.New(file, _kit.Subset("Architectural"));
            string folder = Path.Combine(_kit.SetFolder, "Architectural", "Interiors");

            dialog.Row("Subset name").Text = "Interiors";
            dialog.Row("Create Folder Hierarchy").Text = "Yes";

            Assert.Equal(folder, dialog.Row("New Sheet Location").Text);
            Assert.True(dialog.Row("New Sheet Location").IsReadOnly);
            Assert.False(Directory.Exists(folder));

            Assert.True(dialog.Apply());

            Assert.True(Directory.Exists(folder));
            CadSheetSubset interiors = _kit.Reopen()
                .SheetSet.Subsets.Single(s => s.Name == "Architectural")
                .Subsets.Single();
            Assert.Equal(folder, interiors.NewSheetLocation.Resolve(file.Folder), ignoreCase: true);
        }

        [Fact]
        public void AChangedLocationTemplateAndPrompt_AreWrittenToTheNewSubset()
        {
            CadSheetSetFile file = _kit.Make();
            var dialog = SheetSetSubsetPropertiesViewModel.New(file, _kit.Subset("Structural"));
            string details = Path.Combine(_kit.Folder, "Details");
            dialog.PickTemplate = now => new CadDrawingReference(
                CadFileReference.FromPath(_kit.Template, file.Folder),
                "Cover",
                null,
                null
            );
            dialog.BrowseFolder = now => details;

            dialog.Row("Subset name").Text = "Details";
            Assert.True(dialog.Row("New Sheet Location").Browse());
            Assert.True(dialog.Row("Sheet Creation Template").Browse());
            dialog.Row("Prompt for Template").Text = "Yes";
            dialog.Row("Publish Sheets in Subset").Text = "No";
            Assert.True(dialog.Apply());

            CadSheetSubset made = _kit.Reopen().SheetSet.Subsets.Single(s => s.Name == "Structural").Subsets.Single();
            Assert.Equal(details, made.NewSheetLocation.FileName);
            Assert.Equal("Cover", made.Template.Name);
            Assert.True(made.PromptForTemplate);
            Assert.False(made.PublishSheets);
        }

        [Fact]
        public void ANameASiblingHasIsRefusedInRed_LetterCaseIgnored_AndOkIsGreyed()
        {
            CadSheetSetFile file = _kit.Make();
            file.Change(set => set.Subsets.Single(s => s.Name == "Architectural").NewSubset("Details", null, false));
            var dialog = SheetSetSubsetPropertiesViewModel.New(file, _kit.Subset("Architectural"));

            dialog.Row("Subset name").Text = "details";

            Assert.Equal("Architectural already holds a subset named Details.", dialog.Error);
            Assert.True(dialog.Row("Subset name").HasError);
            Assert.False(dialog.CanApply);
            Assert.False(dialog.Apply());
            Assert.Single(_kit.Reopen().SheetSet.Subsets.Single(s => s.Name == "Architectural").Subsets);
        }

        [Fact]
        public void AnEmptyName_AndAFolderNameThatCannotBeOne_AreRefused()
        {
            CadSheetSetFile file = _kit.Make();
            var dialog = SheetSetSubsetPropertiesViewModel.New(file, file.SheetSet);

            Assert.False(dialog.CanApply);

            dialog.Row("Subset name").Text = "Plans: 1/50";
            Assert.True(dialog.CanApply);
            dialog.Row("Create Folder Hierarchy").Text = "Yes";

            Assert.Equal("The name \"Plans: 1/50\" cannot be the name of a folder.", dialog.Error);
            Assert.False(dialog.CanApply);
        }

        [Fact]
        public void RenameSubset_ChangesTheNameOfTheSubset_AndASiblingCheckIgnoresItself()
        {
            CadSheetSetFile file = _kit.Make();
            var dialog = SheetSetSubsetPropertiesViewModel.Existing(file, _kit.Subset("Structural"));

            Assert.Equal("Structural", dialog.Row("Subset name").Text);
            dialog.Row("Subset name").Text = "STRUCTURAL";
            Assert.Null(dialog.Error);
            dialog.Row("Subset name").Text = "Architectural";
            Assert.Equal("Tower already holds a subset named Architectural.", dialog.Error);

            dialog.Row("Subset name").Text = "Structure";
            Assert.True(dialog.Apply());

            Assert.Equal(new[] { "Architectural", "Structure" }, _kit.Reopen().SheetSet.Subsets.Select(s => s.Name));
        }

        [Fact]
        public void ALockedSet_OpensTheSubsetReadOnly()
        {
            CadSheetSetFile file = _kit.Make();
            var dialog = SheetSetSubsetPropertiesViewModel.Existing(
                file,
                _kit.Subset("Structural"),
                new SheetSetDialogOptions { ReadOnlyReason = "The sheet set is locked by h.le on WS-07." }
            );

            dialog.Row("Subset name").Text = "Other";

            Assert.True(dialog.IsReadOnly);
            Assert.False(dialog.Apply());
            Assert.Equal("Structural", _kit.Reopen().SheetSet.Subsets[1].Name);
        }
    }
}
