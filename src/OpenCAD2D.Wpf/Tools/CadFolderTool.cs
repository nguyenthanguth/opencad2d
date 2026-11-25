using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using GeometryHelper;
using OpenCAD2D.Plugins;
using OpenCAD2D.Remote;

namespace OpenCAD2D.Wpf
{
    /// <summary>
    /// One Python tool: a folder, its commands, and the process that runs it. The commands are the ones to put in the registry of
    /// every drawing; typing one begins a run in that drawing (<see cref="CadRemoteServer.CreateRunCommand"/>), which starts the
    /// process if none is alive and tells it with a <c>run</c> message on its control pipe. What comes back on that pipe is
    /// <c>ready</c> (the commands and snaps the tool bound, once it has imported), <c>ended</c> (the tool is done with a run) and
    /// <c>snapped</c> (the answer to a question of <see cref="Snaps"/>).
    /// </summary>
    internal sealed class CadFolderTool
    {
        // How many of the last lines of a tool's standard error say, with the exit code, why it ended.
        private const int ErrorLines = 5;

        private readonly CadToolHost _host;
        private readonly CadToolHost.Prepare _prepare;
        private readonly bool _isScript;
        private readonly string _pythons;
        private readonly string _token;
        private string _python;

        // The process, made on the thread of the drawings, or on a thread of its own where a snap starts it (see EnsureStartedQuietly):
        // starting is under _startLock. Everything else of the tool is touched on the thread of the drawings only.
        private volatile Live _live;
        private readonly object _startLock = new object();
        private readonly Dictionary<int, Timer> _watches = new Dictionary<int, Timer>();
        private volatile bool _unloaded;

        // A snap's start of the process (EnsureStartedQuietly): whether one is under way, whether the environment was not ready for it
        // (the next command makes it so), and the time before which none is made after the process ended.
        private bool _quietBusy;
        private bool _quietRefused;
        private long _quietNotBefore;

        internal CadFolderTool(
            CadToolHost host,
            string folder,
            string python,
            CadToolHost.Prepare prepare,
            bool isScript,
            IReadOnlyList<CadPluginCommandEntry> commands,
            string pythons
        )
        {
            _host = host;
            Folder = folder;
            _python = python;
            _prepare = prepare;
            _isScript = isScript;
            _pythons = pythons;
            Name = System.IO.Path.GetFileName(folder.TrimEnd('\\', '/'));
            _token = host.Server.IssueToken();
            Commands = commands
                .Select(c => host.Server.CreateRunCommand(c.Name, c.UsesSelection, _token, Started))
                .ToList();
            Snaps = new CadToolSnaps(
                Name,
                SendToProcess,
                action => host.Post(action),
                EnsureStartedQuietly,
                documents => host.SnapsAnswered?.Invoke(documents)
            );
        }

        /// <summary>Gets the name of the tool: the name of its folder.</summary>
        public string Name { get; }

        /// <summary>Gets the full path of the tool's folder.</summary>
        public string Folder { get; }

        /// <summary>Gets the commands of the tool, to put in the registry of every drawing.</summary>
        public IReadOnlyList<CadCommand> Commands { get; }

        /// <summary>Gets whether the tool's process is alive.</summary>
        public bool IsRunning => _live != null;

        /// <summary>Gets the process id of the program that was started for the tool, the launcher of its venv; 0 while there is none.</summary>
        public int ProcessId => _live?.Process.Id ?? 0;

        /// <summary>Gets the process id the interpreter says it has, once it is ready; 0 before.</summary>
        public int InterpreterId { get; private set; }

        /// <summary>Gets the commands the tool bound with <c>@command</c>, as it told the app when it was ready; none before.</summary>
        public IReadOnlyList<string> BoundCommands { get; private set; } = Array.Empty<string>();

        /// <summary>Gets the snaps the tool bound with <c>@snap</c>, in capitals, as it told the app when it was ready; none before.</summary>
        public IReadOnlyList<string> BoundSnaps { get; private set; } = Array.Empty<string>();

