using System;
using System.Globalization;
using System.IO;
using System.Net;
using System.Net.Http;
using System.Security.Cryptography;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

namespace OpenCAD2D.Share
{
    /// <summary>
    /// Reads Share's API (<c>GET /api/tools/{slug}</c>, and the files and pictures it points at from <c>/api/files/...</c>) over
    /// an <see cref="HttpClient"/> the caller gives, so that a test serves the answers itself. It asks only when one of its
    /// methods is called: nothing here runs on a timer or at start, and it keeps nothing between calls. Each request has a time
    /// limit for the whole of it, the body included (<see cref="Timeout"/>), and a size limit: the tool's answer 1 MB, a picture
    /// 5 MB, a file the size the API gave and at most 64 MB. A redirect is an error, since the API answers where it is asked; the
    /// client given should not follow them either (<c>AllowAutoRedirect = false</c>). Every failure is a
    /// <see cref="ShareException"/> whose message is the sentence to show; a request cancelled by the caller throws
    /// <see cref="OperationCanceledException"/>.
    /// </summary>
    internal sealed class ShareClient
    {
        /// <summary>The most a tool's answer may be, in bytes.</summary>
        internal const long MostJson = 1024 * 1024;

        /// <summary>The most a cover picture may be, in bytes.</summary>
        internal const long MostPicture = 5L * 1024 * 1024;

        /// <summary>The most a file may be, in bytes, whatever size the API gave.</summary>
        internal const long MostFile = 64L * 1024 * 1024;

        private static readonly string Agent = "OpenCAD2D/" + AgentVersion();
        private readonly HttpClient _http;

        /// <summary>Makes the client.</summary>
        /// <param name="http">The client every request goes through; the caller owns it.</param>
        /// <param name="site">The address Share is read at; <see cref="ShareLink.Site"/> (<c>https://opencad2d.com/</c>) where null.</param>
        /// <param name="timeout">How long one request may take, body included; 60 seconds where null.</param>
        /// <exception cref="ArgumentNullException">Thrown when the client is null.</exception>
        /// <exception cref="ArgumentOutOfRangeException">Thrown when the timeout is not above zero.</exception>
        public ShareClient(HttpClient http, Uri site = null, TimeSpan? timeout = null)
        {
            _http = http ?? throw new ArgumentNullException(nameof(http));
            Site = site ?? ShareLink.Site;
            Timeout = timeout ?? TimeSpan.FromSeconds(60);

            if (Timeout <= TimeSpan.Zero)
            {
                throw new ArgumentOutOfRangeException(nameof(timeout), "The timeout must be above zero.");
            }
        }

        /// <summary>Gets the address Share is read at.</summary>
        public Uri Site { get; }

        /// <summary>Gets how long one request may take, from sending it to the last byte of its body.</summary>
        public TimeSpan Timeout { get; }

        /// <summary>
        /// Finds the tool a pasted link points at: one request, <c>GET /api/tools/{slug}</c>. A link that is no tool's asks nothing.
        /// </summary>
        /// <param name="link">The pasted link, as <c>https://opencad2d.com/share/stairs</c>.</param>
        /// <param name="cancel">Cancels the request.</param>
        /// <returns>The tool's live version, carrying the slug of the link.</returns>
        /// <exception cref="ShareException">Thrown for a link that is no tool's, a tool Share does not have (a 404), a server that cannot be reached, answers an error or a redirect, or is too slow, and an answer that is too big or this app cannot read.</exception>
        /// <exception cref="OperationCanceledException">Thrown when <paramref name="cancel"/> is cancelled.</exception>
        public async Task<ShareTool> FindAsync(string link, CancellationToken cancel = default)
        {
            if (!ShareLink.TryGetSlug(link, Site, out string slug))
            {
                throw new ShareException(
                    "That is not the link of a tool on Share. Paste the address of its page, such as "
                        + new Uri(Site, "share/stairs").AbsoluteUri
                        + "."
                );
            }

            byte[] answer = await ReadAsync(
                    new Uri(Site, "api/tools/" + slug),
                    MostJson,
                    "Share's answer",
                    "Share has no tool called \"" + slug + "\".",
                    cancel
                )
                .ConfigureAwait(false);
            return ShareTool.Parse(new UTF8Encoding(false).GetString(answer), Site, slug);
        }

