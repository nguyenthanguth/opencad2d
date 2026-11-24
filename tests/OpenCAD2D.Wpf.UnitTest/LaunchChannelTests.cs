using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.IO.Pipes;
using System.Linq;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Xunit;

namespace OpenCAD2D.Wpf.UnitTest
{
    /// <summary>
    /// A start with a file handing it to the running app: the channel they share (a pipe named for the app and the session)
    /// and the queue that holds the paths until the window can open them. No window and no Application is made, and no pipe
    /// has the name of the real app: each test names its own.
    /// </summary>
    public sealed class LaunchChannelTests
    {
        private static string NewName() => "OpenCAD2D.Test.Open." + Guid.NewGuid().ToString("N");

        [Fact]
        public void AStartWithAFile_ForwardsItsFullPath_AndAStartWithoutOne_ForwardsNothing()
        {
            Assert.Null(CadLaunchChannel.FileToForward(null));
            Assert.Null(CadLaunchChannel.FileToForward(new string[0]));
            Assert.Null(CadLaunchChannel.FileToForward(new[] { "--screenshot", "o.png", "a.dxf" }));
            Assert.Null(CadLaunchChannel.FileToForward(new[] { "--register" }));
            Assert.Null(CadLaunchChannel.FileToForward(new[] { "--UNREGISTER" }));
            Assert.Null(CadLaunchChannel.FileToForward(new[] { "--other" }));
            Assert.Equal(Path.GetFullPath("a.dxf"), CadLaunchChannel.FileToForward(new[] { "a.dxf" }));
            Assert.Equal(@"D:\p\a.dwg", CadLaunchChannel.FileToForward(new[] { @"D:\p\a.dwg", "b.dxf" }));
            Assert.Equal(@"D:\p\a.dwg", CadLaunchChannel.FileToForward(new[] { "--x", @"D:\p\a.dwg" }));
        }

        [Fact]
        public void AStartWithSeveralFiles_ForwardsEveryOne_InTheOrderOfTheCommandLine()
        {
            Assert.Empty(CadLaunchChannel.FilesToForward(null));
            Assert.Empty(CadLaunchChannel.FilesToForward(new[] { "--register", "a.dxf" }));
            Assert.Equal(
                new[] { @"D:\p\a.dwg", @"D:\p\b.dwg", Path.GetFullPath("c.dxf") },
                CadLaunchChannel.FilesToForward(new[] { @"D:\p\a.dwg", "--x", @"D:\p\b.dwg", "c.dxf" })
            );
        }

        [Fact]
        public void TheChannelIsKeyedByAppAndSession()
        {
            Assert.Equal(
                "OpenCAD2D.Open." + Process.GetCurrentProcess().SessionId,
                CadLaunchChannel.NameFor("OpenCAD2D")
            );
        }

        [Fact]
        public void TheFirstClaimOwnsTheName_ASecondIsRefused_AndTheNameIsFreeOnceTheOwnerStops()
        {
            string name = NewName();
            CadLaunchChannel a = CadLaunchChannel.TryClaim(name, p => { });
            Assert.NotNull(a);

            using (CadLaunchChannel b = CadLaunchChannel.TryClaim(name, p => { }))
            {
                Assert.Null(b);
            }

            a.Dispose();
            a.Dispose();

            using (CadLaunchChannel c = CadLaunchChannel.TryClaim(name, p => { }))
            {
                Assert.NotNull(c);
            }
        }

        [Fact]
        public void AForwardedPath_ReachesTheOwner_AndTheSenderIsToldOk()
        {
            string name = NewName();
            var sink = new BlockingCollection<string>();

            using (CadLaunchChannel owner = CadLaunchChannel.TryClaim(name, sink.Add))
            {
                Assert.NotNull(owner);
                Assert.True(CadLaunchChannel.TryForward(name, "D:\\p\\\u00e9 a.dxf", 1000, 3000));
                Assert.True(sink.TryTake(out string got, 5000));
                Assert.Equal("D:\\p\\\u00e9 a.dxf", got);
            }
        }

        [Fact]
        public void NoOwner_ForwardsNothing()
        {
            Assert.False(CadLaunchChannel.TryForward(NewName(), @"D:\a.dxf", 200, 200));
        }

        [Fact]
        public void AnOwnerThatNeverAnswers_IsGivenUpOn_AndTheStartGoesOn()
        {
            string name = NewName();

            using (var silent = new NamedPipeServerStream(name, PipeDirection.InOut, 1))
            {
                var accepted = new Thread(() => silent.WaitForConnection());
                accepted.Start();
                var watch = Stopwatch.StartNew();
                Assert.False(CadLaunchChannel.TryForward(name, @"D:\a.dxf", 500, 500));
                watch.Stop();
                Assert.True(watch.ElapsedMilliseconds < 10000);
                Assert.True(accepted.Join(5000));
            }
        }

