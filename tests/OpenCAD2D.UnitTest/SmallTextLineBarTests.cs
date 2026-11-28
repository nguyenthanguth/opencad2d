using System;
using System.Collections.Generic;
using System.Linq;
using GeometryHelper.Dxf;
using OpenCAD2D.Wpf;
using SkiaSharp;
using Xunit;

namespace OpenCAD2D.UnitTest
{
    /// <summary>
    /// A small text of several lines, zoomed out, is one bar for each line, each as wide as its own line and ragged on the
    /// right, as AutoCAD draws a paragraph at a sheet's extents, not one slab; turned, mirrored, stretched or justified, the
    /// bars follow the lines. Drawn with no window, pixels read back.
    /// </summary>
    public class SmallTextLineBarTests
    {
        private static readonly DisplayStyle White = new DisplayStyle(0xFFFFFF, 0.25, null);

        /// <summary>A canvas 200 by 150 at one pixel to the unit, the drawing's (0, 0) at its lower left corner.</summary>
        private static CadView PixelView()
        {
            var view = new CadView(200, 150);
            view.ZoomTo(new DisplayBox(0, 0, 200, 150), 0.0);
            return view;
        }

        /// <summary>A bar read back: the rows or columns it spans and how far it reaches across them.</summary>
        private readonly struct Bar
        {
            public Bar(int from, int to, int least, int most)
            {
                From = from;
                To = to;
                Least = least;
                Most = most;
            }

            public int From { get; }
            public int To { get; }
            public int Least { get; }
            public int Most { get; }
            public int Length => Most - Least + 1;
        }

        private static bool Lit(SKColor c) => c.Red > 20 || c.Green > 20 || c.Blue > 20;

        /// <summary>The runs of rows (or, with <paramref name="byColumns"/>, columns) holding a lit pixel, each with its reach across.</summary>
        private static List<Bar> Bars(SKBitmap bitmap, bool byColumns = false)
        {
            int along = byColumns ? bitmap.Width : bitmap.Height;
            int across = byColumns ? bitmap.Height : bitmap.Width;
            var bars = new List<Bar>();
            int from = -1,
                least = int.MaxValue,
                most = int.MinValue;

            for (int i = 0; i <= along; i++)
            {
                bool any = false;

                for (int j = 0; i < along && j < across; j++)
                {
                    if (Lit(byColumns ? bitmap.GetPixel(i, j) : bitmap.GetPixel(j, i)))
                    {
                        any = true;
                        least = Math.Min(least, j);
                        most = Math.Max(most, j);
                    }
                }

                if (any && from < 0)
                {
                    from = i;
                }
                else if (!any && from >= 0)
                {
                    bars.Add(new Bar(from, i - 1, least, most));
                    from = -1;
                    least = int.MaxValue;
                    most = int.MinValue;
                }
            }

            return bars;
        }

        private static SKBitmap Draw(CadDocument document, params DisplayItem2[] items)
        {
            var bitmap = new SKBitmap(200, 150, SKColorType.Rgba8888, SKAlphaType.Premul);

            using (var canvas = new SKCanvas(bitmap))
            using (var renderer = new SkiaCanvasRenderer())
            {
                if (items.Length == 0)
                {
                    renderer.Render(canvas, document, document.ModelSpace, PixelView(), new CanvasOptions());
                }
                else
                {
                    renderer.RenderItems(
                        canvas,
                        document,
                        document.ModelSpace,
                        PixelView(),
                        new CanvasOptions(),
                        items
                    );
                }
            }

            return bitmap;
        }

        private static CadDocument Empty() => CadDocument.From(TestDrawings.Drawing(null, null));

        private static DisplayText2 Words(
            string text,
            double x = 100,
            double y = 75,
            double height = 4,
            double rotation = 0,
            double widthFactor = 1,
            int horizontal = 0,
            int vertical = 0,
            bool backward = false
        ) =>
            new DisplayText2(
                text,
                x,
                y,
                height,
                rotation,
                widthFactor,
                horizontal,
                vertical,
                White,
                "T1",
                "0",
                "Arial",
                0.0,
                backward
            );

