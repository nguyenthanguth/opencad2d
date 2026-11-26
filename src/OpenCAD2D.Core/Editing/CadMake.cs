using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using GeometryHelper.Dxf;
using GeometryHelper.Geometry;

namespace OpenCAD2D
{
    /// <summary>
    /// The arguments of one <see cref="CadMake.Run"/> call: names with a text, a number, a yes or no, a list of texts, a list of
    /// numbers or a table of texts, which the app's Remote channel carries as JSON and a drawing worked on in this process
    /// takes as they are.
    /// </summary>
    public sealed class CadMakeArgs
    {
        private readonly Dictionary<string, object> _values = new Dictionary<string, object>(StringComparer.Ordinal);

        /// <summary>Gets the arguments set, by name.</summary>
        public IReadOnlyDictionary<string, object> Values => _values;

        /// <summary>Sets an argument: a <see cref="string"/>, a <see cref="double"/>, a <see cref="bool"/>, a <c>string[]</c>, a <c>double[]</c> or a <c>string[][]</c>; null leaves it out.</summary>
        /// <param name="name">The name.</param>
        /// <param name="value">The value.</param>
        /// <returns>These arguments, so that a call is made in one chain.</returns>
        /// <exception cref="ArgumentNullException">Thrown when the name is null.</exception>
        /// <exception cref="ArgumentException">Thrown when the value is of a kind the arguments do not carry.</exception>
        public CadMakeArgs Set(string name, object value)
        {
            if (name == null)
            {
                throw new ArgumentNullException(nameof(name));
            }

            if (value == null)
            {
                _values.Remove(name);
                return this;
            }

            if (
                !(
                    value is string
                    || value is double
                    || value is bool
                    || value is string[]
                    || value is double[]
                    || value is string[][]
                )
            )
            {
                throw new ArgumentException(
                    "An argument is a text, a number, a bool or a list of them.",
                    nameof(value)
                );
            }

            _values[name] = value;
            return this;
        }

        /// <summary>Gets a text; the fallback where the argument is not given.</summary>
        public string Text(string name, string fallback = null) =>
            _values.TryGetValue(name, out object value) && value is string text ? text : fallback;

        /// <summary>Gets a number; null where the argument is not given.</summary>
        public double? Number(string name) =>
            _values.TryGetValue(name, out object value) && value is double number ? number : (double?)null;

        /// <summary>Gets a yes or no; the fallback where the argument is not given.</summary>
        public bool Flag(string name, bool fallback = false) =>
            _values.TryGetValue(name, out object value) && value is bool flag ? flag : fallback;

        /// <summary>Gets a list of texts; an empty list where the argument is not given.</summary>
        public IReadOnlyList<string> Texts(string name) =>
            _values.TryGetValue(name, out object value) && value is string[] texts ? texts : Array.Empty<string>();

        /// <summary>Gets a list of numbers; an empty list where the argument is not given.</summary>
        public IReadOnlyList<double> Numbers(string name) =>
            _values.TryGetValue(name, out object value) && value is double[] numbers ? numbers : Array.Empty<double>();

        /// <summary>Gets a table of texts, row by row; an empty table where the argument is not given.</summary>
        public IReadOnlyList<string[]> Rows(string name) =>
            _values.TryGetValue(name, out object value) && value is string[][] rows ? rows : Array.Empty<string[]>();
    }

