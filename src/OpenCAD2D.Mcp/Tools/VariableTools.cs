using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Globalization;
using System.Linq;
using System.Text.Json;
using ModelContextProtocol.Server;

namespace OpenCAD2D.Mcp
{
    internal sealed partial class DrawingTools
    {
        /// <summary>The most variables one <c>get_variables</c> call reads.</summary>
        private const int MostVariables = 100;

        [McpServerTool(
            Name = "get_variables",
            ReadOnly = true,
            Destructive = false,
            Idempotent = true,
            OpenWorld = false
        )]
        [Description(
            "Reads system variables of the drawing as AutoCAD's getvar does (units, scales, the current layer and styles, the drawing's extents and limits). Give names, such as [\"LTSCALE\", \"CLAYER\"], to read any variable the engine knows; leave them out to read the documented ones, which are also the ones set_variable takes (and a few it does not). An angle is in degrees, a point is [x, y] or [x, y, z]. A name the engine does not know comes back with an error and no value."
        )]
        public VariablesResult GetVariables(
            [Description("The names, either case; the documented variables where left out. At most 100.")]
                string[] names = null
        ) => _session.Run("get_variables", false, drawing =>
            {
                List<string> wanted =
                    names == null || names.Length == 0
                        ? SystemVariables.Documented.Select(e => e.Name).ToList()
                        : names
                            .Select(n => n?.Trim())
                            .Where(n => !string.IsNullOrEmpty(n))
                            .Distinct(StringComparer.OrdinalIgnoreCase)
                            .ToList();

                if (wanted.Count > MostVariables)
                {
                    throw new ArgumentException("Ask for at most " + MostVariables + " variables at a time.");
                }

                return new VariablesResult { Variables = wanted.Select(n => Read(drawing, n)).ToList() };
            });

        private static VariableValue Read(IDrawingBackend drawing, string name)
        {
            SystemVariables.Entry entry = SystemVariables.Find(name);
            var value = new VariableValue
            {
                Name = (entry?.Name ?? name).ToUpperInvariant(),
                Settable = entry != null && entry.Settable,
                Meaning = entry?.Meaning,
            };

            try
            {
                object raw = drawing.Variable(name);
                bool angle = entry != null && entry.Kind == SystemVariables.VariableKind.Angle;

                switch (raw)
                {
                    case int whole:
                        value.Kind = "integer";
                        value.Value = whole;
                        break;
                    case double real:
                        value.Kind = angle ? "angle" : "real";
                        value.Value = angle ? Math.Round(real * 180.0 / Math.PI, 6) : Math.Round(real, 6);
                        break;
                    case double[] point:
                        value.Kind = "point";
                        value.Value = point.Select(c => Math.Round(c, 4)).ToArray();
                        break;
                    default:
                        value.Kind = "text";
                        value.Value = Convert.ToString(raw, CultureInfo.InvariantCulture) ?? string.Empty;
                        break;
                }
            }
            catch (Exception exception) when (exception is ArgumentException || exception is Remote.CadRemoteException)
            {
                value.Error = FirstLine(exception.Message);
            }

            return value;
        }

        private static string FirstLine(string message)
        {
            int line = message.IndexOfAny(new[] { '\r', '\n' });
            return line < 0 ? message : message.Substring(0, line);
        }

        [McpServerTool(
            Name = "set_variable",
            ReadOnly = false,
            Destructive = false,
            Idempotent = true,
            OpenWorld = false
        )]
        [Description(
            "Sets a system variable of the drawing, as AutoCAD's setvar does, for the documented ones only (LTSCALE, CELTSCALE, TEXTSIZE, CLAYER, TEXTSTYLE, INSUNITS, LUNITS, LUPREC, AUNITS, AUPREC, ANGBASE, PDMODE, PDSIZE ...: get_variables lists them with what each means). The value is a number or a text as the variable takes it, an angle in degrees. One undo step. A value the variable refuses, or a variable that is not documented, is refused with the reason. Refused when the server is read-only."
        )]
        public VariableSetResult SetVariable(
            [Description("The name, either case.")] string name,
            [Description("The value: a number, or a text for a variable that holds one (CLAYER, TEXTSTYLE).")]
                JsonElement value
        ) => _session.Run("set_variable", true, drawing =>
            {
                SystemVariables.Entry entry = SystemVariables.Find(name);

                if (entry == null || !entry.Settable)
                {
                    throw new ArgumentException(
                        (string.IsNullOrWhiteSpace(name) ? "A variable" : name.Trim())
                            + " is not one set_variable sets. The variables it sets are "
                            + SystemVariables.SettableNames()
                            + "."
                    );
                }

                Q(drawing).SetVariable(entry.Name, Converted(entry, value));
                return new VariableSetResult { Name = entry.Name, Value = Read(drawing, entry.Name).Value };
            });

        private static object Converted(SystemVariables.Entry entry, JsonElement value)
        {
            switch (entry.Kind)
            {
                case SystemVariables.VariableKind.Integer:
                    if (value.ValueKind == JsonValueKind.Number && value.TryGetInt32(out int whole))
                    {
                        return whole;
                    }

                    throw new ArgumentException(entry.Name + " takes a whole number.");
                case SystemVariables.VariableKind.Real:
                case SystemVariables.VariableKind.Angle:
                    if (
                        value.ValueKind == JsonValueKind.Number
                        && value.TryGetDouble(out double real)
                        && !double.IsNaN(real)
                        && !double.IsInfinity(real)
                    )
                    {
                        return entry.Kind == SystemVariables.VariableKind.Angle ? real * Math.PI / 180.0 : real;
                    }

                    throw new ArgumentException(
                        entry.Name
                            + " takes a number"
                            + (entry.Kind == SystemVariables.VariableKind.Angle ? " of degrees." : ".")
                    );
                default:
                    if (value.ValueKind == JsonValueKind.String)
                    {
                        return value.GetString();
                    }

                    if (value.ValueKind == JsonValueKind.Number)
                    {
                        return value.GetRawText();
                    }

                    throw new ArgumentException(entry.Name + " takes a text.");
            }
        }
    }
}