        private static SKBitmap DrawMText(string contents, double height = 4.0)
        {
            CadDocument document = CadDocument.From(
                TestDrawings.Drawing(
                    null,
                    null,
                    TestDrawings.Entity(
                        "MTEXT",
                        "0",
                        256,
                        "M1",
                        100,
                        "AcDbEntity",
                        100,
                        "AcDbMText",
                        10,
                        20.0,
                        20,
                        140.0,
                        30,
                        0.0,
                        40,
                        height,
                        41,
                        150.0,
                        71,
                        1,
                        1,
                        contents
                    )
                )
            );
            return Draw(document);
        }

        [Fact]
        public void AnMtextOfTenLinesAt4Pixels_IsTenBarsWithGapsAndRaggedWidths()
        {
            string[] lines =
            {
                "one",
                "a longer second line here",
                "short",
                "the fourth line is the longest of the ten lines",
                "five",
                "sixth line of text",
                "seven",
                "eighth line of the paragraph",
                "nine words in this line",
                "ten",
            };

            using (SKBitmap bitmap = DrawMText(string.Join("\\P", lines)))
            {
                List<Bar> bars = Bars(bitmap);

                Assert.Equal(10, bars.Count);
                Assert.True(
                    bars.Select(b => b.Length).Distinct().Count() >= 8,
                    "ragged: " + string.Join(",", bars.Select(b => b.Length))
                );

                for (int i = 1; i < bars.Count; i++)
                {
                    Assert.True(bars[i].From > bars[i - 1].To + 1, "a gap before bar " + i);
                }

                // Each as wide as its line: the longest line's bar is the widest and the shortest's the narrowest.
                Assert.Equal(bars.Max(b => b.Length), bars[3].Length);
                Assert.Equal(bars.Min(b => b.Length), Math.Min(bars[0].Length, bars[9].Length));
            }
        }

        [Fact]
        public void AnMtextOfSeveralLooks_IsABarForEachLineToo()
        {
            using (
                SKBitmap bitmap = DrawMText(
                    "{\\C1;red words here}\\P{\\C3;green}\\Pplain words over a longer line\\P{\\C1;red}"
                )
            )
            {
                List<Bar> bars = Bars(bitmap);

                Assert.Equal(4, bars.Count);
                Assert.True(bars.Select(b => b.Length).Distinct().Count() >= 3);
            }
        }

        [Fact]
        public void AnMtextThatWraps_IsSeveralRaggedBarsNotOneSlab()
        {
            using (
                SKBitmap bitmap = DrawMText(
                    "this paragraph is long enough that it wraps around the width of its box over and over again until it has "
                        + "run to many lines of text of different lengths one after the other down the sheet"
                )
            )
            {
                // The box is 150 wide, so the words wrap there; the line breaks are where the layout puts them.
                List<Bar> bars = Bars(bitmap);

                Assert.True(bars.Count >= 3, "bars: " + bars.Count);
                Assert.True(bars.Select(b => b.Length).Distinct().Count() >= 2);
            }
        }

        [Fact]
        public void ATextOfThreeLines_IsThreeBarsOfWidthsInProportionToTheirLetters()
        {
            using (SKBitmap bitmap = Draw(Empty(), Words("abcde\nabcdefg\na")))
            {
                List<Bar> bars = Bars(bitmap);

                // 0.6 of the height a letter: 7 letters at 4 is 16.8 across, 5 is 12, 1 is 2.4 (kept to a pixel at least).
                Assert.Equal(3, bars.Count);
                Assert.InRange(bars[0].Length, 11, 14);
                Assert.InRange(bars[1].Length, 16, 19);
                Assert.InRange(bars[2].Length, 2, 4);
                Assert.Equal(bars[0].Least, bars[1].Least);
                Assert.True(bars[1].From > bars[0].To + 1, "a gap between the lines");
            }
        }

