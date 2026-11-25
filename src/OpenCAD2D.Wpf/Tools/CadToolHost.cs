using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using GeometryHelper;
using OpenCAD2D.Plugins;

namespace OpenCAD2D.Wpf
{
    /// <summary>
    /// The Python tools of the app: each runs in a <c>python.exe</c> of its own that the app starts at the tool's first command and
    /// keeps until the tool is unloaded or the app closes, and drives the app on the private channel of the tools
    /// (<see cref="CadRemoteServer.ForTools"/>). A tool's command is a command of the drawing it is typed in, held open by the app
    /// while the process works: its questions are asked on the command line, its changes are one undo step, Esc takes them back and
    /// stops the process within 5 seconds, and a process that crashes, hangs or leaves takes nothing of the app with it.
    /// </summary>
    /// <remarks>
    /// Make it, and call <see cref="Add(string, string, IEnumerable{string}, string)"/> and <see cref="Dispose"/>, on the thread of the drawings; the process's pipes are read
    /// on threads of their own and everything they find is handed to the dispatcher. Nothing here waits for a tool on that thread.
    /// </remarks>
    internal sealed class CadToolHost : IDisposable
    {
        /// <summary>
        /// Makes a tool's Python ready, on a thread other than that of the drawings, when its first command is typed: the venv made
        /// and filled (<see cref="PythonEnvironments.Prepare"/>), after the question of pip if there is one. It is given the run that
        /// waits for it, so that a question can be asked in that run, and the action that stops the tool's process where its venv
        /// is about to be replaced.
        /// </summary>
        /// <param name="run">The run of the command that was typed.</param>
        /// <param name="beforeSwap">Ends the tool's process; to be called when a venv in use is replaced.</param>
        /// <returns>The environment, ready or with the lines that say why not.</returns>
        public delegate PythonEnvironment Prepare(CadRemoteToolRun run, Action beforeSwap);

        private readonly ICadRemoteDispatcher _dispatcher;
        private readonly CadRemoteServer _server;
        private readonly string _library;
        private readonly List<CadFolderTool> _tools = new List<CadFolderTool>();
        private Action<Action> _post;
        private bool _disposed;

        /// <summary>Makes the host and opens the channel of the tools; call it on the thread of the drawings.</summary>
        /// <param name="host">The app's drawings and their commands.</param>
        /// <param name="dispatcher">Runs work on the thread the drawings belong to.</param>
        /// <param name="library">The folder of OpenCAD2D.Core.dll, OpenCAD2D.Remote.dll and GeometryHelper.dll with the Fonts, Hatches and Linetypes folders, which the tools load them from (<c>OPENCAD2D_LIB</c>).</param>
        /// <exception cref="ArgumentNullException">Thrown when the host, the dispatcher or the library is null.</exception>
        /// <exception cref="DirectoryNotFoundException">Thrown when the library folder does not exist.</exception>
        public CadToolHost(ICadRemoteHost host, ICadRemoteDispatcher dispatcher, string library)
            : this(host, dispatcher, library, () => CadRemoteServer.ForTools(host, dispatcher)) { }

        /// <summary>Makes the host with the channel of the tools under an instance name of its own, so that tests that make several in one process do not meet.</summary>
        internal CadToolHost(ICadRemoteHost host, ICadRemoteDispatcher dispatcher, string library, string instance)
            : this(host, dispatcher, library, () => new CadRemoteServer(host, dispatcher, instance, true)) { }

        private CadToolHost(
            ICadRemoteHost host,
            ICadRemoteDispatcher dispatcher,
            string library,
            Func<CadRemoteServer> serve
        )
        {
            if (library == null)
            {
                throw new ArgumentNullException(nameof(library));
            }

            if (!Directory.Exists(library))
            {
                throw new DirectoryNotFoundException("The library folder " + library + " does not exist.");
            }

            _dispatcher = dispatcher ?? throw new ArgumentNullException(nameof(dispatcher));
            _library = Path.GetFullPath(library);
            _server = serve();
            _server.Start();
        }

