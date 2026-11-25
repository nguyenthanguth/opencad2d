using System;
using System.IO;
using System.Linq;
using GeometryHelper.Dxf;
using GeometryHelper.Geometry;
using Xunit;
using static OpenCAD2D.UnitTest.EditingDrawings;

namespace OpenCAD2D.UnitTest
{
    /// <summary>
    /// The blocks of the drawing of a loaded external reference (<c>grid|TAG</c> for the block TAG of <c>grid</c>), as AutoCAD
    /// 2021 lists them (accoreconsole: -BLOCK ? says <c>Xdep: "grid"</c>, -INSERT ? lists the user's blocks alone, -INSERT of one
    /// says "Cannot insert dependent block.", and its DXFOUT writes none): held while the reference is loaded, none once it is
    /// unloaded or not found, and never in the drawing's own table or file.
    /// </summary>
    public sealed class XrefDependentBlockTests : IDisposable
    {
        private const string LongName = "LONGNAME_OF_A_BLOCK_IN_A_GRID";

        private readonly string _folder = Path.Combine(
            Path.GetTempPath(),
            "gk",
            "xdep-" + Guid.NewGuid().ToString("N")
        );

        public XrefDependentBlockTests()
        {
            Directory.CreateDirectory(Path.Combine(_folder, "plans"));
        }

        public void Dispose()
        {
            WaitingRoutines.EndAll();

            try
            {
                Directory.Delete(_folder, true);
            }
            catch (IOException)
            {
                // A file a watcher still holds is left for the system to clear.
            }
        }

        /// <summary>
        /// The drawing grid: blocks TAG (a line from (0, 0) to (0, 10)), PLAN (a circle of radius 5) and one of a name of 29
        /// letters, saved beside the host.
        /// </summary>
        private string Grid()
        {
            DxfDatabase database = CadTemplate.CreateMetric();

            foreach (string name in new[] { "TAG", "PLAN", LongName })
            {
                var block = new DxfBlockTableRecord { Name = name };
                database.BlockTable.Add(block);
                block.AppendEntity(
                    name == "PLAN"
                        ? (DxfEntity)new DxfCircle(GeoPoint3.Origin, new GeoVector3(0, 0, 1), 5)
                        : new DxfLine(new GeoPoint3(0, 0, 0), new GeoPoint3(0, 10, 0))
                );
            }

            string path = Path.Combine(_folder, "plans", "grid.dxf");
            database.Save(path);
            return path;
        }

        /// <summary>A metric drawing with the block USR (a line), saved as host.dxf and opened, grid attached at (0, 0).</summary>
        private CadDocument Host(out CadCommandSession session)
        {
            string grid = Grid();
            DxfDatabase database = CadTemplate.CreateMetric();
            var user = new DxfBlockTableRecord { Name = "USR" };
            database.BlockTable.Add(user);
            user.AppendEntity(new DxfLine(new GeoPoint3(0, 0, 0), new GeoPoint3(10, 0, 0)));
            string path = Path.Combine(_folder, "plans", "host.dxf");
            database.Save(path);
            CadDocument document = CadDocument.Open(path);
            session = Session(document);
            Run(session, "-XREF", "A", grid, P(0, 0), Enter, Enter, Enter);
            return document;
        }

        private static string[] ListOf(CadCommandSession session)
        {
            int from = session.History.ToList().FindLastIndex(l => l == "Defined blocks.");
            return session.History.Skip(from).ToArray();
        }

        /// <summary>A line of the list, the name quoted and indented two spaces, the rest from the 37th column.</summary>
        private static string Row(string name, string rest) => ("  \"" + name + "\"").PadRight(36) + rest;

        [Fact]
        public void BlockQuestionMark_ListsTheReferenceAndTheBlocksItBrings_InOneOrderOfNames_AsAutoCADWritesThem()
        {
            // AutoCAD 2021 (accoreconsole, xref probes): the user's block quoted alone; the reference `Xref: resolved`; each
            // block of its drawing `Xdep: "grid"`; every name beside its note from the 37th column, or 3 spaces after a name
            // that reaches it; the counts 1, 1, 3 and 0.
            CadDocument document = Host(out CadCommandSession session);

            Run(session, "-BLOCK", "?");
            Assert.Equal("Enter block(s) to list <*>:", session.PromptText);
            Run(session, Enter);

            Assert.Equal(
                new[]
                {
                    "Defined blocks.",
                    Row("grid", "Xref: resolved"),
                    "  \"grid|" + LongName + "\"   Xdep: \"grid\"",
                    Row("grid|PLAN", "Xdep: \"grid\""),
                    Row("grid|TAG", "Xdep: \"grid\""),
                    "  \"USR\"",
                    "User     External     Dependent   Unnamed",
                    "Blocks   References   Blocks      Blocks",
                    "    1         1           3           0",
                },
                ListOf(session)
            );
            Assert.False(session.IsRunning);
            Assert.False(document.Database.BlockTable.Has("grid|TAG"));
        }

        [Fact]
        public void BlockQuestionMark_GivenPatterns_ListsAndCountsWhatMatchesAlone_LettersOfEitherCaseTheSame()
        {
            Host(out CadCommandSession session);

            Run(session, "-BLOCK", "?", "GRID|t*,us*");

            Assert.Equal(
                new[]
                {
                    "Defined blocks.",
                    Row("grid|TAG", "Xdep: \"grid\""),
                    "  \"USR\"",
                    "User     External     Dependent   Unnamed",
                    "Blocks   References   Blocks      Blocks",
                    "    1         0           1           0",
                },
                ListOf(session)
            );

            Run(session, "-BLOCK", "?", "grid");
            Assert.Equal(
                new[]
                {
                    "Defined blocks.",
                    Row("grid", "Xref: resolved"),
                    "User     External     Dependent   Unnamed",
                    "Blocks   References   Blocks      Blocks",
                    "    0         1           0           0",
                },
                ListOf(session)
            );
        }

