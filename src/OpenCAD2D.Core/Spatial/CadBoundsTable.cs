using System;

namespace OpenCAD2D
{
    /// <summary>
    /// The boxes of the entities of a space as they are drawn (<see cref="CadSpatial.Bounds(CadDocument, CadSpace)"/>): a
    /// handle and four numbers a row, kept as two arrays so that a drawing of a million entities is not a million objects and
    /// a program that reads it copies two arrays. A row is the box of one entity in the order the space draws them.
    /// </summary>
    public sealed class CadBoundsTable
    {
        private readonly string[] _handles;
        private readonly double[] _boxes;

        internal CadBoundsTable(string[] handles, double[] boxes)
        {
            _handles = handles;
            _boxes = boxes;
        }

        /// <summary>Gets the number of rows.</summary>
        public int Count => _handles.Length;

        /// <summary>Gets the handle of each row. The array is the table's own: read it, do not change it.</summary>
        public string[] Handles => _handles;

        /// <summary>
        /// Gets the boxes, four numbers a row in the order of <see cref="Handles"/>: the least X, the least Y, the greatest X
        /// and the greatest Y, in the units of the drawing. The array is the table's own: read it, do not change it.
        /// </summary>
        public double[] Boxes => _boxes;

        /// <summary>Gets the box of a row.</summary>
        /// <param name="row">The number of the row, from 0.</param>
        /// <returns>The box.</returns>
        /// <exception cref="ArgumentOutOfRangeException">Thrown when the row is not in the table.</exception>
        public DisplayBox BoxOf(int row)
        {
            if (row < 0 || row >= _handles.Length)
            {
                throw new ArgumentOutOfRangeException(nameof(row));
            }

            return new DisplayBox(_boxes[4 * row], _boxes[4 * row + 1], _boxes[4 * row + 2], _boxes[4 * row + 3]);
        }
    }
}
