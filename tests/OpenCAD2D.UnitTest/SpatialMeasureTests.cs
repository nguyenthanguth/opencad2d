using System;
using System.Linq;
using GeometryHelper;
using GeometryHelper.Dxf;
using GeometryHelper.Geometry;
using Xunit;
using static OpenCAD2D.UnitTest.EditingDrawings;
using static OpenCAD2D.UnitTest.SpatialDrawings;

namespace OpenCAD2D.UnitTest
{
    /// <summary>
    /// <see cref="CadMeasure"/>: the box a thing would take, found before it is added, equals the box
    /// <see cref="CadSpatial.Bounds(CadDocument, CadSpace)"/> gives for the same thing once it is added, within 1E-6, for each
    /// kind: SHX and TrueType words, turned and justified ones, the columns of an MTEXT, a dimension's picture and a
    /// multileader with its landing; and measuring changes nothing in the drawing.
    /// </summary>
    public sealed class SpatialMeasureTests : IDisposable
    {
        private readonly SpatialDrawings _drawings = new SpatialDrawings();

        public void Dispose() => _drawings.Dispose();

        /// <summary>Adds a copy of an entity as the Python commit adds one (a dimension with the picture DIM draws) and gives its box as the drawing draws it.</summary>
        private static DisplayBox AddedBox(CadDocument document, DxfEntity entity)
        {
            DxfEntity copy = entity.Clone();

            if (copy is DxfDimension dimension)
            {
                var picture = new DxfBlockTableRecord { Name = "*D" };
                document.Database.BlockTable.Add(picture);
                dimension.DimBlock = picture;
                document.Database.ModelSpace.AppendEntity(copy);
                Assert.True(CadDimensionPicture.Redraw(dimension));
            }
            else
            {
                document.Database.ModelSpace.AppendEntity(copy);
            }

            document.Regen();
            CadBoundsTable table = CadSpatial.Bounds(document, new[] { copy.Handle });
            return table.Count == 0 ? DisplayBox.Empty : table.BoxOf(0);
        }

        private static void AssertSame(DisplayBox wanted, DisplayBox got)
        {
            Assert.Equal(wanted.IsEmpty, got.IsEmpty);

            if (!wanted.IsEmpty)
            {
                Assert.Equal(wanted.MinX, got.MinX, 6);
                Assert.Equal(wanted.MinY, got.MinY, 6);
                Assert.Equal(wanted.MaxX, got.MaxX, 6);
                Assert.Equal(wanted.MaxY, got.MaxY, 6);
            }
        }

        private static DxfDBText Plain(string words, double x, double y, double height, double degrees = 0.0) =>
            new DxfDBText
            {
                TextString = words,
                Position = new GeoPoint3(x, y, 0),
                Height = height,
                Rotation = degrees * Math.PI / 180.0,
                TextStyleName = "OC_SHX",
            };

        private CadMeasurement Measured(CadDocument document, DxfEntity entity, out DisplayBox added)
        {
            CadMeasurement measured = CadMeasure.Of(document, entity);
            added = AddedBox(document, entity);
            return measured;
        }

        [Fact]
        public void AText_HasTheBoxItHasOnceAdded_AndOneFrameOfItsWords()
        {
            CadDocument document = _drawings.Blank();

            CadMeasurement measured = Measured(document, Plain("SIDE FOOD", 12, 30, 5.0), out DisplayBox added);

            AssertSame(added, measured.Box);
            Assert.False(measured.Box.IsEmpty);
            CadFrame frame = Assert.Single(measured.Frames);
            Assert.Equal(0.0, frame.Rotation, 9);
            Assert.True(frame.Width > 5.0 && frame.Height > 3.0, "frame " + frame.Width + " by " + frame.Height);
        }

        [Fact]
        public void ATurnedText_IsTurnedInItsFrame_AndEqualsTheAddedBox()
        {
            CadDocument document = _drawings.Blank();

            CadMeasurement measured = Measured(document, Plain("SIDE FOOD", 50, 50, 5.0, 30.0), out DisplayBox added);

            AssertSame(added, measured.Box);
            CadFrame frame = Assert.Single(measured.Frames);
            Assert.Equal(30.0 * Math.PI / 180.0, frame.Rotation, 9);
            Assert.Equal(frame.Box.MinX, measured.Box.MinX, 6);
        }

