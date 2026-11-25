using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;
using GeometryHelper;

namespace OpenCAD2D
{
    /// <summary>
    /// OpenCAD2D ISO, the presenter's own single-stroke font in the proportions of ISO 3098 type B (TCVN 7284), upright, with
    /// the Vietnamese letters: 242 code points made in code into the bytes of a compiled SHX unifont, 100 above its baseline
    /// and 30 below, so that a text 2.5 high draws its capitals 2.5 high and the lettering's line width d, a tenth of that,
    /// is the 0.25 of the plotted lineweight. A unit of the font is d / 10: the skeleton, the centreline of each stroke,
    /// lies on that grid, and the pen lands on it.
    /// </summary>
    /// <remarks>
    /// <para>
    /// The skeleton is the text file <c>opencad2d.glyphs.txt</c> beside this class, one glyph a line, its path of lines, arcs,
    /// circles and dots in d. A letter's ink starts at x 0; the generator adds the left bearing of 1 d, and a right bearing of
    /// 1 d to the advance, so that two letters stand 2 d apart as type B asks (a space is 4 d, a word gap 6 d, a digit 7 d
    /// wide). The 134 Vietnamese letters beyond Đ, đ and the horned Ơ, ơ, Ư, ư are composed from a base letter and the marks
    /// of the file: marks sit 1 d above the capitals and 1 d above the x-height, a circumflex takes an acute, grave or hook
    /// beside its right arm and a tilde above it, a breve takes every tone above it, a dot below sits in the descender.
    /// </para>
    /// <para>
    /// The bytes are written as AutoCAD 2021 reads them (measured): an arc that starts and sweeps by quarter turns is code
    /// 10 (radius 1 to 127, the sign bit of the octant byte for clockwise), any other arc code 12 by its bulge, cut into
    /// equal parts of at most a half circle whose ends are on the grid, so that the pen lands where the file says. A
    /// glyph's last move puts the pen at its advance. Where the file is wrong, or an arc cannot be written, the generator
    /// throws an <see cref="InvalidDataException"/> that names the glyph's code and what is wrong.
    /// </para>
    /// </remarks>
    internal static class OpenCad2dIso
    {
        private const string ResourceName = "OpenCAD2D.opencad2d.glyphs.txt";

        // d is 10 font units.
        private const int Bearing = 10;
        private const int DotRadius = 3;
        private const int CapitalMarks = 110;
        private const int SmallMarks = 80;
        private const int StackedMarks = 20;
        private const int BesideX = 21;
        private const int BesideY = 5;
        private const int DotBelowY = -16;
        private const int CombiningBack = 35;

        // What a signed byte holds, the most a record may take (our own margin under AutoCAD's 2000), and how far the pen's
        // path of an arc written by its bulge may stray from the circle, in font units.
        private const int MostByte = 127;
        private const int MostRecord = 2000;
        private const int MostParts = 8;
        private const double MostArcError = 0.5;

        private static readonly Tolerance Exact = new Tolerance(1E-9, 1E-9, 1E-9);

        // Half a grid step: an arc on the grid whose widest point falls between two grid points, as the parentheses' does by
        // 0.006 of a unit, still has the width written for it; a typo of a tenth of d does not.
        private static readonly Tolerance Grid = new Tolerance(0.5, 0.5);

        // How far a number typed in the file, to one decimal of d, may be off its tenth after the float of reading it.
        private static readonly Tolerance Typed = new Tolerance(1E-6, 1E-6);

        private static readonly string[] ToneNames = { null, "grave", "acute", "hook", "tilde", "dotbelow" };
        private static readonly int[] ToneMarks = { 0, 0x300, 0x301, 0x309, 0x303, 0x323 };

        // The bases of the precomposed letters, capitals: the glyph they are drawn on, their decomposition up to the tone,
        // the mark over them, and the codes of none, grave, acute, hook, tilde and dot below.
        private static readonly Row[] Rows =
        {
            new Row(0x41, "A", null, 0x41, 0xC0, 0xC1, 0x1EA2, 0xC3, 0x1EA0),
            new Row(0x41, "Ă", "breve", 0x102, 0x1EB0, 0x1EAE, 0x1EB2, 0x1EB4, 0x1EB6),
            new Row(0x41, "Â", "circumflex", 0xC2, 0x1EA6, 0x1EA4, 0x1EA8, 0x1EAA, 0x1EAC),
            new Row(0x45, "E", null, 0x45, 0xC8, 0xC9, 0x1EBA, 0x1EBC, 0x1EB8),
            new Row(0x45, "Ê", "circumflex", 0xCA, 0x1EC0, 0x1EBE, 0x1EC2, 0x1EC4, 0x1EC6),
            new Row(0x49, "I", null, 0x49, 0xCC, 0xCD, 0x1EC8, 0x128, 0x1ECA),
            new Row(0x4F, "O", null, 0x4F, 0xD2, 0xD3, 0x1ECE, 0xD5, 0x1ECC),
            new Row(0x4F, "Ô", "circumflex", 0xD4, 0x1ED2, 0x1ED0, 0x1ED4, 0x1ED6, 0x1ED8),
            new Row(0x1A0, "Ơ", null, 0x1A0, 0x1EDC, 0x1EDA, 0x1EDE, 0x1EE0, 0x1EE2),
            new Row(0x55, "U", null, 0x55, 0xD9, 0xDA, 0x1EE6, 0x168, 0x1EE4),
            new Row(0x1AF, "Ư", null, 0x1AF, 0x1EEA, 0x1EE8, 0x1EEC, 0x1EEE, 0x1EF0),
            new Row(0x59, "Y", null, 0x59, 0x1EF2, 0xDD, 0x1EF6, 0x1EF8, 0x1EF4),
        };

