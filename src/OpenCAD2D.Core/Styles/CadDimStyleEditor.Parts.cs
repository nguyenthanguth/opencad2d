using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using GeometryHelper.Dxf;
using GeometryHelper.Geometry;

namespace OpenCAD2D
{
    public static partial class CadDimStyleEditor
    {
        private const CadStylePart DL1 = CadStylePart.DimensionLine1;
        private const CadStylePart DL2 = CadStylePart.DimensionLine2;
        private const CadStylePart EL1 = CadStylePart.ExtensionLine1;
        private const CadStylePart EL2 = CadStylePart.ExtensionLine2;
        private const CadStylePart A1 = CadStylePart.Arrow1;
        private const CadStylePart A2 = CadStylePart.Arrow2;
        private const CadStylePart TXT = CadStylePart.Text;

        /// <summary>
        /// Every field of the four dimension style dialogs, in the order of their tabs and rows, with the parts of the sample it
        /// sets (a part of every dimension of the tab's sample that has it, but where <c>Dimensions</c> names those it is of).
        /// A field of two variables is keyed by its first; the second field of one variable (the size of the centre mark, the
        /// frame round the text) by the variable and a <c>#</c>.
        /// </summary>
        private static readonly PartRow[] PartRows =
        {
            // Lines
            new PartRow("DIMCLRD", DL1, DL2, A1, A2, CadStylePart.TextFrame),
            new PartRow("DIMLTYPE", DL1, DL2),
            new PartRow("DIMLWD", DL1, DL2, A1, A2),
            new PartRow("DIMDLE", DL1, DL2),
            new PartRow("DIMDLI", DL1, DL2).Of(1),
            new PartRow("DIMSD1", DL1),
            new PartRow("DIMSD2", DL2),
            new PartRow("DIMCLRE", EL1, EL2),
            new PartRow("DIMLTEX1", EL1),
            new PartRow("DIMLTEX2", EL2),
            new PartRow("DIMLWE", EL1, EL2),
            new PartRow("DIMSE1", EL1),
            new PartRow("DIMSE2", EL2),
            new PartRow("DIMEXE", EL1, EL2),
            new PartRow("DIMEXO", EL1, EL2),
            new PartRow("DIMFXLON", EL1, EL2),
            new PartRow("DIMFXL", EL1, EL2),
            // Symbols and arrows
            new PartRow("DIMBLK1", A1),
            new PartRow("DIMBLK2", A2),
            new PartRow("DIMLDRBLK", CadStylePart.LeaderArrow, CadStylePart.LeaderLine),
            new PartRow("DIMASZ", A1, A2, CadStylePart.LeaderArrow),
            new PartRow("DIMCEN", CadStylePart.CentreMark),
            new PartRow("DIMCEN#", CadStylePart.CentreMark),
            new PartRow("DIMARCSYM", CadStylePart.ArcSymbol),
            new PartRow("DIMJOGANG", CadStylePart.Jog),
            // Text
            new PartRow("DIMTXSTY", TXT),
            new PartRow("DIMCLRT", TXT),
            new PartRow("DIMTFILL", CadStylePart.TextFill),
            new PartRow("DIMTXT", TXT),
            new PartRow("DIMTFAC", TXT),
            new PartRow("DIMGAP#", CadStylePart.TextFrame),
            new PartRow("DIMTAD", TXT),
            new PartRow("DIMJUST", TXT),
            new PartRow("DIMTXTDIRECTION", TXT),
            new PartRow("DIMGAP", TXT, DL1, DL2),
            new PartRow("DIMTIH", TXT).Of(1),
            // Fit
            new PartRow("DIMATFIT", TXT, A1, A2).Of(1),
            new PartRow("DIMTMOVE", TXT, CadStylePart.TextLeader).Of(2),
            new PartRow("ANNOTATIVE"),
            new PartRow("DIMSCALE"),
            new PartRow("DIMUPT"),
            new PartRow("DIMTOFL", DL1, DL2).Of(1),
            // Primary units
            new PartRow("DIMLUNIT", TXT).Of(0),
            new PartRow("DIMDEC", TXT).Of(0),
            new PartRow("DIMFRAC", TXT).Of(0),
            new PartRow("DIMDSEP", TXT).Of(0),
            new PartRow("DIMRND", TXT).Of(0),
            new PartRow("DIMPOST", TXT).Of(0),
            new PartRow("DIMLFAC", TXT).Of(0),
            new PartRow("DIMZIN", TXT).Of(0),
            new PartRow("DIMAUNIT", TXT).Of(1),
            new PartRow("DIMADEC", TXT).Of(1),
            new PartRow("DIMAZIN", TXT).Of(1),
            // Alternate units
            new PartRow("DIMALT", TXT),
            new PartRow("DIMALTU", TXT),
            new PartRow("DIMALTD", TXT),
            new PartRow("DIMALTF", TXT),
            new PartRow("DIMALTRND", TXT),
            new PartRow("DIMAPOST", TXT),
            new PartRow("DIMALTZ", TXT),
            new PartRow("DIMPOST\\X", TXT),
            // Tolerances (Scaling for height is DIMTFAC again, of the Text tab's row)
            new PartRow("DIMTOL", TXT),
            new PartRow("DIMTDEC", TXT),
            new PartRow("DIMTP", TXT),
            new PartRow("DIMTM", TXT),
            new PartRow("DIMTOLJ", TXT),
            new PartRow("DIMTZIN", TXT),
        };