        /// <summary>Gets the object snaps of the tool: the finders its plugin.json's snaps are given, and the points the process answered.</summary>
        internal CadToolSnaps Snaps { get; }

        /// <summary>Gets how many times a process has been started for the tool.</summary>
        public int Starts { get; private set; }

        /// <summary>Gets how many runs of the tool's commands ended any way but well: with an error, cancelled, or never begun.</summary>
        internal int RunsNotCompleted { get; private set; }

        /// <summary>The state of one process of the tool.</summary>
        private sealed class Live
        {
            public CadToolProcess Process;
            public readonly Dictionary<int, CadRemoteToolRun> Runs = new Dictionary<int, CadRemoteToolRun>();

            // The session a line of the standard output goes to: that of the last run.
            public CadCommandSession Session;
            public readonly Queue<string> Errors = new Queue<string>();
        }

        // ---- A command typed ----------------------------------------------------------------------------------------

        private void Started(CadRemoteToolRun run)
        {
            if (_unloaded)
            {
                throw new InvalidOperationException("The tool " + Name + " is unloaded.");
            }

            run.Ended += (sender, e) =>
            {
                if (run.Outcome != CadRemoteToolRunOutcome.Completed)
                {
                    RunsNotCompleted++;
                }
            };

            if (_live == null && _prepare != null)
            {
                Preparing(run);
                return;
            }

            Dispatch(_live ?? Start(), run);
        }

        /// <summary>Tells the process of a run: it is the run's, and the last one's session gets the lines the process prints.</summary>
        private void Dispatch(Live live, CadRemoteToolRun run)
        {
            live.Runs[run.Id] = run;
            live.Session = run.Session;
            run.Ended += (sender, e) => Ended(live, run);
            live.Process.Send(
                RemoteJson.Write(
                    new JsonObject()
                        .Set("op", "run")
                        .Set("run", (long)run.Id)
                        .Set("command", run.Name)
                        .Set("document", (long)run.DocumentId)
                )
            );
        }

        /// <summary>
        /// The tool has no process: its environment is made ready on a thread of its own (pip may ask a question of the run and
        /// take seconds), while the run waits at its prompt, and the process is started where that ends, unless Esc ended the run
        /// or the tool was unloaded meanwhile.
        /// </summary>
        private void Preparing(CadRemoteToolRun run)
        {
            Task.Run(() =>
            {
                PythonEnvironment environment;

                try
                {
                    environment = _prepare(run, () => _host.Dispatcher.Invoke(StopForSwap));
                }
                catch (Exception ex) when (!(ex is OutOfMemoryException))
                {
                    GeometryHelperLog.Warn("CadFolderTool: the environment of " + Name + " could not be made.", ex);
                    environment = PythonEnvironment.Failed(
                        Folder,
                        Name + " was not started: its environment could not be made (" + ex.Message + ")."
                    );
                }

                try
                {
                    _host.Dispatcher.Invoke(() => Prepared(run, environment));
                }
                catch (Exception ex) when (!(ex is OutOfMemoryException))
                {
                    GeometryHelperLog.Warn("CadFolderTool: the start of " + Name + " failed.", ex);
                }
            });
        }

        private void Prepared(CadRemoteToolRun run, PythonEnvironment environment)
        {
            // Esc ended the run, or the tool was unloaded, while its environment was being made: nothing starts.
            if (run.IsEnded || _unloaded)
            {
                return;
            }

            if (!environment.IsReady)
            {
                Failed(run, environment.Error);
                return;
            }

            _python = environment.Python;
            _quietRefused = false;

            // A change of the tool's packages that failed leaves it on the venv it had: the lines say so, on the command line and in the log.
            if (environment.Failure != null)
            {
                foreach (string line in environment.Failure.Replace("\r", string.Empty).Split('\n'))
                {
                    run.Session.Write(line);
                    _host.Say(Name + ": " + line);
                }
            }

            try
            {
                Dispatch(_live ?? Start(), run);
            }
            catch (Exception ex) when (!(ex is OutOfMemoryException))
            {
                Failed(run, ex.Message);
            }
        }

