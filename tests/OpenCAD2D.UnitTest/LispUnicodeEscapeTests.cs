using System;
using System.Linq;
using Xunit;
using static OpenCAD2D.UnitTest.EditingDrawings;

namespace OpenCAD2D.UnitTest
{
    /// <summary>
    /// A <c>\U+XXXX</c> in a LISP string as AutoCAD 2021's accoreconsole measured it (probes of 2026-11-25): the string keeps
    /// its seven characters (<c>(strlen "\U+0111")</c> is 7), and the escape becomes the character in two places. What is
    /// shown does, wherever a prompt, a message or a result is written: <c>princ</c>, <c>prompt</c>, the message of every
    /// <c>get</c> function. What is stored does, wherever a routine hands a string to the drawing: the text of an
    /// <c>entmake</c>ed TEXT or MTEXT, a layer name, extended data, a system variable, the text typed to <c>command</c>.
    /// Exactly four hex digits count, in either case; a fifth digit, as in <c>\U+1F600</c>, is a character of its own. A
    /// string typed to a <c>get</c> function is not changed.
    /// </summary>
    public sealed class LispUnicodeEscapeTests : IDisposable
    {
        public void Dispose() => WaitingRoutines.EndAll();

        private static CadCommandSession Typed(params object[] inputs) => Run(Session(Blank()), inputs);

        private static string Value(CadCommandSession session, string form) => LispEntityTests.Value(session, form);

        // The code of the character at a place of the text of the last entity: (ascii (substr text place 1)).
        private static string CodeAt(CadCommandSession session, int place) =>
            Value(session, "(ascii (substr (cdr (assoc 1 (entget (entlast)))) " + place + " 1))");

        private const string LastText = "(strlen (cdr (assoc 1 (entget (entlast)))))";

        [Fact]
        public void GetpointPrompt_ShowsTheEscapesAsCharacters_OnTheCommandLineAndInTheHistory()
        {
            // AutoCAD 2021: (getpoint "\nCh\U+1ECDn \U+0111i\U+1EC3m \U+0111\U+1EA7u HCN :") asks "Chọn điểm đầu HCN :"
            CadCommandSession session = Typed(@"(getpoint ""\nCh\U+1ECDn \U+0111i\U+1EC3m \U+0111\U+1EA7u HCN :"")");

            Assert.Equal("Chọn điểm đầu HCN :", session.PromptText);

            Run(session, "1,2");

            string[] history = session.History.Select(l => l.TrimEnd()).ToArray();

            Assert.Contains("Chọn điểm đầu HCN :1,2", history);
            Assert.DoesNotContain(history.Skip(1), line => line.Contains("\\U+"));
        }

        [Theory]
        [InlineData(@"(getstring ""\n\U+0110i\U+1EC3m cu\U+1ED1i :"")", "Điểm cuối :")]
        [InlineData(@"(getreal ""\nB\U+00E1n k\U+00EDnh cong :"")", "Bán kính cong :")]
        [InlineData(@"(getint ""\nS\U+1ED1 l\U+01B0\U+1EE3ng :"")", "Số lượng :")]
        [InlineData(@"(getdist ""\nKho\U+1EA3ng c\U+00E1ch :"")", "Khoảng cách :")]
        [InlineData(@"(getangle ""\nG\U+00F3c :"")", "Góc :")]
        [InlineData(@"(getcorner '(0 0) ""\nG\U+00F3c \U+0111\U+1ED1i :"")", "Góc đối :")]
        [InlineData(@"(entsel ""\nCh\U+1ECDn \U+0111\U+1ED1i t\U+01B0\U+1EE3ng :"")", "Chọn đối tượng :")]
        [InlineData(@"(getkword ""\nCh\U+1ECDn [\U+0111\U+1ED3ng/Mo] :"")", "Chọn [đồng/Mo] :")]
        [InlineData(@"(getpoint ""\nlower \u+00e9 \U+00e9"")", "lower é é")] // either case, u or U, hex digits
        [InlineData(@"(getpoint ""\nfive \U+01111"")", "five đ1")] // a fifth digit is a character
        [InlineData(@"(getpoint ""\nshort \U+011 here"")", "short UU+011 here")] // fewer than four digits: UU+011
        [InlineData(@"(getpoint ""\nbad \U+ZZZZ"")", "bad UU+ZZZZ")] // not hex: UU+ZZZZ
        [InlineData(@"(getpoint ""\nmbcs \M+10111"")", "mbcs M+10111")] // \M+ is a letter M after a dropped backslash
        public void EveryGetFunctionsPrompt_ShowsTheEscapesAsCharacters(string form, string prompt)
        {
            CadCommandSession session = Typed(form);

            Assert.Equal(prompt, session.PromptText);
        }

