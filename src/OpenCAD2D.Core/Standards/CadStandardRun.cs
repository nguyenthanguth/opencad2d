using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using GeometryHelper.Dxf;

namespace OpenCAD2D
{
    /// <summary>
    /// One check of a drawing against a standard: the rules about objects and tables, then the rules about words on paper, which
    /// are run once for each view of the drawing (a layout's own paper, each viewport of a layout, or model space on its own).
    /// </summary>
    internal sealed class CadStandardRun
    {
        private const int LayerName = 0;
        private const int LayerProperties = 1;
        private const int LayerZero = 2;
        private const int ByLayerColor = 3;
        private const int ByLayerLinetype = 4;
        private const int ByLayerLineweight = 5;
        private const int TextStyle = 6;
        private const int TextFont = 7;
        private const int DimStyle = 8;
        private const int DimFont = 9;
        private const int TextHeight = 10;
        private const int GapWordLine = 11;
        private const int GapWordWord = 12;
        private const int DimChain = 13;

        // What a difference of two numbers a hair over a tolerance it is meant to equal (3.51 - 3.5 against 0.01) is allowed.
        private const double Rounding = 1E-9;

        private readonly CadDocument _document;
        private readonly CadStandard _standard;
        private readonly double _modelScale;
        private readonly double _tolerance;
        private readonly bool[] _on;
        private readonly HashSet<string>[] _subjects;
        private readonly HashSet<string>[] _offenders;
        private readonly List<CadViolation> _found = new List<CadViolation>();
        private readonly List<double> _severity = new List<double>();
        private readonly Dictionary<string, int> _slots = new Dictionary<string, int>(StringComparer.Ordinal);
        private readonly Dictionary<string, bool> _annotative = new Dictionary<string, bool>(StringComparer.Ordinal);

        public CadStandardRun(CadDocument document, CadStandard standard, double modelScale, double tolerance)
        {
            _document = document;
            _standard = standard;
            _modelScale = modelScale;
            _tolerance = tolerance;
            int rules = CadStandards.RuleIds.Count;
            _on = new bool[rules];
            _subjects = new HashSet<string>[rules];
            _offenders = new HashSet<string>[rules];

            for (int i = 0; i < rules; i++)
            {
                _on[i] = standard.Runs(CadStandards.RuleIds[i]);
                _subjects[i] = new HashSet<string>(StringComparer.Ordinal);
                _offenders[i] = new HashSet<string>(StringComparer.Ordinal);
            }
        }

        public CadStandardResult Run()
        {
            foreach (CadSpace space in _document.Spaces)
            {
                ObjectRules(space);
            }

            TableRules();

            if (_on[TextHeight] || _on[GapWordLine] || _on[GapWordWord] || _on[DimChain])
            {
                foreach (View view in Views())
                {
                    if (_on[TextHeight])
                    {
                        Heights(view);
                    }

                    if (_on[GapWordLine] || _on[GapWordWord])
                    {
                        Gaps(view);
                    }

                    if (_on[DimChain])
                    {
                        Chains(view);
                    }
                }
            }

            return Result();
        }

        // ---- the result ---------------------------------------------------------------------------------------------

        private CadStandardResult Result()
        {
            var rules = new List<CadRuleScore>();

            for (int i = 0; i < _on.Length; i++)
            {
                if (!_on[i])
                {
                    continue;
                }

                int subjects = _subjects[i].Count;
                int offenders = Math.Min(_offenders[i].Count, subjects);
                double score = subjects == 0 ? 1.0 : 1.0 - (double)offenders / subjects;
                rules.Add(new CadRuleScore(CadStandards.RuleIds[i], score, subjects, offenders, _standard.Weights[i]));
            }

            var order = _found.ToList();
            order.Sort(Compare);
            return new CadStandardResult(_standard.Name, rules, order);
        }

        private static int Compare(CadViolation a, CadViolation b)
        {
            int result = CadStandards.IndexOf(a.Rule).CompareTo(CadStandards.IndexOf(b.Rule));

            if (result == 0)
            {
                result = CompareHandles(a.Handle, b.Handle);
            }

            if (result == 0)
            {
                result = CompareHandles(a.Other, b.Other);
            }

            if (result == 0)
            {
                result = string.CompareOrdinal(a.Space, b.Space);
            }

            if (result == 0)
            {
                result = CompareHandles(a.Through, b.Through);
            }

            if (result == 0)
            {
                result = a.Value.CompareTo(b.Value);
            }

            return result != 0 ? result : string.CompareOrdinal(a.Measured, b.Measured);
        }

        /// <summary>Orders handles as the hexadecimal numbers they are (by length, then by letters); a missing handle first.</summary>
        private static int CompareHandles(string a, string b)
        {
            if (a == null || b == null)
            {
                return (a == null ? 0 : 1) - (b == null ? 0 : 1);
            }

            return a.Length != b.Length ? a.Length.CompareTo(b.Length) : string.CompareOrdinal(a, b);
        }

