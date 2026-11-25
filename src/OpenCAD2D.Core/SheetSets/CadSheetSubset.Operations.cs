using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Xml.Linq;
using GeometryHelper;
using GeometryHelper.Dxf;
using IoFile = System.IO.File;
using IoPath = System.IO.Path;

namespace OpenCAD2D.SheetSets
{
    public partial class CadSheetSubset
    {
        /// <summary>
        /// Adds a subset as New Subset does: the name must not be empty and must not be the name of a subset
        /// already in this group (letter case ignored); with <paramref name="createFolder"/> the subset gets a folder of its
        /// own, its name inside the folder new sheets of this group go to, made on disk now and set as the subset's
        /// <see cref="NewSheetLocation"/>, so that its sheets are saved there (Create Folder Hierarchy).
        /// </summary>
        /// <param name="name">The name; for a folder it must also be a name a folder can have.</param>
        /// <param name="description">The description, or null for none.</param>
        /// <param name="createFolder">Whether the subset gets a folder.</param>
        /// <param name="index">The place among the subsets and sheets of this group; -1 for the end.</param>
        /// <returns>The new subset.</returns>
        /// <exception cref="ArgumentNullException">Thrown when the name is null.</exception>
        /// <exception cref="ArgumentException">Thrown when the name is empty, is a sibling subset's, or cannot be a folder's where one is made.</exception>
        /// <exception cref="ArgumentOutOfRangeException">Thrown when the place is not -1 or from 0 to the count of children.</exception>
        /// <exception cref="InvalidOperationException">Thrown outside <see cref="CadSheetSetFile.Change"/>.</exception>
        public CadSheetSubset NewSubset(string name, string description, bool createFolder, int index = -1)
        {
            if (name == null)
            {
                throw new ArgumentNullException(nameof(name));
            }

            Edit();
            Guard.Argument(name.Trim().Length > 0, nameof(name), "A subset needs a name.");
            Guard.Argument(
                !Subsets.Any(s => string.Equals(s.Name, name, StringComparison.OrdinalIgnoreCase)),
                nameof(name),
                "There is a subset named \"" + name + "\" in this group already."
            );
            string folder = null;
            if (createFolder)
            {
                Guard.Argument(
                    name.IndexOfAny(IoPath.GetInvalidFileNameChars()) < 0 && name.Trim('.', ' ').Length > 0,
                    nameof(name),
                    "The name \"" + name + "\" cannot be the name of a folder."
                );
                folder = IoPath.Combine(LocationFolder(), name);
            }

            CadSheetSubset subset = AddSubset(name, description, index);
            if (folder != null)
            {
                Directory.CreateDirectory(folder);
                subset.NewSheetLocation = CadFileReference.FromPath(folder, File.Folder);
            }

            return subset;
        }

