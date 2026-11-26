using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Reflection;
using GeometryHelper;
using GeometryHelper.Dxf;
using GeometryHelper.Geometry;

namespace OpenCAD2D
{
    /// <summary>
    /// A drawing written as JSON for a program that learns from drawings or reads them: every entity walked down to its shapes
    /// (see <see cref="CadFlatten"/>) with its handle, kind, layer, colour, linetype, box, words and geometry in world
    /// millimetres, beside the tables of layers, styles, linetypes and blocks. The document is described by a JSON Schema,
    /// <see cref="Schema"/>, and carries <c>"schema": "opencad2d.drawing"</c> and <c>"version": 1</c>.
    /// </summary>
    /// <remarks>
    /// <para>
    /// The same drawing gives the same text on every run and on both .NET Framework and .NET: entities in the order of
    /// their handles (a hexadecimal handle read as a number; see <see cref="CadFlatten.Walk"/>), tables by name, numbers rounded
    /// to six decimals (a nanometre) and written with a point. Lengths are millimetres, as <see cref="CadUnitScale"/> reads the
    /// drawing's INSUNITS (and the plot paper units of a layout for its paper space); a drawing of no unit is left as it is
    /// and the <c>units</c> object says <c>"normalised": false</c>. Angles are degrees, counter-clockwise seen from above, the
    /// way DXF writes them. Every point is [x, y]: the drawing is two-dimensional.
    /// </para>
    /// <para>
    /// An entity has <c>box</c>, [min x, min y, max x, max y] of its extents (null where it has none), whatever its
    /// <c>kind</c>; the kinds the library can describe (LINE, CIRCLE, ARC, ELLIPSE, LWPOLYLINE, POLYLINE, SPLINE, POINT, TEXT,
    /// ATTRIB, ATTDEF, MTEXT, DIMENSION, LEADER, MULTILEADER, HATCH, SOLID, TRACE, 3DFACE and an INSERT that could not be
    /// walked) carry geometry as well, in the fields the schema lists; any other kind has the common fields and the box. An
    /// entity that came out of a block has <c>path</c> (the names of the blocks), <c>indexPath</c> and <c>matrix</c> (block
    /// coordinates to world millimetres, 4 by 4, rows first).
    /// </para>
    /// </remarks>
    public static class CadDrawingJson
    {
        /// <summary>The version of the document this class writes; the schema's <c>version</c> is this number.</summary>
        public const int Version = 1;

        private static string _schema;

        /// <summary>
        /// Gets the JSON Schema (draft 2020-12) of the document <see cref="Write(CadDocument, string)"/> gives: the common fields of an entity, the
        /// fields of each kind, the tables and the report. It is the file <c>docs/schemas/opencad2d-drawing-v1.schema.json</c> of the
        /// repository, built into this assembly.
        /// </summary>
        public static string Schema
        {
            get
            {
                if (_schema == null)
                {
                    using (
                        Stream stream = typeof(CadDrawingJson)
                            .GetTypeInfo()
                            .Assembly.GetManifestResourceStream("OpenCAD2D.opencad2d-drawing-v1.schema.json")
                    )
                    using (var reader = new StreamReader(stream))
                    {
                        _schema = reader.ReadToEnd();
                    }
                }

                return _schema;
            }
        }

        /// <summary>
        /// Writes the drawing as a JSON document: <c>schema</c>, <c>version</c>, <c>name</c>, <c>units</c>, <c>layouts</c>,
        /// <c>layers</c>, <c>textStyles</c>, <c>dimStyles</c>, <c>linetypes</c>, <c>blocks</c>, <c>report</c> and
        /// <c>entities</c>. A drawing of one LINE from (0, 0) to (100, 0) in millimetres gives an entity
        /// <c>{"handle":"2A","kind":"LINE","layer":"0",...,"start":[0,0],"end":[100,0]}</c>.
        /// </summary>
        /// <param name="document">The drawing.</param>
        /// <param name="layout">The layout to write (<c>Model</c> for model space), or null for model space and every layout.</param>
        /// <returns>The JSON text.</returns>
        /// <exception cref="ArgumentNullException">Thrown when the drawing is null.</exception>
        /// <exception cref="ArgumentException">Thrown when the drawing has no layout of that name.</exception>
        public static string Write(CadDocument document, string layout)
        {
            CadFlatDrawing flat = CadFlatten.Walk(document, layout);
            return Write(flat, document);
        }

