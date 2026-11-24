using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Windows.Threading;
using Xunit;
using static OpenCAD2D.Wpf.UnitTest.PythonWindowKit;

namespace OpenCAD2D.Wpf.UnitTest
{
    /// <summary>
    /// The Python tools of the window with real processes (TF7): PYRUN of a file in a process of the default venv, PYLOAD of a
    /// folder and the command it declares run in the process the first command starts, a command declared and not defined
    /// failing its run with its line, and the question of pip on the command line. Each test makes a window's model on the
    /// thread of a dispatcher it pumps, with venvs in a temporary folder and a wheelhouse made in code: nothing reaches the
    /// network, the user's app data or a window.
    /// </summary>
    public sealed class PythonToolWindowTests : IClassFixture<ToolPython>, IDisposable
    {
        private readonly ToolPython _python;
        private readonly TestFolder _folder = new TestFolder();

        public PythonToolWindowTests(ToolPython python)
        {
            _python = python;
        }

        public void Dispose() => _folder.Dispose();

        /// <summary>A window's model with a drawing open and Python used, its venvs in the test's folder and its tools' channel under a name of its own.</summary>
        private MainViewModel Window(string wheelhouse = null)
        {
            var model = new MainViewModel { SettingsPath = Path.Combine(_folder.FullPath, "settings.json") };
            model.ApplySettings(
                new GlobalSettings { SecureLoad = 0, PythonPath = PythonEnvTestSupport.MachinePython.Executable }
            );
            Assert.True(model.Open(_folder.Write("room.dxf", TestFiles.Room(out _, out _))));
            model.UsePython(
                Dispatcher.CurrentDispatcher,
                new PythonToolSetup
                {
                    Environments =
                        wheelhouse == null
                            ? new PythonEnvironments(
                                Path.Combine(_folder.FullPath, "envs"),
                                PythonEnvTestSupport.SharedBase,
                                PythonEnvTestSupport.AppPython,
                                PythonEnvTestSupport.ShippedWheels
                            )
                            : PythonEnvTestSupport.Environments(_folder.FullPath, wheelhouse),
                    MakeHost = (host, dispatcher) =>
                        new CadToolHost(
                            host,
                            dispatcher,
                            _python.Library,
                            Guid.NewGuid().ToString("N").Substring(0, 8)
                        ),
                }
            );
            return model;
        }

        private string Tool(string name, string source, string manifest, string requirements = "")
        {
            string folder = Path.Combine(_folder.FullPath, "Pythons", name);
            Directory.CreateDirectory(folder);
            File.WriteAllText(Path.Combine(folder, "requirements.txt"), requirements, new UTF8Encoding(false));
            File.WriteAllText(Path.Combine(folder, "__init__.py"), source, new UTF8Encoding(false));
            File.WriteAllText(Path.Combine(folder, "plugin.json"), manifest, new UTF8Encoding(false));
            return folder;
        }

        private static void WaitFor(MainViewModel model, Func<bool> done, string what) =>
            Pump(
                () =>
                {
                    model.RefreshHistory();
                    return done();
                },
                what + " History: " + string.Join(" | ", model.History.Skip(Math.Max(0, model.History.Count - 12)))
            );

        private static bool Said(MainViewModel model, string line) => model.History.Contains(line);

        [Fact]
        public void Pyrun_OfAFile_RunsItInAProcessOfTheDefaultVenv_AsARunNamedPyrun_InOneUndoStep()
        {
            string script = _folder.WriteText(
                "ring.py",
                "import sys\n"
                    + "from opencad2d.cad import active_drawing, write\n"
                    + "drawing = active_drawing()\n"
                    + "drawing.add_circle((10, 20), 5)\n"
                    + "drawing.commit()\n"
                    + "write('name is ' + __name__)\n"
                    + "print('argv is ' + str(len(sys.argv)))\n"
            );

            OnWindowThread(() =>
            {
                MainViewModel model = Window();

                try
                {
                    int before = model.Document.EntityCount;
                    model.Execute("PYRUN");
                    model.Execute(script);

                    WaitFor(model, () => Said(model, "argv is 1") && !model.Session.IsRunning, "PYRUN did not end.");

                    Assert.Contains("name is __main__", model.History);
                    Assert.Equal(before + 1, model.Document.EntityCount);
                    Assert.Equal("PYRUN", model.Document.NextUndo.Name);
                    Assert.True(model.Document.Undo());
                    Assert.Equal(before, model.Document.EntityCount);
                }
                finally
                {
                    model.DisposePython();
                }
            });
        }

