using System;
using Xunit;

namespace OpenCAD2D.Mcp.UnitTest
{
    /// <summary>The command line of the exe.</summary>
    public class McpOptionsTests
    {
        [Fact]
        public void No_arguments_start_a_server_with_no_drawing_that_writes_but_runs_no_scripts()
        {
            McpOptions options = McpOptions.Parse(Array.Empty<string>());

            Assert.Null(options.File);
            Assert.False(options.Attach);
            Assert.False(options.ReadOnly);
            Assert.False(options.AllowScripts);
        }

        [Fact]
        public void Every_flag_is_read()
        {
            McpOptions file = McpOptions.Parse(
                new[] { "--file", @"C:\plans\a b.dwg", "--read-only", "--allow-scripts" }
            );
            McpOptions attach = McpOptions.Parse(new[] { "--attach", "--pid", "4120", "--remote-folder", @"C:\x" });

            Assert.Equal(@"C:\plans\a b.dwg", file.File);
            Assert.True(file.ReadOnly);
            Assert.True(file.AllowScripts);
            Assert.True(attach.Attach);
            Assert.Equal(4120, attach.ProcessId);
            Assert.Equal(@"C:\x", attach.RemoteFolder);
        }

        [Theory]
        [InlineData("--file")]
        [InlineData("--pid")]
        [InlineData("--nonsense")]
        [InlineData("--file", "a.dxf", "--attach")]
        [InlineData("--pid", "12")]
        [InlineData("--attach", "--pid", "twelve")]
        public void A_command_line_that_is_not_understood_is_refused_with_a_reason(params string[] args)
        {
            ArgumentException refused = Assert.Throws<ArgumentException>(() => McpOptions.Parse(args));

            Assert.False(string.IsNullOrWhiteSpace(refused.Message));
        }
    }
}
