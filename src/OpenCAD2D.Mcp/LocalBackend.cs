using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using GeometryHelper;
using GeometryHelper.Dxf;
using GeometryHelper.Geometry;

namespace OpenCAD2D.Mcp
{
    /// <summary>
    /// A drawing file worked on in this process with no app, through the engine itself: the way the Python
    /// <c>open_file</c> works, the commit by <see cref="CadDocument.ApplyEdit"/> and the edits by the <see cref="CadEditor"/> of
    /// a command session, each one undo step.
    /// </summary>
    internal sealed class LocalBackend : IDrawingBackend
    {
        private CadCommandSession _session;

        /// <summary>Starts from a document of the engine.</summary>
        public LocalBackend(CadDocument document)
        {
            Document = document ?? throw new ArgumentNullException(nameof(document));
            CadRender.Prepare(document);
        }

        /// <summary>Gets the document.</summary>
        public CadDocument Document { get; }

        /// <summary>Opens a DXF or DWG file.</summary>
        public static LocalBackend Open(string path) =>
            new LocalBackend(CadDocument.Open(System.IO.Path.GetFullPath(path)));

        /// <summary>Makes a blank drawing (AutoCAD 2013, layer 0 only, Layout1 an A3 sheet in millimetres) with no file.</summary>
        public static LocalBackend New()
        {
            var database = new DxfDatabase("AC1027");
            CadTemplate.UseMillimetreSheet(database);
            return new LocalBackend(CadDocument.From(database));
        }

        public string Mode => "file";

        public string Name => Document.Title;

        public string Path =>
            Document.FileName != null && System.IO.Path.IsPathRooted(Document.FileName) ? Document.FileName : null;

        public bool IsModified => Document.IsModified;

        public string CurrentLayout => (Document.CurrentSpace ?? Document.ModelSpace).Name;

        public string CurrentLayer => Document.Database.Clayer;

        public string CurrentDimensionStyle => CadDimStyleEditor.Current(Document.Database);

        public IReadOnlyList<string> Layouts => Spaces().Select(s => s.Name).ToList();

        private CadCommandSession Session => _session ?? (_session = new CadCommandSession(Document));

        private IEnumerable<CadSpace> Spaces() => Document.Spaces.Where(s => !s.IsBlockEditor);

        private CadSpace SpaceNamed(string layout)
        {
            if (string.IsNullOrEmpty(layout) || string.Equals(layout, "Model", StringComparison.OrdinalIgnoreCase))
            {
                return Document.ModelSpace;
            }

            return Spaces().FirstOrDefault(s => string.Equals(s.Name, layout, StringComparison.OrdinalIgnoreCase))
                ?? throw new ArgumentException("The drawing " + Name + " has no layout named " + layout + ".");
        }

        public IReadOnlyList<EntityRef> Entities(string layout)
        {
            var found = new List<EntityRef>();

            foreach (DxfEntity entity in SpaceNamed(layout).Block)
            {
                if (!entity.IsErased && !string.IsNullOrEmpty(entity.Handle))
                {
                    found.Add(new EntityRef(entity.Handle, entity));
                }
            }

            return found;
        }

        public IReadOnlyList<LayerRow> Layers()
        {
            var rows = new List<LayerRow>();

            foreach (DxfLayerTableRecord layer in Document.Database.LayerTable)
            {
                rows.Add(
                    new LayerRow
                    {
                        Name = layer.Name,
                        Color = layer.ColorIndex,
                        Linetype = layer.LinetypeName,
                        LineWeight = (int)layer.LineWeight,
                        IsOn = !layer.IsOff,
                        IsFrozen = layer.IsFrozen,
                        IsLocked = layer.IsLocked,
                        IsPlottable = layer.IsPlottable,
                        Description = layer.Description,
                    }
                );
            }

            return rows;
        }

        public IReadOnlyList<DxfTextStyleTableRecord> TextStyles() => Document.Database.TextStyleTable.ToList();

        public IReadOnlyList<DxfDimStyleTableRecord> DimensionStyles() => Document.Database.DimStyleTable.ToList();

        public IReadOnlyList<DxfBlockTableRecord> Blocks() =>
            Document.Database.BlockTable.Where(b => !b.IsLayout && !b.IsAnonymous && !b.Name.StartsWith("*")).ToList();

        public IReadOnlyList<string> Linetypes() => Document.Database.LinetypeTable.Select(l => l.Name).ToList();

        public object Variable(string name) => Session.Editor.GetVariable(name);

