using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using GeometryHelper.Dxf;
using GeometryHelper.Geometry;
using OpenCAD2D.SheetSets;
using Xunit;

namespace OpenCAD2D.UnitTest
{
    /// <summary>
    /// The <c>\AcSm</c> fields of a sheet set as AutoCAD 2021 evaluates them. The expected words are those AutoCAD gave for the
    /// same codes in its console (a sheet A-101 "Ground floor" in the subset Plans: "A-101", "A-101 - Ground floor", "----" for
    /// a property with no value, "####" for a name it does not know, the placeholder's own name for <c>?Sheet.Number</c>), and
    /// the FIELD objects carry the data AutoCAD keeps. Every set and every sheet is built in code in a temporary folder.
    /// </summary>
    public sealed class SheetSetFieldTests : IDisposable
    {
        private readonly string _folder = Path.Combine(
            Path.GetTempPath(),
            "gk",
            "sheetsetfields-" + Guid.NewGuid().ToString("N")
        );

        public SheetSetFieldTests()
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

        private static CadFieldContext Context(CadDocument document, int reason = CadFieldContexts.Demand) =>
            new CadFieldContext(document, reason) { Culture = CultureInfo.InvariantCulture };

        /// <summary>A sheet drawing: its file, the title block's insert in Layout1 and the words of each attribute by tag.</summary>
        private sealed class Sheet
        {
            internal string Path;
            internal CadDocument Document;
            internal DxfBlockReference Insert;
            internal DxfLayout Layout;

            internal DxfAttributeReference Attribute(string tag) => Insert.AttributeCollection.First(a => a.Tag == tag);

            internal string Words(string tag) => Attribute(tag).TextString;
        }

        private CadSheetSetFile NewSet()
        {
            Directory.CreateDirectory(Path.Combine(_folder, "set"));
            return CadSheetSetFile.Create(Path.Combine(_folder, "set", "Tower.dst"), "Tower", "A tower", Counter());
        }

        private static Func<Guid> Counter() => SheetSetFileTests.Counter();

        /// <summary>
        /// Makes a sheet drawing in the set's folder holding one title block in Layout1 with an attribute for each tag, the
        /// sheet of the set in the subset (null for the set itself), its link to the set written as a drawing carries it, and
        /// a field in each attribute. The attributes are given as tag, field code pairs.
        /// </summary>
        private Sheet AddSheet(
            CadSheetSetFile file,
            CadSheetSubset subset,
            string number,
            string title,
            string description,
            params string[] attributes
        )
        {
            Directory.CreateDirectory(Path.GetDirectoryName(file.Path));
            string path = Path.Combine(file.Folder, number + ".dxf");
            var database = new DxfDatabase("AC1032");
            DxfLayout layout = database.Layouts.First(l => l.LayoutName == "Layout1");
            layout.Initialize();
            var block = new DxfBlockTableRecord { Name = "TITLEBLOCK" };
            database.BlockTable.Add(block);
            var definitions = new List<DxfAttributeDefinition>();

            for (int i = 0; i < attributes.Length; i += 2)
            {
                var definition = new DxfAttributeDefinition(
                    new GeoPoint3(0, -10 * i, 0),
                    "x",
                    attributes[i],
                    attributes[i],
                    null
                );
                block.AppendEntity(definition);
                definitions.Add(definition);
            }

            var insert = new DxfBlockReference(new GeoPoint3(0, 0, 0), block);
            layout.BlockTableRecord.AppendEntity(insert);

            foreach (DxfAttributeDefinition definition in definitions)
            {
                var attribute = new DxfAttributeReference();
                attribute.SetAttributeFromBlock(definition, insert.BlockTransform);
                insert.AttributeCollection.AppendAttribute(attribute);
            }

            CadDocument document = CadDocument.From(database, path);
            var sheet = new Sheet
            {
                Path = path,
                Document = document,
                Insert = insert,
                Layout = layout,
            };
            file.Change(set =>
            {
                CadSheetSubset parent = subset == null ? set : (CadSheetSubset)set.Find(subset.Id);
                CadSheetSetSheet made = parent.AddSheet(
                    number,
                    title,
                    new CadDrawingReference(
                        CadFileReference.FromPath(path, file.Folder),
                        "Layout1",
                        layout.Handle,
                        layout.BlockTableRecord.Handle
                    )
                );
                made.Description = description;
            });
            WriteLink(database, file.Path, path, "Layout1", layout.Handle);

            for (int i = 0; i < attributes.Length; i += 2)
            {
                CadFields.Set(sheet.Attribute(attributes[i]), attributes[i + 1], Context(document));
            }

            document.SaveAs(path);
            return sheet;
        }

        /// <summary>Writes the link a sheet's drawing carries to its set, as AutoCAD does: the dictionary AcSheetSetData of xrecords.</summary>
        private static void WriteLink(DxfDatabase database, string dst, string dwg, string layout, string handle)
        {
            var link = new DxfDBDictionary();
            database.NamedObjectsDictionary.SetAt("AcSheetSetData", link);
            link.SetAt("ShSetFileName", new DxfXrecord { Data = new[] { new DxfTypedValue(1, dst) } });
            link.SetAt("SheetDwgName", new DxfXrecord { Data = new[] { new DxfTypedValue(1, dwg) } });
            link.SetAt("LayoutName", new DxfXrecord { Data = new[] { new DxfTypedValue(1, layout) } });
            link.SetAt(
                "LayoutHandle",
                new DxfXrecord { Data = new[] { new DxfTypedValue(1, handle.ToLowerInvariant()) } }
            );
            link.SetAt("ShSetVersion", new DxfXrecord { Data = new[] { new DxfTypedValue(90, "2") } });
            link.SetAt("UpdateCount", new DxfXrecord { Data = new[] { new DxfTypedValue(90, "1") } });
            link.SetAt(
                "UpdateTime",
                new DxfXrecord { Data = new[] { new DxfTypedValue(1, "2026/11/26 10:00:00.000") } }
            );
        }

