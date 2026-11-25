using System;
using System.Collections.Generic;
using System.Linq;
using GeometryHelper.Dxf;
using GeometryHelper.Geometry;
using Xunit;
using static OpenCAD2D.UnitTest.EditingDrawings;

namespace OpenCAD2D.UnitTest
{
    /// <summary>
    /// The editor of a session (<see cref="CadEditor"/>): the view, the named views, the selection by area and the system
    /// variables, each call held to what the typed command does in a twin session. The reference drawing is a line from
    /// (0, 0) to (100, 0) and a circle about (50, 50) of radius 25, extents (0, 0) to (100, 75), on the console view of a
    /// session with no window (942 by 534 pixels, 10 units high about the origin).
    /// </summary>
    public class EditorViewTests
    {
        private const double Close = 1E-4;

        private static CadCommandSession Reference(out CadDocument document)
        {
            document = Blank();
            CadCommandSession session = Session(document);
            Run(session, "LINE", P(0, 0), P(100, 0), Enter);
            Run(session, "CIRCLE", P(50, 50), "25");
            Assert.False(session.IsRunning);
            return session;
        }

        private static void AssertBox(
            double minX,
            double minY,
            double maxX,
            double maxY,
            DisplayBox box,
            double tolerance = Close
        )
        {
            Assert.Equal(minX, box.MinX, tolerance);
            Assert.Equal(minY, box.MinY, tolerance);
            Assert.Equal(maxX, box.MaxX, tolerance);
            Assert.Equal(maxY, box.MaxY, tolerance);
        }

        private static void AssertSame(DisplayBox expected, DisplayBox actual)
        {
            AssertBox(expected.MinX, expected.MinY, expected.MaxX, expected.MaxY, actual, 1E-9);
        }

        /// <summary>Lines along y = 0, 10, 20, 30 and 40, each from x 0 to 100, made in that order.</summary>
        private static CadCommandSession Ladder(out CadDocument document, out string[] handles)
        {
            document = Blank();
            CadCommandSession session = Session(document);

            for (int y = 0; y <= 40; y += 10)
            {
                Run(session, "LINE", P(0, y), P(100, y), Enter);
            }

            handles = All<DxfLine>(document.ModelSpace).Select(l => l.Handle).ToArray();
            Assert.Equal(5, handles.Length);
            return session;
        }

        // ---- the view ----

        [Fact]
        public void ZoomExtents_ShowsWhatTheTypedZoomExtentsShows_AsOneUndoStepNamedZoom()
        {
            CadCommandSession session = Reference(out CadDocument document);
            CadCommandSession twin = Reference(out CadDocument twinDocument);
            Run(twin, "ZOOM", "E");

            session.Editor.ZoomExtents();

            AssertBox(-23.50187, -4.16667, 123.50187, 79.16667, session.CommandView.VisibleBox);
            AssertSame(twin.CommandView.VisibleBox, session.CommandView.VisibleBox);
            Assert.Equal("ZOOM", document.NextUndo.Name);
            Assert.Equal(twinDocument.NextUndo.Name, document.NextUndo.Name);
        }

        [Fact]
        public void ZoomWindow_FitsTheWindowWithNoMargin_AsZoomWDoes()
        {
            CadCommandSession session = Reference(out _);
            CadCommandSession twin = Reference(out _);
            Run(twin, "ZOOM", "W", P(0, 0), P(10, 10));

            session.Editor.ZoomWindow(P(0, 0), P(10, 10));

            AssertBox(-3.82022, 0, 13.82022, 10, session.CommandView.VisibleBox);
            AssertSame(twin.CommandView.VisibleBox, session.CommandView.VisibleBox);
        }

        [Fact]
        public void ZoomWindow_TakesTheCornersInEitherOrder_AndRefusesAWindowWithNoWidthOrNoHeight()
        {
            CadCommandSession session = Reference(out CadDocument document);
            session.Editor.ZoomWindow(P(10, 10), P(0, 0));
            AssertBox(-3.82022, 0, 13.82022, 10, session.CommandView.VisibleBox);
            CadUndoStep step = document.NextUndo;

            Assert.Throws<ArgumentException>(() => session.Editor.ZoomWindow(P(5, 0), P(5, 10)));
            Assert.Throws<ArgumentException>(() => session.Editor.ZoomWindow(P(0, 5), P(10, 5)));
            Assert.Throws<ArgumentException>(() => session.Editor.ZoomWindow(P(0, 0), P(double.NaN, 10)));
            AssertBox(-3.82022, 0, 13.82022, 10, session.CommandView.VisibleBox);
            Assert.Same(step, document.NextUndo);
        }