        [Fact]
        public void TheLinesOfAMessageBeforeTheLastNewLine_ShowTheEscapesToo()
        {
            CadCommandSession session = Typed(@"(getpoint ""\nL\U+00F3\U+0111\nPick \U+0111: "")");

            Assert.Equal("Pick đ: ", session.PromptText);
            Assert.Contains("Lóđ", session.History.Select(l => l.TrimEnd()));
        }

        [Fact]
        public void AStringTypedToAGetFunction_IsNotChanged()
        {
            // AutoCAD 2021: typing \U+0111 at (getstring) gives the seven characters
            CadCommandSession session = Typed(@"(setq s (getstring ""\nName: ""))");

            Run(session, @"\U+0111");

            Assert.Equal("7", Value(session, "(strlen s)"));
        }

        [Fact]
        public void TheEscapeInAStringOfARoutine_StaysSevenCharacters()
        {
            CadCommandSession session = Typed(@"(setq s ""a\U+0111b"")");

            Assert.Equal("9", Value(session, "(strlen s)"));
        }

        [Fact]
        public void EntmakeOfAText_StoresTheCharacterOfTheEscape()
        {
            // AutoCAD 2021: (entmake '((0 . "TEXT") (10 0.0 0.0 0.0) (40 . 2.5) (1 . "a\U+0111b"))) holds the three characters 97 273 98
            CadCommandSession session = Typed(
                @"(entmake '((0 . ""TEXT"") (10 0.0 0.0 0.0) (40 . 2.5) (1 . ""a\U+0111b"")))"
            );

            Assert.Equal("3", Value(session, LastText));
            Assert.Equal("273", CodeAt(session, 2));
            Assert.Equal("98", CodeAt(session, 3));
        }

        [Fact]
        public void EntmakeOfAnMText_StoresTheCharacterOfTheEscape()
        {
            // AutoCAD 2021 (C2): the MTEXT of entmake with a\U+0111b holds 97 273 98
            CadCommandSession session = Typed(
                @"(entmake '((0 . ""MTEXT"") (100 . ""AcDbEntity"") (100 . ""AcDbMText"") (10 0.0 0.0 0.0) (40 . 2.5) (1 . ""a\U+0111b"")))"
            );

            Assert.Equal("3", Value(session, LastText));
            Assert.Equal("273", CodeAt(session, 2));
        }

        [Fact]
        public void EntmodOfAnMText_StoresTheCharacterOfTheEscape()
        {
            // AutoCAD 2021 (C6): entmod with (1 . "p\U+0111q") gives 112 273 113
            CadCommandSession session = Typed(
                @"(entmake '((0 . ""MTEXT"") (100 . ""AcDbEntity"") (100 . ""AcDbMText"") (10 0.0 0.0 0.0) (40 . 2.5) (1 . ""x"")))"
            );
            Run(session, @"(setq tx (entget (entlast)))");
            Run(session, @"(entmod (subst (cons 1 ""p\U+0111q"") (assoc 1 tx) tx))");

            Assert.Equal("3", Value(session, LastText));
            Assert.Equal("273", CodeAt(session, 2));
        }

        [Fact]
        public void TheTextTypedToCommandText_HoldsTheCharacterOfTheEscape()
        {
            // AutoCAD 2021 (C3): (command "_.TEXT" "0,5" "2.5" "0" "a\U+0111b") makes the text 97 273 98
            CadCommandSession session = Typed(@"(command ""_.TEXT"" ""0,5"" ""2.5"" ""0"" ""a\U+0111b"")");

            Assert.Equal("3", Value(session, LastText));
            Assert.Equal("273", CodeAt(session, 2));
        }

        [Fact]
        public void ALayerNamedWithAnEscape_HasTheCharacterInItsName_AndIsFoundByEitherSpelling()
        {
            // AutoCAD 2021 (F1, F3): the layer of entmake "L\U+0111" is 76 273; (tblsearch "layer" "L\U+0111") finds it
            CadCommandSession session = Typed(
                @"(entmake '((0 . ""LAYER"") (100 . ""AcDbSymbolTableRecord"") (100 . ""AcDbLayerTableRecord"") (2 . ""L\U+0111"") (70 . 0) (62 . 7) (6 . ""Continuous"")))"
            );

            Assert.Equal("2", Value(session, @"(strlen (cdr (assoc 2 (tblsearch ""layer"" ""L\U+0111""))))"));
            Assert.Equal(
                "273",
                Value(session, @"(ascii (substr (cdr (assoc 2 (tblsearch ""layer"" (strcat ""L"" (chr 273))))) 2 1))")
            );
        }

