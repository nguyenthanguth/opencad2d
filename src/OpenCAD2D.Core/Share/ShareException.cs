using System;

namespace OpenCAD2D.Share
{
    /// <summary>
    /// Why a tool could not be found on Share or installed from it, in a sentence the window shows as it is: a link that is
    /// no tool's, a tool Share does not have, a server that cannot be reached, a file whose SHA-256 is not the one Share
    /// recorded, a zip that holds a path out of the tool's folder.
    /// </summary>
    internal sealed class ShareException : Exception
    {
        /// <summary>Makes the exception.</summary>
        /// <param name="message">The sentence.</param>
        public ShareException(string message)
            : base(message) { }

        /// <summary>Makes the exception with the one that caused it.</summary>
        /// <param name="message">The sentence.</param>
        /// <param name="inner">The cause.</param>
        public ShareException(string message, Exception inner)
            : base(message, inner) { }
    }
}
