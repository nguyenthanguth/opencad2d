using System;
using System.Collections.Generic;
using System.Linq;
using GeometryHelper;
using GeometryHelper.Dxf;
using GeometryHelper.Geometry;

namespace OpenCAD2D
{
    /// <summary>
    /// The sample dimensions of the style previews with their parts told apart: each sample dimension is made by the builders
    /// the DIM commands use, with their entities tagged as they are made (see <see cref="Tag{T}(T, CadStylePart)"/>), and the
    /// entities of its block copied out into model space, each with a handle of its own, so that the display list names the part
    /// each item belongs to.
    /// </summary>
    internal static partial class CadCommands
    {
        /// <summary>
        /// Draws one sample of a dimension style into a drawing of its own, in the style <paramref name="style"/> of that drawing,
        /// as <see cref="CadDimStyleEditor.Sample"/> says.
        /// </summary>
        internal static CadStyleSample StyleSample(
            DxfDatabase database,
            DxfDimStyleTableRecord style,
            CadDimSampleKind kind
        )
        {
            var builder = new SampleBuilder(database, style);
            bool was = _tagging;
            _tagging = true;

            try
            {
                builder.Draw(kind);
            }
            finally
            {
                _tagging = was;
            }

            return builder.Finish(kind);
        }

        /// <summary>Draws the parts and the dimensions of one sample, and keeps which entities of its drawing are which part.</summary>
        private sealed class SampleBuilder
        {
            private readonly DxfDatabase _database;
            private readonly DxfDimStyleTableRecord _style;
            private readonly double _k;
            private readonly List<SampleShape> _shapes = new List<SampleShape>();
            private readonly List<string> _untagged = new List<string>();
            private readonly List<PartEntry> _parts = new List<PartEntry>();
            private readonly Dictionary<int, PartEntry> _entries = new Dictionary<int, PartEntry>();

            internal SampleBuilder(DxfDatabase database, DxfDimStyleTableRecord style)
            {
                _database = database;
                _style = style;
                _k = style.Dimscale > 0.0 ? style.Dimscale : 1.0;
            }

            internal CadStyleSample Finish(CadDimSampleKind kind) =>
                new CadStyleSample(
                    _database,
                    kind,
                    _parts.ConvertAll(p => new CadStyleSamplePart(p.Part, p.Dimension, p.Handles)),
                    _shapes,
                    _untagged
                );

            internal void Draw(CadDimSampleKind kind)
            {
                switch (kind)
                {
                    case CadDimSampleKind.Lines:
                        Lines();
                        break;
                    case CadDimSampleKind.SymbolsAndArrows:
                        Symbols();
                        break;
                    case CadDimSampleKind.Text:
                        TextTab();
                        break;
                    case CadDimSampleKind.Fit:
                        Fit();
                        break;
                    case CadDimSampleKind.PrimaryUnits:
                    case CadDimSampleKind.AlternateUnits:
                    case CadDimSampleKind.Tolerances:
                        Units();
                        break;
                    default:
                        Overview();
                        break;
                }
            }

            /// <summary>A point of the sample, its coordinates in millimetres at DIMSCALE 1 made as many times bigger as the scale says.</summary>
            private GeoPoint2 P(double x, double y) => new GeoPoint2(x * _k, y * _k);

            /// <summary>Today's sample, as <see cref="DrawSamples"/> draws it: the part with a linear 32 across its top, a circle of 12 with its radius, an angle of 60.</summary>
            private void Overview()
            {
                Part(0, 0, 32, 20);
                var hole = P(56, 10);
                Outline(new DxfCircle(P3(hole), new GeoVector3(0, 0, 1), 12.0 * _k));
                Outline(new DxfLine(P3(P(0, -40)), P3(P(36, -40))));
                Outline(new DxfLine(P3(P(0, -40)), P3(P(18, -40 + 31.176914536239792))));
                Linear(0.0, P(0, 20), P(32, 20), P(16, 32));
                Radial(hole, 12.0, 8.0);
                Angular(P(0, -40), P(36, -40), P(18, -40 + 31.176914536239792), P(24, -26));
            }

            /// <summary>The Lines tab: the part with a baseline pair across its top, 20 and 32 long, DIMDLI apart.</summary>
            private void Lines()
            {
                Part(0, 0, 32, 20);
                Linear(0.0, P(0, 20), P(20, 20), P(10, 32));
                Linear(0.0, P(0, 20), P(32, 20), new GeoPoint2(16 * _k, (32 + _style.Dimdli) * _k));
            }

