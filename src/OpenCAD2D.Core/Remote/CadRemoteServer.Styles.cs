using System;
using System.Collections.Generic;
using System.Linq;
using GeometryHelper;
using GeometryHelper.Dxf;
using OpenCAD2D.Remote;

namespace OpenCAD2D
{
    /// <summary>The text styles, dimension styles and blocks of a drawing, read from outside.</summary>
    /// <remarks>
    /// A table record travels in the small drawing the objects travel in (<see cref="RemoteFragment"/>), copied into it as the
    /// library copies the records an object brings, with the records it points at: a dimension style comes with its text style,
    /// its linetypes and its arrowhead blocks. The answer names the records wanted, in order, as the fragment holds more
    /// (the Standard and ISO-25 of every new drawing, which the drawing's own take the place of).
    /// </remarks>
    public sealed partial class CadRemoteServer
    {
        private static JsonObject TextStyles(CadDocument document)
        {
            DxfDatabase source = document.Database;
            DxfDatabase fragment = new DxfDatabase(RemoteFragment.Version);
            List<string> names = Sorted(source.TextStyleTable.Select(s => s.Name));
            Carry(fragment, source, "STYLE", fragment.TextStyleTable, names);
            return Records(fragment, names);
        }

        private static JsonObject DimStyles(CadDocument document)
        {
            DxfDatabase source = document.Database;
            DxfDatabase fragment = new DxfDatabase(RemoteFragment.Version);
            List<DxfDimStyleTableRecord> styles = source
                .DimStyleTable.OrderBy(s => s.Name, StringComparer.OrdinalIgnoreCase)
                .ThenBy(s => s.Name, StringComparer.Ordinal)
                .ToList();

            // What the styles point at comes first, so that the copies point at copies: the text styles and linetypes they name,
            // and the blocks they draw their arrowheads by.
            Carry(
                fragment,
                source,
                "STYLE",
                fragment.TextStyleTable,
                Sorted(styles.Select(s => s.Dimtxsty?.Name).Where(n => n != null))
            );
            Carry(
                fragment,
                source,
                "LTYPE",
                fragment.LinetypeTable,
                Sorted(
                    styles
                        .SelectMany(s => new DxfLinetypeTableRecord[] { s.Dimltype, s.Dimltex1, s.Dimltex2 })
                        .Where(l => l != null)
                        .Select(l => l.Name)
                )
            );
            CarryBlocks(
                fragment,
                source,
                styles
                    .SelectMany(s => new DxfBlockTableRecord[] { s.Dimblk, s.Dimblk1, s.Dimblk2, s.Dimldrblk })
                    .Where(b => b != null && !b.IsDependent)
                    .GroupBy(b => b.Name, StringComparer.OrdinalIgnoreCase)
                    .Select(g => g.First())
                    .OrderBy(b => b.Name, StringComparer.OrdinalIgnoreCase)
                    .ThenBy(b => b.Name, StringComparer.Ordinal)
                    .ToList()
            );

            List<string> names = styles.Select(s => s.Name).ToList();
            Carry(fragment, source, "DIMSTYLE", fragment.DimStyleTable, names);
            return Records(fragment, names);
        }

        private static JsonObject Blocks(CadDocument document)
        {
            DxfDatabase source = document.Database;
            DxfDatabase fragment = new DxfDatabase(RemoteFragment.Version);
            List<CarriedBlock> blocks = source
                .BlockTable.Where(b =>
                    !b.IsLayout && !b.IsAnonymous && !b.Name.StartsWith("*", StringComparison.Ordinal)
                )
                .Select(b => new CarriedBlock(b, b.Name, source, IsMarkedDependent(b), b.IsResolved))
                .ToList();

            // The blocks the loaded references bring (grid|TAG), which AutoCAD lists with the drawing's own; one the drawing
            // holds a record of already is left as that record has it.
            foreach (CadDependentBlock dependent in CadXrefBlocks.Of(document).Dependents)
            {
                if (!source.BlockTable.Has(dependent.Name))
                {
                    blocks.Add(new CarriedBlock(dependent.Block, dependent.Name, dependent.Source, true, true));
                }
            }

            blocks = blocks
                .OrderBy(b => b.Name, StringComparer.OrdinalIgnoreCase)
                .ThenBy(b => b.Name, StringComparer.Ordinal)
                .ToList();
            CarryBlocks(fragment, blocks);
            return Records(fragment, blocks.Select(b => b.Name).Where(n => fragment.BlockTable.Has(n)).ToList());
        }

        /// <summary>Whether a block is one an external reference brought, by the | in its name; the flag alone does not make the small drawing take it.</summary>
        private static bool IsMarkedDependent(DxfBlockTableRecord block) =>
            block.IsDependent && block.Name.IndexOf('|') >= 0;

        /// <summary>
        /// The blocks the loaded external references of a document bring (<c>grid|TAG</c>), each with its objects, in a small
        /// drawing of their own, in the order of their names: what <see cref="CadXrefManager.GetDependentBlocks"/> gives.
        /// </summary>
        internal static IReadOnlyList<DxfBlockTableRecord> DependentBlocks(CadDocument document)
        {
            DxfDatabase fragment = new DxfDatabase(RemoteFragment.Version);
            List<CarriedBlock> blocks = CadXrefBlocks
                .Of(document)
                .Dependents.Select(d => new CarriedBlock(d.Block, d.Name, d.Source, true, true))
                .ToList();
            CarryBlocks(fragment, blocks);
            return blocks.Where(b => fragment.BlockTable.Has(b.Name)).Select(b => fragment.BlockTable[b.Name]).ToList();
        }

