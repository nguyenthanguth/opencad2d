using System;
using System.IO;
using System.Linq;
using GeometryHelper.Dxf;
using GeometryHelper.Geometry;
using Xunit;

namespace OpenCAD2D.Wpf.UnitTest
{
    /// <summary>
    /// The edit of a reference in place in the window: Edit Block In-place of the object menu of a block reference starts REFEDIT
    /// on the reference selected, and the commands of the session are described for the Shortcuts page.
    /// </summary>
    public sealed class RefEditWindowTests
    {
        /// <summary>A metric drawing with the block BOX (a line from (0, 0) to (100, 0)) placed at (1 000, 0); the handle of the INSERT.</summary>
        private static MainViewModel Open(out string insert)
        {
            DxfDatabase database = CadTemplate.CreateMetric();
            var box = new DxfBlockTableRecord { Name = "BOX", Origin = new GeoPoint3(0, 0, 0) };
            database.BlockTable.Add(box);
            box.AppendEntity(new DxfLine(new GeoPoint3(0, 0, 0), new GeoPoint3(100, 0, 0)));
            var reference = new DxfBlockReference(new GeoPoint3(1000, 0, 0), box);
            database.ModelSpace.AppendEntity(reference);
            insert = reference.Handle;
            var model = new MainViewModel();
            model.OpenDocument(CadDocument.From(database, "box.dxf"));
            return model;
        }

        [Fact]
        public void EditBlockInPlace_OfTheObjectMenuOfABlockReference_StartsRefeditOnIt()
        {
            MainViewModel model = Open(out string insert);
            model.Document.Selection.Set(new[] { insert });

            CadMenuItemViewModel item = model.CanvasMenu().Single(i => i.Header == "Edit Block In-place");

            Assert.True(item.IsEnabled);
            Assert.Equal('i', item.AccessKey);
            item.Command.Execute(null);
            Assert.Equal("Enter object selection method [All/Nested] <All>:", model.Prompt);

            model.Execute(string.Empty);

            Assert.NotNull(model.Document.RefEdit);
            Assert.Equal("BOX", model.Document.RefEdit.Name);
        }

        [Fact]
        public void TheSaveOfTheWindow_WhileAnEditIsOpen_IsRefusedAsQsaveIs_AndWritesNothing()
        {
            using (var folder = new TestFolder())
            {
                DxfDatabase database = CadTemplate.CreateMetric();
                var box = new DxfBlockTableRecord { Name = "BOX", Origin = new GeoPoint3(0, 0, 0) };
                database.BlockTable.Add(box);
                box.AppendEntity(new DxfLine(new GeoPoint3(0, 0, 0), new GeoPoint3(100, 0, 0)));
                var reference = new DxfBlockReference(new GeoPoint3(1000, 0, 0), box);
                database.ModelSpace.AppendEntity(reference);
                string path = folder.Write("box.dxf", database);
                var model = new MainViewModel();
                Assert.True(model.Open(path));
                Assert.True(model.Document.OpenRefEdit(new[] { reference.Handle }, null).Succeeded);
                DateTime written = File.GetLastWriteTimeUtc(path);
                byte[] before = File.ReadAllBytes(path);

                Assert.False(model.Save());
                Assert.False(model.SaveAs(Path.Combine(folder.FullPath, "other.dxf")));

                Assert.Equal("** Drawing save not allowed during reference editing **", model.History.Last());
                Assert.Equal(before, File.ReadAllBytes(path));
                Assert.Equal(written, File.GetLastWriteTimeUtc(path));
                Assert.False(File.Exists(Path.Combine(folder.FullPath, "other.dxf")));
                Assert.NotNull(model.Document.RefEdit);
            }
        }

        [Fact]
        public void TheCommandsOfTheEdit_AreListedWithADescription_OnTheShortcutsPage()
        {
            var model = new MainViewModel();
            GlobalSettingsViewModel dialog = model.CreateGlobalSettings("Shortcuts");
            dialog.ShortcutSearch = string.Empty;

            foreach (string command in new[] { "REFEDIT", "-REFEDIT", "REFSET", "REFCLOSE" })
            {
                ShortcutRowViewModel row = dialog.Shortcuts.Single(r => r.Command == command);
                Assert.False(string.IsNullOrWhiteSpace(row.Description), command);
            }
        }
    }
}
