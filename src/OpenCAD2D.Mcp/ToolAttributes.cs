using System;

namespace OpenCAD2D.Mcp
{
    /// <summary>
    /// Marks a tool that writes a file and no part of the drawing. A plan runs such a tool only among its last steps, after what
    /// the plan drew is kept, and not at all in a dry run or a plan that stopped.
    /// </summary>
    [AttributeUsage(AttributeTargets.Method)]
    internal sealed class WritesFileAttribute : Attribute { }

    /// <summary>
    /// Marks a tool that is not annotated read-only but that a read-only server runs: it writes no part of the drawing, or refuses
    /// the call that would on its own. A plan on a read-only server leaves such a tool to that refusal, as a call alone does.
    /// </summary>
    [AttributeUsage(AttributeTargets.Method)]
    internal sealed class ReadOnlyAllowedAttribute : Attribute
    {
        /// <summary>Gets or sets the name of a boolean argument that, when true, makes the call write (<c>fix</c> of <c>audit</c>); null where no call does.</summary>
        public string WritesWhen { get; set; }
    }
}
