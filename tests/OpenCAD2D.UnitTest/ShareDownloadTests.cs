using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net.Http;
using System.Threading.Tasks;
using OpenCAD2D.Share;
using OpenCAD2D.Tests;
using Xunit;

namespace OpenCAD2D.UnitTest
{
    /// <summary>
    /// Download from Share, in the engine (W3): the link a user pastes, the tool the API gives for it, the cover picture, what
    /// is on this PC by SHA-256, and the install into the folder of each kind with every file checked against the SHA-256 the
    /// API gave. A fake handler serves the API, so no test touches the network, and every folder is a temporary one.
    /// </summary>
    public sealed class ShareDownloadTests : IDisposable
    {
        private const string StairsLsp = "(defun c:STAIRS () (princ \"stairs\"))\n";

        private readonly string _root = Path.Combine(Path.GetTempPath(), "gk", "share-" + Guid.NewGuid().ToString("N"));
        private readonly FakeShareServer _server = new FakeShareServer();
        private readonly HttpClient _http;

        public ShareDownloadTests()
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

        private string[] Listing(string folder) =>
            Directory
                .GetFiles(folder, "*", SearchOption.AllDirectories)
                .Select(f => f.Substring(folder.Length + 1).Replace('\\', '/'))
                .OrderBy(f => f, StringComparer.Ordinal)
                .ToArray();

        // ---- The link

        [Theory]
        [InlineData("https://opencad2d.com/share/stairs", "stairs")]
        [InlineData("  https://opencad2d.com/share/stairs/  ", "stairs")]
        [InlineData("https://www.opencad2d.com/share/stairs?ref=x#top", "stairs")]
        [InlineData("https://OpenCAD2D.com/Share/Stairs-2", "stairs-2")]
        public void ALinkToAToolsPage_GivesItsSlug(string link, string slug)
        {
            Assert.True(ShareLink.TryGetSlug(link, ShareLink.Site, out string found));
            Assert.Equal(slug, found);
        }

        [Theory]
        [InlineData("")]
        [InlineData("stairs")]
        [InlineData("http://opencad2d.com/share/stairs")]
        [InlineData("https://example.com/share/stairs")]
        [InlineData("https://opencad2d.com/share")]
        [InlineData("https://opencad2d.com/share/by/someone")]
        [InlineData("https://opencad2d.com/share/stairs/extra")]
        [InlineData("https://opencad2d.com/api/tools/stairs")]
        [InlineData("https://opencad2d.com/share/st airs")]
        [InlineData("https://opencad2d.com/share/..")]
        public void ATextThatIsNoToolsLink_GivesNoSlug(string link)
        {
            Assert.False(ShareLink.TryGetSlug(link, ShareLink.Site, out string slug));
            Assert.Null(slug);
        }

        // ---- Find

        [Fact]
        public async Task Find_ReadsTheToolTheApiGives_WithOneRequest()
        {
            byte[] picture = FakeShareServer.Bytes("not really a png");
            _server.AddTool(
                "stairs",
                "csharp",
                "Stairs",
                "1.2",
                new[] { File("Stairs.cs", "class Stairs {}"), File("Stairs.csproj", "<Project />") },
                new[] { "STAIRS" },
                picture
            );

            ShareTool tool = await Client.FindAsync("https://opencad2d.com/share/stairs");

            Assert.Equal("/api/tools/stairs", Assert.Single(_server.Requests));
            Assert.Equal("stairs", tool.Slug);
            Assert.Equal("Stairs", tool.Name);
            Assert.Equal("Draws Stairs from a few numbers.", tool.Summary);
            Assert.Equal(ShareKind.CSharp, tool.Kind);
            Assert.Equal("archi_tools", tool.Author);
            Assert.Equal("1.2", tool.Version);
            Assert.Equal("2026-11-20T09:30:00Z", tool.Approved);
            Assert.Equal(new[] { "STAIRS" }, tool.Commands);
            Assert.Equal(new[] { "Stairs.cs", "Stairs.csproj" }, tool.Files.Select(f => f.Name));
            Assert.Equal(FakeShareServer.Sha(FakeShareServer.Bytes("class Stairs {}")), tool.Files[0].Sha256);
            Assert.Equal(15, tool.Files[0].Size);
            Assert.Equal(FakeShareServer.Sha(picture), tool.PictureSha256);
        }

