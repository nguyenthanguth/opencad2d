using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Threading;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using OpenCAD2D.Plugins;
using OpenCAD2D.Wpf.Plugins;
using Xunit;

namespace OpenCAD2D.Wpf.UnitTest
{
    /// <summary>
    /// Load/Unload Applications and a tool's packages (TF6): Load folder…, the question before the packages of a C# plugin
    /// are downloaded as it reaches the window (a plugin that was not loaded because it was answered No stays in the table with
    /// the badge "needs packages", a restore that failed has "install failed" and a red line), and the file types Load… offers.
    /// The loader is a fake whose Compile says what the compiler of the .NET 8 app says; no window is shown.
    /// </summary>
    public sealed class AppLoadPackagesTests : IDisposable
    {
        private const string SampleName = "OpenCAD2D.SamplePlugin";
        private readonly TestFolder _folder = new TestFolder();
        private readonly List<IDisposable> _disposables = new List<IDisposable>();

        public void Dispose()
        {
            foreach (IDisposable item in _disposables)
            {
                item.Dispose();
            }

            _folder.Dispose();
        }

        private string SettingsFile => Path.Combine(_folder.FullPath, "settings.json");

        /// <summary>How the fake compiler answers the plugin's packages.</summary>
        private enum Packages
        {
            /// <summary>The question is asked and answered Yes: the plugin is built.</summary>
            Yes,

            /// <summary>The question is asked and answered No.</summary>
            No,

            /// <summary>nuget.org cannot be reached.</summary>
            Offline,

            /// <summary>The restore refuses the project: a package is not found.</summary>
            Missing,

            /// <summary>The project has no packages: nothing is asked.</summary>
            None,
        }

        /// <summary>
        /// A loader that compiles as the app on .NET 8 does where the plugin names packages, without the compiler and without
        /// the network: what it says for a plugin is what <see cref="Answer"/> is, and each question is counted in
        /// <see cref="Asked"/>. The plugin built is the sample plugin's .dll.
        /// </summary>
        private sealed class PackageLoader : CadPluginLoader
        {
            private readonly CadPluginLoader _inner = new CadPluginAssemblyLoader();
            private readonly string _dll;
            private readonly string _pdb;

            public PackageLoader(string dll, string pdb)
            {
                _dll = dll;
                _pdb = pdb;
            }

            public Packages Answer { get; set; }

            public List<string> Asked { get; } = new List<string>();

            public override bool CanUnload => _inner.CanUnload;

            public override bool CanCompile => true;

            public override IReadOnlyList<Assembly> Load(
                string name,
                IReadOnlyList<string> files,
                string probeFolder
            ) => _inner.Load(name, files, probeFolder);

            public override void Unload(IReadOnlyList<Assembly> assemblies) => _inner.Unload(assemblies);

            public override CadPluginBuild Compile(string name, string projectPath)
            {
                if (Answer != Packages.None)
                {
                    Asked.Add(name);
                }

                switch (Answer)
                {
                    case Packages.No:
                        throw new CadPluginException(name, name + " was not loaded: its packages were not downloaded.");
                    case Packages.Offline:
                        throw new CadPluginException(
                            name,
                            name
                                + " was not loaded: nuget.org could not be reached (No such host is known). Connect and run PLUGINRELOAD "
                                + name
                                + "."
                        );
                    case Packages.Missing:
                        return new CadPluginBuild(
                            2,
                            new[]
                            {
                                "Stairs.csproj(7,6): error NU1101: Unable to find package Foo. No packages exist with this id in source(s): nuget.org",
                                "Stairs.csproj(8,6): error NU1101: Unable to find package Bar. No packages exist with this id in source(s): nuget.org",
                            }
                        );
                    default:
                        return new CadPluginBuild(_dll, _pdb, false, Array.Empty<string>());
                }
            }
        }

        private PackageLoader _loader;