    /// <summary>
    /// The edits a program outside the command line makes that no command makes in one call: the Properties palette's rows
    /// set on objects, the words of a text changed, the cells of a table filled, a layout's sheet, a layer renamed or deleted, a
    /// viewport's scale and frozen layers. Each is one undo step of the document, made by the code the presenter runs, so a
    /// drawing worked on in this process (<see cref="Run"/>) and the app's (the Remote channel's <c>make</c> request, which
    /// calls this) do the same.
    /// </summary>
    /// <remarks>
    /// <list type="table">
    /// <listheader><term>what</term><description>arguments, and what comes back</description></listheader>
    /// <item><term>set_rows</term><description><c>handles</c>, <c>rows</c> (pairs of a row's label and the text typed into it); the handles.</description></item>
    /// <item><term>edit_text</term><description><c>handle</c> of a TEXT or an MTEXT and any of <c>contents</c>, <c>height</c>, <c>rotation</c> (degrees), <c>style</c>, <c>justify</c> (as <c>MiddleCenter</c>); the handle.</description></item>
    /// <item><term>table_cells</term><description><c>handle</c> of a table, <c>cells</c> (rows of texts from the top left cell; an empty text leaves its cell); the handle.</description></item>
    /// <item><term>page_setup</term><description><c>layout</c>, <c>paper</c> (a sheet of <see cref="CadPaper.Standard"/> as <c>ISO A3</c>, or <c>420x297</c> in millimetres), <c>landscape</c>; the layout's name.</description></item>
    /// <item><term>rename_layer</term><description><c>name</c>, <c>new_name</c>; the new name.</description></item>
    /// <item><term>delete_layer</term><description><c>name</c>, and <c>move_to</c> to take the objects on it to another layer first; the name.</description></item>
    /// <item><term>viewport_view</term><description><c>handle</c> of a viewport, <c>scale</c> (the n of 1:n; 0.5 is 2:1) and <c>center</c> ([x, y] of model space); the handle.</description></item>
    /// <item><term>viewport_freeze</term><description><c>handle</c> of a viewport, <c>layers</c>, <c>frozen</c>; the handle.</description></item>
    /// <item><term>handles</term><description><c>layout</c> (Model where left out); the handles of every object of the layout in the order it draws them, those of kinds a query cannot carry (a viewport) among them.</description></item>
    /// <item><term>set_transparency</term><description><c>handles</c>, <c>percent</c> (0 solid to 90, or -1 for ByLayer); the handles.</description></item>
    /// <item><term>set_attribute</term><description><c>handle</c> of a block reference, <c>tag</c>, <c>value</c>; the handle.</description></item>
    /// <item><term>current_layer</term><description><c>name</c>; the name.</description></item>
    /// <item><term>current_style</term><description><c>kind</c> (<c>mleader</c> or <c>table</c>) and <c>name</c>; the name.</description></item>
    /// </list>
    /// </remarks>
    public static class CadMake
    {
        private const string Step = "MCP";

        /// <summary>Makes one of the edits listed in the remarks.</summary>
        /// <param name="document">The drawing.</param>
        /// <param name="what">The edit.</param>
        /// <param name="args">Its arguments.</param>
        /// <returns>What the edit gives back, as the remarks say.</returns>
        /// <exception cref="ArgumentNullException">Thrown when an argument of the call is null.</exception>
        /// <exception cref="ArgumentException">Thrown when the edit is unknown or an argument is missing or wrong.</exception>
        /// <exception cref="InvalidOperationException">Thrown when the drawing refuses the edit (a locked layer, a name taken); nothing is changed.</exception>
        public static IReadOnlyList<string> Run(CadDocument document, string what, CadMakeArgs args)
        {
            if (document == null)
            {
                throw new ArgumentNullException(nameof(document));
            }

            if (what == null)
            {
                throw new ArgumentNullException(nameof(what));
            }

            if (args == null)
            {
                throw new ArgumentNullException(nameof(args));
            }

            switch (what)
            {
                case "set_rows":
                    return SetRows(document, args.Texts("handles"), args.Rows("rows"));
                case "edit_text":
                    return EditText(document, args);
                case "table_cells":
                    return TableCells(document, args);
                case "page_setup":
                    return PageSetup(document, args);
                case "rename_layer":
                    return One(
                        Check(CadLayerEditor.Rename(document, Required(args, "name"), Required(args, "new_name"))),
                        Required(args, "new_name")
                    );
                case "delete_layer":
                    return DeleteLayer(document, args);
                case "viewport_view":
                    return ViewportView(document, args);
                case "viewport_freeze":
                    return ViewportFreeze(document, args);
                case "current_style":
                    return CurrentStyle(document, args);
                case "current_layer":
                    return One(
                        Check(CadLayerEditor.SetCurrent(document, Required(args, "name"))),
                        Required(args, "name")
                    );
                case "set_attribute":
                    return SetAttribute(document, args);
                case "set_transparency":
                    return SetTransparency(document, args);
                case "handles":
                    return Handles(document, args.Text("layout"));
                default:
                    throw new ArgumentException("OpenCAD2D does not know the edit " + what + ".", nameof(what));
            }
        }

