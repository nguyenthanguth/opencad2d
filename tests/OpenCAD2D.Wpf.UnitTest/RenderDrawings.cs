using System;
using System.IO;
using System.Linq;
using GeometryHelper.Dxf;
using GeometryHelper.Geometry;
using SkiaSharp;

namespace OpenCAD2D.Wpf.UnitTest
{
    /// <summary>
    /// The ten drawings of the headless render's tests, built in code (by the commands a person would type, then saved as a
    /// DXF file the app and the facade both open): lines and curves in every kind of colour, wide polylines and solids,
    /// hatches, TrueType and SHX text, dimensions, blocks, linetypes and lineweights, a raster image, and a layout with two
    /// viewports.
    /// </summary>
    internal static class RenderDrawings
    {
        /// <summary>How many drawings there are.</summary>
        public const int Count = 10;

        /// <summary>The name of the layout of drawing 10.</summary>
        public const string LayoutName = "Layout1";

        /// <summary>
        /// The commands of each drawing, parted by semicolons as <c>--run</c> parts them, an empty part being Enter. Drawing 9
        /// holds {0} where its picture's path goes.
        /// </summary>
        private static readonly string[] Scripts =
        {
            // 1: lines, arcs, circles, ellipses, splines, colours of the index and true colours.
            "CECOLOR;7;LINE;0,0;200,100;300,0;;CECOLOR;1;ARC;300,0;350,50;400,0;CECOLOR;3;CIRCLE;100,200;40;CECOLOR;5;ELLIPSE;0,300;100,300;30;CECOLOR;6;SPLINE;200,300;250,350;300,280;350,340;;;;CECOLOR;200;LINE;0,400;400,420;;CECOLOR;12,200,100;CIRCLE;300,200;25;CECOLOR;250;LINE;0,-30;400,-30;;",
            // 2: polylines with widths and bulges, solids, traces and wipeouts.
            "CECOLOR;2;PLINE;0,0;W;4;4;100,0;W;10;0;150,50;A;200,0;L;W;0;0;250,0;;CECOLOR;4;SOLID;0,100;100,100;0,150;100,150;;;CECOLOR;1;TRACE;5;0,200;100,200;100,230;;;CECOLOR;3;PLINE;150,100;250,100;250,160;150,160;C;WIPEOUT;180,120;220,120;220,140;180,140;;",
            // 3: hatches, solid, ANSI31 at three scales (the finest is drawn as a tint) and a pattern turned.
            "CECOLOR;7;RECTANG;0,0;100,100;RECTANG;150,0;250,100;RECTANG;300,0;400,100;RECTANG;0,150;100,250;CECOLOR;1;-HATCH;P;SOLID;50,50;;CECOLOR;3;-HATCH;P;ANSI31;1;0;200,50;;CECOLOR;5;-HATCH;P;ANSI31;0.05;0;350,50;;CECOLOR;4;-HATCH;P;ANSI37;3;45;50,200;;",
            // 4: TrueType text in four looks and an MTEXT that wraps, its runs measured.
            "CECOLOR;7;-STYLE;AR;arial;0;1;0;N;N;N;-STYLE;ARB;arialbd;0;1;0;N;N;N;-STYLE;ARI;ariali;0;1;0;N;N;N;TEXT;S;AR;0,0;20;0;Arial text;;TEXT;S;ARB;0,40;20;0;Bold text;;TEXT;S;ARI;0,80;20;0;Italic text;;MTEXT;0,200;H;15;@300,-100;First line \\Punder \\Lunderlined\\l and more words that wrap around the box width;;;",
            // 5: SHX text in the shipped font and a style naming a SHX that is not there.
            "CECOLOR;7;-STYLE;SH;opencad2d;0;1;0;N;N;N;-STYLE;MS;nosuchfont;0;1;0;N;N;N;TEXT;S;SH;0,0;20;0;SHX style text 123;;TEXT;S;MS;0,40;20;0;Missing shx text;;",
            // 6: dimensions of five kinds on the default style.
            "CECOLOR;7;LINE;0,0;200,0;200,100;;CIRCLE;50,150;30;LINE;300,0;400,60;;DIMLINEAR;0,0;200,0;100,-40;DIMLINEAR;200,0;200,100;260,50;DIMALIGNED;300,0;400,60;380,10;DIMRADIUS;50,180;80,200;DIMDIAMETER;50,120;100,150;DIMANGULAR;;300,0;400,60;200,0;;350,-50;",
            // 7: blocks, nested and turned inserts, an attribute.
            "CECOLOR;7;CIRCLE;0,0;10;LINE;-15,0;15,0;;LINE;0,-15;0,15;;-BLOCK;B1;0,0;ALL;;-INSERT;B1;100,0;1;1;0;-INSERT;B1;100,100;2;2;30;-BLOCK;B2;0,0;ALL;;-INSERT;B2;300,0;1;1;0;-ATTDEF;;TAG;Prompt;Def;200,200;10;0;",
            // 8: linetypes from the shipped file and lineweights.
            "CECOLOR;7;-LINETYPE;L;DASHED;;-LINETYPE;L;CENTER;;LWDISPLAY;1;CELTYPE;DASHED;CELWEIGHT;50;LINE;0,0;300,0;;CELTYPE;CENTER;CELWEIGHT;100;CIRCLE;150,100;60;CELTYPE;ByLayer;CELWEIGHT;30;LINE;0,200;300,200;;CECOLOR;1;PLINE;0,250;300,250;;",
            // 9: a raster image, a circle, a text under 3 pixels high (a bar, a pixel of its colour) and one of 10.
            "CECOLOR;7;-IMAGE;A;{0};0,0;100;0;CIRCLE;500,50;30;TEXT;0,300;0.05;0;tiny;;TEXT;0,350;10;0;Big text;;",
        };

