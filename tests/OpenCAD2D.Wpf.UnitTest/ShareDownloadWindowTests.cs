using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Documents;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using OpenCAD2D.Share;
using OpenCAD2D.Tests;
using Xunit;

namespace OpenCAD2D.Wpf.UnitTest
{
    /// <summary>
    /// Download from Share, the window of SHAREDOWNLOAD (W3): what Find shows, where Download puts each kind of tool, that it
    /// loads it through the loaders of Load/Unload Applications, what On this PC says, and that Share is asked only when Find or
    /// Download is pressed. A fake handler serves the API, the program's folders are a temporary one, the settings file is a
    /// temporary one, and the window is laid out offscreen: no test shows a window or touches the network.
    /// </summary>
    public sealed class ShareDownloadWindowTests : IDisposable
    {
        private const string StairsLsp = "(defun c:STAIRS () (princ \"stairs\"))\n";

        private readonly TestFolder _folder = new TestFolder();
        private readonly FakeShareServer _server = new FakeShareServer();
        private readonly List<IDisposable> _disposables = new List<IDisposable>();

        public void Dispose()
        {
            foreach (IDisposable item in _disposables)
            {
                item.Dispose();
            }

            _folder.Dispose();
        }

        private string SettingsFile => Path.Combine(_folder.FullPath, "settings.json");

        private string Program => Path.Combine(_folder.FullPath, "program");

        private static KeyValuePair<string, byte[]> File(string name, string text) =>
            new KeyValuePair<string, byte[]>(name, FakeShareServer.Bytes(text));

        /// <summary>A window's model on a settings file of its own, a drawing open, a program folder of its own and Share served by the fake.</summary>
        private MainViewModel Opened(FakeShareServer server = null, FakePythonFiles python = null)
        {
            var model = new MainViewModel { SettingsPath = SettingsFile };
            model.LoadSecurity.SecureLoad = 0;
            GlobalSettings changed = model.Settings.Clone();
            changed.SecureLoad = 0;
            model.ApplySettings(changed);
            Assert.True(
                model.Open(_folder.Write("room-" + Guid.NewGuid().ToString("N") + ".dxf", TestFiles.Room(out _, out _)))
            );
            model.Document.SupportRoots = new CadSupportFolders(Program);
            model.PythonFiles = python ?? new FakePythonFiles();
            var http = new HttpClient(server ?? _server);
            _disposables.Add(http);
            model.ShareClient = new ShareClient(http);
            return model;
        }

        private static ShareDownloadViewModel Shown(MainViewModel model)
        {
            ShareDownloadViewModel shown = null;
            model.ShowShareDownload = dialog =>
            {
                shown = dialog;
                return true;
            };
            model.StartCommand("SHAREDOWNLOAD");
            Assert.NotNull(shown);
            return shown;
        }

        private static async Task Find(ShareDownloadViewModel dialog, string slug)
        {
            dialog.Link = "https://opencad2d.com/share/" + slug;
            await dialog.FindAsync();
        }

        // ---- The command, and no call before Find

        [Fact]
        public void TheCommand_ShowsTheWindowsModel_AndShareIsAskedNothing()
        {
            _server.AddTool("stairs", "lisp", "Stairs", "1.0", "stairs.lsp", StairsLsp);
            MainViewModel model = Opened();

            ShareDownloadViewModel dialog = Shown(model);

            Assert.Empty(_server.Requests);
            Assert.Equal(string.Empty, dialog.Link);
            Assert.False(dialog.Found);
            Assert.False(dialog.IsBusy);
            Assert.False(dialog.HasResult);
            Assert.Equal("Download and load", dialog.DownloadLabel);
            Assert.False(dialog.DownloadCommand.CanExecute(null));
        }

        [Fact]
        public void TheWindowItself_AsksShareNothing_WhenItIsMadeAndLaidOut()
        {
            MainViewModel model = Opened();
            ShareDownloadViewModel dialog = Shown(model);

            OnSta(() =>
            {
                var window = new ShareDownloadWindow(dialog);
                FrameworkElement body = window.Body;
                window.Content = null;
                body.DataContext = dialog;
                Layout(body);
                window.Close();
            });

            Assert.Empty(_server.Requests);
        }

        [Fact]
        public void TheCommandWithNoWindow_SaysItCannotBeShown()
        {
            MainViewModel model = Opened();

            model.StartCommand("SHAREDOWNLOAD");

            Assert.Contains(model.History, l => l == "Download from Share cannot be shown here.");
            Assert.Empty(_server.Requests);
        }

        [Fact]
        public void BrowseShareInTheBrowser_OpensTheShareAddress_AndAsksShareNothing()
        {
            ShareDownloadViewModel dialog = Shown(Opened());
            Uri opened = null;
            dialog.OpenBrowser = address => opened = address;

            dialog.BrowseCommand.Execute(null);

            Assert.Equal("https://opencad2d.com/share", opened.AbsoluteUri);
            Assert.Empty(_server.Requests);
        }

        // ---- Find

