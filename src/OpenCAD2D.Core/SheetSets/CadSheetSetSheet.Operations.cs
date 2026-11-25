using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using GeometryHelper;
using GeometryHelper.Dxf;
using IoFile = System.IO.File;
using IoPath = System.IO.Path;

namespace OpenCAD2D.SheetSets
{
    public sealed partial class CadSheetSetSheet
    {
        /// <summary>
        /// The name a new sheet's drawing is saved under unless another is typed: its number, a space and its title
        /// ("A-101 Ground floor plan"), the title alone for a sheet with no number, each character a file cannot hold made an
        /// underscore. It has no extension.
        /// </summary>
        /// <param name="number">The number; null or empty for none.</param>
        /// <param name="title">The title.</param>
        /// <returns>The name.</returns>
        public static string DefaultFileName(string number, string title) =>
            CadSheetSetDrawings.AsFileName(CadSheetSetDrawings.Joined(number, title));

        /// <summary>
        /// The name a new sheet's layout is given, and the name Rename &amp; Renumber offers: the number, a space and the
        /// title, the title alone for a sheet with no number, each character a layout cannot hold (and the #) made an
        /// underscore.
        /// </summary>
        /// <param name="number">The number; null or empty for none.</param>
        /// <param name="title">The title.</param>
        /// <returns>The name.</returns>
        public static string DefaultLayoutName(string number, string title) =>
            CadSheetSetDrawings.AsLayoutName(CadSheetSetDrawings.Joined(number, title));

        /// <summary>
        /// Finds the sheet's layout in its drawing (D7): the drawing is the one the sheet's file names (the relative form first),
        /// and the layout is the one that has the sheet's handle, else the one that has its name. A sheet whose drawing is gone,
        /// or whose drawing has neither, is missing. The drawing is read, not changed.
        /// </summary>
        /// <returns>What was found; never null.</returns>
        public CadFoundLayout FindLayout()
        {
            CadDrawingReference reference = Layout;
            string drawing = reference?.File.Resolve(File.Folder);
            if (drawing == null || !IoFile.Exists(drawing))
            {
                return new CadFoundLayout(CadSheetLayoutStatus.Missing, null, null, null, null);
            }

            DxfLayout layout = CadSheetSetDrawings.Find(
                CadDrawingFile.Open(drawing),
                reference.Handle,
                reference.Name,
                out bool byHandle
            );
            return layout == null
                ? new CadFoundLayout(CadSheetLayoutStatus.Missing, drawing, null, null, null)
                : new CadFoundLayout(
                    byHandle ? CadSheetLayoutStatus.FoundByHandle : CadSheetLayoutStatus.FoundByName,
                    drawing,
                    layout.LayoutName,
                    layout.Handle,
                    layout.OwnerHandle
                );
        }

        /// <summary>
        /// Makes the sheet another layout, as the Expected Layout row of the Sheet Properties does for a sheet that is missing:
        /// the sheet takes the layout's file, name, handle and owner, and the drawing gets the sheet set mark and is saved as
        /// a DWG of AutoCAD 2018.
        /// </summary>
        /// <param name="drawingFile">The path of the drawing, a DWG.</param>
        /// <param name="layoutName">The layout's name in it.</param>
        /// <exception cref="ArgumentNullException">Thrown when the drawing or the layout name is null.</exception>
        /// <exception cref="FileNotFoundException">Thrown when the drawing is not there.</exception>
        /// <exception cref="InvalidOperationException">Thrown outside <see cref="CadSheetSetFile.Change"/>, where the drawing has no such layout, or where the layout is refused as <see cref="CadSheetSet.CheckLayouts(string)"/> says (a layout this sheet is already is not).</exception>
        /// <exception cref="NotSupportedException">Thrown when the DWG writer would leave something of the drawing out; the drawing is not replaced.</exception>
        public void Associate(string drawingFile, string layoutName)
        {
            if (drawingFile == null)
            {
                throw new ArgumentNullException(nameof(drawingFile));
            }

            if (layoutName == null)
            {
                throw new ArgumentNullException(nameof(layoutName));
            }

            Edit();
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
            DxfLayout layout = CadSheetSetDrawings.Find(database, layoutName);
            if (layout == null)
            {
                throw new InvalidOperationException("The drawing " + path + " has no layout \"" + layoutName + "\".");
            }

            CadSheetImportLayout check = CadSheetSetDrawings
                .Check(database, path, File.SheetSet, this)
                .First(c => string.Equals(c.Handle, layout.Handle, StringComparison.Ordinal));
            if (check.IsRefused)
            {
                throw new InvalidOperationException(check.Refusal);
            }

            CadSheetSetHint.Write(database, File.Path, path, layout.LayoutName, layout.Handle, File.Clock());
            CadSheetSetDrawings.Save(database, path, true);
            Layout = CadSheetSetDrawings.Reference(path, File.Folder, layout);
        }

