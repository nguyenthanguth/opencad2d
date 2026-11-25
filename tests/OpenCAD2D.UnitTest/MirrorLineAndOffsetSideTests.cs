using System;
using System.Linq;
using GeometryHelper.Dxf;
using GeometryHelper.Geometry;
using Xunit;
using static OpenCAD2D.UnitTest.EditingDrawings;

namespace OpenCAD2D.UnitTest
{
    /// <summary>
    /// The two points of a mirror line the library cannot give a direction to, and the side point of an offset that lies on
    /// the curve: what the typed MIRROR says to the first, and what the call <see cref="CadEditor.Offset"/> says to the
    /// second. The session's tolerance is <see cref="EditingDrawings.Tight"/> (an EqualPoint of 1E-9), under which a mirror
    /// line of 1E-6 is a line by the tolerance and not by the library, which cannot normalise a vector shorter than about 2E-5.
    /// </summary>
    public class MirrorLineAndOffsetSideTests
    {
        private static CadCommandSession Typed(CadDocument document)
        {
            CadCommandSession session = Session(document);
            session.Tolerance = Tight;
            return session;
        }

        [Fact]
        public void ASecondPointAMillionthFromTheFirst_IsRefusedByMirrorAsTooShort_WhichAsksAgain_AndMakesTheMirrorOnceAGoodPointIsGiven()
        {
            CadDocument document = Plan();
            CadCommandSession session = Typed(document);
            string start = document.Database.ToText();

            Run(session, "MIRROR", new[] { "A2" }, Enter, P(0, 0), P(1E-6, 0));

            // 1E-6 is above the tolerance's 1E-9 and under what the library can normalise: the command says so, writes no error,
            // changes nothing and asks for the second point again.
            Assert.Contains("Invalid point: the mirror line is too short.", session.History);
            Assert.DoesNotContain(session.History, line => line.Contains("*Error*"));
            Assert.True(session.IsRunning);
            Assert.Equal(start, document.Database.ToText());

            Run(session, P(10, 0), Enter);

            // Across the x axis the line A2 from (0, 0) to (0, 50) gives a line from (0, 0) to (0, -50), the source kept.
            Assert.False(session.IsRunning);
            Assert.Equal(9, document.Database.ModelSpace.Count());
            DxfLine copy = document.Database.ModelSpace.OfType<DxfLine>().Last(l => !l.IsErased);
            Near(new GeoPoint3(0, 0, 0), copy.StartPoint);
            Near(new GeoPoint3(0, -50, 0), copy.EndPoint);
        }

        [Fact]
        public void ASecondPointOnTheFirst_IsStillRefusedByMirrorAsNeedingTwoPointsApart()
        {
            CadDocument document = Plan();
            CadCommandSession session = Typed(document);

            Run(session, "MIRROR", new[] { "A2" }, Enter, P(0, 0), P(5E-10, 0));

            Assert.Contains("Invalid point: the mirror line needs two points apart.", session.History);
            Assert.True(session.IsRunning);
        }

        [Fact]
        public void AnOffsetWhoseSidePointLiesOnTheCurve_IsRefusedAsNamingNoSide_ChangingNothing()
        {
            CadDocument document = Plan();
            CadCommandSession session = Typed(document);
            string text = document.Database.ToText();

            // (50, 0) is on the line A1 from (0, 0) to (100, 0): there is no side to offset to.
            ArgumentException refused = Assert.Throws<ArgumentException>(() =>
                session.Editor.Offset(Entity<DxfLine>(document, "A1"), 5.0, new GeoPoint2(50, 0))
            );

            Assert.Contains("lies on the curve", refused.Message, StringComparison.Ordinal);
            Assert.Equal(text, document.Database.ToText());
            Assert.False(document.CanUndo);
        }

        [Fact]
        public void AnOffsetWhoseSidePointIsAHairOffTheCurve_IsAnOffset()
        {
            CadDocument document = Plan();
            CadCommandSession session = Typed(document);

            DxfCurve made = Assert.Single(
                session.Editor.Offset(Entity<DxfLine>(document, "A1"), 5.0, new GeoPoint2(50, 1E-6))
            );

            // A millionth is a thousand times the tolerance's EqualPoint: the point names the side above the line.
            Near(new GeoPoint3(0, 5, 0), Assert.IsType<DxfLine>(made).StartPoint);
        }
    }
}