        [Fact]
        public void ZoomObjects_ShowsTheBoxOfTheObjectsDrawn_AsZoomODoes()
        {
            CadCommandSession session = Reference(out CadDocument document);
            CadCommandSession twin = Reference(out CadDocument twinDocument);
            string circle = All<DxfCircle>(document.ModelSpace).Single().Handle;
            Run(twin, "ZOOM", "O", new[] { circle }, Enter);

            session.Editor.ZoomObjects(new[] { circle });

            AssertSame(twin.CommandView.VisibleBox, session.CommandView.VisibleBox);
            Assert.Equal("ZOOM", document.NextUndo.Name);
            Assert.Equal(50.0, session.CommandView.CenterX, 6);
            Assert.Equal(50.0, session.CommandView.CenterY, 6);
        }

        [Fact]
        public void ZoomObjects_LeavesTheViewAsItWas_ForObjectsOfAnotherSpaceAndForNoneAtAll()
        {
            CadCommandSession session = Reference(out CadDocument document);
            document.CurrentSpace = document.Spaces[1];
            Run(session, "LINE", P(0, 0), P(100, 100), Enter);
            string onPaper = All<DxfLine>(document.CurrentSpace).Single().Handle;
            string inModel = All<DxfCircle>(document.ModelSpace).Single().Handle;
            DisplayBox before = session.CommandView.VisibleBox;
            CadUndoStep step = document.NextUndo;
            int raised = 0;
            session.ViewChanged += (s, e) => raised++;

            session.Editor.ZoomObjects(new[] { inModel });
            session.Editor.ZoomObjects(new string[0]);
            session.Editor.ZoomObjects(new[] { "FFFF" });

            AssertSame(before, session.CommandView.VisibleBox);
            Assert.Same(step, document.NextUndo);
            Assert.Equal(0, raised);

            session.Editor.ZoomObjects(new[] { onPaper });
            Assert.NotEqual(before.MaxX, session.CommandView.VisibleBox.MaxX);
            Assert.Equal(1, raised);
        }

        [Fact]
        public void ZoomCenter_ShowsTheHeightGivenAboutTheCentre()
        {
            CadCommandSession session = Reference(out _);
            CadCommandSession twin = Reference(out _);
            Run(twin, "ZOOM", "C", P(50, 50), "20");

            session.Editor.ZoomCenter(P(50, 50), 20);

            AssertBox(32.35955, 40, 67.64045, 60, session.CommandView.VisibleBox);
            AssertSame(twin.CommandView.VisibleBox, session.CommandView.VisibleBox);
        }

        [Fact]
        public void ZoomCenterAndZoomScaled_RefuseAHeightOrFactorThatIsNotAFiniteNumberAboveNought()
        {
            CadCommandSession session = Reference(out _);

            Assert.Throws<ArgumentOutOfRangeException>(() => session.Editor.ZoomCenter(P(0, 0), 0.0));
            Assert.Throws<ArgumentOutOfRangeException>(() => session.Editor.ZoomCenter(P(0, 0), -1.0));
            Assert.Throws<ArgumentOutOfRangeException>(() => session.Editor.ZoomCenter(P(0, 0), double.NaN));
            Assert.Throws<ArgumentOutOfRangeException>(() =>
                session.Editor.ZoomCenter(P(0, 0), double.PositiveInfinity)
            );
            Assert.Throws<ArgumentOutOfRangeException>(() => session.Editor.ZoomScaled(0.0));
            Assert.Throws<ArgumentOutOfRangeException>(() => session.Editor.ZoomScaled(-2.0));
            Assert.Throws<ArgumentOutOfRangeException>(() => session.Editor.ZoomScaled(double.NaN));
            Assert.Throws<ArgumentOutOfRangeException>(() => session.Editor.ZoomScaled(double.PositiveInfinity));
        }

