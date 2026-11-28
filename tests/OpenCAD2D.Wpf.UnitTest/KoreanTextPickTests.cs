using System;
using System.Linq;
using GeometryHelper.Dxf;
using GeometryHelper.Geometry;
using Xunit;

namespace OpenCAD2D.Wpf.UnitTest
{
    /// <summary>
    /// Korean words in a TrueType font whose file the core cannot read (Gulim and Batang are collections, .ttc) are picked,
    /// hovered and crossed anywhere they are drawn. The canvas draws each Hangul syllable a full em wide and a Latin letter
    /// half that; the pick box took every letter as 0.6 of the height, so that the right of a line was not a part of its
    /// text. They run here because the canvas's measure of letters is a setting of the whole process, which this suite does
    /// not run in parallel.
    /// </summary>
    public sealed class KoreanTextPickTests : IDisposable
    {
        private const string Family = "KrTestFace";

        // The stand-in measure sets a full-width letter one em wide, the em being 1 / 0.72 of the height as the canvas sets it, and
        // a Latin letter or a space half an em; a surrogate unit is half an em, so that a pair is one. These are not Gulim's own numbers.
        private const double Height = 5.0;
        private const string Line =
            "전체적으로 바뀐것은 새로 작성하지만 한두개 바뀐것은 기존성 파일에서 추가로 형성할수 있게 하기 위함";

        private readonly Func<string, bool, bool, string, double> _was = CadTrueTypeFonts.MeasureWidth;

        public KoreanTextPickTests()
        {
            CadTrueTypeFonts.MeasureWidth = (family, bold, italic, letters) => Em(letters);
        }

        public void Dispose() => CadTrueTypeFonts.MeasureWidth = _was;

        private static double Em(string letters) => letters.Sum(c => c >= 0xAC00 && c <= 0xD7A3 ? 1.0 : 0.5) / 0.72;

        private static double Drawn(string line) => Em(line) * Height;

        private static CadDocument Drawing(DxfEntity entity)
        {
            var database = new DxfDatabase("AC1027");
            var style = new DxfTextStyleTableRecord { Name = "KR" };
            style.Font = new DxfFontDescriptor(Family, false, false, 0, 34);
            database.TextStyleTable.Add(style);
            database.ModelSpace.AppendEntity(entity);

            if (entity is DxfDBText text)
            {
                text.TextStyleName = "KR";
            }
            else if (entity is DxfMText mtext)
            {
                mtext.TextStyleName = "KR";
            }

            CadDocument document = CadDocument.From(database);
            document.Fonts = new CadFonts(null, CadFonts.BuiltInName, null, f => f == Family);
            return document;
        }

        private static DxfMText MText(string contents, double width) =>
            new DxfMText
            {
                Contents = contents,
                Location = new GeoPoint3(0, 0, 0),
                TextHeight = Height,
                Width = width,
            };

        /// <summary>Whether a point, a crossing and a window at the far end of a line take the one entity.</summary>
        private static void AssertTakenAtTheRightEnd(CadDocument document, double right, double y)
        {
            DisplayList2 list = document.GetDisplay(document.ModelSpace);
            string handle = Assert.Single(list.Items.Select(i => i.Handle).Distinct());
            var index = new PickIndex(list);
            double near = right - 1.0;

            Assert.Equal(new[] { handle }, index.Pick(near, y, 0.5));
            Assert.Equal(new[] { handle }, index.Crossing(new DisplayBox(near - 0.5, y - 0.5, near + 0.5, y + 0.5)));
            Assert.Equal(new[] { handle }, index.Window(new DisplayBox(-10, -50, right + 10, 20)));
        }

        [Fact]
        public void AnMTextOfKoreanLines_WithNoWidth_IsPickedAtTheRightEndOfItsLongestLine()
        {
            CadDocument document = Drawing(MText(Line + @"\Pdrawing 파일에서 부재", 0.0));

            AssertTakenAtTheRightEnd(document, Drawn(Line), -Height / 2.0);
        }

