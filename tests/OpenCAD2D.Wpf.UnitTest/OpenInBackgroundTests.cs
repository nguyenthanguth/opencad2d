using System;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Threading;
using GeometryHelper.Dwg;
using GeometryHelper.Dxf;
using Xunit;

namespace OpenCAD2D.Wpf.UnitTest
{
    /// <summary>
    /// A drawing opened in the background, as the app's window opens one: read and drawn on a worker thread, shown only once
    /// drawn, a command typed meanwhile not run, and Esc stopping it. Each runs on an STA thread whose dispatcher is the
    /// window's.
    /// </summary>
    public class OpenInBackgroundTests
    {
        [Fact]
        public void AFileOpenedInTheBackground_IsShownOnlyOnceDrawn_AndACommandTypedMeanwhileIsNotRun()
        {
            OnWindowThread(() =>
            {
                using (var folder = new TestFolder())
                {
                    string room = folder.Write("room.dxf", TestFiles.Room(out _, out _));
                    string other = folder.Write("other.dxf", TestFiles.Room(out _, out _));
                    var model = new MainViewModel();
                    Assert.True(model.Open(other));

                    Task<bool> opened = model.OpenAsync(room);

                    // Nothing is shown until the window's thread is free to show it.
                    Assert.False(opened.IsCompleted);
                    Assert.True(model.IsOpening);
                    Assert.Equal("other.dxf", model.Title);
                    Assert.Equal("Opening room.dxf: reading the file", model.OpeningText);
                    Assert.True(model.CancelOpeningCommand.CanExecute(null));

                    // Typed or started, a command waits for no drawing half drawn: it is not run, and says so.
                    model.Execute("LINE");
                    model.StartCommand("CIRCLE");

                    Assert.Equal(
                        "LINE not run: room.dxf is still opening. Run it again once it is shown, or press Esc to stop opening it.",
                        model.History[model.History.Count - 2]
                    );
                    Assert.StartsWith("CIRCLE not run: room.dxf is still opening.", model.History.Last());
                    Assert.False(model.Session.IsRunning);

                    Assert.True(Wait(opened));

                    Assert.False(model.IsOpening);
                    Assert.Equal(string.Empty, model.OpeningText);
                    Assert.False(model.CancelOpeningCommand.CanExecute(null));
                    Assert.Equal("room.dxf", model.Title);
                    Assert.Equal(
                        "Opened room.dxf: 3 entities, " + model.Layers.Count + " layers.",
                        model.History.Last()
                    );

                    // Shown, the drawing takes commands again.
                    model.Execute("LINE");
                    Assert.True(model.Session.IsRunning);
                }
            });
        }

        [Fact]
        public void EscWhileAFileOpensInTheBackground_StopsItAndLeavesTheDrawingShown()
        {
            OnWindowThread(() =>
            {
                using (var folder = new TestFolder())
                {
                    string room = folder.Write("room.dxf", TestFiles.Room(out _, out _));
                    string other = folder.Write("other.dxf", TestFiles.Room(out _, out _));
                    var model = new MainViewModel();
                    Assert.True(model.Open(other));

                    Task<bool> opened = model.OpenAsync(room);
                    model.Escape();

                    Assert.False(Wait(opened));
                    Assert.False(model.IsOpening);
                    Assert.Equal("other.dxf", model.Title);
                    Assert.Equal("Opening room.dxf stopped.", model.History.Last());

                    // Opened again, it opens.
                    Assert.True(Wait(model.OpenAsync(room)));
                    Assert.Equal("room.dxf", model.Title);
                }
            });
        }

        [Fact]
        public void OpenWithOpensInBackgroundSet_StartsTheOpeningAndReturns()
        {
            OnWindowThread(() =>
            {
                using (var folder = new TestFolder())
                {
                    string room = folder.Write("room.dxf", TestFiles.Room(out _, out _));
                    string other = folder.Write("other.dxf", TestFiles.Room(out _, out _));
                    var model = new MainViewModel { OpensInBackground = true };

                    Assert.True(model.Open(room));
                    Assert.True(model.IsOpening);
                    Assert.Null(model.Document);

                    // One drawing opens at a time; the same one asked again joins it, another waits its turn.
                    Task<bool> same = model.OpenAsync(room);
                    Assert.True(model.Open(other));
                    Assert.Equal("other.dxf will open once room.dxf is shown.", model.History.Last());

                    Assert.True(Wait(same));
                    Assert.Equal("room.dxf", model.Title);
                    WaitUntilIdle(model);
                    Assert.Equal("other.dxf", model.Title);
                }
            });
        }

        [Fact]
        public void SeveralDrawingsDroppedOnTheDrawingsListWhileOpeningInTheBackground_AllOpenInOrder_TheLastShown()
        {
            OnWindowThread(() =>
            {
                using (var folder = new TestFolder())
                {
                    string[] paths = Rooms(folder, "a.dxf", "b.dwg", "c.dxf");
                    var model = new MainViewModel { OpensInBackground = true };

                    model.DropFiles(paths, onDrawingList: true);
                    WaitUntilIdle(model);

                    Assert.Equal(new[] { "a.dxf", "b.dwg", "c.dxf" }, Opened(model));
                    Assert.Equal("c.dxf", model.Title);
                }
            });
        }

        [Fact]
        public void SeveralDrawingsDroppedWithNoneOpen_AllOpenInBackground_AsEachOwnDrawing()
        {
            OnWindowThread(() =>
            {
                using (var folder = new TestFolder())
                {
                    string[] paths = Rooms(folder, "a.dxf", "b.dxf");
                    var model = new MainViewModel { OpensInBackground = true };

                    // Dropped on the drawing area, where no drawing is open to insert in: they open.
                    model.DropFiles(paths);
                    WaitUntilIdle(model);

                    Assert.Equal(new[] { "a.dxf", "b.dxf" }, Opened(model));
                    Assert.Equal("b.dxf", model.Title);
                }
            });
        }

