using System;

namespace OpenCAD2D.Wpf
{
    /// <summary>
    /// What the last readying of a Python tool's environment left behind, which Load/Unload Applications shows on the tool's row:
    /// <see cref="AppLoadBadge.NeedsPackages"/> where the question of pip was answered No, <see cref="AppLoadBadge.Installing"/>
    /// while pip installs the packages, <see cref="AppLoadBadge.InstallFailed"/> where they could not be installed, with the lines
    /// of the failure. A change of the packages that failed but left the tool on its old environment has the lines and no badge:
    /// the tool still runs.
    /// </summary>
    internal sealed class PythonToolState
    {
        /// <summary>The state of a tool nothing is wrong with.</summary>
        internal static readonly PythonToolState Ready = new PythonToolState(AppLoadBadge.None, null);

        private PythonToolState(AppLoadBadge badge, string lines)
        {
            Badge = badge;
            Lines = lines;
        }

        /// <summary>Gets the badge after the tool's name: none where the tool is ready or still runs on its old environment.</summary>
        internal AppLoadBadge Badge { get; }

        /// <summary>Gets every line of the failure, as the command line has them; null where nothing failed.</summary>
        internal string Lines { get; }

        /// <summary>Gets the first line of the failure, the red line of the window; empty where nothing failed.</summary>
        internal string FirstLine => Lines == null ? string.Empty : Lines.Replace("\r", string.Empty).Split('\n')[0];

        /// <summary>The question of pip was answered No (or Esc).</summary>
        internal static PythonToolState NeedsPackages() => new PythonToolState(AppLoadBadge.NeedsPackages, null);

        /// <summary>Pip is installing the tool's packages.</summary>
        internal static PythonToolState Installing() => new PythonToolState(AppLoadBadge.Installing, null);

        /// <summary>The tool's environment could not be made.</summary>
        /// <param name="lines">Pip's lines and the app's after them.</param>
        internal static PythonToolState InstallFailed(string lines) =>
            new PythonToolState(AppLoadBadge.InstallFailed, lines);

        /// <summary>A change of the packages failed, and the tool runs on the environment it had.</summary>
        /// <param name="lines">Pip's lines and the app's after them.</param>
        internal static PythonToolState Kept(string lines) => new PythonToolState(AppLoadBadge.None, lines);
    }

    /// <summary>A tool's state changed: which tool, and what it is now.</summary>
    internal sealed class PythonToolStateEventArgs : EventArgs
    {
        internal PythonToolStateEventArgs(string name, string folder, PythonToolState state)
        {
            Name = name;
            Folder = folder;
            State = state;
        }

        /// <summary>Gets the tool's name.</summary>
        internal string Name { get; }

        /// <summary>Gets the tool's folder.</summary>
        internal string Folder { get; }

        /// <summary>Gets what the tool's state is now.</summary>
        internal PythonToolState State { get; }
    }
}
