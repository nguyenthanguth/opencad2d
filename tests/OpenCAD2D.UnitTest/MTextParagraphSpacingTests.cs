using System;
using System.Globalization;
using System.Linq;
using GeometryHelper.Dxf;
using GeometryHelper.Geometry;
using Xunit;

namespace OpenCAD2D.UnitTest
{
    /// <summary>
    /// The paragraph codes that set the line spacing, <c>\pxsm</c> (a multiple), <c>\pxsa</c> ("at least") and <c>\pxse</c> (exactly),
    /// as AutoCAD 2021 lays them out (accoreconsole: the MTEXT's actual height of group 43 and its exploded pieces, in the
    /// built-in font, opencad2d.shx, with letters 10 high unless a case says otherwise). Each case lists the height of the
    /// MTEXT and the baseline of each piece, as "h=80.6299;A:-10;B:-43.3333". Letters of the MTEXT at 2x and 3x of its height
    /// (<c>{\H2x;B}</c>) are the tall runs.
    /// </summary>
    public sealed class MTextParagraphSpacingTests
    {
        private const double Close = 1E-4;

        private static CadMTextLayout Laid(string contents, double width, double height, Action<DxfMText> more = null)
        {
            var db = new DxfDatabase("AC1032");
            db.TextStyleTable["Standard"].FileName = "opencad2d.shx";
            var mtext = new DxfMText
            {
                Contents = contents,
                TextHeight = height,
                Width = width,
                Location = new GeoPoint3(0, 0, 0),
            };
            db.ModelSpace.AppendEntity(mtext);
            more?.Invoke(mtext);
            CadDocument document = CadDocument.From(db);
            document.Fonts = new CadFonts(null, CadFonts.BuiltInName, null);
            return document.LayoutOf((DxfMText)document.FindEntity(mtext.Handle));
        }

        private static void Matches(CadMTextLayout layout, string expected)
        {
            string[] parts = expected.Split(';');
            Assert.Equal(double.Parse(parts[0].Substring(2), CultureInfo.InvariantCulture), layout.ActualHeight, 4);
            string[] pieces = parts.Skip(1).ToArray();
            Assert.Equal(pieces.Length, layout.Pieces.Count);

            for (int i = 0; i < pieces.Length; i++)
            {
                int colon = pieces[i].LastIndexOf(':');
                Assert.Equal(pieces[i].Substring(0, colon), layout.Pieces[i].Text);
                double y = double.Parse(pieces[i].Substring(colon + 1), CultureInfo.InvariantCulture);
                Assert.True(
                    Math.Abs(y - layout.Pieces[i].Y) < Close,
                    "piece " + i + " " + layout.Pieces[i].Text + " stands at " + layout.Pieces[i].Y + ", AutoCAD's " + y
                );
            }
        }