        /// <summary>
        /// Writes a drawing already walked, as <see cref="Write(CadDocument, string)"/> does; for a caller that also builds the
        /// graph of the same walk (<see cref="CadDrawingGraph"/>) and walks once.
        /// </summary>
        /// <param name="flat">The walk.</param>
        /// <param name="document">The drawing the walk was made of.</param>
        /// <returns>The JSON text.</returns>
        /// <exception cref="ArgumentNullException">Thrown when the drawing or the walk is null.</exception>
        public static string Write(CadFlatDrawing flat, CadDocument document)
        {
            if (document == null)
            {
                throw new ArgumentNullException(nameof(document));
            }

            if (flat == null)
            {
                throw new ArgumentNullException(nameof(flat));
            }

            DxfDatabase database = document.Database;
            double modelScale = CadUnitScale.Of(database, out bool normalised);
            var json = new CadJsonWriter(256 + flat.Entities.Count * 220);
            json.Begin();
            json.Name("schema").Value("opencad2d.drawing");
            json.Name("version").Value(Version);
            json.Name("name").Value(document.Title);

            json.Name("units").Begin();
            json.Name("insunits").Value(database.Insunits.ToString());
            json.Name("insunitsCode").Value((int)database.Insunits);
            json.Name("toMillimetres").Value(modelScale);
            json.Name("normalised").Value(normalised);
            json.End();

            json.Name("layouts").BeginArray();

            foreach (CadFlatLayout item in flat.Layouts)
            {
                json.Begin();
                json.Name("name").Value(item.Name);
                json.Name("isModel").Value(item.IsModel);
                json.Name("toMillimetres").Value(item.Scale);
                json.Name("normalised").Value(item.Normalised);
                json.End();
            }

            json.EndArray();
            WriteTables(json, database, modelScale);
            WriteReport(json, flat.Report);
            json.Name("entities").BeginArray();

            foreach (CadFlatEntity entity in flat.Entities)
            {
                WriteEntity(json, flat, entity);
            }

            json.EndArray();
            json.End();
            return json.ToString();
        }

        /// <summary>The records of a table in the order of their names: capitals first, then as written, so that no table order shows.</summary>
        private static List<T> ByName<T>(IEnumerable<T> records, Func<T, string> name) =>
            records
                .OrderBy(r => name(r), StringComparer.OrdinalIgnoreCase)
                .ThenBy(r => name(r), StringComparer.Ordinal)
                .ToList();

        private static void WriteTables(CadJsonWriter json, DxfDatabase database, double scale)
        {
            json.Name("layers").BeginArray();

            foreach (DxfLayerTableRecord layer in ByName(database.LayerTable.Cast<DxfLayerTableRecord>(), l => l.Name))
            {
                json.Begin();
                json.Name("name").Value(layer.Name);
                json.Name("color").Value(ColorText(layer.Color));
                json.Name("linetype").Value(layer.LinetypeName);

                if ((int)layer.LineWeight >= 0)
                {
                    json.Name("lineweight").Value((int)layer.LineWeight / 100.0);
                }
                else
                {
                    json.Name("lineweight").Null();
                }

                json.Name("on").Value(!layer.IsOff);
                json.Name("frozen").Value(layer.IsFrozen);
                json.Name("locked").Value(layer.IsLocked);
                json.Name("plot").Value(layer.IsPlottable);
                json.End();
            }

            json.EndArray();
            json.Name("textStyles").BeginArray();

            foreach (
                DxfTextStyleTableRecord style in ByName(database.TextStyleTable.Cast<DxfTextStyleTableRecord>(), s =>
                    s.Name
                )
            )
            {
                json.Begin();
                json.Name("name").Value(style.Name);
                json.Name("font").Value(style.FileName);
                json.Name("bigFont").Value(string.IsNullOrEmpty(style.BigFontFileName) ? null : style.BigFontFileName);
                json.Name("height").Value(style.TextSize * scale);
                json.Name("widthFactor").Value(style.XScale);
                json.End();
            }

            json.EndArray();
            json.Name("dimStyles").BeginArray();

            foreach (
                DxfDimStyleTableRecord style in ByName(database.DimStyleTable.Cast<DxfDimStyleTableRecord>(), s =>
                    s.Name
                )
            )
            {
                json.Begin();
                json.Name("name").Value(style.Name);
                json.Name("scale").Value(style.Dimscale);
                json.Name("textHeight").Value(style.Dimtxt * scale);
                json.Name("arrowSize").Value(style.Dimasz * scale);
                json.Name("decimals").Value(style.Dimdec);
                json.End();
            }

            json.EndArray();
            json.Name("linetypes").BeginArray();

            foreach (
                DxfLinetypeTableRecord linetype in ByName(database.LinetypeTable.Cast<DxfLinetypeTableRecord>(), l =>
                    l.Name
                )
            )
            {
                json.Begin();
                json.Name("name").Value(linetype.Name);
                json.Name("description").Value(linetype.AsciiDescription ?? string.Empty);
                json.Name("patternLength").Value(linetype.PatternLength * scale);
                json.End();
            }

            json.EndArray();
            json.Name("blocks").BeginArray();

            foreach (
                DxfBlockTableRecord block in ByName(
                    database.BlockTable.Cast<DxfBlockTableRecord>().Where(b => !b.IsLayout),
                    b => b.Name
                )
            )
            {
                json.Begin();
                json.Name("name").Value(block.Name);
                json.Name("origin").Point(block.Origin, scale);
                json.Name("units").Value(block.Units.ToString());
                json.Name("anonymous").Value(block.Name.StartsWith("*", StringComparison.Ordinal));
                json.Name("dynamic").Value(block.IsDynamicBlock);
                json.Name("xref").Value(block.IsFromExternalReference);
                json.Name("entities").Value(block.Count);
                json.End();
            }

            json.EndArray();
        }

