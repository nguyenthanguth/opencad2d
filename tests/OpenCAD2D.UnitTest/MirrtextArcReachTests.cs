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
    /// MIRROR with MIRRTEXT 0 of a TEXT in the built-in font, opencad2d.shx, whose C, 2 and 3 have arcs: AutoCAD 2021 (console,
    /// probe4) turns the text over about, and keeps the ink of its words by, the box (textbox) it measures, in which an arc is
    /// reached by the points it bulges to, not by the polyline the display draws. "ABC123" 10 high at the origin has its box
    /// from (1.0000, -0.6299) to (42.2488, 10.7063); "HILT", whose letters have no arc, from (1.0, 0.0) to (21.5, 10.0).
    /// </summary>
    public class MirrtextArcReachTests
    {
        private const double Degree = Math.PI / 180.0;

        /// <summary>A drawing of one TEXT at the origin in opencad2d.shx, MIRRTEXT 0, its fonts the Fonts folder shipped beside the dll, in which the database finds the font that puts a justified text where its words stand.</summary>
        private static CadDocument InBuiltInFont(
            FontFolder folder,
            string words,
            double height,
            Action<DxfDBText> justify
        )
        {
            var db = new DxfDatabase("AC1027")
            {
                Mirrtext = false,
                FontSearchPath = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "Fonts"),
            };
            db.TextStyleTable["Standard"].FileName = "opencad2d.shx";
            var text = new DxfDBText
            {
                TextString = words,
                Height = height,
                Position = new GeoPoint3(0, 0, 0),
            };
            db.ModelSpace.AppendEntity(text);
            justify(text);
            CadDocument document = CadDocument.From(db, "arcreach.dxf");
            document.Fonts = new CadFonts(new[] { folder.FullPath }, null, null);
            return document;
        }

        /// <summary>Mirrors the one TEXT of the document across the line through two points, keeping the source; gives the copy.</summary>
        private static DxfDBText MirroredCopy(CadDocument document, GeoPoint2 first, GeoPoint2 second)
        {
            string source = All<DxfDBText>(document.ModelSpace)[0].Handle;
            Run(Session(document), "MIRROR", new[] { source }, Enter, first, second, Enter);
            return All<DxfDBText>(document.ModelSpace).Single(e => e.Handle != source);
        }

        /// <summary>The line through (0, 50) at thirty degrees, as its second point 100 along.</summary>
        private static GeoPoint2 ThirtyDegreesFrom50() =>
            P(100 * Math.Cos(30 * Degree), 50 + 100 * Math.Sin(30 * Degree));

        private static void ReadsAt(DxfDBText text, double degrees)
        {
            Assert.True(text.Normal.Z > 0.0, "The normal points down: " + text.Normal + ".");
            Assert.False(text.IsMirroredInX);
            Assert.False(text.IsMirroredInY);
            double turned = (text.Rotation / Degree % 360.0 + 360.0) % 360.0;
            Assert.True(Math.Abs(turned - degrees) < 1E-4, "Turned " + turned + " degrees, not " + degrees + ".");
        }

        private static GeoPoint3 P3(double x, double y) => new GeoPoint3(x, y, 0);

        private static void Place(DxfDBText text, DxfTextHorizontalMode horizontal, DxfTextVerticalMode vertical)
        {
            text.HorizontalMode = horizontal;
            text.VerticalMode = vertical;
            text.AlignmentPoint = new GeoPoint3(0, 0, 0);
        }

        [Fact]
        public void WordsWithArcs_MirroredAcrossALevelLine_StandByTheBoxOfTheirArcsMirrored()
        {
            // AutoCAD 2021: "ABC123" 10 high, box -0.6299 to 10.7063 above its baseline, across y = 50 starts at
            // y = 100 - (-0.6299 + 10.7063) = 89.9236. The strokes the display draws (arcs cut short of their bulges) would
            // put it near 90.07, and the middle of its height at 90.0.
            using (var folder = new FontFolder())
            {
                DxfDBText copy = MirroredCopy(InBuiltInFont(folder, "ABC123", 10, t => { }), P(0, 50), P(100, 50));

                ReadsAt(copy, 0);
                Near(P3(0, 89.923622), copy.Position, 1E-4);
            }
        }

        [Fact]
        public void WordsWithArcs_MirroredAcrossAnUpright_StandByTheBoxOfTheirArcsMirrored()
        {
            // AutoCAD 2021: the box runs 1.0 to 42.2488 along the baseline, so across x = 50 the copy starts at
            // x = 100 - (1.0 + 42.2488) = 56.7512. The display's polylines end short of the bulge of the 3.
            using (var folder = new FontFolder())
            {
                DxfDBText copy = MirroredCopy(InBuiltInFont(folder, "ABC123", 10, t => { }), P(50, 0), P(50, 100));

                ReadsAt(copy, 0);
                Near(P3(56.751181, 0), copy.Position, 1E-4);
            }
        }

        [Fact]
        public void WordsWithArcs_MirroredAcrossAThirtyDegreeLine_ReadAtSixtyDegrees_ByTheBoxOfTheirArcs()
        {
            // AutoCAD 2021: across the line from (0, 50) at 30 degrees the copy reads at 60 and starts at
            // (-34.574871, 69.961811): the baseline turned over the middle of the box, 5.0382 up, and then mirrored.
            using (var folder = new FontFolder())
            {
                DxfDBText copy = MirroredCopy(
                    InBuiltInFont(folder, "ABC123", 10, t => { }),
                    P(0, 50),
                    ThirtyDegreesFrom50()
                );

                ReadsAt(copy, 60);
                Near(P3(-34.574871, 69.961811), copy.Position, 1E-4);
            }
        }

        [Fact]
        public void WordsWithArcs_TwoAndAHalfHigh_MirroredAcrossAnUpright_StandByTheBoxOfTheirArcs()
        {
            // AutoCAD 2021: the box scales with the height, 0.25 to 10.5622 at 2.5 high, so across x = 100 the copy starts at
            // x = 200 - (0.25 + 10.5622) = 189.1878.
            using (var folder = new FontFolder())
            {
                DxfDBText copy = MirroredCopy(InBuiltInFont(folder, "ABC123", 2.5, t => { }), P(100, 0), P(100, 10));

                ReadsAt(copy, 0);
                Near(P3(189.187795, 0), copy.Position, 1E-4);
            }
        }

        [Fact]
        public void WordsWithoutArcs_MirroredAcrossALevelLineAnUprightAndASlantedLine_StandByTheirStrokes()
        {
            // The control, in which the strokes and the box agree: "HILT" 10 high has its box from (1.0, 0.0) to (21.5, 10.0).
            // AutoCAD 2021: across y = 50 it starts at y = 100 - 10 = 90.0, across x = 50 at x = 100 - 22.5 = 77.5, and across
            // the thirty degree line at (-34.641016, 70.0).
            using (var folder = new FontFolder())
            {
                DxfDBText level = MirroredCopy(InBuiltInFont(folder, "HILT", 10, t => { }), P(0, 50), P(100, 50));
                DxfDBText upright = MirroredCopy(InBuiltInFont(folder, "HILT", 10, t => { }), P(50, 0), P(50, 100));
                DxfDBText slanted = MirroredCopy(
                    InBuiltInFont(folder, "HILT", 10, t => { }),
                    P(0, 50),
                    ThirtyDegreesFrom50()
                );

                Near(P3(0, 90.0), level.Position, 1E-4);
                Near(P3(77.5, 0), upright.Position, 1E-4);
                ReadsAt(slanted, 60);
                Near(P3(-34.641016, 70.0), slanted.Position, 1E-4);
            }
        }

        /// <summary>"ABC123" 10 high justified as given on (0, 0), mirrored across the line; checks where the source stands first, as AutoCAD puts it.</summary>
        private static DxfDBText MirroredJustified(
            DxfTextHorizontalMode horizontal,
            DxfTextVerticalMode vertical,
            GeoPoint3 sourceStartsAt,
            GeoPoint2 through
        )
        {
            using (var folder = new FontFolder())
            {
                CadDocument document = InBuiltInFont(folder, "ABC123", 10, t => Place(t, horizontal, vertical));
                Near(sourceStartsAt, All<DxfDBText>(document.ModelSpace)[0].Position, 1E-4);
                return MirroredCopy(document, P(0, 50), through);
            }
        }

        [Fact]
        public void WordsWithArcsJustifiedOnTheirMiddle_MirroredAcrossALevelLine_StandByTheBoxMirrored_ThePointOfAMiddleKept()
        {
            // AutoCAD 2021, "Middle" (horizontal 4): its point (0, 0) is the middle of the box, 5.0382 above the baseline, so the
            // source starts at (-21.6244, -5.0382) and the copy, the box mirrored across y = 50, at (-21.6244, 94.9618) with its
            // point at (0, 100), the mirror of (0, 0).
            DxfDBText copy = MirroredJustified(
                DxfTextHorizontalMode.TextMid,
                DxfTextVerticalMode.TextBase,
                P3(-21.624409, -5.038189),
                P(100, 50)
            );

            ReadsAt(copy, 0);
            Near(P3(-21.624409, 94.961811), copy.Position, 1E-4);
            Near(P3(0, 100.0), copy.AlignmentPoint, 1E-4);
        }

        [Fact]
        public void WordsWithArcsJustifiedOnTheirMiddleCentre_MirroredAcrossALevelLine_StandByTheBoxMirrored_ThePointOffTheMirror()
        {
            // AutoCAD 2021, "Middle centre" (horizontal 1, vertical 2): its point (0, 0) is the middle of the height, 5.0 above
            // the baseline, not of the box, so the source starts at (-21.6244, -5.0) and the copy, the box mirrored, at
            // (-21.6244, 94.9236), 5.0 below its point (0, 99.9236): 0.0764 short of the mirror of (0, 0), the box's 5.0382
            // half-height less the 5.0.
            DxfDBText copy = MirroredJustified(
                DxfTextHorizontalMode.TextCenter,
                DxfTextVerticalMode.TextVerticalMid,
                P3(-21.624409, -5.0),
                P(100, 50)
            );

            ReadsAt(copy, 0);
            Near(P3(-21.624409, 94.923622), copy.Position, 1E-4);
            Near(P3(0, 99.923622), copy.AlignmentPoint, 1E-4);
        }

        [Fact]
        public void WordsWithArcsJustifiedOnTheirMiddle_MirroredAcrossAThirtyDegreeLine_StandByTheBoxMirrored_ThePointOfAMiddleKept()
        {
            // AutoCAD 2021, across the line from (0, 50) at 30 degrees: "Middle" reads at 60, starts at
            // (-49.750275, 53.753618), its point at (-43.301270, 75.0), the mirror of (0, 0).
            DxfDBText copy = MirroredJustified(
                DxfTextHorizontalMode.TextMid,
                DxfTextVerticalMode.TextBase,
                P3(-21.624409, -5.038189),
                ThirtyDegreesFrom50()
            );

            ReadsAt(copy, 60);
            Near(P3(-49.750275, 53.753618), copy.Position, 1E-4);
            Near(P3(-43.301270, 75.0), copy.AlignmentPoint, 1E-4);
        }

        [Fact]
        public void WordsWithArcsJustifiedOnTheirMiddleCentre_MirroredAcrossAThirtyDegreeLine_StandByTheBoxMirrored_ThePointOffTheMirror()
        {
            // AutoCAD 2021, across the line from (0, 50) at 30 degrees: "Middle centre" reads at 60, starts at
            // (-49.717203, 53.734523), its point at (-43.235125, 74.961811), 0.0764 off the mirror of (0, 0), (-43.3013, 75.0).
            DxfDBText copy = MirroredJustified(
                DxfTextHorizontalMode.TextCenter,
                DxfTextVerticalMode.TextVerticalMid,
                P3(-21.624409, -5.0),
                ThirtyDegreesFrom50()
            );

            ReadsAt(copy, 60);
            Near(P3(-49.717203, 53.734523), copy.Position, 1E-4);
            Near(P3(-43.235125, 74.961811), copy.AlignmentPoint, 1E-4);
        }
    }
}