        /// <summary>Notes a violation. Of two with the same rule and objects (one seen in two viewports) the more severe stays.</summary>
        private void Add(int rule, CadViolation violation, double severity, string second = null)
        {
            _offenders[rule].Add(violation.Handle);

            if (second != null)
            {
                _offenders[rule].Add(second);
            }

            // A layer with two properties wrong is two violations; the rest are told apart by the objects alone, so that one seen
            // through two viewports is one.
            string key =
                violation.Rule
                + "|"
                + violation.Handle
                + "|"
                + violation.Other
                + (rule == LayerProperties ? "|" + violation.Measured : "");

            if (_slots.TryGetValue(key, out int slot))
            {
                if (severity > _severity[slot])
                {
                    _found[slot] = violation;
                    _severity[slot] = severity;
                }

                return;
            }

            _slots[key] = _found.Count;
            _found.Add(violation);
            _severity.Add(severity);
        }

        private static string Text(double number) => number.ToString("0.######", CultureInfo.InvariantCulture);

        private readonly Dictionary<CadSpace, CadSpatialIndex> _indexes = new Dictionary<CadSpace, CadSpatialIndex>();

        private CadSpatialIndex IndexOf(CadSpace space)
        {
            if (!_indexes.TryGetValue(space, out CadSpatialIndex index))
            {
                index = CadSpatialIndex.Of(_document.GetDisplay(space));
                _indexes[space] = index;
            }

            return index;
        }

        private double[] BoxOf(CadSpatialIndex index, string handle)
        {
            int slot = index.Find(handle);

            if (slot < 0)
            {
                return null;
            }

            double[] box = index.HandleBox;

            if (box[4 * slot] > box[4 * slot + 2] || box[4 * slot + 1] > box[4 * slot + 3])
            {
                return null;
            }

            return new[] { box[4 * slot], box[4 * slot + 1], box[4 * slot + 2], box[4 * slot + 3] };
        }

        // ---- layers, looks and styles of the objects ----------------------------------------------------------------

        private void ObjectRules(CadSpace space)
        {
            bool any =
                _on[LayerName]
                || _on[LayerZero]
                || _on[ByLayerColor]
                || _on[ByLayerLinetype]
                || _on[ByLayerLineweight]
                || _on[TextStyle]
                || _on[DimStyle];

            if (!any)
            {
                return;
            }

            CadSpatialIndex index = IndexOf(space);

            foreach (DxfEntity entity in space.Block)
            {
                string handle = entity.IsErased ? null : entity.Handle;

                if (handle == null || (entity is DxfViewport viewport && viewport.Number <= 1 && viewport.Id <= 1))
                {
                    continue;
                }

                ObjectRule(space, index, entity, handle);
            }
        }

        private void ObjectRule(CadSpace space, CadSpatialIndex index, DxfEntity entity, string handle)
        {
            string layer = entity.Layer;

            if (_on[LayerName] && !(_standard.AllowLayer0 != null && layer == "0") && layer != "Defpoints")
            {
                _subjects[LayerName].Add(handle);

                if (!_standard.Allowed.Any(e => e.Match.IsMatch(layer)))
                {
                    Add(
                        LayerName,
                        Violation(
                            LayerName,
                            handle,
                            space,
                            index,
                            layer,
                            string.Join(", ", _standard.Allowed.Select(e => e.Pattern))
                        ),
                        1.0
                    );
                }
            }

            if (_on[LayerZero])
            {
                _subjects[LayerZero].Add(handle);

                if (layer == "0")
                {
                    Add(LayerZero, Violation(LayerZero, handle, space, index, "0", "any layer but 0"), 1.0);
                }
            }

            if (_on[ByLayerColor])
            {
                _subjects[ByLayerColor].Add(handle);
                DxfColor color = entity.Color;

                if (!color.IsByLayer)
                {
                    Add(ByLayerColor, Violation(ByLayerColor, handle, space, index, ColorText(color), "ByLayer"), 1.0);
                }
            }

            if (_on[ByLayerLinetype])
            {
                _subjects[ByLayerLinetype].Add(handle);
                string linetype = entity.Linetype;

                if (!string.Equals(linetype, "ByLayer", StringComparison.OrdinalIgnoreCase))
                {
                    Add(ByLayerLinetype, Violation(ByLayerLinetype, handle, space, index, linetype, "ByLayer"), 1.0);
                }
            }

            if (_on[ByLayerLineweight])
            {
                _subjects[ByLayerLineweight].Add(handle);
                DxfLineWeight weight = entity.LineWeight;

                if (weight != DxfLineWeight.ByLayer)
                {
                    string measured =
                        (int)weight >= 0 ? Text((int)weight / 100.0) + " mm"
                        : weight == DxfLineWeight.ByBlock ? "ByBlock"
                        : "default";
                    Add(
                        ByLayerLineweight,
                        Violation(ByLayerLineweight, handle, space, index, measured, "ByLayer"),
                        1.0
                    );
                }
            }

            string style =
                entity is DxfDBText text ? text.TextStyleName
                : entity is DxfMText mtext ? mtext.TextStyleName
                : null;

            if (_on[TextStyle] && (entity is DxfDBText || entity is DxfMText))
            {
                style = string.IsNullOrEmpty(style) ? "Standard" : style;
                _subjects[TextStyle].Add(handle);

                if (!_standard.TextStyles.Any(e => e.Match.IsMatch(style)))
                {
                    Add(
                        TextStyle,
                        Violation(
                            TextStyle,
                            handle,
                            space,
                            index,
                            style,
                            string.Join(", ", _standard.TextStyles.Select(e => e.Pattern))
                        ),
                        1.0
                    );
                }
            }

            if (_on[DimStyle] && entity is DxfDimension dimension)
            {
                string name = string.IsNullOrEmpty(dimension.DimensionStyleName)
                    ? "Standard"
                    : dimension.DimensionStyleName;
                _subjects[DimStyle].Add(handle);

                if (!_standard.DimStyles.Any(e => e.Match.IsMatch(name)))
                {
                    Add(
                        DimStyle,
                        Violation(
                            DimStyle,
                            handle,
                            space,
                            index,
                            name,
                            string.Join(", ", _standard.DimStyles.Select(e => e.Pattern))
                        ),
                        1.0
                    );
                }
            }
        }

