using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using GeometryHelper.Dxf;
using Xunit;
using static OpenCAD2D.UnitTest.EditingDrawings;

namespace OpenCAD2D.UnitTest
{
    /// <summary>
    /// The grips of an MTEXT in columns of heights of their own, and what dragging each does, as AutoCAD 2021's
    /// GetGripPoints and MoveGripPointsAt gave them (accoreconsole, a plug-in of the probe's own: letters 10 high, columns 40
    /// wide, a gutter of 8, the heights 80, 65 and 90; 50 and 90 with the lines of a third column; 40, 25 and 60 with a fourth).
    /// The column past those set has a height grip of its own, at the height of its lines, and dragging it sets that column.
    /// </summary>
    public sealed class MTextManualColumnGripTests
    {
        private const double Close = 1E-4;

        private static void At(double x, double y, CadGrip grip)
        {
            Assert.Equal(x, grip.Point.X, 4);
            Assert.Equal(y, grip.Point.Y, 4);
        }

        [Fact]
        public void ThreeColumnsOfTheirOwnHeights_HaveAHeightGripUnderTheMiddleOfEach_AtTheTopLeft()
        {
            DxfMText mtext = MTextManualColumnTests.Made("manual3", DxfAttachmentPoint.TopLeft, out _);

            IReadOnlyList<CadGrip> grips = CadGrips.Of(mtext);

            Assert.Equal(6, grips.Count);
            At(0.0, 0.0, grips[0]);
            At(40.0, 0.0, grips[1]);
            At(20.0, -80.0, grips[2]);
            At(68.0, -65.0, grips[3]);
            At(116.0, -90.0, grips[4]);
            At(136.0, 0.0, grips[5]);
            Assert.Equal(CadGripKind.MTextGutter, grips[5].Kind);
        }

        [Fact]
        public void ThreeColumnsOfTheirOwnHeights_HaveTheirGripsOnTheOtherSide_AtTheMiddleAndTheBottomRight()
        {
            DxfMText middle = MTextManualColumnTests.Made("manual3", DxfAttachmentPoint.MiddleCenter, out _);
            IReadOnlyList<CadGrip> grips = CadGrips.Of(middle);
            At(-28.0, 0.0, grips[1]);
            At(-48.0, -40.0, grips[2]);
            At(0.0, -32.5, grips[3]);
            At(48.0, -45.0, grips[4]);
            At(68.0, 0.0, grips[5]);

            DxfMText corner = MTextManualColumnTests.Made("manual3", DxfAttachmentPoint.BottomRight, out _);
            grips = CadGrips.Of(corner);
            At(-40.0, 0.0, grips[1]);
            At(-116.0, 80.0, grips[2]);
            At(-68.0, 65.0, grips[3]);
            At(-20.0, 90.0, grips[4]);
            At(-136.0, 0.0, grips[5]);
        }

        [Fact]
        public void TheColumnPastThoseSet_HasAHeightGripAtTheHeightOfItsLines()
        {
            DxfMText mtext = MTextManualColumnTests.Made("manual2", DxfAttachmentPoint.TopLeft, out _);

            IReadOnlyList<CadGrip> grips = CadGrips.Of(mtext);

            Assert.Equal(6, grips.Count);
            At(20.0, -50.0, grips[2]);
            At(68.0, -90.0, grips[3]);
            At(116.0, -94.1806, grips[4]);
            At(136.0, 0.0, grips[5]);

            // Four columns, the last holding the eight lines that fall past the three set: its grip stands at their height.
            DxfMText four = MTextManualColumnTests.Made("manual3small", DxfAttachmentPoint.TopLeft, out _);
            grips = CadGrips.Of(four);
            Assert.Equal(7, grips.Count);
            At(116.0, -60.0, grips[4]);
            At(164.0, -127.5139, grips[5]);
            At(184.0, 0.0, grips[6]);
        }

