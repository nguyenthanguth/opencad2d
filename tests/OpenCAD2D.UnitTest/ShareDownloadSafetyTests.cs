using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;
using OpenCAD2D.Share;
using OpenCAD2D.Tests;
using Xunit;

namespace OpenCAD2D.UnitTest
{
    /// <summary>
    /// What keeps Download from Share safe (W3 review): a hostile answer (a slug that is not the one asked for, an address off
    /// Share's host, a redirect, a file bigger than it said), names Windows keeps (a stream, a device), a zip that repeats a name
    /// in another case, a slow or dropped connection told apart from a full disk, the user's edited files kept as
    /// <c>name.local-date</c>, a locked file that leaves the old version whole, and a staging folder that the app never loads
    /// from. A fake handler serves the API and every folder is a temporary one.
    /// </summary>
    public sealed class ShareDownloadSafetyTests : IDisposable
    {
        private const string StairsLsp = "(defun c:STAIRS () (princ \"stairs\"))\n";

        private readonly string _root = Path.Combine(
            Path.GetTempPath(),
            "gk",
            "share-safe-" + Guid.NewGuid().ToString("N")
        );
        private readonly FakeShareServer _server = new FakeShareServer();
        private readonly HttpClient _http;

        public ShareDownloadSafetyTests()
        {
            Directory.CreateDirectory(_root);
            _http = new HttpClient(_server);
        }

        public void Dispose()
        {
            _http.Dispose();

            try
            {
                Directory.Delete(_root, true);
            }
            catch (IOException) { }
            catch (UnauthorizedAccessException) { }
        }

        private ShareClient Client => new ShareClient(_http);

        private string Folder(string name) => Path.Combine(_root, name);

        private static KeyValuePair<string, byte[]> File(string name, string text) =>
            new KeyValuePair<string, byte[]>(name, FakeShareServer.Bytes(text));

        private async Task<ShareTool> Found(string slug) =>
            await Client.FindAsync("https://opencad2d.com/share/" + slug);

        private static string[] Listing(string folder) =>
            Directory
                .GetFiles(folder, "*", SearchOption.AllDirectories)
                .Select(f => f.Substring(folder.Length + 1).Replace('\\', '/'))
                .OrderBy(f => f, StringComparer.Ordinal)
                .ToArray();

        // The text the API would give for a tool of one file, with the pieces a hostile one would change.
        private string Raw(
            string slug,
            string fileName,
            byte[] bytes,
            string url = null,
            long? size = null,
            string answerSlug = null
        )
        {
            string sha = FakeShareServer.Sha(bytes);
            _server.AddFile(sha, bytes);
            return "{\"slug\":\""
                + (answerSlug ?? slug)
                + "\",\"name\":\"Hostile\",\"kind\":\"lisp\",\"version\":{\"number\":\"1.0\"},\"files\":[{\"name\":\""
                + fileName.Replace("\\", "\\\\")
                + "\",\"size\":"
                + (size ?? bytes.Length)
                + ",\"sha256\":\""
                + sha
                + "\",\"url\":\""
                + (url ?? "https://opencad2d.com/api/files/" + sha)
                + "\"}]}";
        }

        // ---- The answer of the API

        [Theory]
        [InlineData("..\\..\\x")]
        [InlineData("a/b")]
        [InlineData("UPPER")]
        [InlineData("")]
        [InlineData("a b")]
        public void ASlugOfTheAnswerThatIsNotASlug_IsRefused(string slug)
        {
            string json =
                "{\"slug\":\""
                + slug.Replace("\\", "\\\\")
                + "\",\"name\":\"X\",\"kind\":\"lisp\",\"version\":{\"number\":\"1.0\"},\"files\":[]}";

            Assert.Throws<ShareException>(() => ShareTool.Parse(json, ShareLink.Site));
        }

