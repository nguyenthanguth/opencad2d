using System.Collections.Generic;
using System.Linq;
using GeometryHelper.Dxf;
using GeometryHelper.Geometry;
using Xunit;
using static OpenCAD2D.UnitTest.EditingDrawings;

namespace OpenCAD2D.UnitTest
{
    /// <summary>
    /// A dimension on a layout whose points were snapped through a viewport to the model space it shows, as AutoCAD 2021
    /// ties it with DIMASSOC 2 (trans-spatial association): a DIMASSOC of group 70 = 1 that names the viewport before each
    /// object, the dimension showing the model's size (DIMLFAC the negative of the viewport's scale, as AutoCAD sets it),
    /// following an edit of the model objects at once and the view of the viewport only at DIMREGEN. The numbers are those
    /// AutoCAD 2021's console gave for a DIMLINEAR of the end of a line and the middle of another, moved a line and zoomed the
    /// viewport: the points went where the viewport showed them, the line of the dimension kept its distance on the sheet, the
    /// DIMLFAC went from -12.857 to -7.143 with the scale, and a dimension with a point tied to nothing was left alone.
    /// </summary>
    public class ViewportTransSpatialDimensionTests
    {
        private static readonly ObjectSnap Snaps = new ObjectSnap(Tight)
        {
            Modes = CadSnapModes.Endpoint | CadSnapModes.Midpoint | CadSnapModes.Center | CadSnapModes.Intersection,
        };

        /// <summary>
        /// A drawing whose model space holds a line A from (0, 0) to (100, 0) and a line B from (100, 0) to (100, 50), and whose
        /// layout L holds a viewport 200 by 100 about (120, 70) looking at (50, 25) over a height of 200, a scale of 0.5: the end
        /// (0, 0) of A is at (95, 57.5) on the sheet, the end (100, 50) of B at (145, 82.5).
        /// </summary>
        private static CadDocument Sheet(
            out CadCommandSession session,
            out DxfViewport viewport,
            out string a,
            out string b
        )
        {
            DxfDatabase database = CadTemplate.CreateMetric();
            var lineA = new DxfLine(new GeoPoint3(0, 0, 0), new GeoPoint3(100, 0, 0));
            var lineB = new DxfLine(new GeoPoint3(100, 0, 0), new GeoPoint3(100, 50, 0));
            database.ModelSpace.AppendEntity(lineA);
            database.ModelSpace.AppendEntity(lineB);
            a = lineA.Handle;
            b = lineB.Handle;
            var sheet = new DxfLayout { LayoutName = "L" };
            sheet.AddToLayoutDictionary(database, null);
            sheet.Initialize();
            viewport = new DxfViewport
            {
                CenterPoint = new GeoPoint3(120, 70, 0),
                Width = 200,
                Height = 100,
            };
            sheet.BlockTableRecord.AppendEntity(viewport);
            viewport.ViewCenter = new GeoPoint2(50, 25);
            viewport.ViewHeight = 200;
            viewport.On = true;
            CadDocument document = CadDocument.From(database, "sheet.dxf");
            document.CurrentSpace = document.Spaces.Single(s => s.Name == "L");
            session = Session(document);
            return document;
        }

        /// <summary>A click at a place of the sheet with the object snaps on, as the canvas gives it through the viewport.</summary>
        private static void Click(CadCommandSession session, GeoPoint2 cursor)
        {
            CadDocument document = session.Document;
            CadSpace space = document.CurrentSpace;
            CadSnapResult own = Snaps.Snap(document.GetPickIndex(space), cursor, 2.0);
            CadSnapResult result = CadViewportSnap.Through(document, space, Snaps, own, cursor, 2.0, null, null);
            Assert.NotEqual(CadSnapKind.None, result.Kind);
            Assert.True(session.Feed(result, false));
        }

        /// <summary>The DIMLINEAR of the end of A and the far end of B, its line at 40 on the sheet.</summary>
        private static DxfRotatedDimension Dimensioned(
            CadDocument document,
            CadCommandSession session,
            DxfViewport viewport,
            string a,
            string b
        )
        {
            Run(session, "DIMLINEAR");
            Click(session, P(95.3, 57.3));
            Click(session, P(144.7, 82.2));
            Run(session, P(120, 40));
            return Assert.Single(All<DxfRotatedDimension>(document.CurrentSpace));
        }

        private static string Words(DxfDimension dimension) =>
            dimension.DimBlock.OfType<DxfMText>().Single(m => !m.IsErased).Contents;

