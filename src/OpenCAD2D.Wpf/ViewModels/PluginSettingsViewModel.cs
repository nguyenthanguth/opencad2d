using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using OpenCAD2D.Plugins;

namespace OpenCAD2D.Wpf
{
    /// <summary>
    /// One row of Plugin Settings: a setting's label, a box by its kind (a number box, a check box, a text box or a drop-down), its
    /// description and range in grey under it, and, in red under the row, the words of the setting where the value in the box is
    /// refused. A row is checked as its box is edited, by the setting itself, so that the words are those a value set from code is
    /// refused in.
    /// </summary>
    public sealed class PluginSettingRow : ObservableObject
    {
        private readonly CadPluginSetting _setting;
        private string _text;
        private bool _isChecked;
        private string _choice;
        private string _error = string.Empty;

        // What the box held when the dialog was opened, or last read the values again: a row the user has not touched follows the settings.
        private string _shown;

        internal PluginSettingRow(CadPluginSetting setting)
        {
            _setting = setting;
            Read();
            _shown = Written;
        }

        /// <summary>Gets the setting this row is for.</summary>
        internal CadPluginSetting Setting => _setting;

        /// <summary>Gets the label: "Text height".</summary>
        public string Label => _setting.Label;

        /// <summary>Gets the kind of the setting, which says which box the row draws.</summary>
        public CadSettingKind Kind => _setting.Kind;

        /// <summary>Gets whether the row draws a box for typing: a number, a whole number or a text.</summary>
        public bool IsBox =>
            _setting.Kind == CadSettingKind.Number
            || _setting.Kind == CadSettingKind.Integer
            || _setting.Kind == CadSettingKind.Text;

        /// <summary>Gets whether the row draws a check box.</summary>
        public bool IsCheck => _setting.Kind == CadSettingKind.Boolean;

        /// <summary>Gets whether the row draws a drop-down.</summary>
        public bool IsChoice => _setting.Kind == CadSettingKind.Choice;

        /// <summary>Gets the names the drop-down offers; empty for any other kind.</summary>
        public IReadOnlyList<string> Choices => _setting.Choices;

        /// <summary>Gets the description and the range in grey under the box: <c>How high the numbers are written. From 0.1 to 1000.</c></summary>
        public string Hint { get; internal set; }

        /// <summary>Gets or sets what the number box, the whole-number box or the text box holds.</summary>
        public string Text
        {
            get => _text;
            set
            {
                if (Set(ref _text, value ?? string.Empty))
                {
                    Check();
                }
            }
        }

        /// <summary>Gets or sets whether the check box is ticked.</summary>
        public bool IsChecked
        {
            get => _isChecked;
            set
            {
                if (Set(ref _isChecked, value))
                {
                    Check();
                }
            }
        }

        /// <summary>Gets or sets the name the drop-down shows.</summary>
        public string Choice
        {
            get => _choice;
            set
            {
                if (Set(ref _choice, value))
                {
                    Check();
                }
            }
        }

        /// <summary>Gets the words that say why the value in the box is refused, in the setting's own words; empty where it is not.</summary>
        public string Error
        {
            get => _error;
            private set
            {
                if (Set(ref _error, value))
                {
                    Raise(nameof(HasError));
                }
            }
        }

        /// <summary>Gets whether the value in the box is refused.</summary>
        public bool HasError => _error.Length > 0;

        /// <summary>Gets the value in the box, as the setting takes it to convert: the text, whether the box is ticked, or the name chosen.</summary>
        internal object Entered =>
            IsCheck ? (object)_isChecked
            : IsChoice ? _choice
            : _text;

        /// <summary>Gets the text of the box as <see cref="Entered"/> is written in the box.</summary>
        private string Written =>
            IsCheck ? (_isChecked ? "True" : "False")
            : IsChoice ? _choice ?? string.Empty
            : _text;

        private void Check()
        {
            try
            {
                _setting.Convert(Entered);
                Error = string.Empty;
            }
            catch (ArgumentException refusal)
            {
                Error = CadPluginSetting.WordsOf(refusal);
            }
        }

        /// <summary>Puts what the setting holds in the box.</summary>
        private void Read()
        {
            object value = _setting.Value;

            switch (_setting.Kind)
            {
                case CadSettingKind.Boolean:
                    _isChecked = value is bool flag && flag;
                    break;
                case CadSettingKind.Choice:
                    _choice = value?.ToString();
                    break;
                default:
                    _text = Format(value);
                    break;
            }

            Raise(nameof(Text));
            Raise(nameof(IsChecked));
            Raise(nameof(Choice));
            Error = string.Empty;
        }

