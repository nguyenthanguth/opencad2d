using System;
using System.Collections.Generic;

namespace OpenCAD2D
{
    /// <summary>
    /// What <see cref="CadDocuments"/> needs of the window that holds the drawings: the drawings open, the one shown, and the
    /// ways to open, make, show, save and close one. The window implements it over the same members the remote channel
    /// drives (<see cref="ICadRemoteHost"/>), so that a plugin's call does what the user's action and a Remote call do; a
    /// test implements it over a few documents and no window.
    /// </summary>
    internal interface ICadDocumentHost
    {
        /// <summary>Gets the drawings open now, ordered by file name.</summary>
        IReadOnlyList<CadDocument> Documents { get; }

        /// <summary>Gets the drawing the user works in, or null when none is open.</summary>
        CadDocument ActiveDocument { get; }

        /// <summary>Opens a drawing from a file and shows it, or shows it where it is open already.</summary>
        /// <param name="path">The path of a DXF or DWG file.</param>
        /// <returns>The drawing.</returns>
        /// <exception cref="InvalidOperationException">Thrown when the file cannot be opened; the message says why.</exception>
        CadDocument Open(string path);

        /// <summary>Starts a new drawing from a template (null for the app's own) and shows it.</summary>
        /// <param name="template">The path of a template, or null.</param>
        /// <returns>The drawing.</returns>
        /// <exception cref="InvalidOperationException">Thrown when the drawing cannot be made; the message says why.</exception>
        CadDocument New(string template);

        /// <summary>Shows a drawing open in the app, as clicking its entry in the list does.</summary>
        /// <param name="document">The drawing.</param>
        void Activate(CadDocument document);

        /// <summary>Gets the command session of a drawing; null for a drawing the window does not hold.</summary>
        /// <param name="document">The drawing.</param>
        /// <returns>The session.</returns>
        CadCommandSession SessionOf(CadDocument document);

        /// <summary>Saves a drawing to its file.</summary>
        /// <param name="document">The drawing, which has a file.</param>
        /// <exception cref="InvalidOperationException">Thrown when it cannot be written; the message says why.</exception>
        void Save(CadDocument document);

        /// <summary>Closes a drawing without asking and without saving; the caller has checked it may be.</summary>
        /// <param name="document">The drawing, open.</param>
        void Close(CadDocument document);
    }
}
