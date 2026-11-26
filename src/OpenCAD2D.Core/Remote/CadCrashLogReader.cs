using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;

namespace OpenCAD2D
{
    /// <summary>One entry of the app's crash log: when it was written, where the exception was caught, the command running then and the head of the exception.</summary>
    public sealed class CadCrashLogEntry
    {
        internal CadCrashLogEntry(
            string time,
            string caughtBy,
            string lastCommand,
            bool fatal,
            IReadOnlyList<string> head
        )
        {
            Time = time;
            CaughtBy = caughtBy;
            LastCommand = lastCommand;
            Fatal = fatal;
            Head = head;
        }

        /// <summary>Gets the time as the log wrote it, <c>2026-11-26 14:03:11.250 +07:00</c>.</summary>
        public string Time { get; }

        /// <summary>Gets where the exception was caught, as <c>Application.DispatcherUnhandledException</c>.</summary>
        public string CaughtBy { get; }

        /// <summary>Gets the name of the command run last, or an empty text for none.</summary>
        public string LastCommand { get; }

        /// <summary>Gets whether the app could not go on after it (out of memory, a fault of native code).</summary>
        public bool Fatal { get; }

        /// <summary>Gets the first lines of the exception and its stack, the exception's own message first.</summary>
        public IReadOnlyList<string> Head { get; }
    }

    /// <summary>
    /// Reads the crash log the app writes (<c>%AppData%\OpenCAD2D\crash.log</c>, the older half <c>crash.old.log</c>): a file of
    /// entries that begin <c>==== time ====</c>, then the app, the system, where it was caught, the last command, whether it was
    /// fatal and the exception as .NET prints it.
    /// </summary>
    public static class CadCrashLogReader
    {
        /// <summary>The lines of an entry's exception that are kept: its message and the top of its stack.</summary>
        public const int HeadLines = 8;

        /// <summary>
        /// Reads the newest entries, the newest first: those of the file, then, where it holds fewer than asked for, those of the
        /// file of the same name with <c>.old</c> before its extension.
        /// </summary>
        /// <param name="path">The crash log; a file that is not there gives no entries.</param>
        /// <param name="count">The most entries to give; above nought.</param>
        /// <returns>The entries, newest first.</returns>
        /// <exception cref="ArgumentNullException">Thrown when the path is null.</exception>
        /// <exception cref="ArgumentOutOfRangeException">Thrown when the count is not above nought.</exception>
        public static IReadOnlyList<CadCrashLogEntry> Read(string path, int count)
        {
            if (path == null)
            {
                throw new ArgumentNullException(nameof(path));
            }

            if (count < 1)
            {
                throw new ArgumentOutOfRangeException(nameof(count), "Ask for at least one entry.");
            }

            var found = new List<CadCrashLogEntry>();
            string old = Path.Combine(
                Path.GetDirectoryName(Path.GetFullPath(path)),
                Path.GetFileNameWithoutExtension(path) + ".old" + Path.GetExtension(path)
            );

            foreach (string file in new[] { path, old })
            {
                if (found.Count >= count || !File.Exists(file))
                {
                    continue;
                }

                try
                {
                    List<CadCrashLogEntry> entries = Parse(File.ReadAllText(file, Encoding.UTF8));
                    entries.Reverse();
                    found.AddRange(entries.Take(count - found.Count));
                }
                catch (Exception ex) when (ex is IOException || ex is UnauthorizedAccessException)
                {
                    // A log the app is writing this moment, or one the user may not read: the other file still answers.
                }
            }

            return found;
        }

        /// <summary>Splits the text of a crash log into its entries, oldest first.</summary>
        internal static List<CadCrashLogEntry> Parse(string text)
        {
            var entries = new List<CadCrashLogEntry>();
            string[] lines = text.Replace("\r\n", "\n").Replace('\r', '\n').Split('\n');
            int i = 0;

            while (i < lines.Length)
            {
                if (!IsStart(lines[i]))
                {
                    i++;
                    continue;
                }

                string time = lines[i].Trim('=', ' ');
                var body = new List<string>();

                for (i++; i < lines.Length && !IsStart(lines[i]); i++)
                {
                    body.Add(lines[i]);
                }

                entries.Add(Entry(time, body));
            }

            return entries;
        }

        private static bool IsStart(string line) => line.StartsWith("==== ", StringComparison.Ordinal);

        private static CadCrashLogEntry Entry(string time, List<string> body)
        {
            string caught = Field(body, "Caught by: ");
            string last = Field(body, "Last command: ");
            bool fatal = Field(body, "Fatal: ") == "yes";
            int start = body.FindIndex(l => l.StartsWith("Fatal: ", StringComparison.Ordinal)) + 1;
            List<string> head = body.Skip(start)
                .Where(l => l.Trim().Length > 0)
                .Take(HeadLines)
                .Select(l => l.TrimEnd())
                .ToList();
            return new CadCrashLogEntry(time, caught, last == "(none)" ? string.Empty : last, fatal, head);
        }

        private static string Field(List<string> body, string label)
        {
            string line = body.FirstOrDefault(l => l.StartsWith(label, StringComparison.Ordinal));
            return line == null ? string.Empty : line.Substring(label.Length).Trim();
        }
    }
}