        private static void WriteReport(CadJsonWriter json, CadFlatReport report)
        {
            json.Name("report").Begin();
            json.Name("inserts").Value(report.Inserts);
            Strings(json, "xrefs", report.Xrefs);
            Strings(json, "clipped", report.Clipped);
            Strings(json, "refused", report.Refused);
            Strings(json, "blockUnits", report.BlockUnits);
            json.End();
        }

        private static void Strings(CadJsonWriter json, string name, IEnumerable<string> values)
        {
            json.Name(name).BeginArray();

            foreach (string value in values)
            {
                json.Value(value);
            }

            json.EndArray();
        }

        /// <summary>A colour as text: ByLayer, ByBlock, <c>ACI:7</c> for an index, <c>RGB:ff0000</c> for a true colour.</summary>
        internal static string ColorText(DxfColor color)
        {
            if (color == null || color.IsByLayer)
            {
                return "ByLayer";
            }

            if (color.IsByBlock)
            {
                return "ByBlock";
            }

            if (color.IsByAci)
            {
                return "ACI:" + color.ColorIndex.ToString(CultureInfo.InvariantCulture);
            }

            if (color.IsByColor)
            {
                return "RGB:"
                    + color.Red.ToString("x2", CultureInfo.InvariantCulture)
                    + color.Green.ToString("x2", CultureInfo.InvariantCulture)
                    + color.Blue.ToString("x2", CultureInfo.InvariantCulture);
            }

            return color.IsForeground ? "Foreground" : "None";
        }

        /// <summary>The box of an entity's extents in the library's reckoning, in millimetres, or null where it has none.</summary>
        internal static double[] ExtentsBoxOf(DxfEntity entity, double scale)
        {
            try
            {
                GeoAabb3 box = entity.GeometricExtents;

                if (
                    double.IsNaN(box.Min.X)
                    || double.IsNaN(box.Min.Y)
                    || double.IsInfinity(box.Min.X)
                    || double.IsInfinity(box.Max.X)
                    || double.IsInfinity(box.Min.Y)
                    || double.IsInfinity(box.Max.Y)
                )
                {
                    return null;
                }

                return new[] { box.Min.X * scale, box.Min.Y * scale, box.Max.X * scale, box.Max.Y * scale };
            }
            catch (Exception exception)
                when (exception is InvalidOperationException
                    || exception is ArgumentException
                    || exception is NotSupportedException
                )
            {
                return null;
            }
        }

