using System;
using System.IO;
using System.Linq;
using System.Windows.Threading;
using Xunit;
using static OpenCAD2D.Wpf.UnitTest.SecurityKit;

namespace OpenCAD2D.Wpf.UnitTest
{
    /// <summary>
    /// Python in the window (D720, D1670, TF7): with the app's Python off nothing of it is in the commands; with it on, every
    /// drawing has PYRUN and PYLOAD, and PYTHON and PYTHONCLOSE (the palette's commands, removed) are not commands. A tool loads
    /// from its folder with its commands from <c>plugin.json</c> and starts no Python: no process, no venv made, no Python
    /// assembly in the process of the app, and a lone <c>.py</c> is refused with its line. No test here starts a process (the
    /// ones that do are <see cref="PythonToolWindowTests"/>).
    /// </summary>
    public sealed class MainViewModelPythonTests : IDisposable
    {
        private static readonly string[] Names = { "PYRUN", "PYLOAD" };
        private static readonly string[] Removed = { "PYTHON", "PYTHONCLOSE" };
        private readonly TestFolder _folder = new TestFolder();

        private readonly System.Collections.Generic.List<MainViewModel> _models =
            new System.Collections.Generic.List<MainViewModel>();

        public void Dispose()
        {
            foreach (MainViewModel model in _models)
            {
                model.DisposePython();
            }

            _folder.Dispose();
        }

        private string Room(string name) => _folder.Write(name, TestFiles.Room(out _, out _));

        /// <summary>A window's model on a settings file of its own, with a drawing open and Python used over venvs of the test's folder.</summary>
        private MainViewModel Using(int secureLoad = 0, string trustedPaths = "")
        {
            var model = new MainViewModel { SettingsPath = Path.Combine(_folder.FullPath, "settings.json") };
            model.ApplySettings(new GlobalSettings { SecureLoad = secureLoad, TrustedPaths = trustedPaths });
            Assert.True(model.Open(Room("room-" + Guid.NewGuid().ToString("N") + ".dxf")));
            model.UsePython(Dispatcher.CurrentDispatcher, Setup());
            _models.Add(model);
            return model;
        }

        /// <summary>The venvs and the host of a test: folders of its own, so that nothing is written to the user's app data.</summary>
        private PythonToolSetup Setup() =>
            new PythonToolSetup
            {
                Environments = new PythonEnvironments(
                    Path.Combine(_folder.FullPath, "envs"),
                    Path.Combine(_folder.FullPath, "base"),
                    PythonEnvTestSupport.AppPython,
                    PythonEnvTestSupport.ShippedWheels
                ),
                MakeHost = (host, dispatcher) =>
                    new CadToolHost(
                        host,
                        dispatcher,
                        PythonEnvTestSupport.Library,
                        Guid.NewGuid().ToString("N").Substring(0, 8)
                    ),
            };

        /// <summary>A tool: a folder with a requirements.txt, an __init__.py and a plugin.json that names the commands.</summary>
        private string Tool(string name, string manifest, string parent = "Pythons")
        {
            string folder = Path.Combine(_folder.FullPath, parent, name);
            Directory.CreateDirectory(folder);
            File.WriteAllText(Path.Combine(folder, "requirements.txt"), string.Empty);
            File.WriteAllText(Path.Combine(folder, "__init__.py"), "pass\n");

            if (manifest != null)
            {
                File.WriteAllText(Path.Combine(folder, "plugin.json"), manifest);
            }

            return folder;
        }

        [Fact]
        public void WithPythonNotUsed_NoPythonCommandIsInTheRegistry()
        {
            var model = new MainViewModel();
            Assert.True(model.Open(Room("room.dxf")));

            Assert.False(model.UsesPython);

            foreach (string name in Names.Concat(Removed))
            {
                Assert.Null(model.Session.Registry.Find(name));
            }
        }

        [Fact]
        public void WithPythonNotUsed_TypingPyrun_IsNotACommand()
        {
            var model = new MainViewModel();
            Assert.True(model.Open(Room("room.dxf")));

            model.Execute("PYRUN");

            Assert.False(model.Session.IsRunning);
            Assert.False(model.UsesPython);
        }

