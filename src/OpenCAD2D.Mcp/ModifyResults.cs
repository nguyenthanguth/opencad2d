using System.Collections.Generic;

namespace OpenCAD2D.Mcp
{
    /// <summary>What a tool that changes the drawing through a command did: the handles it made, the handles it took away, the ones it changed in place.</summary>
    internal sealed class ModifiedResult
    {
        /// <summary>Gets or sets the handles of the objects made, in the order the layout draws them.</summary>
        public IReadOnlyList<string> Created { get; set; }

        /// <summary>Gets or sets the handles of the objects that are gone.</summary>
        public IReadOnlyList<string> Erased { get; set; }

        /// <summary>Gets or sets the handles of the objects given that are still there but now take another box.</summary>
        public IReadOnlyList<string> Changed { get; set; }

        /// <summary>Gets or sets the last lines the command line wrote.</summary>
        public string Output { get; set; }
    }

    /// <summary>The layout a tool made or worked on, and the objects it made in it.</summary>
    internal sealed class LayoutResult
    {
        public string Name { get; set; }

        /// <summary>Gets or sets the sheet, as <c>ISO A3 (420 by 297 mm)</c>; null where the tool left it.</summary>
        public string Paper { get; set; }

        /// <summary>Gets or sets the handles made: the viewport of an <c>add_viewport</c>.</summary>
        public IReadOnlyList<string> Handles { get; set; }
    }

    /// <summary>The block a tool defined, and the handle of the block reference that replaced what it took.</summary>
    internal sealed class BlockResult
    {
        public string Name { get; set; }

        /// <summary>Gets or sets the number of objects in the block.</summary>
        public int Entities { get; set; }

        /// <summary>Gets or sets the handle of the block reference placed at the base point; null for keep and delete.</summary>
        public string Insert { get; set; }

        /// <summary>Gets or sets the handles erased.</summary>
        public IReadOnlyList<string> Erased { get; set; }
    }

    /// <summary>The name of a record a tool made.</summary>
    internal sealed class RecordResult
    {
        public string Name { get; set; }
    }

    /// <summary>What redo did.</summary>
    internal sealed class RedoResult
    {
        public bool Redone { get; set; }

        public string Message { get; set; }
    }
}
