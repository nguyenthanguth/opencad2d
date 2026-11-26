using System;
using System.Collections.Generic;
using GeometryHelper.Dxf;
using GeometryHelper.Geometry;

namespace OpenCAD2D.Remote
{
    /// <summary>The table records of a drawing, added in a commit, and the styles and linetypes an inserted object names.</summary>
    public sealed partial class CadRemoteDocument
    {
        // The names of the text styles, linetypes and dimension styles the app has, asked once for a commit.
        private readonly Dictionary<string, HashSet<string>> _appHas = new Dictionary<string, HashSet<string>>(
            StringComparer.Ordinal
        );

        /// <summary>
        /// Gets the names of the linetypes of the drawing, in the order of its table, asked of the app each time:
        /// <c>ByBlock</c>, <c>ByLayer</c> and <c>Continuous</c> in a new drawing.
        /// </summary>
        /// <exception cref="CadRemoteException">Thrown when the drawing is closed.</exception>
        public IReadOnlyList<string> Linetypes
        {
            get
            {
                var names = new List<string>();

                foreach (object name in Ask(RemoteOps.Linetypes).GetArray("names"))
                {
                    names.Add(name as string ?? string.Empty);
                }

                return names;
            }
        }

        /// <summary>
        /// Gets or sets the name of the dimension style new dimensions are drawn in, as the style box and Set Current do: the
        /// setting takes effect at once, as one undo step of the app (DIMSTYLE), not at <see cref="CommitChanges"/>.
        /// </summary>
        /// <exception cref="ArgumentNullException">Thrown when the name set is null.</exception>
        /// <exception cref="CadRemoteException">Thrown when the drawing is closed, or on setting a style the drawing has not.</exception>
        public string CurrentDimensionStyle
        {
            get => Ask(RemoteOps.CurrentDimStyle).GetString("name");
            set
            {
                if (value == null)
                {
                    throw new ArgumentNullException(nameof(value));
                }

                Ask(RemoteOps.CurrentDimStyle, request => request.Set("name", value));
            }
        }

        /// <summary>
        /// Queues the table records and the blocks of a drawing of yours to be added to the drawing when
        /// <see cref="CommitChanges"/> is called: its text styles, linetypes, blocks (each with its objects) and dimension styles
        /// that the small drawing of the commit has none of the name of, as <see cref="DxfDatabase.Insert(GeoTransform3, DxfDatabase, bool)"/>
        /// merges them. They are written before the layers and the objects of the commit, so that a layer can have the linetype,
        /// and an object the style or block, that the same commit adds. A record of a name the app's drawing has already is the
        /// app's own, as a merge keeps it, and the one given is left out. The records are copied now, so changing them
        /// afterwards changes nothing.
        /// </summary>
        /// <remarks>
        /// A text style of <c>new DxfDatabase("AC1027")</c> given the name NOTE and the font romans.shx, added to that drawing's
        /// <c>TextStyleTable</c>, then given here, is the text style NOTE in the app after the commit.
        /// </remarks>
        /// <param name="records">A drawing of your own, made in code, whose model space holds no object.</param>
        /// <exception cref="ArgumentNullException">Thrown when the drawing is null.</exception>
        /// <exception cref="ArgumentException">Thrown when the model space of the drawing holds an object, or the drawing is one this call cannot take.</exception>
        public void AddRecords(DxfDatabase records)
        {
            if (records == null)
            {
                throw new ArgumentNullException(nameof(records));
            }

            foreach (DxfEntity entity in records.ModelSpace)
            {
                if (!entity.IsErased)
                {
                    throw new ArgumentException(
                        "AddRecords takes the table records and blocks of a drawing: its model space holds objects, which Insert queues.",
                        nameof(records)
                    );
                }
            }

            DxfDatabase batch = Batch;
            var tables = new JsonObject();
            tables.Set("STYLE", Missing(records.TextStyleTable, batch.TextStyleTable, r => r.Name));
            tables.Set("LTYPE", Missing(records.LinetypeTable, batch.LinetypeTable, r => r.Name));
            tables.Set("DIMSTYLE", Missing(records.DimStyleTable, batch.DimStyleTable, r => r.Name));
            var blocks = new JsonArray();

            foreach (DxfBlockTableRecord block in records.BlockTable)
            {
                if (
                    !block.IsLayout
                    && !block.IsAnonymous
                    && !block.Name.StartsWith("*", StringComparison.Ordinal)
                    && !block.IsFromExternalReference
                    && !block.IsDependent
                    && !batch.BlockTable.Has(block.Name)
                )
                {
                    blocks.Add(block.Name);
                }
            }

            tables.Set("BLOCK", blocks);
            batch.Insert(GeoTransform3.Identity, records, true);
            _queue.Add(new Pending { Kind = "records", Tables = tables });
        }

        private static JsonArray Missing<T>(IEnumerable<T> records, DxfSymbolTable have, Func<T, string> name)
        {
            var missing = new JsonArray();

            foreach (T record in records)
            {
                if (!have.Has(name(record)))
                {
                    missing.Add(name(record));
                }
            }

            return missing;
        }

        /// <summary>
        /// Gives the small drawing of the commit the text style and the linetype an object names, which it holds the object only
        /// with: a name the commit's own records hold is there already, and one the app's drawing has is made as a record of
        /// that name alone, so that the app keeps its own record when the object is copied in. A name neither has is left, and
        /// the library refuses the object as it refuses any that names a style its drawing has not.
        /// </summary>
        private void Provide(DxfEntity entity) => Provide(entity, Batch);

        private void Provide(DxfEntity entity, DxfDatabase batch)
        {
            string linetype = entity.Linetype;

            if (
                !string.IsNullOrEmpty(linetype)
                && !batch.LinetypeTable.Has(linetype)
                && !string.Equals(linetype, "ByLayer", StringComparison.OrdinalIgnoreCase)
                && !string.Equals(linetype, "ByBlock", StringComparison.OrdinalIgnoreCase)
                && AppHas("LTYPE", linetype)
            )
            {
                batch.LinetypeTable.Add(new DxfLinetypeTableRecord { Name = linetype });
            }

            string text =
                entity is DxfDBText single ? single.TextStyleName
                : entity is DxfMText multi ? multi.TextStyleName
                : null;

            if (!string.IsNullOrEmpty(text) && !batch.TextStyleTable.Has(text) && AppHas("STYLE", text))
            {
                batch.TextStyleTable.Add(new DxfTextStyleTableRecord { Name = text });
            }
        }

        /// <summary>Whether the app's drawing has a text style (<c>STYLE</c>) or a linetype (<c>LTYPE</c>) of the name, letters of either case alike; the names are asked of the app once for a commit.</summary>
        private bool AppHas(string kind, string name)
        {
            if (!_appHas.TryGetValue(kind, out HashSet<string> names))
            {
                names = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

                if (kind == "LTYPE")
                {
                    names.UnionWith(Linetypes);
                }
                else
                {
                    foreach (DxfTextStyleTableRecord style in TextStyles)
                    {
                        names.Add(style.Name);
                    }
                }

                _appHas[kind] = names;
            }

            return names.Contains(name);
        }
    }
}
