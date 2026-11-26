using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using OpenCAD2D.Remote;

namespace OpenCAD2D.Mcp
{
    /// <summary>
    /// The snapshots a server holds, by the id it gave each (<c>s1</c>, <c>s2</c> ...), each tied to the drawing it was taken of: the
    /// document of a file, or the id of the app's drawing. A snapshot is released when the agent says so, when the file's drawing is
    /// replaced (the document is gone), and when the server ends; in the app the snapshot also goes with the drawing it is of.
    /// </summary>
    internal sealed class SnapshotStore
    {
        private sealed class Held
        {
            public string Id;
            public object Key;
            public object Snapshot;
        }

        private readonly List<Held> _held = new List<Held>();
        private int _last;

        /// <summary>Gets the ids held, in the order they were given.</summary>
        public IReadOnlyList<string> Ids => _held.Select(h => h.Id).ToList();

        /// <summary>Keeps a snapshot and gives its id.</summary>
        /// <param name="key">What identifies the drawing it is of.</param>
        /// <param name="snapshot">The snapshot the backend gave.</param>
        public string Add(object key, object snapshot)
        {
            string id = "s" + (++_last).ToString(CultureInfo.InvariantCulture);
            _held.Add(
                new Held
                {
                    Id = id,
                    Key = key,
                    Snapshot = snapshot,
                }
            );
            return id;
        }

        /// <summary>Gives the snapshot of an id for the drawing of a key.</summary>
        /// <exception cref="ArgumentException">Thrown when there is no snapshot of that id, or it is of another drawing.</exception>
        public object Take(string id, object key)
        {
            Held held = _held.FirstOrDefault(h => string.Equals(h.Id, id?.Trim(), StringComparison.OrdinalIgnoreCase));

            if (held == null)
            {
                throw new ArgumentException(
                    "There is no snapshot "
                        + id
                        + (
                            _held.Count == 0
                                ? ": none is held; snapshot makes one."
                                : ": the ones held are " + string.Join(", ", Ids) + "."
                        )
                );
            }

            if (!Equals(held.Key, key))
            {
                throw new ArgumentException(
                    "The snapshot " + held.Id + " is of another drawing than the one the tools work on now."
                );
            }

            return held.Snapshot;
        }

        /// <summary>Lets go of a snapshot; one already gone is no error. Says whether there was one.</summary>
        public bool Release(string id)
        {
            Held held = _held.FirstOrDefault(h => string.Equals(h.Id, id?.Trim(), StringComparison.OrdinalIgnoreCase));

            if (held == null)
            {
                return false;
            }

            _held.Remove(held);
            Free(held.Snapshot);
            return true;
        }

        /// <summary>Lets go of every snapshot, as the file's drawing is replaced or the server ends.</summary>
        public void ReleaseAll()
        {
            foreach (Held held in _held.ToList())
            {
                Free(held.Snapshot);
            }

            _held.Clear();
        }

        private static void Free(object snapshot)
        {
            try
            {
                if (snapshot is CadSnapshot own)
                {
                    own.Document.ReleaseSnapshot(own);
                }
                else if (snapshot is CadRemoteSnapshot remote)
                {
                    remote.Document.Release(remote);
                }
            }
            catch (Exception exception) when (exception is CadRemoteException || exception is InvalidOperationException)
            {
                // The app is gone or the drawing closed: what the snapshot held went with it.
            }
        }
    }
}
