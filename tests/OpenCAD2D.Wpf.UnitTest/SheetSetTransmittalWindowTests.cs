using System;
using System.Collections.Generic;
using System.IO;
using System.IO.Compression;
using System.IO.Packaging;
using System.Linq;
using System.Text;
using System.Threading;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using GeometryHelper.Dxf;
using GeometryHelper.Geometry;
using OpenCAD2D.SheetSets;
using Xunit;

namespace OpenCAD2D.Wpf.UnitTest
{
    /// <summary>
    /// Create Transmittal of a sheet set (A12 5b): the dialog's view model (the Sheets tab and its ticks, the Files Table that
    /// follows them, the notes, the zip and its checks, View Report… and OK), the eTransmit items of the Sheet List's menus down
    /// to the report on the command line, and the window laid out with no window shown, with the board's states drawn to PNG
    /// files where <c>OPENCAD2D_SSM_PNG_DIR</c> is set. Every drawing and set is built in code.
    /// </summary>
    public sealed class SheetSetTransmittalWindowTests : IDisposable
    {
        private readonly TestFolder _folder = new TestFolder();

        public void Dispose() => _folder.Dispose();

        private string Root => Path.Combine(_folder.FullPath, "Projects", "Example Tower");

        private string Dst => Path.Combine(Root, "Example Tower.dst");

        private string Outside => Path.Combine(_folder.FullPath, "Outside");

        private string Styles => Path.Combine(_folder.FullPath, "PlotStyles");

        private string Drawing(string folder, string file, Action<DxfDatabase> edit = null)
        {
            Directory.CreateDirectory(folder);
            DxfDatabase database = CadTemplate.CreateMetric();
            database.Layouts.Single(l => l.LayoutName == "Layout1").LayoutName = "Sheet";
            edit?.Invoke(database);
            string path = Path.Combine(folder, file);
            CadDocument.From(database, path).SaveAs(path);
            return path;
        }

        /// <summary>
        /// Example Tower as the board draws it: G-001 at the set's folder, Architectural (A-101, which attaches
        /// <c>..\Model\Tower model.dwg</c> and shows site-photo.jpg outside the set; A-102; A-202 whose drawing is deleted) and
        /// Structural (S-101), a template outside the set and the table monochrome.ctb of the plot style folder.
        /// </summary>
        private CadSheetSetFile Tower()
        {
            Directory.CreateDirectory(Outside);
            Directory.CreateDirectory(Styles);
            File.WriteAllBytes(Path.Combine(Outside, "site-photo.jpg"), new byte[1100]);
            File.WriteAllBytes(Path.Combine(Styles, "tower.ctb"), new byte[300]);
            string model = Drawing(Path.Combine(Root, "Model"), "Tower model.dwg");
            string template = Drawing(Outside, "OpenCAD2D-mm.dwt");
            string cover = Drawing(Root, "G-001 Cover Sheet.dwg", d =>
                d.Layouts.Single(l => l.LayoutName == "Sheet").CurrentStyleSheet = "tower.ctb"
            );
            string plan = Drawing(Path.Combine(Root, "Architectural"), "A-101 Ground Floor Plan.dwg", d =>
            {
                d.ModelSpace.AppendEntity(
                    new DxfBlockReference(new GeoPoint3(0, 0, 0), d.AttachXref("..\\Model\\Tower model.dwg", "model"))
                );
                DxfRasterImageDef
                    .CreateImageDictionary(d)
                    .SetAt(
                        "photo",
                        new DxfRasterImageDef(Path.Combine(Outside, "site-photo.jpg"), new GeoVector2(40, 30))
                    );
            });
            string second = Drawing(Path.Combine(Root, "Architectural"), "A-102 First Floor Plan.dwg");
            string lost = Drawing(Path.Combine(Root, "Architectural"), "A-202 East and West Elevations.dwg");
            string structural = Drawing(Path.Combine(Root, "Structural"), "S-101 Foundation Plan.dwg");
            CadSheetSetFile file = CadSheetSetFile.Create(Dst, "Example Tower", null);
            file.Change(set =>
            {
                set.Template = new CadDrawingReference(CadFileReference.FromPath(template, Root), "Sheet", null, null);
                Named(set.ImportLayouts(cover, new[] { "Sheet" }, false)[0], "G-001", "Cover Sheet");
                CadSheetSubset architectural = set.NewSubset("Architectural", null, false);
                Named(architectural.ImportLayouts(plan, new[] { "Sheet" }, false)[0], "A-101", "Ground Floor Plan");
                Named(architectural.ImportLayouts(second, new[] { "Sheet" }, false)[0], "A-102", "First Floor Plan");
                Named(
                    architectural.ImportLayouts(lost, new[] { "Sheet" }, false)[0],
                    "A-202",
                    "East and West Elevations"
                );
                Named(
                    set.NewSubset("Structural", null, false).ImportLayouts(structural, new[] { "Sheet" }, false)[0],
                    "S-101",
                    "Foundation Plan"
                );
            });
            File.Delete(lost);
            return file;
        }

