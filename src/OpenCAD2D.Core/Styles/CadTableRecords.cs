using System;
using System.Collections.Generic;
using System.Linq;
using GeometryHelper.Dxf;

namespace OpenCAD2D
{
    /// <summary>
    /// Table records of one drawing brought into another by name, as a commit of the Python <c>Drawing</c> and of a remote
    /// client adds the styles, linetypes and blocks it made in a small drawing of its own.
    /// </summary>
    public static class CadTableRecords
    {
        /// <summary>
        /// Copies the named text styles, linetypes, blocks (each with its objects) and dimension styles of another drawing into a
        /// drawing, in that order, so that a dimension style points at the text style and the blocks the drawing then has: the
        /// way the library merges the tables of a drawing it inserts, but only the records named, so that the defaults a small
        /// drawing is made with (<c>ISO-25</c>) are not added to a drawing that lacks them. Each name is of a record the source
        /// has and the drawing has not. Writes no record of the edit: the caller's <see cref="CadEditScope"/> does.
        /// </summary>
        /// <param name="database">The drawing the records go into.</param>
        /// <param name="source">The drawing they are copied from.</param>
        /// <param name="textStyles">The names of the text styles.</param>
        /// <param name="linetypes">The names of the linetypes.</param>
        /// <param name="blocks">The names of the blocks.</param>
        /// <param name="dimensionStyles">The names of the dimension styles.</param>
        /// <exception cref="ArgumentNullException">Thrown when a drawing or a list is null.</exception>
        /// <exception cref="ArgumentException">Thrown when a name is of a record the source has not, or of one the drawing has.</exception>
        public static void Add(
            DxfDatabase database,
            DxfDatabase source,
            IReadOnlyList<string> textStyles,
            IReadOnlyList<string> linetypes,
            IReadOnlyList<string> blocks,
            IReadOnlyList<string> dimensionStyles
        )
        {
            if (database == null)
            {
                throw new ArgumentNullException(nameof(database));
            }

            if (source == null)
            {
                throw new ArgumentNullException(nameof(source));
            }

            Check(database.TextStyleTable, source.TextStyleTable, textStyles, "text style");
            Check(database.LinetypeTable, source.LinetypeTable, linetypes, "linetype");
            Check(database.BlockTable, source.BlockTable, blocks, "block");
            Check(database.DimStyleTable, source.DimStyleTable, dimensionStyles, "dimension style");
            database.File.CopyTableRecords(source.File, "STYLE", textStyles.ToList());
            database.File.CopyTableRecords(source.File, "LTYPE", linetypes.ToList());
            CadRemoteServer.CarryBlocks(database, source, blocks.Select(n => source.BlockTable[n]).ToList());
            database.File.CopyTableRecords(source.File, "DIMSTYLE", dimensionStyles.ToList());
        }

        private static void Check(DxfSymbolTable have, DxfSymbolTable sent, IReadOnlyList<string> names, string word)
        {
            if (names == null)
            {
                throw new ArgumentNullException(nameof(names));
            }

            foreach (string name in names)
            {
                if (string.IsNullOrEmpty(name) || !sent.Has(name))
                {
                    throw new ArgumentException("The source has no " + word + " " + name + ".", nameof(names));
                }

                if (have.Has(name))
                {
                    throw new ArgumentException(
                        "The drawing has the " + word + " " + name + " already.",
                        nameof(names)
                    );
                }
            }
        }
    }
}