        [Fact]
        public void ADimLinearSnappedThroughAViewport_IsTiedToTheModelObjects_AsATransSpatialDimAssoc()
        {
            CadDocument document = Sheet(
                out CadCommandSession session,
                out DxfViewport viewport,
                out string a,
                out string b
            );

            DxfRotatedDimension dimension = Dimensioned(document, session, viewport, a, b);

            DxfDimAssoc assoc = dimension.DimAssoc;
            Assert.NotNull(assoc);
            Assert.True(assoc.IsTransSpace);
            Assert.Equal(3, assoc.AssociativityFlags);
            Assert.Equal(
                new[] { DxfOsnapType.StartPoint, DxfOsnapType.Endpoint },
                assoc.PointReferences.Select(r => r.OsnapType)
            );
            Assert.Equal(new[] { a, b }, assoc.PointReferences.Select(r => r.ObjectHandle));
            Assert.All(assoc.PointReferences, r => Assert.Equal(viewport.Handle, r.ViewportHandle));
            Near(new GeoPoint3(95, 57.5, 0), dimension.XLine1Point, 1E-9);
            Near(new GeoPoint3(145, 82.5, 0), dimension.XLine2Point, 1E-9);

            // The viewport and both lines have the DIMASSOC among their reactors, as AutoCAD's do.
            Assert.Contains(assoc.Handle, viewport.GetPersistentReactorIds());
            Assert.Contains(assoc.Handle, Entity<DxfLine>(document, a).GetPersistentReactorIds());
            Assert.Contains(assoc.Handle, Entity<DxfLine>(document, b).GetPersistentReactorIds());
        }

        [Fact]
        public void ATransSpatialDimension_IsWrittenAsAutoCADWritesIt_AndReadBackTheSame()
        {
            CadDocument document = Sheet(
                out CadCommandSession session,
                out DxfViewport viewport,
                out string a,
                out string b
            );
            Dimensioned(document, session, viewport, a, b);

            string text = document.Database.ToText().Replace("\r\n", "\n");
            DxfDatabase again = DxfDatabase.Parse(text);
            Assert.Equal(text, again.ToText().Replace("\r\n", "\n"));

            // AutoCAD 2021: group 70 of the DIMASSOC is 1 and each reference has the viewport's handle before the object's.
            int at = text.IndexOf("AcDbDimAssoc", System.StringComparison.Ordinal);
            Assert.True(at > 0);
            string[] groups = text.Substring(at).Split('\n').Select(l => l.Trim()).ToArray();
            int seventy = System.Array.IndexOf(groups, "70");
            Assert.Equal("1", groups[seventy + 1]);
            int first = System.Array.IndexOf(groups, "AcDbOsnapPointRef");
            int handles = System.Array.IndexOf(groups, "331", first);
            Assert.Equal(viewport.Handle, groups[handles + 1]);
            Assert.Equal("331", groups[handles + 2]);
            Assert.Equal(a, groups[handles + 3]);

            DxfRotatedDimension reopened = again
                .Layouts.Single(l => l.LayoutName == "L")
                .BlockTableRecord.OfType<DxfRotatedDimension>()
                .Single();
            Assert.True(reopened.DimAssoc.IsTransSpace);
            Assert.All(reopened.DimAssoc.PointReferences, r => Assert.Equal(viewport.Handle, r.ViewportHandle));
            Assert.Equal(new[] { a, b }, reopened.DimAssoc.PointReferences.Select(r => r.ObjectHandle));
        }

        [Fact]
        public void ATransSpatialDimension_ShowsTheModelsSize_ByTheNegativeOfTheViewportsScale()
        {
            CadDocument document = Sheet(
                out CadCommandSession session,
                out DxfViewport viewport,
                out string a,
                out string b
            );

            DxfRotatedDimension dimension = Dimensioned(document, session, viewport, a, b);

            // 50 on the sheet at 1:2 is 100 of the model: AutoCAD sets DIMLFAC to -2 (its xdata ACAD_DIMASSOC_DIMLFAC too).
            Assert.Equal(-2.0, dimension.Dimlfac, 9);
            Assert.StartsWith("100", Words(dimension));
            IReadOnlyList<DxfTypedValue> kept = dimension.GetXDataForApplication("ACAD_DIMASSOC_DIMLFAC");
            Assert.NotNull(kept);
            Assert.Equal(-2.0, double.Parse(kept[1].Value, System.Globalization.CultureInfo.InvariantCulture), 9);
        }

        [Fact]
        public void TheModelLineMoved_CarriesThePointOnTheSheet_TheDimensionLineKeepingItsDistance()
        {
            CadDocument document = Sheet(
                out CadCommandSession session,
                out DxfViewport viewport,
                out string a,
                out string b
            );
            DxfRotatedDimension dimension = Dimensioned(document, session, viewport, a, b);
            string handle = dimension.Handle;
            document.CurrentSpace = document.ModelSpace;
            document.Selection.Set(new[] { a });

            Run(session, "MOVE", P(0, 0), P(0, 10));

            // The end of A is now at (0, 10), shown at (95, 62.5); the line of the dimension, 17.5 below its first origin, at 45.
            var moved = Entity<DxfRotatedDimension>(document, handle);
            Near(new GeoPoint3(95, 62.5, 0), moved.XLine1Point, 1E-9);
            Near(new GeoPoint3(145, 82.5, 0), moved.XLine2Point, 1E-9);
            Assert.Equal(45.0, moved.DimLinePoint.Y, 9);
            Assert.NotNull(moved.DimAssoc);
            Assert.True(moved.DimAssoc.IsTransSpace);

            Run(session, "U");
            var back = Entity<DxfRotatedDimension>(document, handle);
            Near(new GeoPoint3(95, 57.5, 0), back.XLine1Point, 1E-9);
            Assert.Equal(40.0, back.DimLinePoint.Y, 9);
        }