        /// <summary>A drawing open with a manager over <see cref="PackageLoader"/>, and Stairs\Stairs.csproj written in a folder it trusts.</summary>
        private MainViewModel WithTool(Packages answer, out string stairs)
        {
            string built = Path.Combine(_folder.FullPath, "built");
            Directory.CreateDirectory(built);
            string dll = Path.Combine(built, "Stairs.dll");
            File.Copy(Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "SamplePlugin", SampleName + ".dll"), dll);
            File.WriteAllText(Path.ChangeExtension(dll, ".pdb"), string.Empty);
            _loader = new PackageLoader(dll, Path.ChangeExtension(dll, ".pdb")) { Answer = answer };
            var manager = new CadPluginManager(
                _loader,
                Path.Combine(_folder.FullPath, "plugins"),
                Path.Combine(_folder.FullPath, "plugins.json"),
                Path.Combine(_folder.FullPath, "copies")
            );
            _disposables.Add(manager);
            stairs = Path.Combine(_folder.FullPath, "dev", "Stairs", "Stairs.csproj");
            Directory.CreateDirectory(Path.GetDirectoryName(stairs));
            File.WriteAllText(stairs, "// Stairs");
            manager.AddTrustedFolder(Path.Combine(_folder.FullPath, "dev"));
            var model = new MainViewModel { SettingsPath = SettingsFile };
            model.UsePlugins(manager, loadAtStart: false);
            Assert.True(model.Open(_folder.Write("room.dxf", TestFiles.Room(out _, out _))));
            model.LoadSecurity.SecureLoad = 0;
            return model;
        }

        private AppLoadViewModel Created(MainViewModel model)
        {
            var dialog = new AppLoadViewModel(model);
            _disposables.Add(dialog);
            return dialog;
        }

        private static AppLoadRow Row(AppLoadViewModel dialog, string name) =>
            Assert.Single(dialog.Rows, r => r.Name == name);

        private const string Declined = "Stairs was not loaded: its packages were not downloaded.";

        // ---- The file types

        [Fact]
        public void TheOpenDialogOffersTheCsprojOfAPlugin_AndNoLongerALoneCs()
        {
            Assert.Equal(
                "Applications (*.lsp;requirements.txt;*.dll;*.csproj)|*.lsp;requirements.txt;*.dll;*.csproj|Lisp files (*.lsp)|*.lsp|Python tools (requirements.txt)|requirements.txt|Plugins (*.dll)|*.dll|C# plugin (*.csproj)|*.csproj|All files (*.*)|*.*",
                AppLoadViewModel.OpenFilter
            );
            Assert.DoesNotContain("*.cs)", AppLoadViewModel.OpenFilter);
            Assert.DoesNotContain("*.cs;", AppLoadViewModel.OpenFilter);
        }

        // ---- Load folder

        [Fact]
        public void LoadFolder_LoadsAFolderWithACsprojAsACSharpPlugin_AndKeepsItsParentForTheNextLoad()
        {
            MainViewModel model = WithTool(Packages.None, out string stairs);
            AppLoadViewModel dialog = Created(model);
            string folder = Path.GetDirectoryName(stairs);
            string asked = null;
            dialog.AskFolder = start =>
            {
                asked = start;
                return folder;
            };

            dialog.LoadFolderCommand.Execute(null);

            AppLoadRow row = Row(dialog, "Stairs");
            Assert.Equal("C# source", row.Kind);
            Assert.True(row.IsOn);
            Assert.Equal(folder, row.File);
            Assert.Equal(AppLoadBadge.None, row.Badge);
            Assert.False(dialog.HasLoadError);
            Assert.Equal(string.Empty, asked);
            Assert.Equal(Path.GetDirectoryName(folder), model.Settings.AppLoadFolder);
        }

