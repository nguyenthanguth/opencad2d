using System;
using System.Collections.Generic;

namespace OpenCAD2D
{
    /// <summary>
    /// A printer of Windows as -PLOT sees it: the name the command takes for the device and the sheets the printer lists, asked
    /// when the printer is chosen. The window fills the list of the "PLOTPRINTERS" request with them; a host with no printers
    /// leaves it empty, and -PLOT then names the presenter's own plotters alone.
    /// </summary>
    internal sealed class CadPlotPrinter
    {
        private readonly Func<IReadOnlyList<CadPaper>> _papers;

        internal CadPlotPrinter(string name, Func<IReadOnlyList<CadPaper>> papers)
        {
            Name = name ?? throw new ArgumentNullException(nameof(name));
            _papers = papers ?? throw new ArgumentNullException(nameof(papers));
        }

        /// <summary>Gets the name Windows gives the printer: <c>Microsoft Print to PDF</c>.</summary>
        internal string Name { get; }

        /// <summary>The sheets the printer lists, as the Plot dialog's Paper size list takes them; the PDF plotter's where it lists none.</summary>
        internal IReadOnlyList<CadPaper> Papers()
        {
            IReadOnlyList<CadPaper> papers = _papers();
            return papers == null || papers.Count == 0 ? CadPaper.Standard : papers;
        }
    }
}
