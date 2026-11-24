using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using OpenCAD2D.Plugins;
using OpenCAD2D.Wpf.Plugins;
using Xunit;

namespace OpenCAD2D.Wpf.UnitTest
{
    /// <summary>
    /// The question before the NuGet packages of a C# plugin are downloaded, on the command line (FILEDIA 0): PLUGINLOAD and
    /// PLUGINRELOAD stop the load for the packages, list them, and ask "Download these packages? [Yes/No] &lt;No&gt;:"; a No
    /// leaves the plugin not loaded with the badge "needs packages", a Yes loads it, and the window's dialog is not asked.
    /// With FILEDIA 1 the dialog is asked as before. The loader is a fake that stops and downloads as the app's loader does;
    /// no window is shown, nothing reaches the network or the user's app data.
    /// </summary>
    public sealed class PluginPackageCommandLineTests : IDisposable
    {
        private const string SampleName = "OpenCAD2D.SamplePlugin";
        private const string Declined = "Stairs was not loaded: its packages were not downloaded.";
        private readonly TestFolder _folder = new TestFolder();
        private readonly List<IDisposable> _disposables = new List<IDisposable>();
        private PackageLoader _loader;

        public void Dispose()
        {
            foreach (IDisposable item in _disposables)
            {
                item.Dispose();
            }

            _folder.Dispose();
        }

        /// <summary>
        /// A loader that compiles a plugin with two packages to download as the app's does: in the loader's own question (the
        /// dialog) it asks <see cref="Dialog"/>, where the command line asks first it stops with the lines, and after a Yes it
        /// builds the sample plugin.
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

            /// <summary>What the window's dialog answers.</summary>
            public bool Dialog { get; set; }

            /// <summary>How many times the window's dialog was asked.</summary>
            public int DialogsAsked { get; private set; }

            /// <summary>How many times a compile was made, the one that stopped included.</summary>
            public int Compiles { get; private set; }

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
                Compiles++;

                switch (Download)
                {
                    case CadDownloadMode.Defer:
                        throw new CadPluginDownloadPending(
                            new[]
                            {
                                name + " needs these packages from nuget.org:",
                                "  Foo 1.0.0",
                                "  Bar 2.1.0 (needed by another)",
                            }
                        );
                    case CadDownloadMode.Ask:
                        DialogsAsked++;

                        if (!Dialog)
                        {
                            throw new CadPluginException(name, Declined);
                        }

                        break;
                }