        private static void WriteEntity(CadJsonWriter json, CadFlatDrawing walk, CadFlatEntity flat)
        {
            DxfEntity entity = flat.Entity;
            double s = flat.Scale;
            json.Begin();
            json.Name("handle").Value(flat.Handle);
            json.Name("kind").Value(entity.DxfName);
            json.Name("layer").Value(flat.Layer);
            json.Name("color").Value(ColorText(flat.Color));
            json.Name("linetype").Value(flat.Linetype);
            json.Name("layout").Value(flat.Layout);
            double[] box = walk.BoxOf(flat);

            if (box == null)
            {
                json.Name("box").Null();
            }
            else
            {
                json.Name("box").BeginArray().Value(box[0]).Value(box[1]).Value(box[2]).Value(box[3]).EndArray();
            }

            json.Name("path").BeginArray();

            foreach (string name in flat.Path)
            {
                json.Value(name);
            }

            json.EndArray();

            if (flat.Matrix != null)
            {
                json.Name("indexPath").BeginArray();

                foreach (int i in flat.IndexPath)
                {
                    json.Value(i);
                }

                json.EndArray();
                json.Name("matrix").BeginArray();

                for (int row = 0; row < 4; row++)
                {
                    json.BeginArray();

                    for (int column = 0; column < 4; column++)
                    {
                        json.Value(flat.Matrix[row, column] * (row < 3 ? s : 1.0));
                    }

                    json.EndArray();
                }

                json.EndArray();
            }

            if (flat.Clipped)
            {
                json.Name("clipped").Value(true);
            }

            if (flat.Dynamic)
            {
                json.Name("dynamic").Value(true);
            }

            if (flat.Constant)
            {
                json.Name("constant").Value(true);
            }

            CadJsonWriter.Mark mark = json.Here();

            try
            {
                WriteGeometry(json, entity, s);
            }
            catch (Exception exception)
                when (exception is InvalidOperationException
                    || exception is ArgumentException
                    || exception is NotSupportedException
                )
            {
                // An entity the library cannot read further is left with its common fields and its box.
                json.Rewind(mark);
            }

            json.End();
        }

        /// <summary>The angle of a direction in degrees, from nought up to 360.</summary>
        internal static double Degrees(double radians)
        {
            double degrees = radians * 180.0 / Math.PI;
            degrees %= 360.0;
            return degrees < 0.0 ? degrees + 360.0 : degrees;
        }

