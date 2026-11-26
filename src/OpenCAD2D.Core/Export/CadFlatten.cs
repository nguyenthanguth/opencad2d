using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using GeometryHelper;
using GeometryHelper.Dxf;
using GeometryHelper.Geometry;

namespace OpenCAD2D
{
    /// <summary>
    /// One entity of a drawing walked down to the shapes it is drawn as: where it stands in the world, where it came from, and
    /// the look it has once its block references have given what is <c>0</c> and <c>ByBlock</c> to it. The entity is a copy
    /// the walk owns where it came out of a block, the drawing's own object where it stood in a space; either way it is read,
    /// not changed.
    /// </summary>
    public sealed class CadFlatEntity
    {
        internal CadFlatEntity(
            DxfEntity entity,
            string handle,
            string layout,
            double scale,
            IReadOnlyList<string> path,
            IReadOnlyList<int> indexPath,
            GeoTransform3 matrix,
            string layer,
            DxfColor color,
            string linetype
        )
        {
            Entity = entity;
            Handle = handle;
            Layout = layout;
            Scale = scale;
            Path = path;
            IndexPath = indexPath;
            Matrix = matrix;
            Layer = layer;
            Color = color;
            Linetype = linetype;
        }

        /// <summary>Gets the entity, in world coordinates and the units of the drawing (<see cref="Scale"/> takes them to millimetres).</summary>
        public DxfEntity Entity { get; }

        /// <summary>
        /// Gets the handle of the entity of the space this one came out of: its own handle for one that stood in a space, the
        /// handle of the block reference of the space for one that came out of a block. An entity of a block has no handle of
        /// its own in the world, so the pieces of one reference share it, told apart by <see cref="IndexPath"/>.
        /// </summary>
        public string Handle { get; }

        /// <summary>Gets the name of the layout (or <c>Model</c>) the entity is drawn in.</summary>
        public string Layout { get; }

        /// <summary>Gets the factor that takes its lengths to millimetres (<see cref="CadUnitScale"/>).</summary>
        public double Scale { get; }

        /// <summary>
        /// Gets the names of the blocks the entity came out of, the outermost first: BOLT, then HEAD for a circle in the block
        /// HEAD that the block BOLT places; empty for an entity of a space. A dynamic block is named by the dynamic block, not
        /// by the anonymous <c>*U</c> block it draws.
        /// </summary>
        public IReadOnlyList<string> Path { get; }

        /// <summary>
        /// Gets where the entity stands in each level of the walk: its place among what the block reference at that level explodes
        /// into, then among what the next explodes into, and so on; a block reference's attributes follow its pieces, and the
        /// cells of an array of references (MINSERT) follow one another. Empty for an entity of a space. With the handle it names
        /// the entity, and it orders the pieces of one reference.
        /// </summary>
        public IReadOnlyList<int> IndexPath { get; }

        /// <summary>
        /// Gets the transformation from the coordinates of the block the entity is in to the world, all the references of its
        /// <see cref="Path"/> composed (the cell of an array included); null for an entity of a space.
        /// </summary>
        public GeoTransform3 Matrix { get; }

        /// <summary>Gets the layer: the entity's own, where it is not 0, else the layer of the reference it came out of.</summary>
        public string Layer { get; }

        /// <summary>Gets the colour: the entity's own, where it is not ByBlock, else the colour of the reference it came out of.</summary>
        public DxfColor Color { get; }

        /// <summary>Gets the linetype: the entity's own, where it is not ByBlock, else the linetype of the reference it came out of.</summary>
        public string Linetype { get; }

        /// <summary>Gets whether a reference it came out of carries a clip (XCLIP), which the walk does not apply.</summary>
        public bool Clipped { get; internal set; }

        /// <summary>Gets whether the block it came out of is a dynamic block's.</summary>
        public bool Dynamic { get; internal set; }

        /// <summary>Gets whether it is a constant attribute definition of its block, a text the reference shows.</summary>
        public bool Constant { get; internal set; }

        /// <summary>Where the walk found the entity, to settle an order that handle and index path leave open.</summary>
        internal int Sequence { get; set; }

        /// <summary>Gets whether it is an attribute of a reference, or a constant attribute definition of a block.</summary>
        public bool IsAttribute => Entity is DxfAttributeReference || Constant;
    }

