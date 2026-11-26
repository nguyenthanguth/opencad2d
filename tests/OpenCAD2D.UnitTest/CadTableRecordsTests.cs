using System;
using System.Collections.Generic;
using System.Linq;
using GeometryHelper.Dxf;
using GeometryHelper.Geometry;
using Xunit;

namespace OpenCAD2D.UnitTest
{
    /// <summary>The table records brought from one small drawing into another by name, and a dimension style written by variable names (PW5).</summary>
    public class CadTableRecordsTests
    {
        private static readonly string[] None = Array.Empty<string>();

        private static DxfDatabase Source()
        {
            var source = new DxfDatabase("AC1027");
            source.TextStyleTable.Add(
                new DxfTextStyleTableRecord
                {
                    Name = "NOTE",
                    FileName = "romans.shx",
                    XScale = 0.8,
                }
            );
            var plan = new DxfDimStyleTableRecord { Name = "PLAN" };
            source.DimStyleTable.Add(plan);
            plan.Dimtxsty = source.TextStyleTable["NOTE"];
            plan.Dimtxt = 3.5;
            var bolt = new DxfBlockTableRecord { Name = "BOLT", Origin = new GeoPoint3(0, 0, 0) };
            source.BlockTable.Add(bolt);
            bolt.AppendEntity(new DxfLine(new GeoPoint3(0, 0, 0), new GeoPoint3(10, 0, 0)));
            return source;
        }

        [Fact]
        public void TheRecordsNamed_AreCopiedInOrder_AndANameNotNamedIsLeftOut()
        {
            var target = new DxfDatabase("AC1027");

            CadTableRecords.Add(target, Source(), new[] { "NOTE" }, None, new[] { "BOLT" }, new[] { "PLAN" });

            Assert.Equal("romans.shx", target.TextStyleTable["NOTE"].FileName);
            Assert.Equal(0.8, target.TextStyleTable["NOTE"].XScale, 6);
            Assert.Single(target.BlockTable["BOLT"], e => !e.IsErased);
            DxfDimStyleTableRecord plan = target.DimStyleTable["PLAN"];
            Assert.Equal("NOTE", plan.Dimtxsty.Name);
            Assert.Equal(3.5, plan.Dimtxt, 6);
        }

        [Fact]
        public void TheDefaultsASmallDrawingIsMadeWith_AreNotAddedToADrawingThatLacksThem()
        {
            var target = new DxfDatabase("AC1027");
            target.Dimstyle = "Standard";
            target.DimStyleTable["ISO-25"].Erase();
            Assert.False(target.DimStyleTable.Has("ISO-25"));

            CadTableRecords.Add(target, Source(), new[] { "NOTE" }, None, None, None);

            Assert.False(target.DimStyleTable.Has("ISO-25"));
            Assert.True(target.TextStyleTable.Has("NOTE"));
        }

        [Fact]
        public void ANameTheDrawingHasOrTheSourceLacks_IsRefused_AndNothingIsCopied()
        {
            var target = new DxfDatabase("AC1027");
            target.TextStyleTable.Add(new DxfTextStyleTableRecord { Name = "NOTE", FileName = "txt" });
            DxfDatabase source = Source();

            Assert.Throws<ArgumentException>(() =>
                CadTableRecords.Add(target, source, new[] { "NOTE" }, None, None, None)
            );
            Assert.Throws<ArgumentException>(() =>
                CadTableRecords.Add(target, source, None, None, new[] { "NUT" }, None)
            );
            Assert.Throws<ArgumentNullException>(() => CadTableRecords.Add(null, source, None, None, None, None));
            Assert.Throws<ArgumentNullException>(() => CadTableRecords.Add(target, null, None, None, None, None));
            Assert.Equal("txt", target.TextStyleTable["NOTE"].FileName);
        }

        [Fact]
        public void ADimensionStyleWrittenByVariableNames_TakesTheValuesAndTheTextStyleOfTheDrawing()
        {
            var database = new DxfDatabase("AC1027");
            database.TextStyleTable.Add(new DxfTextStyleTableRecord { Name = "NOTE", FileName = "romans.shx" });
            var style = new DxfDimStyleTableRecord { Name = "PLAN" };
            database.DimStyleTable.Add(style);
            SortedDictionary<string, object> values = CadDimStyleEditor.Read(database.DimStyleTable["ISO-25"]);
            values["Dimtxsty"] = "NOTE";
            values["Dimscale"] = 50.0;
            values["Dimdec"] = 1;

            CadDimStyleEditor.Write(database, style, values);

            Assert.Equal("NOTE", style.Dimtxsty.Name);
            Assert.Equal(50.0, style.Dimscale, 6);
            Assert.Equal(1, style.Dimdec);
            Assert.Equal(1.25, style.Dimexe, 6);
        }
    }
}