        private static string Required(CadMakeArgs args, string name) =>
            string.IsNullOrWhiteSpace(args.Text(name))
                ? throw new ArgumentException("The edit needs " + name + ".")
                : args.Text(name).Trim();

        private static IReadOnlyList<string> One(object ignored, string value) => new[] { value };

        private static object Check(CadEditResult result)
        {
            if (!result.Succeeded)
            {
                throw new InvalidOperationException(result.Message);
            }

            return null;
        }

        /// <summary>Runs work as one undo step, taking all of it back where it fails.</summary>
        private static void Grouped(CadDocument document, Action work)
        {
            document.BeginGroup(Step);

            try
            {
                work();
            }
            catch
            {
                document.CancelGroup();
                throw;
            }

            document.EndGroup();
        }

        private static DxfEntity Existing(CadDocument document, string handle)
        {
            DxfEntity entity = string.IsNullOrWhiteSpace(handle) ? null : document.FindEntity(handle.Trim());

            if (entity == null || entity.IsErased)
            {
                throw new ArgumentException("The drawing " + document.Title + " has no object " + handle + ".");
            }

            return entity;
        }

        private static IReadOnlyList<string> SetRows(
            CadDocument document,
            IReadOnlyList<string> handles,
            IReadOnlyList<string[]> rows
        )
        {
            if (handles.Count == 0 || rows.Count == 0)
            {
                throw new ArgumentException("Give the handles and the rows to set.");
            }

            foreach (string handle in handles)
            {
                Existing(document, handle);
            }

            Grouped(document, () =>
            {
                foreach (string[] row in rows)
                {
                    if (row.Length != 2)
                    {
                        throw new ArgumentException("A row is a label and a value.");
                    }

                    Check(CadPropertyEditor.Set(document, handles.ToList(), row[0], row[1]));
                }
            });
            return handles;
        }

        private static string Degrees(double value) => value.ToString("R", CultureInfo.InvariantCulture);

