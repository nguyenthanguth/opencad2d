using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using GeometryHelper;
using GeometryHelper.Dxf;

namespace OpenCAD2D.SheetSets
{
    /// <summary>
    /// The mark a sheet set leaves in the drawing of one of its sheets, AutoCAD's <c>AcSheetSetData</c>: a dictionary of
    /// seven xrecords in the drawing's named object dictionary that names the sheet set's file, the drawing's own file, the
    /// layout that is the sheet (by name and by handle), the version of the mark, how many times it has been written and
    /// when. AutoCAD finds the set of a drawing it opens from it; so does SSLOCATE here. New Sheet, Import Layout as Sheet,
    /// Rename &amp; Renumber and Resave All Sheets write it.
    /// </summary>
    /// <remarks>
    /// A drawing holds one mark, so a drawing with several layouts that are sheets of the set carries the mark of the one
    /// written last. The layout's handle is written in lower-case hexadecimal, as AutoCAD writes it in the mark; the sheet
    /// set's own file keeps it in upper case.
    /// </remarks>
    public sealed class CadSheetSetHint
    {
        internal const string DictionaryName = "AcSheetSetData";
        private const string TimeFormat = "yyyy/MM/dd HH:mm:ss.fff";

        private CadSheetSetHint(
            string sheetSetFileName,
            string sheetDrawingName,
            string layoutName,
            string layoutHandle,
            int version,
            int updateCount,
            string updateTime
        )
        {
            SheetSetFileName = sheetSetFileName;
            SheetDrawingName = sheetDrawingName;
            LayoutName = layoutName;
            LayoutHandle = layoutHandle;
            Version = version;
            UpdateCount = updateCount;
            UpdateTime = updateTime;
        }

        /// <summary>Gets the full path of the sheet set's .dst (<c>ShSetFileName</c>).</summary>
        public string SheetSetFileName { get; }

        /// <summary>Gets the full path the drawing had when the mark was written (<c>SheetDwgName</c>).</summary>
        public string SheetDrawingName { get; }

        /// <summary>Gets the name of the layout that is the sheet (<c>LayoutName</c>).</summary>
        public string LayoutName { get; }

        /// <summary>Gets the handle of that layout in lower-case hexadecimal (<c>LayoutHandle</c>).</summary>
        public string LayoutHandle { get; }

        /// <summary>Gets the version of the mark (<c>ShSetVersion</c>), 2 as AutoCAD 2021 writes it.</summary>
        public int Version { get; }

        /// <summary>Gets how many times the mark has been written (<c>UpdateCount</c>).</summary>
        public int UpdateCount { get; }

        /// <summary>Gets when it was last written, in UTC, as <c>2026/11/26 14:30:05.123</c> (<c>UpdateTime</c>).</summary>
        public string UpdateTime { get; }

        /// <summary>Reads the mark in a drawing.</summary>
        /// <param name="database">The drawing.</param>
        /// <returns>The mark; null where the drawing has none, or none that names a sheet set.</returns>
        /// <exception cref="ArgumentNullException">Thrown when the drawing is null.</exception>
        public static CadSheetSetHint Read(DxfDatabase database)
        {
            if (database == null)
            {
                throw new ArgumentNullException(nameof(database));
            }

            DxfDBDictionary mark;

            try
            {
                mark = Find(database);
            }
            catch (InvalidOperationException)
            {
                // A drawing whose objects cannot be read holds no mark that names a set.
                return null;
            }

            if (mark == null)
            {
                return null;
            }

            string set = Text(mark, "ShSetFileName");
            return set.Length == 0
                ? null
                : new CadSheetSetHint(
                    set,
                    Text(mark, "SheetDwgName"),
                    Text(mark, "LayoutName"),
                    Text(mark, "LayoutHandle"),
                    Number(mark, "ShSetVersion"),
                    Number(mark, "UpdateCount"),
                    Text(mark, "UpdateTime")
                );
        }

        /// <summary>Reads the mark in a drawing file, as <see cref="Read(DxfDatabase)"/> does once the file is read.</summary>
        /// <param name="drawingFile">The path of the DWG or DXF.</param>
        /// <returns>The mark; null where the drawing has none.</returns>
        /// <exception cref="ArgumentNullException">Thrown when the path is null.</exception>
        public static CadSheetSetHint Read(string drawingFile)
        {
            if (drawingFile == null)
            {
                throw new ArgumentNullException(nameof(drawingFile));
            }

            return Read(CadDrawingFile.Open(drawingFile));
        }

        /// <summary>
        /// Writes the mark into a drawing: the seven xrecords, <c>ShSetVersion</c> 2, <c>UpdateCount</c> one more than the
        /// mark there was (1 for a drawing with none) and <c>UpdateTime</c> the time given (UTC, as AutoCAD writes it).
        /// </summary>
        internal static void Write(
            DxfDatabase database,
            string sheetSetFile,
            string drawingFile,
            string layoutName,
            string layoutHandle,
            DateTime updated
        )
        {
            DxfDBDictionary names = database.NamedObjectsDictionary;
            if (names == null)
            {
                throw new NotSupportedException(
                    "A drawing of this version has no named object dictionary to hold the mark."
                );
            }

            int count = Number(Find(database), "UpdateCount") + 1;
            DxfDBDictionary mark = Find(database);
            if (mark == null)
            {
                mark = new DxfDBDictionary();
                names.SetAt(DictionaryName, mark);
            }

            Put(mark, "LayoutHandle", 1, (layoutHandle ?? string.Empty).ToLowerInvariant());
            Put(mark, "LayoutName", 1, layoutName);
            Put(mark, "SheetDwgName", 1, drawingFile);
            Put(mark, "ShSetFileName", 1, sheetSetFile);
            Put(mark, "ShSetVersion", 90, "2");
            Put(mark, "UpdateCount", 90, count.ToString(CultureInfo.InvariantCulture));
            Put(mark, "UpdateTime", 1, updated.ToString(TimeFormat, CultureInfo.InvariantCulture));
        }

        private static DxfDBDictionary Find(DxfDatabase database)
        {
            DxfDBDictionary names = database.NamedObjectsDictionary;
            return names != null && names.Contains(DictionaryName)
                ? names.GetAt(DictionaryName) as DxfDBDictionary
                : null;
        }

        private static IReadOnlyList<DxfTypedValue> Data(DxfDBDictionary mark, string name) =>
            mark != null && mark.Contains(name) && mark.GetAt(name) is DxfXrecord record
                ? record.Data
                : Array.Empty<DxfTypedValue>();

        private static string Text(DxfDBDictionary mark, string name) =>
            Data(mark, name).Where(g => g.TypeCode == 1).Select(g => g.Value).FirstOrDefault() ?? string.Empty;

        private static int Number(DxfDBDictionary mark, string name) =>
            int.TryParse(
                Data(mark, name).Where(g => g.TypeCode == 90).Select(g => g.Value).FirstOrDefault(),
                NumberStyles.Integer,
                CultureInfo.InvariantCulture,
                out int value
            )
                ? value
                : 0;

        private static void Put(DxfDBDictionary mark, string name, int code, string value)
        {
            var data = new[] { new DxfTypedValue(code, value ?? string.Empty) };
            if (mark.Contains(name) && mark.GetAt(name) is DxfXrecord existing)
            {
                existing.Data = data;
            }
            else
            {
                mark.SetAt(name, new DxfXrecord { Data = data });
            }
        }
    }
}
