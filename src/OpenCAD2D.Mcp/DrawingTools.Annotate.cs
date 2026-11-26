using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Globalization;
using System.Linq;
using GeometryHelper.Dxf;
using ModelContextProtocol.Server;

namespace OpenCAD2D.Mcp
{
    /// <summary>The tools that edit a text, a dimension, and the look of objects matched to another's.</summary>
    internal sealed partial class DrawingTools
    {
        private static ChangedResult Changed(string handle) =>
            new ChangedResult { Changed = 1, Handles = new[] { handle.Trim() } };

        [McpServerTool(Name = "edit_text", ReadOnly = false, Destructive = false, Idempotent = true, OpenWorld = false)]
        [Description(
            "Changes a text or an MTEXT: its words, letter height, rotation in degrees, text style and justification (TopLeft to BottomRight, as MiddleCenter). A property left out stays as it is. Refused on a locked layer. One undo step."
        )]
        public ChangedResult EditText(
            [Description("The handle of the TEXT or MTEXT.")] string handle,
            [Description("The new words (an MTEXT: \\P starts a new line).")] string contents = null,
            [Description("The letter height in millimetres, above 0.")] double? height = null,
            [Description("The rotation in degrees, counter-clockwise.")] double? rotation = null,
            [Description("A text style of the drawing (list_text_styles).")] string style = null,
            [Description(
                "TopLeft, TopCenter, TopRight, MiddleLeft, MiddleCenter, MiddleRight, BottomLeft, BottomCenter or BottomRight."
            )]
                string justify = null
        ) => _session.Run("edit_text", true, drawing =>
            {
                Editing(drawing)
                    .Make(
                        "edit_text",
                        new CadMakeArgs()
                            .Set("handle", handle)
                            .Set("contents", contents)
                            .Set("height", height.HasValue ? (object)height.Value : null)
                            .Set("rotation", rotation.HasValue ? (object)rotation.Value : null)
                            .Set("style", string.IsNullOrWhiteSpace(style) ? null : style.Trim())
                            .Set("justify", string.IsNullOrWhiteSpace(justify) ? null : justify.Trim())
                    );
                return Changed(handle);
            });

        [McpServerTool(
            Name = "edit_dimension",
            ReadOnly = false,
            Destructive = false,
            Idempotent = true,
            OpenWorld = false
        )]
        [Description(
            "Changes a dimension, as the Properties palette does: its text (an override such as \"<> TYP\", where <> stands for the measurement; an empty text gives the measurement back), its dimension style, and overrides of its variables by the palette's row name (Text height, Arrow size, Text color, Dim line color, Ext line offset ...). The dimension is drawn again. Refused on a locked layer. One undo step."
        )]
        public ChangedResult EditDimension(
            [Description("The handle of the dimension.")] string handle,
            [Description("The text; \"<>\" is the measurement; an empty text restores it.")] string text = null,
            [Description("A dimension style of the drawing (list_dimension_styles).")] string style = null,
            [Description(
                "Overrides as {\"Text height\": \"3.5\", \"Arrow size\": \"2\"}, the palette's row name and the value as typed there."
            )]
                Dictionary<string, string> overrides = null
        ) => _session.Run("edit_dimension", true, drawing =>
            {
                var rows = new List<string[]>();

                if (style != null)
                {
                    rows.Add(new[] { "Dim style", style.Trim() });
                }

                foreach (
                    KeyValuePair<string, string> item in (overrides ?? new Dictionary<string, string>())
                        .OrderBy(o => o.Key, StringComparer.OrdinalIgnoreCase)
                        .ThenBy(o => o.Key, StringComparer.Ordinal)
                )
                {
                    rows.Add(new[] { item.Key.Trim(), item.Value ?? string.Empty });
                }

                if (text != null)
                {
                    rows.Add(new[] { "Text override", text });
                }

                if (rows.Count == 0)
                {
                    throw new ArgumentException("Give text, style or overrides to set.");
                }

                Editing(drawing)
                    .Make(
                        "set_rows",
                        new CadMakeArgs().Set("handles", new[] { handle?.Trim() }).Set("rows", rows.ToArray())
                    );
                return Changed(handle);
            });

        private static readonly string[] MatchKinds = { "layer", "color", "linetype", "linetype_scale", "lineweight" };