        [Fact]
        public void TheViewportZoomed_MovesNothingUntilDimRegen_ThenTheDimensionIsWhereTheViewportShowsItAtTheNewScale()
        {
            CadDocument document = Sheet(
                out CadCommandSession session,
                out DxfViewport viewport,
                out string a,
                out string b
            );
            DxfRotatedDimension dimension = Dimensioned(document, session, viewport, a, b);
            string handle = dimension.Handle;

            // The viewport shows model space 100 high about (0, 0): 1:1, so that (0, 0) is at (120, 70) and (100, 50) at (220, 120).
            Assert.True(document.SetViewportView(viewport.Handle, P(0, 0), 100.0, "ZOOM").Succeeded);

            var before = Entity<DxfRotatedDimension>(document, handle);
            Near(new GeoPoint3(95, 57.5, 0), before.XLine1Point, 1E-9);
            Assert.Equal(-2.0, before.Dimlfac, 9);

            Run(session, "DIMREGEN");

            var after = Entity<DxfRotatedDimension>(document, handle);
            Near(new GeoPoint3(120, 70, 0), after.XLine1Point, 1E-9);
            Near(new GeoPoint3(220, 120, 0), after.XLine2Point, 1E-9);

            // The line of the dimension keeps its distance on the sheet, 17.5 from the first origin, and the size shown is the model's.
            Assert.Equal(52.5, after.DimLinePoint.Y, 9);
            Assert.Equal(-1.0, after.Dimlfac, 9);
            Assert.StartsWith("100", Words(after));
            Assert.True(after.DimAssoc.IsTransSpace);
            Assert.Equal("DIMREGEN", document.NextUndo.Name);

            Run(session, "U");
            Near(new GeoPoint3(95, 57.5, 0), Entity<DxfRotatedDimension>(document, handle).XLine1Point, 1E-9);
        }

        [Fact]
        public void ADimContinueAfterIt_ClickedThroughTheViewport_IsTiedThroughItToo()
        {
            CadDocument document = Sheet(
                out CadCommandSession session,
                out DxfViewport viewport,
                out string a,
                out string b
            );
            Dimensioned(document, session, viewport, a, b);

            // The middle (50, 0) of A is shown at (120, 57.5).
            Run(session, "DIMCONTINUE");
            Click(session, P(120.2, 57.7));
            Run(session, Enter);

            DxfRotatedDimension[] both = All<DxfRotatedDimension>(document.CurrentSpace).ToArray();
            Assert.Equal(2, both.Length);
            DxfDimAssoc assoc = both[1].DimAssoc;
            Assert.NotNull(assoc);
            Assert.True(assoc.IsTransSpace);
            Assert.Equal(new[] { b, a }, assoc.PointReferences.Select(r => r.ObjectHandle));
            Assert.Equal(DxfOsnapType.Midpoint, assoc.PointReferences[1].OsnapType);
            Assert.All(assoc.PointReferences, r => Assert.Equal(viewport.Handle, r.ViewportHandle));
            Assert.Equal(-2.0, both[1].Dimlfac, 9);
        }

        [Fact]
        public void TheViewportErased_UntiesTheDimension_WhichKeepsItsPoints()
        {
            CadDocument document = Sheet(
                out CadCommandSession session,
                out DxfViewport viewport,
                out string a,
                out string b
            );
            DxfRotatedDimension dimension = Dimensioned(document, session, viewport, a, b);
            string handle = dimension.Handle;
            Assert.NotNull(dimension.DimAssoc);
            document.Selection.Set(new[] { viewport.Handle });

            Run(session, "ERASE", Enter);

            var kept = Entity<DxfRotatedDimension>(document, handle);
            Assert.Null(kept.DimAssoc);
            Near(new GeoPoint3(95, 57.5, 0), kept.XLine1Point, 1E-9);
        }

        [Fact]
        public void ADimensionOfAPointOfThePaperAndAPointThroughTheViewport_IsTiedToNothing()
        {
            CadDocument document = Sheet(
                out CadCommandSession session,
                out DxfViewport viewport,
                out string a,
                out string b
            );
            Run(session, "LINE", P(10, 10), P(30, 10), Enter);
            var paperLine = Assert.Single(All<DxfLine>(document.CurrentSpace));

            Run(session, "DIMLINEAR");
            Click(session, P(10.2, 10.2));
            Click(session, P(144.7, 82.2));
            Run(session, P(60, 40));

            Assert.Null(Assert.Single(All<DxfRotatedDimension>(document.CurrentSpace)).DimAssoc);
        }
    }
}
