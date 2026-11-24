using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using GeometryHelper.Dxf;
using GeometryHelper.Geometry;
using Xunit;

namespace OpenCAD2D.Remote.UnitTest
{
    /// <summary>
    /// A command run from inside a tool's run (<see cref="CadRemoteRun.RunCommand"/>, the <c>cmd()</c> of the Python API): it is
    /// nested in the run's command as AutoLISP's <c>command</c> nests one, silently, so that the run keeps its one undo step and
    /// its Esc.
    /// </summary>
    public class RemoteFeedTests
    {
        private const string Waiting = "STAIRSPY is working... (Esc to stop)";

        private sealed class Scene : IDisposable
        {
            public readonly Harness Harness = new Harness();
            public readonly TestHost Host = new TestHost();
            public readonly CadDocument Document;
            public readonly CadCommandSession Session;
            public readonly CadRemoteServer Tools;
            public readonly string Token;
            public readonly CadCommand Command;
            public CadRemoteToolRun Run;
            public CadApplication Client;
            public CadRemoteRun Joined;

            public Scene()
            {
                var db = new DxfDatabase("AC1027");
                db.ModelSpace.AppendEntity(new DxfLine(new GeoPoint3(0, 0, 0), new GeoPoint3(100, 0, 0)));
                Document = Host.Add(CadDocument.From(db, "A-101.dxf"));
                Session = Host.SessionOf(Document);
                Tools = Harness.ServeTools(Host);
                Token = Tools.IssueToken();
                Command = Tools.CreateRunCommand("STAIRSPY", false, Token, run => Run = run);
                Assert.True(OnApp(() => Session.Begin(Command)));
                Client = Harness.ConnectTool(Tools, Token);
                Joined = Client.JoinRun(Run.Id);
            }

            public void Dispose() => Harness.Dispose();

            public T OnApp<T>(Func<T> function) => Host.OnAppThread(function);

            public void OnApp(Action action) => Host.OnAppThread(action);

            public int Entities() => OnApp(() => Document.Database.ModelSpace.Count(e => !e.IsErased));

            public async Task WaitForPrompt(Func<CadPrompt, bool> wanted)
            {
                for (int i = 0; i < 1000; i++)
                {
                    if (OnApp(() => Session.Prompt != null && wanted(Session.Prompt)))
                    {
                        return;
                    }

                    await Task.Delay(10);
                }

                throw new TimeoutException("The prompt never came.");
            }
        }

        private static bool AsksForCentre(CadPrompt prompt) =>
            !prompt.IsWaiting && prompt.Text.IndexOf("center", StringComparison.OrdinalIgnoreCase) >= 0;

        [Fact]
        public void ACommand_RunInTheRun_DrawsInTheRunsOwnUndoStep_AndWritesNoResumingLine()
        {
            using (var scene = new Scene())
            {
                scene.Joined.RunCommand("LINE", "10,10", "60,10", "");

                Assert.Equal(2, scene.Entities());
                Assert.Equal(Waiting, scene.OnApp(() => scene.Session.PromptText));
                Assert.Equal("STAIRSPY", scene.OnApp(() => scene.Session.Current.Name));
                List<string> history = scene.OnApp(() => scene.Session.History.ToList());
                Assert.DoesNotContain(history, line => line.IndexOf("Resuming", StringComparison.Ordinal) >= 0);
                Assert.DoesNotContain(history, line => line.StartsWith(">>", StringComparison.Ordinal));
                Assert.False(scene.OnApp(() => scene.Session.IsNested));

                // The run goes on: a commit joins the same step, and the end of the run closes the one step.
                CadRemoteDocument drawing = scene.Client.Documents.First();
                drawing.Insert(new DxfLine(new GeoPoint3(0, 50, 0), new GeoPoint3(100, 50, 0)));
                drawing.CommitChanges();
                scene.Joined.End();

                Assert.Equal(3, scene.Entities());
                Assert.Equal("STAIRSPY", scene.OnApp(() => scene.Document.NextUndo.Name));
                scene.OnApp(() => scene.Document.Undo());
                Assert.Equal(1, scene.Entities());
                Assert.False(scene.OnApp(() => scene.Document.CanUndo));
            }
        }

        [Fact]
        public void TwoCommands_InOneRun_AreOneUndoStep()
        {
            using (var scene = new Scene())
            {
                scene.Joined.RunCommand("LINE", "10,10", "60,10", "");
                scene.Joined.RunCommand("CIRCLE", new GeoPoint2(30, 30), "5");
                scene.Joined.End();

                Assert.Equal(3, scene.Entities());
                Assert.Equal("STAIRSPY", scene.OnApp(() => scene.Document.NextUndo.Name));

                scene.OnApp(() => scene.Document.Undo());

                Assert.Equal(1, scene.Entities());
                Assert.False(scene.OnApp(() => scene.Document.CanUndo));
            }
        }

        [Fact]
        public void Esc_AfterACommandOfTheRun_TakesBackWhatItDrew()
        {
            using (var scene = new Scene())
            {
                scene.Joined.RunCommand("LINE", "10,10", "60,10", "");
                Assert.Equal(2, scene.Entities());

                scene.OnApp(() => scene.Session.Escape());

                Assert.Equal(1, scene.Entities());
                Assert.Equal(CadRemoteToolRunOutcome.Cancelled, scene.Run.Outcome);
                Assert.False(scene.OnApp(() => scene.Document.CanUndo));
                Assert.False(scene.OnApp(() => scene.Document.IsInGroup));
            }
        }