        [Fact]
        public void AnMTextOfKoreanLines_WrappedAtItsWidth_IsPickedAtTheRightEndOfEachLine()
        {
            double width = 120.0;
            CadDocument document = Drawing(MText(Line, width));
            DisplayText2 whole = Assert.Single(document.GetDisplay(document.ModelSpace).Items.OfType<DisplayText2>());
            var lines = whole.Parts.OfType<DisplayText2>().ToArray();

            Assert.True(lines.Length > 1);

            foreach (DisplayText2 piece in lines)
            {
                Assert.Equal(piece.X + Drawn(piece.Text), piece.Bounds.MaxX, 3);
            }
        }

        [Fact]
        public void AKoreanTextWithATrueTypeStyle_IsPickedAtTheRightEndOfItsLine()
        {
            CadDocument document = Drawing(
                new DxfDBText
                {
                    TextString = Line,
                    Position = new GeoPoint3(0, 0, 0),
                    Height = Height,
                }
            );

            AssertTakenAtTheRightEnd(document, Drawn(Line), Height / 2.0);
        }

        private static DxfDBText Text(string words) =>
            new DxfDBText
            {
                TextString = words,
                Position = new GeoPoint3(0, 0, 0),
                Height = Height,
            };

        [Fact]
        public void TheExtentsOfAKoreanText_AreAsWideAsItIsDrawn()
        {
            CadDocument document = Drawing(Text(Line));

            DisplayBox extents = document.GetDisplay(document.ModelSpace).Bounds;

            Assert.Equal(Drawn(Line), extents.Width, 6);
        }

        [Fact]
        public void TheWidthTheMirrorTurnsAKoreanTextAbout_IsThatItIsDrawnWith()
        {
            DxfDBText text = Text(Line);
            CadDocument document = Drawing(text);

            Assert.Equal(Drawn(Line), document.TextWidth(text), 6);
        }

        [Fact]
        public void AKoreanTextInAReadableFont_IsAsWideAsTheCanvasDrawsIt()
        {
            CadTrueTypeFonts.MeasureWidth = SkiaCanvasRenderer.MeasureWidth;
            var database = new DxfDatabase("AC1027");
            var style = new DxfTextStyleTableRecord { Name = "KR" };
            style.Font = new DxfFontDescriptor("Malgun Gothic", false, false, 0, 34);
            database.TextStyleTable.Add(style);
            DxfDBText text = Text(Line);
            text.TextStyleName = "KR";
            database.ModelSpace.AppendEntity(text);
            CadDocument document = CadDocument.From(database);
            document.Fonts = new CadFonts(null, CadFonts.BuiltInName, null, SkiaCanvasRenderer.HasFamily);

            DisplayText2 shown = Assert.Single(document.GetDisplay(document.ModelSpace).Items.OfType<DisplayText2>());
            double canvas = SkiaCanvasRenderer.MeasureWidth("Malgun Gothic", false, false, Line) * Height;

            Assert.Equal(canvas, shown.Bounds.Width, canvas * 0.0005);
        }

        [Fact]
        public void AKoreanTextWithLettersBeyondThePlaneOfTheMark_IsPickedAtTheRightEndOfItsLine()
        {
            // Han letters of the extension B, each a surrogate pair of two units: a full em wide.
            string words = "\U00020000\U00020001\U00020002\U00020003\U00020004\U00020005";
            CadDocument document = Drawing(Text(words));

            AssertTakenAtTheRightEnd(document, Drawn(words), Height / 2.0);
        }

        [Fact]
        public void ALatinTextWithATrueTypeStyle_KeepsTheGuessOfItsBox()
        {
            CadDocument document = Drawing(
                new DxfDBText
                {
                    TextString = "ABCD",
                    Position = new GeoPoint3(0, 0, 0),
                    Height = Height,
                }
            );

            DisplayText2 shown = Assert.Single(document.GetDisplay(document.ModelSpace).Items.OfType<DisplayText2>());

            Assert.Equal(4 * 0.6 * Height, shown.Bounds.Width, 6);
        }
    }
}
