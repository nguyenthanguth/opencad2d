using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;

namespace OpenCAD2D.Wpf
{
    /// <summary>
    /// The property grid of Sheet Set, Subset and Sheet Properties: the rows of <see cref="SheetSetGridViewModel.Rows"/> in
    /// sections, each a label and a value typed, chosen or picked by its ... button. A click or the keyboard reaching a row
    /// makes it the selected row, whose description the dialog shows under the grid. Its data context is the dialog's view
    /// model.
    /// </summary>
    public partial class SheetSetPropertyGrid : UserControl
    {
        /// <summary>Makes the grid.</summary>
        public SheetSetPropertyGrid()
        {
            InitializeComponent();
            AddHandler(GotKeyboardFocusEvent, new KeyboardFocusChangedEventHandler(OnFocus), true);
            AddHandler(PreviewMouseLeftButtonDownEvent, new MouseButtonEventHandler(OnPress), true);
        }

        /// <summary>Gets or sets the tallest the grid grows before it scrolls.</summary>
        public double MaxRowsHeight
        {
            get => Scroller.MaxHeight;
            set => Scroller.MaxHeight = value;
        }

        private void OnFocus(object sender, KeyboardFocusChangedEventArgs e) => Select(e.NewFocus);

        private void OnPress(object sender, MouseButtonEventArgs e) => Select(e.OriginalSource);

        private void Select(object source)
        {
            if (
                DataContext is SheetSetGridViewModel model
                && source is FrameworkElement element
                && element.DataContext is SheetSetGridRow row
                && !row.IsSection
            )
            {
                model.SelectedRow = row;
            }
        }

        private void OnDots(object sender, RoutedEventArgs e)
        {
            if (sender is FrameworkElement element && element.DataContext is SheetSetGridRow row)
            {
                if (DataContext is SheetSetGridViewModel model)
                {
                    model.SelectedRow = row;
                }

                row.Browse();
            }
        }
    }
}