        private static readonly Lazy<Letter[]> VietnameseLetters = new Lazy<Letter[]>(Letters);
        private static readonly Lazy<Glyph[]> Font = new Lazy<Glyph[]>(() => Build(Parse(ReadData())));
        private static readonly Lazy<Dictionary<int, Glyph>> ByCode = new Lazy<Dictionary<int, Glyph>>(() =>
            Font.Value.ToDictionary(g => g.Code)
        );
        private static readonly Lazy<byte[]> Bytes = new Lazy<byte[]>(() => Write(Font.Value));

        /// <summary>A step of a stroke: a line to a point, an arc about a centre to a point, or a whole circle, in font units.</summary>
        internal sealed class Step
        {
            /// <summary>Makes a line to a point.</summary>
            /// <param name="x">The x of the end.</param>
            /// <param name="y">The y of the end.</param>
            internal Step(int x, int y)
            {
                X = x;
                Y = y;
            }

            /// <summary>Makes an arc, or a whole circle, about a centre to a point.</summary>
            /// <param name="centreX">The x of the centre.</param>
            /// <param name="centreY">The y of the centre.</param>
            /// <param name="counterClockwise">Whether the arc turns counter-clockwise.</param>
            /// <param name="whole">Whether the step is a whole circle, its end where it starts.</param>
            /// <param name="x">The x of the end.</param>
            /// <param name="y">The y of the end.</param>
            internal Step(int centreX, int centreY, bool counterClockwise, bool whole, int x, int y)
            {
                IsArc = true;
                CentreX = centreX;
                CentreY = centreY;
                CounterClockwise = counterClockwise;
                Whole = whole;
                X = x;
                Y = y;
            }

            /// <summary>Gets whether the step is an arc, or a whole circle, rather than a line.</summary>
            internal bool IsArc { get; }

            /// <summary>Gets whether the arc turns counter-clockwise; whole circles do.</summary>
            internal bool CounterClockwise { get; }

            /// <summary>Gets whether the step is a whole circle, which starts and ends at (<see cref="X"/>, <see cref="Y"/>).</summary>
            internal bool Whole { get; }

            /// <summary>Gets the x of the arc's centre.</summary>
            internal int CentreX { get; }

            /// <summary>Gets the y of the arc's centre.</summary>
            internal int CentreY { get; }

            /// <summary>Gets the x where the step ends.</summary>
            internal int X { get; }

            /// <summary>Gets the y where the step ends.</summary>
            internal int Y { get; }

            /// <summary>Gets the step moved by a vector.</summary>
            internal Step Moved(int dx, int dy) =>
                IsArc
                    ? new Step(CentreX + dx, CentreY + dy, CounterClockwise, Whole, X + dx, Y + dy)
                    : new Step(X + dx, Y + dy);
        }

        /// <summary>A stroke: where the pen goes down and the steps it draws, or a dot, a whole circle of radius 3 that the ink box leaves out.</summary>
        internal sealed class Stroke
        {
            /// <summary>Makes a stroke.</summary>
            /// <param name="x">The x where the pen goes down.</param>
            /// <param name="y">The y where the pen goes down.</param>
            /// <param name="steps">The steps it draws.</param>
            /// <param name="dot">Whether it is a dot.</param>
            internal Stroke(int x, int y, IReadOnlyList<Step> steps, bool dot)
            {
                X = x;
                Y = y;
                Steps = steps;
                Dot = dot;
            }

            /// <summary>Gets the x where the pen goes down.</summary>
            internal int X { get; }

            /// <summary>Gets the y where the pen goes down.</summary>
            internal int Y { get; }

            /// <summary>Gets the steps, in the order they are drawn.</summary>
            internal IReadOnlyList<Step> Steps { get; }

            /// <summary>Gets whether the stroke is a dot.</summary>
            internal bool Dot { get; }

            /// <summary>Gets the stroke moved by a vector.</summary>
            internal Stroke Moved(int dx, int dy) =>
                new Stroke(X + dx, Y + dy, Steps.Select(s => s.Moved(dx, dy)).ToArray(), Dot);
        }

        /// <summary>A letter or sign of the font: its strokes from the pen's start, placed by its left bearing, and its advance, in font units.</summary>
        internal sealed class Glyph
        {
            /// <summary>Makes a glyph.</summary>
            /// <param name="code">The Unicode number.</param>
            /// <param name="width">The width of the ink, or of the cell for a digit.</param>
            /// <param name="advance">How far it moves the pen.</param>
            /// <param name="top">The x where a mark above is centred.</param>
            /// <param name="bottom">The x where a mark below is centred.</param>
            /// <param name="strokes">The strokes, the left bearing in them.</param>
            internal Glyph(int code, int width, int advance, double top, double bottom, IReadOnlyList<Stroke> strokes)
            {
                Code = code;
                Width = width;
                Advance = advance;
                Top = top;
                Bottom = bottom;
                Strokes = strokes;
            }

            /// <summary>Gets the Unicode number.</summary>
            internal int Code { get; }

            /// <summary>Gets the width of the ink as the file writes it, 30 for 3 d; for a digit the width of its cell.</summary>
            internal int Width { get; }

            /// <summary>Gets how far the glyph moves the pen: 20 more than the ink, 70 for a digit, 40 for a space, 0 for a combining mark.</summary>
            internal int Advance { get; }

            /// <summary>Gets the x, bearing included, where a mark above the glyph is centred.</summary>
            internal double Top { get; }

            /// <summary>Gets the x, bearing included, where a mark below the glyph is centred.</summary>
            internal double Bottom { get; }

            /// <summary>Gets the strokes, in the order they are drawn.</summary>
            internal IReadOnlyList<Stroke> Strokes { get; }
        }

        /// <summary>The glyphs and the marks of the data file, as read: the marks in their own frame, their anchor at x 0 and the bottom of their zone at y 0.</summary>
        internal sealed class Skeleton
        {
            /// <summary>Makes the skeleton of a data file.</summary>
            /// <param name="glyphs">The glyphs, by code.</param>
            /// <param name="marks">The marks by name.</param>
            internal Skeleton(IReadOnlyList<Glyph> glyphs, IReadOnlyDictionary<string, IReadOnlyList<Stroke>> marks)
            {
                Glyphs = glyphs;
                Marks = marks;
            }