        [Fact]
        public async Task AnAnswerForAnotherTool_ThanTheLinksSlug_IsRefused()
        {
            _server.AddRaw("stairs", Raw("rings", "rings.lsp", FakeShareServer.Bytes("(princ 1)")));

            ShareException error = await Assert.ThrowsAsync<ShareException>(() =>
                Client.FindAsync("https://opencad2d.com/share/stairs")
            );

            Assert.Contains("not the tool that was asked for", error.Message);
        }

        [Fact]
        public async Task AHostileSlugInTheAnswer_CannotNameAFolder()
        {
            _server.AddRaw(
                "stairs",
                Raw("stairs", "a.lsp", FakeShareServer.Bytes("x"), null, null, "..\\\\..\\\\evil")
            );

            await Assert.ThrowsAsync<ShareException>(() => Client.FindAsync("https://opencad2d.com/share/stairs"));
        }

        [Theory]
        [InlineData("https://evil.example/api/files/x")]
        [InlineData("http://opencad2d.com/api/files/x")]
        [InlineData("https://opencad2d.com.evil.example/api/files/x")]
        [InlineData("https://sub.opencad2d.com/api/files/x")]
        [InlineData("//evil.example/x")]
        public async Task AFileAddressOffShareHost_IsRefused_AndNothingIsAskedOfIt(string url)
        {
            _server.AddRaw("stairs", Raw("stairs", "a.lsp", FakeShareServer.Bytes("x"), url));

            await Assert.ThrowsAsync<ShareException>(() => Client.FindAsync("https://opencad2d.com/share/stairs"));

            Assert.Single(_server.Requests);
        }

        [Fact]
        public async Task AFileAddressOnWwwShareHost_IsAccepted()
        {
            byte[] bytes = FakeShareServer.Bytes("(princ 1)");
            _server.AddRaw(
                "stairs",
                Raw("stairs", "a.lsp", bytes, "https://www.opencad2d.com/api/files/" + FakeShareServer.Sha(bytes))
            );

            ShareTool tool = await Found("stairs");

            Assert.Equal("www.opencad2d.com", tool.Files[0].Url.Host);
        }

        [Fact]
        public async Task ARedirect_IsAnError_NotFollowed()
        {
            _server.RedirectTo = "https://evil.example/api/tools/stairs";

            ShareException error = await Assert.ThrowsAsync<ShareException>(() =>
                Client.FindAsync("https://opencad2d.com/share/stairs")
            );

            Assert.Contains("a redirect", error.Message);
            Assert.Contains("302", error.Message);
        }

        [Fact]
        public async Task AFileThatRedirects_IsAnError_AndLeavesNothing()
        {
            _server.AddTool("stairs", "lisp", "Stairs", "1.0", "stairs.lsp", StairsLsp);
            ShareTool tool = await Found("stairs");
            _server.RedirectTo = "https://evil.example/x";

            ShareException error = await Assert.ThrowsAsync<ShareException>(() =>
                ShareInstaller.InstallAsync(Client, tool, Folder("Lisps\\stairs"))
            );

            Assert.Contains("a redirect", error.Message);
            Assert.False(Directory.Exists(Folder("Lisps\\stairs")));
        }

        [Fact]
        public async Task EveryRequest_SaysItIsOpenCad2d_WithAVersion()
        {
            _server.AddTool(
                "stairs",
                "lisp",
                "Stairs",
                "1.0",
                new[] { File("stairs.lsp", StairsLsp) },
                null,
                FakeShareServer.Bytes("p")
            );
            ShareTool tool = await Found("stairs");
            await Client.GetPictureAsync(tool);
            await ShareInstaller.InstallAsync(Client, tool, Folder("Lisps\\stairs"));

            Assert.Equal(3, _server.UserAgents.Count);
            Assert.All(_server.UserAgents, a => Assert.Matches(@"^OpenCAD2D/\d+\.\d+\.\d+$", a));
        }

        // ---- Time and size

