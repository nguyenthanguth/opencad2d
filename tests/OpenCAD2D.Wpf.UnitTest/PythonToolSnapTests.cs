using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using GeometryHelper;
using GeometryHelper.Dxf;
using GeometryHelper.Geometry;
using OpenCAD2D.Plugins;
using OpenCAD2D.Remote;
using Xunit;
using static OpenCAD2D.Wpf.UnitTest.CanvasMouseTests;
using static OpenCAD2D.Wpf.UnitTest.CanvasViewportDoubleClickTests;

namespace OpenCAD2D.Wpf.UnitTest
{
    /// <summary>
    /// The object snaps of a Python tool in the window, with the tool played by a test (<see cref="FakeToolLink"/>): the finder
    /// the window asks never waits for the tool, a miss is noted and asked in one question after the pass, the answer is kept per
    /// entity until the drawing changes and the point shows with no other move, a point typed after the keyword waits for the
    /// answer a moment, and a failure the tool answers switches the snap off as a finder that throws does. The drawing is two
    /// columns and a wall: a circle of radius 100 about (2 000, 1 500), whose top is (2 000, 1 600), a circle of radius 50 about
    /// (3 000, 1 500) and a line from (0, 0) to (4 000, 0). The processes of the real tools are in
    /// <see cref="PythonToolSnapProcessTests"/>.
    /// </summary>
    [Collection(ViewerSpeedCollection.Name)]
    public sealed class PythonToolSnapTests : IDisposable
    {
        // The cursor 5 from the top of the first circle, and the aperture of 20 round it: the top is well within the aperture.
        private static readonly GeoPoint2 NearTheTop = new GeoPoint2(2004, 1597);
        private const double Aperture = 20.0;
        private static readonly double[][] TheTop = { new[] { 2000.0, 1600.0 } };

        private readonly PluginWindowKit _kit = new PluginWindowKit();

        public void Dispose() => _kit.Dispose();

        /// <summary>A window with the columns open and the snaps of the tool in it, on, as a tool's plugin.json says.</summary>
        private MainViewModel Window(FakeToolLink link, out string first, params CadPluginSnapEntry[] entries)
        {
            MainViewModel model = _kit.Opened(out first, out _, out _);
            _kit.LoadHook(model, host =>
            {
                foreach (CadPluginSnapEntry entry in entries.Length == 0 ? new[] { FakeToolLink.Entry() } : entries)
                {
                    host.AddObjectSnap(link.SnapOf(entry));
                }
            });
            model.SnapModes = CadSnapModes.None;
            return model;
        }

        private static CadSnapResult SnapAt(MainViewModel model, GeoPoint2 cursor) =>
            CadPicking.Snap(model.Document, model.Document.ModelSpace, model.Snap, cursor, Aperture, null);

        private static void Near(GeoPoint2 expected, GeoPoint2 actual)
        {
            Assert.Equal(expected.X, actual.X, 6);
            Assert.Equal(expected.Y, actual.Y, 6);
        }

        // ---- The cache

        [Fact]
        public void TheFirstMoveOverAnEntity_AsksTheToolOnceAndShowsNothing_AndTheAnswerShowsThePointWithNoOtherMove()
        {
            var link = new FakeToolLink();
            MainViewModel model = Window(link, out string circle);

            CadSnapResult first = SnapAt(model, NearTheTop);

            Assert.Equal(CadSnapKind.None, first.Kind);
            Assert.Empty(link.Sent);
            Assert.Empty(link.Answered);

            link.RunPosted();

            JsonObject question = link.Question(0);
            Assert.Equal("snap", question.GetString("op"));
            JsonObject item = Assert.Single(FakeToolLink.Items(question));
            Assert.Equal("CTOP", item.GetString("keyword"));
            Assert.Equal(circle, item.GetString("handle"));
            Assert.Empty(item.GetArray("blocks"));
            var asked = Assert.IsType<DxfCircle>(FakeToolLink.EntityOf(question, item));
            Assert.Equal(100.0, asked.Radius, 9);

            link.Reply(0, new object[] { TheTop });
            link.RunPosted();

            Assert.Equal(new[] { model.Document }, Assert.Single(link.Answered));
            CadSnapResult second = SnapAt(model, NearTheTop);
            Assert.Equal(CadSnapKind.Custom, second.Kind);
            Assert.Equal("Circle top", second.CustomSnap.Name);
            Near(new GeoPoint2(2000, 1600), second.Point);
        }

