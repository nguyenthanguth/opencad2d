using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Threading.Tasks;
using Xunit;

namespace OpenCAD2D.Mcp.UnitTest
{
    /// <summary>
    /// The table describe_kinds gives is written by hand (N3, left as it is): this test holds it to the tools it names, so that a
    /// renamed or dropped argument of an add tool cannot leave a wrong name in it.
    /// </summary>
    public class McpKindArgumentTests
    {
        [Fact]
        public async Task The_arguments_describe_kinds_names_are_arguments_of_the_tools_that_write_the_kind()
        {
            await using McpHarness harness = await McpHarness.StartAsync(new McpOptions());
            JsonNode kinds = await harness.CallAsync("describe_kinds");
            var tools = (await harness.Client.ListToolsAsync()).ToDictionary(t => t.Name, StringComparer.Ordinal);
            int checkedKinds = 0;
            var wrong = new List<string>();

            foreach (JsonNode row in kinds.AsArray())
            {
                string written = (string)row["written"];

                if (written == null || row["arguments"] == null)
                {
                    continue;
                }

                var known = new HashSet<string>(StringComparer.Ordinal);

                foreach (string name in written.Split(',').Select(n => n.Trim()))
                {
                    Assert.True(
                        tools.ContainsKey(name),
                        (string)row["kind"] + " is written by " + name + ", not a tool."
                    );

                    foreach (
                        JsonProperty property in tools[name].JsonSchema.GetProperty("properties").EnumerateObject()
                    )
                    {
                        known.Add(property.Name);
                    }
                }

                foreach (JsonNode argument in row["arguments"].AsArray().Where(a => !known.Contains((string)a)))
                {
                    wrong.Add(
                        (string)row["kind"]
                            + " names the argument "
                            + (string)argument
                            + " which "
                            + written
                            + " has not."
                    );
                }

                checkedKinds++;
            }

            Assert.Empty(wrong);
            Assert.True(checkedKinds > 10);
        }
    }
}
