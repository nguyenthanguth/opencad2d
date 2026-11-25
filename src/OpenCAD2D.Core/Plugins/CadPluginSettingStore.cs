using System;
using System.Collections.Generic;
using System.Globalization;
using GeometryHelper;

namespace OpenCAD2D.Plugins
{
    /// <summary>
    /// Keeps the values of the plugins' settings in <see cref="GlobalSettings.PluginSettings"/>, one entry per setting as
    /// <c>Circles/textHeight=2.5</c> (the plugin's name, a slash, the setting's name, an equals sign and the value in the
    /// invariant culture: <c>True</c> or <c>False</c> for a boolean, the name of a choice). The values are kept by name, not by
    /// object, so that a plugin that is not loaded keeps its values for when it is. A value equal to the setting's default is
    /// not kept, so that a setting never set leaves no entry. Safe to use from any thread: one lock covers the entries.
    /// </summary>
    internal sealed class CadPluginSettingStore
    {
        private readonly Func<GlobalSettings> _settings;
        private readonly object _gate = new object();

        /// <summary>Makes a store over the settings the function gives; asked each time, as the app swaps its settings for a copy when the user applies changes.</summary>
        public CadPluginSettingStore(Func<GlobalSettings> settings)
        {
            _settings = settings ?? throw new ArgumentNullException(nameof(settings));
        }

        /// <summary>Raised after a value is set, on the thread that set it, with no lock held.</summary>
        public event EventHandler Changed;

        /// <summary>
        /// Makes the settings a tool declares, which read and write their values through this store: a setting never set has
        /// its default.
        /// </summary>
        public IReadOnlyList<CadPluginSetting> Declare(string plugin, IReadOnlyList<CadPluginSettingEntry> entries)
        {
            var made = new List<CadPluginSetting>();

            foreach (CadPluginSettingEntry entry in entries)
            {
                CadPluginSetting setting = null;
                setting = new CadPluginSetting(
                    entry,
                    () => Value(plugin, setting),
                    converted => Write(plugin, setting, converted)
                );
                made.Add(setting);
            }

            return made;
        }

        /// <summary>The value a setting has: the one kept, where there is one the setting takes, else its default.</summary>
        public object Value(string plugin, CadPluginSetting setting)
        {
            string stored = Stored(plugin, setting.Name);

            if (stored == null)
            {
                return setting.Default;
            }

            try
            {
                return Canonical(setting, setting.Convert(stored));
            }
            catch (ArgumentException)
            {
                return setting.Default;
            }
        }

        /// <summary>
        /// Sets a setting: the value is converted and checked by the setting, which refuses it in its own words, and kept
        /// unless it is the default, which takes the entry away.
        /// </summary>
        /// <exception cref="ArgumentException">Thrown, as <see cref="CadPluginSetting.Value"/> throws, when the setting refuses the value.</exception>
        public void Set(string plugin, CadPluginSetting setting, object value)
        {
            setting.Value = value;

            // A declared setting has written through this store already.
            if (!setting.IsDeclared)
            {
                Write(plugin, setting, setting.Value);
            }
        }

        /// <summary>
        /// Gives a plugin's settings the values kept for them, as the plugin loads. A value the setting refuses, as a range a
        /// new version of the plugin has narrowed, is dropped with a Warn and the setting keeps its default.
        /// </summary>
        public void Apply(string plugin, IReadOnlyList<CadPluginSetting> settings)
        {
            foreach (CadPluginSetting setting in settings)
            {
                string stored = Stored(plugin, setting.Name);

                if (stored == null)
                {
                    continue;
                }

                try
                {
                    // A declared setting reads the store itself: the value is only checked.
                    object converted = setting.Convert(stored);

                    if (!setting.IsDeclared)
                    {
                        setting.Value = converted;
                    }
                }
                catch (ArgumentException refusal)
                {
                    GeometryHelperLog.Warn(
                        "CadPluginSettingStore: the value kept for "
                            + plugin
                            + "/"
                            + setting.Name
                            + " ("
                            + stored
                            + ") was dropped: "
                            + CadPluginSetting.WordsOf(refusal)
                    );
                    Replace(plugin, setting.Name, null);
                }
            }
        }

