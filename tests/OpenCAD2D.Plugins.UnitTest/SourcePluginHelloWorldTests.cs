using System;
using System.IO;
using System.Linq;
using OpenCAD2D.Plugins;
using Xunit;

#if NET8_0_OR_GREATER
namespace OpenCAD2D.Plugins.UnitTest
{
    /// <summary>
    /// The smallest sample plugin that ships in the Plugins folder, Hello world, as C# source: the folder
    /// <c>Plugins\HelloWorld</c> with <c>HelloWorld.csproj</c> and <c>HelloWorldCommands.cs</c>, which the app compiles at start,
    /// and its command HELLOWORLD writes <c>Hello, World!</c> and changes nothing. On .NET 8 only: .NET Framework does not
    /// compile source.
    /// </summary>
    public sealed class SourcePluginHelloWorldTests : IDisposable
    {
        private readonly PluginFiles _files = new PluginFiles();

        public void Dispose() => _files.Dispose();

        /// <summary>Copies the two files of the sample, as the build of the app does, into Plugins\HelloWorld of the test folder.</summary>
        private void Install()
        {
            string sample = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "HelloWorld");
            string folder = Path.Combine(_files.PluginFolder, "HelloWorld");
            Directory.CreateDirectory(folder);
            File.Copy(Path.Combine(sample, "HelloWorld.csproj.txt"), Path.Combine(folder, "HelloWorld.csproj"));
            File.Copy(Path.Combine(sample, "HelloWorldCommands.cs.txt"), Path.Combine(folder, "HelloWorldCommands.cs"));
        }

        [Fact]
        public void ThePluginInThePluginsFolder_LoadsAtStartWithNoQuestion_AsTheHelloWorldPlugin()
        {
            Install();

            using (CadPluginManager manager = _files.Manager())
            {
                manager.ConfirmUntrusted = path => throw new InvalidOperationException("Not to be asked: " + path);
                CadPluginResult started = Assert.Single(manager.LoadAtStart());

                Assert.True(started.Succeeded, started.ToString());
                Assert.Equal("HelloWorld", started.Plugin.Name);
                Assert.Equal("1.0.0", started.Plugin.Plugin.Version);
                Assert.True(started.Plugin.IsTrusted);
                Assert.True(started.Plugin.IsSource);
                Assert.Equal(new[] { "HELLOWORLD" }, started.Plugin.Plugin.Commands.Select(c => c.Name));
            }
        }

        [Fact]
        public void Helloworld_WritesHelloWorldOnTheCommandLine_AndChangesNothing()
        {
            Install();
            CadDocument document = PluginFiles.Circles();
            var registry = CadCommandRegistry.CreateDefault();

            using (CadPluginManager manager = _files.Manager())
            {
                manager.Attach(registry);
                manager.LoadAtStart();
                CadCommandSession session = PluginFiles.Session(document, registry);

                session.Feed("HELLOWORLD");

                Assert.Equal(CadCommandOutcome.Completed, session.LastOutcome);
                Assert.Equal(new[] { "Hello, World!" }, session.History.Skip(1).Select(l => l.TrimEnd()).ToArray());
                Assert.False(document.CanUndo);
            }
        }
    }
}
#endif
