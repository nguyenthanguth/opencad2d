using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Threading;
using OpenCAD2D.Plugins;
using Xunit;
using static OpenCAD2D.Wpf.UnitTest.PythonWindowKit;

namespace OpenCAD2D.Wpf.UnitTest
{
    /// <summary>
    /// Settings… of Load/Unload Applications and Plugin Settings (PW3-D17, D18): the button is live for one row that is loaded and
    /// declares settings, a C# plugin by <c>[CadSetting]</c> or a Python tool by <c>"settings"</c> in its plugin.json, and greyed
    /// otherwise with a tooltip that says why; the dialog has a row for each setting by kind, its description and range in grey, a
    /// refused value in red with OK greyed, OK applying every value at once and writing the settings file once, Cancel nothing; a
    /// plugin that loads is given the values kept for it. No window is shown: the dialog's content is laid out where a test reads
    /// it, the settings file is the test's own and no tool's process is started.
    /// </summary>
    public sealed class PluginSettingsWindowTests : IDisposable
    {
        private const string SamplePlugin = "OpenCAD2D.SamplePlugin";

        private const string AllKinds =
            "\"settings\": ["
            + " { \"name\": \"textHeight\", \"label\": \"Text height\", \"kind\": \"number\", \"default\": 2.5, \"minimum\": 0.1,"
            + " \"maximum\": 1000, \"description\": \"How high the numbers are written.\" },"
            + " { \"name\": \"start\", \"label\": \"Start number\", \"kind\": \"integer\", \"default\": 1, \"minimum\": 1,"
            + " \"maximum\": 9999, \"description\": \"The number of the first circle.\" },"
            + " { \"name\": \"mark\", \"label\": \"Mark the centre too\", \"kind\": \"boolean\", \"default\": true,"
            + " \"description\": \"A small cross at the centre of each circle marked.\" },"
            + " { \"name\": \"prefix\", \"label\": \"Prefix\", \"kind\": \"text\", \"default\": \"C-\","
            + " \"description\": \"Written before each number; may be empty.\" },"
            + " { \"name\": \"side\", \"label\": \"Side\", \"kind\": \"choice\", \"choices\": [\"Left\", \"Right\"],"
            + " \"default\": \"Left\", \"description\": \"Where the number stands: Left or Right of the top.\" } ]";

        private readonly PluginWindowKit _kit = new PluginWindowKit();

        public void Dispose() => _kit.Dispose();

        private string Tool(string name, string manifest)
        {
            string folder = Path.Combine(_kit.Folder.FullPath, "Pythons", name);
            Directory.CreateDirectory(folder);
            File.WriteAllText(Path.Combine(folder, "requirements.txt"), string.Empty);
            File.WriteAllText(Path.Combine(folder, "__init__.py"), "pass\n", new UTF8Encoding(false));
            File.WriteAllText(
                Path.Combine(folder, "plugin.json"),
                "{ \"name\": \"" + name + "\", " + manifest + " }",
                new UTF8Encoding(false)
            );
            return folder;
        }

        /// <summary>A window's model with the columns open, plugins and Python used (no process starts: a tool's loading starts none), and the settings in a file of the test's own.</summary>
        private MainViewModel Window()
        {
            MainViewModel model = _kit.Opened(out _, out _, out _);
            model.SettingsPath = Path.Combine(_kit.Folder.FullPath, "settings.json");
            model.ApplySettings(new GlobalSettings { SecureLoad = 0 });
            model.UsePython(
                Dispatcher.CurrentDispatcher,
                new PythonToolSetup
                {
                    Environments = new PythonEnvironments(
                        Path.Combine(_kit.Folder.FullPath, "envs"),
                        Path.Combine(_kit.Folder.FullPath, "base"),
                        PythonEnvTestSupport.AppPython,
                        PythonEnvTestSupport.ShippedWheels
                    ),
                    MakeHost = (host, dispatcher) =>
                        new CadToolHost(
                            host,
                            dispatcher,
                            PythonEnvTestSupport.Library,
                            Guid.NewGuid().ToString("N").Substring(0, 8)
                        ),
                }
            );
            return model;
        }

