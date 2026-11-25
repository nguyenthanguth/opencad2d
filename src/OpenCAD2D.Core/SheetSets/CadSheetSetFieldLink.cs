using System;
using GeometryHelper.Dxf;

namespace OpenCAD2D.SheetSets
{
    /// <summary>
    /// What a sheet's drawing keeps of its sheet set, as AutoCAD writes it: the named object dictionary <c>AcSheetSetData</c> of
    /// seven xrecords. A field of the set reads the set's file, the drawing's own file, and the name and handle of the layout
    /// from it. This is the small reader the fields need; the writer of the link belongs to the operations on sheets.
    /// </summary>
    internal sealed class CadSheetSetFieldLink
    {
        private CadSheetSetFieldLink() { }

        /// <summary>Gets the path of the set's file (<c>ShSetFileName</c>), as it was when the link was last written.</summary>
        internal string SetFile { get; private set; }

        /// <summary>Gets the path of the drawing (<c>SheetDwgName</c>), as it was when the link was last written.</summary>
        internal string DrawingFile { get; private set; }

        /// <summary>Gets the name of the layout that is the sheet (<c>LayoutName</c>).</summary>
        internal string LayoutName { get; private set; }

        /// <summary>Gets the handle of the layout (<c>LayoutHandle</c>), in lower case as AutoCAD writes it.</summary>
        internal string LayoutHandle { get; private set; }

        /// <summary>Reads the link of a drawing.</summary>
        /// <param name="database">The drawing.</param>
        /// <returns>The link; null where the drawing has none or it names no set file.</returns>
        internal static CadSheetSetFieldLink Read(DxfDatabase database)
        {
            try
            {
                DxfDBDictionary named = database.NamedObjectsDictionary;

                if (
                    named == null
                    || !named.Contains("AcSheetSetData")
                    || !(named.GetAt("AcSheetSetData") is DxfDBDictionary dictionary)
                )
                {
                    return null;
                }

                var link = new CadSheetSetFieldLink
                {
                    SetFile = Text(dictionary, "ShSetFileName"),
                    DrawingFile = Text(dictionary, "SheetDwgName"),
                    LayoutName = Text(dictionary, "LayoutName"),
                    LayoutHandle = Text(dictionary, "LayoutHandle"),
                };
                return string.IsNullOrEmpty(link.SetFile) ? null : link;
            }
            catch (InvalidOperationException)
            {
                return null;
            }
        }

        private static string Text(DxfDBDictionary dictionary, string name)
        {
            if (!dictionary.Contains(name) || !(dictionary.GetAt(name) is DxfXrecord record))
            {
                return null;
            }

            foreach (DxfTypedValue group in record.Data)
            {
                if (group.TypeCode == 1)
                {
                    return group.Value;
                }
            }

            return null;
        }
    }
}