        [Fact]
        public void ASecondMoveOverTheSameEntity_AsksNothing_BeforeTheAnswerAndAfterIt()
        {
            var link = new FakeToolLink();
            MainViewModel model = Window(link, out _);

            SnapAt(model, NearTheTop);
            SnapAt(model, new GeoPoint2(2003, 1598));
            link.RunPosted();
            SnapAt(model, NearTheTop);
            link.RunPosted();

            Assert.Single(link.Sent);

            link.Reply(0, new object[] { TheTop });
            SnapAt(model, NearTheTop);
            link.RunPosted();

            Assert.Single(link.Sent);
        }

        [Fact]
        public void AnEditAnUndoAndARegenOfTheDrawing_MakeTheNextMoveAskAgain_AChangeOfAnotherDrawingDoesNot()
        {
            var link = new FakeToolLink();
            MainViewModel model = Window(link, out _);
            SnapAt(model, NearTheTop);
            link.RunPosted();
            link.Reply(0, new object[] { TheTop });
            Assert.Equal(CadSnapKind.Custom, SnapAt(model, NearTheTop).Kind);

            // Another drawing changes: what is known of this one stays.
            CadDocument other = CadDocument.From(new DxfDatabase("AC1027"), "other.dxf");
            other.Regen();
            SnapAt(model, NearTheTop);
            link.RunPosted();
            Assert.Single(link.Sent);

            model.Execute("LINE");
            model.Execute("0,-500");
            model.Execute("100,-500");
            model.Execute(string.Empty);
            Assert.Equal(CadSnapKind.None, SnapAt(model, NearTheTop).Kind);
            link.RunPosted();
            Assert.Equal(2, link.Sent.Count);
            link.Reply(1, new object[] { TheTop });
            Assert.Equal(CadSnapKind.Custom, SnapAt(model, NearTheTop).Kind);

            Assert.True(model.Document.Undo());
            SnapAt(model, NearTheTop);
            link.RunPosted();
            Assert.Equal(3, link.Sent.Count);
            link.Reply(2, new object[] { TheTop });

            model.Document.Regen();
            SnapAt(model, NearTheTop);
            link.RunPosted();
            Assert.Equal(4, link.Sent.Count);
        }

        [Fact]
        public void AnAnswerToAQuestionAskedBeforeTheDrawingChanged_IsDropped()
        {
            var link = new FakeToolLink();
            MainViewModel model = Window(link, out _);
            SnapAt(model, NearTheTop);
            link.RunPosted();

            model.Document.Regen();
            link.Reply(0, new object[] { TheTop });
            link.RunPosted();

            Assert.Empty(link.Answered);
            Assert.Equal(CadSnapKind.None, SnapAt(model, NearTheTop).Kind);
        }

        [Fact]
        public void TheMissesOfOnePass_GoInOneQuestion_EveryKeywordOfTheToolTogether()
        {
            var link = new FakeToolLink();
            MainViewModel model = Window(
                link,
                out string circle,
                FakeToolLink.Entry("CTOP", "Circle top"),
                FakeToolLink.Entry("CBOT", "Circle bottom")
            );

            SnapAt(model, NearTheTop);
            link.RunPosted();

            JsonObject question = Assert.Single(link.Sent.Select(RemoteJson.Parse).Cast<JsonObject>());
            List<JsonObject> items = FakeToolLink.Items(question);
            Assert.Equal(new[] { "CTOP", "CBOT" }, items.Select(i => i.GetString("keyword")));
            Assert.All(items, i => Assert.Equal(circle, i.GetString("handle")));
            Assert.Equal(items[0].GetString("ref"), items[1].GetString("ref"));
            Assert.Single(DxfDatabase.Parse(question.GetString("fragment")).ModelSpace);
        }