        [Fact]
        public void ATurnedText_HasItsBarsAlongTheTurn()
        {
            // Turned a quarter, its lines are columns, each as long as its line.
            using (SKBitmap bitmap = Draw(Empty(), Words("abcde\nabcdefg\na", rotation: Math.PI / 2)))
            {
                List<Bar> columns = Bars(bitmap, byColumns: true);

                Assert.Equal(3, columns.Count);
                Assert.InRange(columns[1].Length, 16, 19);
                Assert.InRange(columns[2].Length, 2, 4);
                Assert.InRange(columns[0].Length, 11, 14);
            }
        }

        [Fact]
        public void AMirroredText_RunsItsBarsTheOtherWayFromItsPoint()
        {
            using (SKBitmap forward = Draw(Empty(), Words("abcde\nabcdefg")))
            using (SKBitmap mirrored = Draw(Empty(), Words("abcde\nabcdefg", backward: true)))
            {
                List<Bar> a = Bars(forward);
                List<Bar> b = Bars(mirrored);

                Assert.Equal(2, a.Count);
                Assert.Equal(2, b.Count);
                Assert.InRange(a[0].Least, 99, 101);
                Assert.InRange(b[0].Most, 99, 101);
                Assert.Equal(a[0].Length, b[0].Length);
                Assert.Equal(a[1].Length, b[1].Length);
            }
        }

        [Fact]
        public void AWiderLetterWidth_MakesWiderBars_AndACentredTextCentresEachBar()
        {
            using (SKBitmap one = Draw(Empty(), Words("abcde\nabc")))
            using (SKBitmap twice = Draw(Empty(), Words("abcde\nabc", widthFactor: 2)))
            using (SKBitmap centred = Draw(Empty(), Words("abcde\nabc", horizontal: 1)))
            {
                List<Bar> a = Bars(one);
                List<Bar> b = Bars(twice);
                List<Bar> c = Bars(centred);

                Assert.InRange(b[0].Length, 2 * a[0].Length - 2, 2 * a[0].Length + 2);
                Assert.InRange(b[1].Length, 2 * a[1].Length - 2, 2 * a[1].Length + 2);

                // Each line's bar about the point, the shorter one inside the longer.
                Assert.InRange((c[0].Least + c[0].Most) / 2, 98, 102);
                Assert.InRange((c[1].Least + c[1].Most) / 2, 98, 102);
                Assert.True(c[1].Least > c[0].Least);
            }
        }

        [Fact]
        public void AJustifiedText_PutsItsBarsAboveOrBelowItsPoint()
        {
            // The point is (100, 75), the canvas row 75.
            using (SKBitmap top = Draw(Empty(), Words("abc\nabc\nabc", vertical: 3)))
            using (SKBitmap bottom = Draw(Empty(), Words("abc\nabc\nabc", vertical: 1)))
            {
                List<Bar> t = Bars(top);
                List<Bar> b = Bars(bottom);

                Assert.Equal(3, t.Count);
                Assert.Equal(3, b.Count);
                Assert.InRange(t[0].From, 74, 76);
                Assert.InRange(b[2].To, 74, 76);
            }
        }

        private static SKBitmap DrawText(string value, double height) =>
            Draw(
                CadDocument.From(
                    TestDrawings.Drawing(null, null, TestDrawings.Text(100, 75, height, value, handle: "T1"))
                )
            );

        [Fact]
        public void AnAbsurdlySmallTextOfSpaces_IsNotEvenADot()
        {
            // 0.1 high and under half a pixel across, where an item is one dot; but spaces show nothing, and a word does.
            using (SKBitmap spaces = DrawText("   ", 0.1))
            using (SKBitmap mtextSpaces = DrawMText("   ", 0.1))
            using (SKBitmap word = DrawText("a", 0.1))
            using (SKBitmap mtextWord = DrawMText("a", 0.1))
            {
                Assert.Empty(Bars(spaces));
                Assert.Empty(Bars(mtextSpaces));
                Assert.Single(Bars(word));
                Assert.Single(Bars(mtextWord));
            }
        }
    }
}
