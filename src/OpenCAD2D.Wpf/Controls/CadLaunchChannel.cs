using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.IO.Pipes;
using System.Runtime.InteropServices;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using GeometryHelper;

namespace OpenCAD2D.Wpf
{
    /// <summary>
    /// The channel a start with a file hands it to the running app on: a pipe named <c>&lt;AppName&gt;.Open.&lt;session id&gt;</c>,
    /// such as <c>OpenCAD2D.Open.1</c>, open to the Windows user alone. The first app of a name and a session to start owns it
    /// and listens; a later start with a file sends its path to the owner and exits, which is how a drawing double-clicked in
    /// Explorer opens in the window already running.
    /// </summary>
    /// <remarks>
    /// <para>
    /// The name is made with <c>FILE_FLAG_FIRST_PIPE_INSTANCE</c> (<see cref="CadRemotePipe"/>), so making it is the claim:
    /// exactly one process wins, with no mutex beside it. The claim is tried first, and the owner starts without delay;
    /// connecting first would wait the whole connect timeout on a pipe that does not exist.
    /// </para>
    /// <para>
    /// The talk is UTF-8 in lines ending <c>\n</c>. The sender writes <c>OPEN 1</c> and the full path of the file; the owner
    /// answers <c>OK</c> once the path is handed on, or <c>NO</c> to a first line it does not know. The owner answers from its
    /// listener's threads, not from the window's, so an app busy in a long SAVE answers at once.
    /// </para>
    /// </remarks>
    internal sealed class CadLaunchChannel : IDisposable
    {
        /// <summary>How long a start waits to connect to the owner, in milliseconds: 1 000, which covers an owner busy with another start.</summary>
        internal const int ConnectTimeout = 1000;

        /// <summary>How long a start waits for the owner's answer, and an owner for a start's lines, in milliseconds: 3 000.</summary>
        internal const int AnswerTimeout = 3000;

        private const int StopTimeout = 2000;

        private static readonly Encoding Utf8 = new UTF8Encoding(false);

        private readonly string _name;
        private readonly Action<string> _received;
        private readonly object _gate = new object();
        private readonly List<NamedPipeServerStream> _connections = new List<NamedPipeServerStream>();
        private Thread _listener;
        private bool _stopping;

        private CadLaunchChannel(string name, Action<string> received)
        {
            _name = name;
            _received = received;
        }

        [DllImport("user32.dll")]
        private static extern bool AllowSetForegroundWindow(int processId);

        /// <summary>
        /// The name of the channel of an app in this Windows session: <c>"OpenCAD2D.Open.1"</c> for the app OpenCAD2D in session 1.
        /// </summary>
        /// <param name="appName">The name of the app.</param>
        internal static string NameFor(string appName)
        {
            if (appName == null)
            {
                throw new ArgumentNullException(nameof(appName));
            }

            return appName + ".Open." + Process.GetCurrentProcess().SessionId;
        }

        /// <summary>
        /// The full path a start hands to the running app: the first argument that does not start with <c>--</c>, made full
        /// against the current folder. Null for no arguments, for a first argument <c>--screenshot</c>, <c>--register</c> or
        /// <c>--unregister</c> (those never forward), for no file among the arguments, and for a path that cannot be made full.
        /// </summary>
        /// <param name="args">The command-line arguments of the start.</param>
        internal static string FileToForward(string[] args)
        {
            if (args == null || args.Length == 0)
            {
                return null;
            }

            if (
                string.Equals(args[0], "--screenshot", StringComparison.OrdinalIgnoreCase)
                || string.Equals(args[0], "--register", StringComparison.OrdinalIgnoreCase)
                || string.Equals(args[0], "--unregister", StringComparison.OrdinalIgnoreCase)
            )
            {
                return null;
            }

            foreach (string arg in args)
            {
                if (arg == null || arg.StartsWith("--", StringComparison.Ordinal))
                {
                    continue;
                }

                try
                {
                    return Path.GetFullPath(arg);
                }
                catch (Exception ex) when (ex is ArgumentException || ex is NotSupportedException || ex is IOException)
                {
                    // Explorer's working folder is not the app's, so the start itself reports a path it cannot read.
                    return null;
                }
            }

            return null;
        }

        /// <summary>
        /// Claims the name and starts listening on it. Each path received is given to <paramref name="received"/> on a thread
        /// of the channel's, not the window's.
        /// </summary>
        /// <param name="name">The name of the channel, from <see cref="NameFor"/>.</param>
        /// <param name="received">Called with each full path a start sends.</param>
        /// <returns>The channel it owns; null when another process owns the name or the pipe cannot be made.</returns>
        internal static CadLaunchChannel TryClaim(string name, Action<string> received)
        {
            if (name == null)
            {
                throw new ArgumentNullException(nameof(name));
            }

            if (received == null)
            {
                throw new ArgumentNullException(nameof(received));
            }

            NamedPipeServerStream first;

            try
            {
                first = CadRemotePipe.Create(name, true);
            }
            catch (IOException ex)
            {
                GeometryHelperLog.Debug("CadLaunchChannel: " + name + " is not ours: " + ex.Message);
                return null;
            }

            var channel = new CadLaunchChannel(name, received);
            channel._listener = new Thread(() => channel.Listen(first))
            {
                IsBackground = true,
                Name = "CadLaunchChannel",
            };
            channel._listener.Start();
            GeometryHelperLog.Info("CadLaunchChannel: listening on " + name + ".");
            return channel;
        }

