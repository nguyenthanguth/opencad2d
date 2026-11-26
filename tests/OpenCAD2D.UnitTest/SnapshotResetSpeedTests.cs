using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.Linq;
using GeometryHelper.Dxf;
using GeometryHelper.Geometry;
using Xunit;
using Xunit.Abstractions;
using static OpenCAD2D.UnitTest.SpatialDrawings;

namespace OpenCAD2D.UnitTest
{
    /// <summary>
    /// The speed and the memory of a reset (PW7.3): a drawing of 10 000 objects (texts, lines, circles and INSERTs, the mix of
    /// the standards speed test) is reset after an episode of 100 random edits in far less than it takes to open it again, and
    /// a thousand episodes of a hundred random edits, each ended by a reset, leave the memory where the first hundred put it.
    /// The limits are ceilings a reset that reads the file or keeps what it should forget crosses by a long way; the times and
    /// the memory are written to the test output.
    /// </summary>
    [Collection(ViewerSpeedCollection.Name)]
    public sealed class SnapshotResetSpeedTests : IDisposable
    {
        private readonly SpatialDrawings _drawings = new SpatialDrawings();
        private readonly ITestOutputHelper _output;

        public SnapshotResetSpeedTests(ITestOutputHelper output) => _output = output;

        public void Dispose() => _drawings.Dispose();

        /// <summary>A drawing of <paramref name="count"/> objects laid at random over 1 000 by 1 000, on four layers, some coloured.</summary>
        private CadDocument Drawing(int count)
        {
            CadDocument document = _drawings.Blank();
            document.Database.LayerTable.Add(new DxfLayerTableRecord { Name = "A-WALL" });
            document.Database.LayerTable.Add(new DxfLayerTableRecord { Name = "JUNK" });
            var block = new DxfBlockTableRecord { Name = "MARK" };
            document.Database.BlockTable.Add(block);
            block.AppendEntity(Line(0, 0, 12, 4));
            DxfDBText label = Text("SIDE", 1, 6, 0.0, 2.5);
            block.AppendEntity(label);
            label.TextStyleName = "OC_SHX";
            var random = new Random(10000);

            for (int i = 0; i < count; i++)
            {
                double x = random.NextDouble() * 1000.0;
                double y = random.NextDouble() * 1000.0;
                int kind = i % 20;
                DxfEntity made;

                if (kind < 5)
                {
                    made = Put(document, Text("SIDE FOOD", x, y, random.Next(4) * 90.0, 2.5 + random.Next(4)));
                }
                else if (kind < 15)
                {
                    double length = 5.0 + random.NextDouble() * 25.0;
                    double angle = random.NextDouble() * Math.PI;
                    made = Put(document, Line(x, y, x + length * Math.Cos(angle), y + length * Math.Sin(angle)));
                }
                else if (kind < 18)
                {
                    made = Put(document, Circle(x, y, 1.0 + random.NextDouble() * 9.0));
                }
                else
                {
                    made = Put(
                        document,
                        new DxfBlockReference(new GeoPoint3(x, y, 0), block)
                        {
                            Rotation = random.NextDouble() * Math.PI,
                        }
                    );
                }

                made.Layer =
                    i % 3 == 0 ? "JUNK"
                    : i % 3 == 1 ? "A-WALL"
                    : "0";

                if (i % 7 == 0)
                {
                    made.ColorIndex = 1 + i % 5;
                }
            }

            return document;
        }

        /// <summary>One random edit: an object added, moved, erased, put on another layer or given a colour; each one undo step.</summary>
        private static void RandomEdit(CadDocument document, Random random, List<string> alive)
        {
            int kind = random.Next(20);

            if (kind < 5 || alive.Count == 0)
            {
                document.ApplyEdit("LINE", null, s =>
                {
                    DxfLine line = s.Append(
                        s.Document.ModelSpace,
                        new DxfLine(
                            new GeoPoint3(random.Next(1000), random.Next(1000), 0),
                            new GeoPoint3(random.Next(1000), random.Next(1000), 0)
                        )
                    );
                    alive.Add(line.Handle);
                });
                return;
            }

            int at = random.Next(alive.Count);
            string handle = alive[at];

            if (kind < 12)
            {
                var by = new GeoVector3(random.Next(-20, 20), random.Next(-20, 20), 0);
                document.ApplyEdit("MOVE", new[] { new CadEditRecord(CadEditRecordKind.Transformed, handle) }, s =>
                    s.Document.FindEntity(handle).TransformBy(GeoTransform3.Translation(by))
                );
            }
            else if (kind < 15)
            {
                document.ApplyEdit("ERASE", new[] { new CadEditRecord(CadEditRecordKind.Erased, handle) }, s =>
                    s.Document.FindEntity(handle).Erase()
                );
                alive.RemoveAt(at);
            }
            else if (kind < 18)
            {
                string layer = random.Next(2) == 0 ? "JUNK" : "A-WALL";
                document.ApplyEdit(
                    "CHPROP",
                    new[] { new CadEditRecord(CadEditRecordKind.PropertyChanged, handle, "Layer", null, layer) },
                    s => s.Document.FindEntity(handle).Layer = layer
                );
            }
            else
            {
                int color = 1 + random.Next(7);
                document.ApplyEdit(
                    "CHPROP",
                    new[]
                    {
                        new CadEditRecord(
                            CadEditRecordKind.PropertyChanged,
                            handle,
                            "Color",
                            null,
                            color.ToString(CultureInfo.InvariantCulture)
                        ),
                    },
                    s => s.Document.FindEntity(handle).ColorIndex = color
                );
            }
        }

