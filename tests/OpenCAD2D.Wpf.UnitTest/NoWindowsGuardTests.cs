using System;
using System.IO.Packaging;
using System.Linq;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Threading;
using System.Windows;
using System.Windows.Interop;
using Xunit;

namespace OpenCAD2D.Wpf.UnitTest
{
    /// <summary>The guard that makes a window shown on the screen fail a test (see <see cref="NoWindowsGuard"/>).</summary>
    public sealed class NoWindowsGuardTests
    {
        [Fact]
        public void AWindowShownOnTheScreen_FailsTheTest_BeforeItIsVisible()
        {
            OnSta(() =>
            {
                // Should the guard fail, what shows is a transparent window of one pixel, which is closed at once.
                var window = new Window
                {
                    Title = "Probe",
                    Width = 1,
                    Height = 1,
                    WindowStyle = WindowStyle.None,
                    AllowsTransparency = true,
                    Opacity = 0,
                    ShowInTaskbar = false,
                    ShowActivated = false,
                };

                try
                {
                    var ex = Assert.Throws<InvalidOperationException>(() => window.Show());

                    Assert.Contains("A test showed a real window", ex.Message);
                    Assert.False(IsWindowVisible(new WindowInteropHelper(window).Handle));
                }
                finally
                {
                    window.Close();
                }
            });
        }

        [Fact]
        public void AWindowOffTheScreen_IsShown_AsTheDclAndSecurityTestsShowTheirs()
        {
            OnSta(() =>
            {
                var window = new Window
                {
                    Left = NoWindowsGuard.OffScreen + 100,
                    Top = NoWindowsGuard.OffScreen + 100,
                    Width = 100,
                    Height = 100,
                    ShowInTaskbar = false,
                    ShowActivated = false,
                };

                try
                {
                    window.Show();
                    Assert.True(window.IsVisible);
                    Assert.True(NoWindowsGuard.IsAllowed(window));
                }
                finally
                {
                    window.Close();
                }
            });
        }

        [Fact]
        public void AModalDialogOnTheScreen_FailsTheTest_ButOneOverAnOwnerOffTheScreenIsShown()
        {
            OnSta(() =>
            {
                var owner = new Window
                {
                    Left = NoWindowsGuard.OffScreen + 100,
                    Top = NoWindowsGuard.OffScreen + 100,
                    Width = 100,
                    Height = 100,
                    ShowInTaskbar = false,
                    ShowActivated = false,
                };
                owner.Show();

                try
                {
                    var over = new Window
                    {
                        Owner = owner,
                        Width = 50,
                        Height = 50,
                        ShowInTaskbar = false,
                    };
                    over.Dispatcher.BeginInvoke(new Action(() => over.Close()));
                    over.ShowDialog();
                    Assert.True(NoWindowsGuard.IsAllowed(over));

                    // The same dialog over no owner would be on the screen: shown and left at the first layout, it throws.
                    var alone = new Window
                    {
                        Width = 1,
                        Height = 1,
                        WindowStyle = WindowStyle.None,
                        AllowsTransparency = true,
                        Opacity = 0,
                        ShowInTaskbar = false,
                    };
                    Assert.Throws<InvalidOperationException>(() => alone.ShowDialog());
                }
                finally
                {
                    owner.Close();
                }
            });
        }

        [Fact]
        public void AButtonOfTheSettingsTab_OnAModelHostedByAWorkspace_ThatWasNotLetGo_FailsTheTestInsteadOfShowingGlobalSettings()
        {
            OnWorkspace(
                (model, workspace) =>
                {
                    var tab = (SettingsRibbonViewModel)model.SettingsTab.TabContent;

                    var ex = Assert.Throws<InvalidOperationException>(() => tab.OpenCommand.Execute(null));

                    Assert.Contains("GlobalSettingsWindow", ex.Message);
                }
            );
        }

        [Fact]
        public void EveryButtonOfTheSettingsTab_OnAModelThatWasLetGo_ShowsNothing()
        {
            OnWorkspace(
                (model, workspace) =>
                {
                    RibbonTabHarness.LetGoOfTheWindow(model);
                    var tab = (SettingsRibbonViewModel)model.SettingsTab.TabContent;

                    foreach (
                        string page in new string[] { null }.Concat(
                            model.CreateGlobalSettings().Categories.Select(c => c.Name)
                        )
                    )
                    {
                        tab.OpenCommand.Execute(page);
                    }
                }
            );
        }

        [Fact]
        public void LettingGoOfTheWindow_TakesTheWorkspaceOffEveryRequestOfTheModel()
        {
            OnWorkspace(
                (model, workspace) =>
                {
                    string[] requests = typeof(MainViewModel)
                        .GetEvents(BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic)
                        .Select(e => e.Name)
                        .Where(n => n.EndsWith("Requested", StringComparison.Ordinal))
                        .OrderBy(n => n, StringComparer.Ordinal)
                        .ToArray();
                    Assert.Contains("SettingsRequested", requests);
                    Assert.Contains("DraftingSettingsRequested", requests);
                    Assert.Contains("OpenRequested", requests);

                    Func<string[]> held = () =>
                        typeof(MainViewModel)
                            .GetFields(BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic)
                            .Where(f =>
                                typeof(Delegate).IsAssignableFrom(f.FieldType)
                                && requests.Contains(f.Name.TrimStart('_'), StringComparer.OrdinalIgnoreCase)
                                && f.GetValue(model) != null
                            )
                            .Select(f => f.Name)
                            .OrderBy(n => n, StringComparer.Ordinal)
                            .ToArray();

                    // The workspace is subscribed to a good many of them, or this test proves nothing.
                    Assert.True(held().Length >= 8, string.Join(", ", held()));

                    RibbonTabHarness.LetGoOfTheWindow(model);

                    Assert.Empty(held());
                }
            );
        }

        // A drawing-less model hosted by a workspace on a thread of its own, its hooks as the workspace left them.
        private static void OnWorkspace(Action<MainViewModel, CadWorkspace> check)
        {
            OnSta(() =>
            {
                var model = new MainViewModel();
                var workspace = new CadWorkspace { DataContext = model };

                try
                {
                    check(model, workspace);
                }
                finally
                {
                    workspace.DrawingCanvas.Dispose();
                }
            });
        }

        [DllImport("user32.dll")]
        private static extern bool IsWindowVisible(IntPtr handle);

        private static void OnSta(Action test)
        {
            Exception failure = null;
            var thread = new Thread(() =>
            {
                try
                {
                    _ = PackUriHelper.UriSchemePack;
                    test();
                }
                catch (Exception e)
                {
                    failure = e;
                }
            });
            thread.SetApartmentState(ApartmentState.STA);
            thread.Start();
            thread.Join();
            Assert.Null(failure);
        }
    }
}
