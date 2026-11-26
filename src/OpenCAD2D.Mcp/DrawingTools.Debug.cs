using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Linq;
using GeometryHelper;
using ModelContextProtocol;
using ModelContextProtocol.Protocol;
using ModelContextProtocol.Server;
using OpenCAD2D.Remote;

namespace OpenCAD2D.Mcp
{
    /// <summary>The tools that help find what is wrong: the app's own picture and command line, its faults, a dump of its drawing, a comparison of two drawings and the time things take.</summary>
    internal sealed partial class DrawingTools
    {
        [McpServerTool(
            Name = "app_screenshot",
            ReadOnly = true,
            Destructive = false,
            Idempotent = true,
            OpenWorld = false
        )]
        [Description(
            "The picture of the drawing canvas of the running app, as the window last drew it (the pixels the user sees under the cursor, grips and command line), returned as an image, with its size and the part of the drawing it shows: render_png with that box and size draws the same part from the drawing, to compare what the user sees with what the drawing holds. The app shows nothing and changes nothing for it. Only for a server attached to an app; a drawing file has no canvas (use render_png)."
        )]
        public CallToolResult AppScreenshot(
            [Description("The most pixels along a side; a larger picture is made smaller. 0 leaves it as it is.")]
                int max_side = 0
        ) =>
            _session.RunWithout(() =>
            {
                CadApplication app = _session.Application
                    ?? throw new ArgumentException(
                        "app_screenshot looks at a running app, and this server works on a drawing file: use render_png to draw the file."
                    );
                CadRemoteScreenshot shot = app.GetScreenshot(max_side);
                double[] view = shot.View.Select(EntityDescriber.R).ToArray();
                string text = string.Format(
                    CultureInfo.InvariantCulture,
                    "PNG {0} by {1} pixels, {2} bytes, the canvas of {3}. It shows [{4}, {5}, {6}, {7}] of the drawing: render_png with box [{4}, {5}, {6}, {7}], width {0} and height {1} draws the same part.",
                    shot.Width,
                    shot.Height,
                    shot.Png.Length,
                    shot.Document,
                    view[0],
                    view[1],
                    view[2],
                    view[3]
                );
                return new CallToolResult
                {
                    Content = new List<ContentBlock>
                    {
                        ImageContentBlock.FromBytes(shot.Png, "image/png"),
                        new TextContentBlock { Text = text },
                    },
                };
            });

        [McpServerTool(
            Name = "command_history",
            ReadOnly = true,
            Destructive = false,
            Idempotent = true,
            OpenWorld = false
        )]
        [Description(
            "The last lines of the drawing's command line, the oldest first: each command, each prompt with its answer, each message, as the user sees them in the history above the command line. Shows how a drawing came to be as it is. Attached to an app it is the app's command line, whoever typed; on a drawing file it is this server's own session, which holds what went through the command line (run_command, purge, audit with fix), not the edits the typed tools make."
        )]
        public HistoryResult CommandHistory([Description("How many lines, 1 to 1000.")] int count = 20) =>
            _session.Run("command_history", false, drawing =>
            {
                if (count < 1 || count > 1000)
                {
                    throw new ArgumentException("count is from 1 to 1000.");
                }

                if (drawing is RemoteBackend app)
                {
                    CadRemoteHistory history = app.Document.GetHistory(count);
                    return new HistoryResult
                    {
                        Lines = history.Lines,
                        Total = history.Total,
                        Running = history.IsRunning,
                        Prompt = history.Prompt,
                    };
                }

                CadCommandSession session = ((LocalBackend)drawing).Session;
                IReadOnlyList<string> all = session.History;
                return new HistoryResult
                {
                    Lines = all.Skip(Math.Max(0, all.Count - count)).ToList(),
                    Total = all.Count,
                    Running = session.IsRunning,
                    Prompt = session.PromptText,
                };
            });

