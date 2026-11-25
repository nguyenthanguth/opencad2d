using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using OpenCAD2D.SheetSets;
using Xunit;

namespace OpenCAD2D.Wpf.UnitTest
{
    /// <summary>
    /// Sheet Properties (A12 4b, board A12SheetProperties): the rows of a sheet and its custom property values, Expected layout
    /// and Found layout (a missing sheet re-associated through the first), and the Rename Options that rename the layout and
    /// the drawing file when the title or the number is changed. Drawings are made in code in a temporary folder.
    /// </summary>
    public sealed class SheetSetSheetPropertiesDialogTests : IDisposable
    {
        private readonly SheetSetDialogKit _kit = new SheetSetDialogKit();

        public void Dispose() => _kit.Dispose();

        private string DrawingOf(string number) => _kit.Sheet(number).Layout.File.Resolve(_kit.File.Folder);

        [Fact]
        public void TheGridShowsTheSheet_AndItsLayoutFound()
        {
            CadSheetSetFile file = _kit.Make();

            var dialog = new SheetSetSheetPropertiesViewModel(file, _kit.Sheet("A-101"));

            Assert.Equal("Sheet Properties - A-101 Ground Floor Plan", dialog.Title);
            Assert.Equal("Ground Floor Plan", dialog.Row("Title").Text);
            Assert.Equal("A-101", dialog.Row("Number").Text);
            Assert.Equal("Yes", dialog.Row("Include for publish").Text);
            string layout = "A-101 Ground Floor Plan (" + DrawingOf("A-101") + ")";
            Assert.Equal(layout, dialog.Row("Expected layout").Text);
            Assert.Equal(layout, dialog.Row("Found layout").Text);
            Assert.True(dialog.Row("Found layout").IsReadOnly);
            Assert.False(dialog.Row("Found layout").IsRed);
            Assert.Equal(string.Empty, dialog.Row("Drawn By").Text);
            Assert.False(dialog.Row("Drawn By").IsReadOnly);
            Assert.False(dialog.HasProblem);
            Assert.True(dialog.RenameLayout);
            Assert.True(dialog.PrefixLayoutWithNumber);
            Assert.False(dialog.RenameFile);
            Assert.True(dialog.PrefixFileWithNumber);
        }

        [Fact]
        public void OkSavesTheEditedRowsAndTheCustomValues_InOneWrite()
        {
            CadSheetSetFile file = _kit.Make();
            int revision = file.FileRevision;
            var dialog = new SheetSetSheetPropertiesViewModel(file, _kit.Sheet("A-102"));

            dialog.Row("Description").Text = "Plan at first floor, 1:100";
            dialog.Row("Category").Text = "Plans";
            dialog.Row("Revision number").Text = "C";
            dialog.Row("Revision date").Text = "2026-11-20";
            dialog.Row("Issue purpose").Text = "For tender";
            dialog.Row("Include for publish").Text = "No";
            dialog.Row("Drawn By").Text = "T. Tran";

            Assert.True(dialog.Apply());

            Assert.Equal(revision + 1, file.FileRevision);
            CadSheetSetSheet sheet = _kit.Reopen().SheetSet.AllSheets().Single(s => s.Number == "A-102");
            Assert.Equal("Plan at first floor, 1:100", sheet.Description);
            Assert.Equal("Plans", sheet.Category);
            Assert.Equal("C", sheet.RevisionNumber);
            Assert.Equal("2026-11-20", sheet.RevisionDate);
            Assert.Equal("For tender", sheet.IssuePurpose);
            Assert.False(sheet.IncludeForPublish);
            Assert.Equal("T. Tran", sheet.FindCustomProperty("Drawn By").Value);
            Assert.Equal("Six storeys", _kit.Reopen().SheetSet.Description);
        }