        [Fact]
        public void ASnapOfCircles_IsNotAskedAboutALine_AndAnEntityOfNoHandleIsNotAsked()
        {
            var link = new FakeToolLink();
            MainViewModel model = Window(link, out _);

            // Over the wall, a line.
            CadSnapResult result = SnapAt(model, new GeoPoint2(1000, 3));
            link.RunPosted();

            Assert.Equal(CadSnapKind.None, result.Kind);
            Assert.Empty(link.Sent);
        }

        [Fact]
        public void MoreThanTenThousandEntries_ClearTheCacheWhole()
        {
            var link = new FakeToolLink();
            DxfDatabase database = CadTemplate.CreateMetric();

            for (int i = 0; i < CadToolSnaps.MaxEntries + 5; i++)
            {
                database.ModelSpace.AppendEntity(
                    new DxfCircle(new GeoPoint3(i * 3.0, 0, 0), new GeoVector3(0, 0, 1), 1)
                );
            }

            CadDocument document = CadDocument.From(database, "many.dxf");
            CadCustomSnap snap = link.SnapOf(FakeToolLink.Entry());

            foreach (DxfEntity circle in database.ModelSpace.Cast<DxfEntity>().ToList())
            {
                var asked = new CadSnapRequest(document, circle, circle.Handle, default, 1.0, null);
                snap.Finder(asked);
            }

            // The 10 001st entry cleared the cache: it and the four after it are what is held.
            Assert.Equal(5, link.Snaps.Count);
        }

        // ---- Blocks and viewports

        [Fact]
        public void AnEntityInABlock_IsAskedAsItsPlacedCopy_WithTheReferencesItIsIn()
        {
            // A circle of radius 10 in the block B put at (100, 50) at scale 2: asked as a circle of radius 20 about (100, 50).
            DxfDatabase database = CadTemplate.CreateMetric();
            var block = new DxfBlockTableRecord { Name = "B", Origin = GeoPoint3.Origin };
            database.BlockTable.Add(block);
            var inner = new DxfCircle(GeoPoint3.Origin, new GeoVector3(0, 0, 1), 10) { Layer = "COLUMNS" };
            database.LayerTable.Add(new DxfLayerTableRecord { Name = "COLUMNS", ColorIndex = 1 });
            block.AppendEntity(inner);
            var reference = new DxfBlockReference(new GeoPoint3(100, 50, 0), block)
            {
                ScaleFactors = new GeoVector3(2, 2, 2),
            };
            database.ModelSpace.AppendEntity(reference);
            CadDocument document = CadDocument.From(database, "block.dxf");
            var link = new FakeToolLink();
            CadCustomSnap snap = link.SnapOf(FakeToolLink.Entry());
            var snaps = new ObjectSnap(new Tolerance(1E-9, 1E-9))
            {
                Modes = CadSnapModes.None,
                CustomSnaps = new[] { snap },
                EntityOf = document.FindEntity,
                Document = document,
            };

            snaps.Snap(document.GetPickIndex(document.ModelSpace), new GeoPoint2(100.5, 69.6), 2.0);
            link.RunPosted();

            JsonObject question = link.Question(0);
            JsonObject item = Assert.Single(FakeToolLink.Items(question));
            Assert.Equal(inner.Handle, item.GetString("handle"));
            Assert.Equal(new object[] { reference.Handle }, item.GetArray("blocks").ToArray());
            var placed = Assert.IsType<DxfCircle>(FakeToolLink.EntityOf(question, item));
            Assert.Equal(20.0, placed.Radius, 9);
            Assert.Equal(100.0, placed.Center.X, 9);
            Assert.Equal(50.0, placed.Center.Y, 9);
            Assert.Equal("COLUMNS", placed.Layer);

            link.Reply(0, new object[] { new[] { new[] { 100.0, 70.0 } } });
            CadSnapResult result = snaps.Snap(
                document.GetPickIndex(document.ModelSpace),
                new GeoPoint2(100.5, 69.6),
                2.0
            );

            Assert.Equal(CadSnapKind.Custom, result.Kind);
            Assert.Equal(reference.Handle, result.Handle);
            Near(new GeoPoint2(100, 70), result.Point);
        }

