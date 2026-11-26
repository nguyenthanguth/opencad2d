using System;
using System.Linq;
using Xunit;

namespace OpenCAD2D.Wpf.UnitTest
{
    /// <summary>
    /// -PLOT on the command line choosing a printer of Windows by name, as AutoCAD's does: the device prompt takes an installed
    /// printer and lists them with ?, the paper size prompt takes a sheet of that printer's list, a file is not asked (and not
    /// written), and the plot goes to the printer. The printers are queues that print nothing.
    /// </summary>
    public class PlotPrinterCommandLineTests
    {
        private static MainViewModel Open(TestFolder folder, params FakePlotPrinter[] printers)
        {
            var model = new MainViewModel();
            Assert.True(model.Open(folder.Write("plan-level2.dxf", PlotPdfTests.Plan().Database)));
            model.WindowsPrinters = () => printers;
            return model;
        }

        /// <summary>Answers each prompt with Enter until the prompt starts with the words given.</summary>
        private static void EnterUntil(MainViewModel model, string prompt)
        {
            for (int i = 0; i < 40 && !model.Session.PromptText.StartsWith(prompt, StringComparison.Ordinal); i++)
            {
                model.Execute(string.Empty);
            }

            Assert.StartsWith(prompt, model.Session.PromptText);
        }

        [Fact]
        public void TheDeviceList_NamesTheInstalledPrintersAfterThePresentersOwn()
        {
            using (var folder = new TestFolder())
            {
                MainViewModel model = Open(
                    folder,
                    new FakePlotPrinter("Office printer"),
                    new FakePlotPrinter("Plotter 2")
                );

                model.Execute("-PLOT");
                model.Execute("Y");
                model.Execute(string.Empty);
                Assert.Equal("Enter an output device name or [?] <OpenCAD2D PDF>:", model.Session.PromptText);
                model.Execute("?");

                string[] history = model.History.ToArray();
                int pdf = Array.LastIndexOf(history, "OpenCAD2D PDF");
                Assert.Equal(
                    new[] { "OpenCAD2D PDF", "OpenCAD2D PNG", "OpenCAD2D JPG", "Office printer", "Plotter 2" },
                    history.Skip(pdf).Take(5).ToArray()
                );
            }
        }

        [Fact]
        public void ADeviceOfNoSuchName_IsNotFound_AndAskedAgain_WhateverTheCaseOfAPrinterThatIsThere()
        {
            using (var folder = new TestFolder())
            {
                MainViewModel model = Open(folder, new FakePlotPrinter("Office printer"));

                model.Execute("-PLOT");
                model.Execute("Y");
                model.Execute(string.Empty);
                model.Execute("Warehouse printer");

                Assert.Contains("not found", model.History.Last());
                Assert.StartsWith("Enter an output device name or [?]", model.Session.PromptText);

                model.Execute("OFFICE PRINTER");

                Assert.StartsWith("Enter paper size or [?] <", model.Session.PromptText);
            }
        }

        [Fact]
        public void WithNoPrinters_ThePresentersOwnPlottersAreAllThereIs_AsBefore()
        {
            using (var folder = new TestFolder())
            {
                MainViewModel model = Open(folder);

                model.Execute("-PLOT");
                model.Execute("Y");
                model.Execute(string.Empty);
                model.Execute("Office printer");

                Assert.Equal(
                    "Printer/plotter \"Office printer\" not found: the presenter plots with OpenCAD2D PDF, OpenCAD2D PNG or OpenCAD2D JPG.",
                    model.History.Last()
                );
            }
        }

        [Fact]
        public void ThePaperSizeOfAPrinter_IsOneOfItsOwnSheets_ByName_AndListedByName()
        {
            using (var folder = new TestFolder())
            {
                var printer = new FakePlotPrinter("Office printer");
                MainViewModel model = Open(folder, printer);

                model.Execute("-PLOT");
                model.Execute("Y");
                model.Execute(string.Empty);
                model.Execute("Office printer");

                // The plot was on ISO A3 on its side; the printer has an A3 (297 by 420) and the plot stays on it.
                Assert.Equal("Enter paper size or [?] <A3>:", model.Session.PromptText);
                model.Execute("?");
                string[] history = model.History.ToArray();
                int a4 = Array.LastIndexOf(history, "A4");
                Assert.Equal(new[] { "A4", "A3", "Letter" }, history.Skip(a4).Take(3).ToArray());

                model.Execute("ISO A3");
                Assert.Contains("not found", model.History.Last());

                model.Execute("letter");
                Assert.Equal("Enter paper units [Inches/Millimeters] <Millimeters>:", model.Session.PromptText);
            }
        }

