using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using GeometryHelper;
using GeometryHelper.Dxf;
using GeometryHelper.Geometry;
using Xunit;

namespace OpenCAD2D.UnitTest
{
    /// <summary>The drawings and the driver the editing tests share.</summary>
    internal static class EditingDrawings
    {
        /// <summary>Stands for Esc among the inputs of <see cref="Run"/>.</summary>
        public static readonly object Esc = new object();

        /// <summary>Stands for Enter among the inputs of <see cref="Run"/>.</summary>
        public static readonly object Enter = new object();

        public static readonly Tolerance Tight = new Tolerance(1E-9, 1E-9);

        public static GeoPoint2 P(double x, double y) => new GeoPoint2(x, y);

        /// <summary>
        /// A plan of R2000: on layer Steel a line A1 from (0, 0) to (100, 0); on 0 a line A2 from (0, 0) to (0, 50), a circle A3
        /// of radius 10 about (200, 0), an arc A4 of radius 20 about (300, 0) from 0 to 90 degrees, a polyline A5 from (0, 100)
        /// to (100, 100) to (100, 150), a text A6 "NOTE" 5 high at (0, 200), and A7 an INSERT of the block BOX (a square of 10)
        /// at (500, 0); on layer Locked a line A8 from (0, -50) to (100, -50).
        /// </summary>
        public static DxfDatabase PlanDatabase()
        {
            string box = TestDrawings.Block(
                "BOX",
                TestDrawings.Line(0, 0, 10, 0),
                TestDrawings.Line(10, 0, 10, 10),
                TestDrawings.Line(10, 10, 0, 10),
                TestDrawings.Line(0, 10, 0, 0)
            );
            DxfDatabase database = TestDrawings.Drawing(
                TestDrawings.Layer("Steel", 1) + TestDrawings.Layer("Locked", 3),
                box,
                TestDrawings.Line(0, 0, 100, 0, "Steel", handle: "A1"),
                TestDrawings.Line(0, 0, 0, 50, handle: "A2"),
                TestDrawings.Circle(200, 0, 10, handle: "A3"),
                TestDrawings.Arc(300, 0, 20, 0, 90, handle: "A4"),
                TestDrawings.Polyline("0", "A5", false, 0, 100, 0, 100, 100, 0, 100, 150, 0),
                TestDrawings.Text(0, 200, 5, "NOTE", handle: "A6"),
                TestDrawings.Insert("BOX", 500, 0, handle: "A7"),
                TestDrawings.Line(0, -50, 100, -50, "Locked", handle: "A8")
            );
            database.LayerTable["Locked"].IsLocked = true;
            return database;
        }

        public static CadDocument Plan() => CadDocument.From(PlanDatabase(), "plan.dxf");

        /// <summary>A new drawing of R2013 in millimetres, with its text style, dimension styles (ISO-25 current) and two layouts.</summary>
        public static CadDocument Blank() => CadDocument.From(new DxfDatabase("AC1027"), "blank.dxf");

        /// <summary>
        /// Makes Standard the current dimension style, as AutoCAD's Set Current does: <c>$DIMSTYLE</c> names it and the header
        /// takes its variables, which new dimensions are drawn by. A fixture that changes Standard calls it after each change.
        /// </summary>
        public static void StandardCurrent(DxfDatabase database)
        {
            DxfDimStyleTableRecord standard = database.DimStyleTable["Standard"];
            database.Dimstyle = standard.Name;
            database.SetDimstyleData(standard);
        }

