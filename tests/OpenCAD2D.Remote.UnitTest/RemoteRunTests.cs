using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Threading.Tasks;
using GeometryHelper.Dxf;
using GeometryHelper.Geometry;
using Xunit;

namespace OpenCAD2D.Remote.UnitTest
{
    /// <summary>
    /// The run of a tool's command (S30, S31): a command of the drawing that waits at "STAIRSPY is working... (Esc to stop)"
    /// while a program, on the private channel of the tools, asks its questions on the command line, commits into the command's
    /// one undo step and ends it, and that Esc ends with every change taken back.
    /// </summary>
    public class RemoteRunTests
    {
        private static DxfLine Line(double x1, double y1, double x2, double y2) =>
            new DxfLine(new GeoPoint3(x1, y1, 0), new GeoPoint3(x2, y2, 0));

        /// <summary>A drawing with two lines, from (0, 0) to (100, 0) and from (0, 50) to (100, 50).</summary>
        private static CadDocument TwoLines(string name)
        {
            var db = new DxfDatabase("AC1027");
            db.ModelSpace.AppendEntity(Line(0, 0, 100, 0));
            db.ModelSpace.AppendEntity(Line(0, 50, 100, 50));
            return CadDocument.From(db, name);
        }

        private const string Waiting = "STAIRSPY is working... (Esc to stop)";

        private sealed class Scene : IDisposable
        {
            public Harness Harness = new Harness();
            public TestHost Host = new TestHost();
            public CadDocument Document;
            public CadCommandSession Session;
            public CadRemoteServer Tools;
            public string Token;
            public CadCommand Command;
            public CadRemoteToolRun Run;
            public CadApplication Client;
            public CadRemoteRun Joined;
            public CadRemoteDocument Drawing;

            public Scene(bool usesSelection = false)
            {
                Document = Host.Add(TwoLines("A-101.dxf"));
                Session = Host.SessionOf(Document);
                Tools = Harness.ServeTools(Host);
                Token = Tools.IssueToken();
                Command = Tools.CreateRunCommand("STAIRSPY", usesSelection, Token, run => Run = run);
            }

            public void Dispose() => Harness.Dispose();

            /// <summary>Types the command in the drawing, as the user does, and joins its run from a tool's connection.</summary>
            public void Begin()
            {
                Assert.True(OnApp(() => Session.Begin(Command)));
                Client = Harness.ConnectTool(Tools, Token);
                Joined = Client.JoinRun(Run.Id);
                Drawing = Client.Documents.First(d => d.Name == Document.Title);
            }

            public T OnApp<T>(Func<T> function) => Host.OnAppThread(function);

            public void OnApp(Action action) => Host.OnAppThread(action);

            public int Lines() => OnApp(() => Document.Database.ModelSpace.Count(e => !e.IsErased));

            /// <summary>Waits until the run asks the user: the prompt on the command line is not the waiting one.</summary>
            public async Task WaitForQuestion()
            {
                for (int i = 0; i < 1000; i++)
                {
                    if (OnApp(() => Session.Prompt != null && !Session.Prompt.IsWaiting))
                    {
                        return;
                    }

                    await Task.Delay(10);
                }

                throw new TimeoutException("The run never asked.");
            }

            /// <summary>Asks as a tool does, waits for the question on the command line, answers it by typing, and gives the answer.</summary>
            public async Task<CadRemoteAnswer> Asked(
                CadAskKind kind,
                string message,
                string typed,
                GeoPoint3? basePoint = null,
                IReadOnlyList<string> keywords = null,
                string defaultValue = null
            )
            {
                Task<CadRemoteAnswer> pending = Task.Run(() =>
                    Joined.Ask(kind, message, basePoint, keywords, defaultValue)
                );
                await WaitForQuestion();
                OnApp(() => Session.Feed(typed));
                return await pending;
            }
        }

        [Fact]
        public void ARunCommand_WaitsAtItsPrompt_WhichTakesNoAnswerButEsc()
        {
            using (var scene = new Scene())
            {
                scene.Begin();

                Assert.True(scene.OnApp(() => scene.Session.IsRunning));
                Assert.Equal(Waiting, scene.OnApp(() => scene.Session.PromptText));
                Assert.True(scene.OnApp(() => scene.Session.Prompt.IsWaiting));
                Assert.True(scene.OnApp(() => scene.Document.IsInGroup));
                Assert.Equal("STAIRSPY", scene.Run.Name);
                Assert.False(scene.Run.IsEnded);

                // The drawing is busy for everything else while the tool works.
                scene.OnApp(() => scene.Session.Feed("LINE"));
                scene.OnApp(() => scene.Session.Enter());
                Assert.Equal("STAIRSPY", scene.OnApp(() => scene.Session.Current.Name));
                Assert.Equal(Waiting, scene.OnApp(() => scene.Session.PromptText));
            }
        }