        /// <summary>Ends a run the process never began: the lines are written, the changes (there are none) taken back.</summary>
        private void Failed(CadRemoteToolRun run, string text)
        {
            foreach (string line in text.Replace("\r", string.Empty).Split('\n'))
            {
                run.Session.Write(line);
            }

            run.Error = text;
            run.Finish(CadRemoteToolRunOutcome.Failed);

            if (!run.Session.Resume(run.Command))
            {
                run.Session.Escape();
            }
        }

        /// <summary>The venv is about to be replaced: the tool's process, which holds its files, is ended; the next command starts another.</summary>
        private void StopForSwap()
        {
            Live live = _live;

            if (live != null)
            {
                _live = null;
                live.Process.Kill();
            }
        }

        private Live Start()
        {
            lock (_startLock)
            {
                return _live ?? Make();
            }
        }

        private Live Make()
        {
            var live = new Live();
            var environment = new Dictionary<string, string>
            {
                ["PYTHONUTF8"] = "1",
                ["PYTHONUNBUFFERED"] = "1",
                ["OPENCAD2D_RUNTIME"] = "coreclr",
                ["OPENCAD2D_LIB"] = _host.Library,
                ["OPENCAD2D_TOOL_PIPE"] = _host.Server.PipeName,
                ["OPENCAD2D_TOOL_TOKEN"] = _token,
            };

            if (_pythons != null)
            {
                environment["OPENCAD2D_PYTHONS"] = _pythons;
            }

            try
            {
                live.Process = CadToolProcess.Start(
                    new CadToolStart
                    {
                        Program = _python,
                        // -I: the tool is isolated from the user's Python settings; -X utf8 and -u are what PYTHONUTF8 and
                        // PYTHONUNBUFFERED say, which -I would ignore.
                        Arguments = new[] { "-I", "-X", "utf8", "-u", "-m", "opencad2d.tool", Folder },
                        WorkingFolder = _isScript ? System.IO.Path.GetDirectoryName(Folder) : Folder,
                        Environment = environment,
                    },
                    line => Output(live, line),
                    line => Error(live, line),
                    line => Control(live, line),
                    code => Exited(live, code)
                );
            }
            catch (System.IO.IOException ex)
            {
                throw new InvalidOperationException("The tool " + Name + " could not be started: " + ex.Message, ex);
            }

            _live = live;
            Starts++;
            Snaps.ProcessStarted();
            return live;
        }

        /// <summary>Sends a line to the process on its control pipe; false where the tool has none.</summary>
        private bool SendToProcess(string line)
        {
            Live live = _live;

            if (live == null)
            {
                return false;
            }

            live.Process.Send(line);
            return true;
        }

        /// <summary>
        /// A snap asked about an entity and the tool has no process: its environment is made ready on a thread of its own with no
        /// question and no pip (<see cref="PythonEnvironments.Prepare"/> with <c>ask: false</c>), and the process started there.
        /// Nothing is done where the tool is unloaded, a start is under way, the environment was not ready for the last try (a
        /// command of the tool makes it so), or the last process ended less than 5 seconds ago.
        /// </summary>
        private void EnsureStartedQuietly()
        {
            lock (_startLock)
            {
                if (
                    _unloaded
                    || _live != null
                    || _quietBusy
                    || _quietRefused
                    || Stopwatch.GetTimestamp() < _quietNotBefore
                )
                {
                    return;
                }

                _quietBusy = true;
            }

            Task.Run(() =>
            {
                try
                {
                    if (_prepare != null)
                    {
                        // No run is waiting to be asked anything, and no process runs that the venv could be swapped under.
                        PythonEnvironment environment = _prepare(null, () => { });

                        if (!environment.IsReady)
                        {
                            lock (_startLock)
                            {
                                _quietRefused = true;
                            }

                            return;
                        }

                        _python = environment.Python;
                    }

                    lock (_startLock)
                    {
                        if (!_unloaded && _live == null)
                        {
                            Make();
                        }
                    }
                }
                catch (Exception ex) when (!(ex is OutOfMemoryException))
                {
                    GeometryHelperLog.Warn("CadFolderTool: " + Name + " could not be started for a snap.", ex);

                    lock (_startLock)
                    {
                        _quietRefused = true;
                    }
                }
                finally
                {
                    lock (_startLock)
                    {
                        _quietBusy = false;
                    }
                }
            });
        }

