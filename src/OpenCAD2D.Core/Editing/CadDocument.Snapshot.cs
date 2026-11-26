using System;
using System.Collections.Generic;

namespace OpenCAD2D
{
    /// <summary>
    /// The snapshot and reset of a drawing in memory: one call keeps the drawing as it stands, another puts it back, as often as
    /// wanted, for a program that runs episodes of edits from the same start.
    /// </summary>
    /// <remarks>
    /// <para>
    /// The document already keeps the drawing at every step of its undo list, so a snapshot is a point of that list (the number
    /// of steps behind it, the last of them and the drawing kept there), and costs nothing where the drawing is kept already. A
    /// reset is one jump of the list back to that point: what <see cref="Undo"/> does for one step, done once over all the steps
    /// since, so that the drawing is read from the kept bytes once (a drawing of 10 000 entities is opened in 2 ms, as the
    /// bytes are read lazily) and only the entities the steps touched are drawn again, as after an undo. The steps made since are
    /// then forgotten as the steps undone are forgotten by a new edit, so 1000 episodes hold no more of them than one.
    /// </para>
    /// <para>
    /// Where the list no longer holds the point (the drawing was undone below it and edited again, a Block Editor session joined
    /// the steps, or a reset to an earlier snapshot cut the list), the drawing is read back from the bytes the snapshot keeps
    /// and the undo list starts again with it, nothing left to undo. The drawing is as exact on that path; the views of the
    /// spaces and the user coordinate system, which the steps put back, are not restored, and every display list is made again.
    /// A change written into <see cref="Database"/> directly, not through <see cref="ApplyEdit"/>, after the first step is not
    /// in a snapshot, as it is not in an undo.
    /// </para>
    /// <para>
    /// After a reset the drawing the next one goes to is read ahead on a worker thread, with its index of handles, while the
    /// episode runs, so that a reset of a drawing of 10 000 entities takes 0.2 ms and the first one 4 ms; the drawing read is held
    /// (about 520 MB at 1 000 000 entities, as the readings for undo are) until the next reset to another snapshot or
    /// <see cref="ReleaseSnapshot"/>.
    /// </para>
    /// </remarks>
    public sealed partial class CadDocument
    {
        /// <summary>
        /// Keeps the drawing as it stands, to be put back with <see cref="Reset"/>. The undo list is left as it is; the drawing is
        /// saved once if it has not been, and again (compressed) only where a setting changed since the list kept it is not in the
        /// kept drawing.
        /// </summary>
        /// <returns>The snapshot.</returns>
        /// <exception cref="InvalidOperationException">Thrown when a group of edits is open (a command is running), or the Block
        /// Editor or a reference edit is open.</exception>
        public CadSnapshot TakeSnapshot()
        {
            RefuseSnapshotNow("A snapshot");
            EnsureStart();

            // A real-time zoom or pan straight after another joins its step: a later one must not replace the step the snapshot stands at.
            _realTimeStep = null;
            _realTimeViewStep = null;
            return new CadSnapshot(
                this,
                _position,
                _position > 0 ? _steps[_position - 1] : null,
                _states[_position],
                HasSettingsRun() ? Snapshot() : null
            );
        }

        /// <summary>
        /// Puts the drawing back as it stood when the snapshot was taken: its entities, tables, layouts, handles and header
        /// variables (the current layer and styles, <c>$HANDSEED</c>), the view of each space and the settings the steps changed.
        /// The steps made since are forgotten, so the drawing can be reset again and again from one snapshot. Selected entities
        /// that are still in the drawing stay selected.
        /// </summary>
        /// <param name="snapshot">The snapshot, of this document.</param>
        /// <exception cref="ArgumentNullException">Thrown when the snapshot is null.</exception>
        /// <exception cref="ArgumentException">Thrown when the snapshot is of another document.</exception>
        /// <exception cref="InvalidOperationException">Thrown when a group of edits is open (a command is running), or the Block
        /// Editor or a reference edit is open.</exception>
        public void Reset(CadSnapshot snapshot)
        {
            if (snapshot == null)
            {
                throw new ArgumentNullException(nameof(snapshot));
            }

            if (!ReferenceEquals(snapshot.Document, this))
            {
                throw new ArgumentException("The snapshot is of another drawing.", nameof(snapshot));
            }

            RefuseSnapshotNow("A reset");
            _shownState = null;
            DropReads();

            if (!HoldsPoint(snapshot))
            {
                ResetFromBytes(snapshot);
                return;
            }

            // The steps between the point and where the list stands: taken back, last first, or put again, first first.
            bool undo = _position > snapshot.Position;
            var steps = new List<CadUndoStep>();

            if (undo)
            {
                for (int i = _position - 1; i >= snapshot.Position; i--)
                {
                    steps.Add(_steps[i]);
                }
            }
            else
            {
                for (int i = _position; i < snapshot.Position; i++)
                {
                    steps.Add(_steps[i]);
                }
            }

            _position = snapshot.Position;
            _realTimeStep = null;
            _realTimeViewStep = null;
            ForgetRedo();
            IReadOnlyList<CadEditRecord> records = RecordsOf(steps);
            Move(steps, records, undo);
            Announce(undo ? CadChangeKind.Undo : CadChangeKind.Redo, "RESET", records);
            ReadAheadReset(snapshot.State);
        }