        [Fact]
        public void ZoomScaledByTwo_HalvesTheWidthSeenAboutTheSameCentre_AsZoom2XDoes()
        {
            CadCommandSession session = Reference(out _);
            CadCommandSession twin = Reference(out _);
            Run(twin, "ZOOM", "2X");
            DisplayBox before = session.CommandView.VisibleBox;

            session.Editor.ZoomScaled(2.0);

            DisplayBox after = session.CommandView.VisibleBox;
            Assert.Equal(before.Width / 2.0, after.Width, 9);
            Assert.Equal((before.MinX + before.MaxX) / 2.0, (after.MinX + after.MaxX) / 2.0, 9);
            Assert.Equal((before.MinY + before.MaxY) / 2.0, (after.MinY + after.MaxY) / 2.0, 9);
            AssertSame(twin.CommandView.VisibleBox, after);
        }

        [Fact]
        public void ZoomPrevious_GoesBackOneView_AndSaysFalseWhenThereIsNone_WritingNothing()
        {
            CadCommandSession session = Reference(out CadDocument document);
            DisplayBox start = session.CommandView.VisibleBox;
            int lines = session.History.Count;
            CadUndoStep step = document.NextUndo;

            Assert.False(session.Editor.ZoomPrevious());
            Assert.Equal(lines, session.History.Count);
            Assert.Same(step, document.NextUndo);
            AssertSame(start, session.CommandView.VisibleBox);

            session.Editor.ZoomExtents();
            DisplayBox extents = session.CommandView.VisibleBox;
            session.Editor.ZoomWindow(P(0, 0), P(10, 10));

            Assert.True(session.Editor.ZoomPrevious());
            AssertSame(extents, session.CommandView.VisibleBox);
            Assert.True(session.Editor.ZoomPrevious());
            AssertSame(start, session.CommandView.VisibleBox);
            Assert.False(session.Editor.ZoomPrevious());
            Assert.Equal(lines, session.History.Count);
        }

        [Fact]
        public void Pan_MovesTheDrawingByTheDisplacement_AsPanDoes_AsOneStepNamedPan()
        {
            CadCommandSession session = Reference(out CadDocument document);
            CadCommandSession twin = Reference(out _);
            session.Editor.ZoomWindow(P(0, 0), P(10, 10));
            Run(twin, "ZOOM", "W", P(0, 0), P(10, 10));
            Run(twin, "-PAN", P(10, 0), Enter);

            session.Editor.Pan(new GeoVector2(10, 0));

            AssertBox(-13.82022, 0, 3.82022, 10, session.CommandView.VisibleBox);
            AssertSame(twin.CommandView.VisibleBox, session.CommandView.VisibleBox);
            Assert.Equal("PAN", document.NextUndo.Name);
        }

        [Fact]
        public void UndoAfterAnApiZoom_PutsTheViewBack_AndRedoDoesItAgain()
        {
            CadCommandSession session = Reference(out CadDocument document);
            DisplayBox start = session.CommandView.VisibleBox;

            session.Editor.ZoomExtents();
            DisplayBox zoomed = session.CommandView.VisibleBox;
            Run(session, "U");
            AssertSame(start, session.CommandView.VisibleBox);
            Run(session, "REDO");
            AssertSame(zoomed, session.CommandView.VisibleBox);

            session.Editor.Pan(new GeoVector2(5, 5));
            Run(session, "U");
            AssertSame(zoomed, session.CommandView.VisibleBox);
            Assert.Equal(2, All<DxfLine>(document.ModelSpace).Count + All<DxfCircle>(document.ModelSpace).Count);
        }

        [Fact]
        public void InsideARunningCommand_TheViewCallsJoinItsUndoStep()
        {
            CadCommandSession session = Reference(out CadDocument document);
            DisplayBox start = session.CommandView.VisibleBox;
            session.Registry.Add(new CadCommand("ZDRAW", ZoomAndDraw));

            Run(session, "ZDRAW");
            Assert.False(session.IsRunning);

            Assert.Equal(3, All<DxfLine>(document.ModelSpace).Count + All<DxfCircle>(document.ModelSpace).Count);
            Assert.Equal("ZDRAW", document.NextUndo.Name);
            Run(session, "U");
            Assert.Equal(2, All<DxfLine>(document.ModelSpace).Count + All<DxfCircle>(document.ModelSpace).Count);
            AssertSame(start, session.CommandView.VisibleBox);
        }

