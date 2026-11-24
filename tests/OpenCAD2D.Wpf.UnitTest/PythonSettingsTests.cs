using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Xunit;

namespace OpenCAD2D.Wpf.UnitTest
{
    /// <summary>
    /// The Python section of Global settings, in Folder preference (D720, D721, <c>design/PySettingsPython.png</c>): its path
    /// box is <c>GlobalSettings.PythonPath</c>, and what it found, where it looked and what it passed over are what
    /// <see cref="PythonInstallation.Find(string)"/> gives for the text in the box, read again as the text changes. A good Python
    /// is the one of the machine (the tests of the tools' venvs use it too); a bad one is a <c>python.exe</c> made in a temporary
    /// folder that is no program at all. No process of a tool starts.
    /// </summary>
    public sealed class PythonSettingsTests : IDisposable
    {
        private readonly TestFolder _folder = new TestFolder();

        public void Dispose() => _folder.Dispose();

        private string SettingsFile => Path.Combine(_folder.FullPath, "settings.json");

        private GlobalSettingsViewModel Dialog(string pythonPath = "")
        {
            var model = new MainViewModel { SettingsPath = SettingsFile };
            model.ApplySettings(new GlobalSettings { PythonPath = pythonPath });
            return model.CreateGlobalSettings("Folder preference");
        }

        /// <summary>A folder with a python.exe that is two bytes and no program; gives the folder.</summary>
        private string Install(string name)
        {
            string folder = Path.Combine(_folder.FullPath, name);
            Directory.CreateDirectory(folder);
            File.WriteAllBytes(Path.Combine(folder, "python.exe"), new byte[] { 0x4D, 0x5A });
            return folder;
        }

        private static string MachineFolder => Path.GetDirectoryName(PythonEnvTestSupport.MachinePython.Executable);

        [Fact]
        public void ThePathBox_ReadsTheSettingsPythonPath_AndWritingItIsAppliedAndSavedWithTheOthers()
        {
            string first = Install("first");
            string second = Install("second");
            var model = new MainViewModel { SettingsPath = SettingsFile };
            model.ApplySettings(new GlobalSettings { PythonPath = first });
            GlobalSettingsViewModel dialog = model.CreateGlobalSettings("Folder preference");
            Assert.Equal(first, dialog.PythonPath);

            dialog.PythonPath = second;
            Assert.Equal(first, model.Settings.PythonPath);

            Assert.True(dialog.Apply());
            Assert.Equal(second, model.Settings.PythonPath);

            Assert.True(dialog.Ok());
            Assert.Equal(second, GlobalSettings.Load(SettingsFile).PythonPath);
        }

        [Fact]
        public void WithNothingSet_ThePathBoxIsEmpty_AndAutomaticIsWhatIsLookedFor()
        {
            GlobalSettingsViewModel dialog = Dialog();

            PythonInstallation automatic = PythonInstallation.Find("");
            Assert.Equal("", dialog.PythonPath);
            Assert.Equal(automatic.IsUsable, dialog.IsPythonUsable);
            Assert.Equal(automatic.IsUsable ? automatic.ToString() : automatic.Problem, dialog.PythonFound);
            Assert.Equal(automatic.Source ?? "Automatic: nothing usable", dialog.PythonSource);
            Assert.Equal(automatic.PassedOver, dialog.PythonPassedOver);
            Assert.Equal(automatic.PassedOver.Count > 0, dialog.HasPythonPassedOver);
        }

        [Fact]
        public void AFolderTyped_IsTheInstallInIt_FoundAsGlobalSettings_WithItsVersionAndProgram()
        {
            GlobalSettingsViewModel dialog = Dialog();

            dialog.PythonPath = MachineFolder;

            Assert.True(dialog.IsPythonUsable);
            Assert.Equal(
                "Python "
                    + PythonEnvTestSupport.MachinePython.Version
                    + " ("
                    + Path.Combine(MachineFolder, "python.exe")
                    + ")",
                dialog.PythonFound
            );
            Assert.Equal("Global settings", dialog.PythonSource);
            Assert.Empty(dialog.PythonPassedOver);
            Assert.False(dialog.HasPythonPassedOver);
        }