        private CadViolation Violation(
            int rule,
            string handle,
            CadSpace space,
            CadSpatialIndex index,
            string measured,
            string allowed
        ) =>
            new CadViolation(
                CadStandards.RuleIds[rule],
                handle,
                null,
                space.Name,
                null,
                BoxOf(index, handle),
                measured,
                allowed,
                double.NaN,
                double.NaN
            );

        private static string ColorText(DxfColor color)
        {
            if (color.IsByBlock)
            {
                return "ByBlock";
            }

            return color.IsByAci
                ? color.ColorIndex.ToString(CultureInfo.InvariantCulture)
                : "#" + color.Red.ToString("X2") + color.Green.ToString("X2") + color.Blue.ToString("X2");
        }

        // ---- the tables: layers, text styles, dimension styles ------------------------------------------------------

        private void TableRules()
        {
            if (_on[LayerProperties])
            {
                foreach (DxfLayerTableRecord record in _document.Database.LayerTable)
                {
                    LayerRecord(record);
                }
            }

            if (_on[TextFont])
            {
                foreach (DxfTextStyleTableRecord record in _document.Database.TextStyleTable)
                {
                    StyleEntryOf(_standard.TextStyles, record.Name, out CadStandard.StyleEntry entry);

                    if (entry?.Font != null)
                    {
                        FontRule(TextFont, record.Handle, record.Name, entry, record);
                    }
                }
            }

            if (_on[DimFont])
            {
                DxfDatabase database = _document.Database;

                foreach (DxfDimStyleTableRecord record in database.DimStyleTable)
                {
                    StyleEntryOf(_standard.DimStyles, record.Name, out CadStandard.StyleEntry entry);

                    if (entry?.Font == null)
                    {
                        continue;
                    }

                    DxfTextStyleTableRecord text = record.Dimtxsty
                        ?? (database.TextStyleTable.Has("Standard") ? database.TextStyleTable["Standard"] : null);
                    FontRule(DimFont, record.Handle, record.Name, entry, text);
                }
            }
        }

        private static void StyleEntryOf(
            List<CadStandard.StyleEntry> entries,
            string name,
            out CadStandard.StyleEntry entry
        )
        {
            entry = entries.FirstOrDefault(e => e.Match.IsMatch(name));
        }

        private void LayerRecord(DxfLayerTableRecord record)
        {
            CadStandard.LayerEntry entry = _standard.Allowed.FirstOrDefault(e => e.Match.IsMatch(record.Name));

            if (entry == null || (entry.Color == null && entry.Linetype == null && entry.Lineweight == null))
            {
                return;
            }

            string handle = record.Handle;
            _subjects[LayerProperties].Add(handle);

            if (entry.Color != null && (record.Color.ColorValue & 0xFFFFFF) != (entry.Color.ColorValue & 0xFFFFFF))
            {
                Mismatch(handle, "color " + ColorText(record.Color), "color " + ColorText(entry.Color));
            }

            string linetype = string.IsNullOrEmpty(record.LinetypeName) ? "Continuous" : record.LinetypeName;

            if (entry.Linetype != null && !string.Equals(linetype, entry.Linetype, StringComparison.OrdinalIgnoreCase))
            {
                Mismatch(handle, "linetype " + linetype, "linetype " + entry.Linetype);
            }

            if (entry.Lineweight != null)
            {
                int raw = (int)record.LineWeight;
                double weight = raw >= 0 ? raw / 100.0 : 0.25;

                if (Math.Abs(weight - entry.Lineweight.Value) > 0.005)
                {
                    Mismatch(
                        handle,
                        "lineweight " + Text(weight) + " mm",
                        "lineweight " + Text(entry.Lineweight.Value) + " mm"
                    );
                }
            }
        }