        private static IEnumerable<CadPrompt> ZoomAndDraw(CadCommandContext c)
        {
            c.Editor.ZoomWindow(P(0, 0), P(10, 10));
            c.Editor.Pan(new GeoVector2(1, 1));
            c.Apply(null, s =>
                s.Append(c.Document.ModelSpace, new DxfLine(new GeoPoint3(0, 0, 0), new GeoPoint3(1, 1, 0)))
            );
            c.Editor.ZoomExtents();
            yield break;
        }

        [Fact]
        public void ThroughAViewport_ZoomExtentsFitsTheViewportsView_AsZoomDoes_AndZoomCenterIsRefusedInWords()
        {
            CadCommandSession session = InViewport(out CadDocument document, out string handle);
            CadCommandSession twin = InViewport(out CadDocument twinDocument, out string twinHandle);
            Run(twin, "ZOOM", "2X");
            Run(twin, "ZOOM", "E");
            session.Editor.ZoomScaled(2.0);
            session.Editor.ZoomExtents();

            DxfViewport viewport = Entity<DxfViewport>(document, handle);
            DxfViewport twinViewport = Entity<DxfViewport>(twinDocument, twinHandle);
            Assert.Equal(twinViewport.ViewHeight, viewport.ViewHeight, 9);
            Near(twinViewport.ViewCenter, viewport.ViewCenter);
            Assert.Equal("ZOOM", document.NextUndo.Name);

            InvalidOperationException refused = Assert.Throws<InvalidOperationException>(() =>
                session.Editor.ZoomCenter(P(0, 0), 10)
            );
            Assert.Equal(
                "ZOOM Center works in model space or on the paper: through a viewport ZOOM offers All, Extents, Previous and Window.",
                refused.Message
            );
            Assert.Throws<InvalidOperationException>(() => session.Editor.ZoomObjects(new[] { "1" }));
            Assert.True(session.Editor.ZoomPrevious());
        }

        /// <summary>A rectangle 2 000 by 1 000 in model space, and on Layout1 a viewport 200 by 100 worked through.</summary>
        private static CadCommandSession InViewport(out CadDocument document, out string handle)
        {
            document = Blank();
            CadCommandSession session = Session(document);
            Run(session, "REC", P(0, 0), P(2000, 1000));
            document.CurrentSpace = document.Spaces[1];
            Run(session, "MV", P(10, 10), P(210, 110));
            handle = All<DxfViewport>(document.CurrentSpace).Last().Handle;
            Run(session, "MS");
            Assert.Equal(handle, document.ActiveViewport.Handle);
            return session;
        }

        [Fact]
        public void SetCurrentView_FitsTheBoxWithNoMargin_AndGetCurrentViewGivesTheVisibleBox()
        {
            CadCommandSession session = Reference(out CadDocument document);

            session.Editor.SetCurrentView(new DisplayBox(0, 0, 10, 10));

            AssertBox(-3.82022, 0, 13.82022, 10, session.Editor.GetCurrentView());
            AssertSame(session.CommandView.VisibleBox, session.Editor.GetCurrentView());
            Assert.Equal("ZOOM", document.NextUndo.Name);
            Assert.Throws<ArgumentException>(() => session.Editor.SetCurrentView(DisplayBox.Empty));
            Assert.Throws<ArgumentException>(() => session.Editor.SetCurrentView(new DisplayBox(0, 0, 0, 10)));
            Assert.Throws<ArgumentException>(() => session.Editor.SetCurrentView(new DisplayBox(0, 0, 10, 0)));
        }

