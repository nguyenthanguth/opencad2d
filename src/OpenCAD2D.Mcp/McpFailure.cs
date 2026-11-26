using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;

namespace OpenCAD2D.Mcp
{
    /// <summary>A tool call the server could not do: when, which tool, what it said, and for a fault of the server's own the head of its stack.</summary>
    internal sealed class McpFailure
    {
        public McpFailure(DateTime time, string tool, string message, bool refusal, IReadOnlyList<string> stack)
        {
            Time = time;
            Tool = tool;
            Message = message;
            IsRefusal = refusal;
            Stack = stack;
        }

        public DateTime Time { get; }

        /// <summary>Gets the name of the tool; null for a call that opens or makes a drawing.</summary>
        public string Tool { get; }

        public string Message { get; }

        /// <summary>Gets whether it was a refusal the agent can act on (a bad argument, a locked layer) and no fault.</summary>
        public bool IsRefusal { get; }

        /// <summary>Gets the first lines of the stack; empty for a refusal.</summary>
        public IReadOnlyList<string> Stack { get; }
    }

    /// <summary>The last <see cref="Kept"/> failures of a server, in memory.</summary>
    internal sealed class McpFailures
    {
        /// <summary>How many failures are kept.</summary>
        public const int Kept = 50;

        /// <summary>How many lines of a stack are kept.</summary>
        private const int StackLines = 6;

        private readonly List<McpFailure> _items = new List<McpFailure>();

        public void Add(string tool, Exception exception, bool refusal)
        {
            IReadOnlyList<string> stack = refusal
                ? Array.Empty<string>()
                : (exception.StackTrace ?? string.Empty)
                    .Replace("\r", string.Empty)
                    .Split('\n')
                    .Select(l => l.Trim())
                    .Where(l => l.Length > 0)
                    .Take(StackLines)
                    .ToList();
            string message = exception.GetType().Name + ": " + exception.Message;

            lock (_items)
            {
                _items.Add(new McpFailure(DateTime.Now, tool, refusal ? exception.Message : message, refusal, stack));

                if (_items.Count > Kept)
                {
                    _items.RemoveAt(0);
                }
            }
        }

        /// <summary>The newest failures, the newest first.</summary>
        public IReadOnlyList<McpFailure> Newest(int count)
        {
            lock (_items)
            {
                return Enumerable.Reverse(_items).Take(count).ToList();
            }
        }

        /// <summary>A time as the crash log writes it.</summary>
        public static string Stamp(DateTime time) =>
            time.ToString("yyyy-MM-dd HH:mm:ss.fff zzz", CultureInfo.InvariantCulture);
    }
}
