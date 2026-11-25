using System;
using System.Windows;
using OpenCAD2D.SheetSets;

namespace OpenCAD2D.Wpf
{
    /// <summary>
    /// Joins the Sheet Set Manager palette to the sheet set dialogs of A12 4b: the palette's menu items ask for a dialog
    /// (<see cref="SheetSetManagerViewModel.DialogRequested"/>), and this shows it over the window, on the sheet set's file and
    /// the row the menu was opened on, with the palette's own change as the way the dialog changes the set. The dialogs of a
    /// locked set open read-only.
    /// </summary>
    internal static class SheetSetPaletteDialogs
    {
        /// <summary>Joins a palette to the dialogs, once: calling it again for the same palette does nothing.</summary>
        /// <param name="palette">The palette's view model.</param>
        /// <param name="owner">The window the dialogs are shown over, asked each time one is shown.</param>
        internal static void Attach(SheetSetManagerViewModel palette, Func<Window> owner)
        {
            if (palette == null)
            {
                throw new ArgumentNullException(nameof(palette));
            }

            if (palette.DialogsAttached)
            {
                return;
            }

            palette.DialogsAttached = true;
            palette.DialogRequested += (sender, e) => e.Handled = Show(palette, e, owner());
        }

        /// <summary>Shows the dialog a menu item asked for.</summary>
        /// <param name="palette">The palette's view model.</param>
        /// <param name="e">What was asked for, and the row it was asked on.</param>
        /// <param name="owner">The window the dialog is shown over; null for none.</param>
        /// <returns>True when a dialog was shown (the request is handled); false where no sheet set is open.</returns>
        internal static bool Show(SheetSetManagerViewModel palette, SheetSetDialogEventArgs e, Window owner)
        {
            CadSheetSetFile file = palette.Current;
            if (file == null || e.Node == null)
            {
                return false;
            }

            file.ReloadIfChanged();
            CadSheetComponent part = file.SheetSet.Find(e.Node.Id);
            if (part == null)
            {
                return false;
            }

            // A sheet's group is the subset it is in; a subset or the set is its own.
            var group = (part as CadSheetSubset) ?? part.Parent;
            SheetSetNodeViewModel reveal = palette.NodeOf(group.Id);
            switch (e.Dialog)
            {
                case SheetSetDialog.NewSheet:
                    SheetSetNewSheetResult made = SheetSetDialogs.ShowNewSheet(
                        owner,
                        file,
                        group,
                        part as CadSheetSetSheet,
                        palette.DialogOptions(reveal)
                    );
                    if (made != null && made.OpenInEditor)
                    {
                        palette.OpenSheetById(made.SheetId);
                    }

                    break;
                case SheetSetDialog.NewSubset:
                    SheetSetDialogs.ShowNewSubset(owner, file, group, palette.DialogOptions(reveal));
                    break;
                case SheetSetDialog.ImportLayouts:
                    SheetSetDialogs.ShowImportLayouts(owner, file, group, palette.DialogOptions(reveal));
                    break;
                case SheetSetDialog.RenameAndRenumber:
                    if (part is CadSheetSetSheet renamed)
                    {
                        SheetSetDialogs.ShowRenameAndRenumber(owner, file, renamed, palette.DialogOptions());
                    }

                    break;
                case SheetSetDialog.RenameSubset:
                    if (part is CadSheetSubset && !(part is CadSheetSet))
                    {
                        SheetSetDialogs.ShowSubsetProperties(
                            owner,
                            file,
                            (CadSheetSubset)part,
                            palette.DialogOptions()
                        );
                    }

                    break;
                default:
                    if (part is CadSheetSet)
                    {
                        SheetSetDialogs.ShowSheetSetProperties(owner, file, palette.DialogOptions());
                    }
                    else if (part is CadSheetSubset subset)
                    {
                        SheetSetDialogs.ShowSubsetProperties(owner, file, subset, palette.DialogOptions());
                    }
                    else if (part is CadSheetSetSheet sheet)
                    {
                        SheetSetDialogs.ShowSheetProperties(owner, file, sheet, palette.DialogOptions());
                    }

                    break;
            }

            return true;
        }
    }
}
