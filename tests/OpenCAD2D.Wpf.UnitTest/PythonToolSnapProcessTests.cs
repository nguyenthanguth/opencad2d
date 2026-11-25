using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Text;
using System.Windows.Threading;
using GeometryHelper.Geometry;
using OpenCAD2D.Plugins;
using Xunit;
using static OpenCAD2D.Wpf.UnitTest.PythonWindowKit;

namespace OpenCAD2D.Wpf.UnitTest
{
    /// <summary>
    /// The object snaps and settings of Python tools with the real processes: a tool listed with the plugin snaps under its name,
    /// its process started by the first snap that needs it (never one that would ask a question), the answer showing the point with
    /// no other move, its snaps leaving with the tool and its keyword left out where a plugin has it, and a setting OK of Plugin
    /// Settings wrote read by the tool at its next <c>setting()</c>. Each test makes a window's model on the thread of a dispatcher
    /// it pumps, with venvs in a temporary folder: nothing reaches the network, the user's app data or a real window.
    /// </summary>
    public sealed class PythonToolSnapProcessTests : IClassFixture<ToolPython>, IDisposable
    {
        private static readonly GeoPoint2 NearTheTop = new GeoPoint2(2004, 1597);
        private const double Aperture = 20.0;

        private const string TopSnap =
            "from opencad2d.cad import SnapRequest, snap\n"
            + "@snap('CTOP')\n"
            + "def top(request):\n"
            + "    c = request.entity\n"
            + "    return [(c.Center.X, c.Center.Y + c.Radius)]\n";

        private readonly ToolPython _python;
        private readonly PluginWindowKit _kit = new PluginWindowKit();

        public PythonToolSnapProcessTests(ToolPython python)
        {
            _python = python;
        }

        public void Dispose() => _kit.Dispose();

        private static string Snaps(params string[] keywords) =>
            string.Join(
                ", ",
                keywords.Select(k =>
                    "{ \"keyword\": \""
                    + k
                    + "\", \"name\": \""
                    + (k == "CTOP" ? "Circle top" : "Circle bottom")
                    + "\", \"entityTypes\": [\"CIRCLE\"], \"onByDefault\": "
                    + (k == "CTOP" ? "true" : "false")
                    + " }"
                )
            );

        /// <summary>Makes a tool's folder under the kit's: its code, its plugin.json (the text between the braces after the name) and its requirements.</summary>
        private string Tool(string name, string source, string manifest, string requirements = "")
        {
            string folder = Path.Combine(_kit.Folder.FullPath, "Pythons", name);
            Directory.CreateDirectory(folder);
            File.WriteAllText(Path.Combine(folder, "requirements.txt"), requirements, new UTF8Encoding(false));
            File.WriteAllText(Path.Combine(folder, "__init__.py"), source, new UTF8Encoding(false));
            File.WriteAllText(
                Path.Combine(folder, "plugin.json"),
                "{ \"name\": \"" + name + "\", " + manifest + " }",
                new UTF8Encoding(false)
            );
            return folder;
        }

        /// <summary>A window's model with the columns open and plugins and Python used, its venvs in the kit's folder.</summary>
        private MainViewModel Window(PythonEnvironments environments = null)
        {
            MainViewModel model = _kit.Opened(out _, out _, out _);
            model.SettingsPath = Path.Combine(_kit.Folder.FullPath, "settings.json");
            model.ApplySettings(
                new GlobalSettings { SecureLoad = 0, PythonPath = PythonEnvTestSupport.MachinePython.Executable }
            );
            model.UsePython(
                Dispatcher.CurrentDispatcher,
                new PythonToolSetup
                {
                    Environments = environments
                        ?? new PythonEnvironments(
                            Path.Combine(_kit.Folder.FullPath, "envs"),
                            PythonEnvTestSupport.SharedBase,
                            PythonEnvTestSupport.AppPython,
                            PythonEnvTestSupport.ShippedWheels
                        ),
                    MakeHost = (host, dispatcher) =>
                        new CadToolHost(
                            host,
                            dispatcher,
                            _python.Library,
                            Guid.NewGuid().ToString("N").Substring(0, 8)
                        ),
                }
            );
            model.SnapModes = CadSnapModes.None;
            return model;
        }

        private static CadSnapResult SnapAt(MainViewModel model) =>
            CadPicking.Snap(model.Document, model.Document.ModelSpace, model.Snap, NearTheTop, Aperture, null);

        private static CadFolderTool ToolOf(MainViewModel model) => ((PythonToolFiles)model.PythonFiles).Tools.Single();

        private static void Wait(MainViewModel model, Func<bool> done, string what) =>
            Pump(
                () =>
                {
                    model.RefreshHistory();
                    return done();
                },
                what + " History: " + string.Join(" | ", model.History.Skip(Math.Max(0, model.History.Count - 10)))
            );