        [Fact]
        public async Task Find_ShowsThePictureNameSummaryTypeVersionShaCommandsAndFile()
        {
            byte[] picture = FakeShareServer.Bytes("cover");
            _server.AddTool(
                "stairs",
                "csharp",
                "Stairs",
                "1.2",
                new[] { File("Stairs.cs", "class Stairs {}") },
                new[] { "STAIRS" },
                picture
            );
            ShareDownloadViewModel dialog = Shown(Opened());

            await Find(dialog, "stairs");

            Assert.True(dialog.Found);
            Assert.Equal(picture, dialog.Picture);
            Assert.Equal("Stairs", dialog.Title);
            Assert.Equal("Draws Stairs from a few numbers.", dialog.Summary);
            Assert.Equal("C# source, compiled when it loads", dialog.TypeText);
            Assert.Equal("1.2, by archi_tools", dialog.VersionText);
            string sha = FakeShareServer.Sha(FakeShareServer.Bytes("class Stairs {}"));
            Assert.Equal(sha.Substring(0, 12) + "…" + sha.Substring(54), dialog.ShaText);
            Assert.Equal("Not on this PC", dialog.OnThisPcText);
            Assert.Equal("Approved by the Share reviewers, 2026-11-20", dialog.ReviewText);
            Assert.Equal("STAIRS", dialog.CommandsText);
            Assert.Equal("Stairs.cs, 15 bytes, MIT licence", dialog.FileText);
            Assert.Equal(Path.Combine(Program, "Plugins", "stairs"), dialog.SaveTo);
            Assert.True(dialog.LoadNow);
            Assert.Equal("Download and load", dialog.DownloadLabel);
            Assert.False(dialog.HasResult);
            Assert.Equal(
                new[] { "/api/tools/stairs", "/api/files/pictures/" + FakeShareServer.Sha(picture) + ".png" },
                _server.Requests
            );
        }

        [Fact]
        public async Task AFileOfSixKilobytes_IsSaidInKilobytes_AndSeveralFilesAreListed()
        {
            _server.AddTool(
                "stairs",
                "lisp",
                "Stairs",
                "1.0",
                new[] { File("a.lsp", new string('a', 6000)), File("b.lsp", "(princ 1)") }
            );
            ShareDownloadViewModel dialog = Shown(Opened());

            await Find(dialog, "stairs");

            Assert.Equal("2 files (a.lsp, b.lsp), 6 KB, MIT licence", dialog.FileText);
            Assert.EndsWith(" (a.lsp, and 1 more)", dialog.ShaText);
        }

        [Fact]
        public async Task Find_OfALinkThatIsNoTool_SaysSo_WithoutAskingShare()
        {
            ShareDownloadViewModel dialog = Shown(Opened());
            dialog.Link = "https://example.com/not-share";

            await dialog.FindAsync();

            Assert.False(dialog.Found);
            Assert.True(dialog.ResultIsError);
            Assert.Contains("not the link of a tool on Share", dialog.Result);
            Assert.Empty(_server.Requests);
        }

        [Fact]
        public async Task Find_OfAToolShareDoesNotHave_Says404InAWord_AndClearsTheCardBefore()
        {
            _server.AddTool("stairs", "lisp", "Stairs", "1.0", "stairs.lsp", StairsLsp);
            ShareDownloadViewModel dialog = Shown(Opened());
            await Find(dialog, "stairs");
            Assert.True(dialog.Found);

            await Find(dialog, "nothing");

            Assert.False(dialog.Found);
            Assert.Equal("Share has no tool called \"nothing\".", dialog.Result);
            Assert.True(dialog.ResultIsError);
            Assert.Equal(string.Empty, dialog.Title);
            Assert.False(dialog.DownloadCommand.CanExecute(null));
        }

        [Fact]
        public async Task Find_WhereShareCannotBeReached_SaysSo()
        {
            _server.Down = new HttpRequestException("No such host is known");
            ShareDownloadViewModel dialog = Shown(Opened());

            await Find(dialog, "stairs");

            Assert.False(dialog.Found);
            Assert.StartsWith("Share could not be reached", dialog.Result);
            Assert.True(dialog.ResultIsError);
            Assert.False(dialog.IsBusy);
        }

        // ---- Download: where each kind goes, and that it loads through the loaders

        [Fact]
        public async Task AnAutoLispTool_GoesToTheLispsFolderUnderItsSlug_AndIsLoaded()
        {
            _server.AddTool("stairs", "lisp", "Stairs", "1.0", "stairs.lsp", StairsLsp, new[] { "STAIRS" });
            MainViewModel model = Opened();
            ShareDownloadViewModel dialog = Shown(model);
            await Find(dialog, "stairs");
            string folder = Path.Combine(Program, "Lisps", "stairs");
            Assert.Equal(folder, dialog.SaveTo);

            await dialog.DownloadAsync();

            Assert.Equal(StairsLsp, System.IO.File.ReadAllText(Path.Combine(folder, "stairs.lsp")));
            Assert.True(System.IO.File.Exists(Path.Combine(folder, "share.json")));
            Assert.Contains(model.LoadedLispFiles, f =>
                string.Equals(f, Path.Combine(folder, "stairs.lsp"), StringComparison.OrdinalIgnoreCase)
            );
            Assert.Equal(
                "Stairs 1.0 was saved to "
                    + folder
                    + " and is loaded; its SHA-256 matches the one on Share. Type STAIRS to use it.",
                dialog.Result
            );
            Assert.False(dialog.ResultIsError);
            Assert.True(dialog.IsDone);
            Assert.False(dialog.Found);
            Assert.Equal("Done", dialog.DownloadLabel);
            Assert.Contains(model.History, l => l == dialog.Result);
        }

