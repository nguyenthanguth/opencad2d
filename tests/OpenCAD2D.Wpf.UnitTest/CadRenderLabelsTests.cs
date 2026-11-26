using System;
using System.IO;
using System.Linq;
using GeometryHelper.Dxf;
using GeometryHelper.Geometry;
using Xunit;

namespace OpenCAD2D.Wpf.UnitTest
{
    /// <summary>
    /// The labels of a picture: the matrix the renderer draws by, the class and instance of each pixel from the renderer in id
    /// mode, and what both say about the drawings of the headless render's tests (<see cref="RenderDrawings"/>). They run here,
    /// where nothing runs in parallel, because <c>Prepare</c> sets the canvas's measure of text for the whole process.
    /// </summary>
    public class CadRenderLabelsTests : IDisposable
    {
        private readonly string _folder = Path.Combine(
            Path.GetTempPath(),
            "gk",
            "cad-labels-" + Guid.NewGuid().ToString("N")
        );

        public CadRenderLabelsTests() => Directory.CreateDirectory(_folder);

        public void Dispose()
        {
            try
            {
                Directory.Delete(_folder, true);
            }
            catch (IOException) { }
            catch (UnauthorizedAccessException) { }
        }

        private CadDocument Drawing(int number) => CadDocument.Open(RenderDrawings.Build(number, _folder));

        private static CadRenderOptions Wide(int number) =>
            new CadRenderOptions { Width = 800, Layout = RenderDrawings.Space(number) };

        /// <summary>Gets whether a pixel of a picture is not black.</summary>
        private static bool Lit(CadRenderImage picture, int x, int y)
        {
            int at = 3 * (y * picture.Width + x);
            return picture.Rgb[at] != 0 || picture.Rgb[at + 1] != 0 || picture.Rgb[at + 2] != 0;
        }

        /// <summary>Gets whether a pixel of a picture is at half its brightness or more in some channel.</summary>
        private static bool Bright(CadRenderImage picture, int x, int y)
        {
            int at = 3 * (y * picture.Width + x);
            return picture.Rgb[at] >= 128 || picture.Rgb[at + 1] >= 128 || picture.Rgb[at + 2] >= 128;
        }

        private static int Count(CadRenderLabels labels, int cls) => labels.Classes.Count(c => c == cls);

        [Fact]
        public void TheMatrix_IsTheViewTheRendererDrawsBy_ForTheExtentsOfARectangle()
        {
            var database = new DxfDatabase("AC1027");
            database.ModelSpace.AppendEntity(new DxfLine(new GeoPoint3(0, 0, 0), new GeoPoint3(400, 300, 0)));
            CadDocument document = CadDocument.From(database, "rectangle.dxf");

            double[] m = CadRender.Transform(document, null);

            Assert.Equal(9, m.Length);
            Assert.Equal(new double[] { 0, 0, 1 }, m.Skip(6).ToArray());
            // x = a * x + c * y + e, y = b * x + d * y + f
            double Px(double x, double y) => m[0] * x + m[1] * y + m[2];
            double Py(double x, double y) => m[3] * x + m[4] * y + m[5];
            Assert.Equal(3.6, m[0], 9);
            Assert.Equal(-3.6, m[4], 9);
            Assert.Equal(0.0, m[1], 9);
            Assert.Equal(0.0, m[3], 9);
            Assert.Equal(80.0, Px(0, 0), 6);
            Assert.Equal(1140.0, Py(0, 0), 6);
            Assert.Equal(1520.0, Px(400, 300), 6);
            Assert.Equal(60.0, Py(400, 300), 6);
        }