        /// <summary>
        /// Downloads a tool's cover picture (5 MB at most) and checks its SHA-256 against the one the API gave. A picture is a
        /// nicety: any failure, a missing picture, a server that does not answer or bytes that are not the recorded ones, gives
        /// null and no exception.
        /// </summary>
        /// <param name="tool">The tool.</param>
        /// <param name="cancel">Cancels the request.</param>
        /// <returns>The picture's bytes; null where the tool has none or it could not be had whole.</returns>
        /// <exception cref="ArgumentNullException">Thrown when the tool is null.</exception>
        /// <exception cref="OperationCanceledException">Thrown when <paramref name="cancel"/> is cancelled.</exception>
        public async Task<byte[]> GetPictureAsync(ShareTool tool, CancellationToken cancel = default)
        {
            if (tool == null)
            {
                throw new ArgumentNullException(nameof(tool));
            }

            if (tool.PictureUrl == null)
            {
                return null;
            }

            try
            {
                byte[] bytes = await ReadAsync(tool.PictureUrl, MostPicture, "The picture", null, cancel)
                    .ConfigureAwait(false);
                return string.Equals(Sha256Of(bytes), tool.PictureSha256, StringComparison.Ordinal) ? bytes : null;
            }
            catch (ShareException)
            {
                return null;
            }
        }

        /// <summary>
        /// Downloads one file of a tool into <paramref name="path"/> (overwriting it) and gives the SHA-256 of what was written,
        /// to compare with <see cref="ShareFile.Sha256"/>; it does not compare, so that the caller decides what a difference costs.
        /// More bytes than the size the API gave, or than 64 MB, are refused as soon as they arrive.
        /// </summary>
        /// <param name="file">The file.</param>
        /// <param name="path">Where to write it; its folder must exist.</param>
        /// <param name="cancel">Cancels the request.</param>
        /// <returns>The SHA-256 of the bytes written, as 64 lower-case hex digits.</returns>
        /// <exception cref="ArgumentNullException">Thrown when the file is null.</exception>
        /// <exception cref="ArgumentException">Thrown when the path is empty.</exception>
        /// <exception cref="ShareException">Thrown when the download breaks off or is too slow, the server answers an error or a redirect, or the file is bigger than it should be.</exception>
        /// <exception cref="IOException">Thrown when the file cannot be written: a fault of the disk, not of the network.</exception>
        /// <exception cref="OperationCanceledException">Thrown when <paramref name="cancel"/> is cancelled.</exception>
        public async Task<string> DownloadAsync(ShareFile file, string path, CancellationToken cancel = default)
        {
            if (file == null)
            {
                throw new ArgumentNullException(nameof(file));
            }

            if (string.IsNullOrWhiteSpace(path))
            {
                throw new ArgumentException("The path is empty.", nameof(path));
            }

            if (file.Size > MostFile)
            {
                throw new ShareException(
                    file.Name + " is larger than 64 MB, which is no tool of source, so it was not installed."
                );
            }

            using (CancellationTokenSource limit = CancellationTokenSource.CreateLinkedTokenSource(cancel))
            {
                limit.CancelAfter(Timeout);

                try
                {
                    using (HttpResponseMessage response = await SendAsync(file.Url, limit.Token).ConfigureAwait(false))
                    {
                        Check(response);

                        if (response.Content.Headers.ContentLength > file.Size)
                        {
                            throw TooBig(file.Name);
                        }

                        using (SHA256 hash = SHA256.Create())
                        using (Stream source = await response.Content.ReadAsStreamAsync().ConfigureAwait(false))
                        using (var target = new FileStream(path, FileMode.Create, FileAccess.Write, FileShare.None))
                        {
                            var buffer = new byte[81920];
                            long total = 0;

                            while (true)
                            {
                                int read;

                                try
                                {
                                    read = await source
                                        .ReadAsync(buffer, 0, buffer.Length, limit.Token)
                                        .ConfigureAwait(false);
                                }
                                catch (Exception e) when (IsNetworkFault(e))
                                {
                                    throw BrokenOff(file.Name, e);
                                }

                                if (read == 0)
                                {
                                    break;
                                }

                                total += read;

                                if (total > file.Size)
                                {
                                    throw TooBig(file.Name);
                                }

                                hash.TransformBlock(buffer, 0, read, null, 0);
                                target.Write(buffer, 0, read);
                            }

                            hash.TransformFinalBlock(buffer, 0, 0);
                            return Hex(hash.Hash);
                        }
                    }
                }
                catch (OperationCanceledException e) when (!cancel.IsCancellationRequested)
                {
                    throw TimedOut(e);
                }
            }
        }

        /// <summary>The SHA-256 of some bytes, as 64 lower-case hex digits.</summary>
        internal static string Sha256Of(byte[] bytes)
        {
            using (SHA256 hash = SHA256.Create())
            {
                return Hex(hash.ComputeHash(bytes));
            }
        }