        [Fact]
        public void ChangingTheTitleWithTheRenameOptionsOn_RenamesTheLayout_AndLeavesTheFile()
        {
            CadSheetSetFile file = _kit.Make();
            string drawing = DrawingOf("A-101");
            var dialog = new SheetSetSheetPropertiesViewModel(file, _kit.Sheet("A-101"));

            dialog.Row("Title").Text = "Ground Floor Plan B";
            Assert.True(dialog.Apply());

            Assert.Equal(new[] { "A-101 Ground Floor Plan B" }, SheetSetDialogKit.LayoutNames(drawing));
            CadSheetSetSheet sheet = _kit.Reopen().SheetSet.AllSheets().Single(s => s.Number == "A-101");
            Assert.Equal("Ground Floor Plan B", sheet.Title);
            Assert.Equal("A-101 Ground Floor Plan B", sheet.Layout.Name);
            Assert.Equal(drawing, sheet.Layout.File.Resolve(file.Folder), ignoreCase: true);
        }

        [Fact]
        public void TheRenameOptionsOff_ChangeTheTitleAndNothingInTheDrawing()
        {
            CadSheetSetFile file = _kit.Make();
            string drawing = DrawingOf("A-101");
            var dialog = new SheetSetSheetPropertiesViewModel(file, _kit.Sheet("A-101"));
            dialog.RenameLayout = false;

            dialog.Row("Title").Text = "Ground Floor Plan B";
            Assert.True(dialog.Apply());

            Assert.Equal(new[] { "A-101 Ground Floor Plan" }, SheetSetDialogKit.LayoutNames(drawing));
            Assert.Equal(
                "Ground Floor Plan B",
                _kit.Reopen().SheetSet.AllSheets().Single(s => s.Number == "A-101").Title
            );
        }

        [Fact]
        public void PrefixWithSheetNumberOff_NamesTheLayoutByTheTitleAlone()
        {
            CadSheetSetFile file = _kit.Make();
            string drawing = DrawingOf("A-101");
            var dialog = new SheetSetSheetPropertiesViewModel(file, _kit.Sheet("A-101"));
            dialog.PrefixLayoutWithNumber = false;

            dialog.Row("Number").Text = "A-111";
            Assert.True(dialog.Apply());

            Assert.Equal(new[] { "Ground Floor Plan" }, SheetSetDialogKit.LayoutNames(drawing));
        }

        [Fact]
        public void RenameDrawingFileOn_RenamesTheFile_AndTellsTheChangerWhichDrawingItRewrites()
        {
            CadSheetSetFile file = _kit.Make();
            string drawing = DrawingOf("A-101");
            IReadOnlyList<string> rewritten = null;
            string followed = null;
            var dialog = new SheetSetSheetPropertiesViewModel(
                file,
                _kit.Sheet("A-101"),
                new SheetSetDialogOptions
                {
                    Changer = (edit, drawings, follow) =>
                    {
                        rewritten = drawings;
                        followed = follow;
                        file.Change(edit);
                    },
                }
            );
            dialog.RenameFile = true;

            dialog.Row("Title").Text = "Plan";
            Assert.True(dialog.Apply());

            string renamed = Path.Combine(Path.GetDirectoryName(drawing), "A-101 Plan.dwg");
            Assert.True(File.Exists(renamed));
            Assert.False(File.Exists(drawing));
            Assert.Equal(new[] { drawing }, rewritten);
            Assert.Equal(_kit.Sheet("A-101").Id, followed);
            Assert.Equal(renamed, DrawingOf("A-101"), ignoreCase: true);
        }

