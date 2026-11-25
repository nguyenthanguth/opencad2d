using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using GeometryHelper.Dxf;
using OpenCAD2D.SheetSets;
using Xunit;
using static OpenCAD2D.UnitTest.EditingDrawings;

namespace OpenCAD2D.UnitTest
{
    /// <summary>
    /// OPENSHEETSET and NEWSHEETSET with their command-line forms (FILEDIA 0 or no window), the Sheet Set Manager's system
    /// variables as AutoCAD 2021 gave them in its console (defaults 1, 1, 0, "", 60 and 2; SSLOCATE and SSMAUTOOPEN 0 or 1 only,
    /// SSMPOLLTIME 20 to 600, SSMSHEETSTATUS 0 to 2, SSMSTATE and SSFOUND read only), and SSLOCATE finding the set of a drawing
    /// that opens. Every set and drawing is built in code in a temporary folder.
    /// </summary>
    public sealed class SheetSetCommandFileTests : IDisposable
    {
        private readonly string _folder = Path.Combine(
            Path.GetTempPath(),
            "gk",
            "sheetsetcmd-" + Guid.NewGuid().ToString("N")
        );

        public SheetSetCommandFileTests()
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

        private CadCommandSession Start(out List<CadUiRequestEventArgs> asked, bool handle = false)
        {
            var seen = new List<CadUiRequestEventArgs>();
            CadCommandSession session = Session(Blank());
            session.SheetSetOptions.StoreFolder = Path.Combine(_folder, "store");
            session.UiRequested += (sender, e) =>
            {
                seen.Add(e);
                e.Handled = handle;
            };
            asked = seen;
            return session;
        }

        private string MakeSet(string name = "Tower")
        {
            string path = Path.Combine(_folder, name + ".dst");
            CadSheetSetFile.Create(path, name, null);
            return path;
        }

        // ----- The variables

        [Fact]
        public void TheVariables_StartAsAutoCadHasThem()
        {
            CadCommandSession session = Session(Blank());
            CadCommandRegistry registry = session.Registry;

            Assert.Equal("1", registry.FindVariable("SSLOCATE").GetValue(session));
            Assert.Equal("1", registry.FindVariable("SSMAUTOOPEN").GetValue(session));
            Assert.Equal("0", registry.FindVariable("SSMSTATE").GetValue(session));
            Assert.Equal("\"\"", registry.FindVariable("SSFOUND").GetValue(session));
            Assert.Equal("60", registry.FindVariable("SSMPOLLTIME").GetValue(session));
            Assert.Equal("2", registry.FindVariable("SSMSHEETSTATUS").GetValue(session));
            Assert.True(registry.FindVariable("SSMSTATE").IsReadOnly);
            Assert.True(registry.FindVariable("SSFOUND").IsReadOnly);
        }

        [Fact]
        public void TheSwitches_TakeZeroOrOneOnly_InAutoCadsWords()
        {
            CadCommandSession session = Run(Session(Blank()), "SSLOCATE");

            Assert.Equal("Enter new value for SSLOCATE <1>:", session.PromptText);
            Run(session, "5");
            Assert.Equal("Requires 0 or 1 only.", session.History.Last());
            Run(session, "0");

            Assert.False(session.IsRunning);
            Assert.False(session.SheetSetOptions.Locate);
            Run(session, "SSMAUTOOPEN", "0");
            Assert.False(session.SheetSetOptions.AutoOpen);
        }

        [Fact]
        public void ThePollTimeIsFrom20To600_AndTheSheetStatusFrom0To2()
        {
            CadCommandSession session = Run(Session(Blank()), "SSMPOLLTIME", "10");

            Assert.Equal("Requires an integer between 20 and 600.", session.History.Last());
            Run(session, "700");
            Assert.Equal("Requires an integer between 20 and 600.", session.History.Last());
            Run(session, "600");
            Assert.Equal(600, session.SheetSetOptions.PollSeconds);

            Run(session, "SSMSHEETSTATUS", "3");
            Assert.Equal("Requires an integer between 0 and 2.", session.History.Last());
            Run(session, "1");
            Assert.Equal(1, session.SheetSetOptions.SheetStatus);
        }

        [Fact]
        public void TheOptions_AreSharedBetweenSessionsThatAreGivenOne()
        {
            var options = new CadSheetSetOptions();
            CadCommandSession first = Session(Blank());
            CadCommandSession second = Session(Blank());
            first.SheetSetOptions = options;
            second.SheetSetOptions = options;

            Run(first, "SSMPOLLTIME", "120");

            Assert.Equal("120", second.Registry.FindVariable("SSMPOLLTIME").GetValue(second));
            Assert.Throws<ArgumentNullException>(() => first.SheetSetOptions = null);
        }

