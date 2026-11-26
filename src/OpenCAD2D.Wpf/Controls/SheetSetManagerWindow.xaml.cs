using System;
using System.Collections.Generic;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using System.Windows.Media;

namespace OpenCAD2D.Wpf
{
    /// <summary>
    /// AutoCAD's Sheet Set Manager palette, which SHEETSET shows beside the window and leaves open until SHEETSETHIDE: a floating
    /// window with the sheet set control over the tabs Sheet List, Sheet Views and Model Views, the tab's toolbar, the tree of
    /// the set and the Details or Preview pane under it. A double click opens a sheet, a row dragged onto another moves it, and
    /// the right-click menus of the set, a subset and a sheet run its operations. Its logic is <see cref="SheetSetManagerViewModel"/>.
    /// </summary>
    public partial class SheetSetManagerWindow : Window
    {
        private const string DragFormat = "OpenCAD2D.SheetSetRows";

        private static SheetSetManagerWindow _open;
        private SheetViewsTab _sheetViews;
        private ModelViewsTab _modelViews;
        private Point _dragStart;
        private SheetSetNodeViewModel _dragNode;
        private List<SheetSetNodeViewModel> _dragged;
        private List<SheetSetNodeViewModel> _selectedAtDown = new List<SheetSetNodeViewModel>();

        /// <summary>Makes the palette over its view model.</summary>
        /// <param name="model">The view model.</param>
        /// <exception cref="ArgumentNullException">Thrown when the model is null.</exception>
        public SheetSetManagerWindow(SheetSetManagerViewModel model)
        {
            Model = model ?? throw new ArgumentNullException(nameof(model));
            string tab = model.Tab;
            InitializeComponent();

            // The tab the palette was left on is the one shown again: the first tab's button being checked at once has made it the
            // model's, which the model's own is put back over.
            model.Tab = tab;
            DataContext = model;
            model.AskOpenFile = AskSheetSetFile;
            model.ConfirmRemove = ConfirmRemove;
            model.AskFolder = AskLocationFolder;
            model.ShowViewCategoryDialog = dialog => ViewCategoryWindow.Show(this, dialog);
            model.ShowRenameRenumberViewDialog = dialog => RenameRenumberViewWindow.Show(this, dialog);
            model.PropertyChanged += OnModelChanged;
            ShowTab();
            ShowPane();
        }

        /// <summary>Gets the view model.</summary>
        public SheetSetManagerViewModel Model { get; }

        /// <summary>Gets the palette's content, so that a screenshot can draw it with no window shown.</summary>
        public FrameworkElement Body => Root;

        /// <summary>
        /// Shows the palette over a window, left open beside it: the one open already is brought to the front. The model is told it
        /// is open, so that SSMSTATE follows it, and the palette looks at its sheets again each time it is activated.
        /// </summary>
        /// <param name="owner">The window it is shown over; null for none.</param>
        /// <param name="mainModel">The window's view model, whose drawings it opens.</param>
        /// <param name="model">The view model.</param>
        public static void ShowPalette(Window owner, MainViewModel mainModel, SheetSetManagerViewModel model)
        {
            if (_open != null)
            {
                _open.Activate();
                return;
            }

            var window = new SheetSetManagerWindow(model) { Owner = owner };
            _open = window;
            window.Closed += (s, e) =>
            {
                mainModel.NoteSheetSetShown(false);
                _open = null;
            };
            window.Activated += (s, e) => model.Refresh();
            mainModel.NoteSheetSetShown(true);
            window.Show();
        }

        /// <summary>Closes the palette that is open, as SHEETSETHIDE does; nothing where none is.</summary>
        public static void ClosePalette() => _open?.Close();

        private string AskSheetSetFile()
        {
            var dialog = new Microsoft.Win32.OpenFileDialog
            {
                Filter = "Sheet set data (*.dst)|*.dst|All files (*.*)|*.*",
                Title = "Open Sheet Set",
            };
            return dialog.ShowDialog(this) == true ? dialog.FileName : null;
        }