        [Fact]
        public async Task AServerThatGoesQuiet_EndsInASentence_WhenTheTimeIsUp()
        {
            _server.Hang = true;
            var client = new ShareClient(_http, null, TimeSpan.FromMilliseconds(150));

            ShareException error = await Assert.ThrowsAsync<ShareException>(() =>
                client.FindAsync("https://opencad2d.com/share/stairs")
            );

            Assert.Contains("did not answer in time", error.Message);
        }

        [Fact]
        public async Task ADownloadThatGoesQuiet_EndsInASentence_AndLeavesNothing()
        {
            _server.AddTool("stairs", "lisp", "Stairs", "1.0", "stairs.lsp", StairsLsp);
            var client = new ShareClient(_http, null, TimeSpan.FromMilliseconds(300));
            ShareTool tool = await client.FindAsync("https://opencad2d.com/share/stairs");
            _server.Hang = true;

            ShareException error = await Assert.ThrowsAsync<ShareException>(() =>
                ShareInstaller.InstallAsync(client, tool, Folder("Lisps\\stairs"))
            );

            Assert.Contains("did not answer in time", error.Message);
            Assert.False(Directory.Exists(Folder("Lisps\\stairs")));
        }

        [Fact]
        public async Task ACancelledRequest_IsACancellation_NotASentence()
        {
            _server.Hang = true;
            using (var cancel = new CancellationTokenSource())
            {
                Task<ShareTool> asking = Client.FindAsync("https://opencad2d.com/share/stairs", cancel.Token);
                cancel.CancelAfter(100);

                await Assert.ThrowsAnyAsync<OperationCanceledException>(() => asking);
            }
        }

        [Fact]
        public async Task AnAnswerOverOneMegabyte_IsRefused()
        {
            _server.AddRaw("stairs", "{\"slug\":\"stairs\",\"pad\":\"" + new string('x', 1024 * 1024 + 10) + "\"}");

            ShareException error = await Assert.ThrowsAsync<ShareException>(() =>
                Client.FindAsync("https://opencad2d.com/share/stairs")
            );

            Assert.Contains("bigger than Share said", error.Message);
        }

        [Fact]
        public async Task AFileLongerThanItsApiSize_IsRefused_AsSoonAsItIsToo_AndLeavesNothing()
        {
            _server.AddTool(
                "stairs",
                "lisp",
                "Stairs",
                "1.0",
                new[] { File("stairs.lsp", StairsLsp) },
                null,
                null,
                new Dictionary<string, byte[]> { ["stairs.lsp"] = FakeShareServer.Bytes(StairsLsp + StairsLsp) }
            );
            ShareTool tool = await Found("stairs");

            ShareException error = await Assert.ThrowsAsync<ShareException>(() =>
                ShareInstaller.InstallAsync(Client, tool, Folder("Lisps\\stairs"))
            );

            Assert.Contains("bigger than Share said", error.Message);
            Assert.False(Directory.Exists(Folder("Lisps\\stairs")));
        }

        [Fact]
        public async Task AFileTheApiSaysIsOverSixtyFourMegabytes_IsRefused_BeforeItIsAsked()
        {
            byte[] bytes = FakeShareServer.Bytes("(princ 1)");
            _server.AddRaw("stairs", Raw("stairs", "a.lsp", bytes, null, 70L * 1024 * 1024));
            ShareTool tool = await Found("stairs");
            int asked = _server.Requests.Count;

            ShareException error = await Assert.ThrowsAsync<ShareException>(() =>
                ShareInstaller.InstallAsync(Client, tool, Folder("Lisps\\stairs"))
            );

            Assert.Contains("larger than 64 MB", error.Message);
            Assert.Equal(asked, _server.Requests.Count);
        }

        // ---- The network and the disk are told apart

