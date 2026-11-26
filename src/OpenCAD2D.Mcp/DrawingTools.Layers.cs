using System;
using System.ComponentModel;
using System.Linq;
using ModelContextProtocol.Server;

namespace OpenCAD2D.Mcp
{
    /// <summary>The tools that set the current layer, change a layer's look, rename and delete layers.</summary>
    internal sealed partial class DrawingTools
    {
        private static LayerInfo Info(LayerRow l) =>
            new LayerInfo
            {
                Name = l.Name,
                Color = l.Color,
                Linetype = l.Linetype,
                LineWeight = l.LineWeight,
                On = l.IsOn,
                Frozen = l.IsFrozen,
                Locked = l.IsLocked,
                Plottable = l.IsPlottable,
                Description = l.Description,
            };

        private static LayerRow FoundLayer(IDrawingBackend drawing, string name)
        {
            string wanted = name?.Trim();

            if (string.IsNullOrEmpty(wanted))
            {
                throw new ArgumentException("Give the name of a layer.");
            }

            return drawing
                .Layers()
                .FirstOrDefault(l => string.Equals(l.Name, wanted, StringComparison.OrdinalIgnoreCase))
                ?? throw new ArgumentException(
                    "The drawing has no layer " + wanted + ": list_layers shows the ones it has."
                );
        }

        [McpServerTool(
            Name = "set_current_layer",
            ReadOnly = false,
            Destructive = false,
            Idempotent = true,
            OpenWorld = false
        )]
        [Description(
            "Makes a layer the current one, where new objects go when no layer is given. Refused for a frozen layer or a layer of an external reference. One undo step."
        )]
        public LayerInfo SetCurrentLayerTool([Description("The layer's name.")] string name) =>
            _session.Run("set_current_layer", true, drawing =>
            {
                LayerRow row = FoundLayer(drawing, name);
                Editing(drawing).SetCurrentLayer(row.Name);
                return Info(FoundLayer(drawing, row.Name));
            });

        [McpServerTool(Name = "set_layer", ReadOnly = false, Destructive = false, Idempotent = true, OpenWorld = false)]
        [Description(
            "Changes the look of a layer, as the Layer manager does; a property left out stays as it is. Refused for the current layer frozen, a linetype the drawing has not (load_linetype first), a colour or lineweight AutoCAD has not. One undo step."
        )]
        public LayerInfo SetLayer(
            [Description("The layer's name.")] string name,
            [Description("False to turn the layer off.")] bool? on = null,
            [Description("True to freeze the layer.")] bool? frozen = null,
            [Description("True to lock the layer.")] bool? locked = null,
            [Description("AutoCAD colour index 1 to 255.")] int? color = null,
            [Description("A linetype of the drawing.")] string linetype = null,
            [Description(
                "Lineweight in hundredths of a millimetre, one of AutoCAD's: 0, 5, 9, 13, 15, 18, 20, 25, 30, 35, 40, 50, 53, 60, 70, 80, 90, 100, 106, 120, 140, 158, 200, 211; or -3 for the default weight."
            )]
                int? lineweight = null,
            [Description("False to keep the layer off the plot.")] bool? plot = null
        ) => _session.Run("set_layer", true, drawing =>
            {
                LayerRow row = FoundLayer(drawing, name);

                if (
                    !on.HasValue
                    && !frozen.HasValue
                    && !locked.HasValue
                    && !color.HasValue
                    && string.IsNullOrWhiteSpace(linetype)
                    && !lineweight.HasValue
                    && !plot.HasValue
                )
                {
                    throw new ArgumentException(
                        "Give a property to set: on, frozen, locked, color, linetype, lineweight or plot."
                    );
                }

                if (color.HasValue)
                {
                    if (color.Value < 1 || color.Value > 255)
                    {
                        throw new ArgumentException("A layer's colour index is 1 to 255.");
                    }

                    row.Color = color.Value;
                }

                if (!string.IsNullOrWhiteSpace(linetype))
                {
                    string type = linetype.Trim();
                    row.Linetype = drawing
                        .Linetypes()
                        .FirstOrDefault(l => string.Equals(l, type, StringComparison.OrdinalIgnoreCase))
                        ?? throw new ArgumentException(
                            "The drawing has no linetype " + type + ": load_linetype brings one in."
                        );
                }

                if (lineweight.HasValue)
                {
                    if (lineweight.Value != -3 && !LineWeights.Contains(lineweight.Value))
                    {
                        throw new ArgumentException(
                            "The lineweight "
                                + lineweight.Value
                                + " is none of AutoCAD's: "
                                + string.Join(", ", LineWeights)
                                + ", or -3."
                        );
                    }

                    row.LineWeight = lineweight.Value;
                }

                row.IsOn = on ?? row.IsOn;
                row.IsFrozen = frozen ?? row.IsFrozen;
                row.IsLocked = locked ?? row.IsLocked;
                row.IsPlottable = plot ?? row.IsPlottable;
                Editing(drawing).ModifyLayer(row);
                return Info(FoundLayer(drawing, row.Name));
            });

        [McpServerTool(
            Name = "rename_layer",
            ReadOnly = false,
            Destructive = false,
            Idempotent = false,
            OpenWorld = false
        )]
        [Description(
            "Renames a layer; the objects on it follow. Refused for layer 0, Defpoints, a layer of an external reference, a name the drawing has or one with a character AutoCAD refuses. One undo step."
        )]
        public LayerInfo RenameLayer(
            [Description("The layer's name now.")] string name,
            [Description("The new name.")] string new_name
        ) => _session.Run("rename_layer", true, drawing =>
            {
                LayerRow row = FoundLayer(drawing, name);
                string next = RecordName(new_name, "layer");
                Editing(drawing).Make("rename_layer", new CadMakeArgs().Set("name", row.Name).Set("new_name", next));
                return Info(FoundLayer(drawing, next));
            });

        [McpServerTool(
            Name = "delete_layer",
            ReadOnly = false,
            Destructive = true,
            Idempotent = false,
            OpenWorld = false
        )]
        [Description(
            "Deletes a layer. Refused while objects are on it, unless move_to names another layer they are moved to first; refused for layer 0, Defpoints, the current layer, a layer of an external reference. One undo step."
        )]
        public ChangedResult DeleteLayer(
            [Description("The layer to delete.")] string name,
            [Description("A layer to move the objects on it to; leave out to refuse a layer in use.")]
                string move_to = null
        ) => _session.Run("delete_layer", true, drawing =>
            {
                LayerRow row = FoundLayer(drawing, name);
                string target = string.IsNullOrWhiteSpace(move_to) ? null : FoundLayer(drawing, move_to).Name;
                Editing(drawing).Make("delete_layer", new CadMakeArgs().Set("name", row.Name).Set("move_to", target));
                return new ChangedResult { Changed = 1, Handles = new[] { row.Name } };
            });
    }
}
