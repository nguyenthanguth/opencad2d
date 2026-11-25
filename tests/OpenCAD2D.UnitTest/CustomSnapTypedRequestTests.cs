using System;
using System.Collections.Generic;
using GeometryHelper.Dxf;
using GeometryHelper.Geometry;
using OpenCAD2D.Plugins;
using Xunit;
using static OpenCAD2D.UnitTest.EditingDrawings;

namespace OpenCAD2D.UnitTest
{
    /// <summary>
    /// A finder is told whether it is asked for a point typed after the snap's keyword or for the cursor moving over the drawing
    /// (<c>CadSnapRequest.IsTyped</c>, PW3-D7): the app waits a moment for a tool's answer to a typed point, and never for the
    /// cursor's.
    /// </summary>
    public class CustomSnapTypedRequestTests
    {
        private static CadDocument OneCircle()
        {
            DxfDatabase database = CadTemplate.CreateMetric();
            database.ModelSpace.AppendEntity(new DxfCircle(GeoPoint3.Origin, new GeoVector3(0, 0, 1), 10));
            return CadDocument.From(database, "typed-request.dxf");
        }

        private static CadCustomSnap CircleTop(List<CadSnapRequest> seen)
        {
            var snap = new CadCustomSnap("Circle top", "CTOP", request =>
            {
                seen.Add(request);
                return request.Entity is DxfCircle circle
                    ? new[] { P(circle.Center.X, circle.Center.Y + circle.Radius) }
                    : Array.Empty<GeoPoint2>();
            });
            snap.EntityTypes.Add(typeof(DxfCircle));
            return snap;
        }

        [Fact]
        public void AFinderAskedForATypedPoint_IsToldSo()
        {
            CadDocument document = OneCircle();
            CadCommandSession session = Session(document);
            session.SnapAperture = 2.0;
            Run(session, "LINE");
            var seen = new List<CadSnapRequest>();

            session.CustomSnapOverride = CircleTop(seen);
            Run(session, "1,9");

            Assert.NotEmpty(seen);
            Assert.All(seen, request => Assert.True(request.IsTyped));
        }

        [Fact]
        public void AFinderAskedByTheCursor_IsNotToldItIsTyped()
        {
            CadDocument document = OneCircle();
            var seen = new List<CadSnapRequest>();
            var snaps = new ObjectSnap(Tight)
            {
                Modes = CadSnapModes.None,
                CustomSnaps = new[] { CircleTop(seen) },
                EntityOf = document.FindEntity,
                Document = document,
            };

            CadSnapResult result = snaps.Snap(document.GetPickIndex(document.ModelSpace), P(0.2, 9.8), 2.0);

            Assert.Equal(CadSnapKind.Custom, result.Kind);
            Assert.NotEmpty(seen);
            Assert.All(seen, request => Assert.False(request.IsTyped));
        }
    }
}