        private void Mismatch(string handle, string measured, string allowed) =>
            Add(
                LayerProperties,
                new CadViolation(
                    "layer-properties",
                    handle,
                    null,
                    string.Empty,
                    null,
                    null,
                    measured,
                    allowed,
                    double.NaN,
                    double.NaN
                ),
                1.0
            );

        private void FontRule(
            int rule,
            string handle,
            string name,
            CadStandard.StyleEntry entry,
            DxfTextStyleTableRecord style
        )
        {
            _subjects[rule].Add(handle);
            string typeFace = style?.Font.TypeFace ?? string.Empty;
            string file = style?.FileName ?? string.Empty;
            string wanted = FontKey(entry.Font);

            if (wanted == FontKey(typeFace) || wanted == FontKey(file))
            {
                return;
            }

            string found = typeFace.Length > 0 ? typeFace : file;
            Add(
                rule,
                new CadViolation(
                    CadStandards.RuleIds[rule],
                    handle,
                    null,
                    string.Empty,
                    null,
                    null,
                    found.Length > 0 ? found : "no font",
                    entry.Font,
                    double.NaN,
                    double.NaN
                ),
                1.0
            );
        }

        /// <summary>A font as it is compared: the name of its file or typeface without folder or extension, in lower case.</summary>
        private static string FontKey(string font)
        {
            string name = (font ?? string.Empty).Trim();

            if (name.Length == 0)
            {
                return string.Empty;
            }

            try
            {
                return Path.GetFileNameWithoutExtension(name).ToLowerInvariant();
            }
            catch (ArgumentException)
            {
                return name.ToLowerInvariant();
            }
        }

        // ---- views: where the words are seen on paper ---------------------------------------------------------------

        /// <summary>A display list as it is plotted: the factor to paper millimetres and, through a viewport, the window it shows.</summary>
        private sealed class View
        {
            public CadSpace Space;
            public DisplayList2 List;
            public CadSpatialIndex Index;
            public double Factor;
            public DisplayViewport2 Viewport;
            public string ViewportHandle;
            public bool OnItsOwn;

            /// <summary>Whether a thing with this box, on this layer, is on the paper of the view.</summary>
            public bool Shows(double minX, double minY, double maxX, double maxY, string layer)
            {
                if (Viewport == null)
                {
                    return true;
                }

                if (layer != null && Viewport.FrozenLayers.Contains(layer))
                {
                    return false;
                }

                Affine2 toPaper = Viewport.ModelToPaper;
                double lowX = double.PositiveInfinity;
                double lowY = double.PositiveInfinity;
                double highX = double.NegativeInfinity;
                double highY = double.NegativeInfinity;

                for (int i = 0; i < 4; i++)
                {
                    double x = toPaper.X(i % 2 == 0 ? minX : maxX, i < 2 ? minY : maxY);
                    double y = toPaper.Y(i % 2 == 0 ? minX : maxX, i < 2 ? minY : maxY);
                    lowX = Math.Min(lowX, x);
                    lowY = Math.Min(lowY, y);
                    highX = Math.Max(highX, x);
                    highY = Math.Max(highY, y);
                }

                DisplayBox frame = Viewport.Bounds;
                return highX >= frame.MinX && lowX <= frame.MaxX && highY >= frame.MinY && lowY <= frame.MaxY;
            }
        }

