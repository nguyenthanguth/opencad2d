using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using GeometryHelper;
using GeometryHelper.Dxf;
using GeometryHelper.Geometry;
using OpenCAD2D.Remote;

namespace OpenCAD2D
{
    /// <summary>
    /// The channel of the tools: a second server of the app that a program started by the app (a Python tool) drives, with the
    /// requests of a run on top of the ordinary ones. A run is the command the user typed, held open by the app while the tool
    /// works: its questions are asked on the drawing's command line, its commits are in the command's one undo step, and Esc
    /// ends it with every change taken back.
    /// </summary>
    public sealed partial class CadRemoteServer
    {
        /// <summary>How many ended runs the server remembers, to tell a tool that its run has ended instead of that it never was.</summary>
        private const int RunsKept = 100;

        /// <summary>The bytes of a token: 32, written as 64 hexadecimal letters.</summary>
        private const int TokenBytes = 32;

        private readonly bool _forTools;
        private readonly List<string> _tokens = new List<string>();

        // The runs, touched only on the dispatcher's thread.
        private readonly Dictionary<int, CadRemoteToolRun> _runs = new Dictionary<int, CadRemoteToolRun>();
        private int _lastRun;

        /// <summary>
        /// Makes the server of the tools of an app: the pipe <c>OpenCAD2D.&lt;process id&gt;.tools</c>, never announced in the
        /// instance folder, open whether or not the app allows other programs to connect, and taking a client only with a token
        /// <see cref="IssueToken"/> gave. Call <see cref="Start"/> on the thread of the drawings, as for the app's own server.
        /// </summary>
        /// <param name="host">The app's drawings and their commands.</param>
        /// <param name="dispatcher">Runs work on the thread the drawings belong to.</param>
        /// <returns>The server, not started.</returns>
        /// <exception cref="ArgumentNullException">Thrown when the host or the dispatcher is null.</exception>
        public static CadRemoteServer ForTools(ICadRemoteHost host, ICadRemoteDispatcher dispatcher) =>
            new CadRemoteServer(host, dispatcher, null, "tools", true);

        /// <summary>Makes the server of the tools under an instance name of its own, so that tests do not meet.</summary>
        internal CadRemoteServer(ICadRemoteHost host, ICadRemoteDispatcher dispatcher, string instance, bool forTools)
            : this(host, dispatcher, null, instance, forTools) { }

        /// <summary>
        /// Gives a tool the token it connects with: 32 random bytes as 64 hexadecimal letters. A token is good until
        /// <see cref="RevokeToken"/>; the app gives each tool process its own and takes it back when the process ends.
        /// </summary>
        /// <returns>The token.</returns>
        /// <exception cref="InvalidOperationException">Thrown on the app's own server, which takes clients with no token.</exception>
        public string IssueToken()
        {
            if (!_forTools)
            {
                throw new InvalidOperationException("Only the server of the tools takes tokens.");
            }

            byte[] bytes = new byte[TokenBytes];

            using (RandomNumberGenerator random = RandomNumberGenerator.Create())
            {
                random.GetBytes(bytes);
            }

            var token = new StringBuilder(TokenBytes * 2);

            foreach (byte b in bytes)
            {
                token.Append(b.ToString("x2", CultureInfo.InvariantCulture));
            }

            lock (_gate)
            {
                _tokens.Add(token.ToString());
            }

            return token.ToString();
        }

        /// <summary>
        /// Takes a token back: the connections that carry it are refused at their next request and closed, and no new hello
        /// takes it. Does nothing for a token the server does not know.
        /// </summary>
        /// <param name="token">The token.</param>
        public void RevokeToken(string token)
        {
            lock (_gate)
            {
                _tokens.RemoveAll(t => SameToken(t, token));
            }
        }

        private bool HasToken(string token)
        {
            if (token == null)
            {
                return false;
            }

            lock (_gate)
            {
                bool found = false;

                // Every token is compared whole, so that the time a refusal takes says nothing of how near the guess was.
                foreach (string known in _tokens)
                {
                    found |= SameToken(known, token);
                }

                return found;
            }
        }

        private static bool SameToken(string a, string b)
        {
            if (a == null || b == null || a.Length != b.Length)
            {
                return false;
            }

            int difference = 0;

            for (int i = 0; i < a.Length; i++)
            {
                difference |= a[i] ^ b[i];
            }

            return difference == 0;
        }

