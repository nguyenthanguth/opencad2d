using System;
using System.IO;
using System.Linq;
using OpenCAD2D.SheetSets;
using Xunit;

namespace OpenCAD2D.Wpf.UnitTest
{
    /// <summary>
    /// New Sheet and Select Layout as Sheet Template (A12 4b, board A12NewSheet): the number, title and file name (which follows
    /// them until it is typed), the folder and template that come from the subset or the set, a title or a file the sheet cannot
    /// have refused in the dialog's words with OK greyed, the sheet put after the one it was asked from, and the template's
    /// layouts listed in tab order. Drawings are made in code in a temporary folder.
    /// </summary>
    public sealed class SheetSetNewSheetDialogTests : IDisposable
    {
        private readonly SheetSetDialogKit _kit = new SheetSetDialogKit();

        public void Dispose() => _kit.Dispose();

        private string Architectural => Path.Combine(_kit.SetFolder, "Architectural");

        [Fact]
        public void TheDialogStartsWithTheFolderAndTemplateOfTheSubset()
        {
            CadSheetSetFile file = _kit.Make();

            var dialog = new SheetSetNewSheetViewModel(file, _kit.Subset("Architectural"), null);

            Assert.Equal("New Sheet", dialog.Title);
            Assert.Equal(string.Empty, dialog.Number);
            Assert.Equal(string.Empty, dialog.SheetTitle);
            Assert.Equal(Architectural, dialog.FolderPath);
            Assert.Equal("A1 sheet (" + _kit.Template + ")", dialog.SheetTemplate);
            Assert.True(dialog.OpenInEditor);
            Assert.False(dialog.PromptsForTemplate);
            Assert.False(dialog.CanApply);
        }

        [Fact]
        public void TheFileNameFollowsNumberAndTitle_UntilItIsTypedIn()
        {
            var dialog = new SheetSetNewSheetViewModel(_kit.Make(), _kit.Subset("Architectural"), null);

            dialog.Number = "A-104";
            dialog.SheetTitle = "Roof Terrace Plan";
            Assert.Equal("A-104 Roof Terrace Plan", dialog.FileName);

            dialog.FileName = "Roof";
            dialog.SheetTitle = "Roof Terrace Plan B";
            Assert.Equal("Roof", dialog.FileName);
        }

        [Fact]
        public void ASheetWithNoNumber_IsNamedByItsTitleAlone()
        {
            var dialog = new SheetSetNewSheetViewModel(_kit.Make(), _kit.Subset("Architectural"), null);

            dialog.SheetTitle = "Cover note";

            Assert.Equal("Cover note", dialog.FileName);
            Assert.True(dialog.CanApply);
        }

        [Fact]
        public void OkMakesTheDrawingFromTheTemplate_AndAddsTheSheetAfterTheOneItWasAskedFrom()
        {
            CadSheetSetFile file = _kit.Make();
            var dialog = new SheetSetNewSheetViewModel(file, _kit.Subset("Architectural"), _kit.Sheet("A-101"));
            dialog.Number = "A-104";
            dialog.SheetTitle = "Roof Terrace Plan";

            Assert.True(dialog.CanApply);
            Assert.True(dialog.Apply());

            string drawing = Path.Combine(Architectural, "A-104 Roof Terrace Plan.dwg");
            Assert.True(File.Exists(drawing));
            Assert.Equal(new[] { "A-104 Roof Terrace Plan" }, SheetSetDialogKit.LayoutNames(drawing));
            CadSheetSubset subset = _kit.Reopen().SheetSet.Subsets.Single(s => s.Name == "Architectural");
            Assert.Equal(new[] { "A-101", "A-104", "A-102" }, subset.Sheets.Select(s => s.Number).ToArray());
            Assert.NotNull(dialog.Created);
            Assert.Equal(subset.Sheets[1].Id, dialog.Created.SheetId);
            Assert.Equal(drawing, dialog.Created.DrawingFile, ignoreCase: true);
            Assert.Equal("A-104 Roof Terrace Plan", dialog.Created.LayoutName);
            Assert.True(dialog.Created.OpenInEditor);
            Assert.NotNull(CadSheetSetHint.Read(drawing));
        }

