using System;
using System.Linq;
using GeometryHelper.Dxf;
using GeometryHelper.Geometry;
using Xunit;

namespace OpenCAD2D.Remote.UnitTest
{
    /// <summary>
    /// A request with a member of the wrong kind or out of range, sent by hand: the app refuses it in words that name the member,
    /// never with "could not do the request" (a fault of the app) and never by taking a wrong value for a right one (a word for
    /// the number 0, a cap of minus 4 294 967 295 for 1).
    /// </summary>
    public class RemoteBadRequestTests
    {
        private static CadDocument Drawing()
        {
            var db = new DxfDatabase("AC1027");
            db.ModelSpace.AppendEntity(new DxfLine(new GeoPoint3(0, 0, 0), new GeoPoint3(10, 0, 0)));
            return CadDocument.From(db, "bad.dxf");
        }

        private const string Standard = "{\"schema\":\"opencad2d.standard\",\"version\":1}";

        private static JsonArray Items(params object[] items) => new JsonArray(items);

        private static void Refused(string op, Action<JsonObject> fill, string mentions)
        {
            var harness = new Harness();
            var host = new TestHost();
            host.Add(Drawing());
            harness.Serve(host);

            using (harness)
            {
                CadApplication app = harness.Connect();
                var request = new JsonObject().Set("op", op).Set("doc", (long)app.Documents.Single().Id);
                fill(request);

                CadRemoteException refused = Assert.Throws<CadRemoteException>(() => app.Connection.Request(request));

                Assert.DoesNotContain("could not do the request", refused.Message);
                Assert.Contains(mentions, refused.Message);
            }
        }

        [Fact]
        public void AGraphCapOfAHugeNegativeNumber_IsRefused_NotTakenForOne()
        {
            Refused(
                RemoteOps.Export,
                r => r.Set("what", "graph").Set("tolerance", 0.001).Set("maxEdges", -4294967295L),
                "number of edges above nought"
            );
        }

        [Fact]
        public void AGraphCapThatIsNoNumber_IsRefusedNamingIt()
        {
            Refused(RemoteOps.Export, r => r.Set("what", "graph").Set("maxEdges", "many"), "maxEdges");
        }

        [Fact]
        public void AnExportOfTheWrongKinds_IsRefusedNamingTheMember()
        {
            Refused(RemoteOps.Export, r => r.Set("what", "xml"), "what");
            Refused(RemoteOps.Export, r => r.Set("what", "graph").Set("tolerance", "fine"), "tolerance");
            Refused(RemoteOps.Export, r => r.Set("what", "json").Set("layout", 5L), "layout");
        }

        [Fact]
        public void AFrameWithAWordInIt_IsRefused_NotMeasuredFromNought()
        {
            Refused(RemoteOps.FreeSpace, r => r.Set("frame", Items("a", 0L, 10L, 10L)), "frame");
        }

        [Fact]
        public void ACandidateWithAWordInIt_IsRefused_NotTakenForNought()
        {
            Refused(RemoteOps.Fits, r => r.Set("candidate", Items(0L, 0L, "b", 10L, 0L)), "candidate");
        }

        [Fact]
        public void ALeaderWithAWordForAPoint_OrAScaleThatIsNoNumber_IsRefusedNamingIt()
        {
            Refused(
                RemoteOps.MeasureLeader,
                r => r.Set("arrow", Items("a", 0L)).Set("landing", Items(1L, 1L)),
                "arrow"
            );
            Refused(
                RemoteOps.MeasureLeader,
                r => r.Set("arrow", Items(0L, 0L)).Set("landing", Items(1L, null)).Set("words", "x"),
                "landing"
            );
            Refused(
                RemoteOps.MeasureLeader,
                r => r.Set("arrow", Items(0L, 0L)).Set("landing", Items(5L, 5L)).Set("words", "x").Set("scale", "big"),
                "scale"
            );
        }

        [Fact]
        public void TheOptionsOfAFreeSpace_OfTheWrongKind_AreRefusedNamingThem()
        {
            Refused(RemoteOps.FreeSpace, r => r.Set("frame", Items(0L, 0L, 10L, 10L)).Set("kinds", Items(1L)), "kinds");
            Refused(RemoteOps.FreeSpace, r => r.Set("frame", Items(0L, 0L, 10L, 10L)).Set("margin", "wide"), "margin");
            Refused(
                RemoteOps.Fits,
                r => r.Set("candidate", Items(0L, 0L, 1L, 1L, 0L)).Set("tolerance", -1L),
                "tolerance"
            );
        }

        [Fact]
        public void AnOverlapsOfANegativeTolerance_OrOfWordsThatAreNotNames_IsRefusedNamingTheMember()
        {
            Refused(RemoteOps.Overlaps, r => r.Set("tolerance", -1L), "tolerance");
            Refused(RemoteOps.Overlaps, r => r.Set("tolerance", "fine"), "tolerance");
            Refused(RemoteOps.Overlaps, r => r.Set("words", Items("TEXT", 5L)), "words");
            Refused(RemoteOps.Overlaps, r => r.Set("over", "LINE"), "over");
            Refused(RemoteOps.Overlaps, r => r.Set("handles", Items(null, "2A")), "handles");
        }

        [Fact]
        public void TheBoundsOfHandlesThatAreNotNames_AreRefusedNamingTheMember()
        {
            Refused(RemoteOps.Bounds, r => r.Set("handles", Items("2A", 5L)), "handles");
            Refused(RemoteOps.Bounds, r => r.Set("handles", "2A"), "handles");
            Refused(RemoteOps.Bounds, r => r.Set("layout", 5L), "layout");
        }

        [Fact]
        public void ACheckOfAToleranceOrScaleOfTheWrongKind_IsRefusedNamingTheMember()
        {
            Refused(RemoteOps.CheckStandard, r => r.Set("standard", Standard).Set("tolerance", -1L), "tolerance");
            Refused(RemoteOps.CheckStandard, r => r.Set("standard", Standard).Set("tolerance", "fine"), "tolerance");
            Refused(RemoteOps.CheckStandard, r => r.Set("standard", Standard).Set("modelScale", "big"), "modelScale");
        }

        [Fact]
        public void AMeasureOfAFragmentThatIsNoDrawing_IsRefused()
        {
            Refused(RemoteOps.Measure, r => r.Set("fragment", "this is not DXF"), "fragment");
        }

        [Fact]
        public void ACommitWhoseRecordsAreMalformed_IsRefusedNamingTheMember()
        {
            Refused(
                RemoteOps.Commit,
                r => r.Set("fragment", string.Empty).Set("operations", Items(new JsonObject().Set("op", "records"))),
                "tables"
            );
            Refused(
                RemoteOps.Commit,
                r =>
                    r.Set("fragment", string.Empty)
                        .Set(
                            "operations",
                            Items(
                                new JsonObject()
                                    .Set("op", "records")
                                    .Set("tables", new JsonObject().Set("LTYPE", Items(5L)))
                            )
                        ),
                "LTYPE"
            );
            Refused(RemoteOps.Commit, r => r.Set("fragment", string.Empty).Set("operations", Items("x")), "operations");
        }
    }
}
