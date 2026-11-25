using System;
using System.IO;
using System.Linq;
using OpenCAD2D.SheetSets;
using Xunit;

namespace OpenCAD2D.Wpf.UnitTest
{
    /// <summary>
    /// Sheet Set Properties (A12 4b, board A12SheetSetProperties): the grid shows the values of a set made in code, an edit is
    /// saved through <see cref="CadSheetSetFile.Change"/> in one write, a name the set cannot have is refused in the dialog's
    /// words with OK greyed, and a locked set opens read-only. No window is shown.
    /// </summary>
    public sealed class SheetSetPropertiesDialogTests : IDisposable
    {
        private readonly SheetSetDialogKit _kit = new SheetSetDialogKit();

        public void Dispose() => _kit.Dispose();

        [Fact]
        public void TheGridShowsTheValuesOfTheSet_InAutoCadsSectionsAndRows()
        {
            CadSheetSetFile file = _kit.Make();

            var dialog = new SheetSetPropertiesViewModel(file);

            Assert.Equal("Sheet Set Properties - Tower", dialog.Title);
            Assert.Equal("Tower", dialog.Row("Name").Text);
            Assert.Equal(file.Path, dialog.Row("Sheet set data file").Text);
            Assert.True(dialog.Row("Sheet set data file").IsReadOnly);
            Assert.Equal("Six storeys", dialog.Row("Description").Text);
            Assert.Equal("ET-2026-014", dialog.Row("Project number").Text);
            Assert.Equal("A1 sheet (" + _kit.Template + ")", dialog.Row("Sheet creation template").Text);
            Assert.Equal("No", dialog.Row("Prompt for template").Text);
            Assert.Equal("Northbank", dialog.Row("Client").Text);
            Assert.False(dialog.Row("Client").IsReadOnly);
            Assert.True(dialog.Row("Drawn By").IsReadOnly);
            Assert.Equal(
                new[]
                {
                    "Sheet Set",
                    "Project Control",
                    "Sheet Creation",
                    "Sheet Set Custom Properties",
                    "Sheet Custom Properties",
                },
                dialog.Rows.Where(r => r.IsSection).Select(r => r.Label).ToArray()
            );
        }

        [Fact]
        public void ClickingARow_ShowsItsDescriptionUnderTheGrid()
        {
            var dialog = new SheetSetPropertiesViewModel(_kit.Make());

            dialog.SelectedRow = dialog.Row("Sheet creation template");

            Assert.Equal("Sheet creation template", dialog.DescriptionTitle);
            Assert.Contains("Select Layout as Sheet Template", dialog.DescriptionText);
        }

        [Fact]
        public void OkSavesTheEditedValues_InOneWriteOfTheFile()
        {
            CadSheetSetFile file = _kit.Make();
            int revision = file.FileRevision;
            var dialog = new SheetSetPropertiesViewModel(file);

            dialog.Row("Name").Text = "Example Tower";
            dialog.Row("Description").Text = "Six-storey residential tower";
            dialog.Row("Project name").Text = "Tower works";
            dialog.Row("Client").Text = "Northbank Developments";
            dialog.Row("Prompt for template").Text = "Yes";

            Assert.True(dialog.Apply());

            Assert.Equal(revision + 1, file.FileRevision);
            CadSheetSet set = _kit.Reopen().SheetSet;
            Assert.Equal("Example Tower", set.Name);
            Assert.Equal("Six-storey residential tower", set.Description);
            Assert.Equal("Tower works", set.ProjectName);
            Assert.Equal("ET-2026-014", set.ProjectNumber);
            Assert.Equal("Northbank Developments", set.FindCustomProperty("Client").Value);
            Assert.Equal(CadSheetSetPropertyFlags.Set, set.FindCustomProperty("Client").Flags);
            Assert.True(set.PromptForTemplate);
        }

        [Fact]
        public void OkWithNothingChanged_WritesNothing()
        {
            CadSheetSetFile file = _kit.Make();
            int revision = file.FileRevision;
            var dialog = new SheetSetPropertiesViewModel(file);

            Assert.True(dialog.Apply());

            Assert.Equal(revision, file.FileRevision);
        }

        [Fact]
        public void ANameLeftEmpty_IsRefusedInRed_AndOkIsGreyed()
        {
            CadSheetSetFile file = _kit.Make();
            var dialog = new SheetSetPropertiesViewModel(file);

            dialog.Row("Name").Text = "  ";

            Assert.Equal("A sheet set needs a name.", dialog.Error);
            Assert.True(dialog.Row("Name").HasError);
            Assert.False(dialog.CanApply);
            Assert.False(dialog.Apply());
            Assert.Equal("Tower", _kit.Reopen().SheetSet.Name);

            dialog.Row("Name").Text = "Tower B";

            Assert.Null(dialog.Error);
            Assert.True(dialog.CanApply);
        }