        /// <summary>The set of the tests: the subset Plans with A-101 "Ground floor", and G-001 "Cover" in the set itself, the custom properties Client (the set's), Drawn By and Checked By (every sheet's).</summary>
        private CadSheetSetFile Tower(out CadSheetSubset plans)
        {
            CadSheetSetFile file = NewSet();
            CadSheetSubset made = null;
            file.Change(set =>
            {
                made = set.AddSubset("Plans", "The plans");
                set.SetCustomProperty("Client", "Acme", CadSheetSetPropertyFlags.Set);
                set.SetCustomProperty("Drawn By", "DEF", CadSheetSetPropertyFlags.Sheet);
                set.SetCustomProperty("Checked By", "", CadSheetSetPropertyFlags.Sheet);
                set.ProjectName = "Tower project";
            });
            plans = made;
            return file;
        }

        private static string Shown(DxfEntity text) => CadFields.Words(text);

        /// <summary>
        /// What a drawing's FIELD object of AcSm keeps by name, as its file writes it: the names in the order written (group 6),
        /// each with the text that follows it (group 1); the field is the one whose code (group 2, group 3 before it) holds a text.
        /// </summary>
        private static List<KeyValuePair<string, string>> KeptData(CadDocument document, string codeHolds)
        {
            string[] lines = document.Database.ToText().Split('\n');
            var found = new List<KeyValuePair<string, string>>();

            for (int i = 0; i + 1 < lines.Length; i += 2)
            {
                if (lines[i].Trim() != "0" || lines[i + 1].Trim() != "FIELD")
                {
                    continue;
                }

                int end = i + 2;
                var groups = new List<KeyValuePair<string, string>>();

                while (end + 1 < lines.Length && lines[end].Trim() != "0")
                {
                    groups.Add(new KeyValuePair<string, string>(lines[end].Trim(), lines[end + 1].TrimEnd('\r')));
                    end += 2;
                }

                string code = string.Concat(groups.Where(g => g.Key == "3" || g.Key == "2").Select(g => g.Value));

                if (!groups.Any(g => g.Key == "1" && g.Value == "AcSm") || !code.Contains(codeHolds))
                {
                    continue;
                }

                var data = new List<KeyValuePair<string, string>>();

                for (int g = 0; g < groups.Count; g++)
                {
                    if (groups[g].Key == "6" && groups[g].Value != "ACFD_FIELDTEXT_CHECKSUM")
                    {
                        string text = groups
                            .Skip(g + 1)
                            .TakeWhile(x => x.Key != "6")
                            .FirstOrDefault(x => x.Key == "1")
                            .Value;
                        data.Add(new KeyValuePair<string, string>(groups[g].Value, text));
                    }
                }

                return data;
            }

            return found;
        }

        // ---- Unbound: the sheet of the layout the field sits on ----

        [Theory]
        [InlineData("\\AcSm Sheet.Number", "A-101")]
        [InlineData("\\AcSm Sheet.Title", "Ground floor")]
        [InlineData("\\AcSm Sheet.NumberAndTitle", "A-101 - Ground floor")]
        [InlineData("\\AcSm Sheet.Description", "The ground floor plan")]
        [InlineData("\\AcSm Sheet.Name", "A-101 Ground floor")]
        [InlineData("\\AcSm Subset.Name", "Plans")]
        [InlineData("\\AcSm Subset.Description", "The plans")]
        [InlineData("\\AcSm SheetSet.Name", "Tower")]
        [InlineData("\\AcSm SheetSet.Description", "A tower")]
        [InlineData("\\AcSm SheetSet.ProjectName", "Tower project")]
        [InlineData("\\AcSm SheetSet.Client", "Acme")]
        [InlineData("\\AcSm Sheet.Drawn By", "DEF")]
        public void ASheetsFields_ShowWhatTheSetHoldsOfItsSheetItsSubsetAndTheSet(string code, string expected)
        {
            CadSheetSetFile file = Tower(out CadSheetSubset plans);
            Sheet sheet = AddSheet(
                file,
                plans,
                "A-101",
                "Ground floor",
                "The ground floor plan",
                "F",
                "%<" + code + ">%"
            );

            Assert.Equal(expected, sheet.Words("F"));
        }

        [Theory]
        [InlineData("\\AcSm Sheet.Category")]
        [InlineData("\\AcSm Sheet.RevisionNumber")]
        [InlineData("\\AcSm Sheet.RevisionDate")]
        [InlineData("\\AcSm Sheet.IssuePurpose")]
        [InlineData("\\AcSm SheetSet.ProjectNumber")]
        [InlineData("\\AcSm SheetSet.ProjectPhase")]
        [InlineData("\\AcSm SheetSet.ProjectMilestone")]
        [InlineData("\\AcSm Sheet.Checked By")]
        public void APropertyWithNoValue_ShowsFourHyphens_AsAutoCadShowsIt(string code)
        {
            CadSheetSetFile file = Tower(out CadSheetSubset plans);
            Sheet sheet = AddSheet(file, plans, "A-101", "Ground floor", "", "F", "%<" + code + ">%");

            Assert.Equal("----", sheet.Words("F"));
        }

        [Theory]
        [InlineData("\\AcSm Sheet.DrawnBy")]
        [InlineData("\\AcSm Sheet.SubsetName")]
        [InlineData("\\AcSm Sheet.Nothing")]
        [InlineData("\\AcSm SheetSet.Nothing")]
        [InlineData("\\AcSm Subset.Number")]
        [InlineData("\\AcSm Sheet.Client")]
        [InlineData("\\AcSm SheetSet.Drawn By")]
        [InlineData("\\AcSm CurrentSheetNumber")]
        [InlineData("\\AcVar CurrentSheetNumber")]
        public void ANameTheSetDoesNotKnow_ShowsFourHashes_AsAutoCadShowsIt(string code)
        {
            CadSheetSetFile file = Tower(out CadSheetSubset plans);
            Sheet sheet = AddSheet(file, plans, "A-101", "Ground floor", "", "F", "%<" + code + ">%");

            Assert.Equal("####", sheet.Words("F"));
        }

        [Theory]
        [InlineData("\\AcSm sheet.number")]
        [InlineData("\\AcSm SHEET.NUMBER")]
        [InlineData("\\AcSm Sheet.number")]
        public void TheComponentAndTheBuiltInPropertyAreNamedWithAnyLetterCase(string code)
        {
            CadSheetSetFile file = Tower(out CadSheetSubset plans);
            Sheet sheet = AddSheet(file, plans, "A-101", "Ground floor", "", "F", "%<" + code + ">%");

            Assert.Equal("A-101", sheet.Words("F"));
        }