            /// <summary>Gets the glyphs of the file by their code, the letters made of a base and marks left out.</summary>
            internal IReadOnlyList<Glyph> Glyphs { get; }

            /// <summary>Gets the marks by their name: acute, grave, hook, tilde, dotbelow, circumflex and breve.</summary>
            internal IReadOnlyDictionary<string, IReadOnlyList<Stroke>> Marks { get; }
        }

        /// <summary>A Vietnamese letter made of a base and marks, or one of the horned letters the file draws itself.</summary>
        internal sealed class Letter
        {
            /// <summary>Makes a letter of the table.</summary>
            /// <param name="code">The code point.</param>
            /// <param name="baseCode">The code of the glyph it is drawn on.</param>
            /// <param name="modifier">The mark that goes with the base, or null.</param>
            /// <param name="tone">The tone, 0 to 5.</param>
            /// <param name="capital">Whether it is a capital.</param>
            /// <param name="decomposition">The base and marks in the order of the table.</param>
            internal Letter(int code, int baseCode, string modifier, int tone, bool capital, string decomposition)
            {
                Code = code;
                BaseCode = baseCode;
                Modifier = modifier;
                Tone = tone;
                Capital = capital;
                Decomposition = decomposition;
            }

            /// <summary>Gets the code point of the letter.</summary>
            internal int Code { get; }

            /// <summary>Gets the code of the glyph it is drawn on, the letter's base: A for Ă and Â, Ơ for Ợ.</summary>
            internal int BaseCode { get; }

            /// <summary>Gets the mark that goes with the base, <c>breve</c> or <c>circumflex</c>, or null.</summary>
            internal string Modifier { get; }

            /// <summary>Gets the tone: 0 none, 1 grave, 2 acute, 3 hook, 4 tilde, 5 dot below.</summary>
            internal int Tone { get; }

            /// <summary>Gets whether the letter is a capital.</summary>
            internal bool Capital { get; }

            /// <summary>
            /// Gets the letter as its base and marks, in the order the table gives them (the base, the circumflex, breve or
            /// horn, then the tone): its canonical decomposition, up to the order of marks Unicode fixes.
            /// </summary>
            internal string Decomposition { get; }
        }

        private sealed class Row
        {
            internal Row(int baseCode, string spelled, string modifier, params int[] codes)
            {
                BaseCode = baseCode;
                Spelled = spelled;
                Modifier = modifier;
                Codes = codes;
            }

            internal int BaseCode { get; }

            internal string Spelled { get; }

            internal string Modifier { get; }

            internal int[] Codes { get; }
        }

        /// <summary>Gets the code points of the font, 242 of them, in ascending order.</summary>
        internal static IReadOnlyList<int> CodePoints => Font.Value.Select(g => g.Code).ToArray();

        /// <summary>
        /// Gets the Vietnamese letters the font composes or draws beyond ASCII, 132 of them, in ascending order: both cases of
        /// the table of the 12 bases and their 5 tones, but for the 12 plain letters and Đ, đ, which carry no marks.
        /// </summary>
        internal static IReadOnlyList<Letter> Vietnamese => VietnameseLetters.Value;

        /// <summary>
        /// The bytes of the font as a compiled SHX unifont 100 above and 30 below, its letters by their Unicode numbers,
        /// made once from the embedded data file.
        /// </summary>
        /// <returns>The bytes, a copy.</returns>
        internal static byte[] Unifont() => (byte[])Bytes.Value.Clone();

        /// <summary>Gets a glyph of the font as its strokes, composed letters and combining marks included; null where the font has none.</summary>
        /// <param name="code">The Unicode number.</param>
        /// <returns>The glyph, or null.</returns>
        internal static Glyph GlyphOf(int code) => ByCode.Value.TryGetValue(code, out Glyph glyph) ? glyph : null;

        /// <summary>Reads the data file, the glyphs as they are drawn on their own: a letter's ink moved by its left bearing.</summary>
        /// <param name="text">The text of the file.</param>
        /// <returns>The glyphs and marks.</returns>
        /// <exception cref="InvalidDataException">A line is wrong; the message names its glyph's code or mark and the line.</exception>
        internal static Skeleton Parse(string text)
        {
            var glyphs = new SortedDictionary<int, Glyph>();
            var marks = new SortedDictionary<string, IReadOnlyList<Stroke>>(StringComparer.Ordinal);
            string[] lines = text.Split('\n');

            for (int n = 0; n < lines.Length; n++)
            {
                string line = lines[n].Trim();

                if (line.Length == 0 || line[0] == '#')
                {
                    continue;
                }

                string[] tokens = line.Split(new[] { ' ', '\t' }, StringSplitOptions.RemoveEmptyEntries);

                if (tokens[0] == "glyph")
                {
                    Glyph glyph = ParseGlyph(tokens, n + 1);

                    if (glyphs.ContainsKey(glyph.Code))
                    {
                        throw Wrong("glyph " + Hex(glyph.Code), n + 1, "the code is there twice");
                    }

                    glyphs.Add(glyph.Code, glyph);
                }
                else if (tokens[0] == "mark" && tokens.Length >= 3 && tokens[2] == ":")
                {
                    string who = "mark " + tokens[1];

                    if (marks.ContainsKey(tokens[1]))
                    {
                        throw Wrong(who, n + 1, "the name is there twice");
                    }

                    marks.Add(tokens[1], Path(tokens, 3, who, n + 1));
                }
                else
                {
                    throw Wrong("data", n + 1, "a line is neither a glyph nor a mark: " + line);
                }
            }

            return new Skeleton(glyphs.Values.ToArray(), marks);
        }

