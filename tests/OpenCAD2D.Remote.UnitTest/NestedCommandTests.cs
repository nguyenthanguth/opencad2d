using System;
using System.Collections.Generic;
using System.Linq;
using GeometryHelper.Dxf;
using GeometryHelper.Geometry;
using Xunit;

namespace OpenCAD2D.Remote.UnitTest
{
    /// <summary>
    /// A command a host runs inside the command it holds open (<c>CadCommandSession.BeginNested</c>): it asks its prompts with no
    /// "&gt;&gt;", ends with no "Resuming" line, shares the host's undo step, and Esc at it ends the host's command too.
    /// </summary>
    public class NestedCommandTests
    {
        private const string Message = "HOLDER is working... (Esc to stop)";

        private static CadCommandSession SessionWith(out CadDocument document)
        {
            var db = new DxfDatabase("AC1027");
            db.ModelSpace.AppendEntity(new DxfLine(new GeoPoint3(0, 0, 0), new GeoPoint3(100, 0, 0)));
            document = CadDocument.From(db, "A-101.dxf");
            return new CadCommandSession(document) { PickAperture = 1.0 };
        }

        /// <summary>A command that holds an undo step open and waits, as the run of a tool does.</summary>
        private static CadCommand Holder(List<string> log) => new CadCommand("HOLDER", c => Hold(c, log));

        private static IEnumerable<CadPrompt> Hold(CadCommandContext c, List<string> log)
        {
            c.Document.BeginGroup("HOLDER");

            try
            {
                while (true)
                {
                    yield return CadPrompt.ForWaiting(Message);
                    log.Add("went on");
                }
            }
            finally
            {
                log.Add("left");

                if (c.Document.IsInGroup)
                {
                    c.Document.CancelGroup();
                }
            }
        }

        private static CadCommand Find(CadCommandSession session, string name) => session.Registry.Find(name);

        [Fact]
        public void ACommand_NestedInTheHost_AsksItsPromptsPlain_AndGivesTheHostItsPromptBackSilently()
        {
            CadCommandSession session = SessionWith(out CadDocument document);
            CadCommand holder = Holder(new List<string>());
            session.Begin(holder);
            int lines = session.History.Count;

            Assert.True(session.BeginNested(holder, Find(session, "LINE")));

            Assert.True(session.IsNested);
            Assert.True(session.IsTransparent);
            Assert.Equal("LINE", session.Current.Name);
            Assert.False(session.PromptText.StartsWith(">>", StringComparison.Ordinal));
            Assert.True(session.Feed("10,10"));
            Assert.True(session.Feed("60,10"));
            session.Enter();

            Assert.False(session.IsNested);
            Assert.False(session.IsTransparent);
            Assert.Equal("HOLDER", session.Current.Name);
            Assert.Equal(Message, session.PromptText);
            Assert.True(session.Prompt.IsWaiting);
            Assert.Equal(CadCommandOutcome.Completed, session.NestedOutcome);
            Assert.DoesNotContain(session.History.Skip(lines), l =>
                l.IndexOf("Resuming", StringComparison.Ordinal) >= 0
            );
            Assert.DoesNotContain(session.History.Skip(lines), l => l.StartsWith(">>", StringComparison.Ordinal));
            Assert.Equal(2, document.Database.ModelSpace.Count(e => !e.IsErased));
        }

        [Fact]
        public void TheNestedCommandsEdits_JoinTheHostsStep_WhichIsOneStepOrNone()
        {
            CadCommandSession session = SessionWith(out CadDocument document);
            CadCommand holder = Holder(new List<string>());
            session.Begin(holder);
            session.BeginNested(holder, Find(session, "LINE"));
            session.Feed("10,10");
            session.Feed("60,10");
            session.Enter();

            Assert.True(document.IsInGroup);
            Assert.False(document.CanUndo);

            session.Escape();

            Assert.Equal(1, document.Database.ModelSpace.Count(e => !e.IsErased));
            Assert.False(document.CanUndo);
        }

        [Fact]
        public void Esc_AtTheNestedCommand_EndsTheHostsCommandToo()
        {
            CadCommandSession session = SessionWith(out _);
            var log = new List<string>();
            CadCommand holder = Holder(log);
            session.Begin(holder);
            session.BeginNested(holder, Find(session, "LINE"));
            session.Feed("10,10");

            session.Escape();

            Assert.False(session.IsRunning);
            Assert.Equal(CadCommandOutcome.Cancelled, session.LastOutcome);
            Assert.Contains("left", log);
        }

        [Fact]
        public void TheHostEndingTheNestedCommand_LeavesItsOwnCommandRunning()
        {
            CadCommandSession session = SessionWith(out _);
            var log = new List<string>();
            CadCommand holder = Holder(log);
            session.Begin(holder);
            session.BeginNested(holder, Find(session, "LINE"));
            session.Feed("10,10");

            session.EndNested();

            Assert.True(session.IsRunning);
            Assert.False(session.IsNested);
            Assert.Equal("HOLDER", session.Current.Name);
            Assert.Equal(Message, session.PromptText);
            Assert.Equal(CadCommandOutcome.Cancelled, session.NestedOutcome);
            Assert.DoesNotContain("left", log);
        }

        [Fact]
        public void ACommand_IsNotNested_WhereTheHostsCommandIsNotTheOneRunning_OrOneIsNestedAlready()
        {
            CadCommandSession session = SessionWith(out _);
            CadCommand holder = Holder(new List<string>());

            Assert.False(session.BeginNested(holder, Find(session, "LINE")));

            session.Start("CIRCLE");
            Assert.False(session.BeginNested(holder, Find(session, "LINE")));
            session.Escape();

            session.Begin(holder);
            Assert.True(session.BeginNested(holder, Find(session, "LINE")));
            Assert.False(session.BeginNested(holder, Find(session, "CIRCLE")));
            Assert.Equal("LINE", session.Current.Name);
        }

        [Fact]
        public void ANestedCommandThatThrows_IsFailed_AndTheHostIsToldByTheOutcome()
        {
            CadCommandSession session = SessionWith(out _);
            CadCommand holder = Holder(new List<string>());
            session.Begin(holder);
            var failing = new CadCommand("FAILING", c => throw new InvalidOperationException("broken"));

            Assert.True(session.BeginNested(holder, failing));

            Assert.False(session.IsNested);
            Assert.Equal(CadCommandOutcome.Failed, session.NestedOutcome);
            Assert.Contains("*Error* broken", session.History);
        }

        [Fact]
        public void ATransparentCommand_StillWritesItsResumingLine_NestingChangedNothingOfIt()
        {
            CadCommandSession session = SessionWith(out _);
            session.Start("LINE");
            session.Feed("0,0");

            session.Feed("'ZOOM");
            session.Feed("E");

            Assert.Equal("LINE", session.Current.Name);
            Assert.Contains("Resuming LINE command.", session.History);
        }
    }
}