        private static readonly Dictionary<string, PartRow> PartRowsByKey = PartRows.ToDictionary(
            r => r.Key,
            StringComparer.Ordinal
        );

        /// <summary>
        /// Gets the parts of a sample a field of the dimension style dialogs sets, from the table of the fields: the
        /// extension and dimension lines, the arrowheads, the words. A field of no part (DIMSCALE, Annotative, DIMUPT) gives none.
        /// The key is the name of the variable the dialog shows beside the field, as <c>DIMEXE</c>, letters of either case and
        /// spaces left out the same; a field of two variables is keyed by the first (<c>DIMTOL</c>, <c>DIMTIH</c>), the second
        /// field of one variable by the variable and a <c>#</c> (<c>DIMCEN#</c> the centre mark's size, <c>DIMGAP#</c> the frame
        /// round the text), and the placement of the alternate units as <c>DIMPOST\X</c>.
        /// </summary>
        /// <param name="key">The key of the field.</param>
        /// <returns>The parts; none for a field of no part and for a key no field has.</returns>
        public static IReadOnlyList<CadStylePart> PartsOf(string key) =>
            key != null && PartRowsByKey.TryGetValue(PartKey(key), out PartRow row)
                ? row.Parts
                : Array.Empty<CadStylePart>();

        /// <summary>
        /// Gets the keys of the fields that set a part, in the order of the dialog: the tabs from Lines to Tolerances, the rows of
        /// each in order.
        /// </summary>
        /// <param name="part">The part.</param>
        /// <returns>The keys, each as <see cref="PartsOf"/> takes it.</returns>
        public static IReadOnlyList<string> KeysOf(CadStylePart part) =>
            PartRows.Where(r => Array.IndexOf(r.Parts, part) >= 0).Select(r => r.Key).ToList();

        /// <summary>
        /// Gets the parts of a sample a field lights: those <see cref="PartsOf"/> names, of every dimension of the sample that has
        /// them, but for the fields that set one dimension of a tab's sample alone (DIMDLI the second baseline dimension, the
        /// units of the linear and of the angle, DIMATFIT and DIMTOFL the narrow dimension, DIMTMOVE the one whose words were moved).
        /// </summary>
        internal static IReadOnlyList<CadStyleSamplePart> LitParts(CadStyleSample sample, string key)
        {
            if (sample == null)
            {
                throw new ArgumentNullException(nameof(sample));
            }

            if (key == null || !PartRowsByKey.TryGetValue(PartKey(key), out PartRow row))
            {
                return Array.Empty<CadStyleSamplePart>();
            }

            return sample
                .Parts.Where(p =>
                    Array.IndexOf(row.Parts, p.Part) >= 0
                    && (row.Dimensions == null || Array.IndexOf(row.Dimensions, p.Dimension) >= 0)
                )
                .ToList();
        }

        private static string PartKey(string key) => key.Replace(" ", string.Empty).ToUpperInvariant();