        /// <summary>
        /// The whole font from the data: its glyphs, the 134 letters made of a base and marks (Đ and đ only they are in the
        /// data) and the five combining tone marks, in ascending order of code.
        /// </summary>
        /// <param name="skeleton">The data file as <see cref="Parse"/> read it.</param>
        /// <returns>The glyphs.</returns>
        /// <exception cref="InvalidDataException">A base or a mark a letter needs is missing, or a code is there twice.</exception>
        internal static Glyph[] Build(Skeleton skeleton)
        {
            var glyphs = new SortedDictionary<int, Glyph>();

            foreach (Glyph glyph in skeleton.Glyphs)
            {
                glyphs.Add(glyph.Code, glyph);
            }

            foreach (Letter letter in Vietnamese)
            {
                if (letter.Modifier == null && letter.Tone == 0)
                {
                    continue; // Ơ, ơ, Ư, ư: drawn in the file.
                }

                Add(glyphs, Compose(skeleton, letter));
            }

            for (int tone = 1; tone < ToneNames.Length; tone++)
            {
                string name = ToneNames[tone];
                int y = tone == 5 ? DotBelowY : SmallMarks;
                Add(
                    glyphs,
                    new Glyph(ToneMarks[tone], 0, 0, 0, 0, Place(skeleton, name, -CombiningBack, y, ToneMarks[tone]))
                );
            }

            return glyphs.Values.ToArray();
        }

        /// <summary>
        /// Writes glyphs as the bytes of a compiled SHX unifont 100 above and 30 below: the pen up and moved to each stroke,
        /// down along its lines (code 8), quarter-turn arcs (code 10) and the other arcs by their bulge (code 12), then up
        /// and moved to the advance.
        /// </summary>
        /// <param name="glyphs">The glyphs, any order.</param>
        /// <returns>The bytes.</returns>
        /// <exception cref="InvalidDataException">A glyph cannot be written: a code there twice, a quarter-turn arc of a radius over 127, an arc no grid points can cut right, a line that cannot be split, a record of 2000 bytes.</exception>
        internal static byte[] Write(IEnumerable<Glyph> glyphs)
        {
            Glyph[] sorted = glyphs.OrderBy(g => g.Code).ToArray();
            var bytes = new List<byte>(Encoding.ASCII.GetBytes("AutoCAD-86 unifont 1.0\r\n\x1A"));
            byte[] info = Encoding
                .ASCII.GetBytes("OpenCAD2D ISO\0")
                .Concat(new byte[] { 100, 30, 0, 0, 0, 0 })
                .ToArray();
            Word(bytes, sorted.Length + 1);
            Word(bytes, 0);
            Word(bytes, info.Length);
            bytes.AddRange(info);

            for (int i = 0; i < sorted.Length; i++)
            {
                if (i > 0 && sorted[i].Code == sorted[i - 1].Code)
                {
                    throw new InvalidDataException("glyph " + Hex(sorted[i].Code) + ": the code is there twice");
                }

                byte[] record = Record(sorted[i]);
                Word(bytes, sorted[i].Code);
                Word(bytes, record.Length);
                bytes.AddRange(record);
            }

            return bytes.ToArray();
        }

        private static string ReadData()
        {
            using (Stream stream = typeof(OpenCad2dIso).Assembly.GetManifestResourceStream(ResourceName))
            {
                if (stream == null)
                {
                    throw new InvalidDataException("data: the resource " + ResourceName + " is not in the assembly");
                }

                using (var reader = new StreamReader(stream, new UTF8Encoding(false)))
                {
                    return reader.ReadToEnd();
                }
            }
        }

        private static string Hex(int code) => code.ToString("X4", CultureInfo.InvariantCulture);

        private static InvalidDataException Wrong(string who, int line, string what) =>
            new InvalidDataException(who + ", line " + line.ToString(CultureInfo.InvariantCulture) + ": " + what);

        private static Glyph ParseGlyph(string[] tokens, int line)
        {
            int colon = Array.IndexOf(tokens, ":", Math.Min(3, tokens.Length));

            if (tokens.Length < 4 || colon < 0)
            {
                throw Wrong(
                    "glyph " + (tokens.Length > 1 ? tokens[1] : "?"),
                    line,
                    "the line is no glyph <hex> <char> w=.. : path"
                );
            }

            if (
                tokens[1].Length < 4
                || tokens[1].Length > 6
                || !int.TryParse(tokens[1], NumberStyles.AllowHexSpecifier, CultureInfo.InvariantCulture, out int code)
                || code > 0xFFFF
            )
            {
                throw Wrong("glyph " + tokens[1], line, "the code is not 4 to 6 hex digits of a 16 bit number");
            }

            string who = "glyph " + Hex(code);
            double? width = null;
            double? advance = null;
            double? top = null;
            double? bottom = null;

            for (int i = 3; i < colon; i++)
            {
                int equals = tokens[i].IndexOf('=');
                string key = equals < 0 ? tokens[i] : tokens[i].Substring(0, equals);

                if (
                    equals < 0
                    || !double.TryParse(
                        tokens[i].Substring(equals + 1),
                        NumberStyles.Float,
                        CultureInfo.InvariantCulture,
                        out double value
                    )
                )
                {
                    throw Wrong(who, line, "the attribute '" + tokens[i] + "' is not name=number");
                }

                switch (key)
                {
                    case "w":
                        width = value;
                        break;
                    case "adv":
                        advance = value;
                        break;
                    case "top":
                        top = value;
                        break;
                    case "bot":
                        bottom = value;
                        break;
                    default:
                        throw Wrong(who, line, "the attribute '" + key + "' is not w, adv, top or bot");
                }
            }

            if (width == null)
            {
                throw Wrong(who, line, "w= is missing");
            }

            int w = OnGrid(width.Value, who, line, "w");
            int adv = advance == null ? w + (2 * Bearing) : OnGrid(advance.Value, who, line, "adv");
            List<Stroke> strokes = Path(tokens, colon + 1, who, line);
            CheckInk(strokes, w, advance != null, who, line);
            double topX = (top ?? (width.Value / 2.0)) * 10.0;
            double bottomX = bottom == null ? topX : bottom.Value * 10.0;
            return new Glyph(
                code,
                w,
                adv,
                topX + Bearing,
                bottomX + Bearing,
                strokes.Select(s => s.Moved(Bearing, 0)).ToArray()
            );
        }