        [Fact]
        public async Task AnAsk_IsAnsweredByTyping_AndTheRunGoesBackToWaiting()
        {
            using (var scene = new Scene())
            {
                scene.Begin();
                Task<CadRemoteAnswer> pending = Task.Run(() =>
                    scene.Joined.Ask(CadAskKind.Point, "Specify the first step")
                );
                await scene.WaitForQuestion();

                Assert.Equal("Specify the first step:", scene.OnApp(() => scene.Session.PromptText));
                Assert.Equal(CadPromptKind.Point, scene.OnApp(() => scene.Session.Prompt.Kind));
                Assert.True(scene.OnApp(() => scene.Session.Feed("12,5")));
                CadRemoteAnswer answer = await pending;

                Assert.Equal(CadAnswerKind.Point, answer.Kind);
                Assert.Equal(12.0, answer.Point.X, 9);
                Assert.Equal(5.0, answer.Point.Y, 9);
                Assert.Equal(0.0, answer.Point.Z, 9);
                Assert.Equal(Waiting, scene.OnApp(() => scene.Session.PromptText));
                Assert.True(scene.OnApp(() => scene.Session.IsRunning));
            }
        }

        [Fact]
        public async Task AnAsk_OfEveryKind_IsAnsweredAsTheCommandLineAnswersIt()
        {
            using (var scene = new Scene())
            {
                scene.Begin();

                CadRemoteAnswer distance = await scene.Asked(
                    CadAskKind.Distance,
                    "Specify the rise",
                    "50",
                    new GeoPoint3(10, 10, 0)
                );
                CadRemoteAnswer angle = await scene.Asked(CadAskKind.Angle, "Specify the slope", "90");
                CadRemoteAnswer whole = await scene.Asked(CadAskKind.Integer, "How many steps", "7");
                CadRemoteAnswer real = await scene.Asked(CadAskKind.Real, "Scale factor", "2.5");
                CadRemoteAnswer text = await scene.Asked(CadAskKind.String, "Name of the stairs", "Main stair");
                CadRemoteAnswer keyword = await scene.Asked(
                    CadAskKind.Keyword,
                    "Mode",
                    "n",
                    keywords: new[] { "Yes", "No" },
                    defaultValue: "Yes"
                );

                Assert.Equal(CadAnswerKind.Number, distance.Kind);
                Assert.Equal(50.0, distance.Number, 9);
                Assert.Equal(CadAnswerKind.Number, angle.Kind);
                Assert.Equal(Math.PI / 2, angle.Number, 9);
                Assert.Equal(7.0, whole.Number, 9);
                Assert.Equal(2.5, real.Number, 9);
                Assert.Equal(CadAnswerKind.Text, text.Kind);
                Assert.Equal("Main stair", text.Text);
                Assert.Equal(CadAnswerKind.Keyword, keyword.Kind);
                Assert.Equal("No", keyword.Text);
            }
        }

        [Fact]
        public async Task ARealAsk_AsksAgain_AfterTextThatIsNoNumber()
        {
            using (var scene = new Scene())
            {
                scene.Begin();
                Task<CadRemoteAnswer> pending = Task.Run(() => scene.Joined.Ask(CadAskKind.Real, "Scale factor"));
                await scene.WaitForQuestion();

                scene.OnApp(() => scene.Session.Feed("wide"));
                await Task.Delay(50);

                Assert.False(pending.IsCompleted);
                Assert.Contains("Requires numeric value.", scene.OnApp(() => scene.Session.History.ToList()));
                await scene.WaitForQuestion();
                scene.OnApp(() => scene.Session.Feed("3"));

                Assert.Equal(3.0, (await pending).Number, 9);
            }
        }