        private static void Named(CadSheetSetSheet sheet, string number, string title)
        {
            sheet.Number = number;
            sheet.Title = title;
        }

        private TransmittalViewModel Dialog(CadSheetSetFile file, params string[] selected)
        {
            CadSheetSet set = file.SheetSet;
            CadSheetComponent[] selection = selected
                .Select(n => set.AllSheets().Cast<CadSheetComponent>().Concat(set.Subsets).Single(c => Name(c) == n))
                .ToArray();
            return new TransmittalViewModel(new CadTransmittal(file, Styles, null), selection);
        }

        private static string Name(CadSheetComponent part) =>
            part is CadSheetSetSheet sheet ? sheet.Number : ((CadSheetSubset)part).Name;

        private static string[] Ticked(TransmittalViewModel dialog) =>
            dialog
                .Rows.Where(r => r.Kind == SheetSetNodeKind.Sheet && r.IsChecked == true)
                .Select(r => r.Text)
                .ToArray();

        private static TransmittalSheetRow Row(TransmittalViewModel dialog, string text) =>
            dialog.Rows.Single(r => r.Text == text);

        private static string[] Files(TransmittalViewModel dialog) => dialog.FileRows.Select(r => r.Name).ToArray();

        [Fact]
        public void TheDialog_StartsTickedAsTheRightClickChose_AndASheetNotFoundIsRedAndCannotBeTicked()
        {
            CadSheetSetFile file = Tower();

            TransmittalViewModel dialog = Dialog(file, "Architectural");

            Assert.Equal("Create Transmittal - Example Tower", dialog.Title);
            Assert.Equal(
                new[]
                {
                    "Example Tower",
                    "G-001 - Cover Sheet",
                    "Architectural",
                    "A-101 - Ground Floor Plan",
                    "A-102 - First Floor Plan",
                    "A-202 - East and West Elevations",
                    "Structural",
                    "S-101 - Foundation Plan",
                },
                dialog.Rows.Select(r => r.Text).ToArray()
            );
            Assert.Equal(new[] { "A-101 - Ground Floor Plan", "A-102 - First Floor Plan" }, Ticked(dialog));
            Assert.Null(Row(dialog, "Example Tower").IsChecked);
            Assert.True(Row(dialog, "Architectural").IsChecked);
            Assert.False(Row(dialog, "Structural").IsChecked);
            TransmittalSheetRow lost = Row(dialog, "A-202 - East and West Elevations");
            Assert.True(lost.IsMissing);
            Assert.False(lost.IsEnabled);
            Assert.Equal(SheetSetTone.Error, lost.Tone);
            Assert.Equal("sheet-missing", lost.Icon);
            lost.IsChecked = true;
            Assert.False(lost.IsChecked);
        }

