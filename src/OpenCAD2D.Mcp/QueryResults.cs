using System.Collections.Generic;

namespace OpenCAD2D.Mcp
{
    /// <summary>What <c>get_entity</c> answers: one entity in full.</summary>
    internal sealed class EntityDetail
    {
        public string Handle { get; set; }

        /// <summary>Gets or sets the DXF name of the kind: LINE, CIRCLE, ARC, LWPOLYLINE, TEXT, MTEXT, DIMENSION, INSERT, HATCH ...</summary>
        public string Kind { get; set; }

        public string Layer { get; set; }

        /// <summary>Gets or sets the colour as set: <c>BYLAYER</c>, <c>BYBLOCK</c>, an index such as <c>1</c>, or red, green and blue such as <c>255,128,0</c>.</summary>
        public string Color { get; set; }

        public string Linetype { get; set; }

        /// <summary>Gets or sets the lineweight in hundredths of a millimetre; a negative number is a default, BYLAYER or BYBLOCK.</summary>
        public int LineWeight { get; set; }

        public double LinetypeScale { get; set; }

        /// <summary>Gets or sets the box the entity takes as it is drawn, <c>[minX, minY, maxX, maxY]</c>; null where it draws nothing.</summary>
        public double[] Box { get; set; }

        /// <summary>Gets or sets the kind's own values in millimetres and degrees: the text of a TEXT, the vertices and bulges of a polyline, the measurement of a dimension.</summary>
        public Dictionary<string, object> Details { get; set; }

        /// <summary>Gets or sets what the Properties palette shows for the entity: its label and groups of rows.</summary>
        public PaletteView Properties { get; set; }
    }

    /// <summary>One group of <c>count_entities</c>.</summary>
    internal sealed class CountRow
    {
        public string Key { get; set; }

        public int Count { get; set; }
    }

    /// <summary>What <c>count_entities</c> answers.</summary>
    internal sealed class CountResult : PagedResult
    {
        /// <summary>Gets or sets what the entities were grouped by.</summary>
        public string GroupBy { get; set; }

        /// <summary>Gets or sets how many entities passed the filters, in all.</summary>
        public int Entities { get; set; }

        public IReadOnlyList<CountRow> Groups { get; set; }
    }

    /// <summary>One group of <c>aggregate</c>.</summary>
    internal sealed class AggregateRow
    {
        public string Key { get; set; }

        /// <summary>Gets or sets how many entities of the group have the measure.</summary>
        public int Count { get; set; }

        /// <summary>Gets or sets the entities of the group that have no such measure (a text has no length, an open polyline no area).</summary>
        public int Skipped { get; set; }

        public double Sum { get; set; }

        public double Min { get; set; }

        public double Max { get; set; }

        public double Average { get; set; }
    }

    /// <summary>What <c>aggregate</c> answers.</summary>
    internal sealed class AggregateResult : PagedResult
    {
        /// <summary>Gets or sets <c>length</c> (millimetres) or <c>area</c> (square millimetres).</summary>
        public string Measure { get; set; }

        public string GroupBy { get; set; }

        /// <summary>Gets or sets how many entities passed the filters, in all.</summary>
        public int Entities { get; set; }

        public IReadOnlyList<AggregateRow> Groups { get; set; }
    }

    /// <summary>One viewport of a layout.</summary>
    internal sealed class ViewportInfo
    {
        public string Handle { get; set; }

        /// <summary>Gets or sets the viewport's number; the viewport of the sheet itself is number 1.</summary>
        public int Number { get; set; }

        public bool On { get; set; }

        public bool Locked { get; set; }

        /// <summary>Gets or sets where the viewport stands on the sheet, <c>[x, y]</c> of its centre in paper millimetres.</summary>
        public double[] Center { get; set; }

        public double Width { get; set; }

        public double Height { get; set; }

        /// <summary>Gets or sets the part of model space it shows: the centre, <c>[x, y]</c>.</summary>
        public double[] ViewCenter { get; set; }

        public double ViewHeight { get; set; }

        /// <summary>Gets or sets the paper length of one model unit: 0.1 for 1:10.</summary>
        public double CustomScale { get; set; }

        /// <summary>Gets or sets the scale as it is written, <c>1:10</c> or <c>2:1</c>; null where the viewport has none.</summary>
        public string Scale { get; set; }

        /// <summary>Gets or sets the layers frozen in this viewport only.</summary>
        public IReadOnlyList<string> FrozenLayers { get; set; }
    }