        /// <summary>
        /// Sends one full path to the owner of the name and waits for its OK. The sender lets the owner take the foreground
        /// first, as Explorer gave the sender the right to.
        /// </summary>
        /// <param name="name">The name of the channel, from <see cref="NameFor"/>.</param>
        /// <param name="fullPath">The full path of the drawing.</param>
        /// <param name="connectTimeout">Milliseconds to wait to connect.</param>
        /// <param name="answerTimeout">Milliseconds to wait for the answer once connected.</param>
        /// <returns>True when the owner answered OK; false when there is no owner, it did not answer in time, it answered NO, or the pipe failed.</returns>
        internal static bool TryForward(string name, string fullPath, int connectTimeout, int answerTimeout)
        {
            if (name == null)
            {
                throw new ArgumentNullException(nameof(name));
            }

            if (fullPath == null)
            {
                throw new ArgumentNullException(nameof(fullPath));
            }

            try
            {
                using (var client = new NamedPipeClientStream(".", name, PipeDirection.InOut, PipeOptions.Asynchronous))
                {
                    client.Connect(connectTimeout);

                    // -1 is ASFW_ANY: any process may take the foreground, which the owner does on receipt.
                    AllowSetForegroundWindow(-1);

                    // Leaving the using closes the pipe, which ends an exchange the owner never answers.
                    Task<bool> exchange = Exchange(client, Utf8.GetBytes("OPEN 1\n" + fullPath + "\n"));
                    return exchange.Wait(answerTimeout) && exchange.Result;
                }
            }
            catch (Exception ex)
                when (ex is TimeoutException
                    || ex is IOException
                    || ex is UnauthorizedAccessException
                    || ex is ObjectDisposedException
                    || ex is AggregateException
                )
            {
                GeometryHelperLog.Debug("CadLaunchChannel: nothing was forwarded to " + name + ": " + ex.Message);
                return false;
            }
        }

        private static async Task<bool> Exchange(NamedPipeClientStream client, byte[] request)
        {
            try
            {
                await client.WriteAsync(request, 0, request.Length).ConfigureAwait(false);
                await client.FlushAsync().ConfigureAwait(false);

                var reply = new byte[16];
                int length = 0;

                while (length < reply.Length)
                {
                    int count = await client.ReadAsync(reply, length, reply.Length - length).ConfigureAwait(false);

                    if (count == 0)
                    {
                        return false;
                    }

                    length += count;

                    if (reply[length - 1] == (byte)'\n')
                    {
                        break;
                    }
                }

                return Utf8.GetString(reply, 0, length) == "OK\n";
            }
            catch (Exception ex)
                when (ex is IOException || ex is ObjectDisposedException || ex is OperationCanceledException)
            {
                return false;
            }
        }

        /// <summary>Stops listening and closes the pipe, so that the name is free again. Calling it again does nothing.</summary>
        public void Dispose()
        {
            Thread listener;
            List<NamedPipeServerStream> open;

            lock (_gate)
            {
                if (_stopping)
                {
                    return;
                }

                _stopping = true;
                listener = _listener;
                open = new List<NamedPipeServerStream>(_connections);
            }

            if (listener != null)
            {
                // The listener waits in a blocking call for the next start: one that connects and leaves wakes it.
                try
                {
                    using (var wake = new NamedPipeClientStream(".", _name, PipeDirection.InOut))
                    {
                        wake.Connect(1000);
                    }
                }
                catch (Exception ex)
                    when (ex is TimeoutException || ex is IOException || ex is UnauthorizedAccessException)
                {
                    GeometryHelperLog.Debug("CadLaunchChannel: the listener was not woken: " + ex.Message);
                }

                listener.Join(StopTimeout);
            }

            foreach (NamedPipeServerStream pipe in open)
            {
                CloseQuietly(pipe);
            }

            GeometryHelperLog.Info("CadLaunchChannel: stopped " + _name + ".");
        }

        private static void CloseQuietly(NamedPipeServerStream pipe)
        {
            try
            {
                pipe.Dispose();
            }
            catch (Exception ex) when (ex is IOException || ex is ObjectDisposedException)
            {
                GeometryHelperLog.Debug("CadLaunchChannel: a pipe was closed twice: " + ex.Message);
            }
        }

