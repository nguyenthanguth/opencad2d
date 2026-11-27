using System;
using System.Collections.Generic;
using System.Linq;
using GeometryHelper.Dxf;
using GeometryHelper.Geometry;
using Xunit;

namespace OpenCAD2D.Remote.UnitTest
{
    /// <summary>
    /// The multileader styles and table styles of a drawing read from outside (N5): the settings of each style as the app holds
    /// them, sorted by name ignoring case and then ordinally, the objects drawn in a style naming it as they come, and a request
    /// of a kind it does not know refused in words.
    /// </summary>
    public class RemoteAnnotationStyleTests
    {
        /// <summary>
        /// A drawing with the multileader styles Zeta (words 3.5 high, an arrow 4 long, scale 10) and alpha (annotative), the table
        /// styles Zeta (data words 6 high, margins 2 and 3) and alpha, a multileader and a table drawn in Zeta. The names are
        /// chosen so that an ordinal sort (Zeta before alpha) is not the sort ignoring case (alpha, Standard, Zeta).
        /// </summary>
        private static CadDocument Styled(out string note, out string table)
        {
            var document = CadDocument.From(new DxfDatabase("AC1027"), "styles.dxf");
            CadMLeaderStyleEditor.AddStandard(document);
            CadTableStyleEditor.AddStandard(document);
            Assert.True(CadMLeaderStyleEditor.Add(document, "Zeta", "Standard").Succeeded);
            Assert.True(CadMLeaderStyleEditor.Add(document, "alpha", "Standard").Succeeded);
            Assert.True(CadTableStyleEditor.Add(document, "Zeta", "Standard").Succeeded);
            Assert.True(CadTableStyleEditor.Add(document, "alpha", "Standard").Succeeded);

            DxfDatabase database = document.Database;
            CadMLeaderStyleSettings zeta = CadMLeaderStyleSettings.Of(CadMLeaderStyleEditor.Find(database, "Zeta"));
            zeta.TextHeight = 3.5;
            zeta.ArrowSize = 4.0;
            zeta.Scale = 10.0;
            zeta.Arrowhead = "Dot";
            Assert.True(CadMLeaderStyleEditor.Apply(document, "Zeta", zeta).Succeeded);
            CadMLeaderStyleSettings alpha = CadMLeaderStyleSettings.Of(CadMLeaderStyleEditor.Find(database, "alpha"));
            alpha.Annotative = true;
            Assert.True(CadMLeaderStyleEditor.Apply(document, "alpha", alpha).Succeeded);

            CadTableStyleSettings sheet = CadTableStyleSettings.Of(CadTableStyleEditor.Find(database, "Zeta"));
            sheet.Data.TextHeight = 6.0;
            sheet.HorizontalMargin = 2.0;
            sheet.VerticalMargin = 3.0;
            Assert.True(CadTableStyleEditor.Apply(document, "Zeta", sheet).Succeeded);

            var leader = CadMLeaderStyleEditor.MakeLanded(
                CadMLeaderStyleEditor.Find(database, "Zeta"),
                10.0,
                new GeoPoint3(0, 0, 0),
                new GeoPoint3(100, 60, 0),
                "NOTCH"
            );
            database.ModelSpace.AppendEntity(leader);
            leader.MLeaderStyle = "Zeta";
            var grid = new DxfTable(new GeoPoint3(500, 0, 0), 2, 2, 10, 50);
            database.ModelSpace.AppendEntity(grid);
            grid.TableStyle = CadTableStyleEditor.Find(database, "Zeta");
            note = leader.Handle;
            table = grid.Handle;
            return document;
        }

        private static (
            Harness harness,
            CadDocument document,
            CadRemoteDocument remote,
            string note,
            string table
        ) Start()
        {
            var harness = new Harness();
            var host = new TestHost();
            CadDocument document = host.Add(Styled(out string note, out string table));
            harness.Serve(host);
            return (harness, document, harness.Connect().Documents.Single(), note, table);
        }

