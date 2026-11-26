using System;
using System.Collections.Generic;
using System.Globalization;

namespace OpenCAD2D.Remote
{
    /// <summary>The picture of the app's drawing canvas, as <see cref="CadApplication.GetScreenshot"/> reads it.</summary>
    public sealed class CadRemoteScreenshot
    {
        internal CadRemoteScreenshot(byte[] png, int width, int height, double[] view, string document)
        {
            Png = png;
            Width = width;
            Height = height;
            View = view;
            Document = document;
        }

        /// <summary>Gets the PNG file's bytes: 8-bit red, green and blue, the pixels the window composes its cursor, grips and command line over.</summary>
        public byte[] Png { get; }

        /// <summary>Gets the width in pixels.</summary>
        public int Width { get; }

        /// <summary>Gets the height in pixels.</summary>
        public int Height { get; }

        /// <summary>Gets the part of the drawing the canvas shows, <c>[minX, minY, maxX, maxY]</c> in the units of the drawing.</summary>
        public double[] View { get; }

        /// <summary>Gets the name of the drawing shown.</summary>
        public string Document { get; }
    }

    /// <summary>The last lines of a drawing's command line, as <see cref="CadRemoteDocument.GetHistory"/> reads them.</summary>
    public sealed class CadRemoteHistory
    {
        internal CadRemoteHistory(IReadOnlyList<string> lines, int total, bool running, string prompt)
        {
            Lines = lines;
            Total = total;
            IsRunning = running;
            Prompt = prompt;
        }

        /// <summary>Gets the lines asked for, the oldest first: each command, each prompt with its answer, each message.</summary>
        public IReadOnlyList<string> Lines { get; }

        /// <summary>Gets how many lines the command line holds in all.</summary>
        public int Total { get; }

        /// <summary>Gets whether a command is running.</summary>
        public bool IsRunning { get; }

        /// <summary>Gets the prompt a running command waits at; empty where none runs.</summary>
        public string Prompt { get; }
    }

    /// <summary>One fault of the app: an exception its crash log holds, or a request it could not answer.</summary>
    public sealed class CadRemoteError
    {
        internal CadRemoteError(string time, string source, string command, bool fatal, IReadOnlyList<string> head)
        {
            Time = time;
            Source = source;
            Command = command;
            IsFatal = fatal;
            Head = head;
        }

        /// <summary>Gets the time, as <c>2026-11-26 14:03:11.250 +07:00</c>.</summary>
        public string Time { get; }

        /// <summary>Gets where it was caught: <c>Application.DispatcherUnhandledException</c> for the crash log, <c>request query</c> for a request.</summary>
        public string Source { get; }

        /// <summary>Gets the command run last, for a crash log entry; empty otherwise.</summary>
        public string Command { get; }

        /// <summary>Gets whether the app could not go on after it.</summary>
        public bool IsFatal { get; }

        /// <summary>Gets the exception's message and the top of its stack.</summary>
        public IReadOnlyList<string> Head { get; }
    }

    /// <summary>What <see cref="CadApplication.GetErrors"/> reads: the app's crash log and the requests it could not answer.</summary>
    public sealed class CadRemoteErrors
    {
        internal CadRemoteErrors(
            string crashLog,
            IReadOnlyList<CadRemoteError> entries,
            IReadOnlyList<CadRemoteError> requests
        )
        {
            CrashLogPath = crashLog;
            CrashLog = entries;
            Requests = requests;
        }

        /// <summary>Gets the file the app writes its crash log to; null where it writes none.</summary>
        public string CrashLogPath { get; }

        /// <summary>Gets the entries of the crash log, the newest first.</summary>
        public IReadOnlyList<CadRemoteError> CrashLog { get; }

        /// <summary>Gets the requests the app could not answer since it started, the newest first.</summary>
        public IReadOnlyList<CadRemoteError> Requests { get; }
    }

