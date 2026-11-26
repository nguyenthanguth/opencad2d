using System;
using GeometryHelper.Dxf;

namespace OpenCAD2D
{
    /// <summary>
    /// The factor that takes the lengths of a drawing to millimetres, as the export for AI pipelines (<see cref="CadFlatten"/>,
    /// <see cref="CadDrawingJson"/>, <see cref="CadDrawingGraph"/>) reads them: the drawing's INSUNITS for model space, the
    /// plot paper units of a layout for its paper space. A drawing in inches has 25.4; one of metres, 1000; one of no unit
    /// (INSUNITS 0) is left as it is, factor 1, and says so.
    /// </summary>
    public static class CadUnitScale
    {
        /// <summary>
        /// Gets the millimetres in a unit of INSUNITS, as the DXF reference lists them and the reader takes them.
        /// </summary>
        /// <param name="units">The unit.</param>
        /// <returns>The millimetres in one of it: 25.4 for inches, 1000 for metres; 0 for <see cref="DxfUnits.Undefined"/>
        /// or a number the reference does not list.</returns>
        public static double Millimetres(DxfUnits units)
        {
            const double SurveyFoot = 1200.0 / 3937.0 * 1000.0;

            switch ((int)units)
            {
                case 1:
                    return 25.4;
                case 2:
                    return 304.8;
                case 3:
                    return 1609344.0;
                case 4:
                    return 1.0;
                case 5:
                    return 10.0;
                case 6:
                    return 1000.0;
                case 7:
                    return 1E6;
                case 8:
                    return 2.54E-5;
                case 9:
                    return 0.0254;
                case 10:
                    return 914.4;
                case 11:
                    return 1E-7;
                case 12:
                    return 1E-6;
                case 13:
                    return 1E-3;
                case 14:
                    return 100.0;
                case 15:
                    return 1E4;
                case 16:
                    return 1E5;
                case 17:
                    return 1E12;
                case 18:
                    return 1.495978707E14;
                case 19:
                    return 9.4607304725808E18;
                case 20:
                    return 3.0856775814913673E19;
                case 21:
                    return SurveyFoot;
                case 22:
                    return SurveyFoot / 12.0;
                case 23:
                    return SurveyFoot * 3.0;
                case 24:
                    return SurveyFoot * 5280.0;
                default:
                    return 0.0;
            }
        }

        /// <summary>
        /// Gets the factor that takes the lengths of model space to millimetres: the millimetres in the drawing's INSUNITS.
        /// </summary>
        /// <param name="database">The drawing.</param>
        /// <param name="normalised">Whether the drawing says its unit: false for INSUNITS 0, whose lengths are then left as
        /// they are (factor 1).</param>
        /// <returns>The factor: 25.4 for a drawing of inches, 1 for one of millimetres or of no unit.</returns>
        /// <exception cref="ArgumentNullException">Thrown when the drawing is null.</exception>
        public static double Of(DxfDatabase database, out bool normalised)
        {
            if (database == null)
            {
                throw new ArgumentNullException(nameof(database));
            }

            double factor = Millimetres(database.Insunits);
            normalised = factor > 0.0;
            return normalised ? factor : 1.0;
        }

        /// <summary>
        /// Gets the factor that takes the lengths of a layout's paper space to millimetres: the plot paper units of the layout,
        /// 25.4 for inches; 1 for millimetres, and for a layout in pixels or with no plot settings, whose paper has no length.
        /// </summary>
        /// <param name="layout">The layout.</param>
        /// <returns>The factor.</returns>
        public static double OfPaper(DxfLayout layout) =>
            layout != null && layout.PlotPaperUnits == DxfPlotPaperUnit.Inches ? 25.4 : 1.0;
    }
}