        /// <summary>The sample plugin with settings (Text height and Layer) as a plugin of the kit's trusted folder.</summary>
        private string SampleWithSettings()
        {
            string path = Path.Combine(_kit.Folder.FullPath, "dev", SamplePlugin + ".dll");
            Directory.CreateDirectory(Path.GetDirectoryName(path));
            File.Copy(
                Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "SamplePlugin", SamplePlugin + ".dll"),
                path,
                true
            );
            return path;
        }

        private static AppLoadRow Row(AppLoadViewModel dialog, string name) =>
            Assert.Single(dialog.Rows, r => r.Name == name);

        private static void Choose(AppLoadViewModel dialog, params string[] names) =>
            dialog.ChooseRows(names.Select(n => Row(dialog, n)));

        private static void WithWindow(Action test) => OnWindowThread(test);

        /// <summary>Whether an element and everything above it in the dialog is not collapsed.</summary>
        private static bool Shown(DependencyObject element)
        {
            for (DependencyObject at = element; at != null; at = System.Windows.Media.VisualTreeHelper.GetParent(at))
            {
                if (at is UIElement ui && ui.Visibility != Visibility.Visible)
                {
                    return false;
                }
            }

            return true;
        }

        // ---- The button

        [Fact]
        public void SettingsIsGreyedForARowNotLoadedOrDeclaringNone_AndLiveForAPluginAndAToolThatDeclareSome()
        {
            WithWindow(() =>
            {
                MainViewModel model = Window();

                try
                {
                    Assert.True(model.LoadPlugin(SampleWithSettings()).Succeeded);
                    _kit.LoadSample(model);
                    model.LoadApplication(Tool("Numbering", AllKinds));
                    model.LoadApplication(Tool("Plain", "\"commands\": [ \"PLAINCMD\" ]"));
                    using (var dialog = new AppLoadViewModel(model))
                    {
                        Assert.True(Row(dialog, SamplePlugin).HasSettings);
                        Assert.True(Row(dialog, "Numbering").HasSettings);
                        Assert.False(Row(dialog, PluginWindowKit.SampleName).HasSettings);
                        Assert.False(Row(dialog, "Plain").HasSettings);

                        Choose(dialog, SamplePlugin);
                        Assert.True(dialog.SettingsCommand.CanExecute(null));
                        Assert.Equal("The settings of " + SamplePlugin, dialog.SettingsToolTip);

                        Choose(dialog, "Numbering");
                        Assert.True(dialog.SettingsCommand.CanExecute(null));

                        Choose(dialog, PluginWindowKit.SampleName);
                        Assert.False(dialog.SettingsCommand.CanExecute(null));
                        Assert.Equal(PluginWindowKit.SampleName + " declares no settings", dialog.SettingsToolTip);

                        Choose(dialog, "Plain");
                        Assert.False(dialog.SettingsCommand.CanExecute(null));
                        Assert.Equal("Plain declares no settings", dialog.SettingsToolTip);

                        Choose(dialog);
                        Assert.False(dialog.SettingsCommand.CanExecute(null));
                        Assert.Equal("Choose a loaded plugin or tool to change its settings", dialog.SettingsToolTip);

                        Choose(dialog, SamplePlugin, "Numbering");
                        Assert.False(dialog.SettingsCommand.CanExecute(null));
                        Assert.Equal("Choose one row: Settings… shows the settings of one", dialog.SettingsToolTip);
                    }
                }
                finally
                {
                    model.DisposePython();
                }
            });
        }