        /// <summary>A number of d in the file as font units: a tenth of d, a whole number of units, or a mistake.</summary>
        private static int OnGrid(double value, string who, int line, string what)
        {
            double units = value * 10.0;
            double whole = Math.Round(units);

            if (Math.Abs(units - whole) > Typed.EqualPoint)
            {
                throw Wrong(
                    who,
                    line,
                    what + " " + value.ToString(CultureInfo.InvariantCulture) + " is not on the 0.1 grid"
                );
            }

            return (int)whole;
        }

        private static int[] Numbers(string token, int count, string who, int line, string op)
        {
            string[] parts = token.Split(',');
            var numbers = new int[count];

            for (int i = 0; i < count; i++)
            {
                if (
                    parts.Length != count
                    || !double.TryParse(parts[i], NumberStyles.Float, CultureInfo.InvariantCulture, out double value)
                )
                {
                    throw Wrong(
                        who,
                        line,
                        "op '" + op + "' wants " + count.ToString(CultureInfo.InvariantCulture) + " numbers, x,y first"
                    );
                }

                numbers[i] = OnGrid(value, who, line, "op '" + op + "'");
            }

            return numbers;
        }

        private static List<Stroke> Path(string[] tokens, int from, string who, int line)
        {
            var strokes = new List<Stroke>();
            var steps = new List<Step>();
            int startX = 0;
            int startY = 0;
            int x = 0;
            int y = 0;
            bool open = false;

            void Close()
            {
                if (open)
                {
                    if (steps.Count == 0)
                    {
                        throw Wrong(who, line, "a stroke at " + startX + "," + startY + " has no line or arc");
                    }

                    strokes.Add(new Stroke(startX, startY, steps.ToArray(), false));
                    steps.Clear();
                    open = false;
                }
            }

            for (int i = from; i < tokens.Length; i++)
            {
                string token = tokens[i];

                if (token == "·" || token == "|")
                {
                    continue;
                }

                char op = token[0];

                if (op == 'M')
                {
                    Close();
                    int[] p = Numbers(token.Substring(1), 2, who, line, token);
                    startX = x = p[0];
                    startY = y = p[1];
                    open = true;
                }
                else if (op == 'L')
                {
                    int[] p = Numbers(token.Substring(1), 2, who, line, token);

                    if (!open)
                    {
                        throw Wrong(who, line, "op '" + token + "' has no pen: a stroke starts with M");
                    }

                    steps.Add(new Step(p[0], p[1]));
                    x = p[0];
                    y = p[1];
                }
                else if (token == "A+" || token == "A-")
                {
                    if (!open || i + 2 >= tokens.Length)
                    {
                        throw Wrong(who, line, "op '" + token + "' has no pen or lacks its centre and end");
                    }

                    int[] c = Numbers(tokens[++i], 2, who, line, token);
                    int[] e = Numbers(tokens[++i], 2, who, line, token);
                    double r1 = Math.Sqrt(((x - c[0]) * (x - c[0])) + ((y - c[1]) * (y - c[1])));
                    double r2 = Math.Sqrt(((e[0] - c[0]) * (e[0] - c[0])) + ((e[1] - c[1]) * (e[1] - c[1])));

                    if (Math.Abs(r1 - r2) > Exact.EqualPoint)
                    {
                        throw Wrong(
                            who,
                            line,
                            "op '"
                                + token
                                + " "
                                + tokens[i - 1]
                                + " "
                                + tokens[i]
                                + "': the pen and the end are not the same distance from the centre"
                        );
                    }

                    if (r1 < Exact.EqualPoint || (x == e[0] && y == e[1]))
                    {
                        throw Wrong(who, line, "op '" + token + "' starts and ends at one point: a whole circle is O");
                    }

                    steps.Add(new Step(c[0], c[1], token[1] == '+', false, e[0], e[1]));
                    x = e[0];
                    y = e[1];
                }
                else if (op == 'O' || op == 'D')
                {
                    Close();
                    int[] p = Numbers(token.Substring(1), op == 'O' ? 3 : 2, who, line, token);
                    int r = op == 'O' ? p[2] : DotRadius;

                    if (r <= 0)
                    {
                        throw Wrong(who, line, "op '" + token + "' has no radius");
                    }

                    strokes.Add(
                        new Stroke(
                            p[0] + r,
                            p[1],
                            new[] { new Step(p[0], p[1], true, true, p[0] + r, p[1]) },
                            op == 'D'
                        )
                    );
                }
                else
                {
                    throw Wrong(who, line, "op '" + token + "' is not M, L, A+, A-, O or D");
                }
            }

            Close();
            return strokes;
        }

