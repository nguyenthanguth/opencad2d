using System;
using System.Collections.Generic;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;

namespace OpenCAD2D.Wpf
{
    /// <summary>
    /// The body of the Model Views tab of the Sheet Set Manager: the toolbar with Refresh and Add New Location, and the tree of
    /// the set's resource locations, the drawings in them and the named model views of each. A view or drawing dragged onto the
    /// drawing area is placed on the sheet current (Place on Sheet), a double click on a view does the same, and the right-click
    /// menu of a row is <see cref="SheetSetManagerViewModel.ModelViewsMenuFor"/>. Its logic is <see cref="SheetSetManagerViewModel"/>.
    /// </summary>
    public partial class ModelViewsTab : UserControl
    {
        private Point _dragStart;
        private SheetSetTabRow _dragRow;

        /// <summary>Makes the tab's body over the palette's view model.</summary>
        /// <param name="model">The view model.</param>
        /// <exception cref="ArgumentNullException">Thrown when the model is null.</exception>
        public ModelViewsTab(SheetSetManagerViewModel model)
        {
            Model = model ?? throw new ArgumentNullException(nameof(model));
            InitializeComponent();
            DataContext = model;
        }

        /// <summary>Gets the view model.</summary>
        public SheetSetManagerViewModel Model { get; }

        /// <summary>Gets the list that shows the tree.</summary>
        public ListBox TreeList => Tree;

        private void OnRefresh(object sender, RoutedEventArgs e) => Model.RefreshModelViews();

        private void OnAddLocation(object sender, RoutedEventArgs e) => Model.AddLocationDialog();

        private void OnTreeMouseDown(object sender, MouseButtonEventArgs e)
        {
            _dragStart = e.GetPosition(Tree);
            _dragRow = SheetViewsTab.RowOf(e.OriginalSource as DependencyObject);

            // The arrow of a row opens and closes it, and takes no selection with it.
            if (
                _dragRow != null
                && _dragRow.IsExpandable
                && e.OriginalSource is SheetSetIcon icon
                && (icon.Glyph == "chevron-down" || icon.Glyph == "chevron-right")
            )
            {
                _dragRow.IsExpanded = !_dragRow.IsExpanded;
                _dragRow = null;
                e.Handled = true;
            }
        }

        private void OnTreeMouseMove(object sender, MouseEventArgs e)
        {
            if (e.LeftButton != MouseButtonState.Pressed || _dragRow == null)
            {
                return;
            }

            Vector moved = e.GetPosition(Tree) - _dragStart;

            if (
                Math.Abs(moved.X) < SystemParameters.MinimumHorizontalDragDistance
                && Math.Abs(moved.Y) < SystemParameters.MinimumVerticalDragDistance
            )
            {
                return;
            }

            SheetSetTabRow row = _dragRow;
            _dragRow = null;

            // A drawing or one of its views is dragged onto the drawing area; a location is not.
            if (row.Kind != SheetSetTabRowKind.Drawing && row.Kind != SheetSetTabRowKind.ModelView)
            {
                return;
            }

            var data = new DataObject();
            data.SetData(SheetSetModelViewDrag.Format, SheetSetModelViewDrag.Write(row.Path, row.ViewName));
            DragDrop.DoDragDrop(Tree, data, DragDropEffects.Copy);
        }

        private void OnTreeDoubleClick(object sender, MouseButtonEventArgs e)
        {
            SheetSetTabRow row = SheetViewsTab.RowOf(e.OriginalSource as DependencyObject);

            if (row == null)
            {
                return;
            }

            if (row.Kind == SheetSetTabRowKind.ModelView)
            {
                Model.PlaceOnSheet(row);
            }
            else if (
                row.IsExpandable
                && !(
                    e.OriginalSource is SheetSetIcon icon && icon.Glyph.StartsWith("chevron", StringComparison.Ordinal)
                )
            )
            {
                row.IsExpanded = !row.IsExpanded;
            }
        }

        /// <summary>A right click on a row outside the selection selects that row alone, as a click does.</summary>
        private void OnTreeRightDown(object sender, MouseButtonEventArgs e)
        {
            SheetSetTabRow row = SheetViewsTab.RowOf(e.OriginalSource as DependencyObject);

            if (row != null && !row.IsSelected)
            {
                foreach (SheetSetTabRow selected in Model.SelectedModelRows)
                {
                    selected.IsSelected = false;
                }

                row.IsSelected = true;
            }
        }

        /// <summary>The right-click menu of the row under the pointer, built as it opens; one for no row too (Add New Location…).</summary>
        private void OnTreeMenuOpening(object sender, ContextMenuEventArgs e)
        {
            IReadOnlyList<CadMenuItemViewModel> items = Model.ModelViewsMenuFor(
                SheetViewsTab.RowOf(e.OriginalSource as DependencyObject)
            );

            if (items.Count == 0)
            {
                e.Handled = true;
                return;
            }

            Tree.ContextMenu = CadMenus.Make(items);
        }

        private void OnTreeKeyDown(object sender, KeyEventArgs e)
        {
            SheetSetTabRow row = Model.SelectedModelRows.FirstOrDefault();

            if (row == null)
            {
                return;
            }

            switch (e.Key)
            {
                case Key.Enter when row.Kind == SheetSetTabRowKind.ModelView:
                    Model.PlaceOnSheet(row);
                    e.Handled = true;
                    break;
                case Key.Right when row.IsExpandable && !row.IsExpanded:
                    row.IsExpanded = true;
                    e.Handled = true;
                    break;
                case Key.Left when row.IsExpandable && row.IsExpanded:
                    row.IsExpanded = false;
                    e.Handled = true;
                    break;
            }
        }
    }
}
