using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using ModelContextProtocol;
using OpenCAD2D.Remote;

namespace OpenCAD2D.Mcp
{
    /// <summary>
    /// The state of one server: the options it was started with, the drawing the tools work on, and the rules of safety
    /// every tool goes through. One tool runs at a time, so a drawing is never touched by two calls at once.
    /// </summary>
    internal sealed class McpSession : IDisposable
    {
        private readonly object _gate = new object();
        private readonly McpFailures _failures = new McpFailures();
        private IDrawingBackend _file;
        private CadApplication _app;
        private CadRemoteDocument _chosen;

        /// <summary>Makes the session; call <see cref="Start"/> to open the file the options name.</summary>
        public McpSession(McpOptions options)
        {
            Options = options ?? throw new ArgumentNullException(nameof(options));
        }

        /// <summary>Gets the options the server was started with.</summary>
        public McpOptions Options { get; }

        /// <summary>Opens the file of <c>--file</c>, where there is one; a file that cannot be opened throws.</summary>
        public void Start()
        {
            if (Options.File != null)
            {
                _file = LocalBackend.Open(Options.File);
            }
        }

        /// <summary>
        /// Runs one tool: refuses it first where it writes and the server is read-only, then gives it the drawing, and turns what
        /// goes wrong (a handle the drawing has not, a locked layer, an app that is gone) into the message the agent reads.
        /// </summary>
        /// <typeparam name="T">The type of the answer.</typeparam>
        /// <param name="tool">The name of the tool, for the refusal.</param>
        /// <param name="writes">Whether the tool changes the drawing or a file.</param>
        /// <param name="work">The work, given the drawing.</param>
        /// <returns>What the work gives.</returns>
        /// <exception cref="McpException">Thrown for a refusal or a failure, with the message for the agent.</exception>
        public T Run<T>(string tool, bool writes, Func<IDrawingBackend, T> work)
        {
            lock (_gate)
            {
                if (writes && Options.ReadOnly)
                {
                    throw new McpException(
                        tool
                            + " is refused: this server was started with --read-only, so no tool that writes is allowed."
                    );
                }

                return Guarded(tool, () => work(Backend));
            }
        }

        /// <summary>Runs work that needs no open drawing (opening one, making one), under the same rules as <see cref="Run"/>.</summary>
        /// <typeparam name="T">The type of the answer.</typeparam>
        /// <param name="work">The work.</param>
        /// <returns>What the work gives.</returns>
        /// <exception cref="McpException">Thrown for a failure, with the message for the agent.</exception>
        public T RunWithout<T>(Func<T> work)
        {
            lock (_gate)
            {
                return Guarded(null, work);
            }
        }

        private T Guarded<T>(string tool, Func<T> work)
        {
            try
            {
                return work();
            }
            catch (McpException)
            {
                throw;
            }
            catch (Exception exception)
            {
                // A refusal the agent can act on is its answer; anything else is also written for whoever runs the server.
                bool refusal =
                    exception is ArgumentException
                    || exception is InvalidOperationException
                    || exception is CadRemoteException;

                if (!refusal)
                {
                    Console.Error.WriteLine(exception);
                }

                _failures.Add(tool, exception, refusal);
                throw new McpException(exception.Message, exception);
            }
        }

        /// <summary>
        /// Gets the app of an attached server, found and connected to on first use, for a tool that asks the app about itself and not
        /// about a drawing; null for a server that works on a file.
        /// </summary>
        /// <exception cref="McpException">Thrown when the app is not there.</exception>
        public CadApplication Application => Options.Attach ? App() : null;

        /// <summary>Gets the tool calls this server could not do, the newest first, as <c>last_errors</c> tells them.</summary>
        /// <param name="count">How many to give.</param>
        /// <returns>The failures.</returns>
        public IReadOnlyList<McpFailure> Failures(int count) => _failures.Newest(count);

