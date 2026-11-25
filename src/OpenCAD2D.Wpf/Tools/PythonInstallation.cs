using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Globalization;
using System.IO;
using System.Text.RegularExpressions;
using GeometryHelper;
using Microsoft.Win32;

namespace OpenCAD2D.Wpf
{
    /// <summary>
    /// The Python the tools' environments are made from, found before anything is made. It is the one named in Global settings;
    /// failing that the newest 64-bit CPython 3.11 to 3.15 registered under PEP 514 (HKCU before HKLM at the same version). A
    /// tool's process needs only its <c>python.exe</c>, so nothing but that file is looked at. A setting that does not work is a
    /// problem to show, never silently replaced by another Python.
    /// </summary>
    internal sealed class PythonInstallation
    {
        private const int OldestMinor = 11;
        private const int NewestMinor = 15;
        private static readonly Regex Minor = new Regex(
            @"^3\.(\d+)",
            RegexOptions.CultureInvariant | RegexOptions.IgnoreCase
        );

        private readonly List<string> _passedOver = new List<string>();

        private PythonInstallation() { }

        /// <summary>Gets the full path of the <c>python.exe</c>; null where no Python was found.</summary>
        public string Executable { get; private set; }

        /// <summary>Gets the version, <c>3.14.4</c> as the interpreter or the registry gives it, else <c>3.14</c> from the registry's tag; null where no Python was found.</summary>
        public string Version { get; private set; }

        /// <summary>Gets where it came from: <c>Global settings</c> or <c>Registry (PEP 514)</c>; null where no Python was found.</summary>
        public string Source { get; private set; }

        /// <summary>Gets what the app shows when no Python can be used, or null when one can.</summary>
        public string Problem { get; private set; }

        /// <summary>Gets each candidate that was left out and why, in the order they were looked at: <c>3.13: D:\Python313\python.exe is missing</c>; <c>3.10 (HKLM): no InstallPath</c> where both hives register the tag.</summary>
        public IReadOnlyList<string> PassedOver => _passedOver;

        /// <summary>Gets whether a Python can be used: there is no <see cref="Problem"/>.</summary>
        public bool IsUsable => Problem == null;

        /// <summary>Gets the tag of the Python's wheels and of its folder of the shared base: <c>cp314</c> for Python 3.14; null where no Python was found.</summary>
        public string Tag => Version == null ? null : "cp3" + MinorOf(Version).ToString(CultureInfo.InvariantCulture);

        /// <summary>Finds the Python to use, as the class describes.</summary>
        /// <param name="configured">The <c>python.exe</c> (or its folder) set in Global settings; empty or null for the automatic order.</param>
        /// <returns>The installation, with a <see cref="Problem"/> where none can be used.</returns>
        public static PythonInstallation Find(string configured) => Find(configured, ReadRegistry());

        /// <summary>Finds the Python from the given setting and registry entries, which a test feeds.</summary>
        internal static PythonInstallation Find(string configured, IReadOnlyList<PythonRegistration> registered)
        {
            var found = new PythonInstallation();
            string setting = (configured ?? string.Empty).Trim().Trim('"').Trim();

            if (setting.Length == 0)
            {
                return found.FromRegistry(registered ?? new PythonRegistration[0]);
            }

            found.Source = "Global settings";
            string exe = Directory.Exists(setting) ? Path.Combine(setting, "python.exe") : setting;

            if (!File.Exists(exe))
            {
                return found.Fail("The Python set in Global settings (" + setting + ") was not found.");
            }

            string reason = Ask(exe, out string version);

            if (reason != null)
            {
                return found.Fail(reason.Replace("{path}", exe));
            }

            found.Executable = Path.GetFullPath(exe);
            found.Version = version;
            return found;
        }

        /// <inheritdoc/>
        public override string ToString() => IsUsable ? "Python " + Version + " (" + Executable + ")" : Problem;

        private PythonInstallation Fail(string problem)
        {
            Problem = problem;
            Executable = null;
            Version = null;
            return this;
        }

        private static int MinorOf(string version)
        {
            Match match = Minor.Match(version ?? string.Empty);
            return match.Success ? int.Parse(match.Groups[1].Value, CultureInfo.InvariantCulture) : 0;
        }

