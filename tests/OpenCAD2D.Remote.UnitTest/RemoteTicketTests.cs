using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Threading;
using GeometryHelper.Dxf;
using GeometryHelper.Geometry;
using Xunit;
using Xunit.Abstractions;

namespace OpenCAD2D.Remote.UnitTest
{
    /// <summary>
    /// The handles of the objects a commit inserted, which the app's answer carries: <see cref="CadRemoteDocument.HandleOfTicket"/>
    /// gives them for the tickets <see cref="CadRemoteDocument.Insert"/> returned, after an edit call as after
    /// <see cref="CadRemoteDocument.CommitChanges"/>, with no request of its own. The requests are counted by a dispatcher that
    /// runs the server's work as the test host does and counts the pieces of it.
    /// </summary>
    public class RemoteTicketTests
    {
        private readonly ITestOutputHelper _output;

        public RemoteTicketTests(ITestOutputHelper output)
        {
            _output = output;
        }

        private sealed class CountingDispatcher : ICadRemoteDispatcher
        {
            private readonly InlineDispatcher _inner;
            private int _count;

            public CountingDispatcher(object gate)
            {
                _inner = new InlineDispatcher(gate);
            }

            public int Count => Volatile.Read(ref _count);

            public void Invoke(Action action)
            {
                Interlocked.Increment(ref _count);
                _inner.Invoke(action);
            }
        }

        private sealed class Scene : IDisposable
        {
            public readonly Harness Harness = new Harness();
            public readonly CountingDispatcher Requests;
            public readonly CadDocument Served;
            public readonly CadRemoteDocument Remote;
            public readonly DxfLine Line;

            public Scene()
            {
                var host = new TestHost();
                Served = host.Add(CadDocument.From(new DxfDatabase("AC1027"), "A-101.dxf"));
                Served.Database.ModelSpace.AppendEntity(new DxfLine(new GeoPoint3(0, 0, 0), new GeoPoint3(100, 0, 0)));
                Requests = new CountingDispatcher(host.Gate);
                Harness.Serve(host, Requests);
                Remote = Harness.Connect().Documents.Single();
                Line = (DxfLine)Remote.GetObjects().Single();
            }

            public void Dispose() => Harness.Dispose();
        }

        private static DxfLine Made(int i) => new DxfLine(new GeoPoint3(i, 10, 0), new GeoPoint3(i, 20, 0));

        [Fact]
        public void TheTicketsOfInsertsSentWithACopy_GiveTheirHandlesWithNoRequestOfTheirOwn()
        {
            using (var scene = new Scene())
            {
                var tickets = new List<string>();

                for (int i = 0; i < 25; i++)
                {
                    tickets.Add(scene.Remote.Insert(Made(i)));
                }

                IReadOnlyList<DxfLine> copies = scene.Remote.Copy(new[] { scene.Line }, new GeoVector2(0, 50));
                int afterCommit = scene.Requests.Count;
                string[] handles = tickets.Select(t => scene.Remote.HandleOfTicket(t)).ToArray();

                // The handles are those of the 25 lines the commit inserted, in the order queued, and asking for them cost nothing.
                Assert.Equal(afterCommit, scene.Requests.Count);
                Assert.Single(copies);
                Assert.Equal(25, handles.Distinct().Count());
                Assert.All(handles, h => Assert.NotNull(scene.Served.FindEntity(h)));

                for (int i = 0; i < handles.Length; i++)
                {
                    Near(new GeoPoint3(i, 10, 0), ((DxfLine)scene.Served.FindEntity(handles[i])).StartPoint);
                }
            }
        }

        [Fact]
        public void TheTicketsOfInsertsSentWithACommit_GiveTheirHandlesWithNoRequestOfTheirOwn()
        {
            using (var scene = new Scene())
            {
                string ticket = scene.Remote.Insert(Made(3));
                IReadOnlyList<string> committed = scene.Remote.CommitChanges();
                int afterCommit = scene.Requests.Count;

                Assert.Equal(committed.Single(), scene.Remote.HandleOfTicket(ticket));
                Assert.Equal(afterCommit, scene.Requests.Count);
            }
        }

        [Fact]
        public void AHandleStandsForItself_AndATicketNotCommittedYetIsRefused()
        {
            using (var scene = new Scene())
            {
                string handle = scene.Remote.HandleOf(scene.Line);
                string ticket = scene.Remote.Insert(Made(0));

                Assert.Equal(handle, scene.Remote.HandleOfTicket(handle));
                CadRemoteException refused = Assert.Throws<CadRemoteException>(() =>
                    scene.Remote.HandleOfTicket(ticket)
                );
                Assert.Contains("CommitChanges", refused.Message, StringComparison.Ordinal);
                Assert.Throws<ArgumentNullException>(() => scene.Remote.HandleOfTicket(null));
            }
        }

        [Fact]
        public void TheOldWay_ReadingEachTicketWithGetObject_CostsOneRequestPerInsert()
        {
            using (var scene = new Scene())
            {
                var tickets = new List<string>();

                for (int i = 0; i < 10; i++)
                {
                    tickets.Add(scene.Remote.Insert(Made(i)));
                }

                scene.Remote.Copy(new[] { scene.Line }, new GeoVector2(0, 50));
                int afterCommit = scene.Requests.Count;

                foreach (string ticket in tickets)
                {
                    Assert.NotNull(scene.Remote.HandleOf(scene.Remote.GetObject(ticket)));
                }

                // What a script that asks for the handles through the objects pays, which HandleOfTicket does not.
                Assert.True(scene.Requests.Count - afterCommit >= 10);
            }
        }

        [Fact]
        public void AThousandInsertsAndACopy_GiveTheirHandlesAtOnce_WhereTheObjectsTookARequestEach()
        {
            using (var scene = new Scene())
            {
                // The first commit pays for the JIT of both sides: not counted.
                scene.Remote.Insert(Made(0));
                scene.Remote.Copy(new[] { scene.Line }, new GeoVector2(0, 1));
                var tickets = new List<string>();
                var whole = Stopwatch.StartNew();

                for (int i = 0; i < 1000; i++)
                {
                    tickets.Add(scene.Remote.Insert(Made(i)));
                }

                scene.Remote.Copy(new[] { scene.Line }, new GeoVector2(0, 50));
                var sent = whole.Elapsed;
                var read = Stopwatch.StartNew();
                string[] handles = tickets.Select(t => scene.Remote.HandleOfTicket(t)).ToArray();
                read.Stop();
                var through = Stopwatch.StartNew();
                string[] viaObjects = tickets.Select(t => scene.Remote.HandleOf(scene.Remote.GetObject(t))).ToArray();
                through.Stop();

                _output.WriteLine(
                    "1000 Insert + Copy: {0:0.0} ms; the 1000 handles by HandleOfTicket: {1:0.00} ms, by GetObject + HandleOf: {2:0.0} ms.",
                    sent.TotalMilliseconds,
                    read.Elapsed.TotalMilliseconds,
                    through.Elapsed.TotalMilliseconds
                );
                Assert.Equal(viaObjects, handles);
                Assert.True(read.ElapsedMilliseconds < through.ElapsedMilliseconds);
            }
        }

        private static void Near(GeoPoint3 expected, GeoPoint3 actual)
        {
            Assert.True((expected - actual).Length < 1E-9, expected + " against " + actual);
        }
    }
}