        [Fact]
        public async Task AnEnterAtAnAsk_GivesTheDefault_OrNothing()
        {
            using (var scene = new Scene())
            {
                scene.Begin();
                Task<CadRemoteAnswer> keyword = Task.Run(() =>
                    scene.Joined.Ask(CadAskKind.Keyword, "Mode", null, new[] { "Yes", "No" }, "No")
                );
                await scene.WaitForQuestion();
                scene.OnApp(() => scene.Session.Enter());
                CadRemoteAnswer defaulted = await keyword;

                Task<CadRemoteAnswer> point = Task.Run(() => scene.Joined.Ask(CadAskKind.Point, "Specify the corner"));
                await scene.WaitForQuestion();
                scene.OnApp(() => scene.Session.Enter());
                CadRemoteAnswer nothing = await point;

                Assert.Equal(CadAnswerKind.Keyword, defaulted.Kind);
                Assert.Equal("No", defaulted.Text);
                Assert.Equal(CadAnswerKind.None, nothing.Kind);
            }
        }

        [Fact]
        public async Task AnEntityAsk_IsAnsweredByAPick_AndComesBackWithTheObject()
        {
            using (var scene = new Scene())
            {
                scene.Begin();
                DxfEntity second = scene.Document.Database.ModelSpace.Skip(1).First();
                Task<CadRemoteAnswer> pending = Task.Run(() => scene.Joined.Ask(CadAskKind.Entity, "Select the plate"));
                await scene.WaitForQuestion();

                Assert.Equal("Select the plate:", scene.OnApp(() => scene.Session.PromptText));
                scene.OnApp(() => scene.Session.Feed(new GeoPoint2(50, 50.2)));
                CadRemoteAnswer answer = await pending;

                Assert.Equal(CadAnswerKind.Selection, answer.Kind);
                Assert.Equal(new[] { second.Handle }, answer.Handles);
                DxfLine line = Assert.IsType<DxfLine>(Assert.Single(answer.Entities));
                Assert.Equal(50.0, line.StartPoint.Y, 9);
                Assert.Equal(Waiting, scene.OnApp(() => scene.Session.PromptText));
            }
        }

        [Fact]
        public async Task ASelectionAsk_GathersObjectsUntilEnter_InTheOrderOfTheirHandles()
        {
            using (var scene = new Scene())
            {
                scene.Begin();
                List<string> handles = scene.Document.Database.ModelSpace.Select(e => e.Handle).ToList();
                Task<CadRemoteAnswer> pending = Task.Run(() =>
                    scene.Joined.Ask(CadAskKind.Selection, "Select the bars")
                );
                await scene.WaitForQuestion();

                scene.OnApp(() => scene.Session.Feed(new GeoPoint2(50, 50.2)));
                scene.OnApp(() => scene.Session.Feed(new GeoPoint2(50, 0.2)));
                scene.OnApp(() => scene.Session.Enter());
                CadRemoteAnswer answer = await pending;

                Assert.Equal(handles, answer.Handles);
                Assert.Equal(2, answer.Entities.Count);
            }
        }

        [Fact]
        public async Task ASelectionAsk_TakesTheObjectsSelectedBefore_WhereTheCommandUsesTheSelection()
        {
            using (var scene = new Scene(usesSelection: true))
            {
                string first = scene.Document.Database.ModelSpace.First().Handle;
                scene.OnApp(() => scene.Document.Selection.Set(new[] { first }));
                scene.Begin();

                CadRemoteAnswer answer = await Task.Run(() =>
                    scene.Joined.Ask(CadAskKind.Selection, "Select the bars")
                );

                Assert.Equal(new[] { first }, answer.Handles);
                Assert.Equal(Waiting, scene.OnApp(() => scene.Session.PromptText));
            }
        }

        [Fact]
        public async Task AnAsk_DoesNotBlockASecondClient()
        {
            using (var scene = new Scene())
            {
                scene.Begin();
                Task<CadRemoteAnswer> pending = Task.Run(() =>
                    scene.Joined.Ask(CadAskKind.Point, "Specify the first step")
                );
                await scene.WaitForQuestion();

                // The question waits for the user. Another tool, with a token of its own, is served meanwhile.
                CadApplication other = scene.Harness.ConnectTool(scene.Tools, scene.Tools.IssueToken());
                var timer = Stopwatch.StartNew();
                int seen = other.Documents.Single().GetObjects().Count;
                timer.Stop();

                Assert.Equal(2, seen);
                Assert.True(
                    timer.ElapsedMilliseconds < 3000,
                    "A second client waited " + timer.ElapsedMilliseconds + " ms."
                );
                Assert.False(pending.IsCompleted);

                scene.OnApp(() => scene.Session.Feed("1,2"));
                Assert.Equal(1.0, (await pending).Point.X, 9);
            }
        }

