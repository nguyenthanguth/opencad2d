using System;
using System.Collections.Generic;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Text;
using GeometryHelper.Dxf;
using GeometryHelper.Geometry;
using OpenCAD2D.SheetSets;
using Xunit;

namespace OpenCAD2D.UnitTest
{
    /// <summary>
    /// The transmittal of a sheet set (A12 5b, D18): one zip of the set's data file, the drawings of its sheets, their external
    /// references and images, the sheet creation template, the plot style tables the layouts name and a report, with the folders
    /// of the set kept and the paths that would be wrong written again in the copies; unzipped somewhere else, with the folders
    /// the set was made in gone, the set opens, each sheet's layout is found and the external reference and the image resolve.
    /// Every drawing and set is built in code in a temporary folder.
    /// </summary>
    public sealed class SheetSetTransmittalTests : IDisposable
    {
        private readonly string _folder = Path.Combine(
            Path.GetTempPath(),
            "gk",
            "sheetsettransmittal-" + Guid.NewGuid().ToString("N")
        );

        public SheetSetTransmittalTests()
        {
            Directory.CreateDirectory(_folder);
        }

        public void Dispose()
        {
            try
            {
                Directory.Delete(_folder, true);
            }
            catch (IOException)
            {
                // A file a watcher still holds is left for the system to clear.
            }
        }

        private string Root => Path.Combine(_folder, "Projects", "Tower");

        private string Outside => Path.Combine(_folder, "Outside");

        private string Styles => Path.Combine(_folder, "PlotStyles");

        private static readonly DateTime Noon = new DateTime(2026, 11, 26, 5, 30, 0, DateTimeKind.Utc);

        private sealed class Project
        {
            internal CadSheetSetFile File;
            internal string Cover;
            internal string A101;
            internal string Model;
            internal string Photo;
            internal string Template;
            internal string Ctb;
        }

        /// <summary>
        /// Example Tower in <c>Projects\Tower</c>: the sheet G-001 (cover.dwg, its layout plotted with tower.ctb, a table in
        /// PlotStyles) and the sheet A-101 of the subset Architectural (Architectural\a101.dwg, which attaches
        /// <c>..\Model\model.dwg</c> and shows site-photo.jpg by the full path of a folder outside the set), with sheets.dwt
        /// outside the set as the sheet creation template.
        /// </summary>
        private Project Tower(bool inside = false)
        {
            string resources = inside ? Path.Combine(Root, "Resources") : Outside;
            Directory.CreateDirectory(Path.Combine(Root, "Architectural"));
            Directory.CreateDirectory(Path.Combine(Root, "Model"));
            Directory.CreateDirectory(resources);
            Directory.CreateDirectory(Styles);
            var project = new Project
            {
                Model = SheetSetTestDrawings.Write(Path.Combine(Root, "Model"), "model.dwg", "Sheet"),
                Photo = Path.Combine(resources, "site-photo.jpg"),
                Template = SheetSetTestDrawings.Write(resources, "sheets.dwt", "A1 sheet"),
                Ctb = Path.Combine(Styles, "tower.ctb"),
            };
            File.WriteAllBytes(project.Photo, Encoding.ASCII.GetBytes("not really a photograph, but bytes"));
            File.WriteAllBytes(project.Ctb, Encoding.ASCII.GetBytes("plot style table bytes"));

            DxfDatabase cover = SheetSetTestDrawings.Build("Sheet");
            cover.Layouts.Single(l => l.LayoutName == "Sheet").CurrentStyleSheet = "tower.ctb";
            project.Cover = Save(cover, Path.Combine(Root, "cover.dwg"));

            DxfDatabase plan = SheetSetTestDrawings.Build("Sheet");
            plan.ModelSpace.AppendEntity(
                new DxfBlockReference(new GeoPoint3(0, 0, 0), plan.AttachXref("..\\Model\\model.dwg", "model"))
            );
            DxfDBDictionary images = DxfRasterImageDef.CreateImageDictionary(plan);
            images.SetAt(
                "photo",
                new DxfRasterImageDef(inside ? "..\\Resources\\site-photo.jpg" : project.Photo, new GeoVector2(40, 30))
            );
            project.A101 = Save(plan, Path.Combine(Root, "Architectural", "a101.dwg"));

            project.File = CadSheetSetFile.Create(
                Path.Combine(Root, "Tower.dst"),
                "Example Tower",
                null,
                SheetSetFileTests.Counter()
            );
            project.File.Clock = () => Noon;
            project.File.Change(set =>
            {
                set.Template = new CadDrawingReference(
                    CadFileReference.FromPath(project.Template, Root),
                    "A1 sheet",
                    null,
                    null
                );
                Sheet(set, "G-001", "Cover Sheet", project.Cover, "Sheet");
                Sheet(set.NewSubset("Architectural", null, false), "A-101", "Ground Floor Plan", project.A101, "Sheet");
            });
            return project;
        }