    /// <summary>One layout in <c>list_layouts</c>.</summary>
    internal sealed class LayoutInfo
    {
        public string Name { get; set; }

        /// <summary>Gets or sets the place of its tab; the Model tab is 0.</summary>
        public int TabOrder { get; set; }

        public bool Current { get; set; }

        /// <summary>Gets or sets the sheet <c>[width, height]</c> in the paper units; null for model space.</summary>
        public double[] PaperSize { get; set; }

        /// <summary>Gets or sets <c>millimetres</c>, <c>inches</c> or <c>pixels</c>.</summary>
        public string PaperUnits { get; set; }

        /// <summary>Gets or sets the name of the paper, such as <c>ISO_A3_(420.00_x_297.00_MM)</c>.</summary>
        public string Paper { get; set; }

        /// <summary>Gets or sets the quarter turns of the plot in degrees: 0, 90, 180 or 270.</summary>
        public int Rotation { get; set; }

        public int Entities { get; set; }

        public IReadOnlyList<ViewportInfo> Viewports { get; set; }
    }

    /// <summary>One linetype in <c>list_linetypes</c>.</summary>
    internal sealed class LinetypeInfo
    {
        public string Name { get; set; }

        public string Description { get; set; }

        /// <summary>Gets or sets the length of one repeat of the pattern, in millimetres.</summary>
        public double PatternLength { get; set; }

        /// <summary>Gets or sets the dashes of the pattern: a positive number a dash, a negative a gap, 0 a dot.</summary>
        public IReadOnlyList<double> Dashes { get; set; }
    }

    /// <summary>One multileader style in <c>list_multileader_styles</c>.</summary>
    internal sealed class MLeaderStyleInfo
    {
        public string Name { get; set; }

        public bool Current { get; set; }

        /// <summary>Gets or sets <c>straight</c>, <c>spline</c> or <c>none</c>.</summary>
        public string LeaderType { get; set; }

        public string Arrowhead { get; set; }

        public double ArrowSize { get; set; }

        public bool Landing { get; set; }

        public double LandingDistance { get; set; }

        public double LandingGap { get; set; }

        public string TextStyle { get; set; }

        public double TextHeight { get; set; }

        /// <summary>Gets or sets the overall scale.</summary>
        public double Scale { get; set; }

        /// <summary>Gets or sets whether the style is annotative, which takes its scale from the annotation scale.</summary>
        public bool Annotative { get; set; }

        /// <summary>Gets or sets the scale as the style holds it, not rounded; read by measure_leader, never written out.</summary>
        internal double DrawnScale { get; set; }

        /// <summary>Gets or sets <c>text</c>, <c>block</c> or <c>none</c>.</summary>
        public string Content { get; set; }

        public string DefaultText { get; set; }

        public int MaxLeaderPoints { get; set; }
    }

    /// <summary>One cell style (title, header or data) of a table style.</summary>
    internal sealed class TableCellStyleInfo
    {
        public string TextStyle { get; set; }

        public double TextHeight { get; set; }

        /// <summary>Gets or sets the alignment, such as <c>MiddleCenter</c>.</summary>
        public string Alignment { get; set; }
    }

    /// <summary>One table style in <c>list_table_styles</c>.</summary>
    internal sealed class TableStyleInfo
    {
        public string Name { get; set; }

        public bool Current { get; set; }

        public string Description { get; set; }

        public double HorizontalMargin { get; set; }

        public double VerticalMargin { get; set; }

        public bool TitleShown { get; set; }

        public bool HeaderShown { get; set; }

        public TableCellStyleInfo Title { get; set; }

        public TableCellStyleInfo Header { get; set; }

        public TableCellStyleInfo Data { get; set; }
    }

    /// <summary>A system variable's value as <c>get_variables</c> gives it.</summary>
    internal sealed class VariableValue
    {
        public string Name { get; set; }

        /// <summary>Gets or sets the value: a whole number, a real (an angle in degrees), a text, or a point <c>[x, y]</c> or <c>[x, y, z]</c>; null where the variable is not known.</summary>
        public object Value { get; set; }

        /// <summary>Gets or sets <c>integer</c>, <c>real</c>, <c>angle</c>, <c>text</c> or <c>point</c>.</summary>
        public string Kind { get; set; }

        /// <summary>Gets or sets whether <c>set_variable</c> takes it.</summary>
        public bool Settable { get; set; }

        public string Meaning { get; set; }

        /// <summary>Gets or sets the reason where the variable could not be read.</summary>
        public string Error { get; set; }
    }

