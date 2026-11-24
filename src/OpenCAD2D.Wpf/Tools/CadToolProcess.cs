using System;
using System.Collections;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.ComponentModel;
using System.Globalization;
using System.IO;
using System.IO.Pipes;
using System.Linq;
using System.Runtime.InteropServices;
using System.Text;
using System.Threading;
using GeometryHelper;

namespace OpenCAD2D.Wpf
{
    /// <summary>
    /// What a tool's process is started with: the program and its arguments, the folder it works in, and the environment it
    /// has besides the app's own.
    /// </summary>
    internal sealed class CadToolStart
    {
        /// <summary>Gets or sets the full path of the program, a <c>python.exe</c>.</summary>
        public string Program { get; set; }

        /// <summary>Gets or sets the arguments before the two handles of the control pipes, which the process gets last.</summary>
        public IReadOnlyList<string> Arguments { get; set; } = Array.Empty<string>();

        /// <summary>Gets or sets the folder the process works in.</summary>
        public string WorkingFolder { get; set; }

        /// <summary>Gets or sets the variables of the environment the process has besides the app's own, which these replace.</summary>
        public IReadOnlyDictionary<string, string> Environment { get; set; } = new Dictionary<string, string>();
    }

    /// <summary>
    /// A tool's process, started the way an app that must never be taken down by a tool starts one: suspended, put in a job
    /// object that kills everything in it when the app lets it go (so a venv's launcher and the interpreter it starts die
    /// with the app, even when the app is killed), and only then let run. It is given two anonymous pipes of its own to talk
    /// to the app on (lines of text), and its standard output and error are read here, a line at a time. Nothing the process
    /// does holds up the thread that started it: each pipe has a thread, and what comes is handed to the callbacks on those.
    /// </summary>
    internal sealed class CadToolProcess : IDisposable
    {
        // How long the readers of a process that has ended are waited for, to give the lines it left; milliseconds.
        private const int DrainWait = 2000;

        private readonly Action<string> _onOutput;
        private readonly Action<string> _onError;
        private readonly Action<string> _onControl;
        private readonly Action<int> _onExit;
        private readonly BlockingCollection<string> _outgoing = new BlockingCollection<string>();
        private readonly AnonymousPipeServerStream[] _pipes;
        private readonly IntPtr _process;
        private IntPtr _job;
        private int _killed;
        private int _disposed;

        private CadToolProcess(
            int id,
            IntPtr process,
            IntPtr job,
            AnonymousPipeServerStream[] pipes,
            Action<string> onOutput,
            Action<string> onError,
            Action<string> onControl,
            Action<int> onExit
        )
        {
            Id = id;
            _process = process;
            _job = job;
            _pipes = pipes;
            _onOutput = onOutput;
            _onError = onError;
            _onControl = onControl;
            _onExit = onExit;
        }

        /// <summary>Gets the process id of the program that was started: the launcher of a venv, whose child is the interpreter.</summary>
        public int Id { get; }

        /// <summary>Gets whether the app ended the process (<see cref="Kill"/>, or the app closing), so that its end is no crash.</summary>
        public bool WasKilled => Volatile.Read(ref _killed) != 0;

