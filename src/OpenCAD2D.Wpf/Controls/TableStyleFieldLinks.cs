using System;
using System.Collections.Generic;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Threading;

namespace OpenCAD2D.Wpf
{
    /// <summary>
    /// Links the controls of the Modify Table Style dialog to its previews, as <see cref="StyleFieldLinks"/> does for a list of
    /// fields: a control is named by the key of the setting it sets, either itself (<c>Key</c>, for a border button) or by the row of
    /// the grid it stands in (<c>RowKeys</c>, one key to a row, empty for a row that sets nothing). The view model is told which
    /// setting the pointer is over and which has the focus, and the rows of the settings to light are shaded and barred in the accent.
    /// </summary>
    public static class TableStyleFieldLinks
    {
        /// <summary>The key of the setting a control sets: <c>Border:Outside</c>.</summary>
        public static readonly DependencyProperty KeyProperty = DependencyProperty.RegisterAttached(
            "Key",
            typeof(string),
            typeof(TableStyleFieldLinks),
            new PropertyMetadata(null)
        );

        /// <summary>The keys of the settings of the rows of a grid, one to a row, separated by commas; a row that sets nothing has none.</summary>
        public static readonly DependencyProperty RowKeysProperty = DependencyProperty.RegisterAttached(
            "RowKeys",
            typeof(string),
            typeof(TableStyleFieldLinks),
            new PropertyMetadata(null)
        );

        /// <summary>Gets the key of the setting a control sets.</summary>
        /// <param name="element">The control.</param>
        /// <returns>The key; null for none.</returns>
        public static string GetKey(DependencyObject element) => (string)element.GetValue(KeyProperty);

        /// <summary>Sets the key of the setting a control sets.</summary>
        /// <param name="element">The control.</param>
        /// <param name="value">The key.</param>
        public static void SetKey(DependencyObject element, string value) => element.SetValue(KeyProperty, value);

        /// <summary>Gets the keys of the settings of the rows of a grid.</summary>
        /// <param name="element">The grid.</param>
        /// <returns>The keys, separated by commas.</returns>
        public static string GetRowKeys(DependencyObject element) => (string)element.GetValue(RowKeysProperty);

        /// <summary>Sets the keys of the settings of the rows of a grid.</summary>
        /// <param name="element">The grid.</param>
        /// <param name="value">The keys, separated by commas.</param>
        public static void SetRowKeys(DependencyObject element, string value) =>
            element.SetValue(RowKeysProperty, value);

        /// <summary>
        /// Watches a dialog: the pointer over a setting, and the focus entering or leaving the control of one, are reported with its
        /// key. A border button is hovered but takes no focus into account: after a click it would keep its lines lit.
        /// </summary>
        /// <param name="root">The dialog's content.</param>
        /// <param name="hovered">Called with a key and whether the pointer is over it.</param>
        /// <param name="focused">Called with a key and whether the focus is in it.</param>
        internal static void Attach(FrameworkElement root, Action<string, bool> hovered, Action<string, bool> focused)
        {
            string over = null;

            void Over(string next)
            {
                if (string.Equals(over, next, StringComparison.Ordinal))
                {
                    return;
                }

                string before = over;
                over = next;

                if (before != null)
                {
                    hovered(before, false);
                }

                if (next != null)
                {
                    hovered(next, true);
                }
            }

            root.PreviewMouseMove += (s, e) => Over(KeyOf(e.OriginalSource as DependencyObject, root));
            root.MouseLeave += (s, e) => Over(null);
            root.AddHandler(
                UIElement.GotKeyboardFocusEvent,
                new KeyboardFocusChangedEventHandler(
                    (s, e) =>
                    {
                        string key = KeyOf(e.NewFocus as DependencyObject, root);

                        if (key != null && !key.StartsWith("Border:", StringComparison.Ordinal))
                        {
                            focused(key, true);
                        }
                    }
                ),
                true
            );
            root.AddHandler(
                UIElement.LostKeyboardFocusEvent,
                new KeyboardFocusChangedEventHandler(
                    (s, e) =>
                    {
                        string key = KeyOf(e.OldFocus as DependencyObject, root);

                        if (key != null && !key.StartsWith("Border:", StringComparison.Ordinal))
                        {
                            focused(key, false);
                        }
                    }
                ),
                true
            );
        }

        /// <summary>The key of the setting a control sets: its own, else that of its row in the grid it stands in; null for a control that sets none.</summary>
        /// <param name="source">The control, or something inside it.</param>
        /// <param name="root">The dialog's content, where the search stops.</param>
        /// <returns>The key; null for none.</returns>
        internal static string KeyOf(DependencyObject source, DependencyObject root)
        {
            for (DependencyObject d = source; d != null && !ReferenceEquals(d, root); d = Parent(d))
            {
                string own = GetKey(d);

                if (own != null)
                {
                    return own;
                }

                if (Parent(d) is Grid grid && GetRowKeys(grid) != null && d is UIElement element)
                {
                    string[] keys = GetRowKeys(grid).Split(',');
                    int row = Grid.GetRow(element);
                    return row >= 0 && row < keys.Length && keys[row].Length > 0 ? keys[row] : null;
                }
            }

            return null;
        }

