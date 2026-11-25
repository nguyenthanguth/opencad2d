using System;
using System.Collections.Generic;
using System.Globalization;
using GeometryHelper.Geometry;
using OpenCAD2D.Remote;

namespace OpenCAD2D.Plugins
{
    /// <summary>
    /// One command a <c>plugin.json</c> declares, written as a name (<c>"RING"</c>) or as an object that says more:
    /// <c>{ "name": "RING", "alias": "RG", "description": "Draws a ring", "usesSelection": true }</c>. A Python tool declares its
    /// commands here so that the app shows them, and starts the tool's process only when one is used.
    /// </summary>
    public sealed class CadPluginCommandEntry
    {
        internal CadPluginCommandEntry(string name, string alias, string description, bool usesSelection)
        {
            Name = name;
            Alias = alias;
            Description = description;
            UsesSelection = usesSelection;
        }

        /// <summary>Gets the name of the command, in capitals.</summary>
        public string Name { get; }

        /// <summary>Gets the alias typed in place of the name, in capitals; null where none is declared.</summary>
        public string Alias { get; }

        /// <summary>Gets what the command does, in a line; null where none is declared.</summary>
        public string Description { get; }

        /// <summary>Gets whether the command takes the objects selected before it starts, as MOVE does.</summary>
        public bool UsesSelection { get; }
    }

    /// <summary>One tab of the <c>"ribbon"</c> of a <c>plugin.json</c>: <c>{ "id": "tools", "title": "Tools", "panels": [ ... ] }</c>.</summary>
    public sealed class CadPluginRibbonTabEntry
    {
        internal CadPluginRibbonTabEntry(string id, string title, IReadOnlyList<CadPluginRibbonPanelEntry> panels)
        {
            Id = id;
            Title = title;
            Panels = panels;
        }

        /// <summary>Gets the tab's identity, unique among the loaded plugins; the title where none is declared.</summary>
        public string Id { get; }

        /// <summary>Gets the title on the tab strip.</summary>
        public string Title { get; }

        /// <summary>Gets the panels, left to right.</summary>
        public IReadOnlyList<CadPluginRibbonPanelEntry> Panels { get; }
    }

    /// <summary>One panel of a tab of the <c>"ribbon"</c> of a <c>plugin.json</c>: <c>{ "title": "Draw", "buttons": [ ... ] }</c>.</summary>
    public sealed class CadPluginRibbonPanelEntry
    {
        internal CadPluginRibbonPanelEntry(string title, IReadOnlyList<CadPluginRibbonButtonEntry> buttons)
        {
            Title = title;
            Buttons = buttons;
        }

        /// <summary>Gets the title under the panel.</summary>
        public string Title { get; }

        /// <summary>Gets the buttons, left to right.</summary>
        public IReadOnlyList<CadPluginRibbonButtonEntry> Buttons { get; }
    }

    /// <summary>
    /// One button of a panel of the <c>"ribbon"</c> of a <c>plugin.json</c>:
    /// <c>{ "text": "Ring", "command": "RING", "icon": "ring.png", "size": "large", "tooltip": "Draws a ring" }</c>. A button starts a
    /// command and does nothing else.
    /// </summary>
    public sealed class CadPluginRibbonButtonEntry
    {
        internal CadPluginRibbonButtonEntry(string text, string command, string icon, bool isLarge, string tooltip)
        {
            Text = text;
            Command = command;
            Icon = icon;
            IsLarge = isLarge;
            ToolTip = tooltip;
        }

        /// <summary>Gets the text of the button.</summary>
        public string Text { get; }

        /// <summary>Gets the command the button starts.</summary>
        public string Command { get; }

        /// <summary>Gets the path of the button's icon, relative to the manifest; null where none is declared.</summary>
        public string Icon { get; }

        /// <summary>Gets whether the button is drawn large (<c>"size": "large"</c>, the icon 32 pixels over the text); false for the standard size.</summary>
        public bool IsLarge { get; }

        /// <summary>Gets the tooltip of the button; null where none is declared.</summary>
        public string ToolTip { get; }
    }

