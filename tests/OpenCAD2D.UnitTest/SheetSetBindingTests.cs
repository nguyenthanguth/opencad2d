using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using GeometryHelper.Dxf;
using GeometryHelper.Geometry;
using OpenCAD2D.SheetSets;
using Xunit;

namespace OpenCAD2D.UnitTest
{
    /// <summary>
    /// New Sheet and Import Layouts as Sheets bind the <c>\AcSm</c> fields of the new sheet to the sheet once the set is saved
    /// (A12 3d, D12), and the fields read the sheet set mark through <see cref="CadSheetSetHint"/>, the one reader of it. Every set
    /// and drawing is built in code in a temporary folder.
    /// </summary>
    public sealed class SheetSetBindingTests : IDisposable
    {
        private readonly string _folder = Path.Combine(
            Path.GetTempPath(),
            "gk",
            "sheetsetbind-" + Guid.NewGuid().ToString("N")
        );

        public SheetSetBindingTests()
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

        private string SetFolder => Path.Combine(_folder, "set");

        private CadSheetSetFile NewSet(out string template)
        {
            Directory.CreateDirectory(SetFolder);
            CadSheetSetFile file = CadSheetSetFile.Create(Path.Combine(SetFolder, "Tower.dst"), "Tower", null);
            template = Path.Combine(_folder, "sheets.dwt");
            CadDocument
                .From(CadSheetSetExamples.CreateTemplate(), "sheets.dwt")
                .SaveAsTemplate(template, "test", true, null);
            string dwt = template;
            file.Change(set =>
            {
                set.Template = new CadDrawingReference(
                    CadFileReference.FromPath(dwt, file.Folder),
                    CadSheetSetExamples.TemplateLayout,
                    null,
                    null
                );
                set.AddCustomProperty("Client", "Acme", CadSheetSetPropertyFlags.Set);
                set.AddCustomProperty("Drawn By", "TN", CadSheetSetPropertyFlags.Sheet);
            });
            return file;
        }

        private static DxfBlockReference TitleBlock(string drawing, string layout) =>
            CadDocument
                .Open(drawing)
                .Database.Layouts.Single(l => l.LayoutName == layout)
                .BlockTableRecord.OfType<DxfBlockReference>()
                .Single();

        [Fact]
        public void ANewSheet_HasTheFieldsOfItsTitleBlockBoundToItself_AndShowingItsWords()
        {
            CadSheetSetFile file = NewSet(out _);
            CadSheetSetSheet sheet = null;

            file.Change(set => sheet = set.NewSheet("A-101", "Ground floor"));

            string drawing = sheet.Layout.File.Resolve(file.Folder);
            DxfBlockReference block = TitleBlock(drawing, "A-101 Ground floor");
            string head = "\\AcSm Database(\"" + file.Path + "\").SheetSet(\"" + file.SheetSet.Id + "\")";
            DxfAttributeReference number = block.AttributeCollection.Single(a => a.Tag == "NUMBER");
            Assert.Equal("%<" + head + ".Component(\"" + sheet.Id + "\").Number>%", CadFields.CodesOf(number));
            Assert.Equal(
                "%<" + head + ".Client>%",
                CadFields.CodesOf(block.AttributeCollection.Single(a => a.Tag == "CLIENT"))
            );
            Assert.Equal("A-101", number.TextString);
            Assert.Equal("Ground floor", block.AttributeCollection.Single(a => a.Tag == "TITLE").TextString);
            Assert.Equal("Acme", block.AttributeCollection.Single(a => a.Tag == "CLIENT").TextString);
            Assert.Equal("TN", block.AttributeCollection.Single(a => a.Tag == "DRAWNBY").TextString);
            Assert.Equal("Tower", block.AttributeCollection.Single(a => a.Tag == "PROJECT").TextString);
        }

        [Fact]
        public void ARenumberedSheet_ShowsTheNewNumber_WhenItsDrawingIsOpenedAndTheFieldsUpdate()
        {
            CadSheetSetFile file = NewSet(out _);
            CadSheetSetSheet sheet = null;
            file.Change(set => sheet = set.NewSheet("A-101", "Ground floor"));
            string drawing = sheet.Layout.File.Resolve(file.Folder);

            file.Change(set => ((CadSheetSetSheet)set.Find(sheet.Id)).Number = "A-202");

            CadDocument opened = CadDocument.Open(drawing);
            opened.UpdateFields(CadFieldContexts.Demand);
            DxfBlockReference block = opened
                .Database.Layouts.Single(l => l.LayoutName == "A-101 Ground floor")
                .BlockTableRecord.OfType<DxfBlockReference>()
                .Single();
            Assert.Equal("A-202", block.AttributeCollection.Single(a => a.Tag == "NUMBER").TextString);
        }

