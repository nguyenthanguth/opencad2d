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

        public EntityRef Find(string handle)
        {
            DxfEntity entity = Document.GetObject(handle);
            return entity == null ? null : new EntityRef(handle, entity);
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

        public void SetVariable(string name, object value) => Document.SetVariable(name, value);

        public object TakeSnapshot() => Document.TakeSnapshot();

        public void ResetTo(object snapshot) => Document.Reset((CadRemoteSnapshot)snapshot);
    }
}
