using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using GeometryHelper.Dxf;
using GeometryHelper.Geometry;

namespace OpenCAD2D
{
    public static partial class CadMLeaderStyleEditor
    {
        private const CadStylePart LeaderLine = CadStylePart.LeaderLine;
        private const CadStylePart Landing = CadStylePart.Landing;
        private const CadStylePart Words = CadStylePart.Text;
        private const CadStylePart Arrow = CadStylePart.Arrow;

        /// <summary>
        /// Every setting of the Modify Multileader Style dialog, in the order of its tabs and rows, with the parts of the sample it
        /// sets. A row of several settings (the block's source, attachment, colour and scale) is a key for each.
        /// </summary>
        private static readonly KeyValuePair<string, CadStylePart[]>[] PartRows =
        {
            // Leader Format
            Row("LeaderType", LeaderLine),
            Row("LeaderColor", LeaderLine, Landing, Arrow),
            Row("LeaderLinetype", LeaderLine, Landing),
            Row("LeaderLineweight", LeaderLine, Landing),
            Row("Arrowhead", Arrow),
            Row("ArrowSize", Arrow),
            Row("BreakSize"),
            // Leader Structure
            Row("MaxLeaderPoints", LeaderLine),
            Row("FirstSegmentAngle", LeaderLine),
            Row("SecondSegmentAngle", Landing),
            Row("IncludeLanding", Landing),
            Row("SetLandingDistance", Landing),
            Row("LandingDistance", Landing),
            Row("Annotative"),
            Row("Scale"),
            // Content
            Row("ContentType", Words, CadStylePart.Block),
            Row("BlockName", CadStylePart.Block),
            Row("BlockConnection", CadStylePart.Block),
            Row("BlockColor", CadStylePart.Block),
            Row("BlockScale", CadStylePart.Block),
            Row("DefaultText", Words),
            Row("TextStyle", Words),
            Row("TextAngle", Words),
            Row("TextColor", Words),
            Row("TextHeight", Words),
            Row("AlwaysLeftJustify", Words),
            Row("FrameText", Words),
            Row("AttachmentDirection", Landing, Words),
            Row("LeftAttachment", Landing, Words),
            Row("RightAttachment", Landing, Words),
            Row("TopAttachment", Landing, Words),
            Row("BottomAttachment", Landing, Words),
            Row("LandingGap", Landing, Words),
            Row("ExtendLeaderToText", Landing),
        };

        private static readonly Dictionary<string, CadStylePart[]> PartRowsByKey = PartRows.ToDictionary(
            r => r.Key,
            r => r.Value,
            StringComparer.OrdinalIgnoreCase
        );

        private static KeyValuePair<string, CadStylePart[]> Row(string key, params CadStylePart[] parts) =>
            new KeyValuePair<string, CadStylePart[]>(key, parts);

        /// <summary>
        /// Makes the sample multileader of the Manager's preview with its parts told apart: the drawing of
        /// <see cref="Sample"/>, its multileader broken into the entities it draws as (<see cref="DxfMLeader.ExplodeBroken"/>) in
        /// model space, each with a handle of its own, the multileader erased. The parts follow the order the pieces come in: the
        /// content first, the words (<see cref="CadStylePart.Text"/>) or the block (<see cref="CadStylePart.Block"/>); then the
        /// dogleg of each leader (<see cref="CadStylePart.Landing"/>); then each leader line (<see cref="CadStylePart.LeaderLine"/>,
        /// a polyline or a spline) and its arrowhead (<see cref="CadStylePart.Arrow"/>, a solid or the insert of its block).
        /// </summary>
        /// <param name="source">The drawing edited, whose text style's font the words take; null for Standard's own.</param>
        /// <param name="settings">The settings of the style.</param>
        /// <returns>The sample; of no parts where the settings do not make a style.</returns>
        /// <exception cref="ArgumentNullException">Thrown when the settings are null.</exception>
        public static CadStyleSample PreviewSample(DxfDatabase source, CadMLeaderStyleSettings settings)
        {
            DxfDatabase database = Sample(source, settings);
            DxfMLeader mleader = database.ModelSpace.OfType<DxfMLeader>().FirstOrDefault();
            double size =
                settings.TextHeight > 0.0 && !double.IsInfinity(settings.TextHeight) ? settings.TextHeight : 2.5;
            var landingPoint = new GeoPoint2(6.0 * size, 4.0 * size);
            var shape = new SampleShape
            {
                Kind = SampleShapeKind.MLeader,
                Centre = new GeoPoint2(0.0, 0.0),
                LandStart = landingPoint,
                LandEnd = landingPoint,
            };
            var entries = new List<KeyValuePair<CadStylePart, List<string>>>();
            var untagged = new List<string>();

            if (mleader != null)
            {
                List<DxfEntity> pieces = mleader.ExplodeBroken().ToList();
                mleader.Erase();
                bool contentTaken = settings.ContentType != DxfContentType.BlockContent;

                foreach (DxfEntity piece in pieces)
                {
                    CadStylePart part = PartOfPiece(piece, ref contentTaken);
                    database.ModelSpace.AppendEntity(piece);

                    if (part == CadStylePart.None)
                    {
                        untagged.Add(piece.Handle);
                        continue;
                    }

                    KeyValuePair<CadStylePart, List<string>> entry = entries.FirstOrDefault(e => e.Key == part);

                    if (entry.Value == null)
                    {
                        entry = new KeyValuePair<CadStylePart, List<string>>(part, new List<string>());
                        entries.Add(entry);
                    }

                    entry.Value.Add(piece.Handle);

                    if (part == CadStylePart.Text && piece is DxfMText words)
                    {
                        shape.Words = words;
                    }
                    else if (part == CadStylePart.Landing && piece is DxfLine landing)
                    {
                        shape.LandStart = new GeoPoint2(landing.StartPoint.X, landing.StartPoint.Y);
                        shape.LandEnd = new GeoPoint2(landing.EndPoint.X, landing.EndPoint.Y);
                    }
                }
            }

            return new CadStyleSample(
                database,
                CadDimSampleKind.Overview,
                entries.ConvertAll(e => new CadStyleSamplePart(e.Key, 0, e.Value)),
                new[] { shape },
                untagged
            );
        }