        /// <summary>
        /// Fails where the ink of the lines, arcs and circles (the dots left out) does not run from x 0 to the width, or for a
        /// glyph with an advance of its own, a digit, does not lie inside 0 to the width.
        /// </summary>
        private static void CheckInk(IReadOnlyList<Stroke> strokes, int width, bool cell, string who, int line)
        {
            double left = double.MaxValue;
            double right = double.MinValue;

            foreach (Stroke stroke in strokes.Where(s => !s.Dot))
            {
                Reach(stroke.X, ref left, ref right);
                int x = stroke.X;
                int y = stroke.Y;

                foreach (Step step in stroke.Steps)
                {
                    if (step.IsArc)
                    {
                        Reach(step, x, y, ref left, ref right);
                    }

                    Reach(step.X, ref left, ref right);
                    x = step.X;
                    y = step.Y;
                }
            }

            if (left > right)
            {
                return;
            }

            bool fits = cell
                ? left >= -Grid.EqualPoint && right <= width + Grid.EqualPoint
                : Math.Abs(left) <= Grid.EqualPoint && Math.Abs(right - width) <= Grid.EqualPoint;

            if (!fits)
            {
                throw Wrong(
                    who,
                    line,
                    "the ink runs from "
                        + left.ToString("0.##", CultureInfo.InvariantCulture)
                        + " to "
                        + right.ToString("0.##", CultureInfo.InvariantCulture)
                        + " units, not "
                        + (cell ? "inside 0 to " : "0 to ")
                        + width.ToString(CultureInfo.InvariantCulture)
                );
            }
        }

        private static void Reach(double x, ref double left, ref double right)
        {
            left = Math.Min(left, x);
            right = Math.Max(right, x);
        }

        /// <summary>The x an arc reaches along its way: its centre's x less or plus its radius where the arc passes the left or right of its circle.</summary>
        private static void Reach(Step step, int fromX, int fromY, ref double left, ref double right)
        {
            double r = Math.Sqrt(
                ((fromX - step.CentreX) * (fromX - step.CentreX)) + ((fromY - step.CentreY) * (fromY - step.CentreY))
            );

            if (step.Whole)
            {
                Reach(step.CentreX - r, ref left, ref right);
                Reach(step.CentreX + r, ref left, ref right);
                return;
            }

            double first = Math.Atan2(fromY - step.CentreY, fromX - step.CentreX);
            double sweep = Sweep(fromX, fromY, step);

            for (int quarter = 0; quarter < 4; quarter += 2)
            {
                double along = step.CounterClockwise
                    ? Turn((quarter * Math.PI / 2.0) - first)
                    : Turn(first - (quarter * Math.PI / 2.0));

                if (along <= Math.Abs(sweep) + Exact.EqualAngleRad || along >= (2.0 * Math.PI) - Exact.EqualAngleRad)
                {
                    Reach(step.CentreX + (quarter == 0 ? r : -r), ref left, ref right);
                }
            }
        }

        /// <summary>An angle brought into 0 up to a whole turn.</summary>
        private static double Turn(double angle)
        {
            double turn = angle % (2.0 * Math.PI);
            return turn < 0.0 ? turn + (2.0 * Math.PI) : turn;
        }

        /// <summary>How far an arc turns from where the pen stands, in radians, positive counter-clockwise; between 0 and a whole turn, never 0.</summary>
        private static double Sweep(int fromX, int fromY, Step step)
        {
            double a0 = Math.Atan2(fromY - step.CentreY, fromX - step.CentreX);
            double a1 = Math.Atan2(step.Y - step.CentreY, step.X - step.CentreX);
            double turn = step.CounterClockwise ? Turn(a1 - a0) : Turn(a0 - a1);

            if (turn < Exact.EqualAngleRad)
            {
                turn = 2.0 * Math.PI;
            }

            return step.CounterClockwise ? turn : -turn;
        }

        private static Letter[] Letters()
        {
            var letters = new SortedDictionary<int, Letter>();

            foreach (Row row in Rows)
            {
                for (int tone = 0; tone < row.Codes.Length; tone++)
                {
                    int code = row.Codes[tone];

                    if (code < 0x80)
                    {
                        continue; // The plain ASCII letter is not Vietnamese.
                    }

                    string mark = tone == 0 ? string.Empty : char.ConvertFromUtf32(ToneMarks[tone]);
                    letters.Add(code, new Letter(code, row.BaseCode, row.Modifier, tone, true, row.Spelled + mark));
                    int lower = Lower(code);
                    letters.Add(
                        lower,
                        new Letter(
                            lower,
                            Lower(row.BaseCode),
                            row.Modifier,
                            tone,
                            false,
                            row.Spelled.ToLowerInvariant() + mark
                        )
                    );
                }
            }

            return letters.Values.ToArray();
        }

        /// <summary>The lowercase of a capital of the table: 0x20 on for ASCII and Latin-1, one on for the rest.</summary>
        private static int Lower(int capital) => capital < 0x100 ? capital + 0x20 : capital + 1;

        private static void Add(SortedDictionary<int, Glyph> glyphs, Glyph glyph)
        {
            if (glyphs.ContainsKey(glyph.Code))
            {
                throw new InvalidDataException(
                    "glyph " + Hex(glyph.Code) + ": the code is in the data and is also made of a base and marks"
                );
            }

            glyphs.Add(glyph.Code, glyph);
        }

        /// <summary>A letter of a base and marks: the base's glyph and the marks placed as the design says, its advance the base's.</summary>
        private static Glyph Compose(Skeleton skeleton, Letter letter)
        {
            Glyph glyph = skeleton.Glyphs.FirstOrDefault(g => g.Code == letter.BaseCode);
            string who = "glyph " + Hex(letter.Code);

            if (glyph == null)
            {
                throw new InvalidDataException(who + ": its base " + Hex(letter.BaseCode) + " is not in the data");
            }

            int y0 = letter.Capital ? CapitalMarks : SmallMarks;
            bool tall = letter.Modifier != null;

            // The dot of an i goes where a mark goes above it.
            bool above = letter.Tone >= 1 && letter.Tone <= 4;
            var strokes = new List<Stroke>(glyph.Strokes.Where(s => !(s.Dot && glyph.Code == 0x69 && above)));

            if (tall)
            {
                strokes.AddRange(Place(skeleton, letter.Modifier, glyph.Top, y0, letter.Code));
            }

            if (above)
            {
                bool tilde = letter.Tone == 4;
                bool beside = tall && letter.Modifier == "circumflex" && !tilde;
                double x = beside ? glyph.Top + BesideX : glyph.Top;
                int y = beside
                    ? y0 + BesideY
                    : (tall && (tilde || letter.Modifier == "breve") ? y0 + StackedMarks : y0);
                strokes.AddRange(Place(skeleton, ToneNames[letter.Tone], x, y, letter.Code));
            }

            if (letter.Tone == 5)
            {
                strokes.AddRange(Place(skeleton, "dotbelow", glyph.Bottom, DotBelowY, letter.Code));
            }

            return new Glyph(letter.Code, glyph.Width, glyph.Advance, glyph.Top, glyph.Bottom, strokes);
        }