        [Fact]
        public void LoadFolderCancelled_OrNotAsked_LoadsNothing()
        {
            MainViewModel model = WithTool(Packages.None, out _);
            AppLoadViewModel dialog = Created(model);
            int before = model.History.Count;

            dialog.LoadFolderCommand.Execute(null);
            dialog.AskFolder = start => null;
            dialog.LoadFolderCommand.Execute(null);

            Assert.Equal(before, model.History.Count);
            Assert.Empty(dialog.Rows);
        }

        [Fact]
        public void LoadFolder_LoadsAFolderWithARequirementsTxt_AsAPythonTool_AndAsksNothingOfTheNuGetSource()
        {
            MainViewModel model = WithTool(Packages.None, out _);
            var python = new FakePythonFiles();
            model.PythonFiles = python;
            AppLoadViewModel dialog = Created(model);
            string survey = Path.Combine(_folder.FullPath, "dev", "Survey");
            Directory.CreateDirectory(survey);
            File.WriteAllText(Path.Combine(survey, "requirements.txt"), "numpy>=1.26\n");
            File.WriteAllText(Path.Combine(survey, "__init__.py"), "x = 1\n");

            dialog.LoadFolder(survey);

            Assert.Equal(new[] { survey }, python.Loads);
            Assert.False(dialog.HasLoadError);
            AppLoadRow row = Assert.Single(dialog.Rows);
            Assert.Equal("Python", row.Kind);
            Assert.Equal("Survey", row.Name);
            Assert.Empty(_loader.Asked);
        }

        [Fact]
        public void LoadFolder_OfAFolderThatIsNoPlugin_GetsTheManagersLine_AndNoRow()
        {
            MainViewModel model = WithTool(Packages.None, out _);
            AppLoadViewModel dialog = Created(model);
            string empty = Path.Combine(_folder.FullPath, "dev", "Empty");
            Directory.CreateDirectory(empty);

            dialog.LoadFolder(empty);

            Assert.True(dialog.HasLoadError);
            Assert.Equal("Empty", dialog.LoadErrorName);
            Assert.Empty(dialog.Rows);
        }

        // ---- The question answered No: needs packages

        [Fact]
        public void AnsweredNo_ThePluginStaysUnloaded_WithTheLineOfTheSpec_AndARowThatNeedsPackages()
        {
            MainViewModel model = WithTool(Packages.No, out string stairs);
            AppLoadViewModel dialog = Created(model);

            dialog.Load(new[] { stairs });

            Assert.Contains(Declined, model.History);
            Assert.Null(model.Plugins.Find("Stairs"));
            AppLoadRow row = Row(dialog, "Stairs");
            Assert.False(row.IsOn);
            Assert.Equal("C# source", row.Kind);
            Assert.Equal(Path.GetDirectoryName(stairs), row.File);
            Assert.Equal(AppLoadBadge.NeedsPackages, row.Badge);
            Assert.Equal("needs packages", row.BadgeText);
            Assert.True(row.HasBadge);

            // The user's own answer is not a failure: no red line.
            Assert.False(dialog.HasLoadError);
        }

        [Fact]
        public void ARowThatNeedsPackages_StaysInTheTable_WithItsBadge_InAWindowOpenedLater()
        {
            MainViewModel model = WithTool(Packages.No, out string stairs);
            Created(model).LoadFolder(Path.GetDirectoryName(stairs));

            AppLoadViewModel later = Created(model);

            Assert.Equal(AppLoadBadge.NeedsPackages, Row(later, "Stairs").Badge);
            Assert.True(model.Settings.KnownApplications.ContainsKey(Path.GetDirectoryName(stairs)));
        }