        /// <summary>Asks for the folder of Add New Location: the file dialog left in a folder, as neither WPF of .NET Framework nor this project has a folder dialog.</summary>
        private string AskLocationFolder()
        {
            var dialog = new Microsoft.Win32.OpenFileDialog
            {
                Title = "Add New Location",
                FileName = "Select this folder",
                ValidateNames = false,
                CheckFileExists = false,
                CheckPathExists = true,
            };
            return dialog.ShowDialog(this) == true ? System.IO.Path.GetDirectoryName(dialog.FileName) : null;
        }

        private void OnModelChanged(object sender, System.ComponentModel.PropertyChangedEventArgs e)
        {
            if (e.PropertyName == nameof(SheetSetManagerViewModel.Tab))
            {
                ShowTab();
            }
            else if (e.PropertyName == nameof(SheetSetManagerViewModel.PaneShown))
            {
                ShowPane();
            }
        }

        /// <summary>Puts the body of the tab shown in the tab host, made the first time it is asked for; the Sheet List tab has its own tree and leaves the host empty.</summary>
        private void ShowTab()
        {
            TabHost.Children.Clear();
            RadioButton button =
                Model.Tab == "Sheet Views" ? SheetViewsTabButton
                : Model.Tab == "Model Views" ? ModelViewsTabButton
                : SheetListTab;

            if (button.IsChecked != true)
            {
                button.IsChecked = true;
            }

            if (Model.Tab == "Sheet Views")
            {
                TabHost.Children.Add(_sheetViews ?? (_sheetViews = new SheetViewsTab(Model)));
            }
            else if (Model.Tab == "Model Views")
            {
                TabHost.Children.Add(_modelViews ?? (_modelViews = new ModelViewsTab(Model)));
            }
        }

        /// <summary>Shows or hides the pane under the tree, as the Preview/Details Pane item of the Model Views menu says.</summary>
        private void ShowPane()
        {
            bool shown = Model.PaneShown;
            Pane.Visibility = shown ? Visibility.Visible : Visibility.Collapsed;
            PaneSplitter.Visibility = shown ? Visibility.Visible : Visibility.Collapsed;
            SplitterRow.Height = shown ? GridLength.Auto : new GridLength(0);
            PaneRow.MinHeight = shown ? 70 : 0;
            PaneRow.Height = shown ? new GridLength(230) : new GridLength(0);
        }

        private bool ConfirmRemove(string question) =>
            MessageBox.Show(this, question, "Sheet Set Manager", MessageBoxButton.YesNo, MessageBoxImage.Question)
            == MessageBoxResult.Yes;

        private void OnHeaderDown(object sender, MouseButtonEventArgs e)
        {
            if (e.ButtonState == MouseButtonState.Pressed && IsLoaded)
            {
                DragMove();
            }
        }

        private void OnClose(object sender, RoutedEventArgs e) => Close();

        private void OnTab(object sender, RoutedEventArgs e)
        {
            if (Model != null)
            {
                Model.Tab = (string)((RadioButton)sender).Content;
            }
        }

        private void OnDetails(object sender, RoutedEventArgs e)
        {
            if (Model != null)
            {
                Model.ShowPreview = false;
            }
        }

        private void OnPreview(object sender, RoutedEventArgs e)
        {
            if (Model != null)
            {
                Model.ShowPreview = true;
            }
        }

        // ---- The control and the toolbar

        /// <summary>Opens the list of the sheet set control under it, the border of the control blue while it is open.</summary>
        private void OnControlDown(object sender, MouseButtonEventArgs e)
        {
            ContextMenu menu = CadMenus.Make(Model.ControlMenu());
            Brush border = Control.BorderBrush;
            Control.BorderBrush = (Brush)FindResource("GhAccent");
            menu.Closed += (s, a) => Control.BorderBrush = border;
            Open(menu, Control);
            e.Handled = true;
        }

        private static void Open(ContextMenu menu, FrameworkElement under)
        {
            menu.PlacementTarget = under;
            menu.Placement = PlacementMode.Bottom;
            menu.IsOpen = true;
        }