        [Theory]
        [InlineData("A\\PB\\PC", 0, 10, "h=44.5932;A:-10.0000;B:-26.6667;C:-43.9633")]
        [InlineData("\\pxsm2;A\\PB\\PC", 0, 10, "h=80.6299;A:-10.0000;B:-43.3333;C:-77.2966")]
        [InlineData("\\pxsm1.5;A\\PB\\PC", 0, 10, "h=63.9633;A:-10.0000;B:-35.0000;C:-60.6299")]
        [InlineData("\\pxsm0.5;A\\PB\\PC", 0, 10, "h=25.3937;A:-10.0000;B:-18.3333;C:-27.2966")]
        [InlineData("\\pxsm2;A\\P{\\H2x;B}\\PC", 0, 10, "h=113.9633;A:-10.0000;B:-53.3333;C:-110.6299")]
        [InlineData("\\pxsm2;{\\H2x;A}\\PB\\PC", 0, 10, "h=113.9633;A:-20.0000;B:-76.6667;C:-110.6299")]
        [InlineData("\\pxsm2;A\\PB\\P{\\H2x;C}", 0, 10, "h=94.5932;A:-10.0000;B:-43.3333;C:-87.9265")]
        [InlineData("\\pxsm1;A\\P{\\H2x;B}\\PC", 0, 10, "h=63.9633;A:-10.0000;B:-36.6667;C:-60.6299")]
        [InlineData("\\pxsm0.5;A\\P{\\H2x;B}\\PC", 0, 10, "h=33.7270;A:-10.0000;B:-28.3333;C:-35.6299")]
        [InlineData("\\pxsm2;A\\P{\\H3x;B}\\PC", 0, 10, "h=147.2966;A:-10.0000;B:-63.3333;C:-143.9633")]
        [InlineData("\\pxsm2;A\\P{\\H0.5x;B}\\PC", 0, 10, "h=63.9633;A:-10.0000;B:-38.3333;C:-60.6299")]
        [InlineData("\\pxsm2;A{\\H2x;B}C\\PD", 0, 10, "h=80.6213;A:-20.0000;B:-20.0000;C:-20.0000;D:-77.2880")]
        [InlineData("\\pxsm2;A\\PB\\PC", 0, 0.048, "h=0.3870;A:-0.0480;B:-0.2080;C:-0.3710")]
        [InlineData("\\pxsm2;A\\P{\\H3x;B}\\PC", 0, 0.048, "h=0.7070;A:-0.0480;B:-0.3040;C:-0.6910")]
        [InlineData("\\pxsm2;A\\PB", 0, 10, "h=46.6667;A:-10.0000;B:-43.3333")]
        [InlineData("\\pxsm2;A\\P\\S1/2;\\PC", 0, 10, "h=107.3957;A:-10.0000;1:-43.3333;2:-57.3730;C:-104.0623")]
        [InlineData("\\pxsa2;A\\PB\\PC", 0, 10, "h=93.3333;A:-23.3333;B:-56.6667;C:-90.0000")]
        [InlineData("\\pxsa1;A\\PB\\PC", 0, 10, "h=47.2966;A:-10.0000;B:-26.6667;C:-43.9633")]
        [InlineData("\\pxsa0.5;A\\PB\\PC", 0, 10, "h=47.2966;A:-10.0000;B:-26.6667;C:-43.9633")]
        [InlineData("\\pxsa3;A\\PB\\PC", 0, 10, "h=140.0000;A:-36.6667;B:-86.6667;C:-136.6667")]
        [InlineData("\\pxsa2;{\\H2x;A}\\PB\\PC", 0, 10, "h=93.3333;A:-20.0000;B:-56.6667;C:-90.0000")]
        [InlineData("\\pxsa1;{\\H2x;A}\\PB\\PC", 0, 10, "h=63.9633;A:-20.0000;B:-43.3333;C:-60.6299")]
        [InlineData("\\pxsa0.5;{\\H2x;A}\\PB\\PC", 0, 10, "h=63.9633;A:-20.0000;B:-43.3333;C:-60.6299")]
        [InlineData("\\pxsa3;{\\H2x;A}\\PB\\PC", 0, 10, "h=140.0000;A:-33.3333;B:-86.6667;C:-136.6667")]
        [InlineData("\\pxsa2;A\\P{\\H2x;B}\\PC", 0, 10, "h=93.3333;A:-23.3333;B:-53.3333;C:-90.0000")]
        [InlineData("\\pxsa1;A\\P{\\H2x;B}\\PC", 0, 10, "h=63.9633;A:-10.0000;B:-36.6667;C:-60.6299")]
        [InlineData("\\pxsa0.5;A\\P{\\H2x;B}\\PC", 0, 10, "h=63.9633;A:-10.0000;B:-36.6667;C:-60.6299")]
        [InlineData("\\pxsa2;A\\PB\\P{\\H2x;C}", 0, 10, "h=94.5932;A:-23.3333;B:-56.6667;C:-87.9265")]
        [InlineData("\\pxsa2;A", 0, 10, "h=26.6667;A:-23.3333")]
        [InlineData("\\pxsa2;{\\H2x;A}", 0, 10, "h=26.6667;A:-20.0000")]
        [InlineData("\\pxsa1;{\\H3x;A}", 0, 10, "h=40.0000;A:-30.0000")]
        [InlineData("\\pxsa2;A\\PB", 0, 10, "h=60.0000;A:-23.3333;B:-56.6667")]
        [InlineData("\\pxsa2;A\\PB\\PC", 0, 0.048, "h=0.4480;A:-0.1120;B:-0.2720;C:-0.4320")]
        [InlineData("\\pxse2;A\\P{\\H2x;B}\\PC", 0, 10, "h=87.2966;A:-20.0000;B:-53.3333;C:-86.6667")]
        [InlineData("\\pxsa2;AA AA AA AA", 20, 10, "h=126.6667;AA:-23.3333;AA:-56.6667;AA:-90.0000;AA:-123.3333")]
        [InlineData("\\pxsm2;AA AA AA AA", 20, 10, "h=113.3333;AA:-10.0000;AA:-43.3333;AA:-76.6667;AA:-110.0000")]
        [InlineData("A\\P\\pxsa2;B\\PC", 0, 10, "h=76.6667;A:-10.0000;B:-40.0000;C:-73.3333")]
        [InlineData("A\\P\\pxsm2;B\\PC", 0, 10, "h=63.9633;A:-10.0000;B:-26.6667;C:-60.6299")]
        [InlineData("\\pxsm2;A\\P\\pxsm1;B\\PC", 0, 10, "h=63.9633;A:-10.0000;B:-43.3333;C:-60.6299")]
        [InlineData("A\\P\\pxsm2;{\\H2x;B}\\PC", 0, 10, "h=97.2966;A:-10.0000;B:-36.6667;C:-93.9633")]
        [InlineData("A\\P\\pxsa2;{\\H2x;B}\\PC", 0, 10, "h=76.6667;A:-10.0000;B:-36.6667;C:-73.3333")]
        [InlineData("\\pxse0.5;A\\P{\\H0.5x;A}", 0, 10, "h=16.6667;A:-5.0000;A:-13.3333")]
        [InlineData("\\pxse0.5;{\\H2x;A}", 0, 10, "h=8.3333;A:-5.0000")]
        [InlineData("\\pxse0.5;A\\P{\\H0.5x;C}", 0, 10, "h=16.6667;A:-5.0000;C:-13.3333")]
        [InlineData("\\pxse0.5;A\\P{\\H1x;C}", 0, 10, "h=16.6667;A:-5.0000;C:-13.3333")]
        [InlineData("\\pxse0.5;A\\P{\\H3x;C}", 0, 10, "h=16.6667;A:-5.0000;C:-13.3333")]
        [InlineData("\\pxse0.5;{\\H2x;C}", 0, 10, "h=8.3333;C:-5.0000")]
        [InlineData("\\pxse1;A\\P{\\H0.5x;A}", 0, 10, "h=26.6667;A:-10.0000;A:-26.6667")]
        [InlineData("\\pxse1;{\\H2x;A}", 0, 10, "h=16.6667;A:-10.0000")]
        [InlineData("\\pxse1;A\\P{\\H0.5x;C}", 0, 10, "h=26.9816;A:-10.0000;C:-26.6667")]
        [InlineData("\\pxse1;A\\P{\\H1x;C}", 0, 10, "h=33.3333;A:-10.0000;C:-26.6667")]
        [InlineData("\\pxse1;A\\P{\\H3x;C}", 0, 10, "h=33.3333;A:-10.0000;C:-26.6667")]
        [InlineData("\\pxse1;{\\H2x;C}", 0, 10, "h=16.6667;C:-10.0000")]
        [InlineData("\\pxse2;A\\P{\\H0.5x;A}", 0, 10, "h=46.6667;A:-20.0000;A:-53.3333")]
        [InlineData("\\pxse2;{\\H2x;A}", 0, 10, "h=33.3333;A:-20.0000")]
        [InlineData("\\pxse2;A\\P{\\H0.5x;C}", 0, 10, "h=46.9816;A:-20.0000;C:-53.3333")]
        [InlineData("\\pxse2;A\\P{\\H1x;C}", 0, 10, "h=53.9633;A:-20.0000;C:-53.3333")]
        [InlineData("\\pxse2;A\\P{\\H3x;C}", 0, 10, "h=66.6667;A:-20.0000;C:-53.3333")]
        [InlineData("\\pxse2;{\\H2x;C}", 0, 10, "h=33.3333;C:-20.0000")]
        [InlineData("\\pxse3;A\\P{\\H0.5x;A}", 0, 10, "h=66.6667;A:-30.0000;A:-80.0000")]
        [InlineData("\\pxse3;{\\H2x;A}", 0, 10, "h=36.6667;A:-30.0000")]
        [InlineData("\\pxse3;A\\P{\\H0.5x;C}", 0, 10, "h=66.9816;A:-30.0000;C:-80.0000")]
        [InlineData("\\pxse3;A\\P{\\H1x;C}", 0, 10, "h=73.9633;A:-30.0000;C:-80.0000")]
        [InlineData("\\pxse3;A\\P{\\H3x;C}", 0, 10, "h=100.0000;A:-30.0000;C:-80.0000")]
        [InlineData("\\pxse3;{\\H2x;C}", 0, 10, "h=37.9265;C:-30.0000")]
        [InlineData("\\pxsa0.5;{\\H0.5x;A}", 0, 10, "h=6.6667;A:-5.0000")]
        [InlineData("\\pxsa0.5;{\\H3x;A}", 0, 10, "h=40.0000;A:-30.0000")]
        [InlineData("\\pxsa1;{\\H2x;A}", 0, 10, "h=26.6667;A:-20.0000")]
        [InlineData("\\pxsa1.5;{\\H1.5x;A}", 0, 10, "h=20.0000;A:-15.0000")]
        [InlineData("\\pxsa2;{\\H1x;A}", 0, 10, "h=26.6667;A:-23.3333")]
        [InlineData("\\pxsa3;{\\H0.5x;A}", 0, 10, "h=40.0000;A:-38.3333")]
        [InlineData("\\pxsa3;{\\H3x;A}", 0, 10, "h=40.0000;A:-30.0000")]
        [InlineData("\\pxsa4;{\\H2x;A}", 0, 10, "h=53.3333;A:-46.6667")]
        [InlineData("\\pxsa0.5;{\\H1x;A}\\P{\\H1x;B}", 0, 10, "h=30.0000;A:-10.0000;B:-26.6667")]
        [InlineData("\\pxsa0.5;{\\H2x;A}\\P{\\H1x;B}", 0, 10, "h=46.6667;A:-20.0000;B:-43.3333")]
        [InlineData("\\pxsa1;{\\H0.5x;A}\\P{\\H1x;B}", 0, 10, "h=30.0000;A:-11.6667;B:-26.6667")]
        [InlineData("\\pxsa1;{\\H1.5x;A}\\P{\\H1x;B}", 0, 10, "h=38.3333;A:-15.0000;B:-35.0000")]
        [InlineData("\\pxsa1;{\\H3x;A}\\P{\\H1x;B}", 0, 10, "h=63.3333;A:-30.0000;B:-60.0000")]
        [InlineData("\\pxsa1.5;{\\H1x;A}\\P{\\H1x;B}", 0, 10, "h=45.0000;A:-16.6667;B:-41.6667")]
        [InlineData("\\pxsa1.5;{\\H2x;A}\\P{\\H1x;B}", 0, 10, "h=53.3333;A:-20.0000;B:-50.0000")]
        [InlineData("\\pxsa2;{\\H0.5x;A}\\P{\\H1x;B}", 0, 10, "h=60.0000;A:-25.0000;B:-56.6667")]
        [InlineData("\\pxsa2;{\\H1.5x;A}\\P{\\H1x;B}", 0, 10, "h=60.0000;A:-21.6667;B:-56.6667")]
        [InlineData("\\pxsa2;{\\H3x;A}\\P{\\H1x;B}", 0, 10, "h=76.6667;A:-30.0000;B:-73.3333")]
        [InlineData("\\pxsa3;{\\H1x;A}\\P{\\H1x;B}", 0, 10, "h=90.0000;A:-36.6667;B:-86.6667")]
        [InlineData("\\pxsa3;{\\H2x;A}\\P{\\H1x;B}", 0, 10, "h=90.0000;A:-33.3333;B:-86.6667")]
        [InlineData("\\pxsa4;{\\H0.5x;A}\\P{\\H1x;B}", 0, 10, "h=120.0000;A:-51.6667;B:-116.6667")]
        [InlineData("\\pxsa4;{\\H1.5x;A}\\P{\\H1x;B}", 0, 10, "h=120.0000;A:-48.3333;B:-116.6667")]
        [InlineData("\\pxsa4;{\\H3x;A}\\P{\\H1x;B}", 0, 10, "h=120.0000;A:-43.3333;B:-116.6667")]
        [InlineData(
            "\\pxsm0.5;{\\H0.5x;A}\\P{\\H0.5x;B}\\P{\\H0.5x;A}",
            0,
            10,
            "h=12.5000;A:-5.0000;B:-9.1667;A:-13.3333"
        )]
        [InlineData(
            "\\pxsm0.5;{\\H0.5x;A}\\P{\\H2x;B}\\P{\\H1x;A}",
            0,
            10,
            "h=29.1667;A:-5.0000;B:-24.1667;A:-30.8333"
        )]
        [InlineData("\\pxsm0.5;{\\H1x;A}\\P{\\H1x;B}\\P{\\H2x;A}", 0, 10, "h=33.3333;A:-10.0000;B:-18.3333;A:-36.6667")]
        [InlineData(
            "\\pxsm0.5;{\\H2x;A}\\P{\\H1x;B}\\P{\\H0.5x;A}",
            0,
            10,
            "h=29.1667;A:-20.0000;B:-26.6667;A:-30.0000"
        )]
        [InlineData(
            "\\pxsm0.8;{\\H0.5x;A}\\P{\\H0.5x;B}\\P{\\H1x;A}",
            0,
            10,
            "h=26.6667;A:-5.0000;B:-11.6667;A:-23.3333"
        )]
        [InlineData(
            "\\pxsm0.8;{\\H0.5x;A}\\P{\\H2x;B}\\P{\\H2x;A}",
            0,
            10,
            "h=60.0000;A:-5.0000;B:-26.6667;A:-53.3333"
        )]
        [InlineData(
            "\\pxsm0.8;{\\H1x;A}\\P{\\H2x;B}\\P{\\H0.5x;A}",
            0,
            10,
            "h=46.6667;A:-10.0000;B:-33.3333;A:-45.0000"
        )]
        [InlineData("\\pxsm0.8;{\\H2x;A}\\P{\\H1x;B}\\P{\\H1x;A}", 0, 10, "h=53.3333;A:-20.0000;B:-36.6667;A:-50.0000")]
        [InlineData(
            "\\pxsm1;{\\H0.5x;A}\\P{\\H0.5x;B}\\P{\\H2x;A}",
            0,
            10,
            "h=43.3333;A:-5.0000;B:-13.3333;A:-36.6667"
        )]
        [InlineData(
            "\\pxsm1;{\\H1x;A}\\P{\\H0.5x;B}\\P{\\H0.5x;A}",
            0,
            10,
            "h=31.6667;A:-10.0000;B:-21.6667;A:-30.0000"
        )]
        [InlineData("\\pxsm1;{\\H1x;A}\\P{\\H2x;B}\\P{\\H1x;A}", 0, 10, "h=63.3333;A:-10.0000;B:-36.6667;A:-60.0000")]
        [InlineData("\\pxsm1;{\\H2x;A}\\P{\\H1x;B}\\P{\\H2x;A}", 0, 10, "h=76.6667;A:-20.0000;B:-43.3333;A:-70.0000")]
        [InlineData(
            "\\pxsm1.5;{\\H0.5x;A}\\P{\\H1x;B}\\P{\\H0.5x;A}",
            0,
            10,
            "h=44.1667;A:-5.0000;B:-22.5000;A:-42.5000"
        )]
        [InlineData(
            "\\pxsm1.5;{\\H1x;A}\\P{\\H0.5x;B}\\P{\\H1x;A}",
            0,
            10,
            "h=50.8333;A:-10.0000;B:-30.0000;A:-47.5000"
        )]
        [InlineData(
            "\\pxsm1.5;{\\H1x;A}\\P{\\H2x;B}\\P{\\H2x;A}",
            0,
            10,
            "h=101.6667;A:-10.0000;B:-45.0000;A:-95.0000"
        )]
        [InlineData(
            "\\pxsm1.5;{\\H2x;A}\\P{\\H2x;B}\\P{\\H0.5x;A}",
            0,
            10,
            "h=106.6667;A:-20.0000;B:-70.0000;A:-105.0000"
        )]
        [InlineData("\\pxsm2;{\\H0.5x;A}\\P{\\H1x;B}\\P{\\H1x;A}", 0, 10, "h=63.3333;A:-5.0000;B:-26.6667;A:-60.0000")]
        [InlineData("\\pxsm2;{\\H1x;A}\\P{\\H0.5x;B}\\P{\\H2x;A}", 0, 10, "h=76.6667;A:-10.0000;B:-38.3333;A:-70.0000")]
        [InlineData(
            "\\pxsm2;{\\H2x;A}\\P{\\H0.5x;B}\\P{\\H0.5x;A}",
            0,
            10,
            "h=90.0000;A:-20.0000;B:-71.6667;A:-88.3333"
        )]
        [InlineData("\\pxsm2;{\\H2x;A}\\P{\\H2x;B}\\P{\\H1x;A}", 0, 10, "h=146.6667;A:-20.0000;B:-86.6667;A:-143.3333")]
        [InlineData(
            "\\pxsa0.5;{\\H0.5x;A}\\P{\\H0.5x;B}\\P{\\H0.5x;A}",
            0,
            10,
            "h=23.3333;A:-5.0000;B:-13.3333;A:-21.6667"
        )]
        [InlineData(
            "\\pxsa0.5;{\\H1x;A}\\P{\\H0.5x;B}\\P{\\H1x;A}",
            0,
            10,
            "h=38.3333;A:-10.0000;B:-21.6667;A:-35.0000"
        )]
        [InlineData(
            "\\pxsa0.5;{\\H2x;A}\\P{\\H0.5x;B}\\P{\\H2x;A}",
            0,
            10,
            "h=68.3333;A:-20.0000;B:-38.3333;A:-61.6667"
        )]
        [InlineData(
            "\\pxsa0.5;{\\H3x;A}\\P{\\H0.5x;B}\\P{\\H3x;A}",
            0,
            10,
            "h=98.3333;A:-30.0000;B:-55.0000;A:-88.3333"
        )]
        [InlineData(
            "\\pxsa0.8;{\\H0.5x;A}\\P{\\H1x;B}\\P{\\H0.5x;A}",
            0,
            10,
            "h=40.6667;A:-9.0000;B:-23.3333;A:-39.0000"
        )]
        [InlineData("\\pxsa0.8;{\\H1x;A}\\P{\\H1x;B}\\P{\\H1x;A}", 0, 10, "h=46.6667;A:-10.0000;B:-26.6667;A:-43.3333")]
        [InlineData("\\pxsa0.8;{\\H2x;A}\\P{\\H1x;B}\\P{\\H2x;A}", 0, 10, "h=76.6667;A:-20.0000;B:-43.3333;A:-70.0000")]
        [InlineData(
            "\\pxsa0.8;{\\H3x;A}\\P{\\H1x;B}\\P{\\H3x;A}",
            0,
            10,
            "h=106.6667;A:-30.0000;B:-60.0000;A:-96.6667"
        )]
        [InlineData(
            "\\pxsa1;{\\H0.5x;A}\\P{\\H2x;B}\\P{\\H0.5x;A}",
            0,
            10,
            "h=63.3333;A:-11.6667;B:-36.6667;A:-61.6667"
        )]
        [InlineData("\\pxsa1;{\\H1x;A}\\P{\\H2x;B}\\P{\\H1x;A}", 0, 10, "h=63.3333;A:-10.0000;B:-36.6667;A:-60.0000")]
        [InlineData("\\pxsa1;{\\H2x;A}\\P{\\H2x;B}\\P{\\H2x;A}", 0, 10, "h=93.3333;A:-20.0000;B:-53.3333;A:-86.6667")]
        [InlineData("\\pxsa1;{\\H3x;A}\\P{\\H2x;B}\\P{\\H3x;A}", 0, 10, "h=123.3333;A:-30.0000;B:-70.0000;A:-113.3333")]
        [InlineData(
            "\\pxsa1.5;{\\H0.5x;A}\\P{\\H3x;B}\\P{\\H0.5x;A}",
            0,
            10,
            "h=95.0000;A:-18.3333;B:-55.0000;A:-93.3333"
        )]
        [InlineData("\\pxsa1.5;{\\H1x;A}\\P{\\H3x;B}\\P{\\H1x;A}", 0, 10, "h=95.0000;A:-16.6667;B:-55.0000;A:-91.6667")]
        [InlineData(
            "\\pxsa1.5;{\\H2x;A}\\P{\\H3x;B}\\P{\\H2x;A}",
            0,
            10,
            "h=110.0000;A:-20.0000;B:-63.3333;A:-103.3333"
        )]
        [InlineData(
            "\\pxsa1.5;{\\H3x;A}\\P{\\H3x;B}\\P{\\H3x;A}",
            0,
            10,
            "h=140.0000;A:-30.0000;B:-80.0000;A:-130.0000"
        )]
        [InlineData(
            "\\pxsa2;{\\H1x;A}\\P{\\H0.5x;B}\\P{\\H0.5x;A}",
            0,
            10,
            "h=93.3333;A:-23.3333;B:-58.3333;A:-91.6667"
        )]
        [InlineData("\\pxsa2;{\\H2x;A}\\P{\\H0.5x;B}\\P{\\H1x;A}", 0, 10, "h=93.3333;A:-20.0000;B:-58.3333;A:-90.0000")]
        [InlineData(
            "\\pxsa2;{\\H3x;A}\\P{\\H0.5x;B}\\P{\\H2x;A}",
            0,
            10,
            "h=110.0000;A:-30.0000;B:-75.0000;A:-103.3333"
        )]
        [InlineData(
            "\\pxsa3;{\\H0.5x;A}\\P{\\H0.5x;B}\\P{\\H3x;A}",
            0,
            10,
            "h=140.0000;A:-38.3333;B:-88.3333;A:-130.0000"
        )]
        [InlineData(
            "\\pxsa3;{\\H1x;A}\\P{\\H1x;B}\\P{\\H0.5x;A}",
            0,
            10,
            "h=140.0000;A:-36.6667;B:-86.6667;A:-138.3333"
        )]
        [InlineData("\\pxsa3;{\\H2x;A}\\P{\\H1x;B}\\P{\\H1x;A}", 0, 10, "h=140.0000;A:-33.3333;B:-86.6667;A:-136.6667")]
        [InlineData("\\pxsa3;{\\H3x;A}\\P{\\H1x;B}\\P{\\H2x;A}", 0, 10, "h=140.0000;A:-30.0000;B:-86.6667;A:-133.3333")]
        [InlineData("\\pxsm2;A\\P\\S1/2;\\PA", 0, 10, "h=106.7657;A:-10.0000;1:-43.3333;2:-57.3730;A:-103.4324")]
        [InlineData("\\pxsm2;A\\PA\\P\\S1/2;", 0, 10, "h=90.7063;A:-10.0000;A:-43.3333;1:-76.6667;2:-90.7063")]
        [InlineData("\\pxsm2;C\\PC\\PC", 0, 10, "h=83.7795;C:-10.6299;C:-45.5381;C:-80.4462")]
        [InlineData(
            "\\pxsm2;AA AA {\\H2x;AA} AA AA AA",
            45,
            10,
            "h=146.6667;AA AA:-10.0000;AA:-53.3333;AA AA:-110.0000;AA:-143.3333"
        )]
        [InlineData("\\pxsm2;\\pxb1,a2;A\\PA", 0, 10, "h=86.6667;A:-20.0000;A:-83.3333")]
        [InlineData("\\pxsm2;A{\\H2x;A}{\\A1;A}\\PA", 0, 10, "h=80.0000;A:-20.0000;A:-20.0000;A:-15.0000;A:-76.6667")]
        [InlineData("\\pxsm0.5;A\\P\\S1/2;\\PA", 0, 10, "h=31.6914;A:-10.0000;1:-18.3333;2:-32.3730;A:-33.3581")]
        [InlineData("\\pxsm0.5;A\\PA\\P\\S1/2;", 0, 10, "h=31.6914;A:-10.0000;A:-18.3333;1:-26.6667;2:-40.7063")]
        [InlineData("\\pxsm0.5;C\\PC\\PC", 0, 10, "h=26.1811;C:-10.6299;C:-19.3570;C:-28.0840")]
        [InlineData(
            "\\pxsm0.5;AA AA {\\H2x;AA} AA AA AA",
            45,
            10,
            "h=41.6667;AA AA:-10.0000;AA:-28.3333;AA AA:-35.0000;AA:-43.3333"
        )]
        [InlineData("\\pxsm0.5;\\pxb1,a2;A\\PA", 0, 10, "h=56.6667;A:-20.0000;A:-58.3333")]
        [InlineData("\\pxsm0.5;A{\\H2x;A}{\\A1;A}\\PA", 0, 10, "h=25.0000;A:-20.0000;A:-20.0000;A:-15.0000;A:-26.6667")]
        [InlineData("\\pxsa1;A\\P\\S1/2;\\PA", 0, 10, "h=60.0495;A:-10.0000;1:-26.6667;2:-40.7063;A:-56.7162")]
        [InlineData("\\pxsa1;A\\PA\\P\\S1/2;", 0, 10, "h=57.3730;A:-10.0000;A:-26.6667;1:-43.3333;2:-57.3730")]
        [InlineData("\\pxsa1;C\\PC\\PC", 0, 10, "h=48.8714;C:-10.6299;C:-28.0840;C:-45.5381")]
        [InlineData(
            "\\pxsa1;AA AA {\\H2x;AA} AA AA AA",
            45,
            10,
            "h=80.0000;AA AA:-10.0000;AA:-36.6667;AA AA:-60.0000;AA:-76.6667"
        )]
        [InlineData("\\pxsa1;\\pxb1,a2;A\\PA", 0, 10, "h=70.0000;A:-20.0000;A:-66.6667")]
        [InlineData("\\pxsa1;A{\\H2x;A}{\\A1;A}\\PA", 0, 10, "h=46.6667;A:-20.0000;A:-20.0000;A:-15.0000;A:-43.3333")]
        [InlineData("\\pxsa2;A\\P\\S1/2;\\PA", 0, 10, "h=93.3333;A:-23.3333;1:-45.9604;2:-60.0000;A:-90.0000")]
        [InlineData("\\pxsa2;A\\PA\\P\\S1/2;", 0, 10, "h=93.3333;A:-23.3333;A:-56.6667;1:-79.2937;2:-93.3333")]
        [InlineData("\\pxsa2;C\\PC\\PC", 0, 10, "h=93.3333;C:-23.3333;C:-56.6667;C:-90.0000")]
        [InlineData(
            "\\pxsa2;AA AA {\\H2x;AA} AA AA AA",
            45,
            10,
            "h=126.6667;AA AA:-23.3333;AA:-53.3333;AA AA:-90.0000;AA:-123.3333"
        )]
        [InlineData("\\pxsa2;\\pxb1,a2;A\\PA", 0, 10, "h=100.0000;A:-33.3333;A:-96.6667")]
        [InlineData("\\pxsa2;A{\\H2x;A}{\\A1;A}\\PA", 0, 10, "h=60.0000;A:-20.0000;A:-20.0000;A:-15.0000;A:-56.6667")]
        [InlineData("\\pxsa3;A\\P\\S1/2;\\PA", 0, 10, "h=140.0000;A:-36.6667;1:-75.9604;2:-90.0000;A:-136.6667")]
        [InlineData("\\pxsa3;A\\PA\\P\\S1/2;", 0, 10, "h=140.0000;A:-36.6667;A:-86.6667;1:-125.9604;2:-140.0000")]
        [InlineData("\\pxsa3;C\\PC\\PC", 0, 10, "h=140.0000;C:-36.6667;C:-86.6667;C:-136.6667")]
        [InlineData(
            "\\pxsa3;AA AA {\\H2x;AA} AA AA AA",
            45,
            10,
            "h=190.0000;AA AA:-36.6667;AA:-83.3333;AA AA:-136.6667;AA:-186.6667"
        )]
        [InlineData("\\pxsa3;\\pxb1,a2;A\\PA", 0, 10, "h=130.0000;A:-46.6667;A:-126.6667")]
        [InlineData("\\pxsa3;A{\\H2x;A}{\\A1;A}\\PA", 0, 10, "h=90.0000;A:-33.3333;A:-33.3333;A:-28.3333;A:-86.6667")]
        [InlineData("\\pxse2;A\\P\\S1/2;\\PA", 0, 10, "h=86.6667;A:-20.0000;1:-39.2937;2:-53.3333;A:-86.6667")]
        [InlineData("\\pxse2;A\\PA\\P\\S1/2;", 0, 10, "h=97.3730;A:-20.0000;A:-53.3333;1:-72.6270;2:-86.6667")]
        [InlineData("\\pxse2;C\\PC\\PC", 0, 10, "h=87.2966;C:-20.0000;C:-53.3333;C:-86.6667")]
        [InlineData(
            "\\pxse2;AA AA {\\H2x;AA} AA AA AA",
            45,
            10,
            "h=120.0000;AA AA:-20.0000;AA:-53.3333;AA AA:-86.6667;AA:-120.0000"
        )]
        [InlineData("\\pxse2;\\pxb1,a2;A\\PA", 0, 10, "h=93.3333;A:-30.0000;A:-93.3333")]
        [InlineData("\\pxse2;A{\\H2x;A}{\\A1;A}\\PA", 0, 10, "h=53.3333;A:-20.0000;A:-20.0000;A:-15.0000;A:-53.3333")]
        [InlineData("A{\\H2x;A}", 0, 10, "h=20.0000;A:-20.0000;A:-20.0000")]
        public void TheLinesOfAParagraphSetByACode_StandWhereAutoCADPutsThem(
            string contents,
            double width,
            double height,
            string expected
        )
        {
            Matches(Laid(contents, width, height), expected);
        }

