using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading;
using System.Windows.Automation;
using Xunit;

namespace OpenCAD2D.Wpf.UnitTest
{
    /// <summary>
    /// Marks a test that prints through the real "Microsoft Print to PDF" queue of Windows: it is skipped unless a person asked
    /// for it, and where the computer
    /// has no such printer (a build server, a machine with the feature switched off), and says so.
    /// </summary>
    internal sealed class MicrosoftPrintToPdfFactAttribute : FactAttribute
    {
        internal const string PrinterName = "Microsoft Print to PDF";

        public MicrosoftPrintToPdfFactAttribute()
        {
            if (Environment.GetEnvironmentVariable("OPENCAD2D_PRINT_TO_PDF_TEST") != "1")
            {
                Skip =
                    "Prints through the real queue, whose Save dialog shows on the screen: set OPENCAD2D_PRINT_TO_PDF_TEST=1 to run it.";
            }
            else if (!WindowsPlotPrinter.Installed().Any(p => p.Name == PrinterName))
            {
                Skip = "This computer has no printer named " + PrinterName + ".";
            }
        }
    }

    /// <summary>
    /// Answers the Save Print Output As dialog the port PORTPROMPT of "Microsoft Print to PDF" opens, by UI Automation: it
    /// looks only at the windows of this process (the one the test runs in) and types the file's name into the one it finds
    /// and presses Save. A dialog of any other process is not touched, only noted, so that the test says it was never answered.
    /// </summary>
    internal sealed class SaveDialogAnswerer : IDisposable
    {
        private readonly string _file;
        private readonly int _processId = Process.GetCurrentProcess().Id;
        private readonly Thread _thread;
        private volatile bool _stop;

        internal SaveDialogAnswerer(string file)
        {
            _file = file;
            _thread = new Thread(Run) { IsBackground = true, Name = "Save dialog answerer" };
            _thread.SetApartmentState(ApartmentState.MTA);
            _thread.Start();
        }

        /// <summary>Gets the names of the windows of this process seen, with what was done to each, for the message of a failure.</summary>
        internal List<string> Seen { get; } = new List<string>();

        /// <summary>Gets whether a Save dialog was answered.</summary>
        internal bool Answered { get; private set; }

        /// <summary>Gets the names of Save Print Output dialogs of other processes, which were left alone.</summary>
        internal List<string> Foreign { get; } = new List<string>();

        public void Dispose()
        {
            _stop = true;
            _thread.Join(5000);
        }

        private void Run()
        {
            while (!_stop)
            {
                try
                {
                    foreach (
                        AutomationElement window in AutomationElement.RootElement.FindAll(
                            TreeScope.Children,
                            Condition.TrueCondition
                        )
                    )
                    {
                        string name = window.Current.Name ?? string.Empty;

                        if (name.IndexOf("Save Print Output", StringComparison.OrdinalIgnoreCase) < 0)
                        {
                            continue;
                        }

                        if (window.Current.ProcessId != _processId)
                        {
                            lock (Foreign)
                            {
                                if (!Foreign.Contains(name + " pid " + window.Current.ProcessId))
                                {
                                    Foreign.Add(name + " pid " + window.Current.ProcessId);
                                }
                            }

                            continue;
                        }

                        Answer(window);
                    }
                }
                catch (Exception exception)
                    when (exception is ElementNotAvailableException
                        || exception is InvalidOperationException
                        || exception is System.ComponentModel.Win32Exception
                    )
                {
                    // The dialog closed while it was being read: look again.
                }

                Thread.Sleep(250);
            }
        }

        private void Answer(AutomationElement dialog)
        {
            var edit = dialog.FindFirst(
                TreeScope.Descendants,
                new AndCondition(
                    new PropertyCondition(AutomationElement.ControlTypeProperty, ControlType.Edit),
                    new PropertyCondition(AutomationElement.NameProperty, "File name:")
                )
            );

            AutomationElement save = dialog.FindFirst(
                TreeScope.Descendants,
                new AndCondition(
                    new PropertyCondition(AutomationElement.ControlTypeProperty, ControlType.Button),
                    new PropertyCondition(AutomationElement.AutomationIdProperty, "1")
                )
            );

            lock (Seen)
            {
                Seen.Add("dialog " + dialog.Current.Name + ": edit " + (edit != null) + ", save " + (save != null));
            }

            if (edit == null || save == null)
            {
                return;
            }

            ((ValuePattern)edit.GetCurrentPattern(ValuePattern.Pattern)).SetValue(_file);
            ((InvokePattern)save.GetCurrentPattern(InvokePattern.Pattern)).Invoke();
            Answered = true;
            Thread.Sleep(1000);
        }
    }