        [Theory]
        [InlineData(DxfTextHorizontalMode.TextCenter, DxfTextVerticalMode.TextBase)]
        [InlineData(DxfTextHorizontalMode.TextRight, DxfTextVerticalMode.TextTop)]
        [InlineData(DxfTextHorizontalMode.TextMid, DxfTextVerticalMode.TextVerticalMid)]
        public void AJustifiedText_EqualsTheAddedBox_AndStandsAboutItsAlignmentPoint(
            DxfTextHorizontalMode horizontal,
            DxfTextVerticalMode vertical
        )
        {
            CadDocument document = _drawings.Blank();
            DxfDBText text = Plain("SIDE FOOD", 100, 100, 5.0, 10.0);
            text.HorizontalMode = horizontal;
            text.VerticalMode = vertical;
            text.AlignmentPoint = new GeoPoint3(100, 100, 0);

            CadMeasurement measured = Measured(document, text, out DisplayBox added);

            AssertSame(added, measured.Box);
            Assert.False(measured.Box.IsEmpty);
        }

        [Fact]
        public void ATrueTypeText_IsMeasuredByItsFont_AsTheAddedOneIs()
        {
            var database = new DxfDatabase("AC1027");
            database.TextStyleTable.Add(
                new DxfTextStyleTableRecord
                {
                    Name = "ARIAL",
                    Font = new DxfFontDescriptor("Arial", false, false, 0, 34),
                }
            );
            CadDocument document = CadDocument.From(database, "ttf.dxf");
            document.Fonts = new CadFonts(null, CadFonts.BuiltInName, null, f => f == "Arial");
            DxfDBText text = Plain("Wiggle", 5, 5, 10.0, 15.0);
            text.TextStyleName = "ARIAL";

            CadMeasurement measured = Measured(document, text, out DisplayBox added);

            AssertSame(added, measured.Box);
            Assert.True(
                measured.Box.Width > 10.0 * 3.0,
                "Arial words are wider than a guess of nothing: " + measured.Box.Width
            );
        }

        [Fact]
        public void AnMText_HasTheColumnsOfItsLines_AsTheAddedOneHas()
        {
            CadDocument document = _drawings.Blank();
            var mtext = new DxfMText
            {
                Contents = "SIDE FOOD SIDE FOOD SIDE FOOD SIDE",
                Location = new GeoPoint3(10, 80, 0),
                TextHeight = 5.0,
                Width = 60.0,
                TextStyleName = "OC_SHX",
            };

            CadMeasurement measured = Measured(document, mtext, out DisplayBox added);

            AssertSame(added, measured.Box);
            Assert.True(measured.Frames.Count >= 2, "wrapped at 60 it has " + measured.Frames.Count + " lines");
            Assert.True(measured.Box.Width <= 60.0 + 1E-6 + 20.0, "the box is of the lines, about the width asked");
        }

        [Fact]
        public void AnMTextTurnedAndAttachedAtItsMiddle_EqualsTheAddedBox()
        {
            CadDocument document = _drawings.Blank();
            var mtext = new DxfMText
            {
                Contents = "SIDE FOOD SIDE FOOD",
                Location = new GeoPoint3(40, 40, 0),
                TextHeight = 4.0,
                Width = 50.0,
                Rotation = 20.0 * Math.PI / 180.0,
                Attachment = DxfAttachmentPoint.MiddleCenter,
                TextStyleName = "OC_SHX",
            };

            CadMeasurement measured = Measured(document, mtext, out DisplayBox added);

            AssertSame(added, measured.Box);
            Assert.All(measured.Frames, f => Assert.Equal(20.0 * Math.PI / 180.0, f.Rotation, 9));
        }

