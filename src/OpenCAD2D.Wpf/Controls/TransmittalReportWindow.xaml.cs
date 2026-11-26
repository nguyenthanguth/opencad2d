using System;
using System.Windows;
using System.Windows.Input;

namespace OpenCAD2D.Wpf
{
    /// <summary>Shows the text of a transmittal report, read only, from View Report… of Create Transmittal.</summary>
    public partial class TransmittalReportWindow : Window
    {
        /// <summary>Makes the window over a report.</summary>
        /// <param name="title">The title of the dialog it was asked from, which it carries.</param>
        /// <param name="report">The text of the report.</param>
        /// <exception cref="ArgumentNullException">Thrown when the report is null.</exception>
        public TransmittalReportWindow(string title, string report)
        {
            if (report == null)
            {
                throw new ArgumentNullException(nameof(report));
            }

            InitializeComponent();
            Heading.Text = string.IsNullOrEmpty(title) ? "Transmittal Report" : "Transmittal Report - " + title;
            Text.Text = report;
        }

        /// <summary>Gets the window's content, so that a screenshot can draw it with no window shown.</summary>
        public FrameworkElement Body => Root;

        /// <summary>Shows the report over a window.</summary>
        /// <param name="owner">The window it is shown over; null for none.</param>
        /// <param name="title">The title of the dialog it was asked from.</param>
        /// <param name="report">The text of the report.</param>
        public static void Show(Window owner, string title, string report) =>
            new TransmittalReportWindow(title, report) { Owner = owner }.ShowDialog();

        private void OnHeaderDown(object sender, MouseButtonEventArgs e)
        {
            if (e.ButtonState == MouseButtonState.Pressed && IsLoaded)
            {
                DragMove();
            }
        }

        private void OnClose(object sender, RoutedEventArgs e) => DialogResult = true;
    }
}
