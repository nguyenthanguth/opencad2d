using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using OpenCAD2D.Remote;

namespace OpenCAD2D.Wpf
{
    /// <summary>
    /// The venvs of the Python tools: one for each tool, <c>envs\&lt;name&gt;-&lt;8 hex&gt;</c> (the hex is the start of the SHA-256 of
    /// the tool folder's full path, so two tools of one name in two places have two), made from the Python of the app's settings,
    /// holding the packages of the tool's <c>requirements.txt</c> and never the user's own Python environment. So two tools may
    /// pin two versions of one package, and both run.
    /// </summary>
    /// <remarks>
    /// A venv is made with no pip of its own (0.07 s and 0.5 MB, against 4 s and 12 MB), and the Python's pip installs into it
    /// with <c>--python</c>. <c>opencad2d</c> is not installed in a venv: a <c>.pth</c> file puts the app's own copy of the package
    /// and a shared base on the path, after the venv's own packages. The base, <c>base\cp3XY</c>, holds pythonnet, clr_loader,
    /// cffi and pycparser, installed once for a Python version from the wheels the app ships (no network, no question: they are
    /// the app's own). The tool's packages come after a yes: pip's dry run lists what would be installed, the
    /// <see cref="Confirm"/> hook is asked with the list, and only on a yes is anything installed. A key (the tool's
    /// <c>requirements.txt</c>, the Python's path and version) is kept in the venv; when it changes, a new venv is built beside the
    /// old and swapped in once it works, so a failed install leaves the old venv as it was. Nothing here touches
    /// <c>%LocalAppData%\OpenCAD2D</c> except through the folders given to the constructor.
    /// </remarks>
    internal sealed class PythonEnvironments
    {
        private const string KeyFile = "opencad2d-tool.json";
        private const string PathFile = "opencad2d-tool.pth";
        private const string RequirementsFile = "opencad2d-requirements.txt";
        private const string ReportFile = "opencad2d-report.json";
        private const int PipMilliseconds = 600000;
        private const int PipLines = 5;
        private static readonly string[] BasePackages = { "pythonnet", "clr_loader", "cffi", "pycparser" };

        private readonly string _environments;
        private readonly string _base;
        private readonly string _appPython;
        private readonly string _wheels;
        private readonly string[] _pipOptions;
        private readonly object _lock = new object();

        /// <summary>Makes the venvs of the folders given. Tests pass temporary folders and a wheelhouse of their own.</summary>
        /// <param name="environments">The folder of the venvs, <c>%LocalAppData%\OpenCAD2D\python\envs</c> for the app.</param>
        /// <param name="baseFolder">The folder of the shared bases, one folder <c>cp3XY</c> for each Python version.</param>
        /// <param name="appPython">The folder that holds the app's copy of the package <c>opencad2d</c>.</param>
        /// <param name="wheels">The folder of the wheels the base is installed from (pythonnet, clr_loader, cffi for each supported Python, pycparser).</param>
        /// <param name="pipOptions">Options added to every pip run that installs a tool's packages, such as <c>--no-index</c> and <c>--find-links</c> of a local wheelhouse; none for pip's own index.</param>
        /// <exception cref="ArgumentNullException">Thrown when a folder is null.</exception>
        public PythonEnvironments(
            string environments,
            string baseFolder,
            string appPython,
            string wheels,
            IEnumerable<string> pipOptions = null
        )
        {
            _environments = Path.GetFullPath(environments ?? throw new ArgumentNullException(nameof(environments)));
            _base = Path.GetFullPath(baseFolder ?? throw new ArgumentNullException(nameof(baseFolder)));
            _appPython = Path.GetFullPath(appPython ?? throw new ArgumentNullException(nameof(appPython)));
            _wheels = Path.GetFullPath(wheels ?? throw new ArgumentNullException(nameof(wheels)));
            _pipOptions = pipOptions?.ToArray() ?? new string[0];
        }

        /// <summary>Gets or sets what is asked before a tool's packages are installed: given the tool's name and the packages pip would install (the ones the tool names first, then the rest, each in ordinal order), whether to install them. Null means no one can be asked, and nothing is installed.</summary>
        public Func<string, IReadOnlyList<PythonPackage>, bool> Confirm { get; set; }

