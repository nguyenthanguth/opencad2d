using System;
using System.Windows;
using System.Windows.Input;

namespace OpenCAD2D.Wpf
{
    /// <summary>
    /// Plugin Settings, as the design draws it: 540 wide, the title <c>Plugin Settings – Circle marks</c>, a line under it saying
    /// what the plugin is and where, one row for each setting (its label, a box by kind, its description and range in grey, and in
    /// red the words of the setting where the value is refused), and at the foot OK, greyed while a row is refused, and Cancel.
    /// Its logic is <see cref="PluginSettingsViewModel"/>, which the window lets go of when it closes. The window closed, or Esc,
    /// is Cancel.
    /// </summary>
    public partial class PluginSettingsWindow : Window
    {
        /// <summary>Makes the dialog over its view model.</summary>
        /// <param name="model">The view model.</param>
        /// <exception cref="ArgumentNullException">Thrown when the model is null.</exception>
        public PluginSettingsWindow(PluginSettingsViewModel model)
        {
            Model = model ?? throw new ArgumentNullException(nameof(model));
            InitializeComponent();
            DataContext = model;
            Closed += (s, e) => Model.Dispose();
        }

        /// <summary>Gets the view model.</summary>
        public PluginSettingsViewModel Model { get; }

        /// <summary>Gets the dialog's content, so that a screenshot can draw it with no window shown.</summary>
        public FrameworkElement Body => Root;

        /// <summary>Shows the dialog over a window, which waits until it is closed.</summary>
        /// <param name="owner">The window it is shown over; null for none.</param>
        /// <param name="model">The view model.</param>
        /// <returns>True when OK applied the values.</returns>
        public static bool Show(Window owner, PluginSettingsViewModel model) =>
            new PluginSettingsWindow(model) { Owner = owner }.ShowDialog() == true;

        private void OnHeaderDown(object sender, MouseButtonEventArgs e)
        {
            if (e.ButtonState == MouseButtonState.Pressed && IsLoaded)
            {
                DragMove();
            }
        }

        private void OnOk(object sender, RoutedEventArgs e)
        {
            if (Model.Apply())
            {
                DialogResult = true;
            }
        }

        private void OnCancel(object sender, RoutedEventArgs e) => DialogResult = false;
    }
}