            /// <summary>The Symbols and arrows tab: a linear 32, a circle of 10 with radius and centre mark, an arc length, a jogged radius, a leader.</summary>
            private void Symbols()
            {
                Part(0, 0, 32, 20);
                var hole = P(50, 10);
                Outline(new DxfCircle(P3(hole), new GeoVector3(0, 0, 1), 10.0 * _k));
                Linear(0.0, P(0, 20), P(32, 20), P(16, 32));

                // The line through the circle (DIMTOFL) takes the centre mark away: the sample's radius is drawn without it, so that
                // the mark of DIMCEN shows.
                bool through = _style.Dimtofl;
                _style.Dimtofl = false;

                try
                {
                    Radial(hole, 10.0, 8.0);
                }
                finally
                {
                    _style.Dimtofl = through;
                }

                ArcLength(P(12, -22), 8.0, 11.0);
                Jogged(P(90, -14), 30.0, P(78, -18));
                Leader(
                    hole + new GeoVector2(Math.Cos(5.0 * Math.PI / 4.0), Math.Sin(5.0 * Math.PI / 4.0)) * (10.0 * _k)
                );
            }

            /// <summary>The Text tab: the part with a linear 32 across its top and a vertical 20 on its left.</summary>
            private void TextTab()
            {
                Part(0, 0, 32, 20);
                Linear(0.0, P(0, 20), P(32, 20), P(16, 32));
                Linear(Math.PI / 2.0, P(0, 0), P(0, 20), P(-12, 10));
            }

            /// <summary>The Fit tab: a wide dimension, a narrow one over a slot, and one whose words were moved by hand to the right of its line.</summary>
            private void Fit()
            {
                Part(0, 0, 32, 20);
                Part(44, 0, 49, 20);
                Linear(0.0, P(0, 20), P(32, 20), P(16, 32));
                Linear(0.0, P(44, 20), P(49, 20), P(46.5, 32));
                Linear(0.0, P(0, 0), P(32, 0), P(16, -8), P(40, -8));
            }

            /// <summary>The units tabs: a linear 28.375 over a part that long, and an angle of 37.5 degrees.</summary>
            private void Units()
            {
                Part(0, 0, 28.375, 20);
                double radians = 37.5 * Math.PI / 180.0;
                GeoPoint2 vertex = P(40, 0);
                GeoPoint2 first = vertex + new GeoVector2(30.0 * _k, 0.0);
                GeoPoint2 second = vertex + new GeoVector2(Math.Cos(radians), Math.Sin(radians)) * (30.0 * _k);
                Outline(new DxfLine(P3(vertex), P3(first)));
                Outline(new DxfLine(P3(vertex), P3(second)));
                Linear(0.0, P(0, 20), P(28.375, 20), P(14.1875, 32));
                Angular(
                    vertex,
                    first,
                    second,
                    vertex + new GeoVector2(Math.Cos(radians / 2.0), Math.Sin(radians / 2.0)) * (22.0 * _k)
                );
            }

            /// <summary>The four sides of a rectangle of the sample, in millimetres at DIMSCALE 1.</summary>
            private void Part(double x0, double y0, double x1, double y1)
            {
                GeoPoint2[] corners = { P(x0, y0), P(x1, y0), P(x1, y1), P(x0, y1) };

                for (int i = 0; i < corners.Length; i++)
                {
                    Outline(new DxfLine(P3(corners[i]), P3(corners[(i + 1) % corners.Length])));
                }
            }

            private void Outline(DxfEntity entity) => _database.ModelSpace.AppendEntity(entity);

            /// <summary>A linear dimension of two origins with its line through a point, its words moved by hand to a point where one is given.</summary>
            private void Linear(double rotation, GeoPoint2 a, GeoPoint2 b, GeoPoint2 on, GeoPoint2? placed = null)
            {
                var along = new GeoVector2(Math.Cos(rotation), Math.Sin(rotation));
                var dimension = new DxfRotatedDimension(rotation, P3(a), P3(b), P3(on), string.Empty, null);

                if (placed.HasValue)
                {
                    dimension.TextPosition = P3(placed.Value);
                    dimension.UsingDefaultTextPosition = false;
                }

                Add(
                    dimension,
                    new SampleShape
                    {
                        Kind = SampleShapeKind.Linear,
                        P1 = a,
                        P2 = b,
                        Q1 = Foot(a, on, along, 0.0),
                        Q2 = Foot(b, on, along, 0.0),
                    }
                );
            }

