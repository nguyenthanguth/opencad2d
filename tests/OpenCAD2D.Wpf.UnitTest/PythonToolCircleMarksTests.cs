using System;
using System.IO;
using System.Linq;
using System.Windows.Threading;
using GeometryHelper.Dxf;
using GeometryHelper.Geometry;
using OpenCAD2D.Plugins;
using Xunit;
using static OpenCAD2D.Wpf.UnitTest.PythonWindowKit;

namespace OpenCAD2D.Wpf.UnitTest
{
    /// <summary>
    /// The fourth Python tool of <c>src/python/examples/tools</c>, circle_marks (the other three are in
    /// <see cref="PythonToolExampleTests"/>), run as it is in the repository with a real process: its snap CTOP gives the top of a
    /// circle of radius 25 about (100, 50), (100, 75); MARKTOPS writes a text of the height its setting holds, 2.5 until Plugin
    /// Settings changes it, at that point; its ribbon button and its setting are listed with no process started.
    /// </summary>
    public sealed class PythonToolCircleMarksTests : IClassFixture<ToolPython>, IDisposable
    {
        private readonly ToolPython _python;
        private readonly TestFolder _folder = new TestFolder();

        public PythonToolCircleMarksTests(ToolPython python)
        {
            _python = python;
        }

        public void Dispose() => _folder.Dispose();

        private MainViewModel Window()
        {
            var model = new MainViewModel { SettingsPath = Path.Combine(_folder.FullPath, "settings.json") };
            model.ApplySettings(
                new GlobalSettings { SecureLoad = 0, PythonPath = PythonEnvTestSupport.MachinePython.Executable }
            );
            var database = new DxfDatabase("AC1027");
            database.ModelSpace.AppendEntity(new DxfCircle(new GeoPoint3(100, 50, 0), GeoVector3.ZAxis, 25));
            Assert.True(model.Open(_folder.Write("circle.dxf", database)));
            model.UsePython(
                Dispatcher.CurrentDispatcher,
                new PythonToolSetup
                {
                    Environments = new PythonEnvironments(
                        Path.Combine(_folder.FullPath, "envs"),
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

        private string Example()
        {
            string folder = Path.Combine(_python.Root, "src", "python", "examples", "tools", "circle_marks");
            Assert.True(File.Exists(Path.Combine(folder, "__init__.py")), folder + " is not written");
            return folder;
        }

        private static void Wait(MainViewModel model, Func<bool> done, string what) =>
            Pump(
                () =>
                {
                    model.RefreshHistory();
                    return done();
                },
                what + " History: " + string.Join(" | ", model.History.Skip(Math.Max(0, model.History.Count - 10)))
            );

        private static DxfDBText[] Texts(MainViewModel model) =>
            model.Document.ModelSpace.Block.OfType<DxfDBText>().Where(t => !t.IsErased).ToArray();

        private static void Mark(MainViewModel model)
        {
            model.Execute("MARKTOPS");
            Wait(
                model,
                () =>
                    model.Session.Prompt != null && model.Prompt.StartsWith("Select objects", StringComparison.Ordinal),
                "The prompt did not come."
            );
            model.Execute("ALL");
            Wait(model, () => model.Prompt.StartsWith("Select objects", StringComparison.Ordinal), "No second prompt.");
            model.Execute(string.Empty);
            Wait(model, () => !model.Session.IsRunning, "The command did not end.");
        }

        [Fact]
        public void Loading_ListsItsCommandItsSnapAndItsSetting_WithNoProcessStarted()
        {
            OnWindowThread(() =>
            {
                MainViewModel model = Window();

                try
                {
                    Assert.Equal(AppLoadStatusKind.Loaded, model.LoadApplication(Example()).Kind);

                    Assert.Contains("circle_marks loaded: MARKTOPS.", model.History);
                    MainViewModel.PluginSnapInfo top = Assert.Single(model.PluginSnapList);
                    Assert.Equal("Circle top", top.Snap.Name);
                    Assert.Equal("circle_marks", top.PluginName);
                    Assert.Same(top.Snap, Assert.Single(model.Snap.CustomSnaps));
                    CadPluginSetting height = Assert.Single(model.PythonSettingsOf(Example()));
                    Assert.Equal("Text height", height.Label);
                    Assert.Equal(2.5, height.Value);
                    Assert.Equal(0, ((PythonToolFiles)model.PythonFiles).Tools.Single().Starts);
                }
                finally
                {
                    model.DisposePython();
                }
            });
        }

        [Fact]
        public void TheSnapGivesTheTopOfTheCircle_AfterTheToolsProcessAnswers()
        {
            OnWindowThread(() =>
            {
                MainViewModel model = Window();

                try
                {
                    model.LoadApplication(Example());
                    int answered = 0;
                    model.PluginSnapAnswered += (sender, e) => answered++;
                    var near = new GeoPoint2(100.2, 74.8);

                    CadSnapResult before = CadPicking.Snap(
                        model.Document,
                        model.Document.ModelSpace,
                        model.Snap,
                        near,
                        2.0,
                        null
                    );
                    Wait(model, () => answered > 0, "The answer did not come.");
                    CadSnapResult after = CadPicking.Snap(
                        model.Document,
                        model.Document.ModelSpace,
                        model.Snap,
                        near,
                        2.0,
                        null
                    );

                    Assert.Equal(CadSnapKind.None, before.Kind);
                    Assert.Equal(CadSnapKind.Custom, after.Kind);
                    Assert.Equal(100.0, after.Point.X, 6);
                    Assert.Equal(75.0, after.Point.Y, 6);
                }
                finally
                {
                    model.DisposePython();
                }
            });
        }

        [Fact]
        public void MarkTops_WritesATextOfTheHeightTheSettingHolds_AtTheTopOfTheCircle_AndTheNextValueOkWroteIsUsed()
        {
            OnWindowThread(() =>
            {
                MainViewModel model = Window();

                try
                {
                    model.LoadApplication(Example());

                    Mark(model);

                    DxfDBText text = Assert.Single(Texts(model));
                    Assert.Equal("R25", text.TextString);
                    Assert.Equal(2.5, text.Height, 9);
                    Assert.Equal(100.0, text.Position.X, 9);
                    Assert.Equal(75.0, text.Position.Y, 9);

                    PluginSettingsViewModel dialog = PythonToolSnapProcessTests.SettingsOf(model, "circle_marks");
                    dialog.Rows[0].Text = "4";
                    Assert.True(dialog.Apply());
                    Mark(model);

                    Assert.Equal(new[] { 2.5, 4.0 }, Texts(model).Select(t => t.Height));
                }
                finally
                {
                    model.DisposePython();
                }
            });
        }
    }
}
