using System;
using System.IO;
using System.Linq;
using GeometryHelper.Dxf;
using GeometryHelper.Geometry;
using OpenCAD2D.SheetSets;

namespace OpenCAD2D.Wpf.UnitTest
{
    /// <summary>
    /// What the sheet set dialog tests stand on: a temporary folder, drawings made in code with the layouts named, and a sheet
    /// set "Tower" with its .dst, a sheet creation template and sheets in two subsets, so that a test reads values it knows and
    /// no drawing of anyone's goes into the repository.
    /// </summary>
    internal sealed class SheetSetDialogKit : IDisposable
    {
        private static readonly DateTime Noon = new DateTime(2026, 11, 26, 14, 30, 5, 123);

        internal SheetSetDialogKit()
        {
            Folder = Path.Combine(Path.GetTempPath(), "gk", "sheetsetdlg-" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(SetFolder);
        }

        /// <summary>Gets the temporary folder everything of the test is in.</summary>
        internal string Folder { get; }

        /// <summary>Gets the folder of the .dst.</summary>
        internal string SetFolder => Path.Combine(Folder, "set");

        /// <summary>Gets the path of the .dst.</summary>
        internal string Dst => Path.Combine(SetFolder, "Tower.dst");

        /// <summary>Gets the path of the sheet creation template, a .dwt with the layouts Cover and A1 sheet.</summary>
        internal string Template => Path.Combine(Folder, "templates", "sheets.dwt");

        /// <summary>Gets the set, once <see cref="Make"/> has made it.</summary>
        internal CadSheetSetFile File { get; private set; }

        public void Dispose()
        {
            try
            {
                Directory.Delete(Folder, true);
            }
            catch (IOException)
            {
                // A file a watcher still holds is left for the system to clear.
            }
        }

        /// <summary>A GUID source that counts, so that the IDs are the same on every run.</summary>
        internal static Func<Guid> Counter()
        {
            uint n = 0xA0000000;
            return () => new Guid(++n, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0);
        }

        /// <summary>A drawing with the layouts named, the first being the layout every drawing starts with, renamed; written as a DWG.</summary>
        internal string Drawing(string folder, string file, params string[] layouts) =>
            DrawingWithBlocks(folder, file, layouts, new string[0]);

        /// <summary>A drawing as <see cref="Drawing"/> makes it, with a block of each name given holding a circle.</summary>
        internal string DrawingWithBlocks(string folder, string file, string[] layouts, string[] blocks)
        {
            Directory.CreateDirectory(folder);
            string path = Path.Combine(folder, file);
            var database = new DxfDatabase("AC1032");
            database.Layouts.Single(l => l.LayoutName == "Layout1").LayoutName = layouts[0];

            foreach (string name in layouts.Skip(1))
            {
                var layout = new DxfLayout { LayoutName = name };
                layout.AddToLayoutDictionary(database, null);
                layout.Initialize();
            }

            for (int i = 0; i < layouts.Length; i++)
            {
                database
                    .Layouts.Single(l => l.LayoutName == layouts[i])
                    .BlockTableRecord.AppendEntity(
                        new DxfLine(new GeoPoint3(10 * (i + 1), 0, 0), new GeoPoint3(10 * (i + 1), 50, 0))
                    );
            }

            foreach (string name in blocks)
            {
                var block = new DxfBlockTableRecord { Name = name, Origin = new GeoPoint3(0, 0, 0) };
                database.BlockTable.Add(block);
                block.AppendEntity(new DxfCircle(new GeoPoint3(0, 0, 0), GeoVector3.ZAxis, 2));
            }

            database.ModelSpace.AppendEntity(new DxfCircle(new GeoPoint3(0, 0, 0), GeoVector3.ZAxis, 5));
            CadDocument.From(database, file).SaveAs(path);
            return path;
        }

        /// <summary>The names of the layouts of a drawing on disk other than Model, in the order of their tabs.</summary>
        internal static string[] LayoutNames(string path) =>
            CadDrawingFile
                .Open(path)
                .Layouts.Where(l => l.LayoutName != "Model")
                .OrderBy(l => l.TabOrder)
                .Select(l => l.LayoutName)
                .ToArray();

        /// <summary>
        /// Makes the set: "Tower", described "Six storeys", the template of the A1 sheet layout (not asked for), and two subsets, Architectural
        /// (with a sheet A-101 Ground Floor Plan and a sheet A-102 First Floor Plan, both saved drawings) and Structural (with
        /// the sheet S-101 Foundation Plan); the set holds a custom property of its own, Client, and one of every sheet, Drawn By.
        /// </summary>
        internal CadSheetSetFile Make()
        {
            File = CadSheetSetFile.Create(Dst, "Tower", "Six storeys", Counter());
            File.Clock = () => Noon;
            Drawing(Path.GetDirectoryName(Template), Path.GetFileName(Template), "Cover", "A1 sheet");
            string architectural = Path.Combine(SetFolder, "Architectural");
            string structural = Path.Combine(SetFolder, "Structural");
            File.Change(set =>
            {
                set.Template = new CadDrawingReference(
                    CadFileReference.FromPath(Template, SetFolder),
                    "A1 sheet",
                    null,
                    null
                );
                set.PromptForTemplate = false;
                set.ProjectNumber = "ET-2026-014";
                set.AddCustomProperty("Client", "Northbank", CadSheetSetPropertyFlags.Set);
                set.AddCustomProperty("Drawn By", string.Empty, CadSheetSetPropertyFlags.Sheet);
                CadSheetSubset a = set.NewSubset("Architectural", null, true);
                CadSheetSubset s = set.NewSubset("Structural", null, true);
                a.NewSheet("A-101", "Ground Floor Plan");
                a.NewSheet("A-102", "First Floor Plan");
                s.NewSheet("S-101", "Foundation Plan");
            });
            return File;
        }

        /// <summary>The subset of a name in the set as it is now.</summary>
        internal CadSheetSubset Subset(string name) => File.SheetSet.Subsets.Single(s => s.Name == name);

        /// <summary>The sheet of a number in the set as it is now.</summary>
        internal CadSheetSetSheet Sheet(string number) => File.SheetSet.AllSheets().Single(s => s.Number == number);

        /// <summary>The set read again from disk, so that a test sees what was written and not what is in memory.</summary>
        internal CadSheetSetFile Reopen() => CadSheetSetFile.Open(Dst);
    }
}
