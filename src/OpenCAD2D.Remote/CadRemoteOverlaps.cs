using System;

namespace OpenCAD2D.Remote
{
    /// <summary>
    /// The pairs of entities whose words overlap (<see cref="CadRemoteDocument.GetOverlaps"/>): the handle of the entity whose
    /// words they are, the handle of what they meet and the box of the common part, kept as three arrays. A pair is one row
    /// however many of the parts of the two meet.
    /// </summary>
    public sealed class CadRemoteOverlaps
    {
        internal CadRemoteOverlaps(string[] words, string[] others, double[] boxes)
        {
            Words = words;
            Others = others;
            Boxes = boxes;
        }

        /// <summary>Gets the number of rows.</summary>
        public int Count => Words.Length;

        /// <summary>Gets the handle of the entity whose words meet the other, a row at a time; where two entities of words meet, the one drawn first.</summary>
        public string[] Words { get; }

        /// <summary>Gets the handle of the entity the words meet, a row at a time.</summary>
        public string[] Others { get; }

        /// <summary>Gets the boxes of the common parts, four numbers a row: the least X, the least Y, the greatest X and the greatest Y.</summary>
        public double[] Boxes { get; }

        /// <summary>Gets the box of the common part of a row as its four numbers.</summary>
        /// <param name="row">The number of the row, from 0.</param>
        /// <returns>The four numbers.</returns>
        /// <exception cref="ArgumentOutOfRangeException">Thrown when the row is not in the table.</exception>
        public double[] BoxOf(int row)
        {
            if (row < 0 || row >= Words.Length)
            {
                throw new ArgumentOutOfRangeException(nameof(row));
            }

            return new[] { Boxes[4 * row], Boxes[4 * row + 1], Boxes[4 * row + 2], Boxes[4 * row + 3] };
        }
    }
}
