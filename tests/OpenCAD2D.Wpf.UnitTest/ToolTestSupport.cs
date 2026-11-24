using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Text;

namespace OpenCAD2D.Wpf.UnitTest
{
    /// <summary>
    /// An app with no window for the tool host: a few drawings, a command session for each and one gate that stands for the
    /// window's thread, which a test takes to touch a drawing while the host's threads wait for it.
    /// </summary>
    internal sealed class ToolTestApp : ICadRemoteHost, ICadRemoteDispatcher
    {
        private readonly List<CadDocument> _documents = new List<CadDocument>();
        private readonly Dictionary<CadDocument, CadCommandSession> _sessions =
            new Dictionary<CadDocument, CadCommandSession>();

        /// <summary>Gets the gate the host's work is run under.</summary>
        public object Gate { get; } = new object();

        public string Title => "OpenCAD2D";

        public IReadOnlyList<CadDocument> Documents => _documents;

        public CadDocument ActiveDocument { get; private set; }

        /// <summary>Puts a drawing in the app and shows it.</summary>
        public CadDocument Add(CadDocument document)
        {
            _documents.Add(document);
            _sessions[document] = new CadCommandSession(document) { PickAperture = 1.0 };
            ActiveDocument = document;
            return document;
        }

        public void Invoke(Action action)
        {
            lock (Gate)
            {
                action();
            }
        }

        /// <summary>Runs a function as the window's thread would, between two pieces of the host's work.</summary>
        public T OnApp<T>(Func<T> function)
        {
            lock (Gate)
            {
                return function();
            }
        }

        /// <summary>Runs an action as the window's thread would, between two pieces of the host's work.</summary>
        public void OnApp(Action action)
        {
            lock (Gate)
            {
                action();
            }
        }

        public CadDocument Open(string path) => throw new NotSupportedException();

        public CadDocument New(string template) => throw new NotSupportedException();

        public void Activate(CadDocument document) => ActiveDocument = document;

        public CadCommandSession SessionOf(CadDocument document) => _sessions[document];

        public void Type(CadDocument document, string line) => _sessions[document].Feed(line);

        public void Save(CadDocument document) => document.Save();

        public void SaveAs(CadDocument document, string path, string version) => document.SaveAs(path, version, null);

        public void Close(CadDocument document)
        {
            _documents.Remove(document);
            _sessions.Remove(document);
        }
    }

