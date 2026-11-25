using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Xml.Linq;
using GeometryHelper;
using IoPath = System.IO.Path;

namespace OpenCAD2D.SheetSets
{
    public sealed partial class CadSheetSetFile
    {
        /// <summary>
        /// Makes a sheet set from an example, as the wizard's first route does: the example's organisation and settings are
        /// kept (its subsets, nested as they are, its custom properties, sheet creation template and folder, publish options,
        /// view categories and the parts this program does not model) and its sheets are left out. Every part has an ID of its
        /// own, every file the example names is named again relative to the new .dst, a folder that was the example's own
        /// folder becomes the new one, and the file is saved with <see cref="DbVersion"/> "1.1" and <see cref="FileRevision"/> 1.
        /// </summary>
        /// <param name="path">The path of the .dst to make; the file must not be there, and its folder must.</param>
        /// <param name="name">The name of the new sheet set.</param>
        /// <param name="description">The description, or null for none.</param>
        /// <param name="examplePath">The path of the example's .dst.</param>
        /// <param name="createFolderHierarchy">Whether each subset gets a folder, named after it and inside its parent's, made on disk and set as the subset's sheet location (Create a folder hierarchy based on subsets).</param>
        /// <returns>The file, as saved.</returns>
        /// <exception cref="ArgumentNullException">Thrown when the path, the name or the example is null.</exception>
        /// <exception cref="FileNotFoundException">Thrown when the example is not there.</exception>
        /// <exception cref="FormatException">Thrown when the example is not a sheet set data file.</exception>
        /// <exception cref="IOException">Thrown when a file is already at the path.</exception>
        public static CadSheetSetFile CreateFromExample(
            string path,
            string name,
            string description,
            string examplePath,
            bool createFolderHierarchy
        ) => CreateFromExample(path, name, description, examplePath, createFolderHierarchy, null);

        /// <summary>
        /// Makes a sheet set of layouts of drawings that exist, as the wizard's second route does: an empty set, then the
        /// layouts of each source imported as sheets in the order given (with the set marks written into the drawings), into
        /// subsets made from the folders where the options say so. Nothing is left behind where an import is refused: the
        /// .dst is deleted again (drawings already marked keep a mark that names a set that is not there).
        /// </summary>
        /// <param name="path">The path of the .dst to make; the file must not be there, and its folder must.</param>
        /// <param name="name">The name of the new sheet set.</param>
        /// <param name="description">The description, or null for none.</param>
        /// <param name="sources">The drawings and their layouts, in the order of the sheets.</param>
        /// <param name="options">The import options; null for all off.</param>
        /// <returns>The file, as saved.</returns>
        /// <exception cref="ArgumentNullException">Thrown when the path, the name or the sources are null.</exception>
        /// <exception cref="IOException">Thrown when a file is already at the path.</exception>
        /// <exception cref="InvalidOperationException">Thrown when a layout is refused (the message says why).</exception>
        public static CadSheetSetFile CreateFromDrawings(
            string path,
            string name,
            string description,
            IEnumerable<CadSheetImportSource> sources,
            CadSheetImportOptions options
        ) => CreateFromDrawings(path, name, description, sources, options, null, null);

        /// <summary>
        /// Checks the layouts of a drawing for the wizard's Choose Layouts page, before any set is there: as
        /// <see cref="CadSheetSet.CheckLayouts(string)"/> does for a set, without the check against its sheets.
        /// </summary>
        /// <param name="drawingFile">The path of the drawing.</param>
        /// <returns>The layouts; empty where the drawing is not a DWG.</returns>
        /// <exception cref="ArgumentNullException">Thrown when the path is null.</exception>
        /// <exception cref="FileNotFoundException">Thrown when the drawing is not there.</exception>
        public static IReadOnlyList<CadSheetImportLayout> CheckLayouts(string drawingFile) =>
            CadSheetSetDrawings.CheckFile(drawingFile, null);

        /// <summary>Gets or sets what tells the time for the <c>UpdateTime</c> of a sheet set mark, UTC as AutoCAD writes it (measured: a mark made at 03:14 local time on 26 November at UTC+7 says 20:14 on the 25th); a test gives a clock that does not move.</summary>
        internal Func<DateTime> Clock { get; set; } = () => DateTime.UtcNow;

        /// <summary>Makes a set from an example, new objects getting their GUIDs from a source of the caller's.</summary>
        internal static CadSheetSetFile CreateFromExample(
            string path,
            string name,
            string description,
            string examplePath,
            bool createFolderHierarchy,
            Func<Guid> newGuid
        )
        {
            if (path == null)
            {
                throw new ArgumentNullException(nameof(path));
            }

            if (name == null)
            {
                throw new ArgumentNullException(nameof(name));
            }

            if (examplePath == null)
            {
                throw new ArgumentNullException(nameof(examplePath));
            }

            Guard.Argument(path.Trim().Length > 0, nameof(path), "A sheet set needs the path of its .dst.");
            var file = new CadSheetSetFile(IoPath.GetFullPath(path), newGuid);
            if (File.Exists(file.Path))
            {
                throw new IOException("A file is already at " + file.Path + ".");
            }

            CadSheetSetFile example = Open(examplePath);
            file._document = file.Blank(name, description);
            XElement fresh = file._document.Root.Element(CadDstTree.SheetSetClass);
            XElement set = new XElement(example._document.Root.Element(CadDstTree.SheetSetClass));
            foreach (XElement sheet in set.Descendants(CadDstTree.SheetClass).ToList())
            {
                sheet.Remove();
            }

            CadDstTree.FindObject(set, "SheetSelSets")?.RemoveNodes();
            file.GiveNewIds(set);
            file.Rebase(set, example.Folder);
            CadDstTree.SetString(set, "Name", name);
            CadDstTree.SetString(set, "Desc", description);
            fresh.ReplaceWith(set);
            if (createFolderHierarchy)
            {
                file.MakeFolders(set, file.Folder);
            }

            file.Save();
            return file;
        }