        [Fact]
        public void ACustomPropertyIsNamedWithAnyLetterCase_ASheetsOwnValueBeatsTheDefault()
        {
            CadSheetSetFile file = Tower(out CadSheetSubset plans);
            Sheet own = AddSheet(file, plans, "A-101", "Ground floor", "", "F", "%<\\AcSm Sheet.drawn by>%");
            Sheet other = AddSheet(file, plans, "A-102", "First floor", "", "F", "%<\\AcSm SheetSet.CLIENT>%");
            file.Change(set =>
            {
                ((CadSheetSetSheet)set.Find(set.AllSheets().First(s => s.Number == "A-101").Id)).SetCustomProperty(
                    "Drawn By",
                    "TN",
                    CadSheetSetPropertyFlags.Sheet
                );
            });

            own.Document.UpdateFields(CadFieldContexts.Open);
            other.Document.UpdateFields(CadFieldContexts.Open);

            Assert.Equal("TN", own.Words("F"));
            Assert.Equal("Acme", other.Words("F"));
        }

        [Fact]
        public void ASheetInTheSetItself_HasNoSubset_AndItsSubsetFieldShowsFourHashes()
        {
            CadSheetSetFile file = Tower(out _);
            Sheet cover = AddSheet(
                file,
                null,
                "G-001",
                "Cover",
                "",
                "F",
                "%<\\AcSm Subset.Name>%",
                "N",
                "%<\\AcSm Sheet.NumberAndTitle>%"
            );

            Assert.Equal("####", cover.Words("F"));
            Assert.Equal("G-001 - Cover", cover.Words("N"));
        }

        [Fact]
        public void NumberAndTitle_IsJustTheTitleWithNoNumber_AndTheNumberAndAHyphenWithNoTitle()
        {
            CadSheetSetFile file = Tower(out CadSheetSubset plans);
            Sheet untitled = AddSheet(
                file,
                plans,
                "T-1",
                "",
                "",
                "N",
                "%<\\AcSm Sheet.NumberAndTitle>%",
                "T",
                "%<\\AcSm Sheet.Title>%"
            );
            Sheet unnumbered = AddSheet(
                file,
                plans,
                "U",
                "No number sheet",
                "",
                "N",
                "%<\\AcSm Sheet.NumberAndTitle>%",
                "M",
                "%<\\AcSm Sheet.Number>%"
            );
            file.Change(set =>
            {
                set.AllSheets().First(s => s.Number == "U").Number = "";
            });
            unnumbered.Document.UpdateFields(CadFieldContexts.Open);

            Assert.Equal("T-1 - ", untitled.Words("N"));
            Assert.Equal("----", untitled.Words("T"));
            Assert.Equal("No number sheet", unnumbered.Words("N"));
            Assert.Equal("----", unnumbered.Words("M"));
        }

        [Theory]
        [InlineData("%tc1", "GROUND FLOOR")]
        [InlineData("%tc2", "ground floor")]
        [InlineData("%tc3", "Ground floor")]
        [InlineData("%tc4", "Ground Floor")]
        public void TheTextCaseFormat_ChangesTheCaseOfWhatTheFieldShows(string format, string expected)
        {
            CadSheetSetFile file = Tower(out CadSheetSubset plans);
            Sheet sheet = AddSheet(
                file,
                plans,
                "A-101",
                "Ground floor",
                "",
                "F",
                "%<\\AcSm Sheet.Title \\f \"" + format + "\">%"
            );

            Assert.Equal(expected, sheet.Words("F"));
        }

        [Fact]
        public void ARenumberedSheet_ShowsItsNewNumber_OnceTheFieldsAreUpdated()
        {
            CadSheetSetFile file = Tower(out CadSheetSubset plans);
            Sheet sheet = AddSheet(
                file,
                plans,
                "A-101",
                "Ground floor",
                "",
                "NUMBER",
                "%<\\AcSm Sheet.Number \\f \"%tc1\">%",
                "BOTH",
                "%<\\AcSm Sheet.NumberAndTitle>%"
            );
            Assert.Equal("A-101", sheet.Words("NUMBER"));

            file.Change(set => set.AllSheets().Single().Number = "a-02");

            Assert.Equal("A-101", sheet.Words("NUMBER"));
            sheet.Document.UpdateFields(CadFieldContexts.Regen);
            Assert.Equal("A-02", sheet.Words("NUMBER"));
            Assert.Equal("a-02 - Ground floor", sheet.Words("BOTH"));
        }

        [Fact]
        public void ACustomSetPropertyChanged_ReachesTheFieldOfEverySheet()
        {
            CadSheetSetFile file = Tower(out CadSheetSubset plans);
            Sheet first = AddSheet(file, plans, "A-101", "Ground floor", "", "C", "%<\\AcSm SheetSet.Client>%");
            Sheet second = AddSheet(file, null, "G-001", "Cover", "", "C", "%<\\AcSm SheetSet.Client>%");

            file.Change(set => set.SetCustomProperty("Client", "Beta Ltd", CadSheetSetPropertyFlags.Set));
            first.Document.UpdateFields(CadFieldContexts.Open);
            second.Document.UpdateFields(CadFieldContexts.Open);

            Assert.Equal("Beta Ltd", first.Words("C"));
            Assert.Equal("Beta Ltd", second.Words("C"));
        }

        [Fact]
        public void TheLayoutIsFoundByItsHandle_ThenByItsName_InTheDrawingsOwnFile()
        {
            CadSheetSetFile file = Tower(out CadSheetSubset plans);
            Sheet sheet = AddSheet(file, plans, "A-101", "Ground floor", "", "F", "%<\\AcSm Sheet.Number>%");
            // The layout is told apart by its name when the handle the set holds is of no layout.
            file.Change(set =>
            {
                CadSheetSetSheet one = set.AllSheets().Single();
                one.Layout = new CadDrawingReference(one.Layout.File, "Layout1", "7F7F", one.Layout.OwnerHandle);
                one.Number = "B-7";
            });

            sheet.Document.UpdateFields(CadFieldContexts.Open);

            Assert.Equal("B-7", sheet.Words("F"));
        }