        /// <summary>A mark's strokes put with its anchor at x, y; an x between two grid points, the E's, goes to the next one to the right.</summary>
        private static Stroke[] Place(Skeleton skeleton, string name, double x, int y, int code)
        {
            if (!skeleton.Marks.TryGetValue(name, out IReadOnlyList<Stroke> mark))
            {
                throw new InvalidDataException("glyph " + Hex(code) + ": the mark " + name + " is not in the data");
            }

            int at = (int)Math.Round(x, MidpointRounding.AwayFromZero);
            return mark.Select(s => s.Moved(at, y)).ToArray();
        }

        private static void Word(List<byte> bytes, int value)
        {
            bytes.Add((byte)(value & 0xFF));
            bytes.Add((byte)((value >> 8) & 0xFF));
        }

        /// <summary>
        /// The record of a glyph: an empty name, then for each stroke the pen lifted and moved to its first point and put
        /// down along its steps, and last the pen lifted and moved to the advance on the baseline.
        /// </summary>
        private static byte[] Record(Glyph glyph)
        {
            string who = "glyph " + Hex(glyph.Code);
            var codes = new List<byte> { 0 };
            int x = 0;
            int y = 0;

            foreach (Stroke stroke in glyph.Strokes)
            {
                codes.Add(2);
                Move(codes, ref x, ref y, stroke.X, stroke.Y, false, who);
                codes.Add(1);

                foreach (Step step in stroke.Steps)
                {
                    if (!step.IsArc)
                    {
                        Move(codes, ref x, ref y, step.X, step.Y, true, who);
                    }
                    else if (step.Whole)
                    {
                        Circle(codes, step, who);
                    }
                    else
                    {
                        Arc(codes, ref x, ref y, step, who);
                    }
                }
            }

            codes.Add(2);
            Move(codes, ref x, ref y, glyph.Advance, 0, false, who);
            codes.Add(0);

            if (codes.Count >= MostRecord)
            {
                throw new InvalidDataException(
                    who + ": the record is " + codes.Count + " bytes, over the margin of " + MostRecord
                );
            }

            return codes.ToArray();
        }

        /// <summary>
        /// A move by signed bytes (code 8), cut into equal parts where it is longer than 127. A move that inks is cut only
        /// into parts on the grid, so that it stays one straight line.
        /// </summary>
        private static void Move(List<byte> codes, ref int x, ref int y, int toX, int toY, bool inked, string who)
        {
            int dx = toX - x;
            int dy = toY - y;

            if (dx == 0 && dy == 0)
            {
                return;
            }

            int most = Math.Max(Math.Abs(dx), Math.Abs(dy));
            int parts = (most + MostByte - 1) / MostByte;

            while (inked && parts <= most && (dx % parts != 0 || dy % parts != 0))
            {
                parts++;
            }

            if (parts > most)
            {
                throw new InvalidDataException(
                    who
                        + ": the line by "
                        + dx
                        + ","
                        + dy
                        + " is over 127 and cannot be cut into equal parts on the grid"
                );
            }

            int atX = 0;
            int atY = 0;

            for (int k = 1; k <= parts; k++)
            {
                int nextX = (int)Math.Round(dx * (double)k / parts, MidpointRounding.AwayFromZero);
                int nextY = (int)Math.Round(dy * (double)k / parts, MidpointRounding.AwayFromZero);
                codes.Add(8);
                codes.Add((byte)(sbyte)(nextX - atX));
                codes.Add((byte)(sbyte)(nextY - atY));
                atX = nextX;
                atY = nextY;
            }

            x = toX;
            y = toY;
        }

        /// <summary>A whole circle: code 10 of a count of 0, from the angle 0 where the pen stands.</summary>
        private static void Circle(List<byte> codes, Step step, string who)
        {
            int radius = step.X - step.CentreX;

            if (radius < 1 || radius > MostByte)
            {
                throw new InvalidDataException(who + ": a circle of radius " + radius + " is out of 1 to 127");
            }

            codes.Add(10);
            codes.Add((byte)radius);
            codes.Add(0);
        }

        /// <summary>An arc, by quarter turns as code 10 where it starts on an axis of its centre and ends on one, else by its bulge.</summary>
        private static void Arc(List<byte> codes, ref int x, ref int y, Step step, string who)
        {
            int ax = x - step.CentreX;
            int ay = y - step.CentreY;
            int bx = step.X - step.CentreX;
            int by = step.Y - step.CentreY;

            if ((ax == 0 || ay == 0) && (bx == 0 || by == 0))
            {
                int radius = Math.Abs(ax) + Math.Abs(ay);

                if (radius < 1 || radius > MostByte || radius != Math.Abs(bx) + Math.Abs(by))
                {
                    throw new InvalidDataException(
                        who + ": an arc of quarter turns of radius " + radius + " is not of one radius of 1 to 127"
                    );
                }

                int from = Quarter(ax, ay);
                int quarters = step.CounterClockwise
                    ? (Quarter(bx, by) - from + 4) % 4
                    : (from - Quarter(bx, by) + 4) % 4;
                if (quarters == 0)
                {
                    throw new InvalidDataException(
                        who + ": an arc starts and ends at one point; a whole circle is its own step"
                    );
                }

                codes.Add(10);
                codes.Add((byte)radius);
                codes.Add((byte)((step.CounterClockwise ? 0 : 0x80) | ((2 * from) << 4) | (2 * quarters)));
                x = step.X;
                y = step.Y;
                return;
            }

            Bulges(codes, ref x, ref y, step, who);
        }

