using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using OpenCAD2D.SheetSets;
using Xunit;

namespace OpenCAD2D.Wpf.UnitTest
{
    /// <summary>
    /// Rename and Renumber Sheet (A12 4b, board A12RenameRenumber): the sheet's number, title, layout name and file name; the
    /// layout and file names that follow the title as it is typed while their boxes are on; Previous and Next that apply the
    /// changes first and load the neighbour in set order; a drawing open in the session said so; names the layout or the file
    /// cannot have refused with OK greyed. Drawings are made in code in a temporary folder.
    /// </summary>
    public sealed class SheetSetRenameDialogTests : IDisposable
    {
        private readonly SheetSetDialogKit _kit = new SheetSetDialogKit();

        public void Dispose() => _kit.Dispose();

        private string DrawingOf(string number) => _kit.Sheet(number).Layout.File.Resolve(_kit.File.Folder);

        [Fact]
        public void TheDialogShowsTheSheet_ItsLayoutAndItsFile_WithTheLayoutBoxOn()
        {
            CadSheetSetFile file = _kit.Make();

            var dialog = new SheetSetRenameViewModel(file, _kit.Sheet("A-102"));

            Assert.Equal("Rename & Renumber Sheet", dialog.Title);
            Assert.Equal("A-102", dialog.Number);
            Assert.Equal("First Floor Plan", dialog.SheetTitle);
            Assert.Equal("A-102 First Floor Plan", dialog.LayoutName);
            Assert.Equal("A-102 First Floor Plan", dialog.FileName);
            Assert.Equal(Path.Combine(_kit.SetFolder, "Architectural"), dialog.FolderPath);
            Assert.True(dialog.RenameLayout);
            Assert.True(dialog.PrefixLayoutWithNumber);
            Assert.False(dialog.RenameFile);
            Assert.True(dialog.PrefixFileWithNumber);
            Assert.True(dialog.CanApply);
        }

        [Fact]
        public void TheLayoutAndFileNamesFollowTheTitle_WhileTheirBoxesAreOn_NumberFirstWhenPrefixIsOn()
        {
            var dialog = new SheetSetRenameViewModel(_kit.Make(), _kit.Sheet("A-102"));

            dialog.SheetTitle = "Second Floor Plan";

            Assert.Equal("A-102 Second Floor Plan", dialog.LayoutName);
            Assert.Equal("A-102 First Floor Plan", dialog.FileName);

            dialog.RenameFile = true;
            Assert.Equal("A-102 Second Floor Plan", dialog.FileName);

            dialog.PrefixLayoutWithNumber = false;
            Assert.Equal("Second Floor Plan", dialog.LayoutName);

            dialog.Number = "A-103";
            Assert.Equal("Second Floor Plan", dialog.LayoutName);
            Assert.Equal("A-103 Second Floor Plan", dialog.FileName);
        }

        [Fact]
        public void ANameTypedDirectly_IsKept_UntilTheTitleOrTheNumberChangesAgain()
        {
            var dialog = new SheetSetRenameViewModel(_kit.Make(), _kit.Sheet("A-102"));

            dialog.LayoutName = "Level 1";
            Assert.Equal("Level 1", dialog.LayoutName);

            dialog.SheetTitle = "Level one plan";
            Assert.Equal("A-102 Level one plan", dialog.LayoutName);
        }

        [Fact]
        public void OkRenamesTheLayoutAndTheFile_AndWritesTheNumberAndTitle()
        {
            CadSheetSetFile file = _kit.Make();
            string drawing = DrawingOf("A-102");
            var dialog = new SheetSetRenameViewModel(file, _kit.Sheet("A-102"));
            dialog.RenameFile = true;

            dialog.Number = "A-201";
            dialog.SheetTitle = "Second Floor Plan";
            Assert.True(dialog.Apply());

            string renamed = Path.Combine(Path.GetDirectoryName(drawing), "A-201 Second Floor Plan.dwg");
            Assert.True(File.Exists(renamed));
            Assert.False(File.Exists(drawing));
            Assert.Equal(new[] { "A-201 Second Floor Plan" }, SheetSetDialogKit.LayoutNames(renamed));
            CadSheetSetSheet sheet = _kit.Reopen().SheetSet.AllSheets().Single(s => s.Id == dialog.SheetId);
            Assert.Equal("A-201", sheet.Number);
            Assert.Equal("Second Floor Plan", sheet.Title);
            Assert.Equal("A-201 Second Floor Plan", sheet.Layout.Name);
            Assert.Equal(renamed, sheet.Layout.File.Resolve(file.Folder), ignoreCase: true);
            Assert.True(dialog.Changed);
        }

        [Fact]
        public void OkWithNothingChanged_WritesNothingAndTouchesNoDrawing()
        {
            CadSheetSetFile file = _kit.Make();
            int revision = file.FileRevision;
            string drawing = DrawingOf("A-102");
            DateTime written = File.GetLastWriteTimeUtc(drawing);
            var dialog = new SheetSetRenameViewModel(file, _kit.Sheet("A-102"));

            Assert.True(dialog.Apply());

            Assert.Equal(revision, file.FileRevision);
            Assert.Equal(written, File.GetLastWriteTimeUtc(drawing));
            Assert.False(dialog.Changed);
        }

