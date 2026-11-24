using System;
using System.IO;
using System.Linq;
using System.Text;
using GeometryHelper;
using GeometryHelper.Dxf;
using GeometryHelper.Geometry;
using Xunit;
using static OpenCAD2D.UnitTest.EditingDrawings;

namespace OpenCAD2D.UnitTest
{
    /// <summary>
    /// The built-in names of the program before it was called OpenCAD2D, still read: the plotters GeometryHelper PDF, PNG and
    /// JPG a drawing or page setup names plot to OpenCAD2D PDF, PNG and JPG and are saved by the new names; the linetype file
    /// GeometryHelper.lin and the pattern file GeometryHelper.pat are the built-in sets; the sample template
    /// GeometryHelper-mm.dwt a settings file names for QNEW finds OpenCAD2D-mm.dwt. The built-in font is OpenCAD2D Simplex.
    /// </summary>
    public sealed class OldBuiltInNamesTests : IDisposable
    {
        private readonly string _folder = Path.Combine(
            Path.GetTempPath(),
            "oc-oldnames-" + Guid.NewGuid().ToString("N")
        );

        public OldBuiltInNamesTests()
        {
            Directory.CreateDirectory(_folder);
        }

        public void Dispose()
        {
            Directory.Delete(_folder, true);
        }

        [Fact]
        public void ALayoutNamingTheOldPngDevice_IsReadAsOpenCad2dPng_OnItsImageSize()
        {
            CadDocument document = PlotTests.Plan();
            var settings = new CadPlotSettings();
            settings.UseDevice(CadPlotSettings.PngDevice);
            Assert.True(settings.ApplyTo(document, document.ModelSpace).Succeeded);
            DxfLayout model = document.Database.Layouts.Single(l => l.ModelType);
            model.PlotConfigurationName = "GeometryHelper PNG";

            CadPlotSettings read = CadPlotSettings.Read(document, document.ModelSpace);

            Assert.Equal("OpenCAD2D PNG", read.DeviceName);
            Assert.True(read.IsRaster);
            Assert.True(read.Paper.IsPixels);
            Assert.Equal(1920.0, Math.Max(read.Paper.Width, read.Paper.Height));
            Assert.Equal("OpenCAD2D PNG", read.SavedPlotter);
        }

        [Fact]
        public void APageSetupNamingTheOldPdfPlotter_PlotsWithOpenCad2dPdf_AndIsNoPrinter()
        {
            CadDocument document = PlotTests.Plan();
            Assert.True(new CadPlotSettings().SaveAsPageSetup(document, "Old").Succeeded);
            ((DxfPlotSettings)document.Database.PlotSettingsDictionary["Old"]).PlotConfigurationName =
                "geometryhelper pdf";

            CadPlotSettings read = CadPlotSettings.ReadPageSetup(document, document.ModelSpace, "Old");

            Assert.Equal("OpenCAD2D PDF", read.SavedPlotter);
            Assert.Equal("OpenCAD2D PDF", read.DeviceName);
            Assert.False(read.IsPrinter);
        }

        [Fact]
        public void TheOldDeviceNames_ChooseTheDevicesOfTheirNamesNow()
        {
            var settings = new CadPlotSettings();

            settings.UseDevice("GeometryHelper JPG");
            Assert.Equal("OpenCAD2D JPG", settings.DeviceName);
            Assert.True(settings.Paper.IsPixels);

            settings.UseDevice("GeometryHelper PDF");
            Assert.Equal("OpenCAD2D PDF", settings.DeviceName);
            Assert.False(settings.Paper.IsPixels);

            settings.DeviceName = "GeometryHelper PNG";
            Assert.Equal("OpenCAD2D PNG", settings.DeviceName);
            Assert.True(CadPlotSettings.IsRasterDevice("GeometryHelper JPG"));
            Assert.Throws<ArgumentException>(() => settings.UseDevice("GeometryHelper"));
        }

        [Fact]
        public void ADrawingNamingTheOldPlotters_IsSavedWithTheNewNames_AndAPrinterIsLeftAsItIs()
        {
            CadDocument document = PlotTests.Plan();
            Assert.True(new CadPlotSettings().SaveAsPageSetup(document, "Old").Succeeded);
            Assert.True(new CadPlotSettings().SaveAsPageSetup(document, "Office").Succeeded);
            document.Database.Layouts.Single(l => l.ModelType).PlotConfigurationName = "GeometryHelper PDF";
            ((DxfPlotSettings)document.Database.PlotSettingsDictionary["Old"]).PlotConfigurationName =
                "GeometryHelper JPG";
            ((DxfPlotSettings)document.Database.PlotSettingsDictionary["Office"]).PlotConfigurationName =
                "Office printer";
            string file = Path.Combine(_folder, "old-plotters.dxf");

            document.SaveAs(file);
            DxfDatabase read = CadDrawingFile.Open(file);

            Assert.Equal("OpenCAD2D PDF", read.Layouts.Single(l => l.ModelType).PlotConfigurationName);
            Assert.Equal("OpenCAD2D JPG", ((DxfPlotSettings)read.PlotSettingsDictionary["Old"]).PlotConfigurationName);
            Assert.Equal(
                "Office printer",
                ((DxfPlotSettings)read.PlotSettingsDictionary["Office"]).PlotConfigurationName
            );
        }