        private void Listen(NamedPipeServerStream pipe)
        {
            try
            {
                Accept(pipe);
            }
            catch (Exception ex) when (!(ex is OutOfMemoryException))
            {
                // A thread of the pipe must never take the app down with it.
                GeometryHelperLog.Warn("CadLaunchChannel: listening stopped.", ex);
            }
        }

        private void Accept(NamedPipeServerStream pipe)
        {
            while (!_stopping)
            {
                try
                {
                    pipe.WaitForConnection();
                }
                catch (IOException ex)
                {
                    GeometryHelperLog.Debug("CadLaunchChannel: a start left before it was taken: " + ex.Message);
                    CloseQuietly(pipe);
                    pipe = Next();

                    if (pipe == null)
                    {
                        return;
                    }

                    continue;
                }

                if (_stopping)
                {
                    CloseQuietly(pipe);
                    return;
                }

                // The next instance is made before this one is served, so that the name is never without a pipe to connect to.
                NamedPipeServerStream next = Next();
                NamedPipeServerStream connected = pipe;

                lock (_gate)
                {
                    _connections.Add(connected);
                }

                new Thread(() => Serve(connected)) { IsBackground = true, Name = "CadLaunchChannel start" }.Start();
                pipe = next;

                if (pipe == null)
                {
                    return;
                }
            }

            CloseQuietly(pipe);
        }

        private NamedPipeServerStream Next()
        {
            try
            {
                return CadRemotePipe.Create(_name, false);
            }
            catch (IOException ex)
            {
                GeometryHelperLog.Warn(
                    "CadLaunchChannel: no pipe could be made for the next start; listening stops.",
                    ex
                );
                return null;
            }
        }

        private void Serve(NamedPipeServerStream pipe)
        {
            Timer guard = null;

            try
            {
                // A start that connects and never writes is let go after the answer timeout, not held for ever.
                guard = new Timer(_ => CloseQuietly(pipe), null, AnswerTimeout, Timeout.Infinite);

                var reader = new StreamReader(pipe, Utf8, false, 1024, true);
                string first = reader.ReadLine();

                if (first == null)
                {
                    return;
                }

                string answer = "NO\n";

                if (first == "OPEN 1")
                {
                    string path = reader.ReadLine();

                    if (!string.IsNullOrEmpty(path))
                    {
                        _received(path);
                        answer = "OK\n";
                    }
                }

                byte[] bytes = Utf8.GetBytes(answer);
                pipe.Write(bytes, 0, bytes.Length);
                pipe.Flush();
                pipe.WaitForPipeDrain();
            }
            catch (Exception ex)
                when (ex is IOException || ex is ObjectDisposedException || ex is InvalidOperationException)
            {
                GeometryHelperLog.Debug("CadLaunchChannel: a start's connection ended: " + ex.Message);
            }
            catch (Exception ex) when (!(ex is OutOfMemoryException))
            {
                // A thread of the pipe must never take the app down with it.
                GeometryHelperLog.Warn("CadLaunchChannel: a start's connection failed.", ex);
            }
            finally
            {
                guard?.Dispose();

                lock (_gate)
                {
                    _connections.Remove(pipe);
                }

                CloseQuietly(pipe);
            }
        }
    }

    /// <summary>
    /// The paths handed to the running app, on the window's thread: each opened when <c>canOpen</c> says so, in the order they
    /// came. A path that arrives while a drawing is still opening, or a dialog is up, waits and opens when the app can, so
    /// that two drawings selected in Explorer at once both open, one after the other, and none is refused.
    /// </summary>
    internal sealed class CadForwardedFiles
    {
        private readonly Func<bool> _canOpen;
        private readonly Action<string> _open;
        private readonly Queue<string> _waiting = new Queue<string>();

        /// <summary>Makes the queue.</summary>
        /// <param name="canOpen">Whether the app can start opening a drawing now; asked before each path.</param>
        /// <param name="open">Opens a drawing: <c>MainViewModel.Open</c>.</param>
        internal CadForwardedFiles(Func<bool> canOpen, Action<string> open)
        {
            _canOpen = canOpen ?? throw new ArgumentNullException(nameof(canOpen));
            _open = open ?? throw new ArgumentNullException(nameof(open));
        }

        /// <summary>The number of paths kept for later.</summary>
        internal int Waiting => _waiting.Count;

        /// <summary>Opens the path now when the app can and no earlier path waits, else keeps it behind those that wait.</summary>
        /// <param name="path">The full path of the drawing.</param>
        internal void Add(string path)
        {
            _waiting.Enqueue(path ?? throw new ArgumentNullException(nameof(path)));
            Retry();
        }

        /// <summary>
        /// Opens the kept paths in order while the app can. It asks again before each path, so one that starts an opening makes
        /// the next wait for it to end.
        /// </summary>
        internal void Retry()
        {
            while (_waiting.Count > 0 && _canOpen())
            {
                _open(_waiting.Dequeue());
            }
        }
    }
}
