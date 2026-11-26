using System;
using System.Collections.Generic;
using System.Globalization;
using GeometryHelper.Geometry;
using OpenCAD2D.Remote;

namespace OpenCAD2D
{
    /// <summary>What a program that debugs the app asks: the picture of its canvas, a drawing's command line, and what went wrong in it.</summary>
    public sealed partial class CadRemoteServer
    {
        /// <summary>How many failed requests the server remembers.</summary>
        private const int FailuresKept = 50;

        /// <summary>The most lines or entries one request gives.</summary>
        private const int MostRows = 1000;

        // The requests the app could not answer for a fault of its own, newest last: touched from the pipe's threads.
        private readonly List<JsonObject> _failures = new List<JsonObject>();

        /// <summary>Answers a request that no case of <see cref="Execute"/> knows: the debugging ones, or a refusal of an op of a newer client.</summary>
        private JsonObject OtherRequest(string op, JsonObject request)
        {
            switch (op)
            {
                case RemoteDebugOps.Screenshot:
                    return Screenshot(request);
                case RemoteDebugOps.History:
                    return History(Find(request), request);
                case RemoteDebugOps.Errors:
                    return Errors(request);
                default:
                    throw new Refusal("OpenCAD2D does not know the request " + op + ": it is of an older version.");
            }
        }

        /// <summary>Remembers a request that failed for a fault of the app's own, with the head of its stack.</summary>
        private void NoteFailure(string op, Exception exception)
        {
            string[] stack = (exception.StackTrace ?? string.Empty).Replace("\r", string.Empty).Split('\n');
            var head = new JsonArray();

            for (int i = 0; i < stack.Length && i < CadCrashLogReader.HeadLines; i++)
            {
                if (stack[i].Trim().Length > 0)
                {
                    head.Add(stack[i].Trim());
                }
            }

            var failure = new JsonObject()
                .Set("time", DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss.fff zzz", CultureInfo.InvariantCulture))
                .Set("request", op ?? string.Empty)
                .Set("message", exception.GetType().Name + ": " + exception.Message)
                .Set("stack", head);

            lock (_failures)
            {
                _failures.Add(failure);

                if (_failures.Count > FailuresKept)
                {
                    _failures.RemoveAt(0);
                }
            }
        }

        private JsonObject Screenshot(JsonObject request)
        {
            if (!(_host is ICadRemoteDiagnostics diagnostics))
            {
                throw new Refusal(
                    "This OpenCAD2D has no window to take a picture of: it runs with no canvas. Draw the drawing with render_png instead."
                );
            }

            CadDocument document = _host.ActiveDocument;

            if (document == null)
            {
                throw new Refusal("OpenCAD2D shows no drawing: there is no canvas to take a picture of.");
            }

            long maxSide = request.GetLong("maxSide", 0);

            if (maxSide < 0 || maxSide > 16384)
            {
                throw new Refusal("The most pixels along a side is from 0 (as it is) to 16384.");
            }

            CadRemotePicture picture = diagnostics.CanvasPicture((int)maxSide);

            if (picture == null)
            {
                throw new Refusal("The window shows no drawing canvas now: open a drawing in it.");
            }

            DisplayBox view = _host.SessionOf(document).Editor.GetCurrentView();
            return new JsonObject()
                .Set("png", Convert.ToBase64String(picture.Png))
                .Set("width", (long)picture.Width)
                .Set("height", (long)picture.Height)
                .Set("min", Corner(view, true))
                .Set("max", Corner(view, false))
                .Set("document", document.Title);
        }

        private JsonObject History(CadDocument document, JsonObject request)
        {
            CadCommandSession session = _host.SessionOf(document);
            int count = Rows(request, 20);
            IReadOnlyList<string> history = session.History;
            int first = Math.Max(0, history.Count - count);
            var lines = new JsonArray();

            for (int i = first; i < history.Count; i++)
            {
                lines.Add(history[i]);
            }

            return new JsonObject()
                .Set("lines", lines)
                .Set("total", (long)history.Count)
                .Set("running", session.IsRunning)
                .Set("prompt", session.PromptText);
        }

        private JsonObject Errors(JsonObject request)
        {
            int count = Rows(request, 10);
            string path = (_host as ICadRemoteDiagnostics)?.CrashLogPath;
            var entries = new JsonArray();

            if (path != null)
            {
                foreach (CadCrashLogEntry entry in CadCrashLogReader.Read(path, count))
                {
                    var head = new JsonArray();

                    foreach (string line in entry.Head)
                    {
                        head.Add(line);
                    }

                    entries.Add(
                        new JsonObject()
                            .Set("time", entry.Time)
                            .Set("caughtBy", entry.CaughtBy)
                            .Set("command", entry.LastCommand)
                            .Set("fatal", entry.Fatal)
                            .Set("head", head)
                    );
                }
            }

            var requests = new JsonArray();

            lock (_failures)
            {
                for (int i = _failures.Count - 1; i >= 0 && requests.Count < count; i--)
                {
                    requests.Add(_failures[i]);
                }
            }

            return new JsonObject().Set("crashLog", path).Set("entries", entries).Set("requests", requests);
        }

        private static int Rows(JsonObject request, long fallback)
        {
            long count = request.GetLong("count", fallback);

            if (count < 1 || count > MostRows)
            {
                throw new Refusal("The count is from 1 to " + MostRows + ".");
            }

            return (int)count;
        }
    }
}
