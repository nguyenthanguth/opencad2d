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
    internal sealed partial class RemoteBackend : IDrawingBackend
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
            var found = new List<EntityRef>();

            foreach (DxfEntity entity in Document.GetObjects(new CadObjectFilter { Layout = Layout(layout) }))
            {
                string handle = Document.HandleOf(entity);

                if (handle != null)
                {
                    found.Add(new EntityRef(handle, entity));
                }
            }

            return found;
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

        public bool Undo() => Document.SendCommand("U").Completed;

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

        public void Dispose() { }
    }
}