        private IEnumerable<View> Views()
        {
            var views = new List<View>();
            bool anyViewport = false;
            double mm = CadUnitScale.Of(_document.Database, out _);

            foreach (CadSpace space in _document.Spaces)
            {
                if (space.IsModel)
                {
                    continue;
                }

                DisplayList2 list = _document.GetDisplay(space);
                double paper = CadUnitScale.OfPaper(space.Layout);
                views.Add(
                    new View
                    {
                        Space = space,
                        List = list,
                        Index = IndexOf(space),
                        Factor = paper,
                    }
                );

                if (_modelScale > 0.0)
                {
                    continue;
                }

                IReadOnlyList<DisplayItem2> items = list.Items;

                for (int i = 0; i < list.Count; i++)
                {
                    if (items[i] is DisplayViewport2 viewport && viewport.Scale > 0.0)
                    {
                        anyViewport = true;
                        views.Add(
                            new View
                            {
                                Space = _document.ModelSpace,
                                List = viewport.Model,
                                Index = CadSpatialIndex.Of(viewport.Model),
                                Factor = viewport.Scale * paper,
                                Viewport = viewport,
                                ViewportHandle = viewport.Handle,
                            }
                        );
                    }
                }
            }

            if (_modelScale > 0.0 || !anyViewport)
            {
                DisplayList2 list = _document.GetDisplay(_document.ModelSpace);
                views.Insert(
                    0,
                    new View
                    {
                        Space = _document.ModelSpace,
                        List = list,
                        Index = IndexOf(_document.ModelSpace),
                        Factor = _modelScale > 0.0 ? _modelScale : mm,
                        OnItsOwn = true,
                    }
                );
            }

            return views;
        }

        // ---- the heights of words on paper --------------------------------------------------------------------------

        private sealed class Worst
        {
            public string Handle;
            public double Height;
            public double Nearest;
            public double Off;
            public DisplayBox Box;
        }

        private void Heights(View view)
        {
            var worst = new List<Worst>();
            var seen = new Dictionary<string, int>(StringComparer.Ordinal);
            IReadOnlyList<DisplayItem2> items = view.List.Items;

            for (int i = 0; i < view.List.Count; i++)
            {
                if (items[i] is DisplayText2 text && !string.IsNullOrEmpty(text.Handle))
                {
                    HeightOf(view, text, text.Handle, worst, seen);
                }
            }

            foreach (Worst each in worst)
            {
                if (each.Off <= _standard.HeightTolerance + Rounding)
                {
                    continue;
                }

                string allowed = string.Join(", ", _standard.Heights.Select(Text)) + " mm";
                Add(
                    TextHeight,
                    new CadViolation(
                        "text-height",
                        each.Handle,
                        null,
                        view.Space.Name,
                        view.ViewportHandle,
                        new[] { each.Box.MinX, each.Box.MinY, each.Box.MaxX, each.Box.MaxY },
                        Text(each.Height) + " mm",
                        allowed,
                        each.Height,
                        each.Nearest
                    ),
                    each.Off
                );
            }
        }

        private void HeightOf(
            View view,
            DisplayText2 text,
            string handle,
            List<Worst> worst,
            Dictionary<string, int> seen
        )
        {
            if (text.Parts != null)
            {
                foreach (DisplayItem2 part in text.Parts)
                {
                    if (part is DisplayText2 word)
                    {
                        HeightOf(view, word, handle, worst, seen);
                    }
                }

                return;
            }

            DisplayBox box = text.VisibleBounds;

            if (
                string.IsNullOrWhiteSpace(text.Text)
                || !(text.Height > 0.0)
                || box.IsEmpty
                || !view.Shows(box.MinX, box.MinY, box.MaxX, box.MaxY, text.Layer)
            )
            {
                return;
            }

            _subjects[TextHeight].Add(handle);
            double paper = text.Height * FactorFor(view, handle);
            double nearest = double.NaN;
            double off = double.PositiveInfinity;

            foreach (double allowed in _standard.Heights)
            {
                double gap = Math.Abs(paper - allowed);

                if (gap < off)
                {
                    off = gap;
                    nearest = allowed;
                }
            }

            if (!seen.TryGetValue(handle, out int slot))
            {
                seen[handle] = worst.Count;
                worst.Add(
                    new Worst
                    {
                        Handle = handle,
                        Height = paper,
                        Nearest = nearest,
                        Off = off,
                        Box = box,
                    }
                );
            }
            else if (off > worst[slot].Off)
            {
                worst[slot] = new Worst
                {
                    Handle = handle,
                    Height = paper,
                    Nearest = nearest,
                    Off = off,
                    Box = box,
                };
            }
        }

        /// <summary>
        /// The paper millimetres in a unit of the view's list for an object: the view's factor, except that an annotative object in
        /// model space on its own is at its own annotation scale (the scale the list draws it at, CANNOSCALE), so its paper size is
        /// the size it was made at.
        /// </summary>
        private double FactorFor(View view, string handle)
        {
            if (!view.OnItsOwn)
            {
                return view.Factor;
            }

            if (!_annotative.TryGetValue(handle, out bool annotative))
            {
                annotative = _document.FindEntity(handle)?.Annotative == DxfAnnotativeStates.True;
                _annotative[handle] = annotative;
            }

            if (!annotative)
            {
                return view.Factor;
            }

            DxfAnnotationScale scale = DxfObjectContextCollection.Find(
                _document.Database,
                _document.Database.Cannoscale
            );
            return scale == null ? view.Factor : scale.Scale * CadUnitScale.Of(_document.Database, out _);
        }