        [Fact]
        public void ModelSpaceThroughAViewport_IsAskedInModelCoordinates_AndThePointLandsOnTheSheet()
        {
            // A circle of radius 10 about the origin in model space, shown by a viewport 200 by 100 about (150, 100) at 1:2: its
            // top (0, 10) is at (150, 105) on the sheet.
            DxfDatabase database = CadTemplate.CreateMetric();
            database.ModelSpace.AppendEntity(new DxfCircle(GeoPoint3.Origin, new GeoVector3(0, 0, 1), 10));
            var sheet = new DxfLayout { LayoutName = "L" };
            sheet.AddToLayoutDictionary(database, null);
            sheet.Initialize();
            var frame = new DxfViewport
            {
                CenterPoint = new GeoPoint3(150, 100, 0),
                Width = 200,
                Height = 100,
            };
            sheet.BlockTableRecord.AppendEntity(frame);
            frame.ViewCenter = new GeoPoint2(0, 0);
            frame.ViewHeight = 200;
            frame.On = true;
            CadDocument document = CadDocument.From(database, "layout.dxf");
            CadSpace layout = document.Spaces.Single(s => s.Name == "L");
            document.CurrentSpace = layout;
            var link = new FakeToolLink();
            CadCustomSnap snap = link.SnapOf(FakeToolLink.Entry());
            var snaps = new ObjectSnap(new Tolerance(1E-9, 1E-9))
            {
                Modes = CadSnapModes.None,
                CustomSnaps = new[] { snap },
                EntityOf = document.FindEntity,
                Document = document,
            };
            GeoPoint2 cursor = new GeoPoint2(150.2, 104.8);

            CadSnapResult Through()
            {
                CadSnapResult own = snaps.Snap(document.GetPickIndex(layout), cursor, 1.0);
                return CadViewportSnap.Through(document, layout, snaps, own, cursor, 1.0, null, null);
            }

            Through();
            link.RunPosted();

            JsonObject question = link.Question(0);
            JsonObject item = Assert.Single(FakeToolLink.Items(question));
            var asked = Assert.IsType<DxfCircle>(FakeToolLink.EntityOf(question, item));
            Assert.Equal(0.0, asked.Center.X, 9);
            Assert.Equal(0.0, asked.Center.Y, 9);
            Assert.Equal(10.0, asked.Radius, 9);

            link.Reply(0, new object[] { new[] { new[] { 0.0, 10.0 } } });
            CadSnapResult result = Through();

            Assert.Equal(CadSnapKind.Custom, result.Kind);
            Near(new GeoPoint2(150, 105), result.Point);
        }

        // ---- A point typed after the keyword

        [Fact]
        [Trait("Category", "Timing")]
        public void ATypedPointAfterTheKeyword_WaitsForTheAnswer_AndTakesThePointNearestIt()
        {
            var link = new FakeToolLink();
            MainViewModel model = Window(link, out _);
            model.Session.SnapAperture = Aperture;

            // The tool answers 100 ms after the question, on a thread of its own, as the pipe's reader does.
            link.OnSend = line =>
                Task.Run(() =>
                {
                    Thread.Sleep(100);
                    link.Reply(
                        ((JsonObject)RemoteJson.Parse(line)).GetLong("id"),
                        new object[] { new[] { new[] { 2100.0, 1500.0 }, new[] { 2000.0, 1600.0 } } }
                    );
                });
            model.Execute("LINE");
            model.Execute("CTOP");
            var watch = Stopwatch.StartNew();

            model.Execute("2004,1597");

            Assert.True(watch.Elapsed < TimeSpan.FromSeconds(5), "took " + watch.Elapsed);
            Near(new GeoPoint2(2000, 1600), model.Session.LastPoint);
            Assert.Single(link.Sent);
        }