        [Fact]
        public void AnAttributesField_IsEvaluatedAgainWhenTheFieldsOfTheDrawingAre_AsATextsIs()
        {
            CadSheetSetFile file = Tower(out CadSheetSubset plans);
            Sheet sheet = AddSheet(file, plans, "A-101", "Ground floor", "", "BY", "%<\\AcVar Author>%");
            sheet.Document.Database.SummaryInfo = new DxfDatabaseSummaryInfoBuilder
            {
                Author = "Jane Author",
            }.ToDatabaseSummaryInfo();

            Assert.Equal(1, sheet.Document.UpdateFields(CadFieldContexts.Regen));

            Assert.Equal("Jane Author", sheet.Words("BY"));
        }

        // ---- The Field dialog: the SheetSet category ----

        /// <summary>The Field dialog of a sheet's drawing, Layout1 shown, as FIELD makes it.</summary>
        private CadFieldRequest Dialog(Sheet sheet)
        {
            sheet.Document.CurrentSpace = sheet.Document.Spaces.Single(s => s.Name == "Layout1");
            return new CadFieldRequest(Context(sheet.Document));
        }

        private static CadFieldName Name(string name) =>
            CadFieldRequest.NamesOf("SheetSet").Single(n => n.Name == name);

        private static string Code(string body) => "%<\\AcSm " + body + ">%";

        private static string BoundHead(string path, string set) =>
            "\\AcSm Database(\"" + path + "\").SheetSet(\"" + set + "\")";

        [Fact]
        public void TheSheetSetCategory_IsListedLast_WithSheetSetAndItsPlaceholderFirstThenTheCurrentSheetNames()
        {
            Assert.Equal("SheetSet", CadFieldRequest.CategoryNames.Last());
            Assert.Equal(
                new[]
                {
                    "SheetSet",
                    "SheetSetPlaceholder",
                    "CurrentSheetCategory",
                    "CurrentSheetCustom",
                    "CurrentSheetDescription",
                    "CurrentSheetIssuePurpose",
                    "CurrentSheetNumber",
                    "CurrentSheetNumberAndTitle",
                    "CurrentSheetRevisionDate",
                    "CurrentSheetRevisionNumber",
                    "CurrentSheetSet",
                    "CurrentSheetSetCustom",
                    "CurrentSheetSetDescription",
                    "CurrentSheetSetProjectMilestone",
                    "CurrentSheetSetProjectName",
                    "CurrentSheetSetProjectNumber",
                    "CurrentSheetSetProjectPhase",
                    "CurrentSheetSubSet",
                    "CurrentSheetTitle",
                },
                CadFieldRequest.NamesOf("SheetSet").Select(n => n.Name)
            );
            Assert.Equal(19, CadFieldRequest.NamesOf("All").Count(n => n.Category == "SheetSet"));
        }

        [Theory]
        [InlineData("CurrentSheetCategory", "Sheet.Category", "----")]
        [InlineData("CurrentSheetDescription", "Sheet.Description", "The ground floor plan")]
        [InlineData("CurrentSheetIssuePurpose", "Sheet.IssuePurpose", "----")]
        [InlineData("CurrentSheetNumber", "Sheet.Number", "A-101")]
        [InlineData("CurrentSheetNumberAndTitle", "Sheet.NumberAndTitle", "A-101 - Ground floor")]
        [InlineData("CurrentSheetRevisionDate", "Sheet.RevisionDate", "----")]
        [InlineData("CurrentSheetRevisionNumber", "Sheet.RevisionNumber", "----")]
        [InlineData("CurrentSheetSet", "SheetSet.Name", "Tower")]
        [InlineData("CurrentSheetSetDescription", "SheetSet.Description", "A tower")]
        [InlineData("CurrentSheetSetProjectMilestone", "SheetSet.ProjectMilestone", "----")]
        [InlineData("CurrentSheetSetProjectName", "SheetSet.ProjectName", "Tower project")]
        [InlineData("CurrentSheetSetProjectNumber", "SheetSet.ProjectNumber", "----")]
        [InlineData("CurrentSheetSetProjectPhase", "SheetSet.ProjectPhase", "----")]
        [InlineData("CurrentSheetSubSet", "Subset.Name", "Plans")]
        [InlineData("CurrentSheetTitle", "Sheet.Title", "Ground floor")]
        public void ACurrentSheetName_WritesTheUnboundCodeAutoCadKeepsInATitleBlock_AndPreviewsWhatItComesTo(
            string name,
            string body,
            string preview
        )
        {
            CadSheetSetFile file = Tower(out CadSheetSubset plans);
            Sheet sheet = AddSheet(file, plans, "A-101", "Ground floor", "The ground floor plan");
            CadFieldRequest request = Dialog(sheet);

            request.SetCategory("SheetSet");
            request.Choose(Name(name));

            Assert.Equal(Code(body), request.Code);
            Assert.Equal(preview, request.Preview);
            request.FormatIndex = 1;
            Assert.Equal(Code(body + " \\f \"%tc1\""), request.Code);
        }

        [Fact]
        public void TheCustomNames_AreThoseTheSetDefinesForTheSheetsOrForTheSet_AndTheCodeNamesTheOneChosen()
        {
            CadSheetSetFile file = Tower(out CadSheetSubset plans);
            Sheet sheet = AddSheet(file, plans, "A-101", "Ground floor", "");
            CadFieldRequest request = Dialog(sheet);

            request.Choose(Name("CurrentSheetCustom"));
            Assert.Equal(new[] { "Checked By", "Drawn By" }, request.CustomNames);
            request.ChooseCustomName("Drawn By");
            Assert.Equal(Code("Sheet.Drawn By"), request.Code);
            Assert.Equal("DEF", request.Preview);

            request.Choose(Name("CurrentSheetSetCustom"));
            Assert.Equal(new[] { "Client" }, request.CustomNames);
            request.ChooseCustomName("Client");
            Assert.Equal(Code("SheetSet.Client"), request.Code);
            Assert.Equal("Acme", request.Preview);
            request.ChooseCustomName(string.Empty);
            Assert.Equal(string.Empty, request.Code);
        }

