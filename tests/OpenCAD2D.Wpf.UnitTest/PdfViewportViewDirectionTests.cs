using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;
using GeometryHelper.Dxf;
using GeometryHelper.Geometry;
using Xunit;

namespace OpenCAD2D.Wpf.UnitTest
{
    /// <summary>
    /// A layout plotted to PDF whose viewport looks along a direction that is not the world's Z: model space is plotted
    /// through the viewport projected, as the display draws it, its hyperlink over the box of the projected line.
    /// </summary>
    public class PdfViewportViewDirectionTests
    {
        /// <summary>
        /// A metric drawing whose Layout1, a sheet of 420 by 297, has one viewport 200 by 150 about (150, 100) showing model
        /// space 300 high (1:2) about the view's origin, looking from the front, along (0, -1, 0): a model point (x, y, z) stands at
        /// (150 + x / 2, 100 + z / 2).
        /// </summary>
        private static DxfDatabase FrontView(out DxfLayout layout)
        {
            DxfDatabase db = CadTemplate.CreateMetric();
            layout = db.Layouts.First(l => l.LayoutName == "Layout1");
            layout.Initialize();
            var frame = new DxfViewport
            {
                CenterPoint = new GeoPoint3(150, 100, 0),
                Width = 200,
                Height = 150,
                ViewHeight = 300,
                ViewCenter = new GeoPoint2(0, 0),
            };
            layout.BlockTableRecord.AppendEntity(frame);
            frame.On = true;
            frame.ViewDirection = new GeoVector3(0, -1, 0);
            return db;
        }

        private static DxfLine Link(DxfBlockTableRecord owner, GeoPoint3 from, GeoPoint3 to, string address)
        {
            var line = new DxfLine(from, to);
            owner.AppendEntity(line);
            line.Hyperlinks.Add(new DxfHyperLink { Name = address, Description = address });
            return line;
        }

        /// <summary>Plots the first layout to a PDF and gives the rectangles of the links the page carries, by address.</summary>
        private static Dictionary<string, double[]> Export(DxfDatabase database)
        {
            CadDocument document = CadDocument.From(database, "sheet.dxf");
            CadSpace space = document.Spaces.First(s => !s.IsModel);
            CadPlotSettings settings = CadPlotSettings.Read(document, space);
            settings.UseDevice(CadPlotSettings.PdfDevice);
            settings.Area = CadPlotArea.Window;
            settings.Window = new DisplayBox(0, 0, 420, 297);
            settings.FitToPaper = true;
            settings.PdfHyperlinks = true;
            string text;

            using (var memory = new MemoryStream())
            {
                CadPdfWriter.Write(memory, document, space, settings, DisplayBox.Empty);
                text = Encoding.GetEncoding(28591).GetString(memory.ToArray());
            }

            var links = new Dictionary<string, double[]>();

            foreach (Match match in Regex.Matches(text, @"/Rect \[([^\]]*)\][^>]*?/URI \(([^)]*)\)"))
            {
                links[match.Groups[2].Value] = match
                    .Groups[1]
                    .Value.Split(new[] { ' ' }, StringSplitOptions.RemoveEmptyEntries)
                    .Select(v => double.Parse(v, CultureInfo.InvariantCulture))
                    .ToArray();
            }

            return links;
        }

        [Fact]
        public void AModelLineSeenFromTheFront_IsPlottedWhereItsProjectionStands_NotWhereItsPlanDoes()
        {
            DxfDatabase db = FrontView(out DxfLayout layout);

            // The model line (0, 0, 0) to (100, 200, 100) is seen from the front from (150, 100) to (200, 150) on the sheet,
            // the depth of 200 dropped; in plan it would run to (200, 200).
            Link(db.ModelSpace, new GeoPoint3(0, 0, 0), new GeoPoint3(100, 200, 100), "https://example.com/model.pdf");
            Link(
                layout.BlockTableRecord,
                new GeoPoint3(150, 100, 0),
                new GeoPoint3(200, 150, 0),
                "https://example.com/direct.pdf"
            );

            Dictionary<string, double[]> links = Export(db);

            double[] direct = links["https://example.com/direct.pdf"];
            double[] seen = links["https://example.com/model.pdf"];
            Assert.Equal(4, seen.Length);

            for (int i = 0; i < 4; i++)
            {
                Assert.Equal(direct[i], seen[i], 2);
            }
        }
    }
}