        [Fact]
        public void SettingsIsGreyedForARowNotLoaded_WithATooltipThatSaysSo_AndForAnAutoLispFile()
        {
            WithWindow(() =>
            {
                MainViewModel model = Window();

                try
                {
                    Assert.True(model.LoadPlugin(SampleWithSettings()).Succeeded);
                    string lisp = _kit.Folder.WriteText("marks.lsp", "(defun c:marks () (princ))\n");
                    model.LoadApplication(lisp);
                    using (var dialog = new AppLoadViewModel(model))
                    {
                        Choose(dialog, "marks.lsp");
                        Assert.False(dialog.SettingsCommand.CanExecute(null));
                        Assert.Equal("marks.lsp has no settings", dialog.SettingsToolTip);

                        model.UnloadPlugin(SamplePlugin);
                        Choose(dialog, SamplePlugin);

                        Assert.False(dialog.SettingsCommand.CanExecute(null));
                        Assert.Equal(
                            SamplePlugin + " is not loaded: load it to change its settings",
                            dialog.SettingsToolTip
                        );
                    }
                }
                finally
                {
                    model.DisposePython();
                }
            });
        }

        [Fact]
        public void PressingSettings_OpensPluginSettingsOfTheRowChosen_WithItsNameAndWhereItIs()
        {
            WithWindow(() =>
            {
                MainViewModel model = Window();

                try
                {
                    string folder = Tool("Numbering", AllKinds);
                    model.LoadApplication(folder);
                    PluginSettingsViewModel shown = null;

                    using (var dialog = new AppLoadViewModel(model))
                    {
                        dialog.ShowSettings = settings =>
                        {
                            shown = settings;
                            return false;
                        };
                        Choose(dialog, "Numbering");

                        dialog.SettingsCommand.Execute(null);
                    }

                    Assert.NotNull(shown);
                    Assert.Equal("Plugin Settings – Numbering", shown.Title);
                    Assert.Equal("Python tool · " + folder + " · kept in the settings file", shown.Description);
                }
                finally
                {
                    model.DisposePython();
                }
            });
        }

        // ---- The dialog

        [Fact]
        public void TheDialogOfAPythonTool_HasARowForEachSettingByKind_WithItsRangeAndDescriptionInGrey()
        {
            WithWindow(() =>
            {
                MainViewModel model = Window();

                try
                {
                    model.LoadApplication(Tool("Numbering", AllKinds));

                    PluginSettingsViewModel dialog = PythonToolSnapProcessTests.SettingsOf(model, "Numbering");

                    Assert.Equal(
                        new[] { "Text height", "Start number", "Mark the centre too", "Prefix", "Side" },
                        dialog.Rows.Select(r => r.Label)
                    );
                    Assert.Equal(
                        new[]
                        {
                            CadSettingKind.Number,
                            CadSettingKind.Integer,
                            CadSettingKind.Boolean,
                            CadSettingKind.Text,
                            CadSettingKind.Choice,
                        },
                        dialog.Rows.Select(r => r.Kind)
                    );
                    Assert.Equal(new[] { true, true, false, true, false }, dialog.Rows.Select(r => r.IsBox));
                    Assert.True(dialog.Rows[2].IsCheck);
                    Assert.True(dialog.Rows[4].IsChoice);
                    Assert.Equal(new[] { "Left", "Right" }, dialog.Rows[4].Choices);
                    Assert.Equal(
                        new[]
                        {
                            "How high the numbers are written. From 0.1 to 1000.",
                            "The number of the first circle. A whole number from 1 to 9999.",
                            "A small cross at the centre of each circle marked.",
                            "Written before each number; may be empty.",
                            "Where the number stands: Left or Right of the top.",
                        },
                        dialog.Rows.Select(r => r.Hint)
                    );
                    Assert.Equal("2.5", dialog.Rows[0].Text);
                    Assert.Equal("1", dialog.Rows[1].Text);
                    Assert.True(dialog.Rows[2].IsChecked);
                    Assert.Equal("C-", dialog.Rows[3].Text);
                    Assert.Equal("Left", dialog.Rows[4].Choice);
                    Assert.True(dialog.CanApply);
                }
                finally
                {
                    model.DisposePython();
                }
            });
        }

