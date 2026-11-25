using System;
using System.Collections.Generic;
using System.IO;
using System.IO.Packaging;
using System.Linq;
using System.Threading;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using GeometryHelper.Dxf;
using OpenCAD2D.SheetSets;
using Xunit;

namespace OpenCAD2D.Wpf.UnitTest
{
    /// <summary>
    /// The windows and the entry points of a sheet set publish (A12 5a), driven with no window shown: Sheet Set PDF Publish
    /// Options and what OK saves in the set, the progress window's state, and the main view model's Publish entries (the
    /// Publish items of the Sheet List call them) with the dialogs stubbed, down to the report on the command line.
    /// </summary>
    public sealed class SheetSetPublishWindowTests
    {
        /// <summary>A set with two sheets, each a layout of a drawing of its own, and its folder.</summary>
        private static CadSheetSetFile TwoSheets(TestFolder folder)
        {
            string Drawing(string name)
            {
                DxfDatabase database = CadTemplate.CreateMetric();
                database.Layouts.Single(l => l.LayoutName == "Layout1").LayoutName = "Sheet";
                database.ModelSpace.AppendEntity(
                    new DxfLine(
                        new GeometryHelper.Geometry.GeoPoint3(0, 0, 0),
                        new GeometryHelper.Geometry.GeoPoint3(1000, 500, 0)
                    )
                );
                string path = Path.Combine(folder.FullPath, name);
                CadDocument document = CadDocument.From(database, path);
                CadSpace space = document.Spaces.Single(s => s.Name == "Sheet");
                CadPlotSettings settings = CadPlotSettings.Read(document, space);
                settings.Paper = CadPaper.Standard[4];
                settings.Landscape = true;
                settings.Area = CadPlotArea.Layout;
                Assert.True(settings.ApplyTo(document, space).Succeeded);
                document.SaveAs(path);
                return path;
            }

            CadSheetSetFile file = CadSheetSetFile.Create(
                Path.Combine(folder.FullPath, "Tower.dst"),
                "Example Tower",
                null
            );
            file.Change(set =>
            {
                CadSheetSetSheet first = set.ImportLayouts(Drawing("one.dwg"), new[] { "Sheet" }, false)[0];
                first.Number = "A-101";
                first.Title = "Ground Floor Plan";
                CadSheetSetSheet second = set.ImportLayouts(Drawing("two.dwg"), new[] { "Sheet" }, false)[0];
                second.Number = "A-102";
                second.Title = "First Floor Plan";
                set.PublishOptions.PromptForName = false;
            });
            return file;
        }

        private static string Line(MainViewModel model, string start) =>
            model.History.FirstOrDefault(l => l.StartsWith(start, StringComparison.Ordinal));

        [Fact]
        public void TheOptionsDialog_StartsFromTheSet_AndOkSavesLocationPromptAndMultiSheetInItsFile()
        {
            using (var folder = new TestFolder())
            {
                CadSheetSetFile file = TwoSheets(folder);
                file.Change(set => set.PublishOptions.PromptForName = true);

                var dialog = new SheetSetPublishOptionsViewModel(file, null);

                Assert.Equal(folder.FullPath, dialog.Location);
                Assert.True(dialog.PromptForName);
                Assert.True(dialog.MultiSheet);
                Assert.Equal("Example Tower", dialog.FileName);
                Assert.True(dialog.IncludeHyperlinks);
                Assert.True(dialog.CreateBookmarks);
                Assert.False(dialog.IsFileNameEnabled);

                dialog.PromptForName = false;
                Assert.True(dialog.IsFileNameEnabled);
                dialog.MultiSheet = false;
                Assert.False(dialog.IsFileNameEnabled);
                dialog.MultiSheet = true;
                string issued = Path.Combine(folder.FullPath, "Issued");
                dialog.Location = issued;
                dialog.FileName = "Tower issue 3";
                dialog.IncludeHyperlinks = false;
                dialog.CreateBookmarks = false;

                Assert.True(dialog.Accept());

                CadSheetSetPublishOptions saved = CadSheetSetFile.Open(file.Path).SheetSet.PublishOptions;
                Assert.Equal(issued, saved.OutputFolder.Resolve(folder.FullPath) ?? saved.OutputFolder.FileName);
                Assert.True(saved.MultiSheet);
                Assert.False(saved.PromptForName);
                Assert.Equal("Tower issue 3", dialog.Options.FileName);
                Assert.False(dialog.Options.IncludeHyperlinks);
                Assert.False(dialog.Options.CreateBookmarks);
            }
        }

