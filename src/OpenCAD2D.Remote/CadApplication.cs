using System;
using System.Collections.Generic;
using System.IO;
using GeometryHelper;

namespace OpenCAD2D.Remote
{
    /// <summary>
    /// A running OpenCAD2D, driven from another program the way a Tekla Open API program drives a running Tekla
    /// Structures: <c>new CadApplication()</c> connects, <see cref="Documents"/> lists the drawings it has open and each
    /// <see cref="CadRemoteDocument"/> reads and edits one of them.
    /// </summary>
    /// <remarks>
    /// <para>
    /// The program talks to the app over a named pipe that only the Windows user who runs the app can open, and the
    /// entities travel as DXF text, so what is read and written are the library's own <c>DxfLine</c>, <c>DxfMText</c>,
    /// <c>DxfDimension</c> and the rest. Nothing else is installed on the app's side: it is on by default in the app.
    /// </para>
    /// <para>
    /// One request is run at a time in an app. A second program that asks while the first is being served waits for it.
    /// </para>
    /// </remarks>
    /// <example>
    /// <code>
    /// using (var app = new CadApplication())
    /// {
    ///     CadRemoteDocument drawing = app.ActiveDocument;
    ///     drawing.Insert(new DxfLine(new GeoPoint3(0, 0, 0), new GeoPoint3(1000, 0, 0)) { Layer = "Steel" });
    ///     drawing.CommitChanges();
    ///     drawing.SendCommand("ZOOM E ");
    /// }
    /// </code>
    /// </example>
    public sealed class CadApplication : IDisposable
    {
        private readonly RemoteConnection _connection;
        private readonly Dictionary<int, CadRemoteDocument> _documents = new Dictionary<int, CadRemoteDocument>();

        /// <summary>Connects to the app the user activated last.</summary>
        /// <exception cref="CadRemoteException">Thrown when no OpenCAD2D is running or it does not take the connection.</exception>
        public CadApplication()
            : this(RemoteInstance.DefaultFolder()) { }

        /// <summary>Connects to one of the apps <see cref="GetRunning()"/> listed.</summary>
        /// <param name="app">The app.</param>
        /// <exception cref="ArgumentNullException">Thrown when the app is null.</exception>
        /// <exception cref="CadRemoteException">Thrown when the app has closed or does not take the connection.</exception>
        public CadApplication(CadRunningApp app)
        {
            if (app == null)
            {
                throw new ArgumentNullException(nameof(app));
            }

            Running = app;
            _connection = new RemoteConnection(app.Instance);
            _connection.Request(new JsonObject().Set("op", RemoteOps.Hello));
            Documents = new CadRemoteDocuments(this);
        }

        /// <summary>
        /// Connects to the private channel an app gives the tools it starts, by the name of its pipe and with the token it gave
        /// that tool: the channel is open whether or not "Allow other programs to connect" is on, takes no other client, and is
        /// never listed in <see cref="GetRunning()"/>. A tool joins the run of its command with <see cref="JoinRun"/>.
        /// </summary>
        /// <param name="pipe">The name of the pipe, as <c>OpenCAD2D.4120.tools</c>.</param>
        /// <param name="token">The token the app gave the tool.</param>
        /// <exception cref="ArgumentException">Thrown when the pipe or the token is empty.</exception>
        /// <exception cref="CadRemoteException">Thrown when the app is not there, or does not take the token.</exception>
        public CadApplication(string pipe, string token)
        {
            if (string.IsNullOrEmpty(pipe))
            {
                throw new ArgumentException("The pipe needs a name.", nameof(pipe));
            }

            if (string.IsNullOrEmpty(token))
            {
                throw new ArgumentException(
                    "The channel of the tools needs the token the app gave the tool.",
                    nameof(token)
                );
            }

            int processId = ProcessOf(pipe);
            Running = new CadRunningApp(
                new RemoteInstance
                {
                    ProcessId = processId,
                    Pipe = pipe,
                    Title = string.Empty,
                },
                Array.Empty<string>(),
                string.Empty
            );
            _connection = new RemoteConnection(pipe, processId, token);
            _connection.Request(new JsonObject().Set("op", RemoteOps.Hello));
            Documents = new CadRemoteDocuments(this);
        }

        /// <summary>The process id in the name of a pipe, <c>OpenCAD2D.&lt;process id&gt;.tools</c>; 0 where the name has none.</summary>
        private static int ProcessOf(string pipe)
        {
            string[] parts = pipe.Split('.');
            return parts.Length > 1 && int.TryParse(parts[1], out int processId) ? processId : 0;
        }

