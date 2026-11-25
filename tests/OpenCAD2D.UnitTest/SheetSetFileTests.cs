using System;
using System.IO;
using System.Linq;
using System.Text;
using System.Xml.Linq;
using OpenCAD2D.SheetSets;
using Xunit;

namespace OpenCAD2D.UnitTest
{
    /// <summary>
    /// The sheet set data file (.dst) as a file: opened, saved the way AutoCAD saves it (one line, properties in alphabetical
    /// order, every change at once, a revision count), the parts this program does not model kept as they were, and a stale
    /// copy reloaded before a change. Every set is built in code; no Autodesk file is used.
    /// </summary>
    public class SheetSetFileTests : IDisposable
    {
        private readonly string _folder = Path.Combine(
            Path.GetTempPath(),
            "gk",
            "sheetsetfile-" + Guid.NewGuid().ToString("N")
        );

        public SheetSetFileTests()
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

        /// <summary>A GUID source that counts: A0000001-0000-..., A0000002-0000-..., so the IDs are the same on every run.</summary>
        internal static Func<Guid> Counter()
        {
            uint n = 0xA0000000;
            return () => new Guid(++n, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0);
        }

        private string Dst(string name = "set.dst") => Path.Combine(_folder, name);

        /// <summary>The sheet set data file as plain text, the cipher taken off.</summary>
        private static string Text(string path) => CadDstCodec.Decode(File.ReadAllBytes(path));

        [Fact]
        public void ANewSet_IsWrittenAsAutoCadWritesOne_OnOneLineWithCrLfAtTheEnds()
        {
            CadSheetSetFile.Create(Dst(), "Tower", "A tower", Counter());

            string text = Text(Dst());

            Assert.StartsWith(
                "<?xml version=\"1.0\" encoding=\"UTF-8\"?>\r\n<AcSmDatabase clsid=\"g2162C6B6-0CE4-40E8-912B-46F59DFDF826\"",
                text
            );
            Assert.EndsWith("</AcSmDatabase>\r\n", text);
            Assert.Equal(2, text.Count(c => c == '\n'));
            Assert.DoesNotContain("\t", text);
            Assert.DoesNotContain(" />", text);
            Assert.Contains("<AcSmProjectPointLocations ", text);
        }

        [Fact]
        public void ASetBuiltInCode_SavedAndReopened_ReadsBackElementByElement()
        {
            CadSheetSetFile built = BuildTower(Dst());
            string before = Dump(built.SheetSet);

            CadSheetSetFile reopened = CadSheetSetFile.Open(Dst(), Counter());

            Assert.Equal(before, Dump(reopened.SheetSet));
            Assert.True(XNode.DeepEquals(built.Document, reopened.Document));
            Assert.Equal(built.FileRevision, reopened.FileRevision);
            Assert.Equal(built.DbFingerPrint, reopened.DbFingerPrint);
            Assert.Equal("1.1", reopened.DbVersion);
        }

        [Fact]
        public void TheSetBuiltTwiceWithACounter_GivesTheSameBytes()
        {
            BuildTower(Dst());
            byte[] first = File.ReadAllBytes(Dst());
            File.Delete(Dst());

            BuildTower(Dst());

            Assert.Equal(first, File.ReadAllBytes(Dst()));
        }

        [Fact]
        public void TheIds_AreTheCountersUpperCaseGuidsAfterAG()
        {
            CadSheetSetFile file = CadSheetSetFile.Create(Dst(), "Tower", null, Counter());

            Assert.Equal("gA0000001-0000-0000-0000-000000000000", (string)file.Document.Root.Attribute("ID"));
            Assert.Equal("gA0000002-0000-0000-0000-000000000000", file.DbFingerPrint);
            Assert.Equal("gA0000003-0000-0000-0000-000000000000", file.SheetSet.Id);
        }

        [Fact]
        public void EverySave_RaisesTheFileRevisionByOne_AndLeavesNoTemporaryFile()
        {
            CadSheetSetFile file = CadSheetSetFile.Create(Dst(), "Tower", null, Counter());
            Assert.Equal(1, file.FileRevision);

            file.Change(set => set.Description = "one");
            file.Change(set => set.Description = "two");

            Assert.Equal(3, file.FileRevision);
            Assert.Equal(3, CadSheetSetFile.Open(Dst()).FileRevision);
            Assert.Equal(new[] { Dst() }, Directory.GetFiles(_folder));
        }

