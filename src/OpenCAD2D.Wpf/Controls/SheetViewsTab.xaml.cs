using System;
using System.Collections.Generic;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;

namespace OpenCAD2D.Wpf
{
    /// <summary>
    /// The body of the Sheet Views tab of the Sheet Set Manager: the toolbar with New View Category and the two buttons View by
    /// Category and View by Sheet, and the tree of the views placed on the sheets. A double click shows a view (Display), a view
    /// dragged onto a category is put in it, and the right-click menu of a row is <see cref="SheetSetManagerViewModel.SheetViewsMenuFor"/>.
    /// Its logic is <see cref="SheetSetManagerViewModel"/>.
    /// </summary>
    public partial class SheetViewsTab : UserControl
    {
        private const string DragFormat = "OpenCAD2D.SheetSetViewRows";

        private Point _dragStart;
        private SheetSetTabRow _dragRow;
        private List<SheetSetTabRow> _dragged;
        private List<SheetSetTabRow> _selectedAtDown = new List<SheetSetTabRow>();

        /// <summary>Makes the tab's body over the palette's view model.</summary>
        /// <param name="model">The view model.</param>
        /// <exception cref="ArgumentNullException">Thrown when the model is null.</exception>
        public SheetViewsTab(SheetSetManagerViewModel model)
        {
            Model = model ?? throw new ArgumentNullException(nameof(model));
            InitializeComponent();
            DataContext = model;
        }

        /// <summary>Gets the view model.</summary>
        public SheetSetManagerViewModel Model { get; }

        /// <summary>Gets the list that shows the tree.</summary>
        public ListBox TreeList => Tree;

        private void OnNewCategory(object sender, RoutedEventArgs e) => Model.NewViewCategoryDialog();

        /// <summary>The row an element is in, or null.</summary>
        internal static SheetSetTabRow RowOf(DependencyObject source)
        {
            while (source != null)
            {
                if (source is ListBoxItem item)
                {
                    return item.DataContext as SheetSetTabRow;
                }

                source =
                    source is Visual || source is System.Windows.Media.Media3D.Visual3D
                        ? VisualTreeHelper.GetParent(source)
                        : LogicalTreeHelper.GetParent(source);
            }

            return null;
        }

        private void OnTreeMouseDown(object sender, MouseButtonEventArgs e)
        {
            _dragStart = e.GetPosition(Tree);
            _selectedAtDown = Model.SelectedViewRows.ToList();
            _dragRow = RowOf(e.OriginalSource as DependencyObject);

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
            if (e.LeftButton != MouseButtonState.Pressed || _dragRow == null || _dragged != null)
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

            // Only views are dragged (onto a category), and not in a set that is locked.
            if (Model.IsLocked || row.Kind != SheetSetTabRowKind.View)
            {
                return;
            }

            _dragged = _selectedAtDown.Contains(row) ? _selectedAtDown : new List<SheetSetTabRow> { row };

            var data = new DataObject();
            data.SetData(DragFormat, row.Id);

            try
            {
                DragDrop.DoDragDrop(Tree, data, DragDropEffects.Move);
            }
            finally
            {
                _dragged = null;
                DropLine.Visibility = Visibility.Collapsed;
            }
        }

        /// <summary>The row of a category or the set under a point of the tree, where a view can be dropped; false elsewhere.</summary>
        private bool TargetAt(Point at, out SheetSetTabRow target, out Rect bounds)
        {
            target = RowOf(Tree.InputHitTest(at) as DependencyObject);
            bounds = Rect.Empty;

            if (
                target == null
                || (target.Kind != SheetSetTabRowKind.Category && target.Kind != SheetSetTabRowKind.SheetSet)
                || !(Tree.ItemContainerGenerator.ContainerFromItem(target) is ListBoxItem item)
            )
            {
                return false;
            }

            Point top = item.TranslatePoint(new Point(0, 0), Tree);
            bounds = new Rect(top, new Size(item.ActualWidth, item.ActualHeight));
            return true;
        }

        private void OnTreeDragOver(object sender, DragEventArgs e)
        {
            e.Effects = DragDropEffects.None;
            DropLine.Visibility = Visibility.Collapsed;

            if (
                _dragged != null
                && Model.ViewsByCategory
                && TargetAt(e.GetPosition(Tree), out SheetSetTabRow _, out Rect bounds)
            )
            {
                e.Effects = DragDropEffects.Move;
                Point origin = Tree.TranslatePoint(new Point(0, 0), (UIElement)DropLine.Parent);
                DropLine.Margin = new Thickness(0, origin.Y + bounds.Top, 0, 0);
                DropLine.Visibility = Visibility.Visible;
            }

            e.Handled = true;
        }

        private void OnTreeDragLeave(object sender, DragEventArgs e) => DropLine.Visibility = Visibility.Collapsed;

        private void OnTreeDrop(object sender, DragEventArgs e)
        {
            DropLine.Visibility = Visibility.Collapsed;

            if (
                _dragged != null
                && Model.ViewsByCategory
                && TargetAt(e.GetPosition(Tree), out SheetSetTabRow target, out Rect _)
            )
            {
                Model.SetCategory(_dragged, target);
            }

            e.Handled = true;
        }

        private void OnTreeDoubleClick(object sender, MouseButtonEventArgs e)
        {
            SheetSetTabRow row = RowOf(e.OriginalSource as DependencyObject);

            if (row == null)
            {
                return;
            }

            if (row.Kind == SheetSetTabRowKind.View)
            {
                Model.DisplayView(row);
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
            SheetSetTabRow row = RowOf(e.OriginalSource as DependencyObject);

            if (row != null && !row.IsSelected)
            {
                foreach (SheetSetTabRow selected in Model.SelectedViewRows)
                {
                    selected.IsSelected = false;
                }

                row.IsSelected = true;
            }
        }

        /// <summary>The right-click menu of the row under the pointer, built as it opens; one for no row too (New View Category…).</summary>
        private void OnTreeMenuOpening(object sender, ContextMenuEventArgs e)
        {
            IReadOnlyList<CadMenuItemViewModel> items = Model.SheetViewsMenuFor(
                RowOf(e.OriginalSource as DependencyObject)
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
            SheetSetTabRow row = Model.SelectedViewRows.FirstOrDefault();

            if (row == null)
            {
                return;
            }

            switch (e.Key)
            {
                case Key.Enter when row.Kind == SheetSetTabRowKind.View:
                    Model.DisplayView(row);
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