    /// <summary>
    /// A Python for the tool tests, made with no network: a venv with no pip, in a temporary folder, whose <c>.pth</c> file puts
    /// the package <c>opencad2d</c> of this repository's source (as the app's own copy is) and a folder with pythonnet and its three
    /// dependencies (copied from a Python that has them) on the path. A tool runs on coreclr, from the library the build left.
    /// </summary>
    public sealed class ToolPython : IDisposable
    {
        public ToolPython()
        {
            Root = RepositoryRoot();
            Folder = Path.Combine(Path.GetTempPath(), "OpenCAD2D.ToolTests", Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(Folder);
            string site = FindPythonnet(Root, out string maker);
            string baseFolder = Path.Combine(Folder, "base");
            Directory.CreateDirectory(baseFolder);

            foreach (string name in new[] { "pythonnet", "clr_loader", "cffi", "pycparser" })
            {
                Copy(Path.Combine(site, name), Path.Combine(baseFolder, name));
            }

            foreach (
                string file in Directory.GetFiles(site, "_cffi_backend*").Concat(Directory.GetFiles(site, "clr.py"))
            )
            {
                File.Copy(file, Path.Combine(baseFolder, Path.GetFileName(file)));
            }

            string venv = Path.Combine(Folder, "venv");
            Run(maker, "-m venv --without-pip \"" + venv + "\"");
            File.WriteAllText(
                Path.Combine(venv, "Lib", "site-packages", "opencad2d-tool.pth"),
                Path.Combine(Root, "src", "python") + "\n" + baseFolder + "\n",
                new UTF8Encoding(false)
            );
            Python = Path.Combine(venv, "Scripts", "python.exe");

            // The library the build leaves, as the wheel takes it: the three assemblies and their support folders.
            string configuration = new DirectoryInfo(AppDomain.CurrentDomain.BaseDirectory.TrimEnd('\\')).Parent.Name;
            Library = Path.Combine(Root, "src", "OpenCAD2D.Core", "bin", configuration, "netstandard2.0");
        }

        /// <summary>Gets the folder of the repository.</summary>
        public string Root { get; }

        /// <summary>Gets the temporary folder the venv is in.</summary>
        public string Folder { get; }

        /// <summary>Gets the <c>python.exe</c> of the venv.</summary>
        public string Python { get; }

        /// <summary>Gets the folder tools load OpenCAD2D.Core.dll and the rest from.</summary>
        public string Library { get; }

        public void Dispose()
        {
            try
            {
                Directory.Delete(Folder, true);
            }
            catch (IOException) { }
            catch (UnauthorizedAccessException) { }
        }

        /// <summary>Makes a tool's folder in the temporary folder, its <c>__init__.py</c> the source.</summary>
        public string Tool(string name, string source)
        {
            string folder = Path.Combine(Folder, "Pythons", name);
            Directory.CreateDirectory(folder);
            File.WriteAllText(Path.Combine(folder, "__init__.py"), source, new UTF8Encoding(false));
            File.WriteAllText(Path.Combine(folder, "requirements.txt"), string.Empty);
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

        /// <summary>
        /// A site-packages that has pythonnet, clr_loader, cffi and pycparser, and the python.exe to make a venv from that goes
        /// with it (a different Python would not take the compiled cffi): the venv the Python suite makes (here or in the
        /// repository this is a worktree of), else a Python of the machine, else the one named by <c>OPENCAD2D_TEST_PYTHON</c>.
        /// </summary>
        private static string FindPythonnet(string root, out string python)
        {
            var found = new List<string>();
            string named = Environment.GetEnvironmentVariable("OPENCAD2D_TEST_PYTHON");

            if (!string.IsNullOrEmpty(named))
            {
                found.Add(Path.GetDirectoryName(Path.GetFullPath(named)));
            }

            found.Add(Path.Combine(root, "artifacts", "python", "venv", "Scripts"));
            string main = MainRepository(root);

            if (main != null)
            {
                found.Add(Path.Combine(main, "artifacts", "python", "venv", "Scripts"));
            }

            found.AddRange(PythonsOfTheMachine());

            foreach (string scripts in found)
            {
                // A venv's python.exe is in Scripts, with Lib beside it; an installed Python's has Lib beside it too.
                string exe = Path.Combine(scripts, "python.exe");
                string home =
                    File.Exists(Path.Combine(scripts, "python.exe"))
                    && Directory.Exists(Path.Combine(scripts, "..", "Lib"))
                        ? Path.GetFullPath(Path.Combine(scripts, ".."))
                        : Path.GetFullPath(scripts);
                string site = Path.Combine(home, "Lib", "site-packages");

                if (
                    File.Exists(exe)
                    && Directory.Exists(Path.Combine(site, "pythonnet"))
                    && Directory.Exists(Path.Combine(site, "clr_loader"))
                    && Directory.Exists(Path.Combine(site, "cffi"))
                    && Directory.Exists(Path.Combine(site, "pycparser"))
                )
                {
                    python = exe;
                    return site;
                }
            }

            throw new InvalidOperationException(
                "No Python with pythonnet was found for the tool tests: run tools/python/verify-python.sh once (it makes artifacts/python/venv), or name a python.exe with pythonnet in OPENCAD2D_TEST_PYTHON."
            );
        }

        /// <summary>The folder of the repository a worktree belongs to, from the <c>.git</c> file it holds; null where this is no worktree.</summary>
        private static string MainRepository(string root)
        {
            string git = Path.Combine(root, ".git");

            if (!File.Exists(git))
            {
                return null;
            }

            string line = File.ReadAllLines(git).FirstOrDefault(l => l.StartsWith("gitdir:", StringComparison.Ordinal));
            int worktrees = line == null ? -1 : line.IndexOf("/.git/worktrees/", StringComparison.Ordinal);
            return worktrees < 0
                ? null
                : line.Substring("gitdir:".Length, worktrees - "gitdir:".Length).Trim().Replace('/', '\\');
        }

        private static IEnumerable<string> PythonsOfTheMachine()
        {
            string output;

            try
            {
                output = Run("py", "-0p");
            }
            catch (System.ComponentModel.Win32Exception)
            {
                yield break;
            }

            foreach (string line in output.Split('\n'))
            {
                string path = line.Trim();
                int exe = path.IndexOf("python.exe", StringComparison.OrdinalIgnoreCase);

                if (exe > 0)
                {
                    int start = path.LastIndexOf(' ', exe) + 1;
                    yield return Path.GetDirectoryName(path.Substring(start, exe + "python.exe".Length - start));
                }
            }
        }

        private static string Run(string program, string arguments)
        {
            var start = new ProcessStartInfo(program, arguments)
            {
                UseShellExecute = false,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                CreateNoWindow = true,
            };

            using (Process process = Process.Start(start))
            {
                string output = process.StandardOutput.ReadToEnd();
                string error = process.StandardError.ReadToEnd();

                if (!process.WaitForExit(120000) || (process.ExitCode != 0 && program != "py"))
                {
                    throw new InvalidOperationException(program + " " + arguments + " failed: " + error);
                }

                return output;
            }
        }

        private static void Copy(string from, string to)
        {
            Directory.CreateDirectory(to);

            foreach (string file in Directory.GetFiles(from))
            {
                File.Copy(file, Path.Combine(to, Path.GetFileName(file)));
            }

            foreach (string folder in Directory.GetDirectories(from).Where(d => Path.GetFileName(d) != "__pycache__"))
            {
                Copy(folder, Path.Combine(to, Path.GetFileName(folder)));
            }
        }
    }
}
