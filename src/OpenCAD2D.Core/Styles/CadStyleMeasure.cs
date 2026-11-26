using GeometryHelper.Geometry;

namespace OpenCAD2D
{
    /// <summary>
    /// A distance (or an angle) a field of a style dialog sets, measured on the sample so that the preview can show it as a
    /// callout: the two points it runs between in the sample's drawing coordinates, the variable and the value as the field
    /// shows it. The sample's own scale is in the points: at DIMSCALE 25 the measure of DIMEXE 1.25 is 31.25 long, its label
    /// still reads 1.25.
    /// </summary>
    public sealed class CadStyleMeasure
    {
        internal CadStyleMeasure(string key, GeoPoint2 from, GeoPoint2 to, double? angle, string label)
        {
            Key = key;
            From = from;
            To = to;
            Angle = angle;
            Label = label;
        }

        /// <summary>Gets the key of the field it belongs to, as <c>DIMEXE</c>.</summary>
        public string Key { get; }

        /// <summary>Gets where it starts, in the drawing coordinates of the sample; for a point alone (an attachment) where the point is, <see cref="To"/> the same.</summary>
        public GeoPoint2 From { get; }

        /// <summary>Gets where it ends, in the drawing coordinates of the sample.</summary>
        public GeoPoint2 To { get; }

        /// <summary>
        /// Gets the angle in radians of an angle callout, an arc about <see cref="From"/> of the radius <c>|To - From|</c> that
        /// starts towards <see cref="To"/> and runs through this angle, counter-clockwise where it is above nought; null for a distance.
        /// </summary>
        public double? Angle { get; }

        /// <summary>Gets the label as the preview writes it: the variable and the value as the field shows it, as <c>DIMEXE = 1.25</c>.</summary>
        public string Label { get; }

        /// <inheritdoc/>
        public override string ToString() => Label;
    }
}