        [Fact]
        public async Task APythonTool_GoesToAFolderOfItsOwnInPythons_AndIsLoadedAsPyloadLoadsIt()
        {
            byte[] zip = FakeShareServer.Zip(
                FakeShareServer.Entry("requirements.txt", ""),
                FakeShareServer.Entry("__init__.py", "pass\n")
            );
            _server.AddTool(
                "rings",
                "python",
                "Rings",
                "1.0",
                new[] { new KeyValuePair<string, byte[]>("Rings.zip", zip) },
                new[] { "RINGS" }
            );
            var python = new FakePythonFiles();
            MainViewModel model = Opened(null, python);
            ShareDownloadViewModel dialog = Shown(model);
            await Find(dialog, "rings");
            string folder = Path.Combine(Program, "Pythons", "rings");

            await dialog.DownloadAsync();

            Assert.True(System.IO.File.Exists(Path.Combine(folder, "requirements.txt")));
            Assert.True(System.IO.File.Exists(Path.Combine(folder, "__init__.py")));
            Assert.Equal(new[] { folder }, python.Loads.Select(Path.GetFullPath));
            Assert.True(dialog.IsDone);
            Assert.Contains("is loaded", dialog.Result);
        }

        [Fact]
        public async Task ACSharpTool_GoesToAFolderOfItsOwnInPlugins_AndTheLoadersAreAsked()
        {
            _server.AddTool(
                "stairs",
                "csharp",
                "Stairs",
                "1.0",
                new[] { File("Stairs.cs", "class Stairs {}"), File("Stairs.csproj", "<Project />") },
                new[] { "STAIRS" }
            );
            MainViewModel model = Opened();
            ShareDownloadViewModel dialog = Shown(model);
            await Find(dialog, "stairs");
            string folder = Path.Combine(Program, "Plugins", "stairs");

            await dialog.DownloadAsync();

            Assert.True(System.IO.File.Exists(Path.Combine(folder, "Stairs.cs")));
            Assert.True(System.IO.File.Exists(Path.Combine(folder, "Stairs.csproj")));

            // The window has no plugin manager, so the plugin loader's own refusal is what the result says: the files stay saved.
            Assert.True(dialog.ResultIsError);
            Assert.StartsWith("Stairs 1.0 was saved to " + folder + ", but it did not load: ", dialog.Result);
            Assert.Contains("No plugin manager is in use", dialog.Result);
            Assert.False(dialog.IsDone);
        }

        [Fact]
        public async Task WithLoadItNowUnticked_TheToolIsSavedAndNotLoaded()
        {
            _server.AddTool("stairs", "lisp", "Stairs", "1.0", "stairs.lsp", StairsLsp, new[] { "STAIRS" });
            MainViewModel model = Opened();
            ShareDownloadViewModel dialog = Shown(model);
            await Find(dialog, "stairs");
            dialog.LoadNow = false;
            Assert.Equal("Download", dialog.DownloadLabel);

            await dialog.DownloadAsync();

            string saved = Path.Combine(Program, "Share", "stairs");
            Assert.Equal(saved, dialog.SaveTo);
            Assert.True(System.IO.File.Exists(Path.Combine(saved, "stairs.lsp")));
            Assert.False(Directory.Exists(Path.Combine(Program, "Lisps", "stairs")));
            Assert.Empty(model.LoadedLispFiles);
            Assert.EndsWith(
                "; its SHA-256 matches the one on Share. Saved; not loaded. Load it from the Applications window.",
                dialog.Result
            );
            Assert.DoesNotContain("is loaded", dialog.Result);
        }

        // ---- The other folder of the … button

        [Fact]
        public async Task AFolderChosenWithTheDots_HoldsTheToolsOwnFolder_AndTheToolIsKeptForEveryStart()
        {
            _server.AddTool("stairs", "lisp", "Stairs", "1.0", "stairs.lsp", StairsLsp, new[] { "STAIRS" });
            MainViewModel model = Opened();
            ShareDownloadViewModel dialog = Shown(model);
            await Find(dialog, "stairs");
            string mine = Path.Combine(_folder.FullPath, "mine");
            Directory.CreateDirectory(mine);
            string asked = null;
            dialog.AskFolder = start =>
            {
                asked = start;
                return mine;
            };

            dialog.ChooseFolderCommand.Execute(null);

            Assert.Equal(Path.Combine(Program, "Lisps"), asked);
            string folder = Path.Combine(mine, "stairs");
            Assert.Equal(folder, dialog.SaveTo);

            await dialog.DownloadAsync();

            string file = Path.Combine(folder, "stairs.lsp");
            Assert.True(System.IO.File.Exists(file));
            Assert.Contains(model.LoadedLispFiles, f => string.Equals(f, file, StringComparison.OrdinalIgnoreCase));
            Assert.Contains(model.Settings.LispStartupSuite, f =>
                string.Equals(f, file, StringComparison.OrdinalIgnoreCase)
            );
        }