        [Fact]
        public void TheMultileaderStyles_AreTheDrawingsOwn_SortedIgnoringCase_WithTheirSettings()
        {
            var (harness, document, remote, _, _) = Start();
            using (harness)
            {
                IReadOnlyList<CadRemoteMultileaderStyle> styles = remote.GetMultileaderStyles();

                Assert.Equal(new[] { "alpha", "Standard", "Zeta" }, styles.Select(s => s.Name).ToArray());
                CadRemoteMultileaderStyle zeta = styles.Single(s => s.Name == "Zeta");
                Assert.Equal(3.5, zeta.TextHeight, 9);
                Assert.Equal(4.0, zeta.ArrowSize, 9);
                Assert.Equal(10.0, zeta.Scale, 9);
                Assert.False(zeta.IsAnnotative);
                Assert.Equal("text", zeta.Content);
                Assert.Equal("straight", zeta.LeaderType);
                Assert.True(styles.Single(s => s.Name == "alpha").IsAnnotative);
                Assert.Equal(new[] { "Standard" }, styles.Where(s => s.IsCurrent).Select(s => s.Name).ToArray());

                // What the engine holds of the style is what comes across, and a copy changed in the caller changes nothing.
                CadMLeaderStyleSettings own = CadMLeaderStyleSettings.Of(
                    CadMLeaderStyleEditor.Find(document.Database, "Zeta")
                );
                Assert.Equal(own.Arrowhead, zeta.Arrowhead);
                Assert.Equal(own.LandingDistance, zeta.LandingLength, 9);
                Assert.Equal(own.LandingGap, zeta.LandingGap, 9);
                Assert.Equal(own.TextStyle, zeta.TextStyle);
                Assert.Equal(own.IncludeLanding, zeta.HasLanding);
            }
        }

        [Fact]
        public void TheTableStyles_AreTheDrawingsOwn_SortedIgnoringCase_WithTheirHeightsAndMargins()
        {
            var (harness, _, remote, _, _) = Start();
            using (harness)
            {
                IReadOnlyList<CadRemoteTableStyle> styles = remote.GetTableStyles();

                Assert.Equal(new[] { "alpha", "Standard", "Zeta" }, styles.Select(s => s.Name).ToArray());
                CadRemoteTableStyle zeta = styles.Single(s => s.Name == "Zeta");
                Assert.Equal(6.0, zeta.Data.TextHeight, 9);
                Assert.Equal(2.0, zeta.HorizontalMargin, 9);
                Assert.Equal(3.0, zeta.VerticalMargin, 9);
                CadRemoteTableStyle standard = styles.Single(s => s.Name == "Standard");
                Assert.Equal(6.0, standard.Title.TextHeight, 9);
                Assert.Equal(4.5, standard.Header.TextHeight, 9);
                Assert.Equal(4.5, standard.Data.TextHeight, 9);
                Assert.Equal(1.5, standard.HorizontalMargin, 9);
                Assert.True(standard.IsTitleShown);
                Assert.True(standard.IsHeaderShown);
                Assert.Equal("Standard", styles.Single(s => s.IsCurrent).Name);
            }
        }

        [Fact]
        public void AMultileaderAndATable_ComeWithTheNameOfTheirStyle_AndTheirOwnSettingsUnchanged()
        {
            var (harness, document, remote, note, table) = Start();
            using (harness)
            {
                var leader = (DxfMLeader)remote.GetObject(note);
                var grid = (DxfTable)remote.GetObject(table);
                var original = (DxfMLeader)document.FindEntity(note);

                Assert.Equal("Zeta", leader.MLeaderStyle);
                Assert.Equal("Zeta", grid.TableStyleName);
                Assert.Equal(original.Scale, leader.Scale, 9);
                Assert.Equal(original.TextHeight, leader.TextHeight, 9);
                Assert.Equal(original.ArrowSize, leader.ArrowSize, 9);
                Assert.Equal(original.MText.Text, leader.MText.Text);
            }
        }

