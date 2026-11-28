using System.Collections.Generic;
using System.Linq;
using GeometryHelper.Dxf;
using GeometryHelper.Geometry;

namespace OpenCAD2D.Mcp
{
    internal sealed partial class LocalBackend : IQueryBackend
    {
        public object DrawingKey => Document;

        public EntityRef Find(string handle)
        {
            DxfEntity entity = Document.FindEntity(handle);
            return entity == null || entity.IsErased ? null : new EntityRef(entity.Handle, entity);
        }

        public IReadOnlyList<EntityRef> FindAll(IReadOnlyList<string> handles) =>
            handles.Select(Find).Where(e => e != null).ToList();

        public PaletteView Properties(IReadOnlyList<string> handles)
        {
            CadSelectionInfo info = CadProperties.Describe(Document, null, handles.ToList());
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
                                Value = r.Value ?? string.Empty,
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
        ) => CadSpatial.Select(Document, SpaceNamed(layout), how, points, tolerance);

        public IReadOnlyList<string> SelectedHandles() =>
            Document
                .Selection.Handles.OrderBy(h => h, System.StringComparer.OrdinalIgnoreCase)
                .ThenBy(h => h, System.StringComparer.Ordinal)
                .ToList();

        public IReadOnlyList<string> ListLines(IReadOnlyList<string> handles) => CadInquiry.List(Document, handles);

        public IReadOnlyList<LayoutInfo> LayoutInfos() => TableReads.Layouts(Document, CurrentLayout);

        public IReadOnlyList<LinetypeInfo> LinetypeInfos() => TableReads.Linetypes(Document.Database);

        public IReadOnlyList<BlockInfo> BlockInfos() => TableReads.Blocks(Document.Database);

        public IReadOnlyList<MLeaderStyleInfo> MLeaderStyles() => AnnotationStyleReads.MLeaderStyles(Document.Database);

        public IReadOnlyList<TableStyleInfo> TableStyles() => AnnotationStyleReads.TableStyles(Document.Database);

        public object TakeSnapshot() => Document.TakeSnapshot();

        public void ResetTo(object snapshot) => Document.Reset((CadSnapshot)snapshot);
    }
}