        /// <summary>
        /// <see cref="Blank"/> with the multileader style Standard, as ezdxf 1.4.5 writes its default one, which a MULTILEADER
        /// takes: the library reads a multileader style but does not write one.
        /// </summary>
        public static CadDocument BlankWithMLeaderStyle()
        {
            string text = new DxfDatabase("AC1027").ToText();
            string entry = "  3\nACAD_PLOTSTYLENAME\n350\n31\n";
            Assert.Contains(entry, text);
            text = text.Replace(entry, "  3\nACAD_MLEADERSTYLE\n350\nF0\n" + entry);
            string style =
                string.Join(
                    "\n",
                    new[]
                    {
                        "  0",
                        "DICTIONARY",
                        "  5",
                        "F0",
                        "330",
                        "C",
                        "100",
                        "AcDbDictionary",
                        "281",
                        "1",
                        "  3",
                        "Standard",
                        "350",
                        "F1",
                        "  0",
                        "MLEADERSTYLE",
                        "  5",
                        "F1",
                        "102",
                        "{ACAD_REACTORS",
                        "330",
                        "F0",
                        "102",
                        "}",
                        "330",
                        "F0",
                        "100",
                        "AcDbMLeaderStyle",
                        "179",
                        "2",
                        "170",
                        "2",
                        "171",
                        "1",
                        "172",
                        "0",
                        " 90",
                        "2",
                        " 40",
                        "0.0",
                        " 41",
                        "0.0",
                        "173",
                        "1",
                        " 91",
                        "-1056964608",
                        " 92",
                        "-2",
                        "290",
                        "1",
                        " 42",
                        "2.0",
                        "291",
                        "1",
                        " 43",
                        "8.0",
                        "  3",
                        "Standard",
                        " 44",
                        "4.0",
                        "300",
                        string.Empty,
                        "342",
                        "21",
                        "174",
                        "1",
                        "175",
                        "1",
                        "176",
                        "0",
                        "178",
                        "1",
                        " 93",
                        "-1056964608",
                        " 45",
                        "4.0",
                        "292",
                        "0",
                        "297",
                        "0",
                        " 46",
                        "4.0",
                        " 94",
                        "-1056964608",
                        " 47",
                        "1.0",
                        " 49",
                        "1.0",
                        "140",
                        "1.0",
                        "294",
                        "1",
                        "141",
                        "0.0",
                        "177",
                        "0",
                        "142",
                        "1.0",
                        "295",
                        "0",
                        "296",
                        "0",
                        "143",
                        "3.75",
                        "271",
                        "0",
                        "272",
                        "9",
                        "273",
                        "9",
                    }
                ) + "\n";
            int objects = text.IndexOf("OBJECTS\n", StringComparison.Ordinal);
            int end = text.IndexOf("  0\nENDSEC\n", objects, StringComparison.Ordinal);
            text = text.Substring(0, end) + style + text.Substring(end);
            return CadDocument.From(DxfDatabase.Parse(text), "mleader.dxf");
        }

        public static CadCommandSession Session(CadDocument document) =>
            WaitingRoutines.Track(new CadCommandSession(document) { PickAperture = 1.0 });

        /// <summary>
        /// Feeds inputs in turn: a text starts a command when none runs and answers the prompt when one does, a point is
        /// picked, a string array is a selection, <see cref="Enter"/> and <see cref="Esc"/> are the keys.
        /// </summary>
        public static CadCommandSession Run(CadCommandSession session, params object[] inputs)
        {
            foreach (object input in inputs)
            {
                switch (input)
                {
                    case string text:
                        session.Feed(text);
                        break;
                    case GeoPoint2 point:
                        session.Feed(point);
                        break;
                    case string[] handles:
                        session.FeedSelection(handles);
                        break;
                    default:
                        if (ReferenceEquals(input, Enter))
                        {
                            session.Enter();
                        }
                        else if (ReferenceEquals(input, Esc))
                        {
                            session.Escape();
                        }
                        else
                        {
                            throw new ArgumentException("An input of no known kind: " + input);
                        }

                        break;
                }
            }

            return session;
        }

        /// <summary>Checks that the drawing reads back from its text as it is: the same text again, every entity's shapes read.</summary>
        public static void AssertReopens(CadDocument document)
        {
            string text = document.Database.ToText();
            DxfDatabase again = DxfDatabase.Parse(text);
            Assert.Equal(text, again.ToText());

            foreach (DxfEntity entity in again.ModelSpace)
            {
                Assert.NotNull(entity.GetGeometry());
            }
        }

        public static T Entity<T>(CadDocument document, string handle)
            where T : DxfEntity
        {
            DxfEntity entity = document.FindEntity(handle);
            Assert.NotNull(entity);
            return Assert.IsAssignableFrom<T>(entity);
        }

        /// <summary>The entities of a space of a kind, in the order of the space.</summary>
        public static List<T> All<T>(CadSpace space)
            where T : DxfEntity
        {
            var found = new List<T>();

            foreach (DxfEntity entity in space.Block)
            {
                if (entity is T kind)
                {
                    found.Add(kind);
                }
            }

            return found;
        }