        [Fact]
        public void AChangeThatThrows_SavesNothing_AndLeavesTheTreeAsItWas()
        {
            CadSheetSetFile file = CadSheetSetFile.Create(Dst(), "Tower", null, Counter());
            byte[] bytes = File.ReadAllBytes(Dst());
            string before = Dump(file.SheetSet);

            Assert.Throws<InvalidOperationException>(() =>
                file.Change(set =>
                {
                    set.Name = "Renamed";
                    set.AddSubset("Plans");
                    throw new InvalidOperationException("stop");
                })
            );

            Assert.Equal(bytes, File.ReadAllBytes(Dst()));
            Assert.Equal(before, Dump(file.SheetSet));
            Assert.Equal(1, file.FileRevision);
        }

        [Fact]
        public void ATreeEditedOutsideAChange_IsRefused()
        {
            CadSheetSetFile file = CadSheetSetFile.Create(Dst(), "Tower", null, Counter());

            InvalidOperationException error = Assert.Throws<InvalidOperationException>(() => file.SheetSet.Name = "x");

            Assert.Contains("Change", error.Message);
            Assert.Equal("Tower", file.SheetSet.Name);
        }

        [Fact]
        public void AChangeAfterTheFileMovedOnDisk_IsAppliedToTheFreshCopy()
        {
            CadSheetSetFile mine = CadSheetSetFile.Create(Dst(), "Tower", null, Counter());
            CadSheetSetFile other = CadSheetSetFile.Open(Dst(), Counter());
            other.Change(set => set.Name = "Tower B");
            File.SetLastWriteTimeUtc(Dst(), DateTime.UtcNow.AddMinutes(5));
            Assert.True(mine.HasChangedOnDisk);

            mine.Change(set => set.Description = "mine");

            CadSheetSetFile read = CadSheetSetFile.Open(Dst());
            Assert.Equal("Tower B", read.SheetSet.Name);
            Assert.Equal("mine", read.SheetSet.Description);
            Assert.Equal(3, read.FileRevision);
            Assert.Equal(3, mine.FileRevision);
            Assert.False(mine.HasChangedOnDisk);
        }

        [Fact]
        public void ReloadIfChanged_ReloadsOnlyWhereTheFileMoved()
        {
            CadSheetSetFile mine = CadSheetSetFile.Create(Dst(), "Tower", null, Counter());
            Assert.False(mine.ReloadIfChanged());
            CadSheetSetFile other = CadSheetSetFile.Open(Dst(), Counter());
            other.Change(set => set.Name = "Tower B");
            File.SetLastWriteTimeUtc(Dst(), DateTime.UtcNow.AddMinutes(5));

            Assert.True(mine.ReloadIfChanged());

            Assert.Equal("Tower B", mine.SheetSet.Name);
            Assert.False(mine.ReloadIfChanged());
        }

        [Fact]
        public void PartsThisProgramDoesNotModel_AreKeptWholeThroughAChangeAndASave()
        {
            CadSheetSetFile file = CadSheetSetFile.Create(Dst(), "Tower", null, Counter());
            string unknownSelSets =
                "<AcSmSheetSelSets clsid=\"g444780B8-6527-43A8-8DC4-FAB41B7E48BB\" ID=\"gAAAA0001-0000-0000-0000-000000000001\" propname=\"SheetSelSets\" vt=\"13\">"
                + "<AcSmSheetSelSet clsid=\"g11111111-2222-3333-4444-555555555555\" ID=\"gAAAA0002-0000-0000-0000-000000000002\" propname=\"Odd &amp; Even\" vt=\"13\"><AcSmProp propname=\"Flag\" vt=\"2\">-1</AcSmProp></AcSmSheetSelSet>"
                + "</AcSmSheetSelSets>";
            string unknownDb = "<FutureThing ID=\"gAAAA0003-0000-0000-0000-000000000003\" note=\"x &quot;y&quot;\"/>";
            // The text of the file with the empty selection sets of the new set swapped for ours, and an element of a class
            // this program has never heard of added to the database.
            string text = Text(Dst());
            int from = text.IndexOf("<AcSmSheetSelSets", StringComparison.Ordinal);
            int to = text.IndexOf("/>", from, StringComparison.Ordinal) + 2;
            text = text.Substring(0, from) + unknownSelSets + text.Substring(to);
            text = text.Replace("</AcSmDatabase>", unknownDb + "</AcSmDatabase>");
            File.WriteAllBytes(Dst(), CadDstCodec.Encode(text));
            CadSheetSetFile reopened = CadSheetSetFile.Open(Dst(), Counter());

            reopened.Change(set =>
            {
                set.Name = "Tower B";
                set.AddSubset("Plans");
            });

            string saved = Text(Dst());
            Assert.Contains(unknownSelSets, saved);
            Assert.Contains(unknownDb, saved);
            Assert.Contains("Tower B", saved);
        }