        private static string Save(DxfDatabase database, string path)
        {
            CadDocument.From(database, Path.GetFileName(path)).SaveAs(path);
            return path;
        }

        private static void Sheet(CadSheetSubset into, string number, string title, string drawing, string layout)
        {
            CadSheetSetSheet sheet = into.ImportLayouts(drawing, new[] { layout }, false).Single();
            sheet.Number = number;
            sheet.Title = title;
        }

        private CadTransmittal Transmittal(Project project) => new CadTransmittal(project.File, Styles, null);

        private static List<string> Entries(string zip)
        {
            using (ZipArchive archive = ZipFile.OpenRead(zip))
            {
                return archive.Entries.Select(e => e.FullName).ToList();
            }
        }

        private static void Extract(string zip, string into)
        {
            using (ZipArchive archive = ZipFile.OpenRead(zip))
            {
                foreach (ZipArchiveEntry entry in archive.Entries)
                {
                    string path = Path.Combine(into, entry.FullName.Replace('/', '\\'));
                    Directory.CreateDirectory(Path.GetDirectoryName(path));
                    entry.ExtractToFile(path, true);
                }
            }
        }

        private string ZipOf(string name) => Path.Combine(_folder, "out", name + ".zip");

        [Fact]
        public void TheZip_HoldsTheSetItsSheetsTheirReferencesTheTemplateAndTheStyleTable_InTheOrderOfTheirPaths()
        {
            Project project = Tower();
            CadTransmittal transmittal = Transmittal(project);

            CadTransmittalResult result = transmittal.Write(transmittal.Gather(null), ZipOf("all"));

            Assert.Equal(
                new[]
                {
                    "Architectural/a101.dwg",
                    "cover.dwg",
                    "Files/sheets.dwt",
                    "Files/site-photo.jpg",
                    "Files/tower.ctb",
                    "Model/model.dwg",
                    "Tower.dst",
                    "transmittal.txt",
                },
                Entries(ZipOf("all"))
            );
            Assert.Equal(7, result.FileCount);
            Assert.Equal(new FileInfo(ZipOf("all")).Length, result.ZipSize);
        }

        [Fact]
        public void TheFilesTable_SaysWhatEachFileIsForWhereItIsAndWhereItGoes()
        {
            Project project = Tower();

            IReadOnlyList<CadTransmittalFile> files = Transmittal(project).Gather(null);

            Assert.Equal(
                new[]
                {
                    "SheetSet Tower.dst",
                    "Sheet Architectural\\a101.dwg",
                    "Sheet cover.dwg",
                    "ExternalReference Model\\model.dwg",
                    "Template Files\\sheets.dwt",
                    "Image Files\\site-photo.jpg",
                    "PlotStyleTable Files\\tower.ctb",
                },
                files.Select(f => f.Kind + " " + f.PathInZip).ToArray()
            );
            Assert.All(files, f => Assert.True(f.IsFound && f.IsIncluded));
            CadTransmittalFile photo = files.Single(f => f.Name == "site-photo.jpg");
            Assert.Equal(".\\Files", photo.FolderInZip);
            Assert.Equal("34 B", photo.SizeText);
            Assert.Equal(new[] { "a101.dwg" }, photo.UsedBy);
            Assert.Equal(".\\", files.Single(f => f.Name == "Tower.dst").FolderInZip);
            Assert.Equal(".\\Architectural", files.Single(f => f.Name == "a101.dwg").FolderInZip);
        }