        [Fact]
        public void ViewChanged_IsRaisedByEveryViewCallThatMovedTheView_AndBySelectionAndVariablesNever()
        {
            CadCommandSession session = Reference(out _);
            int raised = 0;
            session.ViewChanged += (s, e) => raised++;

            session.Editor.ZoomExtents();
            session.Editor.ZoomWindow(P(0, 0), P(10, 10));
            session.Editor.ZoomCenter(P(1, 1), 5);
            session.Editor.ZoomScaled(2);
            session.Editor.Pan(new GeoVector2(1, 0));
            session.Editor.ZoomPrevious();
            session.Editor.SetCurrentView(new DisplayBox(0, 0, 5, 5));
            Assert.Equal(7, raised);

            session.Editor.SaveView("A");
            session.Editor.RestoreView("A");
            Assert.Equal(8, raised);

            session.Editor.SelectAll();
            session.Editor.SetVariable("LTSCALE", 2.0);
            session.Editor.Regen();
            Assert.Equal(8, raised);
        }

        [Fact]
        public void Regen_DoesWhatRegenDoes_TemporaryGraphicsClearedAndThePointSizeTaken_WithoutWritingItsLine()
        {
            CadCommandSession session = Reference(out CadDocument document);
            session.View = new CadView(800, 600);
            session.View.ZoomTo(new DisplayBox(0, 0, 100, 50), 0.0);
            session.AddTemporaryVectors(
                new[] { new CadTemporaryVector(new GeoPoint3(0, 0, 0), new GeoPoint3(10, 10, 0), 1, false) }
            );
            int lines = session.History.Count;
            CadUndoStep step = document.NextUndo;
            Assert.NotEmpty(session.TemporaryVectors);

            session.Editor.Regen();

            Assert.Empty(session.TemporaryVectors);
            Assert.Equal(session.View.VisibleBox.Height, document.PointViewHeight, 9);
            Assert.Equal(lines, session.History.Count);
            Assert.Same(step, document.NextUndo);
        }

        [Fact]
        public void HasWindow_IsFalseForASessionWithNoView_AndTheCallsStillWorkOnTheConsoleView()
        {
            CadCommandSession session = Reference(out _);
            Assert.False(session.Editor.HasWindow);
            Assert.Same(session.Document, session.Editor.Document);

            session.Editor.ZoomExtents();
            Assert.Null(session.View);
            AssertBox(-23.50187, -4.16667, 123.50187, 79.16667, session.Editor.GetCurrentView());

            session.View = new CadView(400, 300);
            Assert.True(session.Editor.HasWindow);
        }

        [Fact]
        public void TheEditorIsMadeOncePerSession_AndTheContextOfACommandGivesTheSessionsOwn()
        {
            CadCommandSession session = Reference(out _);
            CadEditor seen = null;
            session.Registry.Add(new CadCommand("SEEEDITOR", c => See(c, e => seen = e)));

            Run(session, "SEEEDITOR");

            Assert.Same(session.Editor, seen);
            Assert.Same(session.Editor, session.Editor);
            Assert.NotSame(session.Editor, Reference(out _).Editor);
        }

        private static IEnumerable<CadPrompt> See(CadCommandContext c, Action<CadEditor> take)
        {
            take(c.Editor);
            yield break;
        }

        // ---- the named views ----

        [Fact]
        public void SaveViewThenRestoreView_AfterZoomExtents_ShowsTheSavedBox_AndDeleteViewRemovesIt()
        {
            CadCommandSession session = Reference(out CadDocument document);
            session.Editor.ZoomWindow(P(0, 0), P(40, 30));
            DisplayBox saved = session.CommandView.VisibleBox;

            session.Editor.SaveView("Corner");

            Assert.Equal(new[] { "Corner" }, session.Editor.NamedViews);
            DxfViewTableRecord record = document.Database.ViewTable["Corner"];
            Assert.Equal((saved.MinX + saved.MaxX) / 2.0, record.CenterPoint.X, 9);
            Assert.Equal(saved.Width, record.Width, 9);
            Assert.Equal(saved.Height, record.Height, 9);
            Assert.Equal("VIEW", document.NextUndo.Name);
            Assert.Empty(document.Database.LayerStateManager.GetLayerStateNames());

            session.Editor.ZoomExtents();
            Assert.NotEqual(saved.Width, session.CommandView.VisibleBox.Width, 3);

            session.Editor.RestoreView("corner");
            AssertSame(saved, session.CommandView.VisibleBox);

            session.Editor.DeleteView("Corner");
            Assert.Empty(session.Editor.NamedViews);
            Assert.False(document.Database.ViewTable.Has("Corner"));
            Assert.Equal("VIEW", document.NextUndo.Name);
        }

