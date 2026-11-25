using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Input;

namespace OpenCAD2D.Wpf
{
    /// <summary>
    /// The small modal window a sheet set publish to PDF shows while it runs: the app has no background plotting, so this
    /// window shows the run, sheet by sheet, and its Cancel stops it. Its logic is
    /// <see cref="SheetSetPublishProgressViewModel"/>.
    /// </summary>
    public partial class SheetSetPublishProgressWindow : Window
    {
        private bool _finished;

        /// <summary>Makes the window over its view model.</summary>
        /// <param name="model">The view model.</param>
        /// <exception cref="ArgumentNullException">Thrown when the model is null.</exception>
        public SheetSetPublishProgressWindow(SheetSetPublishProgressViewModel model)
        {
            Model = model ?? throw new ArgumentNullException(nameof(model));
            InitializeComponent();
            DataContext = model;
        }

        /// <summary>Gets the view model.</summary>
        public SheetSetPublishProgressViewModel Model { get; }

        /// <summary>Gets the window's content, so that a screenshot can draw it with no window shown.</summary>
        public FrameworkElement Body => Root;

        /// <summary>
        /// Publishes a sheet list to PDF with this window shown over an owner, as the publish runs on another thread so that the
        /// window shows each sheet and its Cancel is heard. The window closes when the publish is done.
        /// </summary>
        /// <param name="owner">The window it is shown over; null for none.</param>
        /// <param name="list">The sheet list and its options, of PDFs.</param>
        /// <param name="printers">The printers of Windows this computer has; null for none.</param>
        /// <returns>What was done; a cancel is told in its messages.</returns>
        /// <exception cref="ArgumentNullException">Thrown when the list is null.</exception>
        public static CadPublishResult Run(Window owner, CadPublishList list, IEnumerable<IPlotPrinter> printers)
        {
            if (list == null)
            {
                throw new ArgumentNullException(nameof(list));
            }

            var model = new SheetSetPublishProgressViewModel(
                list.SheetSetName ?? "Sheets",
                list.Options.MultiSheet ? list.MultiSheetFile() : list.OutputFolder()
            );
            var window = new SheetSetPublishProgressWindow(model) { Owner = owner };
            var progress = new Progress<CadPublishProgress>(model.Report);
            Task<CadPublishResult> run = null;

            // Started once the window is shown, so that it is there to close when a quick publish is done.
            window.Loaded += (sender, e) =>
            {
                run = Task.Run(() => CadPublisher.Publish(list, printers, progress, model.Token));
                run.ContinueWith(
                    done =>
                        window.Dispatcher.BeginInvoke(
                            new Action(() =>
                            {
                                window._finished = true;
                                window.Close();
                            })
                        ),
                    TaskScheduler.Default
                );
            };
            window.ShowDialog();
            return run.GetAwaiter().GetResult();
        }

        private void OnHeaderDown(object sender, MouseButtonEventArgs e)
        {
            if (e.ButtonState == MouseButtonState.Pressed && IsLoaded)
            {
                DragMove();
            }
        }

        /// <summary>A window closed before the publish is done asks it to stop, and closes when it has.</summary>
        protected override void OnClosing(System.ComponentModel.CancelEventArgs e)
        {
            if (!_finished)
            {
                e.Cancel = true;
                Model.Cancel();
            }

            base.OnClosing(e);
        }

        private void OnCancel(object sender, RoutedEventArgs e) => Model.Cancel();
    }
}
