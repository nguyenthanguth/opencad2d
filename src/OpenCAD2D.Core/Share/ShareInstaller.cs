using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

namespace OpenCAD2D.Share
{
    /// <summary>What an install from Share wrote: the tool's folder, the files in it, the version it replaced and the copies it kept of files the user had changed.</summary>
    internal sealed class ShareInstalled
    {
        internal ShareInstalled(
            string folder,
            string version,
            string previousVersion,
            IReadOnlyList<string> files,
            IReadOnlyList<string> backedUp
        )
        {
            Folder = folder;
            Version = version;
            PreviousVersion = previousVersion;
            Files = files;
            BackedUp = backedUp;
        }

        /// <summary>Gets the full path of the tool's folder.</summary>
        public string Folder { get; }

        /// <summary>Gets the version number installed.</summary>
        public string Version { get; }

        /// <summary>Gets the version number of the copy this install replaced; null where the tool was not installed before.</summary>
        public string PreviousVersion { get; }

        /// <summary>Gets the full paths of the files written, a zip's contents unpacked and <c>share.json</c> left out, in ordinal order.</summary>
        public IReadOnlyList<string> Files { get; }

        /// <summary>
        /// Gets the full paths of the copies kept of files that were not what a Share install wrote (edited by the user, or there
        /// before the first install) where the install replaced or removed them: <c>stairs.lsp.local-2026-11-28</c> beside where
        /// <c>stairs.lsp</c> was. Empty where nothing was in the way.
        /// </summary>
        public IReadOnlyList<string> BackedUp { get; }
    }

    /// <summary>
    /// Installs a tool of Share into a folder: where a tool goes in the app's folders (<see cref="ToolFolder"/>), and the install
    /// itself (<see cref="InstallAsync"/>). The install downloads every file of the live version into a staging folder (hidden,
    /// under <c>Share\.share-tmp</c> beside the app's folders, never inside the <c>Lisps</c>, <c>Pythons</c> or <c>Plugins</c> that
    /// the app walks), checks each one's SHA-256 against the one the API gave, unpacks a zip with the path of each entry checked,
    /// builds the whole new tool folder there (a file of the user's that the install would replace or remove is kept beside it
    /// as <c>name.local-yyyy-MM-dd</c>, and any other file of theirs is carried over), and only then swaps it for the old folder:
    /// the old folder is renamed aside, the new one renamed into place and the old one deleted, the old one renamed back if the
    /// second rename fails. A file that does not match, a zip entry that leaves the folder or is not source, a locked file or
    /// a failed write leaves the old version as it was, or no folder if there was none.
    /// </summary>
    internal static class ShareInstaller
    {
        /// <summary>The name of the folder of tools saved without being loaded, and of the staging folder in it, beside the app's folders.</summary>
        internal const string ShareFolder = "Share";

        /// <summary>The name of the hidden staging folder; a zip entry may not have a part that starts with <c>.share-</c>.</summary>
        internal const string StageFolder = ".share-tmp";

        /// <summary>The most a zip may unpack to, in bytes: a tool of source is far smaller.</summary>
        private const long MostUnpacked = 64L * 1024 * 1024;

        // What Share takes is source: AutoLISP and its dialogs, Python, C# and its project, and the text beside them.
        private static readonly string[] SourceExtensions =
        {
            ".lsp",
            ".dcl",
            ".py",
            ".cs",
            ".csproj",
            ".json",
            ".txt",
            ".md",
        };

        // What Windows takes for a device wherever it stands in a name, with any extension: CON.txt is CON.
        private static readonly string[] ReservedNames =
        {
            "CON",
            "PRN",
            "AUX",
            "NUL",
            "COM1",
            "COM2",
            "COM3",
            "COM4",
            "COM5",
            "COM6",
            "COM7",
            "COM8",
            "COM9",
            "LPT1",
            "LPT2",
            "LPT3",
            "LPT4",
            "LPT5",
            "LPT6",
            "LPT7",
            "LPT8",
            "LPT9",
        };