        private void OnPublishPdf(object sender, RoutedEventArgs e) => Model.PublishToPdf();

        private void OnPublishPlotter(object sender, RoutedEventArgs e) => Model.PublishToPlotter();

        private void OnPublishOverride(object sender, RoutedEventArgs e) =>
            Open(CadMenus.Make(Model.ToolbarMenu(true)), (FrameworkElement)sender);

        private void OnPublishMenu(object sender, RoutedEventArgs e) =>
            Open(CadMenus.Make(Model.ToolbarMenu(false)), (FrameworkElement)sender);

        // ---- The tree

        /// <summary>The row an element is in, or null.</summary>
        private static SheetSetNodeViewModel NodeOf(DependencyObject source)
        {
            while (source != null)
            {
                if (source is ListBoxItem item)
                {
                    return item.DataContext as SheetSetNodeViewModel;
                }

                source =
                    source is Visual || source is System.Windows.Media.Media3D.Visual3D
                        ? VisualTreeHelper.GetParent(source)
                        : LogicalTreeHelper.GetParent(source);
            }

            return null;
        }

        private ListBoxItem ItemOf(SheetSetNodeViewModel node) =>
            Tree.ItemContainerGenerator.ContainerFromItem(node) as ListBoxItem;

        private void OnTreeMouseDown(object sender, MouseButtonEventArgs e)
        {
            _dragStart = e.GetPosition(Tree);

            // The list's own handling of this click may narrow the selection to the row clicked; a drag that follows is of what was selected.
            _selectedAtDown = Model.SelectedNodes.ToList();
            _dragNode = NodeOf(e.OriginalSource as DependencyObject);

            // The arrow of a row opens and closes it, and takes no selection with it.
            if (
                _dragNode != null
                && _dragNode.IsExpandable
                && e.OriginalSource is SheetSetIcon icon
                && (icon.Glyph == "chevron-down" || icon.Glyph == "chevron-right")
            )
            {
                _dragNode.IsExpanded = !_dragNode.IsExpanded;
                _dragNode = null;
                e.Handled = true;
            }
        }

