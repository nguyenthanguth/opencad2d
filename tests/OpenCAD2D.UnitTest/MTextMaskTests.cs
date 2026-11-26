using System;
using System.Globalization;
using System.Linq;
using GeometryHelper.Dxf;
using GeometryHelper.Geometry;
using Xunit;

namespace OpenCAD2D.UnitTest
{
    /// <summary>
    /// The mask and the frame of an MTEXT as AutoCAD 2021 draws them, measured off its PDF plot (the fills and strokes of the
    /// page, each within 0.25 of a drawing unit, the plot's own step): the box the words are set in grown by the border offset
    /// less one times the height of the letters; the box as high as the defined height or the lines, whichever is more, and
    /// as wide as the width given or the widest line; in columns one box for each, as wide as the column and as high as it is
    /// set or its lines are, each hung at the attachment by its own height. Every case is 10 high letters, "AB" over "CD" in
    /// the built-in font (27.93 high), the mask grown by 5 unless a case says another border offset.
    /// </summary>
    public sealed class MTextMaskTests
    {
        private const double Step = 0.25;

        private static CadDocument Plain(
            DxfAttachmentPoint attachment,
            double width,
            double height,
            double scale,
            bool frame
        )
        {
            var db = new DxfDatabase("AC1032");
            db.TextStyleTable["Standard"].FileName = "opencad2d.shx";
            var mtext = new DxfMText
            {
                Contents = "AB\\PCD",
                TextHeight = 10.0,
                Width = width,
                Height = height,
                Attachment = attachment,
                Location = new GeoPoint3(0, 0, 0),
            };
            db.ModelSpace.AppendEntity(mtext);
            Mask(mtext, scale, frame);
            CadDocument document = CadDocument.From(db);
            document.Fonts = new CadFonts(null, CadFonts.BuiltInName, null);
            return document;
        }

        private static void Mask(DxfMText mtext, double scale, bool frame)
        {
            if (frame)
            {
                mtext.ShowBorders = true;
                return;
            }

            mtext.BackgroundFill = true;
            mtext.UseBackgroundColor = false;
            mtext.BackgroundScaleFactor = scale;
            mtext.BackgroundFillColor = DxfColor.FromRgb(255, 0, 0);
        }

        private static double[][] Rings(CadDocument document)
        {
            DisplayText2 shown = document.GetDisplay(document.ModelSpace).Items.OfType<DisplayText2>().Single();
            return shown
                .Parts.Select(p => p is DisplayFill2 fill ? fill.Rings.Single() : (p as DisplayPolyline2)?.Xy.ToArray())
                .Where(r => r != null)
                .ToArray();
        }

        private static void Boxes(string expected, double[][] rings)
        {
            string[] boxes = expected.Split(';');
            Assert.Equal(boxes.Length, rings.Length);

            for (int i = 0; i < boxes.Length; i++)
            {
                double[] want = boxes[i]
                    .Split(' ')
                    .Select(v => double.Parse(v, CultureInfo.InvariantCulture))
                    .ToArray();
                double[] xs = rings[i].Where((v, k) => k % 2 == 0).ToArray();
                double[] ys = rings[i].Where((v, k) => k % 2 == 1).ToArray();
                double[] got = { xs.Min(), xs.Max(), ys.Min(), ys.Max() };

                for (int k = 0; k < 4; k++)
                {
                    Assert.True(
                        Math.Abs(want[k] - got[k]) <= Step,
                        "box "
                            + i
                            + " is "
                            + string.Join(" ", got.Select(v => v.ToString("0.###", CultureInfo.InvariantCulture)))
                            + ", AutoCAD's "
                            + boxes[i]
                    );
                }
            }
        }

