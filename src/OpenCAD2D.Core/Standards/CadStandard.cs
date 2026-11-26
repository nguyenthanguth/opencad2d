using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Text.RegularExpressions;
using GeometryHelper.Dxf;
using OpenCAD2D.Remote;

namespace OpenCAD2D
{
    /// <summary>
    /// A drawing standard a company writes once and checks its drawings against (<see cref="CadStandards.Check"/>): the layers a
    /// drawing may use and how each looks, the text and dimension styles with their fonts, the heights words may have on paper,
    /// the least gap between words and lines, how a chain of dimensions lines up, and whether an object may sit on layer 0 or
    /// take a colour or linetype of its own. It is read from a JSON file of a versioned schema (<see cref="Schema"/>,
    /// <c>docs/schemas/opencad2d-standard-v1.schema.json</c>) in which every member but the two that name the schema is optional,
    /// and a rule whose member is absent is not run. A member the schema does not know is refused, so that a misspelt rule is
    /// not skipped without a word.
    /// </summary>
    public sealed class CadStandard
    {
        /// <summary>The version of the schema this reads and <see cref="Schema"/> describes.</summary>
        public const int Version = 1;

        private static string _schema;

        internal CadStandard() { }

        /// <summary>
        /// Gets the JSON Schema (draft 2020-12) of a standard file: the file <c>docs/schemas/opencad2d-standard-v1.schema.json</c>
        /// of the repository, built into this assembly.
        /// </summary>
        public static string Schema
        {
            get
            {
                if (_schema == null)
                {
                    using (
                        Stream stream = typeof(CadStandard)
                            .GetTypeInfo()
                            .Assembly.GetManifestResourceStream("OpenCAD2D.opencad2d-standard-v1.schema.json")
                    )
                    using (var reader = new StreamReader(stream))
                    {
                        _schema = reader.ReadToEnd();
                    }
                }

                return _schema;
            }
        }

        /// <summary>Gets the name the standard gives itself, or an empty text.</summary>
        public string Name { get; private set; } = string.Empty;

        internal bool HasLayers => Allowed != null;

        internal List<LayerEntry> Allowed;
        internal bool AllowOthers;
        internal bool? AllowLayer0;
        internal bool? ByLayerColor;
        internal bool? ByLayerLinetype;
        internal bool? ByLayerLineweight;
        internal List<StyleEntry> TextStyles;
        internal List<StyleEntry> DimStyles;
        internal double[] Heights;
        internal double HeightTolerance = 0.01;
        internal double WordLine = -1.0;
        internal double WordWord = -1.0;
        internal bool Chains;
        internal double ChainTolerance = 0.5;
        internal double ChainReach = 10.0;
        internal readonly double[] Weights = Enumerable.Repeat(1.0, CadStandards.RuleIds.Count).ToArray();

        /// <summary>One layer a standard allows: a name pattern and what each layer of that name looks like.</summary>
        internal sealed class LayerEntry
        {
            public string Pattern;
            public Regex Match;
            public DxfColor Color;
            public string Linetype;
            public double? Lineweight;
        }

        /// <summary>One text or dimension style a standard allows: a name pattern and the font it uses.</summary>
        internal sealed class StyleEntry
        {
            public string Pattern;
            public Regex Match;
            public string Font;
        }

        /// <summary>
        /// Reads a standard from its JSON text.
        /// </summary>
        /// <param name="json">The text of the file.</param>
        /// <returns>The standard.</returns>
        /// <exception cref="ArgumentNullException">Thrown when the text is null.</exception>
        /// <exception cref="ArgumentException">Thrown when the text is not JSON, is not a standard of this version, has a member the
        /// schema does not know, or holds a value out of range: the message names the member.</exception>
        public static CadStandard Parse(string json)
        {
            if (json == null)
            {
                throw new ArgumentNullException(nameof(json));
            }

            object parsed;

            try
            {
                parsed = RemoteJson.Parse(json);
            }
            catch (FormatException e)
            {
                throw new ArgumentException("The standard is not JSON: " + e.Message, nameof(json));
            }

            if (!(parsed is JsonObject root))
            {
                throw new ArgumentException("The standard is a JSON object.", nameof(json));
            }

            var standard = new CadStandard();

            try
            {
                standard.Read(root);
            }
            catch (InvalidCastException e)
            {
                throw new ArgumentException("The standard has a member of the wrong kind: " + e.Message, nameof(json));
            }

            return standard;
        }

