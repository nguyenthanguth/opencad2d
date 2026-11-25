using System;
using System.Collections.Generic;
using System.Linq;
using GeometryHelper.Dxf;
using GeometryHelper.Geometry;
using OpenCAD2D.Plugins;
using Xunit;
using static OpenCAD2D.UnitTest.EditingDrawings;

namespace OpenCAD2D.UnitTest
{
    /// <summary>
    /// A ribbon button's macro (A2P P5): <see cref="CadRibbonButton.FromMacro"/> and the session that runs it as AutoCAD's CUI
    /// buttons do, <c>^C</c> cancelling, <c>;</c> and a space Enter, <c>\</c> waiting for the user and a parenthesis AutoLISP.
    /// </summary>
    public sealed class RibbonMacroTests
    {
        private static void Macro(CadCommandSession session, string macro) =>
            session.RunMacro(macro, "Probe", session.Feed);

        private static DxfCircle OnlyCircle(CadDocument document) => Assert.Single(All<DxfCircle>(document.ModelSpace));

        [Fact]
        public void AMacroOfACommandAndItsAnswers_DrawsAtOnce()
        {
            CadDocument document = Blank();
            CadCommandSession session = Session(document);

            Macro(session, "^C^C_CIRCLE;0,0;50;");

            DxfCircle circle = OnlyCircle(document);
            Near(new GeoPoint3(0, 0, 0), circle.Center);
            Assert.Equal(50.0, circle.Radius, 9);
            Assert.False(session.IsRunning);
            Assert.Equal("CIRCLE", document.NextUndo.Name);
            document.Undo();
            Assert.False(document.CanUndo);
        }

        [Fact]
        public void AMacroEndingInAWord_EntersIt()
        {
            CadCommandSession session = Session(Blank());

            Macro(session, "^C^CLINE");

            Assert.Equal("LINE", session.Current.Name);
            Assert.Equal("Specify first point:", session.PromptText);
        }

        [Fact]
        public void ABackslash_WaitsForTheUser_ThenGoesOn()
        {
            CadDocument document = Blank();
            CadCommandSession session = Session(document);

            Macro(session, "^C^C_CIRCLE;\\50;");

            Assert.Equal("CIRCLE", session.Current.Name);
            Assert.Contains("center", session.PromptText);
            Assert.Empty(All<DxfCircle>(document.ModelSpace));

            session.Feed("10,10");

            DxfCircle circle = OnlyCircle(document);
            Near(new GeoPoint3(10, 10, 0), circle.Center);
            Assert.Equal(50.0, circle.Radius, 9);
            Assert.False(session.IsRunning);
        }

        [Fact]
        public void TwoBackslashes_WaitForTwoAnswers()
        {
            CadDocument document = Blank();
            CadCommandSession session = Session(document);

            Macro(session, "^C^C_LINE;\\\\;");

            Assert.Equal("LINE", session.Current.Name);
            session.Feed("0,0");
            Assert.True(session.IsRunning);
            Assert.Empty(All<DxfLine>(document.ModelSpace));
            session.Feed("30,0");

            Assert.False(session.IsRunning);
            DxfLine line = Assert.Single(All<DxfLine>(document.ModelSpace));
            Near(new GeoPoint3(30, 0, 0), line.EndPoint);
            Assert.Equal("LINE", document.NextUndo.Name);
        }

        [Fact]
        public void EscAtAPause_DropsTheRest()
        {
            CadDocument document = Blank();
            CadCommandSession session = Session(document);
            Macro(session, "^C^C_CIRCLE;\\50;");

            session.Escape();
            session.Feed("10,10");

            Assert.Empty(All<DxfCircle>(document.ModelSpace));
            Assert.False(session.IsRunning);
            Assert.StartsWith("Unknown command \"10,10\".", session.History.Last(), StringComparison.Ordinal);
        }