        [Fact]
        public void NamedViews_AreListedAsDashViewQuestionMarkListsThem_AndASavedNameIsRedefined()
        {
            CadCommandSession session = Reference(out CadDocument document);
            session.Editor.SaveView("b");
            session.Editor.SaveView("A");
            session.Editor.SaveView("a2");

            Assert.Equal(new[] { "A", "a2", "b" }, session.Editor.NamedViews);

            session.Editor.ZoomWindow(P(0, 0), P(40, 30));
            DisplayBox moved = session.CommandView.VisibleBox;
            session.Editor.SaveView("A");
            Assert.Equal(3, session.Editor.NamedViews.Count);
            Assert.Equal(moved.Width, document.Database.ViewTable["A"].Width, 9);
        }

        [Fact]
        public void AnUnknownViewName_ThrowsForRestoreAndDelete_AndABadNameForSave()
        {
            CadCommandSession session = Reference(out CadDocument document);
            CadUndoStep step = document.NextUndo;

            Assert.Throws<ArgumentException>(() => session.Editor.RestoreView("Nothing"));
            Assert.Throws<ArgumentException>(() => session.Editor.DeleteView("Nothing"));
            Assert.Throws<ArgumentException>(() => session.Editor.SaveView(""));
            Assert.Throws<ArgumentException>(() => session.Editor.SaveView(" padded "));
            Assert.Throws<ArgumentException>(() => session.Editor.SaveView("a/b"));
            Assert.Throws<ArgumentNullException>(() => session.Editor.SaveView(null));
            Assert.Same(step, document.NextUndo);
            Assert.Empty(session.Editor.NamedViews);
        }

        [Fact]
        public void ARefusedEdit_OfANamedView_ThrowsWithTheRefusalsWords_AndChangesNothing()
        {
            // The hand-made drawing of the plan has no VIEW table to add a view to.
            CadDocument document = Plan();
            CadCommandSession session = Session(document);
            CadUndoStep step = document.NextUndo;

            InvalidOperationException refused = Assert.Throws<InvalidOperationException>(() =>
                session.Editor.SaveView("V")
            );

            Assert.Equal("The file has no VIEW table to add the view V to.", refused.Message);
            Assert.Same(step, document.NextUndo);
            Assert.False(document.IsInGroup);
            Assert.Empty(session.Editor.NamedViews);
        }

        // ---- the selection ----

        [Fact]
        public void SelectAll_GivesTheWorkingSpacesObjectsInTheDrawingsOrder_AFilterNarrowsIt_AndThePickfirstStaysAsItWas()
        {
            CadCommandSession session = Reference(out CadDocument document);
            string line = All<DxfLine>(document.ModelSpace).Single().Handle;
            string circle = All<DxfCircle>(document.ModelSpace).Single().Handle;
            document.Selection.Set(new[] { circle });

            IReadOnlyList<string> all = session.Editor.SelectAll();
            IReadOnlyList<string> circles = session.Editor.SelectAll(e => e is DxfCircle);

            Assert.Equal(new[] { line, circle }, all);
            Assert.Equal(new[] { circle }, circles);
            Assert.Equal(new[] { circle }, document.Selection.Handles);
            Assert.Empty(session.Editor.SelectAll(e => false));
        }

        [Fact]
        public void SelectAll_OfALayoutLeavesTheFirstViewportOut_AndThroughAViewportTakesModelSpace()
        {
            CadCommandSession session = InViewport(out CadDocument document, out string handle);

            // Through the viewport the working space is model space: its one rectangle.
            Assert.Single(session.Editor.SelectAll());

            Run(session, "PS");
            IReadOnlyList<string> onPaper = session.Editor.SelectAll();
            Assert.Equal(new[] { handle }, onPaper);
            Assert.DoesNotContain(onPaper, h => document.FindEntity(h) is DxfViewport v && v.Number == 1);
        }

