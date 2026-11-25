using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Threading;
using OpenCAD2D.Plugins;
using Xunit;

namespace OpenCAD2D.UnitTest
{
    /// <summary>
    /// The values of the plugins' settings, kept in <see cref="GlobalSettings.PluginSettings"/> as <c>Circles/textHeight=2.5</c>
    /// (PW3-D16, D17): the store a tool's declared settings read and write through, and a C# plugin's properties are given their
    /// kept values by.
    /// </summary>
    public class PluginSettingStoreTests
    {
        private const string Declared =
            "{ \"name\": \"Circles\", \"settings\": ["
            + " { \"name\": \"textHeight\", \"label\": \"Text height\", \"kind\": \"number\", \"default\": 2.5, \"minimum\": 0.1, \"maximum\": 1000 },"
            + " { \"name\": \"start\", \"kind\": \"integer\", \"default\": 1 },"
            + " { \"name\": \"boxed\", \"kind\": \"boolean\", \"default\": false },"
            + " { \"name\": \"note\", \"kind\": \"text\", \"default\": \"\" },"
            + " { \"name\": \"side\", \"kind\": \"choice\", \"choices\": [\"Left\", \"Right\"], \"default\": \"Left\" } ] }";

        private sealed class Holder
        {
            public double TextHeight { get; set; } = 2.5;
        }

        private static CadPluginSetting Property(Holder target)
        {
            var attribute = new CadSettingAttribute("Text height") { Minimum = 0.1, Maximum = 1000 };
            return new CadPluginSetting(
                typeof(Holder).GetProperty(nameof(Holder.TextHeight)),
                target,
                attribute,
                CadSettingKind.Number
            );
        }

        private static IReadOnlyList<CadPluginSetting> Make(
            GlobalSettings settings,
            out CadPluginSettingStore store,
            string plugin = "Circles"
        )
        {
            GlobalSettings held = settings;
            store = new CadPluginSettingStore(() => held);
            return store.Declare(plugin, CadPluginManifest.Parse(Declared).Settings);
        }

        private static CadPluginSetting Named(IReadOnlyList<CadPluginSetting> settings, string name) =>
            settings.Single(s => s.Name == name);

        [Fact]
        public void ASettingNeverSet_HasItsDefault_AndLeavesNoEntry()
        {
            var settings = new GlobalSettings();
            IReadOnlyList<CadPluginSetting> made = Make(settings, out _);

            Assert.Equal(2.5, Named(made, "textHeight").Value);
            Assert.Equal(1, Named(made, "start").Value);
            Assert.Equal(false, Named(made, "boxed").Value);
            Assert.Equal("Left", Named(made, "side").Value);
            Assert.Empty(settings.PluginSettings);
        }

        [Fact]
        public void AStoredSettingConverts2Point5_AndRefuses0Point05_InTheWordsAPropertySettingUses()
        {
            var settings = new GlobalSettings();
            IReadOnlyList<CadPluginSetting> made = Make(settings, out CadPluginSettingStore store);
            CadPluginSetting height = Named(made, "textHeight");

            store.Set("Circles", height, "7.5");
            Assert.Equal(7.5, height.Value);

            ArgumentOutOfRangeException refused = Assert.Throws<ArgumentOutOfRangeException>(() =>
                store.Set("Circles", height, 0.05)
            );
            ArgumentOutOfRangeException onAProperty = Assert.Throws<ArgumentOutOfRangeException>(() =>
                Property(new Holder()).Value = 0.05
            );

            Assert.Equal("The setting Text height lies from 0.1 to 1000.", CadPluginSetting.WordsOf(refused));
            Assert.Equal(CadPluginSetting.WordsOf(onAProperty), CadPluginSetting.WordsOf(refused));
            Assert.Equal(7.5, height.Value);
            Assert.Equal(new[] { "Circles/textHeight=7.5" }, settings.PluginSettings);
        }

        [Fact]
        public void TheStoreWritesTheValueInTheInvariantCulture_UnderGermanSettings()
        {
            CultureInfo before = Thread.CurrentThread.CurrentCulture;

            try
            {
                Thread.CurrentThread.CurrentCulture = new CultureInfo("de-DE");
                var settings = new GlobalSettings();
                IReadOnlyList<CadPluginSetting> made = Make(settings, out CadPluginSettingStore store);

                store.Set("Circles", Named(made, "textHeight"), 2.75);
                store.Set("Circles", Named(made, "boxed"), true);
                store.Set("Circles", Named(made, "start"), 12);
                store.Set("Circles", Named(made, "side"), "right");

                Assert.Equal(
                    new[] { "Circles/textHeight=2.75", "Circles/boxed=True", "Circles/start=12", "Circles/side=Right" },
                    settings.PluginSettings
                );
                Assert.Equal(2.75, Named(made, "textHeight").Value);
                Assert.Equal("Right", Named(made, "side").Value);
            }
            finally
            {
                Thread.CurrentThread.CurrentCulture = before;
            }
        }

