using System;
using System.Collections.Generic;
using System.Linq;
using GeometryHelper.Dxf;
using GeometryHelper.Geometry;
using Xunit;

namespace OpenCAD2D.Remote.UnitTest
{
    /// <summary>
    /// The table records a commit adds (<see cref="CadRemoteDocument.AddRecords"/>), the text styles and linetypes the objects of
    /// a commit name, the linetypes and the current dimension style seen from outside (PW5).
    /// </summary>
    public class RemoteRecordsTests
    {
        private static DxfDBText Text(string style)
        {
            var text = new DxfDBText
            {
                TextString = "Plate",
                Position = new GeoPoint3(0, 5, 0),
                Height = 3.0,
            };
            text.TextStyleName = style;
            return text;
        }

        /// <summary>A drawing with the text style NOTE (romans.shx, 0.8 wide), the dimension style PLAN and the linetype DASHED.</summary>
        private static CadDocument WithRecords(string name)
        {
            var db = new DxfDatabase("AC1027");
            db.TextStyleTable.Add(
                new DxfTextStyleTableRecord
                {
                    Name = "NOTE",
                    FileName = "romans.shx",
                    XScale = 0.8,
                }
            );
            db.DimStyleTable.Add(new DxfDimStyleTableRecord { Name = "PLAN", Dimtxt = 3.5 });
            var dashed = new DxfLinetypeTableRecord { Name = "DASHED", NumDashes = 2 };
            dashed.SetDashLengthAt(0, 12.0);
            dashed.SetDashLengthAt(1, -6.0);
            db.LinetypeTable.Add(dashed);
            return CadDocument.From(db, name);
        }

        private static (Harness harness, CadDocument document, CadRemoteDocument remote) Start(CadDocument document)
        {
            var harness = new Harness();
            var host = new TestHost();
            host.Add(document);
            harness.Serve(host);
            return (harness, document, harness.Connect().Documents.Single());
        }

        /// <summary>A drawing of your own holding the text style NOTE, as a client makes the records it adds.</summary>
        private static DxfDatabase NoteRecords(string font)
        {
            var records = new DxfDatabase("AC1027");
            records.TextStyleTable.Add(new DxfTextStyleTableRecord { Name = "NOTE", FileName = font });
            return records;
        }

        [Fact]
        public void ATextInATextStyleTheAppHas_IsInserted_WithTheAppsOwnStyle()
        {
            var (harness, document, remote) = Start(WithRecords("A-101.dxf"));
            using (harness)
            {
                remote.Insert(Text("NOTE"));
                IReadOnlyList<string> handles = remote.CommitChanges();

                var text = Assert.IsType<DxfDBText>(document.FindEntity(Assert.Single(handles)));
                Assert.Equal("NOTE", text.TextStyleName);
                DxfTextStyleTableRecord style = document.Database.TextStyleTable["NOTE"];
                Assert.Equal("romans.shx", style.FileName);
                Assert.Equal(0.8, style.XScale, 6);
                Assert.Equal(2, document.Database.TextStyleTable.Count());
            }
        }

        [Fact]
        public void AnMTextInATextStyleTheAppHas_AndALineInALinetypeItHas_AreInserted()
        {
            var (harness, document, remote) = Start(WithRecords("A-101.dxf"));
            using (harness)
            {
                var mtext = new DxfMText
                {
                    Contents = "Plate PL10",
                    Location = new GeoPoint3(0, 5, 0),
                    TextHeight = 3.0,
                };
                mtext.TextStyleName = "NOTE";
                remote.Insert(mtext);
                remote.Insert(new DxfLine(new GeoPoint3(0, 0, 0), new GeoPoint3(10, 0, 0)) { Linetype = "DASHED" });

                IReadOnlyList<string> handles = remote.CommitChanges();

                Assert.Equal("NOTE", ((DxfMText)document.FindEntity(handles[0])).TextStyleName);
                Assert.Equal("DASHED", document.FindEntity(handles[1]).Linetype);
                Assert.Equal(4, document.Database.LinetypeTable.Count());
                Assert.Equal(12.0, document.Database.LinetypeTable["DASHED"].DashLengthAt(0), 6);
                Assert.Equal(-6.0, document.Database.LinetypeTable["DASHED"].DashLengthAt(1), 6);
                Assert.Equal("REMOTE", document.NextUndo.Name);
            }
        }

        [Fact]
        public void ATextInAStyleNobodyHas_IsRefusedByTheLibrary_AsItRefusesAnyObjectThatNamesOne()
        {
            var (harness, document, remote) = Start(WithRecords("A-101.dxf"));
            using (harness)
            {
                Assert.Throws<ArgumentException>(() => remote.Insert(Text("Nowhere")));

                Assert.Empty(remote.CommitChanges());
                Assert.Empty(document.Database.ModelSpace);
            }
        }

        [Fact]
        public void ATextStyleAddedWithATextInIt_IsOneUndoStep_AndUndoTakesBothAway()
        {
            var (harness, document, remote) = Start(CadDocument.From(new DxfDatabase("AC1027"), "A-101.dxf"));
            using (harness)
            {
                remote.AddRecords(NoteRecords("romans.shx"));
                remote.Insert(Text("NOTE"));

                IReadOnlyList<string> handles = remote.CommitChanges();

                Assert.Equal("romans.shx", document.Database.TextStyleTable["NOTE"].FileName);
                Assert.Equal("NOTE", ((DxfDBText)document.FindEntity(Assert.Single(handles))).TextStyleName);
                Assert.Equal("REMOTE", document.NextUndo.Name);

                Assert.True(document.Undo());

                Assert.False(document.Database.TextStyleTable.Has("NOTE"));
                Assert.Empty(document.Database.ModelSpace);
                Assert.False(document.CanUndo);
            }
        }

