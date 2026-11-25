using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using OpenCAD2D.SheetSets;
using Xunit;
using static OpenCAD2D.Wpf.UnitTest.PythonWindowKit;

namespace OpenCAD2D.Wpf.UnitTest
{
    /// <summary>
    /// The palette's menus open the sheet set dialogs of A12 4b: each kind of <see cref="SheetSetDialog"/> asked for on a row
    /// opens the view model of its dialog on that row's part of the set, a locked set opens it read-only, and a change made in a
    /// dialog goes through the palette's own change, so that the tree is made again and a drawing open in the app is saved,
    /// closed and opened again under the name a rename gives it. No dialog is shown: the test takes the place of the call
    /// that shows it.
    /// </summary>
    [Collection("SheetSetDialogPresenter")]
    public sealed class SheetSetPaletteDialogsTests : IDisposable
    {
        private readonly SheetSetDialogKit _kit = new SheetSetDialogKit();

        public void Dispose()
        {
            SheetSetDialogWindow.Presenter = null;
            _kit.Dispose();
        }

        private SheetSetManagerViewModel Palette(out MainViewModel model)
        {
            _kit.Make();
            model = new MainViewModel();
            var palette = new SheetSetManagerViewModel(model);
            Assert.True(palette.OpenSet(_kit.Dst));
            SheetSetPaletteDialogs.Attach(palette, () => null);
            return palette;
        }

        private static SheetSetNodeViewModel Node(SheetSetManagerViewModel palette, string text) =>
            palette.Rows.Single(r => r.Text == text);

        private static void Click(SheetSetManagerViewModel palette, SheetSetNodeViewModel node, string header) =>
            palette.MenuFor(node).Single(i => i.Header == header).Command.Execute(null);

        /// <summary>Takes the place of showing a dialog: records its view model and gives it to what the test does with it, answering Cancel.</summary>
        private static List<SheetSetDialogViewModel> Record(Func<SheetSetDialogViewModel, bool> act = null)
        {
            var shown = new List<SheetSetDialogViewModel>();
            SheetSetDialogWindow.Presenter = window =>
            {
                shown.Add(window.Model);
                return act != null && act(window.Model);
            };
            return shown;
        }

        [Fact]
        public void EachMenuItemThatAsksForADialog_OpensTheViewModelOfThatDialog_OnItsRow()
        {
            OnWindowThread(() =>
            {
                SheetSetManagerViewModel palette = Palette(out MainViewModel model);
                List<SheetSetDialogViewModel> shown = Record();
                SheetSetNodeViewModel sheet = Node(palette, "A-102 - First Floor Plan");
                SheetSetNodeViewModel subset = Node(palette, "Architectural");

                Click(palette, palette.Root, "Properties…");
                Click(palette, subset, "Properties…");
                Click(palette, sheet, "Properties…");
                Click(palette, subset, "Rename Subset…");
                Click(palette, subset, "New Subset…");
                Click(palette, sheet, "New Sheet…");
                Click(palette, subset, "Import Layout as Sheet…");
                Click(palette, sheet, "Rename & Renumber…");

                Assert.Equal(
                    new[]
                    {
                        typeof(SheetSetPropertiesViewModel),
                        typeof(SheetSetSubsetPropertiesViewModel),
                        typeof(SheetSetSheetPropertiesViewModel),
                        typeof(SheetSetSubsetPropertiesViewModel),
                        typeof(SheetSetSubsetPropertiesViewModel),
                        typeof(SheetSetNewSheetViewModel),
                        typeof(SheetSetImportLayoutsViewModel),
                        typeof(SheetSetRenameViewModel),
                    },
                    shown.Select(s => s.GetType()).ToArray()
                );
                Assert.False(((SheetSetSubsetPropertiesViewModel)shown[1]).IsNew);
                Assert.Equal("Architectural", ((SheetSetSubsetPropertiesViewModel)shown[1]).Row("Subset name").Text);
                Assert.Equal("Architectural", ((SheetSetSubsetPropertiesViewModel)shown[3]).Row("Subset name").Text);
                Assert.True(((SheetSetSubsetPropertiesViewModel)shown[4]).IsNew);
                Assert.Equal("Sheet Properties - A-102 First Floor Plan", shown[2].Title);
                Assert.Equal("A-102", ((SheetSetRenameViewModel)shown[7]).Number);
                Assert.DoesNotContain(model.History, line => line.Contains("not built yet"));
            });
        }

