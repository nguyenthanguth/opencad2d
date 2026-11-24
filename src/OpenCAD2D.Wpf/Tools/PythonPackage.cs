using System;

namespace OpenCAD2D.Wpf
{
    /// <summary>
    /// One package pip would install for a tool, as the question before the install lists it: <c>numpy 2.4.4</c>, asked for by
    /// the tool's <c>requirements.txt</c>, or <c>packaging 24.2</c>, which another package needs. The list comes from
    /// <c>pip install --dry-run --report</c>, so nothing has been downloaded into the tool's environment when it is shown.
    /// </summary>
    internal sealed class PythonPackage
    {
        /// <summary>Initializes a package.</summary>
        /// <param name="name">The package's name, as pip writes it (<c>demo-pkg</c>).</param>
        /// <param name="version">The version pip chose (<c>2.4.4</c>).</param>
        /// <param name="isRequested">Whether the tool's <c>requirements.txt</c> names the package, rather than another package needing it.</param>
        /// <param name="askedAs">The third column of the question: the line of <c>requirements.txt</c> that names the package (<c>shapely&gt;=2.0</c>), or <c>for shapely</c> where another package needs it; null for none.</param>
        /// <exception cref="ArgumentNullException">Thrown when the name or the version is null.</exception>
        public PythonPackage(string name, string version, bool isRequested, string askedAs = null)
        {
            Name = name ?? throw new ArgumentNullException(nameof(name));
            Version = version ?? throw new ArgumentNullException(nameof(version));
            IsRequested = isRequested;
            AskedAs = askedAs ?? string.Empty;
        }

        /// <summary>Gets the package's name.</summary>
        public string Name { get; }

        /// <summary>Gets the version pip chose.</summary>
        public string Version { get; }

        /// <summary>Gets whether the tool's <c>requirements.txt</c> names the package; false where another package needs it.</summary>
        public bool IsRequested { get; }

        /// <summary>Gets how the package was asked for: the line of <c>requirements.txt</c> that names it (<c>shapely&gt;=2.0</c>), <c>for shapely</c> where another package needs it, or <c>needed by another</c> where none of the list says it does; empty where not known.</summary>
        public string AskedAs { get; }

        /// <summary>Gets the package as the question lists it: <c>numpy 2.4.4</c>.</summary>
        /// <returns>The name, a space and the version.</returns>
        public override string ToString() => Name + " " + Version;
    }
}
