using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using GeometryHelper.Dxf;
using GeometryHelper.Geometry;
using OpenCAD2D.SheetSets;
using Xunit;

namespace OpenCAD2D.Wpf.UnitTest
{
    /// <summary>
    /// The Create Sheet Set wizard of NEWSHEETSET (A12 4c), view model only: both routes ending in a set on disk, the pages of
    /// each route, which of Back, Next and Finish are on at each page, the refusals in the wizard's words, nothing written
    /// before Finish, the Import Options, the draft of Sheet Set Properties, and the request NEWSHEETSET answers. The sets and
    /// the drawings are built in code in a temporary folder.
    /// </summary>
    public class NewSheetSetViewModelTests : IDisposable
    {
        private readonly TestFolder _folder = new TestFolder();

        public void Dispose() => _folder.Dispose();

        private string Templates => Path.Combine(_folder.FullPath, "templates");

        private string Projects => Path.Combine(_folder.FullPath, "Projects");

        private string Drawings => Path.Combine(_folder.FullPath, "Drawings");

        private string Dst => Path.Combine(Projects, "Example Tower.dst");

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

            database.ModelSpace.AppendEntity(new DxfCircle(new GeoPoint3(0, 0, 0), GeoVector3.ZAxis, 5));
            string path = Path.Combine(folder, file);
            CadDocument.From(database, file).SaveAs(path);
            return path;
        }

        /// <summary>An example sheet set in the template folder: subsets General, Plans (holding Details), the properties Client (the set's) and Drawn By (every sheet's), and a sheet that the copy must leave out.</summary>
        private string Example(string name = "OpenCAD2D Metric Sheet Set", string title = null)
        {
            Directory.CreateDirectory(Templates);
            string dwt = Drawing(Templates, name + ".dwt", "Cover", "A1 sheet");
            string path = Path.Combine(Templates, name + ".dst");
            CadSheetSetFile file = CadSheetSetFile.Create(path, title ?? name, "Subsets General, Plans and Details");
            file.Change(set =>
            {
                set.Template = new CadDrawingReference(
                    CadFileReference.FromPath(dwt, Templates),
                    "A1 sheet",
                    null,
                    null
                );
                set.PromptForTemplate = false;
                set.NewSubset("General", null, false);
                CadSheetSubset plans = set.NewSubset("Plans", null, false);
                plans.NewSubset("Details", null, false);
                set.AddCustomProperty("Client", "Acme", CadSheetSetPropertyFlags.Set);
                set.AddCustomProperty("Drawn By", "RM", CadSheetSetPropertyFlags.Sheet);
            });
            return path;
        }

        private MainViewModel Model()
        {
            var model = new MainViewModel();
            model.SheetSetStoreFolder = Projects;
            return model;
        }

        /// <summary>The wizard at Begin on the example route, with the example made and the folder of the settings set.</summary>
        private NewSheetSetViewModel Wizard(out MainViewModel model)
        {
            Example();
            model = Model();
            return new NewSheetSetViewModel(model, Templates);
        }

        private static void ToDetails(NewSheetSetViewModel wizard, bool fromDrawings)
        {
            wizard.IsExistingDrawings = fromDrawings;
            wizard.IsFromExample = !fromDrawings;
            wizard.Next();

            if (!fromDrawings)
            {
                wizard.Next();
            }
        }

        private static string[] Pages(NewSheetSetViewModel wizard) => wizard.Pages.Select(p => p.Title).ToArray();

        private static string[] Names(IEnumerable<NewSheetSetLayoutNode> rows) => rows.Select(r => r.Text).ToArray();

        private static string[] Subsets(CadSheetSubset group, string indent = "") =>
            group
                .Children.SelectMany(c =>
                    c is CadSheetSubset subset
                        ? new[] { indent + subset.Name }.Concat(Subsets(subset, indent + " "))
                        : new[] { indent + ((CadSheetSetSheet)c).Title }
                )
                .ToArray();

