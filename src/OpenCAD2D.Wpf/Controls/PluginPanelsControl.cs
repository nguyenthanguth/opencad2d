using System.Collections.Generic;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;

namespace OpenCAD2D.Wpf
{
    /// <summary>
    /// The panels a plugin's ribbon tab or the Plugins tab shows, each a <see cref="RibbonPanel"/> in a
    /// <see cref="RibbonStrip"/>, so that they shrink as the built-in panels do: its items are
    /// <see cref="PluginPanelViewModel"/>s, and the container made for each has the panel's title, its columns as its body, its
    /// slide-out and the title that opens it, its launcher and the icon of its collapsed button.
    /// </summary>
    internal sealed class PluginPanelsControl : ItemsControl
    {
        static PluginPanelsControl()
        {
            FocusableProperty.OverrideMetadata(typeof(PluginPanelsControl), new FrameworkPropertyMetadata(false));
        }

        /// <inheritdoc/>
        protected override bool IsItemItsOwnContainerOverride(object item) => item is RibbonPanel;

        /// <inheritdoc/>
        protected override DependencyObject GetContainerForItemOverride() => new RibbonPanel();

        /// <inheritdoc/>
        protected override void PrepareContainerForItemOverride(DependencyObject element, object item)
        {
            base.PrepareContainerForItemOverride(element, item);

            if (!(element is RibbonPanel panel) || !(item is PluginPanelViewModel view))
            {
                return;
            }

            panel.Title = view.Title;
            panel.Icon = view.IconGeometry;
            panel.IconImage = view.IconImage;
            panel.Body = Columns(view.Columns);

            if (view.HasSlideOut)
            {
                panel.SlideOut = Columns(view.SlideOutColumns);
                panel.Header = new ToggleButton
                {
                    Style = TryFindResource("SlideOutTitle") as Style,
                    Content = view.Title + " ▾",
                    ToolTip = "More of " + view.Title,
                };
            }

            if (view.Launcher != null)
            {
                panel.Launcher = view.Launcher.RunCommand;
                panel.LauncherToolTip = new ToolTip
                {
                    Style = TryFindResource("PluginToolTip") as Style,
                    Content = view.Launcher.ToolTipInfo,
                };
            }
        }

        /// <inheritdoc/>
        protected override void ClearContainerForItemOverride(DependencyObject element, object item)
        {
            if (element is RibbonPanel panel)
            {
                panel.Body = null;
                panel.SlideOut = null;
                panel.Header = null;
                panel.Launcher = null;
                panel.LauncherToolTip = null;
            }

            base.ClearContainerForItemOverride(element, item);
        }

        /// <summary>The columns of a panel side by side, each as the window's template for a <see cref="PluginColumnViewModel"/> draws it.</summary>
        private static ItemsControl Columns(IReadOnlyList<PluginColumnViewModel> columns)
        {
            var row = new FrameworkElementFactory(typeof(StackPanel));
            row.SetValue(StackPanel.OrientationProperty, Orientation.Horizontal);
            return new ItemsControl
            {
                ItemsSource = columns,
                Focusable = false,
                ItemsPanel = new ItemsPanelTemplate(row),
            };
        }
    }
}
