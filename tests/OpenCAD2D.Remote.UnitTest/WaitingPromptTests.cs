using System.Collections.Generic;
using System.Linq;
using GeometryHelper.Dxf;
using GeometryHelper.Geometry;
using Xunit;

namespace OpenCAD2D.Remote.UnitTest
{
    /// <summary>The waiting prompt (S30): shown while a command works for a program outside the app, it takes only Esc.</summary>
    public class WaitingPromptTests
    {
        private const string Message = "STAIRSPY is working... (Esc to stop)";

        private static CadCommandSession SessionWith(out CadDocument document)
        {
            var db = new DxfDatabase("AC1027");
            db.ModelSpace.AppendEntity(new DxfLine(new GeoPoint3(0, 0, 0), new GeoPoint3(100, 0, 0)));
            document = CadDocument.From(db, "A-101.dxf");
            return new CadCommandSession(document) { PickAperture = 1.0 };
        }

        /// <summary>A command that waits until it is told to go on, then asks a point.</summary>
        private static CadCommand Waiter(List<string> log) => new CadCommand("WAITER", c => Body(c, log));

        private static IEnumerable<CadPrompt> Body(CadCommandContext c, List<string> log)
        {
            log.Add("waiting");
            yield return CadPrompt.ForWaiting(Message);
            log.Add("went on");
            yield return CadPrompt.ForPoint("Specify the point");
            log.Add("point " + c.Input.Point.X);
        }

        [Fact]
        public void AWaitingPrompt_ShowsItsMessageAsItIs_WithNoColon()
        {
            CadPrompt prompt = CadPrompt.ForWaiting(Message);

            Assert.Equal(Message, prompt.Text);
            Assert.True(prompt.IsWaiting);
            Assert.False(CadPrompt.ForString("Name").IsWaiting);
        }

        [Fact]
        public void AWaitingPrompt_TakesNoTextPointSelectionOrEnter()
        {
            CadCommandSession session = SessionWith(out CadDocument document);
            var log = new List<string>();
            session.Start(Waiter(log));
            int lines = session.History.Count;

            Assert.False(session.Feed("12,5"));
            Assert.False(session.Feed(new GeoPoint2(5, 0.2)));
            Assert.False(session.FeedSelection(document.Database.ModelSpace.Select(e => e.Handle)));
            session.Enter();
            Assert.False(session.Feed("   "));

            Assert.Equal(new[] { "waiting" }, log);
            Assert.Equal(Message, session.PromptText);
            Assert.True(session.IsRunning);
            Assert.Equal(lines, session.History.Count);
        }

        [Fact]
        public void Esc_AtAWaitingPrompt_EndsTheCommand_WritingCancelAlone()
        {
            CadCommandSession session = SessionWith(out _);
            session.Start(Waiter(new List<string>()));

            session.Escape();

            Assert.False(session.IsRunning);
            Assert.Equal("*Cancel*", session.History.Last());
            Assert.Equal(CadCommandOutcome.Cancelled, session.LastOutcome);
        }

        [Fact]
        public void TheHost_EndsTheWaitFromCode_AndTheCommandGoesOn()
        {
            CadCommandSession session = SessionWith(out _);
            var log = new List<string>();
            CadCommand command = Waiter(log);
            session.Start(command);

            Assert.True(session.Resume(command));

            Assert.Equal(new[] { "waiting", "went on" }, log);
            Assert.Equal("Specify the point:", session.PromptText);
            Assert.True(session.Feed("40,0"));
            Assert.Equal(new[] { "waiting", "went on", "point 40" }, log);
            Assert.False(session.IsRunning);
        }

        [Fact]
        public void TheHost_CannotResumeACommandThatIsNotTheOneRunning()
        {
            CadCommandSession session = SessionWith(out _);
            CadCommand command = Waiter(new List<string>());

            Assert.False(session.Resume(command));

            session.Start(command);

            Assert.False(session.Resume(Waiter(new List<string>())));
        }
    }
}