        /// <summary>
        /// Three drawings in two folders under Drawings: Architectural holds A-101 (one layout) and A-201 (North and South,
        /// East and West, Sketch #2); Structural holds S-101.
        /// </summary>
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
            Drawing(Path.Combine(Drawings, "Structural"), "S-101 Foundation Plan.dwg", "S-101 Foundation Plan");
        }

        private NewSheetSetViewModel ToLayouts(MainViewModel model, string folder)
        {
            var wizard = new NewSheetSetViewModel(model, Templates);
            ToDetails(wizard, true);
            wizard.Next();
            Assert.True(wizard.AddDrawingFolder(folder));
            return wizard;
        }

        // ---- Begin and the route

        [Fact]
        public void AtBegin_BackIsOff_NextIsOn_AndTheRouteIsTheExampleOne()
        {
            NewSheetSetViewModel wizard = Wizard(out _);

            Assert.Equal(NewSheetSetPage.Begin, wizard.Page);
            Assert.Equal("Create Sheet Set - Begin", wizard.PageTitle);
            Assert.False(wizard.CanBack);
            Assert.False(wizard.BackCommand.CanExecute(null));
            Assert.True(wizard.CanNext);
            Assert.False(wizard.CanFinish);
            Assert.True(wizard.IsFromExample);
            Assert.False(wizard.IsExistingDrawings);
            Assert.Equal(new[] { "Begin", "Sheet Set Example", "Sheet Set Details", "Confirm" }, Pages(wizard));
            Assert.Equal("Begin", wizard.Pages.Single(p => p.IsCurrent).Title);
            Assert.Equal(string.Empty, wizard.Message);
        }

        [Fact]
        public void ChoosingExistingDrawings_ChangesThePagesOfTheRoute_AndTheFolderHierarchyBoxGoes()
        {
            NewSheetSetViewModel wizard = Wizard(out _);
            Assert.True(wizard.ShowFolderHierarchy);

            wizard.IsExistingDrawings = true;

            Assert.Equal(new[] { "Begin", "Sheet Set Details", "Choose Layouts", "Confirm" }, Pages(wizard));
            Assert.False(wizard.IsFromExample);
            Assert.False(wizard.ShowFolderHierarchy);
            wizard.Next();
            Assert.Equal(NewSheetSetPage.Details, wizard.Page);
            Assert.Equal("Create Sheet Set - Sheet Set Details", wizard.PageTitle);
        }

        [Fact]
        public void Back_GoesBackOnePageOfTheRoute_FromEveryPage()
        {
            NewSheetSetViewModel wizard = Wizard(out _);
            wizard.Next();
            wizard.Next();
            Assert.Equal(NewSheetSetPage.Details, wizard.Page);

            wizard.Back();
            Assert.Equal(NewSheetSetPage.Example, wizard.Page);
            wizard.Back();
            Assert.Equal(NewSheetSetPage.Begin, wizard.Page);

            wizard.IsExistingDrawings = true;
            wizard.Next();
            wizard.Next();
            Assert.Equal(NewSheetSetPage.ChooseLayouts, wizard.Page);
            wizard.Back();
            Assert.Equal(NewSheetSetPage.Details, wizard.Page);
        }

        // ---- Sheet Set Example

        [Fact]
        public void TheExamples_AreTheDstFilesOfTheTemplateFolder_SortedByName_TheFirstChosen()
        {
            Example("OpenCAD2D Metric Sheet Set");
            Example("New Sheet Set");
            File.WriteAllText(Path.Combine(Templates, "notes.txt"), "not a set");
            MainViewModel model = Model();

            var wizard = new NewSheetSetViewModel(model, Templates);

            Assert.Equal(Templates, wizard.ExampleFolder);
            Assert.Equal(new[] { "New Sheet Set", "OpenCAD2D Metric Sheet Set" }, wizard.Examples.Select(e => e.Name));
            Assert.Equal("New Sheet Set", wizard.SelectedExample.Name);
            Assert.True(wizard.IsListedExample);
        }

        [Fact]
        public void TheBoxUnderTheList_ShowsTheTitleTheDescriptionAndThePathOfTheExampleChosen()
        {
            Example("New Sheet Set");
            Example("OpenCAD2D Metric Sheet Set", "OpenCAD2D Metric");
            var wizard = new NewSheetSetViewModel(Model(), Templates);

            wizard.SelectedExample = wizard.Examples.Single(e => e.Name == "OpenCAD2D Metric Sheet Set");

            Assert.Equal("OpenCAD2D Metric", wizard.ExampleTitle);
            Assert.Equal(
                "Title: OpenCAD2D Metric"
                    + Environment.NewLine
                    + "Description: Subsets General, Plans and Details"
                    + Environment.NewLine
                    + "Path: "
                    + Path.Combine(Templates, "OpenCAD2D Metric Sheet Set.dst"),
                wizard.ExampleDetails
            );
        }

        [Fact]
        public void WithNoExampleInTheFolder_TheBrowseChoiceIsOn_AndNextIsOffUntilAFileIsGiven()
        {
            Directory.CreateDirectory(Templates);
            var wizard = new NewSheetSetViewModel(Model(), Templates);
            wizard.Next();

            Assert.True(wizard.IsBrowsedExample);
            Assert.Empty(wizard.Examples);
            Assert.Equal(NewSheetSetPage.Example, wizard.Page);
            Assert.Equal("Browse to the sheet set data file to use as an example.", wizard.Message);
            Assert.False(wizard.CanNext);

            string other = Example("Other");
            Directory.Move(Templates, Templates + "-moved");
            wizard.BrowsedExamplePath = Path.Combine(Templates + "-moved", "Other.dst");
            Assert.Equal(string.Empty, wizard.Message);
            Assert.True(wizard.CanNext);
            Assert.Equal(other, Path.Combine(Templates, "Other.dst"));
        }

        [Fact]
        public void AFileThatIsNotThere_OrIsNoSheetSet_IsRefusedInTheWizardsWords_AndNextStaysOff()
        {
            NewSheetSetViewModel wizard = Wizard(out _);
            wizard.Next();
            wizard.IsBrowsedExample = true;

            wizard.BrowsedExamplePath = Path.Combine(Templates, "gone.dst");
            Assert.Equal(
                "The sheet set data file " + Path.Combine(Templates, "gone.dst") + " is not there.",
                wizard.Message
            );
            Assert.False(wizard.CanNext);
            Assert.False(wizard.NextCommand.CanExecute(null));

            string text = _folder.WriteText("plain.dst", "this is no sheet set");
            wizard.BrowsedExamplePath = text;
            Assert.StartsWith(text + " is not a sheet set data file.", wizard.Message, StringComparison.Ordinal);
            Assert.False(wizard.CanNext);
            wizard.Next();
            Assert.Equal(NewSheetSetPage.Example, wizard.Page);

            wizard.IsListedExample = true;
            Assert.Equal(string.Empty, wizard.Message);
            Assert.True(wizard.CanNext);
        }

        [Fact]
        public void Browse_TakesTheFileTheWindowGives_AndMakesItTheExample()
        {
            NewSheetSetViewModel wizard = Wizard(out _);
            string other = Example("Other", "Other Example");
            wizard.Next();
            string asked = null;
            wizard.AskExample = present =>
            {
                asked = present;
                return other;
            };

            wizard.BrowseExample();

            Assert.Equal(string.Empty, asked);
            Assert.True(wizard.IsBrowsedExample);
            Assert.Equal(other, wizard.BrowsedExamplePath);
            Assert.Equal("Other Example", wizard.ExampleTitle);
        }

        // ---- Sheet Set Details

        [Fact]
        public void TheNameBeginsAsNewSheetSet_AndTakesANumberWhereTheDstIsThereAlready()
        {
            Example();
            MainViewModel model = Model();
            Assert.Equal("New Sheet Set", new NewSheetSetViewModel(model, Templates).Name);

            Directory.CreateDirectory(Projects);
            File.WriteAllText(Path.Combine(Projects, "New Sheet Set.dst"), "x");
            File.WriteAllText(Path.Combine(Projects, "New Sheet Set 1.dst"), "x");

            var wizard = new NewSheetSetViewModel(model, Templates);
            Assert.Equal("New Sheet Set 2", wizard.Name);
            Assert.Equal(Projects, wizard.Folder);
            Assert.Equal(Path.Combine(Projects, "New Sheet Set 2.dst"), wizard.DstPath);
        }

        [Fact]
        public void TheDetailsPage_RefusesWhatIsWrong_InItsWords_AndNextIsOffWhileTheLineIsRed()
        {
            NewSheetSetViewModel wizard = Wizard(out _);
            ToDetails(wizard, false);

            Assert.True(wizard.CanNext);

            wizard.Name = "  ";
            Assert.Equal("Name the sheet set.", wizard.Message);
            Assert.False(wizard.CanNext);
            Assert.Null(wizard.DstPath);

            wizard.Name = "Tower: north";
            Assert.Equal(
                "The name of a sheet set is the name of its file, so it cannot hold any of \\ / : * ? \" < > |.",
                wizard.Message
            );
            Assert.False(wizard.CanNext);

            wizard.Name = "Example Tower";
            wizard.Folder = string.Empty;
            Assert.Equal("Say where to store the sheet set data file.", wizard.Message);

            wizard.Folder = "Projects\\Tower";
            Assert.Equal("Give the whole path of the folder, as D:\\Projects\\Tower.", wizard.Message);
            Assert.False(wizard.CanNext);

            string file = _folder.WriteText("a-file", "x");
            wizard.Folder = file;
            Assert.Equal(file + " is a file, not a folder.", wizard.Message);

            wizard.Folder = Projects;
            Assert.Equal(string.Empty, wizard.Message);
            Assert.True(wizard.CanNext);
            Assert.True(wizard.HasMessage == false);
        }

        [Fact]
        public void ADstThatIsThereAlready_IsRefused_AndChoosingAnotherNameClearsIt()
        {
            NewSheetSetViewModel wizard = Wizard(out _);
            Directory.CreateDirectory(Projects);
            File.WriteAllText(Dst, "already here");
            ToDetails(wizard, false);

            wizard.Name = "Example Tower";

            Assert.Equal(
                "Example Tower.dst already exists in " + Projects + ". Choose another name or folder.",
                wizard.Message
            );
            Assert.False(wizard.CanNext);
            wizard.Name = "Example Tower 2";
            Assert.True(wizard.CanNext);
        }

        [Fact]
        public void TheFolderHierarchyBox_IsOnTheExampleRouteOnly()
        {
            NewSheetSetViewModel wizard = Wizard(out _);
            Assert.True(wizard.ShowFolderHierarchy);
            Assert.False(wizard.CreateFolderHierarchy);

            wizard.IsExistingDrawings = true;

            Assert.False(wizard.ShowFolderHierarchy);
        }

        // ---- Nothing before Finish

        [Fact]
        public void WalkingToConfirm_AndBack_AndCancel_WriteNothing_NoFileNoFolder()
        {
            NewSheetSetViewModel wizard = Wizard(out MainViewModel model);
            ToDetails(wizard, false);
            wizard.CreateFolderHierarchy = true;
            wizard.Next();

            Assert.Equal(NewSheetSetPage.Confirm, wizard.Page);
            Assert.False(Directory.Exists(Projects));
            wizard.Back();
            wizard.Next();
            bool? closed = null;
            wizard.CloseRequested += (s, ok) => closed = ok;

            wizard.Cancel();

            Assert.False(closed);
            Assert.False(Directory.Exists(Projects));
            Assert.False(File.Exists(Dst));
            Assert.Empty(model.SheetSetManager.Rows);
            Assert.Empty(model.Settings.RecentSheetSets);
        }

        [Fact]
        public void TheCancelCommand_IsWhatTheCrossRuns_AndClosesWithNothingMade()
        {
            NewSheetSetViewModel wizard = Wizard(out _);
            bool? closed = null;
            wizard.CloseRequested += (s, ok) => closed = ok;

            wizard.CancelCommand.Execute(null);

            Assert.False(closed);
            Assert.Null(wizard.CreatedFile);
        }

        // ---- Confirm and Finish, the example route

        [Fact]
        public void TheConfirmPage_OfTheExampleRoute_PreviewsTheSetInPlainText_FinishIsOnAndNextOff()
        {
            NewSheetSetViewModel wizard = Wizard(out _);
            ToDetails(wizard, false);
            wizard.Name = "Example Tower";
            wizard.Description = "Six-storey residential tower, 48 flats";
            wizard.CreateFolderHierarchy = true;
            wizard.Next();

            Assert.Equal(NewSheetSetPage.Confirm, wizard.Page);
            Assert.False(wizard.CanNext);
            Assert.True(wizard.CanFinish);
            Assert.True(wizard.FinishCommand.CanExecute(null));
            Assert.True(wizard.CanBack);
            Assert.Equal("Example Tower", wizard.PreviewTitle);
            string[] lines = wizard.PreviewBody.Split(new[] { Environment.NewLine }, StringSplitOptions.None);
            Assert.Equal("Sheet set data file: " + Dst, lines[0]);
            Assert.Equal("Example: OpenCAD2D Metric Sheet Set", lines[1]);
            Assert.Equal("Description: Six-storey residential tower, 48 flats", lines[2]);
            Assert.Equal("Sheet storage location: " + Projects, lines[3]);
            Assert.StartsWith("Sheet creation template: A1 sheet (", lines[4], StringComparison.Ordinal);
            Assert.EndsWith("OpenCAD2D Metric Sheet Set.dwt)", lines[4], StringComparison.Ordinal);
            Assert.Equal("Prompt for template: No", lines[5]);
            Assert.Equal("Folders made: General, Plans, Plans\\Details", lines[6]);
            Assert.Equal(
                new[]
                {
                    "Subsets:",
                    "  General",
                    "  Plans",
                    "    Details",
                    string.Empty,
                    "Sheet set custom properties:",
                    "  Client",
                    string.Empty,
                    "Sheet custom properties:",
                    "  Drawn By",
                },
                lines.Skip(8).ToArray()
            );
        }

        [Fact]
        public void Finish_OfTheExampleRoute_MakesTheSetOnDisk_WithTheExamplesSubsetsAndProperties_AndNoSheets()
        {
            NewSheetSetViewModel wizard = Wizard(out MainViewModel model);
            ToDetails(wizard, false);
            wizard.Name = "Example Tower";
            wizard.Description = "Six-storey residential tower";
            wizard.CreateFolderHierarchy = true;
            wizard.Next();
            bool? closed = null;
            wizard.CloseRequested += (s, ok) => closed = ok;

            Assert.True(wizard.Finish());

            Assert.True(closed);
            Assert.Equal(Dst, wizard.CreatedFile);
            Assert.True(File.Exists(Dst));
            CadSheetSet set = CadSheetSetFile.Open(Dst).SheetSet;
            Assert.Equal("Example Tower", set.Name);
            Assert.Equal("Six-storey residential tower", set.Description);
            Assert.Equal(new[] { "General", "Plans", " Details" }, Subsets(set));
            Assert.Empty(set.AllSheets());
            Assert.Equal("Acme", set.FindCustomProperty("Client").Value);
            Assert.True(set.FindCustomProperty("Drawn By").IsSheetProperty);
            Assert.True(Directory.Exists(Path.Combine(Projects, "General")));
            Assert.True(Directory.Exists(Path.Combine(Projects, "Plans", "Details")));
            Assert.Equal("A1 sheet", set.Template.Name);
            Assert.False(wizard.CanFinish);
        }

        [Fact]
        public void Finish_OpensTheSetInThePalette_PutsItFirstInRecent_AndKeepsTheChoicesInTheSettings()
        {
            NewSheetSetViewModel wizard = Wizard(out MainViewModel model);
            int changes = 0;
            model.SettingsChanged += (s, e) => changes++;
            ToDetails(wizard, false);
            wizard.Name = "Example Tower";
            wizard.Folder = Path.Combine(Projects, "Tower");
            wizard.Next();

            Assert.True(wizard.Finish());

            string dst = Path.Combine(Projects, "Tower", "Example Tower.dst");
            Assert.True(File.Exists(dst));
            Assert.True(model.SheetSetManager.HasSet);
            Assert.Equal(dst, model.SheetSetManager.Current.Path);
            Assert.Equal("Example Tower", model.SheetSetManager.ControlText);
            Assert.Equal(dst, model.Settings.RecentSheetSets[0]);
            Assert.Equal(Path.Combine(Projects, "Tower"), model.SheetSetStoreFolder);
            Assert.False(model.Settings.NewSheetSetFromDrawings);
            Assert.True(changes > 0);
        }

        [Fact]
        public void Finish_RefusesADstThatAppearedAfterTheDetailsPage_OnTheLine_AndLeavesTheWizardOpen()
        {
            NewSheetSetViewModel wizard = Wizard(out MainViewModel model);
            ToDetails(wizard, false);
            wizard.Name = "Example Tower";
            wizard.Next();
            Directory.CreateDirectory(Projects);
            File.WriteAllText(Dst, "somebody else's");
            bool? closed = null;
            wizard.CloseRequested += (s, ok) => closed = ok;

            Assert.False(wizard.Finish());

            Assert.Null(closed);
            Assert.Equal(
                "Example Tower.dst already exists in " + Projects + ". Choose another name or folder.",
                wizard.Message
            );
            Assert.Equal("somebody else's", File.ReadAllText(Dst));
            Assert.False(model.SheetSetManager.HasSet);
            Assert.Null(wizard.CreatedFile);
        }

        [Fact]
        public void Finish_WhereTheExampleHasGoneAfterTheChoice_IsRefusedInTheEnginesWords_AndNothingIsLeft()
        {
            NewSheetSetViewModel wizard = Wizard(out _);
            ToDetails(wizard, false);
            wizard.Next();
            File.Delete(Path.Combine(Templates, "OpenCAD2D Metric Sheet Set.dst"));

            Assert.False(wizard.Finish());

            Assert.StartsWith("The sheet set was not made. ", wizard.Message, StringComparison.Ordinal);
            Assert.False(File.Exists(wizard.DstPath));
            Assert.Equal(NewSheetSetPage.Confirm, wizard.Page);
        }

        // ---- Choose Layouts, the existing drawings route

        [Fact]
        public void ChooseLayouts_WithNoFolder_AsksForOne_AndNextIsOff()
        {
            MainViewModel model = Model();
            var wizard = new NewSheetSetViewModel(model, Templates);
            ToDetails(wizard, true);
            wizard.Next();

            Assert.Equal(NewSheetSetPage.ChooseLayouts, wizard.Page);
            Assert.Equal("Pick a folder of drawings with Browse….", wizard.Message);
            Assert.False(wizard.CanNext);
            Assert.Empty(wizard.LayoutRows);
        }

        [Fact]
        public void TheTree_IsTheFolder_ItsDrawingsAndFolders_SortedByName_EachDrawingWithItsLayouts()
        {
            Towers();
            var wizard = ToLayouts(Model(), Drawings);

            Assert.Equal(
                new[]
                {
                    Drawings,
                    "Architectural",
                    "A-101 Ground Floor Plan.dwg",
                    "A-101 Ground Floor Plan",
                    "A-201 Elevations.dwg",
                    "North and South",
                    "East and West",
                    "Sketch #2",
                    "Structural",
                    "S-101 Foundation Plan.dwg",
                    "S-101 Foundation Plan",
                },
                Names(wizard.LayoutRows)
            );
            Assert.Equal(
                new[]
                {
                    NewSheetSetNodeKind.Folder,
                    NewSheetSetNodeKind.Folder,
                    NewSheetSetNodeKind.Drawing,
                    NewSheetSetNodeKind.Layout,
                    NewSheetSetNodeKind.Drawing,
                    NewSheetSetNodeKind.Layout,
                    NewSheetSetNodeKind.Layout,
                    NewSheetSetNodeKind.Layout,
                    NewSheetSetNodeKind.Folder,
                    NewSheetSetNodeKind.Drawing,
                    NewSheetSetNodeKind.Layout,
                },
                wizard.LayoutRows.Select(r => r.Kind)
            );
            Assert.Equal(new[] { 0, 1, 2, 3, 2, 3, 3, 3, 1, 2, 3 }, wizard.LayoutRows.Select(r => r.Depth));
        }

        [Fact]
        public void ALayoutWithAHashInItsName_IsGreyClearAndCannotBeTicked_WithItsReason()
        {
            Towers();
            var wizard = ToLayouts(Model(), Drawings);
            NewSheetSetLayoutNode sketch = wizard.LayoutRows.Single(r => r.Text == "Sketch #2");

            Assert.True(sketch.IsRefused);
            Assert.False(sketch.IsCheckable);
            Assert.False(sketch.IsChecked);
            Assert.Equal("# in the name", sketch.Note);
            Assert.Contains("holds a #", sketch.Refusal, StringComparison.Ordinal);

            sketch.IsChecked = true;

            Assert.False(sketch.IsChecked);
            Assert.Equal(4, wizard.TickedLayoutCount);
        }

        [Fact]
        public void ALayoutThatIsASheetOfAnotherSet_IsRefused_WithTheNameOfThatSet()
        {
            Towers();
            string cover = Drawing(Path.Combine(Drawings, "Old"), "Old cover.dwg", "Cover");
            Directory.CreateDirectory(Path.Combine(_folder.FullPath, "Riverside"));
            CadSheetSetFile.CreateFromDrawings(
                Path.Combine(_folder.FullPath, "Riverside", "Riverside Clinic.dst"),
                "Riverside Clinic",
                null,
                new[] { new CadSheetImportSource(null, cover, new[] { "Cover" }) },
                null
            );

            var wizard = ToLayouts(Model(), Drawings);

            NewSheetSetLayoutNode refused = wizard.LayoutRows.Single(r => r.Text == "Cover");
            Assert.True(refused.IsRefused);
            Assert.Equal("in Riverside Clinic.dst", refused.Note);
            Assert.False(refused.IsChecked);
            NewSheetSetLayoutNode old = wizard.LayoutRows.Single(r => r.Text == "Old cover.dwg");
            Assert.False(old.IsCheckable);
            Assert.Equal(4, wizard.TickedLayoutCount);
        }

        [Fact]
        public void TheLayoutsThatCanBeSheets_AreTickedAtFirst_AndATickOnAFolderOrDrawing_TicksEverythingBelow()
        {
            Towers();
            var wizard = ToLayouts(Model(), Drawings);
            NewSheetSetLayoutNode root = wizard.LayoutRows[0];
            NewSheetSetLayoutNode architectural = wizard.LayoutRows.Single(r => r.Text == "Architectural");
            NewSheetSetLayoutNode elevations = wizard.LayoutRows.Single(r => r.Text == "A-201 Elevations.dwg");

            Assert.True(root.IsChecked);
            Assert.True(elevations.IsChecked);
            Assert.Equal(4, wizard.TickedLayoutCount);

            elevations.IsChecked = false;

            Assert.Equal(2, wizard.TickedLayoutCount);
            Assert.False(elevations.IsChecked);
            Assert.Null(architectural.IsChecked);
            Assert.Null(root.IsChecked);

            architectural.IsChecked = true;

            Assert.True(elevations.IsChecked);
            Assert.True(architectural.IsChecked);
            Assert.Equal(4, wizard.TickedLayoutCount);
            Assert.False(wizard.LayoutRows.Single(r => r.Text == "Sketch #2").IsChecked);
        }

        [Fact]
        public void ClickingAMixedBox_TicksIt_AsTheDesignHasIt()
        {
            Towers();
            var wizard = ToLayouts(Model(), Drawings);
            NewSheetSetLayoutNode elevations = wizard.LayoutRows.Single(r => r.Text == "A-201 Elevations.dwg");
            wizard.LayoutRows.Single(r => r.Text == "East and West").IsChecked = false;
            Assert.Null(elevations.IsChecked);

            // The box is clicked: WPF hands a mixed box back as false.
            elevations.IsChecked = false;

            Assert.True(elevations.IsChecked);
            Assert.True(wizard.LayoutRows.Single(r => r.Text == "East and West").IsChecked);
        }

        [Fact]
        public void WithNoLayoutTicked_NextIsOff_AndTheLineSaysToTickOne()
        {
            Towers();
            var wizard = ToLayouts(Model(), Drawings);

            wizard.LayoutRows[0].IsChecked = false;

            Assert.Equal(0, wizard.TickedLayoutCount);
            Assert.Equal("Check the layouts that are to become sheets.", wizard.Message);
            Assert.False(wizard.CanNext);

            wizard.LayoutRows.Single(r => r.Text == "S-101 Foundation Plan").IsChecked = true;

            Assert.Equal(string.Empty, wizard.Message);
            Assert.True(wizard.CanNext);
        }

        [Fact]
        public void AFolderWithNoDrawing_IsNotAdded_AndAnUnreadableDrawingIsLeftOut_WithTheNameOnTheLine()
        {
            Directory.CreateDirectory(Path.Combine(_folder.FullPath, "empty"));
            File.WriteAllText(Path.Combine(_folder.FullPath, "empty", "notes.txt"), "x");
            var wizard = new NewSheetSetViewModel(Model(), Templates);
            ToDetails(wizard, true);
            wizard.Next();

            Assert.False(wizard.AddDrawingFolder(Path.Combine(_folder.FullPath, "empty")));
            Assert.Equal(
                "There is no drawing (DWG) in " + Path.Combine(_folder.FullPath, "empty") + " or the folders below it.",
                wizard.Message
            );
            Assert.Empty(wizard.LayoutRows);

            Towers();
            File.WriteAllText(Path.Combine(Drawings, "Structural", "broken.dwg"), "not a drawing");
            Assert.True(wizard.AddDrawingFolder(Drawings));
            Assert.StartsWith(
                "A drawing could not be read and is left out. broken.dwg: ",
                wizard.Message,
                StringComparison.Ordinal
            );
            Assert.DoesNotContain("broken.dwg", Names(wizard.LayoutRows));

            Assert.False(wizard.AddDrawingFolder(Path.Combine(_folder.FullPath, "nowhere")));
            Assert.Equal("The folder " + Path.Combine(_folder.FullPath, "nowhere") + " is not there.", wizard.Message);
        }

        [Fact]
        public void AFolderAddedTwice_IsListedOnce_AndADrawingAlreadyListedIsNotListedAgainUnderAnotherFolder()
        {
            Towers();
            var wizard = ToLayouts(Model(), Drawings);
            int rows = wizard.LayoutRows.Count;

            Assert.True(wizard.AddDrawingFolder(Drawings + Path.DirectorySeparatorChar));
            Assert.Equal(rows, wizard.LayoutRows.Count);

            Assert.False(wizard.AddDrawingFolder(Path.Combine(Drawings, "Structural")));
            Assert.Equal(rows, wizard.LayoutRows.Count);
            Assert.Single(wizard.LayoutRows, r => r.Text == "S-101 Foundation Plan.dwg");
        }

        [Fact]
        public void SeveralFolders_StayInTheListInTheOrderTheyWereAdded_AndTheirDrawingsAreTheSourcesInThatOrder()
        {
            Towers();
            Drawing(Path.Combine(_folder.FullPath, "More"), "M-001.dwg", "M-001");
            var wizard = ToLayouts(Model(), Path.Combine(Drawings, "Structural"));

            Assert.True(wizard.AddDrawingFolder(Path.Combine(_folder.FullPath, "More")));
            Assert.True(wizard.AddDrawingFolder(Path.Combine(Drawings, "Architectural")));

            Assert.Equal(
                new[]
                {
                    "S-101 Foundation Plan.dwg",
                    "M-001.dwg",
                    "A-101 Ground Floor Plan.dwg",
                    "A-201 Elevations.dwg",
                },
                wizard.Sources().Select(s => Path.GetFileName(s.DrawingFile))
            );
            Assert.Equal(
                new[]
                {
                    Path.Combine(Drawings, "Structural"),
                    Path.Combine(_folder.FullPath, "More"),
                    Path.Combine(Drawings, "Architectural"),
                    Path.Combine(Drawings, "Architectural"),
                },
                wizard.Sources().Select(s => s.RootFolder)
            );
        }

        [Fact]
        public void ARowCollapsed_HidesItsRows_AndTheTreeKeepsTheTicks()
        {
            Towers();
            var wizard = ToLayouts(Model(), Drawings);
            wizard.LayoutRows.Single(r => r.Text == "Architectural").IsExpanded = false;

            Assert.Equal(
                new[] { Drawings, "Architectural", "Structural", "S-101 Foundation Plan.dwg", "S-101 Foundation Plan" },
                Names(wizard.LayoutRows)
            );
            Assert.Equal(4, wizard.TickedLayoutCount);
        }

        // ---- Import Options

        [Fact]
        public void TheImportOptions_AreAllOnByDefault_AndKeptInTheSettingsAfterOK()
        {
            Towers();
            MainViewModel model = Model();
            var wizard = ToLayouts(model, Drawings);

            Assert.True(wizard.ImportOptions.PrefixTitleWithFileName);
            Assert.True(wizard.ImportOptions.CreateSubsetsFromFolders);
            Assert.True(wizard.ImportOptions.IgnoreTopLevelFolder);

            SheetImportOptionsViewModel shown = null;
            wizard.AskImportOptions = dialog =>
            {
                shown = dialog;
                Assert.True(dialog.CanIgnoreTopLevelFolder);
                dialog.PrefixTitleWithFileName = false;
                dialog.CreateSubsetsFromFolders = false;
                Assert.False(dialog.CanIgnoreTopLevelFolder);
                dialog.Ok();
                return true;
            };

            wizard.ImportOptionsCommand.Execute(null);

            Assert.NotNull(shown);
            Assert.False(wizard.ImportOptions.PrefixTitleWithFileName);
            Assert.False(wizard.ImportOptions.CreateSubsetsFromFolders);
            Assert.True(wizard.ImportOptions.IgnoreTopLevelFolder);
            Assert.False(model.Settings.SheetImportPrefixTitles);
            Assert.False(model.Settings.SheetImportSubsetsFromFolders);
            Assert.True(model.Settings.SheetImportIgnoreTopFolder);
            Assert.False(new NewSheetSetViewModel(model, Templates).ImportOptions.PrefixTitleWithFileName);
        }

        [Fact]
        public void ImportOptions_CancelledOrWithNoDialog_ChangeNothing()
        {
            Towers();
            MainViewModel model = Model();
            var wizard = ToLayouts(model, Drawings);

            wizard.ImportOptionsCommand.Execute(null);
            wizard.AskImportOptions = dialog =>
            {
                dialog.PrefixTitleWithFileName = false;
                return false;
            };
            wizard.ImportOptionsCommand.Execute(null);

            Assert.True(wizard.ImportOptions.PrefixTitleWithFileName);
            Assert.True(model.Settings.SheetImportPrefixTitles);
        }

        [Fact]
        public void TheImportOptionsDialog_KnowsItsCommands_AndTheHelpAddress()
        {
            var options = new CadSheetImportOptions { CreateSubsetsFromFolders = true };
            var dialog = new SheetImportOptionsViewModel(options);
            bool? closed = null;
            dialog.CloseRequested += (s, ok) => closed = ok;

            dialog.CancelCommand.Execute(null);
            Assert.False(closed);
            Assert.False(options.PrefixTitleWithFileName);

            dialog.PrefixTitleWithFileName = true;
            dialog.OkCommand.Execute(null);
            Assert.True(closed);
            Assert.True(options.PrefixTitleWithFileName);
            Assert.Throws<ArgumentNullException>(() => new SheetImportOptionsViewModel(null));
        }

        // ---- Confirm and Finish, the existing drawings route

        private static string[] Lines(string text) =>
            text.Split(new[] { Environment.NewLine }, StringSplitOptions.None);

        [Fact]
        public void TheConfirmPage_OfTheDrawingsRoute_ListsTheSubsetsAndSheets_AndWhatIsNotImported()
        {
            Towers();
            var wizard = ToLayouts(Model(), Drawings);
            wizard.Name = "Example Tower";
            wizard.Description = "Six-storey residential tower";
            wizard.Next();

            Assert.Equal(NewSheetSetPage.Confirm, wizard.Page);
            Assert.True(wizard.CanFinish);
            Assert.False(wizard.CanNext);
            Assert.Equal("Example Tower", wizard.PreviewTitle);
            Assert.Equal(
                new[]
                {
                    "Sheet set data file: " + Dst,
                    "Description: Six-storey residential tower",
                    string.Empty,
                    "Architectural",
                    "  A-101 Ground Floor Plan - A-101 Ground Floor Plan",
                    "  A-201 Elevations - North and South",
                    "  A-201 Elevations - East and West",
                    "Structural",
                    "  S-101 Foundation Plan - S-101 Foundation Plan",
                    string.Empty,
                    "4 sheets in 2 subsets. Not imported: Sketch #2 (its name holds #).",
                },
                Lines(wizard.PreviewBody)
            );
        }

        [Fact]
        public void TheImportOptions_ChangeThePreview_TitlesSubsetsAndTheTopFolder()
        {
            Towers();
            var wizard = ToLayouts(Model(), Drawings);
            wizard.ImportOptions.PrefixTitleWithFileName = false;
            wizard.ImportOptions.IgnoreTopLevelFolder = false;
            wizard.Next();

            Assert.Equal(
                new[]
                {
                    "Drawings",
                    "  Architectural",
                    "    A-101 Ground Floor Plan",
                    "    North and South",
                    "    East and West",
                    "  Structural",
                    "    S-101 Foundation Plan",
                },
                Lines(wizard.PreviewBody).Skip(2).Take(7)
            );

            wizard.Back();
            wizard.ImportOptions.CreateSubsetsFromFolders = false;
            wizard.Next();

            Assert.Equal(
                new[]
                {
                    "A-101 Ground Floor Plan",
                    "North and South",
                    "East and West",
                    "S-101 Foundation Plan",
                    string.Empty,
                    "4 sheets. Not imported: Sketch #2 (its name holds #).",
                },
                Lines(wizard.PreviewBody).Skip(2)
            );
        }

        [Fact]
        public void Finish_OfTheDrawingsRoute_MakesTheSetOnDisk_WithTheSheetsInTreeOrder_InSubsetsOfTheFolders()
        {
            Towers();
            MainViewModel model = Model();
            var wizard = ToLayouts(model, Drawings);
            wizard.Name = "Example Tower";
            wizard.Next();
            bool? closed = null;
            wizard.CloseRequested += (s, ok) => closed = ok;

            Assert.True(wizard.Finish());

            Assert.True(closed);
            Assert.Equal(Dst, wizard.CreatedFile);
            CadSheetSet set = CadSheetSetFile.Open(Dst).SheetSet;
            Assert.Equal(
                new[]
                {
                    "Architectural",
                    " A-101 Ground Floor Plan - A-101 Ground Floor Plan",
                    " A-201 Elevations - North and South",
                    " A-201 Elevations - East and West",
                    "Structural",
                    " S-101 Foundation Plan - S-101 Foundation Plan",
                },
                Subsets(set)
            );
            Assert.All(set.AllSheets(), s => Assert.Equal(string.Empty, s.Number));
            Assert.All(set.AllSheets(), s => Assert.False(s.FindLayout().IsMissing));
            Assert.Equal(Dst, model.SheetSetManager.Current.Path);
            Assert.Equal(Dst, model.Settings.RecentSheetSets[0]);
            Assert.True(model.Settings.NewSheetSetFromDrawings);
            Assert.True(new NewSheetSetViewModel(model, Templates).IsFromDrawings);
        }

        [Fact]
        public void TheDrawingsUsed_CarryTheMarkOfTheNewSet_SoTheyAreRefusedForAnotherOne()
        {
            Towers();
            var wizard = ToLayouts(Model(), Drawings);
            wizard.Next();
            Assert.True(wizard.Finish());

            IReadOnlyList<CadSheetImportLayout> checks = CadSheetSetFile.CheckLayouts(
                Path.Combine(Drawings, "Structural", "S-101 Foundation Plan.dwg")
            );

            Assert.Single(checks);
            Assert.True(checks[0].IsRefused);
            Assert.Contains("New Sheet Set", checks[0].Refusal, StringComparison.Ordinal);
        }

        [Fact]
        public void ThePreview_IsTheSetThatFinishMakes_ForEveryCombinationOfTheImportOptions()
        {
            foreach (bool prefix in new[] { false, true })
            {
                foreach (bool subsets in new[] { false, true })
                {
                    foreach (bool ignoreTop in new[] { false, true })
                    {
                        using (var run = new TestFolder())
                        {
                            string drawings = Path.Combine(run.FullPath, "Drawings");
                            Drawing(Path.Combine(drawings, "A", "deep"), "d1.dwg", "One", "Two");
                            Drawing(Path.Combine(drawings, "A"), "a1.dwg", "Three");
                            Drawing(drawings, "top.dwg", "Four");
                            Drawing(Path.Combine(drawings, "B"), "b1.dwg", "Five");
                            MainViewModel model = Model();
                            model.SheetSetStoreFolder = Path.Combine(run.FullPath, "out");
                            var wizard = ToLayouts(model, drawings);
                            wizard.ImportOptions.PrefixTitleWithFileName = prefix;
                            wizard.ImportOptions.CreateSubsetsFromFolders = subsets;
                            wizard.ImportOptions.IgnoreTopLevelFolder = ignoreTop;
                            wizard.Next();
                            string[] preview = Lines(wizard.PreviewBody)
                                .Skip(2)
                                .TakeWhile(l => l.Length > 0)
                                .Select(l => l.Replace("  ", " "))
                                .Select(l => l.TrimEnd())
                                .ToArray();

                            Assert.True(wizard.Finish());

                            string[] made = Subsets(CadSheetSetFile.Open(wizard.CreatedFile).SheetSet);
                            Assert.Equal(made, preview);
                        }
                    }
                }
            }
        }

        [Fact]
        public void Finish_OfTheDrawingsRoute_WhereALayoutBecameASheetMeanwhile_IsRefused_AndLeavesNoDst()
        {
            Towers();
            var wizard = ToLayouts(Model(), Drawings);
            wizard.Next();
            CadSheetSetFile.CreateFromDrawings(
                Path.Combine(_folder.FullPath, "other.dst"),
                "Other",
                null,
                new[]
                {
                    new CadSheetImportSource(
                        Drawings,
                        Path.Combine(Drawings, "Structural", "S-101 Foundation Plan.dwg"),
                        new[] { "S-101 Foundation Plan" }
                    ),
                },
                null
            );

            Assert.False(wizard.Finish());

            Assert.StartsWith("The sheet set was not made. ", wizard.Message, StringComparison.Ordinal);
            Assert.Contains("Other", wizard.Message, StringComparison.Ordinal);
            Assert.False(File.Exists(Dst));
            Assert.Null(wizard.CreatedFile);
        }

        // ---- Sheet Set Properties

        [Fact]
        public void SheetSetProperties_WhereTheWindowHasNoDialog_SaysSoOnTheLine_AndMakesNoDraft()
        {
            NewSheetSetViewModel wizard = Wizard(out _);
            ToDetails(wizard, false);

            wizard.PropertiesCommand.Execute(null);

            Assert.Equal("There is no Sheet Set Properties dialog to show here.", wizard.Message);
            Assert.Null(wizard.Draft);
        }

        [Fact]
        public void TheSheetSetPropertiesDialogOfTheWizard_HasTheNameAndTheDataFileReadOnly_ButNotOtherwise()
        {
            NewSheetSetViewModel wizard = Wizard(out _);
            ToDetails(wizard, false);
            CadSheetSetFile draft = null;
            wizard.AskProperties = d =>
            {
                draft = d;
                return false;
            };
            wizard.OpenProperties();

            var wizardsDialog = new SheetSetPropertiesViewModel(
                draft,
                new SheetSetDialogOptions { NameReadOnly = true }
            );
            var plainDialog = new SheetSetPropertiesViewModel(draft);

            Assert.True(wizardsDialog.Rows.Single(r => r.Label == "Name").IsReadOnly);
            Assert.True(wizardsDialog.Rows.Single(r => r.Label == "Sheet set data file").IsReadOnly);
            Assert.False(wizardsDialog.Rows.Single(r => r.Label == "Project number").IsReadOnly);
            Assert.False(plainDialog.Rows.Single(r => r.Label == "Name").IsReadOnly);
        }

        [Theory]
        [InlineData(false)]
        [InlineData(true)]
        public void SheetSetProperties_EditADraft_WhichFinishWritesIntoTheSetMade_AndTheDraftIsDeleted(
            bool fromDrawings
        )
        {
            Example();
            Towers();
            MainViewModel model = Model();
            var wizard = new NewSheetSetViewModel(model, Templates);
            ToDetails(wizard, fromDrawings);
            wizard.Name = "Example Tower";
            string draftFolder = null;
            wizard.AskProperties = draft =>
            {
                draftFolder = draft.Folder;
                Assert.Equal("Example Tower", draft.SheetSet.Name);
                draft.Change(set =>
                {
                    set.ProjectNumber = "P-77";
                    set.ProjectName = "Tower";
                    set.PromptForTemplate = true;
                    set.Description = "Edited in the dialog";
                    set.AddCustomProperty("Checked By", "AB", CadSheetSetPropertyFlags.Sheet);
                    set.SetCustomProperty("Client", "Zed", CadSheetSetPropertyFlags.Set);
                });
                return true;
            };

            wizard.PropertiesCommand.Execute(null);

            Assert.NotNull(wizard.Draft);
            Assert.True(Directory.Exists(draftFolder));
            Assert.Equal("Edited in the dialog", wizard.Description);
            Assert.False(File.Exists(Dst));

            if (fromDrawings)
            {
                wizard.Next();
                wizard.AddDrawingFolder(Path.Combine(Drawings, "Structural"));
            }

            wizard.Next();
            Assert.True(wizard.Finish());

            CadSheetSet set = CadSheetSetFile.Open(Dst).SheetSet;
            Assert.Equal("P-77", set.ProjectNumber);
            Assert.Equal("Tower", set.ProjectName);
            Assert.True(set.PromptForTemplate);
            Assert.Equal("Edited in the dialog", set.Description);
            Assert.Equal("AB", set.FindCustomProperty("Checked By").Value);
            Assert.True(set.FindCustomProperty("Checked By").IsSheetProperty);

            Assert.Equal("Zed", set.FindCustomProperty("Client").Value);
            if (fromDrawings)
            {
                Assert.All(set.AllSheets(), s => Assert.Equal("AB", s.FindCustomProperty("Checked By").Value));
                Assert.Single(set.AllSheets());
            }
            else
            {
                Assert.Equal("A1 sheet", set.Template.Name);
                Assert.Equal(
                    Path.Combine(Templates, "OpenCAD2D Metric Sheet Set.dwt"),
                    set.Template.File.Resolve(Projects)
                );
                Assert.True(
                    set.NewSheetLocation == null
                        || !set.NewSheetLocation.FileName.StartsWith(draftFolder, StringComparison.OrdinalIgnoreCase)
                );
            }

            Assert.False(Directory.Exists(draftFolder));
        }

        [Fact]
        public void CancelAndDispose_DeleteTheDraft_AndAChangeOfRouteDropsIt()
        {
            NewSheetSetViewModel wizard = Wizard(out _);
            ToDetails(wizard, false);
            string draftFolder = null;
            wizard.AskProperties = draft =>
            {
                draftFolder = draft.Folder;
                return false;
            };

            wizard.OpenProperties();
            Assert.True(Directory.Exists(draftFolder));
            wizard.Cancel();
            Assert.False(Directory.Exists(draftFolder));
            Assert.Null(wizard.Draft);

            wizard.OpenProperties();
            string second = draftFolder;
            Assert.True(Directory.Exists(second));
            wizard.IsExistingDrawings = true;
            Assert.False(Directory.Exists(second));
            Assert.Null(wizard.Draft);

            wizard.OpenProperties();
            Assert.True(Directory.Exists(draftFolder));
            wizard.Dispose();
            Assert.False(Directory.Exists(draftFolder));
        }

        // ---- The request NEWSHEETSET answers

        [Fact]
        public void TheRequestNEWSHEETSET_ShowsTheWizard_AndAMadeSetIsShownInThePalette()
        {
            Example();
            MainViewModel model = Model();
            Assert.True(model.Open(_folder.Write("plan.dxf", CadTemplate.CreateMetric())));
            NewSheetSetViewModel shown = null;
            SheetSetManagerViewModel palette = null;
            model.ShowSheetSetManager = p => palette = p;
            model.ShowNewSheetSet = wizard =>
            {
                shown = wizard;
                wizard.IsBrowsedExample = true;
                wizard.BrowsedExamplePath = Path.Combine(Templates, "OpenCAD2D Metric Sheet Set.dst");
                ToDetails(wizard, false);
                wizard.Name = "Example Tower";
                wizard.Next();
                Assert.True(wizard.Finish());
            };

            CadUiRequestEventArgs request = model.Session.Ask("NEWSHEETSET", model.Document);

            Assert.True(request.Handled);
            Assert.NotNull(shown);
            Assert.True(File.Exists(Dst));
            Assert.Same(model.SheetSetManager, palette);
            Assert.Equal(Dst, model.SheetSetManager.Current.Path);
        }

        [Fact]
        public void TheRequestNEWSHEETSET_CancelledIsStillAnswered_AndShowsNoPalette_WhereNoWindowIsGivenItIsNot()
        {
            Example();
            MainViewModel model = Model();
            Assert.True(model.Open(_folder.Write("plan.dxf", CadTemplate.CreateMetric())));

            Assert.False(model.Session.Ask("NEWSHEETSET", model.Document).Handled);

            bool shownPalette = false;
            model.ShowSheetSetManager = p => shownPalette = true;
            model.ShowNewSheetSet = wizard => wizard.Cancel();

            Assert.True(model.Session.Ask("NEWSHEETSET", model.Document).Handled);
            Assert.False(shownPalette);
            Assert.False(model.SheetSetManager.HasSet);
        }

        [Fact]
        public void TheNewSheetSetItemOfThePalette_StillStartsTheCommand_WhichAsksTheWindow()
        {
            Example();
            MainViewModel model = Model();
            Assert.True(model.Open(_folder.Write("plan.dxf", CadTemplate.CreateMetric())));
            SheetSetManagerViewModel palette = model.SheetSetManager;

            CadMenuItemViewModel item = palette.ControlMenu().Single(i => i.Header == "New Sheet Set…");
            item.Command.Execute(null);

            Assert.Contains(
                "NEWSHEETSET",
                model.History.Last(l => l.StartsWith("Command:", StringComparison.Ordinal)),
                StringComparison.Ordinal
            );
        }

        [Fact]
        public void TheSettingsOfTheWizard_AreWrittenAndReadBack_WithTheDefaultsAsTheyWere()
        {
            var settings = new GlobalSettings();
            Assert.False(settings.NewSheetSetFromDrawings);
            Assert.True(settings.SheetImportPrefixTitles);
            Assert.True(settings.SheetImportSubsetsFromFolders);
            Assert.True(settings.SheetImportIgnoreTopFolder);

            settings.NewSheetSetFromDrawings = true;
            settings.SheetImportPrefixTitles = false;
            settings.SheetImportSubsetsFromFolders = false;
            settings.SheetImportIgnoreTopFolder = false;
            string path = Path.Combine(_folder.FullPath, "settings.json");
            settings.Save(path);

            GlobalSettings again = GlobalSettings.Load(path);

            Assert.True(again.NewSheetSetFromDrawings);
            Assert.False(again.SheetImportPrefixTitles);
            Assert.False(again.SheetImportSubsetsFromFolders);
            Assert.False(again.SheetImportIgnoreTopFolder);
        }
    }
}