        [Fact]
        public void NewProperties_AreInsertedInAlphabeticalOrderOfTheirNames_AsAutoCadWritesThem()
        {
            CadSheetSetFile file = CadSheetSetFile.Create(Dst(), "Tower", null, Counter());

            file.Change(set =>
            {
                CadSheetSetSheet sheet = set.AddSheet("A-101", "Ground floor", null);
                sheet.RevisionNumber = "B";
                sheet.Description = "Plan";
                sheet.IssuePurpose = "Tender";
                sheet.RevisionDate = "2026-11-26";
                sheet.IncludeForPublish = false;
                sheet.Category = "Plans";
            });

            XElement sheetElement = file.Document.Root.Element("AcSmSheetSet").Element("AcSmSheet");
            string[] names = sheetElement.Elements().Select(e => (string)e.Attribute("propname")).ToArray();
            Assert.Equal(
                new[]
                {
                    "Category",
                    "Desc",
                    "DoNotPlot",
                    "IssuePurpose",
                    "Number",
                    "RevisionDate",
                    "RevisionNumber",
                    "SheetViews",
                    "Title",
                },
                names
            );
        }

        [Fact]
        public void CustomPropertyNames_AreOrderedLetterCaseIgnored_AsAutoCadOrdersThem()
        {
            CadSheetSetFile file = CadSheetSetFile.Create(Dst(), "Tower", null, Counter());

            file.Change(set =>
            {
                foreach (string name in new[] { "zeta", "Alpha", "beta", "Zed", "alpha2" })
                {
                    set.SetCustomProperty(name, "v-" + name, CadSheetSetPropertyFlags.Set);
                }
            });

            string[] names = file
                .Document.Root.Element("AcSmSheetSet")
                .Element("AcSmCustomPropertyBag")
                .Elements()
                .Select(e => (string)e.Attribute("propname"))
                .ToArray();
            Assert.Equal(new[] { "Alpha", "alpha2", "beta", "Zed", "zeta" }, names);
        }

        [Fact]
        public void TheChildrenOfASet_AreItsPropertiesInAlphabeticalOrder_ThenItsSubsetsAndSheetsInSetOrder()
        {
            CadSheetSetFile file = CadSheetSetFile.Create(Dst(), "Tower", null, Counter());

            file.Change(set =>
            {
                set.AddSubset("Plans");
                set.ProjectName = "Tower";
                set.Description = "d";
            });

            string[] names = file
                .Document.Root.Element("AcSmSheetSet")
                .Elements()
                .Select(e => (string)e.Attribute("propname") ?? "<" + e.Name.LocalName + ">")
                .ToArray();
            Assert.Equal("<AcSmSubset>", names.Last());
            Assert.Equal(
                names.Take(names.Length - 1).OrderBy(n => n, StringComparer.OrdinalIgnoreCase).ToArray(),
                names.Take(names.Length - 1).ToArray()
            );
        }

        [Fact]
        public void APropertyAtItsDefault_IsLeftOutOfTheFile_AsAutoCadLeavesItOut()
        {
            CadSheetSetFile file = CadSheetSetFile.Create(Dst(), "Tower", "d", Counter());

            file.Change(set =>
            {
                set.Description = "";
                CadSheetSetSheet sheet = set.AddSheet("", "T", null);
                sheet.IncludeForPublish = true;
            });

            string text = Text(Dst());
            Assert.DoesNotContain("propname=\"Desc\"", text);
            Assert.DoesNotContain("propname=\"Number\"", text);
            Assert.DoesNotContain("DoNotPlot", text);
        }

        [Fact]
        public void LineEndsAndTabsInAValue_AreWrittenAsCrLfAndTab_AndReadBackAsLfAndTab()
        {
            CadSheetSetFile file = CadSheetSetFile.Create(Dst(), "Tower", null, Counter());

            file.Change(set => set.Description = "one\r\ntwo\rthree\nfour\tfive");

            string text = Text(Dst());
            Assert.Contains(">one\r\ntwo\r\nthree\r\nfour\tfive<", text);
            Assert.Equal("one\ntwo\nthree\nfour\tfive", CadSheetSetFile.Open(Dst()).SheetSet.Description);
        }

