using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading;
using GeometryHelper.Dxf;
using GeometryHelper.Geometry;
using OpenCAD2D.SheetSets;
using Xunit;

namespace OpenCAD2D.Wpf.UnitTest
{
    /// <summary>
    /// Publishing a sheet set to PDF (A12 5a, D17): the included sheets of a set of three drawings in two subsets as one PDF
    /// in set order, with a page of the size of each sheet, the text of each layout and AutoCAD's bookmarks; a PDF for each
    /// sheet named as AutoCAD names them; a page setup override that changes the paper; the outline written into any PDF
    /// the writer makes; and the progress and cancel of the publisher. Every drawing and set is built in code.
    /// </summary>
    public sealed class SheetSetPublishTests
    {
        /// <summary>A drawing saved as a DWG: a layout for each name given, on the paper given, with its marker written in it.</summary>
        private static string Drawing(
            string folder,
            string file,
            params (string Layout, CadPaper Paper, string Marker)[] layouts
        )
        {
            DxfDatabase database = CadTemplate.CreateMetric();
            database.TextStyleTable["Standard"].Font = new DxfFontDescriptor("Arial", false, false, 0, 34);
            database.Layouts.Single(l => l.LayoutName == "Layout1").LayoutName = layouts[0].Layout;

            foreach ((string name, _, _) in layouts.Skip(1))
            {
                var layout = new DxfLayout { LayoutName = name };
                layout.AddToLayoutDictionary(database, null);
                layout.Initialize();
            }

            foreach ((string name, _, string marker) in layouts)
            {
                DxfLayout layout = database.Layouts.Single(l => l.LayoutName == name);
                layout.BlockTableRecord.AppendEntity(new DxfLine(new GeoPoint3(10, 10, 0), new GeoPoint3(100, 10, 0)));
                layout.BlockTableRecord.AppendEntity(
                    new DxfDBText
                    {
                        TextString = marker,
                        Position = new GeoPoint3(20, 40, 0),
                        Height = 10,
                    }
                );
            }

            string path = Path.Combine(folder, file);
            CadDocument document = CadDocument.From(database, path);

            foreach ((string name, CadPaper paper, _) in layouts)
            {
                OnPaper(document, name, paper);
            }

            document.SaveAs(path);
            return path;
        }

        private static void OnPaper(CadDocument document, string layout, CadPaper paper)
        {
            CadSpace space = document.Spaces.Single(s => s.Name == layout);
            CadPlotSettings settings = CadPlotSettings.Read(document, space);
            settings.Paper = paper;
            settings.Landscape = true;
            settings.Area = CadPlotArea.Layout;
            settings.FitToPaper = false;
            settings.PaperUnits = 1;
            settings.DrawingUnits = 1;
            Assert.True(settings.ApplyTo(document, space).Succeeded);
        }

        /// <summary>
        /// Example Tower: three drawings in two subsets. Architectural holds A-101 (a layout of the first drawing, ISO A3) and
        /// A-102 (the second drawing, ISO A4, not included for publish); Structural holds S-101 (the third drawing, ISO A1) and
        /// S-102 (another layout of it, ISO A4). Each layout carries its own marker text.
        /// </summary>
        private static CadSheetSetFile Tower(TestFolder folder)
        {
            string first = Drawing(folder.FullPath, "first.dwg", ("Plan", CadPaper.Standard[3], "MARK-A101"));
            string second = Drawing(folder.FullPath, "second.dwg", ("Plan", CadPaper.Standard[4], "MARK-A102"));
            string third = Drawing(
                folder.FullPath,
                "third.dwg",
                ("Found", CadPaper.Standard[1], "MARK-S101"),
                ("Frame", CadPaper.Standard[4], "MARK-S102")
            );
            CadSheetSetFile file = CadSheetSetFile.Create(
                Path.Combine(folder.FullPath, "Tower.dst"),
                "Example Tower",
                null
            );
            file.Change(set =>
            {
                CadSheetSubset architectural = set.NewSubset("Architectural", null, false);
                Named(architectural.ImportLayouts(first, new[] { "Plan" }, false)[0], "A-101", "Ground Floor Plan");
                CadSheetSetSheet excluded = architectural.ImportLayouts(second, new[] { "Plan" }, false)[0];
                Named(excluded, "A-102", "First Floor Plan");
                excluded.IncludeForPublish = false;
                CadSheetSubset structural = set.NewSubset("Structural", null, false);
                IReadOnlyList<CadSheetSetSheet> sheets = structural.ImportLayouts(
                    third,
                    new[] { "Found", "Frame" },
                    false
                );
                Named(sheets[0], "S-101", "Foundation Plan");
                Named(sheets[1], "S-102", "Framing Plan");
                set.PublishOptions.OutputFolder = CadFileReference.FromPath(
                    Path.Combine(folder.FullPath, "Issued"),
                    folder.FullPath
                );
            });
            return file;
        }

