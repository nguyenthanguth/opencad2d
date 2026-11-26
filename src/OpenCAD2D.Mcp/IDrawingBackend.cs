using System;
using System.Collections.Generic;
using GeometryHelper.Dxf;

namespace OpenCAD2D.Mcp
{
    /// <summary>An entity of a drawing with its handle: a copy for a drawing of the app, the drawing's own for a file.</summary>
    internal sealed class EntityRef
    {
        public EntityRef(string handle, DxfEntity entity)
        {
            Handle = handle;
            Entity = entity;
        }

        /// <summary>Gets the handle in the drawing, as <c>2F</c>.</summary>
        public string Handle { get; }

        /// <summary>Gets the entity; read it, never change it.</summary>
        public DxfEntity Entity { get; }
    }

    /// <summary>A layer of the drawing as the tools list it.</summary>
    internal sealed class LayerRow
    {
        public string Name { get; set; }

        /// <summary>Gets or sets the AutoCAD colour index, 1 to 255 (7 is white or black by the ground).</summary>
        public int Color { get; set; }

        public string Linetype { get; set; }

        /// <summary>Gets or sets the lineweight in hundredths of a millimetre; a negative number is a default, BYLAYER or BYBLOCK.</summary>
        public int LineWeight { get; set; }

        public bool IsOn { get; set; }

        public bool IsFrozen { get; set; }

        public bool IsLocked { get; set; }

        public bool IsPlottable { get; set; }

        public string Description { get; set; }
    }

    /// <summary>The boxes of what a space draws: a row for each handle, as <c>MinX, MinY, MaxX, MaxY</c> in four numbers.</summary>
    internal sealed class BoxRows
    {
        public BoxRows(string[] handles, double[] boxes)
        {
            Handles = handles;
            Boxes = boxes;
        }

        public string[] Handles { get; }

        /// <summary>Gets the boxes, four numbers a row.</summary>
        public double[] Boxes { get; }
    }

    /// <summary>The pairs a word overlaps: the entity of the word, the entity it overlaps and the box of the meeting.</summary>
    internal sealed class OverlapRows
    {
        public OverlapRows(string[] words, string[] others, double[] boxes)
        {
            Words = words;
            Others = others;
            Boxes = boxes;
        }

        public string[] Words { get; }

        public string[] Others { get; }

        /// <summary>Gets the boxes, four numbers a pair.</summary>
        public double[] Boxes { get; }
    }

    /// <summary>What a thing would take: the box of everything it draws (null for nothing) and five numbers a word frame.</summary>
    internal sealed class MeasureRows
    {
        public MeasureRows(double[] box, double[] frames)
        {
            Box = box;
            Frames = frames;
        }

        /// <summary>Gets the box as <c>MinX, MinY, MaxX, MaxY</c>; null where the thing draws nothing.</summary>
        public double[] Box { get; }

        /// <summary>Gets the frames, five numbers each: X, Y, width, height and the angle in radians.</summary>
        public double[] Frames { get; }
    }

    /// <summary>The scores and violations of a check against a standard, with the numbers as the answer writes them.</summary>
    internal sealed class StandardRows
    {
        public StandardRows(
            string name,
            double total,
            IReadOnlyList<RuleScoreInfo> rules,
            IReadOnlyList<ViolationInfo> violations
        )
        {
            Name = name;
            Total = total;
            Rules = rules;
            Violations = violations;
        }

        public string Name { get; }

        public double Total { get; }

        public IReadOnlyList<RuleScoreInfo> Rules { get; }

        public IReadOnlyList<ViolationInfo> Violations { get; }
    }

    /// <summary>What <c>render_png</c> asks for.</summary>
    internal sealed class RenderRequest
    {
        /// <summary>Gets or sets the layout; null for model space.</summary>
        public string Layout { get; set; }

        /// <summary>Gets or sets the part drawn as <c>MinX, MinY, MaxX, MaxY</c>; null for all of it.</summary>
        public double[] Box { get; set; }

        public int Width { get; set; }

        public int Height { get; set; }

        /// <summary>Gets or sets the pixels to an inch of paper on a layout; 0 where none is given.</summary>
        public double Dpi { get; set; }
    }

