using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using System.Text.RegularExpressions;
using GeometryHelper;

namespace OpenCAD2D.SheetSets
{
    /// <summary>
    /// A path as a sheet set data file keeps it, in the four forms AutoCAD writes so that a set still finds its files after
    /// the folder is moved or opened on another machine: the absolute path, the path relative to the .dst (<c>.\A-101.dwg</c>
    /// for a drawing beside the set, <c>..\Shared\T.dwt</c> for one above it, <c>.</c> for the folder itself), the path
    /// with environment variables (<c>%USERPROFILE%\Sheets</c>) and the path with a special folder
    /// (<c>$(CSIDL_PERSONAL)\Sheets</c>). A form that is absent is null.
    /// </summary>
    /// <remarks>
    /// <see cref="Resolve"/> tries the forms in the order relative, absolute, environment, special folder, and the first
    /// that exists is the file; a set whose folder has been moved therefore keeps working through the relative form. A
    /// reference made by <see cref="FromPath"/> has the absolute and the relative form only: the other two are kept where a
    /// file has them and are never added.
    /// </remarks>
    public sealed class CadFileReference
    {
        private static readonly Regex SpecialFolderToken = new Regex(@"\$\((\w+)\)", RegexOptions.CultureInvariant);

        /// <summary>Makes a reference from its four forms.</summary>
        /// <param name="fileName">The absolute path, or null.</param>
        /// <param name="relativeFileName">The path relative to the .dst, or null.</param>
        /// <param name="environFileName">The path with environment variables, or null.</param>
        /// <param name="specialFolderFileName">The path with a special folder, or null.</param>
        public CadFileReference(
            string fileName,
            string relativeFileName,
            string environFileName,
            string specialFolderFileName
        )
        {
            FileName = string.IsNullOrEmpty(fileName) ? null : fileName;
            RelativeFileName = string.IsNullOrEmpty(relativeFileName) ? null : relativeFileName;
            EnvironFileName = string.IsNullOrEmpty(environFileName) ? null : environFileName;
            SpecialFolderFileName = string.IsNullOrEmpty(specialFolderFileName) ? null : specialFolderFileName;
        }

        /// <summary>Gets the absolute path, or null.</summary>
        public string FileName { get; }

        /// <summary>Gets the path relative to the folder of the .dst (<c>.\A-101.dwg</c>, <c>..\T.dwt</c>, <c>.</c>), or null.</summary>
        public string RelativeFileName { get; }

        /// <summary>Gets the path with environment variables in it (<c>%USERPROFILE%\Sheets</c>), or null.</summary>
        public string EnvironFileName { get; }

        /// <summary>Gets the path with a special folder in it (<c>$(CSIDL_PERSONAL)\Sheets</c>), or null.</summary>
        public string SpecialFolderFileName { get; }

        /// <summary>
        /// Makes the reference the writer makes for a path: the absolute form and the form relative to the .dst. A path on
        /// another drive than the .dst has no relative form.
        /// </summary>
        /// <param name="path">The path of the file or folder.</param>
        /// <param name="setFolder">The folder of the .dst.</param>
        /// <returns>The reference.</returns>
        /// <exception cref="ArgumentNullException">Thrown when the path or the folder is null.</exception>
        /// <exception cref="ArgumentException">Thrown when the path or the folder is empty.</exception>
        public static CadFileReference FromPath(string path, string setFolder)
        {
            if (path == null)
            {
                throw new ArgumentNullException(nameof(path));
            }

            if (setFolder == null)
            {
                throw new ArgumentNullException(nameof(setFolder));
            }

            Guard.Argument(path.Trim().Length > 0, nameof(path), "A file reference needs a path.");
            Guard.Argument(
                setFolder.Trim().Length > 0,
                nameof(setFolder),
                "A file reference needs the folder of the set."
            );
            string full = Path.GetFullPath(path).TrimEnd('\\', '/');
            if (full.Length == 2 && full[1] == ':')
            {
                full += "\\";
            }

            return new CadFileReference(full, RelativeTo(Path.GetFullPath(setFolder), full), null, null);
        }

        /// <summary>
        /// Finds the file or folder the reference names: the relative form against the folder of the .dst, then the
        /// absolute form, then the environment form with its variables expanded, then the special folder form.
        /// </summary>
        /// <param name="setFolder">The folder of the .dst.</param>
        /// <returns>The path of the first form that exists; null where none does, as a sheet whose drawing is gone.</returns>
        /// <exception cref="ArgumentNullException">Thrown when the folder is null.</exception>
        public string Resolve(string setFolder)
        {
            if (setFolder == null)
            {
                throw new ArgumentNullException(nameof(setFolder));
            }

            foreach (string candidate in Candidates(setFolder))
            {
                if (Exists(candidate))
                {
                    return candidate;
                }
            }

            return null;
        }

