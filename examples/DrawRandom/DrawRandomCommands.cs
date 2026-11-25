using System;
using System.Collections.Generic;
using System.Globalization;
using GeometryHelper.Dxf;
using GeometryHelper.Geometry;
using OpenCAD2D;
using OpenCAD2D.Plugins;

[assembly: CadPluginApi("1.0")]

namespace DrawRandom
{
    /// <summary>
    /// The sample plugin that draws: CSRANDOM asks how many objects and where, then draws that many lines, circles, arcs,
    /// polylines, texts and rectangles of random size, each on one of five layers and in a random colour. It shows the
    /// prompts of a command, a layer made through the editor, and a run of edits that is one undo step.
    /// </summary>
    public sealed class DrawRandomCommands
    {
        private const int LayerCount = 5;
        private static readonly string[] Words = { "Beam", "Plate", "Bolt", "Stud", "Rib", "Cap", "Pin", "Nut" };

        /// <summary>
        /// Asks "Number of objects &lt;50&gt;:" (1 to 10000) and the two corners of the area, and draws the objects inside it.
        /// Enter at the first corner takes the view shown, or the extents of the drawing where there is no window. The layers
        /// RANDOM-1 to RANDOM-5 are made if missing, each with a random colour; a layer that is there is kept as it is. Every
        /// object is a LINE, CIRCLE, ARC, LWPOLYLINE of 3 to 6 vertices (closed or open), TEXT or rectangle, on a random one of
        /// the five layers, ByLayer half the time and else of a random colour from 1 to 255. The layers and the objects are one
        /// undo step. With 50 objects it ends with <c>Drew 50 objects on 5 layers.</c>. Set the environment variable
        /// <c>OPENCAD2D_RANDOM_SEED</c> to a whole number to draw the same objects every time.
        /// </summary>
        /// <param name="c">The command's context.</param>
        /// <returns>The prompts: the number, the first corner and the opposite corner.</returns>
        [CadCommand("CSRANDOM", Description = "Draw objects of random kinds on random layers in random colours")]
        public IEnumerable<CadPrompt> Draw(CadCommandContext c)
        {
            int count = 50;

            while (true)
            {
                yield return CadPrompt.ForInteger("Number of objects", null, "50");

                if (c.Input.Kind != CadInputKind.Number)
                {
                    break; // Enter: the 50 of the prompt.
                }

                if (c.Input.Number >= 1 && c.Input.Number <= 10000)
                {
                    count = (int)c.Input.Number;
                    break;
                }

                c.Write("Requires an integer between 1 and 10000.");
            }

            GeoPoint2 first;
            GeoPoint2 second;
            yield return CadPrompt.ForPoint("Specify first corner", null, null, "view");

            if (c.Input.IsPoint)
            {
                first = c.Input.Point;

                while (true)
                {
                    yield return CadPrompt.ForPoint("Specify opposite corner", first);
                    second = c.Input.Point;

                    // The area has to have a width and a height.
                    double gap = c.Session.Tolerance.EqualPoint;

                    if (Math.Abs(second.X - first.X) > gap && Math.Abs(second.Y - first.Y) > gap)
                    {
                        break;
                    }

                    c.Write("The two corners must differ in x and in y.");
                }
            }
            else
            {
                DisplayBox area = DefaultArea(c);
                first = new GeoPoint2(area.MinX, area.MinY);
                second = new GeoPoint2(area.MaxX, area.MaxY);
            }

            // The one place the random numbers come from.
            Random random = NewRandom();
            double minX = Math.Min(first.X, second.X);
            double minY = Math.Min(first.Y, second.Y);
            double width = Math.Abs(second.X - first.X);
            double height = Math.Abs(second.Y - first.Y);

            // The layers and the objects join the one undo step the command opens here.
            c.OpenUndoStep();

            for (int i = 1; i <= LayerCount; i++)
            {
                string name = "RANDOM-" + i.ToString(CultureInfo.InvariantCulture);

                if (!c.Database.LayerTable.Has(name))
                {
                    CadLayerEditor.Add(c.Document, name);
                    CadLayerEditor.SetColor(c.Document, name, random.Next(1, 256));
                }
            }

            c.Apply(null, scope =>
            {
                for (int i = 0; i < count; i++)
                {
                    // Each object lives in a small box of 3 to 15 % of the shorter side, which lies inside the area.
                    double side = Math.Min(width, height) * (0.03 + 0.12 * random.NextDouble());
                    double boxWidth = side * (0.5 + 0.5 * random.NextDouble());
                    double boxHeight = side * (0.5 + 0.5 * random.NextDouble());
                    double x = minX + (width - boxWidth) * random.NextDouble();
                    double y = minY + (height - boxHeight) * random.NextDouble();

                    DxfEntity entity = Make(random, x, y, boxWidth, boxHeight);
                    entity.Layer = "RANDOM-" + random.Next(1, LayerCount + 1).ToString(CultureInfo.InvariantCulture);

                    // ByLayer (256) is what an entity has until it is given a colour.
                    if (random.Next(2) == 0)
                    {
                        entity.ColorIndex = random.Next(1, 256);
                    }

                    scope.Append(c.Space, entity);
                }
            });

            c.Write(
                "Drew "
                    + count.ToString(CultureInfo.InvariantCulture)
                    + " objects on "
                    + LayerCount.ToString(CultureInfo.InvariantCulture)
                    + " layers."
            );
        }