        [Fact]
        public void TheLastEntryOfASettingWins_AndEntriesOfPluginsNotLoadedAreKept()
        {
            var settings = new GlobalSettings
            {
                PluginSettings = new List<string>
                {
                    "Circles/textHeight=3",
                    "Bolts/diameter=20",
                    "Circles/textHeight=4",
                    "not an entry",
                },
            };
            IReadOnlyList<CadPluginSetting> made = Make(settings, out CadPluginSettingStore store);

            Assert.Equal(4.0, Named(made, "textHeight").Value);

            store.Set("Circles", Named(made, "textHeight"), 6);

            Assert.Equal(
                new[] { "Bolts/diameter=20", "not an entry", "Circles/textHeight=6" },
                settings.PluginSettings
            );
        }

        [Fact]
        public void AValueEqualToTheDefault_RemovesItsEntry()
        {
            var settings = new GlobalSettings();
            IReadOnlyList<CadPluginSetting> made = Make(settings, out CadPluginSettingStore store);
            store.Set("Circles", Named(made, "textHeight"), 6);
            store.Set("Circles", Named(made, "boxed"), true);

            store.Set("Circles", Named(made, "textHeight"), 2.5);

            Assert.Equal(new[] { "Circles/boxed=True" }, settings.PluginSettings);
            Assert.Equal(2.5, Named(made, "textHeight").Value);
        }

        [Fact]
        public void AValueHoldingAnEqualsSignAndASlash_ReadsBackWhole()
        {
            var settings = new GlobalSettings();
            IReadOnlyList<CadPluginSetting> made = Make(settings, out CadPluginSettingStore store);

            store.Set("Circles", Named(made, "note"), "a=b/c=d");

            Assert.Equal(new[] { "Circles/note=a=b/c=d" }, settings.PluginSettings);
            Assert.Equal("a=b/c=d", Named(made, "note").Value);
        }

        [Fact]
        public void APluginNameHoldingAnEqualsSign_IsKeptAndReadBack()
        {
            var settings = new GlobalSettings();
            IReadOnlyList<CadPluginSetting> made = Make(settings, out CadPluginSettingStore store, "a=b");

            store.Set("a=b", Named(made, "start"), 5);

            Assert.Equal(new[] { "a=b/start=5" }, settings.PluginSettings);
            Assert.Equal(5, Named(made, "start").Value);
        }

        [Fact]
        public void TwoPlugins_KeepTheirOwnValuesOfOneSettingName()
        {
            var settings = new GlobalSettings();
            IReadOnlyList<CadPluginSetting> first = Make(settings, out CadPluginSettingStore store);
            IReadOnlyList<CadPluginSetting> second = store.Declare("Bolts", CadPluginManifest.Parse(Declared).Settings);

            store.Set("Circles", Named(first, "start"), 5);
            store.Set("Bolts", Named(second, "start"), 9);

            Assert.Equal(5, Named(first, "start").Value);
            Assert.Equal(9, Named(second, "start").Value);
        }

        [Fact]
        public void AStoredValueTheSettingRefuses_IsDropped_AndTheDefaultKept()
        {
            var settings = new GlobalSettings
            {
                PluginSettings = new List<string>
                {
                    "Circles/textHeight=0.05",
                    "Circles/start=2.5",
                    "Circles/side=Up",
                    "Circles/boxed=True",
                },
            };
            IReadOnlyList<CadPluginSetting> made = Make(settings, out CadPluginSettingStore store);

            store.Apply("Circles", made);

            Assert.Equal(new[] { "Circles/boxed=True" }, settings.PluginSettings);
            Assert.Equal(2.5, Named(made, "textHeight").Value);
            Assert.Equal(1, Named(made, "start").Value);
            Assert.Equal("Left", Named(made, "side").Value);
            Assert.Equal(true, Named(made, "boxed").Value);
        }

        [Fact]
        public void ACSharpPluginsProperty_IsGivenItsStoredValue_WhenItLoads_AndAPluginNeverSetKeepsItsDefault()
        {
            var settings = new GlobalSettings
            {
                PluginSettings = new List<string> { "Walls/TextHeight=7.5", "Other/TextHeight=9" },
            };
            var store = new CadPluginSettingStore(() => settings);
            var stored = new Holder();
            var never = new Holder();

            store.Apply("Walls", new[] { Property(stored) });
            store.Apply("Floors", new[] { Property(never) });

            Assert.Equal(7.5, stored.TextHeight);
            Assert.Equal(2.5, never.TextHeight);
            Assert.Equal(new[] { "Walls/TextHeight=7.5", "Other/TextHeight=9" }, settings.PluginSettings);
        }