        [Fact]
        public async Task ADownloadThatBreaksOff_SaysTheDownloadBrokeOff_NotThatTheDiskFailed()
        {
            _server.AddTool("stairs", "lisp", "Stairs", "1.0", "stairs.lsp", StairsLsp);
            ShareTool tool = await Found("stairs");
            _server.BreakFiles = true;

            ShareException error = await Assert.ThrowsAsync<ShareException>(() =>
                ShareInstaller.InstallAsync(Client, tool, Folder("Lisps\\stairs"))
            );

            Assert.Contains("the download broke off", error.Message);
            Assert.DoesNotContain("could not be written", error.Message);
            Assert.False(Directory.Exists(Folder("Lisps\\stairs")));
        }

        [Fact]
        public async Task AFolderThatCannotBeMade_SaysTheToolCouldNotBeWritten_NotThatTheDownloadBrokeOff()
        {
            _server.AddTool("stairs", "lisp", "Stairs", "1.0", "stairs.lsp", StairsLsp);
            ShareTool tool = await Found("stairs");
            System.IO.File.WriteAllText(Folder("Lisps"), "a file where the folder should be");

            ShareException error = await Assert.ThrowsAsync<ShareException>(() =>
                ShareInstaller.InstallAsync(Client, tool, Folder("Lisps\\stairs"))
            );

            Assert.Contains("could not be written", error.Message);
            Assert.DoesNotContain("broke off", error.Message);
        }

        // ---- Names

        [Theory]
        [InlineData("CON")]
        [InlineData("con.lsp")]
        [InlineData("NUL.txt")]
        [InlineData("Aux.py")]
        [InlineData("prn.cs")]
        [InlineData("COM1.lsp")]
        [InlineData("com9.txt")]
        [InlineData("LPT1.md")]
        [InlineData("lpt9.json")]
        [InlineData("stairs.lsp:hidden")]
        [InlineData("stairs.lsp::$DATA")]
        public async Task AFileNameWindowsKeeps_OrWithAStream_IsRefused_BeforeAnythingIsDownloaded(string name)
        {
            _server.AddTool("bad", "lisp", "Bad", "1.0", name, "x");
            ShareTool tool = await Found("bad");
            int asked = _server.Requests.Count;

            await Assert.ThrowsAsync<ShareException>(() =>
                ShareInstaller.InstallAsync(Client, tool, Folder("Lisps\\bad"))
            );

            Assert.Equal(asked, _server.Requests.Count);
            Assert.False(Directory.Exists(Folder("Lisps\\bad")));
        }

        [Theory]
        [InlineData("CON.lsp")]
        [InlineData("sub/nul.txt")]
        [InlineData("AUX/readme.md")]
        [InlineData("COM3.py")]
        [InlineData("a.lsp:stream")]
        [InlineData("a.lsp::$DATA")]
        [InlineData(".share-tmp/x.lsp")]
        [InlineData("sub/.SHARE-old/x.lsp")]
        public async Task AZipEntryWithAReservedNameAStreamOrTheStagingsName_IsRefused_AndLeavesNothing(string entry)
        {
            byte[] zip = FakeShareServer.Zip(
                FakeShareServer.Entry("good.lsp", StairsLsp),
                FakeShareServer.Entry(entry, "x")
            );
            _server.AddTool("bad", "lisp", "Bad", "1.0", new[] { new KeyValuePair<string, byte[]>("Bad.zip", zip) });
            ShareTool tool = await Found("bad");

            await Assert.ThrowsAsync<ShareException>(() =>
                ShareInstaller.InstallAsync(Client, tool, Folder("Lisps\\bad"))
            );

            Assert.False(Directory.Exists(Folder("Lisps\\bad")));
        }

