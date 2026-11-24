using System;
using System.Diagnostics;
using System.IO;
using System.IO.Packaging;
using System.Threading;
using System.Windows.Media.Imaging;
using System.Windows.Threading;
using Xunit;

namespace OpenCAD2D.Wpf.UnitTest
{
    /// <summary>
    /// The presenter's own app, OpenCAD2D: its window named after it, and its screenshot mode run as a process on a
    /// room built in code, drawn with no window shown.
    /// </summary>
    public sealed class AppHostTests
    {
        [Fact]
        public void TheAppWindow_IsNamedAfterTheApp_InItsTitleAndItsWorkspace()
        {
            using (var folder = new TestFolder())
            {
                string room = folder.Write("room.dxf", TestFiles.Room(out _, out _));
                string title = null;
                string workspaceName = null;
                string defaultName = null;

                OnSta(() =>
                {
                    _ = PackUriHelper.UriSchemePack;
                    var model = new MainViewModel();
                    Assert.True(model.Open(room));
                    var window = new CadAppWindow(model, "OpenCAD2D");

                    // The bindings are let run, as they run before a window shown is drawn.
                    Dispatcher.CurrentDispatcher.Invoke(() => { }, DispatcherPriority.Background);
                    title = window.Title;
                    workspaceName = window.WorkspaceView.AppName;
                    defaultName = new CadWorkspace().AppName;
                });

                Assert.Equal("OpenCAD2D  room.dxf", title);
                Assert.Equal("OpenCAD2D", workspaceName);
                Assert.Equal("GeometryHelper Drawing", defaultName);
            }
        }

        [Fact]
        public void TheDxfAppsScreenshot_DrawsTheRoomIntoAPng_AndRefusesADialogItDoesNotKnow()
        {
            using (var folder = new TestFolder())
            {
                string room = folder.Write("room.dxf", TestFiles.Room(out _, out _));
                string png = Path.Combine(folder.FullPath, "room.png");

                (int code, string output) = RunApp("--screenshot", png, room, "--size", "1200", "800");
                Assert.Equal(0, code);
                Assert.Contains("(1200 by 800)", output);
                var frame = BitmapFrame.Create(new Uri(png), BitmapCreateOptions.None, BitmapCacheOption.OnLoad);
                Assert.Equal(1200, frame.PixelWidth);
                Assert.Equal(800, frame.PixelHeight);

                // A dialog of GeometryHelper Drawing's is not one of this app's.
                (int refused, string why) = RunApp(
                    "--screenshot",
                    Path.Combine(folder.FullPath, "update.png"),
                    "--dialog",
                    "update"
                );
                Assert.Equal(2, refused);
                Assert.Contains("--dialog update: no such dialog.", why);
                Assert.False(File.Exists(Path.Combine(folder.FullPath, "update.png")));
            }
        }

        [Fact]
        public void TheScreenshotOfASourcePlugin_KeepsItsBuildInItsOwnFolder_NeverTheUsersPluginCache()
        {
            // A name no other run has used, so that a folder of it in the user's cache can only be this test's.
            string name = "StairsShot" + Guid.NewGuid().ToString("N");
            string userCache = Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                "OpenCAD2D",
                "PluginCache",
                name
            );
            string source =
                "using System.Collections.Generic;\n"
                + "using OpenCAD2D;\n"
                + "using OpenCAD2D.Plugins;\n"
                + "\n"
                + "namespace Stairs\n"
                + "{\n"
                + "    public sealed class StairsCommands\n"
                + "    {\n"
                + "        [CadCommand(\"STAIRS\")]\n"
                + "        public IEnumerable<CadPrompt> Draw(CadCommandContext c)\n"
                + "        {\n"
                + "            c.Write(\"Steps: \" + (int)System.Math.Ceiling(3000.0 / 175.0));\n"
                + "            yield break;\n"
                + "        }\n"
                + "    }\n"
                + "}\n";

            try
            {
                using (var folder = new TestFolder())
                {
                    string room = folder.Write("room.dxf", TestFiles.Room(out _, out _));
                    string png = Path.Combine(folder.FullPath, "stairs.png");
                    string plugin = folder.WriteText(name + ".cs", source);

                    (int code, string output) = RunApp(
                        "--screenshot",
                        png,
                        room,
                        "--plugin",
                        plugin,
                        "--run",
                        "STAIRS"
                    );
                    Assert.True(code == 0, output);
                    Assert.True(File.Exists(png));
                    Assert.False(Directory.Exists(userCache), userCache + " was written by a screenshot.");

                    // A source that does not compile exits with 2 and the error on the output.
                    string broken = folder.WriteText(
                        name + "b.cs",
                        source.Replace("            c.Write(", "            x = 1;\n            c.Write(")
                    );
                    (int failed, string why) = RunApp("--screenshot", png, "--plugin", broken);
                    Assert.Equal(2, failed);
                    Assert.Contains(
                        name + "b.cs(12,13): error CS0103: The name 'x' does not exist in the current context",
                        why
                    );
                    Assert.False(Directory.Exists(userCache + "b"), userCache + "b was written by a screenshot.");
                }
            }
            finally
            {
                foreach (string left in new[] { userCache, userCache + "b" })
                {
                    if (Directory.Exists(left))
                    {
                        Directory.Delete(left, true);
                    }
                }
            }
        }