        private static int Quarter(int x, int y) => y == 0 ? (x > 0 ? 0 : 2) : (y > 0 ? 1 : 3);

        /// <summary>
        /// An arc by bulges (code 12), cut into the fewest equal parts, up to 8, each of at most a half circle and a chord of
        /// signed bytes, whose ends are the grid points nearest the circle: the arc is drawn within 0.5 of a unit of it and
        /// the pen lands on the end given.
        /// </summary>
        private static void Bulges(List<byte> codes, ref int x, ref int y, Step step, string who)
        {
            double radius = Math.Sqrt(
                ((x - step.CentreX) * (x - step.CentreX)) + ((y - step.CentreY) * (y - step.CentreY))
            );
            double first = Math.Atan2(y - step.CentreY, x - step.CentreX);
            double sweep = Sweep(x, y, step);
            int fewest = Math.Max(1, (int)Math.Ceiling((Math.Abs(sweep) / Math.PI) - Exact.EqualAngleRad));

            for (int parts = fewest; parts <= MostParts; parts++)
            {
                var points = new List<int[]> { new[] { x, y } };

                for (int k = 1; k < parts; k++)
                {
                    double angle = first + (sweep * k / parts);
                    points.Add(
                        Nearest(
                            step.CentreX + (radius * Math.Cos(angle)),
                            step.CentreY + (radius * Math.Sin(angle)),
                            step,
                            radius
                        )
                    );
                }

                points.Add(new[] { step.X, step.Y });
                byte[] written = Chords(points, step, radius);

                if (written != null)
                {
                    codes.AddRange(written);
                    x = step.X;
                    y = step.Y;
                    return;
                }
            }

            throw new InvalidDataException(
                who
                    + ": an arc about "
                    + step.CentreX
                    + ","
                    + step.CentreY
                    + " to "
                    + step.X
                    + ","
                    + step.Y
                    + " cannot be cut into 8 parts that keep within 127 and 0.5 of a unit"
            );
        }

        /// <summary>Of the four grid points about a point, the one whose distance from the centre is nearest the radius; the first of those in the order floor, ceiling on a tie.</summary>
        private static int[] Nearest(double x, double y, Step step, double radius)
        {
            int[] best = null;
            double least = double.MaxValue;

            foreach (int px in new[] { (int)Math.Floor(x), (int)Math.Ceiling(x) })
            {
                foreach (int py in new[] { (int)Math.Floor(y), (int)Math.Ceiling(y) })
                {
                    double off = Math.Abs(
                        Math.Sqrt(
                            ((px - step.CentreX) * (px - step.CentreX)) + ((py - step.CentreY) * (py - step.CentreY))
                        ) - radius
                    );

                    if (off < least - Exact.EqualPoint)
                    {
                        least = off;
                        best = new[] { px, py };
                    }
                }
            }

            return best;
        }

        /// <summary>The code 12 bytes of the parts between points, or null where a part is over 127 or strays from the circle.</summary>
        private static byte[] Chords(List<int[]> points, Step step, double radius)
        {
            var bytes = new List<byte>();

            for (int i = 1; i < points.Count; i++)
            {
                int dx = points[i][0] - points[i - 1][0];
                int dy = points[i][1] - points[i - 1][1];

                if (Math.Abs(dx) > MostByte || Math.Abs(dy) > MostByte)
                {
                    return null;
                }

                double chord = Math.Sqrt((dx * dx) + (dy * dy));
                double turn = 2.0 * Math.Asin(Math.Min(1.0, chord / (2.0 * radius)));
                int bulge = (int)Math.Round(MostByte * Math.Tan(turn / 4.0), MidpointRounding.AwayFromZero);
                bulge = step.CounterClockwise ? bulge : -bulge;

                if (Stray(points[i - 1][0], points[i - 1][1], dx, dy, bulge, step, radius) > MostArcError)
                {
                    return null;
                }

                bytes.Add(12);
                bytes.Add((byte)(sbyte)dx);
                bytes.Add((byte)(sbyte)dy);
                bytes.Add((byte)(sbyte)bulge);
            }

            return bytes.ToArray();
        }

        /// <summary>How far an arc drawn by its bulge strays from the circle it stands for, at a quarter, half and three quarters of its way, in font units.</summary>
        private static double Stray(int fromX, int fromY, int dx, int dy, int bulge, Step step, double radius)
        {
            double chord = Math.Sqrt((dx * dx) + (dy * dy));
            double sweep = 4.0 * Math.Atan(bulge / (double)MostByte);
            double worst = 0.0;

            for (int k = 1; k <= 3; k++)
            {
                double along = k / 4.0;
                double px;
                double py;

                if (bulge == 0)
                {
                    px = fromX + (dx * along);
                    py = fromY + (dy * along);
                }
                else
                {
                    // As the reader and AutoCAD draw it: the centre from the chord's middle along its left normal.
                    double r = chord / (2.0 * Math.Sin(Math.Abs(sweep) / 2.0));
                    double h = r * Math.Cos(sweep / 2.0) * (sweep > 0.0 ? 1.0 : -1.0);
                    double cx = (dx / 2.0) - (dy / chord * h);
                    double cy = (dy / 2.0) + (dx / chord * h);
                    double angle = Math.Atan2(-cy, -cx) + (sweep * along);
                    px = fromX + cx + (r * Math.Cos(angle));
                    py = fromY + cy + (r * Math.Sin(angle));
                }

                double off =
                    Math.Sqrt(((px - step.CentreX) * (px - step.CentreX)) + ((py - step.CentreY) * (py - step.CentreY)))
                    - radius;
                worst = Math.Max(worst, Math.Abs(off));
            }

            return worst;
        }
    }
}