        [Fact]
        public void ALockedSet_OpensReadOnly_WithTheLockSaidAndNothingSaved()
        {
            CadSheetSetFile file = _kit.Make();
            int revision = file.FileRevision;
            var dialog = new SheetSetPropertiesViewModel(
                file,
                new SheetSetDialogOptions { ReadOnlyReason = "The sheet set is locked by h.le on WS-07." }
            );

            dialog.Row("Name").Text = "Changed";

            Assert.True(dialog.IsReadOnly);
            Assert.Equal("Sheet Set Properties - Tower (read-only)", dialog.Title);
            Assert.Equal(
                "The sheet set is locked by h.le on WS-07. Nothing here can be changed.",
                dialog.ReadOnlyNotice
            );
            Assert.True(dialog.Rows.Where(r => !r.IsSection).All(r => r.IsReadOnly));
            Assert.False(dialog.CanApply);
            Assert.False(dialog.CanEditCustomProperties);
            Assert.False(dialog.Apply());
            Assert.Equal(revision, file.FileRevision);
        }

        [Fact]
        public void TheTemplateAndThePageSetupFile_ArePickedThroughTheButtonsOfTheirRows()
        {
            CadSheetSetFile file = _kit.Make();
            string overrides = _kit.Drawing(Path.Combine(_kit.Folder, "setups"), "Publish setups.dwt", "Plan");
            var dialog = new SheetSetPropertiesViewModel(file);
            CadDrawingReference current = null;
            dialog.PickTemplate = now =>
            {
                current = now;
                return new CadDrawingReference(
                    CadFileReference.FromPath(_kit.Template, file.Folder),
                    "Cover",
                    null,
                    null
                );
            };
            string asked = null;
            dialog.BrowseFile = (title, filter, now) =>
            {
                asked = title;
                return overrides;
            };

            Assert.True(dialog.Row("Sheet creation template").HasButton);
            Assert.True(dialog.Row("Sheet creation template").Browse());
            Assert.True(dialog.Row("Page setup overrides file").Browse());

            Assert.Equal("A1 sheet", current.Name);
            Assert.Equal("Cover (" + _kit.Template + ")", dialog.Row("Sheet creation template").Text);
            Assert.Equal(overrides, dialog.Row("Page setup overrides file").Text);
            Assert.NotNull(asked);

            Assert.True(dialog.Apply());

            CadSheetSet set = _kit.Reopen().SheetSet;
            Assert.Equal("Cover", set.Template.Name);
            Assert.Equal(overrides, set.PageSetupOverrides.Resolve(file.Folder), ignoreCase: true);
        }

        [Fact]
        public void TheModelViewFolders_AreOneRowOfFoldersSeparatedBySemicolons()
        {
            CadSheetSetFile file = _kit.Make();
            string models = Path.Combine(_kit.Folder, "Model");
            string library = Path.Combine(_kit.Folder, "Library");
            Directory.CreateDirectory(models);
            Directory.CreateDirectory(library);
            file.Change(set => set.AddResourceLocation(CadFileReference.FromPath(models, file.Folder)));
            var dialog = new SheetSetPropertiesViewModel(file);
            Assert.Equal(models, dialog.Row("Model view").Text);

            dialog.BrowseFolder = now => library;
            Assert.True(dialog.Row("Model view").Browse());
            Assert.Equal(models + "; " + library, dialog.Row("Model view").Text);
            Assert.True(dialog.Apply());

            Assert.Equal(
                new[] { models, library },
                _kit.Reopen().SheetSet.ResourceLocations.Select(l => l.Resolve(file.Folder)).ToArray()
            );
        }

        [Fact]
        public void EditCustomProperties_OpensTheListThroughItsHook_AndTheRowsShowWhatItChanged()
        {
            CadSheetSetFile file = _kit.Make();
            var dialog = new SheetSetPropertiesViewModel(file);
            dialog.ShowCustomProperties = shown =>
            {
                shown.Change(set =>
                    set.AddCustomProperty("Project Address", "12 River Road", CadSheetSetPropertyFlags.Set)
                );
                return true;
            };

            dialog.EditCustomProperties();

            Assert.Equal("12 River Road", dialog.Row("Project Address").Text);
        }

        [Fact]
        public void TheChangerOfTheCaller_IsHandedTheEdit_InPlaceOfTheFilesOwnChange()
        {
            CadSheetSetFile file = _kit.Make();
            int calls = 0;
            var dialog = new SheetSetPropertiesViewModel(
                file,
                new SheetSetDialogOptions
                {
                    Changer = (edit, drawings, follow) =>
                    {
                        calls++;
                        file.Change(edit);
                    },
                }
            );
            dialog.Row("Name").Text = "Tower B";

            Assert.True(dialog.Apply());

            Assert.Equal(1, calls);
            Assert.Equal("Tower B", _kit.Reopen().SheetSet.Name);
        }

        [Fact]
        public void WhereTheChangeIsRefused_TheDialogSaysWhy_AndStaysOpen()
        {
            CadSheetSetFile file = _kit.Make();
            var dialog = new SheetSetPropertiesViewModel(
                file,
                new SheetSetDialogOptions
                {
                    Changer = (edit, drawings, follow) => throw new InvalidOperationException("The set is busy."),
                }
            );
            dialog.Row("Name").Text = "Tower B";

            Assert.False(dialog.Apply());

            Assert.Equal("The set is busy.", dialog.Error);
        }
    }
}