        [Fact]
        public void TheWholeSetIsTickedWhereTheSetIsChosen_AndTickingARowTicksTheSheetsUnderIt()
        {
            CadSheetSetFile file = Tower();

            TransmittalViewModel dialog = Dialog(file);

            Assert.Equal(
                new[]
                {
                    "G-001 - Cover Sheet",
                    "A-101 - Ground Floor Plan",
                    "A-102 - First Floor Plan",
                    "S-101 - Foundation Plan",
                },
                Ticked(dialog)
            );
            Row(dialog, "Architectural").IsChecked = false;
            Assert.Equal(new[] { "G-001 - Cover Sheet", "S-101 - Foundation Plan" }, Ticked(dialog));
            Assert.Null(Row(dialog, "Example Tower").IsChecked);
            Row(dialog, "Example Tower").IsChecked = false;
            Assert.Empty(Ticked(dialog));
            Assert.False(Row(dialog, "Example Tower").IsChecked);
            Row(dialog, "Example Tower").IsChecked = true;
            Assert.Equal(4, Ticked(dialog).Length);
            Assert.True(Row(dialog, "Architectural").IsChecked);
        }

        [Fact]
        public void ARowClosed_HidesTheRowsUnderIt_AndOpenedShowsThemAgain()
        {
            TransmittalViewModel dialog = Dialog(Tower());

            Row(dialog, "Architectural").IsExpanded = false;

            Assert.Equal(
                new[]
                {
                    "Example Tower",
                    "G-001 - Cover Sheet",
                    "Architectural",
                    "Structural",
                    "S-101 - Foundation Plan",
                },
                dialog.Rows.Select(r => r.Text).ToArray()
            );
            Row(dialog, "Architectural").IsExpanded = true;
            Assert.Equal(8, dialog.Rows.Count);
        }

        [Fact]
        public void TheFilesTable_ListsWhatTheTickedSheetsNeed_AndFollowsTheTicks()
        {
            TransmittalViewModel dialog = Dialog(Tower(), "S-101");

            Assert.Equal(new[] { "Example Tower.dst", "S-101 Foundation Plan.dwg", "OpenCAD2D-mm.dwt" }, Files(dialog));

            Row(dialog, "G-001 - Cover Sheet").IsChecked = true;
            Assert.Equal(
                new[]
                {
                    "Example Tower.dst",
                    "G-001 Cover Sheet.dwg",
                    "S-101 Foundation Plan.dwg",
                    "OpenCAD2D-mm.dwt",
                    "tower.ctb",
                },
                Files(dialog)
            );
            Row(dialog, "Architectural").IsChecked = true;
            Assert.Equal(
                new[]
                {
                    "Example Tower.dst",
                    "A-101 Ground Floor Plan.dwg",
                    "A-102 First Floor Plan.dwg",
                    "G-001 Cover Sheet.dwg",
                    "S-101 Foundation Plan.dwg",
                    "Tower model.dwg",
                    "OpenCAD2D-mm.dwt",
                    "site-photo.jpg",
                    "tower.ctb",
                },
                Files(dialog)
            );
            TransmittalFileRow photo = dialog.FileRows.Single(r => r.Name == "site-photo.jpg");
            Assert.Equal(".\\Files", photo.Where);
            Assert.Equal("1 KB", photo.SizeText);
            Assert.Equal("Image: " + Path.Combine(Outside, "site-photo.jpg"), photo.ToolTip);
            Assert.Equal(
                ".\\Architectural",
                dialog.FileRows.Single(r => r.Name == "A-101 Ground Floor Plan.dwg").Where
            );
        }

