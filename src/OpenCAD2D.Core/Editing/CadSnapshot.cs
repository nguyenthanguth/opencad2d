namespace OpenCAD2D
{
    /// <summary>
    /// A drawing as it stood at one moment, kept in memory by <see cref="CadDocument.TakeSnapshot"/> to put the drawing back to
    /// with <see cref="CadDocument.Reset"/>: for a program that runs an episode of edits on a drawing, many times over, from the
    /// same start (training a model, trying layouts) and cannot afford to read the file again. A snapshot holds the drawing
    /// compressed (a drawing of 10 000 entities, about 0.2 MB) while it is kept, and may be reset to as often as wanted.
    /// </summary>
    public sealed class CadSnapshot
    {
        internal CadSnapshot(
            CadDocument document,
            int position,
            CadUndoStep last,
            CadDocument.KeptState state,
            PackedDrawing own
        )
        {
            Document = document;
            Position = position;
            Last = last;
            State = state;
            Own = own;
        }

        /// <summary>Gets the document the snapshot is of: the only one it can be reset to.</summary>
        public CadDocument Document { get; }

        /// <summary>Gets the number of steps of the undo list behind the point the snapshot was taken at.</summary>
        internal int Position { get; }

        /// <summary>Gets the last step behind the point (the step that made it), or null where there was none.</summary>
        internal CadUndoStep Last { get; }

        /// <summary>Gets the drawing the undo list kept at the point.</summary>
        internal CadDocument.KeptState State { get; }

        /// <summary>
        /// Gets a copy of the drawing of its own, taken where the state the list kept at the point lacks a setting changed after
        /// it, else null: the kept state is then the copy.
        /// </summary>
        internal PackedDrawing Own { get; }

        /// <summary>Gets the bytes of the drawing as it stood, for a reset the undo list can no longer make.</summary>
        internal PackedDrawing Bytes => Own ?? State.Bytes;
    }
}