        private static void Named(CadSheetSetSheet sheet, string number, string title)
        {
            sheet.Number = number;
            sheet.Title = title;
        }

        private static string Latin(byte[] pdf) => Encoding.GetEncoding(28591).GetString(pdf);

        private static string[] Boxes(byte[] pdf) =>
            Regex
                .Matches(Latin(pdf), @"/MediaBox \[0 0 ([0-9.]+) ([0-9.]+)\]")
                .Cast<Match>()
                .Select(m => m.Groups[1].Value + " " + m.Groups[2].Value)
                .ToArray();

        private static int Pages(byte[] pdf) => Regex.Matches(Latin(pdf), @"/Type /Page\b").Count;

        /// <summary>
        /// The outline of a PDF as a reader finds it, one line for each bookmark in order, <c>title</c> with two spaces for each
        /// level above and <c> -&gt; n</c> for the page it goes to counted from 1: the catalog the last trailer names, its
        /// outline, each item's title decoded and its destination found in the page tree. Where an object is written twice (an
        /// incremental update) the last wins, as in a reader.
        /// </summary>
        private static List<string> Outline(byte[] pdf)
        {
            string text = Latin(pdf);
            var objects = new Dictionary<int, string>();

            foreach (
                Match m in Regex.Matches(text, @"(?<=^|\n)(\d+) 0 obj\r?\n(.*?)\r?\nendobj", RegexOptions.Singleline)
            )
            {
                objects[int.Parse(m.Groups[1].Value, CultureInfo.InvariantCulture)] = m.Groups[2].Value;
            }

            Match root = Regex.Matches(text, @"/Root (\d+) 0 R").Cast<Match>().Last();
            string catalog = objects[int.Parse(root.Groups[1].Value, CultureInfo.InvariantCulture)];
            var pages = new List<int>();
            Leaves(objects, Ref(catalog, "/Pages"), pages);
            var lines = new List<string>();
            Match outlines = Regex.Match(catalog, @"/Outlines (\d+) 0 R");

            if (outlines.Success)
            {
                Items(
                    objects,
                    pages,
                    Ref(objects[int.Parse(outlines.Groups[1].Value, CultureInfo.InvariantCulture)], "/First"),
                    0,
                    lines
                );
            }

            return lines;
        }

        private static int Ref(string body, string key) =>
            int.Parse(
                Regex.Match(body, Regex.Escape(key) + @" (\d+) 0 R").Groups[1].Value,
                CultureInfo.InvariantCulture
            );

        private static void Leaves(Dictionary<int, string> objects, int node, List<int> pages)
        {
            string body = objects[node];
            Match kids = Regex.Match(body, @"/Kids\s*\[([^\]]*)\]");

            if (!Regex.IsMatch(body, @"/Type\s*/Pages\b") || !kids.Success)
            {
                pages.Add(node);
                return;
            }

            foreach (Match kid in Regex.Matches(kids.Groups[1].Value, @"(\d+) 0 R"))
            {
                Leaves(objects, int.Parse(kid.Groups[1].Value, CultureInfo.InvariantCulture), pages);
            }
        }

        private static void Items(
            Dictionary<int, string> objects,
            List<int> pages,
            int number,
            int depth,
            List<string> lines
        )
        {
            while (true)
            {
                string item = objects[number];
                string hex = Regex.Match(item, @"/Title <FEFF([0-9A-F]*)>").Groups[1].Value;
                var title = new StringBuilder();

                for (int i = 0; i + 3 < hex.Length; i += 4)
                {
                    title.Append((char)Convert.ToInt32(hex.Substring(i, 4), 16));
                }

                Match dest = Regex.Match(item, @"/Dest \[(\d+) 0 R /Fit\]");
                string page = dest.Success
                    ? " -> " + (pages.IndexOf(int.Parse(dest.Groups[1].Value, CultureInfo.InvariantCulture)) + 1)
                    : string.Empty;
                lines.Add(new string(' ', depth * 2) + title + page);
                Match first = Regex.Match(item, @"/First (\d+) 0 R");

                if (first.Success)
                {
                    Items(
                        objects,
                        pages,
                        int.Parse(first.Groups[1].Value, CultureInfo.InvariantCulture),
                        depth + 1,
                        lines
                    );
                }

                Match next = Regex.Match(item, @"/Next (\d+) 0 R");

                if (!next.Success)
                {
                    return;
                }

                number = int.Parse(next.Groups[1].Value, CultureInfo.InvariantCulture);
            }
        }

