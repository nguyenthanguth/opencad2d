using System;
using System.IO;
using System.Linq;
using System.Windows.Threading;
using GeometryHelper.Dxf;
using Xunit;
using static OpenCAD2D.Wpf.UnitTest.PythonWindowKit;

namespace OpenCAD2D.Wpf.UnitTest
{
    /// <summary>
    /// The Python tools of <c>src/python/examples/tools</c> (number_points, drawing_tools and selection_lengths here, circle_marks,
    /// the fourth, in <see cref="PythonToolCircleMarksTests"/>), run as they are in the repository: each is loaded from its folder like PYLOAD of its <c>requirements.txt</c> does and its commands typed in a
    /// window's model, a real process of the tool answering at the prompts the test types answers to. The text the guide page shows
    /// is the text run here. Each case checks what the command wrote, what it drew, and that one undo takes the whole run back.
    /// </summary>
    public sealed class PythonToolExampleTests : IClassFixture<ToolPython>, IDisposable
    {
        private readonly ToolPython _python;
        private readonly TestFolder _folder = new TestFolder();

        public PythonToolExampleTests(ToolPython python)
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
            Assert.True(model.Open(_folder.Write("room.dxf", TestFiles.Room(out _, out _))));
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
            return model;
        }

        private string Example(string name)
        {
            string folder = Path.Combine(_python.Root, "src", "python", "examples", "tools", name);
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

        /// <summary>Waits for the tool to ask, by the start of the prompt, and types the answer.</summary>
        private static void Answer(MainViewModel model, string prompt, string answer)
        {
            Wait(
                model,
                () => model.Session.Prompt != null && model.Prompt.StartsWith(prompt, StringComparison.Ordinal),
                "The prompt " + prompt + " did not come."
            );
            model.Execute(answer);
        }

        private static void End(MainViewModel model) =>
            Wait(model, () => !model.Session.IsRunning, "The command did not end.");

        private static DxfDBText[] Texts(MainViewModel model) =>
            model.Document.ModelSpace.Block.OfType<DxfDBText>().Where(t => !t.IsErased).ToArray();

        [Fact]
        public void NumberPoints_ThreePointsFromANumberTyped_AreNumberedFromIt_AndTheRunIsOneUndoStep()
        {
            OnWindowThread(() =>
            {
                MainViewModel model = Window();

                try
                {
                    Assert.Equal(AppLoadStatusKind.Loaded, model.LoadApplication(Example("number_points")).Kind);
                    int before = model.Document.EntityCount;

                    model.Execute("NUMBERPOINTS");
                    Answer(model, "First number", "5");
                    Answer(model, "Specify point", "100,200");
                    Answer(model, "Specify point", "300,200");
                    Answer(model, "Specify point", "500,200");
                    Answer(model, "Specify point", "");
                    End(model);

                    Assert.Contains("3 points numbered, 5 to 7.", model.History);
                    DxfDBText[] texts = Texts(model);
                    Assert.Equal(new[] { "5", "6", "7" }, texts.Select(t => t.TextString));
                    Assert.Equal(new[] { 100.0, 300.0, 500.0 }, texts.Select(t => t.Position.X));
                    Assert.Equal(new[] { 200.0, 200.0, 200.0 }, texts.Select(t => t.Position.Y));
                    Assert.Equal(new[] { 2.5, 2.5, 2.5 }, texts.Select(t => t.Height));
                    Assert.Equal(before + 3, model.Document.EntityCount);
                    Assert.Equal("NUMBERPOINTS", model.Document.NextUndo.Name);
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
        public void NumberPoints_EnterAtTheFirstQuestion_NumbersFromOne_AndEnterAtTheFirstPointDrawsNothing()
        {
            OnWindowThread(() =>
            {
                MainViewModel model = Window();

                try
                {
                    model.LoadApplication(Example("number_points"));
                    int before = model.Document.EntityCount;

                    model.Execute("NUMBERPOINTS");
                    Answer(model, "First number", "");
                    Answer(model, "Specify point", "10,20");
                    Answer(model, "Specify point", "30,40");
                    Answer(model, "Specify point", "");
                    End(model);

                    Assert.Contains("2 points numbered, 1 to 2.", model.History);
                    Assert.Equal(new[] { "1", "2" }, Texts(model).Select(t => t.TextString));

                    model.Execute("NUMBERPOINTS");
                    Answer(model, "First number", "");
                    Answer(model, "Specify point", "");
                    End(model);

                    Assert.Contains("No point picked.", model.History);
                    Assert.Equal(before + 2, model.Document.EntityCount);
                }
                finally
                {
                    model.DisposePython();
                }
            });
        }

        [Fact]
        public void NumberPoints_EscAtAPoint_EndsTheCommand_AndTakesTheNumbersBack()
        {
            OnWindowThread(() =>
            {
                MainViewModel model = Window();

                try
                {
                    model.LoadApplication(Example("number_points"));
                    int before = model.Document.EntityCount;

                    model.Execute("NUMBERPOINTS");
                    Answer(model, "First number", "1");
                    Answer(model, "Specify point", "10,20");
                    Wait(
                        model,
                        () => model.Prompt.StartsWith("Specify point", StringComparison.Ordinal),
                        "No second prompt."
                    );
                    model.Session.Escape();
                    End(model);

                    Assert.Equal(before, model.Document.EntityCount);
                    Assert.Empty(Texts(model));
                }
                finally
                {
                    model.DisposePython();
                }
            });
        }

        [Fact]
        public void DrawingTools_LoadingWritesItsCommands_AndRectcDrawsAClosedRectangleAboutTheCentre()
        {
            OnWindowThread(() =>
            {
                MainViewModel model = Window();

                try
                {
                    Assert.Equal(AppLoadStatusKind.Loaded, model.LoadApplication(Example("drawing_tools")).Kind);
                    Assert.Contains("drawing_tools loaded: LAYERCOUNT, RECTC.", model.History);
                    Assert.Equal("RECTC", model.Session.Registry.Shortcuts.Aliases["RC"]);
                    int before = model.Document.EntityCount;

                    model.Execute("RECTC");
                    Answer(model, "Specify centre", "0,0");
                    Answer(model, "Width", "200");
                    Answer(model, "Height", "100");
                    End(model);

                    DxfPolyline rectangle = model
                        .Document.ModelSpace.Block.OfType<DxfPolyline>()
                        .Single(p => !p.IsErased);
                    Assert.True(rectangle.Closed);
                    Assert.Equal(600.0, rectangle.Length, 6);
                    Assert.Equal(20000.0, rectangle.Area, 6);
                    Assert.Equal(before + 1, model.Document.EntityCount);
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
        public void DrawingTools_RectcOfNoWidth_IsRefusedWithItsLine_AndDrawsNothing()
        {
            OnWindowThread(() =>
            {
                MainViewModel model = Window();

                try
                {
                    model.LoadApplication(Example("drawing_tools"));
                    int before = model.Document.EntityCount;

                    model.Execute("RC");
                    Answer(model, "Specify centre", "0,0");
                    Answer(model, "Width", "0");
                    Answer(model, "Height", "100");
                    End(model);

                    Assert.Contains("The width and the height must be more than 0.", model.History);
                    Assert.Equal(before, model.Document.EntityCount);
                }
                finally
                {
                    model.DisposePython();
                }
            });
        }

        [Fact]
        public void DrawingTools_LayercountWritesTheObjectsOnEachLayer_SortedByName()
        {
            OnWindowThread(() =>
            {
                MainViewModel model = Window();

                try
                {
                    model.LoadApplication(Example("drawing_tools"));

                    model.Execute("LAYERCOUNT");
                    End(model);

                    int lines = model.History.Count(l =>
                        System.Text.RegularExpressions.Regex.IsMatch(l, @"^\w+: \d+$")
                    );
                    Assert.True(lines >= 1, "LAYERCOUNT wrote no line.");
                    Assert.Equal(
                        model.Document.ModelSpace.Block.Count(e => !e.IsErased),
                        model
                            .History.Select(l => System.Text.RegularExpressions.Regex.Match(l, @"^(\w+): (\d+)$"))
                            .Where(m => m.Success)
                            .Sum(m => int.Parse(m.Groups[2].Value))
                    );
                }
                finally
                {
                    model.DisposePython();
                }
            });
        }

        [Fact]
        public void SelectionLengths_TotalsTheLengthAndTheClosedAreaOfWhatIsSelected_AndNothingSelectedSaysSo()
        {
            OnWindowThread(() =>
            {
                MainViewModel model = Window();

                try
                {
                    model.LoadApplication(Example("selection_lengths"));

                    model.Execute("SELECTIONLENGTHS");
                    Answer(model, "Select objects", "");
                    End(model);

                    Assert.Contains("Nothing selected.", model.History);

                    int count = model.Document.ModelSpace.Block.Count(e => !e.IsErased);
                    model.Execute("SELECTIONLENGTHS");
                    Answer(model, "Select objects", "ALL");
                    Answer(model, "Select objects", "");
                    End(model);

                    Assert.Contains(model.History, l =>
                        l.StartsWith(count + " objects: length ", StringComparison.Ordinal)
                    );
                    Assert.True(model.Document.NextUndo == null || model.Document.NextUndo.Name != "SELECTIONLENGTHS");
                }
                finally
                {
                    model.DisposePython();
                }
            });
        }
    }
}