        [Fact]
        public async Task TheCoverPicture_IsDownloadedAndCheckedAgainstItsSha256()
        {
            byte[] picture = FakeShareServer.Bytes("picture bytes");
            _server.AddTool(
                "stairs",
                "lisp",
                "Stairs",
                "1.0",
                new[] { File("stairs.lsp", StairsLsp) },
                new[] { "STAIRS" },
                picture
            );
            ShareTool tool = await Found("stairs");

            Assert.Equal(picture, await Client.GetPictureAsync(tool));
        }

        [Fact]
        public async Task ACoverPictureWhoseBytesAreNotTheRecordedOnes_IsLeftOut_WithNoException()
        {
            byte[] picture = FakeShareServer.Bytes("picture bytes");
            _server.AddTool(
                "stairs",
                "lisp",
                "Stairs",
                "1.0",
                new[] { File("stairs.lsp", StairsLsp) },
                null,
                picture,
                new Dictionary<string, byte[]> { ["picture"] = FakeShareServer.Bytes("another picture") }
            );
            ShareTool tool = await Found("stairs");

            Assert.Null(await Client.GetPictureAsync(tool));
        }

        [Fact]
        public async Task ACoverPictureThatCannotBeHad_IsLeftOut_WithNoException()
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
            _server.Status = 500;

            Assert.Null(await Client.GetPictureAsync(tool));
        }

        [Fact]
        public async Task AToolWithNoPicture_AsksNoPictureAtAll()
        {
            _server.AddTool("stairs", "lisp", "Stairs", "1.0", "stairs.lsp", StairsLsp);
            ShareTool tool = await Found("stairs");

            Assert.Null(tool.PictureUrl);
            Assert.Null(await Client.GetPictureAsync(tool));
            Assert.Single(_server.Requests);
        }

        [Fact]
        public async Task ALinkThatIsNoTool_SaysSo_AndAsksNothing()
        {
            ShareException error = await Assert.ThrowsAsync<ShareException>(() =>
                Client.FindAsync("https://example.com/somewhere")
            );

            Assert.Contains("not the link of a tool on Share", error.Message);
            Assert.Contains("https://opencad2d.com/share/stairs", error.Message);
            Assert.Empty(_server.Requests);
        }

        [Fact]
        public async Task AToolShareDoesNotHave_SaysSo_ByItsSlug()
        {
            ShareException error = await Assert.ThrowsAsync<ShareException>(() =>
                Client.FindAsync("https://opencad2d.com/share/nothing-here")
            );

            Assert.Equal("Share has no tool called \"nothing-here\".", error.Message);
        }

        [Fact]
        public async Task AServerThatCannotBeReached_IsASentence_NotAnException()
        {
            _server.Down = new HttpRequestException("No such host is known");

            ShareException error = await Assert.ThrowsAsync<ShareException>(() =>
                Client.FindAsync("https://opencad2d.com/share/stairs")
            );

            Assert.Contains("Share could not be reached", error.Message);
            Assert.Contains("No such host is known", error.Message);
        }

        [Fact]
        public async Task AServerThatAnswersAnError_IsASentenceWithItsStatus()
        {
            _server.Status = 503;

            ShareException error = await Assert.ThrowsAsync<ShareException>(() =>
                Client.FindAsync("https://opencad2d.com/share/stairs")
            );

            Assert.Contains("503", error.Message);
        }

