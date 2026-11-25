using System;
using System.Threading;
using GeometryHelper.Dxf;
using Xunit;

namespace OpenCAD2D.Remote.UnitTest
{
    public class RemoteSelfConnectionTests
    {
        private const string Refusal =
            "A script inside OpenCAD2D cannot drive the same app through CadApplication: use opencad2d.cad, which reaches the drawing directly.";

        /// <summary>A dispatcher that owns the thread that made it, as the window's does: the thread a script of the app runs on.</summary>
        private sealed class OwnedDispatcher : ICadRemoteDispatcher, ICadRemoteOwnerThread
        {
            private readonly Thread _owner = Thread.CurrentThread;
            private readonly InlineDispatcher _inline;

            public OwnedDispatcher(object gate) => _inline = new InlineDispatcher(gate);

            public bool IsOwnerThread() => Thread.CurrentThread == _owner;

            public void Invoke(Action action) => _inline.Invoke(action);
        }

        /// <summary>
        /// Runs a function on a thread of its own and gives its result. Task.Run would not do: a test runs on a pool thread that goes
        /// back to the pool at its first await, and the pool can hand it the work, the thread the dispatcher owns.
        /// </summary>
        private static T OnAnotherThread<T>(Func<T> function)
        {
            T result = default;
            Exception failure = null;
            var thread = new Thread(() =>
            {
                try
                {
                    result = function();
                }
                catch (Exception ex)
                {
                    failure = ex;
                }
            });
            thread.Start();
            thread.Join();

            if (failure != null)
            {
                throw new InvalidOperationException("The other thread failed.", failure);
            }

            return result;
        }

        private static TestHost HostWithOneDrawing()
        {
            var host = new TestHost();
            host.Add(CadDocument.From(new DxfDatabase("AC1027"), "A-101.dxf"));
            return host;
        }

        [Fact]
        public void ACallerOnTheThreadTheAppOwns_IsRefusedAtOnce_NotLeftWaitingForItself()
        {
            using (var harness = new Harness())
            {
                TestHost host = HostWithOneDrawing();
                harness.Serve(host, new OwnedDispatcher(host.Gate));

                // The test's thread is the app's thread: a request to the drawings from here would wait for itself for ever.
                CadRemoteException refused = Assert.Throws<CadRemoteException>(() => harness.Connect());

                Assert.Equal(Refusal, refused.Message);
            }
        }

        [Fact]
        public void ACallerOnAnotherThreadOfTheSameProcess_StillConnects()
        {
            using (var harness = new Harness())
            {
                TestHost host = HostWithOneDrawing();
                harness.Serve(host, new OwnedDispatcher(host.Gate));

                int open = OnAnotherThread(() => harness.Connect().Documents.Count);

                Assert.Equal(1, open);
            }
        }

        [Fact]
        public void AServerWhoseDispatcherSaysNothingOfItsThread_RefusesNobody()
        {
            using (var harness = new Harness())
            {
                TestHost host = HostWithOneDrawing();
                harness.Serve(host);

                Assert.Equal(1, harness.Connect().Documents.Count);
            }
        }

        [Fact]
        public void AServerStopped_NoLongerRefusesItsOwnThread()
        {
            using (var harness = new Harness())
            {
                TestHost host = HostWithOneDrawing();
                CadRemoteServer server = harness.Serve(host, new OwnedDispatcher(host.Gate));
                CadRunningApp running = Assert.Single(CadApplication.GetRunning(harness.Folder));
                server.Stop();

                // The pipe is gone, so the request fails as any request to a closed app does, with the connection's words.
                CadRemoteException closed = Assert.Throws<CadRemoteException>(() => harness.Connect(running));

                Assert.DoesNotContain("cannot drive the same app", closed.Message, StringComparison.Ordinal);
            }
        }
    }
}
