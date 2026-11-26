using System;

namespace OpenCAD2D.Remote
{
    /// <summary>A drawing of the app kept as it stands and put back to it, for programs that run episodes of edits in the app.</summary>
    public sealed partial class CadRemoteDocument
    {
        /// <summary>
        /// Asks the app to keep the drawing as it stands, with the changes not saved, to put it back to with
        /// <see cref="Reset"/>: for a program that tries edits on the drawing again and again from the same start. What the user
        /// did before can still be undone after a reset while the app's undo list holds the point of the snapshot; where it does not (a reset to a later snapshot after a reset to an earlier one) the app reads the drawing back from the snapshot's bytes and starts its undo list afresh. What came after the snapshot is the episode.
        /// </summary>
        /// <returns>The snapshot.</returns>
        /// <exception cref="CadRemoteException">Thrown when the drawing is closed, or a command is running in it.</exception>
        public CadRemoteSnapshot TakeSnapshot() =>
            new CadRemoteSnapshot(this, Ask(RemoteOps.Snapshot).GetLong("snapshot"));

        /// <summary>
        /// Asks the app to put the drawing back as the snapshot kept it: its entities, tables, layouts, handles and header
        /// variables. The steps made since are forgotten, not made one more step, so that a thousand resets leave the app's memory
        /// where one does; the display is updated as after an undo.
        /// </summary>
        /// <param name="snapshot">The snapshot of this drawing.</param>
        /// <exception cref="ArgumentNullException">Thrown when the snapshot is null.</exception>
        /// <exception cref="ArgumentException">Thrown when the snapshot is of another drawing.</exception>
        /// <exception cref="CadRemoteException">Thrown when the drawing is closed, a command is running in it, or the snapshot was released.</exception>
        public void Reset(CadRemoteSnapshot snapshot)
        {
            Own(snapshot);
            Ask(RemoteOps.Reset, request => request.Set("snapshot", snapshot.Id));
        }

        /// <summary>Tells the app to forget a snapshot and the memory it holds; one already released is no error.</summary>
        /// <param name="snapshot">The snapshot of this drawing.</param>
        /// <exception cref="ArgumentNullException">Thrown when the snapshot is null.</exception>
        /// <exception cref="ArgumentException">Thrown when the snapshot is of another drawing.</exception>
        /// <exception cref="CadRemoteException">Thrown when the app cannot be reached.</exception>
        public void Release(CadRemoteSnapshot snapshot)
        {
            Own(snapshot);
            Ask(RemoteOps.ReleaseSnapshot, request => request.Set("snapshot", snapshot.Id));
        }

        private void Own(CadRemoteSnapshot snapshot)
        {
            if (snapshot == null)
            {
                throw new ArgumentNullException(nameof(snapshot));
            }

            if (!ReferenceEquals(snapshot.Document, this))
            {
                throw new ArgumentException("The snapshot is of another drawing.", nameof(snapshot));
            }
        }
    }
}
