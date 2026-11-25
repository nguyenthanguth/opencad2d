using System;
using System.IO;
using System.Linq;
using OpenCAD2D.SheetSets;
using Xunit;

namespace OpenCAD2D.Wpf.UnitTest
{
    /// <summary>
    /// Import Layouts as Sheets (A12 4b, board A12ImportLayouts): empty until a drawing is chosen; the layouts of the drawing
    /// in tab order, each checked unless the set refuses it (a # in its name, a sheet of this set already, a sheet of another
    /// set), the reason said in the dialog's words; Import Checked adds a sheet for each layout checked at the end of the
    /// subset, with no number, the title the layout's name (prefixed with the file name where asked). Drawings are made in
    /// code in a temporary folder.
    /// </summary>
    public sealed class SheetSetImportDialogTests : IDisposable
    {
        private readonly SheetSetDialogKit _kit = new SheetSetDialogKit();

        public void Dispose() => _kit.Dispose();

        private string Foundation(params string[] layouts) =>
            _kit.Drawing(Path.Combine(_kit.Folder, "drawings"), "S-201 Slab.dwg", layouts);

        private SheetSetImportLayoutsViewModel Dialog(CadSheetSetFile file, string subset, string drawing)
        {
            var dialog = new SheetSetImportLayoutsViewModel(file, subset == null ? file.SheetSet : _kit.Subset(subset));
            dialog.BrowseFile = (title, filter, now) => drawing;
            return dialog;
        }

        [Fact]
        public void BeforeADrawingIsChosen_TheListIsEmpty_AndImportCheckedIsGreyed()
        {
            var dialog = new SheetSetImportLayoutsViewModel(_kit.Make(), _kit.Subset("Structural"));

            Assert.Equal("Import Layouts as Sheets", dialog.Title);
            Assert.Empty(dialog.Layouts);
            Assert.False(dialog.CanApply);
            Assert.False(dialog.PrefixTitleWithFileName);
        }

        [Fact]
        public void BrowseForDrawings_ListsTheLayoutsInTabOrder_AllCheckedThatTheSetAccepts()
        {
            CadSheetSetFile file = _kit.Make();
            string drawing = Foundation("Slab plan", "Pile layout", "Section");
            SheetSetImportLayoutsViewModel dialog = Dialog(file, "Structural", drawing);

            dialog.BrowseDrawing();

            Assert.Equal(drawing, dialog.DrawingFile);
            Assert.Equal(
                new[] { "Slab plan", "Pile layout", "Section" },
                dialog.Layouts.Select(l => l.LayoutName).ToArray()
            );
            Assert.All(dialog.Layouts, l => Assert.Equal("S-201 Slab.dwg", l.DrawingName));
            Assert.All(dialog.Layouts, l => Assert.True(l.IsChecked));
            Assert.True(dialog.CanApply);
            Assert.Null(dialog.Notice);
        }

        [Fact]
        public void ALayoutWithAHashInItsName_IsRefused_UncheckedAndNotChangeable_WithTheReasonShown()
        {
            CadSheetSetFile file = _kit.Make();
            SheetSetImportLayoutsViewModel dialog = Dialog(file, "Structural", Foundation("Slab plan", "Old#issue"));

            dialog.BrowseDrawing();

            SheetSetImportRow refused = dialog.Layouts.Single(l => l.LayoutName == "Old#issue");
            Assert.True(refused.IsRefused);
            Assert.False(refused.IsChecked);
            refused.IsChecked = true;
            Assert.False(refused.IsChecked);
            Assert.Equal("The layout name \"Old#issue\" holds a #, which a sheet set cannot use.", dialog.Notice);
            Assert.True(dialog.CanApply);
        }