        public static void Near(GeoPoint3 expected, GeoPoint3 actual, double tolerance = 1E-9)
        {
            Assert.True(expected.DistanceTo(actual) <= tolerance, "Expected " + expected + " but was " + actual + ".");
        }

        public static void Near(GeoPoint2 expected, GeoPoint2 actual, double tolerance = 1E-9)
        {
            Assert.True((expected - actual).Length <= tolerance, "Expected " + expected + " but was " + actual + ".");
        }

        #region Edit-call twins (EditorEditTests)

        /// <summary>
        /// The ENTITIES section of a drawing as its groups: each group's code, then its value, both trimmed. What the drawing a
        /// command made and the drawing the call that does the same made are compared by.
        /// </summary>
        public static List<string> EntitiesOf(CadDocument document)
        {
            string[] lines = document.Database.ToText().Replace("\r\n", "\n").Split('\n');
            var groups = new List<string>();
            bool inside = false;

            for (int i = 0; i + 1 < lines.Length; i += 2)
            {
                string code = lines[i].Trim();
                string value = lines[i + 1].Trim();

                if (inside && code == "0" && value == "ENDSEC")
                {
                    break;
                }

                if (inside)
                {
                    groups.Add(code);
                    groups.Add(value);
                }
                else if (code == "2" && value == "ENTITIES")
                {
                    inside = true;
                }
            }

            return groups;
        }

        /// <summary>
        /// Checks that two drawings hold the same ENTITIES: the same groups in the same order, a number within the tolerance's
        /// <c>EqualPoint</c> of the other (a turn typed in degrees and one given in radians differ in the last digit), any other
        /// value as it is. Says the first differences, with their group numbers, where they are not.
        /// </summary>
        public static void AssertSameEntities(CadDocument expected, CadDocument actual, Tolerance tolerance)
        {
            List<string> a = EntitiesOf(expected);
            List<string> b = EntitiesOf(actual);
            var differences = new List<string>();

            if (a.Count != b.Count)
            {
                differences.Add("groups: " + a.Count / 2 + " against " + b.Count / 2);
            }

            for (int i = 0; i + 1 < Math.Min(a.Count, b.Count) && differences.Count < 6; i += 2)
            {
                bool same =
                    a[i] == b[i]
                    && (
                        a[i + 1] == b[i + 1]
                        || (
                            double.TryParse(a[i + 1], NumberStyles.Float, CultureInfo.InvariantCulture, out double x)
                            && double.TryParse(b[i + 1], NumberStyles.Float, CultureInfo.InvariantCulture, out double y)
                            && Math.Abs(x - y) <= tolerance.EqualPoint
                        )
                    );

                if (!same)
                {
                    differences.Add(
                        "group " + i / 2 + " (code " + a[i] + "): " + a[i + 1] + " against " + b[i + 1] + "."
                    );
                }
            }

            Assert.Empty(differences);
        }

        /// <summary>
        /// A drawing of R2013 to be mirrored, with MIRRTEXT as given: a TEXT "ABC" 2.5 high at (10, 20) turned 30 degrees, a hatch
        /// of the user pattern at 30 degrees, its lines 2 apart, over (42, 50) to (58, 58), and a rotated dimension from (0, -20)
        /// to (30, -20) with its line through (15, -30), made by DIMLINEAR. The handles come in that order.
        /// </summary>
        public static CadDocument MirrorSources(bool mirrtext, out string[] handles)
        {
            CadDocument document = Blank();
            document.Database.Mirrtext = mirrtext;
            var text = new DxfDBText();
            document.Database.ModelSpace.AppendEntity(text);
            text.TextString = "ABC";
            text.Height = 2.5;
            text.Position = new GeoPoint3(10, 20, 0);
            text.Rotation = Math.PI / 6.0;
            var hatch = new DxfHatch();
            hatch.SetHatchPattern(DxfHatchPatternType.UserDefined, "_USER");
            hatch.PatternAngle = Math.PI / 6.0;
            hatch.PatternSpace = 2.0;
            hatch.AppendLoop(
                DxfHatchLoopTypes.External,
                new[] { new GeoPoint2(42, 50), new GeoPoint2(58, 50), new GeoPoint2(58, 58), new GeoPoint2(42, 58) },
                null
            );
            document.Database.ModelSpace.AppendEntity(hatch);
            Run(Session(document), "DIMLINEAR", P(0, -20), P(30, -20), P(15, -30));
            DxfRotatedDimension dimension = All<DxfRotatedDimension>(document.ModelSpace).Single();
            handles = new[] { text.Handle, hatch.Handle, dimension.Handle };
            return document;
        }