        private void Write(string plugin, CadPluginSetting setting, object converted)
        {
            object value = Canonical(setting, converted);
            Replace(plugin, setting.Name, Equals(value, setting.Default) ? null : Format(value));

            try
            {
                Changed?.Invoke(this, EventArgs.Empty);
            }
            catch (Exception exception) when (!(exception is OutOfMemoryException))
            {
                GeometryHelperLog.Warn("CadPluginSettingStore: a handler of Changed threw.", exception);
            }
        }

        /// <summary>The value kept for a setting, as text: the last entry of it; null where there is none.</summary>
        private string Stored(string plugin, string name)
        {
            lock (_gate)
            {
                string found = null;

                foreach (string entry in _settings().PluginSettings)
                {
                    if (
                        TryParse(entry, out string owner, out string setting, out string value)
                        && Is(owner, setting, plugin, name)
                    )
                    {
                        found = value;
                    }
                }

                return found;
            }
        }

        /// <summary>Takes the entries of a setting away and puts one with the value in their place; with no value, takes them away alone. The list is replaced whole, so that a thread saving the settings meanwhile reads one list or the other.</summary>
        private void Replace(string plugin, string name, string value)
        {
            lock (_gate)
            {
                GlobalSettings settings = _settings();
                var entries = new List<string>(settings.PluginSettings.Count + 1);

                foreach (string entry in settings.PluginSettings)
                {
                    if (
                        !TryParse(entry, out string owner, out string setting, out _)
                        || !Is(owner, setting, plugin, name)
                    )
                    {
                        entries.Add(entry);
                    }
                }

                if (value != null)
                {
                    entries.Add(plugin + "/" + name + "=" + value);
                }

                settings.PluginSettings = entries;
            }
        }

        private static bool Is(string owner, string setting, string plugin, string name) =>
            string.Equals(owner, plugin, StringComparison.OrdinalIgnoreCase)
            && string.Equals(setting, name, StringComparison.Ordinal);

        /// <summary>A value as the setting's kind holds it: a choice by its name, whatever it was given as.</summary>
        private static object Canonical(CadPluginSetting setting, object value) =>
            setting.Kind == CadSettingKind.Choice ? value?.ToString() : value;

        private static string Format(object value)
        {
            switch (value)
            {
                case null:
                    return string.Empty;
                case double number:
                    return number.ToString("R", CultureInfo.InvariantCulture);
                case bool flag:
                    return flag ? "True" : "False";
                default:
                    return Convert.ToString(value, CultureInfo.InvariantCulture);
            }
        }

        /// <summary>
        /// Splits an entry at the first equals sign whose left part ends in a slash and an identifier, so that a plugin's name
        /// may hold an equals sign and a value may hold both.
        /// </summary>
        private static bool TryParse(string entry, out string plugin, out string setting, out string value)
        {
            plugin = setting = value = null;

            if (entry == null)
            {
                return false;
            }

            int equals = entry.IndexOf('=');

            while (equals >= 0)
            {
                int slash = equals == 0 ? -1 : entry.LastIndexOf('/', equals - 1);

                if (slash > 0 && IsIdentifier(entry, slash + 1, equals))
                {
                    plugin = entry.Substring(0, slash);
                    setting = entry.Substring(slash + 1, equals - slash - 1);
                    value = entry.Substring(equals + 1);
                    return true;
                }

                equals = entry.IndexOf('=', equals + 1);
            }

            return false;
        }

        private static bool IsIdentifier(string text, int from, int to)
        {
            if (to <= from)
            {
                return false;
            }

            for (int i = from; i < to; i++)
            {
                char c = text[i];

                if (!(char.IsLetter(c) || c == '_' || (i > from && char.IsDigit(c))))
                {
                    return false;
                }
            }

            return true;
        }
    }
}