        private static void WriteGeometry(CadJsonWriter json, DxfEntity entity, double s)
        {
            switch (entity)
            {
                case DxfLine line:
                    json.Name("start").Point(line.StartPoint, s);
                    json.Name("end").Point(line.EndPoint, s);
                    break;
                case DxfCircle circle:
                    json.Name("center").Point(circle.Center, s);
                    json.Name("radius").Value(circle.Radius * s);
                    break;
                case DxfArc arc:
                {
                    // An arc stands in the coordinates of its normal; seen from above, an arc of a normal turned down runs
                    // the other way, from 180 minus its end angle to 180 minus its start angle.
                    double start = Degrees(arc.StartAngle);
                    double end = Degrees(arc.EndAngle);

                    if (arc.Normal.Z < 0.0)
                    {
                        double flippedStart = Degrees(Math.PI - arc.EndAngle);
                        end = Degrees(Math.PI - arc.StartAngle);
                        start = flippedStart;
                    }

                    json.Name("center").Point(arc.Center, s);
                    json.Name("radius").Value(arc.Radius * s);
                    json.Name("startAngle").Value(start);
                    json.Name("endAngle").Value(end);
                    break;
                }

                case DxfEllipse ellipse:
                    WriteEllipse(json, ellipse, s);
                    break;
                case DxfPolyline lw:
                {
                    bool flip = lw.Normal.Z < 0.0;
                    int count = lw.NumberOfVertices;
                    json.Name("vertices").BeginArray();

                    for (int i = 0; i < count; i++)
                    {
                        json.Point(lw.GetPoint3dAt(i), s);
                    }

                    json.EndArray();
                    json.Name("bulges").BeginArray();

                    for (int i = 0; i < count; i++)
                    {
                        json.Value(flip ? -lw.GetBulgeAt(i) : lw.GetBulgeAt(i));
                    }

                    json.EndArray();
                    json.Name("closed").Value(lw.Closed);
                    break;
                }

                case DxfPolyline2d poly2d:
                {
                    var vertices = new List<DxfVertex2d>(poly2d.Cast<DxfVertex2d>());
                    json.Name("vertices").BeginArray();

                    foreach (DxfVertex2d vertex in vertices)
                    {
                        json.Point(vertex.Position, s);
                    }

                    json.EndArray();
                    json.Name("bulges").BeginArray();

                    foreach (DxfVertex2d vertex in vertices)
                    {
                        json.Value(vertex.Bulge);
                    }

                    json.EndArray();
                    json.Name("closed").Value(poly2d.Closed);
                    break;
                }

                case DxfPolyline3d poly3d:
                    json.Name("vertices").BeginArray();

                    foreach (DxfPolylineVertex3d vertex in poly3d)
                    {
                        json.Point(vertex.Position, s);
                    }

                    json.EndArray();
                    json.Name("closed").Value(poly3d.Closed);
                    break;
                case DxfSpline spline:
                    json.Name("degree").Value(spline.Degree);
                    json.Name("closed").Value(spline.Closed);
                    Points(json, "controlPoints", spline.ControlPoints, s);
                    Points(json, "fitPoints", spline.FitPoints, s);
                    Numbers(json, "knots", spline.Knots);
                    Numbers(json, "weights", spline.Weights);
                    break;
                case DxfDBPoint point:
                    json.Name("position").Point(point.Position, s);
                    break;
                case DxfAttributeReference attribute:
                    json.Name("tag").Value(attribute.Tag);
                    json.Name("invisible").Value(attribute.Invisible);
                    WriteText(json, attribute, s, attribute.IsMTextAttribute ? attribute.MTextAttribute.Text : null);
                    break;
                case DxfAttributeDefinition definition:
                    json.Name("tag").Value(definition.Tag);
                    json.Name("invisible").Value(definition.Invisible);
                    WriteText(json, definition, s, null);
                    break;
                case DxfDBText text:
                    WriteText(json, text, s, null);
                    break;
                case DxfMText mtext:
                    json.Name("position").Point(mtext.Location, s);
                    json.Name("height").Value(mtext.TextHeight * s);
                    json.Name("width").Value(mtext.Width * s);
                    json.Name("rotation").Value(Degrees(mtext.Rotation));
                    json.Name("attachment").Value(mtext.Attachment.ToString());
                    json.Name("style").Value(mtext.TextStyleName);
                    json.Name("text").Value(mtext.Text);
                    json.Name("contents").Value(mtext.Contents);
                    break;
                case DxfDimension dimension:
                    WriteDimension(json, dimension, s);
                    break;
                case DxfLeader leader:
                {
                    json.Name("vertices").BeginArray();

                    for (int i = 0; i < leader.NumVertices; i++)
                    {
                        json.Point(leader.VertexAt(i), s);
                    }

                    json.EndArray();
                    json.Name("arrow").Value(leader.HasArrowHead);
                    json.Name("annotation").Value(leader.Annotation?.Handle);
                    break;
                }

                case DxfMLeader mleader:
                    WriteMLeader(json, mleader, s);
                    break;
                case DxfHatch hatch:
                    WriteHatch(json, hatch, s);
                    break;
                case DxfSolid solid:
                    json.Name("vertices").BeginArray();

                    for (short i = 0; i < 4; i++)
                    {
                        json.Point(solid.GetPointAt(i), s);
                    }

                    json.EndArray();
                    break;
                case DxfFace face:
                    json.Name("vertices").BeginArray();

                    for (short i = 0; i < 4; i++)
                    {
                        json.Point(face.GetVertexAt(i), s);
                    }

                    json.EndArray();
                    break;
                case DxfBlockReference insert:
                {
                    GeoVector3 scale = insert.ScaleFactors;
                    json.Name("name").Value(insert.Name);
                    json.Name("position").Point(insert.Position, s);
                    json.Name("scale").Pair(scale.X, scale.Y);
                    json.Name("rotation").Value(Degrees(insert.Rotation));
                    json.Name("attributes").BeginArray();

                    foreach (DxfAttributeReference attribute in insert.AttributeCollection)
                    {
                        json.Begin();
                        json.Name("tag").Value(attribute.Tag);
                        json.Name("text").Value(attribute.TextString);
                        json.End();
                    }

                    json.EndArray();
                    break;
                }
            }
        }

        private static void Points(CadJsonWriter json, string name, IReadOnlyList<GeoPoint3> points, double s)
        {
            json.Name(name).BeginArray();

            foreach (GeoPoint3 point in points)
            {
                json.Point(point, s);
            }

            json.EndArray();
        }

        private static void Numbers(CadJsonWriter json, string name, IReadOnlyList<double> numbers)
        {
            json.Name(name).BeginArray();

            foreach (double number in numbers)
            {
                json.Value(number);
            }

            json.EndArray();
        }

