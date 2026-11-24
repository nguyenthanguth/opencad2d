using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Text;
using System.Threading;

namespace OpenCAD2D.Wpf
{
    /// <summary>
    /// A program run to its end with its output kept: the Python of a tool asked its version, <c>-m venv</c> making an
    /// environment, <c>-m pip</c> reading what a tool needs and installing it. Nothing is shown and nothing is read from the
    /// keyboard; the program's text is UTF-8.
    /// </summary>
    internal sealed class PythonRun
    {
        private PythonRun() { }

        /// <summary>Gets the exit code of the program; -1 where it was ended for taking longer than it was given.</summary>
        public int ExitCode { get; private set; }

        /// <summary>Gets what the program wrote on its standard output.</summary>
        public string Output { get; private set; }

        /// <summary>Gets what the program wrote on its standard error.</summary>
        public string Error { get; private set; }

        /// <summary>Runs a program and waits for it.</summary>
        /// <param name="program">The full path of the program.</param>
        /// <param name="arguments">Its arguments, each quoted as the C runtime reads it back.</param>
        /// <param name="timeoutMilliseconds">How long it is given before it is ended.</param>
        /// <returns>What it wrote and how it ended.</returns>
        /// <exception cref="ArgumentNullException">Thrown when the program or the arguments are null.</exception>
        /// <exception cref="System.ComponentModel.Win32Exception">Thrown when the program cannot be started.</exception>
        public static PythonRun Start(string program, IEnumerable<string> arguments, int timeoutMilliseconds)
        {
            if (program == null)
            {
                throw new ArgumentNullException(nameof(program));
            }

            if (arguments == null)
            {
                throw new ArgumentNullException(nameof(arguments));
            }

            var start = new ProcessStartInfo(program, string.Join(" ", arguments.Select(CadToolProcess.Quote)))
            {
                UseShellExecute = false,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                RedirectStandardInput = true,
                CreateNoWindow = true,
                StandardOutputEncoding = Encoding.UTF8,
                StandardErrorEncoding = Encoding.UTF8,
            };
            start.EnvironmentVariables["PYTHONUTF8"] = "1";
            start.EnvironmentVariables["PIP_DISABLE_PIP_VERSION_CHECK"] = "1";
            start.EnvironmentVariables["PIP_NO_INPUT"] = "1";
            var output = new StringBuilder();
            var error = new StringBuilder();

            using (Process process = Process.Start(start))
            {
                process.OutputDataReceived += (sender, e) => Append(output, e.Data);
                process.ErrorDataReceived += (sender, e) => Append(error, e.Data);
                process.BeginOutputReadLine();
                process.BeginErrorReadLine();
                process.StandardInput.Close();
                bool ended = process.WaitForExit(timeoutMilliseconds);

                if (!ended)
                {
                    try
                    {
                        process.Kill();
                    }
                    catch (InvalidOperationException) { }
                }

                // The overload with no time waits for the output events to finish too.
                process.WaitForExit();
                return new PythonRun
                {
                    ExitCode = ended ? process.ExitCode : -1,
                    Output = Text(output),
                    Error = Text(error),
                };
            }
        }

        private static void Append(StringBuilder text, string line)
        {
            if (line != null)
            {
                lock (text)
                {
                    text.Append(line).Append('\n');
                }
            }
        }

        private static string Text(StringBuilder text)
        {
            lock (text)
            {
                return text.ToString();
            }
        }
    }
}
