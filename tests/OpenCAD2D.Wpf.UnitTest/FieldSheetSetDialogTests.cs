using System;
using System.IO;
using System.Linq;
using GeometryHelper.Dxf;
using OpenCAD2D.SheetSets;
using Xunit;

namespace OpenCAD2D.Wpf.UnitTest
{
    /// <summary>
    /// The SheetSet category of the Field dialog, driven with no window: the category and its names, what each kind of field
    /// shows of the dialog (the tree and the properties of a sheet set field, the types of a placeholder, the custom property of
    /// a custom field), the tree choosing a node, and the code and preview that come of it.
    /// </summary>
    public sealed class FieldSheetSetDialogTests : IDisposable
    {
        private readonly string _folder = Path.Combine(
            Path.GetTempPath(),
            "gk",
            "fieldsheetsetdialog-" + Guid.NewGuid().ToString("N")
        );

        public FieldSheetSetDialogTests()
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

        /// <summary>The dialog of a drawing that is no sheet, given a set of its own: Tower with the subset Plans, the sheet A-101 "Ground floor" in it, the sheet G-001 "Cover" in the set, and the custom properties Client and Drawn By.</summary>
        private FieldDialogViewModel Dialog(out CadSheetSetFile file)
        {
            file = CadSheetSetFile.Create(Path.Combine(_folder, "Tower.dst"), "Tower", "A tower");
            file.Change(set =>
            {
                set.AddSubset("Plans").AddSheet("A-101", "Ground floor", null);
                set.AddSheet("G-001", "Cover", null);
                set.SetCustomProperty("Client", "Acme", CadSheetSetPropertyFlags.Set);
                set.SetCustomProperty("Drawn By", "DEF", CadSheetSetPropertyFlags.Sheet);
            });
            var request = new CadFieldRequest(
                new CadFieldContext(CadDocument.From(new DxfDatabase("AC1032"), Path.Combine(_folder, "plain.dxf")))
            );
            request.SetSheetSet(CadSheetSetFile.Open(file.Path));
            return new FieldDialogViewModel(request);
        }

        [Fact]
        public void TheSheetSetCategory_ListsItsNames_AndEachKindOfFieldShowsItsOwnOptions()
        {
            FieldDialogViewModel dialog = Dialog(out _);

            dialog.Category = "SheetSet";

            Assert.Equal(19, dialog.Names.Count);
            Assert.Equal("SheetSet", dialog.Names[0].Name);
            dialog.SelectedName = dialog.Names.Single(n => n.Name == "SheetSet");
            Assert.True(dialog.IsSheetSet);
            Assert.False(dialog.IsPlaceholder);
            Assert.False(dialog.HasCustomName);
            Assert.True(dialog.HasFormats);
            dialog.SelectedName = dialog.Names.Single(n => n.Name == "SheetSetPlaceholder");
            Assert.True(dialog.IsPlaceholder);
            Assert.False(dialog.IsSheetSet);
            Assert.False(dialog.HasCustomName);
            dialog.SelectedPlaceholder = "SheetCustom";
            Assert.True(dialog.HasCustomName);
            dialog.SelectedName = dialog.Names.Single(n => n.Name == "CurrentSheetCustom");
            Assert.True(dialog.HasCustomName);
            Assert.False(dialog.IsSheetSet);
            Assert.False(dialog.IsPlaceholder);
            dialog.SelectedName = dialog.Names.Single(n => n.Name == "CurrentSheetNumber");
            Assert.False(dialog.HasCustomName);
            Assert.True(dialog.HasFormats);
            Assert.Equal("%<\\AcSm Sheet.Number>%", dialog.Code);
        }

        [Fact]
        public void TheTreeOfASheetSetField_ChoosesTheNode_AndTheListOfPropertiesAndTheCodeFollowIt()
        {
            FieldDialogViewModel dialog = Dialog(out CadSheetSetFile file);
            dialog.Category = "SheetSet";
            dialog.SelectedName = dialog.Names.Single(n => n.Name == "SheetSet");

            FieldSheetNodeViewModel root = Assert.Single(dialog.SheetSetRoots);
            Assert.Equal("Tower", root.Text);
            Assert.Equal(new[] { "Plans", "G-001 - Cover" }, root.Children.Select(c => c.Text));
            Assert.True(root.IsSelected);
            Assert.Equal(string.Empty, dialog.SheetSetHint);
            Assert.Equal("Name", dialog.SelectedSheetSetProperty);

            FieldSheetNodeViewModel sheet = root.Children[0].Children.Single();
            sheet.IsSelected = true;

            Assert.Equal("A-101 - Ground floor", sheet.Text);
            Assert.Contains("Drawn By", dialog.SheetSetProperties);
            dialog.SelectedSheetSetProperty = "Title";
            Assert.EndsWith(".Title>%", dialog.Code);
            Assert.Contains(".Component(\"" + dialog.Request.SheetSetNode.Id + "\")", dialog.Code);
            Assert.Equal("Ground floor", dialog.Preview);
            Assert.True(dialog.CanOk);
        }

        [Fact]
        public void ADrawingWithNoSheetSet_ShowsWhyThereIsNoTree_AndOkIsOffUntilThereIsACode()
        {
            var dialog = new FieldDialogViewModel(
                new CadFieldRequest(
                    new CadFieldContext(CadDocument.From(new DxfDatabase("AC1032"), Path.Combine(_folder, "p.dxf")))
                )
            );
            dialog.Category = "SheetSet";
            dialog.SelectedName = dialog.Names.Single(n => n.Name == "SheetSet");

            Assert.Empty(dialog.SheetSetRoots);
            Assert.Contains("not a sheet of a sheet set", dialog.SheetSetHint);
            Assert.False(dialog.CanOk);

            dialog.SelectedName = dialog.Names.Single(n => n.Name == "CurrentSheetNumber");

            Assert.True(dialog.CanOk);
            Assert.Equal("####", dialog.Preview);
        }

        [Fact]
        public void ThePlaceholderAndCustomChoices_SetTheirCodes()
        {
            FieldDialogViewModel dialog = Dialog(out _);
            dialog.Category = "SheetSet";
            dialog.SelectedName = dialog.Names.Single(n => n.Name == "SheetSetPlaceholder");

            Assert.Equal(9, dialog.PlaceholderTypes.Count);
            dialog.SelectedPlaceholder = "ViewNumber";
            Assert.Equal("%<\\AcSm ?View.Number>%", dialog.Code);
            Assert.Equal("ViewNumber", dialog.Preview);
            dialog.SelectedPlaceholder = "SheetCustom";
            Assert.Equal(new[] { "Drawn By" }, dialog.CustomNames);
            dialog.SelectedCustomName = "Drawn By";
            Assert.Equal("%<\\AcSm ?Sheet.Drawn By>%", dialog.Code);

            dialog.SelectedName = dialog.Names.Single(n => n.Name == "CurrentSheetSetCustom");
            Assert.Equal(new[] { "Client" }, dialog.CustomNames);
            dialog.SelectedCustomName = "Client";
            Assert.Equal("%<\\AcSm SheetSet.Client>%", dialog.Code);
        }
    }
}