        [McpServerTool(
            Name = "last_errors",
            ReadOnly = true,
            Destructive = false,
            Idempotent = true,
            OpenWorld = false
        )]
        [Description(
            "What went wrong lately. app: the running app's own faults, the entries of its crash log (%AppData%\\OpenCAD2D\\crash.log, then crash.old.log: the exception nobody caught, where it was caught, the command running then, the head of the stack) and the requests the app could not answer since it started. server: the tool calls this server could not do, with the message and, for a fault that is no plain refusal, the head of the stack. Newest first."
        )]
        public ErrorsResult LastErrors([Description("How many of each, 1 to 1000.")] int count = 10) =>
            _session.RunWithout(() =>
            {
                if (count < 1 || count > 1000)
                {
                    throw new ArgumentException("count is from 1 to 1000.");
                }

                var result = new ErrorsResult
                {
                    App = Array.Empty<ErrorRow>(),
                    Server = _session
                        .Failures(count)
                        .Select(f =>
                        {
                            var text = new List<string> { f.Message };
                            text.AddRange(f.Stack);
                            return new ErrorRow
                            {
                                Time = McpFailures.Stamp(f.Time),
                                Source = f.IsRefusal ? "tool refused" : "tool failed",
                                Where = f.Tool ?? "open or new drawing",
                                Text = text,
                            };
                        })
                        .ToList(),
                };

                CadApplication app = _session.Application;

                if (app == null)
                {
                    result.Note =
                        "A drawing file worked on in this server has no app: only the tool calls this server could not do are listed.";
                    return result;
                }

                try
                {
                    CadRemoteErrors errors = app.GetErrors(count);
                    result.CrashLog = errors.CrashLogPath;
                    result.App = errors
                        .CrashLog.Select(e => new ErrorRow
                        {
                            Time = e.Time,
                            Source = "crash log: " + e.Source,
                            Where = e.Command,
                            Fatal = e.IsFatal,
                            Text = e.Head,
                        })
                        .Concat(
                            errors.Requests.Select(r => new ErrorRow
                            {
                                Time = r.Time,
                                Source = "app request",
                                Where = r.Source.StartsWith("request ", StringComparison.Ordinal)
                                    ? r.Source.Substring("request ".Length)
                                    : r.Source,
                                Text = r.Head,
                            })
                        )
                        .ToList();

                    if (errors.CrashLogPath == null)
                    {
                        result.Note = "This app writes no crash log: only the requests it could not answer are listed.";
                    }
                }
                catch (CadRemoteException exception) when (exception.Message.Contains("does not know the request"))
                {
                    result.Note = "This app is of a version that does not tell its faults: " + exception.Message;
                }

                return result;
            });

        [McpServerTool(
            Name = "dump_drawing",
            ReadOnly = false,
            Destructive = false,
            Idempotent = false,
            OpenWorld = false
        )]
        [Description(
            "Saves the drawing as it is now as a DXF file in the debug folder (%TEMP%\\OpenCAD2D\\mcp-debug), so that it can be kept, opened, or compared later with compare_drawings. Attached to an app it is the app's drawing as it stands, changes not saved included. It writes only inside the debug folder, never the drawing's own file, so it is allowed with --read-only. The default name is the drawing's with the time."
        )]
        public DumpResult DumpDrawing(
            [Description(
                "A file name in the debug folder, as before.dxf, or a path inside it; the drawing's name and the time where left out."
            )]
                string path = null
        ) => _session.Run("dump_drawing", false, drawing =>
            {
                string folder = Path.GetFullPath(McpKit.DebugFolder());
                string full = DumpPath(drawing, folder, path);
                Directory.CreateDirectory(Path.GetDirectoryName(full));
                McpKit.Snapshot(drawing).SaveCopy(full, null);
                return new DumpResult
                {
                    Path = full,
                    Bytes = new FileInfo(full).Length,
                    Mode = drawing.Mode,
                };
            });