        [Fact]
        public void WithNoSheetToFollow_TheSheetGoesAtTheEndOfTheSubset_AndTheEditorBoxCanBeOff()
        {
            CadSheetSetFile file = _kit.Make();
            var dialog = new SheetSetNewSheetViewModel(file, _kit.Subset("Structural"), null);
            dialog.SheetTitle = "Slab";
            dialog.OpenInEditor = false;

            Assert.True(dialog.Apply());

            Assert.Equal(
                new[] { "S-101", string.Empty },
                _kit.Reopen()
                    .SheetSet.Subsets.Single(s => s.Name == "Structural")
                    .Sheets.Select(s => s.Number)
                    .ToArray()
            );
            Assert.False(dialog.Created.OpenInEditor);
        }

        [Fact]
        public void AFileThatIsThereAlready_IsRefusedInRed_NeverOverwritten_AndOkIsGreyed()
        {
            CadSheetSetFile file = _kit.Make();
            string existing = Path.Combine(Architectural, "A-103 Roof Plan.dwg");
            File.Copy(Path.Combine(Architectural, "A-101 Ground Floor Plan.dwg"), existing);
            long length = new FileInfo(existing).Length;
            var dialog = new SheetSetNewSheetViewModel(file, _kit.Subset("Architectural"), null);
            dialog.Number = "A-104";
            dialog.SheetTitle = "Roof Terrace Plan";

            dialog.FileName = "A-103 Roof Plan";

            Assert.Equal(
                "A-103 Roof Plan.dwg already exists in this folder. Choose another file name, or Import Layout as Sheet to use that drawing.",
                dialog.Error
            );
            Assert.False(dialog.CanApply);
            Assert.False(dialog.Apply());
            Assert.Equal(length, new FileInfo(existing).Length);
            Assert.Equal(2, _kit.Reopen().SheetSet.Subsets.Single(s => s.Name == "Architectural").Sheets.Count);

            dialog.FileName = "A-104 Roof Plan";

            Assert.Null(dialog.Error);
            Assert.True(dialog.CanApply);
        }

        [Fact]
        public void AFileNameACharacterOfWhichAFileCannotHold_IsRefused()
        {
            var dialog = new SheetSetNewSheetViewModel(_kit.Make(), _kit.Subset("Architectural"), null);
            dialog.SheetTitle = "Plan";

            dialog.FileName = "Plan: ground";

            Assert.Equal("The file name \"Plan: ground\" holds a character a file cannot.", dialog.Error);
            Assert.False(dialog.CanApply);
        }

        [Fact]
        public void ASetWithNoTemplate_SaysSo_AndAnEmptyTitleIsNotRefusedAloud()
        {
            string dst = Path.Combine(_kit.SetFolder, "Bare.dst");
            CadSheetSetFile bare = CadSheetSetFile.Create(dst, "Bare", null, SheetSetDialogKit.Counter());
            bare.Change(set => set.PromptForTemplate = false);

            var dialog = new SheetSetNewSheetViewModel(bare, bare.SheetSet, null);
            dialog.SheetTitle = "Plan";

            Assert.Equal(
                "There is no sheet creation template: set one in the Sheet Set Properties of the sheet set or the subset.",
                dialog.Error
            );
            Assert.False(dialog.CanApply);
            Assert.Equal("None", dialog.SheetTemplate);
        }

        [Fact]
        public void WherePromptForTemplateIsYes_TheTemplateIsAskedFirst_AndTheOneChosenIsUsed()
        {
            CadSheetSetFile file = _kit.Make();
            file.Change(set => set.Subsets.Single(s => s.Name == "Structural").PromptForTemplate = true);
            var dialog = new SheetSetNewSheetViewModel(file, _kit.Subset("Structural"), null);
            dialog.SheetTitle = "Slab";

            Assert.True(dialog.PromptsForTemplate);
            Assert.False(dialog.CanApply);

            dialog.Template = new CadDrawingReference(
                CadFileReference.FromPath(_kit.Template, file.Folder),
                "Cover",
                null,
                null
            );
            Assert.Equal("Cover (" + _kit.Template + ")", dialog.SheetTemplate);
            Assert.True(dialog.Apply());

            string drawing = Path.Combine(_kit.SetFolder, "Structural", "Slab.dwg");
            Assert.Equal(new[] { "Slab" }, SheetSetDialogKit.LayoutNames(drawing));
        }

