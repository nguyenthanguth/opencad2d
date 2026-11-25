using System;
using System.Collections.Generic;
using System.Linq;
using GeometryHelper.Dxf;
using GeometryHelper.Geometry;
using Xunit;

namespace OpenCAD2D.Remote.UnitTest
{
    /// <summary>
    /// The view, the named views, the system variables and the selection by area through Remote (A6): each call gives what the
    /// call of the drawing's own <see cref="CadEditor"/> gives on a twin document. The drawing is a line from (0, 0) to
    /// (100, 0) and a circle about (50, 50) of radius 25, on the console view of a session with no window.
    /// </summary>
    public class RemoteViewTests
    {
        private static CadDocument Reference(string name)
        {
            var database = new DxfDatabase("AC1027");
            database.ModelSpace.AppendEntity(new DxfLine(new GeoPoint3(0, 0, 0), new GeoPoint3(100, 0, 0)));
            database.ModelSpace.AppendEntity(new DxfCircle(new GeoPoint3(50, 50, 0), GeoVector3.ZAxis, 25));
            return CadDocument.From(database, name);
        }

        private static GeoPoint2 P(double x, double y) => new GeoPoint2(x, y);

        private static void AssertBox(DisplayBox expected, CadRemoteExtents actual)
        {
            Assert.Equal(expected.MinX, actual.Min.X, 9);
            Assert.Equal(expected.MinY, actual.Min.Y, 9);
            Assert.Equal(expected.MaxX, actual.Max.X, 9);
            Assert.Equal(expected.MaxY, actual.Max.Y, 9);
        }

        private sealed class Scene : IDisposable
        {
            public readonly Harness Harness = new Harness();
            public readonly TestHost Host = new TestHost();
            public readonly CadDocument Document;
            public readonly CadCommandSession Session;
            public readonly CadCommandSession Twin;
            public readonly CadApplication App;
            public readonly CadRemoteDocument Drawing;

            public Scene()
            {
                Document = Host.Add(Reference("A.dxf"));
                Session = Host.SessionOf(Document);
                Twin = new CadCommandSession(Reference("twin.dxf"));
                Harness.Serve(Host);
                App = Harness.Connect();
                Drawing = App.Documents.Get("A.dxf");
            }

            public void Dispose() => Harness.Dispose();

            public T OnApp<T>(Func<T> function) => Host.OnAppThread(function);

            public void OnApp(Action action) => Host.OnAppThread(action);

            public DisplayBox Shown() => OnApp(() => Session.CommandView.VisibleBox);
        }

        private static void AssertSameView(Scene scene)
        {
            DisplayBox twin = scene.Twin.CommandView.VisibleBox;
            DisplayBox shown = scene.Shown();
            Assert.Equal(twin.MinX, shown.MinX, 9);
            Assert.Equal(twin.MinY, shown.MinY, 9);
            Assert.Equal(twin.MaxX, shown.MaxX, 9);
            Assert.Equal(twin.MaxY, shown.MaxY, 9);
            AssertBox(shown, scene.Drawing.CurrentView);
        }

        [Fact]
        public void EachViewCall_GivesWhatTheInProcessCallGivesOnATwinDocument()
        {
            using (var scene = new Scene())
            {
                scene.Drawing.ZoomExtents();
                scene.Twin.Editor.ZoomExtents();
                AssertSameView(scene);
                Assert.Equal(-23.50187, scene.Drawing.CurrentView.Min.X, 4);
                Assert.Equal(79.16667, scene.Drawing.CurrentView.Max.Y, 4);

                scene.Drawing.ZoomWindow(P(0, 0), P(10, 10));
                scene.Twin.Editor.ZoomWindow(P(0, 0), P(10, 10));
                AssertSameView(scene);
                Assert.Equal(-3.82022, scene.Drawing.CurrentView.Min.X, 4);

                scene.Drawing.Pan(new GeoVector2(10, 0));
                scene.Twin.Editor.Pan(new GeoVector2(10, 0));
                AssertSameView(scene);
                Assert.Equal(-13.82022, scene.Drawing.CurrentView.Min.X, 4);

                scene.Drawing.ZoomCenter(P(50, 50), 20);
                scene.Twin.Editor.ZoomCenter(P(50, 50), 20);
                AssertSameView(scene);
                Assert.Equal(32.35955, scene.Drawing.CurrentView.Min.X, 4);

                scene.Drawing.ZoomScaled(2);
                scene.Twin.Editor.ZoomScaled(2);
                AssertSameView(scene);

                Assert.True(scene.Drawing.ZoomPrevious());
                Assert.True(scene.Twin.Editor.ZoomPrevious());
                AssertSameView(scene);

                scene.Drawing.SetCurrentView(P(10, 10), P(0, 0));
                scene.Twin.Editor.SetCurrentView(new DisplayBox(0, 0, 10, 10));
                AssertSameView(scene);
            }
        }