    /// <summary>
    /// The plot printed through the Windows print path on "Microsoft Print to PDF" and the PDF it writes read back: one page
    /// of the sheet's size. WPF's print queue has no way to name the output file, so the printer's port opens its Save Print
    /// Output As dialog, a real window on the user's screen: the test therefore runs only when a person asks for it, by
    /// setting the environment variable <c>OPENCAD2D_PRINT_TO_PDF_TEST</c> to 1, and is skipped otherwise. A real paper
    /// printer is the maintainer's to try; this is the same code (WPF's print queue, the print ticket, the page as pixels).
    /// </summary>
    public class PlotWindowsPrinterTests
    {
        [MicrosoftPrintToPdfFact]
        public void ALayoutPrintedOnMicrosoftPrintToPdf_IsOnePageOfTheSheetsSize()
        {
            string folder = Path.Combine(Path.GetTempPath(), "gk", "print-pdf-" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(folder);
            string file = Path.Combine(folder, "plan.pdf");

            try
            {
                Exception failure = null;

                using (var answerer = new SaveDialogAnswerer(file))
                {
                    var thread = new Thread(() =>
                    {
                        try
                        {
                            CadDocument document = PlotPdfTests.Plan();
                            var job = new CadPlotJob(
                                document.ModelSpace,
                                PlotPdfTests.AtOneToTwenty(),
                                DisplayBox.Empty,
                                null
                            );
                            IPlotPrinter printer = WindowsPlotPrinter
                                .Installed()
                                .Single(p => p.Name == MicrosoftPrintToPdfFactAttribute.PrinterName);
                            var dialog = new PlotDialogViewModel(document, job, false, new[] { printer });
                            dialog.Device = dialog.Devices.Single(d => d.Name == printer.Name);
                            dialog.Print();
                        }
                        catch (Exception exception)
                        {
                            failure = exception;
                        }
                    });
                    thread.SetApartmentState(ApartmentState.STA);
                    thread.Start();
                    thread.Join();

                    Assert.True(failure == null, "Printing failed: " + failure);

                    for (int i = 0; i < 240 && !(File.Exists(file) && Stable(file)); i++)
                    {
                        Thread.Sleep(250);
                    }

                    Assert.True(
                        File.Exists(file),
                        "No PDF was written. Save dialogs seen: "
                            + string.Join("; ", answerer.Seen)
                            + ". Save dialogs of other processes (left alone): "
                            + string.Join("; ", answerer.Foreign)
                    );
                }

                string text = Encoding.GetEncoding(28591).GetString(File.ReadAllBytes(file));
                Assert.StartsWith("%PDF-", text, StringComparison.Ordinal);
                Assert.Single(Regex.Matches(text, @"/Type\s*/Page\b").Cast<Match>());
                Match box = Regex.Match(text, @"/MediaBox\s*\[\s*0\s+0\s+([0-9.]+)\s+([0-9.]+)\s*\]");
                Assert.True(box.Success, "No MediaBox.");

                // ISO A3 on its side is 1190.55 by 841.89 points; the driver's own rounding is a point at most.
                Assert.Equal(1190.55, double.Parse(box.Groups[1].Value, CultureInfo.InvariantCulture), 0);
                Assert.Equal(841.89, double.Parse(box.Groups[2].Value, CultureInfo.InvariantCulture), 0);
            }
            finally
            {
                Directory.Delete(folder, true);
            }
        }

        /// <summary>Whether a file has stopped growing for a moment: the driver has done with it.</summary>
        private static bool Stable(string file)
        {
            long before = new FileInfo(file).Length;
            Thread.Sleep(500);
            return before > 0 && new FileInfo(file).Length == before;
        }
    }
}
