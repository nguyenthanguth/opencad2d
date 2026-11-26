namespace OpenCAD2D.Remote
{
    /// <summary>The requests that tell what the app is doing and what went wrong in it, for a program that debugs it. They read; none changes a drawing.</summary>
    internal static class RemoteDebugOps
    {
        /// <summary>The picture of the canvas of the drawing shown (<c>maxSide</c>, the most pixels along a side, 0 for as it is): <c>png</c> as base64, <c>width</c>, <c>height</c>, <c>min</c> and <c>max</c> the part of the drawing it shows, <c>document</c>.</summary>
        public const string Screenshot = "screenshot";

        /// <summary>The last lines of the command line of a drawing (<c>count</c>): <c>lines</c>, <c>total</c>, <c>running</c> and <c>prompt</c>.</summary>
        public const string History = "history";

        /// <summary>The app's recent faults (<c>count</c>): <c>crashLog</c> (its path, or null), <c>entries</c> from the crash log and <c>requests</c> the app could not answer.</summary>
        public const string Errors = "errors";
    }
}