        /// <summary>
        /// Starts a process. The callbacks are called on threads of their own, in the order of what each pipe gave: a line of the
        /// standard output, a line of the standard error, a line of the control pipe, and, once the process has ended and its
        /// pipes are read to the end, its exit code. The last of them is called exactly once.
        /// </summary>
        /// <param name="start">What to start.</param>
        /// <param name="onOutput">Called with each line the process wrote on its standard output.</param>
        /// <param name="onError">Called with each line the process wrote on its standard error.</param>
        /// <param name="onControl">Called with each line the process wrote on its control pipe.</param>
        /// <param name="onExit">Called with the exit code once the process has ended.</param>
        /// <returns>The process, running.</returns>
        /// <exception cref="IOException">Thrown when the process cannot be started or cannot be put in its job; the message says why.</exception>
        public static CadToolProcess Start(
            CadToolStart start,
            Action<string> onOutput,
            Action<string> onError,
            Action<string> onControl,
            Action<int> onExit
        )
        {
            // Two pipes the process talks on, one for each way, and its three standard streams; the server end of each is the app's.
            var toTool = new AnonymousPipeServerStream(PipeDirection.Out, HandleInheritability.Inheritable);
            var fromTool = new AnonymousPipeServerStream(PipeDirection.In, HandleInheritability.Inheritable);
            var input = new AnonymousPipeServerStream(PipeDirection.Out, HandleInheritability.Inheritable);
            var output = new AnonymousPipeServerStream(PipeDirection.In, HandleInheritability.Inheritable);
            var error = new AnonymousPipeServerStream(PipeDirection.In, HandleInheritability.Inheritable);
            var pipes = new[] { toTool, fromTool, input, output, error };
            IntPtr job = IntPtr.Zero;
            IntPtr process = IntPtr.Zero;

            try
            {
                var arguments = new List<string>(start.Arguments)
                {
                    toTool.GetClientHandleAsString(),
                    fromTool.GetClientHandleAsString(),
                };
                job = MakeJob();
                CadToolNative.ProcessInformation created = Create(
                    start,
                    arguments,
                    new[]
                    {
                        input.ClientSafePipeHandle.DangerousGetHandle(),
                        output.ClientSafePipeHandle.DangerousGetHandle(),
                        error.ClientSafePipeHandle.DangerousGetHandle(),
                        toTool.ClientSafePipeHandle.DangerousGetHandle(),
                        fromTool.ClientSafePipeHandle.DangerousGetHandle(),
                    }
                );
                process = created.hProcess;

                try
                {
                    if (!CadToolNative.AssignProcessToJobObject(job, process))
                    {
                        throw new IOException("The process could not be put in a job: " + new Win32Exception().Message);
                    }
                }
                catch
                {
                    CadToolNative.TerminateProcess(process, 1);
                    CadToolNative.CloseHandle(created.hThread);
                    throw;
                }

                CadToolNative.ResumeThread(created.hThread);
                CadToolNative.CloseHandle(created.hThread);

                // The client ends belong to the process now: the app's copies are closed, so that the end of the process is the
                // end of its pipes.
                foreach (AnonymousPipeServerStream pipe in pipes)
                {
                    pipe.DisposeLocalCopyOfClientHandle();
                }

                var tool = new CadToolProcess(
                    created.dwProcessId,
                    process,
                    job,
                    pipes,
                    onOutput,
                    onError,
                    onControl,
                    onExit
                );
                tool.Run(input, toTool, fromTool, output, error);
                return tool;
            }
            catch
            {
                if (job != IntPtr.Zero)
                {
                    CadToolNative.CloseHandle(job);
                }

                if (process != IntPtr.Zero)
                {
                    CadToolNative.CloseHandle(process);
                }

                foreach (AnonymousPipeServerStream pipe in pipes)
                {
                    pipe.Dispose();
                }

                throw;
            }
        }

        /// <summary>Starts the threads that read and write the pipes and watch for the end of the process.</summary>
        private void Run(
            AnonymousPipeServerStream input,
            AnonymousPipeServerStream toTool,
            AnonymousPipeServerStream fromTool,
            AnonymousPipeServerStream output,
            AnonymousPipeServerStream error
        )
        {
            // Nothing is written to the tool's standard input: it reads the end of it.
            input.Dispose();
            Thread outputs = Reader("output", output, _onOutput);
            Thread errors = Reader("error", error, _onError);
            Thread controls = Reader("control", fromTool, _onControl);
            Background("writer", () => Write(toTool));
            Background("exit", () => Wait(outputs, errors, controls));
        }

        private Thread Reader(string name, Stream stream, Action<string> onLine)
        {
            return Background(name, () =>
            {
                try
                {
                    using (var reader = new StreamReader(stream, new UTF8Encoding(false)))
                    {
                        string line;

                        while ((line = reader.ReadLine()) != null)
                        {
                            Guarded(onLine, line);
                        }
                    }
                }
                catch (Exception ex) when (ex is IOException || ex is ObjectDisposedException)
                {
                    GeometryHelperLog.Debug("CadToolProcess: a pipe of process " + Id + " ended: " + ex.Message);
                }
            });
        }

        private void Write(Stream stream)
        {
            try
            {
                foreach (string line in _outgoing.GetConsumingEnumerable())
                {
                    byte[] bytes = new UTF8Encoding(false).GetBytes(line + "\n");
                    stream.Write(bytes, 0, bytes.Length);
                    stream.Flush();
                }
            }
            catch (Exception ex) when (ex is IOException || ex is ObjectDisposedException)
            {
                // The process has gone: what is still to be sent has nobody to read it.
                GeometryHelperLog.Debug("CadToolProcess: the control pipe of " + Id + " is closed: " + ex.Message);
            }
            finally
            {
                stream.Dispose();
            }
        }

