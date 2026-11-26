using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Windows.Input;
using GeometryHelper;
using GeometryHelper.Dxf;

namespace OpenCAD2D.Wpf
{
    /// <summary>The ground of the style previews: the canvas's black, or white.</summary>
    public enum StylePreviewGround
    {
        /// <summary>The canvas's black ground; lit parts #3D9BFF.</summary>
        Black,

        /// <summary>A white ground, as paper shows the drawing; lit parts the darker #0A6BD6.</summary>
        White,
    }

    /// <summary>
    /// What a style preview shows of one tab or one style, and what it is asked: the sample, the parts the field being edited or
    /// hovered lights, the callouts of the distance it sets, the chip for a field that has no part, and the part under the pointer
    /// with the fields that set it. It holds no picture and knows no window: the preview view draws what it holds, and the dialog's
    /// view model tells it the field that is active and takes from it the fields to light and the field a click goes to.
    /// </summary>
    public sealed class StylePreviewViewModel : ObservableObject
    {
        private static StylePreviewGround _ground;

        private readonly StylePreviewSource _source;
        private CadStyleSample _sample;
        private string _activeKey;
        private CadStylePart _hover;
        private IReadOnlyList<CadStylePart> _litParts;
        private IReadOnlyList<CadStyleSamplePart> _litSampleParts;
        private IReadOnlyList<CadStyleMeasure> _measures;
        private string _chip;
        private double _zoomPercent = 100.0;

        internal StylePreviewViewModel(StylePreviewSource source, CadFonts fonts)
        {
            _source = source ?? throw new ArgumentNullException(nameof(source));
            Fonts = fonts;
            FitCommand = new DelegateCommand(_ => FitRequested?.Invoke(this, EventArgs.Empty));
        }

        /// <summary>Raised when the ground changes, for every preview.</summary>
        public static event EventHandler GroundChanged;

        /// <summary>Gets or sets the ground of every preview of the session: black by default; kept until the app closes, not in the settings file.</summary>
        public static StylePreviewGround Ground
        {
            get => _ground;
            set
            {
                if (_ground != value)
                {
                    _ground = value;
                    GroundChanged?.Invoke(null, EventArgs.Empty);
                }
            }
        }

        /// <summary>Raised when a click on a part asks for the first field that sets it; the argument is the field's key, as <c>DIMBLK2</c>.</summary>
        public event EventHandler<string> FieldRequested;

        /// <summary>Raised when the preview is to show the whole sample again.</summary>
        internal event EventHandler FitRequested;

        /// <summary>Gets the sample: its drawing, with the parts told apart; made again after a value changed.</summary>
        public CadStyleSample Sample => _sample ?? (_sample = _source.Build());

        /// <summary>Gets the parts lit: those of the field active, or the part under the pointer; none for a field that sets no part.</summary>
        public IReadOnlyList<CadStylePart> LitParts =>
            _litParts ?? (_litParts = LitSampleParts.Select(p => p.Part).Distinct().ToList());

        /// <summary>Gets the callouts of the distance the field active sets, and the ghosts of a part it leaves out; none while a part is hovered.</summary>
        public IReadOnlyList<CadStyleMeasure> Measures =>
            _measures
            ?? (
                _measures =
                    _hover == CadStylePart.None && _activeKey != null
                        ? _source.Measures(Sample, _activeKey)
                        : Array.Empty<CadStyleMeasure>()
            );

        /// <summary>
        /// Gets the chip for a field that lights nothing or little: <c>Every part · DIMSCALE = 1</c>, the formatted words with the
        /// value a units field sets, <c>Suppressed</c> for a part the style leaves out; empty for none.
        /// </summary>
        public string Chip => _chip ?? (_chip = ChipOfTheActiveKey());

        /// <summary>Gets the part under the pointer, <see cref="CadStylePart.None"/> where there is none.</summary>
        public CadStylePart HoverPart => _hover;