    /// <summary>
    /// One object snap a Python tool declares in the <c>"snaps"</c> of its <c>plugin.json</c>:
    /// <c>{ "keyword": "CTOP", "name": "Circle top", "entityTypes": ["CIRCLE"], "glyph": "quadrant", "onByDefault": true }</c>.
    /// The glyph is the name of a built-in marker (one of <see cref="CadSnapKind"/>, in any case) or strokes in pixels about
    /// the point, Y down: <c>[ { "points": [[-4.5, 4.5], [4.5, 4.5], [0, -4.5]], "closed": true, "dotted": false, "filled": false } ]</c>.
    /// The tool's code is bound to the keyword when the tool starts; the app lists the snap before that.
    /// </summary>
    public sealed class CadPluginSnapEntry
    {
        private CadPluginSnapEntry(
            string keyword,
            string name,
            IReadOnlyList<string> entityTypes,
            IReadOnlyList<CadMarkerStroke> glyph,
            bool isOnByDefault
        )
        {
            Keyword = keyword;
            Name = name;
            EntityTypes = entityTypes;
            Glyph = glyph;
            IsOnByDefault = isOnByDefault;
        }

        /// <summary>Gets the keyword typed as an override, in capitals: "CTOP".</summary>
        public string Keyword { get; }

        /// <summary>Gets the name the menus, Drafting Settings and the snap tip show: "Circle top".</summary>
        public string Name { get; }

        /// <summary>Gets the DXF names of the entity types the snap applies to, in capitals, as <c>DxfEntity.DxfName</c> gives them: <c>["CIRCLE", "ARC"]</c>; empty for every entity.</summary>
        public IReadOnlyList<string> EntityTypes { get; }

        /// <summary>Gets the marker as strokes in pixels about the snap point, Y down; null for the default glyph, a square with a dot.</summary>
        public IReadOnlyList<CadMarkerStroke> Glyph { get; }

        /// <summary>Gets whether the snap starts on for a user who has never chosen it.</summary>
        public bool IsOnByDefault { get; }

        /// <summary>The snap an item of <c>"snaps"</c> declares; null for one with no keyword or no name.</summary>
        /// <exception cref="FormatException">Thrown when the glyph is a name no marker has, or a stroke that is malformed.</exception>
        internal static CadPluginSnapEntry From(object item)
        {
            if (
                !(item is JsonObject declared)
                || string.IsNullOrWhiteSpace(declared.GetString("keyword"))
                || string.IsNullOrWhiteSpace(declared.GetString("name"))
            )
            {
                return null;
            }

            string keyword = declared.GetString("keyword").Trim().ToUpperInvariant();
            var types = new List<string>();

            foreach (object type in declared.GetArray("entityTypes"))
            {
                if (type is string text && !string.IsNullOrWhiteSpace(text))
                {
                    types.Add(text.Trim().ToUpperInvariant());
                }
            }

            return new CadPluginSnapEntry(
                keyword,
                declared.GetString("name").Trim(),
                types,
                GlyphOf(keyword, declared.Get("glyph")),
                declared.GetBool("onByDefault")
            );
        }

        private static IReadOnlyList<CadMarkerStroke> GlyphOf(string keyword, object glyph)
        {
            string where = "The snap " + keyword + ": ";

            if (glyph == null)
            {
                return null;
            }

            if (glyph is string name)
            {
                var names = new List<string>();

                foreach (CadSnapKind kind in Enum.GetValues(typeof(CadSnapKind)))
                {
                    IReadOnlyList<CadMarkerStroke> strokes = CadSnapMarker.Strokes(kind);

                    if (strokes.Count == 0)
                    {
                        continue;
                    }

                    if (string.Equals(kind.ToString(), name.Trim(), StringComparison.OrdinalIgnoreCase))
                    {
                        return strokes;
                    }

                    names.Add(kind.ToString().ToLowerInvariant());
                }

                throw new FormatException(
                    where + "the glyph \"" + name + "\" is no marker; it is one of " + string.Join(", ", names) + "."
                );
            }

            if (!(glyph is JsonArray declared) || declared.Count == 0)
            {
                throw new FormatException(
                    where + "the glyph is a marker's name or a list of strokes, each { \"points\": [[x, y], ...] }."
                );
            }

            var result = new List<CadMarkerStroke>();

            for (int i = 0; i < declared.Count; i++)
            {
                result.Add(StrokeOf(where + "stroke " + (i + 1) + " of the glyph ", declared[i]));
            }

            return result;
        }