        [Fact]
        public void APlotOnAPrinter_AsksNoFile_RefusesAFile_AndPrintsOnTheSheetChosen()
        {
            using (var folder = new TestFolder())
            {
                var printer = new FakePlotPrinter("Office printer");
                MainViewModel model = Open(folder, printer);

                model.Execute("-PLOT");
                model.Execute("Y");
                model.Execute(string.Empty);
                model.Execute("Office printer");
                model.Execute("A4");
                EnterUntil(model, "Write the plot to a file [Yes/No] <No>:");
                model.Execute("Y");

                Assert.Equal("Plot to file is not supported for this printer.", model.History.Last());
                Assert.Equal("Write the plot to a file [Yes/No] <No>:", model.Session.PromptText);
                model.Execute("N");
                Assert.Equal("Save changes to page setup [Yes/No] <No>:", model.Session.PromptText);
                model.Execute(string.Empty);
                Assert.Equal("Proceed with plot [Yes/No] <Yes>:", model.Session.PromptText);
                model.Execute(string.Empty);

                Assert.False(model.Session.IsRunning);
                PlotPrintJob job = Assert.Single(printer.Jobs);
                Assert.Equal("A4", job.Paper.Name);
                Assert.Equal(1, job.Copies);
                Assert.Contains(model.History, line =>
                    line.StartsWith("Effective plotting area:", StringComparison.Ordinal)
                );
                Assert.Equal("Plot sent to Office printer.", model.History.Last());
            }
        }

        [Fact]
        public void WithoutTheDetailedConfiguration_APrinterIsNamedAtTheDevicePrompt_AndPrintsOnItsSheetOfTheSize()
        {
            using (var folder = new TestFolder())
            {
                var printer = new FakePlotPrinter("Office printer");
                MainViewModel model = Open(folder, printer);

                model.Execute("-PLOT");
                model.Execute("N");
                model.Execute(string.Empty);
                model.Execute(string.Empty);
                Assert.StartsWith("Enter an output device name or [?]", model.Session.PromptText);
                model.Execute("Office printer");
                Assert.Equal("Write the plot to a file [Yes/No] <No>:", model.Session.PromptText);
                model.Execute(string.Empty);
                model.Execute(string.Empty);
                model.Execute(string.Empty);

                PlotPrintJob job = Assert.Single(printer.Jobs);
                Assert.Equal("A3", job.Paper.Name);
                Assert.Equal(420.0, job.SheetWidth);
                Assert.Equal(297.0, job.SheetHeight);
                Assert.Equal("Plot sent to Office printer.", model.History.Last());
            }
        }

        [Fact]
        public void APrinterThatFails_IsSaidOnTheCommandLine_ByMinus_Plot()
        {
            using (var folder = new TestFolder())
            {
                var printer = new FakePlotPrinter("Office printer")
                {
                    FailWith = new InvalidOperationException("The queue is paused."),
                };
                MainViewModel model = Open(folder, printer);

                model.Execute("-PLOT");
                model.Execute("N");
                model.Execute(string.Empty);
                model.Execute(string.Empty);
                model.Execute("Office printer");
                model.Execute(string.Empty);
                model.Execute(string.Empty);
                model.Execute(string.Empty);

                Assert.Equal("The plot was not printed: The queue is paused.", model.History.Last());
                Assert.False(model.Session.IsRunning);
            }
        }

        [Fact]
        public void APageSetupThatNamesAnInstalledPrinter_OffersItAsTheDevice()
        {
            using (var folder = new TestFolder())
            {
                CadDocument plan = PlotPdfTests.Plan();
                plan.Database.Layouts.Single(l => l.ModelType).PlotConfigurationName = "Office printer";
                var model = new MainViewModel();
                Assert.True(model.Open(folder.Write("plan-level2.dxf", plan.Database)));
                model.WindowsPrinters = () => new[] { new FakePlotPrinter("Office printer") };

                model.Execute("-PLOT");
                model.Execute("Y");
                model.Execute(string.Empty);

                Assert.Equal("Enter an output device name or [?] <Office printer>:", model.Session.PromptText);
            }
        }
    }
}