        private void Wait(Thread outputs, Thread errors, Thread controls)
        {
            CadToolNative.WaitForSingleObject(_process, CadToolNative.Infinite);
            CadToolNative.GetExitCodeProcess(_process, out uint code);
            _outgoing.CompleteAdding();

            // Whatever the process left running goes with it, so that every end of its pipes is closed and read to the end; its
            // last lines are read before its end is told, for they say why it ended.
            CloseJob();

            foreach (Thread reader in new[] { outputs, errors, controls })
            {
                reader.Join(DrainWait);
            }

            CadToolNative.CloseHandle(_process);

            foreach (AnonymousPipeServerStream pipe in _pipes)
            {
                pipe.Dispose();
            }

            Guarded(_onExit, unchecked((int)code));
        }

        private static void Guarded<T>(Action<T> action, T value)
        {
            try
            {
                action(value);
            }
            catch (Exception ex) when (!(ex is OutOfMemoryException))
            {
                // What a callback does never takes a thread of the pipes, or the app, down with it.
                GeometryHelperLog.Warn("CadToolProcess: a callback failed.", ex);
            }
        }

        private static Thread Background(string name, ThreadStart work)
        {
            var thread = new Thread(work) { IsBackground = true, Name = "CadToolProcess " + name };
            thread.Start();
            return thread;
        }

        /// <summary>Sends a line to the process on its control pipe, without waiting for it to be read.</summary>
        /// <param name="line">The line, with no end of line in it.</param>
        public void Send(string line)
        {
            try
            {
                _outgoing.Add(line);
            }
            catch (InvalidOperationException)
            {
                // The process has ended: nobody is left to read it.
            }
        }

        /// <summary>
        /// Ends the process and everything it started, at once, as closing its job does. Does nothing once the process has ended
        /// or been ended.
        /// </summary>
        public void Kill()
        {
            if (Interlocked.Exchange(ref _killed, 1) != 0)
            {
                return;
            }

            CloseJob();
        }

        private void CloseJob()
        {
            IntPtr job = Interlocked.Exchange(ref _job, IntPtr.Zero);

            if (job != IntPtr.Zero)
            {
                CadToolNative.CloseHandle(job);
            }
        }

        /// <summary>Ends the process, as <see cref="Kill"/> does.</summary>
        public void Dispose()
        {
            if (Interlocked.Exchange(ref _disposed, 1) == 0)
            {
                Kill();
            }
        }

        private static IntPtr MakeJob()
        {
            IntPtr job = CadToolNative.CreateJobObjectW(IntPtr.Zero, null);

            if (job == IntPtr.Zero)
            {
                throw new IOException("A job for the process could not be made: " + new Win32Exception().Message);
            }

            var limits = new CadToolNative.JobExtendedLimitInformation();
            limits.BasicLimitInformation.LimitFlags = CadToolNative.JobObjectLimitKillOnJobClose;

            if (
                !CadToolNative.SetInformationJobObject(
                    job,
                    CadToolNative.JobObjectExtendedLimitInformation,
                    ref limits,
                    Marshal.SizeOf(typeof(CadToolNative.JobExtendedLimitInformation))
                )
            )
            {
                string why = new Win32Exception().Message;
                CadToolNative.CloseHandle(job);
                throw new IOException("The job of the process could not be set to end it with the app: " + why);
            }

            return job;
        }