        [Fact]
        public void APointOfALine_MapsToAPixelTheLineColoured()
        {
            CadDocument document = Drawing(1);
            var options = new CadRenderOptions { Width = 1000 };
            double[] m = CadRender.Transform(document, options);
            CadRenderImage picture = CadRender.Draw(document, options);

            // Drawing 1 starts with the line from (0, 0) to (200, 100) in colour 7.
            for (int i = 0; i <= 10; i++)
            {
                double x = 20.0 * i;
                double y = 10.0 * i;
                int px = (int)Math.Floor(m[0] * x + m[1] * y + m[2]);
                int py = (int)Math.Floor(m[3] * x + m[4] * y + m[5]);
                bool coloured = false;

                for (int dy = -1; dy <= 1; dy++)
                {
                    for (int dx = -1; dx <= 1; dx++)
                    {
                        coloured |= Lit(picture, px + dx, py + dy);
                    }
                }

                Assert.True(coloured, "No pixel near (" + px + ", " + py + ") is lit for the point " + x + ", " + y);
            }
        }

        [Fact]
        public void TheMatrixOfTheLabels_IsTheMatrixOfTheTransform()
        {
            CadDocument document = Drawing(1);
            var options = new CadRenderOptions { Width = 640, Height = 400 };

            CadRenderLabels labels = CadRender.DrawLabels(document, options, null);

            Assert.Equal(CadRender.Transform(document, options), labels.Transform);
            Assert.Equal(640, labels.Width);
            Assert.Equal(400, labels.Height);
            Assert.Equal(640 * 400, labels.Classes.Length);
            Assert.Equal(640 * 400, labels.Instances.Length);
        }

        [Fact]
        public void TheLabelsOfALine_AreOnThePixelsOfItsPicture_AndInItsInstance()
        {
            CadDocument document = Drawing(1);
            var options = new CadRenderOptions { Width = 800 };

            CadRenderLabels labels = CadRender.DrawLabels(document, options, null);
            CadRenderImage picture = CadRender.Draw(document, options);

            int labelled = 0;

            for (int y = 0; y < labels.Height; y++)
            {
                for (int x = 0; x < labels.Width; x++)
                {
                    int at = y * labels.Width + x;

                    if (labels.Classes[at] != 0)
                    {
                        labelled++;
                        Assert.True(Lit(picture, x, y), "A pixel of a mask is dark in the picture: " + x + ", " + y);
                        Assert.InRange(labels.Instances[at], 1, labels.Handles.Length);
                        Assert.Equal(CadLabelClass.Lines, labels.Classes[at]);
                    }
                    else
                    {
                        Assert.Equal(0, labels.Instances[at]);
                    }
                }
            }

            Assert.True(labelled > 500, "Only " + labelled + " pixels are labelled.");
        }

        [Fact]
        public void TheWordsOfATrueTypeText_AreWhereThePictureShowsThem()
        {
            CadDocument document = Drawing(4);
            CadRenderOptions options = Wide(4);

            CadRenderLabels labels = CadRender.DrawLabels(document, options, null);
            CadRenderImage picture = CadRender.Draw(document, options);

            int labelled = 0,
                lit = 0,
                brightMissed = 0,
                bright = 0,
                outside = 0;

            for (int y = 0; y < labels.Height; y++)
            {
                for (int x = 0; x < labels.Width; x++)
                {
                    bool word = labels.Classes[y * labels.Width + x] == CadLabelClass.Words;
                    labelled += word ? 1 : 0;
                    lit += Lit(picture, x, y) ? 1 : 0;

                    if (word && !Near(picture, x, y, 2))
                    {
                        outside++;
                    }

                    if (Bright(picture, x, y))
                    {
                        bright++;
                        brightMissed += word ? 0 : 1;
                    }
                }
            }

            // Drawing 4 holds text only: every labelled pixel is words. The picture is antialiased, the labels are not, and
            // the font's monochrome letters may sit a pixel from the antialiased ones: a labelled pixel is lit or by one that is.
            Assert.Equal(labelled, labels.Classes.Count(c => c != 0));
            Assert.True(labelled > 2000, "Only " + labelled + " pixels of words.");
            Assert.True(labelled <= lit, labelled + " labelled, " + lit + " lit");
            Assert.Equal(0, outside);
            Assert.True(brightMissed <= bright / 8, brightMissed + " of " + bright + " bright pixels are not words.");
        }

