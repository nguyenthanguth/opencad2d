using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Threading;
using System.Threading.Tasks;
using ModelContextProtocol.Protocol;
using ModelContextProtocol.Server;

namespace OpenCAD2D.Mcp
{
    /// <summary>
    /// A tool of the server that checks its arguments against its own input schema before it is called, as a step of a plan is checked,
    /// so that a missing or wrongly typed argument is answered with which argument it is and what the tool takes, and not with the
    /// SDK's "An error occurred invoking 'add_line'.".
    /// </summary>
    internal sealed class CheckedTool : DelegatingMcpServerTool
    {
        /// <summary>Wraps a tool.</summary>
        /// <param name="inner">The tool.</param>
        public CheckedTool(McpServerTool inner)
            : base(inner) { }

        /// <inheritdoc />
        public override ValueTask<CallToolResult> InvokeAsync(
            RequestContext<CallToolRequestParams> request,
            CancellationToken cancellationToken = default
        )
        {
            var arguments = new JsonObject();
            IDictionary<string, JsonElement> given = request?.Params?.Arguments;

            if (given != null)
            {
                var kept = new Dictionary<string, JsonElement>(StringComparer.Ordinal);

                foreach (string name in new SortedSet<string>(given.Keys, StringComparer.Ordinal))
                {
                    // A client that sends null for an argument that has a default means to leave it out.
                    if (given[name].ValueKind != JsonValueKind.Null || Required(name))
                    {
                        arguments[name] = JsonSerializer.SerializeToNode(given[name]);
                        kept[name] = given[name];
                    }
                }

                // The tool is called without them too: the SDK would refuse a null for a parameter of a type that has none.
                if (kept.Count != given.Count)
                {
                    request.Params.Arguments = kept;
                }
            }

            List<string> wrong = PlanSchema.Validate(ProtocolTool.InputSchema, arguments);

            if (wrong.Count == 0)
            {
                return base.InvokeAsync(request, cancellationToken);
            }

            return new ValueTask<CallToolResult>(
                new CallToolResult
                {
                    IsError = true,
                    Content = new List<ContentBlock>
                    {
                        new TextContentBlock { Text = ProtocolTool.Name + " was not run: " + string.Join(" ", wrong) },
                    },
                }
            );
        }

        private bool Required(string name) => IsRequired(ProtocolTool.InputSchema, name);

        /// <summary>Whether the input schema of a tool lists the argument as required.</summary>
        internal static bool IsRequired(JsonElement schema, string name) =>
            schema.TryGetProperty("required", out JsonElement required)
            && required.ValueKind == JsonValueKind.Array
            && required.EnumerateArray().Any(r => string.Equals(r.GetString(), name, StringComparison.Ordinal));
    }
}