        [Fact]
        public void Pyrun_OfAFileThatRaises_WritesTheLine_TakesTheDrawingBack_AndTheNextPyrunWorks()
        {
            string bad = _folder.WriteText(
                "bad.py",
                "from opencad2d.cad import active_drawing\n"
                    + "drawing = active_drawing()\n"
                    + "drawing.add_circle((1, 1), 1)\n"
                    + "drawing.commit()\n"
                    + "raise ValueError('boom')\n"
            );
            string good = _folder.WriteText("good.py", "from opencad2d.cad import write\nwrite('good ran')\n");

            OnWindowThread(() =>
            {
                MainViewModel model = Window();

                try
                {
                    int before = model.Document.EntityCount;
                    model.Execute("PYRUN");
                    model.Execute(bad);
                    WaitFor(
                        model,
                        () => model.History.Any(l => l.Contains("ValueError: boom")) && !model.Session.IsRunning,
                        "the error of bad.py did not come."
                    );
                    Assert.Equal(before, model.Document.EntityCount);

                    model.Execute("PYRUN");
                    model.Execute(good);
                    WaitFor(model, () => Said(model, "good ran") && !model.Session.IsRunning, "good.py did not run.");
                }
                finally
                {
                    model.DisposePython();
                }
            });
        }

        [Fact]
        public void Pyload_OfAFolder_ShowsItsCommandsAtOnce_AndTheFirstCommandPreparesTheVenvAndStartsTheProcess()
        {
            string folder = Tool(
                "Greeter",
                "from opencad2d.cad import command, write\n"
                    + "@command('GREETME')\n"
                    + "def greet():\n"
                    + "    write('Hello from the tool')\n",
                "{ \"name\": \"Greeter\", \"commands\": [ \"GREETME\" ] }"
            );

            OnWindowThread(() =>
            {
                MainViewModel model = Window();

                try
                {
                    model.AskPythonFiles = (filter, start) => new[] { Path.Combine(folder, "requirements.txt") };
                    model.Execute("PYLOAD");

                    Assert.Contains("Greeter loaded: GREETME.", model.History);
                    Assert.NotNull(model.Session.Registry.Find("GREETME"));
                    Assert.Equal(new[] { folder }, model.LoadedPythonFiles);
                    Assert.False(Directory.Exists(Path.Combine(_folder.FullPath, "envs")));
                    CadFolderTool tool = ((PythonToolFiles)model.PythonFiles).Tools.Single();
                    Assert.False(tool.IsRunning);

                    model.Execute("GREETME");
                    WaitFor(
                        model,
                        () => Said(model, "Hello from the tool") && !model.Session.IsRunning,
                        "GREETME did not end."
                    );

                    Assert.True(tool.IsRunning);
                    Assert.Equal(1, tool.Starts);
                    Assert.True(model.HasPythonStarted);
                    Assert.True(Directory.Exists(Path.Combine(_folder.FullPath, "envs")));

                    Assert.True(model.UnloadPythonFile(folder));
                    Assert.Null(model.Session.Registry.Find("GREETME"));
                }
                finally
                {
                    model.DisposePython();
                }
            });
        }

        [Fact]
        public void ACommandPluginJsonDeclares_ButTheToolDoesNotDefine_FailsItsRunWithItsLine_AndTheNextCommandStillRuns()
        {
            string folder = Tool(
                "Ghosts",
                "from opencad2d.cad import command, write\n"
                    + "@command('REAL')\n"
                    + "def real():\n"
                    + "    write('real ran')\n",
                "{ \"name\": \"Ghosts\", \"commands\": [ \"GHOST\", \"REAL\" ] }"
            );

            OnWindowThread(() =>
            {
                MainViewModel model = Window();

                try
                {
                    Assert.Equal(AppLoadStatusKind.Loaded, model.LoadApplication(folder).Kind);

                    model.Execute("GHOST");
                    WaitFor(
                        model,
                        () =>
                            Said(model, "GHOST is declared by the tool, but it has no @command of that name.")
                            && !model.Session.IsRunning,
                        "the run of GHOST did not fail."
                    );

                    model.Execute("REAL");
                    WaitFor(model, () => Said(model, "real ran") && !model.Session.IsRunning, "REAL did not run.");
                }
                finally
                {
                    model.DisposePython();
                }
            });
        }

