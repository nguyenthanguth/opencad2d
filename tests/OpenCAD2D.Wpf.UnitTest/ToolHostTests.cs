using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Threading;
using GeometryHelper.Dxf;
using GeometryHelper.Geometry;
using OpenCAD2D.Plugins;
using OpenCAD2D.Wpf;
using Xunit;
using Xunit.Abstractions;

namespace OpenCAD2D.Wpf.UnitTest
{
    /// <summary>
    /// The Python tool host (S32, S34 to S37): a tool's process started at its first command, the command a run of the drawing it
    /// is typed in, and a tool that raises, exits, hangs or is stopped by Esc taking nothing of the app with it. Every test starts
    /// real processes, of a Python the fixture makes in a temporary folder (no network, no setting of the user's).
    /// </summary>
    public sealed class ToolHostTests : IClassFixture<ToolPython>
    {
        private readonly ToolPython _python;
        private readonly ITestOutputHelper _output;

        public ToolHostTests(ToolPython python, ITestOutputHelper output)
        {
            _python = python;
            _output = output;
        }

        /// <summary>The app, a drawing with two circles and a line, and a host of tools; the host's processes end with the scene.</summary>
        private sealed class Scene : IDisposable
        {
            public readonly ToolTestApp App = new ToolTestApp();
            public readonly CadDocument Document;
            public readonly CadCommandSession Session;
            public readonly CadToolHost Host;
            public readonly List<string> Log = new List<string>();

            public Scene(ToolPython python)
            {
                Document = App.Add(TwoCirclesAndALine());
                Session = App.SessionOf(Document);
                Host = new CadToolHost(App, App, python.Library, Guid.NewGuid().ToString("N").Substring(0, 8))
                {
                    Log = line =>
                    {
                        lock (Log)
                            Log.Add(line);
                    },
                };
            }

            public int Entities => App.OnApp(() => Document.Database.ModelSpace.Count(e => !e.IsErased));

            public List<string> History => App.OnApp(() => Session.History.ToList());

            public List<string> LogLines()
            {
                lock (Log)
                {
                    return Log.ToList();
                }
            }

            public CadFolderTool Add(ToolPython python, string name, string source, params string[] commands)
            {
                CadFolderTool tool = Host.Add(python.Tool(name, source), python.Python, commands, null);
                App.OnApp(() => Host.AddCommandsTo(Session.Registry));
                return tool;
            }

            /// <summary>Types a command as the user does, and gives the history lines it wrote.</summary>
            public List<string> Run(string name, double seconds = 60)
            {
                int before = History.Count;
                Assert.True(App.OnApp(() => Session.Start(name)));
                WaitUntil(() => !App.OnApp(() => Session.IsRunning), seconds, "The command " + name + " did not end.");
                return History.Skip(before).ToList();
            }

            public void WaitUntil(Func<bool> done, double seconds, string failure)
            {
                var watch = Stopwatch.StartNew();

                while (!done())
                {
                    if (watch.Elapsed.TotalSeconds > seconds)
                    {
                        throw new TimeoutException(
                            failure
                                + " History: "
                                + string.Join(" | ", History.Skip(Math.Max(0, History.Count - 12)))
                                + " Log: "
                                + string.Join(" | ", LogLines())
                        );
                    }

                    Thread.Sleep(15);
                }
            }

            public void Dispose() => Host.Dispose();
        }

        /// <summary>A drawing with two circles and a line in model space, the layer Steel and a block of one line.</summary>
        private static CadDocument TwoCirclesAndALine()
        {
            var database = new DxfDatabase("AC1027");
            database.LayerTable.Add(new DxfLayerTableRecord { Name = "Steel", ColorIndex = 1 });
            var block = new DxfBlockTableRecord { Name = "BOLT" };
            database.BlockTable.Add(block);
            block.AppendEntity(new DxfLine(new GeoPoint3(0, 0, 0), new GeoPoint3(1, 0, 0)));
            database.ModelSpace.AppendEntity(new DxfCircle(new GeoPoint3(0, 0, 0), GeoVector3.ZAxis, 10));
            database.ModelSpace.AppendEntity(new DxfCircle(new GeoPoint3(50, 0, 0), GeoVector3.ZAxis, 10));
            database.ModelSpace.AppendEntity(new DxfLine(new GeoPoint3(0, 0, 0), new GeoPoint3(120, 80, 0)));
            database.Extmin = new GeoPoint3(-10, -10, 0);
            database.Extmax = new GeoPoint3(120, 80, 0);
            return CadDocument.From(database, "Plan.dxf");
        }

