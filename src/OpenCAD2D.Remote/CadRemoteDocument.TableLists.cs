using System.Collections.Generic;

namespace OpenCAD2D.Remote
{
    /// <summary>The layouts, linetypes and block summaries of a drawing, asked without copying its database or objects.</summary>
    public sealed partial class CadRemoteDocument
    {
        /// <summary>
        /// Gets the spaces of the drawing in tab order (<c>Model</c> first, then each layout), with each sheet's paper size and
        /// units, rotation, entity count and viewports. The drawing itself is not copied.
        /// </summary>
        /// <returns>The spaces, starting with <c>Model</c>.</returns>
        /// <exception cref="CadRemoteException">Thrown when the drawing is closed.</exception>
        public IReadOnlyList<CadRemoteLayout> GetLayouts()
        {
            var layouts = new List<CadRemoteLayout>();

            foreach (object item in Ask(RemoteOps.LayoutDetails).GetArray("layouts"))
            {
                layouts.Add(CadRemoteLayout.FromJson((JsonObject)item));
            }

            return layouts;
        }

        /// <summary>
        /// Gets the linetypes of the drawing, sorted by name without regard to case, then by ordinal order, with their
        /// descriptions, pattern lengths and dashes. The drawing itself is not copied.
        /// </summary>
        /// <returns>The linetypes; a new drawing has ByBlock, ByLayer and Continuous.</returns>
        /// <exception cref="CadRemoteException">Thrown when the drawing is closed.</exception>
        public IReadOnlyList<CadRemoteLinetype> GetLinetypes()
        {
            var linetypes = new List<CadRemoteLinetype>();

            foreach (object item in Ask(RemoteOps.LinetypeDetails).GetArray("linetypes"))
            {
                linetypes.Add(CadRemoteLinetype.FromJson((JsonObject)item));
            }

            return linetypes;
        }

        /// <summary>
        /// Gets the blocks of the drawing that an INSERT can place (layout blocks, anonymous blocks and external references left
        /// out), sorted by name without regard to case, then by ordinal order, with the entity count of each. Unlike
        /// <see cref="Blocks"/>, the entities of the definitions are not copied.
        /// </summary>
        /// <returns>The block summaries.</returns>
        /// <exception cref="CadRemoteException">Thrown when the drawing is closed.</exception>
        public IReadOnlyList<CadRemoteBlock> GetBlocks()
        {
            var blocks = new List<CadRemoteBlock>();

            foreach (object item in Ask(RemoteOps.BlockSummaries).GetArray("blocks"))
            {
                blocks.Add(CadRemoteBlock.FromJson((JsonObject)item));
            }

            return blocks;
        }
    }
}