        [Fact]
        public void TheCharactersOfXml_AreEscapedAsAutoCadEscapesThem()
        {
            CadSheetSetFile file = CadSheetSetFile.Create(Dst(), "Tower", null, Counter());

            file.Change(set => set.Name = "Set <&> \"q\" 'a' \u00E9 \u4E2D \U0001F600");

            string text = Text(Dst());
            Assert.Contains(">Set &lt;&amp;&gt; \"q\" 'a' \u00E9 \u4E2D \U0001F600<", text);
            Assert.Equal("Set <&> \"q\" 'a' \u00E9 \u4E2D \U0001F600", CadSheetSetFile.Open(Dst()).SheetSet.Name);
        }

        [Fact]
        public void AControlCharacterXmlCannotHold_IsRefusedAtTheWriter_AndNothingIsSaved()
        {
            CadSheetSetFile file = CadSheetSetFile.Create(Dst(), "Tower", null, Counter());
            byte[] bytes = File.ReadAllBytes(Dst());

            ArgumentException error = Assert.Throws<ArgumentException>(() =>
                file.Change(set => set.Description = "a\u0001b")
            );

            Assert.Contains("control character", error.Message);
            Assert.Equal(bytes, File.ReadAllBytes(Dst()));
            Assert.Equal("", file.SheetSet.Description);
        }

        [Fact]
        public void AFileReference_ResolvesByItsRelativePathFirst_SoAMovedFolderKeepsWorking()
        {
            string oldFolder = Path.Combine(_folder, "old");
            string newFolder = Path.Combine(_folder, "new");
            Directory.CreateDirectory(Path.Combine(oldFolder, "dwg"));
            File.WriteAllText(Path.Combine(oldFolder, "dwg", "A-101.dwg"), "x");
            CadFileReference reference = CadFileReference.FromPath(
                Path.Combine(oldFolder, "dwg", "A-101.dwg"),
                oldFolder
            );
            Assert.Equal(@".\dwg\A-101.dwg", reference.RelativeFileName);
            Assert.Equal(Path.Combine(oldFolder, "dwg", "A-101.dwg"), reference.Resolve(oldFolder));

            Directory.Move(oldFolder, newFolder);

            Assert.Equal(Path.Combine(newFolder, "dwg", "A-101.dwg"), reference.Resolve(newFolder));
        }

        [Fact]
        public void AFileReference_FallsBackToTheAbsolutePath_ThenTheEnvironmentForm_AndNullWhereNoneExists()
        {
            string target = Path.Combine(_folder, "elsewhere", "t.dwt");
            Directory.CreateDirectory(Path.GetDirectoryName(target));
            File.WriteAllText(target, "x");
            string folder = Path.Combine(_folder, "sets");
            Directory.CreateDirectory(folder);
            Environment.SetEnvironmentVariable("OC_SHEETSET_TEST", Path.GetDirectoryName(target));
            try
            {
                var absolute = new CadFileReference(target, @".\missing.dwt", null, null);
                var environ = new CadFileReference(
                    @"Z:\gone\t.dwt",
                    @".\missing.dwt",
                    @"%OC_SHEETSET_TEST%\t.dwt",
                    null
                );
                var none = new CadFileReference(
                    @"Z:\gone\t.dwt",
                    @".\missing.dwt",
                    @"%OC_SHEETSET_TEST%\no.dwt",
                    @"$(CSIDL_NOWHERE)\x"
                );

                Assert.Equal(target, absolute.Resolve(folder));
                Assert.Equal(target, environ.Resolve(folder));
                Assert.Null(none.Resolve(folder));
            }
            finally
            {
                Environment.SetEnvironmentVariable("OC_SHEETSET_TEST", null);
            }
        }

        [Fact]
        public void AFileReference_WithASpecialFolderForm_ResolvesThroughTheFolderOfThatName()
        {
            string documents = Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments);
            var reference = new CadFileReference(null, null, null, @"$(CSIDL_PERSONAL)");

            Assert.Equal(Directory.Exists(documents) ? documents : null, reference.Resolve(_folder));
        }

