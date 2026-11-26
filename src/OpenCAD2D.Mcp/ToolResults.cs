using System.Collections.Generic;
using System.Text.Json;

namespace OpenCAD2D.Mcp
{
    /// <summary>What <c>open_drawing</c>, <c>new_drawing</c> and <c>save</c> answer: which drawing the tools work on now.</summary>
    internal sealed class DrawingResult
    {
        /// <summary>Gets or sets <c>file</c> for a drawing worked on in the server's process, <c>app</c> for a drawing of a running app.</summary>
        public string Mode { get; set; }

        public string Name { get; set; }

        /// <summary>Gets or sets the full path of the file; null for a drawing not saved yet.</summary>
        public string Path { get; set; }

        public bool Modified { get; set; }
    }

    /// <summary>What <c>info</c> answers.</summary>
    internal sealed class InfoResult
    {
        public string Mode { get; set; }

        public string Name { get; set; }

        public string Path { get; set; }

        public bool Modified { get; set; }

        /// <summary>Gets or sets whether the server refuses every tool that writes.</summary>
        public bool ReadOnly { get; set; }

        /// <summary>Gets or sets whether <c>run_command</c> is allowed.</summary>
        public bool ScriptsAllowed { get; set; }

        /// <summary>Gets or sets the drawing units of INSUNITS, as <c>millimetres</c>.</summary>
        public string Units { get; set; }

        public string CurrentLayout { get; set; }

        public string CurrentLayer { get; set; }

        public string CurrentDimensionStyle { get; set; }

        public IReadOnlyList<string> Layouts { get; set; }

        /// <summary>Gets or sets the layout the extents and counts are of.</summary>
        public string Layout { get; set; }

        /// <summary>Gets or sets the box everything of the layout is drawn in, <c>[minX, minY, maxX, maxY]</c>; null for an empty layout.</summary>
        public double[] Extents { get; set; }

        /// <summary>Gets or sets how many entities of each kind the layout has, kinds in alphabetical order.</summary>
        public SortedDictionary<string, int> Counts { get; set; }

        public int EntityCount { get; set; }
    }

    /// <summary>One layer in <c>list_layers</c>.</summary>
    internal sealed class LayerInfo
    {
        public string Name { get; set; }

        public int Color { get; set; }

        public string Linetype { get; set; }

        /// <summary>Gets or sets the lineweight in hundredths of a millimetre; a negative number is a default.</summary>
        public int LineWeight { get; set; }

        public bool On { get; set; }

        public bool Frozen { get; set; }

        public bool Locked { get; set; }

        public bool Plottable { get; set; }

        public string Description { get; set; }
    }

    /// <summary>One text style in <c>list_text_styles</c>.</summary>
    internal sealed class TextStyleInfo
    {
        public string Name { get; set; }

        public string Font { get; set; }

        /// <summary>Gets or sets the fixed height of the style; 0 where each text sets its own.</summary>
        public double Height { get; set; }

        public double WidthFactor { get; set; }

        /// <summary>Gets or sets whether the style is the one new texts take.</summary>
        public bool Current { get; set; }
    }

    /// <summary>One dimension style in <c>list_dimension_styles</c>.</summary>
    internal sealed class DimensionStyleInfo
    {
        public string Name { get; set; }

        /// <summary>Gets or sets the overall scale (DIMSCALE).</summary>
        public double Scale { get; set; }

        /// <summary>Gets or sets the height of the dimension text (DIMTXT).</summary>
        public double TextHeight { get; set; }

        /// <summary>Gets or sets the arrow size (DIMASZ).</summary>
        public double ArrowSize { get; set; }

        /// <summary>Gets or sets the decimal places of a length (DIMDEC).</summary>
        public int Decimals { get; set; }

        public string TextStyle { get; set; }

        public bool Current { get; set; }
    }

    /// <summary>One block in <c>list_blocks</c>.</summary>
    internal sealed class BlockInfo
    {
        public string Name { get; set; }

        /// <summary>Gets or sets the number of entities in the block's definition.</summary>
        public int Entities { get; set; }
    }

    /// <summary>A page of a long answer: the numbers that say where it is in the whole.</summary>
    internal class PagedResult
    {
        /// <summary>Gets or sets how many rows there are in all.</summary>
        public int Total { get; set; }

        /// <summary>Gets or sets the page, numbered from 1.</summary>
        public int Page { get; set; }

        public int PageSize { get; set; }