        [Fact]
        public void WithBothBoxesOff_OnlyTheSetIsWritten_AndNoDrawingIsNamedToTheChanger()
        {
            CadSheetSetFile file = _kit.Make();
            var rewritten = new List<string>();
            var dialog = new SheetSetRenameViewModel(
                file,
                _kit.Sheet("A-102"),
                new SheetSetDialogOptions
                {
                    Changer = (edit, drawings, follow) =>
                    {
                        rewritten.AddRange(drawings ?? new string[0]);
                        file.Change(edit);
                    },
                }
            );
            dialog.RenameLayout = false;

            dialog.SheetTitle = "Other";
            Assert.True(dialog.Apply());

            Assert.Empty(rewritten);
            Assert.Equal(new[] { "A-102 First Floor Plan" }, SheetSetDialogKit.LayoutNames(DrawingOf("A-102")));
        }

        [Fact]
        public void ALayoutNameAFileOrALayoutCannotHave_IsRefusedInRed_AndOkIsGreyed()
        {
            var dialog = new SheetSetRenameViewModel(_kit.Make(), _kit.Sheet("A-102"));

            dialog.LayoutName = "Plan #2";
            Assert.Equal("The layout name \"Plan #2\" holds a #, which a sheet set cannot use.", dialog.Error);
            Assert.False(dialog.CanApply);

            dialog.LayoutName = "Plan 2";
            Assert.Null(dialog.Error);

            dialog.RenameFile = true;
            dialog.FileName = "Plan: 2";
            Assert.Equal("The file name \"Plan: 2\" holds a character a file cannot.", dialog.Error);
            Assert.False(dialog.CanApply);

            dialog.FileName = "A-101 Ground Floor Plan";
            Assert.Equal("A-101 Ground Floor Plan.dwg already exists in this folder.", dialog.Error);
            Assert.False(dialog.CanApply);
        }

        [Fact]
        public void NextAppliesTheChangesFirst_ThenLoadsTheNeighbourInSetOrder()
        {
            CadSheetSetFile file = _kit.Make();
            var dialog = new SheetSetRenameViewModel(file, _kit.Sheet("A-101"));

            Assert.False(dialog.HasPrevious);
            Assert.True(dialog.HasNext);

            dialog.Number = "A-111";
            Assert.True(dialog.Next());

            Assert.Equal("A-102", dialog.Number);
            Assert.Equal("First Floor Plan", dialog.SheetTitle);
            Assert.True(dialog.HasPrevious);
            Assert.Equal("A-111", _kit.Reopen().SheetSet.AllSheets().First().Number);

            Assert.True(dialog.Next());
            Assert.Equal("S-101", dialog.Number);
            Assert.False(dialog.HasNext);
            Assert.False(dialog.Next());

            Assert.True(dialog.Previous());
            Assert.Equal("A-102", dialog.Number);
            Assert.True(dialog.Previous());
            Assert.Equal("A-111", dialog.Number);
            Assert.False(dialog.HasPrevious);
        }

        [Fact]
        public void NextStaysOnTheSheet_WhereTheChangesAreRefused()
        {
            var dialog = new SheetSetRenameViewModel(_kit.Make(), _kit.Sheet("A-101"));
            dialog.LayoutName = "Plan #2";

            Assert.False(dialog.Next());

            Assert.Equal("A-101", dialog.Number);
            Assert.NotNull(dialog.Error);
        }

        [Fact]
        public void ADrawingOpenInTheSession_IsSaidToBeSavedRenamedAndReopened_WhereItsFileIsRenamed()
        {
            CadSheetSetFile file = _kit.Make();
            string drawing = DrawingOf("A-102");
            var dialog = new SheetSetRenameViewModel(
                file,
                _kit.Sheet("A-102"),
                new SheetSetDialogOptions
                {
                    IsOpenInSession = path => string.Equals(path, drawing, StringComparison.OrdinalIgnoreCase),
                }
            );

            Assert.Null(dialog.Notice);

            dialog.RenameFile = true;
            dialog.SheetTitle = "Other";

            Assert.Equal(
                "A-102 First Floor Plan.dwg is open in this session. It is saved, renamed and reopened under the new name at OK.",
                dialog.Notice
            );
        }

        [Fact]
        public void ASheetWhoseLayoutIsMissing_CanStillHaveItsNumberAndTitleChanged()
        {
            CadSheetSetFile file = _kit.Make();
            File.Delete(DrawingOf("A-102"));
            var dialog = new SheetSetRenameViewModel(file, _kit.Sheet("A-102"));
            dialog.RenameLayout = false;

            dialog.Number = "A-120";

            Assert.True(dialog.Apply());
            Assert.Equal("A-120", _kit.Reopen().SheetSet.AllSheets().Single(s => s.Id == dialog.SheetId).Number);
        }

        [Fact]
        public void ALockedSet_CannotRenameAnything()
        {
            CadSheetSetFile file = _kit.Make();
            var dialog = new SheetSetRenameViewModel(
                file,
                _kit.Sheet("A-102"),
                new SheetSetDialogOptions { ReadOnlyReason = "The sheet set is locked by h.le on WS-07." }
            );
            dialog.Number = "A-300";

            Assert.False(dialog.CanApply);
            Assert.False(dialog.Apply());
            Assert.Equal("A-102", _kit.Reopen().SheetSet.AllSheets().ElementAt(1).Number);
        }
    }
}