        [Fact]
        public void AnOptionsDialogLeftAsItStarted_WritesNoOutputFolderIntoASetThatNamesNone()
        {
            using (var folder = new TestFolder())
            {
                CadSheetSetFile file = TwoSheets(folder);
                var dialog = new SheetSetPublishOptionsViewModel(file, null);
                dialog.MultiSheet = false;

                Assert.True(dialog.Accept());

                CadSheetSetPublishOptions saved = CadSheetSetFile.Open(file.Path).SheetSet.PublishOptions;
                Assert.Null(saved.OutputFolder);
                Assert.False(saved.MultiSheet);
            }
        }

        [Fact]
        public void TheOptionsDialog_RefusesAMissingLocationAndAFileNameThatIsNoFileNameAndSavesNothing()
        {
            using (var folder = new TestFolder())
            {
                CadSheetSetFile file = TwoSheets(folder);
                var dialog = new SheetSetPublishOptionsViewModel(file, null) { PromptForName = false };
                int revision = file.FileRevision;

                dialog.Location = "  ";
                Assert.False(dialog.Accept());
                Assert.Equal("Enter the folder the PDF files are saved in.", dialog.Message);

                dialog.Location = folder.FullPath;
                Assert.Equal(string.Empty, dialog.Message);
                dialog.FileName = "Plan: level 1";
                Assert.False(dialog.Accept());
                Assert.StartsWith("Enter a file name", dialog.Message, StringComparison.Ordinal);
                Assert.Equal(revision, CadSheetSetFile.Open(file.Path).FileRevision);
            }
        }

        [Fact]
        public void Browse_SetsTheLocationToTheFolderAsked_AndLeavesItWhereNoneWasChosen()
        {
            using (var folder = new TestFolder())
            {
                CadSheetSetFile file = TwoSheets(folder);
                var dialog = new SheetSetPublishOptionsViewModel(file, null);
                string asked = null;
                string answer = Path.Combine(folder.FullPath, "Chosen");
                dialog.AskFolder = now =>
                {
                    asked = now;
                    return answer;
                };

                dialog.BrowseLocation();

                Assert.Equal(folder.FullPath, asked);
                Assert.Equal(answer, dialog.Location);
                dialog.AskFolder = now => null;
                dialog.BrowseLocation();
                Assert.Equal(answer, dialog.Location);
            }
        }

        [Fact]
        public void TheProgressWindow_SaysTheSheetBeingDrawnAndHowFarTheRunIs_AndCancelAsksTheRunToStop()
        {
            var progress = new SheetSetPublishProgressViewModel("Example Tower", "D:\\Issued\\Example Tower.pdf");
            Assert.Equal("Publish - Example Tower", progress.Title);
            Assert.Equal("Publishing Example Tower to PDF", progress.Heading);
            Assert.Equal("0%", progress.PercentText);
            Assert.True(progress.IsCancelEnabled);

            progress.Report(new CadPublishProgress(4, 9, "A-103 Roof Plan"));

            Assert.Equal("Sheet 4 of 9: A-103 Roof Plan", progress.Status);
            Assert.Equal("33%", progress.PercentText);
            Assert.False(progress.Token.IsCancellationRequested);

            progress.Cancel();

            Assert.True(progress.Token.IsCancellationRequested);
            Assert.False(progress.IsCancelEnabled);
            Assert.Equal("Canceling after the sheet being drawn", progress.Status);
            progress.Report(new CadPublishProgress(5, 9, "A-104"));
            Assert.Equal("Canceling after the sheet being drawn", progress.Status);
        }