        /// <summary>The area Enter takes: the view shown, or the extents of the drawing where the app has no window.</summary>
        private static DisplayBox DefaultArea(CadCommandContext c)
        {
            if (c.Editor.HasWindow)
            {
                return c.Editor.GetCurrentView();
            }

            // The extents are an empty box until something is drawn: then a square of 1000 stands in.
            GeoPoint3 least = c.Database.Extmin;
            GeoPoint3 greatest = c.Database.Extmax;
            double gap = c.Session.Tolerance.EqualPoint;
            return greatest.X - least.X > gap && greatest.Y - least.Y > gap
                ? new DisplayBox(least.X, least.Y, greatest.X, greatest.Y)
                : new DisplayBox(0, 0, 1000, 1000);
        }

        /// <summary>The random numbers of the command: the same every time where OPENCAD2D_RANDOM_SEED is a whole number.</summary>
        private static Random NewRandom() =>
            int.TryParse(
                Environment.GetEnvironmentVariable("OPENCAD2D_RANDOM_SEED"),
                NumberStyles.Integer,
                CultureInfo.InvariantCulture,
                out int seed
            )
                ? new Random(seed)
                : new Random();

        /// <summary>One object of a random kind that fills the box from (x, y), boxWidth wide and boxHeight high, at most.</summary>
        private static DxfEntity Make(Random random, double x, double y, double boxWidth, double boxHeight)
        {
            var centre = new GeoPoint3(x + boxWidth / 2.0, y + boxHeight / 2.0, 0);
            double radius = Math.Min(boxWidth, boxHeight) / 2.0;

            switch (random.Next(6))
            {
                case 0: // LINE: one of the two diagonals of the box.
                    return random.Next(2) == 0
                        ? new DxfLine(new GeoPoint3(x, y, 0), new GeoPoint3(x + boxWidth, y + boxHeight, 0))
                        : new DxfLine(new GeoPoint3(x, y + boxHeight, 0), new GeoPoint3(x + boxWidth, y, 0));

                case 1:
                    return new DxfCircle(centre, GeoVector3.ZAxis, radius);

                case 2: // ARC: from a random angle, counter-clockwise by 30 to 300 degrees (the angles are radians).
                    double start = 2.0 * Math.PI * random.NextDouble();
                    double sweep = Math.PI * (1.0 / 6.0 + 5.0 / 6.0 * random.NextDouble());
                    return new DxfArc(centre, radius, start, (start + sweep) % (2.0 * Math.PI));

                case 3: // LWPOLYLINE of 3 to 6 vertices in the box, closed or open.
                    var polyline = new DxfPolyline();
                    int vertices = random.Next(3, 7);

                    for (int i = 0; i < vertices; i++)
                    {
                        polyline.AddVertexAt(
                            i,
                            new GeoPoint2(x + boxWidth * random.NextDouble(), y + boxHeight * random.NextDouble()),
                            0,
                            0,
                            0
                        );
                    }

                    polyline.Closed = random.Next(2) == 0;
                    return polyline;

                case 4: // TEXT: a letter is at most 0.8 of its height wide and goes 0.3 of it below the baseline.
                    string word = Words[random.Next(Words.Length)];
                    double textHeight = Math.Min(boxHeight / 1.5, boxWidth / (0.8 * word.Length));
                    return new DxfDBText
                    {
                        TextString = word,
                        Height = textHeight,
                        Position = new GeoPoint3(x, y + 0.3 * textHeight, 0),
                    };

                default: // The rectangle: a closed polyline of four vertices.
                    var rectangle = new DxfPolyline { Closed = true };
                    rectangle.AddVertexAt(0, new GeoPoint2(x, y), 0, 0, 0);
                    rectangle.AddVertexAt(1, new GeoPoint2(x + boxWidth, y), 0, 0, 0);
                    rectangle.AddVertexAt(2, new GeoPoint2(x + boxWidth, y + boxHeight), 0, 0, 0);
                    rectangle.AddVertexAt(3, new GeoPoint2(x, y + boxHeight), 0, 0, 0);
                    return rectangle;
            }
        }
    }
}
