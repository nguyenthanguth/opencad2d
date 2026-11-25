using System;
using System.IO;
using System.Linq;
using GeometryHelper.Dxf;
using GeometryHelper.Geometry;
using Xunit;

namespace OpenCAD2D.Wpf.UnitTest
{
    /// <summary>
    /// The window's drawings as a plugin reaches them (A9): <see cref="CadDocuments"/> over the view model, so that a call does
    /// what File &gt; Open, QNEW, a click on a row of the DRAWINGS list and the x of a row do, and a Remote call does. The
    /// settings file of every view model is in the test's own folder, and no dialog is shown: the questions a user is asked
    /// are set to fail the test if they are asked.
    /// </summary>
    public class DocumentManagerWindowTests
    {
        private static MainViewModel Window(TestFolder folder)
        {
            var model = new MainViewModel { SettingsPath = Path.Combine(folder.FullPath, "settings.json") };
            model.AskSaveChanges = d => throw new InvalidOperationException("A plugin's close asked " + d.Title + ".");
            model.AskSaveAll = d => throw new InvalidOperationException("A plugin's close asked all.");
            return model;
        }

        private static void Draw(CadDocument document)
        {
            document.ApplyEdit("draw", null, s =>
                s.Append(document.ModelSpace, new DxfLine(new GeoPoint3(0, 0, 0), new GeoPoint3(500, 0, 0)))
            );
        }

        [Fact]
        public void TheWindowsDrawings_AreListedByFileName_WithTheOneShownActive_ForEveryContextAndSession()
        {
            using (var folder = new TestFolder())
            {
                MainViewModel model = Window(folder);
                CadDocuments none = model.PluginContext().Documents;
                Assert.True(none.HasWindow);
                Assert.Empty(none);
                Assert.Null(none.Active);

                model.Open(folder.Write("room.dxf", TestFiles.Room(out _, out _)));
                model.Open(folder.Write("plan.dxf", new DxfDatabase("AC1027")));

                CadDocuments documents = model.PluginContext().Documents;

                Assert.Same(none, documents);
                Assert.Same(documents, model.Session.Documents);
                Assert.Equal(new[] { "plan.dxf", "room.dxf" }, documents.Select(d => d.Title));
                Assert.Same(model.Document, documents.Active);
                Assert.Equal("plan.dxf", documents.Active.Title);
            }
        }

        [Fact]
        public void Open_ReadsAFileAndShowsIt_AndAFileOpenAlreadyIsShownNotReadTwice()
        {
            using (var folder = new TestFolder())
            {
                MainViewModel model = Window(folder);
                string room = folder.Write("room.dxf", TestFiles.Room(out _, out _));
                string plan = folder.Write("plan.dxf", new DxfDatabase("AC1027"));
                model.Open(plan);
                CadDocuments documents = model.PluginContext().Documents;

                CadDocument opened = documents.Open(room);

                Assert.Same(model.Document, opened);
                Assert.Equal("room.dxf", opened.Title);
                Assert.Contains(model.History, l => l.StartsWith("Opened room.dxf:", StringComparison.Ordinal));
                Assert.Equal(2, documents.Count);

                CadDocument planAgain = documents.Open(plan);

                Assert.Equal("plan.dxf", planAgain.Title);
                Assert.Same(model.Document, planAgain);
                Assert.Equal(2, documents.Count);
                Assert.Same(opened, documents.Single(d => d.Title == "room.dxf"));
            }
        }

        [Fact]
        public void Open_OfAFileThatIsNotThere_ThrowsWithWhatTheCommandLineSays_AndTheShownDrawingStays()
        {
            using (var folder = new TestFolder())
            {
                MainViewModel model = Window(folder);
                model.Open(folder.Write("plan.dxf", new DxfDatabase("AC1027")));
                CadDocument shown = model.Document;
                CadDocuments documents = model.PluginContext().Documents;

                InvalidOperationException failed = Assert.Throws<InvalidOperationException>(() =>
                    documents.Open(Path.Combine(folder.FullPath, "missing.dxf"))
                );

                Assert.Contains("Cannot open", failed.Message, StringComparison.Ordinal);
                Assert.Same(shown, model.Document);
                Assert.Single(documents);
            }
        }

        [Fact]
        public void Add_StartsANewDrawing_OfTheAppsOwnTemplate_OrOfOneGiven_AndShowsIt()
        {
            using (var folder = new TestFolder())
            {
                MainViewModel model = Window(folder);
                CadDocuments documents = model.PluginContext().Documents;

                CadDocument own = documents.Add();

                Assert.Same(model.Document, own);
                Assert.Equal("Drawing1.dwg", own.Title);
                Assert.False(own.HasFile);

                string template = folder.Write("plan.dxf", TestFiles.Room(out _, out _));
                CadDocument fromTemplate = documents.Add(template);

                Assert.Same(model.Document, fromTemplate);
                Assert.Equal("Drawing2.dwg", fromTemplate.Title);
                Assert.True(fromTemplate.EntityCount > 0);
                Assert.Equal(2, documents.Count);

                InvalidOperationException bad = Assert.Throws<InvalidOperationException>(() =>
                    documents.Add(Path.Combine(folder.FullPath, "none.dwt"))
                );

                Assert.Contains("Cannot read the template", bad.Message, StringComparison.Ordinal);
                Assert.Equal(2, documents.Count);
            }
        }