        [Fact]
        public void ALayoutThatIsASheetOfThisSetAlready_AndOneOfAnotherSet_AreRefused()
        {
            CadSheetSetFile file = _kit.Make();
            string own = _kit.Sheet("A-101").Layout.File.Resolve(file.Folder);
            SheetSetImportLayoutsViewModel dialog = Dialog(file, "Structural", own);

            dialog.BrowseDrawing();

            Assert.True(dialog.Layouts.Single().IsRefused);
            Assert.Contains("already a sheet of the sheet set \"Tower\"", dialog.Notice);
            Assert.False(dialog.CanApply);

            string elsewhere = Foundation("Slab plan");
            Directory.CreateDirectory(Path.Combine(_kit.Folder, "other"));
            CadSheetSetFile other = CadSheetSetFile.Create(
                Path.Combine(_kit.Folder, "other", "Riverside Clinic.dst"),
                "Riverside Clinic",
                null,
                SheetSetDialogKit.Counter()
            );
            other.Change(set => set.ImportLayouts(elsewhere, new[] { "Slab plan" }, false));
            dialog = Dialog(file, "Structural", elsewhere);

            dialog.BrowseDrawing();

            Assert.True(dialog.Layouts.Single().IsRefused);
            Assert.Contains("Riverside Clinic", dialog.Notice);
        }

        [Fact]
        public void ImportChecked_AddsASheetForEachLayoutChecked_AtTheEndOfTheSubset_WithNoNumber()
        {
            CadSheetSetFile file = _kit.Make();
            string drawing = Foundation("Slab plan", "Pile layout", "Section");
            SheetSetImportLayoutsViewModel dialog = Dialog(file, "Structural", drawing);
            dialog.BrowseDrawing();
            dialog.Layouts.Single(l => l.LayoutName == "Section").IsChecked = false;

            Assert.True(dialog.Apply());

            CadSheetSubset structural = _kit.Reopen().SheetSet.Subsets.Single(s => s.Name == "Structural");
            Assert.Equal(
                new[] { "Foundation Plan", "Slab plan", "Pile layout" },
                structural.Sheets.Select(s => s.Title).ToArray()
            );
            Assert.Equal(
                new[] { "S-101", string.Empty, string.Empty },
                structural.Sheets.Select(s => s.Number).ToArray()
            );
            Assert.Equal(new[] { structural.Sheets[1].Id, structural.Sheets[2].Id }, dialog.ImportedSheetIds.ToArray());
            Assert.NotNull(CadSheetSetHint.Read(drawing));
        }

        [Fact]
        public void PrefixSheetTitlesWithFileName_PutsTheDrawingNameBeforeTheLayoutName()
        {
            CadSheetSetFile file = _kit.Make();
            SheetSetImportLayoutsViewModel dialog = Dialog(file, null, Foundation("Slab plan"));
            dialog.BrowseDrawing();
            dialog.PrefixTitleWithFileName = true;

            Assert.True(dialog.Apply());

            Assert.Equal("S-201 Slab - Slab plan", _kit.Reopen().SheetSet.Sheets.Single().Title);
        }

        [Fact]
        public void WithNothingChecked_ImportCheckedIsGreyed()
        {
            CadSheetSetFile file = _kit.Make();
            SheetSetImportLayoutsViewModel dialog = Dialog(file, "Structural", Foundation("Slab plan"));
            dialog.BrowseDrawing();

            dialog.Layouts[0].IsChecked = false;

            Assert.False(dialog.CanApply);
            Assert.False(dialog.Apply());
        }

        [Fact]
        public void ADrawingThatIsNotThere_IsSaidSo_AndTheListStaysEmpty()
        {
            var dialog = new SheetSetImportLayoutsViewModel(_kit.Make(), _kit.Subset("Structural"));
            string missing = Path.Combine(_kit.Folder, "nothing.dwg");
            dialog.BrowseFile = (title, filter, now) => missing;

            dialog.BrowseDrawing();

            Assert.Equal("The drawing " + missing + " is not there.", dialog.Error);
            Assert.Empty(dialog.Layouts);
        }

        [Fact]
        public void ALockedSet_CannotImport()
        {
            CadSheetSetFile file = _kit.Make();
            var dialog = new SheetSetImportLayoutsViewModel(
                file,
                _kit.Subset("Structural"),
                new SheetSetDialogOptions { ReadOnlyReason = "The sheet set is locked by h.le on WS-07." }
            );
            dialog.BrowseFile = (title, filter, now) => Foundation("Slab plan");
            dialog.BrowseDrawing();

            Assert.False(dialog.CanApply);
            Assert.False(dialog.Apply());
        }
    }
}