        /// <summary>
        /// A drawing of R2013 with a line from (0, 0) to (100, 0) and the dimension DIMLINEAR makes of it by selecting it, tied to
        /// the line's two ends (a measurement of 100, its line through (50, -30)). The handles are the line's and the dimension's.
        /// </summary>
        public static CadDocument TiedDimension(out string[] handles)
        {
            CadDocument document = Blank();
            CadCommandSession session = Session(document);
            Run(session, "LINE", P(0, 0), P(100, 0), Enter);
            string line = All<DxfLine>(document.ModelSpace).Single().Handle;
            Run(session, "DIMLINEAR", Enter, new[] { line }, P(50, -30));
            handles = new[] { line, All<DxfRotatedDimension>(document.ModelSpace).Single().Handle };
            return document;
        }

        /// <summary>Adds to a plan the closed polyline of a plate on layer 0: a rectangle from (0, 300) to (100, 350), first corner (0, 300).</summary>
        public static DxfPolyline AddPlate(CadDocument document)
        {
            var plate = new DxfPolyline();
            plate.AddVertexAt(0, new GeoPoint2(0, 300), 0.0, 0.0, 0.0);
            plate.AddVertexAt(1, new GeoPoint2(100, 300), 0.0, 0.0, 0.0);
            plate.AddVertexAt(2, new GeoPoint2(100, 350), 0.0, 0.0, 0.0);
            plate.AddVertexAt(3, new GeoPoint2(0, 350), 0.0, 0.0, 0.0);
            plate.Closed = true;
            document.Database.ModelSpace.AppendEntity(plate);
            return plate;
        }

        /// <summary>Adds to the block BOX of a plan a line from (0, 5) to (10, 5), which lies inside a block and in no space.</summary>
        public static DxfLine AddLineInBox(CadDocument document)
        {
            var line = new DxfLine(new GeoPoint3(0, 5, 0), new GeoPoint3(10, 5, 0));
            document.Database.BlockTable["BOX"].AppendEntity(line);
            return line;
        }

        /// <summary>Adds to the first layout of a drawing a line from (0, 0) to (100, 0) and gives it, with the layout's space.</summary>
        public static DxfLine AddLineOnSheet(CadDocument document, out CadSpace sheet)
        {
            sheet = document.Spaces.First(s => !s.IsModel);
            var line = new DxfLine(new GeoPoint3(0, 0, 0), new GeoPoint3(100, 0, 0));
            sheet.Block.AppendEntity(line);
            return line;
        }

        /// <summary>The owner "Model", which gives every object of its own the rule it was made with and takes no edit itself.</summary>
        internal sealed class ModelOwner : ICadEntityOwner
        {
            private readonly CadEditRule _rule;

            public ModelOwner(CadEditRule rule)
            {
                _rule = rule;
            }

            public string Name => "Model";

            public CadEditRule GetEditRule(DxfEntity entity) => _rule;

            public bool TryApply(CadDocument document, CadEdit edit) => false;
        }

        /// <summary>
        /// A drawing of two lines in model space, A1 from (0, 0) to (100, 0) which the owner "Model" holds under the rule given,
        /// and A2 from (0, 50) to (100, 50) which nothing owns.
        /// </summary>
        public static CadDocument OwnedLines(CadEditRule rule)
        {
            DxfDatabase database = TestDrawings.Drawing(
                null,
                null,
                TestDrawings.Line(0, 0, 100, 0, handle: "A1"),
                TestDrawings.Line(0, 50, 100, 50, handle: "A2")
            );
            new CadOwnerTag("Model", "view-1").WriteTo(
                database.ModelSpace.OfType<DxfLine>().Single(e => e.Handle == "A1")
            );
            CadDocument document = CadDocument.From(database, "owned.dxf");
            document.Owners.Register(new ModelOwner(rule));
            return document;
        }

        #endregion
    }
}