        private static JsonObject Records(DxfDatabase fragment, List<string> names)
        {
            var list = new JsonArray();

            foreach (string name in names)
            {
                list.Add(name);
            }

            return new JsonObject().Set("fragment", fragment.ToText()).Set("names", list);
        }

        /// <summary>Names sorted by case-blind order, then by ordinal order, each once.</summary>
        private static List<string> Sorted(IEnumerable<string> names) =>
            names
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .OrderBy(n => n, StringComparer.OrdinalIgnoreCase)
                .ThenBy(n => n, StringComparer.Ordinal)
                .ToList();

        /// <summary>
        /// Copies the records of a table by name into the small drawing. A record the small drawing has of the name, one of
        /// the two it is made with, is taken out first, as the copy keeps the one a drawing has.
        /// </summary>
        private static void Carry(
            DxfDatabase fragment,
            DxfDatabase source,
            string table,
            DxfSymbolTable into,
            List<string> names
        )
        {
            foreach (string name in names)
            {
                if (into.Has(name))
                {
                    fragment.File.RemoveTableRecord(into[name].Item, table);
                }
            }

            fragment.File.CopyTableRecords(source.File, table, names);
        }

        /// <summary>A block to carry: as it is in the drawing it comes from, under the name it is to have in the small drawing.</summary>
        private sealed class CarriedBlock
        {
            internal CarriedBlock(
                DxfBlockTableRecord block,
                string name,
                DxfDatabase source,
                bool dependent,
                bool resolved
            )
            {
                Block = block;
                Name = name;
                Source = source;
                Dependent = dependent;
                Resolved = resolved;
            }

            internal DxfBlockTableRecord Block { get; }

            internal string Name { get; }

            /// <summary>The drawing that holds <see cref="Block"/> and its objects.</summary>
            internal DxfDatabase Source { get; }

            /// <summary>Whether an external reference brought it, so that the small drawing marks it so (flag 16).</summary>
            internal bool Dependent { get; }

            /// <summary>Whether that reference is resolved (flag 32).</summary>
            internal bool Resolved { get; }
        }

        private static void CarryBlocks(DxfDatabase fragment, DxfDatabase source, List<DxfBlockTableRecord> blocks) =>
            CarryBlocks(
                fragment,
                blocks.Select(b => new CarriedBlock(b, b.Name, source, IsMarkedDependent(b), b.IsResolved)).ToList()
            );

        /// <summary>
        /// Makes blocks in the small drawing, as <see cref="DxfDatabase.Insert(GeometryHelper.Geometry.GeoTransform3, DxfDatabase, bool)"/>
        /// merges them: every record first, then the objects of each, so that an INSERT of one block in another places the
        /// block the small drawing has of that name. An external reference is carried as its name and path alone. A block an
        /// external reference brought (<c>grid|TAG</c>) is marked so, flagged 48 where the reference is resolved, and carried
        /// with the objects of its own drawing, naming what they draw by as that drawing does; one whose name the small
        /// drawing cannot take is left out.
        /// </summary>
        private static void CarryBlocks(DxfDatabase fragment, List<CarriedBlock> blocks)
        {
            var made = new List<CarriedBlock>();

            foreach (CarriedBlock carried in blocks)
            {
                DxfBlockTableRecord block = carried.Block;

                if (fragment.BlockTable.Has(carried.Name))
                {
                    continue;
                }

                var record = new DxfBlockTableRecord
                {
                    Name = carried.Name,
                    Origin = block.Origin,
                    MadeFlags =
                        (block.HasAttributeDefinitions ? 2 : 0)
                        | (block.IsFromExternalReference ? 4 : 0)
                        | (block.IsFromOverlayReference ? 8 : 0),
                };

                if (block.IsFromExternalReference || block.IsFromOverlayReference)
                {
                    record.PathName = block.PathName;
                }
                else
                {
                    record.Comments = block.Comments;
                    record.Units = block.Units;
                    record.Explodable = block.Explodable;
                    record.BlockScaling = block.BlockScaling;
                }

                if (carried.Dependent)
                {
                    record.SetDependent(true, carried.Resolved);
                }

                try
                {
                    fragment.BlockTable.Add(record);
                }
                catch (ArgumentException e) when (carried.Dependent)
                {
                    GeometryHelperLog.Debug(
                        "CadRemoteServer: the block " + carried.Name + " is not carried: " + e.Message
                    );
                    continue;
                }

                if (block.Annotative == DxfAnnotativeStates.True)
                {
                    record.XData = block.GetXDataForApplication("AcadAnnotative");
                }

                if (!block.IsFromExternalReference && !block.IsFromOverlayReference)
                {
                    made.Add(carried);
                }
            }

            foreach (CarriedBlock carried in made)
            {
                var objects = new List<DxfEntity>(carried.Block);

                if (objects.Count > 0)
                {
                    carried.Source.WblockCloneObjects(objects, fragment.BlockTable[carried.Name]);
                }
            }
        }
    }
}