    /// <summary>What a walk of a drawing left alone or could not do, in words a pipeline can log.</summary>
    public sealed class CadFlatReport
    {
        private readonly SortedSet<string> _xrefs = new SortedSet<string>(StringComparer.Ordinal);
        private readonly SortedSet<string> _clipped = new SortedSet<string>(StringComparer.Ordinal);
        private readonly SortedSet<string> _refused = new SortedSet<string>(StringComparer.Ordinal);
        private readonly SortedSet<string> _blockUnits = new SortedSet<string>(StringComparer.Ordinal);

        internal CadFlatReport() { }

        /// <summary>Gets how many block references the walk went into, nested ones counted each time they are placed.</summary>
        public int Inserts { get; internal set; }

        /// <summary>Gets the names of the external references met: left as one entity each, the content of the file not read.</summary>
        public IReadOnlyCollection<string> Xrefs => _xrefs;

        /// <summary>Gets the names of the blocks placed with a clip (XCLIP), walked as if there were none.</summary>
        public IReadOnlyCollection<string> Clipped => _clipped;

        /// <summary>
        /// Gets the block references the walk could not go into, each as <c>NAME: why</c>, left as one entity each: a block the
        /// drawing has not, one that places itself, one nested more than 64 deep, one that would give more than the 2 000 000
        /// entities the reader places.
        /// </summary>
        public IReadOnlyCollection<string> Refused => _refused;

        /// <summary>
        /// Gets the blocks drawn in a unit other than the drawing's, each as a sentence, for a pipeline to decide on: a block
        /// of inches in a drawing of millimetres is listed with the 25.4 AutoCAD's INSERT scales it by when the block is
        /// inserted; a block reference read from a file has that scale in it already, and is not scaled again.
        /// </summary>
        public IReadOnlyCollection<string> BlockUnits => _blockUnits;

        internal void Xref(string name) => _xrefs.Add(name);

        internal void Clip(string name) => _clipped.Add(name);

        internal void Refuse(string name, string why) => _refused.Add(name + ": " + why);

        internal void BlockUnit(string sentence) => _blockUnits.Add(sentence);
    }

    /// <summary>One layout of a walked drawing, with the factor that takes its lengths to millimetres.</summary>
    public sealed class CadFlatLayout
    {
        internal CadFlatLayout(string name, bool isModel, double scale, bool normalised)
        {
            Name = name;
            IsModel = isModel;
            Scale = scale;
            Normalised = normalised;
        }

        /// <summary>Gets the name: <c>Model</c> or the name of the layout.</summary>
        public string Name { get; }

        /// <summary>Gets whether it is model space.</summary>
        public bool IsModel { get; }

        /// <summary>Gets the factor that takes its lengths to millimetres.</summary>
        public double Scale { get; }

        /// <summary>Gets whether the drawing says its unit; false for a model space of INSUNITS 0, left as it is.</summary>
        public bool Normalised { get; }
    }

    /// <summary>A drawing walked down to its shapes: every entity of its spaces, block references replaced by what they draw.</summary>
    public sealed class CadFlatDrawing
    {
        internal CadFlatDrawing(
            IReadOnlyList<CadFlatEntity> entities,
            IReadOnlyList<CadFlatLayout> layouts,
            CadFlatReport report
        )
        {
            Entities = entities;
            Layouts = layouts;
            Report = report;
        }

        /// <summary>Gets the entities, in the order of their handles (see <see cref="CadFlatten.Walk"/>).</summary>
        public IReadOnlyList<CadFlatEntity> Entities { get; }

        /// <summary>Gets the layouts walked, model space first, the rest in the order of the drawing.</summary>
        public IReadOnlyList<CadFlatLayout> Layouts { get; }

        /// <summary>Gets what the walk left alone.</summary>
        public CadFlatReport Report { get; }
    }