        public BoxRows Bounds(string layout)
        {
            CadBoundsTable table = CadSpatial.Bounds(Document, SpaceNamed(layout));
            return new BoxRows(table.Handles, table.Boxes);
        }

        public BoxRows BoundsOf(IReadOnlyList<string> handles)
        {
            CadBoundsTable table = CadSpatial.Bounds(Document, handles);
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
            var options = new CadOverlapOptions
            {
                Words = words,
                Over = over,
                Handles = handles,
            };
            CadOverlapTable table = CadSpatial.Overlaps(
                Document,
                SpaceNamed(layout),
                options,
                new Tolerance(tolerance, 1e-5)
            );
            return new OverlapRows(table.Words, table.Others, table.Boxes);
        }

        public MeasureRows Measure(DxfEntity entity) => Measured(CadMeasure.Of(Document, entity));

        public MeasureRows MeasureLeader(
            string style,
            double arrowX,
            double arrowY,
            double landingX,
            double landingY,
            string words,
            double? scale
        ) =>
            Measured(
                CadMeasure.Leader(
                    Document,
                    style,
                    new GeoPoint2(arrowX, arrowY),
                    new GeoPoint2(landingX, landingY),
                    words,
                    scale
                )
            );

        private static MeasureRows Measured(CadMeasurement measured)
        {
            DisplayBox box = measured.Box;
            var frames = new double[5 * measured.Frames.Count];

            for (int i = 0; i < measured.Frames.Count; i++)
            {
                CadFrame frame = measured.Frames[i];
                frames[5 * i] = frame.X;
                frames[5 * i + 1] = frame.Y;
                frames[5 * i + 2] = frame.Width;
                frames[5 * i + 3] = frame.Height;
                frames[5 * i + 4] = frame.Rotation;
            }

            return new MeasureRows(box.IsEmpty ? null : new[] { box.MinX, box.MinY, box.MaxX, box.MaxY }, frames);
        }

        public double[] FreeSpace(
            string layout,
            double[] frame,
            IReadOnlyList<string> kinds,
            double margin,
            double minWidth,
            double minHeight,
            double tolerance
        )
        {
            CadFreeSpace table = CadSpatial.FreeSpace(
                Document,
                SpaceNamed(layout),
                new DisplayBox(frame[0], frame[1], frame[2], frame[3]),
                new CadFreeSpaceOptions
                {
                    Kinds = kinds,
                    Margin = margin,
                    MinWidth = minWidth,
                    MinHeight = minHeight,
                },
                new Tolerance(tolerance, 1e-5)
            );
            return table.Boxes;
        }

        public bool Fits(
            string layout,
            double[] candidate,
            IReadOnlyList<string> kinds,
            double margin,
            double tolerance
        ) =>
            CadSpatial.Fits(
                Document,
                SpaceNamed(layout),
                new CadFrame(candidate[0], candidate[1], candidate[2], candidate[3], candidate[4]),
                new CadFreeSpaceOptions { Kinds = kinds, Margin = margin },
                new Tolerance(tolerance, 1e-5)
            );

