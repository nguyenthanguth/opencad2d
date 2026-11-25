using GeometryHelper.Geometry;

namespace OpenCAD2D.Remote
{
    /// <summary>
    /// A box of the drawing: the extents the header of a drawing holds, as <see cref="CadRemoteDocument.Extents"/> reads them,
    /// or the part of the space shown that the view shows, as <see cref="CadRemoteDocument.CurrentView"/> reads it: the least and
    /// the greatest corner, in the units of the drawing. A drawing with nothing drawn has the empty box a header starts with,
    /// whose least corner is above its greatest, which <see cref="IsEmpty"/> says.
    /// </summary>
    public sealed class CadRemoteExtents
    {
        internal CadRemoteExtents(GeoPoint3 least, GeoPoint3 greatest)
        {
            Min = least;
            Max = greatest;
        }

        /// <summary>Gets the least corner (<c>EXTMIN</c>).</summary>
        public GeoPoint3 Min { get; }

        /// <summary>Gets the greatest corner (<c>EXTMAX</c>).</summary>
        public GeoPoint3 Max { get; }

        /// <summary>Gets whether the box is the empty one: the least corner is above the greatest in X.</summary>
        public bool IsEmpty => Min.X > Max.X;
    }
}