        [Fact]
        public async Task TheDotsCancelled_KeepTheFolderItHad()
        {
            _server.AddTool("stairs", "lisp", "Stairs", "1.0", "stairs.lsp", StairsLsp);
            ShareDownloadViewModel dialog = Shown(Opened());
            await Find(dialog, "stairs");
            string before = dialog.SaveTo;
            dialog.AskFolder = start => null;

            dialog.ChooseFolderCommand.Execute(null);

            Assert.Equal(before, dialog.SaveTo);
        }

        // ---- The checks

        [Fact]
        public async Task ASha256ThatIsNotShares_RefusesWithAClearMessage_AndLeavesNothing()
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
            MainViewModel model = Opened();
            ShareDownloadViewModel dialog = Shown(model);
            await Find(dialog, "stairs");

            await dialog.DownloadAsync();

            Assert.True(dialog.ResultIsError);
            Assert.StartsWith("stairs.lsp from Share does not match its SHA-256", dialog.Result);
            Assert.EndsWith("so it was not installed.", dialog.Result);
            Assert.False(Directory.Exists(Path.Combine(Program, "Lisps", "stairs")));
            Assert.Empty(model.LoadedLispFiles);
            Assert.False(dialog.IsDone);
            Assert.True(dialog.Found);
        }

        [Fact]
        public async Task AZipWithAPathOutOfTheFolder_IsRefused_AndLeavesNothing()
        {
            byte[] zip = FakeShareServer.Zip(
                FakeShareServer.Entry("requirements.txt", ""),
                FakeShareServer.Entry("..\\..\\evil.py", "x")
            );
            _server.AddTool(
                "rings",
                "python",
                "Rings",
                "1.0",
                new[] { new KeyValuePair<string, byte[]>("Rings.zip", zip) }
            );
            var python = new FakePythonFiles();
            ShareDownloadViewModel dialog = Shown(Opened(null, python));
            await Find(dialog, "rings");

            await dialog.DownloadAsync();

            Assert.True(dialog.ResultIsError);
            Assert.Contains("path out of the tool's folder", dialog.Result);
            Assert.False(Directory.Exists(Path.Combine(Program, "Pythons", "rings")));
            Assert.Empty(python.Loads);
        }

        // ---- On this PC

        [Fact]
        public async Task OnThisPc_SaysSame_Newer_AndOlder_BySha256_AndTheButtonSaysWhatItWillDo()
        {
            var one = new FakeShareServer();
            one.AddTool("stairs", "lisp", "Stairs", "1.1", "stairs.lsp", StairsLsp, new[] { "STAIRS" });
            MainViewModel model = Opened(one);
            ShareDownloadViewModel first = Shown(model);
            await Find(first, "stairs");
            await first.DownloadAsync();
            string sha = FakeShareServer.Sha(FakeShareServer.Bytes(StairsLsp)).Substring(0, 12);

            // The same file: nothing to update.
            ShareDownloadViewModel same = Shown(model);
            await Find(same, "stairs");
            Assert.Equal("1.1 (SHA-256 " + sha + "…), the same file", same.OnThisPcText);
            Assert.Equal("Download again and load", same.DownloadLabel);

            // Share has a later file: Update.
            var two = new FakeShareServer();
            two.AddTool(
                "stairs",
                "lisp",
                "Stairs",
                "1.2",
                "stairs.lsp",
                "(defun c:STAIRS () (princ \"two\"))\n",
                new[] { "STAIRS" }
            );
            model.ShareClient = new ShareClient(new HttpClient(two));
            ShareDownloadViewModel newer = Shown(model);
            await Find(newer, "stairs");
            Assert.Equal("1.1 (SHA-256 " + sha + "…), so this is a newer file", newer.OnThisPcText);
            Assert.Equal("Update to 1.2 and load", newer.DownloadLabel);
            newer.LoadNow = false;
            // Unticked, the tool goes to Share beside the app, where none is installed: a plain Download.
            Assert.Equal("Download", newer.DownloadLabel);
            Assert.Equal("Not on this PC", newer.OnThisPcText);

            // Share has an earlier file than the copy.
            var zero = new FakeShareServer();
            zero.AddTool("stairs", "lisp", "Stairs", "1.0", "stairs.lsp", "(princ \"old\")", new[] { "STAIRS" });
            model.ShareClient = new ShareClient(new HttpClient(zero));
            ShareDownloadViewModel older = Shown(model);
            await Find(older, "stairs");
            Assert.Equal("1.1 (SHA-256 " + sha + "…), so this is an older file", older.OnThisPcText);
            Assert.Equal("Replace with 1.0 and load", older.DownloadLabel);
        }

