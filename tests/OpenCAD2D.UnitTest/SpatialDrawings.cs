using System;
using System.Linq;
using GeometryHelper.Dxf;
using GeometryHelper.Geometry;

namespace OpenCAD2D.UnitTest
{
    /// <summary>
    /// Drawings for the spatial tests whose words are drawn in an SHX font of the test's own (letters S, I, D, E, F, O and the
    /// space, 10 high), so that their frames are the display's own and not a measure of a font of the machine.
    /// </summary>
    internal sealed class SpatialDrawings : IDisposable
    {
        private readonly FontFolder _folder = new FontFolder();

        public void Dispose() => _folder.Dispose();

        /// <summary>A drawing with no entities whose style OC_SHX is the test font.</summary>
        public CadDocument Blank()
        {
            _folder.Write("test.shx", ShxFiles.Unifont());
            var database = new DxfDatabase("AC1027");
            database.TextStyleTable.Add(new DxfTextStyleTableRecord { Name = "OC_SHX", FileName = "test.shx" });
            CadDocument document = CadDocument.From(database, "spatial.dxf");
            document.Fonts = new CadFonts(new[] { _folder.FullPath }, null, null, f => f == "Arial");
            return document;
        }

        /// <summary>Adds an entity to model space, a text in the test font. Everything is added before the display is first asked for.</summary>
        public static T Put<T>(CadDocument document, T entity)
            where T : DxfEntity
        {
            document.Database.ModelSpace.AppendEntity(entity);

            if (entity is DxfDBText text)
            {
                text.TextStyleName = "OC_SHX";
            }
            else if (entity is DxfMText mtext)
            {
                mtext.TextStyleName = "OC_SHX";
            }

            return entity;
        }

        /// <summary>A TEXT set at a point, at an angle in degrees.</summary>
        public static DxfDBText Text(string words, double x, double y, double degrees = 0.0, double height = 5.0) =>
            new DxfDBText
            {
                TextString = words,
                Position = new GeoPoint3(x, y, 0),
                Height = height,
                Rotation = degrees * Math.PI / 180.0,
            };

        /// <summary>A LINE.</summary>
        public static DxfLine Line(double x1, double y1, double x2, double y2) =>
            new DxfLine(new GeoPoint3(x1, y1, 0), new GeoPoint3(x2, y2, 0));

        /// <summary>A CIRCLE.</summary>
        public static DxfCircle Circle(double x, double y, double radius) =>
            new DxfCircle(new GeoPoint3(x, y, 0), GeoVector3.ZAxis, radius);

        /// <summary>The frame the display gives a text of these words, found in a drawing of that text alone.</summary>
        public CadTextFrame FrameOf(string words, double x, double y, double degrees = 0.0, double height = 5.0)
        {
            CadDocument document = Blank();
            DxfDBText text = Put(document, Text(words, x, y, degrees, height));
            return CadTextFrame.Of(
                document
                    .GetDisplay(document.ModelSpace)
                    .Items.OfType<DisplayText2>()
                    .Single(t => t.Handle == text.Handle)
            );
        }
    }
}
