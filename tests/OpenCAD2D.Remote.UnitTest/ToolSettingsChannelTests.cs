using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Threading;
using GeometryHelper.Dxf;
using OpenCAD2D.Plugins;
using Xunit;

namespace OpenCAD2D.Remote.UnitTest
{
    /// <summary>
    /// A tool reads and writes the settings its plugin.json declares over the channel of the tools (PW3-D19): the ops
    /// <c>toolSetting</c> and <c>setToolSetting</c>, the token naming the tool, answered without the dispatcher and refused on
    /// the app's own channel.
    /// </summary>
    [Collection(ViewerSpeedCollection.Name)]
    public class ToolSettingsChannelTests
    {
        private const string Declared =
            "{ \"name\": \"Circles\", \"settings\": ["
            + " { \"name\": \"textHeight\", \"label\": \"Text height\", \"kind\": \"number\", \"default\": 2.5, \"minimum\": 0.1, \"maximum\": 1000 },"
            + " { \"name\": \"start\", \"kind\": \"integer\", \"default\": 1 },"
            + " { \"name\": \"boxed\", \"kind\": \"boolean\", \"default\": false },"
            + " { \"name\": \"side\", \"kind\": \"choice\", \"choices\": [\"Left\", \"Right\"], \"default\": \"Left\" } ] }";

        private const string Other =
            "{ \"name\": \"Bolts\", \"settings\": [ { \"name\": \"diameter\", \"kind\": \"number\", \"default\": 20 } ] }";

        private static CadDocument Blank(string name) => CadDocument.From(new DxfDatabase("AC1027"), name);

        private static CadPluginSettingStore Store(GlobalSettings settings) =>
            new CadPluginSettingStore(() => settings);

        /// <summary>A tools server with one tool, Circles, whose settings the store holds, and a client with its token.</summary>
        private static CadApplication Connect(
            Harness harness,
            TestHost host,
            CadPluginSettingStore store,
            string manifest,
            out CadRemoteServer tools,
            ICadRemoteDispatcher dispatcher = null
        )
        {
            tools = dispatcher == null ? harness.ServeTools(host) : harness.ServeTools(host, dispatcher);
            string token = tools.IssueToken();
            CadPluginManifest parsed = CadPluginManifest.Parse(manifest);
            tools.SetToolSettings(token, parsed.Name, store.Declare(parsed.Name, parsed.Settings));
            return harness.ConnectTool(tools, token);
        }

        private static TestHost OneDrawing()
        {
            var host = new TestHost();
            host.Add(Blank("A-101.dxf"));
            return host;
        }

        [Fact]
        public void AToolReadsItsSettings_ByTheirKinds_AndADefaultUntilOneIsSet()
        {
            using (var harness = new Harness())
            {
                CadApplication app = Connect(harness, OneDrawing(), Store(new GlobalSettings()), Declared, out _);

                Assert.Equal(2.5, Assert.IsType<double>(app.GetToolSetting("textHeight")));
                Assert.Equal(1, Assert.IsType<int>(app.GetToolSetting("start")));
                Assert.False(Assert.IsType<bool>(app.GetToolSetting("boxed")));
                Assert.Equal("Left", Assert.IsType<string>(app.GetToolSetting("side")));
            }
        }

        [Fact]
        public void AToolSetsASetting_AndTheStoreKeepsIt()
        {
            using (var harness = new Harness())
            {
                var settings = new GlobalSettings();
                CadApplication app = Connect(harness, OneDrawing(), Store(settings), Declared, out _);

                app.SetToolSetting("textHeight", 5.0);
                app.SetToolSetting("start", 7);
                app.SetToolSetting("boxed", true);
                app.SetToolSetting("side", "right");

                Assert.Equal(5.0, Assert.IsType<double>(app.GetToolSetting("textHeight")));
                Assert.Equal(7, app.GetToolSetting("start"));
                Assert.Equal("Right", app.GetToolSetting("side"));
                Assert.Equal(
                    new[] { "Circles/textHeight=5", "Circles/start=7", "Circles/boxed=True", "Circles/side=Right" },
                    settings.PluginSettings
                );
            }
        }

        [Fact]
        public void AToolReadsTheValueTheAppKeptBeforeItStarted()
        {
            using (var harness = new Harness())
            {
                var settings = new GlobalSettings { PluginSettings = new List<string> { "Circles/textHeight=0.75" } };
                CadApplication app = Connect(harness, OneDrawing(), Store(settings), Declared, out _);

                Assert.Equal(0.75, app.GetToolSetting("textHeight"));
            }
        }

        [Fact]
        public void ATextThatReadsAsTheKind_IsTaken_ByTheSettingsOwnRules()
        {
            using (var harness = new Harness())
            {
                CadApplication app = Connect(harness, OneDrawing(), Store(new GlobalSettings()), Declared, out _);

                app.SetToolSetting("textHeight", "3.5");

                Assert.Equal(3.5, app.GetToolSetting("textHeight"));
            }
        }