        private static List<string> Alive(CadDocument document) =>
            document.ModelSpace.Block.Select(e => e.Handle).ToList();

        [Fact]
        public void TenThousandObjects_AreResetInAFractionOfTheTimeToOpenThem()
        {
            CadDocument document = Drawing(10000);
            string text = document.Database.ToText();
            document.GetDisplay(document.ModelSpace);
            string before = document.Database.ToText();
            CadSnapshot snapshot = document.TakeSnapshot();
            var random = new Random(7);
            var clock = Stopwatch.StartNew();
            var runs = new List<string>();
            bool fast = false;

            // A busy machine stretches one run: the comparison is made again, up to three times, until one run is within the limit.
            for (int attempt = 0; attempt < 3 && !fast; attempt++)
            {
                GC.Collect();
                clock.Restart();
                CadDocument opened = CadDocument.Open(
                    new System.IO.MemoryStream(new System.Text.UTF8Encoding(false).GetBytes(text)),
                    "reopened.dxf"
                );
                opened.Fonts = document.Fonts;
                opened.GetDisplay(opened.ModelSpace);
                double open = clock.Elapsed.TotalMilliseconds;
                double reset = 0.0;
                double display = 0.0;
                const int Rounds = 6;

                for (int round = 0; round < Rounds; round++)
                {
                    List<string> alive = Alive(document);

                    for (int i = 0; i < 100; i++)
                    {
                        RandomEdit(document, random, alive);
                    }

                    document.GetDisplay(document.ModelSpace);
                    clock.Restart();
                    document.Reset(snapshot);
                    double one = clock.Elapsed.TotalMilliseconds;
                    document.GetDisplay(document.ModelSpace);

                    // The first reset to a snapshot reads the drawing at once; the next ones find it read while the episode ran.
                    if (round > 0)
                    {
                        reset += one;
                        display += clock.Elapsed.TotalMilliseconds - one;
                    }
                }

                reset /= Rounds - 1;
                display /= Rounds - 1;
                string line =
                    $"10 000 objects: open the file and draw {open:0.0} ms; reset {reset:0.0} ms and draw again {display:0.0} ms";
                _output.WriteLine(line);
                runs.Add(line);
                fast = reset + display < open / 2.0;
            }

            Assert.Equal(before, document.Database.ToText());
            Assert.True(fast, string.Join("; ", runs));
        }

        [Fact]
        public void AThousandEpisodesOfAHundredEdits_EachEndedByAReset_LeaveTheMemoryWhereItWas()
        {
            CadDocument document = Drawing(200);
            string before = document.Database.ToText();
            CadSnapshot snapshot = document.TakeSnapshot();
            var random = new Random(11);
            long atHundred = 0;
            long atEnd = 0;
            var clock = Stopwatch.StartNew();

            for (int episode = 1; episode <= 1000; episode++)
            {
                List<string> alive = Alive(document);

                for (int i = 0; i < 100; i++)
                {
                    RandomEdit(document, random, alive);
                }

                document.Reset(snapshot);

                if (episode == 100)
                {
                    atHundred = GC.GetTotalMemory(true);
                }
            }

            atEnd = GC.GetTotalMemory(true);
            _output.WriteLine(
                $"1000 episodes of 100 edits in {clock.Elapsed.TotalSeconds:0.0} s; managed memory after 100: {atHundred / 1048576.0:0.0} MB, after 1000: {atEnd / 1048576.0:0.0} MB"
            );
            Assert.Equal(before, document.Database.ToText());
            Assert.False(document.CanUndo);
            Assert.True(
                atEnd - atHundred < 8L * 1048576,
                $"memory grew from {atHundred / 1048576.0:0.0} to {atEnd / 1048576.0:0.0} MB"
            );
        }
    }
}