        [Fact]
        public void AFileUnticked_StaysUnticked_WhenSheetsAreTickedAndClearedAfter_AndTheStatusSaysWhatIsIncluded()
        {
            TransmittalViewModel dialog = Dialog(Tower(), "A-101");
            Assert.StartsWith("Included 5 files, ", dialog.Status);
            Assert.False(dialog.StatusIsError);
            TransmittalFileRow photo = dialog.FileRows.Single(r => r.Name == "site-photo.jpg");

            photo.IsIncluded = false;

            Assert.Equal(4, dialog.IncludedCount);
            Assert.StartsWith("Included 4 files, ", dialog.Status);
            Row(dialog, "S-101 - Foundation Plan").IsChecked = true;
            Assert.False(dialog.FileRows.Single(r => r.Name == "site-photo.jpg").IsIncluded);
            Assert.True(dialog.FileRows.Single(r => r.Name == "S-101 Foundation Plan.dwg").IsIncluded);
        }

        [Fact]
        public void AFileNotFound_IsRedUncheckedAndLeftOut_AndTheStatusLineSaysSo()
        {
            CadSheetSetFile file = Tower();
            File.Delete(Path.Combine(Outside, "site-photo.jpg"));

            TransmittalViewModel dialog = Dialog(file, "A-101");

            TransmittalFileRow missing = dialog.FileRows.Single(r => !r.File.IsFound);
            Assert.Equal("site-photo.jpg", missing.Name);
            Assert.False(missing.IsIncluded);
            Assert.False(missing.IsEnabled);
            missing.IsIncluded = true;
            Assert.False(missing.IsIncluded);
            Assert.Equal(SheetSetTone.Error, missing.Tone);
            Assert.Equal("missing: referenced by A-101 Ground Floor Plan.dwg", missing.Where);
            Assert.Equal("sheet-missing", missing.Icon);
            Assert.Same(missing, dialog.FileRows.Last());
            Assert.Equal("1 file not found; it is listed in the report and left out.", dialog.Status);
            Assert.True(dialog.StatusIsError);
            Assert.True(dialog.FooterIsError);
            Assert.Equal(dialog.Status, dialog.FooterText);
        }

        [Fact]
        public void TheZipStartsInATransmittalsFolderBesideTheDst_AndNotesAndTheFieldBoxGoToTheTransmittal()
        {
            CadSheetSetFile file = Tower();
            var transmittal = new CadTransmittal(file, Styles, null);
            var dialog = new TransmittalViewModel(transmittal, null);

            Assert.Equal(Path.Combine(Root, "Transmittals", "Example Tower.zip"), dialog.ZipPath);
            Assert.True(dialog.UpdateFields);
            dialog.Notes = "Issued for tender, 26 Nov 2026.";
            dialog.UpdateFields = false;
            dialog.ZipPath = Path.Combine(_folder.FullPath, "out", "tender");
            Assert.True(dialog.Accept());

            Assert.Equal(Path.Combine(_folder.FullPath, "out", "tender.zip"), dialog.Result.ZipPath);
            Assert.Contains("Issued for tender, 26 Nov 2026.", dialog.Result.Report);
            Assert.False(transmittal.UpdateFields);
        }

        [Fact]
        public void OK_WritesTheTickedFilesOnly_AndTheReportIsInTheZip()
        {
            CadSheetSetFile file = Tower();
            TransmittalViewModel dialog = Dialog(file, "A-101");
            dialog.FileRows.Single(r => r.Name == "site-photo.jpg").IsIncluded = false;
            string zip = Path.Combine(_folder.FullPath, "out", "tower.zip");
            dialog.ZipPath = zip;

            Assert.True(dialog.Accept());

            Assert.Equal(
                new[]
                {
                    "Architectural/A-101 Ground Floor Plan.dwg",
                    "Example Tower.dst",
                    "Files/OpenCAD2D-mm.dwt",
                    "Model/Tower model.dwg",
                    "transmittal.txt",
                },
                Entries(zip)
            );
            Assert.Equal(4, dialog.Result.FileCount);
            Assert.Equal(string.Empty, dialog.Message);
        }

        private static List<string> Entries(string zip)
        {
            using (ZipArchive archive = ZipFile.OpenRead(zip))
            {
                return archive.Entries.Select(e => e.FullName).ToList();
            }
        }