        /// <summary>Makes the venvs of the app: <c>%LocalAppData%\OpenCAD2D\python\envs</c> and <c>base</c>, the package and the wheels beside the app's exe in <c>python</c> and <c>python\wheels</c>.</summary>
        /// <param name="appFolder">The folder of the app's exe.</param>
        /// <returns>The venvs.</returns>
        /// <exception cref="ArgumentNullException">Thrown when the folder is null.</exception>
        public static PythonEnvironments ForApp(string appFolder)
        {
            if (appFolder == null)
            {
                throw new ArgumentNullException(nameof(appFolder));
            }

            string python = Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                "OpenCAD2D",
                "python"
            );
            return new PythonEnvironments(
                Path.Combine(python, "envs"),
                Path.Combine(python, "base"),
                Path.Combine(appFolder, "python"),
                Path.Combine(appFolder, "python", "wheels")
            );
        }

        /// <summary>Gets the folder of a tool's venv: <c>&lt;envs&gt;\Stairs-1a2b3c4d</c>, made or not.</summary>
        /// <param name="toolFolder">The tool's folder.</param>
        /// <returns>The venv's full path.</returns>
        /// <exception cref="ArgumentNullException">Thrown when the folder is null.</exception>
        public string FolderOf(string toolFolder)
        {
            if (toolFolder == null)
            {
                throw new ArgumentNullException(nameof(toolFolder));
            }

            string full = Path.GetFullPath(toolFolder).TrimEnd('\\', '/');
            byte[] hash;

            using (SHA256 sha = SHA256.Create())
            {
                hash = sha.ComputeHash(Encoding.UTF8.GetBytes(full.ToLowerInvariant()));
            }

            return Path.Combine(_environments, Path.GetFileName(full) + "-" + Hex(hash).Substring(0, 8));
        }

        /// <summary>
        /// Makes a tool's venv ready: nothing is done where the venv holds the key of the tool's <c>requirements.txt</c> and the
        /// Python already; otherwise a venv is built, the packages are listed by pip's dry run, <see cref="Confirm"/> is asked, and
        /// on a yes they are installed and the venv put in place. It takes as long as pip does: call it off the window's thread.
        /// </summary>
        /// <param name="toolFolder">The tool's folder, with its <c>requirements.txt</c>.</param>
        /// <param name="python">The Python the venv is made from.</param>
        /// <param name="beforeSwap">Called when a venv that exists is about to be replaced, to stop the tool's process, which holds its files; null for none.</param>
        /// <returns>The venv, or the lines to show where it could not be made; the venv that was there is left as it was.</returns>
        /// <exception cref="ArgumentNullException">Thrown when the folder or the Python is null.</exception>
        public PythonEnvironment Prepare(string toolFolder, PythonInstallation python, Action beforeSwap = null)
        {
            if (toolFolder == null)
            {
                throw new ArgumentNullException(nameof(toolFolder));
            }

            if (python == null)
            {
                throw new ArgumentNullException(nameof(python));
            }

            string folder = Path.GetFullPath(toolFolder).TrimEnd('\\', '/');
            string tool = Path.GetFileName(folder);
            string target = FolderOf(folder);
            string requirementsPath = Path.Combine(folder, "requirements.txt");

            if (!python.IsUsable)
            {
                return PythonEnvironment.Failed(target, python.Problem);
            }

            if (!File.Exists(requirementsPath))
            {
                return PythonEnvironment.Failed(
                    target,
                    tool + " has no requirements.txt: a Python tool is a folder with requirements.txt."
                );
            }

            byte[] written = File.ReadAllBytes(requirementsPath);
            IReadOnlyList<PythonRequirement> requirements = PythonRequirement.Read(
                new UTF8Encoding(false).GetString(written).TrimStart('﻿'),
                out string refused
            );

            if (refused != null)
            {
                return PythonEnvironment.Failed(target, refused);
            }

            string key = Key(written, python);

            lock (_lock)
            {
                if (IsCurrent(target, key))
                {
                    return PythonEnvironment.Ready(target, new PythonPackage[0]);
                }

                try
                {
                    return Build(tool, folder, target, key, requirements, python, beforeSwap);
                }
                catch (Exception exception)
                    when (exception is IOException
                        || exception is UnauthorizedAccessException
                        || exception is System.ComponentModel.Win32Exception
                    )
                {
                    return PythonEnvironment.Failed(
                        target,
                        tool + " was not installed: its environment could not be made (" + exception.Message + ")."
                    );
                }
            }
        }

