using System;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Threading;
using GeometryHelper.Geometry;
using OpenCAD2D.Remote;
using SkiaSharp;
using Xunit;

namespace OpenCAD2D.Wpf.UnitTest
{
    /// <summary>
    /// What a program that debugs the app asks of its window through Remote: the picture of the drawing canvas and the crash log.
    /// The canvas is the app's own, offscreen on an STA thread with no window shown, answering a client on another thread through
    /// the window's dispatcher, as the real app answers.
    /// </summary>
    public class RemoteScreenshotTests
    {
        private const int Width = 800;
        private const int Height = 600;

        /// <summary>
        /// Runs a test on an STA thread with the room open in a view model and a canvas of 800 by 600 over it, the remote server
        /// serving the window's host from this thread, and the test's client on a thread of its own while this one pumps the
        /// dispatcher the server's requests are run on.
        /// </summary>
        private static void OnWindow(
            bool withCanvas,
            string crashLog,
            Action<MainViewModel, CadCanvas, Func<Func<CadApplication, object>, object>> test
        )
        {
            Exception failure = null;
            var thread = new Thread(() =>
            {
                try
                {
                    using (var folder = new TestFolder())
                    {
                        var model = new MainViewModel();
                        model.Open(folder.Write("room.dxf", TestFiles.Room(out _, out _)));
                        var canvas = new CadCanvas
                        {
                            Document = model.Document,
                            Space = model.Space,
                            View = model.View,
                            Session = model.Session,
                            Snap = model.Snap,
                        };
                        canvas.PixelSizeChanged += (s, e) => model.SetCanvasSize(canvas.PixelWidth, canvas.PixelHeight);
                        canvas.Measure(new Size(Width, Height));
                        canvas.Arrange(new Rect(0, 0, Width, Height));
                        canvas.RenderNow();
                        var host = new CadAppRemoteHost(
                            model,
                            () => "room.dxf - OpenCAD2D",
                            Dispatcher.CurrentDispatcher,
                            withCanvas ? (Func<CadCanvas>)(() => canvas) : () => null,
                            crashLog
                        );
                        string remote = Path.Combine(folder.FullPath, "remote");

                        using (var server = new CadRemoteServer(host, host, remote, "shot"))
                        {
                            server.Start();

                            // The client runs where it can wait; this thread is the window's and answers while it waits.
                            test(model, canvas, ask =>
                            {
                                object answer = null;
                                Exception error = null;
                                var frame = new DispatcherFrame();
                                Task.Run(() =>
                                {
                                    try
                                    {
                                        using (var app = new CadApplication(remote))
                                        {
                                            answer = ask(app);
                                        }
                                    }
                                    catch (Exception ex)
                                    {
                                        error = ex;
                                    }
                                    finally
                                    {
                                        frame.Continue = false;
                                    }
                                });
                                Dispatcher.PushFrame(frame);

                                if (error != null)
                                {
                                    throw error;
                                }

                                return answer;
                            });
                        }

                        canvas.Dispose();
                    }
                }
                catch (Exception ex)
                {
                    failure = ex;
                }
            });
            thread.SetApartmentState(ApartmentState.STA);
            thread.Start();
            thread.Join();
            Assert.Null(failure);
        }

        [Fact]
        public void TheScreenshot_IsTheCanvasBitmapAsLastDrawn_WithTheViewTheCanvasShows_AndNothingIsDrawnAgain()
        {
            OnWindow(true, null, (model, canvas, ask) =>
            {
                byte[] own = canvas.BitmapPng();
                int drawn = canvas.SceneRenders;

                var shot = (CadRemoteScreenshot)ask(app => app.GetScreenshot());

                Assert.Equal(own, shot.Png);
                Assert.Equal(canvas.PixelWidth, shot.Width);
                Assert.Equal(canvas.PixelHeight, shot.Height);
                Assert.Equal("room.dxf", shot.Document);
                DisplayBox view = model.Session.Editor.GetCurrentView();
                Assert.Equal(new[] { view.MinX, view.MinY, view.MaxX, view.MaxY }, shot.View);
                Assert.Equal(drawn, canvas.SceneRenders);
            });
        }

        [Fact]
        public void TheScreenshot_AtAMaxSide_KeepsTheShapeAndIsAPictureOfTheSameCanvas()
        {
            OnWindow(true, null, (model, canvas, ask) =>
            {
                var small = (CadRemoteScreenshot)ask(app => app.GetScreenshot(200));
                var large = (CadRemoteScreenshot)ask(app => app.GetScreenshot(5000));

                Assert.Equal(200, Math.Max(small.Width, small.Height));
                Assert.Equal((double)canvas.PixelWidth / canvas.PixelHeight, (double)small.Width / small.Height, 1);
                using (SKBitmap picture = SKBitmap.Decode(small.Png))
                {
                    Assert.Equal(small.Width, picture.Width);
                    Assert.Equal(small.Height, picture.Height);
                }

                // A side the canvas is under leaves the picture as it is.
                Assert.Equal(canvas.PixelWidth, large.Width);
                Assert.Equal(canvas.BitmapPng(), large.Png);
            });
        }

