using System;
using System.Collections.Generic;
using System.IO;
using System.Windows;
using OpenCAD2D.SheetSets;

namespace OpenCAD2D.Wpf
{
    /// <summary>
    /// The doors to the sheet set dialogs of A12 4b, which the palette's menus open: each takes the window it is shown over, the
    /// sheet set's file, the part of the set it is for and, optionally, <see cref="SheetSetDialogOptions"/> (why the set is
    /// read-only, how a change is made, which drawings are open). The dialog saves the set itself, through the options' changer
    /// where there is one, so that the caller only has to show the result (its tree again). A locked set opens a dialog
    /// read-only with one Close, and the call then returns as if cancelled.
    /// </summary>
    public static class SheetSetDialogs
    {
        /// <summary>Shows Sheet Set Properties over a set.</summary>
        /// <param name="owner">The window it is shown over; null for none.</param>
        /// <param name="file">The sheet set's file.</param>
        /// <param name="options">What the caller gives the dialog; null for none.</param>
        /// <returns>True when OK saved the properties.</returns>
        /// <exception cref="ArgumentNullException">Thrown when the file is null.</exception>
        public static bool ShowSheetSetProperties(
            Window owner,
            CadSheetSetFile file,
            SheetSetDialogOptions options = null
        ) => SheetSetPropertiesWindow.Show(owner, new SheetSetPropertiesViewModel(file, options));

        /// <summary>Shows Subset Properties over a subset that is in the set: what Properties… and Rename Subset… of a subset open.</summary>
        /// <param name="owner">The window it is shown over; null for none.</param>
        /// <param name="file">The sheet set's file.</param>
        /// <param name="subset">The subset (not the sheet set itself).</param>
        /// <param name="options">What the caller gives the dialog; null for none.</param>
        /// <returns>True when OK saved the properties.</returns>
        /// <exception cref="ArgumentNullException">Thrown when the file or the subset is null.</exception>
        public static bool ShowSubsetProperties(
            Window owner,
            CadSheetSetFile file,
            CadSheetSubset subset,
            SheetSetDialogOptions options = null
        ) => SheetSetPropertiesWindow.Show(owner, SheetSetSubsetPropertiesViewModel.Existing(file, subset, options));

        /// <summary>Shows Subset Properties for a subset to be made inside a group: what New Subset… opens.</summary>
        /// <param name="owner">The window it is shown over; null for none.</param>
        /// <param name="file">The sheet set's file.</param>
        /// <param name="parent">The group it goes into (the sheet set itself or a subset).</param>
        /// <param name="options">What the caller gives the dialog; null for none.</param>
        /// <returns>The ID of the subset OK made; null where the dialog was cancelled.</returns>
        /// <exception cref="ArgumentNullException">Thrown when the file or the group is null.</exception>
        public static string ShowNewSubset(
            Window owner,
            CadSheetSetFile file,
            CadSheetSubset parent,
            SheetSetDialogOptions options = null
        )
        {
            SheetSetSubsetPropertiesViewModel model = SheetSetSubsetPropertiesViewModel.New(file, parent, options);
            return SheetSetPropertiesWindow.Show(owner, model) ? model.SubsetId : null;
        }

        /// <summary>Shows Sheet Properties over a sheet.</summary>
        /// <param name="owner">The window it is shown over; null for none.</param>
        /// <param name="file">The sheet set's file.</param>
        /// <param name="sheet">The sheet.</param>
        /// <param name="options">What the caller gives the dialog; null for none.</param>
        /// <returns>True when OK saved the properties.</returns>
        /// <exception cref="ArgumentNullException">Thrown when the file or the sheet is null.</exception>
        public static bool ShowSheetProperties(
            Window owner,
            CadSheetSetFile file,
            CadSheetSetSheet sheet,
            SheetSetDialogOptions options = null
        ) => SheetSetPropertiesWindow.Show(owner, new SheetSetSheetPropertiesViewModel(file, sheet, options));

        /// <summary>Shows Custom Properties over a set (Edit Custom Properties… of Sheet Set Properties).</summary>
        /// <param name="owner">The window it is shown over; null for none.</param>
        /// <param name="file">The sheet set's file.</param>
        /// <param name="options">What the caller gives the dialog; null for none.</param>
        /// <returns>True when OK saved the changes.</returns>
        /// <exception cref="ArgumentNullException">Thrown when the file is null.</exception>
        public static bool ShowCustomProperties(
            Window owner,
            CadSheetSetFile file,
            SheetSetDialogOptions options = null
        ) => SheetSetCustomPropertiesWindow.Show(owner, new SheetSetCustomPropertiesViewModel(file, options));