        [Fact]
        public void AnMTextInTwoColumns_HasTheBoxOfItsColumns_AndTheGapBetweenThemIsNoWord()
        {
            CadDocument document = _drawings.Blank();
            var mtext = new DxfMText
            {
                Contents = @"SIDE FOOD\PSIDE FOOD\PSIDE FOOD\PSIDE FOOD",
                Location = new GeoPoint3(0, 200, 0),
                TextHeight = 5.0,
                Width = 60.0,
                Height = 20.0,
                TextStyleName = "OC_SHX",
            };
            mtext.SetStaticColumns(60.0, 10.0, 2);

            CadMeasurement measured = Measured(document, mtext, out DisplayBox added);

            AssertSame(added, measured.Box);
            Assert.True(measured.Box.Width > 70.0, "two columns, the second beyond the first: " + measured.Box.Width);
            Assert.True(measured.Frames.Count >= 2);
            Assert.True(
                measured.Frames.Select(f => f.Box).OrderBy(b => b.MinX).First().MaxX < measured.Box.MinX + 60.0 + 1E-6,
                "the first column's words end in the first column"
            );
        }

        [Fact]
        public void ADimensionWhoseWordsAreMovedAndTurned_HasTheBoxOfWhereTheyStand()
        {
            CadDocument document = _drawings.Blank();
            var moved = new DxfRotatedDimension(
                0.0,
                new GeoPoint3(0, 0, 0),
                new GeoPoint3(100, 0, 0),
                new GeoPoint3(100, 20, 0),
                "<> TYP",
                null
            )
            {
                DimensionStyleName = "Standard",
            };
            moved.TextPosition = new GeoPoint3(150, 60, 0);
            moved.UsingDefaultTextPosition = false;
            moved.TextRotation = 30.0 * Math.PI / 180.0;
            var level = new DxfRotatedDimension(
                0.0,
                new GeoPoint3(0, 0, 0),
                new GeoPoint3(100, 0, 0),
                new GeoPoint3(100, 20, 0),
                "<> TYP",
                null
            )
            {
                DimensionStyleName = "Standard",
            };

            CadMeasurement measured = Measured(document, moved, out DisplayBox added);
            DisplayBox plain = CadMeasure.Of(document, level).Box;

            AssertSame(added, measured.Box);
            Assert.NotEqual(plain.MaxY, measured.Box.MaxY, 3);
            Assert.Contains(measured.Frames, f => Math.Abs(f.Rotation - 30.0 * Math.PI / 180.0) < 1E-6);
        }

        [Fact]
        public void ALinearDimension_HasTheBoxOfItsPicture_AndOneFrameForItsWords()
        {
            CadDocument document = _drawings.Blank();
            var dimension = new DxfRotatedDimension(
                0.0,
                new GeoPoint3(0, 0, 0),
                new GeoPoint3(100, 0, 0),
                new GeoPoint3(100, 20, 0),
                string.Empty,
                null
            )
            {
                DimensionStyleName = "Standard",
            };

            CadMeasurement measured = Measured(document, dimension, out DisplayBox added);

            AssertSame(added, measured.Box);
            Assert.True(measured.Box.MinX <= 0.0 + 1E-6 && measured.Box.MaxX >= 100.0 - 1E-6);
            Assert.NotEmpty(measured.Frames);
        }

        [Fact]
        public void ADimensionInAStyleOfItsOwn_IsDrawnInThatStyle_AndEqualsTheAddedBox()
        {
            CadDocument document = _drawings.Blank();
            DxfDatabase database = document.Database;
            var words = new DxfTextStyleTableRecord { Name = "OC_DIM", FileName = "test.shx" };
            database.TextStyleTable.Add(words);
            var style = new DxfDimStyleTableRecord { Name = "BIG" };
            database.DimStyleTable.Add(style);
            style.Dimtxt = 7.5;
            style.Dimasz = 6.0;
            style.Dimscale = 2.0;
            style.Dimtxsty = words;
            style.Dimtad = 1;
            style.Dimdec = 3;
            var dimension = new DxfAlignedDimension(
                new GeoPoint3(0, 0, 0),
                new GeoPoint3(30, 40, 0),
                new GeoPoint3(40, 5, 0),
                "<> TYP",
                null
            )
            {
                DimensionStyleName = "BIG",
            };

            CadMeasurement measured = Measured(document, dimension, out DisplayBox added);
            CadDocument standard = _drawings.Blank();
            standard.Database.TextStyleTable.Add(
                new DxfTextStyleTableRecord { Name = "OC_DIM", FileName = "test.shx" }
            );
            DxfDimStyleTableRecord plain = new DxfDimStyleTableRecord { Name = "BIG" };
            standard.Database.DimStyleTable.Add(plain);
            var asStandard = new DxfAlignedDimension(
                new GeoPoint3(0, 0, 0),
                new GeoPoint3(30, 40, 0),
                new GeoPoint3(40, 5, 0),
                "<> TYP",
                null
            )
            {
                DimensionStyleName = "BIG",
            };
            DisplayBox plainBox = CadMeasure.Of(standard, asStandard).Box;

            AssertSame(added, measured.Box);
            Assert.True(
                measured.Box.Width > plainBox.Width + 1.0 || measured.Box.Height > plainBox.Height + 1.0,
                "the style's text 7.5 high at scale 2 draws more than the default style does"
            );
        }