        /// <summary>One field of the dialogs: its key, the parts of the sample it sets and the dimensions of the sample it is of.</summary>
        private sealed class PartRow
        {
            internal PartRow(string key, params CadStylePart[] parts)
            {
                Key = key;
                Parts = parts;
            }

            internal string Key { get; }

            internal CadStylePart[] Parts { get; }

            /// <summary>Gets the dimensions of the sample the field is of, 0-based; null for every dimension that has the parts.</summary>
            internal int[] Dimensions { get; private set; }

            internal PartRow Of(params int[] dimensions)
            {
                Dimensions = dimensions;
                return this;
            }
        }

        /// <summary>
        /// Measures what a field of the dimension style dialogs sets on the sample, as a callout: DIMEXE from the dimension line
        /// to the end of the first extension line, DIMEXO from the origin to the start of it, DIMFXL along it to the line, DIMASZ
        /// along the second arrowhead from its tip, DIMDLE the run of the line past the second extension line (for ticks),
        /// DIMDLI between the two dimension lines of the Lines sample, DIMTXT the height of the words, DIMGAP the gap between
        /// the dimension line and the words, DIMCEN# the half of the centre mark and DIMJOGANG the angle of the jog (an arc). A
        /// part the style leaves out (DIMSE1, DIMSE2, DIMSD1, DIMSD2 on, DIMCEN none) is given with the Key of its field as the
        /// ghost of where it would be. The points are in the drawing coordinates of the sample, so the lengths are the values
        /// times the overall scale; the label has the value as the field shows it.
        /// </summary>
        /// <param name="sample">The sample, as <see cref="Sample"/> made it.</param>
        /// <param name="values">The style's variables by name.</param>
        /// <param name="key">The key of the field, as <see cref="PartsOf"/> takes it.</param>
        /// <returns>The measures; none where the field sets no distance, or the sample has not the dimension it is measured on.</returns>
        /// <exception cref="ArgumentNullException">Thrown when the sample or the values are null.</exception>
        public static IReadOnlyList<CadStyleMeasure> Measures(
            CadStyleSample sample,
            IDictionary<string, object> values,
            string key
        )
        {
            if (sample == null)
            {
                throw new ArgumentNullException(nameof(sample));
            }

            if (values == null)
            {
                throw new ArgumentNullException(nameof(values));
            }

            var none = Array.Empty<CadStyleMeasure>();

            if (key == null || sample.Shapes.Count == 0)
            {
                return none;
            }

            string name = PartKey(key);
            double k = Number(values, "Dimscale") > 0.0 ? Number(values, "Dimscale") : 1.0;
            SampleShape linear = sample.Shapes[0].Kind == SampleShapeKind.Linear ? sample.Shapes[0] : null;
            SampleShape radial = sample.Shapes.FirstOrDefault(s => s.Kind == SampleShapeKind.Radial);
            SampleShape jogged = sample.Shapes.FirstOrDefault(s => s.Kind == SampleShapeKind.Jogged);

            switch (name)
            {
                case "DIMEXE":
                    return linear == null
                        ? none
                        : Along(name, linear.Q1, Out(linear), Number(values, "Dimexe") * k, values);
                case "DIMEXO":
                    return linear == null
                        ? none
                        : Along(name, linear.P1, Out(linear), Number(values, "Dimexo") * k, values);
                case "DIMFXL":
                    return linear == null
                        ? none
                        : Along(
                            name,
                            linear.Q1 - Out(linear) * (Number(values, "Dimfxlen") * k),
                            Out(linear),
                            Number(values, "Dimfxlen") * k,
                            values,
                            "Dimfxlen"
                        );
                case "DIMASZ":
                    return linear == null
                        ? none
                        : Along(name, linear.Q2, -Line(linear), Number(values, "Dimasz") * k, values);
                case "DIMDLE":
                    return linear == null || !Ticks(values) || !(Number(values, "Dimdle") > 0.0)
                        ? none
                        : Along(name, linear.Q2, Line(linear), Number(values, "Dimdle") * k, values);
                case "DIMDLI":
                    return DimensionLineSpacing(sample, values, k);
                case "DIMTXT":
                    return linear == null ? none : WordsHeight(sample, linear, values, k);
                case "DIMGAP":
                    return linear == null ? none : WordsGap(sample, linear, values, k);
                case "DIMCEN#":
                    return radial == null || Number(values, "Dimcen") == 0.0
                        ? none
                        : Along(
                            name,
                            radial.Centre,
                            new GeoVector2(1.0, 0.0),
                            Math.Abs(Number(values, "Dimcen")) * k,
                            values,
                            "Dimcen"
                        );
                case "DIMCEN":
                    // Of no centre mark the ghost of the half mark it would be, DIMCEN's 2.5 where the style has none.
                    return radial == null || Number(values, "Dimcen") != 0.0
                        ? none
                        : new[] { Ghost(name, radial.Centre, radial.Centre + new GeoVector2(2.5 * k, 0.0), "None") };
                case "DIMJOGANG":
                    return jogged == null ? none : JogAngle(jogged, values);
                case "DIMSE1":
                    return linear == null || !Flag(values, "Dimse1")
                        ? none
                        : ExtensionGhost(name, linear, 0, values, k);
                case "DIMSE2":
                    return linear == null || !Flag(values, "Dimse2")
                        ? none
                        : ExtensionGhost(name, linear, 1, values, k);
                case "DIMSD1":
                    return linear == null || !Flag(values, "Dimsd1") ? none : LineGhost(name, linear, 0, values, k);
                case "DIMSD2":
                    return linear == null || !Flag(values, "Dimsd2") ? none : LineGhost(name, linear, 1, values, k);
                default:
                    return none;
            }
        }

