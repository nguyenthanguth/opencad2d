using System;
using System.Windows;
using System.Windows.Input;

namespace OpenCAD2D.Wpf
{
    /// <summary>AutoCAD's Rename &amp; Renumber View dialog, from the Sheet Views tab of the Sheet Set Manager; its logic is <see cref="RenameRenumberViewDialogViewModel"/>.</summary>
    public partial class RenameRenumberViewWindow : Window
    {
        /// <summary>Makes the dialog over its view model.</summary>
        /// <param name="model">The view model.</param>
        /// <exception cref="ArgumentNullException">Thrown when the model is null.</exception>
        public RenameRenumberViewWindow(RenameRenumberViewDialogViewModel model)
        {
            Model = model ?? throw new ArgumentNullException(nameof(model));
            InitializeComponent();
            DataContext = model;
            Loaded += (s, e) =>
            {
                NumberBox.Focus();
                NumberBox.SelectAll();
            };
        }

        /// <summary>Gets the view model.</summary>
        public RenameRenumberViewDialogViewModel Model { get; }

        /// <summary>Gets the dialog's content, so that a screenshot can draw it with no window shown.</summary>
        public FrameworkElement Body => Root;

        /// <summary>Shows the dialog over a window and says whether OK was pressed and the change was made.</summary>
        /// <param name="owner">The window it is shown over; null for none.</param>
        /// <param name="model">The view model.</param>
        /// <returns>True when OK was pressed and the set took the change.</returns>
        public static bool Show(Window owner, RenameRenumberViewDialogViewModel model) =>
            new RenameRenumberViewWindow(model) { Owner = owner }.ShowDialog() == true;

        private void OnHeaderDown(object sender, MouseButtonEventArgs e)
        {
            if (e.ButtonState == MouseButtonState.Pressed && IsLoaded)
            {
                DragMove();
            }
        }

        private void OnHelp(object sender, RoutedEventArgs e) => PlotHelp.Open(Model.HelpUrl);

        private void OnPrevious(object sender, RoutedEventArgs e) => Model.Previous();

        private void OnNext(object sender, RoutedEventArgs e) => Model.Next();

        private void OnOk(object sender, RoutedEventArgs e)
        {
            if (Model.Accept())
            {
                DialogResult = true;
            }
        }

        private void OnCancel(object sender, RoutedEventArgs e) => DialogResult = false;
    }
}
