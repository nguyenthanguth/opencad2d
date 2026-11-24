using System;
using System.Runtime.CompilerServices;
using System.Windows;

namespace OpenCAD2D.Wpf.UnitTest
{
    /// <summary>
    /// Makes a test that shows a window on the screen fail instead of showing it. A view model asks for a dialog through an
    /// event or a delegate the workspace hooks up (Global settings, Drafting Settings, the style dialogs, every Show and Ask
    /// hook), so a test that hosts a model in a <see cref="CadWorkspace"/> and presses a button opens the real window unless it
    /// let every hook go first; one that forgot, or missed a hook added since, put windows on the user's screen one after
    /// another. WPF lays a window out for the first time inside Show and ShowDialog, once its HwndSource exists and before it
    /// has been shown to the system, and tells it so by SizeChanged: the guard throws there, so the call fails and nothing
    /// appears. A window is allowed only where the tests put one on purpose: itself or the window that owns it at
    /// <see cref="OffScreen"/> or further, which is how the DCL and security tests show theirs. A window laid out by hand, with
    /// no HwndSource, is not shown and is let be.
    /// </summary>
    internal static class NoWindowsGuard
    {
        /// <summary>The Left of a window the tests show off the screen, as DclDialogWindowTests and SecurityKit place theirs.</summary>
        public const double OffScreen = 30000;

        [ModuleInitializer]
        internal static void Install() =>
            EventManager.RegisterClassHandler(
                typeof(Window),
                FrameworkElement.SizeChangedEvent,
                new SizeChangedEventHandler(OnSizeChanged),
                true
            );

        /// <summary>Whether the guard lets this window show: it, or a window that owns it, is off the screen.</summary>
        internal static bool IsAllowed(Window window)
        {
            for (Window w = window; w != null; w = w.Owner)
            {
                if (w.Left >= OffScreen)
                {
                    return true;
                }
            }

            return false;
        }

        private static void OnSizeChanged(object sender, SizeChangedEventArgs e)
        {
            var window = sender as Window;

            if (window == null || PresentationSource.FromVisual(window) == null || IsAllowed(window))
            {
                return;
            }

            throw new InvalidOperationException(
                "A test showed a real window ("
                    + window.GetType().Name
                    + ", \""
                    + window.Title
                    + "\") on the screen. A test never shows one: let go of the hooks of the window "
                    + "(RibbonTabHarness.LetGoOfTheWindow), or place the window at Left "
                    + OffScreen
                    + " or more."
            );
        }
    }
}