        /// <summary>Tells the server which settings the tool declares, so that the tool reads and sets them over its channel (<c>toolSetting</c>, <c>setToolSetting</c>).</summary>
        /// <param name="settings">The settings, made over the settings store.</param>
        internal void DeclareSettings(IReadOnlyList<CadPluginSetting> settings) =>
            _host.Server.SetToolSettings(_token, Name, settings);

        // ---- The end of a run -----------------------------------------------------------------------------------------

        /// <summary>A run ended, in the app, inside the session's Finish: where Esc ended it the tool is told, and given 5 seconds to leave it.</summary>
        private void Ended(Live live, CadRemoteToolRun run)
        {
            live.Runs.Remove(run.Id);

            // A script ran once: its process is not kept for a next command.
            if (_isScript && run.Outcome != CadRemoteToolRunOutcome.Cancelled)
            {
                Unload();
                return;
            }

            if (run.Outcome != CadRemoteToolRunOutcome.Cancelled || _live != live || live.Process.WasKilled)
            {
                return;
            }

            live.Process.Send(RemoteJson.Write(new JsonObject().Set("op", "cancel").Set("run", (long)run.Id)));
            Watch(live, run.Id, () =>
                Stop(live, run.Session, run.Name + ": the tool " + Name + " did not stop within 5 s and was ended.")
            );
        }

        private void Watch(Live live, int run, Action stop)
        {
            _watches[run] = new Timer(
                _ =>
                {
                    try
                    {
                        _host.Dispatcher.Invoke(() =>
                        {
                            if (_watches.TryGetValue(run, out Timer timer))
                            {
                                _watches.Remove(run);
                                timer.Dispose();

                                if (_live == live)
                                {
                                    stop();
                                }
                            }
                        });
                    }
                    catch (Exception ex) when (!(ex is OutOfMemoryException))
                    {
                        GeometryHelperLog.Warn("CadFolderTool: the stop of " + Name + " failed.", ex);
                    }
                },
                null,
                _host.StopAfter,
                Timeout.InfiniteTimeSpan
            );
        }

        /// <summary>Ends the process: the next command of the tool starts another.</summary>
        private void Stop(Live live, CadCommandSession session, string line)
        {
            _live = null;
            live.Process.Kill();

            if (session != null)
            {
                session.Write(line);
            }
            else
            {
                _host.Say(line);
            }
        }

        /// <summary>The process ended, and its pipes were read to the end: its runs still open are cancelled, with what it said of why.</summary>
        private void Exited(Live live, int code)
        {
            // What was asked of the process will not be answered, and a point typed that waits for it stops waiting.
            Snaps.Clear();

            lock (_startLock)
            {
                _quietNotBefore = Stopwatch.GetTimestamp() + 5 * Stopwatch.Frequency;
            }

            _host.Dispatcher.Invoke(() =>
            {
                if (_live == live)
                {
                    _live = null;
                }

                Snaps.Clear();

                foreach (CadRemoteToolRun run in live.Runs.Values.OrderBy(r => r.Id).ToList())
                {
                    // Taken back first: the lines are the history's, after what Esc wrote.
                    run.Cancel();

                    if (!live.Process.WasKilled)
                    {
                        run.Session.Write(
                            run.Name
                                + ": the tool "
                                + Name
                                + " ended unexpectedly (exit code "
                                + code.ToString(CultureInfo.InvariantCulture)
                                + ")."
                        );

                        foreach (string line in LastErrors(live))
                        {
                            run.Session.Write(line);
                        }
                    }
                }

                live.Runs.Clear();
            });
        }

