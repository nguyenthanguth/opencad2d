using System;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Threading;
using OpenCAD2D.Plugins;

namespace OpenCAD2D.Remote.TestServer
{
    /// <summary>
    /// Hosts a <see cref="CadRemoteServer"/> over a host with no window, so a script in another process can drive it as it
    /// would drive the real app. It writes <c>READY &lt;pipe name&gt; &lt;process id&gt;</c> when it listens, then answers
    /// each line of its standard input with one line: <c>type &lt;text&gt;</c> types into the active drawing's command line,
    /// <c>escape</c> presses Escape there (how a test answers or cancels a pick), <c>window &lt;width&gt; &lt;height&gt;</c> gives the
    /// session of every drawing a view of a canvas of that size in pixels, so that they are shown in a window (the view calls
    /// then work on it and not on the session's own console view), <c>tools</c> opens the private channel of the
    /// tools and answers <c>DONE &lt;pipe&gt; &lt;token&gt;</c> (a new token each time), <c>run &lt;NAME&gt; &lt;token&gt;</c> starts the run
    /// of a command of that name in the active drawing for the tool of that token and answers
    /// <c>DONE &lt;run&gt; &lt;drawing&gt;</c>, <c>toolsettings &lt;token&gt; &lt;JSON&gt;</c> gives the tool of that token the settings of a
    /// plugin.json-shaped object (<c>{"name": "Circles", "settings": [ ... ]}</c>), kept in a settings store of the process, and
    /// answers <c>DONE</c> (the tool then reads and sets them with <c>toolSetting</c> and <c>setToolSetting</c>), <c>quit</c> stops it. It exits by itself
    /// after <c>--minutes</c>, so a test run that died leaves nothing running.
    /// </summary>
    internal static class Program
    {
        private const string Usage = "usage: OpenCAD2D.Remote.TestServer.exe --folder <dir> [--minutes <n>]";

        private static int Main(string[] args)
        {
            if (!TryReadArguments(args, out string folder, out double minutes))
            {
                Console.Error.WriteLine(Usage);
                return 2;
            }

            Type[] wanted = { typeof(ICadRemoteHost), typeof(ICadRemoteDispatcher), typeof(string), typeof(string) };
            ConstructorInfo constructor = typeof(CadRemoteServer)
                .GetConstructors(BindingFlags.NonPublic | BindingFlags.Instance)
                .FirstOrDefault(c => c.GetParameters().Select(p => p.ParameterType).SequenceEqual(wanted));
            if (constructor == null)
            {
                Console.Error.WriteLine(
                    "CadRemoteServer has no internal constructor (host, dispatcher, folder, instance): the test server is out of date."
                );
                return 3;
            }

            Directory.CreateDirectory(folder);
            var host = new TestHost();
            var settings = new GlobalSettings();
            var gate = new object();
            var server = (CadRemoteServer)
                constructor.Invoke(new object[] { host, new InlineDispatcher(gate), folder, "pytest" });
            server.Start();
            CadRemoteServer tools = null;
            var store = new CadPluginSettingStore(() => settings);

            // The timer ends the process from its own thread: the main one is waiting on the standard input.
            using (
                new Timer(
                    _ =>
                    {
                        server.Dispose();
                        Environment.Exit(4);
                    },
                    null,
                    TimeSpan.FromMinutes(minutes),
                    Timeout.InfiniteTimeSpan
                )
            )
            {
                Console.Out.WriteLine("READY " + server.PipeName + " " + Environment.ProcessId);
                Console.Out.Flush();

                string line;
                while ((line = Console.In.ReadLine()) != null)
                {
                    int space = line.IndexOf(' ');
                    string word = space < 0 ? line : line.Substring(0, space);
                    if (word == "quit")
                    {
                        break;
                    }

                    string rest = space < 0 ? "" : line.Substring(space + 1);
                    Console.Out.WriteLine(
                        word == "tools" || word == "run" || word == "toolsettings"
                            ? Tools(host, gate, store, ref tools, word, rest)
                            : Answer(host, gate, word, rest)
                    );
                    Console.Out.Flush();
                }
            }

            tools?.Dispose();
            server.Dispose();
            return 0;
        }

