using System;
using System.Collections.Generic;
using System.Globalization;

namespace OpenCAD2D.Mcp
{
    /// <summary>The command line of <c>OpenCAD2D.Mcp.exe</c>.</summary>
    internal sealed class McpOptions
    {
        /// <summary>The usage text, written on standard error for <c>--help</c> and for a command line that is not understood.</summary>
        public const string Usage =
            "OpenCAD2D.Mcp.exe [--file <path.dxf|dwg> | --attach [--pid <n>]] [--read-only] [--allow-scripts]\n"
            + "\n"
            + "  --file <path>      open a drawing file and work on it in this process (no app needed)\n"
            + "  --attach           work on the drawing of a running OpenCAD2D through its Remote API\n"
            + "  --pid <n>          with --attach, the process id of the app (the one activated last by default)\n"
            + "  --read-only        refuse every tool that writes: add_*, move, rotate, scale, erase, set_properties, undo, save, run_command\n"
            + "  --allow-scripts    allow run_command, which types free command, LISP or script text\n"
            + "  --version          write the version and stop\n"
            + "\n"
            + "With neither --file nor --attach no drawing is open: the agent calls open_drawing or new_drawing.";

        /// <summary>Gets or sets the drawing file to open at the start; null for none.</summary>
        public string File { get; set; }

        /// <summary>Gets or sets whether to work on a running app.</summary>
        public bool Attach { get; set; }

        /// <summary>Gets or sets the process id of the app to attach to; 0 for the one activated last.</summary>
        public int ProcessId { get; set; }

        /// <summary>Gets or sets the folder apps announce themselves in, which only a test sets (the real one is the user's).</summary>
        public string RemoteFolder { get; set; }

        /// <summary>Gets or sets whether every tool that writes is refused.</summary>
        public bool ReadOnly { get; set; }

        /// <summary>Gets or sets whether <c>run_command</c> is allowed.</summary>
        public bool AllowScripts { get; set; }

        /// <summary>Gets or sets whether the version was asked for.</summary>
        public bool ShowVersion { get; set; }

        /// <summary>Gets or sets whether the usage was asked for.</summary>
        public bool ShowHelp { get; set; }

        /// <summary>Reads a command line.</summary>
        /// <param name="args">The arguments.</param>
        /// <returns>The options.</returns>
        /// <exception cref="ArgumentException">Thrown for an argument that is not understood, one without its value, or <c>--file</c> with <c>--attach</c>.</exception>
        public static McpOptions Parse(IReadOnlyList<string> args)
        {
            if (args == null)
            {
                throw new ArgumentNullException(nameof(args));
            }

            var options = new McpOptions();

            for (int i = 0; i < args.Count; i++)
            {
                switch (args[i])
                {
                    case "--file":
                        options.File = Value(args, ref i);
                        break;
                    case "--attach":
                        options.Attach = true;
                        break;
                    case "--pid":
                        if (
                            !int.TryParse(
                                Value(args, ref i),
                                NumberStyles.None,
                                CultureInfo.InvariantCulture,
                                out int pid
                            )
                        )
                        {
                            throw new ArgumentException("--pid takes a process id, a whole number.");
                        }

                        options.ProcessId = pid;
                        break;
                    case "--remote-folder":
                        options.RemoteFolder = Value(args, ref i);
                        break;
                    case "--read-only":
                        options.ReadOnly = true;
                        break;
                    case "--allow-scripts":
                        options.AllowScripts = true;
                        break;
                    case "--version":
                        options.ShowVersion = true;
                        break;
                    case "--help":
                    case "-h":
                    case "-?":
                        options.ShowHelp = true;
                        break;
                    default:
                        throw new ArgumentException("The argument " + args[i] + " is not understood.");
                }
            }

            if (options.File != null && options.Attach)
            {
                throw new ArgumentException("--file and --attach are two ways to get a drawing: give one.");
            }

            if ((options.ProcessId != 0 || options.RemoteFolder != null) && !options.Attach)
            {
                throw new ArgumentException("--pid and --remote-folder go with --attach.");
            }

            return options;
        }

        private static string Value(IReadOnlyList<string> args, ref int i)
        {
            if (i + 1 >= args.Count)
            {
                throw new ArgumentException(args[i] + " needs a value.");
            }

            return args[++i];
        }
    }
}
