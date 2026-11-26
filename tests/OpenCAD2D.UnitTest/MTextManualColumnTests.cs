using System;
using System.Globalization;
using System.Linq;
using GeometryHelper.Dxf;
using GeometryHelper.Geometry;
using Xunit;

namespace OpenCAD2D.UnitTest
{
    /// <summary>
    /// An MTEXT in columns as AutoCAD 2021 sets it (accoreconsole: the MTEXT exploded, then each column exploded again, the
    /// built-in font, opencad2d.shx, letters 10 high, columns 40 wide and a gutter of 8): the lines of columns of heights of
    /// their own hung as the attachment says, each column by its own lines, and what no column of those set holds, in one
    /// column more that has no bound. The lines are "AB 1" down, 14 of them, 30 in the case of one column of a height.
    /// </summary>
    public sealed class MTextManualColumnTests
    {
        private const double Close = 1E-4;

        internal static DxfMText Made(string kind, DxfAttachmentPoint attachment, out CadDocument document)
        {
            int lines =
                kind.StartsWith("man", StringComparison.Ordinal) && !kind.StartsWith("manual", StringComparison.Ordinal)
                    ? 30
                    : 14;
            var db = new DxfDatabase("AC1032");
            db.TextStyleTable["Standard"].FileName = "opencad2d.shx";
            var mtext = new DxfMText
            {
                Contents = string.Join("\\P", Enumerable.Range(1, lines).Select(i => "AB " + i)),
                TextHeight = 10.0,
                Width = 40.0,
                Attachment = attachment,
                Location = new GeoPoint3(0, 0, 0),
            };
            db.ModelSpace.AppendEntity(mtext);

            switch (kind)
            {
                case "static2":
                    mtext.Height = 100.0;
                    mtext.SetStaticColumns(40.0, 8.0, 2);
                    break;
                case "static3":
                    mtext.Height = 70.0;
                    mtext.SetStaticColumns(36.0, 5.0, 3);
                    break;
                case "dynauto":
                    mtext.Height = 80.0;
                    mtext.SetDynamicColumns(40.0, 8.0, true);
                    break;
                default:
                    Heights(mtext, kind);
                    break;
            }

            document = CadDocument.From(db);
            document.Fonts = new CadFonts(null, CadFonts.BuiltInName, null);
            return (DxfMText)document.FindEntity(mtext.Handle);
        }

        private static void Heights(DxfMText mtext, string kind)
        {
            double[] heights =
                kind == "manual3" ? new[] { 80.0, 65.0, 90.0 }
                : kind == "manual2" ? new[] { 50.0, 90.0 }
                : kind == "manual3small" ? new[] { 40.0, 25.0, 60.0 }
                : kind == "man30" ? new[] { 30.0 }
                : kind == "man30x2" ? new[] { 30.0, 30.0 }
                : kind == "man30x50" ? new[] { 30.0, 50.0 }
                : kind == "man50" ? new[] { 50.0 }
                : new[] { 20.0, 20.0, 20.0 };
            mtext.Height = heights[0];
            mtext.SetDynamicColumns(40.0, 8.0, false);
            mtext.ColumnCount = heights.Length;

            for (int i = 0; i < heights.Length; i++)
            {
                mtext.SetColumnHeight(i, heights[i]);
            }
        }