        [Fact]
        public void APathOnAnotherDrive_HasNoRelativeForm()
        {
            CadFileReference reference = CadFileReference.FromPath(@"Z:\other\x.dwg", @"C:\sets");

            Assert.Equal(@"Z:\other\x.dwg", reference.FileName);
            Assert.Null(reference.RelativeFileName);
        }

        [Fact]
        public void APathAboveTheSetFolder_IsWrittenWithDotDot_AndThePathOfTheFolderItselfAsADot()
        {
            Assert.Equal(@"..\x\y.dwg", CadFileReference.FromPath(@"C:\a\x\y.dwg", @"C:\a\sets").RelativeFileName);
            Assert.Equal(".", CadFileReference.FromPath(@"C:\a\sets", @"C:\a\sets").RelativeFileName);
        }

        [Fact]
        public void AFileThatIsNotASheetSetFile_IsRefused_WithTheMessageOfTheCodec()
        {
            File.WriteAllText(Dst(), "<AcSmDatabase/>");

            FormatException error = Assert.Throws<FormatException>(() => CadSheetSetFile.Open(Dst()));

            Assert.StartsWith("the file is not a sheet set data file", error.Message);
        }

        [Fact]
        public void AFileOfAnotherXmlDocument_IsRefused()
        {
            File.WriteAllBytes(Dst(), CadDstCodec.Encode("<?xml version=\"1.0\" encoding=\"UTF-8\"?>\r\n<Other/>\r\n"));

            FormatException error = Assert.Throws<FormatException>(() => CadSheetSetFile.Open(Dst()));

            Assert.StartsWith("the file is not a sheet set data file", error.Message);
        }

        [Fact]
        public void AFileThatIsNotThere_IsRefusedAsAFileNotFound()
        {
            Assert.Throws<FileNotFoundException>(() => CadSheetSetFile.Open(Dst("none.dst")));
        }

        [Fact]
        public void ANewSetOverAFileThatIsThere_IsRefused_AndTheFileIsLeftAlone()
        {
            File.WriteAllText(Dst(), "mine");

            Assert.Throws<IOException>(() => CadSheetSetFile.Create(Dst(), "Tower"));

            Assert.Equal("mine", File.ReadAllText(Dst()));
        }

        /// <summary>A small tower: two subsets (one inside the other), three sheets, custom properties of both owners.</summary>
        internal static CadSheetSetFile BuildTower(string path)
        {
            CadSheetSetFile file = CadSheetSetFile.Create(path, "Example Tower", "A tower of 12 storeys", Counter());
            string folder = Path.GetDirectoryName(path);
            file.Change(set =>
            {
                set.ProjectNumber = "T-2026";
                set.ProjectName = "Tower B";
                set.ProjectPhase = "Design";
                set.ProjectMilestone = "50%";
                set.Template = new CadDrawingReference(
                    CadFileReference.FromPath(Path.Combine(folder, "OpenCAD2D-mm.dwt"), folder),
                    "A1 sheet",
                    "2B",
                    null
                );
                set.NewSheetLocation = CadFileReference.FromPath(Path.Combine(folder, "Sheets"), folder);
                set.PromptForTemplate = true;
                set.PageSetupOverrides = CadFileReference.FromPath(Path.Combine(folder, "Setups.dwt"), folder);
                set.AddResourceLocation(CadFileReference.FromPath(Path.Combine(folder, "Models"), folder));
                set.AddResourceLocation(CadFileReference.FromPath(Path.Combine(folder, "Details"), folder));
                set.SetCustomProperty("Client", "Acme & Sons", CadSheetSetPropertyFlags.Set);
                set.SetCustomProperty("Drawn By", "NT", CadSheetSetPropertyFlags.Sheet);
                CadSheetSubset plans = set.AddSubset("Plans", "Floor plans");
                CadSheetSubset details = plans.AddSubset("Details");
                CadSheetSetSheet g = set.AddSheet("G-001", "Cover", null, 0);
                CadSheetSetSheet a = plans.AddSheet(
                    "A-101",
                    "Ground floor",
                    new CadDrawingReference(
                        CadFileReference.FromPath(Path.Combine(folder, "Sheets", "A-101.dwg"), folder),
                        "Ground floor",
                        "1A2B",
                        "1B"
                    )
                );
                a.Description = "Ground floor plan";
                a.RevisionNumber = "C";
                a.SetCustomProperty("Drawn By", "TN", CadSheetSetPropertyFlags.Sheet);
                CadSheetSetSheet d = details.AddSheet("A-501", "Stair detail", null);
                d.IncludeForPublish = false;
                CadViewCategory category = set.AddViewCategory("Floor Plans", "Plans of the floors");
                CadSheetView view = a.AddView(
                    "1",
                    "Ground floor",
                    new CadDrawingReference(
                        CadFileReference.FromPath(Path.Combine(folder, "Sheets", "A-101.dwg"), folder),
                        "GROUND",
                        "2C",
                        "6"
                    ),
                    category
                );
                Assert.NotNull(view.Id);
                Assert.NotNull(g.Id);
            });
            return file;
        }