        /// <summary>Gets whether a pixel or one within a distance of it is not black in the picture.</summary>
        private static bool Near(CadRenderImage picture, int x, int y, int distance)
        {
            for (int dy = -distance; dy <= distance; dy++)
            {
                for (int dx = -distance; dx <= distance; dx++)
                {
                    int xx = x + dx;
                    int yy = y + dy;

                    if (xx >= 0 && yy >= 0 && xx < picture.Width && yy < picture.Height && Lit(picture, xx, yy))
                    {
                        return true;
                    }
                }
            }

            return false;
        }

        [Fact]
        public void TheClassesOfDimensions_AreWordsAndDimensionsAndLines()
        {
            CadDocument document = Drawing(6);

            CadRenderLabels labels = CadRender.DrawLabels(document, Wide(6), null);

            Assert.True(Count(labels, CadLabelClass.Words) > 50, "words " + Count(labels, CadLabelClass.Words));
            Assert.True(Count(labels, CadLabelClass.Dimensions) > 100);
            Assert.True(Count(labels, CadLabelClass.Lines) > 100);
            Assert.Equal(0, Count(labels, CadLabelClass.Hatches));
            Assert.Equal(0, Count(labels, CadLabelClass.Blocks));

            // Every pixel of a dimension or of its words belongs to an entity the dimension kind says it is.
            for (int i = 0; i < labels.Classes.Length; i++)
            {
                if (labels.Classes[i] == CadLabelClass.Dimensions)
                {
                    Assert.Equal(CadLabelClass.Dimensions, labels.Categories[labels.Instances[i] - 1]);
                }
            }
        }

        [Fact]
        public void TheHatches_AreClassFour_TheDenseOneToo()
        {
            CadDocument document = Drawing(3);
            CadRenderOptions options = Wide(3);

            CadRenderLabels labels = CadRender.DrawLabels(document, options, null);
            double[] m = labels.Transform;

            // The solid hatch fills the square from (0, 0) to (100, 100) of the drawing: its pixels are the area at the scale of the picture.
            double scale = Math.Abs(m[0]);
            int solid = 0;
            int dense = 0;

            for (int y = 0; y < labels.Height; y++)
            {
                for (int x = 0; x < labels.Width; x++)
                {
                    int at = y * labels.Width + x;
                    double wx = (x + 0.5 - m[2]) / m[0];
                    double wy = (y + 0.5 - m[5]) / m[4];

                    if (labels.Classes[at] == CadLabelClass.Hatches && wx > 5 && wx < 95 && wy > 5 && wy < 95)
                    {
                        solid++;
                    }

                    if (labels.Classes[at] == CadLabelClass.Hatches && wx > 305 && wx < 395 && wy > 5 && wy < 95)
                    {
                        dense++;
                    }
                }
            }

            double expected = 90.0 * 90.0 * scale * scale;
            Assert.InRange(solid, (int)(expected * 0.99), (int)(expected * 1.01));
            Assert.True(dense > expected * 0.5, "The dense hatch labels only " + dense + " of " + expected);
        }

        [Fact]
        public void ABlock_IsClassFive_OnlyWhenBlocksAreAsked_ElseItsPiecesAreClassedByWhatTheyAre()
        {
            CadDocument document = Drawing(7);
            CadRenderOptions options = Wide(7);

            CadRenderLabels piecewise = CadRender.DrawLabels(document, options, null);
            CadRenderLabels whole = CadRender.DrawLabels(document, options, new[] { 1, 2, 3, 4, 5 });

            Assert.Equal(0, Count(piecewise, CadLabelClass.Blocks));
            int blocks = Count(whole, CadLabelClass.Blocks);
            Assert.True(blocks > 1000, "blocks " + blocks);

            // A pixel of a block is a pixel of the drawing that was lines or words, and of an INSERT's entity.
            for (int i = 0; i < whole.Classes.Length; i++)
            {
                if (whole.Classes[i] == CadLabelClass.Blocks)
                {
                    Assert.Equal(CadLabelClass.Blocks, whole.Categories[whole.Instances[i] - 1]);
                    Assert.NotEqual(0, piecewise.Classes[i]);
                }
            }
        }