        private IEnumerable<string> Candidates(string setFolder)
        {
            if (RelativeFileName != null)
            {
                string combined = null;
                try
                {
                    combined = Path.GetFullPath(Path.Combine(setFolder, RelativeFileName));
                }
                catch (Exception e)
                    when (e is ArgumentException || e is NotSupportedException || e is PathTooLongException)
                {
                    // A form that is no path names no file.
                }

                if (combined != null)
                {
                    yield return combined;
                }
            }

            if (FileName != null)
            {
                yield return FileName;
            }

            if (EnvironFileName != null)
            {
                yield return Environment.ExpandEnvironmentVariables(EnvironFileName);
            }

            if (SpecialFolderFileName != null)
            {
                yield return SpecialFolderToken.Replace(SpecialFolderFileName, ExpandSpecialFolder);
            }
        }

        private static bool Exists(string path)
        {
            try
            {
                return File.Exists(path) || Directory.Exists(path);
            }
            catch (Exception e) when (e is ArgumentException || e is NotSupportedException || e is IOException)
            {
                return false;
            }
        }

        private static string ExpandSpecialFolder(Match token)
        {
            switch (token.Groups[1].Value)
            {
                case "CSIDL_PERSONAL":
                    return Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments);
                case "CSIDL_PROGRAM_FILES":
                    return Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles);
                case "CSIDL_PROGRAM_FILESX86":
                    return Environment.GetFolderPath(Environment.SpecialFolder.ProgramFilesX86);
                case "CSIDL_APPDATA":
                    return Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData);
                case "CSIDL_LOCAL_APPDATA":
                    return Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
                case "CSIDL_COMMON_APPDATA":
                    return Environment.GetFolderPath(Environment.SpecialFolder.CommonApplicationData);
                case "CSIDL_DESKTOP":
                case "CSIDL_DESKTOPDIRECTORY":
                    return Environment.GetFolderPath(Environment.SpecialFolder.DesktopDirectory);
                default:
                    return token.Value;
            }
        }

        private static string RelativeTo(string folder, string target)
        {
            folder = folder.TrimEnd('\\', '/');
            if (
                !string.Equals(
                    Path.GetPathRoot(folder + "\\"),
                    Path.GetPathRoot(target + "\\"),
                    StringComparison.OrdinalIgnoreCase
                )
            )
            {
                return null;
            }

            string[] from = folder.Split(new[] { '\\', '/' }, StringSplitOptions.RemoveEmptyEntries);
            string[] to = target.Split(new[] { '\\', '/' }, StringSplitOptions.RemoveEmptyEntries);
            int common = 0;
            while (
                common < from.Length
                && common < to.Length
                && string.Equals(from[common], to[common], StringComparison.OrdinalIgnoreCase)
            )
            {
                common++;
            }

            var relative = new StringBuilder();
            for (int i = common; i < from.Length; i++)
            {
                relative.Append(relative.Length == 0 ? ".." : "\\..");
            }

            for (int i = common; i < to.Length; i++)
            {
                relative.Append(relative.Length == 0 ? string.Empty : "\\").Append(to[i]);
            }

            if (relative.Length == 0)
            {
                return ".";
            }

            return common == from.Length ? ".\\" + relative : relative.ToString();
        }
    }

    /// <summary>
    /// What a sheet set data file keeps of an object inside a drawing: the drawing's file (in the four forms of
    /// <see cref="CadFileReference"/>), the object's name and its handle. A sheet names its layout this way, the sheet
    /// creation template names its layout, the label block names its block and a sheet view names its model space view.
    /// </summary>
    public sealed class CadDrawingReference
    {
        /// <summary>Makes a reference to an object of a drawing.</summary>
        /// <param name="file">The drawing's file.</param>
        /// <param name="name">The object's name (a layout's name, a block's, a view's), or null.</param>
        /// <param name="handle">The object's handle, in hexadecimal, or null.</param>
        /// <param name="ownerHandle">The handle of the object's owner (a layout's block table record), or null.</param>
        /// <exception cref="ArgumentNullException">Thrown when the file is null.</exception>
        public CadDrawingReference(CadFileReference file, string name, string handle, string ownerHandle)
        {
            File = file ?? throw new ArgumentNullException(nameof(file));
            Name = string.IsNullOrEmpty(name) ? null : name;
            Handle = string.IsNullOrEmpty(handle) ? null : handle;
            OwnerHandle = string.IsNullOrEmpty(ownerHandle) ? null : ownerHandle;
        }

        /// <summary>Gets the drawing's file.</summary>
        public CadFileReference File { get; }

        /// <summary>Gets the object's name, or null.</summary>
        public string Name { get; }

        /// <summary>Gets the object's handle in hexadecimal (written in upper case), or null.</summary>
        public string Handle { get; }

        /// <summary>Gets the handle of the object's owner, or null.</summary>
        public string OwnerHandle { get; }
    }
}
