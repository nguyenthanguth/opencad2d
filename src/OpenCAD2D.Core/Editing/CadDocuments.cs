using System;
using System.Collections;
using System.Collections.Generic;
using GeometryHelper;

namespace OpenCAD2D
{
    /// <summary>
    /// The drawings open in the app, as AutoCAD's <c>DocumentManager</c> holds the documents of a session: listed, one made
    /// active, one opened, a new one made from the template, one closed with or without its changes saved. A plugin reaches
    /// it as <see cref="Plugins.CadPluginContext.Documents"/> or <see cref="CadCommandContext.Documents"/>. Every call does
    /// what the user's action or a Remote call does, through the window's own code: <see cref="Open"/> is File &gt; Open, a
    /// drawing opened already is shown, not read twice; <see cref="Add"/> is QNEW; <see cref="Active"/> set is a click on
    /// the drawing's row of the DRAWINGS list; the close calls are the x of a row once the question is answered.
    /// </summary>
    /// <remarks>
    /// <para>
    /// The drawings are listed by file name, as Remote lists them, a drawing with no file by its name (Drawing1.dwg). Call it
    /// on the window's thread, as every callback of a plugin is run. A callback's <see cref="Plugins.CadPluginContext.Document"/>
    /// is the drawing shown when it was made, so it is read again after a call that shows another drawing.
    /// </para>
    /// <para>
    /// Where no window runs the plugin (a test, a plugin's own test, the Remote test server) <see cref="HasWindow"/> is false:
    /// the list is the one drawing of the session and it is the active one, and the calls that open, make, show or close a
    /// drawing throw <see cref="InvalidOperationException"/> and say so, as the window holds the drawings and a session alone
    /// does not.
    /// </para>
    /// <para>
    /// Showing another drawing ends the command running in the drawing shown, as it does when the user clicks another row.
    /// A call that would do so while a command runs there is refused instead, so that a plugin's command is not ended under
    /// its own body: <see cref="Open"/>, <see cref="Add"/> and setting <see cref="Active"/> belong to a button's, a menu's or
    /// a DRAWINGS row's callback, where no command runs. A command may read the list and close another drawing.
    /// </para>
    /// </remarks>
    public sealed class CadDocuments : IReadOnlyList<CadDocument>
    {
        private readonly ICadDocumentHost _host;
        private readonly CadDocument _own;

        internal CadDocuments(ICadDocumentHost host, CadDocument own)
        {
            _host = host;
            _own = own;
        }

        /// <summary>
        /// Gets whether the app's window holds the drawings: false in a test and in the Remote test server, where the list is
        /// the session's own drawing and the calls that change what is open say there is no window.
        /// </summary>
        public bool HasWindow => _host != null;

        /// <summary>Gets the number of drawings open: 2 for a window with <c>A-101.dwg</c> and <c>B-202.dwg</c> open.</summary>
        public int Count => Snapshot().Count;

        /// <summary>Gets a drawing open by its place in the list, which is ordered by file name.</summary>
        /// <param name="index">The place, from 0.</param>
        /// <exception cref="ArgumentOutOfRangeException">Thrown when there is no drawing at that place.</exception>
        public CadDocument this[int index] => Snapshot()[index];

        /// <summary>
        /// Gets or sets the drawing the user works in, null where none is open: AutoCAD's <c>MdiActiveDocument</c>. Setting it
        /// shows another drawing open, as clicking its row of the DRAWINGS list does; the drawing already shown is left as it
        /// is.
        /// </summary>
        /// <exception cref="ArgumentNullException">Thrown when set to null.</exception>
        /// <exception cref="ArgumentException">Thrown when set to a drawing that is not open.</exception>
        /// <exception cref="InvalidOperationException">Thrown when set with no window, or while a command runs in the drawing shown.</exception>
        public CadDocument Active
        {
            get => _host == null ? _own : _host.ActiveDocument;
            set
            {
                if (value == null)
                {
                    throw new ArgumentNullException(nameof(value));
                }

                RequireWindow("show a drawing");
                RequireOpen(value);

                if (ReferenceEquals(value, _host.ActiveDocument))
                {
                    return;
                }

                RequireNotBusy("show another drawing", "show the drawing again");
                _host.Activate(value);
            }
        }

