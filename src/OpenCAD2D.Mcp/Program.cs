using System;
using System.Threading;
using System.Threading.Tasks;

namespace OpenCAD2D.Mcp
{
    /// <summary>
    /// <c>OpenCAD2D.Mcp.exe</c>: an MCP server over standard input and output, with which an AI agent (Claude Desktop, Claude Code,
    /// Cursor) opens a drawing file or attaches to a running OpenCAD2D, then reads, draws, edits, checks and renders it. Standard
    /// output carries the protocol and nothing else; what the server has to say goes to standard error.
    /// </summary>
    internal static class Program
    {
        private static async Task<int> Main(string[] args)
        {
            McpOptions options;

            try
            {
                options = McpOptions.Parse(args);
            }
            catch (ArgumentException exception)
            {
                Console.Error.WriteLine(exception.Message);
                Console.Error.WriteLine(McpOptions.Usage);
                return 2;
            }

            if (options.ShowHelp)
            {
                Console.Out.WriteLine(McpOptions.Usage);
                return 0;
            }

            if (options.ShowVersion)
            {
                Console.Out.WriteLine(typeof(Program).Assembly.GetName().Version.ToString(3));
                return 0;
            }

            // The protocol owns the real standard output: a stray line written by anything else would break it.
            Console.SetOut(Console.Error);

            using (var session = new McpSession(options))
            {
                try
                {
                    session.Start();
                }
                catch (Exception exception)
                {
                    Console.Error.WriteLine("OpenCAD2D.Mcp could not open " + options.File + ": " + exception.Message);
                    return 1;
                }

                await McpHost.RunStdioAsync(session, CancellationToken.None).ConfigureAwait(false);
            }

            return 0;
        }
    }
}
