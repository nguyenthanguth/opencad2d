using System;
using GeometryHelper;

namespace OpenCAD2D
{
    /// <summary>
    /// A rectangle that may be turned: what a word takes on the page (<see cref="CadMeasurement.Frames"/>), and what a program
    /// asks whether it fits (<see cref="CadSpatial.Fits"/>). It is given by its first corner, the one at the foot of the start of
    /// a line of words, its width along the line, its height across it and the angle the line runs at. A word 20 long and 5
    /// high set at (10, 30) and turned 90 degrees has its first corner at (10, 30), its second at (10, 50) and its far corner at
    /// (5, 50).
    /// </summary>
    public sealed class CadFrame
    {
        /// <summary>Makes a frame.</summary>
        /// <param name="x">The X of the first corner.</param>
        /// <param name="y">The Y of the first corner.</param>
        /// <param name="width">The width, along the angle, above nought.</param>
        /// <param name="height">The height, across it, above nought.</param>
        /// <param name="rotation">The angle the width runs at, counter-clockwise from the X axis, in radians.</param>
        /// <exception cref="ArgumentException">Thrown when a number is not finite or the width or the height is not above nought.</exception>
        public CadFrame(double x, double y, double width, double height, double rotation)
        {
            Guard.Argument(
                Guard.IsFinite(x) && Guard.IsFinite(y) && Guard.IsFinite(rotation),
                nameof(x),
                "A frame is placed at a finite point and angle."
            );
            Guard.Argument(
                Guard.IsFinite(width) && Guard.IsFinite(height) && width > 0.0 && height > 0.0,
                nameof(width),
                "A frame has a width and a height above nought."
            );
            X = x;
            Y = y;
            Width = width;
            Height = height;
            Rotation = rotation;
        }

        /// <summary>Gets the X of the first corner.</summary>
        public double X { get; }

        /// <summary>Gets the Y of the first corner.</summary>
        public double Y { get; }

        /// <summary>Gets the width, along <see cref="Rotation"/>.</summary>
        public double Width { get; }

        /// <summary>Gets the height, across it.</summary>
        public double Height { get; }

        /// <summary>Gets the angle the width runs at, in radians.</summary>
        public double Rotation { get; }

        /// <summary>Gets the box of the frame in the drawing: its four corners' least and greatest.</summary>
        public DisplayBox Box => ToTextFrame().Box();

        /// <summary>Makes the frame of a box that is not turned.</summary>
        /// <param name="box">The box, not empty and above nought each way.</param>
        /// <returns>The frame.</returns>
        /// <exception cref="ArgumentException">Thrown when the box is empty or has no width or no height.</exception>
        public static CadFrame FromBox(DisplayBox box) =>
            new CadFrame(box.MinX, box.MinY, box.MaxX - box.MinX, box.MaxY - box.MinY, 0.0);

        internal static CadFrame Of(in CadTextFrame frame)
        {
            frame.Corner(0, 0.0, out double x, out double y);
            return new CadFrame(x, y, frame.X1 - frame.X0, frame.Y1 - frame.Y0, Math.Atan2(frame.Sin, frame.Cos));
        }

        internal CadTextFrame ToTextFrame() =>
            new CadTextFrame
            {
                X = X,
                Y = Y,
                Cos = Math.Cos(Rotation),
                Sin = Math.Sin(Rotation),
                X0 = 0.0,
                Y0 = 0.0,
                X1 = Width,
                Y1 = Height,
            };
    }
}