        [Fact]
        public void SettingTheActiveDrawing_ShowsIt_AsAClickOnItsRowDoes()
        {
            using (var folder = new TestFolder())
            {
                MainViewModel model = Window(folder);
                model.Open(folder.Write("room.dxf", TestFiles.Room(out _, out _)));
                model.Open(folder.Write("plan.dxf", new DxfDatabase("AC1027")));
                CadDocuments documents = model.PluginContext().Documents;
                CadDocument room = documents.Single(d => d.Title == "room.dxf");
                Assert.Equal("plan.dxf", model.Document.Title);

                documents.Active = room;

                Assert.Same(room, model.Document);
                Assert.Same(room, documents.Active);
                Assert.Same(room, model.PluginContext().Document);
                Assert.Throws<ArgumentException>(() =>
                    documents.Active = CadDocument.From(new DxfDatabase("AC1027"), "x.dxf")
                );
            }
        }

        [Fact]
        public void Close_ClosesAnUnmodifiedDrawingAtOnce_AndTheNextOneIsShown_TheLastLeavesTheStartScreen()
        {
            using (var folder = new TestFolder())
            {
                MainViewModel model = Window(folder);
                model.Open(folder.Write("room.dxf", TestFiles.Room(out _, out _)));
                model.Open(folder.Write("plan.dxf", new DxfDatabase("AC1027")));
                CadDocuments documents = model.PluginContext().Documents;
                CadDocument plan = documents.Active;

                documents.Close(plan);

                Assert.Equal(new[] { "room.dxf" }, documents.Select(d => d.Title));
                Assert.Equal("room.dxf", model.Document.Title);

                documents.Close(documents.Active);

                Assert.Empty(documents);
                Assert.False(model.HasDocument);
                Assert.Null(documents.Active);
            }
        }

        [Fact]
        public void Close_OfAModifiedDrawing_IsRefusedAsRemotesIs_AndCloseAndDiscardClosesItWithoutSaving()
        {
            using (var folder = new TestFolder())
            {
                MainViewModel model = Window(folder);
                string path = folder.Write("plan.dxf", new DxfDatabase("AC1027"));
                byte[] before = File.ReadAllBytes(path);
                model.Open(path);
                CadDocuments documents = model.PluginContext().Documents;
                CadDocument plan = documents.Active;
                Draw(plan);

                InvalidOperationException refused = Assert.Throws<InvalidOperationException>(() =>
                    documents.Close(plan)
                );

                Assert.Equal("plan.dxf has changes not saved: save it, or close it with discard.", refused.Message);
                Assert.Single(documents);
                Assert.True(plan.IsModified);

                documents.CloseAndDiscard(plan);

                Assert.Empty(documents);
                Assert.Equal(before, File.ReadAllBytes(path));
            }
        }

        [Fact]
        public void CloseAndSave_WritesTheFileAsSaveDoes_ThenCloses_AndADrawingWithNoFileIsRefused()
        {
            using (var folder = new TestFolder())
            {
                MainViewModel model = Window(folder);
                string path = folder.Write("plan.dxf", new DxfDatabase("AC1027"));
                model.Open(path);
                CadDocuments documents = model.PluginContext().Documents;
                CadDocument plan = documents.Active;
                Draw(plan);
                CadDocument fresh = documents.Add();
                Draw(fresh);

                InvalidOperationException noFile = Assert.Throws<InvalidOperationException>(() =>
                    documents.CloseAndSave(fresh)
                );

                Assert.Equal("The drawing Drawing1.dwg has no file yet: save it to a file first.", noFile.Message);
                Assert.Equal(2, documents.Count);

                documents.CloseAndSave(plan);

                Assert.Equal(new[] { "Drawing1.dwg" }, documents.Select(d => d.Title));
                Assert.Single(DxfDatabase.Open(path).ModelSpace);
            }
        }

        [Fact]
        public void WhileACommandRunsInTheDrawingShown_OpenAddAndShowingAnotherAreRefused_AndTheCommandKeepsRunning()
        {
            using (var folder = new TestFolder())
            {
                MainViewModel model = Window(folder);
                string room = folder.Write("room.dxf", TestFiles.Room(out _, out _));
                model.Open(folder.Write("plan.dxf", new DxfDatabase("AC1027")));
                CadDocuments documents = model.PluginContext().Documents;
                model.Execute("LINE");
                Assert.True(model.IsCommandRunning);

                InvalidOperationException open = Assert.Throws<InvalidOperationException>(() => documents.Open(room));
                Assert.Throws<InvalidOperationException>(() => documents.Add());

                Assert.Contains("busy with the command LINE", open.Message, StringComparison.Ordinal);
                Assert.True(model.IsCommandRunning);
                Assert.Single(documents);

                // The drawing is busy, so it is not closed either; ending the command lets all of it work.
                Assert.Throws<InvalidOperationException>(() => documents.CloseAndDiscard(documents.Active));
                model.Session.Escape();
                CadDocument opened = documents.Open(room);

                Assert.Same(model.Document, opened);
                Assert.Equal(2, documents.Count);
            }
        }
    }
}