        [Fact]
        public void ARecordOfANameTheAppHas_IsTheAppsOwn_AndTheOneGivenIsLeftOut()
        {
            var (harness, document, remote) = Start(WithRecords("A-101.dxf"));
            using (harness)
            {
                remote.AddRecords(NoteRecords("arial.ttf"));
                remote.Insert(Text("NOTE"));

                remote.CommitChanges();

                Assert.Equal("romans.shx", document.Database.TextStyleTable["NOTE"].FileName);
            }
        }

        [Fact]
        public void ALinetypeAddedWithALayerThatHasIt_AndADimensionStyleThatPointsAtATextStyleOfTheCommit_AreWrittenInOrder()
        {
            var (harness, document, remote) = Start(CadDocument.From(new DxfDatabase("AC1027"), "A-101.dxf"));
            using (harness)
            {
                var records = new DxfDatabase("AC1027");
                var hidden = new DxfLinetypeTableRecord { Name = "HIDDEN", NumDashes = 2 };
                hidden.SetDashLengthAt(0, 6.0);
                hidden.SetDashLengthAt(1, -3.0);
                records.LinetypeTable.Add(hidden);
                records.TextStyleTable.Add(new DxfTextStyleTableRecord { Name = "NOTE", FileName = "romans.shx" });
                var plan = new DxfDimStyleTableRecord { Name = "PLAN" };
                records.DimStyleTable.Add(plan);
                plan.Dimtxsty = records.TextStyleTable["NOTE"];
                plan.Dimtxt = 3.5;

                remote.AddLayer(new CadRemoteLayer("Hid") { Linetype = "HIDDEN" });
                remote.AddRecords(records);
                remote.CommitChanges();

                Assert.Equal("HIDDEN", document.Database.LayerTable["Hid"].LinetypeName);
                DxfDimStyleTableRecord made = document.Database.DimStyleTable["PLAN"];
                Assert.Equal("NOTE", made.Dimtxsty.Name);
                Assert.Equal(3.5, made.Dimtxt, 6);
                Assert.False(document.Database.DimStyleTable.Has("Copy"));
            }
        }

        [Fact]
        public void ABlockWithItsObjects_AndAnInsertOfItInTheSameCommit_AreAdded()
        {
            var (harness, document, remote) = Start(CadDocument.From(new DxfDatabase("AC1027"), "A-101.dxf"));
            using (harness)
            {
                var records = new DxfDatabase("AC1027");
                var bolt = new DxfBlockTableRecord { Name = "BOLT", Origin = new GeoPoint3(0, 0, 0) };
                records.BlockTable.Add(bolt);
                bolt.AppendEntity(new DxfLine(new GeoPoint3(0, 0, 0), new GeoPoint3(10, 0, 0)));
                var scratch = new DxfDatabase("AC1027");
                var carrier = new DxfBlockTableRecord { Name = "BOLT" };
                scratch.BlockTable.Add(carrier);

                remote.AddRecords(records);
                remote.Insert(new DxfBlockReference(new GeoPoint3(100, 50, 0), carrier));
                IReadOnlyList<string> handles = remote.CommitChanges();

                Assert.Single(document.Database.BlockTable["BOLT"], e => !e.IsErased);
                var insert = Assert.IsType<DxfBlockReference>(document.FindEntity(Assert.Single(handles)));
                Assert.Equal("BOLT", insert.Name);
            }
        }

        [Fact]
        public void RecordsThatHoldAnObjectInModelSpace_AreRefused_AndNullIsRefused()
        {
            var (harness, _, remote) = Start(CadDocument.From(new DxfDatabase("AC1027"), "A-101.dxf"));
            using (harness)
            {
                var records = new DxfDatabase("AC1027");
                records.ModelSpace.AppendEntity(new DxfLine(new GeoPoint3(0, 0, 0), new GeoPoint3(1, 0, 0)));

                Assert.Throws<ArgumentException>(() => remote.AddRecords(records));
                Assert.Throws<ArgumentNullException>(() => remote.AddRecords(null));
            }
        }

        [Fact]
        public void TheLinetypes_AreListedInTheOrderOfTheTable()
        {
            var (harness, _, remote) = Start(WithRecords("A-101.dxf"));
            using (harness)
            {
                Assert.Equal(
                    new[] { "ByBlock", "ByLayer", "Continuous", "DASHED" },
                    remote.Linetypes.OrderBy(n => n, StringComparer.Ordinal).ToArray()
                );
            }
        }

        [Fact]
        public void TheCurrentDimensionStyle_IsReadAndSet_AsOneUndoStep_AndAStyleTheDrawingLacksIsRefused()
        {
            var (harness, document, remote) = Start(WithRecords("A-101.dxf"));
            using (harness)
            {
                string before = remote.CurrentDimensionStyle;

                remote.CurrentDimensionStyle = "PLAN";

                Assert.Equal("PLAN", remote.CurrentDimensionStyle);
                Assert.Equal("PLAN", document.Database.Dimstyle);
                Assert.True(document.Undo());
                Assert.Equal(before, remote.CurrentDimensionStyle);
                Assert.Throws<CadRemoteException>(() => remote.CurrentDimensionStyle = "Nowhere");
                Assert.Throws<ArgumentNullException>(() => remote.CurrentDimensionStyle = null);
            }
        }
    }
}