        /// <summary>
        /// Makes a new sheet as New Sheet does (D11): a drawing is made from the layout of the sheet creation template with
        /// every other layout taken out and nothing left in model space, the layout is named "Number Title" and the drawing is
        /// saved as a DWG of AutoCAD 2018 in the folder new sheets of this group go to; the sheet is added to this group and
        /// takes every sheet custom property at its default; the drawing gets the sheet set mark (<see cref="CadSheetSetHint"/>).
        /// </summary>
        /// <remarks>
        /// The template is the one given, else this group's, else that of the group it is in, up to the sheet set. The folder is
        /// this group's <see cref="NewSheetLocation"/>, else that of the group it is in, up to the sheet set, else the folder of
        /// the .dst; it is made where it is not there. Fields in the layout are not rewritten here.
        /// </remarks>
        /// <param name="number">The sheet number, free text; empty for none.</param>
        /// <param name="title">The sheet title, which must not be empty.</param>
        /// <param name="template">The layout of a drawing template to make it from, or null for this group's.</param>
        /// <param name="fileName">The drawing's file name, with or without .dwg; null for "Number Title".</param>
        /// <param name="index">The place among the subsets and sheets of this group; -1 for the end.</param>
        /// <returns>The new sheet.</returns>
        /// <exception cref="ArgumentNullException">Thrown when the number or the title is null.</exception>
        /// <exception cref="ArgumentException">Thrown when the title is empty, or the file name holds a character a file cannot.</exception>
        /// <exception cref="ArgumentOutOfRangeException">Thrown when the place is not -1 or from 0 to the count of children.</exception>
        /// <exception cref="InvalidOperationException">Thrown outside <see cref="CadSheetSetFile.Change"/>, or where there is no template to make it from.</exception>
        /// <exception cref="FileNotFoundException">Thrown when the template's file, or its layout, is not there.</exception>
        /// <exception cref="IOException">Thrown when the drawing's file is there already.</exception>
        public CadSheetSetSheet NewSheet(
            string number,
            string title,
            CadDrawingReference template = null,
            string fileName = null,
            int index = -1
        )
        {
            if (number == null)
            {
                throw new ArgumentNullException(nameof(number));
            }

            if (title == null)
            {
                throw new ArgumentNullException(nameof(title));
            }

            Edit();
            Guard.Argument(title.Trim().Length > 0, nameof(title), "A sheet needs a title.");
            RequireIndex(index, Components().Count);
            CadDrawingReference source = template
                ?? Ancestry()
                    .Select(e => CadDstTree.ReadDrawing(CadDstTree.FindObject(e, "DefDwtLayout")))
                    .FirstOrDefault(r => r != null);
            if (source == null)
            {
                throw new InvalidOperationException(
                    "There is no sheet creation template for a new sheet: set one for the sheet set or the subset."
                );
            }

            string name = CadSheetSetDrawings.DwgName(fileName ?? CadSheetSetSheet.DefaultFileName(number, title));
            Guard.Argument(name.Length > 4, nameof(fileName), "A sheet needs a file name.");
            string folder = LocationFolder();
            string path = IoPath.GetFullPath(IoPath.Combine(folder, name));
            if (IoFile.Exists(path))
            {
                throw new IOException("The file " + path + " is there already.");
            }

            string dwt = source.File.Resolve(File.Folder);
            if (dwt == null)
            {
                throw new FileNotFoundException(
                    "The sheet creation template "
                        + (source.File.FileName ?? source.File.RelativeFileName)
                        + " is not there."
                );
            }

            DxfDatabase templateDrawing = CadDrawingFile.Open(dwt);
            DxfLayout chosen = CadSheetSetDrawings.Find(templateDrawing, source.Handle, source.Name, out _);
            if (chosen == null)
            {
                throw new FileNotFoundException(
                    "The sheet creation template " + dwt + " has no layout \"" + source.Name + "\".",
                    dwt
                );
            }

            DxfDatabase made = CadCommands.TemplateOf(templateDrawing, chosen.LayoutName, true);
            DxfLayout layout = CadSheetSetDrawings.SheetLayouts(made).Single();
            string layoutName = CadSheetSetSheet.DefaultLayoutName(number, title);
            layout.LayoutName =
                CadSheetSetDrawings.WhyNotLayoutName(layoutName) == null ? layoutName : layoutName + "_";
            CadSheetSetHint.Write(made, File.Path, path, layout.LayoutName, layout.Handle, File.Clock());
            Directory.CreateDirectory(folder);
            CadSheetSetDrawings.Save(made, path, false);
            CadSheetSetSheet sheet = AddSheet(
                number,
                title,
                CadSheetSetDrawings.Reference(path, File.Folder, layout),
                index
            );
            GiveSheetProperties(sheet);
            return sheet;
        }