        [Fact]
        public void SSMSTATE_IsOneWhileTheWindowSaysThePaletteIsShown_AndZeroWithNoWindow()
        {
            CadCommandSession session = Start(out List<CadUiRequestEventArgs> asked);
            CadSystemVariable state = session.Registry.FindVariable("SSMSTATE");

            Assert.Equal("0", state.GetValue(session));
            session.UiRequested += (sender, e) =>
            {
                if (e.Name == "SSMSTATE")
                {
                    e.Answer = "1";
                    e.Handled = true;
                }
            };

            Assert.Equal("1", state.GetValue(session));
            Assert.Contains(asked, e => e.Name == "SSMSTATE");
            Run(session, "SSMSTATE");
            Assert.Equal("SSMSTATE = 1 (read only)", session.History.Last());
        }

        // ----- OPENSHEETSET

        [Fact]
        public void OpensheetsetWithFileDialogs_AsksTheWindowForItsDialog_AndAsksNothingMoreWhenItAnswers()
        {
            CadCommandSession session = Start(out List<CadUiRequestEventArgs> asked, true);

            Run(session, "OPENSHEETSET");

            Assert.False(session.IsRunning);
            CadUiRequestEventArgs request = Assert.Single(asked);
            Assert.Equal("OPENSHEETSET", request.Name);
            Assert.Null(((CadSheetSetRequest)request.Payload).Path);
        }

        [Fact]
        public void OpensheetsetWithFiledia0_AsksTheNameOnTheCommandLine_AndHandsTheWindowTheFileToOpen()
        {
            string dst = MakeSet("Example Tower");
            CadCommandSession session = Start(out List<CadUiRequestEventArgs> asked, true);
            Run(session, "FILEDIA", "0", "OPENSHEETSET");

            Assert.Equal("Enter name of sheet set to open:", session.PromptText);
            Run(session, Path.Combine(_folder, "Example Tower"));

            Assert.False(session.IsRunning);
            CadUiRequestEventArgs request = Assert.Single(asked);
            Assert.Equal("OPENSHEETSET", request.Name);
            var payload = (CadSheetSetRequest)request.Payload;
            Assert.Equal(dst, payload.Path);
            Assert.False(payload.ShowPalette);
        }

        [Fact]
        public void TheTypedForm_IsAskedWithNoWindow_AndSaysWhereThereIsNoWindowToOpenTheSetIn()
        {
            string dst = MakeSet();
            CadCommandSession session = Start(out _);

            Run(session, "OPENSHEETSET");
            Assert.Equal("Enter name of sheet set to open:", session.PromptText);
            Run(session, dst);

            Assert.Equal(
                "The Sheet Set Manager is a palette of the window; there is none to open the sheet set in.",
                session.History.Last()
            );
        }

        [Fact]
        public void ADashOpensheetset_AsksTheNameEvenWhereFileDialogsAreOn()
        {
            CadCommandSession session = Start(out List<CadUiRequestEventArgs> asked, true);

            Run(session, "-OPENSHEETSET");

            Assert.Equal("Enter name of sheet set to open:", session.PromptText);
            Assert.Empty(asked);
            Run(session, Esc);
        }

        [Fact]
        public void AFileThatIsNotThere_IsAnswered_FileCanNotBeOpened_AndOneThatIsNoSheetSet_SheetSetFileIsNotValid()
        {
            string notASet = Path.Combine(_folder, "notes.dst");
            File.WriteAllText(notASet, "not a sheet set");
            CadCommandSession session = Start(out List<CadUiRequestEventArgs> asked, true);

            Run(session, "-OPENSHEETSET", Path.Combine(_folder, "missing.dst"));
            Assert.Equal("File can not be opened.", session.History.Last());
            Run(session, "-OPENSHEETSET", notASet);
            Assert.Equal("Sheet Set file is not valid.", session.History.Last());
            Run(session, "-OPENSHEETSET", Enter);

            Assert.Empty(asked);
        }

        // ----- NEWSHEETSET

        [Fact]
        public void NewsheetsetWithFileDialogs_AsksTheWindowForItsWizard()
        {
            CadCommandSession session = Start(out List<CadUiRequestEventArgs> asked, true);

            Run(session, "NEWSHEETSET");

            Assert.False(session.IsRunning);
            Assert.Equal("NEWSHEETSET", Assert.Single(asked).Name);
        }

