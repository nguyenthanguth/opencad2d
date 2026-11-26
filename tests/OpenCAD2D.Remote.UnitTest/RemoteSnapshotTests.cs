using System;
using System.Linq;
using GeometryHelper.Dxf;
using GeometryHelper.Geometry;
using Xunit;

namespace OpenCAD2D.Remote.UnitTest
{
    /// <summary>
    /// A drawing of the app kept and put back from outside (<see cref="CadRemoteDocument.TakeSnapshot"/>, <see cref="CadRemoteDocument.Reset"/>):
    /// the reset is the app's own, drawing and handles to the byte, the steps made since forgotten and what the user did before
    /// still undoable, and it is refused, with the reason, while a command runs in the drawing and for a snapshot let go.
    /// </summary>
    public class RemoteSnapshotTests
    {
        private static CadDocument Plan()
        {
            var db = new DxfDatabase("AC1027");
            db.ModelSpace.AppendEntity(new DxfLine(new GeoPoint3(0, 0, 0), new GeoPoint3(100, 0, 0)));
            db.ModelSpace.AppendEntity(new DxfCircle { Center = new GeoPoint3(50, 50, 0), Radius = 12.5 });
            return CadDocument.From(db, "snapshot.dxf");
        }

        private static void Add(CadDocument document, double y) =>
            document.ApplyEdit("LINE", null, s =>
                s.Append(s.Document.ModelSpace, new DxfLine(new GeoPoint3(0, y, 0), new GeoPoint3(10, y, 0)))
            );

        private static (Harness harness, CadRemoteDocument remote) Start(params CadDocument[] documents)
        {
            var harness = new Harness();
            var host = new TestHost();

            foreach (CadDocument document in documents)
            {
                host.Add(document);
            }

            harness.Serve(host);
            return (harness, harness.Connect().Documents.First());
        }

        [Fact]
        public void AResetFromOutside_PutsTheDrawingBackToTheByte_AsOftenAsAsked()
        {
            CadDocument document = Plan();
            Add(document, 5);
            var (harness, remote) = Start(document);

            using (harness)
            {
                string before = document.Database.ToText();
                CadRemoteSnapshot snapshot = remote.TakeSnapshot();

                for (int episode = 0; episode < 3; episode++)
                {
                    Add(document, 10 + episode);
                    Add(document, 20 + episode);
                    Assert.NotEqual(before, document.Database.ToText());
                    remote.Reset(snapshot);
                    Assert.Equal(before, document.Database.ToText());
                }

                // What the user did before the snapshot is still a step to undo, and nothing of the episode is.
                Assert.True(document.CanUndo);
                Assert.Equal("LINE", document.NextUndo.Name);
                Assert.Same(remote, snapshot.Document);
            }
        }

        [Fact]
        public void AResetIsRefusedWithTheReason_WhileACommandRuns_AndForASnapshotLetGo()
        {
            CadDocument document = Plan();
            var (harness, remote) = Start(document);

            using (harness)
            {
                CadRemoteSnapshot snapshot = remote.TakeSnapshot();
                Add(document, 10);

                document.BeginGroup("LINE");
                CadRemoteException busy = Assert.Throws<CadRemoteException>(() => remote.Reset(snapshot));
                Assert.Contains("busy", busy.Message);
                Assert.Contains("end it", busy.Message);
                Assert.Throws<CadRemoteException>(() => remote.TakeSnapshot());
                document.CancelGroup();

                remote.Reset(snapshot);
                remote.Release(snapshot);
                remote.Release(snapshot);
                CadRemoteException gone = Assert.Throws<CadRemoteException>(() => remote.Reset(snapshot));
                Assert.Contains("no snapshot", gone.Message);
            }
        }

        [Fact]
        public void ASnapshotIsOfOneDrawing_AndNullIsRefused()
        {
            var (harness, remote) = Start(Plan(), Plan());

            using (harness)
            {
                CadRemoteDocument other = harness.Connect().Documents.Last();
                CadRemoteSnapshot snapshot = remote.TakeSnapshot();

                Assert.Throws<ArgumentException>(() => other.Reset(snapshot));
                Assert.Throws<ArgumentException>(() => other.Release(snapshot));
                Assert.Throws<ArgumentNullException>(() => remote.Reset(null));
                Assert.Throws<ArgumentNullException>(() => remote.Release(null));
            }
        }
    }
}
