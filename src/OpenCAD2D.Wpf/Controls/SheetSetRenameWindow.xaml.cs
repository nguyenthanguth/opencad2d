using System;
using System.Windows;
using System.Windows.Controls;

namespace OpenCAD2D.Wpf
{
    /// <summary>
    /// Rename &amp; Renumber Sheet, as the board A12RenameRenumber draws it: Number, Sheet title, Layout name, File name and Folder
    /// path, the Rename boxes with their Prefix boxes, the yellow line for a drawing open here, the red line for a name refused, and
    /// Previous and Next at the left of the footer. Its logic is <see cref="SheetSetRenameViewModel"/>.
    /// </summary>
    public partial class SheetSetRenameWindow : SheetSetDialogWindow
    {
        /// <summary>Makes the dialog over its view model.</summary>
        /// <param name="model">The view model.</param>
        /// <exception cref="ArgumentNullException">Thrown when the model is null.</exception>
        public SheetSetRenameWindow(SheetSetRenameViewModel model)
            : base(model)
        {
            InitializeComponent();
            Wire(Frame);
            var buttons = new StackPanel { Orientation = Orientation.Horizontal };
            buttons.Children.Add(Step("< Previous", "HasPrevious", () => model.Previous(), 8));
            buttons.Children.Add(Step("Next >", "HasNext", () => model.Next(), 0));
            Frame.FooterContent = buttons;
        }

        /// <summary>Gets the view model.</summary>
        public new SheetSetRenameViewModel Model => (SheetSetRenameViewModel)base.Model;

        /// <summary>Shows the dialog over a window, which waits until it is closed.</summary>
        /// <param name="owner">The window it is shown over; null for none.</param>
        /// <param name="model">The view model.</param>
        /// <returns>True when OK applied the changes.</returns>
        public static bool Show(Window owner, SheetSetRenameViewModel model) =>
            new SheetSetRenameWindow(model).Present(owner);

        private Button Step(string text, string enabledProperty, Func<bool> step, double gap)
        {
            var button = new Button
            {
                Content = text,
                Style = (Style)FindResource("GhDialogButton"),
                MinWidth = 75,
                Margin = new Thickness(0, 0, gap, 0),
            };
            button.SetBinding(
                IsEnabledProperty,
                new System.Windows.Data.Binding(enabledProperty) { Mode = System.Windows.Data.BindingMode.OneWay }
            );
            button.Click += (s, e) => step();
            return button;
        }
    }
}
