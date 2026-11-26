using System;
using System.Collections.Generic;
using GeometryHelper.Dxf;

namespace OpenCAD2D
{
    public static partial class CadDimStyleEditor
    {
        /// <summary>
        /// Makes the sample of one tab of the dimension style dialogs, with the parts of its dimensions told apart: a drawing of
        /// its own holding the style's variables (the text style copied by its name and font, the arrowheads' blocks made, the
        /// linetypes of the dimension and extension lines copied), and in its model space the entities of the sample's dimensions,
        /// each its own, drawn by the builders the DIM commands use (see <see cref="CadDimSampleKind"/> for what each sample holds).
        /// The sample is as many times bigger as the style's overall scale (DIMSCALE) says, nought taken for 1. The
        /// <see cref="CadDimSampleKind.Overview"/> sample draws what <see cref="Samples"/> draws, item for item.
        /// </summary>
        /// <param name="source">The drawing the style's text styles and linetypes are taken from.</param>
        /// <param name="values">The style's variables by name.</param>
        /// <param name="kind">The sample.</param>
        /// <returns>The sample.</returns>
        /// <exception cref="ArgumentNullException">Thrown when the drawing or the values are null.</exception>
        public static CadStyleSample Sample(
            DxfDatabase source,
            IDictionary<string, object> values,
            CadDimSampleKind kind
        )
        {
            if (values == null)
            {
                throw new ArgumentNullException(nameof(values));
            }

            DxfDatabase database = PreviewDrawing(source, values, true);
            return CadCommands.StyleSample(database, database.DimStyleTable["Preview"], kind);
        }
    }
}
