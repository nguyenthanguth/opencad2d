using System.Collections.Generic;

namespace OpenCAD2D
{
    /// <summary>
    /// What a thing would take on the page, found before it is added (<see cref="CadMeasure"/>): the box of everything it draws
    /// and the frame of each of its words. The box is the one <see cref="CadSpatial.Bounds(CadDocument, CadSpace)"/> gives for
    /// the thing once it is added, and the frames are the ones <see cref="CadSpatial.Overlaps"/> looks at.
    /// </summary>
    public sealed class CadMeasurement
    {
        internal CadMeasurement(DisplayBox box, IReadOnlyList<CadFrame> frames)
        {
            Box = box;
            Frames = frames;
        }

        /// <summary>
        /// Gets the box of everything the thing draws: its letters, lines and arrows. Empty where it draws nothing, a text of
        /// no words. A TEXT 5 high reading "Plate" set at (0, 0) in a font whose letters are 3 wide has the box from (0, 0) to
        /// (15, 5).
        /// </summary>
        public DisplayBox Box { get; }

        /// <summary>
        /// Gets the frame of each of its words, in the order they are drawn: one for a TEXT, one for each line run of an MTEXT, one
        /// for the words of a dimension. A word turned 30 degrees has a frame turned 30 degrees, whose box is larger than the
        /// word's own.
        /// </summary>
        public IReadOnlyList<CadFrame> Frames { get; }
    }
}