        [Fact]
        public void ZoomObjects_TakesTheHandlesOfObjectsReadFromTheDrawing()
        {
            using (var scene = new Scene())
            {
                DxfEntity circle = scene.Drawing.GetObjects().OfType<DxfCircle>().Single();
                string handle = scene.Drawing.HandleOf(circle);
                scene.Drawing.ZoomObjects(new[] { handle });
                scene.Twin.Editor.ZoomObjects(
                    new[] { scene.Twin.Document.ModelSpace.Block.OfType<DxfCircle>().Single().Handle }
                );

                AssertSameView(scene);
                Assert.Equal(50.0, scene.OnApp(() => scene.Session.CommandView.CenterX), 6);
                Assert.Throws<ArgumentNullException>(() => scene.Drawing.ZoomObjects(null));
            }
        }

        [Fact]
        public void AViewCallThatTheEditorRefuses_IsRefusedInTheEditorsWords()
        {
            using (var scene = new Scene())
            {
                CadRemoteException flat = Assert.Throws<CadRemoteException>(() =>
                    scene.Drawing.ZoomWindow(P(5, 0), P(5, 10))
                );
                CadRemoteException height = Assert.Throws<CadRemoteException>(() =>
                    scene.Drawing.ZoomCenter(P(0, 0), 0.0)
                );

                Assert.Contains("The window has no width or no height.", flat.Message, StringComparison.Ordinal);
                Assert.Contains("height", height.Message, StringComparison.Ordinal);
                Assert.Throws<CadRemoteException>(() => scene.Drawing.ZoomScaled(-1.0));
                Assert.False(scene.Drawing.ZoomPrevious());
            }
        }

        [Fact]
        public void AViewRequestOnADrawingNotShown_IsRefusedInWords_ButRegenAndTheNamedViewsAndVariablesAreNot()
        {
            using (var harness = new Harness())
            {
                var host = new TestHost();
                host.Add(Reference("A.dxf"));
                host.Add(Reference("B.dxf"));
                host.Activate(host.Documents[0]);
                harness.Serve(host);
                CadRemoteDocument other = harness.Connect().Documents.Get("B.dxf");
                const string words = "The drawing B.dxf is not the drawing shown: activate it first.";

                Assert.Equal(words, Assert.Throws<CadRemoteException>(() => other.ZoomExtents()).Message);
                Assert.Equal(
                    words,
                    Assert.Throws<CadRemoteException>(() => other.ZoomWindow(P(0, 0), P(1, 1))).Message
                );
                Assert.Equal(words, Assert.Throws<CadRemoteException>(() => other.Pan(new GeoVector2(1, 1))).Message);
                Assert.Equal(words, Assert.Throws<CadRemoteException>(() => other.CurrentView).Message);
                Assert.Equal(
                    words,
                    Assert.Throws<CadRemoteException>(() => other.SetCurrentView(P(0, 0), P(1, 1))).Message
                );
                Assert.Equal(words, Assert.Throws<CadRemoteException>(() => other.SaveView("V")).Message);
                Assert.Equal(words, Assert.Throws<CadRemoteException>(() => other.RestoreView("V")).Message);

                other.Regen();
                Assert.Empty(other.NamedViews);
                Assert.Equal(2, other.GetVariable("LUNITS"));
                Assert.Equal(2, other.SelectAll().Count);
                Assert.Throws<CadRemoteException>(() => other.DeleteView("V"));

                other.Activate();
                other.ZoomExtents();
                other.SaveView("V");
                Assert.Equal(new[] { "V" }, other.NamedViews);
            }
        }