        private static IReadOnlyList<string> EditText(CadDocument document, CadMakeArgs args)
        {
            string handle = Required(args, "handle");
            DxfEntity entity = Existing(document, handle);
            bool single = entity is DxfDBText;

            if (!single && !(entity is DxfMText))
            {
                throw new ArgumentException(
                    "The object " + handle + " is a " + CadDocument.KindName(entity) + ", not a TEXT or an MTEXT."
                );
            }

            var rows = new List<string[]>();

            if (args.Text("contents") != null)
            {
                rows.Add(new[] { "Contents", args.Text("contents") });
            }

            if (args.Number("height").HasValue)
            {
                rows.Add(new[] { single ? "Height" : "Text height", Degrees(args.Number("height").Value) });
            }

            if (args.Number("rotation").HasValue)
            {
                rows.Add(new[] { "Rotation", Degrees(args.Number("rotation").Value) });
            }

            string style = args.Text("style");
            string justify = args.Text("justify");

            if (style != null && !document.Database.TextStyleTable.Has(style))
            {
                throw new ArgumentException("The drawing has no text style " + style + ".");
            }

            DxfAttachmentPoint? place = null;

            if (justify != null)
            {
                if (!Enum.TryParse(justify.Replace("_", string.Empty), true, out DxfAttachmentPoint parsed))
                {
                    throw new ArgumentException(
                        "The justification is one of TopLeft, TopCenter, TopRight, MiddleLeft, MiddleCenter, MiddleRight, BottomLeft, BottomCenter, BottomRight."
                    );
                }

                place = parsed;
            }

            if (rows.Count == 0 && style == null && place == null)
            {
                throw new ArgumentException("Give contents, height, rotation, style or justify to set.");
            }

            Grouped(document, () =>
            {
                if (rows.Count > 0)
                {
                    SetRows(document, new[] { handle }, rows);
                }

                if (style != null || place != null)
                {
                    CadEditResult done = document.ApplyEdit(
                        Step,
                        new[] { new CadEditRecord(CadEditRecordKind.Reshaped, handle, "Text") },
                        scope =>
                        {
                            DxfEntity live = scope.Document.FindEntity(handle);

                            if (live is DxfDBText text)
                            {
                                if (style != null)
                                {
                                    text.TextStyleName = style;
                                }

                                if (place.HasValue)
                                {
                                    text.Justify = place.Value;
                                }
                            }
                            else if (live is DxfMText mtext)
                            {
                                if (style != null)
                                {
                                    mtext.TextStyleName = style;
                                }

                                if (place.HasValue)
                                {
                                    mtext.Attachment = place.Value;
                                }
                            }
                        }
                    );
                    Check(done);
                }
            });
            return new[] { handle };
        }

        private static IReadOnlyList<string> TableCells(CadDocument document, CadMakeArgs args)
        {
            string handle = Required(args, "handle");

            if (!(Existing(document, handle) is DxfTable table))
            {
                throw new ArgumentException("The object " + handle + " is not a table.");
            }

            IReadOnlyList<string[]> rows = args.Rows("cells");

            if (rows.Count > table.NumRows || rows.Any(r => r.Length > table.NumColumns))
            {
                throw new ArgumentException(
                    "The table has "
                        + table.NumRows
                        + " rows and "
                        + table.NumColumns
                        + " columns: the cells given do not fit."
                );
            }

            Grouped(document, () =>
            {
                for (int r = 0; r < rows.Count; r++)
                {
                    for (int c = 0; c < rows[r].Length; c++)
                    {
                        if (!string.IsNullOrEmpty(rows[r][c]))
                        {
                            Check(CadTableEditor.SetText(document, handle, r, c, rows[r][c]));
                        }
                    }
                }
            });
            return new[] { handle };
        }

        private static CadSpace Layout(CadDocument document, string name)
        {
            CadSpace space = document.Spaces.FirstOrDefault(s =>
                !s.IsBlockEditor && !s.IsModel && string.Equals(s.Name, name, StringComparison.OrdinalIgnoreCase)
            );
            return space
                ?? throw new ArgumentException(
                    "The drawing " + document.Title + " has no layout named " + name + " (Model has no sheet)."
                );
        }

        /// <summary>
        /// Finds a sheet by its name (<c>ISO A3</c>, letters of either case, spaces optional) or makes one from a size in millimetres
        /// (<c>420x297</c>, each side 50 to 20 000), with margins of 5.
        /// </summary>
        /// <param name="paper">The sheet.</param>
        /// <returns>The sheet.</returns>
        /// <exception cref="ArgumentException">Thrown when the text is neither.</exception>
        public static CadPaper Paper(string paper)
        {
            if (string.IsNullOrWhiteSpace(paper))
            {
                throw new ArgumentException(
                    "The paper is one of "
                        + string.Join(", ", CadPaper.Standard.Select(p => p.Name))
                        + ", or a size in millimetres as 420x297."
                );
            }

            paper = paper.Trim();
            CadPaper found = CadPaper.Standard.FirstOrDefault(p =>
                string.Equals(p.Name, paper, StringComparison.OrdinalIgnoreCase)
                || string.Equals(
                    p.Name.Replace(" ", string.Empty),
                    paper.Replace(" ", string.Empty),
                    StringComparison.OrdinalIgnoreCase
                )
            );

            if (found == null)
            {
                string[] parts = paper.ToLowerInvariant().Split('x');

                if (
                    parts.Length != 2
                    || !double.TryParse(parts[0], NumberStyles.Float, CultureInfo.InvariantCulture, out double width)
                    || !double.TryParse(parts[1], NumberStyles.Float, CultureInfo.InvariantCulture, out double height)
                    || !(width >= 50.0 && height >= 50.0 && width <= 20000.0 && height <= 20000.0)
                )
                {
                    throw new ArgumentException(
                        "The paper is one of "
                            + string.Join(", ", CadPaper.Standard.Select(p => p.Name))
                            + ", or a size in millimetres as 420x297."
                    );
                }

                found = new CadPaper(width + " x " + height, width, height, 5.0, 5.0, 5.0, 5.0);
            }

            return found;
        }