        [Fact]
        public void UnzippedElsewhere_WithTheOriginalFoldersGone_TheSetOpensEverySheetIsFoundAndTheReferencesResolve()
        {
            Project project = Tower();
            CadTransmittal transmittal = Transmittal(project);
            transmittal.Write(transmittal.Gather(null), ZipOf("moved"));
            string elsewhere = Path.Combine(_folder, "Received", "Tower");
            Extract(ZipOf("moved"), elsewhere);
            Directory.Delete(Path.Combine(_folder, "Projects"), true);
            Directory.Delete(Outside, true);
            Directory.Delete(Styles, true);

            CadSheetSetFile moved = CadSheetSetFile.Open(Path.Combine(elsewhere, "Tower.dst"));

            List<CadSheetSetSheet> sheets = moved.SheetSet.AllSheets().ToList();
            Assert.Equal(new[] { "G-001", "A-101" }, sheets.Select(s => s.Number).ToArray());
            foreach (CadSheetSetSheet sheet in sheets)
            {
                CadFoundLayout found = sheet.FindLayout();
                Assert.False(found.IsMissing, sheet.Number);
                Assert.Equal("Sheet", found.LayoutName);
                Assert.StartsWith(elsewhere, found.DrawingFile, StringComparison.OrdinalIgnoreCase);
            }

            Assert.Equal(
                Path.Combine(elsewhere, "Files", "sheets.dwt"),
                moved.SheetSet.Template.File.Resolve(moved.Folder),
                ignoreCase: true
            );

            string a101 = Path.Combine(elsewhere, "Architectural", "a101.dwg");
            DxfDatabase plan = CadDrawingFile.Open(a101);
            DxfBlockTableRecord xref = plan.BlockTable.Single(b => b.IsFromExternalReference && !b.IsLayout);
            Assert.Equal("..\\Model\\model.dwg", xref.PathName);
            Assert.True(File.Exists(Path.GetFullPath(Path.Combine(Path.GetDirectoryName(a101), xref.PathName))));
            DxfRasterImageDef image = (DxfRasterImageDef)
                DxfRasterImageDef.GetImageDictionary(plan).Cast<DxfDBDictionaryEntry>().Single().Value;
            Assert.Equal("..\\Files\\site-photo.jpg", image.SourceFileName);
            Assert.True(File.Exists(Path.GetFullPath(Path.Combine(Path.GetDirectoryName(a101), image.SourceFileName))));
        }

        [Fact]
        public void TheOriginalFiles_AreNotTouched()
        {
            Project project = Tower();
            string[] paths =
            {
                project.File.Path,
                project.Cover,
                project.A101,
                project.Model,
                project.Photo,
                project.Template,
            };
            Dictionary<string, byte[]> before = paths.ToDictionary(p => p, File.ReadAllBytes);
            CadTransmittal transmittal = Transmittal(project);

            transmittal.Write(transmittal.Gather(null), ZipOf("untouched"));

            foreach (string path in paths)
            {
                Assert.Equal(before[path], File.ReadAllBytes(path));
            }
        }

