using System;

namespace OpenCAD2D.Remote
{
    /// <summary>
    /// The empty rectangles of a frame as the app finds them (<see cref="CadRemoteDocument.GetFreeSpace"/>): four numbers a
    /// rectangle, the largest first, kept as one array.
    /// </summary>
    public sealed class CadRemoteFreeSpace
    {
        internal CadRemoteFreeSpace(double[] boxes)
        {
            Boxes = boxes;
        }

        /// <summary>Gets the number of rectangles.</summary>
        public int Count => Boxes.Length / 4;

        /// <summary>Gets the rectangles, four numbers each: the least X, the least Y, the greatest X and the greatest Y.</summary>
        public double[] Boxes { get; }

        /// <summary>Gets a rectangle as its four numbers: the least X, the least Y, the greatest X and the greatest Y.</summary>
        /// <param name="row">The number of the rectangle, from 0.</param>
        /// <returns>The four numbers.</returns>
        /// <exception cref="ArgumentOutOfRangeException">Thrown when there is no such rectangle.</exception>
        public double[] BoxOf(int row)
        {
            if (row < 0 || row >= Count)
            {
                throw new ArgumentOutOfRangeException(nameof(row));
            }

            return new[] { Boxes[4 * row], Boxes[4 * row + 1], Boxes[4 * row + 2], Boxes[4 * row + 3] };
        }
    }
}
