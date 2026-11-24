using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Threading;
using GeometryHelper;
using OpenCAD2D.Remote;

namespace OpenCAD2D.Wpf
{
    /// <summary>
    /// One Python tool: a folder, its commands, and the process that runs it. The commands are the ones to put in the registry of
    /// every drawing; typing one begins a run in that drawing (<see cref="CadRemoteServer.CreateRunCommand"/>), which starts the
    /// process if none is alive and tells it with a <c>run</c> message on its control pipe. What comes back on that pipe is
    /// <c>ready</c> (the commands the tool bound, once it has imported) and <c>ended</c> (the tool is done with a run).
    /// </summary>
    internal sealed class CadFolderTool
    {
        // How many of the last lines of a tool's standard error say, with the exit code, why it ended.
        private const int ErrorLines = 5;

        private readonly CadToolHost _host;
        private readonly string _python;
        private readonly string _pythons;
        private readonly string _token;

        // Touched on the thread of the drawings only.
        private Live _live;
        private readonly Dictionary<int, Timer> _watches = new Dictionary<int, Timer>();
        private bool _unloaded;

        internal CadFolderTool(
            CadToolHost host,
            string folder,
            string python,
            IReadOnlyList<string> commands,
            string pythons
        )
        {
            _host = host;
            Folder = folder;
            _python = python;
            _pythons = pythons;
            Name = System.IO.Path.GetFileName(folder.TrimEnd('\\', '/'));
            _token = host.Server.IssueToken();
            Commands = commands.Select(c => host.Server.CreateRunCommand(c, false, _token, Started)).ToList();
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

        /// <summary>Gets how many times a process has been started for the tool.</summary>
        public int Starts { get; private set; }

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

            Live live = _live ?? Start();
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

        private Live Start()
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
                        WorkingFolder = Folder,
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
            return live;
        }

        // ---- The end of a run -----------------------------------------------------------------------------------------

        /// <summary>A run ended, in the app, inside the session's Finish: where Esc ended it the tool is told, and given 5 seconds to leave it.</summary>
        private void Ended(Live live, CadRemoteToolRun run)
        {
            live.Runs.Remove(run.Id);

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
            _host.Dispatcher.Invoke(() =>
            {
                if (_live == live)
                {
                    _live = null;
                }

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
                    var commands = message.GetArray("commands").OfType<string>().ToList();
                    _host.Dispatcher.Invoke(() =>
                    {
                        InterpreterId = (int)message.GetLong("pid");
                        BoundCommands = commands;

                        foreach (
                            string unknown in commands
                                .Except(Commands.Select(c => c.Name))
                                .OrderBy(c => c, StringComparer.Ordinal)
                        )
                        {
                            _host.Say(
                                Name + ": the command " + unknown + " is defined with @command but not declared."
                            );
                        }
                    });
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
