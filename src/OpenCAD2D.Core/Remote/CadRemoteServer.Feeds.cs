using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Threading;
using GeometryHelper.Geometry;
using OpenCAD2D.Remote;

namespace OpenCAD2D
{
    /// <summary>
    /// <c>feed</c> of the channel of the tools: a command run inside the run, with the answers its prompts are given, as AutoLISP's
    /// <c>command</c> does it (the command is nested in the run's own, silently, so the run keeps its one undo step and its Esc).
    /// </summary>
    public sealed partial class CadRemoteServer
    {
        /// <summary>The one backslash that hands a prompt of a fed command to the user, as AutoLISP's <c>pause</c> does.</summary>
        private const string Pause = "\\";

        // The commands made by CreateRunCommand: a tool's command is not fed from a tool, whose process would wait for itself.
        private readonly HashSet<CadCommand> _runCommands = new HashSet<CadCommand>();

        /// <summary>Where a feed has got to: the answers given so far, and the prompt the user has been left at.</summary>
        private sealed class FeedState
        {
            public CadRemoteToolRun Run;
            public List<object> Answers;
            public string Shown;
            public int Next = 1;
            public CadPrompt Paused;
        }

        /// <summary>The answers of a <c>feed</c> request: texts, points as <c>{x, y}</c>, lists of handles.</summary>
        private static List<object> FeedAnswers(JsonObject request)
        {
            var answers = new List<object>();

            foreach (object item in request.GetArray("answers"))
            {
                switch (item)
                {
                    case string text:
                        answers.Add(text);
                        break;
                    case JsonObject point:
                        answers.Add(new GeoPoint2(point.GetDouble("x"), point.GetDouble("y")));
                        break;
                    case JsonArray handles:
                        answers.Add(handles.OfType<string>().ToList());
                        break;
                    default:
                        throw new Refusal("cmd takes text, numbers, points and lists of handles.");
                }
            }

            if (answers.Count == 0 || !(answers[0] is string name) || string.IsNullOrWhiteSpace(name))
            {
                throw new Refusal("cmd needs the name of a command first.");
            }

            return answers;
        }

        /// <summary>
        /// Runs a command inside the run and gives it its answers: each goes to the prompt the command waits at, one after the
        /// other. A <c>PAUSE</c> leaves that prompt to the user, and the request waits for them on its own thread, holding no lock
        /// of the server, until the command asks the next question or ends. The request ends when the command has.
        /// </summary>
        private JsonObject RunFeed(JsonObject request, string token)
        {
            List<object> answers = FeedAnswers(request);
            FeedState state = Dispatch(() => BeginFeed(request, token, answers));
            bool done = false;

            while (!done)
            {
                done = Dispatch(() => FeedSome(state));

                // The user is left at a prompt: the request waits for the command to ask another or end.
                while (!done && state.Paused != null && !Dispatch(() => Moved(state)))
                {
                    Thread.Sleep(CommandPoll);

                    if (_stopping)
                    {
                        throw new Refusal("OpenCAD2D is shutting down.");
                    }
                }
            }

            return new JsonObject();
        }

        /// <summary>Starts the command, nested in the run's own.</summary>
        private FeedState BeginFeed(JsonObject request, string token, List<object> answers)
        {
            CadRemoteToolRun run = LiveRun(request, token);

            if (run.Pending != null)
            {
                throw new Refusal(run.Name + " asked a question that the user has not answered: wait for it.");
            }

            CadCommandSession session = run.Session;
            string typed = ((string)answers[0]).Trim().TrimStart('_', '.');
            CadCommand command = session.Registry.Find(typed);

            if (command == null)
            {
                throw new Refusal(((string)answers[0]).Trim() + " is not a command.");
            }

            lock (_gate)
            {
                if (_runCommands.Contains(command))
                {
                    throw new Refusal(
                        command.Name + " is the command of a tool: a tool does not run a tool's command with cmd."
                    );
                }
            }

            session.Write("Command: " + typed);

            if (!session.BeginNested(run.Command, command))
            {
                throw new Refusal(
                    (session.Current?.Name ?? run.Name)
                        + " is still running: a command can only be run at its own prompt."
                );
            }

            return new FeedState
            {
                Run = run,
                Answers = answers,
                Shown = command.Name,
            };
        }

        /// <summary>Gives the answers from the next one on, until the end, or until one is left to the user.</summary>
        /// <returns>True when the command has ended after the last answer.</returns>
        private bool FeedSome(FeedState state)
        {
            CadRemoteToolRun run = state.Run;
            CadCommandSession session = run.Session;
            state.Paused = null;

            while (state.Next < state.Answers.Count)
            {
                Check(state);

                if (!session.IsNested)
                {
                    throw new Refusal(
                        state.Shown + " had ended before " + Describe(state.Answers[state.Next]) + " could be given."
                    );
                }

                object answer = state.Answers[state.Next];
                string prompt = session.PromptText;
                state.Next++;

                if (answer is string pause && pause == Pause)
                {
                    state.Paused = session.Prompt;
                    return false;
                }

                if (!FeedOne(session, answer))
                {
                    session.EndNested();
                    throw new Refusal(state.Shown + " did not take " + Describe(answer) + " at '" + prompt + "'.");
                }
            }

            Check(state);

            if (session.IsNested)
            {
                string prompt = session.PromptText;
                session.EndNested();
                throw new Refusal(state.Shown + " still asked '" + prompt + "' after the last answer.");
            }

            return true;
        }

        /// <summary>Refuses where the run has ended under the feed (Esc), or the command failed, which ends the run as the failure of any command does.</summary>
        private static void Check(FeedState state)
        {
            CadRemoteToolRun run = state.Run;

            if (run.IsEnded)
            {
                throw new Refusal("The user ended " + run.Name + " with Esc.", true);
            }

            if (!run.Session.IsNested && run.Session.NestedOutcome == CadCommandOutcome.Failed)
            {
                run.Cancel();
                throw new Refusal(state.Shown + " failed: " + run.Name + " was ended and its changes taken back.");
            }
        }

        /// <summary>Whether the prompt the user was left at has been answered: the command asks another, or ends, or the run ends.</summary>
        private static bool Moved(FeedState state) =>
            state.Run.IsEnded
            || !state.Run.Session.IsNested
            || !ReferenceEquals(state.Run.Session.Prompt, state.Paused);

        private static bool FeedOne(CadCommandSession session, object answer)
        {
            switch (answer)
            {
                case string text when text.Length == 0:
                    session.Enter();
                    return true;
                case string text:
                    return session.FeedTyped(text);
                case GeoPoint2 point:
                    return session.Feed(point);
                case List<string> handles:
                    if (!session.FeedSelection(handles))
                    {
                        return false;
                    }

                    session.Enter();
                    return true;
                default:
                    return false;
            }
        }

        private static string Describe(object answer)
        {
            switch (answer)
            {
                case string text:
                    return text.Length == 0 ? "Enter" : "'" + text + "'";
                case GeoPoint2 point:
                    return point.X.ToString("R", CultureInfo.InvariantCulture)
                        + ","
                        + point.Y.ToString("R", CultureInfo.InvariantCulture);
                case List<string> handles:
                    return "[" + string.Join(", ", handles) + "]";
                default:
                    return answer?.ToString() ?? "null";
            }
        }
    }
}
