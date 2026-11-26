namespace OpenCAD2D.Remote
{
    /// <summary>
    /// A drawing of the app as it stood when <see cref="CadRemoteDocument.TakeSnapshot"/> kept it, to be put back with
    /// <see cref="CadRemoteDocument.Reset"/>. The app holds the drawing (compressed, about 0.2 MB for 10 000 entities) until
    /// <see cref="CadRemoteDocument.Release"/> or until the drawing is closed.
    /// </summary>
    public sealed class CadRemoteSnapshot
    {
        internal CadRemoteSnapshot(CadRemoteDocument document, long id)
        {
            Document = document;
            Id = id;
        }

        /// <summary>Gets the drawing the snapshot is of.</summary>
        public CadRemoteDocument Document { get; }

        /// <summary>Gets the number the app knows the snapshot by.</summary>
        internal long Id { get; }
    }
}