        [McpServerTool(
            Name = "match_properties",
            ReadOnly = false,
            Destructive = false,
            Idempotent = true,
            OpenWorld = false
        )]
        [Description(
            "Gives objects the look of another, as MATCHPROP: the properties chosen in which, of layer, color, linetype, linetype_scale and lineweight (all five where left out), read from the source and set on each target. Refused for a target on a locked layer. One undo step."
        )]
        public ChangedResult MatchProperties(
            [Description("The handle of the object to copy the look from.")] string source,
            [Description(TargetsText)] string[] targets,
            [Description("Which of layer, color, linetype, linetype_scale, lineweight to copy.")] string[] which = null
        ) => _session.Run("match_properties", true, drawing =>
            {
                List<string> list = Handles(targets);
                string from = source?.Trim();
                List<string> kinds =
                    which == null || which.Length == 0
                        ? MatchKinds.ToList()
                        : which.Select(w => w?.Trim().ToLowerInvariant()).Distinct().ToList();

                foreach (string kind in kinds)
                {
                    if (!MatchKinds.Contains(kind))
                    {
                        throw new ArgumentException(
                            "which holds layer, color, linetype, linetype_scale and lineweight, not " + kind + "."
                        );
                    }
                }

                DxfEntity entity = Editing(drawing).Objects(new[] { from }).FirstOrDefault()?.Entity
                    ?? throw new ArgumentException("The drawing has no object " + source + ".");
                var rows = new List<string[]>();

                foreach (string kind in kinds)
                {
                    switch (kind)
                    {
                        case "layer":
                            rows.Add(new[] { "Layer", entity.Layer });
                            break;
                        case "color":
                            rows.Add(new[] { "Color", ColorRow(entity.ColorIndex) });
                            break;
                        case "linetype":
                            rows.Add(new[] { "Linetype", LinetypeRow(entity.Linetype) });
                            break;
                        case "linetype_scale":
                            rows.Add(
                                new[]
                                {
                                    "Linetype scale",
                                    entity.LinetypeScale.ToString("R", CultureInfo.InvariantCulture),
                                }
                            );
                            break;
                        default:
                            rows.Add(new[] { "Lineweight", LineWeightRow((int)entity.LineWeight) });
                            break;
                    }
                }

                list.RemoveAll(h => string.Equals(h, from, StringComparison.OrdinalIgnoreCase));

                if (list.Count == 0)
                {
                    throw new ArgumentException("Give at least one target besides the source.");
                }

                Editing(drawing)
                    .Make("set_rows", new CadMakeArgs().Set("handles", list.ToArray()).Set("rows", rows.ToArray()));
                return new ChangedResult { Changed = list.Count, Handles = list };
            });

        /// <summary>
        /// Sets the properties of the extended <c>set_properties</c> through the rows of the Properties palette, all in one undo step:
        /// the layer, colour and linetype as the first call of the tool takes them, with the linetype scale, lineweight and transparency.
        /// </summary>
        private static void SetPropertyRows(
            IDrawingBackend drawing,
            List<string> handles,
            string layer,
            int? color,
            string linetype,
            double? linetypeScale,
            int? lineweight,
            int? transparency
        )
        {
            var rows = new List<string[]>();

            if (!string.IsNullOrEmpty(layer))
            {
                rows.Add(new[] { "Layer", layer.Trim() });
            }

            if (color.HasValue)
            {
                rows.Add(new[] { "Color", ColorRow(color.Value) });
            }

            if (!string.IsNullOrEmpty(linetype))
            {
                rows.Add(new[] { "Linetype", LinetypeRow(linetype.Trim()) });
            }

            if (linetypeScale.HasValue)
            {
                if (!(linetypeScale.Value > 0.0) || double.IsInfinity(linetypeScale.Value))
                {
                    throw new ArgumentException("The linetype scale is above nought.");
                }

                rows.Add(new[] { "Linetype scale", linetypeScale.Value.ToString("R", CultureInfo.InvariantCulture) });
            }

            if (lineweight.HasValue)
            {
                rows.Add(new[] { "Lineweight", LineWeightRow(lineweight.Value) });
            }

            if (transparency.HasValue && (transparency.Value < -1 || transparency.Value > 90))
            {
                throw new ArgumentException("The transparency is 0 to 90 per cent, or -1 for BYLAYER.");
            }

            if (
                !string.IsNullOrEmpty(layer)
                && !drawing.Layers().Any(l => string.Equals(l.Name, layer, StringComparison.OrdinalIgnoreCase))
            )
            {
                throw new ArgumentException(
                    "The drawing has no layer " + layer + ": list_layers shows the ones it has."
                );
            }

            IDrawingEditing editing = Editing(drawing);
            editing.Group(() =>
            {
                if (rows.Count > 0)
                {
                    editing.Make(
                        "set_rows",
                        new CadMakeArgs().Set("handles", handles.ToArray()).Set("rows", rows.ToArray())
                    );
                }

                if (transparency.HasValue)
                {
                    editing.Make(
                        "set_transparency",
                        new CadMakeArgs().Set("handles", handles.ToArray()).Set("percent", (double)transparency.Value)
                    );
                }
            });
        }

        private static string LinetypeRow(string linetype) =>
            string.IsNullOrEmpty(linetype) || string.Equals(linetype, "BYLAYER", StringComparison.OrdinalIgnoreCase)
                ? "ByLayer"
            : string.Equals(linetype, "BYBLOCK", StringComparison.OrdinalIgnoreCase) ? "ByBlock"
            : linetype;

        /// <summary>A lineweight in hundredths of a millimetre as the Properties palette types it: <c>0.25 mm</c>, ByLayer, ByBlock or Default.</summary>
        private static string LineWeightRow(int weight)
        {
            if (weight == -1)
            {
                return "ByLayer";
            }

            if (weight == -2)
            {
                return "ByBlock";
            }

            if (weight == -3)
            {
                return "Default";
            }

            if (weight < 0 || !LineWeights.Contains(weight))
            {
                throw new ArgumentException(
                    "The lineweight "
                        + weight
                        + " is none of AutoCAD's: "
                        + string.Join(", ", LineWeights)
                        + ", or -1 BYLAYER, -2 BYBLOCK, -3 default."
                );
            }

            return (weight / 100.0).ToString("0.00", CultureInfo.InvariantCulture) + " mm";
        }
    }
}