    public sealed partial class CadApplication
    {
        /// <summary>
        /// Gets the picture of the drawing canvas of the window as it last drew it, nothing shown or changed: the picture
        /// <c>--screenshot --canvas</c> writes, so that what the user sees can be compared with a render of the same drawing.
        /// </summary>
        /// <param name="maxSide">The most pixels along a side; a larger picture is made smaller, keeping its shape; 0 for as it is.</param>
        /// <returns>The picture, its size and the part of the drawing it shows.</returns>
        /// <exception cref="ArgumentOutOfRangeException">Thrown when the number is below 0 or above 16384.</exception>
        /// <exception cref="CadRemoteException">Thrown when the app has no window (a test server), shows no drawing, or is of a version that does not know the request.</exception>
        public CadRemoteScreenshot GetScreenshot(int maxSide = 0)
        {
            if (maxSide < 0 || maxSide > 16384)
            {
                throw new ArgumentOutOfRangeException(
                    nameof(maxSide),
                    "The most pixels along a side is from 0 to 16384."
                );
            }

            JsonObject answer = _connection.Request(
                new JsonObject().Set("op", RemoteDebugOps.Screenshot).Set("maxSide", (long)maxSide)
            );
            JsonObject min = answer.GetObject("min");
            JsonObject max = answer.GetObject("max");
            return new CadRemoteScreenshot(
                Convert.FromBase64String(answer.GetString("png") ?? string.Empty),
                (int)answer.GetLong("width"),
                (int)answer.GetLong("height"),
                new[] { min.GetDouble("x"), min.GetDouble("y"), max.GetDouble("x"), max.GetDouble("y") },
                answer.GetString("document") ?? string.Empty
            );
        }

        /// <summary>
        /// Gets what went wrong in the app lately: the entries of its crash log (the exceptions nobody caught, with the command that
        /// ran) and the requests it could not answer for a fault of its own. Nothing is changed.
        /// </summary>
        /// <param name="count">The most of each to give, from 1 to 1000.</param>
        /// <returns>The crash log and the failed requests, the newest first.</returns>
        /// <exception cref="ArgumentOutOfRangeException">Thrown when the count is not from 1 to 1000.</exception>
        /// <exception cref="CadRemoteException">Thrown when the app is of a version that does not know the request.</exception>
        public CadRemoteErrors GetErrors(int count = 10)
        {
            if (count < 1 || count > 1000)
            {
                throw new ArgumentOutOfRangeException(nameof(count), "The count is from 1 to 1000.");
            }

            JsonObject answer = _connection.Request(
                new JsonObject().Set("op", RemoteDebugOps.Errors).Set("count", (long)count)
            );
            var entries = new List<CadRemoteError>();

            foreach (object item in answer.GetArray("entries"))
            {
                if (item is JsonObject entry)
                {
                    entries.Add(
                        new CadRemoteError(
                            entry.GetString("time") ?? string.Empty,
                            entry.GetString("caughtBy") ?? string.Empty,
                            entry.GetString("command") ?? string.Empty,
                            entry.GetBool("fatal"),
                            Texts(entry.GetArray("head"))
                        )
                    );
                }
            }

            var requests = new List<CadRemoteError>();

            foreach (object item in answer.GetArray("requests"))
            {
                if (item is JsonObject failure)
                {
                    var head = new List<string> { failure.GetString("message") ?? string.Empty };
                    head.AddRange(Texts(failure.GetArray("stack")));
                    requests.Add(
                        new CadRemoteError(
                            failure.GetString("time") ?? string.Empty,
                            "request " + failure.GetString("request"),
                            string.Empty,
                            false,
                            head
                        )
                    );
                }
            }

            return new CadRemoteErrors(answer.GetString("crashLog"), entries, requests);
        }

        private static List<string> Texts(JsonArray items)
        {
            var texts = new List<string>();

            foreach (object item in items)
            {
                texts.Add(Convert.ToString(item, CultureInfo.InvariantCulture) ?? string.Empty);
            }

            return texts;
        }
    }
}