        /// <summary>The space drawing <paramref name="number"/> is shown in: model space, or the layout of drawing 10.</summary>
        public static string Space(int number) => number == 10 ? LayoutName : null;

        /// <summary>Builds drawing <paramref name="number"/> (1 to 10) as a DXF file in a folder and gives its path.</summary>
        public static string Build(int number, string folder)
        {
            Directory.CreateDirectory(folder);
            string path = Path.Combine(folder, "drawing" + number + ".dxf");

            if (number == 10)
            {
                WithLayout(path);
                return path;
            }

            CadDocument document = CadDocument.From(new DxfDatabase("AC1027"), "drawing" + number + ".dxf");
            var session = new CadCommandSession(document);
            string script = Scripts[number - 1];

            if (number == 9)
            {
                script = script.Replace("{0}", Picture(folder).Replace('\\', '/'));
            }

            Run(session, script);

            if (number == 8)
            {
                document.Database.LineWeightDisplay = true;
            }

            if (number == 9)
            {
                DxfRasterImage image = document.Database.ModelSpace.OfType<DxfRasterImage>().Single();
                image.SetClipBoundary(
                    DxfClipBoundaryType.Poly,
                    new[]
                    {
                        new GeoPoint2(0.5, 0.5),
                        new GeoPoint2(7.5, 0.5),
                        new GeoPoint2(4.0, 7.5),
                        new GeoPoint2(0.5, 0.5),
                    }
                );
            }

            document.SaveAs(path);
            return path;
        }

        /// <summary>Types commands into a session: a part is a line typed, an empty part is Enter.</summary>
        public static void Run(CadCommandSession session, string script)
        {
            foreach (string part in script.Split(';'))
            {
                if (part.Length == 0)
                {
                    session.Enter();
                }
                else
                {
                    session.Feed(part);
                }
            }
        }

        /// <summary>
        /// Writes a picture of 8 by 8 pixels, red and green in squares of 2, as a PNG in the folder and gives its path: the
        /// picture of drawing 9.
        /// </summary>
        private static string Picture(string folder)
        {
            string path = Path.Combine(folder, "picture.png");

            using (var bitmap = new SKBitmap(8, 8))
            {
                for (int y = 0; y < 8; y++)
                {
                    for (int x = 0; x < 8; x++)
                    {
                        bitmap.SetPixel(
                            x,
                            y,
                            ((x / 2) + (y / 2)) % 2 == 0 ? new SKColor(220, 30, 30) : new SKColor(30, 200, 60)
                        );
                    }
                }

                using (SKImage image = SKImage.FromBitmap(bitmap))
                using (SKData data = image.Encode(SKEncodedImageFormat.Png, 100))
                {
                    File.WriteAllBytes(path, data.ToArray());
                }
            }

            return path;
        }

        /// <summary>
        /// Drawing 10: a model space of four walls and two circles, and Layout1 of A3 with two viewports on it, the second
        /// turned by half a radian and clipped by a polygon of five corners.
        /// </summary>
        private static void WithLayout(string path)
        {
            var database = new DxfDatabase("AC1027");
            database.LayerTable.Add(new DxfLayerTableRecord { Name = "WALLS", ColorIndex = 3 });
            double[,] walls =
            {
                { 0, 0, 4000, 0 },
                { 0, 3000, 4000, 3000 },
                { 0, 0, 0, 3000 },
                { 4000, 0, 4000, 3000 },
            };

            for (int i = 0; i < 4; i++)
            {
                database.ModelSpace.AppendEntity(
                    new DxfLine(new GeoPoint3(walls[i, 0], walls[i, 1], 0), new GeoPoint3(walls[i, 2], walls[i, 3], 0))
                    {
                        Layer = "WALLS",
                    }
                );
            }

            database.ModelSpace.AppendEntity(
                new DxfCircle(new GeoPoint3(2000, 1500, 0), new GeoVector3(0, 0, 1), 600) { ColorIndex = 1 }
            );
            database.ModelSpace.AppendEntity(
                new DxfCircle(new GeoPoint3(1000, 700, 0), new GeoVector3(0, 0, 1), 200) { ColorIndex = 5 }
            );
            DxfLayout layout = database.Layouts.Single(l => l.LayoutName == LayoutName);
            layout.Initialize();
            var one = new DxfViewport
            {
                CenterPoint = new GeoPoint3(80, 100, 0),
                Width = 120,
                Height = 150,
                ViewHeight = 150 * 25.0,
                ViewCenter = new GeoPoint2(2000, 1500),
            };
            layout.BlockTableRecord.AppendEntity(one);
            one.On = true;
            var two = new DxfViewport
            {
                CenterPoint = new GeoPoint3(220, 100, 0),
                Width = 120,
                Height = 150,
                ViewHeight = 150 * 20.0,
                ViewCenter = new GeoPoint2(1000, 700),
                TwistAngle = 0.5,
            };
            layout.BlockTableRecord.AppendEntity(two);
            two.On = true;
            var shape = new DxfPolyline();
            double[] corners = { 170, 40, 270, 40, 270, 100, 240, 160, 170, 160 };

            for (int i = 0; i < corners.Length / 2; i++)
            {
                shape.AddVertexAt(i, new GeoPoint2(corners[2 * i], corners[2 * i + 1]), 0.0, 0.0, 0.0);
            }

            shape.Closed = true;
            layout.BlockTableRecord.AppendEntity(shape);
            two.ClipTo(shape);
            database.Save(path);
        }
    }
}