        [Fact]
        public async Task AnUpdate_SaysItReplacedTheVersionItHad_AndOnThisPcIsTheSameAfterwards()
        {
            var one = new FakeShareServer();
            one.AddTool("stairs", "lisp", "Stairs", "1.1", "stairs.lsp", StairsLsp, new[] { "STAIRS" });
            MainViewModel model = Opened(one);
            ShareDownloadViewModel first = Shown(model);
            await Find(first, "stairs");
            await first.DownloadAsync();

            var two = new FakeShareServer();
            two.AddTool(
                "stairs",
                "lisp",
                "Stairs",
                "1.2",
                "stairs.lsp",
                "(defun c:STAIRS () (princ \"two\"))\n",
                new[] { "STAIRS" }
            );
            model.ShareClient = new ShareClient(new HttpClient(two));
            ShareDownloadViewModel update = Shown(model);
            await Find(update, "stairs");

            await update.DownloadAsync();

            Assert.Equal(
                "Stairs 1.2 replaced 1.1 and is loaded; its SHA-256 matches the one on Share. Type STAIRS to use it.",
                update.Result
            );
            Assert.Equal(
                "(defun c:STAIRS () (princ \"two\"))\n",
                System.IO.File.ReadAllText(Path.Combine(Program, "Lisps", "stairs", "stairs.lsp"))
            );
            Assert.StartsWith("1.2 (SHA-256 ", update.OnThisPcText);
            Assert.EndsWith(", the same file", update.OnThisPcText);
        }

        [Fact]
        public async Task DoneCloses_AndDownloadTwiceInARow_DoesNotAskShareAgain()
        {
            _server.AddTool("stairs", "lisp", "Stairs", "1.0", "stairs.lsp", StairsLsp);
            ShareDownloadViewModel dialog = Shown(Opened());
            await Find(dialog, "stairs");
            int closes = 0;
            dialog.CloseRequested += (s, e) => closes++;
            await dialog.DownloadAsync();
            int asked = _server.Requests.Count;

            await dialog.DownloadAsync();
            dialog.DownloadCommand.Execute(null);

            Assert.Equal(asked, _server.Requests.Count);
            Assert.Equal(1, closes);
        }

        // ---- The window

        [Fact]
        public async Task TheWindow_ShowsTheCoverPicture_TheDetails_AndTheButtonsOfTheDesign()
        {
            byte[] png = Png();
            _server.AddTool(
                "stairs",
                "csharp",
                "Stairs",
                "1.2",
                new[] { File("Stairs.cs", "class Stairs {}") },
                new[] { "STAIRS" },
                png
            );
            ShareDownloadViewModel dialog = Shown(Opened());
            await Find(dialog, "stairs");
            Uri opened = null;
            dialog.OpenBrowser = address => opened = address;

            OnSta(() =>
            {
                var window = new ShareDownloadWindow(dialog);
                FrameworkElement body = window.Body;
                window.Content = null;
                body.DataContext = dialog;
                dialog.OpenBrowser = address => opened = address;
                window.Content = null;
                Layout(body);

                List<DependencyObject> all = Descendants(body).ToList();
                Assert.Equal(Visibility.Visible, ((FrameworkElement)Named(all, "Details")).Visibility);
                Assert.Equal("Stairs", Assert.Single(all.OfType<TextBlock>(), t => t.Text == "Stairs").Text);
                Assert.Contains(all.OfType<TextBlock>(), t => t.Text == "Not on this PC");
                Assert.Contains(all.OfType<TextBlock>(), t => t.Text == "Approved by the Share reviewers, 2026-11-20");
                Assert.Equal("Download and load", ((Button)Named(all, "DownloadButton")).Content);
                Assert.Equal("Close", ((Button)Named(all, "CloseButton")).Content);
                Assert.Equal("Find", ((Button)Named(all, "FindButton")).Content);
                Assert.Equal(Path.Combine(Program, "Plugins", "stairs"), ((TextBox)Named(all, "SaveToBox")).Text);
                Assert.True(((TextBox)Named(all, "SaveToBox")).IsReadOnly);
                Assert.True(((CheckBox)Named(all, "LoadNowBox")).IsChecked);
                Assert.Equal("Load it now, and at every start", ((CheckBox)Named(all, "LoadNowBox")).Content);
                Assert.Equal("https://opencad2d.com/share/stairs", ((TextBox)Named(all, "LinkBox")).Text);
                Assert.Equal(Visibility.Collapsed, ((FrameworkElement)Named(all, "ResultLine")).Visibility);

                Hyperlink link = all.OfType<Hyperlink>().Single();
                link.Command.Execute(null);
            });

            Assert.Equal("https://opencad2d.com/share", opened.AbsoluteUri);
        }