        /// <summary>Gets whether a rule is run for this standard.</summary>
        /// <param name="rule">The id of the rule (<see cref="CadStandards.RuleIds"/>).</param>
        /// <returns>Whether the standard asks for it.</returns>
        /// <exception cref="ArgumentException">Thrown when there is no rule of the id.</exception>
        public bool Runs(string rule)
        {
            switch (CadStandards.IndexOf(rule))
            {
                case 0:
                    return HasLayers && !AllowOthers;
                case 1:
                    return HasLayers && Allowed.Any(e => e.Color != null || e.Linetype != null || e.Lineweight != null);
                case 2:
                    return AllowLayer0 == false;
                case 3:
                    return ByLayerColor == true;
                case 4:
                    return ByLayerLinetype == true;
                case 5:
                    return ByLayerLineweight == true;
                case 6:
                    return TextStyles != null;
                case 7:
                    return TextStyles != null && TextStyles.Any(e => e.Font != null);
                case 8:
                    return DimStyles != null;
                case 9:
                    return DimStyles != null && DimStyles.Any(e => e.Font != null);
                case 10:
                    return Heights != null;
                case 11:
                    return WordLine >= 0.0;
                case 12:
                    return WordWord >= 0.0;
                default:
                    return Chains;
            }
        }

        /// <summary>Gets the weight of a rule in the total score: 1 unless the standard says otherwise.</summary>
        /// <param name="rule">The id of the rule.</param>
        /// <returns>The weight, nought or more.</returns>
        /// <exception cref="ArgumentException">Thrown when there is no rule of the id.</exception>
        public double WeightOf(string rule) => Weights[CadStandards.IndexOf(rule)];

        private void Read(JsonObject root)
        {
            Only(
                root,
                "standard",
                "$schema",
                "schema",
                "version",
                "name",
                "layers",
                "byLayer",
                "textStyles",
                "dimStyles",
                "textHeights",
                "gaps",
                "dimensionChains",
                "weights"
            );

            if (root.GetString("schema") != "opencad2d.standard")
            {
                throw Bad("standard.schema", "has to be \"opencad2d.standard\"");
            }

            if (!(root.Get("version") is long version) || version != Version)
            {
                throw Bad("standard.version", "has to be " + Version.ToString(CultureInfo.InvariantCulture));
            }

            Name = root.GetString("name") ?? string.Empty;

            if (root.Get("layers") is JsonObject layers)
            {
                ReadLayers(layers);
            }

            if (root.Get("byLayer") is JsonObject byLayer)
            {
                Only(byLayer, "standard.byLayer", "color", "linetype", "lineweight");
                ByLayerColor = Flag(byLayer, "standard.byLayer.color");
                ByLayerLinetype = Flag(byLayer, "standard.byLayer.linetype");
                ByLayerLineweight = Flag(byLayer, "standard.byLayer.lineweight");
            }

            TextStyles = Styles(root, "textStyles");
            DimStyles = Styles(root, "dimStyles");

            if (root.Get("textHeights") is JsonObject heights)
            {
                Only(heights, "standard.textHeights", "paper", "tolerance");
                JsonArray paper = heights.GetArray("paper");

                if (paper.Count == 0)
                {
                    throw Bad("standard.textHeights.paper", "needs at least one height in millimetres");
                }

                Heights = paper
                    .Select(
                        (h, i) =>
                            h is long || h is double
                                ? Convert.ToDouble(h, CultureInfo.InvariantCulture)
                                : throw Bad("standard.textHeights.paper[" + i + "]", "has to be a number")
                    )
                    .OrderBy(h => h)
                    .ToArray();

                if (Heights.Any(h => !(h > 0.0)))
                {
                    throw Bad("standard.textHeights.paper", "holds heights above nought");
                }

                HeightTolerance = Number(heights, "standard.textHeights.tolerance", false, 0.01);
            }

            if (root.Get("gaps") is JsonObject gaps)
            {
                Only(gaps, "standard.gaps", "wordLine", "wordWord");
                WordLine = gaps.Get("wordLine") == null ? -1.0 : Number(gaps, "standard.gaps.wordLine", true, 0.0);
                WordWord = gaps.Get("wordWord") == null ? -1.0 : Number(gaps, "standard.gaps.wordWord", true, 0.0);
            }

            if (root.Get("dimensionChains") is JsonObject chains)
            {
                Only(chains, "standard.dimensionChains", "tolerance", "reach");
                Chains = true;
                ChainTolerance = Number(chains, "standard.dimensionChains.tolerance", false, 0.5);
                ChainReach = Number(chains, "standard.dimensionChains.reach", false, 10.0);
            }

            if (root.Get("weights") is JsonObject weights)
            {
                foreach (KeyValuePair<string, object> weight in weights.Members)
                {
                    int rule = CadStandards.RuleIds.ToList().IndexOf(weight.Key);

                    if (rule < 0)
                    {
                        throw Bad(
                            "standard.weights." + weight.Key,
                            "is no rule (" + string.Join(", ", CadStandards.RuleIds) + ")"
                        );
                    }

                    Weights[rule] = Number(weights, "standard.weights." + weight.Key, false, 1.0);
                }
            }
        }