        [Fact]
        [Trait("Category", "Timing")]
        public void ATypedPointWhoseToolNeverAnswers_SaysNothingWasFoundAfterASecond_AndTheNextMoveStillReturnsAtOnce()
        {
            var link = new FakeToolLink();
            MainViewModel model = Window(link, out _);
            model.Session.SnapAperture = Aperture;
            model.Execute("LINE");
            model.Execute("CTOP");
            var watch = Stopwatch.StartNew();

            model.Execute("2004,1597");

            Assert.Contains("No Circle top found for specified point.", model.History);
            Assert.InRange(watch.Elapsed.TotalSeconds, 0.8, 10.0);
            Assert.Single(link.Sent);

            watch.Restart();
            SnapAt(model, NearTheTop);

            Assert.True(watch.Elapsed < TimeSpan.FromSeconds(1), "took " + watch.Elapsed);
        }

        [Fact]
        [Trait("Category", "Timing")]
        public void ATypedPointWhoseProcessEnds_StopsWaiting()
        {
            var link = new FakeToolLink();
            MainViewModel model = Window(link, out _);
            model.Session.SnapAperture = Aperture;
            link.OnSend = line =>
                Task.Run(() =>
                {
                    Thread.Sleep(100);
                    link.Snaps.Clear();
                });
            model.Execute("LINE");
            model.Execute("CTOP");
            var watch = Stopwatch.StartNew();

            model.Execute("2004,1597");

            Assert.Contains("No Circle top found for specified point.", model.History);
            Assert.True(watch.Elapsed < TimeSpan.FromSeconds(0.9), "took " + watch.Elapsed);
        }

        // ---- A failure

        [Fact]
        public void AnErrorTheToolAnswers_SwitchesTheSnapOffWithTheWordsOfAFinderThatThrows_AndChoosingItOnAgainAsksAgain()
        {
            var link = new FakeToolLink();
            MainViewModel model = Window(link, out _);
            SnapAt(model, NearTheTop);
            link.RunPosted();

            link.Reply(0, "CTOP: ValueError: no circle");
            SnapAt(model, NearTheTop);

            Assert.Contains("The snap CTOP of HookPlugin failed and is off: ValueError: no circle", model.History);

            // Off for the session: the next move asks nothing.
            SnapAt(model, NearTheTop);
            link.RunPosted();
            Assert.Single(link.Sent);

            model.SetPluginSnaps(new[] { new KeyValuePair<CadCustomSnap, bool>(model.Snap.CustomSnaps[0], true) });
            SnapAt(model, NearTheTop);
            link.RunPosted();

            Assert.Equal(2, link.Sent.Count);
        }

        [Fact]
        public void ASnapTheToolHasNoFinderFor_IsSwitchedOffWithTheToolsOwnWords()
        {
            var link = new FakeToolLink();
            MainViewModel model = Window(link, out _);
            SnapAt(model, NearTheTop);
            link.RunPosted();

            link.Reply(0, "CTOP has no @snap in the tool.");
            SnapAt(model, NearTheTop);

            Assert.Contains(
                "The snap CTOP of HookPlugin failed and is off: CTOP has no @snap in the tool.",
                model.History
            );
        }

        [Fact]
        public void AnAnswerTheAppCannotRead_IsAnErrorOfEveryItemOfItsQuestion()
        {
            var link = new FakeToolLink();
            MainViewModel model = Window(
                link,
                out _,
                FakeToolLink.Entry("CTOP", "Circle top"),
                FakeToolLink.Entry("CBOT", "Circle bottom")
            );
            SnapAt(model, NearTheTop);
            link.RunPosted();

            // One answer to two items.
            link.Reply(0, new object[] { TheTop });
            SnapAt(model, NearTheTop);

            Assert.Contains(model.History, l =>
                l.StartsWith("The snap CTOP of HookPlugin failed and is off: ", StringComparison.Ordinal)
            );
        }

