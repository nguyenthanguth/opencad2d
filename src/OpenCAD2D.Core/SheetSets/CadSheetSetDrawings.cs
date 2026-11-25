using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using GeometryHelper;
using GeometryHelper.Dxf;
using IoPath = System.IO.Path;

namespace OpenCAD2D.SheetSets
{
    /// <summary>
    /// What the sheet set operations do to the drawings on disk: reading one without a window, finding a layout in it by its
    /// handle and then its name (D7), checking whether a layout may become a sheet, naming files and layouts, and saving a
    /// drawing as a DWG of AutoCAD 2018 without ever replacing a file with one that has lost something.
    /// </summary>
    internal static class CadSheetSetDrawings
    {
        /// <summary>The layouts of a drawing that can be sheets (every layout but Model), in the order of their tabs, a tie by name.</summary>
        internal static List<DxfLayout> SheetLayouts(DxfDatabase database)
        {
            List<DxfLayout> layouts = database
                .Layouts.Where(l =>
                    !string.Equals(l.LayoutName, "Model", StringComparison.OrdinalIgnoreCase)
                    && l.BlockTableRecord != null
                )
                .ToList();
            layouts.Sort(
                (a, b) =>
                {
                    int byTab = a.TabOrder.CompareTo(b.TabOrder);
                    return byTab != 0 ? byTab : string.CompareOrdinal(a.LayoutName, b.LayoutName);
                }
            );
            return layouts;
        }

        /// <summary>Finds a layout of a drawing by its handle, and where that is gone or not given by its name (letter case ignored); null where neither is there. Sets whether the handle found it.</summary>
        internal static DxfLayout Find(DxfDatabase database, string handle, string name, out bool byHandle)
        {
            List<DxfLayout> layouts = SheetLayouts(database);
            byHandle = false;
            if (!string.IsNullOrEmpty(handle))
            {
                DxfLayout found = layouts.FirstOrDefault(l =>
                    string.Equals(l.Handle, handle, StringComparison.OrdinalIgnoreCase)
                );
                if (found != null)
                {
                    byHandle = true;
                    return found;
                }
            }

            return string.IsNullOrEmpty(name)
                ? null
                : layouts.FirstOrDefault(l => string.Equals(l.LayoutName, name, StringComparison.OrdinalIgnoreCase));
        }

        /// <summary>The layout of a name in a drawing, letter case ignored; null where there is none.</summary>
        internal static DxfLayout Find(DxfDatabase database, string name) => Find(database, null, name, out _);

        /// <summary>Reads a drawing file and checks its layouts for import into a set (see <see cref="Check"/>); a drawing that is not a DWG has none that can be sheets.</summary>
        internal static List<CadSheetImportLayout> CheckFile(string drawingFile, CadSheetSet into)
        {
            if (drawingFile == null)
            {
                throw new ArgumentNullException(nameof(drawingFile));
            }

            string path = Full(drawingFile);
            if (!File.Exists(path))
            {
                throw new FileNotFoundException("The drawing " + path + " is not there.", path);
            }

            return CadDrawingFile.IsDwgName(path)
                ? Check(CadDrawingFile.Open(path), path, into, null)
                : new List<CadSheetImportLayout>();
        }

        /// <summary>
        /// The layouts of a drawing with the reason each cannot become a sheet: a # in its name; a sheet of the set it would go
        /// into (but <paramref name="ignore"/>); a sheet of the set the drawing's mark names, where that is another set, is
        /// there and still lists it.
        /// </summary>
        internal static List<CadSheetImportLayout> Check(
            DxfDatabase database,
            string drawingPath,
            CadSheetSet into,
            CadSheetSetSheet ignore
        )
        {
            CadSheetSetHint hint = CadSheetSetHint.Read(database);
            CadSheetSetFile otherFile = null;
            if (
                hint != null
                && (into == null || !Same(hint.SheetSetFileName, into.File.Path))
                && File.Exists(hint.SheetSetFileName)
            )
            {
                try
                {
                    otherFile = CadSheetSetFile.Open(hint.SheetSetFileName);
                }
                catch (Exception e) when (e is FormatException || e is IOException || e is UnauthorizedAccessException)
                {
                    // A mark that names a file that is no sheet set, or one that cannot be read, names no other set.
                }
            }

            CadSheetSet other = otherFile?.SheetSet;
            var checks = new List<CadSheetImportLayout>();
            foreach (DxfLayout layout in SheetLayouts(database))
            {
                string refusal = null;
                if (layout.LayoutName.IndexOf('#') >= 0)
                {
                    refusal = "The layout name \"" + layout.LayoutName + "\" holds a #, which a sheet set cannot use.";
                }
                else if (into != null && Owns(into, database, drawingPath, layout, ignore))
                {
                    refusal = AlreadyASheet(layout, into);
                }
                else if (other != null && Owns(other, database, drawingPath, layout, null))
                {
                    refusal = AlreadyASheet(layout, other);
                }

                checks.Add(new CadSheetImportLayout(layout.LayoutName, layout.Handle, refusal));
            }

            return checks;
        }