        [Fact]
        public void OKWithNoSheetTicked_OrNoZip_IsRefusedWithTheReason()
        {
            TransmittalViewModel dialog = Dialog(Tower());
            Row(dialog, "Example Tower").IsChecked = false;

            Assert.False(dialog.Accept());
            Assert.Equal("Tick at least one sheet.", dialog.Message);
            Assert.Equal("Tick at least one sheet.", dialog.Status);
            Assert.Empty(dialog.FileRows);

            Row(dialog, "Example Tower").IsChecked = true;
            dialog.ZipPath = "   ";
            Assert.False(dialog.Accept());
            Assert.Equal("Enter the zip file to write.", dialog.Message);
            dialog.ZipPath = Path.Combine(_folder.FullPath, "out", "a.zip");
            Assert.Equal(string.Empty, dialog.Message);
        }

        [Fact]
        public void AZipThatIsThere_IsReplacedOnlyWhereTheQuestionIsAnsweredYes()
        {
            TransmittalViewModel dialog = Dialog(Tower());
            string zip = Path.Combine(_folder.FullPath, "out", "tower.zip");
            Directory.CreateDirectory(Path.GetDirectoryName(zip));
            File.WriteAllText(zip, "an old zip");
            dialog.ZipPath = zip;
            var asked = new List<string>();
            dialog.AskReplace = path =>
            {
                asked.Add(path);
                return false;
            };

            Assert.False(dialog.Accept());
            Assert.Equal("The zip was not replaced.", dialog.Message);
            Assert.Equal("an old zip", File.ReadAllText(zip));

            dialog.AskReplace = path =>
            {
                asked.Add(path);
                return true;
            };
            Assert.True(dialog.Accept());

            Assert.Equal(new[] { zip, zip }, asked);
            Assert.Equal("Example Tower.dst", Entries(zip).Single(e => e.EndsWith(".dst", StringComparison.Ordinal)));
        }

        [Fact]
        public void BrowseSetsTheZip_AndViewReportShowsTheReportAsItWouldBeWritten()
        {
            TransmittalViewModel dialog = Dialog(Tower(), "A-101");
            dialog.AskZip = current => Path.Combine(_folder.FullPath, "chosen.zip");
            string shown = null;
            dialog.ShowReport = text => shown = text;
            dialog.Notes = "Hold the survey.";

            dialog.BrowseZip();
            dialog.ViewReport();

            Assert.Equal(Path.Combine(_folder.FullPath, "chosen.zip"), dialog.ZipPath);
            Assert.StartsWith("Transmittal report\r\n", shown);
            Assert.Contains("Hold the survey.", shown);
            Assert.Contains("Package:     " + Path.Combine(_folder.FullPath, "chosen.zip"), shown);
            Assert.Contains("A-101 - Ground Floor Plan\r\n", shown);
            Assert.Contains("Date:        (not written yet)", shown);
            Assert.Contains("A-101 Ground Floor Plan.dwg: the path of the image ", shown);
            Assert.False(File.Exists(Path.Combine(_folder.FullPath, "chosen.zip")));
        }

