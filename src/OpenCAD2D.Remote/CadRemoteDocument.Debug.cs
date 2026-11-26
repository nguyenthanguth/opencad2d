using System;
using System.Collections.Generic;

namespace OpenCAD2D.Remote
{
    /// <summary>What the command line of a drawing has written, for a program that debugs the app.</summary>
    public sealed partial class CadRemoteDocument
    {
        /// <summary>Gets the app the drawing is open in, for a request that is the app's and not the drawing's.</summary>
        internal CadApplication Application => _app;

        /// <summary>
        /// Gets the last lines of the drawing's command line, the oldest first: each command, each prompt with its answer, each
        /// message, as the user sees them in the history. Nothing is typed or changed.
        /// </summary>
        /// <param name="count">The most lines to give, from 1 to 1000.</param>
        /// <returns>The lines, how many the command line holds in all, and whether a command runs.</returns>
        /// <exception cref="ArgumentOutOfRangeException">Thrown when the count is not from 1 to 1000.</exception>
        /// <exception cref="CadRemoteException">Thrown when the drawing is closed.</exception>
        public CadRemoteHistory GetHistory(int count = 20)
        {
            if (count < 1 || count > 1000)
            {
                throw new ArgumentOutOfRangeException(nameof(count), "The count is from 1 to 1000.");
            }

            JsonObject answer = Ask(RemoteDebugOps.History, request => request.Set("count", (long)count));
            var lines = new List<string>();

            foreach (object item in answer.GetArray("lines"))
            {
                lines.Add(item as string ?? string.Empty);
            }

            return new CadRemoteHistory(
                lines,
                (int)answer.GetLong("total"),
                answer.GetBool("running"),
                answer.GetString("prompt") ?? string.Empty
            );
        }
    }
}