        private const string Draw =
            @"
from opencad2d.cad import active_drawing, command, write

@command('DRAWIT')
def draw_it():
    drawing = active_drawing()
    drawing.add_line((0, 100), (100, 100))
    drawing.commit()
    write('drew a line in ' + drawing.name)
    print('printed after write')
";

        /// <summary>The sample tool folder the app ships, as it is in the repository: <c>examples\Pythons\&lt;name&gt;</c>.</summary>
        private string Sample(string name) => Path.Combine(_python.Root, "examples", "Pythons", name);

        [Fact]
        public void Ghpyinfo_FromPythonsDrawingInfo_GivesTheSameLinesAsTheFileDid_ExtentsIncluded()
        {
            // The sample as it ships: the folder Pythons\DrawingInfo with a requirements.txt, an __init__.py and a plugin.json, whose
            // command is GHPYINFO. Its Extents line reads the header of the drawing over the channel (CadRemoteDocument.Extents).
            using (var scene = new Scene(_python))
            {
                string folder = Sample("DrawingInfo");
                CadPluginManifest manifest = CadPluginManifest.Read(Path.Combine(folder, "plugin.json"));
                CadFolderTool tool = scene.Host.Add(folder, _python.Python, manifest.Commands, null);
                scene.App.OnApp(() => scene.Host.AddCommandsTo(scene.Session.Registry));
                Assert.Equal(new[] { "GHPYINFO" }, tool.Commands.Select(c => c.Name));

                List<string> lines = scene.Run("GHPYINFO");

                Assert.Equal(
                    new[]
                    {
                        "Drawing: Plan.dxf in not saved yet",
                        "Current layer: 0",
                        "Model space: 3 objects",
                        "  CIRCLE   2",
                        "  LINE     1",
                        "Layers: 2, blocks: 1",
                        "Extents: (-10.00,-10.00) to (120.00,80.00)",
                    },
                    lines.Where(l => !l.StartsWith("Command:", StringComparison.Ordinal) && l != "GHPYINFO")
                );
                Assert.False(scene.App.OnApp(() => scene.Document.CanUndo));
                Assert.Equal(3, scene.Entities);
            }
        }

        [Fact]
        public void Pyhelloworld_FromPythonsHelloWorld_WritesHelloWorld_AndChangesNothing()
        {
            using (var scene = new Scene(_python))
            {
                string folder = Sample("HelloWorld");
                CadPluginManifest manifest = CadPluginManifest.Read(Path.Combine(folder, "plugin.json"));
                scene.Host.Add(folder, _python.Python, manifest.Commands, null);
                scene.App.OnApp(() => scene.Host.AddCommandsTo(scene.Session.Registry));

                List<string> lines = scene.Run("PYHELLOWORLD");

                Assert.Equal(new[] { "Hello, World!" }, lines.Where(l => l == "Hello, World!"));
                Assert.False(scene.App.OnApp(() => scene.Document.CanUndo));
                Assert.Equal(3, scene.Entities);
            }
        }

        [Fact]
        public void TheSamples_ShipWithAnEmptyRequirementsFile_AnInitPy_AndAPluginJsonThatNamesTheirCommand()
        {
            foreach (string name in new[] { "DrawingInfo", "HelloWorld" })
            {
                string folder = Sample(name);

                Assert.Empty(File.ReadAllText(Path.Combine(folder, "requirements.txt")).Trim());
                Assert.True(File.Exists(Path.Combine(folder, "__init__.py")));
                Assert.Single(CadPluginManifest.Read(Path.Combine(folder, "plugin.json")).CommandEntries);
            }
        }