        /// <summary>
        /// Shows New Sheet for a new sheet in a group, asking Select Layout as Sheet Template first where the group's Prompt for
        /// template is Yes. The drawing and the sheet are made at OK; opening the drawing, where the result says so, is the caller's.
        /// </summary>
        /// <param name="owner">The window it is shown over; null for none.</param>
        /// <param name="file">The sheet set's file.</param>
        /// <param name="parent">The group the sheet goes into (the sheet set itself or a subset).</param>
        /// <param name="after">The sheet the new one goes after, in the same group; null for the end of the group.</param>
        /// <param name="options">What the caller gives the dialog; null for none.</param>
        /// <returns>What was made; null where the dialog was cancelled.</returns>
        /// <exception cref="ArgumentNullException">Thrown when the file or the group is null.</exception>
        public static SheetSetNewSheetResult ShowNewSheet(
            Window owner,
            CadSheetSetFile file,
            CadSheetSubset parent,
            CadSheetSetSheet after,
            SheetSetDialogOptions options = null
        )
        {
            var model = new SheetSetNewSheetViewModel(file, parent, after, options);
            if (model.PromptsForTemplate)
            {
                SheetSetTemplatePickViewModel pick = SheetSetTemplatePickViewModel.ForLayouts(file, null, options);
                if (!SheetSetTemplateWindow.Show(owner, pick))
                {
                    return null;
                }

                model.Template = pick.Result;
            }

            return SheetSetNewSheetWindow.Show(owner, model) ? model.Created : null;
        }

        /// <summary>Shows Rename &amp; Renumber Sheet over a sheet. Previous and Next apply the changes and show the neighbouring sheet.</summary>
        /// <param name="owner">The window it is shown over; null for none.</param>
        /// <param name="file">The sheet set's file.</param>
        /// <param name="sheet">The sheet.</param>
        /// <param name="options">What the caller gives the dialog; null for none.</param>
        /// <returns>True when the set was changed (by OK, Previous or Next), so that the caller shows it again.</returns>
        /// <exception cref="ArgumentNullException">Thrown when the file or the sheet is null.</exception>
        public static bool ShowRenameAndRenumber(
            Window owner,
            CadSheetSetFile file,
            CadSheetSetSheet sheet,
            SheetSetDialogOptions options = null
        )
        {
            var model = new SheetSetRenameViewModel(file, sheet, options);
            SheetSetRenameWindow.Show(owner, model);
            return model.Changed;
        }

        /// <summary>Shows Import Layouts as Sheets for a group.</summary>
        /// <param name="owner">The window it is shown over; null for none.</param>
        /// <param name="file">The sheet set's file.</param>
        /// <param name="into">The group the sheets go into (the sheet set itself or a subset).</param>
        /// <param name="options">What the caller gives the dialog; null for none.</param>
        /// <returns>The IDs of the sheets Import Checked made, in the order of the layouts; null where the dialog was cancelled.</returns>
        /// <exception cref="ArgumentNullException">Thrown when the file or the group is null.</exception>
        public static IReadOnlyList<string> ShowImportLayouts(
            Window owner,
            CadSheetSetFile file,
            CadSheetSubset into,
            SheetSetDialogOptions options = null
        )
        {
            var model = new SheetSetImportLayoutsViewModel(file, into, options);
            return SheetSetImportLayoutsWindow.Show(owner, model) ? model.ImportedSheetIds : null;
        }

        /// <summary>The ... button of a template row: Select Layout as Sheet Template over a dialog.</summary>
        internal static CadDrawingReference PickTemplate(
            Window owner,
            SheetSetDialogViewModel model,
            CadDrawingReference current
        )
        {
            SheetSetTemplatePickViewModel pick = SheetSetTemplatePickViewModel.ForLayouts(
                model.File,
                current,
                model.Options
            );
            return SheetSetTemplateWindow.Show(owner, pick) ? pick.Result : null;
        }

        /// <summary>The ... button of Label block for views: the block picker over a dialog.</summary>
        internal static CadDrawingReference PickBlock(
            Window owner,
            SheetSetDialogViewModel model,
            CadDrawingReference current
        )
        {
            SheetSetTemplatePickViewModel pick = SheetSetTemplatePickViewModel.ForBlocks(
                model.File,
                current,
                model.Options
            );
            return SheetSetTemplateWindow.Show(owner, pick) ? pick.Result : null;
        }

        /// <summary>The ... button of Expected layout: the layout picker over a dialog; the drawing it starts with is the sheet's.</summary>
        internal static SheetSetLayoutChoice PickLayout(
            Window owner,
            SheetSetDialogViewModel model,
            string currentDrawing
        )
        {
            CadDrawingReference current =
                currentDrawing == null
                    ? null
                    : new CadDrawingReference(
                        CadFileReference.FromPath(currentDrawing, model.File.Folder),
                        null,
                        null,
                        null
                    );
            SheetSetTemplatePickViewModel pick = SheetSetTemplatePickViewModel.ForExpectedLayout(
                model.File,
                current,
                model.Options
            );
            return SheetSetTemplateWindow.Show(owner, pick)
                ? new SheetSetLayoutChoice(Path.GetFullPath(pick.FileName.Trim()), pick.Result.Name)
                : null;
        }
    }
}