        [Fact]
        public void AFileThatCannotBeRead_AmongSeveralOpening_IsSaidAndTheRestStillOpen()
        {
            OnWindowThread(() =>
            {
                using (var folder = new TestFolder())
                {
                    string[] paths = Rooms(folder, "a.dxf", "c.dxf");
                    string broken = folder.WriteText("b.dwg", "this is not a drawing");
                    var model = new MainViewModel { OpensInBackground = true };

                    model.DropFiles(new[] { paths[0], broken, paths[1] }, onDrawingList: true);
                    WaitUntilIdle(model);

                    Assert.Equal(new[] { "a.dxf", "c.dxf" }, Opened(model));
                    Assert.Equal("c.dxf", model.Title);
                    Assert.Contains(model.History, l => l.StartsWith("Cannot open b.dwg", StringComparison.Ordinal));
                }
            });
        }

        [Fact]
        public void ADrawingOpenAlreadyAmongSeveralDropped_IsJustShown_NotOpenedAgain()
        {
            OnWindowThread(() =>
            {
                using (var folder = new TestFolder())
                {
                    string[] paths = Rooms(folder, "a.dxf", "b.dxf");
                    var model = new MainViewModel { OpensInBackground = true };
                    Assert.True(Wait(model.OpenAsync(paths[0])));

                    model.DropFiles(new[] { paths[1], paths[0] }, onDrawingList: true);
                    WaitUntilIdle(model);

                    Assert.Equal(new[] { "a.dxf", "b.dxf" }, Opened(model));
                    Assert.Single(model.History, l => l.StartsWith("Opened a.dxf", StringComparison.Ordinal));
                    Assert.Equal("a.dxf", model.Title);
                }
            });
        }

        [Fact]
        public void EscWhileSeveralFilesOpen_StopsTheOneOpeningAndTheOnesWaiting()
        {
            OnWindowThread(() =>
            {
                using (var folder = new TestFolder())
                {
                    string[] paths = Rooms(folder, "a.dxf", "b.dxf", "c.dxf");
                    var model = new MainViewModel { OpensInBackground = true };

                    model.DropFiles(paths, onDrawingList: true);
                    Assert.True(model.IsOpening);
                    model.Escape();
                    WaitUntilIdle(model);

                    Assert.Empty(Opened(model));
                    Assert.False(model.HasDocument);
                    Assert.Contains(model.History, l => l == "Opening a.dxf stopped.");
                    Assert.Contains(model.History, l => l == "Opening b.dxf stopped.");
                    Assert.Contains(model.History, l => l == "Opening c.dxf stopped.");
                }
            });
        }

        private static string[] Rooms(TestFolder folder, params string[] names)
        {
            var paths = new string[names.Length];

            for (int i = 0; i < names.Length; i++)
            {
                DxfDatabase room = TestFiles.Room(out _, out _);

                if (names[i].EndsWith(".dwg", StringComparison.Ordinal))
                {
                    paths[i] = System.IO.Path.Combine(folder.FullPath, names[i]);
                    DwgWriter.Write(room, paths[i]);
                }
                else
                {
                    paths[i] = folder.Write(names[i], room);
                }
            }

            return paths;
        }

        /// <summary>The titles of the drawings opened, in the order they were opened.</summary>
        private static string[] Opened(MainViewModel model) =>
            model
                .History.Where(l => l.StartsWith("Opened ", StringComparison.Ordinal))
                .Select(l => l.Substring("Opened ".Length, l.IndexOf(':') - "Opened ".Length))
                .ToArray();

        /// <summary>Lets the window's dispatcher run until no drawing is opening, a minute at most.</summary>
        private static void WaitUntilIdle(MainViewModel model)
        {
            DateTime end = DateTime.UtcNow.AddMinutes(1);

            while (model.IsOpening && DateTime.UtcNow < end)
            {
                var frame = new DispatcherFrame();
                Dispatcher.CurrentDispatcher.BeginInvoke(
                    DispatcherPriority.Background,
                    new Action(() => frame.Continue = false)
                );
                Dispatcher.PushFrame(frame);
                Thread.Sleep(5);
            }

            Assert.False(model.IsOpening, "The openings did not end within a minute.");
        }

        /// <summary>Runs a test on an STA thread, as the window's.</summary>
        private static void OnWindowThread(Action test)
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
                finally
                {
                    Dispatcher.CurrentDispatcher.InvokeShutdown();
                }
            });
            thread.SetApartmentState(ApartmentState.STA);
            thread.Start();
            thread.Join();
            Assert.Null(failure);
        }

        /// <summary>Lets the window's dispatcher run until an opening ends, a minute at most, and gives what it gave.</summary>
        private static bool Wait(Task<bool> opened)
        {
            var frame = new DispatcherFrame();
            opened.ContinueWith(_ => frame.Continue = false, TaskContinuationOptions.ExecuteSynchronously);
            var timeout = new DispatcherTimer(
                TimeSpan.FromMinutes(1),
                DispatcherPriority.Normal,
                (s, e) => frame.Continue = false,
                Dispatcher.CurrentDispatcher
            );
            Dispatcher.PushFrame(frame);
            timeout.Stop();
            Assert.True(opened.IsCompleted, "The opening did not end within a minute.");
            return opened.Result;
        }
    }
}
