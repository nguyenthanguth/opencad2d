using System;
using System.Diagnostics;
using System.IO;
using System.Linq;
using GeometryHelper.Dxf;
using Xunit;

namespace OpenCAD2D.Remote.UnitTest
{
    /// <summary>
    /// The private channel of the tools (S33): a second server of the app, on its own pipe, never announced, open whether or
    /// not "Allow other programs to connect" is on, and taking a client only with the token of a tool.
    /// </summary>
    public class RemoteToolsChannelTests
    {
        private static CadDocument Blank(string name) => CadDocument.From(new DxfDatabase("AC1027"), name);

        private static RemoteConnection Raw(CadRemoteServer server, string token) =>
            new RemoteConnection(server.PipeName, Process.GetCurrentProcess().Id, token);

        private static JsonObject Hello() => new JsonObject().Set("op", RemoteOps.Hello);

        [Fact]
        public void AHelloWithNoToken_OnTheToolsPipe_IsRefused()
        {
            using (var harness = new Harness())
            {
                var host = new TestHost();
                host.Add(Blank("A-101.dxf"));
                CadRemoteServer tools = harness.ServeTools(host);
                tools.IssueToken();

                using (RemoteConnection connection = Raw(tools, null))
                {
                    CadRemoteException refused = Assert.Throws<CadRemoteException>(() => connection.Request(Hello()));

                    Assert.Contains("token", refused.Message, StringComparison.Ordinal);
                }
            }
        }

        [Fact]
        public void AHelloWithAWrongOrRevokedToken_IsRefused_AndSoIsARequestBeforeTheHello()
        {
            using (var harness = new Harness())
            {
                var host = new TestHost();
                host.Add(Blank("A-101.dxf"));
                CadRemoteServer tools = harness.ServeTools(host);
                string token = tools.IssueToken();
                string revoked = tools.IssueToken();
                tools.RevokeToken(revoked);

                using (RemoteConnection wrong = Raw(tools, "not-the-token"))
                {
                    Assert.Throws<CadRemoteException>(() => wrong.Request(Hello()));
                }

                using (RemoteConnection gone = Raw(tools, revoked))
                {
                    Assert.Throws<CadRemoteException>(() => gone.Request(Hello()));
                }

                // The right token is taken: it is the token, not the pipe, that lets a client in.
                using (CadApplication app = new CadApplication(tools.PipeName, token))
                {
                    Assert.Equal("A-101.dxf", Assert.Single(app.Documents).Name);
                }
            }
        }

        [Fact]
        public void ATokenRevokedWhileAClientIsConnected_RefusesItsNextRequest()
        {
            using (var harness = new Harness())
            {
                var host = new TestHost();
                host.Add(Blank("A-101.dxf"));
                CadRemoteServer tools = harness.ServeTools(host);
                string token = tools.IssueToken();
                CadApplication app = harness.ConnectTool(tools, token);
                Assert.Single(app.Documents);

                tools.RevokeToken(token);

                Assert.Throws<CadRemoteException>(() => app.Documents.Count);
            }
        }

        [Fact]
        public void TheToolsPipe_IsNeverAnnounced_SoNoClientFindsItInTheFolder()
        {
            using (var harness = new Harness())
            {
                var host = new TestHost();
                host.Add(Blank("A-101.dxf"));
                CadRemoteServer tools = harness.ServeTools(host);
                tools.IssueToken();

                Assert.Empty(Directory.GetFiles(harness.Folder));
                Assert.Empty(CadApplication.GetRunning(harness.Folder));
                Assert.StartsWith(
                    "OpenCAD2D." + Process.GetCurrentProcess().Id + ".",
                    tools.PipeName,
                    StringComparison.Ordinal
                );
            }
        }

        [Fact]
        public void TheToolsPipe_IsOpenWhenTheAppsOwnChannelIsOff()
        {
            using (var harness = new Harness())
            {
                var host = new TestHost();
                host.Add(Blank("A-101.dxf"));
                CadRemoteServer own = harness.Serve(host);
                CadRemoteServer tools = harness.ServeTools(host);
                string token = tools.IssueToken();

                // "Allow other programs to connect" turned off stops the app's own server and takes its file away.
                own.Stop();

                Assert.Empty(CadApplication.GetRunning(harness.Folder));
                Assert.Throws<CadRemoteException>(() => harness.Connect());
                Assert.True(tools.IsRunning);
                CadApplication app = harness.ConnectTool(tools, token);
                Assert.Equal("A-101.dxf", Assert.Single(app.Documents).Name);
            }
        }

        [Fact]
        public void TheToolsServerOfTheApp_IsNamedByTheProcess_AndTakesNoClientWithoutAToken()
        {
            var host = new TestHost();
            host.Add(Blank("A-101.dxf"));

            using (CadRemoteServer tools = CadRemoteServer.ForTools(host, new InlineDispatcher(host.Gate)))
            {
                tools.Start();
                string token = tools.IssueToken();

                Assert.Equal("OpenCAD2D." + Process.GetCurrentProcess().Id + ".tools", tools.PipeName);

                using (RemoteConnection connection = Raw(tools, null))
                {
                    Assert.Throws<CadRemoteException>(() => connection.Request(Hello()));
                }

                using (var app = new CadApplication(tools.PipeName, token))
                {
                    Assert.Equal(Process.GetCurrentProcess().Id, app.Running.ProcessId);
                    Assert.Single(app.Documents);
                }
            }
        }

        [Fact]
        public void ATokenIs32RandomBytes_AndNoTwoAreTheSame()
        {
            using (var harness = new Harness())
            {
                CadRemoteServer tools = harness.ServeTools(new TestHost());

                string[] tokens = Enumerable.Range(0, 20).Select(_ => tools.IssueToken()).ToArray();

                Assert.Equal(tokens.Length, tokens.Distinct(StringComparer.Ordinal).Count());
                Assert.All(tokens, t => Assert.Equal(64, t.Length));
                Assert.All(tokens, t => Assert.Matches("^[0-9a-f]{64}$", t));
            }
        }

        [Fact]
        public void ARunCommand_CannotBeMadeOnTheAppsOwnServer()
        {
            using (var harness = new Harness())
            {
                CadRemoteServer own = harness.Serve(new TestHost());

                InvalidOperationException refused = Assert.Throws<InvalidOperationException>(() =>
                    own.CreateRunCommand("STAIRSPY", false, "token", null)
                );

                Assert.Contains("tools", refused.Message, StringComparison.Ordinal);
            }
        }
    }
}
