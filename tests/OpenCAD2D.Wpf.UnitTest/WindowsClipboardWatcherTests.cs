using System;
using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Threading;
using System.Windows;
using Xunit;

namespace OpenCAD2D.Wpf.UnitTest
{
    /// <summary>
    /// A program that watches the clipboard (clipboard history, a remote-desktop or phone sync, a screenshot tool) opens it
    /// at every change and asks for the text, and Windows puts that question to the program that set the clipboard: its
    /// thread has to answer before the watcher lets go. A copy that only slept between its tries never answered, so on
    /// such a machine it failed every time.
    /// </summary>
    public sealed class WindowsClipboardWatcherTests
    {
        private const uint UnicodeText = 13;

        [DllImport("user32.dll")]
        private static extern uint GetClipboardSequenceNumber();

        [DllImport("user32.dll", SetLastError = true)]
        private static extern bool OpenClipboard(IntPtr window);

        [DllImport("user32.dll", SetLastError = true)]
        private static extern bool CloseClipboard();

        [DllImport("user32.dll")]
        private static extern IntPtr GetClipboardData(uint format);

        [DllImport("kernel32.dll")]
        private static extern IntPtr GlobalLock(IntPtr memory);

        [DllImport("kernel32.dll")]
        private static extern bool GlobalUnlock(IntPtr memory);

        [Fact]
        public void ACopyWhileAWatcherAsksForTheText_GoesThroughAndTheWatcherGetsTheText()
        {
            string watched = null;
            bool set = false;
            string read = null;

            Sta(() =>
                RealClipboard.Preserving(() =>
                {
                    var watcher = new Thread(() => watched = WatchForText()) { IsBackground = true };
                    watcher.Start();
                    Thread.Sleep(100);

                    set = CadWindowsClipboard.TrySetText("watched text");
                    read = CadWindowsClipboard.TryGetText();

                    Assert.True(watcher.Join(10000), "The watcher never got its answer.");
                })
            );

            Assert.True(set);
            Assert.Equal("watched text", read);
            Assert.Equal("watched text", watched);
        }

        /// <summary>
        /// What such a program does once the clipboard has changed: opens it with no window, asks for the Unicode text and
        /// closes it, again and again until it has the text. The text is the one that was set, which only the setting thread can give while it
        /// has not flushed.
        /// </summary>
        private static string WatchForText()
        {
            uint seen = GetClipboardSequenceNumber();
            var clock = Stopwatch.StartNew();

            while (clock.ElapsedMilliseconds < 8000)
            {
                if (GetClipboardSequenceNumber() == seen || !OpenClipboard(IntPtr.Zero))
                {
                    continue;
                }

                try
                {
                    IntPtr data = GetClipboardData(UnicodeText);
                    IntPtr memory = data == IntPtr.Zero ? IntPtr.Zero : GlobalLock(data);

                    if (memory != IntPtr.Zero)
                    {
                        string text = Marshal.PtrToStringUni(memory);
                        GlobalUnlock(data);
                        return text;
                    }
                }
                finally
                {
                    CloseClipboard();
                }
            }

            return null;
        }

        private static void Sta(Action action)
        {
            Exception failure = null;
            var thread = new Thread(() =>
            {
                try
                {
                    action();
                }
                catch (Exception exception)
                {
                    failure = exception;
                }
            });
            thread.SetApartmentState(ApartmentState.STA);
            thread.Start();
            thread.Join();

            if (failure != null)
            {
                throw new Xunit.Sdk.XunitException(failure.ToString());
            }
        }
    }

    /// <summary>
    /// A test that writes the real clipboard puts back the text it found there, so that it does not destroy what the person at
    /// the desk had copied (a picture or files copied cannot be put back). Runs on an STA thread.
    /// </summary>
    internal static class RealClipboard
    {
        public static void Preserving(Action action)
        {
            string before = CadWindowsClipboard.TryGetText();

            try
            {
                action();
            }
            finally
            {
                if (before != null)
                {
                    CadWindowsClipboard.TrySetText(before);
                }
                else
                {
                    CadWindowsClipboard.Retry(Clipboard.Clear);
                }
            }
        }
    }
}