        /// <summary>Gets what the tooltip under the pointer says: the part and the fields that set it, as <c>Arrowhead 2 · DIMBLK2, DIMASZ · click to edit</c>; empty where no part is hovered.</summary>
        public string HoverText
        {
            get
            {
                if (_hover == CadStylePart.None)
                {
                    return string.Empty;
                }

                List<string> keys = HoverKeysInOrder().Select(_source.KeyText).Distinct().ToList();
                string names = keys.Count > 6 ? string.Join(", ", keys.Take(6)) + ", …" : string.Join(", ", keys);
                return StylePreviewSource.NameOf(_hover)
                    + (names.Length == 0 ? string.Empty : " · " + names)
                    + (CanEdit && keys.Count > 0 ? " · click to edit" : string.Empty);
            }
        }

        /// <summary>Gets the command of Fit: the sample shown whole again.</summary>
        public ICommand FitCommand { get; }

        /// <summary>Gets how far the preview is zoomed, in percent of Fit; set by the view.</summary>
        public double ZoomPercent
        {
            get => _zoomPercent;
            internal set => Set(ref _zoomPercent, value);
        }

        /// <summary>Gets the key of the field that is active; null for none.</summary>
        internal string ActiveKey => _activeKey;

        /// <summary>Gets the fonts of the drawing edited, which the sample's words are drawn in.</summary>
        internal CadFonts Fonts { get; }

        /// <summary>Gets or sets whether a click on a part goes to a field: false in the Managers, which have none.</summary>
        internal bool CanEdit { get; set; } = true;

        /// <summary>Gets or sets whether a field key belongs to the tab shown, so that the fields of that tab come first in the tooltip and in a click.</summary>
        internal Func<string, bool> InCurrentTab { get; set; }

        /// <summary>Gets the keys of the fields that set the part hovered, as <see cref="CadDimStyleEditor.KeysOf"/> or <see cref="CadMLeaderStyleEditor.KeysOf"/> lists them; none where no part is.</summary>
        internal IReadOnlyList<string> HoverKeys =>
            _hover == CadStylePart.None ? Array.Empty<string>() : _source.KeysOf(_hover);

        /// <summary>Gets whether the chip stands beside the parts lit (the words a units field sets) rather than in the corner: it has parts to stand beside.</summary>
        internal bool ChipBesideLit => Chip.Length > 0 && Chip != "Suppressed" && LitHandles.Count > 0;

        /// <summary>Gets the handles of the entities lit, in the order the sample draws them.</summary>
        internal IReadOnlyList<string> LitHandles =>
            LitSampleParts.SelectMany(p => p.Handles).Distinct(StringComparer.Ordinal).ToList();

        /// <summary>Gets whether the field active sets a part the sample draws no entity of, as the words' fill of none: the preview draws a ghost round the words.</summary>
        internal bool GhostsWordsBox =>
            _hover == CadStylePart.None
            && _activeKey != null
            && _source.GhostsWordsBox(_activeKey)
            && LitSampleParts.Count == 0;

        private IReadOnlyList<CadStyleSamplePart> LitSampleParts =>
            _litSampleParts
            ?? (
                _litSampleParts =
                    _hover != CadStylePart.None ? Sample.Parts.Where(p => p.Part == _hover).ToList()
                    : _activeKey == null ? (IReadOnlyList<CadStyleSamplePart>)Array.Empty<CadStyleSamplePart>()
                    : _source.LitParts(Sample, _activeKey)
            );

        /// <summary>Makes the sample again from the values, after one changed or the tab changed; the part hovered is dropped.</summary>
        internal void Invalidate()
        {
            _sample = null;
            _hover = CadStylePart.None;
            Reset();
            Raise(nameof(Sample));
            Raise(nameof(HoverPart));
            Raise(nameof(HoverText));
        }

        /// <summary>Tells the preview which field is active, by its key (<c>DIMEXE</c>); null for none.</summary>
        internal void SetActiveKey(string key)
        {
            string next = string.IsNullOrEmpty(key) ? null : key;

            if (string.Equals(next, _activeKey, StringComparison.Ordinal))
            {
                return;
            }

            _activeKey = next;
            Reset();
        }

        /// <summary>Tells the preview which part the pointer is over, as the view finds it; <see cref="CadStylePart.None"/> for none.</summary>
        /// <param name="part">The part.</param>
        public void PointerAt(CadStylePart part)
        {
            if (part == _hover)
            {
                return;
            }

            _hover = part;
            Reset();
            Raise(nameof(HoverPart));
            Raise(nameof(HoverText));
        }

