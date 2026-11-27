using System;
using System.Collections.Generic;
using System.Linq;
using GeometryHelper.Dxf;
using GeometryHelper.Geometry;
using OpenCAD2D.Remote;

namespace OpenCAD2D.Mcp
{
    /// <summary>
    /// A drawing of a running OpenCAD2D worked on through its Remote API, the way a Python script outside the app does
    /// (<c>_remote.py</c>): the app does the work and keeps its own undo steps, and what is drawn is what its window shows.
    /// </summary>
    internal sealed partial class RemoteBackend : IDrawingBackend, IDrawingEditing
    {
        /// <summary>How long run_command waits for a command to end, in seconds, before it says the command is still waiting.</summary>
        private const int CommandSeconds = 10;

        /// <summary>Starts from a drawing of an app the session's connection is made to.</summary>
        public RemoteBackend(CadRemoteDocument document)
        {
            Document = document ?? throw new ArgumentNullException(nameof(document));
        }

        /// <summary>Gets the drawing of the app.</summary>
        public CadRemoteDocument Document { get; }

        public string Mode => "app";

        public string Name => Document.Name;

        public string Path => string.IsNullOrEmpty(Document.Path) ? null : Document.Path;

        public bool IsModified => Document.IsModified;

        public string CurrentLayout => Document.CurrentLayout;

        public string CurrentLayer => Document.CurrentLayer;

        public string CurrentDimensionStyle => Document.CurrentDimensionStyle;

        public IReadOnlyList<string> Layouts => Document.Layouts;

        private static string Layout(string layout) => string.IsNullOrEmpty(layout) ? "Model" : layout;

        public IReadOnlyList<EntityRef> Entities(string layout)
        {
            return EntitiesOf(layout);
        }

        private List<EntityRef> EntitiesOf(string layout)
        {
            var found = new List<EntityRef>();

            foreach (DxfEntity entity in Document.GetObjects(new CadObjectFilter { Layout = Layout(layout) }))
            {
                string handle = Document.HandleOf(entity);

                if (handle != null)
                {
                    found.Add(new EntityRef(handle, entity));
                }
            }

            // A query of the app cannot carry a viewport, which only a layout has: the handles of the layout, in the order it draws
            // them, name the ones that are missing, and they are read from a copy of the app's database.
            if (!string.Equals(Layout(layout), "Model", StringComparison.OrdinalIgnoreCase))
            {
                IReadOnlyList<string> order = Document.Make("handles", new CadMakeArgs().Set("layout", layout).Values);
                var have = new HashSet<string>(found.Select(f => f.Handle), StringComparer.OrdinalIgnoreCase);

                if (order.Any(h => !have.Contains(h)))
                {
                    Dictionary<string, DxfEntity> viewports = ViewportsOf(order.Where(h => !have.Contains(h)));
                    var byHandle = found.ToDictionary(f => f.Handle, StringComparer.OrdinalIgnoreCase);
                    var all = new List<EntityRef>();

                    foreach (string handle in order)
                    {
                        if (byHandle.TryGetValue(handle, out EntityRef known))
                        {
                            all.Add(known);
                        }
                        else if (viewports.TryGetValue(handle, out DxfEntity viewport))
                        {
                            all.Add(new EntityRef(handle, viewport));
                        }
                    }

                    all.AddRange(found.Where(f => !order.Contains(f.Handle, StringComparer.OrdinalIgnoreCase)));
                    return all;
                }
            }

            return found;
        }

        /// <summary>The viewports among some handles, as entities of a copy of the app's database (the app's queries do not carry them).</summary>
        private Dictionary<string, DxfEntity> ViewportsOf(IEnumerable<string> handles)
        {
            CadDocument copy = CadDocument.From(Document.GetDatabase(), Name);
            var viewports = new Dictionary<string, DxfEntity>(StringComparer.OrdinalIgnoreCase);

            foreach (string handle in handles)
            {
                if (copy.FindEntity(handle) is DxfViewport viewport && !viewport.IsErased)
                {
                    viewports[handle] = viewport;
                }
            }

            return viewports;
        }

        public IReadOnlyList<LayerRow> Layers() =>
            Document
                .Layers.Select(l => new LayerRow
                {
                    Name = l.Name,
                    Color = l.Color.ColorIndex,
                    Linetype = l.Linetype,
                    LineWeight = (int)l.LineWeight,
                    IsOn = l.IsOn,
                    IsFrozen = l.IsFrozen,
                    IsLocked = l.IsLocked,
                    IsPlottable = l.IsPlottable,
                    Description = l.Description,
                })
                .ToList();