        [Fact]
        public void TheSheetSetField_ListsTheTreeOfTheSet_ChoosesTheSheetOfTheLayout_AndBindsTheNodeAndPropertyChosen()
        {
            CadSheetSetFile file = Tower(out CadSheetSubset plans);
            AddSheet(file, null, "G-001", "Cover", "");
            Sheet sheet = AddSheet(file, plans, "A-101", "Ground floor", "The ground floor plan");
            CadFieldRequest request = Dialog(sheet);
            request.Choose(Name("SheetSet"));
            string set = file.SheetSet.Id;
            CadSheetSetSheet made = CadSheetSetFile
                .Open(file.Path)
                .SheetSet.AllSheets()
                .Single(s => s.Number == "A-101");
            string head = BoundHead(file.Path, set);

            Assert.Equal("Tower", request.SheetSetTree.Text);
            Assert.Equal(new[] { "Plans", "G-001 - Cover" }, request.SheetSetTree.Children.Select(c => c.Text));
            Assert.Equal(
                new[] { "A-101 - Ground floor" },
                request.SheetSetTree.Children[0].Children.Select(c => c.Text)
            );
            Assert.Equal(made.Id, request.SheetSetNode.Id);
            Assert.Equal(
                new[]
                {
                    "Number",
                    "Title",
                    "NumberAndTitle",
                    "Description",
                    "Category",
                    "RevisionNumber",
                    "RevisionDate",
                    "IssuePurpose",
                    "Checked By",
                    "Drawn By",
                },
                request.SheetSetProperties
            );
            Assert.Equal("Number", request.SheetSetProperty);
            Assert.Equal("%<" + head + ".Component(\"" + made.Id + "\").Number>%", request.Code);
            Assert.Equal("A-101", request.Preview);

            request.ChooseSheetSetProperty("drawn by");
            request.FormatIndex = 3;
            Assert.Equal("DEF", request.Preview);
            Assert.EndsWith(".Drawn By \\f \"%tc3\">%", request.Code);

            request.ChooseSheetSetNode(set);
            Assert.Equal(
                new[]
                {
                    "Name",
                    "Description",
                    "ProjectNumber",
                    "ProjectName",
                    "ProjectPhase",
                    "ProjectMilestone",
                    "Client",
                },
                request.SheetSetProperties
            );
            request.ChooseSheetSetProperty("Client");
            Assert.Equal("%<" + head + ".Client \\f \"%tc3\">%", request.Code);
            Assert.Equal("Acme", request.Preview);

            request.ChooseSheetSetNode(plans.Id);
            Assert.Equal(new[] { "Name", "Description" }, request.SheetSetProperties);
            request.ChooseSheetSetProperty("Name");
            Assert.Contains(".Component(\"" + plans.Id + "\").Name", request.Code);
            Assert.Equal("Plans", request.Preview);
        }

        [Fact]
        public void TheSheetSetFieldOfADrawingThatIsNoSheet_HasNoTree_AndNoCodeUntilASetIsGiven()
        {
            CadSheetSetFile file = Tower(out CadSheetSubset plans);
            AddSheet(file, plans, "A-101", "Ground floor", "");
            var database = new DxfDatabase("AC1032");
            var request = new CadFieldRequest(Context(CadDocument.From(database, Path.Combine(_folder, "plain.dxf"))));
            request.Choose(Name("SheetSet"));

            Assert.Null(request.SheetSetTree);
            Assert.Equal(string.Empty, request.Code);

            request.SetSheetSet(CadSheetSetFile.Open(file.Path), file.SheetSet.AllSheets().Single().Id);

            Assert.Equal("Tower", request.SheetSetTree.Text);
            Assert.Contains(".Number", request.Code);
        }

        [Theory]
        [InlineData("SheetNumberAndTitle", "?Sheet.NumberAndTitle")]
        [InlineData("SheetTitle", "?Sheet.Title")]
        [InlineData("SheetNumber", "?Sheet.Number")]
        [InlineData("SheetDescription", "?Sheet.Description")]
        [InlineData("ViewNumberAndTitle", "?View.NumberAndTitle")]
        [InlineData("ViewTitle", "?View.Title")]
        [InlineData("ViewNumber", "?View.Number")]
        [InlineData("ViewportScale", "?View.ViewportScale")]
        public void ThePlaceholderTypes_WriteTheirPlaceholderCode_AndShowTheirOwnName(string type, string body)
        {
            CadSheetSetFile file = Tower(out CadSheetSubset plans);
            Sheet sheet = AddSheet(file, plans, "A-101", "Ground floor", "");
            CadFieldRequest request = Dialog(sheet);
            request.Choose(Name("SheetSetPlaceholder"));

            request.ChoosePlaceholder(type);

            Assert.Equal(9, request.PlaceholderTypes.Count);
            Assert.Equal(Code(body), request.Code);
            Assert.Equal(type, request.Preview);
        }

        [Fact]
        public void TheSheetCustomPlaceholder_NamesACustomPropertyOfTheSheet()
        {
            CadSheetSetFile file = Tower(out CadSheetSubset plans);
            Sheet sheet = AddSheet(file, plans, "A-101", "Ground floor", "");
            CadFieldRequest request = Dialog(sheet);
            request.Choose(Name("SheetSetPlaceholder"));
            request.ChoosePlaceholder("SheetCustom");
            request.ChooseCustomName("Checked By");

            Assert.Equal(Code("?Sheet.Checked By"), request.Code);
            request.ChooseCustomName("Drawn By");
            Assert.Equal(Code("?Sheet.Drawn By"), request.Code);
            Assert.Equal("Drawn By", request.Preview);
        }

        [Theory]
        [InlineData("CurrentSheetNumber", 0)]
        [InlineData("CurrentSheetSetProjectName", 2)]
        [InlineData("CurrentSheetSubSet", 4)]
        public void LoadingTheCodeOfACurrentSheetField_ChoosesItsNameAndItsCase(string name, int format)
        {
            CadSheetSetFile file = Tower(out CadSheetSubset plans);
            Sheet sheet = AddSheet(file, plans, "A-101", "Ground floor", "");
            CadFieldRequest first = Dialog(sheet);
            first.Choose(Name(name));
            first.FormatIndex = format;
            CadFieldRequest again = Dialog(sheet);

            Assert.True(again.Load(first.Code));

            Assert.Equal(name, again.Name.Name);
            Assert.Equal(format, again.FormatIndex);
            Assert.Equal(first.Code, again.Code);
        }

