using System;
using System.IO;
using Xunit;

namespace OpenCAD2D.Wpf.UnitTest
{
    /// <summary>
    /// The "Passed over" lines of the Python finder when one tag is registered in both hives: each line says which entry it is, so
    /// two lines are never the same sentence. Registered Pythons are empty <c>python.exe</c> files the finder never runs.
    /// </summary>
    public sealed class PythonPassedOverTests : IDisposable
    {
        private readonly string _folder = Path.Combine(
            Path.GetTempPath(),
            "OpenCAD2D.PythonEnvTests",
            "passed-" + Guid.NewGuid().ToString("N")
        );

        public PythonPassedOverTests() => Directory.CreateDirectory(_folder);

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

        private static PythonRegistration Registered(string hive, string tag, string exe) =>
            new PythonRegistration
            {
                Hive = hive,
                Tag = tag,
                Version = tag,
                SysArchitecture = "64bit",
                InstallPath = exe == null ? null : Path.GetDirectoryName(exe),
                ExecutablePath = exe,
            };

        [Fact]
        public void TheSameTagWithNoInstallPathInBothHives_GivesTwoLines_EachNamingItsHive()
        {
            PythonInstallation found = PythonInstallation.Find(
                null,
                new[]
                {
                    Registered("HKCU", "3.10", null),
                    Registered("HKLM", "3.10", null),
                    Registered("HKCU", "3.14", Exe("p314")),
                }
            );

            Assert.True(found.IsUsable, found.Problem);
            Assert.Equal(new[] { "3.10 (HKCU): no InstallPath", "3.10 (HKLM): no InstallPath" }, found.PassedOver);
        }

        [Fact]
        public void TheSameTagUsableInBothHives_NamesTheHiveOfTheLoserAndOfTheWinner()
        {
            PythonInstallation found = PythonInstallation.Find(
                null,
                new[] { Registered("HKLM", "3.13", Exe("machine")), Registered("HKCU", "3.13", Exe("user")) }
            );

            Assert.Equal(Path.Combine(_folder, "user", "python.exe"), found.Executable);
            Assert.Equal(new[] { "3.13 (HKLM): 3.13 (HKCU) was chosen instead" }, found.PassedOver);
        }

        [Fact]
        public void ATagRegisteredOnce_KeepsTheLineItHad()
        {
            PythonInstallation found = PythonInstallation.Find(
                null,
                new[] { Registered("HKLM", "3.11", null), Registered("HKCU", "3.10", null) }
            );

            Assert.Equal(new[] { "3.11: no InstallPath", "3.10: no InstallPath" }, found.PassedOver);
        }
    }
}
