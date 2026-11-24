using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Xunit;
using static OpenCAD2D.Wpf.UnitTest.PythonEnvTestSupport;

namespace OpenCAD2D.Wpf.UnitTest
{
    /// <summary>
    /// A venv for each Python tool and the pip that fills it (S23 to S29): two tools with two versions of one package, the report
    /// listed before anything is installed, a missing version and a clash that change nothing, a key that makes a new venv
    /// swapped in, a lower bound installed as its version. Every pip run is real and offline: a wheelhouse made in code, given
    /// with <c>--no-index --find-links</c>, and a temporary folder for every root.
    /// </summary>
    public sealed class PythonEnvTests : IDisposable
    {
        private readonly string _root = Path.Combine(
            Path.GetTempPath(),
            "OpenCAD2D.PythonEnvTests",
            Guid.NewGuid().ToString("N")
        );
        private readonly string _wheelhouse;
        private readonly PythonEnvironments _environments;
        private readonly List<string> _asked = new List<string>();

        public PythonEnvTests()
        {
            Directory.CreateDirectory(_root);
            _wheelhouse = Path.Combine(_root, "wheelhouse");
            Wheel(_wheelhouse, "demo-pkg", "1.0");
            Wheel(_wheelhouse, "demo-pkg", "2.0");
            _environments = Environments(_root, _wheelhouse);
            _environments.Confirm = Yes;
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

        /// <summary>The question a user would say yes to; each tool and its packages asked about are kept.</summary>
        private bool Yes(string tool, IReadOnlyList<PythonPackage> packages)
        {
            _asked.Add(tool + ": " + string.Join(", ", packages.Select(p => p.ToString())));
            return true;
        }

        private PythonEnvironment Prepare(string tool, string requirements, Action beforeSwap = null) =>
            _environments.Prepare(Tool(_root, tool, requirements), MachinePython, beforeSwap);

        private static string Version(PythonEnvironment environment) =>
            Run(environment.Python, "import demo_pkg; print(demo_pkg.VERSION)");

        [Fact]
        public void TwoToolsThatPinTwoVersionsOfOnePackage_EachImportsItsOwn()
        {
            PythonEnvironment first = Prepare("ToolA", "demo-pkg==1.0\n");
            PythonEnvironment second = Prepare("ToolB", "demo-pkg==2.0\n");

            Assert.True(first.IsReady, first.Error);
            Assert.True(second.IsReady, second.Error);
            Assert.NotEqual(first.Folder, second.Folder);
            Assert.Equal("1.0", Version(first));
            Assert.Equal("2.0", Version(second));
        }

        [Fact]
        public void AVenv_FindsTheBaseAndTheAppsPackage_AfterItsOwnPackages()
        {
            PythonEnvironment environment = Prepare("Tool", "demo-pkg==1.0\n");

            Assert.True(environment.IsReady, environment.Error);
            string[] lines = File.ReadAllLines(
                Path.Combine(environment.Folder, "Lib", "site-packages", "opencad2d-tool.pth")
            );
            Assert.Equal(new[] { AppPython, Path.Combine(SharedBase, MachinePython.Tag) }, lines);
            string order = Run(
                environment.Python,
                "import sys, demo_pkg, pythonnet, clr_loader, cffi, pycparser\n"
                    + "own = sys.path.index(next(p for p in sys.path if p.endswith('site-packages')))\n"
                    + "base = sys.path.index(r'"
                    + Path.Combine(SharedBase, MachinePython.Tag)
                    + "')\n"
                    + "print(own < base)"
            );
            Assert.Equal("True", order);
        }

        [Fact]
        public void TheReportIsListedBeforeAnythingIsInstalled_TheToolsOwnFirst()
        {
            Wheel(_wheelhouse, "other", "1.0", new[] { "demo-pkg==2.0" });
            var installedWhenAsked = new List<bool>();
            string site = null;
            _environments.Confirm = (tool, packages) =>
            {
                site = Path.Combine(
                    _environments.FolderOf(Path.Combine(_root, "Pythons", tool)),
                    "Lib",
                    "site-packages"
                );
                installedWhenAsked.Add(Directory.Exists(Path.Combine(site, "demo_pkg")));
                return Yes(tool, packages);
            };

            PythonEnvironment environment = Prepare("Tool", "other==1.0\n");

            Assert.True(environment.IsReady, environment.Error);
            Assert.Equal(new[] { "Tool: other 1.0, demo-pkg 2.0" }, _asked);
            Assert.Equal(new[] { false }, installedWhenAsked);
            Assert.True(Directory.Exists(Path.Combine(site, "demo_pkg")));
            Assert.Equal("other 1.0, demo-pkg 2.0", string.Join(", ", environment.Installed));
            Assert.Equal("2.0", Version(environment));
        }

        [Fact]
        public void AVersionThatDoesNotExist_GivesPipsLines_AndChangesNothing()
        {
            PythonEnvironment environment = Prepare("Tool", "demo-pkg==9.9\n");

            Assert.False(environment.IsReady);
            Assert.Contains(
                "ERROR: Could not find a version that satisfies the requirement demo-pkg==9.9",
                environment.Error
            );
            Assert.Contains("ERROR: No matching distribution found for demo-pkg==9.9", environment.Error);
            Assert.Contains(
                "Tool was not installed. If this computer is offline, connect and try again, or run: ",
                environment.Error
            );
            Assert.Contains("-m pip --python", environment.Error);
            Assert.Empty(_asked);
            Assert.False(Directory.Exists(environment.Folder));
            Assert.False(
                Directory.Exists(Path.Combine(_root, "envs"))
                    && Directory.GetFileSystemEntries(Path.Combine(_root, "envs")).Length > 0
            );
        }

        [Fact]
        public void TwoRequirementsThatClash_GivePipsLines_AndChangeNothing()
        {
            PythonEnvironment environment = Prepare("Tool", "demo-pkg==1.0\ndemo-pkg==2.0\n");

            Assert.False(environment.IsReady);
            Assert.Contains("conflicting dependencies", environment.Error);
            Assert.Contains(
                "ERROR: Cannot install demo-pkg==1.0 and demo-pkg==2.0 because these package versions have conflicting dependencies.",
                environment.Error
            );
            Assert.Contains("Tool was not installed.", environment.Error);
            Assert.Empty(_asked);
            Assert.False(Directory.Exists(environment.Folder));
        }

        [Fact]
        public void AFailedChange_LeavesTheVenvAsItWas()
        {
            PythonEnvironment before = Prepare("Tool", "demo-pkg==1.0\n");
            string key = File.ReadAllText(Path.Combine(before.Folder, "opencad2d-tool.json"));
            int swaps = 0;

            PythonEnvironment missing = Prepare("Tool", "demo-pkg==9.9\n", () => swaps++);

            Assert.True(missing.IsReady, missing.Error);
            Assert.Contains("Could not find a version", missing.Failure);
            Assert.Contains("Tool was not installed.", missing.Failure);
            Assert.Equal(before.Python, missing.Python);
            Assert.Equal(0, swaps);
            Assert.Equal(before.Folder, missing.Folder);
            Assert.Equal("1.0", Version(before));
            Assert.Equal(key, File.ReadAllText(Path.Combine(before.Folder, "opencad2d-tool.json")));
            Assert.False(Directory.Exists(before.Folder + ".new"));
            Assert.Single(_asked);
        }

        [Fact]
        public void TheKeyChangesWithRequirements_AndTheVenvIsSwapped_AnUnchangedOneIsLeftAlone()
        {
            int swaps = 0;
            PythonEnvironment first = Prepare("Tool", "demo-pkg==1.0\n", () => swaps++);
            PythonEnvironment again = Prepare("Tool", "demo-pkg==1.0\n", () => swaps++);

            Assert.True(first.IsReady, first.Error);
            Assert.True(again.IsReady, again.Error);
            Assert.Empty(again.Installed);
            Assert.Single(_asked);
            Assert.Equal(0, swaps);

            PythonEnvironment changed = Prepare("Tool", "demo-pkg==2.0\n", () => swaps++);

            Assert.True(changed.IsReady, changed.Error);
            Assert.Equal(first.Folder, changed.Folder);
            Assert.Equal(new[] { "Tool: demo-pkg 1.0", "Tool: demo-pkg 2.0" }, _asked);
            Assert.Equal(1, swaps);
            Assert.Equal("2.0", Version(changed));
            Assert.False(Directory.Exists(first.Folder + ".new"));
            Assert.False(Directory.Exists(first.Folder + ".old"));
        }

        [Fact]
        public void AnInstallThatFailsAfterTheYes_KeepsTheOldVenv()
        {
            PythonEnvironment before = Prepare("Tool", "demo-pkg==1.0\n");
            int swaps = 0;
            _environments.Confirm = (tool, packages) =>
            {
                // The wheel goes between the list and the install, as a download that fails would.
                File.Delete(Path.Combine(_wheelhouse, "demo_pkg-2.0-py3-none-any.whl"));
                return Yes(tool, packages);
            };

            PythonEnvironment failed = Prepare("Tool", "demo-pkg==2.0\n", () => swaps++);

            Assert.True(failed.IsReady, failed.Error);
            Assert.Contains("ERROR:", failed.Failure);
            Assert.Contains("Tool was not installed.", failed.Failure);
            Assert.Equal(before.Python, failed.Python);
            Assert.Equal("1.0", Version(failed));
            Assert.Equal(0, swaps);
            Assert.Equal("1.0", Version(before));
            Assert.False(Directory.Exists(before.Folder + ".new"));
            Assert.False(Directory.Exists(before.Folder + ".old"));
        }

        [Fact]
        public void AChangeThatIsNotApproved_OrIsRefused_KeepsTheOldVenvRunning_WithTheLines()
        {
            PythonEnvironment before = Prepare("Tool", "demo-pkg==1.0\n");
            _environments.Confirm = (tool, packages) => false;

            PythonEnvironment declined = Prepare("Tool", "demo-pkg==2.0\n");
            PythonEnvironment refused = Prepare("Tool", "-r other.txt\n");

            Assert.True(declined.IsReady, declined.Error);
            Assert.Contains("its packages were not approved", declined.Failure);
            Assert.Equal(before.Python, declined.Python);
            Assert.Equal("1.0", Version(declined));
            Assert.True(refused.IsReady, refused.Error);
            Assert.Equal("requirements.txt line 1: options are not taken", refused.Failure);
            Assert.Equal(before.Python, refused.Python);
        }

        [Fact]
        public void ATool_WithNoVenvYet_StaysFailed_WhenItsInstallFails()
        {
            PythonEnvironment missing = Prepare("Tool", "demo-pkg==9.9\n");

            Assert.False(missing.IsReady);
            Assert.Null(missing.Python);
            Assert.Null(missing.Failure);
        }

        [Fact]
        public void AVenvInUse_IsNotReplaced_AndTheNewOneIsDiscarded()
        {
            PythonEnvironment before = Prepare("Tool", "demo-pkg==1.0\n");

            // A file held open stands for the tool's process, which holds its venv.
            using (
                new FileStream(
                    Path.Combine(before.Folder, "pyvenv.cfg"),
                    FileMode.Open,
                    FileAccess.Read,
                    FileShare.None
                )
            )
            {
                PythonEnvironment held = Prepare("Tool", "demo-pkg==2.0\n");

                Assert.True(held.IsReady, held.Error);
                Assert.Contains(
                    "Tool was not installed: its environment is in use and could not be replaced",
                    held.Failure
                );
            }

            Assert.Equal("1.0", Version(before));
            Assert.False(Directory.Exists(before.Folder + ".new"));
            Assert.False(Directory.Exists(before.Folder + ".old"));
        }

        [Fact]
        public void ALowerBound_IsInstalledAsThatVersion()
        {
            PythonEnvironment environment = Prepare("Tool", "demo-pkg>=1.0\n");

            Assert.True(environment.IsReady, environment.Error);
            Assert.Equal(new[] { "Tool: demo-pkg 1.0" }, _asked);
            Assert.Equal("1.0", Version(environment));
            Assert.Null(environment.Error);
        }

        [Fact]
        public void ALowerBoundWithNoWheelForThisPython_IsTakenAsWritten_AndPipPicks()
        {
            string wheelhouse = Path.Combine(_root, "only-other-pythons");
            Wheel(wheelhouse, "demo-pkg", "1.0", null, "cp27-cp27m-win32");
            Wheel(wheelhouse, "demo-pkg", "1.5");
            PythonEnvironments asking = Environments(_root, wheelhouse);
            asking.Confirm = Yes;

            PythonEnvironment environment = asking.Prepare(Tool(_root, "Tool", "demo-pkg>=1.0\n"), MachinePython);

            Assert.True(environment.IsReady, environment.Error);
            Assert.Equal(new[] { "Tool: demo-pkg 1.5" }, _asked);
            Assert.Equal("1.5", Version(environment));
        }

        [Fact]
        public void ALowerBoundWhoseLowestVersionHasOnlyASourceArchive_TakesTheWheel_AndBuildsNothing()
        {
            // 1.0 is a source archive only, as numpy 2.0.0 is on Python 3.14; 2.0 has a wheel. Pinned to 1.0, pip would build
            // 1.0 from source (minutes for numpy) and the venv would be reported ready.
            string wheelhouse = Path.Combine(_root, "source-first");
            string built = Path.Combine(_root, "built.txt");
            SourceArchive(wheelhouse, "demo-pkg", "1.0", built);
            Wheel(wheelhouse, "demo-pkg", "2.0");
            PythonEnvironments asking = Environments(_root, wheelhouse);
            asking.Confirm = Yes;

            PythonEnvironment environment = asking.Prepare(Tool(_root, "Tool", "demo-pkg>=1.0\n"), MachinePython);

            Assert.True(environment.IsReady, environment.Error);
            Assert.Equal(new[] { "Tool: demo-pkg 2.0" }, _asked);
            Assert.Equal("2.0", Version(environment));
            Assert.False(File.Exists(built));
        }

        [Fact]
        public void ALowerBoundThatClashesWithAnotherLine_IsTakenAsWritten()
        {
            Wheel(_wheelhouse, "other", "1.0", new[] { "demo-pkg==2.0" });

            PythonEnvironment environment = Prepare("Tool", "demo-pkg>=1.0\nother==1.0\n");

            Assert.True(environment.IsReady, environment.Error);
            Assert.Equal(new[] { "Tool: demo-pkg 2.0, other 1.0" }, _asked);
            Assert.Equal("2.0", Version(environment));
        }

        [Fact]
        public void WithNoHook_NothingIsInstalled_AndTheToolIsRefusedWithTheLineToRun()
        {
            _environments.Confirm = null;

            PythonEnvironment environment = Prepare("Tool", "demo-pkg==1.0\n");

            Assert.False(environment.IsReady);
            Assert.Contains("Tool was not installed: its packages were not approved.", environment.Error);
            Assert.Contains("install -r", environment.Error);
            Assert.False(Directory.Exists(environment.Folder));
        }

        [Fact]
        public void ANo_InstallsNothing_AndLeavesNoVenv()
        {
            _environments.Confirm = (tool, packages) => false;

            PythonEnvironment environment = Prepare("Tool", "demo-pkg==1.0\n");

            Assert.False(environment.IsReady);
            Assert.Contains("were not approved", environment.Error);
            Assert.False(Directory.Exists(environment.Folder));
        }

        [Fact]
        public void ATool_WithNoPackages_NeedsNoQuestion_AndNoPip()
        {
            _environments.Confirm = (tool, packages) => throw new InvalidOperationException("Nothing to ask.");

            PythonEnvironment environment = Prepare("Tool", "# nothing\n\n");

            Assert.True(environment.IsReady, environment.Error);
            Assert.Empty(environment.Installed);
            Assert.Equal("True", Run(environment.Python, "import pythonnet, clr_loader; print(True)"));
        }

        [Fact]
        public void AnOptionLine_IsRefused_WithItsLineNumber()
        {
            PythonEnvironment environment = Prepare("Tool", "demo-pkg==1.0\n\n--index-url http://elsewhere/\n");

            Assert.False(environment.IsReady);
            Assert.Equal("requirements.txt line 3: options are not taken", environment.Error);
            Assert.False(Directory.Exists(environment.Folder));
        }

        [Fact]
        public void AFolderWithNoRequirementsTxt_IsRefused()
        {
            string folder = Path.Combine(_root, "Pythons", "Bare");
            Directory.CreateDirectory(folder);

            PythonEnvironment environment = _environments.Prepare(folder, MachinePython);

            Assert.False(environment.IsReady);
            Assert.Equal(
                "Bare has no requirements.txt: a Python tool is a folder with requirements.txt.",
                environment.Error
            );
        }

        [Fact]
        public void TheBase_IsMadeOnce_ForAPythonVersion_AndTheNextToolNeedsNoWheels()
        {
            string baseFolder = Path.Combine(_root, "own-base");
            string wheels = Path.Combine(_root, "own-wheels");
            Directory.CreateDirectory(wheels);

            foreach (string wheel in Directory.GetFiles(ShippedWheels, "*.whl"))
            {
                File.Copy(wheel, Path.Combine(wheels, Path.GetFileName(wheel)));
            }

            var environments = new PythonEnvironments(
                Path.Combine(_root, "envs"),
                baseFolder,
                AppPython,
                wheels,
                new[] { "--no-index", "--find-links", _wheelhouse }
            )
            {
                Confirm = Yes,
            };

            Assert.True(environments.Prepare(Tool(_root, "One", "demo-pkg==1.0\n"), MachinePython).IsReady);
            string made = Assert.Single(Directory.GetDirectories(baseFolder));
            Directory.Delete(wheels, true);
            PythonEnvironment second = environments.Prepare(Tool(_root, "Two", "demo-pkg==2.0\n"), MachinePython);

            Assert.True(second.IsReady, second.Error);
            Assert.Equal(Path.Combine(baseFolder, MachinePython.Tag), made);
            Assert.Single(Directory.GetDirectories(baseFolder));
            Assert.True(Directory.Exists(Path.Combine(made, "pythonnet")));
            Assert.True(Directory.Exists(Path.Combine(made, "clr_loader")));
            Assert.True(Directory.Exists(Path.Combine(made, "cffi")));
            Assert.True(Directory.Exists(Path.Combine(made, "pycparser")));
        }

        [Fact]
        public void WithNoWheels_TheBaseCannotBeMade_AndNothingIsLeft()
        {
            string baseFolder = Path.Combine(_root, "empty-base");
            var environments = new PythonEnvironments(
                Path.Combine(_root, "envs"),
                baseFolder,
                AppPython,
                Path.Combine(_root, "no-wheels")
            );

            PythonEnvironment environment = environments.Prepare(Tool(_root, "Tool", string.Empty), MachinePython);

            Assert.False(environment.IsReady);
            Assert.Contains("The Python tools could not be set up: pip could not install pythonnet", environment.Error);
            Assert.Contains("ERROR:", environment.Error);
            Assert.False(Directory.Exists(baseFolder) && Directory.GetFileSystemEntries(baseFolder).Length > 0);
        }

        [Fact]
        public void APythonWithNoPip_IsSaidSo()
        {
            string bare = Path.Combine(_root, "bare");
            Assert.Equal(
                0,
                PythonRun
                    .Start(MachinePython.Executable, new[] { "-m", "venv", "--without-pip", bare }, 120000)
                    .ExitCode
            );
            PythonInstallation withoutPip = PythonInstallation.Find(Path.Combine(bare, "Scripts", "python.exe"));
            Assert.True(withoutPip.IsUsable, withoutPip.Problem);

            PythonEnvironment environment = _environments.Prepare(Tool(_root, "Tool", "demo-pkg==1.0"), withoutPip);

            Assert.False(environment.IsReady);
            Assert.Equal(
                "The Python at "
                    + withoutPip.Executable
                    + " has no pip: install Python from python.org with pip, or set another in Global settings > Python.",
                environment.Error
            );
        }

        [Fact]
        public void TwoToolsOfOneNameInTwoPlaces_HaveTwoVenvs()
        {
            string one = Tool(Path.Combine(_root, "a"), "Stairs", string.Empty);
            string two = Tool(Path.Combine(_root, "b"), "Stairs", string.Empty);

            string first = _environments.FolderOf(one);
            string second = _environments.FolderOf(two);

            Assert.NotEqual(first, second);
            Assert.Matches(@"[\\/]Stairs-[0-9a-f]{8}$", first);
            Assert.Matches(@"[\\/]Stairs-[0-9a-f]{8}$", second);
            Assert.Equal(
                first.Substring(first.Length - 8),
                _environments.FolderOf(one.ToUpperInvariant()).Substring(first.Length - 8)
            );
            Assert.Equal(Path.Combine(_root, "envs"), Path.GetDirectoryName(first));
        }

        [Fact]
        public void TheEnvironmentsOfTheApp_AreInLocalAppData_NeverMadeByATest()
        {
            PythonEnvironments app = PythonEnvironments.ForApp(Path.Combine(_root, "app"));

            string folder = app.FolderOf(Path.Combine(_root, "Pythons", "Stairs"));

            Assert.StartsWith(
                Path.Combine(
                    Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                    "OpenCAD2D",
                    "python",
                    "envs",
                    "Stairs-"
                ),
                folder,
                StringComparison.Ordinal
            );
        }
    }
}
