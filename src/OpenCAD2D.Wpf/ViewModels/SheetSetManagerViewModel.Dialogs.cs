using System;
using System.Collections.Generic;
using System.Linq;
using OpenCAD2D.SheetSets;

namespace OpenCAD2D.Wpf
{
    public sealed partial class SheetSetManagerViewModel
    {
        /// <summary>Gets or sets whether the window has joined <see cref="DialogRequested"/> to the sheet set dialogs of A12 4b.</summary>
        internal bool DialogsAttached { get; set; }

        /// <summary>
        /// What the sheet set dialogs are given by the palette: why the set is read-only where it is locked, whether a drawing is
        /// open in the app, and the palette's own change as the way they change the set, so that a drawing a change rewrites is
        /// saved, closed and opened again (B18) and the tree is made again. What the core refuses is thrown to the dialog, which
        /// says it in its red line (the palette says it on the command line too).
        /// </summary>
        /// <param name="reveal">A row to show open afterwards, so that what was added under it is seen; null for none.</param>
        /// <returns>The options.</returns>
        internal SheetSetDialogOptions DialogOptions(SheetSetNodeViewModel reveal = null) =>
            new SheetSetDialogOptions
            {
                ReadOnlyReason = IsLocked ? _lockReason : null,
                IsOpenInSession = path => _main.OpenDocumentOf(path) != null,
                Changer = (edit, drawings, follow) =>
                {
                    Exception refusal = null;
                    bool done = Change(
                        set =>
                        {
                            try
                            {
                                edit(set);
                            }
                            catch (Exception ex)
                            {
                                refusal = ex;
                                throw;
                            }
                        },
                        drawings,
                        follow,
                        reveal
                    );

                    if (!done)
                    {
                        throw refusal ?? new InvalidOperationException("The sheet set could not be changed.");
                    }
                },
            };

        /// <summary>Opens the drawing of a sheet, found by its ID in the tree as it is now, on its layout (Open in drawing editor of New Sheet).</summary>
        /// <param name="sheetId">The sheet's ID.</param>
        /// <returns>Whether a drawing was asked to open.</returns>
        internal bool OpenSheetById(string sheetId)
        {
            SheetSetNodeViewModel node = AllSheets().FirstOrDefault(s => s.Id == sheetId);
            return node != null && OpenSheets(new[] { node }, false);
        }

        /// <summary>The row of a part of the set in the tree as it is now, found by its ID; null where it is not there.</summary>
        /// <param name="id">The part's ID.</param>
        /// <returns>The row.</returns>
        internal SheetSetNodeViewModel NodeOf(string id) =>
            _root == null ? null : Walk(_root).FirstOrDefault(n => n.Id == id);
    }
}