        /// <summary>Gets or sets the number of pages; 0 for no rows.</summary>
        public int Pages { get; set; }
    }

    /// <summary>What <c>query_entities</c> answers.</summary>
    internal sealed class EntitiesResult : PagedResult
    {
        public IReadOnlyList<EntityInfo> Entities { get; set; }
    }

    /// <summary>One entity box in <c>bounds</c>.</summary>
    internal sealed class BoundsRow
    {
        public string Handle { get; set; }

        /// <summary>Gets or sets <c>[minX, minY, maxX, maxY]</c>.</summary>
        public double[] Box { get; set; }
    }

    /// <summary>What <c>bounds</c> answers.</summary>
    internal sealed class BoundsResult : PagedResult
    {
        public IReadOnlyList<BoundsRow> Rows { get; set; }
    }

    /// <summary>One pair in <c>overlaps</c>: a word and what it is over.</summary>
    internal sealed class OverlapRow
    {
        /// <summary>Gets or sets the handle of the entity whose word it is.</summary>
        public string Word { get; set; }

        /// <summary>Gets or sets the handle of the entity the word overlaps.</summary>
        public string Other { get; set; }

        /// <summary>Gets or sets the box of the places they meet, <c>[minX, minY, maxX, maxY]</c>.</summary>
        public double[] Box { get; set; }
    }

    /// <summary>What <c>overlaps</c> answers.</summary>
    internal sealed class OverlapsResult : PagedResult
    {
        public IReadOnlyList<OverlapRow> Rows { get; set; }
    }

    /// <summary>What an <c>add_*</c> tool answers: the handle the entity has in the drawing.</summary>
    internal sealed class AddedResult
    {
        public string Handle { get; set; }

        public string Kind { get; set; }
    }

    /// <summary>What <c>move</c>, <c>rotate</c>, <c>scale</c>, <c>erase</c> and <c>set_properties</c> answer.</summary>
    internal sealed class ChangedResult
    {
        public int Changed { get; set; }

        public IReadOnlyList<string> Handles { get; set; }
    }

    /// <summary>What <c>undo</c> answers.</summary>
    internal sealed class UndoResult
    {
        /// <summary>Gets or sets whether a step was taken back.</summary>
        public bool Undone { get; set; }

        public string Message { get; set; }
    }

    /// <summary>What <c>run_command</c> answers.</summary>
    internal sealed class CommandResult
    {
        /// <summary>Gets or sets what the command line wrote.</summary>
        public string Output { get; set; }
    }

    /// <summary>The frame of one word: where it stands and how it is turned, as <c>measure_*</c> gives it and <c>fits</c> takes it.</summary>
    internal sealed class FrameInfo
    {
        /// <summary>Gets or sets the X of the first corner, the foot of the start of the word.</summary>
        public double X { get; set; }

        /// <summary>Gets or sets the Y of the first corner.</summary>
        public double Y { get; set; }

        /// <summary>Gets or sets the length of the word along its baseline.</summary>
        public double Width { get; set; }

        /// <summary>Gets or sets the height of the frame, across the baseline.</summary>
        public double Height { get; set; }

        /// <summary>Gets or sets the angle the width runs at, in degrees counter-clockwise from the +x direction.</summary>
        public double Rotation { get; set; }
    }

    /// <summary>What the <c>measure_*</c> tools answer: what a thing would take on the page, found before it is added.</summary>
    internal sealed class MeasureResult
    {
        /// <summary>Gets or sets the box of everything it draws, <c>[minX, minY, maxX, maxY]</c>; null where it draws nothing.</summary>
        public double[] Box { get; set; }

        /// <summary>Gets or sets the frame of each word, in the order they are drawn.</summary>
        public IReadOnlyList<FrameInfo> Frames { get; set; }
    }

    /// <summary>What <c>free_space</c> answers: the empty rectangles of a frame, the largest first.</summary>
    internal sealed class FreeSpaceResult : PagedResult
    {
        /// <summary>Gets or sets the rectangles of the page, each <c>[minX, minY, maxX, maxY]</c>.</summary>
        public IReadOnlyList<double[]> Boxes { get; set; }
    }

    /// <summary>What <c>fits</c> answers.</summary>
    internal sealed class FitsResult
    {
        /// <summary>Gets or sets whether nothing of the kinds asked about is in the candidate or within the margin of it.</summary>
        public bool Fits { get; set; }
    }