        /// <summary>
        /// Opens a DWG or DXF file and shows it, or shows it where it is open already, as File &gt; Open does with the file
        /// read at once. Each extension is told of the drawing and the line "Opened plan.dxf: 12 entities, 3 layers." goes to
        /// the command line.
        /// </summary>
        /// <param name="path">The path of the file.</param>
        /// <returns>The drawing.</returns>
        /// <exception cref="ArgumentNullException">Thrown when the path is null.</exception>
        /// <exception cref="ArgumentException">Thrown when the path is empty.</exception>
        /// <exception cref="InvalidOperationException">Thrown when the file cannot be opened (the message is what the command line said), with no window, or while a command runs in the drawing shown.</exception>
        public CadDocument Open(string path)
        {
            if (path == null)
            {
                throw new ArgumentNullException(nameof(path));
            }

            Guard.Argument(path.Trim().Length > 0, nameof(path), "Open needs the path of a file.");
            RequireWindow("open a drawing");
            RequireNotBusy("open a drawing", "open the drawing again");
            return _host.Open(path) ?? throw new InvalidOperationException("OpenCAD2D could not open " + path + ".");
        }

        /// <summary>
        /// Starts a new drawing and shows it, as QNEW does: from the template, named Drawing1.dwg and then on, with no file of
        /// its own until it is saved.
        /// </summary>
        /// <param name="template">The full path of a template (.dwt, .dwg or .dxf), or null for the app's own: the one Global settings name for QNEW, else the metric template.</param>
        /// <returns>The drawing.</returns>
        /// <exception cref="InvalidOperationException">Thrown when the template cannot be read (the message says why), with no window, or while a command runs in the drawing shown.</exception>
        public CadDocument Add(string template = null)
        {
            RequireWindow("add a drawing");
            RequireNotBusy("add a drawing", "add the drawing again");
            return _host.New(template)
                ?? throw new InvalidOperationException("OpenCAD2D could not start a new drawing.");
        }

        /// <summary>
        /// Closes a drawing that has no changes unsaved, as the x of its row does: it leaves the list, and the next drawing is
        /// shown, or the start screen where it was the last. Nothing is asked and nothing is saved.
        /// </summary>
        /// <param name="document">The drawing, open.</param>
        /// <exception cref="ArgumentNullException">Thrown when the drawing is null.</exception>
        /// <exception cref="ArgumentException">Thrown when the drawing is not open.</exception>
        /// <exception cref="InvalidOperationException">Thrown, and nothing changes, where the drawing has changes not saved (save it, or use <see cref="CloseAndSave"/> or <see cref="CloseAndDiscard"/>), where a command runs in it, or with no window.</exception>
        public void Close(CadDocument document) => Close(document, false);

        /// <summary>
        /// Closes a drawing and throws away its changes not saved, as the x of its row does once the question is answered
        /// Don't save.
        /// </summary>
        /// <param name="document">The drawing, open.</param>
        /// <exception cref="ArgumentNullException">Thrown when the drawing is null.</exception>
        /// <exception cref="ArgumentException">Thrown when the drawing is not open.</exception>
        /// <exception cref="InvalidOperationException">Thrown, and nothing changes, where a command runs in it, or with no window.</exception>
        public void CloseAndDiscard(CadDocument document) => Close(document, true);

