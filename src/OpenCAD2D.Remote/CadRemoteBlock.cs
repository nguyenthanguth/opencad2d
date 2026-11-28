namespace OpenCAD2D.Remote
{
    /// <summary>
    /// A block summary of a drawing of a running OpenCAD2D, as <see cref="CadRemoteDocument.GetBlocks"/> lists it without
    /// copying the block's objects: its name and how many entities it holds.
    /// </summary>
    public sealed class CadRemoteBlock
    {
        internal static CadRemoteBlock FromJson(JsonObject json) =>
            new CadRemoteBlock
            {
                Name = json.GetString("name") ?? string.Empty,
                Entities = (int)json.GetLong("entities"),
            };

        /// <summary>Gets the name of the block.</summary>
        public string Name { get; private set; }

        /// <summary>Gets the number of non-erased entities in the block's definition.</summary>
        public int Entities { get; private set; }
    }
}