        // kind, attachment, grip, step x, step y, column width, gutter, heights of the first columns.
        [Theory]
        [InlineData("manual3", DxfAttachmentPoint.TopLeft, 1, 5.0, -3.0, 45.0, 8.0, "80;65;90")]
        [InlineData("manual3", DxfAttachmentPoint.TopLeft, 1, -30.0, 2.0, 10.0, 8.0, "80;65;90")]
        [InlineData("manual3", DxfAttachmentPoint.TopLeft, 1, -500.0, 0.0, 10.0, 8.0, "80;65;90")]
        [InlineData("manual3", DxfAttachmentPoint.TopLeft, 2, 5.0, -3.0, 40.0, 8.0, "83;65;90")]
        [InlineData("manual3", DxfAttachmentPoint.TopLeft, 2, 0.0, 10.0, 40.0, 8.0, "70;65;90")]
        [InlineData("manual3", DxfAttachmentPoint.TopLeft, 3, 0.0, -4.0, 40.0, 8.0, "80;69;90")]
        [InlineData("manual3", DxfAttachmentPoint.TopLeft, 4, 0.0, 5.0, 40.0, 8.0, "80;65;85")]
        [InlineData("manual3", DxfAttachmentPoint.TopLeft, 5, 5.0, -3.0, 40.0, 10.5, "80;65;90")]
        [InlineData("manual3", DxfAttachmentPoint.TopLeft, 5, -100.0, 0.0, 40.0, 0.0, "80;65;90")]
        [InlineData("manual3", DxfAttachmentPoint.TopLeft, 5, 10.0, 0.0, 40.0, 13.0, "80;65;90")]
        [InlineData("manual3", DxfAttachmentPoint.MiddleCenter, 1, 5.0, 0.0, 45.0, 8.0, "80;65;90")]
        [InlineData("manual3", DxfAttachmentPoint.MiddleCenter, 2, 0.0, -4.0, 40.0, 8.0, "84;65;90")]
        [InlineData("manual3", DxfAttachmentPoint.MiddleCenter, 5, 5.0, 0.0, 40.0, 10.5, "80;65;90")]
        [InlineData("manual3", DxfAttachmentPoint.BottomRight, 1, 5.0, 0.0, 35.0, 8.0, "80;65;90")]
        [InlineData("manual3", DxfAttachmentPoint.BottomRight, 2, 0.0, -4.0, 40.0, 8.0, "76;65;90")]
        [InlineData("manual3", DxfAttachmentPoint.BottomRight, 2, 0.0, 6.0, 40.0, 8.0, "86;65;90")]
        [InlineData("manual3", DxfAttachmentPoint.BottomRight, 5, 5.0, 0.0, 40.0, 5.5, "80;65;90")]
        [InlineData("manual2", DxfAttachmentPoint.TopLeft, 2, 0.0, -4.0, 40.0, 8.0, "54;90")]
        [InlineData("manual2", DxfAttachmentPoint.TopLeft, 3, 0.0, 5.0, 40.0, 8.0, "50;85")]
        [InlineData("manual2", DxfAttachmentPoint.TopLeft, 4, 0.0, -10.0, 40.0, 8.0, "50;90;104.1806")]
        [InlineData("manual2", DxfAttachmentPoint.TopLeft, 4, 0.0, 10.0, 40.0, 8.0, "50;90;84.1806")]
        [InlineData("manual2", DxfAttachmentPoint.TopLeft, 4, 0.0, -500.0, 40.0, 8.0, "50;90;594.1806")]
        [InlineData("manual2", DxfAttachmentPoint.TopLeft, 5, 10.0, 0.0, 40.0, 13.0, "50;90")]
        [InlineData("manual2", DxfAttachmentPoint.TopLeft, 5, -30.0, 0.0, 40.0, 0.0, "50;90")]
        [InlineData("manual3small", DxfAttachmentPoint.TopLeft, 5, 0.0, -5.0, 40.0, 8.0, "40;25;60;132.5139")]
        [InlineData("manual3small", DxfAttachmentPoint.TopLeft, 5, 0.0, 5.0, 40.0, 8.0, "40;25;60;122.5139")]
        [InlineData("manual3small", DxfAttachmentPoint.TopLeft, 4, 0.0, 3.0, 40.0, 8.0, "40;25;57")]
        [InlineData("manual3small", DxfAttachmentPoint.TopLeft, 6, 10.0, 0.0, 40.0, 11.3333, "40;25;60")]
        public void AGripOfColumnsOfTheirOwnHeights_SetsWhatAutoCADsDoes(
            string kind,
            DxfAttachmentPoint attachment,
            int grip,
            double dx,
            double dy,
            double columnWidth,
            double gutter,
            string heights
        )
        {
            DxfMText mtext = MTextManualColumnTests.Made(kind, attachment, out CadDocument document);
            CadGrip dragged = CadGrips.Of(document.FindEntity(mtext.Handle))[grip];

            CadEditResult result = CadGrips.Drag(
                document,
                mtext.Handle,
                grip,
                P(dragged.Point.X + dx, dragged.Point.Y + dy)
            );

            Assert.True(result.Succeeded, result.Message);
            var after = (DxfMText)document.FindEntity(mtext.Handle);
            Assert.Equal(DxfColumnType.DynamicColumns, after.ColumnType);
            Assert.Equal(columnWidth, after.ColumnWidth, 4);
            Assert.Equal(gutter, after.ColumnGutterWidth, 3);
            double[] expected = heights.Split(';').Select(h => double.Parse(h, CultureInfo.InvariantCulture)).ToArray();

            for (int i = 0; i < expected.Length; i++)
            {
                Assert.True(
                    Math.Abs(expected[i] - after.GetColumnHeight(i)) < Close,
                    "column " + i + " is " + after.GetColumnHeight(i) + " high, AutoCAD's " + expected[i]
                );
            }

            Assert.True(document.Undo());
        }
    }
}
