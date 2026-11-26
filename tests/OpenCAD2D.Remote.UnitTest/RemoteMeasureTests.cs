using System;
using System.Linq;
using GeometryHelper;
using GeometryHelper.Dxf;
using GeometryHelper.Geometry;
using Xunit;

namespace OpenCAD2D.Remote.UnitTest
{
    /// <summary>
    /// What a thing would take before it is added, the empty rectangles of a frame and whether a candidate fits, asked from
    /// outside (<see cref="CadRemoteDocument.Measure"/>, <see cref="CadRemoteDocument.MeasureLeader"/>,
    /// <see cref="CadRemoteDocument.GetFreeSpace"/>, <see cref="CadRemoteDocument.Fits"/>): each answer is the one
    /// <see cref="CadMeasure"/> and <see cref="CadSpatial"/> give in the app, number for number.
    /// </summary>
    public class RemoteMeasureTests
    {
        private static readonly Tolerance Touching = new Tolerance(1E-3, 1E-5);

        private static CadDocument Plan(out string line)
        {
            var db = new DxfDatabase("AC1027");
            var across = new DxfLine(new GeoPoint3(70, 0, 0), new GeoPoint3(70, 50, 0));
            db.ModelSpace.AppendEntity(across);
            line = across.Handle;
            return CadDocument.From(db, "measure.dxf");
        }

        private static (Harness harness, CadRemoteDocument remote) Start(CadDocument document)
        {
            var harness = new Harness();
            var host = new TestHost();
            host.Add(document);
            harness.Serve(host);
            return (harness, harness.Connect().Documents.Single());
        }

        private static DxfDBText Words() =>
            new DxfDBText
            {
                TextString = "Plate A-12",
                Position = new GeoPoint3(10, 20, 0),
                Height = 5.0,
                Rotation = 0.3,
            };

        [Fact]
        public void ATextMeasured_IsTheAppsOwnMeasure_BoxAndFrames()
        {
            CadDocument document = Plan(out _);
            CadMeasurement inApp = CadMeasure.Of(document, Words());
            var (harness, remote) = Start(document);

            using (harness)
            {
                CadRemoteMeasurement got = remote.Measure(Words());

                Assert.Equal(new[] { inApp.Box.MinX, inApp.Box.MinY, inApp.Box.MaxX, inApp.Box.MaxY }, got.Box);
                Assert.Equal(1, got.FrameCount);
                CadFrame frame = inApp.Frames.Single();
                Assert.Equal(new[] { frame.X, frame.Y, frame.Width, frame.Height, frame.Rotation }, got.FrameOf(0));
                Assert.DoesNotContain(document.ModelSpace.Block.Cast<DxfEntity>(), e => e is DxfDBText);
            }
        }

        [Fact]
        public void ADimensionMeasured_IsDrawnByTheAppInTheStyleItNames()
        {
            CadDocument document = Plan(out _);
            var dimension = new DxfRotatedDimension(
                0.0,
                new GeoPoint3(0, 0, 0),
                new GeoPoint3(100, 0, 0),
                new GeoPoint3(100, 20, 0),
                string.Empty,
                null
            )
            {
                DimensionStyleName = "Standard",
            };
            CadMeasurement inApp = CadMeasure.Of(document, dimension);
            var (harness, remote) = Start(document);

            using (harness)
            {
                CadRemoteMeasurement got = remote.Measure(dimension);

                Assert.Equal(new[] { inApp.Box.MinX, inApp.Box.MinY, inApp.Box.MaxX, inApp.Box.MaxY }, got.Box);
                Assert.Equal(inApp.Frames.Count, got.FrameCount);
            }
        }

        [Fact]
        public void AStyleTheAppLacks_IsRefusedInWords()
        {
            CadDocument document = Plan(out _);
            var dimension = new DxfRotatedDimension(
                0.0,
                new GeoPoint3(0, 0, 0),
                new GeoPoint3(100, 0, 0),
                new GeoPoint3(100, 20, 0),
                string.Empty,
                null
            )
            {
                DimensionStyleName = "NOPE",
            };
            var (harness, remote) = Start(document);

            using (harness)
            {
                CadRemoteException error = Assert.Throws<CadRemoteException>(() => remote.Measure(dimension));

                Assert.Contains("NOPE", error.Message, StringComparison.Ordinal);
                Assert.Throws<ArgumentNullException>(() => remote.Measure(null));
            }
        }

        [Fact]
        public void TheFreeSpaceOfAFrame_AndWhetherACandidateFits_AreTheAppsOwn()
        {
            CadDocument document = Plan(out _);
            CadFreeSpace inApp = CadSpatial.FreeSpace(
                document,
                document.ModelSpace,
                new DisplayBox(0, 0, 100, 50),
                new CadFreeSpaceOptions { Margin = 2.0 },
                Touching
            );
            var (harness, remote) = Start(document);

            using (harness)
            {
                CadRemoteFreeSpace got = remote.GetFreeSpace(null, 0, 0, 100, 50, null, 2.0, 0.0, 0.0, 1E-3);
                CadRemoteFreeSpace wide = remote.GetFreeSpace(
                    null,
                    0,
                    0,
                    100,
                    50,
                    new[] { "LINE" },
                    0.0,
                    40.0,
                    0.0,
                    1E-3
                );

                Assert.Equal(inApp.Boxes, got.Boxes);
                Assert.Equal(2, got.Count);
                Assert.Equal(new[] { 0.0, 0.0, 68.0, 50.0 }, got.BoxOf(0));
                Assert.Equal(new[] { 0.0, 0.0, 70.0, 50.0 }, wide.BoxOf(0));
                Assert.True(remote.Fits(null, 40, 10, 20, 10, 0.0, null, 0.0, 1E-3));
                Assert.False(remote.Fits(null, 60, 10, 20, 10, 0.0, null, 0.0, 1E-3));
                Assert.False(remote.Fits(null, 40, 10, 20, 10, 0.0, null, 15.0, 1E-3));
                Assert.True(remote.Fits(null, 60, 10, 20, 10, 0.0, new[] { "TEXT" }, 0.0, 1E-3));
                Assert.Throws<CadRemoteException>(() =>
                    remote.GetFreeSpace(null, 0, 0, 0, 50, null, 0.0, 0.0, 0.0, 1E-3)
                );
                Assert.Throws<CadRemoteException>(() => remote.Fits(null, 0, 0, 0.0, 10, 0.0, null, 0.0, 1E-3));
            }
        }

        [Fact]
        public void AMultileaderMeasured_IsTheAppsOwn()
        {
            CadDocument document = Plan(out _);
            Assert.True(CadMLeaderStyleEditor.AddStandard(document).Succeeded);
            CadMeasurement inApp = CadMeasure.Leader(
                document,
                null,
                new GeoPoint2(0, 0),
                new GeoPoint2(30, 20),
                "B-12",
                null
            );
            var (harness, remote) = Start(document);

            using (harness)
            {
                CadRemoteMeasurement got = remote.MeasureLeader(null, 0, 0, 30, 20, "B-12", null);

                Assert.Equal(new[] { inApp.Box.MinX, inApp.Box.MinY, inApp.Box.MaxX, inApp.Box.MaxY }, got.Box);
                CadRemoteException error = Assert.Throws<CadRemoteException>(() =>
                    remote.MeasureLeader("NOPE", 0, 0, 30, 20, "B-12", null)
                );
                Assert.Contains("NOPE", error.Message, StringComparison.Ordinal);
            }
        }
    }
}
