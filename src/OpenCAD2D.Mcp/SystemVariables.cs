using System;
using System.Collections.Generic;
using System.Linq;

namespace OpenCAD2D.Mcp
{
    /// <summary>
    /// The system variables an agent is told about and may set: the ones that a drawing keeps and that drafting needs (units,
    /// text and linetype scales, the current layer and styles, point look). <c>get_variables</c> reads any variable the engine
    /// knows; <c>set_variable</c> only these, so that a variable of the app itself (OSMODE, FILEDIA, the paths) is never changed
    /// from outside.
    /// </summary>
    internal static class SystemVariables
    {
        /// <summary>What a variable holds.</summary>
        public enum VariableKind
        {
            Integer,
            Real,
            Angle,
            Text,
            Point,
        }

        /// <summary>A documented variable.</summary>
        public sealed class Entry
        {
            public Entry(string name, VariableKind kind, bool settable, string meaning)
            {
                Name = name;
                Kind = kind;
                Settable = settable;
                Meaning = meaning;
            }

            public string Name { get; }

            public VariableKind Kind { get; }

            public bool Settable { get; }

            public string Meaning { get; }
        }

        /// <summary>The documented variables, in alphabetical order.</summary>
        public static readonly IReadOnlyList<Entry> Documented = new[]
        {
            new Entry("ANGBASE", VariableKind.Angle, true, "the direction of angle 0, in degrees"),
            new Entry("ANGDIR", VariableKind.Integer, true, "0 angles count counter-clockwise, 1 clockwise"),
            new Entry("ATTMODE", VariableKind.Integer, true, "0 attributes hidden, 1 normal, 2 all shown"),
            new Entry(
                "AUNITS",
                VariableKind.Integer,
                true,
                "angle units: 0 decimal degrees, 1 deg/min/sec, 2 grads, 3 radians, 4 surveyor"
            ),
            new Entry("AUPREC", VariableKind.Integer, true, "decimal places of an angle, 0 to 8"),
            new Entry("CANNOSCALE", VariableKind.Text, true, "the current annotation scale, such as 1:10"),
            new Entry("CECOLOR", VariableKind.Text, true, "the colour new objects take: BYLAYER, BYBLOCK or an index"),
            new Entry("CELTSCALE", VariableKind.Real, true, "the linetype scale new objects take"),
            new Entry("CELTYPE", VariableKind.Text, true, "the linetype new objects take"),
            new Entry(
                "CELWEIGHT",
                VariableKind.Integer,
                true,
                "the lineweight new objects take, in hundredths of a millimetre; -1 BYLAYER, -2 BYBLOCK, -3 default"
            ),
            new Entry("CLAYER", VariableKind.Text, true, "the current layer"),
            new Entry("CTAB", VariableKind.Text, false, "the current layout tab: Model or a layout's name"),
            new Entry("DIMSCALE", VariableKind.Real, true, "the overall scale of the current dimension style"),
            new Entry("DIMSTYLE", VariableKind.Text, true, "the current dimension style"),
            new Entry("DWGNAME", VariableKind.Text, false, "the file name of the drawing"),
            new Entry("DWGPREFIX", VariableKind.Text, false, "the folder of the drawing"),
            new Entry("EXTMAX", VariableKind.Point, false, "the upper right corner of what is drawn"),
            new Entry("EXTMIN", VariableKind.Point, false, "the lower left corner of what is drawn"),
            new Entry("FILLMODE", VariableKind.Integer, true, "1 solid fills are shown, 0 only their outline"),
            new Entry("HPNAME", VariableKind.Text, true, "the hatch pattern new hatches take"),
            new Entry("HPSCALE", VariableKind.Real, true, "the scale new hatches take"),
            new Entry(
                "INSUNITS",
                VariableKind.Integer,
                true,
                "the drawing units: 0 unitless, 1 inches, 2 feet, 4 millimetres, 5 centimetres, 6 metres"
            ),
            new Entry("LIMMAX", VariableKind.Point, false, "the upper right limit of the drawing"),
            new Entry("LIMMIN", VariableKind.Point, false, "the lower left limit of the drawing"),
            new Entry("LTSCALE", VariableKind.Real, true, "the global linetype scale"),
            new Entry(
                "LUNITS",
                VariableKind.Integer,
                true,
                "length units: 1 scientific, 2 decimal, 3 engineering, 4 architectural, 5 fractional"
            ),
            new Entry("LUPREC", VariableKind.Integer, true, "decimal places of a length, 0 to 8"),
            new Entry("MEASUREMENT", VariableKind.Integer, true, "0 imperial, 1 metric hatch patterns and linetypes"),
            new Entry(
                "MIRRTEXT",
                VariableKind.Integer,
                true,
                "1 texts are mirrored with their objects, 0 they stay readable"
            ),
            new Entry(
                "MSLTSCALE",
                VariableKind.Integer,
                true,
                "1 linetypes follow the annotation scale of model space"
            ),
            new Entry(
                "PDMODE",
                VariableKind.Integer,
                true,
                "how a point is drawn, 0 a dot, 3 an X, 33 a circle about a dot"
            ),
            new Entry("PDSIZE", VariableKind.Real, true, "the size of a point as drawn"),
            new Entry("PLINEWID", VariableKind.Real, true, "the width a new polyline takes"),
            new Entry(
                "PSLTSCALE",
                VariableKind.Integer,
                true,
                "1 linetypes in paper space are scaled by the viewport, 0 by the layout"
            ),
            new Entry("TEXTSIZE", VariableKind.Real, true, "the height new texts take"),
            new Entry("TEXTSTYLE", VariableKind.Text, true, "the text style new texts take"),
            new Entry("TILEMODE", VariableKind.Integer, false, "1 model space is shown, 0 a layout"),
            new Entry("UNITMODE", VariableKind.Integer, true, "0 fractions and feet are shown spaced, 1 as entered"),
        };

        /// <summary>Finds a documented variable by name, either case; null where it is not one.</summary>
        public static Entry Find(string name) =>
            name == null
                ? null
                : Documented.FirstOrDefault(e =>
                    string.Equals(e.Name, name.Trim(), StringComparison.OrdinalIgnoreCase)
                );

        /// <summary>The names the agent may set, for a message.</summary>
        public static string SettableNames() =>
            string.Join(", ", Documented.Where(e => e.Settable).Select(e => e.Name));
    }
}