        [Theory]
        [InlineData(DxfAttachmentPoint.TopLeft, 40.0, 0.0, 1.5, "-5 45 -32.93 5")]
        [InlineData(DxfAttachmentPoint.TopLeft, 40.0, 60.0, 1.5, "-5 45 -65 5")]
        [InlineData(DxfAttachmentPoint.TopLeft, 40.0, 15.0, 1.5, "-5 45 -32.93 5")]
        [InlineData(DxfAttachmentPoint.TopLeft, 40.0, 60.0, 2.0, "-10 50 -70 10")]
        [InlineData(DxfAttachmentPoint.TopLeft, 40.0, 60.0, 1.0, "0 40 -60 0")]
        [InlineData(DxfAttachmentPoint.TopRight, 40.0, 60.0, 1.5, "-45 5 -65 5")]
        [InlineData(DxfAttachmentPoint.MiddleCenter, 40.0, 60.0, 1.5, "-25 25 -35 35")]
        [InlineData(DxfAttachmentPoint.MiddleCenter, 40.0, 15.0, 1.5, "-25 25 -18.96 18.96")]
        [InlineData(DxfAttachmentPoint.MiddleRight, 0.0, 60.0, 1.5, "-18 5 -35 35")]
        [InlineData(DxfAttachmentPoint.BottomRight, 40.0, 60.0, 1.5, "-45 5 -5 65")]
        [InlineData(DxfAttachmentPoint.BottomRight, 40.0, 15.0, 1.5, "-45 5 -5 32.93")]
        [InlineData(DxfAttachmentPoint.BottomCenter, 40.0, 0.0, 1.5, "-25 25 -5 32.93")]
        public void AMask_IsAsHighAsTheDefinedHeightOrTheLines_WhicheverIsMore_AndGrownByTheBorderOffset(
            DxfAttachmentPoint attachment,
            double width,
            double height,
            double scale,
            string expected
        )
        {
            Boxes(expected, Rings(Plain(attachment, width, height, scale, false)));
        }

        [Theory]
        [InlineData(DxfAttachmentPoint.TopLeft, 40.0, 60.0, "-5 45 -65 5")]
        [InlineData(DxfAttachmentPoint.TopLeft, 40.0, 15.0, "-5 45 -32.93 5")]
        [InlineData(DxfAttachmentPoint.MiddleCenter, 40.0, 60.0, "-25 25 -35 35")]
        [InlineData(DxfAttachmentPoint.BottomLeft, 40.0, 60.0, "-5 45 -5 65")]
        public void AFrame_IsTheBoxOfTheMask(
            DxfAttachmentPoint attachment,
            double width,
            double height,
            string expected
        )
        {
            Boxes(expected, Rings(Plain(attachment, width, height, 1.5, true)));
        }

        [Theory]
        [InlineData("static2", DxfAttachmentPoint.TopLeft, "-5 45 -105 5;43 93 -132.51 5")]
        [InlineData("static2", DxfAttachmentPoint.MiddleCenter, "-49 1 -55 55;-1 49 -68.76 68.76")]
        [InlineData("static2", DxfAttachmentPoint.BottomRight, "-93 -43 -5 105;-45 5 -5 132.51")]
        [InlineData("dynauto", DxfAttachmentPoint.TopLeft, "-5 45 -85 5;43 93 -85 5;91 141 -85 5")]
        [InlineData("dynauto", DxfAttachmentPoint.MiddleCenter, "-73 -23 -45 45;-25 25 -45 45;23 73 -45 45")]
        [InlineData("manual3", DxfAttachmentPoint.TopLeft, "-5 45 -85 5;43 93 -70 5;91 141 -95 5")]
        [InlineData("manual3", DxfAttachmentPoint.MiddleCenter, "-73 -23 -45 45;-25 25 -37.5 37.5;23 73 -50 50")]
        [InlineData("manual3", DxfAttachmentPoint.BottomRight, "-141 -91 -5 85;-93 -43 -5 70;-45 5 -5 95")]
        [InlineData("manual2", DxfAttachmentPoint.TopLeft, "-5 45 -55 5;43 93 -95 5;91 141 -99.18 5")]
        [InlineData("manual2", DxfAttachmentPoint.MiddleCenter, "-73 -23 -30 30;-25 25 -50 50;23 73 -52.09 52.09")]
        [InlineData("manual2", DxfAttachmentPoint.BottomRight, "-141 -91 -5 55;-93 -43 -5 95;-45 5 -5 99.18")]
        public void AMaskInColumns_IsABoxForEachColumn_AsHighAsItIsSetOrItsLinesAre_HungByItsOwnHeight(
            string kind,
            DxfAttachmentPoint attachment,
            string expected
        )
        {
            CadDocument document = null;
            DxfMText mtext = MTextManualColumnTests.Made(kind, attachment, out document);
            Mask(mtext, 1.5, false);

            Boxes(expected, Rings(document));
        }
    }
}