        /// <summary>The way the extension lines of a linear dimension run, from the origin to the dimension line.</summary>
        private static GeoVector2 Out(SampleShape linear)
        {
            GeoVector2 up = linear.Q1 - linear.P1;
            return up.Length > 1E-12 ? up / up.Length : Normal(Line(linear));
        }

        /// <summary>The way a linear dimension's line runs, from its first extension line to its second.</summary>
        private static GeoVector2 Line(SampleShape linear)
        {
            GeoVector2 along = linear.Q2 - linear.Q1;
            return along.Length > 1E-12 ? along / along.Length : new GeoVector2(1.0, 0.0);
        }

        private static GeoVector2 Normal(GeoVector2 way) => new GeoVector2(-way.Y, way.X);

        /// <summary>A callout of a distance from a point along a way, labelled with the value of a variable as the field shows it.</summary>
        private static IReadOnlyList<CadStyleMeasure> Along(
            string key,
            GeoPoint2 from,
            GeoVector2 way,
            double length,
            IDictionary<string, object> values,
            string variable = null
        ) =>
            new[]
            {
                new CadStyleMeasure(
                    key,
                    from,
                    from + way * length,
                    null,
                    Label(key, FieldNumber(Number(values, variable ?? Property(key))))
                ),
            };

        private static CadStyleMeasure Ghost(string key, GeoPoint2 from, GeoPoint2 to, string value) =>
            new CadStyleMeasure(key, from, to, null, Label(key, value));

        /// <summary>The ghost of the extension line a dimension's DIMSE1 or DIMSE2 leaves out: from where it would start to where it would end.</summary>
        private static IReadOnlyList<CadStyleMeasure> ExtensionGhost(
            string key,
            SampleShape linear,
            int side,
            IDictionary<string, object> values,
            double k
        )
        {
            GeoPoint2 origin = side == 0 ? linear.P1 : linear.P2;
            GeoPoint2 on = side == 0 ? linear.Q1 : linear.Q2;
            GeoVector2 way = (on - origin).Length > 1E-12 ? (on - origin) / (on - origin).Length : Normal(Line(linear));
            double length = (on - origin).Length;
            double offset = Number(values, "Dimexo") * k;
            double start = Flag(values, "DimfxlenOn")
                ? Math.Max(length - Number(values, "Dimfxlen") * k, Math.Min(offset, length))
                : offset;
            return new[] { Ghost(key, origin + way * start, on + way * (Number(values, "Dimexe") * k), "on") };
        }