        /// <summary>
        /// The part a piece of an exploded multileader is: the first insert of a style of block content the block, a text the
        /// words, a line the landing, a polyline or a spline a leader line, and a solid or an insert the arrowhead.
        /// </summary>
        private static CadStylePart PartOfPiece(DxfEntity piece, ref bool contentTaken)
        {
            switch (piece)
            {
                case DxfBlockReference _ when !contentTaken:
                    contentTaken = true;
                    return CadStylePart.Block;
                case DxfMText _:
                    return Words;
                case DxfLine _:
                    return Landing;
                case DxfPolyline _:
                case DxfPolyline2d _:
                case DxfSpline _:
                    return LeaderLine;
                case DxfSolid _:
                case DxfBlockReference _:
                    return Arrow;
                default:
                    return CadStylePart.None;
            }
        }

        /// <summary>
        /// Gets the parts of the sample a setting of the Modify Multileader Style dialog sets. The key is the name of the
        /// setting, as <see cref="CadMLeaderStyleSettings"/> names it (<c>ArrowSize</c>, <c>LandingGap</c>), letters of either case
        /// the same. A setting of no part (the break size, annotative, the scale) gives none.
        /// </summary>
        /// <param name="key">The key of the setting.</param>
        /// <returns>The parts; none for a setting of no part and for a key no setting has.</returns>
        public static IReadOnlyList<CadStylePart> PartsOf(string key) =>
            key != null && PartRowsByKey.TryGetValue(key.Replace(" ", string.Empty), out CadStylePart[] parts)
                ? parts
                : Array.Empty<CadStylePart>();

        /// <summary>Gets the keys of the settings that set a part, in the order of the dialog: Leader Format, Leader Structure, Content, each in its rows' order.</summary>
        /// <param name="part">The part.</param>
        /// <returns>The keys, each as <see cref="PartsOf"/> takes it.</returns>
        public static IReadOnlyList<string> KeysOf(CadStylePart part) =>
            PartRows.Where(r => Array.IndexOf(r.Value, part) >= 0).Select(r => r.Key).ToList();