        private PythonEnvironment Build(
            string tool,
            string folder,
            string target,
            string key,
            IReadOnlyList<PythonRequirement> requirements,
            PythonInstallation python,
            Action beforeSwap
        )
        {
            // Pip is asked for only where it will be used: to make the base, or to list and install the tool's packages.
            bool needsPip = requirements.Count > 0 || !Directory.Exists(BaseFolder(python));

            if (needsPip && PythonRun.Start(python.Executable, new[] { "-m", "pip", "--version" }, 60000).ExitCode != 0)
            {
                return PythonEnvironment.Failed(
                    target,
                    "The Python at "
                        + python.Executable
                        + " has no pip: install Python from python.org with pip, or set another in Global settings > Python."
                );
            }

            string error = EnsureBase(python);

            if (error != null)
            {
                return PythonEnvironment.Failed(target, error);
            }

            // A venv that is there is replaced only when the new one works; a first venv is made where it will stay.
            bool replacing = Directory.Exists(target);
            string staging = replacing ? target + ".new" : target;
            Discard(staging);
            PythonRun made = PythonRun.Start(
                python.Executable,
                new[] { "-m", "venv", "--without-pip", staging },
                120000
            );

            if (made.ExitCode != 0)
            {
                Discard(staging);
                return PythonEnvironment.Failed(
                    target,
                    tool + " was not installed: its environment could not be made. " + Lines(made)
                );
            }

            string venvPython = Path.Combine(staging, "Scripts", "python.exe");
            File.WriteAllText(
                Path.Combine(staging, "Lib", "site-packages", PathFile),
                _appPython + "\n" + BaseFolder(python) + "\n",
                new UTF8Encoding(false)
            );
            IReadOnlyList<PythonPackage> packages = new PythonPackage[0];

            if (requirements.Count > 0)
            {
                packages = Resolve(requirements, python, staging, venvPython, out string resolution);

                if (packages == null)
                {
                    Discard(staging);
                    return PythonEnvironment.Failed(
                        target,
                        resolution + "\n" + NotInstalled(tool, folder, python, target, true)
                    );
                }

                bool approved;

                try
                {
                    approved = Confirm != null && Confirm(tool, packages);
                }
                catch
                {
                    Discard(staging);
                    throw;
                }

                if (!approved)
                {
                    Discard(staging);
                    return PythonEnvironment.Failed(target, NotInstalled(tool, folder, python, target, false));
                }

                PythonRun installed = PythonRun.Start(
                    python.Executable,
                    new[]
                    {
                        "-m",
                        "pip",
                        "--python",
                        venvPython,
                        "install",
                        "-r",
                        Path.Combine(staging, RequirementsFile),
                    }.Concat(_pipOptions),
                    PipMilliseconds
                );

                if (installed.ExitCode != 0)
                {
                    Discard(staging);
                    return PythonEnvironment.Failed(
                        target,
                        PipError(installed) + "\n" + NotInstalled(tool, folder, python, target, true)
                    );
                }
            }

            File.WriteAllText(
                Path.Combine(staging, KeyFile),
                RemoteJson.Write(
                    new JsonObject().Set("key", key).Set("python", python.Executable).Set("version", python.Version)
                ),
                new UTF8Encoding(false)
            );

            if (replacing)
            {
                error = Swap(tool, target, staging, beforeSwap);

                if (error != null)
                {
                    Discard(staging);
                    return PythonEnvironment.Failed(target, error);
                }
            }

            return PythonEnvironment.Ready(target, packages);
        }