        [Fact]
        public void TickingARowThatNeedsPackages_AsksAgain_AndYesLoadsIt()
        {
            MainViewModel model = WithTool(Packages.No, out string stairs);
            AppLoadViewModel dialog = Created(model);
            dialog.Load(new[] { stairs });
            _loader.Answer = Packages.Yes;

            Row(dialog, "Stairs").IsOn = true;

            Assert.Equal(new[] { "Stairs", "Stairs" }, _loader.Asked);
            AppLoadRow row = Row(dialog, "Stairs");
            Assert.True(row.IsOn);
            Assert.Equal(AppLoadBadge.None, row.Badge);
            Assert.NotNull(model.Plugins.Find("Stairs"));
            Assert.False(dialog.HasLoadError);
        }

        [Fact]
        public void TickingARowThatNeedsPackages_AnsweredNoAgain_KeepsItUntickedWithItsBadge()
        {
            MainViewModel model = WithTool(Packages.No, out string stairs);
            AppLoadViewModel dialog = Created(model);
            dialog.Load(new[] { stairs });

            Row(dialog, "Stairs").IsOn = true;

            Assert.Equal(2, _loader.Asked.Count);
            Assert.False(Row(dialog, "Stairs").IsOn);
            Assert.Equal(AppLoadBadge.NeedsPackages, Row(dialog, "Stairs").Badge);
            Assert.Equal(2, model.History.Count(l => l == Declined));
        }

        [Fact]
        public void AnsweredYes_LoadsThePlugin_WithNoBadge()
        {
            MainViewModel model = WithTool(Packages.Yes, out string stairs);
            AppLoadViewModel dialog = Created(model);

            dialog.Load(new[] { stairs });

            AppLoadRow row = Row(dialog, "Stairs");
            Assert.True(row.IsOn);
            Assert.Equal(AppLoadBadge.None, row.Badge);
            Assert.False(row.HasBadge);
            Assert.Equal(string.Empty, row.BadgeText);
            Assert.False(dialog.HasLoadError);
        }

        [Fact]
        public void RemoveOfARowThatNeedsPackages_TakesItOutOfTheList_AndOutOfTheKnownApplications()
        {
            MainViewModel model = WithTool(Packages.No, out string stairs);
            AppLoadViewModel dialog = Created(model);
            dialog.Load(new[] { stairs });
            dialog.ChooseRows(new[] { Row(dialog, "Stairs") });

            dialog.RemoveCommand.Execute(null);

            Assert.Empty(dialog.Rows);
            Assert.False(model.Settings.KnownApplications.ContainsKey(Path.GetDirectoryName(stairs)));
            Assert.Empty(Created(model).Rows);
        }

        // ---- The restore that failed: install failed

        [Fact]
        public void ARestoreThatCannotReachNuGet_GivesTheBadgeInstallFailed_AndARedLineWithTheReason()
        {
            MainViewModel model = WithTool(Packages.Offline, out string stairs);
            AppLoadViewModel dialog = Created(model);

            dialog.LoadFolder(Path.GetDirectoryName(stairs));

            string reason =
                "nuget.org could not be reached (No such host is known). Connect and run PLUGINRELOAD Stairs.";
            AppLoadRow row = Row(dialog, "Stairs");
            Assert.False(row.IsOn);
            Assert.Equal(AppLoadBadge.InstallFailed, row.Badge);
            Assert.Equal("install failed", row.BadgeText);
            Assert.True(dialog.HasLoadError);
            Assert.Equal("Stairs", dialog.LoadErrorName);
            Assert.Equal(" was not installed: " + reason, dialog.LoadErrorText);
            Assert.Equal("Every line is on the command line", dialog.LoadErrorHint);
            Assert.Contains("Stairs was not loaded: " + reason, model.History);
        }