        [Fact]
        public void PublishSheetSet_PublishesTheSelectionToPdf_AndSaysTheSheetsThenTheFileWritten()
        {
            using (var folder = new TestFolder())
            {
                CadSheetSetFile file = TwoSheets(folder);
                var model = new MainViewModel();

                model.PublishSheetSet(file, null, CadPublishTo.Pdf, null);

                string pdf = Path.Combine(folder.FullPath, "Example Tower.pdf");
                Assert.True(File.Exists(pdf));
                Assert.Equal(
                    "Publishing Example Tower: 2 sheets, 2 to publish, in set order.",
                    Line(model, "Publishing")
                );
                Assert.Contains("Published 2 sheets to " + pdf + ".", model.History);
                Assert.Contains("Publish complete: 2 sheets published.", model.History);
            }
        }

        [Fact]
        public void PublishSheetSet_OfOneSheet_WritesItsPdfOnly_AndAskedForTheNameOnlyWhereTheSetPrompts()
        {
            using (var folder = new TestFolder())
            {
                CadSheetSetFile file = TwoSheets(folder);
                file.Change(set => set.PublishOptions.PromptForName = true);
                var model = new MainViewModel();
                string asked = null;
                string chosen = Path.Combine(folder.FullPath, "Chosen.pdf");
                model.AskSheetSetPublishFile = path =>
                {
                    asked = path;
                    return chosen;
                };
                var second = new CadSheetComponent[] { file.SheetSet.AllSheets().Last() };

                model.PublishSheetSet(file, second, CadPublishTo.Pdf, null);

                Assert.Equal(Path.Combine(folder.FullPath, "Example Tower.pdf"), asked);
                Assert.True(File.Exists(chosen));
                Assert.Equal(
                    "Publishing Example Tower: 1 sheet, 1 to publish, in set order.",
                    Line(model, "Publishing")
                );

                model.AskSheetSetPublishFile = path => null;
                File.Delete(chosen);
                model.PublishSheetSet(file, second, CadPublishTo.Pdf, null);
                Assert.False(File.Exists(chosen));
                Assert.Equal("The publishing operation was canceled.", model.History.Last());
            }
        }

        [Fact]
        public void PublishSheetSet_UsesTheProgressRunTheWorkspaceSets_AndSaysWhatThereIsNoSheetToPublish()
        {
            using (var folder = new TestFolder())
            {
                CadSheetSetFile file = TwoSheets(folder);
                var model = new MainViewModel();
                CadPublishList got = null;
                model.RunSheetSetPublish = (list, printers) =>
                {
                    got = list;
                    return CadPublisher.Publish(list, printers);
                };

                model.PublishSheetSet(file, null, CadPublishTo.Pdf, null);

                Assert.NotNull(got);
                Assert.Equal(
                    new[] { "A-101 Ground Floor Plan", "A-102 First Floor Plan" },
                    got.Sheets.Select(s => s.Name)
                );
                Assert.Equal("Example Tower", got.SheetSetName);

                file.Change(set =>
                {
                    foreach (CadSheetSetSheet sheet in set.AllSheets())
                    {
                        sheet.IncludeForPublish = false;
                    }
                });
                got = null;
                model.PublishSheetSet(file, null, CadPublishTo.Pdf, null);
                Assert.Null(got);
                Assert.Equal("There are no sheets to publish.", model.History.Last());
                Assert.Contains("Skipped A-101 Ground Floor Plan: not included for publish.", model.History);
            }
        }