        [Fact]
        public void ACommand_DrawsInOneUndoStepNamedAfterIt_AndItsPrintComesInOrderWithItsWrite()
        {
            using (var scene = new Scene(_python))
            {
                scene.Add(_python, "Draw", Draw, "DRAWIT");

                List<string> lines = scene.Run("DRAWIT");

                Assert.Equal(4, scene.Entities);
                Assert.Equal(
                    new[] { "drew a line in Plan.dxf", "printed after write" },
                    lines.Where(l =>
                        l.StartsWith("drew", StringComparison.Ordinal)
                        || l.StartsWith("printed", StringComparison.Ordinal)
                    )
                );
                Assert.Equal("DRAWIT", scene.App.OnApp(() => scene.Document.NextUndo.Name));
                scene.App.OnApp(() => scene.Document.Undo());
                Assert.Equal(3, scene.Entities);
                Assert.False(scene.App.OnApp(() => scene.Document.CanUndo));
            }
        }

        [Fact]
        public void TheFirstCommand_StartsTheProcess_AndTheNextOnesUseIt()
        {
            using (var scene = new Scene(_python))
            {
                CadFolderTool tool = scene.Add(_python, "Draw", Draw, "DRAWIT");
                Assert.False(tool.IsRunning);
                Assert.Equal(0, tool.Starts);

                var watch = Stopwatch.StartNew();
                scene.Run("DRAWIT");
                double first = watch.Elapsed.TotalMilliseconds;
                int process = tool.ProcessId;
                watch.Restart();
                scene.Run("DRAWIT");
                double second = watch.Elapsed.TotalMilliseconds;

                _output.WriteLine(
                    "The first command of a tool (the process started, opencad2d imported, the connection made, one run): {0:F0} ms; the second, in the same process: {1:F0} ms.",
                    first,
                    second
                );
                Assert.True(tool.IsRunning);
                Assert.Equal(1, tool.Starts);
                Assert.Equal(process, tool.ProcessId);
                Assert.True(tool.InterpreterId > 0);
                Assert.Equal(new[] { "DRAWIT" }, tool.BoundCommands);
                Assert.True(first < 20000, "the first command took " + first + " ms");
            }
        }

        [Fact]
        public void AToolThatRaises_LeavesTheDrawingAsItWas_WritesTheLine_AndTheNextCommandWorks()
        {
            const string source =
                @"
from opencad2d.cad import active_drawing, command

@command('BOOM')
def boom():
    drawing = active_drawing()
    drawing.add_line((0, 100), (100, 100))
    drawing.commit()
    raise ValueError('no stairs here')

@command('FINE')
def fine():
    drawing = active_drawing()
    drawing.add_line((0, 200), (100, 200))
    drawing.commit()
";

            using (var scene = new Scene(_python))
            {
                CadFolderTool tool = scene.Add(_python, "Boom", source, "BOOM", "FINE");

                List<string> lines = scene.Run("BOOM");

                Assert.Contains("BOOM: ValueError: no stairs here", lines);
                Assert.Equal(3, scene.Entities);
                Assert.False(scene.App.OnApp(() => scene.Document.CanUndo));
                Assert.Contains(scene.LogLines(), l =>
                    l.IndexOf("ValueError: no stairs here", StringComparison.Ordinal) >= 0
                );

                scene.Run("FINE");

                Assert.Equal(4, scene.Entities);
                Assert.Equal(1, tool.Starts);
            }
        }

        [Fact]
        public void AToolThatExits_CancelsItsRun_WritesTheLineWithTheExitCode_AndTheNextCommandStartsANewProcess()
        {
            const string source =
                @"
import os, sys
from opencad2d.cad import active_drawing, command

@command('LEAVE')
def leave():
    drawing = active_drawing()
    drawing.add_line((0, 100), (100, 100))
    drawing.commit()
    print('about to go', file=sys.stderr)
    os._exit(3)

@command('FINE')
def fine():
    drawing = active_drawing()
    drawing.add_line((0, 200), (100, 200))
    drawing.commit()
";

            using (var scene = new Scene(_python))
            {
                CadFolderTool tool = scene.Add(_python, "Leave", source, "LEAVE", "FINE");

                List<string> lines = scene.Run("LEAVE");

                Assert.Contains("LEAVE: the tool Leave ended unexpectedly (exit code 3).", lines);
                Assert.Contains("about to go", lines);
                Assert.Equal(3, scene.Entities);
                Assert.False(scene.App.OnApp(() => scene.Document.CanUndo));
                Assert.False(scene.App.OnApp(() => scene.Document.IsInGroup));
                scene.WaitUntil(() => !tool.IsRunning, 10, "The tool was still taken for running.");

                scene.Run("FINE");

                Assert.Equal(4, scene.Entities);
                Assert.Equal(2, tool.Starts);
            }
        }