        // ---- the gaps around words ----------------------------------------------------------------------------------

        private void Gaps(View view)
        {
            CadSpatialIndex index = view.Index;
            double wordLine = _standard.WordLine;
            double wordWord = _standard.WordWord;
            double reach = Math.Max(_on[GapWordLine] ? wordLine : 0.0, _on[GapWordWord] ? wordWord : 0.0) / view.Factor;
            double slack = _tolerance / view.Factor;
            var corners = new double[index.LeafCount][];
            var shown = new sbyte[index.LeafCount];
            var hits = new List<int>();
            double[] box = index.LeafBox;

            for (int leaf = 0; leaf < index.LeafCount; leaf++)
            {
                if (index.LeafKind[leaf] != CadSpatialIndex.TextLeaf || !Visible(view, shown, leaf))
                {
                    continue;
                }

                string handle = index.Handles[index.LeafHandle[leaf]];

                if (_on[GapWordLine])
                {
                    _subjects[GapWordLine].Add(handle);
                }

                if (_on[GapWordWord])
                {
                    _subjects[GapWordWord].Add(handle);
                }

                hits.Clear();
                index.Tree.Search(
                    box[4 * leaf] - reach,
                    box[4 * leaf + 1] - reach,
                    box[4 * leaf + 2] + reach,
                    box[4 * leaf + 3] + reach,
                    hits
                );
                CadTextFrame frame = index.Frames[index.LeafFrame[leaf]];
                double[] mine = corners[leaf] ?? (corners[leaf] = CadStandardDistance.Corners(frame));

                foreach (int near in hits)
                {
                    int owner = index.LeafHandle[leaf];
                    int other = index.LeafHandle[near];

                    if (other == owner || !Visible(view, shown, near))
                    {
                        continue;
                    }

                    if (index.LeafKind[near] == CadSpatialIndex.TextLeaf)
                    {
                        if (_on[GapWordWord] && leaf < near)
                        {
                            double[] theirs = corners[near]
                                ?? (corners[near] = CadStandardDistance.Corners(index.Frames[index.LeafFrame[near]]));
                            Gap(
                                view,
                                GapWordWord,
                                wordWord,
                                owner,
                                other,
                                mine,
                                CadStandardDistance.FrameFrame(mine, theirs)
                            );
                        }
                    }
                    else if (_on[GapWordLine])
                    {
                        CadNearest nearest = Nearest(mine, index.LeafItem[near], slack);
                        Gap(view, GapWordLine, wordLine, owner, other, mine, nearest);
                    }
                }
            }
        }

        /// <summary>Whether a leaf is on the paper of the view, kept once for each leaf (1 yes, -1 no).</summary>
        private static bool Visible(View view, sbyte[] shown, int leaf)
        {
            if (shown[leaf] == 0)
            {
                CadSpatialIndex index = view.Index;
                double[] box = index.LeafBox;
                shown[leaf] = (sbyte)(
                    view.Shows(
                        box[4 * leaf],
                        box[4 * leaf + 1],
                        box[4 * leaf + 2],
                        box[4 * leaf + 3],
                        index.LeafItem[leaf].Layer
                    )
                        ? 1
                        : -1
                );
            }

            return shown[leaf] > 0;
        }

        private void Gap(View view, int rule, double least, int first, int second, double[] frame, CadNearest nearest)
        {
            double paper = nearest.Distance * view.Factor;

            if (!(paper < least - _tolerance))
            {
                return;
            }

            CadSpatialIndex index = view.Index;
            string one = index.Handles[first];
            string two = index.Handles[second];

            // Words of two objects are named by the one the list draws first.
            if (rule == GapWordWord && second < first)
            {
                string swap = one;
                one = two;
                two = swap;
            }

            double[] place =
            {
                Math.Min(Math.Min(Math.Min(frame[0], frame[2]), Math.Min(frame[4], frame[6])), nearest.Bx),
                Math.Min(Math.Min(Math.Min(frame[1], frame[3]), Math.Min(frame[5], frame[7])), nearest.By),
                Math.Max(Math.Max(Math.Max(frame[0], frame[2]), Math.Max(frame[4], frame[6])), nearest.Bx),
                Math.Max(Math.Max(Math.Max(frame[1], frame[3]), Math.Max(frame[5], frame[7])), nearest.By),
            };
            Add(
                rule,
                new CadViolation(
                    CadStandards.RuleIds[rule],
                    one,
                    two,
                    view.Space.Name,
                    view.ViewportHandle,
                    place,
                    Text(paper) + " mm",
                    "at least " + Text(least) + " mm",
                    paper,
                    least
                ),
                least - paper,
                rule == GapWordWord ? two : null
            );
        }

