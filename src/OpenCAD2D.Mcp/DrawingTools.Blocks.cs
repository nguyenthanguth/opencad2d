using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Globalization;
using System.Linq;
using GeometryHelper.Dxf;
using GeometryHelper.Geometry;
using ModelContextProtocol.Server;

namespace OpenCAD2D.Mcp
{
    /// <summary>The tools of blocks: defining one from objects, attribute definitions, inserting a block with its attributes, setting an attribute.</summary>
    internal sealed partial class DrawingTools
    {
        [McpServerTool(
            Name = "add_attribute_definition",
            ReadOnly = false,
            Destructive = false,
            Idempotent = false,
            OpenWorld = false
        )]
        [Description(
            "Adds an attribute definition (ATTDEF): a tag with a prompt and a default value that a block, defined with define_block, carries so that each insert of it holds its own words (a drawing number, a title). Add it where it should show inside the block, then pass its handle to define_block with the shapes. One undo step."
        )]
        public AddedResult AddAttributeDefinition(
            [Description(
                "The tag, one word with no space, as TITLE; it names the attribute in insert_block_with_attributes and set_attribute."
            )]
                string tag,
            [Description("Insertion point [x, y]: the lower left corner of the first letter.")] double[] at,
            [Description("Letter height in millimetres, above 0.")] double height,
            [Description("What an insert asks for; the tag where left out.")] string prompt = null,
            [Description("The value an insert has when none is given.")] string default_value = "",
            [Description("A text style of the drawing; Standard where left out.")] string text_style = null,
            [Description("True to hide the attribute's words.")] bool invisible = false,
            [Description("True for a constant attribute: its value is fixed in the block and no insert asks for it.")]
                bool constant = false,
            [Description(LayerText)] string layer = null,
            [Description(ColorText)] int? color = null,
            [Description(LayoutText)] string layout = null
        ) => Add("add_attribute_definition", layout, _ =>
            {
                string name = tag?.Trim();

                if (string.IsNullOrEmpty(name) || name.Any(char.IsWhiteSpace))
                {
                    throw new ArgumentException("The tag is one word with no space in it.");
                }

                if (!(height > 0.0) || double.IsInfinity(height))
                {
                    throw new ArgumentException("The height of an attribute is above nought.");
                }

                var definition = new DxfAttributeDefinition(
                    Point("at", at),
                    default_value ?? string.Empty,
                    name,
                    string.IsNullOrEmpty(prompt) ? name : prompt,
                    null
                )
                {
                    Height = height,
                    Invisible = invisible,
                    Constant = constant,
                };

                if (!string.IsNullOrEmpty(text_style))
                {
                    definition.TextStyleName = text_style;
                }

                return EntityFactory.Looks(definition, layer, color, null);
            });

        /// <summary>The attribute definitions of a block that an INSERT gives its words, in the order of the block.</summary>
        private static List<DxfAttributeDefinition> AskedFor(IDrawingBackend drawing, string block)
        {
            DxfBlockTableRecord record = drawing
                .Blocks()
                .FirstOrDefault(b => string.Equals(b.Name, block, StringComparison.OrdinalIgnoreCase));

            if (record == null)
            {
                throw new ArgumentException(
                    "The drawing has no block " + block + ": list_blocks shows the ones it has."
                );
            }

            return record.OfType<DxfAttributeDefinition>().Where(d => !d.Constant && !d.IsErased).ToList();
        }

