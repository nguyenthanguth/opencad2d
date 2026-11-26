using System;
using System.Collections.Generic;

namespace OpenCAD2D
{
    /// <summary>The inquiry commands (LIST) run for a program that wants their words, with no window and no command line of the user's.</summary>
    public static class CadInquiry
    {
        /// <summary>
        /// Runs LIST on some entities in a command session of its own and gives the lines it writes, as AutoCAD's LIST writes them:
        /// the kind and layer, the space, the handle and the shape of each entity (the length and angle of a line, the centre and
        /// radius of a circle, the corners of a polyline). The drawing's own command line, its selection and its undo list are not
        /// touched: a line from (0, 0) to (300, 400) lists "Length = 500.0000".
        /// </summary>
        /// <param name="document">The drawing.</param>
        /// <param name="handles">The handles of the entities, in the order to list them; one the drawing has not is refused.</param>
        /// <returns>The lines, none for no handles.</returns>
        /// <exception cref="ArgumentNullException">Thrown when the document or the handles are null.</exception>
        /// <exception cref="ArgumentException">Thrown when a handle is not an entity of the drawing.</exception>
        public static IReadOnlyList<string> List(CadDocument document, IReadOnlyList<string> handles)
        {
            if (document == null)
            {
                throw new ArgumentNullException(nameof(document));
            }

            if (handles == null)
            {
                throw new ArgumentNullException(nameof(handles));
            }

            var lines = new List<string>();

            if (handles.Count == 0)
            {
                return lines;
            }

            foreach (string handle in handles)
            {
                if (document.FindEntity(handle) == null)
                {
                    throw new ArgumentException("The drawing " + document.Title + " has no object " + handle + ".");
                }
            }

            var session = new CadCommandSession(document);
            session.Start("LIST");
            session.FeedSelection(handles);
            int mark = session.History.Count;
            session.Enter();

            for (int i = mark; i < session.History.Count; i++)
            {
                lines.Add(session.History[i]);
            }

            if (session.IsRunning)
            {
                session.Escape();
            }

            return lines;
        }
    }
}