        [Fact]
        public async Task AZipThatRepeatsANameInAnotherCase_IsRefused_AsItWouldOverwriteItself()
        {
            byte[] zip = FakeShareServer.Zip(
                FakeShareServer.Entry("Stairs.lsp", "(princ 1)"),
                FakeShareServer.Entry("stairs.LSP", "(princ 2)")
            );
            _server.AddTool("bad", "lisp", "Bad", "1.0", new[] { new KeyValuePair<string, byte[]>("Bad.zip", zip) });
            ShareTool tool = await Found("bad");

            ShareException error = await Assert.ThrowsAsync<ShareException>(() =>
                ShareInstaller.InstallAsync(Client, tool, Folder("Lisps\\bad"))
            );

            Assert.Contains("twice", error.Message);
            Assert.False(Directory.Exists(Folder("Lisps\\bad")));
        }

        [Fact]
        public async Task TwoFilesOfTheApiThatDifferOnlyInCase_AreRefused()
        {
            _server.AddTool(
                "bad",
                "lisp",
                "Bad",
                "1.0",
                new[] { File("A.lsp", "(princ 1)"), File("a.lsp", "(princ 2)") }
            );
            ShareTool tool = await Found("bad");

            ShareException error = await Assert.ThrowsAsync<ShareException>(() =>
                ShareInstaller.InstallAsync(Client, tool, Folder("Lisps\\bad"))
            );

            Assert.Contains("twice", error.Message);
            Assert.False(Directory.Exists(Folder("Lisps\\bad")));
        }

        // ---- The user's own work

        [Fact]
        public async Task AFileTheUserEdited_ShowsChangedOnThisPc_ByItsSha256()
        {
            _server.AddTool(
                "stairs",
                "lisp",
                "Stairs",
                "1.0",
                new[] { File("stairs.lsp", StairsLsp), File("b.lsp", "(princ 2)") }
            );
            ShareTool tool = await Found("stairs");
            string folder = Folder("Lisps\\stairs");
            await ShareInstaller.InstallAsync(Client, tool, folder);
            Assert.Equal(ShareCopyState.Same, ShareCopy.Check(tool, folder).State);

            System.IO.File.AppendAllText(Path.Combine(folder, "stairs.lsp"), "; mine\n");

            ShareCopy copy = ShareCopy.Check(tool, folder);
            Assert.Equal(ShareCopyState.Changed, copy.State);
            Assert.Equal(new[] { "stairs.lsp" }, copy.ChangedFiles);
            Assert.Equal("1.0", copy.Version);
        }

        [Fact]
        public async Task AFileTheUserDeleted_OrReplaced_AlsoShowsChanged()
        {
            _server.AddTool(
                "stairs",
                "lisp",
                "Stairs",
                "1.0",
                new[] { File("a.lsp", "(princ 1)"), File("b.lsp", "(princ 2)") }
            );
            ShareTool tool = await Found("stairs");
            string folder = Folder("Lisps\\stairs");
            await ShareInstaller.InstallAsync(Client, tool, folder);

            System.IO.File.Delete(Path.Combine(folder, "a.lsp"));
            ShareCopy deleted = ShareCopy.Check(tool, folder);
            Assert.Equal(ShareCopyState.Changed, deleted.State);
            Assert.Equal(new[] { "a.lsp" }, deleted.ChangedFiles);

            System.IO.File.WriteAllText(Path.Combine(folder, "a.lsp"), "(princ 1)");
            Assert.Equal(ShareCopyState.Same, ShareCopy.Check(tool, folder).State);
        }

