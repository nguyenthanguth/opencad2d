using System;
using System.IO;
using Xunit;

namespace OpenCAD2D.Wpf.UnitTest
{
    /// <summary>
    /// Which Python the venvs are made from (S24): the Global settings path, else the newest 64-bit 3.11 to 3.15 of PEP 514. The
    /// registry is not read: registrations are given to the seam. Registered Pythons are empty <c>python.exe</c> files that the
    /// finder never runs; a Python named by the setting is run once, to ask its version.
    /// </summary>
    public sealed class PythonEnvFinderTests : IDisposable
    {
        private const string NoPython =
            "No Python 3.11 to 3.15 (64-bit) was found. Install one from python.org, or set its python.exe in Global settings > Python.";

        private readonly string _folder = Path.Combine(
            Path.GetTempPath(),
            "OpenCAD2D.PythonEnvTests",
            "finder-" + Guid.NewGuid().ToString("N")
        );

        public PythonEnvFinderTests()
        {
            Directory.CreateDirectory(_folder);
        }

        public void Dispose()
        {
            try
            {
                Directory.Delete(_folder, true);
            }
            catch (IOException) { }
        }

        private string Exe(string name)
        {
            string folder = Path.Combine(_folder, name);
            Directory.CreateDirectory(folder);
            string exe = Path.Combine(folder, "python.exe");
            File.WriteAllText(exe, "not run");
            return exe;
        }

        private static PythonRegistration Registered(
            string hive,
            string tag,
            string version,
            string exe,
            string architecture = "64bit"
        ) =>
            new PythonRegistration
            {
                Hive = hive,
                Tag = tag,
                Version = version,
                SysArchitecture = architecture,
                InstallPath = exe == null ? null : Path.GetDirectoryName(exe),
                ExecutablePath = exe,
            };

        [Fact]
        public void TheNewestOfThe311To315Pythons_IsChosen_AndTheOthersAreSaidToBePassedOver()
        {
            PythonInstallation found = PythonInstallation.Find(
                null,
                new[]
                {
                    Registered("HKCU", "3.12", "3.12.8", Exe("p312")),
                    Registered("HKCU", "3.14", "3.14.4", Exe("p314")),
                    Registered("HKLM", "3.10", "3.10.9", Exe("p310")),
                    Registered("HKLM", "3.16", "3.16.0", Exe("p316")),
                }
            );

            Assert.True(found.IsUsable, found.Problem);
            Assert.Equal(Path.Combine(_folder, "p314", "python.exe"), found.Executable);
            Assert.Equal("3.14.4", found.Version);
            Assert.Equal("cp314", found.Tag);
            Assert.Equal("Registry (PEP 514)", found.Source);
            Assert.Equal(
                new[]
                {
                    "3.10: Python 3.10 is outside 3.11 to 3.15",
                    "3.16: Python 3.16 is outside 3.11 to 3.15",
                    "3.12: 3.14 was chosen instead",
                },
                found.PassedOver
            );
        }

        [Fact]
        public void AtOneVersion_TheUsersOwnPython_IsChosenOverTheMachinesAndTheOrderOfTheListIsNotWhatDecides()
        {
            PythonRegistration machine = Registered("HKLM", "3.13", "3.13.1", Exe("machine"));
            PythonRegistration user = Registered("HKCU", "3.13", "3.13.1", Exe("user"));

            Assert.Equal(
                Path.Combine(_folder, "user", "python.exe"),
                PythonInstallation.Find(null, new[] { machine, user }).Executable
            );
            Assert.Equal(
                Path.Combine(_folder, "user", "python.exe"),
                PythonInstallation.Find(null, new[] { user, machine }).Executable
            );
        }

        [Fact]
        public void A32BitPython_AMissingExe_AndAnEntryWithNoPath_AreLeftOut_AndNoneUsableSaysSo()
        {
            PythonInstallation found = PythonInstallation.Find(
                string.Empty,
                new[]
                {
                    Registered("HKCU", "3.13-32", "3.13.1", Exe("p32"), "32bit"),
                    Registered("HKCU", "3.12", "3.12.1", Path.Combine(_folder, "gone", "python.exe")),
                    Registered("HKLM", "3.11", "3.11.1", null),
                }
            );

            Assert.False(found.IsUsable);
            Assert.Equal(NoPython, found.Problem);
            Assert.Null(found.Executable);
            Assert.Null(found.Tag);
            Assert.Contains(found.PassedOver, line => line.EndsWith("is a 32-bit Python", StringComparison.Ordinal));
            Assert.Contains(found.PassedOver, line =>
                line.EndsWith("gone\\python.exe is missing", StringComparison.Ordinal)
            );
            Assert.Contains("3.11: no InstallPath", found.PassedOver);
        }

        [Fact]
        public void ASettingThatIsNotThere_IsAProblem_NeverReplacedByAnotherPython()
        {
            string missing = Path.Combine(_folder, "nowhere", "python.exe");

            PythonInstallation found = PythonInstallation.Find(
                missing,
                new[] { Registered("HKCU", "3.14", "3.14.4", Exe("p314")) }
            );

            Assert.False(found.IsUsable);
            Assert.Equal("The Python set in Global settings (" + missing + ") was not found.", found.Problem);
            Assert.Equal("Global settings", found.Source);
        }

        [Fact]
        public void ASettingThatIsAFile_ButNotAPython_IsAProblem()
        {
            PythonInstallation found = PythonInstallation.Find(Exe("fake"), new PythonRegistration[0]);

            Assert.False(found.IsUsable);
            Assert.EndsWith("python.exe did not run: it is not a Python program.", found.Problem);
        }

