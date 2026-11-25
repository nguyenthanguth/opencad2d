using System;
using System.Windows;

namespace OpenCAD2D.Wpf
{
    /// <summary>
    /// Import Layouts as Sheets, as the board A12ImportLayouts draws it: Browse for Drawings…, the list of the layouts of the
    /// drawing with a check box each, the yellow reason of a layout the set refuses, Prefix sheet titles with file name, and Import
    /// Checked (OK), Cancel and Help. Its logic is <see cref="SheetSetImportLayoutsViewModel"/>.
    /// </summary>
    public partial class SheetSetImportLayoutsWindow : SheetSetDialogWindow
    {
        /// <summary>Makes the dialog over its view model.</summary>
        /// <param name="model">The view model.</param>
        /// <exception cref="ArgumentNullException">Thrown when the model is null.</exception>
        public SheetSetImportLayoutsWindow(SheetSetImportLayoutsViewModel model)
            : base(model)
        {
            InitializeComponent();
            Wire(Frame);
        }

        /// <summary>Gets the view model.</summary>
        public new SheetSetImportLayoutsViewModel Model => (SheetSetImportLayoutsViewModel)base.Model;

        /// <summary>Shows the dialog over a window, which waits until it is closed.</summary>
        /// <param name="owner">The window it is shown over; null for none.</param>
        /// <param name="model">The view model.</param>
        /// <returns>True when Import Checked made the sheets.</returns>
        public static bool Show(Window owner, SheetSetImportLayoutsViewModel model) =>
            new SheetSetImportLayoutsWindow(model).Present(owner);

        private void OnBrowse(object sender, RoutedEventArgs e) => Model.BrowseDrawing();
    }
}
