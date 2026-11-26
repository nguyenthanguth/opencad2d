using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Threading;
using ModelContextProtocol;
using ModelContextProtocol.Protocol;
using ModelContextProtocol.Server;

namespace OpenCAD2D.Mcp
{
    /// <summary>
    /// Runs a plan: checks every step, opens the transaction its writers share, calls each tool through the table the server
    /// serves, hands each step the values it takes from the steps before it, and then commits, or takes everything back.
    /// </summary>
    internal sealed class PlanRunner
    {
        /// <summary>The most steps of a plan: a plan is read and answered as one message.</summary>
        public const int MostSteps = 200;

        /// <summary>The tools a plan never runs, with the reason: they replace the drawing, step through its history or write its file, which a plan cannot take back.</summary>
        private static readonly SortedDictionary<string, string> Refused = new SortedDictionary<string, string>(
            StringComparer.Ordinal
        )
        {
            { "new_drawing", "it replaces the drawing the plan works on" },
            { "open_drawing", "it replaces the drawing the plan works on" },
            { "redo", "it steps through the history the plan's undo step is part of" },
            { "run_plan", "a plan does not hold a plan" },
            { "save", "it writes the drawing's file, which a plan that stops cannot take back" },
            { "undo", "it steps through the history the plan's undo step is part of" },
        };

        /// <summary>
        /// The tools that write a file and no part of the drawing. Those that end a plan are run after the plan's group of edits has been
        /// kept, and not at all where the plan was stopped, taken back or only a dry run; <c>save_as</c> is allowed in a plan only there.
        /// </summary>
        private static readonly HashSet<string> FileWriters = new HashSet<string>(StringComparer.Ordinal)
        {
            "export_svg",
            "plot_pdf",
            "save_as",
        };

        private readonly McpSession _session;
        private readonly McpServerPrimitiveCollection<McpServerTool> _tools;

        /// <summary>Makes a runner over the tools the server serves.</summary>
        public PlanRunner(McpSession session, McpServerPrimitiveCollection<McpServerTool> tools)
        {
            _session = session;
            _tools = tools;
        }

        /// <summary>One step as it was read: the tool found, its arguments and whether it writes.</summary>
        private sealed class Prepared
        {
            public string Id;
            public string ToolName;
            public McpServerTool Tool;
            public JsonObject Arguments;
            public bool Writes;
            public List<string> Errors = new List<string>();
        }