        /// <summary>Gets or sets where what the tools write on their standard error, and the notes of the host, go; the app's log where null.</summary>
        public Action<string> Log { get; set; }

        /// <summary>Gets how long a tool is given to end a run that was cancelled, or to unload, before its process is ended.</summary>
        public TimeSpan StopAfter { get; } = TimeSpan.FromSeconds(5);

        /// <summary>Gets the tools added.</summary>
        public IReadOnlyList<CadFolderTool> Tools => _tools;

        internal CadRemoteServer Server => _server;

        internal ICadRemoteDispatcher Dispatcher => _dispatcher;

        /// <summary>
        /// Gets or sets how work is run on the thread of the drawings after the work in hand, at Background priority, from any
        /// thread, without waiting for it: what the window's own dispatcher does with <c>BeginInvoke</c>. Where none is set it is
        /// run on a thread of the pool and waited for there, through <see cref="Dispatcher"/>.
        /// </summary>
        internal Action<Action> Post
        {
            get => _post ?? (action => Task.Run(() => _dispatcher.Invoke(action)));
            set => _post = value;
        }

        /// <summary>Gets or sets what the window does with the drawings a tool's snap answers came for (<see cref="CadToolSnaps"/>), called on the thread of the drawings: the canvas follows the cursor again.</summary>
        internal Action<IReadOnlyList<CadDocument>> SnapsAnswered { get; set; }

        internal string Library => _library;

        /// <summary>
        /// Adds a tool: its folder, the Python that runs it, and the commands it declares. No process is started: the first time
        /// one of its commands is typed, the process is, and it stays until <see cref="CadFolderTool.Unload"/> or <see cref="Dispose"/>.
        /// </summary>
        /// <param name="folder">The folder of the tool, whose <c>__init__.py</c> is its package.</param>
        /// <param name="python">The <c>python.exe</c> of the tool's venv (any Python that can import <c>opencad2d</c>).</param>
        /// <param name="commands">The names of the commands the tool defines with <c>@command</c>.</param>
        /// <param name="pythons">A folder of packages the tool imports besides the venv's, such as the Pythons folder of the app; null for none.</param>
        /// <returns>The tool, whose <see cref="CadFolderTool.Commands"/> are for the registry of every drawing.</returns>
        /// <exception cref="ArgumentNullException">Thrown when the folder, the Python or the commands are null.</exception>
        /// <exception cref="ArgumentException">Thrown when a command's name is not one word.</exception>
        /// <exception cref="DirectoryNotFoundException">Thrown when the folder does not exist.</exception>
        /// <exception cref="ObjectDisposedException">Thrown when the host has been disposed.</exception>
        public CadFolderTool Add(string folder, string python, IEnumerable<string> commands, string pythons = null)
        {
            if (python == null)
            {
                throw new ArgumentNullException(nameof(python));
            }

            if (commands == null)
            {
                throw new ArgumentNullException(nameof(commands));
            }

            return Add(
                folder,
                python,
                null,
                commands.Select(c => new CadPluginCommandEntry(c, null, null, false)).ToList(),
                pythons
            );
        }

        /// <summary>
        /// Adds a tool that has no Python yet: its commands as a <c>plugin.json</c> declares them, and the function that makes its
        /// environment ready at its first command (see <see cref="Prepare"/>). Nothing is made now, and no process is started.
        /// </summary>
        /// <param name="folder">The folder of the tool, whose <c>__init__.py</c> is its package.</param>
        /// <param name="prepare">Makes the environment ready at the first command.</param>
        /// <param name="commands">The commands the tool declares.</param>
        /// <param name="pythons">A folder of packages the tool imports besides the venv's, such as the Pythons folder of the app; null for none.</param>
        /// <returns>The tool.</returns>
        /// <exception cref="ArgumentNullException">Thrown when the folder, the function or the commands are null.</exception>
        /// <exception cref="ArgumentException">Thrown when a command's name is not one word.</exception>
        /// <exception cref="DirectoryNotFoundException">Thrown when the folder does not exist.</exception>
        /// <exception cref="ObjectDisposedException">Thrown when the host has been disposed.</exception>
        public CadFolderTool Add(
            string folder,
            Prepare prepare,
            IReadOnlyList<CadPluginCommandEntry> commands,
            string pythons = null
        )
        {
            if (prepare == null)
            {
                throw new ArgumentNullException(nameof(prepare));
            }

            return Add(folder, null, prepare, commands, pythons);
        }