        /// <summary>The SHA-256 of a file, as 64 lower-case hex digits.</summary>
        internal static string Sha256OfFile(string path)
        {
            using (SHA256 hash = SHA256.Create())
            using (FileStream stream = File.OpenRead(path))
            {
                return Hex(hash.ComputeHash(stream));
            }
        }

        private static string Hex(byte[] hash)
        {
            var text = new StringBuilder(hash.Length * 2);

            foreach (byte b in hash)
            {
                text.Append(b.ToString("x2", CultureInfo.InvariantCulture));
            }

            return text.ToString();
        }

        // One GET read whole under a limit of bytes and of time; a 404 says notFound where that is given.
        private async Task<byte[]> ReadAsync(
            Uri address,
            long most,
            string what,
            string notFound,
            CancellationToken cancel
        )
        {
            using (CancellationTokenSource limit = CancellationTokenSource.CreateLinkedTokenSource(cancel))
            {
                limit.CancelAfter(Timeout);

                try
                {
                    using (HttpResponseMessage response = await SendAsync(address, limit.Token).ConfigureAwait(false))
                    {
                        if (notFound != null && response.StatusCode == HttpStatusCode.NotFound)
                        {
                            throw new ShareException(notFound);
                        }

                        Check(response);

                        if (response.Content.Headers.ContentLength > most)
                        {
                            throw TooBig(what);
                        }

                        using (Stream source = await response.Content.ReadAsStreamAsync().ConfigureAwait(false))
                        using (var all = new MemoryStream())
                        {
                            var buffer = new byte[81920];

                            while (true)
                            {
                                int read;

                                try
                                {
                                    read = await source
                                        .ReadAsync(buffer, 0, buffer.Length, limit.Token)
                                        .ConfigureAwait(false);
                                }
                                catch (Exception e) when (IsNetworkFault(e))
                                {
                                    throw BrokenOff(what, e);
                                }

                                if (read == 0)
                                {
                                    return all.ToArray();
                                }

                                if (all.Length + read > most)
                                {
                                    throw TooBig(what);
                                }

                                all.Write(buffer, 0, read);
                            }
                        }
                    }
                }
                catch (OperationCanceledException e) when (!cancel.IsCancellationRequested)
                {
                    throw TimedOut(e);
                }
            }
        }

        // One GET, as the app is known by it; a failure of the network is a sentence.
        private async Task<HttpResponseMessage> SendAsync(Uri address, CancellationToken cancel)
        {
            using (var request = new HttpRequestMessage(HttpMethod.Get, address))
            {
                request.Headers.UserAgent.ParseAdd(Agent);

                try
                {
                    return await _http
                        .SendAsync(request, HttpCompletionOption.ResponseHeadersRead, cancel)
                        .ConfigureAwait(false);
                }
                catch (HttpRequestException e)
                {
                    throw new ShareException(
                        "Share could not be reached (" + e.Message + "). Check the connection and try again.",
                        e
                    );
                }
            }
        }

        private static void Check(HttpResponseMessage response)
        {
            int status = (int)response.StatusCode;

            if (status >= 300 && status < 400)
            {
                throw new ShareException(
                    "Share answered "
                        + status
                        + " "
                        + response.ReasonPhrase
                        + ", a redirect, which this app does not follow."
                );
            }

            if (!response.IsSuccessStatusCode)
            {
                throw new ShareException(
                    "Share answered " + status + " " + response.ReasonPhrase + ". Try again later."
                );
            }
        }

        // What a body read can fail with when the connection fails, as against a fault of the disk.
        private static bool IsNetworkFault(Exception e) =>
            e is IOException || e is HttpRequestException || e is WebException || e is ObjectDisposedException;

        private static ShareException BrokenOff(string what, Exception e) =>
            new ShareException(
                what + ": the download broke off (" + e.Message + "). Nothing was installed; try again.",
                e
            );

        private static ShareException TimedOut(Exception e) =>
            new ShareException("Share did not answer in time, so the download broke off. Try again later.", e);

        private static ShareException TooBig(string what) =>
            new ShareException(what + " is bigger than Share said it would be, so it was not accepted.");

        private static string AgentVersion()
        {
            Version version = typeof(ShareClient).Assembly.GetName().Version;
            return version == null
                ? "0"
                : version.Major.ToString(CultureInfo.InvariantCulture)
                    + "."
                    + version.Minor.ToString(CultureInfo.InvariantCulture)
                    + "."
                    + Math.Max(version.Build, 0).ToString(CultureInfo.InvariantCulture);
        }
    }
}
