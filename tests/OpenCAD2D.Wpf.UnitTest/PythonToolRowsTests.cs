using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Threading;
using Xunit;
using static OpenCAD2D.Wpf.UnitTest.PythonWindowKit;

namespace OpenCAD2D.Wpf.UnitTest
{
    /// <summary>
    /// The question of pip and the rows of the Python tools in Load/Unload Applications (TF6b): the dialog is asked on the window's
    /// thread, with the packages the tool names first and each with what it was asked as; a No leaves the row with the badge
    /// "needs packages" and the next command asks again; "installing…" shows on the row while pip works on a thread of its own
    /// and the window stays free; a failed install gives "install failed" with pip's first ERROR line in red; a failed change
    /// that leaves the tool on its old venv gives the red line and no badge. Each test makes a window's model on the thread of a
    /// dispatcher it pumps, with venvs in a temporary folder and a wheelhouse made in code: nothing reaches the network, the
    /// user's app data or a real window.
    /// </summary>
    public sealed class PythonToolRowsTests : IClassFixture<ToolPython>, IDisposable
    {
        private const string NeedsDemo = "NeedsDemo";
        private readonly ToolPython _python;
        private readonly TestFolder _folder = new TestFolder();
        private PythonEnvironments _environments;

        public PythonToolRowsTests(ToolPython python)
        {
            _python = python;
        }

        public void Dispose() => _folder.Dispose();

        private static PythonPackage[] Packages() =>
            new[]
            {
                new PythonPackage("packaging", "24.2", false, "for shapely"),
                new PythonPackage("shapely", "2.0.0", true, "shapely>=2.0"),
                new PythonPackage("numpy", "2.1.3", true, "numpy==2.1.3"),
            };

        private string Wheelhouse()
        {
            string wheelhouse = Path.Combine(_folder.FullPath, "wheelhouse");
            PythonEnvTestSupport.Wheel(wheelhouse, "demo-pkg", "1.0");
            PythonEnvTestSupport.Wheel(wheelhouse, "top-pkg", "1.0", new[] { "demo-pkg>=1.0" });
            return wheelhouse;
        }

        private string Tool(string name, string requirements, string command = "DEMOVERSION")
        {
            string folder = Path.Combine(_folder.FullPath, "Pythons", name);
            Directory.CreateDirectory(folder);
            File.WriteAllText(Path.Combine(folder, "requirements.txt"), requirements, new UTF8Encoding(false));
            File.WriteAllText(
                Path.Combine(folder, "__init__.py"),
                "from opencad2d.cad import command, write\n"
                    + "@command('"
                    + command
                    + "')\n"
                    + "def show():\n"
                    + "    write('"
                    + name
                    + " ran')\n",
                new UTF8Encoding(false)
            );
            File.WriteAllText(
                Path.Combine(folder, "plugin.json"),
                "{ \"name\": \"" + name + "\", \"commands\": [ \"" + command + "\" ] }",
                new UTF8Encoding(false)
            );
            return folder;
        }

