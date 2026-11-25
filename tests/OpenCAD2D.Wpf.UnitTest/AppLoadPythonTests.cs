using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Windows.Threading;
using OpenCAD2D.Plugins;
using Xunit;

namespace OpenCAD2D.Wpf.UnitTest
{
    /// <summary>
    /// The Python tools of a window that has a fake in place of the tools (D1624): what each tool declares, which it fails
    /// to load with, and what it leaves loaded are the fake's to say, so no process starts (the real tools have their own
    /// suite). A tool is named, by the name of its folder, by what it declares in <see cref="Defines"/>, by what it fails
    /// with in <see cref="Fails"/>.
    /// </summary>
    internal sealed class FakePythonFiles : IPythonFiles
    {
        private readonly List<string> _loaded = new List<string>();
        private readonly Dictionary<string, string[]> _commands = new Dictionary<string, string[]>(
            StringComparer.OrdinalIgnoreCase
        );

        /// <summary>Gets what a file of a name defines when it loads, in the order it defines it.</summary>
        public Dictionary<string, string[]> Defines { get; } =
            new Dictionary<string, string[]>(StringComparer.OrdinalIgnoreCase);

        /// <summary>Gets the line a file of a name writes to the command line when it fails to load; such a file is not loaded.</summary>
        public Dictionary<string, string> Fails { get; } =
            new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);

        /// <summary>Gets the paths Load was called with, in order, as given.</summary>
        public List<string> Loads { get; } = new List<string>();

        /// <summary>Gets the paths Unload was called with, in order, as given.</summary>
        public List<string> Unloads { get; } = new List<string>();

        public IReadOnlyList<string> Loaded => _loaded.ToArray();

        /// <summary>The fake loads no engine, so its files add nothing to the window.</summary>
        public IReadOnlyList<CadPluginHost> PluginHosts => Array.Empty<CadPluginHost>();

        public event EventHandler PluginHostsChanged
        {
            add { }
            remove { }
        }

        public bool Load(CadCommandSession session, string path)
        {
            Loads.Add(path);
            string full = Path.GetFullPath(path);
            string name = Path.GetFileName(full);

            if (Fails.TryGetValue(name, out string failure))
            {
                _commands.Remove(full);
                _loaded.RemoveAll(f => string.Equals(f, full, StringComparison.OrdinalIgnoreCase));
                session.Write(failure);
                return false;
            }

            string[] defined = Defines.TryGetValue(name, out string[] found) ? found : Array.Empty<string>();

            if (!_loaded.Contains(full, StringComparer.OrdinalIgnoreCase))
            {
                _loaded.Add(full);
            }

            _commands[full] = defined;

            // The line the host writes itself, which the command line keeps.
            session.Write(name + " loaded" + (defined.Length == 0 ? "." : ": " + string.Join(", ", defined) + "."));
            return true;
        }

        public IReadOnlyList<string> CommandsOf(string path)
        {
            string full = Path.GetFullPath(path);
            return
                _loaded.Contains(full, StringComparer.OrdinalIgnoreCase) && _commands.TryGetValue(full, out string[] c)
                ? c
                : Array.Empty<string>();
        }