        /// <summary>Lists what pip would install, with the pins of <see cref="PythonRequirement"/> taken back one at a time where pip cannot install them; null with the lines to show where pip cannot resolve the file at all.</summary>
        private IReadOnlyList<PythonPackage> Resolve(
            IReadOnlyList<PythonRequirement> requirements,
            PythonInstallation python,
            string staging,
            string venvPython,
            out string error
        )
        {
            string file = Path.Combine(staging, RequirementsFile);
            string report = Path.Combine(staging, ReportFile);

            // At most three dry runs: with the pins, with the pins pip named taken back, with all of them taken back.
            for (int attempt = 0; ; attempt++)
            {
                File.WriteAllText(
                    file,
                    string.Join("\n", requirements.Select(r => r.Text)) + "\n",
                    new UTF8Encoding(false)
                );
                PythonRun run = PythonRun.Start(
                    python.Executable,
                    new[]
                    {
                        "-m",
                        "pip",
                        "--python",
                        venvPython,
                        "install",
                        "--dry-run",
                        "--report",
                        report,
                        "-r",
                        file,
                    }.Concat(_pipOptions),
                    PipMilliseconds
                );

                if (run.ExitCode == 0)
                {
                    error = null;
                    return ReadReport(report);
                }

                List<PythonRequirement> pinned = requirements.Where(r => r.IsPinned).ToList();

                if (pinned.Count == 0 || attempt == 2)
                {
                    error = PipError(run);
                    return null;
                }

                // A pin pip says it found no version of is taken back alone; any other failure (the pins clashing with another
                // line) takes them all back, since the file as written may well work.
                string said = run.Error + run.Output;
                List<PythonRequirement> missing = said.Contains("satisfies the requirement")
                    ? pinned
                        .Where(r => said.IndexOf(r.Text.Split(';')[0].Trim(), StringComparison.OrdinalIgnoreCase) >= 0)
                        .ToList()
                    : new List<PythonRequirement>();

                foreach (PythonRequirement requirement in missing.Count > 0 && attempt == 0 ? missing : pinned)
                {
                    requirement.Unpin();
                }
            }
        }

        private static IReadOnlyList<PythonPackage> ReadReport(string path)
        {
            var packages = new List<PythonPackage>();
            var report = (JsonObject)RemoteJson.Parse(File.ReadAllText(path, Encoding.UTF8));
            File.Delete(path);

            foreach (object item in report.GetArray("install"))
            {
                var install = (JsonObject)item;
                JsonObject metadata = install.GetObject("metadata");
                packages.Add(
                    new PythonPackage(
                        metadata.GetString("name"),
                        metadata.GetString("version"),
                        install.GetBool("requested")
                    )
                );
            }

            return packages
                .OrderBy(p => p.IsRequested ? 0 : 1)
                .ThenBy(p => p.Name, StringComparer.OrdinalIgnoreCase)
                .ThenBy(p => p.Name, StringComparer.Ordinal)
                .ThenBy(p => p.Version, StringComparer.Ordinal)
                .ToList();
        }

        /// <summary>Makes the base of a Python version, once: pythonnet, clr_loader, cffi and pycparser from the wheels the app ships, with no index. Null where it is there or was made; the line to show where it could not be.</summary>
        private string EnsureBase(PythonInstallation python)
        {
            string folder = BaseFolder(python);

            if (Directory.Exists(folder))
            {
                return null;
            }

            // Made beside and moved in whole, so that a folder that exists is a base that is complete.
            string temporary = folder + "." + Guid.NewGuid().ToString("N").Substring(0, 8) + ".tmp";
            PythonRun run = PythonRun.Start(
                python.Executable,
                new[] { "-m", "pip", "install", "--target", temporary, "--no-index", "--find-links", _wheels }.Concat(
                    BasePackages
                ),
                PipMilliseconds
            );

            if (run.ExitCode != 0)
            {
                Discard(temporary);
                return "The Python tools could not be set up: pip could not install "
                    + string.Join(", ", BasePackages)
                    + " from "
                    + _wheels
                    + ".\n"
                    + PipError(run);
            }

            try
            {
                Directory.Move(temporary, folder);
            }
            catch (IOException)
            {
                // Another copy of the app made it first.
                Discard(temporary);

                if (!Directory.Exists(folder))
                {
                    throw;
                }
            }

            return null;
        }

        private string BaseFolder(PythonInstallation python) => Path.Combine(_base, python.Tag);

        /// <summary>Puts a built venv where the tool's is: the tool's process is stopped, the old venv set aside, the new one moved in, the old one deleted. Null where it is done; the line to show where it is not, with the old venv back.</summary>
        private static string Swap(string tool, string target, string staging, Action beforeSwap)
        {
            string old = target + ".old";
            Discard(old);
            beforeSwap?.Invoke();

            try
            {
                Directory.Move(target, old);
            }
            catch (Exception exception) when (exception is IOException || exception is UnauthorizedAccessException)
            {
                return tool
                    + " was not installed: its environment is in use and could not be replaced ("
                    + exception.Message
                    + ").";
            }

            try
            {
                Directory.Move(staging, target);
            }
            catch (Exception exception) when (exception is IOException || exception is UnauthorizedAccessException)
            {
                Directory.Move(old, target);
                return tool
                    + " was not installed: its new environment could not be put in place ("
                    + exception.Message
                    + ").";
            }

            Discard(old);
            return null;
        }