        [Fact]
        public void NewsheetsetOnTheCommandLine_MakesAnEmptySetFromAnExample_AndHandsItToTheWindowToOpen()
        {
            string example = MakeSet("Example");
            string store = Path.Combine(_folder, "store");
            Directory.CreateDirectory(store);
            CadCommandSession session = Start(out List<CadUiRequestEventArgs> asked, true);
            Run(session, "FILEDIA", "0", "NEWSHEETSET");

            Assert.Equal("Create a sheet set using [Example/Drawings] <Example>:", session.PromptText);
            Run(session, Enter);
            Assert.StartsWith("Select a sheet set to use as an example", session.PromptText);
            Run(session, example);
            Assert.Equal("Name of new sheet set:", session.PromptText);
            Run(session, "Harbour Tower");
            Assert.Equal("Description (optional):", session.PromptText);
            Run(session, "Two towers");
            Assert.Equal("Store sheet set data file (.dst) here <" + store + ">:", session.PromptText);
            Run(session, Enter);
            Assert.Equal("Create a folder hierarchy based on subsets [Yes/No] <No>:", session.PromptText);
            Run(session, Enter);

            string made = Path.Combine(store, "Harbour Tower.dst");
            Assert.False(session.IsRunning);
            CadSheetSet set = CadSheetSetFile.Open(made).SheetSet;
            Assert.Equal("Harbour Tower", set.Name);
            Assert.Equal("Two towers", set.Description);
            Assert.Contains("The sheet set \"Harbour Tower\" is made: " + made + ".", session.History);
            CadUiRequestEventArgs open = Assert.Single(asked);
            Assert.Equal(made, ((CadSheetSetRequest)open.Payload).Path);
        }

        [Fact]
        public void NewsheetsetAsksANameAgain_WhereTheFileIsThereAlready_AndAnEmptyNameEndsIt()
        {
            string store = Path.Combine(_folder, "store");
            Directory.CreateDirectory(store);
            CadSheetSetFile.Create(Path.Combine(store, "Tower.dst"), "Tower", null);
            CadCommandSession session = Start(out _);
            Run(session, "FILEDIA", "0", "NEWSHEETSET", "Drawings", Enter);

            Run(session, "Tower", Enter, Enter);

            Assert.Contains("This file already exists. Please enter a different filename.", session.History);
            Assert.Equal("Name of new sheet set:", session.PromptText);
            Run(session, Enter);
            Assert.False(session.IsRunning);
            Assert.Equal(new[] { "Tower.dst" }, Directory.GetFiles(store).Select(Path.GetFileName));
        }

        [Fact]
        public void NewsheetsetFromDrawings_ImportsTheLayoutsThatAreNotRefused_AndSaysWhyTheOthersAre()
        {
            string drawing = SheetSetTestDrawings.Write(_folder, "plans.dwg", "Ground", "Roof #2", "First");
            CadCommandSession session = Start(out List<CadUiRequestEventArgs> asked, true);
            Run(session, "FILEDIA", "0", "NEWSHEETSET", "Drawings");

            Assert.StartsWith("Enter the drawings whose layouts become the sheets", session.PromptText);
            Run(session, drawing, "Tower", Enter, Enter);
            Assert.StartsWith("The location entered is not an existing folder.", session.PromptText);
            Run(session, Enter);

            Assert.False(session.IsRunning);
            Assert.Contains(session.History, line => line.Contains("Roof #2") && line.Contains("#"));
            string made = Path.Combine(_folder, "store", "Tower.dst");
            CadSheetSetFile file = CadSheetSetFile.Open(made);
            Assert.Equal(new[] { "Ground", "First" }, file.SheetSet.AllSheets().Select(s => s.Layout.Name));
            Assert.Equal(made, ((CadSheetSetRequest)Assert.Single(asked).Payload).Path);
        }

        // ----- SSLOCATE

        private string MakeSheetOfSet(out string dst)
        {
            dst = Path.Combine(_folder, "set", "Tower.dst");
            Directory.CreateDirectory(Path.GetDirectoryName(dst));
            CadSheetSetFile file = CadSheetSetFile.Create(dst, "Tower", null);
            string template = SheetSetTestDrawings.Write(_folder, "sheets.dwt", "A1 sheet");
            CadDrawingReference layout = null;
            file.Change(set =>
            {
                set.Template = new CadDrawingReference(
                    CadFileReference.FromPath(template, file.Folder),
                    "A1 sheet",
                    null,
                    null
                );
                layout = set.NewSheet("A-101", "Ground floor").Layout;
            });
            return layout.File.Resolve(file.Folder);
        }

