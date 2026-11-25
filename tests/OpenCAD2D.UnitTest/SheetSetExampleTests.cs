using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using GeometryHelper.Dxf;
using OpenCAD2D.SheetSets;
using Xunit;

namespace OpenCAD2D.UnitTest
{
    /// <summary>
    /// The example sheet sets that ship in the SheetSetExamples folder (A12-D19) and the template of their sheets: they are there,
    /// they read back as the code makes them, a set made from one opens with its subsets and properties, and a sheet made in it
    /// takes the "A1 sheet" layout of the template with the title block's fields bound to itself. The files are written from the
    /// code: set the environment variable OPENCAD2D_WRITE_SHEETSET_EXAMPLES to the full path of the folder to write and run
    /// <see cref="TheExamplesAreWrittenWhereTheVariableSays"/>.
    /// </summary>
    public sealed class SheetSetExampleTests : IDisposable
    {
        private const string WriteVariable = "OPENCAD2D_WRITE_SHEETSET_EXAMPLES";

        private readonly string _folder = Path.Combine(
            Path.GetTempPath(),
            "gk",
            "sheetsetexamples-" + Guid.NewGuid().ToString("N")
        );

        public SheetSetExampleTests()
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

        private static string Shipped(string file) => Path.Combine(CadSheetSetExamples.ShippedFolder, file);

        private static string Text(string path) => CadDstCodec.Decode(File.ReadAllBytes(path));

        [Fact]
        public void TheExamplesAreWrittenWhereTheVariableSays()
        {
            string target = Environment.GetEnvironmentVariable(WriteVariable);

            if (string.IsNullOrWhiteSpace(target))
            {
                return;
            }

            Assert.True(Path.IsPathRooted(target), WriteVariable + " is the full path of the folder to write.");
            CadSheetSetExamples.Write(target);
            Assert.True(File.Exists(Path.Combine(target, CadSheetSetExamples.TemplateFileName)));
        }

        [Fact]
        public void TheExamplesShipInTheirFolderBesideTheDll_AreListedByName_AndNoFileIsAutodesks()
        {
            Assert.Equal(
                new[] { "New Sheet Set.dst", "OpenCAD2D Metric Sheet Set.dst" },
                CadSheetSetExamples.Shipped().Select(Path.GetFileName)
            );
            Assert.True(File.Exists(Shipped(CadSheetSetExamples.TemplateFileName)));
            Assert.Equal(
                Path.Combine(CadSupportFolders.Default.ProgramFolder, "SheetSetExamples"),
                CadSheetSetExamples.ShippedFolder
            );
            Assert.Empty(CadSheetSetExamples.FindIn(Path.Combine(_folder, "nowhere")));
        }

        [Fact]
        public void TheShippedSets_AreTheBytesTheCodeMakes_AndNameTheirFilesByRelativePathsAlone()
        {
            string made = Path.Combine(_folder, "made");
            CadSheetSetExamples.Write(made);

            foreach (string name in new[] { "New Sheet Set.dst", "OpenCAD2D Metric Sheet Set.dst" })
            {
                string text = Text(Shipped(name));
                Assert.Equal(Text(Path.Combine(made, name)), text);
                Assert.DoesNotContain(":\\", text);
                Assert.Contains("Relative_FileName", text);
            }
        }

        [Fact]
        public void TheTemplate_ReadsBackAsTheDrawingTheCodeMakes_WithTheDescriptionAndTheOneSheet()
        {
            DxfDatabase shipped = CadDrawingFile.Open(Shipped(CadSheetSetExamples.TemplateFileName));

            Assert.True(CadDrawingFile.IsDwg(Shipped(CadSheetSetExamples.TemplateFileName)));
            Assert.Equal(
                CadSheetSetExamples.TemplateDescription,
                CadTemplate.DescriptionOf(Shipped(CadSheetSetExamples.TemplateFileName))
            );
            Assert.Equal(new[] { "A1 sheet" }, SheetSetTestDrawingsOrder(shipped));
            Assert.Equal(
                SheetSetTestDrawingsOrder(CadSheetSetExamples.CreateTemplate()),
                SheetSetTestDrawingsOrder(shipped)
            );
        }

        private static string[] SheetSetTestDrawingsOrder(DxfDatabase database) =>
            database
                .Layouts.Where(l => l.LayoutName != "Model")
                .OrderBy(l => l.TabOrder)
                .Select(l => l.LayoutName)
                .ToArray();

        [Fact]
        public void TheSheetOfTheTemplate_HasAFrameAndATitleBlockWhoseAttributesHoldTheSheetSetFields()
        {
            DxfDatabase database = CadDrawingFile.Open(Shipped(CadSheetSetExamples.TemplateFileName));

            foreach (string name in new[] { "A1 sheet" })
            {
                DxfLayout layout = database.Layouts.Single(l => l.LayoutName == name);
                Assert.Single(layout.BlockTableRecord.OfType<DxfPolyline>());
                DxfBlockReference insert = layout.BlockTableRecord.OfType<DxfBlockReference>().Single();
                var codes = insert.AttributeCollection.ToDictionary(a => a.Tag, a => CadFields.CodesOf(a));
                Assert.Equal("%<\\AcSm Sheet.Number>%", codes["NUMBER"]);
                Assert.Equal("%<\\AcSm Sheet.Title>%", codes["TITLE"]);
                Assert.Equal("%<\\AcSm SheetSet.Name>%", codes["PROJECT"]);
                Assert.Equal("%<\\AcSm SheetSet.Client>%", codes["CLIENT"]);
                Assert.Equal("%<\\AcSm SheetSet.Project Address>%", codes["ADDRESS"]);
                Assert.Equal("%<\\AcSm Sheet.Drawn By>%", codes["DRAWNBY"]);
                Assert.Equal("%<\\AcSm Sheet.Checked By>%", codes["CHECKEDBY"]);
            }
        }

