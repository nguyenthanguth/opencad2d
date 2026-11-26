namespace OpenCAD2D
{
    /// <summary>
    /// What a host that has a window can also tell a program that debugs the app: the picture of its canvas and the file its
    /// crash log is written to. A <see cref="CadRemoteServer"/> asks for it only when the host implements it, so a host with no
    /// window (a test, a plugin's own) leaves it out and the requests are refused in words.
    /// </summary>
    public interface ICadRemoteDiagnostics
    {
        /// <summary>Gets the file the app writes the exceptions nobody caught to; null where it writes none.</summary>
        string CrashLogPath { get; }

        /// <summary>
        /// Gets the picture of the drawing canvas as the window last drew it, without showing anything. Called on the thread of the
        /// dispatcher the server was given.
        /// </summary>
        /// <param name="maxSide">The most pixels along a side; a picture larger is made smaller, keeping its shape; 0 leaves it as it is.</param>
        /// <returns>The picture; null where the window shows no drawing canvas (the start screen).</returns>
        CadRemotePicture CanvasPicture(int maxSide);
    }

    /// <summary>The picture of a canvas: a PNG of 8-bit red, green and blue and its size in pixels.</summary>
    public sealed class CadRemotePicture
    {
        /// <summary>Makes a picture.</summary>
        /// <param name="png">The PNG file's bytes.</param>
        /// <param name="width">The width in pixels.</param>
        /// <param name="height">The height in pixels.</param>
        /// <exception cref="System.ArgumentNullException">Thrown when the PNG is null.</exception>
        public CadRemotePicture(byte[] png, int width, int height)
        {
            Png = png ?? throw new System.ArgumentNullException(nameof(png));
            Width = width;
            Height = height;
        }

        /// <summary>Gets the PNG file's bytes.</summary>
        public byte[] Png { get; }

        /// <summary>Gets the width in pixels.</summary>
        public int Width { get; }

        /// <summary>Gets the height in pixels.</summary>
        public int Height { get; }
    }
}
