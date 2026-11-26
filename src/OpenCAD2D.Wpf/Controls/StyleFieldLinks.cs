using System;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Threading;

namespace OpenCAD2D.Wpf
{
    /// <summary>
    /// Links the rows of a style dialog's field list to its preview: tells the view model which field the pointer is over and which
    /// has the keyboard focus (the preview lights the part of that field), and, when a click on a part of the preview chooses a field,
    /// scrolls its row into view and puts the focus in its control.
    /// </summary>
    internal static class StyleFieldLinks
    {
        /// <summary>
        /// Watches a list of fields. The pointer over a row, and the focus entering or leaving a row's controls, are reported with the
        /// field of the row; a group's title is no field.
        /// </summary>
        /// <typeparam name="T">The type of the field view models.</typeparam>
        /// <param name="list">The items control that lists the fields.</param>
        /// <param name="hovered">Called with a field and whether the pointer is over it.</param>
        /// <param name="focused">Called with a field and whether the focus is in it.</param>
        /// <param name="isField">Whether a view model is a field, not a group's title.</param>
        internal static void Attach<T>(
            ItemsControl list,
            Action<T, bool> hovered,
            Action<T, bool> focused,
            Func<T, bool> isField
        )
            where T : class
        {
            T over = null;

            void Over(T next)
            {
                if (ReferenceEquals(over, next))
                {
                    return;
                }

                T before = over;
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

            list.PreviewMouseMove += (s, e) =>
            {
                T field = FieldOf<T>(e.OriginalSource as DependencyObject, list);
                Over(field != null && isField(field) ? field : null);
            };
            list.MouseLeave += (s, e) => Over(null);
            list.AddHandler(
                UIElement.GotKeyboardFocusEvent,
                new KeyboardFocusChangedEventHandler(
                    (s, e) =>
                    {
                        T field = FieldOf<T>(e.NewFocus as DependencyObject, list);

                        if (field != null && isField(field))
                        {
                            focused(field, true);
                        }
                    }
                ),
                true
            );
            list.AddHandler(
                UIElement.LostKeyboardFocusEvent,
                new KeyboardFocusChangedEventHandler(
                    (s, e) =>
                    {
                        T field = FieldOf<T>(e.OldFocus as DependencyObject, list);

                        if (field != null && isField(field))
                        {
                            focused(field, false);
                        }
                    }
                ),
                true
            );
        }

        /// <summary>The field of the row a control is in: the nearest ancestor within the list whose data is a field.</summary>
        internal static T FieldOf<T>(DependencyObject source, ItemsControl list)
            where T : class
        {
            for (DependencyObject d = source; d != null && !ReferenceEquals(d, list); d = Parent(d))
            {
                if (d is FrameworkElement element && element.DataContext is T field)
                {
                    return field;
                }
            }

            return null;
        }

        private static DependencyObject Parent(DependencyObject d) =>
            d is Visual ? VisualTreeHelper.GetParent(d) : LogicalTreeHelper.GetParent(d);

        /// <summary>
        /// Scrolls the row of a field into view and puts the focus in its control, once the list has drawn the rows of the tab that
        /// was just shown.
        /// </summary>
        /// <typeparam name="T">The type of the field view models.</typeparam>
        /// <param name="list">The items control that lists the fields.</param>
        /// <param name="field">The field.</param>
        internal static void Focus<T>(ItemsControl list, T field)
            where T : class
        {
            list.Dispatcher.BeginInvoke(
                DispatcherPriority.Loaded,
                new Action(() =>
                {
                    list.UpdateLayout();

                    if (!(list.ItemContainerGenerator.ContainerFromItem(field) is FrameworkElement row))
                    {
                        return;
                    }

                    row.BringIntoView();
                    FrameworkElement control = FirstControl(row);
                    control?.Focus();
                })
            );
        }

        private static FrameworkElement FirstControl(DependencyObject parent)
        {
            for (int i = 0; i < VisualTreeHelper.GetChildrenCount(parent); i++)
            {
                DependencyObject child = VisualTreeHelper.GetChild(parent, i);

                if (
                    (child is TextBox || child is ComboBox || child is CheckBox || child is RadioButton)
                    && child is FrameworkElement element
                    && element.IsVisible
                    && element.IsEnabled
                )
                {
                    return element;
                }

                FrameworkElement found = FirstControl(child);

                if (found != null)
                {
                    return found;
                }
            }

            return null;
        }
    }
}