        private static string DumpPath(IDrawingBackend drawing, string folder, string path)
        {
            string full;

            if (string.IsNullOrWhiteSpace(path))
            {
                string name = Path.GetFileNameWithoutExtension(drawing.Name ?? "drawing");
                string safe = new string(
                    name.Select(c => Path.GetInvalidFileNameChars().Contains(c) ? '_' : c).ToArray()
                );
                full = Path.Combine(
                    folder,
                    safe + "-" + DateTime.Now.ToString("yyyyMMdd-HHmmss-fff", CultureInfo.InvariantCulture) + ".dxf"
                );
            }
            else
            {
                string given = path.Trim();

                if (Path.GetExtension(given).Length == 0)
                {
                    given += ".dxf";
                }
                else if (!string.Equals(Path.GetExtension(given), ".dxf", StringComparison.OrdinalIgnoreCase))
                {
                    throw new ArgumentException("A dump is a DXF file: " + given + " does not end in .dxf.");
                }

                full = Path.GetFullPath(Path.IsPathRooted(given) ? given : Path.Combine(folder, given));
            }

            if (!full.StartsWith(folder + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase))
            {
                throw new ArgumentException(
                    "A dump is written inside the debug folder "
                        + folder
                        + ", and "
                        + full
                        + " is not in it. Use save_as to write a copy elsewhere."
                );
            }

            if (
                drawing.Path != null
                && string.Equals(Path.GetFullPath(drawing.Path), full, StringComparison.OrdinalIgnoreCase)
            )
            {
                throw new ArgumentException("That is the open drawing's own file: a dump never writes it.");
            }

            return full;
        }

        [McpServerTool(
            Name = "compare_drawings",
            ReadOnly = true,
            Destructive = false,
            Idempotent = true,
            OpenWorld = false
        )]
        [Description(
            "Compares two states of a drawing object by object, by handle: the objects added, the objects removed, and the objects changed with the names of the properties that differ (layer, color, start, end, box, text ...) and how far their box moved. Each of a and b is current (the drawing the tools work on, as it is now) or the path of a DXF or DWG file, a dump_drawing file for one. Lengths are millimetres and compared to 0.001 mm; block pieces are told apart by their place in the block. Two drawings drawn separately share no handles: everything of one is added and removed. Lists are cut at max_rows, the counts are of everything."
        )]
        public DiffResult CompareDrawings(
            [Description(
                "The first state: current, or a file path (a dump: its full path, or its name in the debug folder)."
            )]
                string a,
            [Description("The second state: current, or a file path.")] string b = "current",
            [Description("The most rows of each list, 1 to 500.")] int max_rows = DefaultPageSize
        ) => _session.Run("compare_drawings", false, drawing =>
            {
                if (max_rows < 1 || max_rows > MostPageSize)
                {
                    throw new ArgumentException("max_rows is from 1 to " + MostPageSize + ".");
                }

                CadDocument first = Compared(drawing, a, "a");
                CadDocument second = Compared(drawing, b, "b");
                CadDrawingDiffResult diff = CadDrawingDiff.Compare(first, second, Tol);
                bool cut =
                    diff.Added.Count > max_rows || diff.Removed.Count > max_rows || diff.Changed.Count > max_rows;
                return new DiffResult
                {
                    A = a,
                    B = b,
                    Same = diff.IsSame,
                    AddedCount = diff.Added.Count,
                    RemovedCount = diff.Removed.Count,
                    ChangedCount = diff.Changed.Count,
                    Unchanged = diff.Unchanged,
                    Truncated = cut,
                    Added = diff.Added.Take(max_rows).Select(Row).ToList(),
                    Removed = diff.Removed.Take(max_rows).Select(Row).ToList(),
                    Changed = diff.Changed.Take(max_rows).Select(Row).ToList(),
                };
            });

        private static ChangeRow Row(CadDrawingChange change) =>
            new ChangeRow
            {
                Handle = change.Handle,
                Kind = change.Kind,
                Layout = change.Layout,
                Layer = change.Layer,
                Properties = change.Properties.Count == 0 ? null : change.Properties,
                BoxBefore = Rounded(change.BoxBefore),
                BoxAfter = Rounded(change.BoxAfter),
                MovedBy = Rounded(change.MovedBy),
            };