        /// <summary>
        /// Renames and renumbers the sheet as Rename &amp; Renumber Sheet does (B18). A new number and title are only written
        /// into the set. A new layout name renames the layout in the drawing, and a new file name renames the drawing in its
        /// folder, so that the drawing is read, given the sheet set mark, saved as a DWG of AutoCAD 2018 under the new name and
        /// the old file deleted; every sheet of the set that is a layout of that drawing follows the file. The drawing is only
        /// touched when its layout name or its file name changes. A drawing that is open in the app must be saved and closed
        /// by the caller first, and opened again after.
        /// </summary>
        /// <param name="number">The new number; empty for none.</param>
        /// <param name="title">The new title.</param>
        /// <param name="layoutName">The new layout name, or null to keep the layout's name.</param>
        /// <param name="fileName">The new file name, with or without .dwg, or null to keep the file's name; the folder stays.</param>
        /// <exception cref="ArgumentNullException">Thrown when the number or the title is null.</exception>
        /// <exception cref="ArgumentException">Thrown when the layout name is not one a layout can have (empty, a character it cannot hold, a # or Model), or the file name holds a character a file cannot.</exception>
        /// <exception cref="InvalidOperationException">Thrown outside <see cref="CadSheetSetFile.Change"/>, where a layout or file is to be renamed and the sheet is missing, or where the drawing has a layout of the new name.</exception>
        /// <exception cref="IOException">Thrown when a file of the new name is there already.</exception>
        /// <exception cref="NotSupportedException">Thrown when the DWG writer would leave something of the drawing out; the drawing is not replaced.</exception>
        public void RenameAndRenumber(string number, string title, string layoutName = null, string fileName = null)
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
            if (layoutName != null)
            {
                string reason = CadSheetSetDrawings.WhyNotLayoutName(layoutName);
                Guard.Argument(reason == null, nameof(layoutName), reason);
            }

            string newFile = fileName == null ? null : CadSheetSetDrawings.DwgName(fileName);
            CadDrawingReference reference = Layout;
            CadFoundLayout found = FindLayout();
            string currentLayout = found.IsMissing ? reference?.Name : found.LayoutName;
            string currentFile = found.DrawingFile ?? reference?.File.Locate(File.Folder);
            bool layoutChanges =
                layoutName != null && !string.Equals(layoutName, currentLayout, StringComparison.Ordinal);
            bool fileChanges =
                newFile != null
                && !string.Equals(
                    newFile,
                    currentFile == null ? null : IoPath.GetFileName(currentFile),
                    StringComparison.Ordinal
                );
            if ((layoutChanges || fileChanges) && found.IsMissing)
            {
                throw new InvalidOperationException(
                    "The sheet's layout is missing, so its layout and its drawing cannot be renamed."
                );
            }

            if (layoutChanges || fileChanges)
            {
                RenameInDrawing(found, layoutChanges ? layoutName : null, fileChanges ? newFile : null);
            }

            Number = number;
            Title = title;
        }

        /// <summary>Renames the found layout and moves its drawing: the drawing is read, changed, marked and saved under its new name, the old file deleted, and the sheets that are layouts of it named again.</summary>
        private void RenameInDrawing(CadFoundLayout found, string layoutName, string fileName)
        {
            string oldPath = found.DrawingFile;
            string newPath = fileName == null ? oldPath : IoPath.Combine(IoPath.GetDirectoryName(oldPath), fileName);
            bool moves = !string.Equals(newPath, oldPath, StringComparison.Ordinal);
            bool samePlace = CadSheetSetDrawings.Same(newPath, oldPath);
            if (moves && !samePlace && IoFile.Exists(newPath))
            {
                throw new IOException("The file " + newPath + " is there already.");
            }

            List<CadSheetSetSheet> followers = AllSheetsOf()
                .Where(s =>
                    s.Element != Element
                    && s.Layout != null
                    && CadSheetSetDrawings.Same(s.Layout.File.Resolve(File.Folder) ?? string.Empty, oldPath)
                )
                .ToList();
            DxfDatabase database = CadDrawingFile.Open(oldPath);
            DxfLayout layout = CadSheetSetDrawings.Find(database, found.LayoutHandle, found.LayoutName, out _);
            if (layoutName != null)
            {
                DxfLayout other = CadSheetSetDrawings.Find(database, layoutName);
                if (other != null && !string.Equals(other.Handle, layout.Handle, StringComparison.Ordinal))
                {
                    throw new InvalidOperationException(
                        "The drawing has a layout named \"" + layoutName + "\" already."
                    );
                }

                layout.LayoutName = layoutName;
            }

            CadSheetSetHint.Write(database, File.Path, newPath, layout.LayoutName, layout.Handle, File.Clock());
            if (moves && !samePlace)
            {
                CadSheetSetDrawings.Save(database, newPath, true);
                IoFile.Delete(oldPath);
            }
            else
            {
                CadSheetSetDrawings.Save(database, oldPath, true);
                if (moves)
                {
                    IoFile.Move(oldPath, newPath);
                }
            }

            Layout = CadSheetSetDrawings.Reference(newPath, File.Folder, layout);
            CadFileReference moved = CadFileReference.FromPath(newPath, File.Folder);
            foreach (CadSheetSetSheet follower in followers)
            {
                CadDrawingReference theirs = follower.Layout;
                follower.Layout = new CadDrawingReference(moved, theirs.Name, theirs.Handle, theirs.OwnerHandle);
            }
        }

        private IEnumerable<CadSheetSetSheet> AllSheetsOf() => File.SheetSet.AllSheets();
    }
}