        /// <summary>
        /// Saves a drawing to its file, as Ctrl+S does, then closes it. A drawing that cannot be closed is not saved.
        /// </summary>
        /// <param name="document">The drawing, open, with a file.</param>
        /// <exception cref="ArgumentNullException">Thrown when the drawing is null.</exception>
        /// <exception cref="ArgumentException">Thrown when the drawing is not open.</exception>
        /// <exception cref="InvalidOperationException">Thrown, and nothing changes, where the drawing has no file yet, where a command runs in it, or with no window; also when it cannot be written (the message says why), and it stays open.</exception>
        public void CloseAndSave(CadDocument document)
        {
            RequireOpenForClose(document);
            string refusal = CloseRefusal(document, _host.SessionOf(document), true);

            if (refusal != null)
            {
                throw new InvalidOperationException(refusal);
            }

            if (!document.HasFile)
            {
                throw new InvalidOperationException(
                    "The drawing " + document.Title + " has no file yet: save it to a file first."
                );
            }

            _host.Save(document);
            _host.Close(document);
        }

        /// <summary>Gets an enumerator over the drawings open, in the order of the list; the list does not change under it.</summary>
        /// <returns>The enumerator.</returns>
        public IEnumerator<CadDocument> GetEnumerator() => Snapshot().GetEnumerator();

        IEnumerator IEnumerable.GetEnumerator() => GetEnumerator();

        /// <summary>
        /// Says why a drawing may not be closed, in the words Remote's close uses, or null where it may: it has changes not
        /// saved and they are not to be thrown away, a command runs in it, or a routine waits for the user, whose Esc the
        /// close would have to press for them.
        /// </summary>
        internal static string CloseRefusal(CadDocument document, CadCommandSession session, bool discard)
        {
            if (document.IsModified && !discard)
            {
                return document.Title + " has changes not saved: save it, or close it with discard.";
            }

            return BusyRefusal(document, session, "close the drawing again");
        }

        /// <summary>Says that a command runs in the drawing, or null where none does.</summary>
        private static string BusyRefusal(CadDocument document, CadCommandSession session, string again)
        {
            if (document.IsInGroup || (session != null && (session.IsRunning || session.IsLispWaiting)))
            {
                return "The drawing "
                    + document.Title
                    + " is busy with the command "
                    + (session?.Current?.Name ?? "running in it")
                    + ": end it, then "
                    + again
                    + ".";
            }

            return null;
        }

        private IReadOnlyList<CadDocument> Snapshot()
        {
            if (_host != null)
            {
                return _host.Documents;
            }

            return _own == null ? Array.Empty<CadDocument>() : new[] { _own };
        }

        private void Close(CadDocument document, bool discard)
        {
            RequireOpenForClose(document);
            string refusal = CloseRefusal(document, _host.SessionOf(document), discard);

            if (refusal != null)
            {
                throw new InvalidOperationException(refusal);
            }

            _host.Close(document);
        }

        private void RequireOpenForClose(CadDocument document)
        {
            if (document == null)
            {
                throw new ArgumentNullException(nameof(document));
            }

            RequireWindow("close a drawing");
            RequireOpen(document);
        }

        private void RequireWindow(string what)
        {
            if (_host == null)
            {
                throw new InvalidOperationException(
                    "Cannot " + what + ": this session has no OpenCAD2D window, which holds the open drawings."
                );
            }
        }

        private void RequireOpen(CadDocument document)
        {
            Guard.Argument(
                System.Linq.Enumerable.Contains(_host.Documents, document),
                nameof(document),
                "The drawing " + document.Title + " is not open."
            );
        }

        /// <summary>Refuses what would show another drawing while a command runs in the drawing shown, which showing it would end.</summary>
        private void RequireNotBusy(string what, string again)
        {
            CadDocument shown = _host.ActiveDocument;

            if (shown == null)
            {
                return;
            }

            string refusal = BusyRefusal(shown, _host.SessionOf(shown), again);

            if (refusal != null)
            {
                throw new InvalidOperationException("Cannot " + what + ". " + refusal);
            }
        }
    }
}