        private static CadDocument Compared(IDrawingBackend drawing, string which, string name)
        {
            if (string.IsNullOrWhiteSpace(which))
            {
                throw new ArgumentException(name + " is current, or the path of a DXF or DWG file.");
            }

            if (string.Equals(which.Trim(), "current", StringComparison.OrdinalIgnoreCase))
            {
                return McpKit.Snapshot(drawing);
            }

            string given = which.Trim();

            // A file name with no folder is a dump of the debug folder where there is one of that name, as dump_drawing takes it.
            string inDebug = Path.IsPathRooted(given)
                ? null
                : Path.GetFullPath(Path.Combine(McpKit.DebugFolder(), given));
            string full = inDebug != null && File.Exists(inDebug) ? inDebug : Path.GetFullPath(given);

            if (!File.Exists(full))
            {
                throw new ArgumentException(
                    name + " is current, or the path of a file; there is no file " + full + "."
                );
            }

            return McpKit.Prepared(CadDocument.Open(full));
        }

        [McpServerTool(Name = "timings", ReadOnly = true, Destructive = false, Idempotent = true, OpenWorld = false)]
        [Description(
            "Times a thing the drawing makes slow, in milliseconds, to find which: regen (REGEN of the current space: the display list built again), render (a render_png of a layout, with the picture made, nothing returned) or open (reading the drawing's file into a new document in this process). Each run is timed; the first often includes warming up, so ask for 3 or more and read the median. Attached to an app, regen is the app's own time (the request included) and render includes reading the drawing from the app. Nothing is written or changed."
        )]
        public TimingResult Timings(
            [Description("regen, render or open.")] string what,
            [Description("How many times, 1 to 20.")] int runs = 1,
            [Description("The layout a render is of; model space where left out.")] string layout = null
        ) => _session.Run("timings", false, drawing =>
            {
                string kind = (what ?? string.Empty).Trim().ToLowerInvariant();

                if (runs < 1 || runs > 20)
                {
                    throw new ArgumentException("runs is from 1 to 20.");
                }

                string space = Layout(drawing, layout);
                Action once;
                string note;

                switch (kind)
                {
                    case "regen":
                        space = drawing.CurrentLayout;
                        note =
                            "REGEN of " + space + (McpKit.IsApp(drawing) ? ", the request to the app included." : ".");
                        once = drawing is RemoteBackend app
                            ? (Action)(() => app.Document.Regen())
                            : () => ((LocalBackend)drawing).Session.Editor.Regen();
                        break;
                    case "render":
                        note =
                            "A picture of "
                            + space
                            + " at "
                            + RenderOptions.DefaultWidth
                            + " pixels wide, drawn and encoded"
                            + (McpKit.IsApp(drawing) ? ", the drawing read from the app included." : ".");
                        once = () =>
                            drawing.RenderPng(
                                new RenderRequest
                                {
                                    Layout = space,
                                    Box = null,
                                    Width = RenderOptions.DefaultWidth,
                                }
                            );
                        break;
                    case "open":
                        string file = drawing.Path
                            ?? throw new ArgumentException(
                                "The drawing has no file yet: save it first, there is nothing to open."
                            );
                        note = "Reading " + file + " into a new document in this process.";
                        once = () => CadDocument.Open(file);
                        break;
                    default:
                        throw new ArgumentException("what is regen, render or open.");
                }

                var times = new double[runs];

                for (int i = 0; i < runs; i++)
                {
                    var watch = Stopwatch.StartNew();
                    once();
                    watch.Stop();
                    times[i] = Math.Round(watch.Elapsed.TotalMilliseconds, 2);
                }

                double[] sorted = times.OrderBy(t => t).ToArray();
                double median = runs % 2 == 1 ? sorted[runs / 2] : (sorted[runs / 2 - 1] + sorted[runs / 2]) / 2.0;
                return new TimingResult
                {
                    What = kind,
                    Mode = drawing.Mode,
                    Layout = kind == "open" ? null : space,
                    Ms = times,
                    MedianMs = Math.Round(median, 2),
                    Entities = kind == "open" ? 0 : drawing.Entities(space).Count,
                    Note = note,
                };
            });
    }
}