        private static IReadOnlyList<string> LastErrors(Live live)
        {
            lock (live.Errors)
            {
                return live.Errors.ToList();
            }
        }

        // ---- What the process says ------------------------------------------------------------------------------------

        private void Output(Live live, string line)
        {
            _host.Dispatcher.Invoke(() =>
            {
                if (live.Session != null)
                {
                    live.Session.Write(line);
                }
                else
                {
                    _host.Say(Name + ": " + line);
                }
            });
        }

        private void Error(Live live, string line)
        {
            lock (live.Errors)
            {
                live.Errors.Enqueue(line);

                while (live.Errors.Count > ErrorLines)
                {
                    live.Errors.Dequeue();
                }
            }

            _host.Say(Name + ": " + line);
        }

        private void Control(Live live, string line)
        {
            if (!(RemoteJson.Parse(line) is JsonObject message))
            {
                return;
            }

            switch (message.GetString("op"))
            {
                case "ready":
                    // Not handed to the window's thread: a point typed after a snap's keyword waits there for the tool's first
                    // answer, which comes after this line on this thread.
                    var commands = message.GetArray("commands").OfType<string>().ToList();
                    var snaps = message.GetArray("snaps").OfType<string>().ToList();
                    InterpreterId = (int)message.GetLong("pid");
                    BoundCommands = commands;
                    BoundSnaps = snaps;

                    foreach (
                        string unknown in commands
                            .Except(Commands.Select(c => c.Name))
                            .OrderBy(c => c, StringComparer.Ordinal)
                    )
                    {
                        _host.Say(Name + ": the command " + unknown + " is defined with @command but not declared.");
                    }

                    foreach (string unknown in snaps.Except(Snaps.Declared).OrderBy(c => c, StringComparer.Ordinal))
                    {
                        _host.Say(Name + ": the snap " + unknown + " is defined with @snap but not declared.");
                    }

                    break;
                case "snapped":
                    Snaps.Answered(message);
                    break;
                case "ended":
                    int run = (int)message.GetLong("run");
                    _host.Dispatcher.Invoke(() =>
                    {
                        if (_watches.TryGetValue(run, out Timer timer))
                        {
                            _watches.Remove(run);
                            timer.Dispose();
                        }
                    });
                    break;
            }
        }

        // ---- Leaving --------------------------------------------------------------------------------------------------

        /// <summary>
        /// Unloads the tool: its process is told to leave (its <c>@on_unload</c> functions run) and ended after 5 seconds where it
        /// does not, its runs are cancelled, and its commands run no more. Nothing waits for the process.
        /// </summary>
        public void Unload()
        {
            if (_unloaded)
            {
                return;
            }

            _unloaded = true;
            Snaps.Close();
            _host.Server.RevokeToken(_token);
            Live live = _live;

            if (live != null)
            {
                live.Process.Send(RemoteJson.Write(new JsonObject().Set("op", "unload")));
                Watch(live, 0, () => Stop(live, null, "The tool " + Name + " did not leave within 5 s and was ended."));
            }
        }

        /// <summary>Ends the process at once and takes the token back: the app is closing.</summary>
        internal void Stop()
        {
            _unloaded = true;
            Snaps.Close();
            _host.Server.RevokeToken(_token);

            foreach (Timer timer in _watches.Values)
            {
                timer.Dispose();
            }

            _watches.Clear();
            _live?.Process.Kill();
            _live = null;
        }
    }
}
