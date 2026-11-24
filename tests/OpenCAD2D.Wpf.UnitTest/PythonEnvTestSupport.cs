using System;
using System.Collections.Generic;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Security.Cryptography;
using System.Text;

namespace OpenCAD2D.Wpf.UnitTest
{
    /// <summary>
    /// What the tests of the tools' venvs share: a wheelhouse made in code (a zip with <c>METADATA</c>, <c>WHEEL</c> and
    /// <c>RECORD</c> holds a package whose one module says its version), the Python of the machine to make venvs from, and a base
    /// for it that is made once for the whole test run from the wheels the app ships. No test reaches the network or writes
    /// <c>%LocalAppData%\OpenCAD2D</c>: pip is given <c>--no-index --find-links</c> and every folder is in a temporary one.
    /// </summary>
    internal static class PythonEnvTestSupport
    {
        private static readonly Lazy<PythonInstallation> Found = new Lazy<PythonInstallation>(FindPython);
        private static readonly Lazy<string> Base = new Lazy<string>(MakeBase);

        /// <summary>Gets the folder of the repository.</summary>
        public static string Root { get; } = RepositoryRoot();

        /// <summary>Gets the folder of the wheels the app ships (pythonnet, clr_loader, cffi for each Python, pycparser).</summary>
        public static string ShippedWheels => Path.Combine(Root, "src", "OpenCAD2D.App", "python-wheels");

        /// <summary>Gets the app's own copy of the package <c>opencad2d</c>, as the repository has it.</summary>
        public static string AppPython => Path.Combine(Root, "src", "python");

        /// <summary>Gets the Python of the machine the tests make venvs from: the one <c>OPENCAD2D_TEST_PYTHON</c> names, else the newest registered.</summary>
        public static PythonInstallation MachinePython => Found.Value;

        /// <summary>Gets a folder that holds the base for the Python above, made on the first use of any test and removed when the test run ends.</summary>
        public static string SharedBase => Base.Value;

        /// <summary>Makes venvs of a folder of the test, with a wheelhouse of its own and the shared base.</summary>
        public static PythonEnvironments Environments(string root, string wheelhouse, string baseFolder = null) =>
            new PythonEnvironments(
                Path.Combine(root, "envs"),
                baseFolder ?? SharedBase,
                AppPython,
                ShippedWheels,
                new[] { "--no-index", "--find-links", wheelhouse }
            );

        /// <summary>Writes one wheel of a made-up package into a wheelhouse: a module <c>demo_pkg</c> with <c>VERSION = "1.0"</c>.</summary>
        /// <param name="wheelhouse">The wheelhouse folder.</param>
        /// <param name="name">The package's name, <c>demo-pkg</c>.</param>
        /// <param name="version">Its version.</param>
        /// <param name="requires">Its <c>Requires-Dist</c> lines (<c>demo-pkg==2.0</c>).</param>
        /// <param name="tag">The wheel's tag; <c>py3-none-any</c> installs anywhere.</param>
        /// <returns>The wheel's path.</returns>
        public static string Wheel(
            string wheelhouse,
            string name,
            string version,
            string[] requires = null,
            string tag = "py3-none-any"
        )
        {
            Directory.CreateDirectory(wheelhouse);
            string module = name.Replace('-', '_');
            string info = module + "-" + version + ".dist-info";
            var files = new List<KeyValuePair<string, string>>
            {
                new KeyValuePair<string, string>(module + "/__init__.py", "VERSION = \"" + version + "\"\n"),
                new KeyValuePair<string, string>(
                    info + "/METADATA",
                    "Metadata-Version: 2.1\nName: "
                        + name
                        + "\nVersion: "
                        + version
                        + "\n"
                        + string.Concat((requires ?? new string[0]).Select(r => "Requires-Dist: " + r + "\n"))
                ),
                new KeyValuePair<string, string>(
                    info + "/WHEEL",
                    "Wheel-Version: 1.0\nGenerator: tests\nRoot-Is-Purelib: true\nTag: " + tag + "\n"
                ),
            };
            var record = new StringBuilder();

            using (SHA256 sha = SHA256.Create())
            {
                foreach (KeyValuePair<string, string> file in files)
                {
                    byte[] bytes = Encoding.UTF8.GetBytes(file.Value);
                    string hash = Convert
                        .ToBase64String(sha.ComputeHash(bytes))
                        .TrimEnd('=')
                        .Replace('+', '-')
                        .Replace('/', '_');
                    record
                        .Append(file.Key)
                        .Append(",sha256=")
                        .Append(hash)
                        .Append(',')
                        .Append(bytes.Length)
                        .Append('\n');
                }
            }

            record.Append(info).Append("/RECORD,,\n");
            files.Add(new KeyValuePair<string, string>(info + "/RECORD", record.ToString()));
            string path = Path.Combine(wheelhouse, module + "-" + version + "-" + tag + ".whl");

            using (ZipArchive zip = ZipFile.Open(path, ZipArchiveMode.Create))
            {
                foreach (KeyValuePair<string, string> file in files)
                {
                    using (var writer = new StreamWriter(zip.CreateEntry(file.Key).Open(), new UTF8Encoding(false)))
                    {
                        writer.Write(file.Value);
                    }
                }
            }

            return path;
        }