        [Fact]
        public void ASettingThatNamesARealPython_GivesItsVersion_AFolderOfItToo()
        {
            string exe = PythonEnvTestSupport.MachinePython.Executable;

            PythonInstallation byFile = PythonInstallation.Find(exe, new PythonRegistration[0]);
            PythonInstallation byFolder = PythonInstallation.Find(
                "\"" + Path.GetDirectoryName(exe) + "\"",
                new PythonRegistration[0]
            );

            Assert.True(byFile.IsUsable, byFile.Problem);
            Assert.True(byFolder.IsUsable, byFolder.Problem);
            Assert.Equal(exe, byFile.Executable);
            Assert.Equal(byFile.Executable, byFolder.Executable);
            Assert.StartsWith("3.", byFile.Version, StringComparison.Ordinal);
            Assert.Equal("Global settings", byFile.Source);
            Assert.Equal("cp3" + byFile.Version.Split('.')[1], byFile.Tag);
        }

        /// <summary>A folder that holds a <c>python.exe</c> that is only a file, as the app's <c>python\runtime</c> would hold the real one.</summary>
        private string FakeBundled()
        {
            string exe = Exe("bundled-fake");
            return Path.GetDirectoryName(exe);
        }

        [Fact]
        public void TheBundledPython_IsLast_AMachinePythonOfTheRegistryIsChosenOverIt_WithoutRunningIt()
        {
            PythonInstallation found = PythonInstallation.Find(
                null,
                new[] { Registered("HKCU", "3.13", "3.13.1", Exe("p313")) },
                FakeBundled()
            );

            Assert.True(found.IsUsable, found.Problem);
            Assert.Equal(Path.Combine(_folder, "p313", "python.exe"), found.Executable);
            Assert.Equal("Registry (PEP 514)", found.Source);
            Assert.Empty(found.PassedOver);
        }

        [Fact]
        public void TheBundledPython_IsLast_ASettingIsChosenOverIt_AndAMissingSettingIsNotReplacedByIt()
        {
            string real = PythonEnvTestSupport.MachinePython.Executable;
            string missing = Path.Combine(_folder, "nowhere", "python.exe");
            var registered = new PythonRegistration[0];

            PythonInstallation set = PythonInstallation.Find(real, registered, FakeBundled());
            PythonInstallation gone = PythonInstallation.Find(missing, registered, Path.GetDirectoryName(real));

            Assert.Equal(real, set.Executable);
            Assert.Equal("Global settings", set.Source);
            Assert.False(gone.IsUsable);
            Assert.Equal("The Python set in Global settings (" + missing + ") was not found.", gone.Problem);
        }

        [Fact]
        public void WithNoSettingAndNoMachinePython_TheBundledPython_IsTheOneChosen()
        {
            string real = PythonEnvTestSupport.MachinePython.Executable;
            PythonRegistration unusable = Registered("HKCU", "3.10", "3.10.9", Exe("p310"));

            PythonInstallation found = PythonInstallation.Find(" ", new[] { unusable }, Path.GetDirectoryName(real));

            Assert.True(found.IsUsable, found.Problem);
            Assert.Equal(real, found.Executable);
            Assert.StartsWith("3.", found.Version, StringComparison.Ordinal);
            Assert.Equal("cp3" + found.Version.Split('.')[1], found.Tag);
            Assert.Equal("Bundled with OpenCAD2D", found.Source);
            Assert.Equal(new[] { "3.10: Python 3.10 is outside 3.11 to 3.15" }, found.PassedOver);
        }

        [Fact]
        public void ABundledFolderWithNoPythonExe_IsNothing_ABundledFileThatDoesNotRun_IsSaidToBePassedOver()
        {
            string empty = Path.Combine(_folder, "empty");
            Directory.CreateDirectory(empty);

            PythonInstallation none = PythonInstallation.Find(null, new PythonRegistration[0], empty);
            PythonInstallation broken = PythonInstallation.Find(null, new PythonRegistration[0], FakeBundled());

            Assert.False(none.IsUsable);
            Assert.Equal(NoPython, none.Problem);
            Assert.Empty(none.PassedOver);
            Assert.False(broken.IsUsable);
            Assert.Equal(NoPython, broken.Problem);
            Assert.Equal(
                new[]
                {
                    "Bundled: "
                        + Path.Combine(_folder, "bundled-fake", "python.exe")
                        + " did not run: it is not a Python program.",
                },
                broken.PassedOver
            );
        }

        [Fact]
        public void AVenvIsMadeFromTheBundledPython_WhereItIsTheOneChosen_AndRunsOnIt()
        {
            string real = PythonEnvTestSupport.MachinePython.Executable;
            string root = Path.Combine(_folder, "venv");
            Directory.CreateDirectory(root);
            PythonInstallation bundled = PythonInstallation.Find(
                null,
                new PythonRegistration[0],
                Path.GetDirectoryName(real)
            );

            PythonEnvironment environment = PythonEnvTestSupport
                .Environments(root, Path.Combine(root, "wheelhouse"))
                .Prepare(PythonEnvTestSupport.Tool(root, "Plain", ""), bundled);

            Assert.True(environment.IsReady, environment.Error);
            Assert.Equal("Bundled with OpenCAD2D", bundled.Source);
            Assert.Contains(
                "home = " + Path.GetDirectoryName(real),
                File.ReadAllText(Path.Combine(environment.Folder, "pyvenv.cfg")),
                StringComparison.OrdinalIgnoreCase
            );
            Assert.Equal(
                "True",
                PythonEnvTestSupport.Run(environment.Python, "import pythonnet, clr_loader; print(True)")
            );
        }
    }
}