        /// <summary>Runs the interpreter once to ask its version and its width; the reason it cannot be used (with {path} for its name), or null with the version.</summary>
        private static string Ask(string exe, out string version)
        {
            version = null;
            PythonRun run;

            try
            {
                run = PythonRun.Start(
                    exe,
                    new[]
                    {
                        "-I",
                        "-c",
                        "import sys; print(sys.version.split()[0], 64 if sys.maxsize > 2**32 else 32)",
                    },
                    60000
                );
            }
            catch (Win32Exception)
            {
                return "{path} did not run: it is not a Python program.";
            }

            string[] parts = run.Output.Trim().Split(' ');

            if (run.ExitCode != 0 || parts.Length != 2 || MinorOf(parts[0]) == 0)
            {
                return "{path} did not run as Python.";
            }

            int minor = MinorOf(parts[0]);

            if (minor < OldestMinor || minor > NewestMinor)
            {
                return "{path} is Python 3."
                    + minor.ToString(CultureInfo.InvariantCulture)
                    + ": OpenCAD2D needs 3.11 to 3.15.";
            }

            if (parts[1] != "64")
            {
                return "{path} is a 32-bit Python: OpenCAD2D needs a 64-bit one.";
            }

            version = parts[0];
            return null;
        }

        private PythonInstallation FromRegistry(IReadOnlyList<PythonRegistration> registered)
        {
            Source = "Registry (PEP 514)";
            var usable = new List<Candidate>();

            foreach (PythonRegistration entry in registered)
            {
                string reason = Candidate.Judge(entry, out Candidate candidate);

                if (reason != null)
                {
                    _passedOver.Add(Name(entry, registered) + ": " + reason);
                }
                else
                {
                    usable.Add(candidate);
                }
            }

            Candidate chosen = null;

            foreach (Candidate candidate in usable)
            {
                if (
                    chosen == null
                    || candidate.Minor > chosen.Minor
                    || (
                        candidate.Minor == chosen.Minor && candidate.Entry.Hive == "HKCU" && chosen.Entry.Hive != "HKCU"
                    )
                )
                {
                    chosen = candidate;
                }
            }

            foreach (Candidate candidate in usable)
            {
                if (!ReferenceEquals(candidate, chosen))
                {
                    _passedOver.Add(
                        Name(candidate.Entry, registered)
                            + ": "
                            + Name(chosen.Entry, registered)
                            + " was chosen instead"
                    );
                }
            }

            if (chosen == null)
            {
                Source = null;
                return Fail(
                    "No Python 3.11 to 3.15 (64-bit) was found. Install one from python.org, or set its python.exe in Global settings > Python."
                );
            }

            Executable = Path.GetFullPath(chosen.Executable);
            Version = chosen.Entry.Version ?? "3." + chosen.Minor.ToString(CultureInfo.InvariantCulture);
            Problem = null;
            return this;
        }

        /// <summary>
        /// How a line of <see cref="PassedOver"/> names an entry: its tag, <c>3.10</c>, and the hive after it, <c>3.10 (HKLM)</c>,
        /// where the same tag is registered in both hives and the tag alone would name two entries.
        /// </summary>
        private static string Name(PythonRegistration entry, IReadOnlyList<PythonRegistration> registered)
        {
            foreach (PythonRegistration other in registered)
            {
                if (
                    !ReferenceEquals(other, entry)
                    && string.Equals(other.Tag, entry.Tag, StringComparison.OrdinalIgnoreCase)
                    && !string.Equals(other.Hive, entry.Hive, StringComparison.Ordinal)
                )
                {
                    return entry.Tag + " (" + entry.Hive + ")";
                }
            }

            return entry.Tag;
        }

        /// <summary>A registered Python that passed every check, with the numbers the choice needs.</summary>
        private sealed class Candidate
        {
            internal PythonRegistration Entry;
            internal string Executable;
            internal int Minor;