        [Fact]
        public async Task AnAsk_DoesNotBlockTheAppsOwnChannel_NorWaitForIt()
        {
            using (var scene = new Scene())
            {
                scene.Harness.Serve(scene.Host);
                CadApplication app = scene.Harness.Connect();
                scene.Begin();
                Task<CadRemoteAnswer> pending = Task.Run(() =>
                    scene.Joined.Ask(CadAskKind.Point, "Specify the first step")
                );
                await scene.WaitForQuestion();

                // A client of the app's own pipe reads the drawing while the tool's question is open.
                Assert.Equal(2, app.Documents.Single().GetObjects().Count);

                scene.OnApp(() => scene.Session.Feed("1,2"));
                await pending;
            }
        }

        [Fact]
        public void ACommit_InARun_IsTakenBack_ByEscAtTheWaitingPrompt()
        {
            using (var scene = new Scene())
            {
                scene.Begin();
                scene.Drawing.Insert(Line(0, 100, 100, 100));
                IReadOnlyList<string> made = scene.Drawing.CommitChanges();

                Assert.Single(made);
                Assert.Equal(3, scene.Lines());
                Assert.True(scene.OnApp(() => scene.Document.IsInGroup));
                Assert.False(scene.OnApp(() => scene.Document.CanUndo));

                scene.OnApp(() => scene.Session.Escape());

                Assert.Equal(2, scene.Lines());
                Assert.False(scene.OnApp(() => scene.Document.IsInGroup));
                Assert.False(scene.OnApp(() => scene.Document.CanUndo));
                Assert.False(scene.OnApp(() => scene.Session.IsRunning));
                Assert.True(scene.Run.IsEnded);
                Assert.Equal(CadRemoteToolRunOutcome.Cancelled, scene.Run.Outcome);
                Assert.Equal("*Cancel*", scene.OnApp(() => scene.Session.History.Last()));

                // Every later request of the run is refused as cancelled.
                CadRemoteException refused = Assert.Throws<CadRemoteException>(() => scene.Drawing.GetObjects());
                Assert.True(refused.IsCancelled);
                Assert.Contains("STAIRSPY", refused.Message, StringComparison.Ordinal);
                Assert.True(Assert.Throws<CadRemoteException>(() => scene.Joined.Write("late")).IsCancelled);
            }
        }

        [Fact]
        public async Task Esc_AtAnOpenQuestion_AnswersTheAskCancelled_AndTakesBackTheCommit()
        {
            using (var scene = new Scene())
            {
                scene.Begin();
                scene.Drawing.Insert(Line(0, 100, 100, 100));
                scene.Drawing.CommitChanges();
                Task<CadRemoteAnswer> pending = Task.Run(() =>
                    scene.Joined.Ask(CadAskKind.Point, "Specify the first step")
                );
                await scene.WaitForQuestion();

                scene.OnApp(() => scene.Session.Escape());
                CadRemoteException cancelled = await Assert.ThrowsAsync<CadRemoteException>(() => pending);

                Assert.True(cancelled.IsCancelled);
                Assert.Equal(2, scene.Lines());
                Assert.Equal(CadRemoteToolRunOutcome.Cancelled, scene.Run.Outcome);
                Assert.False(scene.OnApp(() => scene.Document.IsInGroup));
            }
        }

        [Fact]
        public void TheEndOfARun_KeepsItsCommits_AsOneUndoStepNamedAfterTheCommand()
        {
            using (var scene = new Scene())
            {
                scene.Begin();
                scene.Drawing.Insert(Line(0, 100, 100, 100));
                scene.Drawing.CommitChanges();
                scene.Drawing.Insert(Line(0, 150, 100, 150));
                scene.Drawing.CommitChanges();
                Assert.False(scene.OnApp(() => scene.Document.CanUndo));

                scene.Joined.End();

                Assert.Equal(4, scene.Lines());
                Assert.False(scene.OnApp(() => scene.Session.IsRunning));
                Assert.False(scene.OnApp(() => scene.Document.IsInGroup));
                Assert.Equal(CadRemoteToolRunOutcome.Completed, scene.Run.Outcome);
                Assert.Equal("STAIRSPY", scene.OnApp(() => scene.Document.NextUndo.Name));

                scene.OnApp(() => scene.Document.Undo());

                Assert.Equal(2, scene.Lines());
                Assert.False(scene.OnApp(() => scene.Document.CanUndo));
            }
        }