        [Fact]
        public void APathThatIsAlreadyRight_IsNotWrittenAgain_ThePhotographOutsideTheSetIs()
        {
            Project project = Tower();
            CadTransmittal transmittal = Transmittal(project);

            CadTransmittalResult result = transmittal.Write(transmittal.Gather(null), ZipOf("paths"));

            Assert.Contains(
                "a101.dwg: the path of the image " + project.Photo + " is now ..\\Files\\site-photo.jpg.",
                result.Changes
            );
            Assert.DoesNotContain(result.Changes, c => c.Contains("external reference"));
            Assert.Contains(
                "Tower.dst: the sheet creation template of Example Tower is now .\\Files\\sheets.dwt.",
                result.Changes
            );
            using (ZipArchive archive = ZipFile.OpenRead(ZipOf("paths")))
            {
                byte[] copy = ReadEntry(archive, "Model/model.dwg");
                Assert.Equal(File.ReadAllBytes(project.Model), copy);
            }
        }

        private static byte[] ReadEntry(ZipArchive archive, string name)
        {
            using (Stream stream = archive.GetEntry(name).Open())
            using (var memory = new MemoryStream())
            {
                stream.CopyTo(memory);
                return memory.ToArray();
            }
        }

        [Fact]
        public void TwoRuns_OverFilesThatNeedNothingWrittenAgain_GiveTheSameBytes()
        {
            Project project = Tower(inside: true);
            File.SetLastWriteTime(project.Photo, new DateTime(2026, 3, 4, 5, 6, 8));

            CadTransmittal first = Transmittal(project);
            first.Notes = "Issued for tender.";
            first.UpdateFields = false;
            Assert.Empty(first.Write(first.Gather(null), ZipOf("one")).Changes);
            byte[] firstBytes = File.ReadAllBytes(ZipOf("one"));
            CadTransmittal second = Transmittal(project);
            second.Notes = "Issued for tender.";
            second.UpdateFields = false;
            Assert.Empty(second.Write(second.Gather(null), ZipOf("one")).Changes);

            Assert.Equal(firstBytes, File.ReadAllBytes(ZipOf("one")));
        }

        [Fact]
        public void TheReport_ListsTheNotesTheSheetsTheFilesAndTheChanges_AndIsTheLastEntryOfTheZip()
        {
            Project project = Tower();
            CadTransmittal transmittal = Transmittal(project);
            transmittal.Notes = "Issued for tender, 26 Nov 2026.";

            CadTransmittalResult result = transmittal.Write(transmittal.Gather(null), ZipOf("report"));

            string text;
            using (ZipArchive archive = ZipFile.OpenRead(ZipOf("report")))
            {
                Assert.Equal("transmittal.txt", archive.Entries.Last().FullName);
                text = Encoding.UTF8.GetString(ReadEntry(archive, "transmittal.txt"));
            }

            Assert.Equal(result.Report, text);
            Assert.Contains("Sheet set:   Example Tower", text);
            Assert.Contains("Date:        2026-11-26 05:30 UTC", text);
            Assert.Contains("Issued for tender, 26 Nov 2026.", text);
            Assert.Contains("G-001 - Cover Sheet\r\nA-101 - Ground Floor Plan\r\n", text);
            Assert.Contains("Files in the package (7 files, ", text);
            Assert.Contains("Files\\site-photo.jpg  (image, 34 B)", text);
            Assert.Contains("    from " + project.Photo, text);
            Assert.DoesNotContain("Files not found", text);
        }

        [Fact]
        public void AFileNotFound_IsListedUncheckedKeptOutOfTheZipAndNamedInTheReport()
        {
            Project project = Tower();
            File.Delete(project.Photo);
            CadTransmittal transmittal = Transmittal(project);

            IReadOnlyList<CadTransmittalFile> files = transmittal.Gather(null);
            CadTransmittalFile missing = files.Single(f => !f.IsFound);
            missing.IsIncluded = true;
            CadTransmittalResult result = transmittal.Write(files, ZipOf("missing"));

            Assert.Equal(CadTransmittalFileKind.Image, missing.Kind);
            Assert.False(missing.IsIncluded);
            Assert.Equal("not found", missing.Note);
            Assert.Equal(project.Photo, missing.Reference);
            Assert.Equal(new[] { "a101.dwg" }, missing.UsedBy);
            Assert.DoesNotContain("Files/site-photo.jpg", Entries(ZipOf("missing")));
            Assert.Contains("Files not found", result.Report);
            Assert.Contains(project.Photo + "  (image; needed by a101.dwg)", result.Report);
            Assert.Equal(files.Last(), missing);
        }

