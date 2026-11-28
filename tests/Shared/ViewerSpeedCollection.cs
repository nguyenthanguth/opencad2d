using Xunit;

namespace OpenCAD2D
{
    /// <summary>
    /// The tests that assert elapsed time, run with no other test beside them: run side by side, each one's collections and
    /// threads stretch the others' timers.
    /// </summary>
    [CollectionDefinition(Name, DisableParallelization = true)]
    public class ViewerSpeedCollection
    {
        /// <summary>The name of the collection.</summary>
        public const string Name = "Viewer speed";
    }
}