        [Fact]
        public void TheClassesAsked_AreAllThatIsLabelled()
        {
            CadDocument document = Drawing(6);
            CadRenderOptions options = Wide(6);

            CadRenderLabels words = CadRender.DrawLabels(document, options, new[] { CadLabelClass.Words });
            CadRenderLabels all = CadRender.DrawLabels(document, options, null);

            Assert.True(Count(words, CadLabelClass.Words) > 0);
            Assert.Equal(words.Classes.Length, words.Classes.Count(c => c == 0 || c == CadLabelClass.Words));

            // A class left out is not drawn over what it covered: only the other classes' pixels go, so the words are the same where nothing covers them.
            for (int i = 0; i < words.Classes.Length; i++)
            {
                if (words.Classes[i] == CadLabelClass.Words)
                {
                    Assert.Equal(CadLabelClass.Words, all.Classes[i]);
                    Assert.Equal(all.Instances[i], words.Instances[i]);
                }
                else
                {
                    Assert.Equal(0, words.Instances[i]);
                }
            }
        }

        [Theory]
        [InlineData(0)]
        [InlineData(6)]
        [InlineData(-1)]
        public void AClassOutsideOneToFive_IsRefused(int id)
        {
            CadDocument document = Drawing(1);

            ArgumentException error = Assert.Throws<ArgumentException>(() =>
                CadRender.DrawLabels(document, null, new[] { id })
            );

            Assert.Contains("1 to 5", error.Message);
        }

        [Fact]
        public void TheBoxOfAnEntity_HoldsEveryPixelOfItsInstance_AndItsArea()
        {
            CadDocument document = Drawing(10);
            CadRenderOptions options = Wide(10);

            CadRenderLabels labels = CadRender.DrawLabels(document, options, null);

            var areas = new int[labels.Handles.Length];

            for (int y = 0; y < labels.Height; y++)
            {
                for (int x = 0; x < labels.Width; x++)
                {
                    int k = labels.Instances[y * labels.Width + x] - 1;

                    if (k >= 0)
                    {
                        areas[k]++;
                        Assert.True(labels.Boxes[4 * k] <= x && x + 1 <= labels.Boxes[4 * k + 2], "x of instance " + k);
                        Assert.True(
                            labels.Boxes[4 * k + 1] <= y && y + 1 <= labels.Boxes[4 * k + 3],
                            "y of instance " + k
                        );
                    }
                }
            }

            Assert.Equal(areas, labels.Areas);
            Assert.Contains(areas, a => a > 0);
        }

        [Fact]
        public void ALayout_LabelsTheModelEntitiesItsViewportsShow_AndNotTheFrameOrThePaper()
        {
            CadDocument document = Drawing(10);

            CadRenderLabels labels = CadRender.DrawLabels(document, Wide(10), null);

            // The layout draws two viewports of the walls and two circles, in model space's entities: lines, all of them.
            Assert.True(Count(labels, CadLabelClass.Lines) > 1000, "lines " + Count(labels, CadLabelClass.Lines));
            Assert.Equal(0, Count(labels, CadLabelClass.Words));

            // The paper is white in the picture and nothing in the labels: the corner of the sheet is the ground.
            Assert.Equal(0, labels.Classes[0]);
            Assert.Equal(0, labels.Instances[0]);
            Assert.True(labels.Handles.Length >= 7, "handles " + labels.Handles.Length);
        }

        [Fact]
        public void ATextOverAHatch_IsWordsWhereTheLettersAre_AndHatchesElseWhere()
        {
            CadDocument document = CadDocument.From(new DxfDatabase("AC1027"), "over.dxf");
            RenderDrawings.Run(
                new CadCommandSession(document),
                "CECOLOR;7;-STYLE;AR;arial;0;1;0;N;N;N;RECTANG;0,0;100,50;CECOLOR;1;-HATCH;P;SOLID;50,25;;CECOLOR;3;TEXT;S;AR;10,10;30;0;ABC;;"
            );
            var options = new CadRenderOptions { Width = 800, Height = 400 };

            CadRenderLabels labels = CadRender.DrawLabels(document, options, null);

            int words = Count(labels, CadLabelClass.Words);
            int hatches = Count(labels, CadLabelClass.Hatches);
            Assert.True(words > 500, "words " + words);
            Assert.True(hatches > words, "hatches " + hatches);

            // The letters are drawn after the hatch, so they are on top: no hatch pixel is among the letters' own pixels,
            // which the text alone gives.
            CadRenderLabels alone = CadRender.DrawLabels(document, options, new[] { CadLabelClass.Words });
            Assert.Equal(words, Count(alone, CadLabelClass.Words));
        }