        private static string AlreadyASheet(DxfLayout layout, CadSheetSet set) =>
            "The layout \""
            + layout.LayoutName
            + "\" is already a sheet of the sheet set \""
            + set.Name
            + "\" ("
            + set.File.Path
            + ").";

        /// <summary>Whether a sheet of the set (but one) is a layout of the drawing, found as <see cref="CadSheetSetSheet.FindLayout"/> finds it.</summary>
        private static bool Owns(
            CadSheetSet set,
            DxfDatabase database,
            string drawingPath,
            DxfLayout layout,
            CadSheetSetSheet ignore
        )
        {
            foreach (CadSheetSetSheet sheet in set.AllSheets())
            {
                CadDrawingReference reference = sheet.Layout;
                if (reference == null || (ignore != null && sheet.Element == ignore.Element))
                {
                    continue;
                }

                string file = reference.File.Resolve(set.File.Folder);
                if (
                    file != null
                    && Same(file, drawingPath)
                    && string.Equals(
                        Find(database, reference.Handle, reference.Name, out _)?.Handle,
                        layout.Handle,
                        StringComparison.OrdinalIgnoreCase
                    )
                )
                {
                    return true;
                }
            }

            return false;
        }

        /// <summary>The reference to a layout a sheet holds: the drawing's file against the set's folder, the layout's name, handle and owner.</summary>
        internal static CadDrawingReference Reference(string drawingFile, string setFolder, DxfLayout layout) =>
            new CadDrawingReference(
                CadFileReference.FromPath(drawingFile, setFolder),
                layout.LayoutName,
                layout.Handle,
                layout.OwnerHandle
            );

        /// <summary>The full path of a path, trimmed of its closing separators, for comparing two of them (letter case ignored by the caller).</summary>
        internal static string Full(string path) => IoPath.GetFullPath(path).TrimEnd('\\', '/');

        /// <summary>Whether two paths name the same place, letter case ignored; a text that is no path names no place.</summary>
        internal static bool Same(string a, string b)
        {
            if (a == null || b == null)
            {
                return false;
            }

            try
            {
                return string.Equals(Full(a), Full(b), StringComparison.OrdinalIgnoreCase);
            }
            catch (Exception e) when (e is ArgumentException || e is NotSupportedException || e is PathTooLongException)
            {
                return false;
            }
        }

        /// <summary>The name of a sheet's file or layout when it is made from its number and title: "A-101 Ground floor", or the title alone for a sheet with no number.</summary>
        internal static string Joined(string number, string title) =>
            string.IsNullOrEmpty(number)
                ? title ?? string.Empty
                : number + (string.IsNullOrEmpty(title) ? string.Empty : " " + title);

        /// <summary>A text as a file name: each character a file cannot hold made an underscore, the ends trimmed of spaces and dots.</summary>
        internal static string AsFileName(string text) =>
            Replace(text, IoPath.GetInvalidFileNameChars()).Trim(' ', '.');

        /// <summary>A text as a layout name: each character a layout cannot hold, and the # a sheet set cannot, made an underscore.</summary>
        internal static string AsLayoutName(string text) =>
            Replace(text, (CadCommands.LayoutBadCharacters + "#").ToCharArray()).Trim();