        [Theory]
        [InlineData("\\pxsm1.5;A\\PB\\PA", 2, false, "h=63.3333;A:-10.0000;B:-35.0000;A:-60.0000")]
        [InlineData("\\pxsm2;A\\P{\\H2x;B}\\PA", 2, false, "h=113.3333;A:-10.0000;B:-53.3333;A:-110.0000")]
        [InlineData("\\pxsa1;A\\PB\\PA", 2, false, "h=46.6667;A:-10.0000;B:-26.6667;A:-43.3333")]
        [InlineData("\\pxsa1.5;A\\P{\\H2x;B}\\PA", 2, false, "h=78.3333;A:-16.6667;B:-45.0000;A:-75.0000")]
        [InlineData("\\pxse2;A\\PB\\PA", 2, false, "h=86.6667;A:-20.0000;B:-53.3333;A:-86.6667")]
        [InlineData("\\pxsm2;A\\PB\\PA", 0.5, false, "h=80.0000;A:-10.0000;B:-43.3333;A:-76.6667")]
        [InlineData("\\pxsm0.5;A\\P{\\H2x;B}\\PA", 0.5, false, "h=33.3333;A:-10.0000;B:-28.3333;A:-35.0000")]
        [InlineData("\\pxsa1.5;A\\PB\\PA", 0.5, false, "h=70.0000;A:-16.6667;B:-41.6667;A:-66.6667")]
        [InlineData("\\pxsa3;A\\P{\\H2x;B}\\PA", 0.5, false, "h=140.0000;A:-36.6667;B:-83.3333;A:-136.6667")]
        [InlineData("\\pxsm1.5;A\\P{\\H2x;B}\\PA", 1, true, "h=88.3333;A:-10.0000;B:-45.0000;A:-85.0000")]
        [InlineData("\\pxsm0.5;A\\PB\\PA", 1, true, "h=25.0000;A:-10.0000;B:-18.3333;A:-26.6667")]
        [InlineData("\\pxsa1;A\\P{\\H2x;B}\\PA", 1, true, "h=63.3333;A:-10.0000;B:-36.6667;A:-60.0000")]
        [InlineData("\\pxsa3;A\\PB\\PA", 1, true, "h=140.0000;A:-36.6667;B:-86.6667;A:-136.6667")]
        [InlineData("\\pxse2;A\\P{\\H2x;B}\\PA", 1, true, "h=86.6667;A:-20.0000;B:-53.3333;A:-86.6667")]
        [InlineData("\\pxsm1.5;A\\PB\\PA", 2, true, "h=63.3333;A:-10.0000;B:-35.0000;A:-60.0000")]
        [InlineData("\\pxsm2;A\\P{\\H2x;B}\\PA", 2, true, "h=113.3333;A:-10.0000;B:-53.3333;A:-110.0000")]
        [InlineData("\\pxsa1;A\\PB\\PA", 2, true, "h=46.6667;A:-10.0000;B:-26.6667;A:-43.3333")]
        [InlineData("\\pxsa1.5;A\\P{\\H2x;B}\\PA", 2, true, "h=78.3333;A:-16.6667;B:-45.0000;A:-75.0000")]
        [InlineData("\\pxse2;A\\PB\\PA", 2, true, "h=86.6667;A:-20.0000;B:-53.3333;A:-86.6667")]
        [InlineData("\\pxsm2;A\\PB\\PA", 0.5, true, "h=80.0000;A:-10.0000;B:-43.3333;A:-76.6667")]
        [InlineData("\\pxsm0.5;A\\P{\\H2x;B}\\PA", 0.5, true, "h=33.3333;A:-10.0000;B:-28.3333;A:-35.0000")]
        [InlineData("\\pxsa1.5;A\\PB\\PA", 0.5, true, "h=70.0000;A:-16.6667;B:-41.6667;A:-66.6667")]
        [InlineData("\\pxsa3;A\\P{\\H2x;B}\\PA", 0.5, true, "h=140.0000;A:-36.6667;B:-83.3333;A:-136.6667")]
        public void AParagraphCodeOfSpacing_IsNotScaledByTheMTextsOwnLineSpacingFactorOrStyle(
            string contents,
            double factor,
            bool exactly,
            string expected
        )
        {
            // The factor of group 44 and the style of group 73 are the MTEXT's own: where a paragraph says how it is spaced they
            // count for nothing, as the same codes give the same lines at a factor of 1, 0.5 and 2, at least and exactly.
            CadMTextLayout layout = Laid(contents, 0.0, 10.0, t =>
            {
                t.LineSpacingFactor = factor;
                t.LineSpacingStyle = exactly ? DxfLineSpacingStyle.Exactly : DxfLineSpacingStyle.AtLeast;
            });
            Matches(layout, expected);
        }
    }
}
