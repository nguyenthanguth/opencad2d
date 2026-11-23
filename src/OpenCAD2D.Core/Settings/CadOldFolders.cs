using System;
using System.IO;

namespace OpenCAD2D
{
    /// <summary>
    /// The folders the app kept the user's files in while it was GeometryHelper CAD, copied into the folder of OpenCAD2D at
    /// its first start: <c>%AppData%\GeometryHelper\Drawing</c> (settings.json, crash.log, PlotStyles, Template,
    /// ToolPalettes.json, sample.cus) and <c>%AppData%\GeometryHelper\Dxf</c> (plugins.json, filter.nfl), both into
    /// <c>%AppData%\OpenCAD2D</c>. The old folders are left as they are, so an older build still finds its files.
    /// </summary>
    public static class CadOldFolders
    {
        /// <summary>Gets the folders of GeometryHelper CAD under %AppData%, in the order they are copied.</summary>
        public static string[] Default
        {
            get
            {
                string appData = Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData);
                return new[]
                {
                    Path.Combine(appData, "GeometryHelper", "Drawing"),
                    Path.Combine(appData, "GeometryHelper", "Dxf"),
                };
            }
        }

        /// <summary>
        /// Copies the old folders into the new one once: only while the new folder does not exist yet, so that a user who
        /// has started OpenCAD2D before is never given the old files again. A file in two old folders keeps the copy of the
        /// first; a file that cannot be read is left out, and the copy goes on.
        /// </summary>
        /// <param name="newFolder">The folder of OpenCAD2D, <c>%AppData%\OpenCAD2D</c> for the user's.</param>
        /// <param name="oldFolders">The old folders, <see cref="Default"/> for the user's.</param>
        /// <returns>The number of files copied; 0 where the new folder was there already or no old folder was.</returns>
        public static int CopyOnce(string newFolder, params string[] oldFolders)
        {
            if (newFolder == null)
            {
                throw new ArgumentNullException(nameof(newFolder));
            }

            if (oldFolders == null)
            {
                throw new ArgumentNullException(nameof(oldFolders));
            }

            if (Directory.Exists(newFolder))
            {
                return 0;
            }

            int copied = 0;

            foreach (string old in oldFolders)
            {
                if (old != null && Directory.Exists(old))
                {
                    copied += Copy(old, newFolder);
                }
            }

            return copied;
        }

        private static int Copy(string from, string to)
        {
            int copied = 0;
            Directory.CreateDirectory(to);

            foreach (string file in Directory.GetFiles(from))
            {
                string target = Path.Combine(to, Path.GetFileName(file));

                if (!File.Exists(target))
                {
                    try
                    {
                        File.Copy(file, target);
                        copied++;
                    }
                    catch (IOException) { }
                    catch (UnauthorizedAccessException) { }
                }
            }

            foreach (string folder in Directory.GetDirectories(from))
            {
                copied += Copy(folder, Path.Combine(to, Path.GetFileName(folder)));
            }

            return copied;
        }
    }
}