        [Fact]
        public void AToolsSnaps_AreListedWithThePluginSnapsUnderTheToolsName_OnAsItsPluginJsonSays()
        {
            OnWindowThread(() =>
            {
                MainViewModel model = Window();

                try
                {
                    string folder = Tool("Circles", TopSnap, "\"snaps\": [ " + Snaps("CTOP", "CBOT") + " ]");

                    Assert.Equal(AppLoadStatusKind.Loaded, model.LoadApplication(folder).Kind);

                    Assert.Equal(new[] { "CTOP", "CBOT" }, model.PluginSnapList.Select(i => i.Snap.Keyword));
                    Assert.All(model.PluginSnapList, i => Assert.Equal("Circles", i.PluginName));
                    Assert.Equal(new[] { "CTOP" }, model.Snap.CustomSnaps.Select(s => s.Keyword));
                    IReadOnlyList<CadMenuItemViewModel> menu = model.OsnapMenu();
                    Assert.True(menu.Single(i => i.Header == "Circle top").IsChecked);
                    Assert.False(menu.Single(i => i.Header == "Circle bottom").IsChecked);
                    DraftingSettingsViewModel dialog = model.CreateDraftingSettings();
                    Assert.True(dialog.HasPluginSnaps);
                    Assert.Equal(new[] { "Circle top", "Circle bottom" }, dialog.PluginSnaps.Select(r => r.Name));
                    Assert.All(dialog.PluginSnaps, r => Assert.Equal("Circles", r.PluginName));
                    Assert.Equal(new[] { true, false }, dialog.PluginSnaps.Select(r => r.IsChecked));
                    Assert.Equal(0, ToolOf(model).Starts);
                }
                finally
                {
                    model.DisposePython();
                }
            });
        }

        [Fact]
        public void ATool_WithNoProcessAndAReadyVenv_IsStartedByItsSnapsFirstMiss_AndThePointShowsWithNoOtherMove()
        {
            OnWindowThread(() =>
            {
                MainViewModel model = Window();

                try
                {
                    model.LoadApplication(Tool("Circles", TopSnap, "\"snaps\": [ " + Snaps("CTOP") + " ]"));
                    CadFolderTool tool = ToolOf(model);
                    int answered = 0;
                    model.PluginSnapAnswered += (sender, e) => answered++;
                    Assert.Equal(0, tool.Starts);

                    Assert.Equal(CadSnapKind.None, SnapAt(model).Kind);

                    Wait(model, () => answered > 0, "The tool's answer did not come.");
                    Assert.Equal(1, tool.Starts);
                    Assert.Equal(new[] { "CTOP" }, tool.BoundSnaps);
                    CadSnapResult result = SnapAt(model);
                    Assert.Equal(CadSnapKind.Custom, result.Kind);
                    Assert.Equal(2000.0, result.Point.X, 6);
                    Assert.Equal(1600.0, result.Point.Y, 6);
                }
                finally
                {
                    model.DisposePython();
                }
            });
        }

        [Fact]
        public void ATool_WhoseSnapDeclaresNoFinder_IsSwitchedOffWithTheToolsWords_AfterItsProcessStarted()
        {
            OnWindowThread(() =>
            {
                MainViewModel model = Window();

                try
                {
                    model.LoadApplication(Tool("Circles", "pass\n", "\"snaps\": [ " + Snaps("CTOP") + " ]"));
                    int answered = 0;
                    model.PluginSnapAnswered += (sender, e) => answered++;

                    SnapAt(model);
                    Wait(model, () => answered > 0, "The tool's answer did not come.");
                    SnapAt(model);

                    Assert.Contains(
                        "The snap CTOP of Circles failed and is off: CTOP has no @snap in the tool.",
                        model.History
                    );
                }
                finally
                {
                    model.DisposePython();
                }
            });
        }

        [Fact]
        public void AToolWhosePackagesAreNotApproved_IsNotStartedByASnap_AndNothingIsAsked()
        {
            OnWindowThread(() =>
            {
                string wheelhouse = Path.Combine(_kit.Folder.FullPath, "wheelhouse");
                PythonEnvTestSupport.Wheel(wheelhouse, "demo-pkg", "1.0");
                var environments = PythonEnvTestSupport.Environments(_kit.Folder.FullPath, wheelhouse);
                int asked = 0;
                environments.Confirm = (tool, packages) =>
                {
                    asked++;
                    return false;
                };
                MainViewModel model = Window(environments);

                try
                {
                    model.LoadApplication(
                        Tool("Circles", TopSnap, "\"snaps\": [ " + Snaps("CTOP") + " ]", "demo-pkg>=1.0\n")
                    );
                    CadFolderTool tool = ToolOf(model);

                    SnapAt(model);
                    var watch = Stopwatch.StartNew();
                    Pump(() => watch.Elapsed > TimeSpan.FromSeconds(1.5), "The wait did not end.");

                    Assert.Equal(0, asked);
                    Assert.Equal(0, tool.Starts);
                    Assert.Equal(CadSnapKind.None, SnapAt(model).Kind);
                }
                finally
                {
                    model.DisposePython();
                }
            });
        }

