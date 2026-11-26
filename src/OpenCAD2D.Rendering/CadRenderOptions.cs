namespace OpenCAD2D
{
    /// <summary>
    /// How <see cref="CadRender"/> draws a space with no window: which space, what part of it, how large and on what
    /// ground. Every property has a default that gives the picture of ZOOM Extents on a canvas of 1 600 pixels along its
    /// longer side, on the canvas's black.
    /// </summary>
    public sealed class CadRenderOptions
    {
        /// <summary>
        /// Gets or sets the layout drawn: its name as its tab shows it, letters of either case the same, or "Model" for model
        /// space; null, the default, for the space the drawing was left in.
        /// </summary>
        public string Layout { get; set; }

        /// <summary>
        /// Gets or sets the part of the space drawn, in its own units (model units, or millimetres of paper on a layout),
        /// fitted to the picture with no margin and centred; empty, the default, for the extents of the space with the 5 per
        /// cent margin ZOOM Extents leaves, a layout's sheet of paper among them.
        /// </summary>
        public DisplayBox Box { get; set; } = DisplayBox.Empty;

        /// <summary>
        /// Gets or sets the width of the picture in pixels. Nought, the default, takes it from <see cref="Height"/> where
        /// that is given, and else from <see cref="Dpi"/> on a layout or 1 600 pixels along the longer side in model space.
        /// </summary>
        public int Width { get; set; }

        /// <summary>
        /// Gets or sets the height of the picture in pixels. Nought, the default, takes it from <see cref="Width"/> where
        /// that is given, and else as <see cref="Width"/> does.
        /// </summary>
        public int Height { get; set; }

        /// <summary>
        /// Gets or sets the pixels to an inch of paper, 96 by default, for a layout drawn at no size of its own: a sheet of
        /// A3, 420 by 297 mm, is 1 587 by 1 123 pixels at 96 and 4 961 by 3 508 at 300 (a layout whose paper units
        /// are inches, 16.54 by 11.69, the same). It has no effect in model space, nor where <see cref="Width"/> or
        /// <see cref="Height"/> is given; the widths of lines stay the canvas's, in pixels.
        /// </summary>
        public double Dpi { get; set; } = 96.0;

        /// <summary>
        /// Gets or sets the colour of model space's ground as 0xRRGGBB: 0, the default, is the black of the canvas; 0xFFFFFF
        /// is white, on which the colour 7 is drawn black. The paper of a layout is white whatever this is.
        /// </summary>
        public int Background { get; set; }
    }
}
