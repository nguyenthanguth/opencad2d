using System;
using System.Windows;
using System.Windows.Input;

namespace OpenCAD2D.Wpf
{
    /// <summary>
    /// AutoCAD's Orthographic UCS depth dialog: the depth of a face as a number, OK and Cancel, and a button that picks a new
    /// origin in the drawing instead. Its logic is <see cref="UcsDepthViewModel"/>.
    /// </summary>
    public partial class UcsDepthWindow : Window
    {
        /// <summary>Makes the dialog over its view model.</summary>
        /// <param name="model">The view model.</param>
        /// <exception cref="ArgumentNullException">Thrown when the model is null.</exception>
        public UcsDepthWindow(UcsDepthViewModel model)
        {
            Model = model ?? throw new ArgumentNullException(nameof(model));
            InitializeComponent();
            DataContext = model;
            Loaded += (sender, e) =>
            {
                DepthBox.Focus();
                DepthBox.SelectAll();
            };
        }

        /// <summary>Gets the view model.</summary>
        public UcsDepthViewModel Model { get; }

        /// <summary>Gets the dialog's content, so that a screenshot can draw it with no window shown.</summary>
        public FrameworkElement Body => Root;

        /// <summary>Shows the dialog over a window.</summary>
        /// <param name="owner">The window it is shown over; null for none.</param>
        /// <param name="model">The view model.</param>
        /// <returns>True where OK gave the face a depth; false for Cancel, or where a new origin is to be picked.</returns>
        public static bool Ask(Window owner, UcsDepthViewModel model) =>
            new UcsDepthWindow(model) { Owner = owner }.ShowDialog() == true;

        private void OnHeaderDown(object sender, MouseButtonEventArgs e)
        {
            if (e.ButtonState == MouseButtonState.Pressed && IsLoaded)
            {
                DragMove();
            }
        }

        private void OnOk(object sender, RoutedEventArgs e)
        {
            if (Model.Accept())
            {
                DialogResult = true;
            }
        }

        private void OnCancel(object sender, RoutedEventArgs e) => DialogResult = false;

        private void OnPick(object sender, RoutedEventArgs e)
        {
            Model.Pick();
            DialogResult = false;
        }
    }
}
