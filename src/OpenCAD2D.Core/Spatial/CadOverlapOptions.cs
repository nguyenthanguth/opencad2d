using System.Collections.Generic;

namespace OpenCAD2D
{
    /// <summary>
    /// Which entities <see cref="CadSpatial.Overlaps"/> looks at. A kind is the DXF name of an entity of the space, in any case:
    /// <c>TEXT</c>, <c>MTEXT</c>, <c>DIMENSION</c>, <c>MULTILEADER</c> (<c>MLEADER</c> too), <c>LINE</c>, <c>LWPOLYLINE</c>,
    /// <c>INSERT</c>. What an INSERT draws, its attributes and the text of its block included, is of the kind INSERT.
    /// </summary>
    public sealed class CadOverlapOptions
    {
        /// <summary>
        /// Gets or sets the kinds whose words are looked at; null for the defaults, <c>TEXT</c>, <c>MTEXT</c>,
        /// <c>DIMENSION</c> and <c>MULTILEADER</c>. A dimension's words are those of its picture.
        /// </summary>
        public IReadOnlyCollection<string> Words { get; set; }

        /// <summary>
        /// Gets or sets the kinds the words are looked at against: their lines, curves, fills and, for a kind of words, their
        /// own words; null for every kind. <c>Words</c> of <c>TEXT</c> and <c>Over</c> of <c>LINE</c> and <c>ARC</c> ask what
        /// text a line or an arc runs through.
        /// </summary>
        public IReadOnlyCollection<string> Over { get; set; }

        /// <summary>
        /// Gets or sets the handles the answer is about: only pairs in which one of them stands, as the words or as what the
        /// words meet, are given. Null for every pair of the space. A handle the space does not draw is left out.
        /// </summary>
        public IReadOnlyCollection<string> Handles { get; set; }
    }
}
