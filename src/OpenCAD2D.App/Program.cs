using System;
using System.IO;
using System.Windows.Media.Imaging;
using OpenCAD2D.Wpf;

namespace OpenCAD2D.App
{
    /// <summary>
    /// Starts OpenCAD2D, the presenter on its own: the window of <see cref="CadAppHost"/>, with no model drawing.
    /// <c>OpenCAD2D.exe plan.dxf</c> opens the window on a drawing, as a file association does; <c>--screenshot
    /// out.png plan.dxf [--size 1600 960]</c> and the other options of <see cref="CadAppHost"/> draw the window or one of its
    /// dialogs into a PNG, with no window shown, and exit: 0 when written, 2 for arguments or a file it cannot read.
    /// <c>--register</c> and <c>--unregister</c> associate .dxf and .dwg files with the app for the user, or take that out. The
    /// settings and the command plugins' list are kept in <c>%AppData%\OpenCAD2D</c>, which the first start fills from the
    /// folders of GeometryHelper CAD (<see cref="CadOldFolders"/>); the icons are in <c>Icons</c>, written by
    /// <c>make-icons.ps1</c>.
    /// </summary>
    public static class Program
    {
        /// <summary>The entry point.</summary>
        /// <param name="args">The command-line arguments.</param>
        /// <returns>The exit code.</returns>
        [STAThread]
        public static int Main(string[] args)
        {
            // The code pages of the big fonts (932, 936, 949, 950) and of old DXF files, which .NET (Core) reads only once registered.
            System.Text.Encoding.RegisterProvider(System.Text.CodePagesEncodingProvider.Instance);

            string appData = Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData);
            string folder = Path.Combine(appData, "OpenCAD2D");

            // The first start of OpenCAD2D takes the settings of GeometryHelper CAD, as the app was called before.
            CadOldFolders.CopyOnce(folder, CadOldFolders.Default);

            var host = new CadAppHost("OpenCAD2D", folder) { EnablePython = true };

            // The window's icon, every size of app.ico, the taskbar taking the one it needs.
            using (Stream icon = typeof(Program).Assembly.GetManifestResourceStream("OpenCAD2D.App.app.ico"))
            {
                host.WindowIcon = BitmapFrame.Create(icon, BitmapCreateOptions.None, BitmapCacheOption.OnLoad);
            }

            return host.Run(args);
        }
    }
}