        private static CadToolNative.ProcessInformation Create(
            CadToolStart start,
            List<string> arguments,
            IntPtr[] inherited
        )
        {
            // The three standard streams are the first three handles; only the handles in the list are inherited.
            IntPtr list = IntPtr.Zero;
            IntPtr handles = IntPtr.Zero;
            IntPtr environment = IntPtr.Zero;
            bool listed = false;

            try
            {
                IntPtr size = IntPtr.Zero;
                CadToolNative.InitializeProcThreadAttributeList(IntPtr.Zero, 1, 0, ref size);
                list = Marshal.AllocHGlobal(size);

                if (!CadToolNative.InitializeProcThreadAttributeList(list, 1, 0, ref size))
                {
                    throw new IOException(
                        "The attributes of the process could not be made: " + new Win32Exception().Message
                    );
                }

                listed = true;
                handles = Marshal.AllocHGlobal(IntPtr.Size * inherited.Length);

                for (int i = 0; i < inherited.Length; i++)
                {
                    Marshal.WriteIntPtr(handles, i * IntPtr.Size, inherited[i]);
                }

                if (
                    !CadToolNative.UpdateProcThreadAttribute(
                        list,
                        0,
                        (IntPtr)CadToolNative.ProcThreadAttributeHandleList,
                        handles,
                        (IntPtr)(IntPtr.Size * inherited.Length),
                        IntPtr.Zero,
                        IntPtr.Zero
                    )
                )
                {
                    throw new IOException(
                        "The handles of the process could not be set: " + new Win32Exception().Message
                    );
                }

                var info = new CadToolNative.StartupInfoEx { lpAttributeList = list };
                info.StartupInfo.cb = Marshal.SizeOf(typeof(CadToolNative.StartupInfoEx));
                info.StartupInfo.dwFlags = CadToolNative.StartfUseStdHandles;
                info.StartupInfo.hStdInput = inherited[0];
                info.StartupInfo.hStdOutput = inherited[1];
                info.StartupInfo.hStdError = inherited[2];
                environment = Marshal.StringToHGlobalUni(EnvironmentBlock(start.Environment));
                char[] commandLine = (CommandLine(start.Program, arguments) + "\0").ToCharArray();

                if (
                    !CadToolNative.CreateProcessW(
                        start.Program,
                        commandLine,
                        IntPtr.Zero,
                        IntPtr.Zero,
                        true,
                        CadToolNative.CreateSuspended
                            | CadToolNative.CreateUnicodeEnvironment
                            | CadToolNative.ExtendedStartupInfoPresent
                            | CadToolNative.CreateNoWindow,
                        environment,
                        start.WorkingFolder,
                        ref info,
                        out CadToolNative.ProcessInformation created
                    )
                )
                {
                    throw new IOException(start.Program + " could not be started: " + new Win32Exception().Message);
                }

                return created;
            }
            finally
            {
                if (listed)
                {
                    CadToolNative.DeleteProcThreadAttributeList(list);
                }

                if (list != IntPtr.Zero)
                {
                    Marshal.FreeHGlobal(list);
                }

                if (handles != IntPtr.Zero)
                {
                    Marshal.FreeHGlobal(handles);
                }

                if (environment != IntPtr.Zero)
                {
                    Marshal.FreeHGlobal(environment);
                }
            }
        }

        /// <summary>The environment of the process as Windows takes it: <c>NAME=value</c> and a nul for each, sorted by name, a nul at the end.</summary>
        private static string EnvironmentBlock(IReadOnlyDictionary<string, string> given)
        {
            var variables = new SortedDictionary<string, string>(StringComparer.OrdinalIgnoreCase);

            foreach (DictionaryEntry entry in System.Environment.GetEnvironmentVariables())
            {
                variables[(string)entry.Key] = (string)entry.Value;
            }

            foreach (KeyValuePair<string, string> pair in given)
            {
                variables[pair.Key] = pair.Value;
            }

            var block = new StringBuilder();

            foreach (KeyValuePair<string, string> pair in variables)
            {
                block.Append(pair.Key).Append('=').Append(pair.Value).Append('\0');
            }

            return block.Append('\0').ToString();
        }

        /// <summary>The command line of a program and its arguments, each quoted as the C runtime of the process reads them back.</summary>
        internal static string CommandLine(string program, IEnumerable<string> arguments) =>
            string.Join(" ", new[] { program }.Concat(arguments).Select(Quote));

        internal static string Quote(string argument)
        {
            if (argument.Length > 0 && argument.IndexOfAny(new[] { ' ', '\t', '"' }) < 0)
            {
                return argument;
            }

            var quoted = new StringBuilder("\"");
            int backslashes = 0;

            foreach (char c in argument)
            {
                if (c == '\\')
                {
                    backslashes++;
                }
                else if (c == '"')
                {
                    quoted.Append('\\', backslashes * 2 + 1).Append('"');
                    backslashes = 0;
                }
                else
                {
                    quoted.Append('\\', backslashes).Append(c);
                    backslashes = 0;
                }
            }

            return quoted.Append('\\', backslashes * 2).Append('"').ToString();
        }
    }
}