        [Theory]
        [InlineData(
            "manual3",
            DxfAttachmentPoint.MiddleCenter,
            "-60.5000,29.0719;-61.2644,11.6990;-61.3744,-5.1087;-61.0000,-21.7753;-61.2717,-38.4420;-13.0000,20.0000;-13.0000,3.3333;-13.0000,-13.3333;-13.2717,-30.0000;31.5000,28.7570;32.0000,12.0903;31.2356,-5.2827;31.1256,-22.0903;31.5000,-38.7570"
        )]
        [InlineData(
            "manual3",
            DxfAttachmentPoint.BottomRight,
            "-120.0000,68.1438;-121.5287,50.7709;-121.7488,33.9633;-121.0000,17.2966;-121.5433,0.6299;-73.0000,50.6299;-73.0000,33.9633;-73.0000,17.2966;-73.5433,0.6299;-32.0000,67.5139;-31.0000,50.8472;-32.5287,33.4743;-32.7488,16.6667;-32.0000,0.0000"
        )]
        [InlineData(
            "manual3",
            DxfAttachmentPoint.BottomLeft,
            "-1.0000,68.1438;-1.0000,50.7709;-1.0000,33.9633;-1.0000,17.2966;-1.0000,0.6299;47.0000,50.6299;47.0000,33.9633;47.0000,17.2966;47.0000,0.6299;95.0000,67.5139;95.0000,50.8472;95.0000,33.4743;95.0000,16.6667;95.0000,0.0000"
        )]
        [InlineData(
            "manual3",
            DxfAttachmentPoint.MiddleLeft,
            "-1.0000,29.0719;-1.0000,11.6990;-1.0000,-5.1087;-1.0000,-21.7753;-1.0000,-38.4420;47.0000,20.0000;47.0000,3.3333;47.0000,-13.3333;47.0000,-30.0000;95.0000,28.7570;95.0000,12.0903;95.0000,-5.2827;95.0000,-22.0903;95.0000,-38.7570"
        )]
        [InlineData(
            "manual2",
            DxfAttachmentPoint.TopLeft,
            "-1.0000,-10.0000;-1.0000,-27.3730;-1.0000,-44.1806;47.0000,-10.0000;47.0000,-26.6667;47.0000,-43.9633;47.0000,-60.6299;47.0000,-77.2966;95.0000,-10.0000;95.0000,-26.6667;95.0000,-43.3333;95.0000,-60.7063;95.0000,-77.5139;95.0000,-94.1806"
        )]
        [InlineData(
            "manual2",
            DxfAttachmentPoint.MiddleCenter,
            "-60.5000,12.1608;-61.2644,-5.2122;-61.3744,-22.0198;-13.0000,28.6483;-13.2717,11.9816;-13.0000,-5.3150;-13.0000,-21.9816;-13.0000,-38.6483;34.7283,37.0903;31.5000,20.4236;32.0000,3.7570;31.2356,-13.6160;31.1256,-30.4236;31.5000,-47.0903"
        )]
        [InlineData(
            "manual2",
            DxfAttachmentPoint.BottomRight,
            "-120.0000,34.3215;-121.5287,16.9486;-121.7488,0.1409;-73.0000,67.2966;-73.5433,50.6299;-73.0000,33.3333;-73.0000,16.6667;-73.0000,0.0000;-25.5433,84.1806;-32.0000,67.5139;-31.0000,50.8472;-32.5287,33.4743;-32.7488,16.6667;-32.0000,0.0000"
        )]
        [InlineData(
            "manual3small",
            DxfAttachmentPoint.TopLeft,
            "-1.0000,-10.0000;-1.0000,-27.3730;47.0000,-10.1409;95.0000,-10.0000;95.0000,-26.6667;95.0000,-43.9633;143.0000,-10.0000;143.0000,-26.6667;143.0000,-43.3333;143.0000,-60.0000;143.0000,-76.6667;143.0000,-94.0396;143.0000,-110.8472;143.0000,-127.5139"
        )]
        [InlineData(
            "manual3small",
            DxfAttachmentPoint.BottomCenter,
            "-84.5000,17.3730;-85.2644,0.0000;-37.3744,0.1409;11.0000,33.9633;10.7283,17.2966;11.0000,0.0000;59.0000,117.5139;59.0000,100.8472;58.7283,84.1806;55.5000,67.5139;56.0000,50.8472;55.2356,33.4743;55.1256,16.6667;55.5000,0.0000"
        )]
        [InlineData(
            "manual3small",
            DxfAttachmentPoint.MiddleRight,
            "-168.0000,3.6865;-169.5287,-13.6865;-121.7488,-5.0000;-73.0000,11.9816;-73.5433,-4.6850;-73.0000,-21.9816;-25.0000,53.7570;-25.0000,37.0903;-25.5433,20.4236;-32.0000,3.7570;-31.0000,-12.9097;-32.5287,-30.2827;-32.7488,-47.0903;-32.0000,-63.7570"
        )]
        [InlineData(
            "manual3",
            DxfAttachmentPoint.TopCenter,
            "-60.5000,-10.0000;-61.2644,-27.3730;-61.3744,-44.1806;-61.0000,-60.8472;-61.2717,-77.5139;-13.0000,-10.6299;-13.0000,-27.2966;-13.0000,-43.9633;-13.2717,-60.6299;31.5000,-10.0000;32.0000,-26.6667;31.2356,-44.0396;31.1256,-60.8472;31.5000,-77.5139"
        )]
        [InlineData(
            "static2",
            DxfAttachmentPoint.BottomRight,
            "-72.0000,84.8105;-73.5287,67.4375;-73.7488,50.6299;-73.0000,33.9633;-73.5433,17.2966;-73.0000,0.0000;-25.0000,117.5139;-25.0000,100.8472;-25.5433,84.1806;-32.0000,67.5139;-31.0000,50.8472;-32.5287,33.4743;-32.7488,16.6667;-32.0000,0.0000"
        )]
        [InlineData(
            "static3",
            DxfAttachmentPoint.MiddleLeft,
            "-1.0000,20.4236;-1.0000,3.0507;-1.0000,-13.7570;-1.0000,-30.4236;40.0000,20.3150;40.0000,3.0184;40.0000,-13.6483;40.0000,-30.3150;81.0000,37.0903;81.0000,20.4236;81.0000,3.7570;81.0000,-13.6160;81.0000,-30.4236;81.0000,-47.0903"
        )]
        [InlineData(
            "dynauto",
            DxfAttachmentPoint.MiddleCenter,
            "-60.5000,29.0719;-61.2644,11.6990;-61.3744,-5.1087;-61.0000,-21.7753;-61.2717,-38.4420;-13.0000,28.0184;-13.0000,11.3517;-13.0000,-5.3150;-13.2717,-21.9816;-16.5000,-38.6483;32.0000,20.4236;31.2356,3.0507;31.1256,-13.7570;31.5000,-30.4236"
        )]
        [InlineData(
            "man30",
            DxfAttachmentPoint.TopLeft,
            "-1.0000,-10.0000;-1.0000,-27.3730;47.0000,-10.1409;47.0000,-26.8076;47.0000,-43.4743;47.0000,-60.7709;47.0000,-77.4375;47.0000,-94.1042;47.0000,-110.7709;47.0000,-127.4375;47.0000,-144.1042;47.0000,-161.4772;47.0000,-178.2848;47.0000,-194.9514;47.0000,-211.6181;47.0000,-228.9147;47.0000,-245.5814;47.0000,-262.2480;47.0000,-278.9147;47.0000,-296.2877;47.0000,-313.6606;47.0000,-331.0336;47.0000,-348.4066;47.0000,-365.7795;47.0000,-383.1525;47.0000,-400.5255;47.0000,-417.8984;47.0000,-435.2714;47.0000,-452.6444;47.0000,-469.4520"
        )]
        [InlineData(
            "man30x2",
            DxfAttachmentPoint.TopLeft,
            "-1.0000,-10.0000;-1.0000,-27.3730;47.0000,-10.1409;47.0000,-26.8076;95.0000,-10.0000;95.0000,-27.2966;95.0000,-43.9633;95.0000,-60.6299;95.0000,-77.2966;95.0000,-93.9633;95.0000,-110.6299;95.0000,-128.0029;95.0000,-144.8105;95.0000,-161.4772;95.0000,-178.1438;95.0000,-195.4404;95.0000,-212.1071;95.0000,-228.7738;95.0000,-245.4404;95.0000,-262.8134;95.0000,-280.1864;95.0000,-297.5593;95.0000,-314.9323;95.0000,-332.3052;95.0000,-349.6782;95.0000,-367.0512;95.0000,-384.4241;95.0000,-401.7971;95.0000,-419.1701;95.0000,-435.9777"
        )]
        [InlineData(
            "man30x50",
            DxfAttachmentPoint.TopLeft,
            "-1.0000,-10.0000;-1.0000,-27.3730;47.0000,-10.1409;47.0000,-26.8076;47.0000,-43.4743;95.0000,-10.6299;95.0000,-27.2966;95.0000,-43.9633;95.0000,-60.6299;95.0000,-77.2966;95.0000,-93.9633;95.0000,-111.3362;95.0000,-128.1438;95.0000,-144.8105;95.0000,-161.4772;95.0000,-178.7738;95.0000,-195.4404;95.0000,-212.1071;95.0000,-228.7738;95.0000,-246.1467;95.0000,-263.5197;95.0000,-280.8927;95.0000,-298.2656;95.0000,-315.6386;95.0000,-333.0115;95.0000,-350.3845;95.0000,-367.7575;95.0000,-385.1304;95.0000,-402.5034;95.0000,-419.3110"
        )]
        [InlineData(
            "man50",
            DxfAttachmentPoint.TopLeft,
            "-1.0000,-10.0000;-1.0000,-27.3730;-1.0000,-44.1806;47.0000,-10.0000;47.0000,-26.6667;47.0000,-43.9633;47.0000,-60.6299;47.0000,-77.2966;47.0000,-93.9633;47.0000,-110.6299;47.0000,-127.2966;47.0000,-144.6696;47.0000,-161.4772;47.0000,-178.1438;47.0000,-194.8105;47.0000,-212.1071;47.0000,-228.7738;47.0000,-245.4404;47.0000,-262.1071;47.0000,-279.4801;47.0000,-296.8530;47.0000,-314.2260;47.0000,-331.5990;47.0000,-348.9719;47.0000,-366.3449;47.0000,-383.7178;47.0000,-401.0908;47.0000,-418.4638;47.0000,-435.8367;47.0000,-452.6444"
        )]
        [InlineData(
            "man20x3",
            DxfAttachmentPoint.TopLeft,
            "-1.0000,-10.0000;47.0000,-10.7063;95.0000,-10.1409;143.0000,-10.0000;143.0000,-26.6667;143.0000,-43.9633;143.0000,-60.6299;143.0000,-77.2966;143.0000,-93.9633;143.0000,-110.6299;143.0000,-127.2966;143.0000,-144.6696;143.0000,-161.4772;143.0000,-178.1438;143.0000,-194.8105;143.0000,-212.1071;143.0000,-228.7738;143.0000,-245.4404;143.0000,-262.1071;143.0000,-279.4801;143.0000,-296.8530;143.0000,-314.2260;143.0000,-331.5990;143.0000,-348.9719;143.0000,-366.3449;143.0000,-383.7178;143.0000,-401.0908;143.0000,-418.4638;143.0000,-435.8367;143.0000,-452.6444"
        )]
        public void TheLinesOfColumns_StandWhereAutoCADPutsThem(
            string kind,
            DxfAttachmentPoint attachment,
            string expected
        )
        {
            DxfMText mtext = Made(kind, attachment, out CadDocument document);

            CadMTextLayout layout = document.LayoutOf(mtext);

            string[] places = expected.Split(';');
            Assert.Equal(places.Length, layout.Pieces.Count);

            for (int i = 0; i < places.Length; i++)
            {
                string[] xy = places[i].Split(',');
                double x = double.Parse(xy[0], CultureInfo.InvariantCulture);
                double y = double.Parse(xy[1], CultureInfo.InvariantCulture);
                Assert.True(
                    Math.Abs(x - layout.Pieces[i].X) < Close && Math.Abs(y - layout.Pieces[i].Y) < Close,
                    kind
                        + " "
                        + attachment
                        + " piece "
                        + i
                        + " "
                        + layout.Pieces[i].Text
                        + " stands at "
                        + layout.Pieces[i].X
                        + ", "
                        + layout.Pieces[i].Y
                        + ", AutoCAD's "
                        + x
                        + ", "
                        + y
                );
            }
        }

        [Theory]
        [InlineData("manual3", 3)]
        [InlineData("manual2", 3)]
        [InlineData("manual3small", 4)]
        [InlineData("man30", 2)]
        [InlineData("man20x3", 4)]
        public void ColumnsOfTheirOwnHeights_AreAsManyAsTheLinesFill_AndOneMoreHoldsWhatTheyDoNot(
            string kind,
            int columns
        )
        {
            DxfMText mtext = Made(kind, DxfAttachmentPoint.TopLeft, out CadDocument document);

            CadMTextLayout layout = document.LayoutOf(mtext);

            Assert.Equal(columns, layout.ColumnCount);
            Assert.Equal(columns, layout.Pieces.Select(p => p.Column).Distinct().Count());
        }
    }
}