        [Fact]
        public void LoadingABoundCodeAPlaceholderAndACustomCode_ChoosesTheFieldTheyAre()
        {
            CadSheetSetFile file = Tower(out CadSheetSubset plans);
            Sheet sheet = AddSheet(file, plans, "A-101", "Ground floor", "");
            CadFieldRequest source = Dialog(sheet);
            source.Choose(Name("SheetSet"));
            source.ChooseSheetSetProperty("Title");
            string bound = source.Code;
            source.Choose(Name("SheetSetPlaceholder"));
            source.ChoosePlaceholder("ViewTitle");
            string placeholder = source.Code;
            source.Choose(Name("CurrentSheetCustom"));
            source.ChooseCustomName("Drawn By");
            string custom = source.Code;
            var other = new CadFieldRequest(
                Context(CadDocument.From(new DxfDatabase("AC1032"), Path.Combine(_folder, "plain.dxf")))
            );

            Assert.True(other.Load(bound));
            Assert.Equal("SheetSet", other.Name.Name);
            Assert.Equal("Title", other.SheetSetProperty);
            Assert.Equal(bound, other.Code);
            Assert.True(other.Load(placeholder));
            Assert.Equal("SheetSetPlaceholder", other.Name.Name);
            Assert.Equal("ViewTitle", other.PlaceholderType);
            Assert.True(other.Load(custom));
            Assert.Equal("CurrentSheetCustom", other.Name.Name);
            Assert.Equal("Drawn By", other.CustomName);
        }

        // ---- Not a sheet, or no set ----

        [Fact]
        public void ADrawingThatIsNoSheet_ShowsFourHashesForAnUnboundField_AndKeepsItsWordsForAnEvaluatedOne()
        {
            var database = new DxfDatabase("AC1032");
            CadDocument document = CadDocument.From(database, Path.Combine(_folder, "plain.dxf"));
            var text = new DxfDBText
            {
                TextString = "x",
                Position = new GeoPoint3(0, 0, 0),
                Height = 2.5,
            };
            database.ModelSpace.AppendEntity(text);

            CadFields.Set(text, "%<\\AcSm Sheet.Number>%", Context(document));

            Assert.Equal("####", Shown(text));
        }

        [Fact]
        public void AFieldOnModelSpace_OfASheetsDrawing_ShowsFourHashes()
        {
            CadSheetSetFile file = Tower(out CadSheetSubset plans);
            Sheet sheet = AddSheet(file, plans, "A-101", "Ground floor", "", "F", "%<\\AcSm Sheet.Number>%");
            var text = new DxfDBText
            {
                TextString = "x",
                Position = new GeoPoint3(0, 0, 0),
                Height = 2.5,
            };
            sheet.Document.Database.ModelSpace.AppendEntity(text);

            CadFields.Set(text, "%<\\AcSm Sheet.Number>%", Context(sheet.Document));

            Assert.Equal("####", Shown(text));
            Assert.Equal("A-101", sheet.Words("F"));
        }

        [Fact]
        public void WhenTheSetCannotBeReached_AFieldKeepsWhatItShowed_AndANewOneShowsFourHashes()
        {
            CadSheetSetFile file = Tower(out CadSheetSubset plans);
            Sheet sheet = AddSheet(file, plans, "A-101", "Ground floor", "", "OLD", "%<\\AcSm Sheet.Number>%");
            File.Delete(file.Path);

            sheet.Document.UpdateFields(CadFieldContexts.Open);
            var fresh = new DxfAttributeReference();
            fresh.SetAttributeFromBlock(
                sheet.Document.Database.BlockTable["TITLEBLOCK"].OfType<DxfAttributeDefinition>().First(),
                sheet.Insert.BlockTransform
            );
            sheet.Insert.AttributeCollection.AppendAttribute(fresh);
            CadFields.Set(fresh, "%<\\AcSm Sheet.Title>%", Context(sheet.Document));

            Assert.Equal("A-101", sheet.Words("OLD"));
            Assert.Equal("####", Shown(fresh));
        }

        // ---- Placeholders ----

        [Theory]
        [InlineData("\\AcSm ?Sheet.Number", "SheetNumber")]
        [InlineData("\\AcSm ?Sheet.Title", "SheetTitle")]
        [InlineData("\\AcSm ?View.Number", "ViewNumber")]
        [InlineData("\\AcSm ?View.Title", "ViewTitle")]
        [InlineData("\\AcSm ?View.ViewportScale", "ViewportScale")]
        [InlineData("\\AcSm ?Sheet.Description", "SheetDescription")]
        [InlineData("\\AcSm ?Sheet.NumberAndTitle", "SheetNumberAndTitle")]
        [InlineData("\\AcSm ?View.NumberAndTitle", "ViewNumberAndTitle")]
        [InlineData("\\AcSm ?sheet.number", "SheetNumber")]
        [InlineData("\\AcSm ?Sheet.Drawn By", "Drawn By")]
        [InlineData("\\AcSm ?View.Description", "----")]
        [InlineData("\\AcSm ?View.Number \\f \"%tc1\"", "VIEWNUMBER")]
        [InlineData("\\AcSm ?Foo.Number", "####")]
        public void APlaceholder_ShowsItsOwnName_UntilItIsBound(string code, string expected)
        {
            var database = new DxfDatabase("AC1032");
            CadDocument document = CadDocument.From(database, Path.Combine(_folder, "label.dxf"));
            var text = new DxfDBText
            {
                TextString = "x",
                Position = new GeoPoint3(0, 0, 0),
                Height = 2.5,
            };
            database.ModelSpace.AppendEntity(text);

            CadFields.Set(text, "%<" + code + ">%", Context(document));

            Assert.Equal(expected, Shown(text));
        }

        // ---- Bound ----