        [Fact]
        public void SelectWindow_TakesOnlyWhatLiesWhollyInside_AndSelectCrossingWindowWhatItCrosses()
        {
            CadCommandSession session = Ladder(out _, out string[] lines);

            Assert.Equal(new[] { lines[1], lines[2], lines[3] }, session.Editor.SelectWindow(P(-1, 5), P(101, 35)));
            Assert.Equal(new[] { lines[2] }, session.Editor.SelectCrossingWindow(P(50, 15), P(60, 25)));
            Assert.Equal(new[] { lines[1], lines[2] }, session.Editor.SelectCrossingWindow(P(60, 25), P(50, 9)));
            Assert.Empty(session.Editor.SelectWindow(P(10, 5), P(20, 35)));
            Assert.Equal(
                new[] { lines[1], lines[2], lines[3] },
                session.Editor.SelectCrossingWindow(P(20, 35), P(10, 5))
            );
        }

        [Fact]
        public void SelectWindow_AndSelectCrossingWindow_LeaveThePickfirstSelectionAsItWas_AndIncludeLockedLayers()
        {
            CadCommandSession session = Ladder(out CadDocument document, out string[] lines);
            document.Selection.Set(new[] { lines[4] });
            document.Database.LayerTable.Add(new DxfLayerTableRecord { Name = "Held", ColorIndex = 1 });
            document.Database.LayerTable["Held"].IsLocked = true;
            ((DxfLine)document.FindEntity(lines[2])).Layer = "Held";

            IReadOnlyList<string> found = session.Editor.SelectWindow(P(-1, 5), P(101, 35));

            Assert.Equal(new[] { lines[1], lines[2], lines[3] }, found);
            Assert.Equal(new[] { lines[4] }, document.Selection.Handles);
        }

        [Fact]
        public void SelectWindow_WithAWindowIsHeldToTheView_AsTheSelectionsOwnWindowIs()
        {
            CadCommandSession session = Ladder(out _, out string[] lines);
            session.View = new CadView(400, 300);
            session.View.ZoomTo(new DisplayBox(35, 8.7, 35 + 22.6 * 4.0 / 3.0, 31.3), 0.0);
            Assert.True(session.Editor.HasWindow);

            Assert.Equal(new[] { lines[1], lines[2], lines[3] }, session.Editor.SelectWindow(P(-1, -1), P(101, 41)));
            Assert.Equal(
                new[] { lines[1], lines[2], lines[3] },
                session.Editor.SelectCrossingWindow(P(-1, -1), P(101, 41))
            );

            // With no window every line the box takes is taken.
            session.View = null;
            Assert.Equal(lines, session.Editor.SelectWindow(P(-1, -1), P(101, 41)));
        }

        // ---- the variables ----

        [Fact]
        public void GetVariable_GivesGetvarsValue_AnIntegerARealATextAndAPoint()
        {
            CadCommandSession session = Reference(out CadDocument document);

            Assert.Equal(2, Assert.IsType<int>(session.Editor.GetVariable("LUNITS")));
            Assert.Equal(1.0, Assert.IsType<double>(session.Editor.GetVariable("ltscale")));
            Assert.Equal("0", Assert.IsType<string>(session.Editor.GetVariable("CLAYER")));
            Assert.Equal("blank.dxf", Assert.IsType<string>(session.Editor.GetVariable("DWGNAME")));
            double[] base3 = Assert.IsType<double[]>(session.Editor.GetVariable("INSBASE"));
            Assert.Equal(3, base3.Length);
            double[] min = Assert.IsType<double[]>(session.Editor.GetVariable("EXTMIN"));
            Assert.Equal(3, min.Length);
            Assert.Equal(document.Database.Extmin.X, min[0], 9);
            Assert.Throws<ArgumentNullException>(() => session.Editor.GetVariable(null));
        }