        /// <summary>A window's model with a drawing open and Python used, its venvs in the test's folder and its tools' channel under a name of its own.</summary>
        private MainViewModel Window(string wheelhouse)
        {
            var model = new MainViewModel { SettingsPath = Path.Combine(_folder.FullPath, "settings.json") };
            model.ApplySettings(
                new GlobalSettings { SecureLoad = 0, PythonPath = PythonEnvTestSupport.MachinePython.Executable }
            );
            Assert.True(model.Open(_folder.Write("room.dxf", TestFiles.Room(out _, out _))));
            _environments = PythonEnvTestSupport.Environments(_folder.FullPath, wheelhouse);
            model.UsePython(
                Dispatcher.CurrentDispatcher,
                new PythonToolSetup
                {
                    Environments = _environments,
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

        private static AppLoadRow Row(AppLoadViewModel dialog, string name) =>
            Assert.Single(dialog.Rows, r => r.Name == name);

        // ---- The question

        [Fact]
        public void ThePipQuestion_HasTheTextsOfTheBoard_ThePackagesTheToolNamesFirst_AndAskedAsInTheThirdColumn()
        {
            PackageQuestionViewModel question = PythonPackageQuestion.Question(
                "Survey",
                "D:\\Tools\\Survey\\requirements.txt",
                Packages()
            );

            Assert.Equal("Packages for Python tool Survey", question.Title);
            Assert.Equal(
                "The tool Survey (D:\\Tools\\Survey\\requirements.txt) needs these packages in its own environment. "
                    + "pip worked out the list; nothing is installed yet.",
                question.Lead
            );
            Assert.Equal("Asked as", question.ThirdHead);
            Assert.Equal(new[] { "numpy", "shapely", "packaging" }, question.Rows.Select(r => r.Name));
            Assert.Equal(new[] { "2.1.3", "2.0.0", "24.2" }, question.Rows.Select(r => r.Version));
            Assert.Equal(new[] { "numpy==2.1.3", "shapely>=2.0", "for shapely" }, question.Rows.Select(r => r.Note));
            Assert.Equal(new[] { true, true, false }, question.Rows.Select(r => r.IsDirect));
            Assert.DoesNotContain(question.Rows, r => r.IsNoteHighlighted);
            Assert.Equal(
                "Installing runs the packages\u2019 code with your rights. The tool gets an environment of its own in "
                    + "%LocalAppData%\\OpenCAD2D\\python\\envs, so its versions never change another tool\u2019s.",
                question.Foot
            );
            Assert.Equal("With pip, from the Python package index", question.Source);
            Assert.Equal("Don\u2019t install", question.NoLabel);
            Assert.Equal("Install", question.YesLabel);
        }

        [Fact]
        public void ThePipQuestion_InTheWindow_ShowsTheBoardsTextsWithNoWarning()
        {
            string[] texts = null;
            Size size = default;

            Thread thread = new Thread(() =>
            {
                _ = System.IO.Packaging.PackUriHelper.UriSchemePack;
                var window = new PackageQuestionWindow(
                    PythonPackageQuestion.Question("Survey", "D:\\Tools\\Survey\\requirements.txt", Packages())
                );
                FrameworkElement root = window.Body;
                window.Content = null;
                root.DataContext = window.Model;
                root.Measure(new Size(560, 440));
                root.Arrange(new Rect(0, 0, 560, 440));
                root.UpdateLayout();
                List<DependencyObject> all = Descendants(root).ToList();
                size = new Size(window.Width, window.Height);
                texts = all.OfType<TextBlock>()
                    .Select(t => t.Text)
                    .Concat(all.OfType<Button>().Select(b => b.Content as string))
                    .ToArray();
                window.Close();
            });
            thread.SetApartmentState(ApartmentState.STA);
            thread.Start();
            thread.Join();

            Assert.Equal(new Size(560, 440), size);

            foreach (
                string text in new[]
                {
                    "Packages for Python tool Survey",
                    "Package",
                    "Version",
                    "Asked as",
                    "numpy",
                    "2.1.3",
                    "numpy==2.1.3",
                    "shapely>=2.0",
                    "for shapely",
                    "With pip, from the Python package index",
                    "Don\u2019t install",
                    "Install",
                }
            )
            {
                Assert.Contains(text, texts);
            }

            Assert.DoesNotContain("State", texts);
            Assert.DoesNotContain(texts, t => t != null && t.Contains("range"));
        }

        [Fact]
        public void TheQuestionOfPip_IsAskedInTheDialogOnTheWindowsThread_WhileTheEnvironmentIsMadeOffIt_AndYesInstallsAndRuns()
        {
            string wheelhouse = Wheelhouse();
            string folder = Tool(NeedsDemo, "top-pkg>=1.0\n");

            OnWindowThread(() =>
            {
                int windowThread = Environment.CurrentManagedThreadId;
                int askedOn = 0;
                int madeOn = 0;
                PackageQuestionViewModel asked = null;
                MainViewModel model = Window(wheelhouse);

                try
                {
                    Func<string, IReadOnlyList<PythonPackage>, bool> confirm = _environments.Confirm;
                    _environments.Confirm = (tool, packages) =>
                    {
                        madeOn = Environment.CurrentManagedThreadId;
                        return confirm(tool, packages);
                    };
                    model.AskPythonPackages = question =>
                    {
                        askedOn = Environment.CurrentManagedThreadId;
                        asked = question;
                        return true;
                    };
                    Assert.Equal(AppLoadStatusKind.Loaded, model.LoadApplication(folder).Kind);

                    model.Execute("DEMOVERSION");
                    WaitFor(
                        model,
                        () => Said(model, NeedsDemo + " ran") && !model.Session.IsRunning,
                        "DEMOVERSION did not run."
                    );

                    Assert.Equal(windowThread, askedOn);
                    Assert.NotNull(asked);
                    Assert.Equal("Packages for Python tool " + NeedsDemo, asked.Title);
                    Assert.Contains(Path.Combine(folder, "requirements.txt"), asked.Lead);
                    Assert.Equal(new[] { "top-pkg", "demo-pkg" }, asked.Rows.Select(r => r.Name));
                    Assert.Equal(new[] { "top-pkg>=1.0", "for top-pkg" }, asked.Rows.Select(r => r.Note));
                    Assert.Equal(new[] { true, false }, asked.Rows.Select(r => r.IsDirect));
                    Assert.NotEqual(0, madeOn);
                    Assert.NotEqual(windowThread, madeOn);
                    Assert.DoesNotContain(model.History, l =>
                        l.StartsWith("Install with pip", StringComparison.Ordinal)
                    );
                    Assert.Equal(AppLoadBadge.None, model.PythonStateOf(folder).Badge);
                }
                finally
                {
                    model.DisposePython();
                }
            });
        }

        [Fact]
        public void WithFiledia0_TheQuestionOfPipIsAskedOnTheCommandLine_NotInTheDialog()
        {
            string wheelhouse = Wheelhouse();
            string folder = Tool(NeedsDemo, "demo-pkg==1.0\n");

            OnWindowThread(() =>
            {
                MainViewModel model = Window(wheelhouse);
                int dialogs = 0;

                try
                {
                    model.AskPythonPackages = question =>
                    {
                        dialogs++;
                        return true;
                    };
                    model.Execute("FILEDIA");
                    model.Execute("0");
                    model.LoadApplication(folder);

                    model.Execute("DEMOVERSION");
                    WaitFor(
                        model,
                        () =>
                            model.Session.Prompt != null
                            && model.Session.Prompt.Text.StartsWith("Install with pip", StringComparison.Ordinal),
                        "the question of pip was not asked on the command line."
                    );
                    model.Execute("Yes");
                    WaitFor(
                        model,
                        () => Said(model, NeedsDemo + " ran") && !model.Session.IsRunning,
                        "DEMOVERSION did not run."
                    );

                    Assert.Equal(0, dialogs);
                }
                finally
                {
                    model.DisposePython();
                }
            });
        }

        // ---- The rows

        [Fact]
        public void ANo_LeavesTheRowWithTheBadgeNeedsPackages_SaysItWasNotApproved_AndTheNextCommandAsksAgain()
        {
            string wheelhouse = Wheelhouse();
            string folder = Tool(NeedsDemo, "demo-pkg==1.0\n");

            OnWindowThread(() =>
            {
                MainViewModel model = Window(wheelhouse);
                int asked = 0;
                bool answer = false;

                try
                {
                    model.AskPythonPackages = question =>
                    {
                        asked++;
                        return answer;
                    };
                    model.LoadApplication(folder);
                    var dialog = new AppLoadViewModel(model);

                    try
                    {
                        Assert.Equal(AppLoadBadge.None, Row(dialog, NeedsDemo).Badge);

                        model.Execute("DEMOVERSION");
                        WaitFor(
                            model,
                            () =>
                                model.History.Any(l =>
                                    l.StartsWith(
                                        NeedsDemo + " was not installed: its packages were not approved.",
                                        StringComparison.Ordinal
                                    )
                                ) && !model.Session.IsRunning,
                            "the run did not end with the line."
                        );

                        AppLoadRow row = Row(dialog, NeedsDemo);
                        Assert.Equal(1, asked);
                        Assert.Equal(AppLoadBadge.NeedsPackages, row.Badge);
                        Assert.Equal("needs packages", row.BadgeText);
                        Assert.True(row.HasBadge);
                        Assert.True(row.IsOn);
                        Assert.Contains("next command asks again", row.BadgeToolTip);
                        Assert.False(dialog.HasLoadError);

                        answer = true;
                        model.Execute("DEMOVERSION");
                        WaitFor(
                            model,
                            () => Said(model, NeedsDemo + " ran") && !model.Session.IsRunning,
                            "the second run did not end."
                        );

                        Assert.Equal(2, asked);
                        Assert.Equal(AppLoadBadge.None, Row(dialog, NeedsDemo).Badge);
                        Assert.False(Row(dialog, NeedsDemo).HasBadge);
                    }
                    finally
                    {
                        dialog.Dispose();
                    }
                }
                finally
                {
                    model.DisposePython();
                }
            });
        }

        [Fact]
        public void WhilePipInstalls_TheRowShowsInstallingInBlue_TheWindowStaysFree_AndTheBadgeClearsWhenItEnds()
        {
            string wheelhouse = Wheelhouse();
            string folder = Tool(NeedsDemo, "demo-pkg==1.0\n");

            OnWindowThread(() =>
            {
                int windowThread = Environment.CurrentManagedThreadId;
                MainViewModel model = Window(wheelhouse);
                int madeOn = 0;
                AppLoadBadge seen = AppLoadBadge.None;
                string text = null;
                string tip = null;
                string[] texts = null;
                Color fill = default;
                Color line = default;
                Color label = default;
                int ticks = 0;
                bool rowStillInstallingAfterTick = false;

                try
                {
                    Func<string, IReadOnlyList<PythonPackage>, bool> confirm = _environments.Confirm;
                    _environments.Confirm = (tool, packages) =>
                    {
                        // Where pip is asked: on the thread of the environment, not the window's.
                        madeOn = Environment.CurrentManagedThreadId;
                        return confirm(tool, packages);
                    };
                    model.AskPythonPackages = question => true;
                    model.LoadApplication(folder);
                    var dialog = new AppLoadViewModel(model);

                    try
                    {
                        model.Execute("DEMOVERSION");

                        // The badge appears while pip works; the window is laid out with it, and the dispatcher keeps running.
                        Pump(
                            () =>
                            {
                                AppLoadRow row = dialog.Rows.Single(r => r.Name == NeedsDemo);

                                if (seen == AppLoadBadge.None && row.Badge == AppLoadBadge.Installing)
                                {
                                    seen = row.Badge;
                                    text = row.BadgeText;
                                    tip = row.BadgeToolTip;
                                    Dispatcher.CurrentDispatcher.BeginInvoke(
                                        new Action(() =>
                                        {
                                            ticks++;
                                            rowStillInstallingAfterTick = row.Badge == AppLoadBadge.Installing;
                                        })
                                    );
                                    // The window disposes the model it is given when it closes: it gets one of its own.
                                    var shown = new AppLoadViewModel(model);
                                    var window = new AppLoadWindow(shown);
                                    FrameworkElement root = window.Body;
                                    window.Content = null;
                                    root.DataContext = shown;
                                    root.Measure(new Size(900, 700));
                                    root.Arrange(new Rect(0, 0, 900, 700));
                                    root.UpdateLayout();
                                    List<DependencyObject> all = Descendants(root).ToList();
                                    texts = all.OfType<TextBlock>().Select(t => t.Text).ToArray();
                                    Border badge = all.OfType<Border>()
                                        .Single(b => b.Name == "Badge" && b.Visibility == Visibility.Visible);
                                    fill = ((SolidColorBrush)badge.Background).Color;
                                    line = ((SolidColorBrush)badge.BorderBrush).Color;
                                    label = (
                                        (SolidColorBrush)
                                            all.OfType<TextBlock>()
                                                .First(t => t.Name == "BadgeLabel" && t.ActualWidth > 0)
                                                .Foreground
                                    ).Color;
                                    window.Close();
                                }

                                return seen != AppLoadBadge.None && ticks > 0;
                            },
                            "the row never showed installing."
                        );

                        WaitFor(
                            model,
                            () => Said(model, NeedsDemo + " ran") && !model.Session.IsRunning,
                            "DEMOVERSION did not run."
                        );

                        Assert.Equal(AppLoadBadge.Installing, seen);
                        Assert.Equal("installing\u2026", text);
                        Assert.Contains("pip is installing", tip);
                        Assert.Contains("installing\u2026", texts);
                        Assert.Equal(Color.FromRgb(0x14, 0x30, 0x4F), fill);
                        Assert.Equal(Color.FromRgb(0x3D, 0x9B, 0xFF), line);
                        Assert.Equal(Color.FromRgb(0x9C, 0xCB, 0xFF), label);
                        Assert.Equal(1, ticks);
                        Assert.True(rowStillInstallingAfterTick);
                        Assert.NotEqual(0, madeOn);
                        Assert.NotEqual(windowThread, madeOn);
                        Assert.Equal(AppLoadBadge.None, Row(dialog, NeedsDemo).Badge);
                    }
                    finally
                    {
                        dialog.Dispose();
                    }
                }
                finally
                {
                    model.DisposePython();
                }
            });
        }

        [Fact]
        public void AnInstallThatFails_ShowsInstallFailed_AndARedLineWithPipsFirstErrorLine_EveryLineBeingOnTheCommandLine()
        {
            string wheelhouse = Wheelhouse();
            string folder = Tool("Grid", "demo-pkg==9.9\n", "GRIDPY");

            OnWindowThread(() =>
            {
                MainViewModel model = Window(wheelhouse);
                int asked = 0;

                try
                {
                    model.AskPythonPackages = question =>
                    {
                        asked++;
                        return true;
                    };
                    model.LoadApplication(folder);
                    var dialog = new AppLoadViewModel(model);

                    try
                    {
                        model.Execute("GRIDPY");
                        WaitFor(
                            model,
                            () =>
                                model.History.Any(l => l.Contains("No matching distribution"))
                                && !model.Session.IsRunning,
                            "the run of GRIDPY did not fail with pip's lines."
                        );

                        AppLoadRow row = Row(dialog, "Grid");
                        Assert.Equal(0, asked);
                        Assert.Equal(AppLoadBadge.InstallFailed, row.Badge);
                        Assert.Equal("install failed", row.BadgeText);
                        Assert.True(dialog.HasLoadError);
                        Assert.Equal("Grid", dialog.LoadErrorName);
                        Assert.StartsWith(
                            " was not installed: ERROR: Could not find a version that satisfies the requirement demo-pkg==9.9",
                            dialog.LoadErrorText
                        );
                        Assert.DoesNotContain("\n", dialog.LoadErrorText);
                        Assert.Contains("ERROR: No matching distribution found for demo-pkg==9.9", dialog.LoadError);
                        Assert.Contains("Grid was not installed. If this computer is offline", dialog.LoadError);
                        Assert.Equal("Every line is on the command line", dialog.LoadErrorHint);
                        Assert.Contains(model.History, l =>
                            l.StartsWith("ERROR: Could not find a version", StringComparison.Ordinal)
                        );
                        Assert.False(((PythonToolFiles)model.PythonFiles).Tools.Single().IsRunning);
                    }
                    finally
                    {
                        dialog.Dispose();
                    }
                }
                finally
                {
                    model.DisposePython();
                }
            });
        }

        [Fact]
        public void AChangeThatFails_ButLeavesTheToolOnItsOldVenv_GivesTheRedLineAndNoFailedBadge_AndTheToolStillRuns()
        {
            string wheelhouse = Wheelhouse();
            string folder = Tool("Steady", string.Empty, "STEADY");

            OnWindowThread(() =>
            {
                MainViewModel model = Window(wheelhouse);

                try
                {
                    model.AskPythonPackages = question => true;
                    model.LoadApplication(folder);
                    model.Execute("STEADY");
                    WaitFor(model, () => Said(model, "Steady ran") && !model.Session.IsRunning, "STEADY did not run.");
                    Assert.True(model.UnloadPythonFile(folder));
                    File.WriteAllText(Path.Combine(folder, "requirements.txt"), "demo-pkg==9.9\n");
                    model.LoadApplication(folder);
                    var dialog = new AppLoadViewModel(model);

                    try
                    {
                        model.Execute("STEADY");
                        WaitFor(
                            model,
                            () => model.History.Count(l => l == "Steady ran") == 2 && !model.Session.IsRunning,
                            "STEADY did not run on the old venv."
                        );

                        AppLoadRow row = Row(dialog, "Steady");
                        Assert.Equal(AppLoadBadge.None, row.Badge);
                        Assert.False(row.HasBadge);
                        Assert.True(row.IsOn);
                        Assert.True(dialog.HasLoadError);
                        Assert.Equal("Steady", dialog.LoadErrorName);
                        Assert.StartsWith(" was not installed: ERROR: ", dialog.LoadErrorText);
                        Assert.Equal("Every line is on the command line", dialog.LoadErrorHint);
                        Assert.True(((PythonToolFiles)model.PythonFiles).Tools.Single().IsRunning);
                    }
                    finally
                    {
                        dialog.Dispose();
                    }
                }
                finally
                {
                    model.DisposePython();
                }
            });
        }
    }
}