        [Fact]
        public void UsePython_GivesTheDrawingsOpenAndTheOnesMadeLater_PyrunAndPyload_AndNotThePalettesCommands()
        {
            var model = new MainViewModel { SettingsPath = Path.Combine(_folder.FullPath, "settings.json") };
            Assert.True(model.Open(Room("before.dxf")));
            CadCommandSession before = model.Session;

            model.UsePython(Dispatcher.CurrentDispatcher, Setup());
            Assert.True(model.Open(Room("after.dxf")));

            Assert.NotSame(before, model.Session);
            Assert.True(model.UsesPython);

            foreach (CadCommandSession session in new[] { before, model.Session })
            {
                foreach (string name in Names)
                {
                    Assert.NotNull(session.Registry.Find(name));
                }

                foreach (string name in Removed)
                {
                    Assert.Null(session.Registry.Find(name));
                }
            }
        }

        [Fact]
        public void TypingPythonAfterUsePython_IsNotACommand_AsThePaletteIsGone()
        {
            MainViewModel model = Using();

            model.Execute("PYTHON");
            model.Execute("PYTHONCLOSE");

            Assert.False(model.Session.IsRunning);
            Assert.Contains(model.Session.History, line =>
                line.Contains("Unknown command \"PYTHON\"", StringComparison.Ordinal)
            );
            Assert.Contains(model.Session.History, line =>
                line.Contains("Unknown command \"PYTHONCLOSE\"", StringComparison.Ordinal)
            );
        }

        [Fact]
        public void TheAppWithPythonUsedAndAToolLoaded_HasNoPythonRuntimeInItsProcess_AndNoToolProcess()
        {
            MainViewModel model = Using();
            string folder = Tool("Hello", "{ \"name\": \"Hello\", \"commands\": [ \"SAYHELLO\" ] }");

            Assert.Equal(AppLoadStatusKind.Loaded, model.LoadApplication(folder).Kind);

            Assert.DoesNotContain(AppDomain.CurrentDomain.GetAssemblies(), a =>
                a.GetName().Name == "Python.Runtime" || a.GetName().Name == "OpenCAD2D.Python"
            );
            Assert.False(model.HasPythonStarted);
        }

        [Fact]
        public void TheToolsOfTheStart_AreTheFoldersOfPythonsWithARequirementsFile_ThenTheSuite_AndNoLonePy()
        {
            string pythons = Path.Combine(_folder.FullPath, "Pythons");
            Tool("beta", null);
            Tool("Alpha", null);
            Directory.CreateDirectory(Path.Combine(pythons, "package"));
            File.WriteAllText(Path.Combine(pythons, "package", "__init__.py"), "pass\n");
            File.WriteAllText(Path.Combine(pythons, "lone.py"), "pass\n");
            string suited = Tool("suited", null, "elsewhere");
            string lone = Path.Combine(_folder.FullPath, "elsewhere", "old.py");
            File.WriteAllText(lone, "pass\n");

            System.Collections.Generic.IReadOnlyList<string> found = PythonToolFiles.ToolsToLoad(
                new[] { pythons, Path.Combine(_folder.FullPath, "nowhere") },
                new[] { suited, lone, Path.Combine(suited, "requirements.txt") }
            );

            Assert.Equal(new[] { Path.Combine(pythons, "Alpha"), Path.Combine(pythons, "beta"), suited, lone }, found);
        }

        [Fact]
        public void TheStart_LoadsTheToolsOfAPythonsFolderInOrder_AndOneThatFailsWritesItsLineAndTheNextLoads()
        {
            MainViewModel model = Using();
            string pythons = Path.Combine(_folder.FullPath, "Pythons");
            Tool("Zeta", "{ \"name\": \"Zeta\", \"commands\": [ \"ZETA1\" ] }");
            Tool("Broken", "not json");
            Tool("Alpha", "{ \"name\": \"Alpha\", \"commands\": [ \"ALPHA1\" ] }");

            ((PythonToolFiles)model.PythonFiles).Startup(model.Session, new[] { pythons }, new string[0]);
            model.RefreshHistory();

            Assert.Equal(
                new[] { Path.Combine(pythons, "Alpha"), Path.Combine(pythons, "Zeta") },
                model.LoadedPythonFiles
            );
            Assert.NotNull(model.Session.Registry.Find("ALPHA1"));
            Assert.NotNull(model.Session.Registry.Find("ZETA1"));
            Assert.Contains("Alpha loaded: ALPHA1.", model.History);
            Assert.Contains("Zeta loaded: ZETA1.", model.History);
            Assert.Contains(model.History, l =>
                l.StartsWith("Broken was not loaded: plugin.json: ", StringComparison.Ordinal)
            );
            Assert.False(model.HasPythonStarted);
        }