        [Fact]
        public void ALockedSet_CannotMakeASheet()
        {
            var dialog = new SheetSetNewSheetViewModel(
                _kit.Make(),
                _kit.Subset("Architectural"),
                null,
                new SheetSetDialogOptions { ReadOnlyReason = "The sheet set is locked by h.le on WS-07." }
            );
            dialog.SheetTitle = "Plan";

            Assert.False(dialog.CanApply);
            Assert.False(dialog.Apply());
        }

        [Fact]
        public void SelectLayoutAsSheetTemplate_ListsTheLayoutsOfTheDrawingInTabOrder_WithTheCurrentOneChosen()
        {
            CadSheetSetFile file = _kit.Make();
            CadDrawingReference current = file.SheetSet.Template;

            var dialog = SheetSetTemplatePickViewModel.ForLayouts(file, current);

            Assert.Equal("Select Layout as Sheet Template", dialog.Title);
            Assert.Equal(_kit.Template, dialog.FileName, ignoreCase: true);
            Assert.Equal(new[] { "Cover", "A1 sheet" }, dialog.Items.ToArray());
            Assert.Equal("A1 sheet", dialog.SelectedItem);
            Assert.True(dialog.CanApply);

            dialog.SelectedItem = "Cover";
            Assert.True(dialog.Apply());

            Assert.Equal("Cover", dialog.Result.Name);
            Assert.Equal(_kit.Template, dialog.Result.File.Resolve(file.Folder), ignoreCase: true);
            Assert.False(string.IsNullOrEmpty(dialog.Result.Handle));
        }

        [Fact]
        public void BrowseForADrawing_ListsItsLayouts_AndAFileThatIsNotThereIsSaidSo()
        {
            CadSheetSetFile file = _kit.Make();
            string other = _kit.Drawing(Path.Combine(_kit.Folder, "other"), "Other.dwt", "Plan", "Section");
            var dialog = SheetSetTemplatePickViewModel.ForLayouts(file, null);
            dialog.BrowseFile = (title, filter, now) => other;

            Assert.Empty(dialog.Items);
            Assert.False(dialog.CanApply);

            dialog.Browse();

            Assert.Equal(new[] { "Plan", "Section" }, dialog.Items.ToArray());
            Assert.Equal("Plan", dialog.SelectedItem);

            dialog.FileName = Path.Combine(_kit.Folder, "nothing.dwt");

            Assert.Equal("The file " + Path.Combine(_kit.Folder, "nothing.dwt") + " is not there.", dialog.Error);
            Assert.Empty(dialog.Items);
            Assert.False(dialog.CanApply);
        }

        [Fact]
        public void ForABlock_TheListHoldsTheBlocksOfTheDrawing_NotItsLayouts()
        {
            CadSheetSetFile file = _kit.Make();
            string labels = _kit.DrawingWithBlocks(
                Path.Combine(_kit.Folder, "blocks"),
                "Labels.dwg",
                new[] { "Plan" },
                new[] { "View Title", "North" }
            );
            var dialog = SheetSetTemplatePickViewModel.ForBlocks(file, null);
            dialog.BrowseFile = (title, filter, now) => labels;

            dialog.Browse();
            dialog.SelectedItem = "View Title";

            Assert.Equal("Select Block as Label Block", dialog.Title);
            Assert.Equal(
                new[] { "North", "View Title" },
                dialog.Items.OrderBy(n => n, StringComparer.Ordinal).ToArray()
            );
            Assert.True(dialog.Apply());
            Assert.Equal("View Title", dialog.Result.Name);
            Assert.False(string.IsNullOrEmpty(dialog.Result.Handle));
        }
    }
}