        /// <summary>Runs a plan on a drawing.</summary>
        /// <param name="drawing">The drawing the plan works on.</param>
        /// <param name="context">The request of the plan, which each step's request is made from.</param>
        /// <param name="steps">The steps.</param>
        /// <param name="dryRun">Whether to put the drawing back after the steps.</param>
        /// <param name="stopOnError">Whether the first step that fails stops the plan and takes it back.</param>
        /// <param name="cancel">Cancels the plan between two steps; what was done is taken back.</param>
        /// <returns>What each step gave, and what came of the plan.</returns>
        /// <exception cref="McpException">Thrown for a plan with no steps or too many, one with a writer when the server is read-only, or an app that is busy.</exception>
        public PlanResult Run(
            IDrawingBackend drawing,
            RequestContext<CallToolRequestParams> context,
            IReadOnlyList<PlanStep> steps,
            bool dryRun,
            bool stopOnError,
            CancellationToken cancel
        )
        {
            if (steps == null || steps.Count == 0)
            {
                throw new McpException("A plan needs at least one step: {tool, arguments, id}.");
            }

            if (steps.Count > MostSteps)
            {
                throw new McpException(
                    "A plan has at most " + MostSteps + " steps and this one has " + steps.Count + ": split it."
                );
            }

            List<Prepared> prepared = Prepare(steps, out int trailStart);
            List<Prepared> writers = prepared.Where(p => p.Writes).ToList();

            if (writers.Count > 0 && _session.Options.ReadOnly)
            {
                throw new McpException(
                    "run_plan is refused: this server was started with --read-only and the plan has steps that write ("
                        + string.Join(", ", writers.Select(w => w.Id + " " + w.ToolName))
                        + "). Nothing was run."
                );
            }

            if (prepared.Any(p => p.Errors.Count > 0))
            {
                return Invalid(prepared, dryRun);
            }

            var results = new Dictionary<string, JsonNode>(StringComparer.OrdinalIgnoreCase);
            var answers = new List<PlanStepResult>();
            bool drafting = prepared.Take(trailStart).Any(p => p.Writes);
            PlanTransaction transaction = drafting ? PlanTransaction.Open(drawing) : null;
            bool taken = false;
            bool stopped = false;
            int next = 0;

            try
            {
                for (; next < trailStart; next++)
                {
                    cancel.ThrowIfCancellationRequested();
                    Prepared step = prepared[next];
                    PlanStepResult answer = RunStep(step, context, results, cancel);
                    answers.Add(answer);

                    if (answer.Ok)
                    {
                        results[step.Id] = answer.Result ?? new JsonObject();
                    }

                    // A tool that cancelled its own group of edits took the whole plan back with it.
                    if (transaction != null && !transaction.IsOpen)
                    {
                        taken = true;
                        answer.Error = answer.Error ?? "the plan's group of edits was closed by this step";
                        answer.Ok = false;
                        answer.Result = null;
                        stopped = true;
                        next++;
                        break;
                    }

                    if (!answer.Ok && stopOnError)
                    {
                        stopped = true;
                        next++;
                        break;
                    }
                }

                for (; stopped && next < prepared.Count; next++)
                {
                    answers.Add(Skipped(prepared[next], "Not run: the plan stopped at an earlier step."));
                }

                bool failed = answers.Any(a => !a.Ok && !a.Skipped);
                bool back = taken || (stopped && failed) || dryRun;

                if (transaction != null)
                {
                    if (back)
                    {
                        transaction.Rollback();
                    }
                    else
                    {
                        transaction.Commit();
                    }

                    transaction = null;
                }

                // The files at the end are written once what the plan drew is kept: a dry run writes none, and a plan that was stopped
                // has none to write.
                for (; next < prepared.Count; next++)
                {
                    cancel.ThrowIfCancellationRequested();
                    Prepared step = prepared[next];

                    if (dryRun)
                    {
                        answers.Add(
                            new PlanStepResult
                            {
                                Id = step.Id,
                                Tool = step.ToolName,
                                Ok = true,
                                Result = new JsonObject { ["written"] = false, ["note"] = "A dry run writes no file." },
                            }
                        );
                        continue;
                    }

                    PlanStepResult answer = RunStep(step, context, results, cancel);
                    answers.Add(answer);

                    if (answer.Ok)
                    {
                        results[step.Id] = answer.Result ?? new JsonObject();
                    }
                    else if (stopOnError)
                    {
                        next++;

                        for (; next < prepared.Count; next++)
                        {
                            answers.Add(Skipped(prepared[next], "Not run: the plan stopped at an earlier step."));
                        }

                        break;
                    }
                }

                return Done(prepared, answers, dryRun, back && drafting, taken, trailStart);
            }
            finally
            {
                // An exception on the way (a cancel, a lost app) leaves nothing half done.
                if (transaction != null)
                {
                    try
                    {
                        transaction.Rollback();
                    }
                    catch (Exception exception) when (!(exception is OperationCanceledException))
                    {
                        Console.Error.WriteLine(exception);
                    }
                }
            }
        }

        /// <summary>Reads the steps: ids, tools, arguments, and what is wrong with each, for all of them.</summary>
        private List<Prepared> Prepare(IReadOnlyList<PlanStep> steps, out int trailStart)
        {
            var list = new List<Prepared>();
            var seen = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);

            for (int i = 0; i < steps.Count; i++)
            {
                PlanStep step = steps[i];
                var item = new Prepared
                {
                    Id = string.IsNullOrWhiteSpace(step?.Id)
                        ? "s" + (i + 1).ToString(CultureInfo.InvariantCulture)
                        : step.Id.Trim(),
                    ToolName = step?.Tool?.Trim() ?? string.Empty,
                    Arguments = step?.Arguments == null ? new JsonObject() : (JsonObject)step.Arguments.DeepClone(),
                };
                list.Add(item);

                if (!PlanSchema.Id.IsMatch(item.Id))
                {
                    item.Errors.Add(
                        "The id "
                            + item.Id
                            + " is not allowed: letters, digits, underscore and hyphen, starting with a letter or underscore."
                    );
                }
                else if (seen.TryGetValue(item.Id, out int first))
                {
                    item.Errors.Add(
                        "The id " + item.Id + " is also the id of step " + first + ": give each step its own."
                    );
                }
                else
                {
                    seen[item.Id] = i + 1;
                }
            }

            // The steps at the end that only write files: they are run after the plan's edits are kept.
            trailStart = list.Count;

            while (trailStart > 0 && FileWriters.Contains(list[trailStart - 1].ToolName))
            {
                trailStart--;
            }

            for (int i = 0; i < list.Count; i++)
            {
                Prepared item = list[i];
                Check(item, list, i, trailStart);
            }

            return list;
        }