        [Fact]
        public void ThePythonExeTyped_IsFoundAsTheFolderIs()
        {
            GlobalSettingsViewModel dialog = Dialog();

            dialog.PythonPath = PythonEnvTestSupport.MachinePython.Executable;

            Assert.True(dialog.IsPythonUsable);
            Assert.Equal(PythonEnvTestSupport.MachinePython.ToString(), dialog.PythonFound);
        }

        [Fact]
        public void APathThatIsNotThere_ShowsTheFindersTextWordForWord_AndSaysWhereItLooked()
        {
            string path = Path.Combine(_folder.FullPath, "nowhere", "python.exe");
            GlobalSettingsViewModel dialog = Dialog();

            dialog.PythonPath = path;

            Assert.False(dialog.IsPythonUsable);
            Assert.Equal("The Python set in Global settings (" + path + ") was not found.", dialog.PythonFound);
            Assert.Equal("Global settings", dialog.PythonSource);
        }

        [Fact]
        public void APythonExeThatIsNoProgram_ShowsThatItDidNotRun_AsAPythonProgram()
        {
            string exe = Path.Combine(Install("broken"), "python.exe");
            GlobalSettingsViewModel dialog = Dialog();

            dialog.PythonPath = exe;

            Assert.False(dialog.IsPythonUsable);
            Assert.Equal(exe + " did not run: it is not a Python program.", dialog.PythonFound);
        }

        [Fact]
        public void TheFoundSourceAndPassedOverLines_AreReadAgainEachTimeTheTextChanges()
        {
            GlobalSettingsViewModel dialog = Dialog();
            var changed = new List<string>();
            dialog.PropertyChanged += (s, e) => changed.Add(e.PropertyName);

            dialog.PythonPath = MachineFolder;

            Assert.Contains(nameof(GlobalSettingsViewModel.PythonPath), changed);
            Assert.Contains(nameof(GlobalSettingsViewModel.PythonFound), changed);
            Assert.Contains(nameof(GlobalSettingsViewModel.PythonSource), changed);
            Assert.Contains(nameof(GlobalSettingsViewModel.PythonPassedOver), changed);

            string before = dialog.PythonFound;
            dialog.PythonPath = Install("broken");

            Assert.NotEqual(before, dialog.PythonFound);
            Assert.EndsWith("did not run: it is not a Python program.", dialog.PythonFound, StringComparison.Ordinal);
        }

        [Fact]
        public void Browse_PutsTheFileChosenInTheBox_AndChoosingNothingLeavesTheBoxAsItWas()
        {
            string exe = PythonEnvTestSupport.MachinePython.Executable;
            GlobalSettingsViewModel dialog = Dialog();

            dialog.BrowsePythonFile = () => null;
            dialog.BrowsePythonCommand.Execute(null);
            Assert.Equal("", dialog.PythonPath);

            dialog.BrowsePythonFile = () => exe;
            dialog.BrowsePythonCommand.Execute(null);

            Assert.Equal(exe, dialog.PythonPath);
            Assert.StartsWith("Python ", dialog.PythonFound, StringComparison.Ordinal);
        }

        [Fact]
        public void TheSectionIsShownOnlyWhereTheAppUsesPython()
        {
            Assert.False(Dialog().IsPythonUsed);

            var model = new MainViewModel { SettingsPath = SettingsFile };
            model.ApplySettings(
                new GlobalSettings { PythonPath = Path.Combine(_folder.FullPath, "nowhere", "python.exe") }
            );
            model.UsePython(System.Windows.Threading.Dispatcher.CurrentDispatcher);

            Assert.True(model.CreateGlobalSettings("Folder preference").IsPythonUsed);
        }

        [Fact]
        public void NoToolIsRunningAtStart_SoNoChangeOfThePathWaitsForARestart()
        {
            var model = new MainViewModel { SettingsPath = SettingsFile };
            model.UsePython(System.Windows.Threading.Dispatcher.CurrentDispatcher);

            Assert.False(model.CreateGlobalSettings("Folder preference").HasPythonStarted);
        }
    }
}