        private static string Bound(CadSheetSetFile file, string ids, string property) =>
            "%<\\AcSm Database(\"" + file.Path + "\")" + ids + "." + property + ">%";

        [Fact]
        public void ABoundField_ReadsTheComponentItNames_InADrawingThatIsNoSheet()
        {
            CadSheetSetFile file = Tower(out CadSheetSubset plans);
            AddSheet(file, plans, "A-101", "Ground floor", "The ground floor plan");
            CadSheetSetSheet sheet = CadSheetSetFile.Open(file.Path).SheetSet.AllSheets().Single();
            string set = file.SheetSet.Id;
            var database = new DxfDatabase("AC1032");
            CadDocument document = CadDocument.From(database, Path.Combine(_folder, "elsewhere.dxf"));
            var text = new DxfDBText
            {
                TextString = "x",
                Position = new GeoPoint3(0, 0, 0),
                Height = 2.5,
            };
            database.ModelSpace.AppendEntity(text);
            string component = ".SheetSet(\"" + set + "\").Component(\"" + sheet.Id + "\")";

            CadFields.Set(text, Bound(file, component, "NumberAndTitle \\f \"%tc1\""), Context(document));
            string upper = Shown(text);
            CadFields.Set(text, Bound(file, component, "Drawn By"), Context(document));
            string drawn = Shown(text);
            CadFields.Set(text, Bound(file, ".SheetSet(\"" + set + "\")", "Client"), Context(document));
            string client = Shown(text);
            CadFields.Set(
                text,
                Bound(file, ".SheetSet(\"" + set + "\").Component(\"" + set + "\")", "Name"),
                Context(document)
            );
            string name = Shown(text);
            CadFields.Set(
                text,
                Bound(file, ".SheetSet(\"" + set + "\").Component(\"" + plans.Id + "\")", "Name"),
                Context(document)
            );
            string subset = Shown(text);
            CadFields.Set(
                text,
                Bound(file, ".SheetSet(\"" + set + "\").Component(\"gDEAD\")", "Number"),
                Context(document)
            );
            string gone = Shown(text);

            Assert.Equal("A-101 - GROUND FLOOR", upper);
            Assert.Equal("DEF", drawn);
            Assert.Equal("Acme", client);
            Assert.Equal("Tower", name);
            Assert.Equal("Plans", subset);
            Assert.Equal("####", gone);
        }

        [Fact]
        public void ABoundFieldWithAFormatAndAHyperlink_ReadsItsPropertyUpToTheFirstSwitch_AndBindingKeepsTheSwitches()
        {
            CadSheetSetFile file = Tower(out CadSheetSubset plans);
            Sheet sheet = AddSheet(
                file,
                plans,
                "A-101",
                "Ground floor",
                "",
                "L",
                "%<\\AcSm Sheet.Number \\href \"C:\\Tower\\A-101.dwg#,GROUND FLOOR#1\">%"
            );
            CadSheetSetFields.Bind(sheet.Document, "Layout1", file, file.SheetSet.AllSheets().Single());
            string code = CadFields.CodesOf(sheet.Attribute("L"));

            Assert.Equal("A-101", sheet.Words("L"));
            Assert.EndsWith(".Number \\href \"C:\\Tower\\A-101.dwg#,GROUND FLOOR#1\">%", code);
            Assert.Contains(".Component(\"" + file.SheetSet.AllSheets().Single().Id + "\")", code);
        }

        [Fact]
        public void ABoundField_WritesTheDataAutoCadKeeps_AndAnUnboundOneTheNamesOfItsComponentAndProperty()
        {
            CadSheetSetFile file = Tower(out CadSheetSubset plans);
            Sheet sheet = AddSheet(
                file,
                plans,
                "A-101",
                "Ground floor",
                "",
                "U",
                "%<\\AcSm Sheet.Number \\f \"%tc1\">%",
                "B",
                "%<\\AcSm Database(\"" + file.Path + "\").SheetSet(\"" + file.SheetSet.Id + "\").Client>%"
            );

            List<KeyValuePair<string, string>> unbound = KeptData(sheet.Document, "Sheet.Number");
            List<KeyValuePair<string, string>> bound = KeptData(sheet.Document, ".Client");

            Assert.Equal(new[] { "SheetSetCompName", "SheetSetPropertyName" }, unbound.Select(d => d.Key));
            Assert.Equal(new[] { "Sheet", "Number" }, unbound.Select(d => d.Value));
            Assert.Equal(new[] { "SheetSetFile", "SheetSetId", "SheetSetPropertyName" }, bound.Select(d => d.Key));
            Assert.Equal(new[] { file.Path, file.SheetSet.Id, "Client" }, bound.Select(d => d.Value));
        }

        [Fact]
        public void ABoundFieldOfASheet_AlsoKeepsTheSheetsDrawingAndItsDate()
        {
            CadSheetSetFile file = Tower(out CadSheetSubset plans);
            Sheet sheet = AddSheet(file, plans, "A-101", "Ground floor", "", "F", "%<\\AcSm Sheet.Number>%");
            CadSheetSetFields.Bind(sheet.Document, "Layout1", file, file.SheetSet.AllSheets().Single());

            List<KeyValuePair<string, string>> bound = KeptData(sheet.Document, ".Number");

            Assert.Equal(
                new[]
                {
                    "SheetSetCompId",
                    "SheetSetCompName",
                    "SheetSetDwgFile",
                    "SheetSetDwgFileDate",
                    "SheetSetFile",
                    "SheetSetId",
                    "SheetSetPropertyName",
                },
                bound.Select(d => d.Key)
            );
            Assert.Equal("Component", bound[1].Value);
            Assert.Equal(sheet.Path, bound[2].Value);
            Assert.Equal("Number", bound[6].Value);
        }