        private void Check(Prepared item, List<Prepared> all, int index, int trailStart)
        {
            if (string.IsNullOrEmpty(item.ToolName))
            {
                item.Errors.Add("The step names no tool.");
                return;
            }

            if (!_tools.TryGetPrimitive(item.ToolName, out McpServerTool tool))
            {
                item.Errors.Add(
                    "There is no tool "
                        + item.ToolName
                        + ": the tools are "
                        + string.Join(
                            ", ",
                            _tools.Select(t => t.ProtocolTool.Name).OrderBy(n => n, StringComparer.Ordinal)
                        )
                        + "."
                );
                return;
            }

            item.Tool = tool;
            item.Writes = tool.ProtocolTool.Annotations?.ReadOnlyHint != true;

            if (Refused.TryGetValue(item.ToolName, out string why))
            {
                item.Errors.Add(item.ToolName + " is not allowed in a plan: " + why + ".");
                return;
            }

            if (item.ToolName == "save_as" && index < trailStart)
            {
                item.Errors.Add(
                    "save_as is allowed in a plan only as its last step, or among the last steps that write files (plot_pdf, export_svg, save_as): it writes once the steps before it are kept, and a plan that stops cannot take a file back."
                );
                return;
            }

            if (item.ToolName == "run_command" && !_session.Options.AllowScripts)
            {
                item.Errors.Add(
                    "run_command is refused: it types free command, AutoLISP or script text, which this server only does when it was started with --allow-scripts."
                );
                return;
            }

            if (item.ToolName == "run_command" && _session.Options.Attach)
            {
                item.Errors.Add(
                    "run_command is not allowed in a plan on an app: a command it starts may wait for the user's answer, and the plan could not end its group of steps while it does."
                );
                return;
            }

            item.Errors.AddRange(PlanSchema.Validate(tool.ProtocolTool.InputSchema, item.Arguments));

            // The text of run_command is typed as written: no reference puts another step's answer on the command line.
            if (item.ToolName != "run_command")
            {
                CheckReferences(item.Arguments, all, index, item.Errors, string.Empty);
            }
        }

        /// <summary>Finds the references of a value and checks that each names an earlier step.</summary>
        private static void CheckReferences(
            JsonNode value,
            List<Prepared> all,
            int index,
            List<string> errors,
            string path
        )
        {
            if (value is JsonObject members)
            {
                foreach (KeyValuePair<string, JsonNode> member in members.OrderBy(m => m.Key, StringComparer.Ordinal))
                {
                    CheckReferences(
                        member.Value,
                        all,
                        index,
                        errors,
                        path.Length == 0 ? member.Key : path + "." + member.Key
                    );
                }
            }
            else if (value is JsonArray items)
            {
                for (int i = 0; i < items.Count; i++)
                {
                    CheckReferences(
                        items[i],
                        all,
                        index,
                        errors,
                        path + "[" + i.ToString(CultureInfo.InvariantCulture) + "]"
                    );
                }
            }
            else if (value is JsonValue leaf && leaf.GetValueKind() == JsonValueKind.String)
            {
                string text = leaf.GetValue<string>();

                if (text.StartsWith("$$", StringComparison.Ordinal))
                {
                    return;
                }

                try
                {
                    if (PlanSchema.TryReference(text, out string id, out _))
                    {
                        int at = all.FindIndex(p => string.Equals(p.Id, id, StringComparison.OrdinalIgnoreCase));

                        if (at < 0)
                        {
                            errors.Add(
                                "The argument " + path + " refers to " + text + " but no step has the id " + id + "."
                            );
                        }
                        else if (at >= index)
                        {
                            errors.Add(
                                "The argument "
                                    + path
                                    + " refers to "
                                    + text
                                    + ", which is the step "
                                    + (
                                        at == index
                                            ? "itself"
                                            : (at + 1).ToString(CultureInfo.InvariantCulture) + ", after it"
                                    )
                                    + ": a step takes values only from the steps before it."
                            );
                        }
                    }
                }
                catch (ArgumentException exception)
                {
                    errors.Add("The argument " + path + ": " + exception.Message);
                }
            }
        }