        [Fact]
        public void ASheetWhoseDrawingIsGone_ReadsNotFoundInRed_AndSaysWhyAndHowToMendIt()
        {
            CadSheetSetFile file = _kit.Make();
            string drawing = DrawingOf("A-102");
            File.Delete(drawing);

            var dialog = new SheetSetSheetPropertiesViewModel(file, _kit.Sheet("A-102"));

            Assert.Equal("Not found", dialog.Row("Found layout").Text);
            Assert.True(dialog.Row("Found layout").IsRed);
            Assert.True(dialog.HasProblem);
            Assert.Equal(
                "The drawing "
                    + drawing
                    + " does not exist. Pick the drawing and layout under Expected layout (...), or Cancel.",
                dialog.Problem
            );
            Assert.True(dialog.CanApply);

            dialog.Row("Title").Text = "First Floor Plan B";
            Assert.True(dialog.Apply());
            Assert.Equal(
                "First Floor Plan B",
                _kit.Reopen().SheetSet.AllSheets().Single(s => s.Number == "A-102").Title
            );
        }

        [Fact]
        public void ExpectedLayout_ReassociatesTheSheetWithAnotherDrawingAndLayout_AndMarksIt()
        {
            CadSheetSetFile file = _kit.Make();
            File.Delete(DrawingOf("A-102"));
            string other = _kit.Drawing(Path.Combine(_kit.Folder, "found"), "Plan.dwg", "Level 1", "Level 2");
            var dialog = new SheetSetSheetPropertiesViewModel(file, _kit.Sheet("A-102"));
            string asked = null;
            dialog.PickLayout = now =>
            {
                asked = now;
                return new SheetSetLayoutChoice(other, "Level 2");
            };

            Assert.True(dialog.Row("Expected layout").HasButton);
            Assert.True(dialog.Row("Expected layout").Browse());
            Assert.Equal("Level 2 (" + other + ")", dialog.Row("Expected layout").Text);
            Assert.True(dialog.Apply());

            Assert.NotNull(asked);
            CadSheetSetSheet sheet = _kit.Reopen().SheetSet.AllSheets().Single(s => s.Number == "A-102");
            Assert.Equal("Level 2", sheet.Layout.Name);
            Assert.Equal(other, sheet.Layout.File.Resolve(file.Folder), ignoreCase: true);
            Assert.NotNull(CadSheetSetHint.Read(other));
        }

        [Fact]
        public void ALayoutTheSetRefuses_IsRefusedInTheDialogsWords_AndTheDrawingIsLeftAlone()
        {
            CadSheetSetFile file = _kit.Make();
            string other = _kit.Drawing(Path.Combine(_kit.Folder, "found"), "Plan.dwg", "Level#1");
            var dialog = new SheetSetSheetPropertiesViewModel(file, _kit.Sheet("A-102"));
            dialog.PickLayout = now => new SheetSetLayoutChoice(other, "Level#1");
            Assert.True(dialog.Row("Expected layout").Browse());

            Assert.False(dialog.Apply());

            Assert.Contains("holds a #", dialog.Error);
            Assert.Null(CadSheetSetHint.Read(other));
        }

        [Fact]
        public void ALockedSet_OpensTheSheetReadOnly_AndTheRenameOptionsAreGreyed()
        {
            CadSheetSetFile file = _kit.Make();
            var dialog = new SheetSetSheetPropertiesViewModel(
                file,
                _kit.Sheet("A-101"),
                new SheetSetDialogOptions { ReadOnlyReason = "The sheet set is locked by h.le on WS-07." }
            );

            Assert.True(dialog.IsReadOnly);
            Assert.False(dialog.CanEditRenameOptions);
            Assert.False(dialog.Row("Expected layout").HasButton);
            Assert.False(dialog.Apply());
        }

        [Fact]
        public void PrefixWithSheetNumber_IsGreyedWhileItsParentBoxIsOff()
        {
            var dialog = new SheetSetSheetPropertiesViewModel(_kit.Make(), _kit.Sheet("A-101"));

            Assert.True(dialog.CanEditPrefixLayout);
            Assert.False(dialog.CanEditPrefixFile);

            dialog.RenameLayout = false;
            dialog.RenameFile = true;

            Assert.False(dialog.CanEditPrefixLayout);
            Assert.True(dialog.CanEditPrefixFile);
        }
    }
}