        /// <summary>Refuses a tool that types free command, LISP or script text unless the server was started with <c>--allow-scripts</c>.</summary>
        /// <param name="tool">The name of the tool.</param>
        /// <exception cref="McpException">Thrown when scripts are not allowed.</exception>
        public void RequireScripts(string tool)
        {
            if (!Options.AllowScripts)
            {
                throw new McpException(
                    tool
                        + " is refused: it types free command, AutoLISP or script text, which this server only does when it was started with --allow-scripts."
                );
            }
        }

        /// <summary>Gets the drawing the tools work on: the open file, or the chosen or active drawing of the app.</summary>
        /// <exception cref="McpException">Thrown when none is open, or the app is not there.</exception>
        public IDrawingBackend Backend
        {
            get
            {
                if (!Options.Attach)
                {
                    return _file
                        ?? throw new McpException(
                            "No drawing is open: call open_drawing with a path, or new_drawing, first."
                        );
                }

                // Walking the app's drawings asks it again: a drawing the user closed is dropped, and the facts of the rest
                // (modified, active) are as the app has them now.
                CadApplication app = App();
                CadRemoteDocument chosen = _chosen;
                List<CadRemoteDocument> open = app.Documents.ToList();

                if (chosen != null && !open.Any(d => d.Id == chosen.Id))
                {
                    _chosen = chosen = null;
                }

                CadRemoteDocument document = chosen ?? open.FirstOrDefault(d => d.IsActive);
                return new RemoteBackend(
                    document ?? throw new McpException("The app has no drawing open: call open_drawing or new_drawing.")
                );
            }
        }

        /// <summary>Opens a drawing file as the drawing the tools work on: in this process, or in the app for <c>--attach</c>.</summary>
        /// <param name="path">The DXF or DWG file.</param>
        /// <param name="discard">Whether the changes the open drawing has may be thrown away.</param>
        /// <exception cref="ArgumentException">Thrown when the open drawing has changes and they are not to be discarded, or the file is not there.</exception>
        public void Open(string path, bool discard)
        {
            if (string.IsNullOrWhiteSpace(path))
            {
                throw new ArgumentException("open_drawing needs the path of a DXF or DWG file.");
            }

            string full = Path.GetFullPath(path);

            if (!File.Exists(full))
            {
                throw new ArgumentException("There is no file " + full + ".");
            }

            if (Options.Attach)
            {
                _chosen = App().Open(full);
                return;
            }

            Replace(LocalBackend.Open(full), discard);
        }

        /// <summary>Makes a blank drawing as the drawing the tools work on: in this process, or in the app for <c>--attach</c>.</summary>
        /// <param name="discard">Whether the changes the open drawing has may be thrown away.</param>
        public void New(bool discard)
        {
            if (Options.Attach)
            {
                _chosen = App().New();
                return;
            }

            Replace(LocalBackend.New(), discard);
        }

        private void Replace(LocalBackend next, bool discard)
        {
            if (_file != null && _file.IsModified && !discard)
            {
                throw new ArgumentException(
                    "The open drawing "
                        + _file.Name
                        + " has changes that are not saved: call save first, or pass discard true to throw them away."
                );
            }

            _file?.Dispose();
            _file = next;
        }

        private CadApplication App()
        {
            if (_app != null)
            {
                return _app;
            }

            IReadOnlyList<CadRunningApp> running =
                Options.RemoteFolder == null
                    ? CadApplication.GetRunning()
                    : CadApplication.GetRunning(Options.RemoteFolder);
            CadRunningApp chosen =
                Options.ProcessId == 0
                    ? running.FirstOrDefault()
                    : running.FirstOrDefault(a => a.ProcessId == Options.ProcessId);

            if (chosen == null)
            {
                throw new McpException(
                    Options.ProcessId == 0
                        ? "No OpenCAD2D is running. Start the app, check that it allows other programs to connect, and call the tool again."
                        : "No OpenCAD2D with the process id " + Options.ProcessId + " is running."
                );
            }

            _app = new CadApplication(chosen);
            return _app;
        }

        /// <summary>Closes the connection to the app and the open file; the app keeps its drawings.</summary>
        public void Dispose()
        {
            _file?.Dispose();
            _app?.Dispose();
        }
    }
}