        [Fact]
        public void TheEtransmitItems_ShowTheDialogOverTheSheetsTheyWereChosenOn_AndSayWhatWasWritten()
        {
            Tower();
            var model = new MainViewModel();
            var palette = new SheetSetManagerViewModel(model);
            Assert.True(palette.OpenSet(Dst));
            var shown = new List<string[]>();
            string zip = Path.Combine(_folder.FullPath, "out", "tower.zip");
            model.ShowTransmittal = dialog =>
            {
                shown.Add(Ticked(dialog));
                dialog.ZipPath = zip;
                return dialog.Accept();
            };

            SheetSetNodeViewModel structural = palette.Rows.Single(r => r.Text == "Structural");
            palette.MenuFor(structural).Single(i => i.Header == "eTransmit…").Command.Execute(null);
            palette.MenuFor(palette.Root).Single(i => i.Header == "eTransmit…").Command.Execute(null);
            SheetSetNodeViewModel cover = palette.Rows.Single(r => r.Text == "G-001 - Cover Sheet");
            palette.MenuFor(cover).Single(i => i.Header == "eTransmit…").Command.Execute(null);

            Assert.Equal(
                new[]
                {
                    new[] { "S-101 - Foundation Plan" },
                    new[]
                    {
                        "G-001 - Cover Sheet",
                        "A-101 - Ground Floor Plan",
                        "A-102 - First Floor Plan",
                        "S-101 - Foundation Plan",
                    },
                    new[] { "G-001 - Cover Sheet" },
                },
                shown.ToArray()
            );
            Assert.Contains(model.History, line =>
                line.StartsWith("Transmittal written: " + zip + " (", StringComparison.Ordinal)
            );
            Assert.Contains(
                "Example Tower.dst: the sheet creation template of Example Tower is now .\\Files\\OpenCAD2D-mm.dwt.",
                model.History
            );
        }

        [Fact]
        public void ACanceledDialog_SaysSo_AndWritesNothing()
        {
            Tower();
            var model = new MainViewModel();
            var palette = new SheetSetManagerViewModel(model);
            Assert.True(palette.OpenSet(Dst));
            model.ShowTransmittal = dialog => false;

            palette.MenuFor(palette.Root).Single(i => i.Header == "eTransmit…").Command.Execute(null);

            Assert.Contains("The transmittal was canceled.", model.History);
            Assert.False(Directory.Exists(Path.Combine(Root, "Transmittals")));
        }

        // ----- the window -----

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