        // ---- The window thread never waits

        [Fact]
        [Trait("Category", "Timing")]
        public void AMoveOverAToolThatNeverAnswers_ReturnsAtOnce_WhereverTheSenderWouldBlock()
        {
            var link = new FakeToolLink();
            var never = new ManualResetEventSlim(false);
            link.OnSend = line => never.Wait(TimeSpan.FromSeconds(30));
            MainViewModel model = Window(link, out _);
            var watch = Stopwatch.StartNew();

            for (int i = 0; i < 20; i++)
            {
                SnapAt(model, new GeoPoint2(2004 - i * 0.1, 1597));
            }

            Assert.True(watch.Elapsed < TimeSpan.FromSeconds(1), "took " + watch.Elapsed);
            Assert.Empty(link.Sent);
            Assert.Single(link.Posted);
            never.Set();
        }

        [Fact]
        public void ATool_WithNoProcess_HasTheQuestionKeptAndItsStartAskedFor_AndTheProcessStartedSendsIt()
        {
            var link = new FakeToolLink { Connected = false };
            MainViewModel model = Window(link, out _);

            SnapAt(model, NearTheTop);
            link.RunPosted();

            Assert.Equal(1, link.Starts);
            link.Connected = true;
            link.Snaps.ProcessStarted();

            Assert.Equal(2, link.Sent.Count);
            Assert.Equal(link.Sent[0], link.Sent[1]);
        }

        // ---- Leaving

        [Fact]
        public void ClearingTheSnaps_AsAnUnloadAReloadOrTheProcessEndingDoes_MakesTheNextMoveAskAgain()
        {
            var link = new FakeToolLink();
            MainViewModel model = Window(link, out _);
            SnapAt(model, NearTheTop);
            link.RunPosted();
            link.Reply(0, new object[] { TheTop });
            Assert.Equal(CadSnapKind.Custom, SnapAt(model, NearTheTop).Kind);

            link.Snaps.Clear();

            Assert.Equal(CadSnapKind.None, SnapAt(model, NearTheTop).Kind);
            link.RunPosted();
            Assert.Equal(2, link.Sent.Count);
        }

        [Fact]
        public void AnAnswerWhereTheViewPointShows_ReachesTheCanvas_WhichFollowsTheCursorAgain()
        {
            // The sheet of the room: the column of radius 100 about (2 000, 1 500) is shown 5 across about (150, 100).
            OnLayout(null, (model, canvas, viewport) =>
            {
                var link = new FakeToolLink();
                CadCustomSnap snap = link.SnapOf(FakeToolLink.Entry());
                model.Snap.Modes = CadSnapModes.None;
                model.Snap.CustomSnaps = new[] { snap };
                model.Toggles.Single(t => t.Name == "OSNAP").IsOn = true;
                model.StartCommand("LINE");
                Point near = At(canvas, 150.0, 105.0);

                Mouse(canvas, UIElement.MouseMoveEvent, new Point(near.X + 3.0, near.Y - 3.0));
                Assert.Equal(CadSnapKind.None, canvas.CurrentSnap.Kind);
                link.RunPosted();
                // The finder is asked in model coordinates: the column's top is (2 000, 1 600) there.
                link.Reply(0, new object[] { TheTop });
                link.RunPosted();
                Assert.Equal(CadSnapKind.None, canvas.CurrentSnap.Kind);

                // No other move of the mouse: the canvas is told, and tracks the cursor again where it is.
                canvas.Retrack();

                Assert.Equal(CadSnapKind.Custom, canvas.CurrentSnap.Kind);
                Assert.Same(snap, canvas.CurrentSnap.CustomSnap);
                Assert.Equal(105.0, canvas.CurrentSnap.Point.Y, 6);
            });
        }
    }
}
