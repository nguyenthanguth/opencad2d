using System;
using System.Collections.Generic;
using System.IO;
using System.IO.Packaging;
using System.Linq;
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
    /// The Create Sheet Set wizard (A12 4c) and its Import Options dialog made on a thread of their own and laid out with no
    /// window shown: what the boards draw is there on each page, and with <c>OPENCAD2D_SSM_PNG_DIR</c> set each state of the
    /// boards A12WizardExample and A12WizardDrawings is drawn to a PNG file in that folder.
    /// </summary>
    public class NewSheetSetWindowTests : IDisposable
    {
        private readonly TestFolder _folder = new TestFolder();

        public void Dispose() => _folder.Dispose();

        private string Templates => Path.Combine(_folder.FullPath, "templates");

        private string Projects => Path.Combine(_folder.FullPath, "Projects", "Example Tower");

        private string Drawings => Path.Combine(_folder.FullPath, "Projects", "Example Tower", "Drawings");

        private string Drawing(string folder, string file, params string[] layouts)
        {
            Directory.CreateDirectory(folder);
            var database = new DxfDatabase("AC1032");
            database.Layouts.Single(l => l.LayoutName == "Layout1").LayoutName = layouts[0];

            foreach (string name in layouts.Skip(1))
            {
                var layout = new DxfLayout { LayoutName = name };
                layout.AddToLayoutDictionary(database, null);
                layout.Initialize();
            }

            foreach (string name in layouts)
            {
                database
                    .Layouts.Single(l => l.LayoutName == name)
                    .BlockTableRecord.AppendEntity(new DxfLine(new GeoPoint3(10, 0, 0), new GeoPoint3(10, 50, 0)));
            }

            string path = Path.Combine(folder, file);
            CadDocument.From(database, file).SaveAs(path);
            return path;
        }

        /// <summary>The two examples of the board's list, in the template folder.</summary>
        private void Examples()
        {
            Directory.CreateDirectory(Templates);
            CadSheetSetFile.Create(Path.Combine(Templates, "New Sheet Set.dst"), "New Sheet Set");
            string dwt = Drawing(Templates, "OpenCAD2D-mm.dwt", "A1 sheet");
            CadSheetSetFile metric = CadSheetSetFile.Create(
                Path.Combine(Templates, "OpenCAD2D Metric Sheet Set.dst"),
                "OpenCAD2D Metric Sheet Set",
                "Subsets General, Plans and Details; custom properties Client, Project Address (set), Drawn By, Checked By (sheet)."
            );
            metric.Change(set =>
            {
                set.Template = new CadDrawingReference(
                    CadFileReference.FromPath(dwt, Templates),
                    "A1 sheet",
                    null,
                    null
                );
                set.PromptForTemplate = false;
                set.NewSubset("General", null, false);
                set.NewSubset("Plans", null, false);
                set.NewSubset("Details", null, false);
                set.AddCustomProperty("Client", string.Empty, CadSheetSetPropertyFlags.Set);
                set.AddCustomProperty("Project Address", string.Empty, CadSheetSetPropertyFlags.Set);
                set.AddCustomProperty("Drawn By", string.Empty, CadSheetSetPropertyFlags.Sheet);
                set.AddCustomProperty("Checked By", string.Empty, CadSheetSetPropertyFlags.Sheet);
            });
        }

        private MainViewModel Model()
        {
            var model = new MainViewModel();
            model.SheetSetStoreFolder = Projects;
            return model;
        }

        /// <summary>The drawings of the board: Architectural with two drawings (one layout with a # in its name), an old cover that belongs to another set, Structural with one.</summary>
        private void Towers()
        {
            Drawing(Path.Combine(Drawings, "Architectural"), "A-101 Ground Floor Plan.dwg", "A-101 Ground Floor Plan");
            Drawing(
                Path.Combine(Drawings, "Architectural"),
                "A-201 Elevations.dwg",
                "North and South",
                "East and West",
                "Sketch #2"
            );
            string cover = Drawing(Path.Combine(Drawings, "Architectural"), "Old cover.dwg", "Cover");
            Drawing(Path.Combine(Drawings, "Structural"), "S-101 Foundation Plan.dwg", "S-101 Foundation Plan");
            Directory.CreateDirectory(Path.Combine(_folder.FullPath, "Riverside"));
            CadSheetSetFile.CreateFromDrawings(
                Path.Combine(_folder.FullPath, "Riverside", "Riverside Clinic.dst"),
                "Riverside Clinic",
                null,
                new[] { new CadSheetImportSource(null, cover, new[] { "Cover" }) },
                null
            );
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

        private static IEnumerable<T> Shown<T>(FrameworkElement body)
            where T : FrameworkElement => Descendants(body).OfType<T>().Where(e => e.IsVisible || IsShown(e));

        /// <summary>Whether an element and every element above it is Visible; a window never shown has none that IsVisible.</summary>
        private static bool IsShown(FrameworkElement element)
        {
            for (DependencyObject o = element; o != null; o = VisualTreeHelper.GetParent(o))
            {
                if (o is UIElement ui && ui.Visibility != Visibility.Visible)
                {
                    return false;
                }
            }

            return true;
        }

        private static NewSheetSetWindow Made(NewSheetSetViewModel model, out FrameworkElement body)
        {
            var window = new NewSheetSetWindow(model);
            body = window.Body;
            CadAppHost.TakeOut(window, body);
            body.DataContext = model;
            Layout(body, 660, 470);
            return window;
        }

        private static List<string> Words(FrameworkElement body) =>
            Shown<TextBlock>(body).Select(t => t.Text).Where(t => !string.IsNullOrEmpty(t)).ToList();

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

        private static void Show(NewSheetSetViewModel wizard, FrameworkElement body)
        {
            body.UpdateLayout();
            body.Measure(new Size(660, 470));
            body.Arrange(new Rect(0, 0, 660, 470));
            body.UpdateLayout();
        }

        private static string[] Buttons(FrameworkElement body) =>
            Shown<Button>(body)
                .Where(b => b.Content is string)
                .Select(b => (string)b.Content + (b.IsEnabled ? string.Empty : " (off)"))
                .ToArray();

        // ---- Route 1: from an example

        [Fact]
        public void Begin_OfTheExampleRoute_HasTheTwoChoices_ThePagesDownTheLeft_AndBackOff()
        {
            OnSta(() =>
            {
                _ = PackUriHelper.UriSchemePack;
                Examples();
                var wizard = new NewSheetSetViewModel(Model(), Templates);
                NewSheetSetWindow window = Made(wizard, out FrameworkElement body);

                Assert.Equal(660, body.ActualWidth);
                Assert.Equal(470, body.ActualHeight);
                List<string> words = Words(body);
                Assert.Contains("Create Sheet Set - Begin", words);
                Assert.Contains("Create a sheet set using", words);
                Assert.Contains("Begin", words);
                Assert.Contains("Sheet Set Example", words);
                Assert.Contains("Sheet Set Details", words);
                Assert.Contains("Confirm", words);
                Assert.DoesNotContain("Choose Layouts", words);
                Assert.Equal(
                    new[] { "An example sheet set", "Existing drawings" },
                    Shown<RadioButton>(body).Select(r => (string)r.Content).ToArray()
                );
                Assert.True(Shown<RadioButton>(body).First().IsChecked);
                Assert.Equal(new[] { "< Back (off)", "Next >", "Cancel" }, Buttons(body));
                Png(body, "a12-wizard-example-1-begin");
                window.Close();
            });
        }

        [Fact]
        public void TheExamplePage_ListsTheExamples_TheBrowseChoice_AndTheBoxOfTheOneChosen()
        {
            OnSta(() =>
            {
                _ = PackUriHelper.UriSchemePack;
                Examples();
                var wizard = new NewSheetSetViewModel(Model(), Templates);
                NewSheetSetWindow window = Made(wizard, out FrameworkElement body);
                wizard.Next();
                wizard.SelectedExample = wizard.Examples.Single(e => e.Name == "OpenCAD2D Metric Sheet Set");
                Show(wizard, body);

                List<string> words = Words(body);
                Assert.Contains("Create Sheet Set - Sheet Set Example", words);
                Assert.Contains(
                    "Select a sheet set to use as an example",
                    Shown<RadioButton>(body).Select(r => (string)r.Content)
                );
                Assert.Contains(
                    "Browse to another sheet set to use as an example",
                    Shown<RadioButton>(body).Select(r => (string)r.Content)
                );
                Assert.Contains("New Sheet Set", words);
                Assert.Contains("OpenCAD2D Metric Sheet Set", words);
                ListBox list = Shown<ListBox>(body).Single();
                Assert.Equal(2, list.Items.Count);
                Assert.Same(wizard.SelectedExample, list.SelectedItem);
                Assert.Contains(words, w =>
                    w.StartsWith("Title: OpenCAD2D Metric Sheet Set", StringComparison.Ordinal)
                );
                Assert.Equal(
                    new[] { "< Back", "Next >", "Cancel" },
                    Buttons(body).Where(b => !b.StartsWith("…", StringComparison.Ordinal)).ToArray()
                );
                Png(body, "a12-wizard-example-2-example");
                window.Close();
            });
        }

        [Fact]
        public void TheDetailsPage_HasTheBoxes_TheNote_TheFolderHierarchyOnTheExampleRoute_AndSheetSetProperties()
        {
            OnSta(() =>
            {
                _ = PackUriHelper.UriSchemePack;
                Examples();
                var wizard = new NewSheetSetViewModel(Model(), Templates);
                NewSheetSetWindow window = Made(wizard, out FrameworkElement body);
                wizard.Next();
                wizard.Next();
                wizard.Name = "Example Tower";
                wizard.Description = "Six-storey residential tower, 48 flats";
                wizard.CreateFolderHierarchy = true;
                Show(wizard, body);

                List<string> words = Words(body);
                Assert.Contains("Create Sheet Set - Sheet Set Details", words);
                Assert.Contains("Name of new sheet set:", words);
                Assert.Contains("Description (optional):", words);
                Assert.Contains("Store sheet set data file (.dst) here:", words);
                Assert.Contains("Note: keep the .dst where everyone working on the set can reach it.", words);
                Assert.Contains(
                    "Create a folder hierarchy based on subsets",
                    Shown<CheckBox>(body).Select(c => (string)c.Content)
                );
                Assert.Contains("Sheet Set Properties…", Buttons(body));
                Assert.Equal(
                    new[] { "Example Tower", "Six-storey residential tower, 48 flats", Projects },
                    Shown<TextBox>(body).Select(t => t.Text).ToArray()
                );
                Assert.DoesNotContain(words, w => w.Contains("already exists"));
                Png(body, "a12-wizard-example-3-details");
                window.Close();
            });
        }

        [Fact]
        public void TheConfirmPage_ShowsThePreviewInBold_AndFinishInPlaceOfNext()
        {
            OnSta(() =>
            {
                _ = PackUriHelper.UriSchemePack;
                Examples();
                var wizard = new NewSheetSetViewModel(Model(), Templates);
                NewSheetSetWindow window = Made(wizard, out FrameworkElement body);
                wizard.Next();
                wizard.SelectedExample = wizard.Examples.Single(e => e.Name == "OpenCAD2D Metric Sheet Set");
                wizard.Next();
                wizard.Name = "Example Tower";
                wizard.Description = "Six-storey residential tower, 48 flats";
                wizard.CreateFolderHierarchy = true;
                wizard.Next();
                Show(wizard, body);

                List<string> words = Words(body);
                Assert.Contains("Create Sheet Set - Confirm", words);
                Assert.Contains("Sheet Set Preview:", words);
                TextBlock title = Shown<TextBlock>(body).Single(t => t.Text == "Example Tower");
                Assert.Equal(FontWeights.SemiBold, title.FontWeight);
                Assert.Contains(words, w => w.Contains("Sheet set data file: ", StringComparison.Ordinal));
                Assert.Equal(new[] { "< Back", "Finish", "Cancel" }, Buttons(body));
                Png(body, "a12-wizard-example-4-confirm");
                window.Close();
            });
        }

        // ---- Route 2: from existing drawings

        [Fact]
        public void Begin_OfTheDrawingsRoute_ShowsItsPagesInTheirOrder()
        {
            OnSta(() =>
            {
                _ = PackUriHelper.UriSchemePack;
                Examples();
                var wizard = new NewSheetSetViewModel(Model(), Templates) { IsExistingDrawings = true };
                NewSheetSetWindow window = Made(wizard, out FrameworkElement body);

                List<string> words = Words(body);
                Assert.Contains("Choose Layouts", words);
                Assert.DoesNotContain("Sheet Set Example", words);
                Assert.True(Shown<RadioButton>(body).Last().IsChecked);
                Assert.True(
                    words.IndexOf("Sheet Set Details") < words.IndexOf("Choose Layouts"),
                    "Sheet Set Details comes before Choose Layouts in the list."
                );
                Png(body, "a12-wizard-drawings-1-begin");
                window.Close();
            });
        }

        [Fact]
        public void TheDetailsPage_OfTheDrawingsRoute_HasNoFolderHierarchyBox_AndShowsTheRedBoxWhereTheDstIsThere()
        {
            OnSta(() =>
            {
                _ = PackUriHelper.UriSchemePack;
                Examples();
                Directory.CreateDirectory(Projects);
                File.WriteAllText(Path.Combine(Projects, "Example Tower.dst"), "x");
                var wizard = new NewSheetSetViewModel(Model(), Templates) { IsExistingDrawings = true };
                NewSheetSetWindow window = Made(wizard, out FrameworkElement body);
                wizard.Next();
                wizard.Name = "Example Tower";
                wizard.Description = "Six-storey residential tower, 48 flats";
                Show(wizard, body);

                List<string> words = Words(body);
                Assert.DoesNotContain(
                    "Create a folder hierarchy based on subsets",
                    Shown<CheckBox>(body).Select(c => (string)c.Content)
                );
                string refusal = "Example Tower.dst already exists in " + Projects + ". Choose another name or folder.";
                Assert.Single(words, w => w == refusal);
                Assert.Equal(
                    new[] { "< Back", "Next > (off)", "Cancel" },
                    Buttons(body)
                        .Where(b =>
                            !b.StartsWith("…", StringComparison.Ordinal)
                            && !b.StartsWith("Sheet Set Properties", StringComparison.Ordinal)
                        )
                        .ToArray()
                );
                Png(body, "a12-wizard-drawings-2-details");
                window.Close();
            });
        }

        [Fact]
        public void ChooseLayouts_ShowsTheTreeWithItsBoxes_TheRefusedLayoutsGreyWithTheirReasons()
        {
            OnSta(() =>
            {
                _ = PackUriHelper.UriSchemePack;
                Examples();
                Towers();
                var wizard = new NewSheetSetViewModel(Model(), Templates) { IsExistingDrawings = true };
                NewSheetSetWindow window = Made(wizard, out FrameworkElement body);
                wizard.Next();
                wizard.Name = "Example Tower";
                wizard.Next();
                Assert.True(wizard.AddDrawingFolder(Drawings));
                wizard.LayoutRows.Single(r => r.Text == "Structural").IsExpanded = false;
                Show(wizard, body);

                List<string> words = Words(body);
                Assert.Contains("Create Sheet Set - Choose Layouts", words);
                Assert.Contains("Pick folders of drawings. The layouts checked become sheets.", words);
                Assert.Contains("Browse…", Buttons(body));
                Assert.Contains("Import Options…", Buttons(body));
                ListBox tree = Shown<ListBox>(body).Single();
                Assert.Equal(wizard.LayoutRows.Count, tree.Items.Count);
                Assert.Contains("# in the name", words);
                Assert.Contains("in Riverside Clinic.dst", words);
                Assert.Contains("Sketch #2", words);
                Assert.Contains("Old cover.dwg", words);
                Assert.DoesNotContain("S-101 Foundation Plan", words);
                Assert.Equal(
                    new[] { "< Back", "Next >", "Cancel" },
                    Buttons(body).Where(b => b.StartsWith("<") || b.StartsWith("Next") || b == "Cancel").ToArray()
                );
                Png(body, "a12-wizard-drawings-3-layouts");
                window.Close();
            });
        }

        [Fact]
        public void TheImportOptionsDialog_HasTheThreeBoxes_TheNoteAndOkCancelHelp()
        {
            OnSta(() =>
            {
                _ = PackUriHelper.UriSchemePack;
                var options = new CadSheetImportOptions
                {
                    PrefixTitleWithFileName = true,
                    CreateSubsetsFromFolders = true,
                    IgnoreTopLevelFolder = true,
                };
                var model = new SheetImportOptionsViewModel(options);
                var window = new SheetImportOptionsWindow(model);
                FrameworkElement body = window.Body;
                CadAppHost.TakeOut(window, body);
                body.DataContext = model;
                Layout(body, 380, 226);

                Assert.Equal(
                    new[]
                    {
                        "Prefix sheet titles with file name",
                        "Create subsets based on folder structure",
                        "Ignore top level folder",
                    },
                    Descendants(body).OfType<CheckBox>().Select(c => (string)c.Content).ToArray()
                );
                Assert.All(Descendants(body).OfType<CheckBox>(), c => Assert.True(c.IsChecked));
                Assert.Equal(
                    new[] { "OK", "Cancel", "Help" },
                    Descendants(body)
                        .OfType<Button>()
                        .Where(b => b.Content is string)
                        .Select(b => (string)b.Content)
                        .ToArray()
                );
                Assert.Contains("Import Options", Descendants(body).OfType<TextBlock>().Select(t => t.Text));

                model.CreateSubsetsFromFolders = false;
                body.UpdateLayout();
                Assert.False(Descendants(body).OfType<CheckBox>().Last().IsEnabled);
                model.CreateSubsetsFromFolders = true;
                Png(body, "a12-wizard-drawings-4-import-options");
                window.Close();
            });
        }

        [Fact]
        public void TheConfirmPage_OfTheDrawingsRoute_ShowsTheSubsetsSheetsAndWhatIsNotImported()
        {
            OnSta(() =>
            {
                _ = PackUriHelper.UriSchemePack;
                Examples();
                Towers();
                var wizard = new NewSheetSetViewModel(Model(), Templates) { IsExistingDrawings = true };
                NewSheetSetWindow window = Made(wizard, out FrameworkElement body);
                wizard.Next();
                wizard.Name = "Example Tower";
                wizard.Description = "Six-storey residential tower, 48 flats";
                wizard.Next();
                Assert.True(wizard.AddDrawingFolder(Drawings));
                wizard.Next();
                Show(wizard, body);

                List<string> words = Words(body);
                Assert.Contains("Create Sheet Set - Confirm", words);
                Assert.Contains(words, w => w.Contains("A-201 Elevations - North and South", StringComparison.Ordinal));
                Assert.Contains(words, w =>
                    w.Contains(
                        "Not imported: Sketch #2 (its name holds #), Cover (a sheet of Riverside Clinic.dst).",
                        StringComparison.Ordinal
                    )
                );
                Assert.Equal(new[] { "< Back", "Finish", "Cancel" }, Buttons(body));
                Png(body, "a12-wizard-drawings-5-confirm");
                window.Close();
            });
        }

        [Fact]
        public void TheWindow_FinishesThroughTheModel_AndTheCrossAndCancelCloseItWithNothingMade()
        {
            OnSta(() =>
            {
                _ = PackUriHelper.UriSchemePack;
                Examples();
                var wizard = new NewSheetSetViewModel(Model(), Templates);
                NewSheetSetWindow window = Made(wizard, out FrameworkElement body);
                Button cross = Descendants(body)
                    .OfType<Button>()
                    .Single(b => b.Style == (Style)window.FindResource("GhTitleCloseButton"));

                Assert.Same(wizard.CancelCommand, cross.Command);
                Assert.Same(
                    wizard.CancelCommand,
                    Shown<Button>(body).Single(b => (b.Content as string) == "Cancel").Command
                );
                Assert.Same(
                    wizard.NextCommand,
                    Shown<Button>(body).Single(b => (b.Content as string) == "Next >").Command
                );
                Assert.Same(
                    wizard.AddFolderCommand,
                    Descendants(body).OfType<Button>().Single(b => (b.Content as string) == "Browse…").Command
                );
                Assert.NotNull(wizard.AskExample);
                Assert.NotNull(wizard.AskStorageFolder);
                Assert.NotNull(wizard.AskDrawingFolder);
                Assert.NotNull(wizard.AskImportOptions);
                Assert.NotNull(wizard.AskProperties);
                window.Close();
            });
        }
    }
}