        [Fact]
        public void Esc_InABusyLoop_EndsTheRunAndStopsTheLoopWithinFiveSeconds_InTheSameProcess()
        {
            const string source =
                @"
from opencad2d.cad import active_drawing, command, write

@command('SPIN')
def spin():
    drawing = active_drawing()
    drawing.add_line((0, 100), (100, 100))
    drawing.commit()
    write('spinning')
    while True:
        pass

@command('FINE')
def fine():
    write('fine')
";

            using (var scene = new Scene(_python))
            {
                CadFolderTool tool = scene.Add(_python, "Spin", source, "SPIN", "FINE");
                Assert.True(scene.App.OnApp(() => scene.Session.Start("SPIN")));
                scene.WaitUntil(() => scene.History.Contains("spinning"), 60, "The tool never started to spin.");
                Assert.Equal(4, scene.Entities);

                var watch = Stopwatch.StartNew();
                scene.App.OnApp(() => scene.Session.Escape());
                Assert.Equal(3, scene.Entities);
                List<string> lines = scene.Run("FINE");
                double took = watch.Elapsed.TotalSeconds;

                _output.WriteLine("Esc in while True: pass to the next command done: {0:F2} s.", took);
                Assert.Contains("fine", lines);
                Assert.True(took < 5, "the loop took " + took + " s to stop");
                Assert.Equal(1, tool.Starts);
                Assert.DoesNotContain(scene.History, l => l.IndexOf("did not stop", StringComparison.Ordinal) >= 0);
                Assert.False(scene.App.OnApp(() => scene.Document.CanUndo));
            }
        }

        [Fact]
        public void AToolEscCannotStop_IsEndedAfterFiveSeconds_WithItsLine_AndTheNextCommandStartsANewProcess()
        {
            const string source =
                @"
import threading
from opencad2d.cad import active_drawing, command, write

@command('HANG')
def hang():
    drawing = active_drawing()
    drawing.add_line((0, 100), (100, 100))
    drawing.commit()
    write('hanging')
    threading.Event().wait()

@command('FINE')
def fine():
    write('fine')
";

            using (var scene = new Scene(_python))
            {
                CadFolderTool tool = scene.Add(_python, "Hang", source, "HANG", "FINE");
                Assert.True(scene.App.OnApp(() => scene.Session.Start("HANG")));
                scene.WaitUntil(() => scene.History.Contains("hanging"), 60, "The tool never began to hang.");
                int process = tool.ProcessId;
                int interpreter = tool.InterpreterId;

                var watch = Stopwatch.StartNew();
                scene.App.OnApp(() => scene.Session.Escape());
                Assert.Equal(3, scene.Entities);
                scene.WaitUntil(
                    () => scene.History.Contains("HANG: the tool Hang did not stop within 5 s and was ended."),
                    20,
                    "The tool was not ended."
                );
                double took = watch.Elapsed.TotalSeconds;
                _output.WriteLine("A hung tool was ended after {0:F2} s.", took);

                Assert.InRange(took, 4.5, 8.0);
                Assert.False(tool.IsRunning);
                scene.WaitUntil(() => !Alive(process) && !Alive(interpreter), 10, "The process of the tool was left.");

                List<string> lines = scene.Run("FINE");

                Assert.Contains("fine", lines);
                Assert.Equal(2, tool.Starts);
                Assert.Equal(3, scene.Entities);
            }
        }

