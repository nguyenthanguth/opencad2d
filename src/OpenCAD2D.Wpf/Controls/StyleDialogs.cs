using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;

namespace OpenCAD2D.Wpf
{
    /// <summary>
    /// Shows the style dialogs over a window: the Text Style dialog (STYLE), the Dimension Style Manager (DIMSTYLE), the Table
    /// Style dialog (TABLESTYLE) and the Multileader Style Manager (MLEADERSTYLE).
    /// </summary>
    public static class StyleDialogs
    {
        /// <summary>Whether a mouse event came from a row of the list it was raised on, and not from the empty area under the rows or the scroll bar.</summary>
        /// <param name="sender">The list the event was raised on.</param>
        /// <param name="e">The mouse event.</param>
        /// <returns>True when the event's source is inside a row.</returns>
        internal static bool IsOnRow(object sender, MouseEventArgs e) =>
            sender is ItemsControl list
            && e.OriginalSource is DependencyObject source
            && ItemsControl.ContainerFromElement(list, source) != null;

        /// <summary>Shows a style dialog over the drawing shown; nothing with no drawing.</summary>
        /// <param name="owner">The window it is shown over.</param>
        /// <param name="model">The window's view model.</param>
        /// <param name="name">STYLE for the Text Style dialog, DIMSTYLE for the Dimension Style Manager, TABLESTYLE for the Table Style dialog, MLEADERSTYLE for the Multileader Style Manager.</param>
        public static void Show(Window owner, MainViewModel model, string name)
        {
            if (model == null)
            {
                return;
            }

            if (name == "STYLE")
            {
                ShowTextStyle(owner, model);
                return;
            }

            if (name == "TABLESTYLE")
            {
                ShowTableStyle(owner, model);
                return;
            }

            if (name == "MLEADERSTYLE")
            {
                MLeaderStyleManagerViewModel styles = model.CreateMLeaderStyleManager();

                if (styles != null)
                {
                    new MLeaderStyleManagerWindow(styles, () => ShowTextStyle(owner, model))
                    {
                        Owner = owner,
                    }.ShowDialog();
                }

                return;
            }

            DimStyleManagerViewModel manager = model.CreateDimStyleManager();

            if (manager != null)
            {
                new DimStyleManagerWindow(manager, () => ShowTextStyle(owner, model)) { Owner = owner }.ShowDialog();
            }
        }

        /// <summary>Shows the Table Style dialog (TABLESTYLE) over a window, as the table button of the Insert Table dialog does too.</summary>
        /// <param name="owner">The window it is shown over.</param>
        /// <param name="model">The window's view model.</param>
        public static void ShowTableStyle(Window owner, MainViewModel model)
        {
            TableStyleManagerViewModel manager = model?.CreateTableStyleManager();

            if (manager != null)
            {
                new TableStyleWindow(manager) { Owner = owner }.ShowDialog();
            }
        }

        private static void ShowTextStyle(Window owner, MainViewModel model)
        {
            TextStyleViewModel style = model.CreateTextStyle();

            if (style != null)
            {
                // Over the dialog in front, as the "..." of the Text tab shows it over the dimension style's dialog.
                Window active = Application.Current?.Windows.OfType<Window>().FirstOrDefault(w => w.IsActive);
                new TextStyleWindow(style) { Owner = active ?? owner }.ShowDialog();
            }
        }
    }
}