        [Fact]
        public void AFileUnticked_IsLeftOut_AndNoPathIsWrittenAgainToIt()
        {
            Project project = Tower();
            CadTransmittal transmittal = Transmittal(project);
            IReadOnlyList<CadTransmittalFile> files = transmittal.Gather(null);
            files.Single(f => f.Name == "site-photo.jpg").IsIncluded = false;

            CadTransmittalResult result = transmittal.Write(files, ZipOf("unticked"));

            Assert.DoesNotContain("Files/site-photo.jpg", Entries(ZipOf("unticked")));
            Assert.DoesNotContain(result.Changes, c => c.Contains("image"));
            Assert.Contains("Files left out by the sender", result.Report);
            using (ZipArchive archive = ZipFile.OpenRead(ZipOf("unticked")))
            {
                Assert.Equal(File.ReadAllBytes(project.A101), ReadEntry(archive, "Architectural/a101.dwg"));
            }
        }

        [Fact]
        public void ASelectionOfOneSheet_SendsThatSheetAndWhatItNeeds_AndTheSetsOwnFiles()
        {
            Project project = Tower();
            CadSheetSetSheet cover = project.File.SheetSet.AllSheets().Single(s => s.Number == "G-001");
            CadTransmittal transmittal = Transmittal(project);

            IReadOnlyList<CadTransmittalFile> files = transmittal.Gather(new CadSheetComponent[] { cover });

            Assert.Equal(
                new[] { "Tower.dst", "cover.dwg", "sheets.dwt", "tower.ctb" },
                files.Select(f => f.Name).ToArray()
            );
        }

        [Fact]
        public void ASubsetSelected_SendsEverySheetUnderIt()
        {
            Project project = Tower();
            CadSheetSubset architectural = project.File.SheetSet.Subsets.Single();
            CadTransmittal transmittal = Transmittal(project);

            IReadOnlyList<CadTransmittalFile> files = transmittal.Gather(new CadSheetComponent[] { architectural });

            Assert.Equal(
                new[] { "Tower.dst", "a101.dwg", "model.dwg", "sheets.dwt", "site-photo.jpg" },
                files.Select(f => f.Name).ToArray()
            );
        }

        [Fact]
        public void TwoFilesOfOneNameOutsideTheSet_GoInFilesTheSecondNumbered_InTheOrderOfTheirPaths()
        {
            Project project = Tower();
            string other = Path.Combine(_folder, "Elsewhere");
            Directory.CreateDirectory(other);
            File.WriteAllBytes(Path.Combine(other, "site-photo.jpg"), new byte[] { 1, 2, 3 });
            SheetSetTestDrawings.Edit(project.Cover, database =>
                DxfRasterImageDef
                    .CreateImageDictionary(database)
                    .SetAt("second", new DxfRasterImageDef(Path.Combine(other, "site-photo.jpg"), new GeoVector2(1, 1)))
            );
            CadTransmittal transmittal = Transmittal(project);

            List<CadTransmittalFile> images = transmittal
                .Gather(null)
                .Where(f => f.Kind == CadTransmittalFileKind.Image)
                .ToList();

            Assert.Equal(
                new[]
                {
                    project.Photo + " -> Files\\site-photo (2).jpg",
                    Path.Combine(other, "site-photo.jpg") + " -> Files\\site-photo.jpg",
                },
                images.Select(f => f.Source + " -> " + f.PathInZip).ToArray()
            );
        }

