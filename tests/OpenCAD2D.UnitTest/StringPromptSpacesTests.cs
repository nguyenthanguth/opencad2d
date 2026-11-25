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
    /// Which prompts for text take a space (<see cref="CadPrompt.TakesSpaces"/>), as AutoCAD 2021 does: most end at a space as
    /// Enter does (a name, a file name, a list of names) and a few take the whole line (the text of TEXT, a description, the
    /// value of an attribute). Each case was measured in accoreconsole with a .scr file whose answer holds a space ("AA BB");
    /// before the prompts were set one by one every prompt for text took the space.
    /// </summary>
    public sealed class StringPromptSpacesTests
    {
        private static CadDocument RunScript(string script, out CadCommandSession session)
        {
            CadDocument document = Blank();
            string file = Path.Combine(Path.GetTempPath(), "gh-stringspaces-" + Guid.NewGuid().ToString("N") + ".scr");
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

        /// <summary>Runs the inputs and gives the prompt the last one left, after checking its text is the one the case means.</summary>
        private static CadPrompt PromptAfter(string asked, object[] inputs)
        {
            CadCommandSession session = Session(Blank());
            session.Tolerance = Tolerance.Default;
            Run(session, inputs);

            Assert.True(session.IsRunning, "No command waits after " + inputs.Length + " inputs.");
            Assert.Contains(asked, session.Prompt.Text, StringComparison.OrdinalIgnoreCase);
            return session.Prompt;
        }

        public static TheoryData<string, object[]> EndAtASpace() =>
            new TheoryData<string, object[]>
            {
                { "Enter name list for new layer", new object[] { "-LAYER", "N" } },
                { "Enter name for new layer", new object[] { "-LAYER", "M" } },
                { "Enter layer name to make current", new object[] { "-LAYER", "S" } },
                { "Enter old layer name", new object[] { "-LAYER", "R" } },
                { "Enter name list of layer(s) to turn off", new object[] { "-LAYER", "OFF" } },
                { "Enter name list of layer(s) to freeze", new object[] { "-LAYER", "F" } },
                { "Enter old layer name", new object[] { "-RENAME", "LA" } },
                { "Enter old block name", new object[] { "-RENAME", "B" } },
                { "Enter view name to save", new object[] { "-VIEW", "S" } },
                { "Enter view name(s) to delete", new object[] { "-VIEW", "D" } },
                { "Enter name to save current UCS", new object[] { "UCS", "S" } },
                { "Enter name of UCS to restore", new object[] { "UCS", "R" } },
                { "Enter name of text style", new object[] { "-STYLE" } },
                { "Enter linetype(s) to load", new object[] { "-LINETYPE", "L" } },
                { "Enter name of linetype to create", new object[] { "-LINETYPE", "C" } },
                { "Enter a group name", new object[] { "-GROUP", "C" } },
                { "Enter attribute tag name", new object[] { "-ATTDEF", Enter } },
                { "Enter name(s) to purge", new object[] { "-PURGE", "B" } },
                { "Enter variable(s) to list", new object[] { "SETVAR", "?" } },
                { "Enter new value for CLAYER", new object[] { "SETVAR", "CLAYER" } },
                { "Enter new value for TEXTSTYLE", new object[] { "SETVAR", "TEXTSTYLE" } },
                { "Enter dimension style(s) to list", new object[] { "-DIMSTYLE", "?" } },
                { "Enter dimension variable name to override", new object[] { "DIMOVERRIDE" } },
                { "Enter command name to repeat", new object[] { "MULTIPLE" } },
                { "Enter script file name", new object[] { "SCRIPT" } },
                { "Enter DXF file name", new object[] { "DXFOUT" } },
                { "Enter name of output file", new object[] { "-WBLOCK" } },
                { "Enter name of file to attach", new object[] { "-XREF", "A" } },
                { "Enter image file name to attach", new object[] { "-IMAGE", "A" } },
                { "Enter new Layout name", new object[] { "-LAYOUT", "N" } },
                { "Enter mline style name", new object[] { "MLINE", "ST" } },
                { "Enter name(s) of viewport configurations to delete", new object[] { "-VPORTS", "D" } },
                { "Enter UCS name(s) to list", new object[] { "UCS", "?" } },
            };

        [Theory]
        [MemberData(nameof(EndAtASpace))]
        public void AName_AFileName_AndAListOfNames_EndAtASpace(string asked, object[] inputs)
        {
            Assert.False(PromptAfter(asked, inputs).TakesSpaces);
        }

        public static TheoryData<string, object[]> TakeTheWholeLine() =>
            new TheoryData<string, object[]>
            {
                { "Enter text", new object[] { "TEXT", new GeoPoint2(0, 0), "2", "0" } },
                {
                    "Enter dimension text",
                    new object[] { "DIMLINEAR", new GeoPoint2(0, 0), new GeoPoint2(10, 0), "T" }
                },
                { "Enter layer description", new object[] { "-LAYER", "D" } },
                { "Enter a group description", new object[] { "-GROUP", "C", "GG" } },
                { "Enter attribute prompt", new object[] { "-ATTDEF", Enter, "TAG1" } },
                { "Enter default attribute value", new object[] { "-ATTDEF", Enter, "TAG1", "PROMPT" } },
                { "Enter name for new scale", new object[] { "-SCALELISTEDIT", "A" } },
                { "Enter scale ratio", new object[] { "-SCALELISTEDIT", "A", "XX" } },
                { "Enter new value for DIMPOST", new object[] { "SETVAR", "DIMPOST" } },
                { "Enter new value for CANNOSCALE", new object[] { "SETVAR", "CANNOSCALE" } },
                { "Enter new value for HPLAYER", new object[] { "SETVAR", "HPLAYER" } },
                { "Enter name for new user parameter", new object[] { "-PARAMETERS", "N" } },
                { "Expression", new object[] { "CAL" } },
            };

        [Theory]
        [MemberData(nameof(TakeTheWholeLine))]
        public void TheTextOfAnAnnotation_ADescription_AndAValue_TakeTheWholeLine(string asked, object[] inputs)
        {
            Assert.True(PromptAfter(asked, inputs).TakesSpaces);
        }

        [Fact]
        public void ForString_EndsAtASpace_UntilItIsMadeToAllowSpaces()
        {
            Assert.False(CadPrompt.ForString("Name:").TakesSpaces);
            Assert.True(CadPrompt.ForString("Text:").AllowingSpaces().TakesSpaces);
        }

        [Fact]
        public void ANameTypedInAScript_EndsAtTheSpace_AndTheNextWordIsTheNextAnswer()
        {
            CadDocument document = RunScript("-LAYER N AA BB\n", out CadCommandSession session);

            Assert.True(document.Database.LayerTable.Has("AA"));
            Assert.False(document.Database.LayerTable.Has("AA BB"));
            Assert.Equal("Script stopped at line 1: -LAYER N AA BB", session.History.Last());
        }

        [Fact]
        public void TheOldAndNewNamesOfRename_AreTwoWordsOfTheLine()
        {
            CadDocument document = RunScript("-LAYER N AA\n\n-RENAME LA AA BB\n", out CadCommandSession session);

            Assert.False(document.Database.LayerTable.Has("AA"));
            Assert.True(document.Database.LayerTable.Has("BB"));
            Assert.DoesNotContain(session.History, h => h.StartsWith("Script stopped", StringComparison.Ordinal));
        }

        [Fact]
        public void ASetvarOfAName_EndsAtASpace_AndOfFreeText_TakesTheRestOfTheLine()
        {
            CadDocument document = RunScript(
                "-LAYER N AA\n\nSETVAR CLAYER AA SETVAR DIMPOST mm and more\n",
                out CadCommandSession session
            );

            Assert.Equal("AA", document.Database.Clayer);
            Assert.Equal("mm and more", session.Registry.FindVariable("DIMPOST").ValueForLisp(session));
            Assert.DoesNotContain(session.History, h => h.StartsWith("Script stopped", StringComparison.Ordinal));
        }

        [Fact]
        public void ALayerDescription_TakesTheRestOfTheLine_AndTheNamesThatFollowEndAtASpace()
        {
            CadDocument document = RunScript(
                "-LAYER N AA\n\n-LAYER D Two or three words\nAA\n\n",
                out CadCommandSession session
            );

            Assert.Equal("Two or three words", document.Database.LayerTable["AA"].Description);
            Assert.DoesNotContain(session.History, h => h.StartsWith("Script stopped", StringComparison.Ordinal));
        }

        [Fact]
        public void TheTextOfTEXT_StillTakesTheRestOfTheLine()
        {
            CadDocument document = RunScript("TEXT 0,0 2 0 Hello  World \n\n", out _);

            Assert.Equal("Hello  World ", Assert.Single(All<DxfDBText>(document.ModelSpace)).TextString);
        }
    }
}