        public IReadOnlyList<DxfTextStyleTableRecord> TextStyles() => Document.TextStyles;

        public IReadOnlyList<DxfDimStyleTableRecord> DimensionStyles() => Document.DimensionStyles;

        public IReadOnlyList<DxfBlockTableRecord> Blocks() => Document.Blocks;

        public IReadOnlyList<string> Linetypes() => Document.Linetypes;

        public object Variable(string name) => Document.GetVariable(name);

        public BoxRows Bounds(string layout)
        {
            CadRemoteBounds table = Document.GetBounds(Layout(layout));
            return new BoxRows(table.Handles, table.Boxes);
        }

        public BoxRows BoundsOf(IReadOnlyList<string> handles)
        {
            CadRemoteBounds table = Document.GetBoundsOf(handles);
            return new BoxRows(table.Handles, table.Boxes);
        }

        public OverlapRows Overlaps(
            string layout,
            IReadOnlyList<string> words,
            IReadOnlyList<string> over,
            IReadOnlyList<string> handles,
            double tolerance
        )
        {
            CadRemoteOverlaps table = Document.GetOverlaps(Layout(layout), words, over, handles, tolerance);
            return new OverlapRows(table.Words, table.Others, table.Boxes);
        }

        public MeasureRows Measure(DxfEntity entity) => Measured(Document.Measure(entity));

        public MeasureRows MeasureLeader(
            string style,
            double arrowX,
            double arrowY,
            double landingX,
            double landingY,
            string words,
            double? scale
        ) => Measured(Document.MeasureLeader(style, arrowX, arrowY, landingX, landingY, words, scale));

        private static MeasureRows Measured(CadRemoteMeasurement measured) =>
            new MeasureRows(measured.Box.Length == 0 ? null : measured.Box, measured.Frames);

        public double[] FreeSpace(
            string layout,
            double[] frame,
            IReadOnlyList<string> kinds,
            double margin,
            double minWidth,
            double minHeight,
            double tolerance
        ) =>
            Document
                .GetFreeSpace(
                    Layout(layout),
                    frame[0],
                    frame[1],
                    frame[2],
                    frame[3],
                    kinds,
                    margin,
                    minWidth,
                    minHeight,
                    tolerance
                )
                .Boxes;

        public bool Fits(
            string layout,
            double[] candidate,
            IReadOnlyList<string> kinds,
            double margin,
            double tolerance
        ) =>
            Document.Fits(
                Layout(layout),
                candidate[0],
                candidate[1],
                candidate[2],
                candidate[3],
                candidate[4],
                kinds,
                margin,
                tolerance
            );

        public StandardRows Check(string standard, double modelScale, double tolerance)
        {
            CadRemoteStandardResult result = Document.CheckStandard(standard, modelScale, tolerance);
            return new StandardRows(
                result.Name,
                result.Total,
                result
                    .Rules.Select(r => new RuleScoreInfo
                    {
                        Rule = r.Rule,
                        Score = r.Score,
                        Subjects = r.Subjects,
                        Offenders = r.Offenders,
                        Weight = r.Weight,
                    })
                    .ToList(),
                result
                    .Violations.Select(v => new ViolationInfo
                    {
                        Rule = v.Rule,
                        Handle = v.Handle,
                        Other = v.Other,
                        Space = v.Space,
                        Through = v.Through,
                        Box = v.Box,
                        Measured = v.Measured,
                        Allowed = v.Allowed,
                        Value = double.IsNaN(v.Value) ? (double?)null : v.Value,
                        Limit = double.IsNaN(v.Limit) ? (double?)null : v.Limit,
                    })
                    .ToList()
            );
        }

        public string ExportJson(string layout) => Document.ExportJson(layout);

        public string ExportGraph(string layout, double tolerance) => Document.ExportGraph(layout, tolerance);

        public IReadOnlyList<string> Add(IReadOnlyList<DxfEntity> entities, string layout)
        {
            foreach (DxfEntity entity in entities)
            {
                Document.Insert(entity, string.IsNullOrEmpty(layout) ? null : layout);
            }

            return Document.CommitChanges();
        }