        private static void WriteText(CadJsonWriter json, DxfDBText text, double s, string plain)
        {
            json.Name("position").Point(text.Position, s);
            json.Name("alignmentPoint").Point(text.AlignmentPoint, s);
            json.Name("justify").Value(text.Justify.ToString());
            json.Name("height").Value(text.Height * s);
            json.Name("rotation").Value(Degrees(text.Rotation));
            json.Name("widthFactor").Value(text.WidthFactor);
            json.Name("style").Value(text.TextStyleName);
            json.Name("text").Value(plain ?? text.TextString);
        }

        /// <summary>
        /// An ellipse as its centre and its two half axes (point at t = centre + major cos t + minor sin t), the parameters of
        /// its first and last point, and whether it is whole. The minor axis is the normal crossed with the major, so that
        /// the parameter runs the way the ellipse is drawn, counter-clockwise or not.
        /// </summary>
        private static void WriteEllipse(CadJsonWriter json, DxfEllipse ellipse, double s)
        {
            GeoVector3 major = ellipse.MajorAxis;
            GeoVector3 minor = ellipse.MinorAxis;
            bool whole = ellipse.Closed;
            double start = 0.0;
            double end = 2.0 * Math.PI;

            if (!whole)
            {
                start = Parameter(ellipse.Center, major, minor, ellipse.StartPoint);
                end = Parameter(ellipse.Center, major, minor, ellipse.EndPoint);

                if (end <= start)
                {
                    end += 2.0 * Math.PI;
                }
            }

            json.Name("center").Point(ellipse.Center, s);
            json.Name("majorAxis").Vector(major, s);
            json.Name("minorAxis").Vector(minor, s);
            json.Name("startParam").Value(start);
            json.Name("endParam").Value(end);
            json.Name("closed").Value(whole);
        }

        /// <summary>The parameter t of a point on an ellipse: its offset from the centre, taken in the basis of the two half axes, has cosine and sine t.</summary>
        private static double Parameter(GeoPoint3 center, GeoVector3 major, GeoVector3 minor, GeoPoint3 point)
        {
            double dx = point.X - center.X;
            double dy = point.Y - center.Y;
            double majorSquared = major.X * major.X + major.Y * major.Y;
            double minorSquared = minor.X * minor.X + minor.Y * minor.Y;

            // An ellipse standing on edge to the plane has no axis to measure along: its parameter is left at nought.
            if (majorSquared <= 0.0 || minorSquared <= 0.0)
            {
                return 0.0;
            }

            double cos = (dx * major.X + dy * major.Y) / majorSquared;
            double sin = (dx * minor.X + dy * minor.Y) / minorSquared;
            double t = Math.Atan2(sin, cos);
            return t < 0.0 ? t + 2.0 * Math.PI : t;
        }

