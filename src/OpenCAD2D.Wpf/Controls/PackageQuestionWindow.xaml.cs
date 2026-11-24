using System;
using System.Windows;
using System.Windows.Input;

namespace OpenCAD2D.Wpf
{
    /// <summary>
    /// The question put before a tool's packages are downloaded or installed, as the PackageQuestion board draws it: 560 by
    /// 440, a title, one sentence naming the tool and its file, a table of the packages (Package, Version and a third column the
    /// caller names), the sentence that says what loading runs, and at the bottom where the packages come from with the two
    /// buttons. It asks about the NuGet packages of a C# plugin (Download and load, Don't load); the texts all come from
    /// <see cref="PackageQuestionViewModel"/>, so that a Python tool's pip packages can use the same window. The window closed,
    /// or Esc, is No.
    /// </summary>
    public partial class PackageQuestionWindow : Window
    {
        /// <summary>Makes the question.</summary>
        /// <param name="model">What it says.</param>
        /// <exception cref="ArgumentNullException">Thrown when the model is null.</exception>
        public PackageQuestionWindow(PackageQuestionViewModel model)
        {
            Model = model ?? throw new ArgumentNullException(nameof(model));
            InitializeComponent();
            DataContext = model;
        }

        /// <summary>Gets what the question says.</summary>
        public PackageQuestionViewModel Model { get; }

        /// <summary>Gets the question's content, so that a screenshot can draw it with no window shown.</summary>
        public FrameworkElement Body => Root;

        /// <summary>Gets whether the answer was Yes; false for No, for the window closed and for Esc.</summary>
        public bool Answer { get; private set; }

        /// <summary>Asks the question over a window and waits for the answer.</summary>
        /// <param name="owner">The window it is shown over; null, or a window not shown, for the middle of the screen.</param>
        /// <param name="model">What it says.</param>
        /// <returns>True for Yes; false for No, for the window closed and for Esc.</returns>
        public static bool Ask(Window owner, PackageQuestionViewModel model)
        {
            var question = new PackageQuestionWindow(model);

            if (owner != null && owner.IsVisible)
            {
                question.Owner = owner;
            }
            else
            {
                question.WindowStartupLocation = WindowStartupLocation.CenterScreen;
            }

            question.ShowDialog();
            return question.Answer;
        }

        private void OnHeaderDown(object sender, MouseButtonEventArgs e)
        {
            if (e.ButtonState == MouseButtonState.Pressed && IsLoaded)
            {
                DragMove();
            }
        }

        private void OnYes(object sender, RoutedEventArgs e)
        {
            Answer = true;
            DialogResult = true;
        }

        private void OnNo(object sender, RoutedEventArgs e)
        {
            Answer = false;
            DialogResult = false;
        }
    }
}