        /// <summary>The ghost of the half of the dimension line a dimension's DIMSD1 or DIMSD2 leaves out, from the back of its arrowhead to the middle.</summary>
        private static IReadOnlyList<CadStyleMeasure> LineGhost(
            string key,
            SampleShape linear,
            int side,
            IDictionary<string, object> values,
            double k
        )
        {
            GeoVector2 way = Line(linear);
            double arrow = Number(values, "Dimasz") * k;
            GeoPoint2 middle = new GeoPoint2((linear.Q1.X + linear.Q2.X) / 2.0, (linear.Q1.Y + linear.Q2.Y) / 2.0);
            return new[]
            {
                side == 0
                    ? Ghost(key, linear.Q1 + way * arrow, middle, "on")
                    : Ghost(key, middle, linear.Q2 - way * arrow, "on"),
            };
        }

        /// <summary>DIMDLI: between the dimension lines of the first two dimensions of the Lines sample, at the end of the first.</summary>
        private static IReadOnlyList<CadStyleMeasure> DimensionLineSpacing(
            CadStyleSample sample,
            IDictionary<string, object> values,
            double k
        )
        {
            if (
                sample.Kind != CadDimSampleKind.Lines
                || sample.Shapes.Count < 2
                || sample.Shapes[0].Kind != SampleShapeKind.Linear
                || sample.Shapes[1].Kind != SampleShapeKind.Linear
            )
            {
                return Array.Empty<CadStyleMeasure>();
            }

            SampleShape first = sample.Shapes[0];
            GeoVector2 up = Normal(Line(first));
            GeoVector2 between = sample.Shapes[1].Q1 - first.Q1;
            double side = between.X * up.X + between.Y * up.Y >= 0.0 ? 1.0 : -1.0;
            return Along("DIMDLI", first.Q2, up * side, Number(values, "Dimdli") * k, values);
        }

        /// <summary>The words of a sample dimension: their middle, the way they read and the way up, and their half width and half height as the font measures them.</summary>
        private static bool WordsBox(
            CadStyleSample sample,
            SampleShape shape,
            out GeoPoint2 middle,
            out GeoVector2 read,
            out GeoVector2 up,
            out double halfWidth,
            out double halfHeight
        )
        {
            DxfMText words = shape.Words;
            middle = default;
            read = default;
            up = default;
            halfWidth = 0.0;
            halfHeight = 0.0;

            if (words == null || sample.Style == null)
            {
                return false;
            }

            double width = CadCommands.WordsWidth(sample.Style, words.Contents, words.TextHeight, out double tall);
            middle = new GeoPoint2(words.Location.X, words.Location.Y);
            read = new GeoVector2(Math.Cos(words.Rotation), Math.Sin(words.Rotation));
            up = Normal(read);
            halfWidth = width / 2.0;
            halfHeight = tall / 2.0;
            return true;
        }

        /// <summary>DIMTXT: the height of the words, along their left edge.</summary>
        private static IReadOnlyList<CadStyleMeasure> WordsHeight(
            CadStyleSample sample,
            SampleShape linear,
            IDictionary<string, object> values,
            double k
        )
        {
            if (
                !WordsBox(
                    sample,
                    linear,
                    out GeoPoint2 middle,
                    out GeoVector2 read,
                    out GeoVector2 up,
                    out double hw,
                    out _
                )
            )
            {
                return Array.Empty<CadStyleMeasure>();
            }

            double half = Number(values, "Dimtxt") * k / 2.0;
            GeoPoint2 left = middle - read * hw;
            return new[]
            {
                new CadStyleMeasure(
                    "DIMTXT",
                    left - up * half,
                    left + up * half,
                    null,
                    Label("DIMTXT", FieldNumber(Number(values, "Dimtxt")))
                ),
            };
        }