        [Fact]
        public void TheDialogOfACSharpPlugin_HasARowForEachProperty_AsAToolsHas()
        {
            WithWindow(() =>
            {
                MainViewModel model = Window();

                try
                {
                    Assert.True(model.LoadPlugin(SampleWithSettings()).Succeeded);

                    PluginSettingsViewModel dialog = PythonToolSnapProcessTests.SettingsOf(model, SamplePlugin);

                    Assert.Equal("Plugin Settings – " + SamplePlugin, dialog.Title);
                    Assert.Equal(new[] { "Text height", "Layer" }, dialog.Rows.Select(r => r.Label));
                    Assert.Equal(
                        new[]
                        {
                            "How high the numbers are written. From 0.1 to 1000.",
                            "The layer the numbers are put on.",
                        },
                        dialog.Rows.Select(r => r.Hint)
                    );
                    Assert.Equal(new[] { "2.5", "Mark" }, dialog.Rows.Select(r => r.Text));
                    Assert.StartsWith("C# plugin · ", dialog.Description);
                }
                finally
                {
                    model.DisposePython();
                }
            });
        }

        [Fact]
        public void TheWindowShowsALabelABoxAndTheGreyWordsOfEveryRow_AndOkAndCancel()
        {
            WithWindow(() =>
            {
                MainViewModel model = Window();

                try
                {
                    model.LoadApplication(Tool("Numbering", AllKinds));
                    PluginSettingsViewModel settings = PythonToolSnapProcessTests.SettingsOf(model, "Numbering");
                    var window = new PluginSettingsWindow(settings);
                    FrameworkElement root = window.Body;
                    window.Content = null;
                    root.DataContext = settings;
                    Layout(root, 540, 700);
                    List<DependencyObject> all = Descendants(root).ToList();

                    string[] texts = all.OfType<TextBlock>().Select(t => t.Text).ToArray();
                    foreach (
                        string text in new[]
                        {
                            "Plugin Settings – Numbering",
                            "Text height",
                            "How high the numbers are written. From 0.1 to 1000.",
                            "Start number",
                            "A whole number from 1 to 9999.",
                            "Mark the centre too",
                            "Prefix",
                            "Side",
                            "OK applies every value at once.",
                        }
                    )
                    {
                        Assert.Contains(texts, t => t.Contains(text));
                    }

                    // Each row draws the box of its kind and collapses the other two.
                    Assert.Equal(new[] { "2.5", "1", "C-" }, all.OfType<TextBox>().Where(Shown).Select(t => t.Text));
                    Assert.Single(all.OfType<CheckBox>(), c => Shown(c));
                    Assert.Equal(
                        new[] { "Left", "Right" },
                        Assert.Single(all.OfType<ComboBox>(), c => Shown(c)).Items.Cast<string>()
                    );
                    Assert.Equal("OK", window.OkButton.Content);
                    Assert.Equal("Cancel", window.CancelButton.Content);
                    Assert.True(window.OkButton.IsEnabled);
                    Assert.Equal(540.0, window.Width);
                    window.Close();
                }
                finally
                {
                    model.DisposePython();
                }
            });
        }

        // ---- OK and Cancel