        private void OnTreeMouseMove(object sender, MouseEventArgs e)
        {
            if (e.LeftButton != MouseButtonState.Pressed || _dragNode == null || _dragged != null)
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

            SheetSetNodeViewModel node = _dragNode;
            _dragNode = null;

            // A locked set is not dragged, and the set itself never is.
            if (Model.IsLocked || node.IsSheetSet)
            {
                return;
            }

            _dragged = _selectedAtDown.Contains(node) ? _selectedAtDown : new List<SheetSetNodeViewModel> { node };

            foreach (SheetSetNodeViewModel row in _dragged)
            {
                row.IsSelected = true;
            }

            var data = new DataObject();
            data.SetData(DragFormat, node.Id);

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

        /// <summary>Where on a row a drop is: the top fourth of a row that holds rows is before it, the bottom fourth after it, the middle in it; a sheet holds nothing, so its top half is before it and its bottom half after.</summary>
        /// <param name="onSheet">Whether the row is a sheet.</param>
        /// <param name="fraction">How far down the row, from 0 at its top to 1 at its bottom.</param>
        /// <returns>The position.</returns>
        internal static SheetSetDropPosition PositionAt(bool onSheet, double fraction) =>
            onSheet ? (fraction < 0.5 ? SheetSetDropPosition.Before : SheetSetDropPosition.After)
            : fraction < 0.25 ? SheetSetDropPosition.Before
            : fraction > 0.75 ? SheetSetDropPosition.After
            : SheetSetDropPosition.Into;

        /// <summary>The row under a point of the tree and where on it the point is: the top fourth is before it, the bottom fourth after it, the middle in it where it holds rows, else by halves.</summary>
        private bool TargetAt(
            Point at,
            out SheetSetNodeViewModel node,
            out SheetSetDropPosition position,
            out Rect bounds
        )
        {
            node = null;
            position = SheetSetDropPosition.Into;
            bounds = Rect.Empty;
            node = NodeOf(Tree.InputHitTest(at) as DependencyObject);
            ListBoxItem item = node == null ? null : ItemOf(node);

            if (item == null)
            {
                return false;
            }

            Point top = item.TranslatePoint(new Point(0, 0), Tree);
            bounds = new Rect(top, new Size(item.ActualWidth, item.ActualHeight));
            double y = (at.Y - top.Y) / Math.Max(1.0, item.ActualHeight);
            position = PositionAt(node.IsSheet, y);
            return true;
        }

        private void OnTreeDragOver(object sender, DragEventArgs e)
        {
            e.Effects = DragDropEffects.None;
            DropLine.Visibility = Visibility.Collapsed;

            if (
                _dragged != null
                && TargetAt(
                    e.GetPosition(Tree),
                    out SheetSetNodeViewModel target,
                    out SheetSetDropPosition position,
                    out Rect bounds
                )
                && Model.PlanDrop(_dragged, target, position) != null
            )
            {
                e.Effects = DragDropEffects.Move;
                Point origin = Tree.TranslatePoint(new Point(0, 0), (UIElement)DropLine.Parent);
                bool into = position == SheetSetDropPosition.Into;
                DropLine.Height = into ? bounds.Height : 2;
                DropLine.Opacity = into ? 0.3 : 1;
                DropLine.Margin = new Thickness(
                    0,
                    origin.Y + bounds.Top + (position == SheetSetDropPosition.After ? bounds.Height - 2 : 0),
                    0,
                    0
                );
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
                && TargetAt(
                    e.GetPosition(Tree),
                    out SheetSetNodeViewModel target,
                    out SheetSetDropPosition position,
                    out _
                )
            )
            {
                Model.Move(_dragged, target, position);
            }

            e.Handled = true;
        }

        private void OnTreeDoubleClick(object sender, MouseButtonEventArgs e)
        {
            SheetSetNodeViewModel node = NodeOf(e.OriginalSource as DependencyObject);

            if (node == null)
            {
                return;
            }

            if (node.IsSheet)
            {
                Model.OpenSheets(new[] { node }, false);
            }
            else if (
                node.IsExpandable
                && !(
                    e.OriginalSource is SheetSetIcon icon && icon.Glyph.StartsWith("chevron", StringComparison.Ordinal)
                )
            )
            {
                node.IsExpanded = !node.IsExpanded;
            }
        }

        /// <summary>A right click on a row outside the selection selects that row alone, as a click does.</summary>
        private void OnTreeRightDown(object sender, MouseButtonEventArgs e)
        {
            SheetSetNodeViewModel node = NodeOf(e.OriginalSource as DependencyObject);

            if (node != null && !node.IsSelected)
            {
                foreach (SheetSetNodeViewModel row in Model.SelectedNodes)
                {
                    row.IsSelected = false;
                }

                node.IsSelected = true;
            }
        }

        /// <summary>The right-click menu of the row under the pointer, built as it opens; none for a click on no row.</summary>
        private void OnTreeMenuOpening(object sender, ContextMenuEventArgs e)
        {
            SheetSetNodeViewModel node = NodeOf(e.OriginalSource as DependencyObject);
            IReadOnlyList<CadMenuItemViewModel> items = Model.MenuFor(node);

            if (items.Count == 0)
            {
                e.Handled = true;
                return;
            }

            Tree.ContextMenu = CadMenus.Make(items);
        }

        private void OnTreeKeyDown(object sender, KeyEventArgs e)
        {
            SheetSetNodeViewModel node = Model.SelectedNodes.FirstOrDefault();

            if (node == null)
            {
                return;
            }

            switch (e.Key)
            {
                case Key.Enter when node.IsSheet:
                    Model.OpenSheets(Model.SelectedNodes, false);
                    e.Handled = true;
                    break;
                case Key.Right when node.IsExpandable && !node.IsExpanded:
                    node.IsExpanded = true;
                    e.Handled = true;
                    break;
                case Key.Left when node.IsExpandable && node.IsExpanded:
                    node.IsExpanded = false;
                    e.Handled = true;
                    break;
            }
        }
    }
}