        [Fact]
        public void AClientThatNeverWrites_DoesNotHoldTheOwnerBack()
        {
            string name = NewName();
            var sink = new BlockingCollection<string>();

            using (CadLaunchChannel owner = CadLaunchChannel.TryClaim(name, sink.Add))
            using (var idle = new NamedPipeClientStream(".", name, PipeDirection.InOut))
            {
                idle.Connect(1000);
                Assert.True(CadLaunchChannel.TryForward(name, @"D:\a.dxf", 1000, 3000));
                Assert.True(sink.TryTake(out string got, 5000));
                Assert.Equal(@"D:\a.dxf", got);
            }
        }

        [Fact]
        public void EightStartsAtTheSameInstant_GiveOneOwner_AndTheOtherSevenPathsReachIt()
        {
            string name = NewName();
            var sink = new BlockingCollection<string>();
            var owners = new ConcurrentBag<CadLaunchChannel>();
            var forwarded = new ConcurrentBag<bool>();

            using (var barrier = new Barrier(8))
            {
                Thread[] starts = Enumerable
                    .Range(0, 8)
                    .Select(i => new Thread(() =>
                    {
                        barrier.SignalAndWait();
                        CadLaunchChannel claimed = CadLaunchChannel.TryClaim(name, sink.Add);

                        if (claimed != null)
                        {
                            owners.Add(claimed);
                            return;
                        }

                        forwarded.Add(CadLaunchChannel.TryForward(name, @"D:\p\" + i + ".dxf", 1000, 3000));
                    }))
                    .ToArray();

                foreach (Thread start in starts)
                {
                    start.Start();
                }

                foreach (Thread start in starts)
                {
                    Assert.True(start.Join(30000));
                }
            }

            try
            {
                Assert.Single(owners);
                Assert.Equal(7, forwarded.Count);
                Assert.All(forwarded, ok => Assert.True(ok));

                var paths = new List<string>();

                while (paths.Count < 7 && sink.TryTake(out string got, 5000))
                {
                    paths.Add(got);
                }

                Assert.Equal(7, paths.Distinct().Count());
            }
            finally
            {
                foreach (CadLaunchChannel owner in owners)
                {
                    owner.Dispose();
                }
            }
        }

        [Fact]
        public void AFirstLineItDoesNotKnow_IsAnsweredNo()
        {
            string name = NewName();
            var sink = new BlockingCollection<string>();

            using (CadLaunchChannel owner = CadLaunchChannel.TryClaim(name, sink.Add))
            using (var client = new NamedPipeClientStream(".", name, PipeDirection.InOut))
            {
                client.Connect(1000);
                byte[] bytes = new UTF8Encoding(false).GetBytes("OPEN 99\nx\n");
                client.Write(bytes, 0, bytes.Length);
                client.Flush();
                var reply = new byte[16];
                int count = client.Read(reply, 0, reply.Length);
                Assert.Equal("NO\n", Encoding.UTF8.GetString(reply, 0, count));
                Assert.False(sink.TryTake(out _, 300));
            }
        }

        [Fact]
        public void AnOwnerThatAnswersNo_IsGivenUpOn()
        {
            string name = NewName();

            using (var old = new NamedPipeServerStream(name, PipeDirection.InOut, 1))
            {
                var answered = new Thread(() =>
                {
                    old.WaitForConnection();
                    var buffer = new byte[64];
                    old.Read(buffer, 0, buffer.Length);
                    byte[] no = Encoding.UTF8.GetBytes("NO\n");
                    old.Write(no, 0, no.Length);
                    old.Flush();
                    old.WaitForPipeDrain();
                });
                answered.Start();
                Assert.False(CadLaunchChannel.TryForward(name, @"D:\a.dxf", 1000, 3000));
                Assert.True(answered.Join(5000));
            }
        }

        [Fact]
        public void ForwardedFiles_WaitWhileTheAppCannotOpen_AndOpenInOrderOnceItCan()
        {
            bool can = false;
            var opened = new List<string>();
            var files = new CadForwardedFiles(() => can, opened.Add);

            files.Add("a");
            files.Add("b");
            Assert.Empty(opened);
            Assert.Equal(2, files.Waiting);

            can = true;
            files.Retry();
            Assert.Equal(new[] { "a", "b" }, opened);
            Assert.Equal(0, files.Waiting);

            files.Add("c");
            Assert.Equal(new[] { "a", "b", "c" }, opened);
        }

        [Fact]
        public void AnOpeningThatStarts_HoldsTheNextFile_UntilItEnds()
        {
            bool opening = false;
            var opened = new List<string>();
            var files = new CadForwardedFiles(
                () => !opening,
                path =>
                {
                    opened.Add(path);
                    opening = true;
                }
            );

            files.Add("a");
            files.Add("b");
            Assert.Equal(new[] { "a" }, opened);
            Assert.Equal(1, files.Waiting);

            files.Retry();
            Assert.Equal(new[] { "a" }, opened);

            opening = false;
            files.Retry();
            Assert.Equal(new[] { "a", "b" }, opened);
            Assert.Equal(0, files.Waiting);
        }
    }
}