        [Fact]
        public void ANewSheetInASubset_BindsTheSubsetFieldToTheSubset()
        {
            CadSheetSetFile file = NewSet(out string dwt);
            CadSheetSetSheet sheet = null;
            string code = "%<\\AcSm Subset.Name>%";
            CadDocument template = CadDocument.Open(dwt);
            DxfLayout a1 = template.Database.Layouts.Single(l => l.LayoutName == "A1 sheet");
            DxfBlockReference block = a1.BlockTableRecord.OfType<DxfBlockReference>().Single();
            CadFields.Set(
                block.AttributeCollection.Single(a => a.Tag == "CHECKEDBY"),
                code,
                new CadFieldContext(template)
            );
            template.SaveAs(dwt);

            file.Change(set => sheet = set.AddSubset("Plans").NewSheet("A-101", "Ground floor"));

            DxfBlockReference made = TitleBlock(sheet.Layout.File.Resolve(file.Folder), "A-101 Ground floor");
            DxfAttributeReference attribute = made.AttributeCollection.Single(a => a.Tag == "CHECKEDBY");
            Assert.Equal("Plans", attribute.TextString);
            Assert.Contains("Component(\"" + sheet.Parent.Id + "\").Name", CadFields.CodesOf(attribute));
        }

        [Fact]
        public void AnImportedLayout_HasItsFieldsBoundToItsSheet_AndTheRestOfTheLayoutIsLeftAlone()
        {
            CadSheetSetFile file = NewSet(out string dwt);
            string drawing = Path.Combine(_folder, "plan.dwg");
            DxfDatabase database = SheetSetTestDrawings.Build("Plan");
            DxfLayout layout = database.Layouts.Single(l => l.LayoutName == "Plan");
            layout.Initialize();
            var block = new DxfBlockTableRecord { Name = "TB" };
            database.BlockTable.Add(block);
            var definition = new DxfAttributeDefinition(new GeoPoint3(0, 0, 0), "x", "N", "N", null);
            block.AppendEntity(definition);
            var insert = new DxfBlockReference(new GeoPoint3(0, 0, 0), block);
            layout.BlockTableRecord.AppendEntity(insert);
            var attribute = new DxfAttributeReference();
            attribute.SetAttributeFromBlock(definition, insert.BlockTransform);
            insert.AttributeCollection.AppendAttribute(attribute);
            CadDocument source = CadDocument.From(database, "plan.dwg");
            CadFields.Set(attribute, "%<\\AcSm Sheet.Title>%", new CadFieldContext(source));
            source.SaveAs(drawing);
            int lines = SheetSetTestDrawings.Count<DxfLine>(drawing, "Plan");
            CadSheetSetSheet sheet = null;

            file.Change(set => sheet = set.ImportLayouts(drawing, new[] { "Plan" }, false).Single());

            DxfBlockReference imported = TitleBlock(drawing, "Plan");
            DxfAttributeReference bound = imported.AttributeCollection.Single();
            Assert.Contains("Component(\"" + sheet.Id + "\").Title", CadFields.CodesOf(bound));
            Assert.Equal("Plan", bound.TextString);
            Assert.Equal(lines, SheetSetTestDrawings.Count<DxfLine>(drawing, "Plan"));
            Assert.Equal(1, SheetSetTestDrawings.ModelCount(drawing));
            Assert.NotNull(CadSheetSetHint.Read(drawing));
        }

        [Fact]
        public void AnEditThatThrows_BindsNothing()
        {
            CadSheetSetFile file = NewSet(out _);
            CadSheetSetSheet sheet = null;

            Assert.Throws<InvalidOperationException>(() =>
                file.Change(set =>
                {
                    sheet = set.NewSheet("A-101", "Ground floor");
                    throw new InvalidOperationException("stop");
                })
            );

            string drawing = Path.Combine(SetFolder, "A-101 Ground floor.dwg");
            Assert.Equal(
                "%<\\AcSm Sheet.Number>%",
                CadFields.CodesOf(
                    TitleBlock(drawing, "A-101 Ground floor").AttributeCollection.Single(a => a.Tag == "NUMBER")
                )
            );
            Assert.Empty(CadSheetSetFile.Open(file.Path).SheetSet.AllSheets());
        }

        [Fact]
        public void TheFieldsAndTheOperations_ReadTheMarkWithOneReader_TheOtherIsGone()
        {
            Assert.Null(typeof(CadSheetSetHint).Assembly.GetType("OpenCAD2D.SheetSets.CadSheetSetFieldLink"));
            CadSheetSetFile file = NewSet(out _);
            CadSheetSetSheet sheet = null;
            file.Change(set => sheet = set.NewSheet("A-101", "Ground floor"));

            CadSheetSetHint hint = CadSheetSetHint.Read(sheet.Layout.File.Resolve(file.Folder));

            Assert.Equal(file.Path, hint.SheetSetFileName, ignoreCase: true);
            Assert.Equal("A-101 Ground floor", hint.LayoutName);
            Assert.Equal(sheet.Layout.Handle.ToLowerInvariant(), hint.LayoutHandle);
        }
    }
}
