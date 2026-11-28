using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using OpenCAD2D.Remote;

namespace OpenCAD2D.Share
{
    /// <summary>How the copy of a tool on this PC compares with the live version on Share.</summary>
    internal enum ShareCopyState
    {
        /// <summary>Nothing of the tool was installed here from Share: no manifest, or its files are gone.</summary>
        NotOnThisPc,

        /// <summary>The copy's files have the same SHA-256 as the live version's: there is nothing to update.</summary>
        Same,

        /// <summary>The live version on Share is newer than the copy: version 1.2 against a copy of 1.1.</summary>
        Newer,

        /// <summary>The live version on Share is older than the copy: version 1.1 against a copy of 1.2, as when a reviewer took the newer one down.</summary>
        Older,

        /// <summary>The same version number, with other bytes: the copy is not what Share has under that number.</summary>
        Different,

        /// <summary>A file the install wrote has been edited, replaced or deleted since: it no longer has the SHA-256 the manifest kept, so an update would overwrite the user's work.</summary>
        Changed,
    }

    /// <summary>
    /// What a tool's folder keeps of an install from Share, in <c>share.json</c>: the tool's slug, the version number, the name
    /// and SHA-256 of each file as Share gave them (a zip's is the zip's), and the paths written. It is how "On this PC" tells
    /// a copy from the live version by SHA-256, and how an update removes the files an older version had and the new one has not.
    /// </summary>
    internal sealed class ShareManifest
    {
        /// <summary>The name of the manifest in the tool's folder.</summary>
        internal const string FileName = "share.json";

        internal ShareManifest(
            string slug,
            string version,
            IReadOnlyList<KeyValuePair<string, string>> files,
            IReadOnlyList<KeyValuePair<string, string>> installed
        )
        {
            Slug = slug;
            Version = version;
            Files = files;
            Installed = installed;
        }

        /// <summary>Gets the tool's slug.</summary>
        internal string Slug { get; }

        /// <summary>Gets the version number installed.</summary>
        internal string Version { get; }

        /// <summary>Gets the name and SHA-256 of each file Share gave, by name.</summary>
        internal IReadOnlyList<KeyValuePair<string, string>> Files { get; }

        /// <summary>Gets each path written under the tool's folder, with <c>/</c> between folders, in name order, and the SHA-256 of the file as it was written.</summary>
        internal IReadOnlyList<KeyValuePair<string, string>> Installed { get; }

        /// <summary>Reads the manifest of a tool's folder; null where there is none or it is not one this app wrote.</summary>
        internal static ShareManifest TryRead(string folder)
        {
            try
            {
                string path = System.IO.Path.Combine(folder, FileName);

                if (!File.Exists(path) || !(RemoteJson.Parse(File.ReadAllText(path)) is JsonObject root))
                {
                    return null;
                }

                string slug = root.GetString("slug");
                string version = root.GetString("version");

                if (string.IsNullOrEmpty(slug) || string.IsNullOrEmpty(version))
                {
                    return null;
                }

                var files = new List<KeyValuePair<string, string>>();

                foreach (JsonObject file in root.GetArray("files").OfType<JsonObject>())
                {
                    string name = file.GetString("name");
                    string sha = file.GetString("sha256");

                    if (string.IsNullOrEmpty(name) || !ShareTool.IsSha256(sha))
                    {
                        return null;
                    }

                    files.Add(new KeyValuePair<string, string>(name, sha.ToLowerInvariant()));
                }

                var installed = new List<KeyValuePair<string, string>>();

                foreach (JsonObject written in root.GetArray("paths").OfType<JsonObject>())
                {
                    string name = written.GetString("path");
                    string sha = written.GetString("sha256");

                    if (string.IsNullOrEmpty(name) || !ShareTool.IsSha256(sha))
                    {
                        return null;
                    }

                    installed.Add(new KeyValuePair<string, string>(name, sha.ToLowerInvariant()));
                }

                return new ShareManifest(slug, version, files, installed);
            }
            catch (Exception e)
                when (e is FormatException
                    || e is IOException
                    || e is UnauthorizedAccessException
                    || e is ArgumentException
                    || e is NotSupportedException
                )
            {
                return null;
            }
        }

        /// <summary>Gets whether a file of the tool's folder is still what the install wrote: it exists and has the SHA-256 the manifest kept. A file that cannot be read is not.</summary>
        internal static bool StillIs(string folder, string path, string sha256)
        {
            try
            {
                string file = System.IO.Path.Combine(folder, path.Replace('/', System.IO.Path.DirectorySeparatorChar));
                return File.Exists(file)
                    && string.Equals(ShareClient.Sha256OfFile(file), sha256, StringComparison.Ordinal);
            }
            catch (Exception e) when (e is IOException || e is UnauthorizedAccessException)
            {
                return false;
            }
        }

        /// <summary>Writes the manifest into a tool's folder.</summary>
        internal void Write(string folder)
        {
            var files = new JsonArray();

            foreach (KeyValuePair<string, string> file in Files)
            {
                files.Add(new JsonObject().Set("name", file.Key).Set("sha256", file.Value));
            }

            var written = new JsonArray();

            foreach (KeyValuePair<string, string> file in Installed)
            {
                written.Add(new JsonObject().Set("path", file.Key).Set("sha256", file.Value));
            }

            var root = new JsonObject()
                .Set("slug", Slug)
                .Set("version", Version)
                .Set("files", files)
                .Set("paths", written);
            File.WriteAllText(System.IO.Path.Combine(folder, FileName), RemoteJson.Write(root));
        }
    }