    /// <summary>
    /// Walks the block references of a drawing, nested ones and arrays of them included, to the entities they are drawn as, each
    /// in world coordinates with the path of blocks it came out of and the transformation that placed it, the drawing left as it
    /// is. It is what EXPLODE and BURST do, repeated down the nest and kept apart from the drawing.
    /// </summary>
    /// <remarks>
    /// <para>
    /// The walk calls the library's <c>Explode</c> of a block reference, the same call EXPLODE and BURST make, one level at a
    /// time on copies: each copy is already placed in the world, so a block turned and scaled within another gives its pieces
    /// where the drawing shows them, a circle under a scale unlike along its axes comes out an ELLIPSE, and an inner reference
    /// that cannot hold the placing is exploded in its turn, as EXPLODE does (that inner block is then missing from
    /// <see cref="CadFlatEntity.Path"/>). No second transformation code stands beside it. An array of references (MINSERT),
    /// which EXPLODE refuses, is walked cell by cell: the pieces of its first cell moved along the turned axes of the array by
    /// the spacing of the column and row, as the library moves them to measure the array.
    /// </para>
    /// <para>
    /// What a piece is on layer 0, ByBlock in colour or ByBlock in linetype takes the layer, colour and linetype of the
    /// reference, all the way down, as BURST gives them. The attributes of a reference come out as entities of their own, and a
    /// constant attribute definition of a block as one with <see cref="CadFlatEntity.Constant"/>; a variable definition is left
    /// out, its attribute standing in its place.
    /// </para>
    /// </remarks>
    public static class CadFlatten
    {
        /// <summary>How deep blocks may be nested before a reference is refused; a block that places itself is refused by the library earlier.</summary>
        private const int MostDepth = 64;

        /// <summary>The bound the library holds the entities exploding makes to, the reader's <c>MaxPlacedEntities</c>.</summary>
        private const long MostEntities = 2000000L;

        /// <summary>
        /// Walks the spaces of a drawing. The entities come in the order of their handles, hexadecimal numbers taken as numbers
        /// (handle 9 before 1A), the pieces of one block reference after each other in the order of <see cref="CadFlatEntity.IndexPath"/>;
        /// the drawing is not changed and no entity of it is.
        /// </summary>
        /// <param name="document">The drawing.</param>
        /// <param name="layout">The name of the layout to walk (<c>Model</c> for model space), compared in capitals; null for
        /// model space and every layout, in the order of the drawing.</param>
        /// <returns>The entities, the layouts and what was left alone.</returns>
        /// <exception cref="ArgumentNullException">Thrown when the drawing is null.</exception>
        /// <exception cref="ArgumentException">Thrown when the drawing has no layout of that name.</exception>
        public static CadFlatDrawing Walk(CadDocument document, string layout)
        {
            if (document == null)
            {
                throw new ArgumentNullException(nameof(document));
            }

            List<CadSpace> spaces = document.Spaces.Where(s => !s.IsBlockEditor).ToList();

            if (layout != null)
            {
                spaces = spaces.Where(s => string.Equals(s.Name, layout, StringComparison.OrdinalIgnoreCase)).ToList();
                Guard.Argument(spaces.Count > 0, nameof(layout), "The drawing has no layout named " + layout + ".");
            }

            double modelScale = CadUnitScale.Of(document.Database, out bool normalised);
            var report = new CadFlatReport();
            var layouts = new List<CadFlatLayout>();
            var found = new List<CadFlatEntity>();

            foreach (CadSpace space in spaces)
            {
                double scale = space.IsModel ? modelScale : CadUnitScale.OfPaper(space.Layout);
                layouts.Add(new CadFlatLayout(space.Name, space.IsModel, scale, !space.IsModel || normalised));
                var walker = new Walker(document.Database, space.Name, scale, report, found);

                foreach (DxfEntity entity in space.Block)
                {
                    if (!entity.IsErased)
                    {
                        walker.Top(entity);
                    }
                }
            }

            for (int i = 0; i < found.Count; i++)
            {
                found[i].Sequence = i;
            }

            found.Sort(CompareEntities);
            return new CadFlatDrawing(found.AsReadOnly(), layouts.AsReadOnly(), report);
        }

        /// <summary>The hexadecimal number of a handle; the largest where it is not one, so that such a handle sorts last.</summary>
        internal static long HandleKey(string handle) =>
            handle != null && long.TryParse(handle, NumberStyles.HexNumber, CultureInfo.InvariantCulture, out long key)
                ? key
                : long.MaxValue;