        /// <summary>
        /// Opens the tools channel on the first call and gives a token (<c>tools</c>), or starts the run of a command for the tool of
        /// a token in the active drawing (<c>run NAME TOKEN</c>), and gives the line to answer with.
        /// </summary>
        private static string Tools(
            TestHost host,
            object gate,
            CadPluginSettingStore store,
            ref CadRemoteServer tools,
            string word,
            string rest
        )
        {
            try
            {
                lock (gate)
                {
                    if (tools == null)
                    {
                        tools = CadRemoteServer.ForTools(host, new InlineDispatcher(gate));
                        tools.Start();
                    }

                    if (word == "tools")
                    {
                        return "DONE " + tools.PipeName + " " + tools.IssueToken();
                    }

                    if (word == "toolsettings")
                    {
                        int at = rest.IndexOf(' ');

                        if (at <= 0)
                        {
                            return "ERROR toolsettings needs a token and the JSON of a plugin.json";
                        }

                        CadPluginManifest manifest = CadPluginManifest.Parse(rest.Substring(at + 1));
                        tools.SetToolSettings(
                            rest.Substring(0, at),
                            manifest.Name,
                            store.Declare(manifest.Name, manifest.Settings)
                        );
                        return "DONE";
                    }

                    string[] parts = rest.Split(' ');
                    CadDocument active = host.ActiveDocument;
                    if (parts.Length != 2 || parts[0].Length == 0 || parts[1].Length == 0)
                    {
                        return "ERROR run needs a name and a token";
                    }

                    if (active == null)
                    {
                        return "ERROR no drawing is open";
                    }

                    CadRemoteToolRun run = null;
                    CadCommand command = tools.CreateRunCommand(parts[0], false, parts[1], r => run = r);
                    return host.SessionOf(active).Start(command)
                        ? "DONE " + run.Id + " " + run.DocumentId
                        : "ERROR a command is running in the drawing";
                }
            }
            catch (Exception ex)
            {
                // One answer is one line, whatever the message holds.
                return "ERROR " + ex.GetType().Name + ": " + ex.Message.Replace("\r", " ").Replace("\n", " ");
            }
        }

        /// <summary>Runs one console line under the gate, on the active drawing, and gives the line to answer with.</summary>
        private static string Answer(TestHost host, object gate, string word, string rest)
        {
            if (word != "type" && word != "escape" && word != "window")
            {
                return "ERROR unknown command " + word;
            }

            try
            {
                lock (gate)
                {
                    if (word == "window")
                    {
                        string[] size = rest.Split(new[] { ' ' }, StringSplitOptions.RemoveEmptyEntries);
                        if (
                            size.Length != 2
                            || !double.TryParse(
                                size[0],
                                NumberStyles.Float,
                                CultureInfo.InvariantCulture,
                                out double width
                            )
                            || !double.TryParse(
                                size[1],
                                NumberStyles.Float,
                                CultureInfo.InvariantCulture,
                                out double height
                            )
                            || !(width >= 1.0)
                            || !(height >= 1.0)
                            || double.IsInfinity(width)
                            || double.IsInfinity(height)
                        )
                        {
                            return "ERROR window needs a width and a height in pixels, each at least 1";
                        }

                        host.ShowInWindow(width, height);
                        return "DONE";
                    }

                    CadDocument active = host.ActiveDocument;
                    if (active == null)
                    {
                        return "ERROR no drawing is open";
                    }

                    if (word == "type")
                    {
                        host.Type(active, rest);
                    }
                    else
                    {
                        host.SessionOf(active).Escape();
                    }
                }

                return "DONE";
            }
            catch (Exception ex)
            {
                // One answer is one line, whatever the message holds.
                return "ERROR " + ex.GetType().Name + ": " + ex.Message.Replace("\r", " ").Replace("\n", " ");
            }
        }

        private static bool TryReadArguments(string[] args, out string folder, out double minutes)
        {
            folder = null;
            minutes = 15;
            for (int i = 0; i < args.Length; i++)
            {
                if (args[i] == "--folder" && i + 1 < args.Length)
                {
                    folder = args[++i];
                }
                else if (args[i] == "--minutes" && i + 1 < args.Length)
                {
                    if (
                        !double.TryParse(args[++i], NumberStyles.Float, CultureInfo.InvariantCulture, out minutes)
                        || minutes <= 0
                    )
                    {
                        return false;
                    }
                }
                else
                {
                    return false;
                }
            }

            return !string.IsNullOrEmpty(folder);
        }
    }
}