    /// <summary>What <c>get_variables</c> answers.</summary>
    internal sealed class VariablesResult
    {
        public IReadOnlyList<VariableValue> Variables { get; set; }
    }

    /// <summary>What <c>set_variable</c> answers.</summary>
    internal sealed class VariableSetResult
    {
        public string Name { get; set; }

        /// <summary>Gets or sets the value the drawing has now, as <c>get_variables</c> gives it.</summary>
        public object Value { get; set; }
    }

    /// <summary>One word of <c>get_texts</c>.</summary>
    internal sealed class TextRow
    {
        public string Handle { get; set; }

        /// <summary>Gets or sets TEXT, MTEXT, MULTILEADER, or ATTRIB for the value of an attribute of a block reference (then <c>handle</c> is the block reference's).</summary>
        public string Kind { get; set; }

        public string Layer { get; set; }

        public string Text { get; set; }

        public double[] At { get; set; }

        public double Height { get; set; }

        /// <summary>Gets or sets the rotation in degrees.</summary>
        public double Rotation { get; set; }

        public string Style { get; set; }

        /// <summary>Gets or sets the tag of an attribute; null for the others.</summary>
        public string Tag { get; set; }

        public double[] Box { get; set; }
    }

    /// <summary>What <c>get_texts</c> answers.</summary>
    internal sealed class TextsResult : PagedResult
    {
        public IReadOnlyList<TextRow> Texts { get; set; }
    }

    /// <summary>One dimension of <c>get_dimensions</c>.</summary>
    internal sealed class DimensionRow
    {
        public string Handle { get; set; }

        public string Layer { get; set; }

        /// <summary>Gets or sets the kind of dimension, such as <c>RotatedDimension</c>, <c>RadialDimension</c>, <c>LineAngularDimension2</c>.</summary>
        public string Type { get; set; }

        public string Style { get; set; }

        /// <summary>Gets or sets the measured value: millimetres, or degrees for an angular dimension.</summary>
        public double Measurement { get; set; }

        /// <summary>Gets or sets <c>mm</c> or <c>degrees</c>.</summary>
        public string Unit { get; set; }

        /// <summary>Gets or sets the text the dimension is drawn with; null where its picture has none.</summary>
        public string Text { get; set; }

        /// <summary>Gets or sets the text typed over the measurement (<c>&lt;&gt;</c> is the measurement); empty where there is none.</summary>
        public string TextOverride { get; set; }

        /// <summary>Gets or sets the defining points by name, <c>[x, y]</c> each.</summary>
        public Dictionary<string, double[]> Points { get; set; }

        public double[] TextPosition { get; set; }

        public double[] Box { get; set; }
    }

    /// <summary>What <c>get_dimensions</c> answers.</summary>
    internal sealed class DimensionsResult : PagedResult
    {
        public IReadOnlyList<DimensionRow> Dimensions { get; set; }
    }

    /// <summary>One attribute of a block reference or a definition.</summary>
    internal sealed class AttributeRow
    {
        public string Tag { get; set; }

        /// <summary>Gets or sets the value of a block reference's attribute; the default text of a definition.</summary>
        public string Value { get; set; }

        /// <summary>Gets or sets the prompt of a definition.</summary>
        public string Prompt { get; set; }

        public bool Constant { get; set; }

        public bool Invisible { get; set; }

        public double[] At { get; set; }

        public double Height { get; set; }
    }

    /// <summary>What <c>get_attributes</c> answers.</summary>
    internal sealed class AttributesResult
    {
        public string Handle { get; set; }

        public string Block { get; set; }

        public IReadOnlyList<AttributeRow> Attributes { get; set; }

        /// <summary>Gets or sets the tags the block defines that this reference has no attribute for.</summary>
        public IReadOnlyList<string> Missing { get; set; }
    }

    /// <summary>What <c>get_block</c> answers: the definition of a block.</summary>
    internal sealed class BlockDetail : PagedResult
    {
        public string Name { get; set; }

        /// <summary>Gets or sets the base point of the definition, <c>[x, y]</c>.</summary>
        public double[] BasePoint { get; set; }

        public string Units { get; set; }

        public bool Explodable { get; set; }

        public IReadOnlyList<AttributeRow> AttributeDefinitions { get; set; }

        /// <summary>Gets or sets the entities of the definition with no handle, paged.</summary>
        public IReadOnlyList<EntityInfo> Entities { get; set; }
    }