        private static void Layout(FrameworkElement body, double width, double height)
        {
            body.Width = width;
            body.Height = height;
            body.Measure(new Size(width, height));
            body.Arrange(new Rect(0, 0, width, height));
            body.UpdateLayout();
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

        private static TransmittalWindow Made(TransmittalViewModel dialog, out FrameworkElement body)
        {
            var window = new TransmittalWindow(dialog);
            body = window.Body;
            CadAppHost.TakeOut(window, body);
            body.DataContext = dialog;
            Layout(body, 720, 452);
            return window;
        }

        private static string Png(FrameworkElement element, string name)
        {
            string folder = Environment.GetEnvironmentVariable("OPENCAD2D_SSM_PNG_DIR");

            if (string.IsNullOrEmpty(folder))
            {
                return null;
            }

            Directory.CreateDirectory(folder);
            var bitmap = new RenderTargetBitmap(
                (int)Math.Ceiling(element.ActualWidth),
                (int)Math.Ceiling(element.ActualHeight),
                96,
                96,
                PixelFormats.Pbgra32
            );
            bitmap.Render(element);
            var encoder = new PngBitmapEncoder();
            encoder.Frames.Add(BitmapFrame.Create(bitmap));
            string path = Path.Combine(folder, name + ".png");

            using (FileStream stream = File.Create(path))
            {
                encoder.Save(stream);
            }

            return path;
        }

        [Fact]
        public void TheWindow_HasTheTabsTheTreeTheNotesTheZipTheFieldBoxAndTheButtonsOfTheDesign()
        {
            OnSta(() =>
            {
                _ = PackUriHelper.UriSchemePack;
                TransmittalViewModel dialog = Dialog(Tower(), "Architectural");
                dialog.Notes = "Issued for tender, 26 Nov 2026.";
                TransmittalWindow window = Made(dialog, out FrameworkElement body);

                Assert.Equal("Create Transmittal", window.Title);
                List<string> words = Descendants(body).OfType<TextBlock>().Select(t => t.Text).ToList();
                Assert.Contains("Create Transmittal", words);
                Assert.Contains("Enter notes to be included with this transmittal package:", words);
                Assert.Contains("Zip file:", words);
                Assert.Contains("Example Tower", words);
                Assert.Contains("A-202 - East and West Elevations", words);
                Assert.Equal(
                    new[] { "Sheets", "Files Table" },
                    Descendants(body).OfType<RadioButton>().Select(r => (string)r.Content).ToArray()
                );
                Assert.Equal(
                    new[] { "View Report…", "OK", "Cancel", "Help" },
                    Descendants(body)
                        .OfType<Button>()
                        .Select(b => b.Content as string)
                        .Where(c => c != null && c != "…")
                        .ToArray()
                );
                Assert.Contains(Descendants(body).OfType<CheckBox>(), c =>
                    c.Content as string == "Update fields in the copies first" && c.IsChecked == true
                );
                TextBox notes = Descendants(body)
                    .OfType<TextBox>()
                    .First(t => t.Text.StartsWith("Issued", StringComparison.Ordinal));
                Assert.True(notes.AcceptsReturn);
                ListBox sheets = Descendants(body).OfType<ListBox>().First(l => l.Items.Count == 8);
                Assert.Equal(Visibility.Visible, sheets.Visibility);
                window.Close();
            });
        }

        [Fact]
        public void TheFilesTableTab_ShowsTheColumnsAndTheFilesOfTheDesign_AndAFileNotFoundInRed()
        {
            OnSta(() =>
            {
                _ = PackUriHelper.UriSchemePack;
                CadSheetSetFile file = Tower();
                File.Delete(Path.Combine(Outside, "site-photo.jpg"));
                TransmittalViewModel dialog = Dialog(file, "Architectural");
                dialog.IsFilesTab = true;
                Made(dialog, out FrameworkElement body);

                List<string> words = Descendants(body).OfType<TextBlock>().Select(t => t.Text).ToList();
                Assert.Contains("File name", words);
                Assert.Contains("Path in zip", words);
                Assert.Contains("Size", words);
                Assert.Contains("Example Tower.dst", words);
                Assert.Contains("missing: referenced by A-101 Ground Floor Plan.dwg", words);
                Assert.Contains("1 file not found; it is listed in the report and left out.", words);
                ListBox sheets = Descendants(body).OfType<ListBox>().First(l => l.Name == "SheetList");
                Assert.Equal(Visibility.Collapsed, sheets.Visibility);
                Assert.Equal(
                    Visibility.Visible,
                    Descendants(body).OfType<ListBox>().Single(l => l.Name == "FileList").Visibility
                );
                TextBlock status = Descendants(body)
                    .OfType<TextBlock>()
                    .Single(t => System.Windows.Automation.AutomationProperties.GetName(t) == "Message");
                Assert.Equal("#FFFF8A7A", ((SolidColorBrush)status.Foreground).Color.ToString());
            });
        }

        [Fact]
        public void TheStatesOfTheBoard_AreDrawnToPngFiles_WhereAFolderIsGiven()
        {
            OnSta(() =>
            {
                _ = PackUriHelper.UriSchemePack;
                CadSheetSetFile file = Tower();
                TransmittalViewModel sheets = Dialog(file);
                sheets.Notes = "Issued for tender, 26 Nov 2026.";
                Made(sheets, out FrameworkElement sheetsBody);
                Png(sheetsBody, "transmittal-sheets");

                File.Delete(Path.Combine(Outside, "site-photo.jpg"));
                TransmittalViewModel table = Dialog(file);
                table.Notes = "Issued for tender, 26 Nov 2026.";
                table.IsFilesTab = true;
                Made(table, out FrameworkElement tableBody);
                Png(tableBody, "transmittal-files");

                string text = null;
                table.ShowReport = report => text = report;
                table.ViewReport();
                var report = new TransmittalReportWindow(table.SetName, text);
                CadAppHost.TakeOut(report, report.Body);
                Layout(report.Body, 640, 480);
                Png(report.Body, "transmittal-report");
            });
        }
    }
}
