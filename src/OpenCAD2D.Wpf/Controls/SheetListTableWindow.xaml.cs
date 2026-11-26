using System;
using System.Windows;

namespace OpenCAD2D.Wpf
{
    /// <summary>
    /// Insert Sheet List Table and Edit Sheet List Table Settings, as the board A12SheetListTable draws them: the table style with
    /// its preview and Include Subsets as Headers in Output at the left, the tabs Table Data (the title text, the columns with
    /// their data type and heading, Add, Remove, Move Up, Move Down) and Subsets and Sheets (the tree of the set with a check box
    /// each, Populate From Sheet Selection greyed) at the right, and OK, Cancel and Help. Its logic is
    /// <see cref="SheetListTableViewModel"/>.
    /// </summary>
    public partial class SheetListTableWindow : SheetSetDialogWindow
    {
        /// <summary>Makes the dialog over its view model.</summary>
        /// <param name="model">The view model.</param>
        /// <exception cref="ArgumentNullException">Thrown when the model is null.</exception>
        public SheetListTableWindow(SheetListTableViewModel model)
            : base(model)
        {
            InitializeComponent();
            Wire(Frame);
        }

        /// <summary>Gets the view model.</summary>
        public new SheetListTableViewModel Model => (SheetListTableViewModel)base.Model;

        /// <summary>Gets or sets what shows the Table Style dialog over this one, and which the "..." button next to the table style calls; null for none.</summary>
        public Action<Window> ShowTableStyles { get; set; }

        /// <summary>Shows the dialog over a window, which waits until it is closed.</summary>
        /// <param name="owner">The window it is shown over; null for none.</param>
        /// <param name="model">The view model.</param>
        /// <param name="showTableStyles">What shows the Table Style dialog over the dialog given; null for none.</param>
        /// <returns>True when OK was answered.</returns>
        public static bool Show(Window owner, SheetListTableViewModel model, Action<Window> showTableStyles = null) =>
            new SheetListTableWindow(model) { ShowTableStyles = showTableStyles }.Present(owner);

        private void OnTableStyles(object sender, RoutedEventArgs e)
        {
            ShowTableStyles?.Invoke(this);
            Model.ReloadStyles();
        }

        private void OnAdd(object sender, RoutedEventArgs e) => Model.AddColumn();

        private void OnRemove(object sender, RoutedEventArgs e) => Model.RemoveColumn();

        private void OnUp(object sender, RoutedEventArgs e) => Model.MoveColumnUp();

        private void OnDown(object sender, RoutedEventArgs e) => Model.MoveColumnDown();
    }
}