        [Fact]
        public void ALoadedTool_ShowsItsCommandsFromPluginJson_AsTheyAreDeclared_AndStartsNoProcessAndMakesNoVenv()
        {
            MainViewModel model = Using();
            string folder = Tool(
                "Stairs",
                "{ \"name\": \"Stairs\", \"commands\": [ { \"name\": \"stairspy\", \"alias\": \"stpy\", \"description\": \"Steps\" }, \"OTHERONE\" ] }"
            );

            AppLoadResult result = model.LoadApplication(folder);

            Assert.Equal(AppLoadStatusKind.Loaded, result.Kind);
            Assert.Equal("Stairs successfully loaded.", result.Message);
            Assert.Contains("Stairs loaded: OTHERONE, STAIRSPY.", model.History);
            Assert.NotNull(model.Session.Registry.Find("STAIRSPY"));
            Assert.NotNull(model.Session.Registry.Find("OTHERONE"));
            Assert.Equal("STAIRSPY", model.Session.Registry.Shortcuts.Aliases["STPY"]);
            Assert.Equal(new[] { "STAIRSPY", "OTHERONE" }, model.PythonCommandsOf(folder));
            Assert.Equal(new[] { folder }, model.LoadedPythonFiles);
            Assert.All(((PythonToolFiles)model.PythonFiles).Tools, tool =>
            {
                Assert.False(tool.IsRunning);
                Assert.Equal(0, tool.Starts);
            });
            Assert.False(Directory.Exists(Path.Combine(_folder.FullPath, "envs")));
            Assert.False(Directory.Exists(Path.Combine(_folder.FullPath, "base")));
        }

        [Fact]
        public void AToolLoadedFromItsRequirementsFile_IsTheToolOfItsFolder_AndUnloadingTakesItsCommandsOut()
        {
            MainViewModel model = Using();
            string folder = Tool(
                "Stairs",
                "{ \"name\": \"Stairs\", \"commands\": [ { \"name\": \"STAIRSPY\", \"alias\": \"STPY\" } ] }"
            );

            Assert.Equal(
                AppLoadStatusKind.Loaded,
                model.LoadApplication(Path.Combine(folder, "requirements.txt")).Kind
            );
            Assert.NotNull(model.Session.Registry.Find("STAIRSPY"));

            Assert.True(model.UnloadPythonFile(folder));

            Assert.Null(model.Session.Registry.Find("STAIRSPY"));
            Assert.False(model.Session.Registry.Shortcuts.Aliases.ContainsKey("STPY"));
            Assert.Empty(model.LoadedPythonFiles);
            Assert.False(model.UnloadPythonFile(folder));
        }

        [Fact]
        public void AToolWithNoPluginJson_LoadsWithTheLineThatItDeclaresNoCommand()
        {
            MainViewModel model = Using();
            string folder = Tool("Quiet", null);

            AppLoadResult result = model.LoadApplication(folder);

            Assert.Equal(AppLoadStatusKind.Loaded, result.Kind);
            Assert.Contains("Quiet declares no command in plugin.json.", model.History);
            Assert.Empty(model.PythonCommandsOf(folder));
        }

        [Fact]
        public void AToolWhosePluginJsonCannotBeRead_IsNotLoaded_WithTheLineThatSaysWhy()
        {
            MainViewModel model = Using();
            string folder = Tool("Broken", "this is not json");

            AppLoadResult result = model.LoadApplication(folder);

            Assert.Equal(AppLoadStatusKind.Failed, result.Kind);
            Assert.Contains(model.History, l =>
                l.StartsWith("Broken was not loaded: plugin.json: ", StringComparison.Ordinal)
            );
            Assert.Empty(model.LoadedPythonFiles);
        }

        [Fact]
        public void AToolWithNoInitPy_IsNotLoaded_WithTheLineThatSaysWhy()
        {
            MainViewModel model = Using();
            string folder = Tool("Empty", "{ \"name\": \"Empty\" }");
            File.Delete(Path.Combine(folder, "__init__.py"));

            AppLoadResult result = model.LoadApplication(folder);

            Assert.Equal(AppLoadStatusKind.Failed, result.Kind);
            Assert.Contains(
                "Empty has no __init__.py: a Python tool is a folder with requirements.txt and __init__.py.",
                model.History
            );
        }

