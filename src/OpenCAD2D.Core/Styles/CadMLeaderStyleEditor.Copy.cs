using GeometryHelper.Dxf;

namespace OpenCAD2D
{
    public static partial class CadMLeaderStyleEditor
    {
        /// <summary>
        /// Writes a multileader style of settings into a drawing under a name, as the style manager writes one: the scratch a
        /// measure draws a multileader in holds the style the real drawing has (<see cref="CadMeasure.Leader"/>). The text style
        /// the settings name, and the block of a style of block content, are the drawing's to have.
        /// </summary>
        /// <param name="database">The drawing.</param>
        /// <param name="name">The name; a style of that name is not there already.</param>
        /// <param name="settings">The settings.</param>
        /// <returns>The style.</returns>
        internal static DxfMLeaderStyle WriteNew(DxfDatabase database, string name, CadMLeaderStyleSettings settings)
        {
            var style = new DxfMLeaderStyle();
            Dictionary(database).SetAt(name, style);
            Write(database, style, settings);
            return style;
        }
    }
}