        /// <summary>
        /// Writes a source archive of a made-up package into a wheelhouse: a <c>.zip</c> that pip takes for a source distribution
        /// and builds with the backend inside it (no index, no setuptools), which creates a marker file whenever pip builds
        /// metadata or a wheel, so a test can say that nothing was built. Its module says the version, as <see cref="Wheel"/>'s does.
        /// </summary>
        /// <param name="wheelhouse">The wheelhouse folder.</param>
        /// <param name="name">The package's name, <c>demo-pkg</c>.</param>
        /// <param name="version">Its version.</param>
        /// <param name="marker">The file the backend creates when pip builds from the archive.</param>
        /// <returns>The archive's path.</returns>
        public static string SourceArchive(string wheelhouse, string name, string version, string marker)
        {
            Directory.CreateDirectory(wheelhouse);
            string module = name.Replace('-', '_');
            string backend = @"import os, zipfile
MARKER = r'@MARKER@'
INFO = '@MODULE@-@VERSION@.dist-info'
METADATA = 'Metadata-Version: 2.1\nName: @NAME@\nVersion: @VERSION@\n'
def get_requires_for_build_wheel(config_settings=None): return []
def prepare_metadata_for_build_wheel(metadata_directory, config_settings=None):
    open(MARKER, 'a').close()
    os.makedirs(os.path.join(metadata_directory, INFO))
    with open(os.path.join(metadata_directory, INFO, 'METADATA'), 'w') as f: f.write(METADATA)
    return INFO
def build_wheel(wheel_directory, config_settings=None, metadata_directory=None):
    open(MARKER, 'a').close()
    file = '@MODULE@-@VERSION@-py3-none-any.whl'
    with zipfile.ZipFile(os.path.join(wheel_directory, file), 'w') as z:
        z.writestr('@MODULE@/__init__.py', 'VERSION = ""@VERSION@""\n')
        z.writestr(INFO + '/METADATA', METADATA)
        z.writestr(INFO + '/WHEEL', 'Wheel-Version: 1.0\nGenerator: tests\nRoot-Is-Purelib: true\nTag: py3-none-any\n')
        z.writestr(INFO + '/RECORD', '')
    return file
".Replace("@MARKER@", marker).Replace("@MODULE@", module).Replace("@NAME@", name).Replace("@VERSION@", version);
            var files = new[]
            {
                new KeyValuePair<string, string>(
                    "PKG-INFO",
                    "Metadata-Version: 2.1\nName: " + name + "\nVersion: " + version + "\n"
                ),
                new KeyValuePair<string, string>(
                    "pyproject.toml",
                    "[build-system]\nrequires = []\nbuild-backend = \"backend\"\nbackend-path = [\".\"]\n"
                ),
                new KeyValuePair<string, string>("backend.py", backend),
            };
            string path = Path.Combine(wheelhouse, module + "-" + version + ".zip");

            using (ZipArchive zip = ZipFile.Open(path, ZipArchiveMode.Create))
            {
                foreach (KeyValuePair<string, string> file in files)
                {
                    string entry = module + "-" + version + "/" + file.Key;

                    using (var writer = new StreamWriter(zip.CreateEntry(entry).Open(), new UTF8Encoding(false)))
                    {
                        writer.Write(file.Value);
                    }
                }
            }

            return path;
        }

        /// <summary>Makes a tool's folder with the given <c>requirements.txt</c>.</summary>
        public static string Tool(string root, string name, string requirements)
        {
            string folder = Path.Combine(root, "Pythons", name);
            Directory.CreateDirectory(folder);
            File.WriteAllText(Path.Combine(folder, "requirements.txt"), requirements, new UTF8Encoding(false));
            return folder;
        }

        /// <summary>Runs a line of Python in a venv and gives what it printed.</summary>
        public static string Run(string python, string code)
        {
            PythonRun run = PythonRun.Start(python, new[] { "-I", "-c", code }, 120000);

            if (run.ExitCode != 0)
            {
                throw new InvalidOperationException(python + " -c " + code + " failed: " + run.Error);
            }

            return run.Output.Trim();
        }

        private static PythonInstallation FindPython()
        {
            PythonInstallation python = PythonInstallation.Find(
                Environment.GetEnvironmentVariable("OPENCAD2D_TEST_PYTHON")
            );

            if (!python.IsUsable)
            {
                throw new InvalidOperationException(
                    "No Python for the venv tests: "
                        + python.Problem
                        + " Install one, or name a python.exe with pip in OPENCAD2D_TEST_PYTHON."
                );
            }

            return python;
        }

        private static string MakeBase()
        {
            string folder = Path.Combine(
                Path.GetTempPath(),
                "OpenCAD2D.PythonEnvTests",
                "base-" + Guid.NewGuid().ToString("N").Substring(0, 8)
            );
            AppDomain.CurrentDomain.ProcessExit += (sender, e) =>
            {
                try
                {
                    Directory.Delete(folder, true);
                }
                catch (IOException) { }
                catch (UnauthorizedAccessException) { }
            };
            return folder;
        }

        private static string RepositoryRoot()
        {
            for (
                var folder = new DirectoryInfo(AppDomain.CurrentDomain.BaseDirectory);
                folder != null;
                folder = folder.Parent
            )
            {
                if (File.Exists(Path.Combine(folder.FullName, "OpenCAD2D.slnx")))
                {
                    return folder.FullName;
                }
            }

            throw new InvalidOperationException(
                "The folder of OpenCAD2D.slnx is not above " + AppDomain.CurrentDomain.BaseDirectory + "."
            );
        }
    }
}
