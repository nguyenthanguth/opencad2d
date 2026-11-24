using System;
using OpenCAD2D.Plugins;

namespace OpenCAD2D.Wpf
{
    /// <summary>The state badge after a row's name in Load/Unload Applications: what holds a tool back from loading.</summary>
    public enum AppLoadBadge
    {
        /// <summary>No badge: the row is loaded, or not loaded for no reason the window knows.</summary>
        None,

        /// <summary><c>needs packages</c> (amber): the question before downloading the plugin's packages was answered No; ticking the row asks again.</summary>
        NeedsPackages,

        /// <summary><c>install failed</c> (red): the packages could not be restored, and the red line names the first error.</summary>
        InstallFailed,

        /// <summary><c>installing…</c> (blue): pip is installing a Python tool's packages, off the window's thread; the row clears or turns to <see cref="InstallFailed"/> when it ends.</summary>
        Installing,
    }

    /// <summary>
    /// What a plugin load that stopped on the plugin's packages left behind, which Load/Unload Applications shows as a badge
    /// after the row's name (<c>needs packages</c>, <c>install failed</c>) and, for a failure, as its red line.
    /// </summary>
    internal sealed class ToolPackageState
    {
        private const string NotLoaded = " was not loaded: ";

        private ToolPackageState(AppLoadBadge kind, string text)
        {
            Kind = kind;
            Text = text;
        }

        /// <summary>Gets what stopped the load: <see cref="AppLoadBadge.NeedsPackages"/> or <see cref="AppLoadBadge.InstallFailed"/>.</summary>
        internal AppLoadBadge Kind { get; }

        /// <summary>
        /// Gets the first error as the red line says it: <c>nuget.org could not be reached (...). Connect and run PLUGINRELOAD
        /// Stairs.</c>, or <c>Stairs.csproj(7,6): error NU1101: Unable to find package Foo.</c>; empty for
        /// <see cref="AppLoadBadge.NeedsPackages"/>.
        /// </summary>
        internal string Text { get; }

        /// <summary>
        /// Reads what a refused plugin load says: <c>Stairs was not loaded: its packages were not downloaded.</c> is the
        /// question answered No, <c>Stairs was not loaded: nuget.org could not be reached (...)</c> and a build whose first error
        /// is a restore error (<c>error NU...</c>) are a failed install; any other refusal is not about packages.
        /// </summary>
        /// <param name="result">What the plugin manager said of the load.</param>
        /// <returns>The state, or null where the load succeeded or stopped for another reason.</returns>
        internal static ToolPackageState From(CadPluginResult result)
        {
            if (result == null || result.Succeeded)
            {
                return null;
            }

            string[] lines = result.Message.Split('\n');
            int at = lines[0].IndexOf(NotLoaded, StringComparison.Ordinal);

            if (at > 0)
            {
                string reason = lines[0].Substring(at + NotLoaded.Length);

                if (reason == "its packages were not downloaded.")
                {
                    return new ToolPackageState(AppLoadBadge.NeedsPackages, string.Empty);
                }

                if (
                    !lines[0].StartsWith("Plugin ", StringComparison.Ordinal)
                    && reason.Contains(" could not be reached (")
                )
                {
                    return new ToolPackageState(AppLoadBadge.InstallFailed, reason);
                }
            }

            if (
                at > 0
                && lines.Length > 1
                && lines[0].StartsWith("Plugin ", StringComparison.Ordinal)
                && lines[1].Contains(": error NU")
            )
            {
                return new ToolPackageState(AppLoadBadge.InstallFailed, lines[1]);
            }

            return null;
        }
    }
}