        private static DependencyObject Parent(DependencyObject d) =>
            d is Visual ? VisualTreeHelper.GetParent(d) : LogicalTreeHelper.GetParent(d);

        /// <summary>
        /// Puts a shaded bar behind every row of every grid under a root that has a key, hidden until its setting is to be lit.
        /// </summary>
        /// <param name="root">The dialog's content.</param>
        /// <param name="bar">The brush of the bar at the row's left.</param>
        /// <param name="shade">The brush of the row's ground.</param>
        /// <returns>The bars by key, for <see cref="Light"/>.</returns>
        internal static IReadOnlyDictionary<string, Border> Rows(DependencyObject root, Brush bar, Brush shade)
        {
            var rows = new Dictionary<string, Border>(StringComparer.Ordinal);

            foreach (Grid grid in Grids(root))
            {
                string[] keys = GetRowKeys(grid).Split(',');

                for (int row = 0; row < keys.Length; row++)
                {
                    if (keys[row].Length == 0)
                    {
                        continue;
                    }

                    var light = new Border
                    {
                        Background = shade,
                        BorderBrush = bar,
                        BorderThickness = new Thickness(3, 0, 0, 0),
                        Margin = new Thickness(-8, -2, -4, 0),
                        IsHitTestVisible = false,
                        Visibility = Visibility.Collapsed,
                    };
                    Grid.SetRow(light, row);
                    Grid.SetColumnSpan(light, Math.Max(1, grid.ColumnDefinitions.Count));
                    Panel.SetZIndex(light, -1);
                    grid.Children.Add(light);
                    rows[keys[row]] = light;
                }
            }

            return rows;
        }

        /// <summary>Shows the bars of the keys to light, and hides the others.</summary>
        /// <param name="rows">The bars, as <see cref="Rows"/> made them.</param>
        /// <param name="lit">The keys to light.</param>
        internal static void Light(IReadOnlyDictionary<string, Border> rows, IReadOnlyCollection<string> lit)
        {
            var keys = new HashSet<string>(lit, StringComparer.Ordinal);

            foreach (KeyValuePair<string, Border> row in rows)
            {
                row.Value.Visibility = keys.Contains(row.Key) ? Visibility.Visible : Visibility.Collapsed;
            }
        }

        private static IEnumerable<Grid> Grids(DependencyObject parent)
        {
            foreach (object child in LogicalTreeHelper.GetChildren(parent))
            {
                if (!(child is DependencyObject element))
                {
                    continue;
                }

                if (element is Grid grid && GetRowKeys(grid) != null)
                {
                    yield return grid;
                }

                foreach (Grid below in Grids(element))
                {
                    yield return below;
                }
            }
        }

        /// <summary>
        /// Puts the focus in the control of a setting once the dialog has drawn the tab that was just shown: its own control where the
        /// key names one, else the first control of its row.
        /// </summary>
        /// <param name="root">The dialog's content.</param>
        /// <param name="key">The setting's key.</param>
        internal static void Focus(FrameworkElement root, string key)
        {
            root.Dispatcher.BeginInvoke(
                DispatcherPriority.Loaded,
                new Action(() =>
                {
                    root.UpdateLayout();
                    FrameworkElement control = Find(root, key);
                    control?.Focus();
                })
            );
        }

        private static FrameworkElement Find(DependencyObject parent, string key)
        {
            for (int i = 0; i < VisualTreeHelper.GetChildrenCount(parent); i++)
            {
                DependencyObject child = VisualTreeHelper.GetChild(parent, i);
                string[] keys = child is Grid grid && GetRowKeys(grid) != null ? GetRowKeys(grid).Split(',') : null;
                int row = keys == null ? -1 : Array.IndexOf(keys, key);

                if (row >= 0)
                {
                    for (int c = 0; c < VisualTreeHelper.GetChildrenCount(child); c++)
                    {
                        DependencyObject cell = VisualTreeHelper.GetChild(child, c);

                        if (cell is UIElement element && Grid.GetRow(element) == row)
                        {
                            FrameworkElement control = StyleFieldLinks.IsControl(cell)
                                ? (FrameworkElement)cell
                                : StyleFieldLinks.FirstControl(cell);

                            if (control != null)
                            {
                                return control;
                            }
                        }
                    }
                }

                FrameworkElement found = Find(child, key);

                if (found != null)
                {
                    return found;
                }
            }

            return null;
        }
    }
}
