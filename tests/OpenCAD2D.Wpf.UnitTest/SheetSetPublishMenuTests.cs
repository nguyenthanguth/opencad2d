using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using GeometryHelper.Dxf;
using OpenCAD2D.SheetSets;
using Xunit;

namespace OpenCAD2D.Wpf.UnitTest
{
    /// <summary>
    /// The Publish items of the Sheet List's menus and toolbar (A12 5a): each calls its entry point of the main view model with the
    /// sheets it was chosen on, the checks (Publish Sheets in Subset, Publish in Reverse Order) are the set's own, and the page
    /// setup override names come from the overrides file. The dialogs and the progress run are stubbed.
    /// </summary>
    public sealed class SheetSetPublishMenuTests : IDisposable
    {
        private readonly TestFolder _folder = new TestFolder();

        public void Dispose() => _folder.Dispose();

        private string Dst => Path.Combine(_folder.FullPath, "Tower.dst");

        /// <summary>Example Tower: Architectural (A-101, A-102), Structural (S-101), each sheet a layout of its own drawing, and an overrides file with the page setup "A2 plot".</summary>
        private void Tower()
        {
            string Drawing(string name)
            {
                DxfDatabase database = CadTemplate.CreateMetric();
                database.Layouts.Single(l => l.LayoutName == "Layout1").LayoutName = "Sheet";
                string path = Path.Combine(_folder.FullPath, name);
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

            CadDocument setups = CadDocument.From(new DxfDatabase("AC1032"), "Setups.dwt");
            CadPlotSettings plot = CadPlotSettings.Read(setups, setups.ModelSpace);
            plot.Paper = CadPaper.Standard[2];
            Assert.True(plot.SaveAsPageSetup(setups, "A2 plot").Succeeded);
            string dwt = Path.Combine(_folder.FullPath, "Setups.dwt");
            setups.SaveAs(dwt);
            CadSheetSetFile file = CadSheetSetFile.Create(Dst, "Example Tower", null);
            file.Change(set =>
            {
                set.PageSetupOverrides = CadFileReference.FromPath(dwt, _folder.FullPath);
                set.PublishOptions.PromptForName = false;
                CadSheetSubset architectural = set.NewSubset("Architectural", null, false);
                CadSheetSetSheet a = architectural.ImportLayouts(Drawing("a101.dwg"), new[] { "Sheet" }, false)[0];
                a.Number = "A-101";
                a.Title = "Ground Floor Plan";
                CadSheetSetSheet b = architectural.ImportLayouts(Drawing("a102.dwg"), new[] { "Sheet" }, false)[0];
                b.Number = "A-102";
                b.Title = "First Floor Plan";
                CadSheetSetSheet c = set.NewSubset("Structural", null, false)
                    .ImportLayouts(Drawing("s101.dwg"), new[] { "Sheet" }, false)[0];
                c.Number = "S-101";
                c.Title = "Foundation Plan";
            });
        }

        private SheetSetManagerViewModel Opened(out MainViewModel model, List<CadPublishList> runs)
        {
            Tower();
            model = new MainViewModel();
            model.RunSheetSetPublish = (list, printers) =>
            {
                runs.Add(list);
                return new CadPublishResult();
            };
            var palette = new SheetSetManagerViewModel(model);
            Assert.True(palette.OpenSet(Dst));
            return palette;
        }

        private static SheetSetNodeViewModel Node(SheetSetManagerViewModel palette, string text) =>
            palette.Rows.Single(r => r.Text == text);

        private static CadMenuItemViewModel Item(IEnumerable<CadMenuItemViewModel> items, string header) =>
            items.Single(i => i.Header == header);

        private static void Click(CadMenuItemViewModel item) => item.Command.Execute(null);

        private static string[] Names(CadPublishList list) => list.Sheets.Select(s => s.Name).ToArray();

        private static CadMenuItemViewModel Publish(SheetSetManagerViewModel palette, SheetSetNodeViewModel node) =>
            Item(palette.MenuFor(node), "Publish");

        [Fact]
        public void TheSetMenusPublishToPdf_PublishesTheWholeSetInSetOrder_ThroughTheMainViewModel()
        {
            var runs = new List<CadPublishList>();
            SheetSetManagerViewModel palette = Opened(out MainViewModel model, runs);

            Click(Item(Publish(palette, palette.Root).Items, "Publish to PDF"));

            CadPublishList list = Assert.Single(runs);
            Assert.Equal(
                new[] { "A-101 Ground Floor Plan", "A-102 First Floor Plan", "S-101 Foundation Plan" },
                Names(list)
            );
            Assert.Equal(CadPublishTo.Pdf, list.Options.PublishTo);
            Assert.Equal("Example Tower", list.SheetSetName);
            Assert.Contains("Publishing Example Tower: 3 sheets, 3 to publish, in set order.", model.History);
        }

        [Fact]
        public void ASubsetsAndASheetsPublishToPdf_PublishOnlyTheSheetsUnderThem_AndASelectionOfSheetsAllOfThem()
        {
            var runs = new List<CadPublishList>();
            SheetSetManagerViewModel palette = Opened(out _, runs);
            SheetSetNodeViewModel structural = Node(palette, "Structural");
            SheetSetNodeViewModel first = Node(palette, "A-101 - Ground Floor Plan");
            SheetSetNodeViewModel second = Node(palette, "A-102 - First Floor Plan");

            Click(Item(Publish(palette, structural).Items, "Publish to PDF"));
            Assert.Equal(new[] { "S-101 Foundation Plan" }, Names(runs[0]));

            first.IsSelected = true;
            second.IsSelected = true;
            Click(Item(Publish(palette, first).Items, "Publish to PDF"));
            Assert.Equal(new[] { "A-101 Ground Floor Plan", "A-102 First Floor Plan" }, Names(runs[1]));

            Click(Item(Publish(palette, Node(palette, "S-101 - Foundation Plan")).Items, "Publish to PDF"));
            Assert.Equal(new[] { "S-101 Foundation Plan" }, Names(runs[2]));
        }

        [Fact]
        public void TheToolbarsPublishToPdf_ActsOnTheSelectedSheets_OrOnTheWholeSetWhereNoneOrTheSetIsSelected()
        {
            var runs = new List<CadPublishList>();
            SheetSetManagerViewModel palette = Opened(out _, runs);

            palette.PublishToPdf();
            Assert.Equal(3, runs[0].Sheets.Count);

            Node(palette, "S-101 - Foundation Plan").IsSelected = true;
            palette.PublishToPdf();
            Assert.Equal(new[] { "S-101 Foundation Plan" }, Names(runs[1]));

            palette.Root.IsSelected = true;
            palette.PublishToPdf();
            Assert.Equal(3, runs[2].Sheets.Count);
        }

        [Fact]
        public void PublishToPlotter_PublishesTheSheetsAtOnce_ThroughTheMainViewModelWithNoProgressWindow()
        {
            var runs = new List<CadPublishList>();
            SheetSetManagerViewModel palette = Opened(out MainViewModel model, runs);

            Click(Item(Publish(palette, palette.Root).Items, "Publish to Plotter"));
            palette.PublishToPlotter();

            Assert.Empty(runs);
            Assert.True(File.Exists(Path.Combine(_folder.FullPath, "A-101 Ground Floor Plan.pdf")));
            Assert.Equal(
                2,
                model.History.Count(l => l.StartsWith("Publishing Example Tower: 3 sheets", StringComparison.Ordinal))
            );
        }

        [Fact]
        public void PublishUsingPageSetupOverride_ListsTheOverridesFilesPageSetups_AndPublishesTheChosenNodesWithIt()
        {
            var runs = new List<CadPublishList>();
            SheetSetManagerViewModel palette = Opened(out _, runs);

            Assert.Equal(
                new[] { "A2 plot" },
                Item(Publish(palette, palette.Root).Items, "Publish Using Page Setup Override")
                    .Items.Select(i => i.Header)
            );
            Click(
                Item(Item(Publish(palette, palette.Root).Items, "Publish Using Page Setup Override").Items, "A2 plot")
            );
            Click(
                Item(
                    Item(
                        Publish(palette, Node(palette, "Structural")).Items,
                        "Publish Using Page Setup Override"
                    ).Items,
                    "A2 plot"
                )
            );
            Click(
                Item(
                    Item(
                        Publish(palette, Node(palette, "A-102 - First Floor Plan")).Items,
                        "Publish Using Page Setup Override"
                    ).Items,
                    "A2 plot"
                )
            );

            Assert.Equal(3, runs[0].Sheets.Count);
            Assert.All(runs[0].Sheets, s => Assert.Equal("A2 plot", s.PageSetup));
            Assert.Equal(new[] { "S-101 Foundation Plan" }, Names(runs[1]));
            Assert.Equal("A2 plot", runs[1].Sheets[0].PageSetup);
            Assert.Equal(new[] { "A-102 First Floor Plan" }, Names(runs[2]));
        }

        [Fact]
        public void PublishInReverseOrder_IsTheSetsOwnCheck_OnTheSetAndTheSubsetMenus_AndReversesWhatIsPublished()
        {
            var runs = new List<CadPublishList>();
            SheetSetManagerViewModel palette = Opened(out _, runs);
            Assert.False(Item(Publish(palette, palette.Root).Items, "Publish in Reverse Order").IsChecked);

            Click(Item(Publish(palette, palette.Root).Items, "Publish in Reverse Order"));

            Assert.True(CadSheetSetFile.Open(Dst).SheetSet.PublishOptions.ReverseOrder);
            Assert.True(Item(Publish(palette, palette.Root).Items, "Publish in Reverse Order").IsChecked);
            Assert.True(
                Item(Publish(palette, Node(palette, "Architectural")).Items, "Publish in Reverse Order").IsChecked
            );
            Click(Item(Publish(palette, palette.Root).Items, "Publish to PDF"));
            Assert.Equal(
                new[] { "S-101 Foundation Plan", "A-102 First Floor Plan", "A-101 Ground Floor Plan" },
                Names(runs[0])
            );

            Click(Item(Publish(palette, Node(palette, "Architectural")).Items, "Publish in Reverse Order"));
            Assert.False(CadSheetSetFile.Open(Dst).SheetSet.PublishOptions.ReverseOrder);
        }

        [Fact]
        public void PublishSheetsInSubset_IsTheSubsetsOwnCheck_AndLeavesItsSheetsOutOfThePublish()
        {
            var runs = new List<CadPublishList>();
            SheetSetManagerViewModel palette = Opened(out MainViewModel model, runs);
            Assert.True(
                Item(Publish(palette, Node(palette, "Architectural")).Items, "Publish Sheets in Subset").IsChecked
            );

            Click(Item(Publish(palette, Node(palette, "Architectural")).Items, "Publish Sheets in Subset"));

            Assert.False(
                CadSheetSetFile.Open(Dst).SheetSet.Subsets.Single(s => s.Name == "Architectural").PublishSheets
            );
            Assert.False(
                Item(Publish(palette, Node(palette, "Architectural")).Items, "Publish Sheets in Subset").IsChecked
            );
            Assert.True(
                Item(Publish(palette, Node(palette, "Structural")).Items, "Publish Sheets in Subset").IsChecked
            );
            Click(Item(Publish(palette, palette.Root).Items, "Publish to PDF"));
            Assert.Equal(new[] { "S-101 Foundation Plan" }, Names(runs[0]));
            Assert.Contains(
                "Skipped A-101 Ground Floor Plan: the subset Architectural does not publish its sheets.",
                model.History
            );

            Click(Item(Publish(palette, Node(palette, "Architectural")).Items, "Publish Sheets in Subset"));
            Assert.True(
                CadSheetSetFile.Open(Dst).SheetSet.Subsets.Single(s => s.Name == "Architectural").PublishSheets
            );
        }

        [Fact]
        public void SheetSetPdfPublishOptions_OpensTheDialogOverTheSet_AndPublishDialogBoxTheOneOverItsSheets()
        {
            var runs = new List<CadPublishList>();
            SheetSetManagerViewModel palette = Opened(out MainViewModel model, runs);
            SheetSetPublishOptionsViewModel options = null;
            model.ShowSheetSetPublishOptions = dialog =>
            {
                options = dialog;
                return false;
            };
            PublishViewModel publish = null;
            model.ShowPublish = dialog =>
            {
                publish = dialog;
                return false;
            };

            Click(Item(Publish(palette, palette.Root).Items, "Sheet Set PDF Publish Options…"));
            Click(Item(Publish(palette, Node(palette, "Architectural")).Items, "Publish Dialog Box…"));

            Assert.Equal("Example Tower", options.SetName);
            Assert.Equal(new[] { "A-101 Ground Floor Plan", "A-102 First Floor Plan" }, Names(publish.List));
            Assert.Equal("Example Tower", publish.List.SheetSetName);
            Assert.Empty(runs);

            publish = null;
            Click(Item(Publish(palette, Node(palette, "A-101 - Ground Floor Plan")).Items, "Publish Dialog Box…"));
            Assert.Equal(new[] { "A-101 Ground Floor Plan" }, Names(publish.List));
        }

        [Fact]
        public void InALockedSet_TheChecksAndTheOptionsAreOff_ButPublishingStillIs()
        {
            var runs = new List<CadPublishList>();
            SheetSetManagerViewModel palette = Opened(out _, runs);
            File.SetAttributes(Dst, FileAttributes.ReadOnly);
            try
            {
                palette.Refresh();
                Assert.True(palette.IsLocked);
                CadMenuItemViewModel menu = Publish(palette, palette.Root);

                Assert.False(Item(menu.Items, "Publish in Reverse Order").IsEnabled);
                Assert.False(Item(menu.Items, "Sheet Set PDF Publish Options…").IsEnabled);
                Assert.True(Item(menu.Items, "Publish to PDF").IsEnabled);
                Assert.False(
                    Item(Publish(palette, Node(palette, "Structural")).Items, "Publish Sheets in Subset").IsEnabled
                );
            }
            finally
            {
                File.SetAttributes(Dst, FileAttributes.Normal);
            }
        }
    }
}