        [Theory]
        [InlineData("not json")]
        [InlineData("{}")]
        [InlineData("{\"slug\":\"a\",\"name\":\"A\",\"kind\":\"exe\",\"version\":{\"number\":\"1.0\"}}")]
        [InlineData(
            "{\"slug\":\"a\",\"name\":\"A\",\"kind\":\"lisp\",\"version\":{\"number\":\"1.0\"},\"files\":[{\"name\":\"a.lsp\",\"sha256\":\"zz\",\"url\":\"https://opencad2d.com/api/files/zz\"}]}"
        )]
        [InlineData(
            "{\"slug\":\"a\",\"name\":\"A\",\"kind\":\"lisp\",\"version\":{\"number\":\"1.0\"},\"files\":[{\"name\":\"a.lsp\",\"sha256\":\"0000000000000000000000000000000000000000000000000000000000000000\",\"url\":\"https://elsewhere.example/a.lsp\"}]}"
        )]
        public void AnAnswerThisAppCannotRead_IsRefused(string json)
        {
            Assert.Throws<ShareException>(() => ShareTool.Parse(json, ShareLink.Site));
        }

        // ---- Where each kind goes

        [Fact]
        public void EachKindGoesToTheFolderTheAppLoadsItFrom_UnderItsSlug_AndNotLoadedGoesToShare()
        {
            var folders = new CadSupportFolders(Folder("app"));

            Assert.Equal(
                Folder("app\\Lisps\\stairs"),
                ShareInstaller.ToolFolder(folders, ShareKind.Lisp, "stairs", true)
            );
            Assert.Equal(
                Folder("app\\Pythons\\stairs"),
                ShareInstaller.ToolFolder(folders, ShareKind.Python, "stairs", true)
            );
            Assert.Equal(
                Folder("app\\Plugins\\stairs"),
                ShareInstaller.ToolFolder(folders, ShareKind.CSharp, "stairs", true)
            );
            Assert.Equal(
                Folder("app\\Share\\stairs"),
                ShareInstaller.ToolFolder(folders, ShareKind.Lisp, "stairs", false)
            );
            Assert.Equal(
                Folder("app\\Share\\stairs"),
                ShareInstaller.ToolFolder(folders, ShareKind.CSharp, "stairs", false)
            );
            Assert.Equal(Folder("app\\Share\\.share-tmp"), ShareInstaller.StagingRoot(folders));
            Assert.Null(ShareInstaller.ToolFolder(new CadSupportFolders(null), ShareKind.Lisp, "stairs", true));
            Assert.Throws<ArgumentException>(() => ShareInstaller.ToolFolder(folders, ShareKind.Lisp, "..\\x", true));
        }

        // ---- Install

        [Fact]
        public async Task Install_WritesTheFilesAndTheManifest_AndLeavesNoTemporaryFolder()
        {
            _server.AddTool(
                "stairs",
                "csharp",
                "Stairs",
                "1.2",
                new[] { File("Stairs.cs", "class Stairs {}"), File("Stairs.csproj", "<Project />") },
                new[] { "STAIRS" }
            );
            ShareTool tool = await Found("stairs");
            string folder = Folder("Plugins\\stairs");

            ShareInstalled done = await ShareInstaller.InstallAsync(Client, tool, folder);

            Assert.Equal(folder, done.Folder);
            Assert.Equal("1.2", done.Version);
            Assert.Null(done.PreviousVersion);
            Assert.Equal(new[] { "Stairs.cs", "Stairs.csproj", "share.json" }, Listing(folder));
            Assert.Equal("class Stairs {}", System.IO.File.ReadAllText(Path.Combine(folder, "Stairs.cs")));
            Assert.Equal(
                new[] { Path.Combine(folder, "Stairs.cs"), Path.Combine(folder, "Stairs.csproj") },
                done.Files
            );
        }

