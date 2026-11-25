using System;
using System.Collections.Generic;
using GeometryHelper.Dxf;

namespace OpenCAD2D
{
    /// <summary>
    /// A block the drawing of a loaded external reference brings into the drawing that attaches it, <c>grid|TAG</c> for the
    /// block TAG of the drawing <c>grid</c>, which AutoCAD 2021 lists in the block table (-BLOCK ? says <c>Xdep: "grid"</c>,
    /// <c>tblnext</c> says <c>70 48</c>) and refuses to insert ("Cannot insert dependent block.").
    /// </summary>
    internal sealed class CadDependentBlock
    {
        internal CadDependentBlock(string name, string reference, DxfBlockTableRecord block, DxfDatabase source)
        {
            Name = name;
            Reference = reference;
            Block = block;
            Source = source;
        }

        /// <summary>The name in the drawing that attaches it: the reference's name, a | and the block's own.</summary>
        internal string Name { get; }

        /// <summary>The name of the external reference that brings it.</summary>
        internal string Reference { get; }

        /// <summary>The block as the drawing of the reference has it.</summary>
        internal DxfBlockTableRecord Block { get; }

        /// <summary>The drawing of the reference, which holds <see cref="Block"/> and its objects.</summary>
        internal DxfDatabase Source { get; }
    }

    /// <summary>
    /// The external references of a drawing and the blocks their drawings bring, as AutoCAD 2021 lists them in the block
    /// table while the references are loaded and writes none of them to a DXF or a DWG: each reference not unloaded and whose
    /// file is read brings the named blocks of its drawing (no layout, no block AutoCAD names itself, no reference or block of a
    /// reference of its own) as <c>grid|TAG</c>, a reference nested in its drawing as a reference of its own, as AutoCAD names
    /// it (<c>inner</c> and <c>inner|INNERB</c>). Measured with accoreconsole on drawings attached, unloaded, reloaded and
    /// detached: unloading and detaching take them away, reloading brings them back.
    /// </summary>
    internal sealed class CadXrefBlocks
    {
        private CadXrefBlocks(List<KeyValuePair<string, string>> references, List<CadDependentBlock> dependents)
        {
            References = references;
            Dependents = dependents;
        }

        /// <summary>The references the list shows, each by name and by where it stands in AutoCAD's words (resolved, unloaded, not found), in the order of their names.</summary>
        internal IReadOnlyList<KeyValuePair<string, string>> References { get; }

        /// <summary>The blocks the loaded references bring, in the order of their names.</summary>
        internal IReadOnlyList<CadDependentBlock> Dependents { get; }

        /// <summary>Reads the references of a document and what the drawings of the loaded ones bring.</summary>
        internal static CadXrefBlocks Of(CadDocument document)
        {
            var references = new List<KeyValuePair<string, string>>();
            var dependents = new List<CadDependentBlock>();

            foreach (CadExternalReference row in document.Xrefs.GetReferences())
            {
                string word = Word(row, document.Xrefs);

                if (row.IsImage || row.IsPdf || word == null)
                {
                    continue;
                }

                references.Add(new KeyValuePair<string, string>(row.Name, word));

                if (word != "resolved")
                {
                    continue;
                }

                DxfDatabase source = document.Xrefs.Read(row.FoundAt).Database;

                if (source == null)
                {
                    continue;
                }

                foreach (DxfBlockTableRecord block in source.BlockTable)
                {
                    if (
                        !block.IsLayout
                        && !block.IsAnonymous
                        && !block.IsFromExternalReference
                        && !block.IsFromOverlayReference
                        && !block.IsDependent
                        && !block.Name.StartsWith("*", StringComparison.Ordinal)
                    )
                    {
                        dependents.Add(new CadDependentBlock(row.Name + "|" + block.Name, row.Name, block, source));
                    }
                }
            }

            references.Sort((a, b) => ByName(a.Key, b.Key));
            dependents.Sort((a, b) => ByName(a.Name, b.Name));
            return new CadXrefBlocks(references, dependents);
        }

        /// <summary>
        /// Where a reference stands in the words of -BLOCK ?: resolved (loaded, its INSERTs erased or not), unloaded, not found
        /// or unresolved; null for one the list shows none of (a nested one whose parent is not loaded).
        /// </summary>
        private static string Word(CadExternalReference row, CadXrefManager xrefs)
        {
            switch (row.Status)
            {
                case CadReferenceStatus.Loaded:
                case CadReferenceStatus.NeedsReloading:
                    return "resolved";
                case CadReferenceStatus.Unreferenced:
                    return xrefs.IsUnloaded(row.Name) ? "unloaded"
                        : row.FoundAt != null ? "resolved"
                        : "not found";
                case CadReferenceStatus.Unloaded:
                    return "unloaded";
                case CadReferenceStatus.NotFound:
                    return "not found";
                case CadReferenceStatus.Unresolved:
                    return "unresolved";
                default:
                    return null;
            }
        }

        private static int ByName(string a, string b)
        {
            int byName = string.Compare(a, b, StringComparison.OrdinalIgnoreCase);
            return byName != 0 ? byName : string.CompareOrdinal(a, b);
        }
    }
}