        [Fact]
        public void AMacroWithoutCtrlC_AnswersTheCommandRunning()
        {
            CadDocument document = Blank();
            CadCommandSession session = Run(Session(document), "LINE", P(0, 0));

            Macro(session, "@10,0;");

            Assert.Equal("LINE", session.Current.Name);
            session.Escape();
            DxfLine line = Assert.Single(All<DxfLine>(document.ModelSpace));
            Assert.Equal(10.0, line.EndPoint.X, 9);
            Assert.Equal(0.0, line.EndPoint.Y, 9);
        }

        [Fact]
        public void AnAutoLispExpression_IsOneToken()
        {
            CadDocument document = Blank();
            CadCommandSession session = Session(document);

            Macro(session, "^C^C(setq a 5) CIRCLE;0,0;!a;");

            DxfCircle circle = OnlyCircle(document);
            Assert.Equal(5.0, circle.Radius, 9);
            Assert.False(session.IsRunning);
        }

        [Theory]
        [InlineData("*^C^CLINE", "*")]
        [InlineData("^C^C_LINE;$M=$(if,1,2)", "$M=")]
        [InlineData("^C^C_LINE;^V", "^V")]
        public void ATokenThePresenterDoesNotRun_IsRefusedBeforeAnythingRuns(string macro, string token)
        {
            CadCommandSession session = Session(Blank());

            Macro(session, macro);

            Assert.False(session.IsRunning);
            Assert.Equal(
                "Probe: the macro " + macro + " uses " + token + ", which the presenter does not run.",
                session.History.Last()
            );
            Assert.DoesNotContain(session.History, line => line.StartsWith("Command:", StringComparison.Ordinal));
        }

        [Fact]
        public void AnAnswerTheCommandRefuses_StopsTheMacro()
        {
            CadDocument document = Blank();
            CadCommandSession session = Session(document);

            Macro(session, "^C^C_CIRCLE;abc;50;");

            Assert.Equal("Macro stopped at abc.", session.History.Last());
            Assert.Equal("CIRCLE", session.Current.Name);
            Assert.DoesNotContain(session.History, line => line.Contains("50"));
        }

        [Fact]
        public void ACancelOfTheMacroItself_DoesNotDropWhatFollowsAPause()
        {
            CadDocument document = Blank();
            CadCommandSession session = Session(document);
            Run(session, "LINE");

            Macro(session, "^C^C_CIRCLE;\\50;");
            session.Feed("0,0");

            Assert.Equal(50.0, OnlyCircle(document).Radius, 9);
        }

        [Fact]
        public void ACommandTheUserStartsAfterAPause_DropsTheRest()
        {
            CadDocument document = Blank();
            CadCommandSession session = Session(document);
            Macro(session, "^C^C_CIRCLE;\\50;");

            session.Start("LINE");
            session.Escape();
            session.Start("CIRCLE");
            session.Feed("0,0");

            Assert.Contains("radius", session.PromptText);
            Assert.True(session.IsRunning);
        }

        [Fact]
        public void FromMacro_MakesAButtonThatHasNeitherACommandNorACallback()
        {
            CadRibbonButton button = CadRibbonButton.FromMacro("Circle R10", "^C^C_CIRCLE;\\10;");

            Assert.Equal("Circle R10", button.Text);
            Assert.Equal("^C^C_CIRCLE;\\10;", button.Macro);
            Assert.Null(button.Command);
            Assert.Null(button.Run);
            Assert.Null(button.CanRun);
            Assert.Null(new CadRibbonButton("Line", "LINE").Macro);
        }

        [Fact]
        public void FromMacro_RefusesNullAndEmpty()
        {
            Assert.Throws<ArgumentNullException>(() => CadRibbonButton.FromMacro(null, "^C^CLINE"));
            Assert.Throws<ArgumentNullException>(() => CadRibbonButton.FromMacro("Line", null));
            Assert.Throws<ArgumentException>(() => CadRibbonButton.FromMacro("Line", " "));
            Assert.Throws<ArgumentException>(() => CadRibbonButton.FromMacro("Line", string.Empty));
        }
    }
}