        [Fact]
        public void ATool_WithAPackage_AsksOnTheCommandLine_AndAYesInstallsItAndRunsTheCommand()
        {
            string wheelhouse = Path.Combine(_folder.FullPath, "wheelhouse");
            PythonEnvTestSupport.Wheel(wheelhouse, "demo-pkg", "1.0");
            string folder = Tool(
                "NeedsDemo",
                "import demo_pkg\n"
                    + "from opencad2d.cad import command, write\n"
                    + "@command('DEMOVERSION')\n"
                    + "def show():\n"
                    + "    write('demo_pkg ' + demo_pkg.VERSION)\n",
                "{ \"name\": \"NeedsDemo\", \"commands\": [ \"DEMOVERSION\" ] }",
                "demo-pkg==1.0\n"
            );

            OnWindowThread(() =>
            {
                MainViewModel model = Window(wheelhouse);

                try
                {
                    Assert.Equal(AppLoadStatusKind.Loaded, model.LoadApplication(folder).Kind);

                    model.Execute("DEMOVERSION");
                    WaitFor(
                        model,
                        () =>
                            model.Session.Prompt != null
                            && model.Session.Prompt.Text.StartsWith("Install with pip", StringComparison.Ordinal),
                        "the question of pip was not asked."
                    );
                    Assert.Contains("NeedsDemo needs these packages from the Python Package Index:", model.History);
                    Assert.Contains("  demo-pkg 1.0", model.History);
                    Assert.False(
                        Directory.Exists(Path.Combine(_folder.FullPath, "envs"))
                            && Directory
                                .GetDirectories(Path.Combine(_folder.FullPath, "envs"))
                                .Any(d => Directory.Exists(Path.Combine(d, "Lib", "site-packages", "demo_pkg")))
                    );

                    model.Execute("Yes");
                    WaitFor(
                        model,
                        () => Said(model, "demo_pkg 1.0") && !model.Session.IsRunning,
                        "DEMOVERSION did not run."
                    );
                }
                finally
                {
                    model.DisposePython();
                }
            });
        }

        [Fact]
        public void ATool_WithAPackage_AnsweredNo_IsNotInstalled_TheRunEndsWithTheLineThatSaysSo_AndNothingIsInstalled()
        {
            string wheelhouse = Path.Combine(_folder.FullPath, "wheelhouse");
            PythonEnvTestSupport.Wheel(wheelhouse, "demo-pkg", "1.0");
            string folder = Tool(
                "NeedsDemo",
                "import demo_pkg\n",
                "{ \"name\": \"NeedsDemo\", \"commands\": [ \"DEMOVERSION\" ] }",
                "demo-pkg==1.0\n"
            );

            OnWindowThread(() =>
            {
                MainViewModel model = Window(wheelhouse);

                try
                {
                    model.LoadApplication(folder);
                    model.Execute("DEMOVERSION");
                    WaitFor(
                        model,
                        () =>
                            model.Session.Prompt != null
                            && model.Session.Prompt.Text.StartsWith("Install with pip", StringComparison.Ordinal),
                        "the question of pip was not asked."
                    );

                    model.Execute("No");

                    WaitFor(
                        model,
                        () =>
                            model.History.Any(l =>
                                l.StartsWith(
                                    "NeedsDemo was not installed: its packages were not approved.",
                                    StringComparison.Ordinal
                                )
                            ) && !model.Session.IsRunning,
                        "the run did not end with the line."
                    );
                    Assert.False(((PythonToolFiles)model.PythonFiles).Tools.Single().IsRunning);
                }
                finally
                {
                    model.DisposePython();
                }
            });
        }

