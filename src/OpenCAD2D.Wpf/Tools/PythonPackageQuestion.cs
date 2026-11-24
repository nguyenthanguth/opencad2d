using System;
using System.Collections.Generic;

namespace OpenCAD2D.Wpf
{
    /// <summary>
    /// The question before pip installs a Python tool's packages, as the PackageQuestion board draws it for pip: <c>Packages for
    /// Python tool Survey</c>, the sentence that names the tool and its <c>requirements.txt</c>, a table (Package, Version, Asked
    /// as) with the packages the tool names first, in bold, each with the line that names it, then the packages those need, each
    /// with <c>for</c> and the package that needs it, the sentence about the code that runs, and <c>With pip, from the Python
    /// package index</c> with the buttons <c>Don't install</c> and <c>Install</c>. It is shown by <see cref="PackageQuestionWindow"/>,
    /// which the NuGet question of a C# plugin uses too.
    /// </summary>
    internal static class PythonPackageQuestion
    {
        /// <summary>Gets what the window says for a tool whose packages pip has listed.</summary>
        /// <param name="tool">The tool's name.</param>
        /// <param name="requirements">The path of the tool's <c>requirements.txt</c>.</param>
        /// <param name="packages">What pip would install, in the order to show: the tool's own packages first, then the rest, each group in ordinal order of the name.</param>
        /// <returns>The question for <see cref="PackageQuestionWindow"/>.</returns>
        /// <exception cref="ArgumentNullException">Thrown when an argument is null.</exception>
        internal static PackageQuestionViewModel Question(
            string tool,
            string requirements,
            IReadOnlyList<PythonPackage> packages
        )
        {
            if (tool == null)
            {
                throw new ArgumentNullException(nameof(tool));
            }

            if (requirements == null)
            {
                throw new ArgumentNullException(nameof(requirements));
            }

            if (packages == null)
            {
                throw new ArgumentNullException(nameof(packages));
            }

            var ordered = new List<PythonPackage>(packages);
            ordered.Sort(
                (a, b) =>
                {
                    int order = a.IsRequested == b.IsRequested ? 0 : (a.IsRequested ? -1 : 1);
                    order = order != 0 ? order : string.Compare(a.Name, b.Name, StringComparison.OrdinalIgnoreCase);
                    order = order != 0 ? order : string.CompareOrdinal(a.Name, b.Name);
                    return order != 0 ? order : string.CompareOrdinal(a.Version, b.Version);
                }
            );
            var rows = new List<PackageQuestionRow>();

            foreach (PythonPackage package in ordered)
            {
                rows.Add(
                    new PackageQuestionRow(package.Name, package.Version, package.IsRequested, package.AskedAs, false)
                );
            }

            return new PackageQuestionViewModel(
                "Packages for Python tool " + tool,
                "The tool "
                    + tool
                    + " ("
                    + requirements
                    + ") needs these packages in its own environment. pip worked out the list; nothing is installed yet.",
                "Asked as",
                rows,
                "Installing runs the packages’ code with your rights. The tool gets an environment of its own in "
                    + "%LocalAppData%\\OpenCAD2D\\python\\envs, so its versions never change another tool’s.",
                "With pip, from the Python package index",
                "Don’t install",
                "Install"
            );
        }
    }
}
