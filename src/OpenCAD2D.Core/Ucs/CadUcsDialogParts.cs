using GeometryHelper.Geometry;

namespace OpenCAD2D
{
    /// <summary>What a row of the Named UCSs list stands for.</summary>
    public enum CadUcsRowKind
    {
        /// <summary>The current system when it has no name and is not the world: listed first, only then.</summary>
        Unnamed,

        /// <summary>The world coordinate system.</summary>
        World,

        /// <summary>The system before the last change, which Set Current goes back to, as UCS Previous.</summary>
        Previous,

        /// <summary>A system of the drawing's UCS table.</summary>
        Named,
    }

    /// <summary>One row of the Named UCSs list of the UCS dialog, with what it can be given: Unnamed, World, Previous or a named system.</summary>
    public sealed class CadUcsDialogRow
    {
        internal CadUcsDialogRow(CadUcsRowKind kind, string name, CadUcs system, string original)
        {
            Kind = kind;
            Name = name;
            System = system;
            Original = original;
        }

        /// <summary>Gets what the row stands for.</summary>
        public CadUcsRowKind Kind { get; internal set; }

        /// <summary>Gets the name the list shows: <c>Unnamed</c>, <c>World</c>, <c>Previous</c> or the name of the system as the dialog has it now.</summary>
        public string Name { get; internal set; }

        /// <summary>Gets the system the row stands for; for Previous the system Set Current would go back to, the world where there is none.</summary>
        public CadUcs System { get; internal set; }

        /// <summary>Gets whether the row is the current system as the dialog has it now: the black triangle of the list.</summary>
        public bool IsCurrent { get; internal set; }

        /// <summary>Gets whether the row can be renamed: the named systems, and Unnamed, which saves the current system under the name.</summary>
        public bool CanRename => Kind == CadUcsRowKind.Named || Kind == CadUcsRowKind.Unnamed;

        /// <summary>Gets whether the row can be deleted: the named systems only.</summary>
        public bool CanDelete => Kind == CadUcsRowKind.Named;

        /// <summary>The name the system has in the drawing; null for a row the dialog made by saving the unnamed current system.</summary>
        internal string Original { get; }

        /// <inheritdoc/>
        public override string ToString() => Name;
    }

    /// <summary>One row of the Orthographic UCSs tab: Top, Bottom, Front, Back, Left or Right and the depth of its plane.</summary>
    public sealed class CadUcsOrthoRow
    {
        internal CadUcsOrthoRow(string name, GeoVector3 xAxis, GeoVector3 yAxis)
        {
            Name = name;
            XAxis = xAxis;
            YAxis = yAxis;
        }

        /// <summary>Gets the name of the row: Top, Bottom, Front, Back, Left or Right.</summary>
        public string Name { get; }

        /// <summary>
        /// Gets the depth: the distance of the plane of the system from the origin of the system it is relative to, along the
        /// normal of the plane, in millimetres. Zero until set; Reset gives zero again.
        /// </summary>
        public double Depth { get; internal set; }

        /// <summary>The X axis of the system, in the axes of the system it is relative to.</summary>
        internal GeoVector3 XAxis { get; }

        /// <summary>The Y axis of the system, in the axes of the system it is relative to.</summary>
        internal GeoVector3 YAxis { get; }

        /// <inheritdoc/>
        public override string ToString() => Name;
    }

    /// <summary>A name the UCS dialog refuses, as the message box of AutoCAD 2021 shows it, with its Close button.</summary>
    public sealed class CadUcsDialogError
    {
        internal CadUcsDialogError(string title, string message, string detail)
        {
            Title = title;
            Message = message;
            Detail = detail;
        }

        /// <summary>Gets the title of the message: <c>UCS - Name Already Exists</c>, <c>UCS - Text Length</c> or <c>UCS - Unsupported Characters</c>.</summary>
        public string Title { get; }

        /// <summary>Gets the line in bold: <c>The name, Org100, is already in use.</c>.</summary>
        public string Message { get; }

        /// <summary>Gets the line under it; empty where the message has none.</summary>
        public string Detail { get; }
    }

    /// <summary>
    /// What the UCS Details dialog shows of a system: its name, its origin and its three axes, each in the coordinates of the
    /// system the dialog is relative to. For the Org100 system (origin (35.3553, 106.0660), turned 45 degrees) relative to
    /// World the origin is (35.3553, 106.0660, 0) and the X axis (0.7071, 0.7071, 0).
    /// </summary>
    public sealed class CadUcsDetails
    {
        internal CadUcsDetails(string name, GeoPoint3 origin, GeoVector3 xAxis, GeoVector3 yAxis, GeoVector3 zAxis)
        {
            Name = name;
            Origin = origin;
            XAxis = xAxis;
            YAxis = yAxis;
            ZAxis = zAxis;
        }

        /// <summary>Gets the name of the system.</summary>
        public string Name { get; }

        /// <summary>Gets the origin, relative to the system chosen.</summary>
        public GeoPoint3 Origin { get; }

        /// <summary>Gets the X axis, of unit length, relative to the system chosen.</summary>
        public GeoVector3 XAxis { get; }

        /// <summary>Gets the Y axis, of unit length, relative to the system chosen.</summary>
        public GeoVector3 YAxis { get; }

        /// <summary>Gets the Z axis, of unit length, relative to the system chosen.</summary>
        public GeoVector3 ZAxis { get; }
    }
}