        public void AddLayer(LayerRow layer)
        {
            Document.AddLayer(
                new CadRemoteLayer(layer.Name)
                {
                    Color = DxfColor.FromColorIndex(DxfColorMethod.ByAci, (short)layer.Color),
                    Linetype = layer.Linetype,
                    LineWeight = (DxfLineWeight)layer.LineWeight,
                    IsOn = layer.IsOn,
                    IsFrozen = layer.IsFrozen,
                    IsLocked = layer.IsLocked,
                    IsPlottable = layer.IsPlottable,
                }
            );
            Document.CommitChanges();
        }

        private List<DxfEntity> Found(IReadOnlyList<string> handles)
        {
            var found = new List<DxfEntity>();

            foreach (string handle in handles)
            {
                found.Add(
                    Document.GetObject(handle)
                        ?? throw new ArgumentException("The drawing " + Name + " has no object " + handle + ".")
                );
            }

            return found;
        }

        public void Move(IReadOnlyList<string> handles, double dx, double dy) =>
            Document.Move(Found(handles), new GeoVector2(dx, dy));

        public void Rotate(IReadOnlyList<string> handles, double x, double y, double radians) =>
            Document.Rotate(Found(handles), new GeoPoint2(x, y), radians);

        public void Scale(IReadOnlyList<string> handles, double x, double y, double factor) =>
            Document.Scale(Found(handles), new GeoPoint2(x, y), factor);

        public void Erase(IReadOnlyList<string> handles)
        {
            foreach (string handle in handles)
            {
                Document.Delete(handle);
            }

            Document.CommitChanges();
        }

        public void SetProperties(IReadOnlyList<string> handles, string layer, int? color, string linetype)
        {
            foreach (DxfEntity entity in Found(handles))
            {
                if (!string.IsNullOrEmpty(layer))
                {
                    entity.Layer = layer;
                }

                if (color.HasValue)
                {
                    entity.ColorIndex = color.Value;
                }

                if (!string.IsNullOrEmpty(linetype))
                {
                    entity.Linetype = linetype;
                }

                Document.Modify(entity);
            }

            Document.CommitChanges();
        }

        public bool Undo()
        {
            RefuseWhileRunning();
            CadRemoteTyped sent = Document.TypeScript("U\n", false);
            return sent.Completed && TookBack(sent.Output, "Everything has been undone");
        }

        /// <summary>
        /// Whether what the command line wrote after U or REDO says a step was taken: a line that is not the command's echo and not
        /// the sentence that says there was none (<paramref name="none"/>).
        /// </summary>
        private static bool TookBack(IReadOnlyList<string> output, string none) =>
            output.Any(l =>
            {
                string line = l.Trim();
                return line.Length > 0
                    && !line.StartsWith("Command:", StringComparison.OrdinalIgnoreCase)
                    && line.IndexOf(none, StringComparison.OrdinalIgnoreCase) < 0;
            });

        public string Save(string path)
        {
            if (string.IsNullOrEmpty(path))
            {
                Document.Save();
            }
            else
            {
                Document.SaveAs(System.IO.Path.GetFullPath(path));
            }

            return Document.Path;
        }

        public byte[] RenderPng(RenderRequest request)
        {
            // The app's drawing as it stands (changes not saved included) is read into a document of this process and drawn here;
            // the app's file is never written.
            LocalBackend local = new LocalBackend(CadDocument.From(Document.GetDatabase(), Name));
            return local.RenderPng(request);
        }

        public string RunCommand(string text)
        {
            CadRemoteCommandResult result = Document.SendCommand(text, TimeSpan.FromSeconds(CommandSeconds));
            string output = string.Join("\n", result.Output);
            return result.Completed
                ? output
                : output
                    + "\n(The command is still waiting for an answer in the app: answer it with another run_command, or press Esc in the app.)";
        }

        /// <summary>
        /// Types a fixed text of the tool's own, as <see cref="RunCommand"/> does, but strictly: the app refuses it where a command is
        /// running by the time it types the first line, and types none once the command has ended.
        /// </summary>
        public string RunFixed(string text)
        {
            CadRemoteTyped sent = Document.TypeScript(text + "\n", false);
            string output = string.Join("\n", sent.Output);
            return sent.Completed ? output : output + "\n(The command is still waiting for an answer in the app.)";
        }

        public void Dispose() { }
    }
}
