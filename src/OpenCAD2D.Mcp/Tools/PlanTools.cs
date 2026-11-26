using System.Collections.Generic;
using System.ComponentModel;
using System.Text.Json.Nodes;
using System.Threading;
using ModelContextProtocol.Protocol;
using ModelContextProtocol.Server;

namespace OpenCAD2D.Mcp
{
    /// <summary>One step of a plan: a tool of the server, its arguments, and a name later steps refer to it by.</summary>
    internal sealed class PlanStep
    {
        /// <summary>Gets or sets the name of the tool, as <c>add_line</c>.</summary>
        [Description("The name of the tool to call, as in the tool list: add_line, move, measure_text ...")]
        public string Tool { get; set; }

        /// <summary>Gets or sets the arguments of the tool, as its own schema takes them.</summary>
        [Description(
            "The arguments of the tool, a JSON object as the tool takes them. A string that is exactly $id or $id.path is replaced by that value of the result of the earlier step with that id, as \"$tag1.handle\" or \"$free.boxes[0]\"."
        )]
        public JsonObject Arguments { get; set; }

        /// <summary>Gets or sets the name later steps refer to this one by; <c>s1</c>, <c>s2</c> ... (the step number) where left out.</summary>
        [Description(
            "A name for the step, letters, digits, underscore and hyphen, starting with a letter or underscore, for \"$id.path\" references; s1, s2 ... (the step's number) where left out."
        )]
        public string Id { get; set; }
    }

    /// <summary>What a step of a plan gave.</summary>
    internal sealed class PlanStepResult
    {
        /// <summary>Gets or sets the id of the step, the one it was given or <c>s</c> and its number.</summary>
        public string Id { get; set; }

        public string Tool { get; set; }

        /// <summary>Gets or sets whether the step ran and the tool gave an answer.</summary>
        public bool Ok { get; set; }

        /// <summary>Gets or sets whether the step was not run: the plan stopped before it, or a step before it was refused.</summary>
        public bool Skipped { get; set; }

        /// <summary>Gets or sets what the tool answered, as it answers when called alone; null for a step that failed or was skipped.</summary>
        public JsonNode Result { get; set; }

        /// <summary>Gets or sets why the step failed or was refused; null for a step that ran.</summary>
        public string Error { get; set; }
    }

    /// <summary>The counts of a plan.</summary>
    internal sealed class PlanTotals
    {
        public int Steps { get; set; }

        /// <summary>Gets or sets how many steps ran and gave an answer.</summary>
        public int Ok { get; set; }

        public int Failed { get; set; }

        public int Skipped { get; set; }

        /// <summary>Gets or sets how many steps are of tools that write.</summary>
        public int Writers { get; set; }
    }

    /// <summary>What <c>run_plan</c> answers.</summary>
    internal sealed class PlanResult
    {
        /// <summary>Gets or sets whether the plan was a dry run.</summary>
        public bool DryRun { get; set; }

        /// <summary>Gets or sets whether every step ran and gave an answer.</summary>
        public bool Ok { get; set; }

        /// <summary>Gets or sets whether the drawing was put back as it was before the plan, a dry run or a plan that stopped.</summary>
        public bool RolledBack { get; set; }

        /// <summary>Gets or sets whether the plan changed the drawing: one undo step was made, and an undo takes it all back.</summary>
        public bool Changed { get; set; }

        /// <summary>Gets or sets what came of the plan in a sentence.</summary>
        public string Message { get; set; }

        public PlanTotals Totals { get; set; }

        public IReadOnlyList<PlanStepResult> Steps { get; set; }
    }

    /// <summary>The tool that runs a list of calls of the other tools in one call.</summary>
    internal sealed class PlanTools
    {
        private readonly McpSession _session;
        private readonly PlanRunner _runner;

        /// <summary>Makes the tool over the tools the server serves.</summary>
        /// <param name="session">The session.</param>
        /// <param name="tools">The tool table of the server: every tool a plan may call is in it.</param>
        public PlanTools(McpSession session, McpServerPrimitiveCollection<McpServerTool> tools)
        {
            _session = session ?? throw new System.ArgumentNullException(nameof(session));
            _runner = new PlanRunner(session, tools ?? throw new System.ArgumentNullException(nameof(tools)));
        }

        [McpServerTool(Name = "run_plan", ReadOnly = false, Destructive = true, Idempotent = false, OpenWorld = false)]
        [Description(
            "Runs a list of calls of other tools in one call, one after the other, and gives each step's answer. Every step is checked first (the tool, its arguments against its schema, its references) and nothing runs if any is wrong. A step's arguments may take a value from an earlier step's answer: a string that is exactly \"$id.path\" (the step's id, then .name or [n], as \"$a1.handle\" or \"$free.boxes[0]\") is replaced by it. All the steps that write are one undo step, so one undo takes them all back; the first step that fails stops the plan and the drawing is put back as it was (stop_on_error false goes on instead and keeps what worked). dry_run runs the plan and puts everything back, to see what it would answer, handles included. Not allowed in a plan: run_plan, open_drawing, new_drawing, undo, redo, save, save_as; run_command only when the server allows scripts. A plan with a step that writes is refused when the server is read-only. At most 200 steps."
        )]
        public PlanResult RunPlan(
            [Description("The steps, in order: {tool, arguments, id}.")] PlanStep[] steps,
            [Description("True to run the plan and put the drawing back as it was, reporting what it would give.")]
                bool dry_run = false,
            [Description(
                "True (the default) to stop at the first step that fails and take back what the plan did; false to go on and keep what worked."
            )]
                bool stop_on_error = true,
            RequestContext<CallToolRequestParams> context = null,
            CancellationToken cancel = default
        ) =>
            _session.Run("run_plan", false, drawing =>
                _runner.Run(drawing, context, steps, dry_run, stop_on_error, cancel)
            );
    }
}
