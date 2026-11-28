using System;
using System.Collections.Generic;
using System.Linq;
using OpenCAD2D.Remote;

namespace OpenCAD2D.Share
{
    /// <summary>The kinds of tool Share holds, as the API names them: AutoLISP, a Python tool and C# source.</summary>
    internal enum ShareKind
    {
        /// <summary>An AutoLISP routine, saved in a folder of its own under <c>Lisps</c>, which loads every <c>.lsp</c> at any depth.</summary>
        Lisp,

        /// <summary>A Python tool, saved as a folder of its own under <c>Pythons</c> (it holds a <c>requirements.txt</c>).</summary>
        Python,

        /// <summary>A C# plugin given as source, saved as a folder of its own under <c>Plugins</c> (it holds a <c>.csproj</c>), compiled when it loads.</summary>
        CSharp,
    }

    /// <summary>One file of a tool's live version on Share: its name, its size in bytes, its SHA-256 and where to download it from.</summary>
    internal sealed class ShareFile
    {
        internal ShareFile(string name, long size, string sha256, Uri url)
        {
            Name = name;
            Size = size;
            Sha256 = sha256;
            Url = url;
        }

        /// <summary>Gets the file's name, as <c>Stairs.cs</c>.</summary>
        public string Name { get; }

        /// <summary>Gets the file's size in bytes, as Share recorded it.</summary>
        public long Size { get; }

        /// <summary>Gets the file's SHA-256 as 64 lower-case hex digits, as Share recorded it when the file was uploaded.</summary>
        public string Sha256 { get; }

        /// <summary>Gets the address the file is downloaded from, on Share's own host.</summary>
        public Uri Url { get; }
    }

    /// <summary>
    /// A tool as <c>GET /api/tools/{slug}</c> gives it: the live version of a tool that Share's reviewers approved, with its
    /// name, summary, author, commands, files and cover picture. Every tool on Share is under the MIT licence.
    /// </summary>
    internal sealed class ShareTool
    {
        private ShareTool(
            string slug,
            string name,
            string summary,
            ShareKind kind,
            string author,
            string version,
            string approved,
            IReadOnlyList<string> commands,
            IReadOnlyList<ShareFile> files,
            Uri pictureUrl,
            string pictureSha256
        )
        {
            Slug = slug;
            Name = name;
            Summary = summary;
            Kind = kind;
            Author = author;
            Version = version;
            Approved = approved;
            Commands = commands;
            Files = files;
            PictureUrl = pictureUrl;
            PictureSha256 = pictureSha256;
        }

        /// <summary>Gets the tool's address name, as <c>stairs</c> in <c>https://opencad2d.com/share/stairs</c>: lower-case letters, digits and hyphens.</summary>
        public string Slug { get; }

        /// <summary>Gets the tool's name, as <c>Stairs</c>.</summary>
        public string Name { get; }

        /// <summary>Gets the reviewers' one-sentence summary of what the tool does.</summary>
        public string Summary { get; }

        /// <summary>Gets the kind of tool.</summary>
        public ShareKind Kind { get; }

        /// <summary>Gets the author's name.</summary>
        public string Author { get; }

        /// <summary>Gets the live version's number, as <c>1.2</c>.</summary>
        public string Version { get; }

        /// <summary>Gets when the reviewers approved the live version, as the API wrote it (a UTC time such as <c>2026-11-20T09:30:00Z</c>); empty where it gave none.</summary>
        public string Approved { get; }

        /// <summary>Gets the commands the tool adds, in the order the file defines them; empty where Share read none.</summary>
        public IReadOnlyList<string> Commands { get; }

        /// <summary>Gets the files of the live version, by name.</summary>
        public IReadOnlyList<ShareFile> Files { get; }

        /// <summary>Gets the address of the cover picture; null where the tool has none.</summary>
        public Uri PictureUrl { get; }

        /// <summary>Gets the SHA-256 of the cover picture; null where the tool has none.</summary>
        public string PictureSha256 { get; }