        [Fact]
        public void ARadialAndAnAngularDimension_EqualTheirAddedBoxes()
        {
            CadDocument document = _drawings.Blank();
            var radial = new DxfRadialDimension(
                new GeoPoint3(50, 50, 0),
                new GeoPoint3(70, 70, 0),
                0.0,
                string.Empty,
                null
            )
            {
                DimensionStyleName = "Standard",
            };
            var angular = new DxfPoint3AngularDimension(
                new GeoPoint3(0, 0, 0),
                new GeoPoint3(100, 0, 0),
                new GeoPoint3(0, 100, 0),
                new GeoPoint3(50, 50, 0),
                string.Empty,
                null
            )
            {
                DimensionStyleName = "Standard",
            };

            CadMeasurement one = Measured(document, radial, out DisplayBox radialAdded);
            CadMeasurement two = Measured(document, angular, out DisplayBox angularAdded);

            AssertSame(radialAdded, one.Box);
            AssertSame(angularAdded, two.Box);
            Assert.False(one.Box.IsEmpty);
            Assert.False(two.Box.IsEmpty);
        }

        [Fact]
        public void ADimensionNamingAStyleTheDrawingLacks_IsRefused_AsTheCommitRefusesIt()
        {
            CadDocument document = _drawings.Blank();
            var dimension = new DxfRotatedDimension(
                0.0,
                new GeoPoint3(0, 0, 0),
                new GeoPoint3(100, 0, 0),
                new GeoPoint3(100, 20, 0),
                string.Empty,
                null
            )
            {
                DimensionStyleName = "NOPE",
            };

            ArgumentException refused = Assert.Throws<ArgumentException>(() => CadMeasure.Of(document, dimension));

            Assert.Contains("NOPE", refused.Message, StringComparison.Ordinal);
        }

        [Fact]
        public void ADimensionOnAFrozenLayer_DrawsNothing_AsTheAddedOneDoesNot()
        {
            CadDocument document = _drawings.Blank();
            var layer = new DxfLayerTableRecord { Name = "GONE", IsFrozen = true };
            document.Database.LayerTable.Add(layer);
            var dimension = new DxfRotatedDimension(
                0.0,
                new GeoPoint3(0, 0, 0),
                new GeoPoint3(100, 0, 0),
                new GeoPoint3(100, 20, 0),
                string.Empty,
                null
            )
            {
                DimensionStyleName = "Standard",
                Layer = "GONE",
            };

            CadMeasurement measured = Measured(document, dimension, out DisplayBox added);

            Assert.True(added.IsEmpty);
            Assert.True(measured.Box.IsEmpty);
        }

        [Fact]
        public void AMultileaderWithItsLanding_HasTheBoxMLeaderMakes_OnceAdded()
        {
            CadDocument document = BlankWithMLeaderStyle();

            CadMeasurement measured = CadMeasure.Leader(document, null, P(0, 0), P(30, 20), "B-12", null);
            Run(Session(document), "MLD", P(0, 0), P(30, 20), "B-12");

            DxfMLeader mleader = Assert.Single(All<DxfMLeader>(document.ModelSpace));
            DisplayBox added = CadSpatial.Bounds(document, new[] { mleader.Handle }).BoxOf(0);
            AssertSame(added, measured.Box);
            Assert.True(measured.Box.MaxX > 30.0 && measured.Box.MinX <= 0.0 + 1E-6);
            Assert.NotEmpty(measured.Frames);
        }