        [Fact]
        public void PublishSheetSet_ToAPlotter_RunsAtOnceWithNoProgressWindow()
        {
            using (var folder = new TestFolder())
            {
                CadSheetSetFile file = TwoSheets(folder);
                var model = new MainViewModel();
                bool windowRun = false;
                model.RunSheetSetPublish = (list, printers) =>
                {
                    windowRun = true;
                    return CadPublisher.Publish(list, printers);
                };

                model.PublishSheetSet(file, null, CadPublishTo.PlotterNamedInPageSetup, null);

                Assert.False(windowRun);
                // No printer is named in these pages' setups, so each sheet is a PDF of its own, as the Publish dialog does.
                Assert.True(File.Exists(Path.Combine(folder.FullPath, "A-101 Ground Floor Plan.pdf")));
            }
        }

        [Fact]
        public void PublishSheetSetDialog_ShowsThePublishDialogOverTheSheetsInSetOrder_AndPublishesWhereItIsOk()
        {
            using (var folder = new TestFolder())
            {
                CadSheetSetFile file = TwoSheets(folder);
                var model = new MainViewModel();
                PublishViewModel shown = null;
                model.ShowPublish = dialog =>
                {
                    shown = dialog;
                    return false;
                };

                model.PublishSheetSetDialog(file, null);

                Assert.NotNull(shown);
                Assert.Equal(
                    new[] { "A-101 Ground Floor Plan", "A-102 First Floor Plan" },
                    shown.List.Sheets.Select(s => s.Name)
                );
                Assert.Equal("Example Tower", shown.List.SheetSetName);
                Assert.Equal("The publishing operation was canceled.", model.History.Last());
                Assert.False(File.Exists(Path.Combine(folder.FullPath, "Example Tower.pdf")));

                model.ShowPublish = dialog => true;
                model.PublishSheetSetDialog(file, null);
                Assert.True(File.Exists(Path.Combine(folder.FullPath, "Example Tower.pdf")));
            }
        }

        [Fact]
        public void EditSheetSetPublishOptions_KeepsTheDialogsChoicesForLaterPublishes_AndSavesTheSetsOwn()
        {
            using (var folder = new TestFolder())
            {
                CadSheetSetFile file = TwoSheets(folder);
                var model = new MainViewModel();
                model.ShowSheetSetPublishOptions = dialog =>
                {
                    dialog.MultiSheet = false;
                    dialog.CreateBookmarks = false;
                    return dialog.Accept();
                };

                model.EditSheetSetPublishOptions(file);

                Assert.False(CadSheetSetFile.Open(file.Path).SheetSet.PublishOptions.MultiSheet);
                model.PublishSheetSet(file, null, CadPublishTo.Pdf, null);
                string one = Path.Combine(folder.FullPath, "A-101 Ground Floor Plan.pdf");
                Assert.True(File.Exists(one));
                Assert.DoesNotContain(
                    "/Outlines",
                    System.Text.Encoding.GetEncoding(28591).GetString(File.ReadAllBytes(one))
                );

                // A dialog canceled changes nothing; the next one starts from what was kept.
                bool startedOff = false;
                model.ShowSheetSetPublishOptions = dialog =>
                {
                    startedOff = !dialog.CreateBookmarks;
                    return false;
                };
                model.EditSheetSetPublishOptions(file);
                Assert.True(startedOff);
            }
        }