        [Fact]
        public void ACSharpPluginsPropertyNarrowedByANewVersion_DropsTheOldValue()
        {
            var settings = new GlobalSettings { PluginSettings = new List<string> { "Walls/TextHeight=2000" } };
            var store = new CadPluginSettingStore(() => settings);
            var holder = new Holder();

            store.Apply("Walls", new[] { Property(holder) });

            Assert.Equal(2.5, holder.TextHeight);
            Assert.Empty(settings.PluginSettings);
        }

        [Fact]
        public void SettingACSharpPluginsProperty_WritesTheValueToItAndKeepsIt()
        {
            var settings = new GlobalSettings();
            var store = new CadPluginSettingStore(() => settings);
            var holder = new Holder();
            CadPluginSetting height = Property(holder);

            store.Set("Walls", height, 4);

            Assert.Equal(4.0, holder.TextHeight);
            Assert.Equal(new[] { "Walls/TextHeight=4" }, settings.PluginSettings);
        }

        [Fact]
        public void TheStoreFollowsTheSettingsTheAppSwapsInForACopy()
        {
            var first = new GlobalSettings();
            GlobalSettings held = first;
            var store = new CadPluginSettingStore(() => held);
            IReadOnlyList<CadPluginSetting> made = store.Declare("Circles", CadPluginManifest.Parse(Declared).Settings);
            store.Set("Circles", Named(made, "start"), 5);

            held = first.Clone();
            store.Set("Circles", Named(made, "boxed"), true);

            Assert.Equal(new[] { "Circles/start=5" }, first.PluginSettings);
            Assert.Equal(new[] { "Circles/start=5", "Circles/boxed=True" }, held.PluginSettings);
            Assert.Equal(5, Named(made, "start").Value);
        }

        [Fact]
        public void ASetRaisesChanged_OnceItIsKept_AndASetRefusedDoesNot()
        {
            var settings = new GlobalSettings();
            IReadOnlyList<CadPluginSetting> made = Make(settings, out CadPluginSettingStore store);
            var seen = new List<int>();
            store.Changed += (sender, args) => seen.Add(settings.PluginSettings.Count);

            store.Set("Circles", Named(made, "start"), 5);
            Assert.Throws<ArgumentOutOfRangeException>(() => store.Set("Circles", Named(made, "textHeight"), 0.05));

            Assert.Equal(new[] { 1 }, seen);
        }

        [Fact]
        public void PluginSettings_NullSetsAnEmptyList_AClonesChangesLeaveTheOriginalAlone_AndTheValuesSurviveSavingAndLoading()
        {
            var settings = new GlobalSettings();
            Assert.Empty(settings.PluginSettings);
            settings.PluginSettings = null;
            Assert.NotNull(settings.PluginSettings);
            settings.PluginSettings = new List<string> { "Circles/textHeight=2.75", "Circles/note=a=b/c" };

            GlobalSettings copy = settings.Clone();
            copy.PluginSettings.Add("Bolts/diameter=20");
            copy.PluginSettings[0] = "Circles/textHeight=9";

            Assert.Equal(new[] { "Circles/textHeight=2.75", "Circles/note=a=b/c" }, settings.PluginSettings);
            Assert.Equal(3, copy.PluginSettings.Count);

            string folder = Path.Combine(Path.GetTempPath(), "gk-plugin-settings-" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(folder);
            string path = Path.Combine(folder, "settings.json");

            try
            {
                settings.Save(path);

                GlobalSettings loaded = GlobalSettings.Load(path);

                Assert.Equal(new[] { "Circles/textHeight=2.75", "Circles/note=a=b/c" }, loaded.PluginSettings);
            }
            finally
            {
                Directory.Delete(folder, true);
            }
        }

        [Fact]
        public void ASettingsFileMadeBeforePluginSettingsExisted_LoadsWithNone()
        {
            string folder = Path.Combine(Path.GetTempPath(), "gk-plugin-settings-" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(folder);
            string path = Path.Combine(folder, "settings.json");

            try
            {
                new GlobalSettings().Save(path);
                File.WriteAllText(path, File.ReadAllText(path).Replace("\"pluginSettings\"", "\"someOtherList\""));

                Assert.Empty(GlobalSettings.Load(path).PluginSettings);
            }
            finally
            {
                Directory.Delete(folder, true);
            }
        }
    }
}