        /// <summary>The order the entities are given in: handle as a number, handle as text, the index path, then where the walk found it.</summary>
        private static int CompareEntities(CadFlatEntity a, CadFlatEntity b)
        {
            int order = HandleKey(a.Handle).CompareTo(HandleKey(b.Handle));

            if (order != 0)
            {
                return order;
            }

            order = string.CompareOrdinal(a.Handle, b.Handle);

            if (order != 0)
            {
                return order;
            }

            int common = Math.Min(a.IndexPath.Count, b.IndexPath.Count);

            for (int i = 0; i < common; i++)
            {
                order = a.IndexPath[i].CompareTo(b.IndexPath[i]);

                if (order != 0)
                {
                    return order;
                }
            }

            order = a.IndexPath.Count.CompareTo(b.IndexPath.Count);
            return order != 0 ? order : a.Sequence.CompareTo(b.Sequence);
        }

        /// <summary>The layer, colour and linetype a level of the walk hands down.</summary>
        private struct Look
        {
            public string Layer;
            public DxfColor Color;
            public string Linetype;

            /// <summary>The look of an entity inside a block reference of this look: 0 and ByBlock become the reference's, as BURST gives them.</summary>
            public Look Of(DxfEntity entity) =>
                new Look
                {
                    Layer = string.Equals(entity.Layer, "0", StringComparison.Ordinal) ? Layer : entity.Layer,
                    Color = entity.Color.IsByBlock ? Color : entity.Color,
                    Linetype = string.Equals(entity.Linetype, "ByBlock", StringComparison.OrdinalIgnoreCase)
                        ? Linetype
                        : entity.Linetype,
                };
        }

        private sealed class Walker
        {
            private readonly DxfDatabase _database;
            private readonly string _layout;
            private readonly double _scale;
            private readonly CadFlatReport _report;
            private readonly List<CadFlatEntity> _found;
            private readonly Dictionary<string, bool> _maybeDynamic = new Dictionary<string, bool>(
                StringComparer.Ordinal
            );
            private readonly HashSet<string> _unitsSaid = new HashSet<string>(StringComparer.Ordinal);
            private readonly DxfUnits _insunits;

            public Walker(
                DxfDatabase database,
                string layout,
                double scale,
                CadFlatReport report,
                List<CadFlatEntity> found
            )
            {
                _database = database;
                _layout = layout;
                _scale = scale;
                _report = report;
                _found = found;
                _insunits = database.Insunits;
            }

            /// <summary>An entity of a space: a block reference is walked, any other is one entity as it stands.</summary>
            public void Top(DxfEntity entity)
            {
                var look = new Look
                {
                    Layer = entity.Layer,
                    Color = entity.Color,
                    Linetype = entity.Linetype,
                };

                if (entity is DxfBlockReference insert)
                {
                    Enter(insert, entity.Handle, new List<string>(), new List<int>(), look, 0);
                    return;
                }

                _found.Add(
                    new CadFlatEntity(
                        entity,
                        entity.Handle,
                        _layout,
                        _scale,
                        Array.Empty<string>(),
                        Array.Empty<int>(),
                        null,
                        look.Layer,
                        look.Color,
                        look.Linetype
                    )
                );
            }

            /// <summary>The name a reference goes under in a path: the dynamic block's, not the anonymous block it draws.</summary>
            private string NameOf(DxfBlockReference insert, DxfBlockTableRecord block, out bool dynamic)
            {
                dynamic = false;

                // A reference is dynamic when its block is, or is the anonymous copy of one: asked once for each block, since asking
                // a reference costs a look through the block's data.
                if (!_maybeDynamic.TryGetValue(block.Name, out bool maybe))
                {
                    maybe = block.IsDynamicBlock || block.IsAnonymous;
                    _maybeDynamic.Add(block.Name, maybe);
                }

                if (!maybe)
                {
                    return block.Name;
                }

                try
                {
                    if (insert.IsDynamicBlock)
                    {
                        dynamic = true;
                        return insert.DynamicBlockTableRecord?.Name ?? block.Name;
                    }
                }
                catch (InvalidOperationException)
                {
                    // A reference whose block data cannot be read is told by the block it places.
                }

                return block.Name;
            }