    /// <summary>
    /// What is on this PC of a tool of Share, found from the manifest its install left in the tool's folder and compared by
    /// SHA-256 with the live version: <see cref="State"/>, and the version and SHA-256 of the copy. Nothing is asked of the
    /// server.
    /// </summary>
    internal sealed class ShareCopy
    {
        private ShareCopy(ShareCopyState state, string version, string sha256, IReadOnlyList<string> changed = null)
        {
            State = state;
            Version = version;
            Sha256 = sha256;
            ChangedFiles = changed ?? Array.Empty<string>();
        }

        /// <summary>Gets the paths, with <c>/</c> between folders, of the files that are no longer what the install wrote (edited, replaced or deleted); empty unless <see cref="State"/> is <see cref="ShareCopyState.Changed"/>.</summary>
        public IReadOnlyList<string> ChangedFiles { get; }

        /// <summary>Gets how the copy compares with the live version.</summary>
        public ShareCopyState State { get; }

        /// <summary>Gets the version number of the copy; empty where <see cref="State"/> is <see cref="ShareCopyState.NotOnThisPc"/>.</summary>
        public string Version { get; }

        /// <summary>Gets the SHA-256 of the copy's first file, as Share gave it when it was installed; empty where there is no copy.</summary>
        public string Sha256 { get; }

        /// <summary>
        /// Compares the copy in a tool's folder with the live version. The copy is what the manifest of the folder says was
        /// installed; a folder with no manifest, one of another tool, or one whose files have been deleted has no copy. The
        /// files are the same where each name and SHA-256 of the manifest is one of the live version's and the live version
        /// has no more; else the version numbers decide (<c>1.10</c> is later than <c>1.9</c>), and an equal number with
        /// other bytes is <see cref="ShareCopyState.Different"/>.
        /// </summary>
        /// <param name="tool">The tool on Share.</param>
        /// <param name="toolFolder">The folder the tool was or is to be saved in.</param>
        /// <returns>What is on this PC.</returns>
        /// <exception cref="ArgumentNullException">Thrown when the tool is null.</exception>
        /// <exception cref="ArgumentException">Thrown when the folder is empty.</exception>
        public static ShareCopy Check(ShareTool tool, string toolFolder)
        {
            if (tool == null)
            {
                throw new ArgumentNullException(nameof(tool));
            }

            if (string.IsNullOrWhiteSpace(toolFolder))
            {
                throw new ArgumentException("The folder is empty.", nameof(toolFolder));
            }

            ShareManifest manifest = ShareManifest.TryRead(toolFolder);

            if (
                manifest == null
                || manifest.Slug != tool.Slug
                || manifest.Files.Count == 0
                || manifest.Installed.Count == 0
                || !manifest.Installed.Any(p => File.Exists(System.IO.Path.Combine(toolFolder, p.Key)))
            )
            {
                return new ShareCopy(ShareCopyState.NotOnThisPc, string.Empty, string.Empty);
            }

            string sha = manifest.Files[0].Value;
            List<string> changed = manifest
                .Installed.Where(p => !ShareManifest.StillIs(toolFolder, p.Key, p.Value))
                .Select(p => p.Key)
                .ToList();

            if (changed.Count > 0)
            {
                return new ShareCopy(ShareCopyState.Changed, manifest.Version, sha, changed);
            }

            bool same =
                manifest.Files.Count == tool.Files.Count
                && manifest.Files.All(f =>
                    tool.Files.Any(t =>
                        string.Equals(t.Name, f.Key, StringComparison.OrdinalIgnoreCase)
                        && string.Equals(t.Sha256, f.Value, StringComparison.Ordinal)
                    )
                );

            if (same)
            {
                return new ShareCopy(ShareCopyState.Same, manifest.Version, sha);
            }

            int order = CompareVersions(tool.Version, manifest.Version);
            return new ShareCopy(
                order > 0 ? ShareCopyState.Newer
                    : order < 0 ? ShareCopyState.Older
                    : ShareCopyState.Different,
                manifest.Version,
                sha
            );
        }

        /// <summary>
        /// Compares two version numbers part by part as whole numbers, so that <c>1.10</c> comes after <c>1.9</c> and <c>2</c>
        /// equals <c>2.0</c>; a part that is no whole number counts as 0.
        /// </summary>
        internal static int CompareVersions(string a, string b)
        {
            string[] left = (a ?? string.Empty).Split('.');
            string[] right = (b ?? string.Empty).Split('.');

            for (int i = 0; i < Math.Max(left.Length, right.Length); i++)
            {
                long x = i < left.Length ? Part(left[i]) : 0;
                long y = i < right.Length ? Part(right[i]) : 0;

                if (x != y)
                {
                    return x < y ? -1 : 1;
                }
            }

            return 0;
        }

        private static long Part(string text) =>
            long.TryParse(text, NumberStyles.None, CultureInfo.InvariantCulture, out long number) ? number : 0;
    }
}