        [Fact]
        public void Esc_AtTheQuestionOfPip_IsANo_AndTheRunEnds()
        {
            string wheelhouse = Path.Combine(_folder.FullPath, "wheelhouse");
            PythonEnvTestSupport.Wheel(wheelhouse, "demo-pkg", "1.0");
            string folder = Tool(
                "NeedsDemo",
                "import demo_pkg\n",
                "{ \"name\": \"NeedsDemo\", \"commands\": [ \"DEMOVERSION\" ] }",
                "demo-pkg==1.0\n"
            );

            OnWindowThread(() =>
            {
                MainViewModel model = Window(wheelhouse);

                try
                {
                    model.LoadApplication(folder);
                    model.Execute("DEMOVERSION");
                    WaitFor(
                        model,
                        () =>
                            model.Session.Prompt != null
                            && model.Session.Prompt.Text.StartsWith("Install with pip", StringComparison.Ordinal),
                        "the question of pip was not asked."
                    );

                    model.Session.Escape();

                    WaitFor(model, () => !model.Session.IsRunning, "the run did not end.");
                    Assert.False(((PythonToolFiles)model.PythonFiles).Tools.Single().IsRunning);
                }
                finally
                {
                    model.DisposePython();
                }
            });
        }

        [Fact]
        public void AChangeOfRequirementsThatCannotBeInstalled_LeavesTheToolOnItsOldVenv_AndTheLinesAreWritten()
        {
            string wheelhouse = Path.Combine(_folder.FullPath, "wheelhouse");
            PythonEnvTestSupport.Wheel(wheelhouse, "demo-pkg", "1.0");
            string folder = Tool(
                "Steady",
                "from opencad2d.cad import command, write\n@command('STEADY')\ndef steady():\n    write('steady ran')\n",
                "{ \"name\": \"Steady\", \"commands\": [ \"STEADY\" ] }"
            );

            OnWindowThread(() =>
            {
                MainViewModel model = Window(wheelhouse);

                try
                {
                    model.LoadApplication(folder);
                    model.Execute("STEADY");
                    WaitFor(model, () => Said(model, "steady ran") && !model.Session.IsRunning, "STEADY did not run.");
                    Assert.True(model.UnloadPythonFile(folder));
                    File.WriteAllText(Path.Combine(folder, "requirements.txt"), "demo-pkg==9.9\n");
                    model.LoadApplication(folder);

                    model.Execute("STEADY");
                    WaitFor(
                        model,
                        () =>
                            Said(model, "steady ran")
                            && model.History.Count(l => l == "steady ran") == 2
                            && !model.Session.IsRunning,
                        "STEADY did not run on the old venv."
                    );

                    Assert.Contains(model.History, l =>
                        l.StartsWith("ERROR: ", StringComparison.Ordinal) || l.Contains("No matching distribution")
                    );
                    Assert.Contains(model.History, l =>
                        l.StartsWith("Steady was not installed", StringComparison.Ordinal)
                    );
                }
                finally
                {
                    model.DisposePython();
                }
            });
        }

        [Fact]
        public void ALoadedToolsRibbonFromPluginJson_IsShownAtLoad_WithNoProcess()
        {
            string folder = Tool(
                "Ribboned",
                "pass\n",
                "{ \"name\": \"Ribboned\", \"commands\": [ \"RIBBONCMD\" ], \"ribbon\": [ { \"id\": \"pytools\", \"title\": \"Py Tools\", \"panels\": [ { \"title\": \"Draw\", \"buttons\": [ { \"text\": \"Go\", \"command\": \"RIBBONCMD\", \"size\": \"large\", \"tooltip\": \"Runs it\" } ] } ] } ] }"
            );

            OnWindowThread(() =>
            {
                MainViewModel model = Window();

                try
                {
                    Assert.Empty(model.PluginTabs);

                    Assert.Equal(AppLoadStatusKind.Loaded, model.LoadApplication(folder).Kind);

                    CadWorkspaceExtension tab = Assert.Single(model.PluginTabs);
                    Assert.Equal("Py Tools", tab.TabHeader);
                    Assert.Equal(new[] { "Draw" }, ((PluginTabViewModel)tab.TabContent).Panels.Select(p => p.Title));
                    Assert.False(((PythonToolFiles)model.PythonFiles).Tools.Single().IsRunning);

                    Assert.True(model.UnloadPythonFile(folder));
                    Assert.Empty(model.PluginTabs);
                }
                finally
                {
                    model.DisposePython();
                }
            });
        }
    }
}