        /// <summary>Asks for the field that sets the part under the pointer: raises <see cref="FieldRequested"/> with the key of the first, those of the tab shown first; nothing where no part is hovered or no field sets it.</summary>
        public void Click()
        {
            if (!CanEdit)
            {
                return;
            }

            string first = HoverKeysInOrder().FirstOrDefault();

            if (first != null)
            {
                FieldRequested?.Invoke(this, first);
            }
        }

        /// <summary>The keys of the fields that set the part hovered, those of the tab shown first, each group in the dialog's order.</summary>
        private IEnumerable<string> HoverKeysInOrder()
        {
            IReadOnlyList<string> keys = HoverKeys;

            if (InCurrentTab == null)
            {
                return keys;
            }

            return keys.Where(InCurrentTab).Concat(keys.Where(k => !InCurrentTab(k)));
        }

        private void Reset()
        {
            _litParts = null;
            _litSampleParts = null;
            _measures = null;
            _chip = null;
            Raise(nameof(LitParts));
            Raise(nameof(Measures));
            Raise(nameof(Chip));
        }

        private string ChipOfTheActiveKey()
        {
            if (_hover != CadStylePart.None || _activeKey == null)
            {
                return string.Empty;
            }

            IReadOnlyList<CadStyleMeasure> measures = Measures;
            string chip = _source.Chip(Sample, _activeKey, LitSampleParts, measures);

            if (chip != null)
            {
                return chip;
            }

            bool ghost = measures.Any(m => StylePreviewSource.IsGhost(m.Key));
            return ghost || (_source.PartsOf(_activeKey).Count > 0 && LitSampleParts.Count == 0)
                ? "Suppressed"
                : string.Empty;
        }
    }

    /// <summary>What a style preview asks of the style it previews: the sample, the table of fields to parts, the callouts and the chips.</summary>
    internal abstract class StylePreviewSource
    {
        private static readonly Dictionary<CadStylePart, string> Names = new Dictionary<CadStylePart, string>
        {
            { CadStylePart.DefinitionPoint, "Definition point" },
            { CadStylePart.DimensionLine1, "Dimension line 1" },
            { CadStylePart.DimensionLine2, "Dimension line 2" },
            { CadStylePart.ExtensionLine1, "Extension line 1" },
            { CadStylePart.ExtensionLine2, "Extension line 2" },
            { CadStylePart.Arrow1, "Arrowhead 1" },
            { CadStylePart.Arrow2, "Arrowhead 2" },
            { CadStylePart.LeaderArrow, "Leader arrowhead" },
            { CadStylePart.LeaderLine, "Leader line" },
            { CadStylePart.Text, "Text" },
            { CadStylePart.TextFrame, "Text frame" },
            { CadStylePart.TextFill, "Text fill" },
            { CadStylePart.TextLeader, "Text leader" },
            { CadStylePart.CentreMark, "Centre mark" },
            { CadStylePart.ArcSymbol, "Arc length symbol" },
            { CadStylePart.Jog, "Jog" },
            { CadStylePart.Landing, "Landing" },
            { CadStylePart.Block, "Block" },
            { CadStylePart.Arrow, "Arrowhead" },
        };

        /// <summary>The words that name a part in a tooltip: <c>Arrowhead 2</c>.</summary>
        internal static string NameOf(CadStylePart part) =>
            Names.TryGetValue(part, out string name) ? name : part.ToString();

        /// <summary>Whether the measure of a key is the ghost of a part the style leaves out rather than a callout.</summary>
        internal static bool IsGhost(string key) =>
            key == "DIMSE1"
            || key == "DIMSE2"
            || key == "DIMSD1"
            || key == "DIMSD2"
            || key == "DIMCEN"
            || key == "IncludeLanding";

        internal abstract CadStyleSample Build();

        internal abstract IReadOnlyList<CadStylePart> PartsOf(string key);

        internal abstract IReadOnlyList<string> KeysOf(CadStylePart part);

        internal abstract IReadOnlyList<CadStyleSamplePart> LitParts(CadStyleSample sample, string key);

        internal abstract IReadOnlyList<CadStyleMeasure> Measures(CadStyleSample sample, string key);

