using OpenCAD2D.Remote;

namespace OpenCAD2D
{
    /// <summary>The whole drawing as the app has it now, asked from outside: for a program that draws or checks it in its own process.</summary>
    public sealed partial class CadRemoteServer
    {
        /// <summary>
        /// The drawing as DXF text, written by the core's writer from the document's database as it stands: the header, the tables,
        /// the blocks, the layouts with their viewports and every object, with the changes not saved included. Nothing is written
        /// to the drawing's file.
        /// </summary>
        private static JsonObject DatabaseText(CadDocument document) =>
            new JsonObject().Set("text", document.Database.ToText());
    }
}