        [Fact]
        public async Task ASha256ThatIsNotTheOneTheApiGave_IsRefused_AndLeavesNothing()
        {
            _server.AddTool(
                "stairs",
                "lisp",
                "Stairs",
                "1.0",
                new[] { File("stairs.lsp", StairsLsp) },
                null,
                null,
                new Dictionary<string, byte[]> { ["stairs.lsp"] = FakeShareServer.Bytes("(princ \"tampered\")") }
            );
            ShareTool tool = await Found("stairs");
            string folder = Folder("Lisps\\stairs");

            ShareException error = await Assert.ThrowsAsync<ShareException>(() =>
                ShareInstaller.InstallAsync(Client, tool, folder)
            );

            Assert.Contains("stairs.lsp from Share does not match its SHA-256", error.Message);
            Assert.Contains("was not installed", error.Message);
            Assert.False(Directory.Exists(folder));
        }

        [Fact]
        public async Task ASha256Mismatch_OfAnUpdate_LeavesTheInstalledCopyAsItWas()
        {
            _server.AddTool("stairs", "lisp", "Stairs", "1.0", "stairs.lsp", StairsLsp);
            string folder = Folder("Lisps\\stairs");
            await ShareInstaller.InstallAsync(Client, await Found("stairs"), folder);

            var second = new FakeShareServer();
            second.AddTool(
                "stairs",
                "lisp",
                "Stairs",
                "1.1",
                new[] { File("stairs.lsp", "(princ \"two\")") },
                null,
                null,
                new Dictionary<string, byte[]> { ["stairs.lsp"] = FakeShareServer.Bytes("(princ \"evil\")") }
            );
            var client = new ShareClient(new HttpClient(second));
            ShareTool newer = await client.FindAsync("https://opencad2d.com/share/stairs");

            await Assert.ThrowsAsync<ShareException>(() => ShareInstaller.InstallAsync(client, newer, folder));

            Assert.Equal(new[] { "share.json", "stairs.lsp" }, Listing(folder));
            Assert.Equal(StairsLsp, System.IO.File.ReadAllText(Path.Combine(folder, "stairs.lsp")));
            Assert.Equal(ShareCopyState.Same, ShareCopy.Check(await Found("stairs"), folder).State);
        }

        [Fact]
        public async Task AZip_IsUnpackedIntoTheToolsFolder_WithItsSubfolders()
        {
            byte[] zip = FakeShareServer.Zip(
                FakeShareServer.Entry("requirements.txt", ""),
                FakeShareServer.Entry("plugin.json", "{ \"name\": \"Rings\" }"),
                FakeShareServer.Entry("rings/__init__.py", "pass\n")
            );
            _server.AddTool(
                "rings",
                "python",
                "Rings",
                "1.0",
                new[] { new KeyValuePair<string, byte[]>("Rings.zip", zip) },
                new[] { "RINGS" }
            );
            string folder = Folder("Pythons\\rings");

            await ShareInstaller.InstallAsync(Client, await Found("rings"), folder);

            Assert.Equal(
                new[] { "plugin.json", "requirements.txt", "rings/__init__.py", "share.json" },
                Listing(folder)
            );
            Assert.Equal("pass\n", System.IO.File.ReadAllText(Path.Combine(folder, "rings", "__init__.py")));
        }

        [Theory]
        [InlineData("..\\..\\evil.cs")]
        [InlineData("../evil.cs")]
        [InlineData("sub/../../evil.cs")]
        [InlineData("/etc/evil.cs")]
        [InlineData("C:\\evil.cs")]
        [InlineData("C:evil.cs")]
        public async Task AZipWithAPathOutOfTheFolder_IsRefused_AndLeavesNothing(string entry)
        {
            byte[] zip = FakeShareServer.Zip(
                FakeShareServer.Entry("good.lsp", StairsLsp),
                FakeShareServer.Entry(entry, "x")
            );
            _server.AddTool("bad", "lisp", "Bad", "1.0", new[] { new KeyValuePair<string, byte[]>("Bad.zip", zip) });
            ShareTool tool = await Found("bad");
            string folder = Folder("Lisps\\bad");

            ShareException error = await Assert.ThrowsAsync<ShareException>(() =>
                ShareInstaller.InstallAsync(Client, tool, folder)
            );

            Assert.Contains("path out of the tool's folder", error.Message);
            Assert.False(Directory.Exists(folder));
            Assert.False(System.IO.File.Exists(Path.Combine(_root, "evil.cs")));
            Assert.False(System.IO.File.Exists(Path.Combine(_root, "Lisps", "evil.cs")));
        }