        [Fact]
        public void AToolSetsATextSetting_WhichIsKeptUnlessItIsTheDefault()
        {
            const string manifest =
                "{ \"name\": \"Circles\", \"settings\": [ { \"name\": \"title\", \"kind\": \"text\", \"default\": \"Plan\" } ] }";

            using (var harness = new Harness())
            {
                var settings = new GlobalSettings();
                CadApplication app = Connect(harness, OneDrawing(), Store(settings), manifest, out _);

                app.SetToolSetting("title", "Section");

                Assert.Equal("Section", app.GetToolSetting("title"));
                Assert.Equal(new[] { "Circles/title=Section" }, settings.PluginSettings);

                app.SetToolSetting("title", "Plan");

                Assert.Equal("Plan", app.GetToolSetting("title"));
                Assert.Empty(settings.PluginSettings);
            }
        }

        [Fact]
        public void AValueOutOfRange_IsRefused_InTheSettingsOwnWords_AndTheValueStays()
        {
            using (var harness = new Harness())
            {
                var settings = new GlobalSettings();
                CadApplication app = Connect(harness, OneDrawing(), Store(settings), Declared, out _);

                CadRemoteException refused = Assert.Throws<CadRemoteException>(() =>
                    app.SetToolSetting("textHeight", 0.05)
                );

                Assert.Equal("The setting Text height lies from 0.1 to 1000.", refused.Message);
                Assert.Equal(2.5, app.GetToolSetting("textHeight"));
                Assert.Empty(settings.PluginSettings);
            }
        }

        [Fact]
        public void AValueOfAnotherKind_IsRefused_InTheSettingsOwnWords()
        {
            using (var harness = new Harness())
            {
                CadApplication app = Connect(harness, OneDrawing(), Store(new GlobalSettings()), Declared, out _);

                Assert.Equal(
                    "The setting start is a whole number, not 1.5.",
                    Assert.Throws<CadRemoteException>(() => app.SetToolSetting("start", 1.5)).Message
                );
                Assert.Equal(
                    "The setting side is one of Left, Right, not Up.",
                    Assert.Throws<CadRemoteException>(() => app.SetToolSetting("side", "Up")).Message
                );
                Assert.Throws<ArgumentException>(() => app.SetToolSetting("start", new object()));
                Assert.Throws<ArgumentException>(() => app.SetToolSetting("textHeight", double.NaN));
            }
        }

        [Fact]
        public void ASettingTheToolDoesNotDeclare_IsUnknown_ToItsToken()
        {
            using (var harness = new Harness())
            {
                var settings = new GlobalSettings();
                CadPluginSettingStore store = Store(settings);
                CadApplication circles = Connect(harness, OneDrawing(), store, Declared, out CadRemoteServer tools);
                string bolts = tools.IssueToken();
                CadPluginManifest other = CadPluginManifest.Parse(Other);
                tools.SetToolSettings(bolts, other.Name, store.Declare(other.Name, other.Settings));
                CadApplication boltsApp = harness.ConnectTool(tools, bolts);

                Assert.Equal(
                    "The tool Circles declares no setting nonesuch.",
                    Assert.Throws<CadRemoteException>(() => circles.GetToolSetting("nonesuch")).Message
                );
                Assert.Equal(
                    "The tool Circles declares no setting diameter.",
                    Assert.Throws<CadRemoteException>(() => circles.GetToolSetting("diameter")).Message
                );
                Assert.Equal(
                    "The tool Bolts declares no setting textHeight.",
                    Assert.Throws<CadRemoteException>(() => boltsApp.SetToolSetting("textHeight", 3)).Message
                );
                Assert.Equal(20.0, boltsApp.GetToolSetting("diameter"));
            }
        }

        [Fact]
        [Trait("Category", "Timing")]
        public void TheSettingsOps_AreAnsweredWhileTheWindowThreadIsHeld()
        {
            using (var harness = new Harness())
            {
                TestHost host = OneDrawing();
                CadApplication app = Connect(harness, host, Store(new GlobalSettings()), Declared, out _);
                var held = new ManualResetEventSlim();
                var release = new ManualResetEventSlim();
                var window = new Thread(() =>
                {
                    lock (host.Gate)
                    {
                        held.Set();
                        release.Wait(TimeSpan.FromSeconds(5));
                    }
                });
                window.Start();

                try
                {
                    Assert.True(held.Wait(TimeSpan.FromSeconds(5)));
                    var stopwatch = Stopwatch.StartNew();

                    // A request that needs the window thread waits for it, here 5 seconds; the settings do not.
                    object read = app.GetToolSetting("textHeight");
                    app.SetToolSetting("textHeight", 4.5);
                    object written = app.GetToolSetting("textHeight");

                    Assert.True(
                        stopwatch.Elapsed < TimeSpan.FromSeconds(2.5),
                        "A setting waited for the window thread."
                    );
                    Assert.Equal(2.5, read);
                    Assert.Equal(4.5, written);
                }
                finally
                {
                    release.Set();
                    window.Join();
                }
            }
        }

        [Fact]
        public void TheSettingsOps_AreRefusedOnTheAppsOwnChannel()
        {
            using (var harness = new Harness())
            {
                var host = OneDrawing();
                harness.Serve(host);
                CadApplication app = harness.Connect();

                Assert.Equal(
                    "toolSetting works on the channel of the tools only.",
                    Assert.Throws<CadRemoteException>(() => app.GetToolSetting("textHeight")).Message
                );
                Assert.Contains(
                    "channel of the tools only",
                    Assert.Throws<CadRemoteException>(() => app.SetToolSetting("textHeight", 3)).Message,
                    StringComparison.Ordinal
                );
            }
        }
    }
}
