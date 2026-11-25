using System;

namespace OpenCAD2D.SheetSets
{
    /// <summary>
    /// The owner bits of a custom property of a sheet set, the <c>PropertyFlags</c> of AutoCAD's sheet set API (read from the
    /// type library of <c>AcSmComponents</c>). A property is defined in the sheet set's own list: <see cref="Set"/> for one
    /// the set holds, <see cref="Sheet"/> for one every sheet holds (the set's entry carries the default); each sheet's list
    /// then carries its own value. The bits this program does not write, <see cref="Subset"/> and <see cref="Child"/>, are
    /// kept as a file has them.
    /// </summary>
    [Flags]
    public enum CadSheetSetPropertyFlags
    {
        /// <summary>No owner bit.</summary>
        None = 0,

        /// <summary>The property is the sheet set's own (<c>CUSTOM_SHEETSET_PROP</c>).</summary>
        Set = 1,

        /// <summary>The property is held by every sheet (<c>CUSTOM_SHEET_PROP</c>).</summary>
        Sheet = 2,

        /// <summary>The property is held by every subset (<c>CUSTOM_SUBSET_PROP</c>).</summary>
        Subset = 4,

        /// <summary>The property is a child of another (<c>IS_CHILD</c>).</summary>
        Child = 8,
    }

    /// <summary>
    /// A custom property of a sheet set, a subset or a sheet: a name, a text value and the owner bits. Read through
    /// <see cref="CadSheetComponent.CustomProperties"/>; set through <see cref="CadSheetComponent.SetCustomProperty"/>. A
    /// property whose value is not text (the transmittal setups AutoCAD keeps in the set's list) is not listed.
    /// </summary>
    public sealed class CadSheetSetProperty
    {
        internal CadSheetSetProperty(string name, string value, CadSheetSetPropertyFlags flags)
        {
            Name = name;
            Value = value;
            Flags = flags;
        }

        /// <summary>Gets the name, as written (a field names it with the letter case it was given).</summary>
        public string Name { get; }

        /// <summary>Gets the value; empty where the file holds none.</summary>
        public string Value { get; }

        /// <summary>Gets the owner bits.</summary>
        public CadSheetSetPropertyFlags Flags { get; }

        /// <summary>Gets whether the property is the sheet set's own.</summary>
        public bool IsSetProperty => (Flags & CadSheetSetPropertyFlags.Set) != 0;

        /// <summary>Gets whether every sheet holds the property.</summary>
        public bool IsSheetProperty => (Flags & CadSheetSetPropertyFlags.Sheet) != 0;
    }
}