        /// <summary>
        /// Reads the answer of <c>GET /api/tools/{slug}</c>. Its slug must be a slug (<see cref="ShareLink.IsSlug"/>) and the one asked for, and every address in it must lie on the host of <paramref name="site"/> (or its <c>www.</c>), since the slug names the folder the tool is saved in and the addresses are what is downloaded.
        /// </summary>
        /// <param name="json">The answer's text.</param>
        /// <param name="site">The address Share is read at, as <c>https://opencad2d.com/</c>.</param>
        /// <param name="expectedSlug">The slug the link gave, which the answer must carry; null for any valid slug.</param>
        /// <returns>The tool.</returns>
        /// <exception cref="ArgumentNullException">Thrown when the text or the site is null.</exception>
        /// <exception cref="ShareException">Thrown when the text is no tool this app reads: not JSON, a field missing, a slug that is not a slug or is not the one asked for, or an address off Share's host.</exception>
        public static ShareTool Parse(string json, Uri site, string expectedSlug = null)
        {
            if (json == null)
            {
                throw new ArgumentNullException(nameof(json));
            }
            if (site == null)
            {
                throw new ArgumentNullException(nameof(site));
            }
            JsonObject root;

            try
            {
                root = RemoteJson.Parse(json) as JsonObject;
            }
            catch (FormatException)
            {
                root = null;
            }

            string slug = root?.GetString("slug");
            string name = root?.GetString("name");
            JsonObject version = root?.GetObject("version");
            string number = version?.GetString("number");

            if (string.IsNullOrWhiteSpace(slug) || string.IsNullOrWhiteSpace(name) || string.IsNullOrWhiteSpace(number))
            {
                throw new ShareException("Share's answer is not a tool this app can read.");
            }
            if (
                !ShareLink.IsSlug(slug)
                || (expectedSlug != null && !string.Equals(slug, expectedSlug, StringComparison.Ordinal))
            )
            {
                throw new ShareException(
                    "Share's answer is not the tool that was asked for, so nothing was read from it."
                );
            }

            ShareKind kind;

            switch (root.GetString("kind"))
            {
                case "lisp":
                    kind = ShareKind.Lisp;
                    break;
                case "python":
                    kind = ShareKind.Python;
                    break;
                case "csharp":
                    kind = ShareKind.CSharp;
                    break;
                default:
                    throw new ShareException("Share's answer gives a kind of tool this app does not know.");
            }

            var files = new List<ShareFile>();

            foreach (JsonObject file in root.GetArray("files").OfType<JsonObject>())
            {
                string fileName = file.GetString("name");
                string sha = file.GetString("sha256");
                Uri url = AddressOn(site, file.GetString("url"));

                if (
                    string.IsNullOrWhiteSpace(fileName)
                    || !IsSha256(sha)
                    || url == null
                    || file.GetLong("size", -1) < 0
                )
                {
                    throw new ShareException("Share's answer lists a file this app cannot read.");
                }

                files.Add(new ShareFile(fileName, file.GetLong("size"), sha.ToLowerInvariant(), url));
            }

            JsonObject picture = root.GetObject("picture");
            Uri pictureUrl = picture == null ? null : AddressOn(site, picture.GetString("url"));
            string pictureSha = picture?.GetString("sha256");

            if (pictureUrl == null || !IsSha256(pictureSha))
            {
                pictureUrl = null;
                pictureSha = null;
            }

            return new ShareTool(
                slug,
                name,
                root.GetString("summary") ?? string.Empty,
                kind,
                root.GetString("author") ?? string.Empty,
                number,
                version.GetString("approved") ?? string.Empty,
                root.GetArray("commands").OfType<string>().ToList(),
                files
                    .OrderBy(f => f.Name, StringComparer.OrdinalIgnoreCase)
                    .ThenBy(f => f.Name, StringComparer.Ordinal)
                    .ToList(),
                pictureUrl,
                pictureSha?.ToLowerInvariant()
            );
        }

        /// <summary>Gets whether a text is a SHA-256: 64 hex digits.</summary>
        internal static bool IsSha256(string text)
        {
            if (text == null || text.Length != 64)
            {
                return false;
            }

            foreach (char c in text)
            {
                if (!((c >= '0' && c <= '9') || (c >= 'a' && c <= 'f') || (c >= 'A' && c <= 'F')))
                {
                    return false;
                }
            }

            return true;
        }

        // An address the API gave, if it is absolute, https and on the host the site is read at; else null.
        private static Uri AddressOn(Uri site, string text)
        {
            return
                Uri.TryCreate(text, UriKind.Absolute, out Uri address)
                && address.Scheme == site.Scheme
                && (
                    string.Equals(address.Host, site.Host, StringComparison.OrdinalIgnoreCase)
                    || string.Equals(address.Host, "www." + site.Host, StringComparison.OrdinalIgnoreCase)
                )
                ? address
                : null;
        }
    }
}
