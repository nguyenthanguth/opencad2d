using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using GeometryHelper;
using GeometryHelper.Dxf;
using GeometryHelper.Geometry;
using Xunit;
using static OpenCAD2D.UnitTest.EditingDrawings;

namespace OpenCAD2D.UnitTest
{
    /// <summary>
    /// MLEDIT's CA on open and closed multilines picked near mitred corners, against the records AutoCAD 2021 left (a second,
    /// random set after <see cref="MlineClosedCutMitreTests"/>: rectilinear shapes of 3 to 6 vertices, the picks 2 to 28 from a
    /// corner or anywhere on a segment; a closed shape is written with "c:" in front of its points). An element a pick lies
    /// off (the inner element at a concave corner starts and ends away from the vertices) is cut only at the other pick: from
    /// it on to the end of its segment, or up to it on the last segment of a closed multiline; where both picks lie off it
    /// on one segment, the element is left whole. Each row is the option, the points of the multiline, the two picks and the
    /// record AutoCAD left, its flags and the vertices with each element's stretches after the offset in braces.
    /// </summary>
    public class MlineCutOffElementTests
    {
        private static readonly string[] Cases =
        {
            "CA|c:0,0;120,0;120,60;70,60;70,120;0,120|120,34|0,14|f3 (0,0){}{0,20} (120,0){34}{54} (120,60){0}{0} (70,60){0}{0} (70,120){0}{0} (0,120){0,106}{0,126}",
            "CA|c:0,0;100,0;100,50;0,50;0,100;100,100|100,9|15.147,15.147|f3 (0,0){}{0,48.2843} (100,0){9}{29} (100,50){0}{0} (0,50){0}{0} (0,100){0}{0} (100,100){0,120.0003}{0,71.716}",
            "CA|0,0;100,0;100,50;0,50;0,100;100,100|0,63|0,97|f1 (0,0){0}{0} (100,0){0}{0} (100,50){0}{0} (0,50){0,13,47}{0} (0,100){0}{0} (100,100){0}{0}",
            "CA|0,0;100,0;100,50;0,50;0,100;100,100|0,57|0,89|f1 (0,0){0}{0} (100,0){0}{0} (100,50){0}{0} (0,50){0,7,39}{0} (0,100){0}{0} (100,100){0}{0}",
            "CA|c:0,0;100,0;100,50;0,50;0,100;100,100|0,63|12,100|f3 (0,0){0}{0} (100,0){0}{0} (100,50){0}{0} (0,50){0,13}{0} (0,100){12}{0} (100,100){0}{0}",
            "CA|0,0;100,0;100,50;0,50;0,100;100,100|18,100|7,50|f1 (0,0){0}{0} (100,0){0}{0} (100,50){0,93}{0} (0,50){}{0} (0,100){18}{0} (100,100){0}{0}",
            "CA|c:0,0;100,0;100,50;50,50;50,100;0,100|34,100|55,50|f3 (0,0){0}{0} (100,0){0}{0} (100,50){0,45}{0} (50,50){}{0} (50,100){16}{0,36} (0,100){0}{0}",
            "CA|c:0,0;120,0;120,60;70,60;70,120;0,120|76,60|70,108|f3 (0,0){0}{0} (120,0){0}{0} (120,60){0,44}{0} (70,60){48}{0,28} (70,120){0}{0} (0,120){0}{0}",
            "CA|c:0,0;120,0;120,60;70,60;70,120;0,120|0,6|86,60|f3 (0,0){}{0,20} (120,0){}{} (120,60){34}{} (70,60){0}{0} (70,120){0}{0} (0,120){0,114}{0,134}",
            "CA|c:0,0;100,0;100,50;50,50;50,100;0,100|55,50|0,74|f3 (0,0){0}{0} (100,0){0}{0} (100,50){0,45}{0} (50,50){}{0} (50,100){}{0} (0,100){26}{46}",
            "CA|c:0,0;100,0;100,50;50,50;50,100;0,100|50,96|68,50|f3 (0,0){0}{0} (100,0){0}{0} (100,50){0,32}{0} (50,50){46}{0,26} (50,100){0}{0} (0,100){0}{0}",
            "CA|c:0,0;100,0;100,50;50,50;50,100;0,100|50,57|50,90|f3 (0,0){0}{0} (100,0){0}{0} (100,50){0}{0} (50,50){0,7,40}{0,20} (50,100){0}{0} (0,100){0}{0}",
            "CA|c:0,0;100,0;100,50;0,50;0,100;100,100|90,100|82,0|f3 (0,0){82}{0,48.2843,130.2843} (100,0){0}{0} (100,50){0}{0} (0,50){0}{0} (0,100){0,90}{0} (100,100){}{0}",
            "CA|c:0,0;100,0;100,50;0,50;0,100;100,100|51.917,51.917|11,100|f3 (0,0){0}{0} (100,0){0}{0} (100,50){0}{0} (0,50){0}{0} (0,100){0,11}{0} (100,100){67.9996}{19.7154}",
            "CA|c:0,0;120,0;120,60;70,60;70,120;0,120|84,60|70,117|f3 (0,0){0}{0} (120,0){0}{0} (120,60){0,36}{0} (70,60){57}{0,37} (70,120){0}{0} (0,120){0}{0}",
            "CA|c:0,0;120,0;120,60;70,60;70,120;0,120|70,74|55,120|f3 (0,0){0}{0} (120,0){0}{0} (120,60){0}{0} (70,60){0,14}{0} (70,120){15}{0,35} (0,120){0}{0}",
            "CA|c:0,0;100,0;100,50;50,50;50,100;0,100|100,35|0,39|f3 (0,0){}{0,20} (100,0){35}{55} (100,50){0}{0} (50,50){0}{0} (50,100){0}{0} (0,100){0,61}{0,81}",
            "CA|c:0,0;100,0;100,50;0,50;0,100;100,100|55,100|72,0|f3 (0,0){72}{0,48.2843,120.2843} (100,0){0}{0} (100,50){0}{0} (0,50){0}{0} (0,100){0,55}{0} (100,100){}{0}",
            "CA|c:0,0;100,0;100,50;0,50;0,100;100,100|17.269,17.269|100,20|f3 (0,0){}{0,48.2843} (100,0){20}{40} (100,50){0}{0} (0,50){0}{0} (0,100){0}{0} (100,100){0,116.9993}{0,68.715}",
            "CA|c:0,0;100,0;100,50;0,50;0,100;100,100|7,100|5.248,5.248|f3 (0,0){0}{0} (100,0){0}{0} (100,50){0}{0} (0,50){0}{0} (0,100){0,7}{0} (100,100){133.9996}{85.7153}",
            "CA|c:0,0;120,0;120,60;70,60;70,120;0,120|0,17|70,72|f3 (0,0){0}{0} (120,0){0}{0} (120,60){0}{0} (70,60){0,12}{0} (70,120){}{0} (0,120){103}{123}",
            "CA|c:0,0;100,0;100,50;50,50;50,100;0,100|0,15|50,54|f3 (0,0){0}{0} (100,0){0}{0} (100,50){0}{0} (50,50){0,4}{0} (50,100){}{0} (0,100){85}{105}",
            "CA|c:0,0;120,0;120,60;70,60;70,120;0,120|111,60|0,3|f3 (0,0){}{0,20} (120,0){}{} (120,60){9}{29} (70,60){0}{0} (70,120){0}{0} (0,120){0,117}{0,137}",
            "CA|c:0,0;100,0;100,50;50,50;50,100;0,100|0,33|100,14|f3 (0,0){}{0,20} (100,0){14}{34} (100,50){0}{0} (50,50){0}{0} (50,100){0}{0} (0,100){0,67}{0,87}",
            "CA|c:0,0;120,0;120,60;70,60;70,120;0,120|88,60|0,47|f3 (0,0){0}{0} (120,0){0}{0} (120,60){0,32}{0} (70,60){}{0} (70,120){}{0} (0,120){73}{93}",
            "CS|0,0;100,0;100,50;50,50;50,100;0,100|23,0|50,89|f1 (0,0){0,23}{0} (100,0){}{0} (100,50){}{0} (50,50){39}{0} (50,100){0}{0} (0,100){0}{0}",
            "CA|c:0,0;100,0;100,100;0,100|21,100|18,100|f3 (0,0){0}{0} (100,0){0}{0} (100,100){0,79,82}{0,99,102} (0,100){0}{0}",
            "CA|0,0;100,0;100,100|11,0|100,61|f1 (0,0){0,11}{0,11} (100,0){61}{81} (100,100){0}{0}",
            "CA|c:0,0;100,0;100,100|66.059,66.059|14,0|f3 (0,0){14}{0,48.2843,62.2843} (100,0){0}{0} (100,100){0,47.9998}{0,96.2841}",
            "CA|0,0;120,0;120,60;70,60;70,120;0,120|120,10|120,57|f1 (0,0){0}{0} (120,0){0,10,57}{0,30,77} (120,60){0}{0} (70,60){0}{0} (70,120){0}{0} (0,120){0}{0}",
            "CA|c:0,0;100,0;100,50;50,50;50,100;0,100|0,46|0,50|f3 (0,0){0}{0} (100,0){0}{0} (100,50){0}{0} (50,50){0}{0} (50,100){0}{0} (0,100){0,50,54}{0,70,74}",
            "CA|0,0;100,0;100,50;50,50;50,100;0,100|95,0|52,0|f1 (0,0){0,52,95}{0,52,95} (100,0){0}{0} (100,50){0}{0} (50,50){0}{0} (50,100){0}{0} (0,100){0}{0}",
            "CS|0,0;100,0;100,50;50,50;50,100;0,100|31,100|40,100|f1 (0,0){0}{0} (100,0){0}{0} (100,50){0}{0} (50,50){0}{0} (50,100){0,10,19}{0} (0,100){0}{0}",
            "CA|c:0,0;120,0;120,60;70,60;70,120;0,120|114,60|109,0|f3 (0,0){0,109}{0,129} (120,0){}{} (120,60){6}{26} (70,60){0}{0} (70,120){0}{0} (0,120){0}{0}",
            "CS|c:0,0;100,0;100,50;0,50;0,100;100,100|73,100|0,76|f3 (0,0){0}{0} (100,0){0}{0} (100,50){0}{0} (0,50){0,26}{0} (0,100){73}{0} (100,100){0}{0}",
            "CA|c:0,0;100,0;100,100;0,100|27,0|13,100|f3 (0,0){27}{0,20,47} (100,0){0}{0} (100,100){0,87}{0,107} (0,100){}{}",
            "CA|0,0;100,0;100,100|72,0|92,0|f1 (0,0){0,72,92}{0,72,92} (100,0){0}{0} (100,100){0}{0}",
            "CA|0,0;100,0;100,50;50,50;50,100;0,100|100,25|89,50|f1 (0,0){0}{0} (100,0){0,25}{0,45} (100,50){11}{31} (50,50){0}{0} (50,100){0}{0} (0,100){0}{0}",
            "CA|c:0,0;120,0;120,60;70,60;70,120;0,120|0,106|0,17|f3 (0,0){0}{0} (120,0){0}{0} (120,60){0}{0} (70,60){0}{0} (70,120){0}{0} (0,120){0,14,103}{0,34,123}",
            "CA|0,0;100,0;100,50;0,50;0,100;100,100|0,74|23,0|f1 (0,0){0,23}{0,23} (100,0){}{} (100,50){}{} (0,50){24}{4} (0,100){0}{0} (100,100){0}{0}",
            "CS|0,0;100,0;100,50;50,50;50,100;0,100|18,0|50,74|f1 (0,0){0,18}{0} (100,0){}{0} (100,50){}{0} (50,50){24}{0} (50,100){0}{0} (0,100){0}{0}",
        };