        [Theory]
        [InlineData("tool.dll")]
        [InlineData("tool.exe")]
        [InlineData("tool.fas")]
        [InlineData("inner.zip")]
        [InlineData("noextension")]
        public async Task AZipWithAFileThatIsNotSource_IsRefused_AndLeavesNothing(string entry)
        {
            byte[] zip = FakeShareServer.Zip(
                FakeShareServer.Entry("good.lsp", StairsLsp),
                FakeShareServer.Entry(entry, "x")
            );
            _server.AddTool("bad", "lisp", "Bad", "1.0", new[] { new KeyValuePair<string, byte[]>("Bad.zip", zip) });
            ShareTool tool = await Found("bad");
            string folder = Folder("Lisps\\bad");

            ShareException error = await Assert.ThrowsAsync<ShareException>(() =>
                ShareInstaller.InstallAsync(Client, tool, folder)
            );

            Assert.Contains("is not source", error.Message);
            Assert.False(Directory.Exists(folder));
        }

        [Fact]
        public async Task AZipThatUnpacksToMoreThanSixtyFourMegabytes_IsRefused_AndLeavesNothing()
        {
            byte[] zip = FakeShareServer.Zip(FakeShareServer.Entry("big.txt", new string('0', 65 * 1024 * 1024)));
            _server.AddTool("big", "lisp", "Big", "1.0", new[] { new KeyValuePair<string, byte[]>("Big.zip", zip) });
            ShareTool tool = await Found("big");
            string folder = Folder("Lisps\\big");

            ShareException error = await Assert.ThrowsAsync<ShareException>(() =>
                ShareInstaller.InstallAsync(Client, tool, folder)
            );

            Assert.Contains("more than 64 MB", error.Message);
            Assert.False(Directory.Exists(folder));
        }

        [Theory]
        [InlineData("tool.dll")]
        [InlineData("..\\evil.lsp")]
        [InlineData("sub/evil.lsp")]
        [InlineData("share.json")]
        public async Task AFileNameThatIsNotAPlainSourceName_IsRefused_BeforeAnythingIsDownloaded(string name)
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

        [Fact]
        public async Task AnUpdate_ReplacesTheFiles_RemovesWhatTheNewVersionLacks_AndKeepsTheUsersOwnFiles()
        {
            _server.AddTool(
                "stairs",
                "lisp",
                "Stairs",
                "1.0",
                new[] { File("stairs.lsp", StairsLsp), File("old.lsp", "(princ \"old\")") }
            );
            string folder = Folder("Lisps\\stairs");
            await ShareInstaller.InstallAsync(Client, await Found("stairs"), folder);
            System.IO.File.WriteAllText(Path.Combine(folder, "mine.lsp"), "(princ \"mine\")");

            var second = new FakeShareServer();
            second.AddTool("stairs", "lisp", "Stairs", "1.1", "stairs.lsp", "(princ \"two\")");
            var client = new ShareClient(new HttpClient(second));
            ShareTool newer = await client.FindAsync("https://opencad2d.com/share/stairs");

            ShareInstalled done = await ShareInstaller.InstallAsync(client, newer, folder);

            Assert.Equal("1.0", done.PreviousVersion);
            Assert.Equal(new[] { "mine.lsp", "share.json", "stairs.lsp" }, Listing(folder));
            Assert.Equal("(princ \"two\")", System.IO.File.ReadAllText(Path.Combine(folder, "stairs.lsp")));
        }