        /// <summary>
        /// Makes the command of a tool, to put in the app's registry under the name the user types. The command is the run: it
        /// begins an undo step named after it, calls <paramref name="started"/> so that the app can tell the tool, and then waits
        /// at <c>NAME is working... (Esc to stop)</c> while the tool asks its questions, commits and ends it
        /// (<c>ask</c>, <c>commit</c>, <c>end</c>, <c>fail</c> of the channel). The step is kept when the tool ends it well and
        /// taken back when it fails or Esc ends it, and the drawing is busy for every other command until then.
        /// </summary>
        /// <param name="name">The name typed to run it, as <see cref="CadCommand"/> takes it.</param>
        /// <param name="usesSelection">Whether the objects selected before the command are taken by its first <c>selection</c> question, as MOVE takes them.</param>
        /// <param name="token">The token of the tool process that does the work: only a connection with it may name the run.</param>
        /// <param name="started">Called on the thread of the drawings as the command begins, with the run; null for nothing. It must not wait for the tool.</param>
        /// <returns>The command.</returns>
        /// <exception cref="ArgumentNullException">Thrown when the name or the token is null.</exception>
        /// <exception cref="ArgumentException">Thrown when the name is not one word.</exception>
        /// <exception cref="InvalidOperationException">Thrown on the app's own server, which has no runs.</exception>
        public CadCommand CreateRunCommand(
            string name,
            bool usesSelection,
            string token,
            Action<CadRemoteToolRun> started
        )
        {
            if (!_forTools)
            {
                throw new InvalidOperationException("Only the server of the tools has runs.");
            }

            if (token == null)
            {
                throw new ArgumentNullException(nameof(token));
            }

            var command = new CadCommand(name, c => Running(c, token, started), usesSelection);

            lock (_gate)
            {
                _runCommands.Add(command);
            }

            return command;
        }

        // ---- The body of the command ---------------------------------------------------------------------------------

        private IEnumerable<CadPrompt> Running(CadCommandContext c, string token, Action<CadRemoteToolRun> started)
        {
            CadDocument document = c.Document;
            var run = new CadRemoteToolRun(c.Command.Name, token, c.Session, c.Command);
            CadPrompt waiting = CadPrompt.ForWaiting(run.Name + " is working... (Esc to stop)");
            document.BeginGroup(run.Name);

            try
            {
                run.Id = ++_lastRun;
                run.DocumentId = IdOf(document);

                foreach (
                    int old in _runs
                        .Where(r => r.Value.IsEnded && r.Key <= run.Id - RunsKept)
                        .Select(r => r.Key)
                        .ToList()
                )
                {
                    _runs.Remove(old);
                }

                _runs[run.Id] = run;

                try
                {
                    started?.Invoke(run);
                }
                catch (Exception ex)
                {
                    run.Error = ex.Message;
                    run.Finish(CadRemoteToolRunOutcome.Failed);
                    throw;
                }

                while (!run.Ending)
                {
                    CadRemoteToolRun.RunQuestion question = run.Pending;

                    if (question == null)
                    {
                        yield return waiting;
                        continue;
                    }

                    foreach (CadPrompt prompt in Asking(c, question))
                    {
                        yield return prompt;

                        // The tool ended the run with the question open: the answer is dropped, the question cancelled below.
                        if (run.Ending)
                        {
                            break;
                        }
                    }

                    if (!run.Ending)
                    {
                        run.Pending = null;
                        question.Done.Set();
                    }
                }
            }
            finally
            {
                // However the body is left: the tool ended it, Esc ended it, or the command failed.
                CadRemoteToolRunOutcome outcome = run.Ending ? run.Intended : CadRemoteToolRunOutcome.Cancelled;

                if (run.Pending != null)
                {
                    run.Pending.Cancelled = true;
                    run.Pending.ByEsc = outcome == CadRemoteToolRunOutcome.Cancelled;
                    run.Pending.Done.Set();
                    run.Pending = null;
                }

                try
                {
                    if (document.IsInGroup)
                    {
                        if (outcome == CadRemoteToolRunOutcome.Completed)
                        {
                            document.EndGroup();
                        }
                        else
                        {
                            document.CancelGroup();
                        }
                    }
                }
                catch (Exception ex)
                {
                    GeometryHelperLog.Warn(
                        "CadRemoteServer: the undo step of " + run.Name + " could not be ended.",
                        ex
                    );
                }

                run.Close(outcome);
            }
        }