        [Fact]
        public void Ok_WritesEveryValueToTheStoreAndTheSettingsFileOnce_AndCancelWritesNothing()
        {
            WithWindow(() =>
            {
                MainViewModel model = Window();

                try
                {
                    model.LoadApplication(Tool("Numbering", AllKinds));
                    int changes = 0;
                    model.PluginSettingsChanged += (sender, e) => changes++;

                    // Cancel: the boxes edited and the dialog left, nothing applied.
                    PluginSettingsViewModel cancelled = PythonToolSnapProcessTests.SettingsOf(model, "Numbering");
                    cancelled.Rows[0].Text = "9";
                    cancelled.Dispose();
                    Assert.Empty(model.Settings.PluginSettings);
                    Assert.Equal(0, changes);

                    PluginSettingsViewModel dialog = PythonToolSnapProcessTests.SettingsOf(model, "Numbering");
                    dialog.Rows[0].Text = "7.25";
                    dialog.Rows[1].Text = "12";
                    dialog.Rows[2].IsChecked = false;
                    dialog.Rows[3].Text = "N-";
                    dialog.Rows[4].Choice = "Right";

                    Assert.True(dialog.Apply());

                    Assert.Equal(1, changes);
                    Assert.Equal(
                        new[]
                        {
                            "Numbering/textHeight=7.25",
                            "Numbering/start=12",
                            "Numbering/mark=False",
                            "Numbering/prefix=N-",
                            "Numbering/side=Right",
                        },
                        model.Settings.PluginSettings
                    );
                    string file = File.ReadAllText(model.SettingsPath);
                    Assert.Contains("Numbering/textHeight=7.25", file);
                    Assert.Contains("Numbering/side=Right", file);

                    // A second OK with the values as they are writes nothing.
                    Assert.True(dialog.Apply());
                    Assert.Equal(1, changes);
                }
                finally
                {
                    model.DisposePython();
                }
            });
        }

        [Fact]
        public void AValueOutOfItsRange_IsSaidUnderItsRowInTheSettingsWords_AndOkIsGreyedUntilItIsFixed()
        {
            WithWindow(() =>
            {
                MainViewModel model = Window();

                try
                {
                    Assert.True(model.LoadPlugin(SampleWithSettings()).Succeeded);
                    PluginSettingsViewModel dialog = PythonToolSnapProcessTests.SettingsOf(model, SamplePlugin);
                    var window = new PluginSettingsWindow(dialog);
                    FrameworkElement root = window.Body;
                    window.Content = null;
                    root.DataContext = dialog;
                    Layout(root, 540, 700);
                    Assert.True(window.OkButton.IsEnabled);

                    dialog.Rows[0].Text = "0.05";
                    Layout(root, 540, 700);

                    Assert.Equal("The setting Text height lies from 0.1 to 1000.", dialog.Rows[0].Error);
                    Assert.True(dialog.Rows[0].HasError);
                    Assert.False(dialog.CanApply);
                    Assert.False(window.OkButton.IsEnabled);
                    Assert.False(dialog.Apply());
                    Assert.Empty(model.Settings.PluginSettings);
                    Assert.Contains(Descendants(root).OfType<TextBlock>(), t =>
                        t.Text == "The setting Text height lies from 0.1 to 1000." && t.Visibility == Visibility.Visible
                    );

                    dialog.Rows[0].Text = "3";
                    Layout(root, 540, 700);

                    Assert.Equal(string.Empty, dialog.Rows[0].Error);
                    Assert.True(dialog.CanApply);
                    Assert.True(window.OkButton.IsEnabled);
                    Assert.True(dialog.Apply());
                    Assert.Contains(SamplePlugin + "/TextHeight=3", model.Settings.PluginSettings);
                    window.Close();
                }
                finally
                {
                    model.DisposePython();
                }
            });
        }

        [Fact]
        public void AValueSetByTheToolWhileTheDialogIsOpen_IsReadAgainByTheBoxesTheUserHasNotTouched()
        {
            WithWindow(() =>
            {
                MainViewModel model = Window();

                try
                {
                    string folder = Tool("Numbering", AllKinds);
                    model.LoadApplication(folder);
                    PluginSettingsViewModel dialog = PythonToolSnapProcessTests.SettingsOf(model, "Numbering");
                    dialog.Rows[1].Text = "20";

                    // The tool sets two of them over its channel, as its thread does.
                    IReadOnlyList<CadPluginSetting> declared = ((PythonToolFiles)model.PythonFiles).SettingsOf(folder);
                    CadPluginSetting height = declared.Single(s => s.Name == "textHeight");
                    CadPluginSetting start = declared.Single(s => s.Name == "start");
                    height.Value = 4.0;
                    start.Value = 5;
                    Pump(() => dialog.Rows[0].Text == "4", "The box did not read the value again.");

                    Assert.Equal("4", dialog.Rows[0].Text);
                    Assert.Equal("20", dialog.Rows[1].Text);
                }
                finally
                {
                    model.DisposePython();
                }
            });
        }

