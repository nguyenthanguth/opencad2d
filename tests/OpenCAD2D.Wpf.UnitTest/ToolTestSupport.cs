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
    /// A Python for the tool tests, made with no network as the app makes a tool's: a venv of the machine's Python in a temporary
    /// folder (<see cref="PythonEnvironments"/>), whose <c>.pth</c> file puts the package <c>opencad2d</c> of this repository's
    /// source (as the app's own copy is) and a base with pythonnet and its three dependencies, installed from the wheels the app ships,
    /// on the path. A tool runs on coreclr, from the library the build left.
    /// </summary>
    public sealed class ToolPython : IDisposable
    {
        public ToolPython()
        {
            Root = PythonEnvTestSupport.Root;
            Folder = Path.Combine(Path.GetTempPath(), "OpenCAD2D.ToolTests", Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(Folder);
            var environments = new PythonEnvironments(
                Path.Combine(Folder, "envs"),
                PythonEnvTestSupport.SharedBase,
                PythonEnvTestSupport.AppPython,
                PythonEnvTestSupport.ShippedWheels
            );
            PythonEnvironment venv = environments.Prepare(
                Tool("_default", string.Empty),
                PythonEnvTestSupport.MachinePython
            );

            if (!venv.IsReady)
            {
                throw new InvalidOperationException("The venv of the tool tests could not be made: " + venv.Error);
            }

            Python = venv.Python;

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
    }
}
