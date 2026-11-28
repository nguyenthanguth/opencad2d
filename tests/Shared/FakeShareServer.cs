using System;
using System.Collections.Generic;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Security.Cryptography;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

namespace OpenCAD2D.Tests
{
    /// <summary>
    /// Share's API as a test serves it, with no network: <c>GET /api/tools/{slug}</c> answers the JSON of a tool added with
    /// <see cref="AddTool"/> and <c>GET /api/files/{key}</c> the bytes of its files and picture. Every request it sees is
    /// kept in <see cref="Requests"/>, so a test can say that nothing was asked.
    /// </summary>
    internal sealed class FakeShareServer : HttpMessageHandler
    {
        private readonly object _lock = new object();
        private readonly Dictionary<string, string> _tools = new Dictionary<string, string>(StringComparer.Ordinal);
        private readonly Dictionary<string, byte[]> _files = new Dictionary<string, byte[]>(StringComparer.Ordinal);
        private readonly List<string> _requests = new List<string>();

        /// <summary>Gets the addresses asked, in order, as <c>/api/tools/stairs</c>.</summary>
        public IReadOnlyList<string> Requests
        {
            get
            {
                lock (_lock)
                {
                    return _requests.ToList();
                }
            }
        }

        /// <summary>Gets or sets what the server does instead of answering, as a network that is down: null for nothing.</summary>
        public Exception Down { get; set; }

        /// <summary>Gets or sets a status for every request, as 500; zero for the real answers.</summary>
        public int Status { get; set; }

        /// <summary>Gets or sets whether a request is never answered, until it is cancelled: a server that has gone quiet.</summary>
        public bool Hang { get; set; }

        /// <summary>Gets or sets an address every request is redirected to (a 302); null for none.</summary>
        public string RedirectTo { get; set; }

        /// <summary>Gets or sets whether a file's body breaks off half way with an <see cref="IOException"/>, as a connection that drops.</summary>
        public bool BreakFiles { get; set; }

        /// <summary>Gets or sets what is done on each request, given its path, before it is answered.</summary>
        public Action<string> OnRequest { get; set; }

        /// <summary>Gets the User-Agent of each request, in order.</summary>
        public IReadOnlyList<string> UserAgents
        {
            get
            {
                lock (_lock)
                {
                    return _agents.ToList();
                }
            }
        }

        /// <summary>Adds the text of an answer for a slug as it is, for a tool the API would never give.</summary>
        public void AddRaw(string slug, string json) => _tools[slug] = json;

        /// <summary>Serves bytes at <c>/api/files/{key}</c>.</summary>
        public void AddFile(string key, byte[] bytes) => _files[key] = bytes;

        private readonly List<string> _agents = new List<string>();

        // A body that gives half of its bytes and then fails, as a dropped connection does.
        private sealed class BrokenStream : MemoryStream
        {
            public BrokenStream(byte[] bytes)
                : base(bytes, 0, bytes.Length / 2) { }

            public override int Read(byte[] buffer, int offset, int count)
            {
                int read = base.Read(buffer, offset, count);

                if (read == 0)
                {
                    throw new IOException("The connection was closed.");
                }

                return read;
            }

            public override Task<int> ReadAsync(
                byte[] buffer,
                int offset,
                int count,
                CancellationToken cancellationToken
            ) => Task.FromResult(Read(buffer, offset, count));
        }

        /// <summary>The SHA-256 of some bytes, as 64 lower-case hex digits.</summary>
        public static string Sha(byte[] bytes)
        {
            using (SHA256 sha = SHA256.Create())
            {
                return string.Concat(sha.ComputeHash(bytes).Select(b => b.ToString("x2")));
            }
        }

        /// <summary>The bytes of some text, UTF-8 with no byte-order mark.</summary>
        public static byte[] Bytes(string text) => new UTF8Encoding(false).GetBytes(text);

        /// <summary>A zip of files given as name and text pairs; a name may hold <c>..</c> or a drive, as a hostile zip does.</summary>
        public static byte[] Zip(params KeyValuePair<string, string>[] entries)
        {
            using (var memory = new MemoryStream())
            {
                using (var zip = new ZipArchive(memory, ZipArchiveMode.Create, true))
                {
                    foreach (KeyValuePair<string, string> entry in entries)
                    {
                        ZipArchiveEntry made = zip.CreateEntry(entry.Key);

                        using (Stream stream = made.Open())
                        {
                            byte[] bytes = Bytes(entry.Value);
                            stream.Write(bytes, 0, bytes.Length);
                        }
                    }
                }

                return memory.ToArray();
            }
        }

        /// <summary>A name and text, for <see cref="Zip"/>.</summary>
        public static KeyValuePair<string, string> Entry(string name, string text) =>
            new KeyValuePair<string, string>(name, text);

