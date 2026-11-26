using System;
using System.IO;
using System.Linq;
using System.Threading;
using GeometryHelper.Dxf;
using GeometryHelper.Geometry;
using OpenCAD2D.Remote;
using Xunit;

namespace OpenCAD2D.Remote.UnitTest
{
    public class RemoteDisposeFailedEditAndTitleTests
    {
        private static DxfLine Line(double x1, double y1, double x2, double y2) =>
            new DxfLine(new GeoPoint3(x1, y1, 0), new GeoPoint3(x2, y2, 0));

        [Fact]
        public void AFailedCommitInARun_SaysSoOnTheCommandLine_WhenTheToolCatchesTheError()
        {
            using (var harness = new Harness())
            {
                var host = new TestHost();
                var db = new DxfDatabase("AC1027");
                db.ModelSpace.AppendEntity(Line(0, 0, 100, 0));
                CadDocument document = host.Add(CadDocument.From(db, "A-101.dxf"));
                CadCommandSession session = host.SessionOf(document);
                CadRemoteServer tools = harness.ServeTools(host);
                string token = tools.IssueToken();
                CadRemoteToolRun run = null;
                CadCommand command = tools.CreateRunCommand("STAIRSPY", false, token, r => run = r);
                Assert.True(host.OnAppThread(() => session.Begin(command)));
                CadApplication client = harness.ConnectTool(tools, token);
                client.JoinRun(run.Id);
                CadRemoteDocument drawing = client.Documents.First(d => d.Name == document.Title);

                drawing.Insert(Line(0, 100, 100, 100));
                drawing.CommitChanges();
                Assert.Equal(2, host.OnAppThread(() => document.Database.ModelSpace.Count(e => !e.IsErased)));

                string handle = db.ModelSpace.First().Handle;
                drawing.Delete(handle);
                drawing.Delete(handle);
                CadRemoteException failed = Assert.Throws<CadRemoteException>(() => drawing.CommitChanges());

                Assert.Equal(1, host.OnAppThread(() => document.Database.ModelSpace.Count(e => !e.IsErased)));
                string[] history = host.OnAppThread(() => session.History.ToArray());
                Assert.Contains(history, h => h.StartsWith("STAIRSPY: an edit failed", StringComparison.Ordinal));
            }
        }

        [Fact]
        public void ACadApplication_UsedAfterDispose_RaisesObjectDisposed_AndOpensNoPipe()
        {
            using (var harness = new Harness())
            {
                var host = new TestHost();
                host.Add(CadDocument.From(new DxfDatabase("AC1027"), "A.dxf"));
                harness.Serve(host);
                CadApplication app = harness.Connect();
                CadRemoteDocument document = app.Documents.Single();
                Assert.Equal("A.dxf", document.Name);

                app.Dispose();

                ObjectDisposedException closed = Assert.Throws<ObjectDisposedException>(() => app.Documents.ToList());
                Assert.Contains("make a new CadApplication", closed.Message, StringComparison.Ordinal);
                Assert.Throws<ObjectDisposedException>(() => document.GetObjects());
                app.Dispose();
            }
        }

        [Fact]
        public void TheAnnouncedTitle_FollowsTheHost_WithoutMovingTheTimeOfTheLastActivation()
        {
            using (var harness = new Harness())
            {
                var host = new TestHost { Title = "OpenCAD2D  " };
                CadRemoteServer server = harness.Serve(host);
                CadRunningApp before = Assert.Single(CadApplication.GetRunning(harness.Folder));
                Assert.Equal("OpenCAD2D  ", before.Title);
                Thread.Sleep(30);

                host.Title = "OpenCAD2D  plan.dxf";
                server.NoteTitleChanged();

                CadRunningApp after = Assert.Single(CadApplication.GetRunning(harness.Folder));
                Assert.Equal("OpenCAD2D  plan.dxf", after.Title);
                Assert.Equal(before.Instance.LastActivated, after.Instance.LastActivated);
            }
        }
    }
}