        /// <summary>
        /// Gets the folder a tool is saved in. Where it is to be loaded (<paramref name="autoLoad"/>), its slug under the folder
        /// the app loads that kind from, <c>Lisps</c> for AutoLISP, <c>Pythons</c> for a Python tool and <c>Plugins</c> for C#
        /// source, beside <c>OpenCAD2D.Core.dll</c>: everything in <c>Lisps</c> loads at every start at any depth, a folder of
        /// <c>Pythons</c> that holds a <c>requirements.txt</c> is a tool, and a folder of <c>Plugins</c> that holds a
        /// <c>.csproj</c> is a plugin compiled when it loads. Where it is not, its slug under <c>Share</c> beside them, a folder
        /// the app never loads from.
        /// </summary>
        /// <param name="folders">The app's default folders.</param>
        /// <param name="kind">The kind of tool.</param>
        /// <param name="slug">The tool's slug.</param>
        /// <param name="autoLoad">True for the folder the app loads from; false for <c>Share</c>, where nothing runs.</param>
        /// <returns>The folder, as <c>C:\App\Plugins\stairs</c>; null where the app has no program folder.</returns>
        /// <exception cref="ArgumentNullException">Thrown when the folders are null.</exception>
        /// <exception cref="ArgumentException">Thrown when the slug is not one.</exception>
        public static string ToolFolder(CadSupportFolders folders, ShareKind kind, string slug, bool autoLoad)
        {
            if (folders == null)
            {
                throw new ArgumentNullException(nameof(folders));
            }

            if (!ShareLink.IsSlug(slug))
            {
                throw new ArgumentException(
                    "The slug is not one: lower-case letters, digits and hyphens.",
                    nameof(slug)
                );
            }

            if (folders.ProgramFolder == null)
            {
                return null;
            }

            if (!autoLoad)
            {
                return Path.Combine(folders.ProgramFolder, ShareFolder, slug);
            }

            return Path.Combine(
                folders.FolderOf(
                    kind == ShareKind.Lisp ? CadSupportKind.Lisps
                    : kind == ShareKind.Python ? CadSupportKind.Pythons
                    : CadSupportKind.Plugins
                ),
                slug
            );
        }

        /// <summary>Gets the hidden staging folder beside the app's folders, <c>Share\.share-tmp</c>; null where the app has no program folder.</summary>
        /// <param name="folders">The app's default folders.</param>
        /// <returns>The folder.</returns>
        /// <exception cref="ArgumentNullException">Thrown when the folders are null.</exception>
        public static string StagingRoot(CadSupportFolders folders)
        {
            if (folders == null)
            {
                throw new ArgumentNullException(nameof(folders));
            }

            return folders.ProgramFolder == null ? null : Path.Combine(folders.ProgramFolder, ShareFolder, StageFolder);
        }