        [Fact]
        public void InsertQuestionMark_ListsTheUsersBlocksAlone_WithTheTwoColumnCount()
        {
            // AutoCAD 2021 (accoreconsole): -INSERT ? lists the blocks it can insert, not a reference or what it brings, and
            // counts the user's and the unnamed ones.
            Host(out CadCommandSession session);

            Run(session, "-INSERT", "?", Enter);

            Assert.Equal(
                new[] { "Defined blocks.", "  \"USR\"", "User     Unnamed", "Blocks   Blocks", "    1        0" },
                ListOf(session)
            );
        }

        [Fact]
        public void InsertOfADependentBlock_SaysItCannotBe_AndPlacesNothing()
        {
            // AutoCAD 2021 (accoreconsole): "Cannot insert dependent block." and the command is over.
            CadDocument document = Host(out CadCommandSession session);
            int inserts = All<DxfBlockReference>(document.ModelSpace).Count();

            Run(session, "-INSERT", "grid|TAG");

            Assert.Equal("Cannot insert dependent block.", session.History.Last());
            Assert.False(session.IsRunning);
            Assert.Equal(inserts, All<DxfBlockReference>(document.ModelSpace).Count());

            Run(session, "-BLOCK", "grid|NEW");
            Assert.Equal("Invalid block name.", session.History.Last());
        }

        [Fact]
        public void AReferenceUnloaded_BringsNoBlocks_AndIsListedUnloaded_UntilItIsReloaded()
        {
            // AutoCAD 2021 (accoreconsole): -XREF Unload takes grid|TAG and the others away and lists grid as `Xref: unloaded`;
            // Reload brings them back.
            Host(out CadCommandSession session);
            Run(session, "-XREF", "U", "grid");

            Run(session, "-BLOCK", "?", Enter);
            Assert.Equal(
                new[]
                {
                    "Defined blocks.",
                    Row("grid", "Xref: unloaded"),
                    "  \"USR\"",
                    "User     External     Dependent   Unnamed",
                    "Blocks   References   Blocks      Blocks",
                    "    1         1           0           0",
                },
                ListOf(session)
            );

            Run(session, "-XREF", "R", "grid");
            Run(session, "-BLOCK", "?", Enter);
            Assert.Equal("    1         1           3           0", ListOf(session).Last());
        }

        [Fact]
        public void AReferenceWhoseFileIsGone_BringsNoBlocks_AndIsListedNotFound()
        {
            // AutoCAD 2021 (accoreconsole, the DWG opened with its xref moved away): `Xref: not found` and no dependent block.
            Host(out CadCommandSession session);
            File.Delete(Path.Combine(_folder, "plans", "grid.dxf"));

            Run(session, "-BLOCK", "?", Enter);

            Assert.Equal(
                new[]
                {
                    "Defined blocks.",
                    Row("grid", "Xref: not found"),
                    "  \"USR\"",
                    "User     External     Dependent   Unnamed",
                    "Blocks   References   Blocks      Blocks",
                    "    1         1           0           0",
                },
                ListOf(session)
            );
        }

        [Fact]
        public void TheDependentBlocks_AreNotInTheDrawingsOwnTable_OrItsFile_AsAutoCADWritesNone()
        {
            // AutoCAD 2021's DXFOUT of a drawing with grid attached wrote the layers of grid and neither its BLOCK_RECORDs nor its
            // BLOCKs; its DXF reader refuses a block table that names one.
            CadDocument document = Host(out _);

            Assert.False(document.Database.BlockTable.Has("grid|TAG"));
            Assert.DoesNotContain("grid|TAG", document.Database.ToText(), StringComparison.Ordinal);

            string saved = Path.Combine(_folder, "plans", "saved.dxf");
            document.SaveAs(saved);

            Assert.DoesNotContain("grid|TAG", File.ReadAllText(saved), StringComparison.Ordinal);
        }

        [Fact]
        public void GetDependentBlocks_GivesCopiesWithTheirObjects_FlaggedResolved_InOrderOfNames()
        {
            CadDocument document = Host(out _);

            System.Collections.Generic.IReadOnlyList<DxfBlockTableRecord> blocks = document.Xrefs.GetDependentBlocks();

            Assert.Equal(new[] { "grid|" + LongName, "grid|PLAN", "grid|TAG" }, blocks.Select(b => b.Name));
            DxfBlockTableRecord tag = blocks.Single(b => b.Name == "grid|TAG");
            Assert.True(tag.IsDependent);
            Assert.True(tag.IsResolved);
            Assert.False(tag.IsFromExternalReference);
            DxfLine line = Assert.IsType<DxfLine>(Assert.Single(tag.Cast<DxfEntity>()));
            Near(new GeoPoint3(0, 10, 0), line.EndPoint);
            Assert.IsType<DxfCircle>(Assert.Single(blocks.Single(b => b.Name == "grid|PLAN").Cast<DxfEntity>()));

            Assert.False(document.Database.BlockTable.Has("grid|TAG"));
        }
    }
}
