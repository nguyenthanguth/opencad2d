using System;
using System.Collections.Generic;
using System.Linq;
using OpenCAD2D.Remote;

namespace OpenCAD2D
{
    /// <summary>A drawing kept as it stands and put back to it, asked from outside: for a program that runs episodes of edits in the app.</summary>
    public sealed partial class CadRemoteServer
    {
        // The snapshots taken for outside programs, by the number given them: touched only on the dispatcher's thread.
        private readonly Dictionary<long, CadSnapshot> _snapshots = new Dictionary<long, CadSnapshot>();
        private long _lastSnapshot;

        /// <summary>
        /// Answers a <c>snapshot</c>: keeps the drawing as it stands (<see cref="CadDocument.TakeSnapshot"/>) and gives the number
        /// to reset it with. The snapshots of drawings closed since are forgotten here.
        /// </summary>
        private JsonObject TakeSnapshot(CadDocument document)
        {
            Busy(document, "take a snapshot");

            foreach (
                long gone in _snapshots
                    .Where(p => !_host.Documents.Contains(p.Value.Document))
                    .Select(p => p.Key)
                    .ToList()
            )
            {
                _snapshots.Remove(gone);
            }

            CadSnapshot snapshot;

            try
            {
                snapshot = document.TakeSnapshot();
            }
            catch (InvalidOperationException exception)
            {
                throw new Refusal(exception.Message);
            }

            _snapshots[++_lastSnapshot] = snapshot;
            return new JsonObject().Set("snapshot", _lastSnapshot);
        }

        /// <summary>
        /// Answers a <c>reset</c>: puts the drawing back as the snapshot kept it (<see cref="CadDocument.Reset"/>). The steps made
        /// since are forgotten, not made one more step, so that a thousand resets leave the app's memory where one does: what the
        /// user did before the snapshot stays undoable. Refused while a command runs in the drawing, or for a snapshot of another
        /// drawing or one released.
        /// </summary>
        private JsonObject ResetToSnapshot(CadDocument document, JsonObject request)
        {
            Busy(document, "reset it");

            if (!_snapshots.TryGetValue(request.GetLong("snapshot", -1), out CadSnapshot snapshot))
            {
                throw new Refusal(
                    "OpenCAD2D has no snapshot "
                        + request.GetLong("snapshot", -1)
                        + ": it was released, or its drawing closed."
                );
            }

            try
            {
                document.Reset(snapshot);
            }
            catch (Exception exception) when (exception is InvalidOperationException || exception is ArgumentException)
            {
                throw new Refusal(exception.Message.Split('\n')[0].TrimEnd('\r'));
            }

            return new JsonObject();
        }

        /// <summary>Answers a <c>releaseSnapshot</c>: the app forgets the snapshot, and the memory it held; one already forgotten is no error.</summary>
        private JsonObject ReleaseSnapshot(JsonObject request)
        {
            long number = request.GetLong("snapshot", -1);

            if (_snapshots.TryGetValue(number, out CadSnapshot snapshot))
            {
                snapshot.Document.ReleaseSnapshot(snapshot);
                _snapshots.Remove(number);
            }

            return new JsonObject();
        }

        /// <summary>Refuses while a command runs in the drawing: its group of edits is open, and what it holds would not survive.</summary>
        private void Busy(CadDocument document, string what)
        {
            CadCommandSession session = _host.SessionOf(document);

            if (document.IsInGroup || (session != null && session.IsRunning))
            {
                throw new Refusal(
                    "The drawing "
                        + document.Title
                        + " is busy with the command "
                        + (session?.Current?.Name ?? "running in it")
                        + ": end it, then "
                        + what
                        + " again."
                );
            }
        }
    }
}