        private static CadMarkerStroke StrokeOf(string where, object item)
        {
            if (!(item is JsonObject stroke) || !(stroke.Get("points") is JsonArray declared) || declared.Count < 2)
            {
                throw new FormatException(where + "needs \"points\", at least two pairs [x, y] in pixels.");
            }

            var points = new List<GeoPoint2>();

            foreach (object pair in declared)
            {
                if (
                    !(pair is JsonArray xy)
                    || xy.Count != 2
                    || !CadPluginSettingEntry.TryNumber(xy[0], out double x)
                    || !CadPluginSettingEntry.TryNumber(xy[1], out double y)
                )
                {
                    throw new FormatException(where + "has a point that is not a pair of finite numbers [x, y].");
                }

                points.Add(new GeoPoint2(x, y));
            }

            return new CadMarkerStroke(
                points,
                stroke.GetBool("closed"),
                stroke.GetBool("dotted"),
                stroke.GetBool("filled")
            );
        }
    }

    /// <summary>
    /// One setting a Python tool declares in the <c>"settings"</c> of its <c>plugin.json</c>, as a C# plugin declares one with
    /// <see cref="CadSettingAttribute"/>:
    /// <c>{ "name": "textHeight", "label": "Text height", "kind": "number", "default": 2.5, "minimum": 0.1, "maximum": 1000, "description": "How high the numbers are written." }</c>.
    /// The kinds are <c>number</c>, <c>integer</c>, <c>boolean</c>, <c>text</c> and <c>choice</c> (with <c>"choices": ["Left", "Right"]</c>);
    /// the name is an identifier, <c>[A-Za-z_][A-Za-z0-9_]*</c>, once in the file; the label is the name where not given; the
    /// default is needed and must be of the kind, in range and, for a choice, one of the choices.
    /// </summary>
    public sealed class CadPluginSettingEntry
    {
        private CadPluginSettingEntry(
            string name,
            string label,
            string description,
            CadSettingKind kind,
            object defaultValue,
            double minimum,
            double maximum,
            IReadOnlyList<string> choices
        )
        {
            Name = name;
            Label = label;
            Description = description;
            Kind = kind;
            Default = defaultValue;
            Minimum = minimum;
            Maximum = maximum;
            Choices = choices;
        }

        /// <summary>Gets the name the tool reads and writes the setting by: "textHeight".</summary>
        public string Name { get; }

        /// <summary>Gets the label the presenter shows: "Text height"; the name where none is declared.</summary>
        public string Label { get; }

        /// <summary>Gets what the setting does; null for nothing.</summary>
        public string Description { get; }

        /// <summary>Gets the kind of value it holds.</summary>
        public CadSettingKind Kind { get; }

        /// <summary>Gets the default: a <c>double</c>, an <c>int</c>, a <c>bool</c>, a <c>string</c> or, for a choice, the name of the choice as <see cref="Choices"/> spell it.</summary>
        public object Default { get; }

        /// <summary>Gets the least value of a number or an integer, inclusive; minus infinity for none and for any other kind.</summary>
        public double Minimum { get; }

        /// <summary>Gets the greatest value of a number or an integer, inclusive; infinity for none and for any other kind.</summary>
        public double Maximum { get; }

        /// <summary>Gets the names a choice offers, in the order written; empty for any other kind.</summary>
        public IReadOnlyList<string> Choices { get; }