        /// <summary>
        /// Measures what a setting sets on the sample, as a callout: the arrowhead's size along the leader from its tip
        /// (<c>ArrowSize</c>), the landing's length (<c>LandingDistance</c>), the gap from its end to the words
        /// (<c>LandingGap</c>), the height of the words (<c>TextHeight</c>) and the angle the first segment is held to
        /// (<c>FirstSegmentAngle</c>, an arc from the x axis), and, of a style that leaves the landing out, the ghost of the dogleg it would
        /// have (<c>IncludeLanding</c>). The points are in the drawing coordinates of the sample, so the lengths
        /// are the values times the style's scale; the label has the value as the dialog shows it.
        /// </summary>
        /// <param name="sample">The sample, as <see cref="PreviewSample"/> made it.</param>
        /// <param name="settings">The settings of the style.</param>
        /// <param name="key">The key of the setting.</param>
        /// <returns>The measures; none where the setting sets no distance, or the sample lacks the part it is measured on.</returns>
        /// <exception cref="ArgumentNullException">Thrown when the sample or the settings are null.</exception>
        public static IReadOnlyList<CadStyleMeasure> Measures(
            CadStyleSample sample,
            CadMLeaderStyleSettings settings,
            string key
        )
        {
            if (sample == null)
            {
                throw new ArgumentNullException(nameof(sample));
            }

            if (settings == null)
            {
                throw new ArgumentNullException(nameof(settings));
            }

            var none = Array.Empty<CadStyleMeasure>();
            SampleShape shape = sample.Shapes.FirstOrDefault(s => s.Kind == SampleShapeKind.MLeader);

            if (shape == null || key == null)
            {
                return none;
            }

            double scale = settings.Scale > 0.0 ? settings.Scale : 1.0;
            bool landed = (shape.LandEnd - shape.LandStart).Length > 1E-12;
            GeoVector2 landing = landed
                ? (shape.LandEnd - shape.LandStart) / (shape.LandEnd - shape.LandStart).Length
                : new GeoVector2(1.0, 0.0);
            GeoVector2 leader = shape.LandStart - shape.Centre;

            switch (key.Replace(" ", string.Empty).ToUpperInvariant())
            {
                case "ARROWSIZE":
                    return leader.Length <= 1E-12
                        ? none
                        : new[]
                        {
                            Callout(
                                "ArrowSize",
                                shape.Centre,
                                shape.Centre + leader / leader.Length * (settings.ArrowSize * scale),
                                null,
                                settings.ArrowSize
                            ),
                        };
                case "LANDINGDISTANCE":
                    return !landed
                        ? none
                        : new[]
                        {
                            Callout(
                                "LandingDistance",
                                shape.LandStart,
                                shape.LandStart + landing * (settings.LandingDistance * scale),
                                null,
                                settings.LandingDistance
                            ),
                        };
                case "LANDINGGAP":
                    return !landed
                        ? none
                        : new[]
                        {
                            Callout(
                                "LandingGap",
                                shape.LandEnd,
                                shape.LandEnd + landing * (settings.LandingGap * scale),
                                null,
                                settings.LandingGap
                            ),
                        };
                case "TEXTHEIGHT":
                    return shape.Words == null
                        ? none
                        : new[]
                        {
                            Callout(
                                "TextHeight",
                                new GeoPoint2(shape.Words.Location.X, shape.Words.Location.Y),
                                new GeoPoint2(
                                    shape.Words.Location.X,
                                    shape.Words.Location.Y - settings.TextHeight * scale
                                ),
                                null,
                                settings.TextHeight
                            ),
                        };
                case "INCLUDELANDING":
                    // Of a style that leaves the landing out, the ghost of the dogleg it would have: from where the leader would end.
                    return settings.IncludeLanding || leader.Length <= 1E-12
                        ? none
                        : new[]
                        {
                            new CadStyleMeasure(
                                "IncludeLanding",
                                shape.LandStart,
                                shape.LandStart + new GeoVector2(1.0, 0.0) * (settings.LandingDistance * scale),
                                null,
                                "IncludeLanding = off"
                            ),
                        };
                case "FIRSTSEGMENTANGLE":
                    double degrees = SegmentDegrees(settings.FirstSegmentAngle);
                    return degrees <= 0.0 || leader.Length <= 1E-12
                        ? none
                        : new[]
                        {
                            new CadStyleMeasure(
                                "FirstSegmentAngle",
                                shape.Centre,
                                shape.Centre + new GeoVector2(0.6 * leader.Length, 0.0),
                                degrees * Math.PI / 180.0,
                                "FirstSegmentAngle = " + degrees.ToString("0.####", CultureInfo.InvariantCulture)
                            ),
                        };
                default:
                    return none;
            }
        }

        private static CadStyleMeasure Callout(string key, GeoPoint2 from, GeoPoint2 to, double? angle, double value) =>
            new CadStyleMeasure(
                key,
                from,
                to,
                angle,
                key + " = " + value.ToString("0.0###", CultureInfo.InvariantCulture)
            );

        /// <summary>The degrees an angle constraint holds a segment to; nought for none and for horizontal.</summary>
        private static double SegmentDegrees(DxfAngleConstraint constraint)
        {
            switch (constraint)
            {
                case DxfAngleConstraint.Degrees015:
                    return 15.0;
                case DxfAngleConstraint.Degrees030:
                    return 30.0;
                case DxfAngleConstraint.Degrees045:
                    return 45.0;
                case DxfAngleConstraint.Degrees060:
                    return 60.0;
                case DxfAngleConstraint.Degrees090:
                    return 90.0;
                default:
                    return 0.0;
            }
        }
    }
}
