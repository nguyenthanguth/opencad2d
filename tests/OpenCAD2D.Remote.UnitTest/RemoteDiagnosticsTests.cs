using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using GeometryHelper.Dxf;
using GeometryHelper.Geometry;
using Xunit;

namespace OpenCAD2D.Remote.UnitTest
{
    /// <summary>
    /// What a program that debugs the app asks through Remote: the picture of its canvas, a drawing's command line and the app's
    /// faults. The host with a window is stood for by a host that answers from the test; one with none is the plain
    /// <see cref="TestHost"/>.
    /// </summary>
    public class RemoteDiagnosticsTests
    {
        /// <summary>A host that also has a canvas and a crash log, as the app's window has, answering from the test.</summary>
        private sealed class WindowHost : ICadRemoteHost, ICadRemoteDiagnostics
        {
            private readonly TestHost _inner;

            public WindowHost(TestHost inner)
            {
                _inner = inner;
            }

            public int AskedMaxSide { get; private set; } = -1;

            public Func<int, CadRemotePicture> Picture { get; set; } = side => null;

            public string CrashLogPath { get; set; }

            public CadRemotePicture CanvasPicture(int maxSide)
            {
                AskedMaxSide = maxSide;
                return Picture(maxSide);
            }

            public string Title => _inner.Title;

            public IReadOnlyList<CadDocument> Documents => _inner.Documents;

            public CadDocument ActiveDocument => _inner.ActiveDocument;

            public CadDocument Open(string path) => _inner.Open(path);

            public CadDocument New(string template) => _inner.New(template);

            public void Activate(CadDocument document) => _inner.Activate(document);

            public CadCommandSession SessionOf(CadDocument document) => _inner.SessionOf(document);

            public void Type(CadDocument document, string line) => _inner.Type(document, line);

            public void Save(CadDocument document) => _inner.Save(document);

            public void SaveAs(CadDocument document, string path, string version) =>
                _inner.SaveAs(document, path, version);

            public void Close(CadDocument document) => _inner.Close(document);
        }

        private sealed class Scene : IDisposable
        {
            public readonly TestHost Host = new TestHost();
            public readonly WindowHost Window;
            public readonly CadDocument Document;
            private readonly string _folder = Path.Combine(
                Path.GetTempPath(),
                "OpenCAD2D.RemoteTests",
                Guid.NewGuid().ToString("N")
            );
            private readonly CadRemoteServer _server;
            public readonly CadApplication App;

            public Scene(bool window)
            {
                var database = new DxfDatabase("AC1027");
                database.ModelSpace.AppendEntity(new DxfLine(new GeoPoint3(0, 0, 0), new GeoPoint3(100, 0, 0)));
                Document = Host.Add(CadDocument.From(database, "A.dxf"));
                Window = new WindowHost(Host);
                Directory.CreateDirectory(_folder);
                ICadRemoteHost served = window ? (ICadRemoteHost)Window : Host;
                _server = new CadRemoteServer(served, new InlineDispatcher(Host.Gate), _folder, "diag");
                _server.Start();
                App = new CadApplication(_folder);
            }

            public string Folder => _folder;

            public void Dispose()
            {
                App.Dispose();
                _server.Dispose();

                try
                {
                    Directory.Delete(_folder, true);
                }
                catch (IOException) { }
                catch (UnauthorizedAccessException) { }
            }
        }

        [Fact]
        public void TheScreenshot_IsThePictureTheWindowGives_AtTheSizeAskedFor_WithThePartOfTheDrawingItShows()
        {
            using (var scene = new Scene(true))
            {
                byte[] png = { 0x89, 0x50, 0x4E, 0x47, 1, 2, 3 };
                scene.Window.Picture = side => new CadRemotePicture(
                    png,
                    side == 0 ? 942 : side,
                    side == 0 ? 534 : side / 2
                );

                CadRemoteScreenshot full = scene.App.GetScreenshot();
                CadRemoteScreenshot small = scene.App.GetScreenshot(300);

                Assert.Equal(png, full.Png);
                Assert.Equal(942, full.Width);
                Assert.Equal(534, full.Height);
                Assert.Equal("A.dxf", full.Document);
                Assert.Equal(300, small.Width);
                Assert.Equal(150, small.Height);
                Assert.Equal(300, scene.Window.AskedMaxSide);

                // The part of the drawing shown is the view of the drawing's session: the same four numbers.
                DisplayBox view = scene.Host.OnAppThread(() =>
                    scene.Host.SessionOf(scene.Document).Editor.GetCurrentView()
                );
                Assert.Equal(new[] { view.MinX, view.MinY, view.MaxX, view.MaxY }, full.View);
            }
        }