        [Fact]
        public void ARequestOfAnEndedRun_IsRefused()
        {
            using (var scene = new Scene())
            {
                scene.Begin();
                scene.Joined.End();

                // A tool that keeps using the number of the run it ended is told so, in words, whatever the request.
                CadRemoteRun again = scene.Client.JoinRun(scene.Run.Id);
                CadRemoteException read = Assert.Throws<CadRemoteException>(() => scene.Drawing.GetObjects());
                CadRemoteException ask = Assert.Throws<CadRemoteException>(() => again.Ask(CadAskKind.Point, "Again"));
                CadRemoteException end = Assert.Throws<CadRemoteException>(() => again.End());

                foreach (CadRemoteException refused in new[] { read, ask, end })
                {
                    Assert.False(refused.IsCancelled);
                    Assert.Contains(
                        "The run " + scene.Run.Id + " of STAIRSPY has ended.",
                        refused.Message,
                        StringComparison.Ordinal
                    );
                }
            }
        }

        [Fact]
        public void AFailedRun_TakesBackItsCommit_AndWritesTheLineTheToolGave()
        {
            using (var scene = new Scene())
            {
                scene.Begin();
                scene.Drawing.Insert(Line(0, 100, 100, 100));
                scene.Drawing.CommitChanges();

                scene.Joined.Fail("STAIRSPY: ValueError: the rise is not a number");

                Assert.Equal(2, scene.Lines());
                Assert.False(scene.OnApp(() => scene.Session.IsRunning));
                Assert.False(scene.OnApp(() => scene.Document.CanUndo));
                Assert.Equal(CadRemoteToolRunOutcome.Failed, scene.Run.Outcome);
                Assert.Equal("STAIRSPY: ValueError: the rise is not a number", scene.Run.Error);
                Assert.Contains(
                    "STAIRSPY: ValueError: the rise is not a number",
                    scene.OnApp(() => scene.Session.History.ToList())
                );
            }
        }

        [Fact]
        public void TheAppCancellingARun_EndsItAsEscDoes_AndRaisesEnded()
        {
            using (var scene = new Scene())
            {
                scene.Begin();
                scene.Drawing.Insert(Line(0, 100, 100, 100));
                scene.Drawing.CommitChanges();
                int raised = 0;
                CadRemoteToolRunOutcome? seen = null;
                scene.Run.Ended += (s, e) =>
                {
                    raised++;
                    seen = scene.Run.Outcome;
                };

                scene.OnApp(() => scene.Run.Cancel());

                Assert.Equal(1, raised);
                Assert.Equal(CadRemoteToolRunOutcome.Cancelled, seen);
                Assert.Equal(2, scene.Lines());
                Assert.False(scene.OnApp(() => scene.Session.IsRunning));
            }
        }

        [Fact]
        public void AWriteInARun_GoesToTheCommandLine()
        {
            using (var scene = new Scene())
            {
                scene.Begin();

                scene.Joined.Write("Stairs: 18 steps");

                Assert.Contains("Stairs: 18 steps", scene.OnApp(() => scene.Session.History.ToList()));
            }
        }

        [Fact]
        public async Task ASecondAsk_WhileOneIsOpen_IsRefused()
        {
            using (var scene = new Scene())
            {
                scene.Begin();
                Task<CadRemoteAnswer> first = Task.Run(() => scene.Joined.Ask(CadAskKind.Point, "First"));
                await scene.WaitForQuestion();
                CadApplication second = scene.Harness.ConnectTool(scene.Tools, scene.Token);
                CadRemoteRun joined = second.JoinRun(scene.Run.Id);

                CadRemoteException refused = Assert.Throws<CadRemoteException>(() =>
                    joined.Ask(CadAskKind.Point, "Second")
                );

                Assert.Contains("already asked", refused.Message, StringComparison.Ordinal);
                scene.OnApp(() => scene.Session.Feed("1,1"));
                await first;
            }
        }

        [Fact]
        public void ARunOfAnotherToolsToken_IsRefused()
        {
            using (var scene = new Scene())
            {
                scene.Begin();
                CadApplication stranger = scene.Harness.ConnectTool(scene.Tools, scene.Tools.IssueToken());
                CadRemoteRun joined = stranger.JoinRun(scene.Run.Id);

                CadRemoteException refused = Assert.Throws<CadRemoteException>(() => joined.Write("mine now"));

                Assert.Contains("belongs to another tool", refused.Message, StringComparison.Ordinal);
                Assert.DoesNotContain("mine now", scene.OnApp(() => scene.Session.History.ToList()));
            }
        }

