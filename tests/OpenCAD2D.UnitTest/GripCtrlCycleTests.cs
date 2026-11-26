using System.Collections.Generic;
using System.Linq;
using GeometryHelper;
using GeometryHelper.Dxf;
using GeometryHelper.Geometry;
using Xunit;
using static OpenCAD2D.UnitTest.EditingDrawings;

namespace OpenCAD2D.UnitTest
{
    /// <summary>
    /// Ctrl cycling of a multi-functional grip, as AutoCAD 2021's help describes it: with the grip hot, each Ctrl press goes to
    /// the next option of the grip's menu and back to the first after the last, the command line naming the option. A polyline's
    /// vertex cycles Stretch Vertex, Add Vertex, Remove Vertex; the middle of a straight segment Stretch, Add Vertex, Convert to Arc.
    /// </summary>
    public class GripCtrlCycleTests
    {
        // A rectangle 100 by 50, closed: vertices 0 to 3, then the middles of its four segments (grips 4 to 7).
        private static CadDocument Rectangle(bool closed = true) =>
            CadDocument.From(
                TestDrawings.Drawing(
                    null,
                    null,
                    TestDrawings.Polyline("0", "R1", closed, 0, 0, 0, 100, 0, 0, 100, 50, 0, 0, 50, 0)
                ),
                "ctrl.dxf"
            );

        private static CadCommandSession Hot(CadDocument document, string handle, int grip)
        {
            CadCommandSession session = Session(document);
            session.Tolerance = Tolerance.Default;
            document.Selection.Set(new[] { handle });
            Assert.True(session.StartGripEdit(new[] { new KeyValuePair<string, int>(handle, grip) }));
            return session;
        }

        private static DxfPolyline Poly(CadDocument document) => Entity<DxfPolyline>(document, "R1");

        [Fact]
        public void OnAVertexGrip_CtrlCyclesStretchAddVertexRemoveVertexAndBackToStretch()
        {
            CadDocument document = Rectangle();
            CadCommandSession session = Hot(document, "R1", 1);
            Assert.Equal("** STRETCH **", session.History.Last());
            Assert.Equal("Specify stretch point or [Base point/Copy/Undo/eXit]:", session.PromptText);

            Assert.True(session.CycleGripOption());
            Assert.True(session.IsGripEditing);
            Assert.Equal("** ADD VERTEX **", session.History.Last());
            Assert.Equal("Specify new vertex point or [Base point/Copy/Undo/eXit]:", session.PromptText);

            Assert.True(session.CycleGripOption());
            Assert.True(session.IsGripEditing);
            Assert.Equal("** REMOVE VERTEX **", session.History.Last());
            Assert.Equal("Pick to remove vertex:", session.PromptText);

            Assert.True(session.CycleGripOption());
            Assert.True(session.IsGripEditing);
            Assert.Equal("** STRETCH **", session.History.Last());
            Assert.Equal("Specify stretch point or [Base point/Copy/Undo/eXit]:", session.PromptText);

            // The cycle writes no "*Cancel*" and changes nothing in the drawing.
            Assert.DoesNotContain("*Cancel*", session.History);
            Assert.False(document.CanUndo);
            Assert.Equal(4, Poly(document).NumberOfVertices);
        }

        [Fact]
        public void OnTheMiddleOfAStraightSegment_CtrlCyclesStretchAddVertexConvertToArcAndBackToStretch()
        {
            CadDocument document = Rectangle();
            CadCommandSession session = Hot(document, "R1", 4);
            Assert.Equal("** STRETCH **", session.History.Last());

            Assert.True(session.CycleGripOption());
            Assert.Equal("** ADD VERTEX **", session.History.Last());
            Assert.Equal("Specify new vertex point or [Base point/Copy/Undo/eXit]:", session.PromptText);

            Assert.True(session.CycleGripOption());
            Assert.Equal("** CONVERT TO ARC **", session.History.Last());
            Assert.Equal("Specify midpoint of arc segment:", session.PromptText);

            Assert.True(session.CycleGripOption());
            Assert.Equal("** STRETCH **", session.History.Last());
            Assert.True(session.IsGripEditing);
        }