        private static IReadOnlyList<string> PageSetup(CadDocument document, CadMakeArgs args)
        {
            CadSpace space = Layout(document, Required(args, "layout"));
            CadPaper found = Paper(Required(args, "paper"));
            CadPlotSettings settings = CadPlotSettings.Read(document, space);
            settings.Paper = found;
            settings.Landscape = args.Flag("landscape", true);
            settings.Area = CadPlotArea.Layout;
            settings.FitToPaper = false;
            settings.PaperUnits = 1.0;
            settings.DrawingUnits = 1.0;
            Check(settings.ApplyTo(document, space));
            return new[] { space.Name };
        }

        private static IReadOnlyList<string> DeleteLayer(CadDocument document, CadMakeArgs args)
        {
            string name = Required(args, "name");
            string target = args.Text("move_to");

            if (string.IsNullOrWhiteSpace(target))
            {
                return One(Check(CadLayerEditor.Delete(document, name)), name);
            }

            return One(Check(CadLayerEditor.Merge(document, new[] { name }, target.Trim())), name);
        }

        private static DxfViewport Viewport(CadDocument document, string handle)
        {
            if (!(Existing(document, handle) is DxfViewport viewport) || viewport.Number == 1)
            {
                throw new ArgumentException(
                    "The object " + handle + " is not a viewport of a layout that shows model space."
                );
            }

            return viewport;
        }

        private static IReadOnlyList<string> ViewportView(CadDocument document, CadMakeArgs args)
        {
            string handle = Required(args, "handle");
            DxfViewport viewport = Viewport(document, handle);
            double? scale = args.Number("scale");

            if (!scale.HasValue || !(scale.Value > 0.0) || double.IsInfinity(scale.Value))
            {
                throw new ArgumentException("The scale is the n of 1:n, a number above nought.");
            }

            IReadOnlyList<double> at = args.Numbers("center");

            if (at.Count != 0 && at.Count != 2)
            {
                throw new ArgumentException("The centre is [x, y].");
            }

            GeoPoint2 center = at.Count == 2 ? new GeoPoint2(at[0], at[1]) : viewport.ViewCenter;
            Check(document.SetViewportView(handle, center, viewport.Height * scale.Value, "MVIEW"));
            return new[] { handle };
        }

        private static IReadOnlyList<string> ViewportFreeze(CadDocument document, CadMakeArgs args)
        {
            string handle = Required(args, "handle");
            Viewport(document, handle);
            IReadOnlyList<string> layers = args.Texts("layers");

            if (layers.Count == 0)
            {
                throw new ArgumentException("Give the layers to freeze or thaw.");
            }

            Check(
                CadViewportLayerEditor.SetFrozen(
                    document,
                    new[] { handle },
                    layers,
                    args.Flag("frozen", true),
                    "VPLAYER"
                )
            );
            return new[] { handle };
        }

