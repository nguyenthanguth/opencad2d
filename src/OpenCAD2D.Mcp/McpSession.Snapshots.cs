namespace OpenCAD2D.Mcp
{
    internal sealed partial class McpSession
    {
        /// <summary>Gets the snapshots the agent has taken of the drawings it works on.</summary>
        public SnapshotStore Snapshots { get; } = new SnapshotStore();
    }
}
