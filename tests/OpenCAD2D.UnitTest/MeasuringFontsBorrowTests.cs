using System.Linq;
using GeometryHelper.Dxf;
using GeometryHelper.Geometry;
using Xunit;

namespace OpenCAD2D.UnitTest
{
    /// <summary>
    /// The fonts a measuring scope borrows (<see cref="DxfDatabase.BorrowFonts(string)"/>) are read once for a path and kept for
    /// the life of the drawing, so that a font file added to a folder the drawing already measured in is seen again only once
    /// the document's <see cref="CadDocument.Fonts"/> are set, as Apply in Global settings sets them.
    /// </summary>
    public sealed class MeasuringFontsBorrowTests : System.IDisposable
    {
        private readonly FontFolder _folder = new FontFolder();

        public void Dispose() => _folder.Dispose();

        /// <summary>A drawing of one TEXT "IOI" centred on the origin in the style font test.shx, whose folder has no such font yet.</summary>
        private CadDocument WithoutTheFont(FontFolder folder, out string handle)
        {
            var document = CadDocument.From(new DxfDatabase("AC1027"), "borrow.dxf");
            document.Database.TextStyleTable["Standard"].FileName = "test.shx";
            var text = new DxfDBText
            {
                TextString = "IOI",
                Height = 4.0,
                Position = new GeoPoint3(0, 0, 0),
                HorizontalMode = DxfTextHorizontalMode.TextCenter,
                AlignmentPoint = new GeoPoint3(0, 0, 0),
            };
            document.Database.ModelSpace.AppendEntity(text);
            handle = text.Handle;
            document.Fonts = new CadFonts(new[] { folder.FullPath }, null, null);
            return document;
        }

        /// <summary>The width of the box (textbox) gives the text, as the LISP function measures it.</summary>
        private static string Width(CadCommandSession session, string handle)
        {
            session.Feed("(setq tb (textbox (entget (handent \"" + handle + "\"))))");
            int before = session.History.Count;
            session.Feed("(rtos (- (car (cadr tb)) (car (car tb))) 2 6)");
            return session.History.Skip(before).Last(l => l.Trim().Length > 0).Trim();
        }

        [Fact]
        public void AFontAddedToTheFolder_IsMeasuredInOnceTheDocumentsFontsAreSetAgain()
        {
            CadDocument document = WithoutTheFont(_folder, out string handle);
            CadCommandSession session = EditingDrawings.Session(document);
            string without = Width(session, handle);

            _folder.Write("test.shx", ShxFiles.Unifont());
            document.Fonts = new CadFonts(new[] { _folder.FullPath }, null, null);
            string with = Width(session, handle);

            using (var other = new FontFolder())
            {
                other.Write("test.shx", ShxFiles.Unifont());
                CadDocument reference = WithoutTheFont(other, out string same);
                Assert.Equal(Width(EditingDrawings.Session(reference), same), with);
            }

            Assert.NotEqual(without, with);
        }

        [Fact]
        public void MeasuringTwice_InTheSameFolder_GivesTheSameBoxAndLeavesTheDrawingsPathEmpty()
        {
            _folder.Write("test.shx", ShxFiles.Unifont());
            CadDocument document = WithoutTheFont(_folder, out string handle);
            CadCommandSession session = EditingDrawings.Session(document);

            Assert.Equal(Width(session, handle), Width(session, handle));
            Assert.Equal(string.Empty, document.Database.FontSearchPath);
        }
    }
}
