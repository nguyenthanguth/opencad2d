using System;

namespace OpenCAD2D
{
    /// <summary>
    /// The pairs of entities that overlap (<see cref="CadSpatial.Overlaps"/>): the handle of the one that is words, the handle
    /// of the other and the box of what they have in common, kept as three arrays. A pair is one row however many of the
    /// parts of the two meet; its box holds all the places they do.
    /// </summary>
    public sealed class CadOverlapTable
    {
        private readonly string[] _words;
        private readonly string[] _others;
        private readonly double[] _boxes;

        internal CadOverlapTable(string[] words, string[] others, double[] boxes)
        {
            _words = words;
            _others = others;
            _boxes = boxes;
        }

        /// <summary>Gets the number of rows.</summary>
        public int Count => _words.Length;

        /// <summary>
        /// Gets the handle of the entity whose words meet the other, a row at a time. Where two entities of words meet each
        /// other the one drawn first is named here. The array is the table's own: read it, do not change it.
        /// </summary>
        public string[] Words => _words;

        /// <summary>Gets the handle of the entity the words meet, a row at a time. The array is the table's own: read it, do not change it.</summary>
        public string[] Others => _others;

        /// <summary>
        /// Gets the boxes of the common parts, four numbers a row: the least X, the least Y, the greatest X and the greatest Y.
        /// A word frame from (0, 0) to (20, 2.5) that a line from (5, -10) to (5, 10) crosses has the box from (5, 0.001) to (5, 2.499) for it at a tolerance of 0.001. The
        /// array is the table's own: read it, do not change it.
        /// </summary>
        public double[] Boxes => _boxes;

        /// <summary>Gets the box of the common part of a row.</summary>
        /// <param name="row">The number of the row, from 0.</param>
        /// <returns>The box.</returns>
        /// <exception cref="ArgumentOutOfRangeException">Thrown when the row is not in the table.</exception>
        public DisplayBox BoxOf(int row)
        {
            if (row < 0 || row >= _words.Length)
            {
                throw new ArgumentOutOfRangeException(nameof(row));
            }

            return new DisplayBox(_boxes[4 * row], _boxes[4 * row + 1], _boxes[4 * row + 2], _boxes[4 * row + 3]);
        }
    }
}