        [Fact]
        public async Task TheWindow_DrawsTheCoverPictureOfTheTool()
        {
            _server.AddTool("stairs", "lisp", "Stairs", "1.0", new[] { File("stairs.lsp", StairsLsp) }, null, Png());
            ShareDownloadViewModel dialog = Shown(Opened());
            await Find(dialog, "stairs");

            OnSta(() =>
            {
                var window = new ShareDownloadWindow(dialog);
                window.Content = null;
                Image cover = Descendants(window.Body).OfType<Image>().Single(i => i.Name == "Cover");
                var picture = Assert.IsAssignableFrom<BitmapSource>(cover.Source);
                Assert.Equal(2, picture.PixelWidth);
                window.Close();
            });
        }

        [Fact]
        public async Task AFailedFind_ShowsTheReasonInTheResultLine_InRed_AndHidesTheCard()
        {
            ShareDownloadViewModel dialog = Shown(Opened());
            await Find(dialog, "nothing");

            OnSta(() =>
            {
                var window = new ShareDownloadWindow(dialog);
                FrameworkElement body = window.Body;
                window.Content = null;
                body.DataContext = dialog;
                Layout(body);
                List<DependencyObject> all = Descendants(body).ToList();

                Assert.Equal(Visibility.Collapsed, ((FrameworkElement)Named(all, "Details")).Visibility);
                Assert.Equal(Visibility.Visible, ((FrameworkElement)Named(all, "ResultLine")).Visibility);
                Assert.Equal("Share has no tool called \"nothing\".", ((TextBlock)Named(all, "ResultText")).Text);
                Color ground = ((SolidColorBrush)((Border)Named(all, "ResultLine")).Background).Color;
                Assert.True(ground.R > ground.G, "The error line is reddish: " + ground);
            });
        }

        // ---- Nothing runs unless the box is ticked

        [Fact]
        public async Task TheFolderFollowsTheBox_LispsBeside_WhenTicked_AndShareBeside_WhenNot()
        {
            _server.AddTool("stairs", "csharp", "Stairs", "1.0", new[] { File("Stairs.cs", "class Stairs {}") });
            ShareDownloadViewModel dialog = Shown(Opened());
            await Find(dialog, "stairs");
            Assert.Equal(Path.Combine(Program, "Plugins", "stairs"), dialog.SaveTo);

            dialog.LoadNow = false;
            Assert.Equal(Path.Combine(Program, "Share", "stairs"), dialog.SaveTo);

            dialog.LoadNow = true;
            Assert.Equal(Path.Combine(Program, "Plugins", "stairs"), dialog.SaveTo);
        }

        [Fact]
        public async Task AFolderPickedWithTheDots_StaysWhateverTheBoxSays_AndUntickedItIsSavedThereNotLoaded()
        {
            _server.AddTool("stairs", "lisp", "Stairs", "1.0", "stairs.lsp", StairsLsp, new[] { "STAIRS" });
            MainViewModel model = Opened();
            ShareDownloadViewModel dialog = Shown(model);
            await Find(dialog, "stairs");
            string mine = Path.Combine(_folder.FullPath, "mine");
            Directory.CreateDirectory(mine);
            dialog.AskFolder = start => mine;
            dialog.ChooseFolderCommand.Execute(null);

            dialog.LoadNow = false;
            Assert.Equal(Path.Combine(mine, "stairs"), dialog.SaveTo);
            await dialog.DownloadAsync();

            Assert.True(System.IO.File.Exists(Path.Combine(mine, "stairs", "stairs.lsp")));
            Assert.Empty(model.LoadedLispFiles);
            Assert.Empty(model.Settings.LispStartupSuite);
            Assert.False(Directory.Exists(Path.Combine(Program, "Lisps", "stairs")));
        }

        [Fact]
        public async Task ANewFind_GoesBackToTheFolderOfTheKind_ForgettingAFolderPicked()
        {
            _server.AddTool("stairs", "lisp", "Stairs", "1.0", "stairs.lsp", StairsLsp);
            ShareDownloadViewModel dialog = Shown(Opened());
            await Find(dialog, "stairs");
            dialog.AskFolder = start => _folder.FullPath;
            dialog.ChooseFolderCommand.Execute(null);

            await Find(dialog, "stairs");

            Assert.Equal(Path.Combine(Program, "Lisps", "stairs"), dialog.SaveTo);
        }

        // ---- The user's own work

        [Fact]
        public async Task AChangedFile_ShowsChangedOnThisPc_AndDownloadAsksFirst_AndNoLeavesItAsItWas()
        {
            _server.AddTool("stairs", "lisp", "Stairs", "1.0", "stairs.lsp", StairsLsp, new[] { "STAIRS" });
            MainViewModel model = Opened();
            ShareDownloadViewModel first = Shown(model);
            await Find(first, "stairs");
            await first.DownloadAsync();
            string file = Path.Combine(Program, "Lisps", "stairs", "stairs.lsp");
            System.IO.File.AppendAllText(file, "; mine\n");

            ShareDownloadViewModel dialog = Shown(model);
            await Find(dialog, "stairs");
            string question = null;
            dialog.AskConfirm = text =>
            {
                question = text;
                return false;
            };
            int asked = _server.Requests.Count;

            Assert.StartsWith("Changed on this PC: stairs.lsp (installed as 1.0 (SHA-256 ", dialog.OnThisPcText);
            Assert.Equal("Replace with 1.0 and load", dialog.DownloadLabel);

            await dialog.DownloadAsync();

            Assert.Contains("stairs.lsp is not as Share's install left it", question);
            Assert.Contains("name>.local-", question);
            Assert.Equal(StairsLsp + "; mine\n", System.IO.File.ReadAllText(file));
            Assert.Equal(asked, _server.Requests.Count);
            Assert.StartsWith("Nothing was changed", dialog.Result);
            Assert.False(dialog.ResultIsError);
            Assert.False(dialog.IsDone);
        }