        [Fact]
        public void AThreeDrawingSetInTwoSubsets_PublishedToOnePdf_HasExactlyTheIncludedSheetsInSetOrder()
        {
            using (var folder = new TestFolder())
            {
                CadSheetSetFile file = Tower(folder);
                CadSheetSetPublish publish = CadSheetSetPublish.Create(file.SheetSet, null, null, null, null);

                CadPublishResult result = CadPublisher.Publish(publish.List, null);

                string pdfFile = Path.Combine(folder.FullPath, "Issued", "Example Tower.pdf");
                Assert.Equal(new[] { pdfFile }, result.Files);
                Assert.Equal(3, result.Written);
                Assert.Equal(0, result.Failed);
                byte[] pdf = File.ReadAllBytes(pdfFile);

                // A-101 on ISO A3, S-101 on ISO A1, S-102 on ISO A4, each on its side; A-102, not included, has no page.
                Assert.Equal(3, Pages(pdf));
                Assert.Equal(new[] { "1190.5512 841.8898", "2383.937 1683.7795", "841.8898 595.2756" }, Boxes(pdf));
                string text = Pdf.Text(pdf);
                Assert.True(
                    text.IndexOf("MARK-A101", StringComparison.Ordinal)
                        < text.IndexOf("MARK-S101", StringComparison.Ordinal)
                        && text.IndexOf("MARK-S101", StringComparison.Ordinal)
                            < text.IndexOf("MARK-S102", StringComparison.Ordinal),
                    "The text of the pages is not in set order: " + text
                );
                Assert.Contains("MARK-A101", text);
                Assert.DoesNotContain("MARK-A102", text);

                // AutoCAD 2021's outline: Sheets and Views, and a bookmark under it for each page, none for a subset.
                Assert.Equal(
                    new[]
                    {
                        "Sheets and Views",
                        "  A-101 Ground Floor Plan -> 1",
                        "  S-101 Foundation Plan -> 2",
                        "  S-102 Framing Plan -> 3",
                    },
                    Outline(pdf)
                );
                Pdf.AssertCrossReferencesPointAtTheirObjects(pdf);
            }
        }

        [Fact]
        public void WithAFilePerSheet_EachSheetIsAPdfNamedByItsNumberAndTitle_WithTheOutlineOfItsOwnPage()
        {
            using (var folder = new TestFolder())
            {
                CadSheetSetFile file = Tower(folder);
                file.Change(set => set.PublishOptions.MultiSheet = false);
                CadSheetSetPublish publish = CadSheetSetPublish.Create(file.SheetSet, null, null, null, null);

                CadPublishResult result = CadPublisher.Publish(publish.List, null);

                string issued = Path.Combine(folder.FullPath, "Issued");
                Assert.Equal(
                    new[] { "A-101 Ground Floor Plan.pdf", "S-101 Foundation Plan.pdf", "S-102 Framing Plan.pdf" },
                    Directory
                        .GetFiles(issued)
                        .Select(Path.GetFileName)
                        .OrderBy(n => n, StringComparer.Ordinal)
                        .ToArray()
                );
                Assert.Equal(3, result.Files.Count);
                byte[] one = File.ReadAllBytes(Path.Combine(issued, "S-101 Foundation Plan.pdf"));
                Assert.Equal(1, Pages(one));
                Assert.Equal(new[] { "2383.937 1683.7795" }, Boxes(one));
                Assert.Contains("MARK-S101", Pdf.Text(one));
                Assert.DoesNotContain("MARK-S102", Pdf.Text(one));
                Assert.Equal(new[] { "Sheets and Views", "  S-101 Foundation Plan -> 1" }, Outline(one));
                Pdf.AssertCrossReferencesPointAtTheirObjects(one);
            }
        }