        [Fact]
        public void ARestoreThatRefusesAPackage_ShowsTheFirstErrorOnTheRedLine_AndEveryErrorInItsTooltip()
        {
            MainViewModel model = WithTool(Packages.Missing, out string stairs);
            AppLoadViewModel dialog = Created(model);

            dialog.LoadFolder(Path.GetDirectoryName(stairs));

            Assert.Equal(AppLoadBadge.InstallFailed, Row(dialog, "Stairs").Badge);
            Assert.Equal("Stairs", dialog.LoadErrorName);
            Assert.Equal(
                " was not installed: Stairs.csproj(7,6): error NU1101: Unable to find package Foo. No packages exist with this id in source(s): nuget.org",
                dialog.LoadErrorText
            );
            Assert.Equal(
                "Plugin Stairs was not loaded: 2 errors\n"
                    + "Stairs.csproj(7,6): error NU1101: Unable to find package Foo. No packages exist with this id in source(s): nuget.org\n"
                    + "Stairs.csproj(8,6): error NU1101: Unable to find package Bar. No packages exist with this id in source(s): nuget.org",
                dialog.LoadError
            );
            Assert.Equal("Every line is on the command line", dialog.LoadErrorHint);
        }

        [Fact]
        public void AnInstallThatFailedAndThenWorks_LosesItsBadgeAndItsRedLine()
        {
            MainViewModel model = WithTool(Packages.Offline, out string stairs);
            AppLoadViewModel dialog = Created(model);
            dialog.LoadFolder(Path.GetDirectoryName(stairs));
            _loader.Answer = Packages.Yes;

            Row(dialog, "Stairs").IsOn = true;

            AppLoadRow row = Row(dialog, "Stairs");
            Assert.True(row.IsOn);
            Assert.Equal(AppLoadBadge.None, row.Badge);
            Assert.False(dialog.HasLoadError);
        }

        [Fact]
        public void ABuildThatFailsOtherwise_IsNotAPackageProblem_AndHasNoBadge()
        {
            MainViewModel model = WithTool(Packages.None, out string stairs);
            AppLoadViewModel dialog = Created(model);

            // No .csproj of its own: the manager refuses the folder before anything is compiled.
            string bare = Path.Combine(_folder.FullPath, "dev", "Bare");
            Directory.CreateDirectory(bare);
            File.WriteAllText(Path.Combine(bare, "Bare.cs"), "class X {}");
            dialog.LoadFolder(bare);

            Assert.True(dialog.HasLoadError);
            Assert.DoesNotContain(dialog.Rows, r => r.HasBadge);
            Assert.Equal("Shown until the next load", dialog.LoadErrorHint);
        }

        // ---- The window

        private static IEnumerable<DependencyObject> Descendants(DependencyObject parent)
        {
            for (int i = 0; i < VisualTreeHelper.GetChildrenCount(parent); i++)
            {
                DependencyObject child = VisualTreeHelper.GetChild(parent, i);
                yield return child;

                foreach (DependencyObject below in Descendants(child))
                {
                    yield return below;
                }
            }
        }

        private static void OnSta(Action test)
        {
            Exception failure = null;
            var thread = new Thread(() =>
            {
                try
                {
                    test();
                }
                catch (Exception ex)
                {
                    failure = ex;
                }
            });
            thread.SetApartmentState(ApartmentState.STA);
            thread.Start();
            thread.Join();

            if (failure != null)
            {
                System.Runtime.ExceptionServices.ExceptionDispatchInfo.Capture(failure).Throw();
            }
        }