        [Fact]
        public async Task AChangedFile_WithNoQuestionHook_CountsAsNo()
        {
            _server.AddTool("stairs", "lisp", "Stairs", "1.0", "stairs.lsp", StairsLsp);
            MainViewModel model = Opened();
            ShareDownloadViewModel first = Shown(model);
            await Find(first, "stairs");
            await first.DownloadAsync();
            string file = Path.Combine(Program, "Lisps", "stairs", "stairs.lsp");
            System.IO.File.WriteAllText(file, "(princ \"mine\")");
            ShareDownloadViewModel dialog = Shown(model);
            await Find(dialog, "stairs");

            await dialog.DownloadAsync();

            Assert.Equal("(princ \"mine\")", System.IO.File.ReadAllText(file));
            Assert.StartsWith("Nothing was changed", dialog.Result);
        }

        [Fact]
        public async Task AChangedFile_WithYes_IsReplaced_AndAGoodCopyIsKeptAndSaid()
        {
            _server.AddTool("stairs", "lisp", "Stairs", "1.0", "stairs.lsp", StairsLsp, new[] { "STAIRS" });
            MainViewModel model = Opened();
            ShareDownloadViewModel first = Shown(model);
            await Find(first, "stairs");
            await first.DownloadAsync();
            string folder = Path.Combine(Program, "Lisps", "stairs");
            System.IO.File.WriteAllText(Path.Combine(folder, "stairs.lsp"), "(princ \"mine\")");
            ShareDownloadViewModel dialog = Shown(model);
            await Find(dialog, "stairs");
            dialog.AskConfirm = text => true;

            await dialog.DownloadAsync();

            string kept = "stairs.lsp.local-" + DateTime.Now.ToString("yyyy-MM-dd");
            Assert.Equal(StairsLsp, System.IO.File.ReadAllText(Path.Combine(folder, "stairs.lsp")));
            Assert.Equal("(princ \"mine\")", System.IO.File.ReadAllText(Path.Combine(folder, kept)));
            Assert.Contains("Your changes were kept as " + kept + ".", dialog.Result);
            Assert.True(dialog.IsDone);
        }

        [Fact]
        public async Task AnUnchangedCopy_IsReplacedWithNoQuestion()
        {
            _server.AddTool("stairs", "lisp", "Stairs", "1.0", "stairs.lsp", StairsLsp);
            MainViewModel model = Opened();
            ShareDownloadViewModel first = Shown(model);
            await Find(first, "stairs");
            await first.DownloadAsync();
            ShareDownloadViewModel dialog = Shown(model);
            await Find(dialog, "stairs");
            bool asked = false;
            dialog.AskConfirm = text =>
            {
                asked = true;
                return false;
            };

            await dialog.DownloadAsync();

            Assert.False(asked);
            Assert.True(dialog.IsDone);
        }

        // ---- Whatever goes wrong is a sentence

        [Fact]
        public async Task Find_WithAnExceptionOfAnyKind_EndsAsASentence_AndTheTaskDoesNotFault()
        {
            _server.AddTool("stairs", "lisp", "Stairs", "1.0", "stairs.lsp", StairsLsp);
            ShareDownloadViewModel dialog = Shown(Opened());
            await Find(dialog, "stairs");
            Assert.True(dialog.Found);
            _server.Down = new InvalidOperationException("boom");

            Task finding = Find(dialog, "stairs");
            await finding;

            Assert.Equal(TaskStatus.RanToCompletion, finding.Status);
            Assert.False(dialog.Found);
            Assert.Equal(string.Empty, dialog.Title);
            Assert.Equal("That tool could not be read from Share (boom).", dialog.Result);
            Assert.True(dialog.ResultIsError);
            Assert.False(dialog.IsBusy);
        }

        [Fact]
        public async Task Find_OfAnAnswerForAnotherTool_ShowsNothing()
        {
            _server.AddTool("rings", "lisp", "Rings", "1.0", "rings.lsp", "(princ 1)");
            ShareDownloadViewModel dialog = Shown(Opened());
            dialog.Link = "https://opencad2d.com/share/stairs";
            _server.AddRaw(
                "stairs",
                "{\"slug\":\"rings\",\"name\":\"Rings\",\"kind\":\"lisp\",\"version\":{\"number\":\"1.0\"},\"files\":[]}"
            );

            await dialog.FindAsync();

            Assert.False(dialog.Found);
            Assert.Contains("not the tool that was asked for", dialog.Result);
        }

