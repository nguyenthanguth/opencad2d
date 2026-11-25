using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using GeometryHelper.Dxf;
using GeometryHelper.Geometry;

namespace OpenCAD2D.SheetSets
{
    /// <summary>
    /// The example sheet sets OpenCAD2D ships, the list the Create Sheet Set wizard's first route offers (A12-D19): our own,
    /// made by our writer, never AutoCAD's. They lie in the <c>SheetSetExamples</c> folder beside OpenCAD2D.Core.dll:
    /// <c>New Sheet Set.dst</c>, an empty set; <c>OpenCAD2D Metric Sheet Set.dst</c>, with the subsets General, Plans and
    /// Details, the set properties Client and Project Address, the sheet properties Drawn By and Checked By, and as its sheet
    /// creation template the layout "A1 sheet" of <c>OpenCAD2D-sheets-mm.dwt</c> beside it; and that template, a drawing in
    /// millimetres with the one sheet "A1 sheet" (841 by 594), a frame and a title block whose attributes hold
    /// <c>\AcSm</c> fields (the sheet's number and title, the set's name, client and address, who drew and who checked), which a
    /// new sheet binds to itself.
    /// </summary>
    public static class CadSheetSetExamples
    {
        /// <summary>The name of the folder, beside OpenCAD2D.Core.dll, the examples are shipped in.</summary>
        public const string FolderName = "SheetSetExamples";

        /// <summary>The name of the empty example set, which is also the name of its sheet set.</summary>
        public const string EmptyName = "New Sheet Set";

        /// <summary>The name of the metric example set, which is also the name of its sheet set.</summary>
        public const string MetricName = "OpenCAD2D Metric Sheet Set";

        /// <summary>The file name of the drawing template of the metric example.</summary>
        public const string TemplateFileName = "OpenCAD2D-sheets-mm.dwt";

        /// <summary>The description the template carries, the text Select Template shows under its name.</summary>
        public const string TemplateDescription =
            "OpenCAD2D sheet template: millimetres, the layout A1 sheet with a frame and a title block of sheet set fields.";

        /// <summary>The name of the layout of <see cref="TemplateFileName"/> the metric example makes its sheets from.</summary>
        public const string TemplateLayout = "A1 sheet";

        private const string BlockName = "OpenCAD2D Title Block";
        private const string TitleLayer = "Title block";

        /// <summary>Gets the folder the examples are shipped in, <c>SheetSetExamples</c> beside OpenCAD2D.Core.dll; null where the dll has no place on disk.</summary>
        public static string ShippedFolder
        {
            get
            {
                string program = CadSupportFolders.Default.ProgramFolder;
                return program == null ? null : Path.Combine(program, FolderName);
            }
        }

        /// <summary>
        /// Lists the sheet set data files of a folder and its subfolders, in the order of the walk (<see cref="CadSupportWalk"/>):
        /// the folder's own files by name, letter case ignored and then as written, then those of its subfolders in the same way,
        /// so that the example sets the wizard lists are always in the same order.
        /// </summary>
        /// <param name="folder">The folder.</param>
        /// <returns>The full paths of the .dst files; empty where the folder is not there.</returns>
        public static IReadOnlyList<string> FindIn(string folder)
        {
            try
            {
                if (string.IsNullOrWhiteSpace(folder) || !Directory.Exists(folder))
                {
                    return Array.Empty<string>();
                }

                var found = new List<string>();

                foreach (string each in CadSupportWalk.Folders(folder))
                {
                    found.AddRange(
                        CadSupportWalk
                            .FilesOf(each, name =>
                                string.Equals(Path.GetExtension(name), ".dst", StringComparison.OrdinalIgnoreCase)
                            )
                            .OrderBy(f => Path.GetFileNameWithoutExtension(f), StringComparer.OrdinalIgnoreCase)
                            .ThenBy(f => Path.GetFileNameWithoutExtension(f), StringComparer.Ordinal)
                    );
                }

                return found;
            }
            catch (Exception exception)
                when (exception is IOException
                    || exception is UnauthorizedAccessException
                    || exception is ArgumentException
                    || exception is NotSupportedException
                )
            {
                return Array.Empty<string>();
            }
        }

