using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using GeometryHelper;
using GeometryHelper.Dxf;
using GeometryHelper.Geometry;

namespace OpenCAD2D.SheetSets
{
    /// <summary>
    /// A named view of model space in a drawing of a resource location, as the Model Views tab lists it: its name, the centre of
    /// what it shows and how wide and how high that is, in the drawing's own units (a view 32 000 by 18 500 of a drawing in
    /// millimetres), and where its record is in the drawing's view table.
    /// </summary>
    public sealed class CadModelView
    {
        internal CadModelView(
            string name,
            string handle,
            string ownerHandle,
            GeoPoint2 center,
            double width,
            double height,
            DxfUnits units
        )
        {
            Name = name;
            Handle = handle;
            OwnerHandle = ownerHandle;
            Center = center;
            Width = width;
            Height = height;
            Units = units;
        }

        /// <summary>Gets the name of the view.</summary>
        public string Name { get; }

        /// <summary>Gets the handle of the view's record in the drawing, in hexadecimal; null where it has none.</summary>
        public string Handle { get; }

        /// <summary>Gets the handle of the drawing's view table, which owns the record; null where it has none.</summary>
        public string OwnerHandle { get; }

        /// <summary>Gets the centre of what the view shows, in the drawing's model space.</summary>
        public GeoPoint2 Center { get; }

        /// <summary>Gets how wide what the view shows is, in the drawing's units.</summary>
        public double Width { get; }

        /// <summary>Gets how high what the view shows is, in the drawing's units.</summary>
        public double Height { get; }

        /// <summary>Gets the unit the drawing is drawn in (its INSUNITS), which the width and the height are in.</summary>
        public DxfUnits Units { get; }
    }

    /// <summary>
    /// What the Model Views tab reads of a resource location (a folder the set lists under <c>Resources</c>) and of the drawings
    /// in it, with no window and without changing a file: the drawings of a folder, and the named views of model space of one.
    /// </summary>
    public static class CadSheetSetResources
    {
        /// <summary>
        /// Lists the drawings of a folder: the .dwg files in it and not those in the folders under it, as the Model Views tab shows
        /// them, in the order of their names (letters of either case alike first, then ordinal).
        /// </summary>
        /// <param name="folder">The folder.</param>
        /// <returns>The full paths; none where the folder is not there.</returns>
        /// <exception cref="ArgumentNullException">Thrown when the folder is null.</exception>
        public static IReadOnlyList<string> DrawingsIn(string folder)
        {
            if (folder == null)
            {
                throw new ArgumentNullException(nameof(folder));
            }

            if (!Directory.Exists(folder))
            {
                return Array.Empty<string>();
            }

            List<string> files = Directory
                .GetFiles(folder, "*.dwg", SearchOption.TopDirectoryOnly)
                .Where(f => string.Equals(Path.GetExtension(f), ".dwg", StringComparison.OrdinalIgnoreCase))
                .ToList();
            files.Sort(ByName);
            return files;
        }

        /// <summary>
        /// Reads the named views of model space of a drawing (those saved in a layout are left out), in the order of their names
        /// (letters of either case alike first, then ordinal). The drawing is read and not kept open.
        /// </summary>
        /// <param name="drawingFile">The drawing.</param>
        /// <returns>The views; none for a drawing that has none.</returns>
        /// <exception cref="ArgumentNullException">Thrown when the file is null.</exception>
        /// <exception cref="IOException">Thrown when the file cannot be read.</exception>
        /// <exception cref="FormatException">Thrown when the file is a drawing that cannot be read.</exception>
        public static IReadOnlyList<CadModelView> ModelViewsOf(string drawingFile)
        {
            if (drawingFile == null)
            {
                throw new ArgumentNullException(nameof(drawingFile));
            }

            return ModelViewsOf(CadDrawingFile.Open(drawingFile));
        }

        internal static IReadOnlyList<CadModelView> ModelViewsOf(DxfDatabase database)
        {
            var views = new List<CadModelView>();

            foreach (DxfViewTableRecord view in database.ViewTable)
            {
                if (!view.IsPaperspaceView && view.Name.Length > 0)
                {
                    views.Add(Of(database, view));
                }
            }

            views.Sort((a, b) => ByName(a.Name, b.Name));
            return views;
        }

        internal static CadModelView Of(DxfDatabase database, DxfViewTableRecord view) =>
            new CadModelView(
                view.Name,
                view.Handle,
                database.ViewTable.Handle,
                view.CenterPoint,
                view.Width,
                view.Height,
                database.Insunits
            );

        private static int ByName(string a, string b)
        {
            int order = string.Compare(a, b, StringComparison.OrdinalIgnoreCase);
            return order != 0 ? order : string.CompareOrdinal(a, b);
        }
    }
}
