using System;
using System.Collections.Generic;
using System.Globalization;
using System.Text.RegularExpressions;

namespace OpenCAD2D.Wpf
{
    /// <summary>
    /// The lines of a tool's <c>requirements.txt</c>: the packages it names and nothing else. A tool names packages only, so an
    /// option line (<c>-r other.txt</c>, <c>--index-url ...</c>, <c>-e .</c>) is refused. A line that asks for a version or
    /// higher and nothing more, <c>numpy&gt;=1.26</c>, is installed as that version, <c>numpy==1.26</c>: it is quick, and the
    /// same on every computer. Where pip finds no <c>1.26</c> to install for the Python in use, the line is taken as written and
    /// pip picks (see <see cref="PythonEnvironments"/>).
    /// </summary>
    internal sealed class PythonRequirement
    {
        // name, optional [extras], >=, one version and nothing else but an optional environment marker.
        private static readonly Regex LowerBound = new Regex(
            @"^(?<name>[A-Za-z0-9][A-Za-z0-9._-]*)\s*(?<extras>\[[^\]]*\])?\s*>=\s*(?<version>[^\s,;<>=!~]+)\s*(?<marker>;.*)?$",
            RegexOptions.CultureInvariant
        );

        private PythonRequirement(string written, string pinned)
        {
            Written = written;
            Pinned = pinned;
            Text = pinned ?? written;
        }

        /// <summary>Gets the line as the file has it, with its comment and the space around it taken off.</summary>
        public string Written { get; }

        /// <summary>Gets the line as <c>name==version</c> where it was a lower bound and nothing more; null for any other line.</summary>
        public string Pinned { get; }

        /// <summary>Gets the line pip is given: <see cref="Pinned"/> where there is one, else <see cref="Written"/>. It goes back to <see cref="Written"/> where the pin cannot be installed.</summary>
        public string Text { get; private set; }

        /// <summary>Gets whether the line is given as a pinned version now.</summary>
        public bool IsPinned => Pinned != null && Text == Pinned;

        /// <summary>Gives pip the line as written, where the pin could not be installed.</summary>
        public void Unpin() => Text = Written;

        /// <summary>
        /// Reads the packages of a requirements file: a blank line and a comment (<c>#</c> at the start or after a space) are
        /// skipped, an option line is refused, every other line is a requirement for pip.
        /// </summary>
        /// <param name="text">The file's text.</param>
        /// <param name="error">The line to show where an option was found (<c>requirements.txt line 3: options are not taken</c>); null where all is well.</param>
        /// <returns>The requirements in the order of the file; empty where there are none or an error was found.</returns>
        /// <exception cref="ArgumentNullException">Thrown when the text is null.</exception>
        public static IReadOnlyList<PythonRequirement> Read(string text, out string error)
        {
            if (text == null)
            {
                throw new ArgumentNullException(nameof(text));
            }

            error = null;
            var requirements = new List<PythonRequirement>();
            string[] lines = text.Replace("\r", string.Empty).Split('\n');

            for (int i = 0; i < lines.Length; i++)
            {
                string line = Regex.Replace(lines[i], @"(^|\s)#.*$", string.Empty).Trim();

                if (line.Length == 0)
                {
                    continue;
                }

                if (line[0] == '-')
                {
                    error =
                        "requirements.txt line "
                        + (i + 1).ToString(CultureInfo.InvariantCulture)
                        + ": options are not taken";
                    return new PythonRequirement[0];
                }

                Match bound = LowerBound.Match(line);
                string pinned = bound.Success
                    ? bound.Groups["name"].Value
                        + bound.Groups["extras"].Value
                        + "=="
                        + bound.Groups["version"].Value
                        + (bound.Groups["marker"].Success ? " " + bound.Groups["marker"].Value : string.Empty)
                    : null;
                requirements.Add(new PythonRequirement(line, pinned));
            }

            return requirements;
        }
    }
}