        private static void WriteDimension(CadJsonWriter json, DxfDimension dimension, double s)
        {
            string type;
            var points = new List<KeyValuePair<string, GeoPoint3>>();

            switch (dimension)
            {
                case DxfRotatedDimension rotated:
                    type = "linear";
                    points.Add(Pair("xLine1", rotated.XLine1Point));
                    points.Add(Pair("xLine2", rotated.XLine2Point));
                    points.Add(Pair("dimLine", rotated.DimLinePoint));
                    break;
                case DxfAlignedDimension aligned:
                    type = "aligned";
                    points.Add(Pair("xLine1", aligned.XLine1Point));
                    points.Add(Pair("xLine2", aligned.XLine2Point));
                    points.Add(Pair("dimLine", aligned.DimLinePoint));
                    break;
                case DxfRadialDimension radial:
                    type = "radius";
                    points.Add(Pair("center", radial.Center));
                    points.Add(Pair("chord", radial.ChordPoint));
                    break;
                case DxfDiametricDimension diameter:
                    type = "diameter";
                    points.Add(Pair("chord", diameter.ChordPoint));
                    points.Add(Pair("farChord", diameter.FarChordPoint));
                    break;
                case DxfOrdinateDimension ordinate:
                    type = "ordinate";
                    points.Add(Pair("origin", ordinate.Origin));
                    points.Add(Pair("definingPoint", ordinate.DefiningPoint));
                    points.Add(Pair("leaderEnd", ordinate.LeaderEndPoint));
                    break;
                case DxfLineAngularDimension2 angular2:
                    type = "angular2Line";
                    points.Add(Pair("xLine1Start", angular2.XLine1Start));
                    points.Add(Pair("xLine1End", angular2.XLine1End));
                    points.Add(Pair("xLine2Start", angular2.XLine2Start));
                    points.Add(Pair("xLine2End", angular2.XLine2End));
                    points.Add(Pair("arc", angular2.ArcPoint));
                    break;
                case DxfPoint3AngularDimension angular3:
                    type = "angular3Point";
                    points.Add(Pair("center", angular3.CenterPoint));
                    points.Add(Pair("xLine1", angular3.XLine1Point));
                    points.Add(Pair("xLine2", angular3.XLine2Point));
                    points.Add(Pair("arc", angular3.ArcPoint));
                    break;
                case DxfArcDimension arcLength:
                    type = "arcLength";
                    points.Add(Pair("center", arcLength.CenterPoint));
                    points.Add(Pair("xLine1", arcLength.XLine1Point));
                    points.Add(Pair("xLine2", arcLength.XLine2Point));
                    points.Add(Pair("arc", arcLength.ArcPoint));
                    break;
                case DxfRadialDimensionLarge jogged:
                    type = "jogged";
                    points.Add(Pair("center", jogged.Center));
                    points.Add(Pair("chord", jogged.ChordPoint));
                    points.Add(Pair("jog", jogged.JogPoint));
                    break;
                default:
                    type = "other";
                    break;
            }

            json.Name("dimType").Value(type);
            double measurement = dimension.Measurement;
            bool angular = type.StartsWith("angular", StringComparison.Ordinal);
            json.Name("measurement").Value(angular ? Degrees(measurement) : measurement * s);
            json.Name("text").Value(dimension.DimensionText ?? string.Empty);
            json.Name("textPosition").Point(dimension.TextPosition, s);
            json.Name("style").Value(dimension.DimensionStyleName);
            json.Name("points").Begin();

            foreach (KeyValuePair<string, GeoPoint3> point in points)
            {
                json.Name(point.Key).Point(point.Value, s);
            }

            json.End();

            KeyValuePair<string, GeoPoint3> Pair(string name, GeoPoint3 value) =>
                new KeyValuePair<string, GeoPoint3>(name, value);
        }

        private static void WriteMLeader(CadJsonWriter json, DxfMLeader mleader, double s)
        {
            string text = null;

            if (mleader.ContentType == DxfContentType.MTextContent && mleader.MText != null)
            {
                text = mleader.MText.Text;
            }

            json.Name("text").Value(text);
            json.Name("textLocation").Point(mleader.TextLocation, s);
            json.Name("lines").BeginArray();

            foreach (object shape in mleader.GetGeometry())
            {
                IReadOnlyList<GeoPoint3> vertices =
                    shape is GeoPolyline3 polyline ? polyline.Vertices
                    : shape is GeoLine3 line ? new[] { line.StartPoint, line.EndPoint }
                    : null;

                if (vertices == null)
                {
                    continue;
                }

                json.BeginArray();

                foreach (GeoPoint3 vertex in vertices)
                {
                    json.Point(vertex, s);
                }

                json.EndArray();
            }

            json.EndArray();
        }

        /// <summary>A hatch as its pattern, its fill and the corners of each boundary loop (the end points of a loop's arcs, not their bulge).</summary>
        private static void WriteHatch(CadJsonWriter json, DxfHatch hatch, double s)
        {
            json.Name("pattern").Value(hatch.PatternName);
            json.Name("solid").Value(hatch.IsSolidFill);
            json.Name("associative").Value(hatch.Associative);
            json.Name("loops").BeginArray();

            for (int i = 0; i < hatch.NumberOfLoops; i++)
            {
                DxfHatchLoop loop = hatch.GetLoopAt(i);
                json.BeginArray();

                if (loop.Polyline is GeoPolygonArc3 polygon)
                {
                    foreach (GeoPoint3 vertex in polygon.Vertices)
                    {
                        json.Point(vertex, s);
                    }
                }
                else if (loop.Polyline is GeoPolylineArc3 polyline)
                {
                    foreach (GeoPoint3 vertex in polyline.Vertices)
                    {
                        json.Point(vertex, s);
                    }
                }
                else
                {
                    foreach (object curve in loop.Curves)
                    {
                        if (curve is GeoLine3 line)
                        {
                            json.Point(line.StartPoint, s);
                        }
                        else if (curve is GeoArc3 arc)
                        {
                            json.Point(arc.StartPoint, s);
                        }
                    }
                }

                json.EndArray();
            }

            json.EndArray();
        }
    }
}