        private static bool IsCurrent(string target, string key)
        {
            string file = Path.Combine(target, KeyFile);

            if (!File.Exists(file) || !File.Exists(Path.Combine(target, "Scripts", "python.exe")))
            {
                return false;
            }

            try
            {
                return RemoteJson.Parse(File.ReadAllText(file, Encoding.UTF8)) is JsonObject json
                    && string.Equals(json.GetString("key"), key, StringComparison.Ordinal);
            }
            catch (FormatException)
            {
                return false;
            }
        }

        /// <summary>The key of a venv: the SHA-256 of the tool's requirements.txt, the Python's path and its version.</summary>
        private static string Key(byte[] requirements, PythonInstallation python)
        {
            using (SHA256 sha = SHA256.Create())
            {
                byte[] tail = Encoding.UTF8.GetBytes("\n" + python.Executable + "\n" + python.Version);
                return Hex(sha.ComputeHash(requirements.Concat(tail).ToArray()));
            }
        }

        private static string Hex(byte[] bytes) => string.Concat(bytes.Select(b => b.ToString("x2")));

        /// <summary>The line of the app after pip's own: what was not done, and the command that does it by hand.</summary>
        private static string NotInstalled(
            string tool,
            string folder,
            PythonInstallation python,
            string target,
            bool offline
        )
        {
            string hand =
                "\""
                + python.Executable
                + "\" -m pip --python \""
                + Path.Combine(target, "Scripts", "python.exe")
                + "\" install -r \""
                + Path.Combine(folder, "requirements.txt")
                + "\"";
            return offline
                ? tool + " was not installed. If this computer is offline, connect and try again, or run: " + hand
                : tool + " was not installed: its packages were not approved. To install them yourself, run: " + hand;
        }

        /// <summary>Pip's own words for a run that failed: from its first <c>ERROR:</c> line to the end, else the last lines.</summary>
        private static string PipError(PythonRun run) => Lines(run, true);

        private static string Lines(PythonRun run, bool fromError = false)
        {
            string[] lines = (run.Error.Trim().Length > 0 ? run.Error : run.Output).Trim().Split('\n');
            int first = Array.FindIndex(lines, l => l.StartsWith("ERROR:", StringComparison.Ordinal));
            int from = fromError && first >= 0 ? first : Math.Max(0, lines.Length - PipLines);
            return string.Join("\n", lines.Skip(from).Select(l => l.TrimEnd())).Trim();
        }

        private static void Discard(string folder)
        {
            try
            {
                if (Directory.Exists(folder))
                {
                    Directory.Delete(folder, true);
                }
            }
            catch (IOException) { }
            catch (UnauthorizedAccessException) { }
        }
    }

    /// <summary>
    /// What <see cref="PythonEnvironments.Prepare"/> made of a tool's venv: the <c>python.exe</c> to start the tool with, or the
    /// lines to show where it could not be made.
    /// </summary>
    internal sealed class PythonEnvironment
    {
        private PythonEnvironment() { }

        /// <summary>Gets the folder of the venv.</summary>
        public string Folder { get; private set; }

        /// <summary>Gets the <c>python.exe</c> of the venv, the program a tool's process is started with; null where the venv is not ready.</summary>
        public string Python { get; private set; }

        /// <summary>Gets what to show where the venv is not ready: pip's own lines and the app's after them; null where it is.</summary>
        public string Error { get; private set; }

        /// <summary>Gets whether the venv is ready: there is no <see cref="Error"/>.</summary>
        public bool IsReady => Error == null;

        /// <summary>Gets the packages installed by this call, in the order they were asked about; none where the venv was ready already or the tool names none.</summary>
        public IReadOnlyList<PythonPackage> Installed { get; private set; } = new PythonPackage[0];

        internal static PythonEnvironment Ready(string folder, IReadOnlyList<PythonPackage> installed) =>
            new PythonEnvironment
            {
                Folder = folder,
                Python = Path.Combine(folder, "Scripts", "python.exe"),
                Installed = installed,
            };

        internal static PythonEnvironment Failed(string folder, string error) =>
            new PythonEnvironment { Folder = folder, Error = error };
    }
}