        /// <summary>
        /// Adds a one-run tool for a single <c>.py</c> file, as PYRUN runs it: one command, <c>PYRUN</c>, whose run runs the file as
        /// <c>python file.py</c> does, in a process that ends with it. Nothing is started now.
        /// </summary>
        /// <param name="file">The <c>.py</c> file.</param>
        /// <param name="prepare">Makes the environment ready.</param>
        /// <returns>The tool, whose one command is for the registry of the drawing the file runs in.</returns>
        /// <exception cref="ArgumentNullException">Thrown when the file or the function is null.</exception>
        /// <exception cref="FileNotFoundException">Thrown when the file does not exist.</exception>
        /// <exception cref="ObjectDisposedException">Thrown when the host has been disposed.</exception>
        public CadFolderTool AddScript(string file, Prepare prepare)
        {
            if (file == null)
            {
                throw new ArgumentNullException(nameof(file));
            }

            if (prepare == null)
            {
                throw new ArgumentNullException(nameof(prepare));
            }

            if (_disposed)
            {
                throw new ObjectDisposedException(nameof(CadToolHost));
            }

            if (!File.Exists(file))
            {
                throw new FileNotFoundException("The script " + file + " does not exist.", file);
            }

            var tool = new CadFolderTool(
                this,
                Path.GetFullPath(file),
                null,
                prepare,
                true,
                new[] { new CadPluginCommandEntry("PYRUN", null, null, false) },
                null
            );
            _tools.Add(tool);
            return tool;
        }

        private CadFolderTool Add(
            string folder,
            string python,
            Prepare prepare,
            IReadOnlyList<CadPluginCommandEntry> commands,
            string pythons
        )
        {
            if (folder == null)
            {
                throw new ArgumentNullException(nameof(folder));
            }

            if (commands == null)
            {
                throw new ArgumentNullException(nameof(commands));
            }

            if (_disposed)
            {
                throw new ObjectDisposedException(nameof(CadToolHost));
            }

            if (!Directory.Exists(folder))
            {
                throw new DirectoryNotFoundException("The tool's folder " + folder + " does not exist.");
            }

            var tool = new CadFolderTool(this, Path.GetFullPath(folder), python, prepare, false, commands, pythons);
            _tools.Add(tool);
            return tool;
        }

        /// <summary>Adds the commands of every tool to the registry of a drawing, as plugins add theirs.</summary>
        /// <param name="registry">The registry.</param>
        /// <exception cref="ArgumentNullException">Thrown when the registry is null.</exception>
        public void AddCommandsTo(CadCommandRegistry registry)
        {
            if (registry == null)
            {
                throw new ArgumentNullException(nameof(registry));
            }

            foreach (CadFolderTool tool in _tools)
            {
                foreach (CadCommand command in tool.Commands)
                {
                    registry.Add(command);
                }
            }
        }

        internal void Say(string line)
        {
            Action<string> log = Log;

            if (log != null)
            {
                log(line);
            }
            else
            {
                GeometryHelperLog.Info(line);
            }
        }

        /// <summary>Ends every tool's process at once and closes the channel of the tools. Calling it again does nothing.</summary>
        public void Dispose()
        {
            if (_disposed)
            {
                return;
            }

            _disposed = true;

            foreach (CadFolderTool tool in _tools)
            {
                tool.Stop();
            }

            _server.Dispose();
        }
    }
}