        /// <summary>
        /// Inserts a block as one edit of the core, with the words of its attributes set and nothing typed: no name reaches a command
        /// line, no prompt is answered, and the Edit Attributes dialog of ATTDIA is never asked for. Gives the handle of the INSERT.
        /// </summary>
        private static string InsertBlock(
            IDrawingBackend drawing,
            string block,
            GeoPoint3 at,
            double scale,
            double rotation,
            IReadOnlyDictionary<string, string> values
        )
        {
            List<DxfAttributeDefinition> asked = AskedFor(drawing, block);
            var tags = new HashSet<string>(asked.Select(d => d.Tag), StringComparer.OrdinalIgnoreCase);

            foreach (
                string given in values
                    .Keys.OrderBy(k => k, StringComparer.OrdinalIgnoreCase)
                    .ThenBy(k => k, StringComparer.Ordinal)
            )
            {
                if (!tags.Contains(given))
                {
                    throw new ArgumentException(
                        "The block "
                            + block
                            + " has no attribute "
                            + given
                            + ": it asks for "
                            + (
                                tags.Count == 0
                                    ? "none"
                                    : string.Join(
                                        ", ",
                                        tags.OrderBy(t => t, StringComparer.OrdinalIgnoreCase)
                                            .ThenBy(t => t, StringComparer.Ordinal)
                                    )
                            )
                            + "."
                    );
                }
            }

            string[][] rows = values
                .OrderBy(v => v.Key, StringComparer.OrdinalIgnoreCase)
                .ThenBy(v => v.Key, StringComparer.Ordinal)
                .Select(v => new[] { v.Key, v.Value ?? string.Empty })
                .ToArray();
            return Editing(drawing)
                .Make(
                    "insert_block",
                    new CadMakeArgs()
                        .Set("name", block)
                        .Set("at", new[] { at.X, at.Y })
                        .Set("scale", scale)
                        .Set("rotation", rotation)
                        .Set("rows", rows)
                )
                .FirstOrDefault()
                ?? throw new InvalidOperationException("The block " + block + " was not inserted.");
        }

        [McpServerTool(
            Name = "insert_block_with_attributes",
            ReadOnly = false,
            Destructive = false,
            Idempotent = false,
            OpenWorld = false
        )]
        [Description(
            "Places a block of the drawing at a point, as -INSERT, and fills its attributes: values gives the words by tag ({\"TITLE\": \"PLATE\", \"NUMBER\": \"A-101\"}); a tag left out keeps the block's default. The block's constant attributes are not asked for. One undo step."
        )]
        public AddedResult InsertBlockWithAttributes(
            [Description("The name of a block of the drawing (list_blocks).")] string name,
            [Description("Insertion point [x, y].")] double[] at,
            [Description("The words of the attributes by tag.")] Dictionary<string, string> values = null,
            [Description("Scale factor, above 0.")] double scale = 1.0,
            [Description("Rotation in degrees, counter-clockwise.")] double rotation = 0.0,
            [Description(LayerText)] string layer = null,
            [Description(ColorText)] int? color = null,
            [Description(LayoutText)] string layout = null
        ) => _session.Run("insert_block_with_attributes", true, drawing =>
            {
                GeoPoint3 p = Point("at", at);

                if (!(scale > 0.0) || double.IsInfinity(scale) || double.IsNaN(rotation) || double.IsInfinity(rotation))
                {
                    throw new ArgumentException("The scale is above nought and the rotation a number.");
                }

                if (string.IsNullOrWhiteSpace(name) || name.Any(char.IsWhiteSpace))
                {
                    throw new ArgumentException("The name of a block is one word with no space in it.");
                }

                IDrawingEditing editing = Editing(drawing);
                string handle = null;
                editing.Group(() => InLayout(drawing, layout, () =>
                    {
                        handle = InsertBlock(
                            drawing,
                            name.Trim(),
                            p,
                            scale,
                            rotation,
                            values ?? new Dictionary<string, string>()
                        );
                        Look(editing, new[] { handle }, layer, color);
                    }));
                return new AddedResult { Handle = handle, Kind = "INSERT" };
            });

        [McpServerTool(
            Name = "set_attribute",
            ReadOnly = false,
            Destructive = false,
            Idempotent = true,
            OpenWorld = false
        )]
        [Description(
            "Sets the words of one attribute of a block reference that insert_block_with_attributes or add_insert placed, by its tag. Refused on a locked layer. One undo step."
        )]
        public ChangedResult SetAttribute(
            [Description("The handle of the block reference.")] string handle,
            [Description("The tag of the attribute.")] string tag,
            [Description("The new words.")] string value
        ) => _session.Run("set_attribute", true, drawing =>
            {
                Editing(drawing)
                    .Make(
                        "set_attribute",
                        new CadMakeArgs().Set("handle", handle).Set("tag", tag).Set("value", value ?? string.Empty)
                    );
                return new ChangedResult { Changed = 1, Handles = new[] { handle.Trim() } };
            });