        [Fact]
        public void ATableTheProgramMakesItself_IsNotSent_AndOneThatIsNowhereIsListedNotFound()
        {
            Project project = Tower();
            SheetSetTestDrawings.Edit(project.Cover, database =>
                database.Layouts.Single(l => l.LayoutName == "Sheet").CurrentStyleSheet = "monochrome.ctb"
            );
            SheetSetTestDrawings.Edit(project.A101, database =>
                database.Layouts.Single(l => l.LayoutName == "Sheet").CurrentStyleSheet = "client.ctb"
            );
            CadTransmittal transmittal = Transmittal(project);

            IReadOnlyList<CadTransmittalFile> files = transmittal.Gather(null);

            Assert.DoesNotContain(files, f => f.Name == "monochrome.ctb");
            CadTransmittalFile client = files.Single(f => f.Name == "client.ctb");
            Assert.False(client.IsFound);
            Assert.Equal(CadTransmittalFileKind.PlotStyleTable, client.Kind);
        }

        [Fact]
        public void TheDefaultZip_IsTheSetsNameInATransmittalsFolderBesideTheDst()
        {
            Project project = Tower();

            Assert.Equal(Path.Combine(Root, "Transmittals", "Example Tower.zip"), Transmittal(project).DefaultZipPath);
        }

        [Fact]
        public void ADrawingWithFields_IsUpdatedInTheCopy_OnlyWhereTheBoxIsOn()
        {
            Project project = Tower(inside: true);
            CadDocument cover = CadDocument.Open(project.Cover);
            CadFields.SetEval(cover.Database, 28);
            var text = new DxfDBText
            {
                TextString = "stale",
                Position = new GeoPoint3(5, 5, 0),
                Height = 3,
            };
            cover.Database.Layouts.Single(l => l.LayoutName == "Sheet").BlockTableRecord.AppendEntity(text);
            CadFields.Set(
                text,
                "%<\\AcVar Date \\f \"yyyy\">%",
                new CadFieldContext(cover) { Now = new DateTime(2000, 1, 2) }
            );
            cover.SaveAs(project.Cover);
            Assert.Equal("2000", YearIn(project.Cover, null));
            CadTransmittal on = Transmittal(project);
            CadTransmittal off = Transmittal(project);
            off.UpdateFields = false;

            CadTransmittalResult updated = on.Write(on.Gather(null), ZipOf("fields-on"));
            CadTransmittalResult kept = off.Write(off.Gather(null), ZipOf("fields-off"));

            Assert.Contains("cover.dwg: its fields were updated.", updated.Changes);
            Assert.DoesNotContain(kept.Changes, c => c.Contains("fields"));
            using (ZipArchive archive = ZipFile.OpenRead(ZipOf("fields-off")))
            {
                Assert.Equal(File.ReadAllBytes(project.Cover), ReadEntry(archive, "cover.dwg"));
            }

            Assert.Equal(
                DateTime.Now.Year.ToString(System.Globalization.CultureInfo.InvariantCulture),
                YearIn(ZipOf("fields-on"), "cover.dwg")
            );
            Assert.Equal("2000", YearIn(project.Cover, null));
        }

        private string YearIn(string zipOrDrawing, string entry)
        {
            string path = zipOrDrawing;
            if (entry != null)
            {
                path = Path.Combine(_folder, "peek-" + Guid.NewGuid().ToString("N") + ".dwg");
                using (ZipArchive archive = ZipFile.OpenRead(zipOrDrawing))
                {
                    File.WriteAllBytes(path, ReadEntry(archive, entry));
                }
            }

            return SheetSetTestDrawings
                .Read(path)
                .Layouts.Single(l => l.LayoutName == "Sheet")
                .BlockTableRecord.OfType<DxfDBText>()
                .Single()
                .TextString;
        }
    }
}
