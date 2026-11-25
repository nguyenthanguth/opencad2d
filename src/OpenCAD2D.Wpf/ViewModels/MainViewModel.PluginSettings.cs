using System;
using System.Collections.Generic;
using System.Globalization;
using System.Runtime.CompilerServices;
using System.Windows.Threading;
using GeometryHelper;
using OpenCAD2D.Plugins;

namespace OpenCAD2D.Wpf
{
    /// <summary>
    /// The values of the settings the plugins and the Python tools declare, kept in <see cref="GlobalSettings.PluginSettings"/>
    /// by the settings store: a C# plugin that loads is given the values kept for it, Plugin Settings (opened from Load/Unload
    /// Applications) changes them, and a tool sets its own over the channel of the tools, from its own thread. The settings file
    /// is written on the window's thread after a value changes; an open dialog is told to read the values again.
    /// </summary>
    public sealed partial class MainViewModel
    {
        private CadPluginSettingStore _pluginSettingStore;
        private readonly ConditionalWeakTable<CadPlugin, object> _settingsGiven =
            new ConditionalWeakTable<CadPlugin, object>();
        private int _pluginSettingsBatch;
        private bool _pluginSettingsSavePosted;
        private readonly object _pluginSettingsGate = new object();

        /// <summary>Occurs, on the window's thread, when the value of a plugin's or a tool's setting changed and the settings file was written: an open Plugin Settings reads the values again.</summary>
        internal event EventHandler PluginSettingsChanged;

        /// <summary>Gets the store of the values of the plugins' settings, over the settings in use.</summary>
        internal CadPluginSettingStore PluginSettingStore
        {
            get
            {
                if (_pluginSettingStore == null)
                {
                    _pluginSettingStore = new CadPluginSettingStore(() => _settings);
                    _pluginSettingStore.Changed += OnPluginSettingChanged;
                }

                return _pluginSettingStore;
            }
        }

        /// <summary>A value was set, on the window's thread or a tool's: the settings file is written on the window's thread, once for the values set while that waits to run.</summary>
        private void OnPluginSettingChanged(object sender, EventArgs e)
        {
            if (_pluginSettingsBatch > 0)
            {
                return;
            }

            if (_pythonDispatcher == null || _pythonDispatcher.CheckAccess())
            {
                SavePluginSettings();
                return;
            }

            lock (_pluginSettingsGate)
            {
                if (_pluginSettingsSavePosted)
                {
                    return;
                }

                _pluginSettingsSavePosted = true;
            }

            _pythonDispatcher.BeginInvoke(
                DispatcherPriority.Background,
                new Action(() =>
                {
                    lock (_pluginSettingsGate)
                    {
                        _pluginSettingsSavePosted = false;
                    }

                    SavePluginSettings();
                })
            );
        }

        private void SavePluginSettings()
        {
            ChangeSettings(s => { }, "a plugin setting was changed");
            PluginSettingsChanged?.Invoke(this, EventArgs.Empty);
        }

        /// <summary>
        /// Sets the values Plugin Settings was given OK on, all at once: each is converted and checked by its setting before any
        /// is set, and the settings file is written once, for the values that differ from what the settings hold, and not at all
        /// where none does.
        /// </summary>
        /// <param name="plugin">The name of the plugin or tool, as its row in Load/Unload Applications shows it.</param>
        /// <param name="values">The settings and the values to give them.</param>
        /// <exception cref="ArgumentException">Thrown, as <see cref="CadPluginSetting.Value"/> throws, when a setting refuses its value; none was set then.</exception>
        internal void SetPluginSettings(string plugin, IEnumerable<KeyValuePair<CadPluginSetting, object>> values)
        {
            var changes = new List<KeyValuePair<CadPluginSetting, object>>();

            foreach (KeyValuePair<CadPluginSetting, object> pair in values)
            {
                object converted = pair.Key.Convert(pair.Value);

                // A choice is held as its name, whatever it was converted to.
                if (
                    !string.Equals(
                        Convert.ToString(converted, CultureInfo.InvariantCulture),
                        Convert.ToString(pair.Key.Value, CultureInfo.InvariantCulture),
                        StringComparison.Ordinal
                    )
                )
                {
                    changes.Add(pair);
                }
            }

            if (changes.Count == 0)
            {
                return;
            }

            _pluginSettingsBatch++;

            try
            {
                foreach (KeyValuePair<CadPluginSetting, object> pair in changes)
                {
                    PluginSettingStore.Set(plugin, pair.Key, pair.Value);
                }
            }
            finally
            {
                _pluginSettingsBatch--;
            }

            SavePluginSettings();
        }

        /// <summary>Gets the settings a Python tool loaded declares, in its order; empty where it declares none or is not loaded.</summary>
        /// <param name="path">The tool's folder.</param>
        internal IReadOnlyList<CadPluginSetting> PythonSettingsOf(string path) =>
            _pythonTools?.SettingsOf(path) ?? (IReadOnlyList<CadPluginSetting>)Array.Empty<CadPluginSetting>();

        /// <summary>
        /// Gives each plugin loaded since the last call the values kept for its settings: a value is written to its property as
        /// the plugin loads (start, NETLOAD, reload), a plugin never set keeps its defaults, and one the plugin's setting now
        /// refuses is dropped with a Warn.
        /// </summary>
        private void GivePluginSettingValues()
        {
            if (_plugins == null)
            {
                return;
            }

            foreach (CadLoadedPlugin entry in _plugins.LoadedInOrder())
            {
                CadPlugin plugin = entry.Plugin;

                if (plugin == null || _settingsGiven.TryGetValue(plugin, out _))
                {
                    continue;
                }

                _settingsGiven.Add(plugin, null);

                try
                {
                    PluginSettingStore.Apply(entry.Name, plugin.Settings);
                }
                catch (Exception exception) when (!(exception is OutOfMemoryException))
                {
                    GeometryHelperLog.Warn(
                        "MainViewModel: the values kept for the settings of the plugin "
                            + entry.Name
                            + " could not be given to it.",
                        exception
                    );
                }
            }
        }
    }
}
