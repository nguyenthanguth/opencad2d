using System;
using System.Windows;

namespace OpenCAD2D.Wpf
{
    /// <summary>
    /// Add Custom Property, as the board draws it: Name, Default value and Owner (Sheet Set or Sheet), the red line under them where
    /// the name is refused, and OK, Cancel and Help. Its logic is <see cref="SheetSetAddPropertyViewModel"/>.
    /// </summary>
    public partial class SheetSetAddPropertyWindow : SheetSetDialogWindow
    {
        /// <summary>Makes the dialog over its view model.</summary>
        /// <param name="model">The view model.</param>
        /// <exception cref="ArgumentNullException">Thrown when the model is null.</exception>
        public SheetSetAddPropertyWindow(SheetSetAddPropertyViewModel model)
            : base(model)
        {
            InitializeComponent();
            Wire(Frame);
            OwnerSet.IsChecked = !model.OwnerIsSheet;
            OwnerSheet.IsChecked = model.OwnerIsSheet;
        }

        /// <summary>Gets the view model.</summary>
        public new SheetSetAddPropertyViewModel Model => (SheetSetAddPropertyViewModel)base.Model;

        /// <summary>Shows the dialog over a window, which waits until it is closed.</summary>
        /// <param name="owner">The window it is shown over; null for none.</param>
        /// <param name="model">The view model.</param>
        /// <returns>True when OK added the property to the list.</returns>
        public static bool Show(Window owner, SheetSetAddPropertyViewModel model) =>
            new SheetSetAddPropertyWindow(model).Present(owner);

        private void OnOwnerSet(object sender, RoutedEventArgs e) => Model.OwnerIsSheet = false;

        private void OnOwnerSheet(object sender, RoutedEventArgs e) => Model.OwnerIsSheet = true;
    }
}
