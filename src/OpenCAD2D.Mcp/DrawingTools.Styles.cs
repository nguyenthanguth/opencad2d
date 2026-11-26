using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Globalization;
using System.Linq;
using System.Reflection;
using GeometryHelper.Dxf;
using ModelContextProtocol.Server;

namespace OpenCAD2D.Mcp
{
    /// <summary>The tools that add table records: text styles, dimension styles and linetypes.</summary>
    internal sealed partial class DrawingTools
    {
        private static readonly string[] NoHandles = Array.Empty<string>();
        private static readonly DxfEntity[] NoEntities = Array.Empty<DxfEntity>();

        /// <summary>Makes the name of a record: one line, no character AutoCAD refuses in a name.</summary>
        private static string RecordName(string name, string what)
        {
            string trimmed = name?.Trim();

            if (
                string.IsNullOrEmpty(trimmed)
                || trimmed.Length > 255
                || trimmed.IndexOfAny(new[] { '<', '>', '/', '\\', '"', ':', ';', '?', '*', '|', '=', '`', ',' }) >= 0
                || trimmed.Any(char.IsControl)
            )
            {
                throw new ArgumentException(
                    "A "
                        + what
                        + " needs a name of up to 255 characters without any of < > / \\ \" : ; ? * | = ` , in it."
                );
            }

            return trimmed;
        }

        [McpServerTool(
            Name = "add_text_style",
            ReadOnly = false,
            Destructive = false,
            Idempotent = false,
            OpenWorld = false
        )]
        [Description(
            "Adds a text style, as STYLE: a name for a font, a width factor and a fixed height. add_text and add_mtext take the name. Refused for a name the drawing has (either case). One undo step."
        )]
        public RecordResult AddTextStyle(
            [Description("The style's name.")] string name,
            [Description(
                "The font file: a shape font as romans.shx or a TrueType font as arial.ttf; txt (the font of Standard) where left out."
            )]
                string font = "txt",
            [Description("How wide the letters are for their height, above 0; 1 where left out.")]
                double width_factor = 1.0,
            [Description("The fixed height of texts in the style, 0 for each text to give its own.")]
                double height = 0.0
        ) => _session.Run("add_text_style", true, drawing =>
            {
                string label = RecordName(name, "text style");

                if (string.IsNullOrWhiteSpace(font))
                {
                    throw new ArgumentException("The font of a text style is a font file such as romans.shx.");
                }

                if (
                    !(width_factor > 0.0)
                    || double.IsInfinity(width_factor)
                    || !(height >= 0.0)
                    || double.IsInfinity(height)
                )
                {
                    throw new ArgumentException("The width factor is above 0 and the height is 0 or more.");
                }

                if (drawing.TextStyles().Any(s => string.Equals(s.Name, label, StringComparison.OrdinalIgnoreCase)))
                {
                    throw new ArgumentException("Text style \"" + label + "\" already exists.");
                }

                var records = new DxfDatabase("AC1027");
                records.TextStyleTable.Add(
                    new DxfTextStyleTableRecord
                    {
                        Name = label,
                        FileName = font.Trim(),
                        XScale = width_factor,
                        TextSize = height,
                    }
                );
                Editing(drawing).Apply(records, NoEntities, null, NoHandles);
                return new RecordResult { Name = label };
            });