        [Fact]
        public void Cmd_InARun_IsOneUndoStepWithTheRun_AndEscTakesItBack()
        {
            const string source =
                @"
from opencad2d.cad import active_drawing, cmd, command, write

@command('LINES')
def lines():
    cmd('LINE', (0, 100), (50, 100), '')
    cmd('CIRCLE', '10,150', 5)
    active_drawing().add_line((0, 200), (50, 200))

@command('LINEWAIT')
def line_wait():
    cmd('LINE', (0, 100), (50, 100), '')
    write('drawn')
    while True:
        pass
";

            using (var scene = new Scene(_python))
            {
                scene.Add(_python, "Lines", source, "LINES", "LINEWAIT");

                List<string> lines = scene.Run("LINES");

                Assert.Equal(6, scene.Entities);
                Assert.DoesNotContain(lines, l => l.IndexOf("Resuming", StringComparison.Ordinal) >= 0);
                Assert.Equal("LINES", scene.App.OnApp(() => scene.Document.NextUndo.Name));
                scene.App.OnApp(() => scene.Document.Undo());
                Assert.Equal(3, scene.Entities);
                Assert.False(scene.App.OnApp(() => scene.Document.CanUndo));

                Assert.True(scene.App.OnApp(() => scene.Session.Start("LINEWAIT")));
                scene.WaitUntil(() => scene.History.Contains("drawn"), 60, "The tool never drew.");
                Assert.Equal(4, scene.Entities);
                scene.App.OnApp(() => scene.Session.Escape());

                Assert.Equal(3, scene.Entities);
                Assert.False(scene.App.OnApp(() => scene.Document.CanUndo));
                Assert.False(scene.App.OnApp(() => scene.Session.IsRunning));
            }
        }

        [Fact]
        public void WhatAToolWritesOnItsStandardError_GoesToTheLog_AndNotToTheCommandLine()
        {
            const string source =
                @"
import sys
from opencad2d.cad import command

@command('NOISY')
def noisy():
    print('to the log', file=sys.stderr)
    print('to the line')
";

            using (var scene = new Scene(_python))
            {
                scene.Add(_python, "Noisy", source, "NOISY");

                List<string> lines = scene.Run("NOISY");

                Assert.Contains("to the line", lines);
                Assert.DoesNotContain("to the log", lines);
                scene.WaitUntil(
                    () => scene.LogLines().Contains("Noisy: to the log"),
                    10,
                    "The log never had the line."
                );
            }
        }

        [Fact]
        public void WhatAToolWritesOnItsRawStandardOutput_IsReadByTheApp_AndGoesToTheCommandLineOfItsLastRun()
        {
            const string source =
                @"
import sys
from opencad2d.cad import command

print('loaded')

@command('RAW')
def raw():
    sys.__stdout__.write('raw line\n')
    sys.__stdout__.flush()
";

            using (var scene = new Scene(_python))
            {
                scene.Add(_python, "Raw", source, "RAW");

                scene.Run("RAW");

                scene.WaitUntil(() => scene.History.Contains("raw line"), 10, "The raw line never came.");
                scene.WaitUntil(
                    () => scene.History.Contains("loaded"),
                    10,
                    "The line printed at the import never came."
                );
            }
        }

        [Fact]
        public void APackageInThePythonsFolder_IsImportedByTheTool_AfterTheVenvsOwnPackages()
        {
            const string source =
                @"
from opencad2d.cad import command, write
from shared_stairs import RISE

@command('RISE')
def rise():
    write('rise ' + str(RISE))
";

            string pythons = Path.Combine(_python.Folder, "SharedPythons");
            Directory.CreateDirectory(Path.Combine(pythons, "shared_stairs"));
            File.WriteAllText(Path.Combine(pythons, "shared_stairs", "__init__.py"), "RISE = 175" + (char)10);

            using (var scene = new Scene(_python))
            {
                CadFolderTool tool = scene.Host.Add(
                    _python.Tool("Rise", source),
                    _python.Python,
                    new[] { "RISE" },
                    pythons
                );
                scene.App.OnApp(() => scene.Host.AddCommandsTo(scene.Session.Registry));

                List<string> lines = scene.Run("RISE");

                Assert.Contains("rise 175", lines);
                Assert.True(tool.IsRunning);
            }
        }