        [Fact]
        public void ARunThatDoesNotExist_IsRefused()
        {
            using (var scene = new Scene())
            {
                CadApplication client = scene.Harness.ConnectTool(scene.Tools, scene.Token);
                CadRemoteRun joined = client.JoinRun(99);

                CadRemoteException refused = Assert.Throws<CadRemoteException>(() => joined.Write("nothing"));

                Assert.Contains("no run 99", refused.Message, StringComparison.Ordinal);
            }
        }

        [Fact]
        public void ACommit_InARun_IsRefusedForAnotherDrawing_WhoseUndoStepIsNotTheRuns()
        {
            using (var scene = new Scene())
            {
                CadDocument other = scene.Host.Add(TwoLines("B-202.dxf"));
                scene.Host.Activate(scene.Document);
                scene.Begin();
                CadRemoteDocument b = scene.Client.Documents.Single(d => d.Name == "B-202.dxf");
                b.Insert(Line(0, 100, 100, 100));

                CadRemoteException refused = Assert.Throws<CadRemoteException>(() => b.CommitChanges());

                Assert.Contains("A-101.dxf", refused.Message, StringComparison.Ordinal);
                Assert.Equal(2, scene.OnApp(() => other.Database.ModelSpace.Count(e => !e.IsErased)));
            }
        }

        [Fact]
        public void APickOrACommand_InARun_IsRefused_ForTheRunAsksWithAsk()
        {
            using (var scene = new Scene())
            {
                scene.Begin();

                CadRemoteException pick = Assert.Throws<CadRemoteException>(() => scene.Drawing.PickPoint("Pick"));
                CadRemoteException command = Assert.Throws<CadRemoteException>(() => scene.Drawing.SendCommand("LINE"));

                Assert.Contains("cannot be made inside a run", pick.Message, StringComparison.Ordinal);
                Assert.Contains("cannot be made inside a run", command.Message, StringComparison.Ordinal);
            }
        }

        [Fact]
        public void ARunRequest_OnTheAppsOwnChannel_IsRefused_ForOnlyTheToolsChannelHasRuns()
        {
            using (var scene = new Scene())
            {
                scene.Begin();
                CadRemoteServer app = scene.Harness.Serve(scene.Host);
                CadApplication client = scene.Harness.Connect();
                CadRemoteRun joined = client.JoinRun(scene.Run.Id);

                CadRemoteException refused = Assert.Throws<CadRemoteException>(() => joined.Write("hello"));

                Assert.Contains("no run", refused.Message, StringComparison.Ordinal);
                Assert.NotNull(app);
            }
        }

        [Fact]
        public void ARunCommand_StartedWhileACommandRuns_DoesNotBegin()
        {
            using (var scene = new Scene())
            {
                scene.OnApp(() => scene.Session.Start("LINE"));

                Assert.False(scene.OnApp(() => scene.Session.Start(scene.Command)));
                Assert.Null(scene.Run);
                Assert.Equal("LINE", scene.OnApp(() => scene.Session.Current.Name));
            }
        }

        [Fact]
        public void ARunCommand_WhoseStartCallbackThrows_IsFailed_WithItsLineAndNoStepLeftOpen()
        {
            using (var scene = new Scene())
            {
                CadRemoteToolRun seen = null;
                CadCommand command = scene.Tools.CreateRunCommand("STAIRSPY", false, scene.Token, run =>
                {
                    seen = run;
                    throw new InvalidOperationException("The tool could not be started.");
                });

                scene.OnApp(() => scene.Session.Begin(command));

                Assert.Equal(CadRemoteToolRunOutcome.Failed, seen.Outcome);
                Assert.Equal("The tool could not be started.", seen.Error);
                Assert.False(scene.OnApp(() => scene.Session.IsRunning));
                Assert.False(scene.OnApp(() => scene.Document.IsInGroup));
                Assert.Contains(
                    "*Error* The tool could not be started.",
                    scene.OnApp(() => scene.Session.History.ToList())
                );
            }
        }

        [Fact]
        public void TheStartCallback_OfARunCommand_SeesTheRunAndTheDrawingsNumber()
        {
            using (var scene = new Scene())
            {
                scene.Begin();

                Assert.Equal(scene.Document, scene.Run.Document);
                Assert.Equal(scene.Drawing.Id, scene.Run.DocumentId);
                Assert.True(scene.Run.Id > 0);
            }
        }
    }
}