            /// <summary>The reason an entry cannot be used, or null with the candidate it makes.</summary>
            internal static string Judge(PythonRegistration entry, out Candidate candidate)
            {
                candidate = null;

                if (string.IsNullOrEmpty(entry.InstallPath) && string.IsNullOrEmpty(entry.ExecutablePath))
                {
                    return "no InstallPath";
                }

                string exe = !string.IsNullOrEmpty(entry.ExecutablePath)
                    ? entry.ExecutablePath
                    : Path.Combine(entry.InstallPath, "python.exe");

                if (!File.Exists(exe))
                {
                    return exe + " is missing";
                }

                bool is32 =
                    string.Equals(entry.SysArchitecture, "32bit", StringComparison.OrdinalIgnoreCase)
                    || (entry.SysArchitecture == null && entry.Tag.EndsWith("-32", StringComparison.Ordinal));

                if (is32)
                {
                    return exe + " is a 32-bit Python";
                }

                int minor = MinorOf(entry.Version ?? entry.Tag);

                if (minor < OldestMinor || minor > NewestMinor)
                {
                    return minor == 0
                        ? "its version is not 3.x"
                        : "Python 3." + minor.ToString(CultureInfo.InvariantCulture) + " is outside 3.11 to 3.15";
                }

                candidate = new Candidate
                {
                    Entry = entry,
                    Executable = exe,
                    Minor = minor,
                };
                return null;
            }
        }

        /// <summary>Reads <c>Software\Python\PythonCore\&lt;tag&gt;</c> of HKCU, then HKLM, the tags of each in ordinal order.</summary>
        internal static IReadOnlyList<PythonRegistration> ReadRegistry()
        {
            var entries = new List<PythonRegistration>();

            foreach (RegistryHive hive in new[] { RegistryHive.CurrentUser, RegistryHive.LocalMachine })
            {
                try
                {
                    using (RegistryKey root = RegistryKey.OpenBaseKey(hive, RegistryView.Registry64))
                    using (RegistryKey core = root.OpenSubKey(@"Software\Python\PythonCore"))
                    {
                        if (core == null)
                        {
                            continue;
                        }

                        string[] tags = core.GetSubKeyNames();
                        Array.Sort(tags, StringComparer.Ordinal);

                        foreach (string tag in tags)
                        {
                            using (RegistryKey key = core.OpenSubKey(tag))
                            using (RegistryKey install = key?.OpenSubKey("InstallPath"))
                            {
                                entries.Add(
                                    new PythonRegistration
                                    {
                                        Hive = hive == RegistryHive.CurrentUser ? "HKCU" : "HKLM",
                                        Tag = tag,
                                        InstallPath = install?.GetValue(null) as string,
                                        ExecutablePath = install?.GetValue("ExecutablePath") as string,
                                        SysArchitecture = key?.GetValue("SysArchitecture") as string,
                                        Version = key?.GetValue("Version") as string,
                                    }
                                );
                            }
                        }
                    }
                }
                catch (Exception exception)
                    when (exception is System.Security.SecurityException
                        || exception is UnauthorizedAccessException
                        || exception is IOException
                    )
                {
                    GeometryHelperLog.Debug("PythonInstallation: the registry could not be read.", exception);
                }
            }

            return entries;
        }
    }

    /// <summary>One <c>Software\Python\PythonCore\&lt;tag&gt;</c> key of the registry (PEP 514), as the finder reads it; a test makes them.</summary>
    internal sealed class PythonRegistration
    {
        /// <summary>Gets or sets HKCU or HKLM.</summary>
        internal string Hive { get; set; }

        /// <summary>Gets or sets the key's name: <c>3.14</c>.</summary>
        internal string Tag { get; set; }

        /// <summary>Gets or sets the default value of <c>InstallPath</c>; null where there is none.</summary>
        internal string InstallPath { get; set; }

        /// <summary>Gets or sets the <c>ExecutablePath</c> value of <c>InstallPath</c>; null where there is none.</summary>
        internal string ExecutablePath { get; set; }

        /// <summary>Gets or sets the <c>SysArchitecture</c> value: <c>64bit</c>; null where there is none.</summary>
        internal string SysArchitecture { get; set; }

        /// <summary>Gets or sets the <c>Version</c> value: <c>3.14.4</c>; null where there is none.</summary>
        internal string Version { get; set; }
    }
}