        [Fact]
        public async Task AnUpdate_KeepsTheUsersEditedFileAsNameLocalDate_ThenWritesTheNewOne()
        {
            _server.AddTool(
                "stairs",
                "lisp",
                "Stairs",
                "1.0",
                new[] { File("stairs.lsp", StairsLsp), File("gone.lsp", "(princ 0)") }
            );
            string folder = Folder("Lisps\\stairs");
            await ShareInstaller.InstallAsync(Client, await Found("stairs"), folder);
            System.IO.File.WriteAllText(Path.Combine(folder, "stairs.lsp"), "(princ \"mine\")");
            System.IO.File.WriteAllText(Path.Combine(folder, "gone.lsp"), "(princ \"mine too\")");
            System.IO.File.WriteAllText(Path.Combine(folder, "notes.txt"), "my own notes");

            var second = new FakeShareServer();
            second.AddTool("stairs", "lisp", "Stairs", "1.1", "stairs.lsp", "(princ \"two\")");
            var client = new ShareClient(new HttpClient(second));
            ShareInstalled done = await ShareInstaller.InstallAsync(
                client,
                await client.FindAsync("https://opencad2d.com/share/stairs"),
                folder
            );

            string day = DateTime.Now.ToString("yyyy-MM-dd");
            Assert.Equal(
                new[] { "gone.lsp.local-" + day, "notes.txt", "share.json", "stairs.lsp", "stairs.lsp.local-" + day },
                Listing(folder)
            );
            Assert.Equal("(princ \"two\")", System.IO.File.ReadAllText(Path.Combine(folder, "stairs.lsp")));
            Assert.Equal(
                "(princ \"mine\")",
                System.IO.File.ReadAllText(Path.Combine(folder, "stairs.lsp.local-" + day))
            );
            Assert.Equal(
                "(princ \"mine too\")",
                System.IO.File.ReadAllText(Path.Combine(folder, "gone.lsp.local-" + day))
            );
            Assert.Equal("my own notes", System.IO.File.ReadAllText(Path.Combine(folder, "notes.txt")));
            Assert.Equal(
                new[]
                {
                    Path.Combine(folder, "gone.lsp.local-" + day),
                    Path.Combine(folder, "stairs.lsp.local-" + day),
                },
                done.BackedUp
            );
        }

        [Fact]
        public async Task AFileOfTheUserAtAPathTheToolWrites_WithNoEarlierInstall_IsKeptToo()
        {
            string folder = Folder("Lisps\\stairs");
            Directory.CreateDirectory(folder);
            System.IO.File.WriteAllText(Path.Combine(folder, "stairs.lsp"), "(princ \"before Share\")");
            _server.AddTool("stairs", "lisp", "Stairs", "1.0", "stairs.lsp", StairsLsp);

            ShareInstalled done = await ShareInstaller.InstallAsync(Client, await Found("stairs"), folder);

            string kept = Path.Combine(folder, "stairs.lsp.local-" + DateTime.Now.ToString("yyyy-MM-dd"));
            Assert.Equal(new[] { kept }, done.BackedUp);
            Assert.Equal("(princ \"before Share\")", System.IO.File.ReadAllText(kept));
            Assert.Equal(StairsLsp, System.IO.File.ReadAllText(Path.Combine(folder, "stairs.lsp")));
        }

        [Fact]
        public async Task ASecondBackupOnTheSameDay_DoesNotOverwriteTheFirst()
        {
            _server.AddTool("stairs", "lisp", "Stairs", "1.0", "stairs.lsp", StairsLsp);
            string folder = Folder("Lisps\\stairs");
            await ShareInstaller.InstallAsync(Client, await Found("stairs"), folder);
            string day = DateTime.Now.ToString("yyyy-MM-dd");

            for (int round = 1; round <= 2; round++)
            {
                System.IO.File.WriteAllText(Path.Combine(folder, "stairs.lsp"), "(princ \"edit " + round + "\")");
                await ShareInstaller.InstallAsync(Client, await Found("stairs"), folder);
            }

            Assert.Equal(
                new[] { "share.json", "stairs.lsp", "stairs.lsp.local-" + day, "stairs.lsp.local-" + day + "-2" },
                Listing(folder)
            );
            Assert.Equal(
                "(princ \"edit 1\")",
                System.IO.File.ReadAllText(Path.Combine(folder, "stairs.lsp.local-" + day))
            );
            Assert.Equal(
                "(princ \"edit 2\")",
                System.IO.File.ReadAllText(Path.Combine(folder, "stairs.lsp.local-" + day + "-2"))
            );
        }