        [Fact]
        public void ACommandAToolDeclaresThatIsACommandAlready_IsLeftToTheOneThatHasIt_WithALine()
        {
            MainViewModel model = Using();
            string folder = Tool("Clash", "{ \"name\": \"Clash\", \"commands\": [ \"LINE\", \"FRESH\" ] }");

            model.LoadApplication(folder);

            Assert.Contains("PYLOAD: LINE is a command already; Clash did not define it.", model.History);
            Assert.Contains("Clash loaded: FRESH.", model.History);
            Assert.Equal(new[] { "FRESH" }, model.PythonCommandsOf(folder));
        }

        [Fact]
        public void ALonePy_InPythonsOrTheSuite_OrGivenToPyload_IsRefusedWithTheLineThatSaysHowToMakeItATool()
        {
            MainViewModel model = Using();
            string lone = _folder.WriteText("DrawingInfo.py", "pass\n");
            string line =
                "DrawingInfo.py is a lone Python file: a Python tool is a folder with requirements.txt. Put it in Pythons\\DrawingInfo\\ as __init__.py (see the Python guide).";

            AppLoadResult byLoad = model.LoadApplication(lone);
            ((PythonToolFiles)model.PythonFiles).Startup(model.Session, new string[0], new[] { lone });

            Assert.Equal(AppLoadStatusKind.Refused, byLoad.Kind);
            Assert.Equal(line, byLoad.Message);
            model.RefreshHistory();
            Assert.Equal(2, model.History.Count(l => l == line));
            Assert.Empty(model.LoadedPythonFiles);
        }

        [Fact]
        public void AToolUnderSecureLoadTwo_OutsideTheTrustedFolders_IsRefusedWithItsFullPath_AndOneInsideLoads()
        {
            string trusted = Path.Combine(_folder.FullPath, "trusted");
            MainViewModel model = Using(2, trusted + "\\...");
            string inside = Tool("In", "{ \"name\": \"In\", \"commands\": [ \"IN1\" ] }", "trusted");
            string outside = Tool("Out", "{ \"name\": \"Out\", \"commands\": [ \"OUT1\" ] }", "other");

            AppLoadResult refused = model.LoadApplication(outside);
            AppLoadResult allowed = model.LoadApplication(inside);

            Assert.Equal(AppLoadStatusKind.Failed, refused.Kind);
            Assert.Contains("File load canceled: " + Path.Combine(outside, "__init__.py"), model.History);
            Assert.Equal(AppLoadStatusKind.Loaded, allowed.Kind);
            Assert.Null(model.Session.Registry.Find("OUT1"));
            Assert.NotNull(model.Session.Registry.Find("IN1"));
        }

        [Fact]
        public void SetvarSecureLoadZero_AfterPythonIsUsed_AllowsTheNextTool_BecauseTheGateIsReadLive()
        {
            MainViewModel model = Using(2);
            string folder = Tool("Gated", "{ \"name\": \"Gated\", \"commands\": [ \"GATED1\" ] }");
            Assert.Equal(AppLoadStatusKind.Failed, model.LoadApplication(folder).Kind);

            model.Execute("SETVAR");
            model.Execute("SECURELOAD");
            model.Execute("0");

            Assert.Equal(AppLoadStatusKind.Loaded, model.LoadApplication(folder).Kind);
        }

        [Fact]
        public void ATool_LoadedAgain_ReplacesItsCommands_AndTheOldOnesAreGone()
        {
            MainViewModel model = Using();
            string folder = Tool("Again", "{ \"name\": \"Again\", \"commands\": [ \"ONE\", \"TWO\" ] }");
            model.LoadApplication(folder);
            File.WriteAllText(
                Path.Combine(folder, "plugin.json"),
                "{ \"name\": \"Again\", \"commands\": [ \"ONE\", \"THREE\" ] }"
            );

            model.LoadApplication(folder);

            Assert.NotNull(model.Session.Registry.Find("ONE"));
            Assert.Null(model.Session.Registry.Find("TWO"));
            Assert.NotNull(model.Session.Registry.Find("THREE"));
            Assert.Equal(new[] { folder }, model.LoadedPythonFiles);
        }
    }
}
