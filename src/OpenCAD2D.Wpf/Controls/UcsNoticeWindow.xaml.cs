using System;
using System.Windows;
using System.Windows.Input;

namespace OpenCAD2D.Wpf
{
    /// <summary>The message of a name the UCS dialog refuses, as AutoCAD 2021 shows it: a title, a line in bold, the line under it and a Close button.</summary>
    public partial class UcsNoticeWindow : Window
    {
        /// <summary>Makes the message.</summary>
        /// <param name="error">What was refused.</param>
        /// <exception cref="ArgumentNullException">Thrown when the error is null.</exception>
        public UcsNoticeWindow(CadUcsDialogError error)
        {
            if (error == null)
            {
                throw new ArgumentNullException(nameof(error));
            }

            InitializeComponent();
            Title = error.Title;
            TitleText.Text = error.Title;
            MessageText.Text = error.Message;
            DetailText.Text = error.Detail;
            DetailText.Visibility = string.IsNullOrEmpty(error.Detail) ? Visibility.Collapsed : Visibility.Visible;
        }

        /// <summary>Gets the message's content, so that a screenshot can draw it with no window shown.</summary>
        public FrameworkElement Body => Root;

        /// <summary>Shows the message over a window; it ends when Close is pressed.</summary>
        /// <param name="owner">The window it is shown over; null for none.</param>
        /// <param name="error">What was refused.</param>
        public static void Show(Window owner, CadUcsDialogError error) =>
            new UcsNoticeWindow(error) { Owner = owner }.ShowDialog();

        private void OnClose(object sender, RoutedEventArgs e) => DialogResult = true;

        private void OnHeaderDown(object sender, MouseButtonEventArgs e)
        {
            if (e.ButtonState == MouseButtonState.Pressed && IsLoaded)
            {
                DragMove();
            }
        }
    }
}
