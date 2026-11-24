#if NET8_0_OR_GREATER
using System;
using System.Collections.Generic;
using System.Globalization;

namespace OpenCAD2D.Wpf.Plugins
{
    /// <summary>
    /// One NuGet package a plugin's .csproj needs, as the question before a download lists it
    /// (<see cref="CadPluginAssemblyLoader.ConfirmDownload"/>): <c>Newtonsoft.Json 13.0.3</c>, asked for by the plugin itself,
    /// or <c>Microsoft.Data.Sqlite.Core 9.0.0</c>, which another package needs. A package already in the cache folder of the
    /// app is listed too, and needs no download.
    /// </summary>
    public sealed class CadPackage
    {
        /// <summary>Initializes a package.</summary>
        /// <param name="id">The package's id.</param>
        /// <param name="version">The version the restore chose, as NuGet writes it (<c>13.0.3</c>, <c>1.0.0-beta.1</c>).</param>
        /// <param name="isDirect">Whether the plugin's .csproj names the package, rather than another package needing it.</param>
        /// <param name="isCached">Whether the package is in the cache folder already, so that nothing is downloaded for it.</param>
        public CadPackage(string id, string version, bool isDirect, bool isCached)
        {
            Id = id ?? throw new ArgumentNullException(nameof(id));
            Version = version ?? throw new ArgumentNullException(nameof(version));
            IsDirect = isDirect;
            IsCached = isCached;
        }

        /// <summary>Gets the package's id.</summary>
        public string Id { get; }

        /// <summary>Gets the version the restore chose.</summary>
        public string Version { get; }

        /// <summary>Gets whether the plugin's .csproj names the package; false where another package needs it.</summary>
        public bool IsDirect { get; }

        /// <summary>Gets whether the package is in the cache folder already, so that nothing is downloaded for it.</summary>
        public bool IsCached { get; }

        /// <summary>Gets the package as the question lists it: <c>Newtonsoft.Json 13.0.3</c>.</summary>
        /// <returns>The id, a space and the version.</returns>
        public override string ToString() => Id + " " + Version;
    }

    /// <summary>
    /// The question before the packages of a plugin are downloaded, in the form of the command line (FILEDIA 0): a plugin
    /// Stairs that needs 3 packages gives <c>Download 3 packages for Stairs from nuget.org? [Yes/No] &lt;No&gt;:</c>. The window
    /// asks the same of <see cref="CadPluginAssemblyLoader.ConfirmDownload"/> with a dialog that lists the packages.
    /// </summary>
    public static class CadPackageQuestion
    {
        /// <summary>
        /// Gets the prompt of the command line for a plugin whose packages are listed: the packages not cached are the ones
        /// downloaded, and are the count. Two packages to download for Stairs give
        /// <c>Download 2 packages for Stairs from nuget.org? [Yes/No] &lt;No&gt;:</c>, one gives <c>Download 1 package ...</c>.
        /// </summary>
        /// <param name="plugin">The plugin's name.</param>
        /// <param name="packages">The packages the plugin needs, cached ones too.</param>
        /// <returns>The prompt.</returns>
        public static string Prompt(string plugin, IReadOnlyList<CadPackage> packages)
        {
            if (plugin == null)
            {
                throw new ArgumentNullException(nameof(plugin));
            }

            if (packages == null)
            {
                throw new ArgumentNullException(nameof(packages));
            }

            int count = 0;

            foreach (CadPackage package in packages)
            {
                if (!package.IsCached)
                {
                    count++;
                }
            }

            return "Download "
                + count.ToString(CultureInfo.InvariantCulture)
                + (count == 1 ? " package" : " packages")
                + " for "
                + plugin
                + " from nuget.org? [Yes/No] <No>:";
        }

        /// <summary>
        /// Gets what the window's question says for a plugin whose packages are listed, as the PackageQuestion board draws it:
        /// the title <c>Packages for Stairs</c>, the sentence <c>The plugin Stairs (D:\Tools\Stairs\Stairs.csproj) uses 2
        /// packages that are not on this computer yet.</c> (the packages not cached are the count), a table with the packages
        /// the .csproj names first, in bold, then those they pull in, each group in ordinal order of the id, each marked
        /// <c>download</c> or <c>in the cache</c>, and the buttons <c>Don't load</c> and <c>Download and load</c>.
        /// </summary>
        /// <param name="plugin">The plugin's name.</param>
        /// <param name="project">The path of the plugin's .csproj; null leaves it out of the sentence.</param>
        /// <param name="packages">The packages the plugin needs, cached ones too.</param>
        /// <returns>The question for <see cref="PackageQuestionWindow"/>.</returns>
        public static PackageQuestionViewModel Question(
            string plugin,
            string project,
            IReadOnlyList<CadPackage> packages
        )
        {
            if (plugin == null)
            {
                throw new ArgumentNullException(nameof(plugin));
            }

            if (packages == null)
            {
                throw new ArgumentNullException(nameof(packages));
            }

            var ordered = new List<CadPackage>(packages);
            ordered.Sort(
                (a, b) =>
                    a.IsDirect != b.IsDirect ? (a.IsDirect ? -1 : 1)
                    : string.CompareOrdinal(a.Id, b.Id) != 0 ? string.CompareOrdinal(a.Id, b.Id)
                    : string.CompareOrdinal(a.Version, b.Version)
            );
            var rows = new List<PackageQuestionRow>();
            int download = 0;

            foreach (CadPackage package in ordered)
            {
                download += package.IsCached ? 0 : 1;
                rows.Add(
                    new PackageQuestionRow(
                        package.Id,
                        package.Version,
                        package.IsDirect,
                        package.IsCached ? "in the cache" : "download",
                        !package.IsCached
                    )
                );
            }

            return new PackageQuestionViewModel(
                "Packages for " + plugin,
                "The plugin "
                    + plugin
                    + (project != null ? " (" + project + ")" : string.Empty)
                    + " uses "
                    + download.ToString(CultureInfo.InvariantCulture)
                    + (download == 1 ? " package that is" : " packages that are")
                    + " not on this computer yet.",
                "State",
                rows,
                "Loading a plugin runs its code, and its packages’ code, with your rights. The packages are kept in %LocalAppData%\\OpenCAD2D\\NuGet and shared by every plugin.",
                "From nuget.org",
                "Don’t load",
                "Download and load"
            );
        }

        /// <summary>
        /// Reads the answer typed at <see cref="Prompt"/>: Y and Yes (in any case) are yes; N, No and nothing typed (Enter,
        /// the default) are no; anything else is not an answer.
        /// </summary>
        /// <param name="text">What was typed.</param>
        /// <returns>True for yes, false for no, null where it is neither, so that the question is asked again.</returns>
        public static bool? Answer(string text)
        {
            string answer = (text ?? string.Empty).Trim();

            if (
                answer.Length == 0
                || string.Equals(answer, "N", StringComparison.OrdinalIgnoreCase)
                || string.Equals(answer, "No", StringComparison.OrdinalIgnoreCase)
            )
            {
                return false;
            }

            return
                string.Equals(answer, "Y", StringComparison.OrdinalIgnoreCase)
                || string.Equals(answer, "Yes", StringComparison.OrdinalIgnoreCase)
                ? true
                : (bool?)null;
        }
    }
}
#endif
