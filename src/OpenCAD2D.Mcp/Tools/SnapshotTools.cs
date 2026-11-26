using System.ComponentModel;
using System.Linq;
using ModelContextProtocol.Server;

namespace OpenCAD2D.Mcp
{
    internal sealed partial class DrawingTools
    {
        [McpServerTool(Name = "snapshot", ReadOnly = false, Destructive = false, Idempotent = false, OpenWorld = false)]
        [Description(
            "Keeps the drawing as it stands (with changes not saved) and gives an id such as s1 to put it back to with reset: try a layout, look at it, and roll it back. It costs next to nothing where the drawing keeps its undo list, and the drawing itself is not changed. In a server attached to an app the app holds the snapshot until release_snapshot or until the drawing is closed. Refused while a command runs in the drawing."
        )]
        public SnapshotResult Snapshot() => _session.Run("snapshot", false, drawing =>
            {
                IQueryBackend query = Q(drawing);
                object snapshot = query.TakeSnapshot();
                return new SnapshotResult
                {
                    Snapshot = _session.Snapshots.Add(query.DrawingKey, snapshot),
                    Drawing = drawing.Name,
                    Entities = drawing.Entities("Model").Count,
                };
            });

        [McpServerTool(Name = "reset", ReadOnly = false, Destructive = true, Idempotent = true, OpenWorld = false)]
        [Description(
            "Puts the drawing back as a snapshot kept it: its entities with their handles, tables, layouts and the current layer and styles. What was done since is gone, and it is not an undo step: undo cannot bring it back (what was done before the snapshot can still be undone). The snapshot stays, so the drawing can be reset to it again. Refused when the server is read-only."
        )]
        public ResetResult Reset([Description("The id snapshot gave, such as s1.")] string snapshot) =>
            _session.Run("reset", true, drawing =>
            {
                IQueryBackend query = Q(drawing);
                object kept = _session.Snapshots.Take(snapshot, query.DrawingKey);
                query.ResetTo(kept);
                return new ResetResult
                {
                    Snapshot = snapshot.Trim(),
                    Done = "reset",
                    Entities = drawing.Entities("Model").Count,
                    Note = "The steps since the snapshot are forgotten, not made one more undo step.",
                };
            });

        [McpServerTool(
            Name = "release_snapshot",
            ReadOnly = false,
            Destructive = false,
            Idempotent = true,
            OpenWorld = false
        )]
        [Description(
            "Lets go of a snapshot and the memory it holds. One already released is no error. Snapshots are also released when the drawing is closed or replaced and when the server ends."
        )]
        public ResetResult ReleaseSnapshot([Description("The id snapshot gave, such as s1.")] string snapshot) =>
            _session.RunWithout(() =>
            {
                bool had = _session.Snapshots.Release(snapshot);
                return new ResetResult
                {
                    Snapshot = snapshot?.Trim(),
                    Done = "released",
                    Note = had ? null : "There was no such snapshot, so nothing was released.",
                };
            });
    }
}