        [Fact]
        public void ExtendedDataOfAnEntmake_HoldsTheCharacterOfTheEscape()
        {
            // AutoCAD 2021 (F4b): the 1000 group of x\U+0111y is 120 273 121
            CadCommandSession session = Typed(@"(regapp ""GHAPP"")");
            Run(
                session,
                @"(entmake (list '(0 . ""TEXT"") '(10 0.0 0.0 0.0) '(40 . 2.5) '(1 . ""t"") (list -3 (list ""GHAPP"" (cons 1000 ""x\U+0111y"")))))"
            );

            Assert.Equal(
                "273",
                Value(
                    session,
                    @"(ascii (substr (cdr (cadr (assoc ""GHAPP"" (cdr (assoc -3 (entget (entlast) '(""GHAPP""))))))) 2 1))"
                )
            );
        }

        [Fact]
        public void ASystemVariableSetFromAString_HoldsTheCharacterOfTheEscape()
        {
            // AutoCAD 2021 (F6): (setvar "DIMPOST" "u\U+0111v") then getvar gives 117 273 118
            CadCommandSession session = Typed(@"(setvar ""DIMPOST"" ""u\U+0111v"")");

            Assert.Equal("3", Value(session, @"(strlen (getvar ""DIMPOST""))"));
            Assert.Equal("273", Value(session, @"(ascii (substr (getvar ""DIMPOST"") 2 1))"));
        }

        [Theory]
        [InlineData(@"x\U+00e9\u+01e9", "xéǩ")] // either case of u and of the hex digits (AutoCAD 2021, F8: 120 233 489)
        [InlineData(@"x\U+01111", "xđ1")] // a fifth digit is a character of its own (F9: 120 273 49)
        [InlineData(@"x\U+1F600", "xὠ0")] // the four digits 1F60 and a 0 (F10: 120 8032 48)
        [InlineData(@"x\M+10111", "xM+10111")] // \M+ is not an escape: the backslash is dropped at reading (F11)
        [InlineData(@"x\U+0000y", "x")] // U+0000 ends the text (F14: 120)
        [InlineData(@"x\U+0041y", "xAy")] // (F13)
        [InlineData(@"x%%u\U+0111", "x%%uđ")] // the %% codes are left to the text (F15)
        public void TheTextOfAnEntmake_FollowsTheRuleOfTheProbes(string written, string stored)
        {
            CadCommandSession session = Typed(
                "(entmake '((0 . \"TEXT\") (10 0.0 0.0 0.0) (40 . 2.5) (1 . \"" + written + "\")))"
            );

            Assert.Equal(stored.Length.ToString(), Value(session, LastText));

            for (int i = 0; i < stored.Length; i++)
            {
                Assert.Equal(((int)stored[i]).ToString(), CodeAt(session, i + 1));
            }
        }

        [Fact]
        public void AStringBuiltAtRunTime_IsStoredAsTheCharacterToo()
        {
            // AutoCAD 2021 (F12): (strcat (chr 92) "U+0111") put in a TEXT is stored as 273
            CadCommandSession session = Typed(@"(setq rt (strcat (chr 92) ""U+0111""))");
            Run(session, @"(entmake (list '(0 . ""TEXT"") '(10 0.0 0.0 0.0) '(40 . 2.5) (cons 1 rt)))");

            Assert.Equal("1", Value(session, LastText));
            Assert.Equal("273", CodeAt(session, 1));
        }

        [Fact]
        public void TextTypedAtTheTextCommandsPrompt_HoldsTheCharacterOfTheEscape_AndTheHistoryShowsIt()
        {
            // AutoCAD 2021 (C3, the console echo "Enter text: ađb"): the same as an argument of command
            CadCommandSession session = Typed("TEXT", "0,5", "2.5", "0", @"a\U+0111b", Enter);

            Assert.Contains("Enter text: ađb", session.History.Select(l => l.TrimEnd()));
            Assert.Equal("3", Value(session, LastText));
            Assert.Equal("273", CodeAt(session, 2));
        }

        [Fact]
        public void TextTypedToTheLayerCommand_NamesTheLayerWithTheCharacter()
        {
            // AutoCAD 2021 (C4): -LAYER Make with L\U+0111 makes the layer 76 273 and sets it current
            CadCommandSession session = Typed("-LAYER", "Make", @"L\U+0111", Enter);

            Assert.Equal("2", Value(session, @"(strlen (getvar ""CLAYER""))"));
            Assert.Equal("273", Value(session, @"(ascii (substr (getvar ""CLAYER"") 2 1))"));
        }

        [Fact]
        public void AnAlert_ShowsTheEscapesAsCharacters()
        {
            // AutoCAD 2021 (probe5): (alert "\U+0111x") shows the message box "đx"
            CadCommandSession session = Typed(@"(alert ""\U+0111x"")");

            Assert.Contains("đx", session.History.Select(l => l.TrimEnd()));
        }
    }
}