        /// <summary>Why a name is no layout's (empty, over 255 characters, a character a layout cannot hold, a # or the name Model), or null where it fits.</summary>
        internal static string WhyNotLayoutName(string name)
        {
            if (string.IsNullOrWhiteSpace(name))
            {
                return "A layout needs a name.";
            }

            if (name.Length > 255)
            {
                return "A layout name is at most 255 characters.";
            }

            if (name.IndexOfAny(CadCommands.LayoutBadCharacters.ToCharArray()) >= 0)
            {
                return "The layout name \""
                    + name
                    + "\" holds a character a layout cannot: "
                    + CadCommands.LayoutBadCharacters;
            }

            if (name.IndexOf('#') >= 0)
            {
                return "The layout name \"" + name + "\" holds a #, which a sheet set cannot use.";
            }

            return string.Equals(name, "Model", StringComparison.OrdinalIgnoreCase)
                ? "Model space cannot be a sheet."
                : null;
        }

        /// <summary>The file name a sheet is saved under: the name given, or the default, with .dwg at its end where it has none.</summary>
        internal static string DwgName(string fileName)
        {
            if (fileName.IndexOfAny(IoPath.GetInvalidFileNameChars()) >= 0)
            {
                throw new ArgumentException(
                    "The file name \"" + fileName + "\" holds a character a file cannot.",
                    nameof(fileName)
                );
            }

            return fileName.EndsWith(".dwg", StringComparison.OrdinalIgnoreCase) ? fileName : fileName + ".dwg";
        }

        /// <summary>
        /// Saves a drawing as a DWG of AutoCAD 2018 at a path: written beside it first and put in its place once written. Where
        /// the DWG writer would leave a kind of entity out and the caller asks it, nothing is replaced and the save is refused,
        /// so that making a drawing a sheet never loses what the user drew.
        /// </summary>
        internal static void Save(DxfDatabase database, string path, bool refuseLoss)
        {
            string temp = path + ".sheetset.tmp.dwg";
            try
            {
                string left = CadDrawingFile.Save(database, temp);
                if (refuseLoss && left.Length > 0)
                {
                    throw new NotSupportedException(
                        "The drawing "
                            + IoPath.GetFileName(path)
                            + " cannot be saved as a DWG without losing "
                            + left
                            + "; it is left as it was."
                    );
                }

                if (File.Exists(path))
                {
                    File.Replace(temp, path, null);
                }
                else
                {
                    File.Move(temp, path);
                }
            }
            finally
            {
                try
                {
                    File.Delete(temp);
                }
                catch (IOException)
                {
                    // A temporary file that cannot be deleted is left; the save went on or failed for its own reason.
                }
            }
        }

        /// <summary>
        /// Binds the unbound <c>\AcSm</c> fields of the layouts that have become sheets once the set that holds them is saved (the
        /// fields come to the sheets' own words only then): the drawing is read as the app reads one, each layout's fields are
        /// bound (<see cref="CadSheetSetFields.Bind"/>) and, where any was, the drawing is saved again. A drawing that cannot be
        /// read or saved again is left as it is, said in the log: its fields stay unbound and still show the sheet's words.
        /// </summary>
        internal static void BindAfterSave(
            CadSheetSetFile file,
            string drawingPath,
            IReadOnlyList<KeyValuePair<string, CadSheetSetSheet>> sheets,
            bool refuseLoss
        ) =>
            file.AfterSave(() =>
            {
                try
                {
                    CadDocument document = CadDocument.Open(drawingPath);
                    int bound = 0;

                    foreach (KeyValuePair<string, CadSheetSetSheet> sheet in sheets)
                    {
                        bound += CadSheetSetFields.Bind(document, sheet.Key, file, sheet.Value);
                    }

                    if (bound > 0)
                    {
                        Save(document.Database, drawingPath, refuseLoss);
                    }
                }
                catch (Exception e)
                    when (e is IOException
                        || e is UnauthorizedAccessException
                        || e is NotSupportedException
                        || e is InvalidOperationException
                        || e is FormatException
                    )
                {
                    GeometryHelperLog.Warn(
                        "Sheet set: the fields of " + drawingPath + " were not bound to its sheets: " + e.Message
                    );
                }
            });

        private static string Replace(string text, char[] bad)
        {
            char[] letters = (text ?? string.Empty).ToCharArray();
            for (int i = 0; i < letters.Length; i++)
            {
                if (Array.IndexOf(bad, letters[i]) >= 0 || char.IsControl(letters[i]))
                {
                    letters[i] = '_';
                }
            }

            return new string(letters);
        }
    }
}