        /// <summary>The nearest approach of a word's frame to a curve of the display: a chain, some segments, an arc or a fill.</summary>
        private static CadNearest Nearest(double[] frame, DisplayItem2 item, double slack)
        {
            CadNearest best = CadNearest.None;

            switch (item)
            {
                case DisplayPolyline2 chain:
                    for (int i = 0; i + 1 < chain.Count && best.Distance > 0.0; i++)
                    {
                        best.Take(
                            CadStandardDistance.FrameSegment(
                                frame,
                                chain.VertexX(i),
                                chain.VertexY(i),
                                chain.VertexX(i + 1),
                                chain.VertexY(i + 1)
                            )
                        );
                    }

                    if (chain.Closed && chain.Count > 2)
                    {
                        best.Take(
                            CadStandardDistance.FrameSegment(
                                frame,
                                chain.VertexX(chain.Count - 1),
                                chain.VertexY(chain.Count - 1),
                                chain.VertexX(0),
                                chain.VertexY(0)
                            )
                        );
                    }

                    break;
                case DisplaySegments2 segments:
                    IReadOnlyList<double> all = segments.Xy;

                    for (int i = 0; i + 3 < all.Count && best.Distance > 0.0; i += 4)
                    {
                        best.Take(CadStandardDistance.FrameSegment(frame, all[i], all[i + 1], all[i + 2], all[i + 3]));
                    }

                    break;
                case DisplayArc2 arc:
                    double[] flat = arc.Flatten(Math.Max(slack, 1E-6));

                    for (int i = 0; 2 * i + 3 < flat.Length && best.Distance > 0.0; i++)
                    {
                        best.Take(
                            CadStandardDistance.FrameSegment(
                                frame,
                                flat[2 * i],
                                flat[2 * i + 1],
                                flat[2 * i + 2],
                                flat[2 * i + 3]
                            )
                        );
                    }

                    break;
                case DisplayFill2 fill:
                    foreach (double[] ring in fill.Rings)
                    {
                        int count = ring.Length / 2;

                        for (int i = 0; i < count && best.Distance > 0.0; i++)
                        {
                            int j = (i + 1) % count;
                            best.Take(
                                CadStandardDistance.FrameSegment(
                                    frame,
                                    ring[2 * i],
                                    ring[2 * i + 1],
                                    ring[2 * j],
                                    ring[2 * j + 1]
                                )
                            );
                        }
                    }

                    for (int i = 0; i < 4 && best.Distance > 0.0; i++)
                    {
                        if (fill.Contains(frame[2 * i], frame[2 * i + 1]))
                        {
                            best = new CadNearest
                            {
                                Distance = 0.0,
                                Ax = frame[2 * i],
                                Ay = frame[2 * i + 1],
                                Bx = frame[2 * i],
                                By = frame[2 * i + 1],
                            };
                        }
                    }

                    break;
            }

            return best;
        }

        // ---- dimension chains ---------------------------------------------------------------------------------------

        private sealed class Chained
        {
            public string Handle;
            public double Angle;
            public double X1;
            public double Y1;
            public double X2;
            public double Y2;
            public double LineX;
            public double LineY;
        }

        private void Chains(View view)
        {
            CadSpatialIndex index = view.Index;
            string[] kinds = index.KindsOf(_document);
            var dims = new List<Chained>();

            for (int slot = 0; slot < index.HandleCount; slot++)
            {
                if (kinds[slot] != "DIMENSION")
                {
                    continue;
                }

                DxfEntity entity = _document.FindEntity(index.Handles[slot]);
                GeometryHelper.Geometry.GeoPoint3 one;
                GeometryHelper.Geometry.GeoPoint3 two;
                GeometryHelper.Geometry.GeoPoint3 line;
                double angle;

                if (entity is DxfRotatedDimension rotated)
                {
                    one = rotated.XLine1Point;
                    two = rotated.XLine2Point;
                    line = rotated.DimLinePoint;
                    angle = rotated.Rotation;
                }
                else if (entity is DxfAlignedDimension aligned)
                {
                    one = aligned.XLine1Point;
                    two = aligned.XLine2Point;
                    line = aligned.DimLinePoint;
                    angle = Math.Atan2(two.Y - one.Y, two.X - one.X);
                }
                else
                {
                    continue;
                }

                double[] box = BoxOf(index, index.Handles[slot]);

                if (box == null || !view.Shows(box[0], box[1], box[2], box[3], entity.Layer))
                {
                    continue;
                }

                angle %= Math.PI;

                if (angle < 0.0)
                {
                    angle += Math.PI;
                }

                dims.Add(
                    new Chained
                    {
                        Handle = index.Handles[slot],
                        Angle = angle,
                        X1 = one.X,
                        Y1 = one.Y,
                        X2 = two.X,
                        Y2 = two.Y,
                        LineX = line.X,
                        LineY = line.Y,
                    }
                );
            }

            if (dims.Count >= 2)
            {
                ChainGroups(view, dims);
            }
        }

