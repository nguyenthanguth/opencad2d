using System;
using System.IO;

namespace OpenCAD2D.Wpf
{
    /// <summary>
    /// What names a Python tool on disk: a folder with a <c>requirements.txt</c>, given as the folder or as that file. A <c>.py</c>
    /// file alone is not a tool (PYRUN still runs one once), and a folder without <c>requirements.txt</c> is a package the tools
    /// import.
    /// </summary>
    internal static class PythonToolFolder
    {
        /// <summary>The file that makes a folder a Python tool.</summary>
        public const string RequirementsFile = "requirements.txt";

        /// <summary>The folder a path names as a tool: the folder itself where it holds a <c>requirements.txt</c>, or the folder of a file of that name; null for any other path.</summary>
        /// <param name="path">A path, as written.</param>
        /// <returns>The full path of the folder, or null.</returns>
        public static string Resolve(string path)
        {
            if (string.IsNullOrWhiteSpace(path))
            {
                return null;
            }

            try
            {
                string full = Path.GetFullPath(path.Trim().Trim('"')).TrimEnd('\\', '/');

                if (Directory.Exists(full))
                {
                    return File.Exists(Path.Combine(full, RequirementsFile)) ? full : null;
                }

                return
                    string.Equals(Path.GetFileName(full), RequirementsFile, StringComparison.OrdinalIgnoreCase)
                    && File.Exists(full)
                    ? Path.GetDirectoryName(full)
                    : null;
            }
            catch (Exception exception)
                when (exception is ArgumentException
                    || exception is NotSupportedException
                    || exception is PathTooLongException
                    || exception is IOException
                )
            {
                return null;
            }
        }

        /// <summary>Whether a path is a <c>.py</c> file, a lone Python file that is no tool.</summary>
        /// <param name="path">A path.</param>
        /// <returns>True for a path that ends in <c>.py</c>, whatever the case.</returns>
        public static bool IsLoneFile(string path) =>
            !string.IsNullOrEmpty(path)
            && string.Equals(Path.GetExtension(path.Trim().Trim('"')), ".py", StringComparison.OrdinalIgnoreCase);

        /// <summary>The line that refuses a lone <c>.py</c> file where a tool is wanted, and says how to make it one.</summary>
        /// <param name="path">The file.</param>
        /// <returns>For <c>D:\x\GH-DrawingInfo.py</c>: <c>GH-DrawingInfo.py is a lone Python file: a Python tool is a folder with requirements.txt. Put it in Pythons\GH-DrawingInfo\ as __init__.py (see the Python guide).</c></returns>
        public static string LoneFileLine(string path)
        {
            string name = Path.GetFileName(path.Trim().Trim('"'));
            string stem = Path.GetFileNameWithoutExtension(name);
            return name
                + " is a lone Python file: a Python tool is a folder with requirements.txt. Put it in Pythons\\"
                + stem
                + "\\ as __init__.py (see the Python guide).";
        }
    }
}