        [Fact]
        public void TheEmptyExample_HasNoSubsetNorSheet_AndMakesASetThatAsksForATemplate()
        {
            string dst = Path.Combine(_folder, "New", "Fresh.dst");
            Directory.CreateDirectory(Path.GetDirectoryName(dst));

            CadSheetSetFile file = CadSheetSetFile.CreateFromExample(
                dst,
                "Fresh",
                null,
                Shipped("New Sheet Set.dst"),
                false
            );

            Assert.Equal("Fresh", file.SheetSet.Name);
            Assert.Empty(file.SheetSet.Children);
            Assert.True(file.SheetSet.PromptForTemplate);
            Assert.Null(file.SheetSet.Template);
            Assert.Equal(
                Path.GetDirectoryName(dst),
                file.SheetSet.NewSheetLocation.Resolve(file.Folder),
                ignoreCase: true
            );
        }

        [Fact]
        public void TheMetricExample_MakesASetWithItsSubsetsAndProperties_AndASheetFromTheTemplatesA1Layout()
        {
            string dst = Path.Combine(_folder, "Tower", "Tower.dst");
            Directory.CreateDirectory(Path.GetDirectoryName(dst));
            CadSheetSetFile file = CadSheetSetFile.CreateFromExample(
                dst,
                "Tower",
                "A tower",
                Shipped("OpenCAD2D Metric Sheet Set.dst"),
                false
            );
            Assert.Equal(new[] { "General", "Plans", "Details" }, file.SheetSet.Subsets.Select(s => s.Name));
            Assert.Equal(
                new[] { "Checked By", "Client", "Drawn By", "Project Address" },
                file.SheetSet.CustomProperties.Select(p => p.Name)
            );
            Assert.False(file.SheetSet.PromptForTemplate);
            Assert.Equal("A1 sheet", file.SheetSet.Template.Name);
            file.Change(set =>
            {
                set.SetCustomProperty("Client", "Acme", CadSheetSetPropertyFlags.Set);
                set.ProjectName = "Tower";
            });

            CadSheetSetSheet sheet = null;
            file.Change(set => sheet = set.Subsets.Single(s => s.Name == "Plans").NewSheet("A-101", "Ground floor"));

            string drawing = sheet.Layout.File.Resolve(file.Folder);
            Assert.Equal(Path.Combine(Path.GetDirectoryName(dst), "A-101 Ground floor.dwg"), drawing, ignoreCase: true);
            CadDocument opened = CadDocument.Open(drawing);
            DxfBlockReference insert = opened
                .Database.Layouts.Single(l => l.LayoutName == "A-101 Ground floor")
                .BlockTableRecord.OfType<DxfBlockReference>()
                .Single();
            var words = insert.AttributeCollection.ToDictionary(a => a.Tag, a => a.TextString);
            Assert.Equal("A-101", words["NUMBER"]);
            Assert.Equal("Ground floor", words["TITLE"]);
            Assert.Equal("Tower", words["PROJECT"]);
            Assert.Equal("Acme", words["CLIENT"]);
            Assert.Equal("----", words["DRAWNBY"]);
            Assert.Contains(
                "Component(\"" + sheet.Id + "\").Number",
                CadFields.CodesOf(insert.AttributeCollection.Single(a => a.Tag == "NUMBER"))
            );
        }

        [Fact]
        public void ASetFromTheMetricExample_CanBeMovedWithItsExamples_BecauseItNamesItsFilesByRelativePaths()
        {
            string examples = Path.Combine(_folder, "examples");
            CadSheetSetExamples.Write(examples);
            string dst = Path.Combine(_folder, "Moved", "Moved.dst");
            Directory.CreateDirectory(Path.GetDirectoryName(dst));

            CadSheetSetFile file = CadSheetSetFile.CreateFromExample(
                dst,
                "Moved",
                null,
                Path.Combine(examples, "OpenCAD2D Metric Sheet Set.dst"),
                true
            );

            Assert.Equal(
                Path.Combine(examples, CadSheetSetExamples.TemplateFileName),
                file.SheetSet.Template.File.Resolve(file.Folder),
                ignoreCase: true
            );
            Assert.True(Directory.Exists(Path.Combine(Path.GetDirectoryName(dst), "Plans")));
            Assert.Equal(
                Path.Combine(Path.GetDirectoryName(dst), "Plans"),
                file.SheetSet.Subsets.Single(s => s.Name == "Plans").NewSheetLocation.Resolve(file.Folder),
                ignoreCase: true
            );
        }
    }
}