        [Fact]
        public async Task InstallAsksNothingUntilItIsCalled_AndDownloadsEachFileOnce()
        {
            _server.AddTool(
                "stairs",
                "lisp",
                "Stairs",
                "1.0",
                new[] { File("a.lsp", "(princ 1)"), File("b.lsp", "(princ 2)") }
            );
            ShareTool tool = await Found("stairs");
            Assert.Single(_server.Requests);

            await ShareInstaller.InstallAsync(Client, tool, Folder("Lisps\\stairs"));

            Assert.Equal(3, _server.Requests.Count);
            Assert.Equal(2, _server.Requests.Count(r => r.StartsWith("/api/files/", StringComparison.Ordinal)));
        }

        // ---- On this PC

        [Fact]
        public async Task OnThisPc_IsNotOnThisPc_WhereNothingWasInstalled()
        {
            _server.AddTool("stairs", "lisp", "Stairs", "1.0", "stairs.lsp", StairsLsp);

            ShareCopy copy = ShareCopy.Check(await Found("stairs"), Folder("Lisps\\stairs"));

            Assert.Equal(ShareCopyState.NotOnThisPc, copy.State);
            Assert.Equal(string.Empty, copy.Version);
        }

        [Fact]
        public async Task OnThisPc_IsSame_WhereTheCopyHasTheLiveFilesSha256()
        {
            _server.AddTool("stairs", "lisp", "Stairs", "1.0", "stairs.lsp", StairsLsp);
            ShareTool tool = await Found("stairs");
            string folder = Folder("Lisps\\stairs");
            await ShareInstaller.InstallAsync(Client, tool, folder);

            ShareCopy copy = ShareCopy.Check(tool, folder);

            Assert.Equal(ShareCopyState.Same, copy.State);
            Assert.Equal("1.0", copy.Version);
            Assert.Equal(tool.Files[0].Sha256, copy.Sha256);
        }

        [Theory]
        [InlineData("1.1", "1.2", "Newer")]
        [InlineData("1.9", "1.10", "Newer")]
        [InlineData("1.2", "1.1", "Older")]
        [InlineData("1.10", "1.9", "Older")]
        [InlineData("2", "2.0", "Different")]
        public async Task OnThisPc_ComparesTheVersionNumbers_WhereTheSha256sDiffer(
            string installed,
            string live,
            string expected
        )
        {
            _server.AddTool("stairs", "lisp", "Stairs", installed, "stairs.lsp", StairsLsp);
            string folder = Folder("Lisps\\stairs");
            await ShareInstaller.InstallAsync(Client, await Found("stairs"), folder);

            var second = new FakeShareServer();
            second.AddTool("stairs", "lisp", "Stairs", live, "stairs.lsp", "(princ \"other\")");
            ShareTool tool = await new ShareClient(new HttpClient(second)).FindAsync(
                "https://opencad2d.com/share/stairs"
            );

            ShareCopy copy = ShareCopy.Check(tool, folder);

            Assert.Equal(expected, copy.State.ToString());
            Assert.Equal(installed, copy.Version);
            Assert.Equal(FakeShareServer.Sha(FakeShareServer.Bytes(StairsLsp)), copy.Sha256);
        }

        [Fact]
        public async Task OnThisPc_IsNotOnThisPc_WhereTheFolderIsAnotherToolsOrItsFilesAreGone()
        {
            _server.AddTool("stairs", "lisp", "Stairs", "1.0", "stairs.lsp", StairsLsp);
            _server.AddTool("rings", "lisp", "Rings", "1.0", "rings.lsp", "(princ 1)");
            string folder = Folder("Lisps\\stairs");
            await ShareInstaller.InstallAsync(Client, await Found("stairs"), folder);

            Assert.Equal(ShareCopyState.NotOnThisPc, ShareCopy.Check(await Found("rings"), folder).State);

            System.IO.File.Delete(Path.Combine(folder, "stairs.lsp"));
            Assert.Equal(ShareCopyState.NotOnThisPc, ShareCopy.Check(await Found("stairs"), folder).State);

            System.IO.File.WriteAllText(Path.Combine(folder, "share.json"), "not json");
            Assert.Equal(ShareCopyState.NotOnThisPc, ShareCopy.Check(await Found("stairs"), folder).State);
        }
    }
}