        /// <summary>
        /// Makes layouts of a drawing sheets of the set, as Import Layouts as Sheets does: a sheet for each, at the end of this
        /// group, with no number and the layout's name as its title (the drawing's file name, a hyphen and the layout's name
        /// with <paramref name="prefixTitleWithFileName"/>), every sheet custom property at its default. The drawing is
        /// rewritten as a DWG of AutoCAD 2018 with the sheet set mark; the layout is not touched. A layout that
        /// <see cref="CadSheetSet.CheckLayouts(string)"/> refuses is refused here, and then nothing is changed.
        /// </summary>
        /// <param name="drawingFile">The path of the drawing, a DWG.</param>
        /// <param name="layoutNames">The names of the layouts, in the order of their sheets.</param>
        /// <param name="prefixTitleWithFileName">Whether the title carries the file name.</param>
        /// <returns>The new sheets, in the order of the layouts.</returns>
        /// <exception cref="ArgumentNullException">Thrown when the drawing or the layouts are null.</exception>
        /// <exception cref="ArgumentException">Thrown when there is no layout, or the same layout is named twice.</exception>
        /// <exception cref="FileNotFoundException">Thrown when the drawing is not there.</exception>
        /// <exception cref="InvalidOperationException">Thrown outside <see cref="CadSheetSetFile.Change"/>, or where a layout is refused (the message says why).</exception>
        /// <exception cref="NotSupportedException">Thrown when the DWG writer would leave something of the drawing out; the drawing is not replaced.</exception>
        public IReadOnlyList<CadSheetSetSheet> ImportLayouts(
            string drawingFile,
            IReadOnlyList<string> layoutNames,
            bool prefixTitleWithFileName
        )
        {
            if (drawingFile == null)
            {
                throw new ArgumentNullException(nameof(drawingFile));
            }

            if (layoutNames == null)
            {
                throw new ArgumentNullException(nameof(layoutNames));
            }

            Edit();
            Guard.Argument(layoutNames.Count > 0, nameof(layoutNames), "There is no layout to import.");
            Guard.Argument(
                layoutNames.Select(n => (n ?? string.Empty).ToUpperInvariant()).Distinct().Count() == layoutNames.Count,
                nameof(layoutNames),
                "A layout is named twice."
            );
            string path = CadSheetSetDrawings.Full(drawingFile);
            if (!IoFile.Exists(path))
            {
                throw new FileNotFoundException("The drawing " + path + " is not there.", path);
            }

            if (!CadDrawingFile.IsDwgName(path))
            {
                throw new InvalidOperationException("Only a layout of a DWG can be a sheet, not one of " + path + ".");
            }

            DxfDatabase database = CadDrawingFile.Open(path);
            List<CadSheetImportLayout> checks = CadSheetSetDrawings.Check(database, path, File.SheetSet, null);
            var layouts = new List<DxfLayout>();
            foreach (string layoutName in layoutNames)
            {
                DxfLayout layout = CadSheetSetDrawings.Find(database, layoutName);
                if (layout == null)
                {
                    throw new InvalidOperationException(
                        "The drawing " + path + " has no layout \"" + layoutName + "\"."
                    );
                }

                CadSheetImportLayout check = checks.First(c =>
                    string.Equals(c.Handle, layout.Handle, StringComparison.Ordinal)
                );
                if (check.IsRefused)
                {
                    throw new InvalidOperationException(check.Refusal);
                }

                layouts.Add(layout);
            }

            DxfLayout last = layouts[layouts.Count - 1];
            CadSheetSetHint.Write(database, File.Path, path, last.LayoutName, last.Handle, File.Clock());
            CadSheetSetDrawings.Save(database, path, true);
            string prefix = IoPath.GetFileNameWithoutExtension(path) + " - ";
            var sheets = new List<CadSheetSetSheet>();
            foreach (DxfLayout layout in layouts)
            {
                CadSheetSetSheet sheet = AddSheet(
                    string.Empty,
                    (prefixTitleWithFileName ? prefix : string.Empty) + layout.LayoutName,
                    CadSheetSetDrawings.Reference(path, File.Folder, layout)
                );
                GiveSheetProperties(sheet);
                sheets.Add(sheet);
            }

            return sheets;
        }

        /// <summary>This group and the groups it is in, up to the sheet set, nearest first.</summary>
        private IEnumerable<XElement> Ancestry()
        {
            for (XElement e = Element; e != null && IsContainer(e); e = e.Parent)
            {
                yield return e;
            }
        }

        /// <summary>The folder new sheets of this group are saved in: the nearest sheet location of this group or the groups above it, as it names the folder even where it is not there yet; the folder of the .dst where none is set.</summary>
        private string LocationFolder() =>
            Ancestry()
                .Select(e => CadDstTree.ReadFile(CadDstTree.FindObject(e, "NewSheetLocation"))?.Locate(File.Folder))
                .FirstOrDefault(p => p != null)
            ?? File.Folder;

        /// <summary>Gives a new sheet every sheet property of the set at its default value.</summary>
        private void GiveSheetProperties(CadSheetSetSheet sheet)
        {
            foreach (CadSheetSetProperty property in File.SheetSet.CustomProperties.Where(p => p.IsSheetProperty))
            {
                sheet.SetCustomProperty(property.Name, property.Value, CadSheetSetPropertyFlags.Sheet);
            }
        }
    }
}
