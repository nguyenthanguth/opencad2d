using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using GeometryHelper;

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
    /// Make it, and call <see cref="Add"/> and <see cref="Dispose"/>, on the thread of the drawings; the process's pipes are read
    /// on threads of their own and everything they find is handed to the dispatcher. Nothing here waits for a tool on that thread.
    /// </remarks>
    internal sealed class CadToolHost : IDisposable
    {
        private readonly ICadRemoteDispatcher _dispatcher;
        private readonly CadRemoteServer _server;
        private readonly string _library;
        private readonly List<CadFolderTool> _tools = new List<CadFolderTool>();
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
            if (folder == null)
            {
                throw new ArgumentNullException(nameof(folder));
            }

            if (python == null)
            {
                throw new ArgumentNullException(nameof(python));
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

            var tool = new CadFolderTool(this, Path.GetFullPath(folder), python, commands.ToList(), pythons);
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