        /// <summary>
        /// Joins the run of a command the app started for this tool: every request made on this connection from now on is
        /// part of that run, so that the changes <see cref="CadRemoteDocument.CommitChanges"/> makes are in the one undo step
        /// of the command, and Esc in the app takes them back. The run is left by <see cref="CadRemoteRun.End"/> or
        /// <see cref="CadRemoteRun.Fail"/>.
        /// </summary>
        /// <param name="run">The number of the run, as the app told the tool when it started the command.</param>
        /// <returns>The run.</returns>
        /// <exception cref="ArgumentOutOfRangeException">Thrown when the number is not above nought.</exception>
        public CadRemoteRun JoinRun(int run)
        {
            if (run <= 0)
            {
                throw new ArgumentOutOfRangeException(nameof(run), "A run is numbered from 1.");
            }

            _connection.RunId = run;
            return new CadRemoteRun(this, run);
        }

        /// <summary>Connects to the app that announced itself last in a folder: the apps of a test, or of a second Windows profile.</summary>
        internal CadApplication(string folder)
            : this(Pick(folder)) { }

        private static CadRunningApp Pick(string folder)
        {
            List<RemoteInstance> found = RemoteInstance.ReadAll(folder);

            if (found.Count == 0)
            {
                throw new CadRemoteException(
                    "No OpenCAD2D is running. Start the app, or check that it allows other programs to connect."
                );
            }

            return new CadRunningApp(found[0], Array.Empty<string>(), folder);
        }

        /// <summary>Gets the app this one is connected to.</summary>
        public CadRunningApp Running { get; }

        /// <summary>Gets the drawings the app has open.</summary>
        public CadRemoteDocuments Documents { get; }

        internal RemoteConnection Connection => _connection;

        /// <summary>
        /// Lists the apps that are running, the one activated last first: each with its process id, its window's title and the
        /// drawings it has open. An app that has gone leaves no trace; an app busy with a long command is listed with no
        /// drawings.
        /// </summary>
        /// <returns>The running apps, none when none is running.</returns>
        public static IReadOnlyList<CadRunningApp> GetRunning() => GetRunning(RemoteInstance.DefaultFolder());

        internal static IReadOnlyList<CadRunningApp> GetRunning(string folder)
        {
            var apps = new List<CadRunningApp>();

            foreach (RemoteInstance instance in RemoteInstance.ReadAll(folder))
            {
                var names = new List<string>();

                using (var connection = new RemoteConnection(instance) { ConnectTimeout = 2000 })
                {
                    try
                    {
                        foreach (
                            object item in connection
                                .Request(new JsonObject().Set("op", RemoteOps.Documents))
                                .GetArray("documents")
                        )
                        {
                            var entry = (JsonObject)item;
                            string path = entry.GetString("path");
                            names.Add(string.IsNullOrEmpty(path) ? entry.GetString("name") : path);
                        }
                    }
                    catch (CadRemoteException ex)
                    {
                        GeometryHelperLog.Debug(
                            "Remote: process " + instance.ProcessId + " was not asked: " + ex.Message
                        );
                    }
                }

                apps.Add(new CadRunningApp(instance, names, folder));
            }

            return apps;
        }

        /// <summary>Connects to the app that has a file open.</summary>
        /// <param name="path">The path of the drawing's file.</param>
        /// <returns>The connection to the first app, the one activated last first, that has the file open.</returns>
        /// <exception cref="ArgumentNullException">Thrown when the path is null.</exception>
        /// <exception cref="CadRemoteException">Thrown when no running app has the file open.</exception>
        public static CadApplication ForDocument(string path) => ForDocument(path, RemoteInstance.DefaultFolder());

        internal static CadApplication ForDocument(string path, string folder)
        {
            if (path == null)
            {
                throw new ArgumentNullException(nameof(path));
            }

            string full = Path.GetFullPath(path);

            foreach (CadRunningApp app in GetRunning(folder))
            {
                foreach (string drawing in app.Drawings)
                {
                    if (string.Equals(drawing, full, StringComparison.OrdinalIgnoreCase))
                    {
                        return new CadApplication(app);
                    }
                }
            }

            throw new CadRemoteException("No running OpenCAD2D has " + full + " open.");
        }

        /// <summary>
        /// Reads a setting the tool declares in its plugin.json, as the user last set it in Load/Unload Applications, or its
        /// default. Only on the channel of the tools, with the token of the tool; the app answers at once, whatever its window is
        /// doing, in a run or out of one.
        /// </summary>
        /// <param name="name">The setting's name, as plugin.json gives it: "textHeight".</param>
        /// <returns>A <c>double</c> for a number, an <c>int</c> for an integer, a <c>bool</c>, a <c>string</c> for a text or the name of a choice; null for a text with no value.</returns>
        /// <exception cref="ArgumentNullException">Thrown when the name is null.</exception>
        /// <exception cref="CadRemoteException">Thrown when the tool declares no such setting, or this is not the channel of the tools.</exception>
        public object GetToolSetting(string name)
        {
            if (name == null)
            {
                throw new ArgumentNullException(nameof(name));
            }

            JsonObject answer = _connection.Request(
                new JsonObject().Set("op", RemoteOps.ToolSetting).Set("name", name)
            );

            switch (answer.GetString("kind"))
            {
                case "number":
                    return answer.GetDouble("value");
                case "integer":
                    return (int)answer.GetLong("value");
                default:
                    return answer.Get("value");
            }
        }

