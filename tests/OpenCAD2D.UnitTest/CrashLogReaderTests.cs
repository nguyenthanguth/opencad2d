using System;
using System.IO;
using System.Linq;
using Xunit;

namespace OpenCAD2D.UnitTest
{
    /// <summary>Reading the crash log the app writes: the entries newest first, from the log and from the older half, and what each says.</summary>
    public class CrashLogReaderTests : IDisposable
    {
        private readonly string _folder = Path.Combine(
            Path.GetTempPath(),
            "OpenCAD2D.UnitTest",
            "crashlog-" + Guid.NewGuid().ToString("N")
        );

        public CrashLogReaderTests() => Directory.CreateDirectory(_folder);

        public void Dispose()
        {
            try
            {
                Directory.Delete(_folder, true);
            }
            catch (IOException) { }
            catch (UnauthorizedAccessException) { }
        }

        private static string Entry(string time, string caught, string command, bool fatal, string message) =>
            "==== "
            + time
            + " ====\n"
            + "App: OpenCAD2D 2.0.0.0, .NET 8.0.11\n"
            + "OS: Microsoft Windows 10.0.26100\n"
            + "Caught by: "
            + caught
            + "\n"
            + "Last command: "
            + command
            + "\n"
            + "Fatal: "
            + (fatal ? "yes" : "no")
            + "\n"
            + "System.InvalidOperationException: "
            + message
            + "\n"
            + "   at OpenCAD2D.CadEditor.Move()\n"
            + "   at OpenCAD2D.Wpf.Canvas.OnClick()\n"
            + "\n";

        [Fact]
        public void TheEntriesCome_NewestFirst_WithWhereTheyWereCaughtTheCommandAndTheHeadOfTheException()
        {
            string path = Path.Combine(_folder, "crash.log");
            File.WriteAllText(
                path,
                Entry(
                    "2026-11-26 10:00:00.000 +07:00",
                    "Application.DispatcherUnhandledException",
                    "MOVE",
                    false,
                    "first"
                )
                    + Entry(
                        "2026-11-26 11:30:00.500 +07:00",
                        "TaskScheduler.UnobservedTaskException",
                        "(none)",
                        true,
                        "second"
                    )
            );

            var entries = CadCrashLogReader.Read(path, 10);

            Assert.Equal(2, entries.Count);
            Assert.Equal("2026-11-26 11:30:00.500 +07:00", entries[0].Time);
            Assert.Equal("TaskScheduler.UnobservedTaskException", entries[0].CaughtBy);
            Assert.Equal(string.Empty, entries[0].LastCommand);
            Assert.True(entries[0].Fatal);
            Assert.Equal("System.InvalidOperationException: second", entries[0].Head[0]);
            Assert.Equal("at OpenCAD2D.CadEditor.Move()", entries[0].Head[1].Trim());
            Assert.Equal("MOVE", entries[1].LastCommand);
            Assert.False(entries[1].Fatal);
            Assert.Single(CadCrashLogReader.Read(path, 1));
        }

        [Fact]
        public void WhereTheLogHoldsFewerThanAskedFor_TheOlderHalfFollows()
        {
            string path = Path.Combine(_folder, "crash.log");
            File.WriteAllText(path, Entry("2026-11-26 12:00:00.000 +07:00", "B", "LINE", false, "new"));
            File.WriteAllText(
                Path.Combine(_folder, "crash.old.log"),
                Entry("2026-11-25 08:00:00.000 +07:00", "A", "ZOOM", false, "old1")
                    + Entry("2026-11-25 09:00:00.000 +07:00", "A", "PAN", false, "old2")
            );

            var entries = CadCrashLogReader.Read(path, 3);

            Assert.Equal(new[] { "new", "old2", "old1" }, entries.Select(e => e.Head[0].Split(' ').Last()).ToArray());
            Assert.Equal(2, CadCrashLogReader.Read(path, 2).Count);
        }

        [Fact]
        public void ALongStack_IsCutAtTheHead_AndAMissingFileGivesNothing()
        {
            string path = Path.Combine(_folder, "crash.log");
            File.WriteAllText(
                path,
                Entry("2026-11-26 10:00:00.000 +07:00", "A", "X", false, "long")
                    .Replace(
                        "   at OpenCAD2D.Wpf.Canvas.OnClick()\n",
                        string.Concat(Enumerable.Range(0, 30).Select(i => "   at Frame" + i + "()\n"))
                    )
            );

            Assert.Equal(CadCrashLogReader.HeadLines, CadCrashLogReader.Read(path, 1)[0].Head.Count);
            Assert.Empty(CadCrashLogReader.Read(Path.Combine(_folder, "nothing.log"), 5));
        }

        [Fact]
        public void ANullPathOrACountBelowOneIsRefused()
        {
            Assert.Throws<ArgumentNullException>(() => CadCrashLogReader.Read(null, 1));
            Assert.Throws<ArgumentOutOfRangeException>(() => CadCrashLogReader.Read(Path.Combine(_folder, "x.log"), 0));
        }
    }
}