        // ---- Loading a plugin

        [Fact]
        public void ACSharpPluginLoaded_IsGivenTheValuesKeptForIt_AndOneNeverSetKeepsItsDefaults()
        {
            WithWindow(() =>
            {
                MainViewModel model = Window();

                try
                {
                    model.Settings.PluginSettings = new List<string> { SamplePlugin + "/TextHeight=7.5" };

                    CadPluginResult loaded = model.LoadPlugin(SampleWithSettings());

                    Assert.True(loaded.Succeeded, loaded.ToString());
                    IReadOnlyList<CadPluginSetting> settings = loaded.Plugin.Plugin.Settings;
                    Assert.Equal(7.5, settings.Single(s => s.Name == "TextHeight").Value);
                    Assert.Equal("Mark", settings.Single(s => s.Name == "Layer").Value);

                    // The sample UI plugin was never set and declares none: nothing happens to it.
                    _kit.LoadSample(model);
                }
                finally
                {
                    model.DisposePython();
                }
            });
        }

        [Fact]
        public void ACSharpPluginThatNeverHadAValueKept_KeepsItsDefault_AndAValueItNowRefusesIsDropped()
        {
            WithWindow(() =>
            {
                MainViewModel model = Window();

                try
                {
                    CadPluginResult first = model.LoadPlugin(SampleWithSettings());
                    Assert.Equal(2.5, first.Plugin.Plugin.Settings.Single(s => s.Name == "TextHeight").Value);
                    Assert.True(model.UnloadPlugin(SamplePlugin).Succeeded);

                    // A range the plugin has narrowed since the value was kept.
                    model.Settings.PluginSettings = new List<string> { SamplePlugin + "/TextHeight=0.05" };
                    CadPluginResult second = model.LoadPlugin(SampleWithSettings());

                    Assert.Equal(2.5, second.Plugin.Plugin.Settings.Single(s => s.Name == "TextHeight").Value);
                    Assert.Empty(model.Settings.PluginSettings);
                }
                finally
                {
                    model.DisposePython();
                }
            });
        }

        [Fact]
        public void TheValueOkWroteForACSharpPlugin_IsGivenToItAgainWhenItIsLoadedAgain()
        {
            WithWindow(() =>
            {
                MainViewModel model = Window();

                try
                {
                    Assert.True(model.LoadPlugin(SampleWithSettings()).Succeeded);
                    PluginSettingsViewModel dialog = PythonToolSnapProcessTests.SettingsOf(model, SamplePlugin);
                    dialog.Rows[0].Text = "4.5";
                    dialog.Rows[1].Text = "Walls";
                    Assert.True(dialog.Apply());
                    Assert.Equal(
                        new[] { SamplePlugin + "/TextHeight=4.5", SamplePlugin + "/Layer=Walls" },
                        model.Settings.PluginSettings
                    );

                    CadPluginResult reloaded = model.ReloadPlugin(SamplePlugin);

                    Assert.True(reloaded.Succeeded, reloaded.ToString());
                    IReadOnlyList<CadPluginSetting> settings = reloaded.Plugin.Plugin.Settings;
                    Assert.Equal(4.5, settings.Single(s => s.Name == "TextHeight").Value);
                    Assert.Equal("Walls", settings.Single(s => s.Name == "Layer").Value);
                }
                finally
                {
                    model.DisposePython();
                }
            });
        }
    }
}
