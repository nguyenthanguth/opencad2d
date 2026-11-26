using System.Collections.Generic;
using GeometryHelper.Geometry;

namespace OpenCAD2D.Mcp
{
    /// <summary>A row of the Properties palette: a label and the value as the palette writes it.</summary>
    internal sealed class PaletteRow
    {
        public string Label { get; set; }

        public string Value { get; set; }

        public bool ReadOnly { get; set; }
    }

    /// <summary>A group of rows of the Properties palette, such as <c>Geometry</c>.</summary>
    internal sealed class PaletteGroup
    {
        public string Title { get; set; }

        public IReadOnlyList<PaletteRow> Rows { get; set; }
    }

    /// <summary>What the Properties palette shows for some entities.</summary>
    internal sealed class PaletteView
    {
        /// <summary>Gets or sets the palette's label, such as <c>Line (1)</c>.</summary>
        public string Label { get; set; }

        public IReadOnlyList<PaletteGroup> Groups { get; set; }
    }

    /// <summary>
    /// What the read and query tools ask of a drawing beyond <see cref="IDrawingBackend"/>: one entity by handle, the Properties
    /// palette, the entities in an area, LIST, a copy to read the rare tables from, a system variable set and the snapshots. Both
    /// backends give it, the file by the engine and the app through its Remote API.
    /// </summary>
    internal interface IQueryBackend
    {
        /// <summary>Gets what identifies the drawing a snapshot belongs to: the document of a file, the id of the app's drawing.</summary>
        object DrawingKey { get; }

        /// <summary>The entity of a handle, or null where the drawing has none (or it is erased).</summary>
        EntityRef Find(string handle);

        /// <summary>What the Properties palette shows for the entities of the handles, as if they were selected.</summary>
        PaletteView Properties(IReadOnlyList<string> handles);

        /// <summary>The handles an area takes in a layout (null or "Model" for model space), whole; see <see cref="CadSpatial.Select"/>.</summary>
        IReadOnlyList<string> HandlesIn(
            string layout,
            CadAreaSelection how,
            IReadOnlyList<GeoPoint2> points,
            double tolerance
        );

        /// <summary>The handles selected in the drawing now: the user's in the app, the document's own for a file.</summary>
        IReadOnlyList<string> SelectedHandles();

        /// <summary>The lines LIST writes for the handles.</summary>
        IReadOnlyList<string> ListLines(IReadOnlyList<string> handles);

        /// <summary>
        /// A document to read what has no request of its own (layouts, multileader and table styles, block definitions): the drawing's
        /// own for a file, a copy of the app's drawing, changes not saved included, for the app. Read it; never change it.
        /// </summary>
        CadDocument ReadDocument();

        /// <summary>Sets a system variable as <c>setvar</c> does; one undo step where the drawing keeps it.</summary>
        void SetVariable(string name, object value);

        /// <summary>Keeps the drawing as it stands and gives what <see cref="ResetTo"/> takes.</summary>
        object TakeSnapshot();

        /// <summary>Puts the drawing back as the snapshot kept it; the undo steps made since are forgotten.</summary>
        void ResetTo(object snapshot);
    }
}