        private void ReadLayers(JsonObject layers)
        {
            Only(layers, "standard.layers", "allowed", "allowOthers", "allowLayer0");
            AllowOthers = layers.GetBool("allowOthers", false);
            AllowLayer0 = layers.Get("allowLayer0") == null ? (bool?)null : Flag(layers, "standard.layers.allowLayer0");

            if (!(layers.Get("allowed") is JsonArray allowed))
            {
                return;
            }

            Allowed = new List<LayerEntry>();

            for (int i = 0; i < allowed.Count; i++)
            {
                string where = "standard.layers.allowed[" + i.ToString(CultureInfo.InvariantCulture) + "]";

                if (!(allowed[i] is JsonObject entry))
                {
                    throw Bad(where, "has to be an object");
                }

                Only(entry, where, "name", "color", "linetype", "lineweight");
                string pattern = Pattern(entry, where);
                Allowed.Add(
                    new LayerEntry
                    {
                        Pattern = pattern,
                        Match = CadWildcard.ToRegex(pattern, true, true),
                        Color = ColorOf(entry.Get("color"), where + ".color"),
                        Linetype = entry.GetString("linetype"),
                        Lineweight =
                            entry.Get("lineweight") == null
                                ? (double?)null
                                : Number(entry, where + ".lineweight", false, 0.0),
                    }
                );
            }
        }

        private static List<StyleEntry> Styles(JsonObject root, string member)
        {
            if (!(root.Get(member) is JsonArray styles))
            {
                return null;
            }

            var list = new List<StyleEntry>();

            for (int i = 0; i < styles.Count; i++)
            {
                string where = "standard." + member + "[" + i.ToString(CultureInfo.InvariantCulture) + "]";

                if (!(styles[i] is JsonObject entry))
                {
                    throw Bad(where, "has to be an object");
                }

                Only(entry, where, "name", "font");
                string pattern = Pattern(entry, where);
                list.Add(
                    new StyleEntry
                    {
                        Pattern = pattern,
                        Match = CadWildcard.ToRegex(pattern, true, true),
                        Font = entry.GetString("font"),
                    }
                );
            }

            return list;
        }

        private static string Pattern(JsonObject entry, string where)
        {
            string pattern = entry.GetString("name");

            if (string.IsNullOrEmpty(pattern))
            {
                throw Bad(where + ".name", "is a name or a wild-card pattern");
            }

            return pattern;
        }

        private static DxfColor ColorOf(object value, string where)
        {
            if (value == null)
            {
                return null;
            }

            if (value is long aci && aci >= 1 && aci <= 255)
            {
                return DxfColor.FromColorIndex(DxfColorMethod.ByAci, (short)aci);
            }

            if (value is string text && Regex.IsMatch(text, "^#[0-9A-Fa-f]{6}$"))
            {
                int rgb = int.Parse(text.Substring(1), NumberStyles.HexNumber, CultureInfo.InvariantCulture);
                return DxfColor.FromRgb((byte)(rgb >> 16), (byte)(rgb >> 8), (byte)rgb);
            }

            throw Bad(where, "is an AutoCAD colour number from 1 to 255 or a text \"#RRGGBB\"");
        }

        private static bool Flag(JsonObject owner, string where)
        {
            object value = owner.Get(where.Substring(where.LastIndexOf('.') + 1));

            if (value == null)
            {
                return false;
            }

            return value is bool flag ? flag : throw Bad(where, "has to be true or false");
        }

        private static double Number(JsonObject owner, string where, bool required, double fallback)
        {
            object value = owner.Get(where.Substring(where.LastIndexOf('.') + 1));

            if (value == null)
            {
                return required ? throw Bad(where, "is a number") : fallback;
            }

            if (!(value is long || value is double))
            {
                throw Bad(where, "has to be a number");
            }

            double number = Convert.ToDouble(value, CultureInfo.InvariantCulture);

            if (double.IsNaN(number) || double.IsInfinity(number) || number < 0.0)
            {
                throw Bad(where, "has to be a number of nought or more");
            }

            return number;
        }

        private static void Only(JsonObject owner, string where, params string[] known)
        {
            foreach (KeyValuePair<string, object> member in owner.Members)
            {
                if (Array.IndexOf(known, member.Key) < 0)
                {
                    throw Bad(
                        where + "." + member.Key,
                        "is not a member of a standard (" + string.Join(", ", known) + ")"
                    );
                }
            }
        }

        private static ArgumentException Bad(string where, string why) =>
            new ArgumentException("The standard's " + where + " " + why + ".");
    }
}