        /// <summary>Reads the setting again where the user has not changed the box since it was last read.</summary>
        internal void Refresh()
        {
            if (Written == _shown)
            {
                Read();
                _shown = Written;
            }
        }

        internal static string Format(object value) =>
            value is double number
                ? number.ToString("R", CultureInfo.InvariantCulture)
                : Convert.ToString(value, CultureInfo.InvariantCulture) ?? string.Empty;
    }

    /// <summary>
    /// Plugin Settings, as the design draws it: the settings a plugin or a Python tool declares, one row each in the order it
    /// declares them (<see cref="PluginSettingRow"/>), the values kept in the settings file or the defaults, and OK and Cancel at
    /// the foot. OK is greyed while a row is refused; it applies every value at once and writes the settings file once; Cancel
    /// changes nothing. Opened from Settings… of Load/Unload Applications, for a row loaded that declares settings.
    /// </summary>
    public sealed class PluginSettingsViewModel : ObservableObject, IDisposable
    {
        private readonly MainViewModel _model;
        private readonly string _plugin;

        internal PluginSettingsViewModel(
            MainViewModel model,
            string plugin,
            string description,
            IReadOnlyList<CadPluginSetting> settings
        )
        {
            _model = model;
            _plugin = plugin;
            Description = description;
            Rows = settings.Select(s => new PluginSettingRow(s) { Hint = HintOf(s) }).ToList();

            foreach (PluginSettingRow row in Rows)
            {
                row.PropertyChanged += (sender, e) =>
                {
                    if (e.PropertyName == nameof(PluginSettingRow.HasError))
                    {
                        Raise(nameof(CanApply));
                    }
                };
            }

            model.PluginSettingsChanged += OnValuesChanged;
        }

        /// <summary>Gets the title: <c>Plugin Settings – Circle marks</c>.</summary>
        public string Title => "Plugin Settings – " + _plugin;

        /// <summary>Gets the line under the title: what the plugin is, where it is, and that its values are kept in the settings file.</summary>
        public string Description { get; }

        /// <summary>Gets the rows, one for each setting, in the order the plugin declares them.</summary>
        public IReadOnlyList<PluginSettingRow> Rows { get; }

        /// <summary>Gets whether OK can be pressed: no row is refused.</summary>
        public bool CanApply => Rows.All(r => !r.HasError);

        /// <summary>
        /// OK: gives every setting the value in its row, all at once, and writes the settings file once. Nothing is set, and
        /// false given, where a row is refused.
        /// </summary>
        /// <returns>Whether the values were applied.</returns>
        public bool Apply()
        {
            if (!CanApply)
            {
                return false;
            }

            _model.SetPluginSettings(
                _plugin,
                Rows.Select(r => new KeyValuePair<CadPluginSetting, object>(r.Setting, r.Entered))
            );
            return true;
        }

        /// <summary>The values of the settings changed under the open dialog, as a tool set one over its channel: the boxes the user has not touched read them again.</summary>
        private void OnValuesChanged(object sender, EventArgs e)
        {
            foreach (PluginSettingRow row in Rows)
            {
                row.Refresh();
            }
        }

        /// <summary>Lets go of the window's view model.</summary>
        public void Dispose() => _model.PluginSettingsChanged -= OnValuesChanged;

        /// <summary>The description and the range, in grey under the box: <c>How high the numbers are written. From 0.1 to 1000.</c></summary>
        internal static string HintOf(CadPluginSetting setting)
        {
            string description = (setting.Description ?? string.Empty).Trim();

            if (description.Length > 0 && ".!?".IndexOf(description[description.Length - 1]) < 0)
            {
                description += ".";
            }

            string range = RangeOf(setting);
            return range.Length == 0 ? description
                : description.Length == 0 ? range
                : description + " " + range;
        }

        private static string RangeOf(CadPluginSetting setting)
        {
            if (setting.Kind != CadSettingKind.Number && setting.Kind != CadSettingKind.Integer)
            {
                return string.Empty;
            }

            bool least = !double.IsNegativeInfinity(setting.Minimum);
            bool most = !double.IsPositiveInfinity(setting.Maximum);
            string from = PluginSettingRow.Format(setting.Minimum);
            string to = PluginSettingRow.Format(setting.Maximum);

            if (setting.Kind == CadSettingKind.Integer)
            {
                return least && most ? "A whole number from " + from + " to " + to + "."
                    : least ? "A whole number, at least " + from + "."
                    : most ? "A whole number, at most " + to + "."
                    : "A whole number.";
            }

            return least && most ? "From " + from + " to " + to + "."
                : least ? "At least " + from + "."
                : most ? "At most " + to + "."
                : string.Empty;
        }
    }
}
