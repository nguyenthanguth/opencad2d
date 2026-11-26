using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using GeometryHelper.Dxf;
using OpenCAD2D.Remote;

namespace OpenCAD2D.Mcp
{
    /// <summary>
    /// What the output and debug tools need of a drawing that the backend interface does not give: the drawing as a document of this
    /// process (the open file itself, or a copy of the app's drawing as it stands now), the app behind it, and the rules of the files
    /// they write. A backend is reached through its own <c>Document</c>, so that the interface the other tools share stays as it is.
    /// </summary>
    internal static class McpKit
    {
        /// <summary>Gets whether the drawing is one of a running app.</summary>
        public static bool IsApp(IDrawingBackend drawing) => drawing is RemoteBackend;

        /// <summary>
        /// Gets the drawing as a document of this process, to be read and never changed: the open file's own document, or for a drawing
        /// of the app a copy made from the app's database as it stands now, changes not saved included. The app is not touched.
        /// </summary>
        /// <exception cref="InvalidOperationException">Thrown for a backend this server does not know.</exception>
        public static CadDocument Snapshot(IDrawingBackend drawing)
        {
            switch (drawing)
            {
                case LocalBackend local:
                    return local.Document;
                case RemoteBackend remote:
                    return Prepared(CadDocument.From(remote.Document.GetDatabase(), remote.Name));
                default:
                    throw new InvalidOperationException("The drawing is of a kind this server cannot read.");
            }
        }

        /// <summary>Sets a document read from a file or a database up as the app sets one up, so that the boxes of its text are the app's.</summary>
        public static CadDocument Prepared(CadDocument document)
        {
            CadRender.Prepare(document);
            return document;
        }

        /// <summary>Gets the app's drawing behind a drawing of an attached server; the other tools refuse a file.</summary>
        /// <param name="drawing">The drawing.</param>
        /// <param name="tool">The tool, for the refusal.</param>
        /// <param name="instead">What to use for a file.</param>
        /// <exception cref="ArgumentException">Thrown for a drawing file worked on in this process.</exception>
        public static CadRemoteDocument App(IDrawingBackend drawing, string tool, string instead) =>
            (drawing as RemoteBackend)?.Document
            ?? throw new ArgumentException(
                tool + " looks at a running app, and this server works on a drawing file: " + instead
            );

        /// <summary>The folder the debug files are written to, <c>%TEMP%\OpenCAD2D\mcp-debug</c>.</summary>
        public static string DebugFolder() => Path.Combine(Path.GetTempPath(), "OpenCAD2D", "mcp-debug");

        /// <summary>
        /// Gives the full path a tool may write: the extension is the one asked for (added where there is none, refused where it is
        /// another), a folder that is not there is made, and a file that is there is refused unless <paramref name="overwrite"/>, and the open drawing's own file always.
        /// </summary>
        /// <param name="path">The path the agent gave.</param>
        /// <param name="extension">The extension with its dot, as <c>.pdf</c>.</param>
        /// <param name="overwrite">Whether a file already there may be replaced.</param>
        /// <param name="own">The file of the open drawing, which is never written; it is named in the refusal.</param>
        /// <returns>The full path.</returns>
        /// <exception cref="ArgumentException">Thrown for no path, another extension, a file that is there and not to be replaced, or the open drawing's own file.</exception>
        public static string Target(string path, string extension, bool overwrite, string own)
        {
            if (string.IsNullOrWhiteSpace(path))
            {
                throw new ArgumentException("Give the path of the " + extension + " file to write.");
            }

            string given = path.Trim();
            string current = Path.GetExtension(given);

            if (current.Length == 0)
            {
                given += extension;
            }
            else if (!string.Equals(current, extension, StringComparison.OrdinalIgnoreCase))
            {
                throw new ArgumentException("The file is " + extension + ", not " + current + ": " + given + ".");
            }

            string full = Path.GetFullPath(given);

            if (own != null && string.Equals(Path.GetFullPath(own), full, StringComparison.OrdinalIgnoreCase))
            {
                throw new ArgumentException(
                    "That is the open drawing's own file, "
                        + full
                        + ": no tool but save writes it, whatever overwrite says. Give another path."
                );
            }

            if (File.Exists(full) && !overwrite)
            {
                throw new ArgumentException(
                    "The file " + full + " is there already: pass overwrite true to replace it, or give another path."
                );
            }

            Directory.CreateDirectory(Path.GetDirectoryName(full));
            return full;
        }

        /// <summary>The union of a table's boxes as <c>[minX, minY, maxX, maxY]</c>; null for none.</summary>
        public static double[] Union(BoxRows rows)
        {
            if (rows.Handles.Length == 0)
            {
                return null;
            }

            double[] box = { double.MaxValue, double.MaxValue, double.MinValue, double.MinValue };

            for (int i = 0; i < rows.Handles.Length; i++)
            {
                box[0] = Math.Min(box[0], rows.Boxes[4 * i]);
                box[1] = Math.Min(box[1], rows.Boxes[4 * i + 1]);
                box[2] = Math.Max(box[2], rows.Boxes[4 * i + 2]);
                box[3] = Math.Max(box[3], rows.Boxes[4 * i + 3]);
            }

            return box;
        }

        /// <summary>The version code a name of a release stands for: <c>2018</c>, <c>R2018</c> and <c>AC1032</c> are AutoCAD 2018's.</summary>
        /// <param name="version">The name.</param>
        /// <returns>The code, as <c>AC1032</c>; null for none given.</returns>
        /// <exception cref="ArgumentException">Thrown for a name that is none of the versions.</exception>
        public static string VersionCode(string version)
        {
            if (string.IsNullOrWhiteSpace(version))
            {
                return null;
            }

            string name = version.Trim().ToUpperInvariant();

            if (name.StartsWith("AC10", StringComparison.Ordinal) && Codes.ContainsValue(name))
            {
                return name;
            }

            name = name.StartsWith("R", StringComparison.Ordinal) ? name.Substring(1) : name;

            if (Codes.TryGetValue(name, out string code))
            {
                return code;
            }

            throw new ArgumentException(
                "The version is one of "
                    + string.Join(", ", Codes.Keys)
                    + " (or its code, as AC1032): "
                    + version
                    + " is not."
            );
        }

        private static readonly SortedDictionary<string, string> Codes = new SortedDictionary<string, string>(
            StringComparer.Ordinal
        )
        {
            ["12"] = "AC1009",
            ["2000"] = "AC1015",
            ["2004"] = "AC1018",
            ["2007"] = "AC1021",
            ["2010"] = "AC1024",
            ["2013"] = "AC1027",
            ["2018"] = "AC1032",
        };
    }
}