            /// <summary>The radius of a circle at 45 degrees, its words put out by <paramref name="leader"/> past the rim (millimetres at DIMSCALE 1).</summary>
            private void Radial(GeoPoint2 centre, double radius, double leader)
            {
                GeoPoint2 chord = centre + new GeoVector2(1, 1) / Math.Sqrt(2.0) * (radius * _k);
                Add(
                    new DxfRadialDimension(P3(centre), P3(chord), leader * _k, string.Empty, null),
                    new SampleShape
                    {
                        Kind = SampleShapeKind.Radial,
                        Centre = centre,
                        Rim = chord,
                    }
                );
            }

            /// <summary>An angle of three points, its arc through a point.</summary>
            private void Angular(GeoPoint2 vertex, GeoPoint2 first, GeoPoint2 second, GeoPoint2 arcPoint) =>
                Add(
                    new DxfPoint3AngularDimension(P3(vertex), P3(first), P3(second), P3(arcPoint), string.Empty, null),
                    new SampleShape { Kind = SampleShapeKind.Angular, Centre = vertex }
                );

            /// <summary>An arc of 90 degrees (45 to 135) about a centre, and its arc length dimension measured on an arc of <paramref name="distance"/>.</summary>
            private void ArcLength(GeoPoint2 centre, double radius, double distance)
            {
                double start = Math.PI / 4.0;
                double sweep = Math.PI / 2.0;
                Outline(new DxfArc(P3(centre), radius * _k, start, start + sweep));
                GeoPoint2 first = centre + Heading(start) * (radius * _k);
                GeoPoint2 second = centre + Heading(start + sweep) * (radius * _k);
                ArcLengthPlace(centre, radius * _k, start, sweep, distance * _k, false, out GeoPoint2 arcPoint);
                Add(
                    new DxfArcDimension(P3(centre), P3(first), P3(second), P3(arcPoint), string.Empty, null)
                    {
                        ArcStartParam = start,
                        ArcEndParam = start + sweep,
                        Leader1Point = P3(first),
                        Leader2Point = P3(second),
                    },
                    new SampleShape { Kind = SampleShapeKind.ArcLength, Centre = centre }
                );
            }

            /// <summary>An arc of 40 degrees about a centre, left of it, and a jogged radius to it from a centre drawn in its place.</summary>
            private void Jogged(GeoPoint2 centre, double radius, GeoPoint2 over)
            {
                double r = radius * _k;
                Outline(new DxfArc(P3(centre), r, 160.0 * Math.PI / 180.0, 200.0 * Math.PI / 180.0));
                GeoPoint2 rim = centre + new GeoVector2(-1.0, 0.0) * r;
                GeoVector2 u = (rim - centre) / r;
                GeoPoint2 location = over + (rim - over) * 0.6;
                GeoPoint2 given = over + u * (4.0 * _k);
                var probe = new DxfRadialDimensionLarge(
                    P3(centre),
                    P3(centre),
                    P3(over),
                    P3(over),
                    0.0,
                    string.Empty,
                    null
                );
                string words = Words(_style, probe, r, null, "R");
                double width = WordsWidth(_style, words, _style.Dimtxt * _k, out double tall);
                JogPlaces(
                    centre,
                    r,
                    over,
                    location,
                    given,
                    _style.Dimjogang,
                    _style.Dimasz * _k,
                    Math.Abs(_style.Dimgap) * _k,
                    width,
                    _style.Dimtad,
                    _style.Dimtih,
                    tall,
                    1E-9,
                    out GeoPoint2 chord,
                    out GeoPoint2 jog,
                    out GeoPoint2 at
                );
                GeoVector2 chordWay = UnitOf(chord - centre);
                JogFrame(
                    centre,
                    r,
                    chordWay,
                    over,
                    jog,
                    _style.Dimjogang,
                    out GeoPoint2 front,
                    out GeoPoint2 back,
                    out _
                );
                Add(
                    new DxfRadialDimensionLarge(P3(centre), P3(chord), P3(over), P3(jog), 0.0, string.Empty, null)
                    {
                        TextPosition = P3(at),
                    },
                    new SampleShape
                    {
                        Kind = SampleShapeKind.Jogged,
                        Centre = centre,
                        Rim = chord,
                        Front = front,
                        Back = back,
                    }
                );
            }

