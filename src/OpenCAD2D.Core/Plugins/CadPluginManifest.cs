using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Runtime.Serialization;
using System.Runtime.Serialization.Json;
using System.Text;
using OpenCAD2D.Remote;

namespace OpenCAD2D.Plugins
{
    /// <summary>
    /// What a plugin says of itself in its <c>plugin.json</c>, read without loading its code (D18 of the plan):
    /// <code>
    /// {
    ///   "id": "number-circles",
    ///   "name": "Number circles",
    ///   "version": "1.0.0",
    ///   "author": "Nguyen Thang",
    ///   "kind": "command",
    ///   "minimumApiVersion": "1.0",
    ///   "commands": [ "NUMCIRCLES" ],
    ///   "icon": "icon.png"
    /// }
    /// </code>
    /// Only the name is needed; the id is the name where not given, the kind "command", the API version 1.0. A command is a
    /// name or an object, <c>{ "name": "NUMCIRCLES", "alias": "NC", "description": "Numbers the circles", "usesSelection": true }</c>
    /// (<see cref="CommandEntries"/>); a Python tool, whose process the app starts only when a command is used, also declares
    /// what it adds to the ribbon: <c>"ribbon": [ { "id": "tools", "title": "Tools", "panels": [ { "title": "Draw", "buttons": [
    /// { "text": "Number", "command": "NUMCIRCLES", "icon": "number.png", "size": "large", "tooltip": "Numbers the circles" } ] } ] } ]</c>
    /// (<see cref="Ribbon"/>), the object snaps it adds, <c>"snaps": [ { "keyword": "CTOP", "name": "Circle top", "entityTypes": [ "CIRCLE" ], "glyph": "quadrant", "onByDefault": true } ]</c>
    /// (<see cref="Snaps"/>), and the settings the user may change, <c>"settings": [ { "name": "textHeight", "label": "Text height", "kind": "number", "default": 2.5, "minimum": 0.1, "maximum": 1000 } ]</c>
    /// (<see cref="Settings"/>).
    /// </summary>
    public sealed class CadPluginManifest
    {
        /// <summary>The name of the manifest's file: plugin.json.</summary>
        public const string FileName = "plugin.json";

        /// <summary>The kind of a command plugin, the only kind this presenter loads: "command".</summary>
        public const string CommandKind = "command";

        private CadPluginManifest(Data data, JsonObject root)
        {
            Name = data.Name.Trim();
            Id = string.IsNullOrWhiteSpace(data.Id) ? Name : data.Id.Trim();
            Version = data.Version?.Trim() ?? string.Empty;
            Author = data.Author?.Trim() ?? string.Empty;
            Kind = string.IsNullOrWhiteSpace(data.Kind) ? CommandKind : data.Kind.Trim().ToLowerInvariant();
            MinimumApiVersion = string.IsNullOrWhiteSpace(data.MinimumApiVersion)
                ? new Version(1, 0)
                : System.Version.Parse(data.MinimumApiVersion.Trim());
            var entries = new List<CadPluginCommandEntry>();
            var commands = new List<string>();

            foreach (object item in root.GetArray("commands"))
            {
                CadPluginCommandEntry entry = CommandEntryOf(item);

                if (entry != null)
                {
                    entries.Add(entry);
                    commands.Add(entry.Name);
                }
            }

            Commands = commands;
            CommandEntries = entries;
            Ribbon = RibbonOf(root.GetArray("ribbon"));
            Snaps = SnapsOf(root.GetArray("snaps"));
            Settings = SettingsOf(root.GetArray("settings"));
            Icon = string.IsNullOrWhiteSpace(data.Icon) ? null : data.Icon.Trim();
        }

        /// <summary>Gets the id, a short name for the plugin; the name where not given.</summary>
        public string Id { get; }

        /// <summary>Gets the name, which the Plugin manager shows and PLUGINUNLOAD and PLUGINRELOAD are given.</summary>
        public string Name { get; }

        /// <summary>Gets the plugin's version, as written; empty where not given.</summary>
        public string Version { get; }

        /// <summary>Gets the author; empty where not given.</summary>
        public string Author { get; }

        /// <summary>Gets the kind, in small letters: "command" for a command plugin.</summary>
        public string Kind { get; }

        /// <summary>Gets the least version of the plugin API the plugin needs; 1.0 where not given.</summary>
        public Version MinimumApiVersion { get; }

        /// <summary>Gets the names of the commands, in capitals, in the order written.</summary>
        public IReadOnlyList<string> Commands { get; }