        [Fact]
        public async Task ALockedFile_StopsAnUpdate_AndTheOldVersionIsWhole()
        {
            _server.AddTool(
                "stairs",
                "lisp",
                "Stairs",
                "1.0",
                new[] { File("stairs.lsp", StairsLsp), File("b.lsp", "(princ 2)") }
            );
            string folder = Folder("Lisps\\stairs");
            await ShareInstaller.InstallAsync(Client, await Found("stairs"), folder);

            var second = new FakeShareServer();
            second.AddTool(
                "stairs",
                "lisp",
                "Stairs",
                "1.1",
                new[] { File("stairs.lsp", "(princ \"two\")"), File("b.lsp", "(princ 3)") }
            );
            var client = new ShareClient(new HttpClient(second));
            ShareTool newer = await client.FindAsync("https://opencad2d.com/share/stairs");

            using (
                var held = new FileStream(Path.Combine(folder, "b.lsp"), FileMode.Open, FileAccess.Read, FileShare.None)
            )
            {
                ShareException error = await Assert.ThrowsAsync<ShareException>(() =>
                    ShareInstaller.InstallAsync(client, newer, folder)
                );

                Assert.Contains("could not be written", error.Message);
            }

            Assert.Equal(new[] { "b.lsp", "share.json", "stairs.lsp" }, Listing(folder));
            Assert.Equal(StairsLsp, System.IO.File.ReadAllText(Path.Combine(folder, "stairs.lsp")));
            Assert.Equal(ShareCopyState.Same, ShareCopy.Check(await Found("stairs"), folder).State);
        }

        // ---- The staging folder

        [Fact]
        public async Task TheStagingFolder_IsHidden_AndNeverWalkedByTheLoaderOfLisps()
        {
            var folders = new CadSupportFolders(Folder("program"));
            string lisps = folders.FolderOf(CadSupportKind.Lisps);
            Directory.CreateDirectory(lisps);
            System.IO.File.WriteAllText(Path.Combine(lisps, "ok.lsp"), "(princ 1)");
            _server.AddTool(
                "stairs",
                "lisp",
                "Stairs",
                "1.0",
                new[] { File("a.lsp", "(princ 1)"), File("b.lsp", "(princ 2)") }
            );
            ShareTool tool = await Found("stairs");
            var seen = new List<string>();
            bool stagingThere = false;
            bool hidden = false;
            int files = 0;

            // At the second file the first one is downloaded and the staging folder exists, here beside the tool's own folder.
            _server.OnRequest = path =>
            {
                if (path.StartsWith("/api/files/", StringComparison.Ordinal) && ++files == 2)
                {
                    string staging = Path.Combine(lisps, ".share-tmp");
                    stagingThere = Directory.Exists(staging);
                    hidden = stagingThere && (new DirectoryInfo(staging).Attributes & FileAttributes.Hidden) != 0;

                    foreach (string folder in folders.SearchFoldersOf(CadSupportKind.Lisps))
                    {
                        seen.AddRange(CadSupportWalk.FilesOf(folder, name => true));
                    }
                }
            };

            await ShareInstaller.InstallAsync(Client, tool, Path.Combine(lisps, "stairs"));

            Assert.True(stagingThere, "The staging folder was there.");
            Assert.True(hidden, "The staging folder is hidden.");
            Assert.Equal(new[] { Path.Combine(lisps, "ok.lsp") }, seen);
        }

