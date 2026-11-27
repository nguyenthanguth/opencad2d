using System;
using System.Collections.Generic;
using System.Linq;
using GeometryHelper.Dxf;
using GeometryHelper.Geometry;
using OpenCAD2D.Remote;

namespace OpenCAD2D.Mcp
{
    internal sealed partial class RemoteBackend : IQueryBackend
    {
        public object DrawingKey => Document.Id;

        public EntityRef Find(string handle) => FindAll(new[] { handle }).FirstOrDefault();

        public IReadOnlyList<EntityRef> FindAll(IReadOnlyList<string> handles)
        {
            List<DxfEntity> entities = handles.Select(h => Document.GetObject(h)).ToList();
            CadDocument copy = null;

            for (int i = 0; i < entities.Count; i++)
            {
                // The app does not hand out a viewport, and its copy of a multileader or a table has no style table to find the name
                // of its style in: both are read from one copy of the whole database, made once for all the handles.
                if (entities[i] == null || entities[i] is DxfMLeader || entities[i] is DxfTable)
                {
                    copy = copy ?? CadDocument.From(Document.GetDatabase(), Name);
                    DxfEntity inCopy = copy.FindEntity(handles[i]);
                    entities[i] =
                        entities[i] == null
                            ? (inCopy is DxfViewport viewport && !viewport.IsErased ? viewport : null)
                            : inCopy ?? entities[i];
                }
            }

            return handles
                .Select((h, i) => entities[i] == null ? null : new EntityRef(h, entities[i]))
                .Where(e => e != null)
                .ToList();
        }

        public PaletteView Properties(IReadOnlyList<string> handles)
        {
            CadRemoteProperties info = Document.GetProperties(handles);
            return new PaletteView
            {
                Label = info.Label,
                Groups = info
                    .Groups.Select(g => new PaletteGroup
                    {
                        Title = g.Title,
                        Rows = g
                            .Rows.Select(r => new PaletteRow
                            {
                                Label = r.Label,
                                Value = r.Value,
                                ReadOnly = r.IsReadOnly,
                            })
                            .ToList(),
                    })
                    .ToList(),
            };
        }

        public IReadOnlyList<string> HandlesIn(
            string layout,
            CadAreaSelection how,
            IReadOnlyList<GeoPoint2> points,
            double tolerance
        )
        {
            string word;

            switch (how)
            {
                case CadAreaSelection.Window:
                    word = "window";
                    break;
                case CadAreaSelection.Crossing:
                    word = "crossing";
                    break;
                case CadAreaSelection.Fence:
                    word = "fence";
                    break;
                case CadAreaSelection.WindowPolygon:
                    word = "wpolygon";
                    break;
                case CadAreaSelection.CrossingPolygon:
                    word = "cpolygon";
                    break;
                default:
                    word = "point";
                    break;
            }

            return Document.GetHandlesIn(Layout(layout), word, points, tolerance);
        }

        public IReadOnlyList<string> SelectedHandles() =>
            Document
                .GetSelectedObjects()
                .Select(Document.HandleOf)
                .Where(h => h != null)
                .OrderBy(h => h, StringComparer.OrdinalIgnoreCase)
                .ThenBy(h => h, StringComparer.Ordinal)
                .ToList();

        public IReadOnlyList<string> ListLines(IReadOnlyList<string> handles) => Document.ListObjects(handles);

        public CadDocument ReadDocument() => CadDocument.From(Document.GetDatabase(), Name);

        public object TakeSnapshot() => Document.TakeSnapshot();

        public void ResetTo(object snapshot) => Document.Reset((CadRemoteSnapshot)snapshot);
    }
}