        /// <summary>Lists the example sets shipped with the program (<see cref="ShippedFolder"/>).</summary>
        /// <returns>The full paths of the .dst files; empty where none is shipped.</returns>
        public static IReadOnlyList<string> Shipped() => FindIn(ShippedFolder);

        /// <summary>
        /// Makes the examples in a folder, as the files that are shipped are made: the template, then the two sets with GUIDs
        /// from a counter, so that the bytes of a set are the same every time. A file of the same name is replaced. The files of the
        /// sets name the template and the folder by relative paths alone, so that the folder can be put anywhere.
        /// </summary>
        /// <param name="folder">The folder; made where it is not there.</param>
        internal static void Write(string folder)
        {
            Directory.CreateDirectory(folder);
            string template = Path.Combine(folder, TemplateFileName);
            CadDocument
                .From(CreateTemplate(), TemplateFileName)
                .SaveAsTemplate(template, TemplateDescription, true, null);
            Func<Guid> guids = Counter();
            CadSheetSetFile empty = Make(folder, EmptyName, null, guids);
            empty.Change(set => set.NewSheetLocation = Relative("."));
            CadSheetSetFile metric = Make(
                folder,
                MetricName,
                "A metric sheet set of A1 sheets with a title block of sheet set fields.",
                guids
            );
            metric.Change(set =>
            {
                set.NewSheetLocation = Relative(".");
                set.PromptForTemplate = false;
                set.Template = new CadDrawingReference(Relative(@".\" + TemplateFileName), TemplateLayout, null, null);
                set.AddSubset("General");
                set.AddSubset("Plans");
                set.AddSubset("Details");
                set.AddCustomProperty("Client", string.Empty, CadSheetSetPropertyFlags.Set);
                set.AddCustomProperty("Project Address", string.Empty, CadSheetSetPropertyFlags.Set);
                set.AddCustomProperty("Drawn By", string.Empty, CadSheetSetPropertyFlags.Sheet);
                set.AddCustomProperty("Checked By", string.Empty, CadSheetSetPropertyFlags.Sheet);
            });
        }

        private static CadSheetSetFile Make(string folder, string name, string description, Func<Guid> guids)
        {
            string path = Path.Combine(folder, name + ".dst");
            File.Delete(path);
            return CadSheetSetFile.Create(path, name, description, guids);
        }

        private static CadFileReference Relative(string path) => new CadFileReference(null, path, null, null);

        private static Func<Guid> Counter()
        {
            int next = 0;
            return () => new Guid(++next, 0, 0, new byte[] { 0, 0, 0, 0, 0, 0, 0, 0xA2 });
        }

        /// <summary>
        /// Makes the drawing of the template: the sample template's layers and styles (<see cref="CadTemplate.CreateSample"/>), and
        /// in place of its one layout A3 the layout "A1 sheet", with its frame 10 in from the paper's edge and the title block,
        /// 180 by 54 at the frame's bottom right corner. It holds one layout because a new sheet takes the layout it is made from
        /// out of a copy of the drawing from which the others are deleted, and deleting the layout that holds the paper space
        /// block moves another layout's entities over by cloning them, which leaves the fields of their attributes behind.
        /// </summary>
        /// <returns>The drawing.</returns>
        internal static DxfDatabase CreateTemplate()
        {
            DxfDatabase database = CadTemplate.CreateSample();
            DxfLayout a1 = database.Layouts.First(l => l.LayoutName == "A3");
            a1.LayoutName = TemplateLayout;
            a1.CanonicalMediaName = "ISO_A1_(841.00_x_594.00_MM)";
            a1.PlotPaperUnits = DxfPlotPaperUnit.Millimeters;
            a1.PlotType = DxfPlotType.Layout;
            DxfBlockTableRecord block = MakeTitleBlock(database);
            Place(CadDocument.From(database, TemplateFileName), a1, block, 841.0, 594.0);
            return database;
        }

        private static DxfBlockTableRecord MakeTitleBlock(DxfDatabase database)
        {
            var block = new DxfBlockTableRecord { Name = BlockName };
            database.BlockTable.Add(block);
            var frame = new DxfPolyline();
            GeoPoint2[] corners =
            {
                new GeoPoint2(0, 0),
                new GeoPoint2(180, 0),
                new GeoPoint2(180, 54),
                new GeoPoint2(0, 54),
            };

            for (int i = 0; i < corners.Length; i++)
            {
                frame.AddVertexAt(i, corners[i], 0.0, 0.0, 0.0);
            }

            frame.Closed = true;
            frame.Layer = TitleLayer;
            block.AppendEntity(frame);

            // The rules: two across, and the upright between the two halves of the lower rows.
            AddLine(block, 0, 18, 180, 18);
            AddLine(block, 0, 36, 180, 36);
            AddLine(block, 90, 0, 90, 36);
            AddField(block, "PROJECT", "Project", 2, 46);
            AddField(block, "TITLE", "Sheet title", 2, 28);
            AddField(block, "NUMBER", "Sheet number", 120, 28);
            AddField(block, "CLIENT", "Client", 2, 10);
            AddField(block, "ADDRESS", "Address", 92, 10);
            AddField(block, "DRAWNBY", "Drawn by", 2, 2);
            AddField(block, "CHECKEDBY", "Checked by", 92, 2);
            return block;
        }

        private static void AddLine(DxfBlockTableRecord block, double x1, double y1, double x2, double y2)
        {
            var line = new DxfLine(new GeoPoint3(x1, y1, 0), new GeoPoint3(x2, y2, 0)) { Layer = TitleLayer };
            block.AppendEntity(line);
        }

        /// <summary>A label in small letters and, under it, the attribute definition that holds the words.</summary>
        private static void AddField(DxfBlockTableRecord block, string tag, string label, double x, double y)
        {
            var text = new DxfDBText
            {
                TextString = label,
                Position = new GeoPoint3(x, y + 9.5, 0),
                Height = 1.8,
                Layer = TitleLayer,
            };
            block.AppendEntity(text);
            var definition = new DxfAttributeDefinition(new GeoPoint3(x, y, 0), label, tag, label, null)
            {
                Height = 3.5,
                Layer = TitleLayer,
            };
            block.AppendEntity(definition);
        }

        /// <summary>The fields of the title block's attributes, by tag.</summary>
        private static readonly KeyValuePair<string, string>[] Codes =
        {
            new KeyValuePair<string, string>("PROJECT", "%<\\AcSm SheetSet.Name>%"),
            new KeyValuePair<string, string>("TITLE", "%<\\AcSm Sheet.Title>%"),
            new KeyValuePair<string, string>("NUMBER", "%<\\AcSm Sheet.Number>%"),
            new KeyValuePair<string, string>("CLIENT", "%<\\AcSm SheetSet.Client>%"),
            new KeyValuePair<string, string>("ADDRESS", "%<\\AcSm SheetSet.Project Address>%"),
            new KeyValuePair<string, string>("DRAWNBY", "%<\\AcSm Sheet.Drawn By>%"),
            new KeyValuePair<string, string>("CHECKEDBY", "%<\\AcSm Sheet.Checked By>%"),
        };

        /// <summary>Puts the frame and the title block in a layout of a sheet of the given paper size.</summary>
        private static void Place(
            CadDocument document,
            DxfLayout layout,
            DxfBlockTableRecord block,
            double width,
            double height
        )
        {
            var frame = new DxfPolyline();
            GeoPoint2[] corners =
            {
                new GeoPoint2(10, 10),
                new GeoPoint2(width - 10, 10),
                new GeoPoint2(width - 10, height - 10),
                new GeoPoint2(10, height - 10),
            };

            for (int i = 0; i < corners.Length; i++)
            {
                frame.AddVertexAt(i, corners[i], 0.0, 0.0, 0.0);
            }

            frame.Closed = true;
            frame.Layer = TitleLayer;
            layout.BlockTableRecord.AppendEntity(frame);
            var insert = new DxfBlockReference(new GeoPoint3(width - 10 - 180, 10, 0), block) { Layer = TitleLayer };
            layout.BlockTableRecord.AppendEntity(insert);
            var context = new CadFieldContext(document);

            foreach (DxfAttributeDefinition definition in block.OfType<DxfAttributeDefinition>())
            {
                var attribute = new DxfAttributeReference();
                attribute.SetAttributeFromBlock(definition, insert.BlockTransform);
                insert.AttributeCollection.AppendAttribute(attribute);
                CadFields.Set(attribute, Codes.First(c => c.Key == definition.Tag).Value, context);
            }
        }
    }
}