        /// <summary>
        /// DIMGAP: from the dimension line, at the foot of the words' middle, to the words' box, which stands that gap off it; for
        /// words on the line (DIMTAD 0) the gap the line is broken by beside them.
        /// </summary>
        private static IReadOnlyList<CadStyleMeasure> WordsGap(
            CadStyleSample sample,
            SampleShape linear,
            IDictionary<string, object> values,
            double k
        )
        {
            if (
                !WordsBox(
                    sample,
                    linear,
                    out GeoPoint2 middle,
                    out GeoVector2 read,
                    out _,
                    out double hw,
                    out double hh
                )
            )
            {
                return Array.Empty<CadStyleMeasure>();
            }

            GeoVector2 along = Line(linear);
            GeoVector2 normal = Normal(along);
            GeoVector2 off = middle - linear.Q1;
            GeoPoint2 foot = linear.Q1 + along * (off.X * along.X + off.Y * along.Y);
            double height = (middle - foot).X * normal.X + (middle - foot).Y * normal.Y;
            double gap = Math.Abs(Number(values, "Dimgap")) * k;
            string label = Label("DIMGAP", FieldNumber(Math.Abs(Number(values, "Dimgap"))));

            if (Math.Abs(height) < hh)
            {
                // On the line: the gap beside the words, where the line stops short of them.
                GeoPoint2 edge = middle - read * hw;
                return new[] { new CadStyleMeasure("DIMGAP", edge - read * gap, edge, null, label) };
            }

            return new[]
            {
                new CadStyleMeasure("DIMGAP", foot, foot + normal * (height > 0.0 ? gap : -gap), null, label),
            };
        }

        /// <summary>DIMJOGANG: an arc about the end of the jog on the radius, from the radius towards the jog, through the angle set.</summary>
        private static IReadOnlyList<CadStyleMeasure> JogAngle(SampleShape jogged, IDictionary<string, object> values)
        {
            GeoVector2 jog = jogged.Front - jogged.Back;

            if (jog.Length <= 1E-9)
            {
                return Array.Empty<CadStyleMeasure>();
            }

            GeoVector2 radius = (jogged.Rim - jogged.Centre) / (jogged.Rim - jogged.Centre).Length;
            GeoVector2 jogWay = jog / jog.Length;

            // The radius's way the jog leans towards, so that the angle between them is the sharp one of the dialog.
            if (radius.X * jogWay.X + radius.Y * jogWay.Y < 0.0)
            {
                radius = -radius;
            }

            double angle = Math.Atan2(
                radius.X * jogWay.Y - radius.Y * jogWay.X,
                radius.X * jogWay.X + radius.Y * jogWay.Y
            );
            double degrees = Number(values, "Dimjogang") * 180.0 / Math.PI;
            return new[]
            {
                new CadStyleMeasure(
                    "DIMJOGANG",
                    jogged.Back,
                    jogged.Back + radius * (0.7 * jog.Length),
                    angle,
                    Label("DIMJOGANG", degrees.ToString("0.####", CultureInfo.InvariantCulture))
                ),
            };
        }

        /// <summary>Whether the second arrowhead is a tick, which the dimension line runs on past by DIMDLE.</summary>
        private static bool Ticks(IDictionary<string, object> values)
        {
            object second =
                Flag(values, "Dimsah") && values.TryGetValue("Dimblk2", out object two) ? two
                : values.TryGetValue("Dimblk", out object one) ? one
                : null;
            return second is string arrow && CadArrowheads.IsTick(CadArrowheads.BlockOf(arrow));
        }

        /// <summary>The name of the variable a key stands for, as the library names it: <c>DIMEXE</c> is <c>Dimexe</c>.</summary>
        private static string Property(string key) =>
            key.Length > 0 ? char.ToUpperInvariant(key[0]) + key.Substring(1).ToLowerInvariant() : key;

        private static double Number(IDictionary<string, object> values, string name)
        {
            if (name == null || !values.TryGetValue(name, out object value))
            {
                return 0.0;
            }

            switch (value)
            {
                case double number:
                    return number;
                case int whole:
                    return whole;
                case IConvertible convertible when !(value is string) && !(value is bool):
                    return convertible.ToDouble(CultureInfo.InvariantCulture);
                default:
                    return 0.0;
            }
        }

        private static bool Flag(IDictionary<string, object> values, string name) =>
            values.TryGetValue(name, out object value) && value is bool on && on;

        /// <summary>A number as the field of a dialog shows it: at least one decimal, up to four (1.25, 2.5, 3.75, 1.0).</summary>
        private static string FieldNumber(double number) => number.ToString("0.0###", CultureInfo.InvariantCulture);

        private static string Label(string key, string value) =>
            (key.EndsWith("#", StringComparison.Ordinal) ? key.Substring(0, key.Length - 1) : key) + " = " + value;
    }
}