        [Fact]
        public void OnTheMiddleOfAnArcSegment_TheThirdOptionIsConvertToLine_WhichPicksAsAutoCadsTableSays()
        {
            CadDocument document = CadDocument.From(
                TestDrawings.Drawing(
                    null,
                    null,
                    TestDrawings.Polyline("0", "R1", false, 0, 0, 0.7, 100, 0, 0, 100, 50, 0)
                ),
                "ctrlarc.dxf"
            );
            CadCommandSession session = Hot(document, "R1", 3);

            Assert.True(session.CycleGripOption());
            Assert.True(session.CycleGripOption());

            Assert.Equal("** CONVERT TO LINE **", session.History.Last());
            Assert.Equal("Pick to convert to line segment:", session.PromptText);

            Run(session, P(10, 10));

            Assert.False(session.IsRunning);
            Assert.Equal(0.0, Poly(document).GetBulgeAt(0));
            Assert.Equal("GRIP", document.NextUndo.Name);
        }

        [Fact]
        public void AVertexStretchedAfterAFullCycle_MovesTheVertex()
        {
            CadDocument document = Rectangle();
            CadCommandSession session = Hot(document, "R1", 1);
            session.CycleGripOption();
            session.CycleGripOption();
            session.CycleGripOption();

            Run(session, P(110, -5));

            Assert.False(session.IsRunning);
            Assert.Equal(4, Poly(document).NumberOfVertices);
            Near(P(110, -5), Poly(document).GetPoint2dAt(1));
            Assert.Equal("GRIP", document.NextUndo.Name);
            Assert.True(document.Undo());
            Assert.False(document.CanUndo);
        }

        [Fact]
        public void AddVertexReachedByCtrl_PutsTheVertexAfterTheGripsVertex_AsOneUndoStep()
        {
            CadDocument document = Rectangle();
            CadCommandSession session = Hot(document, "R1", 1);
            session.CycleGripOption();

            Run(session, P(120, 25));

            Assert.False(session.IsRunning);
            Assert.Equal(5, Poly(document).NumberOfVertices);
            Near(P(100, 0), Poly(document).GetPoint2dAt(1));
            Near(P(120, 25), Poly(document).GetPoint2dAt(2));
            Near(P(100, 50), Poly(document).GetPoint2dAt(3));
            Assert.True(document.Undo());
            Assert.Equal(4, Poly(document).NumberOfVertices);
            Assert.False(document.CanUndo);
        }

        [Fact]
        public void RemoveVertexReachedByCtrl_WaitsForAPick_AndAnyPointTakesTheVertexOut()
        {
            CadDocument document = Rectangle();
            CadCommandSession session = Hot(document, "R1", 2);
            session.CycleGripOption();
            session.CycleGripOption();
            Assert.Equal(4, Poly(document).NumberOfVertices);
            Assert.False(document.CanUndo);

            Run(session, P(500, 500));

            Assert.False(session.IsRunning);
            Assert.Equal(3, Poly(document).NumberOfVertices);
            Near(P(0, 50), Poly(document).GetPoint2dAt(2));
            Assert.Equal("GRIP", document.NextUndo.Name);
            Assert.True(document.Undo());
            Assert.Equal(4, Poly(document).NumberOfVertices);
        }

        [Fact]
        public void ConvertToArcReachedByCtrl_GivesTheBulgeOfPointFour_ForAPoint20OffTheChord()
        {
            CadDocument document = CadDocument.From(
                TestDrawings.Drawing(null, null, TestDrawings.Polyline("0", "R1", false, 0, 0, 0, 100, 0, 0)),
                "ctrlconv.dxf"
            );
            CadCommandSession session = Hot(document, "R1", 2);
            session.CycleGripOption();
            session.CycleGripOption();

            Run(session, P(50, 20));

            Assert.False(session.IsRunning);
            Assert.Equal(-0.4, Poly(document).GetBulgeAt(0), 9);
            Assert.True(document.Undo());
            Assert.Equal(0.0, Poly(document).GetBulgeAt(0));
        }

        [Fact]
        public void EscAfterCycling_ChangesNothing()
        {
            CadDocument document = Rectangle();
            CadCommandSession session = Hot(document, "R1", 1);
            session.CycleGripOption();
            session.CycleGripOption();

            session.Escape();

            Assert.False(session.IsRunning);
            Assert.Equal(4, Poly(document).NumberOfVertices);
            Assert.False(document.CanUndo);
        }

        [Fact]
        public void RemoveVertexIsSkippedWhereItIsGrey_ATwoVertexPolylineCyclesStretchAndAddVertex()
        {
            CadDocument document = CadDocument.From(
                TestDrawings.Drawing(null, null, TestDrawings.Polyline("0", "R1", false, 0, 0, 0, 10, 0, 0)),
                "ctrltwo.dxf"
            );
            CadCommandSession session = Hot(document, "R1", 0);

            Assert.True(session.CycleGripOption());
            Assert.Equal("** ADD VERTEX **", session.History.Last());
            Assert.True(session.CycleGripOption());
            Assert.Equal("** STRETCH **", session.History.Last());
        }