        [Fact]
        public void ALeaderThatTakesItsArrowFromItsStyle_KeepsTheArrowOnTheCopy_AndTheTableTheMarginsOfItsStyle()
        {
            var (harness, document, remote, note, table) = Start();
            using (harness)
            {
                var original = (DxfMLeader)document.FindEntity(note);
                Assert.Equal("_Dot", original.ArrowSymbol.Name);
                var leader = (DxfMLeader)remote.GetObject(note);
                var grid = (DxfTable)remote.GetObject(table);

                Assert.NotNull(leader.ArrowSymbol);
                Assert.Equal(original.ArrowSymbol.Name, leader.ArrowSymbol.Name);
                var style = (DxfTableStyle)grid.TableStyle;
                Assert.Equal(2.0, style.HorizontalCellMargin, 9);
                Assert.Equal(3.0, style.VerticalCellMargin, 9);
                Assert.Equal(6.0, style.TextHeight(DxfRowType.DataRow), 9);
            }
        }

        [Fact]
        public void ACopyOfALeaderCommittedBack_KeepsItsArrow_AndTheRealLeaderIsLeftAsItWas()
        {
            var (harness, document, remote, note, _) = Start();
            using (harness)
            {
                string before = ((DxfMLeader)document.FindEntity(note)).ArrowSymbol.Name;

                remote.Insert(remote.GetObject(note));
                string made = Assert.Single(remote.CommitChanges());

                var again = (DxfMLeader)document.FindEntity(made);
                Assert.Equal(before, again.ArrowSymbol.Name);
                // A commit carries no style object, as before: the new leader names none, and keeps the settings it was drawn with.
                Assert.Null(again.MLeaderStyle);
                Assert.Equal(before, ((DxfMLeader)document.FindEntity(note)).ArrowSymbol.Name);
            }
        }

        [Fact]
        public void ACopyCommittedIntoADrawingWithoutItsStyle_MakesNoStyleThere()
        {
            var harness = new Harness();
            var host = new TestHost();
            CadDocument source = host.Add(Styled(out string note, out _));
            CadDocument bare = host.Add(CadDocument.From(new DxfDatabase("AC1027"), "bare.dxf"));
            harness.Serve(host);

            using (harness)
            {
                CadRemoteDocuments documents = harness.Connect().Documents;
                CadRemoteDocument from = documents.Single(d => d.Name == "styles.dxf");
                CadRemoteDocument into = documents.Single(d => d.Name == "bare.dxf");

                into.Insert(from.GetObject(note));
                string made = Assert.Single(into.CommitChanges());

                Assert.Empty(CadMLeaderStyleEditor.Styles(bare.Database));
                Assert.Equal("_Dot", ((DxfMLeader)bare.FindEntity(made)).ArrowSymbol?.Name);
            }
        }

        [Fact]
        public void ARequestOfAKindItDoesNotKnow_IsRefusedInWords_AndTheKindsAreTold()
        {
            var (harness, _, remote, _, _) = Start();
            using (harness)
            {
                CadApplication app = harness.Connect();
                var request = new JsonObject()
                    .Set("op", RemoteOps.AnnotationStyles)
                    .Set("doc", (long)app.Documents.Single().Id)
                    .Set("kind", "dimension");
                CadRemoteException refused = Assert.Throws<CadRemoteException>(() => app.Connection.Request(request));
                Assert.Contains("kind", refused.Message);
                Assert.Contains("multileader or table", refused.Message);

                request.Set("kind", 5L);
                refused = Assert.Throws<CadRemoteException>(() => app.Connection.Request(request));
                Assert.Contains("kind has to be text", refused.Message);
                Assert.Equal(3, remote.GetTableStyles().Count);
            }
        }
    }
}