        /// <summary>
        /// Downloads the live version of a tool into a folder, as <see cref="ShareInstaller"/> describes: every file checked
        /// against Share's SHA-256 in a staging folder, the new folder built there and swapped for the old one. Whatever was
        /// left in the staging folder by an install that was cut off is cleaned first.
        /// </summary>
        /// <param name="client">What the files are downloaded through.</param>
        /// <param name="tool">The tool, as <see cref="ShareClient.FindAsync"/> found it.</param>
        /// <param name="toolFolder">The folder to install into, made where it does not exist.</param>
        /// <param name="stagingRoot">The folder the staging is made in, which must lie on the volume of <paramref name="toolFolder"/>; null, or another volume, for a hidden <c>.share-tmp</c> beside the tool's folder.</param>
        /// <param name="cancel">Cancels the install; the folder is left as it was.</param>
        /// <returns>What was written.</returns>
        /// <exception cref="ArgumentNullException">Thrown when the client or the tool is null.</exception>
        /// <exception cref="ArgumentException">Thrown when the folder is empty.</exception>
        /// <exception cref="ShareException">Thrown when the tool has no files, a name is unsafe, reserved or not source, a file's SHA-256 is not the one Share recorded, a zip holds a path out of the folder, a file cannot be had or the folder cannot be written; the old version is as it was.</exception>
        /// <exception cref="OperationCanceledException">Thrown when <paramref name="cancel"/> is cancelled.</exception>
        public static async Task<ShareInstalled> InstallAsync(
            ShareClient client,
            ShareTool tool,
            string toolFolder,
            string stagingRoot = null,
            CancellationToken cancel = default
        )
        {
            if (client == null)
            {
                throw new ArgumentNullException(nameof(client));
            }

            if (tool == null)
            {
                throw new ArgumentNullException(nameof(tool));
            }

            if (string.IsNullOrWhiteSpace(toolFolder))
            {
                throw new ArgumentException("The folder is empty.", nameof(toolFolder));
            }

            if (tool.Files.Count == 0)
            {
                throw new ShareException("Share lists no file for " + tool.Name + ".");
            }

            foreach (ShareFile file in tool.Files)
            {
                string unsafeName = Refusal(file.Name, true);

                if (unsafeName != null || file.Name.IndexOfAny(new[] { '/', '\\' }) >= 0)
                {
                    throw new ShareException(
                        unsafeName ?? "Share names a file \"" + file.Name + "\" that has a folder in its name."
                    );
                }
            }

            string folder = Path.GetFullPath(toolFolder).TrimEnd(Path.DirectorySeparatorChar);
            string root = StageOn(folder, stagingRoot);
            string work = Path.Combine(root, Guid.NewGuid().ToString("N"));

            try
            {
                CleanLeftovers(root);
                Directory.CreateDirectory(root);
                Hide(root);
                string downloads = Path.Combine(work, "download");
                string ready = Path.Combine(work, "new");
                Directory.CreateDirectory(downloads);
                Directory.CreateDirectory(ready);
                File.WriteAllText(Path.Combine(work, "target.txt"), folder);
                ShareManifest before = ShareManifest.TryRead(folder);
                var paths = new List<string>();

                foreach (ShareFile file in tool.Files)
                {
                    cancel.ThrowIfCancellationRequested();
                    string got = Path.Combine(downloads, file.Name);
                    string sha = await client.DownloadAsync(file, got, cancel).ConfigureAwait(false);

                    if (!string.Equals(sha, file.Sha256, StringComparison.Ordinal))
                    {
                        throw new ShareException(
                            file.Name
                                + " from Share does not match its SHA-256 (Share recorded "
                                + Short(file.Sha256)
                                + ", the download is "
                                + Short(sha)
                                + "), so it was not installed."
                        );
                    }

                    if (string.Equals(Path.GetExtension(file.Name), ".zip", StringComparison.OrdinalIgnoreCase))
                    {
                        Unpack(got, ready, paths);
                    }
                    else
                    {
                        Add(paths, file.Name);
                        File.Move(got, Path.Combine(ready, file.Name));
                    }
                }

                paths.Sort(StringComparer.Ordinal);
                var written = paths
                    .Select(p => new KeyValuePair<string, string>(p, ShareClient.Sha256OfFile(Under(ready, p))))
                    .ToList();
                var backedUp = new List<string>();

                if (Directory.Exists(folder))
                {
                    CarryOver(folder, ready, before, paths, backedUp);
                }

                new ShareManifest(
                    tool.Slug,
                    tool.Version,
                    tool.Files.Select(f => new KeyValuePair<string, string>(f.Name, f.Sha256)).ToList(),
                    written
                ).Write(ready);

                // The last point a cancel stops the install: from here the swap runs to its end.
                cancel.ThrowIfCancellationRequested();
                Swap(folder, ready, Path.Combine(work, "old"));

                return new ShareInstalled(
                    folder,
                    tool.Version,
                    before?.Version,
                    paths.Select(p => Under(folder, p)).ToList(),
                    backedUp.Select(p => Under(folder, p)).ToList()
                );
            }
            catch (IOException e)
            {
                throw new ShareException(
                    "The tool could not be written to " + folder + " (" + e.Message + "); it was not installed.",
                    e
                );
            }
            catch (UnauthorizedAccessException e)
            {
                throw new ShareException(
                    "The tool could not be written to " + folder + " (" + e.Message + "); it was not installed.",
                    e
                );
            }
            finally
            {
                Settle(work);
            }
        }