        [Fact]
        public void TheOldLinetypeFileName_TypedAtLinetypeLoad_LoadsTheBuiltInSet()
        {
            CadDocument document = CadDocument.From(new DxfDatabase("AC1027"), "blank.dxf");
            CadCommandSession session = Session(document);
            session.Tolerance = Tolerance.Default;

            Run(session, "-LINETYPE", "L", "DASHED");
            Assert.Equal("Enter linetype file to search <OpenCAD2D.lin>:", session.PromptText);
            Run(session, "GeometryHelper", Enter);

            Assert.Contains("Linetype DASHED loaded.", session.History);
            Assert.Equal(19.05, document.Database.LinetypeTable["DASHED"].PatternLength, 12);
        }

        [Fact]
        public void TheOldPatternFileName_GivesTheBuiltInPattern()
        {
            var database = new DxfDatabase("AC1027");
            GeoPoint3[] corners =
            {
                new GeoPoint3(0, 0, 0),
                new GeoPoint3(30, 0, 0),
                new GeoPoint3(30, 30, 0),
                new GeoPoint3(0, 30, 0),
            };

            for (int i = 0; i < 4; i++)
            {
                database.ModelSpace.AppendEntity(new DxfLine(corners[i], corners[(i + 1) % 4]));
            }

            CadDocument document = CadDocument.From(database, "hatch.dxf");
            document.HatchSettings = new CadHatchSettings
            {
                PatternName = "ANSI31",
                PatternFile = "GeometryHelper.pat",
            };
            CadCommandSession session = Session(document);
            session.Tolerance = Tolerance.Default;

            Run(session, "HATCH", P(5, 5), Enter);

            DxfHatch hatch = Assert.Single(All<DxfHatch>(document.ModelSpace));
            Assert.Equal("ANSI31", hatch.PatternName);
            Assert.Equal(3.175 / Math.Sqrt(2.0), -hatch.GetPatternDefinitionAt(0).OffsetX, 9);
        }

        [Fact]
        public void TheOldSampleTemplateName_SavedForQnew_FindsOpenCad2dMm()
        {
            string templates = CadSupportFolders.Default.FolderOf(CadSupportKind.DrawingTemplates);
            string shipped = Path.Combine(templates, "OpenCAD2D-mm.dwt");
            string file = Path.Combine(_folder, "settings.json");
            File.WriteAllText(file, "{\"templates\":{\"qnew\":\"GeometryHelper-mm.dwt\"}}", new UTF8Encoding(false));

            GlobalSettings read = GlobalSettings.Load(file);

            Assert.Equal("GeometryHelper-mm.dwt", read.QnewTemplate);
            Assert.Equal(shipped, CadTemplate.Find(read.QnewTemplate, templates));
            Assert.Equal(shipped, CadTemplate.Find(Path.Combine(templates, "GeometryHelper-mm"), _folder));
            Assert.Null(CadTemplate.Find("GeometryHelper-mm.dwt", _folder));
        }

        [Fact]
        public void ATemplateOfTheOldSampleNameOfTheUsersOwn_IsFoundBeforeTheSample()
        {
            string own = Path.Combine(_folder, "GeometryHelper-mm.dwt");
            File.Copy(
                Path.Combine(CadSupportFolders.Default.FolderOf(CadSupportKind.DrawingTemplates), "OpenCAD2D-mm.dwt"),
                own
            );
            File.Copy(own, Path.Combine(_folder, "OpenCAD2D-mm.dwt"));

            Assert.Equal(own, CadTemplate.Find("GeometryHelper-mm", _folder));
        }

        [Fact]
        public void TheBuiltInFont_IsOpenCad2dSimplex_InItsTitle_AndInTheShippedShxFile()
        {
            Assert.Equal("OpenCAD2D Simplex", CadFonts.BuiltInTitle);
            string shipped = Encoding.ASCII.GetString(
                File.ReadAllBytes(
                    Path.Combine(
                        CadSupportFolders.Default.FolderOf(CadSupportKind.Fonts),
                        CadFonts.BuiltInName + ".shx"
                    )
                )
            );

            Assert.Contains("OpenCAD2D Simplex\0", shipped);
            Assert.DoesNotContain("GeometryHelper", shipped);
        }
    }
}