        /// <summary>The chip of a field, or null where it has none of its own (the preview then says "Suppressed" of a part left out).</summary>
        internal abstract string Chip(
            CadStyleSample sample,
            string key,
            IReadOnlyList<CadStyleSamplePart> lit,
            IReadOnlyList<CadStyleMeasure> measures
        );

        /// <summary>How a field is named in a tooltip: its key without the mark that tells two fields of one variable apart.</summary>
        internal virtual string KeyText(string key) =>
            key.EndsWith("#", StringComparison.Ordinal) ? key.Substring(0, key.Length - 1) : key;

        /// <summary>Whether the field of a key sets a part that, left out, is drawn as a ghost box round the words.</summary>
        internal virtual bool GhostsWordsBox(string key) => false;

        /// <summary>A sample of nothing, for settings that make no style.</summary>
        internal static CadStyleSample Empty() =>
            new CadStyleSample(
                new DxfDatabase("AC1027"),
                CadDimSampleKind.Overview,
                Array.Empty<CadStyleSamplePart>(),
                Array.Empty<SampleShape>(),
                Array.Empty<string>()
            );
    }

    /// <summary>The preview of a dimension style: the sample of a tab, its fields' parts and callouts.</summary>
    internal sealed class DimPreviewSource : StylePreviewSource
    {
        private static readonly HashSet<string> Formatting = new HashSet<string>(StringComparer.Ordinal)
        {
            "DIMLUNIT",
            "DIMDEC",
            "DIMFRAC",
            "DIMDSEP",
            "DIMRND",
            "DIMPOST",
            "DIMLFAC",
            "DIMZIN",
            "DIMAUNIT",
            "DIMADEC",
            "DIMAZIN",
            "DIMALT",
            "DIMALTU",
            "DIMALTD",
            "DIMALTF",
            "DIMALTRND",
            "DIMAPOST",
            "DIMALTZ",
            "DIMPOST\\X",
            "DIMTOL",
            "DIMTDEC",
            "DIMTP",
            "DIMTM",
            "DIMTFAC",
            "DIMTOLJ",
            "DIMTZIN",
        };

        private readonly DxfDatabase _database;
        private readonly Func<IDictionary<string, object>> _values;
        private readonly Func<CadDimSampleKind> _kind;
        private readonly Func<string, string> _valueText;

        internal DimPreviewSource(
            DxfDatabase database,
            Func<IDictionary<string, object>> values,
            Func<CadDimSampleKind> kind,
            Func<string, string> valueText
        )
        {
            _database = database;
            _values = values;
            _kind = kind;
            _valueText = valueText;
        }

        internal override CadStyleSample Build()
        {
            try
            {
                return CadDimStyleEditor.Sample(_database, _values(), _kind());
            }
            catch (Exception exception) when (exception is ArgumentException || exception is InvalidOperationException)
            {
                GeometryHelperLog.Debug("StylePreview: the sample dimensions were not drawn: " + exception.Message);
                return Empty();
            }
        }

        internal override IReadOnlyList<CadStylePart> PartsOf(string key) => CadDimStyleEditor.PartsOf(key);

        internal override IReadOnlyList<string> KeysOf(CadStylePart part) => CadDimStyleEditor.KeysOf(part);

        internal override IReadOnlyList<CadStyleSamplePart> LitParts(CadStyleSample sample, string key) =>
            CadDimStyleEditor.LitParts(sample, key);

        internal override IReadOnlyList<CadStyleMeasure> Measures(CadStyleSample sample, string key) =>
            CadDimStyleEditor.Measures(sample, _values(), key);

        internal override bool GhostsWordsBox(string key) =>
            key.Replace(" ", string.Empty).ToUpperInvariant() == "DIMTFILL" || key.ToUpperInvariant() == "DIMGAP#";