        [Fact]
        public void ABoundFieldWhoseSetMovedWithItsSheets_IsFoundByTheFoldersItWasWrittenWith()
        {
            CadSheetSetFile file = Tower(out CadSheetSubset plans);
            Sheet sheet = AddSheet(file, plans, "A-101", "Ground floor", "", "F", "%<\\AcSm Sheet.Number>%");
            CadSheetSetFields.Bind(sheet.Document, "Layout1", file, file.SheetSet.AllSheets().Single());
            sheet.Document.SaveAs(sheet.Path);
            string moved = Path.Combine(_folder, "moved");
            Directory.Move(Path.GetDirectoryName(file.Path), moved);
            CadSheetSetFile there = CadSheetSetFile.Open(Path.Combine(moved, "Tower.dst"));
            there.Change(set => set.AllSheets().Single().Number = "A-777");

            CadDocument reopened = CadDocument.Open(Path.Combine(moved, "A-101.dxf"));
            DxfBlockReference insert = reopened
                .Spaces.Single(s => s.Name == "Layout1")
                .Block.OfType<DxfBlockReference>()
                .Single();

            Assert.Equal("A-777", insert.AttributeCollection.Single().TextString);
        }

        [Fact]
        public void AnUnboundFieldWhoseSetMovedWithItsSheets_IsFoundThroughTheDrawingsLink()
        {
            CadSheetSetFile file = Tower(out CadSheetSubset plans);
            AddSheet(file, plans, "A-101", "Ground floor", "", "F", "%<\\AcSm Sheet.Number>%");
            string moved = Path.Combine(_folder, "moved");
            Directory.Move(Path.GetDirectoryName(file.Path), moved);
            CadSheetSetFile there = CadSheetSetFile.Open(Path.Combine(moved, "Tower.dst"));
            there.Change(set => set.AllSheets().Single().Number = "A-888");

            CadDocument reopened = CadDocument.Open(Path.Combine(moved, "A-101.dxf"));
            DxfBlockReference insert = reopened
                .Spaces.Single(s => s.Name == "Layout1")
                .Block.OfType<DxfBlockReference>()
                .Single();

            Assert.Equal("A-888", insert.AttributeCollection.Single().TextString);
        }

        // ---- Binding a new or imported sheet ----

        [Fact]
        public void Bind_RewritesTheUnboundCodesOfTheLayoutToTheBoundFormOfThatSheet()
        {
            CadSheetSetFile file = Tower(out CadSheetSubset plans);
            Sheet sheet = AddSheet(
                file,
                plans,
                "A-101",
                "Ground floor",
                "",
                "N",
                "%<\\AcSm Sheet.Number \\f \"%tc1\">%",
                "T",
                "%<\\AcSm Sheet.Drawn By>%",
                "C",
                "%<\\AcSm SheetSet.Client>%",
                "S",
                "%<\\AcSm Subset.Name>%",
                "P",
                "%<\\AcSm ?View.Number>%",
                "V",
                "%<\\AcVar Date>%"
            );
            CadSheetSetSheet made = file.SheetSet.AllSheets().Single();
            string dst = file.Path;
            string set = file.SheetSet.Id;
            string head = "\\AcSm Database(\"" + dst + "\").SheetSet(\"" + set + "\")";

            int bound = CadSheetSetFields.Bind(sheet.Document, "Layout1", file, made);

            Assert.Equal(4, bound);
            Assert.Equal(
                "%<" + head + ".Component(\"" + made.Id + "\").Number \\f \"%tc1\">%",
                CadFields.CodesOf(sheet.Attribute("N"))
            );
            Assert.Equal(
                "%<" + head + ".Component(\"" + made.Id + "\").Drawn By>%",
                CadFields.CodesOf(sheet.Attribute("T"))
            );
            Assert.Equal("%<" + head + ".Client>%", CadFields.CodesOf(sheet.Attribute("C")));
            Assert.Equal(
                "%<" + head + ".Component(\"" + plans.Id + "\").Name>%",
                CadFields.CodesOf(sheet.Attribute("S"))
            );
            Assert.Equal("%<\\AcSm ?View.Number>%", CadFields.CodesOf(sheet.Attribute("P")));
            Assert.Equal("%<\\AcVar Date>%", CadFields.CodesOf(sheet.Attribute("V")));
            Assert.Equal("A-101", sheet.Words("N"));
            Assert.Equal("DEF", sheet.Words("T"));
            Assert.Equal("Acme", sheet.Words("C"));
            Assert.Equal("Plans", sheet.Words("S"));
        }

        [Fact]
        public void Bind_LeavesTheFieldsOfOtherLayoutsAndOfModelSpaceAsTheyWere()
        {
            CadSheetSetFile file = Tower(out CadSheetSubset plans);
            Sheet sheet = AddSheet(file, plans, "A-101", "Ground floor", "", "N", "%<\\AcSm Sheet.Number>%");
            var model = new DxfDBText
            {
                TextString = "x",
                Position = new GeoPoint3(0, 0, 0),
                Height = 2.5,
            };
            sheet.Document.Database.ModelSpace.AppendEntity(model);
            CadFields.Set(model, "%<\\AcSm Sheet.Number>%", Context(sheet.Document));

            int bound = CadSheetSetFields.Bind(sheet.Document, "Layout2", file, file.SheetSet.AllSheets().Single());
            int one = CadSheetSetFields.Bind(sheet.Document, "Layout1", file, file.SheetSet.AllSheets().Single());

            Assert.Equal(0, bound);
            Assert.Equal(1, one);
            Assert.Equal("%<\\AcSm Sheet.Number>%", CadFields.CodesOf(model));
        }

        [Fact]
        public void Bind_OfADrawingWithNoFields_BindsNothing_AndRefusesANullArgument()
        {
            CadSheetSetFile file = Tower(out CadSheetSubset plans);
            Sheet sheet = AddSheet(file, plans, "A-101", "Ground floor", "");
            CadSheetSetSheet made = file.SheetSet.AllSheets().Single();

            Assert.Equal(0, CadSheetSetFields.Bind(sheet.Document, "Layout1", file, made));
            Assert.Throws<ArgumentNullException>(() => CadSheetSetFields.Bind(null, "Layout1", file, made));
            Assert.Throws<ArgumentNullException>(() => CadSheetSetFields.Bind(sheet.Document, null, file, made));
            Assert.Throws<ArgumentNullException>(() => CadSheetSetFields.Bind(sheet.Document, "Layout1", null, made));
            Assert.Throws<ArgumentNullException>(() => CadSheetSetFields.Bind(sheet.Document, "Layout1", file, null));
        }
    }
}