        [Fact]
        public void AMultileaderInADrawingOfNoStyle_IsMeasuredInTheStandardOneMLeaderMakes()
        {
            CadDocument document = Blank();
            Assert.Null(CadMLeaderStyleEditor.Find(document.Database, "Standard"));

            CadMeasurement measured = CadMeasure.Leader(document, null, P(0, 0), P(30, 20), "B-12", null);
            Assert.Null(CadMLeaderStyleEditor.Find(document.Database, "Standard"));
            Run(Session(document), "MLD", P(0, 0), P(30, 20), "B-12");

            DxfMLeader mleader = Assert.Single(All<DxfMLeader>(document.ModelSpace));
            AssertSame(CadSpatial.Bounds(document, new[] { mleader.Handle }).BoxOf(0), measured.Box);
            Assert.False(measured.Box.IsEmpty);
        }

        [Fact]
        public void AMultileaderOnTheLeft_AtAScale_HasTheBoxMLeaderMakes_OnceAdded()
        {
            CadDocument document = BlankWithMLeaderStyle();
            document.Database.MLeaderscale = 2.0;

            CadMeasurement measured = CadMeasure.Leader(
                document,
                "Standard",
                P(100, 100),
                P(60, 120),
                "PL10 x 200",
                null
            );
            Run(Session(document), "MLD", P(100, 100), P(60, 120), "PL10 x 200");

            DxfMLeader mleader = Assert.Single(All<DxfMLeader>(document.ModelSpace));
            AssertSame(CadSpatial.Bounds(document, new[] { mleader.Handle }).BoxOf(0), measured.Box);
            Assert.True(measured.Box.MinX < 60.0);
        }

        [Fact]
        public void AMultileaderOfAStyleTheDrawingLacks_OrOfNoWords_IsRefused()
        {
            CadDocument document = BlankWithMLeaderStyle();

            Assert.Throws<ArgumentException>(() => CadMeasure.Leader(document, "NOPE", P(0, 0), P(30, 20), "x", null));
            Assert.Throws<ArgumentException>(() => CadMeasure.Leader(document, null, P(0, 0), P(30, 20), "", null));
            Assert.Throws<ArgumentException>(() => CadMeasure.Leader(document, null, P(0, 0), P(0, 0), "x", null));
            Assert.Throws<ArgumentException>(() => CadMeasure.Leader(document, null, P(0, 0), P(30, 20), "x", 0.0));
        }

        [Fact]
        public void ALandingWithinTheTolerance_OfTheArrow_IsRefused_AsOneAtTheArrowIs()
        {
            CadDocument document = BlankWithMLeaderStyle();
            var touching = new Tolerance(1E-3, 1E-5);

            Assert.Throws<ArgumentException>(() => CadMeasure.Leader(document, null, P(0, 0), P(1E-12, 0), "x", null));
            Assert.Throws<ArgumentException>(() =>
                CadMeasure.Leader(document, null, P(5, 5), P(5, 5 + 1E-12), "x", null, touching)
            );
            Assert.Throws<ArgumentException>(() =>
                CadMeasure.Leader(document, null, P(0, 0), P(0.5, 0), "x", null, new Tolerance(1.0, 1E-5))
            );
            Assert.NotNull(CadMeasure.Leader(document, null, P(0, 0), P(0.5, 0), "x", null, touching));
        }