        private static IReadOnlyList<string> Handles(CadDocument document, string layout)
        {
            CadSpace space =
                string.IsNullOrWhiteSpace(layout) || string.Equals(layout, "Model", StringComparison.OrdinalIgnoreCase)
                    ? document.ModelSpace
                    : Layout(document, layout.Trim());
            return space
                .Block.Where(e => !e.IsErased && !string.IsNullOrEmpty(e.Handle))
                .Select(e => e.Handle)
                .ToList();
        }

        private static IReadOnlyList<string> SetTransparency(CadDocument document, CadMakeArgs args)
        {
            IReadOnlyList<string> handles = args.Texts("handles");
            double percent = args.Number("percent") ?? throw new ArgumentException("The edit needs percent.");

            if (handles.Count == 0 || percent < -1.0 || percent > 90.0 || percent != Math.Floor(percent))
            {
                throw new ArgumentException(
                    "Give handles and a whole transparency of 0 to 90 per cent, or -1 for ByLayer."
                );
            }

            foreach (string handle in handles)
            {
                DxfEntity entity = Existing(document, handle);

                if (CadLayerEditor.IsLocked(document.Database, entity.Layer))
                {
                    throw new InvalidOperationException("The object " + handle + " is on a locked layer.");
                }
            }

            DxfTransparency transparency =
                percent < 0.0
                    ? DxfTransparency.ByLayer
                    : new DxfTransparency((byte)Math.Floor(255.0 * (100.0 - percent) / 100.0));
            Check(
                document.ApplyEdit(
                    Step,
                    handles
                        .Select(h => new CadEditRecord(CadEditRecordKind.PropertyChanged, h, "Transparency"))
                        .ToList(),
                    scope =>
                    {
                        foreach (string handle in handles)
                        {
                            scope.Document.FindEntity(handle).Transparency = transparency;
                        }
                    }
                )
            );
            return handles;
        }

        private static IReadOnlyList<string> SetAttribute(CadDocument document, CadMakeArgs args)
        {
            string handle = Required(args, "handle");
            string tag = Required(args, "tag");
            string value = args.Text("value") ?? throw new ArgumentException("The edit needs value.");

            if (!(Existing(document, handle) is DxfBlockReference insert))
            {
                throw new ArgumentException("The object " + handle + " is not a block reference.");
            }

            if (!insert.AttributeCollection.Any(a => string.Equals(a.Tag, tag, StringComparison.OrdinalIgnoreCase)))
            {
                throw new ArgumentException(
                    "The block reference "
                        + handle
                        + " has no attribute "
                        + tag
                        + ": it has "
                        + string.Join(
                            ", ",
                            insert
                                .AttributeCollection.Select(a => a.Tag)
                                .OrderBy(t => t, StringComparer.OrdinalIgnoreCase)
                                .ThenBy(t => t, StringComparer.Ordinal)
                        )
                        + "."
                );
            }

            if (CadLayerEditor.IsLocked(document.Database, insert.Layer))
            {
                throw new InvalidOperationException("The block reference " + handle + " is on a locked layer.");
            }

            Check(
                document.ApplyEdit(
                    Step,
                    new[] { new CadEditRecord(CadEditRecordKind.Reshaped, handle, "Attribute") },
                    scope =>
                    {
                        var live = (DxfBlockReference)scope.Document.FindEntity(handle);

                        foreach (DxfAttributeReference a in live.AttributeCollection)
                        {
                            if (string.Equals(a.Tag, tag, StringComparison.OrdinalIgnoreCase))
                            {
                                a.TextString = value;
                            }
                        }
                    }
                )
            );
            return new[] { handle };
        }

        private static IReadOnlyList<string> CurrentStyle(CadDocument document, CadMakeArgs args)
        {
            string name = Required(args, "name");

            switch (Required(args, "kind").ToLowerInvariant())
            {
                case "mleader":
                    return One(Check(CadMLeaderStyleEditor.SetCurrent(document, name)), name);
                case "table":
                    return One(Check(CadTableStyleEditor.SetCurrent(document, name)), name);
                default:
                    throw new ArgumentException("The kind of style is mleader or table.");
            }
        }
    }
}