        /// <summary>The setting an item of <c>"settings"</c> declares, the names read so far given to check that this one is new.</summary>
        /// <exception cref="FormatException">Thrown when the item breaks a rule of the setting; the message says which.</exception>
        internal static CadPluginSettingEntry From(object item, int position, ISet<string> seen)
        {
            if (!(item is JsonObject declared))
            {
                throw new FormatException("The setting " + position + " of \"settings\" is not an object.");
            }

            string name = declared.GetString("name")?.Trim();

            if (!IsIdentifier(name))
            {
                throw new FormatException(
                    "The setting "
                        + position
                        + " of \"settings\" needs a name that is an identifier, letters, digits and underscores, not starting with a digit"
                        + (string.IsNullOrEmpty(name) ? "." : ", not \"" + name + "\".")
                );
            }

            if (!seen.Add(name))
            {
                throw new FormatException("The setting " + name + " is declared twice.");
            }

            string where = "The setting " + name + ": ";
            string kindName = declared.GetString("kind")?.Trim();
            CadSettingKind? found = null;

            foreach (CadSettingKind kind in Enum.GetValues(typeof(CadSettingKind)))
            {
                if (string.Equals(kind.ToString(), kindName, StringComparison.OrdinalIgnoreCase))
                {
                    found = kind;
                }
            }

            if (found == null)
            {
                throw new FormatException(
                    where
                        + "the kind is one of number, integer, boolean, text and choice"
                        + (kindName == null ? "." : ", not " + kindName + ".")
                );
            }

            CadSettingKind type = found.Value;
            double minimum = double.NegativeInfinity;
            double maximum = double.PositiveInfinity;

            if (type == CadSettingKind.Number || type == CadSettingKind.Integer)
            {
                minimum = Limit(where, declared, "minimum", minimum);
                maximum = Limit(where, declared, "maximum", maximum);

                if (minimum > maximum)
                {
                    throw new FormatException(
                        where + "the minimum " + Show(minimum) + " is above the maximum " + Show(maximum) + "."
                    );
                }
            }

            var choices = new List<string>();

            if (type == CadSettingKind.Choice)
            {
                foreach (object choice in declared.GetArray("choices"))
                {
                    string text = (choice as string)?.Trim();

                    if (string.IsNullOrEmpty(text))
                    {
                        throw new FormatException(where + "a choice is a name, not empty.");
                    }

                    if (choices.Exists(c => string.Equals(c, text, StringComparison.OrdinalIgnoreCase)))
                    {
                        throw new FormatException(where + "the choice " + text + " is given twice.");
                    }

                    choices.Add(text);
                }

                if (choices.Count == 0)
                {
                    throw new FormatException(where + "a choice needs \"choices\", the names it offers.");
                }
            }

            string label = declared.GetString("label")?.Trim();
            string description = declared.GetString("description")?.Trim();
            return new CadPluginSettingEntry(
                name,
                string.IsNullOrEmpty(label) ? name : label,
                string.IsNullOrEmpty(description) ? null : description,
                type,
                DefaultOf(where, type, declared.Get("default"), minimum, maximum, choices),
                minimum,
                maximum,
                choices
            );
        }

        private static object DefaultOf(
            string where,
            CadSettingKind kind,
            object value,
            double minimum,
            double maximum,
            List<string> choices
        )
        {
            if (value == null)
            {
                throw new FormatException(where + "it needs a \"default\".");
            }

            switch (kind)
            {
                case CadSettingKind.Number:
                case CadSettingKind.Integer:
                    if (!TryNumber(value, out double number))
                    {
                        throw new FormatException(where + "the default is not a number.");
                    }

                    if (
                        kind == CadSettingKind.Integer
                        && (number != Math.Floor(number) || Math.Abs(number) > int.MaxValue)
                    )
                    {
                        throw new FormatException(where + "the default " + Show(number) + " is not a whole number.");
                    }

                    if (number < minimum || number > maximum)
                    {
                        throw new FormatException(
                            where
                                + "the default "
                                + Show(number)
                                + " lies outside "
                                + Show(minimum)
                                + " to "
                                + Show(maximum)
                                + "."
                        );
                    }

                    return kind == CadSettingKind.Integer ? (object)(int)number : number;
                case CadSettingKind.Boolean:
                    return value is bool flag
                        ? flag
                        : throw new FormatException(where + "the default is not true or false.");
                case CadSettingKind.Text:
                    return value is string text ? text : throw new FormatException(where + "the default is not text.");
                default:
                    string wanted = (value as string)?.Trim();

                    foreach (string choice in choices)
                    {
                        if (string.Equals(choice, wanted, StringComparison.OrdinalIgnoreCase))
                        {
                            return choice;
                        }
                    }

                    throw new FormatException(
                        where + "the default is none of the choices " + string.Join(", ", choices) + "."
                    );
            }
        }

        private static double Limit(string where, JsonObject declared, string member, double none)
        {
            object value = declared.Get(member);

            if (value == null)
            {
                return none;
            }

            return TryNumber(value, out double number)
                ? number
                : throw new FormatException(where + "the " + member + " is not a number.");
        }

        /// <summary>Whether a JSON value is a number a setting or a marker may hold: whole or not, and finite.</summary>
        internal static bool TryNumber(object value, out double number)
        {
            number =
                value is long whole ? whole
                : value is double real ? real
                : double.NaN;
            return !double.IsNaN(number) && !double.IsInfinity(number);
        }

        private static bool IsIdentifier(string name)
        {
            if (string.IsNullOrEmpty(name))
            {
                return false;
            }

            for (int i = 0; i < name.Length; i++)
            {
                char c = name[i];
                bool letter = (c >= 'A' && c <= 'Z') || (c >= 'a' && c <= 'z') || c == '_';

                if (!letter && !(i > 0 && c >= '0' && c <= '9'))
                {
                    return false;
                }
            }

            return true;
        }

        private static string Show(double value) => value.ToString("R", CultureInfo.InvariantCulture);
    }
}
