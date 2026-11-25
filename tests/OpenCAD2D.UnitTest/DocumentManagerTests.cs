using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using GeometryHelper.Dxf;
using Xunit;
using static OpenCAD2D.UnitTest.EditingDrawings;

namespace OpenCAD2D.UnitTest
{
    /// <summary>
    /// The drawings open as a plugin reaches them (<see cref="CadDocuments"/>, A9), over a few documents and no window: what
    /// is listed, what is made active, opened, added and closed, and the refusals, whose words are those of Remote's close.
    /// </summary>
    public class DocumentManagerTests : IDisposable
    {
        private readonly string _folder = Path.Combine(Path.GetTempPath(), "gk-docs-" + Guid.NewGuid().ToString("N"));

        public DocumentManagerTests()
        {
            Directory.CreateDirectory(_folder);
        }

        public void Dispose()
        {
            Directory.Delete(_folder, true);
        }

        /// <summary>An app of a few documents, each with a command session, as the window keeps them; it lists them by name.</summary>
        private sealed class FakeHost : ICadDocumentHost
        {
            private readonly List<CadDocument> _documents = new List<CadDocument>();
            private readonly Dictionary<CadDocument, CadCommandSession> _sessions =
                new Dictionary<CadDocument, CadCommandSession>();

            public List<string> Calls { get; } = new List<string>();

            public string OpenFailure { get; set; }

            public IReadOnlyList<CadDocument> Documents =>
                _documents.OrderBy(d => d.Title, StringComparer.OrdinalIgnoreCase).ToList();

            public CadDocument ActiveDocument { get; private set; }

            public CadDocument Add(CadDocument document, CadCommandRegistry registry = null)
            {
                _documents.Add(document);
                _sessions[document] =
                    registry == null
                        ? Session(document)
                        : WaitingRoutines.Track(new CadCommandSession(document, registry) { PickAperture = 1.0 });
                ActiveDocument = document;
                return document;
            }

            public CadDocument Open(string path)
            {
                Calls.Add("open " + path);

                if (OpenFailure != null)
                {
                    throw new InvalidOperationException(OpenFailure);
                }

                return Add(CadDocument.From(new DxfDatabase("AC1027"), Path.GetFileName(path)));
            }

            public CadDocument New(string template)
            {
                Calls.Add("new " + (template ?? "(app's own)"));
                return Add(CadDocument.From(new DxfDatabase("AC1027"), "Drawing" + (_documents.Count + 1) + ".dwg"));
            }

            public void Activate(CadDocument document)
            {
                Calls.Add("activate " + document.Title);
                ActiveDocument = document;
            }

            public CadCommandSession SessionOf(CadDocument document) =>
                _sessions.TryGetValue(document, out CadCommandSession session) ? session : null;

            public void Save(CadDocument document)
            {
                Calls.Add("save " + document.Title);
                document.Save();
            }

            public void Close(CadDocument document)
            {
                Calls.Add("close " + document.Title);
                _documents.Remove(document);
                _sessions.Remove(document);

                if (ReferenceEquals(ActiveDocument, document))
                {
                    ActiveDocument = _documents.LastOrDefault();
                }
            }
        }

        private static CadDocument Named(string name) => CadDocument.From(new DxfDatabase("AC1027"), name);

        private static void Draw(CadDocument document)
        {
            document.ApplyEdit("draw", null, s =>
                s.Append(
                    document.ModelSpace,
                    new DxfLine(
                        new GeometryHelper.Geometry.GeoPoint3(0, 0, 0),
                        new GeometryHelper.Geometry.GeoPoint3(100, 0, 0)
                    )
                )
            );
        }

        /// <summary>A window's documents A-101 and B-202 (the last one added is shown), and the documents object over them.</summary>
        private static CadDocuments Over(FakeHost host, out CadDocument a, out CadDocument b)
        {
            a = host.Add(Named("A-101.dxf"));
            b = host.Add(Named("B-202.dxf"));
            return new CadDocuments(host, null);
        }

        [Fact]
        public void ASessionWithNoWindow_ListsItsOwnDrawingAsTheActiveOne_AndSaysItHasNoWindow()
        {
            CadDocument document = Named("plan.dxf");
            CadCommandSession session = Session(document);

            CadDocuments documents = session.Documents;

            Assert.False(documents.HasWindow);
            Assert.Single(documents);
            Assert.Same(document, documents.Active);
            Assert.Same(document, documents[0]);
            Assert.Same(document, Assert.Single(documents));
        }