        [Fact]
        public void Measuring_ChangesNothing_NoHandleNoEditNoStaleList()
        {
            CadDocument document = _drawings.Blank();
            Put(document, Line(0, 0, 100, 0));
            Put(document, Text("SIDE", 5, 5));
            DisplayList2 before = document.GetDisplay(document.ModelSpace);
            string text = document.Database.ToText();
            bool modified = document.IsModified;
            CadUndoStep undo = document.NextUndo;

            CadMeasure.Of(document, Plain("SIDE FOOD", 12, 30, 5.0));
            CadMeasure.Of(
                document,
                new DxfMText
                {
                    Contents = "SIDE FOOD",
                    Location = new GeoPoint3(0, 0, 0),
                    TextHeight = 3.0,
                    Width = 30.0,
                    TextStyleName = "OC_SHX",
                }
            );
            CadMeasure.Of(
                document,
                new DxfRotatedDimension(
                    0.0,
                    new GeoPoint3(0, 0, 0),
                    new GeoPoint3(100, 0, 0),
                    new GeoPoint3(100, 20, 0),
                    string.Empty,
                    null
                )
                {
                    DimensionStyleName = "Standard",
                }
            );

            Assert.Same(before, document.GetDisplay(document.ModelSpace));
            Assert.Equal(text, document.Database.ToText());
            Assert.Equal(modified, document.IsModified);
            Assert.Same(undo, document.NextUndo);
        }

        [Fact]
        public void TheEntityGiven_IsNotChanged()
        {
            CadDocument document = _drawings.Blank();
            var dimension = new DxfRotatedDimension(
                0.0,
                new GeoPoint3(0, 0, 0),
                new GeoPoint3(100, 0, 0),
                new GeoPoint3(100, 20, 0),
                string.Empty,
                null
            )
            {
                DimensionStyleName = "Standard",
            };

            CadMeasure.Of(document, dimension);

            Assert.Null(dimension.DimBlock);
            Assert.Null(dimension.Handle);
        }

        [Fact]
        public void ATextOrAnMTextNamingAStyleTheDrawingLacks_IsRefused()
        {
            CadDocument document = _drawings.Blank();
            DxfDBText single = Plain("SIDE", 0, 0, 5.0);
            single.TextStyleName = "NOPE";
            var multi = new DxfMText
            {
                Contents = "SIDE",
                Location = new GeoPoint3(0, 0, 0),
                TextHeight = 5.0,
                TextStyleName = "NOPE",
            };

            Assert.Contains(
                "NOPE",
                Assert.Throws<ArgumentException>(() => CadMeasure.Of(document, single)).Message,
                StringComparison.Ordinal
            );
            Assert.Throws<ArgumentException>(() => CadMeasure.Of(document, multi));
        }

        [Fact]
        public void ArgumentsAreChecked()
        {
            CadDocument document = _drawings.Blank();

            Assert.Throws<ArgumentNullException>(() => CadMeasure.Of(null, Plain("A", 0, 0, 1.0)));
            Assert.Throws<ArgumentNullException>(() => CadMeasure.Of(document, null));
            Assert.Throws<ArgumentException>(() => new CadFrame(0, 0, 0.0, 1.0, 0.0));
            Assert.Throws<ArgumentException>(() => new CadFrame(0, 0, 1.0, double.NaN, 0.0));
        }

        [Fact]
        public void ATextOfNoWords_HasNoBoxAndNoFrame()
        {
            CadDocument document = _drawings.Blank();

            CadMeasurement measured = Measured(document, Plain("", 0, 0, 5.0), out DisplayBox added);

            Assert.True(added.IsEmpty);
            Assert.True(measured.Box.IsEmpty);
            Assert.Empty(measured.Frames);
        }

        [Fact]
        public void TheFramesOfAMeasure_AreTheFramesOverlapsLooksAt()
        {
            CadDocument document = _drawings.Blank();
            DxfDBText text = Put(document, Text("SIDE FOOD", 20, 20, 25.0, 5.0));
            Put(document, Line(40, 0, 40, 60));
            CadOverlapTable hits = CadSpatial.Overlaps(document, document.ModelSpace, null, new Tolerance(1E-3, 1E-5));
            Assert.Single(hits.Words);

            CadMeasurement measured = CadMeasure.Of(document, text);
            CadFrame frame = Assert.Single(measured.Frames);

            Assert.False(
                CadSpatial.Fits(
                    document,
                    document.ModelSpace,
                    frame,
                    new CadFreeSpaceOptions { Kinds = new[] { "LINE" } },
                    new Tolerance(1E-3, 1E-5)
                ),
                "the measured frame of the text lies across the line"
            );
        }
    }
}
