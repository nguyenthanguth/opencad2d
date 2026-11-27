using System.Collections.Generic;

namespace OpenCAD2D.Remote
{
    /// <summary>The multileader styles and table styles of a drawing.</summary>
    public sealed partial class CadRemoteDocument
    {
        /// <summary>
        /// Gets the multileader styles of the drawing, sorted by name without regard to case, then by ordinal order. They are
        /// copies, asked of the app each time, and the drawing itself is not copied: a drawing of 10000 objects costs no more
        /// than one of none.
        /// </summary>
        /// <returns>The styles; a new drawing has Standard.</returns>
        /// <exception cref="CadRemoteException">Thrown when the drawing is closed.</exception>
        public IReadOnlyList<CadRemoteMultileaderStyle> GetMultileaderStyles()
        {
            var styles = new List<CadRemoteMultileaderStyle>();

            foreach (
                object item in Ask(RemoteOps.AnnotationStyles, r => r.Set("kind", "multileader"))
                    .GetArray("multileaderStyles")
            )
            {
                styles.Add(CadRemoteMultileaderStyle.FromJson((JsonObject)item));
            }

            return styles;
        }

        /// <summary>
        /// Gets the table styles of the drawing, sorted by name without regard to case, then by ordinal order. They are copies,
        /// asked of the app each time, and the drawing itself is not copied.
        /// </summary>
        /// <returns>The styles; a new drawing has Standard.</returns>
        /// <exception cref="CadRemoteException">Thrown when the drawing is closed.</exception>
        public IReadOnlyList<CadRemoteTableStyle> GetTableStyles()
        {
            var styles = new List<CadRemoteTableStyle>();

            foreach (
                object item in Ask(RemoteOps.AnnotationStyles, r => r.Set("kind", "table")).GetArray("tableStyles")
            )
            {
                styles.Add(CadRemoteTableStyle.FromJson((JsonObject)item));
            }

            return styles;
        }
    }
}