        [Fact]
        public void ASessionWithNoWindow_RefusesEveryCallThatChangesWhatIsOpen_WithPlainWords()
        {
            CadDocument document = Named("plan.dxf");
            CadDocuments documents = Session(document).Documents;
            const string tail = ": this session has no OpenCAD2D window, which holds the open drawings.";

            Assert.Equal(
                "Cannot open a drawing" + tail,
                Assert.Throws<InvalidOperationException>(() => documents.Open("C:\\x\\a.dxf")).Message
            );
            Assert.Equal(
                "Cannot add a drawing" + tail,
                Assert.Throws<InvalidOperationException>(() => documents.Add()).Message
            );
            Assert.Equal(
                "Cannot show a drawing" + tail,
                Assert.Throws<InvalidOperationException>(() => documents.Active = document).Message
            );
            Assert.Equal(
                "Cannot close a drawing" + tail,
                Assert.Throws<InvalidOperationException>(() => documents.Close(document)).Message
            );
            Assert.Equal(
                "Cannot close a drawing" + tail,
                Assert.Throws<InvalidOperationException>(() => documents.CloseAndDiscard(document)).Message
            );
            Assert.Equal(
                "Cannot close a drawing" + tail,
                Assert.Throws<InvalidOperationException>(() => documents.CloseAndSave(document)).Message
            );
            Assert.Single(documents);
        }

        [Fact]
        public void TheListIsInTheOrderOfTheHost_TheActiveOneIsTheHosts_AndTheCountFollowsWhatIsOpen()
        {
            var host = new FakeHost();
            CadDocuments documents = Over(host, out CadDocument a, out CadDocument b);
            CadDocument c = host.Add(Named("0-first.dxf"));

            Assert.True(documents.HasWindow);
            Assert.Equal(3, documents.Count);
            Assert.Equal(new[] { "0-first.dxf", "A-101.dxf", "B-202.dxf" }, documents.Select(d => d.Title));
            Assert.Same(a, documents[1]);
            Assert.Same(c, documents.Active);

            documents.Close(c);

            Assert.Equal(2, documents.Count);
            Assert.Same(b, documents.Active);
        }

        [Fact]
        public void WithNoDrawingOpen_TheListIsEmpty_AndNoDrawingIsActive()
        {
            var documents = new CadDocuments(new FakeHost(), null);

            Assert.Empty(documents);
            Assert.Null(documents.Active);
            Assert.Empty(documents);
            Assert.Throws<ArgumentOutOfRangeException>(() => documents[0]);
        }

        [Fact]
        public void SettingTheActiveDrawing_ShowsIt_AndShowingTheOneShownAlreadyAsksTheHostNothing()
        {
            var host = new FakeHost();
            CadDocuments documents = Over(host, out CadDocument a, out CadDocument b);

            documents.Active = a;
            documents.Active = a;

            Assert.Same(a, documents.Active);
            Assert.Equal(new[] { "activate A-101.dxf" }, host.Calls);
        }

        [Fact]
        public void SettingTheActiveDrawingToNullOrToOneNotOpen_IsRefused_AndTheShownOneStays()
        {
            var host = new FakeHost();
            CadDocuments documents = Over(host, out _, out CadDocument b);
            CadDocument stranger = Named("elsewhere.dxf");

            Assert.Throws<ArgumentNullException>(() => documents.Active = null);
            ArgumentException refused = Assert.Throws<ArgumentException>(() => documents.Active = stranger);

            Assert.Contains("The drawing elsewhere.dxf is not open.", refused.Message, StringComparison.Ordinal);
            Assert.Same(b, documents.Active);
            Assert.Empty(host.Calls);
        }

        [Fact]
        public void OpenGivesTheDrawingTheHostShows_AndAFileThatCannotBeOpenedThrowsWithTheHostsWords()
        {
            var host = new FakeHost();
            var documents = new CadDocuments(host, null);

            CadDocument opened = documents.Open("C:\\plans\\a.dxf");

            Assert.Same(opened, documents.Active);
            Assert.Equal(new[] { "open C:\\plans\\a.dxf" }, host.Calls);

            host.OpenFailure = "Cannot open C:\\plans\\broken.dxf: the file is not a DXF.";
            InvalidOperationException failed = Assert.Throws<InvalidOperationException>(() =>
                documents.Open("C:\\plans\\broken.dxf")
            );

            Assert.Equal("Cannot open C:\\plans\\broken.dxf: the file is not a DXF.", failed.Message);
            Assert.Single(documents);
        }

        [Fact]
        public void OpenNeedsAPath_NullOrBlankIsRefusedBeforeTheHostIsAsked()
        {
            var host = new FakeHost();
            var documents = new CadDocuments(host, null);

            Assert.Throws<ArgumentNullException>(() => documents.Open(null));
            Assert.Throws<ArgumentException>(() => documents.Open("  "));
            Assert.Empty(host.Calls);
        }

