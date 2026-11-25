using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Xunit;

namespace OpenCAD2D.Wpf.UnitTest
{
    /// <summary>
    /// A Python tool dropped on the window (D1622), its folder with a requirements.txt, with <see cref="FakePythonFiles"/> in
    /// place of the tools: it loads as APPLOAD's Load does, through the Python tools of the window, and with no typed echo; a
    /// lone .py is refused with its line; it is passed over without a word while a command waits and with no drawing open, as
    /// a .lsp is. Driven at the view-model level, with no real drag, as
    /// <see cref="FileDropTests"/> is.
    /// </summary>
    public sealed class FileDropPythonTests : IDisposable
    {
        private readonly TestFolder _folder = new TestFolder();

        public void Dispose() => _folder.Dispose();

        /// <summary>A window's model on a settings file of its own with a drawing open and the fake as its Python files.</summary>
        private MainViewModel Opened(out FakePythonFiles python)
        {
            var model = new MainViewModel { SettingsPath = Path.Combine(_folder.FullPath, "settings.json") };
            GlobalSettings changed = model.Settings.Clone();
            changed.SecureLoad = 0;
            model.ApplySettings(changed);
            Assert.True(model.Open(_folder.Write("room.dxf", TestFiles.Room(out _, out _))));
            python = new FakePythonFiles();
            model.PythonFiles = python;
            return model;
        }

        /// <summary>A Python tool in a folder of its own: the folder, with a requirements.txt and an __init__.py.</summary>
        private string Py(string name)
        {
            string folder = Path.Combine(_folder.FullPath, name);
            Directory.CreateDirectory(folder);
            File.WriteAllText(Path.Combine(folder, "requirements.txt"), string.Empty);
            File.WriteAllText(Path.Combine(folder, "__init__.py"), "pass\n");
            return folder;
        }

        private static string[] Since(MainViewModel model, int count) => model.History.Skip(count).ToArray();

        [Fact]
        public void AToolFolderDropped_LoadsThroughThePythonTools_AndTheCommandLineGetsTheToolsLineAlone()
        {
            MainViewModel model = Opened(out FakePythonFiles python);
            string tools = Py("tools");
            python.Defines["tools"] = new[] { "PYDROP" };
            int before = model.History.Count;

            model.DropFiles(new[] { tools });

            Assert.Equal(new[] { tools }, python.Loads);
            Assert.Equal(new[] { "tools loaded: PYDROP." }, Since(model, before));
            Assert.DoesNotContain(model.History, l => l.StartsWith("Command:", StringComparison.Ordinal));
        }

        [Fact]
        public void AToolFolderDropped_RaisesNoDragAndDropBox_AndIsAmongTheLoadedTools()
        {
            MainViewModel model = Opened(out FakePythonFiles python);
            string tools = Py("tools");
            python.Defines["tools"] = new[] { "PYDROP" };
            var boxes = new List<string>();
            model.ShowDropMessage = (title, text) => boxes.Add(text);

            model.DropFiles(new[] { tools });

            Assert.Empty(boxes);
            Assert.Equal(new[] { tools }, python.Loaded);

            using (var dialog = new AppLoadViewModel(model))
            {
                Assert.Equal("PYDROP", Assert.Single(dialog.Rows).Commands);
            }
        }

        [Fact]
        public void TheRequirementsFileOfAToolDropped_LoadsTheToolOfItsFolder()
        {
            MainViewModel model = Opened(out FakePythonFiles python);
            string tools = Py("tools");

            model.DropFiles(new[] { Path.Combine(tools, "requirements.txt") });

            Assert.Equal(new[] { tools }, python.Loads);
        }

        [Fact]
        public void ALonePyDropped_IsRefusedWithTheLineThatSaysHowToMakeItATool_AndRaisesNoDragAndDropBox()
        {
            MainViewModel model = Opened(out FakePythonFiles python);
            string lone = _folder.WriteText("DrawingInfo.py", "pass\n");
            var boxes = new List<string>();
            model.ShowDropMessage = (title, text) => boxes.Add(text);
            int before = model.History.Count;

            model.DropFiles(new[] { lone });

            Assert.Empty(python.Loads);
            Assert.Empty(boxes);
            Assert.Equal(
                new[]
                {
                    "DrawingInfo.py is a lone Python file: a Python tool is a folder with requirements.txt. Put it in Pythons\\DrawingInfo\\ as __init__.py (see the Python guide).",
                },
                Since(model, before)
            );
        }

        [Fact]
        public void TwoToolFoldersDropped_LoadInTheOrderOfTheDrop()
        {
            MainViewModel model = Opened(out FakePythonFiles python);
            string b = Py("b");
            string a = Py("a");

            model.DropFiles(new[] { b, a });

            Assert.Equal(new[] { b, a }, python.Loads);
        }

        [Fact]
        public void ALspAndAToolDroppedTogether_EachLoadTheirOwnWay()
        {
            string lisp = _folder.WriteText("tools.lsp", "(defun c:dropped () (princ))\n");
            MainViewModel model = Opened(out FakePythonFiles python);
            string tools = Py("tools");

            model.DropFiles(new[] { lisp, tools });

            Assert.Equal(new[] { lisp }, model.LoadedLispFiles);
            Assert.Equal(new[] { tools }, python.Loads);
        }

        [Fact]
        public void AToolDroppedWhileACommandWaits_IsPassedOverWithoutAWord_AndTheCommandStillWaits()
        {
            MainViewModel model = Opened(out FakePythonFiles python);
            string tools = Py("tools");
            var boxes = new List<string>();
            model.ShowDropMessage = (title, text) => boxes.Add(text);
            model.StartCommand("LINE");
            model.Execute("0,0");
            Assert.True(model.Session.IsRunning);
            string prompt = model.Session.Prompt.Text;
            int before = model.History.Count;

            model.DropFiles(new[] { tools });

            Assert.True(model.Session.IsRunning);
            Assert.Equal(prompt, model.Session.Prompt.Text);
            Assert.Empty(python.Loads);
            Assert.Empty(boxes);
            Assert.Empty(Since(model, before));
        }

        [Fact]
        public void AToolDroppedWithNoDrawingOpen_IsPassedOverWithoutAWord()
        {
            var model = new MainViewModel { SettingsPath = Path.Combine(_folder.FullPath, "settings.json") };
            var python = new FakePythonFiles();
            model.PythonFiles = python;
            var boxes = new List<string>();
            model.ShowDropMessage = (title, text) => boxes.Add(text);
            int before = model.History.Count;

            model.DropFiles(new[] { Py("tools") });

            Assert.False(model.HasDocument);
            Assert.Empty(python.Loads);
            Assert.Empty(boxes);
            Assert.Empty(Since(model, before));
        }
    }
}
