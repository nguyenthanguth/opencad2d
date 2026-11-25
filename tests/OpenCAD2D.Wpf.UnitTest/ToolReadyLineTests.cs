using System;
using System.Diagnostics;
using System.Threading;
using GeometryHelper.Dxf;
using Xunit;

namespace OpenCAD2D.Wpf.UnitTest
{
    /// <summary>
    /// The <c>ready</c> line of a tool's process, which says what it bound, is read on the thread of the control pipe with nothing
    /// handed to the window's thread: a point typed after the keyword of a snap waits on that thread for the tool's first answer,
    /// which the tool sends after this line, so a line that needed the window's thread would hold the answer back for the whole
    /// wait (PW3-D7). A real process of a tool, and a gate the test holds as the window's thread is held.
    /// </summary>
    public sealed class ToolReadyLineTests : IClassFixture<ToolPython>
    {
        private readonly ToolPython _python;

        public ToolReadyLineTests(ToolPython python)
        {
            _python = python;
        }

        [Fact]
        public void TheReadyLineOfATool_IsReadWhileTheWindowsThreadIsBusy()
        {
            var app = new ToolTestApp();
            CadDocument document = app.Add(CadDocument.From(new DxfDatabase("AC1027"), "ready.dxf"));
            CadCommandSession session = app.SessionOf(document);

            using (var host = new CadToolHost(app, app, _python.Library, Guid.NewGuid().ToString("N").Substring(0, 8)))
            {
                CadFolderTool tool = host.Add(
                    _python.Tool(
                        "Readied",
                        "from opencad2d.cad import command, snap\n"
                            + "@command('NOTHING')\n"
                            + "def nothing(): pass\n"
                            + "@snap('CTOP')\n"
                            + "def top(request): return []\n"
                    ),
                    _python.Python,
                    new[] { "NOTHING" },
                    null
                );
                app.OnApp(() => host.AddCommandsTo(session.Registry));
                Assert.True(app.OnApp(() => session.Start("NOTHING")));

                // The window's thread is busy from here: nothing the host hands to it runs. The line is read all the same.
                lock (app.Gate)
                {
                    var watch = Stopwatch.StartNew();

                    while (tool.InterpreterId == 0 && watch.Elapsed < TimeSpan.FromSeconds(60))
                    {
                        Thread.Sleep(15);
                    }

                    Assert.NotEqual(0, tool.InterpreterId);
                    Assert.Equal(new[] { "NOTHING" }, tool.BoundCommands);
                    Assert.Equal(new[] { "CTOP" }, tool.BoundSnaps);
                }

                var wait = Stopwatch.StartNew();

                while (app.OnApp(() => session.IsRunning))
                {
                    Assert.True(wait.Elapsed < TimeSpan.FromSeconds(60), "The command did not end.");
                    Thread.Sleep(15);
                }
            }
        }
    }
}
