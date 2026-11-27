using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using Xunit;

namespace OpenCAD2D.Mcp.UnitTest
{
    /// <summary>
    /// The Erase option OFFSET remembers (N3): the offset tool types its own, and puts back the one the user had, so that the
    /// user's next OFFSET offers what it did before the tool ran, in a file and in an app.
    /// </summary>
    public class McpOffsetEraseTests
    {
        public static IEnumerable<object[]> Modes => new[] { new object[] { "file" }, new object[] { "app" } };

        /// <summary>The Erase option OFFSET shows as the current one, read by starting it and finding it unfinished.</summary>
        private static string Setting(Bench bench)
        {
            var editing = (IDrawingEditing)bench.Harness.Session.Backend;

            try
            {
                editing.Run(new CommandScript().Word("OFFSET"));
            }
            catch (Exception e)
            {
                int at = e.Message.IndexOf("Erase source=", StringComparison.Ordinal);
                Assert.True(at >= 0, e.Message);
                return e.Message.Substring(at + "Erase source=".Length, 3).TrimEnd(' ', '|');
            }

            throw new InvalidOperationException("OFFSET ended with no input.");
        }

        private static void User(Bench bench, string erase) =>
            ((IDrawingEditing)bench.Harness.Session.Backend).Run(
                new CommandScript().Word("OFFSET").Word("E").Word(erase).Enter().Enter()
            );

        [Theory]
        [MemberData(nameof(Modes))]
        public async Task An_offset_leaves_the_erase_option_the_user_had(string mode)
        {
            await using Bench bench = await Bench.Start(mode);
            string line = await bench.Add("add_line", ("start", new[] { 0.0, 0.0 }), ("end", new[] { 100.0, 0.0 }));
            Assert.Equal("No", Setting(bench));

            // The user has Erase on; an offset that keeps its source leaves it on.
            User(bench, "Y");
            Assert.Equal("Yes", Setting(bench));
            await bench.Call("offset", ("handle", line), ("side", new[] { 50.0, 10.0 }), ("distance", 10.0));
            Assert.Equal("Yes", Setting(bench));
            Assert.Equal(2, await bench.Count("LINE"));

            // The user has it off; an offset that erases its source leaves it off, and erased the source.
            User(bench, "N");
            await bench.Call(
                "offset",
                ("handle", line),
                ("side", new[] { 50.0, -10.0 }),
                ("distance", 10.0),
                ("erase_source", true)
            );
            Assert.Equal("No", Setting(bench));
            Assert.Equal(2, await bench.Count("LINE"));
        }
    }
}