        /// <summary>
        /// Cleans a staging folder of what an install that was cut off left: each work folder in it is deleted, except that one
        /// that holds the old version of a tool whose folder is gone (a swap cut off between its two renames) gives it back first.
        /// </summary>
        /// <param name="stagingRoot">The staging folder; one that does not exist is passed over.</param>
        public static void CleanLeftovers(string stagingRoot)
        {
            if (string.IsNullOrEmpty(stagingRoot) || !Directory.Exists(stagingRoot))
            {
                return;
            }

            try
            {
                foreach (string work in Directory.GetDirectories(stagingRoot))
                {
                    Settle(work);
                }
            }
            catch (Exception e) when (e is IOException || e is UnauthorizedAccessException) { }
        }

        // The staging folder for a tool's folder: the one given where it is on the same volume, else a hidden one beside it.
        private static string StageOn(string folder, string stagingRoot)
        {
            if (
                !string.IsNullOrEmpty(stagingRoot)
                && string.Equals(
                    Path.GetPathRoot(Path.GetFullPath(stagingRoot)),
                    Path.GetPathRoot(folder),
                    StringComparison.OrdinalIgnoreCase
                )
            )
            {
                return Path.GetFullPath(stagingRoot);
            }

            return Path.Combine(Path.GetDirectoryName(folder) ?? folder, StageFolder);
        }

        private static void Hide(string folder)
        {
            try
            {
                var info = new DirectoryInfo(folder);
                info.Attributes |= FileAttributes.Hidden;
            }
            catch (Exception e) when (e is IOException || e is UnauthorizedAccessException) { }
        }

        private static string Under(string folder, string path) =>
            Path.Combine(folder, path.Replace('/', Path.DirectorySeparatorChar));

        /// <summary>
        /// Builds the user's side of the new folder: every file of the old folder that the old install did not write as it stands
        /// is kept. A file of the user's own is carried over where it is; one the install wrote and the user changed, where the
        /// new version has a file of the name or has none, is kept as <c>name.local-yyyy-MM-dd</c>. A file that is as the old
        /// install wrote it is let go: it is replaced, or its version had it and this one has not. A file that cannot be read
        /// stops the install, with the old folder as it was.
        /// </summary>
        private static void CarryOver(
            string folder,
            string ready,
            ShareManifest before,
            List<string> newPaths,
            List<string> backedUp
        )
        {
            string day = DateTime.Now.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);
            var written = new HashSet<string>(newPaths, StringComparer.OrdinalIgnoreCase);
            string[] files = Directory.GetFiles(folder, "*", SearchOption.AllDirectories);
            Array.Sort(files, StringComparer.Ordinal);

            // Every name the old folder has is taken, so that a copy kept never lands on a file of the user's.
            var taken = new HashSet<string>(
                files.Select(f => f.Substring(folder.Length + 1).Replace('\\', '/')),
                StringComparer.OrdinalIgnoreCase
            );
            taken.UnionWith(written);
            var carry = new List<KeyValuePair<string, string>>();

            foreach (string file in files)
            {
                string relative = file.Substring(folder.Length + 1).Replace('\\', '/');

                if (string.Equals(relative, ShareManifest.FileName, StringComparison.OrdinalIgnoreCase))
                {
                    continue;
                }

                string sha = ShareClient.Sha256OfFile(file);
                KeyValuePair<string, string> wrote =
                    before == null
                        ? default
                        : before.Installed.FirstOrDefault(i =>
                            string.Equals(i.Key, relative, StringComparison.OrdinalIgnoreCase)
                        );

                if (wrote.Key != null && string.Equals(wrote.Value, sha, StringComparison.Ordinal))
                {
                    continue;
                }

                string target = relative;

                if (wrote.Key != null || written.Contains(relative))
                {
                    target = relative + ".local-" + day;

                    for (int n = 2; taken.Contains(target); n++)
                    {
                        target = relative + ".local-" + day + "-" + n.ToString(CultureInfo.InvariantCulture);
                    }

                    taken.Add(target);
                    backedUp.Add(target);
                }

                carry.Add(new KeyValuePair<string, string>(file, target));
            }