        public bool Unload(string path)
        {
            Unloads.Add(path);
            string full = Path.GetFullPath(path);
            _commands.Remove(full);
            return _loaded.RemoveAll(f => string.Equals(f, full, StringComparison.OrdinalIgnoreCase)) > 0;
        }
    }

    /// <summary>
    /// Python rows in Load/Unload Applications (D1618 to D1621, D1624), with <see cref="FakePythonFiles"/> in place of the
    /// tools: the file filters, the row a loaded tool folder gets (Kind, commands, On, At start), the footer lines, a lone .py
    /// refused, Unload, Reload and the On box, the Python Startup Suite, the <c>Python N</c> chip and its filter, the rows
    /// staying as the shown drawing changes, and PYLOAD's picker. No test depends on a row that is gone leaving the table.
    /// </summary>
    public sealed class AppLoadPythonTests : IDisposable
    {
        private readonly TestFolder _folder = new TestFolder();
        private readonly List<IDisposable> _disposables = new List<IDisposable>();

        public void Dispose()
        {
            foreach (IDisposable item in _disposables)
            {
                item.Dispose();
            }

            _folder.Dispose();
        }

        private string SettingsFile => Path.Combine(_folder.FullPath, "settings.json");

        /// <summary>A Python tool in a folder of its own: its folder, with a requirements.txt and an __init__.py.</summary>
        private string Py(string name) => ToolIn(Path.Combine(_folder.FullPath, "py"), name);

        private static string ToolIn(string parent, string name)
        {
            string folder = Path.Combine(parent, name);
            Directory.CreateDirectory(folder);
            File.WriteAllText(Path.Combine(folder, "requirements.txt"), string.Empty);
            File.WriteAllText(Path.Combine(folder, "__init__.py"), "pass\n");
            return folder;
        }

        /// <summary>A lone .py file, which is no tool.</summary>
        private string LonePy(string name)
        {
            Directory.CreateDirectory(Path.Combine(_folder.FullPath, "lone"));
            string path = Path.Combine(_folder.FullPath, "lone", name);
            File.WriteAllText(path, "pass\n");
            return path;
        }

        private string Lisp(string name, string text)
        {
            Directory.CreateDirectory(Path.Combine(_folder.FullPath, "lisp"));
            string path = Path.Combine(_folder.FullPath, "lisp", name);
            File.WriteAllText(path, text);
            return path;
        }

        /// <summary>A window's model on a settings file of its own, a drawing open, and the fake as its Python files.</summary>
        private MainViewModel Opened(out FakePythonFiles python)
        {
            var model = new MainViewModel { SettingsPath = SettingsFile };
            model.LoadSecurity.SecureLoad = 0;
            GlobalSettings changed = model.Settings.Clone();
            changed.SecureLoad = 0;
            model.ApplySettings(changed);
            Assert.True(
                model.Open(_folder.Write("room-" + Guid.NewGuid().ToString("N") + ".dxf", TestFiles.Room(out _, out _)))
            );
            python = new FakePythonFiles();
            model.PythonFiles = python;
            return model;
        }

        private AppLoadViewModel Created(MainViewModel model)
        {
            var dialog = new AppLoadViewModel(model);
            _disposables.Add(dialog);
            return dialog;
        }

        private static AppLoadRow Row(AppLoadViewModel dialog, string name) =>
            Assert.Single(dialog.Rows, r => r.Name == name);

        // ---- The filters

        [Fact]
        public void TheOpenDialogOffersApplicationsFirst_ThenLispPythonToolsPluginsAndAllFiles()
        {
            Assert.Equal(
                "Applications (*.lsp;requirements.txt;*.dll;*.csproj)|*.lsp;requirements.txt;*.dll;*.csproj|Lisp files (*.lsp)|*.lsp|Python tools (requirements.txt)|requirements.txt|Plugins (*.dll)|*.dll|C# plugin (*.csproj)|*.csproj|All files (*.*)|*.*",
                AppLoadViewModel.OpenFilter
            );
        }

        [Fact]
        public void ThePythonFilterOfPyLoad_IsTheRequirementsFileOfAToolAlone()
        {
            Assert.Equal("Python tools (requirements.txt)|requirements.txt", AppLoadViewModel.PythonFilter);
        }

        // ---- A loaded file

        [Fact]
        public void LoadingAToolFolder_ShowsARowOfKindPython_WithItsCommandsInOrder_Ticked_AndNotAtStart()
        {
            MainViewModel model = Opened(out FakePythonFiles python);
            string path = Py("tools");
            python.Defines["tools"] = new[] { "ZETA", "ALPHA" };
            AppLoadViewModel dialog = Created(model);

            dialog.Load(new[] { path });

            AppLoadRow row = Assert.Single(dialog.Rows);
            Assert.Equal("tools", row.Name);
            Assert.Equal("Python", row.Kind);
            Assert.False(row.IsPlugin);
            Assert.Equal("ZETA, ALPHA", row.Commands);
            Assert.Equal(path, row.File);
            Assert.True(row.IsOn);
            Assert.False(row.AtStart);
            Assert.True(row.CanChangeAtStart);
            Assert.Equal(new[] { path }, python.Loads);
            Assert.Equal(Path.GetDirectoryName(path), model.Settings.AppLoadFolder);
        }

        [Fact]
        public void AToolThatDeclaresNoCommand_HasAnEmptyCommandsCell()
        {
            MainViewModel model = Opened(out _);
            AppLoadViewModel dialog = Created(model);

            dialog.Load(new[] { Py("plain") });

            Assert.Equal(string.Empty, Assert.Single(dialog.Rows).Commands);
        }

        [Fact]
        public void TheRequirementsFileOfAToolGiven_LoadsTheToolOfItsFolder_AndTheRowIsTheFolder()
        {
            MainViewModel model = Opened(out FakePythonFiles python);
            string folder = Py("viaFile");
            python.Defines["viaFile"] = new[] { "VIA" };
            AppLoadViewModel dialog = Created(model);

            dialog.Load(new[] { Path.Combine(folder, "requirements.txt") });

            AppLoadRow row = Assert.Single(dialog.Rows);
            Assert.Equal("viaFile", row.Name);
            Assert.Equal("Python", row.Kind);
            Assert.Equal(folder, row.File);
            Assert.Equal(new[] { folder }, python.Loads);
        }

        [Fact]
        public void ALonePyFile_IsRefusedWithTheLineThatSaysHowToMakeItATool_AndLoadsNothing()
        {
            MainViewModel model = Opened(out FakePythonFiles python);
            string path = LonePy("DrawingInfo.py");
            AppLoadViewModel dialog = Created(model);

            AppLoadResult result = model.LoadApplication(path);
            dialog.Load(new[] { path });

            const string line =
                "DrawingInfo.py is a lone Python file: a Python tool is a folder with requirements.txt. Put it in Pythons\\DrawingInfo\\ as __init__.py (see the Python guide).";
            Assert.Equal(AppLoadStatusKind.Refused, result.Kind);
            Assert.Equal(line, result.Message);
            Assert.Contains(line, model.History);
            Assert.True(dialog.HasLoadError);
            Assert.Empty(python.Loads);
        }

        [Fact]
        public void AFolderWithNoRequirementsFile_IsNoTool_AndIsNotLoadedAsOne()
        {
            MainViewModel model = Opened(out FakePythonFiles python);
            string folder = Path.Combine(_folder.FullPath, "package");
            Directory.CreateDirectory(folder);
            File.WriteAllText(Path.Combine(folder, "__init__.py"), "pass\n");

            AppLoadResult result = model.LoadApplication(folder);

            // Any other folder goes to the plugin manager, which the model here has none of.
            Assert.Equal(AppLoadStatusKind.Failed, result.Kind);
            Assert.Empty(python.Loads);
        }

        [Fact]
        public void TheFooterOfAToolThatLoaded_IsSuccessfullyLoaded_AndTheHostsOwnLineStaysOnTheCommandLine()
        {
            MainViewModel model = Opened(out FakePythonFiles python);
            python.Defines["tools"] = new[] { "C1", "DOT" };

            AppLoadResult result = model.LoadApplication(Py("tools"));

            Assert.Equal(AppLoadStatusKind.Loaded, result.Kind);
            Assert.Equal("tools successfully loaded.", result.Message);
            Assert.Contains("tools loaded: C1, DOT.", model.History);
        }

        [Fact]
        public void TheFooterOfAToolThatFailed_IsFailedToLoadAndTheSummary_AndTheLoadErrorLineShowsIt()
        {
            MainViewModel model = Opened(out FakePythonFiles python);
            python.Fails["bad"] = "Python error: RuntimeError: boom (bad, line 2)";
            string path = Py("bad");
            AppLoadViewModel dialog = Created(model);

            AppLoadResult result = model.LoadApplication(path);
            dialog.Load(new[] { path });

            Assert.Equal(AppLoadStatusKind.Failed, result.Kind);
            Assert.StartsWith("bad failed to load: ", result.Message, StringComparison.Ordinal);
            Assert.Contains("RuntimeError: boom", result.Message, StringComparison.Ordinal);
            Assert.True(dialog.HasLoadError);
            Assert.Equal("bad", dialog.LoadErrorName);
            Assert.StartsWith(" failed to load: ", dialog.LoadErrorText, StringComparison.Ordinal);
            Assert.Contains("RuntimeError: boom", dialog.LoadErrorText, StringComparison.Ordinal);
        }

        [Fact]
        public void AToolThatFailed_KeepsNoCommand_AndLeavesNoLoadedRow()
        {
            MainViewModel model = Opened(out FakePythonFiles python);
            python.Fails["bad"] = "Python error: RuntimeError: boom (bad, line 2)";
            AppLoadViewModel dialog = Created(model);

            dialog.Load(new[] { Py("bad") });

            Assert.DoesNotContain(dialog.Rows, r => r.IsOn);
            Assert.Empty(python.Loaded);
        }

        [Fact]
        public void WithPythonNotInUse_AToolFails_WithAReasonAfterFailedToLoad()
        {
            // The window has no Python tools (the host did not enable Python): the tool is not loaded and the footer says
            // it failed, with a reason.
            MainViewModel model = Opened(out _);
            model.PythonFiles = null;

            AppLoadResult result = model.LoadApplication(Py("tools"));

            Assert.Equal(AppLoadStatusKind.Failed, result.Kind);
            Assert.StartsWith("tools failed to load: ", result.Message, StringComparison.Ordinal);
            Assert.True(result.Message.Length > "tools failed to load: ".Length);
        }

        [Fact]
        public void LoadingAToolWithNoDrawingOpen_SaysToOpenADrawing_AndLoadsNothing()
        {
            var model = new MainViewModel { SettingsPath = SettingsFile };
            var python = new FakePythonFiles();
            model.PythonFiles = python;
            AppLoadViewModel dialog = Created(model);

            dialog.Load(new[] { Py("tools") });

            Assert.Empty(python.Loads);
            Assert.Empty(dialog.Rows);
            Assert.Contains("Open a drawing to load tools.", model.History);
            Assert.True(dialog.HasLoadError);
            Assert.Equal("Open a drawing to load tools.", dialog.LoadError);
        }

        [Fact]
        public void ATextFile_IsRefusedWithTheWordingThatNamesPythonToo()
        {
            MainViewModel model = Opened(out FakePythonFiles python);
            string notes = _folder.WriteText("notes.txt", "hello");

            AppLoadResult result = model.LoadApplication(notes);

            Assert.Equal(AppLoadStatusKind.Refused, result.Kind);
            Assert.Equal("notes.txt is not an AutoLISP file, a Python file or a plugin.", result.Message);
            Assert.Equal(" is not an AutoLISP file, a Python file or a plugin.", result.Detail);
            Assert.Equal(result.Message, model.History.Last());
            Assert.Empty(python.Loads);
        }

        // ---- Unload, the On box, Reload

        [Fact]
        public void UnloadOfAToolRow_TakesItsCommandsOut_SaysSo_AndTheRowStaysUnticked_NamingThemStill()
        {
            MainViewModel model = Opened(out FakePythonFiles python);
            string path = Py("tools");
            python.Defines["tools"] = new[] { "C1", "DOT" };
            AppLoadViewModel dialog = Created(model);
            dialog.Load(new[] { path });
            dialog.ChooseRows(dialog.Rows);

            dialog.UnloadCommand.Execute(null);

            AppLoadRow row = Row(dialog, "tools");
            Assert.False(row.IsOn);
            Assert.Equal("C1, DOT", row.Commands);
            Assert.Equal(new[] { path }, python.Unloads);
            Assert.Empty(python.Loaded);
            Assert.Equal("tools unloaded.", model.History.Last());
        }

        [Fact]
        public void UntickingOn_IsUnload_AndTickingItAgainLoadsTheFileAgain_WithItsCommandsBack()
        {
            MainViewModel model = Opened(out FakePythonFiles python);
            string path = Py("tools");
            python.Defines["tools"] = new[] { "C1", "DOT" };
            AppLoadViewModel dialog = Created(model);
            dialog.Load(new[] { path });

            Row(dialog, "tools").IsOn = false;

            AppLoadRow off = Row(dialog, "tools");
            Assert.False(off.IsOn);
            Assert.Equal("C1, DOT", off.Commands);
            Assert.Equal("tools unloaded.", model.History.Last());

            off.IsOn = true;

            AppLoadRow on = Row(dialog, "tools");
            Assert.True(on.IsOn);
            Assert.Equal("C1, DOT", on.Commands);
            Assert.Equal(new[] { path, path }, python.Loads);
        }

        [Fact]
        public void ReloadLoadsTheChosenPyRowAgain_AndTheCommandsFollowTheFile()
        {
            MainViewModel model = Opened(out FakePythonFiles python);
            string path = Py("edit");
            python.Defines["edit"] = new[] { "ONE" };
            AppLoadViewModel dialog = Created(model);
            dialog.Load(new[] { path });
            python.Defines["edit"] = new[] { "ONE", "TWO" };
            dialog.ChooseRows(dialog.Rows);

            dialog.ReloadCommand.Execute(null);

            Assert.Equal("ONE, TWO", Assert.Single(dialog.Rows).Commands);
            Assert.Equal(new[] { path, path }, python.Loads);
        }

        // ---- At start

        [Fact]
        public void AToolInThePythonsFolder_ShowsATickedBoxThatCannotBeChanged_AndTheSuiteIsLeftAlone()
        {
            MainViewModel model = Opened(out _);
            string program = Path.Combine(_folder.FullPath, "program");
            string pythons = Directory.CreateDirectory(Path.Combine(program, "Pythons")).FullName;
            model.Document.SupportRoots = new CadSupportFolders(program);
            string inside = ToolIn(pythons, "gh-tools");
            string outside = Py("mine");
            AppLoadViewModel dialog = Created(model);
            dialog.Load(new[] { inside, outside });

            AppLoadRow fixedRow = Row(dialog, "gh-tools");
            Assert.True(fixedRow.AtStart);
            Assert.True(fixedRow.IsAtStartFixed);
            Assert.False(fixedRow.CanChangeAtStart);
            Assert.True(Row(dialog, "mine").CanChangeAtStart);

            fixedRow.AtStart = false;

            Assert.True(Row(dialog, "gh-tools").AtStart);
            Assert.Empty(model.Settings.PythonStartupSuite);
        }

        [Fact]
        public void AnUnloadedToolOfThePythonsFolder_AndOneFromElsewhere_StayInANewWindow_Unticked()
        {
            MainViewModel model = Opened(out FakePythonFiles python);
            string program = Path.Combine(_folder.FullPath, "program");
            string pythons = Directory.CreateDirectory(Path.Combine(program, "Pythons")).FullName;
            model.Document.SupportRoots = new CadSupportFolders(program);
            string inside = ToolIn(pythons, "gh-tools");
            string outside = Py("mine");
            AppLoadViewModel dialog = Created(model);
            dialog.Load(new[] { inside, outside });
            dialog.ChooseRows(dialog.Rows);

            dialog.Unload();

            Assert.All(dialog.Rows, r => Assert.False(r.IsOn));

            AppLoadViewModel again = Created(model);
            Assert.Equal(2, again.Rows.Count);
            Assert.False(Row(again, "gh-tools").IsOn);
            Assert.False(Row(again, "mine").IsOn);

            // A new run of the app over the settings file.
            var second = new MainViewModel { SettingsPath = SettingsFile };
            second.ApplySettings(GlobalSettings.Load(SettingsFile));
            Assert.True(
                second.Open(
                    _folder.Write("room-" + Guid.NewGuid().ToString("N") + ".dxf", TestFiles.Room(out _, out _))
                )
            );
            second.PythonFiles = new FakePythonFiles();
            second.Document.SupportRoots = new CadSupportFolders(program);
            AppLoadViewModel restarted = Created(second);
            Assert.Equal(2, restarted.Rows.Count);
            Assert.False(Row(restarted, "gh-tools").IsOn);
            Assert.False(Row(restarted, "mine").IsOn);

            Row(restarted, "gh-tools").IsOn = true;
            Assert.True(Row(restarted, "gh-tools").IsOn);
        }

        [Fact]
        public void TickingAtStart_WritesThePythonStartupSuite_AndUntickingTakesItOut_WhileTheLispSuiteIsNotTouched()
        {
            MainViewModel model = Opened(out _);
            string path = Py("start");
            AppLoadViewModel dialog = Created(model);
            dialog.Load(new[] { path });

            Row(dialog, "start").AtStart = true;

            Assert.Equal(new[] { path }, model.Settings.PythonStartupSuite);
            Assert.Equal(new[] { path }, GlobalSettings.Load(SettingsFile).PythonStartupSuite);
            Assert.Empty(model.Settings.LispStartupSuite);
            Assert.True(Row(dialog, "start").AtStart);

            // A window opened afterwards reads the suite back.
            Assert.True(Row(Created(model), "start").AtStart);

            Row(dialog, "start").AtStart = false;

            Assert.Empty(model.Settings.PythonStartupSuite);
            Assert.Empty(GlobalSettings.Load(SettingsFile).PythonStartupSuite);
            Assert.False(Row(dialog, "start").AtStart);
        }

        // ---- The chips

        [Fact]
        public void ThePythonChip_CountsThePyRows_AndTheLispAndPluginChipsAreUnchanged()
        {
            MainViewModel model = Opened(out _);
            AppLoadViewModel dialog = Created(model);

            dialog.Load(new[] { Lisp("b.lsp", "(setq b 1)"), Py("c"), Lisp("a.lsp", "(setq a 1)"), Py("d") });

            Assert.Equal("All 4", dialog.AllChipText);
            Assert.Equal("Lisp 2", dialog.LispChipText);
            Assert.Equal("Python 2", dialog.PythonChipText);
            Assert.Equal("Plugins 0", dialog.PluginChipText);
        }

        [Fact]
        public void TheFilters_ShowTheirOwnKindAlone_AndAllShowsTheRowsSortedByName()
        {
            MainViewModel model = Opened(out _);
            AppLoadViewModel dialog = Created(model);
            dialog.Load(new[] { Lisp("b.lsp", "(setq b 1)"), Py("c"), Lisp("a.lsp", "(setq a 1)"), Py("d") });

            Assert.Equal(new[] { "a.lsp", "b.lsp", "c", "d" }, dialog.Rows.Select(r => r.Name));

            dialog.FilterCommand.Execute("Python");
            Assert.Equal("Python", dialog.Filter);
            Assert.Equal(new[] { "c", "d" }, dialog.Rows.Select(r => r.Name));

            dialog.FilterCommand.Execute("Lisp");
            Assert.Equal(new[] { "a.lsp", "b.lsp" }, dialog.Rows.Select(r => r.Name));

            dialog.FilterCommand.Execute("Plugin");
            Assert.Empty(dialog.Rows);

            dialog.FilterCommand.Execute("All");
            Assert.Equal(4, dialog.Rows.Count);
        }

        // ---- Dropped on the table

        [Fact]
        public void TheRequirementsFileOfAToolDroppedOnTheTable_LoadsAsLoadDoes_AndShowsItsRow()
        {
            MainViewModel model = Opened(out FakePythonFiles python);
            string path = Py("dropped");
            python.Defines["dropped"] = new[] { "DROPPED" };
            AppLoadViewModel dialog = Created(model);

            dialog.LoadDropped(new[] { Path.Combine(path, "requirements.txt") });

            AppLoadRow row = Assert.Single(dialog.Rows);
            Assert.Equal("Python", row.Kind);
            Assert.Equal("DROPPED", row.Commands);
            Assert.Equal(new[] { path }, python.Loads);
        }

        // ---- Not tied to the drawing shown

        [Fact]
        public void AToolRow_IsTheSameInAWindowOpenedWhenAnotherDrawingIsShown()
        {
            // Python commands are process-wide (D1618): the file loaded while drawing A was shown is listed, on, with its
            // commands, by a window made while drawing B is shown.
            MainViewModel model = Opened(out FakePythonFiles python);
            string path = Py("tools");
            python.Defines["tools"] = new[] { "C1", "DOT" };
            Created(model).Load(new[] { path });
            Assert.True(
                model.Open(
                    _folder.Write("second-" + Guid.NewGuid().ToString("N") + ".dxf", TestFiles.Room(out _, out _))
                )
            );

            AppLoadRow row = Row(Created(model), "tools");

            Assert.True(row.IsOn);
            Assert.Equal("C1, DOT", row.Commands);
            Assert.Equal(path, row.File);
        }

        // ---- PYLOAD in the window

        [Fact]
        public void PyLoadInTheWindow_AsksForToolsOnTheRememberedFolder_AndLoadsTheOnesChosenInOrder()
        {
            var model = new MainViewModel { SettingsPath = SettingsFile };
            model.ApplySettings(
                new GlobalSettings { PythonPath = Path.Combine(_folder.FullPath, "nowhere", "python.exe") }
            );
            Assert.True(model.Open(_folder.Write("room.dxf", TestFiles.Room(out _, out _))));
            model.UsePython(Dispatcher.CurrentDispatcher);
            var python = new FakePythonFiles();
            model.PythonFiles = python;
            string first = Py("one");
            string second = Py("two");
            var asked = new List<string>();
            model.AskPythonFiles = (filter, folder) =>
            {
                asked.Add(filter + "@" + folder);
                return new[] { second, first };
            };

            model.StartCommand("PYLOAD");
            model.StartCommand("PYLOAD");

            string remembered = Path.GetDirectoryName(first);
            Assert.Equal(
                new[]
                {
                    "Python tools (requirements.txt)|requirements.txt@",
                    "Python tools (requirements.txt)|requirements.txt@" + remembered,
                },
                asked
            );
            Assert.Equal(new[] { second, first, second, first }, python.Loads);
            Assert.Equal(remembered, GlobalSettings.Load(SettingsFile).AppLoadFolder);
            Assert.False(model.IsCommandRunning);
        }

        [Fact]
        public void PyLoadInTheWindow_Cancelled_LoadsNothing()
        {
            var model = new MainViewModel { SettingsPath = SettingsFile };
            model.ApplySettings(
                new GlobalSettings { PythonPath = Path.Combine(_folder.FullPath, "nowhere", "python.exe") }
            );
            Assert.True(model.Open(_folder.Write("room.dxf", TestFiles.Room(out _, out _))));
            model.UsePython(Dispatcher.CurrentDispatcher);
            var python = new FakePythonFiles();
            model.PythonFiles = python;
            model.AskPythonFiles = (filter, folder) => null;

            model.StartCommand("PYLOAD");

            Assert.Empty(python.Loads);
            Assert.False(model.IsCommandRunning);
        }
    }
}