        [McpServerTool(
            Name = "define_block",
            ReadOnly = false,
            Destructive = true,
            Idempotent = false,
            OpenWorld = false
        )]
        [Description(
            "Defines a block from objects, as BLOCK: the objects (attribute definitions among them) become a block of the name, whose base point is the point that lands on the insertion point of each insert. source says what becomes of the objects: keep (they stay as they are), convert (they are replaced by one insert of the block at the base point, with the attributes at their defaults) or delete (they are erased). One undo step."
        )]
        public BlockResult DefineBlock(
            [Description("The block's name: one word, none of < > / \\ \" : ; ? * | = ` , in it.")] string name,
            [Description("The base point [x, y].")] double[] base_point,
            [Description(TargetsText)] string[] handles,
            [Description("keep, convert or delete.")] string source = "convert"
        ) => _session.Run("define_block", true, drawing =>
            {
                string label = RecordName(name, "block");

                if (label.Any(char.IsWhiteSpace) || label.StartsWith("*", StringComparison.Ordinal))
                {
                    throw new ArgumentException("The name of a block is one word, not starting with *.");
                }

                GeoPoint3 b = Point("base_point", base_point);
                List<string> list = Handles(handles);
                string how = (source ?? string.Empty).Trim().ToLowerInvariant();

                if (how != "keep" && how != "convert" && how != "delete")
                {
                    throw new ArgumentException("source is keep, convert or delete.");
                }

                if (drawing.Blocks().Any(x => string.Equals(x.Name, label, StringComparison.OrdinalIgnoreCase)))
                {
                    throw new ArgumentException("Block \"" + label + "\" already exists.");
                }

                IReadOnlyList<EntityRef> found = Editing(drawing).Objects(list);
                var chosen = new List<EntityRef>();

                foreach (string handle in list)
                {
                    chosen.Add(
                        found.FirstOrDefault(e => string.Equals(e.Handle, handle, StringComparison.OrdinalIgnoreCase))
                            ?? throw new ArgumentException("The drawing has no object " + handle + ".")
                    );
                }

                var records = new DxfDatabase("AC1027");
                var block = new DxfBlockTableRecord { Name = label, Origin = b };
                records.BlockTable.Add(block);

                foreach (EntityRef item in chosen)
                {
                    DxfEntity copy = item.Entity.Clone();
                    EnsureRecords(drawing, records, copy);
                    block.AppendEntity(copy);
                }

                IDrawingEditing editing = Editing(drawing);
                string inserted = null;
                editing.Group(() =>
                {
                    editing.Apply(records, NoEntities, null, how == "keep" ? NoHandles : list);

                    if (how == "convert")
                    {
                        inserted = InsertBlock(drawing, label, b, 1.0, 0.0, new Dictionary<string, string>());
                    }
                });
                return new BlockResult
                {
                    Name = label,
                    Entities = chosen.Count,
                    Insert = inserted,
                    Erased = how == "keep" ? NoHandles : list,
                };
            });

        /// <summary>Gives the small drawing a block is defined in the text styles and linetypes an object names, which the drawing has.</summary>
        private static void EnsureRecords(IDrawingBackend drawing, DxfDatabase records, DxfEntity entity)
        {
            string style =
                entity is DxfDBText text ? text.TextStyleName
                : entity is DxfMText mtext ? mtext.TextStyleName
                : null;

            if (!string.IsNullOrEmpty(style) && !records.TextStyleTable.Has(style))
            {
                records.TextStyleTable.Add(new DxfTextStyleTableRecord { Name = style });
            }

            string linetype = entity.Linetype;

            if (
                !string.IsNullOrEmpty(linetype)
                && !records.LinetypeTable.Has(linetype)
                && !string.Equals(linetype, "BYLAYER", StringComparison.OrdinalIgnoreCase)
                && !string.Equals(linetype, "BYBLOCK", StringComparison.OrdinalIgnoreCase)
                && drawing.Linetypes().Any(l => string.Equals(l, linetype, StringComparison.OrdinalIgnoreCase))
            )
            {
                records.LinetypeTable.Add(new DxfLinetypeTableRecord { Name = linetype });
            }
        }
    }
}