    /// <summary>What <c>measure_distance</c> answers, as DIST writes it.</summary>
    internal sealed class DistanceResult
    {
        public double Distance { get; set; }

        /// <summary>Gets or sets the angle in the XY plane, in degrees from 0 to 360.</summary>
        public double Angle { get; set; }

        public double DeltaX { get; set; }

        public double DeltaY { get; set; }
    }

    /// <summary>What <c>measure_area</c> answers, as AREA writes it.</summary>
    internal sealed class AreaResult
    {
        public double Area { get; set; }

        /// <summary>Gets or sets the length of the boundary.</summary>
        public double Perimeter { get; set; }

        /// <summary>Gets or sets what was measured: <c>points</c>, or the kind of the entity.</summary>
        public string Of { get; set; }
    }

    /// <summary>What <c>measure_length</c> answers.</summary>
    internal sealed class LengthResult
    {
        public string Handle { get; set; }

        public string Kind { get; set; }

        public double Length { get; set; }
    }

    /// <summary>What <c>list_properties</c> answers: the lines LIST writes.</summary>
    internal sealed class ListResult
    {
        public IReadOnlyList<string> Lines { get; set; }
    }

    /// <summary>What <c>id_point</c> answers, as ID writes it, and the entities at the point.</summary>
    internal sealed class IdPointResult
    {
        public double X { get; set; }

        public double Y { get; set; }

        public double Aperture { get; set; }

        /// <summary>Gets or sets the entities that pass within the aperture of the point, the one picked first leading.</summary>
        public IReadOnlyList<EntityInfo> Entities { get; set; }
    }

    /// <summary>What <c>select</c> and <c>select_similar</c> answer.</summary>
    internal sealed class SelectResult : PagedResult
    {
        public string Mode { get; set; }

        public IReadOnlyList<string> Handles { get; set; }
    }

    /// <summary>What <c>server_status</c> answers.</summary>
    internal sealed class StatusResult
    {
        public string Server { get; set; }

        public string Version { get; set; }

        /// <summary><c>file</c> or <c>app</c>: the way the server was started.</summary>
        public string Mode { get; set; }

        public bool ReadOnly { get; set; }

        public bool ScriptsAllowed { get; set; }

        /// <summary>Gets or sets the process id the server was told to attach to; 0 for the app last used.</summary>
        public int ProcessId { get; set; }

        /// <summary>Gets or sets whether a drawing is open for the tools.</summary>
        public bool DrawingOpen { get; set; }

        public DrawingResult Drawing { get; set; }

        public string CurrentLayout { get; set; }

        public string CurrentLayer { get; set; }

        /// <summary>Gets or sets the snapshots the server holds, <c>s1</c>, <c>s2</c> ...</summary>
        public IReadOnlyList<string> Snapshots { get; set; }

        /// <summary>Gets or sets why the app is out of reach, where the server is attached and could not ask it.</summary>
        public string Problem { get; set; }
    }

    /// <summary>One kind in <c>describe_kinds</c>.</summary>
    internal sealed class KindDescription
    {
        /// <summary>Gets or sets the DXF name, as <c>query_entities</c> gives it and <c>kinds</c> takes it.</summary>
        public string Kind { get; set; }

        public string What { get; set; }

        /// <summary>Gets or sets the tool that makes it; null where the server only reads the kind.</summary>
        public string Written { get; set; }

        /// <summary>Gets or sets the values <c>get_entity</c> gives in <c>details</c> for the kind.</summary>
        public IReadOnlyList<string> Fields { get; set; }

        /// <summary>Gets or sets the arguments the writing tool takes, where there is one.</summary>
        public IReadOnlyList<string> Arguments { get; set; }
    }

    /// <summary>What <c>snapshot</c> answers.</summary>
    internal sealed class SnapshotResult
    {
        /// <summary>Gets or sets the id to reset the drawing with, <c>s1</c>.</summary>
        public string Snapshot { get; set; }

        public string Drawing { get; set; }

        /// <summary>Gets or sets how many entities the drawing had.</summary>
        public int Entities { get; set; }
    }

    /// <summary>What <c>reset</c> and <c>release_snapshot</c> answer.</summary>
    internal sealed class ResetResult
    {
        public string Snapshot { get; set; }

        /// <summary>Gets or sets what was done: <c>reset</c> or <c>released</c>.</summary>
        public string Done { get; set; }

        /// <summary>Gets or sets how many entities the drawing has now.</summary>
        public int Entities { get; set; }

        public string Note { get; set; }
    }
}