        [Fact]
        public void TheWindow_HasLoadFolderBesideLoad_TheBadgeOfARowThatNeedsPackages_AndTheFooterOfTheBoard()
        {
            MainViewModel model = WithTool(Packages.No, out string stairs);
            AppLoadViewModel dialog = Created(model);
            dialog.Load(new[] { stairs });
            string[] texts = null;
            Color badgeFill = default;
            Color badgeLine = default;
            Color badgeText = default;
            Color outlined = default;
            int visibleBadges = 0;

            OnSta(() =>
            {
                var window = new AppLoadWindow(dialog);
                FrameworkElement root = window.Body;
                window.Content = null;
                root.DataContext = dialog;
                root.Measure(new Size(900, 700));
                root.Arrange(new Rect(0, 0, 900, 700));
                root.UpdateLayout();
                List<DependencyObject> all = Descendants(root).ToList();
                texts = all.OfType<TextBlock>()
                    .Select(t => t.Text)
                    .Concat(all.OfType<Button>().Select(b => b.Content as string))
                    .ToArray();
                Border badge = all.OfType<Border>().Single(b => b.Name == "Badge");
                visibleBadges = all.OfType<Border>()
                    .Count(b => b.Name == "Badge" && b.Visibility == Visibility.Visible);
                badgeFill = ((SolidColorBrush)badge.Background).Color;
                badgeLine = ((SolidColorBrush)badge.BorderBrush).Color;
                badgeText = (
                    (SolidColorBrush)all.OfType<TextBlock>().Single(t => t.Name == "BadgeLabel").Foreground
                ).Color;
                outlined = (
                    (SolidColorBrush)all.OfType<Button>().Single(b => b.Name == "LoadFolderButton").Foreground
                ).Color;
                window.Close();
            });

            Assert.Contains("Load… (.lsp, .dll)", texts);
            Assert.Contains("Load folder…", texts);
            Assert.Contains("needs packages", texts);
            Assert.Contains(
                "Each tool run is one undo step. A tool runs its code, and its packages’ code, with your rights.",
                texts
            );
            Assert.Equal(1, visibleBadges);
            Assert.Equal(Color.FromRgb(0x3A, 0x2F, 0x12), badgeFill);
            Assert.Equal(Color.FromRgb(0xD2, 0x99, 0x22), badgeLine);
            Assert.Equal(Color.FromRgb(0xF2, 0xCC, 0x60), badgeText);
            Assert.Equal(Color.FromRgb(0x6C, 0xB6, 0xFF), outlined);
        }

        [Fact]
        public void TheWindow_ShowsTheBadgeInstallFailedInRed_AndNoBadgeOnALoadedRow()
        {
            MainViewModel model = WithTool(Packages.Offline, out string stairs);
            AppLoadViewModel dialog = Created(model);
            dialog.LoadFolder(Path.GetDirectoryName(stairs));
            _loader.Answer = Packages.None;

            // A second plugin, loaded: it has no badge.
            string other = Path.Combine(_folder.FullPath, "dev", "Other");
            Directory.CreateDirectory(other);
            File.WriteAllText(Path.Combine(other, "Other.csproj"), "// Other");
            dialog.LoadFolder(other);
            Color badgeFill = default;
            Color badgeLine = default;
            Color badgeText = default;
            int visibleBadges = 0;

            OnSta(() =>
            {
                var window = new AppLoadWindow(dialog);
                FrameworkElement root = window.Body;
                window.Content = null;
                root.DataContext = dialog;
                root.Measure(new Size(900, 700));
                root.Arrange(new Rect(0, 0, 900, 700));
                root.UpdateLayout();
                List<DependencyObject> all = Descendants(root).ToList();
                List<Border> badges = all.OfType<Border>()
                    .Where(b => b.Name == "Badge" && b.Visibility == Visibility.Visible)
                    .ToList();
                visibleBadges = badges.Count;
                badgeFill = ((SolidColorBrush)badges[0].Background).Color;
                badgeLine = ((SolidColorBrush)badges[0].BorderBrush).Color;
                badgeText = (
                    (SolidColorBrush)
                        all.OfType<TextBlock>().First(t => t.Name == "BadgeLabel" && t.ActualWidth > 0).Foreground
                ).Color;
                window.Close();
            });

            Assert.Equal(2, dialog.Rows.Count);
            Assert.Equal(1, visibleBadges);
            Assert.Equal(Color.FromRgb(0x3A, 0x1D, 0x1D), badgeFill);
            Assert.Equal(Color.FromRgb(0xF8, 0x51, 0x49), badgeLine);
            Assert.Equal(Color.FromRgb(0xFF, 0xB4, 0xAE), badgeText);
        }
    }
}