        [Fact]
        public void ALockedSet_OpensTheDialogsReadOnly_WithTheLockSaid()
        {
            OnWindowThread(() =>
            {
                SheetSetManagerViewModel palette = Palette(out _);
                File.SetAttributes(_kit.Dst, FileAttributes.ReadOnly);
                try
                {
                    Assert.True(palette.OpenSet(_kit.Dst));
                    Assert.True(palette.IsLocked);
                    List<SheetSetDialogViewModel> shown = Record();

                    Click(palette, palette.Root, "Properties…");

                    SheetSetDialogViewModel dialog = Assert.Single(shown);
                    Assert.True(dialog.IsReadOnly);
                    Assert.Contains("Read-only", dialog.ReadOnlyNotice);
                    Assert.EndsWith("Nothing here can be changed.", dialog.ReadOnlyNotice);
                }
                finally
                {
                    File.SetAttributes(_kit.Dst, FileAttributes.Normal);
                }
            });
        }

        [Fact]
        public void ANewSubsetMadeInItsDialog_ShowsInTheTree_ThroughThePalettesOwnChange()
        {
            OnWindowThread(() =>
            {
                SheetSetManagerViewModel palette = Palette(out _);
                Record(dialog =>
                {
                    var subset = (SheetSetSubsetPropertiesViewModel)dialog;
                    subset.Row("Subset name").Text = "Interiors";
                    return dialog.Apply();
                });

                Click(palette, Node(palette, "Architectural"), "New Subset…");

                Assert.Contains("Interiors", palette.Rows.Select(r => r.Text));
                Assert.Equal("Interiors", _kit.Reopen().SheetSet.Subsets[0].Subsets.Single().Name);
            });
        }

        [Fact]
        public void AFileRenamedInTheDialog_ClosesTheOpenDrawing_AndOpensItAgainUnderItsNewName()
        {
            OnWindowThread(() =>
            {
                SheetSetManagerViewModel palette = Palette(out MainViewModel model);
                string old = _kit.Sheet("A-102").Layout.File.Resolve(_kit.File.Folder);
                Assert.True(palette.OpenSheets(new[] { Node(palette, "A-102 - First Floor Plan") }, false));
                Assert.NotNull(model.OpenDocumentOf(old));
                Record(dialog =>
                {
                    var rename = (SheetSetRenameViewModel)dialog;
                    rename.RenameFile = true;
                    Assert.Equal(
                        "A-102 First Floor Plan.dwg is open in this session. It is saved, renamed and reopened under the new name at OK.",
                        rename.Notice
                    );
                    rename.SheetTitle = "Second Floor Plan";
                    return dialog.Apply();
                });

                Click(palette, Node(palette, "A-102 - First Floor Plan"), "Rename & Renumber…");

                string renamed = Path.Combine(Path.GetDirectoryName(old), "A-102 Second Floor Plan.dwg");
                Assert.False(File.Exists(old));
                Assert.True(File.Exists(renamed));
                Assert.Null(model.OpenDocumentOf(old));
                Assert.NotNull(model.OpenDocumentOf(renamed));
                Assert.Contains("A-102 - Second Floor Plan", palette.Rows.Select(r => r.Text));
            });
        }

        [Fact]
        public void AFileNameTheFolderHasAlready_IsRefusedInTheDialog_AndNothingIsRenamed()
        {
            OnWindowThread(() =>
            {
                SheetSetManagerViewModel palette = Palette(out _);
                string folder = Path.Combine(_kit.SetFolder, "Architectural");
                SheetSetDialogViewModel refused = null;
                Record(dialog =>
                {
                    var rename = (SheetSetRenameViewModel)dialog;
                    // A file of the new name is in the folder already, so the dialog refuses before the core is asked.
                    File.Copy(Path.Combine(folder, "A-101 Ground Floor Plan.dwg"), Path.Combine(folder, "taken.dwg"));
                    rename.RenameFile = true;
                    rename.FileName = "taken";
                    Assert.False(rename.CanApply);
                    refused = dialog;
                    return false;
                });

                Click(palette, Node(palette, "A-102 - First Floor Plan"), "Rename & Renumber…");

                Assert.Equal("taken.dwg already exists in this folder.", ((SheetSetRenameViewModel)refused).Error);
                Assert.True(File.Exists(Path.Combine(folder, "A-102 First Floor Plan.dwg")));
            });
        }
    }
}
