using System;
using System.IO;
using System.Linq;
using GeometryHelper;
using GeometryHelper.Dxf;
using GeometryHelper.Geometry;
using Xunit;
using static OpenCAD2D.UnitTest.EditingDrawings;

namespace OpenCAD2D.UnitTest
{
    /// <summary>
    /// SCRIPT reads a line as AutoCAD 2021 does (measured in accoreconsole with a .scr file): a space and the end of a line are
    /// both Enter, so <c>LINE 0,0 10,0 </c> is the word LINE, two points and an Enter that ends the line; two spaces in a row
    /// are two Enters; a tab is a space; where the prompt takes text (TEXT's "Enter text:", MTEXT) the rest of the line is the
    /// text, spaces and all (TEXT and -MTEXT go on asking for a line until a blank one); a form of AutoLISP in parentheses, and a string in it, keeps its spaces and may run over lines; and
    /// a line is a comment only when its first character is a semicolon.
    /// </summary>
    public sealed class ScriptWordsTests
    {
        private static CadDocument RunScript(string script, out CadCommandSession session)
        {
            CadDocument document = Blank();
            string file = Path.Combine(Path.GetTempPath(), "gh-scriptwords-" + Guid.NewGuid().ToString("N") + ".scr");
            File.WriteAllText(file, script);

            try
            {
                session = Session(document);
                session.Tolerance = Tolerance.Default;
                Run(session, "SCRIPT", file);
            }
            finally
            {
                File.Delete(file);
            }

            return document;
        }

        [Fact]
        public void ALineWithSpaces_IsTheCommandAndItsAnswers_AndATrailingSpaceIsTheEnterThatEndsLine()
        {
            CadDocument document = RunScript("LINE 0,0 10,0 \nCIRCLE 5,5 3\n", out CadCommandSession session);

            DxfLine line = Assert.Single(All<DxfLine>(document.ModelSpace));
            Near(new GeoPoint3(0, 0, 0), line.StartPoint);
            Near(new GeoPoint3(10, 0, 0), line.EndPoint);
            Assert.Equal(3.0, Assert.Single(All<DxfCircle>(document.ModelSpace)).Radius, 9);
            Assert.False(session.IsRunning);
            Assert.DoesNotContain(session.History, h => h.StartsWith("Script stopped", StringComparison.Ordinal));
        }

        [Fact]
        public void ATabIsASpace()
        {
            CadDocument document = RunScript("LINE\t0,0\t10,0\t\n", out _);

            Assert.Single(All<DxfLine>(document.ModelSpace));
        }

        [Fact]
        public void TwoSpacesInARow_AreTwoEnters_SoTheLineEndsAndTheNextWordIsACommand()
        {
            CadDocument document = RunScript("LINE 0,0 10,0  CIRCLE 5,5 3\n", out CadCommandSession session);

            Assert.Single(All<DxfLine>(document.ModelSpace));
            Assert.Single(All<DxfCircle>(document.ModelSpace));
            Assert.False(session.IsRunning);
        }

        [Fact]
        public void TheEndOfALineAfterAPoint_IsNotAnExtraEnter_SoTheLineGoesOnOnTheNextLine()
        {
            CadDocument document = RunScript("LINE 0,0 10,0\n10,10\n\n", out _);

            Assert.Equal(2, All<DxfLine>(document.ModelSpace).Count);
        }

        [Fact]
        public void TheTextOfTEXT_IsTheRestOfTheLine_SpacesKept()
        {
            CadDocument document = RunScript("TEXT 0,0 2 0 Hello  World \n\n", out CadCommandSession session);

            DxfDBText text = Assert.Single(All<DxfDBText>(document.ModelSpace));
            Assert.Equal("Hello  World ", text.TextString);
            Assert.False(session.IsRunning);
        }

        [Fact]
        public void TheTextOfMinusMTEXT_IsTheRestOfTheLine_AndABlankLineEndsIt()
        {
            CadDocument document = RunScript("-MTEXT 0,0 20,10 Hello World\n\n", out CadCommandSession session);

            DxfMText text = Assert.Single(All<DxfMText>(document.ModelSpace));
            Assert.Equal("Hello World", text.Contents);
            Assert.False(session.IsRunning);
        }

        [Fact]
        public void AFormOfLisp_KeepsItsSpaces_RunsOverLines_AndThenTheLineGoesOn()
        {
            CadDocument document = RunScript(
                "(setq a (list 1\n2))\n(setq s \"a b  c\")\n(setq t \"x\ny\")\n(setq q 5) LINE 0,0 (list 10.0 0.0 0.0) \n",
                out CadCommandSession session
            );

            Assert.Equal("a b  c", session.Lisp.GetValue("S"));
            Assert.Equal("x\ny", session.Lisp.GetValue("T"));
            Assert.Equal(5, session.Lisp.GetValue("Q"));
            DxfLine line = Assert.Single(All<DxfLine>(document.ModelSpace));
            Near(new GeoPoint3(10, 0, 0), line.EndPoint);
        }

        [Fact]
        public void OnlyALineThatStartsWithASemicolonIsAComment_EvenInsideAnOpenString()
        {
            CadDocument document = RunScript(
                "; whole line\nLINE 0,0 10,0 \n;LINE 3,3 4,4 \n(setq s \"a\n;b\nc\")\n",
                out CadCommandSession session
            );

            Assert.Single(All<DxfLine>(document.ModelSpace));
            Assert.Equal("a\nc", session.Lisp.GetValue("S"));
        }

        [Fact]
        public void ASemicolonAfterASpace_IsAWordOfTheCommand_AndIsRefused()
        {
            CadDocument document = RunScript("LINE 0,0 10,0 ; note\nCIRCLE 5,5 3\n", out CadCommandSession session);

            Assert.Equal("Script stopped at line 1: LINE 0,0 10,0 ; note", session.History.Last());
            Assert.Empty(All<DxfCircle>(document.ModelSpace));
        }

        [Fact]
        public void ASpaceBeforeAWord_IsAnEnterThatRepeatsTheLastCommand()
        {
            CadDocument document = RunScript("LINE 0,0 10,0 \n CIRCLE\n", out CadCommandSession session);

            Assert.Equal("Script stopped at line 2: CIRCLE", session.History.Last());
            Assert.Empty(All<DxfCircle>(document.ModelSpace));
        }
    }
}