        public static IEnumerable<object[]> Table() => Cases.Select(c => new object[] { c });

        private const int Elements = 2;

        private static GeoPoint2 Point(string text)
        {
            string[] xy = text.Split(',');
            return P(
                double.Parse(xy[0], CultureInfo.InvariantCulture),
                double.Parse(xy[1], CultureInfo.InvariantCulture)
            );
        }

        /// <summary>The parameters after the offset that an element holds at a vertex, as the record writes them.</summary>
        private static double[] Tail(DxfUnknownEntity mline, int vertex, int element)
        {
            int at = -1;
            int each = -1;
            var found = new List<double>();

            foreach (DxfTypedValue group in mline.Groups)
            {
                if (group.TypeCode == 11)
                {
                    at++;
                    each = -1;
                }
                else if (group.TypeCode == 74)
                {
                    each++;
                }
                else if (group.TypeCode == 41 && at == vertex && each == element)
                {
                    found.Add(double.Parse(group.Value.Trim(), CultureInfo.InvariantCulture));
                }
            }

            return found.Skip(1).ToArray();
        }

        private static string Number(double value) =>
            (Math.Round(value, 4) + 0.0).ToString("0.####", CultureInfo.InvariantCulture);

        /// <summary>The multiline as the rows write it: its flags, then each vertex with its elements' stretches.</summary>
        private static string Describe(DxfUnknownEntity mline)
        {
            var builder = new DxfMlineBuilder(mline);
            int flags =
                1
                | (builder.IsClosed ? 2 : 0)
                | (builder.SuppressStartCaps ? 4 : 0)
                | (builder.SuppressEndCaps ? 8 : 0);
            var vertices = new List<string>();

            for (int i = 0; i < builder.Points.Count; i++)
            {
                string text = "(" + Number(builder.Points[i].X) + "," + Number(builder.Points[i].Y) + ")";

                for (int e = 0; e < Elements; e++)
                {
                    text += "{" + string.Join(",", Tail(mline, i, e).Select(Number)) + "}";
                }

                vertices.Add(text);
            }

            return "f" + flags + " " + string.Join(" ", vertices);
        }

        [Theory]
        [MemberData(nameof(Table))]
        public void ACutOfAMultilineNearMitredCorners_LeavesWhatAutoCADLeaves(string row)
        {
            string[] parts = row.Split(new[] { '|' }, 5);
            CadDocument document = Blank();
            CadCommandSession session = Session(document);
            session.Tolerance = Tolerance.Default;
            session.PickAperture = 3.0;

            bool closed = parts[1].StartsWith("c:", StringComparison.Ordinal);
            string points = closed ? parts[1].Substring(2) : parts[1];

            Run(session, "MLINE");
            Run(
                session,
                points.Split(';').Select(p => (object)Point(p)).Concat(new object[] { closed ? "C" : Enter }).ToArray()
            );
            Run(session, "MLEDIT", parts[0], Point(parts[2]), Point(parts[3]), Enter);

            DxfUnknownEntity mline = All<DxfUnknownEntity>(document.ModelSpace).Single(m => m.DxfName == "MLINE");

            Assert.Equal(parts[4], Describe(mline));
        }
    }
}