        [Fact]
        public async Task TheStagingFolderGiven_IsUsed_AndTheLispsFolderGetsOnlyTheTool()
        {
            var folders = new CadSupportFolders(Folder("program"));
            string lisps = folders.FolderOf(CadSupportKind.Lisps);
            _server.AddTool(
                "stairs",
                "lisp",
                "Stairs",
                "1.0",
                new[] { File("a.lsp", "(princ 1)"), File("b.lsp", "(princ 2)") }
            );
            ShareTool tool = await Found("stairs");
            bool usedGiven = false;
            int files = 0;
            _server.OnRequest = path =>
            {
                if (path.StartsWith("/api/files/", StringComparison.Ordinal) && ++files == 2)
                {
                    usedGiven =
                        Directory.Exists(ShareInstaller.StagingRoot(folders))
                        && Directory.GetDirectories(ShareInstaller.StagingRoot(folders)).Length == 1;
                }
            };

            await ShareInstaller.InstallAsync(
                Client,
                tool,
                Path.Combine(lisps, "stairs"),
                ShareInstaller.StagingRoot(folders)
            );

            Assert.True(usedGiven);
            Assert.Equal(new[] { "stairs" }, Directory.GetDirectories(lisps).Select(Path.GetFileName));
            Assert.Empty(Directory.GetDirectories(ShareInstaller.StagingRoot(folders)));
        }

        [Fact]
        public void LeftoversOfACutOffInstall_AreCleaned_AndAnOldVersionWhoseFolderIsGoneComesBack()
        {
            string staging = Folder("program\\Share\\.share-tmp");
            string tool = Folder("program\\Lisps\\stairs");
            string cut = Path.Combine(staging, "aaaa");
            Directory.CreateDirectory(Path.Combine(cut, "old"));
            System.IO.File.WriteAllText(Path.Combine(cut, "old", "stairs.lsp"), "the old version");
            System.IO.File.WriteAllText(Path.Combine(cut, "target.txt"), tool);
            string junk = Path.Combine(staging, "bbbb");
            Directory.CreateDirectory(Path.Combine(junk, "download"));
            System.IO.File.WriteAllText(Path.Combine(junk, "download", "x.lsp"), "half a download");

            ShareInstaller.CleanLeftovers(staging);

            Assert.Equal("the old version", System.IO.File.ReadAllText(Path.Combine(tool, "stairs.lsp")));
            Assert.Empty(Directory.GetDirectories(staging));
        }

        [Fact]
        public async Task AnInstall_CleansTheLeftoversOfAnEarlierOne()
        {
            var folders = new CadSupportFolders(Folder("program"));
            string staging = ShareInstaller.StagingRoot(folders);
            Directory.CreateDirectory(Path.Combine(staging, "cccc", "download"));
            _server.AddTool("stairs", "lisp", "Stairs", "1.0", "stairs.lsp", StairsLsp);

            await ShareInstaller.InstallAsync(Client, await Found("stairs"), Folder("program\\Share\\stairs"), staging);

            Assert.Empty(Directory.GetDirectories(staging));
        }

        [Fact]
        public async Task ACancelledInstall_LeavesTheOldVersionAndNoStaging()
        {
            _server.AddTool("stairs", "lisp", "Stairs", "1.0", "stairs.lsp", StairsLsp);
            string folder = Folder("Lisps\\stairs");
            await ShareInstaller.InstallAsync(Client, await Found("stairs"), folder);

            var second = new FakeShareServer();
            second.AddTool("stairs", "lisp", "Stairs", "1.1", "stairs.lsp", "(princ \"two\")");
            var client = new ShareClient(new HttpClient(second));
            ShareTool newer = await client.FindAsync("https://opencad2d.com/share/stairs");

            using (var cancel = new CancellationTokenSource())
            {
                second.OnRequest = path =>
                {
                    if (path.StartsWith("/api/files/", StringComparison.Ordinal))
                    {
                        cancel.Cancel();
                    }
                };

                await Assert.ThrowsAnyAsync<OperationCanceledException>(() =>
                    ShareInstaller.InstallAsync(client, newer, folder, null, cancel.Token)
                );
            }

            Assert.Equal(StairsLsp, System.IO.File.ReadAllText(Path.Combine(folder, "stairs.lsp")));
            Assert.Equal(ShareCopyState.Same, ShareCopy.Check(await Found("stairs"), folder).State);
        }
    }
}