    /// <summary>
    /// The drawing the tools work on: a drawing file read and written through the engine, or a drawing of a running app
    /// through its Remote API. Every method that writes is one undo step. Handles are the drawing's, as <c>2F</c>.
    /// </summary>
    internal interface IDrawingBackend : IDisposable
    {
        /// <summary>Gets <c>file</c> for a drawing file worked on here, <c>app</c> for a drawing of a running app.</summary>
        string Mode { get; }

        string Name { get; }

        /// <summary>Gets the full path of the file, or null for a drawing made new and not saved.</summary>
        string Path { get; }

        bool IsModified { get; }

        string CurrentLayout { get; }

        string CurrentLayer { get; }

        string CurrentDimensionStyle { get; }

        IReadOnlyList<string> Layouts { get; }

        /// <summary>The entities of a layout (null or "Model" for model space) in the order it draws them, none erased.</summary>
        IReadOnlyList<EntityRef> Entities(string layout);

        IReadOnlyList<LayerRow> Layers();

        IReadOnlyList<DxfTextStyleTableRecord> TextStyles();

        IReadOnlyList<DxfDimStyleTableRecord> DimensionStyles();

        IReadOnlyList<DxfBlockTableRecord> Blocks();

        /// <summary>The names of the linetypes of the drawing.</summary>
        IReadOnlyList<string> Linetypes();

        /// <summary>The value of a system variable, such as <c>INSUNITS</c>.</summary>
        object Variable(string name);

        BoxRows Bounds(string layout);

        BoxRows BoundsOf(IReadOnlyList<string> handles);

        OverlapRows Overlaps(
            string layout,
            IReadOnlyList<string> words,
            IReadOnlyList<string> over,
            IReadOnlyList<string> handles,
            double tolerance
        );

        /// <summary>Measures an entity as it would be once added to the drawing, without adding it.</summary>
        MeasureRows Measure(DxfEntity entity);

        /// <summary>Measures the multileader MLEADER would make; the style null is the current one, the scale null the one MLEADER gives.</summary>
        MeasureRows MeasureLeader(
            string style,
            double arrowX,
            double arrowY,
            double landingX,
            double landingY,
            string words,
            double? scale
        );

        /// <summary>The empty rectangles of a frame <c>MinX, MinY, MaxX, MaxY</c>, four numbers each, the largest first.</summary>
        double[] FreeSpace(
            string layout,
            double[] frame,
            IReadOnlyList<string> kinds,
            double margin,
            double minWidth,
            double minHeight,
            double tolerance
        );

        /// <summary>Whether a candidate (X, Y, width, height, angle in radians) is clear of what the kinds name, the margin round it.</summary>
        bool Fits(string layout, double[] candidate, IReadOnlyList<string> kinds, double margin, double tolerance);

        /// <summary>Checks the drawing against a standard, given as its JSON text.</summary>
        StandardRows Check(string standard, double modelScale, double tolerance);

        /// <summary>The drawing as the JSON text of schema <c>opencad2d.drawing</c>; the layout null for model space and every layout.</summary>
        string ExportJson(string layout);

        /// <summary>The graph of the drawing as the JSON text of schema <c>opencad2d.graph</c>; the layout null for model space and every layout.</summary>
        string ExportGraph(string layout, double tolerance);

        /// <summary>Adds the entities to a layout as one undo step and gives their handles, in order.</summary>
        IReadOnlyList<string> Add(IReadOnlyList<DxfEntity> entities, string layout);

        /// <summary>Adds a layer with the look given, as one undo step; refused for a name the drawing has or a name or look it does not take.</summary>
        void AddLayer(LayerRow layer);

        void Move(IReadOnlyList<string> handles, double dx, double dy);

        void Rotate(IReadOnlyList<string> handles, double x, double y, double radians);

        void Scale(IReadOnlyList<string> handles, double x, double y, double factor);

        void Erase(IReadOnlyList<string> handles);

        /// <summary>Sets the layer, colour index and linetype of entities, where given, as one undo step.</summary>
        void SetProperties(IReadOnlyList<string> handles, string layer, int? color, string linetype);

        /// <summary>Takes back the last undo step; false when there was none to take back.</summary>
        bool Undo();

        /// <summary>Saves the drawing to its own file, or to another path where one is given, and gives the path written.</summary>
        string Save(string path);

        /// <summary>Draws a layout to a PNG and gives its bytes.</summary>
        byte[] RenderPng(RenderRequest request);

        /// <summary>Runs command text as typed on the command line and gives what it printed.</summary>
        string RunCommand(string text);
    }
}