        [McpServerTool(
            Name = "add_dimension_style",
            ReadOnly = false,
            Destructive = false,
            Idempotent = false,
            OpenWorld = false
        )]
        [Description(
            "Adds a dimension style, as the Dimension Style Manager's New does from ISO-25: a text style, an overall scale, the height of the words and the size of the arrowheads, and any other dimension variable by the name the library gives it (Dimdec 2, Dimtad 1, Dimexe 1.5; a number, 0 or 1 for a switch). Refused for a name the drawing has. One undo step."
        )]
        public RecordResult AddDimensionStyle(
            [Description("The style's name.")] string name,
            [Description("A text style of the drawing; ISO-25's where left out.")] string text_style = null,
            [Description(
                "The overall scale (DIMSCALE), above 0; 1 where left out. A drawing plotted at 1:50 wants 50."
            )]
                double scale = 1.0,
            [Description("The height of the words (DIMTXT) in millimetres, above 0.")] double text_height = 2.5,
            [Description("The size of the arrowheads (DIMASZ) in millimetres, above 0.")] double arrow_size = 2.5,
            [Description("Other variables as {\"Dimdec\": 2, \"Dimtad\": 1}.")]
                Dictionary<string, double> variables = null
        ) => _session.Run("add_dimension_style", true, drawing =>
            {
                string label = RecordName(name, "dimension style");

                if (
                    !(scale > 0.0)
                    || !(text_height > 0.0)
                    || !(arrow_size > 0.0)
                    || double.IsInfinity(scale)
                    || double.IsInfinity(text_height)
                    || double.IsInfinity(arrow_size)
                )
                {
                    throw new ArgumentException("The scale, the text height and the arrow size are above 0.");
                }

                if (
                    drawing.DimensionStyles().Any(s => string.Equals(s.Name, label, StringComparison.OrdinalIgnoreCase))
                )
                {
                    throw new ArgumentException("Dimension style \"" + label + "\" already exists.");
                }

                string textStyle = string.IsNullOrWhiteSpace(text_style) ? null : text_style.Trim();

                if (
                    textStyle != null
                    && !drawing
                        .TextStyles()
                        .Any(s => string.Equals(s.Name, textStyle, StringComparison.OrdinalIgnoreCase))
                )
                {
                    throw new ArgumentException(
                        "The drawing has no text style " + textStyle + ": list_text_styles shows the ones it has."
                    );
                }

                var records = new DxfDatabase("AC1027");

                if (textStyle != null)
                {
                    records.TextStyleTable.Add(new DxfTextStyleTableRecord { Name = textStyle });
                }

                SortedDictionary<string, object> values = CadDimStyleEditor.Read(records.DimStyleTable["ISO-25"]);
                var style = new DxfDimStyleTableRecord { Name = label };
                records.DimStyleTable.Add(style);

                if (textStyle != null)
                {
                    values["Dimtxsty"] = textStyle;
                }

                values["Dimscale"] = scale;
                values["Dimtxt"] = text_height;
                values["Dimasz"] = arrow_size;
                var known = new HashSet<string>(CadDimStyleEditor.VariableNames, StringComparer.Ordinal);

                foreach (
                    KeyValuePair<string, double> variable in (variables ?? new Dictionary<string, double>()).OrderBy(
                        v => v.Key,
                        StringComparer.Ordinal
                    )
                )
                {
                    if (!known.Contains(variable.Key))
                    {
                        throw new ArgumentException(
                            variable.Key + " is no dimension variable: give a name such as Dimdec, Dimtad or Dimexe."
                        );
                    }

                    values[variable.Key] = Boxed(typeof(DxfDimStyleTableRecord), variable.Key, variable.Value);
                }

                CadDimStyleEditor.Write(records, style, values);
                Editing(drawing).Apply(records, NoEntities, null, NoHandles);
                return new RecordResult { Name = label };
            });

        /// <summary>A number as the object the property of a style takes: a double, an integer, a switch or an enum value.</summary>
        private static object Boxed(Type type, string name, double value)
        {
            Type kind = type.GetProperty(name, BindingFlags.Public | BindingFlags.Instance)?.PropertyType
                ?? throw new ArgumentException(name + " is no dimension variable.");

            if (kind.IsEnum)
            {
                return Enum.ToObject(kind, (int)Math.Round(value));
            }

            if (kind == typeof(bool))
            {
                return value != 0.0;
            }

            if (kind == typeof(double) || kind == typeof(float))
            {
                return value;
            }

            if (kind == typeof(int) || kind == typeof(short) || kind == typeof(byte))
            {
                return Convert.ChangeType(Math.Round(value), kind, CultureInfo.InvariantCulture);
            }

            throw new ArgumentException(
                "The dimension variable " + name + " is not a number: give it through its own tool."
            );
        }

        [McpServerTool(
            Name = "load_linetype",
            ReadOnly = false,
            Destructive = false,
            Idempotent = true,
            OpenWorld = false
        )]
        [Description(
            "Loads a linetype of the built-in set into the drawing, as LINETYPE Load: BORDER, CENTER, DASHDOT, DASHED, DIVIDE, DOT, HIDDEN and PHANTOM each with its 2 and X2, GAS_LINE and the ISO ones; the lengths follow the drawing's units. A linetype the drawing has is left as it is. add_layer and set_properties then take it. One undo step."
        )]
        public RecordResult LoadLinetype([Description("The linetype's name, either case.")] string name) =>
            _session.Run("load_linetype", true, drawing =>
            {
                string wanted = name?.Trim();

                if (string.IsNullOrEmpty(wanted))
                {
                    throw new ArgumentException("A linetype needs a name.");
                }

                CadLinetypeDefinition found = CadLinetypeFile.BuiltIn.FirstOrDefault(d =>
                    string.Equals(d.Name, wanted, StringComparison.OrdinalIgnoreCase)
                );

                if (found == null)
                {
                    throw new ArgumentException(
                        "The built-in linetypes are "
                            + string.Join(
                                ", ",
                                CadLinetypeFile
                                    .BuiltIn.Select(d => d.Name)
                                    .OrderBy(n => n, StringComparer.OrdinalIgnoreCase)
                                    .ThenBy(n => n, StringComparer.Ordinal)
                            )
                            + "."
                    );
                }

                string known = drawing
                    .Linetypes()
                    .FirstOrDefault(l => string.Equals(l, found.Name, StringComparison.OrdinalIgnoreCase));

                if (known != null)
                {
                    return new RecordResult { Name = known };
                }

                var records = new DxfDatabase("AC1027")
                {
                    Measurement = (DxfMeasurementValue)
                        Convert.ToInt32(drawing.Variable("MEASUREMENT"), CultureInfo.InvariantCulture),
                };
                CadLinetypes.Add(records, found);
                Editing(drawing).Apply(records, NoEntities, null, NoHandles);
                return new RecordResult { Name = found.Name };
            });
    }
}