        /// <summary>
        /// Lets go of the drawing read ahead for the next reset to the snapshot, which a program that is done with the snapshot
        /// would otherwise leave held until the next reset to another. The snapshot itself stays valid, and is reset to as before,
        /// the first reset then slower.
        /// </summary>
        /// <param name="snapshot">The snapshot.</param>
        /// <exception cref="ArgumentNullException">Thrown when the snapshot is null.</exception>
        public void ReleaseSnapshot(CadSnapshot snapshot)
        {
            if (snapshot == null)
            {
                throw new ArgumentNullException(nameof(snapshot));
            }

            if (_readReset != null && ReferenceEquals(_readReset.State, snapshot.State))
            {
                _readReset.Drop();
                _readReset = null;
            }
        }

        /// <summary>Refuses a snapshot or a reset while a command's group of edits is open, or the Block Editor or a reference edit is: the drawing is then half of something.</summary>
        private void RefuseSnapshotNow(string what)
        {
            if (_groupDepth > 0)
            {
                throw new InvalidOperationException(
                    what + " is refused while a command is running: a group of edits is open."
                );
            }

            if (_blockEditor != null || _refEdit != null)
            {
                throw new InvalidOperationException(
                    what + " is refused while the Block Editor or a reference edit is open."
                );
            }
        }

        /// <summary>Whether the undo list still holds the point of the snapshot: its last step and its kept drawing are where it left them.</summary>
        private bool HoldsPoint(CadSnapshot snapshot) =>
            snapshot.Position <= _steps.Count
            && snapshot.Position < _states.Count
            && ReferenceEquals(_states[snapshot.Position], snapshot.State)
            && (snapshot.Position == 0 || ReferenceEquals(_steps[snapshot.Position - 1], snapshot.Last));

        /// <summary>
        /// Reads on a worker thread the drawing the next reset to this state goes to, from the same bytes and with its index of
        /// handles, while the program runs its next episode, so that the next reset finds it read, as an undo finds its drawing
        /// (see <see cref="ReadAround"/>). Unlike those, an edit does not stop it: it is the whole point of it. One such reading is
        /// held at most, for the state last reset to.
        /// </summary>
        private void ReadAheadReset(KeptState state)
        {
            if (_readReset != null && _readReset.State == state)
            {
                return;
            }

            _readReset?.Drop();
            _readReset = StartReading(state, null);
        }

        /// <summary>Lets go of the drawings read ahead for an undo and a redo: the list they were read for is about to change.</summary>
        private void DropReads()
        {
            _readUndo?.Drop();
            _readUndo = null;
            _readRedo?.Drop();
            _readRedo = null;
        }

        /// <summary>
        /// Reads the drawing back from the snapshot's bytes where the undo list no longer holds its point: the list starts again with
        /// the drawing as it is, and every display list is made again, as a failed edit taken back does.
        /// </summary>
        private void ResetFromBytes(CadSnapshot snapshot)
        {
            _steps.Clear();
            _states.Clear();
            _position = 0;
            _savedPosition = -1;
            _undoLimit = 0;
            _realTimeStep = null;
            _realTimeViewStep = null;
            Reload(snapshot.Bytes, null);
            _readReset?.Drop();
            _readReset = null;
            Changed?.Invoke(
                this,
                new CadDocumentChangedEventArgs(
                    CadChangeKind.Rollback,
                    "RESET",
                    Array.Empty<string>(),
                    AllSpaces.ToArray(),
                    true
                )
            );
        }
    }
}