        /// <summary>
        /// Sets a setting the tool declares in its plugin.json. The app checks the value as it checks one typed in the settings
        /// window, keeps it in its settings file and shows it there; the next <see cref="GetToolSetting"/> gives it. Only on the
        /// channel of the tools, with the token of the tool.
        /// </summary>
        /// <param name="name">The setting's name, as plugin.json gives it.</param>
        /// <param name="value">A number, a <c>bool</c>, a text or the name of a choice; null for a text with no value. A text that reads as the setting's kind is taken ("2.5" for a number).</param>
        /// <exception cref="ArgumentNullException">Thrown when the name is null.</exception>
        /// <exception cref="ArgumentException">Thrown when the value is none of those types, or a number that is not finite.</exception>
        /// <exception cref="CadRemoteException">Thrown when the tool declares no such setting, the value is out of its range or of another kind (the message is the setting's own words: "The setting Text height lies from 0.1 to 1000."), or this is not the channel of the tools.</exception>
        public void SetToolSetting(string name, object value)
        {
            if (name == null)
            {
                throw new ArgumentNullException(nameof(name));
            }

            _connection.Request(
                new JsonObject().Set("op", RemoteOps.SetToolSetting).Set("name", name).Set("value", Wire(value))
            );
        }

        /// <summary>A value as the channel carries it: a whole number as a <c>long</c>, any other number as a <c>double</c>.</summary>
        private static object Wire(object value)
        {
            switch (value)
            {
                case null:
                case string _:
                case bool _:
                    return value;
                case sbyte _:
                case byte _:
                case short _:
                case ushort _:
                case int _:
                case uint _:
                case long _:
                    return System.Convert.ToInt64(value, System.Globalization.CultureInfo.InvariantCulture);
                case ulong big:
                    return big <= long.MaxValue ? (object)(long)big : (double)big;
                case float _:
                case double _:
                case decimal _:
                    double number = System.Convert.ToDouble(value, System.Globalization.CultureInfo.InvariantCulture);

                    if (double.IsNaN(number) || double.IsInfinity(number))
                    {
                        throw new ArgumentException(
                            "A setting is never set to a number that is not finite.",
                            nameof(value)
                        );
                    }

                    return number;
                default:
                    throw new ArgumentException(
                        "A setting is set to a number, a bool or a text, not a " + value.GetType().Name + ".",
                        nameof(value)
                    );
            }
        }

        /// <summary>Gets the drawing the user works in, or null when the app has none open.</summary>
        public CadRemoteDocument ActiveDocument
        {
            get
            {
                foreach (CadRemoteDocument document in Documents)
                {
                    if (document.IsActive)
                    {
                        return document;
                    }
                }

                return null;
            }
        }

        /// <summary>Opens a drawing from a file and shows it, or, where the app has the file open already, returns that drawing and shows it.</summary>
        /// <param name="path">The path of a DXF or DWG file.</param>
        /// <returns>The drawing.</returns>
        /// <exception cref="ArgumentNullException">Thrown when the path is null.</exception>
        /// <exception cref="CadRemoteException">Thrown when the file cannot be opened.</exception>
        public CadRemoteDocument Open(string path)
        {
            if (path == null)
            {
                throw new ArgumentNullException(nameof(path));
            }

            JsonObject answer = _connection.Request(
                new JsonObject().Set("op", RemoteOps.Open).Set("path", Path.GetFullPath(path))
            );
            return Known((JsonObject)answer.Get("document"));
        }

        /// <summary>Starts a new drawing and shows it.</summary>
        /// <param name="template">The path of a template to start from; null for the app's own template.</param>
        /// <returns>The drawing.</returns>
        /// <exception cref="CadRemoteException">Thrown when the template cannot be read.</exception>
        public CadRemoteDocument New(string template = null)
        {
            JsonObject answer = _connection.Request(
                new JsonObject()
                    .Set("op", RemoteOps.New)
                    .Set("template", template == null ? null : Path.GetFullPath(template))
            );
            return Known((JsonObject)answer.Get("document"));
        }

        /// <summary>The drawing of an entry of the app's answer: the one this connection has made already, its facts brought up to date.</summary>
        internal CadRemoteDocument Known(JsonObject entry)
        {
            int id = (int)entry.GetLong("id");

            if (!_documents.TryGetValue(id, out CadRemoteDocument document))
            {
                document = new CadRemoteDocument(this, id);
                _documents[id] = document;
            }

            document.Update(entry);
            return document;
        }

        /// <summary>
        /// Closes the connection; the app keeps running and its drawings open. A call on this object or on a drawing of it after
        /// that raises <see cref="ObjectDisposedException"/> and opens no new pipe (a connection lost by the app is opened
        /// again; one closed here is not): make a new <see cref="CadApplication"/>. Calling it again does nothing.
        /// </summary>
        public void Dispose() => _connection.Dispose();
    }
}