        /// <summary>Gives every object under a sheet set element an ID of this file, and every reference to one of them its new ID.</summary>
        private void GiveNewIds(XElement set)
        {
            var renamed = new Dictionary<string, string>(StringComparer.Ordinal);
            foreach (XElement element in set.DescendantsAndSelf().Where(e => e.Attribute("ID") != null).ToList())
            {
                string id = NewId();
                renamed[(string)element.Attribute("ID")] = id;
                element.SetAttributeValue("ID", id);
            }

            foreach (
                XElement reference in set.Descendants(CadDstTree.Prop).Where(e => (string)e.Attribute("vt") == "-1")
            )
            {
                if (renamed.TryGetValue(reference.Value, out string id))
                {
                    reference.Value = id;
                }
            }
        }

        /// <summary>
        /// Names again, relative to this file, every file the example names that is there: the relative form and the absolute
        /// form of it, the environment and special folder forms left out as they would no longer match. A sheet location that
        /// was the example folder itself becomes the folder of this file. A file that is not there is left as it was written.
        /// </summary>
        private void Rebase(XElement set, string exampleFolder)
        {
            foreach (XElement element in set.DescendantsAndSelf().Where(e => e.Name.LocalName != CadDstTree.Prop))
            {
                CadFileReference reference = CadDstTree.ReadFile(element);
                string resolved = reference?.Resolve(exampleFolder);
                if (resolved == null)
                {
                    continue;
                }

                bool own =
                    CadDstTree.PropName(element) == "NewSheetLocation"
                    && CadSheetSetDrawings.Same(resolved, exampleFolder);
                CadDstTree.WriteFile(element, CadFileReference.FromPath(own ? Folder : resolved, Folder));
            }
        }

        /// <summary>Makes a folder for each subset under a group, named after it and inside the folder of its parent, and sets it as the sheet location of the subset.</summary>
        private void MakeFolders(XElement group, string parentFolder)
        {
            foreach (XElement subset in group.Elements(CadDstTree.SubsetClass))
            {
                string name = CadSheetSetDrawings.AsFileName(CadDstTree.GetString(subset, "Name"));
                string folder = IoPath.Combine(parentFolder, name.Length == 0 ? "Subset" : name);
                Directory.CreateDirectory(folder);
                XElement location = CadDstTree.FindObject(subset, "NewSheetLocation");
                if (location == null)
                {
                    location = NewObject(CadDstTree.FileReferenceClass, "NewSheetLocation");
                    CadDstTree.InsertNamed(subset, location);
                }

                CadDstTree.WriteFile(location, CadFileReference.FromPath(folder, Folder));
                MakeFolders(subset, folder);
            }
        }

        /// <summary>Makes a set from drawings, new objects getting their GUIDs and the marks their time from sources of the caller's.</summary>
        internal static CadSheetSetFile CreateFromDrawings(
            string path,
            string name,
            string description,
            IEnumerable<CadSheetImportSource> sources,
            CadSheetImportOptions options,
            Func<Guid> newGuid,
            Func<DateTime> clock
        )
        {
            if (sources == null)
            {
                throw new ArgumentNullException(nameof(sources));
            }

            CadSheetSetFile file = Create(path, name, description, newGuid);
            file.Clock = clock ?? file.Clock;
            CadSheetImportOptions chosen = options ?? new CadSheetImportOptions();
            try
            {
                file.Change(set =>
                {
                    foreach (CadSheetImportSource source in sources)
                    {
                        CadSheetSubset into = SubsetOf(set, source, chosen);
                        into.ImportLayouts(source.DrawingFile, source.LayoutNames, chosen.PrefixTitleWithFileName);
                    }
                });
            }
            catch
            {
                try
                {
                    File.Delete(file.Path);
                }
                catch (IOException)
                {
                    // A set that cannot be deleted is left; the import failed for the reason that goes on.
                }

                throw;
            }

            return file;
        }

        /// <summary>The subset the sheets of a source go into: the set itself, or the subsets that stand for the folders between the picked folder and the drawing, made where they are not there yet.</summary>
        private static CadSheetSubset SubsetOf(
            CadSheetSet set,
            CadSheetImportSource source,
            CadSheetImportOptions options
        )
        {
            if (!options.CreateSubsetsFromFolders || source.RootFolder == null)
            {
                return set;
            }

            string root = CadSheetSetDrawings.Full(source.RootFolder);
            string folder = CadSheetSetDrawings.Full(IoPath.GetDirectoryName(IoPath.GetFullPath(source.DrawingFile)));
            bool inside =
                string.Equals(folder, root, StringComparison.OrdinalIgnoreCase)
                || folder.StartsWith(root + "\\", StringComparison.OrdinalIgnoreCase);
            if (!inside)
            {
                return set;
            }

            var parts = new List<string>();
            if (!options.IgnoreTopLevelFolder)
            {
                string top = IoPath.GetFileName(root);
                parts.Add(top.Length == 0 ? root : top);
            }

            parts.AddRange(
                folder.Substring(root.Length).Split(new[] { '\\', '/' }, StringSplitOptions.RemoveEmptyEntries)
            );
            CadSheetSubset current = set;
            foreach (string part in parts)
            {
                CadSheetSubset existing = current.Subsets.FirstOrDefault(s =>
                    string.Equals(s.Name, part, StringComparison.OrdinalIgnoreCase)
                );
                current = existing ?? current.AddSubset(part);
            }

            return current;
        }
    }
}
