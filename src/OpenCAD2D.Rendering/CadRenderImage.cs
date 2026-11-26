namespace OpenCAD2D
{
    /// <summary>
    /// The pixels of a picture <see cref="CadRender.Draw"/> made: rows from the top, three bytes a pixel, red, green and
    /// blue, with no alpha. A picture 1 600 by 960 pixels holds 4 608 000 bytes.
    /// </summary>
    public sealed class CadRenderImage
    {
        internal CadRenderImage(int width, int height, byte[] rgb)
        {
            Width = width;
            Height = height;
            Rgb = rgb;
        }

        /// <summary>Gets the width of the picture in pixels.</summary>
        public int Width { get; }

        /// <summary>Gets the height of the picture in pixels.</summary>
        public int Height { get; }

        /// <summary>
        /// Gets the pixels, <see cref="Height"/> rows of <see cref="Width"/> pixels from the top, each a red, a green and a
        /// blue byte: the length is 3 times the width times the height.
        /// </summary>
        public byte[] Rgb { get; }
    }
}
