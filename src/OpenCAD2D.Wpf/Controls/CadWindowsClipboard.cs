using System;
using System.Diagnostics;
using System.IO;
using System.Runtime.InteropServices;
using System.Threading;
using System.Windows;
using System.Windows.Media.Imaging;
using GeometryHelper;

namespace OpenCAD2D.Wpf
{
    /// <summary>
    /// The Windows clipboard as the presenter's (<see cref="ICadClipboard"/>): objects go under the private format
    /// <see cref="CadClipboardData.Format"/>, so that any copy of the app reads them, and their words as Unicode text, which
    /// every other app reads; text another app put there comes back as text, and a picture as the bytes of a PNG. Windows may hold the clipboard for a moment for
    /// another app: each call tries a few times before it gives up, and a call that gives up is logged and reads as empty.
    /// A program that watches the clipboard asks the app that set it for the text, and the app answers only while its
    /// thread takes messages, so every wait between tries answers them.
    /// </summary>
    public sealed class CadWindowsClipboard : ICadClipboard
    {
        private const int Tries = 5;

        /// <summary>The wait between the tries of the flush, as the clipboard of WPF has it.</summary>
        private const int FlushWait = 100;

        private const int FlushTries = 10;

        /// <summary>PM_NOREMOVE | PM_QS_SENDMESSAGE: look at no queue but the messages other threads and programs send.</summary>
        private const uint SentMessagesOnly = 0x00200000;

        [StructLayout(LayoutKind.Sequential)]
        private struct Message
        {
            public IntPtr Window;
            public uint Id;
            public IntPtr WParam;
            public IntPtr LParam;
            public uint Time;
            public int X;
            public int Y;
        }

        [DllImport("ole32.dll")]
        private static extern int OleFlushClipboard();

        [DllImport("user32.dll")]
        private static extern bool PeekMessage(out Message message, IntPtr window, uint min, uint max, uint remove);

        /// <inheritdoc/>
        public void SetData(CadClipboardData data)
        {
            if (data == null)
            {
                return;
            }

            var dataObject = new DataObject();
            string format = data.ToFormat();

            if (format != null)
            {
                dataObject.SetData(CadClipboardData.Format, format);
            }

            if (data.Text != null)
            {
                dataObject.SetData(DataFormats.UnicodeText, data.Text);
            }

            Retry(() => SetAndFlush(dataObject));
        }

        /// <inheritdoc/>
        public CadClipboardData GetData()
        {
            CadClipboardData read = null;
            Retry(() =>
            {
                IDataObject held = Clipboard.GetDataObject();

                if (held == null)
                {
                    read = null;
                    return;
                }

                string format = held.GetDataPresent(CadClipboardData.Format)
                    ? held.GetData(CadClipboardData.Format) as string
                    : null;
                string text = held.GetDataPresent(DataFormats.UnicodeText)
                    ? held.GetData(DataFormats.UnicodeText) as string
                    : null;
                read = CadClipboardData.FromFormat(format, string.IsNullOrEmpty(text) ? null : text);

                // A picture another app copied, taken as a PNG, which PASTECLIP pastes where there are no objects and no text.
                if (read == null || (!read.HasObjects && read.Text == null))
                {
                    byte[] picture = PictureOf(held);

                    if (picture != null)
                    {
                        read = new CadClipboardData(null, null, picture: picture);
                    }
                }
            });

            return read;
        }

        /// <summary>The bitmap the clipboard holds written as a PNG, with its alpha; null where it holds none or Windows cannot give it.</summary>
        private static byte[] PictureOf(IDataObject held)
        {
            try
            {
                if (
                    !held.GetDataPresent(DataFormats.Bitmap)
                    || !(held.GetData(DataFormats.Bitmap) is BitmapSource image)
                )
                {
                    return null;
                }

                var encoder = new PngBitmapEncoder();
                encoder.Frames.Add(BitmapFrame.Create(image));

                using (var memory = new MemoryStream())
                {
                    encoder.Save(memory);
                    return memory.ToArray();
                }
            }
            catch (Exception exception)
                when (exception is COMException
                    || exception is ArgumentException
                    || exception is NotSupportedException
                    || exception is InvalidOperationException
                    || exception is IOException
                )
            {
                GeometryHelperLog.Debug(
                    "CadWindowsClipboard: the picture on the clipboard could not be read (" + exception.Message + ")."
                );
                return null;
            }
        }

        /// <summary>
        /// Puts plain text on the clipboard as every other app reads it, trying again while another app holds the
        /// clipboard; false, logged, where it stayed busy. Null is taken as empty text.
        /// </summary>
        internal static bool TrySetText(string text) =>
            Retry(() => SetAndFlush(new DataObject(DataFormats.UnicodeText, text ?? string.Empty)));

        /// <summary>The text on the clipboard, trying again while another app holds it; null where there is none or it stayed busy.</summary>
        internal static string TryGetText()
        {
            string text = null;
            Retry(() => text = Clipboard.ContainsText() ? Clipboard.GetText() : null);
            return text;
        }

        /// <summary>
        /// <c>Clipboard.SetDataObject(data, true)</c>, written out so that the waits answer the messages sent to this thread:
        /// the clipboard is set, then flushed (the data leaves the app, so that it stays on the clipboard after the app is
        /// closed) until Windows lets the flush open the clipboard. A program that watches the clipboard holds it open while
        /// it asks this thread for the text, so a flush that only slept never got in, where one that answers does at the
        /// second try, about 100 ms later.
        /// </summary>
        private static void SetAndFlush(DataObject data)
        {
            Clipboard.SetDataObject(data, false);

            for (int i = 1; ; i++)
            {
                int result = OleFlushClipboard();

                if (result >= 0)
                {
                    return;
                }

                if (i == FlushTries)
                {
                    Marshal.ThrowExceptionForHR(result);
                }

                Wait(FlushWait);
            }
        }

        /// <summary>Waits the given milliseconds, answering what other programs send to this thread while it does.</summary>
        private static void Wait(int milliseconds)
        {
            var clock = Stopwatch.StartNew();

            do
            {
                PeekMessage(out _, IntPtr.Zero, 0, 0, SentMessagesOnly);
                Thread.Sleep(2);
            } while (clock.ElapsedMilliseconds < milliseconds);
        }

        /// <summary>
        /// Runs a clipboard call, trying it again a few times when Windows reports the clipboard held by another app
        /// (CLIPBRD_E_CANT_OPEN comes as a COMException); true when it went through, false, logged, when it stayed busy.
        /// </summary>
        internal static bool Retry(Action action)
        {
            for (int i = 1; ; i++)
            {
                try
                {
                    action();
                    return true;
                }
                catch (COMException exception) when (i < Tries)
                {
                    GeometryHelperLog.Debug(
                        "CadWindowsClipboard: the clipboard was busy (" + exception.Message + "), tried again."
                    );
                    Wait(20);
                }
                catch (COMException exception)
                {
                    GeometryHelperLog.Warn("CadWindowsClipboard: the clipboard stayed busy.", exception);
                    return false;
                }
            }
        }
    }
}