        private void ChainGroups(View view, List<Chained> dims)
        {
            double angleTolerance = 0.1 * Math.PI / 180.0;
            double touch = _standard.ChainTolerance / view.Factor;
            double reach = _standard.ChainReach / view.Factor;
            int[] order = Enumerable.Range(0, dims.Count).ToArray();
            Array.Sort(order, (a, b) =>
            {
                int result = dims[a].Angle.CompareTo(dims[b].Angle);
                return result != 0 ? result : a.CompareTo(b);
            });

            var groups = new List<List<int>>();

            foreach (int each in order)
            {
                if (groups.Count > 0 && dims[each].Angle - dims[groups[groups.Count - 1][0]].Angle <= angleTolerance)
                {
                    groups[groups.Count - 1].Add(each);
                }
                else
                {
                    groups.Add(new List<int> { each });
                }
            }

            foreach (List<int> group in groups)
            {
                if (group.Count >= 2)
                {
                    ChainGroup(view, dims, group, touch, reach);
                }
            }
        }

        private void ChainGroup(View view, List<Chained> dims, List<int> group, double touch, double reach)
        {
            group.Sort();
            double angle = dims[group[0]].Angle;
            double ux = Math.Cos(angle);
            double uy = Math.Sin(angle);
            var along = new double[group.Count][];

            for (int i = 0; i < group.Count; i++)
            {
                Chained dim = dims[group[i]];
                double p1 = dim.X1 * ux + dim.Y1 * uy;
                double p2 = dim.X2 * ux + dim.Y2 * uy;
                along[i] = new[] { Math.Min(p1, p2), Math.Max(p1, p2), -dim.LineX * uy + dim.LineY * ux };
            }

            // Union-find over dimensions that meet end to end and whose lines are within reach of each other sideways.
            var parent = Enumerable.Range(0, group.Count).ToArray();
            int[] byStart = Enumerable.Range(0, group.Count).ToArray();
            Array.Sort(byStart, (a, b) =>
            {
                int result = along[a][0].CompareTo(along[b][0]);
                return result != 0 ? result : a.CompareTo(b);
            });
            var starts = byStart.Select(i => along[i][0]).ToArray();

            for (int i = 0; i < group.Count; i++)
            {
                int at = 0;
                int high = starts.Length;

                while (at < high)
                {
                    int middle = (at + high) / 2;

                    if (starts[middle] < along[i][1] - touch)
                    {
                        at = middle + 1;
                    }
                    else
                    {
                        high = middle;
                    }
                }

                for (int k = at; k < starts.Length && starts[k] <= along[i][1] + touch; k++)
                {
                    int j = byStart[k];

                    if (j != i && Math.Abs(along[i][2] - along[j][2]) <= reach)
                    {
                        Union(parent, i, j);
                    }
                }
            }

            var members = new SortedDictionary<int, List<int>>();

            for (int i = 0; i < group.Count; i++)
            {
                int root = Find(parent, i);

                if (!members.TryGetValue(root, out List<int> list))
                {
                    members[root] = list = new List<int>();
                }

                list.Add(i);
            }

            CadSpatialIndex index = view.Index;

            foreach (List<int> chain in members.Values)
            {
                if (chain.Count < 2)
                {
                    continue;
                }

                var sides = chain.OrderBy(i => along[i][2]).ThenBy(i => i).ToList();
                int reference = sides[(sides.Count - 1) / 2];

                foreach (int i in chain)
                {
                    string handle = dims[group[i]].Handle;
                    _subjects[DimChain].Add(handle);
                    double off = Math.Abs(along[i][2] - along[reference][2]) * view.Factor;

                    if (off > _standard.ChainTolerance + Rounding && i != reference)
                    {
                        Add(
                            DimChain,
                            new CadViolation(
                                "dim-chain",
                                handle,
                                dims[group[reference]].Handle,
                                view.Space.Name,
                                view.ViewportHandle,
                                BoxOf(index, handle),
                                Text(off) + " mm off the line of the chain",
                                "within " + Text(_standard.ChainTolerance) + " mm",
                                off,
                                _standard.ChainTolerance
                            ),
                            off
                        );
                    }
                }
            }
        }

        private static int Find(int[] parent, int i)
        {
            while (parent[i] != i)
            {
                parent[i] = parent[parent[i]];
                i = parent[i];
            }

            return i;
        }

        private static void Union(int[] parent, int a, int b)
        {
            int ra = Find(parent, a);
            int rb = Find(parent, b);

            if (ra != rb)
            {
                parent[Math.Max(ra, rb)] = Math.Min(ra, rb);
            }
        }
    }
}