        /// <summary>Runs one step: its references put in, its tool called, its answer read.</summary>
        private PlanStepResult RunStep(
            Prepared step,
            RequestContext<CallToolRequestParams> context,
            Dictionary<string, JsonNode> results,
            CancellationToken cancel
        )
        {
            var answer = new PlanStepResult { Id = step.Id, Tool = step.ToolName };

            try
            {
                var arguments = new Dictionary<string, JsonElement>(StringComparer.Ordinal);
                var filled = (JsonObject)(
                    step.ToolName == "run_command"
                        ? step.Arguments.DeepClone()
                        : Fill(step.Arguments.DeepClone(), results, string.Empty)
                );

                // What a reference put in is checked as what was written is: a value of the wrong type is refused here.
                List<string> wrong = PlanSchema.Validate(step.Tool.ProtocolTool.InputSchema, filled);

                if (wrong.Count > 0)
                {
                    throw new ArgumentException(string.Join(" ", wrong));
                }

                foreach (KeyValuePair<string, JsonNode> member in filled.OrderBy(m => m.Key, StringComparer.Ordinal))
                {
                    arguments[member.Key] = JsonSerializer.SerializeToElement(member.Value);
                }

                var request = new RequestContext<CallToolRequestParams>(
                    context.Server,
                    context.JsonRpcRequest,
                    new CallToolRequestParams { Name = step.ToolName, Arguments = arguments }
                )
                {
                    MatchedPrimitive = step.Tool,
                };

                // The tools run on this thread, inside the session's lock, which the plan holds for all its steps.
                CallToolResult result = step.Tool.InvokeAsync(request, cancel).AsTask().GetAwaiter().GetResult();

                if (result.IsError == true)
                {
                    answer.Error = TextOf(result);
                }
                else
                {
                    answer.Ok = true;
                    answer.Result = ResultOf(result);
                }
            }
            catch (ArgumentException exception)
            {
                answer.Error = exception.Message;
            }
            catch (McpException exception)
            {
                answer.Error = exception.Message;
            }

            return answer;
        }

        /// <summary>Puts the values of earlier steps into a value, where a string is a reference; a string of <c>$$</c> is one of <c>$</c>.</summary>
        private static JsonNode Fill(JsonNode value, Dictionary<string, JsonNode> results, string path)
        {
            if (value is JsonObject members)
            {
                foreach (string key in members.Select(m => m.Key).OrderBy(k => k, StringComparer.Ordinal).ToList())
                {
                    JsonNode old = members[key];
                    JsonNode filled = Fill(old, results, path.Length == 0 ? key : path + "." + key);

                    // A node put back into the object it already belongs to is refused: only a replaced value is set.
                    if (!ReferenceEquals(old, filled))
                    {
                        members[key] = filled;
                    }
                }

                return members;
            }

            if (value is JsonArray items)
            {
                for (int i = 0; i < items.Count; i++)
                {
                    JsonNode old = items[i];
                    JsonNode filled = Fill(old, results, path + "[" + i.ToString(CultureInfo.InvariantCulture) + "]");

                    if (!ReferenceEquals(old, filled))
                    {
                        items[i] = filled;
                    }
                }

                return items;
            }

            if (value is JsonValue leaf && leaf.GetValueKind() == JsonValueKind.String)
            {
                string text = leaf.GetValue<string>();

                if (text.StartsWith("$$", StringComparison.Ordinal))
                {
                    return JsonValue.Create(text.Substring(1));
                }

                if (PlanSchema.TryReference(text, out string id, out string rest))
                {
                    if (!results.TryGetValue(id, out JsonNode result))
                    {
                        throw new ArgumentException(
                            "The argument " + path + " refers to " + text + ", but the step " + id + " gave no answer."
                        );
                    }

                    try
                    {
                        return PlanSchema.Follow(result, rest);
                    }
                    catch (ArgumentException exception)
                    {
                        throw new ArgumentException(
                            "The argument " + path + " refers to " + text + ", but " + exception.Message
                        );
                    }
                }
            }

            return value;
        }

        /// <summary>The answer of a tool as JSON: its structured answer, or its text read as JSON, or its text and the size of its pictures.</summary>
        private static JsonNode ResultOf(CallToolResult result)
        {
            if (result.StructuredContent.HasValue)
            {
                return JsonNode.Parse(result.StructuredContent.Value.GetRawText());
            }

            List<string> texts = result.Content.OfType<TextContentBlock>().Select(t => t.Text).ToList();
            List<ImageContentBlock> images = result.Content.OfType<ImageContentBlock>().ToList();

            if (images.Count == 0 && texts.Count == 1)
            {
                try
                {
                    return JsonNode.Parse(texts[0]);
                }
                catch (JsonException) { }
            }

            var answer = new JsonObject();

            if (texts.Count > 0)
            {
                answer["text"] = string.Join("\n", texts);
            }

            if (images.Count > 0)
            {
                var list = new JsonArray();

                foreach (ImageContentBlock image in images)
                {
                    list.Add(new JsonObject { ["mimeType"] = image.MimeType, ["bytes"] = image.DecodedData.Length });
                }

                answer["images"] = list;
                answer["note"] = "The picture itself is not in a plan's answer: call the tool alone to see it.";
            }

            return answer;
        }