        /// <summary>Gets the commands as declared, in the order written: the names of <see cref="Commands"/> with the alias, the description and whether the command takes the selection, where the manifest gives them.</summary>
        public IReadOnlyList<CadPluginCommandEntry> CommandEntries { get; }

        /// <summary>Gets the tabs, panels and buttons the plugin declares for the ribbon, in the order written; none where it declares none.</summary>
        public IReadOnlyList<CadPluginRibbonTabEntry> Ribbon { get; }

        /// <summary>Gets the object snaps a Python tool declares, in the order written; none where it declares none. A snap with no keyword or no name is passed over.</summary>
        public IReadOnlyList<CadPluginSnapEntry> Snaps { get; }

        /// <summary>Gets the settings a Python tool declares, in the order written; none where it declares none. A C# plugin's settings are declared in its code with <see cref="CadSettingAttribute"/>, and this list is ignored for it.</summary>
        public IReadOnlyList<CadPluginSettingEntry> Settings { get; }

        /// <summary>Gets the path of the icon, relative to the manifest; null where not given.</summary>
        public string Icon { get; }

        /// <summary>
        /// Says why the presenter does not load the plugin, or null where it may: a kind other than "command", or a newer
        /// version of the plugin API than <see cref="CadPlugin.ApiVersion"/>.
        /// </summary>
        /// <returns>The reason, or null.</returns>
        public string Refusal()
        {
            if (!string.Equals(Kind, CommandKind, StringComparison.Ordinal))
            {
                return "The plugin "
                    + Name
                    + " is of the kind \""
                    + Kind
                    + "\", and this presenter loads command plugins only.";
            }

            if (MinimumApiVersion > CadPlugin.ApiVersion)
            {
                return "The plugin "
                    + Name
                    + " needs version "
                    + MinimumApiVersion
                    + " of the plugin API, and this presenter has "
                    + CadPlugin.ApiVersion
                    + ": it needs a newer presenter.";
            }

            return null;
        }

        /// <summary>Reads a manifest from its JSON text.</summary>
        /// <param name="json">The text.</param>
        /// <returns>The manifest.</returns>
        /// <exception cref="ArgumentNullException">Thrown when the text is null.</exception>
        /// <exception cref="FormatException">Thrown when the text is not JSON, has no name, has a minimum API version that is not a version, or has a snap's glyph or a setting that breaks a rule (the message says which).</exception>
        public static CadPluginManifest Parse(string json)
        {
            if (json == null)
            {
                throw new ArgumentNullException(nameof(json));
            }

            Data data;

            try
            {
                using (var stream = new MemoryStream(Encoding.UTF8.GetBytes(json)))
                {
                    data = (Data)new DataContractJsonSerializer(typeof(Data)).ReadObject(stream);
                }
            }
            catch (Exception exception)
                when (exception is SerializationException || exception is System.Xml.XmlException)
            {
                throw new FormatException(
                    "The plugin's manifest is not JSON of the form plugin.json has: " + exception.Message,
                    exception
                );
            }

            if (data == null || string.IsNullOrWhiteSpace(data.Name))
            {
                throw new FormatException("The plugin's manifest gives no name.");
            }

            if (
                !string.IsNullOrWhiteSpace(data.MinimumApiVersion)
                && !System.Version.TryParse(data.MinimumApiVersion.Trim(), out _)
            )
            {
                throw new FormatException(
                    "The plugin's manifest gives the API version \""
                        + data.MinimumApiVersion
                        + "\", which is not a version."
                );
            }

            return new CadPluginManifest(data, Root(json));
        }

        /// <summary>The same text read as plain JSON, where the commands may be names or objects.</summary>
        private static JsonObject Root(string json)
        {
            try
            {
                return RemoteJson.Parse(json) as JsonObject ?? new JsonObject();
            }
            catch (FormatException exception)
            {
                throw new FormatException(
                    "The plugin's manifest is not JSON of the form plugin.json has: " + exception.Message,
                    exception
                );
            }
        }

