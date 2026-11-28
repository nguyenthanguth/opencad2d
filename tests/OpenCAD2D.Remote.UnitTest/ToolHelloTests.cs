using System;
using System.Diagnostics;
using System.Threading;
using GeometryHelper.Dxf;
using Xunit;

namespace OpenCAD2D.Remote.UnitTest
{
    /// <summary>
    /// A tool's first request on the channel of the tools, its hello, is answered with no dispatch (PW3-D7): a tool started while the
    /// window's thread waits for its first answer, as a point typed after the keyword of its snap does, could not otherwise start
    /// in time to give one.
    /// </summary>
    [Collection(ViewerSpeedCollection.Name)]
    public class ToolHelloTests
    {
        [Fact]
        [Trait("Category", "Timing")]
        public void AToolConnects_WhileTheWindowThreadIsHeld_WithNoWaitForIt()
        {
            using (var harness = new Harness())
            {
                var host = new TestHost();
                host.Add(CadDocument.From(new DxfDatabase("AC1027"), "A-101.dxf"));
                CadRemoteServer tools = harness.ServeTools(host);
                string token = tools.IssueToken();
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

                    // The connection makes its hello; a request that needs the window thread would wait here for the 5 seconds.
                    CadApplication app = harness.ConnectTool(tools, token);

                    Assert.True(
                        stopwatch.Elapsed < TimeSpan.FromSeconds(2.5),
                        "The hello waited for the window thread."
                    );
                    Assert.NotNull(app);
                }
                finally
                {
                    release.Set();
                    window.Join();
                }
            }
        }
    }
}