        [Fact]
        public void AToolThatCannotBeStarted_FailsItsCommandWithTheReason_AndTheDrawingIsAsItWas()
        {
            using (var scene = new Scene(_python))
            {
                CadFolderTool tool = scene.Host.Add(
                    _python.Tool("Draw", Draw),
                    Path.Combine(_python.Folder, "nowhere", "python.exe"),
                    new[] { "DRAWIT" },
                    null
                );
                scene.App.OnApp(() => scene.Host.AddCommandsTo(scene.Session.Registry));

                List<string> lines = scene.Run("DRAWIT", 20);

                Assert.Contains(lines, l =>
                    l.IndexOf("The tool Draw could not be started", StringComparison.Ordinal) >= 0
                );
                Assert.False(tool.IsRunning);
                Assert.Equal(3, scene.Entities);
                Assert.False(scene.App.OnApp(() => scene.Document.IsInGroup));
            }
        }

        [Fact]
        public void AnUnloadedTool_RunsItsOnUnloadFunctions_AndItsProcessLeaves()
        {
            const string source =
                @"
import sys
from opencad2d.cad import command, on_unload

@command('HELLO')
def hello():
    print('hello')

@on_unload
def bye():
    print('bye', file=sys.stderr)
";

            using (var scene = new Scene(_python))
            {
                CadFolderTool tool = scene.Add(_python, "Hello", source, "HELLO");
                scene.Run("HELLO");
                int process = tool.ProcessId;

                tool.Unload();

                scene.WaitUntil(() => !Alive(process), 20, "The process did not leave.");
                scene.WaitUntil(() => scene.LogLines().Contains("Hello: bye"), 10, "The unloader never ran.");
                scene.WaitUntil(() => !tool.IsRunning, 10, "The tool was still taken for running.");
                List<string> lines = scene.Run("HELLO", 20);
                Assert.Contains(lines, l => l.IndexOf("is unloaded", StringComparison.Ordinal) >= 0);
            }
        }

        [Fact]
        public void NoProcessIsLeft_AfterTheHostIsDisposed_ForTheJobEndsThemAll()
        {
            const string source =
                @"
from opencad2d.cad import command, write

@command('IDLE')
def idle():
    write('idle')
";
            var processes = new List<int>();

            using (var scene = new Scene(_python))
            {
                CadFolderTool first = scene.Add(_python, "One", source, "IDLE");
                CadFolderTool second = scene.Add(_python, "Two", source.Replace("IDLE", "IDLE2"), "IDLE2");
                scene.Run("IDLE");
                scene.Run("IDLE2");

                foreach (CadFolderTool tool in new[] { first, second })
                {
                    Assert.True(tool.IsRunning);
                    Assert.True(Alive(tool.ProcessId));
                    Assert.True(Alive(tool.InterpreterId));
                    processes.Add(tool.ProcessId);
                    processes.Add(tool.InterpreterId);
                }

                // Two tools, each a launcher and the interpreter it started: four processes, none of them the test's own.
                Assert.Equal(4, processes.Distinct().Count());
            }

            var watch = Stopwatch.StartNew();

            while (processes.Any(Alive))
            {
                Assert.True(
                    watch.Elapsed.TotalSeconds < 10,
                    "Processes were left: " + string.Join(", ", processes.Where(Alive))
                );
                Thread.Sleep(20);
            }

            _output.WriteLine(
                "The four processes of two tools were gone {0:F0} ms after the host was disposed.",
                watch.Elapsed.TotalMilliseconds
            );
        }

        [Fact]
        public void TheCommandLineOfAProcess_QuotesEachArgumentAsTheRuntimeReadsItBack()
        {
            Assert.Equal(
                "\"C:\\Program Files\\python.exe\" -I -m opencad2d.tool \"C:\\My Tools\\Stairs\\\\\" \"say \\\"hi\\\"\" 7",
                CadToolProcess.CommandLine(
                    "C:\\Program Files\\python.exe",
                    new[] { "-I", "-m", "opencad2d.tool", "C:\\My Tools\\Stairs\\", "say \"hi\"", "7" }
                )
            );
        }

        private static bool Alive(int id)
        {
            if (id <= 0)
            {
                return false;
            }

            try
            {
                using (Process process = Process.GetProcessById(id))
                {
                    return !process.HasExited;
                }
            }
            catch (ArgumentException)
            {
                return false;
            }
            catch (InvalidOperationException)
            {
                return false;
            }
        }
    }
}