        /// <summary>The prompts of one question of the tool, which leaves the answer in <see cref="CadRemoteToolRun.RunQuestion.Input"/>.</summary>
        private static IEnumerable<CadPrompt> Asking(CadCommandContext c, CadRemoteToolRun.RunQuestion question)
        {
            IReadOnlyList<string> keywords = question.Keywords;

            switch (question.Kind)
            {
                case "point":
                    yield return CadPrompt.ForPoint(question.Message, question.Base, keywords, question.Default);
                    break;
                case "distance":
                    yield return CadPrompt.ForDistance(question.Message, question.Base, keywords, question.Default);
                    break;
                case "angle":
                    yield return CadPrompt.ForAngle(question.Message, question.Base, keywords, question.Default);
                    break;
                case "integer":
                    yield return CadPrompt.ForInteger(question.Message, keywords, question.Default);
                    break;
                case "string":
                    yield return CadPrompt.ForString(question.Message, question.Default);
                    break;
                case "keyword":
                    yield return CadPrompt.ForKeyword(question.Message, keywords, question.Default);
                    break;
                case "entity":
                    yield return CadPrompt.ForEntity(question.Message, keywords, question.Default);
                    break;
                case "selection":
                    foreach (CadPrompt asked in c.SelectFirst(CadPrompt.ForSelection(question.Message)))
                    {
                        yield return asked;
                    }

                    // Asking is not a command that uses the selection up: what was selected before stays selected.
                    c.ClearsSelection = false;
                    break;
                default:
                    // "real": a number typed, asked again after text that is none, as AutoCAD's getreal asks.
                    while (true)
                    {
                        yield return CadPrompt.ForKeywordOrText(
                            question.Message,
                            keywords ?? Array.Empty<string>(),
                            question.Default
                        );

                        if (
                            c.Input.Kind == CadInputKind.Text
                            && double.TryParse(
                                c.Input.Text.Trim(),
                                NumberStyles.Float,
                                CultureInfo.InvariantCulture,
                                out double number
                            )
                            && !double.IsNaN(number)
                            && !double.IsInfinity(number)
                        )
                        {
                            question.Parsed = number;
                            break;
                        }

                        if (c.Input.Kind != CadInputKind.Text)
                        {
                            break;
                        }

                        c.Write("Requires numeric value" + (question.Keywords != null ? " or option keyword." : "."));
                    }

                    break;
            }

            question.Input = c.Input;
        }

        // ---- The requests of a run -----------------------------------------------------------------------------------

        /// <summary>The run a request names, alive, and the tool's own: refused otherwise, in words that say why.</summary>
        private CadRemoteToolRun LiveRun(JsonObject request, string token)
        {
            if (request.Get("run") == null)
            {
                throw new Refusal(
                    "The request " + request.GetString("op") + " is made inside a run: it names the run."
                );
            }

            long id = request.GetLong("run", -1);

            if (!_runs.TryGetValue((int)id, out CadRemoteToolRun run))
            {
                throw new Refusal(
                    id > 0 && id <= _lastRun
                        ? "The run " + id + " has ended."
                        : "OpenCAD2D has no run " + id + " for this tool."
                );
            }

            if (!SameToken(run.Token, token))
            {
                throw new Refusal("The run " + id + " of " + run.Name + " belongs to another tool.");
            }

            if (run.IsEnded)
            {
                throw run.Outcome == CadRemoteToolRunOutcome.Cancelled
                    ? new Refusal("The run " + id + " of " + run.Name + " was ended with Esc.", true)
                    : new Refusal("The run " + id + " of " + run.Name + " has ended.");
            }

            return run;
        }

        /// <summary>Whether an op is one that only makes sense inside a run.</summary>
        private static bool IsRunOp(string op) =>
            op == RemoteOps.Ask
            || op == RemoteOps.Feed
            || op == RemoteOps.Write
            || op == RemoteOps.End
            || op == RemoteOps.Fail;

        /// <summary>Answers a request of a run other than a question, in one dispatch, holding up no other client.</summary>
        private JsonObject RunRequest(string op, JsonObject request, string token)
        {
            return Dispatch(() =>
            {
                CadRemoteToolRun run = LiveRun(request, token);

                switch (op)
                {
                    case RemoteOps.Write:
                        run.Session.Write(request.GetString("text") ?? string.Empty);
                        return new JsonObject();
                    case RemoteOps.End:
                        return Leave(run, CadRemoteToolRunOutcome.Completed);
                    case RemoteOps.Fail:
                        string line = request.GetString("message") ?? run.Name + " failed.";
                        run.Error = line;
                        run.Session.Write(line);
                        return Leave(run, CadRemoteToolRunOutcome.Failed);
                    case RemoteOps.Pick:
                    case RemoteOps.Command:
                        throw new Refusal(
                            "The request " + op + " cannot be made inside a run: the run asks the user with ask."
                        );
                    case RemoteOps.Commit:
                        return Commit(Find(request), request, run);
                    default:
                        return Execute(op, request);
                }
            });
        }

