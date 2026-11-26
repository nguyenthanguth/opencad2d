using System;

namespace OpenCAD2D
{
    /// <summary>
    /// The empty rectangles of a frame (<see cref="CadSpatial.FreeSpace"/>): four numbers a row, kept as one array so that a
    /// frame with thousands of them is one array and no thousands of objects. A row is a rectangle no line or word of the
    /// kinds asked about runs through, which cannot be made larger; the rows are the largest first.
    /// </summary>
    public sealed class CadFreeSpace
    {
        private readonly double[] _boxes;

        internal CadFreeSpace(double[] boxes)
        {
            _boxes = boxes;
        }

        /// <summary>Gets the number of rectangles.</summary>
        public int Count => _boxes.Length / 4;

        /// <summary>
        /// Gets the rectangles, four numbers each: the least X, the least Y, the greatest X and the greatest Y, in the units of
        /// the drawing, the largest area first; rectangles of one area by the least Y, then the least X, then the greatest Y,
        /// then the greatest X. The array is the table's own: read it, do not change it.
        /// </summary>
        public double[] Boxes => _boxes;

        /// <summary>Gets a rectangle.</summary>
        /// <param name="row">The number of the rectangle, from 0.</param>
        /// <returns>The box.</returns>
        /// <exception cref="ArgumentOutOfRangeException">Thrown when the row is not in the table.</exception>
        public DisplayBox BoxOf(int row)
        {
            if (row < 0 || row >= Count)
            {
                throw new ArgumentOutOfRangeException(nameof(row));
            }

            return new DisplayBox(_boxes[4 * row], _boxes[4 * row + 1], _boxes[4 * row + 2], _boxes[4 * row + 3]);
        }
    }
}