        private static CadPluginCommandEntry CommandEntryOf(object item)
        {
            if (item is string text)
            {
                return string.IsNullOrWhiteSpace(text)
                    ? null
                    : new CadPluginCommandEntry(text.Trim().ToUpperInvariant(), null, null, false);
            }

            if (!(item is JsonObject declared) || string.IsNullOrWhiteSpace(declared.GetString("name")))
            {
                return null;
            }

            return new CadPluginCommandEntry(
                declared.GetString("name").Trim().ToUpperInvariant(),
                string.IsNullOrWhiteSpace(declared.GetString("alias"))
                    ? null
                    : declared.GetString("alias").Trim().ToUpperInvariant(),
                string.IsNullOrWhiteSpace(declared.GetString("description"))
                    ? null
                    : declared.GetString("description").Trim(),
                declared.GetBool("usesSelection")
            );
        }

        private static IReadOnlyList<CadPluginSnapEntry> SnapsOf(JsonArray declared)
        {
            var found = new List<CadPluginSnapEntry>();

            foreach (object item in declared)
            {
                CadPluginSnapEntry entry = CadPluginSnapEntry.From(item);

                if (entry != null)
                {
                    found.Add(entry);
                }
            }

            return found;
        }

        private static IReadOnlyList<CadPluginSettingEntry> SettingsOf(JsonArray declared)
        {
            var found = new List<CadPluginSettingEntry>();
            var names = new HashSet<string>(StringComparer.Ordinal);

            for (int i = 0; i < declared.Count; i++)
            {
                found.Add(CadPluginSettingEntry.From(declared[i], i + 1, names));
            }

            return found;
        }

        private static IReadOnlyList<CadPluginRibbonTabEntry> RibbonOf(JsonArray tabs)
        {
            var found = new List<CadPluginRibbonTabEntry>();

            foreach (JsonObject tab in tabs.OfType<JsonObject>())
            {
                string title = tab.GetString("title")?.Trim();

                if (string.IsNullOrEmpty(title))
                {
                    continue;
                }

                var panels = new List<CadPluginRibbonPanelEntry>();

                foreach (JsonObject panel in tab.GetArray("panels").OfType<JsonObject>())
                {
                    var buttons = new List<CadPluginRibbonButtonEntry>();

                    foreach (JsonObject button in panel.GetArray("buttons").OfType<JsonObject>())
                    {
                        string text = button.GetString("text")?.Trim();
                        string command = button.GetString("command")?.Trim();

                        if (!string.IsNullOrEmpty(text) && !string.IsNullOrEmpty(command))
                        {
                            buttons.Add(
                                new CadPluginRibbonButtonEntry(
                                    text,
                                    command,
                                    string.IsNullOrWhiteSpace(button.GetString("icon"))
                                        ? null
                                        : button.GetString("icon").Trim(),
                                    string.Equals(
                                        button.GetString("size")?.Trim(),
                                        "large",
                                        StringComparison.OrdinalIgnoreCase
                                    ),
                                    string.IsNullOrWhiteSpace(button.GetString("tooltip"))
                                        ? null
                                        : button.GetString("tooltip").Trim()
                                )
                            );
                        }
                    }

                    panels.Add(
                        new CadPluginRibbonPanelEntry(panel.GetString("title")?.Trim() ?? string.Empty, buttons)
                    );
                }

                string id = tab.GetString("id")?.Trim();
                found.Add(new CadPluginRibbonTabEntry(string.IsNullOrEmpty(id) ? title : id, title, panels));
            }

            return found;
        }

        /// <summary>Reads a manifest from a plugin.json file, without loading any code.</summary>
        /// <param name="path">The path of the plugin.json.</param>
        /// <returns>The manifest.</returns>
        /// <exception cref="ArgumentNullException">Thrown when the path is null.</exception>
        /// <exception cref="FileNotFoundException">Thrown when there is no such file.</exception>
        /// <exception cref="FormatException">Thrown when the manifest cannot be read (see <see cref="Parse"/>).</exception>
        public static CadPluginManifest Read(string path)
        {
            if (path == null)
            {
                throw new ArgumentNullException(nameof(path));
            }

            return Parse(File.ReadAllText(path, Encoding.UTF8));
        }

        /// <inheritdoc/>
        public override string ToString() => Name + " " + Version;

        [DataContract]
        private sealed class Data
        {
            [DataMember(Name = "id")]
            public string Id { get; set; }

            [DataMember(Name = "name")]
            public string Name { get; set; }

            [DataMember(Name = "version")]
            public string Version { get; set; }

            [DataMember(Name = "author")]
            public string Author { get; set; }

            [DataMember(Name = "kind")]
            public string Kind { get; set; }

            [DataMember(Name = "minimumApiVersion")]
            public string MinimumApiVersion { get; set; }

            [DataMember(Name = "icon")]
            public string Icon { get; set; }
        }
    }
}