        public StandardRows Check(string standard, double modelScale, double tolerance)
        {
            CadStandardResult result = CadStandards.Check(
                Document,
                CadStandard.Parse(standard),
                modelScale,
                new Tolerance(tolerance, 1e-5)
            );
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

        public string ExportJson(string layout) => CadDrawingJson.Write(Document, layout);

        public string ExportGraph(string layout, double tolerance) =>
            CadDrawingGraph.Build(Document, layout, tolerance).ToJson();

        public IReadOnlyList<string> Add(IReadOnlyList<DxfEntity> entities, string layout)
        {
            CadSpace space = SpaceNamed(layout);
            var batch = new DxfDatabase("AC1027");
            var group = new List<DxfEntity>();

            foreach (DxfEntity entity in entities)
            {
                DxfEntity copy = entity.Clone();
                Provide(batch, copy);

                // The app draws a dimension's picture; the copy only needs a block of its own to be added with.
                if (copy is DxfDimension dimension && dimension.DimBlock == null)
                {
                    var picture = new DxfBlockTableRecord { Name = "*D" };
                    batch.BlockTable.Add(picture);
                    dimension.DimBlock = picture;
                }

                batch.ModelSpace.AppendEntity(copy);
                group.Add(copy);
            }

            var handles = new List<string>();
            CadEditResult result = Document.ApplyEdit("MCP", Array.Empty<CadEditRecord>(), scope =>
                InsertGroup(scope, batch, space, group, handles)
            );

            if (!result.Succeeded)
            {
                throw new InvalidOperationException(result.Message);
            }

            return handles;
        }

        /// <summary>
        /// Gives the small drawing a record of each name the entity brings that the drawing has (its linetype, its text style,
        /// its block), so that the copy into the drawing keeps the drawing's own record; refuses a name the drawing has not.
        /// </summary>
        private void Provide(DxfDatabase batch, DxfEntity entity)
        {
            DxfDatabase database = Document.Database;
            string linetype = entity.Linetype;

            if (
                !string.IsNullOrEmpty(linetype)
                && !string.Equals(linetype, "BYLAYER", StringComparison.OrdinalIgnoreCase)
                && !string.Equals(linetype, "BYBLOCK", StringComparison.OrdinalIgnoreCase)
                && !string.Equals(linetype, "CONTINUOUS", StringComparison.OrdinalIgnoreCase)
            )
            {
                if (!database.LinetypeTable.Has(linetype))
                {
                    throw new ArgumentException("The drawing has no linetype " + linetype + ".");
                }

                if (!batch.LinetypeTable.Has(linetype))
                {
                    batch.LinetypeTable.Add(new DxfLinetypeTableRecord { Name = linetype });
                }
            }

            string style =
                entity is DxfDBText text ? text.TextStyleName
                : entity is DxfMText mtext ? mtext.TextStyleName
                : null;

            if (!string.IsNullOrEmpty(style) && !string.Equals(style, "Standard", StringComparison.OrdinalIgnoreCase))
            {
                if (!database.TextStyleTable.Has(style))
                {
                    throw new ArgumentException(
                        "The drawing has no text style " + style + ": list_text_styles shows the ones it has."
                    );
                }

                if (!batch.TextStyleTable.Has(style))
                {
                    batch.TextStyleTable.Add(new DxfTextStyleTableRecord { Name = style });
                }
            }

            if (entity is DxfBlockReference insert)
            {
                if (!database.BlockTable.Has(insert.Name))
                {
                    throw new ArgumentException(
                        "The drawing has no block " + insert.Name + ": list_blocks shows the ones it has."
                    );
                }

                if (!batch.BlockTable.Has(insert.Name))
                {
                    batch.BlockTable.Add(new DxfBlockTableRecord { Name = insert.Name });
                }
            }
        }

        private void InsertGroup(
            CadEditScope scope,
            DxfDatabase batch,
            CadSpace space,
            List<DxfEntity> group,
            List<string> handles
        )
        {
            // A layer the drawing lacks is made by the copy; the layer box and the Layer manager are told.
            List<string> missing = group
                .Select(e => e.Layer)
                .Where(l => !string.IsNullOrEmpty(l) && !Document.Database.LayerTable.Has(l))
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .OrderBy(l => l, StringComparer.OrdinalIgnoreCase)
                .ThenBy(l => l, StringComparer.Ordinal)
                .ToList();
            IReadOnlyList<DxfEntity> copies = batch.WblockCloneObjects(group, space.Block);

            foreach (DxfEntity copy in copies)
            {
                if (string.IsNullOrEmpty(copy.Handle) || copy.IsErased)
                {
                    throw new InvalidOperationException("The drawing did not take an object.");
                }

                // A dimension made in code comes with a picture that is empty: the engine draws it, as DIM would.
                if (copy is DxfDimension dimension && !dimension.DimBlock.Any(e => !e.IsErased))
                {
                    CadDimensionPicture.Redraw(dimension);
                }

                scope.Added(space, copy);
                handles.Add(copy.Handle);
            }

            foreach (string layer in missing)
            {
                scope.Record(new CadEditRecord(CadEditRecordKind.TableChanged, null, "Layer", null, layer));
            }
        }

        public void AddLayer(LayerRow layer)
        {
            Document.BeginGroup("MCP");

            try
            {
                Check(CadLayerEditor.Add(Document, layer.Name));
                Check(CadLayerEditor.SetLinetype(Document, layer.Name, layer.Linetype));
                Check(CadLayerEditor.SetLineWeight(Document, layer.Name, (DxfLineWeight)layer.LineWeight));
                Check(CadLayerEditor.SetColor(Document, layer.Name, layer.Color));

                if (!layer.IsOn)
                {
                    Check(CadLayerEditor.SetOn(Document, layer.Name, false));
                }

                if (layer.IsFrozen)
                {
                    Check(CadLayerEditor.SetFrozen(Document, layer.Name, true));
                }

                if (layer.IsLocked)
                {
                    Check(CadLayerEditor.SetLocked(Document, layer.Name, true));
                }
            }
            catch
            {
                Document.CancelGroup();
                throw;
            }

            Document.EndGroup();
        }

        private static void Check(CadEditResult result)
        {
            if (!result.Succeeded)
            {
                throw new InvalidOperationException(result.Message);
            }
        }

        private List<DxfEntity> Found(IReadOnlyList<string> handles)
        {
            var entities = new List<DxfEntity>();

            foreach (string handle in handles)
            {
                DxfEntity entity = Document.FindEntity(handle);

                if (entity == null || entity.IsErased)
                {
                    throw new ArgumentException("The drawing " + Name + " has no object " + handle + ".");
                }

                entities.Add(entity);
            }

            return entities;
        }

        public void Move(IReadOnlyList<string> handles, double dx, double dy) =>
            Session.Editor.Move(Found(handles), new GeoVector2(dx, dy));

        public void Rotate(IReadOnlyList<string> handles, double x, double y, double radians) =>
            Session.Editor.Rotate(Found(handles), new GeoPoint2(x, y), radians);

        public void Scale(IReadOnlyList<string> handles, double x, double y, double factor) =>
            Session.Editor.Scale(Found(handles), new GeoPoint2(x, y), factor);

        public void Erase(IReadOnlyList<string> handles) => Session.Editor.Erase(Found(handles));

        public void SetProperties(IReadOnlyList<string> handles, string layer, int? color, string linetype)
        {
            Found(handles);
            Document.BeginGroup("MCP");

            try
            {
                Set(handles, "Layer", layer);
                Set(handles, "Color", color.HasValue ? ColorText(color.Value) : null);
                Set(handles, "Linetype", linetype);
            }
            catch
            {
                Document.CancelGroup();
                throw;
            }

            Document.EndGroup();
        }

        private static string ColorText(int index) =>
            index == 256 ? "ByLayer"
            : index == 0 ? "ByBlock"
            : index.ToString(System.Globalization.CultureInfo.InvariantCulture);

        private void Set(IReadOnlyList<string> handles, string label, string value)
        {
            if (value == null)
            {
                return;
            }

            CadEditResult result = CadPropertyEditor.Set(Document, handles.ToList(), label, value);

            if (!result.Succeeded)
            {
                throw new InvalidOperationException(result.Message);
            }
        }

        public bool Undo() => Document.Undo();

        public string Save(string path)
        {
            string target = path;

            if (string.IsNullOrEmpty(target))
            {
                target = Path ?? throw new ArgumentException("The drawing has no file yet: give save a path.");
                Document.SavingInGroup(Document.Save);
                return target;
            }

            target = System.IO.Path.GetFullPath(target);
            Document.SavingInGroup(() => Document.SaveAs(target));
            return target;
        }

        public byte[] RenderPng(RenderRequest request)
        {
            string file = System.IO.Path.Combine(
                System.IO.Path.GetTempPath(),
                "opencad2d-mcp-" + Guid.NewGuid().ToString("N") + ".png"
            );

            try
            {
                CadRender.WritePng(file, Document, RenderOptions.Of(request));
                return File.ReadAllBytes(file);
            }
            finally
            {
                if (File.Exists(file))
                {
                    File.Delete(file);
                }
            }
        }

        public string RunCommand(string text)
        {
            CadCommandSession session = Session;
            int mark = session.History.Count;

            // As the app takes it: a line a time, the empty one a text ends with dropped, an empty line before it Enter.
            string[] lines = text.Replace("\r\n", "\n").Replace('\r', '\n').Split('\n');
            int count = lines[lines.Length - 1].Length == 0 ? lines.Length - 1 : lines.Length;

            foreach (string line in lines.Take(count))
            {
                if (line.TrimStart().StartsWith("(", StringComparison.Ordinal))
                {
                    session.Feed(line);
                    continue;
                }

                if (line.Trim().Length == 0)
                {
                    session.Enter();
                    continue;
                }

                foreach (string word in line.Split(new[] { ' ', '\t' }, StringSplitOptions.RemoveEmptyEntries))
                {
                    session.Feed(word);
                }
            }

            string waiting = null;

            if (session.IsRunning)
            {
                waiting = session.PromptText;
                session.Escape();
            }

            var output = new List<string>();

            for (int i = mark; i < session.History.Count; i++)
            {
                output.Add(session.History[i]);
            }

            if (waiting != null)
            {
                output.Add("(The command was still asking \"" + waiting + "\" and was cancelled.)");
            }

            return string.Join("\n", output);
        }

        public void Dispose() { }
    }
}