        [Fact]
        public void HasWindow_IsFalseOnTheTestHost_AndTrueOnceTheHostGivesTheSessionAView()
        {
            using (var scene = new Scene())
            {
                Assert.False(scene.Drawing.HasWindow);

                scene.OnApp(() => scene.Session.View = new CadView(400, 300));

                Assert.True(scene.Drawing.HasWindow);
                scene.Drawing.ZoomWindow(P(0, 0), P(40, 30));
                Assert.Equal(0.0, scene.Drawing.CurrentView.Min.X, 6);
                Assert.Equal(40.0, scene.Drawing.CurrentView.Max.X, 6);
            }
        }

        [Fact]
        public void NamedViews_RoundTrip_SaveRestoreAndDelete_AndAnUnknownNameIsRefused()
        {
            using (var scene = new Scene())
            {
                scene.Drawing.ZoomWindow(P(0, 0), P(40, 30));
                DisplayBox saved = scene.Shown();
                scene.Drawing.SaveView("Corner");
                scene.Drawing.SaveView("a");
                Assert.Equal(new[] { "a", "Corner" }, scene.Drawing.NamedViews);

                scene.Drawing.ZoomExtents();
                scene.Drawing.RestoreView("corner");
                AssertBox(saved, scene.Drawing.CurrentView);

                scene.Drawing.DeleteView("Corner");
                Assert.Equal(new[] { "a" }, scene.Drawing.NamedViews);
                Assert.Contains(
                    "no view named Corner",
                    Assert.Throws<CadRemoteException>(() => scene.Drawing.RestoreView("Corner")).Message,
                    StringComparison.Ordinal
                );
                Assert.Throws<CadRemoteException>(() => scene.Drawing.DeleteView("Corner"));
                Assert.Throws<CadRemoteException>(() => scene.Drawing.SaveView("bad/name"));
                Assert.Throws<ArgumentNullException>(() => scene.Drawing.SaveView(null));
            }
        }

        [Fact]
        public void Variables_RoundTripAnIntegerADoubleATextAndAPoint_AndRefusalsComeInTheEditorsWords()
        {
            using (var scene = new Scene())
            {
                Assert.Equal(2, Assert.IsType<int>(scene.Drawing.GetVariable("LUNITS")));
                scene.Drawing.SetVariable("LUNITS", 4);
                Assert.Equal(4, Assert.IsType<int>(scene.Drawing.GetVariable("LUNITS")));
                Assert.Equal(4, scene.OnApp(() => scene.Document.Database.Lunits));

                scene.Drawing.SetVariable("LTSCALE", 2.5);
                Assert.Equal(2.5, Assert.IsType<double>(scene.Drawing.GetVariable("ltscale")));
                scene.Drawing.SetVariable("LTSCALE", 3.0);
                Assert.Equal(3.0, Assert.IsType<double>(scene.Drawing.GetVariable("LTSCALE")));

                Assert.Equal("0", Assert.IsType<string>(scene.Drawing.GetVariable("CLAYER")));
                scene.Drawing.SetVariable("INSBASE", new[] { 1.5, 2.5, 0.0 });
                Assert.Equal(new[] { 1.5, 2.5, 0.0 }, Assert.IsType<double[]>(scene.Drawing.GetVariable("INSBASE")));

                Assert.Contains(
                    "The system variable NOSUCH is not known.",
                    Assert.Throws<CadRemoteException>(() => scene.Drawing.GetVariable("NOSUCH")).Message,
                    StringComparison.Ordinal
                );
                Assert.Contains(
                    "is read only.",
                    Assert
                        .Throws<CadRemoteException>(() => scene.Drawing.SetVariable("EXTMIN", new[] { 0.0, 0.0, 0.0 }))
                        .Message,
                    StringComparison.Ordinal
                );
                Assert.Contains(
                    "The value 7 is refused for LUNITS.",
                    Assert.Throws<CadRemoteException>(() => scene.Drawing.SetVariable("LUNITS", 7)).Message,
                    StringComparison.Ordinal
                );
                Assert.Throws<ArgumentException>(() => scene.Drawing.SetVariable("LUNITS", true));
                Assert.Throws<ArgumentNullException>(() => scene.Drawing.SetVariable("LUNITS", null));
                Assert.Equal(4, scene.Drawing.GetVariable("LUNITS"));
                Assert.Equal("SETVAR", scene.OnApp(() => scene.Document.NextUndo.Name));
            }
        }