        [Fact]
        public void AMenuItemChosenFirst_CyclesOnFromIt_AddVertexToRemoveVertexToStretch()
        {
            CadDocument document = Rectangle();
            CadCommandSession session = Session(document);
            session.Tolerance = Tolerance.Default;
            Assert.True(session.StartGripAction("R1", 1, CadGripAction.AddVertex));

            Assert.True(session.CycleGripOption());
            Assert.Equal("** REMOVE VERTEX **", session.History.Last());
            Assert.True(session.CycleGripOption());
            Assert.Equal("** STRETCH **", session.History.Last());
        }

        [Fact]
        public void ALinesEnd_CyclesStretchAndLengthen_AndTheMiddleOfALine_HasNoOptionToCycle()
        {
            CadDocument document = Plan();
            CadCommandSession session = Hot(document, "A1", 1);

            Assert.True(session.CycleGripOption());
            Assert.Equal("** LENGTHEN **", session.History.Last());
            Assert.True(session.CycleGripOption());
            Assert.Equal("** STRETCH **", session.History.Last());
            session.Escape();

            CadCommandSession middle = Hot(document, "A1", 2);
            Assert.False(middle.CycleGripOption());
            Assert.True(middle.IsGripEditing);
            Assert.Equal("** STRETCH **", middle.History.Last());
        }

        [Fact]
        public void NothingCycles_WithNoGripEditRunning_OrWithSeveralHotGrips_OrOnceAnEditIsMade()
        {
            CadDocument document = Rectangle();
            CadCommandSession idle = Session(document);
            Assert.False(idle.CycleGripOption());

            // Two hot grips stretch together: no one option to move on from.
            CadCommandSession two = Session(document);
            document.Selection.Set(new[] { "R1" });
            Assert.True(
                two.StartGripEdit(
                    new[] { new KeyValuePair<string, int>("R1", 0), new KeyValuePair<string, int>("R1", 1) }
                )
            );
            Assert.False(two.CycleGripOption());
            Assert.Equal("** STRETCH **", two.History.Last());
            two.Escape();

            // Add Vertex with Copy on has made a vertex: the option is the one the edit began with.
            CadCommandSession made = Hot(document, "R1", 3);
            made.CycleGripOption();
            Run(made, "C", P(-10, 40));
            Assert.True(made.IsRunning);
            Assert.False(made.CycleGripOption());
            Assert.Equal("** ADD VERTEX (multiple) **", made.History.Last());
            Run(made, "X");
            Assert.Equal(5, Poly(document).NumberOfVertices);
        }

        [Fact]
        public void ASplinesFitPoint_CyclesStretchAddRemoveInTheWordsOfItsTable_RemoveWaitingForAPick()
        {
            CadDocument document = Blank();
            var spline = new DxfSpline();
            document.Database.ModelSpace.AppendEntity(spline);
            spline.FitData = new DxfFitData(
                new[] { new GeoPoint3(0, 0, 0), new GeoPoint3(100, 50, 0), new GeoPoint3(200, 0, 0) },
                3,
                0.0
            );
            CadCommandSession session = Hot(document, spline.Handle, 1);

            Assert.True(session.CycleGripOption());
            Assert.Equal("** ADD FIT POINT **", session.History.Last());
            Assert.True(session.CycleGripOption());
            Assert.Equal("** REMOVE FIT POINT **", session.History.Last());
            Assert.Equal("Pick to remove fit point:", session.PromptText);

            Run(session, P(5, 5));

            Assert.False(session.IsRunning);
            Assert.Equal(2, Entity<DxfSpline>(document, spline.Handle).NumFitPoints);
        }

        [Fact]
        public void ACommandRunningThatIsNotAGripEdit_IsLeftAlone()
        {
            CadDocument document = Rectangle();
            CadCommandSession session = Session(document);
            session.Feed("LINE");

            Assert.False(session.CycleGripOption());

            Assert.True(session.IsRunning);
            Assert.False(session.IsGripEditing);
        }

        [Fact]
        public void CtrlHeldAtTheFirstPoint_StillTurnsCopyOn_AsBefore()
        {
            CadDocument document = Rectangle();
            CadCommandSession session = Hot(document, "R1", 1);

            Assert.True(session.Feed(P(110, -5), false, true));

            Assert.True(session.IsRunning);
            Assert.Equal("** STRETCH (multiple) **", session.History.Last());
        }
    }
}
