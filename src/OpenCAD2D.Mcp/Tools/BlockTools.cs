using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Linq;
using GeometryHelper.Dxf;
using ModelContextProtocol.Server;

namespace OpenCAD2D.Mcp
{
    internal sealed partial class DrawingTools
    {
        [McpServerTool(Name = "get_block", ReadOnly = true, Destructive = false, Idempotent = true, OpenWorld = false)]
        [Description(
            "The definition of a block: its base point, units, whether it can be exploded, its attribute definitions (tag, prompt, default text, flags) and the entities it holds, as query_entities describes them (with no handle: the parts of a definition are not entities of the drawing). Take a name from list_blocks. The entities are paged."
        )]
        public BlockDetail GetBlock(
            [Description("The name of the block, either case.")] string name,
            [Description("The page of entities, numbered from 1.")] int page = 1,
            [Description("The entities of a page, 1 to 500.")] int page_size = DefaultPageSize
        ) => _session.Run("get_block", false, drawing =>
            {
                DxfBlockTableRecord block = BlockNamed(drawing, name);
                List<DxfEntity> parts = block.Where(e => !e.IsErased).ToList();
                var result = new BlockDetail
                {
                    Name = block.Name,
                    BasePoint = new[] { EntityDescriber.R(block.Origin.X), EntityDescriber.R(block.Origin.Y) },
                    Units = block.Units.ToString(),
                    Explodable = block.Explodable,
                    AttributeDefinitions = parts.OfType<DxfAttributeDefinition>().Select(DefinitionRow).ToList(),
                };
                List<DxfEntity> shown = Page(
                    parts.Where(e => !(e is DxfAttributeDefinition)).ToList(),
                    page,
                    page_size,
                    result
                );
                result.Entities = shown.Select(e => EntityDescriber.Describe(new EntityRef(null, e))).ToList();
                return result;
            });

        private static DxfBlockTableRecord BlockNamed(IDrawingBackend drawing, string name)
        {
            string wanted = name?.Trim();

            if (string.IsNullOrEmpty(wanted))
            {
                throw new ArgumentException("Give the name of a block: list_blocks shows the ones the drawing has.");
            }

            return drawing
                .Blocks()
                .FirstOrDefault(b => string.Equals(b.Name, wanted, StringComparison.OrdinalIgnoreCase))
                ?? throw new ArgumentException(
                    "The drawing has no block " + wanted + ": list_blocks shows the ones it has."
                );
        }

        private static AttributeRow DefinitionRow(DxfAttributeDefinition definition) =>
            new AttributeRow
            {
                Tag = definition.Tag,
                Value = definition.TextString,
                Prompt = definition.Prompt,
                Constant = definition.Constant,
                Invisible = definition.Invisible,
                At = new[] { EntityDescriber.R(definition.Position.X), EntityDescriber.R(definition.Position.Y) },
                Height = EntityDescriber.R(definition.Height),
            };

        [McpServerTool(
            Name = "get_attributes",
            ReadOnly = true,
            Destructive = false,
            Idempotent = true,
            OpenWorld = false
        )]
        [Description(
            "The attributes of a block reference: the tag and value of each, whether it is constant or hidden, where it stands and how high it is, and the tags the block defines that the reference has no attribute for. The handle is of an INSERT."
        )]
        public AttributesResult GetAttributes([Description("The handle of the block reference.")] string handle) =>
            _session.Run("get_attributes", false, drawing =>
            {
                EntityRef item = Found(drawing, handle);

                if (!(item.Entity is DxfBlockReference insert))
                {
                    throw new ArgumentException(
                        "The object " + item.Handle + " is a " + item.Entity.DxfName + ", not an INSERT."
                    );
                }

                List<AttributeRow> rows = EntityDetails
                    .Attributes(insert)
                    .Select(a => new AttributeRow
                    {
                        Tag = a.Tag,
                        Value = a.TextString,
                        Constant = a.IsConstant,
                        Invisible = a.Invisible,
                        At = new[] { EntityDescriber.R(a.Position.X), EntityDescriber.R(a.Position.Y) },
                        Height = EntityDescriber.R(a.Height),
                    })
                    .ToList();
                DxfBlockTableRecord block = drawing
                    .Blocks()
                    .FirstOrDefault(b => string.Equals(b.Name, insert.Name, StringComparison.OrdinalIgnoreCase));
                List<string> missing =
                    block == null
                        ? new List<string>()
                        : block
                            .OfType<DxfAttributeDefinition>()
                            .Where(d =>
                                !d.IsErased
                                && !d.Constant
                                && !rows.Any(r => string.Equals(r.Tag, d.Tag, StringComparison.OrdinalIgnoreCase))
                            )
                            .Select(d => d.Tag)
                            .ToList();

                foreach (AttributeRow row in rows)
                {
                    DxfAttributeDefinition definition = block
                        ?.OfType<DxfAttributeDefinition>()
                        .FirstOrDefault(d => string.Equals(d.Tag, row.Tag, StringComparison.OrdinalIgnoreCase));
                    row.Prompt = definition?.Prompt;
                }

                return new AttributesResult
                {
                    Handle = item.Handle,
                    Block = insert.Name,
                    Attributes = rows,
                    Missing = missing,
                };
            });
    }
}