        [Fact]
        public void APageSetupOverride_ChangesThePaperOfEveryPage()
        {
            using (var folder = new TestFolder())
            {
                CadSheetSetFile file = Tower(folder);
                CadDocument setups = CadDocument.From(new DxfDatabase("AC1032"), "Setups.dwt");
                CadPlotSettings settings = CadPlotSettings.Read(setups, setups.ModelSpace);
                settings.Paper = CadPaper.Standard[2];
                settings.Landscape = true;
                settings.Area = CadPlotArea.Extents;
                Assert.True(settings.SaveAsPageSetup(setups, "A2 plot").Succeeded);
                string dwt = Path.Combine(folder.FullPath, "Setups.dwt");
                setups.SaveAs(dwt);
                file.Change(set => set.PageSetupOverrides = CadFileReference.FromPath(dwt, folder.FullPath));
                CadSheetSetPublish publish = CadSheetSetPublish.Create(file.SheetSet, null, "A2 plot", null, null);

                CadPublisher.Publish(publish.List, null);

                byte[] pdf = File.ReadAllBytes(Path.Combine(folder.FullPath, "Issued", "Example Tower.pdf"));
                Assert.Equal(3, Pages(pdf));
                Assert.Equal(new[] { "1683.7795 1190.5512", "1683.7795 1190.5512", "1683.7795 1190.5512" }, Boxes(pdf));
            }
        }

        [Fact]
        public void TheBookmarksOfTheOptionsAreOff_WhereTheyAreNotAsked_AndTheListOfThePublishDialogHasNone()
        {
            using (var folder = new TestFolder())
            {
                CadSheetSetFile file = Tower(folder);
                CadSheetSetPublish publish = CadSheetSetPublish.Create(
                    file.SheetSet,
                    null,
                    null,
                    new CadPublishOptions { CreateBookmarks = false },
                    null
                );

                CadPublisher.Publish(publish.List, null);

                byte[] pdf = File.ReadAllBytes(Path.Combine(folder.FullPath, "Issued", "Example Tower.pdf"));
                Assert.Empty(Outline(pdf));
                Assert.DoesNotContain("/Outlines", Latin(pdf));
                Assert.Equal(3, Pages(pdf));
            }
        }

        [Fact]
        public void APublishInReverseOrder_WritesThePagesAndTheirBookmarksInTheReverseOfSetOrder()
        {
            using (var folder = new TestFolder())
            {
                CadSheetSetFile file = Tower(folder);
                file.Change(set => set.PublishOptions.ReverseOrder = true);
                CadSheetSetPublish publish = CadSheetSetPublish.Create(file.SheetSet, null, null, null, null);

                CadPublisher.Publish(publish.List, null);

                byte[] pdf = File.ReadAllBytes(Path.Combine(folder.FullPath, "Issued", "Example Tower.pdf"));
                Assert.Equal(new[] { "841.8898 595.2756", "2383.937 1683.7795", "1190.5512 841.8898" }, Boxes(pdf));
                Assert.Equal(
                    new[]
                    {
                        "Sheets and Views",
                        "  S-102 Framing Plan -> 1",
                        "  S-101 Foundation Plan -> 2",
                        "  A-101 Ground Floor Plan -> 3",
                    },
                    Outline(pdf)
                );
            }
        }

        [Fact]
        public void ASheetWhoseDrawingIsGone_IsToldAndLeftOut_TheOthersArePublishedWithTheirBookmarks()
        {
            using (var folder = new TestFolder())
            {
                CadSheetSetFile file = Tower(folder);
                File.Delete(Path.Combine(folder.FullPath, "third.dwg"));
                CadSheetSetPublish publish = CadSheetSetPublish.Create(file.SheetSet, null, null, null, null);

                CadPublishResult result = CadPublisher.Publish(publish.List, null);

                Assert.Equal(1, result.Written);
                Assert.Equal(2, result.Failed);
                Assert.Contains("S-101 Foundation Plan: Drawing not found. Not published.", result.Messages);
                byte[] pdf = File.ReadAllBytes(Path.Combine(folder.FullPath, "Issued", "Example Tower.pdf"));
                Assert.Equal(new[] { "Sheets and Views", "  A-101 Ground Floor Plan -> 1" }, Outline(pdf));
            }
        }

