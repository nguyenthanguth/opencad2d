using System;
using System.Collections.Generic;
using GeometryHelper.Dxf;
using GeometryHelper.Geometry;

namespace OpenCAD2D
{
    /// <summary>
    /// A sample drawing of a style preview with its parts told apart: the dimensions (or the multileader) of the sample are
    /// drawn into model space of <see cref="Database"/> as the entities of their pictures, each with a handle of its own, so that
    /// the display list names the part an item belongs to (<see cref="PartOf"/>) and the preview lights it. A sample dimension
    /// is made by the builders the DIM commands use, its block's entities copied out into model space and the dimension erased:
    /// what is drawn is what the dimension drew.
    /// </summary>
    public sealed class CadStyleSample
    {
        private readonly Dictionary<string, CadStylePart> _byHandle = new Dictionary<string, CadStylePart>(
            StringComparer.Ordinal
        );

        internal CadStyleSample(
            DxfDatabase database,
            CadDimSampleKind kind,
            IReadOnlyList<CadStyleSamplePart> parts,
            IReadOnlyList<SampleShape> shapes,
            IReadOnlyList<string> untagged
        )
        {
            Database = database;
            Kind = kind;
            Parts = parts;
            Shapes = shapes;
            Untagged = untagged;

            foreach (CadStyleSamplePart part in parts)
            {
                foreach (string handle in part.Handles)
                {
                    if (!_byHandle.ContainsKey(handle))
                    {
                        _byHandle.Add(handle, part.Part);
                    }
                }
            }
        }

        /// <summary>Gets the drawing of the sample: its model space holds the outline and the parts, each its own entities.</summary>
        public DxfDatabase Database { get; }

        /// <summary>
        /// Gets the parts of the sample in the order they are drawn: one for each part of each dimension, with the handles of its
        /// entities. The words of a dimension whose style fills behind them are listed twice, as <see cref="CadStylePart.Text"/>
        /// and as <see cref="CadStylePart.TextFill"/>, the one entity.
        /// </summary>
        public IReadOnlyList<CadStyleSamplePart> Parts { get; }

        /// <summary>Gets the kind of dimension sample this is; <see cref="CadDimSampleKind.Overview"/> for a multileader's.</summary>
        internal CadDimSampleKind Kind { get; }

        /// <summary>Gets what the sample drew, as the callouts need it, in the order the shapes are drawn.</summary>
        internal IReadOnlyList<SampleShape> Shapes { get; }

        /// <summary>Gets the handles of entities drawn out of a dimension's block that no builder gave a part: none where every entity is tagged.</summary>
        internal IReadOnlyList<string> Untagged { get; }

        /// <summary>Gets the dimension style of the sample's drawing, the style's variables, which the callouts measure the words by; null for a multileader's sample.</summary>
        internal DxfDimStyleTableRecord Style =>
            Database.DimStyleTable.Has("Preview") ? Database.DimStyleTable["Preview"] : null;

        /// <summary>Gets the part an entity of the sample is, by its handle.</summary>
        /// <param name="handle">The handle of an entity of <see cref="Database"/>.</param>
        /// <returns>The part; <see cref="CadStylePart.None"/> for an entity that is no part (the outline) or a handle the sample has not.</returns>
        public CadStylePart PartOf(string handle) =>
            handle != null && _byHandle.TryGetValue(handle, out CadStylePart part) ? part : CadStylePart.None;
    }

    /// <summary>One part of one dimension of a <see cref="CadStyleSample"/>: its entities.</summary>
    public sealed class CadStyleSamplePart
    {
        internal CadStyleSamplePart(CadStylePart part, int dimension, IReadOnlyList<string> handles)
        {
            Part = part;
            Dimension = dimension;
            Handles = handles;
        }

        /// <summary>Gets which part of the dimension it is.</summary>
        public CadStylePart Part { get; }

        /// <summary>Gets which dimension of the sample it belongs to: 0 for the first drawn, 1 for the next.</summary>
        public int Dimension { get; }

        /// <summary>Gets the handles of its entities in the sample's drawing, in the order they are drawn.</summary>
        public IReadOnlyList<string> Handles { get; }
    }

    /// <summary>What a dimension, a leader or a multileader of a sample drew, as the callouts need it: the points its picture was laid out by.</summary>
    internal sealed class SampleShape
    {
        /// <summary>Gets or sets the kind of shape.</summary>
        public SampleShapeKind Kind { get; set; }

        /// <summary>Gets or sets a linear dimension's first origin.</summary>
        public GeoPoint2 P1 { get; set; }

        /// <summary>Gets or sets a linear dimension's second origin.</summary>
        public GeoPoint2 P2 { get; set; }

        /// <summary>Gets or sets where a linear dimension's line meets its first extension line.</summary>
        public GeoPoint2 Q1 { get; set; }

        /// <summary>Gets or sets where a linear dimension's line meets its second extension line.</summary>
        public GeoPoint2 Q2 { get; set; }

        /// <summary>Gets or sets the centre of a round dimension, or the point a multileader's arrow points at.</summary>
        public GeoPoint2 Centre { get; set; }

        /// <summary>Gets or sets the point on the rim a radius is measured to.</summary>
        public GeoPoint2 Rim { get; set; }

        /// <summary>Gets or sets the end of a jog on the parallel through the centre drawn.</summary>
        public GeoPoint2 Front { get; set; }

        /// <summary>Gets or sets the end of a jog on the radius.</summary>
        public GeoPoint2 Back { get; set; }

        /// <summary>Gets or sets the start of a multileader's landing.</summary>
        public GeoPoint2 LandStart { get; set; }

        /// <summary>Gets or sets the end of a multileader's landing.</summary>
        public GeoPoint2 LandEnd { get; set; }

        /// <summary>Gets or sets the words of the dimension or multileader, as drawn into the sample; null for none.</summary>
        public DxfMText Words { get; set; }
    }

    /// <summary>The kinds of shape a sample draws.</summary>
    internal enum SampleShapeKind
    {
        /// <summary>A linear dimension.</summary>
        Linear,

        /// <summary>A radius.</summary>
        Radial,

        /// <summary>An angle.</summary>
        Angular,

        /// <summary>An arc length dimension.</summary>
        ArcLength,

        /// <summary>A jogged radius.</summary>
        Jogged,

        /// <summary>The leader of the dimension style.</summary>
        Leader,

        /// <summary>A multileader.</summary>
        MLeader,
    }
}