        [Fact]
        public async Task APause_LeavesAPromptToTheUser_AndEscAtItEndsTheRunWithTheDrawingAsItWas()
        {
            using (var scene = new Scene())
            {
                scene.Joined.RunCommand("LINE", "10,10", "60,10", "");
                Task pending = Task.Run(() => scene.Joined.RunCommand("CIRCLE", CadRemoteRun.Pause, "5"));
                await scene.WaitForPrompt(AsksForCentre);

                scene.OnApp(() => scene.Session.Escape());
                CadRemoteException cancelled = await Assert.ThrowsAsync<CadRemoteException>(() => pending);

                Assert.True(cancelled.IsCancelled);
                Assert.Equal(1, scene.Entities());
                Assert.Equal(CadRemoteToolRunOutcome.Cancelled, scene.Run.Outcome);
                Assert.False(scene.OnApp(() => scene.Session.IsRunning));
                Assert.False(scene.OnApp(() => scene.Document.CanUndo));
            }
        }

        [Fact]
        public async Task APause_IsAnsweredByTheUser_AndTheCommandGoesOnWithTheRestOfItsAnswers()
        {
            using (var scene = new Scene())
            {
                Task pending = Task.Run(() => scene.Joined.RunCommand("CIRCLE", CadRemoteRun.Pause, "5"));
                await scene.WaitForPrompt(AsksForCentre);
                Assert.True(scene.OnApp(() => scene.Session.IsNested));

                scene.OnApp(() => scene.Session.Feed("20,20"));
                await pending;

                DxfCircle circle = scene.OnApp(() => scene.Document.Database.ModelSpace.OfType<DxfCircle>().Single());
                Assert.Equal(20.0, circle.Center.X, 9);
                Assert.Equal(5.0, circle.Radius, 9);
                Assert.Equal(Waiting, scene.OnApp(() => scene.Session.PromptText));
                Assert.False(scene.Run.IsEnded);
            }
        }

        [Fact]
        public void ACommandStillAskingAfterTheLastAnswer_IsEnded_AndTheRunGoesOn()
        {
            using (var scene = new Scene())
            {
                CadRemoteException refused = Assert.Throws<CadRemoteException>(() =>
                    scene.Joined.RunCommand("LINE", "10,10", "60,10")
                );

                Assert.False(refused.IsCancelled);
                Assert.Contains("still asked", refused.Message, StringComparison.Ordinal);
                Assert.False(scene.Run.IsEnded);
                Assert.False(scene.OnApp(() => scene.Session.IsNested));
                Assert.Equal(Waiting, scene.OnApp(() => scene.Session.PromptText));

                // The line it drew before it was ended stays in the run's step, which the run goes on to end.
                scene.Joined.End();
                Assert.Equal(2, scene.Entities());
            }
        }

        [Fact]
        public void AnAnswerTheCommandDoesNotTake_EndsItAndTellsWhichAndWhere()
        {
            using (var scene = new Scene())
            {
                CadRemoteException refused = Assert.Throws<CadRemoteException>(() =>
                    scene.Joined.RunCommand("CIRCLE", "10,10", "banana")
                );

                Assert.Contains("did not take 'banana'", refused.Message, StringComparison.Ordinal);
                Assert.False(scene.Run.IsEnded);
                Assert.False(scene.OnApp(() => scene.Session.IsNested));
            }
        }

        [Fact]
        public void ANameThatIsNoCommand_ATool_OrNone_IsRefused_AndTheRunGoesOn()
        {
            using (var scene = new Scene())
            {
                Assert.Contains(
                    "is not a command",
                    Assert.Throws<CadRemoteException>(() => scene.Joined.RunCommand("NOSUCHTHING")).Message,
                    StringComparison.Ordinal
                );
                Assert.Contains(
                    "needs the name",
                    Assert.Throws<CadRemoteException>(() => scene.Joined.RunCommand("  ")).Message,
                    StringComparison.Ordinal
                );

                scene.OnApp(() => scene.Session.Registry.Add(scene.Command));
                Assert.Contains(
                    "command of a tool",
                    Assert.Throws<CadRemoteException>(() => scene.Joined.RunCommand("STAIRSPY")).Message,
                    StringComparison.Ordinal
                );
                Assert.False(scene.Run.IsEnded);
                Assert.Equal(Waiting, scene.OnApp(() => scene.Session.PromptText));
            }
        }

        [Fact]
        public void ACommandOfARunThatEnded_IsRefused()
        {
            using (var scene = new Scene())
            {
                scene.Joined.End();

                Assert.Throws<CadRemoteException>(() => scene.Joined.RunCommand("LINE", "1,1", "2,2", ""));
            }
        }

        [Fact]
        public void AListOfHandles_AnswersSelectObjects_AndIsEndedWithEnter()
        {
            using (var scene = new Scene())
            {
                string handle = scene.OnApp(() => scene.Document.Database.ModelSpace.First().Handle);

                scene.Joined.RunCommand("ERASE", new List<string> { handle });

                Assert.Equal(0, scene.Entities());
                Assert.Equal(Waiting, scene.OnApp(() => scene.Session.PromptText));
                scene.Joined.End();
                scene.OnApp(() => scene.Document.Undo());
                Assert.Equal(1, scene.Entities());
            }
        }
    }
}
