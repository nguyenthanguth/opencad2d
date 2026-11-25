using System;
using System.Collections.Generic;
using System.IO;
using Xunit;
using static OpenCAD2D.Wpf.UnitTest.PythonEnvTestSupport;

namespace OpenCAD2D.Wpf.UnitTest
{
    /// <summary>
    /// A tool's venv made for a start nobody asked for (<c>Prepare(..., ask: false)</c>, which a snap's first question to a tool
    /// with no process makes): it is never asked about packages and pip never runs, so a venv is made only for a tool that names
    /// no package and only where the shared base is there already; otherwise the tool is not ready, and no venv, base or
    /// question is left behind. Every folder is a temporary one and no pip is run, so no wheelhouse is needed.
    /// </summary>
    public sealed class PythonEnvQuietTests : IDisposable
    {
        private readonly string _root = Path.Combine(
            Path.GetTempPath(),
            "OpenCAD2D.PythonEnvQuietTests",
            Guid.NewGuid().ToString("N")
        );
        private readonly List<string> _asked = new List<string>();

        public PythonEnvQuietTests()
        {
            Directory.CreateDirectory(_root);
        }

        public void Dispose()
        {
            try
            {
                Directory.Delete(_root, true);
            }
            catch (IOException) { }
            catch (UnauthorizedAccessException) { }
        }

        private PythonEnvironments Environments(string baseFolder)
        {
            PythonEnvironments environments = PythonEnvTestSupport.Environments(
                _root,
                Path.Combine(_root, "wheels"),
                baseFolder
            );
            environments.Confirm = (tool, packages) =>
            {
                _asked.Add(tool);
                return true;
            };
            return environments;
        }

        [Fact]
        public void AToolThatNamesNoPackage_AndTheBaseIsThere_GetsItsVenvWithNoQuestion()
        {
            PythonEnvironments environments = Environments(SharedBase);

            PythonEnvironment venv = environments.Prepare(
                Tool(_root, "Plain", string.Empty),
                MachinePython,
                null,
                null,
                false
            );

            Assert.True(venv.IsReady, venv.Error);
            Assert.True(File.Exists(venv.Python));
            Assert.Empty(_asked);
        }

        [Fact]
        public void AToolThatNamesAPackage_IsNotReady_AskedNothing_AndLeavesNoVenv()
        {
            PythonEnvironments environments = Environments(SharedBase);
            string tool = Tool(_root, "Needs", "demo-pkg>=1.0\n");

            PythonEnvironment venv = environments.Prepare(tool, MachinePython, null, null, false);

            Assert.False(venv.IsReady);
            Assert.Contains("needs its packages installed first", venv.Error);
            Assert.Empty(_asked);
            Assert.False(Directory.Exists(environments.FolderOf(tool)));
        }

        [Fact]
        public void WhereTheBaseIsNotThereYet_TheToolIsNotReady_AndNoBaseIsMade()
        {
            string empty = Path.Combine(_root, "no-base");
            PythonEnvironments environments = Environments(empty);
            string tool = Tool(_root, "Plain", string.Empty);

            PythonEnvironment venv = environments.Prepare(tool, MachinePython, null, null, false);

            Assert.False(venv.IsReady);
            Assert.False(Directory.Exists(empty));
            Assert.False(Directory.Exists(environments.FolderOf(tool)));
            Assert.Empty(_asked);
        }

        [Fact]
        public void AVenvThatIsCurrent_IsReadyAsItWas_WhetherOrNotAnyoneIsAsked()
        {
            PythonEnvironments environments = Environments(SharedBase);
            string tool = Tool(_root, "Plain", string.Empty);
            PythonEnvironment made = environments.Prepare(tool, MachinePython);
            Assert.True(made.IsReady, made.Error);

            PythonEnvironment again = environments.Prepare(tool, MachinePython, null, null, false);

            Assert.True(again.IsReady, again.Error);
            Assert.Equal(made.Folder, again.Folder);
        }

        [Fact]
        public void AToolWhoseRequirementsChangedToNeedAPackage_IsNotReadyOnItsOldVenv()
        {
            PythonEnvironments environments = Environments(SharedBase);
            string tool = Tool(_root, "Grows", string.Empty);
            Assert.True(environments.Prepare(tool, MachinePython).IsReady);
            File.WriteAllText(Path.Combine(tool, "requirements.txt"), "demo-pkg>=1.0\n");

            PythonEnvironment venv = environments.Prepare(tool, MachinePython, null, null, false);

            Assert.False(venv.IsReady);
            Assert.Null(venv.Failure);
            Assert.Empty(_asked);
        }
    }
}
