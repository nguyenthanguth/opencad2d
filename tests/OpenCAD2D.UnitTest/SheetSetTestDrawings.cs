using System;
using System.IO;
using System.Linq;
using GeometryHelper.Dxf;
using GeometryHelper.Geometry;

namespace OpenCAD2D.UnitTest
{
    /// <summary>
    /// Drawings for the sheet set tests, made in code and written to a temporary folder as DWG files: layouts of given names
    /// in the order given, a line in the sheet of each and a circle in model space, so that a test can tell what was kept
    /// of the drawing and what was taken out.
    /// </summary>
    internal static class SheetSetTestDrawings
    {
        /// <summary>A drawing with the layouts named, the first being the layout every drawing starts with, renamed.</summary>
        internal static DxfDatabase Build(params string[] layouts)
        {
            var database = new DxfDatabase("AC1032");
            DxfLayout first = database.Layouts.Single(l => l.LayoutName == "Layout1");
            first.LayoutName = layouts[0];

            foreach (string name in layouts.Skip(1))
            {
                var layout = new DxfLayout { LayoutName = name };
                layout.AddToLayoutDictionary(database, null);
                layout.Initialize();
            }

            for (int i = 0; i < layouts.Length; i++)
            {
                DxfLayout layout = database.Layouts.Single(l => l.LayoutName == layouts[i]);
                layout.BlockTableRecord.AppendEntity(
                    new DxfLine(new GeoPoint3(10 * (i + 1), 0, 0), new GeoPoint3(10 * (i + 1), 50, 0))
                );
            }

            database.ModelSpace.AppendEntity(new DxfCircle(new GeoPoint3(0, 0, 0), GeoVector3.ZAxis, 5));
            return database;
        }

        /// <summary>Writes a drawing of the layouts named as a DWG (a .dwt where the name says so) and gives its path.</summary>
        internal static string Write(string folder, string file, params string[] layouts)
        {
            Directory.CreateDirectory(folder);
            string path = Path.Combine(folder, file);
            CadDocument.From(Build(layouts), file).SaveAs(path);
            return path;
        }

        /// <summary>Changes a drawing on disk: read, edited by the action and written again as a DWG.</summary>
        internal static void Edit(string path, Action<DxfDatabase> edit)
        {
            DxfDatabase database = Read(path);
            edit(database);
            CadDocument.From(database, Path.GetFileName(path)).SaveAs(path);
        }

        /// <summary>Reads a drawing back.</summary>
        internal static DxfDatabase Read(string path) => CadDrawingFile.Open(path);

        /// <summary>The names of the layouts of a drawing other than Model, in the order of their tabs.</summary>
        internal static string[] LayoutNames(string path) =>
            Read(path)
                .Layouts.Where(l => l.LayoutName != "Model")
                .OrderBy(l => l.TabOrder)
                .Select(l => l.LayoutName)
                .ToArray();

        /// <summary>The handle of a layout of a drawing on disk.</summary>
        internal static string HandleOf(string path, string layout) =>
            Read(path).Layouts.Single(l => l.LayoutName == layout).Handle;

        /// <summary>How many entities a layout of a drawing on disk holds of a type.</summary>
        internal static int Count<T>(string path, string layout) =>
            Read(path).Layouts.Single(l => l.LayoutName == layout).BlockTableRecord.OfType<T>().Count();

        /// <summary>How many entities model space of a drawing on disk holds.</summary>
        internal static int ModelCount(string path) => Read(path).ModelSpace.Count();
    }
}
