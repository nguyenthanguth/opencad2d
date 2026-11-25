using System;
using System.Linq;
using OpenCAD2D.SheetSets;
using Xunit;

namespace OpenCAD2D.Wpf.UnitTest
{
    /// <summary>
    /// Custom Properties and Add Custom Property (A12 4b, board A12CustomProperties): the list of the set's properties with
    /// their owners, Add and Delete staged until OK writes the .dst once, a name refused in the dialog's words, and the
    /// default of a property of every sheet copied into every sheet. Every set and drawing is made in code.
    /// </summary>
    public sealed class SheetSetCustomPropertiesDialogTests : IDisposable
    {
        private readonly SheetSetDialogKit _kit = new SheetSetDialogKit();

        public void Dispose() => _kit.Dispose();

        [Fact]
        public void TheListShowsNameDefaultValueAndOwner_OfEveryPropertyOfTheSet()
        {
            var dialog = new SheetSetCustomPropertiesViewModel(_kit.Make());

            Assert.Equal("Custom Properties", dialog.Title);
            Assert.Equal(new[] { "Client", "Drawn By" }, dialog.Items.Select(i => i.Name).ToArray());
            Assert.Equal("Northbank", dialog.Items[0].DefaultValue);
            Assert.Equal("Sheet Set", dialog.Items[0].OwnerText);
            Assert.Equal("Sheet", dialog.Items[1].OwnerText);
        }

        [Fact]
        public void AddIsStaged_NothingIsWrittenUntilOk_AndOkCopiesASheetPropertyIntoEverySheet()
        {
            CadSheetSetFile file = _kit.Make();
            int revision = file.FileRevision;
            var dialog = new SheetSetCustomPropertiesViewModel(file);

            dialog.Add("Approved By", "T. Tran", CadSheetSetPropertyFlags.Sheet);
            dialog.Add("Project Address", "12 River Road", CadSheetSetPropertyFlags.Set);

            Assert.Equal(4, dialog.Items.Count);
            Assert.Equal(revision, file.FileRevision);
            Assert.Null(_kit.Reopen().SheetSet.FindCustomProperty("Approved By"));

            Assert.True(dialog.Apply());

            Assert.Equal(revision + 1, file.FileRevision);
            CadSheetSet set = _kit.Reopen().SheetSet;
            Assert.Equal("12 River Road", set.FindCustomProperty("Project Address").Value);
            Assert.Equal(CadSheetSetPropertyFlags.Sheet, set.FindCustomProperty("Approved By").Flags);
            Assert.Equal(
                new[] { "T. Tran", "T. Tran", "T. Tran" },
                set.AllSheets().Select(s => s.FindCustomProperty("Approved By").Value).ToArray()
            );
            Assert.Null(set.AllSheets().First().FindCustomProperty("Project Address"));
        }

        [Fact]
        public void ANameIsRefused_InTheWordsOfTheDialog_WhenEmptyUsedOrUnfit()
        {
            var dialog = new SheetSetCustomPropertiesViewModel(_kit.Make());

            Assert.Equal("A custom property needs a name.", dialog.WhyNotName("  "));
            Assert.Equal(
                "A custom property named Client already exists in this sheet set.",
                dialog.WhyNotName("client")
            );
            Assert.Equal(
                "The name \"Plan.Sheet\" holds a dot or a double quote, which a field cannot carry.",
                dialog.WhyNotName("Plan.Sheet")
            );
            Assert.Equal(
                "The name \"Say \"hi\"\" holds a dot or a double quote, which a field cannot carry.",
                dialog.WhyNotName("Say \"hi\"")
            );
            Assert.Null(dialog.WhyNotName("Approved By"));
            Assert.Throws<ArgumentException>(() => dialog.Add("Client", "x", CadSheetSetPropertyFlags.Set));
        }

        [Fact]
        public void DeleteAsksFirst_ThenTakesThePropertyOutOfTheSetAndOfEverySheet_AtOk()
        {
            CadSheetSetFile file = _kit.Make();
            file.Change(set =>
                set.AllSheets().First().SetCustomProperty("Drawn By", "T. Tran", CadSheetSetPropertyFlags.Sheet)
            );
            var dialog = new SheetSetCustomPropertiesViewModel(file);
            string asked = null;
            dialog.ConfirmDelete = name =>
            {
                asked = name;
                return false;
            };
            dialog.SelectedItem = dialog.Items.Single(i => i.Name == "Drawn By");

            Assert.False(dialog.DeleteSelected());
            Assert.Equal("Drawn By", asked);
            Assert.Equal(2, dialog.Items.Count);

            dialog.ConfirmDelete = name => true;
            Assert.True(dialog.DeleteSelected());
            Assert.Single(dialog.Items);
            Assert.NotNull(_kit.Reopen().SheetSet.FindCustomProperty("Drawn By"));

            Assert.True(dialog.Apply());

            CadSheetSet set = _kit.Reopen().SheetSet;
            Assert.Null(set.FindCustomProperty("Drawn By"));
            Assert.All(set.AllSheets(), s => Assert.Null(s.FindCustomProperty("Drawn By")));
        }

        [Fact]
        public void APropertyIsNeverEdited_ItIsDeletedAndAddedAgain_WithTheNewDefault()
        {
            CadSheetSetFile file = _kit.Make();
            var dialog = new SheetSetCustomPropertiesViewModel(file);
            dialog.ConfirmDelete = name => true;
            dialog.SelectedItem = dialog.Items.Single(i => i.Name == "Client");

            Assert.True(dialog.DeleteSelected());
            dialog.Add("Client", "Southbank", CadSheetSetPropertyFlags.Set);
            Assert.True(dialog.Apply());

            Assert.Equal("Southbank", _kit.Reopen().SheetSet.FindCustomProperty("Client").Value);
        }

        [Fact]
        public void CancelLeavesTheFileAlone_AndALockedSetCannotAddOrDelete()
        {
            CadSheetSetFile file = _kit.Make();
            int revision = file.FileRevision;
            var dialog = new SheetSetCustomPropertiesViewModel(
                file,
                new SheetSetDialogOptions { ReadOnlyReason = "The sheet set is locked by h.le on WS-07." }
            );

            Assert.False(dialog.CanAdd);
            Assert.False(dialog.CanDelete);
            Assert.False(dialog.Apply());
            Assert.Equal(revision, file.FileRevision);
        }

        [Fact]
        public void AddCustomProperty_TakesTheNameTheDefaultAndTheOwner_AndGreysOkWhileANameIsRefused()
        {
            var list = new SheetSetCustomPropertiesViewModel(_kit.Make());
            var dialog = new SheetSetAddPropertyViewModel(list);

            Assert.Equal("Add Custom Property", dialog.Title);
            Assert.Equal(CadSheetSetPropertyFlags.Set, dialog.Owner);
            Assert.False(dialog.CanApply);

            dialog.Name = "client";
            Assert.Equal("A custom property named Client already exists in this sheet set.", dialog.Error);
            Assert.False(dialog.CanApply);

            dialog.Name = "Approved By";
            dialog.DefaultValue = "T. Tran";
            dialog.Owner = CadSheetSetPropertyFlags.Sheet;
            Assert.Null(dialog.Error);
            Assert.True(dialog.CanApply);
            Assert.True(dialog.Apply());

            Assert.Equal("Approved By", list.Items.Last().Name);
            Assert.Equal("Sheet", list.Items.Last().OwnerText);
            Assert.Equal("T. Tran", list.Items.Last().DefaultValue);
        }
    }
}