        [Fact]
        public void ThePublisherTellsEachSheetBeforeItIsDrawn_AndACancelStopsAtTheNextSheetWithNoFileWritten()
        {
            using (var folder = new TestFolder())
            {
                CadSheetSetFile file = Tower(folder);
                CadSheetSetPublish publish = CadSheetSetPublish.Create(file.SheetSet, null, null, null, null);
                var told = new List<string>();
                var progress = new SyncProgress(p => told.Add(p.Sheet + "/" + p.Count + " " + p.Name));

                CadPublisher.Publish(publish.List, null, progress, CancellationToken.None);

                Assert.Equal(
                    new[] { "1/3 A-101 Ground Floor Plan", "2/3 S-101 Foundation Plan", "3/3 S-102 Framing Plan" },
                    told
                );

                string pdfFile = Path.Combine(folder.FullPath, "Issued", "Example Tower.pdf");
                File.Delete(pdfFile);
                using (var source = new CancellationTokenSource())
                {
                    told.Clear();
                    var stopping = new SyncProgress(p =>
                    {
                        told.Add(p.Name);

                        if (p.Sheet == 2)
                        {
                            source.Cancel();
                        }
                    });

                    CadPublishResult result = CadPublisher.Publish(publish.List, null, stopping, source.Token);

                    // The cancel is seen before the third sheet is drawn; a PDF of several sheets is written whole or not at all.
                    Assert.Equal(new[] { "A-101 Ground Floor Plan", "S-101 Foundation Plan" }, told);
                    Assert.False(File.Exists(pdfFile));
                    Assert.Equal("The publishing operation was canceled.", result.Messages.Last());
                    Assert.DoesNotContain(result.Messages, m =>
                        m.StartsWith("Publish complete", StringComparison.Ordinal)
                    );
                    Assert.Empty(result.Files);
                }
            }
        }

        [Fact]
        public void ThePdfWriter_GivesTwentyPagesAnOutlineWhoseBookmarksGoToTheirOwnPages_WhateverTheShapeOfThePageTree()
        {
            using (var folder = new TestFolder())
            {
                string drawing = Drawing(folder.FullPath, "one.dwg", ("Plan", CadPaper.Standard[4], "MARK"));
                CadDocument document = CadDocument.Open(drawing);
                CadSpace space = document.Spaces.Single(s => s.Name == "Plan");
                CadPlotSettings settings = CadPlotSettings.Read(document, space);
                settings.UseDevice(CadPlotSettings.PdfDevice);
                var pages = new List<CadPlotJob>();
                var documents = new List<CadDocument>();
                var top = new CadPdfBookmark("All", -1);
                CadPdfBookmark nested = null;

                for (int i = 0; i < 20; i++)
                {
                    pages.Add(new CadPlotJob(space, settings, DisplayBox.Empty, null));
                    documents.Add(document);

                    if (i == 10)
                    {
                        nested = new CadPdfBookmark("Second half đồ", -1);
                        top.Children.Add(nested);
                    }

                    (nested ?? top).Children.Add(new CadPdfBookmark("Page " + (i + 1), i));
                }

                string pdfFile = Path.Combine(folder.FullPath, "twenty.pdf");
                var told = new List<int>();

                CadPdfWriter.WritePages(pdfFile, documents, pages, new[] { top }, told.Add);

                byte[] pdf = File.ReadAllBytes(pdfFile);
                Assert.Equal(Enumerable.Range(0, 20), told);
                Assert.Equal(20, Pages(pdf));
                List<string> outline = Outline(pdf);
                Assert.Equal("All", outline[0]);
                Assert.Equal("  Page 1 -> 1", outline[1]);
                Assert.Equal("  Page 10 -> 10", outline[10]);
                Assert.Equal("  Second half đồ", outline[11]);
                Assert.Equal("    Page 11 -> 11", outline[12]);
                Assert.Equal("    Page 12 -> 12", outline[13]);
                Assert.Equal("    Page 20 -> 20", outline[21]);
                Assert.Equal(22, outline.Count);
                Pdf.AssertCrossReferencesPointAtTheirObjects(pdf);
                Assert.Throws<ArgumentException>(() =>
                    CadPdfWriter.WritePages(
                        pdfFile,
                        documents.Take(2).ToList(),
                        pages.Take(2).ToList(),
                        new[] { new CadPdfBookmark("Beyond", 2) },
                        null
                    )
                );
            }
        }

        /// <summary>A progress that runs its action where it is reported, not on a thread of the pool as <see cref="Progress{T}"/> does.</summary>
        private sealed class SyncProgress : IProgress<CadPublishProgress>
        {
            private readonly Action<CadPublishProgress> _report;

            public SyncProgress(Action<CadPublishProgress> report)
            {
                _report = report;
            }

            public void Report(CadPublishProgress value) => _report(value);
        }
    }
}