        [Fact]
        public void TheScreenshotOfAPlugin_KeepsWhatItRemembersInItsOwnFolder_NeverTheUsersSettings()
        {
            using (var folder = new TestFolder())
            {
                string room = folder.Write("room.dxf", TestFiles.Room(out _, out _));
                string png = Path.Combine(folder.FullPath, "plugin.png");
                string plugin = Path.Combine(folder.FullPath, "OpenCAD2D.SamplePlugin.dll");
                File.Copy(
                    Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "SamplePlugin", "OpenCAD2D.SamplePlugin.dll"),
                    plugin
                );

                // The screenshot makes the Application of its process and never closes it, so it runs in a domain of its own, which
                // is unloaded after it; there the file a new view model keeps its settings in stands for the user's, a file of this
                // test's own, so that nothing of the real user's is ever at risk.
                string users = Path.Combine(folder.FullPath, "users", "settings.json");
                string own = CadAppHost.ScreenshotSettingsPath;
                int code = -1;

                try
                {
                    OnSta(() =>
                    {
                        AppDomain domain = AppDomain.CreateDomain(
                            "screenshot",
                            null,
                            new AppDomainSetup
                            {
                                ApplicationBase = AppDomain.CurrentDomain.BaseDirectory,
                                ConfigurationFile = AppDomain.CurrentDomain.SetupInformation.ConfigurationFile,
                            }
                        );

                        try
                        {
                            domain.SetData("folder", folder.FullPath);
                            domain.SetData("users", users);
                            domain.SetData("args", new[] { "--screenshot", png, room, "--plugin", plugin });
                            domain.DoCallBack(ScreenshotInThisDomain);
                            code = (int)domain.GetData("code");
                        }
                        finally
                        {
                            AppDomain.Unload(domain);
                        }
                    });

                    Assert.Equal(0, code);
                    Assert.True(File.Exists(png));
                    Assert.False(File.Exists(users), "The screenshot wrote the settings of the user: " + users);

                    // The plugin is still remembered, in the file of the run's own.
                    Assert.True(File.Exists(own), own);
                    Assert.Equal(
                        new[] { plugin },
                        GlobalSettings.Load(own).KnownApplications.Keys,
                        StringComparer.OrdinalIgnoreCase
                    );
                }
                finally
                {
                    if (Directory.Exists(Path.GetDirectoryName(own)))
                    {
                        Directory.Delete(Path.GetDirectoryName(own), true);
                    }
                }
            }
        }

        [Fact]
        public void OnlyTheWindowCountsAsAStart_SoAScreenshotOrARegistrationLeavesTheUsersFoldersAlone()
        {
            Assert.True(CadAppHost.ShowsWindow(null));
            Assert.True(CadAppHost.ShowsWindow(new string[0]));
            Assert.True(CadAppHost.ShowsWindow(new[] { @"D:\plans\room.dxf" }));
            Assert.False(CadAppHost.ShowsWindow(new[] { "--screenshot", "out.png" }));
            Assert.False(CadAppHost.ShowsWindow(new[] { "--SCREENSHOT", "out.png" }));
            Assert.False(CadAppHost.ShowsWindow(new[] { "--register" }));
            Assert.False(CadAppHost.ShowsWindow(new[] { "--unregister" }));
        }

        /// <summary>Runs the screenshot with the folders and the arguments the domain was given, and leaves its exit code in the domain.</summary>
        private static void ScreenshotInThisDomain()
        {
            AppDomain domain = AppDomain.CurrentDomain;
            MainViewModel.DefaultSettingsPath = (string)domain.GetData("users");
            var host = new CadAppHost("OpenCAD2D", (string)domain.GetData("folder"));
            domain.SetData("code", host.Run((string[])domain.GetData("args")));
        }

        /// <summary>Runs the app built beside this test's build, the same configuration, and gives its exit code and its output.</summary>
        internal static (int Code, string Output) RunApp(params string[] args)
        {
            var bin = new DirectoryInfo(AppDomain.CurrentDomain.BaseDirectory.TrimEnd(Path.DirectorySeparatorChar));
            string configuration = bin.Parent.Name;
            string root = bin.Parent.Parent.Parent.Parent.Parent.FullName;
            string exe = Path.Combine(
                root,
                "src",
                "OpenCAD2D.App",
                "bin",
                configuration,
                "net8.0-windows",
                "OpenCAD2D.exe"
            );
            Assert.True(File.Exists(exe), exe);
            var start = new ProcessStartInfo(exe)
            {
                Arguments = string.Join(" ", Array.ConvertAll(args, a => "\"" + a + "\"")),
                UseShellExecute = false,
                CreateNoWindow = true,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
            };

            using (Process process = Process.Start(start))
            {
                var error = process.StandardError.ReadToEndAsync();
                string output = process.StandardOutput.ReadToEnd();

                if (!process.WaitForExit(120000))
                {
                    process.Kill();
                    throw new TimeoutException("The app did not exit in two minutes.");
                }

                return (process.ExitCode, output + error.Result);
            }
        }

        private static void OnSta(Action action)
        {
            Exception failure = null;
            var thread = new Thread(() =>
            {
                try
                {
                    action();
                }
                catch (Exception ex)
                {
                    failure = ex;
                }
            });
            thread.SetApartmentState(ApartmentState.STA);
            thread.Start();
            thread.Join();

            if (failure != null)
            {
                throw new InvalidOperationException("The STA thread failed.", failure);
            }
        }
    }
}
