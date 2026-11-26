using System.Collections.Generic;

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
}
