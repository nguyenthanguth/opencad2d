using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Xunit;

namespace OpenCAD2D.Wpf.UnitTest
{
    public sealed class PythonFolderPreferenceTests : IDisposable
    {
        private readonly TestFolder _folder = new TestFolder();

        public void Dispose() => _folder.Dispose();

        [Fact]
        public void TheFolderPreferencePage_HasAPythonsFolderRow_WithOpenFolder_AndNotSaved()
        {
            string program = Path.Combine(_folder.FullPath, "dll");
            GlobalSettingsViewModel dialog = new MainViewModel().CreateGlobalSettings("Folder preference");
            var opened = new List<string>();
            dialog.OpenFolder = opened.Add;
            dialog.SupportRoots = new CadSupportFolders(program);

            DefaultFolderRow absent = Assert.Single(dialog.DefaultPythonFolders);
            Assert.Equal("Pythons", absent.Kind);
            Assert.Equal(Path.Combine(program, "Pythons"), absent.Folder);
            Assert.Equal("(default) not found", absent.Mark);
            Assert.False(dialog.OpenDefaultFolderCommand.CanExecute(absent));

            Directory.CreateDirectory(Path.Combine(program, "Pythons"));
            dialog.SupportRoots = new CadSupportFolders(program);
            DefaultFolderRow row = Assert.Single(dialog.DefaultPythonFolders);
            Assert.Equal("(default)", row.Mark);
            dialog.OpenDefaultFolderCommand.Execute(row);
            Assert.Equal(new[] { Path.Combine(program, "Pythons") }, opened);
            Assert.DoesNotContain(dialog.DefaultSupportFolders, r => r.Kind == "Pythons");
            Assert.Empty(dialog.SupportFolders);
        }

        [Fact]
        public void TheFolderPreferencePage_ListsThePythonStartupSuite_InItsOrder_OrSaysItIsEmpty()
        {
            var model = new MainViewModel();
            GlobalSettingsViewModel empty = model.CreateGlobalSettings("Folder preference");
            Assert.StartsWith("None.", empty.PythonStartupSuiteText, StringComparison.Ordinal);
            Assert.Contains("Load/Unload Applications", empty.PythonStartupSuiteText);

            string b = Path.Combine(_folder.FullPath, "Beta", "requirements.txt");
            string a = Path.Combine(_folder.FullPath, "Alpha", "requirements.txt");
            model.Settings.PythonStartupSuite = new[] { b, a };
            GlobalSettingsViewModel dialog = model.CreateGlobalSettings("Folder preference");

            Assert.Equal(b + "\n" + a, dialog.PythonStartupSuiteText);
        }
    }
}
