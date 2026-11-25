using System;
using System.Windows;

namespace OpenCAD2D.Wpf
{
    /// <summary>
    /// Select Layout as Sheet Template (and the pickers of Expected layout and of Label block for views, which are the same form
    /// with their own words): a file name with a ... button and the list of the layouts or blocks of that drawing, 480 wide.
    /// Its logic is <see cref="SheetSetTemplatePickViewModel"/>.
    /// </summary>
    public partial class SheetSetTemplateWindow : SheetSetDialogWindow
    {
        /// <summary>Makes the dialog over its view model.</summary>
        /// <param name="model">The view model.</param>
        /// <exception cref="ArgumentNullException">Thrown when the model is null.</exception>
        public SheetSetTemplateWindow(SheetSetTemplatePickViewModel model)
            : base(model)
        {
            InitializeComponent();
            Wire(Frame);
        }

        /// <summary>Gets the view model.</summary>
        public new SheetSetTemplatePickViewModel Model => (SheetSetTemplatePickViewModel)base.Model;

        /// <summary>Shows the dialog over a window, which waits until it is closed.</summary>
        /// <param name="owner">The window it is shown over; null for none.</param>
        /// <param name="model">The view model.</param>
        /// <returns>True when OK chose a layout or block; it is the view model's <see cref="SheetSetTemplatePickViewModel.Result"/>.</returns>
        public static bool Show(Window owner, SheetSetTemplatePickViewModel model) =>
            new SheetSetTemplateWindow(model).Present(owner);

        private void OnBrowse(object sender, RoutedEventArgs e) => Model.Browse();
    }
}
