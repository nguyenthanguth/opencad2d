using System;

namespace OpenCAD2D.Remote
{
    /// <summary>
    /// What an object would take on the page, found by the app before it is added (<see cref="CadRemoteDocument.Measure"/>):
    /// the box of everything it draws and the frame of each of its words, kept as two arrays.
    /// </summary>
    public sealed class CadRemoteMeasurement
    {
        internal CadRemoteMeasurement(double[] box, double[] frames)
        {
            Box = box;
            Frames = frames;
        }

        /// <summary>
        /// Gets the box as four numbers, the least X, the least Y, the greatest X and the greatest Y; none where the object draws
        /// nothing, a text of no words.
        /// </summary>
        public double[] Box { get; }

        /// <summary>
        /// Gets the frames of the words, five numbers each in the order they are drawn: the X and Y of the first corner, the
        /// width, the height and the angle the width runs at in radians.
        /// </summary>
        public double[] Frames { get; }

        /// <summary>Gets the number of word frames.</summary>
        public int FrameCount => Frames.Length / 5;

        /// <summary>Gets the frame of a word as its five numbers.</summary>
        /// <param name="row">The number of the word, from 0.</param>
        /// <returns>The first corner's X and Y, the width, the height and the angle.</returns>
        /// <exception cref="ArgumentOutOfRangeException">Thrown when there is no such word.</exception>
        public double[] FrameOf(int row)
        {
            if (row < 0 || row >= FrameCount)
            {
                throw new ArgumentOutOfRangeException(nameof(row));
            }

            return new[]
            {
                Frames[5 * row],
                Frames[5 * row + 1],
                Frames[5 * row + 2],
                Frames[5 * row + 3],
                Frames[5 * row + 4],
            };
        }
    }
}
