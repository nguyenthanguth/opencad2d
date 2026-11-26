using System.Collections.Generic;

namespace OpenCAD2D.Remote
{
    /// <summary>
    /// What the Properties palette of the app shows for some entities (<see cref="CadRemoteDocument.GetProperties"/>): a label such as
    /// <c>Line (1)</c> and the rows in groups (General, Geometry, Text, Misc, Attributes), each value written as the palette writes it.
    /// </summary>
    public sealed class CadRemoteProperties
    {
        internal CadRemoteProperties(string label, IReadOnlyList<CadRemotePropertyGroup> groups)
        {
            Label = label;
            Groups = groups;
        }

        /// <summary>Gets the box of the palette's label: <c>Line (1)</c>, <c>All (3)</c> for several kinds, <c>No selection</c> for none.</summary>
        public string Label { get; }

        /// <summary>Gets the groups, in the order the palette lists them.</summary>
        public IReadOnlyList<CadRemotePropertyGroup> Groups { get; }
    }

    /// <summary>A group of rows of the Properties palette, such as <c>Geometry</c>.</summary>
    public sealed class CadRemotePropertyGroup
    {
        internal CadRemotePropertyGroup(string title, IReadOnlyList<CadRemotePropertyRow> rows)
        {
            Title = title;
            Rows = rows;
        }

        /// <summary>Gets the title of the group.</summary>
        public string Title { get; }

        /// <summary>Gets the rows, in the order the palette lists them.</summary>
        public IReadOnlyList<CadRemotePropertyRow> Rows { get; }
    }

    /// <summary>A row of the Properties palette: a label and the value as the palette writes it.</summary>
    public sealed class CadRemotePropertyRow
    {
        internal CadRemotePropertyRow(string label, string value, bool isReadOnly)
        {
            Label = label;
            Value = value;
            IsReadOnly = isReadOnly;
        }

        /// <summary>Gets the label, such as <c>Length</c>.</summary>
        public string Label { get; }

        /// <summary>Gets the value as the palette writes it, such as <c>500.0000</c>; <c>*Varies*</c> where several entities differ.</summary>
        public string Value { get; }

        /// <summary>Gets whether the palette shows the row as one that cannot be changed.</summary>
        public bool IsReadOnly { get; }
    }
}