        [Fact]
        public void SetVariable_IsSetvars_ADrawingVariableAsOneUndoStep()
        {
            CadCommandSession session = Reference(out CadDocument document);
            CadUndoStep drawn = document.NextUndo;

            session.Editor.SetVariable("LTSCALE", 2.5);
            Assert.Equal(2.5, session.Editor.GetVariable("LTSCALE"));
            Assert.Equal("SETVAR", document.NextUndo.Name);
            Assert.False(document.IsInGroup);

            session.Editor.SetVariable("LUNITS", 4);
            session.Editor.SetVariable("INSBASE", new[] { 1.0, 2.0, 0.0 });
            Assert.Equal(4, session.Editor.GetVariable("LUNITS"));
            Assert.Equal(new[] { 1.0, 2.0, 0.0 }, session.Editor.GetVariable("INSBASE"));

            Run(session, "U");
            Assert.Equal(new[] { 0.0, 0.0, 0.0 }, session.Editor.GetVariable("INSBASE"));
            Run(session, "U");
            Assert.Equal(2, session.Editor.GetVariable("LUNITS"));
            Run(session, "U");
            Assert.Equal(1.0, session.Editor.GetVariable("LTSCALE"));
            Assert.Same(drawn, document.NextUndo);
        }

        [Fact]
        public void SetVariable_OfAVariableAWholeRealIsGiven_TakesItAsSetvarDoes_AndTheSameValueMakesNoStep()
        {
            CadCommandSession session = Reference(out CadDocument document);
            CadUndoStep drawn = document.NextUndo;

            session.Editor.SetVariable("LUNITS", 2.0);
            session.Editor.SetVariable("LTSCALE", 1);
            session.Editor.SetVariable("LTSCALE", 1.0);

            Assert.Same(drawn, document.NextUndo);
            Assert.Equal(1.0, session.Editor.GetVariable("LTSCALE"));
        }

        private static IEnumerable<CadPrompt> SetScale(CadCommandContext c)
        {
            c.Editor.SetVariable("LTSCALE", 3.0);
            c.Editor.SetVariable("LUNITS", 3);
            yield break;
        }

        [Fact]
        public void SetVariable_InsideARunningCommand_JoinsItsUndoStep()
        {
            CadCommandSession session = Reference(out CadDocument document);
            session.Registry.Add(new CadCommand("SETSCALE", SetScale));

            Run(session, "SETSCALE");

            Assert.Equal(3.0, session.Editor.GetVariable("LTSCALE"));
            Assert.Equal("SETSCALE", document.NextUndo.Name);
            Run(session, "U");
            Assert.Equal(1.0, session.Editor.GetVariable("LTSCALE"));
            Assert.Equal(2, session.Editor.GetVariable("LUNITS"));
        }

        [Fact]
        public void SetVariable_ThrowsWithTheWordsOfTheRefusal_ForAnUnknownReadOnlyOrRefusedValue()
        {
            CadCommandSession session = Reference(out CadDocument document);
            CadUndoStep drawn = document.NextUndo;

            ArgumentException unknown = Assert.Throws<ArgumentException>(() => session.Editor.SetVariable("NOSUCH", 1));
            Assert.Equal("The system variable NOSUCH is not known.", unknown.Message);
            Assert.Equal(
                "The system variable NOSUCH is not known.",
                Assert.Throws<ArgumentException>(() => session.Editor.GetVariable("NOSUCH")).Message
            );
            Assert.Equal(
                "The system variable EXTMIN is read only.",
                Assert
                    .Throws<ArgumentException>(() => session.Editor.SetVariable("EXTMIN", new[] { 1.0, 2.0, 3.0 }))
                    .Message
            );
            Assert.Equal(
                "The value 7 is refused for LUNITS.",
                Assert.Throws<ArgumentException>(() => session.Editor.SetVariable("LUNITS", 7)).Message
            );
            Assert.Equal(
                "The value 1.5 is refused for LUNITS.",
                Assert.Throws<ArgumentException>(() => session.Editor.SetVariable("LUNITS", 1.5)).Message
            );
            Assert.Throws<ArgumentException>(() => session.Editor.SetVariable("LTSCALE", "wide"));
            Assert.Throws<ArgumentNullException>(() => session.Editor.SetVariable(null, 1));
            Assert.Throws<ArgumentNullException>(() => session.Editor.SetVariable("LTSCALE", null));
            Assert.Same(drawn, document.NextUndo);
            Assert.Equal(2, session.Editor.GetVariable("LUNITS"));
        }
    }
}
