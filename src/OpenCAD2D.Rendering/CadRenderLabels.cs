namespace OpenCAD2D
{
    /// <summary>
    /// The classes <see cref="CadRender.DrawLabels"/> sorts what a picture shows into: a number from 1 to 5 for each pixel
    /// that holds something, 0 for the ground. The numbers are fixed and are the category ids of a COCO file made from the
    /// same picture.
    /// </summary>
    /// <remarks>
    /// A pixel takes the class of the item drawn on top of it, in the order the drawing draws them. An item is a word where it
    /// is a piece of text (the letters of a TEXT, an MTEXT, an attribute, a dimension's text, a leader's) or belongs to a TEXT,
    /// an MTEXT, an ATTRIB or an ATTDEF (an MTEXT's underline); it is a dimension where it belongs to a DIMENSION and is not text
    /// (its lines and arrowheads); it is a hatch where it belongs to a HATCH, a SOLID or a TRACE (its pattern lines and
    /// its fill) or is the frame of a WIPEOUT (its fill blanks what is under it, as it does in the picture, and is the ground, class 0); a block where <see cref="Blocks"/> is asked for and it belongs to an INSERT; and a line otherwise (lines, arcs,
    /// circles, polylines, splines, ellipses, leaders and table lines).
    /// </remarks>
    public static class CadLabelClass
    {
        /// <summary>The class of a pixel nothing is drawn on: 0.</summary>
        public const int Background = 0;

        /// <summary>The class of text: 1.</summary>
        public const int Words = 1;

        /// <summary>The class of the lines and arrowheads of a dimension: 2.</summary>
        public const int Dimensions = 2;

        /// <summary>The class of lines, arcs, circles, polylines, splines and everything else that is drawn as a curve: 3.</summary>
        public const int Lines = 3;

        /// <summary>The class of hatches, solids and traces, and of the frame of a wipeout: 4.</summary>
        public const int Hatches = 4;

        /// <summary>The class of an INSERT's whole drawing, where blocks are asked for: 5.</summary>
        public const int Blocks = 5;

        /// <summary>The most classes, ground included: the number after the last class.</summary>
        public const int Count = 6;

        /// <summary>
        /// Gets the name of a class: the ground is "background", then "words", "dimensions", "lines", "hatches" and "blocks",
        /// the names of the categories of a COCO file.
        /// </summary>
        /// <param name="id">The class, 0 to 5.</param>
        /// <returns>The name.</returns>
        /// <exception cref="System.ArgumentOutOfRangeException">Thrown when the class is not 0 to 5.</exception>
        public static string NameOf(int id)
        {
            switch (id)
            {
                case Background:
                    return "background";
                case Words:
                    return "words";
                case Dimensions:
                    return "dimensions";
                case Lines:
                    return "lines";
                case Hatches:
                    return "hatches";
                case Blocks:
                    return "blocks";
                default:
                    throw new System.ArgumentOutOfRangeException(nameof(id), id, "A class is 0 to 5.");
            }
        }
    }

    /// <summary>
    /// The labels of a picture <see cref="CadRender.DrawLabels"/> made: for each pixel the class of what is drawn on it and the
    /// entity that draws it, exactly, with no antialiasing, as two arrays the size of the picture; the matrix from the drawing
    /// to the pixels; and for each entity its box in pixels, the pixels it holds and the frames of its words. A picture of
    /// 1 600 by 1 200 pixels holds 1 920 000 classes (bytes) and as many instance numbers (4 bytes each).
    /// </summary>
    public sealed class CadRenderLabels
    {
        internal CadRenderLabels(
            int width,
            int height,
            byte[] classes,
            int[] instances,
            string[] handles,
            byte[] categories,
            double[] transform,
            double[] boxes,
            int[] areas,
            int[] frameInstances,
            double[] frameCorners
        )
        {
            Width = width;
            Height = height;
            Classes = classes;
            Instances = instances;
            Handles = handles;
            Categories = categories;
            Transform = transform;
            Boxes = boxes;
            Areas = areas;
            FrameInstances = frameInstances;
            FrameCorners = frameCorners;
        }

        /// <summary>Gets the width of the picture in pixels.</summary>
        public int Width { get; }

        /// <summary>Gets the height of the picture in pixels.</summary>
        public int Height { get; }

        /// <summary>
        /// Gets the class of each pixel, <see cref="Height"/> rows of <see cref="Width"/> pixels from the top: a
        /// <see cref="CadLabelClass"/>, 0 where nothing is drawn or where the class was not asked for.
        /// </summary>
        public byte[] Classes { get; }

        /// <summary>
        /// Gets the instance of each pixel in the order of <see cref="Classes"/>: 0 where nothing is drawn, else k, the entity
        /// <c>Handles[k - 1]</c> that draws the pixel.
        /// </summary>
        public int[] Instances { get; }

        /// <summary>
        /// Gets the handles the instance numbers stand for: the entities of the space in the order its display list first draws
        /// them, those that draw nothing included, then the entities of model space a viewport shows, in the order of the
        /// viewports. An entity keeps its number as long as the drawing is not changed.
        /// </summary>
        public string[] Handles { get; }

        /// <summary>
        /// Gets the category of each entity of <see cref="Handles"/>, a <see cref="CadLabelClass"/> by its kind: words for a TEXT,
        /// an MTEXT, an ATTRIB or an ATTDEF; dimensions for a DIMENSION; hatches for a HATCH, a SOLID, a TRACE or a WIPEOUT (whose frame is all of it that has pixels: its fill blanks); blocks
        /// for an INSERT; lines for the rest.
        /// </summary>
        public byte[] Categories { get; }

        /// <summary>
        /// Gets the matrix <see cref="CadRender.Transform"/> gives for the same options: nine numbers, a 3 by 3 row by row, from
        /// a point of the drawing to its place in the picture in pixels.
        /// </summary>
        public double[] Transform { get; }

        /// <summary>
        /// Gets the box in pixels of each entity of <see cref="Handles"/> that holds a pixel, four numbers each (the least x, the
        /// least y, the greatest x and the greatest y, whole numbers, y down from the top of the picture): the box the drawing
        /// gives it (<see cref="CadSpatial"/>) through the matrix, widened to hold every pixel of the entity, and clipped to the
        /// picture. All four are 0 for an entity with no pixel.
        /// </summary>
        public double[] Boxes { get; }

        /// <summary>Gets the number of pixels each entity of <see cref="Handles"/> holds in <see cref="Instances"/>.</summary>
        public int[] Areas { get; }

        /// <summary>
        /// Gets, for each frame of a word in <see cref="FrameCorners"/>, the number of the instance whose word it is, from 1.
        /// </summary>
        public int[] FrameInstances { get; }

        /// <summary>
        /// Gets the frames of the words of the space, eight numbers each: the four corners (x then y), counter-clockwise from the
        /// foot of the start, in pixels, turned as the text is: a text 20 long and 2.5 high turned 30 degrees is the four corners of
        /// that rectangle on the picture, not the corners of the box round it. A frame is not clipped to the picture.
        /// </summary>
        public double[] FrameCorners { get; }
    }
}
