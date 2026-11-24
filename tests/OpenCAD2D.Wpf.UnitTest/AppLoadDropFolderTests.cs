using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Xunit;

namespace OpenCAD2D.Wpf.UnitTest
{
    /// <summary>
    /// A folder dropped on the table of Load/Unload Applications (TF6b): a tool's folder loads as Load folder… loads it, a
    /// Python tool or a C# plugin, and any other folder is passed over as before. The Python tools are the fake of the Python
    /// rows tests; no window is shown.
    /// </summary>
    public sealed class AppLoadDropFolderTests : IDisposable
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

        private string Folder(string name, params string[] files)
        {
            string folder = Path.Combine(_folder.FullPath, "dropped", name);
            Directory.CreateDirectory(folder);

            foreach (string file in files)
            {
                File.WriteAllText(Path.Combine(folder, file), string.Empty);
            }

            return folder;
        }

        private AppLoadViewModel Created(MainViewModel model)
        {
            var dialog = new AppLoadViewModel(model);
            _disposables.Add(dialog);
            return dialog;
        }

        [Fact]
        public void AFolderWithARequirementsFile_DroppedOnTheTable_LoadsAsAPythonTool_AndShowsItsRow()
        {
            MainViewModel model = Opened(out FakePythonFiles python);
            string tool = Folder("Survey", "requirements.txt", "__init__.py");
            python.Defines["Survey"] = new[] { "SURVEY" };
            AppLoadViewModel dialog = Created(model);

            dialog.LoadDropped(new[] { tool });

            AppLoadRow row = Assert.Single(dialog.Rows);
            Assert.Equal("Survey", row.Name);
            Assert.Equal("Python", row.Kind);
            Assert.Equal("SURVEY", row.Commands);
            Assert.True(row.IsOn);
            Assert.Equal(new[] { tool }, python.Loads);
        }

        [Fact]
        public void AFolderWithACsproj_DroppedOnTheTable_IsLoadedAsAPlugin_NotPassedOver()
        {
            MainViewModel model = Opened(out FakePythonFiles python);
            string plugin = Folder("Stairs", "Stairs.csproj");
            AppLoadViewModel dialog = Created(model);

            // No plugin manager is in use here: the line that says so shows the folder was taken as a plugin.
            dialog.LoadDropped(new[] { plugin });

            Assert.Contains("No plugin manager is in use: applications cannot be loaded.", model.History);
            Assert.True(dialog.HasLoadError);
            Assert.Equal("Stairs", dialog.LoadErrorName);
            Assert.Empty(python.Loads);
        }

        [Fact]
        public void AnyOtherFolder_DroppedOnTheTable_IsPassedOver_WhileAFileInTheSameDropLoads()
        {
            MainViewModel model = Opened(out FakePythonFiles python);
            string empty = Folder("Empty");
            string notes = Folder("Notes", "notes.txt");
            string tool = Folder("Grid", "requirements.txt", "__init__.py");
            AppLoadViewModel dialog = Created(model);

            dialog.LoadDropped(new[] { empty, notes, Path.Combine(_folder.FullPath, "missing"), tool });

            Assert.Equal(new[] { tool }, python.Loads);
            Assert.Equal(new[] { "Grid" }, dialog.Rows.Select(r => r.Name));
            Assert.False(dialog.HasLoadError);
            Assert.DoesNotContain("No plugin manager is in use: applications cannot be loaded.", model.History);
        }
    }
}
