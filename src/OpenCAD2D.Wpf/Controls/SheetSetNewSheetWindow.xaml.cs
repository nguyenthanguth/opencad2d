using System;
using System.Windows;

namespace OpenCAD2D.Wpf
{
    /// <summary>
    /// New Sheet, as the board A12NewSheet draws it: Number, Sheet title, File name, Folder path and Sheet template (the last two
    /// read-only), the red line where the title or the file is refused, and Open in drawing editor. Its logic is
    /// <see cref="SheetSetNewSheetViewModel"/>; <see cref="SheetSetDialogs.ShowNewSheet"/> asks Select Layout as Sheet Template
    /// first where the subset's Prompt for template is Yes.
    /// </summary>
    public partial class SheetSetNewSheetWindow : SheetSetDialogWindow
    {
        /// <summary>Makes the dialog over its view model.</summary>
        /// <param name="model">The view model.</param>
        /// <exception cref="ArgumentNullException">Thrown when the model is null.</exception>
        public SheetSetNewSheetWindow(SheetSetNewSheetViewModel model)
            : base(model)
        {
            InitializeComponent();
            Wire(Frame);
        }

        /// <summary>Gets the view model.</summary>
        public new SheetSetNewSheetViewModel Model => (SheetSetNewSheetViewModel)base.Model;

        /// <summary>Shows the dialog over a window, which waits until it is closed.</summary>
        /// <param name="owner">The window it is shown over; null for none.</param>
        /// <param name="model">The view model.</param>
        /// <returns>True when OK made the sheet.</returns>
        public static bool Show(Window owner, SheetSetNewSheetViewModel model) =>
            new SheetSetNewSheetWindow(model).Present(owner);
    }
}