        internal override string Chip(
            CadStyleSample sample,
            string key,
            IReadOnlyList<CadStyleSamplePart> lit,
            IReadOnlyList<CadStyleMeasure> measures
        )
        {
            string name = key.Replace(" ", string.Empty).ToUpperInvariant();
            IDictionary<string, object> values = _values();

            switch (name)
            {
                case "DIMSCALE":
                    double scale = values.TryGetValue("Dimscale", out object s) && s is double d && d > 0.0 ? d : 1.0;
                    return "Every part · DIMSCALE = " + scale.ToString("0.####", CultureInfo.InvariantCulture);
                case "ANNOTATIVE":
                    return "Every part · Annotative = " + (_valueText(key) ?? "off");
                case "DIMUPT":
                    return "Takes effect when a dimension is placed";
                case "DIMDLE":
                    return measures.Count == 0 ? "Shows with tick arrowheads" : null;
            }

            if (!Formatting.Contains(name))
            {
                return null;
            }

            CadStyleSamplePart words = lit.FirstOrDefault(p => p.Part == CadStylePart.Text);
            DxfMText text =
                words == null
                    ? null
                    : sample.Database.ModelSpace.OfType<DxfMText>().FirstOrDefault(m => m.Handle == words.Handles[0]);
            string value = _valueText(key);
            string shown = text == null ? null : CadMTextContent.Parse(text.Contents, text.TextHeight).PlainText;
            return name + " = " + value + (string.IsNullOrEmpty(shown) ? string.Empty : " · " + shown);
        }
    }

    /// <summary>The preview of a multileader style: the one sample of the style, its settings' parts and callouts.</summary>
    internal sealed class MLeaderPreviewSource : StylePreviewSource
    {
        private readonly DxfDatabase _database;
        private readonly Func<CadMLeaderStyleSettings> _settings;
        private readonly Func<string, string> _valueText;
        private readonly Func<string, string> _labelOf;

        internal MLeaderPreviewSource(
            DxfDatabase database,
            Func<CadMLeaderStyleSettings> settings,
            Func<string, string> valueText,
            Func<string, string> labelOf
        )
        {
            _database = database;
            _settings = settings;
            _valueText = valueText;
            _labelOf = labelOf;
        }

        internal override CadStyleSample Build()
        {
            CadMLeaderStyleSettings settings = _settings();

            if (settings == null)
            {
                return Empty();
            }

            try
            {
                return CadMLeaderStyleEditor.PreviewSample(_database, settings);
            }
            catch (Exception exception) when (exception is ArgumentException || exception is InvalidOperationException)
            {
                GeometryHelperLog.Debug("StylePreview: the sample multileader was not drawn: " + exception.Message);
                return Empty();
            }
        }

        internal override IReadOnlyList<CadStylePart> PartsOf(string key) => CadMLeaderStyleEditor.PartsOf(key);

        internal override IReadOnlyList<string> KeysOf(CadStylePart part) => CadMLeaderStyleEditor.KeysOf(part);

        internal override IReadOnlyList<CadStyleSamplePart> LitParts(CadStyleSample sample, string key)
        {
            IReadOnlyList<CadStylePart> parts = CadMLeaderStyleEditor.PartsOf(key);
            return sample.Parts.Where(p => parts.Contains(p.Part)).ToList();
        }

        internal override IReadOnlyList<CadStyleMeasure> Measures(CadStyleSample sample, string key)
        {
            CadMLeaderStyleSettings settings = _settings();
            if (settings == null)
            {
                return Array.Empty<CadStyleMeasure>();
            }

            // Labelled in the field's own words and value, as the dialog shows them: "Landing gap = 2".
            string field = _labelOf(key);
            string value = _valueText(key);
            return CadMLeaderStyleEditor
                .Measures(sample, settings, key)
                .Select(m =>
                    string.IsNullOrEmpty(field) || string.IsNullOrEmpty(value) || IsGhost(m.Key)
                        ? m
                        : new CadStyleMeasure(m.Key, m.From, m.To, m.Angle, field + " = " + value)
                )
                .ToList();
        }

        internal override string KeyText(string key) => _labelOf(key) ?? key;

        internal override string Chip(
            CadStyleSample sample,
            string key,
            IReadOnlyList<CadStyleSamplePart> lit,
            IReadOnlyList<CadStyleMeasure> measures
        )
        {
            switch (key)
            {
                case "BreakSize":
                    return "Shows where a leader is broken (DIMBREAK)";
                case "Annotative":
                case "Scale":
                    return "Every part · " + (_labelOf(key) ?? key) + " = " + _valueText(key);
                case "MaxLeaderPoints":
                case "SecondSegmentAngle":
                    return (_labelOf(key) ?? key) + " = " + _valueText(key);
                default:
                    return null;
            }
        }
    }
}
