using System;
using System.Windows;

namespace OpenCAD2D.Wpf
{
    /// <summary>
    /// Custom Properties, as the board draws it: a list of Name, Default Value and Owner with Add… and Delete at its side, and OK
    /// and Cancel at the foot. Add… opens Add Custom Property over this dialog; Delete asks first. What is added and deleted is
    /// kept until OK writes the sheet set. Its logic is <see cref="SheetSetCustomPropertiesViewModel"/>.
    /// </summary>
    public partial class SheetSetCustomPropertiesWindow : SheetSetDialogWindow
    {
        /// <summary>Makes the dialog over its view model.</summary>
        /// <param name="model">The view model.</param>
        /// <exception cref="ArgumentNullException">Thrown when the model is null.</exception>
        public SheetSetCustomPropertiesWindow(SheetSetCustomPropertiesViewModel model)
            : base(model)
        {
            InitializeComponent();
            Wire(Frame);
            model.ConfirmDelete = model.ConfirmDelete ?? Confirm;
        }

        /// <summary>Gets the view model.</summary>
        public new SheetSetCustomPropertiesViewModel Model => (SheetSetCustomPropertiesViewModel)base.Model;

        /// <summary>Shows the dialog over a window, which waits until it is closed.</summary>
        /// <param name="owner">The window it is shown over; null for none.</param>
        /// <param name="model">The view model.</param>
        /// <returns>True when OK applied the changes.</returns>
        public static bool Show(Window owner, SheetSetCustomPropertiesViewModel model) =>
            new SheetSetCustomPropertiesWindow(model).Present(owner);

        private bool Confirm(string name)
        {
            var question = new CadQuestionWindow(
                "Delete the custom property " + name + "?",
                "Delete the custom property " + name + "?",
                "Delete: takes the property out of the sheet set and out of every sheet when OK is chosen.\nKeep: leaves it.",
                "Delete",
                "Keep"
            )
            {
                Owner = this,
            };
            return question.ShowDialog() == true;
        }

        private void OnAdd(object sender, RoutedEventArgs e) =>
            SheetSetAddPropertyWindow.Show(this, new SheetSetAddPropertyViewModel(Model));

        private void OnDelete(object sender, RoutedEventArgs e) => Model.DeleteSelected();
    }
}