        /// <summary>The model as text, in set order: every property of every part this program models.</summary>
        internal static string Dump(CadSheetSet set)
        {
            var text = new StringBuilder();
            text.AppendLine("SET " + set.Id + " " + set.Name + "|" + set.Description);
            text.AppendLine(
                "  project "
                    + set.ProjectNumber
                    + "|"
                    + set.ProjectName
                    + "|"
                    + set.ProjectPhase
                    + "|"
                    + set.ProjectMilestone
            );
            text.AppendLine("  template " + Describe(set.Template));
            text.AppendLine("  location " + Describe(set.NewSheetLocation) + " prompt=" + set.PromptForTemplate);
            text.AppendLine("  overrides " + Describe(set.PageSetupOverrides) + " label=" + Describe(set.LabelBlock));
            foreach (CadFileReference location in set.ResourceLocations)
            {
                text.AppendLine("  resource " + Describe(location));
            }

            foreach (CadViewCategory category in set.ViewCategories)
            {
                text.AppendLine("  category " + category.Id + " " + category.Name + "|" + category.Description);
            }

            DumpProperties(text, "  ", set);
            DumpChildren(text, "  ", set);
            return text.ToString();
        }

        private static void DumpChildren(StringBuilder text, string indent, CadSheetSubset parent)
        {
            foreach (CadSheetComponent child in parent.Children)
            {
                if (child is CadSheetSubset subset)
                {
                    text.AppendLine(indent + "SUBSET " + subset.Id + " " + subset.Name + "|" + subset.Description);
                    text.AppendLine(
                        indent + "  template " + Describe(subset.Template) + " prompt=" + subset.PromptForTemplate
                    );
                    DumpProperties(text, indent + "  ", subset);
                    DumpChildren(text, indent + "  ", subset);
                }
                else
                {
                    var sheet = (CadSheetSetSheet)child;
                    text.AppendLine(
                        indent
                            + "SHEET "
                            + sheet.Id
                            + " "
                            + sheet.Number
                            + "|"
                            + sheet.Title
                            + "|"
                            + sheet.Description
                            + "|"
                            + sheet.IncludeForPublish
                            + "|"
                            + sheet.RevisionNumber
                            + "|"
                            + sheet.RevisionDate
                            + "|"
                            + sheet.IssuePurpose
                            + "|"
                            + sheet.Category
                    );
                    text.AppendLine(indent + "  layout " + Describe(sheet.Layout));
                    DumpProperties(text, indent + "  ", sheet);
                    foreach (CadSheetView view in sheet.Views)
                    {
                        text.AppendLine(
                            indent
                                + "  VIEW "
                                + view.Id
                                + " "
                                + view.Number
                                + "|"
                                + view.Title
                                + "|"
                                + Describe(view.NamedView)
                                + "|"
                                + view.Category?.Id
                        );
                    }
                }
            }
        }

        private static void DumpProperties(StringBuilder text, string indent, CadSheetComponent owner)
        {
            foreach (CadSheetSetProperty property in owner.CustomProperties)
            {
                text.AppendLine(
                    indent + "prop " + property.Name + "=" + property.Value + " flags=" + (int)property.Flags
                );
            }
        }

        internal static string Describe(CadFileReference reference) =>
            reference == null
                ? "-"
                : reference.FileName
                    + ";"
                    + reference.RelativeFileName
                    + ";"
                    + reference.EnvironFileName
                    + ";"
                    + reference.SpecialFolderFileName;

        internal static string Describe(CadDrawingReference reference) =>
            reference == null
                ? "-"
                : Describe(reference.File)
                    + ";"
                    + reference.Name
                    + ";"
                    + reference.Handle
                    + ";"
                    + reference.OwnerHandle;
    }
}