    /// <summary>How one rule of a standard fared in <c>check_standard</c>.</summary>
    internal sealed class RuleScoreInfo
    {
        /// <summary>Gets or sets the id of the rule, as <c>text-height</c> or <c>gap-word-line</c>.</summary>
        public string Rule { get; set; }

        /// <summary>Gets or sets the score, from 0 (every subject offends) to 1 (none does).</summary>
        public double Score { get; set; }

        /// <summary>Gets or sets how many things the rule looked at.</summary>
        public int Subjects { get; set; }

        /// <summary>Gets or sets how many of them offend.</summary>
        public int Offenders { get; set; }

        /// <summary>Gets or sets the weight of the rule in the total score.</summary>
        public double Weight { get; set; }
    }

    /// <summary>One place the drawing breaks a rule of the standard in <c>check_standard</c>.</summary>
    internal sealed class ViolationInfo
    {
        /// <summary>Gets or sets the id of the rule broken.</summary>
        public string Rule { get; set; }

        /// <summary>Gets or sets the handle of the object, or of the record of the layer or style, that breaks the rule.</summary>
        public string Handle { get; set; }

        /// <summary>Gets or sets the handle of the second object where the rule is about two; null otherwise.</summary>
        public string Other { get; set; }

        /// <summary>Gets or sets the name of the space the object is in; empty for a record of a table.</summary>
        public string Space { get; set; }

        /// <summary>Gets or sets the handle of the viewport the object was measured through; null where none.</summary>
        public string Through { get; set; }

        /// <summary>Gets or sets the place <c>[minX, minY, maxX, maxY]</c>; null where the violation has none.</summary>
        public double[] Box { get; set; }

        /// <summary>Gets or sets what was found, as a sentence: <c>3.2 mm</c>.</summary>
        public string Measured { get; set; }

        /// <summary>Gets or sets what the standard allows, as a sentence: <c>2.5, 3.5, 5, 7 mm</c>.</summary>
        public string Allowed { get; set; }

        /// <summary>Gets or sets the number found, in millimetres on paper; null where the rule is about a name.</summary>
        public double? Value { get; set; }

        /// <summary>Gets or sets the number allowed against <see cref="Value"/>; null where the rule is about a name.</summary>
        public double? Limit { get; set; }
    }

    /// <summary>What <c>check_standard</c> answers: the scores, and a page of the violations.</summary>
    internal sealed class StandardResult : PagedResult
    {
        /// <summary>Gets or sets the name of the standard.</summary>
        public string Name { get; set; }

        /// <summary>Gets or sets the total score from 0 to 1: the weighted mean of the rules the standard runs.</summary>
        public double Score { get; set; }

        /// <summary>Gets or sets the score of each rule the standard runs.</summary>
        public IReadOnlyList<RuleScoreInfo> Rules { get; set; }

        /// <summary>Gets or sets the violations of the page, sorted by rule, then handle.</summary>
        public IReadOnlyList<ViolationInfo> Violations { get; set; }
    }

    /// <summary>What <c>export_json</c> and <c>export_graph</c> answer: what the document holds, and a page of its rows.</summary>
    internal sealed class ExportResult : PagedResult
    {
        /// <summary>Gets or sets the members of the document that are not lists (<c>schema</c>, <c>version</c>, <c>units</c>, <c>tolerance</c>).</summary>
        public SortedDictionary<string, JsonElement> Header { get; set; }

        /// <summary>Gets or sets how many rows each list of the document has: <c>layers</c>, <c>blocks</c>, <c>entities</c>, <c>edges</c>.</summary>
        public SortedDictionary<string, int> Tables { get; set; }

        /// <summary>Gets or sets how many rows of each kind the whole list has, kinds in alphabetical order.</summary>
        public SortedDictionary<string, int> Kinds { get; set; }

        /// <summary>Gets or sets the name of the list the rows are of: <c>entities</c>, <c>nodes</c> or <c>edges</c>.</summary>
        public string Part { get; set; }

        /// <summary>Gets or sets the rows of the page, as the document writes them.</summary>
        public IReadOnlyList<JsonElement> Rows { get; set; }
    }

    /// <summary>What <c>export_to_file</c> answers.</summary>
    internal sealed class ExportedResult
    {
        /// <summary>Gets or sets the full path written.</summary>
        public string Path { get; set; }

        /// <summary>Gets or sets <c>json</c> or <c>graph</c>.</summary>
        public string What { get; set; }

        /// <summary>Gets or sets how many bytes were written.</summary>
        public long Bytes { get; set; }
    }
}
