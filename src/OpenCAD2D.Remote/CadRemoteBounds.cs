using System;

namespace OpenCAD2D.Remote
{
    /// <summary>
    /// The boxes of the entities of a drawing as the app draws them (<see cref="CadRemoteDocument.GetBounds"/>): a handle and four
    /// numbers a row, kept as two arrays so that a drawing of 100 000 entities is two arrays and no 100 000 objects. A row is the
    /// box of one entity in the order the space is drawn; an entity that draws nothing has none.
    /// </summary>
    public sealed class CadRemoteBounds
    {
        internal CadRemoteBounds(string[] handles, double[] boxes)
        {
            Handles = handles;
            Boxes = boxes;
        }

        /// <summary>Gets the number of rows.</summary>
        public int Count => Handles.Length;

        /// <summary>Gets the handle of each row.</summary>
        public string[] Handles { get; }

        /// <summary>
        /// Gets the boxes, four numbers a row in the order of <see cref="Handles"/>: the least X, the least Y, the greatest X and the
        /// greatest Y, in the units of the drawing.
        /// </summary>
        public double[] Boxes { get; }

        /// <summary>Gets the box of a row as its four numbers: the least X, the least Y, the greatest X and the greatest Y.</summary>
        /// <param name="row">The number of the row, from 0.</param>
        /// <returns>The four numbers.</returns>
        /// <exception cref="ArgumentOutOfRangeException">Thrown when the row is not in the table.</exception>
        public double[] BoxOf(int row)
        {
            if (row < 0 || row >= Handles.Length)
            {
                throw new ArgumentOutOfRangeException(nameof(row));
            }

            return new[] { Boxes[4 * row], Boxes[4 * row + 1], Boxes[4 * row + 2], Boxes[4 * row + 3] };
        }
    }
}