            /// <summary>One reference as a single entity, where it cannot be walked.</summary>
            private void Leaf(DxfBlockReference insert, string handle, List<string> path, List<int> index, Look look)
            {
                _found.Add(
                    new CadFlatEntity(
                        insert,
                        handle,
                        _layout,
                        _scale,
                        path.ToArray(),
                        index.ToArray(),
                        null,
                        look.Layer,
                        look.Color,
                        look.Linetype
                    )
                );
            }

            /// <summary>Goes into a reference: its pieces, then its attributes, each a flat entity or, for a nested reference, walked in turn.</summary>
            private void Enter(
                DxfBlockReference insert,
                string handle,
                List<string> path,
                List<int> index,
                Look look,
                int depth
            )
            {
                DxfBlockTableRecord block = insert.BlockTableRecord;

                if (block == null)
                {
                    _report.Refuse(insert.Name, "the drawing has no block of that name");
                    Leaf(insert, handle, path, index, look);
                    return;
                }

                string name = NameOf(insert, block, out bool dynamic);

                if (block.IsFromExternalReference)
                {
                    _report.Xref(name);
                    Leaf(insert, handle, path, index, look);
                    return;
                }

                if (depth >= MostDepth)
                {
                    _report.Refuse(
                        name,
                        "nested more than " + MostDepth.ToString(CultureInfo.InvariantCulture) + " deep"
                    );
                    Leaf(insert, handle, path, index, look);
                    return;
                }

                bool clipped = false;

                try
                {
                    clipped = insert.SpatialFilter != null;
                }
                catch (InvalidOperationException)
                {
                    // A clip that cannot be read is not reported.
                }

                if (clipped)
                {
                    _report.Clip(name);
                }

                if (
                    _unitsSaid.Add(name)
                    && block.Units != DxfUnits.Undefined
                    && _insunits != DxfUnits.Undefined
                    && block.Units != _insunits
                )
                {
                    _report.BlockUnit(
                        name
                            + " is drawn in "
                            + block.Units
                            + " in a drawing of "
                            + _insunits
                            + " (INSERT scales by "
                            + insert.UnitFactor.ToString("0.######", CultureInfo.InvariantCulture)
                            + " when it places it)"
                    );
                }

                List<KeyValuePair<DxfEntity, GeoTransform3>> parts;

                try
                {
                    parts = Pieces(insert);
                }
                catch (Exception exception)
                    when (exception is ArgumentException
                        || exception is InvalidOperationException
                        || exception is NotSupportedException
                    )
                {
                    _report.Refuse(name, exception.Message);
                    Leaf(insert, handle, path, index, look);
                    return;
                }

                _report.Inserts++;
                path.Add(name);
                int at = 0;

                foreach (KeyValuePair<DxfEntity, GeoTransform3> pair in parts)
                {
                    index.Add(at++);
                    Part(pair.Key, pair.Value, handle, path, index, look, clipped, dynamic, depth);
                    index.RemoveAt(index.Count - 1);
                }

                foreach (DxfAttributeReference attribute in insert.AttributeCollection)
                {
                    index.Add(at++);
                    Add(
                        attribute,
                        insert.BlockTransform,
                        handle,
                        path,
                        index,
                        look.Of(attribute),
                        clipped,
                        dynamic,
                        false
                    );
                    index.RemoveAt(index.Count - 1);
                }

                path.RemoveAt(path.Count - 1);
            }

            private void Part(
                DxfEntity part,
                GeoTransform3 matrix,
                string handle,
                List<string> path,
                List<int> index,
                Look look,
                bool clipped,
                bool dynamic,
                int depth
            )
            {
                if (part is DxfBlockReference nested)
                {
                    // The look of the nested reference is what its own layer, colour and linetype make of the one it is in.
                    Look own = look.Of(nested);
                    int before = _found.Count;
                    Enter(nested, handle, path, index, own, depth + 1);

                    for (int i = before; i < _found.Count; i++)
                    {
                        CadFlatEntity made = _found[i];
                        made.Clipped |= clipped;
                        made.Dynamic |= dynamic;
                    }

                    return;
                }

                if (part is DxfAttributeDefinition definition)
                {
                    // A constant attribute shows its text; a variable one is shown by the attribute of the reference.
                    if (definition.Constant)
                    {
                        Add(part, matrix, handle, path, index, look.Of(part), clipped, dynamic, true);
                    }

                    return;
                }

                Add(part, matrix, handle, path, index, look.Of(part), clipped, dynamic, false);
            }