        [Fact]
        public void TheScreenshot_IsRefusedInWords_WhereThereIsNoWindowNoCanvasOrNoDrawing()
        {
            using (var scene = new Scene(false))
            {
                var none = Assert.Throws<CadRemoteException>(() => scene.App.GetScreenshot());
                Assert.Contains("no window", none.Message);
            }

            using (var scene = new Scene(true))
            {
                var canvas = Assert.Throws<CadRemoteException>(() => scene.App.GetScreenshot());
                Assert.Contains("no drawing canvas", canvas.Message);
                Assert.Throws<ArgumentOutOfRangeException>(() => scene.App.GetScreenshot(-1));
                Assert.Throws<ArgumentOutOfRangeException>(() => scene.App.GetScreenshot(16385));
            }
        }

        [Fact]
        public void TheHistory_IsTheLastLinesOfTheCommandLine_WithWhetherACommandRunsAndItsPrompt()
        {
            using (var scene = new Scene(false))
            {
                CadRemoteDocument drawing = scene.App.Documents.Get("A.dxf");
                CadRemoteCommandResult done = drawing.SendCommand("LINE 0,0 30,40\n\n");
                Assert.True(done.Completed);

                CadRemoteHistory history = drawing.GetHistory(100);
                IReadOnlyList<string> session = scene.Host.OnAppThread(() =>
                    scene.Host.SessionOf(scene.Document).History.ToList()
                );

                Assert.Equal(session, history.Lines);
                Assert.Equal(session.Count, history.Total);
                Assert.False(history.IsRunning);
                Assert.Contains(history.Lines, l => l.Contains("LINE"));
                CadRemoteHistory last = drawing.GetHistory(1);
                Assert.Single(last.Lines);
                Assert.Equal(session.Last(), last.Lines[0]);

                // A command left waiting is running, and its prompt is the one the user sees.
                drawing.SendCommand("LINE 0,0", TimeSpan.FromMilliseconds(200));
                CadRemoteHistory waiting = drawing.GetHistory(5);
                Assert.True(waiting.IsRunning);
                Assert.Contains("point", waiting.Prompt, StringComparison.OrdinalIgnoreCase);
                Assert.Throws<ArgumentOutOfRangeException>(() => drawing.GetHistory(0));
                Assert.Throws<ArgumentOutOfRangeException>(() => drawing.GetHistory(1001));
            }
        }

        [Fact]
        public void TheErrors_AreTheCrashLogEntriesNewestFirst_AndTheRequestsTheAppCouldNotAnswer()
        {
            using (var scene = new Scene(true))
            {
                string log = Path.Combine(scene.Folder, "crash.log");
                File.WriteAllText(
                    log,
                    "==== 2026-11-26 10:00:00.000 +07:00 ====\nApp: x\nOS: y\nCaught by: A\nLast command: MOVE\nFatal: no\nSystem.Exception: one\n   at F()\n\n"
                        + "==== 2026-11-26 11:00:00.000 +07:00 ====\nApp: x\nOS: y\nCaught by: B\nLast command: (none)\nFatal: yes\nSystem.Exception: two\n   at G()\n\n"
                );
                scene.Window.CrashLogPath = log;
                scene.Window.Picture = side => throw new InvalidOperationException("the canvas is gone");
                Assert.Throws<CadRemoteException>(() => scene.App.GetScreenshot());

                CadRemoteErrors errors = scene.App.GetErrors(5);

                Assert.Equal(log, errors.CrashLogPath);
                Assert.Equal(2, errors.CrashLog.Count);
                Assert.Equal("B", errors.CrashLog[0].Source);
                Assert.True(errors.CrashLog[0].IsFatal);
                Assert.Equal(string.Empty, errors.CrashLog[0].Command);
                Assert.Equal("System.Exception: two", errors.CrashLog[0].Head[0]);
                Assert.Equal("MOVE", errors.CrashLog[1].Command);
                CadRemoteError request = Assert.Single(errors.Requests);
                Assert.Equal("request screenshot", request.Source);
                Assert.Contains("the canvas is gone", request.Head[0]);
                Assert.Single(scene.App.GetErrors(1).CrashLog);
                Assert.Throws<ArgumentOutOfRangeException>(() => scene.App.GetErrors(0));
            }
        }

        [Fact]
        public void TheErrors_OfAHostWithNoCrashLog_HaveNoLogAndNoEntries()
        {
            using (var scene = new Scene(false))
            {
                CadRemoteErrors errors = scene.App.GetErrors();

                Assert.Null(errors.CrashLogPath);
                Assert.Empty(errors.CrashLog);
                Assert.Empty(errors.Requests);
            }
        }

        [Fact]
        public void TheFailedRequestsKept_AreTheNewestFifty_NewestFirst()
        {
            using (var scene = new Scene(true))
            {
                int calls = 0;
                scene.Window.Picture = side => throw new InvalidOperationException("fault " + (++calls));

                for (int i = 0; i < 53; i++)
                {
                    Assert.Throws<CadRemoteException>(() => scene.App.GetScreenshot());
                }

                CadRemoteErrors errors = scene.App.GetErrors(1000);

                Assert.Equal(50, errors.Requests.Count);
                Assert.Contains("fault 53", errors.Requests[0].Head[0]);
                Assert.Contains("fault 4", errors.Requests[49].Head[0]);
            }
        }
    }
}