        [Fact]
        public void TheOptionsWindow_IsLaidOutAsTheDesignDrawsIt_AndOkIsRefusedWhileAnEntryIsWrong()
        {
            using (var folder = new TestFolder())
            {
                CadSheetSetFile file = TwoSheets(folder);
                file.Change(set => set.PublishOptions.PromptForName = true);
                OnSta(() =>
                {
                    _ = PackUriHelper.UriSchemePack;
                    var window = new SheetSetPublishOptionsWindow(new SheetSetPublishOptionsViewModel(file, null));
                    Assert.Equal("Sheet Set PDF Publish Options", window.Title);
                    FrameworkElement body = window.Body;
                    object context = window.DataContext;
                    window.Content = null;
                    body.DataContext = context;
                    body.Measure(new Size(480, 606));
                    body.Arrange(new Rect(0, 0, 480, 606));
                    body.UpdateLayout();

                    Assert.Equal(
                        new[]
                        {
                            "Include layer information",
                            "Include hyperlinks",
                            "Create bookmarks",
                            "Capture fonts used in the drawing",
                            "Convert all text to geometry",
                            "Prompt for name",
                            "Multi-sheet file",
                        }.OrderBy(c => c, StringComparer.Ordinal),
                        Descendants(body)
                            .OfType<CheckBox>()
                            .Select(c => c.Content as string)
                            .Where(c => c != null)
                            .OrderBy(c => c, StringComparer.Ordinal)
                    );
                    CheckBox layers = Descendants(body)
                        .OfType<CheckBox>()
                        .Single(c => (string)c.Content == "Include layer information");
                    Assert.False(layers.IsEnabled);
                    Assert.All(Descendants(body).OfType<ComboBox>(), combo =>
                        Assert.False(combo.IsEnabled, "The quality rows the writer does not offer are greyed.")
                    );
                    List<string> buttons = Descendants(body)
                        .OfType<Button>()
                        .Select(b => b.Content as string)
                        .Where(c => c != null)
                        .OrderBy(c => c, StringComparer.Ordinal)
                        .ToList();
                    Assert.Equal(new[] { "Cancel", "Help", "OK", "…" }, buttons);
                    Assert.Contains(Descendants(body).OfType<TextBlock>(), t => t.Text == "Location:");
                    TextBox name = Descendants(body)
                        .OfType<TextBox>()
                        .Single(t => System.Windows.Automation.AutomationProperties.GetName(t) == "File name");
                    Assert.False(name.IsEnabled, "File name is not offered while the set prompts for the name.");
                    window.Close();
                });
            }
        }

        [Fact]
        public void TheProgressWindow_IsLaidOutAsTheDesignDrawsIt()
        {
            OnSta(() =>
            {
                _ = PackUriHelper.UriSchemePack;
                var model = new SheetSetPublishProgressViewModel("Example Tower", "D:\\Issued\\Example Tower.pdf");
                model.Report(new CadPublishProgress(4, 9, "A-103 Roof Plan"));
                var window = new SheetSetPublishProgressWindow(model);
                FrameworkElement body = window.Body;
                object context = window.DataContext;
                window.Content = null;
                body.DataContext = context;
                body.Measure(new Size(520, 214));
                body.Arrange(new Rect(0, 0, 520, 214));
                body.UpdateLayout();

                List<string> words = Descendants(body).OfType<TextBlock>().Select(t => t.Text).ToList();
                Assert.Contains("Publish - Example Tower", words);
                Assert.Contains("Publishing Example Tower to PDF", words);
                Assert.Contains("D:\\Issued\\Example Tower.pdf", words);
                Assert.Contains("Sheet 4 of 9: A-103 Roof Plan", words);
                Assert.Contains("33%", words);
                Assert.Equal(33.3, Descendants(body).OfType<ProgressBar>().Single().Value, 1);
                Assert.Equal(new[] { "Cancel" }, Descendants(body).OfType<Button>().Select(b => b.Content as string));
                window.Close();
            });
        }

        private static IEnumerable<DependencyObject> Descendants(DependencyObject parent)
        {
            for (int i = 0; i < VisualTreeHelper.GetChildrenCount(parent); i++)
            {
                DependencyObject child = VisualTreeHelper.GetChild(parent, i);
                yield return child;

                foreach (DependencyObject below in Descendants(child))
                {
                    yield return below;
                }
            }
        }

        private static void OnSta(Action test)
        {
            Exception failure = null;
            var thread = new Thread(() =>
            {
                try
                {
                    test();
                }
                catch (Exception ex)
                {
                    failure = ex;
                }
            });
            thread.SetApartmentState(ApartmentState.STA);
            thread.Start();
            thread.Join();
            Assert.Null(failure);
        }
    }
}