            foreach (KeyValuePair<string, string> copy in carry)
            {
                string to = Under(ready, copy.Value);
                Directory.CreateDirectory(Path.GetDirectoryName(to));
                File.Copy(copy.Key, to, false);
            }
        }

        /// <summary>
        /// Puts the new folder in place of the old one: the old one is renamed to <paramref name="aside"/>, the new one renamed to
        /// its name, and the old one is left to be deleted with the staging. Where the second rename fails the first is undone.
        /// </summary>
        private static void Swap(string folder, string ready, string aside)
        {
            Directory.CreateDirectory(Path.GetDirectoryName(folder));
            bool hadOld = Directory.Exists(folder);

            if (hadOld)
            {
                Directory.Move(folder, aside);
            }

            try
            {
                Directory.Move(ready, folder);
            }
            catch
            {
                if (hadOld)
                {
                    Directory.Move(aside, folder);
                }

                throw;
            }
        }

        /// <summary>
        /// Ends a work folder of the staging: where it holds the old version of a tool and the tool's folder is gone it is renamed
        /// back first, then the work folder is deleted. A work folder that cannot be deleted is left for the next clean.
        /// </summary>
        private static void Settle(string work)
        {
            try
            {
                string old = Path.Combine(work, "old");
                string targetFile = Path.Combine(work, "target.txt");

                if (Directory.Exists(old) && File.Exists(targetFile))
                {
                    string target = File.ReadAllText(targetFile);

                    if (!Directory.Exists(target))
                    {
                        Directory.CreateDirectory(Path.GetDirectoryName(target));
                        Directory.Move(old, target);
                    }
                }

                if (Directory.Exists(work))
                {
                    Directory.Delete(work, true);
                }
            }
            catch (Exception e) when (e is IOException || e is UnauthorizedAccessException) { }
        }

        // The first eight hex digits of a SHA-256 and an ellipsis: enough to tell two apart in a sentence.
        private static string Short(string sha) => sha.Substring(0, 8) + "…";

        private static void Add(List<string> paths, string path)
        {
            if (paths.Contains(path, StringComparer.OrdinalIgnoreCase))
            {
                throw new ShareException("The tool holds " + path + " twice, so it was not installed.");
            }

            paths.Add(path);
        }

        /// <summary>
        /// Why a name is not one of a source file Share may put in a folder; null where it is. A name is unsafe when it is
        /// rooted, names a drive or a stream (<c>:</c>), has a <c>..</c> or <c>.</c> part, an empty part, a character no file
        /// name has, ends in a dot or a space, has a part that starts with <c>.share-</c> (the staging's), or has a part that
        /// Windows takes for a device (CON, PRN, AUX, NUL, COM1 to COM9, LPT1 to LPT9, with any extension). It is refused when
        /// its extension is not source (a .zip is allowed where <paramref name="allowZip"/>, for a file of the tool itself and
        /// not an entry of a zip).
        /// </summary>
        private static string Refusal(string name, bool allowZip)
        {
            string shown = "\"" + name + "\"";

            if (string.IsNullOrWhiteSpace(name))
            {
                return "Share names a file with no name, so the tool was not installed.";
            }

            string[] parts = name.Replace('\\', '/').Split('/');
            char[] bad = Path.GetInvalidFileNameChars();

            if (
                name.StartsWith("/", StringComparison.Ordinal)
                || name.StartsWith("\\", StringComparison.Ordinal)
                || parts.Any(p =>
                    p.Length == 0
                    || p == "."
                    || p == ".."
                    || p.IndexOfAny(bad) >= 0
                    || p.EndsWith(".", StringComparison.Ordinal)
                    || p.EndsWith(" ", StringComparison.Ordinal)
                    || p.StartsWith(".share-", StringComparison.OrdinalIgnoreCase)
                )
            )
            {
                return shown + " is a path out of the tool's folder, so the tool was not installed.";
            }

            if (parts.Any(IsReserved))
            {
                return shown + " is a name Windows keeps for a device, so the tool was not installed.";
            }

            if (string.Equals(name, ShareManifest.FileName, StringComparison.OrdinalIgnoreCase))
            {
                return shown + " is the name of the file that records the install, so the tool was not installed.";
            }

            string extension = Path.GetExtension(parts[parts.Length - 1]);

            if (
                !SourceExtensions.Contains(extension, StringComparer.OrdinalIgnoreCase)
                && !(allowZip && string.Equals(extension, ".zip", StringComparison.OrdinalIgnoreCase))
            )
            {
                return shown
                    + " is not source (a tool of Share holds .lsp, .dcl, .py, .cs, .csproj, .json, .txt and .md files), so the tool was not installed.";
            }

            return null;
        }