        [Fact]
        public void TheScreenshot_OfTheCanvasShown_IsThePictureOfTheDrawingAtTheViewItGives()
        {
            OnWindow(true, null, (model, canvas, ask) =>
            {
                var shot = (CadRemoteScreenshot)ask(app => app.GetScreenshot());

                // The facade draws the same part of the same drawing at the canvas's size: what the user sees is what the drawing holds.
                CadRenderImage drawn = CadRender.Draw(
                    model.Document,
                    new CadRenderOptions
                    {
                        Layout = "Model",
                        Box = new DisplayBox(shot.View[0], shot.View[1], shot.View[2], shot.View[3]),
                        Width = shot.Width,
                        Height = shot.Height,
                    }
                );

                using (SKBitmap seen = SKBitmap.Decode(shot.Png))
                {
                    Assert.Equal(seen.Width, drawn.Width);
                    Assert.Equal(seen.Height, drawn.Height);
                    int different = 0;

                    for (int y = 0; y < seen.Height; y++)
                    {
                        for (int x = 0; x < seen.Width; x++)
                        {
                            SKColor c = seen.GetPixel(x, y);
                            int i = 3 * (y * drawn.Width + x);

                            if (
                                Math.Abs(c.Red - drawn.Rgb[i]) > 2
                                || Math.Abs(c.Green - drawn.Rgb[i + 1]) > 2
                                || Math.Abs(c.Blue - drawn.Rgb[i + 2]) > 2
                            )
                            {
                                different++;
                            }
                        }
                    }

                    // The canvas draws its grid and the facade does not: the drawing's own pixels are the same.
                    Assert.True(
                        different < seen.Width * seen.Height / 20,
                        different + " of " + seen.Width * seen.Height + " pixels differ."
                    );
                }
            });
        }

        [Fact]
        public void TheScreenshot_WhereTheWindowHasNoCanvas_IsRefusedInWords()
        {
            OnWindow(false, null, (model, canvas, ask) =>
            {
                CadRemoteException refused = Assert.Throws<CadRemoteException>(() => ask(app => app.GetScreenshot()));

                Assert.Contains("no drawing canvas", refused.Message);
            });
        }

        [Fact]
        public void TheErrors_AreTheCrashLogTheAppWrote_WithTheCommandThatRan()
        {
            string log = Path.Combine(
                Path.GetTempPath(),
                "OpenCAD2D.WpfTests",
                Guid.NewGuid().ToString("N"),
                "crash.log"
            );

            try
            {
                CadCrashLog.Write(
                    log,
                    new InvalidOperationException("the plate broke"),
                    "Application.DispatcherUnhandledException",
                    "MOVE"
                );
                CadCrashLog.Write(
                    log,
                    new OutOfMemoryException("no more"),
                    "AppDomain.UnhandledException (terminating: True)",
                    null
                );

                OnWindow(true, log, (model, canvas, ask) =>
                {
                    var errors = (CadRemoteErrors)ask(app => app.GetErrors(5));

                    Assert.Equal(log, errors.CrashLogPath);
                    Assert.Equal(2, errors.CrashLog.Count);
                    Assert.StartsWith("AppDomain.UnhandledException", errors.CrashLog[0].Source);
                    Assert.True(errors.CrashLog[0].IsFatal);
                    Assert.Equal(string.Empty, errors.CrashLog[0].Command);
                    Assert.Equal("Application.DispatcherUnhandledException", errors.CrashLog[1].Source);
                    Assert.Equal("MOVE", errors.CrashLog[1].Command);
                    Assert.False(errors.CrashLog[1].IsFatal);
                    Assert.Contains(
                        "the plate broke",
                        errors.CrashLog[1].Head.First(l => l.Contains("InvalidOperation"))
                    );
                });
            }
            finally
            {
                try
                {
                    Directory.Delete(Path.GetDirectoryName(log), true);
                }
                catch (IOException) { }
                catch (UnauthorizedAccessException) { }
            }
        }

        [Fact]
        public void TheAppsHost_NamesTheCrashLogOfTheApp_UnlessAnotherIsGiven_AndAHostWithNoCanvasHasNoPicture()
        {
            var model = new MainViewModel();

            var own = new CadAppRemoteHost(model, () => "OpenCAD2D", Dispatcher.CurrentDispatcher);
            var other = new CadAppRemoteHost(
                model,
                () => "OpenCAD2D",
                Dispatcher.CurrentDispatcher,
                null,
                "elsewhere.log"
            );

            Assert.Equal(CadCrashLog.DefaultPath, own.CrashLogPath);
            Assert.Equal("elsewhere.log", other.CrashLogPath);
            Assert.Null(own.CanvasPicture(0));
        }
    }
}