        /// <summary>
        /// Adds a tool the way the API shows one. <paramref name="files"/> are served as they are given, and the API's SHA-256
        /// of each is the real one, unless <paramref name="served"/> gives the bytes of a file that the server sends instead.
        /// </summary>
        /// <param name="slug">The slug.</param>
        /// <param name="kind">lisp, python or csharp.</param>
        /// <param name="name">The name.</param>
        /// <param name="version">The live version's number.</param>
        /// <param name="files">The files: name and bytes.</param>
        /// <param name="commands">The commands.</param>
        /// <param name="picture">The cover picture's bytes; null for none.</param>
        /// <param name="served">The bytes sent for a file, by name, where they are not the file's own.</param>
        public void AddTool(
            string slug,
            string kind,
            string name,
            string version,
            IEnumerable<KeyValuePair<string, byte[]>> files,
            string[] commands = null,
            byte[] picture = null,
            IDictionary<string, byte[]> served = null
        )
        {
            var json = new StringBuilder();
            json.Append("{\"id\":7,\"slug\":").Append(Quote(slug));
            json.Append(",\"name\":").Append(Quote(name));
            json.Append(",\"kind\":").Append(Quote(kind));
            json.Append(",\"summary\":").Append(Quote("Draws " + name + " from a few numbers."));
            json.Append(",\"author\":\"archi_tools\",\"license\":\"MIT\",\"downloads\":3");
            json.Append(",\"created\":\"2026-11-01T08:00:00Z\",\"updated\":\"2026-11-20T09:30:00Z\"");

            if (picture == null)
            {
                json.Append(",\"picture\":null");
            }
            else
            {
                string key = Sha(picture);
                json.Append(",\"picture\":{\"url\":\"https://opencad2d.com/api/files/pictures/")
                    .Append(key)
                    .Append(".png\",\"sha256\":\"")
                    .Append(key)
                    .Append("\"}");
                _files["pictures/" + key + ".png"] =
                    served != null && served.TryGetValue("picture", out byte[] other) ? other : picture;
            }

            json.Append(",\"version\":{\"number\":")
                .Append(Quote(version))
                .Append(",\"approved\":\"2026-11-20T09:30:00Z\"}");
            json.Append(",\"commands\":[")
                .Append(string.Join(",", (commands ?? new string[0]).Select(Quote)))
                .Append("]");
            json.Append(",\"files\":[");
            bool first = true;

            foreach (KeyValuePair<string, byte[]> file in files)
            {
                string sha = Sha(file.Value);
                _files[sha] = served != null && served.TryGetValue(file.Key, out byte[] other) ? other : file.Value;

                if (!first)
                {
                    json.Append(',');
                }

                first = false;
                json.Append("{\"name\":").Append(Quote(file.Key));
                json.Append(",\"size\":").Append(file.Value.Length);
                json.Append(",\"sha256\":\"")
                    .Append(sha)
                    .Append("\",\"url\":\"https://opencad2d.com/api/files/")
                    .Append(sha)
                    .Append("\"}");
            }

            json.Append("]}");
            _tools[slug] = json.ToString();
        }

        /// <summary>Adds a tool of one text file.</summary>
        public void AddTool(
            string slug,
            string kind,
            string name,
            string version,
            string fileName,
            string text,
            string[] commands = null
        ) =>
            AddTool(
                slug,
                kind,
                name,
                version,
                new[] { new KeyValuePair<string, byte[]>(fileName, Bytes(text)) },
                commands
            );

        protected override Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request,
            CancellationToken cancellationToken
        )
        {
            string path = request.RequestUri.AbsolutePath;

            lock (_lock)
            {
                _requests.Add(path);
            }

            lock (_lock)
            {
                _agents.Add(request.Headers.UserAgent.ToString());
            }

            OnRequest?.Invoke(path);

            if (Hang)
            {
                var never = new TaskCompletionSource<HttpResponseMessage>();
                cancellationToken.Register(() => never.TrySetCanceled());
                return never.Task;
            }

            if (RedirectTo != null)
            {
                var moved = new HttpResponseMessage(HttpStatusCode.Found);
                moved.Headers.Location = new Uri(RedirectTo);
                return Task.FromResult(moved);
            }

            if (Down != null)
            {
                throw Down;
            }

            if (Status != 0)
            {
                return Task.FromResult(new HttpResponseMessage((HttpStatusCode)Status));
            }

            const string tools = "/api/tools/";
            const string files = "/api/files/";

            if (
                path.StartsWith(tools, StringComparison.Ordinal)
                && _tools.TryGetValue(path.Substring(tools.Length), out string json)
            )
            {
                return Task.FromResult(
                    new HttpResponseMessage(HttpStatusCode.OK)
                    {
                        Content = new StringContent(json, Encoding.UTF8, "application/json"),
                    }
                );
            }

            if (
                path.StartsWith(files, StringComparison.Ordinal)
                && _files.TryGetValue(path.Substring(files.Length), out byte[] bytes)
            )
            {
                return Task.FromResult(
                    new HttpResponseMessage(HttpStatusCode.OK)
                    {
                        Content = BreakFiles
                            ? (HttpContent)new StreamContent(new BrokenStream(bytes))
                            : new ByteArrayContent(bytes),
                    }
                );
            }

            return Task.FromResult(
                new HttpResponseMessage(HttpStatusCode.NotFound)
                {
                    Content = new StringContent(
                        "{\"error\":\"not_found\",\"message\":\"Share has no such tool.\"}",
                        Encoding.UTF8,
                        "application/json"
                    ),
                }
            );
        }

        private static string Quote(string text) => "\"" + text.Replace("\\", "\\\\").Replace("\"", "\\\"") + "\"";
    }
}