        [Fact]
        public void LocateSheetSet_FindsTheSetOfADrawingThatIsASheet_SetsSSFOUND_AndAsksTheWindowToOpenItAndShowThePalette()
        {
            string sheet = MakeSheetOfSet(out string dst);
            CadDocument document = CadDocument.Open(sheet);
            CadCommandSession session = Session(document);
            var asked = new List<CadUiRequestEventArgs>();
            session.UiRequested += (sender, e) => asked.Add(e);

            string found = session.LocateSheetSet();

            Assert.Equal(dst, found, ignoreCase: true);
            Assert.Equal(found, session.SheetSetFound);
            Assert.Equal("\"" + found + "\"", session.Registry.FindVariable("SSFOUND").GetValue(session));
            CadUiRequestEventArgs request = Assert.Single(asked, e => e.Name == "SSLOCATE");
            var payload = (CadSheetSetRequest)request.Payload;
            Assert.Equal(found, payload.Path);
            Assert.True(payload.ShowPalette);
        }

        [Fact]
        public void LocateSheetSet_DoesNotShowThePalette_WhereSSMAUTOOPENIsOff()
        {
            string sheet = MakeSheetOfSet(out _);
            CadCommandSession session = Session(CadDocument.Open(sheet));
            var asked = new List<CadUiRequestEventArgs>();
            session.UiRequested += (sender, e) => asked.Add(e);
            Run(session, "SSMAUTOOPEN", "0");

            Assert.NotNull(session.LocateSheetSet());

            Assert.False(((CadSheetSetRequest)Assert.Single(asked, e => e.Name == "SSLOCATE").Payload).ShowPalette);
        }

        [Fact]
        public void LocateSheetSet_DoesNothing_WhereSSLOCATEIsOff_OrTheDrawingIsNoSheet_OrItsSetIsGone()
        {
            string sheet = MakeSheetOfSet(out string dst);
            CadCommandSession off = Session(CadDocument.Open(sheet));
            var asked = new List<string>();
            off.UiRequested += (sender, e) => asked.Add(e.Name);
            Run(off, "SSLOCATE", "0");

            Assert.Null(off.LocateSheetSet());
            Assert.Equal(string.Empty, off.SheetSetFound);

            CadCommandSession plain = Session(Blank());
            plain.UiRequested += (sender, e) => asked.Add(e.Name);
            Assert.Null(plain.LocateSheetSet());

            File.Delete(dst);
            CadCommandSession gone = Session(CadDocument.Open(sheet));
            gone.UiRequested += (sender, e) => asked.Add(e.Name);
            Assert.Null(gone.LocateSheetSet());
            Assert.Equal(string.Empty, gone.SheetSetFound);
            Assert.DoesNotContain("SSLOCATE", asked);
        }

        [Fact]
        public void LocateSheetSet_FindsASetMovedWithItsSheets()
        {
            string sheet = MakeSheetOfSet(out string dst);
            string moved = Path.Combine(_folder, "moved");
            Directory.Move(Path.GetDirectoryName(dst), moved);
            CadCommandSession session = Session(CadDocument.Open(Path.Combine(moved, Path.GetFileName(sheet))));

            string found = session.LocateSheetSet();

            Assert.Equal(Path.Combine(moved, "Tower.dst"), found, ignoreCase: true);
        }

        // ----- The settings

        [Fact]
        public void TheSettings_KeepTheVariables_AndGiveThemToTheOptionsAShareIsMadeOf()
        {
            string file = Path.Combine(_folder, "settings.json");
            var options = new CadSheetSetOptions
            {
                Locate = false,
                AutoOpen = false,
                PollSeconds = 300,
                SheetStatus = 0,
                StoreFolder = Path.Combine(_folder, "mine"),
            };
            var settings = new GlobalSettings();
            settings.TakeSheetSetOptionsFrom(options);
            settings.Save(file);

            GlobalSettings loaded = GlobalSettings.Load(file);
            var again = new CadSheetSetOptions();
            loaded.CopySheetSetOptionsTo(again);

            Assert.False(again.Locate);
            Assert.False(again.AutoOpen);
            Assert.Equal(300, again.PollSeconds);
            Assert.Equal(0, again.SheetStatus);
            Assert.Equal(Path.Combine(_folder, "mine"), again.StoreFolder);
            Assert.True(new GlobalSettings().SheetSetLocate);
            Assert.Equal(60, new GlobalSettings().SheetSetPollSeconds);
            Assert.Throws<ArgumentOutOfRangeException>(() => settings.SheetSetPollSeconds = 5);
        }

        [Fact]
        public void TheOptionsRaiseChanged_OnlyWhereAValueChanges()
        {
            var options = new CadSheetSetOptions();
            int changes = 0;
            options.Changed += (sender, e) => changes++;

            options.Locate = true;
            options.PollSeconds = 60;
            options.Locate = false;
            options.PollSeconds = 61;

            Assert.Equal(2, changes);
            Assert.Throws<ArgumentOutOfRangeException>(() => options.SheetStatus = 3);
            Assert.Throws<ArgumentOutOfRangeException>(() => options.PollSeconds = 19);
        }
    }
}