        // A part of a path that Windows opens as a device: the name before its first dot, whatever follows.
        private static bool IsReserved(string part)
        {
            int dot = part.IndexOf('.');
            string stem = (dot < 0 ? part : part.Substring(0, dot)).TrimEnd(' ');
            return ReservedNames.Contains(stem, StringComparer.OrdinalIgnoreCase);
        }

        /// <summary>
        /// Unpacks a zip into a folder, entry by entry: every entry's path is checked first, so that one that leaves the folder
        /// or is not source stops the whole install before a byte is written; then the bytes are written, the total capped.
        /// </summary>
        private static void Unpack(string zipPath, string ready, List<string> paths)
        {
            try
            {
                using (ZipArchive zip = ZipFile.OpenRead(zipPath))
                {
                    var entries = new List<KeyValuePair<string, ZipArchiveEntry>>();

                    foreach (ZipArchiveEntry entry in zip.Entries)
                    {
                        if (
                            entry.FullName.EndsWith("/", StringComparison.Ordinal)
                            || entry.FullName.EndsWith("\\", StringComparison.Ordinal)
                        )
                        {
                            continue;
                        }

                        string refusal = Refusal(entry.FullName, false);

                        if (refusal != null)
                        {
                            throw new ShareException(refusal);
                        }

                        string relative = entry.FullName.Replace('\\', '/');
                        string target = Path.GetFullPath(
                            Path.Combine(ready, relative.Replace('/', Path.DirectorySeparatorChar))
                        );

                        if (
                            !target.StartsWith(
                                Path.GetFullPath(ready) + Path.DirectorySeparatorChar,
                                StringComparison.OrdinalIgnoreCase
                            )
                        )
                        {
                            throw new ShareException(
                                "\""
                                    + entry.FullName
                                    + "\" is a path out of the tool's folder, so the tool was not installed."
                            );
                        }

                        Add(paths, relative);
                        entries.Add(new KeyValuePair<string, ZipArchiveEntry>(relative, entry));
                    }

                    long total = 0;

                    foreach (KeyValuePair<string, ZipArchiveEntry> entry in entries)
                    {
                        string target = Path.Combine(ready, entry.Key.Replace('/', Path.DirectorySeparatorChar));
                        Directory.CreateDirectory(Path.GetDirectoryName(target));

                        using (Stream from = entry.Value.Open())
                        using (var to = new FileStream(target, FileMode.Create, FileAccess.Write, FileShare.None))
                        {
                            var buffer = new byte[81920];
                            int read;

                            while ((read = from.Read(buffer, 0, buffer.Length)) > 0)
                            {
                                total += read;

                                if (total > MostUnpacked)
                                {
                                    throw new ShareException(
                                        "The zip unpacks to more than 64 MB, which is no tool of source, so it was not installed."
                                    );
                                }

                                to.Write(buffer, 0, read);
                            }
                        }
                    }
                }
            }
            catch (InvalidDataException e)
            {
                throw new ShareException(
                    "The zip from Share cannot be read (" + e.Message + "), so the tool was not installed.",
                    e
                );
            }
        }
    }
}