        [Fact]
        public void SelectAll_SelectWindow_AndSelectCrossingWindow_GiveCopiesWithHandles_AndLeaveTheUsersSelection()
        {
            using (var scene = new Scene())
            {
                IReadOnlyList<DxfEntity> everything = scene.Drawing.GetObjects();
                scene.Drawing.Select(new[] { scene.Drawing.HandleOf(everything[0]) });

                IReadOnlyList<DxfEntity> all = scene.Drawing.SelectAll();
                IReadOnlyList<DxfEntity> inside = scene.Drawing.SelectWindow(P(-1, -1), P(101, 1));
                IReadOnlyList<DxfEntity> across = scene.Drawing.SelectCrossingWindow(P(40, 20), P(60, 30));
                IReadOnlyList<DxfEntity> none = scene.Drawing.SelectWindow(P(40, 20), P(60, 30));

                Assert.Equal(2, all.Count);
                Assert.IsType<DxfLine>(all[0]);
                Assert.IsType<DxfCircle>(all[1]);
                Assert.Equal(scene.Drawing.HandleOf(everything[0]), scene.Drawing.HandleOf(all[0]));
                Assert.IsType<DxfLine>(Assert.Single(inside));
                Assert.IsType<DxfCircle>(Assert.Single(across));
                Assert.Empty(none);
                Assert.Single(scene.Drawing.GetSelectedObjects());
            }
        }

        [Fact]
        public void WithAWindow_SelectWindowIsHeldToTheView()
        {
            using (var scene = new Scene())
            {
                scene.OnApp(() =>
                {
                    scene.Session.View = new CadView(400, 300);
                    scene.Session.View.ZoomTo(new DisplayBox(-20, -20, 20, 10), 0.0);
                });

                // The circle is outside the view though the window takes it; the line crosses the view's bottom.
                Assert.Empty(scene.Drawing.SelectWindow(P(-1, -30), P(110, 100)).OfType<DxfCircle>());
                Assert.Single(scene.Drawing.SelectCrossingWindow(P(-1, -1), P(110, 100)).OfType<DxfLine>());
            }
        }

        // ---- inside a run ----

        [Fact]
        public void ANewOpInsideARun_JoinsTheRunsUndoStep()
        {
            using (var harness = new Harness())
            {
                var host = new TestHost();
                CadDocument document = host.Add(Reference("A.dxf"));
                CadCommandSession session = host.SessionOf(document);
                CadRemoteServer tools = harness.ServeTools(host);
                string token = tools.IssueToken();
                CadRemoteToolRun run = null;
                CadCommand command = tools.CreateRunCommand("STAIRSPY", false, token, r => run = r);
                DisplayBox start = host.OnAppThread(() => session.CommandView.VisibleBox);
                Assert.True(host.OnAppThread(() => session.Begin(command)));
                CadApplication client = harness.ConnectTool(tools, token);
                CadRemoteRun joined = client.JoinRun(run.Id);
                CadRemoteDocument drawing = client.Documents.First(d => d.Name == document.Title);

                drawing.ZoomWindow(P(0, 0), P(10, 10));
                drawing.SetVariable("LTSCALE", 4.0);
                drawing.SaveView("InRun");
                drawing.Insert(new DxfLine(new GeoPoint3(0, 100, 0), new GeoPoint3(100, 100, 0)));
                drawing.CommitChanges();
                Assert.False(host.OnAppThread(() => document.CanUndo));
                joined.End();

                Assert.Equal("STAIRSPY", host.OnAppThread(() => document.NextUndo.Name));
                Assert.Equal(4.0, host.OnAppThread(() => session.Editor.GetVariable("LTSCALE")));
                host.OnAppThread(() => document.Undo());

                Assert.False(host.OnAppThread(() => document.CanUndo));
                Assert.Equal(1.0, host.OnAppThread(() => session.Editor.GetVariable("LTSCALE")));
                Assert.Empty(host.OnAppThread(() => session.Editor.NamedViews));
                Assert.Equal(2, host.OnAppThread(() => document.Database.ModelSpace.Count(e => !e.IsErased)));
                DisplayBox back = host.OnAppThread(() => session.CommandView.VisibleBox);
                Assert.Equal(start.MinX, back.MinX, 9);
                Assert.Equal(start.MaxY, back.MaxY, 9);
            }
        }
    }
}