        [Fact]
        public void UnloadingTheTool_TakesItsSnapsAwayAndClearsAnOverrideOfOne_AndLoadingItAgainAsksAgain()
        {
            OnWindowThread(() =>
            {
                MainViewModel model = Window();

                try
                {
                    string folder = Tool("Circles", TopSnap, "\"snaps\": [ " + Snaps("CTOP") + " ]");
                    model.LoadApplication(folder);
                    CadFolderTool first = ToolOf(model);
                    model.Execute("LINE");
                    model.Execute("CTOP");
                    Assert.NotNull(model.Snap.CustomOverride);

                    Assert.True(model.UnloadPythonFile(folder));

                    Assert.Empty(model.PluginSnapList);
                    Assert.Empty(model.Snap.CustomSnaps);
                    Assert.Null(model.Snap.CustomOverride);

                    model.LoadApplication(folder);

                    CadFolderTool second = ToolOf(model);
                    Assert.NotSame(first, second);
                    Assert.Equal(0, second.Snaps.Count);
                    Assert.Single(model.PluginSnapList);
                }
                finally
                {
                    model.DisposePython();
                }
            });
        }

        [Fact]
        public void AKeywordALoadedPluginHas_IsLeftOutWithOneLine_AndTheToolsCommandsLoad()
        {
            OnWindowThread(() =>
            {
                MainViewModel model = Window();

                try
                {
                    // The sample plugin's snap is CTOP.
                    _kit.LoadSample(model);
                    string folder = Tool(
                        "Circles",
                        TopSnap
                            + "from opencad2d.cad import command, write\n@command('SHOWIT')\ndef show(): write('shown')\n",
                        "\"commands\": [ \"SHOWIT\" ], \"snaps\": [ " + Snaps("CTOP", "CBOT") + " ]"
                    );

                    Assert.Equal(AppLoadStatusKind.Loaded, model.LoadApplication(folder).Kind);

                    Assert.Single(model.History, l =>
                        l == "Circles: the snap CTOP was left out: another plugin or tool has that keyword."
                    );
                    Assert.Equal(new[] { "SHOWIT" }, model.PythonCommandsOf(folder));
                    Assert.Equal(new[] { "CTOP", "CBOT" }, model.PluginSnapList.Select(i => i.Snap.Keyword));
                    Assert.Equal(
                        new[] { PluginWindowKit.SampleName, "Circles" },
                        model.PluginSnapList.Select(i => i.PluginName)
                    );
                }
                finally
                {
                    model.DisposePython();
                }
            });
        }

        [Fact]
        public void AToolReadsAtItsNextSetting_TheValueOkOfPluginSettingsWrote()
        {
            OnWindowThread(() =>
            {
                MainViewModel model = Window();

                try
                {
                    string folder = Tool(
                        "Heights",
                        "from opencad2d.cad import command, setting, write\n"
                            + "@command('HEIGHT')\n"
                            + "def show():\n"
                            + "    write('height ' + str(setting('textHeight')))\n"
                            + "    import time\n"
                            + "    time.sleep(1)\n",
                        "\"commands\": [ \"HEIGHT\" ], \"settings\": [ { \"name\": \"textHeight\", \"label\": \"Text height\","
                            + " \"kind\": \"number\", \"default\": 2.5, \"minimum\": 0.1, \"maximum\": 1000 } ]"
                    );
                    model.LoadApplication(folder);
                    model.Execute("HEIGHT");
                    Wait(
                        model,
                        () => model.History.Contains("height 2.5") && !model.Session.IsRunning,
                        "The first HEIGHT did not write its height and end."
                    );

                    PluginSettingsViewModel settings = SettingsOf(model, "Heights");
                    settings.Rows[0].Text = "7.25";
                    Assert.True(settings.Apply());

                    model.Execute("HEIGHT");
                    Wait(model, () => model.History.Contains("height 7.25"), "The new height was not written.");
                    Assert.Contains("Heights/textHeight=7.25", model.Settings.PluginSettings);
                    Assert.Contains("Heights/textHeight=7.25", File.ReadAllText(model.SettingsPath));
                }
                finally
                {
                    model.DisposePython();
                }
            });
        }

        /// <summary>Plugin Settings of a row of Load/Unload Applications, opened as its Settings… button opens it.</summary>
        internal static PluginSettingsViewModel SettingsOf(MainViewModel model, string name)
        {
            PluginSettingsViewModel opened = null;
            var applications = new AppLoadViewModel(model);
            applications.ChooseRows(new[] { applications.Rows.Single(r => r.Name == name) });
            applications.ShowSettings = dialog =>
            {
                opened = dialog;
                return false;
            };
            applications.OpenSettings();
            applications.Dispose();
            Assert.NotNull(opened);
            return opened;
        }
    }
}
