using System.Collections.Generic;

namespace OpenCAD2D
{
    /// <summary>
    /// What <see cref="CadSpatial.FreeSpace"/> and <see cref="CadSpatial.Fits"/> count as in the way: the kinds of entity whose
    /// drawn parts take room, and how much clear room to leave round them.
    /// </summary>
    public sealed class CadFreeSpaceOptions
    {
        /// <summary>
        /// Gets or sets the kinds of entity that take room, as the DXF names of <see cref="CadOverlapOptions.Words"/> give them
        /// (<c>TEXT</c>, <c>MTEXT</c>, <c>DIMENSION</c>, <c>LINE</c>, <c>INSERT</c>, <c>MLEADER</c> too); null for every kind. What
        /// an INSERT draws, a block's lines and words, is of the kind INSERT.
        /// </summary>
        public IReadOnlyCollection<string> Kinds { get; set; }

        /// <summary>
        /// Gets or sets the clear room left round what takes room, in millimetres, at least nought: a free rectangle stands that
        /// far from every line and word, and a candidate that fits is that far from them. Nought is the tolerance's: a thing
        /// that only touches another fits beside it.
        /// </summary>
        public double Margin { get; set; }

        /// <summary>
        /// Gets or sets the least width of a free rectangle <see cref="CadSpatial.FreeSpace"/> gives, at least nought. A
        /// rectangle narrower than the thing to place is of no use, and in a crowded frame there are thousands of them: with the
        /// width and height of the thing given, only the rectangles it could stand in are found. Not used by
        /// <see cref="CadSpatial.Fits"/>.
        /// </summary>
        public double MinWidth { get; set; }

        /// <summary>Gets or sets the least height of a free rectangle, as <see cref="MinWidth"/> is the least width.</summary>
        public double MinHeight { get; set; }
    }
}