        /// <summary>The tool ends the run: its command is resumed to leave its body, which keeps or takes back the undo step.</summary>
        private static JsonObject Leave(CadRemoteToolRun run, CadRemoteToolRunOutcome outcome)
        {
            run.Finish(outcome);

            // Another command running inside the run (a transparent ZOOM at a question) is ended with it, as Esc ends them.
            if (!run.Session.Resume(run.Command))
            {
                run.Session.Escape();
            }

            return new JsonObject();
        }

        /// <summary>
        /// Asks the user a question of a run and waits for the answer: the question is given to the command, which asks it on
        /// the command line of the drawing, and the request waits here, on its own thread, holding no lock of the server, so
        /// that every other client is served while the user takes as long as they like.
        /// </summary>
        private JsonObject RunAsk(JsonObject request, string token)
        {
            CadRemoteToolRun run = null;
            CadRemoteToolRun.RunQuestion question = Dispatch(() =>
            {
                run = LiveRun(request, token);

                if (run.Pending != null)
                {
                    throw new Refusal(
                        run.Name + " already asked a question that the user has not answered: wait for it."
                    );
                }

                var asked = Question(request);
                run.Pending = asked;

                if (!run.Session.Resume(run.Command))
                {
                    run.Pending = null;
                    throw new Refusal(
                        "The command line of " + run.Document.Title + " is busy: the question cannot be asked now."
                    );
                }

                return asked;
            });

            while (!question.Done.Wait(CommandPoll))
            {
                if (_stopping)
                {
                    throw new Refusal("OpenCAD2D is shutting down.");
                }
            }

            if (question.Cancelled)
            {
                throw question.ByEsc
                    ? new Refusal("The user ended " + run.Name + " with Esc.", true)
                    : new Refusal("The run " + run.Id + " of " + run.Name + " ended while the question was open.");
            }

            return Dispatch(() => Answered(run, question));
        }

        private static CadRemoteToolRun.RunQuestion Question(JsonObject request)
        {
            string kind = request.GetString("kind");

            if (
                kind != "point"
                && kind != "distance"
                && kind != "angle"
                && kind != "integer"
                && kind != "real"
                && kind != "string"
                && kind != "keyword"
                && kind != "entity"
                && kind != "selection"
            )
            {
                throw new Refusal("OpenCAD2D does not know the question " + kind + ": it is of an older version.");
            }

            JsonObject at = request.GetObject("base");
            var words = new List<string>();
            JsonArray listed = request.GetArray("keywords");

            if (listed != null)
            {
                words.AddRange(listed.OfType<string>());
            }

            if (kind == "keyword" && words.Count == 0)
            {
                throw new Refusal("A question for a keyword needs the keywords.");
            }

            return new CadRemoteToolRun.RunQuestion
            {
                Kind = kind,
                Message = request.GetString("message") ?? string.Empty,
                Base = at == null ? (GeoPoint2?)null : new GeoPoint2(at.GetDouble("x"), at.GetDouble("y")),
                Keywords = words.Count == 0 ? null : words.ToArray(),
                Default = request.GetString("default"),
            };
        }

        /// <summary>The answer the user gave, as the tool reads it: its kind, and the point, number, text or objects.</summary>
        private JsonObject Answered(CadRemoteToolRun run, CadRemoteToolRun.RunQuestion question)
        {
            if (question.Parsed.HasValue)
            {
                return new JsonObject().Set("kind", "number").Set("number", question.Parsed.Value);
            }

            CadInput input = question.Input;

            switch (input.Kind)
            {
                case CadInputKind.Point:
                    return new JsonObject()
                        .Set("kind", "point")
                        .Set("x", input.Point.X)
                        .Set("y", input.Point.Y)
                        .Set("z", 0.0);
                case CadInputKind.Number:
                    return new JsonObject().Set("kind", "number").Set("number", input.Number);
                case CadInputKind.Keyword:
                    return new JsonObject().Set("kind", "keyword").Set("text", input.Text);
                case CadInputKind.Text:
                    return new JsonObject().Set("kind", "text").Set("text", input.Text);
                case CadInputKind.Selection:
                    var entities = new List<DxfEntity>();

                    foreach (
                        string handle in input.Handles.OrderBy(HandleNumber).ThenBy(h => h, StringComparer.Ordinal)
                    )
                    {
                        DxfEntity entity = run.Document.FindEntity(handle);

                        if (entity != null && !entity.IsErased)
                        {
                            entities.Add(entity);
                        }
                    }

                    return Pack(run.Document, entities).Set("kind", "selection").Set("document", Info(run.Document));
                default:
                    return new JsonObject().Set("kind", "none");
            }
        }
    }
}
