using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using GeometryHelper.Dxf;
using OpenCAD2D.SheetSets;

namespace OpenCAD2D.Wpf
{
    /// <summary>
    /// The sheet list table of a sheet set (A12 4e): Insert Sheet List Table asks for its dialog, then for the insertion point on
    /// the command line, and draws the table in the layout current; Update Sheet List Table and Edit Sheet List Table Settings
    /// are the table's right-click items.
    /// </summary>
    public sealed partial class MainViewModel
    {
        /// <summary>Gets or sets what shows the Insert Sheet List Table dialog (and Edit Sheet List Table Settings, the same dialog on a table's settings) and says whether OK was answered; with none, the command line says there is no dialog to show.</summary>
        public Func<SheetListTableViewModel, bool> ShowSheetListTable { get; set; }

        /// <summary>
        /// Insert Sheet List Table: the dialog over the set and the drawing shown, then, once OK is answered, "Specify insertion
        /// point:" on the command line and the table drawn there in the layout current (one undo step). Refused, and said on the
        /// command line, where the space current is model space.
        /// </summary>
        /// <param name="file">The sheet set to list.</param>
        /// <returns>Whether the table's command was started.</returns>
        internal bool InsertSheetListTable(CadSheetSetFile file)
        {
            CadDocument document = Document;

            if (file == null || document == null)
            {
                return false;
            }

            if (document.CurrentSpace == null || document.CurrentSpace.IsModel)
            {
                Say("A sheet list table is put in a layout, not in model space.");
                return false;
            }

            if (ShowSheetListTable == null)
            {
                Say("The Insert Sheet List Table dialog is shown by the window; there is none to show it here.");
                return false;
            }

            var dialog = new SheetListTableViewModel(file, document);

            if (!ShowSheetListTable(dialog))
            {
                return false;
            }

            CadSheetListTableSettings settings = dialog.Settings;
            string style = dialog.TableStyle;
            return StartSheetSetCommand(new CadCommand("SHEETLISTTABLE", c => InsertBody(c, file, settings, style)));
        }

        private static IEnumerable<CadPrompt> InsertBody(
            CadCommandContext c,
            CadSheetSetFile file,
            CadSheetListTableSettings settings,
            string style
        )
        {
            if (c.Space.IsModel)
            {
                c.Write("** Command not allowed in Model Tab **");
                yield break;
            }

            yield return CadPrompt.ForPoint("Specify insertion point");

            if (!c.Input.IsPoint)
            {
                yield break;
            }

            c.OpenUndoStep();

            try
            {
                CadSheetListTable.Insert(c.Document, file, settings, c.Input.Point, style);
                c.Write("Sheet list table of " + file.SheetSet.Name + " inserted.");
            }
            catch (Exception e) when (e is InvalidOperationException || e is ArgumentException)
            {
                c.Write(e.Message);
            }
            finally
            {
                c.CloseUndoStep();
            }
        }

        /// <summary>
        /// Update Sheet List Table: the table made again from the set as it is now, by the settings it keeps, in a table of the same
        /// place, style and column widths (one undo step). A cell typed in by hand is lost, and the command line says so (B20).
        /// </summary>
        /// <param name="handle">The handle of the table.</param>
        /// <returns>Whether the table was made again; why not is said on the command line.</returns>
        internal bool UpdateSheetListTable(string handle)
        {
            CadDocument document = Document;

            if (document == null || handle == null || CadSheetListTable.ReadSettings(document, handle) == null)
            {
                return false;
            }

            return RedrawSheetListTable(document, handle, null, null);
        }

        /// <summary>
        /// Edit Sheet List Table Settings: the Insert Sheet List Table dialog on the settings the table keeps; OK lists the set by
        /// the new ones, in a table of the same place and style (one undo step).
        /// </summary>
        /// <param name="handle">The handle of the table.</param>
        /// <returns>Whether the table was made again; why not is said on the command line.</returns>
        internal bool EditSheetListTable(string handle)
        {
            CadDocument document = Document;
            CadSheetListTableSettings settings =
                document == null || handle == null ? null : CadSheetListTable.ReadSettings(document, handle);

            if (settings == null)
            {
                return false;
            }

            if (ShowSheetListTable == null)
            {
                Say("The Edit Sheet List Table Settings dialog is shown by the window; there is none to show it here.");
                return false;
            }

            CadSheetSetFile file = SetOfSheetListTable(document, handle);

            if (file == null)
            {
                return false;
            }

            var dialog = new SheetListTableViewModel(
                file,
                document,
                settings,
                (document.FindEntity(handle) as DxfTable)?.TableStyleName
            );

            return ShowSheetListTable(dialog) && RedrawSheetListTable(document, handle, file, dialog.Settings);
        }

        private bool RedrawSheetListTable(
            CadDocument document,
            string handle,
            CadSheetSetFile file,
            CadSheetListTableSettings settings
        )
        {
            try
            {
                CadSheetSetFile set = file ?? OpenSetOf(CadSheetListTable.ReadSetFile(document, handle));
                CadSheetListTable.Update(document, handle, set, settings);
                Say(
                    "Sheet list table updated from "
                        + (set?.SheetSet.Name ?? "the sheet set")
                        + ": cells edited by hand were replaced."
                );
                return true;
            }
            catch (Exception e) when (IsSheetListProblem(e))
            {
                Say(e.Message);
                return false;
            }
        }

        /// <summary>The set a table was made from, as a file: the one the palette has current where it is that one, else read from disk; null, said on the command line, where it is not there.</summary>
        private CadSheetSetFile SetOfSheetListTable(CadDocument document, string handle)
        {
            string path = CadSheetListTable.ReadSetFile(document, handle);

            try
            {
                return OpenSetOf(path);
            }
            catch (Exception e) when (IsSheetListProblem(e))
            {
                Say(e.Message);
                return null;
            }
        }

        private CadSheetSetFile OpenSetOf(string path)
        {
            CadSheetSetFile current = _sheetSetManager?.Current;

            if (current != null && string.Equals(current.Path, path, StringComparison.OrdinalIgnoreCase))
            {
                current.ReloadIfChanged();
                return current;
            }

            if (!File.Exists(path))
            {
                throw new FileNotFoundException(
                    "The sheet set " + path + " of the sheet list table is not there.",
                    path
                );
            }

            return CadSheetSetFile.Open(path);
        }

        private static bool IsSheetListProblem(Exception e) =>
            e is IOException
            || e is InvalidOperationException
            || e is ArgumentException
            || e is FormatException
            || e is NotSupportedException
            || e is UnauthorizedAccessException;
    }
}
