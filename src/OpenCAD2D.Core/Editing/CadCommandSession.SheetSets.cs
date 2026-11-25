using System;
using OpenCAD2D.SheetSets;

namespace OpenCAD2D
{
    public sealed partial class CadCommandSession
    {
        private CadSheetSetOptions _sheetSetOptions = new CadSheetSetOptions();

        /// <summary>
        /// Gets or sets the Sheet Set Manager's variables SSLOCATE, SSMAUTOOPEN, SSMPOLLTIME and SSMSHEETSTATUS. A session starts
        /// with a set of its own; a host that has several drawings gives them one to share and keeps its values in the settings.
        /// </summary>
        /// <exception cref="ArgumentNullException">Thrown when the options are null.</exception>
        public CadSheetSetOptions SheetSetOptions
        {
            get => _sheetSetOptions;
            set => _sheetSetOptions = value ?? throw new ArgumentNullException(nameof(value));
        }

        /// <summary>
        /// Gets the file of the sheet set <see cref="LocateSheetSet"/> found for the drawing, as SSFOUND says it; empty where none was
        /// looked for or found. Not kept with the drawing.
        /// </summary>
        public string SheetSetFound { get; private set; } = string.Empty;

        /// <summary>
        /// Finds the sheet set of the drawing, as AutoCAD does when a drawing opens with SSLOCATE on: the sheet set mark the
        /// drawing carries (<see cref="CadSheetSetHint"/>) names the set's file, which is looked for where it says and, if it is
        /// not there, as far from the drawing as the mark says it was, so that a set moved with its sheets is found. The file is
        /// <see cref="SheetSetFound"/> and the window is asked (the request SSLOCATE, a <see cref="CadSheetSetRequest"/>) to open
        /// the set in the Sheet Set Manager, and to show the manager where SSMAUTOOPEN is on. The host calls it when it has opened
        /// a drawing.
        /// </summary>
        /// <returns>The file of the set found; null where SSLOCATE is off, or the drawing is no sheet of a set that can be read.</returns>
        public string LocateSheetSet()
        {
            SheetSetFound = string.Empty;

            if (!SheetSetOptions.Locate)
            {
                return null;
            }

            CadSheetSetFile file = CadSheetSetFields.OpenSet(null, null, new CadFieldContext(Document));

            if (file == null)
            {
                return null;
            }

            SheetSetFound = file.Path;
            Request("SSLOCATE", Document, new CadSheetSetRequest(file.Path, SheetSetOptions.AutoOpen));
            return file.Path;
        }
    }
}
