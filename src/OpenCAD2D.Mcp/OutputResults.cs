using System.Collections.Generic;

namespace OpenCAD2D.Mcp
{
    /// <summary>What <c>plot_pdf</c> answers.</summary>
    internal sealed class PlotResult
    {
        /// <summary>Gets or sets the full path of the PDF written.</summary>
        public string Path { get; set; }

        public string Layout { get; set; }

        public long Bytes { get; set; }

        /// <summary>Gets or sets the sheet as the plot laid it, <c>[width, height]</c> in millimetres.</summary>
        public double[] Sheet { get; set; }

        /// <summary>Gets or sets the scale the drawing was plotted at, as <c>1:10</c>.</summary>
        public string Scale { get; set; }

        /// <summary>Gets or sets the part of the drawing on the page, <c>[minX, minY, maxX, maxY]</c>; null for an empty page.</summary>
        public double[] Area { get; set; }
    }

    /// <summary>What <c>export_svg</c> answers.</summary>
    internal sealed class FileResult
    {
        public string Path { get; set; }

        public string Layout { get; set; }

        public long Bytes { get; set; }
    }

    /// <summary>What <c>save_as</c> answers.</summary>
    internal sealed class SaveAsResult
    {
        public string Path { get; set; }

        /// <summary>Gets or sets <c>dxf</c> or <c>dwg</c>.</summary>
        public string Format { get; set; }

        /// <summary>Gets or sets the version written, as <c>AC1032</c>.</summary>
        public string Version { get; set; }

        public long Bytes { get; set; }

        /// <summary>Gets or sets what the version could not hold, a sentence each.</summary>
        public IReadOnlyList<string> LeftOut { get; set; }
    }

    /// <summary>What <c>purge</c> answers.</summary>
    internal sealed class PurgeResult
    {
        /// <summary>Gets or sets how many of each kind were purged, kinds in alphabetical order; a kind with none is not listed.</summary>
        public SortedDictionary<string, int> Purged { get; set; }

        public int Total { get; set; }

        /// <summary>Gets or sets what the command line said.</summary>
        public IReadOnlyList<string> Messages { get; set; }
    }

    /// <summary>What <c>audit</c> answers.</summary>
    internal sealed class AuditResult
    {
        /// <summary>Gets or sets how many errors the audit found.</summary>
        public int Found { get; set; }

        /// <summary>Gets or sets whether a fix was asked for.</summary>
        public bool Fixed { get; set; }

        /// <summary>Gets or sets how many errors are still there after the audit: all of them without a fix.</summary>
        public int Remaining { get; set; }

        /// <summary>Gets or sets how many errors of each kind were found, kinds in alphabetical order.</summary>
        public SortedDictionary<string, int> Kinds { get; set; }

        /// <summary>Gets or sets the first sentences the audit said, up to 20.</summary>
        public IReadOnlyList<string> Messages { get; set; }
    }

    /// <summary>What <c>zoom_extents</c>, <c>zoom_window</c> and <c>zoom_object</c> answer.</summary>
    internal sealed class ZoomResult
    {
        /// <summary>Gets or sets <c>app</c> for the view of the running app, <c>render</c> for the view <c>render_png</c> draws.</summary>
        public string View { get; set; }

        /// <summary>Gets or sets the part of the drawing now shown, <c>[minX, minY, maxX, maxY]</c>; null for an empty layout.</summary>
        public double[] Box { get; set; }
    }

    /// <summary>What <c>command_history</c> answers.</summary>
    internal sealed class HistoryResult
    {
        /// <summary>Gets or sets the lines, the oldest first.</summary>
        public IReadOnlyList<string> Lines { get; set; }

        /// <summary>Gets or sets how many lines the command line holds in all.</summary>
        public int Total { get; set; }

        public bool Running { get; set; }

        /// <summary>Gets or sets the prompt a running command waits at.</summary>
        public string Prompt { get; set; }
    }

    /// <summary>One fault in <c>last_errors</c>.</summary>
    internal sealed class ErrorRow
    {
        public string Time { get; set; }

        /// <summary>Gets or sets where it came from: <c>crash log</c>, <c>app request</c> or <c>tool</c>.</summary>
        public string Source { get; set; }

        /// <summary>Gets or sets the command the app ran last, the request it could not answer or the tool that failed.</summary>
        public string Where { get; set; }

        public bool Fatal { get; set; }

        /// <summary>Gets or sets the message and the head of the stack.</summary>
        public IReadOnlyList<string> Text { get; set; }
    }

    /// <summary>What <c>last_errors</c> answers.</summary>
    internal sealed class ErrorsResult
    {
        /// <summary>Gets or sets the file the app writes its crash log to; null in file mode or where the app writes none.</summary>
        public string CrashLog { get; set; }

        /// <summary>Gets or sets the app's faults: its crash log entries then the requests it could not answer, each newest first.</summary>
        public IReadOnlyList<ErrorRow> App { get; set; }

        /// <summary>Gets or sets the tool calls this server could not do, the newest first.</summary>
        public IReadOnlyList<ErrorRow> Server { get; set; }

        /// <summary>Gets or sets a sentence where the app could not be asked (a version that does not know the request).</summary>
        public string Note { get; set; }
    }

    /// <summary>What <c>dump_drawing</c> answers.</summary>
    internal sealed class DumpResult
    {
        public string Path { get; set; }

        public long Bytes { get; set; }

        public string Mode { get; set; }
    }

    /// <summary>One object in <c>compare_drawings</c>.</summary>
    internal sealed class ChangeRow
    {
        public string Handle { get; set; }

        public string Kind { get; set; }

        public string Layout { get; set; }

        public string Layer { get; set; }

        /// <summary>Gets or sets the names of what differs, for a changed object.</summary>
        public IReadOnlyList<string> Properties { get; set; }

        /// <summary>Gets or sets the box before, <c>[minX, minY, maxX, maxY]</c>; null for an added object.</summary>
        public double[] BoxBefore { get; set; }

        /// <summary>Gets or sets the box after; null for a removed object.</summary>
        public double[] BoxAfter { get; set; }

        /// <summary>Gets or sets how far the box moved, <c>[dx, dy]</c>, where it kept its size and moved.</summary>
        public double[] MovedBy { get; set; }
    }

    /// <summary>What <c>compare_drawings</c> answers.</summary>
    internal sealed class DiffResult
    {
        public string A { get; set; }

        public string B { get; set; }

        public bool Same { get; set; }

        public int AddedCount { get; set; }

        public int RemovedCount { get; set; }

        public int ChangedCount { get; set; }

        public int Unchanged { get; set; }

        /// <summary>Gets or sets whether a list was cut at <c>max_rows</c>.</summary>
        public bool Truncated { get; set; }

        public IReadOnlyList<ChangeRow> Added { get; set; }

        public IReadOnlyList<ChangeRow> Removed { get; set; }

        public IReadOnlyList<ChangeRow> Changed { get; set; }
    }

    /// <summary>What <c>timings</c> answers.</summary>
    internal sealed class TimingResult
    {
        public string What { get; set; }

        public string Mode { get; set; }

        /// <summary>Gets or sets the layout a render was of; null for the others.</summary>
        public string Layout { get; set; }

        /// <summary>Gets or sets the milliseconds of each run, in order; the first often includes warming up.</summary>
        public double[] Ms { get; set; }

        public double MedianMs { get; set; }

        /// <summary>Gets or sets how many entities the drawing's space holds, to read the time against.</summary>
        public int Entities { get; set; }

        /// <summary>Gets or sets what the time includes.</summary>
        public string Note { get; set; }
    }
}