                return new CadPluginBuild(_dll, _pdb, false, Array.Empty<string>());
            }
        }

        /// <summary>A drawing open with a manager over <see cref="PackageLoader"/>, and Stairs\Stairs.csproj in a folder it trusts; PLUGINLOAD has the window's dialog where <paramref name="withWindow"/>.</summary>
        private MainViewModel Window(out string stairs, bool withWindow = true, string filedia = "0")
        {
            string built = Path.Combine(_folder.FullPath, "built");
            Directory.CreateDirectory(built);
            string dll = Path.Combine(built, "Stairs.dll");
            File.Copy(Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "SamplePlugin", SampleName + ".dll"), dll);
            File.WriteAllText(Path.ChangeExtension(dll, ".pdb"), string.Empty);
            _loader = new PackageLoader(dll, Path.ChangeExtension(dll, ".pdb"));
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
            var model = new MainViewModel { SettingsPath = Path.Combine(_folder.FullPath, "settings.json") };
            model.UsePlugins(manager, loadAtStart: false);

            if (withWindow)
            {
                model.PluginLoadRequested += (s, e) => FileDialogsShown++;
            }

            Assert.True(model.Open(_folder.Write("room.dxf", TestFiles.Room(out _, out _))));
            model.LoadSecurity.SecureLoad = 0;
            model.Execute("FILEDIA");
            model.Execute(filedia);
            return model;
        }

        /// <summary>How many times the window's file dialog of PLUGINLOAD was asked for.</summary>
        private int FileDialogsShown { get; set; }

        private static string Prompt(MainViewModel model) => model.Session.Prompt?.Text;

        private const string Question = "Download these packages? [Yes/No] <No>:";

        // ---- PLUGINLOAD

        [Fact]
        public void WithFiledia0_PluginLoad_AsksThePathOnTheCommandLine_ListsThePackages_AndAsksYesOrNo_NothingIsLoadedYet()
        {
            MainViewModel model = Window(out string stairs);

            model.Execute("PLUGINLOAD");
            Assert.StartsWith("Enter the path of a plugin", Prompt(model));
            model.Execute(Path.GetDirectoryName(stairs));

            Assert.Equal(Question, Prompt(model));
            Assert.Equal(0, FileDialogsShown);
            int at = model.History.ToList().IndexOf("Stairs needs these packages from nuget.org:");
            Assert.True(at >= 0, string.Join(" | ", model.History));
            Assert.Equal("  Foo 1.0.0", model.History[at + 1]);
            Assert.Equal("  Bar 2.1.0 (needed by another)", model.History[at + 2]);
            Assert.Null(model.Plugins.Find("Stairs"));
            Assert.Equal(0, _loader.DialogsAsked);
            Assert.Equal(1, _loader.Compiles);
        }

        [Fact]
        public void WithFiledia0_AnsweredNo_LeavesThePluginNotLoaded_WithTheLineOfTheDialogsNo_AndTheBadgeNeedsPackages()
        {
            MainViewModel model = Window(out string stairs);
            string folder = Path.GetDirectoryName(stairs);

            model.Execute("PLUGINLOAD");
            model.Execute(folder);
            model.Execute("No");

            Assert.Null(Prompt(model));
            Assert.Equal(Declined, model.History.Last(l => l.StartsWith("Stairs", StringComparison.Ordinal)));
            Assert.Null(model.Plugins.Find("Stairs"));
            Assert.Equal(1, _loader.Compiles);
            Assert.Equal(0, _loader.DialogsAsked);
            Assert.Equal(AppLoadBadge.NeedsPackages, model.PackageStateOf(folder).Kind);

            var dialog = new AppLoadViewModel(model);
            _disposables.Add(dialog);
            AppLoadRow row = Assert.Single(dialog.Rows, r => r.Name == "Stairs");
            Assert.False(row.IsOn);
            Assert.Equal(AppLoadBadge.NeedsPackages, row.Badge);
            Assert.Contains(model.Plugins.Log, e => !e.Succeeded && e.Plugin == "Stairs" && e.Result == Declined);
        }

        [Fact]
        public void WithFiledia0_EnterTakesTheDefaultNo_AndSoDoesAnythingButYes()
        {
            MainViewModel model = Window(out string stairs);
            string folder = Path.GetDirectoryName(stairs);

            model.Execute("PLUGINLOAD");
            model.Execute(folder);
            model.Execute(string.Empty);

            Assert.Null(model.Plugins.Find("Stairs"));
            Assert.Equal(AppLoadBadge.NeedsPackages, model.PackageStateOf(folder).Kind);
        }

        [Fact]
        public void WithFiledia0_AnsweredYes_LoadsThePlugin_WithNoBadge_AndTheDialogIsNeverAsked()
        {
            MainViewModel model = Window(out string stairs);
            string folder = Path.GetDirectoryName(stairs);

            model.Execute("PLUGINLOAD");
            model.Execute(folder);
            model.Execute("Yes");

            Assert.Null(Prompt(model));
            Assert.True(model.Plugins.Find("Stairs").IsLoaded);
            Assert.Contains(model.History, l => l.StartsWith("Loaded Stairs", StringComparison.Ordinal));
            Assert.Equal(2, _loader.Compiles);
            Assert.Equal(0, _loader.DialogsAsked);
            Assert.Null(model.PackageStateOf(folder));
        }

        [Fact]
        public void WithFiledia0_AYesAfterANo_ClearsTheBadge()
        {
            MainViewModel model = Window(out string stairs);
            string folder = Path.GetDirectoryName(stairs);
            model.Execute("PLUGINLOAD");
            model.Execute(folder);
            model.Execute("No");
            Assert.NotNull(model.PackageStateOf(folder));

            model.Execute("PLUGINLOAD");
            model.Execute(folder);
            model.Execute("Yes");

            Assert.True(model.Plugins.Find("Stairs").IsLoaded);
            Assert.Null(model.PackageStateOf(folder));
        }

        // ---- PLUGINRELOAD

        [Fact]
        public void WithFiledia0_PluginReload_AsksOnTheCommandLine_AndYesLoadsAgain()
        {
            MainViewModel model = Window(out string stairs);
            string folder = Path.GetDirectoryName(stairs);
            model.Execute("PLUGINLOAD");
            model.Execute(folder);
            model.Execute("No");
            Assert.NotNull(model.PackageStateOf(folder));
            Assert.Null(model.Plugins.Find("Stairs"));

            // PLUGINRELOAD knows a plugin by its name once it has been loaded: load it with the dialog's Yes first.
            model.Execute("FILEDIA");
            model.Execute("1");
            _loader.Dialog = true;
            model.LoadPlugin(folder);
            model.Execute("PLUGINUNLOAD");
            model.Execute("Stairs");
            model.Execute("FILEDIA");
            model.Execute("0");
            int compiles = _loader.Compiles;

            model.Execute("PLUGINRELOAD");
            model.Execute("Stairs");

            Assert.Equal(Question, Prompt(model));
            Assert.False(model.Plugins.Find("Stairs").IsLoaded);
            model.Execute("Yes");
            Assert.True(model.Plugins.Find("Stairs").IsLoaded);
            Assert.Equal(compiles + 2, _loader.Compiles);
            Assert.Contains(model.History, l => l.StartsWith("Reloaded Stairs", StringComparison.Ordinal));
        }

        [Fact]
        public void WithFiledia0_PluginReload_AnsweredNo_LeavesItUnloadedWithTheBadge()
        {
            MainViewModel model = Window(out string stairs, filedia: "1");
            string folder = Path.GetDirectoryName(stairs);
            _loader.Dialog = true;
            model.LoadPlugin(folder);
            model.Execute("FILEDIA");
            model.Execute("0");

            model.Execute("PLUGINRELOAD");
            model.Execute("Stairs");
            model.Execute("No");

            Assert.False(model.Plugins.Find("Stairs").IsLoaded);
            Assert.Equal(AppLoadBadge.NeedsPackages, model.PackageStateOf(folder).Kind);
        }

        // ---- FILEDIA 1

        [Fact]
        public void WithFiledia1_PluginLoad_ShowsTheWindowsFileDialog_AndTheLoadAsksTheDialogHook_NotTheCommandLine()
        {
            MainViewModel model = Window(out string stairs, filedia: "1");
            _loader.Dialog = true;

            model.Execute("PLUGINLOAD");
            Assert.Equal(1, FileDialogsShown);
            Assert.Null(Prompt(model));

            model.LoadPlugin(Path.GetDirectoryName(stairs));

            Assert.Equal(1, _loader.DialogsAsked);
            Assert.Equal(1, _loader.Compiles);
            Assert.True(model.Plugins.Find("Stairs").IsLoaded);
            Assert.DoesNotContain(model.History, l => l.StartsWith("  Foo", StringComparison.Ordinal));
        }

        [Fact]
        public void WithFiledia1_AndNoWindow_TypedPluginLoad_AsksTheDialogHook_AndNoQuestionIsPutOnTheCommandLine()
        {
            MainViewModel model = Window(out string stairs, withWindow: false, filedia: "1");
            _loader.Dialog = false;

            model.Execute("PLUGINLOAD");
            model.Execute(Path.GetDirectoryName(stairs));

            Assert.Null(Prompt(model));
            Assert.Equal(1, _loader.DialogsAsked);
            Assert.Equal(Declined, model.History.Last(l => l.StartsWith("Stairs", StringComparison.Ordinal)));
            Assert.Null(model.Plugins.Find("Stairs"));
        }

        [Fact]
        public void WithFiledia0_ButNoWindow_TypedPluginLoad_AsksOnTheCommandLineToo()
        {
            MainViewModel model = Window(out string stairs, withWindow: false);

            model.Execute("PLUGINLOAD");
            model.Execute(Path.GetDirectoryName(stairs));

            Assert.Equal(Question, Prompt(model));
            model.Execute("Y");
            Assert.True(model.Plugins.Find("Stairs").IsLoaded);
            Assert.Equal(0, _loader.DialogsAsked);
        }
    }
}