        [Fact]
        public void AWipeout_BlanksWhatIsUnderIt_InTheLabels_AsInThePicture()
        {
            CadDocument document = CadDocument.From(new DxfDatabase("AC1027"), "wipe.dxf");
            RenderDrawings.Run(
                new CadCommandSession(document),
                "CECOLOR;7;LINE;0,0;100,100;;LINE;0,100;100,0;;WIPEOUT;30,30;70,30;70,70;30,70;;"
            );
            var options = new CadRenderOptions { Width = 800, Height = 800 };

            CadRenderLabels labels = CadRender.DrawLabels(document, options, null);
            double[] m = labels.Transform;

            // Inside the square the two lines cross at (50, 50) and are blanked; outside it they are drawn.
            int inside = 0;
            int outside = 0;

            for (int y = 0; y < labels.Height; y++)
            {
                for (int x = 0; x < labels.Width; x++)
                {
                    double wx = (x + 0.5 - m[2]) / m[0];
                    double wy = (y + 0.5 - m[5]) / m[4];
                    bool drawn = labels.Classes[y * labels.Width + x] != 0;

                    if (wx > 31 && wx < 69 && wy > 31 && wy < 69)
                    {
                        inside += drawn ? 1 : 0;
                    }
                    else if (wx < 28 || wx > 72 || wy < 28 || wy > 72)
                    {
                        outside += drawn ? 1 : 0;
                    }
                }
            }

            Assert.Equal(0, inside);
            Assert.True(outside > 400, "outside " + outside);
        }

        [Fact]
        public void TwoLabellings_OfOneDrawing_AreTheSame()
        {
            CadDocument document = Drawing(5);
            CadRenderOptions options = Wide(5);

            CadRenderLabels first = CadRender.DrawLabels(document, options, null);
            CadRenderLabels second = CadRender.DrawLabels(document, options, null);

            Assert.Equal(first.Classes, second.Classes);
            Assert.Equal(first.Instances, second.Instances);
            Assert.Equal(first.Handles, second.Handles);
            Assert.Equal(first.Boxes, second.Boxes);
            Assert.Equal(first.FrameCorners, second.FrameCorners);
        }

        [Fact]
        public void TheFramesOfTheWords_AreTheTurnedRectanglesOfTheirTexts()
        {
            var database = new DxfDatabase("AC1027");
            database.TextStyleTable["Standard"].FileName = "arial.ttf";
            database.ModelSpace.AppendEntity(
                new DxfDBText
                {
                    Position = new GeoPoint3(0, 0, 0),
                    Height = 20,
                    TextString = "Turned",
                    Rotation = Math.PI / 6.0,
                }
            );
            CadDocument document = CadDocument.From(database, "turned.dxf");

            CadRenderLabels labels = CadRender.DrawLabels(document, new CadRenderOptions { Width = 800 }, null);

            Assert.Single(labels.FrameInstances);
            Assert.Equal(1, labels.FrameInstances[0]);
            Assert.Equal(8, labels.FrameCorners.Length);
            double[] c = labels.FrameCorners;

            // The first side runs along the baseline at 30 degrees (y down in pixels, so the angle is turned over); the second is square to it.
            double ax = c[2] - c[0];
            double ay = c[3] - c[1];
            double bx = c[6] - c[0];
            double by = c[7] - c[1];
            Assert.Equal(0.0, ax * bx + ay * by, 6);
            Assert.Equal(-Math.PI / 6.0, Math.Atan2(ay, ax), 6);

            // The corners are in the drawn pixels' box: the text's pixels are inside the frame's box.
            Assert.True(labels.Areas[0] > 100);
        }
    }
}