        [Fact]
        public async Task Download_WithAnExceptionOfAnyKind_EndsAsASentence()
        {
            _server.AddTool("stairs", "lisp", "Stairs", "1.0", "stairs.lsp", StairsLsp);
            ShareDownloadViewModel dialog = Shown(Opened());
            await Find(dialog, "stairs");
            _server.Down = new InvalidOperationException("boom");

            Task downloading = dialog.DownloadAsync();
            await downloading;

            Assert.Equal(TaskStatus.RanToCompletion, downloading.Status);
            Assert.Equal("The tool could not be installed (boom).", dialog.Result);
            Assert.True(dialog.ResultIsError);
            Assert.False(dialog.IsBusy);
        }

        // ---- Close cancels

        [Fact]
        public async Task Cancel_StopsAFindThatWaits_WithNoSentence()
        {
            _server.Hang = true;
            ShareDownloadViewModel dialog = Shown(Opened());
            dialog.Link = "https://opencad2d.com/share/stairs";

            Task finding = dialog.FindAsync();
            dialog.Cancel();
            await finding;

            Assert.False(dialog.Found);
            Assert.False(dialog.HasResult);
            Assert.False(dialog.IsBusy);
        }

        [Fact]
        public async Task Cancel_DuringADownload_InstallsNothing_AndLoadsNothing()
        {
            _server.AddTool("stairs", "lisp", "Stairs", "1.0", "stairs.lsp", StairsLsp, new[] { "STAIRS" });
            MainViewModel model = Opened();
            ShareDownloadViewModel dialog = Shown(model);
            await Find(dialog, "stairs");
            _server.OnRequest = path =>
            {
                if (path.StartsWith("/api/files/", StringComparison.Ordinal))
                {
                    dialog.Cancel();
                }
            };

            await dialog.DownloadAsync();

            Assert.False(Directory.Exists(Path.Combine(Program, "Lisps", "stairs")));
            Assert.Empty(model.LoadedLispFiles);
            Assert.False(dialog.HasResult);
            Assert.False(dialog.IsDone);
        }

        [Fact]
        public async Task AfterCancel_FindAndDownloadAskNothing()
        {
            _server.AddTool("stairs", "lisp", "Stairs", "1.0", "stairs.lsp", StairsLsp);
            ShareDownloadViewModel dialog = Shown(Opened());
            await Find(dialog, "stairs");
            dialog.Cancel();
            int asked = _server.Requests.Count;

            await Find(dialog, "stairs");
            await dialog.DownloadAsync();

            Assert.Equal(asked, _server.Requests.Count);
        }

        // ---- Helpers

        private static byte[] Png()
        {
            byte[] bytes = null;
            OnSta(() =>
            {
                BitmapSource source = BitmapSource.Create(
                    2,
                    2,
                    96,
                    96,
                    PixelFormats.Bgr24,
                    null,
                    new byte[2 * 2 * 3 + 2 * 2],
                    8
                );
                var encoder = new PngBitmapEncoder();
                encoder.Frames.Add(BitmapFrame.Create(source));

                using (var stream = new MemoryStream())
                {
                    encoder.Save(stream);
                    bytes = stream.ToArray();
                }
            });
            return bytes;
        }

        private static DependencyObject Named(IEnumerable<DependencyObject> all, string name) =>
            all.OfType<FrameworkElement>().Single(e => e.Name == name);

        private static void Layout(FrameworkElement element)
        {
            element.Measure(new Size(760, 900));
            element.Arrange(new Rect(0, 0, 760, 900));
            element.UpdateLayout();
        }

        private static IEnumerable<DependencyObject> Descendants(DependencyObject root)
        {
            var stack = new Stack<DependencyObject>();
            stack.Push(root);

            while (stack.Count > 0)
            {
                DependencyObject next = stack.Pop();
                yield return next;
                var children = new List<DependencyObject>();

                foreach (object child in LogicalTreeHelper.GetChildren(next))
                {
                    if (child is DependencyObject d)
                    {
                        children.Add(d);
                    }
                }

                if (next is Visual || next is System.Windows.Media.Media3D.Visual3D)
                {
                    for (int i = 0; i < VisualTreeHelper.GetChildrenCount(next); i++)
                    {
                        DependencyObject c = VisualTreeHelper.GetChild(next, i);

                        if (!children.Contains(c))
                        {
                            children.Add(c);
                        }
                    }
                }

                children.Reverse();

                foreach (DependencyObject c in children)
                {
                    stack.Push(c);
                }
            }
        }

        private static void OnSta(Action test)
        {
            Exception failure = null;
            var thread = new Thread(() =>
            {
                try
                {
                    _ = System.IO.Packaging.PackUriHelper.UriSchemePack;
                    test();
                }
                catch (Exception e)
                {
                    failure = e;
                }
            });
            thread.SetApartmentState(ApartmentState.STA);
            thread.Start();
            thread.Join();

            if (failure != null)
            {
                System.Runtime.ExceptionServices.ExceptionDispatchInfo.Capture(failure).Throw();
            }
        }
    }
}