            /// <summary>
            /// A leader of three points from the circle's rim, as the style draws one: its arrowhead DIMASZ long (DIMLDRBLK's block
            /// where the style names one) and its line from the arrowhead's back, each its own entity.
            /// </summary>
            private void Leader(GeoPoint2 tip)
            {
                double size = _style.Dimasz * _k;
                GeoPoint2 knee = P(38, -4);
                GeoPoint2 end = P(34.5, -4);
                GeoVector2 away = UnitOf(tip - knee);
                GeoPoint2 back = tip - away * size;
                var parts = new List<DxfEntity>();
                DxfBlockTableRecord block = _style.Dimldrblk;
                DxfEntity head;

                if (block == null)
                {
                    GeoVector2 side = new GeoVector2(-away.Y, away.X) * (size / 6.0);
                    head = new DxfSolid(P3(back + side), P3(tip), P3(back - side));
                }
                else
                {
                    head = new DxfBlockReference(P3(tip), block)
                    {
                        ScaleFactors = new GeoVector3(size, size, size),
                        Rotation = Math.Atan2(away.Y, away.X),
                    };
                }

                parts.Add(Tag(head, CadStylePart.LeaderArrow));
                var shape = new DxfLeader();
                shape.AppendVertex(P3((tip - knee).Length > size + 1E-9 ? back : tip));
                shape.AppendVertex(P3(knee));
                shape.AppendVertex(P3(end));
                shape.HasArrowHead = false;

                foreach (DxfEntity line in shape.Explode())
                {
                    parts.Add(Tag(line, CadStylePart.LeaderLine));
                }

                int index = _shapes.Count;
                _shapes.Add(new SampleShape { Kind = SampleShapeKind.Leader, Centre = tip });

                foreach (DxfEntity part in parts)
                {
                    _database.ModelSpace.AppendEntity(part);
                    Record(index, part, PartsOfEntity(part));
                }
            }

            /// <summary>
            /// Adds a dimension with a block of its own, draws its picture and copies the entities of the block into model space
            /// (each its own entity with its own handle), the dimension erased.
            /// </summary>
            private void Add(DxfDimension dimension, SampleShape shape)
            {
                Sample(_database, _style, dimension);
                int index = _shapes.Count;
                _shapes.Add(shape);

                foreach (DxfEntity entity in dimension.DimBlock.ToList())
                {
                    DxfEntity copy = (DxfEntity)entity.Clone();
                    _database.ModelSpace.AppendEntity(copy);
                    Record(index, copy, PartsOfEntity(entity));

                    if (shape.Words == null && copy is DxfMText words)
                    {
                        shape.Words = words;
                    }
                }

                dimension.Erase();
            }

            /// <summary>Keeps which parts an entity of the sample is, and gives it the linetype its style gives that part.</summary>
            private void Record(int dimension, DxfEntity entity, CadStylePart[] parts)
            {
                if (parts.Length == 0)
                {
                    _untagged.Add(entity.Handle);
                    return;
                }

                foreach (CadStylePart part in parts)
                {
                    int key = dimension * 64 + (int)part;

                    if (!_entries.TryGetValue(key, out PartEntry entry))
                    {
                        entry = new PartEntry(part, dimension);
                        _entries.Add(key, entry);
                        _parts.Add(entry);
                    }

                    entry.Handles.Add(entity.Handle);
                }

                DxfLinetypeTableRecord linetype =
                    Array.IndexOf(parts, CadStylePart.DimensionLine1) >= 0
                    || Array.IndexOf(parts, CadStylePart.DimensionLine2) >= 0
                        ? _style.Dimltype
                    : Array.IndexOf(parts, CadStylePart.ExtensionLine1) >= 0 ? _style.Dimltex1
                    : Array.IndexOf(parts, CadStylePart.ExtensionLine2) >= 0 ? _style.Dimltex2
                    : null;

                if (linetype != null && !string.IsNullOrEmpty(linetype.Name))
                {
                    entity.Linetype = linetype.Name;
                }
            }
        }

        /// <summary>The entities of one part of one dimension, as they are drawn into a sample.</summary>
        private sealed class PartEntry
        {
            internal PartEntry(CadStylePart part, int dimension)
            {
                Part = part;
                Dimension = dimension;
            }

            internal CadStylePart Part { get; }

            internal int Dimension { get; }

            internal List<string> Handles { get; } = new List<string>();
        }
    }
}