        [Fact]
        public void AddMakesANewDrawingOfTheTemplateGiven_OrOfTheAppsOwnWhereNoneIsGiven()
        {
            var host = new FakeHost();
            var documents = new CadDocuments(host, null);

            CadDocument own = documents.Add();
            CadDocument fromTemplate = documents.Add("C:\\templates\\plan.dwt");

            Assert.Equal(new[] { "new (app's own)", "new C:\\templates\\plan.dwt" }, host.Calls);
            Assert.Same(fromTemplate, documents.Active);
            Assert.Equal(2, documents.Count);
            Assert.NotSame(own, fromTemplate);
        }

        [Fact]
        public void AnUnmodifiedDrawing_IsClosed_AndAnotherIsShown()
        {
            var host = new FakeHost();
            CadDocuments documents = Over(host, out CadDocument a, out CadDocument b);

            documents.Close(b);

            Assert.Equal(new[] { "close B-202.dxf" }, host.Calls);
            Assert.Same(a, Assert.Single(documents));
            Assert.Same(a, documents.Active);
        }

        [Fact]
        public void AModifiedDrawing_IsRefusedByCloseWithTheWordsOfRemote_AndStaysOpenAndModified()
        {
            var host = new FakeHost();
            CadDocuments documents = Over(host, out CadDocument a, out _);
            Draw(a);

            InvalidOperationException refused = Assert.Throws<InvalidOperationException>(() => documents.Close(a));

            Assert.Equal("A-101.dxf has changes not saved: save it, or close it with discard.", refused.Message);
            Assert.Equal(2, documents.Count);
            Assert.True(a.IsModified);
            Assert.Empty(host.Calls);
        }

        [Fact]
        public void CloseAndDiscard_ClosesAModifiedDrawingWithoutSavingIt_AndItsFileIsUnchanged()
        {
            var host = new FakeHost();
            string path = Path.Combine(_folder, "plan.dxf");
            CadDocument plan = Named("plan.dxf");
            plan.SaveAs(path);
            byte[] before = File.ReadAllBytes(path);
            host.Add(plan);
            var documents = new CadDocuments(host, null);
            Draw(plan);
            Assert.True(plan.IsModified);

            documents.CloseAndDiscard(plan);

            Assert.Empty(documents);
            Assert.Equal(new[] { "close plan.dxf" }, host.Calls);
            Assert.Equal(before, File.ReadAllBytes(path));
        }

        [Fact]
        public void CloseAndSave_WritesTheDrawingToItsFile_ThenClosesIt()
        {
            var host = new FakeHost();
            string path = Path.Combine(_folder, "plan.dxf");
            CadDocument plan = Named("plan.dxf");
            plan.SaveAs(path);
            host.Add(plan);
            var documents = new CadDocuments(host, null);
            Draw(plan);

            documents.CloseAndSave(plan);

            Assert.Equal(new[] { "save plan.dxf", "close plan.dxf" }, host.Calls);
            Assert.Empty(documents);
            Assert.Single(DxfDatabase.Open(path).ModelSpace);
        }

        [Fact]
        public void CloseAndSave_OfADrawingWithNoFile_IsRefusedAndNothingIsSavedOrClosed()
        {
            var host = new FakeHost();
            CadDocuments documents = Over(host, out CadDocument a, out _);
            Draw(a);

            InvalidOperationException refused = Assert.Throws<InvalidOperationException>(() =>
                documents.CloseAndSave(a)
            );

            Assert.Equal("The drawing A-101.dxf has no file yet: save it to a file first.", refused.Message);
            Assert.Equal(2, documents.Count);
            Assert.Empty(host.Calls);
            Assert.True(a.IsModified);
        }

        [Fact]
        public void ADrawingWithACommandRunning_IsRefusedByEveryClose_AndCloseAndSaveSavesNothing()
        {
            var host = new FakeHost();
            string path = Path.Combine(_folder, "plan.dxf");
            CadDocument plan = Named("plan.dxf");
            plan.SaveAs(path);
            host.Add(plan);
            var documents = new CadDocuments(host, null);
            host.SessionOf(plan).Feed("LINE");
            Assert.True(host.SessionOf(plan).IsRunning);
            const string words =
                "The drawing plan.dxf is busy with the command LINE: end it, then close the drawing again.";

            Assert.Equal(words, Assert.Throws<InvalidOperationException>(() => documents.Close(plan)).Message);
            Assert.Equal(
                words,
                Assert.Throws<InvalidOperationException>(() => documents.CloseAndDiscard(plan)).Message
            );
            Assert.Equal(words, Assert.Throws<InvalidOperationException>(() => documents.CloseAndSave(plan)).Message);

            Assert.Single(documents);
            Assert.Empty(host.Calls);
            Assert.True(host.SessionOf(plan).IsRunning);
        }