        private static string TextOf(CallToolResult result)
        {
            string text = string.Join("\n", result.Content.OfType<TextContentBlock>().Select(t => t.Text));
            return string.IsNullOrEmpty(text) ? "The tool failed." : text;
        }

        private static PlanStepResult Skipped(Prepared step, string why) =>
            new PlanStepResult
            {
                Id = step.Id,
                Tool = step.ToolName,
                Skipped = true,
                Error = why,
            };

        /// <summary>The answer for a plan that was not run because steps of it are wrong: every problem, and the steps not looked into marked skipped.</summary>
        private static PlanResult Invalid(List<Prepared> prepared, bool dryRun)
        {
            var answers = new List<PlanStepResult>();

            foreach (Prepared step in prepared)
            {
                answers.Add(
                    step.Errors.Count == 0
                        ? Skipped(step, "Not run: the plan has steps that are wrong.")
                        : new PlanStepResult
                        {
                            Id = step.Id,
                            Tool = step.ToolName,
                            Error = string.Join(" ", step.Errors),
                        }
                );
            }

            int wrong = prepared.Count(p => p.Errors.Count > 0);
            return new PlanResult
            {
                DryRun = dryRun,
                Ok = false,
                RolledBack = false,
                Changed = false,
                Message =
                    "Nothing was run: "
                    + wrong
                    + (wrong == 1 ? " step is" : " steps are")
                    + " wrong. Fix "
                    + (wrong == 1 ? "it" : "them")
                    + " and send the plan again; the drawing is as it was.",
                Totals = Totals(prepared, answers),
                Steps = answers,
            };
        }

        private static PlanResult Done(
            List<Prepared> prepared,
            List<PlanStepResult> answers,
            bool dryRun,
            bool rolledBack,
            bool takenByStep,
            int trailStart
        )
        {
            PlanTotals totals = Totals(prepared, answers);
            bool ok = totals.Failed == 0 && totals.Skipped == 0;
            bool changed = !rolledBack && prepared.Where((p, i) => i < trailStart && p.Writes && answers[i].Ok).Any();
            PlanStepResult first = answers.FirstOrDefault(a => !a.Ok && !a.Skipped);
            string message;

            if (takenByStep)
            {
                message =
                    "Step "
                    + first.Id
                    + " ("
                    + first.Tool
                    + ") failed and took the whole plan back with it: the drawing is as it was before the plan. "
                    + first.Error;
            }
            else if (dryRun)
            {
                message = ok
                    ? "Dry run: "
                        + totals.Steps
                        + (totals.Steps == 1 ? " step ran" : " steps ran")
                        + (totals.Writers > 0 ? " and everything was taken back" : "; none of them writes")
                        + ". The drawing is as it was."
                    : "Dry run: step "
                        + first.Id
                        + " ("
                        + first.Tool
                        + ") failed: "
                        + first.Error
                        + " The drawing is as it was.";
            }
            else if (ok)
            {
                message =
                    "Ran "
                    + totals.Steps
                    + (totals.Steps == 1 ? " step" : " steps")
                    + (changed ? "; what they wrote is one undo step." : ".");
            }
            else if (rolledBack)
            {
                message =
                    "Step "
                    + first.Id
                    + " ("
                    + first.Tool
                    + ") failed: "
                    + first.Error
                    + " The plan was stopped and everything it had done was taken back: the drawing is as it was before the plan.";
            }
            else if (totals.Skipped > 0)
            {
                message =
                    "Step "
                    + first.Id
                    + " ("
                    + first.Tool
                    + ") failed: "
                    + first.Error
                    + " The plan was stopped; it had changed nothing.";
            }
            else
            {
                message =
                    totals.Failed
                    + (totals.Failed == 1 ? " step failed" : " steps failed")
                    + " and the other "
                    + totals.Ok
                    + (changed ? " are kept as one undo step." : " changed nothing.");
            }

            return new PlanResult
            {
                DryRun = dryRun,
                Ok = ok,
                RolledBack = rolledBack,
                Changed = changed,
                Message = message,
                Totals = totals,
                Steps = answers,
            };
        }

        private static PlanTotals Totals(List<Prepared> prepared, List<PlanStepResult> answers) =>
            new PlanTotals
            {
                Steps = answers.Count,
                Ok = answers.Count(a => a.Ok),
                Failed = answers.Count(a => !a.Ok && !a.Skipped),
                Skipped = answers.Count(a => a.Skipped),
                Writers = prepared.Count(p => p.Writes),
            };
    }
}