            private void Add(
                DxfEntity entity,
                GeoTransform3 matrix,
                string handle,
                List<string> path,
                List<int> index,
                Look look,
                bool clipped,
                bool dynamic,
                bool constant
            )
            {
                _found.Add(
                    new CadFlatEntity(
                        entity,
                        handle,
                        _layout,
                        _scale,
                        path.ToArray(),
                        index.ToArray(),
                        matrix,
                        look.Layer,
                        look.Color,
                        look.Linetype
                    )
                    {
                        Clipped = clipped,
                        Dynamic = dynamic,
                        Constant = constant,
                    }
                );
            }

            /// <summary>
            /// What a reference explodes into, each piece with the transformation from its block to the world. A MINSERT is made
            /// the plain reference of its first cell (a reference of no drawing, which the library explodes as any), and the
            /// pieces of the other cells are those moved along the turned axes of the array.
            /// </summary>
            private static List<KeyValuePair<DxfEntity, GeoTransform3>> Pieces(DxfBlockReference insert)
            {
                GeoTransform3 matrix = insert.BlockTransform;
                var result = new List<KeyValuePair<DxfEntity, GeoTransform3>>();

                if (!(insert is DxfMInsertBlock array))
                {
                    foreach (DxfEntity part in insert.Explode())
                    {
                        result.Add(new KeyValuePair<DxfEntity, GeoTransform3>(part, matrix));
                    }

                    return result;
                }

                var first = new DxfBlockReference(array.Position, array.BlockTableRecord) { BlockTransform = matrix };
                IReadOnlyList<DxfEntity> cell = first.Explode();
                long columns = Math.Max((int)array.Columns, 1);
                long rows = Math.Max((int)array.Rows, 1);

                if (cell.Count * columns * rows > MostEntities)
                {
                    throw new InvalidOperationException(
                        "its "
                            + (columns * rows).ToString(CultureInfo.InvariantCulture)
                            + " cells would hold more than the "
                            + MostEntities.ToString(CultureInfo.InvariantCulture)
                            + " entities the reader places"
                    );
                }

                OcsAxes(array.Normal, out GeoVector3 u, out GeoVector3 v);
                double angle = array.Rotation;
                GeoVector3 along = u.Multiply(Math.Cos(angle)).Add(v.Multiply(Math.Sin(angle)));
                GeoVector3 across = v.Multiply(Math.Cos(angle)).Subtract(u.Multiply(Math.Sin(angle)));

                for (int row = 0; row < rows; row++)
                {
                    for (int column = 0; column < columns; column++)
                    {
                        if (row == 0 && column == 0)
                        {
                            foreach (DxfEntity part in cell)
                            {
                                result.Add(new KeyValuePair<DxfEntity, GeoTransform3>(part, matrix));
                            }

                            continue;
                        }

                        GeoTransform3 move = GeoTransform3.Translation(
                            along.Multiply(column * array.ColumnSpacing).Add(across.Multiply(row * array.RowSpacing))
                        );
                        GeoTransform3 placed = move.Multiply(matrix);

                        foreach (DxfEntity part in cell)
                        {
                            result.Add(
                                new KeyValuePair<DxfEntity, GeoTransform3>(part.GetTransformedCopy(move), placed)
                            );
                        }
                    }
                }

                return result;
            }

            /// <summary>The axes of the coordinate system a normal makes (AutoCAD's arbitrary axis algorithm), as the library turns an array by.</summary>
            private static void OcsAxes(GeoVector3 normal, out GeoVector3 u, out GeoVector3 v)
            {
                GeoVector3 n = normal.Length > 0.0 ? normal.Divide(normal.Length) : new GeoVector3(0, 0, 1);
                GeoVector3 x =
                    Math.Abs(n.X) < 1.0 / 64.0 && Math.Abs(n.Y) < 1.0 / 64.0
                        ? new GeoVector3(0, 1, 0).CrossProduct(n)
                        : new GeoVector3(0, 0, 1).CrossProduct(n);
                u = x.Divide(x.Length);
                v = n.CrossProduct(u);
            }
        }
    }
}