        [Fact]
        public void ClosingNullOrADrawingNotOpen_IsRefusedWithAnArgumentError()
        {
            var host = new FakeHost();
            CadDocuments documents = Over(host, out _, out _);
            CadDocument stranger = Named("elsewhere.dxf");

            Assert.Throws<ArgumentNullException>(() => documents.Close(null));
            Assert.Throws<ArgumentException>(() => documents.Close(stranger));
            Assert.Throws<ArgumentException>(() => documents.CloseAndDiscard(stranger));
            Assert.Throws<ArgumentException>(() => documents.CloseAndSave(stranger));
            Assert.Equal(2, documents.Count);
            Assert.Empty(host.Calls);
        }

        [Fact]
        public void WhileACommandRunsInTheDrawingShown_OpenAddAndShowingAnotherAreRefused_BecauseShowingWouldEndIt()
        {
            var host = new FakeHost();
            CadDocuments documents = Over(host, out CadDocument a, out CadDocument b);
            host.SessionOf(b).Feed("LINE");

            InvalidOperationException open = Assert.Throws<InvalidOperationException>(() =>
                documents.Open("C:\\plans\\c.dxf")
            );
            InvalidOperationException add = Assert.Throws<InvalidOperationException>(() => documents.Add());
            InvalidOperationException show = Assert.Throws<InvalidOperationException>(() => documents.Active = a);

            Assert.Equal(
                "Cannot open a drawing. The drawing B-202.dxf is busy with the command LINE: end it, then open the drawing again.",
                open.Message
            );
            Assert.Equal(
                "Cannot add a drawing. The drawing B-202.dxf is busy with the command LINE: end it, then add the drawing again.",
                add.Message
            );
            Assert.Equal(
                "Cannot show another drawing. The drawing B-202.dxf is busy with the command LINE: end it, then show the drawing again.",
                show.Message
            );
            Assert.Empty(host.Calls);
            Assert.True(host.SessionOf(b).IsRunning);
            Assert.Same(b, documents.Active);

            // Showing the drawing already shown ends nothing, so it is allowed; and once the command ends all of it works.
            documents.Active = b;
            host.SessionOf(b).Escape();
            documents.Active = a;

            Assert.Same(a, documents.Active);
        }

        [Fact]
        public void ACommandRunningInAnotherDrawing_DoesNotStopOpeningInTheDrawingShown()
        {
            var host = new FakeHost();
            CadDocuments documents = Over(host, out CadDocument a, out _);
            host.SessionOf(a).Feed("LINE");

            CadDocument made = documents.Add();

            Assert.Same(made, documents.Active);
            Assert.True(host.SessionOf(a).IsRunning);
        }

        [Fact]
        public void ACommandsContext_GivesTheSessionsDrawings_AndTheCommandMayCloseAnotherOne()
        {
            var host = new FakeHost();
            var registry = CadCommandRegistry.CreateDefault();
            var seen = new List<string>();
            registry.Add(new CadCommand("CLOSEOTHERS", c =>
                {
                    seen.AddRange(c.Documents.Select(d => d.Title));
                    c.Documents.Close(c.Documents.First(d => !ReferenceEquals(d, c.Document)));
                    return Array.Empty<CadPrompt>();
                }));
            CadDocument a = host.Add(Named("A-101.dxf"));
            CadDocument b = host.Add(Named("B-202.dxf"), registry);
            CadCommandSession session = host.SessionOf(b);
            session.Documents = new CadDocuments(host, null);

            session.Feed("CLOSEOTHERS");

            Assert.Equal(CadCommandOutcome.Completed, session.LastOutcome);
            Assert.Equal(new[] { "A-101.dxf", "B-202.dxf" }, seen);
            Assert.Same(b, Assert.Single(session.Documents));
            Assert.DoesNotContain(a, session.Documents);
        }

        [Fact]
        public void ACommandThatClosesItsOwnDrawing_IsRefusedAsBusy_AndTheDrawingStaysOpen()
        {
            var host = new FakeHost();
            var registry = CadCommandRegistry.CreateDefault();
            string said = null;
            registry.Add(new CadCommand("CLOSEME", c =>
                {
                    try
                    {
                        c.Documents.Close(c.Document);
                    }
                    catch (InvalidOperationException ex)
                    {
                        said = ex.Message;
                    }

                    return Array.Empty<CadPrompt>();
                }));
            CadDocument a = host.Add(Named("A-101.dxf"), registry);
            CadCommandSession session = host.SessionOf(a);
            session.Documents = new CadDocuments(host, null);

            session.Feed("CLOSEME");

            Assert.Equal(
                "The drawing A-101.dxf is busy with the command CLOSEME: end it, then close the drawing again.",
                said
            );
            Assert.Single(session.Documents);
        }
    }
}
